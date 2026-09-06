using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Orders already-generated rail pieces by REAL attachment frontier instead of only radial distance.
///
/// The preserved 4x4 Base is occupied from the beginning. A piece may enter only when at least one
/// of its cells touches the currently occupied floor. After that piece is scheduled, its cells become
/// occupied and unlock the next outer pieces. This makes the stage visibly grow from the existing map.
/// </summary>
[DefaultExecutionOrder(-18000)]
public sealed class BattleAssemblyFrontierPlanner : MonoBehaviour
{
    [Header("Frontier Assembly Timing")]
    [SerializeField, Min(0.01f)] private float pieceEntryStagger = 0.075f;
    [SerializeField, Min(0.1f)] private float maximumAssemblyStagger = 2.2f;

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo BlockEntryStaggerField =
        typeof(BattleRoomManager).GetField("blockEntryStagger", PrivateInstance);
    private static readonly FieldInfo MaxBlockEntryStaggerField =
        typeof(BattleRoomManager).GetField("maxBlockEntryStagger", PrivateInstance);

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private bool subscribed;

    private static readonly Vector2Int[] Cardinal =
    {
        Vector2Int.right,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.down
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleAssemblyFrontierPlanner>() != null)
            return;

        GameObject host = new("BattleAssemblyFrontierPlannerRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleAssemblyFrontierPlanner>();
    }

    private void OnEnable()
    {
        ResolveSystems();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (runManager == null || roomManager == null)
        {
            ResolveSystems();
            Subscribe();
        }
    }

    private void ResolveSystems()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
    }

    private void Subscribe()
    {
        if (subscribed || runManager == null)
            return;

        runManager.NodeEntered += HandleNodeEntered;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || runManager == null)
            return;

        runManager.NodeEntered -= HandleNodeEntered;
        subscribed = false;
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node == null || node.room == null)
            return;
        if (node.type != BattleNodeType.Combat && node.type != BattleNodeType.Elite)
            return;

        ApplyVisibleAssemblyTiming();
        ReorderByAttachmentFrontier(node.room);
    }

    private void ApplyVisibleAssemblyTiming()
    {
        if (roomManager == null)
            return;

        BlockEntryStaggerField?.SetValue(roomManager, Mathf.Max(0.01f, pieceEntryStagger));
        MaxBlockEntryStaggerField?.SetValue(roomManager, Mathf.Max(pieceEntryStagger, maximumAssemblyStagger));
    }

    private static void ReorderByAttachmentFrontier(RoomDefinitionSO room)
    {
        if (room == null || room.blocks == null || room.blocks.Count <= 1)
            return;

        List<PieceInfo> remaining = new();
        for (int i = 0; i < room.blocks.Count; i++)
        {
            MapBlockPlacement placement = room.blocks[i];
            if (placement == null || placement.prefab == null)
                continue;

            HashSet<Vector2Int> cells = ReadPieceCells(placement.prefab);
            if (cells.Count == 0)
                continue;

            // The preserved 4x4 itself is never an incoming piece.
            RemoveBaseCells(cells);
            if (cells.Count == 0)
                continue;

            remaining.Add(new PieceInfo(placement, cells, i));
        }

        if (remaining.Count == 0)
            return;

        HashSet<Vector2Int> occupied = CreateBaseCells();
        List<MapBlockPlacement> ordered = new(remaining.Count);
        int safety = 0;

        while (remaining.Count > 0 && safety++ < 1024)
        {
            int bestIndex = -1;
            int bestContacts = -1;
            float bestDistance = float.MaxValue;
            Vector2 bestOutward = Vector2.zero;

            for (int i = 0; i < remaining.Count; i++)
            {
                PieceInfo piece = remaining[i];
                int contacts = CountContactsAndDirection(piece.cells, occupied, out Vector2 outward);
                if (contacts <= 0)
                    continue;

                float distance = DistanceToBase(piece.cells);
                if (contacts > bestContacts ||
                    (contacts == bestContacts && distance < bestDistance) ||
                    (contacts == bestContacts && Mathf.Approximately(distance, bestDistance) && piece.originalIndex < remaining[bestIndex].originalIndex))
                {
                    bestIndex = i;
                    bestContacts = contacts;
                    bestDistance = distance;
                    bestOutward = outward;
                }
            }

            if (bestIndex < 0)
            {
                // This should not happen for a correctly connected target Room. Keep deterministic fallback,
                // but make the problem visible instead of pretending the piece was connected.
                bestIndex = FindNearestPieceToOccupied(remaining, occupied);
                if (bestIndex < 0)
                    break;

                PieceInfo fallback = remaining[bestIndex];
                bestOutward = RadialOutward(fallback.cells, fallback.originalIndex);
                Debug.LogWarning(
                    $"[BattleAssemblyFrontier] Piece '{fallback.placement.prefab.name}' is disconnected from the current occupied frontier. " +
                    "It will use a radial fallback; check the generated Room shape/connectivity.");
            }

            PieceInfo selected = remaining[bestIndex];
            selected.placement.entryDirection = bestOutward.sqrMagnitude > 0.001f
                ? bestOutward.normalized
                : RadialOutward(selected.cells, selected.originalIndex);

            ordered.Add(selected.placement);
            foreach (Vector2Int cell in selected.cells)
                occupied.Add(cell);

            remaining.RemoveAt(bestIndex);
        }

        // Preserve any non-runtime/unknown placements after the frontier pieces.
        if (ordered.Count > 0)
        {
            HashSet<MapBlockPlacement> included = new(ordered);
            for (int i = 0; i < room.blocks.Count; i++)
            {
                MapBlockPlacement placement = room.blocks[i];
                if (placement != null && !included.Contains(placement))
                    ordered.Add(placement);
            }

            room.blocks = ordered;
        }
    }

    private static HashSet<Vector2Int> ReadPieceCells(MapBlock block)
    {
        HashSet<Vector2Int> cells = new();
        if (block == null)
            return cells;

        Transform[] children = block.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == null || !child.name.StartsWith("Tile_", StringComparison.Ordinal))
                continue;

            cells.Add(new Vector2Int(
                Mathf.RoundToInt(child.localPosition.x),
                Mathf.RoundToInt(child.localPosition.y)));
        }

        return cells;
    }

    private static HashSet<Vector2Int> CreateBaseCells()
    {
        HashSet<Vector2Int> result = new();
        for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
        {
            for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                result.Add(new Vector2Int(x, y));
        }
        return result;
    }

    private static void RemoveBaseCells(HashSet<Vector2Int> cells)
    {
        for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
        {
            for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                cells.Remove(new Vector2Int(x, y));
        }
    }

    private static int CountContactsAndDirection(
        HashSet<Vector2Int> piece,
        HashSet<Vector2Int> occupied,
        out Vector2 outward)
    {
        outward = Vector2.zero;
        int contacts = 0;

        foreach (Vector2Int cell in piece)
        {
            for (int d = 0; d < Cardinal.Length; d++)
            {
                Vector2Int towardOccupied = Cardinal[d];
                if (!occupied.Contains(cell + towardOccupied))
                    continue;

                contacts++;
                // If occupied is to the left of this piece, the piece itself is on the right,
                // so it must start farther right and slide left into the contact.
                outward += -(Vector2)towardOccupied;
            }
        }

        if (outward.sqrMagnitude > 0.001f)
            outward.Normalize();
        return contacts;
    }

    private static float DistanceToBase(HashSet<Vector2Int> cells)
    {
        Vector2 center = CalculateCenter(cells);
        return (center - new Vector2(1.5f, 1.5f)).sqrMagnitude;
    }

    private static int FindNearestPieceToOccupied(List<PieceInfo> pieces, HashSet<Vector2Int> occupied)
    {
        int bestIndex = -1;
        int bestManhattan = int.MaxValue;

        for (int i = 0; i < pieces.Count; i++)
        {
            int localBest = int.MaxValue;
            foreach (Vector2Int cell in pieces[i].cells)
            {
                foreach (Vector2Int existing in occupied)
                {
                    int distance = Mathf.Abs(cell.x - existing.x) + Mathf.Abs(cell.y - existing.y);
                    if (distance < localBest)
                        localBest = distance;
                }
            }

            if (localBest < bestManhattan)
            {
                bestManhattan = localBest;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static Vector2 RadialOutward(HashSet<Vector2Int> cells, int fallbackIndex)
    {
        Vector2 delta = CalculateCenter(cells) - new Vector2(1.5f, 1.5f);
        if (delta.sqrMagnitude < 0.001f)
        {
            Vector2[] fallback = { Vector2.left, Vector2.right, Vector2.down, Vector2.up };
            return fallback[Mathf.Abs(fallbackIndex) % fallback.Length];
        }

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            return delta.x >= 0f ? Vector2.right : Vector2.left;
        return delta.y >= 0f ? Vector2.up : Vector2.down;
    }

    private static Vector2 CalculateCenter(HashSet<Vector2Int> cells)
    {
        if (cells == null || cells.Count == 0)
            return Vector2.zero;

        Vector2 sum = Vector2.zero;
        foreach (Vector2Int cell in cells)
            sum += (Vector2)cell;
        return sum / cells.Count;
    }

    private sealed class PieceInfo
    {
        public readonly MapBlockPlacement placement;
        public readonly HashSet<Vector2Int> cells;
        public readonly int originalIndex;

        public PieceInfo(MapBlockPlacement placement, HashSet<Vector2Int> cells, int originalIndex)
        {
            this.placement = placement;
            this.cells = cells;
            this.originalIndex = originalIndex;
        }
    }
}
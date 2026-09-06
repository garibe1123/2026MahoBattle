using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using NavMeshPlus.Components;
using UnityEngine;

/// <summary>
/// Presentation correction layer for procedural stage assembly.
///
/// Rules enforced here:
/// - The persistent 4x4 Start Base is the real center anchor and is never recreated as an incoming piece.
/// - Room topology rules (10x10+ room, 2-tile+ passages) are independent from presentation-piece size.
/// - Incoming presentation pieces may be as small as 1x1 or 2x2 tiles.
/// - Every incoming piece approaches from the outside toward the persistent-base center.
/// - On clear, pieces leave on the same straight rail axis. No spinning / tumbling exit is used.
///
/// This component intentionally sits between BattleSpatialMapController (-20000) and
/// BattleStageTransitionController (-15000), so it can rewrite the temporary runtime room
/// prototypes after the procedural layout is prepared but before BattleRoomManager instantiates them.
/// </summary>
[DefaultExecutionOrder(-19000)]
public sealed class BattleAssemblyRailPresentation : MonoBehaviour
{
    [Header("Incoming Piece Granularity")]
    [SerializeField, Min(1)] private int minimumPieceTileCount = 1;
    [SerializeField, Min(1)] private int maximumPieceTileSpan = 4;
    [SerializeField, Range(8, 64)] private int maximumPieceCount = 36;

    [Header("Rail Exit")]
    [SerializeField] private Ease railExitEase = Ease.InCubic;
    [SerializeField] private bool disableLegacyExitSpin = true;

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo ActiveBlocksField =
        typeof(BattleRoomManager).GetField("activeBlocks", PrivateInstance);

    private static readonly FieldInfo TransitionSpinField =
        typeof(BattleStageTransitionController).GetField("exitGhostSpinDegrees", PrivateInstance);

    private static readonly FieldInfo TransitionScaleField =
        typeof(BattleStageTransitionController).GetField("exitGhostEndScale", PrivateInstance);

    private static readonly FieldInfo MapBlockExitEaseField =
        typeof(MapBlock).GetField("exitEase", PrivateInstance);

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private BattleSpatialMapController spatialMap;
    private RoomBaseTemplate baseTemplate;
    private BattleStageTransitionController stageTransition;
    private bool subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleAssemblyRailPresentation>() != null)
            return;

        GameObject host = new("BattleAssemblyRailPresentationRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleAssemblyRailPresentation>();
    }

    private void OnEnable()
    {
        StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private IEnumerator BindWhenReady()
    {
        while (runManager == null || spatialMap == null || roomManager == null || baseTemplate == null)
        {
            ResolveSystems();
            yield return null;
        }

        Subscribe();
        ApplyRailExitPresentationSettings();
    }

    private void Update()
    {
        ResolveSystems();
        ApplyRailExitPresentationSettings();
    }

    private void ResolveSystems()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (spatialMap == null)
            spatialMap = FindFirstObjectByType<BattleSpatialMapController>();
        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (stageTransition == null)
            stageTransition = FindFirstObjectByType<BattleStageTransitionController>();
    }

    private void Subscribe()
    {
        if (subscribed || runManager == null)
            return;

        runManager.NodeEntered += HandleNodeEntered;
        runManager.RewardSelectionRequested += HandleRewardSelectionRequested;
        runManager.RunEnded += HandleRunEnded;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || runManager == null)
            return;

        runManager.NodeEntered -= HandleNodeEntered;
        runManager.RewardSelectionRequested -= HandleRewardSelectionRequested;
        runManager.RunEnded -= HandleRunEnded;
        subscribed = false;
    }

    private void ApplyRailExitPresentationSettings()
    {
        if (!disableLegacyExitSpin || stageTransition == null)
            return;

        // MinAttribute is editor-only validation. Runtime zero intentionally means "no spin".
        TransitionSpinField?.SetValue(stageTransition, 0f);
        TransitionScaleField?.SetValue(stageTransition, 1f);
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node == null || node.room == null)
            return;
        if (node.type != BattleNodeType.Combat && node.type != BattleNodeType.Elite)
            return;

        RewriteRuntimeRoomPieces(node);
    }

    /// <summary>
    /// BattleSpatialMapController has already placed temporary runtime prototypes in room.blocks at this point.
    /// We rebuild those temporary prototypes into smaller center-anchored presentation chunks.
    /// </summary>
    private void RewriteRuntimeRoomPieces(BattleNodeData node)
    {
        RoomDefinitionSO room = node.room;
        if (room.blocks == null || room.blocks.Count == 0)
            return;

        Dictionary<Vector2Int, GameObject> sourceTiles = new();
        HashSet<Vector2Int> fullCells = new();
        WallStyle wallStyle = default;
        bool hasWallStyle = false;
        List<GameObject> oldRuntimePrototypes = new();

        for (int i = 0; i < room.blocks.Count; i++)
        {
            MapBlockPlacement placement = room.blocks[i];
            if (placement == null || placement.prefab == null)
                continue;

            GameObject prototypeRoot = placement.prefab.gameObject;
            if (!prototypeRoot.name.StartsWith("__RuntimeRoomPiecePrototype_", StringComparison.Ordinal))
                continue;

            oldRuntimePrototypes.Add(prototypeRoot);

            Transform[] children = prototypeRoot.GetComponentsInChildren<Transform>(true);
            for (int c = 0; c < children.Length; c++)
            {
                Transform child = children[c];
                if (child == null)
                    continue;

                if (child.name.StartsWith("Tile_", StringComparison.Ordinal))
                {
                    Vector2Int cell = new(
                        Mathf.RoundToInt(child.localPosition.x),
                        Mathf.RoundToInt(child.localPosition.y));

                    fullCells.Add(cell);
                    if (!sourceTiles.ContainsKey(cell))
                        sourceTiles[cell] = child.gameObject;
                }
                else if (!hasWallStyle && child.name.StartsWith("RoomEdge", StringComparison.Ordinal))
                {
                    SpriteRenderer wallRenderer = child.GetComponent<SpriteRenderer>();
                    BoxCollider2D wallCollider = child.GetComponent<BoxCollider2D>();
                    if (wallRenderer != null)
                    {
                        wallStyle = WallStyle.From(wallRenderer, wallCollider);
                        hasWallStyle = true;
                    }
                }
            }
        }

        if (fullCells.Count == 0 || sourceTiles.Count == 0)
            return;

        GetBounds(fullCells, out int minX, out int minY, out int maxX, out int maxY);
        int width = maxX - minX + 1;
        int height = maxY - minY + 1;

        int baseStartX = minX + Mathf.Max(0, (width - RoomBaseTemplate.FixedBaseTiles) / 2);
        int baseStartY = minY + Mathf.Max(0, (height - RoomBaseTemplate.FixedBaseTiles) / 2);
        int baseEndX = baseStartX + RoomBaseTemplate.FixedBaseTiles - 1;
        int baseEndY = baseStartY + RoomBaseTemplate.FixedBaseTiles - 1;

        // The persistent Start Base is the center itself. Do not generate duplicate incoming tiles there.
        HashSet<Vector2Int> incomingCells = new(fullCells);
        for (int y = baseStartY; y <= baseEndY; y++)
        {
            for (int x = baseStartX; x <= baseEndX; x++)
                incomingCells.Remove(new Vector2Int(x, y));
        }

        if (incomingCells.Count == 0)
            return;

        Vector2 roomCenter = new(
            (minX + maxX) * 0.5f,
            (minY + maxY) * 0.5f);

        int seed = StableHash(node.id) ^ StableHash(room.roomId) ^ room.proceduralSeed;
        List<HashSet<Vector2Int>> groups = BuildGranularGroups(incomingCells, roomCenter, seed);
        if (groups.Count == 0)
            return;

        if (!hasWallStyle)
            wallStyle = WallStyle.Fallback(sourceTiles);

        List<MapBlockPlacement> rewrittenPlacements = new(groups.Count);
        List<GameObject> newPrototypes = new(groups.Count);

        for (int i = 0; i < groups.Count; i++)
        {
            HashSet<Vector2Int> group = groups[i];
            if (group == null || group.Count == 0)
                continue;

            MapBlock block = CreateGranularPrototype(
                node,
                group,
                fullCells,
                sourceTiles,
                wallStyle,
                i);

            if (block == null)
                continue;

            Vector2 pieceCenter = CalculateCenter(group);
            Vector2 outward = GetDominantOutwardDirection(pieceCenter - roomCenter, i);

            rewrittenPlacements.Add(new MapBlockPlacement
            {
                prefab = block,
                gridPosition = Vector2Int.zero,
                // MapBlock.PlayEnter starts at destination + entryDirection * offset,
                // so outward direction means the piece travels inward toward the persistent base.
                entryDirection = outward
            });
            newPrototypes.Add(block.gameObject);
        }

        if (rewrittenPlacements.Count == 0)
            return;

        room.blocks = rewrittenPlacements;
        StartCoroutine(DestroyOurPrototypesNextFrame(newPrototypes));

        // Old prototypes belong to BattleSpatialMapController and are already scheduled for destruction.
        // Do not destroy them here; its restore coroutine owns their lifetime.
    }

    private List<HashSet<Vector2Int>> BuildGranularGroups(
        HashSet<Vector2Int> cells,
        Vector2 roomCenter,
        int seed)
    {
        List<HashSet<Vector2Int>> groups = new();
        if (cells == null || cells.Count == 0)
            return groups;

        HashSet<Vector2Int> remaining = new(cells);
        List<Vector2Int> ordered = new(cells);
        ordered.Sort((a, b) =>
        {
            float da = ((Vector2)a - roomCenter).sqrMagnitude;
            float db = ((Vector2)b - roomCenter).sqrMagnitude;
            int cmp = da.CompareTo(db); // center-near pieces dock first.
            if (cmp != 0) return cmp;
            cmp = a.y.CompareTo(b.y);
            return cmp != 0 ? cmp : a.x.CompareTo(b.x);
        });

        System.Random random = new(seed);
        int cursor = 0;

        while (remaining.Count > 0 && groups.Count < maximumPieceCount)
        {
            while (cursor < ordered.Count && !remaining.Contains(ordered[cursor]))
                cursor++;

            Vector2Int start;
            if (cursor < ordered.Count)
            {
                start = ordered[cursor];
            }
            else
            {
                start = default;
                foreach (Vector2Int cell in remaining)
                {
                    start = cell;
                    break;
                }
            }

            int targetCount = ChooseTargetPieceCellCount(random);
            HashSet<Vector2Int> group = GrowConnectedGroup(start, remaining, roomCenter, targetCount);
            if (group.Count == 0)
            {
                remaining.Remove(start);
                continue;
            }

            groups.Add(group);
        }

        // If the safety cap is reached, put any remaining cells into nearby final groups.
        if (remaining.Count > 0)
        {
            foreach (Vector2Int cell in remaining)
            {
                int best = FindNearestGroup(groups, cell);
                if (best >= 0)
                    groups[best].Add(cell);
                else
                    groups.Add(new HashSet<Vector2Int> { cell });
            }
        }

        return groups;
    }

    private int ChooseTargetPieceCellCount(System.Random random)
    {
        int maxSpan = Mathf.Clamp(maximumPieceTileSpan, 1, 4);
        int roll = random.Next(100);

        int span;
        if (roll < 12) span = 1;       // explicit 1x1-capable presentation pieces.
        else if (roll < 52) span = 2;  // 2x2 is the common small-piece size.
        else if (roll < 82) span = 3;
        else span = 4;

        span = Mathf.Clamp(span, 1, maxSpan);
        int target = span * span;
        return Mathf.Max(minimumPieceTileCount, target);
    }

    private static HashSet<Vector2Int> GrowConnectedGroup(
        Vector2Int start,
        HashSet<Vector2Int> remaining,
        Vector2 roomCenter,
        int targetCount)
    {
        HashSet<Vector2Int> group = new();
        if (!remaining.Contains(start))
            return group;

        List<Vector2Int> frontier = new() { start };
        HashSet<Vector2Int> queued = new() { start };
        Vector2Int[] dirs =
        {
            Vector2Int.right,
            Vector2Int.left,
            Vector2Int.up,
            Vector2Int.down
        };

        while (frontier.Count > 0 && group.Count < Mathf.Max(1, targetCount))
        {
            frontier.Sort((a, b) =>
            {
                // Prefer cells at a similar radius so pieces read as rings attached around the 4x4 center.
                float ra = Mathf.Abs(((Vector2)a - roomCenter).magnitude - ((Vector2)start - roomCenter).magnitude);
                float rb = Mathf.Abs(((Vector2)b - roomCenter).magnitude - ((Vector2)start - roomCenter).magnitude);
                return ra.CompareTo(rb);
            });

            Vector2Int current = frontier[0];
            frontier.RemoveAt(0);
            if (!remaining.Remove(current))
                continue;

            group.Add(current);

            for (int d = 0; d < dirs.Length; d++)
            {
                Vector2Int next = current + dirs[d];
                if (remaining.Contains(next) && queued.Add(next))
                    frontier.Add(next);
            }
        }

        return group;
    }

    private MapBlock CreateGranularPrototype(
        BattleNodeData node,
        HashSet<Vector2Int> group,
        HashSet<Vector2Int> fullCells,
        Dictionary<Vector2Int, GameObject> sourceTiles,
        WallStyle wallStyle,
        int index)
    {
        GameObject root = new($"__RuntimeRoomPiecePrototype_{node.id}_Rail_{index}");
        root.transform.position = new Vector3(10000f, 10000f, 0f);

        foreach (Vector2Int cell in group)
        {
            if (!sourceTiles.TryGetValue(cell, out GameObject source) || source == null)
                continue;

            GameObject tile = Instantiate(source, root.transform);
            tile.name = $"Tile_{cell.x}_{cell.y}";
            tile.transform.localPosition = new Vector3(cell.x, cell.y, 0f);
            tile.transform.localRotation = Quaternion.identity;
        }

        BuildOuterWalls(root.transform, group, fullCells, wallStyle);

        MapBlock block = root.AddComponent<MapBlock>();
        block.ConfigureRuntimeDockingBlock(
            root.transform,
            true,
            0.95f,
            node.room.largePieceEntryDuration,
            node.room.largePieceEntryOffset);

        MapBlockExitEaseField?.SetValue(block, railExitEase);
        return block;
    }

    private static void BuildOuterWalls(
        Transform root,
        HashSet<Vector2Int> group,
        HashSet<Vector2Int> fullCells,
        WallStyle style)
    {
        Vector2Int[] dirs =
        {
            Vector2Int.right,
            Vector2Int.left,
            Vector2Int.up,
            Vector2Int.down
        };

        foreach (Vector2Int cell in group)
        {
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector2Int edge = dirs[i];
                if (fullCells.Contains(cell + edge))
                    continue;

                CreateWall(root, cell, edge, style);
            }
        }
    }

    private static void CreateWall(Transform root, Vector2Int cell, Vector2Int edge, WallStyle style)
    {
        bool vertical = edge.x != 0;
        float thickness = Mathf.Max(0.03f, style.thickness);
        Vector2 size = vertical
            ? new Vector2(thickness, 1f + thickness)
            : new Vector2(1f + thickness, thickness);

        GameObject wall = new("RoomEdge");
        wall.transform.SetParent(root, false);
        wall.transform.localPosition = (Vector2)cell + (Vector2)edge * 0.5f;

        SpriteRenderer renderer = wall.AddComponent<SpriteRenderer>();
        renderer.sprite = style.sprite;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.size = size;
        renderer.color = style.color;
        renderer.sortingOrder = style.sortingOrder;
        if (style.material != null)
            renderer.sharedMaterial = style.material;

        BoxCollider2D collider = wall.AddComponent<BoxCollider2D>();
        collider.size = size;
        collider.isTrigger = false;

        NavMeshModifier modifier = wall.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = false;
        modifier.overrideArea = true;
        modifier.area = 1;
    }

    private void HandleRewardSelectionRequested(IReadOnlyList<BattleEquipmentSO> _)
    {
        // BattleStageTransitionController runs after this component. Re-centering each MapBlock root on
        // its actual visual bounds makes its existing radial rail calculation use the real piece position
        // instead of the shared RoomOrigin. This does not move anything on screen.
        RecenterActiveBlockRootsForRailExit();
        ApplyRailExitPresentationSettings();
    }

    private void RecenterActiveBlockRootsForRailExit()
    {
        if (roomManager == null || ActiveBlocksField == null)
            return;
        if (ActiveBlocksField.GetValue(roomManager) is not List<MapBlock> blocks)
            return;

        for (int i = 0; i < blocks.Count; i++)
        {
            MapBlock block = blocks[i];
            if (block == null)
                continue;

            if (!TryGetRendererBounds(block.gameObject, out Bounds bounds))
                continue;

            Transform root = block.transform;
            Vector3 desiredRootPosition = bounds.center;
            desiredRootPosition.z = root.position.z;
            Vector3 delta = desiredRootPosition - root.position;
            if (delta.sqrMagnitude < 0.000001f)
            {
                MapBlockExitEaseField?.SetValue(block, railExitEase);
                continue;
            }

            int childCount = root.childCount;
            Vector3[] worldPositions = new Vector3[childCount];
            Quaternion[] worldRotations = new Quaternion[childCount];
            for (int c = 0; c < childCount; c++)
            {
                Transform child = root.GetChild(c);
                worldPositions[c] = child.position;
                worldRotations[c] = child.rotation;
            }

            root.position = desiredRootPosition;
            for (int c = 0; c < childCount; c++)
            {
                Transform child = root.GetChild(c);
                child.position = worldPositions[c];
                child.rotation = worldRotations[c];
            }

            MapBlockExitEaseField?.SetValue(block, railExitEase);
        }
    }

    private void HandleRunEnded(RunEndReason _)
    {
        // No persistent runtime data to clear. Hosts stay alive for the next run.
    }

    private IEnumerator DestroyOurPrototypesNextFrame(List<GameObject> prototypes)
    {
        yield return null;
        if (prototypes == null)
            yield break;

        for (int i = 0; i < prototypes.Count; i++)
        {
            if (prototypes[i] != null)
                Destroy(prototypes[i]);
        }
    }

    private static int FindNearestGroup(List<HashSet<Vector2Int>> groups, Vector2Int cell)
    {
        if (groups == null || groups.Count == 0)
            return -1;

        int bestIndex = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i] == null || groups[i].Count == 0)
                continue;

            Vector2 center = CalculateCenter(groups[i]);
            float distance = ((Vector2)cell - center).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static Vector2 CalculateCenter(HashSet<Vector2Int> cells)
    {
        Vector2 sum = Vector2.zero;
        if (cells == null || cells.Count == 0)
            return sum;

        foreach (Vector2Int cell in cells)
            sum += (Vector2)cell;
        return sum / cells.Count;
    }

    private static Vector2 GetDominantOutwardDirection(Vector2 delta, int fallbackIndex)
    {
        if (delta.sqrMagnitude < 0.0001f)
        {
            Vector2[] fallback = { Vector2.left, Vector2.right, Vector2.down, Vector2.up };
            return fallback[Mathf.Abs(fallbackIndex) % fallback.Length];
        }

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            return delta.x >= 0f ? Vector2.right : Vector2.left;
        return delta.y >= 0f ? Vector2.up : Vector2.down;
    }

    private static void GetBounds(
        HashSet<Vector2Int> cells,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        minX = int.MaxValue;
        minY = int.MaxValue;
        maxX = int.MinValue;
        maxY = int.MinValue;

        foreach (Vector2Int cell in cells)
        {
            minX = Mathf.Min(minX, cell.x);
            minY = Mathf.Min(minY, cell.y);
            maxX = Mathf.Max(maxX, cell.x);
            maxY = Mathf.Max(maxY, cell.y);
        }
    }

    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            int hash = 23;
            if (text != null)
            {
                for (int i = 0; i < text.Length; i++)
                    hash = hash * 31 + text[i];
            }
            return hash;
        }
    }

    private readonly struct WallStyle
    {
        public readonly Sprite sprite;
        public readonly Material material;
        public readonly Color color;
        public readonly int sortingOrder;
        public readonly float thickness;

        public WallStyle(Sprite sprite, Material material, Color color, int sortingOrder, float thickness)
        {
            this.sprite = sprite;
            this.material = material;
            this.color = color;
            this.sortingOrder = sortingOrder;
            this.thickness = thickness;
        }

        public static WallStyle From(SpriteRenderer renderer, BoxCollider2D collider)
        {
            float thickness = 0.12f;
            if (collider != null)
                thickness = Mathf.Min(Mathf.Abs(collider.size.x), Mathf.Abs(collider.size.y));
            else if (renderer != null)
                thickness = Mathf.Min(Mathf.Abs(renderer.size.x), Mathf.Abs(renderer.size.y));

            return new WallStyle(
                renderer != null ? renderer.sprite : null,
                renderer != null ? renderer.sharedMaterial : null,
                renderer != null ? renderer.color : new Color(0.31f, 0.35f, 0.41f, 1f),
                renderer != null ? renderer.sortingOrder : 3,
                Mathf.Max(0.03f, thickness));
        }

        public static WallStyle Fallback(Dictionary<Vector2Int, GameObject> tiles)
        {
            Sprite sprite = null;
            Material material = null;
            if (tiles != null)
            {
                foreach (KeyValuePair<Vector2Int, GameObject> pair in tiles)
                {
                    if (pair.Value == null)
                        continue;
                    SpriteRenderer renderer = pair.Value.GetComponent<SpriteRenderer>();
                    if (renderer == null)
                        continue;
                    sprite = renderer.sprite;
                    material = renderer.sharedMaterial;
                    break;
                }
            }

            return new WallStyle(
                sprite,
                material,
                new Color(0.31f, 0.35f, 0.41f, 1f),
                3,
                0.12f);
        }
    }
}

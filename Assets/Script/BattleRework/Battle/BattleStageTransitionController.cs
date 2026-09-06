using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using NavMeshPlus.Components;
using UnityEngine;

/// <summary>
/// Stage-to-stage spatial presentation.
///
/// Contract:
/// - the persistent 4x4 Base is the authoritative surviving floor.
/// - on clear, a complete existing 4x4 near/under the player is promoted to the next Base.
/// - every other active Room piece is visually cloned and sent straight out on a cardinal rail.
/// - reward acquisition is NOT handled in world space here. Rewards are a click decision in BattleHUD.
/// </summary>
[DefaultExecutionOrder(-15000)]
public sealed class BattleStageTransitionController : MonoBehaviour
{
    [Header("Stage Clear / Rail Exit")]
    [SerializeField, Min(0f)] private float exitGhostExtraDistance = 8f;
    [SerializeField, Min(0f)] private float exitGhostStagger = 0.035f;

    [Header("Persistent Base Presentation")]
    [Tooltip("Generated floor uses sorting -20. The preserved 4x4 remains immediately above it.")]
    [SerializeField] private int persistentBaseFloorSorting = -19;

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo ActiveBlocksField =
        typeof(BattleRoomManager).GetField("activeBlocks", PrivateInstance);

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;

    private readonly List<GameObject> exitGhosts = new();

    private Vector3 preservedBaseTileOrigin;
    private bool hasPreservedBaseOrigin;
    private bool clearedStageCollapsed;
    private bool subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleStageTransitionController>() != null)
            return;

        GameObject host = new("BattleStageTransitionRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleStageTransitionController>();
    }

    private void OnEnable()
    {
        StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        Unsubscribe();
        ClearExitGhosts();
    }

    private IEnumerator BindWhenReady()
    {
        while (runManager == null || roomManager == null || baseTemplate == null || player == null)
        {
            ResolveSystems();
            yield return null;
        }

        // BattleSpatialMapController has a lower execution order and prepares Room data first.
        yield return null;
        Subscribe();

        baseTemplate.EnsurePersistentBase();
        CaptureCurrentBaseAnchor();
        EnsureBasePresentation();
    }

    private void ResolveSystems()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
    }

    private void Subscribe()
    {
        if (subscribed || runManager == null)
            return;

        runManager.StateChanged += HandleStateChanged;
        runManager.NodeEntered += HandleNodeEntered;
        runManager.RewardSelectionRequested += HandleRewardSelectionRequested;
        runManager.RunEnded += HandleRunEnded;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || runManager == null)
            return;

        runManager.StateChanged -= HandleStateChanged;
        runManager.NodeEntered -= HandleNodeEntered;
        runManager.RewardSelectionRequested -= HandleRewardSelectionRequested;
        runManager.RunEnded -= HandleRunEnded;
        subscribed = false;
    }

    private void Update()
    {
        ResolveSystems();
        CleanupNullEntries(exitGhosts);
    }

    private void HandleStateChanged(BattleRunState next)
    {
        if (next != BattleRunState.EnteringNode)
            return;

        ResolveSystems();
        if (baseTemplate == null)
            return;

        baseTemplate.EnsurePersistentBase();
        CaptureCurrentBaseAnchor();
        EnsureBasePresentation();
        ApplyBaseOriginToRoomManager();
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node == null || node.room == null)
            return;
        if (node.type != BattleNodeType.Combat && node.type != BattleNodeType.Elite)
            return;

        ResolveSystems();
        if (baseTemplate == null || roomManager == null)
            return;

        baseTemplate.EnsurePersistentBase();
        CaptureCurrentBaseAnchor();

        if (!hasPreservedBaseOrigin)
        {
            Debug.LogError("[BattleStageTransition] No persistent 4x4 Base exists for the incoming Room.", this);
            return;
        }

        baseTemplate.EnsureVisibleAtTileOrigin(preservedBaseTileOrigin, node.room);
        ApplyBaseOriginToRoomManager();
        EnsureBasePresentation();

        node.room.repositionPlayerOnEnter = false;
        clearedStageCollapsed = false;
    }

    private void CaptureCurrentBaseAnchor()
    {
        if (baseTemplate == null || !baseTemplate.HasPersistentBase)
            return;

        preservedBaseTileOrigin = baseTemplate.FixedTileOriginWorld;
        hasPreservedBaseOrigin = true;
    }

    private void ApplyBaseOriginToRoomManager()
    {
        if (!hasPreservedBaseOrigin || roomManager == null || roomManager.RoomOrigin == null)
            return;

        Vector3 origin = preservedBaseTileOrigin;
        origin.z = roomManager.RoomOrigin.position.z;
        roomManager.RoomOrigin.position = origin;
    }

    private void EnsureBasePresentation()
    {
        if (baseTemplate == null)
            return;

        baseTemplate.EnsurePersistentBase();
        GameObject baseObject = baseTemplate.ActiveBase;
        if (baseObject == null)
            return;

        SpriteRenderer[] renderers = baseObject.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.enabled = true;
            if (renderer.sortingOrder < persistentBaseFloorSorting)
                renderer.sortingOrder = persistentBaseFloorSorting;
        }

        Collider2D[] colliders = baseObject.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider != null && collider.isTrigger)
                collider.enabled = true;
        }

        NavMeshModifier[] modifiers = baseObject.GetComponentsInChildren<NavMeshModifier>(true);
        for (int i = 0; i < modifiers.Length; i++)
        {
            if (modifiers[i] != null)
                modifiers[i].ignoreFromBuild = false;
        }

        BattleWalkableField[] fields = baseObject.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i] != null)
                fields[i].enabled = true;
        }
    }

    /// <summary>
    /// Reward state begins only after the current real field has been collapsed to a valid 4x4.
    /// The reward itself is then presented as clickable UI by BattleHUD.
    /// </summary>
    private void HandleRewardSelectionRequested(IReadOnlyList<BattleEquipmentSO> _)
    {
        ResolveSystems();
        CollapseClearedStageAroundPlayer();
    }

    private void CollapseClearedStageAroundPlayer()
    {
        if (clearedStageCollapsed || baseTemplate == null || player == null || roomManager == null)
            return;
        if (!roomManager.IsRoomActive)
            return;

        Vector3 nextBaseOrigin;
        if (!TryFindExistingFourByFourBase(player.transform.position, out nextBaseOrigin))
        {
            Debug.LogWarning(
                "[BattleStageTransition] Could not find a complete existing 4x4 in the cleared stage. Keeping the current Base.",
                this);
            nextBaseOrigin = baseTemplate.FixedTileOriginWorld;
        }

        preservedBaseTileOrigin = baseTemplate.ReanchorToTileOrigin(nextBaseOrigin);
        hasPreservedBaseOrigin = true;
        EnsureBasePresentation();
        ApplyBaseOriginToRoomManager();

        SpawnExitGhostsFromCurrentRoom();
        HideCurrentRoomBlocks();
        clearedStageCollapsed = true;
    }

    private bool TryFindExistingFourByFourBase(Vector3 playerWorldPosition, out Vector3 lowerLeftTileCenterWorld)
    {
        lowerLeftTileCenterWorld = default;

        HashSet<Vector2Int> existing = new();
        CollectCurrentStageTileCells(existing);
        if (existing.Count < RoomBaseTemplate.FixedBaseTiles * RoomBaseTemplate.FixedBaseTiles)
            return false;

        Vector2Int playerTile = WorldToTile(playerWorldPosition);
        Vector2 playerTilePosition = new(
            playerWorldPosition.x / RoomBaseTemplate.TileWorldSize,
            playerWorldPosition.y / RoomBaseTemplate.TileWorldSize);

        bool found = false;
        Vector2Int bestOrigin = default;
        float bestScore = float.MaxValue;

        // Prefer a complete 4x4 that contains the player's current tile.
        for (int oy = playerTile.y - (RoomBaseTemplate.FixedBaseTiles - 1); oy <= playerTile.y; oy++)
        {
            for (int ox = playerTile.x - (RoomBaseTemplate.FixedBaseTiles - 1); ox <= playerTile.x; ox++)
            {
                Vector2Int origin = new(ox, oy);
                if (!IsCompleteFourByFour(existing, origin))
                    continue;

                float score = ScoreBaseOrigin(origin, playerTilePosition);
                if (!found || score < bestScore)
                {
                    found = true;
                    bestScore = score;
                    bestOrigin = origin;
                }
            }
        }

        // Near an irregular edge, use the closest complete real 4x4 instead of inventing missing tiles.
        if (!found)
        {
            GetCellBounds(existing, out int minX, out int minY, out int maxX, out int maxY);
            int maxOriginX = maxX - RoomBaseTemplate.FixedBaseTiles + 1;
            int maxOriginY = maxY - RoomBaseTemplate.FixedBaseTiles + 1;

            for (int oy = minY; oy <= maxOriginY; oy++)
            {
                for (int ox = minX; ox <= maxOriginX; ox++)
                {
                    Vector2Int origin = new(ox, oy);
                    if (!IsCompleteFourByFour(existing, origin))
                        continue;

                    float score = ScoreBaseOrigin(origin, playerTilePosition);
                    if (!found || score < bestScore)
                    {
                        found = true;
                        bestScore = score;
                        bestOrigin = origin;
                    }
                }
            }
        }

        if (!found)
            return false;

        lowerLeftTileCenterWorld = new Vector3(
            bestOrigin.x * RoomBaseTemplate.TileWorldSize,
            bestOrigin.y * RoomBaseTemplate.TileWorldSize,
            baseTemplate.FixedTileOriginWorld.z);
        return true;
    }

    private void CollectCurrentStageTileCells(HashSet<Vector2Int> cells)
    {
        if (cells == null)
            return;

        if (baseTemplate != null && baseTemplate.HasPersistentBase)
        {
            Vector2Int baseOrigin = WorldToTile(baseTemplate.FixedTileOriginWorld);
            for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
            {
                for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                    cells.Add(baseOrigin + new Vector2Int(x, y));
            }
        }

        if (roomManager == null || ActiveBlocksField == null)
            return;
        if (ActiveBlocksField.GetValue(roomManager) is not List<MapBlock> blocks)
            return;

        for (int i = 0; i < blocks.Count; i++)
        {
            MapBlock block = blocks[i];
            if (block == null)
                continue;

            Transform[] transforms = block.GetComponentsInChildren<Transform>(true);
            for (int t = 0; t < transforms.Length; t++)
            {
                Transform child = transforms[t];
                if (child == null || !child.name.StartsWith("Tile_", StringComparison.Ordinal))
                    continue;

                cells.Add(WorldToTile(child.position));
            }
        }
    }

    private static Vector2Int WorldToTile(Vector3 world)
    {
        return new Vector2Int(
            Mathf.RoundToInt(world.x / RoomBaseTemplate.TileWorldSize),
            Mathf.RoundToInt(world.y / RoomBaseTemplate.TileWorldSize));
    }

    private static bool IsCompleteFourByFour(HashSet<Vector2Int> cells, Vector2Int lowerLeft)
    {
        for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
        {
            for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
            {
                if (!cells.Contains(lowerLeft + new Vector2Int(x, y)))
                    return false;
            }
        }
        return true;
    }

    private static float ScoreBaseOrigin(Vector2Int lowerLeft, Vector2 playerTilePosition)
    {
        Vector2 center = (Vector2)lowerLeft + new Vector2(1.5f, 1.5f);
        return (center - playerTilePosition).sqrMagnitude;
    }

    private static void GetCellBounds(
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

    private void SpawnExitGhostsFromCurrentRoom()
    {
        if (roomManager == null || ActiveBlocksField == null)
            return;
        if (ActiveBlocksField.GetValue(roomManager) is not List<MapBlock> blocks || blocks.Count == 0)
            return;

        GameObject root = new("OutgoingStageVisuals");
        DontDestroyOnLoad(root);

        Vector2 baseCenter = baseTemplate != null
            ? (Vector2)baseTemplate.FixedCenterWorld
            : (player != null ? (Vector2)player.transform.position : (Vector2)roomManager.RoomOrigin.position);

        int ghostIndex = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            MapBlock source = blocks[i];
            if (source == null)
                continue;

            GameObject cloneObject = Instantiate(
                source.gameObject,
                source.transform.position,
                source.transform.rotation,
                root.transform);
            cloneObject.name = "Outgoing_" + source.name;
            DisableGhostGameplay(cloneObject);

            MapBlock ghostBlock = cloneObject.GetComponent<MapBlock>();
            if (ghostBlock == null)
            {
                Destroy(cloneObject);
                continue;
            }

            Vector2 pieceCenter = TryGetRendererBounds(cloneObject, out Bounds bounds)
                ? (Vector2)bounds.center
                : (Vector2)cloneObject.transform.position;
            Vector2 direction = ResolveCardinalRailDirection(pieceCenter - baseCenter, ghostIndex);

            float delay = Mathf.Max(0f, exitGhostStagger) * ghostIndex;
            float duration = Mathf.Max(0.05f, ghostBlock.ExitDuration);
            Tween moveTween = ghostBlock.PlayExit(direction);
            if (moveTween != null)
            {
                if (delay > 0f)
                    moveTween.SetDelay(delay);

                if (exitGhostExtraDistance > 0f)
                {
                    moveTween.OnComplete(() =>
                    {
                        if (cloneObject == null)
                            return;

                        cloneObject.transform
                            .DOMove(
                                cloneObject.transform.position + (Vector3)(direction * exitGhostExtraDistance),
                                0.20f)
                            .SetEase(Ease.InQuad);
                    });
                }
            }

            exitGhosts.Add(cloneObject);
            Destroy(cloneObject, duration + delay + 0.75f);
            ghostIndex++;
        }

        Destroy(root, 5f);
    }

    private static Vector2 ResolveCardinalRailDirection(Vector2 delta, int fallbackIndex)
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

    private void HideCurrentRoomBlocks()
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

            Renderer[] renderers = block.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] != null)
                    renderers[r].enabled = false;
            }

            Collider2D[] colliders = block.GetComponentsInChildren<Collider2D>(true);
            for (int c = 0; c < colliders.Length; c++)
            {
                if (colliders[c] != null)
                    colliders[c].enabled = false;
            }

            NavMeshModifier[] modifiers = block.GetComponentsInChildren<NavMeshModifier>(true);
            for (int m = 0; m < modifiers.Length; m++)
            {
                if (modifiers[m] != null)
                    modifiers[m].ignoreFromBuild = true;
            }

            BattleWalkableField[] fields = block.GetComponentsInChildren<BattleWalkableField>(true);
            for (int f = 0; f < fields.Length; f++)
            {
                if (fields[f] != null)
                    fields[f].enabled = false;
            }
        }

        EnsureBasePresentation();
    }

    private static void DisableGhostGameplay(GameObject root)
    {
        Collider2D[] colliders = root.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        NavMeshModifier[] modifiers = root.GetComponentsInChildren<NavMeshModifier>(true);
        for (int i = 0; i < modifiers.Length; i++)
        {
            if (modifiers[i] != null)
                modifiers[i].ignoreFromBuild = true;
        }

        BattleWalkableField[] fields = root.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i] != null)
                fields[i].enabled = false;
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

    private void HandleRunEnded(RunEndReason _)
    {
        ClearExitGhosts();
        hasPreservedBaseOrigin = false;
        clearedStageCollapsed = false;
    }

    private void ClearExitGhosts()
    {
        for (int i = 0; i < exitGhosts.Count; i++)
        {
            GameObject go = exitGhosts[i];
            if (go == null)
                continue;

            go.transform.DOKill();
            Destroy(go);
        }
        exitGhosts.Clear();
    }

    private static void CleanupNullEntries(List<GameObject> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == null)
                list.RemoveAt(i);
        }
    }
}

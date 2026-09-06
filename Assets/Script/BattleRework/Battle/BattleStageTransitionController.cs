using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using NavMeshPlus.Components;
using UnityEngine;

/// <summary>
/// Stage-to-stage spatial presentation.
/// - the real cleared floor supplies the next persistent 4x4 Base.
/// - the rest of the combat field leaves on cardinal rails.
/// - Reward temporarily docks a non-gameplay 10x4 show floor beside the Base in several heavy slabs.
/// - reward selection itself remains click-only UI; the temporary show floor never becomes next-stage topology.
/// </summary>
[DefaultExecutionOrder(-15000)]
public sealed class BattleStageTransitionController : MonoBehaviour
{
    [Header("Stage Clear / Rail Exit")]
    [SerializeField, Min(0f)] private float exitGhostExtraDistance = 8f;
    [SerializeField, Min(0f)] private float exitGhostStagger = 0.035f;

    [Header("Persistent Base Presentation")]
    [SerializeField] private int persistentBaseFloorSorting = -19;

    [Header("Reward Show Stage")]
    [Tooltip("Temporary floor length attached to the right side of the preserved 4x4 Base.")]
    [SerializeField, Range(6, 16)] private int rewardStageExtraTiles = 10;
    [SerializeField, Range(2, 6)] private int rewardStageDepthTiles = 4;
    [SerializeField, Range(2, 5)] private int rewardStagePieceCount = 3;
    [SerializeField, Min(0.1f)] private float rewardStageEntryDuration = 0.78f;
    [SerializeField, Min(0f)] private float rewardStageEntryStagger = 0.12f;
    [SerializeField, Range(0.1f, 2f)] private float rewardStageImpactStrength = 1.45f;
    [SerializeField, Min(0.5f)] private float rewardStageOffscreenMargin = 3f;
    [SerializeField] private Color rewardStageFloorColor = new(0.30f, 0.24f, 0.38f, 1f);
    [SerializeField] private int rewardStageSortingOrder = -18;

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo ActiveBlocksField =
        typeof(BattleRoomManager).GetField("activeBlocks", PrivateInstance);

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;

    private readonly List<GameObject> exitGhosts = new();
    private readonly List<MapBlock> rewardStagePieces = new();

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
        ClearRewardStageImmediate();
        ClearExitGhosts();
    }

    private IEnumerator BindWhenReady()
    {
        while (runManager == null || roomManager == null || baseTemplate == null || player == null)
        {
            ResolveSystems();
            yield return null;
        }

        yield return null;
        Subscribe();

        baseTemplate.EnsurePersistentBase();
        CaptureCurrentBaseAnchor();
        EnsureBasePresentation();
    }

    private void ResolveSystems()
    {
        if (runManager == null) runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null) roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (baseTemplate == null) baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
    }

    private void Subscribe()
    {
        if (subscribed || runManager == null)
            return;

        runManager.StateChanged += HandleStateChanged;
        runManager.NodeEntered += HandleNodeEntered;
        runManager.RewardSelectionRequested += HandleRewardSelectionRequested;
        runManager.RewardSelected += HandleRewardSelected;
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
        runManager.RewardSelected -= HandleRewardSelected;
        runManager.RunEnded -= HandleRunEnded;
        subscribed = false;
    }

    private void Update()
    {
        ResolveSystems();
        CleanupNullEntries(exitGhosts);
        for (int i = rewardStagePieces.Count - 1; i >= 0; i--)
            if (rewardStagePieces[i] == null)
                rewardStagePieces.RemoveAt(i);
    }

    private void HandleStateChanged(BattleRunState next)
    {
        if (next == BattleRunState.EnteringNode)
        {
            ClearRewardStageImmediate();
            ResolveSystems();
            if (baseTemplate == null)
                return;

            baseTemplate.EnsurePersistentBase();
            CaptureCurrentBaseAnchor();
            EnsureBasePresentation();
            ApplyBaseOriginToRoomManager();
        }
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

        ClearRewardStageImmediate();
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
            if (renderer == null) continue;
            renderer.enabled = true;
            if (renderer.sortingOrder < persistentBaseFloorSorting)
                renderer.sortingOrder = persistentBaseFloorSorting;
        }

        Collider2D[] colliders = baseObject.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null && colliders[i].isTrigger)
                colliders[i].enabled = true;

        NavMeshModifier[] modifiers = baseObject.GetComponentsInChildren<NavMeshModifier>(true);
        for (int i = 0; i < modifiers.Length; i++)
            if (modifiers[i] != null)
                modifiers[i].ignoreFromBuild = false;

        BattleWalkableField[] fields = baseObject.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < fields.Length; i++)
            if (fields[i] != null)
                fields[i].enabled = true;
    }

    private void HandleRewardSelectionRequested(IReadOnlyList<BattleEquipmentSO> _)
    {
        ResolveSystems();
        CollapseClearedStageAroundPlayer();
        BuildRewardShowStage();
    }

    private void HandleRewardSelected(BattleEquipmentSO _)
    {
        DismissRewardShowStage();
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
            Debug.LogWarning("[BattleStageTransition] No complete real 4x4 near the player; keeping current Base.", this);
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

    // ---------------------------------------------------------------------
    // Temporary quiz-show floor: 10x4 beside Base, split into large slabs.
    // ---------------------------------------------------------------------

    private void BuildRewardShowStage()
    {
        ClearRewardStageImmediate();
        if (baseTemplate == null || !baseTemplate.HasPersistentBase)
            return;

        int totalWidth = Mathf.Clamp(rewardStageExtraTiles, 6, 16);
        int depth = Mathf.Clamp(rewardStageDepthTiles, 2, 6);
        int pieceCount = Mathf.Clamp(rewardStagePieceCount, 2, Mathf.Min(5, totalWidth / 2));
        Vector3 baseOrigin = baseTemplate.FixedTileOriginWorld;

        int consumed = 0;
        for (int i = 0; i < pieceCount; i++)
        {
            int piecesLeft = pieceCount - i;
            int tilesLeft = totalWidth - consumed;
            int width = Mathf.Max(2, Mathf.CeilToInt(tilesLeft / (float)piecesLeft));
            if (i == pieceCount - 1)
                width = tilesLeft;

            Vector3 destination = baseOrigin + new Vector3(RoomBaseTemplate.FixedBaseTiles + consumed, 0f, 0f);
            MapBlock piece = CreateRewardStageSlab(i, width, depth, destination);
            if (piece != null)
            {
                float offset = CalculateRightOffscreenEntryOffset(destination, width);
                piece.ConfigureRuntimeDockingBlock(
                    piece.transform.Find("Visual"),
                    false,
                    rewardStageImpactStrength,
                    rewardStageEntryDuration,
                    offset);
                piece.PlayEnter(destination, Vector2.right, rewardStageEntryStagger * i);
                rewardStagePieces.Add(piece);
            }

            consumed += width;
        }
    }

    private MapBlock CreateRewardStageSlab(int index, int width, int height, Vector3 destination)
    {
        GameObject root = new($"RewardShowSlab_{index}_{width}x{height}");
        root.transform.SetParent(transform, true);
        root.transform.position = destination;

        GameObject visual = new("Visual");
        visual.transform.SetParent(root.transform, false);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                GameObject tile = new($"ShowTile_{x}_{y}");
                tile.transform.SetParent(visual.transform, false);
                tile.transform.localPosition = new Vector3(x, y, 0f);
                SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
                renderer.sprite = RewardStageRuntimeSpriteCache.FloorTile32;
                renderer.color = rewardStageFloorColor;
                renderer.sortingOrder = rewardStageSortingOrder;
            }
        }

        MapBlock block = root.AddComponent<MapBlock>();
        return block;
    }

    private float CalculateRightOffscreenEntryOffset(Vector3 destination, int pieceWidth)
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic)
            return 30f + pieceWidth;

        float rightEdge = cam.transform.position.x + cam.orthographicSize * Mathf.Max(0.1f, cam.aspect);
        // The slab's left-most visible edge must begin beyond the viewport before it starts moving.
        float required = rightEdge + rewardStageOffscreenMargin - (destination.x - 0.5f);
        return Mathf.Max(12f, required);
    }

    private void DismissRewardShowStage()
    {
        for (int i = 0; i < rewardStagePieces.Count; i++)
        {
            MapBlock piece = rewardStagePieces[i];
            if (piece == null)
                continue;

            float delay = rewardStageEntryStagger * i * 0.6f;
            Tween tween = piece.PlayExit(Vector2.right);
            if (tween != null)
            {
                if (delay > 0f)
                    tween.SetDelay(delay);
                GameObject go = piece.gameObject;
                tween.OnComplete(() =>
                {
                    if (go != null)
                        Destroy(go);
                });
            }
            else
            {
                Destroy(piece.gameObject);
            }
        }
        rewardStagePieces.Clear();
    }

    private void ClearRewardStageImmediate()
    {
        for (int i = 0; i < rewardStagePieces.Count; i++)
        {
            MapBlock piece = rewardStagePieces[i];
            if (piece == null) continue;
            piece.transform.DOKill();
            Destroy(piece.gameObject);
        }
        rewardStagePieces.Clear();
    }

    // ---------------------------------------------------------------------
    // Real cleared-field 4x4 selection.
    // ---------------------------------------------------------------------

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

        for (int oy = playerTile.y - (RoomBaseTemplate.FixedBaseTiles - 1); oy <= playerTile.y; oy++)
        {
            for (int ox = playerTile.x - (RoomBaseTemplate.FixedBaseTiles - 1); ox <= playerTile.x; ox++)
            {
                Vector2Int origin = new(ox, oy);
                if (!IsCompleteFourByFour(existing, origin)) continue;
                float score = ScoreBaseOrigin(origin, playerTilePosition);
                if (!found || score < bestScore)
                {
                    found = true;
                    bestScore = score;
                    bestOrigin = origin;
                }
            }
        }

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
                    if (!IsCompleteFourByFour(existing, origin)) continue;
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
                for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                    cells.Add(baseOrigin + new Vector2Int(x, y));
        }

        if (roomManager == null || ActiveBlocksField == null)
            return;
        if (ActiveBlocksField.GetValue(roomManager) is not List<MapBlock> blocks)
            return;

        for (int i = 0; i < blocks.Count; i++)
        {
            MapBlock block = blocks[i];
            if (block == null) continue;
            Transform[] transforms = block.GetComponentsInChildren<Transform>(true);
            for (int t = 0; t < transforms.Length; t++)
            {
                Transform child = transforms[t];
                if (child != null && child.name.StartsWith("Tile_", StringComparison.Ordinal))
                    cells.Add(WorldToTile(child.position));
            }
        }
    }

    private static Vector2Int WorldToTile(Vector3 world) => new(
        Mathf.RoundToInt(world.x / RoomBaseTemplate.TileWorldSize),
        Mathf.RoundToInt(world.y / RoomBaseTemplate.TileWorldSize));

    private static bool IsCompleteFourByFour(HashSet<Vector2Int> cells, Vector2Int lowerLeft)
    {
        for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
            for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                if (!cells.Contains(lowerLeft + new Vector2Int(x, y)))
                    return false;
        return true;
    }

    private static float ScoreBaseOrigin(Vector2Int lowerLeft, Vector2 playerTilePosition)
    {
        Vector2 center = (Vector2)lowerLeft + new Vector2(1.5f, 1.5f);
        return (center - playerTilePosition).sqrMagnitude;
    }

    private static void GetCellBounds(HashSet<Vector2Int> cells, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = int.MaxValue; minY = int.MaxValue; maxX = int.MinValue; maxY = int.MinValue;
        foreach (Vector2Int cell in cells)
        {
            minX = Mathf.Min(minX, cell.x); minY = Mathf.Min(minY, cell.y);
            maxX = Mathf.Max(maxX, cell.x); maxY = Mathf.Max(maxY, cell.y);
        }
    }

    // ---------------------------------------------------------------------
    // Old combat-field rail-out presentation.
    // ---------------------------------------------------------------------

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
            if (source == null) continue;

            GameObject cloneObject = Instantiate(source.gameObject, source.transform.position, source.transform.rotation, root.transform);
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
                if (delay > 0f) moveTween.SetDelay(delay);
                if (exitGhostExtraDistance > 0f)
                {
                    moveTween.OnComplete(() =>
                    {
                        if (cloneObject == null) return;
                        cloneObject.transform
                            .DOMove(cloneObject.transform.position + (Vector3)(direction * exitGhostExtraDistance), 0.20f)
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
            if (block == null) continue;

            Renderer[] renderers = block.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++) if (renderers[r] != null) renderers[r].enabled = false;
            Collider2D[] colliders = block.GetComponentsInChildren<Collider2D>(true);
            for (int c = 0; c < colliders.Length; c++) if (colliders[c] != null) colliders[c].enabled = false;
            NavMeshModifier[] modifiers = block.GetComponentsInChildren<NavMeshModifier>(true);
            for (int m = 0; m < modifiers.Length; m++) if (modifiers[m] != null) modifiers[m].ignoreFromBuild = true;
            BattleWalkableField[] fields = block.GetComponentsInChildren<BattleWalkableField>(true);
            for (int f = 0; f < fields.Length; f++) if (fields[f] != null) fields[f].enabled = false;
        }
        EnsureBasePresentation();
    }

    private static void DisableGhostGameplay(GameObject root)
    {
        Collider2D[] colliders = root.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = false;
        NavMeshModifier[] modifiers = root.GetComponentsInChildren<NavMeshModifier>(true);
        for (int i = 0; i < modifiers.Length; i++) if (modifiers[i] != null) modifiers[i].ignoreFromBuild = true;
        BattleWalkableField[] fields = root.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < fields.Length; i++) if (fields[i] != null) fields[i].enabled = false;
    }

    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        if (root == null) return false;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    private void HandleRunEnded(RunEndReason _)
    {
        ClearRewardStageImmediate();
        ClearExitGhosts();
        hasPreservedBaseOrigin = false;
        clearedStageCollapsed = false;
    }

    private void ClearExitGhosts()
    {
        for (int i = 0; i < exitGhosts.Count; i++)
        {
            GameObject go = exitGhosts[i];
            if (go == null) continue;
            go.transform.DOKill();
            Destroy(go);
        }
        exitGhosts.Clear();
    }

    private static void CleanupNullEntries(List<GameObject> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i] == null)
                list.RemoveAt(i);
    }
}

internal static class RewardStageRuntimeSpriteCache
{
    private static Sprite floorTile32;
    public static Sprite FloorTile32 => floorTile32 != null ? floorTile32 : floorTile32 = CreateFloorTile32();

    private static Sprite CreateFloorTile32()
    {
        const int pixels = 32;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (int y = 0; y < pixels; y++)
        {
            for (int x = 0; x < pixels; x++)
            {
                bool seam = x == 0 || y == 0;
                texture.SetPixel(x, y, seam ? new Color(0.80f, 0.82f, 0.90f, 1f) : Color.white);
            }
        }
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, pixels, pixels),
            new Vector2(0.5f, 0.5f),
            pixels,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "RuntimeRewardShowFloor_32px";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}

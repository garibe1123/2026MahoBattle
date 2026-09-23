using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NavMeshPlus.Components;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Single owner of battle-field spatial planning and Stage Map presentation.
///
/// Spatial invariants:
/// - 32px = 1 tile = 1 world unit.
/// - the currently preserved 4x4 Base is INPUT, never an incoming Room product.
/// - targetCells are generated around that Base, then extensionCells = targetCells - baseCells.
/// - only extensionCells become incoming MapBlock pieces.
/// - large connected pieces are preferred; isolated 1x1 pieces are merged whenever possible.
/// - every incoming piece starts fully outside the current camera viewport and slides in on a cardinal rail.
/// - Stage Map is selection-only: no combat-time top-right mini map exists.
/// - Stage Map depth flows from left to right; nodes on the same depth are vertical alternatives.
/// </summary>
[DefaultExecutionOrder(-20000)]
public sealed class BattleSpatialMapController : MonoBehaviour
{
    [Header("Procedural Room")]
    [SerializeField] private Color roomFloorColor = new(0.18f, 0.21f, 0.25f, 1f);
    [SerializeField, Min(0.03f)] private float roomEdgeThickness = 0.12f;
    [SerializeField, Range(0.1f, 1.5f)] private float roomImpactStrength = 0.95f;

    [Header("Extension Piece Assembly")]
    [Tooltip("Maximum rectangular span considered for one incoming piece. Large pieces are preferred.")]
    [SerializeField, Range(3, 8)] private int maximumPieceTileSpan = 6;
    [Tooltip("Pieces smaller than this are merged into an adjacent piece whenever possible.")]
    [SerializeField, Range(2, 12)] private int minimumPieceCellCount = 4;
    [Tooltip("Used by fallback connected growth when a large rectangle cannot be carved from the frontier.")]
    [SerializeField, Range(4, 24)] private int preferredPieceCellCount = 12;
    [SerializeField, Range(8, 64)] private int maximumPieceCount = 32;
    [Tooltip("Fallback rail distance when no orthographic camera is available.")]
    [SerializeField, Min(12f)] private float fallbackOffscreenEntryDistance = 36f;
    [SerializeField, Min(0.5f)] private float offscreenMargin = 2f;

    [Header("Stage Map - Selection Only")]
    [SerializeField] private Vector2 selectionMapSize = new(1560f, 760f);
    [SerializeField, Min(60f)] private float mapHorizontalSpacing = 150f;
    [SerializeField, Min(60f)] private float mapVerticalSpacing = 112f;
    [SerializeField, Min(24f)] private float mapNodeSize = 44f;
    [SerializeField, Min(0.05f)] private float mapRevealDuration = 0.28f;
    [SerializeField, Min(0f)] private float mapRevealSlideDistance = 90f;
    [Tooltip("맵 노드 확정 후 다음 스테이지로 넘어가기 전에 재생하는 충격 연출 시간입니다.")]
    [SerializeField, Range(0.15f, 1f)] private float mapConfirmDuration = 0.44f;
    [Tooltip("선택 확정 순간 실제 월드 카메라가 흔들리는 거리입니다. UI 보드 위치에는 적용하지 않습니다.")]
    [SerializeField, Range(0f, 0.75f)] private float mapConfirmCameraShake = 0.22f;
    [SerializeField, Range(1f, 1.18f)] private float mapConfirmZoom = 1.105f;
    [Header("Stage Map - Route Commit")]
    [SerializeField, Range(0.05f, 0.35f)] private float mapRouteShutdownStep = 0.09f;
    [SerializeField, Range(0.18f, 0.75f)] private float mapRouteTraceDuration = 0.42f;
    [SerializeField, Range(0.10f, 0.50f)] private float mapRouteLockHold = 0.24f;
    [SerializeField] private Color mapRouteSelected = new(0.18f, 0.94f, 0.96f, 1f);
    [SerializeField] private Color mapRouteDenied = new(1f, 0.20f, 0.48f, 1f);
    [Tooltip("Camera focus may move a World-Space node under the cursor. This screen-space hysteresis prevents hover enter/exit feedback loops.")]
    [SerializeField, Range(8f, 160f)] private float mapHoverLatchPixels = 56f;
    [SerializeField] private Color mapUnknown = new(0.18f, 0.21f, 0.27f, 0.96f);
    [SerializeField] private Color mapVisited = new(0.48f, 0.54f, 0.62f, 1f);
    [SerializeField] private Color mapCurrent = new(0.30f, 0.90f, 1f, 1f);
    [SerializeField] private Color mapAvailable = new(0.74f, 0.82f, 0.90f, 1f);
    [SerializeField] private Color mapElite = new(1f, 0.38f, 0.20f, 1f);
    [SerializeField] private Color mapLink = new(0.30f, 0.35f, 0.43f, 0.96f);

    [Header("32px Tile / 48px Character Test Scale")]
    [SerializeField, Min(0.5f)] private float testPlayerWorldHeight = 1.5f;
    [SerializeField, Min(0.1f)] private float testPlayerWorldColliderRadius = 0.60f;
    [SerializeField, Min(0.5f)] private float testMonsterWorldHeight = 1.5f;
    [SerializeField, Min(0.1f)] private float testMonsterWorldColliderRadius = 0.55f;

    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Vector2Int[] Cardinal =
    {
        Vector2Int.right,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.down
    };

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;
    private BattleHUD hud;
    private BattleCameraController battleCameraController;
    private NodeGraphSO graph;

    private readonly Dictionary<string, Vector2> resolvedMapPositions = new();
    private readonly Dictionary<string, ProceduralRoomLayout> roomLayouts = new();
    private readonly HashSet<string> visitedNodeIds = new();
    private readonly HashSet<Vector2Int> currentTargetLocalTiles = new();

    private Vector2Int currentBaseWorldTile;
    private CanvasGroup stageMapCanvasGroup;
    private Canvas stageMapCanvas;
    private RectTransform stageMapPanel;
    private Coroutine stageMapRevealRoutine;
    private Coroutine stageMapConfirmRoutine;
    private Vector2 stageMapPanelRestPosition;
    private bool stageMapSelectionLocked;
    private bool mapSelectionActive;
    private Button trackedStageMapButton;
    private Vector2 trackedStageMapPointerAnchor;
    private RectTransform routeStatusRoot;
    private Text routeStatusText;
    private CanvasGroup routeStatusGroup;
    private Coroutine mapDeniedRoutine;
    private AudioSource mapFeedbackAudio;
    private AudioClip mapDeniedFallbackClip;
    private static Sprite mapRatingStarSprite;
    private float resolvedMapHorizontalSpacing;
    private float resolvedMapVerticalSpacing;
    private float nextCharacterSizingCheck;

    public Vector3 CurrentBaseOriginWorld => baseTemplate != null
        ? baseTemplate.FixedTileOriginWorld
        : new Vector3(currentBaseWorldTile.x, currentBaseWorldTile.y, 0f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleSpatialMapController>() != null)
            return;

        GameObject host = new("BattleStageMapRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleSpatialMapController>();
    }

    private void OnEnable()
    {
        StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        Unsubscribe();
        HideStageMapImmediate();
    }

    private IEnumerator BindWhenReady()
    {
        while (runManager == null || hud == null)
        {
            ResolveSystems();
            if (runManager == null || hud == null)
                yield return null;
        }

        Subscribe();
        mapSelectionActive = runManager != null && runManager.WaitingForNodeSelection;
        EnsureStageMapUI();
        BuildResolvedLayout();
        RefreshStageMap();
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
        if (hud == null)
            hud = FindFirstObjectByType<BattleHUD>();
        if (battleCameraController == null)
            battleCameraController = FindFirstObjectByType<BattleCameraController>();

        if (runManager != null && graph == null)
        {
            FieldInfo field = typeof(BattleRunManager).GetField("nodeGraph", InstanceFields);
            graph = field != null ? field.GetValue(runManager) as NodeGraphSO : null;
        }
    }

    private void Subscribe()
    {
        if (runManager == null)
            return;

        runManager.NodeEntered -= HandleNodeEntered;
        runManager.NodeEntered += HandleNodeEntered;
        runManager.StateChanged -= HandleStateChanged;
        runManager.StateChanged += HandleStateChanged;
        runManager.NextNodeSelectionRequested -= HandleNextNodeSelectionRequested;
        runManager.NextNodeSelectionRequested += HandleNextNodeSelectionRequested;
        runManager.RunEnded -= HandleRunEnded;
        runManager.RunEnded += HandleRunEnded;
    }

    private void Unsubscribe()
    {
        if (runManager == null)
            return;

        runManager.NodeEntered -= HandleNodeEntered;
        runManager.StateChanged -= HandleStateChanged;
        runManager.NextNodeSelectionRequested -= HandleNextNodeSelectionRequested;
        runManager.RunEnded -= HandleRunEnded;
    }

    private void Update()
    {
        if (runManager == null || roomManager == null || graph == null || baseTemplate == null ||
            hud == null || stageMapPanel == null)
        {
            ResolveSystems();
            if (runManager != null)
            {
                Subscribe();
                EnsureStageMapUI();
                BuildResolvedLayout();
                RefreshStageMap();
            }
        }

        if (Time.unscaledTime >= nextCharacterSizingCheck)
        {
            nextCharacterSizingCheck = Time.unscaledTime + 0.35f;
            ApplyReadableDefaultCharacterSizes();
        }

        UpdateStageMapCursorTracking();
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        mapSelectionActive = false;
        if (node == null)
            return;

        visitedNodeIds.Add(node.id);
        if ((node.type == BattleNodeType.Combat || node.type == BattleNodeType.Elite) && node.room != null)
            PrepareProceduralRoomPresentation(node);

        RefreshStageMap();
    }

    private void HandleStateChanged(BattleRunState state)
    {
        mapSelectionActive =
            runManager != null &&
            runManager.RunActive &&
            state == BattleRunState.SelectingNode;
        RefreshStageMap();
    }

    private void HandleNextNodeSelectionRequested(IReadOnlyList<BattleNodeData> _)
    {
        mapSelectionActive = true;
        BuildResolvedLayout();
        RefreshStageMap();
    }

    private void HandleRunEnded(RunEndReason _)
    {
        mapSelectionActive = false;
        visitedNodeIds.Clear();
        roomLayouts.Clear();
        currentTargetLocalTiles.Clear();
        RefreshStageMap();
    }

    // ---------------------------------------------------------------------
    // Base-seeded Room generation
    // ---------------------------------------------------------------------

    private void PrepareProceduralRoomPresentation(BattleNodeData node)
    {
        ResolveSystems();
        RoomDefinitionSO room = node.room;
        if (room == null || roomManager == null || baseTemplate == null)
            return;

        baseTemplate.EnsurePersistentBase();
        Vector3 baseOriginWorld = baseTemplate.FixedTileOriginWorld;
        currentBaseWorldTile = WorldToTile(baseOriginWorld);
        SetRoomOrigin(baseOriginWorld);

        ProceduralRoomLayout layout = GetOrCreateLayout(node);
        if (layout == null || layout.targetCells.Count <= RoomBaseTemplate.FixedBaseTiles * RoomBaseTemplate.FixedBaseTiles)
            return;

        HashSet<Vector2Int> baseCells = CreateBaseCells();
        HashSet<Vector2Int> extensionCells = new(layout.targetCells);
        extensionCells.ExceptWith(baseCells);

        if (extensionCells.Count == 0)
        {
            Debug.LogError($"[BattleSpatial] Room '{room.roomId}' produced no extension cells outside the persistent 4x4 Base.");
            return;
        }

        int seed = StableHash(node.id) ^ StableHash(room.roomId) ^ room.proceduralSeed ^ 0x51A7;
        List<PiecePlan> plans = BuildFrontierAssemblyPlan(extensionCells, baseCells, seed);
        if (plans.Count == 0)
        {
            Debug.LogError($"[BattleSpatial] Room '{room.roomId}' could not build an extension plan from the 4x4 Base.");
            return;
        }

        List<MapBlockPlacement> oldBlocks = room.blocks;
        bool oldReposition = room.repositionPlayerOnEnter;
        Vector2 oldEntry = room.playerEntryOffset;

        List<MapBlockPlacement> runtimePlacements = new(plans.Count);
        List<GameObject> runtimePrototypes = new(plans.Count);

        for (int i = 0; i < plans.Count; i++)
        {
            PiecePlan plan = plans[i];
            float offscreenOffset = CalculateOffscreenEntryOffset(plan.cells, plan.entryDirection, baseOriginWorld, room.largePieceEntryOffset);
            MapBlock prototype = CreateExtensionPrototype(node, layout.targetCells, plan.cells, i, offscreenOffset);
            if (prototype == null)
                continue;

            runtimePrototypes.Add(prototype.gameObject);
            runtimePlacements.Add(new MapBlockPlacement
            {
                gridPosition = Vector2Int.zero,
                prefab = prototype,
                entryDirection = plan.entryDirection
            });
        }

        if (runtimePlacements.Count == 0)
        {
            DestroyPrototypeList(runtimePrototypes);
            return;
        }

        currentTargetLocalTiles.Clear();
        currentTargetLocalTiles.UnionWith(layout.targetCells);

        room.blocks = runtimePlacements;
        room.repositionPlayerOnEnter = false;
        room.playerEntryOffset = Vector2.zero;

        StartCoroutine(RestoreRoomDataNextFrame(room, oldBlocks, oldReposition, oldEntry, runtimePrototypes));
    }

    private void SetRoomOrigin(Vector3 baseOriginWorld)
    {
        if (roomManager == null || roomManager.RoomOrigin == null)
            return;

        Vector3 origin = baseOriginWorld;
        origin.z = roomManager.RoomOrigin.position.z;
        roomManager.RoomOrigin.position = origin;
    }

    private IEnumerator RestoreRoomDataNextFrame(
        RoomDefinitionSO room,
        List<MapBlockPlacement> blocks,
        bool reposition,
        Vector2 entry,
        List<GameObject> prototypes)
    {
        yield return null;

        if (room != null)
        {
            room.blocks = blocks ?? new List<MapBlockPlacement>();
            room.repositionPlayerOnEnter = reposition;
            room.playerEntryOffset = entry;
        }

        DestroyPrototypeList(prototypes);
    }

    private static void DestroyPrototypeList(List<GameObject> prototypes)
    {
        if (prototypes == null)
            return;

        for (int i = 0; i < prototypes.Count; i++)
        {
            if (prototypes[i] != null)
                Destroy(prototypes[i]);
        }
    }

    private ProceduralRoomLayout GetOrCreateLayout(BattleNodeData node)
    {
        if (node == null || node.room == null)
            return null;

        if (roomLayouts.TryGetValue(node.id, out ProceduralRoomLayout cached))
            return cached;

        ProceduralRoomLayout layout = GenerateBaseSeededLayout(node);
        roomLayouts[node.id] = layout;
        return layout;
    }

    private ProceduralRoomLayout GenerateBaseSeededLayout(BattleNodeData node)
    {
        RoomDefinitionSO room = node.room;
        Vector2Int min = room.GetProceduralMinTileSize();
        Vector2Int max = room.GetProceduralMaxTileSize();

        int seed = StableHash(node.id) ^ StableHash(room.roomId) ^ room.proceduralSeed;
        System.Random random = new(seed);

        Vector2Int size = room.useProceduralRoom
            ? new Vector2Int(ChooseDimension(min.x, max.x, random), ChooseDimension(min.y, max.y, random))
            : room.GetLargePieceGridSize();

        size.x = Mathf.Max(RoomDefinitionSO.MinimumCombatRoomTiles, size.x);
        size.y = Mathf.Max(RoomDefinitionSO.MinimumCombatRoomTiles, size.y);

        Vector2Int positiveBaseStart = new(
            Mathf.Max(0, (size.x - RoomBaseTemplate.FixedBaseTiles) / 2),
            Mathf.Max(0, (size.y - RoomBaseTemplate.FixedBaseTiles) / 2));

        RoomLargePieceShape shape = room.largePieceShape == RoomLargePieceShape.Auto
            ? ResolveAutoShape(random)
            : room.largePieceShape;

        HashSet<Vector2Int> positiveTarget = BuildShape(room, size, shape, positiveBaseStart, random);

        for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
        {
            for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                positiveTarget.Add(positiveBaseStart + new Vector2Int(x, y));
        }

        FillEnclosedHoles(positiveTarget, size);

        int chunk = Mathf.Max(RoomDefinitionSO.MinimumPassageTiles, room.GetMinimumRoomChunkTiles());
        if (positiveTarget.Count == 0 || !IsConnected(positiveTarget) || !EveryCellBelongsToChunk(positiveTarget, chunk))
        {
            Debug.LogWarning($"[BattleSpatial] Shape '{shape}' for '{room.roomId}' failed topology validation. Using a solid rectangle.");
            positiveTarget = BuildRectangle(size);
        }

        HashSet<Vector2Int> targetLocal = new();
        foreach (Vector2Int cell in positiveTarget)
            targetLocal.Add(cell - positiveBaseStart);

        HashSet<Vector2Int> baseCells = CreateBaseCells();
        if (!baseCells.IsSubsetOf(targetLocal))
        {
            targetLocal.Clear();
            foreach (Vector2Int cell in BuildRectangle(size))
                targetLocal.Add(cell - positiveBaseStart);
        }

        return new ProceduralRoomLayout(size, targetLocal, shape);
    }

    private static int ChooseDimension(int min, int max, System.Random random)
    {
        min = Mathf.Max(RoomDefinitionSO.MinimumCombatRoomTiles, min);
        max = Mathf.Max(min, max);
        return random.Next(min, max + 1);
    }

    private static RoomLargePieceShape ResolveAutoShape(System.Random random)
    {
        int roll = random.Next(100);
        if (roll < 22) return RoomLargePieceShape.Rectangle;
        if (roll < 42) return RoomLargePieceShape.LShape;
        if (roll < 62) return RoomLargePieceShape.TShape;
        if (roll < 78) return RoomLargePieceShape.Cross;
        return RoomLargePieceShape.Irregular;
    }

    private HashSet<Vector2Int> BuildShape(
        RoomDefinitionSO room,
        Vector2Int size,
        RoomLargePieceShape shape,
        Vector2Int baseStart,
        System.Random random)
    {
        if (shape == RoomLargePieceShape.Custom)
        {
            HashSet<Vector2Int> custom = new();
            if (room.customLargePieceCells != null)
            {
                for (int i = 0; i < room.customLargePieceCells.Count; i++)
                {
                    Vector2Int c = room.customLargePieceCells[i];
                    if (c.x >= 0 && c.y >= 0 && c.x < size.x && c.y < size.y)
                        custom.Add(c);
                }
            }
            return custom;
        }

        if (shape == RoomLargePieceShape.Rectangle)
            return BuildRectangle(size);

        int chunk = Mathf.Clamp(room.GetMinimumRoomChunkTiles(), RoomDefinitionSO.MinimumPassageTiles, Mathf.Min(size.x, size.y));
        int bandX = Mathf.Clamp(baseStart.x, 0, Mathf.Max(0, size.x - chunk));
        int bandY = Mathf.Clamp(baseStart.y, 0, Mathf.Max(0, size.y - chunk));
        HashSet<Vector2Int> cells = new();

        if (shape == RoomLargePieceShape.Cross)
        {
            AddRect(cells, 0, bandY, size.x, chunk, size);
            AddRect(cells, bandX, 0, chunk, size.y, size);
            return cells;
        }

        if (shape == RoomLargePieceShape.TShape)
        {
            bool barTop = random.Next(2) == 0;
            int barY = barTop ? size.y - chunk : 0;
            AddRect(cells, 0, barY, size.x, chunk, size);
            AddRect(cells, bandX, 0, chunk, size.y, size);
            return cells;
        }

        if (shape == RoomLargePieceShape.LShape)
        {
            bool horizontalRight = random.Next(2) == 0;
            bool verticalUp = random.Next(2) == 0;

            int hx = horizontalRight ? baseStart.x : 0;
            int hw = horizontalRight ? size.x - hx : Mathf.Min(size.x, baseStart.x + chunk);
            AddRect(cells, hx, bandY, hw, chunk, size);

            int vy = verticalUp ? baseStart.y : 0;
            int vh = verticalUp ? size.y - vy : Mathf.Min(size.y, baseStart.y + chunk);
            AddRect(cells, bandX, vy, chunk, vh, size);
            return cells;
        }

        cells = BuildRectangle(size);
        ApplySafeCornerNotches(cells, size, baseStart, chunk, room, random);
        return cells;
    }

    private static void AddRect(HashSet<Vector2Int> cells, int x, int y, int width, int height, Vector2Int bounds)
    {
        int minX = Mathf.Clamp(x, 0, bounds.x);
        int minY = Mathf.Clamp(y, 0, bounds.y);
        int maxX = Mathf.Clamp(x + width, 0, bounds.x);
        int maxY = Mathf.Clamp(y + height, 0, bounds.y);

        for (int py = minY; py < maxY; py++)
            for (int px = minX; px < maxX; px++)
                cells.Add(new Vector2Int(px, py));
    }

    private static HashSet<Vector2Int> BuildRectangle(Vector2Int size)
    {
        HashSet<Vector2Int> cells = new();
        for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
                cells.Add(new Vector2Int(x, y));
        return cells;
    }

    private static void ApplySafeCornerNotches(
        HashSet<Vector2Int> cells,
        Vector2Int size,
        Vector2Int baseStart,
        int chunk,
        RoomDefinitionSO room,
        System.Random random)
    {
        int attempts = 1 + Mathf.RoundToInt(room.proceduralComplexity * 2f);
        double chance = Mathf.Clamp01(room.proceduralIndentChance + 0.28f);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (random.NextDouble() > chance)
                continue;

            int corner = random.Next(4);
            int maxCutX = Mathf.Max(chunk, Mathf.Min(size.x / 3, size.x - chunk));
            int maxCutY = Mathf.Max(chunk, Mathf.Min(size.y / 3, size.y - chunk));
            int cutX = random.Next(chunk, maxCutX + 1);
            int cutY = random.Next(chunk, maxCutY + 1);

            for (int y = 0; y < cutY; y++)
            {
                for (int x = 0; x < cutX; x++)
                {
                    int px = (corner == 1 || corner == 3) ? size.x - 1 - x : x;
                    int py = corner >= 2 ? size.y - 1 - y : y;
                    Vector2Int c = new(px, py);
                    if (c.x >= baseStart.x - 1 && c.x <= baseStart.x + RoomBaseTemplate.FixedBaseTiles &&
                        c.y >= baseStart.y - 1 && c.y <= baseStart.y + RoomBaseTemplate.FixedBaseTiles)
                        continue;
                    cells.Remove(c);
                }
            }
        }
    }

    private static void FillEnclosedHoles(HashSet<Vector2Int> cells, Vector2Int size)
    {
        if (cells == null || size.x <= 0 || size.y <= 0)
            return;

        Queue<Vector2Int> queue = new();
        HashSet<Vector2Int> outsideEmpty = new();

        for (int x = 0; x < size.x; x++)
        {
            SeedOutside(new Vector2Int(x, 0), cells, outsideEmpty, queue, size);
            SeedOutside(new Vector2Int(x, size.y - 1), cells, outsideEmpty, queue, size);
        }
        for (int y = 0; y < size.y; y++)
        {
            SeedOutside(new Vector2Int(0, y), cells, outsideEmpty, queue, size);
            SeedOutside(new Vector2Int(size.x - 1, y), cells, outsideEmpty, queue, size);
        }

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            for (int i = 0; i < Cardinal.Length; i++)
                SeedOutside(current + Cardinal[i], cells, outsideEmpty, queue, size);
        }

        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                Vector2Int cell = new(x, y);
                if (!cells.Contains(cell) && !outsideEmpty.Contains(cell))
                    cells.Add(cell);
            }
        }
    }

    private static void SeedOutside(
        Vector2Int cell,
        HashSet<Vector2Int> cells,
        HashSet<Vector2Int> outside,
        Queue<Vector2Int> queue,
        Vector2Int size)
    {
        if (cell.x < 0 || cell.y < 0 || cell.x >= size.x || cell.y >= size.y)
            return;
        if (cells.Contains(cell) || !outside.Add(cell))
            return;
        queue.Enqueue(cell);
    }

    // ---------------------------------------------------------------------
    // Large extension-piece planning
    // ---------------------------------------------------------------------

    private List<PiecePlan> BuildFrontierAssemblyPlan(
        HashSet<Vector2Int> extensionCells,
        HashSet<Vector2Int> baseCells,
        int seed)
    {
        List<PiecePlan> result = new();
        if (extensionCells == null || extensionCells.Count == 0)
            return result;

        HashSet<Vector2Int> remaining = new(extensionCells);
        HashSet<Vector2Int> occupied = new(baseCells);
        System.Random random = new(seed);
        int maxSpan = Mathf.Clamp(maximumPieceTileSpan, 3, 8);
        int minCells = Mathf.Clamp(minimumPieceCellCount, 2, maxSpan * maxSpan);
        int safety = 0;

        while (remaining.Count > 0 && result.Count < maximumPieceCount && safety++ < 2048)
        {
            List<Vector2Int> frontier = CollectFrontierCells(remaining, occupied);
            if (frontier.Count == 0)
            {
                Debug.LogError("[BattleSpatial] Extension target became disconnected from the occupied 4x4 frontier.");
                break;
            }

            Vector2Int seedCell = ChooseFrontierSeed(frontier, remaining, random);
            HashSet<Vector2Int> piece = FindLargestFrontierRectangle(seedCell, remaining, maxSpan, minCells, random);
            if (piece.Count < minCells)
            {
                piece = GrowConnectedPiece(
                    seedCell,
                    remaining,
                    Mathf.Max(minCells, preferredPieceCellCount),
                    maxSpan,
                    random);
            }

            if (piece.Count == 0)
            {
                remaining.Remove(seedCell);
                continue;
            }

            Vector2 entryDirection = ResolveContactOutwardDirection(piece, occupied, result.Count);
            result.Add(new PiecePlan(piece, entryDirection));

            foreach (Vector2Int cell in piece)
            {
                remaining.Remove(cell);
                occupied.Add(cell);
            }
        }

        // If the piece cap was reached, continue with large connected chunks instead of creating single-cell fragments.
        while (remaining.Count > 0 && safety++ < 8192)
        {
            List<Vector2Int> frontier = CollectFrontierCells(remaining, occupied);
            if (frontier.Count == 0)
                break;

            Vector2Int seedCell = frontier[0];
            HashSet<Vector2Int> piece = GrowConnectedPiece(
                seedCell,
                remaining,
                Mathf.Max(preferredPieceCellCount, minCells),
                maxSpan,
                random);
            if (piece.Count == 0)
                piece.Add(seedCell);

            Vector2 entryDirection = ResolveContactOutwardDirection(piece, occupied, result.Count);
            result.Add(new PiecePlan(piece, entryDirection));
            foreach (Vector2Int cell in piece)
            {
                remaining.Remove(cell);
                occupied.Add(cell);
            }
        }

        MergeUndersizedPlans(result, minCells);
        return result;
    }

    private static HashSet<Vector2Int> FindLargestFrontierRectangle(
        Vector2Int seed,
        HashSet<Vector2Int> remaining,
        int maxSpan,
        int minimumCells,
        System.Random random)
    {
        HashSet<Vector2Int> best = new();
        int bestScore = -1;

        for (int width = 1; width <= maxSpan; width++)
        {
            for (int height = 1; height <= maxSpan; height++)
            {
                int area = width * height;
                if (area < minimumCells)
                    continue;

                for (int ox = seed.x - width + 1; ox <= seed.x; ox++)
                {
                    for (int oy = seed.y - height + 1; oy <= seed.y; oy++)
                    {
                        if (seed.x < ox || seed.x >= ox + width || seed.y < oy || seed.y >= oy + height)
                            continue;

                        bool full = true;
                        for (int y = 0; y < height && full; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                if (!remaining.Contains(new Vector2Int(ox + x, oy + y)))
                                {
                                    full = false;
                                    break;
                                }
                            }
                        }

                        if (!full)
                            continue;

                        int compactBonus = Mathf.Min(width, height) >= 2 ? 12 : 0;
                        int score = area * 10 + compactBonus + random.Next(0, 3);
                        if (score <= bestScore)
                            continue;

                        bestScore = score;
                        best.Clear();
                        for (int y = 0; y < height; y++)
                            for (int x = 0; x < width; x++)
                                best.Add(new Vector2Int(ox + x, oy + y));
                    }
                }
            }
        }

        return best;
    }

    private static List<Vector2Int> CollectFrontierCells(HashSet<Vector2Int> remaining, HashSet<Vector2Int> occupied)
    {
        List<Vector2Int> result = new();
        foreach (Vector2Int cell in remaining)
        {
            for (int i = 0; i < Cardinal.Length; i++)
            {
                if (!occupied.Contains(cell + Cardinal[i]))
                    continue;
                result.Add(cell);
                break;
            }
        }
        return result;
    }

    private static Vector2Int ChooseFrontierSeed(List<Vector2Int> frontier, HashSet<Vector2Int> remaining, System.Random random)
    {
        if (frontier == null || frontier.Count == 0)
            return Vector2Int.zero;

        List<Vector2Int> expandable = new();
        for (int i = 0; i < frontier.Count; i++)
        {
            Vector2Int cell = frontier[i];
            int neighbors = 0;
            for (int d = 0; d < Cardinal.Length; d++)
                if (remaining.Contains(cell + Cardinal[d]))
                    neighbors++;
            if (neighbors >= 2)
                expandable.Add(cell);
        }

        List<Vector2Int> source = expandable.Count > 0 ? expandable : frontier;
        return source[random.Next(source.Count)];
    }

    private static HashSet<Vector2Int> GrowConnectedPiece(
        Vector2Int start,
        HashSet<Vector2Int> remaining,
        int targetCount,
        int maximumSpan,
        System.Random random)
    {
        HashSet<Vector2Int> piece = new();
        if (!remaining.Contains(start))
            return piece;

        List<Vector2Int> candidates = new() { start };
        HashSet<Vector2Int> queued = new() { start };
        int minX = start.x;
        int maxX = start.x;
        int minY = start.y;
        int maxY = start.y;

        while (candidates.Count > 0 && piece.Count < Mathf.Max(1, targetCount))
        {
            int candidateIndex = random.Next(candidates.Count);
            Vector2Int current = candidates[candidateIndex];
            candidates.RemoveAt(candidateIndex);

            int nextMinX = Mathf.Min(minX, current.x);
            int nextMaxX = Mathf.Max(maxX, current.x);
            int nextMinY = Mathf.Min(minY, current.y);
            int nextMaxY = Mathf.Max(maxY, current.y);
            if (nextMaxX - nextMinX + 1 > maximumSpan || nextMaxY - nextMinY + 1 > maximumSpan)
                continue;
            if (!remaining.Contains(current))
                continue;

            piece.Add(current);
            minX = nextMinX;
            maxX = nextMaxX;
            minY = nextMinY;
            maxY = nextMaxY;

            for (int d = 0; d < Cardinal.Length; d++)
            {
                Vector2Int next = current + Cardinal[d];
                if (remaining.Contains(next) && queued.Add(next))
                    candidates.Add(next);
            }
        }

        return piece;
    }

    private static void MergeUndersizedPlans(List<PiecePlan> plans, int minimumCells)
    {
        if (plans == null || plans.Count <= 1)
            return;

        bool changed = true;
        int safety = 0;
        while (changed && safety++ < 512)
        {
            changed = false;
            for (int i = 0; i < plans.Count; i++)
            {
                PiecePlan small = plans[i];
                if (small == null || small.cells.Count >= minimumCells)
                    continue;

                int targetIndex = FindAdjacentPlan(plans, i);
                if (targetIndex < 0)
                    continue;

                plans[targetIndex].cells.UnionWith(small.cells);
                plans.RemoveAt(i);
                changed = true;
                break;
            }
        }
    }

    private static int FindAdjacentPlan(List<PiecePlan> plans, int sourceIndex)
    {
        PiecePlan source = plans[sourceIndex];
        int best = -1;
        int bestContacts = 0;

        for (int i = 0; i < plans.Count; i++)
        {
            if (i == sourceIndex || plans[i] == null)
                continue;

            int contacts = 0;
            foreach (Vector2Int cell in source.cells)
            {
                for (int d = 0; d < Cardinal.Length; d++)
                    if (plans[i].cells.Contains(cell + Cardinal[d]))
                        contacts++;
            }

            if (contacts > bestContacts)
            {
                bestContacts = contacts;
                best = i;
            }
        }

        return best;
    }

    private static Vector2 ResolveContactOutwardDirection(HashSet<Vector2Int> piece, HashSet<Vector2Int> occupied, int fallbackIndex)
    {
        Vector2 outward = Vector2.zero;
        int contacts = 0;

        foreach (Vector2Int cell in piece)
        {
            for (int i = 0; i < Cardinal.Length; i++)
            {
                Vector2Int towardOccupied = Cardinal[i];
                if (!occupied.Contains(cell + towardOccupied))
                    continue;
                outward += -(Vector2)towardOccupied;
                contacts++;
            }
        }

        if (contacts > 0 && outward.sqrMagnitude > 0.001f)
        {
            if (Mathf.Abs(outward.x) >= Mathf.Abs(outward.y))
                return outward.x >= 0f ? Vector2.right : Vector2.left;
            return outward.y >= 0f ? Vector2.up : Vector2.down;
        }

        Vector2 delta = CalculateCenter(piece) - new Vector2(1.5f, 1.5f);
        if (delta.sqrMagnitude > 0.001f)
        {
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
                return delta.x >= 0f ? Vector2.right : Vector2.left;
            return delta.y >= 0f ? Vector2.up : Vector2.down;
        }

        Vector2[] fallback = { Vector2.left, Vector2.right, Vector2.down, Vector2.up };
        return fallback[Mathf.Abs(fallbackIndex) % fallback.Length];
    }

    private float CalculateOffscreenEntryOffset(
        HashSet<Vector2Int> pieceCells,
        Vector2 outward,
        Vector3 baseOriginWorld,
        float authoredFallback)
    {
        float fallback = Mathf.Max(fallbackOffscreenEntryDistance, authoredFallback);
        if (pieceCells == null || pieceCells.Count == 0)
            return fallback;

        GetCellBounds(pieceCells, out int minX, out int minY, out int maxX, out int maxY);
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic)
            return fallback;

        float halfH = camera.orthographicSize;
        float halfW = halfH * Mathf.Max(0.1f, camera.aspect);
        Vector3 cameraCenter = camera.transform.position;
        float margin = Mathf.Max(0.5f, offscreenMargin) + 0.5f;
        float required = 0f;

        if (Mathf.Abs(outward.x) >= Mathf.Abs(outward.y))
        {
            if (outward.x >= 0f)
            {
                float screenRight = cameraCenter.x + halfW + margin;
                required = screenRight - (baseOriginWorld.x + minX - 0.5f);
            }
            else
            {
                float screenLeft = cameraCenter.x - halfW - margin;
                required = (baseOriginWorld.x + maxX + 0.5f) - screenLeft;
            }
        }
        else
        {
            if (outward.y >= 0f)
            {
                float screenTop = cameraCenter.y + halfH + margin;
                required = screenTop - (baseOriginWorld.y + minY - 0.5f);
            }
            else
            {
                float screenBottom = cameraCenter.y - halfH - margin;
                required = (baseOriginWorld.y + maxY + 0.5f) - screenBottom;
            }
        }

        return Mathf.Max(fallback, required);
    }

    private MapBlock CreateExtensionPrototype(
        BattleNodeData node,
        HashSet<Vector2Int> fullTargetCells,
        HashSet<Vector2Int> pieceCells,
        int pieceIndex,
        float entryOffset)
    {
        if (pieceCells == null || pieceCells.Count == 0)
            return null;

        GameObject root = new($"__RuntimeRoomPiecePrototype_{node.id}_{pieceIndex}");
        root.transform.position = new Vector3(10000f, 10000f, 0f);

        foreach (Vector2Int cell in pieceCells)
        {
            GameObject floor = new($"Tile_{cell.x}_{cell.y}");
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(cell.x, cell.y, 0f);

            SpriteRenderer renderer = floor.AddComponent<SpriteRenderer>();
            renderer.sprite = SpatialRuntimeSpriteCache.FloorTile32;
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.color = roomFloorColor;
            renderer.sortingOrder = -20;

            NavMeshModifier modifier = floor.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = false;
            modifier.overrideArea = false;
        }

        BuildOuterBoundaryForPiece(root.transform, fullTargetCells, pieceCells);

        MapBlock block = root.AddComponent<MapBlock>();
        block.ConfigureRuntimeDockingBlock(
            root.transform,
            true,
            roomImpactStrength,
            node.room.largePieceEntryDuration,
            entryOffset);
        return block;
    }

    private void BuildOuterBoundaryForPiece(Transform root, HashSet<Vector2Int> fullTargetCells, HashSet<Vector2Int> pieceCells)
    {
        foreach (Vector2Int cell in pieceCells)
        {
            for (int i = 0; i < Cardinal.Length; i++)
            {
                Vector2Int edge = Cardinal[i];
                if (fullTargetCells.Contains(cell + edge))
                    continue;
                CreateBoundaryEdge(root, cell, edge);
            }
        }
    }

    private void CreateBoundaryEdge(Transform root, Vector2Int cell, Vector2Int edge)
    {
        bool vertical = edge.x != 0;
        Vector2 size = vertical
            ? new Vector2(roomEdgeThickness, 1f + roomEdgeThickness)
            : new Vector2(1f + roomEdgeThickness, roomEdgeThickness);

        GameObject wall = new("RoomBoundaryCollider");
        wall.transform.SetParent(root, false);
        wall.transform.localPosition = (Vector2)cell + (Vector2)edge * 0.5f;

        // 회색 RoomEdge는 보이지 않게 하되,
        // 방 외곽 충돌과 NavMesh Not Walkable 처리는 그대로 유지합니다.
        BoxCollider2D collider = wall.AddComponent<BoxCollider2D>();
        collider.size = size;
        collider.isTrigger = false;

        NavMeshModifier modifier = wall.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = false;
        modifier.overrideArea = true;
        modifier.area = 1;
    }

    private static HashSet<Vector2Int> CreateBaseCells()
    {
        HashSet<Vector2Int> cells = new();
        for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
            for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                cells.Add(new Vector2Int(x, y));
        return cells;
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

    private static Vector2Int WorldToTile(Vector3 world)
    {
        return new Vector2Int(
            Mathf.RoundToInt(world.x / RoomBaseTemplate.TileWorldSize),
            Mathf.RoundToInt(world.y / RoomBaseTemplate.TileWorldSize));
    }

    private static void GetCellBounds(HashSet<Vector2Int> cells, out int minX, out int minY, out int maxX, out int maxY)
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

    private static bool EveryCellBelongsToChunk(HashSet<Vector2Int> cells, int chunk)
    {
        if (cells == null || cells.Count == 0)
            return false;

        chunk = Mathf.Max(RoomDefinitionSO.MinimumPassageTiles, chunk);
        foreach (Vector2Int cell in cells)
        {
            bool belongs = false;
            for (int oy = cell.y - chunk + 1; oy <= cell.y && !belongs; oy++)
            {
                for (int ox = cell.x - chunk + 1; ox <= cell.x && !belongs; ox++)
                {
                    bool full = true;
                    for (int y = 0; y < chunk && full; y++)
                    {
                        for (int x = 0; x < chunk; x++)
                        {
                            if (!cells.Contains(new Vector2Int(ox + x, oy + y)))
                            {
                                full = false;
                                break;
                            }
                        }
                    }
                    if (full)
                        belongs = true;
                }
            }
            if (!belongs)
                return false;
        }

        return true;
    }

    private static bool IsConnected(HashSet<Vector2Int> cells)
    {
        if (cells == null || cells.Count == 0)
            return false;

        Vector2Int first = default;
        foreach (Vector2Int cell in cells)
        {
            first = cell;
            break;
        }

        Queue<Vector2Int> queue = new();
        HashSet<Vector2Int> visited = new();
        queue.Enqueue(first);
        visited.Add(first);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            for (int i = 0; i < Cardinal.Length; i++)
            {
                Vector2Int next = current + Cardinal[i];
                if (cells.Contains(next) && visited.Add(next))
                    queue.Enqueue(next);
            }
        }

        return visited.Count == cells.Count;
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            int hash = 23;
            if (text != null)
                for (int i = 0; i < text.Length; i++)
                    hash = hash * 31 + text[i];
            return hash;
        }
    }

    // ---------------------------------------------------------------------
    // Horizontal Stage Map - selection state only
    // ---------------------------------------------------------------------

    private void EnsureStageMapUI()
    {
        if (stageMapPanel != null)
            return;

        ResolveSystems();
        if (hud == null || hud.MapSelectionRoot == null)
            return;

        // 맵은 HUD 전면 Overlay가 아니라 바닥/캐릭터 뒤의 World Space 화면에 그립니다.
        stageMapPanel = hud.MapSelectionRoot;
        stageMapCanvas = stageMapPanel.GetComponentInParent<Canvas>();
        stageMapCanvasGroup = stageMapPanel.GetComponent<CanvasGroup>();
        if (stageMapCanvasGroup == null)
            stageMapCanvasGroup = stageMapPanel.gameObject.AddComponent<CanvasGroup>();
        stageMapCanvasGroup.alpha = 0f;
        stageMapPanelRestPosition = stageMapPanel.anchoredPosition;
        resolvedMapHorizontalSpacing = mapHorizontalSpacing;
        resolvedMapVerticalSpacing = mapVerticalSpacing;
    }

    private void RefreshStageMap()
    {
        if (stageMapPanel == null)
            EnsureStageMapUI();
        if (stageMapPanel == null || graph == null)
            return;

        if (!mapSelectionActive)
        {
            HideStageMapImmediate();
            return;
        }

        bool reveal = !stageMapPanel.gameObject.activeSelf ||
                      stageMapCanvasGroup == null ||
                      stageMapCanvasGroup.alpha <= 0.001f;
        stageMapPanel.gameObject.SetActive(true);

        for (int i = stageMapPanel.childCount - 1; i >= 0; i--)
            Destroy(stageMapPanel.GetChild(i).gameObject);

        BuildResolvedLayout();
        CreateStageMapTitle();

        HashSet<string> available = new();
        if (runManager != null)
        {
            IReadOnlyList<BattleNodeData> choices = runManager.NextNodeChoices;
            for (int i = 0; i < choices.Count; i++)
                if (choices[i] != null)
                    available.Add(choices[i].id);
        }

        List<Vector2> positions = new();
        if (graph.nodes != null)
        {
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                BattleNodeData node = graph.nodes[i];
                if (node != null)
                    positions.Add(ResolveNodeMapPosition(node));
            }
        }

        Vector2 startMarkerPosition = ResolveStartBaseMapPosition(positions);
        positions.Add(startMarkerPosition);

        Vector2 mapCenter = CalculateMapCenter(positions);
        ResolveStageMapSpacing(positions);

        IReadOnlyList<BattleNodeData> startNodes = graph.GetStartNodes();
        for (int i = 0; i < startNodes.Count; i++)
        {
            BattleNodeData startNode = startNodes[i];
            if (startNode != null)
                DrawMapLink("__START__", startNode.id, startMarkerPosition, ResolveNodeMapPosition(startNode), mapCenter);
        }

        if (graph.nodes != null)
        {
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                BattleNodeData node = graph.nodes[i];
                if (node == null)
                    continue;

                List<BattleNodeData> next = graph.GetNextNodes(node);
                for (int n = 0; n < next.Count; n++)
                    DrawMapLink(node.id, next[n].id, ResolveNodeMapPosition(node), ResolveNodeMapPosition(next[n]), mapCenter);
            }
        }

        DrawStartBaseMarker(startMarkerPosition, mapCenter);

        if (graph.nodes == null)
            return;

        for (int i = 0; i < graph.nodes.Count; i++)
        {
            BattleNodeData node = graph.nodes[i];
            if (node == null)
                continue;

            bool selectable = available.Contains(node.id);
            bool current = runManager != null && runManager.CurrentNode == node;
            Color color = mapUnknown;
            if (current)
                color = mapCurrent;
            else if (selectable)
                color = node.type == BattleNodeType.Elite ? mapElite : mapAvailable;
            else if (visitedNodeIds.Contains(node.id))
                color = mapVisited;

            DrawStageNode(node, ResolveNodeMapPosition(node), color, mapCenter, selectable, current);
        }

        if (reveal)
            PlayStageMapReveal();
    }

    private void CreateStageMapTitle()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        GameObject title = new("Title");
        title.transform.SetParent(stageMapPanel, false);
        Text text = title.AddComponent<Text>();
        text.font = font;
        bool openingWaitingRoom = runManager != null && runManager.IsInStartArea;
        text.text = openingWaitingRoom
            ? "[  MAP SELECT  ]"
            : "CHOOSE THE NEXT TAKE";
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 28;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.raycastTarget = false;

        RectTransform rect = title.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -18f);
        rect.sizeDelta = new Vector2(0f, 42f);

        GameObject sub = new("Subtitle");
        sub.transform.SetParent(stageMapPanel, false);
        Text subText = sub.AddComponent<Text>();
        subText.font = font;
        subText.text = openingWaitingRoom
            ? "WAITING ROOM  /  CHOOSE YOUR FIRST STAGE"
            : "START  →  FINAL   /   CLICK ONE OF THE HIGHLIGHTED ROUTES";
        subText.alignment = TextAnchor.MiddleCenter;
        subText.fontSize = 12;
        subText.color = new Color(0.62f, 0.67f, 0.76f, 1f);
        subText.raycastTarget = false;

        RectTransform subRect = sub.GetComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0f, 1f);
        subRect.anchorMax = new Vector2(1f, 1f);
        subRect.pivot = new Vector2(0.5f, 1f);
        subRect.anchoredPosition = new Vector2(0f, -56f);
        subRect.sizeDelta = new Vector2(0f, 24f);
    }

    private void DrawStageNode(
        BattleNodeData node,
        Vector2 position,
        Color color,
        Vector2 mapCenter,
        bool selectable,
        bool current)
    {
        GameObject go = new($"StageNode_{node.id}");
        go.transform.SetParent(stageMapPanel, false);

        Image image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = selectable;

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = selectable
            ? new Color(mapAvailable.r, mapAvailable.g, mapAvailable.b, 0.82f)
            : current
                ? new Color(mapCurrent.r, mapCurrent.g, mapCurrent.b, 0.82f)
                : new Color(1f, 1f, 1f, 0.10f);
        outline.effectDistance = selectable || current
            ? new Vector2(2f, -2f)
            : new Vector2(1f, -1f);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(
            (position.x - mapCenter.x) * resolvedMapHorizontalSpacing,
            (position.y - mapCenter.y) * resolvedMapVerticalSpacing - 20f);
        float size = mapNodeSize * (node.type == BattleNodeType.Elite ? 1.18f : 1f);
        rect.sizeDelta = Vector2.one * size;

        BattleStageMapDeniedPointerRelay denyRelay =
            go.AddComponent<BattleStageMapDeniedPointerRelay>();
        denyRelay.Configure(this, rect, selectable, current);

        if (selectable && runManager != null)
        {
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            string id = node.id;
            button.onClick.AddListener(() => BeginStageNodeSelection(id, rect));
        }

        AddNodeLabel(go.transform, node, selectable, current);
    }

    private void AddNodeLabel(Transform parent, BattleNodeData node, bool selectable, bool current)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        bool rated = node.type == BattleNodeType.Combat || node.type == BattleNodeType.Elite;
        if (rated)
        {
            int stars = runManager != null
                ? runManager.ResolveBattleRatingStars(node)
                : node.GetBattleRatingStars();
            AddNodeRatingStars(parent, stars);
        }

        GameObject label = new("Label");
        label.transform.SetParent(parent, false);
        Text text = label.AddComponent<Text>();
        text.font = font;

        string type = node.type.ToString().ToUpperInvariant();
        string status = current ? "CURRENT" : selectable ? "AVAILABLE" : "LOCKED";
        text.text = $"{type}\n{status}";

        text.fontSize = selectable || current ? 11 : 9;
        text.fontStyle = selectable || current ? FontStyle.Bold : FontStyle.Normal;
        text.alignment = TextAnchor.UpperCenter;
        text.color = Color.white;
        text.raycastTarget = false;

        RectTransform rect = label.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = rated ? new Vector2(0f, -26f) : new Vector2(0f, -7f);
        rect.sizeDelta = rated ? new Vector2(120f, 38f) : new Vector2(120f, 46f);
    }

    private void AddNodeRatingStars(Transform parent, int activeStars)
    {
        GameObject row = new("RatingStars");
        row.transform.SetParent(parent, false);
        RectTransform rowRect = row.AddComponent<RectTransform>();
        rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 0f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -7f);
        rowRect.sizeDelta = new Vector2(82f, 15f);

        const float starSize = 13f;
        const float gap = 3f;
        float totalWidth = starSize * 5f + gap * 4f;
        float startX = -totalWidth * 0.5f + starSize * 0.5f;
        int clampedStars = Mathf.Clamp(activeStars, 1, 5);
        Sprite starSprite = GetMapRatingStarSprite();

        for (int i = 0; i < 5; i++)
        {
            GameObject starObject = new($"RatingStar_{i}");
            starObject.transform.SetParent(rowRect, false);
            RectTransform starRect = starObject.AddComponent<RectTransform>();
            starRect.anchorMin = starRect.anchorMax = new Vector2(0.5f, 0.5f);
            starRect.pivot = new Vector2(0.5f, 0.5f);
            starRect.sizeDelta = Vector2.one * starSize;
            starRect.anchoredPosition = new Vector2(startX + i * (starSize + gap), 0f);

            Image star = starObject.AddComponent<Image>();
            star.sprite = starSprite;
            star.preserveAspect = true;
            star.raycastTarget = false;
            star.color = i < clampedStars
                ? new Color(1f, 0.78f, 0.14f, 1f)
                : new Color(0.48f, 0.52f, 0.60f, 0.28f);
        }
    }

    private static Sprite GetMapRatingStarSprite()
    {
        if (mapRatingStarSprite != null)
            return mapRatingStarSprite;

        const int size = 24;
        Vector2 center = new((size - 1) * 0.5f, (size - 1) * 0.5f);
        Vector2[] polygon = new Vector2[10];
        for (int i = 0; i < polygon.Length; i++)
        {
            float radius = i % 2 == 0 ? 10.5f : 4.6f;
            float angle = (-90f + i * 36f) * Mathf.Deg2Rad;
            polygon[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inside = PointInPolygon(new Vector2(x + 0.5f, y + 0.5f), polygon);
                texture.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
        }

        texture.Apply(false, true);
        mapRatingStarSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size,
            0,
            SpriteMeshType.FullRect);
        mapRatingStarSprite.name = "RuntimeMapRatingStar";
        mapRatingStarSprite.hideFlags = HideFlags.HideAndDontSave;
        return mapRatingStarSprite;
    }

    private static bool PointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
    {
        bool inside = false;
        int j = polygon.Count - 1;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];
            bool crosses =
                (a.y > point.y) != (b.y > point.y) &&
                point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
            if (crosses)
                inside = !inside;
            j = i;
        }

        return inside;
    }

    private Vector2 ResolveStartBaseMapPosition(IReadOnlyList<Vector2> positions)
    {
        if (positions == null || positions.Count == 0)
            return new Vector2(-1f, 0f);

        float minX = positions[0].x;
        float sumY = 0f;
        for (int i = 0; i < positions.Count; i++)
        {
            minX = Mathf.Min(minX, positions[i].x);
            sumY += positions[i].y;
        }

        return new Vector2(minX - 1f, sumY / positions.Count);
    }

    private void DrawStartBaseMarker(Vector2 position, Vector2 mapCenter)
    {
        GameObject go = new("StageStartBase_4x4");
        go.transform.SetParent(stageMapPanel, false);

        Image image = go.AddComponent<Image>();
        bool current = runManager != null && runManager.IsInStartArea;
        image.color = current
            ? new Color(mapCurrent.r * 0.30f, mapCurrent.g * 0.30f, mapCurrent.b * 0.30f, 1f)
            : new Color(mapVisited.r * 0.30f, mapVisited.g * 0.30f, mapVisited.b * 0.30f, 0.92f);
        image.raycastTarget = false;

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = current ? mapCurrent : mapVisited;
        outline.effectDistance = new Vector2(2f, -2f);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(
            (position.x - mapCenter.x) * resolvedMapHorizontalSpacing,
            (position.y - mapCenter.y) * resolvedMapVerticalSpacing - 20f);
        rect.sizeDelta = new Vector2(92f, 62f);
        rect.localRotation = Quaternion.Euler(
            current ? 0.5f : 1.6f,
            current ? -1.2f : -3.4f,
            -0.3f);
        Vector3 local = rect.localPosition;
        local.z = current ? -8f : 8f;
        rect.localPosition = local;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        GameObject label = new("Label");
        label.transform.SetParent(rect, false);
        Text text = label.AddComponent<Text>();
        text.font = font;
        text.text = current
            ? "START\n4 x 4 BASE\nCURRENT"
            : "START\n4 x 4 BASE\nLOCKED";
        text.fontSize = 10;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;

        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(4f, 4f);
        labelRect.offsetMax = new Vector2(-4f, -4f);
    }

    private void BuildResolvedLayout()
    {
        if (graph == null || graph.nodes == null)
            return;

        resolvedMapPositions.Clear();
        SortedDictionary<int, List<BattleNodeData>> byDepth = new();

        for (int i = 0; i < graph.nodes.Count; i++)
        {
            BattleNodeData node = graph.nodes[i];
            if (node == null || string.IsNullOrWhiteSpace(node.id))
                continue;

            if (!byDepth.TryGetValue(node.depth, out List<BattleNodeData> list))
            {
                list = new List<BattleNodeData>();
                byDepth.Add(node.depth, list);
            }
            list.Add(node);
        }

        foreach (KeyValuePair<int, List<BattleNodeData>> pair in byDepth)
        {
            List<BattleNodeData> list = pair.Value;
            list.Sort((a, b) =>
            {
                if (a.useExplicitMapPosition && b.useExplicitMapPosition)
                    return a.mapPosition.x.CompareTo(b.mapPosition.x);
                if (a.useExplicitMapPosition != b.useExplicitMapPosition)
                    return a.useExplicitMapPosition ? -1 : 1;
                return string.CompareOrdinal(a.id, b.id);
            });

            float center = (list.Count - 1) * 0.5f;
            for (int i = 0; i < list.Count; i++)
            {
                BattleNodeData node = list[i];
                float x = node.depth;
                float y = node.useExplicitMapPosition ? node.mapPosition.x : center - i;
                resolvedMapPositions[node.id] = new Vector2(x, y);
            }
        }
    }

    private Vector2 ResolveNodeMapPosition(BattleNodeData node)
    {
        if (node == null)
            return Vector2.zero;
        if (resolvedMapPositions.TryGetValue(node.id, out Vector2 pos))
            return pos;
        return new Vector2(node.depth, node.useExplicitMapPosition ? node.mapPosition.x : 0f);
    }

    private static Vector2 CalculateMapCenter(List<Vector2> positions)
    {
        if (positions == null || positions.Count == 0)
            return Vector2.zero;

        float minX = positions[0].x;
        float maxX = positions[0].x;
        float minY = positions[0].y;
        float maxY = positions[0].y;
        for (int i = 1; i < positions.Count; i++)
        {
            minX = Mathf.Min(minX, positions[i].x);
            maxX = Mathf.Max(maxX, positions[i].x);
            minY = Mathf.Min(minY, positions[i].y);
            maxY = Mathf.Max(maxY, positions[i].y);
        }

        return new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
    }

    private void DrawMapLink(string fromId, string toId, Vector2 from, Vector2 to, Vector2 center)
    {
        Vector2 a = new(
            (from.x - center.x) * resolvedMapHorizontalSpacing,
            (from.y - center.y) * resolvedMapVerticalSpacing - 20f);
        Vector2 b = new(
            (to.x - center.x) * resolvedMapHorizontalSpacing,
            (to.y - center.y) * resolvedMapVerticalSpacing - 20f);
        Vector2 delta = b - a;
        float length = delta.magnitude;
        if (length < 1f)
            return;

        GameObject go = new("StageLink");
        go.transform.SetParent(stageMapPanel, false);
        Image image = go.AddComponent<Image>();
        image.color = mapLink;
        image.raycastTarget = false;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = (a + b) * 0.5f;
        rect.sizeDelta = new Vector2(length, 4f);
        rect.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

        BattleStageMapLinkVisual link = go.AddComponent<BattleStageMapLinkVisual>();
        link.Configure(fromId, toId, image, rect, a, b, mapLink);

        go.transform.SetAsFirstSibling();
    }

    private void ResolveStageMapSpacing(List<Vector2> positions)
    {
        resolvedMapHorizontalSpacing = mapHorizontalSpacing;
        resolvedMapVerticalSpacing = mapVerticalSpacing;
        if (positions == null || positions.Count < 2 || stageMapPanel == null)
            return;

        float minX = positions[0].x;
        float maxX = positions[0].x;
        float minY = positions[0].y;
        float maxY = positions[0].y;
        for (int i = 1; i < positions.Count; i++)
        {
            minX = Mathf.Min(minX, positions[i].x);
            maxX = Mathf.Max(maxX, positions[i].x);
            minY = Mathf.Min(minY, positions[i].y);
            maxY = Mathf.Max(maxY, positions[i].y);
        }

        float horizontalRange = maxX - minX;
        float verticalRange = maxY - minY;
        float panelWidth = stageMapPanel.rect.width > 1f ? stageMapPanel.rect.width : selectionMapSize.x;
        float panelHeight = stageMapPanel.rect.height > 1f ? stageMapPanel.rect.height : selectionMapSize.y;
        float usableWidth = Mathf.Max(1f, panelWidth - 160f);
        float usableHeight = Mathf.Max(1f, panelHeight - 170f);

        // Route topology should occupy the TV, not collapse into its center.
        // Keep conservative caps so large graphs still remain inside the safe area.
        if (horizontalRange > 0.001f)
        {
            float adaptive = usableWidth * 0.58f / horizontalRange;
            resolvedMapHorizontalSpacing = Mathf.Clamp(
                adaptive,
                Mathf.Max(120f, mapHorizontalSpacing),
                280f);
        }

        if (verticalRange > 0.001f)
        {
            float adaptive = usableHeight * 0.62f / verticalRange;
            resolvedMapVerticalSpacing = Mathf.Clamp(
                adaptive,
                Mathf.Max(96f, mapVerticalSpacing),
                180f);
        }
    }

    private void PlayStageMapReveal()
    {
        if (stageMapRevealRoutine != null)
            StopCoroutine(stageMapRevealRoutine);
        stageMapRevealRoutine = StartCoroutine(AnimateStageMapReveal());
    }

    private IEnumerator AnimateStageMapReveal()
    {
        if (stageMapCanvasGroup == null || stageMapPanel == null)
            yield break;

        float duration = Mathf.Max(0.05f, mapRevealDuration);
        float elapsed = 0f;
        Vector2 startPosition = stageMapPanelRestPosition + Vector2.right * mapRevealSlideDistance;
        stageMapCanvasGroup.alpha = 0f;
        stageMapPanel.anchoredPosition = startPosition;
        stageMapPanel.localScale = Vector3.one * 0.96f;
        stageMapPanel.localRotation = Quaternion.Euler(2.4f, -4.5f, 0.65f);
        Vector3 revealLocal = stageMapPanel.localPosition;
        revealLocal.z = 28f;
        stageMapPanel.localPosition = revealLocal;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            stageMapCanvasGroup.alpha = eased;
            stageMapPanel.anchoredPosition = Vector2.LerpUnclamped(startPosition, stageMapPanelRestPosition, eased);
            stageMapPanel.localScale = Vector3.LerpUnclamped(Vector3.one * 0.96f, Vector3.one, eased);
            stageMapPanel.localRotation = Quaternion.Slerp(
                Quaternion.Euler(2.4f, -4.5f, 0.65f),
                Quaternion.identity,
                eased);
            Vector3 local = stageMapPanel.localPosition;
            local.z = Mathf.Lerp(28f, 0f, eased);
            stageMapPanel.localPosition = local;
            yield return null;
        }

        stageMapCanvasGroup.alpha = 1f;
        stageMapPanel.anchoredPosition = stageMapPanelRestPosition;
        stageMapPanel.localScale = Vector3.one;
        stageMapPanel.localRotation = Quaternion.identity;
        Vector3 finalLocal = stageMapPanel.localPosition;
        finalLocal.z = 0f;
        stageMapPanel.localPosition = finalLocal;
        stageMapRevealRoutine = null;
    }

    private void UpdateStageMapCursorTracking()
    {
        if (battleCameraController == null)
            battleCameraController = FindFirstObjectByType<BattleCameraController>();

        if (!mapSelectionActive ||
            stageMapPanel == null ||
            stageMapRevealRoutine != null ||
            stageMapSelectionLocked ||
            !stageMapPanel.gameObject.activeInHierarchy)
        {
            ClearTrackedStageMapHover();
            battleCameraController?.SetMapCursorTracking(false, Vector2.zero);
            return;
        }

        Camera eventCamera = ResolveStageMapEventCamera();
        Vector2 pointer = Input.mousePosition;
        Button directHit = FindStageMapButtonUnderPointer(pointer, eventCamera);

        if (directHit != null)
        {
            SetTrackedStageMapButton(directHit, pointer);
        }
        else if (trackedStageMapButton != null)
        {
            bool canLatch =
                trackedStageMapButton.interactable &&
                trackedStageMapButton.gameObject.activeInHierarchy &&
                Vector2.Distance(pointer, trackedStageMapPointerAnchor) <=
                Mathf.Max(8f, mapHoverLatchPixels);

            if (!canLatch)
                ClearTrackedStageMapHover();
        }

        if (trackedStageMapButton == null)
        {
            battleCameraController?.SetMapCursorTracking(false, Vector2.zero);
            return;
        }

        RectTransform node = trackedStageMapButton.transform as RectTransform;
        if (node == null)
        {
            ClearTrackedStageMapHover();
            battleCameraController?.SetMapCursorTracking(false, Vector2.zero);
            return;
        }

        Vector2 localCenter = stageMapPanel.InverseTransformPoint(
            node.TransformPoint(node.rect.center));
        Rect panelRect = stageMapPanel.rect;
        Vector2 normalized = new(
            panelRect.width > 0.001f
                ? Mathf.Clamp(localCenter.x / (panelRect.width * 0.5f), -1f, 1f)
                : 0f,
            panelRect.height > 0.001f
                ? Mathf.Clamp(localCenter.y / (panelRect.height * 0.5f), -1f, 1f)
                : 0f);

        battleCameraController?.SetMapCursorTracking(true, normalized);
    }

    private Button FindStageMapButtonUnderPointer(Vector2 pointer, Camera eventCamera)
    {
        if (stageMapPanel == null)
            return null;

        Button[] buttons = stageMapPanel.GetComponentsInChildren<Button>(false);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null || !button.interactable)
                continue;

            RectTransform node = button.transform as RectTransform;
            if (node != null &&
                RectTransformUtility.RectangleContainsScreenPoint(node, pointer, eventCamera))
            {
                return button;
            }
        }

        return null;
    }

    private void SetTrackedStageMapButton(Button button, Vector2 pointer)
    {
        if (trackedStageMapButton != button)
        {
            SetTrackedStageMapVisual(trackedStageMapButton, false);
            trackedStageMapButton = button;
            SetTrackedStageMapVisual(trackedStageMapButton, true);
            NotifyPresenterPrototypeMapHover(trackedStageMapButton);
        }

        trackedStageMapPointerAnchor = pointer;
    }

    private void ClearTrackedStageMapHover()
    {
        SetTrackedStageMapVisual(trackedStageMapButton, false);
        trackedStageMapButton = null;
        trackedStageMapPointerAnchor = Vector2.zero;
    }

    private static void SetTrackedStageMapVisual(Button button, bool value)
    {
        if (button == null)
            return;

        BattleStageMapNodePointerFeedback feedback =
            button.GetComponent<BattleStageMapNodePointerFeedback>();
        feedback?.SetTrackedHover(value);
    }

    private void NotifyPresenterPrototypeMapHover(Button button)
    {
        if (button == null || graph == null)
            return;

        const string prefix = "StageNode_";
        string objectName = button.gameObject.name;
        if (string.IsNullOrEmpty(objectName) || !objectName.StartsWith(prefix, StringComparison.Ordinal))
            return;

        string nodeId = objectName.Substring(prefix.Length);
        BattleNodeData node = graph.FindNode(nodeId);
        if (node == null)
            return;

        int stars = runManager != null
            ? runManager.ResolveBattleRatingStars(node)
            : node.GetBattleRatingStars();

        BattleScreenPresenterPrototypeController.NotifyMapHover(node, stars);
    }

    private Camera ResolveStageMapEventCamera()
    {
        if (stageMapCanvas == null && stageMapPanel != null)
            stageMapCanvas = stageMapPanel.GetComponentInParent<Canvas>();

        if (stageMapCanvas == null ||
            stageMapCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        if (stageMapCanvas.worldCamera != null)
            return stageMapCanvas.worldCamera;

        return Camera.main;
    }

    private void BeginStageNodeSelection(string nodeId, RectTransform selectedNode)
    {
        if (stageMapSelectionLocked || runManager == null || !mapSelectionActive)
            return;

        BattleNodeData prototypeNode = graph != null ? graph.FindNode(nodeId) : null;
        if (prototypeNode != null)
        {
            BattleScreenPresenterPrototypeController.NotifyMapConfirm(
                prototypeNode,
                runManager.ResolveBattleRatingStars(prototypeNode));
        }

        stageMapSelectionLocked = true;
        ClearTrackedStageMapHover();
        battleCameraController?.SetMapCursorTracking(false, Vector2.zero);

        if (stageMapCanvasGroup != null)
            stageMapCanvasGroup.blocksRaycasts = false;
        if (stageMapConfirmRoutine != null)
            StopCoroutine(stageMapConfirmRoutine);
        stageMapConfirmRoutine = StartCoroutine(AnimateStageNodeSelection(nodeId, selectedNode));
    }

    private IEnumerator AnimateStageNodeSelection(string nodeId, RectTransform selectedNode)
    {
        if (stageMapRevealRoutine != null)
        {
            StopCoroutine(stageMapRevealRoutine);
            stageMapRevealRoutine = null;
        }

        BattleNodeData selectedNodeData = graph != null ? graph.FindNode(nodeId) : null;
        string routeFromId =
            runManager == null || runManager.IsInStartArea || runManager.CurrentNode == null
                ? "__START__"
                : runManager.CurrentNode.id;

        BattleStageMapLinkVisual[] links = stageMapPanel != null
            ? stageMapPanel.GetComponentsInChildren<BattleStageMapLinkVisual>(true)
            : Array.Empty<BattleStageMapLinkVisual>();

        BattleStageMapLinkVisual selectedLink = null;
        List<BattleStageMapLinkVisual> nonSelected = new();
        for (int i = 0; i < links.Length; i++)
        {
            BattleStageMapLinkVisual link = links[i];
            if (link == null)
                continue;

            if (link.Matches(routeFromId, nodeId))
                selectedLink = link;
            else if (link.StartsAt(routeFromId))
                nonSelected.Add(link);
        }

        float duration = Mathf.Max(0.15f, mapConfirmDuration);
        float elapsed = 0f;
        float boardBaseScale = stageMapPanel != null ? stageMapPanel.localScale.x : 1f;

        if (battleCameraController == null)
            battleCameraController = FindFirstObjectByType<BattleCameraController>();
        battleCameraController?.PlaySelectionConfirmShake(mapConfirmCameraShake, duration);

        while (elapsed < duration && stageMapPanel != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float decay = 1f - t;

            float boardPulse = Mathf.Lerp(boardBaseScale, 1f, t) +
                (Mathf.Max(1f, mapConfirmZoom) - 1f) *
                Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.7f)) * decay;
            stageMapPanel.localScale = Vector3.one * boardPulse;

            if (selectedNode != null)
            {
                float nodePulse = 1f + Mathf.Sin(Mathf.PI * t) * 0.12f;
                selectedNode.localScale = Vector3.one * nodePulse;
            }

            yield return null;
        }

        if (selectedNode != null)
            selectedNode.localScale = Vector3.one;
        if (stageMapPanel != null)
            stageMapPanel.localScale = Vector3.one;

        // Shut down every route that is not the committed branch.
        for (int i = 0; i < nonSelected.Count; i++)
        {
            if (nonSelected[i] != null)
                nonSelected[i].SetSuppressed(true);

            if (mapRouteShutdownStep > 0f)
                yield return WaitUnscaledSeconds(mapRouteShutdownStep);
        }

        EnsureRouteStatus();
        SetRouteStatus(
            true,
            selectedNodeData != null
                ? $"ROUTE LOCKED  /  STAGE {Mathf.Max(1, selectedNodeData.depth + 1):00}"
                : "ROUTE LOCKED");

        if (selectedLink != null)
        {
            float traceElapsed = 0f;
            float traceDuration = Mathf.Max(0.05f, mapRouteTraceDuration);
            while (traceElapsed < traceDuration)
            {
                traceElapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(traceElapsed / traceDuration);
                float eased = t * t * (3f - 2f * t);

                selectedLink.SetTrace(eased, mapRouteSelected);

                if (battleCameraController != null && stageMapPanel != null)
                {
                    Vector2 point = selectedLink.Evaluate(eased);
                    Rect rect = stageMapPanel.rect;
                    Vector2 normalized = new(
                        rect.width > 0.001f
                            ? Mathf.Clamp(point.x / (rect.width * 0.5f), -1f, 1f)
                            : 0f,
                        rect.height > 0.001f
                            ? Mathf.Clamp(point.y / (rect.height * 0.5f), -1f, 1f)
                            : 0f);

                    battleCameraController.SetMapCursorTracking(true, normalized * 0.55f);
                }

                yield return null;
            }

            selectedLink.SetTrace(1f, mapRouteSelected);
        }

        if (mapRouteLockHold > 0f)
            yield return WaitUnscaledSeconds(mapRouteLockHold);

        battleCameraController?.SetMapCursorTracking(false, Vector2.zero);

        if (stageMapPanel != null)
        {
            stageMapPanel.anchoredPosition = stageMapPanelRestPosition;
            stageMapPanel.localScale = Vector3.one;
        }

        SetRouteStatus(false, string.Empty);
        stageMapConfirmRoutine = null;

        // Existing RunManager/StageTransition remains the authoritative node-entry path.
        runManager?.SelectNextNode(nodeId);
    }

    private static IEnumerator WaitUnscaledSeconds(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void EnsureRouteStatus()
    {
        if (stageMapPanel == null || routeStatusRoot != null)
            return;

        GameObject root = new("RouteCommitStatus");
        root.transform.SetParent(stageMapPanel, false);
        routeStatusRoot = root.AddComponent<RectTransform>();
        routeStatusRoot.anchorMin = routeStatusRoot.anchorMax = new Vector2(0.5f, 0f);
        routeStatusRoot.pivot = new Vector2(0.5f, 0f);
        routeStatusRoot.anchoredPosition = new Vector2(0f, 26f);
        routeStatusRoot.sizeDelta = new Vector2(560f, 56f);

        Image back = root.AddComponent<Image>();
        back.color = new Color(0.02f, 0.025f, 0.035f, 0.96f);
        back.raycastTarget = false;

        Outline outline = root.AddComponent<Outline>();
        outline.effectColor = mapRouteSelected;
        outline.effectDistance = new Vector2(4f, -4f);
        outline.useGraphicAlpha = false;

        GameObject textObject = new("Text", typeof(RectTransform));
        textObject.transform.SetParent(root.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        routeStatusText = textObject.AddComponent<Text>();
        routeStatusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        routeStatusText.fontSize = 18;
        routeStatusText.fontStyle = FontStyle.Bold;
        routeStatusText.alignment = TextAnchor.MiddleCenter;
        routeStatusText.color = Color.white;
        routeStatusText.raycastTarget = false;

        routeStatusGroup = root.AddComponent<CanvasGroup>();
        routeStatusGroup.blocksRaycasts = false;
        routeStatusGroup.interactable = false;
        routeStatusRoot.gameObject.SetActive(false);
    }

    private void SetRouteStatus(bool visible, string message)
    {
        EnsureRouteStatus();
        if (routeStatusRoot == null)
            return;

        if (routeStatusText != null)
            routeStatusText.text = message ?? string.Empty;

        routeStatusRoot.gameObject.SetActive(visible);
        if (routeStatusGroup != null)
            routeStatusGroup.alpha = visible ? 1f : 0f;
        if (visible)
            routeStatusRoot.SetAsLastSibling();
    }

    internal void ShowMapDenied(RectTransform nodeRect)
    {
        if (!mapSelectionActive || stageMapSelectionLocked || nodeRect == null)
            return;

        if (mapDeniedRoutine != null)
            StopCoroutine(mapDeniedRoutine);
        mapDeniedRoutine = StartCoroutine(MapDeniedRoutine(nodeRect));
        PlayMapDeniedSound();
    }

    private IEnumerator MapDeniedRoutine(RectTransform nodeRect)
    {
        EnsureRouteStatus();
        SetRouteStatus(true, "ROUTE UNAVAILABLE");

        Vector2 basePosition = nodeRect.anchoredPosition;
        Vector3 baseScale = nodeRect.localScale;
        float elapsed = 0f;
        const float duration = 0.34f;

        while (elapsed < duration && nodeRect != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float envelope = Mathf.Pow(1f - t, 2f);
            float shake = Mathf.Sin(elapsed * 90f) * 7f * envelope;

            nodeRect.anchoredPosition = basePosition + new Vector2(shake, 0f);
            nodeRect.localScale = baseScale * (1f + 0.05f * envelope);
            yield return null;
        }

        if (nodeRect != null)
        {
            nodeRect.anchoredPosition = basePosition;
            nodeRect.localScale = baseScale;
        }

        yield return WaitUnscaledSeconds(0.30f);
        SetRouteStatus(false, string.Empty);
        mapDeniedRoutine = null;
    }

    private void PlayMapDeniedSound()
    {
        if (mapFeedbackAudio == null)
        {
            mapFeedbackAudio = GetComponent<AudioSource>();
            if (mapFeedbackAudio == null)
                mapFeedbackAudio = gameObject.AddComponent<AudioSource>();
            mapFeedbackAudio.playOnAwake = false;
            mapFeedbackAudio.loop = false;
            mapFeedbackAudio.spatialBlend = 0f;
            mapFeedbackAudio.volume = 0.25f;
        }

        if (mapDeniedFallbackClip == null)
        {
            const int sampleRate = 22050;
            const float duration = 0.08f;
            int sampleCount = Mathf.RoundToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            float phase = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)Mathf.Max(1, sampleCount - 1);
                float hz = Mathf.Lerp(150f, 105f, t);
                phase += Mathf.PI * 2f * hz / sampleRate;
                float envelope = Mathf.Pow(1f - t, 2f);
                samples[i] = Mathf.Sin(phase) * envelope * 0.28f;
            }

            mapDeniedFallbackClip = AudioClip.Create(
                "MapRouteDenied",
                sampleCount,
                1,
                sampleRate,
                false);
            mapDeniedFallbackClip.SetData(samples, 0);
        }

        mapFeedbackAudio.PlayOneShot(mapDeniedFallbackClip);
    }

    private void HideStageMapImmediate()
    {
        if (stageMapRevealRoutine != null)
            StopCoroutine(stageMapRevealRoutine);
        stageMapRevealRoutine = null;

        if (stageMapConfirmRoutine != null)
            StopCoroutine(stageMapConfirmRoutine);
        stageMapConfirmRoutine = null;
        stageMapSelectionLocked = false;

        if (mapDeniedRoutine != null)
            StopCoroutine(mapDeniedRoutine);
        mapDeniedRoutine = null;
        SetRouteStatus(false, string.Empty);

        ClearTrackedStageMapHover();

        if (stageMapCanvasGroup != null)
        {
            stageMapCanvasGroup.alpha = 0f;
            stageMapCanvasGroup.blocksRaycasts = true;
        }
        if (stageMapPanel != null)
        {
            stageMapPanel.anchoredPosition = stageMapPanelRestPosition;
            stageMapPanel.localScale = Vector3.one;
            stageMapPanel.localRotation = Quaternion.identity;
            Vector3 local = stageMapPanel.localPosition;
            local.z = 0f;
            stageMapPanel.localPosition = local;
            stageMapPanel.gameObject.SetActive(false);
        }
        battleCameraController?.SetMapCursorTracking(false, Vector2.zero);
    }

    // ---------------------------------------------------------------------
    // Test character sizing
    // ---------------------------------------------------------------------

    private void ApplyReadableDefaultCharacterSizes()
    {
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();

        if (player != null && player.spriteSO != null && player.spriteSO.name.StartsWith("TEST_", StringComparison.OrdinalIgnoreCase))
        {
            SpriteRenderer renderer = player.GetComponent<SpriteRenderer>();
            float scale = ResolveScaleForWorldHeight(renderer, testPlayerWorldHeight);
            player.transform.localScale = new Vector3(scale, scale, 1f);

            CircleCollider2D circle = player.GetComponent<CircleCollider2D>();
            if (circle != null)
                circle.radius = testPlayerWorldColliderRadius / Mathf.Max(0.01f, scale);
        }

        MonsterController[] monsters = FindObjectsByType<MonsterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < monsters.Length; i++)
        {
            MonsterController monster = monsters[i];
            if (monster == null || monster.Definition == null ||
                string.IsNullOrEmpty(monster.Definition.monsterId) ||
                !monster.Definition.monsterId.StartsWith("TEST_", StringComparison.OrdinalIgnoreCase))
                continue;

            SpriteRenderer renderer = monster.GetComponent<SpriteRenderer>();
            float scale = ResolveScaleForWorldHeight(renderer, testMonsterWorldHeight);
            monster.transform.localScale = new Vector3(scale, scale, 1f);

            CircleCollider2D circle = monster.GetComponent<CircleCollider2D>();
            if (circle != null)
                circle.radius = testMonsterWorldColliderRadius / Mathf.Max(0.01f, scale);

            NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
            if (agent != null)
                agent.radius = Mathf.Max(0.35f, testMonsterWorldColliderRadius * 0.75f);
        }
    }

    private static float ResolveScaleForWorldHeight(SpriteRenderer renderer, float targetHeight)
    {
        if (renderer == null || renderer.sprite == null)
            return Mathf.Max(0.1f, targetHeight);

        float spriteHeight = Mathf.Max(0.01f, renderer.sprite.bounds.size.y);
        return Mathf.Max(0.1f, targetHeight / spriteHeight);
    }

    private sealed class ProceduralRoomLayout
    {
        public readonly Vector2Int size;
        public readonly HashSet<Vector2Int> targetCells;
        public readonly RoomLargePieceShape shape;

        public ProceduralRoomLayout(Vector2Int size, HashSet<Vector2Int> targetCells, RoomLargePieceShape shape)
        {
            this.size = size;
            this.targetCells = targetCells ?? new HashSet<Vector2Int>();
            this.shape = shape;
        }
    }

    private sealed class PiecePlan
    {
        public readonly HashSet<Vector2Int> cells;
        public readonly Vector2 entryDirection;

        public PiecePlan(HashSet<Vector2Int> cells, Vector2 entryDirection)
        {
            this.cells = cells ?? new HashSet<Vector2Int>();
            this.entryDirection = entryDirection;
        }
    }
}

internal static class SpatialRuntimeSpriteCache
{
    private static Sprite floorTile32;
    private static Sprite solid32;

    public static Sprite FloorTile32 => floorTile32 != null ? floorTile32 : floorTile32 = CreateFloorTile32();
    public static Sprite Solid32 => solid32 != null ? solid32 : solid32 = CreateSolid32();

    private static Sprite CreateFloorTile32()
    {
        const int pixels = 32;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color inner = Color.white;
        Color edge = new(0.82f, 0.84f, 0.88f, 1f);
        for (int y = 0; y < pixels; y++)
        {
            for (int x = 0; x < pixels; x++)
            {
                bool line = x == 0 || y == 0;
                texture.SetPixel(x, y, line ? edge : inner);
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
        sprite.name = "RuntimeFloorTile_32px_1World";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static Sprite CreateSolid32()
    {
        const int pixels = 32;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] colors = new Color[pixels * pixels];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = Color.white;
        texture.SetPixels(colors);
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, pixels, pixels),
            new Vector2(0.5f, 0.5f),
            pixels,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "RuntimeSolid_32px_1World";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}

#if UNITY_EDITOR
[UnityEditor.InitializeOnLoad]
internal static class BattleStageSelectTestDefaultsEditor
{
    static BattleStageSelectTestDefaultsEditor()
    {
        UnityEditor.EditorApplication.delayCall += Apply;
    }

    private static void Apply()
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        ConfigureRoom("Assets/Resources/BattleTestDefaults/TEST_Room_A.asset", new Vector2Int(14, 15), new Vector2Int(18, 20));
        ConfigureRoom("Assets/Resources/BattleTestDefaults/TEST_Room_B.asset", new Vector2Int(15, 16), new Vector2Int(21, 20));
        ConfigureRoom("Assets/Resources/BattleTestDefaults/TEST_Room_ELITE.asset", new Vector2Int(17, 17), new Vector2Int(22, 22));

        UnityEditor.AssetDatabase.SaveAssets();
    }

    private static void ConfigureRoom(string path, Vector2Int min, Vector2Int max)
    {
        RoomDefinitionSO room = UnityEditor.AssetDatabase.LoadAssetAtPath<RoomDefinitionSO>(path);
        if (room == null)
            return;

        room.startBaseTileSize = new Vector2Int(4, 4);
        room.useProceduralRoom = true;
        room.useLargeRoomPiece = true;
        room.largePieceShape = RoomLargePieceShape.Auto;
        room.proceduralMinTileSize = min;
        room.proceduralMaxTileSize = max;
        room.proceduralMinChunkTileSize = Mathf.Max(RoomDefinitionSO.MinimumPassageTiles, 4);
        room.repositionPlayerOnEnter = false;
        UnityEditor.EditorUtility.SetDirty(room);
    }
}
#endif


internal sealed class BattleStageMapLinkVisual : MonoBehaviour
{
    private string fromId;
    private string toId;
    private Image image;
    private RectTransform rect;
    private Vector2 start;
    private Vector2 end;
    private Color baseColor;
    private Vector2 baseSize;
    private RectTransform pulseRect;
    private Image pulseImage;

    public void Configure(
        string sourceId,
        string destinationId,
        Image linkImage,
        RectTransform linkRect,
        Vector2 startPoint,
        Vector2 endPoint,
        Color color)
    {
        fromId = sourceId;
        toId = destinationId;
        image = linkImage;
        rect = linkRect;
        start = startPoint;
        end = endPoint;
        baseColor = color;
        baseSize = rect != null ? rect.sizeDelta : Vector2.zero;

        if (rect != null)
        {
            GameObject pulse = new("RouteTracePulse", typeof(RectTransform));
            pulse.transform.SetParent(rect, false);
            pulseRect = pulse.GetComponent<RectTransform>();
            pulseRect.anchorMin = pulseRect.anchorMax = new Vector2(0.5f, 0.5f);
            pulseRect.pivot = new Vector2(0.5f, 0.5f);
            pulseRect.sizeDelta = new Vector2(22f, 10f);
            pulseRect.anchoredPosition = new Vector2(-baseSize.x * 0.5f, 0f);

            pulseImage = pulse.AddComponent<Image>();
            pulseImage.raycastTarget = false;
            pulseImage.color = Color.white;
            pulse.SetActive(false);
        }
    }

    public bool Matches(string sourceId, string destinationId)
    {
        return string.Equals(fromId, sourceId, StringComparison.Ordinal) &&
               string.Equals(toId, destinationId, StringComparison.Ordinal);
    }

    public bool StartsAt(string sourceId)
    {
        return string.Equals(fromId, sourceId, StringComparison.Ordinal);
    }

    public Vector2 Evaluate(float t)
    {
        return Vector2.Lerp(start, end, Mathf.Clamp01(t));
    }

    public void SetSuppressed(bool suppressed)
    {
        if (image != null)
        {
            Color color = baseColor;
            color.a = suppressed ? 0.06f : baseColor.a;
            image.color = color;
        }

        if (rect != null)
        {
            Vector2 size = baseSize;
            size.y = suppressed ? 1f : Mathf.Max(1f, baseSize.y);
            rect.sizeDelta = size;
        }

        if (pulseRect != null)
            pulseRect.gameObject.SetActive(false);
    }

    public void SetTrace(float amount, Color selectedColor)
    {
        if (image != null)
        {
            Color color = Color.Lerp(baseColor, selectedColor, Mathf.Clamp01(amount));
            color.a = Mathf.Lerp(baseColor.a, 1f, Mathf.Clamp01(amount));
            image.color = color;
        }

        float t = Mathf.Clamp01(amount);
        if (rect != null)
        {
            Vector2 size = baseSize;
            size.y = Mathf.Lerp(Mathf.Max(1f, baseSize.y), 8f, t);
            rect.sizeDelta = size;
        }

        if (pulseRect != null && pulseImage != null)
        {
            pulseRect.gameObject.SetActive(t < 0.999f);
            pulseRect.anchoredPosition = new Vector2(
                Mathf.Lerp(-baseSize.x * 0.5f, baseSize.x * 0.5f, t),
                0f);
            pulseRect.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(t * Mathf.PI));
            pulseImage.color = selectedColor;
        }
    }
}

internal sealed class BattleStageMapDeniedPointerRelay : MonoBehaviour, IPointerClickHandler
{
    private BattleSpatialMapController owner;
    private RectTransform rect;
    private bool selectable;
    private bool current;

    public void Configure(
        BattleSpatialMapController controller,
        RectTransform nodeRect,
        bool canSelect,
        bool isCurrent)
    {
        owner = controller;
        rect = nodeRect;
        selectable = canSelect;
        current = isCurrent;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null ||
            eventData.button != PointerEventData.InputButton.Left ||
            selectable ||
            current)
        {
            return;
        }

        owner?.ShowMapDenied(rect);
    }
}

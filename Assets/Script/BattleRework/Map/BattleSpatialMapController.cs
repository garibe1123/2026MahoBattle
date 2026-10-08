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

    [Header("Script Selection Cards")]
    [SerializeField] private Vector2 scriptCardSize = new(218f, 302f);
    [SerializeField, Range(24f, 120f)] private float scriptCardGap = 52f;
    [SerializeField, Range(0f, 24f)] private float scriptIdleFloatPixels = 7f;
    [SerializeField, Range(0.03f, 0.40f)] private float scriptIdleCyclesPerSecond = 0.10f;
    [SerializeField, Range(1f, 24f)] private float scriptHoverLiftPixels = 10f;
    [SerializeField, Range(1f, 1.20f)] private float scriptHoverScale = 1.08f;
    [Tooltip("3D Y축 회전 대신 UI 메쉬를 사선으로 밀어 아이소 느낌을 냅니다. 카메라 방향 깊이는 전혀 바꾸지 않습니다.")]
    [SerializeField, Range(0f, 24f)] private float scriptHoverShearPixels = 11f;
    [SerializeField, Range(0f, 10f)] private float scriptPaperWavePixels = 4.2f;
    [SerializeField, Range(0.05f, 1.2f)] private float scriptPaperWaveCyclesPerSecond = 0.38f;
    [SerializeField, Range(0f, 1f)] private float scriptIdleWaveStrength = 0.24f;
    [SerializeField, Range(0f, 2f)] private float scriptHoverRollDegrees = 0.65f;

    [Header("Script Page Turn")]
    [SerializeField, Range(0.08f, 0.40f)] private float scriptPageTurnDuration = 0.18f;
    [SerializeField, Range(0.10f, 0.55f)] private float scriptPageReturnDuration = 0.24f;
    [UnityEngine.Serialization.FormerlySerializedAs("scriptPageCurlPixels")]
    [UnityEngine.Serialization.FormerlySerializedAs("scriptPageTurnSlidePixels")]
    [UnityEngine.Serialization.FormerlySerializedAs("scriptPageTurnLiftPixels")]
    [SerializeField, Range(18f, 72f)] private float scriptPageRollHeightPixels = 36f;
    [UnityEngine.Serialization.FormerlySerializedAs("scriptPageCurlDepthPixels")]
    [SerializeField, Range(3f, 24f)] private float scriptTurnedPageOffsetPixels = 10f;
    [UnityEngine.Serialization.FormerlySerializedAs("scriptPageTurnRollDegrees")]
    [SerializeField, Range(0f, 8f)] private float scriptTurnedPageRollDegrees = 2.2f;
    [SerializeField] private Color scriptPaperTint = new(0.93f, 0.89f, 0.79f, 1f);
    [SerializeField] private Color scriptInkColor = new(0.10f, 0.085f, 0.07f, 1f);
    [SerializeField] private Color scriptEliteAccent = new(0.64f, 0.11f, 0.15f, 1f);
    [SerializeField] private Color scriptNormalAccent = new(0.16f, 0.34f, 0.40f, 1f);

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
    private BattleInputRouter inputRouter;
    private BattleRunManager graphOwner;
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
    private static Sprite scriptPaperSprite;
    private static Sprite scriptSpotlightBeamSprite;
    private float resolvedMapHorizontalSpacing;
    private float resolvedMapVerticalSpacing;
    private float nextCharacterSizingCheck;

    public Vector3 CurrentBaseOriginWorld => baseTemplate != null
        ? baseTemplate.FixedTileOriginWorld
        : new Vector3(currentBaseWorldTile.x, currentBaseWorldTile.y, 0f);


    public bool TryGetHoveredScriptWorldPosition(
        out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;

        if (!mapSelectionActive ||
            trackedStageMapButton == null ||
            !trackedStageMapButton.gameObject.activeInHierarchy)
        {
            return false;
        }

        RectTransform rect =
            trackedStageMapButton.transform
            as RectTransform;

        if (rect == null)
            return false;

        worldPosition =
            rect.TransformPoint(
                rect.rect.center);

        return true;
    }

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
        if (inputRouter == null && Application.isPlaying)
            inputRouter = BattleInputRouter.ResolveOrCreate(this);

        RefreshGraphBinding();
    }

    private void RefreshGraphBinding()
    {
        NodeGraphSO nextGraph = null;
        if (runManager != null)
        {
            FieldInfo field = typeof(BattleRunManager).GetField("nodeGraph", InstanceFields);
            nextGraph = field != null ? field.GetValue(runManager) as NodeGraphSO : null;
        }

        if (graphOwner == runManager && graph == nextGraph)
            return;

        graphOwner = runManager;
        graph = nextGraph;

        resolvedMapPositions.Clear();
        roomLayouts.Clear();
        visitedNodeIds.Clear();
        currentTargetLocalTiles.Clear();
        mapSelectionActive = runManager != null && runManager.WaitingForNodeSelection;
        stageMapSelectionLocked = false;
        trackedStageMapButton = null;
        trackedStageMapPointerAnchor = Vector2.zero;
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
    // Script Selection - replaces the legacy route-map presentation
    // ---------------------------------------------------------------------

    private void EnsureStageMapUI()
    {
        if (stageMapPanel != null)
            return;

        ResolveSystems();
        if (hud == null || hud.MapSelectionRoot == null)
            return;

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
        if (stageMapPanel == null)
            return;

        if (!mapSelectionActive)
        {
            HideStageMapImmediate();
            return;
        }

        bool reveal =
            !stageMapPanel.gameObject.activeSelf ||
            stageMapCanvasGroup == null ||
            stageMapCanvasGroup.alpha <= 0.001f;

        stageMapPanel.gameObject.SetActive(true);
        stageMapSelectionLocked = false;

        for (int i = stageMapPanel.childCount - 1; i >= 0; i--)
            Destroy(stageMapPanel.GetChild(i).gameObject);

        routeStatusRoot = null;
        routeStatusText = null;
        routeStatusGroup = null;

        CreateScriptSelectionTitle();

        IReadOnlyList<BattleNodeData> choices =
            runManager != null
                ? runManager.NextNodeChoices
                : null;

        if ((choices == null || choices.Count == 0) &&
            graph != null &&
            runManager != null &&
            runManager.IsInStartArea)
        {
            choices = graph.GetStartNodes();
        }

        if (choices != null)
        {
            int validCount = 0;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i] != null)
                    validCount++;
            }

            int visualIndex = 0;
            for (int i = 0; i < choices.Count; i++)
            {
                BattleNodeData node = choices[i];
                if (node == null)
                    continue;

                DrawScriptCard(
                    node,
                    visualIndex,
                    Mathf.Max(1, validCount));

                visualIndex++;
            }
        }

        CreateScriptSelectionHint();

        if (reveal)
            PlayStageMapReveal();
        else if (stageMapCanvasGroup != null)
        {
            stageMapCanvasGroup.alpha = 1f;
            stageMapCanvasGroup.blocksRaycasts = true;
        }
    }

    private void CreateScriptSelectionTitle()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        // The old selection headline is intentionally removed.
        // Keep only a quiet context line so the floating paper stack is the visual focus.
        GameObject subtitle = new("Subtitle", typeof(RectTransform));
        subtitle.transform.SetParent(stageMapPanel, false);

        RectTransform subtitleRect = subtitle.GetComponent<RectTransform>();
        subtitleRect.anchorMin = new Vector2(0f, 1f);
        subtitleRect.anchorMax = new Vector2(1f, 1f);
        subtitleRect.pivot = new Vector2(0.5f, 1f);
        subtitleRect.anchoredPosition = new Vector2(0f, -22f);
        subtitleRect.sizeDelta = new Vector2(0f, 22f);

        Text subtitleText = subtitle.AddComponent<Text>();
        subtitleText.font = font;
        subtitleText.text =
            runManager != null && runManager.IsInStartArea
                ? "CHOOSE THE OPENING SCRIPT"
                : "CHOOSE THE NEXT SCRIPT";
        subtitleText.fontSize = 10;
        subtitleText.fontStyle = FontStyle.Bold;
        subtitleText.alignment = TextAnchor.MiddleCenter;
        subtitleText.color = new Color(0.58f, 0.61f, 0.66f, 0.84f);
        subtitleText.raycastTarget = false;
    }

    private void CreateScriptSelectionHint()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        GameObject hint = new("ScriptControlHint", typeof(RectTransform));
        hint.transform.SetParent(stageMapPanel, false);
        RectTransform rect = hint.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 16f);
        rect.sizeDelta = new Vector2(640f, 24f);

        Text text = hint.AddComponent<Text>();
        text.font = font;
        text.text = "HOVER = READ SCRIPT    /    CLICK = LOCK TAKE";
        text.fontSize = 10;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.56f, 0.72f, 0.75f, 0.92f);
        text.raycastTarget = false;
    }

    private void DrawScriptCard(
        BattleNodeData node,
        int index,
        int count)
    {
        if (node == null || stageMapPanel == null)
            return;

        float panelWidth =
            stageMapPanel.rect.width > 1f
                ? stageMapPanel.rect.width
                : selectionMapSize.x;

        float availableWidth =
            Mathf.Max(
                620f,
                panelWidth - 150f);

        float spacing =
            count <= 1
                ? 0f
                : Mathf.Min(
                    scriptCardSize.x + scriptCardGap,
                    availableWidth /
                    Mathf.Max(1, count - 1));

        float centerIndex =
            (count - 1) * 0.5f;

        float offsetIndex =
            index - centerIndex;

        Vector2 basePosition =
            new(
                offsetIndex * spacing,
                -28f -
                Mathf.Abs(offsetIndex) * 7f);

        GameObject rootObject =
            new(
                $"ScriptCard_{node.id}",
                typeof(RectTransform));

        rootObject.transform.SetParent(
            stageMapPanel,
            false);

        RectTransform root =
            rootObject.GetComponent<RectTransform>();

        root.anchorMin =
            root.anchorMax =
                new Vector2(0.5f, 0.5f);

        root.pivot =
            new Vector2(0.5f, 0.5f);

        root.sizeDelta =
            scriptCardSize;

        root.anchoredPosition =
            basePosition;

        Image hitTarget =
            rootObject.AddComponent<Image>();

        hitTarget.color =
            new Color(1f, 1f, 1f, 0.001f);

        hitTarget.raycastTarget = true;

        Button button =
            rootObject.AddComponent<Button>();

        button.targetGraphic =
            hitTarget;

        button.transition =
            Selectable.Transition.None;

        Navigation navigation =
            button.navigation;

        navigation.mode =
            Navigation.Mode.None;

        button.navigation =
            navigation;

        string id = node.id;

        button.onClick.AddListener(
            () =>
                BeginStageNodeSelection(
                    id,
                    root));

        Color accent =
            node.type == BattleNodeType.Elite
                ? scriptEliteAccent
                : scriptNormalAccent;

        RectTransform stageBase =
            CreateScriptStageImage(
                root,
                "ScriptStageBase",
                BattleHudSpriteCache.RoundedPanel,
                new Vector2(
                    scriptCardSize.x * 0.82f,
                    48f),
                new Vector2(
                    0f,
                    -scriptCardSize.y * 0.46f),
                new Color(
                    0.055f,
                    0.050f,
                    0.045f,
                    0.94f));

        RectTransform lightPool =
            CreateScriptStageImage(
                root,
                "ScriptLightPool",
                BattleHudSpriteCache.FloorSpotlight,
                new Vector2(
                    scriptCardSize.x * 1.02f,
                    74f),
                new Vector2(
                    0f,
                    -scriptCardSize.y * 0.44f),
                new Color(
                    1f,
                    0.90f,
                    0.66f,
                    0.20f));

        RectTransform lightBeam =
            CreateScriptStageImage(
                root,
                "ScriptSpotlightBeam",
                GetScriptSpotlightBeamSprite(),
                new Vector2(
                    scriptCardSize.x * 1.18f,
                    scriptCardSize.y * 1.34f),
                new Vector2(
                    0f,
                    8f),
                new Color(
                    1f,
                    0.94f,
                    0.76f,
                    0.15f));

        stageBase.SetAsFirstSibling();
        lightBeam.SetSiblingIndex(1);
        lightPool.SetSiblingIndex(2);

        GameObject visualObject =
            new(
                "PaperVisual",
                typeof(RectTransform));

        visualObject.transform.SetParent(
            root,
            false);

        RectTransform visualRoot =
            visualObject.GetComponent<RectTransform>();

        visualRoot.anchorMin =
            visualRoot.anchorMax =
                new Vector2(0.5f, 0.5f);

        visualRoot.pivot =
            new Vector2(0.5f, 0.5f);

        visualRoot.sizeDelta =
            scriptCardSize;

        visualRoot.anchoredPosition =
            Vector2.zero;

        CanvasGroup visualGroup =
            visualObject.AddComponent<CanvasGroup>();

        visualGroup.blocksRaycasts = false;
        visualGroup.interactable = false;

        RectTransform shadow =
            CreateScriptPage(
                visualRoot,
                "PaperShadow",
                new Color(0f, 0f, 0f, 0.28f),
                new Vector2(11f, -13f),
                -0.6f);

        RectTransform[] paperStack =
            new RectTransform[6];

        for (int i = 0; i < paperStack.Length; i++)
        {
            float depth01 =
                i /
                Mathf.Max(
                    1f,
                    paperStack.Length - 1f);

            Vector2 offset =
                new(
                    2.0f +
                    i * 1.15f,
                    -1.7f -
                    i * 1.25f);

            float rotation =
                Mathf.Lerp(
                    -0.55f,
                    0.75f,
                    depth01);

            Color paperLayerColor =
                Color.Lerp(
                    scriptPaperTint,
                    new Color(
                        0.78f,
                        0.75f,
                        0.68f,
                        1f),
                    0.08f +
                    depth01 * 0.13f);

            paperStack[i] =
                CreateScriptPage(
                    visualRoot,
                    $"PaperStack_{i:00}",
                    paperLayerColor,
                    offset,
                    rotation);
        }

        RectTransform backB =
            paperStack[
                paperStack.Length - 1];

        RectTransform backA =
            paperStack[
                paperStack.Length - 2];

        CreateScriptPaperThicknessEdges(
            visualRoot,
            paperStack.Length);

        RectTransform turnedPageBack =
            CreateScriptPage(
                visualRoot,
                "TurnedPageBack",
                Color.Lerp(
                    scriptPaperTint,
                    Color.white,
                    0.025f),
                new Vector2(
                    4f,
                    7f),
                -1.4f);

        CanvasGroup turnedPageBackGroup =
            turnedPageBack.gameObject.AddComponent<CanvasGroup>();

        turnedPageBackGroup.alpha = 0f;
        turnedPageBackGroup.interactable = false;
        turnedPageBackGroup.blocksRaycasts = false;

        RectTransform detail =
            CreateScriptPage(
                visualRoot,
                "DetailPage",
                Color.Lerp(
                    scriptPaperTint,
                    Color.white,
                    0.035f),
                new Vector2(
                    0.8f,
                    -0.8f),
                0.15f);

        GameObject coverMaskObject =
            new(
                "CoverClip",
                typeof(RectTransform));

        coverMaskObject.transform.SetParent(
            visualRoot,
            false);

        RectTransform coverMask =
            coverMaskObject.GetComponent<RectTransform>();

        coverMask.anchorMin =
            coverMask.anchorMax =
                new Vector2(
                    0.5f,
                    0.5f);

        coverMask.pivot =
            new Vector2(
                0.5f,
                1f);

        coverMask.sizeDelta =
            scriptCardSize;

        coverMask.anchoredPosition =
            new Vector2(
                0f,
                scriptCardSize.y * 0.5f);

        coverMaskObject.AddComponent<RectMask2D>();

        RectTransform front =
            CreateScriptPage(
                coverMask,
                "CoverPage",
                scriptPaperTint,
                Vector2.zero,
                0f);

        front.anchorMin =
            front.anchorMax =
                new Vector2(
                    0.5f,
                    1f);

        front.pivot =
            new Vector2(
                0.5f,
                1f);

        front.sizeDelta =
            scriptCardSize;

        front.anchoredPosition =
            Vector2.zero;

        shadow.SetAsFirstSibling();

        for (int i = 0; i < paperStack.Length; i++)
        {
            paperStack[i].SetSiblingIndex(
                1 + i);
        }

        turnedPageBack.SetSiblingIndex(
            1 + paperStack.Length);

        detail.SetSiblingIndex(
            2 + paperStack.Length);

        coverMask.SetAsLastSibling();

        Outline frontOutline =
            front.gameObject.AddComponent<Outline>();

        frontOutline.effectColor =
            new Color(
                accent.r,
                accent.g,
                accent.b,
                0.38f);

        frontOutline.effectDistance =
            new Vector2(2f, -2f);

        frontOutline.useGraphicAlpha = false;

        CreateScriptClip(front);
        CreateScriptCardContent(front, node, accent);
        CreateScriptDetailContent(detail, node, accent);

        BattleScriptPageTurnRig pageTurnRig =
            CreateScriptPageTurnRig(
                visualRoot,
                coverMask,
                front,
                detail,
                turnedPageBack,
                turnedPageBackGroup,
                scriptPageRollHeightPixels,
                scriptTurnedPageOffsetPixels,
                scriptTurnedPageRollDegrees);

        float side =
            Mathf.Abs(basePosition.x) < 0.01f
                ? 1f
                : Mathf.Sign(basePosition.x);

        float hoverShear =
            -side *
            Mathf.Abs(
                scriptHoverShearPixels);

        float hoverRoll =
            -side *
            Mathf.Abs(
                scriptHoverRollDegrees);

        float phase =
            Mathf.Abs(
                StableHash(node.id) % 1009) /
            1009f *
            Mathf.PI *
            2f;

        BattleScriptCardVisual visual =
            rootObject.AddComponent<BattleScriptCardVisual>();

        visual.Configure(
            visualRoot,
            front,
            detail,
            pageTurnRig,
            backA,
            backB,
            stageBase,
            lightPool,
            lightBeam,
            visualGroup,
            phase,
            scriptIdleFloatPixels,
            scriptIdleCyclesPerSecond,
            scriptHoverLiftPixels,
            scriptHoverScale,
            hoverShear,
            hoverRoll,
            scriptPaperWavePixels,
            scriptPaperWaveCyclesPerSecond,
            scriptIdleWaveStrength,
            scriptPageTurnDuration,
            scriptPageReturnDuration,
            accent);
    }

    private BattleScriptPageTurnRig CreateScriptPageTurnRig(
        RectTransform visualRoot,
        RectTransform coverMask,
        RectTransform coverPage,
        RectTransform detailPage,
        RectTransform turnedPageBack,
        CanvasGroup turnedPageBackGroup,
        float rollHeight,
        float turnedOffset,
        float turnedRoll)
    {
        GameObject curlRootObject =
            new(
                "PageTurnCurl",
                typeof(RectTransform));

        curlRootObject.transform.SetParent(
            visualRoot,
            false);

        RectTransform curlRoot =
            curlRootObject.GetComponent<RectTransform>();

        curlRoot.anchorMin =
            curlRoot.anchorMax =
                new Vector2(
                    0.5f,
                    0.5f);

        curlRoot.pivot =
            new Vector2(
                    0.5f,
                    0.5f);

        curlRoot.sizeDelta =
            scriptCardSize;

        curlRoot.anchoredPosition =
            Vector2.zero;

        CanvasGroup curlGroup =
            curlRootObject.AddComponent<CanvasGroup>();

        curlGroup.alpha = 0f;
        curlGroup.interactable = false;
        curlGroup.blocksRaycasts = false;

        const int stripCount = 9;

        for (int i = 0; i < stripCount; i++)
        {
            GameObject stripObject =
                new(
                    $"CurlStrip_{i:00}",
                    typeof(RectTransform));

            stripObject.transform.SetParent(
                curlRoot,
                false);

            RectTransform strip =
                stripObject.GetComponent<RectTransform>();

            strip.anchorMin =
                strip.anchorMax =
                    new Vector2(
                        0.5f,
                        0.5f);

            strip.pivot =
                new Vector2(
                    0.5f,
                    0.5f);

            strip.sizeDelta =
                new Vector2(
                    scriptCardSize.x,
                    Mathf.Max(
                        3f,
                        rollHeight /
                        stripCount *
                        1.35f));

            Image image =
                stripObject.AddComponent<Image>();

            image.sprite =
                GetScriptPaperSprite();

            image.type =
                Image.Type.Simple;

            float depth01 =
                i /
                (float)(
                    stripCount - 1);

            float shade =
                Mathf.Lerp(
                    0.72f,
                    1.02f,
                    Mathf.Sin(
                        depth01 *
                        Mathf.PI));

            image.color =
                new Color(
                    scriptPaperTint.r * shade,
                    scriptPaperTint.g * shade,
                    scriptPaperTint.b * shade,
                    1f);

            image.raycastTarget = false;
        }

        RectTransform curlShadow =
            CreateScriptStageImage(
                curlRoot,
                "CurlShadow",
                null,
                new Vector2(
                    scriptCardSize.x * 0.94f,
                    8f),
                Vector2.zero,
                new Color(
                    0f,
                    0f,
                    0f,
                    0.20f));

        curlShadow.SetAsFirstSibling();

        BattleScriptPageTurnRig rig =
            visualRoot.gameObject.AddComponent<BattleScriptPageTurnRig>();

        rig.Configure(
            visualRoot,
            coverMask,
            coverPage,
            detailPage,
            curlRoot,
            curlGroup,
            curlShadow,
            turnedPageBack,
            turnedPageBackGroup,
            scriptCardSize,
            rollHeight,
            turnedOffset,
            turnedRoll);

        return rig;
    }

    private static RectTransform CreateScriptStageImage(
        Transform parent,
        string name,
        Sprite sprite,
        Vector2 size,
        Vector2 anchoredPosition,
        Color color)
    {
        GameObject go =
            new(
                name,
                typeof(RectTransform));

        go.transform.SetParent(
            parent,
            false);

        RectTransform rect =
            go.GetComponent<RectTransform>();

        rect.anchorMin =
            rect.anchorMax =
                new Vector2(0.5f, 0.5f);

        rect.pivot =
            new Vector2(0.5f, 0.5f);

        rect.sizeDelta =
            size;

        rect.anchoredPosition =
            anchoredPosition;

        Image image =
            go.AddComponent<Image>();

        image.sprite =
            sprite;

        image.preserveAspect =
            false;

        image.color =
            color;

        image.raycastTarget =
            false;

        return rect;
    }

    private void CreateScriptPaperThicknessEdges(
        Transform parent,
        int sheetCount)
    {
        float thickness =
            Mathf.Clamp(
                sheetCount * 1.15f,
                5f,
                10f);

        RectTransform bottomEdge =
            CreateScriptStageImage(
                parent,
                "PaperStackBottomEdge",
                BattleHudSpriteCache.RoundedPanel,
                new Vector2(
                    scriptCardSize.x - 7f,
                    thickness),
                new Vector2(
                    4.2f,
                    -scriptCardSize.y * 0.5f -
                    thickness * 0.12f),
                new Color(
                    0.68f,
                    0.65f,
                    0.58f,
                    0.92f));

        bottomEdge.pivot =
            new Vector2(
                0.5f,
                0.5f);

        RectTransform rightEdge =
            CreateScriptStageImage(
                parent,
                "PaperStackRightEdge",
                BattleHudSpriteCache.RoundedPanel,
                new Vector2(
                    thickness,
                    scriptCardSize.y - 8f),
                new Vector2(
                    scriptCardSize.x * 0.5f +
                    thickness * 0.04f,
                    -4.4f),
                new Color(
                    0.62f,
                    0.60f,
                    0.54f,
                    0.88f));

        rightEdge.pivot =
            new Vector2(
                0.5f,
                0.5f);

        bottomEdge.SetAsFirstSibling();
        rightEdge.SetAsFirstSibling();
    }

    private RectTransform CreateScriptPage(
        Transform parent,
        string name,
        Color color,
        Vector2 offset,
        float rotation)
    {
        GameObject page =
            new(
                name,
                typeof(RectTransform));

        page.transform.SetParent(
            parent,
            false);

        RectTransform rect =
            page.GetComponent<RectTransform>();

        rect.anchorMin =
            rect.anchorMax =
                new Vector2(0.5f, 0.5f);

        rect.pivot =
            new Vector2(0.5f, 0.5f);

        rect.sizeDelta =
            scriptCardSize;

        rect.anchoredPosition =
            offset;

        rect.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                rotation);

        Image image =
            page.AddComponent<Image>();

        image.sprite =
            GetScriptPaperSprite();

        image.type =
            Image.Type.Simple;

        image.color =
            color;

        image.raycastTarget = false;

        return rect;
    }

    private void CreateScriptClip(
        RectTransform front)
    {
        GameObject clip =
            new(
                "ScriptClip",
                typeof(RectTransform));

        clip.transform.SetParent(
            front,
            false);

        RectTransform rect =
            clip.GetComponent<RectTransform>();

        rect.anchorMin =
            rect.anchorMax =
                new Vector2(0.5f, 1f);

        rect.pivot =
            new Vector2(0.5f, 1f);

        rect.anchoredPosition =
            new Vector2(0f, 8f);

        rect.sizeDelta =
            new Vector2(28f, 18f);

        Image image =
            clip.AddComponent<Image>();

        image.sprite =
            GetScriptPaperSprite();

        image.color =
            new Color(0.24f, 0.20f, 0.14f, 1f);

        image.raycastTarget = false;
    }

    private void CreateScriptCardContent(
        RectTransform front,
        BattleNodeData node,
        Color accent)
    {
        Font font =
            Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");

        if (font == null)
            return;

        string title =
            ResolveScriptTitle(node);

        string subtitle =
            ResolveScriptSubtitle(node);

        Text take =
            CreateScriptText(
                front,
                "TakeNumber",
                $"TAKE {Mathf.Max(1, node.depth + 1):00}",
                10,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                new Color(
                    scriptInkColor.r,
                    scriptInkColor.g,
                    scriptInkColor.b,
                    0.66f));

        RectTransform takeRect =
            take.rectTransform;

        takeRect.anchorMin =
            takeRect.anchorMax =
                new Vector2(0.5f, 1f);

        takeRect.pivot =
            new Vector2(0.5f, 1f);

        takeRect.anchoredPosition =
            new Vector2(0f, -18f);

        takeRect.sizeDelta =
            new Vector2(
                scriptCardSize.x - 30f,
                18f);

        Text titleText =
            CreateScriptText(
                front,
                "ScriptTitle",
                title,
                20,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                scriptInkColor);

        RectTransform titleRect =
            titleText.rectTransform;

        titleRect.anchorMin =
            titleRect.anchorMax =
                new Vector2(0.5f, 1f);

        titleRect.pivot =
            new Vector2(0.5f, 1f);

        titleRect.anchoredPosition =
            new Vector2(0f, -46f);

        titleRect.sizeDelta =
            new Vector2(
                scriptCardSize.x - 28f,
                54f);

        titleText.resizeTextForBestFit = true;
        titleText.resizeTextMinSize = 12;
        titleText.resizeTextMaxSize = 20;

        Text subtitleText =
            CreateScriptText(
                front,
                "ScriptSubtitle",
                subtitle,
                9,
                FontStyle.Italic,
                TextAnchor.MiddleCenter,
                new Color(
                    scriptInkColor.r,
                    scriptInkColor.g,
                    scriptInkColor.b,
                    0.72f));

        RectTransform subtitleRect =
            subtitleText.rectTransform;

        subtitleRect.anchorMin =
            subtitleRect.anchorMax =
                new Vector2(0.5f, 1f);

        subtitleRect.pivot =
            new Vector2(0.5f, 1f);

        subtitleRect.anchoredPosition =
            new Vector2(0f, -98f);

        subtitleRect.sizeDelta =
            new Vector2(
                scriptCardSize.x - 34f,
                26f);

        GameObject coverObject =
            new(
                "ScriptImage",
                typeof(RectTransform));

        coverObject.transform.SetParent(
            front,
            false);

        RectTransform coverRect =
            coverObject.GetComponent<RectTransform>();

        coverRect.anchorMin =
            coverRect.anchorMax =
                new Vector2(0.5f, 0.5f);

        coverRect.pivot =
            new Vector2(0.5f, 0.5f);

        coverRect.anchoredPosition =
            new Vector2(0f, 4f);

        coverRect.sizeDelta =
            new Vector2(
                scriptCardSize.x - 42f,
                112f);

        Image cover =
            coverObject.AddComponent<Image>();

        cover.raycastTarget = false;

        if (node.scriptImage != null)
        {
            cover.sprite =
                node.scriptImage;

            cover.preserveAspect = true;
            cover.color = Color.white;
        }
        else
        {
            cover.sprite =
                GetScriptPaperSprite();

            cover.color =
                new Color(
                    accent.r,
                    accent.g,
                    accent.b,
                    0.18f);

            Text typeText =
                CreateScriptText(
                    coverRect,
                    "FallbackType",
                    node.type.ToString().ToUpperInvariant(),
                    18,
                    FontStyle.Bold,
                    TextAnchor.MiddleCenter,
                    new Color(
                        scriptInkColor.r,
                        scriptInkColor.g,
                        scriptInkColor.b,
                        0.82f));

            StretchRect(
                typeText.rectTransform,
                new Vector2(8f, 8f));
        }

        Text footer =
            CreateScriptText(
                front,
                "Footer",
                "HOVER TO READ  /  CLICK TO LOCK",
                8,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                new Color(
                    scriptInkColor.r,
                    scriptInkColor.g,
                    scriptInkColor.b,
                    0.58f));

        RectTransform footerRect =
            footer.rectTransform;

        footerRect.anchorMin =
            footerRect.anchorMax =
                new Vector2(0.5f, 0f);

        footerRect.pivot =
            new Vector2(0.5f, 0f);

        footerRect.anchoredPosition =
            new Vector2(0f, 16f);

        footerRect.sizeDelta =
            new Vector2(
                scriptCardSize.x - 24f,
                18f);
    }

    private void CreateScriptDetailContent(
        RectTransform detail,
        BattleNodeData node,
        Color accent)
    {
        if (detail == null ||
            node == null)
        {
            return;
        }

        string title =
            ResolveScriptTitle(
                node);

        string subtitle =
            ResolveScriptSubtitle(
                node);

        Text header =
            CreateScriptText(
                detail,
                "DetailHeader",
                "SCENE NOTES",
                9,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                new Color(
                    accent.r,
                    accent.g,
                    accent.b,
                    0.88f));

        RectTransform headerRect =
            header.rectTransform;

        headerRect.anchorMin =
            headerRect.anchorMax =
                new Vector2(
                    0.5f,
                    1f);

        headerRect.pivot =
            new Vector2(
                0.5f,
                1f);

        headerRect.anchoredPosition =
            new Vector2(
                0f,
                -18f);

        headerRect.sizeDelta =
            new Vector2(
                scriptCardSize.x - 34f,
                18f);

        Text titleText =
            CreateScriptText(
                detail,
                "DetailTitle",
                title,
                17,
                FontStyle.Bold,
                TextAnchor.UpperLeft,
                scriptInkColor);

        RectTransform titleRect =
            titleText.rectTransform;

        titleRect.anchorMin =
            titleRect.anchorMax =
                new Vector2(
                    0.5f,
                    1f);

        titleRect.pivot =
            new Vector2(
                0.5f,
                1f);

        titleRect.anchoredPosition =
            new Vector2(
                0f,
                -42f);

        titleRect.sizeDelta =
            new Vector2(
                scriptCardSize.x - 34f,
                48f);

        titleText.resizeTextForBestFit =
            true;

        titleText.resizeTextMinSize = 11;
        titleText.resizeTextMaxSize = 17;

        Text description =
            CreateScriptText(
                detail,
                "DetailDescription",
                subtitle,
                9,
                FontStyle.Italic,
                TextAnchor.UpperLeft,
                new Color(
                    scriptInkColor.r,
                    scriptInkColor.g,
                    scriptInkColor.b,
                    0.74f));

        RectTransform descriptionRect =
            description.rectTransform;

        descriptionRect.anchorMin =
            descriptionRect.anchorMax =
                new Vector2(
                    0.5f,
                    1f);

        descriptionRect.pivot =
            new Vector2(
                0.5f,
                1f);

        descriptionRect.anchoredPosition =
            new Vector2(
                0f,
                -92f);

        descriptionRect.sizeDelta =
            new Vector2(
                scriptCardSize.x - 34f,
                42f);

        RectTransform divider =
            CreateScriptStageImage(
                detail,
                "DetailDivider",
                null,
                new Vector2(
                    scriptCardSize.x - 34f,
                    1.5f),
                new Vector2(
                    0f,
                    12f),
                new Color(
                    scriptInkColor.r,
                    scriptInkColor.g,
                    scriptInkColor.b,
                    0.22f));

        divider.SetAsFirstSibling();

        string roomLabel =
            ResolveScriptRoomLabel(
                node);

        int stars =
            runManager != null
                ? runManager.ResolveBattleRatingStars(
                    node)
                : node.GetBattleRatingStars();

        string detailBody =
            $"TAKE      {Mathf.Max(1, node.depth + 1):00}\n" +
            $"TYPE      {node.type.ToString().ToUpperInvariant()}\n" +
            $"ROOM      {roomLabel}\n" +
            $"RATING    {Mathf.Clamp(stars, 1, 5)} / 5";

        Text body =
            CreateScriptText(
                detail,
                "DetailBody",
                detailBody,
                10,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                new Color(
                    scriptInkColor.r,
                    scriptInkColor.g,
                    scriptInkColor.b,
                    0.86f));

        RectTransform bodyRect =
            body.rectTransform;

        bodyRect.anchorMin =
            bodyRect.anchorMax =
                new Vector2(
                    0.5f,
                    0.5f);

        bodyRect.pivot =
            new Vector2(
                0.5f,
                0.5f);

        bodyRect.anchoredPosition =
            new Vector2(
                0f,
                -4f);

        bodyRect.sizeDelta =
            new Vector2(
                scriptCardSize.x - 38f,
                92f);

        bool rated =
            node.type == BattleNodeType.Combat ||
            node.type == BattleNodeType.Elite;

        if (rated)
        {
            AddScriptRatingStars(
                detail,
                stars,
                accent);
        }

        Text footer =
            CreateScriptText(
                detail,
                "DetailFooter",
                "CLICK TO LOCK THIS TAKE",
                8,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                new Color(
                    accent.r,
                    accent.g,
                    accent.b,
                    0.76f));

        RectTransform footerRect =
            footer.rectTransform;

        footerRect.anchorMin =
            footerRect.anchorMax =
                new Vector2(
                    0.5f,
                    0f);

        footerRect.pivot =
            new Vector2(
                0.5f,
                0f);

        footerRect.anchoredPosition =
            new Vector2(
                0f,
                15f);

        footerRect.sizeDelta =
            new Vector2(
                scriptCardSize.x - 26f,
                18f);
    }

    private static string ResolveScriptRoomLabel(
        BattleNodeData node)
    {
        if (node == null ||
            node.room == null ||
            string.IsNullOrWhiteSpace(
                node.room.roomId))
        {
            return "UNASSIGNED";
        }

        return node.room.roomId
            .Replace(
                "TEST_",
                string.Empty)
            .Replace(
                '_',
                ' ')
            .Trim()
            .ToUpperInvariant();
    }

    private Text CreateScriptText(
        Transform parent,
        string name,
        string value,
        int size,
        FontStyle style,
        TextAnchor alignment,
        Color color)
    {
        GameObject textObject =
            new(
                name,
                typeof(RectTransform));

        textObject.transform.SetParent(
            parent,
            false);

        Text text =
            textObject.AddComponent<Text>();

        text.font =
            Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");

        text.text =
            value ?? string.Empty;

        text.fontSize =
            size;

        text.fontStyle =
            style;

        text.alignment =
            alignment;

        text.color =
            color;

        text.raycastTarget = false;

        return text;
    }

    private void AddScriptRatingStars(
        Transform parent,
        int activeStars,
        Color accent)
    {
        GameObject row =
            new(
                "RatingStars",
                typeof(RectTransform));

        row.transform.SetParent(
            parent,
            false);

        RectTransform rowRect =
            row.GetComponent<RectTransform>();

        rowRect.anchorMin =
            rowRect.anchorMax =
                new Vector2(0.5f, 0f);

        rowRect.pivot =
            new Vector2(0.5f, 0f);

        rowRect.anchoredPosition =
            new Vector2(0f, 40f);

        rowRect.sizeDelta =
            new Vector2(92f, 16f);

        const float starSize = 13f;
        const float gap = 4f;

        float total =
            starSize * 5f +
            gap * 4f;

        float startX =
            -total * 0.5f +
            starSize * 0.5f;

        Sprite starSprite =
            GetMapRatingStarSprite();

        int clamped =
            Mathf.Clamp(
                activeStars,
                1,
                5);

        for (int i = 0; i < 5; i++)
        {
            GameObject starObject =
                new(
                    $"RatingStar_{i}",
                    typeof(RectTransform));

            starObject.transform.SetParent(
                rowRect,
                false);

            RectTransform starRect =
                starObject.GetComponent<RectTransform>();

            starRect.anchorMin =
                starRect.anchorMax =
                    new Vector2(0.5f, 0.5f);

            starRect.pivot =
                new Vector2(0.5f, 0.5f);

            starRect.sizeDelta =
                Vector2.one *
                starSize;

            starRect.anchoredPosition =
                new Vector2(
                    startX +
                    i *
                    (starSize + gap),
                    0f);

            Image star =
                starObject.AddComponent<Image>();

            star.sprite =
                starSprite;

            star.preserveAspect = true;
            star.raycastTarget = false;

            star.color =
                i < clamped
                    ? new Color(
                        0.90f,
                        0.65f,
                        0.18f,
                        1f)
                    : new Color(
                        accent.r,
                        accent.g,
                        accent.b,
                        0.18f);
        }
    }

    private string ResolveScriptTitle(
        BattleNodeData node)
    {
        if (node == null)
            return "UNTITLED SCRIPT";

        if (!string.IsNullOrWhiteSpace(
                node.scriptTitle))
        {
            return node.scriptTitle.Trim();
        }

        if (node.room != null &&
            !string.IsNullOrWhiteSpace(
                node.room.roomId))
        {
            string roomTitle =
                node.room.roomId
                    .Replace("TEST_", string.Empty)
                    .Replace('_', ' ')
                    .Trim();

            if (!string.IsNullOrWhiteSpace(
                    roomTitle))
            {
                return roomTitle.ToUpperInvariant();
            }
        }

        return node.type switch
        {
            BattleNodeType.Elite => "SPECIAL PERFORMANCE",
            BattleNodeType.Shop => "INTERMISSION",
            BattleNodeType.Event => "UNPLANNED SCENE",
            _ => "BATTLE SCENE"
        };
    }

    private static string ResolveScriptSubtitle(
        BattleNodeData node)
    {
        if (node == null)
            return "THE NEXT TAKE";

        if (!string.IsNullOrWhiteSpace(
                node.scriptSubtitle))
        {
            return node.scriptSubtitle.Trim();
        }

        return node.type switch
        {
            BattleNodeType.Elite => "HIGH-RISK LIVE TAKE",
            BattleNodeType.Shop => "PROP & EQUIPMENT BREAK",
            BattleNodeType.Event => "AN UNSCRIPTED TURN",
            _ => "LIVE COMBAT TAKE"
        };
    }

    private static void StretchRect(
        RectTransform rect,
        Vector2 padding)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin =
            new Vector2(
                padding.x,
                padding.y);
        rect.offsetMax =
            new Vector2(
                -padding.x,
                -padding.y);
    }

    private static Sprite GetScriptSpotlightBeamSprite()
    {
        if (scriptSpotlightBeamSprite != null)
            return scriptSpotlightBeamSprite;

        const int width = 64;
        const int height = 128;

        Texture2D texture =
            new(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name =
                    "RuntimeScriptSpotlightBeam",
                filterMode =
                    FilterMode.Bilinear,
                wrapMode =
                    TextureWrapMode.Clamp,
                hideFlags =
                    HideFlags.HideAndDontSave
            };

        for (int y = 0; y < height; y++)
        {
            float v =
                y /
                (float)(
                    height - 1);

            // Narrow source at the top, wider pool-facing edge at the bottom.
            float halfWidth =
                Mathf.Lerp(
                    0.48f,
                    0.16f,
                    v);

            float vertical =
                Mathf.Sin(
                    Mathf.Clamp01(
                        v) *
                    Mathf.PI);

            vertical =
                Mathf.Pow(
                    Mathf.Max(
                        0f,
                        vertical),
                    0.65f);

            vertical *=
                Mathf.Lerp(
                    0.72f,
                    0.34f,
                    v);

            for (int x = 0; x < width; x++)
            {
                float u =
                    (x /
                     (float)(
                         width - 1) -
                     0.5f);

                float normalized =
                    Mathf.Abs(u) /
                    Mathf.Max(
                        0.001f,
                        halfWidth);

                float edge =
                    1f -
                    Mathf.SmoothStep(
                        0.60f,
                        1f,
                        normalized);

                float alpha =
                    Mathf.Clamp01(
                        edge *
                        vertical);

                texture.SetPixel(
                    x,
                    y,
                    new Color(
                        1f,
                        1f,
                        1f,
                        alpha));
            }
        }

        texture.Apply(
            false,
            true);

        scriptSpotlightBeamSprite =
            Sprite.Create(
                texture,
                new Rect(
                    0f,
                    0f,
                    width,
                    height),
                new Vector2(
                    0.5f,
                    0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);

        scriptSpotlightBeamSprite.name =
            "RuntimeScriptSpotlightBeamSprite";

        scriptSpotlightBeamSprite.hideFlags =
            HideFlags.HideAndDontSave;

        return scriptSpotlightBeamSprite;
    }

    private static Sprite GetScriptPaperSprite()
    {
        if (scriptPaperSprite != null)
            return scriptPaperSprite;

        const int width = 96;
        const int height = 132;

        Texture2D texture =
            new(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name =
                    "RuntimeScriptPaper",
                filterMode =
                    FilterMode.Bilinear,
                wrapMode =
                    TextureWrapMode.Clamp,
                hideFlags =
                    HideFlags.HideAndDontSave
            };

        Color[] pixels =
            new Color[
                width *
                height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float edge =
                    Mathf.Min(
                        Mathf.Min(x, width - 1 - x),
                        Mathf.Min(y, height - 1 - y));

                float alpha =
                    Mathf.Clamp01(
                        edge /
                        2.5f);

                float grain =
                    Mathf.PerlinNoise(
                        x * 0.16f + 1.7f,
                        y * 0.14f + 4.2f);

                float value =
                    Mathf.Lerp(
                        0.91f,
                        1.0f,
                        grain);

                pixels[
                    y * width +
                    x] =
                    new Color(
                        value,
                        value * 0.985f,
                        value * 0.94f,
                        alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        scriptPaperSprite =
            Sprite.Create(
                texture,
                new Rect(
                    0f,
                    0f,
                    width,
                    height),
                new Vector2(
                    0.5f,
                    0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);

        scriptPaperSprite.name =
            "RuntimeScriptPaperSprite";

        scriptPaperSprite.hideFlags =
            HideFlags.HideAndDontSave;

        return scriptPaperSprite;
    }

    private static Sprite GetMapRatingStarSprite()
    {
        if (mapRatingStarSprite != null)
            return mapRatingStarSprite;

        const int size = 24;

        Vector2 center =
            new(
                (size - 1) * 0.5f,
                (size - 1) * 0.5f);

        Vector2[] polygon =
            new Vector2[10];

        for (int i = 0; i < polygon.Length; i++)
        {
            float radius =
                i % 2 == 0
                    ? 10.5f
                    : 4.6f;

            float angle =
                (-90f + i * 36f) *
                Mathf.Deg2Rad;

            polygon[i] =
                center +
                new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)) *
                radius;
        }

        Texture2D texture =
            new(
                size,
                size,
                TextureFormat.RGBA32,
                false)
            {
                filterMode =
                    FilterMode.Point,
                wrapMode =
                    TextureWrapMode.Clamp,
                hideFlags =
                    HideFlags.HideAndDontSave
            };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inside =
                    PointInPolygon(
                        new Vector2(
                            x + 0.5f,
                            y + 0.5f),
                        polygon);

                texture.SetPixel(
                    x,
                    y,
                    inside
                        ? Color.white
                        : Color.clear);
            }
        }

        texture.Apply(false, true);

        mapRatingStarSprite =
            Sprite.Create(
                texture,
                new Rect(
                    0f,
                    0f,
                    size,
                    size),
                new Vector2(
                    0.5f,
                    0.5f),
                size,
                0,
                SpriteMeshType.FullRect);

        mapRatingStarSprite.name =
            "RuntimeMapRatingStar";

        mapRatingStarSprite.hideFlags =
            HideFlags.HideAndDontSave;

        return mapRatingStarSprite;
    }

    private static bool PointInPolygon(
        Vector2 point,
        IReadOnlyList<Vector2> polygon)
    {
        bool inside = false;
        int j = polygon.Count - 1;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];

            bool crosses =
                (a.y > point.y) !=
                (b.y > point.y) &&
                point.x <
                (b.x - a.x) *
                (point.y - a.y) /
                (b.y - a.y) +
                a.x;

            if (crosses)
                inside = !inside;

            j = i;
        }

        return inside;
    }

    // Kept as a no-op compatibility method because room generation still invokes it
    // during graph binding. Script Selection no longer needs spatial graph layout.
    private void BuildResolvedLayout()
    {
        resolvedMapPositions.Clear();
    }

    private void PlayStageMapReveal()
    {
        if (stageMapRevealRoutine != null)
            StopCoroutine(stageMapRevealRoutine);

        stageMapRevealRoutine =
            StartCoroutine(
                StageMapRevealRoutine());
    }

    private IEnumerator StageMapRevealRoutine()
    {
        if (stageMapPanel == null ||
            stageMapCanvasGroup == null)
        {
            stageMapRevealRoutine = null;
            yield break;
        }

        float duration =
            Mathf.Max(
                0.05f,
                mapRevealDuration);

        float elapsed = 0f;

        Vector2 startPosition =
            stageMapPanelRestPosition +
            Vector2.down *
            Mathf.Min(
                42f,
                mapRevealSlideDistance);

        stageMapCanvasGroup.alpha = 0f;
        stageMapCanvasGroup.blocksRaycasts = false;

        stageMapPanel.anchoredPosition =
            startPosition;

        stageMapPanel.localScale =
            Vector3.one *
            0.985f;

        while (elapsed < duration)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            float eased =
                t * t *
                (3f - 2f * t);

            stageMapCanvasGroup.alpha =
                eased;

            stageMapPanel.anchoredPosition =
                Vector2.LerpUnclamped(
                    startPosition,
                    stageMapPanelRestPosition,
                    eased);

            stageMapPanel.localScale =
                Vector3.LerpUnclamped(
                    Vector3.one * 0.985f,
                    Vector3.one,
                    eased);

            yield return null;
        }

        stageMapCanvasGroup.alpha = 1f;
        stageMapCanvasGroup.blocksRaycasts = true;
        stageMapPanel.anchoredPosition =
            stageMapPanelRestPosition;
        stageMapPanel.localScale =
            Vector3.one;

        stageMapRevealRoutine = null;
    }

    private void UpdateStageMapCursorTracking()
    {
        if (battleCameraController == null)
            battleCameraController =
                FindFirstObjectByType<BattleCameraController>();

        if (!mapSelectionActive ||
            stageMapPanel == null ||
            stageMapRevealRoutine != null ||
            stageMapSelectionLocked ||
            !stageMapPanel.gameObject.activeInHierarchy)
        {
            ClearTrackedStageMapHover();
            battleCameraController?.SetMapCursorTracking(
                false,
                Vector2.zero);
            return;
        }

        if (inputRouter == null ||
            !inputRouter.PointerPresent)
        {
            ClearTrackedStageMapHover();
            battleCameraController?.SetMapCursorTracking(
                false,
                Vector2.zero);
            return;
        }

        Camera eventCamera =
            ResolveStageMapEventCamera();

        Vector2 pointer =
            inputRouter.PointerPosition;

        Button directHit =
            FindStageMapButtonUnderPointer(
                pointer,
                eventCamera);

        if (directHit != null)
        {
            SetTrackedStageMapButton(
                directHit,
                pointer);
        }
        else if (trackedStageMapButton != null)
        {
            bool canLatch =
                trackedStageMapButton.interactable &&
                trackedStageMapButton.gameObject.activeInHierarchy &&
                Vector2.Distance(
                    pointer,
                    trackedStageMapPointerAnchor) <=
                Mathf.Max(
                    8f,
                    mapHoverLatchPixels);

            if (!canLatch)
                ClearTrackedStageMapHover();
        }

        if (trackedStageMapButton == null)
        {
            battleCameraController?.SetMapCursorTracking(
                false,
                Vector2.zero);
            return;
        }

        // Script Selection keeps the show camera fixed.
        // Hover feedback belongs to the paper mesh/light rig only; moving the
        // camera at the same time reintroduces edge clipping and visual twitch.
        battleCameraController?.SetMapCursorTracking(
            false,
            Vector2.zero);
    }

    private Button FindStageMapButtonUnderPointer(
        Vector2 pointer,
        Camera eventCamera)
    {
        if (stageMapPanel == null)
            return null;

        Button[] buttons =
            stageMapPanel.GetComponentsInChildren<Button>(
                false);

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];

            if (button == null ||
                !button.interactable)
            {
                continue;
            }

            RectTransform rect =
                button.transform
                as RectTransform;

            if (rect != null &&
                RectTransformUtility.RectangleContainsScreenPoint(
                    rect,
                    pointer,
                    eventCamera))
            {
                return button;
            }
        }

        return null;
    }

    private void SetTrackedStageMapButton(
        Button button,
        Vector2 pointer)
    {
        if (trackedStageMapButton != button)
        {
            SetTrackedStageMapVisual(
                trackedStageMapButton,
                false);

            trackedStageMapButton =
                button;

            SetTrackedStageMapVisual(
                trackedStageMapButton,
                true);

            NotifyPresenterPrototypeMapHover(
                trackedStageMapButton);
        }

        trackedStageMapPointerAnchor =
            pointer;
    }

    private void ClearTrackedStageMapHover()
    {
        SetTrackedStageMapVisual(
            trackedStageMapButton,
            false);

        trackedStageMapButton = null;
        trackedStageMapPointerAnchor =
            Vector2.zero;
    }

    private static void SetTrackedStageMapVisual(
        Button button,
        bool value)
    {
        if (button == null)
            return;

        BattleScriptCardVisual visual =
            button.GetComponent<BattleScriptCardVisual>();

        visual?.SetTrackedHover(value);
    }

    private void NotifyPresenterPrototypeMapHover(
        Button button)
    {
        if (button == null ||
            graph == null)
        {
            return;
        }

        const string prefix =
            "ScriptCard_";

        string objectName =
            button.gameObject.name;

        if (string.IsNullOrEmpty(
                objectName) ||
            !objectName.StartsWith(
                prefix,
                StringComparison.Ordinal))
        {
            return;
        }

        string nodeId =
            objectName.Substring(
                prefix.Length);

        BattleNodeData node =
            graph.FindNode(
                nodeId);

        if (node == null)
            return;

        int stars =
            runManager != null
                ? runManager.ResolveBattleRatingStars(
                    node)
                : node.GetBattleRatingStars();

        BattleScreenPresenterPrototypeController.NotifyMapHover(
            node,
            stars);
    }

    private Camera ResolveStageMapEventCamera()
    {
        if (stageMapCanvas == null &&
            stageMapPanel != null)
        {
            stageMapCanvas =
                stageMapPanel.GetComponentInParent<Canvas>();
        }

        if (stageMapCanvas == null ||
            stageMapCanvas.renderMode ==
            RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        if (stageMapCanvas.worldCamera != null)
            return stageMapCanvas.worldCamera;

        return Camera.main;
    }

    private void BeginStageNodeSelection(
        string nodeId,
        RectTransform selectedCard)
    {
        if (stageMapSelectionLocked ||
            runManager == null ||
            !mapSelectionActive)
        {
            return;
        }

        BattleNodeData node =
            graph != null
                ? graph.FindNode(
                    nodeId)
                : null;

        if (node != null)
        {
            BattleScreenPresenterPrototypeController.NotifyMapConfirm(
                node,
                runManager.ResolveBattleRatingStars(
                    node));
        }

        stageMapSelectionLocked = true;

        ClearTrackedStageMapHover();

        battleCameraController?.SetMapCursorTracking(
            false,
            Vector2.zero);

        if (stageMapCanvasGroup != null)
            stageMapCanvasGroup.blocksRaycasts = false;

        if (stageMapConfirmRoutine != null)
            StopCoroutine(stageMapConfirmRoutine);

        stageMapConfirmRoutine =
            StartCoroutine(
                AnimateStageNodeSelection(
                    nodeId,
                    selectedCard));
    }

    private IEnumerator AnimateStageNodeSelection(
        string nodeId,
        RectTransform selectedCard)
    {
        if (stageMapRevealRoutine != null)
        {
            StopCoroutine(stageMapRevealRoutine);
            stageMapRevealRoutine = null;
        }

        BattleNodeData selectedNode =
            graph != null
                ? graph.FindNode(
                    nodeId)
                : null;

        BattleScriptCardVisual[] cards =
            stageMapPanel != null
                ? stageMapPanel.GetComponentsInChildren<BattleScriptCardVisual>(
                    true)
                : Array.Empty<BattleScriptCardVisual>();

        for (int i = 0; i < cards.Length; i++)
        {
            BattleScriptCardVisual card =
                cards[i];

            if (card == null)
                continue;

            bool selected =
                selectedCard != null &&
                card.transform ==
                selectedCard;

            card.SetSelected(
                selected);

            card.SetSuppressed(
                !selected);
        }

        float duration =
            Mathf.Max(
                0.18f,
                mapConfirmDuration);

        battleCameraController?.PlaySelectionConfirmShake(
            mapConfirmCameraShake *
            0.72f,
            duration);

        EnsureRouteStatus();

        SetRouteStatus(
            true,
            selectedNode != null
                ? $"SCRIPT LOCKED  /  TAKE {Mathf.Max(1, selectedNode.depth + 1):00}"
                : "SCRIPT LOCKED");

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            if (stageMapCanvasGroup != null)
            {
                stageMapCanvasGroup.alpha =
                    Mathf.Lerp(
                        1f,
                        0.94f,
                        t * 0.35f);
            }

            yield return null;
        }

        if (mapRouteLockHold > 0f)
            yield return WaitUnscaledSeconds(
                Mathf.Min(
                    0.34f,
                    mapRouteLockHold));

        SetRouteStatus(
            false,
            string.Empty);

        stageMapConfirmRoutine = null;

        runManager?.SelectNextNode(
            nodeId);
    }

    private static IEnumerator WaitUnscaledSeconds(
        float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            yield return null;
        }
    }

    private void EnsureRouteStatus()
    {
        if (stageMapPanel == null ||
            routeStatusRoot != null)
        {
            return;
        }

        GameObject root =
            new(
                "ScriptCommitStatus",
                typeof(RectTransform));

        root.transform.SetParent(
            stageMapPanel,
            false);

        routeStatusRoot =
            root.GetComponent<RectTransform>();

        routeStatusRoot.anchorMin =
            routeStatusRoot.anchorMax =
                new Vector2(0.5f, 0f);

        routeStatusRoot.pivot =
            new Vector2(0.5f, 0f);

        routeStatusRoot.anchoredPosition =
            new Vector2(0f, 46f);

        routeStatusRoot.sizeDelta =
            new Vector2(480f, 42f);

        Image back =
            root.AddComponent<Image>();

        back.sprite =
            GetScriptPaperSprite();

        back.color =
            new Color(
                0.07f,
                0.06f,
                0.05f,
                0.96f);

        back.raycastTarget = false;

        GameObject textObject =
            new(
                "Text",
                typeof(RectTransform));

        textObject.transform.SetParent(
            routeStatusRoot,
            false);

        RectTransform textRect =
            textObject.GetComponent<RectTransform>();

        StretchRect(
            textRect,
            new Vector2(10f, 4f));

        routeStatusText =
            textObject.AddComponent<Text>();

        routeStatusText.font =
            Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");

        routeStatusText.fontSize = 14;
        routeStatusText.fontStyle = FontStyle.Bold;
        routeStatusText.alignment =
            TextAnchor.MiddleCenter;
        routeStatusText.color =
            new Color(
                0.95f,
                0.90f,
                0.78f,
                1f);
        routeStatusText.raycastTarget = false;

        routeStatusGroup =
            root.AddComponent<CanvasGroup>();

        routeStatusGroup.blocksRaycasts = false;
        routeStatusGroup.interactable = false;

        routeStatusRoot.gameObject.SetActive(
            false);
    }

    private void SetRouteStatus(
        bool visible,
        string message)
    {
        if (!visible &&
            routeStatusRoot == null)
        {
            return;
        }

        if (visible)
            EnsureRouteStatus();

        if (routeStatusRoot == null)
            return;

        if (routeStatusText != null)
            routeStatusText.text =
                message ??
                string.Empty;

        routeStatusRoot.gameObject.SetActive(
            visible);

        if (routeStatusGroup != null)
            routeStatusGroup.alpha =
                visible
                    ? 1f
                    : 0f;

        if (visible)
            routeStatusRoot.SetAsLastSibling();
    }

    // Legacy relay compatibility. Script Selection only creates selectable cards,
    // so this is normally never called.
    internal void ShowMapDenied(
        RectTransform cardRect)
    {
        if (!mapSelectionActive ||
            stageMapSelectionLocked ||
            cardRect == null)
        {
            return;
        }

        SetRouteStatus(
            true,
            "SCRIPT UNAVAILABLE");

        StartCoroutine(
            HideDeniedMessage());
    }

    private IEnumerator HideDeniedMessage()
    {
        yield return WaitUnscaledSeconds(
            0.28f);

        if (!stageMapSelectionLocked)
            SetRouteStatus(
                false,
                string.Empty);
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

        SetRouteStatus(
            false,
            string.Empty);

        routeStatusRoot = null;
        routeStatusText = null;
        routeStatusGroup = null;

        ClearTrackedStageMapHover();

        if (stageMapCanvasGroup != null)
        {
            stageMapCanvasGroup.alpha = 0f;
            stageMapCanvasGroup.blocksRaycasts = true;
        }

        if (stageMapPanel != null)
        {
            stageMapPanel.anchoredPosition =
                stageMapPanelRestPosition;

            stageMapPanel.localScale =
                Vector3.one;

            stageMapPanel.localRotation =
                Quaternion.identity;

            Vector3 local =
                stageMapPanel.localPosition;

            local.z = 0f;

            stageMapPanel.localPosition =
                local;

            stageMapPanel.gameObject.SetActive(
                false);
        }

        battleCameraController?.SetMapCursorTracking(
            false,
            Vector2.zero);
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


internal sealed class BattleScriptCardVisual :
    MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private RectTransform visualRoot;
    private RectTransform frontPage;
    private RectTransform detailPage;
    private RectTransform backPageA;
    private RectTransform backPageB;
    private RectTransform stageBase;
    private RectTransform lightPool;
    private RectTransform lightBeam;
    private CanvasGroup visualGroup;
    private BattleScriptPageTurnRig pageTurnRig;
    private Image stageBaseImage;
    private Image lightPoolImage;
    private Image lightBeamImage;

    private Vector2 baseStagePosition;
    private Vector2 basePoolPosition;
    private Vector2 baseBeamPosition;

    private float phase;
    private float idleFloatPixels;
    private float idleCyclesPerSecond;
    private float hoverLiftPixels;
    private float hoverScale;
    private float hoverShearPixels;
    private float hoverRollDegrees;
    private float paperWavePixels;
    private float paperWaveCyclesPerSecond;
    private float idleWaveStrength;
    private float pageTurnDuration;
    private float pageReturnDuration;
    private float pageTurn01;
    private Color accentColor;

    private BattleScriptPaperWaveEffect[] paperWaveEffects;

    private bool pointerHover;
    private bool trackedHover;
    private bool selected;
    private bool suppressed;

    private Vector2 baseFrontPosition;
    private Vector2 baseBackAPosition;
    private Vector2 baseBackBPosition;
    private Quaternion baseFrontRotation;
    private Quaternion baseBackARotation;
    private Quaternion baseBackBRotation;

    public void Configure(
        RectTransform animatedRoot,
        RectTransform front,
        RectTransform detail,
        BattleScriptPageTurnRig turnRig,
        RectTransform backA,
        RectTransform backB,
        RectTransform stageBaseRect,
        RectTransform lightPoolRect,
        RectTransform lightBeamRect,
        CanvasGroup group,
        float idlePhase,
        float floatPixels,
        float floatCycles,
        float hoverLift,
        float selectedHoverScale,
        float hoverShear,
        float hoverRoll,
        float wavePixels,
        float waveCycles,
        float idleWave,
        float turnDuration,
        float returnDuration,
        Color accent)
    {
        visualRoot = animatedRoot;
        frontPage = front;
        detailPage = detail;
        pageTurnRig = turnRig;
        backPageA = backA;
        backPageB = backB;
        stageBase = stageBaseRect;
        lightPool = lightPoolRect;
        lightBeam = lightBeamRect;
        visualGroup = group;

        stageBaseImage =
            stageBase != null
                ? stageBase.GetComponent<Image>()
                : null;

        lightPoolImage =
            lightPool != null
                ? lightPool.GetComponent<Image>()
                : null;

        lightBeamImage =
            lightBeam != null
                ? lightBeam.GetComponent<Image>()
                : null;

        if (stageBase != null)
            baseStagePosition = stageBase.anchoredPosition;

        if (lightPool != null)
            basePoolPosition = lightPool.anchoredPosition;

        if (lightBeam != null)
            baseBeamPosition = lightBeam.anchoredPosition;

        phase = idlePhase;
        idleFloatPixels = Mathf.Max(0f, floatPixels);
        idleCyclesPerSecond = Mathf.Max(0.01f, floatCycles);
        hoverLiftPixels = Mathf.Max(0f, hoverLift);
        hoverScale = Mathf.Max(1f, selectedHoverScale);
        hoverShearPixels = hoverShear;
        hoverRollDegrees = hoverRoll;
        paperWavePixels = Mathf.Max(0f, wavePixels);
        paperWaveCyclesPerSecond = Mathf.Max(0.01f, waveCycles);
        idleWaveStrength = Mathf.Clamp01(idleWave);
        pageTurnDuration = Mathf.Max(0.08f, turnDuration);
        pageReturnDuration = Mathf.Max(0.10f, returnDuration);
        accentColor = accent;

        if (frontPage != null)
        {
            baseFrontPosition =
                frontPage.anchoredPosition;

            baseFrontRotation =
                frontPage.localRotation;
        }

        InstallPaperWaveEffects();

        if (backPageA != null)
        {
            baseBackAPosition = backPageA.anchoredPosition;
            baseBackARotation = backPageA.localRotation;
        }

        if (backPageB != null)
        {
            baseBackBPosition = backPageB.anchoredPosition;
            baseBackBRotation = backPageB.localRotation;
        }

        ApplyImmediate();
    }

    public void SetTrackedHover(bool value)
    {
        trackedHover = value;
    }

    public void SetSelected(bool value)
    {
        selected = value;
        if (value)
        {
            pointerHover = false;
            trackedHover = false;
        }
    }

    public void SetSuppressed(bool value)
    {
        suppressed = value;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!selected)
            pointerHover = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!selected)
            pointerHover = false;
    }

    private void Update()
    {
        if (visualRoot == null)
            return;

        bool hovered =
            !selected &&
            !suppressed &&
            (pointerHover || trackedHover);

        float dt =
            Mathf.Max(
                0f,
                Time.unscaledDeltaTime);

        float motionT =
            1f -
            Mathf.Exp(
                -8.0f *
                dt);

        float time =
            Time.unscaledTime;

        float idlePhase =
            time *
            idleCyclesPerSecond *
            Mathf.PI *
            2f +
            phase;

        float idleY =
            Mathf.Sin(idlePhase) *
            idleFloatPixels;

        float idleRoll =
            Mathf.Sin(
                idlePhase * 0.61f +
                0.8f) *
            0.32f;

        float targetScale =
            selected
                ? hoverScale * 1.025f
                : hovered
                    ? hoverScale
                    : suppressed
                        ? 0.975f
                        : 1f;

        float targetY =
            idleY +
            (selected
                ? hoverLiftPixels * 1.12f
                : hovered
                    ? hoverLiftPixels
                    : 0f);

        // Never move a World-Space UI card toward/away from the camera.
        // Faux depth is produced only by scale + mesh shear.
        Vector3 targetPosition =
            new(
                0f,
                targetY,
                0f);

        float targetRoll =
            selected
                ? 0f
                : hovered
                    ? hoverRollDegrees
                    : idleRoll;

        visualRoot.localPosition =
            Vector3.Lerp(
                visualRoot.localPosition,
                targetPosition,
                motionT);

        visualRoot.localScale =
            Vector3.Lerp(
                visualRoot.localScale,
                Vector3.one *
                targetScale,
                motionT);

        visualRoot.localRotation =
            Quaternion.Slerp(
                visualRoot.localRotation,
                Quaternion.Euler(
                    0f,
                    0f,
                    targetRoll),
                motionT);

        float targetAlpha =
            selected || hovered
                ? 1f
                : suppressed
                    ? 0.26f
                    : 0.91f;

        if (visualGroup != null)
        {
            visualGroup.alpha =
                Mathf.Lerp(
                    visualGroup.alpha,
                    targetAlpha,
                    motionT);
        }

        float targetWave =
            suppressed
                ? paperWavePixels * 0.05f
                : selected
                    ? paperWavePixels * 0.16f
                    : hovered
                        ? paperWavePixels
                        : paperWavePixels *
                          idleWaveStrength;

        float turnTarget =
            hovered || selected
                ? 1f
                : 0f;

        float turnSpeed =
            turnTarget > pageTurn01
                ? 1f /
                  Mathf.Max(
                      0.08f,
                      pageTurnDuration)
                : 1f /
                  Mathf.Max(
                      0.10f,
                      pageReturnDuration);

        pageTurn01 =
            Mathf.MoveTowards(
                pageTurn01,
                turnTarget,
                dt *
                turnSpeed);

        ApplyPageTurn(
            pageTurn01);

        float turnFlutter =
            Mathf.Sin(
                pageTurn01 *
                Mathf.PI);

        float targetShear =
            hovered
                ? hoverShearPixels *
                  (1f - pageTurn01 * 0.72f)
                : selected
                    ? 0f
                    : 0f;

        // The cover catches a brief extra wave while the lower edge flips upward.
        // Detail/back sheets receive only their configured attenuated fraction.
        targetWave +=
            paperWavePixels *
            0.82f *
            turnFlutter;

        ApplyPaperWave(
            targetWave,
            targetShear,
            phase);

        ApplyStageLighting(
            hovered,
            selected,
            motionT,
            time);
    }

    private void ApplyPageTurn(
        float normalized)
    {
        float t =
            Mathf.Clamp01(
                normalized);

        float eased =
            t * t *
            (3f - 2f * t);

        pageTurnRig?.SetProgress(
            eased);
    }

    private void InstallPaperWaveEffects()
    {
        List<BattleScriptPaperWaveEffect> effects =
            new();

        AddPaperWaveEffects(
            frontPage,
            effects,
            subdividePage: true,
            amplitudeMultiplier: 1f);

        AddPaperWaveEffects(
            detailPage,
            effects,
            subdividePage: true,
            amplitudeMultiplier: 0.38f);

        if (visualRoot != null)
        {
            int stackCount = 0;

            for (int i = 0; i < visualRoot.childCount; i++)
            {
                Transform child =
                    visualRoot.GetChild(i);

                if (child == null ||
                    !child.name.StartsWith(
                        "PaperStack_",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                stackCount++;
            }

            int stackIndex = 0;

            for (int i = 0; i < visualRoot.childCount; i++)
            {
                RectTransform child =
                    visualRoot.GetChild(i)
                    as RectTransform;

                if (child == null ||
                    !child.name.StartsWith(
                        "PaperStack_",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                float depth01 =
                    stackCount <= 1
                        ? 1f
                        : stackIndex /
                          (float)(
                              stackCount - 1);

                // Deeper sheets move less, so the stack reads as paper thickness
                // instead of several independent cards.
                float multiplier =
                    Mathf.Lerp(
                        0.72f,
                        0.24f,
                        depth01);

                AddPaperWaveEffects(
                    child,
                    effects,
                    subdividePage: true,
                    amplitudeMultiplier:
                        multiplier);

                stackIndex++;
            }
        }

        paperWaveEffects =
            effects.ToArray();
    }

    private void AddPaperWaveEffects(
        RectTransform pageRoot,
        List<BattleScriptPaperWaveEffect> effects,
        bool subdividePage,
        float amplitudeMultiplier)
    {
        if (pageRoot == null)
            return;

        Graphic[] graphics =
            pageRoot.GetComponentsInChildren<Graphic>(
                true);

        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic =
                graphics[i];

            if (graphic == null)
                continue;

            BattleScriptPaperWaveEffect effect =
                graphic.GetComponent<BattleScriptPaperWaveEffect>();

            if (effect == null)
            {
                effect =
                    graphic.gameObject.AddComponent<BattleScriptPaperWaveEffect>();
            }

            bool subdivide =
                subdividePage &&
                graphic.transform ==
                pageRoot;

            effect.Configure(
                visualRoot,
                subdivide,
                phase,
                paperWaveCyclesPerSecond,
                amplitudeMultiplier);

            effects.Add(
                effect);
        }
    }

    private void ApplyPaperWave(
        float amplitudePixels,
        float shearPixels,
        float wavePhase)
    {
        if (paperWaveEffects == null)
            return;

        for (int i = 0; i < paperWaveEffects.Length; i++)
        {
            BattleScriptPaperWaveEffect effect =
                paperWaveEffects[i];

            if (effect == null)
                continue;

            effect.SetState(
                amplitudePixels,
                shearPixels,
                wavePhase);
        }
    }

    private void ApplyStageLighting(
        bool hovered,
        bool isSelected,
        float t,
        float time)
    {
        float lightAmount =
            isSelected
                ? 1f
                : hovered
                    ? 0.88f
                    : suppressed
                        ? 0.16f
                        : 0.46f;

        float driftPhase =
            time *
            idleCyclesPerSecond *
            Mathf.PI *
            2f +
            phase +
            1.41f;

        float driftX =
            Mathf.Sin(
                driftPhase *
                0.73f) *
            2.4f;

        float driftY =
            Mathf.Sin(
                driftPhase *
                0.51f +
                0.82f) *
            1.4f;

        if (lightPool != null)
        {
            Vector2 target =
                basePoolPosition +
                new Vector2(
                    driftX * 0.32f,
                    driftY * 0.28f);

            lightPool.anchoredPosition =
                Vector2.Lerp(
                    lightPool.anchoredPosition,
                    target,
                    t);

            float targetScale =
                Mathf.Lerp(
                    0.96f,
                    1.08f,
                    lightAmount);

            lightPool.localScale =
                Vector3.Lerp(
                    lightPool.localScale,
                    Vector3.one *
                    targetScale,
                    t);
        }

        if (lightBeam != null)
        {
            Vector2 target =
                baseBeamPosition +
                new Vector2(
                    driftX,
                    driftY +
                    (hovered
                        ? 3f
                        : 0f));

            lightBeam.anchoredPosition =
                Vector2.Lerp(
                    lightBeam.anchoredPosition,
                    target,
                    t);

            float beamScale =
                Mathf.Lerp(
                    0.96f,
                    1.07f,
                    lightAmount);

            lightBeam.localScale =
                Vector3.Lerp(
                    lightBeam.localScale,
                    new Vector3(
                        beamScale,
                        Mathf.Lerp(
                            0.98f,
                            1.06f,
                            lightAmount),
                        1f),
                    t);
        }

        if (stageBase != null)
        {
            stageBase.anchoredPosition =
                Vector2.Lerp(
                    stageBase.anchoredPosition,
                    baseStagePosition,
                    t);

            float baseScale =
                isSelected
                    ? 1.055f
                    : hovered
                        ? 1.035f
                        : 1f;

            stageBase.localScale =
                Vector3.Lerp(
                    stageBase.localScale,
                    Vector3.one *
                    baseScale,
                    t);
        }

        if (lightPoolImage != null)
        {
            Color color =
                lightPoolImage.color;

            color.a =
                Mathf.Lerp(
                    color.a,
                    Mathf.Lerp(
                        0.09f,
                        0.34f,
                        lightAmount),
                    t);

            lightPoolImage.color =
                color;
        }

        if (lightBeamImage != null)
        {
            Color color =
                lightBeamImage.color;

            color.a =
                Mathf.Lerp(
                    color.a,
                    Mathf.Lerp(
                        0.055f,
                        0.27f,
                        lightAmount),
                    t);

            lightBeamImage.color =
                color;
        }

        if (stageBaseImage != null)
        {
            Color color =
                stageBaseImage.color;

            color.a =
                Mathf.Lerp(
                    color.a,
                    suppressed
                        ? 0.38f
                        : 0.94f,
                    t);

            stageBaseImage.color =
                color;
        }
    }

    private void ApplyImmediate()
    {
        if (visualRoot != null)
        {
            visualRoot.localPosition =
                Vector3.zero;

            visualRoot.localScale =
                Vector3.one;

            visualRoot.localRotation =
                Quaternion.identity;
        }

        if (frontPage != null)
        {
            frontPage.localScale =
                Vector3.one;

            frontPage.anchoredPosition =
                baseFrontPosition;

            frontPage.localRotation =
                baseFrontRotation;
        }

        pageTurnRig?.SetProgress(
            0f);

        pageTurn01 = 0f;

        if (visualGroup != null)
            visualGroup.alpha = 0.91f;
    }
}



internal sealed class BattleScriptPaperWaveEffect :
    BaseMeshEffect
{
    private RectTransform waveRoot;
    private bool subdividePage;
    private float phase;
    private float cyclesPerSecond = 0.38f;
    private float amplitudePixels;
    private float shearPixels;
    private float amplitudeMultiplier = 1f;
    private float pageCurl01;
    private float pageCurlPixels;
    private float pageCurlDepthPixels;

    private const int HorizontalSegments = 14;
    private const int VerticalSegments = 24;

    public void Configure(
        RectTransform root,
        bool subdivide,
        float initialPhase,
        float cycles,
        float multiplier)
    {
        waveRoot = root;
        subdividePage = subdivide;
        phase = initialPhase;
        cyclesPerSecond =
            Mathf.Max(
                0.01f,
                cycles);

        amplitudeMultiplier =
            Mathf.Clamp01(
                multiplier);

        if (graphic != null)
            graphic.SetVerticesDirty();
    }

    public void SetState(
        float amplitude,
        float shear,
        float wavePhase)
    {
        amplitudePixels =
            Mathf.Max(
                0f,
                amplitude) *
            amplitudeMultiplier;

        shearPixels =
            shear;

        phase =
            wavePhase;

        if (graphic != null)
            graphic.SetVerticesDirty();
    }


    public void SetPageCurl(
        float normalized,
        float curlPixels,
        float curlDepthPixels)
    {
        pageCurl01 =
            Mathf.Clamp01(
                normalized);

        pageCurlPixels =
            Mathf.Max(
                0f,
                curlPixels);

        pageCurlDepthPixels =
            Mathf.Max(
                0f,
                curlDepthPixels);

        if (graphic != null)
            graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(
        VertexHelper vh)
    {
        if (!IsActive() ||
            vh == null ||
            waveRoot == null)
        {
            return;
        }

        if (subdividePage &&
            graphic is Image)
        {
            BuildSubdividedPage(
                vh);
        }

        float timePhase =
            Time.unscaledTime *
            cyclesPerSecond *
            Mathf.PI *
            2f +
            phase;

        Rect rootRect =
            waveRoot.rect;

        float width =
            Mathf.Max(
                1f,
                rootRect.width);

        float height =
            Mathf.Max(
                1f,
                rootRect.height);

        UIVertex vertex =
            new();

        for (int i = 0;
             i < vh.currentVertCount;
             i++)
        {
            vh.PopulateUIVertex(
                ref vertex,
                i);

            Vector3 world =
                transform.TransformPoint(
                    vertex.position);

            Vector3 rootLocal =
                waveRoot.InverseTransformPoint(
                    world);

            float u =
                Mathf.Clamp01(
                    (rootLocal.x -
                     rootRect.xMin) /
                    width);

            float v =
                Mathf.Clamp01(
                    (rootLocal.y -
                     rootRect.yMin) /
                    height);

            float edge =
                Mathf.Pow(
                    Mathf.Clamp01(
                        Mathf.Abs(
                            u - 0.5f) *
                        2f),
                    1.35f);

            float edgeWeight =
                Mathf.Lerp(
                    0.30f,
                    1f,
                    edge);

            float primary =
                Mathf.Sin(
                    u *
                    Mathf.PI *
                    2.15f +
                    timePhase);

            float secondary =
                Mathf.Sin(
                    u *
                    Mathf.PI *
                    4.10f -
                    timePhase *
                    0.63f +
                    0.9f);

            float verticalWave =
                (primary *
                 0.78f +
                 secondary *
                 0.22f) *
                amplitudePixels *
                edgeWeight;

            float crossWave =
                Mathf.Sin(
                    v *
                    Mathf.PI *
                    1.35f +
                    timePhase *
                    0.71f +
                    1.2f) *
                amplitudePixels *
                0.16f *
                edgeWeight;

            rootLocal.y +=
                verticalWave;

            rootLocal.x +=
                crossWave;

            rootLocal.x +=
                shearPixels *
                (v - 0.5f);

            if (pageCurl01 > 0.0001f)
            {
                // True paper lift in the Y/Z plane.
                // v=1 is pinned to the stack, v=0 is the free edge.
                float progress =
                    Mathf.Clamp01(
                        pageCurl01);

                float down01 =
                    1f - v;

                // Do not bend the entire sheet uniformly from frame one.
                // Curvature grows from the free edge and travels toward the top.
                float activeLength =
                    Mathf.Lerp(
                        0.24f,
                        1f,
                        progress);

                float active =
                    Mathf.Clamp01(
                        down01 /
                        Mathf.Max(
                            0.001f,
                            activeLength));

                float bendProgress =
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        progress);

                float maxAngle =
                    Mathf.Lerp(
                        0.18f,
                        Mathf.PI * 0.92f,
                        bendProgress);

                float theta =
                    maxAngle *
                    active;

                float arcLength =
                    height *
                    activeLength;

                float radius =
                    arcLength /
                    Mathf.Max(
                        0.001f,
                        maxAngle);

                float topY =
                    rootRect.yMax;

                float flatRemainder =
                    Mathf.Max(
                        0f,
                        down01 -
                        activeLength) *
                    height;

                float arcDown =
                    radius *
                    Mathf.Sin(
                        theta);

                rootLocal.y =
                    topY -
                    flatRemainder -
                    arcDown;

                // Give the arc real depth. Resolve which local Z direction faces
                // the camera so this works regardless of how the world-space
                // Canvas is oriented.
                float cameraSide = -1f;

                Camera camera =
                    Camera.main;

                if (camera != null)
                {
                    float cameraLocalZ =
                        waveRoot.InverseTransformPoint(
                            camera.transform.position).z;

                    if (Mathf.Abs(cameraLocalZ) > 0.0001f)
                    {
                        cameraSide =
                            Mathf.Sign(
                                cameraLocalZ);
                    }
                }

                float arcDepth =
                    radius *
                    (1f -
                     Mathf.Cos(
                         theta));

                float depthScale =
                    pageCurlDepthPixels /
                    Mathf.Max(
                        1f,
                        height * 0.25f);

                rootLocal.z +=
                    cameraSide *
                    arcDepth *
                    depthScale;

                float turnBell =
                    Mathf.Sin(
                        progress *
                        Mathf.PI);

                // A hand does not pull the whole edge equally. Let the right side
                // lead slightly, matching the reference where one corner is held.
                float handBias =
                    Mathf.SmoothStep(
                        0.42f,
                        1f,
                        u) *
                    pageCurlPixels *
                    0.16f *
                    turnBell *
                    Mathf.Pow(
                        down01,
                        1.45f);

                rootLocal.y +=
                    handBias;

                rootLocal.z +=
                    cameraSide *
                    handBias *
                    0.46f;

                // Very small lateral bow keeps the silhouette from reading as a
                // perfectly rigid cylinder.
                float lateralBow =
                    (u - 0.5f) *
                    Mathf.Sin(
                        theta) *
                    pageCurlPixels *
                    0.085f;

                rootLocal.x +=
                    lateralBow;

                float facing =
                    Mathf.Abs(
                        Mathf.Cos(
                            theta));

                float shade =
                    Mathf.Lerp(
                        0.72f,
                        1f,
                        facing);

                Color shaded =
                    vertex.color;

                shaded.r *= shade;
                shaded.g *= shade;
                shaded.b *= shade;

                vertex.color =
                    shaded;
            }

            Vector3 deformedWorld =
                waveRoot.TransformPoint(
                    rootLocal);

            vertex.position =
                transform.InverseTransformPoint(
                    deformedWorld);

            vh.SetUIVertex(
                vertex,
                i);
        }
    }

    private void BuildSubdividedPage(
        VertexHelper vh)
    {
        Rect rect =
            graphic.rectTransform.rect;

        Color32 color =
            graphic.color;

        vh.Clear();

        for (int y = 0;
             y <= VerticalSegments;
             y++)
        {
            float v =
                y /
                (float)VerticalSegments;

            float py =
                Mathf.Lerp(
                    rect.yMin,
                    rect.yMax,
                    v);

            for (int x = 0;
                 x <= HorizontalSegments;
                 x++)
            {
                float u =
                    x /
                    (float)HorizontalSegments;

                float px =
                    Mathf.Lerp(
                        rect.xMin,
                        rect.xMax,
                        u);

                UIVertex vertex =
                    UIVertex.simpleVert;

                vertex.position =
                    new Vector3(
                        px,
                        py,
                        0f);

                vertex.color =
                    color;

                vertex.uv0 =
                    new Vector2(
                        u,
                        v);

                vh.AddVert(
                    vertex);
            }
        }

        int stride =
            HorizontalSegments + 1;

        for (int y = 0;
             y < VerticalSegments;
             y++)
        {
            for (int x = 0;
                 x < HorizontalSegments;
                 x++)
            {
                int a =
                    y *
                    stride +
                    x;

                int b =
                    a + 1;

                int c =
                    a + stride;

                int d =
                    c + 1;

                vh.AddTriangle(
                    a,
                    c,
                    b);

                vh.AddTriangle(
                    b,
                    c,
                    d);
            }
        }
    }
}


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

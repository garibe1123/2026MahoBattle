using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public enum BattleStageFlowState
{
    Base,
    ShowEntering,
    RewardShow,
    MapShow,
    ShowExiting,
    RoomEntering,
    Combat,
    RoomExiting,
    NonCombat,
    Ended
}

/// <summary>
/// Battle stage physical-flow state machine.
///
/// BattleRunManager owns logical run rules. This controller owns the physical stage order:
/// Base -> Room Enter -> Combat -> Room Exit -> Base -> Reward/Map Show -> Show Exit -> Base.
///
/// Important invariant:
/// - Room field and Show carriers never enter at the same time.
/// - Reward/Map Show opens only after the combat field collapse is complete.
/// - A selected next Room does not begin building until the current Show has completely exited.
/// - Persistent 4x4 is the common hand-off point between every physical stage.
/// - Procedural Assembly is an Entry transport only. Exit planning is performed on the actual nested MapBlock pieces.
/// </summary>
[DefaultExecutionOrder(-15000)]
[DisallowMultipleComponent]
public sealed class BattleStageTransitionController : MonoBehaviour
{
    private enum CollapseExitSide
    {
        Left,
        Right,
        Down,
        Up
    }

    private sealed class CollapseExitPlan
    {
        public MapBlock block;
        public Bounds bounds;
        public List<Bounds> floorBounds;
        public int entryOrder;
        public CollapseExitSide side;
        public Vector2 direction;
        public float outwardDistance;
        public float exitTravelDistance;
        public float exitDuration;
        public bool directPieceTween;
        public bool hasPreferredExitDirection;
        public Vector2 preferredExitDirection;
    }

    private sealed class FloorVisualSnapshot
    {
        public Sprite sprite;
        public Color color;
    }

    private static BattleStageTransitionController instance;

    [Header("Persistent Base")]
    [SerializeField] private int persistentBaseFloorSorting = -19;

    [Header("Combat Clear Collapse")]
    [Tooltip("4x4 밖 Room Block이 바깥쪽부터 다음 묶음으로 빠져나가기 시작하는 간격입니다.")]
    [SerializeField, Min(0f)] private float collapseExitStagger = 0.10f;
    [Tooltip("마지막 Block 퇴장 뒤 Show Floor를 열기 전 짧은 간격입니다.")]
    [SerializeField, Min(0f)] private float showOpenDelayAfterCollapse = 0.08f;

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;
    private BattleShowWorldSetController showStage;

    private Vector3 preservedBaseTileOrigin;
    private Vector3 showAnchorCenter;
    private bool hasPreservedBaseOrigin;
    private bool hasShowAnchor;
    private bool preparedForIncomingNode;
    private bool selectionCollapsePrepared;
    private bool subscribed;
    private bool showStageGateHeld;

    private Coroutine bindRoutine;
    private Coroutine collapseRoutine;
    private Coroutine queuedNodeEntryRoutine;
    private BattleNodeData queuedNode;
    private BattleStageFlowState flowState = BattleStageFlowState.Base;
    private BattleStageFlowState pendingShowState = BattleStageFlowState.MapShow;

    public static BattleStageTransitionController Instance => instance;
    public bool HasShowAnchor => hasShowAnchor;
    public Vector3 ShowAnchorCenter => showAnchorCenter;
    public BattleStageFlowState FlowState => flowState;
    public bool IsStageTransitioning =>
        flowState == BattleStageFlowState.ShowEntering ||
        flowState == BattleStageFlowState.ShowExiting ||
        flowState == BattleStageFlowState.RoomEntering ||
        flowState == BattleStageFlowState.RoomExiting;
    public bool IsShowPhase =>
        flowState == BattleStageFlowState.ShowEntering ||
        flowState == BattleStageFlowState.RewardShow ||
        flowState == BattleStageFlowState.MapShow ||
        flowState == BattleStageFlowState.ShowExiting;
    public bool IsCombatPhase => flowState == BattleStageFlowState.Combat;
    public bool IsPreCombatPhase => flowState == BattleStageFlowState.RoomEntering;

    public event System.Action<BattleStageFlowState> FlowStateChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleStageTransitionController>() != null)
            return;

        GameObject host = new("BattleStageFlowRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleStageTransitionController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
    }

    private void OnEnable()
    {
        if (bindRoutine == null)
            bindRoutine = StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
            StopCoroutine(bindRoutine);
        if (collapseRoutine != null)
            StopCoroutine(collapseRoutine);
        if (queuedNodeEntryRoutine != null)
            StopCoroutine(queuedNodeEntryRoutine);

        bindRoutine = null;
        collapseRoutine = null;
        queuedNodeEntryRoutine = null;
        queuedNode = null;
        ReleaseShowStageGate();
        Unsubscribe();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private IEnumerator BindWhenReady()
    {
        while (enabled)
        {
            ResolveSystems();
            if (runManager != null && roomManager != null && baseTemplate != null && player != null)
                break;
            yield return null;
        }

        if (!enabled)
        {
            bindRoutine = null;
            yield break;
        }

        Subscribe();
        baseTemplate.EnsurePersistentBase();
        CaptureCurrentBase();
        CaptureShowAnchorFromBase();
        EnsureBaseVisible();
        EnsurePlayerVisible();
        HandleStateChanged(runManager.State);
        bindRoutine = null;
    }

    private void Update()
    {
        ResolveSystems();

        if (flowState != BattleStageFlowState.ShowEntering || showStage == null || showStage.IsTransitioning)
            return;

        if (pendingShowState == BattleStageFlowState.RewardShow && showStage.IsRewardMode)
            SetFlowState(BattleStageFlowState.RewardShow);
        else if (pendingShowState == BattleStageFlowState.MapShow && showStage.IsMapMode)
            SetFlowState(BattleStageFlowState.MapShow);
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
        if (showStage == null)
            showStage = FindFirstObjectByType<BattleShowWorldSetController>();
    }

    private void Subscribe()
    {
        if (subscribed || runManager == null)
            return;

        runManager.StateChanged += HandleStateChanged;
        runManager.NodeEntered += HandleNodeEntered;
        runManager.RunEnded += HandleRunEnded;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || runManager == null)
            return;

        runManager.StateChanged -= HandleStateChanged;
        runManager.NodeEntered -= HandleNodeEntered;
        runManager.RunEnded -= HandleRunEnded;
        subscribed = false;
    }

    private void HandleStateChanged(BattleRunState next)
    {
        ResolveSystems();

        switch (next)
        {
            case BattleRunState.Reward:
                EnsureBaseVisible();
                EnsurePlayerVisible();
                BeginSelectionCollapseIfNeeded(BattleStageFlowState.RewardShow);
                return;

            case BattleRunState.SelectingNode:
                EnsureBaseVisible();
                EnsurePlayerVisible();

                if (!selectionCollapsePrepared && HasCurrentRoomFieldToRetire())
                {
                    BeginSelectionCollapseIfNeeded(BattleStageFlowState.MapShow);
                }
                else
                {
                    pendingShowState = BattleStageFlowState.MapShow;
                    CaptureShowAnchorFromBase();
                    OpenShowStage();
                }
                return;

            case BattleRunState.EnteringNode:
                HoldShowStageGate();
                SetFlowState(BattleStageFlowState.RoomEntering);

                if (HasCurrentRoomFieldToRetire() && !preparedForIncomingNode)
                    PromoteNextBaseAroundPlayer();

                if (!hasPreservedBaseOrigin)
                    CaptureCurrentBase();

                EnsureBaseVisible();
                EnsurePlayerVisible();
                ApplyBaseOriginToRoomManager();
                return;

            case BattleRunState.BuildingRoom:
                HoldShowStageGate();
                SetFlowState(BattleStageFlowState.RoomEntering);
                return;

            case BattleRunState.Combat:
                HoldShowStageGate();
                SetFlowState(BattleStageFlowState.Combat);
                return;

            case BattleRunState.NonCombat:
                HoldShowStageGate();
                SetFlowState(BattleStageFlowState.NonCombat);
                return;

            case BattleRunState.Ended:
                SetFlowState(BattleStageFlowState.Ended);
                return;

            case BattleRunState.None:
                ReleaseShowStageGate();
                SetFlowState(BattleStageFlowState.Base);
                return;
        }
    }

    public bool TryQueueNodeEntry(BattleNodeData node)
    {
        ResolveSystems();

        if (node == null || runManager == null || !runManager.RunActive)
            return false;
        if (queuedNodeEntryRoutine != null)
            return true;

        if (showStage == null || !showStage.IsShowActive)
            return false;

        queuedNode = node;
        queuedNodeEntryRoutine = StartCoroutine(ExitShowThenEnterQueuedNode());
        return true;
    }

    private IEnumerator ExitShowThenEnterQueuedNode()
    {
        SetFlowState(BattleStageFlowState.ShowExiting);
        HoldShowStageGate();

        while (showStage != null && showStage.IsShowActive)
            yield return null;

        BattleNodeData node = queuedNode;
        queuedNode = null;
        queuedNodeEntryRoutine = null;

        SetFlowState(BattleStageFlowState.Base);

        if (node != null && runManager != null && runManager.RunActive)
            runManager.ContinueEnterNodeFromStageFlow(node);
    }

    private void BeginSelectionCollapseIfNeeded(BattleStageFlowState showState)
    {
        ResolveSystems();
        pendingShowState = showState;

        if (collapseRoutine != null)
            return;

        if (selectionCollapsePrepared)
        {
            OpenShowStage();
            return;
        }

        if (roomManager == null || player == null || baseTemplate == null)
        {
            CaptureShowAnchorFromBase();
            OpenShowStage();
            return;
        }

        if (!HasCurrentRoomFieldToRetire())
        {
            CaptureShowAnchorFromBase();
            OpenShowStage();
            return;
        }

        HoldShowStageGate();
        SetFlowState(BattleStageFlowState.RoomExiting);
        collapseRoutine = StartCoroutine(CollapseClearedRoomToPlayerBase());
    }

    private void OpenShowStage()
    {
        if (hasShowAnchor)
            showStage?.SetStageAnchor(showAnchorCenter);

        SetFlowState(BattleStageFlowState.ShowEntering);
        ReleaseShowStageGate();
    }

    private IEnumerator CollapseClearedRoomToPlayerBase()
    {
        List<MapBlock> outgoingRoots = CollectCurrentRoomExitBlocks();
        if (outgoingRoots.Count == 0)
        {
            Debug.LogWarning(
                "[BattleStageFlow] RoomExiting started but BattleRoomManager owns no movement roots. " +
                "Retiring empty room ownership before opening the show.",
                this);

            roomManager?.CompleteAnimatedStageRetirement();
            CaptureShowAnchorFromBase();
            collapseRoutine = null;
            OpenShowStage();
            yield break;
        }

        baseTemplate.EnsurePersistentBase();
        Vector3 nextOrigin = FindBestFourByFourOrigin(player.transform.position);
        nextOrigin.z = baseTemplate.FixedTileOriginWorld.z;

        FloorVisualSnapshot[] preservedFloorVisuals = CaptureFourByFourFloorVisuals(nextOrigin, outgoingRoots);

        preservedBaseTileOrigin = baseTemplate.PromoteToNewBaseAtTileOrigin(nextOrigin);
        hasPreservedBaseOrigin = true;
        preparedForIncomingNode = true;
        selectionCollapsePrepared = true;

        EnsureBaseVisible();
        EnsurePlayerVisible();
        BattleShowPresentationManager.Instance?.RefreshPersistentBaseArt();
        RestoreFourByFourFloorVisuals(preservedFloorVisuals);
        ApplyBaseOriginToRoomManager();
        CaptureShowAnchorFromBase();
        BattleDockHandleVisibilityController.RefreshNow();

        Vector2 baseCenter = showAnchorCenter;
        Bounds baseBounds = CreatePersistentBaseBounds(baseCenter);

        MaskPersistentBaseFloorDuplicates(outgoingRoots, baseBounds);

        // Entry용 Procedural Assembly는 여기서 역할이 끝납니다.
        // 실제 Exit 단위는 1x1 / L / 자유형 AssemblySubPiece MapBlock입니다.
        List<CollapseExitPlan> exitUnits = ExpandRoomExitUnits(outgoingRoots);
        List<CollapseExitPlan> exitOrder = BuildSafeExitOrder(exitUnits, baseBounds);

        if (exitOrder.Count != exitUnits.Count)
        {
            Debug.LogError(
                $"[BattleStageFlow] Could only resolve {exitOrder.Count}/{exitUnits.Count} safe PIECE exit rails. " +
                "Assembly roots are no longer used for this check; an actual visible MapBlock piece has no clear cardinal rail.",
                this);
            collapseRoutine = null;
            yield break;
        }

        Debug.Log(
            $"[BattleStageFlow] Retiring {exitOrder.Count} visible combat room piece(s) sequentially before {pendingShowState}.",
            this);

        float stagger = Mathf.Max(0f, collapseExitStagger);
        for (int i = 0; i < exitOrder.Count; i++)
        {
            CollapseExitPlan plan = exitOrder[i];
            if (plan == null || plan.block == null)
                continue;

            PushOutgoingRenderersBehindBase(plan.block);
            DisableOutgoingWalkable(plan.block);

            Debug.Log(
                $"[BattleStageFlow] Safe piece exit {i + 1}/{exitOrder.Count}: '{plan.block.name}' -> {plan.side}.",
                plan.block);

            Tween exitTween;
            if (plan.directPieceTween)
            {
                plan.block.transform.DOKill();
                Vector3 destination = plan.block.transform.position +
                                      (Vector3)(plan.direction.normalized * Mathf.Max(0.01f, plan.exitTravelDistance));
                exitTween = plan.block.transform
                    .DOMove(destination, Mathf.Max(0.01f, plan.exitDuration))
                    .SetEase(Ease.InQuad)
                    .SetUpdate(true);
            }
            else
            {
                exitTween = plan.block.PlayExit(plan.direction);
                exitTween?.SetUpdate(true);
            }

            if (plan.exitDuration > 0f)
                yield return new WaitForSecondsRealtime(plan.exitDuration);

            if (plan.block != null)
            {
                plan.block.gameObject.SetActive(false);
                Destroy(plan.block.gameObject);
            }

            if (stagger > 0f && i < exitOrder.Count - 1)
                yield return new WaitForSecondsRealtime(stagger);
        }

        yield return null;
        roomManager?.CompleteAnimatedStageRetirement();

        BattleDockHandleVisibilityController.RefreshNow();

        if (showOpenDelayAfterCollapse > 0f)
            yield return new WaitForSecondsRealtime(showOpenDelayAfterCollapse);

        collapseRoutine = null;
        OpenShowStage();
    }

    private bool HasCurrentRoomFieldToRetire()
    {
        if (roomManager == null)
            return false;

        List<MapBlock> ownedBlocks = new();
        if (roomManager.CopyActiveRoomBlocks(ownedBlocks) > 0)
            return true;

        if (roomManager.IsRoomActive)
            return true;

        return CollectCurrentRoomExitBlocksFallback().Count > 0;
    }

    private List<MapBlock> CollectCurrentRoomExitBlocks()
    {
        List<MapBlock> ownedBlocks = new();
        if (roomManager != null && roomManager.CopyActiveRoomBlocks(ownedBlocks) > 0)
            return ownedBlocks;

        return CollectCurrentRoomExitBlocksFallback();
    }

    private List<MapBlock> CollectCurrentRoomExitBlocksFallback()
    {
        MapBlock[] blocks = FindObjectsByType<MapBlock>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        List<MapBlock> result = new();
        HashSet<int> seen = new();

        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (block == null)
                continue;

            MapBlock movementRoot = ResolveMovementRoot(block);
            if (movementRoot == null || !movementRoot.gameObject.activeInHierarchy)
                continue;

            string blockName = movementRoot.name;
            bool rawPrototype =
                blockName.StartsWith("__RuntimeRoomPiecePrototype_", System.StringComparison.Ordinal) &&
                !blockName.EndsWith("(Clone)", System.StringComparison.Ordinal);
            if (rawPrototype || blockName.StartsWith("Outgoing_", System.StringComparison.Ordinal))
                continue;

            if (showStage != null && movementRoot.transform.IsChildOf(showStage.transform))
                continue;

            bool proceduralAssembly = blockName.StartsWith("ProceduralAssemblyGroup_", System.StringComparison.Ordinal);
            bool hasNestedMapBlock = HasNestedMapBlock(movementRoot);

            if (!proceduralAssembly && !hasNestedMapBlock && !movementRoot.ContributesWalkableNavMesh)
                continue;

            int id = movementRoot.GetInstanceID();
            if (seen.Add(id))
                result.Add(movementRoot);
        }

        return result;
    }

    private static MapBlock ResolveMovementRoot(MapBlock block)
    {
        if (block == null)
            return null;

        MapBlock result = block;
        Transform current = block.transform.parent;
        while (current != null)
        {
            MapBlock parentBlock = current.GetComponent<MapBlock>();
            if (parentBlock != null)
                result = parentBlock;
            current = current.parent;
        }

        return result;
    }

    private static bool HasNestedMapBlock(MapBlock root)
    {
        if (root == null)
            return false;

        MapBlock[] nested = root.GetComponentsInChildren<MapBlock>(true);
        for (int i = 0; i < nested.Length; i++)
        {
            if (nested[i] != null && nested[i] != root)
                return true;
        }

        return false;
    }

    private FloorVisualSnapshot[] CaptureFourByFourFloorVisuals(
        Vector3 lowerLeftTileOrigin,
        List<MapBlock> knownRoomBlocks = null)
    {
        int tileCount = RoomBaseTemplate.FixedBaseTiles * RoomBaseTemplate.FixedBaseTiles;
        FloorVisualSnapshot[] snapshots = new FloorVisualSnapshot[tileCount];
        int[] ranks = new int[tileCount];
        for (int i = 0; i < ranks.Length; i++)
            ranks[i] = int.MinValue;

        Vector2Int originCell = WorldToTile(lowerLeftTileOrigin);

        if (baseTemplate != null && baseTemplate.ActiveBase != null)
            CaptureFloorRenderers(baseTemplate.ActiveBase, originCell, snapshots, ranks);

        List<MapBlock> blocks = knownRoomBlocks ?? CollectCurrentRoomExitBlocks();
        for (int i = 0; i < blocks.Count; i++)
        {
            MapBlock block = blocks[i];
            if (block != null)
                CaptureFloorRenderers(block.gameObject, originCell, snapshots, ranks);
        }

        return snapshots;
    }

    private static void CaptureFloorRenderers(
        GameObject root,
        Vector2Int originCell,
        FloorVisualSnapshot[] snapshots,
        int[] ranks)
    {
        if (root == null || snapshots == null || ranks == null)
            return;

        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || renderer.sprite == null)
                continue;
            if (!IsFloorVisualName(renderer.gameObject.name))
                continue;

            Vector2Int cell = WorldToTile(renderer.transform.position);
            int localX = cell.x - originCell.x;
            int localY = cell.y - originCell.y;
            if (localX < 0 || localX >= RoomBaseTemplate.FixedBaseTiles ||
                localY < 0 || localY >= RoomBaseTemplate.FixedBaseTiles)
                continue;

            int index = localY * RoomBaseTemplate.FixedBaseTiles + localX;
            int layerRank = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID);
            int rank = layerRank * 100000 + renderer.sortingOrder;
            if (rank < ranks[index])
                continue;

            ranks[index] = rank;
            snapshots[index] = new FloorVisualSnapshot
            {
                sprite = renderer.sprite,
                color = renderer.color
            };
        }
    }

    private void RestoreFourByFourFloorVisuals(FloorVisualSnapshot[] snapshots)
    {
        if (snapshots == null || baseTemplate == null || baseTemplate.ActiveBase == null)
            return;

        Transform presentationRoot = baseTemplate.ActiveBase.transform.Find("PresentationTemplate");
        if (presentationRoot == null)
            return;

        for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
        {
            for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
            {
                int index = y * RoomBaseTemplate.FixedBaseTiles + x;
                FloorVisualSnapshot snapshot = snapshots[index];
                if (snapshot == null || snapshot.sprite == null)
                    continue;

                Transform floor = presentationRoot.Find($"BaseFloor_{x}_{y}");
                SpriteRenderer renderer = floor != null ? floor.GetComponent<SpriteRenderer>() : null;
                if (renderer == null)
                    continue;

                renderer.sprite = snapshot.sprite;
                renderer.color = snapshot.color;
            }
        }
    }

    private static bool IsFloorVisualName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return false;

        return objectName.StartsWith("Tile_", System.StringComparison.Ordinal) ||
               objectName.StartsWith("ShowTile_", System.StringComparison.Ordinal) ||
               objectName.StartsWith("BaseFloor_", System.StringComparison.Ordinal);
    }

    private Bounds CreatePersistentBaseBounds(Vector2 baseCenter)
    {
        float size = RoomBaseTemplate.FixedBaseTiles * RoomBaseTemplate.TileWorldSize;
        return new Bounds(
            new Vector3(baseCenter.x, baseCenter.y, 0f),
            new Vector3(size, size, 0.1f));
    }

    private static void MaskPersistentBaseFloorDuplicates(List<MapBlock> blocks, Bounds baseBounds)
    {
        if (blocks == null)
            return;

        for (int i = 0; i < blocks.Count; i++)
        {
            MapBlock block = blocks[i];
            if (block == null)
                continue;

            SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                SpriteRenderer renderer = renderers[r];
                if (renderer == null || !renderer.enabled || renderer.sprite == null)
                    continue;

                bool floor = IsFloorVisualName(renderer.gameObject.name) ||
                             renderer.GetComponent<BattleWalkableField>() != null;
                if (!floor)
                    continue;

                Vector3 center = renderer.bounds.center;
                bool inside = center.x >= baseBounds.min.x && center.x <= baseBounds.max.x &&
                              center.y >= baseBounds.min.y && center.y <= baseBounds.max.y;
                if (inside)
                    renderer.enabled = false;
            }
        }
    }

    private List<CollapseExitPlan> ExpandRoomExitUnits(List<MapBlock> roots)
    {
        List<CollapseExitPlan> result = new();
        if (roots == null)
            return result;

        for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            MapBlock root = roots[rootIndex];
            if (root == null)
                continue;

            List<MapBlock> pieces = CollectDirectNestedMapBlocks(root);
            if (pieces.Count == 0)
            {
                List<Bounds> floorBounds = CollectExitFloorBounds(root);
                if (floorBounds.Count == 0)
                {
                    root.gameObject.SetActive(false);
                    Destroy(root.gameObject);
                    continue;
                }

                result.Add(CreateExitPlan(
                    root,
                    floorBounds,
                    rootIndex * 1000,
                    root.ExitTravelDistance,
                    root.ExitDuration,
                    false,
                    root.HasEntrySourceDirection,
                    root.LastEntrySourceDirection));
                continue;
            }

            Transform detachParent = root.transform.parent;
            bool hasPreferred = root.HasEntrySourceDirection;
            Vector2 preferred = root.LastEntrySourceDirection;
            float travelDistance = Mathf.Max(0.01f, root.ExitTravelDistance);
            float duration = Mathf.Max(0.01f, root.ExitDuration);

            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                MapBlock piece = pieces[pieceIndex];
                if (piece == null)
                    continue;

                piece.transform.DOKill();
                piece.transform.SetParent(detachParent, true);

                List<Bounds> floorBounds = CollectExitFloorBounds(piece);
                if (floorBounds.Count == 0)
                {
                    // 이 Piece의 Floor는 전부 새 Persistent 4x4로 흡수된 상태입니다.
                    // 기존 복제본은 더 이상 퇴장시킬 시각 정보가 없으므로 즉시 정리합니다.
                    piece.gameObject.SetActive(false);
                    Destroy(piece.gameObject);
                    continue;
                }

                result.Add(CreateExitPlan(
                    piece,
                    floorBounds,
                    rootIndex * 1000 + pieceIndex,
                    travelDistance,
                    duration,
                    true,
                    hasPreferred,
                    preferred));
            }

            // Entry Transport 부모는 Piece를 모두 분리한 뒤 더 이상 이동/렌더 단위가 아닙니다.
            root.transform.DOKill();
            root.gameObject.SetActive(false);
            Destroy(root.gameObject);
        }

        return result;
    }

    private static CollapseExitPlan CreateExitPlan(
        MapBlock block,
        List<Bounds> floorBounds,
        int entryOrder,
        float travelDistance,
        float duration,
        bool directPieceTween,
        bool hasPreferredDirection,
        Vector2 preferredDirection)
    {
        Bounds aggregate = ResolveAggregateBounds(floorBounds, ResolveBlockBounds(block));
        return new CollapseExitPlan
        {
            block = block,
            bounds = aggregate,
            floorBounds = floorBounds,
            entryOrder = entryOrder,
            exitTravelDistance = Mathf.Max(0.01f, travelDistance),
            exitDuration = Mathf.Max(0.01f, duration),
            directPieceTween = directPieceTween,
            hasPreferredExitDirection = hasPreferredDirection,
            preferredExitDirection = preferredDirection
        };
    }

    private static List<MapBlock> CollectDirectNestedMapBlocks(MapBlock root)
    {
        List<MapBlock> result = new();
        if (root == null)
            return result;

        MapBlock[] nested = root.GetComponentsInChildren<MapBlock>(true);
        for (int i = 0; i < nested.Length; i++)
        {
            MapBlock piece = nested[i];
            if (piece == null || piece == root)
                continue;

            Transform current = piece.transform.parent;
            bool nestedUnderAnotherBlock = false;
            while (current != null && current != root.transform)
            {
                if (current.GetComponent<MapBlock>() != null)
                {
                    nestedUnderAnotherBlock = true;
                    break;
                }
                current = current.parent;
            }

            if (!nestedUnderAnotherBlock && current == root.transform)
                result.Add(piece);
        }

        return result;
    }

    private List<CollapseExitPlan> BuildSafeExitOrder(List<CollapseExitPlan> exitUnits, Bounds baseBounds)
    {
        List<CollapseExitPlan> remaining = exitUnits != null
            ? new List<CollapseExitPlan>(exitUnits)
            : new List<CollapseExitPlan>();

        List<CollapseExitPlan> result = new();
        int[] sideUsage = new int[4];
        float baseSpan = Mathf.Max(baseBounds.size.x, baseBounds.size.y);

        while (remaining.Count > 0)
        {
            CollapseExitPlan chosen = null;
            CollapseExitSide chosenSide = CollapseExitSide.Left;
            float chosenScore = float.PositiveInfinity;

            for (int index = remaining.Count - 1; index >= 0; index--)
            {
                CollapseExitPlan plan = remaining[index];
                for (int sideIndex = 0; sideIndex < 4; sideIndex++)
                {
                    CollapseExitSide side = (CollapseExitSide)sideIndex;
                    Vector2 direction = DirectionFor(side);
                    if (!IsExitPathSafe(plan, direction, baseBounds, remaining))
                        continue;

                    float score = ResolveSafeExitScore(plan, side, baseBounds, sideUsage, baseSpan);
                    if (score >= chosenScore)
                        continue;

                    chosen = plan;
                    chosenSide = side;
                    chosenScore = score;
                }
            }

            if (chosen == null)
                break;

            chosen.side = chosenSide;
            chosen.direction = DirectionFor(chosenSide);
            chosen.outwardDistance = ResolveOutwardDistance(chosen.bounds, baseBounds, chosenSide);
            result.Add(chosen);
            sideUsage[(int)chosenSide]++;
            remaining.Remove(chosen);
        }

        return result;
    }

    private static float ResolveSafeExitScore(
        CollapseExitPlan plan,
        CollapseExitSide side,
        Bounds baseBounds,
        int[] sideUsage,
        float baseSpan)
    {
        Vector2 direction = DirectionFor(side);
        Vector2 fromBase = (Vector2)plan.bounds.center - (Vector2)baseBounds.center;
        float distance = fromBase.magnitude;
        float alignment = distance > 0.001f
            ? Vector2.Dot(fromBase / distance, direction)
            : 0f;

        float score = ResolveClearanceTravel(plan.bounds, baseBounds, side);
        score += (1f - Mathf.Max(0f, alignment)) * baseSpan * 0.45f;
        score += sideUsage[(int)side] * baseSpan * 0.35f;

        if (plan.hasPreferredExitDirection &&
            SameCardinalDirection(plan.preferredExitDirection, direction))
        {
            score -= baseSpan * 8f;
        }

        score -= plan.entryOrder * baseSpan * 0.00008f;
        return score;
    }

    private static bool IsExitPathSafe(
        CollapseExitPlan plan,
        Vector2 direction,
        Bounds baseBounds,
        List<CollapseExitPlan> remaining)
    {
        if (plan == null || plan.block == null || direction.sqrMagnitude <= 0.001f)
            return false;

        if (plan.floorBounds == null || plan.floorBounds.Count == 0)
            return true;

        Vector2 dir = direction.normalized;
        float distance = Mathf.Max(0.01f, plan.exitTravelDistance);
        List<Bounds> movingBounds = plan.floorBounds;

        for (int i = 0; i < movingBounds.Count; i++)
        {
            if (!PathDoesNotEnterObstacle(movingBounds[i], baseBounds, dir, distance, true))
                return false;
        }

        for (int r = 0; r < remaining.Count; r++)
        {
            CollapseExitPlan blocker = remaining[r];
            if (blocker == null || blocker == plan || blocker.floorBounds == null || blocker.floorBounds.Count == 0)
                continue;

            for (int m = 0; m < movingBounds.Count; m++)
            {
                for (int b = 0; b < blocker.floorBounds.Count; b++)
                {
                    if (!PathDoesNotEnterObstacle(movingBounds[m], blocker.floorBounds[b], dir, distance, false))
                        return false;
                }
            }
        }

        return true;
    }

    private static bool PathDoesNotEnterObstacle(
        Bounds movingStart,
        Bounds obstacle,
        Vector2 direction,
        float distance,
        bool requireOutwardWhenOverlapping)
    {
        const float clearance = 0.035f;
        const float overlapEpsilon = 0.0005f;

        float tileSize = Mathf.Max(0.1f, RoomBaseTemplate.TileWorldSize);
        int samples = Mathf.Clamp(
            Mathf.CeilToInt(distance / Mathf.Max(0.05f, tileSize * 0.28f)),
            14,
            256);

        float initialOverlap = OverlapArea2D(movingStart, obstacle, clearance);
        if (initialOverlap > overlapEpsilon && requireOutwardWhenOverlapping)
        {
            Vector2 fromObstacle = (Vector2)movingStart.center - (Vector2)obstacle.center;
            if (fromObstacle.sqrMagnitude > 0.0001f && Vector2.Dot(fromObstacle.normalized, direction) <= 0.05f)
                return false;
        }

        float previousOverlap = initialOverlap;
        for (int sample = 1; sample <= samples; sample++)
        {
            float t = sample / (float)samples;
            Bounds shifted = movingStart;
            shifted.center += (Vector3)(direction * (distance * t));
            float overlap = OverlapArea2D(shifted, obstacle, clearance);

            if (initialOverlap <= overlapEpsilon)
            {
                if (overlap > overlapEpsilon)
                    return false;
            }
            else if (overlap > previousOverlap + overlapEpsilon)
            {
                return false;
            }

            previousOverlap = overlap;
        }

        return true;
    }

    private static float OverlapArea2D(Bounds a, Bounds b, float clearance)
    {
        float safe = Mathf.Max(0f, clearance);
        float aMinX = a.min.x + safe;
        float aMaxX = a.max.x - safe;
        float aMinY = a.min.y + safe;
        float aMaxY = a.max.y - safe;
        float bMinX = b.min.x + safe;
        float bMaxX = b.max.x - safe;
        float bMinY = b.min.y + safe;
        float bMaxY = b.max.y - safe;

        float width = Mathf.Min(aMaxX, bMaxX) - Mathf.Max(aMinX, bMinX);
        float height = Mathf.Min(aMaxY, bMaxY) - Mathf.Max(aMinY, bMinY);
        if (width <= 0f || height <= 0f)
            return 0f;
        return width * height;
    }

    private static List<Bounds> CollectExitFloorBounds(MapBlock block)
    {
        List<Bounds> result = new();
        if (block == null)
            return result;

        float tileSize = Mathf.Max(0.1f, RoomBaseTemplate.TileWorldSize);
        float logicalSize = tileSize * 0.82f;
        HashSet<Vector2Int> occupiedCells = new();

        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || renderer.sprite == null)
                continue;

            bool floor = IsFloorVisualName(renderer.gameObject.name) ||
                         renderer.GetComponent<BattleWalkableField>() != null;
            if (!floor)
                continue;

            Vector2Int cell = WorldToTile(renderer.bounds.center);
            if (!occupiedCells.Add(cell))
                continue;

            Vector3 center = new(
                cell.x * tileSize,
                cell.y * tileSize,
                renderer.bounds.center.z);
            result.Add(new Bounds(
                center,
                new Vector3(logicalSize, logicalSize, 0.1f)));
        }

        return result;
    }

    private static Bounds ResolveAggregateBounds(List<Bounds> bounds, Bounds fallback)
    {
        if (bounds == null || bounds.Count == 0)
            return fallback;

        Bounds aggregate = bounds[0];
        for (int i = 1; i < bounds.Count; i++)
            aggregate.Encapsulate(bounds[i]);
        return aggregate;
    }

    private static bool SameCardinalDirection(Vector2 a, Vector2 b)
    {
        if (a.sqrMagnitude <= 0.001f || b.sqrMagnitude <= 0.001f)
            return false;

        Vector2 aa = Mathf.Abs(a.x) >= Mathf.Abs(a.y)
            ? (a.x >= 0f ? Vector2.right : Vector2.left)
            : (a.y >= 0f ? Vector2.up : Vector2.down);
        Vector2 bb = Mathf.Abs(b.x) >= Mathf.Abs(b.y)
            ? (b.x >= 0f ? Vector2.right : Vector2.left)
            : (b.y >= 0f ? Vector2.up : Vector2.down);
        return aa == bb;
    }

    private static float ResolveClearanceTravel(
        Bounds blockBounds,
        Bounds baseBounds,
        CollapseExitSide side)
    {
        switch (side)
        {
            case CollapseExitSide.Left:
                return Mathf.Max(0f, blockBounds.max.x - baseBounds.min.x);
            case CollapseExitSide.Right:
                return Mathf.Max(0f, baseBounds.max.x - blockBounds.min.x);
            case CollapseExitSide.Up:
                return Mathf.Max(0f, baseBounds.max.y - blockBounds.min.y);
            default:
                return Mathf.Max(0f, blockBounds.max.y - baseBounds.min.y);
        }
    }

    private static Vector2 DirectionFor(CollapseExitSide side)
    {
        switch (side)
        {
            case CollapseExitSide.Left: return Vector2.left;
            case CollapseExitSide.Right: return Vector2.right;
            case CollapseExitSide.Up: return Vector2.up;
            default: return Vector2.down;
        }
    }

    private static float ResolveOutwardDistance(Bounds blockBounds, Bounds baseBounds, CollapseExitSide side)
    {
        switch (side)
        {
            case CollapseExitSide.Left:
                return Mathf.Max(0f, baseBounds.min.x - blockBounds.center.x);
            case CollapseExitSide.Right:
                return Mathf.Max(0f, blockBounds.center.x - baseBounds.max.x);
            case CollapseExitSide.Up:
                return Mathf.Max(0f, blockBounds.center.y - baseBounds.max.y);
            default:
                return Mathf.Max(0f, baseBounds.min.y - blockBounds.center.y);
        }
    }

    private static Bounds ResolveBlockBounds(MapBlock block)
    {
        bool found = false;
        Bounds bounds = default;

        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null)
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

        if (found)
            return bounds;

        Collider2D[] colliders = block.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.enabled)
                continue;

            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (found)
            return bounds;

        return new Bounds(block.transform.position, MapBlock.BlockWorldSize);
    }

    private void PushOutgoingRenderersBehindBase(MapBlock block)
    {
        if (block == null)
            return;

        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        if (renderers.Length == 0)
            return;

        int highest = int.MinValue;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                highest = Mathf.Max(highest, renderers[i].sortingOrder);
        }

        if (highest == int.MinValue)
            return;

        int targetHighest = persistentBaseFloorSorting - 2;
        int shift = targetHighest - highest;
        if (shift >= 0)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].sortingOrder += shift;
        }
    }

    private static void DisableOutgoingWalkable(MapBlock block)
    {
        if (block == null)
            return;

        BattleWalkableField[] fields = block.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i] != null)
                fields[i].enabled = false;
        }
    }

    private void HoldShowStageGate()
    {
        ResolveSystems();
        showStageGateHeld = true;
        showStage?.SetExternalGate(true);
    }

    private void ReleaseShowStageGate()
    {
        ResolveSystems();

        if (hasShowAnchor)
            showStage?.SetStageAnchor(showAnchorCenter);

        showStageGateHeld = false;
        showStage?.SetExternalGate(false);
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node == null || node.room == null)
            return;
        if (node.type != BattleNodeType.Combat && node.type != BattleNodeType.Elite)
            return;

        ResolveSystems();
        if (baseTemplate == null)
            return;

        if (!hasPreservedBaseOrigin)
        {
            baseTemplate.EnsurePersistentBase();
            CaptureCurrentBase();
        }

        if (hasPreservedBaseOrigin)
            baseTemplate.EnsureVisibleAtTileOrigin(preservedBaseTileOrigin, node.room);

        ApplyBaseOriginToRoomManager();
        EnsureBaseVisible();
        EnsurePlayerVisible();
        BattleDockHandleVisibilityController.RefreshNow();

        node.room.repositionPlayerOnEnter = false;
        preparedForIncomingNode = false;
        selectionCollapsePrepared = false;
    }

    private void HandleRunEnded(RunEndReason _)
    {
        if (collapseRoutine != null)
        {
            StopCoroutine(collapseRoutine);
            collapseRoutine = null;
        }
        if (queuedNodeEntryRoutine != null)
        {
            StopCoroutine(queuedNodeEntryRoutine);
            queuedNodeEntryRoutine = null;
        }

        queuedNode = null;
        ResolveSystems();
        EnsureBaseVisible();
        EnsurePlayerVisible();
        preparedForIncomingNode = false;
        selectionCollapsePrepared = false;
        ReleaseShowStageGate();
        SetFlowState(BattleStageFlowState.Ended);
    }

    private void SetFlowState(BattleStageFlowState next)
    {
        if (flowState == next)
            return;

        flowState = next;
        FlowStateChanged?.Invoke(next);
    }

    private void PromoteNextBaseAroundPlayer()
    {
        if (baseTemplate == null || player == null)
            return;

        baseTemplate.EnsurePersistentBase();

        Vector3 nextOrigin = FindBestFourByFourOrigin(player.transform.position);
        nextOrigin.z = baseTemplate.FixedTileOriginWorld.z;
        List<MapBlock> roomBlocks = CollectCurrentRoomExitBlocks();
        FloorVisualSnapshot[] preservedFloorVisuals = CaptureFourByFourFloorVisuals(nextOrigin, roomBlocks);

        preservedBaseTileOrigin = baseTemplate.PromoteToNewBaseAtTileOrigin(nextOrigin);
        hasPreservedBaseOrigin = true;
        preparedForIncomingNode = true;

        EnsureBaseVisible();
        BattleShowPresentationManager.Instance?.RefreshPersistentBaseArt();
        RestoreFourByFourFloorVisuals(preservedFloorVisuals);
        ApplyBaseOriginToRoomManager();
        CaptureShowAnchorFromBase();
        BattleDockHandleVisibilityController.RefreshNow();
    }

    private void CaptureCurrentBase()
    {
        if (baseTemplate == null)
            return;

        baseTemplate.EnsurePersistentBase();
        if (!baseTemplate.HasPersistentBase)
            return;

        preservedBaseTileOrigin = baseTemplate.FixedTileOriginWorld;
        hasPreservedBaseOrigin = true;
    }

    private void CaptureShowAnchorFromBase()
    {
        if (baseTemplate == null || !baseTemplate.HasPersistentBase)
            return;

        showAnchorCenter = baseTemplate.FixedCenterWorld;
        hasShowAnchor = true;
    }

    private void ApplyBaseOriginToRoomManager()
    {
        if (!hasPreservedBaseOrigin || roomManager == null || roomManager.RoomOrigin == null)
            return;

        Vector3 position = preservedBaseTileOrigin;
        position.z = roomManager.RoomOrigin.position.z;
        roomManager.RoomOrigin.position = position;
    }

    private void EnsureBaseVisible()
    {
        if (baseTemplate == null)
            return;

        baseTemplate.EnsurePersistentBase();
        GameObject baseObject = baseTemplate.ActiveBase;
        if (baseObject == null)
            return;

        if (!baseObject.activeSelf)
            baseObject.SetActive(true);

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
            if (colliders[i] != null && colliders[i].isTrigger)
                colliders[i].enabled = true;

        BattleWalkableField[] fields = baseObject.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < fields.Length; i++)
            if (fields[i] != null)
                fields[i].enabled = true;
    }

    private void EnsurePlayerVisible()
    {
        if (player == null)
            return;

        if (!player.gameObject.activeSelf)
            player.gameObject.SetActive(true);

        SpriteRenderer[] renderers = player.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                renderers[i].enabled = true;
    }

    private static Vector3 FindBestFourByFourOrigin(Vector3 playerWorld)
    {
        float tileSize = RoomBaseTemplate.TileWorldSize;
        Vector2Int playerTile = WorldToTile(playerWorld);

        Vector2Int bestOrigin = new(playerTile.x - 1, playerTile.y - 1);
        int bestCoverage = -1;
        float bestDistance = float.MaxValue;

        for (int oy = playerTile.y - (RoomBaseTemplate.FixedBaseTiles - 1); oy <= playerTile.y; oy++)
        {
            for (int ox = playerTile.x - (RoomBaseTemplate.FixedBaseTiles - 1); ox <= playerTile.x; ox++)
            {
                int coverage = 0;
                for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
                {
                    for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                    {
                        Vector2 probe = new((ox + x) * tileSize, (oy + y) * tileSize);
                        if (BattleWalkableField.HasSupport(probe))
                            coverage++;
                    }
                }

                Vector2 candidateCenter = new(
                    (ox + (RoomBaseTemplate.FixedBaseTiles - 1) * 0.5f) * tileSize,
                    (oy + (RoomBaseTemplate.FixedBaseTiles - 1) * 0.5f) * tileSize);
                float distance = ((Vector2)playerWorld - candidateCenter).sqrMagnitude;

                if (coverage > bestCoverage ||
                    coverage == bestCoverage && distance < bestDistance)
                {
                    bestCoverage = coverage;
                    bestDistance = distance;
                    bestOrigin = new Vector2Int(ox, oy);
                }
            }
        }

        return new Vector3(bestOrigin.x * tileSize, bestOrigin.y * tileSize, playerWorld.z);
    }

    private static Vector2Int WorldToTile(Vector3 world)
    {
        float tileSize = Mathf.Max(0.0001f, RoomBaseTemplate.TileWorldSize);
        return new Vector2Int(
            Mathf.RoundToInt(world.x / tileSize),
            Mathf.RoundToInt(world.y / tileSize));
    }
}

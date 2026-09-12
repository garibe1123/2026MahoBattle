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
        public CollapseExitSide side;
        public Vector2 direction;
        public float outwardDistance;
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

        // Stage flow is committed only after WorldSet reports the matching physical mode as settled.
        // This keeps Reward -> Map in ShowEntering while the Presenter carrier is still exiting.
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

        // StateChanged is the single logical trigger for stage transitions.
        // RewardSelectionRequested used to call collapse a second time and is intentionally not subscribed here.
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
                // Normally SelectNextNode queues entry through TryQueueNodeEntry(), so the Show is already gone.
                // This also covers legacy/direct EnterNode callers safely.
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

    /// <summary>
    /// Called by BattleRunManager after a map node is chosen.
    /// Returns true when this state machine takes ownership of the physical hand-off.
    /// The logical node is entered only after the current Show has completely left the stage.
    /// </summary>
    public bool TryQueueNodeEntry(BattleNodeData node)
    {
        ResolveSystems();

        if (node == null || runManager == null || !runManager.RunActive)
            return false;
        if (queuedNodeEntryRoutine != null)
            return true;

        // If no Show exists there is nothing physical to wait for; let RunManager enter immediately.
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

        // WorldSet resolves externalGate as ShowMode.None and owns its actual exit animation.
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

        // currentRoom 플래그가 아니라 실제 월드에 남아 있는 Room 이동 Root를 기준으로 판단합니다.
        // 전투 종료 직후 논리 상태가 먼저 바뀌더라도 타일이 남아 있으면 반드시 RoomExiting을 거칩니다.
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
        // 실제 RoomManager가 진입시킨 Assembly/MapBlock Root를 먼저 고정합니다.
        // Base 재구축이나 Presentation refresh 뒤에 씬을 재검색하지 않습니다.
        List<MapBlock> outgoingBlocks = CollectCurrentRoomExitBlocks();
        if (outgoingBlocks.Count == 0)
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

        // 1) Player가 실제로 설 수 있는 현재 Field 안에서 4x4를 확정합니다.
        baseTemplate.EnsurePersistentBase();
        Vector3 nextOrigin = FindBestFourByFourOrigin(player.transform.position);
        nextOrigin.z = baseTemplate.FixedTileOriginWorld.z;

        // 새 Persistent Base는 별도 GameObject를 다시 만들기 때문에,
        // 재생성 전에 지금 플레이어가 실제로 보고 있던 4x4 바닥 Sprite를 먼저 저장합니다.
        FloorVisualSnapshot[] preservedFloorVisuals = CaptureFourByFourFloorVisuals(nextOrigin, outgoingBlocks);

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

        // 2) 기존 Room Block을 4x4의 좌/우/아래/위 네 방향으로 분류합니다.
        // Procedural Room은 진입 때 사용했던 Assembly Group 자체를 퇴장시켜 조립 단위가 그대로 빠지게 합니다.
        // Legacy Room은 기존 walkable MapBlock을 그대로 사용합니다.
        Vector2 baseCenter = showAnchorCenter;
        Bounds baseBounds = CreatePersistentBaseBounds(baseCenter);
        List<CollapseExitPlan>[] groups = BuildExitGroups(outgoingBlocks, baseBounds);

        int waveCount = 0;
        for (int side = 0; side < groups.Length; side++)
            waveCount = Mathf.Max(waveCount, groups[side].Count);

        float stagger = Mathf.Max(0f, collapseExitStagger);
        float lastExitDuration = 0f;

        Debug.Log(
            $"[BattleStageFlow] Retiring {outgoingBlocks.Count} owned combat room movement root(s) before {pendingShowState}.",
            this);

        for (int wave = 0; wave < waveCount; wave++)
        {
            bool startedAny = false;

            for (int side = 0; side < groups.Length; side++)
            {
                List<CollapseExitPlan> group = groups[side];
                if (wave >= group.Count)
                    continue;

                CollapseExitPlan plan = group[wave];
                if (plan == null || plan.block == null)
                    continue;

                PushOutgoingRenderersBehindBase(plan.block);
                DisableOutgoingWalkable(plan.block);

                // MapBlock이 진입 때 사용한 entryOffset(Procedural Assembly는 기본 36u Rail)을
                // 그대로 Exit 거리로 사용합니다. 순간 삭제/Snap 대신 반드시 화면 밖으로 이동합니다.
                Tween exitTween = plan.block.PlayExit(plan.direction);
                exitTween?.SetUpdate(true);
                lastExitDuration = Mathf.Max(lastExitDuration, plan.block.ExitDuration);
                startedAny = true;
            }

            if (startedAny && stagger > 0f && wave < waveCount - 1)
                yield return new WaitForSecondsRealtime(stagger);
        }

        // 마지막 Wave가 실제 ExitDuration을 전부 소비할 때까지 Show Gate를 유지합니다.
        if (lastExitDuration > 0f)
            yield return new WaitForSecondsRealtime(lastExitDuration);

        // 화면 밖까지 이동한 뒤에만 GameObject를 제거합니다.
        for (int i = 0; i < outgoingBlocks.Count; i++)
        {
            MapBlock block = outgoingBlocks[i];
            if (block == null)
                continue;

            block.gameObject.SetActive(false);
            Destroy(block.gameObject);
        }

        // Destroy 예약을 실제 Frame에 반영한 다음 RoomManager의 ownership만 정식 retire합니다.
        // 이 API는 타일을 다시 Destroy하지 않으므로 다음 EnterRoomRoutine의 ClearImmediate jump-cut도 막습니다.
        yield return null;
        roomManager?.CompleteAnimatedStageRetirement();

        BattleDockHandleVisibilityController.RefreshNow();

        if (showOpenDelayAfterCollapse > 0f)
            yield return new WaitForSecondsRealtime(showOpenDelayAfterCollapse);

        collapseRoutine = null;

        // 3) 전투 Field의 실제 Exit와 Room ownership 정리가 모두 끝난 뒤에만 Show를 엽니다.
        OpenShowStage();
    }

    private bool HasCurrentRoomFieldToRetire()
    {
        if (roomManager == null)
            return false;

        List<MapBlock> ownedBlocks = new();
        if (roomManager.CopyActiveRoomBlocks(ownedBlocks) > 0)
            return true;

        // currentRoom만 남고 이동 Root가 없는 특수/빈 Room도 lifecycle retirement는 필요합니다.
        if (roomManager.IsRoomActive)
            return true;

        return CollectCurrentRoomExitBlocksFallback().Count > 0;
    }

    private List<MapBlock> CollectCurrentRoomExitBlocks()
    {
        List<MapBlock> ownedBlocks = new();
        if (roomManager != null && roomManager.CopyActiveRoomBlocks(ownedBlocks) > 0)
            return ownedBlocks;

        // Legacy/migration scene에서만 hierarchy scan을 fallback으로 사용합니다.
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

            // Assembly child를 발견해도 실제 진입/퇴장 이동 단위인 최상위 MapBlock 부모로 승격합니다.
            // 이름이 바뀌어도 parent MapBlock 구조만 유지되면 같은 Root를 찾을 수 있습니다.
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

            // Procedural Assembly parent는 walkable=false지만 실제 이동 Root입니다.
            // 이름 의존만 하지 않고 nested MapBlock을 가진 부모도 Assembly Root로 인정합니다.
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

        // 기존 Persistent Base가 선택 영역에 포함된 경우도 현재 보이는 Sprite를 후보로 넣습니다.
        if (baseTemplate != null && baseTemplate.ActiveBase != null)
            CaptureFloorRenderers(baseTemplate.ActiveBase, originCell, snapshots, ranks);

        // 실제 전투 Room Piece의 Tile_* Sprite는 PresentationManager가 랜덤 Floor Variant를
        // 직접 적용한 Renderer이므로, 여기서 저장하면 화면에서 보던 모양을 그대로 보존할 수 있습니다.
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

    private List<CollapseExitPlan>[] BuildExitGroups(List<MapBlock> blocks, Bounds baseBounds)
    {
        List<CollapseExitPlan>[] groups =
        {
            new List<CollapseExitPlan>(),
            new List<CollapseExitPlan>(),
            new List<CollapseExitPlan>(),
            new List<CollapseExitPlan>()
        };

        List<CollapseExitPlan> pending = new();
        for (int i = 0; i < blocks.Count; i++)
        {
            MapBlock block = blocks[i];
            if (block == null)
                continue;

            Bounds blockBounds = ResolveBlockBounds(block);
            pending.Add(new CollapseExitPlan
            {
                block = block,
                bounds = blockBounds,
                outwardDistance = ((Vector2)blockBounds.center - (Vector2)baseBounds.center).sqrMagnitude
            });
        }

        // 바깥쪽 조립 단위부터 Rail을 예약합니다. 중앙 Piece가 먼저 한 방향을 독점해서
        // 외곽 Piece를 반대편으로 가로질러 보내는 상황을 막습니다.
        pending.Sort((a, b) =>
        {
            int radial = b.outwardDistance.CompareTo(a.outwardDistance);
            return radial != 0
                ? radial
                : a.block.GetInstanceID().CompareTo(b.block.GetInstanceID());
        });

        float baseSpan = Mathf.Max(baseBounds.size.x, baseBounds.size.y);
        for (int i = 0; i < pending.Count; i++)
        {
            CollapseExitPlan plan = pending[i];
            float bestScore = float.PositiveInfinity;
            CollapseExitSide bestSide = CollapseExitSide.Left;

            for (int sideIndex = 0; sideIndex < groups.Length; sideIndex++)
            {
                CollapseExitSide side = (CollapseExitSide)sideIndex;
                float score = ResolveExitAssignmentScore(
                    plan.bounds,
                    baseBounds,
                    side,
                    groups,
                    baseSpan);

                // 기존 구현은 동률이면 enum 첫 값(Left)에 고정됐습니다.
                // 동률에서는 이미 덜 쓰인 Rail을 우선해 자연스럽게 방향을 분산합니다.
                if (score < bestScore - 0.001f ||
                    Mathf.Abs(score - bestScore) <= 0.001f &&
                    groups[sideIndex].Count < groups[(int)bestSide].Count)
                {
                    bestScore = score;
                    bestSide = side;
                }
            }

            plan.side = bestSide;
            plan.direction = DirectionFor(bestSide);
            plan.outwardDistance = ResolveOutwardDistance(plan.bounds, baseBounds, bestSide);
            groups[(int)bestSide].Add(plan);

            Debug.Log(
                $"[BattleStageFlow] Exit rail '{plan.block.name}' -> {bestSide} " +
                $"(score {bestScore:0.00}, lane {groups[(int)bestSide].Count}).",
                plan.block);
        }

        for (int i = 0; i < groups.Length; i++)
            groups[i].Sort((a, b) => b.outwardDistance.CompareTo(a.outwardDistance));

        return groups;
    }

    private static float ResolveExitAssignmentScore(
        Bounds blockBounds,
        Bounds baseBounds,
        CollapseExitSide side,
        List<CollapseExitPlan>[] groups,
        float baseSpan)
    {
        Vector2 direction = DirectionFor(side);
        Vector2 fromBase = (Vector2)blockBounds.center - (Vector2)baseBounds.center;
        float distanceFromBase = fromBase.magnitude;
        Vector2 radial = distanceFromBase > 0.001f
            ? fromBase / distanceFromBase
            : Vector2.zero;
        float alignment = Vector2.Dot(radial, direction);

        // 4x4를 해당 방향으로 완전히 벗어나기 위한 실제 이동량을 기본 비용으로 사용합니다.
        float score = ResolveClearanceTravel(blockBounds, baseBounds, side);

        // 자기 중심에서 바깥쪽으로 빠지는 Rail을 우선하고, 반대편을 가로질러 나가는 Rail은
        // 강하게 억제합니다. 따라서 분산 때문에 기존 타일/4x4를 관통하는 선택은 하지 않습니다.
        if (distanceFromBase > 0.05f)
        {
            score += (1f - Mathf.Max(0f, alignment)) * baseSpan * 0.30f;
            if (alignment < -0.15f)
                score += -alignment * baseSpan * 3.25f;
        }

        List<CollapseExitPlan> sameSide = groups[(int)side];

        // 기하 비용이 비슷한 조립 단위가 전부 한쪽으로 몰리지 않도록 사용량 패널티를 줍니다.
        score += sameSide.Count * baseSpan * 1.15f;

        // 같은 방향에서 이동축에 수직인 폭까지 겹치면 사실상 같은 Rail입니다.
        // 가능한 경우 다른 방향으로 보내고, 불가피한 경우에만 기존 Wave/Stagger가 처리합니다.
        for (int i = 0; i < sameSide.Count; i++)
        {
            CollapseExitPlan existing = sameSide[i];
            if (existing != null && ExitLanesOverlap(blockBounds, existing.bounds, side))
                score += baseSpan * 1.85f;
        }

        return score;
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

    private static bool ExitLanesOverlap(
        Bounds a,
        Bounds b,
        CollapseExitSide side)
    {
        const float clearance = 0.08f;

        if (side == CollapseExitSide.Left || side == CollapseExitSide.Right)
            return a.min.y < b.max.y - clearance &&
                   a.max.y > b.min.y + clearance;

        return a.min.x < b.max.x - clearance &&
               a.max.x > b.min.x + clearance;
    }

    private static CollapseExitSide ResolveExitSide(Bounds blockBounds, Bounds baseBounds)
    {
        const float epsilon = 0.02f;

        bool fullyLeft = blockBounds.max.x <= baseBounds.min.x + epsilon;
        bool fullyRight = blockBounds.min.x >= baseBounds.max.x - epsilon;
        bool fullyDown = blockBounds.max.y <= baseBounds.min.y + epsilon;
        bool fullyUp = blockBounds.min.y >= baseBounds.max.y - epsilon;

        if (fullyLeft || fullyRight || fullyDown || fullyUp)
        {
            float bestGap = float.NegativeInfinity;
            CollapseExitSide bestSide = CollapseExitSide.Down;

            if (fullyLeft)
                SelectIfGreater(baseBounds.min.x - blockBounds.max.x, CollapseExitSide.Left, ref bestGap, ref bestSide);
            if (fullyRight)
                SelectIfGreater(blockBounds.min.x - baseBounds.max.x, CollapseExitSide.Right, ref bestGap, ref bestSide);
            if (fullyDown)
                SelectIfGreater(baseBounds.min.y - blockBounds.max.y, CollapseExitSide.Down, ref bestGap, ref bestSide);
            if (fullyUp)
                SelectIfGreater(blockBounds.min.y - baseBounds.max.y, CollapseExitSide.Up, ref bestGap, ref bestSide);

            return bestSide;
        }

        float moveLeft = Mathf.Max(0f, blockBounds.max.x - baseBounds.min.x);
        float moveRight = Mathf.Max(0f, baseBounds.max.x - blockBounds.min.x);
        float moveDown = Mathf.Max(0f, blockBounds.max.y - baseBounds.min.y);
        float moveUp = Mathf.Max(0f, baseBounds.max.y - blockBounds.min.y);

        float bestMove = moveLeft;
        CollapseExitSide result = CollapseExitSide.Left;

        SelectIfSmaller(moveRight, CollapseExitSide.Right, ref bestMove, ref result);
        SelectIfSmaller(moveDown, CollapseExitSide.Down, ref bestMove, ref result);
        SelectIfSmaller(moveUp, CollapseExitSide.Up, ref bestMove, ref result);
        return result;
    }

    private static void SelectIfGreater(
        float value,
        CollapseExitSide side,
        ref float bestValue,
        ref CollapseExitSide bestSide)
    {
        if (value <= bestValue)
            return;
        bestValue = value;
        bestSide = side;
    }

    private static void SelectIfSmaller(
        float value,
        CollapseExitSide side,
        ref float bestValue,
        ref CollapseExitSide bestSide)
    {
        if (value >= bestValue)
            return;
        bestValue = value;
        bestSide = side;
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

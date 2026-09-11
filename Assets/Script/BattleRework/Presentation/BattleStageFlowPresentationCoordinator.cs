using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// BattleStageTransitionController의 물리 Stage FSM을 Presentation/Input 계층의 단일 gate로 사용합니다.
///
/// 기존 Presentation 컴포넌트의 Inspector 튜닝과 내부 구현은 그대로 유지하되,
/// BattleRunState가 먼저 바뀌었다는 이유만으로 Room Exit 이전에 Show/Reward 입력이 선행 실행되지 않게 합니다.
///
/// 핵심 규칙:
/// - RoomExiting 동안 Reward 카드 입력 / Show Focus / TV Content / Broadcast 처리를 막습니다.
/// - ShowEntering부터 Show 관련 시각 처리를 허용합니다.
/// - Reward 카드 입력은 실제 WorldSet이 RewardShow로 settle된 뒤에만 허용합니다.
/// - Map 버튼 입력은 실제 WorldSet이 MapShow로 settle된 뒤에만 허용합니다.
/// - ShowExiting 동안에는 Focus/TV/Broadcast를 유지해 Carrier 퇴장 중 화면이 갑자기 꺼지지 않게 합니다.
/// - Combat field cinematic은 RoomExiting 동안 중지했다가 ShowEntering에서 다시 Show lighting owner로 복귀합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(65000)]
public sealed class BattleStageFlowPresentationCoordinator : MonoBehaviour
{
    private const string RewardCardRootName = "PrizeChoices";
    private const string RewardNoticeRootName = "PlacementNotice";
    private const string MapContentName = "MapSelectionContent";

    private static BattleStageFlowPresentationCoordinator instance;

    private BattleStageTransitionController stageFlow;
    private BattleRewardCardActionController rewardCards;
    private BattleShowFocusController showFocus;
    private BattleShowSharedTvContentController sharedTvContent;
    private BattleShowBroadcastNoiseController broadcastNoise;
    private BattleFieldCinematicDirector fieldCinematic;

    private RectTransform rewardCardRoot;
    private RectTransform rewardNoticeRoot;
    private RectTransform mapContentRoot;
    private CanvasGroup rewardCardGroup;
    private CanvasGroup rewardNoticeGroup;
    private CanvasGroup mapContentGroup;

    private BattleStageTransitionController subscribedFlow;
    private float nextResolveTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForCurrentScene()
    {
        EnsureInstalled();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.IsValid() && scene.isLoaded)
            EnsureInstalled();
    }

    private static void EnsureInstalled()
    {
        if (instance != null)
            return;

        BattleStageTransitionController flow = BattleStageTransitionController.Instance;
        if (flow == null)
            flow = FindFirstObjectByType<BattleStageTransitionController>();
        if (flow == null)
            return;

        BattleStageFlowPresentationCoordinator existing =
            flow.GetComponent<BattleStageFlowPresentationCoordinator>();
        if (existing == null)
            existing = flow.gameObject.AddComponent<BattleStageFlowPresentationCoordinator>();

        instance = existing;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ResolveReferences(true);
        SubscribeFlow();
        ApplyFlowPolicy();
    }

    private void OnEnable()
    {
        ResolveReferences(true);
        SubscribeFlow();
        ApplyFlowPolicy();
    }

    private void OnDisable()
    {
        UnsubscribeFlow();
        RestoreInputGroups();
    }

    private void OnDestroy()
    {
        UnsubscribeFlow();
        RestoreInputGroups();

        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextResolveTime)
            return;

        nextResolveTime = Time.unscaledTime + 0.15f;
        ResolveReferences(false);
        SubscribeFlow();
        ApplyFlowPolicy();
    }

    private void LateUpdate()
    {
        // Map/Reward UI 보정 컨트롤러들이 LateUpdate에서 CanvasGroup을 다시 켜더라도
        // 렌더 직전에 Stage FSM 정책이 최종 권한을 갖습니다.
        ApplyInputGate();
    }

    private void ResolveReferences(bool forceUiResolve)
    {
        if (stageFlow == null)
        {
            stageFlow = BattleStageTransitionController.Instance;
            if (stageFlow == null)
                stageFlow = FindFirstObjectByType<BattleStageTransitionController>();
        }

        if (rewardCards == null)
            rewardCards = FindFirstObjectByType<BattleRewardCardActionController>(FindObjectsInactive.Include);
        if (showFocus == null)
            showFocus = FindFirstObjectByType<BattleShowFocusController>(FindObjectsInactive.Include);
        if (sharedTvContent == null)
            sharedTvContent = FindFirstObjectByType<BattleShowSharedTvContentController>(FindObjectsInactive.Include);
        if (broadcastNoise == null)
            broadcastNoise = FindFirstObjectByType<BattleShowBroadcastNoiseController>(FindObjectsInactive.Include);
        if (fieldCinematic == null)
            fieldCinematic = FindFirstObjectByType<BattleFieldCinematicDirector>(FindObjectsInactive.Include);

        if (forceUiResolve || rewardCardRoot == null || rewardNoticeRoot == null || mapContentRoot == null)
            ResolveInputRoots();
    }

    private void ResolveInputRoots()
    {
        if (rewardCardRoot == null)
            rewardCardRoot = FindRect(RewardCardRootName);
        if (rewardNoticeRoot == null)
            rewardNoticeRoot = FindRect(RewardNoticeRootName);
        if (mapContentRoot == null)
            mapContentRoot = FindRect(MapContentName);

        rewardCardGroup = ResolveCanvasGroup(rewardCardRoot, rewardCardGroup);
        rewardNoticeGroup = ResolveCanvasGroup(rewardNoticeRoot, rewardNoticeGroup);
        mapContentGroup = ResolveCanvasGroup(mapContentRoot, mapContentGroup);
    }

    private void SubscribeFlow()
    {
        if (stageFlow == subscribedFlow)
            return;

        UnsubscribeFlow();
        subscribedFlow = stageFlow;
        if (subscribedFlow != null)
            subscribedFlow.FlowStateChanged += HandleFlowStateChanged;
    }

    private void UnsubscribeFlow()
    {
        if (subscribedFlow != null)
            subscribedFlow.FlowStateChanged -= HandleFlowStateChanged;
        subscribedFlow = null;
    }

    private void HandleFlowStateChanged(BattleStageFlowState _)
    {
        ResolveReferences(false);
        ApplyFlowPolicy();
    }

    private void ApplyFlowPolicy()
    {
        if (stageFlow == null)
            return;

        BattleStageFlowState state = stageFlow.FlowState;
        bool showVisuals = stageFlow.IsShowPhase;
        bool rewardInteractive = state == BattleStageFlowState.RewardShow;

        SetEnabled(rewardCards, rewardInteractive);
        SetEnabled(showFocus, showVisuals);
        SetEnabled(sharedTvContent, showVisuals);
        SetEnabled(broadcastNoise, showVisuals);

        // Field director는 Combat과 Show의 실제 Lighting executor입니다.
        // 다만 Combat clear 직후 Room 타일이 빠지는 동안 RunState=Reward를 먼저 읽어
        // Show lighting을 조기 적용하지 못하도록 RoomExiting 구간만 정지합니다.
        bool fieldDirectorAllowed =
            state != BattleStageFlowState.RoomExiting &&
            state != BattleStageFlowState.Ended;
        SetEnabled(fieldCinematic, fieldDirectorAllowed);

        ApplyInputGate();
    }

    private void ApplyInputGate()
    {
        if (stageFlow == null)
            return;

        if (rewardCardRoot == null || rewardNoticeRoot == null || mapContentRoot == null)
            ResolveInputRoots();

        BattleStageFlowState state = stageFlow.FlowState;
        bool rewardInteractive = state == BattleStageFlowState.RewardShow;
        bool mapInteractive = state == BattleStageFlowState.MapShow;

        SetCanvasInput(rewardCardGroup, rewardInteractive);
        SetCanvasInput(rewardNoticeGroup, rewardInteractive);
        SetCanvasInput(mapContentGroup, mapInteractive);
    }

    private void RestoreInputGroups()
    {
        SetCanvasInput(rewardCardGroup, true);
        SetCanvasInput(rewardNoticeGroup, true);
        SetCanvasInput(mapContentGroup, true);
    }

    private static void SetEnabled(Behaviour behaviour, bool enabled)
    {
        if (behaviour != null && behaviour.enabled != enabled)
            behaviour.enabled = enabled;
    }

    private static CanvasGroup ResolveCanvasGroup(RectTransform root, CanvasGroup cached)
    {
        if (root == null)
            return null;
        if (cached != null && cached.transform == root)
            return cached;

        CanvasGroup group = root.GetComponent<CanvasGroup>();
        if (group == null)
            group = root.gameObject.AddComponent<CanvasGroup>();
        return group;
    }

    private static void SetCanvasInput(CanvasGroup group, bool enabled)
    {
        if (group == null)
            return;

        group.blocksRaycasts = enabled;
        group.interactable = enabled;
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }

        return null;
    }
}

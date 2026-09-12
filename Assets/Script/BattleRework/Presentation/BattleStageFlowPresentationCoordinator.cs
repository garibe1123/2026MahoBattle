using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// BattleStageTransitionController의 물리 Stage FSM을 Presentation/Input 계층의 단일 gate로 사용합니다.
///
/// 기존 Presentation 컴포넌트의 Inspector 튜닝과 내부 구현은 그대로 유지하되,
/// BattleRunState가 먼저 바뀌었다는 이유만으로 Room Exit 이전에 Show/Reward 입력이 선행 실행되지 않게 합니다.
///
/// 핵심 규칙:
/// - RoomExiting부터 Show Focus의 배경 암전만 먼저 허용해 다음 Show가 들어오기 전에 무대를 어둡게 만듭니다.
/// - TV Content / Broadcast / Reward 입력은 기존처럼 실제 Show 단계에서만 허용합니다.
/// - Character Spotlight를 소유한 Field Cinematic은 ShowEntering 동안 정지하고 RewardShow / MapShow settle 뒤에 복귀합니다.
/// - Reward 카드 입력은 실제 WorldSet이 RewardShow로 settle된 뒤에만 허용합니다.
/// - Map 버튼 입력은 실제 WorldSet이 MapShow로 settle된 뒤에만 허용합니다.
/// - Reward PACK 편집 중에는 뒤쪽 Show 카메라의 커서 추적을 중지합니다.
/// - ShowExiting 동안에는 Focus/TV/Broadcast를 유지해 Carrier 퇴장 중 화면이 갑자기 꺼지지 않게 합니다.
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
    private BattleRewardFlow rewardFlow;
    private BattleCameraController battleCamera;

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
        // WorldSet(20000)이 먼저 TV 커서 추적을 갱신한 뒤 이 Coordinator(65000)가
        // PACK 편집 중에는 같은 프레임 안에서 다시 끕니다. Camera LateUpdate 전에 적용되므로
        // PACK 뒤쪽 카메라가 화면 밖 마우스 좌표를 따라 버벅이지 않습니다.
        ApplyRewardPackCameraGate();

        if (Time.unscaledTime < nextResolveTime)
            return;

        nextResolveTime = Time.unscaledTime + 0.15f;
        ResolveReferences(false);
        SubscribeFlow();
        ApplyFlowPolicy();
        ApplyRewardPackCameraGate();
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
        if (rewardFlow == null)
            rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        if (battleCamera == null)
            battleCamera = FindFirstObjectByType<BattleCameraController>(FindObjectsInactive.Include);

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
        bool preShowDim = state == BattleStageFlowState.RoomExiting;
        bool rewardInteractive = state == BattleStageFlowState.RewardShow;

        SetEnabled(rewardCards, rewardInteractive);

        // 배경 암전은 Room이 빠지기 시작할 때부터 선행합니다.
        // BattleShowFocusController 내부에서 실제 Spotlight는 WorldSet이 settle되기 전까지 0으로 유지합니다.
        SetEnabled(showFocus, preShowDim || showVisuals);
        SetEnabled(sharedTvContent, showVisuals);
        SetEnabled(broadcastNoise, showVisuals);

        // Player / Presenter의 실제 Character Spotlight는 Field Cinematic이 소유합니다.
        // RoomExiting / ShowEntering에서는 꺼 둔 채 먼저 암전하고,
        // TV/Carrier가 settle된 RewardShow / MapShow부터 다시 켭니다.
        bool fieldDirectorAllowed =
            state != BattleStageFlowState.RoomExiting &&
            state != BattleStageFlowState.ShowEntering &&
            state != BattleStageFlowState.Ended;
        SetEnabled(fieldCinematic, fieldDirectorAllowed);

        ApplyInputGate();
    }

    private void ApplyRewardPackCameraGate()
    {
        if (rewardFlow == null || battleCamera == null)
            return;

        if (rewardFlow.Phase != BattleRewardPhase.PackEditing)
            return;

        battleCamera.SetShowCursorTracking(false, Vector2.zero);
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

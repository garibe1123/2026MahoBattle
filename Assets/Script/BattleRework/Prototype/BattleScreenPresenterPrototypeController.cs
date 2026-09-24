using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Disposable screen presenter prototype.
///
/// - Field presenter / World-Space TV stay untouched.
/// - Screen presenter enters once from the right, black -> white, then remains on screen.
/// - Reward -> Map -> stage preparation does not replay the entrance.
/// - Entering Combat turns the presenter white -> black, then hides it.
/// - Dialogue independently opens, types, idles, closes, and can be replaced by new commentary.
/// - Screen presenter static sprite/material come from BattleShowPresentationManager.
/// </summary>
[DefaultExecutionOrder(70000)]
[DisallowMultipleComponent]
public sealed class BattleScreenPresenterPrototypeController : MonoBehaviour
{
    private enum Mode
    {
        None,
        Reward,
        Map
    }

    private enum Mood
    {
        Neutral,
        Curious,
        Excited,
        Concerned
    }

    private enum PresenterPhase
    {
        Hidden,
        Entering,
        Active,
        BlackingOut
    }

    private enum DialoguePhase
    {
        Hidden,
        Opening,
        Typing,
        Idle,
        Closing
    }

    private enum PresenterLineKey
    {
        RewardIntro,
        MapIntro,
        RewardConfirm,
        MapConfirm,
        Saw,
        OddWeapon,
        RareItem,
        HotItem,
        SafeItem,
        GenericHover,
        GenericSelected,
        MapElite,
        MapShop,
        MapEvent,
        MapCombatHigh,
        MapCombatMid,
        MapCombatLow,
        BoredReward,
        BoredMap
    }

    private const int PresenterFramePixels = 128;

    private static BattleScreenPresenterPrototypeController instance;

    [Header("Screen Overlay")]
    [SerializeField] private int overlaySortingOrder = 470;
    [Tooltip("Reward/Map 선택 중 작은 Mini PACK(780)보다 위에 말풍선을 표시합니다.")]
    [SerializeField] private int dialogueMiniPackSortingOrder = 790;
    [SerializeField] private Vector2 referenceResolution = new(1920f, 1080f);

    [Header("Presenter Square")]
    [Tooltip("정사각 Placeholder/사회자 이미지를 화면보다 여유롭게 크게 잡아 우측과 하단이 자연스럽게 잘리도록 합니다.")]
    [SerializeField] private Vector2 presenterSize = new(1180f, 1180f);
    [Tooltip("우하단 기준 최종 위치입니다. +X는 오른쪽 화면 밖, -Y는 아래 화면 밖으로 밀려 자연스럽게 크롭됩니다.")]
    [SerializeField] private Vector2 presenterVisibleOffset = new(140f, -110f);
    [SerializeField, Min(0f)] private float presenterHiddenOffsetX = 500f;
    [SerializeField, Min(50f)] private float presenterSlideSpeedPixels = 1250f;
    [SerializeField, Min(0.1f)] private float presenterRevealPerSecond = 2.8f;
    [SerializeField, Min(0.1f)] private float presenterBlackoutPerSecond = 3.6f;
    [SerializeField, Min(0f)] private float presenterEntryDelay = 0.08f;

    [Header("Presenter Motion")]
    [Tooltip("Idle은 아주 작게 떠 있는 정도만 사용합니다. PingPong 기반이라 이동 속도가 일정합니다.")]
    [SerializeField, Range(0f, 8f)] private float idleMovePixels = 3.5f;
    [SerializeField, Range(0f, 8f)] private float idleSidePixels = 1.8f;
    [SerializeField, Range(0f, 0.02f)] private float idleScaleAmount = 0.004f;
    [SerializeField, Min(0.1f)] private float idleCyclesPerSecond = 0.28f;

    [Tooltip("대사 타이핑 중 좌우로 작게 떨리는 폭입니다.")]
    [SerializeField, Range(0f, 12f)] private float talkShakePixels = 2.8f;
    [SerializeField, Min(0.1f)] private float talkShakeCyclesPerSecond = 5.5f;

    [Tooltip("새 Hover/선택 반응 때 위로 한 번 튀는 높이입니다.")]
    [SerializeField, Range(0f, 60f)] private float reactionHopPixels = 24f;
    [SerializeField, Min(0.05f)] private float reactionDuration = 0.24f;

    [Tooltip("Excited/확정 반응은 기본 반응보다 크게 튑니다.")]
    [SerializeField, Range(1f, 2f)] private float excitedReactionMultiplier = 1.35f;

    [Tooltip("아무 Hover/대사 갱신이 없을 때 Bored 모션으로 넘어가는 시간입니다.")]
    [SerializeField, Min(1f)] private float boredAfterSeconds = 6f;

    [Tooltip("입력이 오래 없을 때 사회자가 먼저 잡담을 시작하는 시간입니다.")]
    [SerializeField, Min(2f)] private float boredLineAfterSeconds = 9f;

    [Tooltip("계속 입력이 없을 때 다음 잡담까지 기다리는 시간입니다.")]
    [SerializeField, Min(5f)] private float boredLineRepeatSeconds = 14f;

    [Header("Dialogue")]
    [Tooltip("우측 사회자 영역 일부만 남기고 화면 하단 대부분을 사용하는 긴 말풍선입니다.")]
    [SerializeField] private Vector2 dialogueSize = new(1500f, 180f);
    [SerializeField] private Vector2 dialogueVisibleOffset = new(-120f, 28f);
    [SerializeField, Min(0f)] private float dialogueHiddenOffsetY = 72f;
    [SerializeField, Min(0.03f)] private float dialogueOpenDuration = 0.14f;
    [SerializeField, Min(0.03f)] private float dialogueCloseDuration = 0.10f;
    [SerializeField, Min(1f)] private float typeCharactersPerSecond = 36f;
    [SerializeField, Min(0.1f)] private float dialogueIdleDuration = 1.65f;
    [Tooltip("Reward/Map 화면 사회자 Rect 안에서 말풍선 꼬리가 향할 기준점입니다.")]
    [SerializeField] private Vector2 dialogueTailPresenterAnchor = new(0.28f, 0.58f);
    [SerializeField] private Vector2 dialogueTailPivotOffset = new(0f, 0f);
    [SerializeField, Min(8f)] private float dialogueTailBaseWidth = 68f;
    [SerializeField, Min(0f)] private float dialogueTailOutlineWidth = 8f;
    [SerializeField, Range(-6f, 6f)] private float dialogueBubbleRotation = -1.5f;
    [SerializeField, Range(0.75f, 1f)] private float dialogueBubbleStartScale = 0.88f;
    [SerializeField, Range(1f, 1.12f)] private float dialogueBubbleOvershootScale = 1.04f;

    [Header("Show Camera")]
    [Tooltip("Reward TV 화면을 얼마나 크게 잡을지 조절합니다. 1보다 작으면 더 줌인합니다.")]
    [SerializeField, Range(0.90f, 1.05f)] private float rewardCameraZoomRatio = 0.98f;
    [Tooltip("카메라를 오른쪽으로 조금 이동시켜 TV/아이템 선택 화면이 화면상 약간 왼쪽에 오도록 합니다.")]
    [SerializeField, Range(0f, 1.5f)] private float rewardCameraRightBiasWorld = 0.34f;
    [SerializeField, Range(0.90f, 1.10f)] private float mapCameraZoomRatio = 1f;
    [SerializeField, Range(-1f, 1f)] private float mapCameraHorizontalBiasWorld = 0f;

    [Header("Minimal Theme")]
    [SerializeField] private Color dialogueBack = new(0.01f, 0.012f, 0.018f, 0.87f);
    [SerializeField] private Color accent = new(1f, 0.82f, 0.10f, 1f);
    [SerializeField] private Color hotAccent = new(1f, 0.18f, 0.36f, 1f);
    [SerializeField] private Color white = new(0.97f, 0.98f, 1f, 1f);
    [SerializeField] private Color muted = new(0.65f, 0.68f, 0.74f, 1f);

    private BattleRunManager runManager;
    private BattleRewardFlow rewardFlow;
    private BattleShowWorldSetController showWorld;
    private BattleShowPresentationManager presentation;
    private BattleRunState lastState = (BattleRunState)(-1);
    private Mode mode;

    private Canvas overlayCanvas;
    private RectTransform overlayRoot;

    private CanvasGroup presenterGroup;
    private RectTransform presenterRect;
    private Image presenterImage;
    private PresenterPhase presenterPhase = PresenterPhase.Hidden;
    private bool presenterSessionActive;
    private bool presenterEntryPending;
    private float presenterEnterAt;
    private float presenterTint;
    private Material appliedPresenterMaterial;

    private ScreenPresenterMotionState presenterMotionState = ScreenPresenterMotionState.Idle;
    private ScreenPresenterMotionState forcedMotionState = ScreenPresenterMotionState.Idle;
    private float forcedMotionUntil = -1f;
    private ScreenPresenterMotionClip activeMotionClip;
    private Sprite activeMotionSource;
    private readonly List<Sprite> runtimeMotionFrames = new();
    private int motionFrameIndex;
    private float motionFrameTimer;
    private float lastPresenterInteractionTime;
    private float nextBoredLineAt;
    private bool warnedNonMultipleSheet;

    private CanvasGroup dialogueGroup;
    private Canvas dialogueRenderCanvas;
    private RectTransform dialogueRect;
    private Canvas dialogueTailCanvas;
    private CanvasGroup dialogueTailGroup;
    private RectTransform dialogueTailPivotRect;
    private BattleSpeechBubbleTailLineRenderer dialogueTailGraphic;
    private Text nameText;
    private Text contextText;
    private Text dialogueText;
    private Text liveText;

    private DialoguePhase dialoguePhase = DialoguePhase.Hidden;
    private float dialoguePhaseTime;
    private float typeProgress;
    private int visibleCharacters;

    private bool hasPendingCopy;
    private string pendingHeader = string.Empty;
    private string pendingKeyword = string.Empty;
    private string pendingComment = string.Empty;
    private Mood pendingMood = Mood.Neutral;

    private string activeHeader = string.Empty;
    private string activeKeyword = string.Empty;
    private string activeComment = string.Empty;
    private Mood activeMood = Mood.Neutral;

    private bool cameraApplied;
    private float reactionStartedAt = -10f;
    private Mood reactionMood = Mood.Neutral;

    private readonly Dictionary<PresenterLineKey, int> lastLineByEvent = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleScreenPresenterPrototypeController>() != null)
            return;

        GameObject host = new("BattleScreenPresenterPrototypeRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleScreenPresenterPrototypeController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        RestoreCamera();
        ReleaseRuntimeMotionFrames();

        if (overlayCanvas != null)
            Destroy(overlayCanvas.gameObject);

        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        ResolveReferences();

        BattleRunState state = runManager != null
            ? runManager.State
            : (BattleRunState)(-1);

        if (state != lastState)
        {
            BattleRunState previous = lastState;
            lastState = state;
            HandleStateChanged(previous, state);
        }

        EnsureOverlay();
        UpdateDialogueSorting();

        UpdatePresenterMotionState();
        UpdatePresenterVisualSource();
        UpdatePresenterSheetAnimation();
        UpdatePresenterMotion();
        UpdateDialogueState();
        UpdateBoredCommentary();

        if (mode == Mode.Reward || mode == Mode.Map)
            ApplyPrototypeCamera();
    }

    // ---------------------------------------------------------------------
    // Disposable hooks
    // ---------------------------------------------------------------------

    public static void NotifyRewardHover(BattleEquipmentSO equipment)
    {
        Resolve()?.ShowReward(equipment, false);
    }

    public static void NotifyRewardSelected(BattleEquipmentSO equipment)
    {
        Resolve()?.ShowReward(equipment, true);
    }

    public static void NotifyRewardConfirm(BattleEquipmentSO equipment)
    {
        BattleScreenPresenterPrototypeController owner = Resolve();
        if (owner == null || equipment == null)
            return;

        owner.QueueCopy(
            "SOLD / INSTALLING",
            $"{equipment.rarity.ToString().ToUpperInvariant()} / {equipment.GetDisplayName()}",
            owner.PickLine(
                PresenterLineKey.RewardConfirm,
                "좋습니다, 오늘의 픽은 이쪽이네요! 바로 장착 들어갑니다.",
                "선택 끝났습니다. 그럼 다음 무대에서 성능 확인해보죠!",
                "자, 이걸로 확정! 카메라 조금만 잡아주시고요.",
                "드디어 결정됐네요. 오늘의 상품, 바로 투입합니다!",
                "좋아요, 선택 완료! 이제 실전에서 보여드릴 차례네요."),
            Mood.Excited);
    }

    public static void NotifyMapHover(BattleNodeData node, int stars)
    {
        Resolve()?.ShowMap(node, stars);
    }

    public static void NotifyMapConfirm(BattleNodeData node, int stars)
    {
        BattleScreenPresenterPrototypeController owner = Resolve();
        if (owner == null || node == null)
            return;

        owner.QueueCopy(
            "NEXT COURSE",
            $"ROUTE LOCKED / STAGE {Mathf.Max(1, node.depth + 1):00}",
            owner.PickLine(
                PresenterLineKey.MapConfirm,
                "좋습니다, 다음 무대 결정됐습니다! 이쪽으로 갑니다.",
                "선택 완료! 자, 다음 스테이지 바로 열어볼까요?",
                "다음 코스 확정입니다. 화면 전환 준비해주세요!",
                "오, 여기네요. 오늘 다음 무대는 이쪽입니다!",
                "결정됐습니다! 그럼 무대 바꿔서 바로 이어가죠."),
            Mood.Excited);
    }

    public static void NotifyPresenterMotion(
        ScreenPresenterMotionState state,
        float duration = 0.5f)
    {
        BattleScreenPresenterPrototypeController owner = Resolve();
        if (owner == null)
            return;

        owner.forcedMotionState = state;
        owner.forcedMotionUntil =
            Time.unscaledTime + Mathf.Max(0.05f, duration);
        owner.lastPresenterInteractionTime = Time.unscaledTime;
    }

    private static BattleScreenPresenterPrototypeController Resolve()
    {
        if (instance != null)
            return instance;

        instance = FindFirstObjectByType<BattleScreenPresenterPrototypeController>();
        if (instance != null)
            return instance;

        GameObject host = new("BattleScreenPresenterPrototypeRuntime");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<BattleScreenPresenterPrototypeController>();
        return instance;
    }

    // ---------------------------------------------------------------------
    // Show state
    // ---------------------------------------------------------------------

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();

        if (rewardFlow == null)
            rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);

        if (showWorld == null)
            showWorld = FindFirstObjectByType<BattleShowWorldSetController>();

        if (presentation == null)
            presentation = BattleShowPresentationManager.Instance != null
                ? BattleShowPresentationManager.Instance
                : FindFirstObjectByType<BattleShowPresentationManager>();
    }

    private void HandleStateChanged(BattleRunState previous, BattleRunState state)
    {
        bool previousSelection =
            previous == BattleRunState.Reward ||
            previous == BattleRunState.SelectingNode;

        bool selection =
            state == BattleRunState.Reward ||
            state == BattleRunState.SelectingNode;

        if (!selection && previousSelection)
            RestoreCamera();

        if (state == BattleRunState.Reward)
        {
            mode = Mode.Reward;
            EnsurePresenterSession();

            QueueCopy(
                "LIVE SHOP",
                "TODAY'S PICK",
                PickLine(
                    PresenterLineKey.RewardIntro,
                    "자, 오늘의 보상 코너 열렸습니다! 어떤 물건이 나왔을까요?",
                    "전투 종료! 그리고 바로 오늘의 상품 공개 들어갑니다.",
                    "좋습니다, 보상 진열 완료됐고요. 도전자 분의 선택만 남았습니다!",
                    "자, 카메라 이쪽 잡아주시고요. 이번 보상들 한번 보겠습니다.",
                    "오늘의 픽 후보들이 나왔습니다! 과연 뭘 들고 갈까요?"),
                Mood.Neutral);
            return;
        }

        if (state == BattleRunState.SelectingNode)
        {
            mode = Mode.Map;
            EnsurePresenterSession();

            QueueCopy(
                "ROUTE DESK",
                "NEXT STAGE",
                PickLine(
                    PresenterLineKey.MapIntro,
                    "드디어 재밌는 스테이지들이 나왔네요! 도전자 분이 어떤 선택을 할지 볼까요!?",
                    "자, 다음 무대 공개됩니다! 이번엔 어느 쪽으로 갈까요?",
                    "좋습니다, 선택지 오픈! 이번 스테이지들은 좀 기대되는데요?",
                    "다음 코스 후보가 나왔습니다. 자, 도전자 분 선택 들어갑니다!",
                    "무대가 갈렸네요! 과연 오늘의 다음 장면은 어디가 될까요?"),
                Mood.Curious);
            return;
        }

        mode = Mode.None;

        if (dialoguePhase != DialoguePhase.Hidden &&
            dialoguePhase != DialoguePhase.Closing)
        {
            dialoguePhase = DialoguePhase.Closing;
            dialoguePhaseTime = 0f;
        }

        // Presenter remains through EnteringNode / BuildingRoom / Roulette.
        // Combat is the explicit end of this broadcast presenter session.
        if (state == BattleRunState.Combat ||
            state == BattleRunState.Ended ||
            state == BattleRunState.None)
        {
            BeginPresenterBlackout();
        }
    }

    private void EnsurePresenterSession()
    {
        EnsureOverlay();

        if (presenterSessionActive)
            return;

        presenterSessionActive = true;
        presenterEntryPending = true;
        presenterEnterAt = Time.unscaledTime + presenterEntryDelay;
        presenterPhase = PresenterPhase.Hidden;
        presenterTint = 0f;
        presenterMotionState = ScreenPresenterMotionState.Idle;
        forcedMotionUntil = -1f;
        lastPresenterInteractionTime = Time.unscaledTime;
        nextBoredLineAt =
            Time.unscaledTime + Mathf.Max(2f, boredLineAfterSeconds);

        if (presenterRect != null)
        {
            presenterRect.anchoredPosition =
                presenterVisibleOffset + Vector2.right * presenterHiddenOffsetX;
        }

        if (presenterGroup != null)
            presenterGroup.alpha = 0f;

        ApplyPresenterTint();
        UpdatePresenterVisualSource();
    }

    private void BeginPresenterBlackout()
    {
        presenterEntryPending = false;

        if (!presenterSessionActive ||
            presenterPhase == PresenterPhase.Hidden)
        {
            presenterSessionActive = false;
            return;
        }

        presenterPhase = PresenterPhase.BlackingOut;
        SetPresenterMotionState(ScreenPresenterMotionState.Shutdown);
    }

    // ---------------------------------------------------------------------
    // Overlay construction
    // ---------------------------------------------------------------------

    private void EnsureOverlay()
    {
        if (overlayCanvas != null && overlayRoot != null)
            return;

        GameObject canvasObject = new("BattlePresenterBroadcastOverlay");
        canvasObject.transform.SetParent(transform, false);

        overlayCanvas = canvasObject.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = overlaySortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        overlayRoot = canvasObject.GetComponent<RectTransform>();

        BuildPresenterSquare();
        BuildDialogue();
    }

    private void BuildPresenterSquare()
    {
        GameObject go = new("PresenterScreenCutIn", typeof(RectTransform));
        go.transform.SetParent(overlayRoot, false);

        presenterRect = go.GetComponent<RectTransform>();
        // Bottom-right anchored on purpose: the presenter is treated like a cut-in illustration,
        // not a fitted UI portrait. Positive X / negative Y intentionally crop it against the screen.
        presenterRect.anchorMin = presenterRect.anchorMax = new Vector2(1f, 0f);
        presenterRect.pivot = new Vector2(1f, 0f);
        presenterRect.sizeDelta = presenterSize;
        presenterRect.anchoredPosition =
            presenterVisibleOffset + Vector2.right * presenterHiddenOffsetX;

        presenterImage = go.AddComponent<Image>();
        presenterImage.preserveAspect = true;
        presenterImage.raycastTarget = false;
        presenterImage.sprite = BattleHudSpriteCache.DefaultSprite;

        GameObject tailPivot = new("PresenterDialogueTailPivot", typeof(RectTransform));
        tailPivot.transform.SetParent(presenterRect, false);
        dialogueTailPivotRect = tailPivot.GetComponent<RectTransform>();
        dialogueTailPivotRect.anchorMin =
            dialogueTailPivotRect.anchorMax =
                dialogueTailPresenterAnchor;
        dialogueTailPivotRect.pivot = new Vector2(0.5f, 0.5f);
        dialogueTailPivotRect.sizeDelta = Vector2.zero;
        dialogueTailPivotRect.anchoredPosition = dialogueTailPivotOffset;

        presenterGroup = go.AddComponent<CanvasGroup>();
        presenterGroup.alpha = 0f;
        presenterGroup.interactable = false;
        presenterGroup.blocksRaycasts = false;

        ApplyPresenterTint();
    }

    private void BuildDialogue()
    {
        Color bubbleFill = new(0.97f, 0.97f, 0.94f, 1f);
        Color bubbleInk = new(0.012f, 0.012f, 0.018f, 0.99f);
        Color bubbleText = new(0.035f, 0.035f, 0.045f, 1f);
        Color bubbleMuted = new(0.24f, 0.25f, 0.29f, 1f);

        GameObject root = new("PresenterDialogueBubble", typeof(RectTransform));
        root.transform.SetParent(overlayRoot, false);

        dialogueRect = root.GetComponent<RectTransform>();
        dialogueRect.anchorMin = dialogueRect.anchorMax = new Vector2(0.5f, 0f);
        dialogueRect.pivot = new Vector2(0.5f, 0f);
        dialogueRect.sizeDelta = dialogueSize;
        dialogueRect.anchoredPosition =
            dialogueVisibleOffset - Vector2.up * dialogueHiddenOffsetY;
        dialogueRect.localRotation =
            Quaternion.Euler(0f, 0f, dialogueBubbleRotation);

        dialogueRenderCanvas = root.AddComponent<Canvas>();
        dialogueRenderCanvas.overrideSorting = true;
        dialogueRenderCanvas.sortingOrder = dialogueMiniPackSortingOrder;

        dialogueGroup = root.AddComponent<CanvasGroup>();
        dialogueGroup.alpha = 0f;
        dialogueGroup.interactable = false;
        dialogueGroup.blocksRaycasts = false;

        // Pivot-driven jagged tail. This sits behind the bubble but reaches the
        // presenter pivot even while the presenter idles, hops or scales.
        GameObject tail = new("PresenterDialogueTail", typeof(RectTransform));
        tail.transform.SetParent(overlayRoot, false);
        RectTransform tailRect = tail.GetComponent<RectTransform>();
        tailRect.anchorMin = Vector2.zero;
        tailRect.anchorMax = Vector2.one;
        tailRect.pivot = new Vector2(0.5f, 0.5f);
        tailRect.offsetMin = Vector2.zero;
        tailRect.offsetMax = Vector2.zero;

        dialogueTailCanvas = tail.AddComponent<Canvas>();
        dialogueTailCanvas.overrideSorting = true;
        dialogueTailCanvas.sortingOrder = dialogueMiniPackSortingOrder - 1;

        dialogueTailGroup = tail.AddComponent<CanvasGroup>();
        dialogueTailGroup.alpha = 0f;
        dialogueTailGroup.interactable = false;
        dialogueTailGroup.blocksRaycasts = false;

        dialogueTailGraphic =
            tail.AddComponent<BattleSpeechBubbleTailLineRenderer>();
        dialogueTailGraphic.Configure(
            dialogueRect,
            dialogueTailPivotRect,
            bubbleFill,
            bubbleInk,
            dialogueTailBaseWidth,
            dialogueTailOutlineWidth);

        // Rough black ink silhouette slightly larger than the white face.
        GameObject ink = new("BubbleInkBack", typeof(RectTransform));
        ink.transform.SetParent(dialogueRect, false);
        RectTransform inkRect = ink.GetComponent<RectTransform>();
        inkRect.anchorMin = Vector2.zero;
        inkRect.anchorMax = Vector2.one;
        inkRect.pivot = new Vector2(0.5f, 0.5f);
        inkRect.offsetMin = new Vector2(-8f, -9f);
        inkRect.offsetMax = new Vector2(8f, 9f);
        Image inkImage = ink.AddComponent<Image>();
        inkImage.color = bubbleInk;
        inkImage.raycastTarget = false;

        GameObject face = new("BubbleFace", typeof(RectTransform));
        face.transform.SetParent(dialogueRect, false);
        RectTransform faceRect = face.GetComponent<RectTransform>();
        faceRect.anchorMin = Vector2.zero;
        faceRect.anchorMax = Vector2.one;
        faceRect.pivot = new Vector2(0.5f, 0.5f);
        faceRect.offsetMin = new Vector2(2f, 2f);
        faceRect.offsetMax = new Vector2(-2f, -2f);
        Image faceImage = face.AddComponent<Image>();
        faceImage.color = bubbleFill;
        faceImage.raycastTarget = false;

        // Small black badge replaces the old flat "SHOW HOST" strip.
        GameObject badge = new("PresenterNameBadge", typeof(RectTransform));
        badge.transform.SetParent(dialogueRect, false);
        RectTransform badgeRect = badge.GetComponent<RectTransform>();
        badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0f, 1f);
        badgeRect.pivot = new Vector2(0f, 0.5f);
        badgeRect.sizeDelta = new Vector2(210f, 38f);
        badgeRect.anchoredPosition = new Vector2(22f, 5f);
        badgeRect.localRotation = Quaternion.Euler(0f, 0f, 2.5f);
        Image badgeImage = badge.AddComponent<Image>();
        badgeImage.color = bubbleInk;
        badgeImage.raycastTarget = false;

        nameText = CreateText(
            badgeRect,
            "PresenterName",
            "SHOW HOST",
            17,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            Color.white);
        Place(
            nameText.rectTransform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(190f, 30f));

        contextText = CreateText(
            dialogueRect,
            "Context",
            string.Empty,
            11,
            FontStyle.Bold,
            TextAnchor.MiddleRight,
            bubbleMuted);
        Place(
            contextText.rectTransform,
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-24f, -11f),
            new Vector2(560f, 26f));

        dialogueText = CreateText(
            dialogueRect,
            "DialogueText",
            string.Empty,
            27,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            bubbleText);

        RectTransform textRect = dialogueText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(30f, 18f);
        textRect.offsetMax = new Vector2(-34f, -38f);
        dialogueText.horizontalOverflow = HorizontalWrapMode.Wrap;
        dialogueText.verticalOverflow = VerticalWrapMode.Truncate;
        dialogueText.lineSpacing = 1.04f;

        liveText = CreateText(
            dialogueRect,
            "Live",
            "ON LIVE",
            10,
            FontStyle.Bold,
            TextAnchor.LowerRight,
            hotAccent);
        Place(
            liveText.rectTransform,
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(-20f, 8f),
            new Vector2(120f, 20f));

        tail.transform.SetAsFirstSibling();
        ink.transform.SetSiblingIndex(1);
        face.transform.SetSiblingIndex(2);

        dialogueRect.localScale =
            Vector3.one * dialogueBubbleStartScale;
    }

    // ---------------------------------------------------------------------
    // Presenter: constant-speed slide / tint / animation
    // ---------------------------------------------------------------------

    private void UpdatePresenterMotion()
    {
        if (presenterEntryPending &&
            presenterSessionActive &&
            Time.unscaledTime >= presenterEnterAt)
        {
            presenterEntryPending = false;
            presenterPhase = PresenterPhase.Entering;

            if (presenterGroup != null)
                presenterGroup.alpha = 1f;
        }

        if (presenterRect == null || presenterImage == null)
            return;

        float dt = Time.unscaledDeltaTime;

        switch (presenterPhase)
        {
            case PresenterPhase.Hidden:
                if (presenterGroup != null)
                    presenterGroup.alpha = 0f;
                break;

            case PresenterPhase.Entering:
            {
                if (presenterGroup != null)
                    presenterGroup.alpha = 1f;

                presenterRect.anchoredPosition = Vector2.MoveTowards(
                    presenterRect.anchoredPosition,
                    presenterVisibleOffset,
                    Mathf.Max(50f, presenterSlideSpeedPixels) * dt);

                presenterTint = Mathf.MoveTowards(
                    presenterTint,
                    1f,
                    Mathf.Max(0.1f, presenterRevealPerSecond) * dt);

                ApplyPresenterTint();

                bool positionDone =
                    Vector2.SqrMagnitude(
                        presenterRect.anchoredPosition - presenterVisibleOffset) < 0.01f;

                if (positionDone && presenterTint >= 0.999f)
                {
                    presenterRect.anchoredPosition = presenterVisibleOffset;
                    presenterTint = 1f;
                    ApplyPresenterTint();
                    presenterPhase = PresenterPhase.Active;
                }
                break;
            }

            case PresenterPhase.Active:
                presenterRect.anchoredPosition = presenterVisibleOffset;
                presenterTint = 1f;
                ApplyPresenterTint();
                ApplyPresenterIdle();
                break;

            case PresenterPhase.BlackingOut:
                presenterRect.anchoredPosition = presenterVisibleOffset;

                presenterTint = Mathf.MoveTowards(
                    presenterTint,
                    0f,
                    Mathf.Max(0.1f, presenterBlackoutPerSecond) * dt);

                ApplyPresenterTint();

                if (presenterTint <= 0.001f)
                {
                    presenterTint = 0f;
                    ApplyPresenterTint();

                    if (presenterGroup != null)
                        presenterGroup.alpha = 0f;

                    presenterPhase = PresenterPhase.Hidden;
                    presenterSessionActive = false;
                }
                break;
        }
    }

    private void ApplyPresenterIdle()
    {
        if (presenterRect == null)
            return;

        float cycle = Mathf.Max(0.1f, idleCyclesPerSecond);
        float vertical = LinearPingPong(Time.unscaledTime * cycle);
        float horizontal = LinearPingPong(Time.unscaledTime * cycle * 0.73f + 0.31f);

        Vector2 offset = new(
            horizontal * idleSidePixels,
            vertical * idleMovePixels);

        float scale = 1f + vertical * idleScaleAmount;

        if (dialoguePhase == DialoguePhase.Typing)
        {
            float talk = LinearPingPong(
                Time.unscaledTime * Mathf.Max(0.1f, talkShakeCyclesPerSecond));

            offset.x += talk * talkShakePixels;
        }

        float reactionAge = Time.unscaledTime - reactionStartedAt;
        if (reactionAge >= 0f && reactionAge < reactionDuration)
        {
            float t = Mathf.Clamp01(reactionAge / Mathf.Max(0.05f, reactionDuration));
            float hop = t < 0.5f ? t * 2f : (1f - t) * 2f;
            float multiplier = reactionMood == Mood.Excited
                ? excitedReactionMultiplier
                : 1f;

            offset.y += hop * reactionHopPixels * multiplier;
            scale += hop * 0.018f * multiplier;
        }

        presenterRect.anchoredPosition = presenterVisibleOffset + offset;
        presenterRect.localScale = Vector3.one * scale;
    }

    private void ApplyPresenterTint()
    {
        if (presenterImage == null)
            return;

        float v = Mathf.Clamp01(presenterTint);
        presenterImage.color = new Color(v, v, v, 1f);
    }

    private void UpdatePresenterMotionState()
    {
        if (presenterPhase == PresenterPhase.BlackingOut)
        {
            SetPresenterMotionState(ScreenPresenterMotionState.Shutdown);
            return;
        }

        if (!presenterSessionActive ||
            presenterPhase == PresenterPhase.Hidden)
        {
            SetPresenterMotionState(ScreenPresenterMotionState.Idle);
            return;
        }

        if (forcedMotionUntil > Time.unscaledTime)
        {
            SetPresenterMotionState(forcedMotionState);
            return;
        }

        if (dialoguePhase == DialoguePhase.Typing)
        {
            SetPresenterMotionState(ScreenPresenterMotionState.Talk);
            return;
        }

        float reactionAge = Time.unscaledTime - reactionStartedAt;
        if (reactionAge >= 0f && reactionAge < reactionDuration)
        {
            SetPresenterMotionState(MotionFromMood(reactionMood));
            return;
        }

        if (presenterPhase == PresenterPhase.Active &&
            Time.unscaledTime - lastPresenterInteractionTime >=
            Mathf.Max(1f, boredAfterSeconds))
        {
            SetPresenterMotionState(ScreenPresenterMotionState.Bored);
            return;
        }

        SetPresenterMotionState(ScreenPresenterMotionState.Idle);
    }

    private static ScreenPresenterMotionState MotionFromMood(Mood mood)
    {
        return mood switch
        {
            Mood.Curious => ScreenPresenterMotionState.Curious,
            Mood.Excited => ScreenPresenterMotionState.Excited,
            Mood.Concerned => ScreenPresenterMotionState.Concerned,
            _ => ScreenPresenterMotionState.Idle
        };
    }

    private void SetPresenterMotionState(ScreenPresenterMotionState state)
    {
        if (presenterMotionState == state)
            return;

        presenterMotionState = state;
        activeMotionClip = null;
        activeMotionSource = null;
        motionFrameIndex = 0;
        motionFrameTimer = 0f;
    }

    private void UpdatePresenterVisualSource()
    {
        if (presenterImage == null)
            return;

        ScreenPresenterMotionClip clip =
            ResolvePresenterMotionClip(presenterMotionState);

        Sprite source = clip != null ? clip.source : null;

        if (activeMotionClip != clip ||
            activeMotionSource != source)
        {
            activeMotionClip = clip;
            activeMotionSource = source;
            BuildRuntimeMotionFrames(source);
        }

        if (runtimeMotionFrames.Count > 0)
        {
            motionFrameIndex = Mathf.Clamp(
                motionFrameIndex,
                0,
                runtimeMotionFrames.Count - 1);
            presenterImage.sprite = runtimeMotionFrames[motionFrameIndex];
        }
        else
        {
            presenterImage.sprite =
                source != null
                    ? source
                    : BattleHudSpriteCache.DefaultSprite;
        }

        presenterImage.preserveAspect = true;

        Material material = presentation != null
            ? presentation.ScreenPresenterMaterial
            : null;

        if (appliedPresenterMaterial != material)
        {
            appliedPresenterMaterial = material;
            presenterImage.material = material;
        }
    }

    private ScreenPresenterMotionClip ResolvePresenterMotionClip(
        ScreenPresenterMotionState state)
    {
        if (presentation == null)
            return null;

        ScreenPresenterMotionClip clip =
            presentation.GetScreenPresenterMotion(state);

        if (clip != null && clip.source != null)
            return clip;

        ScreenPresenterMotionClip idle =
            presentation.GetScreenPresenterMotion(
                ScreenPresenterMotionState.Idle);

        return idle != null && idle.source != null
            ? idle
            : clip;
    }

    private void BuildRuntimeMotionFrames(Sprite source)
    {
        ReleaseRuntimeMotionFrames();
        motionFrameIndex = 0;
        motionFrameTimer = 0f;

        if (source == null)
            return;

        int width = Mathf.RoundToInt(source.rect.width);
        int height = Mathf.RoundToInt(source.rect.height);

        bool sheet =
            width > PresenterFramePixels ||
            height > PresenterFramePixels;

        if (!sheet)
            return;

        int columns = Mathf.Max(1, width / PresenterFramePixels);
        int rows = Mathf.Max(1, height / PresenterFramePixels);

        if ((width % PresenterFramePixels != 0 ||
             height % PresenterFramePixels != 0) &&
            !warnedNonMultipleSheet)
        {
            warnedNonMultipleSheet = true;
            Debug.LogWarning(
                $"[ScreenPresenter] '{source.name}' 크기 {width}x{height}는 " +
                $"{PresenterFramePixels}px의 정확한 배수가 아닙니다. " +
                "완전한 128x128 셀만 사용하고 남는 픽셀은 무시합니다.",
                this);
        }

        Rect sourceRect = source.rect;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                float x =
                    sourceRect.x +
                    column * PresenterFramePixels;

                // Unity Rect origin is bottom-left; artist sheet order is top-left.
                float y =
                    sourceRect.y +
                    sourceRect.height -
                    (row + 1) * PresenterFramePixels;

                if (x + PresenterFramePixels > sourceRect.xMax + 0.01f ||
                    y < sourceRect.y - 0.01f)
                {
                    continue;
                }

                Sprite frame = Sprite.Create(
                    source.texture,
                    new Rect(
                        x,
                        y,
                        PresenterFramePixels,
                        PresenterFramePixels),
                    new Vector2(0.5f, 0.5f),
                    source.pixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect);

                frame.name =
                    $"{source.name}_Runtime_{row:00}_{column:00}";
                frame.hideFlags = HideFlags.HideAndDontSave;
                runtimeMotionFrames.Add(frame);
            }
        }
    }

    private void UpdatePresenterSheetAnimation()
    {
        if (presenterImage == null ||
            runtimeMotionFrames.Count <= 1)
        {
            return;
        }

        float fps =
            activeMotionClip != null
                ? Mathf.Max(1f, activeMotionClip.fps)
                : 8f;

        float frameDuration = 1f / fps;
        motionFrameTimer += Time.unscaledDeltaTime;

        while (motionFrameTimer >= frameDuration)
        {
            motionFrameTimer -= frameDuration;
            motionFrameIndex =
                (motionFrameIndex + 1) %
                runtimeMotionFrames.Count;
        }

        presenterImage.sprite =
            runtimeMotionFrames[motionFrameIndex];
    }

    private void ReleaseRuntimeMotionFrames()
    {
        for (int i = 0; i < runtimeMotionFrames.Count; i++)
        {
            Sprite frame = runtimeMotionFrames[i];
            if (frame != null)
                Destroy(frame);
        }

        runtimeMotionFrames.Clear();
    }

    // ---------------------------------------------------------------------
    // Dialogue: open -> type -> idle -> close -> update
    // ---------------------------------------------------------------------

    private void QueueCopy(
        string header,
        string keyword,
        string comment,
        Mood mood,
        bool countsAsInteraction = true)
    {
        pendingHeader = header ?? string.Empty;
        pendingKeyword = keyword ?? string.Empty;
        pendingComment = comment ?? string.Empty;
        pendingMood = mood;
        hasPendingCopy = true;

        EnsureOverlay();
        reactionMood = mood;
        reactionStartedAt = Time.unscaledTime;

        if (countsAsInteraction)
        {
            lastPresenterInteractionTime = Time.unscaledTime;
            nextBoredLineAt =
                Time.unscaledTime + Mathf.Max(2f, boredLineAfterSeconds);
        }

        if (dialoguePhase == DialoguePhase.Hidden)
        {
            if (!presenterEntryPending &&
                presenterPhase != PresenterPhase.Hidden)
            {
                BeginPendingDialogue();
            }
            return;
        }

        if (dialoguePhase != DialoguePhase.Closing)
        {
            dialoguePhase = DialoguePhase.Closing;
            dialoguePhaseTime = 0f;
        }
    }

    private void UpdateBoredCommentary()
    {
        if (!presenterSessionActive ||
            presenterPhase != PresenterPhase.Active ||
            mode == Mode.None ||
            dialoguePhase != DialoguePhase.Hidden ||
            hasPendingCopy ||
            Time.unscaledTime < nextBoredLineAt)
        {
            return;
        }

        string line = mode == Mode.Reward
            ? PickLine(
                PresenterLineKey.BoredReward,
                "음... 아직 고르는 중인가 보네요. 광고라도 하나 넣을까요?",
                "어디 간 거야? 담배라도 피러 갔나? 하...",
                "생각보다 고민이 길어지네요. 자, 카메라는 일단 상품 쪽 잡아주시고요.",
                "이 정도면 하나쯤 눈에 들어올 법도 한데... 아직인가요?",
                "아직도 보고 있네요. 음... 저희는 계속 방송 중입니다.")
            : PickLine(
                PresenterLineKey.BoredMap,
                "음... 아직도 고민 중이네요. 다음 코너 준비라도 해둘까요?",
                "어디 간 거야? 잠깐 자리 비운 건가?",
                "길 하나 고르는 데 생각보다 오래 걸리네요. 카메라만 계속 돌고 있습니다.",
                "뭐, 급할 건 없죠. 저희 방송은 아직 안 끝났습니다.",
                "아직인가... 음, 무대 쪽 조명은 계속 켜두죠.");

        QueueCopy(
            "STANDBY",
            mode == Mode.Reward ? "WAITING FOR PICK" : "WAITING FOR ROUTE",
            line,
            Mood.Neutral,
            false);

        forcedMotionState = ScreenPresenterMotionState.Bored;
        forcedMotionUntil = Time.unscaledTime + 1.4f;
        nextBoredLineAt =
            Time.unscaledTime + Mathf.Max(5f, boredLineRepeatSeconds);
    }

    private void BeginPendingDialogue()
    {
        if (!hasPendingCopy || dialogueText == null)
            return;

        activeHeader = pendingHeader;
        activeKeyword = pendingKeyword;
        activeComment = pendingComment;
        activeMood = pendingMood;
        hasPendingCopy = false;

        ApplyDialogueMetadata();

        dialogueText.text = string.Empty;
        visibleCharacters = 0;
        typeProgress = 0f;
        dialoguePhaseTime = 0f;
        dialoguePhase = DialoguePhase.Opening;
    }

    private void UpdateDialogueState()
    {
        if (dialogueGroup == null || dialogueRect == null)
            return;

        float dt = Time.unscaledDeltaTime;

        switch (dialoguePhase)
        {
            case DialoguePhase.Hidden:
                ApplyDialogueOpenValue(0f);

                if (mode != Mode.None &&
                    hasPendingCopy &&
                    !presenterEntryPending &&
                    presenterPhase != PresenterPhase.Hidden)
                {
                    BeginPendingDialogue();
                }
                break;

            case DialoguePhase.Opening:
            {
                dialoguePhaseTime += dt;
                float t = Mathf.Clamp01(
                    dialoguePhaseTime /
                    Mathf.Max(0.03f, dialogueOpenDuration));

                ApplyDialogueOpenValue(Smooth01(t));

                if (t >= 1f)
                {
                    dialoguePhase = DialoguePhase.Typing;
                    dialoguePhaseTime = 0f;
                }
                break;
            }

            case DialoguePhase.Typing:
            {
                ApplyDialogueOpenValue(1f);

                typeProgress += dt * Mathf.Max(1f, typeCharactersPerSecond);

                int targetCharacters = Mathf.Clamp(
                    Mathf.FloorToInt(typeProgress),
                    0,
                    activeComment.Length);

                if (targetCharacters != visibleCharacters)
                {
                    visibleCharacters = targetCharacters;
                    dialogueText.text =
                        activeComment.Substring(0, visibleCharacters);
                }

                if (visibleCharacters >= activeComment.Length)
                {
                    dialogueText.text = activeComment;
                    dialoguePhase = DialoguePhase.Idle;
                    dialoguePhaseTime = 0f;
                }
                break;
            }

            case DialoguePhase.Idle:
                ApplyDialogueOpenValue(1f);
                dialoguePhaseTime += dt;

                if (hasPendingCopy ||
                    dialoguePhaseTime >= Mathf.Max(0.1f, dialogueIdleDuration))
                {
                    dialoguePhase = DialoguePhase.Closing;
                    dialoguePhaseTime = 0f;
                }
                break;

            case DialoguePhase.Closing:
            {
                dialoguePhaseTime += dt;
                float t = Mathf.Clamp01(
                    dialoguePhaseTime /
                    Mathf.Max(0.03f, dialogueCloseDuration));

                ApplyDialogueOpenValue(1f - Smooth01(t));

                if (t >= 1f)
                {
                    dialogueText.text = string.Empty;
                    dialoguePhaseTime = 0f;

                    if (mode != Mode.None && hasPendingCopy)
                        BeginPendingDialogue();
                    else
                        dialoguePhase = DialoguePhase.Hidden;
                }
                break;
            }
        }
    }

    private void ApplyDialogueOpenValue(float value)
    {
        float clamped = Mathf.Clamp01(value);

        dialogueGroup.alpha = clamped;
        if (dialogueTailGroup != null)
            dialogueTailGroup.alpha = clamped;

        dialogueRect.anchoredPosition = dialogueVisibleOffset;

        float scale;
        if (clamped < 0.72f)
        {
            float t = clamped / 0.72f;
            scale = Mathf.Lerp(
                dialogueBubbleStartScale,
                dialogueBubbleOvershootScale,
                Smooth01(t));
        }
        else
        {
            float t = (clamped - 0.72f) / 0.28f;
            scale = Mathf.Lerp(
                dialogueBubbleOvershootScale,
                1f,
                Smooth01(t));
        }

        dialogueRect.localScale = Vector3.one * scale;
    }

    private void UpdateDialogueSorting()
    {
        if (dialogueRenderCanvas == null)
            return;

        bool expandedRewardPack =
            runManager != null &&
            runManager.State == BattleRunState.Reward &&
            rewardFlow != null &&
            (rewardFlow.Phase == BattleRewardPhase.Transferring ||
             rewardFlow.Phase == BattleRewardPhase.PackEditing);

        // Mini PACK lives on BattleKineticLoadoutCanvas order 780.
        // During choice/map the speech bubble sits just above it.
        // Once the large PACK opens, restore the old presenter layer so
        // inventory placement/editing remains visually authoritative.
        int bubbleOrder = expandedRewardPack
            ? overlaySortingOrder
            : Mathf.Max(dialogueMiniPackSortingOrder, 781);

        dialogueRenderCanvas.sortingOrder = bubbleOrder;

        if (dialogueTailCanvas != null)
            dialogueTailCanvas.sortingOrder = bubbleOrder - 1;
    }

    private void ApplyDialogueMetadata()
    {
        if (nameText != null)
            nameText.text = "SHOW HOST";

        if (contextText != null)
        {
            contextText.text = string.IsNullOrWhiteSpace(activeKeyword)
                ? activeHeader
                : $"{activeHeader} / {activeKeyword}";
        }

        if (liveText != null)
        {
            liveText.text = activeMood switch
            {
                Mood.Curious => "CURIOUS",
                Mood.Excited => "HOT PICK",
                Mood.Concerned => "CAUTION",
                _ => "ON LIVE"
            };

            liveText.color = activeMood == Mood.Concerned
                ? new Color(1f, 0.55f, 0.18f, 1f)
                : activeMood == Mood.Excited
                    ? hotAccent
                    : muted;
        }
    }

    // ---------------------------------------------------------------------
    // Camera
    // ---------------------------------------------------------------------

    private void ApplyPrototypeCamera()
    {
        if (showWorld == null || !showWorld.HasCameraAnchor)
            return;

        showWorld.RecomputeSharedCameraFrame();

        bool reward = mode == Mode.Reward;

        float horizontalBias = reward
            ? rewardCameraRightBiasWorld
            : mapCameraHorizontalBiasWorld;

        float zoomRatio = reward
            ? rewardCameraZoomRatio
            : mapCameraZoomRatio;

        Vector3 target =
            showWorld.CameraTargetWorld +
            Vector3.right * horizontalBias;

        float size =
            showWorld.ShowCameraSize *
            Mathf.Max(0.1f, zoomRatio);

        showWorld.OverrideShowCameraFrame(target, size);
        cameraApplied = true;
    }

    private void RestoreCamera()
    {
        if (!cameraApplied)
            return;

        if (showWorld != null)
            showWorld.RecomputeSharedCameraFrame();

        cameraApplied = false;
    }

    // ---------------------------------------------------------------------
    // Commentary selection
    // ---------------------------------------------------------------------

    private void ShowReward(BattleEquipmentSO equipment, bool selected)
    {
        if (equipment == null)
            return;

        string displayName = equipment.GetDisplayName();
        string lower = string.IsNullOrEmpty(displayName)
            ? string.Empty
            : displayName.ToLowerInvariant();

        if (lower.Contains("톱") || lower.Contains("saw"))
        {
            QueueCopy(
                selected ? "PICKED" : "CURIOUS PICK",
                "ODD WEAPON",
                PickLine(
                    PresenterLineKey.Saw,
                    "자, 오늘의 괴상한 상품 나왔습니다. 톱입니다!",
                    "카메라 조금만 당겨주세요. 네, 진짜 톱 맞습니다.",
                    "이건 화면에 잡히는 순간부터 존재감이 있네요.",
                    "도전자 분이 이걸 집으면 다음 전투 그림은 확실하겠는데요?",
                    "정상적인 무기는 잠깐 잊으시죠. 오늘 후보는 톱입니다."),
                Mood.Curious);
            return;
        }

        if (equipment.HasTag(EquipmentTag.OddWeapon))
        {
            QueueCopy(
                selected ? "PICKED" : "CULT PICK",
                "ODD WEAPON",
                PickLine(
                    PresenterLineKey.OddWeapon,
                    "자, 특이 상품 코너입니다. 이건 설명부터 쉽지 않네요.",
                    "이런 물건이 하나쯤 있어야 쇼가 재밌죠.",
                    "카메라 한 번 잡아주시고요. 이건 도전자 분 반응도 궁금합니다.",
                    "정석과는 좀 거리가 있네요. 대신 눈길은 확실히 갑니다.",
                    "오늘 진열대에서 제일 이상한 후보, 일단 이쪽입니다."),
                Mood.Curious);
            return;
        }

        if (equipment.rarity == EquipmentRarity.Unique ||
            equipment.rarity == EquipmentRarity.Epic)
        {
            QueueCopy(
                selected ? "PICKED" : "SPECIAL ITEM",
                $"{equipment.rarity.ToString().ToUpperInvariant()} / {displayName}",
                PickLine(
                    PresenterLineKey.RareItem,
                    "오, 잠깐만요. 오늘의 메인 상품 후보 나왔습니다!",
                    "이건 카메라 좀 더 잡아주세요. 급이 다르네요.",
                    "좋습니다, 이런 게 하나쯤 떠줘야 보상 코너가 살죠.",
                    "희귀 상품 등장입니다. 도전자 분도 그냥 지나치긴 어렵겠는데요?",
                    "자, 오늘 진열대에서 가장 눈에 띄는 후보 중 하나입니다!"),
                Mood.Excited);
            return;
        }

        if (equipment.HasTag(EquipmentTag.Explosion) ||
            equipment.HasTag(EquipmentTag.Burn))
        {
            QueueCopy(
                selected ? "PICKED" : "HOT ITEM",
                "EXPLOSIVE / PRESSURE",
                PickLine(
                    PresenterLineKey.HotItem,
                    "자, 화력 담당 나왔습니다. 다음 무대 꽤 시끄럽겠는데요?",
                    "이쪽은 설명보다 효과 화면이 먼저 떠오르네요.",
                    "좋습니다, 이런 건 전투 들어가면 바로 티가 납니다!",
                    "다음 전투에 불꽃 좀 추가하고 싶다면 이쪽이겠네요.",
                    "오늘의 화끈한 상품, 카메라 이쪽 한번 잡아주시죠."),
                Mood.Excited);
            return;
        }

        if (equipment.HasTag(EquipmentTag.Heal) ||
            equipment.HasTag(EquipmentTag.Defense) ||
            equipment.HasTag(EquipmentTag.Sustain))
        {
            QueueCopy(
                selected ? "PICKED" : "SAFE PICK",
                "SURVIVAL / STABILITY",
                PickLine(
                    PresenterLineKey.SafeItem,
                    "자, 이번엔 안정성 상품입니다. 화려하진 않아도 오래 갑니다.",
                    "조금 얌전한 후보네요. 대신 이런 게 막상 실전에서는 든든하죠.",
                    "오늘의 안전 운전 코너입니다. 생존 쪽으로 챙겨가네요.",
                    "화면은 조용해도 효과는 확실한 타입입니다.",
                    "조금 심심해 보이죠? 그래도 이런 상품이 끝까지 남습니다."),
                Mood.Neutral);
            return;
        }

        QueueCopy(
            selected ? "PICKED" : "ITEM CHECK",
            $"{equipment.rarity.ToString().ToUpperInvariant()} / {displayName}",
            selected
                ? PickLine(
                    PresenterLineKey.GenericSelected,
                    "오, 이쪽이 후보로 올라왔네요!",
                    "좋습니다, 도전자 분 시선이 여기서 멈췄습니다.",
                    "자, 이 상품에 표시 들어갑니다. 최종 픽까지 갈까요?",
                    "여기 하나 체크됐네요. 일단 후보 등록입니다.",
                    "이쪽이 마음에 들었나 봅니다. 카메라 그대로 유지해주세요.")
                : PickLine(
                    PresenterLineKey.GenericHover,
                    "자, 다음 상품입니다. 음... 꽤 무난하네요.",
                    "이번 후보는 정석 쪽입니다. 크게 튀진 않네요.",
                    "카메라 잡아주시고요. 이건 조합을 봐야 판단이 나오겠네요.",
                    "특별한 맛은 적지만 기본은 해줄 것 같습니다.",
                    "음, 쉬어가는 상품이네요. 그래도 후보에서는 빠지진 않겠고요.",
                    "이번 건 실전 화면을 봐야 평가가 나오겠습니다."),
            selected ? Mood.Curious : Mood.Neutral);
    }

    private void ShowMap(BattleNodeData node, int stars)
    {
        if (node == null)
            return;

        int clampedStars = Mathf.Clamp(stars, 1, 5);
        string stage =
            $"STAGE {Mathf.Max(1, node.depth + 1):00} / {clampedStars} STAR";

        switch (node.type)
        {
            case BattleNodeType.Elite:
                QueueCopy(
                    "CAUTION",
                    $"{stage} / ELITE",
                    PickLine(
                        PresenterLineKey.MapElite,
                        "오, 엘리트 스테이지 공개됐습니다! 이제 좀 쇼다운 냄새가 나는데요?",
                        "자, 오늘의 위험 구간입니다. 도전자 분이 이쪽을 고를까요?",
                        "이쪽은 무대가 조금 거칠겠네요. 대신 볼거리는 확실합니다.",
                        "엘리트 코스 등장! 카메라 여기 조금 더 잡아주시고요.",
                        "좋습니다, 이런 스테이지가 하나쯤 있어야 분위기가 올라오죠!"),
                    Mood.Excited);
                break;

            case BattleNodeType.Shop:
                QueueCopy(
                    "SHOPPING BREAK",
                    $"{stage} / SHOP",
                    PickLine(
                        PresenterLineKey.MapShop,
                        "자, 잠깐 쉬어가는 쇼핑 코너입니다!",
                        "다음 무대 전에 장비 점검 한번 하고 갈 수 있겠네요.",
                        "상점 스테이지 공개됐습니다. 전투 대신 쇼핑 타임이네요.",
                        "여기서는 잠깐 템포를 낮추죠. 뭘 살지도 하나의 볼거리니까요.",
                        "좋습니다, 브레이크 타임입니다. 지갑은 조금 바빠지겠지만요."),
                    Mood.Neutral);
                break;

            case BattleNodeType.Event:
                QueueCopy(
                    "SPECIAL SEGMENT",
                    $"{stage} / EVENT",
                    PickLine(
                        PresenterLineKey.MapEvent,
                        "자, 특별 코너 하나 들어왔습니다. 이벤트 스테이지!",
                        "오, 이쪽은 내용 비공개네요. 이런 건 열어봐야 맛이 있죠.",
                        "전투 말고 다른 장면이 준비돼 있습니다. 뭐가 나올까요?",
                        "이벤트 코스 등장입니다. 결과를 모른다는 게 포인트네요.",
                        "자, 잠깐 분위기 바꿔볼 수 있는 선택지가 나왔습니다."),
                    Mood.Curious);
                break;

            default:
                if (clampedStars >= 4)
                {
                    QueueCopy(
                        "COURSE CHECK",
                        $"{stage} / COMBAT",
                        PickLine(
                            PresenterLineKey.MapCombatHigh,
                            "오, 드디어 재밌는 무대가 나왔네요! 이쪽 선택 들어갈까요?",
                            "별 네 개 이상! 오늘의 메인 스테이지 후보입니다.",
                            "자, 긴장감 올라갑니다. 이쪽은 쉽게 끝날 것 같진 않네요.",
                            "좋습니다, 이제 좀 쇼다운 분위기가 나는데요?",
                            "이건 다음 장면 기대해도 되겠습니다. 도전자 분 선택만 남았네요!"),
                        Mood.Excited);
                    break;
                }

                if (clampedStars == 3)
                {
                    QueueCopy(
                        "COURSE CHECK",
                        $"{stage} / COMBAT",
                        PickLine(
                            PresenterLineKey.MapCombatMid,
                            "자, 중간 난이도 무대입니다. 깔끔하게 한 판 보기 좋겠네요.",
                            "별 세 개, 딱 적당한 스테이지가 나왔습니다.",
                            "크게 무겁진 않고, 그렇다고 심심하지도 않은 코스네요.",
                            "오늘의 무난한 메인 코스 정도로 보면 되겠네요.",
                            "자, 이쪽은 안정적으로 방송 이어가기 좋은 선택입니다."),
                        Mood.Neutral);
                    break;
                }

                forcedMotionState = ScreenPresenterMotionState.Bored;
                forcedMotionUntil = Time.unscaledTime + 1.8f;

                QueueCopy(
                    "COURSE CHECK",
                    $"{stage} / COMBAT",
                    PickLine(
                        PresenterLineKey.MapCombatLow,
                        "음... 이번 무대는 조금 쉬어가는 코너네요.",
                        "별이 낮네요. 뭐, 이런 구간도 방송 중간엔 필요하죠.",
                        "자, 이번엔 편하게 볼 수 있는 스테이지입니다.",
                        "음... 긴장감은 잠깐 내려놓으셔도 되겠습니다.",
                        "이번 코스는 잔잔하네요. 다음 큰 무대 전 워밍업 정도로 볼까요?"),
                    Mood.Neutral);
                break;
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private string PickLine(
        PresenterLineKey key,
        params string[] lines)
    {
        if (lines == null || lines.Length == 0)
            return string.Empty;

        if (lines.Length == 1)
        {
            lastLineByEvent[key] = 0;
            return lines[0];
        }

        int previous =
            lastLineByEvent.TryGetValue(key, out int remembered)
                ? remembered
                : -1;

        int pick = Random.Range(0, lines.Length - 1);

        // Pick from N-1 choices, then skip the previous index.
        // This guarantees that the same event never repeats the same line twice in a row.
        if (previous >= 0 && pick >= previous)
            pick++;

        pick = Mathf.Clamp(pick, 0, lines.Length - 1);
        lastLineByEvent[key] = pick;
        return lines[pick];
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static float LinearPingPong(float phase)
    {
        return Mathf.PingPong(phase * 2f, 2f) - 1f;
    }

    private static Text CreateText(
        Transform parent,
        string objectName,
        string value,
        int size,
        FontStyle style,
        TextAnchor alignment,
        Color color)
    {
        GameObject go = new(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Text text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void Place(
        RectTransform rect,
        Vector2 anchor,
        Vector2 pivot,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}

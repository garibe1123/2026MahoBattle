using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Disposable screen presenter prototype.
///
/// - Field presenter / World-Space TV stay untouched.
/// - Screen presenter enters once from the right, black -> white, then remains on screen.
/// - Reward -> Script Selection -> stage preparation does not replay the entrance.
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
        ScriptSelection
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
        ScriptIntro,
        RewardConfirm,
        ScriptConfirmElite,
        ScriptConfirmShop,
        ScriptConfirmEvent,
        ScriptConfirmHigh,
        ScriptConfirmMid,
        ScriptConfirmLow,
        Saw,
        OddWeapon,
        RareItem,
        HotItem,
        SafeItem,
        GenericHover,
        GenericSelected,
        ScriptElite,
        ScriptShop,
        ScriptEvent,
        ScriptCombatHigh,
        ScriptCombatMid,
        ScriptCombatLow,
        BoredReward,
        BoredScript
    }

    private const int PresenterFramePixels = 128;

    private static BattleScreenPresenterPrototypeController instance;

    [Header("Screen Overlay")]
    [SerializeField] private int overlaySortingOrder = 470;
    [Tooltip("Reward/Script Selection 중 작은 Mini PACK(780)보다 위에 말풍선을 표시합니다.")]
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
    [SerializeField] private bool waitForDialogueInput = true;
    [Tooltip("Reward/Map 화면 사회자 Rect 안에서 말풍선 꼬리가 향할 기준점입니다.")]
    [SerializeField] private Vector2 dialogueTailPresenterAnchor = new(0.28f, 0.58f);
    [SerializeField] private Vector2 dialogueTailPivotOffset = new(0f, 0f);
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
    private RectTransform dialogueTailPivotRect;
    private BattleSpeechBubbleFrameFillController dialogueFrameController;
    private BattleSpeechBubbleTailTriangleController dialogueTailGraphic;
    private BattleSpeechBubbleFrameStyle runtimeDialogueFrameStyle;
    private BattleSpeechBubbleTailStyle runtimeDialogueTailStyle;
    private TextMeshProUGUI nameText;
    private TextMeshProUGUI contextText;
    private TextMeshProUGUI dialogueText;
    private TextMeshProUGUI liveText;
    private readonly BattleKoreanTypewriter typewriter = new();
    private TMP_FontAsset checkedDialogueFont, resolvedDialogueFont;
    private string checkedDialogueContent;

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

    /// <summary>
    /// 다른 Screen-Space UI가 현재 호스트 대화창을 피해 배치할 때 사용하는 읽기 전용 정보입니다.
    /// Dialogue 자체의 위치/수명 소유권은 이 Controller에 그대로 남습니다.
    /// </summary>
    public RectTransform DialogueRect => dialogueRect;

    public bool IsDialogueVisible =>
        dialogueRect != null &&
        dialogueGroup != null &&
        dialogueGroup.alpha > 0.01f &&
        dialoguePhase != DialoguePhase.Hidden;

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
        BattleUiAvoidanceResolver.UnregisterZone(
            dialogueRect);

        RestoreCamera();
        ReleaseRuntimeMotionFrames();

        if (overlayCanvas != null)
            Destroy(overlayCanvas.gameObject);

        if (instance == this)
            instance = null;
    }

    private IEnumerator Start()
    {
        WaitForSecondsRealtime retry = new(0.25f);
        while (true)
        {
            ResolveReferences();
            yield return retry;
        }
    }

    private void Update()
    {
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
        UpdateDialogueFonts();
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            AdvanceDialogue();
        UpdateBoredCommentary();

        if (mode == Mode.Reward || mode == Mode.ScriptSelection)
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
                "자아~ 드디어 선택하셨군요! 다음 무대가 기대돼요!",
                "오늘의 픽은 바로 이거랍니다~! 멋진 활약 기대할게요!",
                "어머, 이걸 고르셨어요? 제법 독특한 안목인데요~",
                "자, 결정됐습니다! 과연 어떤 장면을 만들어낼까요?",
                "네에~ 좋은 선택인지는 무대에서 확인해 보시죠!",
                "한참 고민하셨는데요~ 과연 멋진 결과가 나올까요?",
                "짜잔~ 오늘의 선택은 이쪽! 여러분, 주목해 주세요!",
                "선택 완료~! 자아, 다음 무대도 화려하게 부탁드려요!"),
            Mood.Excited);
    }

    public static void NotifyScriptHover(BattleNodeData node, int stars)
    {
        Resolve()?.ShowScript(node, stars);
    }

    // Legacy compatibility for older callers. Script Selection owns this flow now.
    public static void NotifyMapHover(BattleNodeData node, int stars)
    {
        NotifyScriptHover(
            node,
            stars);
    }

    public static void NotifyScriptConfirm(BattleNodeData node, int stars)
    {
        BattleScreenPresenterPrototypeController owner = Resolve();
        if (owner == null || node == null)
            return;

        int clampedStars =
            Mathf.Clamp(
                stars,
                1,
                5);

        string line;
        Mood mood;

        switch (node.type)
        {
            case BattleNodeType.Elite:
                line =
                    owner.PickLine(
                        PresenterLineKey.ScriptConfirmElite,
                        "어머나, 엘리트 코스를 선택하셨네요! 대담하신데요~",
                        "역시 과감하시군요! 화려한 승부가 기대돼요!",
                        "이번엔 엘리트랍니다~! 작은 실수도 조심하셔야 해요!",
                        "아아~ 강한 상대를 고르시다니! 멋지시네요!",
                        "자아, 엘리트 코스 확정! 여러분, 기대해 주세요~!",
                        "위험하지만 보상도 기대되는 선택이군요~!");
                mood = Mood.Excited;
                break;

            case BattleNodeType.Shop:
                line =
                    owner.PickLine(
                        PresenterLineKey.ScriptConfirmShop,
                        "어머, 잠깐 쇼핑 타임인가요~? 좋은 물건 찾아보세요!",
                        "상점으로 결정됐습니다~! 다음 무대를 위한 준비 시간이네요!",
                        "자아~ 지갑은 넉넉하실까요? 멋진 쇼핑 되세요!",
                        "어떤 상품이 기다리고 있을까요~? 천천히 구경해 보시죠!",
                        "쇼핑 타임~! 오늘의 추천 상품도 기대해 주세요!",
                        "어머나, 여기서 지갑을 여시는군요~ 알뜰하게 고르세요!");
                mood = Mood.Neutral;
                break;

            case BattleNodeType.Event:
                line =
                    owner.PickLine(
                        PresenterLineKey.ScriptConfirmEvent,
                        "어머, 특별 이벤트네요~! 어떤 깜짝 소식이 있을까요?",
                        "이쪽을 고르셨군요! 두근두근~ 결과가 궁금해지네요!",
                        "특별 코너 확정입니다~! 선물일까요, 뜻밖의 사건일까요?",
                        "안에는 뭐가 들었을까요? 저도 기대가 되는데요!",
                        "자아~ 비밀의 문이 열립니다! 함께 지켜보시죠!",
                        "이런 신비로운 이벤트라니~ 정말 두근거리네요!");
                mood = Mood.Curious;
                break;

            default:
                if (clampedStars >= 4)
                {
                    line =
                        owner.PickLine(
                            PresenterLineKey.ScriptConfirmHigh,
                            "오호~ 높은 난이도를 선택하셨네요! 용감하시군요!",
                            "자아, 이쪽으로 확정입니다! 여러분, 기대하셔도 좋겠어요!",
                            "어머~ 이번 무대는 아주 화려해지겠는데요?",
                            "과감한 선택이군요! 어떤 활약을 보여주실까요?",
                            "이번엔 높은 난이도예요~! 긴장감 넘치는 무대가 시작돼요!",
                            "눈여겨보시더니 결국 고르셨군요~ 기대가 커지는데요?",
                            "강한 상대에게 도전하시네요! 응원 많이 부탁드려요~");
                    mood = Mood.Excited;
                }
                else if (clampedStars == 3)
                {
                    line =
                        owner.PickLine(
                            PresenterLineKey.ScriptConfirmMid,
                            "이쪽으로 정하셨군요~ 안정감 있는 선택이에요!",
                            "무난하면서도 알찬 코스네요! 기대해 볼까요~?",
                            "별 세 개 코스 확정! 아주 균형 잡힌 선택인데요?",
                            "부담은 조금 덜고, 재미는 놓치지 않으셨네요~",
                            "자아~ 오늘은 차근차근 실력을 보여주실 차례예요!",
                            "중간 난이도군요! 어떤 장면이 나올지 기대돼요~");
                    mood = Mood.Neutral;
                }
                else
                {
                    line =
                        owner.PickLine(
                            PresenterLineKey.ScriptConfirmLow,
                            "어머, 이번엔 여유 있는 코스를 고르셨군요~",
                            "잠깐 쉬어가는 것도 멋진 전략이죠~",
                            "조금 편안한 무대가 되겠네요! 잘 다녀오세요~",
                            "다음 무대를 위한 숨 고르기인가요? 현명하세요!",
                            "이번엔 안전한 쪽이군요~ 차분하게 진행해 보시죠!",
                            "부담 적은 무대예요~ 그래도 방심하시면 안 돼요!",
                            "쉬어가는 무대도 있어야죠~ 편안하게 즐겨 주세요!");
                    mood = Mood.Neutral;
                }
                break;
        }

        owner.QueueCopy(
            "SCRIPT LOCKED",
            $"TAKE {Mathf.Max(1, node.depth + 1):00} / {node.type.ToString().ToUpperInvariant()}",
            line,
            mood);
    }

    // Legacy compatibility for older callers. Script Selection owns this flow now.
    public static void NotifyMapConfirm(BattleNodeData node, int stars)
    {
        NotifyScriptConfirm(
            node,
            stars);
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
                    "자아~ 기다리셨죠? 두근두근 보상 시간입니다!",
                    "오늘도 수고 많으셨어요! 이번 선물은 뭘까요~?",
                    "어머나~ 멋진 보상들이 나왔네요! 함께 살펴보시죠!",
                    "짜잔~ 오늘의 상품들을 소개합니다! 주목해 주세요!",
                    "자아~ 어느 물건이 마음을 사로잡을까요?",
                    "기다리고 기다리던 보상 코너입니다! 박수 부탁드려요~!",
                    "어머, 이번엔 고르기 어렵겠는데요~?",
                    "장비를 새로 단장할 기회예요~ 마음에 드는 걸 골라보세요!"),
                Mood.Neutral);
            return;
        }

        if (state == BattleRunState.SelectingNode)
        {
            mode = Mode.ScriptSelection;
            EnsurePresenterSession();

            QueueCopy(
                "SCRIPT DESK",
                "NEXT TAKE",
                PickLine(
                    PresenterLineKey.ScriptIntro,
                    "자아~ 다음 무대를 소개합니다! 모두 주목해 주세요~!",
                    "오늘의 다음 스테이지 후보는 바로 이쪽들이랍니다~!",
                    "새로운 선택지가 등장했어요! 과연 어느 쪽일까요?",
                    "이제 다음 코스를 정하실 시간이에요~!",
                    "어머나, 길이 여러 갈래네요! 흥미진진하겠어요~",
                    "안전한 무대도, 화려한 무대도 준비돼 있답니다~",
                    "네에~ 이번엔 제법 다양한 선택지가 기다리고 있네요!",
                    "어디로 가시겠어요? 천천히 살펴보세요~"),
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
        BattleSpeechBubbleFrameStyle frameStyle =
            presentation != null
                ? presentation.SelectionSpeechBubbleFrameStyle
                : null;

        Color bubbleFill =
            frameStyle != null
                ? frameStyle.fillColor
                : BattleSpeechBubbleFrameStyle.DefaultFillColor;

        Color bubbleInk =
            frameStyle != null
                ? frameStyle.outlineColor
                : new Color(0.012f, 0.012f, 0.018f, 0.99f);

        Color bubbleText = new(0.035f, 0.035f, 0.045f, 1f);
        Color bubbleMuted = new(0.24f, 0.25f, 0.29f, 1f);

        GameObject root = new("PresenterDialogueBubble", typeof(RectTransform));
        root.transform.SetParent(overlayRoot, false);

        dialogueRect = root.GetComponent<RectTransform>();
        dialogueRect.anchorMin = dialogueRect.anchorMax = new Vector2(0.5f, 0f);
        dialogueRect.pivot = new Vector2(0.5f, 0f);
        // 선택씬 말풍선의 디자인 크기는 고정입니다.
        // 실제 화면 비율/해상도 대응은 CanvasScaler가 담당합니다.
        dialogueRect.sizeDelta = dialogueSize;

        dialogueRect.anchoredPosition =
            dialogueVisibleOffset - Vector2.up * dialogueHiddenOffsetY;

        dialogueRect.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                frameStyle != null
                    ? frameStyle.rotation
                    : dialogueBubbleRotation);

        dialogueRenderCanvas = root.AddComponent<Canvas>();
        root.AddComponent<GraphicRaycaster>();
        dialogueRenderCanvas.overrideSorting = true;
        dialogueRenderCanvas.sortingOrder = dialogueMiniPackSortingOrder;

        dialogueGroup = root.AddComponent<CanvasGroup>();
        dialogueGroup.alpha = 0f;
        dialogueGroup.interactable = true;
        dialogueGroup.blocksRaycasts = true;

        BattleUiAvoidanceResolver.RegisterZone(
            dialogueRect,
            90,
            28f,
            () => IsDialogueVisible);

        BattleSpeechBubbleFrameStyle activeFrameStyle =
            frameStyle ??
            BattleSpeechBubbleFrameStyle.CreateSelectionDefault();

        // OUTLINE + INNER를 한 개의 런타임 Sprite로 렌더링합니다.
        // 빨강/초록 4꼭짓점 Preview와 Play가 동일한 Builder를 사용합니다.
        GameObject frame = new("BubbleFrame", typeof(RectTransform));
        frame.transform.SetParent(dialogueRect, false);

        Image frameImage = frame.AddComponent<Image>();
        frameImage.raycastTarget = false;

        dialogueFrameController =
            frame.AddComponent<BattleSpeechBubbleFrameFillController>();

        dialogueFrameController.Configure(
            frameImage,
            activeFrameStyle,
            dialogueSize,
            presentation != null
                ? presentation.SpeechBubbleStrokeMaterial
                : null);

        dialogueTailGraphic =
            dialogueRect.gameObject.AddComponent<BattleSpeechBubbleTailTriangleController>();
        dialogueTailGraphic.Configure(
            dialogueRect,
            dialogueTailPivotRect,
            activeFrameStyle.fillColor,
            presentation != null
                ? presentation.SelectionSpeechBubbleTailStyle
                : null,
            presentation != null
                ? presentation.SpeechBubbleStrokeMaterial
                : null);

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

        // 우측 상단 상황/아이템 메타 텍스트는 사용하지 않습니다.
        // 대화창은 SHOW HOST 라벨과 본문에만 시선을 집중시킵니다.
        contextText = null;

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
        dialogueText.textWrappingMode = TextWrappingModes.Normal;
        dialogueText.overflowMode = TextOverflowModes.Truncate;
        dialogueText.lineSpacing = 1.04f;

        liveText = CreateText(
            dialogueRect,
            "Live",
            "ON LIVE",
            10,
            FontStyle.Bold,
            TextAnchor.LowerRight,
            hotAccent);
        liveText.raycastTarget = true;
        Button advanceButton = dialogueRect.gameObject.AddComponent<Button>();
        advanceButton.targetGraphic = liveText;
        advanceButton.transition = Selectable.Transition.None;
        Navigation navigation = advanceButton.navigation;
        navigation.mode = Navigation.Mode.None;
        advanceButton.navigation = navigation;
        advanceButton.onClick.AddListener(AdvanceDialogue);
        Place(
            liveText.rectTransform,
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(-20f, 8f),
            new Vector2(120f, 20f));

        // Frame은 최후방, TailShape는 그 바로 앞에 유지합니다.
        // 기존 SetSiblingIndex(1)는 TailShape를 0번으로 밀어 Frame이 Tail 위에
        // 렌더되는 역전이 생겼습니다.
        frame.transform.SetAsFirstSibling();

        dialogueRect.localScale =
            Vector3.one * dialogueBubbleStartScale;
        BattleUiStyleController.RegisterRoot(dialogueRect);
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
        presentation?.RegisterUiMaterialTarget(presenterImage, BattleUiMaterialController.UiChannel.Presenter);
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

        if (waitForDialogueInput && (dialoguePhase == DialoguePhase.Opening ||
            dialoguePhase == DialoguePhase.Typing || dialoguePhase == DialoguePhase.Idle)) return;

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
                "어머, 아직 고민 중이신가요~? 천천히 고르세요!",
                "잠깐 자리를 비우셨을까요? 저희는 기다리고 있답니다~",
                "선택 하나하나가 중요하죠~ 신중하신 모습도 멋져요!",
                "어떤 물건이 마음에 드실까요? 정말 궁금하네요~",
                "아직 고민 중이시군요~ 결정하시기 어렵죠?",
                "저라면 어느 걸 고를까요~? 음, 비밀이에요!",
                "세 가지가 다 매력적이네요~ 하나만 고르기 아쉬우시죠?",
                "시청자 여러분도 궁금하시죠? 어떤 물건을 고르실까요~?")
            : PickLine(
                PresenterLineKey.BoredScript,
                "아직 고르는 중이시군요~ 서두르지 않으셔도 돼요!",
                "아아~ 선택의 순간은 언제나 긴장되죠!",
                "오늘은 길 하나도 신중하게 고르시네요~",
                "여유 있게 살펴보세요~ 저희도 함께 기다릴게요!",
                "어머나, 어느 쪽도 쉽게 결정하기 어렵겠어요~",
                "이 길도 좋고, 저 길도 좋고~ 어떤 선택이 나올까요?",
                "잠깐 멈추셨네요! 혹시 마음은 정하셨나요~?",
                "방송은 계속됩니다~! 선택하시면 다음 무대로 모실게요!");

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

    private void RefreshDialogueVisualVariation()
    {
        if (dialogueRect == null ||
            dialogueFrameController == null ||
            dialogueTailGraphic == null)
        {
            return;
        }

        BattleSpeechBubbleFrameStyle baseFrame =
            presentation != null
                ? presentation.SelectionSpeechBubbleFrameStyle
                : BattleSpeechBubbleFrameStyle.CreateSelectionDefault();

        BattleSpeechBubbleTailStyle baseTail =
            presentation != null
                ? presentation.SelectionSpeechBubbleTailStyle
                : new BattleSpeechBubbleTailStyle();

        BattleSpeechBubbleRuntimeVariationSettings variation =
            presentation != null
                ? presentation.SelectionSpeechBubbleVariation
                : null;

        int seed =
            BattleSpeechBubbleVariationRandom.NextSeed(
                GetInstanceID());

        runtimeDialogueFrameStyle =
            baseFrame.CreateRuntimeVariant(
                variation,
                seed ^ 0x1735A91);

        runtimeDialogueTailStyle =
            baseTail.CreateRuntimeVariant(
                variation,
                seed ^ 0x51B7C2D);

        dialogueRect.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                runtimeDialogueFrameStyle.rotation);

        dialogueFrameController.Configure(
            null,
            runtimeDialogueFrameStyle,
            dialogueSize,
            presentation != null
                ? presentation.SpeechBubbleStrokeMaterial
                : null);

        dialogueTailGraphic.Configure(
            dialogueRect,
            dialogueTailPivotRect,
            runtimeDialogueFrameStyle.fillColor,
            runtimeDialogueTailStyle,
            presentation != null
                ? presentation.SpeechBubbleStrokeMaterial
                : null);
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

        // 같은 기본 디자인을 유지하되, 이 대사가 열리는 순간 한 번만
        // Frame Stroke/Corner와 Tail 중간 Pivot을 미세하게 변형합니다.
        RefreshDialogueVisualVariation();

        ApplyDialogueMetadata();

        dialogueText.text = string.Empty;
        typewriter.Reset(activeComment);
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

        dialogueGroup.blocksRaycasts = dialoguePhase == DialoguePhase.Typing || dialoguePhase == DialoguePhase.Idle;
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
                    typewriter.StepCount);

                if (targetCharacters != visibleCharacters)
                {
                    visibleCharacters = targetCharacters;
                    dialogueText.text = typewriter.Render(visibleCharacters);
                }

                if (visibleCharacters >= typewriter.StepCount)
                {
                    dialogueText.text = activeComment;
                    dialoguePhase = DialoguePhase.Idle;
                    dialoguePhaseTime = 0f;
                    ShowDialogueInputHint();
                }
                break;
            }

            case DialoguePhase.Idle:
                ApplyDialogueOpenValue(1f);
                dialoguePhaseTime += dt;

                if (!waitForDialogueInput && (hasPendingCopy ||
                    dialoguePhaseTime >= Mathf.Max(0.1f, dialogueIdleDuration)))
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
        dialogueRect.anchoredPosition = dialogueVisibleOffset;

        // Pop Scale은 기존 선택씬 연출값으로 고정합니다.
        // 화면 비율 스케일링은 CanvasScaler가 담당합니다.
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
    }

    private void AdvanceDialogue()
    {
        if (dialogueText == null || mode == Mode.None) return;
        if (dialoguePhase == DialoguePhase.Typing)
        {
            dialogueText.text = activeComment;
            visibleCharacters = typewriter.StepCount;
            dialoguePhase = DialoguePhase.Idle;
            dialoguePhaseTime = 0f;
            ShowDialogueInputHint();
        }
        else if (dialoguePhase == DialoguePhase.Idle)
        {
            dialoguePhase = DialoguePhase.Closing;
            dialoguePhaseTime = 0f;
            nextBoredLineAt = Time.unscaledTime + Mathf.Max(5f, boredLineRepeatSeconds);
        }
    }

    private void ShowDialogueInputHint()
    {
        if (waitForDialogueInput && liveText != null) liveText.text = "SPACE / CLICK";
    }

    private void UpdateDialogueFonts()
    {
        if (dialoguePhase == DialoguePhase.Hidden) return;
        TMP_FontAsset font = presentation != null && presentation.UiStyleSettings.fontAsset != null
            ? presentation.UiStyleSettings.fontAsset : presentation != null
                ? BattleUiThemeFonts.LoadTmp(presentation.UiStyle) : null;
        if (font == null) font = BattleUiThemeFonts.LoadTmp(BattleUiStyle.VectorStyleUi);
        if (font == null) font = TMP_Settings.defaultFontAsset;
        if (font == null) return;
        if (nameText != null && nameText.font != font) nameText.font = font;
        if (liveText != null && liveText.font != font) liveText.font = font;
        if (dialogueText != null)
        {
            if (checkedDialogueFont != font || checkedDialogueContent != dialogueText.text)
            {
                checkedDialogueFont = font;
                checkedDialogueContent = dialogueText.text;
                resolvedDialogueFont = font;
                if (!font.HasCharacters(checkedDialogueContent, out uint[] _, true, true))
                {
                    resolvedDialogueFont = BattleUiThemeFonts.LoadTmp(BattleUiStyle.VectorStyleUi);
                    if (resolvedDialogueFont == null) resolvedDialogueFont = TMP_Settings.defaultFontAsset;
                }
            }
            if (resolvedDialogueFont != null && dialogueText.font != resolvedDialogueFont)
                dialogueText.font = resolvedDialogueFont;
        }
    }

    private void ApplyDialogueMetadata()
    {
        if (nameText != null)
            nameText.text = "SHOW HOST";

        // Context metadata display intentionally removed.

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
                    "어머나, 톱이 등장했어요! 제법 강렬한 상품인데요~?",
                    "이것 좀 보세요~! 아주 인상적인 톱이군요!",
                    "손에 쥐면 시선이 집중되겠는데요~?",
                    "다음 무대에서 어떤 모습일지 정말 기대돼요!",
                    "보기만 해도 긴장감이 느껴지는 무기예요~!",
                    "이런 독특한 무기는 화면에서도 단연 돋보이죠!",
                    "어머, 디자인이 아주 강렬하네요~ 시선이 확 가요!",
                    "과연 어떤 활약을 보여줄까요? 기대해 주세요~!"),
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
                    "어머~ 정말 특이한 물건이네요! 어떤 용도일까요?",
                    "이런 깜짝 상품도 준비돼 있답니다~!",
                    "와아, 예상 밖의 선택이군요! 재미있겠어요!",
                    "평범한 무기와는 조금 다르네요~",
                    "오늘 나온 물건 중에서도 꽤 특별해 보여요!",
                    "자아~ 독특한 상품일수록 궁금증도 커지죠?",
                    "어떤 효과가 있을까요? 무대에서 확인해 보시죠~",
                    "평범함을 거부하는 멋진 선택이에요~!"),
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
                    "어머나! 잠깐만요~ 이건 정말 특별한 물건인데요?",
                    "와아~ 이런 귀한 상품이 등장하다니, 놀라워요!",
                    "이건 누구라도 고민할 만한 멋진 선택지예요!",
                    "보기 드문 물건이군요! 놓치기 아까울지도 몰라요~",
                    "반짝반짝~ 오늘의 인기 상품 후보랍니다!",
                    "자아, 화면을 크게 잡아 주세요! 특별한 상품이에요~",
                    "정말 눈길을 사로잡는 물건이네요!",
                    "어머, 이걸 놓치신다면 조금 아쉬우실지도~?"),
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
                    "어머나, 아주 화끈한 물건이 나왔어요~!",
                    "이런 장비라면 무대가 한층 더 화려해지겠죠?",
                    "와아~ 전투에서 어떤 효과가 날지 궁금하네요!",
                    "시원한 한 방을 원하신다면 이쪽은 어떠세요~?",
                    "화려한 연출을 기대해도 좋겠는데요~!",
                    "자아~ 다음 무대를 뜨겁게 달굴 상품이랍니다!",
                    "카메라가 바빠지겠어요~ 놓치지 마세요!",
                    "효과가 아주 강렬하겠네요! 안전에도 주의하세요~"),
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
                    "어머, 든든한 장비가 등장했네요~!",
                    "화려함보다 안정감! 이런 선택도 중요하죠~",
                    "자아~ 안전을 챙기는 것도 멋진 전략이에요!",
                    "보기에는 차분해도 실속이 있겠는데요?",
                    "오래 함께할 수 있는 물건이면 좋겠어요~",
                    "눈에 확 띄진 않아도 든든한 조력자가 되겠죠?",
                    "안정적인 선택을 원하신다면 주목해 주세요~",
                    "나중에 진가를 발휘할지도 몰라요~ 기대해 보세요!"),
                Mood.Neutral);
            return;
        }

        QueueCopy(
            selected ? "PICKED" : "ITEM CHECK",
            $"{equipment.rarity.ToString().ToUpperInvariant()} / {displayName}",
            selected
                ? PickLine(
                    PresenterLineKey.GenericSelected,
                    "어머~ 이쪽에서 멈추셨군요! 마음에 드시나요?",
                    "이 상품이 눈길을 사로잡았나요~?",
                    "자아, 후보 하나가 정해졌어요! 결과가 궁금하네요~",
                    "오늘의 관심 상품이군요~! 아주 좋습니다!",
                    "어머, 이쪽에 관심이 있으시군요?",
                    "손길이 멈췄어요~ 어떤 매력이 있었을까요?",
                    "네에~ 이쪽 상품을 찜하셨네요! 아직 확정은 아니에요~",
                    "오호~ 생각보다 멋진 안목이신데요?")
                : PickLine(
                    PresenterLineKey.GenericHover,
                    "자아, 이 상품은 어떤가요~? 차근차근 살펴보세요!",
                    "무난하면서도 믿음직한 물건이군요!",
                    "이런 장비는 조합에 따라 매력이 달라지죠~",
                    "기본에 충실한 모습이네요~ 안정감 있어요!",
                    "어머, 나쁘지 않은 상품이군요~ 후보에 넣어두세요!",
                    "직접 써보면 새로운 매력을 발견할지도 몰라요!",
                    "겉보기엔 평범하지만, 조합에 따라 달라진답니다~",
                    "의외의 활약을 보여줄 수도 있겠는데요?",
                    "설명만으론 아쉽죠~ 실제 무대에서 확인해 보세요!"),
            selected ? Mood.Curious : Mood.Neutral);
    }

    private void ShowScript(BattleNodeData node, int stars)
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
                        PresenterLineKey.ScriptElite,
                        "어머나, 엘리트 스테이지예요! 긴장되네요~!",
                        "제법 만만치 않은 코스인데요~?",
                        "조금 거친 무대가 될 수도 있어요~ 준비되셨나요?",
                        "이번엔 엘리트군요! 멋진 승부를 기대해 볼까요?",
                        "이런 코스라면 긴장감도 한층 올라가겠어요~!",
                        "자아~ 도전하실 분? 쉽지 않은 무대랍니다!",
                        "어머, 만만히 보시면 곤란할 거예요~!",
                        "어려운 만큼 보상도 기대해 볼 만하겠죠?"),
                    Mood.Excited);
                break;

            case BattleNodeType.Shop:
                QueueCopy(
                    "SHOPPING BREAK",
                    $"{stage} / SHOP",
                    PickLine(
                        PresenterLineKey.ScriptShop,
                        "어머, 상점이에요~! 잠시 쇼핑을 즐기실까요?",
                        "새로운 장비가 기다리고 있을지도 몰라요!",
                        "전투 사이의 달콤한 쇼핑 타임이군요~",
                        "자아~ 오늘은 어떤 상품이 기다리고 있을까요?",
                        "쇼핑 코너도 방송의 묘미죠~!",
                        "주머니 사정도 한번 확인해 보세요~!",
                        "서두르지 말고 천천히 구경하세요~",
                        "꼼꼼히 고르시면 멋진 상품을 만나실지도 몰라요!"),
                    Mood.Neutral);
                break;

            case BattleNodeType.Event:
                QueueCopy(
                    "SPECIAL SEGMENT",
                    $"{stage} / EVENT",
                    PickLine(
                        PresenterLineKey.ScriptEvent,
                        "어머나~ 특별 이벤트가 기다리고 있네요!",
                        "안에 뭐가 있을까요? 저도 무척 궁금해요~",
                        "전투 외의 특별 코너일까요? 기대돼요!",
                        "결과를 알 수 없으니 더 두근거리네요~",
                        "이번엔 분위기를 확 바꿔볼까요?",
                        "비밀에 싸인 특별 코너예요~ 함께 확인해 보시죠!",
                        "어떤 깜짝 선물이 기다릴까요~?",
                        "행운일까요, 뜻밖의 사건일까요? 두근두근~"),
                    Mood.Curious);
                break;

            default:
                if (clampedStars >= 4)
                {
                    QueueCopy(
                        "COURSE CHECK",
                        $"{stage} / COMBAT",
                        PickLine(
                            PresenterLineKey.ScriptCombatHigh,
                            "와아~ 별 네 개 이상! 기대되는 무대인데요?",
                            "이번 무대는 상당히 화려할 것 같아요~",
                            "어머, 쉽지 않은 코스예요~ 자신 있으신가요?",
                            "긴장감이 점점 올라가네요!",
                            "자아~ 다음 무대의 주인공은 어떤 길을 고르실까요?",
                            "여기서부터 분위기가 달라질 것 같아요~!",
                            "높은 난이도랍니다~ 신중하게 선택해 주세요!",
                            "성공한다면 멋진 하이라이트가 되겠죠~?"),
                        Mood.Excited);
                    break;
                }

                if (clampedStars == 3)
                {
                    QueueCopy(
                        "COURSE CHECK",
                        $"{stage} / COMBAT",
                        PickLine(
                            PresenterLineKey.ScriptCombatMid,
                            "별 세 개네요~ 아주 균형 잡힌 코스예요!",
                            "부담은 적당히, 재미도 적당히~ 어떠세요?",
                            "안정감과 긴장감이 함께하는 무대군요!",
                            "오늘은 무난하게 진행해 보실까요~?",
                            "차근차근 도전하고 싶으시다면 이쪽이에요!",
                            "자아~ 즐기기 좋은 스테이지가 기다립니다!",
                            "어머, 이 정도면 도전해 볼 만하겠는데요?",
                            "무난한 코스에도 멋진 장면은 숨어 있답니다~!"),
                        Mood.Neutral);
                    break;
                }

                forcedMotionState = ScreenPresenterMotionState.Bored;
                forcedMotionUntil = Time.unscaledTime + 1.8f;

                QueueCopy(
                    "COURSE CHECK",
                    $"{stage} / COMBAT",
                    PickLine(
                        PresenterLineKey.ScriptCombatLow,
                        "어머, 조금 편안한 코스가 나왔어요~",
                        "별이 낮은 코스네요~ 잠깐 숨을 고르셔도 좋겠어요.",
                        "이번에는 여유롭게 진행해 볼까요?",
                        "긴장은 잠깐 내려놓으셔도 되겠네요~",
                        "다음 무대에 앞선 준비 운동 같은 느낌이에요!",
                        "차분하게 즐기기 좋은 코스랍니다~",
                        "무난해 보이지만 그래도 방심은 금물이에요~",
                        "여유로운 무대도 좋아요! 편안하게 즐겨 주세요~"),
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

    private static TextMeshProUGUI CreateText(
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

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.font = BattleUiThemeFonts.LoadTmp(BattleUiStyle.VectorStyleUi) ?? TMP_Settings.defaultFontAsset;
        text.richText = false;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style == FontStyle.Bold ? FontStyles.Bold : FontStyles.Normal;
        text.alignment = alignment switch
        {
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
            _ => TextAlignmentOptions.Center
        };
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

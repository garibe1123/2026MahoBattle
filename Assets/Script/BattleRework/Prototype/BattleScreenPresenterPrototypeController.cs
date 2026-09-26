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
        MapConfirmElite,
        MapConfirmShop,
        MapConfirmEvent,
        MapConfirmHigh,
        MapConfirmMid,
        MapConfirmLow,
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
                "오, 결국 이쪽으로 가네요. 이건 다음 판에서 바로 티 나겠는데요.",
                "이걸로 정했네요. 좋아요, 그럼 다음 무대에서 한번 보죠.",
                "아, 이쪽이구나. 음... 나쁘지 않은데?",
                "결국 이걸 집네요. 자, 이 선택이 어떻게 굴러갈지 한번 봐야겠네요!",
                "좋네요. 이건 실전에서 어떻게 나올지 좀 궁금하네요.",
                "어, 잠깐 고민하더니 여기로 왔네요. 이 정도면 이유가 있겠죠.",
                "자, 픽 나왔습니다. 화려한 선택은 아닌데 은근 오래 갈 수도 있어요.",
                "오케이, 이걸 들고 갑니다. 다음 전투는 이 장비 기준으로 보면 되겠네요."),
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
                        PresenterLineKey.MapConfirmElite,
                        "오, 엘리트로 가네요. 좋아요, 이건 좀 집중해서 봐야겠네요!",
                        "결국 여기 들어갑니다. 편한 길은 아니고... 대신 볼 건 많겠네요.",
                        "엘리트 확정이네요. 자, 여기부터는 한 번 삐끗하면 꽤 아픕니다.",
                        "어, 이쪽을 고르네요. 이번엔 확실히 세게 가는데?",
                        "좋습니다, 엘리트 코스. 이건 결과가 어떻게 나오든 화면은 나오겠네요.",
                        "여기군요. 음... 위험한 대신 보상까지 생각하면 이해는 갑니다.");
                mood = Mood.Excited;
                break;

            case BattleNodeType.Shop:
                line =
                    owner.PickLine(
                        PresenterLineKey.MapConfirmShop,
                        "오, 상점으로 가네요. 그럼 잠깐 장비부터 정리하고 가죠.",
                        "여기로 정했네요. 좋아요, 전투 전에 한 번 숨 돌릴 수 있겠어요.",
                        "상점 확정. 음... 돈 남아 있으면 여기서 좀 쓰겠네요.",
                        "이쪽이군요. 급하게 갈 건 없고, 뭐 살지 천천히 보면 되겠네요.",
                        "좋습니다, 쇼핑 타임. 다음 전투 전에 세팅 한번 만져보죠.",
                        "어, 상점 가네요. 괜히 들어갔다가 지갑만 비우는 건 아니겠죠.");
                mood = Mood.Neutral;
                break;

            case BattleNodeType.Event:
                line =
                    owner.PickLine(
                        PresenterLineKey.MapConfirmEvent,
                        "오, 이벤트로 가네요. 이건 저도 뭐가 나올지 모르겠네요.",
                        "결국 이쪽이군요. 좋아요, 이런 건 직접 열어봐야 알죠.",
                        "이벤트 확정. 음... 좋은 게 나올지, 이상한 게 나올지 한번 보죠.",
                        "어, 여기 들어가네요. 정보가 없어서 오히려 좀 궁금한데?",
                        "이쪽이군요. 자, 예상은 그만하고 그냥 열어보죠.",
                        "좋습니다, 이벤트 코스. 이런 건 괜히 눌러보고 싶게 만들어놨죠.");
                mood = Mood.Curious;
                break;

            default:
                if (clampedStars >= 4)
                {
                    line =
                        owner.PickLine(
                            PresenterLineKey.MapConfirmHigh,
                            "오, 결국 이쪽으로 가네요. 좋아요, 이번 판은 좀 볼 만하겠는데요.",
                            "여기로 정했네요. 자, 이제 슬슬 긴장 좀 해야겠네요!",
                            "아, 이쪽이구나. 다음 판 그림 제대로 나오겠는데?",
                            "어, 이 길을 택하네요. 생각보다 과감하게 가는데?",
                            "좋습니다, 높은 쪽으로 갑니다. 말보다 직접 보는 게 빠르겠네요.",
                            "이쪽이네요. 음... 아까부터 눈은 갔는데, 진짜 들어가네.",
                            "결국 센 쪽을 고르네요. 편하게 갈 생각은 없나 봅니다.");
                    mood = Mood.Excited;
                }
                else if (clampedStars == 3)
                {
                    line =
                        owner.PickLine(
                            PresenterLineKey.MapConfirmMid,
                            "이쪽으로 정했네요. 딱 무난하게 이어가기 좋은 선택이네요.",
                            "오, 여기군요. 너무 세지도 않고 너무 심심하지도 않고.",
                            "별 세 개 쪽으로 갑니다. 음... 깔끔하게 한 판 보기 좋겠네요.",
                            "결국 이쪽이네요. 크게 무리하진 않고, 그렇다고 쉬어가진 않고.",
                            "좋아요, 여기로 갑니다. 다음 판은 딱 중간 템포겠네요.",
                            "이쪽 선택이군요. 뭐, 지금 흐름엔 이 정도가 잘 맞아 보이네요.");
                    mood = Mood.Neutral;
                }
                else
                {
                    line =
                        owner.PickLine(
                            PresenterLineKey.MapConfirmLow,
                            "아, 이쪽으로 가네요. 이번 판은 좀 편하게 넘기겠는데요.",
                            "여기로 정했네요. 뭐, 굳이 매번 무리할 필요는 없죠.",
                            "오케이, 이쪽이면 잠깐 숨 돌릴 수 있겠네요.",
                            "이쪽 선택이군요. 다음 큰 판 전에 정리 한번 하고 가는 느낌이네요.",
                            "음, 안전한 쪽으로 가네요. 깔끔하게 넘겨보죠.",
                            "여기군요. 크게 힘줄 구간은 아니고, 바로 이어가면 되겠네요.",
                            "좋아요, 이번엔 좀 잔잔하게 갑니다. 이런 판도 있어야죠.");
                    mood = Mood.Neutral;
                }
                break;
        }

        owner.QueueCopy(
            "NEXT COURSE",
            $"ROUTE LOCKED / STAGE {Mathf.Max(1, node.depth + 1):00}",
            line,
            mood);
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
                    "음... 보상 나왔네요. 뭐가 있나 한번 볼까요?",
                    "전투 끝났고, 화면 바로 넘어왔습니다. 자... 뭐가 떴나 보죠.",
                    "오, 이번엔 이런 식으로 나왔네요. 뭘 집으려나.",
                    "하나씩 보죠. 급하게 고를 건 아니니까.",
                    "이번 후보는 이렇네요. 뭐가 눈에 들어오려나.",
                    "자, 보상 시간이네요. 이상하게 전투보다 이때 고민이 더 길어질 때가 있거든요.",
                    "어... 잠깐만. 이번 건 첫인상부터 좀 갈리겠는데?",
                    "세팅 바꿀 타이밍이긴 하죠. 여기서 뭐 하나 제대로 건지면 다음 판이 편해집니다."),
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
                    "오, 다음 길 나왔네요. 이번엔 좀 재밌어 보이는데?",
                    "다음 무대가 이쪽들이구나. 음... 어디로 갈까요?",
                    "선택지 나왔네요. 이번엔 좀 고민되겠는데요?",
                    "다음 코스가 이렇네요. 어디를 고르려나.",
                    "오, 길 갈렸네요. 이럴 때 성향 좀 나오죠.",
                    "자, 다음 무대 후보 들어왔습니다. 안전하게 갈지, 그림 보러 갈지.",
                    "어... 이번 건 그냥 딱 봐도 취향 갈리겠네요.",
                    "이 정도면 아무 데나 찍기는 아깝죠. 잠깐 보고 가는 게 맞겠네요."),
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
                : new Color(0.97f, 0.97f, 0.94f, 1f);

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
        dialogueRenderCanvas.overrideSorting = true;
        dialogueRenderCanvas.sortingOrder = dialogueMiniPackSortingOrder;

        dialogueGroup = root.AddComponent<CanvasGroup>();
        dialogueGroup.alpha = 0f;
        dialogueGroup.interactable = false;
        dialogueGroup.blocksRaycasts = false;

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
            dialogueSize);

        dialogueTailGraphic =
            dialogueRect.gameObject.AddComponent<BattleSpeechBubbleTailTriangleController>();
        dialogueTailGraphic.Configure(
            dialogueRect,
            dialogueTailPivotRect,
            activeFrameStyle.fillColor,
            presentation != null
                ? presentation.SelectionSpeechBubbleTailStyle
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

        // Frame은 최후방, TailShape는 그 바로 앞에 유지합니다.
        // 기존 SetSiblingIndex(1)는 TailShape를 0번으로 밀어 Frame이 Tail 위에
        // 렌더되는 역전이 생겼습니다.
        frame.transform.SetAsFirstSibling();

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
                "음... 아직 보는 중이네요. 뭐, 천천히 보죠.",
                "어디 갔나... 잠깐 자리 비웠나?",
                "생각보다 오래 보네요. 뭔가 걸리는 게 있나.",
                "이 정도면 하나쯤 눈에 들어올 법도 한데... 아직인가.",
                "아직 보고 있네요. 음... 고민되나 보네.",
                "저였으면 아까 하나 찍었을 것 같은데... 뭐, 제가 고르는 건 아니니까.",
                "이게 막상 세 개 놓고 보면 어렵죠. 하나 버리는 게 더 어렵거든.",
                "채팅 있었으면 벌써 서로 다른 거 고르라고 난리 났겠다.")
            : PickLine(
                PresenterLineKey.BoredMap,
                "음... 아직도 고민 중이네요. 뭐, 길이 좀 애매하긴 하죠.",
                "어디 갔나... 잠깐 자리 비웠나?",
                "길 하나 고르는 게 은근 오래 걸리네요.",
                "뭐, 급할 건 없죠. 조금 더 보죠.",
                "아직인가... 음, 좀 고민되긴 하겠네요.",
                "이쪽도 이유 있고 저쪽도 이유 있고... 딱 제일 귀찮은 선택이네요.",
                "저기서 멈춰 있는 거 보니까 마음은 거의 정한 것 같은데?",
                "자, 방송은 계속 갑니다. 결정만 나오면 바로 다음 무대로 넘길게요.");

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
            dialogueSize);

        dialogueTailGraphic.Configure(
            dialogueRect,
            dialogueTailPivotRect,
            runtimeDialogueFrameStyle.fillColor,
            runtimeDialogueTailStyle);
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
                    "오... 톱이네. 이걸 진짜 들고 가려고?",
                    "잠깐, 이거 톱 맞죠? 음... 존재감 하나는 확실하네.",
                    "이건 그냥 지나가기가 어렵네요. 너무 눈에 띄는데.",
                    "이걸 집으면 다음 전투는 그림 하나는 나오겠네요.",
                    "정상적인 무기 느낌은 아닌데... 그래서 더 끌리나?",
                    "자, 이런 물건 나오면 방송하는 입장에선 반갑죠. 뭘 하든 화면은 나오니까.",
                    "아니, 성능 이전에 생김새가 너무 셉니다. 한 번 잡으면 계속 보이겠는데.",
                    "어... 저는 이거 그냥 못 지나칠 것 같아요. 합리적인 선택인지는 모르겠고."),
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
                    "음... 이건 뭐라고 설명해야 하지. 좀 특이하네요.",
                    "이런 게 하나쯤 있으면 확실히 재밌긴 하죠.",
                    "오, 이건 반응이 좀 궁금한데요.",
                    "정석이랑은 거리가 좀 있는데... 눈은 가네요.",
                    "오늘 나온 것 중엔 제일 이상한 축인데... 그래서 후보인가.",
                    "자, 설명이 길어지는 장비는 대체로 둘 중 하나예요. 재밌거나, 골치 아프거나.",
                    "이런 건 직접 써보기 전까지 감이 안 와요. 그래서 더 보고 싶긴 하고.",
                    "어, 딱 봐도 평범하게 굴러가진 않겠네요. 그건 확실하네요."),
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
                    "오, 잠깐. 이건 좀 다른데?",
                    "음... 이건 급이 좀 다르네요.",
                    "이런 게 하나 떠주면 확실히 고민되죠.",
                    "희귀한 게 떴네요. 이건 그냥 넘기기 좀 아깝겠는데.",
                    "오... 이건 오늘 나온 것 중에 확실히 눈에 들어오네요.",
                    "자, 이건 화면 한 번 크게 잡아도 되겠네요. 값어치는 있어 보여요.",
                    "어, 이 정도면 선택지 하나가 아니라 거의 사건인데?",
                    "이건 지나쳤다가 다음 판 내내 생각날 수 있어요. 그런 느낌인데."),
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
                    "오, 이건 다음 무대 좀 시끄럽겠네요.",
                    "설명 안 봐도 대충 어떤 그림 나올지 보이네요.",
                    "이런 건 전투 들어가면 바로 티 나겠는데요.",
                    "다음 전투 좀 화끈하게 가려면 이쪽이겠네요.",
                    "음... 이건 좀 끌리는데. 터지는 맛은 있겠네요.",
                    "자, 조용히 풀 생각은 없어 보이네요. 이런 장비는 들어가는 순간 분위기 확 바뀌죠.",
                    "이건 성능도 성능인데 화면 맛이 확실하겠어요.",
                    "어... 너무 과하면 본인도 같이 정신없을 텐데. 그래도 재밌긴 하겠다."),
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
                    "음, 이건 좀 안정적인 쪽이네요. 화려하진 않고.",
                    "얌전하긴 한데... 막상 실전에선 이런 게 든든하죠.",
                    "생존 쪽으로 챙기려나 보네요.",
                    "화면은 조용해도 효과는 확실하겠네요.",
                    "조금 심심해 보여도 이런 게 오래 가긴 하죠.",
                    "자, 이런 선택은 하이라이트엔 안 잡혀도 끝까지 살아남으면 얘기가 달라집니다.",
                    "어... 재미는 덜해 보여도 실패할 확률도 같이 내려가겠네요.",
                    "이런 거 하나 챙겨두면 나중에 '아, 그때 잘 골랐다' 소리 나오죠."),
                Mood.Neutral);
            return;
        }

        QueueCopy(
            selected ? "PICKED" : "ITEM CHECK",
            $"{equipment.rarity.ToString().ToUpperInvariant()} / {displayName}",
            selected
                ? PickLine(
                    PresenterLineKey.GenericSelected,
                    "오, 이쪽에서 멈췄네요.",
                    "음... 이게 좀 마음에 드나 보네요.",
                    "이걸 후보로 올리네요. 끝까지 갈까요?",
                    "여기 하나 찍었네요. 일단 봐두는 건가.",
                    "이쪽이 좀 끌리나 보네요.",
                    "어, 손이 여기서 멈췄어요. 이런 건 보통 이유가 있죠.",
                    "자, 일단 한 표 들어갔습니다. 아직 확정은 아니고.",
                    "아... 이쪽이구나. 생각보다 취향이 보이는데요?")
                : PickLine(
                    PresenterLineKey.GenericHover,
                    "음... 다음 건 이거네요. 꽤 무난한데.",
                    "정석 쪽이네요. 크게 튀진 않고.",
                    "이건 조합을 좀 봐야겠네요.",
                    "특별하진 않은데 기본은 해주겠네요.",
                    "음, 무난하네요. 그래도 후보에서 빠질 정도는 아니고.",
                    "이건 실제로 써봐야 느낌 오겠네요.",
                    "자, 딱 봤을 때는 좀 평범하죠. 근데 이런 게 은근 조합 타면 세거든요.",
                    "어... 첫인상은 약한데, 그렇다고 바로 넘길 정도는 아니네요.",
                    "이건 설명만 보고 결론 내리기 좀 애매해요. 실제 손에 들어가야 알겠는데."),
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
                        "오, 엘리트네요. 이쪽은 좀 재밌겠는데?",
                        "음... 여긴 확실히 위험하네요. 그래도 볼 건 많겠고.",
                        "이쪽은 좀 거칠겠네요. 대신 심심하진 않겠어요.",
                        "엘리트 코스네. 음... 이건 조금 끌리는데.",
                        "이런 데 하나쯤 있어야 긴장감이 살죠.",
                        "자, 여기부터는 방송 사고가 아니라 진짜 사고 날 수도 있습니다. 대신 그림은 잘 나오겠죠.",
                        "어... 이쪽 고르면 편하게 가는 건 포기하는 거네요.",
                        "엘리트라. 위험한 건 맞는데, 여기 넘기면 보상도 좀 기대해볼 만하죠."),
                    Mood.Excited);
                break;

            case BattleNodeType.Shop:
                QueueCopy(
                    "SHOPPING BREAK",
                    $"{stage} / SHOP",
                    PickLine(
                        PresenterLineKey.MapShop,
                        "오, 상점이네요. 잠깐 쉬어가겠는데.",
                        "다음 무대 전에 장비 좀 보고 갈 수 있겠네요.",
                        "전투는 아니고 쇼핑이네요. 이것도 나쁘진 않죠.",
                        "여긴 잠깐 템포 좀 내려가겠네요. 뭐 살지가 더 중요하겠고.",
                        "음, 브레이크 타임이네요. 지갑은 좀 바빠지겠지만.",
                        "자, 여기선 칼보다 계산기가 더 중요하겠네요. 돈이 얼마나 남았더라.",
                        "어, 상점이면 급하게 갈 필요 없죠. 한번 싹 보고 가도 되고.",
                        "이런 데서 괜히 하나 샀다가 다음 보상 보고 후회하는 경우도 있거든요."),
                    Mood.Neutral);
                break;

            case BattleNodeType.Event:
                QueueCopy(
                    "SPECIAL SEGMENT",
                    $"{stage} / EVENT",
                    PickLine(
                        PresenterLineKey.MapEvent,
                        "오, 이벤트네요. 이건 뭐가 나오려나.",
                        "내용은 안 보이네요. 이런 건 열어봐야 알죠.",
                        "전투 말고 다른 게 나오나 보네요. 음... 뭐지?",
                        "이쪽은 결과를 모르는 게 좀 재밌네요.",
                        "분위기 한번 바꾸기엔 괜찮아 보이는데.",
                        "자, 정보가 없습니다. 방송하는 사람 입장에선 제일 곤란하고 제일 재밌는 타입이죠.",
                        "어... 이런 건 괜히 궁금하게 만들어놔서 문제예요. 눌러보고 싶잖아.",
                        "좋을 수도 있고, 아무것도 아닐 수도 있고. 그래서 이벤트겠죠."),
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
                            "오, 이건 좀 재밌어 보이네요. 이쪽 가려나?",
                            "별 네 개 이상이네요. 음... 메인 무대 느낌은 나는데.",
                            "이쪽은 쉽게 끝나진 않겠네요.",
                            "오, 이제 좀 긴장감 있네요.",
                            "이건 다음 장면 좀 기대해도 되겠네요. 어디 고를지 보죠.",
                            "자, 여기 들어가면 분위기 확 바뀝니다. 슬슬 집중해서 봐야겠는데요.",
                            "어... 난이도 꽤 높네요. 편하게 갈 생각이면 다른 쪽 보는 게 맞고.",
                            "이건 성공하면 하이라이트고, 꼬이면 그대로 사고 나는 거죠. 선택은 본인이 하겠고요."),
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
                            "음, 딱 중간 정도네요. 무난하게 보기 좋겠는데.",
                            "별 세 개네요. 적당하네.",
                            "너무 무겁지도 않고, 너무 심심하지도 않고.",
                            "오늘 무난하게 가려면 이 정도겠네요.",
                            "음... 안정적으로 가려면 이쪽이겠네요.",
                            "자, 딱 방송 한 판 분량으로 보기 좋은 정도네요. 너무 길지도 않고.",
                            "어, 이건 별 고민 없이 들어가도 크게 사고는 안 날 것 같은데.",
                            "이런 코스가 의외로 제일 깔끔하게 끝나기도 하죠. 무난하다고 나쁜 건 아니고."),
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
                        "음... 이번 건 좀 쉬어가는 쪽이네요.",
                        "별이 낮네요. 뭐, 이런 구간도 하나쯤은 있어야죠.",
                        "이번엔 편하게 볼 수 있겠네요.",
                        "음... 긴장감은 잠깐 내려놓으셔도 되겠네요.",
                        "이번 코스는 잔잔하네요. 다음 큰 무대 전 워밍업 정도인가.",
                        "자, 여기서는 큰 건 좀 내려놓고 봐도 되겠네요. 그냥 깔끔하게 넘기는 구간 같아요.",
                        "어... 솔직히 이건 좀 심심해 보이는데. 대신 체력 아끼기엔 좋겠죠.",
                        "이 정도면 한 손 놓고 보셔도... 아니, 그건 좀 그렇고. 아무튼 여유는 있겠네요."),
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

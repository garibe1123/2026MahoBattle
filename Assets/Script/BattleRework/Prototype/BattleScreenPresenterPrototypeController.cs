using System.Collections.Generic;
using UnityEngine;
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
                "오호, 결국 그걸 집었네? 다음 판이 아주 재밌어지겠는데~",
                "흐응, 그걸로 결정? 좋아, 어디 한번 실력 좀 보여줘!",
                "어라, 이쪽이 취향이야? 꽤 의외인데?",
                "그걸 고르겠다고? 히히, 결과가 벌써 궁금해졌어!",
                "좋아, 골랐으면 이제 써봐야지! 기대할게?",
                "한참 고민하더니 그걸 집네? 무슨 꿍꿍이야~?",
                "짠, 오늘의 선택은 이거! 얌전해 보여도 방심은 금물이야~",
                "오케이, 장비 장착! 자아, 다음 무대에선 뭘 보여주려나?"),
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
                        "우와, 엘리트한테 가겠다고? 후후, 제법 배짱 있는데!",
                        "하필 저쪽을 골랐어? 어머, 재밌는 장면 나오겠네~",
                        "엘리트 확정! 자, 실수 한 번이면 아야할 수도 있어?",
                        "어라, 그렇게 센 녀석이 좋아? 용감한 거야, 무모한 거야?",
                        "좋아, 엘리트 코스! 이거야말로 볼 맛이 나지~!",
                        "위험한 만큼 보상도 크겠지? 흐응, 욕심쟁이네?");
                mood = Mood.Excited;
                break;

            case BattleNodeType.Shop:
                line =
                    owner.PickLine(
                        PresenterLineKey.ScriptConfirmShop,
                        "오, 쇼핑하러 가는구나? 지갑은 챙겼지?",
                        "상점으로 결정~ 전투 전에 뭘 사올지 궁금한걸?",
                        "돈이 좀 남아 있나 봐? 히히, 다 쓰진 말고~",
                        "천천히 골라봐. 충동구매도 은근 재밌거든?",
                        "짜잔, 쇼핑 타임! 이번엔 무슨 장난감을 고를까?",
                        "어라, 상점이야? 지갑 텅텅 비워오는 건 아니겠지?");
                mood = Mood.Neutral;
                break;

            case BattleNodeType.Event:
                line =
                    owner.PickLine(
                        PresenterLineKey.ScriptConfirmEvent,
                        "어라, 이벤트? 나도 안을 못 봤는데... 재밌겠다!",
                        "그걸 열어보겠다고? 후후, 호기심이 많네~",
                        "이벤트 확정! 선물일까, 함정일까? 맞혀봐!",
                        "안에 뭐가 들었는지도 모르잖아? 그래도 가겠다고?",
                        "자, 두근두근~ 열어보면 알겠지!",
                        "헤에, 수상한 걸 좋아하네? 나도 그런 거 좋아해!");
                mood = Mood.Curious;
                break;

            default:
                if (clampedStars >= 4)
                {
                    line =
                        owner.PickLine(
                            PresenterLineKey.ScriptConfirmHigh,
                            "오호, 어려운 길로 간다고? 이거 재밌겠는데~",
                            "여기로 결정! 히히, 이제 좀 긴장해도 좋겠어!",
                            "어라, 저쪽이야? 다음 판은 화려하겠는걸!",
                            "흐응, 일부러 위험한 길을 고른 거야? 자신 있어?",
                            "좋아, 높은 난이도! 과연 얼마나 버틸까~?",
                            "아까부터 눈독 들이더니 진짜 고르네? 귀여워라~",
                            "역시 센 쪽이 좋아? 후후, 나중에 후회하지 마?");
                    mood = Mood.Excited;
                }
                else if (clampedStars == 3)
                {
                    line =
                        owner.PickLine(
                            PresenterLineKey.ScriptConfirmMid,
                            "여기로 갈래? 흐응, 아주 얌전한 선택인데~",
                            "오, 딱 중간이네. 재미없지도, 무섭지도 않겠어!",
                            "별 세 개라~ 좋았어, 어디 한번 가보자!",
                            "무리하지는 않겠다는 거지? 제법 신중하네?",
                            "좋아, 적당한 코스! 그런데 적당하다고 방심은 안 돼~",
                            "이쪽이 마음에 들었어? 뭐, 나쁘진 않네!");
                    mood = Mood.Neutral;
                }
                else
                {
                    line =
                        owner.PickLine(
                            PresenterLineKey.ScriptConfirmLow,
                            "어라, 안전한 길? 이번엔 좀 쉬고 싶은가 봐~",
                            "얌전한 선택이네? 뭐어, 가끔은 쉬어야지.",
                            "오, 여긴 좀 편하겠어. 잠깐 한숨 돌릴래?",
                            "다음 큰 무대 전에 힘 아끼려는 거지? 알겠어~",
                            "헤에, 오늘은 안전이 제일이야? 겁먹은 건 아니고?",
                            "여기로 가겠다고? 좋아, 금방 끝내고 돌아와!",
                            "이런 조용한 판도 필요하지~ 하지만 너무 심심하면 곤란한데?");
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
                    "짜잔~! 보상 시간! 어디, 재밌는 물건 좀 있나?",
                    "전투 끝! 자아, 이번엔 뭘 챙겨갈 거야?",
                    "어라, 이번 후보들 좀 수상한데? 히히, 마음에 들어~",
                    "하나씩 구경해 봐! 고르는 재미가 있잖아?",
                    "흐응, 어느 게 끌려? 난 벌써 하나 골랐는데~",
                    "드디어 보상 시간이야! 전투보다 고민이 길어지는 건 아니겠지?",
                    "어머, 첫인상부터 갈리겠는걸? 고르기 어렵겠다!",
                    "장비 바꿀 기회야~ 여기서 좋은 거 하나 건져봐!"),
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
                    "짜잔, 다음 길이 나왔어! 어디가 제일 재밌어 보여?",
                    "다음 무대는 이 중 하나야~ 어디로 보내줄까?",
                    "선택지가 떴네! 흐응, 좀 고민되겠는데?",
                    "자, 다음 코스! 어느 쪽으로 갈지 맞혀볼까~?",
                    "길이 갈렸어! 후후, 여기서 취향이 드러나겠네?",
                    "안전한 길이랑 위험한 길! 자아, 뭐가 좋아?",
                    "어라, 이번엔 취향이 확 갈릴 것 같은데?",
                    "아무거나 찍진 마~ 재미없는 선택이면 나 삐질지도?"),
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
                "흐응~ 아직도 고르는 중이야? 꼼꼼하네?",
                "어라, 어디 갔지? 나 혼자 방송하고 있는 거야?",
                "그렇게 오래 보니까 더 궁금해지잖아~",
                "이쯤 되면 하나쯤 마음에 들었을 텐데? 아닌가?",
                "아직이야? 설마 세 개 다 갖고 싶은 건 아니지?",
                "나라면 벌써 골랐겠다~ 뭐, 네 마음이지만!",
                "셋 중 하나만 고르라니, 은근 잔인하지? 히히.",
                "시청자들이 있었다면 벌써 난리 났을걸~?")
            : PickLine(
                PresenterLineKey.BoredScript,
                "아직 고민 중이야? 흐응, 나 기다리는 거 잘 못하는데~",
                "어라, 잠깐 나갔다 올까? 아니지, 방송 중이지!",
                "길 하나 고르는 데 이렇게 오래 걸려? 귀여워라~",
                "천천히 골라~ 대신 너무 오래 기다리게 하진 말고?",
                "아직도? 히히, 다 위험해 보이긴 하지?",
                "이쪽도 좋고 저쪽도 좋고~ 결국 네가 골라야지!",
                "그 앞에서 멈춘 거 다 봤어! 마음은 정했지?",
                "자아, 방송은 계속돼! 선택만 하면 바로 출발이야~");

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
                    "어라, 톱이야? 그걸 들고 싸우겠다고? 히히!",
                    "잠깐, 진짜 톱 맞지? 우와, 살벌해라~",
                    "오호, 눈에 확 띄는 물건인데? 제법인데!",
                    "그걸 집으면 다음 무대가 꽤 시끄러워지겠는걸?",
                    "이상한 무기일수록 재밌지! 마음에 들어?",
                    "히히, 이런 걸 들고 나오면 카메라가 좋아하겠어~",
                    "성능은 둘째 치고 생김새부터 무섭잖아? 최고!",
                    "나라면 한 번쯤 써볼걸? 후후, 결과는 책임 못 져!"),
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
                    "어라, 이건 대체 뭐야? 이상해서 더 좋은데?",
                    "이런 물건 하나쯤 있어야 재미있지~!",
                    "오호, 이건 무슨 반응이 나올까? 궁금해!",
                    "정석? 그런 건 지루하잖아~ 이쪽 좀 봐!",
                    "오늘 나온 것 중 제일 별나다! 눈이 가네~",
                    "설명이 이렇게 길어? 후후, 위험하거나 엄청 재밌거나!",
                    "일단 써보자! 이상한 물건은 직접 봐야 제맛이지!",
                    "흐응, 평범하게 굴러가진 않겠네? 좋아, 좋아!"),
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
                    "어머, 잠깐! 이건 좀 급이 다른데?",
                    "우와, 이런 게 진짜 나와? 운 좋네~!",
                    "이걸 보고도 고민할 수 있어? 욕심나지?",
                    "희귀한 물건이잖아! 놓치면 아까울걸~",
                    "오호, 오늘 나온 것 중에 제일 반짝거리네!",
                    "잠깐, 카메라 줌! 이건 크게 보여줘야지~",
                    "어라, 선택지가 아니라 거의 대박인데?",
                    "이거 놓치고 나중에 울면 안 된다~?"),
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
                    "어라, 이거 터지는 거야? 히히, 재밌겠다!",
                    "설명 안 읽어도 딱 알겠네~ 펑! 하는 거지?",
                    "우와, 전투 시작하면 바로 티 나겠는걸?",
                    "다음 무대, 화끈하게 가볼래? 이거 어때?",
                    "터지는 맛이 있겠어~ 후후, 꽤 마음에 드는데?",
                    "조용히 끝낼 생각은 없나 봐? 좋아, 시끄러운 거!",
                    "이건 카메라가 바빠지겠어~ 놓치면 안 되는데!",
                    "너무 크게 터뜨리진 마~ 아니, 조금은 괜찮을지도?"),
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
                    "어라, 방어용이네? 이번엔 오래 버텨볼 생각이야?",
                    "얌전한 물건이네~ 그래도 은근 든든하겠어.",
                    "오호, 살아남는 쪽으로 가겠다고? 영리하네!",
                    "화려하진 않아도 효과는 있겠지? 흐응~",
                    "조금 심심하지만, 오래 쓰기엔 딱 좋을지도?",
                    "카메라엔 잘 안 잡혀도 끝까지 살아남으면 멋지겠지!",
                    "재미는 덜해도 안전하긴 하겠어. 뭐, 그것도 좋아~",
                    "나중에 잘 골랐다고 칭찬해줘야 할지도 모르겠네?"),
                Mood.Neutral);
            return;
        }

        QueueCopy(
            selected ? "PICKED" : "ITEM CHECK",
            $"{equipment.rarity.ToString().ToUpperInvariant()} / {displayName}",
            selected
                ? PickLine(
                    PresenterLineKey.GenericSelected,
                    "어라, 여기서 멈췄네? 마음에 들었어?",
                    "흐응, 이게 그렇게 좋아? 취향 독특하네~",
                    "이걸 찜한 거야? 끝까지 갈지는 두고 볼까?",
                    "오호, 일단 후보로 찜! 아직 확정은 아니지?",
                    "이쪽이 끌려? 히히, 티 다 나는데~",
                    "손이 멈춘 거 다 봤어! 왜, 꽂혔어?",
                    "자아, 한 표 들어갔다~ 이제 결정할 차례야!",
                    "어머, 이쪽이구나? 생각보다 재미있는 취향인데?")
                : PickLine(
                    PresenterLineKey.GenericHover,
                    "흐응, 이건 꽤 평범하네? 그래도 볼까?",
                    "정석적인 물건이네~ 재미는 조금 덜하지만!",
                    "이건 조합을 잘 맞춰야겠는걸?",
                    "평범하다고 얕보면 안 돼~ 기본은 하겠어.",
                    "어라, 무난한데? 그래도 후보엔 올려둬!",
                    "직접 써보면 의외로 좋을지도? 히히.",
                    "처음엔 평범해 보여도 조합에 따라 달라지거든~",
                    "첫인상은 얌전해도, 의외로 센 녀석일 수 있어!",
                    "설명만 봐선 모르겠어~ 직접 굴려봐야지!"),
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
                        "오호, 엘리트! 저쪽이면 꽤 재밌겠는데?",
                        "어라, 위험해 보이네? 그게 오히려 끌려?",
                        "이쪽은 좀 거칠 거야~ 견딜 수 있겠어?",
                        "엘리트 코스네! 후후, 나 좀 기대해도 돼?",
                        "이 정도는 돼야 긴장감이 살지~!",
                        "방송 사고가 아니라 진짜 사고 날지도? 히히, 조심해!",
                        "저쪽을 고르면 편하게 가긴 글렀겠네~",
                        "위험한 만큼 보상도 클 거야! 욕심나지?"),
                    Mood.Excited);
                break;

            case BattleNodeType.Shop:
                QueueCopy(
                    "SHOPPING BREAK",
                    $"{stage} / SHOP",
                    PickLine(
                        PresenterLineKey.ScriptShop,
                        "상점이다! 히히, 잠깐 쉬어갈래?",
                        "장비 좀 구경하고 갈래? 나도 구경할래!",
                        "쇼핑 시간이네~ 전투보다 이게 더 재미있을지도?",
                        "여긴 천천히 가도 돼~ 뭘 살지 잘 골라봐!",
                        "브레이크 타임! 지갑도 좀 바빠지겠는데?",
                        "이번엔 칼보다 돈이 필요하겠어~ 얼마나 남았어?",
                        "급하게 고르지 마~ 구경은 공짜잖아!",
                        "괜히 샀다가 다음 보상 보고 후회하면 안 된다~?"),
                    Mood.Neutral);
                break;

            case BattleNodeType.Event:
                QueueCopy(
                    "SPECIAL SEGMENT",
                    $"{stage} / EVENT",
                    PickLine(
                        PresenterLineKey.ScriptEvent,
                        "어라, 이벤트다! 안에는 뭐가 있으려나?",
                        "안을 안 보여주네? 흐응, 더 궁금해지잖아~",
                        "전투 말고 다른 게 나온대! 히히, 뭐지?",
                        "결과를 모르니까 더 재밌어~ 그렇지?",
                        "분위기 바꿀 시간인가 봐! 좋아, 좋아!",
                        "어라~? 뭘 이렇게 꽁꽁 숨겨놨어? 열어보고 싶잖아!",
                        "일부러 수상하게 만들어놨네? 후후, 눌러볼래?",
                        "선물일 수도, 꽝일 수도! 자아, 어디 한번?"),
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
                            "오호, 이쪽은 꽤 재밌어 보이는데? 갈래?",
                            "별 네 개 이상! 슬슬 메인 무대 느낌 나지?",
                            "쉽게 끝나진 않을걸~ 자신 있어?",
                            "이야, 드디어 긴장감이 좀 살아나네!",
                            "다음 장면 기대해도 되지? 어디 고를 거야?",
                            "자아, 여기 들어가면 분위기가 확 바뀔 거야~ 준비됐어?",
                            "위험한 코스인데? 무서우면 다른 데 가도 돼~",
                            "성공하면 대박, 실패하면 대참사! 후후, 선택은 네 몫!"),
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
                            "흐응, 딱 중간이네~ 무난하게 놀기 좋겠어.",
                            "별 세 개라~ 나쁘지 않은데?",
                            "너무 무섭지도, 너무 심심하지도 않겠어!",
                            "오늘은 적당히 놀아보겠다는 거지?",
                            "안정적으로 가고 싶어? 그럼 이쪽이네!",
                            "한 판 놀기 딱 좋은 코스야~ 길지도 짧지도 않고!",
                            "어라, 이 정도면 무난하게 넘길 수 있겠는데?",
                            "평범한 길이 의외로 재밌을 때도 있지~!"),
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
                        "어라, 이번엔 쉬어가는 코스야?",
                        "별이 낮네~ 가끔은 이런 길도 있어야지.",
                        "이번 판은 좀 편하게 구경할 수 있겠어.",
                        "긴장은 잠깐 풀어도 돼~ 너무 방심하진 말고!",
                        "다음 큰 무대 전 워밍업인가? 흐응~",
                        "이쪽은 조용하겠네~ 금방 끝내고 다시 놀자!",
                        "솔직히 좀 심심할지도? 대신 안전하겠지!",
                        "한 손으로 해도 되겠... 아냐, 농담이야! 히히."),
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

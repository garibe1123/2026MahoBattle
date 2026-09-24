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
    [SerializeField] private Vector2 dialogueSize = new(1020f, 154f);
    [SerializeField] private Vector2 dialogueVisibleOffset = new(-280f, 34f);
    [SerializeField, Min(0f)] private float dialogueHiddenOffsetY = 72f;
    [SerializeField, Min(0.03f)] private float dialogueOpenDuration = 0.14f;
    [SerializeField, Min(0.03f)] private float dialogueCloseDuration = 0.10f;
    [SerializeField, Min(1f)] private float typeCharactersPerSecond = 36f;
    [SerializeField, Min(0.1f)] private float dialogueIdleDuration = 1.65f;

    [Header("Show Camera")]
    [SerializeField, Range(1f, 1.35f)] private float showCameraZoomOut = 1.14f;
    [SerializeField, Range(0f, 2f)] private float cameraRightBiasWorld = 0.68f;

    [Header("Minimal Theme")]
    [SerializeField] private Color dialogueBack = new(0.01f, 0.012f, 0.018f, 0.87f);
    [SerializeField] private Color accent = new(1f, 0.82f, 0.10f, 1f);
    [SerializeField] private Color hotAccent = new(1f, 0.18f, 0.36f, 1f);
    [SerializeField] private Color white = new(0.97f, 0.98f, 1f, 1f);
    [SerializeField] private Color muted = new(0.65f, 0.68f, 0.74f, 1f);

    private BattleRunManager runManager;
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
    private RectTransform dialogueRect;
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
                "아 이걸 가네? 오케이. 그럼 바로 끼워봅니다.",
                "픽 끝. 이제 말 필요 없죠. 바로 써봅니다.",
                "결국 이거 집었네. 자, 이게 맞았는지는 다음 판에서 봅시다.",
                "됐고, 실전 갑니다. 여기서 값 못 하면 좀 곤란한데.",
                "한참 보더니 결국 이거네. 고점 나오나 봅시다."),
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
                "어, 여기 가네? 좋습니다. 다음 판 이쪽입니다.",
                "경로 박았습니다. 바로 넘겨요.",
                "아 이걸 찍네. 과감한 건지 그냥 누른 건지는 곧 나옵니다.",
                "여기 확정. 이거 잘 고른 거 맞나? 일단 갑니다.",
                "오케이 길 나왔고. 바로 다음 갑니다."),
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
                    "끝났고요. 자, 뭐 떴나 봅시다.",
                    "오 보상 깔렸네. 벌써 눈 돌아가는데?",
                    "자 보상. 뭐 줬냐 이번엔.",
                    "전투 끝. 이제 또 욕심 부릴 시간 왔습니다.",
                    "어? 이번엔 좀 있는데? 채팅 벌써 갈리겠네."),
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
                    "다음 길 떴습니다. 아 저는 벌써 하나 보이는데.",
                    "후보 떴고. 또 센 데 기웃거리나 봅시다.",
                    "자 어디가 제일 맛있나 봅시다.",
                    "이건 채팅 갈리겠다. 어디 갑니까?",
                    "다음 떴고요. 자 어디 찍나 보죠."),
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

        BuildDialogue();
        BuildPresenterSquare();
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

        presenterGroup = go.AddComponent<CanvasGroup>();
        presenterGroup.alpha = 0f;
        presenterGroup.interactable = false;
        presenterGroup.blocksRaycasts = false;

        ApplyPresenterTint();
    }

    private void BuildDialogue()
    {
        GameObject root = new("PresenterDialogue", typeof(RectTransform));
        root.transform.SetParent(overlayRoot, false);

        dialogueRect = root.GetComponent<RectTransform>();
        dialogueRect.anchorMin = dialogueRect.anchorMax = new Vector2(0.5f, 0f);
        dialogueRect.pivot = new Vector2(0.5f, 0f);
        dialogueRect.sizeDelta = dialogueSize;
        dialogueRect.anchoredPosition =
            dialogueVisibleOffset - Vector2.up * dialogueHiddenOffsetY;

        Image back = root.AddComponent<Image>();
        back.color = dialogueBack;
        back.raycastTarget = false;

        dialogueGroup = root.AddComponent<CanvasGroup>();
        dialogueGroup.alpha = 0f;
        dialogueGroup.interactable = false;
        dialogueGroup.blocksRaycasts = false;

        nameText = CreateText(
            dialogueRect,
            "PresenterName",
            "SHOW HOST",
            17,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            accent);

        Place(
            nameText.rectTransform,
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(26f, -12f),
            new Vector2(220f, 30f));

        contextText = CreateText(
            dialogueRect,
            "Context",
            string.Empty,
            11,
            FontStyle.Bold,
            TextAnchor.MiddleRight,
            muted);

        Place(
            contextText.rectTransform,
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-24f, -12f),
            new Vector2(550f, 28f));

        dialogueText = CreateText(
            dialogueRect,
            "DialogueText",
            string.Empty,
            27,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            white);

        RectTransform textRect = dialogueText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(26f, 18f);
        textRect.offsetMax = new Vector2(-30f, -38f);
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
            new Vector2(-18f, 9f),
            new Vector2(110f, 20f));
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
                "음... 아직 고르는 중인가 보네요.",
                "어디 간 거야? 담배라도 피러 갔나? 하...",
                "생각보다 고민이 길어지네요. 뭐, 천천히 보시죠.",
                "이 정도면 하나쯤 눈에 들어올 법도 한데...",
                "아직도 보고 있네요. 음... 기다려보죠.")
            : PickLine(
                PresenterLineKey.BoredMap,
                "음... 아직도 고민 중이네요.",
                "어디 간 거야? 잠깐 자리 비운 건가?",
                "길 하나 고르는 데 생각보다 오래 걸리네요.",
                "뭐, 급할 건 없죠. 천천히 고르시죠.",
                "아직인가... 음, 기다려보겠습니다.");

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
        dialogueRect.anchoredPosition = Vector2.Lerp(
            dialogueVisibleOffset - Vector2.up * dialogueHiddenOffsetY,
            dialogueVisibleOffset,
            clamped);
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

        Vector3 target =
            showWorld.CameraTargetWorld +
            Vector3.right * cameraRightBiasWorld;

        float size =
            showWorld.ShowCameraSize *
            Mathf.Max(1f, showCameraZoomOut);

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
                    "아니 톱이 왜 나와. 시작부터 좀 세죠?",
                    "이건 클립 하나 나오겠는데? 생긴 것부터 너무 셉니다.",
                    "톱은 좀 치트키죠. 좋든 싫든 기억은 남습니다.",
                    "이거 채팅 바로 반반 갈립니다. 딱 봐도 그래요.",
                    "무난한 거 찾던 사람들 지금 뒤로 가시면 됩니다. 톱이에요."),
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
                    "어 이건 호불호 씨게 오는데. 채팅 판정 갑니다.",
                    "정석은 아니고요. 대신 이런 게 재밌죠.",
                    "뭐 설명보다 그냥 쓰는 거 보고 싶은데요?",
                    "이건 성능보다 취향 먼저 봐야 돼. 이거 가능?",
                    "이상한데... 그래서 더 보고 싶네. 이건 인정."),
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
                    "오오 잠깐. 이건 급이 좀 다른데?",
                    "어 이건 넘기면 안 돼요. 이건 좀 큽니다.",
                    "이거 메인 냄새 나는데? 희귀도부터 다르죠.",
                    "잠깐 확대. 이건 좀 보여줘야 돼.",
                    "아 이런 거 하나 떠줘야지. 이제 좀 방송 같다."),
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
                    "이건 간단합니다. 터져요. 끝.",
                    "화력? 여기 있네요. 그냥 대놓고 셉니다.",
                    "이거 들고 조용히 끝나면 그게 더 이상하죠.",
                    "큰 거 좋아하는 사람들 이건 못 참지.",
                    "안전은 모르겠고 그림은 나옵니다. 그건 확실해."),
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
                    "재미는 좀 없는데 안 죽는 게 원래 제일 세요.",
                    "화면빨은 없는데 저점 방어는 확실하겠네.",
                    "안전빵이네요. 길게 보면 이런 게 또 셉니다.",
                    "아 좀 심심하다. 근데 이런 게 결국 일은 해요.",
                    "모범생 픽이네. 재미는 없고 성적은 잘 나오는."),
                Mood.Neutral);
            return;
        }

        QueueCopy(
            selected ? "PICKED" : "ITEM CHECK",
            $"{equipment.rarity.ToString().ToUpperInvariant()} / {displayName}",
            selected
                ? PickLine(
                    PresenterLineKey.GenericSelected,
                    "어 이걸 집네? 채팅 지금 좀 갈릴 듯.",
                    "이쪽 갔고요. 뭘 본 건진 다음 판에서 나오겠죠.",
                    "여기까지 좁혔네. 예상한 사람?",
                    "여기 찍었습니다. 끝까지 가는지 봐요.",
                    "픽 들어갔고요. 평범해 보여도 조합 만나면 모릅니다.")
                : PickLine(
                    PresenterLineKey.GenericHover,
                    "평범하네... 근데 이런 게 조합 붙으면 갑자기 셉니다.",
                    "정석이네요. 재미는 없는데 망할 일도 적고.",
                    "별거 없어 보이는데 은근 이런 거 오래 갑니다.",
                    "일단 무난. 조합 보고 평가하죠. 지금은 보류.",
                    "안전빵 갑니다. 뭐 매번 도박할 순 없죠.",
                    "이건 말보다 실전이 빠릅니다. 다음 판 보죠."),
            selected ? Mood.Excited : Mood.Neutral);
    }

    private void ShowMap(BattleNodeData node, int stars)
    {
        if (node == null)
            return;

        string stage =
            $"STAGE {Mathf.Max(1, node.depth + 1):00} / {Mathf.Clamp(stars, 1, 5)} STAR";

        switch (node.type)
        {
            case BattleNodeType.Elite:
                QueueCopy(
                    "CAUTION",
                    $"{stage} / ELITE",
                    PickLine(
                        PresenterLineKey.MapElite,
                        "엘리트 떴네. 아 이건 좀 맛있는데?",
                        "편하게 갈 거면 이쪽 보면 안 되죠. 엘리트입니다.",
                        "여기 뚫으면 고점 확실해요. 대신 터질 수도 있고.",
                        "어우 여기 냄새 나는데. 이쪽 가면 좀 빡셉니다.",
                        "센 놈 나옵니다. 이런 데서 클립 나오죠."),
                    Mood.Concerned);
                break;

            case BattleNodeType.Shop:
                QueueCopy(
                    "SHOPPING BREAK",
                    $"{stage} / SHOP",
                    PickLine(
                        PresenterLineKey.MapShop,
                        "상점이네. 또 뭐 집어오나 봅시다.",
                        "상점 떴고요. 여기서 또 세팅 욕심 나오죠.",
                        "잠깐 쇼핑. 숨도 쉬고 돈도 쓰고.",
                        "전투는 쉬는데 돈은 안 쉽니다. 네.",
                        "상점 갑니다. 또 이상한 거 집어오면 재밌겠는데."),
                    Mood.Curious);
                break;

            case BattleNodeType.Event:
                QueueCopy(
                    "SPECIAL SEGMENT",
                    $"{stage} / EVENT",
                    PickLine(
                        PresenterLineKey.MapEvent,
                        "이벤트네. 아 이런 건 몰라서 재밌죠.",
                        "이거 뭐 나올지 모릅니다. 좋은 거냐 사고냐, 찍어봐요.",
                        "정보 없고요. 이럴 때 채팅이 제일 자신감 넘칩니다.",
                        "전투는 아니고요. 갑자기 딴 거 합니다.",
                        "뭐 나올진 모르겠는데 평범하게 끝날 얼굴은 아닙니다."),
                    Mood.Curious);
                break;

            default:
                QueueCopy(
                    "COURSE CHECK",
                    $"{stage} / COMBAT",
                    stars >= 4
                        ? PickLine(
                            PresenterLineKey.MapCombatHigh,
                            "난도 높죠. 근데 솔직히 이런 거 보러 온 거잖아.",
                            "별 많고요. 네, 다음 판 좀 뜨겁습니다.",
                            "이쪽 가면 좀 집중해야 됩니다. 사고 나기 딱 좋아요.",
                            "쉽게는 안 끝나겠네. 볼 건 많겠습니다.",
                            "여긴 좀 쎄합니다. 평범하게 안 끝날 것 같은데.")
                        : PickLine(
                            PresenterLineKey.MapCombatNormal,
                            "정석이네요. 그냥 한 판 깔끔하게 가는 길.",
                            "일반 전투. 별일 없으면 그냥 정직하게 갑니다.",
                            "무난한 길인데... 오늘은 좀 얌전히 가려나?",
                            "바로 전투. 템포 안 끊기고 좋습니다.",
                            "딱 무난합니다. 큰 사고 없이 보기 좋은 쪽."),
                    stars >= 4 ? Mood.Concerned : Mood.Neutral);
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

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
        MapCombatNormal
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
                "도전자의 선택이 확정됐습니다. 여러분, 바로 장착 장면까지 이어서 보시죠!",
                "선택 완료입니다. 화면 그대로 이어서 장착 과정까지 보여드리겠습니다.",
                "오늘 도전자가 고른 건 이쪽입니다. 과연 다음 전투에서 어떤 그림이 나올까요?",
                "확정됐습니다. 이제 시청자 여러분께서는 실제 사용 장면을 확인하시면 되겠습니다.",
                "이번 선택은 여기로 굳어졌습니다. 다음 장면에서 성능을 직접 확인해보죠."),
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
                "다음 방송 코스가 확정됐습니다. 여러분, 이번엔 이쪽 무대로 이동합니다!",
                "경로가 정해졌습니다. 다음 무대에서도 계속 지켜봐 주시죠!",
                "도전자가 이 경로를 선택했습니다. 방송은 그대로 다음 구간으로 이어집니다.",
                "다음 스테이지 확정입니다. 과연 이 선택이 어떤 결과로 이어질까요?",
                "경로가 잡혔습니다. 시청자 여러분, 다음 장면 바로 이어가겠습니다!"),
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
                    "시청자 여러분, 전투가 끝났습니다. 이제 오늘 들어온 보상들을 함께 보시죠.",
                    "오늘의 보상 진열대가 열렸습니다. 어떤 물건이 도전자의 눈에 들어올까요?",
                    "보상 시간입니다. 여러분, 이번 진열대엔 어떤 물건이 나왔는지 확인해보시죠.",
                    "전투는 여기까지. 이제 도전자의 다음 선택을 결정할 쇼핑 시간이 왔습니다.",
                    "이번 진열대도 제법 볼거리가 있군요. 여러분은 어떤 물건이 눈에 들어오시나요?"),
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
                    "이제 다음 코스를 정할 시간입니다. 여러분이라면 어느 쪽 무대를 보고 싶으신가요?",
                    "다음 무대 후보가 나왔습니다. 도전자는 어느 쪽을 고르게 될까요?",
                    "다음 방송 장소 후보입니다. 각 코스가 어떤 장면을 만들지 한번 보시죠.",
                    "경로 선택 시간입니다. 시청자 여러분께서는 어느 쪽이 더 재미있어 보이시나요?",
                    "다음 스테이지 후보가 공개됐습니다. 도전자의 선택을 지켜보시죠."),
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
        Mood mood)
    {
        pendingHeader = header ?? string.Empty;
        pendingKeyword = keyword ?? string.Empty;
        pendingComment = comment ?? string.Empty;
        pendingMood = mood;
        hasPendingCopy = true;

        EnsureOverlay();
        reactionMood = mood;
        reactionStartedAt = Time.unscaledTime;
        lastPresenterInteractionTime = Time.unscaledTime;

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
                    "시청자 여러분, 톱날입니다. 오늘 진열대에서 존재감 하나는 확실하군요.",
                    "이건 화면에 정말 잘 잡히겠네요. 독특한 장비를 좋아하시는 분들은 눈여겨보시죠.",
                    "톱이라... 취향은 갈려도 방송에서 잊히기는 어려운 물건입니다.",
                    "이런 장비가 나와야 채팅창도 갈리죠. 여러분 반응이 벌써 궁금한데요.",
                    "평범한 물건을 기대하셨다면 이번 후보는 확실히 방향이 다릅니다."),
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
                    "호불호가 꽤 갈릴 물건입니다. 시청자 여러분의 평가는 어떨까요?",
                    "정석적인 장비는 아닙니다. 그래서 오히려 방송에서는 더 눈이 가는군요.",
                    "사용 장면이 먼저 궁금해지는 장비네요. 여러분도 그렇지 않으신가요?",
                    "이건 성능표보다 먼저 취향 투표부터 받아보고 싶은 상품입니다.",
                    "이상한데 묘하게 보고 싶습니다. 이런 게 방송용 물건이죠."),
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
                    "오, 이건 카메라를 잡아둘 가치가 있네요. 오늘 진열대의 주연 후보입니다!",
                    "잠깐만요, 여러분. 이건 그냥 지나치기엔 희귀도가 꽤 높습니다.",
                    "희귀도부터 다르죠. 오늘 보상 코너의 메인 상품 후보입니다.",
                    "이 정도면 카메라 한 번 더 당겨야겠습니다. 화면으로도 차이가 보이네요.",
                    "좋습니다. 이런 물건이 나와줘야 시청자분들도 끝까지 보실 맛이 나죠."),
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
                    "설명이 길 필요는 없겠군요. 화끈한 장면을 기다리셨다면 이쪽입니다!",
                    "이번 후보는 방향이 명확합니다. 여러분이 원하신 게 화력이었다면 주목하시죠.",
                    "이 장비가 들어가면 다음 전투가 얌전히 끝나진 않겠네요. 방송적으로는 반갑습니다.",
                    "큰 장면을 기다리시는 시청자분들이라면 이 후보를 빼긴 어렵겠는데요.",
                    "안전성과는 거리가 좀 있어 보이지만, 시청 포인트는 확실합니다."),
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
                    "화려하진 않지만 생존력이 올라갑니다. 장기전을 좋아하신다면 이런 선택도 눈여겨볼 만하죠.",
                    "화면에서는 덜 화려해도 이런 장비가 결국 도전자를 오래 남겨둡니다.",
                    "이번 후보는 안정성 쪽입니다. 다음 전투가 길어질수록 존재감이 커지겠네요.",
                    "조금 심심해 보이죠? 그래도 생존 관련 장비는 결과 화면에서 자주 빛납니다.",
                    "화려함보다는 꾸준함. 시청자 여러분, 이런 선택도 은근히 오래 갑니다."),
                Mood.Neutral);
            return;
        }

        QueueCopy(
            selected ? "PICKED" : "ITEM CHECK",
            $"{equipment.rarity.ToString().ToUpperInvariant()} / {displayName}",
            selected
                ? PickLine(
                    PresenterLineKey.GenericSelected,
                    "도전자가 이쪽을 집었습니다. 여러분, 이 선택 어떻게 보시나요?",
                    "이번 선택은 이쪽입니다. 도전자가 무엇을 노리는지 다음 장면에서 확인해보죠.",
                    "후보가 이쪽으로 좁혀졌습니다. 시청자 여러분도 예상하셨나요?",
                    "도전자가 이 상품에 표를 던졌습니다. 과연 최종 선택까지 이어질까요?",
                    "선택이 들어갔습니다. 무난하지만 다음 전투의 방향을 바꿀 수도 있겠죠.")
                : PickLine(
                    PresenterLineKey.GenericHover,
                    "겉보기엔 무난합니다. 다만 조합에 따라 방송 그림이 꽤 달라질 수도 있겠네요.",
                    "이번 상품은 꽤 정석적입니다. 익숙한 운영을 좋아하시는 분들에겐 눈에 들어오겠네요.",
                    "눈에 확 띄진 않지만 쓰임새는 있어 보입니다. 이런 장비가 의외로 오래 남죠.",
                    "첫인상은 무난합니다. 여러분, 이건 조합까지 봐야 평가가 나오겠네요.",
                    "큰 모험은 아닙니다. 대신 안정적인 장면을 기대할 수 있겠네요.",
                    "이번 상품은 말보다 실제 전투 화면으로 확인하는 편이 빠르겠습니다."),
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
                        "시청자 여러분, 이번 후보는 엘리트 코스입니다. 위험한 만큼 방송 분량은 확실하겠네요.",
                        "엘리트 구간입니다. 편한 전개를 기대하셨다면 이번 후보는 정반대입니다.",
                        "위험도는 올라갑니다. 대신 성공하면 오늘의 하이라이트가 나올 수도 있겠네요.",
                        "분위기부터 다릅니다. 도전자가 이쪽을 택한다면 다음 전투는 긴장해서 보셔야겠네요.",
                        "강한 상대가 기다리는 구간입니다. 시청률만 놓고 보면 아주 반가운 후보죠."),
                    Mood.Concerned);
                break;

            case BattleNodeType.Shop:
                QueueCopy(
                    "SHOPPING BREAK",
                    $"{stage} / SHOP",
                    PickLine(
                        PresenterLineKey.MapShop,
                        "잠깐 쇼핑 구간입니다. 여러분, 다음 전투 전에 도전자가 무엇을 챙길지 지켜보시죠.",
                        "상점 구간입니다. 전투 전에 장비 구성이 어떻게 바뀔지 보는 것도 포인트겠네요.",
                        "전투 전에 쇼핑 타임입니다. 잠시 템포를 바꾸기에 딱 좋은 구간이죠.",
                        "전투는 잠시 쉬어갑니다. 대신 도전자의 지갑은 쉬기 어렵겠네요.",
                        "쇼핑 코스입니다. 시청자 여러분, 과연 어떤 물건이 장바구니에 들어갈까요?"),
                    Mood.Curious);
                break;

            case BattleNodeType.Event:
                QueueCopy(
                    "SPECIAL SEGMENT",
                    $"{stage} / EVENT",
                    PickLine(
                        PresenterLineKey.MapEvent,
                        "이벤트 구간입니다. 무슨 장면이 나올지 모른다는 점이 방송적으로는 가장 흥미롭죠.",
                        "예측 불가 구간입니다. 시청자 여러분도 결과를 한번 맞혀보시죠.",
                        "공개된 정보가 적습니다. 이런 구간이 오히려 채팅창을 가장 뜨겁게 만들죠.",
                        "전투와는 다른 장면이 나올 예정입니다. 잠시 다른 코너라고 생각하시면 되겠습니다.",
                        "무슨 일이 벌어질지는 모르겠습니다. 다만 평범하게 지나갈 가능성은 낮아 보이네요."),
                    Mood.Curious);
                break;

            default:
                QueueCopy(
                    "COURSE CHECK",
                    $"{stage} / COMBAT",
                    stars >= 4
                        ? PickLine(
                            PresenterLineKey.MapCombatHigh,
                            "난도가 꽤 높습니다. 여러분, 이런 코스가 방송의 하이라이트를 만들기도 하죠.",
                            "별 수가 높습니다. 다음 전투는 꽤 빡빡한 장면이 될 것 같군요.",
                            "위험도 높은 코스입니다. 도전자가 선택한다면 긴장해서 지켜보셔야겠습니다.",
                            "쉽게 끝날 코스는 아닙니다. 대신 볼거리는 충분히 나올 것 같네요.",
                            "여긴 조금 긴장하셔도 좋겠습니다. 평범한 전투 화면은 아닐 것 같네요.")
                        : PickLine(
                            PresenterLineKey.MapCombatNormal,
                            "정석적인 전투 코스입니다. 다음 장면을 편하게 이어보기엔 무난하겠네요.",
                            "일반 전투 구간입니다. 큰 변수가 없다면 흐름대로 이어질 가능성이 높습니다.",
                            "이번 후보는 무난한 진행 쪽입니다. 도전자가 안정적인 흐름을 택할지 지켜보시죠.",
                            "바로 다음 전투로 이어지는 정석 코스입니다. 템포가 끊기진 않겠네요.",
                            "위험도와 흐름 모두 무난합니다. 방송 진행상 가장 깔끔한 후보 중 하나네요."),
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

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Disposable full-screen presenter prototype.
///
/// The physical field presenter and World-Space TV remain untouched.
/// This prototype only adds a Screen-Space upper-body presenter cut-in and a minimal dialogue layer.
/// Reward / Map own all gameplay state and only notify this class when commentary should change.
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

    private enum DialoguePhase
    {
        Hidden,
        Opening,
        Typing,
        Idle,
        Closing
    }

    private static BattleScreenPresenterPrototypeController instance;

    [Header("Screen Overlay")]
    [SerializeField] private int overlaySortingOrder = 470;
    [SerializeField] private Vector2 referenceResolution = new(1920f, 1080f);
    [SerializeField, Min(0.01f)] private float showDelay = 0.10f;
    [SerializeField, Min(0.1f)] private float presenterSharpness = 10f;

    [Header("Presenter Upper Body")]
    [Tooltip("화면 우측에서 사회자의 상반신만 보이는 Mask 영역입니다.")]
    [SerializeField] private Vector2 presenterViewportSize = new(560f, 610f);
    [SerializeField] private Vector2 presenterViewportOffset = new(-4f, 54f);
    [Tooltip("원본 전신 Sprite를 크게 넣고 Mask로 하체를 잘라 상반신 구도를 만듭니다.")]
    [SerializeField] private Vector2 presenterSourceSize = new(650f, 980f);
    [SerializeField] private Vector2 presenterSourceOffset = new(0f, -6f);
    [SerializeField, Min(0f)] private float presenterHiddenOffsetX = 220f;
    [SerializeField, Range(0f, 0.04f)] private float presenterIdleScale = 0.008f;
    [SerializeField, Range(0f, 8f)] private float presenterIdlePixels = 2.5f;

    [Header("Dialogue")]
    [SerializeField] private Vector2 dialogueSize = new(1050f, 164f);
    [SerializeField] private Vector2 dialogueVisibleOffset = new(-250f, 32f);
    [SerializeField, Min(0f)] private float dialogueHiddenOffsetY = 64f;
    [SerializeField, Min(0.03f)] private float dialogueOpenDuration = 0.14f;
    [SerializeField, Min(0.03f)] private float dialogueCloseDuration = 0.10f;
    [SerializeField, Min(1f)] private float typeCharactersPerSecond = 34f;
    [SerializeField, Min(0.1f)] private float dialogueIdleDuration = 1.65f;

    [Header("Show Camera")]
    [SerializeField, Range(1f, 1.35f)] private float showCameraZoomOut = 1.14f;
    [Tooltip("카메라를 오른쪽으로 옮겨 World TV를 화면 왼쪽으로 밀고 Presenter 공간을 만듭니다.")]
    [SerializeField, Range(0f, 2f)] private float cameraRightBiasWorld = 0.68f;

    [Header("Minimal Theme")]
    [SerializeField] private Color dialogueBack = new(0.012f, 0.014f, 0.020f, 0.91f);
    [SerializeField] private Color nameBack = new(1f, 0.82f, 0.10f, 0.96f);
    [SerializeField] private Color accent = new(1f, 0.18f, 0.36f, 1f);
    [SerializeField] private Color white = new(0.97f, 0.98f, 1f, 1f);
    [SerializeField] private Color darkInk = new(0.035f, 0.035f, 0.045f, 1f);
    [SerializeField] private Color muted = new(0.68f, 0.71f, 0.77f, 1f);

    private BattleRunManager runManager;
    private BattleShowWorldSetController showWorld;
    private BattleRunState lastState = (BattleRunState)(-1);
    private Mode mode;

    private Canvas overlayCanvas;
    private RectTransform overlayRoot;

    private CanvasGroup presenterGroup;
    private RectTransform presenterViewport;
    private RectTransform presenterImageRect;
    private Image presenterImage;
    private float presenterBlend;

    private CanvasGroup dialogueGroup;
    private RectTransform dialogueRect;
    private RectTransform namePlateRect;
    private Text nameText;
    private Text contextText;
    private Text dialogueText;
    private Text liveText;

    private bool requestedPresenterVisible;
    private float showAt;
    private bool cameraApplied;
    private float reactionStartedAt = -10f;

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
            lastState = state;
            HandleState(state);
        }

        if (mode != Mode.None)
        {
            EnsureOverlay();
            UpdatePresenterSprite();
            ApplyPrototypeCamera();
        }

        UpdatePresenterMotion();
        UpdateDialogueState();
    }

    // ---------------------------------------------------------------------
    // Disposable external hooks
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
            $"{equipment.rarity.ToString().ToUpperInvariant()}  /  {equipment.GetDisplayName()}",
            "좋아요, 그걸로 가죠. 장착되는 모습까지 한번 보시죠!",
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
            $"ROUTE LOCKED  /  STAGE {Mathf.Max(1, node.depth + 1):00}",
            "좋습니다! 다음 방송 코스, 이쪽으로 가보죠!",
            Mood.Excited);
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
    }

    private void HandleState(BattleRunState state)
    {
        RestoreCamera();

        if (state == BattleRunState.Reward)
        {
            mode = Mode.Reward;
            BeginShow();
            QueueCopy(
                "LIVE SHOP",
                "TODAY'S PICK",
                "자, 오늘 들어온 물건들을 한번 살펴볼까요?",
                Mood.Neutral);
            return;
        }

        if (state == BattleRunState.SelectingNode)
        {
            mode = Mode.Map;
            BeginShow();
            QueueCopy(
                "ROUTE DESK",
                "NEXT STAGE",
                "다음 코스를 정할 시간이에요. 어느 쪽이 더 그림이 좋을까요?",
                Mood.Curious);
            return;
        }

        mode = Mode.None;
        requestedPresenterVisible = false;
        hasPendingCopy = false;

        if (dialoguePhase != DialoguePhase.Hidden)
        {
            dialoguePhase = DialoguePhase.Closing;
            dialoguePhaseTime = 0f;
        }
    }

    private void BeginShow()
    {
        EnsureOverlay();
        requestedPresenterVisible = false;
        showAt = Time.unscaledTime + showDelay;
        reactionStartedAt = Time.unscaledTime;
    }

    // ---------------------------------------------------------------------
    // Screen overlay
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
        BuildPresenterUpperBody();
    }

    private void BuildPresenterUpperBody()
    {
        GameObject viewportObject = new("PresenterUpperBodyViewport", typeof(RectTransform));
        viewportObject.transform.SetParent(overlayRoot, false);

        presenterViewport = viewportObject.GetComponent<RectTransform>();
        presenterViewport.anchorMin = presenterViewport.anchorMax = new Vector2(1f, 0.5f);
        presenterViewport.pivot = new Vector2(1f, 0.5f);
        presenterViewport.sizeDelta = presenterViewportSize;
        presenterViewport.anchoredPosition =
            presenterViewportOffset + Vector2.right * presenterHiddenOffsetX;

        viewportObject.AddComponent<RectMask2D>();

        presenterGroup = viewportObject.AddComponent<CanvasGroup>();
        presenterGroup.alpha = 0f;
        presenterGroup.interactable = false;
        presenterGroup.blocksRaycasts = false;

        GameObject imageObject = new("PresenterUpperBody", typeof(RectTransform));
        imageObject.transform.SetParent(presenterViewport, false);

        presenterImageRect = imageObject.GetComponent<RectTransform>();
        presenterImageRect.anchorMin = presenterImageRect.anchorMax = new Vector2(1f, 1f);
        presenterImageRect.pivot = new Vector2(1f, 1f);
        presenterImageRect.sizeDelta = presenterSourceSize;
        presenterImageRect.anchoredPosition = presenterSourceOffset;

        presenterImage = imageObject.AddComponent<Image>();
        presenterImage.preserveAspect = true;
        presenterImage.raycastTarget = false;
        presenterImage.color = Color.white;
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

        GameObject plate = new("NamePlate", typeof(RectTransform));
        plate.transform.SetParent(dialogueRect, false);

        namePlateRect = plate.GetComponent<RectTransform>();
        namePlateRect.anchorMin = namePlateRect.anchorMax = new Vector2(0f, 1f);
        namePlateRect.pivot = new Vector2(0f, 1f);
        namePlateRect.anchoredPosition = new Vector2(20f, 12f);
        namePlateRect.sizeDelta = new Vector2(222f, 40f);

        Image plateImage = plate.AddComponent<Image>();
        plateImage.color = nameBack;
        plateImage.raycastTarget = false;

        nameText = CreateText(
            namePlateRect,
            "PresenterName",
            "SHOW HOST",
            19,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            darkInk);
        Stretch(nameText.rectTransform, 6f);

        contextText = CreateText(
            dialogueRect,
            "Context",
            string.Empty,
            12,
            FontStyle.Bold,
            TextAnchor.MiddleRight,
            muted);

        Place(
            contextText.rectTransform,
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-20f, -12f),
            new Vector2(500f, 28f));

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
        textRect.offsetMin = new Vector2(42f, 20f);
        textRect.offsetMax = new Vector2(-38f, -42f);
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
            accent);

        Place(
            liveText.rectTransform,
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(-18f, 10f),
            new Vector2(110f, 20f));
    }

    // ---------------------------------------------------------------------
    // Presenter motion / idle
    // ---------------------------------------------------------------------

    private void UpdatePresenterMotion()
    {
        if (mode != Mode.None &&
            !requestedPresenterVisible &&
            Time.unscaledTime >= showAt)
        {
            requestedPresenterVisible = true;
        }

        float target = requestedPresenterVisible && mode != Mode.None && presenterViewport != null
            ? 1f
            : 0f;

        float t = 1f - Mathf.Exp(
            -Mathf.Max(0.1f, presenterSharpness) * Time.unscaledDeltaTime);

        presenterBlend = Mathf.Lerp(presenterBlend, target, t);

        if (Mathf.Abs(presenterBlend - target) < 0.001f)
            presenterBlend = target;

        if (presenterGroup != null)
            presenterGroup.alpha = presenterBlend;

        if (presenterViewport != null)
        {
            float eased = Smooth01(presenterBlend);
            presenterViewport.anchoredPosition = Vector2.Lerp(
                presenterViewportOffset + Vector2.right * presenterHiddenOffsetX,
                presenterViewportOffset,
                eased);
        }

        if (presenterImageRect != null)
        {
            float idle = Mathf.Sin(Time.unscaledTime * 1.7f);
            float idleY = idle * presenterIdlePixels * presenterBlend;
            float idleScale = 1f + idle * presenterIdleScale * presenterBlend;

            float reactionAge = Time.unscaledTime - reactionStartedAt;
            float reaction = reactionAge >= 0f && reactionAge < 0.24f
                ? Mathf.Sin(reactionAge / 0.24f * Mathf.PI) * 0.028f
                : 0f;

            presenterImageRect.anchoredPosition =
                presenterSourceOffset + Vector2.up * idleY;
            presenterImageRect.localScale =
                Vector3.one * (idleScale + reaction);
        }
    }

    // ---------------------------------------------------------------------
    // Dialogue state machine
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
        reactionStartedAt = Time.unscaledTime;

        if (dialoguePhase == DialoguePhase.Hidden)
        {
            BeginPendingDialogue();
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
                dialogueGroup.alpha = 0f;
                dialogueRect.anchoredPosition =
                    dialogueVisibleOffset - Vector2.up * dialogueHiddenOffsetY;

                if (mode != Mode.None && hasPendingCopy)
                    BeginPendingDialogue();
                break;

            case DialoguePhase.Opening:
            {
                dialoguePhaseTime += dt;
                float t = Mathf.Clamp01(
                    dialoguePhaseTime / Mathf.Max(0.03f, dialogueOpenDuration));

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
                    dialogueText.text = activeComment.Substring(0, visibleCharacters);
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
                    dialoguePhaseTime / Mathf.Max(0.03f, dialogueCloseDuration));

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

        float scaleY = Mathf.Lerp(0.92f, 1f, clamped);
        dialogueRect.localScale = new Vector3(1f, scaleY, 1f);
    }

    private void ApplyDialogueMetadata()
    {
        if (nameText != null)
            nameText.text = "SHOW HOST";

        if (contextText != null)
        {
            contextText.text = string.IsNullOrWhiteSpace(activeKeyword)
                ? activeHeader
                : $"{activeHeader}   /   {activeKeyword}";
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
                    ? accent
                    : muted;
        }
    }

    // ---------------------------------------------------------------------
    // Camera composition
    // ---------------------------------------------------------------------

    private void ApplyPrototypeCamera()
    {
        if (showWorld == null ||
            mode == Mode.None ||
            !showWorld.HasCameraAnchor)
        {
            return;
        }

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
    // Presenter sprite source
    // ---------------------------------------------------------------------

    private void UpdatePresenterSprite()
    {
        if (presenterImage == null)
            return;

        Sprite sprite = null;

        if (showWorld != null &&
            showWorld.PresenterWorldTransform != null)
        {
            SpriteRenderer renderer =
                showWorld.PresenterWorldTransform.GetComponent<SpriteRenderer>();

            if (renderer == null)
            {
                renderer =
                    showWorld.PresenterWorldTransform
                        .GetComponentInChildren<SpriteRenderer>(true);
            }

            if (renderer != null)
                sprite = renderer.sprite;
        }

        if (sprite == null)
        {
            Image[] images = FindObjectsByType<Image>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null ||
                    image == presenterImage ||
                    image.name != "Presenter" ||
                    image.sprite == null)
                {
                    continue;
                }

                sprite = image.sprite;
                break;
            }
        }

        presenterImage.sprite = sprite;
        presenterImage.enabled = sprite != null;
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
                "ODD WEAPON / MULTI HIT",
                "그래요, 이 톱날은 꽤나 컬트한 매력이 있을지도요!?",
                Mood.Curious);
            return;
        }

        if (equipment.HasTag(EquipmentTag.OddWeapon))
        {
            QueueCopy(
                selected ? "PICKED" : "CULT PICK",
                "ODD WEAPON",
                "호불호는 좀 있겠지만요. 이런 물건을 좋아하는 분은 정말 좋아하시겠네요?",
                Mood.Curious);
            return;
        }

        if (equipment.rarity == EquipmentRarity.Unique ||
            equipment.rarity == EquipmentRarity.Epic)
        {
            QueueCopy(
                selected ? "PICKED" : "SPECIAL ITEM",
                $"{equipment.rarity.ToString().ToUpperInvariant()} / {displayName}",
                "오, 이건 화면에 잡힐 만하네요. 오늘 상품 중에서는 확실히 눈에 띕니다!",
                Mood.Excited);
            return;
        }

        if (equipment.HasTag(EquipmentTag.Explosion) ||
            equipment.HasTag(EquipmentTag.Burn))
        {
            QueueCopy(
                selected ? "PICKED" : "HOT ITEM",
                "EXPLOSIVE / PRESSURE",
                "이건 설명이 필요 없겠네요. 화끈한 쪽을 좋아하신다면 꽤 괜찮은 선택이에요!",
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
                "화려하진 않아도 오래 살아남는 건 꽤 중요한 일이죠. 안정적인 상품입니다.",
                Mood.Neutral);
            return;
        }

        QueueCopy(
            selected ? "PICKED" : "ITEM CHECK",
            $"{equipment.rarity.ToString().ToUpperInvariant()} / {displayName}",
            selected
                ? "좋아요. 일단 이쪽을 좀 더 자세히 보죠."
                : "음, 무난해 보이지만 조합에 따라 제법 재미있는 그림이 나올지도요.",
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
                    "조금 거친 코스네요. 대신 방송 분량은 확실하겠어요.",
                    Mood.Concerned);
                break;

            case BattleNodeType.Shop:
                QueueCopy(
                    "SHOPPING BREAK",
                    $"{stage} / SHOP",
                    "잠깐 쇼핑 타임이군요. 다음 싸움 전에 지갑부터 한번 열어볼까요?",
                    Mood.Curious);
                break;

            case BattleNodeType.Event:
                QueueCopy(
                    "SPECIAL SEGMENT",
                    $"{stage} / EVENT",
                    "이쪽은 무슨 일이 나올지 모르겠네요. 방송적으로는 꽤 흥미롭겠어요.",
                    Mood.Curious);
                break;

            default:
                QueueCopy(
                    "COURSE CHECK",
                    $"{stage} / COMBAT",
                    stars >= 4
                        ? "난도가 꽤 높네요. 그래도 이 정도는 가야 그림이 나오겠죠?"
                        : "정석적인 코스네요. 다음 전투를 보기엔 무난한 선택입니다.",
                    stars >= 4 ? Mood.Concerned : Mood.Neutral);
                break;
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
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

    private static void Stretch(RectTransform rect, float padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }
}

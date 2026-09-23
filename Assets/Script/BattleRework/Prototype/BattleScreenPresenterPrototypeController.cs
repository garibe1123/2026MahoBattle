using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Disposable full-screen presenter prototype.
///
/// World-space TV / field presenter stay untouched.
/// Reward / Map only send one-line notifications here; this component renders a separate
/// Screen-Space Overlay inspired by shopping TV + visual-novel presenter cut-ins.
///
/// Remove this file and the Notify... call sites to remove the experiment.
/// </summary>
[DefaultExecutionOrder(70000)]
[DisallowMultipleComponent]
public sealed class BattleScreenPresenterPrototypeController : MonoBehaviour
{
    private enum Mode { None, Reward, Map }
    private enum Mood { Neutral, Curious, Excited, Concerned }

    private static BattleScreenPresenterPrototypeController instance;

    [Header("Screen Overlay")]
    [SerializeField] private int overlaySortingOrder = 470;
    [SerializeField] private Vector2 referenceResolution = new(1920f, 1080f);
    [SerializeField, Min(0.01f)] private float showDelay = 0.12f;
    [SerializeField, Min(0.1f)] private float fadeSharpness = 11f;

    [Header("Presenter Cutout")]
    [SerializeField] private Vector2 presenterSize = new(520f, 760f);
    [SerializeField] private Vector2 presenterVisibleOffset = new(-18f, -8f);
    [SerializeField, Min(0f)] private float presenterHiddenOffsetX = 170f;

    [Header("Dialogue")]
    [SerializeField] private Vector2 dialogueSize = new(1120f, 188f);
    [SerializeField] private Vector2 dialogueVisibleOffset = new(-245f, 34f);
    [SerializeField, Min(0f)] private float dialogueHiddenOffsetY = 82f;

    [Header("Shopping Info")]
    [SerializeField] private Vector2 infoSize = new(420f, 238f);
    [SerializeField] private Vector2 infoVisibleOffset = new(-430f, -118f);
    [SerializeField, Min(0f)] private float infoHiddenOffsetX = 100f;

    [Header("Show Camera")]
    [SerializeField, Range(1f, 1.45f)] private float showCameraZoomOut = 1.18f;
    [Tooltip("카메라를 오른쪽으로 이동시키면 월드 TV가 화면 왼쪽으로 밀려 Presenter용 여백이 생깁니다.")]
    [SerializeField, Range(0f, 2f)] private float cameraRightBiasWorld = 0.72f;

    [Header("Theme")]
    [SerializeField] private Color dialogueBack = new(0.012f, 0.014f, 0.020f, 0.97f);
    [SerializeField] private Color infoBack = new(0.96f, 0.96f, 0.93f, 0.96f);
    [SerializeField] private Color darkInk = new(0.035f, 0.035f, 0.045f, 1f);
    [SerializeField] private Color accent = new(1f, 0.18f, 0.36f, 1f);
    [SerializeField] private Color yellow = new(1f, 0.83f, 0.08f, 1f);
    [SerializeField] private Color white = new(0.97f, 0.98f, 1f, 1f);
    [SerializeField] private Color muted = new(0.70f, 0.72f, 0.77f, 1f);

    private BattleRunManager runManager;
    private BattleShowWorldSetController showWorld;
    private BattleRunState lastState = (BattleRunState)(-1);
    private Mode mode;

    private Canvas overlayCanvas;
    private CanvasGroup overlayGroup;
    private RectTransform overlayRoot;

    private RectTransform presenterRect;
    private Image presenterImage;

    private RectTransform dialogueRect;
    private Text nameText;
    private Text dialogueText;

    private RectTransform infoRect;
    private Text liveText;
    private Text headerText;
    private Text keywordText;
    private Text reactionText;

    private float visibleBlend;
    private float showAt;
    private float reactionStartedAt = -10f;
    private bool requestedVisible;
    private bool cameraApplied;

    private string currentHeader = "LIVE SHOP";
    private string currentKeyword = "TODAY'S PICK";
    private string currentComment = string.Empty;
    private Mood currentMood = Mood.Neutral;

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

        UpdateOverlayMotion();
    }

    // ---------------------------------------------------------------------
    // Thin disposable hooks
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

        owner.SetCopy(
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

        owner.SetCopy(
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
    // State
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
            SetCopy(
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
            SetCopy(
                "ROUTE DESK",
                "NEXT STAGE",
                "다음 코스를 정할 시간이에요. 어느 쪽이 더 그림이 좋을까요?",
                Mood.Curious);
            return;
        }

        mode = Mode.None;
        requestedVisible = false;
    }

    private void BeginShow()
    {
        requestedVisible = false;
        showAt = Time.unscaledTime + showDelay;
        reactionStartedAt = Time.unscaledTime;
    }

    // ---------------------------------------------------------------------
    // Screen-space layout
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

        overlayGroup = canvasObject.AddComponent<CanvasGroup>();
        overlayGroup.alpha = 0f;
        overlayGroup.interactable = false;
        overlayGroup.blocksRaycasts = false;

        overlayRoot = canvasObject.GetComponent<RectTransform>();

        BuildInfoPanel();
        BuildDialoguePanel();
        BuildPresenterCutout();
        ApplyCachedCopy();
    }

    private void BuildPresenterCutout()
    {
        GameObject go = new("PresenterCutout", typeof(RectTransform));
        go.transform.SetParent(overlayRoot, false);

        presenterRect = go.GetComponent<RectTransform>();
        presenterRect.anchorMin = presenterRect.anchorMax = new Vector2(1f, 0f);
        presenterRect.pivot = new Vector2(1f, 0f);
        presenterRect.sizeDelta = presenterSize;
        presenterRect.anchoredPosition = presenterVisibleOffset + Vector2.right * presenterHiddenOffsetX;

        presenterImage = go.AddComponent<Image>();
        presenterImage.preserveAspect = true;
        presenterImage.raycastTarget = false;
        presenterImage.color = Color.white;

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(yellow.r, yellow.g, yellow.b, 0.92f);
        outline.effectDistance = new Vector2(4f, -4f);

        // Presenter should sit in front of the dialogue, Persona-style.
        presenterRect.SetAsLastSibling();
    }

    private void BuildDialoguePanel()
    {
        GameObject go = new("PresenterDialogue", typeof(RectTransform));
        go.transform.SetParent(overlayRoot, false);

        dialogueRect = go.GetComponent<RectTransform>();
        dialogueRect.anchorMin = dialogueRect.anchorMax = new Vector2(0.5f, 0f);
        dialogueRect.pivot = new Vector2(0.5f, 0f);
        dialogueRect.sizeDelta = dialogueSize;
        dialogueRect.anchoredPosition = dialogueVisibleOffset - Vector2.up * dialogueHiddenOffsetY;
        dialogueRect.localRotation = Quaternion.Euler(0f, 0f, -1.2f);

        Image back = go.AddComponent<Image>();
        back.color = dialogueBack;
        back.raycastTarget = false;

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = white;
        outline.effectDistance = new Vector2(4f, -4f);

        // Yellow TV-shopping wedge / name plate.
        GameObject plate = new("NamePlate", typeof(RectTransform));
        plate.transform.SetParent(dialogueRect, false);

        RectTransform plateRect = plate.GetComponent<RectTransform>();
        plateRect.anchorMin = plateRect.anchorMax = new Vector2(0f, 1f);
        plateRect.pivot = new Vector2(0f, 1f);
        plateRect.anchoredPosition = new Vector2(18f, 14f);
        plateRect.sizeDelta = new Vector2(240f, 46f);

        Image plateImage = plate.AddComponent<Image>();
        plateImage.color = yellow;
        plateImage.raycastTarget = false;

        nameText = CreateText(
            plateRect,
            "PresenterName",
            "SHOW HOST",
            21,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            darkInk);
        Stretch(nameText.rectTransform, 8f);

        dialogueText = CreateText(
            dialogueRect,
            "Dialogue",
            string.Empty,
            27,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            white);

        RectTransform textRect = dialogueText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(44f, 24f);
        textRect.offsetMax = new Vector2(-54f, -46f);
        dialogueText.horizontalOverflow = HorizontalWrapMode.Wrap;
        dialogueText.verticalOverflow = VerticalWrapMode.Truncate;
        dialogueText.lineSpacing = 1.04f;

        GameObject accentStrip = new("DialogueAccent", typeof(RectTransform));
        accentStrip.transform.SetParent(dialogueRect, false);

        RectTransform accentRect = accentStrip.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(1f, 0f);
        accentRect.pivot = new Vector2(0.5f, 0f);
        accentRect.sizeDelta = new Vector2(0f, 8f);

        Image accentImage = accentStrip.AddComponent<Image>();
        accentImage.color = accent;
        accentImage.raycastTarget = false;
    }

    private void BuildInfoPanel()
    {
        GameObject go = new("HomeShoppingInfo", typeof(RectTransform));
        go.transform.SetParent(overlayRoot, false);

        infoRect = go.GetComponent<RectTransform>();
        infoRect.anchorMin = infoRect.anchorMax = new Vector2(1f, 1f);
        infoRect.pivot = new Vector2(1f, 1f);
        infoRect.sizeDelta = infoSize;
        infoRect.anchoredPosition = infoVisibleOffset + Vector2.right * infoHiddenOffsetX;

        Image back = go.AddComponent<Image>();
        back.color = infoBack;
        back.raycastTarget = false;

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(darkInk.r, darkInk.g, darkInk.b, 0.85f);
        outline.effectDistance = new Vector2(3f, -3f);

        GameObject top = new("TopBand", typeof(RectTransform));
        top.transform.SetParent(infoRect, false);

        RectTransform topRect = top.GetComponent<RectTransform>();
        topRect.anchorMin = new Vector2(0f, 1f);
        topRect.anchorMax = new Vector2(1f, 1f);
        topRect.pivot = new Vector2(0.5f, 1f);
        topRect.sizeDelta = new Vector2(0f, 38f);

        Image topImage = top.AddComponent<Image>();
        topImage.color = accent;
        topImage.raycastTarget = false;

        liveText = CreateText(
            topRect,
            "OnLive",
            "[ON LIVE]",
            13,
            FontStyle.Bold,
            TextAnchor.MiddleRight,
            white);
        Stretch(liveText.rectTransform, 12f);

        headerText = CreateText(
            infoRect,
            "Header",
            "LIVE SHOP",
            22,
            FontStyle.Bold,
            TextAnchor.UpperLeft,
            darkInk);
        Place(headerText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -54f), new Vector2(360f, 34f));

        keywordText = CreateText(
            infoRect,
            "Keyword",
            "TODAY'S PICK",
            16,
            FontStyle.Bold,
            TextAnchor.UpperLeft,
            new Color(0.48f, 0.38f, 0.02f, 1f));
        Place(keywordText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -92f), new Vector2(370f, 60f));
        keywordText.horizontalOverflow = HorizontalWrapMode.Wrap;
        keywordText.verticalOverflow = VerticalWrapMode.Truncate;

        reactionText = CreateText(
            infoRect,
            "Reaction",
            "ON AIR",
            14,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            accent);
        Place(reactionText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 18f), new Vector2(220f, 28f));

        Text footer = CreateText(
            infoRect,
            "Footer",
            "MAHO SHOPPING CHANNEL  •  SPECIAL LIVE",
            10,
            FontStyle.Normal,
            TextAnchor.LowerRight,
            muted);
        Place(footer.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 16f), new Vector2(280f, 24f));
    }

    private void UpdateOverlayMotion()
    {
        if (mode != Mode.None && !requestedVisible && Time.unscaledTime >= showAt)
            requestedVisible = true;

        float target = requestedVisible && mode != Mode.None && overlayRoot != null ? 1f : 0f;
        float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, fadeSharpness) * Time.unscaledDeltaTime);
        visibleBlend = Mathf.Lerp(visibleBlend, target, t);

        if (Mathf.Abs(visibleBlend - target) < 0.001f)
            visibleBlend = target;

        if (overlayGroup != null)
            overlayGroup.alpha = visibleBlend;

        float eased = visibleBlend * visibleBlend * (3f - 2f * visibleBlend);

        if (presenterRect != null)
        {
            presenterRect.anchoredPosition = Vector2.Lerp(
                presenterVisibleOffset + Vector2.right * presenterHiddenOffsetX,
                presenterVisibleOffset,
                eased);

            float age = Time.unscaledTime - reactionStartedAt;
            float pulse = age >= 0f && age < 0.30f
                ? Mathf.Sin(age / 0.30f * Mathf.PI) * 0.055f
                : 0f;
            presenterRect.localScale = Vector3.one * (1f + pulse);
        }

        if (dialogueRect != null)
        {
            dialogueRect.anchoredPosition = Vector2.Lerp(
                dialogueVisibleOffset - Vector2.up * dialogueHiddenOffsetY,
                dialogueVisibleOffset,
                eased);
        }

        if (infoRect != null)
        {
            infoRect.anchoredPosition = Vector2.Lerp(
                infoVisibleOffset + Vector2.right * infoHiddenOffsetX,
                infoVisibleOffset,
                eased);
        }
    }

    // ---------------------------------------------------------------------
    // Camera composition
    // ---------------------------------------------------------------------

    private void ApplyPrototypeCamera()
    {
        if (showWorld == null || mode == Mode.None || !showWorld.HasCameraAnchor)
            return;

        // Recompute every frame so this never compounds and so Map/Reward owner changes remain authoritative.
        showWorld.RecomputeSharedCameraFrame();

        Vector3 target = showWorld.CameraTargetWorld + Vector3.right * cameraRightBiasWorld;
        float size = showWorld.ShowCameraSize * Mathf.Max(1f, showCameraZoomOut);

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
    // Presenter source
    // ---------------------------------------------------------------------

    private void UpdatePresenterSprite()
    {
        if (presenterImage == null)
            return;

        Sprite sprite = null;

        // Prefer the physical field presenter when it is currently visible.
        if (showWorld != null && showWorld.PresenterWorldTransform != null)
        {
            SpriteRenderer renderer = showWorld.PresenterWorldTransform.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = showWorld.PresenterWorldTransform.GetComponentInChildren<SpriteRenderer>(true);

            if (renderer != null)
                sprite = renderer.sprite;
        }

        // Fallback to the legacy presenter image so Map can still show the broadcast cutout
        // even when the physical presenter has already left the stage.
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
    // Copy
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
            SetCopy(
                selected ? "PICKED" : "CURIOUS PICK",
                "ODD WEAPON  /  MULTI HIT",
                "그래요, 이 톱날은 꽤나 컬트한 매력이 있을지도요!?",
                Mood.Curious);
            return;
        }

        if (equipment.HasTag(EquipmentTag.OddWeapon))
        {
            SetCopy(
                selected ? "PICKED" : "CULT PICK",
                "ODD WEAPON",
                "호불호는 좀 있겠지만요. 이런 물건을 좋아하는 분은 정말 좋아하시겠네요?",
                Mood.Curious);
            return;
        }

        if (equipment.rarity == EquipmentRarity.Unique ||
            equipment.rarity == EquipmentRarity.Epic)
        {
            SetCopy(
                selected ? "PICKED" : "SPECIAL ITEM",
                $"{equipment.rarity.ToString().ToUpperInvariant()}  /  {displayName}",
                "오, 이건 화면에 잡힐 만하네요. 오늘 상품 중에서는 확실히 눈에 띕니다!",
                Mood.Excited);
            return;
        }

        if (equipment.HasTag(EquipmentTag.Explosion) ||
            equipment.HasTag(EquipmentTag.Burn))
        {
            SetCopy(
                selected ? "PICKED" : "HOT ITEM",
                "EXPLOSIVE  /  PRESSURE",
                "이건 설명이 필요 없겠네요. 화끈한 쪽을 좋아하신다면 꽤 괜찮은 선택이에요!",
                Mood.Excited);
            return;
        }

        if (equipment.HasTag(EquipmentTag.Heal) ||
            equipment.HasTag(EquipmentTag.Defense) ||
            equipment.HasTag(EquipmentTag.Sustain))
        {
            SetCopy(
                selected ? "PICKED" : "SAFE PICK",
                "SURVIVAL  /  STABILITY",
                "화려하진 않아도 오래 살아남는 건 꽤 중요한 일이죠. 안정적인 상품입니다.",
                Mood.Neutral);
            return;
        }

        SetCopy(
            selected ? "PICKED" : "ITEM CHECK",
            $"{equipment.rarity.ToString().ToUpperInvariant()}  /  {displayName}",
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
            $"STAGE {Mathf.Max(1, node.depth + 1):00}  /  {Mathf.Clamp(stars, 1, 5)} STAR";

        switch (node.type)
        {
            case BattleNodeType.Elite:
                SetCopy(
                    "CAUTION",
                    $"{stage}  /  ELITE",
                    "조금 거친 코스네요. 대신 방송 분량은 확실하겠어요.",
                    Mood.Concerned);
                break;

            case BattleNodeType.Shop:
                SetCopy(
                    "SHOPPING BREAK",
                    $"{stage}  /  SHOP",
                    "잠깐 쇼핑 타임이군요. 다음 싸움 전에 지갑부터 한번 열어볼까요?",
                    Mood.Curious);
                break;

            case BattleNodeType.Event:
                SetCopy(
                    "SPECIAL SEGMENT",
                    $"{stage}  /  EVENT",
                    "이쪽은 무슨 일이 나올지 모르겠네요. 방송적으로는 꽤 흥미롭겠어요.",
                    Mood.Curious);
                break;

            default:
                SetCopy(
                    "COURSE CHECK",
                    $"{stage}  /  COMBAT",
                    stars >= 4
                        ? "난도가 꽤 높네요. 그래도 이 정도는 가야 그림이 나오겠죠?"
                        : "정석적인 코스네요. 다음 전투를 보기엔 무난한 선택입니다.",
                    stars >= 4 ? Mood.Concerned : Mood.Neutral);
                break;
        }
    }

    private void SetCopy(string header, string keyword, string comment, Mood mood)
    {
        currentHeader = header ?? string.Empty;
        currentKeyword = keyword ?? string.Empty;
        currentComment = comment ?? string.Empty;
        currentMood = mood;

        EnsureOverlay();
        ApplyCachedCopy();
        reactionStartedAt = Time.unscaledTime;
    }

    private void ApplyCachedCopy()
    {
        if (headerText != null)
            headerText.text = currentHeader;

        if (keywordText != null)
            keywordText.text = currentKeyword;

        if (dialogueText != null)
            dialogueText.text = currentComment;

        if (reactionText == null)
            return;

        reactionText.text = currentMood switch
        {
            Mood.Curious => "CURIOUS PICK",
            Mood.Excited => "HOT PICK!",
            Mood.Concerned => "CAUTION",
            _ => "ON AIR"
        };

        reactionText.color = currentMood == Mood.Concerned
            ? new Color(0.88f, 0.30f, 0.08f, 1f)
            : currentMood == Mood.Excited
                ? accent
                : new Color(0.45f, 0.36f, 0.02f, 1f);
    }

    // ---------------------------------------------------------------------
    // UI helpers
    // ---------------------------------------------------------------------

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

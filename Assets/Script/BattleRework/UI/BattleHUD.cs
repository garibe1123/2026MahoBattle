using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Runtime broadcast HUD.
/// Combat keeps a compact status/loadout HUD.
/// Reward is staged like a quiz show: the real Player remains visible in the lower-left foreground,
/// one medium-large prize screen occupies the upper/right background, one presenter overlaps its right edge,
/// and the current loadout is shown as a thin drop strip below the screen.
/// Clicking a prize only selects it; dropping it on an unlocked slot confirms acquisition.
/// Hovering/changing a prize pulses soft bird-eye floor spotlights under Player and Presenter.
/// </summary>
public sealed class BattleHUD : MonoBehaviour
{
    private static BattleHUD instance;

    [Header("Runtime Style")]
    [SerializeField] private Color panelColor = new(0.022f, 0.028f, 0.043f, 0.94f);
    [SerializeField] private Color accentColor = new(1f, 0.18f, 0.58f, 1f);
    [SerializeField] private Color goldColor = new(1f, 0.80f, 0.25f, 1f);
    [SerializeField] private Color hpColor = new(0.95f, 0.22f, 0.34f, 1f);
    [SerializeField] private Color staminaColor = new(0.24f, 0.80f, 0.93f, 1f);

    [Header("Reward Show")]
    [Tooltip("Presenter artwork. Leave empty to use a plain Sprite-Default placeholder, then replace it from Inspector or SetPresenterSprite().")]
    [SerializeField] private Sprite presenterSprite;
    [SerializeField] private Color presenterColor = new(0.34f, 0.34f, 0.40f, 1f);
    [SerializeField] private Vector2 presenterAnchor = new(0.88f, 0.49f);
    [SerializeField] private Vector2 presenterSize = new(370f, 600f);
    [SerializeField] private Vector2 presenterOffset = Vector2.zero;
    [SerializeField] private bool presenterFlipX;
    [SerializeField] private Color rewardFieldFilter = new(0.06f, 0.035f, 0.11f, 0.025f);
    [Tooltip("Background prize display. Intentionally leaves the lower-left Player area unobstructed.")]
    [SerializeField] private Vector2 rewardScreenSize = new(1120f, 560f);
    [SerializeField] private Vector2 rewardScreenAnchor = new(0.61f, 0.69f);
    [SerializeField] private Vector2 rewardLoadoutSize = new(1120f, 150f);
    [SerializeField] private Vector2 rewardLoadoutAnchor = new(0.61f, 0.145f);
    [SerializeField, Range(1.02f, 1.30f)] private float rewardHoverScale = 1.10f;
    [SerializeField, Min(0f)] private float rewardHoverLift = 12f;

    [Header("Reward Floor Spotlights")]
    [Tooltip("Soft floor glow only. No vertical cone/beam is drawn.")]
    [SerializeField] private Color playerSpotlightColor = new(1f, 0.93f, 0.66f, 0.10f);
    [SerializeField] private Color presenterSpotlightColor = new(0.74f, 0.92f, 1f, 0.10f);
    [SerializeField, Range(0.08f, 0.65f)] private float spotlightPeakAlpha = 0.34f;
    [SerializeField, Min(0.05f)] private float spotlightAttack = 0.09f;
    [SerializeField, Min(0.05f)] private float spotlightRelease = 0.34f;
    [Tooltip("Bird-eye floor ellipse. X should be wider than Y.")]
    [SerializeField] private Vector2 playerSpotlightSize = new(330f, 126f);
    [Tooltip("Bird-eye floor ellipse. X should be wider than Y.")]
    [SerializeField] private Vector2 presenterSpotlightSize = new(410f, 154f);
    [SerializeField] private Vector2 playerSpotlightScreenOffset = new(0f, -18f);
    [SerializeField] private Vector2 presenterSpotlightOffset = new(0f, -278f);

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RunProgressSystem progress;
    private BattleEquipmentSystem equipmentSystem;
    private PlayerController player;

    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private GameObject combatStatusRoot;
    private GameObject equipmentDockRoot;
    private Text stageText;
    private Text enemyText;
    private Text hpText;
    private Text staminaText;
    private Image hpFill;
    private Image staminaFill;
    private Text audienceText;

    private readonly Image[] slotBackgrounds = new Image[BattleEquipmentSystem.MaxSlotCount];
    private readonly Image[] slotIcons = new Image[BattleEquipmentSystem.MaxSlotCount];
    private readonly Text[] slotLabels = new Text[BattleEquipmentSystem.MaxSlotCount];
    private readonly Text[] slotGrades = new Text[BattleEquipmentSystem.MaxSlotCount];

    private GameObject rewardRoot;
    private RectTransform rewardCardRoot;
    private RectTransform rewardInventoryRoot;
    private RectTransform mapSelectionRoot;
    private GameObject rewardInventoryPanel;
    private GameObject rewardNoticePanel;
    private Text rewardTitle;
    private Text rewardSubtitle;
    private Text rewardInstruction;
    private Text focusedRewardName;
    private Text focusedRewardStats;
    private Image presenterImage;
    private RectTransform presenterRect;
    private Image playerSpotlightImage;
    private Image presenterSpotlightImage;

    private readonly Image[] rewardSlotBackgrounds = new Image[BattleEquipmentSystem.MaxSlotCount];
    private readonly Image[] rewardSlotIcons = new Image[BattleEquipmentSystem.MaxSlotCount];
    private readonly Text[] rewardSlotNames = new Text[BattleEquipmentSystem.MaxSlotCount];
    private readonly Text[] rewardSlotGrades = new Text[BattleEquipmentSystem.MaxSlotCount];
    private readonly Text[] rewardSlotActions = new Text[BattleEquipmentSystem.MaxSlotCount];

    private int pendingRewardIndex = -1;
    private int lastRewardCount = -1;
    private BattleRunState lastObservedState = (BattleRunState)(-1);
    private float nextSlowRefresh;
    private bool legacyDummyOverlaysDisabled;

    private GameObject rewardDragGhost;
    private RectTransform rewardDragGhostRect;

    /// <summary>맵 선택 노드가 아이템 선택과 동일한 토크쇼 TV 안에 그려지는 전용 영역입니다.</summary>
    public RectTransform MapSelectionRoot => mapSelectionRoot;

    private static readonly Color RewardCardColor = new(0.095f, 0.080f, 0.155f, 1f);
    private static readonly Color RewardCardSelectedColor = new(0.22f, 0.075f, 0.19f, 1f);
    private static readonly Color RewardEmptySlotColor = new(0.055f, 0.095f, 0.12f, 1f);
    private static readonly Color RewardOccupiedSlotColor = new(0.070f, 0.075f, 0.105f, 1f);
    private static readonly Color RewardReplaceSlotColor = new(0.18f, 0.055f, 0.07f, 1f);
    private static readonly Color RewardMergeSlotColor = new(0.07f, 0.15f, 0.13f, 1f);
    private static readonly Color RewardLockedSlotColor = new(0.025f, 0.028f, 0.038f, 0.84f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleHUD>() != null)
            return;

        GameObject host = new("BattleBroadcastHUDRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleHUD>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            return;
        }

        instance = this;
        EnsureCanvas();
    }

    private void OnDestroy()
    {
        EndRewardDrag();
        KillSpotlightTweens();
        if (instance == this)
            instance = null;
    }

    private void OnEnable()
    {
        ResolveSystems();
        SubscribeEquipment();
    }

    private void OnDisable()
    {
        UnsubscribeEquipment();
        SetRewardVisible(false);
    }

    private void Update()
    {
        ResolveSystems();
        DisableLegacyDummyOverlays();
        if (canvas == null)
            EnsureCanvas();

        bool active = runManager != null && runManager.RunActive;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = active ? 1f : 0f;
            canvasGroup.blocksRaycasts = active;
            canvasGroup.interactable = active;
        }

        if (!active)
        {
            SetRewardVisible(false);
            return;
        }

        RefreshVitalBars();
        RefreshRewardState();

        if (Time.unscaledTime >= nextSlowRefresh)
        {
            nextSlowRefresh = Time.unscaledTime + 0.12f;
            RefreshStatus();
            RefreshEquipment();
        }
    }

    private void LateUpdate()
    {
        UpdateSpotlightPositions();
    }

    public void SetPresenterSprite(Sprite sprite)
    {
        presenterSprite = sprite;
        ApplyPresenterVisual();
    }

    public void SetPresenterColor(Color color)
    {
        presenterColor = color;
        ApplyPresenterVisual();
    }

    public void SetPresenterLayout(Vector2 anchor, Vector2 size, Vector2 offset, bool flipX = false)
    {
        presenterAnchor = anchor;
        presenterSize = size;
        presenterOffset = offset;
        presenterFlipX = flipX;
        ApplyPresenterVisual();
        UpdatePresenterSpotlightPosition();
    }

    private void ResolveSystems()
    {
        if (runManager == null) runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null) roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (progress == null) progress = FindFirstObjectByType<RunProgressSystem>();
        if (player == null) player = FindFirstObjectByType<PlayerController>();

        if (equipmentSystem == null)
        {
            BattleEquipmentSystem found = FindFirstObjectByType<BattleEquipmentSystem>();
            if (found != null)
            {
                UnsubscribeEquipment();
                equipmentSystem = found;
                SubscribeEquipment();
                RefreshEquipment();
                RefreshRewardInventory();
            }
        }
    }

    private void DisableLegacyDummyOverlays()
    {
        if (legacyDummyOverlaysDisabled)
            return;

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null) continue;
            string typeName = behaviour.GetType().Name;
            if (typeName == "BattleDummyUI" || typeName == "BattleDummyLoadoutUI" || typeName == "SynergyDummyUI")
                behaviour.enabled = false;
        }
        legacyDummyOverlaysDisabled = true;
    }

    private void SubscribeEquipment()
    {
        if (equipmentSystem == null)
            return;
        equipmentSystem.InventoryChanged -= HandleInventoryChanged;
        equipmentSystem.InventoryChanged += HandleInventoryChanged;
        equipmentSystem.SlotCapacityChanged -= HandleSlotCapacityChanged;
        equipmentSystem.SlotCapacityChanged += HandleSlotCapacityChanged;
    }

    private void UnsubscribeEquipment()
    {
        if (equipmentSystem == null)
            return;
        equipmentSystem.InventoryChanged -= HandleInventoryChanged;
        equipmentSystem.SlotCapacityChanged -= HandleSlotCapacityChanged;
    }

    private void HandleInventoryChanged()
    {
        RefreshEquipment();
        RefreshRewardInventory();
    }

    private void HandleSlotCapacityChanged(int _)
    {
        RefreshEquipment();
        RefreshRewardInventory();
    }

    private void EnsureCanvas()
    {
        if (canvas != null)
            return;

        EnsureEventSystem();

        GameObject canvasObject = new("BattleBroadcastHUDCanvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        canvasGroup = canvasObject.AddComponent<CanvasGroup>();

        BuildTopStatus();
        BuildEquipmentDock();
        BuildRewardShow();
        RefreshStatus();
        RefreshEquipment();
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject go = new("BattleUIEventSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    private void BuildTopStatus()
    {
        combatStatusRoot = CreatePanel(canvas.transform, "BroadcastStatus", new Vector2(430f, 174f), panelColor);
        RectTransform rect = combatStatusRoot.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -24f);

        GameObject liveBadge = CreatePanel(combatStatusRoot.transform, "LiveBadge", new Vector2(92f, 30f), new Color(0.42f, 0.035f, 0.09f, 0.98f));
        RectTransform liveRect = liveBadge.GetComponent<RectTransform>();
        liveRect.anchorMin = liveRect.anchorMax = new Vector2(0f, 1f);
        liveRect.pivot = new Vector2(0f, 1f);
        liveRect.anchoredPosition = new Vector2(18f, -14f);
        Text onAirText = CreateText(liveBadge.transform, "●  ON AIR", 12, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(onAirText.rectTransform);

        stageText = CreateText(combatStatusRoot.transform, "WAITING FOR TAKE", 18, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        SetAnchors(stageText.rectTransform, new Vector2(0.28f, 0.74f), new Vector2(0.95f, 0.94f));

        enemyText = CreateText(combatStatusRoot.transform, "ENEMY --", 12, FontStyle.Bold, TextAnchor.MiddleRight, goldColor);
        SetAnchors(enemyText.rectTransform, new Vector2(0.63f, 0.56f), new Vector2(0.94f, 0.70f));

        hpText = CreateText(combatStatusRoot.transform, "HP", 11, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.86f, 0.88f, 0.93f, 1f));
        SetAnchors(hpText.rectTransform, new Vector2(0.05f, 0.45f), new Vector2(0.20f, 0.57f));
        hpFill = CreateBar(combatStatusRoot.transform, "HPBar", new Vector2(0.20f, 0.47f), new Vector2(0.94f, 0.56f), hpColor);

        staminaText = CreateText(combatStatusRoot.transform, "ST", 11, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.86f, 0.88f, 0.93f, 1f));
        SetAnchors(staminaText.rectTransform, new Vector2(0.05f, 0.28f), new Vector2(0.20f, 0.40f));
        staminaFill = CreateBar(combatStatusRoot.transform, "StaminaBar", new Vector2(0.20f, 0.30f), new Vector2(0.94f, 0.39f), staminaColor);

        audienceText = CreateText(combatStatusRoot.transform, "VIEWERS 0   •   FANS 0   •   POP 0", 11, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.68f, 0.72f, 0.80f, 1f));
        SetAnchors(audienceText.rectTransform, new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.20f));
    }

    private void BuildEquipmentDock()
    {
        equipmentDockRoot = CreatePanel(canvas.transform, "EquipmentDock", new Vector2(858f, 104f), new Color(0.016f, 0.02f, 0.032f, 0.94f));
        RectTransform dockRect = equipmentDockRoot.GetComponent<RectTransform>();
        dockRect.anchorMin = dockRect.anchorMax = new Vector2(0.5f, 0f);
        dockRect.pivot = new Vector2(0.5f, 0f);
        dockRect.anchoredPosition = new Vector2(0f, 22f);

        float slotWidth = 84f;
        float spacing = 8f;
        float total = BattleEquipmentSystem.MaxSlotCount * slotWidth + (BattleEquipmentSystem.MaxSlotCount - 1) * spacing;
        float start = -total * 0.5f + slotWidth * 0.5f;

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            GameObject slot = CreatePanel(equipmentDockRoot.transform, $"Slot_{i + 1}", new Vector2(slotWidth, 80f), new Color(0.055f, 0.062f, 0.082f, 1f));
            RectTransform slotRect = slot.GetComponent<RectTransform>();
            slotRect.anchorMin = slotRect.anchorMax = new Vector2(0.5f, 0.5f);
            slotRect.anchoredPosition = new Vector2(start + i * (slotWidth + spacing), 0f);
            slotBackgrounds[i] = slot.GetComponent<Image>();

            Button button = slot.AddComponent<Button>();
            button.targetGraphic = slotBackgrounds[i];
            int captured = i;
            button.onClick.AddListener(() => equipmentSystem?.EquipSlot(captured));

            Text number = CreateText(slot.transform, (i + 1).ToString(), 10, FontStyle.Bold, TextAnchor.UpperLeft, new Color(0.62f, 0.66f, 0.74f, 1f));
            SetAnchors(number.rectTransform, new Vector2(0.08f, 0.70f), new Vector2(0.35f, 0.94f));

            slotIcons[i] = CreateImage(slot.transform, "Icon", new Vector2(44f, 44f));
            slotIcons[i].rectTransform.anchorMin = slotIcons[i].rectTransform.anchorMax = new Vector2(0.5f, 0.60f);

            slotLabels[i] = CreateText(slot.transform, "EMPTY", 9, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.72f, 0.75f, 0.82f, 1f));
            SetAnchors(slotLabels[i].rectTransform, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.30f));

            slotGrades[i] = CreateText(slot.transform, string.Empty, 9, FontStyle.Bold, TextAnchor.UpperRight, goldColor);
            SetAnchors(slotGrades[i].rectTransform, new Vector2(0.55f, 0.70f), new Vector2(0.92f, 0.94f));
        }
    }

    private void BuildRewardShow()
    {
        rewardRoot = new GameObject("RewardQuizShow");
        rewardRoot.transform.SetParent(canvas.transform, false);
        RectTransform rootRect = rewardRoot.AddComponent<RectTransform>();
        Stretch(rootRect);

        GameObject filter = new("FieldBroadcastFilter");
        filter.transform.SetParent(rewardRoot.transform, false);
        RectTransform filterRect = filter.AddComponent<RectTransform>();
        Stretch(filterRect);
        Image filterImage = filter.AddComponent<Image>();
        filterImage.color = rewardFieldFilter;
        filterImage.raycastTarget = false;

        // Floor glows are created before screen/characters so they always read as light on the stage floor.
        BuildPlayerSpotlight(rewardRoot.transform);
        BuildPresenterSpotlight(rewardRoot.transform);
        BuildRewardScreen(rewardRoot.transform);
        BuildPresenter(rewardRoot.transform);
        BuildRewardInventory(rewardRoot.transform);
        rewardRoot.SetActive(false);
    }

    private void BuildPlayerSpotlight(Transform parent)
    {
        playerSpotlightImage = CreateImage(parent, "PlayerFloorSpotlight", playerSpotlightSize);
        RectTransform rect = playerSpotlightImage.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        playerSpotlightImage.sprite = BattleHudSpriteCache.FloorSpotlight;
        playerSpotlightImage.preserveAspect = false;
        playerSpotlightImage.color = playerSpotlightColor;
        playerSpotlightImage.raycastTarget = false;
    }

    private void BuildPresenterSpotlight(Transform parent)
    {
        presenterSpotlightImage = CreateImage(parent, "PresenterFloorSpotlight", presenterSpotlightSize);
        RectTransform rect = presenterSpotlightImage.rectTransform;
        rect.anchorMin = rect.anchorMax = presenterAnchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = presenterOffset + presenterSpotlightOffset;
        presenterSpotlightImage.sprite = BattleHudSpriteCache.FloorSpotlight;
        presenterSpotlightImage.preserveAspect = false;
        presenterSpotlightImage.color = presenterSpotlightColor;
        presenterSpotlightImage.raycastTarget = false;
    }

    private void BuildRewardScreen(Transform parent)
    {
        GameObject screen = CreatePanel(parent, "PrizeSelectionScreen", rewardScreenSize, new Color(0.025f, 0.020f, 0.055f, 0.985f));
        RectTransform screenRect = screen.GetComponent<RectTransform>();
        screenRect.anchorMin = screenRect.anchorMax = rewardScreenAnchor;
        screenRect.pivot = new Vector2(0.5f, 0.5f);
        screenRect.anchoredPosition = Vector2.zero;

        GameObject inner = CreatePanel(screen.transform, "ScreenInner", rewardScreenSize - new Vector2(34f, 34f), new Color(0.055f, 0.045f, 0.105f, 1f));
        RectTransform innerRect = inner.GetComponent<RectTransform>();
        innerRect.anchorMin = innerRect.anchorMax = new Vector2(0.5f, 0.5f);
        innerRect.anchoredPosition = Vector2.zero;
        inner.GetComponent<Outline>().effectColor = new Color(0.28f, 0.95f, 0.92f, 0.20f);

        rewardTitle = CreateText(inner.transform, "CHOOSE YOUR PRIZE", 30, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        SetAnchors(rewardTitle.rectTransform, new Vector2(0.05f, 0.865f), new Vector2(0.72f, 0.96f));

        rewardSubtitle = CreateText(inner.transform, "SELECT  •  DRAG  •  DROP", 11, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.72f, 0.76f, 0.86f, 1f));
        SetAnchors(rewardSubtitle.rectTransform, new Vector2(0.05f, 0.805f), new Vector2(0.72f, 0.86f));

        Text live = CreateText(inner.transform, "[ON LIVE]", 14, FontStyle.Bold, TextAnchor.MiddleRight, new Color(1f, 0.10f, 0.12f, 1f));
        SetAnchors(live.rectTransform, new Vector2(0.77f, 0.87f), new Vector2(0.95f, 0.95f));

        GameObject cardRoot = new("PrizeChoices");
        cardRoot.transform.SetParent(inner.transform, false);
        rewardCardRoot = cardRoot.AddComponent<RectTransform>();
        SetAnchors(rewardCardRoot, new Vector2(0.055f, 0.31f), new Vector2(0.945f, 0.79f));

        GameObject mapRoot = new("MapSelectionScreen");
        mapRoot.transform.SetParent(inner.transform, false);
        mapSelectionRoot = mapRoot.AddComponent<RectTransform>();
        SetAnchors(mapSelectionRoot, new Vector2(0.025f, 0.055f), new Vector2(0.975f, 0.94f));
        mapRoot.SetActive(false);

        focusedRewardName = CreateText(inner.transform, "SELECT A PRIZE", 16, FontStyle.Bold, TextAnchor.MiddleLeft, goldColor);
        SetAnchors(focusedRewardName.rectTransform, new Vector2(0.055f, 0.185f), new Vector2(0.38f, 0.28f));

        focusedRewardStats = CreateText(inner.transform, "Hover to inspect. Click to select, then drag it to the loadout strip below.", 11, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.78f, 0.82f, 0.90f, 1f));
        SetAnchors(focusedRewardStats.rectTransform, new Vector2(0.38f, 0.17f), new Vector2(0.945f, 0.285f));

        GameObject notice = CreatePanel(inner.transform, "PlacementNotice", new Vector2(980f, 48f), new Color(0.035f, 0.11f, 0.12f, 0.92f));
        rewardNoticePanel = notice;
        RectTransform noticeRect = notice.GetComponent<RectTransform>();
        noticeRect.anchorMin = noticeRect.anchorMax = new Vector2(0.5f, 0.085f);
        noticeRect.anchoredPosition = Vector2.zero;
        rewardInstruction = CreateText(notice.transform, "SELECT A PRIZE FIRST", 11, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(rewardInstruction.rectTransform);
    }

    private void BuildRewardInventory(Transform parent)
    {
        GameObject bar = CreatePanel(parent, "RewardLoadoutStrip", rewardLoadoutSize, new Color(0.018f, 0.024f, 0.040f, 0.92f));
        rewardInventoryPanel = bar;
        RectTransform barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = barRect.anchorMax = rewardLoadoutAnchor;
        barRect.pivot = new Vector2(0.5f, 0.5f);
        barRect.anchoredPosition = Vector2.zero;

        Text label = CreateText(bar.transform, "CURRENT LOADOUT  /  DROP TARGET", 10, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.70f, 0.76f, 0.86f, 1f));
        SetAnchors(label.rectTransform, new Vector2(0.025f, 0.79f), new Vector2(0.46f, 0.97f));

        GameObject root = new("RewardInventory");
        root.transform.SetParent(bar.transform, false);
        rewardInventoryRoot = root.AddComponent<RectTransform>();
        SetAnchors(rewardInventoryRoot, new Vector2(0.02f, 0.08f), new Vector2(0.98f, 0.77f));

        float slotWidth = 108f;
        float slotHeight = 92f;
        float spacing = 8f;
        float total = BattleEquipmentSystem.MaxSlotCount * slotWidth + (BattleEquipmentSystem.MaxSlotCount - 1) * spacing;
        float start = -total * 0.5f + slotWidth * 0.5f;

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            GameObject slot = CreatePanel(rewardInventoryRoot, $"RewardLoadoutSlot_{i + 1}", new Vector2(slotWidth, slotHeight), RewardLockedSlotColor);
            RectTransform rect = slot.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(start + i * (slotWidth + spacing), 0f);
            rewardSlotBackgrounds[i] = slot.GetComponent<Image>();

            RewardInventoryDropZone zone = slot.AddComponent<RewardInventoryDropZone>();
            zone.Configure(this, i);

            Text number = CreateText(slot.transform, $"{i + 1}", 9, FontStyle.Bold, TextAnchor.UpperLeft, new Color(0.70f, 0.74f, 0.82f, 1f));
            SetAnchors(number.rectTransform, new Vector2(0.06f, 0.75f), new Vector2(0.30f, 0.95f));

            rewardSlotGrades[i] = CreateText(slot.transform, string.Empty, 8, FontStyle.Bold, TextAnchor.UpperRight, goldColor);
            SetAnchors(rewardSlotGrades[i].rectTransform, new Vector2(0.42f, 0.75f), new Vector2(0.94f, 0.95f));

            rewardSlotIcons[i] = CreateImage(slot.transform, "CurrentItemIcon", new Vector2(38f, 38f));
            rewardSlotIcons[i].rectTransform.anchorMin = rewardSlotIcons[i].rectTransform.anchorMax = new Vector2(0.5f, 0.60f);

            rewardSlotNames[i] = CreateText(slot.transform, "LOCKED", 8, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetAnchors(rewardSlotNames[i].rectTransform, new Vector2(0.05f, 0.19f), new Vector2(0.95f, 0.38f));

            rewardSlotActions[i] = CreateText(slot.transform, string.Empty, 7, FontStyle.Bold, TextAnchor.MiddleCenter, accentColor);
            SetAnchors(rewardSlotActions[i].rectTransform, new Vector2(0.04f, 0.015f), new Vector2(0.96f, 0.18f));
        }
    }

    private void BuildPresenter(Transform parent)
    {
        GameObject host = new("Presenter");
        host.transform.SetParent(parent, false);
        presenterRect = host.AddComponent<RectTransform>();
        presenterImage = host.AddComponent<Image>();
        presenterImage.preserveAspect = true;
        presenterImage.raycastTarget = false;
        ApplyPresenterVisual();
    }

    private void ApplyPresenterVisual()
    {
        if (presenterRect != null)
        {
            presenterRect.anchorMin = presenterRect.anchorMax = presenterAnchor;
            presenterRect.pivot = new Vector2(0.5f, 0.5f);
            presenterRect.sizeDelta = presenterSize;
            presenterRect.anchoredPosition = presenterOffset;
            presenterRect.localScale = new Vector3(presenterFlipX ? -1f : 1f, 1f, 1f);
        }

        if (presenterImage != null)
        {
            presenterImage.sprite = presenterSprite != null ? presenterSprite : BattleHudSpriteCache.DefaultSprite;
            presenterImage.color = presenterColor;
            presenterImage.enabled = true;
        }
    }

    private void RefreshRewardState()
    {
        if (runManager == null || rewardRoot == null)
            return;

        bool rewardState = runManager.State == BattleRunState.Reward;
        bool mapSelectionState = runManager.State == BattleRunState.SelectingNode;
        int count = rewardState ? runManager.CurrentRewardChoices.Count : 0;
        bool stateChanged = lastObservedState != runManager.State;

        if (mapSelectionState)
        {
            if (!rewardRoot.activeSelf || stateChanged || mapSelectionRoot == null || !mapSelectionRoot.gameObject.activeSelf)
            {
                pendingRewardIndex = -1;
                lastRewardCount = -1;
                SetRewardVisible(true, true);
                PulseRewardSpotlights();
            }

            lastObservedState = runManager.State;
            return;
        }

        if (!rewardState)
        {
            if (rewardRoot.activeSelf)
                SetRewardVisible(false);
            pendingRewardIndex = -1;
            lastRewardCount = -1;
            lastObservedState = runManager.State;
            return;
        }

        if (!rewardRoot.activeSelf || stateChanged || lastRewardCount != count)
        {
            pendingRewardIndex = -1;
            RebuildRewardCards();
            ClearRewardFocus();
            RefreshRewardInventory();
            SetRewardVisible(true);
            PulseRewardSpotlights();
        }

        lastRewardCount = count;
        lastObservedState = runManager.State;
    }

    private void SetRewardVisible(bool visible, bool mapSelection = false)
    {
        if (rewardRoot != null)
            rewardRoot.SetActive(visible);
        if (combatStatusRoot != null)
            combatStatusRoot.SetActive(!visible);
        if (equipmentDockRoot != null)
            equipmentDockRoot.SetActive(!visible);

        bool showRewardContent = visible && !mapSelection;
        if (rewardTitle != null)
            rewardTitle.gameObject.SetActive(showRewardContent);
        if (rewardSubtitle != null)
            rewardSubtitle.gameObject.SetActive(showRewardContent);
        if (rewardCardRoot != null)
            rewardCardRoot.gameObject.SetActive(showRewardContent);
        if (focusedRewardName != null)
            focusedRewardName.gameObject.SetActive(showRewardContent);
        if (focusedRewardStats != null)
            focusedRewardStats.gameObject.SetActive(showRewardContent);
        if (rewardNoticePanel != null)
            rewardNoticePanel.SetActive(showRewardContent);
        if (rewardInventoryPanel != null)
            rewardInventoryPanel.SetActive(showRewardContent);
        if (mapSelectionRoot != null)
            mapSelectionRoot.gameObject.SetActive(visible && mapSelection);

        if (!visible)
        {
            EndRewardDrag();
            KillSpotlightTweens();
        }
    }

    private void RebuildRewardCards()
    {
        if (rewardCardRoot == null || runManager == null)
            return;

        for (int i = rewardCardRoot.childCount - 1; i >= 0; i--)
            Destroy(rewardCardRoot.GetChild(i).gameObject);

        rewardTitle.text = "CHOOSE YOUR PRIZE";
        rewardSubtitle.text = "SELECT  •  DRAG  •  DROP";

        int count = runManager.CurrentRewardChoices.Count;
        if (count <= 0)
            return;

        float width = Mathf.Min(275f, 835f / count);
        float spacing = 22f;
        float total = count * width + (count - 1) * spacing;
        float start = -total * 0.5f + width * 0.5f;

        for (int i = 0; i < count; i++)
        {
            BattleEquipmentSO reward = runManager.CurrentRewardChoices[i];
            if (reward == null) continue;

            GameObject card = CreatePanel(rewardCardRoot, $"Prize_{i}", new Vector2(width, 245f), RewardCardColor);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            Vector2 basePosition = new(start + i * (width + spacing), 0f);
            cardRect.anchoredPosition = basePosition;

            Image cardImage = card.GetComponent<Image>();
            Button button = card.AddComponent<Button>();
            button.targetGraphic = cardImage;
            int captured = i;
            button.onClick.AddListener(() => SelectRewardForPlacement(captured));

            RewardCardHover hover = card.AddComponent<RewardCardHover>();
            hover.Configure(this, captured, cardRect, basePosition);

            RewardPrizeDrag drag = card.AddComponent<RewardPrizeDrag>();
            drag.Configure(this, captured);

            Image icon = CreateImage(card.transform, "PrizeIcon", new Vector2(105f, 105f));
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.67f);
            icon.sprite = reward.icon;
            icon.enabled = reward.icon != null;

            Text rarity = CreateText(card.transform, reward.rarity.ToString().ToUpperInvariant(), 9, FontStyle.Bold, TextAnchor.MiddleCenter, goldColor);
            SetAnchors(rarity.rectTransform, new Vector2(0.08f, 0.39f), new Vector2(0.92f, 0.47f));

            Text name = CreateText(card.transform, reward.GetDisplayName(), 14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetAnchors(name.rectTransform, new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.39f));

            Text type = CreateText(card.transform, reward.type.ToString().ToUpperInvariant(), 8, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.66f, 0.84f, 0.93f, 1f));
            SetAnchors(type.rectTransform, new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.22f));

            Text action = CreateText(card.transform, "CLICK / DRAG", 8, FontStyle.Bold, TextAnchor.MiddleCenter, accentColor);
            SetAnchors(action.rectTransform, new Vector2(0.08f, 0.025f), new Vector2(0.92f, 0.12f));
        }
    }

    private void SelectRewardForPlacement(int index)
    {
        if (runManager == null || runManager.State != BattleRunState.Reward)
            return;
        if (index < 0 || index >= runManager.CurrentRewardChoices.Count)
            return;
        if (runManager.CurrentRewardChoices[index] == null)
            return;

        pendingRewardIndex = index;
        RefreshRewardChoiceSelection();
        ShowSelectedRewardFocus();
        RefreshRewardInventory();
        PulseRewardSpotlights();

        BattleEquipmentSO selected = runManager.CurrentRewardChoices[index];
        if (rewardInstruction != null)
        {
            rewardInstruction.text = equipmentSystem != null && !equipmentSystem.HasFreeUnlockedSlot()
                ? $"{selected.GetDisplayName().ToUpperInvariant()}  —  LOADOUT FULL: DROP ON THE ITEM TO DISCARD"
                : $"{selected.GetDisplayName().ToUpperInvariant()}  —  DRAG TO AN OPEN SLOT OR DROP ON AN ITEM TO REPLACE";
        }
    }

    private void RefreshRewardChoiceSelection()
    {
        if (rewardCardRoot == null)
            return;

        for (int i = 0; i < rewardCardRoot.childCount; i++)
        {
            Transform child = rewardCardRoot.GetChild(i);
            RewardPrizeDrag drag = child.GetComponent<RewardPrizeDrag>();
            Image image = child.GetComponent<Image>();
            if (drag == null || image == null)
                continue;
            image.color = drag.RewardIndex == pendingRewardIndex ? RewardCardSelectedColor : RewardCardColor;
        }
    }

    internal void HandleRewardCardEnter(int index, RectTransform card, Vector2 basePosition)
    {
        if (runManager == null || index < 0 || index >= runManager.CurrentRewardChoices.Count)
            return;

        if (card != null)
        {
            card.localScale = Vector3.one * rewardHoverScale;
            card.anchoredPosition = basePosition + Vector2.up * rewardHoverLift;
            card.SetAsLastSibling();
        }

        ShowRewardFocus(index);
        PulseRewardSpotlights();
    }

    internal void HandleRewardCardExit(RectTransform card, Vector2 basePosition)
    {
        if (card != null)
        {
            card.localScale = Vector3.one;
            card.anchoredPosition = basePosition;
        }

        if (pendingRewardIndex >= 0)
            ShowSelectedRewardFocus();
        else
            ClearRewardFocus();
    }

    private void ShowRewardFocus(int index)
    {
        if (runManager == null || index < 0 || index >= runManager.CurrentRewardChoices.Count)
            return;

        BattleEquipmentSO reward = runManager.CurrentRewardChoices[index];
        if (reward == null)
            return;

        if (focusedRewardName != null)
            focusedRewardName.text = reward.GetDisplayName();
        if (focusedRewardStats != null)
        {
            focusedRewardStats.text =
                $"{reward.rarity.ToString().ToUpperInvariant()}  /  {reward.type.ToString().ToUpperInvariant()}    " +
                $"DMG ×{reward.damageMultiplier:0.00}    MOVE ×{reward.moveSpeedMultiplier:0.00}    RANGE ×{reward.rangeMultiplier:0.00}";
        }
    }

    private void ShowSelectedRewardFocus()
    {
        if (pendingRewardIndex >= 0)
            ShowRewardFocus(pendingRewardIndex);
    }

    private void ClearRewardFocus()
    {
        if (focusedRewardName != null)
            focusedRewardName.text = "SELECT A PRIZE";
        if (focusedRewardStats != null)
            focusedRewardStats.text = "Hover to inspect. Click to select, then drag it to the loadout strip below.";
        if (rewardInstruction != null)
            rewardInstruction.text = "SELECT A PRIZE FIRST";
    }

    private BattleEquipmentSO GetSelectedReward()
    {
        if (runManager == null || pendingRewardIndex < 0 || pendingRewardIndex >= runManager.CurrentRewardChoices.Count)
            return null;
        return runManager.CurrentRewardChoices[pendingRewardIndex];
    }

    private void RefreshRewardInventory()
    {
        if (rewardSlotBackgrounds[0] == null)
            return;

        BattleEquipmentSO selected = GetSelectedReward();

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            bool unlocked = equipmentSystem != null && i < equipmentSystem.UnlockedSlotCount;
            BattleEquipmentSlot slot = unlocked && i < equipmentSystem.Slots.Count ? equipmentSystem.Slots[i] : null;
            bool occupied = slot != null && slot.equipment != null;
            bool sameSelected = selected != null && occupied && slot.equipment == selected;
            bool canMerge = sameSelected && slot.grade < 3;
            bool maxedSame = sameSelected && slot.grade >= 3;

            if (!unlocked)
            {
                rewardSlotBackgrounds[i].color = RewardLockedSlotColor;
                rewardSlotIcons[i].sprite = null;
                rewardSlotIcons[i].enabled = false;
                rewardSlotNames[i].text = "LOCKED";
                rewardSlotGrades[i].text = string.Empty;
                rewardSlotActions[i].text = string.Empty;
                continue;
            }

            rewardSlotIcons[i].sprite = occupied ? slot.equipment.icon : null;
            rewardSlotIcons[i].enabled = occupied && slot.equipment.icon != null;
            rewardSlotNames[i].text = occupied ? Shorten(slot.equipment.GetDisplayName(), 13) : "EMPTY";
            rewardSlotGrades[i].text = occupied ? $"G{slot.grade} {slot.copies}/3" : string.Empty;

            if (selected == null)
            {
                rewardSlotBackgrounds[i].color = occupied ? RewardOccupiedSlotColor : RewardEmptySlotColor;
                rewardSlotActions[i].text = occupied ? "CURRENT" : "OPEN";
                rewardSlotActions[i].color = occupied ? new Color(0.65f, 0.70f, 0.79f, 1f) : new Color(0.30f, 0.90f, 0.85f, 1f);
            }
            else if (canMerge)
            {
                rewardSlotBackgrounds[i].color = RewardMergeSlotColor;
                rewardSlotActions[i].text = "MERGE";
                rewardSlotActions[i].color = new Color(0.30f, 0.95f, 0.78f, 1f);
            }
            else if (maxedSame)
            {
                rewardSlotBackgrounds[i].color = RewardLockedSlotColor;
                rewardSlotActions[i].text = "MAX";
                rewardSlotActions[i].color = new Color(0.65f, 0.68f, 0.74f, 1f);
            }
            else if (!occupied)
            {
                rewardSlotBackgrounds[i].color = RewardEmptySlotColor;
                rewardSlotActions[i].text = "DROP HERE";
                rewardSlotActions[i].color = new Color(0.30f, 0.95f, 0.85f, 1f);
            }
            else
            {
                rewardSlotBackgrounds[i].color = RewardReplaceSlotColor;
                rewardSlotActions[i].text = "REPLACE";
                rewardSlotActions[i].color = new Color(1f, 0.42f, 0.45f, 1f);
            }
        }
    }

    private void UpdateSpotlightPositions()
    {
        UpdatePlayerSpotlightPosition();
        UpdatePresenterSpotlightPosition();
    }

    private void UpdatePlayerSpotlightPosition()
    {
        if (rewardRoot == null || !rewardRoot.activeSelf || playerSpotlightImage == null || player == null)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 screenPoint = cam.WorldToScreenPoint(player.transform.position);
        RectTransform rootRect = rewardRoot.GetComponent<RectTransform>();
        if (rootRect == null)
            return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screenPoint, null, out Vector2 localPoint))
            playerSpotlightImage.rectTransform.anchoredPosition = localPoint + playerSpotlightScreenOffset;
    }

    private void UpdatePresenterSpotlightPosition()
    {
        if (presenterSpotlightImage == null)
            return;

        RectTransform rect = presenterSpotlightImage.rectTransform;
        rect.anchorMin = rect.anchorMax = presenterAnchor;
        rect.sizeDelta = presenterSpotlightSize;
        rect.anchoredPosition = presenterOffset + presenterSpotlightOffset;
    }

    private void PulseRewardSpotlights()
    {
        PulseSpotlight(playerSpotlightImage);
        PulseSpotlight(presenterSpotlightImage);
    }

    private void PulseSpotlight(Image image)
    {
        if (image == null)
            return;

        image.DOKill();
        image.rectTransform.DOKill();

        Color idle = image.color;
        float idleAlpha = Mathf.Clamp(idle.a, 0.02f, 0.18f);
        idle.a = idleAlpha;
        Color peak = idle;
        peak.a = Mathf.Max(idleAlpha, spotlightPeakAlpha);

        image.color = idle;
        image.rectTransform.localScale = Vector3.one * 0.92f;

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Append(image.DOColor(peak, spotlightAttack).SetEase(Ease.OutQuad));
        sequence.Join(image.rectTransform.DOScale(1.09f, spotlightAttack).SetEase(Ease.OutQuad));
        sequence.Append(image.DOColor(idle, spotlightRelease).SetEase(Ease.OutCubic));
        sequence.Join(image.rectTransform.DOScale(1f, spotlightRelease).SetEase(Ease.OutCubic));
    }

    private void KillSpotlightTweens()
    {
        if (playerSpotlightImage != null)
        {
            playerSpotlightImage.DOKill();
            playerSpotlightImage.rectTransform.DOKill();
            playerSpotlightImage.rectTransform.localScale = Vector3.one;
            playerSpotlightImage.color = playerSpotlightColor;
        }

        if (presenterSpotlightImage != null)
        {
            presenterSpotlightImage.DOKill();
            presenterSpotlightImage.rectTransform.DOKill();
            presenterSpotlightImage.rectTransform.localScale = Vector3.one;
            presenterSpotlightImage.color = presenterSpotlightColor;
        }
    }

    internal void BeginRewardDrag(int index, PointerEventData eventData)
    {
        SelectRewardForPlacement(index);
        BattleEquipmentSO selected = GetSelectedReward();
        if (selected == null || canvas == null)
            return;

        EndRewardDrag();

        rewardDragGhost = CreatePanel(canvas.transform, "RewardDragGhost", new Vector2(200f, 108f), new Color(0.16f, 0.07f, 0.16f, 0.96f));
        rewardDragGhostRect = rewardDragGhost.GetComponent<RectTransform>();
        rewardDragGhostRect.pivot = new Vector2(0.5f, 0.5f);
        rewardDragGhost.transform.SetAsLastSibling();

        CanvasGroup ghostGroup = rewardDragGhost.AddComponent<CanvasGroup>();
        ghostGroup.blocksRaycasts = false;
        ghostGroup.interactable = false;

        Image icon = CreateImage(rewardDragGhost.transform, "Icon", new Vector2(54f, 54f));
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.20f, 0.58f);
        icon.sprite = selected.icon;
        icon.enabled = selected.icon != null;

        Text name = CreateText(rewardDragGhost.transform, selected.GetDisplayName(), 11, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        SetAnchors(name.rectTransform, new Vector2(0.38f, 0.45f), new Vector2(0.95f, 0.78f));
        Text hint = CreateText(rewardDragGhost.transform, "DROP INTO SLOT", 8, FontStyle.Bold, TextAnchor.MiddleLeft, accentColor);
        SetAnchors(hint.rectTransform, new Vector2(0.38f, 0.18f), new Vector2(0.95f, 0.43f));

        UpdateRewardDrag(eventData);
    }

    internal void UpdateRewardDrag(PointerEventData eventData)
    {
        if (rewardDragGhostRect == null || eventData == null)
            return;
        rewardDragGhostRect.position = eventData.position;
    }

    internal void EndRewardDrag()
    {
        if (rewardDragGhost != null)
            Destroy(rewardDragGhost);
        rewardDragGhost = null;
        rewardDragGhostRect = null;
    }

    internal void HandleRewardSlotEnter(int slotIndex)
    {
        BattleEquipmentSO selected = GetSelectedReward();
        if (selected == null || equipmentSystem == null)
            return;
        if (slotIndex < 0 || slotIndex >= equipmentSystem.UnlockedSlotCount)
            return;
        if (!equipmentSystem.CanPlaceIntoSlot(slotIndex, selected))
            return;

        if (rewardSlotBackgrounds[slotIndex] != null)
            rewardSlotBackgrounds[slotIndex].color = new Color(0.30f, 0.17f, 0.30f, 1f);
    }

    internal void HandleRewardSlotExit(int _)
    {
        RefreshRewardInventory();
    }

    internal void HandleRewardDrop(int slotIndex)
    {
        if (runManager == null || equipmentSystem == null || pendingRewardIndex < 0)
            return;

        BattleEquipmentSO selected = GetSelectedReward();
        if (selected == null)
            return;

        if (!equipmentSystem.CanPlaceIntoSlot(slotIndex, selected))
        {
            if (rewardInstruction != null)
                rewardInstruction.text = "THAT SLOT CANNOT TAKE THIS ITEM";
            RefreshRewardInventory();
            return;
        }

        if (!runManager.PlaceRewardIntoSlot(pendingRewardIndex, slotIndex))
        {
            if (rewardInstruction != null)
                rewardInstruction.text = "PLACEMENT FAILED — CHOOSE ANOTHER SLOT";
            RefreshRewardInventory();
            return;
        }

        pendingRewardIndex = -1;
        EndRewardDrag();
        SetRewardVisible(false);
    }

    private void RefreshVitalBars()
    {
        if (player == null)
            return;

        float hpMax = Mathf.Max(1f, player.maxHp);
        float stMax = Mathf.Max(1f, player.maxStamina);
        if (hpFill != null) hpFill.fillAmount = Mathf.Clamp01(player.CurrentHp / hpMax);
        if (staminaFill != null) staminaFill.fillAmount = Mathf.Clamp01(player.CurrentStamina / stMax);
        if (hpText != null) hpText.text = $"HP  {player.CurrentHp:0}/{hpMax:0}";
        if (staminaText != null) staminaText.text = $"ST  {player.CurrentStamina:0}/{stMax:0}";
    }

    private void RefreshStatus()
    {
        if (stageText != null)
        {
            stageText.text = runManager != null && runManager.CurrentNode != null
                ? $"TAKE {runManager.CurrentNode.depth + 1:00}  /  {runManager.CurrentNode.type.ToString().ToUpperInvariant()}"
                : "WAITING FOR NEXT TAKE";
        }

        if (enemyText != null)
            enemyText.text = roomManager != null && roomManager.IsRoomActive ? $"ENEMY  {roomManager.AliveMonsterCount:00}" : "ENEMY  --";

        if (audienceText != null && progress != null)
            audienceText.text = $"VIEWERS {progress.Viewers:N0}   •   FANS {progress.FanPoints:N0}   •   POP {progress.Popularity:N0}";
    }

    private void RefreshEquipment()
    {
        if (slotBackgrounds[0] == null)
            return;

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            bool unlocked = equipmentSystem != null && i < equipmentSystem.UnlockedSlotCount;
            BattleEquipmentSlot slot = unlocked && i < equipmentSystem.Slots.Count ? equipmentSystem.Slots[i] : null;
            bool occupied = slot != null && slot.equipment != null;
            bool equipped = occupied && equipmentSystem.IsSlotEquipped(i);

            slotBackgrounds[i].color = !unlocked
                ? new Color(0.028f, 0.032f, 0.043f, 0.72f)
                : equipped ? new Color(0.18f, 0.07f, 0.16f, 1f) : new Color(0.055f, 0.062f, 0.082f, 1f);

            slotIcons[i].enabled = occupied && slot.equipment.icon != null;
            slotIcons[i].sprite = occupied ? slot.equipment.icon : null;
            slotIcons[i].color = Color.white;

            if (!unlocked)
            {
                slotLabels[i].text = "LOCKED";
                slotLabels[i].color = new Color(0.35f, 0.38f, 0.44f, 1f);
                slotGrades[i].text = string.Empty;
            }
            else if (!occupied)
            {
                slotLabels[i].text = "EMPTY";
                slotLabels[i].color = new Color(0.50f, 0.54f, 0.62f, 1f);
                slotGrades[i].text = string.Empty;
            }
            else
            {
                slotLabels[i].text = Shorten(slot.equipment.GetDisplayName(), 12);
                slotLabels[i].color = Color.white;
                slotGrades[i].text = $"G{slot.grade}";
            }
        }

        RefreshRewardInventory();
    }

    private static string Shorten(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value ?? string.Empty;
        return value.Substring(0, Mathf.Max(1, max - 1)) + "…";
    }

    private static Image CreateBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color fillColor)
    {
        GameObject bg = new(name + "_BG");
        bg.transform.SetParent(parent, false);
        RectTransform bgRect = bg.AddComponent<RectTransform>();
        SetAnchors(bgRect, anchorMin, anchorMax);
        Image bgImage = bg.AddComponent<Image>();
        bgImage.sprite = BattleHudSpriteCache.RoundedPanel;
        bgImage.type = Image.Type.Sliced;
        bgImage.color = new Color(0.12f, 0.13f, 0.17f, 1f);
        bgImage.raycastTarget = false;

        GameObject fill = new(name + "_Fill");
        fill.transform.SetParent(bg.transform, false);
        RectTransform fillRect = fill.AddComponent<RectTransform>();
        Stretch(fillRect);
        Image image = fill.AddComponent<Image>();
        image.sprite = BattleHudSpriteCache.RoundedPanel;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = 0;
        image.fillAmount = 1f;
        image.color = fillColor;
        image.raycastTarget = false;
        return image;
    }

    private static GameObject CreatePanel(Transform parent, string name, Vector2 size, Color color)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        Image image = go.AddComponent<Image>();
        image.sprite = BattleHudSpriteCache.RoundedPanel;
        image.type = Image.Type.Sliced;
        image.color = color;
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.10f);
        outline.effectDistance = new Vector2(2f, -2f);
        return go;
    }

    private static Image CreateImage(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        Image image = go.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private static Text CreateText(Transform parent, string content, int size, FontStyle style, TextAnchor alignment, Color color)
    {
        GameObject go = new("Text");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        Text text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}

internal sealed class RewardCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private BattleHUD owner;
    private int index;
    private RectTransform card;
    private Vector2 basePosition;

    public void Configure(BattleHUD hud, int rewardIndex, RectTransform cardRect, Vector2 originalPosition)
    {
        owner = hud;
        index = rewardIndex;
        card = cardRect;
        basePosition = originalPosition;
    }

    public void OnPointerEnter(PointerEventData eventData) => owner?.HandleRewardCardEnter(index, card, basePosition);
    public void OnPointerExit(PointerEventData eventData) => owner?.HandleRewardCardExit(card, basePosition);
}

internal sealed class RewardPrizeDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private BattleHUD owner;
    private int rewardIndex;

    public int RewardIndex => rewardIndex;

    public void Configure(BattleHUD hud, int index)
    {
        owner = hud;
        rewardIndex = index;
    }

    public void OnBeginDrag(PointerEventData eventData) => owner?.BeginRewardDrag(rewardIndex, eventData);
    public void OnDrag(PointerEventData eventData) => owner?.UpdateRewardDrag(eventData);
    public void OnEndDrag(PointerEventData eventData) => owner?.EndRewardDrag();
}

internal sealed class RewardInventoryDropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    private BattleHUD owner;
    private int slotIndex;

    public void Configure(BattleHUD hud, int index)
    {
        owner = hud;
        slotIndex = index;
    }

    public void OnDrop(PointerEventData eventData) => owner?.HandleRewardDrop(slotIndex);
    public void OnPointerEnter(PointerEventData eventData) => owner?.HandleRewardSlotEnter(slotIndex);
    public void OnPointerExit(PointerEventData eventData) => owner?.HandleRewardSlotExit(slotIndex);
}

internal static class BattleHudSpriteCache
{
    private static Sprite roundedPanel;
    private static Sprite defaultSprite;
    private static Sprite floorSpotlight;

    public static Sprite RoundedPanel => roundedPanel != null ? roundedPanel : roundedPanel = CreateRoundedPanel();
    public static Sprite DefaultSprite => defaultSprite != null ? defaultSprite : defaultSprite = CreateDefaultSprite();
    public static Sprite FloorSpotlight => floorSpotlight != null ? floorSpotlight : floorSpotlight = CreateFloorSpotlight();

    private static Sprite CreateRoundedPanel()
    {
        const int pixels = 32;
        const float radius = 7f;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (int y = 0; y < pixels; y++)
        {
            for (int x = 0; x < pixels; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x - 15.5f) - (15.5f - radius), 0f);
                float dy = Mathf.Max(Mathf.Abs(y - 15.5f) - (15.5f - radius), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(radius + 0.5f - d);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, pixels, pixels),
            new Vector2(0.5f, 0.5f),
            pixels,
            0,
            SpriteMeshType.FullRect,
            new Vector4(8f, 8f, 8f, 8f));
        sprite.name = "RuntimeHudRoundedPanel";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static Sprite CreateDefaultSprite()
    {
        const int pixels = 16;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (int y = 0; y < pixels; y++)
            for (int x = 0; x < pixels; x++)
                texture.SetPixel(x, y, Color.white);

        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, pixels, pixels), new Vector2(0.5f, 0.5f), pixels, 0, SpriteMeshType.FullRect);
        sprite.name = "RuntimeSpriteDefault";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static Sprite CreateFloorSpotlight()
    {
        const int width = 256;
        const int height = 128;
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Vector2 center = new((width - 1) * 0.5f, (height - 1) * 0.5f);
        float invRadiusX = 1f / Mathf.Max(1f, center.x);
        float invRadiusY = 1f / Mathf.Max(1f, center.y);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (x - center.x) * invRadiusX;
                float ny = (y - center.y) * invRadiusY;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);

                // Soft radial floor pool: bright center, feathered edge, no hard crop and no upward beam.
                float core = 1f - Mathf.SmoothStep(0.08f, 0.70f, radius);
                float feather = 1f - Mathf.SmoothStep(0.56f, 1f, radius);
                float alpha = Mathf.Clamp01(core * 0.52f + feather * 0.48f);
                alpha *= Mathf.Clamp01(1f - Mathf.Pow(radius, 3.2f));

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 128f, 0, SpriteMeshType.FullRect);
        sprite.name = "RuntimeRewardBirdEyeFloorSpotlight";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}

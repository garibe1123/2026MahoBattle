using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Runtime broadcast HUD.
/// - compact combat status + equipment dock
/// - reward choice is a field-visible quiz-show presentation, not a fullscreen menu
/// - one presenter portrait slot
/// - live field monitor with [ON LIVE]
/// - reward cards live only in the lower screen region and expand on hover
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
    [Tooltip("Optional one-host image. Leave empty until the presenter art is ready.")]
    [SerializeField] private Sprite presenterSprite;
    [SerializeField] private Color rewardFieldFilter = new(0.06f, 0.035f, 0.11f, 0.24f);
    [SerializeField, Range(1.02f, 1.40f)] private float rewardHoverScale = 1.18f;
    [SerializeField, Min(0f)] private float rewardHoverLift = 24f;
    [SerializeField, Range(256, 1024)] private int liveMonitorWidth = 640;
    [SerializeField, Range(144, 768)] private int liveMonitorHeight = 360;

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RunProgressSystem progress;
    private BattleEquipmentSystem equipmentSystem;
    private PlayerController player;

    private Canvas canvas;
    private CanvasGroup canvasGroup;
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
    private Text rewardTitle;
    private Text rewardSubtitle;
    private Text focusedRewardName;
    private Text focusedRewardStats;
    private Image focusedRewardIcon;
    private Image presenterImage;
    private RawImage liveMonitorImage;
    private RenderTexture liveMonitorTexture;
    private Camera liveMonitorCamera;
    private GameObject liveMonitorCameraObject;

    private int pendingRewardIndex = -1;
    private int lastRewardCount = -1;
    private BattleRunState lastObservedState = (BattleRunState)(-1);
    private float nextSlowRefresh;
    private bool legacyDummyOverlaysDisabled;

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
        ReleaseLiveMonitor();
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
        if (rewardRoot == null || !rewardRoot.activeSelf)
            return;

        RenderLiveMonitor();
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
            if (behaviour == null)
                continue;

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

        equipmentSystem.InventoryChanged -= RefreshEquipment;
        equipmentSystem.InventoryChanged += RefreshEquipment;
        equipmentSystem.SlotCapacityChanged -= HandleSlotCapacityChanged;
        equipmentSystem.SlotCapacityChanged += HandleSlotCapacityChanged;
    }

    private void UnsubscribeEquipment()
    {
        if (equipmentSystem == null)
            return;

        equipmentSystem.InventoryChanged -= RefreshEquipment;
        equipmentSystem.SlotCapacityChanged -= HandleSlotCapacityChanged;
    }

    private void HandleSlotCapacityChanged(int _)
    {
        RefreshEquipment();
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
        GameObject panel = CreatePanel(canvas.transform, "BroadcastStatus", new Vector2(430f, 174f), panelColor);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -24f);

        GameObject liveBadge = CreatePanel(panel.transform, "LiveBadge", new Vector2(92f, 30f), new Color(0.42f, 0.035f, 0.09f, 0.98f));
        RectTransform liveRect = liveBadge.GetComponent<RectTransform>();
        liveRect.anchorMin = liveRect.anchorMax = new Vector2(0f, 1f);
        liveRect.pivot = new Vector2(0f, 1f);
        liveRect.anchoredPosition = new Vector2(18f, -14f);
        Text onAirText = CreateText(liveBadge.transform, "●  ON AIR", 12, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(onAirText.rectTransform);

        stageText = CreateText(panel.transform, "WAITING FOR TAKE", 18, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        SetAnchors(stageText.rectTransform, new Vector2(0.28f, 0.74f), new Vector2(0.95f, 0.94f));

        enemyText = CreateText(panel.transform, "ENEMY --", 12, FontStyle.Bold, TextAnchor.MiddleRight, goldColor);
        SetAnchors(enemyText.rectTransform, new Vector2(0.63f, 0.56f), new Vector2(0.94f, 0.70f));

        hpText = CreateText(panel.transform, "HP", 11, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.86f, 0.88f, 0.93f, 1f));
        SetAnchors(hpText.rectTransform, new Vector2(0.05f, 0.45f), new Vector2(0.20f, 0.57f));
        hpFill = CreateBar(panel.transform, "HPBar", new Vector2(0.20f, 0.47f), new Vector2(0.94f, 0.56f), hpColor);

        staminaText = CreateText(panel.transform, "ST", 11, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.86f, 0.88f, 0.93f, 1f));
        SetAnchors(staminaText.rectTransform, new Vector2(0.05f, 0.28f), new Vector2(0.20f, 0.40f));
        staminaFill = CreateBar(panel.transform, "StaminaBar", new Vector2(0.20f, 0.30f), new Vector2(0.94f, 0.39f), staminaColor);

        audienceText = CreateText(panel.transform, "VIEWERS 0   •   FANS 0   •   POP 0", 11, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.68f, 0.72f, 0.80f, 1f));
        SetAnchors(audienceText.rectTransform, new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.20f));
    }

    private void BuildEquipmentDock()
    {
        GameObject dock = CreatePanel(canvas.transform, "EquipmentDock", new Vector2(858f, 104f), new Color(0.016f, 0.02f, 0.032f, 0.94f));
        RectTransform dockRect = dock.GetComponent<RectTransform>();
        dockRect.anchorMin = dockRect.anchorMax = new Vector2(0.5f, 0f);
        dockRect.pivot = new Vector2(0.5f, 0f);
        dockRect.anchoredPosition = new Vector2(0f, 22f);

        float slotWidth = 84f;
        float spacing = 8f;
        float total = BattleEquipmentSystem.MaxSlotCount * slotWidth + (BattleEquipmentSystem.MaxSlotCount - 1) * spacing;
        float start = -total * 0.5f + slotWidth * 0.5f;

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            GameObject slot = CreatePanel(dock.transform, $"Slot_{i + 1}", new Vector2(slotWidth, 80f), new Color(0.055f, 0.062f, 0.082f, 1f));
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

            GameObject iconGo = new("Icon");
            iconGo.transform.SetParent(slot.transform, false);
            RectTransform iconRect = iconGo.AddComponent<RectTransform>();
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.60f);
            iconRect.sizeDelta = new Vector2(44f, 44f);
            slotIcons[i] = iconGo.AddComponent<Image>();
            slotIcons[i].preserveAspect = true;
            slotIcons[i].raycastTarget = false;

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

        // Thin broadcast tint only. It keeps the real field/player visible instead of replacing them with a menu.
        GameObject filter = new("FieldBroadcastFilter");
        filter.transform.SetParent(rewardRoot.transform, false);
        RectTransform filterRect = filter.AddComponent<RectTransform>();
        Stretch(filterRect);
        Image filterImage = filter.AddComponent<Image>();
        filterImage.color = rewardFieldFilter;
        filterImage.raycastTarget = false;

        BuildLiveMonitor(rewardRoot.transform);
        BuildPresenter(rewardRoot.transform);

        rewardTitle = CreateText(rewardRoot.transform, "PICK YOUR REWARD", 24, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        SetAnchors(rewardTitle.rectTransform, new Vector2(0.29f, 0.38f), new Vector2(0.71f, 0.43f));

        rewardSubtitle = CreateText(rewardRoot.transform, "HOVER TO CHECK  /  CLICK TO LOCK IN", 11, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.73f, 0.76f, 0.84f, 1f));
        SetAnchors(rewardSubtitle.rectTransform, new Vector2(0.29f, 0.34f), new Vector2(0.71f, 0.38f));

        GameObject cardRoot = new("RewardCards");
        cardRoot.transform.SetParent(rewardRoot.transform, false);
        rewardCardRoot = cardRoot.AddComponent<RectTransform>();
        SetAnchors(rewardCardRoot, new Vector2(0.12f, 0.035f), new Vector2(0.88f, 0.31f));

        rewardRoot.SetActive(false);
    }

    private void BuildLiveMonitor(Transform parent)
    {
        GameObject frame = CreatePanel(parent, "LiveFieldMonitor", new Vector2(700f, 330f), new Color(0.015f, 0.018f, 0.027f, 0.98f));
        RectTransform frameRect = frame.GetComponent<RectTransform>();
        frameRect.anchorMin = frameRect.anchorMax = new Vector2(0.5f, 1f);
        frameRect.pivot = new Vector2(0.5f, 1f);
        frameRect.anchoredPosition = new Vector2(-90f, -40f);

        GameObject screen = new("FieldFeed");
        screen.transform.SetParent(frame.transform, false);
        RectTransform screenRect = screen.AddComponent<RectTransform>();
        screenRect.anchorMin = new Vector2(0.025f, 0.055f);
        screenRect.anchorMax = new Vector2(0.975f, 0.945f);
        screenRect.offsetMin = screenRect.offsetMax = Vector2.zero;
        liveMonitorImage = screen.AddComponent<RawImage>();
        liveMonitorImage.color = new Color(0.92f, 0.95f, 1f, 1f);
        liveMonitorImage.raycastTarget = false;

        GameObject tint = new("MonitorTint");
        tint.transform.SetParent(frame.transform, false);
        RectTransform tintRect = tint.AddComponent<RectTransform>();
        tintRect.anchorMin = new Vector2(0.025f, 0.055f);
        tintRect.anchorMax = new Vector2(0.975f, 0.945f);
        tintRect.offsetMin = tintRect.offsetMax = Vector2.zero;
        Image tintImage = tint.AddComponent<Image>();
        tintImage.color = new Color(0.05f, 0.13f, 0.18f, 0.12f);
        tintImage.raycastTarget = false;

        Text live = CreateText(frame.transform, "[ON LIVE]", 14, FontStyle.Bold, TextAnchor.UpperRight, new Color(1f, 0.10f, 0.12f, 1f));
        SetAnchors(live.rectTransform, new Vector2(0.72f, 0.83f), new Vector2(0.95f, 0.94f));

        focusedRewardIcon = CreateImage(frame.transform, "FocusedRewardIcon", new Vector2(82f, 82f));
        RectTransform iconRect = focusedRewardIcon.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.13f, 0.23f);
        iconRect.anchoredPosition = Vector2.zero;
        focusedRewardIcon.enabled = false;

        focusedRewardName = CreateText(frame.transform, "HOVER AN ITEM", 18, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        SetAnchors(focusedRewardName.rectTransform, new Vector2(0.23f, 0.12f), new Vector2(0.72f, 0.28f));

        focusedRewardStats = CreateText(frame.transform, "", 11, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.78f, 0.82f, 0.90f, 1f));
        SetAnchors(focusedRewardStats.rectTransform, new Vector2(0.23f, 0.03f), new Vector2(0.84f, 0.15f));
    }

    private void BuildPresenter(Transform parent)
    {
        GameObject hostFrame = CreatePanel(parent, "Presenter", new Vector2(220f, 290f), new Color(0.06f, 0.025f, 0.08f, 0.74f));
        RectTransform hostRect = hostFrame.GetComponent<RectTransform>();
        hostRect.anchorMin = hostRect.anchorMax = new Vector2(0.82f, 0.70f);
        hostRect.anchoredPosition = new Vector2(0f, 0f);

        presenterImage = CreateImage(hostFrame.transform, "PresenterSprite", new Vector2(190f, 230f));
        presenterImage.rectTransform.anchorMin = presenterImage.rectTransform.anchorMax = new Vector2(0.5f, 0.57f);
        presenterImage.sprite = presenterSprite;
        presenterImage.enabled = presenterSprite != null;

        Text hostName = CreateText(hostFrame.transform, "HOST", 12, FontStyle.Bold, TextAnchor.MiddleCenter, goldColor);
        SetAnchors(hostName.rectTransform, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.15f));
    }

    private void RefreshRewardState()
    {
        if (runManager == null || rewardRoot == null)
            return;

        bool rewardState = runManager.State == BattleRunState.Reward;
        int count = rewardState ? runManager.CurrentRewardChoices.Count : 0;
        bool stateChanged = lastObservedState != runManager.State;

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
            SetRewardVisible(true);
        }

        lastRewardCount = count;
        lastObservedState = runManager.State;
    }

    private void SetRewardVisible(bool visible)
    {
        if (rewardRoot != null)
            rewardRoot.SetActive(visible);

        if (visible)
            EnsureLiveMonitor();
    }

    private void RebuildRewardCards()
    {
        if (rewardCardRoot == null || runManager == null)
            return;

        for (int i = rewardCardRoot.childCount - 1; i >= 0; i--)
            Destroy(rewardCardRoot.GetChild(i).gameObject);

        if (pendingRewardIndex >= 0)
        {
            BuildReplacementChoices();
            return;
        }

        rewardTitle.text = "PICK YOUR REWARD";
        rewardSubtitle.text = "HOVER TO CHECK  /  CLICK TO LOCK IN";

        int count = runManager.CurrentRewardChoices.Count;
        if (count <= 0)
            return;

        float width = Mathf.Min(250f, 740f / count);
        float spacing = 22f;
        float total = count * width + (count - 1) * spacing;
        float start = -total * 0.5f + width * 0.5f;

        for (int i = 0; i < count; i++)
        {
            BattleEquipmentSO reward = runManager.CurrentRewardChoices[i];
            if (reward == null)
                continue;

            GameObject card = CreatePanel(rewardCardRoot, $"Reward_{i}", new Vector2(width, 210f), new Color(0.055f, 0.061f, 0.082f, 0.96f));
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            Vector2 basePosition = new(start + i * (width + spacing), 0f);
            cardRect.anchoredPosition = basePosition;

            Image cardImage = card.GetComponent<Image>();
            Button button = card.AddComponent<Button>();
            button.targetGraphic = cardImage;
            int captured = i;
            button.onClick.AddListener(() => TryChooseReward(captured));

            RewardCardHover hover = card.AddComponent<RewardCardHover>();
            hover.Configure(this, captured, cardRect, basePosition);

            GameObject iconObject = new("Icon");
            iconObject.transform.SetParent(card.transform, false);
            RectTransform iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.66f);
            iconRect.sizeDelta = new Vector2(76f, 76f);
            Image icon = iconObject.AddComponent<Image>();
            icon.sprite = reward.icon;
            icon.enabled = reward.icon != null;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            Text rarity = CreateText(card.transform, reward.rarity.ToString().ToUpperInvariant(), 9, FontStyle.Bold, TextAnchor.MiddleCenter, goldColor);
            SetAnchors(rarity.rectTransform, new Vector2(0.08f, 0.40f), new Vector2(0.92f, 0.48f));

            Text name = CreateText(card.transform, reward.GetDisplayName(), 14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetAnchors(name.rectTransform, new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.40f));

            Text hint = CreateText(card.transform, "CLICK", 10, FontStyle.Bold, TextAnchor.MiddleCenter, accentColor);
            SetAnchors(hint.rectTransform, new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.17f));
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

        BattleEquipmentSO reward = runManager.CurrentRewardChoices[index];
        if (reward == null)
            return;

        focusedRewardName.text = reward.GetDisplayName();
        focusedRewardStats.text = $"{reward.rarity.ToString().ToUpperInvariant()}   /   {reward.type.ToString().ToUpperInvariant()}\nDMG ×{reward.damageMultiplier:0.00}    MOVE ×{reward.moveSpeedMultiplier:0.00}    RANGE ×{reward.rangeMultiplier:0.00}";
        focusedRewardIcon.sprite = reward.icon;
        focusedRewardIcon.enabled = reward.icon != null;
    }

    internal void HandleRewardCardExit(RectTransform card, Vector2 basePosition)
    {
        if (card != null)
        {
            card.localScale = Vector3.one;
            card.anchoredPosition = basePosition;
        }
        ClearRewardFocus();
    }

    private void ClearRewardFocus()
    {
        if (focusedRewardName != null) focusedRewardName.text = "HOVER AN ITEM";
        if (focusedRewardStats != null) focusedRewardStats.text = "The live monitor keeps the field visible while you inspect the next loadout.";
        if (focusedRewardIcon != null)
        {
            focusedRewardIcon.sprite = null;
            focusedRewardIcon.enabled = false;
        }
    }

    private void TryChooseReward(int index)
    {
        if (runManager == null || runManager.State != BattleRunState.Reward)
            return;

        if (runManager.SelectReward(index))
        {
            pendingRewardIndex = -1;
            SetRewardVisible(false);
            return;
        }

        pendingRewardIndex = index;
        RebuildRewardCards();
    }

    private void BuildReplacementChoices()
    {
        if (runManager == null || equipmentSystem == null ||
            pendingRewardIndex < 0 || pendingRewardIndex >= runManager.CurrentRewardChoices.Count)
            return;

        BattleEquipmentSO pending = runManager.CurrentRewardChoices[pendingRewardIndex];
        rewardTitle.text = "INVENTORY FULL";
        rewardSubtitle.text = $"CLICK A SLOT TO REPLACE IT WITH {pending.GetDisplayName().ToUpperInvariant()}";

        int count = equipmentSystem.UnlockedSlotCount;
        float width = Mathf.Min(170f, 720f / Mathf.Max(1, count));
        float spacing = 12f;
        float total = count * width + (count - 1) * spacing;
        float start = -total * 0.5f + width * 0.5f;

        for (int i = 0; i < count; i++)
        {
            BattleEquipmentSlot slot = equipmentSystem.Slots[i];
            string oldName = slot != null && slot.equipment != null ? slot.equipment.GetDisplayName() : "EMPTY";

            GameObject card = CreatePanel(rewardCardRoot, $"ReplaceSlot_{i}", new Vector2(width, 150f), new Color(0.055f, 0.061f, 0.082f, 0.96f));
            RectTransform rect = card.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(start + i * (width + spacing), 12f);

            Button button = card.AddComponent<Button>();
            button.targetGraphic = card.GetComponent<Image>();
            int captured = i;
            button.onClick.AddListener(() => TryReplaceReward(captured));

            Text number = CreateText(card.transform, $"SLOT {i + 1}", 11, FontStyle.Bold, TextAnchor.MiddleCenter, goldColor);
            SetAnchors(number.rectTransform, new Vector2(0.08f, 0.58f), new Vector2(0.92f, 0.80f));

            Text label = CreateText(card.transform, Shorten(oldName, 16), 12, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetAnchors(label.rectTransform, new Vector2(0.08f, 0.25f), new Vector2(0.92f, 0.58f));

            Text action = CreateText(card.transform, "REPLACE", 10, FontStyle.Bold, TextAnchor.MiddleCenter, accentColor);
            SetAnchors(action.rectTransform, new Vector2(0.08f, 0.06f), new Vector2(0.92f, 0.22f));
        }

        GameObject cancel = CreatePanel(rewardCardRoot, "CancelReplace", new Vector2(150f, 36f), new Color(0.16f, 0.17f, 0.21f, 1f));
        RectTransform cancelRect = cancel.GetComponent<RectTransform>();
        cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(0.5f, 0f);
        cancelRect.anchoredPosition = new Vector2(0f, -4f);
        Button cancelButton = cancel.AddComponent<Button>();
        cancelButton.targetGraphic = cancel.GetComponent<Image>();
        cancelButton.onClick.AddListener(() =>
        {
            pendingRewardIndex = -1;
            RebuildRewardCards();
        });
        Text cancelText = CreateText(cancel.transform, "BACK", 10, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(cancelText.rectTransform);
    }

    private void TryReplaceReward(int slotIndex)
    {
        if (runManager == null || pendingRewardIndex < 0)
            return;

        if (!runManager.ReplaceRewardIntoSlot(pendingRewardIndex, slotIndex))
            return;

        pendingRewardIndex = -1;
        SetRewardVisible(false);
    }

    private void EnsureLiveMonitor()
    {
        Camera main = Camera.main;
        if (main == null)
            return;

        if (liveMonitorTexture == null || liveMonitorTexture.width != liveMonitorWidth || liveMonitorTexture.height != liveMonitorHeight)
        {
            if (liveMonitorTexture != null)
                liveMonitorTexture.Release();
            liveMonitorTexture = new RenderTexture(liveMonitorWidth, liveMonitorHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "RewardLiveFieldFeed",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            liveMonitorTexture.Create();
        }

        if (liveMonitorImage != null)
            liveMonitorImage.texture = liveMonitorTexture;

        if (liveMonitorCamera == null)
        {
            liveMonitorCameraObject = new GameObject("RewardLiveMonitorCamera");
            liveMonitorCameraObject.transform.SetParent(transform, false);
            liveMonitorCamera = liveMonitorCameraObject.AddComponent<Camera>();
            liveMonitorCamera.enabled = false;
        }
    }

    private void RenderLiveMonitor()
    {
        Camera main = Camera.main;
        if (main == null)
            return;

        EnsureLiveMonitor();
        if (liveMonitorCamera == null || liveMonitorTexture == null)
            return;

        liveMonitorCamera.CopyFrom(main);
        liveMonitorCamera.enabled = false;
        liveMonitorCamera.targetTexture = liveMonitorTexture;
        liveMonitorCamera.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
        liveMonitorCamera.Render();
        liveMonitorCamera.targetTexture = null;
    }

    private void ReleaseLiveMonitor()
    {
        if (liveMonitorCameraObject != null)
            Destroy(liveMonitorCameraObject);
        liveMonitorCameraObject = null;
        liveMonitorCamera = null;

        if (liveMonitorTexture != null)
        {
            liveMonitorTexture.Release();
            Destroy(liveMonitorTexture);
        }
        liveMonitorTexture = null;
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
            if (runManager != null && runManager.CurrentNode != null)
                stageText.text = $"TAKE {runManager.CurrentNode.depth + 1:00}  /  {runManager.CurrentNode.type.ToString().ToUpperInvariant()}";
            else
                stageText.text = "WAITING FOR NEXT TAKE";
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
                : equipped
                    ? new Color(0.18f, 0.07f, 0.16f, 1f)
                    : new Color(0.055f, 0.062f, 0.082f, 1f);

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
        outline.effectColor = new Color(1f, 1f, 1f, 0.08f);
        outline.effectDistance = new Vector2(1f, -1f);
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

    public void OnPointerEnter(PointerEventData eventData)
    {
        owner?.HandleRewardCardEnter(index, card, basePosition);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.HandleRewardCardExit(card, basePosition);
    }
}

internal static class BattleHudSpriteCache
{
    private static Sprite roundedPanel;
    public static Sprite RoundedPanel => roundedPanel != null ? roundedPanel : roundedPanel = CreateRoundedPanel();

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
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Combat 장비 전환 입력과 공용 3x3 Loadout View의 생성/내용 갱신을 담당합니다.
/// Tab/LB 입력은 BattleInputRouter가 소유하며 이 클래스는 이벤트와 읽기 전용 입력 값만 사용합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30000)]
public sealed class BattleKineticLoadoutUI : MonoBehaviour
{
    private const int GridSize = BattleEquipmentSystem.GridSize;
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const int CanvasSortingOrder = 780;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleGridSynergyController gridSynergy;
    [SerializeField] private BattleTimeScaleController timeScaleController;
    [SerializeField] private BattleInputRouter inputRouter;
    [SerializeField] private PlayerController player;
    [SerializeField] private BattleKineticItemBarUI miniPackUI;
    [SerializeField] private BattleBroadcastDashboardController dashboardController;

    [Header("Switch Input")]
    [SerializeField, Min(0.05f)] private float holdThreshold = 0.14f;
    [SerializeField, Range(0.02f, 0.20f)] private float combatInventoryTimeScale = 0.05f;
    [SerializeField, Range(0.2f, 0.95f)] private float stickThreshold = 0.55f;
    [SerializeField, Range(0.05f, 0.8f)] private float stickReleaseThreshold = 0.22f;

    [Header("Kinetic UI")]
    [SerializeField] private Color inkColor = new(0.028f, 0.030f, 0.036f, 0.98f);
    [SerializeField] private Color paperColor = new(0.92f, 0.94f, 0.97f, 1f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentCyan = new(0.15f, 0.88f, 0.92f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField] private Color lockedColor = new(0.070f, 0.075f, 0.085f, 0.92f);
    [SerializeField, Min(1f)] private float uiSharpness = 16f;
    [SerializeField, Range(0.18f, 0.55f)] private float packHandoffScale = 0.34f;
    [SerializeField, Range(0.18f, 0.55f)] private float packMorphDuration = 0.34f;
    [SerializeField] private Vector2 packFocusedOffset = new(54f, 18f);
    [SerializeField] private Vector2 packInactiveCornerOffset = new(-170f, -132f);

    [Header("Compact Vitals")]
    [SerializeField] private Color hpColor = new(0.95f, 0.18f, 0.30f, 1f);
    [SerializeField] private Color staminaColor = new(0.18f, 0.82f, 0.95f, 1f);
    [SerializeField] private Color vitalsTextColor = new(0.82f, 0.85f, 0.90f, 1f);

    [Header("Grid Mouse")]
    [SerializeField, Range(0.02f, 0.20f)] private float hoverExitGrace = 0.08f;

    private Canvas canvas;
    private CanvasGroup fullGroup;
    private RectTransform fullRoot;
    private RectTransform boardRoot;
    private CanvasGroup boardGroup;
    private RectTransform linkRoot;
    private RectTransform detailRoot;
    private CanvasGroup detailGroup;
    private Text detailTitle;
    private Text detailTags;
    private Text synergySummary;

    private CanvasGroup compactGroup;
    private RectTransform compactRoot;
    private Image compactIcon;
    private Text compactName;
    private Text compactPrompt;
    private RectTransform hpFillRect;
    private RectTransform staminaFillRect;
    private Text hpText;
    private Text staminaText;
    private BattleCombatTabFocus tabFocus = BattleCombatTabFocus.None;
    private float packMorphProgress;

    private readonly RectTransform[] slotRects = new RectTransform[SlotCount];
    private readonly Image[] slotBackgrounds = new Image[SlotCount];
    private readonly Outline[] slotOutlines = new Outline[SlotCount];
    private readonly Image[] slotIcons = new Image[SlotCount];
    private readonly Text[] slotNames = new Text[SlotCount];
    private readonly Text[] slotGrades = new Text[SlotCount];
    private readonly Text[] slotStates = new Text[SlotCount];
    private readonly List<GameObject> linkVisuals = new();

    private bool switchHeld;
    private bool boardWasShown;
    private bool directionMoved;
    private float switchPressedAt;
    private int selectedIndex = -1;
    private bool stickAxisLatched;
    private BattleEquipmentSystem subscribedEquipmentSystem;
    private BattleGridSynergyController subscribedGridSynergy;
    private BattleRunManager subscribedRunManager;
    private bool inputSubscribed;
    private bool combatActive;
    private bool lastNotifiedBoardVisible;
    private int hoveredSlot = -1;
    private int pendingHoverExitSlot = -1;
    private float hoverExitAt;

    public int SelectedIndex => selectedIndex;
    public bool SwitchHeld => switchHeld;
    public bool BoardWasShown => boardWasShown;
    public bool IsSwitchBoardOpen => combatActive && switchHeld && boardWasShown;
    public RectTransform FullRoot => fullRoot;
    public CanvasGroup FullGroup => fullGroup;
    public RectTransform GridBoard => boardRoot;
    public float PackMorphProgress => packMorphProgress;
    public RectTransform CompactRoot => compactRoot;
    public CanvasGroup CompactGroup => compactGroup;
    public int HoveredSlot => hoveredSlot;

    public event Action<bool> SwitchBoardVisibilityChanged;

    private void Awake()
    {
        ResolveReferences();
        EnsureUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        combatActive = ResolveCombatState();
        EnsureUi();
        Subscribe();
        SubscribeInput();
        if (switchHeld)
            EnterBulletTime();
        RefreshAll();
        lastNotifiedBoardVisible = IsSwitchBoardOpen;
        SwitchBoardVisibilityChanged?.Invoke(lastNotifiedBoardVisible);
    }

    private void OnDisable()
    {
        UnsubscribeInput();
        Unsubscribe();
        RestoreTimeScale();
    }

    private void OnDestroy()
    {
        UnsubscribeInput();
        RestoreTimeScale();
    }

    private void Update()
    {
        if (runManager == null || equipmentSystem == null || inputRouter == null || player == null)
        {
            ResolveReferences();
            Subscribe();
            SubscribeInput();
        }

        bool combat = combatActive;
        if ((!combat || BattlePauseController.IsPaused) && switchHeld)
            CancelSwitchMode();

        if (combat && !BattlePauseController.IsPaused)
            UpdateSwitchInput();

        UpdateHoverExitGrace();
        UpdateCompactVitals();
        UpdateGridRaycastState();
        UpdateUiAnimation(combat);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
        {
            runManager = FindFirstObjectByType<BattleRunManager>();
            combatActive = ResolveCombatState();
        }
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (gridSynergy == null)
            gridSynergy = FindFirstObjectByType<BattleGridSynergyController>();
        if (timeScaleController == null)
            timeScaleController = BattleTimeScaleController.ResolveOrCreate(this);
        if (inputRouter == null && Application.isPlaying)
            inputRouter = BattleInputRouter.ResolveOrCreate(this);
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
        if (miniPackUI == null)
            miniPackUI = FindFirstObjectByType<BattleKineticItemBarUI>(FindObjectsInactive.Include);
        if (dashboardController == null)
            dashboardController = FindFirstObjectByType<BattleBroadcastDashboardController>(FindObjectsInactive.Include);
    }

    private void Subscribe()
    {
        if (subscribedEquipmentSystem != equipmentSystem)
        {
            if (subscribedEquipmentSystem != null)
            {
                subscribedEquipmentSystem.InventoryChanged -= RefreshAll;
                subscribedEquipmentSystem.SlotCapacityChanged -= HandleCapacityChanged;
                subscribedEquipmentSystem.EquippedSlotChanged -= HandleEquippedChanged;
            }

            subscribedEquipmentSystem = equipmentSystem;
            if (subscribedEquipmentSystem != null)
            {
                subscribedEquipmentSystem.InventoryChanged += RefreshAll;
                subscribedEquipmentSystem.SlotCapacityChanged += HandleCapacityChanged;
                subscribedEquipmentSystem.EquippedSlotChanged += HandleEquippedChanged;
            }
        }

        if (subscribedGridSynergy != gridSynergy)
        {
            if (subscribedGridSynergy != null)
                subscribedGridSynergy.GridSynergiesChanged -= RefreshAll;

            subscribedGridSynergy = gridSynergy;
            if (subscribedGridSynergy != null)
                subscribedGridSynergy.GridSynergiesChanged += RefreshAll;
        }

        if (subscribedRunManager != runManager)
        {
            if (subscribedRunManager != null)
                subscribedRunManager.StateChanged -= HandleRunStateChanged;

            subscribedRunManager = runManager;
            if (subscribedRunManager != null)
                subscribedRunManager.StateChanged += HandleRunStateChanged;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedEquipmentSystem != null)
        {
            subscribedEquipmentSystem.InventoryChanged -= RefreshAll;
            subscribedEquipmentSystem.SlotCapacityChanged -= HandleCapacityChanged;
            subscribedEquipmentSystem.EquippedSlotChanged -= HandleEquippedChanged;
        }

        if (subscribedGridSynergy != null)
            subscribedGridSynergy.GridSynergiesChanged -= RefreshAll;

        if (subscribedRunManager != null)
            subscribedRunManager.StateChanged -= HandleRunStateChanged;

        subscribedEquipmentSystem = null;
        subscribedGridSynergy = null;
        subscribedRunManager = null;
    }

    private void SubscribeInput()
    {
        if (inputSubscribed || inputRouter == null)
            return;

        inputRouter.TabOpened += HandleTabOpened;
        inputRouter.TabClosed += HandleTabClosed;
        inputRouter.SlotPressed += HandleSlotPressed;
        inputSubscribed = true;
    }

    private void UnsubscribeInput()
    {
        if (!inputSubscribed)
            return;

        if (inputRouter != null)
        {
            inputRouter.TabOpened -= HandleTabOpened;
            inputRouter.TabClosed -= HandleTabClosed;
            inputRouter.SlotPressed -= HandleSlotPressed;
        }
        inputSubscribed = false;
    }

    private void HandleSlotPressed(int index)
    {
        if (runManager == null || equipmentSystem == null || BattlePauseController.IsPaused)
            return;
        if (!runManager.RunActive || runManager.State != BattleRunState.Combat)
            return;
        if (index < 0 || index >= equipmentSystem.UnlockedSlotCount)
            return;

        equipmentSystem.EquipSlot(index);
    }

    private bool ResolveCombatState()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Combat;
    }

    private void HandleRunStateChanged(BattleRunState _)
    {
        bool next = ResolveCombatState();
        if (combatActive == next)
            return;

        combatActive = next;
        if (!combatActive && switchHeld)
            CancelSwitchMode();

        NotifySwitchBoardVisibility();
        RefreshAll();
    }

    private void HandleTabOpened()
    {
        if (!combatActive || BattlePauseController.IsPaused || switchHeld)
            return;

        BeginSwitchMode();
    }

    private void HandleTabClosed()
    {
        if (!switchHeld)
            return;

        CompleteSwitchMode(Time.unscaledTime - switchPressedAt);
    }

    private void HandleCapacityChanged(int _)
    {
        RefreshAll();
    }

    private void HandleEquippedChanged(int index)
    {
        if (!switchHeld && index >= 0)
            selectedIndex = index;
        RefreshAll();
    }

    public bool SetSelectedIndexFromExternal(int index, bool markMoved = true)
    {
        ResolveReferences();
        if (equipmentSystem == null || !equipmentSystem.IsSlotUnlocked(index))
            return false;

        selectedIndex = index;
        if (markMoved)
            directionMoved = true;
        RefreshAll();
        return true;
    }

    public void ClearExternalSelection()
    {
        selectedIndex = -1;
        directionMoved = false;
        RefreshAll();
    }

    public void RefreshPresentation()
    {
        RefreshAll();
    }

    private void UpdateSwitchInput()
    {
        if (!switchHeld || inputRouter == null)
            return;

        float heldDuration = Time.unscaledTime - switchPressedAt;
        if (!boardWasShown && heldDuration >= Mathf.Max(0.05f, holdThreshold))
        {
            boardWasShown = true;
            NotifySwitchBoardVisibility();
            RefreshAll();
        }

        if (boardWasShown && inputRouter.TabHeld)
            UpdateGridNavigation();

        // Map disable/Scene gate 등으로 release 이벤트가 사라진 경우의 안전 복구입니다.
        if (!inputRouter.TabHeld)
            CompleteSwitchMode(heldDuration);
    }

    private void BeginSwitchMode()
    {
        if (equipmentSystem == null)
            return;

        switchHeld = true;
        boardWasShown = false;
        directionMoved = false;
        switchPressedAt = Time.unscaledTime;
        stickAxisLatched = false;

        int equipped = equipmentSystem.EquippedSlotIndex;
        selectedIndex = equipped >= 0 ? equipped : equipmentSystem.FindNextWeaponSlot(-1, 1);
        if (selectedIndex < 0)
            selectedIndex = 0;

        EnterBulletTime();
        RefreshAll();
    }

    private void CompleteSwitchMode(float heldDuration)
    {
        if (!switchHeld)
            return;

        int equipIndex = selectedIndex;
        bool quickTap = !boardWasShown && !directionMoved && heldDuration < Mathf.Max(0.05f, holdThreshold);
        if (quickTap && equipmentSystem != null)
        {
            int next = equipmentSystem.FindNextWeaponSlot(equipmentSystem.EquippedSlotIndex, 1);
            if (next >= 0)
                equipIndex = next;
        }

        if (equipmentSystem != null && equipIndex >= 0)
            equipmentSystem.EquipSlot(equipIndex);

        switchHeld = false;
        boardWasShown = false;
        directionMoved = false;
        NotifySwitchBoardVisibility();
        stickAxisLatched = false;
        RestoreTimeScale();
        RefreshAll();
    }

    private void CancelSwitchMode()
    {
        switchHeld = false;
        boardWasShown = false;
        directionMoved = false;
        stickAxisLatched = false;
        NotifySwitchBoardVisibility();
        RestoreTimeScale();
        RefreshAll();
    }

    private void EnterBulletTime()
    {
        ResolveReferences();
        if (timeScaleController == null)
            return;

        float scale = Mathf.Clamp(combatInventoryTimeScale, 0.02f, 0.20f);
        timeScaleController.Request(BattleTimeScaleController.Owner.CombatInventory, scale);
    }

    private void RestoreTimeScale()
    {
        timeScaleController?.Release(BattleTimeScaleController.Owner.CombatInventory);
    }

    private void UpdateGridNavigation()
    {
        if (inputRouter == null || inputRouter.LastDevice != BattleInputDevice.Gamepad)
        {
            stickAxisLatched = false;
            return;
        }

        Vector2 stick = inputRouter.Move;
        float magnitude = Mathf.Max(Mathf.Abs(stick.x), Mathf.Abs(stick.y));

        if (stickAxisLatched)
        {
            if (magnitude <= stickReleaseThreshold)
                stickAxisLatched = false;
            return;
        }

        if (magnitude < stickThreshold)
            return;

        int dx = 0;
        int dy = 0;
        if (Mathf.Abs(stick.x) >= Mathf.Abs(stick.y))
            dx = stick.x >= 0f ? 1 : -1;
        else
            dy = stick.y >= 0f ? -1 : 1;

        stickAxisLatched = true;
        MoveSelection(dx, dy);
    }

    private void MoveSelection(int dx, int dy)
    {
        if (equipmentSystem == null)
            return;

        Vector2Int grid = BattleEquipmentSystem.SlotIndexToGrid(selectedIndex);
        for (int step = 0; step < GridSize; step++)
        {
            grid.x += dx;
            grid.y += dy;
            if (grid.x < 0 || grid.x >= GridSize || grid.y < 0 || grid.y >= GridSize)
                return;

            int candidate = BattleEquipmentSystem.GridToSlotIndex(grid.x, grid.y);
            if (equipmentSystem.IsSlotUnlocked(candidate))
            {
                selectedIndex = candidate;
                directionMoved = true;
                RefreshAll();
                return;
            }
        }
    }

    private void EnsureUi()
    {
        if (canvas != null)
            return;

        GameObject canvasObject = new("BattleKineticLoadoutCanvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = CanvasSortingOrder;
        if (canvasObject.GetComponent<GraphicRaycaster>() == null)
            canvasObject.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildCompactUi(canvas.transform);
        BuildFullGridUi(canvas.transform);
        RefreshAll();
    }

    private void BuildCompactUi(Transform parent)
    {
        compactRoot = CreateRect(parent, "CurrentLoadoutChip", new Vector2(430f, 156f));
        compactRoot.anchorMin = compactRoot.anchorMax = new Vector2(1f, 0f);
        compactRoot.pivot = new Vector2(1f, 0f);
        compactRoot.anchoredPosition = new Vector2(-28f, 24f);
        compactRoot.localRotation = Quaternion.Euler(0f, 0f, -2.5f);

        Image back = compactRoot.gameObject.AddComponent<Image>();
        back.color = inkColor;
        back.raycastTarget = false;
        Outline outline = compactRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = accentCyan;
        outline.effectDistance = new Vector2(4f, -4f);

        compactGroup = compactRoot.gameObject.AddComponent<CanvasGroup>();
        compactGroup.blocksRaycasts = false;
        compactGroup.interactable = false;

        compactIcon = CreateImage(compactRoot, "Icon", new Vector2(58f, 58f));
        compactIcon.rectTransform.anchorMin = compactIcon.rectTransform.anchorMax = new Vector2(0f, 1f);
        compactIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        compactIcon.rectTransform.anchoredPosition = new Vector2(76f, -48f);
        compactIcon.preserveAspect = true;
        compactIcon.raycastTarget = false;

        compactName = CreateText(compactRoot, "NO WEAPON", 20, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
        SetAnchors(compactName.rectTransform, new Vector2(0.30f, 0.67f), new Vector2(0.96f, 0.90f));

        compactPrompt = CreateText(compactRoot, "TAB / LB  —  SWITCH", 11, FontStyle.Bold, TextAnchor.MiddleLeft, accentCyan);
        SetAnchors(compactPrompt.rectTransform, new Vector2(0.30f, 0.48f), new Vector2(0.96f, 0.62f));

        RectTransform vitals = CreateRect(compactRoot, "CompactVitals", Vector2.zero);
        SetAnchors(vitals, new Vector2(0.08f, 0.08f), new Vector2(0.96f, 0.45f));

        hpText = CreateText(vitals, "HP", 9, FontStyle.Bold, TextAnchor.MiddleLeft, vitalsTextColor);
        SetAnchors(hpText.rectTransform, new Vector2(0f, 0.58f), new Vector2(0.18f, 0.96f));
        hpFillRect = CreateProgressBar(vitals, "HP", new Vector2(0.18f, 0.63f), new Vector2(1f, 0.88f), hpColor);

        staminaText = CreateText(vitals, "ST", 9, FontStyle.Bold, TextAnchor.MiddleLeft, vitalsTextColor);
        SetAnchors(staminaText.rectTransform, new Vector2(0f, 0.05f), new Vector2(0.18f, 0.43f));
        staminaFillRect = CreateProgressBar(vitals, "ST", new Vector2(0.18f, 0.10f), new Vector2(1f, 0.35f), staminaColor);
    }

    private void BuildFullGridUi(Transform parent)
    {
        fullRoot = CreateRect(parent, "LoadoutSwitchFull", Vector2.zero);
        Stretch(fullRoot);
        fullGroup = fullRoot.gameObject.AddComponent<CanvasGroup>();
        fullGroup.alpha = 0f;
        fullGroup.blocksRaycasts = false;
        fullGroup.interactable = false;

        Image dim = fullRoot.gameObject.AddComponent<Image>();
        dim.color = new Color(0.012f, 0.013f, 0.016f, 0.58f);
        dim.raycastTarget = false;

        Text title = CreateText(fullRoot, "LOADOUT // SHIFT", 56, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.sizeDelta = new Vector2(720f, 90f);
        titleRect.anchoredPosition = new Vector2(76f, -74f);
        titleRect.localRotation = Quaternion.identity;

        Text sub = CreateText(fullRoot, "HOLD TAB / LB   •   SELECT SLOT   •   RELEASE TO EQUIP", 14, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.66f, 0.70f, 0.78f, 1f));
        RectTransform subRect = sub.rectTransform;
        subRect.anchorMin = subRect.anchorMax = new Vector2(0f, 1f);
        subRect.pivot = new Vector2(0f, 1f);
        subRect.sizeDelta = new Vector2(720f, 44f);
        subRect.anchoredPosition = new Vector2(92f, -150f);
        subRect.localRotation = Quaternion.identity;

        detailRoot = CreateRect(fullRoot, "DetailPanel", new Vector2(570f, 330f));
        detailGroup = detailRoot.gameObject.AddComponent<CanvasGroup>();
        detailRoot.anchorMin = detailRoot.anchorMax = new Vector2(0f, 0.5f);
        detailRoot.pivot = new Vector2(0f, 0.5f);
        detailRoot.anchoredPosition = new Vector2(98f, -90f);
        detailRoot.localRotation = Quaternion.identity;
        Image detailBack = detailRoot.gameObject.AddComponent<Image>();
        detailBack.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.96f);
        detailBack.raycastTarget = false;

        detailTitle = CreateText(detailRoot, "EMPTY", 29, FontStyle.Bold, TextAnchor.UpperLeft, paperColor);
        SetAnchors(detailTitle.rectTransform, new Vector2(0.07f, 0.67f), new Vector2(0.94f, 0.93f));
        detailTags = CreateText(detailRoot, "—", 13, FontStyle.Bold, TextAnchor.UpperLeft, accentCyan);
        SetAnchors(detailTags.rectTransform, new Vector2(0.07f, 0.37f), new Vector2(0.94f, 0.67f));
        synergySummary = CreateText(detailRoot, "GRID LINK 0", 15, FontStyle.Bold, TextAnchor.LowerLeft, accentCyan);
        SetAnchors(synergySummary.rectTransform, new Vector2(0.07f, 0.08f), new Vector2(0.94f, 0.37f));

        boardRoot = CreateRect(fullRoot, "GridBoard", new Vector2(662f, 662f));
        boardGroup = boardRoot.gameObject.AddComponent<CanvasGroup>();
        boardRoot.anchorMin = boardRoot.anchorMax = new Vector2(0.31f, 0.53f);
        boardRoot.anchoredPosition = Vector2.zero;
        boardRoot.localRotation = Quaternion.Euler(3.5f, -6f, -1.2f);
        Vector3 boardInitial = boardRoot.localPosition;
        boardInitial.z = 34f;
        boardRoot.localPosition = boardInitial;

        RectTransform boardBack = CreateRect(boardRoot, "BoardBack", new Vector2(632f, 632f));
        boardBack.anchorMin = boardBack.anchorMax = new Vector2(0.5f, 0.5f);
        Image boardBackImage = boardBack.gameObject.AddComponent<Image>();
        boardBackImage.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.96f);
        boardBackImage.raycastTarget = true;
        Outline boardOutline = boardBack.gameObject.AddComponent<Outline>();
        boardOutline.effectColor = new Color(paperColor.r, paperColor.g, paperColor.b, 0.42f);
        boardOutline.effectDistance = new Vector2(3f, -3f);

        BattlePackFocusPointerRelay packFocusRelay =
            boardRoot.gameObject.AddComponent<BattlePackFocusPointerRelay>();
        packFocusRelay.Configure(this);

        linkRoot = CreateRect(boardRoot, "SynergyLinks", new Vector2(632f, 632f));
        linkRoot.anchorMin = linkRoot.anchorMax = new Vector2(0.5f, 0.5f);

        const float cellSize = 184f;
        const float spacing = 18f;
        float totalSize = GridSize * cellSize + (GridSize - 1) * spacing;
        float left = -totalSize * 0.5f + cellSize * 0.5f;
        float top = totalSize * 0.5f - cellSize * 0.5f;

        for (int i = 0; i < SlotCount; i++)
        {
            int x = i % GridSize;
            int y = i / GridSize;
            RectTransform slot = CreateRect(boardRoot, $"GridSlot_{i}", new Vector2(cellSize, cellSize));
            slot.anchorMin = slot.anchorMax = new Vector2(0.5f, 0.5f);
            slot.anchoredPosition = new Vector2(left + x * (cellSize + spacing), top - y * (cellSize + spacing));
            slot.localRotation = Quaternion.Euler(0f, 0f, ((i % 3) - 1) * 1.4f);
            slotRects[i] = slot;

            Image background = slot.gameObject.AddComponent<Image>();
            background.color = inkColor;
            background.raycastTarget = true;
            slotBackgrounds[i] = background;

            BattleLoadoutGridPointerTarget target = slot.gameObject.AddComponent<BattleLoadoutGridPointerTarget>();
            target.Configure(this, i);

            Outline outline = slot.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.44f, 0.48f, 0.56f, 0.28f);
            outline.effectDistance = new Vector2(1f, -1f);
            slotOutlines[i] = outline;

            Image icon = CreateImage(slot, "Icon", new Vector2(88f, 88f));
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.30f, 0.64f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            slotIcons[i] = icon;

            Text name = CreateText(slot, "EMPTY", 13, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
            SetAnchors(name.rectTransform, new Vector2(0.52f, 0.43f), new Vector2(0.94f, 0.80f));
            slotNames[i] = name;

            Text grade = CreateText(slot, string.Empty, 10, FontStyle.Bold, TextAnchor.UpperRight, new Color(0.62f, 0.67f, 0.76f, 1f));
            SetAnchors(grade.rectTransform, new Vector2(0.64f, 0.80f), new Vector2(0.93f, 0.94f));
            slotGrades[i] = grade;

            Text state = CreateText(slot, $"{x + 1}-{y + 1}", 10, FontStyle.Bold, TextAnchor.LowerLeft, accentCyan);
            SetAnchors(state.rectTransform, new Vector2(0.08f, 0.07f), new Vector2(0.94f, 0.27f));
            slotStates[i] = state;
        }
    }

    private void RefreshAll()
    {
        if (canvas == null || equipmentSystem == null)
            return;

        int equipped = equipmentSystem.EquippedSlotIndex;
        if (!switchHeld && equipped >= 0 && selectedIndex < 0)
            selectedIndex = equipped;

        for (int i = 0; i < SlotCount; i++)
        {
            bool unlocked = equipmentSystem.IsSlotUnlocked(i);
            BattleEquipmentSlot slot = i < equipmentSystem.Slots.Count ? equipmentSystem.Slots[i] : null;
            BattleEquipmentSO equipment = slot?.equipment;
            bool isEquipped = i == equipped;
            bool isSelected = switchHeld && boardWasShown && i == selectedIndex;

            if (slotBackgrounds[i] != null)
            {
                slotBackgrounds[i].color = !unlocked
                    ? lockedColor
                    : isSelected
                        ? new Color(accentCyan.r * 0.18f, accentCyan.g * 0.18f, accentCyan.b * 0.18f, 0.99f)
                        : isEquipped
                            ? new Color(accentCyan.r * 0.10f, accentCyan.g * 0.10f, accentCyan.b * 0.10f, 0.99f)
                            : new Color(inkColor.r, inkColor.g, inkColor.b, 0.98f);
            }

            if (slotOutlines[i] != null)
            {
                slotOutlines[i].effectColor = !unlocked
                    ? new Color(0.38f, 0.41f, 0.48f, 0.18f)
                    : isSelected
                        ? accentCyan
                        : isEquipped
                            ? new Color(accentCyan.r, accentCyan.g, accentCyan.b, 0.56f)
                            : new Color(0.55f, 0.59f, 0.66f, 0.26f);
                slotOutlines[i].effectDistance = isSelected
                    ? new Vector2(3f, -3f)
                    : isEquipped
                        ? new Vector2(2f, -2f)
                        : new Vector2(1f, -1f);
            }

            if (slotIcons[i] != null)
            {
                slotIcons[i].sprite = equipment != null ? equipment.icon : null;
                slotIcons[i].enabled = unlocked && equipment != null && equipment.icon != null;
                slotIcons[i].color = unlocked ? Color.white : new Color(0.46f, 0.49f, 0.56f, 0.52f);
            }

            if (slotNames[i] != null)
            {
                slotNames[i].text = !unlocked
                    ? "LOCKED"
                    : equipment != null
                        ? equipment.GetDisplayName().ToUpperInvariant()
                        : "EMPTY";
                slotNames[i].color = unlocked ? paperColor : new Color(0.48f, 0.51f, 0.58f, 1f);
            }

            if (slotGrades[i] != null)
            {
                slotGrades[i].text = unlocked && equipment != null ? $"GRADE {slot.grade}" : string.Empty;
                slotGrades[i].color = new Color(0.62f, 0.67f, 0.76f, 1f);
            }

            if (slotStates[i] != null)
            {
                Vector2Int p = BattleEquipmentSystem.SlotIndexToGrid(i);
                slotStates[i].text = !unlocked
                    ? $"{p.x + 1}-{p.y + 1}  // LOCKED"
                    : isEquipped && isSelected
                        ? "EQUIPPED  /  SELECTED"
                        : isSelected
                            ? "SELECTED  // RELEASE TO EQUIP"
                            : isEquipped
                                ? "EQUIPPED"
                                : $"{p.x + 1}-{p.y + 1}  // AVAILABLE";

                slotStates[i].color = !unlocked
                    ? new Color(0.42f, 0.45f, 0.52f, 1f)
                    : isSelected || isEquipped
                        ? accentCyan
                        : new Color(0.55f, 0.60f, 0.70f, 1f);
            }
        }

        RefreshCompact(equipped);
        RefreshDetail();
        RebuildSynergyLinks();
    }

    private void RefreshCompact(int equipped)
    {
        BattleEquipmentSO equipment = null;
        if (equipped >= 0 && equipped < equipmentSystem.Slots.Count)
            equipment = equipmentSystem.Slots[equipped]?.equipment;

        if (compactIcon != null)
        {
            compactIcon.sprite = equipment != null ? equipment.icon : null;
            compactIcon.enabled = equipment != null && equipment.icon != null;
        }
        if (compactName != null)
            compactName.text = equipment != null ? equipment.GetDisplayName().ToUpperInvariant() : "NO MANUAL WEAPON";
        if (compactPrompt != null)
            compactPrompt.text = "TAB / LB  —  TAP NEXT  /  HOLD GRID";
    }

    private void RefreshDetail()
    {
        if (equipmentSystem == null || selectedIndex < 0 || selectedIndex >= equipmentSystem.Slots.Count)
            return;

        BattleEquipmentSlot slot = equipmentSystem.Slots[selectedIndex];
        BattleEquipmentSO equipment = slot?.equipment;
        if (detailTitle != null)
            detailTitle.text = equipment != null ? equipment.GetDisplayName().ToUpperInvariant() : "EMPTY SLOT";

        if (detailTags != null)
        {
            if (equipment == null || equipment.tags == null || equipment.tags.Count == 0)
                detailTags.text = "NO TAG";
            else
                detailTags.text = string.Join("  /  ", equipment.tags).ToUpperInvariant();
        }

        if (synergySummary != null)
        {
            int count = 0;
            List<string> names = new();
            if (gridSynergy != null)
            {
                IReadOnlyList<BattleGridSynergyLink> links = gridSynergy.ActiveLinks;
                for (int i = 0; i < links.Count; i++)
                {
                    BattleGridSynergyLink link = links[i];
                    if (link.slotA != selectedIndex && link.slotB != selectedIndex)
                        continue;
                    count++;
                    if (!names.Contains(link.displayName))
                        names.Add(link.displayName);
                }
            }

            float bonus = gridSynergy != null ? (gridSynergy.GridDamageMultiplier - 1f) * 100f : 0f;
            string linkNames = names.Count > 0 ? string.Join(" + ", names) : "NO ACTIVE LINK";
            synergySummary.text = $"GRID LINK {count}\n{linkNames}\nTOTAL GRID DMG +{bonus:0.#}%";
        }
    }

    private void RebuildSynergyLinks()
    {
        for (int i = 0; i < linkVisuals.Count; i++)
            if (linkVisuals[i] != null)
                Destroy(linkVisuals[i]);
        linkVisuals.Clear();

        if (linkRoot == null || gridSynergy == null)
            return;

        IReadOnlyList<BattleGridSynergyLink> links = gridSynergy.ActiveLinks;
        for (int i = 0; i < links.Count; i++)
        {
            BattleGridSynergyLink link = links[i];
            if (link.slotA < 0 || link.slotA >= SlotCount || link.slotB < 0 || link.slotB >= SlotCount)
                continue;
            if (slotRects[link.slotA] == null || slotRects[link.slotB] == null)
                continue;

            Vector2 from = slotRects[link.slotA].anchoredPosition;
            Vector2 to = slotRects[link.slotB].anchoredPosition;
            Vector2 delta = to - from;

            GameObject lineObject = new($"Link_{link.slotA}_{link.slotB}_{link.kind}");
            lineObject.transform.SetParent(linkRoot, false);
            RectTransform line = lineObject.AddComponent<RectTransform>();
            line.anchorMin = line.anchorMax = new Vector2(0.5f, 0.5f);
            line.pivot = new Vector2(0f, 0.5f);
            line.anchoredPosition = from;
            line.sizeDelta = new Vector2(delta.magnitude, 10f);
            line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

            Image image = lineObject.AddComponent<Image>();
            image.color = ResolveLinkColor(link.kind);
            image.raycastTarget = false;
            lineObject.transform.SetAsFirstSibling();
            linkVisuals.Add(lineObject);
        }
    }

    private Color ResolveLinkColor(BattleGridSynergyKind kind)
    {
        return kind switch
        {
            BattleGridSynergyKind.DetonationChain => accentPink,
            BattleGridSynergyKind.PrecisionCircuit => accentYellow,
            BattleGridSynergyKind.ShockControl => accentCyan,
            BattleGridSynergyKind.RushMelee => accentPink,
            BattleGridSynergyKind.SustainGuard => new Color(0.45f, 0.95f, 0.60f, 1f),
            _ => new Color(accentCyan.r, accentCyan.g, accentCyan.b, 0.72f)
        };
    }

    internal void HandleSlotHover(int index, bool entered)
    {
        if (!entered)
        {
            if (hoveredSlot == index)
            {
                pendingHoverExitSlot = index;
                hoverExitAt = Time.unscaledTime + Mathf.Clamp(hoverExitGrace, 0.02f, 0.20f);
            }
            return;
        }

        if (equipmentSystem == null || !IsSwitchBoardOpen || !equipmentSystem.IsSlotUnlocked(index))
            return;

        dashboardController?.SetPackFocus(true);
        hoveredSlot = index;
        pendingHoverExitSlot = -1;
        hoverExitAt = 0f;

        if (selectedIndex != index)
            SetSelectedIndexFromExternal(index, true);
    }

    internal void HandleSlotClick(int index, PointerEventData.InputButton button)
    {
        if (button != PointerEventData.InputButton.Left ||
            equipmentSystem == null ||
            !IsSwitchBoardOpen ||
            !equipmentSystem.IsSlotUnlocked(index))
            return;

        hoveredSlot = index;
        pendingHoverExitSlot = -1;

        if (selectedIndex != index)
            SetSelectedIndexFromExternal(index, true);

        equipmentSystem.EquipSlot(index);
    }

    public void ClearPackHoverImmediate()
    {
        hoveredSlot = -1;
        pendingHoverExitSlot = -1;
        hoverExitAt = 0f;
    }

    private void UpdateHoverExitGrace()
    {
        if (pendingHoverExitSlot < 0 || Time.unscaledTime < hoverExitAt)
            return;

        if (hoveredSlot == pendingHoverExitSlot)
            hoveredSlot = -1;

        pendingHoverExitSlot = -1;
        hoverExitAt = 0f;
    }

    private void UpdateGridRaycastState()
    {
        if (fullGroup == null)
            return;

        bool active = IsSwitchBoardOpen && fullGroup.alpha > 0.05f;
        fullGroup.blocksRaycasts = active;
        fullGroup.interactable = active;

        if (!active)
            ClearPackHoverImmediate();
    }

    private void UpdateCompactVitals()
    {
        if (player == null || compactRoot == null)
            return;

        float hpMax = Mathf.Max(1f, player.maxHp);
        float staminaMax = Mathf.Max(1f, player.maxStamina);
        float hp01 = Mathf.Clamp01(player.CurrentHp / hpMax);
        float stamina01 = Mathf.Clamp01(player.CurrentStamina / staminaMax);

        SetBarAmount(hpFillRect, hp01);
        SetBarAmount(staminaFillRect, stamina01);

        if (hpText != null)
            hpText.text = $"HP {player.CurrentHp:0}/{hpMax:0}";
        if (staminaText != null)
            staminaText.text = $"ST {player.CurrentStamina:0}/{staminaMax:0}";
    }

    private static RectTransform CreateProgressBar(
        Transform parent,
        string name,
        Vector2 min,
        Vector2 max,
        Color color)
    {
        RectTransform background = CreateRect(parent, name + "Bar_BG", Vector2.zero);
        SetAnchors(background, min, max);
        Image bg = background.gameObject.AddComponent<Image>();
        bg.color = new Color(0.080f, 0.085f, 0.095f, 0.96f);
        bg.raycastTarget = false;

        RectTransform fill = CreateRect(background, name + "Bar_Fill", Vector2.zero);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = new Vector2(2f, 2f);
        fill.offsetMax = new Vector2(-2f, -2f);
        fill.pivot = new Vector2(0f, 0.5f);

        Image image = fill.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return fill;
    }

    private static void SetBarAmount(RectTransform fill, float amount)
    {
        if (fill == null)
            return;

        amount = Mathf.Clamp01(amount);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(amount, 1f);
        fill.offsetMin = new Vector2(2f, 2f);
        fill.offsetMax = new Vector2(-2f, -2f);
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject go = new("BattleLoadoutEventSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    private void UpdateUiAnimation(bool combat)
    {
        if (fullGroup == null || compactGroup == null)
            return;

        bool wantFull = combat && switchHeld && boardWasShown;

        float morphStep = Time.unscaledDeltaTime /
                          Mathf.Max(0.18f, packMorphDuration);
        packMorphProgress = Mathf.MoveTowards(
            packMorphProgress,
            wantFull ? 1f : 0f,
            morphStep);

        float morph = SmoothPackMorph(packMorphProgress);
        float fullReveal = SmoothPackRange(packMorphProgress, 0.46f, 0.90f);
        bool morphVisible = combat && packMorphProgress > 0.001f;

        bool packFocused = wantFull && tabFocus == BattleCombatTabFocus.Pack;
        bool packSuppressed =
            wantFull &&
            tabFocus != BattleCombatTabFocus.None &&
            tabFocus != BattleCombatTabFocus.Pack;
        float t = 1f - Mathf.Exp(-Mathf.Max(1f, uiSharpness) * Time.unscaledDeltaTime);

        fullGroup.alpha = fullReveal;
        compactGroup.alpha = Mathf.Lerp(
            compactGroup.alpha,
            combat && !wantFull ? 1f : 0f,
            t);
        fullGroup.blocksRaycasts = wantFull && packMorphProgress >= 0.72f;
        fullGroup.interactable = fullGroup.blocksRaycasts;

        if (fullRoot != null)
        {
            fullRoot.localScale = Vector3.Lerp(
                fullRoot.localScale,
                Vector3.one * (morphVisible ? 1f : 0.985f),
                t);

            Vector3 local = fullRoot.localPosition;
            local.z = Mathf.Lerp(local.z, morphVisible ? -14f : 42f, t);
            fullRoot.localPosition = local;

            Quaternion targetRotation = Quaternion.Euler(
                morphVisible ? 0.6f : 3.5f,
                morphVisible ? -1.4f : -5.5f,
                morphVisible ? -0.35f : -1.2f);
            fullRoot.localRotation = Quaternion.Slerp(fullRoot.localRotation, targetRotation, t);
        }

        if (compactRoot != null)
        {
            bool compactActive = combat && !wantFull;
            Vector2 compactTarget = new(-28f, 24f);

            compactRoot.anchoredPosition = Vector2.Lerp(
                compactRoot.anchoredPosition,
                compactTarget,
                t);
            compactRoot.localScale = Vector3.Lerp(
                compactRoot.localScale,
                Vector3.one * (compactActive ? 1f : wantFull ? 1.08f : 0.92f),
                t);

            Vector3 local = compactRoot.localPosition;
            local.z = Mathf.Lerp(local.z, compactActive ? -8f : wantFull ? -18f : 28f, t);
            compactRoot.localPosition = local;

            Quaternion targetRotation = Quaternion.Euler(
                compactActive ? 0.4f : wantFull ? 0.2f : 2.2f,
                compactActive ? -1.6f : wantFull ? -0.6f : -4.8f,
                compactActive ? -1.4f : wantFull ? -0.2f : -2.4f);
            compactRoot.localRotation = Quaternion.Slerp(compactRoot.localRotation, targetRotation, t);
        }

        if (boardRoot != null)
        {
            Vector2 boardTarget = packFocused
                ? packFocusedOffset
                : packSuppressed
                    ? packInactiveCornerOffset
                    : Vector2.zero;

            float packScale = packFocused
                ? 1.075f
                : packSuppressed
                    ? 0.76f
                    : Mathf.Lerp(0.88f, 0.92f, morph);

            boardRoot.anchoredPosition = Vector2.Lerp(
                boardRoot.anchoredPosition,
                boardTarget,
                t);
            boardRoot.localRotation = Quaternion.Slerp(
                boardRoot.localRotation,
                packFocused
                    ? Quaternion.Euler(0.1f, -0.4f, -0.08f)
                    : packSuppressed
                        ? Quaternion.Euler(3.4f, -7.0f, -1.2f)
                        : Quaternion.Euler(1.1f, -2.6f, -0.55f),
                t);

            Vector3 local = boardRoot.localPosition;
            local.z = Mathf.Lerp(
                local.z,
                packFocused ? -22f : packSuppressed ? 24f : 2f,
                t);
            boardRoot.localPosition = local;

            boardRoot.localScale = Vector3.Lerp(
                boardRoot.localScale,
                Vector3.one * packScale,
                t);

            if (boardGroup != null)
            {
                float focusAlpha = packFocused
                    ? 1f
                    : packSuppressed ? 0.38f : 0.72f;
                boardGroup.alpha = fullReveal * focusAlpha;
            }
        }

        if (detailRoot != null)
        {
            Vector2 detailTarget = !wantFull
                ? new Vector2(98f, -90f)
                : packFocused
                    ? new Vector2(88f, -72f)
                    : packSuppressed
                        ? new Vector2(54f, -214f)
                        : new Vector2(98f, -90f);

            detailRoot.anchoredPosition = Vector2.Lerp(
                detailRoot.anchoredPosition,
                detailTarget,
                t);
            detailRoot.localScale = Vector3.Lerp(
                detailRoot.localScale,
                Vector3.one * (
                    packFocused ? 1.04f : packSuppressed ? 0.78f : 0.90f),
                t);

            Vector3 detailLocal = detailRoot.localPosition;
            detailLocal.z = Mathf.Lerp(
                detailLocal.z,
                packFocused ? -16f : packSuppressed ? 20f : 4f,
                t);
            detailRoot.localPosition = detailLocal;

            if (detailGroup != null)
            {
                float detailReveal = SmoothPackRange(
                    packMorphProgress,
                    0.62f,
                    0.96f);
                float focusAlpha = packFocused
                    ? 1f
                    : packSuppressed ? 0.34f : 0.68f;
                detailGroup.alpha = detailReveal * focusAlpha;
            }
        }

        if (equipmentSystem != null)
        {
            int equipped = equipmentSystem.EquippedSlotIndex;

            for (int i = 0; i < slotRects.Length; i++)
            {
                RectTransform slot = slotRects[i];
                if (slot == null)
                    continue;

                bool unlocked = equipmentSystem.IsSlotUnlocked(i);
                bool selected = wantFull && i == selectedIndex;
                bool hovered = wantFull && i == hoveredSlot;
                bool equippedSlot = i == equipped;
                Vector2Int grid = BattleEquipmentSystem.SlotIndexToGrid(i);
                float side = grid.x - 1f;

                float targetZ = !wantFull
                    ? 12f
                    : hovered
                        ? -30f
                        : selected
                            ? -24f
                            : equippedSlot
                                ? -9f
                                : unlocked ? 3f : 16f;

                Vector3 local = slot.localPosition;
                local.z = Mathf.Lerp(local.z, targetZ, t);
                slot.localPosition = local;

                Quaternion targetRotation = !wantFull
                    ? Quaternion.Euler(3.2f, -side * 5.5f, side * 1.4f)
                    : hovered
                        ? Quaternion.Euler(-0.35f, side * 0.35f, -side * 0.12f)
                        : selected
                            ? Quaternion.identity
                            : equippedSlot
                                ? Quaternion.Euler(0.5f, -side * 1.2f, side * 0.25f)
                                : Quaternion.Euler(
                                    unlocked ? 1.4f : 2.8f,
                                    -side * (unlocked ? 3.0f : 5.0f),
                                    side * (unlocked ? 0.7f : 1.2f));

                slot.localRotation = Quaternion.Slerp(slot.localRotation, targetRotation, t);

                float targetScale = !wantFull
                    ? 0.97f
                    : hovered
                        ? 1.075f
                        : selected
                            ? 1.06f
                            : equippedSlot ? 1.025f : unlocked ? 1f : 0.985f;

                slot.localScale = Vector3.Lerp(
                    slot.localScale,
                    Vector3.one * targetScale,
                    t);
            }
        }
    }

    private static float SmoothPackMorph(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static float SmoothPackRange(float value, float start, float end)
    {
        if (end <= start + 0.0001f)
            return value >= end ? 1f : 0f;

        float t = Mathf.Clamp01((value - start) / (end - start));
        return t * t * (3f - 2f * t);
    }

    public void SetTabFocusState(BattleCombatTabFocus next)
    {
        tabFocus = IsSwitchBoardOpen ? next : BattleCombatTabFocus.None;
    }

    internal void HandleBoardFocus(bool entered)
    {
        if (!IsSwitchBoardOpen)
            return;

        dashboardController?.SetPackFocus(entered);
        if (!entered)
            ClearPackHoverImmediate();
    }

    private void NotifySwitchBoardVisibility()
    {
        bool visible = IsSwitchBoardOpen;
        if (visible == lastNotifiedBoardVisible)
            return;

        lastNotifiedBoardVisible = visible;
        SwitchBoardVisibilityChanged?.Invoke(visible);
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static Image CreateImage(Transform parent, string name, Vector2 size)
    {
        RectTransform rect = CreateRect(parent, name, size);
        return rect.gameObject.AddComponent<Image>();
    }

    private static Text CreateText(Transform parent, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        RectTransform rect = CreateRect(parent, "Text", Vector2.zero);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

internal sealed class BattlePackFocusPointerRelay : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private BattleKineticLoadoutUI owner;

    public void Configure(BattleKineticLoadoutUI target)
    {
        owner = target;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        owner?.HandleBoardFocus(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.HandleBoardFocus(false);
    }
}

internal sealed class BattleLoadoutGridPointerTarget : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    private BattleKineticLoadoutUI owner;
    private int slotIndex;

    public void Configure(BattleKineticLoadoutUI target, int index)
    {
        owner = target;
        slotIndex = index;
    }

    public void OnPointerEnter(PointerEventData eventData) => owner?.HandleSlotHover(slotIndex, true);
    public void OnPointerExit(PointerEventData eventData) => owner?.HandleSlotHover(slotIndex, false);
    public void OnPointerClick(PointerEventData eventData) => owner?.HandleSlotClick(slotIndex, eventData.button);
}

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
    private enum TabTimeState
    {
        Running,
        Stopping,
        Stopped,
        Resuming
    }

    private const int GridSize = BattleEquipmentSystem.GridSize;
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const int CanvasSortingOrder = 780;
    private const int TabNoiseBandCount = 12;

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
    [SerializeField, Range(0.12f, 0.80f)] private float tabStopDuration = 0.34f;
    [SerializeField, Range(0.12f, 0.90f)] private float tabResumeDuration = 0.44f;
    [SerializeField, Range(8f, 60f)] private float tabSignalFrequency = 34f;
    [SerializeField, Range(0f, 0.24f)] private float tabExtraDimAlpha = 0.10f;
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
    [SerializeField, Range(0.18f, 0.55f)] private float packMorphDuration = 0.34f;
    [SerializeField] private Vector2 packRestOffset = new(-248f, -62f);
    [SerializeField] private Vector2 packFocusedOffset = new(-170f, -76f);
    [SerializeField] private Vector2 packInactiveCornerOffset = new(-378f, -146f);
    [SerializeField] private Vector2 itemTooltipSize = new(360f, 220f);
    [SerializeField, Min(4f)] private float itemTooltipGap = 18f;

    [Header("Compact Vitals")]
    [SerializeField] private Color hpColor = new(0.95f, 0.18f, 0.30f, 1f);
    [SerializeField] private Color staminaColor = new(0.18f, 0.82f, 0.95f, 1f);
    [SerializeField] private Color vitalsTextColor = new(0.82f, 0.85f, 0.90f, 1f);

    [Header("Grid Mouse")]
    [SerializeField, Range(0.02f, 0.20f)] private float hoverExitGrace = 0.08f;

    private Canvas canvas;
    private CanvasGroup fullGroup;
    private RectTransform fullRoot;
    private Image fullDimImage;

    private CanvasGroup tabHoldSignalGroup;
    private RectTransform tabHoldGlyphRoot;
    private RectTransform tabHoldBarLeft;
    private RectTransform tabHoldBarRight;
    private Image tabHoldBarLeftImage;
    private Image tabHoldBarRightImage;
    private readonly RectTransform[] tabNoiseBands = new RectTransform[TabNoiseBandCount];
    private readonly Image[] tabNoiseBandImages = new Image[TabNoiseBandCount];
    private readonly float[] tabNoiseBandSeeds = new float[TabNoiseBandCount];
    private readonly float[] tabNoiseBandBaseY = new float[TabNoiseBandCount];
    private readonly float[] tabNoiseBandWidth = new float[TabNoiseBandCount];
    private readonly float[] tabNoiseBandSpeed = new float[TabNoiseBandCount];
    private Text tabTimeFlowText;
    private RectTransform boardRoot;
    private CanvasGroup boardGroup;
    private RectTransform linkRoot;
    private RectTransform detailRoot;
    private CanvasGroup detailGroup;
    private Text detailTitle;
    private Text detailDescription;
    private Text detailTags;
    private Text synergySummary;

    private RectTransform compareRoot;
    private Image compareSourceIcon;
    private Image compareTargetIcon;
    private Text compareSourceName;
    private Text compareTargetName;
    private Text compareSourceMeta;
    private Text compareTargetMeta;
    private Text compareSwapLabel;
    private Text compareDeltaText;

    private CanvasGroup compactGroup;
    private RectTransform compactRoot;
    private Image compactIcon;
    private Text compactName;
    private Text compactPrompt;
    private RectTransform hpFillRect;
    private RectTransform staminaFillRect;
    private Text hpText;
    private Text staminaText;
    private Vector2 hpTextRestPosition;
    private bool hpTextRestCaptured;
    private BattleCombatTabFocus tabFocus = BattleCombatTabFocus.None;
    private float packMorphProgress;

    private TabTimeState tabTimeState = TabTimeState.Running;
    private float tabTimeElapsed;
    private float tabTimeStartScale = 1f;
    private float tabTimeStartVisual;
    private float tabRequestedScale = 1f;
    private float tabHoldVisual;

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
    private Rect hoveredSlotEntryScreenRect;
    private bool hoveredSlotEntryRectValid;

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
            BeginTabTimeStop();
        RefreshAll();
        lastNotifiedBoardVisible = IsSwitchBoardOpen;
        SwitchBoardVisibilityChanged?.Invoke(lastNotifiedBoardVisible);
    }

    private void OnDisable()
    {
        UnsubscribeInput();
        Unsubscribe();
        ForceRestoreTabTime();
    }

    private void OnDestroy()
    {
        UnsubscribeInput();
        ForceRestoreTabTime();
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

        UpdateTabTimeTransition();
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

        BeginTabTimeStop();
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
        BeginTabTimeResume();
        RefreshAll();
    }

    private void CancelSwitchMode()
    {
        switchHeld = false;
        boardWasShown = false;
        directionMoved = false;
        stickAxisLatched = false;
        NotifySwitchBoardVisibility();
        BeginTabTimeResume();
        RefreshAll();
    }

    private void BeginTabTimeStop()
    {
        ResolveReferences();
        if (timeScaleController == null)
            return;

        tabTimeStartScale = Mathf.Clamp01(timeScaleController.AppliedScale);
        tabTimeStartVisual = tabHoldVisual;
        tabRequestedScale = tabTimeStartScale;
        tabTimeElapsed = 0f;
        tabTimeState = TabTimeState.Stopping;

        timeScaleController.Request(
            BattleTimeScaleController.Owner.CombatInventory,
            tabRequestedScale);
    }

    private void BeginTabTimeResume()
    {
        ResolveReferences();
        if (timeScaleController == null)
            return;

        if (tabTimeState == TabTimeState.Running)
        {
            timeScaleController.Release(BattleTimeScaleController.Owner.CombatInventory);
            return;
        }

        tabTimeStartScale = Mathf.Clamp01(timeScaleController.AppliedScale);
        tabTimeStartVisual = tabHoldVisual;
        tabRequestedScale = tabTimeStartScale;
        tabTimeElapsed = 0f;
        tabTimeState = TabTimeState.Resuming;

        timeScaleController.Request(
            BattleTimeScaleController.Owner.CombatInventory,
            tabRequestedScale);
    }

    private void UpdateTabTimeTransition()
    {
        if (timeScaleController == null)
            return;

        switch (tabTimeState)
        {
            case TabTimeState.Running:
                tabHoldVisual = Mathf.MoveTowards(
                    tabHoldVisual,
                    0f,
                    Time.unscaledDeltaTime * 4f);
                return;

            case TabTimeState.Stopping:
            {
                tabTimeElapsed += Time.unscaledDeltaTime;
                float duration = Mathf.Max(0.01f, tabStopDuration);
                float t = Mathf.Clamp01(tabTimeElapsed / duration);
                float eased = EaseOutCubic(t);

                tabRequestedScale = Mathf.Lerp(
                    tabTimeStartScale,
                    0f,
                    eased);
                timeScaleController.Request(
                    BattleTimeScaleController.Owner.CombatInventory,
                    tabRequestedScale);

                tabHoldVisual = Mathf.Lerp(
                    tabTimeStartVisual,
                    1f,
                    eased);

                if (t >= 1f)
                {
                    tabRequestedScale = 0f;
                    tabHoldVisual = 1f;
                    tabTimeState = TabTimeState.Stopped;
                    timeScaleController.Request(
                        BattleTimeScaleController.Owner.CombatInventory,
                        0f);
                }
                return;
            }

            case TabTimeState.Stopped:
                tabRequestedScale = 0f;
                tabHoldVisual = 1f;
                timeScaleController.Request(
                    BattleTimeScaleController.Owner.CombatInventory,
                    0f);
                return;

            case TabTimeState.Resuming:
            {
                tabTimeElapsed += Time.unscaledDeltaTime;
                float duration = Mathf.Max(0.01f, tabResumeDuration);
                float t = Mathf.Clamp01(tabTimeElapsed / duration);
                float eased = EaseInOutCubic(t);

                tabRequestedScale = Mathf.Lerp(
                    tabTimeStartScale,
                    1f,
                    eased);
                timeScaleController.Request(
                    BattleTimeScaleController.Owner.CombatInventory,
                    tabRequestedScale);

                tabHoldVisual = Mathf.Lerp(
                    tabTimeStartVisual,
                    0f,
                    eased);

                if (t >= 1f)
                {
                    timeScaleController.Release(
                        BattleTimeScaleController.Owner.CombatInventory);
                    tabRequestedScale = 1f;
                    tabHoldVisual = 0f;
                    tabTimeElapsed = 0f;
                    tabTimeState = TabTimeState.Running;
                }
                return;
            }
        }
    }

    private void ForceRestoreTabTime()
    {
        timeScaleController?.Release(
            BattleTimeScaleController.Owner.CombatInventory);
        tabRequestedScale = 1f;
        tabHoldVisual = 0f;
        tabTimeElapsed = 0f;
        tabTimeStartScale = 1f;
        tabTimeStartVisual = 0f;
        tabTimeState = TabTimeState.Running;
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

    private void BuildTabHoldSignal(Transform parent)
    {
        RectTransform root = CreateRect(parent, "TabTimeHoldSignal", Vector2.zero);
        Stretch(root);

        tabHoldSignalGroup = root.gameObject.AddComponent<CanvasGroup>();
        tabHoldSignalGroup.alpha = 0f;
        tabHoldSignalGroup.blocksRaycasts = false;
        tabHoldSignalGroup.interactable = false;

        BuildTabScreenFrame(root);
        BuildTabNoiseBands(root);

        tabHoldGlyphRoot = CreateRect(
            root,
            "TimeHoldGlyph",
            new Vector2(116f, 78f));
        tabHoldGlyphRoot.anchorMin =
            tabHoldGlyphRoot.anchorMax =
                new Vector2(0.5f, 0.86f);
        tabHoldGlyphRoot.pivot = new Vector2(0.5f, 0.5f);
        tabHoldGlyphRoot.anchoredPosition = Vector2.zero;

        tabHoldBarLeft = CreateRect(
            tabHoldGlyphRoot,
            "PauseBarLeft",
            new Vector2(12f, 42f));
        tabHoldBarLeft.anchorMin =
            tabHoldBarLeft.anchorMax =
                new Vector2(0.5f, 0.5f);
        tabHoldBarLeft.anchoredPosition = new Vector2(-13f, 0f);
        tabHoldBarLeftImage = tabHoldBarLeft.gameObject.AddComponent<Image>();
        tabHoldBarLeftImage.color = accentCyan;
        tabHoldBarLeftImage.raycastTarget = false;

        tabHoldBarRight = CreateRect(
            tabHoldGlyphRoot,
            "PauseBarRight",
            new Vector2(12f, 42f));
        tabHoldBarRight.anchorMin =
            tabHoldBarRight.anchorMax =
                new Vector2(0.5f, 0.5f);
        tabHoldBarRight.anchoredPosition = new Vector2(13f, 0f);
        tabHoldBarRightImage = tabHoldBarRight.gameObject.AddComponent<Image>();
        tabHoldBarRightImage.color = accentCyan;
        tabHoldBarRightImage.raycastTarget = false;

        tabTimeFlowText = CreateText(
            root,
            "TIME FLOW 1.00x",
            10,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Color(0.72f, 0.76f, 0.82f, 1f));
        SetAnchors(
            tabTimeFlowText.rectTransform,
            new Vector2(0.42f, 0.79f),
            new Vector2(0.58f, 0.825f));
    }

    private void BuildTabScreenFrame(RectTransform root)
    {
        BuildScreenCorner(root, "FrameTL", new Vector2(0f, 1f), true, true);
        BuildScreenCorner(root, "FrameTR", new Vector2(1f, 1f), false, true);
        BuildScreenCorner(root, "FrameBL", new Vector2(0f, 0f), true, false);
        BuildScreenCorner(root, "FrameBR", new Vector2(1f, 0f), false, false);
    }

    private void BuildScreenCorner(
        Transform parent,
        string name,
        Vector2 anchor,
        bool left,
        bool top)
    {
        RectTransform corner = CreateRect(parent, name, new Vector2(96f, 96f));
        corner.anchorMin = corner.anchorMax = anchor;
        corner.pivot = anchor;
        corner.anchoredPosition = new Vector2(
            left ? 28f : -28f,
            top ? -28f : 28f);

        RectTransform horizontal = CreateRect(
            corner,
            "H",
            new Vector2(72f, 3f));
        horizontal.anchorMin =
            horizontal.anchorMax =
                new Vector2(left ? 0f : 1f, top ? 1f : 0f);
        horizontal.pivot = new Vector2(left ? 0f : 1f, 0.5f);
        horizontal.anchoredPosition = Vector2.zero;
        Image h = horizontal.gameObject.AddComponent<Image>();
        h.color = new Color(
            paperColor.r,
            paperColor.g,
            paperColor.b,
            0.62f);
        h.raycastTarget = false;

        RectTransform vertical = CreateRect(
            corner,
            "V",
            new Vector2(3f, 72f));
        vertical.anchorMin =
            vertical.anchorMax =
                new Vector2(left ? 0f : 1f, top ? 1f : 0f);
        vertical.pivot = new Vector2(0.5f, top ? 1f : 0f);
        vertical.anchoredPosition = Vector2.zero;
        Image v = vertical.gameObject.AddComponent<Image>();
        v.color = new Color(
            paperColor.r,
            paperColor.g,
            paperColor.b,
            0.62f);
        v.raycastTarget = false;
    }

    private void BuildTabNoiseBands(RectTransform root)
    {
        for (int i = 0; i < TabNoiseBandCount; i++)
        {
            float seed = 17.17f + i * 9.731f;
            tabNoiseBandSeeds[i] = seed;
            tabNoiseBandBaseY[i] = Mathf.Lerp(
                -0.46f,
                0.46f,
                Hash01(seed * 1.37f));
            tabNoiseBandWidth[i] = Mathf.Lerp(
                0.18f,
                0.88f,
                Hash01(seed * 2.11f));
            tabNoiseBandSpeed[i] = Mathf.Lerp(
                5.5f,
                21f,
                Hash01(seed * 3.07f));

            RectTransform band = CreateRect(
                root,
                $"NoiseBand_{i:00}",
                new Vector2(0f, 2f));

            float width = tabNoiseBandWidth[i];
            float center = Mathf.Lerp(
                width * 0.5f,
                1f - width * 0.5f,
                Hash01(seed * 4.19f));

            band.anchorMin = new Vector2(center - width * 0.5f, 0.5f);
            band.anchorMax = new Vector2(center + width * 0.5f, 0.5f);
            band.pivot = new Vector2(0.5f, 0.5f);
            band.anchoredPosition = new Vector2(0f, tabNoiseBandBaseY[i] * 1080f);
            band.sizeDelta = new Vector2(0f, 1f);

            Image image = band.gameObject.AddComponent<Image>();
            Color baseColor = i % 5 == 0
                ? accentPink
                : i % 3 == 0
                    ? paperColor
                    : accentCyan;
            image.color = new Color(
                baseColor.r,
                baseColor.g,
                baseColor.b,
                0f);
            image.raycastTarget = false;

            tabNoiseBands[i] = band;
            tabNoiseBandImages[i] = image;
        }
    }

    private static float Hash01(float value)
    {
        return Mathf.Repeat(
            Mathf.Sin(value * 12.9898f + 78.233f) * 43758.5453f,
            1f);
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

        fullDimImage = fullRoot.gameObject.AddComponent<Image>();
        fullDimImage.color = new Color(0.012f, 0.013f, 0.016f, 0.58f);
        fullDimImage.raycastTarget = false;

        BuildTabHoldSignal(fullRoot);

        Text sub = CreateText(
            fullRoot,
            "HOLD TAB / LB   •   TIME STOPS   •   SELECT SLOT   •   RELEASE TO EQUIP",
            12,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            new Color(0.66f, 0.70f, 0.78f, 0.88f));
        RectTransform subRect = sub.rectTransform;
        subRect.anchorMin = subRect.anchorMax = new Vector2(0f, 1f);
        subRect.pivot = new Vector2(0f, 1f);
        subRect.sizeDelta = new Vector2(860f, 36f);
        subRect.anchoredPosition = new Vector2(74f, -62f);
        subRect.localRotation = Quaternion.identity;

        detailRoot = CreateRect(fullRoot, "DetailPanel", itemTooltipSize);
        detailGroup = detailRoot.gameObject.AddComponent<CanvasGroup>();
        detailGroup.alpha = 0f;
        detailGroup.blocksRaycasts = false;
        detailGroup.interactable = false;
        detailRoot.anchorMin = detailRoot.anchorMax = new Vector2(0.5f, 0.5f);
        detailRoot.pivot = new Vector2(0f, 0.5f);
        detailRoot.anchoredPosition = Vector2.zero;
        detailRoot.localRotation = Quaternion.identity;

        Image detailBack = detailRoot.gameObject.AddComponent<Image>();
        detailBack.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.965f);
        detailBack.raycastTarget = false;

        Outline detailOutline = detailRoot.gameObject.AddComponent<Outline>();
        detailOutline.effectColor = new Color(paperColor.r, paperColor.g, paperColor.b, 0.24f);
        detailOutline.effectDistance = new Vector2(2f, -2f);

        detailTitle = CreateText(detailRoot, "EMPTY", 20, FontStyle.Bold, TextAnchor.UpperLeft, paperColor);
        SetAnchors(detailTitle.rectTransform, new Vector2(0.06f, 0.74f), new Vector2(0.94f, 0.94f));

        detailDescription = CreateText(detailRoot, "NO DESCRIPTION", 12, FontStyle.Normal, TextAnchor.UpperLeft, paperColor);
        SetAnchors(detailDescription.rectTransform, new Vector2(0.06f, 0.43f), new Vector2(0.94f, 0.73f));
        detailDescription.horizontalOverflow = HorizontalWrapMode.Wrap;
        detailDescription.verticalOverflow = VerticalWrapMode.Truncate;

        detailTags = CreateText(detailRoot, "—", 10, FontStyle.Bold, TextAnchor.UpperLeft, accentCyan);
        SetAnchors(detailTags.rectTransform, new Vector2(0.06f, 0.25f), new Vector2(0.94f, 0.42f));

        synergySummary = CreateText(detailRoot, "GRID LINK 0", 10, FontStyle.Bold, TextAnchor.LowerLeft, accentCyan);
        SetAnchors(synergySummary.rectTransform, new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.23f));

        compareRoot = CreateRect(detailRoot, "CompareRoot", Vector2.zero);
        Stretch(compareRoot);
        compareRoot.gameObject.SetActive(false);

        RectTransform sourceCard = CreateRect(compareRoot, "SourceCard", Vector2.zero);
        SetAnchors(sourceCard, new Vector2(0.035f, 0.16f), new Vector2(0.43f, 0.94f));
        Image sourceBack = sourceCard.gameObject.AddComponent<Image>();
        sourceBack.color = new Color(0.035f, 0.040f, 0.050f, 0.98f);
        sourceBack.raycastTarget = false;
        Outline sourceOutline = sourceCard.gameObject.AddComponent<Outline>();
        sourceOutline.effectColor = accentYellow;
        sourceOutline.effectDistance = new Vector2(3f, -3f);

        compareSourceIcon = CreateImage(sourceCard, "SourceIcon", new Vector2(72f, 72f));
        compareSourceIcon.rectTransform.anchorMin = compareSourceIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.82f);
        compareSourceIcon.rectTransform.anchoredPosition = Vector2.zero;
        compareSourceIcon.preserveAspect = true;
        compareSourceIcon.raycastTarget = false;

        compareSourceName = CreateText(sourceCard, "SOURCE", 13, FontStyle.Bold, TextAnchor.UpperCenter, paperColor);
        SetAnchors(compareSourceName.rectTransform, new Vector2(0.06f, 0.47f), new Vector2(0.94f, 0.68f));

        compareSourceMeta = CreateText(sourceCard, string.Empty, 9, FontStyle.Bold, TextAnchor.UpperCenter, new Color(0.72f, 0.76f, 0.82f, 1f));
        SetAnchors(compareSourceMeta.rectTransform, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.46f));

        RectTransform targetCard = CreateRect(compareRoot, "TargetCard", Vector2.zero);
        SetAnchors(targetCard, new Vector2(0.57f, 0.16f), new Vector2(0.965f, 0.94f));
        Image targetBack = targetCard.gameObject.AddComponent<Image>();
        targetBack.color = new Color(0.035f, 0.040f, 0.050f, 0.98f);
        targetBack.raycastTarget = false;
        Outline targetOutline = targetCard.gameObject.AddComponent<Outline>();
        targetOutline.effectColor = accentCyan;
        targetOutline.effectDistance = new Vector2(3f, -3f);

        compareTargetIcon = CreateImage(targetCard, "TargetIcon", new Vector2(72f, 72f));
        compareTargetIcon.rectTransform.anchorMin = compareTargetIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.82f);
        compareTargetIcon.rectTransform.anchoredPosition = Vector2.zero;
        compareTargetIcon.preserveAspect = true;
        compareTargetIcon.raycastTarget = false;

        compareTargetName = CreateText(targetCard, "TARGET", 13, FontStyle.Bold, TextAnchor.UpperCenter, paperColor);
        SetAnchors(compareTargetName.rectTransform, new Vector2(0.06f, 0.47f), new Vector2(0.94f, 0.68f));

        compareTargetMeta = CreateText(targetCard, string.Empty, 9, FontStyle.Bold, TextAnchor.UpperCenter, new Color(0.72f, 0.76f, 0.82f, 1f));
        SetAnchors(compareTargetMeta.rectTransform, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.46f));

        compareSwapLabel = CreateText(compareRoot, "<->\nSWAP", 15, FontStyle.Bold, TextAnchor.MiddleCenter, accentYellow);
        SetAnchors(compareSwapLabel.rectTransform, new Vector2(0.43f, 0.38f), new Vector2(0.57f, 0.72f));

        compareDeltaText = CreateText(compareRoot, string.Empty, 9, FontStyle.Bold, TextAnchor.MiddleCenter, accentCyan);
        SetAnchors(compareDeltaText.rectTransform, new Vector2(0.08f, 0.01f), new Vector2(0.92f, 0.15f));

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

        // Tooltip is visual-only and must render above the board without
        // participating in pointer hit testing.
        detailRoot.SetAsLastSibling();
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
        RefreshDetailForSlot(selectedIndex);
    }

    private void RefreshDetailForSlot(int slotIndex)
    {
        if (equipmentSystem == null || slotIndex < 0 || slotIndex >= equipmentSystem.Slots.Count)
            return;

        BattleEquipmentSlot slot = equipmentSystem.Slots[slotIndex];
        BattleEquipmentSO equipment = slot?.equipment;
        if (detailTitle != null)
            detailTitle.text = equipment != null ? equipment.GetDisplayName().ToUpperInvariant() : "EMPTY SLOT";

        if (detailDescription != null)
        {
            detailDescription.text =
                equipment != null && !string.IsNullOrWhiteSpace(equipment.description)
                    ? equipment.description
                    : "NO DESCRIPTION";
        }

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
                    if (link.slotA != slotIndex && link.slotB != slotIndex)
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

    public void ShowRewardInspectTooltip(int slotIndex)
    {
        ResolveReferences();
        EnsureUi();

        if (equipmentSystem == null ||
            slotIndex < 0 ||
            slotIndex >= equipmentSystem.Slots.Count ||
            !equipmentSystem.IsSlotUnlocked(slotIndex) ||
            equipmentSystem.Slots[slotIndex]?.equipment == null ||
            detailRoot == null ||
            detailGroup == null ||
            slotRects[slotIndex] == null)
        {
            HideRewardInspectTooltip();
            return;
        }

        SetDetailCompareMode(false);
        RefreshDetailForSlot(slotIndex);

        detailRoot.sizeDelta = itemTooltipSize;
        detailRoot.gameObject.SetActive(true);
        detailRoot.SetAsLastSibling();
        detailRoot.anchoredPosition = ResolveTooltipPosition(slotRects[slotIndex]);
        detailRoot.localScale = Vector3.one;
        detailRoot.localRotation = Quaternion.identity;

        Vector3 local = detailRoot.localPosition;
        local.z = -24f;
        detailRoot.localPosition = local;

        detailGroup.alpha = 1f;
        detailGroup.blocksRaycasts = false;
        detailGroup.interactable = false;
    }

    public void HideRewardInspectTooltip()
    {
        if (detailGroup == null)
            return;

        detailGroup.alpha = 0f;
        detailGroup.blocksRaycasts = false;
        detailGroup.interactable = false;
    }

    public void ShowRewardCompareTooltip(int sourceSlotIndex, int targetSlotIndex)
    {
        ResolveReferences();
        EnsureUi();

        if (equipmentSystem == null ||
            sourceSlotIndex < 0 ||
            targetSlotIndex < 0 ||
            sourceSlotIndex >= equipmentSystem.Slots.Count ||
            targetSlotIndex >= equipmentSystem.Slots.Count ||
            sourceSlotIndex == targetSlotIndex ||
            !equipmentSystem.IsSlotUnlocked(sourceSlotIndex) ||
            !equipmentSystem.IsSlotUnlocked(targetSlotIndex) ||
            detailRoot == null ||
            detailGroup == null ||
            slotRects[targetSlotIndex] == null)
        {
            HideRewardInspectTooltip();
            return;
        }

        BattleEquipmentSlot sourceSlot = equipmentSystem.Slots[sourceSlotIndex];
        BattleEquipmentSlot targetSlot = equipmentSystem.Slots[targetSlotIndex];
        BattleEquipmentSO source = sourceSlot?.equipment;
        BattleEquipmentSO target = targetSlot?.equipment;

        if (source == null || target == null)
        {
            HideRewardInspectTooltip();
            return;
        }

        SetDetailCompareMode(true);

        detailRoot.sizeDelta = new Vector2(620f, 300f);

        if (compareSourceIcon != null)
        {
            compareSourceIcon.sprite = source.icon;
            compareSourceIcon.enabled = source.icon != null;
        }
        if (compareTargetIcon != null)
        {
            compareTargetIcon.sprite = target.icon;
            compareTargetIcon.enabled = target.icon != null;
        }

        if (compareSourceName != null)
            compareSourceName.text = source.GetDisplayName().ToUpperInvariant();
        if (compareTargetName != null)
            compareTargetName.text = target.GetDisplayName().ToUpperInvariant();

        if (compareSourceMeta != null)
            compareSourceMeta.text = BuildCompareItemMeta(sourceSlot, source, sourceSlotIndex, "FROM");
        if (compareTargetMeta != null)
            compareTargetMeta.text = BuildCompareItemMeta(targetSlot, target, targetSlotIndex, "TO");

        float damageDelta = (target.damageMultiplier - source.damageMultiplier) * 100f;
        float moveDelta = (target.moveSpeedMultiplier - source.moveSpeedMultiplier) * 100f;
        float rangeDelta = (target.rangeMultiplier - source.rangeMultiplier) * 100f;

        if (compareDeltaText != null)
        {
            compareDeltaText.text =
                $"IF SWAPPED  //  DMG {FormatCompareDelta(damageDelta)}   " +
                $"MOVE {FormatCompareDelta(moveDelta)}   " +
                $"RANGE {FormatCompareDelta(rangeDelta)}";
            compareDeltaText.color =
                damageDelta + moveDelta + rangeDelta >= 0f
                    ? accentCyan
                    : accentYellow;
        }

        if (compareSwapLabel != null)
            compareSwapLabel.text = "<->\nSWAP";

        detailRoot.gameObject.SetActive(true);
        detailRoot.SetAsLastSibling();
        detailRoot.anchoredPosition = ResolveTooltipPosition(slotRects[targetSlotIndex]);
        detailRoot.localScale = Vector3.one;
        detailRoot.localRotation = Quaternion.identity;

        Vector3 local = detailRoot.localPosition;
        local.z = -28f;
        detailRoot.localPosition = local;

        detailGroup.alpha = 1f;
        detailGroup.blocksRaycasts = false;
        detailGroup.interactable = false;
    }

    public int ResolveOccupiedSlotUnderPointer(Vector2 screenPoint)
    {
        ResolveReferences();

        if (equipmentSystem == null)
            return -1;

        for (int i = 0; i < slotRects.Length; i++)
        {
            if (slotRects[i] == null ||
                !equipmentSystem.IsSlotUnlocked(i) ||
                i >= equipmentSystem.Slots.Count ||
                equipmentSystem.Slots[i]?.equipment == null)
            {
                continue;
            }

            if (GetScreenRect(slotRects[i]).Contains(screenPoint))
                return i;
        }

        return -1;
    }

    private void SetDetailCompareMode(bool compare)
    {
        if (compareRoot != null)
            compareRoot.gameObject.SetActive(compare);

        if (detailTitle != null)
            detailTitle.gameObject.SetActive(!compare);
        if (detailDescription != null)
            detailDescription.gameObject.SetActive(!compare);
        if (detailTags != null)
            detailTags.gameObject.SetActive(!compare);
        if (synergySummary != null)
            synergySummary.gameObject.SetActive(!compare);
    }

    private static string BuildCompareItemMeta(
        BattleEquipmentSlot slot,
        BattleEquipmentSO equipment,
        int slotIndex,
        string direction)
    {
        Vector2Int grid = BattleEquipmentSystem.SlotIndexToGrid(slotIndex);
        string type = equipment.type.ToString().ToUpperInvariant();
        string rarity = equipment.rarity.ToString().ToUpperInvariant();

        return
            $"{direction}  SLOT {grid.x + 1}-{grid.y + 1}\n" +
            $"GRADE {slot.grade}  //  {rarity} / {type}\n\n" +
            $"DMG   x{equipment.damageMultiplier:0.00}\n" +
            $"MOVE  x{equipment.moveSpeedMultiplier:0.00}\n" +
            $"RANGE x{equipment.rangeMultiplier:0.00}";
    }

    private static string FormatCompareDelta(float value)
    {
        if (Mathf.Abs(value) < 0.05f)
            return "±0%";

        return value > 0f
            ? $"+{value:0.#}%"
            : $"{value:0.#}%";
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
        CaptureHoveredSlotEntryRect(index);
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
        hoveredSlotEntryRectValid = false;
    }

    private void UpdateHoverExitGrace()
    {
        if (pendingHoverExitSlot < 0 || Time.unscaledTime < hoverExitAt)
            return;

        if (hoveredSlot == pendingHoverExitSlot &&
            Input.mousePresent &&
            IsPointerInsideHoveredSlotLatch(
                pendingHoverExitSlot,
                Input.mousePosition))
        {
            pendingHoverExitSlot = -1;
            hoverExitAt = 0f;
            return;
        }

        if (hoveredSlot == pendingHoverExitSlot)
        {
            hoveredSlot = -1;
            hoveredSlotEntryRectValid = false;
        }

        pendingHoverExitSlot = -1;
        hoverExitAt = 0f;
    }

    private void CaptureHoveredSlotEntryRect(int index)
    {
        if (index < 0 || index >= slotRects.Length || slotRects[index] == null)
        {
            hoveredSlotEntryRectValid = false;
            return;
        }

        hoveredSlotEntryScreenRect = GetScreenRect(slotRects[index]);
        hoveredSlotEntryRectValid = true;
    }

    private bool IsPointerInsideHoveredSlotLatch(int index, Vector2 pointer)
    {
        bool insideEntry =
            hoveredSlotEntryRectValid &&
            hoveredSlotEntryScreenRect.Contains(pointer);

        if (index < 0 || index >= slotRects.Length || slotRects[index] == null)
            return insideEntry;

        return insideEntry || GetScreenRect(slotRects[index]).Contains(pointer);
    }

    private static Rect GetScreenRect(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera camera =
            canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        Vector2 max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
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
        {
            hpText.text = $"HP {player.CurrentHp:0}/{hpMax:0}";
            ApplyLowHpNumericFeedback(hp01);
        }

        if (staminaText != null)
            staminaText.text = $"ST {player.CurrentStamina:0}/{staminaMax:0}";
    }

    private void ApplyLowHpNumericFeedback(float hp01)
    {
        if (hpText == null)
            return;

        RectTransform rect = hpText.rectTransform;
        if (!hpTextRestCaptured)
        {
            hpTextRestPosition = rect.anchoredPosition;
            hpTextRestCaptured = true;
        }

        float amplitude;
        if (hp01 > 0.35f)
            amplitude = 0f;
        else if (hp01 > 0.20f)
            amplitude = 0.8f;
        else if (hp01 > 0.10f)
            amplitude = 1.7f;
        else
            amplitude = 2.8f;

        if (amplitude <= 0.001f)
        {
            rect.anchoredPosition = hpTextRestPosition;
            rect.localScale = Vector3.one;
            return;
        }

        float time = Time.unscaledTime;
        float jitterX =
            Mathf.Sin(time * 31f) * amplitude +
            Mathf.Sin(time * 53f + 1.3f) * amplitude * 0.34f;
        float jitterY =
            Mathf.Sin(time * 43f + 0.7f) * amplitude * 0.42f;

        rect.anchoredPosition =
            hpTextRestPosition +
            new Vector2(jitterX, jitterY);

        float pulse = hp01 <= 0.10f
            ? 1f + 0.055f * (0.5f + 0.5f * Mathf.Sin(time * 9f))
            : 1f;
        rect.localScale = Vector3.one * pulse;
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

        UpdateTabHoldVisuals(t, wantFull);

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
            // Reward PACK editing temporarily re-anchors this shared GridBoard.
            // Combat must restore its own geometry every time the TAB board is active,
            // otherwise the next stage inherits Reward's center anchor and shifts right.
            if (combat || morphVisible)
            {
                boardRoot.anchorMin = boardRoot.anchorMax = new Vector2(0.31f, 0.53f);
                boardRoot.pivot = new Vector2(0.5f, 0.5f);
            }

            Vector2 boardTarget = packFocused
                ? packFocusedOffset
                : packSuppressed
                    ? packInactiveCornerOffset
                    : packRestOffset;

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
            int tooltipSlot = ResolveTooltipSlot();
            bool tooltipVisible =
                wantFull &&
                packFocused &&
                tooltipSlot >= 0 &&
                tooltipSlot < slotRects.Length &&
                slotRects[tooltipSlot] != null;

            Vector2 tooltipTarget = tooltipVisible
                ? ResolveTooltipPosition(slotRects[tooltipSlot])
                : detailRoot.anchoredPosition;

            if (tooltipVisible)
            {
                detailRoot.anchoredPosition = Vector2.Lerp(
                    detailRoot.anchoredPosition,
                    tooltipTarget,
                    t);
            }

            detailRoot.localScale = Vector3.Lerp(
                detailRoot.localScale,
                Vector3.one * (tooltipVisible ? 1f : 0.94f),
                t);

            Vector3 detailLocal = detailRoot.localPosition;
            detailLocal.z = Mathf.Lerp(
                detailLocal.z,
                tooltipVisible ? -24f : 12f,
                t);
            detailRoot.localPosition = detailLocal;

            if (detailGroup != null)
            {
                float tooltipReveal = tooltipVisible
                    ? SmoothPackRange(packMorphProgress, 0.70f, 0.94f)
                    : 0f;
                detailGroup.alpha = Mathf.Lerp(
                    detailGroup.alpha,
                    tooltipReveal,
                    t);
                detailGroup.blocksRaycasts = false;
                detailGroup.interactable = false;
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

    private int ResolveTooltipSlot()
    {
        if (hoveredSlot >= 0)
            return hoveredSlot;

        return !Input.mousePresent ? selectedIndex : -1;
    }

    private Vector2 ResolveTooltipPosition(RectTransform slot)
    {
        if (slot == null || fullRoot == null || detailRoot == null)
            return Vector2.zero;

        Vector3 slotRightWorld = slot.TransformPoint(
            new Vector3(slot.rect.xMax, slot.rect.center.y, 0f));
        Vector3 slotCenterWorld = slot.TransformPoint(slot.rect.center);

        Vector2 rightLocal = fullRoot.InverseTransformPoint(slotRightWorld);
        Vector2 centerLocal = fullRoot.InverseTransformPoint(slotCenterWorld);

        float gap = Mathf.Max(4f, itemTooltipGap);
        Vector2 desired = new(
            rightLocal.x + gap,
            centerLocal.y);

        detailRoot.pivot = new Vector2(0f, 0.5f);

        // Detail/compare windows always open to the RIGHT of the hovered slot.
        // We only correct against the actual Game View safe area; there is no
        // left-side fallback anymore.
        Vector2 clamped = ClampRightSideTooltipToViewport(
            fullRoot,
            desired,
            detailRoot.sizeDelta,
            detailRoot.pivot,
            24f);

        // Never allow viewport correction to flip the tooltip to the slot's left.
        clamped.x = Mathf.Max(clamped.x, rightLocal.x + 4f);
        return clamped;
    }

    private static Vector2 ClampRightSideTooltipToViewport(
        RectTransform parent,
        Vector2 pivotLocal,
        Vector2 size,
        Vector2 pivot,
        float margin)
    {
        if (parent == null)
            return pivotLocal;

        Canvas canvas = parent.GetComponentInParent<Canvas>();
        Camera eventCamera =
            canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

        float width = Mathf.Max(1f, size.x);
        float height = Mathf.Max(1f, size.y);

        Vector2[] localCorners =
        {
            pivotLocal + new Vector2(-width * pivot.x, -height * pivot.y),
            pivotLocal + new Vector2(width * (1f - pivot.x), -height * pivot.y),
            pivotLocal + new Vector2(width * (1f - pivot.x), height * (1f - pivot.y)),
            pivotLocal + new Vector2(-width * pivot.x, height * (1f - pivot.y))
        };

        Vector2 screenMin = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 screenMax = new(float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < localCorners.Length; i++)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(
                eventCamera,
                parent.TransformPoint(localCorners[i]));
            screenMin = Vector2.Min(screenMin, screen);
            screenMax = Vector2.Max(screenMax, screen);
        }

        Rect viewport = eventCamera != null
            ? eventCamera.pixelRect
            : new Rect(0f, 0f, Screen.width, Screen.height);

        float safeMargin = Mathf.Max(0f, margin);
        Rect safe = new(
            viewport.xMin + safeMargin,
            viewport.yMin + safeMargin,
            Mathf.Max(1f, viewport.width - safeMargin * 2f),
            Mathf.Max(1f, viewport.height - safeMargin * 2f));

        Vector2 correction = Vector2.zero;
        if (screenMax.x > safe.xMax)
            correction.x -= screenMax.x - safe.xMax;
        if (screenMin.y < safe.yMin)
            correction.y += safe.yMin - screenMin.y;
        if (screenMax.y > safe.yMax)
            correction.y -= screenMax.y - safe.yMax;

        if (correction.sqrMagnitude <= 0.0001f)
            return pivotLocal;

        Vector2 pivotScreen = RectTransformUtility.WorldToScreenPoint(
            eventCamera,
            parent.TransformPoint(pivotLocal));

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent,
                pivotScreen + correction,
                eventCamera,
                out Vector2 correctedLocal))
        {
            return pivotLocal;
        }

        return correctedLocal;
    }

    private void UpdateTabHoldVisuals(float t, bool wantFull)
    {
        float amount = Mathf.Clamp01(tabHoldVisual);
        float visual = EaseOutCubic(amount);
        float signalTime = Time.unscaledTime * Mathf.Max(8f, tabSignalFrequency);
        float pulse = Mathf.Lerp(
            Mathf.PerlinNoise(11.73f, signalTime * 0.021f),
            Mathf.PerlinNoise(47.19f, signalTime * 0.047f),
            0.42f);

        if (tabHoldSignalGroup != null)
        {
            float targetAlpha = wantFull || tabTimeState == TabTimeState.Resuming
                ? visual
                : 0f;
            tabHoldSignalGroup.alpha = Mathf.Lerp(
                tabHoldSignalGroup.alpha,
                targetAlpha,
                t);
        }

        if (fullDimImage != null)
        {
            Color dim = fullDimImage.color;
            dim.a = Mathf.Lerp(
                dim.a,
                0.58f + Mathf.Max(0f, tabExtraDimAlpha) * visual,
                t);
            fullDimImage.color = dim;
        }

        if (tabHoldGlyphRoot != null)
        {
            float scale = Mathf.Lerp(
                0.86f,
                1f + pulse * 0.008f,
                visual);
            tabHoldGlyphRoot.localScale = Vector3.Lerp(
                tabHoldGlyphRoot.localScale,
                Vector3.one * scale,
                t);
            tabHoldGlyphRoot.localRotation = Quaternion.Slerp(
                tabHoldGlyphRoot.localRotation,
                Quaternion.Euler(0f, 0f, Mathf.Lerp(-4f, 0f, visual)),
                t);
        }

        if (tabHoldBarLeft != null && tabHoldBarRight != null)
        {
            float spacing = Mathf.Lerp(8f, 13f, visual);
            tabHoldBarLeft.anchoredPosition = Vector2.Lerp(
                tabHoldBarLeft.anchoredPosition,
                new Vector2(-spacing, 0f),
                t);
            tabHoldBarRight.anchoredPosition = Vector2.Lerp(
                tabHoldBarRight.anchoredPosition,
                new Vector2(spacing, 0f),
                t);
        }

        UpdateTabNoiseBands(t, visual);

        if (tabTimeFlowText != null)
        {
            float shownScale = timeScaleController != null
                ? timeScaleController.AppliedScale
                : Time.timeScale;

            string phase = tabTimeState switch
            {
                TabTimeState.Stopping => "BRAKING",
                TabTimeState.Stopped => "HOLD",
                TabTimeState.Resuming => "RECOVERING",
                _ => "RUNNING"
            };

            tabTimeFlowText.text =
                $"{phase}  //  TIME FLOW {shownScale:0.00}x";
        }
    }

    private void UpdateTabNoiseBands(float t, float visual)
    {
        if (tabNoiseBands == null || tabNoiseBandImages == null)
            return;

        float now = Time.unscaledTime;
        float height = fullRoot != null
            ? Mathf.Max(720f, fullRoot.rect.height)
            : 1080f;

        for (int i = 0; i < TabNoiseBandCount; i++)
        {
            RectTransform band = tabNoiseBands[i];
            Image image = tabNoiseBandImages[i];
            if (band == null || image == null)
                continue;

            float seed = tabNoiseBandSeeds[i];
            float speed = Mathf.Max(1f, tabNoiseBandSpeed[i]);

            float smoothA = Mathf.PerlinNoise(
                seed,
                now * speed * 0.057f);
            float smoothB = Mathf.PerlinNoise(
                seed * 1.913f,
                now * speed * 0.113f);

            float stepIndex = Mathf.Floor(now * speed * 0.72f);
            float stepped = Hash01(seed + stepIndex * 5.731f);
            float steppedB = Hash01(seed * 2.17f + stepIndex * 9.103f);

            // Most frames are quiet. A few bands flare for a frame or two,
            // avoiding the obvious "all scanlines moving together" look.
            float burst = Mathf.Clamp01((stepped - 0.62f) / 0.38f);
            burst *= burst;

            float baseY = tabNoiseBandBaseY[i] * height;
            float drift = (smoothA - 0.5f) * Mathf.Lerp(5f, 24f, smoothB);
            float jump = burst * (steppedB - 0.5f) * Mathf.Lerp(18f, 92f, stepped);
            float xJitter = burst * (Hash01(seed + stepIndex * 3.19f) - 0.5f) * 48f;

            band.anchoredPosition = Vector2.Lerp(
                band.anchoredPosition,
                new Vector2(xJitter, baseY + drift + jump),
                Mathf.Clamp01(t * Mathf.Lerp(0.55f, 1.65f, burst)));

            Vector2 size = band.sizeDelta;
            float thickness =
                Mathf.Lerp(0.6f, 1.5f, smoothB) +
                burst * Mathf.Lerp(1.2f, 4.5f, steppedB);
            size.y = Mathf.Lerp(size.y, thickness, t);
            band.sizeDelta = size;

            Color color = image.color;

            float quietAlpha = Mathf.Lerp(
                0.010f,
                0.055f,
                smoothA * smoothB);
            float burstAlpha = burst * Mathf.Lerp(
                0.08f,
                0.32f,
                steppedB);
            float dropout = Hash01(seed * 4.31f + stepIndex * 1.73f) < 0.17f
                ? 0.12f
                : 1f;

            color.a = visual * (quietAlpha + burstAlpha) * dropout;
            image.color = Color.Lerp(image.color, color, t);
        }
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    private static float EaseInOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
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

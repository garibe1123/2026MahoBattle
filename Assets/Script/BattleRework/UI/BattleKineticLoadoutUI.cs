using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Combat 장비 전환 입력과 공용 3x3 Loadout View의 생성/내용 갱신을 담당합니다.
///
/// 이 클래스의 책임:
/// - Tab / LB 전환 입력
/// - Combat Inventory slow-motion 요청
/// - 공용 LoadoutSwitchFull / GridBoard 생성
/// - 슬롯 아이콘/이름/등급/시너지 내용 갱신
///
/// Time.timeScale은 직접 수정하지 않고 BattleTimeScaleController에 요청합니다.
/// GridBoard / Mini PACK / Detail / Reward Controls의 최종 RectTransform 배치는
/// BattleUnifiedInventoryInspectController가 단독 소유합니다.
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

    [Header("Switch Input")]
    [SerializeField, Min(0.05f)] private float holdThreshold = 0.14f;
    [SerializeField, Range(0.02f, 0.20f)] private float combatInventoryTimeScale = 0.05f;
    [SerializeField, Min(0.05f)] private float stickRepeatDelay = 0.16f;
    [SerializeField, Range(0.2f, 0.95f)] private float stickThreshold = 0.55f;

    [Header("Kinetic UI")]
    [SerializeField] private Color inkColor = new(0.035f, 0.030f, 0.055f, 0.98f);
    [SerializeField] private Color paperColor = new(0.94f, 0.90f, 0.76f, 1f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentCyan = new(0.15f, 0.88f, 0.92f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField] private Color lockedColor = new(0.12f, 0.11f, 0.16f, 0.92f);
    [SerializeField, Min(1f)] private float uiSharpness = 16f;

    private Canvas canvas;
    private CanvasGroup fullGroup;
    private RectTransform fullRoot;
    private RectTransform boardRoot;
    private RectTransform linkRoot;
    private RectTransform detailRoot;
    private Text detailTitle;
    private Text detailTags;
    private Text synergySummary;

    private CanvasGroup compactGroup;
    private RectTransform compactRoot;
    private Image compactIcon;
    private Text compactName;
    private Text compactPrompt;

    private readonly RectTransform[] slotRects = new RectTransform[SlotCount];
    private readonly Image[] slotBackgrounds = new Image[SlotCount];
    private readonly Image[] slotIcons = new Image[SlotCount];
    private readonly Text[] slotNames = new Text[SlotCount];
    private readonly Text[] slotGrades = new Text[SlotCount];
    private readonly Text[] slotStates = new Text[SlotCount];
    private readonly List<GameObject> linkVisuals = new();

    private RectTransform legacyEquipmentDock;

    private bool switchHeld;
    private bool boardWasShown;
    private bool directionMoved;
    private float switchPressedAt;
    private int selectedIndex = -1;
    private float nextStickRepeat;
    private Vector2 lastStickDirection;
    private bool subscribed;

    public int SelectedIndex => selectedIndex;
    public bool SwitchHeld => switchHeld;
    public bool BoardWasShown => boardWasShown;
    public bool IsSwitchBoardOpen => IsCombat() && switchHeld && boardWasShown;
    public RectTransform FullRoot => fullRoot;
    public CanvasGroup FullGroup => fullGroup;
    public RectTransform GridBoard => boardRoot;
    public RectTransform CompactRoot => compactRoot;
    public CanvasGroup CompactGroup => compactGroup;

    private void Awake()
    {
        ResolveReferences();
        EnsureUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureUi();
        Subscribe();
        if (switchHeld)
            EnterBulletTime();
        RefreshAll();
    }

    private void OnDisable()
    {
        Unsubscribe();
        RestoreTimeScale();
    }

    private void OnDestroy()
    {
        RestoreTimeScale();
    }

    private void Update()
    {
        ResolveReferences();
        Subscribe();
        EnsureUi();
        ResolveLegacyCombatHud();

        bool combat = IsCombat();
        if (!combat && switchHeld)
            CancelSwitchMode();

        if (legacyEquipmentDock != null && combat && legacyEquipmentDock.gameObject.activeSelf)
            legacyEquipmentDock.gameObject.SetActive(false);

        if (combat && !BattlePauseController.IsPaused)
            UpdateSwitchInput();

        UpdateUiAnimation(combat);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (gridSynergy == null)
            gridSynergy = FindFirstObjectByType<BattleGridSynergyController>();
        if (timeScaleController == null)
            timeScaleController = BattleTimeScaleController.ResolveOrCreate(this);
    }

    private void Subscribe()
    {
        if (subscribed || equipmentSystem == null)
            return;

        equipmentSystem.InventoryChanged += RefreshAll;
        equipmentSystem.SlotCapacityChanged += HandleCapacityChanged;
        equipmentSystem.EquippedSlotChanged += HandleEquippedChanged;
        if (gridSynergy != null)
            gridSynergy.GridSynergiesChanged += RefreshAll;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (equipmentSystem != null)
        {
            equipmentSystem.InventoryChanged -= RefreshAll;
            equipmentSystem.SlotCapacityChanged -= HandleCapacityChanged;
            equipmentSystem.EquippedSlotChanged -= HandleEquippedChanged;
        }
        if (gridSynergy != null)
            gridSynergy.GridSynergiesChanged -= RefreshAll;
        subscribed = false;
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

    private bool IsCombat()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Combat;
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
        bool pressed = Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.JoystickButton4);
        bool held = Input.GetKey(KeyCode.Tab) || Input.GetKey(KeyCode.JoystickButton4);
        bool released = Input.GetKeyUp(KeyCode.Tab) || Input.GetKeyUp(KeyCode.JoystickButton4);

        if (pressed && !switchHeld)
            BeginSwitchMode();

        if (!switchHeld)
            return;

        float heldDuration = Time.unscaledTime - switchPressedAt;
        if (!boardWasShown && heldDuration >= Mathf.Max(0.05f, holdThreshold))
        {
            boardWasShown = true;
            RefreshAll();
        }

        if (boardWasShown && held)
            UpdateGridNavigation();

        if (released || !held)
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
        nextStickRepeat = 0f;
        lastStickDirection = Vector2.zero;

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
        RestoreTimeScale();
        RefreshAll();
    }

    private void CancelSwitchMode()
    {
        switchHeld = false;
        boardWasShown = false;
        directionMoved = false;
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
        int dx = 0;
        int dy = 0;

        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) dx = -1;
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) dx = 1;
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dy = -1;
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dy = 1;

        if (dx != 0 || dy != 0)
        {
            ResetStickNavigation();
            MoveSelection(dx, dy);
            return;
        }

        if (IsKeyboardNavigationHeld())
        {
            ResetStickNavigation();
            return;
        }

        Vector2 stick = new(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        bool stickActive = Mathf.Abs(stick.x) >= stickThreshold || Mathf.Abs(stick.y) >= stickThreshold;
        if (!stickActive)
        {
            ResetStickNavigation();
            return;
        }

        if (Time.unscaledTime < nextStickRepeat)
            return;

        if (Mathf.Abs(stick.x) >= Mathf.Abs(stick.y))
            dx = stick.x >= 0f ? 1 : -1;
        else
            dy = stick.y >= 0f ? -1 : 1;

        bool newDirection = lastStickDirection.sqrMagnitude <= 0.001f ||
                            Vector2.Dot(stick.normalized, lastStickDirection) < 0.8f;
        nextStickRepeat = Time.unscaledTime + (newDirection ? 0.06f : Mathf.Max(0.05f, stickRepeatDelay));
        lastStickDirection = stick.normalized;
        MoveSelection(dx, dy);
    }

    private static bool IsKeyboardNavigationHeld()
    {
        return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) ||
               Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S) ||
               Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow) ||
               Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow);
    }

    private void ResetStickNavigation()
    {
        lastStickDirection = Vector2.zero;
        nextStickRepeat = 0f;
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
        compactRoot = CreateRect(parent, "CurrentLoadoutChip", new Vector2(430f, 102f));
        compactRoot.anchorMin = compactRoot.anchorMax = new Vector2(1f, 0f);
        compactRoot.pivot = new Vector2(1f, 0f);
        compactRoot.anchoredPosition = new Vector2(-28f, 24f);
        compactRoot.localRotation = Quaternion.Euler(0f, 0f, -2.5f);

        Image back = compactRoot.gameObject.AddComponent<Image>();
        back.color = inkColor;
        back.raycastTarget = false;
        Outline outline = compactRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = accentYellow;
        outline.effectDistance = new Vector2(4f, -4f);

        compactGroup = compactRoot.gameObject.AddComponent<CanvasGroup>();
        compactGroup.blocksRaycasts = false;
        compactGroup.interactable = false;

        RectTransform bar = CreateRect(compactRoot, "AccentSlash", new Vector2(22f, 122f));
        bar.anchorMin = bar.anchorMax = new Vector2(0f, 0.5f);
        bar.anchoredPosition = new Vector2(10f, 0f);
        bar.localRotation = Quaternion.Euler(0f, 0f, 13f);
        Image barImage = bar.gameObject.AddComponent<Image>();
        barImage.color = accentPink;
        barImage.raycastTarget = false;

        compactIcon = CreateImage(compactRoot, "Icon", new Vector2(72f, 72f));
        compactIcon.rectTransform.anchorMin = compactIcon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        compactIcon.rectTransform.anchoredPosition = new Vector2(76f, 0f);
        compactIcon.preserveAspect = true;
        compactIcon.raycastTarget = false;

        compactName = CreateText(compactRoot, "NO WEAPON", 20, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
        SetAnchors(compactName.rectTransform, new Vector2(0.30f, 0.42f), new Vector2(0.96f, 0.90f));

        compactPrompt = CreateText(compactRoot, "TAB / LB  —  SWITCH", 11, FontStyle.Bold, TextAnchor.MiddleLeft, accentYellow);
        SetAnchors(compactPrompt.rectTransform, new Vector2(0.30f, 0.10f), new Vector2(0.96f, 0.43f));
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
        dim.color = new Color(0.015f, 0.012f, 0.025f, 0.58f);
        dim.raycastTarget = false;

        RectTransform yellowWedge = CreateRect(fullRoot, "YellowWedge", new Vector2(980f, 1260f));
        yellowWedge.anchorMin = yellowWedge.anchorMax = new Vector2(0f, 0.5f);
        yellowWedge.anchoredPosition = new Vector2(-390f, 0f);
        yellowWedge.localRotation = Quaternion.Euler(0f, 0f, -14f);
        Image wedgeImage = yellowWedge.gameObject.AddComponent<Image>();
        wedgeImage.color = new Color(accentYellow.r, accentYellow.g, accentYellow.b, 0.93f);
        wedgeImage.raycastTarget = false;

        Text title = CreateText(fullRoot, "LOADOUT // SHIFT", 56, FontStyle.Bold, TextAnchor.MiddleLeft, inkColor);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.sizeDelta = new Vector2(720f, 90f);
        titleRect.anchoredPosition = new Vector2(76f, -74f);
        titleRect.localRotation = Quaternion.Euler(0f, 0f, -4f);

        Text sub = CreateText(fullRoot, "HOLD TAB / LB   •   MOVE   •   RELEASE TO EQUIP", 14, FontStyle.Bold, TextAnchor.MiddleLeft, inkColor);
        RectTransform subRect = sub.rectTransform;
        subRect.anchorMin = subRect.anchorMax = new Vector2(0f, 1f);
        subRect.pivot = new Vector2(0f, 1f);
        subRect.sizeDelta = new Vector2(720f, 44f);
        subRect.anchoredPosition = new Vector2(92f, -150f);
        subRect.localRotation = Quaternion.Euler(0f, 0f, -4f);

        detailRoot = CreateRect(fullRoot, "DetailPanel", new Vector2(570f, 330f));
        detailRoot.anchorMin = detailRoot.anchorMax = new Vector2(0f, 0.5f);
        detailRoot.pivot = new Vector2(0f, 0.5f);
        detailRoot.anchoredPosition = new Vector2(98f, -90f);
        detailRoot.localRotation = Quaternion.Euler(0f, 0f, 3f);
        Image detailBack = detailRoot.gameObject.AddComponent<Image>();
        detailBack.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.96f);
        detailBack.raycastTarget = false;

        detailTitle = CreateText(detailRoot, "EMPTY", 29, FontStyle.Bold, TextAnchor.UpperLeft, paperColor);
        SetAnchors(detailTitle.rectTransform, new Vector2(0.07f, 0.67f), new Vector2(0.94f, 0.93f));
        detailTags = CreateText(detailRoot, "—", 13, FontStyle.Bold, TextAnchor.UpperLeft, accentCyan);
        SetAnchors(detailTags.rectTransform, new Vector2(0.07f, 0.37f), new Vector2(0.94f, 0.67f));
        synergySummary = CreateText(detailRoot, "GRID LINK 0", 15, FontStyle.Bold, TextAnchor.LowerLeft, accentYellow);
        SetAnchors(synergySummary.rectTransform, new Vector2(0.07f, 0.08f), new Vector2(0.94f, 0.37f));

        boardRoot = CreateRect(fullRoot, "GridBoard", new Vector2(720f, 650f));
        boardRoot.anchorMin = boardRoot.anchorMax = new Vector2(0.31f, 0.53f);
        boardRoot.anchoredPosition = Vector2.zero;
        boardRoot.localRotation = Quaternion.Euler(0f, 0f, -4f);

        RectTransform boardBack = CreateRect(boardRoot, "BoardBack", new Vector2(680f, 610f));
        boardBack.anchorMin = boardBack.anchorMax = new Vector2(0.5f, 0.5f);
        Image boardBackImage = boardBack.gameObject.AddComponent<Image>();
        boardBackImage.color = new Color(paperColor.r, paperColor.g, paperColor.b, 0.96f);
        boardBackImage.raycastTarget = false;
        Outline boardOutline = boardBack.gameObject.AddComponent<Outline>();
        boardOutline.effectColor = inkColor;
        boardOutline.effectDistance = new Vector2(8f, -8f);

        linkRoot = CreateRect(boardRoot, "SynergyLinks", new Vector2(680f, 610f));
        linkRoot.anchorMin = linkRoot.anchorMax = new Vector2(0.5f, 0.5f);

        const float cellW = 184f;
        const float cellH = 158f;
        const float spacingX = 22f;
        const float spacingY = 20f;
        float totalW = GridSize * cellW + (GridSize - 1) * spacingX;
        float totalH = GridSize * cellH + (GridSize - 1) * spacingY;
        float left = -totalW * 0.5f + cellW * 0.5f;
        float top = totalH * 0.5f - cellH * 0.5f;

        for (int i = 0; i < SlotCount; i++)
        {
            int x = i % GridSize;
            int y = i / GridSize;
            RectTransform slot = CreateRect(boardRoot, $"GridSlot_{i}", new Vector2(cellW, cellH));
            slot.anchorMin = slot.anchorMax = new Vector2(0.5f, 0.5f);
            slot.anchoredPosition = new Vector2(
                left + x * (cellW + spacingX),
                top - y * (cellH + spacingY));
            slot.localRotation = Quaternion.Euler(0f, 0f, ((i % 3) - 1) * 1.4f);
            slotRects[i] = slot;

            Image background = slot.gameObject.AddComponent<Image>();
            background.color = inkColor;
            background.raycastTarget = false;
            slotBackgrounds[i] = background;

            Outline outline = slot.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(4f, -4f);

            Image icon = CreateImage(slot, "Icon", new Vector2(78f, 78f));
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.28f, 0.60f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            slotIcons[i] = icon;

            Text name = CreateText(slot, "EMPTY", 13, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
            SetAnchors(name.rectTransform, new Vector2(0.50f, 0.42f), new Vector2(0.96f, 0.80f));
            slotNames[i] = name;

            Text grade = CreateText(slot, string.Empty, 10, FontStyle.Bold, TextAnchor.UpperRight, accentYellow);
            SetAnchors(grade.rectTransform, new Vector2(0.63f, 0.76f), new Vector2(0.94f, 0.94f));
            slotGrades[i] = grade;

            Text state = CreateText(slot, $"{x + 1}-{y + 1}", 10, FontStyle.Bold, TextAnchor.LowerLeft, accentCyan);
            SetAnchors(state.rectTransform, new Vector2(0.08f, 0.08f), new Vector2(0.94f, 0.30f));
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

            if (slotBackgrounds[i] != null)
            {
                slotBackgrounds[i].color = !unlocked
                    ? lockedColor
                    : isEquipped
                        ? new Color(accentCyan.r * 0.25f, accentCyan.g * 0.25f, accentCyan.b * 0.25f, 0.98f)
                        : inkColor;
            }

            if (slotRects[i] != null)
                slotRects[i].localScale = Vector3.one;

            if (slotIcons[i] != null)
            {
                slotIcons[i].sprite = equipment != null ? equipment.icon : null;
                slotIcons[i].enabled = unlocked && equipment != null && equipment.icon != null;
            }

            if (slotNames[i] != null)
            {
                slotNames[i].text = !unlocked
                    ? "LOCKED"
                    : equipment != null
                        ? equipment.GetDisplayName().ToUpperInvariant()
                        : "EMPTY";
                slotNames[i].color = paperColor;
            }

            if (slotGrades[i] != null)
            {
                slotGrades[i].text = unlocked && equipment != null ? $"G{slot.grade}" : string.Empty;
                slotGrades[i].color = accentYellow;
            }

            if (slotStates[i] != null)
            {
                Vector2Int p = BattleEquipmentSystem.SlotIndexToGrid(i);
                slotStates[i].text = !unlocked
                    ? $"{p.x + 1}-{p.y + 1}  // LOCK"
                    : isEquipped
                        ? "● EQUIPPED"
                        : $"{p.x + 1}-{p.y + 1}";
                slotStates[i].color = isEquipped ? accentCyan : new Color(0.55f, 0.60f, 0.70f, 1f);
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

    private void UpdateUiAnimation(bool combat)
    {
        if (fullGroup == null || compactGroup == null)
            return;

        bool wantFull = combat && switchHeld && boardWasShown;
        float t = 1f - Mathf.Exp(-Mathf.Max(1f, uiSharpness) * Time.unscaledDeltaTime);
        fullGroup.alpha = Mathf.Lerp(fullGroup.alpha, wantFull ? 1f : 0f, t);
        compactGroup.alpha = Mathf.Lerp(compactGroup.alpha, combat && !wantFull ? 1f : 0f, t);
    }

    private void ResolveLegacyCombatHud()
    {
        if (legacyEquipmentDock == null)
            legacyEquipmentDock = FindRectByName("EquipmentDock");
    }

    private static RectTransform FindRectByName(string target)
    {
        RectTransform[] all = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].name == target)
                return all[i];
        return null;
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

    private static Text CreateText(
        Transform parent,
        string value,
        int fontSize,
        FontStyle style,
        TextAnchor alignment,
        Color color)
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

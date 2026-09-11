using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Combat Tab과 Reward PACK 편집이 공유하는 Inventory UI의 authoritative layout owner입니다.
///
/// 이 클래스만 다음 RectTransform / CanvasGroup 상태를 씁니다.
/// - 좌측 하단 Mini PACK의 위치/크기/표시 상태
/// - 공용 LoadoutSwitchFull / GridBoard의 위치와 스케일
/// - 외부 EquipmentDetailPanel의 고정 위치
/// - Reward TRASH / DONE의 PACK 부착 위치
///
/// 슬롯 데이터와 교환 규칙은 BattleEquipmentSystem,
/// Reward 상태는 BattleRewardFlow,
/// 슬롯 입력은 BattleInventoryInteractionController가 소유합니다.
/// Reward 편집 slow-motion은 BattleTimeScaleController에 요청하며 Time.timeScale을 직접 쓰지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33380)]
public sealed class BattleUnifiedInventoryInspectController : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const int DismissCanvasSortingOrder = 779;
    private const float MiniCellSize = 78f;
    private const float MiniCellGap = 7f;
    private const float MiniGridTotal = MiniCellSize * 3f + MiniCellGap * 2f;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleRewardFlow rewardFlow;
    [SerializeField] private BattleInventoryInteractionController inventoryInteraction;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;
    [SerializeField] private BattleTimeScaleController timeScaleController;

    [Header("Mini PACK")]
    [SerializeField] private Vector2 miniPackSize = new(304f, 326f);
    [SerializeField] private Vector2 combatMiniPackPosition = new(22f, 22f);
    [SerializeField] private Vector2 rewardChoiceMiniPackPosition = new(34f, 28f);
    [SerializeField] private Vector2 miniGridOffset = new(20f, 18f);
    [SerializeField] private Vector2 miniHeaderSize = new(126f, 40f);
    [SerializeField] private Vector2 miniIconSize = new(64f, 64f);
    [SerializeField, Range(0.45f, 0.90f)] private float rewardChoiceMiniPackScale = 0.68f;
    [SerializeField, Range(0.5f, 1f)] private float rewardChoiceMiniPackAlpha = 0.82f;

    [Header("Full Inventory")]
    [SerializeField] private Vector2 boardAnchor = new(0.31f, 0.53f);
    [SerializeField, Range(0.90f, 1.08f)] private float fullGridScale = 1f;
    [SerializeField] private Vector2 detailAnchor = new(0.80f, 0.52f);
    [SerializeField, Range(0.80f, 1.10f)] private float detailScale = 0.96f;

    [Header("Reward Controls")]
    [SerializeField] private Vector2 trashAttachOffset = new(-8f, 0f);
    [SerializeField] private Vector2 doneAttachOffset = new(-8f, 80f);

    [Header("Reward Edit Time")]
    [SerializeField, Range(0.02f, 0.20f)] private float rewardInventoryTimeScale = 0.05f;

    [Header("Selection")]
    [SerializeField] private Color selectedAccent = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Color hoverAccent = new(0.14f, 0.92f, 0.94f, 1f);
    [SerializeField] private Color pickedAccent = new(1f, 0.18f, 0.52f, 1f);

    private RectTransform fullRoot;
    private CanvasGroup fullGroup;
    private RectTransform boardRoot;
    private RectTransform builtInDetailRoot;

    private RectTransform miniPackRoot;
    private RectTransform miniPackCells;
    private CanvasGroup miniPackGroup;

    private RectTransform detailRoot;
    private CanvasGroup detailGroup;
    private Outline detailOutline;

    private RectTransform trashRoot;
    private RectTransform doneRoot;
    private Text fullTitle;
    private Text fullSubtitle;

    private readonly GameObject[] fullSelectionFrames = new GameObject[SlotCount];
    private readonly Outline[] fullSelectionOutlines = new Outline[SlotCount];

    private Canvas dismissCanvas;
    private CanvasGroup dismissGroup;
    private RectTransform dismissRoot;

    private BattleRewardFullInspectController oldRewardFullInspect;
    private BattleEquipmentDetailContextLayoutController oldContextLayout;
    private BattleInventoryDragPresentationController oldDragPresentation;
    private BattleInventoryHudLayoutPolishController oldLayoutPolish;

    private bool selectionSuppressed;
    private int suppressedSourceSlot = -1;
    private int activeInspectSlot = -1;
    private bool mouseWasInsideBoard;
    private float nextResolveTime;

    public int ActiveInspectSlot => activeInspectSlot;

    private void Awake()
    {
        ResolveReferences();
        ResolveUi();
        EnsureDismissCanvas();
        DisableSupersededControllers();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveUi();
        EnsureDismissCanvas();
        DisableSupersededControllers();
        nextResolveTime = 0f;
    }

    private void OnDisable()
    {
        RestoreBulletTime(true);
        SetDismissActive(false);
    }

    private void Update()
    {
        ResolveReferences();
        rewardFlow?.RefreshFromRunState();
        EnsureDismissCanvas();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.10f;
            ResolveUi();
            EnsureFullSelectionFrames();
            DisableSupersededControllers();
        }

        bool rewardEdit = IsRewardEdit();
        bool combatTab = IsCombatTabOpen();
        bool inspectContext = rewardEdit || combatTab;

        if (rewardEdit)
            MaintainRewardBulletTime();
        else
            RestoreBulletTime(false);

        SyncSelection(rewardEdit, combatTab);
        HandleCancelInput(inspectContext);
        TrackMouseLeavingBoard(inspectContext);
        SetDismissActive(inspectContext);
    }

    private void LateUpdate()
    {
        ResolveUi();

        bool rewardChoice = IsRewardChoice();
        bool rewardEdit = IsRewardEdit();
        bool combatTab = IsCombatTabOpen();
        bool combat = IsCombat();
        bool inspectContext = rewardEdit || combatTab;

        HideDuplicateLegacyBars();
        ApplyMiniPackGeometry();
        ApplyMiniPackContext(rewardChoice, rewardEdit, combatTab, combat);
        ApplyFullInventoryLayout(rewardEdit, combatTab);
        AttachContextControls(rewardEdit, combat);
        ApplySelectionFrames(inspectContext, rewardEdit);
        PositionDetailPanel(inspectContext);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (rewardFlow == null)
            rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
        if (detailController == null)
            detailController = FindFirstObjectByType<BattleEquipmentDetailPanelController>(FindObjectsInactive.Include);
        if (timeScaleController == null)
            timeScaleController = BattleTimeScaleController.ResolveOrCreate(this);

        if (oldRewardFullInspect == null)
            oldRewardFullInspect = FindFirstObjectByType<BattleRewardFullInspectController>(FindObjectsInactive.Include);
        if (oldContextLayout == null)
            oldContextLayout = FindFirstObjectByType<BattleEquipmentDetailContextLayoutController>(FindObjectsInactive.Include);
        if (oldDragPresentation == null)
            oldDragPresentation = FindFirstObjectByType<BattleInventoryDragPresentationController>(FindObjectsInactive.Include);
        if (oldLayoutPolish == null)
            oldLayoutPolish = FindFirstObjectByType<BattleInventoryHudLayoutPolishController>(FindObjectsInactive.Include);
    }

    private void DisableSupersededControllers()
    {
        if (oldRewardFullInspect != null && oldRewardFullInspect.enabled)
            oldRewardFullInspect.enabled = false;
        if (oldContextLayout != null && oldContextLayout.enabled)
            oldContextLayout.enabled = false;
        if (oldDragPresentation != null && oldDragPresentation.enabled)
            oldDragPresentation.enabled = false;
        if (oldLayoutPolish != null && oldLayoutPolish.enabled)
            oldLayoutPolish.enabled = false;
    }

    private bool IsCombat()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Combat;
    }

    private bool IsRewardChoice()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward &&
               (rewardFlow == null || rewardFlow.Phase == BattleRewardPhase.Choosing);
    }

    private bool IsRewardEdit()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward &&
               rewardFlow != null && rewardFlow.Phase == BattleRewardPhase.PackEditing;
    }

    private bool IsCombatTabOpen()
    {
        return IsCombat() && kineticLoadout != null && kineticLoadout.IsSwitchBoardOpen;
    }

    private void ResolveUi()
    {
        if (kineticLoadout != null)
        {
            fullRoot ??= kineticLoadout.FullRoot;
            fullGroup ??= kineticLoadout.FullGroup;
            boardRoot ??= kineticLoadout.GridBoard;
        }

        if (fullRoot == null)
        {
            fullRoot = FindRect("LoadoutSwitchFull");
            if (fullRoot != null)
            {
                fullGroup = fullRoot.GetComponent<CanvasGroup>();
                boardRoot = FindChildRect(fullRoot, "GridBoard");
            }
        }

        if (fullRoot != null && builtInDetailRoot == null)
        {
            builtInDetailRoot = FindChildRect(fullRoot, "DetailPanel");
            ResolveFullHeaderTexts();
        }

        if (miniPackRoot == null)
        {
            miniPackRoot = FindRect("BackpackMiniGrid");
            if (miniPackRoot != null)
            {
                miniPackGroup = miniPackRoot.GetComponent<CanvasGroup>();
                miniPackCells = miniPackRoot.Find("BackpackCells") as RectTransform;
            }
        }

        if (detailRoot == null)
        {
            detailRoot = FindRect("EquipmentDetailPanel");
            if (detailRoot != null)
            {
                detailGroup = detailRoot.GetComponent<CanvasGroup>();
                detailOutline = detailRoot.GetComponent<Outline>();
            }
        }

        trashRoot ??= FindRect("InventoryTrash");
        doneRoot ??= FindRect("RewardPackDone");
    }

    private void ResolveFullHeaderTexts()
    {
        if (fullRoot == null)
            return;

        Text[] texts = fullRoot.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null)
                continue;

            string value = text.text ?? string.Empty;
            if (value == "LOADOUT // SHIFT" || value == "PACK // EDIT")
                fullTitle = text;
            else if (value.Contains("HOLD TAB / LB") || value.Contains("MOVE / SWAP"))
                fullSubtitle = text;
        }
    }

    private void ApplyMiniPackGeometry()
    {
        if (miniPackRoot == null)
            return;

        miniPackRoot.anchorMin = miniPackRoot.anchorMax = Vector2.zero;
        miniPackRoot.pivot = Vector2.zero;
        miniPackRoot.sizeDelta = miniPackSize;
        miniPackRoot.localRotation = Quaternion.Euler(0f, 0f, -1.15f);

        RectTransform header = miniPackRoot.Find("PackHeaderTag") as RectTransform;
        if (header != null)
        {
            header.sizeDelta = miniHeaderSize;
            header.anchoredPosition = new Vector2(10f, -5f);
        }

        Text[] headerTexts = miniPackRoot.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < headerTexts.Length; i++)
        {
            Text text = headerTexts[i];
            if (text == null)
                continue;
            if (text.text == "PACK")
                text.fontSize = 18;
            else if (text.text != null && text.text.Contains("/ 9"))
                text.fontSize = 12;
            else if (text.text != null && (text.text.Contains("TAB") || text.text.Contains("LB")))
                text.fontSize = 9;
        }

        miniPackCells ??= miniPackRoot.Find("BackpackCells") as RectTransform;
        if (miniPackCells == null)
            return;

        miniPackCells.anchorMin = miniPackCells.anchorMax = Vector2.zero;
        miniPackCells.pivot = Vector2.zero;
        miniPackCells.sizeDelta = new Vector2(MiniGridTotal, MiniGridTotal);
        miniPackCells.anchoredPosition = miniGridOffset;

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slot = miniPackCells.Find($"BackpackCell_{i}") as RectTransform;
            if (slot == null)
                continue;

            int x = i % BattleEquipmentSystem.GridSize;
            int y = i / BattleEquipmentSystem.GridSize;
            slot.sizeDelta = new Vector2(MiniCellSize, MiniCellSize);
            slot.anchorMin = slot.anchorMax = Vector2.zero;
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.anchoredPosition = new Vector2(
                x * (MiniCellSize + MiniCellGap) + MiniCellSize * 0.5f,
                MiniGridTotal - (y * (MiniCellSize + MiniCellGap) + MiniCellSize * 0.5f));

            RectTransform icon = slot.Find("Icon") as RectTransform;
            if (icon != null)
            {
                icon.sizeDelta = miniIconSize;
                icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
                icon.anchoredPosition = Vector2.zero;
            }

            RectTransform accent = slot.Find("CellAccent") as RectTransform;
            if (accent != null)
            {
                accent.sizeDelta = new Vector2(6f, MiniCellSize - 10f);
                accent.anchoredPosition = new Vector2(4f, 0f);
            }
        }
    }

    private void ApplyMiniPackContext(bool rewardChoice, bool rewardEdit, bool combatTab, bool combat)
    {
        if (miniPackRoot == null || miniPackGroup == null)
            return;

        if (rewardEdit || combatTab)
        {
            miniPackGroup.alpha = 0f;
            miniPackGroup.blocksRaycasts = false;
            miniPackGroup.interactable = false;
            return;
        }

        if (rewardChoice)
        {
            miniPackRoot.anchoredPosition = rewardChoiceMiniPackPosition;
            miniPackRoot.localScale = Vector3.one * rewardChoiceMiniPackScale;
            miniPackGroup.alpha = rewardChoiceMiniPackAlpha;
            miniPackGroup.blocksRaycasts = false;
            miniPackGroup.interactable = false;
            return;
        }

        if (combat)
        {
            miniPackRoot.anchoredPosition = combatMiniPackPosition;
            miniPackRoot.localScale = Vector3.one;
            miniPackGroup.alpha = 1f;
            bool interactive = !BattlePauseController.IsPaused;
            miniPackGroup.blocksRaycasts = interactive;
            miniPackGroup.interactable = interactive;
            return;
        }

        miniPackGroup.alpha = 0f;
        miniPackGroup.blocksRaycasts = false;
        miniPackGroup.interactable = false;
    }

    private void ApplyFullInventoryLayout(bool rewardEdit, bool combatTab)
    {
        if (boardRoot != null)
        {
            boardRoot.anchorMin = boardRoot.anchorMax = boardAnchor;
            boardRoot.pivot = new Vector2(0.5f, 0.5f);
            boardRoot.anchoredPosition = Vector2.zero;
        }

        if (fullRoot != null)
            fullRoot.localScale = Vector3.one * fullGridScale;

        if (rewardEdit && fullRoot != null && fullGroup != null)
        {
            fullRoot.gameObject.SetActive(true);
            fullGroup.alpha = 1f;
            fullGroup.blocksRaycasts = !BattlePauseController.IsPaused;
            fullGroup.interactable = !BattlePauseController.IsPaused;
        }

        if (fullTitle != null)
            fullTitle.text = rewardEdit ? "PACK // EDIT" : "LOADOUT // SHIFT";
        if (fullSubtitle != null)
            fullSubtitle.text = rewardEdit
                ? "MOVE / SWAP  •  B CANCEL  •  TRASH  •  DONE"
                : "HOLD TAB / LB   •   MOVE   •   RELEASE TO EQUIP";

        if (builtInDetailRoot != null)
            builtInDetailRoot.gameObject.SetActive(!(rewardEdit || combatTab));
    }

    private void HideDuplicateLegacyBars()
    {
        HideByName("EquipmentDock");
        HideByName("RewardLoadoutStrip");
    }

    private static void HideByName(string objectName)
    {
        RectTransform rect = FindRect(objectName);
        if (rect != null && rect.gameObject.activeSelf)
            rect.gameObject.SetActive(false);
    }

    private void AttachContextControls(bool rewardEdit, bool combat)
    {
        if (rewardEdit && boardRoot != null)
        {
            AttachControl(trashRoot, boardRoot, trashAttachOffset);
            AttachControl(doneRoot, boardRoot, doneAttachOffset);
            return;
        }

        if (combat && inventoryInteraction != null && inventoryInteraction.IsDraggingItem && miniPackRoot != null)
            AttachControl(trashRoot, miniPackRoot, trashAttachOffset);
    }

    private static void AttachControl(RectTransform control, RectTransform parent, Vector2 localOffset)
    {
        if (control == null || parent == null)
            return;

        if (control.parent != parent)
            control.SetParent(parent, false);

        control.anchorMin = control.anchorMax = new Vector2(1f, 0f);
        control.pivot = new Vector2(0f, 0f);
        control.anchoredPosition = localOffset;
        control.localRotation = Quaternion.identity;
        control.localScale = Vector3.one;
        control.SetAsLastSibling();
    }

    private void SyncSelection(bool rewardEdit, bool combatTab)
    {
        int source = -1;

        if (rewardEdit && inventoryInteraction != null)
        {
            int pad = inventoryInteraction.PadSelectedSlot;
            int mouse = inventoryInteraction.SelectedRewardSlot;
            int hover = inventoryInteraction.HoveredSlot;

            if (inventoryInteraction.PadModeActive && HasItem(pad))
                source = pad;
            else if (HasItem(mouse))
                source = mouse;
            else if (HasItem(hover))
                source = hover;
            else if (activeInspectSlot >= 0 && HasItem(activeInspectSlot))
                source = activeInspectSlot;
            else if (!selectionSuppressed && rewardFlow != null && rewardFlow.ChosenRewardCommitted &&
                     HasItem(rewardFlow.ChosenRewardSlot))
                source = rewardFlow.ChosenRewardSlot;
        }
        else if (combatTab && kineticLoadout != null)
        {
            source = kineticLoadout.SelectedIndex;
        }

        if (selectionSuppressed)
        {
            if (source >= 0 && source != suppressedSourceSlot)
            {
                selectionSuppressed = false;
                suppressedSourceSlot = -1;
            }
            else
            {
                SetActiveInspectSlot(-1);
                return;
            }
        }

        SetActiveInspectSlot(HasItem(source) ? source : -1);
    }

    private void SetActiveInspectSlot(int slotIndex)
    {
        if (activeInspectSlot == slotIndex)
            return;

        activeInspectSlot = slotIndex;
        detailController?.SelectSlotFromPointer(slotIndex);
    }

    public void CancelSelection()
    {
        int current = activeInspectSlot;
        if (current < 0 && kineticLoadout != null)
            current = kineticLoadout.SelectedIndex;
        if (current < 0 && inventoryInteraction != null)
            current = inventoryInteraction.SelectedRewardSlot;

        selectionSuppressed = true;
        suppressedSourceSlot = current;
        activeInspectSlot = -1;

        kineticLoadout?.ClearExternalSelection();
        detailController?.SelectSlotFromPointer(-1);

        if (detailGroup != null)
            detailGroup.alpha = 0f;
    }

    private void HandleCancelInput(bool inspectContext)
    {
        if (!inspectContext || BattlePauseController.IsPaused)
            return;

        if (Input.GetKeyDown(KeyCode.JoystickButton1))
            CancelSelection();
    }

    private void TrackMouseLeavingBoard(bool inspectContext)
    {
        if (!inspectContext || boardRoot == null || !Input.mousePresent)
        {
            mouseWasInsideBoard = false;
            return;
        }

        bool inside = RectTransformUtility.RectangleContainsScreenPoint(boardRoot, Input.mousePosition, null);
        if (inside)
        {
            mouseWasInsideBoard = true;
            return;
        }

        if (mouseWasInsideBoard)
        {
            mouseWasInsideBoard = false;
            CancelSelection();
        }
    }

    private void EnsureFullSelectionFrames()
    {
        if (boardRoot == null)
            return;

        for (int i = 0; i < SlotCount; i++)
        {
            if (fullSelectionFrames[i] != null)
                continue;

            RectTransform slot = FindChildRect(boardRoot, $"GridSlot_{i}");
            if (slot == null)
                continue;

            RectTransform frame = slot.Find("UnifiedSelectionFrame") as RectTransform;
            if (frame == null)
            {
                frame = CreateRect(slot, "UnifiedSelectionFrame", Vector2.zero);
                Stretch(frame);
                Image image = frame.gameObject.AddComponent<Image>();
                image.color = Color.clear;
                image.raycastTarget = false;
                frame.SetAsLastSibling();
            }

            Outline outline = frame.GetComponent<Outline>();
            if (outline == null)
                outline = frame.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(5f, -5f);

            fullSelectionFrames[i] = frame.gameObject;
            fullSelectionOutlines[i] = outline;
            frame.gameObject.SetActive(false);
        }
    }

    private void ApplySelectionFrames(bool inspectContext, bool rewardEdit)
    {
        EnsureFullSelectionFrames();

        for (int i = 0; i < SlotCount; i++)
        {
            GameObject frame = fullSelectionFrames[i];
            if (frame == null)
                continue;

            bool selected = inspectContext && !selectionSuppressed && i == activeInspectSlot && HasItem(i);
            if (frame.activeSelf != selected)
                frame.SetActive(selected);
            if (!selected)
                continue;

            bool picked = rewardEdit && inventoryInteraction != null && inventoryInteraction.PadPickedSlot == i;
            bool hovered = rewardEdit && inventoryInteraction != null && inventoryInteraction.HoveredSlot == i;
            Outline outline = fullSelectionOutlines[i];
            if (outline != null)
            {
                outline.effectColor = picked ? pickedAccent : hovered ? hoverAccent : selectedAccent;
                outline.effectDistance = picked ? new Vector2(7f, -7f) : new Vector2(5f, -5f);
            }
        }
    }

    private void PositionDetailPanel(bool inspectContext)
    {
        if (detailRoot == null || detailGroup == null)
            return;

        bool show = inspectContext && activeInspectSlot >= 0 && !selectionSuppressed && HasItem(activeInspectSlot);
        if (!show)
        {
            detailGroup.alpha = 0f;
            return;
        }

        detailRoot.anchorMin = detailRoot.anchorMax = detailAnchor;
        detailRoot.pivot = new Vector2(0.5f, 0.5f);
        detailRoot.anchoredPosition = Vector2.zero;
        detailRoot.localScale = Vector3.one * detailScale;
        detailRoot.localRotation = Quaternion.identity;

        detailGroup.alpha = 1f;
        detailGroup.blocksRaycasts = false;
        detailGroup.interactable = false;

        if (detailOutline != null)
        {
            detailOutline.effectColor = selectedAccent;
            detailOutline.effectDistance = new Vector2(6f, -6f);
        }
    }

    private void EnsureDismissCanvas()
    {
        if (dismissCanvas != null)
            return;

        GameObject canvasObject = new("BattleInventoryDismissCanvas");
        canvasObject.transform.SetParent(transform, false);
        dismissCanvas = canvasObject.AddComponent<Canvas>();
        dismissCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        dismissCanvas.overrideSorting = true;
        dismissCanvas.sortingOrder = DismissCanvasSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        dismissRoot = CreateRect(canvasObject.transform, "InventoryDismissBackground", Vector2.zero);
        Stretch(dismissRoot);
        Image image = dismissRoot.gameObject.AddComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = true;

        BattleUnifiedInventoryDismissRelay relay = dismissRoot.gameObject.AddComponent<BattleUnifiedInventoryDismissRelay>();
        relay.Configure(this);

        dismissGroup = dismissRoot.gameObject.AddComponent<CanvasGroup>();
        dismissGroup.alpha = 0f;
        dismissGroup.blocksRaycasts = false;
        dismissGroup.interactable = false;
    }

    private void SetDismissActive(bool active)
    {
        if (dismissGroup == null)
            return;

        bool interactable = active && !BattlePauseController.IsPaused;
        dismissGroup.alpha = 0f;
        dismissGroup.blocksRaycasts = interactable;
        dismissGroup.interactable = interactable;
    }

    private void MaintainRewardBulletTime()
    {
        ResolveReferences();
        if (timeScaleController == null)
            return;

        float scale = Mathf.Clamp(rewardInventoryTimeScale, 0.02f, 0.20f);
        timeScaleController.Request(BattleTimeScaleController.Owner.RewardInventory, scale);
    }

    private void RestoreBulletTime(bool force)
    {
        timeScaleController?.Release(BattleTimeScaleController.Owner.RewardInventory);
    }

    private bool HasItem(int slotIndex)
    {
        return equipmentSystem != null && slotIndex >= 0 && slotIndex < equipmentSystem.Slots.Count &&
               equipmentSystem.IsSlotUnlocked(slotIndex) && equipmentSystem.Slots[slotIndex] != null &&
               equipmentSystem.Slots[slotIndex].equipment != null;
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }
        return null;
    }

    private static RectTransform FindChildRect(Transform parent, string objectName)
    {
        if (parent == null)
            return null;

        RectTransform[] all = parent.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].name == objectName)
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

internal sealed class BattleUnifiedInventoryDismissRelay : MonoBehaviour, IPointerClickHandler
{
    private BattleUnifiedInventoryInspectController owner;

    public void Configure(BattleUnifiedInventoryInspectController controller)
    {
        owner = controller;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            owner?.CancelSelection();
    }
}

public static class BattleUnifiedInventoryInspectAutoInstaller
{
#if UNITY_EDITOR
    private static bool installQueued;

    [InitializeOnLoadMethod]
    private static void InitializeEditorInstaller()
    {
        EditorApplication.hierarchyChanged -= QueueInstall;
        EditorApplication.hierarchyChanged += QueueInstall;
        QueueInstall();
    }

    private static void QueueInstall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || installQueued)
            return;
        installQueued = true;
        EditorApplication.delayCall += EnsureEditorComponent;
    }

    private static void EnsureEditorComponent()
    {
        installQueued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null || EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded)
                continue;

            if (manager.GetComponent<BattleUnifiedInventoryInspectController>() != null)
                continue;

            Undo.AddComponent<BattleUnifiedInventoryInspectController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponent()
    {
        BattleSceneManager[] managers = Object.FindObjectsByType<BattleSceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleUnifiedInventoryInspectController>() == null)
                manager.gameObject.AddComponent<BattleUnifiedInventoryInspectController>();
        }
    }
}

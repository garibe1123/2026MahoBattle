using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal enum BattleInventorySurface
{
    MiniPack,
    ExpandedGrid,
    RewardPack
}

/// <summary>
/// 3x3 PACK의 입력과 입력에 직접 종속된 임시 시각 상태를 관리합니다.
///
/// 책임:
/// - Combat Mini PACK 클릭
/// - Reward PACK 슬롯 선택/교환/Drag
/// - Reward Hand <-> PACK 교환
/// - Reward Hand / Drag Ghost
/// - TRASH / 삭제 확인 입력
/// - 패드 PACK 커서/선택
/// - DONE 요청
///
/// Mouse/Keyboard와 Gamepad는 서로 다른 조작 규칙을 사용합니다.
/// - Mouse/Keyboard: Hover/Click으로 Inspect, Drag & Drop으로 이동/교환
/// - Gamepad: Stick 1회 입력으로 커서 이동, A로 Pick/Place
///
/// 이 클래스는 Mini PACK / Full Grid / Detail / TRASH / DONE의 최종 RectTransform을 쓰지 않습니다.
/// 레이아웃은 BattleUnifiedInventoryInspectController가 단독 소유합니다.
/// Reward business state는 BattleRewardFlow가 단독 소유합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30750)]
public sealed class BattleInventoryInteractionController : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const int OverlaySortingOrder = 1550;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleRewardFlow rewardFlow;

    [Header("Theme")]
    [SerializeField] private Color inkColor = new(0.035f, 0.030f, 0.055f, 0.995f);
    [SerializeField] private Color paperColor = new(0.94f, 0.90f, 0.76f, 1f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentCyan = new(0.15f, 0.88f, 0.92f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);

    [Header("Pad PACK Edit")]
    [SerializeField, Range(0.25f, 0.95f)] private float padAxisThreshold = 0.55f;
    [SerializeField, Range(0.05f, 0.8f)] private float padAxisReleaseThreshold = 0.22f;

    [Header("Reward Hand")]
    [SerializeField] private Vector2 handMouseOffset = new(72f, -72f);
    [SerializeField] private Vector2 handPadOffset = new(92f, 0f);

    [Header("Reward Transfer")]
    [SerializeField, Range(0.18f, 0.28f)] private float rewardTransferDuration = 0.23f;
    [SerializeField, Range(30f, 160f)] private float rewardTransferArcHeight = 90f;

    [Header("Optional Reward Transfer Audio")]
    [SerializeField] private AudioClip rewardSelectClip;
    [SerializeField] private AudioClip rewardTransferClip;
    [SerializeField] private AudioClip rewardInstallClip;

    private Canvas interactionCanvas;
    private RectTransform interactionRoot;

    private RectTransform dragGhostRoot;
    private Image dragGhostIcon;
    private RectTransform handGhostRoot;
    private Image handGhostIcon;

    private RectTransform transferGhostRoot;
    private Image transferGhostIcon;
    private Outline transferGhostOutline;
    private readonly RectTransform[] transferTrailRoots = new RectTransform[3];
    private readonly Image[] transferTrailIcons = new Image[3];
    private RectTransform transferImpactRoot;
    private CanvasGroup transferImpactGroup;

    private AudioSource rewardFeedbackAudio;
    private AudioClip fallbackSelectClip;
    private AudioClip fallbackTransferClip;
    private AudioClip fallbackInstallClip;
    private Coroutine rewardTransferRoutine;

    private RectTransform trashRoot;
    private Image trashBack;
    private Text trashLabel;

    private RectTransform doneRoot;
    private Button doneButton;

    private RectTransform discardConfirmRoot;
    private Text discardConfirmText;
    private Image confirmYesBack;
    private Image confirmNoBack;
    private Text confirmYesText;
    private Text confirmNoText;

    private RectTransform miniPackRoot;
    private readonly GameObject[] miniSelectionFrames = new GameObject[SlotCount];
    private readonly Outline[] miniSelectionOutlines = new Outline[SlotCount];
    private readonly GameObject[] fullSelectionFrames = new GameObject[SlotCount];
    private readonly Outline[] fullSelectionOutlines = new Outline[SlotCount];

    // Input state only. These names remain stable until Phase 6 removes legacy visual reflection users.
    private int selectedRewardSlot = -1;
    private int hoveredSlot = -1;
    private int draggingSlot = -1;
    private int padSelectedSlot;
    private int padPickedSlot = -1;
    private bool padAxisLatched;
    private bool padModeActive;

    private BattleRunState lastState = (BattleRunState)(-1);
    private BattleRewardPhase lastRewardPhase = (BattleRewardPhase)(-1);

    private int pendingDiscardSlot = -1;
    private bool confirmYesSelected;
    private bool discardModalOpen;

    private int flashedSlot = -1;
    private float flashUntil;
    private float nextResolveTime;

    public bool IsDraggingItem => draggingSlot >= 0;
    public int DraggingSlot => draggingSlot;
    public bool IsRewardPackEditing => IsReward() && rewardFlow != null && rewardFlow.Phase == BattleRewardPhase.PackEditing;
    public int SelectedRewardSlot => selectedRewardSlot;
    public int HoveredSlot => hoveredSlot;
    public int PadSelectedSlot => padSelectedSlot;
    public int PadPickedSlot => padPickedSlot;
    public bool PadModeActive => padModeActive;

    private void Awake()
    {
        ResolveReferences();
        EnsureOverlayCanvas();
        ResolveMiniPack();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureOverlayCanvas();
        nextResolveTime = 0f;
    }

    private void OnDisable()
    {
        HideTransientVisuals();
        HideDiscardConfirm();
    }

    private void Update()
    {
        ResolveReferences();
        rewardFlow?.RefreshFromRunState();
        EnsureOverlayCanvas();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.12f;
            ResolveMiniPack();
            InstallSlotTargets();
        }

        if (runManager != null && lastState != runManager.State)
        {
            HandleRunStateChanged(runManager.State);
            lastState = runManager.State;
        }

        BattleRewardPhase phase = rewardFlow != null
            ? rewardFlow.Phase
            : BattleRewardPhase.Inactive;
        if (lastRewardPhase != phase)
        {
            HandleRewardPhaseChanged(phase);
            lastRewardPhase = phase;
        }

        if (IsRewardPackEditing)
        {
            if (rewardFlow != null && rewardFlow.HasHand)
            {
                selectedRewardSlot = -1;
                padPickedSlot = -1;
            }
            HandlePadPackInput();
        }
        else
        {
            padPickedSlot = -1;
            padModeActive = false;
        }

        UpdateSelectionFrames();
        UpdateTrashVisibility();
        UpdateRewardDoneState();
        UpdateRewardHandVisual();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (rewardFlow == null)
        {
            rewardFlow = GetComponent<BattleRewardFlow>();
            if (rewardFlow == null)
                rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
            if (rewardFlow == null && Application.isPlaying)
                rewardFlow = gameObject.AddComponent<BattleRewardFlow>();
        }
    }

    private bool IsReward()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
    }

    private bool IsCombat()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Combat;
    }

    private void HandleRunStateChanged(BattleRunState state)
    {
        ClearInspectSelection();
        draggingSlot = -1;
        flashedSlot = -1;
        HideDiscardConfirm();

        if (state == BattleRunState.Reward)
            padSelectedSlot = FindFirstUnlockedSlot();
        else if (handGhostRoot != null)
            handGhostRoot.gameObject.SetActive(false);
    }

    private void HandleRewardPhaseChanged(BattleRewardPhase phase)
    {
        ClearInspectSelection();
        HideDiscardConfirm();

        if (phase != BattleRewardPhase.PackEditing || rewardFlow == null)
        {
            if (handGhostRoot != null)
                handGhostRoot.gameObject.SetActive(false);
            return;
        }

        int focus = rewardFlow.ChosenRewardCommitted
            ? rewardFlow.ChosenRewardSlot
            : FindFirstUnlockedSlot();
        if (focus < 0 || equipmentSystem == null || !equipmentSystem.IsSlotUnlocked(focus))
            focus = FindFirstUnlockedSlot();

        // 패드 커서의 시작 위치만 준비합니다. Mouse/Keyboard에서는 자동으로 슬롯을 Pick하지 않습니다.
        padSelectedSlot = Mathf.Max(0, focus);
    }

    /// <summary>
    /// Detail/selection presentation만 닫습니다. PACK 데이터나 Reward 상태는 변경하지 않습니다.
    /// </summary>
    public void ClearInspectSelection()
    {
        selectedRewardSlot = -1;
        hoveredSlot = -1;
        padPickedSlot = -1;
        padModeActive = false;
        padAxisLatched = false;
    }

    private void ActivateMouseMode()
    {
        padModeActive = false;
        padAxisLatched = false;
        padPickedSlot = -1;
    }

    private void ActivatePadMode()
    {
        if (!padModeActive)
        {
            hoveredSlot = -1;
            selectedRewardSlot = -1;
        }
        padModeActive = true;
    }

    private static bool IsKeyboardDirectionalInputHeld()
    {
        return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) ||
               Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S) ||
               Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow) ||
               Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow);
    }

    private void ResolveMiniPack()
    {
        if (miniPackRoot == null)
            miniPackRoot = FindRect("BackpackMiniGrid");
        if (miniPackRoot == null)
            return;

        Canvas canvas = miniPackRoot.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        // 실제 상호작용 on/off는 Unified Inventory View의 CanvasGroup이 소유합니다.
        Image packImage = miniPackRoot.GetComponent<Image>();
        if (packImage != null)
            packImage.raycastTarget = true;

        BattleInventoryPackDropTarget legacyDrop = miniPackRoot.GetComponent<BattleInventoryPackDropTarget>();
        if (legacyDrop != null)
            legacyDrop.enabled = false;
    }

    private void InstallSlotTargets()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            InstallSlotTarget(FindRect($"BackpackCell_{i}"), i, BattleInventorySurface.MiniPack);
            InstallSlotTarget(FindRect($"GridSlot_{i}"), i, BattleInventorySurface.ExpandedGrid);
            InstallSlotTarget(FindRect($"RewardLoadoutSlot_{i + 1}"), i, BattleInventorySurface.RewardPack);
        }
    }

    private void InstallSlotTarget(RectTransform rect, int index, BattleInventorySurface surface)
    {
        if (rect == null)
            return;

        Image image = rect.GetComponent<Image>();
        if (image != null)
            image.raycastTarget = true;

        Canvas canvas = rect.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        BattleInventorySlotPointer pointer = rect.GetComponent<BattleInventorySlotPointer>();
        if (pointer == null)
            pointer = rect.gameObject.AddComponent<BattleInventorySlotPointer>();
        pointer.Configure(this, index, surface);

        if (surface == BattleInventorySurface.MiniPack)
            EnsureMiniSelectionFrame(rect, index);
        else if (surface == BattleInventorySurface.ExpandedGrid)
            EnsureFullSelectionFrame(rect, index);
    }

    private void EnsureMiniSelectionFrame(RectTransform slot, int index)
    {
        if (index < 0 || index >= SlotCount || miniSelectionFrames[index] != null)
            return;

        RectTransform frame = slot.Find("InteractionSelectionFrame") as RectTransform;
        if (frame == null)
        {
            frame = CreateRect(slot, "InteractionSelectionFrame", Vector2.zero);
            Stretch(frame);
            Image image = frame.gameObject.AddComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = false;
            frame.SetAsLastSibling();
        }

        Outline outline = frame.GetComponent<Outline>();
        if (outline == null)
            outline = frame.gameObject.AddComponent<Outline>();
        outline.effectColor = accentYellow;
        outline.effectDistance = new Vector2(5f, -5f);
        outline.useGraphicAlpha = false;

        miniSelectionFrames[index] = frame.gameObject;
        miniSelectionOutlines[index] = outline;
        frame.gameObject.SetActive(false);
    }

    private void EnsureFullSelectionFrame(RectTransform slot, int index)
    {
        if (index < 0 || index >= SlotCount || fullSelectionFrames[index] != null)
            return;

        RectTransform frame = slot.Find("InteractionFullSelectionFrame") as RectTransform;
        if (frame == null)
        {
            frame = CreateRect(slot, "InteractionFullSelectionFrame", Vector2.zero);
            Stretch(frame);
            Image image = frame.gameObject.AddComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = false;
            frame.SetAsLastSibling();
        }

        Outline outline = frame.GetComponent<Outline>();
        if (outline == null)
            outline = frame.gameObject.AddComponent<Outline>();
        outline.effectColor = accentYellow;
        outline.effectDistance = new Vector2(5f, -5f);
        outline.useGraphicAlpha = false;

        fullSelectionFrames[index] = frame.gameObject;
        fullSelectionOutlines[index] = outline;
        frame.gameObject.SetActive(false);
    }

    internal bool BeginSlotDrag(int slotIndex, BattleInventorySurface surface, PointerEventData eventData)
    {
        if (eventData == null || discardModalOpen || equipmentSystem == null || !HasItem(slotIndex))
            return false;

        ActivateMouseMode();

        if (IsReward())
        {
            if (!IsRewardPackEditing || rewardFlow == null || rewardFlow.HasHand)
                return false;
            selectedRewardSlot = slotIndex;
        }
        else if (!IsCombat())
        {
            return false;
        }

        draggingSlot = slotIndex;
        flashedSlot = -1;

        BattleEquipmentSO equipment = equipmentSystem.Slots[slotIndex].equipment;
        if (dragGhostRoot != null)
        {
            dragGhostRoot.gameObject.SetActive(true);
            dragGhostRoot.position = eventData.position;
            dragGhostRoot.SetAsLastSibling();
        }
        if (dragGhostIcon != null)
        {
            dragGhostIcon.sprite = equipment != null ? equipment.icon : null;
            dragGhostIcon.enabled = equipment != null && equipment.icon != null;
        }

        UpdateTrashVisibility();
        return true;
    }

    internal void UpdateSlotDrag(PointerEventData eventData)
    {
        if (draggingSlot >= 0 && dragGhostRoot != null && eventData != null)
            dragGhostRoot.position = eventData.position;
    }

    internal void EndSlotDrag()
    {
        draggingSlot = -1;
        if (dragGhostRoot != null)
            dragGhostRoot.gameObject.SetActive(false);
        UpdateTrashVisibility();
    }

    internal void HandleSlotDrop(int targetIndex, PointerEventData eventData)
    {
        if (discardModalOpen || equipmentSystem == null || eventData == null ||
            !equipmentSystem.IsSlotUnlocked(targetIndex))
            return;

        ActivateMouseMode();

        BattleInventorySlotPointer source = eventData.pointerDrag != null
            ? eventData.pointerDrag.GetComponent<BattleInventorySlotPointer>()
            : null;
        if (source == null || !source.IsDragging)
            return;

        if (IsReward() && (!IsRewardPackEditing || rewardFlow == null || rewardFlow.HasHand))
            return;

        int sourceIndex = source.SlotIndex;
        if (!equipmentSystem.IsSlotUnlocked(sourceIndex) || sourceIndex == targetIndex)
            return;

        if (!equipmentSystem.SwapSlots(sourceIndex, targetIndex))
            return;

        selectedRewardSlot = IsRewardPackEditing ? targetIndex : -1;
        padSelectedSlot = targetIndex;
        padPickedSlot = -1;
        rewardFlow?.SyncChosenRewardLocation();
        FlashSlot(targetIndex);
    }

    // Legacy Reward card drag target. 클릭 -> 결정 흐름에서는 사용하지 않습니다.
    internal void HandlePackDrop(PointerEventData eventData)
    {
    }

    internal void HandleSlotClick(int slotIndex, BattleInventorySurface surface, PointerEventData.InputButton button)
    {
        if (button != PointerEventData.InputButton.Left || discardModalOpen || equipmentSystem == null ||
            !equipmentSystem.IsSlotUnlocked(slotIndex))
            return;

        ActivateMouseMode();

        if (IsReward())
        {
            if (!IsRewardPackEditing || rewardFlow == null)
                return;

            // Hand가 있으면 클릭한 슬롯에 바로 배치/교환합니다.
            if (rewardFlow.HasHand)
            {
                if (rewardFlow.ExchangeHandWithSlot(slotIndex))
                {
                    selectedRewardSlot = HasItem(slotIndex) ? slotIndex : -1;
                    FlashSlot(slotIndex);
                }
                return;
            }

            // Mouse/Keyboard 모드에서는 클릭이 위치 변경을 일으키지 않습니다.
            // 클릭은 Inspect/Selection만 하고, 이동/교환은 Drag & Drop으로만 수행합니다.
            selectedRewardSlot = HasItem(slotIndex) ? slotIndex : -1;
            return;
        }

        // Expanded Combat Grid는 BattleKineticLoadoutUI가 단독 처리합니다.
        if (IsCombat() && surface != BattleInventorySurface.ExpandedGrid)
        {
            BattleEquipmentSlot slot = slotIndex < equipmentSystem.Slots.Count ? equipmentSystem.Slots[slotIndex] : null;
            if (slot != null && slot.equipment != null && slot.equipment.shootingData != null)
                equipmentSystem.EquipSlot(slotIndex);
        }
    }

    internal void HandleSlotHover(int slotIndex, bool entered)
    {
        if (!IsRewardPackEditing)
            return;

        if (entered)
            ActivateMouseMode();
        hoveredSlot = entered ? slotIndex : (hoveredSlot == slotIndex ? -1 : hoveredSlot);
    }

    internal void HandleTrashDrop(PointerEventData eventData)
    {
        if (discardModalOpen || equipmentSystem == null || eventData == null)
            return;

        ActivateMouseMode();

        BattleInventorySlotPointer source = eventData.pointerDrag != null
            ? eventData.pointerDrag.GetComponent<BattleInventorySlotPointer>()
            : null;
        if (source == null || !source.IsDragging || !HasItem(source.SlotIndex))
            return;

        RequestDiscard(source.SlotIndex);
    }

    internal void HandleTrashClick(PointerEventData.InputButton button)
    {
        if (button != PointerEventData.InputButton.Left || discardModalOpen || !IsRewardPackEditing || rewardFlow == null)
            return;

        ActivateMouseMode();

        if (rewardFlow.HasHand)
        {
            if (rewardFlow.DiscardHand())
                selectedRewardSlot = -1;
            return;
        }

        int target = selectedRewardSlot;
        if (HasItem(target))
            RequestDiscard(target);
    }

    internal void HandleTrashHover(bool hovered)
    {
        if (hovered)
            ActivateMouseMode();

        if (trashBack != null)
            trashBack.color = hovered
                ? new Color(accentPink.r, accentPink.g, accentPink.b, 0.98f)
                : inkColor;
        if (trashLabel != null)
            trashLabel.color = hovered ? inkColor : accentPink;
    }

    private void RequestDiscard(int slotIndex)
    {
        if (!HasItem(slotIndex))
            return;

        pendingDiscardSlot = slotIndex;
        confirmYesSelected = false;
        discardModalOpen = true;
        padPickedSlot = -1;

        BattleEquipmentSO equipment = equipmentSystem.Slots[slotIndex].equipment;
        string itemName = equipment != null ? equipment.GetDisplayName() : "ITEM";
        if (discardConfirmText != null)
            discardConfirmText.text = $"{itemName}\n\n이 아이템을 삭제할까요?\n정말 삭제하시겠습니까?";

        if (discardConfirmRoot != null)
        {
            discardConfirmRoot.gameObject.SetActive(true);
            discardConfirmRoot.SetAsLastSibling();
        }
        RefreshConfirmSelection();
    }

    private void ResolveDiscard(bool yes)
    {
        if (!discardModalOpen)
            return;

        int target = pendingDiscardSlot;
        HideDiscardConfirm();

        if (!yes || !HasItem(target))
            return;

        if (equipmentSystem.DiscardSlot(target))
        {
            if (selectedRewardSlot == target)
                selectedRewardSlot = -1;
            if (padPickedSlot == target)
                padPickedSlot = -1;
            rewardFlow?.SyncChosenRewardLocation();
            FlashSlot(target);
        }
    }

    private void HideDiscardConfirm()
    {
        discardModalOpen = false;
        pendingDiscardSlot = -1;
        confirmYesSelected = false;
        if (discardConfirmRoot != null)
            discardConfirmRoot.gameObject.SetActive(false);
    }

    private void HandlePadPackInput()
    {
        if (!IsRewardPackEditing || rewardFlow == null || BattlePauseController.IsPaused)
            return;

        if (discardModalOpen)
        {
            HandlePadConfirmInput();
            return;
        }

        // Legacy Horizontal/Vertical axis에는 키보드와 패드가 함께 묶여 있으므로,
        // 키보드 방향 입력이 눌린 동안에는 axis를 패드 입력으로 해석하지 않습니다.
        bool keyboardDirectionHeld = IsKeyboardDirectionalInputHeld();
        float axisX = keyboardDirectionHeld ? 0f : Input.GetAxisRaw("Horizontal");
        float axisY = keyboardDirectionHeld ? 0f : Input.GetAxisRaw("Vertical");
        float magnitude = Mathf.Max(Mathf.Abs(axisX), Mathf.Abs(axisY));

        if (!padAxisLatched && magnitude >= padAxisThreshold)
        {
            int dx = 0;
            int dy = 0;
            if (Mathf.Abs(axisX) >= Mathf.Abs(axisY))
                dx = axisX > 0f ? 1 : -1;
            else
                dy = axisY > 0f ? -1 : 1;

            ActivatePadMode();
            MovePadSelection(dx, dy);
            padAxisLatched = true;
        }
        else if (padAxisLatched && magnitude <= padAxisReleaseThreshold)
        {
            // Stick이 중립으로 돌아와야 다음 1회 이동을 받을 수 있습니다.
            padAxisLatched = false;
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton0))
        {
            ActivatePadMode();
            HandlePadSubmit();
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton1))
        {
            ActivatePadMode();
            selectedRewardSlot = -1;
            padPickedSlot = -1;
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton3))
        {
            ActivatePadMode();
            if (rewardFlow.HasHand)
                rewardFlow.DiscardHand();
            else if (HasItem(padSelectedSlot))
                RequestDiscard(padSelectedSlot);
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton7))
        {
            ActivatePadMode();
            RequestRewardCompletion();
        }
    }

    private void MovePadSelection(int dx, int dy)
    {
        if (equipmentSystem == null)
            return;

        Vector2Int p = BattleEquipmentSystem.SlotIndexToGrid(Mathf.Clamp(padSelectedSlot, 0, SlotCount - 1));
        int nx = Mathf.Clamp(p.x + dx, 0, BattleEquipmentSystem.GridSize - 1);
        int ny = Mathf.Clamp(p.y + dy, 0, BattleEquipmentSystem.GridSize - 1);
        int next = BattleEquipmentSystem.GridToSlotIndex(nx, ny);
        if (next >= 0 && equipmentSystem.IsSlotUnlocked(next))
            padSelectedSlot = next;
    }

    private void HandlePadSubmit()
    {
        if (rewardFlow == null || equipmentSystem == null)
            return;

        if (rewardFlow.HasHand)
        {
            if (rewardFlow.ExchangeHandWithSlot(padSelectedSlot))
            {
                selectedRewardSlot = -1;
                padPickedSlot = -1;
                FlashSlot(padSelectedSlot);
            }
            return;
        }

        if (!HasItem(padSelectedSlot))
        {
            if (padPickedSlot >= 0 && equipmentSystem.IsSlotUnlocked(padSelectedSlot) &&
                equipmentSystem.SwapSlots(padPickedSlot, padSelectedSlot))
            {
                rewardFlow.SyncChosenRewardLocation();
                FlashSlot(padSelectedSlot);
                padPickedSlot = -1;
            }
            return;
        }

        if (padPickedSlot < 0)
        {
            padPickedSlot = padSelectedSlot;
            return;
        }

        if (padPickedSlot == padSelectedSlot)
        {
            padPickedSlot = -1;
            return;
        }

        if (equipmentSystem.SwapSlots(padPickedSlot, padSelectedSlot))
        {
            rewardFlow.SyncChosenRewardLocation();
            FlashSlot(padSelectedSlot);
            padPickedSlot = -1;
        }
    }

    private void HandlePadConfirmInput()
    {
        bool keyboardDirectionHeld = IsKeyboardDirectionalInputHeld();
        float x = keyboardDirectionHeld ? 0f : Input.GetAxisRaw("Horizontal");
        if (!padAxisLatched && Mathf.Abs(x) >= padAxisThreshold)
        {
            ActivatePadMode();
            confirmYesSelected = x < 0f;
            padAxisLatched = true;
            RefreshConfirmSelection();
        }
        else if (padAxisLatched && Mathf.Abs(x) <= padAxisReleaseThreshold)
        {
            padAxisLatched = false;
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton0))
        {
            ActivatePadMode();
            ResolveDiscard(confirmYesSelected);
        }
        else if (Input.GetKeyDown(KeyCode.JoystickButton1))
        {
            ActivatePadMode();
            ResolveDiscard(false);
        }
        else if (Input.GetKeyDown(KeyCode.Escape))
        {
            // KBM에서는 방향 선택 없이 Escape만 취소 shortcut으로 허용합니다.
            ActivateMouseMode();
            ResolveDiscard(false);
        }
    }

    private void RequestRewardCompletion()
    {
        if (BattlePauseController.IsPaused ||
            !IsRewardPackEditing ||
            rewardFlow == null ||
            !rewardFlow.CanComplete)
        {
            return;
        }

        rewardFlow.CompleteReward();
    }

    private int FindFirstUnlockedSlot()
    {
        if (equipmentSystem == null)
            return 0;
        for (int i = 0; i < SlotCount; i++)
            if (equipmentSystem.IsSlotUnlocked(i))
                return i;
        return 0;
    }

    private bool HasItem(int index)
    {
        return equipmentSystem != null && index >= 0 && index < equipmentSystem.Slots.Count &&
               equipmentSystem.IsSlotUnlocked(index) && equipmentSystem.Slots[index] != null &&
               equipmentSystem.Slots[index].equipment != null;
    }

    private void FlashSlot(int slotIndex)
    {
        flashedSlot = slotIndex;
        flashUntil = Time.unscaledTime + 0.32f;
    }

    private void UpdateSelectionFrames()
    {
        bool reward = IsRewardPackEditing;
        float pulse01 = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);

        for (int i = 0; i < SlotCount; i++)
        {
            bool flashed = i == flashedSlot && Time.unscaledTime < flashUntil;
            bool picked = reward && i == padPickedSlot;
            bool padSelected = reward && padModeActive && i == padSelectedSlot;
            bool mouseSelected = reward && !padModeActive && i == selectedRewardSlot;
            bool hover = reward && !padModeActive && i == hoveredSlot;
            bool active = flashed || picked || padSelected || mouseSelected || hover;

            ApplySelectionStroke(
                miniSelectionFrames[i],
                miniSelectionOutlines[i],
                active,
                picked,
                padSelected || mouseSelected,
                hover,
                flashed,
                pulse01);

            ApplySelectionStroke(
                fullSelectionFrames[i],
                fullSelectionOutlines[i],
                active,
                picked,
                padSelected || mouseSelected,
                hover,
                flashed,
                pulse01);
        }

        if (flashedSlot >= 0 && Time.unscaledTime >= flashUntil)
            flashedSlot = -1;
    }

    private void ApplySelectionStroke(
        GameObject frame,
        Outline outline,
        bool active,
        bool picked,
        bool selected,
        bool hover,
        bool flashed,
        float pulse01)
    {
        if (frame == null)
            return;

        if (frame.activeSelf != active)
            frame.SetActive(active);
        if (!active || outline == null)
            return;

        Color color;
        float distance;

        if (picked)
        {
            color = accentPink;
            distance = Mathf.Lerp(6.5f, 8f, pulse01);
        }
        else if (selected)
        {
            color = accentYellow;
            color.a = Mathf.Lerp(0.72f, 1f, pulse01);
            distance = Mathf.Lerp(4.8f, 6.2f, pulse01);
        }
        else if (flashed)
        {
            color = accentCyan;
            distance = 6f;
        }
        else
        {
            color = hover ? accentCyan : accentYellow;
            color.a = hover ? 0.88f : 0.82f;
            distance = hover ? 4.2f : 4.8f;
        }

        outline.useGraphicAlpha = false;
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
    }

    private void UpdateTrashVisibility()
    {
        if (trashRoot == null)
            return;

        bool visible = !discardModalOpen && (IsRewardPackEditing || draggingSlot >= 0);
        if (trashRoot.gameObject.activeSelf != visible)
            trashRoot.gameObject.SetActive(visible);
    }

    private void UpdateRewardDoneState()
    {
        bool show = IsRewardPackEditing && !discardModalOpen;
        bool canAdvance =
            show &&
            !BattlePauseController.IsPaused &&
            rewardFlow != null &&
            rewardFlow.CanComplete;

        if (doneRoot != null && doneRoot.gameObject.activeSelf != show)
            doneRoot.gameObject.SetActive(show);
        if (doneButton != null)
            doneButton.interactable = canAdvance;
    }

    private void UpdateRewardHandVisual()
    {
        if (handGhostRoot == null)
            return;

        bool visible = IsRewardPackEditing && rewardFlow != null && rewardFlow.HasHand;
        if (!visible)
        {
            if (handGhostRoot.gameObject.activeSelf)
                handGhostRoot.gameObject.SetActive(false);
            return;
        }

        BattleEquipmentStack hand = rewardFlow.Hand;
        if (!handGhostRoot.gameObject.activeSelf)
            handGhostRoot.gameObject.SetActive(true);

        if (handGhostIcon != null)
        {
            handGhostIcon.sprite = hand.equipment != null ? hand.equipment.icon : null;
            handGhostIcon.enabled = hand.equipment != null && hand.equipment.icon != null;
        }

        if (padModeActive)
        {
            int index = Mathf.Clamp(padSelectedSlot, 0, SlotCount - 1);
            RectTransform slot = FindRect($"GridSlot_{index}") ?? FindRect($"BackpackCell_{index}");
            if (slot != null)
                handGhostRoot.position = slot.position + (Vector3)handPadOffset;
        }
        else if (Input.mousePresent)
        {
            handGhostRoot.position = Input.mousePosition + (Vector3)handMouseOffset;
        }

        handGhostRoot.SetAsLastSibling();
    }

    private void EnsureOverlayCanvas()
    {
        if (interactionCanvas != null)
            return;

        EnsureEventSystem();

        GameObject canvasObject = new("BattleInventoryInteractionCanvas");
        canvasObject.transform.SetParent(transform, false);
        interactionCanvas = canvasObject.AddComponent<Canvas>();
        interactionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        interactionCanvas.overrideSorting = true;
        interactionCanvas.sortingOrder = OverlaySortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        interactionRoot = CreateRect(canvasObject.transform, "InteractionRoot", Vector2.zero);
        Stretch(interactionRoot);

        BuildDragGhost();
        BuildRewardHandGhost();
        BuildRewardTransferVisuals();
        BuildTrash();
        BuildRewardDoneUi();
        BuildDiscardConfirm();
    }

    private void BuildDragGhost()
    {
        dragGhostRoot = CreateRect(interactionRoot, "InventoryDragGhost", new Vector2(106f, 106f));
        Image back = dragGhostRoot.gameObject.AddComponent<Image>();
        back.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.94f);
        back.raycastTarget = false;
        Outline outline = dragGhostRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = accentYellow;
        outline.effectDistance = new Vector2(5f, -5f);

        AddNonBlockingCanvas(dragGhostRoot.gameObject, 2200);
        dragGhostIcon = CreateImage(dragGhostRoot, "Icon", new Vector2(84f, 84f));
        Center(dragGhostIcon.rectTransform);
        dragGhostIcon.raycastTarget = false;
        dragGhostRoot.gameObject.SetActive(false);
    }

    private void BuildRewardHandGhost()
    {
        handGhostRoot = CreateRect(interactionRoot, "RewardHandGhost", new Vector2(112f, 112f));
        Image back = handGhostRoot.gameObject.AddComponent<Image>();
        back.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.96f);
        back.raycastTarget = false;
        Outline outline = handGhostRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = accentPink;
        outline.effectDistance = new Vector2(6f, -6f);

        AddNonBlockingCanvas(handGhostRoot.gameObject, 2210);
        handGhostIcon = CreateImage(handGhostRoot, "Icon", new Vector2(88f, 88f));
        Center(handGhostIcon.rectTransform);
        handGhostIcon.raycastTarget = false;
        handGhostRoot.gameObject.SetActive(false);
    }

    private void BuildRewardTransferVisuals()
    {
        transferGhostRoot = CreateRect(interactionRoot, "RewardTransferGhost", new Vector2(112f, 112f));
        Image back = transferGhostRoot.gameObject.AddComponent<Image>();
        back.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.94f);
        back.raycastTarget = false;

        transferGhostOutline = transferGhostRoot.gameObject.AddComponent<Outline>();
        transferGhostOutline.effectColor = accentYellow;
        transferGhostOutline.effectDistance = new Vector2(6f, -6f);

        AddNonBlockingCanvas(transferGhostRoot.gameObject, 2240);
        transferGhostIcon = CreateImage(transferGhostRoot, "Icon", new Vector2(88f, 88f));
        Center(transferGhostIcon.rectTransform);
        transferGhostIcon.raycastTarget = false;
        transferGhostRoot.gameObject.SetActive(false);

        for (int i = 0; i < transferTrailRoots.Length; i++)
        {
            RectTransform trail = CreateRect(
                interactionRoot,
                $"RewardTransferAfterimage_{i + 1}",
                new Vector2(96f, 96f));

            Image icon = trail.gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            AddNonBlockingCanvas(trail.gameObject, 2230 - i);
            trail.gameObject.SetActive(false);

            transferTrailRoots[i] = trail;
            transferTrailIcons[i] = icon;
        }

        transferImpactRoot = CreateRect(interactionRoot, "RewardTransferImpact", new Vector2(128f, 128f));
        Image impactBack = transferImpactRoot.gameObject.AddComponent<Image>();
        impactBack.color = Color.clear;
        impactBack.raycastTarget = false;

        Outline impactOutline = transferImpactRoot.gameObject.AddComponent<Outline>();
        impactOutline.effectColor = accentCyan;
        impactOutline.effectDistance = new Vector2(8f, -8f);

        transferImpactGroup = transferImpactRoot.gameObject.AddComponent<CanvasGroup>();
        transferImpactGroup.blocksRaycasts = false;
        transferImpactGroup.interactable = false;
        AddNonBlockingCanvas(transferImpactRoot.gameObject, 2250);
        transferImpactRoot.gameObject.SetActive(false);
    }

    private static void AddNonBlockingCanvas(GameObject owner, int order)
    {
        Canvas canvas = owner.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = order;
        CanvasGroup group = owner.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private void BuildTrash()
    {
        trashRoot = CreateRect(interactionRoot, "InventoryTrash", new Vector2(184f, 72f));
        Image back = trashRoot.gameObject.AddComponent<Image>();
        back.color = inkColor;
        back.raycastTarget = true;
        trashBack = back;

        Outline outline = trashRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = accentPink;
        outline.effectDistance = new Vector2(4f, -4f);

        trashLabel = CreateText(trashRoot, "×  TRASH", 17, FontStyle.Bold, TextAnchor.MiddleCenter, accentPink);
        Stretch(trashLabel.rectTransform);

        BattleInventoryTrashDropTarget target = trashRoot.gameObject.AddComponent<BattleInventoryTrashDropTarget>();
        target.Configure(this);
        trashRoot.gameObject.SetActive(false);
    }

    private void BuildRewardDoneUi()
    {
        doneRoot = CreateRect(interactionRoot, "RewardPackDone", new Vector2(184f, 64f));
        Image back = doneRoot.gameObject.AddComponent<Image>();
        back.color = accentCyan;
        back.raycastTarget = true;
        Outline outline = doneRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = inkColor;
        outline.effectDistance = new Vector2(5f, -5f);

        Text label = CreateText(doneRoot, "DONE  /  START", 15, FontStyle.Bold, TextAnchor.MiddleCenter, inkColor);
        Stretch(label.rectTransform);
        doneButton = doneRoot.gameObject.AddComponent<Button>();
        doneButton.targetGraphic = back;
        doneButton.onClick.AddListener(RequestRewardCompletion);
        doneRoot.gameObject.SetActive(false);
    }

    private void BuildDiscardConfirm()
    {
        discardConfirmRoot = CreateRect(interactionRoot, "InventoryDiscardConfirm", Vector2.zero);
        Stretch(discardConfirmRoot);
        Image dim = discardConfirmRoot.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;

        RectTransform panel = CreateRect(discardConfirmRoot, "ConfirmPanel", new Vector2(620f, 330f));
        Center(panel);
        Image panelBack = panel.gameObject.AddComponent<Image>();
        panelBack.color = inkColor;
        Outline panelOutline = panel.gameObject.AddComponent<Outline>();
        panelOutline.effectColor = accentPink;
        panelOutline.effectDistance = new Vector2(8f, -8f);

        discardConfirmText = CreateText(panel, "이 아이템을 삭제할까요?\n정말 삭제하시겠습니까?", 24, FontStyle.Bold, TextAnchor.MiddleCenter, paperColor);
        SetAnchors(discardConfirmText.rectTransform, new Vector2(0.08f, 0.38f), new Vector2(0.92f, 0.90f));

        RectTransform yes = CreateRect(panel, "ConfirmYes", new Vector2(190f, 66f));
        yes.anchorMin = yes.anchorMax = new Vector2(0.32f, 0.17f);
        confirmYesBack = yes.gameObject.AddComponent<Image>();
        confirmYesBack.raycastTarget = true;
        confirmYesText = CreateText(yes, "예", 22, FontStyle.Bold, TextAnchor.MiddleCenter, inkColor);
        Stretch(confirmYesText.rectTransform);
        Button yesButton = yes.gameObject.AddComponent<Button>();
        yesButton.targetGraphic = confirmYesBack;
        yesButton.onClick.AddListener(() => ResolveDiscard(true));

        RectTransform no = CreateRect(panel, "ConfirmNo", new Vector2(190f, 66f));
        no.anchorMin = no.anchorMax = new Vector2(0.68f, 0.17f);
        confirmNoBack = no.gameObject.AddComponent<Image>();
        confirmNoBack.raycastTarget = true;
        confirmNoText = CreateText(no, "아니요", 22, FontStyle.Bold, TextAnchor.MiddleCenter, inkColor);
        Stretch(confirmNoText.rectTransform);
        Button noButton = no.gameObject.AddComponent<Button>();
        noButton.targetGraphic = confirmNoBack;
        noButton.onClick.AddListener(() => ResolveDiscard(false));

        discardConfirmRoot.gameObject.SetActive(false);
        RefreshConfirmSelection();
    }

    private void RefreshConfirmSelection()
    {
        if (confirmYesBack != null)
            confirmYesBack.color = confirmYesSelected ? accentYellow : new Color(paperColor.r, paperColor.g, paperColor.b, 0.48f);
        if (confirmNoBack != null)
            confirmNoBack.color = !confirmYesSelected ? accentCyan : new Color(paperColor.r, paperColor.g, paperColor.b, 0.48f);
        if (confirmYesText != null)
            confirmYesText.color = inkColor;
        if (confirmNoText != null)
            confirmNoText.color = inkColor;
    }

    public void PlayRewardSelectFeedback()
    {
        PlayRewardFeedback(rewardSelectClip, RewardFeedbackTone.Select);
    }

    public bool PlayRewardTransfer(
        RectTransform sourceRect,
        BattleEquipmentSO equipment,
        int targetSlot,
        bool toHand,
        EquipmentRarity rarity,
        Action onArrive,
        Action onComplete)
    {
        EnsureOverlayCanvas();
        if (sourceRect == null || equipment == null || transferGhostRoot == null)
            return false;

        if (rewardTransferRoutine != null)
            StopCoroutine(rewardTransferRoutine);

        Vector2 startScreen = RectToScreenPoint(sourceRect);
        rewardTransferRoutine = StartCoroutine(
            RewardTransferRoutine(
                startScreen,
                equipment,
                targetSlot,
                toHand,
                rarity,
                onArrive,
                onComplete));
        return true;
    }

    private IEnumerator RewardTransferRoutine(
        Vector2 startScreen,
        BattleEquipmentSO equipment,
        int targetSlot,
        bool toHand,
        EquipmentRarity rarity,
        Action onArrive,
        Action onComplete)
    {
        if (transferGhostIcon != null)
        {
            transferGhostIcon.sprite = equipment.icon;
            transferGhostIcon.enabled = equipment.icon != null;
        }

        if (transferGhostOutline != null)
        {
            transferGhostOutline.effectColor =
                (int)rarity >= (int)EquipmentRarity.Epic
                    ? accentPink
                    : accentYellow;
        }

        transferGhostRoot.position = startScreen;
        transferGhostRoot.localScale = Vector3.one * 1.10f;
        transferGhostRoot.gameObject.SetActive(true);
        SetTransferTrailsVisible(false);

        // Transferring phase가 PACK layout을 연 뒤 실제 RectTransform 위치를 한 프레임 기다려 읽습니다.
        yield return null;

        Vector2 endScreen = ResolveRewardTransferDestination(targetSlot, toHand);
        float hold = rarity == EquipmentRarity.Epic
            ? 0.08f
            : rarity == EquipmentRarity.Unique
                ? 0.11f
                : 0f;

        if (hold > 0f)
            yield return WaitUnscaled(hold);

        PlayRewardFeedback(rewardTransferClip, RewardFeedbackTone.Transfer);

        float duration = Mathf.Max(0.05f, rewardTransferDuration);
        float elapsed = 0f;
        bool useTrails = (int)rarity >= (int)EquipmentRarity.Rare;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            Vector2 position = Vector2.Lerp(startScreen, endScreen, eased);
            position.y += Mathf.Sin(eased * Mathf.PI) * rewardTransferArcHeight;
            transferGhostRoot.position = position;

            float iconScale = t < 0.82f
                ? Mathf.Lerp(1.10f, 1.00f, t / 0.82f)
                : Mathf.Lerp(1.00f, 0.94f, (t - 0.82f) / 0.18f);
            transferGhostRoot.localScale = Vector3.one * iconScale;

            if (useTrails)
                UpdateTransferTrails(startScreen, endScreen, eased, equipment.icon);

            yield return null;
        }

        transferGhostRoot.position = endScreen;
        transferGhostRoot.localScale = Vector3.one * 0.94f;
        SetTransferTrailsVisible(false);

        // 데이터는 아이콘이 목적지에 닿는 정확한 시점에 Commit합니다.
        onArrive?.Invoke();

        PlayRewardFeedback(rewardInstallClip, RewardFeedbackTone.Install);
        yield return PlayTransferImpact(endScreen, rarity);

        transferGhostRoot.localScale = Vector3.one;
        transferGhostRoot.gameObject.SetActive(false);
        rewardTransferRoutine = null;

        // Impact까지 끝난 뒤에만 PackEditing으로 넘어가 입력을 풉니다.
        onComplete?.Invoke();
    }

    private IEnumerator PlayTransferImpact(Vector2 position, EquipmentRarity rarity)
    {
        if (transferImpactRoot == null || transferImpactGroup == null)
            yield break;

        transferImpactRoot.position = position;
        transferImpactRoot.localScale = Vector3.one;
        transferImpactGroup.alpha = 1f;
        transferImpactRoot.gameObject.SetActive(true);

        float strength = rarity == EquipmentRarity.Unique ? 1.12f : 1f;
        const float duration = 0.16f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            float scale;
            if (t < 0.28f)
                scale = Mathf.Lerp(1f, 0.92f, t / 0.28f);
            else if (t < 0.62f)
                scale = Mathf.Lerp(0.92f, 1.06f * strength, (t - 0.28f) / 0.34f);
            else
                scale = Mathf.Lerp(1.06f * strength, 1f, (t - 0.62f) / 0.38f);

            transferImpactRoot.localScale = Vector3.one * scale;
            transferGhostRoot.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, Mathf.SmoothStep(0f, 1f, t));
            transferImpactGroup.alpha = t < 0.62f
                ? 1f
                : 1f - Mathf.Clamp01((t - 0.62f) / 0.38f);

            yield return null;
        }

        transferImpactRoot.localScale = Vector3.one;
        transferImpactRoot.gameObject.SetActive(false);
    }

    private void UpdateTransferTrails(Vector2 start, Vector2 end, float currentT, Sprite sprite)
    {
        for (int i = 0; i < transferTrailRoots.Length; i++)
        {
            RectTransform root = transferTrailRoots[i];
            Image icon = transferTrailIcons[i];
            if (root == null || icon == null)
                continue;

            float lagT = Mathf.Clamp01(currentT - 0.07f * (i + 1));
            if (lagT <= 0f)
            {
                root.gameObject.SetActive(false);
                continue;
            }

            Vector2 position = Vector2.Lerp(start, end, lagT);
            position.y += Mathf.Sin(lagT * Mathf.PI) * rewardTransferArcHeight;
            root.position = position;
            root.localScale = Vector3.one * Mathf.Lerp(0.96f, 0.78f, i / 2f);

            icon.sprite = sprite;
            icon.enabled = sprite != null;
            Color color = Color.white;
            color.a = 0.24f - i * 0.055f;
            icon.color = color;
            root.gameObject.SetActive(true);
        }
    }

    private void SetTransferTrailsVisible(bool visible)
    {
        for (int i = 0; i < transferTrailRoots.Length; i++)
        {
            if (transferTrailRoots[i] != null)
                transferTrailRoots[i].gameObject.SetActive(visible);
        }
    }

    private Vector2 ResolveRewardTransferDestination(int targetSlot, bool toHand)
    {
        RectTransform slot = targetSlot >= 0
            ? FindRect($"RewardLoadoutSlot_{targetSlot + 1}") ??
              FindRect($"GridSlot_{targetSlot}") ??
              FindRect($"BackpackCell_{targetSlot}")
            : null;

        if (slot == null)
        {
            int fallback = FindFirstUnlockedSlot();
            slot = FindRect($"RewardLoadoutSlot_{fallback + 1}") ??
                   FindRect($"GridSlot_{fallback}") ??
                   FindRect($"BackpackCell_{fallback}");
        }

        Vector2 basePosition = slot != null
            ? RectToScreenPoint(slot)
            : new Vector2(Screen.width * 0.66f, Screen.height * 0.54f);

        return toHand
            ? basePosition + new Vector2(132f, -28f)
            : basePosition;
    }

    private static Vector2 RectToScreenPoint(RectTransform rect)
    {
        if (rect == null)
            return Vector2.zero;

        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        Vector3 worldCenter = rect.TransformPoint(rect.rect.center);
        return RectTransformUtility.WorldToScreenPoint(camera, worldCenter);
    }

    private static IEnumerator WaitUnscaled(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private enum RewardFeedbackTone
    {
        Select,
        Transfer,
        Install
    }

    private void PlayRewardFeedback(AudioClip assignedClip, RewardFeedbackTone tone)
    {
        EnsureRewardFeedbackAudio();
        if (rewardFeedbackAudio == null)
            return;

        AudioClip clip = assignedClip ?? GetFallbackRewardClip(tone);
        if (clip != null)
            rewardFeedbackAudio.PlayOneShot(clip);
    }

    private void EnsureRewardFeedbackAudio()
    {
        if (rewardFeedbackAudio != null)
            return;

        rewardFeedbackAudio = gameObject.GetComponent<AudioSource>();
        if (rewardFeedbackAudio == null)
            rewardFeedbackAudio = gameObject.AddComponent<AudioSource>();

        rewardFeedbackAudio.playOnAwake = false;
        rewardFeedbackAudio.loop = false;
        rewardFeedbackAudio.spatialBlend = 0f;
        rewardFeedbackAudio.volume = 0.28f;
    }

    private AudioClip GetFallbackRewardClip(RewardFeedbackTone tone)
    {
        switch (tone)
        {
            case RewardFeedbackTone.Select:
                return fallbackSelectClip ??=
                    CreateRewardFeedbackTone("RewardSelectTick", 0.035f, 1180f, 920f, 0.02f);
            case RewardFeedbackTone.Transfer:
                return fallbackTransferClip ??=
                    CreateRewardFeedbackTone("RewardTransferSwish", 0.14f, 520f, 1180f, 0.08f);
            default:
                return fallbackInstallClip ??=
                    CreateRewardFeedbackTone("RewardInstallClick", 0.075f, 210f, 145f, 0.03f);
        }
    }

    private static AudioClip CreateRewardFeedbackTone(
        string clipName,
        float duration,
        float startHz,
        float endHz,
        float noiseAmount)
    {
        const int sampleRate = 22050;
        int sampleCount = Mathf.Max(1, Mathf.RoundToInt(duration * sampleRate));
        float[] samples = new float[sampleCount];
        uint noiseState = 0x12345678u;
        float phase = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = sampleCount > 1 ? i / (float)(sampleCount - 1) : 1f;
            float hz = Mathf.Lerp(startHz, endHz, t);
            phase += Mathf.PI * 2f * hz / sampleRate;

            noiseState = noiseState * 1664525u + 1013904223u;
            float noise = ((noiseState >> 8) / 16777215f) * 2f - 1f;
            float envelope = Mathf.Pow(1f - t, 2f);

            samples[i] =
                (Mathf.Sin(phase) * (1f - noiseAmount) + noise * noiseAmount) *
                envelope *
                0.34f;
        }

        AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void HideTransientVisuals()
    {
        draggingSlot = -1;
        if (dragGhostRoot != null)
            dragGhostRoot.gameObject.SetActive(false);
        if (handGhostRoot != null)
            handGhostRoot.gameObject.SetActive(false);
        if (transferGhostRoot != null)
            transferGhostRoot.gameObject.SetActive(false);
        SetTransferTrailsVisible(false);
        if (transferImpactRoot != null)
            transferImpactRoot.gameObject.SetActive(false);
        if (trashRoot != null)
            trashRoot.gameObject.SetActive(false);
        if (doneRoot != null)
            doneRoot.gameObject.SetActive(false);
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = UnityEngine.Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }
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
        Image image = rect.gameObject.AddComponent<Image>();
        image.preserveAspect = true;
        return image;
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

    private static void Center(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject go = new("BattleInventoryEventSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }
}

internal sealed class BattleInventorySlotPointer : MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler,
    IDropHandler,
    IPointerClickHandler,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private BattleInventoryInteractionController owner;
    private int slotIndex;
    private BattleInventorySurface surface;
    private bool dragging;

    public int SlotIndex => slotIndex;
    public bool IsDragging => dragging;

    public void Configure(BattleInventoryInteractionController controller, int index, BattleInventorySurface slotSurface)
    {
        owner = controller;
        slotIndex = index;
        surface = slotSurface;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = owner != null && owner.BeginSlotDrag(slotIndex, surface, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragging)
            owner?.UpdateSlotDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (dragging)
            owner?.EndSlotDrag();
        dragging = false;
    }

    public void OnDrop(PointerEventData eventData)
    {
        owner?.HandleSlotDrop(slotIndex, eventData);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        owner?.HandleSlotClick(slotIndex, surface, eventData.button);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        owner?.HandleSlotHover(slotIndex, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.HandleSlotHover(slotIndex, false);
    }
}

internal sealed class BattleInventoryPackDropTarget : MonoBehaviour, IDropHandler
{
    private BattleInventoryInteractionController owner;
    public void Configure(BattleInventoryInteractionController controller) => owner = controller;
    public void OnDrop(PointerEventData eventData) => owner?.HandlePackDrop(eventData);
}

internal sealed class BattleInventoryTrashDropTarget : MonoBehaviour,
    IDropHandler,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    private BattleInventoryInteractionController owner;
    public void Configure(BattleInventoryInteractionController controller) => owner = controller;
    public void OnDrop(PointerEventData eventData) => owner?.HandleTrashDrop(eventData);
    public void OnPointerEnter(PointerEventData eventData) => owner?.HandleTrashHover(true);
    public void OnPointerExit(PointerEventData eventData) => owner?.HandleTrashHover(false);
    public void OnPointerClick(PointerEventData eventData) => owner?.HandleTrashClick(eventData.button);
}

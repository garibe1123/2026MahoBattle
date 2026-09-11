using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

internal enum BattleInventorySurface
{
    MiniPack,
    ExpandedGrid,
    RewardPack
}

/// <summary>
/// 3x3 PACK의 입력과 입력에 직접 종속된 임시 시각 상태를 한 곳에서 관리합니다.
///
/// 책임:
/// - Combat 장비 슬롯 클릭/교환
/// - Reward PACK 슬롯 선택/교환/Drag
/// - Reward Hand와 슬롯 교환 입력
/// - Reward Hand Ghost 표시
/// - TRASH 및 삭제 확인 입력
/// - 패드 PACK 커서/선택
///
/// Reward의 business state는 BattleRewardFlow가 단독 소유합니다.
/// 이 클래스는 rewardStaged / stagedReward 같은 복제 상태를 만들지 않습니다.
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

    private Canvas interactionCanvas;
    private RectTransform interactionRoot;
    private RectTransform dragGhostRoot;
    private Image dragGhostIcon;
    private RectTransform handGhostRoot;
    private Image handGhostIcon;
    private RectTransform trashRoot;
    private Image trashBack;
    private Text trashLabel;

    private RectTransform doneRoot;
    private Button doneButton;
    private Text doneLabel;

    private RectTransform discardConfirmRoot;
    private Text discardConfirmText;
    private Image confirmYesBack;
    private Image confirmNoBack;
    private Text confirmYesText;
    private Text confirmNoText;

    private RectTransform miniPackRoot;
    private CanvasGroup miniPackGroup;
    private readonly GameObject[] miniSelectionFrames = new GameObject[SlotCount];
    private readonly Outline[] miniSelectionOutlines = new Outline[SlotCount];

    // Input/presentation state only. Reward business state는 BattleRewardFlow가 소유합니다.
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
    public bool IsRewardPackEditing => IsReward() && rewardFlow != null && rewardFlow.Phase == BattleRewardPhase.PackEditing;
    public int SelectedRewardSlot => selectedRewardSlot;
    public int HoveredSlot => hoveredSlot;
    public int PadSelectedSlot => padSelectedSlot;
    public int PadPickedSlot => padPickedSlot;
    public bool PadModeActive => padModeActive;

    /// <summary>
    /// DONE/NEXT는 입력 계층이 요청만 발생시키고, Run 완료 처리는 Reward adapter가 담당합니다.
    /// BattleRunManager의 구형 private 완료 API와의 bridge가 제거되면 BattleRewardFlow가 직접 처리하게 됩니다.
    /// </summary>
    public event Action RewardDoneRequested;

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
        HideDragVisuals();
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

        BattleRewardPhase currentRewardPhase = rewardFlow != null
            ? rewardFlow.Phase
            : BattleRewardPhase.Inactive;
        if (lastRewardPhase != currentRewardPhase)
        {
            HandleRewardPhaseChanged(currentRewardPhase);
            lastRewardPhase = currentRewardPhase;
        }

        if (IsReward())
            ShowPackForReward();

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
        UpdateRewardEditUi();
        UpdateRewardHandVisual();
    }

    private void HandleRunStateChanged(BattleRunState state)
    {
        selectedRewardSlot = -1;
        hoveredSlot = -1;
        draggingSlot = -1;
        padPickedSlot = -1;
        padModeActive = false;
        padAxisLatched = false;
        flashedSlot = -1;
        HideDiscardConfirm();

        if (state == BattleRunState.Reward)
            padSelectedSlot = FindFirstUnlockedSlot();
        else if (handGhostRoot != null)
            handGhostRoot.gameObject.SetActive(false);
    }

    private void HandleRewardPhaseChanged(BattleRewardPhase phase)
    {
        selectedRewardSlot = -1;
        hoveredSlot = -1;
        padPickedSlot = -1;
        padModeActive = false;
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

        padSelectedSlot = Mathf.Max(0, focus);
        if (!rewardFlow.HasHand && rewardFlow.ChosenRewardCommitted)
            selectedRewardSlot = rewardFlow.ChosenRewardSlot;
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

    private void ResolveMiniPack()
    {
        if (miniPackRoot == null)
            miniPackRoot = FindRect("BackpackMiniGrid");
        if (miniPackRoot == null)
            return;

        if (miniPackGroup == null)
            miniPackGroup = miniPackRoot.GetComponent<CanvasGroup>();

        Canvas canvas = miniPackRoot.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        Image packImage = miniPackRoot.GetComponent<Image>();
        if (packImage != null)
            packImage.raycastTarget = IsRewardPackEditing;

        // 구형 Reward card -> PACK 직접 Drop은 더 이상 사용하지 않습니다.
        BattleInventoryPackDropTarget packDrop = miniPackRoot.GetComponent<BattleInventoryPackDropTarget>();
        if (packDrop != null)
            packDrop.enabled = false;
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
    }

    private void EnsureMiniSelectionFrame(RectTransform slot, int index)
    {
        if (index < 0 || index >= SlotCount || miniSelectionFrames[index] != null)
            return;

        Transform existing = slot.Find("InteractionSelectionFrame");
        RectTransform frame;
        if (existing is RectTransform existingRect)
        {
            frame = existingRect;
        }
        else
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

        miniSelectionFrames[index] = frame.gameObject;
        miniSelectionOutlines[index] = outline;
        frame.gameObject.SetActive(false);
    }

    internal bool BeginSlotDrag(int slotIndex, BattleInventorySurface surface, PointerEventData eventData)
    {
        if (discardModalOpen || equipmentSystem == null || !HasItem(slotIndex))
            return false;

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
        EnsureOverlayCanvas();
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
        if (draggingSlot < 0 || dragGhostRoot == null || eventData == null)
            return;
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
        if (discardModalOpen || equipmentSystem == null || eventData == null || !equipmentSystem.IsSlotUnlocked(targetIndex))
            return;

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

        if (equipmentSystem.SwapSlots(sourceIndex, targetIndex))
        {
            selectedRewardSlot = IsRewardPackEditing ? targetIndex : -1;
            padSelectedSlot = targetIndex;
            padPickedSlot = -1;
            rewardFlow?.SyncChosenRewardLocation();
            FlashSlot(targetIndex);
        }
    }

    /// <summary>
    /// Legacy RewardInventoryPackDropTarget 호환용 no-op입니다.
    /// Reward 후보 카드는 클릭 -> 결정 흐름이므로 PACK 배경에 직접 Drop하지 않습니다.
    /// </summary>
    internal void HandlePackDrop(PointerEventData eventData)
    {
    }

    internal void HandleSlotClick(int slotIndex, BattleInventorySurface surface, PointerEventData.InputButton button)
    {
        if (button != PointerEventData.InputButton.Left || discardModalOpen || equipmentSystem == null ||
            !equipmentSystem.IsSlotUnlocked(slotIndex))
            return;

        if (IsReward())
        {
            if (!IsRewardPackEditing || rewardFlow == null)
                return;

            padSelectedSlot = slotIndex;

            if (rewardFlow.HasHand)
            {
                if (rewardFlow.ExchangeHandWithSlot(slotIndex))
                {
                    selectedRewardSlot = -1;
                    padPickedSlot = -1;
                    FlashSlot(slotIndex);
                }
                return;
            }

            if (selectedRewardSlot < 0)
            {
                if (HasItem(slotIndex))
                    selectedRewardSlot = slotIndex;
                return;
            }

            if (selectedRewardSlot == slotIndex)
            {
                selectedRewardSlot = -1;
                return;
            }

            if (equipmentSystem.SwapSlots(selectedRewardSlot, slotIndex))
            {
                selectedRewardSlot = -1;
                padPickedSlot = -1;
                rewardFlow.SyncChosenRewardLocation();
                FlashSlot(slotIndex);
            }
            return;
        }

        if (IsCombat())
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
        hoveredSlot = entered ? slotIndex : (hoveredSlot == slotIndex ? -1 : hoveredSlot);
    }

    internal void HandleTrashDrop(PointerEventData eventData)
    {
        if (discardModalOpen || equipmentSystem == null || eventData == null)
            return;

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

        if (rewardFlow.HasHand)
        {
            if (rewardFlow.DiscardHand())
            {
                selectedRewardSlot = -1;
                padPickedSlot = -1;
            }
            return;
        }

        int target = selectedRewardSlot >= 0 ? selectedRewardSlot : padSelectedSlot;
        if (HasItem(target))
            RequestDiscard(target);
    }

    internal void HandleTrashHover(bool hovered)
    {
        if (trashBack == null)
            return;
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

        int dx = 0;
        int dy = 0;

        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) dx = -1;
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) dx = 1;
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dy = -1;
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dy = 1;

        float axisX = Input.GetAxisRaw("Horizontal");
        float axisY = Input.GetAxisRaw("Vertical");
        float magnitude = Mathf.Max(Mathf.Abs(axisX), Mathf.Abs(axisY));

        if (!padAxisLatched && magnitude >= padAxisThreshold)
        {
            if (Mathf.Abs(axisX) >= Mathf.Abs(axisY))
                dx = axisX > 0f ? 1 : -1;
            else
                dy = axisY > 0f ? -1 : 1;
            padAxisLatched = true;
            padModeActive = true;
        }
        else if (padAxisLatched && magnitude <= padAxisReleaseThreshold)
        {
            padAxisLatched = false;
        }

        if (dx != 0 || dy != 0)
        {
            MovePadSelection(dx, dy);
            padModeActive = true;
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
        {
            padModeActive = true;
            HandlePadSubmit();
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton1))
        {
            padPickedSlot = -1;
            selectedRewardSlot = -1;
            padModeActive = true;
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton3))
        {
            padModeActive = true;
            if (rewardFlow.HasHand)
                rewardFlow.DiscardHand();
            else if (HasItem(padSelectedSlot))
                RequestDiscard(padSelectedSlot);
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton7))
            RequestRewardCompletion();
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
            if (padPickedSlot >= 0 && equipmentSystem.IsSlotUnlocked(padSelectedSlot))
            {
                if (equipmentSystem.SwapSlots(padPickedSlot, padSelectedSlot))
                {
                    rewardFlow.SyncChosenRewardLocation();
                    FlashSlot(padSelectedSlot);
                    padPickedSlot = -1;
                }
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
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) ||
            Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.D))
        {
            confirmYesSelected = !confirmYesSelected;
            RefreshConfirmSelection();
        }

        float x = Input.GetAxisRaw("Horizontal");
        if (!padAxisLatched && Mathf.Abs(x) >= padAxisThreshold)
        {
            confirmYesSelected = x < 0f;
            padAxisLatched = true;
            RefreshConfirmSelection();
        }
        else if (padAxisLatched && Mathf.Abs(x) <= padAxisReleaseThreshold)
        {
            padAxisLatched = false;
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            ResolveDiscard(confirmYesSelected);
        else if (Input.GetKeyDown(KeyCode.JoystickButton1) || Input.GetKeyDown(KeyCode.Escape))
            ResolveDiscard(false);
    }

    private void RequestRewardCompletion()
    {
        if (!IsRewardPackEditing || rewardFlow == null || !rewardFlow.CanComplete)
            return;

        RewardDoneRequested?.Invoke();
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
        for (int i = 0; i < SlotCount; i++)
        {
            GameObject frame = miniSelectionFrames[i];
            if (frame == null)
                continue;

            bool flashed = i == flashedSlot && Time.unscaledTime < flashUntil;
            bool picked = reward && i == padPickedSlot;
            bool padSelected = reward && padModeActive && i == padSelectedSlot;
            bool mouseSelected = reward && i == selectedRewardSlot;
            bool hover = reward && i == hoveredSlot;
            bool active = flashed || picked || padSelected || mouseSelected || hover;
            frame.SetActive(active);

            Outline outline = miniSelectionOutlines[i];
            if (outline != null)
            {
                outline.effectColor = picked ? accentPink : flashed ? accentCyan : padSelected ? accentYellow : hover ? accentCyan : accentYellow;
                outline.effectDistance = picked ? new Vector2(7f, -7f) : new Vector2(5f, -5f);
            }
        }

        if (flashedSlot >= 0 && Time.unscaledTime >= flashUntil)
            flashedSlot = -1;
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
        BuildTrash();
        BuildRewardDoneUi();
        BuildDiscardConfirm();
    }

    private void BuildDragGhost()
    {
        dragGhostRoot = CreateRect(interactionRoot, "InventoryDragGhost", new Vector2(106f, 106f));
        Image ghostBack = dragGhostRoot.gameObject.AddComponent<Image>();
        ghostBack.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.94f);
        ghostBack.raycastTarget = false;
        Outline outline = dragGhostRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = accentYellow;
        outline.effectDistance = new Vector2(5f, -5f);

        Canvas ghostCanvas = dragGhostRoot.gameObject.AddComponent<Canvas>();
        ghostCanvas.overrideSorting = true;
        ghostCanvas.sortingOrder = 2200;
        CanvasGroup ghostGroup = dragGhostRoot.gameObject.AddComponent<CanvasGroup>();
        ghostGroup.blocksRaycasts = false;
        ghostGroup.interactable = false;

        dragGhostIcon = CreateImage(dragGhostRoot, "Icon", new Vector2(84f, 84f));
        dragGhostIcon.rectTransform.anchorMin = dragGhostIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        dragGhostIcon.rectTransform.anchoredPosition = Vector2.zero;
        dragGhostIcon.raycastTarget = false;
        dragGhostRoot.gameObject.SetActive(false);
    }

    private void BuildRewardHandGhost()
    {
        handGhostRoot = CreateRect(interactionRoot, "RewardHandGhost", new Vector2(112f, 112f));
        Image ghostBack = handGhostRoot.gameObject.AddComponent<Image>();
        ghostBack.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.96f);
        ghostBack.raycastTarget = false;

        Outline outline = handGhostRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = accentPink;
        outline.effectDistance = new Vector2(6f, -6f);

        Canvas ghostCanvas = handGhostRoot.gameObject.AddComponent<Canvas>();
        ghostCanvas.overrideSorting = true;
        ghostCanvas.sortingOrder = 2210;
        CanvasGroup group = handGhostRoot.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        handGhostIcon = CreateImage(handGhostRoot, "Icon", new Vector2(88f, 88f));
        handGhostIcon.rectTransform.anchorMin = handGhostIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        handGhostIcon.rectTransform.anchoredPosition = Vector2.zero;
        handGhostIcon.raycastTarget = false;
        handGhostRoot.gameObject.SetActive(false);
    }

    private void BuildTrash()
    {
        trashRoot = CreateRect(interactionRoot, "InventoryTrash", new Vector2(184f, 72f));
        trashRoot.anchorMin = trashRoot.anchorMax = Vector2.zero;
        trashRoot.pivot = Vector2.zero;
        trashRoot.anchoredPosition = new Vector2(430f, 40f);
        trashRoot.localRotation = Quaternion.Euler(0f, 0f, -1.6f);

        trashBack = trashRoot.gameObject.AddComponent<Image>();
        trashBack.color = inkColor;
        trashBack.raycastTarget = true;
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
        doneRoot.anchorMin = doneRoot.anchorMax = Vector2.zero;
        doneRoot.pivot = Vector2.zero;
        Image back = doneRoot.gameObject.AddComponent<Image>();
        back.color = accentCyan;
        back.raycastTarget = true;
        Outline outline = doneRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = inkColor;
        outline.effectDistance = new Vector2(5f, -5f);

        doneLabel = CreateText(doneRoot, "DONE  /  START", 15, FontStyle.Bold, TextAnchor.MiddleCenter, inkColor);
        Stretch(doneLabel.rectTransform);
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
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        Image panelBack = panel.gameObject.AddComponent<Image>();
        panelBack.color = inkColor;
        Outline panelOutline = panel.gameObject.AddComponent<Outline>();
        panelOutline.effectColor = accentPink;
        panelOutline.effectDistance = new Vector2(8f, -8f);

        discardConfirmText = CreateText(panel, "이 아이템을 삭제할까요?\n정말 삭제하시겠습니까?", 24, FontStyle.Bold, TextAnchor.MiddleCenter, paperColor);
        SetAnchors(discardConfirmText.rectTransform, new Vector2(0.08f, 0.38f), new Vector2(0.92f, 0.90f));

        RectTransform yes = CreateRect(panel, "ConfirmYes", new Vector2(190f, 66f));
        yes.anchorMin = yes.anchorMax = new Vector2(0.32f, 0.17f);
        Image yesBack = yes.gameObject.AddComponent<Image>();
        yesBack.raycastTarget = true;
        confirmYesBack = yesBack;
        confirmYesText = CreateText(yes, "예", 22, FontStyle.Bold, TextAnchor.MiddleCenter, inkColor);
        Stretch(confirmYesText.rectTransform);
        Button yesButton = yes.gameObject.AddComponent<Button>();
        yesButton.targetGraphic = yesBack;
        yesButton.onClick.AddListener(() => ResolveDiscard(true));

        RectTransform no = CreateRect(panel, "ConfirmNo", new Vector2(190f, 66f));
        no.anchorMin = no.anchorMax = new Vector2(0.68f, 0.17f);
        Image noBack = no.gameObject.AddComponent<Image>();
        noBack.raycastTarget = true;
        confirmNoBack = noBack;
        confirmNoText = CreateText(no, "아니요", 22, FontStyle.Bold, TextAnchor.MiddleCenter, inkColor);
        Stretch(confirmNoText.rectTransform);
        Button noButton = no.gameObject.AddComponent<Button>();
        noButton.targetGraphic = noBack;
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

    private void ShowPackForReward()
    {
        ResolveMiniPack();
        if (miniPackGroup != null)
        {
            bool interactive = IsRewardPackEditing && !BattlePauseController.IsPaused && !discardModalOpen;
            miniPackGroup.alpha = 1f;
            miniPackGroup.blocksRaycasts = interactive;
            miniPackGroup.interactable = interactive;
        }

        RectTransform oldStrip = FindRect("RewardLoadoutStrip");
        if (oldStrip != null && oldStrip.gameObject.activeSelf)
            oldStrip.gameObject.SetActive(false);
    }

    private void UpdateTrashVisibility()
    {
        if (trashRoot == null)
            return;

        bool fullBoardOpen = false;
        RectTransform full = FindRect("LoadoutSwitchFull");
        if (full != null)
        {
            CanvasGroup fullGroup = full.GetComponent<CanvasGroup>();
            fullBoardOpen = fullGroup != null && fullGroup.alpha > 0.08f;
        }

        bool visible = !discardModalOpen &&
                       (IsRewardPackEditing || draggingSlot >= 0 || (IsCombat() && fullBoardOpen));
        if (trashRoot.gameObject.activeSelf != visible)
            trashRoot.gameObject.SetActive(visible);
    }

    private void UpdateRewardEditUi()
    {
        bool showDone = IsRewardPackEditing && !discardModalOpen;
        if (doneRoot != null)
        {
            doneRoot.gameObject.SetActive(showDone);
            PositionRewardEditControls(doneRoot, new Vector2(0f, 92f));
        }

        if (doneButton != null)
            doneButton.interactable = showDone && rewardFlow != null && rewardFlow.CanComplete;

        if (trashRoot != null && IsRewardPackEditing)
            PositionRewardEditControls(trashRoot, Vector2.zero);
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

    private void PositionRewardEditControls(RectTransform target, Vector2 extraOffset)
    {
        if (target == null || miniPackRoot == null)
            return;

        float scale = Mathf.Max(1f, miniPackRoot.localScale.x);
        float packWidth = miniPackRoot.sizeDelta.x * scale;
        Vector2 pos = miniPackRoot.anchoredPosition + new Vector2(packWidth + 24f, 0f) + extraOffset;
        target.anchorMin = target.anchorMax = Vector2.zero;
        target.pivot = Vector2.zero;
        target.anchoredPosition = pos;
    }

    private void HideDragVisuals()
    {
        draggingSlot = -1;
        if (dragGhostRoot != null)
            dragGhostRoot.gameObject.SetActive(false);
        if (handGhostRoot != null)
            handGhostRoot.gameObject.SetActive(false);
        if (trashRoot != null)
            trashRoot.gameObject.SetActive(false);
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

/// <summary>
/// 구형 Reward card drag 호환 marker. Phase 3부터 실제 Drop 입력은 사용하지 않습니다.
/// </summary>
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

public static class BattleInventoryInteractionAutoInstaller
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

            if (manager.GetComponent<BattleInventoryInteractionController>() != null)
                continue;

            Undo.AddComponent<BattleInventoryInteractionController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponent()
    {
        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleInventoryInteractionController>() == null)
                manager.gameObject.AddComponent<BattleInventoryInteractionController>();
        }
    }
}

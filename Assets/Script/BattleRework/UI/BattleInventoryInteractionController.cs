using System.Reflection;
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
/// 3x3 PACK의 편집 입력을 한 곳에서 관리합니다.
///
/// Reward 흐름:
/// - 보상 카드를 PACK에 Drop하면 가능한 경우 첫 빈 슬롯을 우선 사용합니다.
/// - 획득 직후 Reward를 끝내지 않고 PACK EDIT 상태를 유지합니다.
/// - 마우스 Drag/Drop 또는 패드 선택 -> 이동 -> A로 슬롯 위치를 교환합니다.
/// - TRASH는 즉시 삭제하지 않고 확인 모달을 거칩니다.
/// - 정리가 끝난 뒤 DONE으로 Reward를 확정합니다.
///
/// 장비 SO / Sprite / Scene 직렬화 데이터는 변경하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30750)]
public sealed class BattleInventoryInteractionController : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const int OverlaySortingOrder = 1550;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;

    [Header("Theme")]
    [SerializeField] private Color inkColor = new(0.035f, 0.030f, 0.055f, 0.995f);
    [SerializeField] private Color paperColor = new(0.94f, 0.90f, 0.76f, 1f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentCyan = new(0.15f, 0.88f, 0.92f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);

    [Header("Pad PACK Edit")]
    [SerializeField, Range(0.25f, 0.95f)] private float padAxisThreshold = 0.55f;
    [SerializeField, Range(0.05f, 0.8f)] private float padAxisReleaseThreshold = 0.22f;

    private Canvas interactionCanvas;
    private RectTransform interactionRoot;
    private RectTransform dragGhostRoot;
    private Image dragGhostIcon;
    private RectTransform trashRoot;
    private Image trashBack;
    private Text trashLabel;

    private RectTransform doneRoot;
    private Text doneLabel;
    private Text rewardEditStatus;

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

    private int selectedRewardSlot = -1;
    private int hoveredSlot = -1;
    private int draggingSlot = -1;
    private int padSelectedSlot;
    private int padPickedSlot = -1;
    private bool padAxisLatched;
    private bool padModeActive;

    private bool rewardStaged;
    private BattleEquipmentSO stagedReward;
    private int stagedRewardSlot = -1;
    private BattleRunState lastState = (BattleRunState)(-1);

    private int pendingDiscardSlot = -1;
    private bool confirmYesSelected;
    private bool discardModalOpen;

    private int flashedSlot = -1;
    private float flashUntil;
    private float nextResolveTime;

    private MethodInfo completeRewardSelectionMethod;

    public bool IsDraggingItem => draggingSlot >= 0;
    public bool IsRewardPackEditing => IsReward() && rewardStaged;

    private void Awake()
    {
        ResolveReferences();
        CacheRunReflection();
        EnsureOverlayCanvas();
        ResolveMiniPack();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheRunReflection();
        EnsureOverlayCanvas();
        nextResolveTime = 0f;
    }

    private void OnDisable()
    {
        HideDragVisuals();
        HideDiscardConfirm();
        SetPrizeChoicesInteractable(true);
    }

    private void Update()
    {
        ResolveReferences();
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

        if (IsReward())
        {
            ShowPackForReward();
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
    }

    private void HandleRunStateChanged(BattleRunState state)
    {
        if (state == BattleRunState.Reward)
        {
            rewardStaged = false;
            stagedReward = null;
            stagedRewardSlot = -1;
            selectedRewardSlot = -1;
            hoveredSlot = -1;
            padSelectedSlot = FindFirstUnlockedSlot();
            padPickedSlot = -1;
            SetPrizeChoicesInteractable(true);
        }
        else
        {
            rewardStaged = false;
            stagedReward = null;
            stagedRewardSlot = -1;
            selectedRewardSlot = -1;
            hoveredSlot = -1;
            padPickedSlot = -1;
            HideDiscardConfirm();
            SetPrizeChoicesInteractable(true);
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
    }

    private void CacheRunReflection()
    {
        completeRewardSelectionMethod ??= typeof(BattleRunManager).GetMethod(
            "CompleteRewardSelection",
            PrivateInstance);
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
            packImage.raycastTarget = IsReward();

        BattleInventoryPackDropTarget packDrop = miniPackRoot.GetComponent<BattleInventoryPackDropTarget>();
        if (packDrop == null)
            packDrop = miniPackRoot.gameObject.AddComponent<BattleInventoryPackDropTarget>();
        packDrop.Configure(this);
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
        if (index < 0 || index >= SlotCount)
            return;

        if (miniSelectionFrames[index] != null)
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

        draggingSlot = slotIndex;
        selectedRewardSlot = IsReward() ? slotIndex : selectedRewardSlot;
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

        RewardPrizeDrag rewardDrag = eventData.pointerDrag != null
            ? eventData.pointerDrag.GetComponent<RewardPrizeDrag>()
            : null;
        if (rewardDrag != null && IsReward())
        {
            TryStageReward(rewardDrag.RewardIndex, targetIndex);
            return;
        }

        BattleInventorySlotPointer source = eventData.pointerDrag != null
            ? eventData.pointerDrag.GetComponent<BattleInventorySlotPointer>()
            : null;
        if (source == null || !source.IsDragging)
            return;

        int sourceIndex = source.SlotIndex;
        if (!equipmentSystem.IsSlotUnlocked(sourceIndex) || sourceIndex == targetIndex)
            return;

        if (equipmentSystem.SwapSlots(sourceIndex, targetIndex))
        {
            selectedRewardSlot = IsReward() ? targetIndex : -1;
            padSelectedSlot = targetIndex;
            FlashSlot(targetIndex);
        }
    }

    internal void HandlePackDrop(PointerEventData eventData)
    {
        if (!IsReward() || discardModalOpen || eventData == null)
            return;

        RewardPrizeDrag rewardDrag = eventData.pointerDrag != null
            ? eventData.pointerDrag.GetComponent<RewardPrizeDrag>()
            : null;
        if (rewardDrag != null)
            TryStageReward(rewardDrag.RewardIndex, -1);
    }

    internal void HandleSlotClick(int slotIndex, BattleInventorySurface surface, PointerEventData.InputButton button)
    {
        if (button != PointerEventData.InputButton.Left || discardModalOpen || equipmentSystem == null ||
            !equipmentSystem.IsSlotUnlocked(slotIndex))
            return;

        if (IsReward())
        {
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
                padSelectedSlot = slotIndex;
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
        if (!IsReward())
            return;
        hoveredSlot = entered ? slotIndex : (hoveredSlot == slotIndex ? -1 : hoveredSlot);
    }

    private bool TryStageReward(int rewardIndex, int preferredSlot)
    {
        if (!IsReward() || rewardStaged || equipmentSystem == null || runManager == null)
            return false;
        if (rewardIndex < 0 || rewardIndex >= runManager.CurrentRewardChoices.Count)
            return false;

        BattleEquipmentSO reward = runManager.CurrentRewardChoices[rewardIndex];
        if (reward == null)
            return false;

        int empty = FindFirstEmptyUnlockedSlot();
        int target = -1;

        // PACK 안의 특정 빈 칸에 직접 놓았으면 그 칸을 존중합니다.
        if (preferredSlot >= 0 && equipmentSystem.IsSlotUnlocked(preferredSlot) && !HasItem(preferredSlot))
            target = preferredSlot;
        else if (empty >= 0)
            target = empty;

        // 빈 칸이 없을 때 같은 장비 합성만 예외적으로 허용합니다. 다른 장비를 자동 삭제/교체하지 않습니다.
        if (target < 0 && preferredSlot >= 0 && equipmentSystem.IsSlotUnlocked(preferredSlot))
        {
            BattleEquipmentSlot preferred = preferredSlot < equipmentSystem.Slots.Count ? equipmentSystem.Slots[preferredSlot] : null;
            if (preferred != null && preferred.equipment == reward && equipmentSystem.CanPlaceIntoSlot(preferredSlot, reward))
                target = preferredSlot;
        }

        if (target < 0)
        {
            SetRewardStatus("PACK FULL  //  TRASH AN ITEM FIRST");
            return false;
        }

        if (!equipmentSystem.PlaceIntoSlot(target, reward))
        {
            SetRewardStatus("CANNOT PLACE HERE");
            return false;
        }

        rewardStaged = true;
        stagedReward = reward;
        stagedRewardSlot = target;
        selectedRewardSlot = target;
        padSelectedSlot = target;
        padPickedSlot = -1;
        FlashSlot(target);
        SetPrizeChoicesInteractable(false);
        SetRewardStatus("ITEM IN PACK  //  REARRANGE OR TRASH  //  DONE WHEN READY");
        return true;
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
        if (button != PointerEventData.InputButton.Left || discardModalOpen || !IsReward())
            return;

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
            FlashSlot(target);
            SetRewardStatus("ITEM DISCARDED  //  PACK EDIT CONTINUES");
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
        if (BattlePauseController.IsPaused)
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

        if (Input.GetKeyDown(KeyCode.JoystickButton3) && HasItem(padSelectedSlot))
        {
            padModeActive = true;
            RequestDiscard(padSelectedSlot);
        }

        // 일반적인 패드 Start 버튼. Reward를 획득한 뒤 PACK 정리가 끝났을 때 확정합니다.
        if (rewardStaged && Input.GetKeyDown(KeyCode.JoystickButton7))
            CompleteRewardPackEdit();
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
        if (!HasItem(padSelectedSlot))
        {
            if (padPickedSlot >= 0 && equipmentSystem.IsSlotUnlocked(padSelectedSlot))
            {
                if (equipmentSystem.SwapSlots(padPickedSlot, padSelectedSlot))
                {
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

    private void CompleteRewardPackEdit()
    {
        if (!rewardStaged || stagedReward == null || runManager == null || !IsReward())
            return;

        BattleEquipmentSO selected = stagedReward;
        rewardStaged = false;
        stagedReward = null;
        stagedRewardSlot = -1;
        selectedRewardSlot = -1;
        padPickedSlot = -1;
        SetPrizeChoicesInteractable(true);

        if (completeRewardSelectionMethod != null)
        {
            completeRewardSelectionMethod.Invoke(runManager, new object[] { selected });
        }
        else
        {
            Debug.LogWarning("[BattleInventory] CompleteRewardSelection reflection fallback failed. Using SkipReward().");
            runManager.SkipReward();
        }
    }

    private int FindFirstEmptyUnlockedSlot()
    {
        if (equipmentSystem == null)
            return -1;
        for (int i = 0; i < SlotCount; i++)
            if (equipmentSystem.IsSlotUnlocked(i) && !HasItem(i))
                return i;
        return -1;
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
        bool reward = IsReward();
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
        Button button = doneRoot.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.onClick.AddListener(CompleteRewardPackEdit);
        doneRoot.gameObject.SetActive(false);

        rewardEditStatus = CreateText(interactionRoot, "", 12, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
        rewardEditStatus.rectTransform.anchorMin = rewardEditStatus.rectTransform.anchorMax = Vector2.zero;
        rewardEditStatus.rectTransform.pivot = Vector2.zero;
        rewardEditStatus.rectTransform.sizeDelta = new Vector2(420f, 54f);
        rewardEditStatus.rectTransform.anchoredPosition = new Vector2(430f, 178f);
        rewardEditStatus.gameObject.SetActive(false);
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
            miniPackGroup.alpha = 1f;
            miniPackGroup.blocksRaycasts = !BattlePauseController.IsPaused && !discardModalOpen;
            miniPackGroup.interactable = !BattlePauseController.IsPaused && !discardModalOpen;
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

        bool visible = !discardModalOpen && (IsReward() || draggingSlot >= 0 || (IsCombat() && fullBoardOpen));
        if (trashRoot.gameObject.activeSelf != visible)
            trashRoot.gameObject.SetActive(visible);
    }

    private void UpdateRewardEditUi()
    {
        bool showDone = IsReward() && rewardStaged && !discardModalOpen;
        if (doneRoot != null)
        {
            doneRoot.gameObject.SetActive(showDone);
            PositionRewardEditControls(doneRoot, new Vector2(0f, 92f));
        }

        if (rewardEditStatus != null)
        {
            rewardEditStatus.gameObject.SetActive(IsReward());
            PositionRewardEditControls(rewardEditStatus.rectTransform, new Vector2(0f, 172f));
        }

        if (trashRoot != null && IsReward())
            PositionRewardEditControls(trashRoot, Vector2.zero);
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

    private void SetRewardStatus(string value)
    {
        if (rewardEditStatus != null)
            rewardEditStatus.text = value;

        RectTransform notice = FindRect("PlacementNotice");
        if (notice != null)
        {
            Text text = notice.GetComponentInChildren<Text>(true);
            if (text != null)
                text.text = value;
        }
    }

    private void SetPrizeChoicesInteractable(bool interactable)
    {
        RectTransform choices = FindRect("PrizeChoices");
        if (choices == null)
            return;

        CanvasGroup group = choices.GetComponent<CanvasGroup>();
        if (group == null)
            group = choices.gameObject.AddComponent<CanvasGroup>();
        group.alpha = interactable ? 1f : 0.34f;
        group.blocksRaycasts = interactable;
        group.interactable = interactable;
    }

    private void HideDragVisuals()
    {
        draggingSlot = -1;
        if (dragGhostRoot != null)
            dragGhostRoot.gameObject.SetActive(false);
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

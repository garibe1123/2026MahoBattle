using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Reward 획득 흐름을 '카드 선택 -> 결정 -> PACK 정리'로 고정합니다.
///
/// - Reward 카드는 직접 Drag하지 않습니다. 클릭으로 후보를 고른 뒤 [결정]으로 확정합니다.
/// - 빈 PACK 슬롯이 있으면 첫 빈 슬롯에 자동 배치하고 PACK Edit로 전환합니다.
/// - PACK이 가득 찼으면 Reward를 Hand 상태로 들고 PACK Edit로 전환합니다.
/// - Hand 상태에서는 슬롯 클릭으로 교환하고, TRASH 클릭으로 현재 Hand를 버립니다.
/// - DONE / NEXT는 Hand가 비어 있을 때만 활성화됩니다.
/// - Reward 선택 카메라는 TV만 보지 않고 Player / Presenter까지 읽히도록 아래쪽 bias를 사용합니다.
///
/// 중요:
/// PACK Edit가 시작된 뒤 실제 슬롯 선택/교환은 BattleInventoryInteractionController가 소유합니다.
/// 이 컨트롤러는 staged reward가 어느 BattleEquipmentSlot 객체에 들어 있는지만 추적하고,
/// selectedRewardSlot을 매 프레임 덮어쓰지 않습니다. 따라서 빈 슬롯 클릭/교환이 정상 작동합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(44000)]
public sealed class BattleRewardDecisionFlowController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const string ObsoleteDescriptionBarName = "RewardActiveDescriptionBar";

    [Header("Hand")]
    [SerializeField] private Vector2 handMouseOffset = new(72f, -72f);
    [SerializeField] private Vector2 handPadOffset = new(92f, 0f);
    [SerializeField, Range(0.25f, 0.95f)] private float padAxisThreshold = 0.55f;
    [SerializeField, Range(0.05f, 0.8f)] private float padAxisReleaseThreshold = 0.22f;

    [Header("Reward Camera")]
    [SerializeField] private float rewardCameraBiasY = -0.72f;

    private BattleRunManager runManager;
    private BattleEquipmentSystem equipmentSystem;
    private BattleInventoryInteractionController inventoryInteraction;
    private BattleHUD battleHud;
    private BattleSelectionLayoutPolicyController selectionLayout;
    private BattleKineticLoadoutUI kineticLoadout;

    private FieldInfo pendingRewardIndexField;
    private FieldInfo rewardStagedField;
    private FieldInfo stagedRewardField;
    private FieldInfo stagedRewardSlotField;
    private FieldInfo selectedRewardSlotField;
    private FieldInfo hoveredSlotField;
    private FieldInfo padSelectedSlotField;
    private FieldInfo padPickedSlotField;
    private FieldInfo padModeField;
    private FieldInfo inventoryLastStateField;
    private FieldInfo rewardEditStatusField;
    private FieldInfo rewardCameraBiasField;
    private MethodInfo loadoutRefreshMethod;

    private RectTransform screenInner;
    private RectTransform prizeChoices;
    private RectTransform handGhost;
    private Image handGhostIcon;
    private RectTransform trashRoot;
    private BattleInventoryTrashDropTarget legacyTrashTarget;
    private RectTransform doneRoot;
    private Button doneButton;
    private Text rewardEditStatus;

    private readonly List<RectTransform> obsoleteChoiceBars = new();

    private bool decisionFlowActive;
    private BattleEquipmentSO chosenReward;
    private bool chosenRewardCommitted;
    private int chosenRewardSlot = -1;
    private BattleEquipmentSlot chosenRewardSlotRef;
    private BattleEquipmentSlot handSlot;
    private bool handIsChosenReward;

    private bool inventoryDisabledByThis;
    private bool doneBound;
    private bool padAxisLatched;
    private bool handPadMode;
    private int handPadSlot;
    private Vector3 lastMousePosition;
    private bool lastMousePositionValid;
    private float nextResolveTime;
    private bool wasReward;

    private void Awake()
    {
        ResolveReferences();
        CacheReflection();
        ResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheReflection();
        ResolveUi();
        nextResolveTime = 0f;
    }

    private void OnDisable()
    {
        RestoreSuppressedInventory();
        SetLegacyPackHandlersEnabled(true);
        SetRewardCardDragEnabled(true);
        HideHandGhost();
        HideRewardEditStatus();
    }

    private void Update()
    {
        ResolveReferences();
        CacheReflection();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveUi();
        }

        bool reward = IsReward();
        if (!reward)
        {
            if (wasReward)
                ResetLocalFlow();
            wasReward = false;
            return;
        }

        wasReward = true;
        ApplyLowerRewardCameraBias();
        HideRewardEditStatus();

        bool packEditing = inventoryInteraction != null && inventoryInteraction.IsRewardPackEditing;
        if (!decisionFlowActive && !packEditing)
        {
            MaintainChoiceStage();
            return;
        }

        if (decisionFlowActive)
            MaintainDecisionPackStage();
    }

    private void LateUpdate()
    {
        if (!IsReward())
            return;

        ResolveUi();
        HideRewardEditStatus();

        if (!decisionFlowActive && (inventoryInteraction == null || !inventoryInteraction.IsRewardPackEditing))
        {
            HideObsoleteChoiceUi();
            ApplyChoiceCopyOnly();
            HideLegacyPackControlsDuringChoice();
            return;
        }

        if (decisionFlowActive)
        {
            ApplyDoneState();
            ApplyHandStateVisuals();
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        if (battleHud == null)
            battleHud = FindFirstObjectByType<BattleHUD>(FindObjectsInactive.Include);
        if (selectionLayout == null)
            selectionLayout = FindFirstObjectByType<BattleSelectionLayoutPolicyController>(FindObjectsInactive.Include);
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
    }

    private void CacheReflection()
    {
        pendingRewardIndexField ??= typeof(BattleHUD).GetField("pendingRewardIndex", PrivateInstance);

        System.Type interactionType = typeof(BattleInventoryInteractionController);
        rewardStagedField ??= interactionType.GetField("rewardStaged", PrivateInstance);
        stagedRewardField ??= interactionType.GetField("stagedReward", PrivateInstance);
        stagedRewardSlotField ??= interactionType.GetField("stagedRewardSlot", PrivateInstance);
        selectedRewardSlotField ??= interactionType.GetField("selectedRewardSlot", PrivateInstance);
        hoveredSlotField ??= interactionType.GetField("hoveredSlot", PrivateInstance);
        padSelectedSlotField ??= interactionType.GetField("padSelectedSlot", PrivateInstance);
        padPickedSlotField ??= interactionType.GetField("padPickedSlot", PrivateInstance);
        padModeField ??= interactionType.GetField("padModeActive", PrivateInstance);
        inventoryLastStateField ??= interactionType.GetField("lastState", PrivateInstance);
        rewardEditStatusField ??= interactionType.GetField("rewardEditStatus", PrivateInstance);

        if (selectionLayout != null && rewardCameraBiasField == null)
            rewardCameraBiasField = typeof(BattleSelectionLayoutPolicyController).GetField("rewardCameraBiasWorld", PrivateInstance);

        if (kineticLoadout != null && loadoutRefreshMethod == null)
            loadoutRefreshMethod = typeof(BattleKineticLoadoutUI).GetMethod("RefreshAll", PrivateInstance);
    }

    private void ResolveUi()
    {
        RectTransform rewardScreen = FindRect("PrizeSelectionScreen");
        screenInner = rewardScreen != null
            ? rewardScreen.Find("ScreenInner") as RectTransform
            : FindRect("ScreenInner");

        prizeChoices = screenInner != null
            ? screenInner.Find("PrizeChoices") as RectTransform
            : FindRect("PrizeChoices");

        if (handGhost == null)
        {
            handGhost = FindRect("InventoryDragGhost");
            handGhostIcon = handGhost != null ? handGhost.Find("Icon")?.GetComponent<Image>() : null;
        }

        RectTransform nextTrash = FindRect("InventoryTrash");
        if (nextTrash != trashRoot)
        {
            trashRoot = nextTrash;
            legacyTrashTarget = trashRoot != null ? trashRoot.GetComponent<BattleInventoryTrashDropTarget>() : null;
            if (trashRoot != null)
            {
                BattleRewardHandTrashRelay relay = trashRoot.GetComponent<BattleRewardHandTrashRelay>();
                if (relay == null)
                    relay = trashRoot.gameObject.AddComponent<BattleRewardHandTrashRelay>();
                relay.Configure(this);
            }
        }

        RectTransform nextDone = FindRect("RewardPackDone");
        if (nextDone != doneRoot)
        {
            doneRoot = nextDone;
            doneButton = doneRoot != null ? doneRoot.GetComponent<Button>() : null;
            doneBound = false;
        }

        if (doneButton != null && !doneBound)
        {
            doneButton.onClick.AddListener(HandleDoneButtonPressed);
            doneBound = true;
        }

        if (inventoryInteraction != null && rewardEditStatusField != null)
            rewardEditStatus = rewardEditStatusField.GetValue(inventoryInteraction) as Text;

        ResolveObsoleteChoiceBars();

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slot = FindRect($"GridSlot_{i}");
            if (slot == null)
                continue;

            Image image = slot.GetComponent<Image>();
            if (image != null)
                image.raycastTarget = true;

            BattleRewardHandSlotRelay relay = slot.GetComponent<BattleRewardHandSlotRelay>();
            if (relay == null)
                relay = slot.gameObject.AddComponent<BattleRewardHandSlotRelay>();
            relay.Configure(this, i);
        }
    }

    private void ResolveObsoleteChoiceBars()
    {
        obsoleteChoiceBars.Clear();
        RectTransform[] all = UnityEngine.Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == ObsoleteDescriptionBarName)
                obsoleteChoiceBars.Add(rect);
        }
    }

    private void HideObsoleteChoiceUi()
    {
        for (int i = 0; i < obsoleteChoiceBars.Count; i++)
        {
            RectTransform bar = obsoleteChoiceBars[i];
            if (bar == null)
                continue;

            CanvasGroup group = bar.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            Image image = bar.GetComponent<Image>();
            if (image != null)
                image.raycastTarget = false;

            if (bar.gameObject.activeSelf)
                bar.gameObject.SetActive(false);
        }
    }

    private void MaintainChoiceStage()
    {
        decisionFlowActive = false;
        handSlot = null;
        handIsChosenReward = false;
        chosenReward = null;
        chosenRewardCommitted = false;
        chosenRewardSlot = -1;
        chosenRewardSlotRef = null;

        SuppressInventoryForStaticRewardChoice();
        SetRewardCardDragEnabled(false);
        HideHandGhost();
        HideRewardEditStatus();
        HideObsoleteChoiceUi();

        int selected = GetPendingRewardIndex();
        bool valid = TryGetReward(selected, out _);
        if (valid && !BattlePauseController.IsPaused &&
            (Input.GetKeyDown(KeyCode.Return) ||
             Input.GetKeyDown(KeyCode.Space) ||
             Input.GetKeyDown(KeyCode.JoystickButton0)))
        {
            ConfirmSelectedReward();
        }
    }

    // CardActionController가 reflection으로 호출하므로 NonPublic 상태를 유지합니다.
    private void ConfirmSelectedReward()
    {
        if (decisionFlowActive || !IsReward() || equipmentSystem == null || inventoryInteraction == null)
            return;

        int rewardIndex = GetPendingRewardIndex();
        if (!TryGetReward(rewardIndex, out BattleEquipmentSO reward))
            return;

        decisionFlowActive = true;
        chosenReward = reward;
        chosenRewardCommitted = false;
        chosenRewardSlot = -1;
        chosenRewardSlotRef = null;
        handSlot = null;
        handIsChosenReward = false;
        handPadMode = false;
        handPadSlot = FindFirstUnlockedSlot();
        padAxisLatched = false;

        int empty = FindFirstEmptyUnlockedSlot();
        if (empty >= 0 && equipmentSystem.PlaceIntoSlot(empty, reward))
        {
            chosenRewardCommitted = true;
            chosenRewardSlot = empty;
            chosenRewardSlotRef = empty < equipmentSystem.Slots.Count ? equipmentSystem.Slots[empty] : null;
            handPadSlot = empty;
        }
        else
        {
            handSlot = new BattleEquipmentSlot
            {
                equipment = reward,
                grade = 1,
                copies = 1
            };
            handIsChosenReward = true;
        }

        SetPendingRewardIndex(-1);
        SetPrizeChoicesInteractable(false);
        SetRewardCardDragEnabled(false);

        if (HasHand)
        {
            SuppressInventoryForHand();
            WriteStageState(false);
        }
        else
        {
            // 자동 저장 직후에만 새 Reward 슬롯을 한 번 선택합니다.
            // 이후 PACK 선택은 BattleInventoryInteractionController가 단독 소유합니다.
            WriteStageState(true);
            RestoreSuppressedInventory();
            SetLegacyPackHandlersEnabled(true);
        }

        HideRewardEditStatus();
        HideObsoleteChoiceUi();
        InvokeLoadoutRefresh();
    }

    private void MaintainDecisionPackStage()
    {
        SetPrizeChoicesInteractable(false);
        SetRewardCardDragEnabled(false);
        HideRewardEditStatus();

        SyncChosenRewardLocation();
        WriteStageState(false);

        if (HasHand)
        {
            SuppressInventoryForHand();
            HandleHandPadInput();
        }
        else
        {
            RestoreSuppressedInventory();
            SetLegacyPackHandlersEnabled(true);

            if (!chosenRewardCommitted && !BattlePauseController.IsPaused && Input.GetKeyDown(KeyCode.JoystickButton7))
                FinishWithoutReward();
        }
    }

    /// <summary>
    /// BattleEquipmentSystem.SwapSlots는 BattleEquipmentSlot 객체 자체를 교환합니다.
    /// 따라서 Reward가 처음 들어간 Slot 객체 reference를 추적하면 사용자가 PACK에서 위치를 바꿔도
    /// stagedRewardSlot을 정확한 새 위치로 동기화할 수 있습니다.
    /// </summary>
    private void SyncChosenRewardLocation()
    {
        if (!chosenRewardCommitted || chosenRewardSlotRef == null || equipmentSystem == null)
            return;

        int found = -1;
        for (int i = 0; i < equipmentSystem.Slots.Count && i < SlotCount; i++)
        {
            if (ReferenceEquals(equipmentSystem.Slots[i], chosenRewardSlotRef))
            {
                found = i;
                break;
            }
        }

        if (found >= 0 && chosenRewardSlotRef.equipment == chosenReward)
        {
            chosenRewardSlot = found;
            return;
        }

        // Reward가 PACK에서 실제로 버려지거나 다른 장비로 교체된 경우입니다.
        chosenRewardCommitted = false;
        chosenRewardSlot = -1;
        chosenRewardSlotRef = null;
    }

    private void HandleHandPadInput()
    {
        if (!HasHand || equipmentSystem == null || BattlePauseController.IsPaused)
            return;

        if (Input.mousePresent)
        {
            Vector3 mouse = Input.mousePosition;
            if (!lastMousePositionValid)
            {
                lastMousePosition = mouse;
                lastMousePositionValid = true;
            }
            else if ((mouse - lastMousePosition).sqrMagnitude > 4f)
            {
                handPadMode = false;
                lastMousePosition = mouse;
            }
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
            handPadMode = true;
        }
        else if (padAxisLatched && magnitude <= padAxisReleaseThreshold)
        {
            padAxisLatched = false;
        }

        if (dx != 0 || dy != 0)
        {
            MoveHandPadSelection(dx, dy);
            handPadMode = true;
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton0) ||
            Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.Space))
        {
            handPadMode = true;
            HandleHandSlotClick(handPadSlot);
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton3))
        {
            handPadMode = true;
            DiscardHand();
        }
    }

    private void MoveHandPadSelection(int dx, int dy)
    {
        if (equipmentSystem == null)
            return;

        Vector2Int p = BattleEquipmentSystem.SlotIndexToGrid(Mathf.Clamp(handPadSlot, 0, SlotCount - 1));
        int nx = Mathf.Clamp(p.x + dx, 0, BattleEquipmentSystem.GridSize - 1);
        int ny = Mathf.Clamp(p.y + dy, 0, BattleEquipmentSystem.GridSize - 1);
        int next = BattleEquipmentSystem.GridToSlotIndex(nx, ny);
        if (next >= 0 && equipmentSystem.IsSlotUnlocked(next))
            handPadSlot = next;
    }

    internal void HandleHandSlotClick(int slotIndex)
    {
        if (!decisionFlowActive || !HasHand || equipmentSystem == null || !IsReward() ||
            !equipmentSystem.IsSlotUnlocked(slotIndex))
            return;

        BattleEquipmentSlot target = slotIndex < equipmentSystem.Slots.Count
            ? equipmentSystem.Slots[slotIndex]
            : null;
        if (target == null)
            return;

        if (handIsChosenReward && target.equipment == chosenReward)
        {
            if (target.grade >= 3)
                return;

            if (equipmentSystem.PlaceIntoSlot(slotIndex, chosenReward))
            {
                chosenRewardCommitted = true;
                chosenRewardSlot = slotIndex;
                chosenRewardSlotRef = equipmentSystem.Slots[slotIndex];
                handSlot = null;
                handIsChosenReward = false;
                AfterHandChanged(slotIndex);
            }
            return;
        }

        BattleEquipmentSlot outgoing = target.equipment != null
            ? new BattleEquipmentSlot
            {
                equipment = target.equipment,
                grade = target.grade,
                copies = target.copies
            }
            : null;

        BattleEquipmentSlot incoming = handSlot;
        bool incomingWasChosen = handIsChosenReward;
        bool outgoingWasChosen = chosenRewardCommitted && chosenRewardSlotRef != null && ReferenceEquals(target, chosenRewardSlotRef);

        bool placed = target.equipment == null
            ? equipmentSystem.PlaceIntoSlot(slotIndex, incoming.equipment)
            : equipmentSystem.ReplaceSlot(slotIndex, incoming.equipment);

        if (!placed)
            return;

        BattleEquipmentSlot inserted = equipmentSystem.Slots[slotIndex];
        if (inserted != null)
        {
            inserted.grade = Mathf.Clamp(incoming.grade, 1, 3);
            inserted.copies = Mathf.Clamp(incoming.copies, 1, 2);
        }

        handSlot = outgoing;
        handIsChosenReward = outgoingWasChosen;

        if (incomingWasChosen)
        {
            chosenRewardCommitted = true;
            chosenRewardSlot = slotIndex;
            chosenRewardSlotRef = inserted;
        }
        else if (outgoingWasChosen)
        {
            chosenRewardCommitted = false;
            chosenRewardSlot = -1;
            chosenRewardSlotRef = null;
        }

        AfterHandChanged(slotIndex);
    }

    internal void HandleHandTrashClick()
    {
        if (decisionFlowActive && HasHand)
            DiscardHand();
    }

    private void DiscardHand()
    {
        if (!HasHand)
            return;

        bool discardedChosenReward = handIsChosenReward;
        handSlot = null;
        handIsChosenReward = false;

        if (discardedChosenReward)
        {
            chosenRewardCommitted = false;
            chosenRewardSlot = -1;
            chosenRewardSlotRef = null;
        }

        AfterHandChanged(chosenRewardSlot);
    }

    private void AfterHandChanged(int focusSlot)
    {
        handPadSlot = focusSlot >= 0 ? focusSlot : FindFirstUnlockedSlot();
        SyncChosenRewardLocation();
        WriteStageState(!HasHand && chosenRewardCommitted);
        InvokeLoadoutRefresh();
        HideRewardEditStatus();

        if (HasHand)
        {
            SuppressInventoryForHand();
        }
        else
        {
            HideHandGhost();
            RestoreSuppressedInventory();
            SetLegacyPackHandlersEnabled(true);
        }
    }

    private void WriteStageState(bool forceRewardSelection)
    {
        if (inventoryInteraction == null || !decisionFlowActive)
            return;

        WriteBool(rewardStagedField, inventoryInteraction, true);
        stagedRewardField?.SetValue(
            inventoryInteraction,
            HasHand ? null : (chosenRewardCommitted ? chosenReward : null));
        WriteInt(
            stagedRewardSlotField,
            inventoryInteraction,
            chosenRewardCommitted ? chosenRewardSlot : -1);
        WriteInt(hoveredSlotField, inventoryInteraction, -1);
        WriteInt(padSelectedSlotField, inventoryInteraction, Mathf.Clamp(handPadSlot, 0, SlotCount - 1));
        WriteInt(padPickedSlotField, inventoryInteraction, -1);
        WriteBool(padModeField, inventoryInteraction, false);

        // Hand를 들고 있을 때는 PACK 자체 선택을 제거합니다.
        // Hand가 없을 때는 첫 자동 저장/Hand 해소 순간에만 선택 슬롯을 지정하고,
        // 이후에는 InventoryInteraction이 클릭 선택을 단독으로 관리합니다.
        if (HasHand)
            WriteInt(selectedRewardSlotField, inventoryInteraction, -1);
        else if (forceRewardSelection && chosenRewardCommitted)
            WriteInt(selectedRewardSlotField, inventoryInteraction, chosenRewardSlot);
    }

    private void SuppressInventoryForStaticRewardChoice()
    {
        if (inventoryInteraction != null && inventoryInteraction.enabled)
        {
            inventoryInteraction.enabled = false;
            inventoryDisabledByThis = true;
        }

        SetLegacyPackHandlersEnabled(false);
        SetPrizeChoicesInteractable(true);
    }

    private void SuppressInventoryForHand()
    {
        if (inventoryInteraction != null && inventoryInteraction.enabled)
        {
            inventoryInteraction.enabled = false;
            inventoryDisabledByThis = true;
        }

        SetLegacyPackHandlersEnabled(false);
        SetPrizeChoicesInteractable(false);
    }

    private void RestoreSuppressedInventory()
    {
        if (inventoryInteraction != null && inventoryDisabledByThis)
        {
            if (inventoryLastStateField != null && runManager != null)
                inventoryLastStateField.SetValue(inventoryInteraction, runManager.State);

            inventoryInteraction.enabled = true;
            inventoryDisabledByThis = false;
        }
    }

    private void SetLegacyPackHandlersEnabled(bool enabled)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            ToggleSlotPointer(FindRect($"BackpackCell_{i}"), enabled);
            ToggleSlotPointer(FindRect($"GridSlot_{i}"), enabled);
            ToggleSlotPointer(FindRect($"RewardLoadoutSlot_{i + 1}"), enabled);
        }

        RectTransform miniPack = FindRect("BackpackMiniGrid");
        if (miniPack != null)
        {
            BattleInventoryPackDropTarget drop = miniPack.GetComponent<BattleInventoryPackDropTarget>();
            if (drop != null)
                drop.enabled = enabled;
        }

        if (legacyTrashTarget != null)
            legacyTrashTarget.enabled = enabled;
    }

    private static void ToggleSlotPointer(RectTransform slot, bool enabled)
    {
        if (slot == null)
            return;

        BattleInventorySlotPointer pointer = slot.GetComponent<BattleInventorySlotPointer>();
        if (pointer != null)
            pointer.enabled = enabled;
    }

    private void SetRewardCardDragEnabled(bool enabled)
    {
        if (prizeChoices == null)
            return;

        RewardPrizeDrag[] drags = prizeChoices.GetComponentsInChildren<RewardPrizeDrag>(true);
        for (int i = 0; i < drags.Length; i++)
        {
            RewardPrizeDrag drag = drags[i];
            if (drag != null)
                drag.enabled = enabled;
        }
    }

    private void ApplyChoiceCopyOnly()
    {
        // 카드/버튼 레이아웃은 BattleRewardCardActionController가 소유합니다.
        // 여기서는 구형 Drag 문구만 현재 선택 흐름에 맞게 정리합니다.
        if (screenInner == null)
            return;

        Text[] texts = screenInner.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null)
                continue;

            string value = text.text ?? string.Empty;
            if (value.Contains("SELECT") && value.Contains("DRAG") && value.Contains("DROP"))
                text.text = "SELECT  •  INSPECT  •  DECIDE";
            else if (value == "CLICK / DRAG")
                text.text = "CLICK TO SELECT";
        }

        // PlacementNotice는 현재 [아이템 획득 포기하기] 버튼으로 재사용되므로
        // 이 컨트롤러가 상태문구를 절대 덮어쓰지 않습니다.
    }

    private void HideLegacyPackControlsDuringChoice()
    {
        if (trashRoot != null && trashRoot.gameObject.activeSelf)
            trashRoot.gameObject.SetActive(false);
        if (doneRoot != null && doneRoot.gameObject.activeSelf)
            doneRoot.gameObject.SetActive(false);
        HideHandGhost();
    }

    private void ApplyDoneState()
    {
        bool ready = decisionFlowActive && !HasHand;

        if (doneRoot != null)
        {
            if (!doneRoot.gameObject.activeSelf)
                doneRoot.gameObject.SetActive(true);
            if (doneButton != null)
                doneButton.interactable = ready;
        }

        if (trashRoot != null && !trashRoot.gameObject.activeSelf)
            trashRoot.gameObject.SetActive(true);
    }

    private void ApplyHandStateVisuals()
    {
        if (!HasHand)
        {
            HideHandGhost();
            if (legacyTrashTarget != null)
                legacyTrashTarget.enabled = true;
            return;
        }

        if (legacyTrashTarget != null)
            legacyTrashTarget.enabled = false;

        if (handGhost == null)
        {
            ResolveUi();
            if (handGhost == null)
                return;
        }

        handGhost.gameObject.SetActive(true);
        if (handGhostIcon != null)
        {
            handGhostIcon.sprite = handSlot.equipment != null ? handSlot.equipment.icon : null;
            handGhostIcon.enabled = handSlot.equipment != null && handSlot.equipment.icon != null;
        }

        if (handPadMode)
        {
            RectTransform slot = FindRect($"GridSlot_{Mathf.Clamp(handPadSlot, 0, SlotCount - 1)}");
            if (slot != null)
                handGhost.position = slot.position + (Vector3)handPadOffset;
        }
        else if (Input.mousePresent)
        {
            handGhost.position = Input.mousePosition + (Vector3)handMouseOffset;
        }

        handGhost.SetAsLastSibling();
    }

    private void HideHandGhost()
    {
        if (handGhost != null && handGhost.gameObject.activeSelf)
            handGhost.gameObject.SetActive(false);
    }

    private void HandleDoneButtonPressed()
    {
        if (!decisionFlowActive || HasHand || !IsReward())
            return;

        SyncChosenRewardLocation();
        if (!chosenRewardCommitted)
            FinishWithoutReward();
    }

    private void FinishWithoutReward()
    {
        if (!decisionFlowActive || HasHand || chosenRewardCommitted || runManager == null || !IsReward())
            return;

        decisionFlowActive = false;
        RestoreSuppressedInventory();
        SetLegacyPackHandlersEnabled(true);
        HideHandGhost();
        HideRewardEditStatus();
        runManager.SkipReward();
    }

    private void SetPrizeChoicesInteractable(bool interactable)
    {
        if (prizeChoices == null)
            return;

        CanvasGroup group = prizeChoices.GetComponent<CanvasGroup>();
        if (group == null)
            group = prizeChoices.gameObject.AddComponent<CanvasGroup>();
        group.alpha = interactable ? 1f : 0.34f;
        group.blocksRaycasts = interactable;
        group.interactable = interactable;
    }

    private void HideRewardEditStatus()
    {
        if (rewardEditStatus == null && inventoryInteraction != null && rewardEditStatusField != null)
            rewardEditStatus = rewardEditStatusField.GetValue(inventoryInteraction) as Text;

        if (rewardEditStatus == null)
            return;

        rewardEditStatus.text = string.Empty;
        if (rewardEditStatus.gameObject.activeSelf)
            rewardEditStatus.gameObject.SetActive(false);
    }

    private void ApplyLowerRewardCameraBias()
    {
        if (selectionLayout == null)
            return;
        if (rewardCameraBiasField == null)
            rewardCameraBiasField = typeof(BattleSelectionLayoutPolicyController).GetField("rewardCameraBiasWorld", PrivateInstance);
        if (rewardCameraBiasField == null)
            return;

        object raw = rewardCameraBiasField.GetValue(selectionLayout);
        if (raw is not Vector2 current)
            return;

        if (current.y > rewardCameraBiasY)
            rewardCameraBiasField.SetValue(selectionLayout, new Vector2(current.x, rewardCameraBiasY));
    }

    private void ResetLocalFlow()
    {
        decisionFlowActive = false;
        chosenReward = null;
        chosenRewardCommitted = false;
        chosenRewardSlot = -1;
        chosenRewardSlotRef = null;
        handSlot = null;
        handIsChosenReward = false;
        handPadMode = false;
        padAxisLatched = false;
        lastMousePositionValid = false;

        RestoreSuppressedInventory();
        SetLegacyPackHandlersEnabled(true);
        SetRewardCardDragEnabled(true);
        HideHandGhost();
        HideRewardEditStatus();
    }

    private int FindFirstEmptyUnlockedSlot()
    {
        if (equipmentSystem == null)
            return -1;

        for (int i = 0; i < SlotCount; i++)
        {
            if (equipmentSystem.IsSlotUnlocked(i) &&
                i < equipmentSystem.Slots.Count &&
                (equipmentSystem.Slots[i] == null || equipmentSystem.Slots[i].equipment == null))
                return i;
        }
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

    private bool TryGetReward(int index, out BattleEquipmentSO reward)
    {
        reward = null;
        if (runManager == null || index < 0 || index >= runManager.CurrentRewardChoices.Count)
            return false;
        reward = runManager.CurrentRewardChoices[index];
        return reward != null;
    }

    private int GetPendingRewardIndex()
    {
        if (battleHud == null || pendingRewardIndexField == null)
            return -1;
        object raw = pendingRewardIndexField.GetValue(battleHud);
        return raw is int index ? index : -1;
    }

    private void SetPendingRewardIndex(int value)
    {
        if (battleHud != null && pendingRewardIndexField != null)
            pendingRewardIndexField.SetValue(battleHud, value);
    }

    private void InvokeLoadoutRefresh()
    {
        if (kineticLoadout != null && loadoutRefreshMethod != null)
            loadoutRefreshMethod.Invoke(kineticLoadout, null);
    }

    private bool IsReward()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
    }

    private bool HasHand => handSlot != null && handSlot.equipment != null;

    private static void WriteInt(FieldInfo field, object owner, int value)
    {
        if (field != null && owner != null)
            field.SetValue(owner, value);
    }

    private static void WriteBool(FieldInfo field, object owner, bool value)
    {
        if (field != null && owner != null)
            field.SetValue(owner, value);
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
}

internal sealed class BattleRewardHandSlotRelay : MonoBehaviour, IPointerClickHandler
{
    private BattleRewardDecisionFlowController owner;
    private int slotIndex;

    public void Configure(BattleRewardDecisionFlowController controller, int index)
    {
        owner = controller;
        slotIndex = index;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            owner?.HandleHandSlotClick(slotIndex);
    }
}

internal sealed class BattleRewardHandTrashRelay : MonoBehaviour, IPointerClickHandler
{
    private BattleRewardDecisionFlowController owner;

    public void Configure(BattleRewardDecisionFlowController controller)
    {
        owner = controller;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            owner?.HandleHandTrashClick();
    }
}

public static class BattleRewardDecisionFlowAutoInstaller
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

            if (manager.GetComponent<BattleRewardDecisionFlowController>() != null)
                continue;

            Undo.AddComponent<BattleRewardDecisionFlowController>(manager.gameObject);
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
            if (manager != null && manager.GetComponent<BattleRewardDecisionFlowController>() == null)
                manager.gameObject.AddComponent<BattleRewardDecisionFlowController>();
        }
    }
}

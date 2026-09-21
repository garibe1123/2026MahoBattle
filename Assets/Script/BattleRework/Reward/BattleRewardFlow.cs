using System;
using UnityEngine;

public enum BattleRewardPhase
{
    Inactive,
    Choosing,
    PackEditing
}

/// <summary>
/// Reward 결정 과정의 authoritative state owner입니다.
///
/// 이 클래스는 UI를 만들거나 RectTransform을 수정하지 않습니다.
/// 책임은 다음으로 제한합니다.
/// - 후보 선택
/// - 선택 확정
/// - Reward Hand
/// - Hand <-> PACK 교환
/// - 선택한 Reward가 PACK에 남아 있는지 추적
/// - Reward 완료/포기 후 Run 진행 요청
///
/// PACK의 실제 데이터 변경은 BattleEquipmentSystem API만 사용합니다.
/// 입력/레이아웃/색/애니메이션은 UI 계층의 책임입니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleRewardFlow : MonoBehaviour
{
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;

    private BattleRewardPhase phase = BattleRewardPhase.Inactive;
    private int selectedChoiceIndex = -1;
    private BattleEquipmentSO chosenReward;
    private bool chosenRewardCommitted;
    private int chosenRewardSlot = -1;
    private BattleEquipmentSlot chosenRewardSlotRef;
    private BattleEquipmentStack hand;
    private bool handIsChosenReward;

    private BattleRunManager subscribedRunManager;
    private BattleEquipmentSystem subscribedEquipmentSystem;

    public BattleRewardPhase Phase => phase;
    public int SelectedChoiceIndex => selectedChoiceIndex;
    public BattleEquipmentSO ChosenReward => chosenReward;
    public bool ChosenRewardCommitted => chosenRewardCommitted;
    public int ChosenRewardSlot => chosenRewardSlot;
    public BattleEquipmentStack Hand => hand;
    public bool HasHand => !hand.IsEmpty;
    public bool HandIsChosenReward => HasHand && handIsChosenReward;
    public bool CanConfirmChoice => phase == BattleRewardPhase.Choosing && SelectedChoice != null;
    public bool CanComplete => phase == BattleRewardPhase.PackEditing && !HasHand;

    public BattleEquipmentSO SelectedChoice
    {
        get
        {
            if (runManager == null || selectedChoiceIndex < 0 ||
                selectedChoiceIndex >= runManager.CurrentRewardChoices.Count)
                return null;
            return runManager.CurrentRewardChoices[selectedChoiceIndex];
        }
    }

    public event Action Changed;

    private void Awake()
    {
        ResolveReferences();
        EnsureSubscriptions();
        RefreshFromRunState();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureSubscriptions();
        RefreshFromRunState();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        ResolveReferences();
        EnsureSubscriptions();
        RefreshFromRunState();

        if (phase == BattleRewardPhase.PackEditing && chosenRewardCommitted)
            SyncChosenRewardLocation();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
    }

    private void EnsureSubscriptions()
    {
        if (subscribedRunManager != runManager)
        {
            if (subscribedRunManager != null)
                subscribedRunManager.StateChanged -= HandleRunStateChanged;

            subscribedRunManager = runManager;
            if (subscribedRunManager != null)
                subscribedRunManager.StateChanged += HandleRunStateChanged;
        }

        if (subscribedEquipmentSystem != equipmentSystem)
        {
            if (subscribedEquipmentSystem != null)
                subscribedEquipmentSystem.InventoryChanged -= HandleInventoryChanged;

            subscribedEquipmentSystem = equipmentSystem;
            if (subscribedEquipmentSystem != null)
                subscribedEquipmentSystem.InventoryChanged += HandleInventoryChanged;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedRunManager != null)
            subscribedRunManager.StateChanged -= HandleRunStateChanged;
        if (subscribedEquipmentSystem != null)
            subscribedEquipmentSystem.InventoryChanged -= HandleInventoryChanged;

        subscribedRunManager = null;
        subscribedEquipmentSystem = null;
    }

    private void HandleRunStateChanged(BattleRunState _)
    {
        RefreshFromRunState();
    }

    private void HandleInventoryChanged()
    {
        if (phase == BattleRewardPhase.PackEditing && chosenRewardCommitted)
            SyncChosenRewardLocation();
    }

    /// <summary>
    /// BattleRunManager의 상위 상태와 RewardFlow의 수명을 동기화합니다.
    /// UI Controller가 실행 순서에 의존하지 않고 명시적으로 호출해도 안전합니다.
    /// </summary>
    public void RefreshFromRunState()
    {
        bool rewardActive = runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;

        if (rewardActive)
        {
            if (phase == BattleRewardPhase.Inactive)
                BeginChoosing();
            return;
        }

        if (phase != BattleRewardPhase.Inactive || selectedChoiceIndex >= 0 || chosenReward != null || HasHand)
            ResetState(false);
    }

    private void BeginChoosing()
    {
        selectedChoiceIndex = -1;
        chosenReward = null;
        chosenRewardCommitted = false;
        chosenRewardSlot = -1;
        chosenRewardSlotRef = null;
        hand.Clear();
        handIsChosenReward = false;
        phase = BattleRewardPhase.Choosing;
        RaiseChanged();
    }

    public bool SelectChoice(int index)
    {
        RefreshFromRunState();
        if (phase != BattleRewardPhase.Choosing || runManager == null ||
            index < 0 || index >= runManager.CurrentRewardChoices.Count ||
            runManager.CurrentRewardChoices[index] == null)
            return false;

        if (selectedChoiceIndex == index)
            return true;

        selectedChoiceIndex = index;
        RaiseChanged();
        return true;
    }

    public void ClearChoice()
    {
        if (phase != BattleRewardPhase.Choosing || selectedChoiceIndex < 0)
            return;

        selectedChoiceIndex = -1;
        RaiseChanged();
    }

    /// <summary>
    /// 선택한 Reward를 확정하고 PACK 편집 단계로 이동합니다.
    /// 빈 슬롯이 있으면 자동 저장하고, 없으면 Hand에 둡니다.
    /// </summary>
    public bool ConfirmSelectedChoice()
    {
        RefreshFromRunState();
        if (phase != BattleRewardPhase.Choosing || equipmentSystem == null)
            return false;

        BattleEquipmentSO reward = SelectedChoice;
        if (reward == null)
            return false;

        chosenReward = reward;
        chosenRewardCommitted = false;
        chosenRewardSlot = -1;
        chosenRewardSlotRef = null;
        hand.Clear();
        handIsChosenReward = false;

        int empty = equipmentSystem.FindFirstEmptyUnlockedSlot();
        if (empty >= 0 && equipmentSystem.PlaceIntoSlot(empty, reward))
        {
            chosenRewardCommitted = true;
            chosenRewardSlot = empty;
            equipmentSystem.TryGetSlot(empty, out chosenRewardSlotRef);
        }
        else
        {
            hand = BattleEquipmentStack.Create(reward);
            handIsChosenReward = true;
        }

        selectedChoiceIndex = -1;
        phase = BattleRewardPhase.PackEditing;
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Reward Hand와 PACK 슬롯을 교환합니다.
    /// 같은 Reward를 합칠 수 있는 슬롯에 놓는 경우에는 교환 대신 합성을 수행합니다.
    /// </summary>
    public bool ExchangeHandWithSlot(int slotIndex)
    {
        RefreshFromRunState();
        if (phase != BattleRewardPhase.PackEditing || !HasHand || equipmentSystem == null ||
            !equipmentSystem.TryGetSlot(slotIndex, out BattleEquipmentSlot target))
            return false;

        if (handIsChosenReward && target.equipment == chosenReward)
        {
            if (target.grade >= 3 || !equipmentSystem.PlaceIntoSlot(slotIndex, chosenReward))
                return false;

            hand.Clear();
            handIsChosenReward = false;
            chosenRewardCommitted = true;
            chosenRewardSlot = slotIndex;
            chosenRewardSlotRef = target;
            RaiseChanged();
            return true;
        }

        bool incomingWasChosenReward = handIsChosenReward;
        bool outgoingWasChosenReward = chosenRewardCommitted &&
                                       chosenRewardSlotRef != null &&
                                       ReferenceEquals(target, chosenRewardSlotRef);

        BattleEquipmentStack nextHand = hand;
        if (!equipmentSystem.ExchangeWithSlot(slotIndex, ref nextHand))
            return false;

        hand = nextHand;
        handIsChosenReward = outgoingWasChosenReward && !hand.IsEmpty;

        if (incomingWasChosenReward)
        {
            chosenRewardCommitted = true;
            chosenRewardSlot = slotIndex;
            equipmentSystem.TryGetSlot(slotIndex, out chosenRewardSlotRef);
        }
        else if (outgoingWasChosenReward)
        {
            chosenRewardCommitted = false;
            chosenRewardSlot = -1;
            chosenRewardSlotRef = null;
        }

        SyncChosenRewardLocation(false);
        RaiseChanged();
        return true;
    }

    public bool DiscardHand()
    {
        if (phase != BattleRewardPhase.PackEditing || !HasHand)
            return false;

        bool discardedChosenReward = handIsChosenReward;
        hand.Clear();
        handIsChosenReward = false;

        if (discardedChosenReward)
        {
            chosenRewardCommitted = false;
            chosenRewardSlot = -1;
            chosenRewardSlotRef = null;
        }

        RaiseChanged();
        return true;
    }

    /// <summary>
    /// SwapSlots는 BattleEquipmentSlot 객체 자체를 이동시키므로 처음 Reward가 들어간 Slot reference를 추적합니다.
    /// 슬롯이 이동해도 선택 Reward의 실제 위치를 유지하고, 삭제/교체되면 committed 상태를 해제합니다.
    /// </summary>
    public void SyncChosenRewardLocation()
    {
        SyncChosenRewardLocation(true);
    }

    private void SyncChosenRewardLocation(bool notify)
    {
        if (!chosenRewardCommitted || chosenRewardSlotRef == null || equipmentSystem == null)
            return;

        int found = -1;
        for (int i = 0; i < equipmentSystem.Slots.Count && i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            if (ReferenceEquals(equipmentSystem.Slots[i], chosenRewardSlotRef))
            {
                found = i;
                break;
            }
        }

        if (found >= 0 && chosenRewardSlotRef.equipment == chosenReward)
        {
            if (chosenRewardSlot != found)
            {
                chosenRewardSlot = found;
                if (notify)
                    RaiseChanged();
            }
            return;
        }

        chosenRewardCommitted = false;
        chosenRewardSlot = -1;
        chosenRewardSlotRef = null;
        if (notify)
            RaiseChanged();
    }

    /// <summary>
    /// PACK 편집을 끝내고 현재 Reward 결정을 Run 진행에 반영합니다.
    /// 선택 Reward가 PACK에 남아 있으면 획득 완료, 남아 있지 않으면 포기로 처리합니다.
    /// </summary>
    public bool CompleteReward()
    {
        RefreshFromRunState();
        if (runManager == null || !CanComplete)
            return false;

        SyncChosenRewardLocation();
        if (!CanComplete)
            return false;

        if (!chosenRewardCommitted)
            return SkipReward();

        if (chosenReward == null)
            return false;

        runManager.CompleteRewardSelection(chosenReward);
        return true;
    }

    /// <summary>
    /// Choice 화면에서 보상을 포기하는 기존 Run 흐름입니다.
    /// PACK 편집 중에는 Hand가 비어 있고 선택 Reward도 PACK에 남아 있지 않을 때만 사용합니다.
    /// </summary>
    public bool SkipReward()
    {
        RefreshFromRunState();
        if (runManager == null || phase == BattleRewardPhase.Inactive)
            return false;

        if (phase == BattleRewardPhase.PackEditing && (HasHand || chosenRewardCommitted))
            return false;

        runManager.SkipReward();
        return true;
    }

    private void ResetState(bool notify)
    {
        phase = BattleRewardPhase.Inactive;
        selectedChoiceIndex = -1;
        chosenReward = null;
        chosenRewardCommitted = false;
        chosenRewardSlot = -1;
        chosenRewardSlotRef = null;
        hand.Clear();
        handIsChosenReward = false;

        if (notify)
            RaiseChanged();
    }

    private void RaiseChanged()
    {
        Changed?.Invoke();
    }
}

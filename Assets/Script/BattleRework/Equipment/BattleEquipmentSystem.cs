using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 3x3 전투 장비 인벤토리의 authoritative data owner입니다.
///
/// 책임:
/// - 슬롯 잠금/해금
/// - 장비 배치/교환/삭제/합성
/// - 현재 장착 슬롯
/// - PlayerShootingSystem과의 무기 등록/장착 동기화
///
/// UI 선택, Hover, RectTransform, Reward 화면 상태는 이 클래스가 소유하지 않습니다.
/// </summary>
public class BattleEquipmentSystem : MonoBehaviour
{
    public const int MaxSlotCount = 9;
    public const int GridSize = 3;

    [SerializeField] private PlayerShootingSystem shootingSystem;

    [Header("Persistent Capacity")]
    [SerializeField, Range(1, MaxSlotCount)] private int unlockedSlotCount = 2;

    [Header("Run Start")]
    [Tooltip("런 시작 시 다시 지급되는 기본 장비입니다. 인런에서 얻은 장비는 다음 런으로 이월하지 않습니다.")]
    [SerializeField] private List<BattleEquipmentSO> startingEquipment = new();

    [Header("Runtime Slots - 3x3 Grid")]
    [Tooltip("0~8 슬롯은 좌상단부터 우하단까지 3x3 공간 인벤토리로 해석됩니다.")]
    [SerializeField] private BattleEquipmentSlot[] slots = new BattleEquipmentSlot[MaxSlotCount];

    [Header("Legacy Input")]
    [Tooltip("기존 1~9 숫자키 직접 장착 방식. 새 Tab/LB Grid Switch UI가 기본 입력이므로 기본값은 끕니다.")]
    [SerializeField] private bool enableNumberKeyEquip;

    // 같은 shootingData를 여러 슬롯이 공유할 수 있으므로 currentWeaponSO 비교만으로
    // 장착 슬롯을 역추적하지 않습니다. 슬롯 번호 자체를 이 시스템이 authoritative하게 소유합니다.
    [SerializeField, HideInInspector] private int equippedSlotIndex = -1;

    private static readonly KeyCode[] SlotKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,
        KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
        KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9
    };

    public IReadOnlyList<BattleEquipmentSlot> Slots => slots;
    public IReadOnlyList<BattleEquipmentSO> StartingEquipment => startingEquipment;
    public int UnlockedSlotCount => unlockedSlotCount;
    public int EquippedSlotIndex
    {
        get
        {
            EnsureSlots();
            return IsEquippableSlot(equippedSlotIndex) ? equippedSlotIndex : -1;
        }
    }

    public bool LegacyNumberKeyEquipEnabled
    {
        get => enableNumberKeyEquip;
        set => enableNumberKeyEquip = value;
    }

    public event Action InventoryChanged;
    public event Action<int> SlotCapacityChanged;
    public event Action<int> EquippedSlotChanged;

    private void Awake()
    {
        EnsureSlots();
        BootstrapEquippedSlotFromShootingSystem();
    }

    private void Update()
    {
        if (!enableNumberKeyEquip)
            return;

        int count = Mathf.Min(unlockedSlotCount, SlotKeys.Length);
        for (int i = 0; i < count; i++)
        {
            if (Input.GetKeyDown(SlotKeys[i]))
                EquipSlot(i);
        }
    }

    public bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();

        if (shootingSystem == null)
            errors.Add("shootingSystem is null");

        if (startingEquipment == null || startingEquipment.Count == 0)
        {
            errors.Add("startingEquipment is empty. Add at least one starter weapon for the vertical slice test.");
        }
        else
        {
            if (startingEquipment.Count > unlockedSlotCount)
                errors.Add("startingEquipment count is greater than unlockedSlotCount.");

            bool hasWeapon = false;
            for (int i = 0; i < startingEquipment.Count; i++)
            {
                BattleEquipmentSO equipment = startingEquipment[i];
                if (equipment == null)
                {
                    errors.Add($"startingEquipment[{i}] is null");
                    continue;
                }

                if (equipment.shootingData != null)
                    hasWeapon = true;
            }

            if (!hasWeapon)
                errors.Add("startingEquipment has no weapon with PlayerShootingSO.");
        }

        report = string.Join("\n", errors);
        return errors.Count == 0;
    }

    private void EnsureSlots()
    {
        if (slots == null || slots.Length != MaxSlotCount)
            slots = new BattleEquipmentSlot[MaxSlotCount];

        for (int i = 0; i < slots.Length; i++)
            slots[i] ??= new BattleEquipmentSlot();

        unlockedSlotCount = Mathf.Clamp(unlockedSlotCount, 1, MaxSlotCount);

        if (equippedSlotIndex >= unlockedSlotCount || equippedSlotIndex >= MaxSlotCount)
            equippedSlotIndex = -1;
    }

    /// <summary>
    /// 구형 씬/직렬화 상태가 currentWeaponSO만 알고 있을 때 최초 1회 슬롯 번호를 복원합니다.
    /// 동일 shootingData가 여러 슬롯에 있으면 첫 번째 일치 슬롯을 사용하며,
    /// 이후에는 equippedSlotIndex가 단독 authoritative state입니다.
    /// </summary>
    private void BootstrapEquippedSlotFromShootingSystem()
    {
        if (equippedSlotIndex >= 0 || shootingSystem == null || shootingSystem.currentWeaponSO == null)
            return;

        for (int i = 0; i < unlockedSlotCount; i++)
        {
            BattleEquipmentSO equipment = slots[i].equipment;
            if (equipment != null && equipment.shootingData == shootingSystem.currentWeaponSO)
            {
                equippedSlotIndex = i;
                return;
            }
        }
    }

    public bool IsSlotUnlocked(int index)
    {
        EnsureSlots();
        return IsUnlockedIndex(index);
    }

    public bool TryGetSlot(int index, out BattleEquipmentSlot slot)
    {
        EnsureSlots();
        if (!IsUnlockedIndex(index))
        {
            slot = null;
            return false;
        }

        slot = slots[index];
        return slot != null;
    }

    public static Vector2Int SlotIndexToGrid(int index)
    {
        index = Mathf.Clamp(index, 0, MaxSlotCount - 1);
        return new Vector2Int(index % GridSize, index / GridSize);
    }

    public static int GridToSlotIndex(int x, int y)
    {
        if (x < 0 || y < 0 || x >= GridSize || y >= GridSize)
            return -1;
        return y * GridSize + x;
    }

    /// <summary>
    /// 현재 장착 슬롯을 기준으로 다음/이전 Manual Weapon 슬롯을 찾습니다.
    /// Tab/LB 짧게 탭했을 때 패드 친화적인 순환 전환에 사용합니다.
    /// </summary>
    public int FindNextWeaponSlot(int fromIndex, int direction = 1)
    {
        EnsureSlots();
        if (unlockedSlotCount <= 0)
            return -1;

        int step = direction < 0 ? -1 : 1;
        int start = fromIndex >= 0 && fromIndex < unlockedSlotCount ? fromIndex : (step > 0 ? -1 : 0);
        for (int offset = 1; offset <= unlockedSlotCount; offset++)
        {
            int candidate = (start + offset * step) % unlockedSlotCount;
            if (candidate < 0)
                candidate += unlockedSlotCount;

            if (IsEquippableSlot(candidate))
                return candidate;
        }

        return -1;
    }

    /// <summary>
    /// 3x3 공간 시너지 빌드 편집용 슬롯 교환입니다.
    /// BattleEquipmentSlot 객체 자체를 교환하며 장착 상태도 슬롯과 함께 이동합니다.
    /// </summary>
    public bool SwapSlots(int firstIndex, int secondIndex)
    {
        EnsureSlots();
        if (!IsUnlockedIndex(firstIndex) || !IsUnlockedIndex(secondIndex))
            return false;
        if (firstIndex == secondIndex)
            return true;

        BattleEquipmentSlot temp = slots[firstIndex];
        slots[firstIndex] = slots[secondIndex];
        slots[secondIndex] = temp;

        if (equippedSlotIndex == firstIndex)
            equippedSlotIndex = secondIndex;
        else if (equippedSlotIndex == secondIndex)
            equippedSlotIndex = firstIndex;

        InventoryChanged?.Invoke();
        EquippedSlotChanged?.Invoke(EquippedSlotIndex);
        return true;
    }

    /// <summary>
    /// PACK 밖의 임시 Hand stack과 지정 슬롯을 교환합니다.
    /// Reward UI가 슬롯 필드를 직접 복사/수정하지 않고 이 API 하나로 교환하도록 하기 위한 공용 경로입니다.
    /// </summary>
    public bool ExchangeWithSlot(int index, ref BattleEquipmentStack hand)
    {
        EnsureSlots();
        if (!IsUnlockedIndex(index))
            return false;

        BattleEquipmentSlot target = slots[index];
        if (target == null)
            return false;

        BattleEquipmentStack outgoing = target.ToStack();
        if (hand.IsEmpty && outgoing.IsEmpty)
            return false;

        BattleEquipmentSO oldEquipment = target.equipment;
        bool oldWasEquipped = equippedSlotIndex == index;

        target.Apply(hand);
        hand = outgoing;

        UnregisterWeaponIfUnused(oldEquipment);

        if (oldWasEquipped)
        {
            if (IsEquippableSlot(index))
                EquipSlot(index);
            else
                EquipFirstAvailableWeapon();
        }

        InventoryChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 로그라이트 런 시작 처리.
    /// 영구 성장으로 열린 슬롯 수는 유지하고, 이전 런의 인런 장비만 비운 뒤 시작 장비를 다시 지급합니다.
    /// </summary>
    public void ResetForRun()
    {
        EnsureSlots();
        equippedSlotIndex = -1;
        shootingSystem?.ResetRuntimeWeapons();

        for (int i = 0; i < slots.Length; i++)
            slots[i].Clear();

        if (startingEquipment != null)
        {
            for (int i = 0; i < startingEquipment.Count; i++)
            {
                BattleEquipmentSO equipment = startingEquipment[i];
                if (equipment == null) continue;
                TryAcquireInternal(equipment, false);
            }
        }

        EquipFirstAvailableWeapon();
        InventoryChanged?.Invoke();
    }

    public bool TryAcquire(BattleEquipmentSO equipment)
    {
        return TryAcquireInternal(equipment, true);
    }

    private bool TryAcquireInternal(BattleEquipmentSO equipment, bool notify)
    {
        if (equipment == null)
            return false;

        EnsureSlots();

        if (TryMerge(equipment))
        {
            if (notify) InventoryChanged?.Invoke();
            return true;
        }

        int empty = FindFirstEmptyUnlockedSlot();
        if (empty < 0)
            return false;

        slots[empty].Set(equipment, 1, 1);

        if (notify) InventoryChanged?.Invoke();
        return true;
    }

    private bool TryMerge(BattleEquipmentSO equipment)
    {
        for (int i = 0; i < unlockedSlotCount; i++)
        {
            BattleEquipmentSlot slot = slots[i];
            if (slot.equipment != equipment || slot.grade >= 3)
                continue;

            MergeCopyIntoSlot(slot);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reward UI처럼 사용자가 정확한 목적 슬롯을 지정하는 획득 경로입니다.
    /// - 빈 슬롯: 해당 슬롯에 배치
    /// - 같은 장비: 해당 슬롯에 복사본을 합쳐 Grade 진행
    /// - 다른 장비: 기존 장비를 버리고 교체
    /// - Grade 3인 같은 장비: 더 합칠 수 없으므로 실패
    /// </summary>
    public bool PlaceIntoSlot(int index, BattleEquipmentSO equipment)
    {
        EnsureSlots();

        if (!IsUnlockedIndex(index) || equipment == null)
            return false;

        BattleEquipmentSlot target = slots[index];
        if (target.equipment == equipment)
        {
            if (target.grade >= 3)
                return false;

            MergeCopyIntoSlot(target);
            InventoryChanged?.Invoke();
            return true;
        }

        return ReplaceSlot(index, equipment);
    }

    public bool CanPlaceIntoSlot(int index, BattleEquipmentSO equipment)
    {
        EnsureSlots();
        if (!IsUnlockedIndex(index) || equipment == null)
            return false;

        BattleEquipmentSlot target = slots[index];
        return target.equipment != equipment || target.grade < 3;
    }

    private static void MergeCopyIntoSlot(BattleEquipmentSlot slot)
    {
        if (slot == null)
            return;

        slot.copies++;
        if (slot.copies >= 3)
        {
            slot.grade = Mathf.Min(3, slot.grade + 1);
            slot.copies = 1;
        }
    }

    public bool ReplaceSlot(int index, BattleEquipmentSO equipment)
    {
        return ReplaceSlot(index, BattleEquipmentStack.Create(equipment));
    }

    public bool ReplaceSlot(int index, BattleEquipmentStack incoming)
    {
        EnsureSlots();

        if (!IsUnlockedIndex(index) || incoming.IsEmpty)
            return false;

        BattleEquipmentSlot target = slots[index];
        BattleEquipmentSO old = target.equipment;
        bool oldWasEquipped = equippedSlotIndex == index;

        target.Apply(incoming);
        UnregisterWeaponIfUnused(old);

        if (oldWasEquipped)
        {
            if (IsEquippableSlot(index))
                EquipSlot(index);
            else
                EquipFirstAvailableWeapon();
        }

        InventoryChanged?.Invoke();
        return true;
    }

    public bool DiscardSlot(int index)
    {
        EnsureSlots();

        if (!IsUnlockedIndex(index))
            return false;

        BattleEquipmentSlot target = slots[index];
        if (target == null || target.equipment == null)
            return false;

        BattleEquipmentSO old = target.equipment;
        bool oldWasEquipped = equippedSlotIndex == index;

        target.Clear();
        UnregisterWeaponIfUnused(old);

        if (oldWasEquipped)
            EquipFirstAvailableWeapon();

        InventoryChanged?.Invoke();
        return true;
    }

    public bool EquipSlot(int index)
    {
        EnsureSlots();

        if (!IsEquippableSlot(index) || shootingSystem == null)
            return false;

        BattleEquipmentSlot slot = slots[index];
        bool equipped = shootingSystem.RegisterWeaponAndEquip(slot.equipment.shootingData);
        if (!equipped)
            return false;

        equippedSlotIndex = index;

        // GridSynergyController가 존재하면 기존 Tag 시너지 + 3x3 인접 시너지를 이후 합산합니다.
        // 컨트롤러가 없는 씬에서도 기존 장비 배율은 그대로 동작합니다.
        shootingSystem.RuntimeDamageMultiplier = Mathf.Max(0f, slot.equipment.damageMultiplier);
        EquippedSlotChanged?.Invoke(equippedSlotIndex);
        return true;
    }

    public bool IsSlotEquipped(int index)
    {
        EnsureSlots();
        return index >= 0 && index == EquippedSlotIndex;
    }

    private bool IsEquippableSlot(int index)
    {
        return IsUnlockedIndex(index) &&
               slots[index] != null &&
               slots[index].equipment != null &&
               slots[index].equipment.shootingData != null;
    }

    private void EquipFirstAvailableWeapon()
    {
        for (int i = 0; i < unlockedSlotCount; i++)
        {
            if (IsEquippableSlot(i) && EquipSlot(i))
                return;
        }

        equippedSlotIndex = -1;
        if (shootingSystem != null)
            shootingSystem.RuntimeDamageMultiplier = 1f;
        EquippedSlotChanged?.Invoke(-1);
    }

    private void UnregisterWeaponIfUnused(BattleEquipmentSO removed)
    {
        if (removed == null || removed.shootingData == null || shootingSystem == null)
            return;

        for (int i = 0; i < unlockedSlotCount; i++)
        {
            BattleEquipmentSO remaining = slots[i].equipment;
            if (remaining != null && remaining.shootingData == removed.shootingData)
                return;
        }

        shootingSystem.UnregisterWeapon(removed.shootingData);
    }

    public void SetUnlockedSlotCount(int count)
    {
        EnsureSlots();

        int next = Mathf.Clamp(count, 1, MaxSlotCount);
        if (next == unlockedSlotCount)
            return;

        unlockedSlotCount = next;

        if (!IsEquippableSlot(equippedSlotIndex))
            EquipFirstAvailableWeapon();

        SlotCapacityChanged?.Invoke(unlockedSlotCount);
        InventoryChanged?.Invoke();
    }

    public void UnlockSlots(int amount = 1)
    {
        if (amount <= 0) return;
        SetUnlockedSlotCount(unlockedSlotCount + amount);
    }

    public int CountTag(EquipmentTag tag)
    {
        EnsureSlots();

        int count = 0;
        for (int i = 0; i < unlockedSlotCount; i++)
        {
            BattleEquipmentSlot slot = slots[i];
            if (slot.equipment != null && slot.equipment.HasTag(tag))
                count++;
        }

        return count;
    }

    public bool HasFreeUnlockedSlot()
    {
        return FindFirstEmptyUnlockedSlot() >= 0;
    }

    public int FindFirstEmptyUnlockedSlot()
    {
        EnsureSlots();

        for (int i = 0; i < unlockedSlotCount; i++)
        {
            if (slots[i].equipment == null)
                return i;
        }

        return -1;
    }

    private bool IsUnlockedIndex(int index)
    {
        return index >= 0 && index < unlockedSlotCount && index < MaxSlotCount;
    }
}

/// <summary>
/// PACK 밖에서 잠시 들고 있는 장비 묶음(Reward Hand 등)을 위한 값 객체입니다.
/// MonoBehaviour나 별도 .cs를 만들지 않고 Equipment domain 안에서만 재사용합니다.
/// </summary>
[Serializable]
public struct BattleEquipmentStack
{
    public BattleEquipmentSO equipment;
    [Range(1, 3)] public int grade;
    [Range(1, 2)] public int copies;

    public bool IsEmpty => equipment == null;

    public static BattleEquipmentStack Create(BattleEquipmentSO equipment, int grade = 1, int copies = 1)
    {
        return new BattleEquipmentStack
        {
            equipment = equipment,
            grade = Mathf.Clamp(grade, 1, 3),
            copies = Mathf.Clamp(copies, 1, 2)
        };
    }

    public void Clear()
    {
        equipment = null;
        grade = 1;
        copies = 1;
    }
}

[Serializable]
public class BattleEquipmentSlot
{
    public BattleEquipmentSO equipment;
    [Range(1, 3)] public int grade = 1;
    [Range(1, 2)] public int copies = 1;

    public BattleEquipmentStack ToStack()
    {
        return equipment == null
            ? default
            : BattleEquipmentStack.Create(equipment, grade, copies);
    }

    public void Apply(BattleEquipmentStack stack)
    {
        if (stack.IsEmpty)
        {
            Clear();
            return;
        }

        Set(stack.equipment, stack.grade, stack.copies);
    }

    public void Set(BattleEquipmentSO nextEquipment, int nextGrade = 1, int nextCopies = 1)
    {
        equipment = nextEquipment;
        grade = Mathf.Clamp(nextGrade, 1, 3);
        copies = Mathf.Clamp(nextCopies, 1, 2);

        if (equipment == null)
        {
            grade = 1;
            copies = 1;
        }
    }

    public void Clear()
    {
        equipment = null;
        grade = 1;
        copies = 1;
    }
}

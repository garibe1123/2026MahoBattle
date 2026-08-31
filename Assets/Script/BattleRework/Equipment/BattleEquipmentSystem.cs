using System;
using System.Collections.Generic;
using UnityEngine;

public class BattleEquipmentSystem : MonoBehaviour
{
    public const int MaxSlotCount = 9;

    [SerializeField] private PlayerShootingSystem shootingSystem;
    [SerializeField, Range(1, MaxSlotCount)] private int unlockedSlotCount = 2;
    [SerializeField] private BattleEquipmentSlot[] slots = new BattleEquipmentSlot[MaxSlotCount];

    public IReadOnlyList<BattleEquipmentSlot> Slots => slots;
    public int UnlockedSlotCount => unlockedSlotCount;

    public event Action InventoryChanged;
    public event Action<int> SlotCapacityChanged;

    private void Awake()
    {
        EnsureSlots();
    }

    private void EnsureSlots()
    {
        if (slots == null || slots.Length != MaxSlotCount)
            slots = new BattleEquipmentSlot[MaxSlotCount];

        for (int i = 0; i < slots.Length; i++)
            slots[i] ??= new BattleEquipmentSlot();

        unlockedSlotCount = Mathf.Clamp(unlockedSlotCount, 1, MaxSlotCount);
    }

    public bool TryAcquire(BattleEquipmentSO equipment)
    {
        if (equipment == null)
            return false;

        EnsureSlots();

        if (TryMerge(equipment))
        {
            InventoryChanged?.Invoke();
            return true;
        }

        int empty = FindEmptyUnlockedSlot();
        if (empty < 0)
            return false;

        slots[empty].equipment = equipment;
        slots[empty].grade = 1;
        slots[empty].copies = 1;
        InventoryChanged?.Invoke();
        return true;
    }

    private bool TryMerge(BattleEquipmentSO equipment)
    {
        for (int i = 0; i < unlockedSlotCount; i++)
        {
            BattleEquipmentSlot slot = slots[i];
            if (slot.equipment != equipment || slot.grade >= 3)
                continue;

            slot.copies++;
            if (slot.copies >= 3)
            {
                slot.grade++;
                slot.copies = 1;
            }

            return true;
        }

        return false;
    }

    public bool ReplaceSlot(int index, BattleEquipmentSO equipment)
    {
        EnsureSlots();

        if (!IsUnlockedIndex(index) || equipment == null)
            return false;

        slots[index].equipment = equipment;
        slots[index].grade = 1;
        slots[index].copies = 1;
        InventoryChanged?.Invoke();
        return true;
    }

    public bool DiscardSlot(int index)
    {
        EnsureSlots();

        if (!IsUnlockedIndex(index))
            return false;

        slots[index].Clear();
        InventoryChanged?.Invoke();
        return true;
    }

    public bool EquipSlot(int index)
    {
        EnsureSlots();

        if (!IsUnlockedIndex(index) || shootingSystem == null)
            return false;

        BattleEquipmentSlot slot = slots[index];
        if (slot.equipment == null || slot.equipment.shootingData == null)
            return false;

        bool equipped = shootingSystem.RegisterWeaponAndEquip(slot.equipment.shootingData);
        if (!equipped)
            return false;

        // 1차 수직 슬라이스에서는 현재 장비의 Damage Multiplier만 실제 사격에 연결합니다.
        // 여러 장비 동시 발동/Synergy 합산은 이후 패스에서 별도 Build 계산 계층으로 확장합니다.
        shootingSystem.RuntimeDamageMultiplier = Mathf.Max(0f, slot.equipment.damageMultiplier);
        return true;
    }

    public void SetUnlockedSlotCount(int count)
    {
        EnsureSlots();

        int next = Mathf.Clamp(count, 1, MaxSlotCount);
        if (next == unlockedSlotCount)
            return;

        unlockedSlotCount = next;
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
        return FindEmptyUnlockedSlot() >= 0;
    }

    private int FindEmptyUnlockedSlot()
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

[Serializable]
public class BattleEquipmentSlot
{
    public BattleEquipmentSO equipment;
    [Range(1, 3)] public int grade = 1;
    [Range(1, 2)] public int copies = 1;

    public void Clear()
    {
        equipment = null;
        grade = 1;
        copies = 1;
    }
}

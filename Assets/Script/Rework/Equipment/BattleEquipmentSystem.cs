using System;
using System.Collections.Generic;
using UnityEngine;

public class BattleEquipmentSystem : MonoBehaviour
{
    public const int SlotCount = 9;

    [SerializeField] private PlayerShootingSystem shootingSystem;
    [SerializeField] private BattleEquipmentSlot[] slots = new BattleEquipmentSlot[SlotCount];

    public IReadOnlyList<BattleEquipmentSlot> Slots => slots;
    public event Action InventoryChanged;

    private void Awake()
    {
        if (slots == null || slots.Length != SlotCount)
            slots = new BattleEquipmentSlot[SlotCount];

        for (int i = 0; i < slots.Length; i++)
            slots[i] ??= new BattleEquipmentSlot();
    }

    public bool TryAcquire(BattleEquipmentSO equipment)
    {
        if (equipment == null) return false;

        if (TryMerge(equipment))
        {
            InventoryChanged?.Invoke();
            return true;
        }

        int empty = FindEmptySlot();
        if (empty < 0) return false;

        slots[empty].equipment = equipment;
        slots[empty].grade = 1;
        slots[empty].copies = 1;
        InventoryChanged?.Invoke();
        return true;
    }

    private bool TryMerge(BattleEquipmentSO equipment)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            BattleEquipmentSlot slot = slots[i];
            if (slot.equipment != equipment || slot.grade >= 3) continue;

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

    public void ReplaceSlot(int index, BattleEquipmentSO equipment)
    {
        if (index < 0 || index >= slots.Length || equipment == null) return;
        slots[index].equipment = equipment;
        slots[index].grade = 1;
        slots[index].copies = 1;
        InventoryChanged?.Invoke();
    }

    public void DiscardSlot(int index)
    {
        if (index < 0 || index >= slots.Length) return;
        slots[index].Clear();
        InventoryChanged?.Invoke();
    }

    public void EquipSlot(int index)
    {
        if (index < 0 || index >= slots.Length) return;
        BattleEquipmentSlot slot = slots[index];
        if (slot.equipment == null || slot.equipment.shootingData == null || shootingSystem == null) return;

        int weaponIndex = shootingSystem.unlockedWeapons.IndexOf(slot.equipment.shootingData);
        if (weaponIndex < 0)
        {
            shootingSystem.unlockedWeapons.Add(slot.equipment.shootingData);
            weaponIndex = shootingSystem.unlockedWeapons.Count - 1;
        }

        shootingSystem.EquipWeapon(weaponIndex);
    }

    private int FindEmptySlot()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].equipment == null) return i;
        }
        return -1;
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

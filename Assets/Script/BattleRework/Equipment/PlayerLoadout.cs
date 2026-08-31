using System.Collections.Generic;
using UnityEngine;

public class PlayerLoadout : MonoBehaviour
{
    [SerializeField] private CoreDefinitionSO mainCore;
    [SerializeField] private List<CoreDefinitionSO> subCores = new();
    [SerializeField, Range(1, 6)] private int unlockedSubCoreSlots = 1;

    public CoreDefinitionSO MainCore => mainCore;
    public IReadOnlyList<CoreDefinitionSO> SubCores => subCores;

    public void SetMainCore(CoreDefinitionSO core)
    {
        mainCore = core;
    }

    public bool TryAddSubCore(CoreDefinitionSO core)
    {
        if (core == null || subCores.Count >= unlockedSubCoreSlots) return false;
        if (subCores.Contains(core)) return false;
        subCores.Add(core);
        return true;
    }

    public void RemoveSubCore(CoreDefinitionSO core)
    {
        subCores.Remove(core);
    }

    public float GetDamageMultiplier()
    {
        float value = mainCore != null ? mainCore.damageMultiplier : 1f;
        foreach (CoreDefinitionSO core in subCores)
            if (core != null) value *= core.damageMultiplier;
        return value;
    }

    public float GetMoveSpeedMultiplier()
    {
        float value = mainCore != null ? mainCore.moveSpeedMultiplier : 1f;
        foreach (CoreDefinitionSO core in subCores)
            if (core != null) value *= core.moveSpeedMultiplier;
        return value;
    }
}

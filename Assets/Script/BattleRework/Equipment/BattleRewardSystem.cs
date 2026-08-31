using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Room Clear 시 제시할 전투장비 후보를 생성합니다.
/// 촬영 스테이지는 장비를 강제하지 않고 ShootingThemeSO의 Tag Weight로 확률만 보정합니다.
/// </summary>
public class BattleRewardSystem : MonoBehaviour
{
    [SerializeField] private List<BattleEquipmentSO> rewardPool = new();
    [SerializeField, Range(1, 5)] private int choiceCount = 3;
    [SerializeField, Min(0)] private int currentMetaLevel;

    public int CurrentMetaLevel => currentMetaLevel;

    public bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();

        if (rewardPool == null || rewardPool.Count == 0)
            errors.Add("rewardPool is empty");
        else
        {
            bool hasValid = false;
            for (int i = 0; i < rewardPool.Count; i++)
            {
                if (rewardPool[i] != null)
                {
                    hasValid = true;
                    break;
                }
            }

            if (!hasValid)
                errors.Add("rewardPool contains no valid BattleEquipmentSO");
        }

        report = string.Join("\n", errors);
        return errors.Count == 0;
    }

    public void SetMetaLevel(int level)
    {
        currentMetaLevel = Mathf.Max(0, level);
    }

    public List<BattleEquipmentSO> GenerateChoices(ShootingThemeSO theme)
    {
        List<BattleEquipmentSO> candidates = new();

        for (int i = 0; i < rewardPool.Count; i++)
        {
            BattleEquipmentSO equipment = rewardPool[i];
            if (equipment == null) continue;
            if (equipment.unlockLevel > currentMetaLevel) continue;
            if (candidates.Contains(equipment)) continue;
            candidates.Add(equipment);
        }

        List<BattleEquipmentSO> result = new();
        int targetCount = Mathf.Min(Mathf.Max(1, choiceCount), candidates.Count);

        while (result.Count < targetCount && candidates.Count > 0)
        {
            int selectedIndex = PickWeightedIndex(candidates, theme);
            if (selectedIndex < 0 || selectedIndex >= candidates.Count)
                break;

            result.Add(candidates[selectedIndex]);
            candidates.RemoveAt(selectedIndex);
        }

        return result;
    }

    private int PickWeightedIndex(List<BattleEquipmentSO> candidates, ShootingThemeSO theme)
    {
        if (candidates == null || candidates.Count == 0)
            return -1;

        float total = 0f;
        for (int i = 0; i < candidates.Count; i++)
            total += GetWeight(candidates[i], theme);

        if (total <= 0f)
            return Random.Range(0, candidates.Count);

        float roll = Random.value * total;
        float cursor = 0f;

        for (int i = 0; i < candidates.Count; i++)
        {
            cursor += GetWeight(candidates[i], theme);
            if (roll <= cursor)
                return i;
        }

        return candidates.Count - 1;
    }

    private static float GetWeight(BattleEquipmentSO equipment, ShootingThemeSO theme)
    {
        if (equipment == null)
            return 0f;

        return theme != null
            ? theme.GetRewardWeight(equipment)
            : Mathf.Max(0.01f, equipment.baseRewardWeight);
    }
}

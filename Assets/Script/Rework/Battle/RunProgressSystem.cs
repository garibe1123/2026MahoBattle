using System;
using UnityEngine;

public class RunProgressSystem : MonoBehaviour
{
    [Header("Runtime Progress")]
    [SerializeField] private int popularity;
    [SerializeField] private int fanPoints;
    [SerializeField] private int monsterKillPoints;

    public int Popularity => popularity;
    public int FanPoints => fanPoints;
    public int MonsterKillPoints => monsterKillPoints;
    public VillainGrade VillainGrade => CalculateVillainGrade(popularity);

    public event Action<int> PopularityChanged;
    public event Action<int> FanPointsChanged;
    public event Action<int> MonsterKillPointsChanged;
    public event Action<VillainGrade> VillainGradeChanged;

    private VillainGrade lastGrade = VillainGrade.C;

    public void ResetForRun()
    {
        popularity = 0;
        monsterKillPoints = 0;
        lastGrade = VillainGrade.C;
        PopularityChanged?.Invoke(popularity);
        MonsterKillPointsChanged?.Invoke(monsterKillPoints);
        VillainGradeChanged?.Invoke(lastGrade);
    }

    public void AddPopularity(int amount)
    {
        popularity = Mathf.Max(0, popularity + amount);
        PopularityChanged?.Invoke(popularity);
        RefreshGrade();
    }

    public void AddFanPoints(int amount)
    {
        fanPoints = Mathf.Max(0, fanPoints + amount);
        FanPointsChanged?.Invoke(fanPoints);
    }

    public void AddMonsterKillPoints(int amount)
    {
        monsterKillPoints = Mathf.Max(0, monsterKillPoints + amount);
        MonsterKillPointsChanged?.Invoke(monsterKillPoints);
    }

    public bool SpendMonsterKillPoints(int amount)
    {
        if (amount <= 0 || monsterKillPoints < amount) return false;
        monsterKillPoints -= amount;
        MonsterKillPointsChanged?.Invoke(monsterKillPoints);
        return true;
    }

    private void RefreshGrade()
    {
        VillainGrade current = CalculateVillainGrade(popularity);
        if (current == lastGrade) return;
        lastGrade = current;
        VillainGradeChanged?.Invoke(current);
    }

    public static VillainGrade CalculateVillainGrade(int value)
    {
        if (value >= 6000) return VillainGrade.S;
        if (value >= 3000) return VillainGrade.A;
        if (value >= 1000) return VillainGrade.B;
        return VillainGrade.C;
    }
}

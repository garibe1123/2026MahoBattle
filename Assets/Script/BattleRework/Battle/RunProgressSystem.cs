using System;
using UnityEngine;

/// <summary>
/// 인런 진행 수치를 한 곳에서 보관합니다.
/// Popularity/FanPoint/MonsterKillPoint를 개별 Manager로 쪼개지 않고 런 상태로 묶습니다.
/// </summary>
public class RunProgressSystem : MonoBehaviour
{
    [SerializeField] private int popularity;
    [SerializeField] private int fanPoints;
    [SerializeField] private int monsterKillPoints;
    [SerializeField] private int viewers;
    [SerializeField] private int likes;

    public int Popularity => popularity;
    public int FanPoints => fanPoints;
    public int MonsterKillPoints => monsterKillPoints;
    public int Viewers => viewers;
    public int Likes => likes;
    public VillainGrade CurrentVillainGrade => EvaluateVillainGrade(popularity);

    public event Action<int> PopularityChanged;
    public event Action<int> FanPointsChanged;
    public event Action<int> MonsterKillPointsChanged;
    public event Action<VillainGrade> VillainGradeChanged;

    public void BeginRun()
    {
        popularity = Mathf.Max(0, popularity);
        monsterKillPoints = 0;
        MonsterKillPointsChanged?.Invoke(monsterKillPoints);
    }

    public void AddPopularity(int amount)
    {
        VillainGrade before = CurrentVillainGrade;
        popularity = Mathf.Max(0, popularity + amount);
        PopularityChanged?.Invoke(popularity);

        VillainGrade after = CurrentVillainGrade;
        if (before != after)
            VillainGradeChanged?.Invoke(after);
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

    public bool TrySpendMonsterKillPoints(int amount)
    {
        amount = Mathf.Max(0, amount);
        if (monsterKillPoints < amount) return false;

        monsterKillPoints -= amount;
        MonsterKillPointsChanged?.Invoke(monsterKillPoints);
        return true;
    }

    public void SetBroadcastMetrics(int currentViewers, int currentLikes)
    {
        viewers = Mathf.Max(0, currentViewers);
        likes = Mathf.Max(0, currentLikes);
    }

    public void EndRun()
    {
        // 몬스터 처치 포인트는 정상/사망/자진하차 모두 이월되지 않습니다.
        monsterKillPoints = 0;
        MonsterKillPointsChanged?.Invoke(monsterKillPoints);
    }

    public static VillainGrade EvaluateVillainGrade(int currentPopularity)
    {
        if (currentPopularity >= 6000) return VillainGrade.S;
        if (currentPopularity >= 3000) return VillainGrade.A;
        if (currentPopularity >= 1000) return VillainGrade.B;
        return VillainGrade.C;
    }
}

using TMPro;
using UnityEngine;

public class BattleHUD : MonoBehaviour
{
    [SerializeField] private RunProgressSystem runProgress;
    [SerializeField] private TextMeshProUGUI popularityText;
    [SerializeField] private TextMeshProUGUI fanPointText;
    [SerializeField] private TextMeshProUGUI monsterPointText;

    private void OnEnable()
    {
        if (runProgress == null) return;
        runProgress.PopularityChanged += RefreshPopularity;
        runProgress.FanPointsChanged += RefreshFanPoints;
        runProgress.MonsterKillPointsChanged += RefreshMonsterPoints;
        RefreshAll();
    }

    private void OnDisable()
    {
        if (runProgress == null) return;
        runProgress.PopularityChanged -= RefreshPopularity;
        runProgress.FanPointsChanged -= RefreshFanPoints;
        runProgress.MonsterKillPointsChanged -= RefreshMonsterPoints;
    }

    private void RefreshAll()
    {
        RefreshPopularity(runProgress.Popularity);
        RefreshFanPoints(runProgress.FanPoints);
        RefreshMonsterPoints(runProgress.MonsterKillPoints);
    }

    private void RefreshPopularity(int value)
    {
        if (popularityText != null) popularityText.text = $"인기도 {value}";
    }

    private void RefreshFanPoints(int value)
    {
        if (fanPointText != null) fanPointText.text = $"팬 {value}";
    }

    private void RefreshMonsterPoints(int value)
    {
        if (monsterPointText != null) monsterPointText.text = $"처치 포인트 {value}";
    }
}

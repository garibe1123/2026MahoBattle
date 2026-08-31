using TMPro;
using UnityEngine;

public class BattleResultUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI settlementText;

    public void Show(BattleRunEndReason reason, int popularity, int fanPoints, int income)
    {
        float multiplier = reason switch
        {
            BattleRunEndReason.BroadcastAccident => 0.5f,
            BattleRunEndReason.Quit => 0.7f,
            _ => 1f
        };

        if (panel != null) panel.SetActive(true);
        if (titleText != null)
        {
            titleText.text = reason switch
            {
                BattleRunEndReason.BroadcastAccident => "방송 사고",
                BattleRunEndReason.Quit => "자진 하차",
                _ => "방종!"
            };
        }

        if (settlementText != null)
        {
            int settledPopularity = Mathf.RoundToInt(popularity * multiplier);
            settlementText.text = $"인기도 정산 {settledPopularity}\n팬 포인트 {fanPoints}\n수익 {income}";
        }
    }
}

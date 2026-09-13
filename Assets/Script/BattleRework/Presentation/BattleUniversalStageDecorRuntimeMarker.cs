using UnityEngine;

/// <summary>
/// Universal Stage Decor의 런타임 Carrier 메타데이터입니다.
/// Presentation 보강(케이블/프레임/Auto Fit)이 이름 파싱에 의존하지 않도록 사용합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleUniversalStageDecorRuntimeMarker : MonoBehaviour
{
    [SerializeField] private BattleUniversalStageDecorCategory category = BattleUniversalStageDecorCategory.Misc;
    [SerializeField] private Vector2Int footprint = Vector2Int.one;
    [SerializeField] private string sourceLabel;

    public BattleUniversalStageDecorCategory Category => category;
    public Vector2Int Footprint => footprint;
    public string SourceLabel => sourceLabel;

    public void Configure(BattleUniversalStageDecorCategory sourceCategory, Vector2Int sourceFootprint, string label)
    {
        category = sourceCategory;
        footprint = BattleUniversalStageDecorEntry.ResolveFootprint(sourceFootprint);
        sourceLabel = label ?? string.Empty;
    }
}

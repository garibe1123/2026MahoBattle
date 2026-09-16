using UnityEngine;

/// <summary>
/// Battle World의 Sorting Order 단일 기준입니다.
///
/// 고정 Field 계층:
/// Lower Base < Base < Handle < Floor < World Actor/Object.
///
/// 이동하는 World Actor/Object는 발(정렬 Anchor)의 world Y를 기준으로 계산합니다.
/// Anchor가 world Y=0이면 Sorting Order도 정확히 0입니다.
/// 화면 아래(-Y)로 내려갈수록 더 큰 Order를 받아 앞에 렌더됩니다.
/// </summary>
public static class BattleWorldSorting
{
    public const int LowerBaseOrder = -12003;
    public const int BaseOrder = -12002;
    public const int HandleOrder = -12001;
    public const int FloorOrder = -12000;

    public const int ActorMinOrder = -10000;
    public const int ActorMaxOrder = 10000;
    public const float OrdersPerWorldUnit = 100f;

    public const int DebugOverlayOffset = 5;

    public static int WorldYToOrder(float anchorWorldY, int localOffset = 0)
    {
        int resolved = Mathf.RoundToInt(-anchorWorldY * OrdersPerWorldUnit) + localOffset;
        return Mathf.Clamp(resolved, ActorMinOrder, ActorMaxOrder);
    }
}

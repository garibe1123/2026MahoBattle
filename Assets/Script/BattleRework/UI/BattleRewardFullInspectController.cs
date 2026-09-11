using UnityEngine;

/// <summary>
/// Legacy compatibility shell.
///
/// Reward PACK 편집은 공용 LoadoutSwitchFull을 재사용하며,
/// Full Grid / Detail / Reward controls의 레이아웃과 Reward 편집 Bullet Time은
/// BattleUnifiedInventoryInspectController가 담당합니다.
/// 기존 Scene 직렬화 참조를 보존하기 위해 타입만 유지합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleRewardFullInspectController : MonoBehaviour
{
    private void Awake()
    {
        enabled = false;
    }
}

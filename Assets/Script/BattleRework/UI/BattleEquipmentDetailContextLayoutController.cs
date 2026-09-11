using UnityEngine;

/// <summary>
/// Legacy compatibility shell.
///
/// EquipmentDetailPanel의 위치/스케일/강조는 Phase 4부터
/// BattleUnifiedInventoryInspectController가 단독 소유합니다.
/// 기존 Scene 직렬화 참조를 보존하기 위해 타입만 유지합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleEquipmentDetailContextLayoutController : MonoBehaviour
{
    private void Awake()
    {
        enabled = false;
    }
}

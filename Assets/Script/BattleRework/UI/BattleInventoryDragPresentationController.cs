using UnityEngine;

/// <summary>
/// Legacy compatibility shell.
///
/// Phase 4부터 PACK / Grid / TRASH / DONE의 RectTransform 배치는
/// BattleUnifiedInventoryInspectController가 단독 소유합니다.
/// 기존 Scene에 직렬화된 컴포넌트 참조를 깨지 않기 위해 타입만 유지하며,
/// 새 Scene에는 별도 AutoInstaller로 추가하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleInventoryDragPresentationController : MonoBehaviour
{
    private void Awake()
    {
        enabled = false;
    }
}

using UnityEngine;

/// <summary>
/// Legacy compatibility shell.
///
/// Mini PACK의 크기/셀 배치/화면 위치 및 Reward 컨트롤 배치는
/// BattleUnifiedInventoryInspectController로 통합되었습니다.
/// 기존 Scene에 남아 있는 컴포넌트 참조만 보존하고 실행은 중단합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleInventoryHudLayoutPolishController : MonoBehaviour
{
    private void Awake()
    {
        enabled = false;
    }
}

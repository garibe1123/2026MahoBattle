using UnityEngine;

/// <summary>
/// Legacy compatibility shell.
///
/// PACK 선택 프레임은 더 이상 이 컴포넌트가 후단에서 덮어쓰지 않습니다.
/// - Mini PACK: BattleInventoryInteractionController의 InteractionSelectionFrame
/// - Full Grid: BattleUnifiedInventoryInspectController의 UnifiedSelectionFrame
///
/// 기존 Scene/Prefab에 직렬화된 컴포넌트 타입을 깨지 않기 위해 타입만 유지합니다.
/// 신규 설치는 하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattlePackSelectionFramePolicyController : MonoBehaviour
{
}

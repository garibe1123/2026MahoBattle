using UnityEngine;

/// <summary>
/// Phase 5 compatibility shell.
/// Reward 카드 Hover/선택/상세/결정/포기/Selection Locked 표현은
/// BattleRewardCardActionController가 단독 소유합니다.
///
/// 기존 Scene에 직렬화된 컴포넌트 참조를 즉시 깨지 않기 위해 타입만 유지합니다.
/// 신규 로직을 이 클래스에 추가하지 마세요.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleRewardSelectionPresentationController : MonoBehaviour
{
}

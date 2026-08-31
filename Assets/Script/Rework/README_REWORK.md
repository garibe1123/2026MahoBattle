# Battle Rework v2

기준 문서: 마법소녀 프로젝트 통합 기획서 최신 우선순위.

## 이번 1차 패스에서 추가한 구조

- BattleRunManager: Branch/Node 진행, Terminal Node 종료
- BattleRoomManager: Room 조립, NavMesh 재빌드, 고정 스폰, Room Clear
- CombatDamage: 공통 데미지 계산 및 IDamageable
- MonsterDefinitionSO / MonsterController / MonsterPool: 카테고리 + 이동 타입 + 스킬 조합 기반 몬스터
- NodeGraphSO / RoomDefinitionSO / MapBlock / BattleObstacle
- ClanDefinitionSO / ShootingThemeSO 분리
- RunProgressSystem: 인기도, 팬 포인트, 몬스터 처치 포인트, C/B/A/S 판정
- FanMissionSO / FanMissionSystem
- BattleEquipmentSO / BattleEquipmentSystem: 9칸 장비, 3중복 승급
- CoreDefinitionSO / PlayerLoadout
- BattleHUD / BattleInfoUI / BattleResultUI

## 아직 Legacy를 삭제하지 않은 이유

현재 Scene/Prefab이 기존 StageSetManager, Enemy, PlayerInventoryUI 등에 직렬화 참조를 가지고 있을 수 있으므로, 새 구조가 씬에 연결되기 전까지 기존 CS는 유지한다.

다음 패스에서 해야 할 일:

1. Projectile 피해 판정을 CombatDamage로 통일
2. PlayerController의 iFrame / Roll 수치를 기획값으로 수정
3. Player death -> BattleRunManager.NotifyPlayerDeath 연결
4. 기존 Enemy 행동 애니메이션 로직을 MonsterController에 이식
5. Room 전환을 Highlight Block 진입 트리거 방식으로 연결
6. Scene/Prefab 참조 전환 후 Legacy 파일 삭제

# Stylish Arcade 적용 범위

| 기존 플레이 화면 | 생성·등록 경로 | 적용 |
| --- | --- | --- |
| 전투 HUD·방송 대시보드 | BattleHUD, BattleBroadcastDashboardController.RegisterRoot | 프레임·헤더·장식 |
| 장착 아이템 바 | BattleKineticItemBarUI.RegisterUiMaterialRoot | 슬롯·선택 강조 |
| 아이템 선택·보상 | BattleHUD.RebuildRewardCards, BattleRewardCardActionController | 카드·버튼·선택·확정·상세 전환 |
| 인벤토리·상세 | BattleKineticLoadoutUI.RegisterUiMaterialRoot, BattleUnifiedInventoryInspectController | 패널·제목·슬롯·상세 전환 |
| 보상 배치 완료 | BattleInventoryInteractionController.BuildRewardDoneUi | 버튼·손 아이템 안내 |
| 룰렛 | BattleRuleRouletteController.EnsureUi | 전체 등록·시작 CTA·선택·확정 |
| 일시정지·설정 | BattlePauseController.RegisterPauseStyle | 프레임·제목·버튼 |
| 결과 | BattleRunResultUI.Show | 프레임·제목·확인 CTA·확정 연출 |

공통 경로는 BattleUiStyleController.RegisterGraphic → BattleUiStyleMeshEffect이며, 버튼 반응은 BattleUiStyleInteraction, 방사선·색종이는 BattleUiStyleBurst가 담당한다. 전환과 반응은 unscaled time을 사용하며 레이아웃과 입력 Rect는 유지한다. 룰렛 시작 CTA 크기만 320×96으로 확장했다.

Stylish를 기본 적용하며 다른 테마의 팔레트와 전환 API를 보존한다. 지정된 UI 머티리얼은 기존 우선순위를 유지하므로 해당 머티리얼의 그래픽은 공통 메시 교체 대상에서 제외된다. 개발 디버그 UI와 전투 이펙트는 대상에서 제외한다.

시작 경로는 보상 선택 → 전송 → PACK 편집(손 아이템 없음) → CompleteReward → 다음 노드 선택 → RuleRoulette → 최종 확인 → ConfirmStartBattle → 기존 전투 진입이다. RuleRoulette의 UI 액션 누락을 수정하고, 보상 외 비전투 상태에서 인벤토리 Raycast 점유를 해제했다. 시작 확정은 최종 확인 이후 프레임에서 한 번만 허용한다.

정적 코드 검토 범위이며 Unity 컴파일·마우스 재현·실제 화면 배치·성능은 미검증이다. InputSystemUIInputModule은 별도 기본 액션을 사용하므로 라우터 누락만으로 마우스 불능의 전체 원인을 확정하지 않는다.

변경 파일(모든 경로는 Assets/ 기준):

- Script/BattleRework/Battle/BattleInputRouter.cs, BattleRuleRouletteController.cs
- Script/BattleRework/Presentation/BattleShowPresentationManager.cs
- Script/BattleRework/UI/BattleInventoryInteractionController.cs, BattleUnifiedInventoryInspectController.cs, BattleKineticLoadoutUI.cs, BattleRewardCardActionController.cs
- Script/BattleRework/UI/BattlePauseController.cs, BattleRunResultUI.cs
- Script/BattleRework/UI/BattleUiStyleController.cs, BattleUiStyleMeshEffect.cs, BattleUiPanelDecoration.cs, BattleUiStyleTmpTitle.cs
- Script/BattleRework/UI/BattleUiStyleInteraction.cs, BattleUiStyleBurst.cs (신규)
- Editor/HarnessStylishArcade.cs, HarnessStylishArcadeScope.md (신규)

BattleRewardFlow의 아이템 데이터 이동·장착·폐기·보상 확정과 BattleUiStyleTitleText의 기존 경계 합산 코드는 수정하지 않았다. 씬·프리팹·ProjectSettings·Packages·외부 에셋·바이너리·meta 수정은 없다.

정적 검사: git diff --check 통과. 신규 Update에는 GetComponent·Find 호출이 없고, 장식과 확정 연출은 raycastTarget=false이며 확정 연출 Canvas에는 GraphicRaycaster를 만들지 않는다.

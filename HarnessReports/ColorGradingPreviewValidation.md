# 0017 색보정 미리보기

## 확인 근거와 수정 범위
- Unity가 없어 화면상의 적용 차단 결함은 재현하지 못했다. 실제 활성 SO, 카메라, Volume 마스크는 Play 중 BattleColorGradingController Inspector에 표시한다.
- 실제 활성 프로필·카메라 마스크와 적용 차단 여부는 미확인이다. 마스크와 생성된 Volume 레이어의 불일치는 가능한 원인일 뿐, 이 씬의 결함으로 확인되지 않았다. 재현 없이 추가했던 런타임 Volume 레이어 변경을 제거했다. Volume 레이어·카메라 마스크·렌더러 설정은 변경하지 않는다. 마스크가 0이면 Inspector에 경고한다.
- Update에서 호출되던 Camera.main / GetComponent 경로는 Start와 Configure로 옮겼다. Update는 캐시된 카메라 데이터만 사용한다.
- 중립 Split Toning과 대기/전투/쇼 가중치는 오류로 취급하지 않았으며 원래 값을 유지한다.

## 미리보기 동작
- 사진에 표시된 기존 F8 PLAY LIGHT TUNER에 Color / Bloom 탭을 추가했다. Apply preview, Original, Reset preview와 RGB/밸런스/Bloom 슬라이더를 Game 화면에서 사용할 수 있다. 이전 구현은 Inspector에만 있어 F8 패널에 나타나지 않았다.
- 이 탭은 실제 연결된 원본 프로필 이름, 카메라 이름, 카메라 Volume 마스크, 런타임 Volume 레이어와 상태 가중치를 표시한다. 마스크가 비어 있거나 해당 레이어를 제외하면 경고만 표시하며 설정은 바꾸지 않는다. Log current settings로 현재 연결 정보를 Console에 기록할 수 있다. 이는 최종 화면의 후처리 적용 성공을 판정하는 자동 테스트가 아니다.
- Play 중 Inspector의 Preview applied로 시작한다. 런타임 SO 복제본의 Split Toning과 Bloom만 기존 Volume 경로에 적용한다. 원본 SO와 Director의 ActiveLightingProfile은 바꾸지 않는다.
- Compare는 현재 활성 원본 프로필과 미리보기를 전환한다. Reset preview는 현재 원본을 복제하고 푸른 암부/따뜻한 명부 및 Bloom 시작값으로 되돌린다. 비교 중 초기화해도 원본 비교 상태는 유지한다.
- 상태나 방 프로필이 바뀌어도 미리보기 조절값은 유지한다. 상태별 Volume 가중치, 피격 반응의 Bloom 가산, 저체력/처치 색보정은 기존 경로를 사용한다.
- 복제본은 HideAndDontSave이고 컨트롤러 파괴 시 제거한다. 에디터 저장/EditorPrefs 경로는 추가하지 않았다.

## Unity에서 확인할 것 (미실행)
0. Game 화면에서 F8 → Color / Bloom → Apply preview를 누른다. Original과 번갈아 비교하고, Reset preview와 슬라이더를 확인한다. Log current settings의 Console 결과와 실제 화면 비교 결과를 함께 기록한다.
1. Play 중 활성 BattleColorGradingController를 선택해 원본 프로필, 카메라, Volume 마스크를 확인한다. 마스크가 비어 있으면 카메라 설정을 사람이 확인한다.
2. 색상/밸런스/Bloom을 바꾸고 Compare 및 Reset preview를 눌러 같은 구도로 비교한다. Reset은 원본 복귀가 아닌 미리보기 시작값 복귀이다.
3. 대기/전투/쇼와 방 전환 뒤 조절값 유지, 상태별 강도, 피격/처치 연출과 캐릭터 가독성을 확인한다.
4. Play 종료 후 원본 SO 변경과 미리보기 값 잔류가 없는지 확인한다.

컴파일·실행·빌드·테스트 명령은 실행하지 않았다. 렌더러의 후처리 지원, 효과 품질, 다른 Volume과의 최종 합성은 Unity에서 확인해야 한다.

## 리뷰 반려 후 확인 상태
- 첨부 사진에는 Lighting / Shadows 두 탭만 보이며 Color / Bloom 탭은 보이지 않는다. 사진만으로 노트북의 코드 버전이나 효과 적용 여부는 판정할 수 없다.
- 로컬 BattleFieldCinematicDirector에는 Color / Bloom 탭, Apply preview / Original / Reset preview와 Log current settings 코드가 있다. 확인 시 해당 파일과 BattleColorGradingController, 이 기록은 git status에서 수정 상태였다. 로컬 변경의 존재를 GitHub 반영이나 노트북 실행 결과로 간주하지 않는다.
- GitHub 확인을 위해 읽기 전용 git ls-remote origin HEAD를 실행했으나 github.com:443 연결 실패로 원격 상태를 확인하지 못했다. 웹 접근도 실패했다. 커밋·푸시·브랜치 조작은 하지 않았다. 노트북에서 테스트하려면 하네스가 변경 코드를 전달한 뒤 Unity에서 다시 컴파일되어야 한다.
- 이 PC에는 Unity가 없어 실행 가능한 환경에서의 활성 프로필·마스크 확인과 차단 결함 재현을 수행할 수 없다. 이 항목은 미완료이며, 확인된 적용 차단 결함이나 그 수정 결과는 없다.
- Unity 검증 시 원본 프로필 이름/에셋 경로, 실제 카메라 이름, Volume 마스크, 생성 Volume 레이어, 재현 상태와 조작, 화면 적용 여부를 기록해야 한다. 같은 조건에서 원인을 분리해 확인한 뒤 해당 원인에 필요한 수정만 추가한다.

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 최종 아트/UI 프리팹 없이 전체 전투 런을 플레이테스트하기 위한 기능성 더미 UI입니다.
/// IMGUI를 사용하므로 Canvas/TMP 세팅 없이 빈 GameObject에 붙이기만 해도 동작합니다.
///
/// 포함 범위:
/// - 촬영 테마/클랜 선택 + Run 시작
/// - 전투 HUD(HP/Stamina/무기/탄약/인기도/팬/처치포인트)
/// - 9칸 장비 Quick Bar + 장착/폐기
/// - Fan Mission 표시/포기/더미 진행
/// - Reward 3택 + Inventory Full 시 즉시 슬롯 교체
/// - Branch Node 선택
/// - Shop/Event Node 더미 상호작용
/// - TAB 분석 화면
/// - Clear/Death/Quit 결과 + Restart
/// </summary>
public class BattleDummyUI : MonoBehaviour
{
    [Header("Core References - empty fields are auto-found")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private RunProgressSystem progress;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private PlayerController player;
    [SerializeField] private PlayerShootingSystem shootingSystem;
    [SerializeField] private FanMissionSystem fanMissionSystem;
    [SerializeField] private BattleInfoUI battleInfoUI;

    [Header("Run Setup - optional selectable assets")]
    [SerializeField] private List<ShootingThemeSO> selectableThemes = new();
    [SerializeField] private List<ClanDefinitionSO> selectableClans = new();

    [Header("Dummy UI")]
    [SerializeField] private bool visible = true;
    [SerializeField] private bool showDeveloperButtons = true;
    [SerializeField] private bool enableTabBulletTimeFallback = true;
    [SerializeField, Range(0.02f, 0.5f)] private float bulletTimeScale = 0.1f;

    private readonly List<FanMissionSO> runtimeDummyMissions = new();

    private int pendingRewardIndex = -1;
    private int dummyMissionSerial;
    private bool analysisOpen;
    private bool ownsBulletTime;
    private float previousTimeScale = 1f;
    private float previousFixedDeltaTime = 0.02f;
    private Vector2 missionScroll;
    private Vector2 analysisScroll;

    private void Awake()
    {
        AutoFindReferences();
        CollectLoadedSetupAssets();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F2))
            visible = !visible;

        bool shouldOpenAnalysis = visible &&
                                  runManager != null &&
                                  runManager.RunActive &&
                                  Input.GetKey(KeyCode.Tab);

        if (shouldOpenAnalysis && !analysisOpen)
            OpenAnalysis();
        else if (!shouldOpenAnalysis && analysisOpen)
            CloseAnalysis();

        if (runManager == null || runManager.State != BattleRunState.Reward)
            pendingRewardIndex = -1;
    }

    private void OnDisable()
    {
        CloseAnalysis();
    }

    private void OnDestroy()
    {
        CloseAnalysis();

        for (int i = 0; i < runtimeDummyMissions.Count; i++)
        {
            if (runtimeDummyMissions[i] != null)
                Destroy(runtimeDummyMissions[i]);
        }

        runtimeDummyMissions.Clear();
    }

    [ContextMenu("Auto Find Dummy UI References")]
    public void AutoFindReferences()
    {
        if (runManager == null) runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null) roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (progress == null) progress = FindFirstObjectByType<RunProgressSystem>();
        if (equipmentSystem == null) equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (shootingSystem == null) shootingSystem = FindFirstObjectByType<PlayerShootingSystem>();
        if (fanMissionSystem == null) fanMissionSystem = FindFirstObjectByType<FanMissionSystem>();
        if (battleInfoUI == null) battleInfoUI = FindFirstObjectByType<BattleInfoUI>();
    }

    private void CollectLoadedSetupAssets()
    {
        if (selectableThemes == null)
            selectableThemes = new List<ShootingThemeSO>();

        if (selectableClans == null)
            selectableClans = new List<ClanDefinitionSO>();

        if (selectableThemes.Count == 0)
        {
            ShootingThemeSO[] loadedThemes = Resources.FindObjectsOfTypeAll<ShootingThemeSO>();
            for (int i = 0; i < loadedThemes.Length; i++)
            {
                if (loadedThemes[i] != null && !selectableThemes.Contains(loadedThemes[i]))
                    selectableThemes.Add(loadedThemes[i]);
            }
        }

        if (selectableClans.Count == 0)
        {
            ClanDefinitionSO[] loadedClans = Resources.FindObjectsOfTypeAll<ClanDefinitionSO>();
            for (int i = 0; i < loadedClans.Length; i++)
            {
                if (loadedClans[i] != null && !selectableClans.Contains(loadedClans[i]))
                    selectableClans.Add(loadedClans[i]);
            }
        }
    }

    private void OpenAnalysis()
    {
        analysisOpen = true;

        // BattleInfoUI가 씬에 있으면 기존 TAB bullet-time 구현을 존중하고,
        // 없을 때만 이 더미 UI가 fallback으로 TimeScale을 직접 관리합니다.
        if (!enableTabBulletTimeFallback || battleInfoUI != null)
            return;

        ownsBulletTime = true;
        previousTimeScale = Time.timeScale;
        previousFixedDeltaTime = Time.fixedDeltaTime;

        Time.timeScale = bulletTimeScale;
        float ratio = previousTimeScale > 0.0001f
            ? bulletTimeScale / previousTimeScale
            : bulletTimeScale;
        Time.fixedDeltaTime = Mathf.Max(0.0001f, previousFixedDeltaTime * ratio);
    }

    private void CloseAnalysis()
    {
        if (!analysisOpen && !ownsBulletTime)
            return;

        analysisOpen = false;

        if (!ownsBulletTime)
            return;

        ownsBulletTime = false;
        Time.timeScale = previousTimeScale;
        Time.fixedDeltaTime = previousFixedDeltaTime;
    }

    private void OnGUI()
    {
        if (!visible)
            return;

        if (runManager == null)
        {
            DrawMissingReferencePanel();
            return;
        }

        if (runManager.RunActive)
        {
            DrawCombatHud();
            DrawFanMissionPanel();
            DrawEquipmentQuickBar();
        }

        DrawStatePanel();

        if (analysisOpen)
            DrawAnalysisPanel();
    }

    private void DrawMissingReferencePanel()
    {
        GUILayout.BeginArea(new Rect(20f, 20f, 420f, 180f), GUI.skin.box);
        GUILayout.Label("BATTLE DUMMY UI - REFERENCES MISSING");
        GUILayout.Label("BattleRunManager를 찾지 못했습니다.");

        if (GUILayout.Button("AUTO FIND REFERENCES", GUILayout.Height(32f)))
            AutoFindReferences();

        GUILayout.Label("F2: Dummy UI 표시/숨김");
        GUILayout.EndArea();
    }

    private void DrawCombatHud()
    {
        GUILayout.BeginArea(new Rect(12f, 12f, 360f, 250f), GUI.skin.box);
        GUILayout.Label("SHOOTING HUD / DUMMY");

        string theme = runManager.ShootingTheme != null
            ? runManager.ShootingTheme.displayName
            : "None";
        string clan = runManager.Clan != null
            ? runManager.Clan.displayName
            : "None";

        GUILayout.Label($"Theme: {theme}   Clan: {clan}");
        GUILayout.Label($"State: {runManager.State}");

        if (runManager.CurrentNode != null)
            GUILayout.Label($"Node: {runManager.CurrentNode.id} / {runManager.CurrentNode.type} / Depth {runManager.CurrentNode.depth}");

        if (roomManager != null && roomManager.CurrentRoom != null)
            GUILayout.Label($"Room: {roomManager.CurrentRoom.roomId}   Enemy: {roomManager.AliveMonsterCount}");

        if (player != null)
        {
            GUILayout.Label($"HP: {player.CurrentHp:0}/{player.maxHp:0}");
            GUILayout.HorizontalSlider(player.CurrentHp, 0f, Mathf.Max(1f, player.maxHp));
            GUILayout.Label($"ST: {player.CurrentStamina:0}/{player.maxStamina:0}");
            GUILayout.HorizontalSlider(player.CurrentStamina, 0f, Mathf.Max(1f, player.maxStamina));
        }

        if (shootingSystem != null)
        {
            string weaponName = shootingSystem.currentWeaponSO != null
                ? shootingSystem.currentWeaponSO.name
                : "NO WEAPON";
            string reload = shootingSystem.IsReloading ? " / RELOADING" : string.Empty;
            GUILayout.Label($"Weapon: {weaponName} / Ammo {shootingSystem.currentAmmo}{reload}");
        }

        if (progress != null)
        {
            GUILayout.Label($"Popularity {progress.Popularity} / Grade {progress.CurrentVillainGrade}");
            GUILayout.Label($"Fans {progress.FanPoints} / KillPoint {progress.MonsterKillPoints}");
            GUILayout.Label($"Viewers {progress.Viewers} / Likes {progress.Likes}");
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("QUIT"))
            runManager.QuitRun();
        GUILayout.Label("TAB: Analysis   F2: UI Toggle");
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    private void DrawFanMissionPanel()
    {
        float width = 340f;
        float x = Mathf.Max(12f, Screen.width - width - 12f);
        float height = Mathf.Min(410f, Screen.height - 180f);

        GUILayout.BeginArea(new Rect(x, 12f, width, height), GUI.skin.box);
        GUILayout.Label("FAN MISSIONS / DUMMY");

        if (fanMissionSystem == null)
        {
            GUILayout.Label("FanMissionSystem 없음");
            GUILayout.EndArea();
            return;
        }

        GUILayout.Label($"Slots: {fanMissionSystem.ActiveMissions.Count}/{fanMissionSystem.UnlockedSlots}");

        missionScroll = GUILayout.BeginScrollView(missionScroll);
        for (int i = 0; i < fanMissionSystem.ActiveMissions.Count; i++)
        {
            FanMissionRuntime runtime = fanMissionSystem.ActiveMissions[i];
            if (runtime == null || runtime.Definition == null)
                continue;

            FanMissionSO definition = runtime.Definition;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(string.IsNullOrWhiteSpace(definition.missionName) ? definition.name : definition.missionName);
            GUILayout.Label(definition.description ?? string.Empty);
            GUILayout.Label($"{runtime.Progress}/{Mathf.Max(1, definition.targetCount)}   Type: {definition.type}");

            if (definition.duration > 0f)
                GUILayout.Label($"Time: {runtime.RemainingTime:0.0}s");

            GUILayout.BeginHorizontal();
            if (showDeveloperButtons && GUILayout.Button("+1 TEST"))
            {
                fanMissionSystem.AddProgress(definition.type, 1);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            if (GUILayout.Button("REJECT"))
            {
                fanMissionSystem.RejectMission(i);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
        GUILayout.EndScrollView();

        if (showDeveloperButtons && GUILayout.Button("ADD RUNTIME DUMMY MISSION"))
            AddRuntimeDummyMission();

        GUILayout.EndArea();
    }

    private void AddRuntimeDummyMission()
    {
        if (fanMissionSystem == null)
            return;

        FanMissionSO mission = ScriptableObject.CreateInstance<FanMissionSO>();
        mission.hideFlags = HideFlags.DontSave;
        mission.name = $"RuntimeDummyFanMission_{dummyMissionSerial}";
        mission.missionName = $"더미 팬미션 #{dummyMissionSerial + 1}";
        mission.type = (FanMissionType)(dummyMissionSerial % 4);
        mission.description = "더미 UI 동작 확인용 팬미션. +1 TEST 또는 REJECT로 판정 가능.";
        mission.targetCount = 3;
        mission.duration = 30f;
        mission.successPopularity = 25;
        mission.successFanPoints = 50;
        mission.failPopularity = -10;
        mission.failFanPoints = 0;

        dummyMissionSerial++;

        if (fanMissionSystem.TryAddMission(mission))
        {
            runtimeDummyMissions.Add(mission);
        }
        else
        {
            Destroy(mission);
        }
    }

    private void DrawEquipmentQuickBar()
    {
        if (equipmentSystem == null)
            return;

        float width = Mathf.Min(Screen.width - 24f, 1120f);
        float x = (Screen.width - width) * 0.5f;
        float y = Screen.height - 148f;

        GUILayout.BeginArea(new Rect(x, y, width, 136f), GUI.skin.box);
        GUILayout.Label("BATTLE EQUIPMENT 1-9");
        GUILayout.BeginHorizontal();

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(112f), GUILayout.Height(92f));
            GUILayout.Label($"[{i + 1}]");

            if (i >= equipmentSystem.UnlockedSlotCount)
            {
                GUILayout.Label("LOCKED");
                GUILayout.EndVertical();
                continue;
            }

            BattleEquipmentSlot slot = equipmentSystem.Slots[i];
            if (slot == null || slot.equipment == null)
            {
                GUILayout.Label("EMPTY");
                GUILayout.EndVertical();
                continue;
            }

            string equipped = equipmentSystem.IsSlotEquipped(i) ? " *" : string.Empty;
            GUILayout.Label($"{slot.equipment.GetDisplayName()}{equipped}");
            GUILayout.Label($"G{slot.grade} / {slot.copies}/3");

            if (slot.equipment.shootingData != null && GUILayout.Button("EQUIP"))
                equipmentSystem.EquipSlot(i);

            if (showDeveloperButtons && GUILayout.Button("DROP"))
                equipmentSystem.DiscardSlot(i);

            GUILayout.EndVertical();
        }

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawStatePanel()
    {
        if (runManager == null)
            return;

        switch (runManager.State)
        {
            case BattleRunState.None:
                DrawRunSetupPanel();
                break;

            case BattleRunState.EnteringNode:
                DrawMessagePanel("NODE ENTER", "다음 촬영 구간으로 진입 중...");
                break;

            case BattleRunState.BuildingRoom:
                DrawMessagePanel("SET BUILDING", "촬영 세트 블록 조립 / NavMesh Bake 중...");
                break;

            case BattleRunState.Reward:
                DrawRewardPanel();
                break;

            case BattleRunState.ExitingRoom:
                DrawMessagePanel("ROOM CLEAR", "보상 확정 완료. Highlight Block으로 이동하세요.");
                break;

            case BattleRunState.SelectingNode:
                DrawNodeSelectionPanel();
                break;

            case BattleRunState.NonCombat:
                DrawNonCombatPanel();
                break;

            case BattleRunState.Ended:
                DrawResultPanel();
                break;
        }
    }

    private void DrawRunSetupPanel()
    {
        Rect rect = CenterRect(620f, 560f);
        GUILayout.BeginArea(rect, GUI.skin.window);
        GUILayout.Label("SHOOTING CONTRACT / DUMMY RUN SETUP");
        GUILayout.Space(8f);

        DrawThemeSelector();
        GUILayout.Space(10f);
        DrawClanSelector();
        GUILayout.Space(12f);

        if (equipmentSystem != null)
            GUILayout.Label($"Battle Equipment Capacity: {equipmentSystem.UnlockedSlotCount}/{BattleEquipmentSystem.MaxSlotCount}");

        if (runManager.ValidateConfiguration(out string report))
        {
            GUILayout.Label("Configuration: READY");
            if (GUILayout.Button("START SHOOTING RUN", GUILayout.Height(46f)))
                runManager.StartRun();
        }
        else
        {
            GUILayout.Label("Configuration: NOT READY");
            GUILayout.TextArea(report, GUILayout.Height(120f));
        }

        GUILayout.Label("F2: Dummy UI Toggle");
        GUILayout.EndArea();
    }

    private void DrawThemeSelector()
    {
        GUILayout.Label("촬영 스테이지 / Shooting Theme");
        string current = runManager.ShootingTheme != null
            ? runManager.ShootingTheme.displayName
            : "None";
        GUILayout.Label($"Current: {current}");

        if (selectableThemes == null || selectableThemes.Count == 0)
        {
            GUILayout.Label("선택 가능한 Theme asset이 로드되지 않음. RunManager Inspector 값 사용.");
            return;
        }

        GUILayout.BeginHorizontal();
        for (int i = 0; i < selectableThemes.Count; i++)
        {
            ShootingThemeSO theme = selectableThemes[i];
            if (theme == null) continue;

            string label = string.IsNullOrWhiteSpace(theme.displayName) ? theme.name : theme.displayName;
            if (GUILayout.Button(label, GUILayout.Height(30f)))
                runManager.SetShootingThemeForNextRun(theme);
        }
        GUILayout.EndHorizontal();
    }

    private void DrawClanSelector()
    {
        GUILayout.Label("클랜 / Clan");
        string current = runManager.Clan != null
            ? runManager.Clan.displayName
            : "None";
        GUILayout.Label($"Current: {current}");

        if (selectableClans == null || selectableClans.Count == 0)
        {
            GUILayout.Label("선택 가능한 Clan asset이 로드되지 않음. RunManager Inspector 값 사용.");
            return;
        }

        GUILayout.BeginHorizontal();
        for (int i = 0; i < selectableClans.Count; i++)
        {
            ClanDefinitionSO clan = selectableClans[i];
            if (clan == null) continue;

            string label = string.IsNullOrWhiteSpace(clan.displayName) ? clan.name : clan.displayName;
            if (GUILayout.Button(label, GUILayout.Height(30f)))
                runManager.SetClanForNextRun(clan);
        }
        GUILayout.EndHorizontal();
    }

    private void DrawRewardPanel()
    {
        Rect rect = CenterRect(700f, pendingRewardIndex >= 0 ? 610f : 430f);
        GUILayout.BeginArea(rect, GUI.skin.window);
        GUILayout.Label("ROOM CLEAR - CHOOSE BATTLE EQUIPMENT");
        GUILayout.Label("하나를 선택하면 인런 장비에 추가됩니다. 동일 장비 3개는 다음 Grade로 승급합니다.");
        GUILayout.Space(8f);

        for (int i = 0; i < runManager.CurrentRewardChoices.Count; i++)
        {
            BattleEquipmentSO reward = runManager.CurrentRewardChoices[i];
            if (reward == null) continue;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"[{i + 1}] {reward.GetDisplayName()} / {reward.rarity}");
            GUILayout.Label($"Type: {reward.type} / Tags: {FormatTags(reward)}");
            GUILayout.Label($"Damage x{reward.damageMultiplier:0.00} / Move x{reward.moveSpeedMultiplier:0.00}");

            if (GUILayout.Button("TAKE", GUILayout.Height(34f)))
            {
                if (!runManager.SelectReward(i))
                    pendingRewardIndex = i;
                else
                    pendingRewardIndex = -1;
            }
            GUILayout.EndVertical();
        }

        if (pendingRewardIndex >= 0 && pendingRewardIndex < runManager.CurrentRewardChoices.Count)
        {
            BattleEquipmentSO pending = runManager.CurrentRewardChoices[pendingRewardIndex];
            GUILayout.Space(10f);
            GUILayout.Label($"INVENTORY FULL - '{pending.GetDisplayName()}' 로 교체할 슬롯 선택");

            if (equipmentSystem != null)
            {
                for (int i = 0; i < equipmentSystem.UnlockedSlotCount; i++)
                {
                    BattleEquipmentSlot slot = equipmentSystem.Slots[i];
                    string oldName = slot != null && slot.equipment != null
                        ? slot.equipment.GetDisplayName()
                        : "EMPTY";

                    if (GUILayout.Button($"Replace [{i + 1}] {oldName}", GUILayout.Height(28f)))
                    {
                        if (runManager.ReplaceRewardIntoSlot(pendingRewardIndex, i))
                            pendingRewardIndex = -1;
                    }
                }
            }

            if (GUILayout.Button("CANCEL REPLACE"))
                pendingRewardIndex = -1;
        }

        if (showDeveloperButtons && GUILayout.Button("SKIP REWARD / DEBUG"))
            runManager.SkipReward();

        GUILayout.EndArea();
    }

    private void DrawNodeSelectionPanel()
    {
        Rect rect = CenterRect(620f, 390f);
        GUILayout.BeginArea(rect, GUI.skin.window);
        GUILayout.Label("SELECT NEXT SHOOTING SCENE");
        GUILayout.Label("다음 Node를 선택하세요.");
        GUILayout.Space(8f);

        for (int i = 0; i < runManager.NextNodeChoices.Count; i++)
        {
            BattleNodeData node = runManager.NextNodeChoices[i];
            if (node == null) continue;

            string terminal = node.isTerminal ? " / TERMINAL" : string.Empty;
            if (GUILayout.Button($"{node.id}  |  {node.type}  |  Depth {node.depth}{terminal}", GUILayout.Height(44f)))
                runManager.SelectNextNode(node.id);
        }

        GUILayout.EndArea();
    }

    private void DrawNonCombatPanel()
    {
        BattleNodeData node = runManager.CurrentNode;
        if (node == null)
            return;

        Rect rect = CenterRect(620f, 420f);
        GUILayout.BeginArea(rect, GUI.skin.window);

        if (node.type == BattleNodeType.Shop)
            DrawDummyShop();
        else
            DrawDummyEvent();

        GUILayout.EndArea();
    }

    private void DrawDummyShop()
    {
        GUILayout.Label("SHOP NODE / DUMMY");
        GUILayout.Label("정식 상점 경제 데이터 전, Node Flow와 성장 UI를 검증하기 위한 더미 기능입니다.");
        GUILayout.Space(10f);

        if (GUILayout.Button("무료 회복 +25 HP 후 진행", GUILayout.Height(38f)))
        {
            player?.Heal(25f);
            runManager.ResolveNonCombatNode();
        }

        if (GUILayout.Button("[TEST] 장비 Capacity +1 후 진행", GUILayout.Height(38f)))
        {
            equipmentSystem?.UnlockSlots(1);
            runManager.ResolveNonCombatNode();
        }

        if (GUILayout.Button("구매 없이 진행", GUILayout.Height(38f)))
            runManager.ResolveNonCombatNode();
    }

    private void DrawDummyEvent()
    {
        GUILayout.Label("CHOICE EVENT / DUMMY");
        GUILayout.Label("선택지 결과가 실제 RunProgress/Player 상태에 반영됩니다.");
        GUILayout.Space(10f);

        if (GUILayout.Button("A. 방송 각을 잡는다  → Popularity +100", GUILayout.Height(40f)))
        {
            progress?.AddPopularity(100);
            runManager.ResolveNonCombatNode();
        }

        if (GUILayout.Button("B. 팬서비스를 한다  → FanPoint +50", GUILayout.Height(40f)))
        {
            progress?.AddFanPoints(50);
            runManager.ResolveNonCombatNode();
        }

        if (GUILayout.Button("C. 무리한 연출을 한다 → HP -20", GUILayout.Height(40f)))
        {
            player?.TakeDamage(20f);
            if (runManager.RunActive)
                runManager.ResolveNonCombatNode();
        }
    }

    private void DrawResultPanel()
    {
        Rect rect = CenterRect(620f, 420f);
        GUILayout.BeginArea(rect, GUI.skin.window);

        RunEndReason? reason = runManager.LastEndReason;
        string title = reason switch
        {
            RunEndReason.Clear => "SHOOTING COMPLETE / CLEAR",
            RunEndReason.Death => "BROADCAST ACCIDENT / DEATH",
            RunEndReason.Quit => "SHOOTING CANCELED / QUIT",
            _ => "RUN ENDED"
        };

        GUILayout.Label(title);
        GUILayout.Space(8f);

        if (progress != null)
        {
            GUILayout.Label($"Popularity: {progress.Popularity}");
            GUILayout.Label($"Fan Point: {progress.FanPoints}");
            GUILayout.Label($"Viewers: {progress.Viewers} / Likes: {progress.Likes}");
            GUILayout.Label($"Villain Grade: {progress.CurrentVillainGrade}");
        }

        GUILayout.Space(12f);
        if (GUILayout.Button("RESTART RUN", GUILayout.Height(46f)))
            runManager.RestartRun();

        GUILayout.EndArea();
    }

    private void DrawMessagePanel(string title, string message)
    {
        Rect rect = CenterRect(520f, 160f);
        GUILayout.BeginArea(rect, GUI.skin.window);
        GUILayout.Label(title);
        GUILayout.Space(8f);
        GUILayout.Label(message);
        GUILayout.EndArea();
    }

    private void DrawAnalysisPanel()
    {
        Rect rect = new Rect(
            Screen.width * 0.18f,
            Screen.height * 0.12f,
            Screen.width * 0.64f,
            Screen.height * 0.68f);

        GUI.Box(rect, string.Empty);
        GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, rect.height - 24f));
        GUILayout.Label("TAB ANALYSIS / DUMMY INTEGRATED INFO");
        GUILayout.Label("Inventory + Fan Mission + Shooting Theme 정보를 한 화면에서 확인합니다.");
        GUILayout.Space(8f);

        analysisScroll = GUILayout.BeginScrollView(analysisScroll);

        if (runManager.ShootingTheme != null)
        {
            GUILayout.Label($"SHOOTING THEME: {runManager.ShootingTheme.displayName}");
            GUILayout.Label(runManager.ShootingTheme.fanReactionTone ?? string.Empty);
            GUILayout.Label("Reward Bias:");

            for (int i = 0; i < runManager.ShootingTheme.rewardBiases.Count; i++)
            {
                ThemeRewardBias bias = runManager.ShootingTheme.rewardBiases[i];
                if (bias != null)
                    GUILayout.Label($"  {bias.tag} +{bias.weightBonus:0.00}");
            }
        }

        GUILayout.Space(10f);
        GUILayout.Label("EQUIPMENT");
        if (equipmentSystem != null)
        {
            for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
            {
                if (i >= equipmentSystem.UnlockedSlotCount)
                {
                    GUILayout.Label($"[{i + 1}] LOCKED");
                    continue;
                }

                BattleEquipmentSlot slot = equipmentSystem.Slots[i];
                if (slot == null || slot.equipment == null)
                {
                    GUILayout.Label($"[{i + 1}] EMPTY");
                    continue;
                }

                GUILayout.Label($"[{i + 1}] {slot.equipment.GetDisplayName()} / G{slot.grade} / {FormatTags(slot.equipment)}");
            }
        }

        GUILayout.Space(10f);
        GUILayout.Label("ACTIVE FAN MISSIONS");
        if (fanMissionSystem != null)
        {
            for (int i = 0; i < fanMissionSystem.ActiveMissions.Count; i++)
            {
                FanMissionRuntime runtime = fanMissionSystem.ActiveMissions[i];
                if (runtime == null || runtime.Definition == null) continue;

                GUILayout.Label($"- {runtime.Definition.missionName}: {runtime.Progress}/{Mathf.Max(1, runtime.Definition.targetCount)}");
            }
        }

        GUILayout.Space(10f);
        GUILayout.Label("CURRENT TAG COUNTS");
        if (equipmentSystem != null)
        {
            EquipmentTag[] tags = (EquipmentTag[])System.Enum.GetValues(typeof(EquipmentTag));
            for (int i = 0; i < tags.Length; i++)
            {
                int count = equipmentSystem.CountTag(tags[i]);
                if (count > 0)
                    GUILayout.Label($"{tags[i]} x{count}");
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private static string FormatTags(BattleEquipmentSO equipment)
    {
        if (equipment == null || equipment.tags == null || equipment.tags.Count == 0)
            return "None";

        string result = string.Empty;
        for (int i = 0; i < equipment.tags.Count; i++)
        {
            if (i > 0) result += ", ";
            result += equipment.tags[i].ToString();
        }

        return result;
    }

    private static Rect CenterRect(float width, float height)
    {
        width = Mathf.Min(width, Screen.width - 24f);
        height = Mathf.Min(height, Screen.height - 24f);
        return new Rect(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);
    }
}

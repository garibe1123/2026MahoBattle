using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 엔진 테스트용 IMGUI입니다.
/// 좌측 레거시 디버그 패널은 Debug Action Map의 F1로 켜고 끌 수 있으며,
/// 우측 상단 KILL ALL ENEMY 퀵 액션과 맵 선택용 BATTLE RATING 테스트 선택기를 제공합니다.
/// 테스트 선택기는 Play Mode에서만 보이며 NodeGraphSO 값을 수정하지 않습니다.
/// 릴리스 빌드에서는 입력과 UI가 모두 비활성입니다.
/// </summary>
public class BattleDebugUI : MonoBehaviour
{
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleInputRouter inputRouter;
    [SerializeField] private bool visible = false;

    private static bool sceneHookInstalled;

    private Vector2 scroll;
    private Coroutine killAllRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallSceneHook()
    {
        if (sceneHookInstalled)
            return;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        sceneHookInstalled = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureCurrentSceneHost()
    {
        EnsureEngineTestHost(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureEngineTestHost(scene);
    }

    private static void EnsureEngineTestHost(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;
        if (!Application.isEditor && !Debug.isDebugBuild)
            return;

        BattleDebugUI[] existing = FindObjectsByType<BattleDebugUI>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (existing.Length > 0)
            return;

        GameObject host = new("BattleEngineTestDebugUI");
        BattleDebugUI ui = host.AddComponent<BattleDebugUI>();
        ui.visible = false;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (!Application.isEditor && !Debug.isDebugBuild)
            return;

        ResolveReferences();

        if (inputRouter != null && inputRouter.DebugTogglePressedThisFrame)
            visible = !visible;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (inputRouter == null && Application.isPlaying)
            inputRouter = BattleInputRouter.ResolveOrCreate(this);
    }

    private void OnGUI()
    {
        if (!Application.isEditor && !Debug.isDebugBuild)
            return;

        DrawBattleRatingTestSelector();
        DrawKillAllEnemyButton();

        if (!visible)
            return;

        GUILayout.BeginArea(new Rect(12f, 12f, 420f, Screen.height - 24f), GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("BATTLE VERTICAL SLICE DEBUG");
        GUILayout.Space(6f);

        if (runManager == null)
        {
            GUILayout.Label("BattleRunManager: NULL");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            return;
        }

        GUILayout.Label($"Run Active: {runManager.RunActive}");
        GUILayout.Label($"State: {runManager.State}");
        GUILayout.Label($"Node: {(runManager.CurrentNode != null ? runManager.CurrentNode.id : "-")}");

        if (roomManager != null)
        {
            GUILayout.Label($"Room: {(roomManager.CurrentRoom != null ? roomManager.CurrentRoom.roomId : "-")}");
            GUILayout.Label($"Alive Monsters: {roomManager.AliveMonsterCount}");
            GUILayout.Label($"Combat Cleared: {roomManager.IsCombatCleared}");
            GUILayout.Label($"Exit Open: {roomManager.IsExitOpen}");
        }

        GUILayout.Space(8f);

        if (!runManager.RunActive || runManager.State == BattleRunState.Ended)
        {
            if (GUILayout.Button("START / RESTART RUN", GUILayout.Height(32f)))
                runManager.RestartRun();
        }
        else
        {
            if (GUILayout.Button("QUIT RUN"))
                runManager.QuitRun();
        }

        if (runManager.State == BattleRunState.Reward)
        {
            GUILayout.Space(10f);
            GUILayout.Label("REWARD - choose one");

            for (int i = 0; i < runManager.CurrentRewardChoices.Count; i++)
            {
                BattleEquipmentSO reward = runManager.CurrentRewardChoices[i];
                string label = reward != null ? reward.GetDisplayName() : "NULL REWARD";
                if (GUILayout.Button($"[{i}] {label}", GUILayout.Height(28f)))
                    runManager.SelectReward(i);
            }

            if (equipmentSystem != null && !equipmentSystem.HasFreeUnlockedSlot())
                GUILayout.Label("Inventory full: discard an equipment below or choose a duplicate that can merge.");

            if (GUILayout.Button("SKIP REWARD (DEBUG)"))
                runManager.SkipReward();
        }

        if (runManager.State == BattleRunState.SelectingNode)
        {
            GUILayout.Space(10f);
            GUILayout.Label("NEXT NODE");

            for (int i = 0; i < runManager.NextNodeChoices.Count; i++)
            {
                BattleNodeData node = runManager.NextNodeChoices[i];
                if (node == null)
                    continue;

                int stars = runManager.ResolveBattleRatingStars(node);
                if (GUILayout.Button(
                        $"{node.id} / {node.type} / Depth {node.depth} / {BuildDebugStars(stars)}",
                        GUILayout.Height(28f)))
                {
                    runManager.SelectNextNode(node.id);
                }
            }
        }

        if (runManager.State == BattleRunState.NonCombat)
        {
            GUILayout.Space(10f);
            GUILayout.Label("NON-COMBAT NODE");
            if (GUILayout.Button("RESOLVE NODE (DEBUG)", GUILayout.Height(28f)))
                runManager.ResolveNonCombatNode();
        }

        DrawEquipment();

        GUILayout.Space(10f);
        GUILayout.Label("F1: Toggle Debug UI");

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawBattleRatingTestSelector()
    {
        if (!Application.isPlaying ||
            runManager == null ||
            runManager.State != BattleRunState.SelectingNode)
        {
            return;
        }

        const float width = 350f;
        const float height = 86f;
        const float rightMargin = 12f;
        const float top = 72f;

        Rect area = new(
            Screen.width - width - rightMargin,
            top,
            width,
            height);

        GUILayout.BeginArea(area, GUI.skin.box);
        GUILayout.Label(
            runManager.DebugBattleRatingOverride <= 0
                ? "BATTLE RATING TEST : AUTO"
                : $"BATTLE RATING TEST : {BuildDebugStars(runManager.DebugBattleRatingOverride)}");

        GUILayout.BeginHorizontal();

        DrawBattleRatingOverrideButton("AUTO", 0);
        for (int stars = 1; stars <= 5; stars++)
            DrawBattleRatingOverrideButton($"★{stars}", stars);

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawBattleRatingOverrideButton(string label, int stars)
    {
        if (runManager == null)
            return;

        int current = runManager.DebugBattleRatingOverride;
        bool selected = current == stars;

        Color previous = GUI.backgroundColor;
        if (selected)
            GUI.backgroundColor = new Color(0.25f, 0.75f, 1f, 1f);

        if (GUILayout.Button(label, GUILayout.Height(30f)))
            runManager.SetDebugBattleRatingOverride(stars);

        GUI.backgroundColor = previous;
    }

    private static string BuildDebugStars(int stars)
    {
        stars = Mathf.Clamp(stars, 1, 5);
        return new string('★', stars) + new string('☆', 5 - stars);
    }

    private void DrawKillAllEnemyButton()
    {
        int aliveCount = CountAliveMonsters();
        if (aliveCount <= 0)
            return;

        const float width = 220f;
        const float height = 48f;
        float top = runManager != null && runManager.State == BattleRunState.SelectingNode
            ? 166f
            : 12f;
        Rect area = new(Screen.width - width - 12f, top, width, height);

        Color previousBackground = GUI.backgroundColor;
        bool previousEnabled = GUI.enabled;
        GUI.backgroundColor = new Color(0.88f, 0.18f, 0.18f, 1f);

        GUILayout.BeginArea(area);
        string label = killAllRoutine != null
            ? $"KILLING ENEMIES... ({aliveCount})"
            : $"KILL ALL ENEMY ({aliveCount})";

        GUI.enabled = killAllRoutine == null;
        if (GUILayout.Button(label, GUILayout.Width(width), GUILayout.Height(height)))
            killAllRoutine = StartCoroutine(KillAllEnemiesRoutine());

        GUILayout.EndArea();
        GUI.enabled = previousEnabled;
        GUI.backgroundColor = previousBackground;
    }

    private static int CountAliveMonsters()
    {
        MonsterController[] monsters = FindObjectsByType<MonsterController>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        int count = 0;
        for (int i = 0; i < monsters.Length; i++)
        {
            if (monsters[i] != null && monsters[i].IsAlive)
                count++;
        }

        return count;
    }

    private IEnumerator KillAllEnemiesRoutine()
    {
        float timeoutAt = Time.realtimeSinceStartup + 3f;

        while (Time.realtimeSinceStartup < timeoutAt)
        {
            MonsterController[] monsters = FindObjectsByType<MonsterController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            bool foundAlive = false;
            for (int i = 0; i < monsters.Length; i++)
            {
                MonsterController monster = monsters[i];
                if (monster == null || !monster.IsAlive)
                    continue;

                foundAlive = true;
                ApplyEngineTestLethalDamage(monster);
            }

            if (!foundAlive)
                break;

            yield return null;
        }

        killAllRoutine = null;
    }

    private void ApplyEngineTestLethalDamage(MonsterController monster)
    {
        if (monster == null || !monster.IsAlive)
            return;

        Vector2 facing = monster.Facing.sqrMagnitude > 0.001f
            ? monster.Facing.normalized
            : Vector2.right;

        Vector2 sourcePosition = (Vector2)monster.transform.position - facing * 2f;
        float lethalDamage = Mathf.Max(
            1000000f,
            monster.CurrentHp + monster.ShieldDurability + 1000f);

        DamageContext context = new(
            gameObject,
            sourcePosition,
            lethalDamage,
            1f,
            0f,
            DamageKind.Melee);

        monster.ReceiveDamage(context, lethalDamage);
    }

    private void DrawEquipment()
    {
        if (equipmentSystem == null)
            return;

        GUILayout.Space(12f);
        GUILayout.Label($"EQUIPMENT ({equipmentSystem.UnlockedSlotCount}/{BattleEquipmentSystem.MaxSlotCount})");

        for (int i = 0; i < equipmentSystem.UnlockedSlotCount; i++)
        {
            BattleEquipmentSlot slot = equipmentSystem.Slots[i];
            if (slot == null || slot.equipment == null)
            {
                GUILayout.Label($"[{i + 1}] EMPTY");
                continue;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"[{i + 1}] {slot.equipment.GetDisplayName()} G{slot.grade} ({slot.copies}/3)");

            if (slot.equipment.shootingData != null && GUILayout.Button("Equip", GUILayout.Width(58f)))
                equipmentSystem.EquipSlot(i);

            if (GUILayout.Button("Discard", GUILayout.Width(68f)))
            {
                equipmentSystem.DiscardSlot(i);
                GUILayout.EndHorizontal();
                break;
            }

            GUILayout.EndHorizontal();
        }
    }
}

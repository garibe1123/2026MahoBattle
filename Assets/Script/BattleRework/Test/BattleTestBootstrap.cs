using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// BattleTestScene entry point. Core installation/validation belongs to BattleSceneManager.
/// The old IMGUI panels are now a fallback only: the runtime BattleHUD / show UI take priority.
/// </summary>
[RequireComponent(typeof(BattleSceneManager))]
public class BattleTestBootstrap : MonoBehaviour
{
    [SerializeField] private BattleSceneManager sceneManager;
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private MonsterPool monsterPool;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleRewardSystem rewardSystem;
    [SerializeField] private SynergyManager synergyManager;
    [SerializeField] private RoomBaseTemplate roomBaseTemplate;

    [Header("Test Startup")]
    [SerializeField] private bool ensureDummyUI = false;
    [Tooltip("false면 BattleSceneEntry 또는 외부 진입 흐름이 START를 담당합니다.")]
    [SerializeField] private bool autoStartRun = false;

    private static bool sceneHookInstalled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallSceneHook()
    {
        if (sceneHookInstalled)
            return;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        sceneHookInstalled = true;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || !scene.isLoaded || scene.name != BattleSceneEntry.DefaultBattleSceneName)
            return;
        if (!Application.isEditor && !Debug.isDebugBuild)
            return;
        if (FindFirstObjectByType<BattleTestBootstrap>() != null)
            return;

        GameObject root = GameObject.Find("BattleSystems");
        if (root == null)
            root = new GameObject("BattleSystems");
        root.AddComponent<BattleTestBootstrap>();
    }

    private void Awake()
    {
        ResolveSceneManager();
        ResolveFromSceneManager();
        EnsureDummyUiComponents();
    }

    private void Start()
    {
        if (Time.timeScale <= 0f)
            Time.timeScale = 1f;

        ResolveSceneManager();
        ResolveFromSceneManager();
        RefreshDummyUiReferences();

        if (!ValidateTestScene(out string report))
        {
            Debug.LogError($"[BattleTest] START BLOCKED. Scene validation failed.\n{report}");
            return;
        }

        Debug.Log("[BattleTest] Scene validation passed.");
        if (autoStartRun && runManager != null && !runManager.RunActive)
        {
            if (sceneManager != null) sceneManager.TryStartRun();
            else runManager.StartRun();
        }
    }

    private void ResolveSceneManager()
    {
        if (sceneManager == null) sceneManager = GetComponent<BattleSceneManager>();
        if (sceneManager == null) sceneManager = FindFirstObjectByType<BattleSceneManager>();
    }

    private void EnsureDummyUiComponents()
    {
        // The polished broadcast HUD is the normal test presentation. Old IMGUI stays opt-in only.
        if (!ensureDummyUI || FindFirstObjectByType<BattleHUD>() != null)
            return;

        if (FindFirstObjectByType<BattleDummyUI>() == null)
            gameObject.AddComponent<BattleDummyUI>();
        if (FindFirstObjectByType<PlayerLoadout>() != null && FindFirstObjectByType<BattleDummyLoadoutUI>() == null)
            gameObject.AddComponent<BattleDummyLoadoutUI>();
        if (FindFirstObjectByType<SynergyDummyUI>() == null)
            gameObject.AddComponent<SynergyDummyUI>();
    }

    private static void RefreshDummyUiReferences()
    {
        BattleDummyUI dummy = FindFirstObjectByType<BattleDummyUI>();
        if (dummy != null) dummy.AutoFindReferences();
    }

    [ContextMenu("Validate Battle Test Scene")]
    public void ValidateFromContextMenu()
    {
        ResolveSceneManager();
        ResolveFromSceneManager();
        bool valid = ValidateTestScene(out string report);
        if (valid) Debug.Log("[BattleTest] Validation passed.");
        else Debug.LogError($"[BattleTest] Validation failed.\n{report}");
    }

    public bool ValidateTestScene(out string report)
    {
        List<string> errors = new();
        if (sceneManager == null)
            errors.Add("BattleSceneManager is missing.");
        else if (!sceneManager.ValidateStartGate(out string sceneReport))
            errors.Add($"BattleSceneManager not ready:\n{sceneReport}");

        if (runManager == null)
            errors.Add("runManager is null");
        else if (!runManager.ValidateConfiguration(out string runReport))
            errors.Add($"BattleRunManager invalid:\n{runReport}");

        report = string.Join("\n", errors);
        return errors.Count == 0;
    }

    private void ResolveFromSceneManager()
    {
        if (sceneManager == null)
            return;

        runManager = sceneManager.RunManager;
        roomManager = sceneManager.RoomManager;
        monsterPool = sceneManager.MonsterPool;
        if (equipmentSystem == null) equipmentSystem = GetComponent<BattleEquipmentSystem>();
        if (rewardSystem == null) rewardSystem = GetComponent<BattleRewardSystem>();
        if (synergyManager == null) synergyManager = GetComponent<SynergyManager>();
        if (roomBaseTemplate == null) roomBaseTemplate = GetComponent<RoomBaseTemplate>();
    }
}

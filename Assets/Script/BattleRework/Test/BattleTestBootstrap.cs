using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// BattleTestScene 전용 진입점입니다.
/// 필수 시스템 설치/배선/시작 Gate는 BattleSceneManager가 전담하고,
/// 이 컴포넌트는 Dummy UI와 테스트 자동 시작만 담당합니다.
///
/// Editor/Development Build에서 BattleScene에 실제 Canvas가 하나도 없으면
/// 테스트가 Reward/Node Selection에서 멈추지 않도록 자동 설치됩니다.
/// 실제 UI Canvas가 들어오면 이 fallback은 자동 설치되지 않습니다.
/// </summary>
[RequireComponent(typeof(BattleSceneManager))]
public class BattleTestBootstrap : MonoBehaviour
{
    [SerializeField] private BattleSceneManager sceneManager;

    // 기존 Scene 직렬화 및 BattleSceneManager 자동 배선 호환용 Reference.
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private MonsterPool monsterPool;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleRewardSystem rewardSystem;
    [SerializeField] private SynergyManager synergyManager;
    [SerializeField] private RoomBaseTemplate roomBaseTemplate;

    [Header("Test Startup")]
    [SerializeField] private bool ensureDummyUI = true;
    [Tooltip("false면 BattleSceneEntry 또는 Dummy Run Setup 화면에서 START를 담당합니다.")]
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

        // 최종 빌드에 임시 IMGUI가 끼어들지 않도록 Editor/Development에서만 fallback.
        if (!Application.isEditor && !Debug.isDebugBuild)
            return;

        // 실제 UI가 이미 있으면 사용자의 UI 구성을 우선합니다.
        if (FindFirstObjectByType<Canvas>() != null)
            return;

        BattleTestBootstrap existing = FindFirstObjectByType<BattleTestBootstrap>();
        if (existing != null)
            return;

        GameObject root = GameObject.Find("BattleSystems");
        if (root == null)
            root = new GameObject("BattleSystems");

        // RequireComponent로 BattleSceneManager/Core systems가 없으면 함께 생성됩니다.
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
        // 다른 Scene/분석 모드에서 정지한 timeScale이 남아 있으면 테스트 전투가 전부 멈춰 보입니다.
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

        if (autoStartRun)
        {
            if (sceneManager != null)
                sceneManager.TryStartRun();
            else
                runManager?.StartRun();
        }
    }

    private void ResolveSceneManager()
    {
        if (sceneManager == null)
            sceneManager = GetComponent<BattleSceneManager>();
        if (sceneManager == null)
            sceneManager = FindFirstObjectByType<BattleSceneManager>();
    }

    private void EnsureDummyUiComponents()
    {
        if (!ensureDummyUI)
            return;

        if (FindFirstObjectByType<BattleDummyUI>() == null)
            gameObject.AddComponent<BattleDummyUI>();

        if (FindFirstObjectByType<PlayerLoadout>() != null &&
            FindFirstObjectByType<BattleDummyLoadoutUI>() == null)
        {
            gameObject.AddComponent<BattleDummyLoadoutUI>();
        }

        if (FindFirstObjectByType<SynergyDummyUI>() == null)
            gameObject.AddComponent<SynergyDummyUI>();
    }

    private static void RefreshDummyUiReferences()
    {
        BattleDummyUI dummy = FindFirstObjectByType<BattleDummyUI>();
        if (dummy != null)
            dummy.AutoFindReferences();
    }

    [ContextMenu("Validate Battle Test Scene")]
    public void ValidateFromContextMenu()
    {
        ResolveSceneManager();
        ResolveFromSceneManager();

        bool valid = ValidateTestScene(out string report);
        if (valid)
            Debug.Log("[BattleTest] Validation passed.");
        else
            Debug.LogError($"[BattleTest] Validation failed.\n{report}");
    }

    public bool ValidateTestScene(out string report)
    {
        List<string> errors = new();

        if (sceneManager == null)
        {
            errors.Add("BattleSceneManager is missing.");
        }
        else if (!sceneManager.ValidateStartGate(out string sceneReport))
        {
            errors.Add($"BattleSceneManager not ready:\n{sceneReport}");
        }

        if (runManager == null)
        {
            errors.Add("runManager is null");
        }
        else if (!runManager.ValidateConfiguration(out string runReport))
        {
            errors.Add($"BattleRunManager invalid:\n{runReport}");
        }

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

        if (equipmentSystem == null)
            equipmentSystem = GetComponent<BattleEquipmentSystem>();
        if (rewardSystem == null)
            rewardSystem = GetComponent<BattleRewardSystem>();
        if (synergyManager == null)
            synergyManager = GetComponent<SynergyManager>();
        if (roomBaseTemplate == null)
            roomBaseTemplate = GetComponent<RoomBaseTemplate>();
    }
}

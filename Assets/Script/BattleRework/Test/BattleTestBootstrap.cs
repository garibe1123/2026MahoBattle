using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BattleTestScene 전용 진입점입니다.
/// 필수 시스템 설치/배선/시작 Gate는 BattleSceneManager가 전담하고,
/// 이 컴포넌트는 Dummy UI와 테스트 자동 시작만 담당합니다.
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
    [Tooltip("false면 Dummy Run Setup 화면에서 START를 눌러 시작합니다.")]
    [SerializeField] private bool autoStartRun = false;

    private void Awake()
    {
        if (sceneManager == null)
            sceneManager = GetComponent<BattleSceneManager>();
        if (sceneManager == null)
            sceneManager = FindFirstObjectByType<BattleSceneManager>();

        ResolveFromSceneManager();

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

    private void Start()
    {
        Time.timeScale = 1f;

        ResolveFromSceneManager();

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

    [ContextMenu("Validate Battle Test Scene")]
    public void ValidateFromContextMenu()
    {
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

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BattleTestScene 전용 부트스트랩입니다.
/// 정식 게임 진입 로직과 분리된 상태에서 필수 Reference / SO 데이터를 검사하고
/// 기능성 더미 UI 및 Runtime Room Base를 통해 수직 슬라이스를 시작할 수 있게 합니다.
/// </summary>
public class BattleTestBootstrap : MonoBehaviour
{
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private MonsterPool monsterPool;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleRewardSystem rewardSystem;
    [SerializeField] private SynergyManager synergyManager;
    [SerializeField] private RoomBaseTemplate roomBaseTemplate;

    [Header("Test Startup")]
    [SerializeField] private bool ensureDummyUI = true;
    [SerializeField] private bool ensureSynergyResolver = true;
    [Tooltip("기존에 씬에 수동 배치한 테스트 Field Base를 지워도 Room 템플릿 기준 Base를 자동 생성합니다.")]
    [SerializeField] private bool ensureRuntimeRoomBase = true;
    [Tooltip("false면 Dummy Run Setup 화면에서 START를 눌러 시작합니다.")]
    [SerializeField] private bool autoStartRun = false;

    private void Awake()
    {
        if (ensureSynergyResolver)
        {
            if (synergyManager == null)
                synergyManager = FindFirstObjectByType<SynergyManager>();

            if (synergyManager == null)
                synergyManager = gameObject.AddComponent<SynergyManager>();
        }

        if (ensureRuntimeRoomBase)
        {
            if (roomBaseTemplate == null)
                roomBaseTemplate = FindFirstObjectByType<RoomBaseTemplate>();

            if (roomBaseTemplate == null)
                roomBaseTemplate = gameObject.AddComponent<RoomBaseTemplate>();
        }

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
        // 이전 PlayMode/UI Bullet Time 상태가 테스트 결과에 영향을 주지 않게 초기화합니다.
        Time.timeScale = 1f;

        if (!ValidateTestScene(out string report))
        {
            Debug.LogError($"[BattleTest] Scene validation failed.\n{report}");
            return;
        }

        Debug.Log("[BattleTest] Scene validation passed.");

        if (autoStartRun)
            runManager.StartRun();
    }

    [ContextMenu("Validate Battle Test Scene")]
    public void ValidateFromContextMenu()
    {
        bool valid = ValidateTestScene(out string report);
        if (valid)
            Debug.Log("[BattleTest] Validation passed.");
        else
            Debug.LogError($"[BattleTest] Validation failed.\n{report}");
    }

    public bool ValidateTestScene(out string report)
    {
        List<string> errors = new();

        if (Camera.main == null)
            errors.Add("No Camera tagged MainCamera was found.");

        if (runManager == null)
        {
            errors.Add("runManager is null");
        }
        else if (!runManager.ValidateConfiguration(out string runReport))
        {
            errors.Add($"BattleRunManager invalid:\n{runReport}");
        }

        if (roomManager == null)
        {
            errors.Add("roomManager is null");
        }
        else if (!roomManager.ValidateConfiguration(out string roomReport))
        {
            errors.Add($"BattleRoomManager invalid:\n{roomReport}");
        }

        if (monsterPool == null)
        {
            errors.Add("monsterPool is null");
        }
        else if (!monsterPool.ValidateConfiguration(out string poolReport))
        {
            errors.Add($"MonsterPool invalid:\n{poolReport}");
        }

        if (equipmentSystem == null)
            errors.Add("equipmentSystem is null");

        if (rewardSystem == null)
        {
            errors.Add("rewardSystem is null");
        }
        else if (!rewardSystem.ValidateConfiguration(out string rewardReport))
        {
            errors.Add($"BattleRewardSystem invalid:\n{rewardReport}");
        }

        if (ensureSynergyResolver)
        {
            if (synergyManager == null)
            {
                errors.Add("synergyManager is null");
            }
            else if (!synergyManager.ValidateConfiguration(out string synergyReport))
            {
                errors.Add($"SynergyManager invalid:\n{synergyReport}");
            }
        }

        if (ensureRuntimeRoomBase && roomBaseTemplate == null)
            errors.Add("roomBaseTemplate is null");

        report = string.Join("\n", errors);
        return errors.Count == 0;
    }
}

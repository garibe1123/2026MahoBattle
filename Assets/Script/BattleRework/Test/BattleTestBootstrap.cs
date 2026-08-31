using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BattleTestScene 전용 부트스트랩입니다.
/// 정식 게임 진입 로직과 분리된 상태에서 필수 Reference / SO 데이터를 검사하고
/// 수직 슬라이스 런을 자동 시작합니다.
/// </summary>
public class BattleTestBootstrap : MonoBehaviour
{
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private MonsterPool monsterPool;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleRewardSystem rewardSystem;
    [SerializeField] private bool autoStartRun = true;

    private void Start()
    {
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

        report = string.Join("\n", errors);
        return errors.Count == 0;
    }
}

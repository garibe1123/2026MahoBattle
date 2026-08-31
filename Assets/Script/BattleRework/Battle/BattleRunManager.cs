using System;
using System.Collections.Generic;
using UnityEngine;

public enum RunEndReason
{
    Clear,
    Death,
    Quit
}

public enum BattleRunState
{
    None,
    EnteringNode,
    BuildingRoom,
    Combat,
    Reward,
    ExitingRoom,
    SelectingNode,
    NonCombat,
    Ended
}

/// <summary>
/// 한 런의 최상위 Flow를 관리합니다.
/// Node 진입 -> Room 생성 -> Combat -> Reward -> Exit -> 다음 Node 선택을
/// 명시적인 상태 머신으로 관리하며, Room 내부 구현과 보상/진행 로직을 분리합니다.
/// </summary>
public class BattleRunManager : MonoBehaviour
{
    [Header("Run Definition")]
    [SerializeField] private NodeGraphSO nodeGraph;
    [SerializeField] private ClanDefinitionSO clan;
    [SerializeField] private ShootingThemeSO shootingTheme;

    [Header("Systems")]
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private RunProgressSystem progress;
    [SerializeField] private BattleRewardSystem rewardSystem;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private PlayerController playerController;

    [Header("Depth Scaling - inspector driven")]
    [SerializeField] private AnimationCurve hpByDepth = AnimationCurve.Linear(0f, 1f, 10f, 1f);
    [SerializeField] private AnimationCurve damageByDepth = AnimationCurve.Linear(0f, 1f, 10f, 1f);

    [Header("Elite Node provisional multiplier")]
    [SerializeField] private float eliteHpMultiplier = 1.5f;
    [SerializeField] private float eliteDamageMultiplier = 1.5f;

    private readonly List<BattleNodeData> nextNodeChoices = new();
    private readonly List<BattleEquipmentSO> currentRewardChoices = new();

    private BattleNodeData currentNode;
    private BattleContext currentContext;
    private BattleRunState state = BattleRunState.None;
    private bool runActive;

    public BattleNodeData CurrentNode => currentNode;
    public BattleContext CurrentContext => currentContext;
    public BattleRunState State => state;
    public bool RunActive => runActive;
    public bool WaitingForNodeSelection => state == BattleRunState.SelectingNode;
    public IReadOnlyList<BattleNodeData> NextNodeChoices => nextNodeChoices;
    public IReadOnlyList<BattleEquipmentSO> CurrentRewardChoices => currentRewardChoices;

    public event Action<BattleRunState> StateChanged;
    public event Action<BattleNodeData> NodeEntered;
    public event Action<IReadOnlyList<BattleNodeData>> NextNodeSelectionRequested;
    public event Action<BattleNodeData> NonCombatNodeEntered;
    public event Action<IReadOnlyList<BattleEquipmentSO>> RewardSelectionRequested;
    public event Action<BattleEquipmentSO> RewardSelected;
    public event Action<RunEndReason> RunEnded;

    private void OnEnable()
    {
        if (roomManager != null)
        {
            roomManager.RoomCombatStarted += HandleRoomCombatStarted;
            roomManager.RoomCombatCleared += HandleRoomCombatCleared;
            roomManager.RoomExited += HandleRoomExited;
            roomManager.MonsterDefeated += HandleMonsterDefeated;
        }

        if (playerController != null)
            playerController.Died += HandlePlayerDeath;
    }

    private void OnDisable()
    {
        if (roomManager != null)
        {
            roomManager.RoomCombatStarted -= HandleRoomCombatStarted;
            roomManager.RoomCombatCleared -= HandleRoomCombatCleared;
            roomManager.RoomExited -= HandleRoomExited;
            roomManager.MonsterDefeated -= HandleMonsterDefeated;
        }

        if (playerController != null)
            playerController.Died -= HandlePlayerDeath;
    }

    public bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();

        if (nodeGraph == null)
            errors.Add("nodeGraph is null");
        else if (!nodeGraph.ValidateGraph(out string graphReport))
            errors.Add($"NodeGraph invalid:\n{graphReport}");

        if (roomManager == null)
            errors.Add("roomManager is null");
        else if (!roomManager.ValidateConfiguration(out string roomReport))
            errors.Add($"BattleRoomManager invalid:\n{roomReport}");

        if (progress == null) errors.Add("progress is null");
        if (rewardSystem == null) errors.Add("rewardSystem is null");
        if (equipmentSystem == null) errors.Add("equipmentSystem is null");
        if (playerController == null) errors.Add("playerController is null");

        if (rewardSystem != null && !rewardSystem.ValidateConfiguration(out string rewardReport))
            errors.Add($"BattleRewardSystem invalid:\n{rewardReport}");

        report = string.Join("\n", errors);
        return errors.Count == 0;
    }

    public void StartRun()
    {
        if (!ValidateConfiguration(out string report))
        {
            Debug.LogError($"[BattleRun] Cannot start run.\n{report}");
            return;
        }

        if (runActive || roomManager.IsRoomActive)
            roomManager.AbortRoom();

        BattleNodeData start = nodeGraph.GetStartNode();
        if (start == null)
        {
            Debug.LogError("[BattleRun] Start node could not be resolved.");
            return;
        }

        currentRewardChoices.Clear();
        nextNodeChoices.Clear();
        currentNode = null;
        currentContext = null;

        playerController.ResetForRun();
        runActive = true;
        progress.BeginRun();
        EnterNode(start);
    }

    public void RestartRun()
    {
        roomManager?.AbortRoom();
        runActive = false;
        SetState(BattleRunState.None);
        StartRun();
    }

    public void SelectNextNode(string nodeId)
    {
        if (!runActive || state != BattleRunState.SelectingNode)
            return;

        BattleNodeData selected = null;
        for (int i = 0; i < nextNodeChoices.Count; i++)
        {
            if (nextNodeChoices[i] != null && nextNodeChoices[i].id == nodeId)
            {
                selected = nextNodeChoices[i];
                break;
            }
        }

        if (selected == null)
        {
            Debug.LogWarning($"[BattleRun] Node '{nodeId}' is not selectable from the current branch.");
            return;
        }

        nextNodeChoices.Clear();
        EnterNode(selected);
    }

    public void ResolveNonCombatNode()
    {
        if (!runActive || currentNode == null || state != BattleRunState.NonCombat)
            return;

        CompleteCurrentNode();
    }

    public bool SelectReward(int rewardIndex)
    {
        if (!runActive || state != BattleRunState.Reward)
            return false;

        if (rewardIndex < 0 || rewardIndex >= currentRewardChoices.Count)
            return false;

        BattleEquipmentSO selected = currentRewardChoices[rewardIndex];
        if (selected == null)
            return false;

        if (!equipmentSystem.TryAcquire(selected))
        {
            Debug.LogWarning("[BattleRun] Reward could not be acquired. Inventory may be full. Replace/discard a slot before selecting again.");
            return false;
        }

        currentRewardChoices.Clear();
        RewardSelected?.Invoke(selected);
        OpenRoomExitAfterReward();
        return true;
    }

    /// <summary>
    /// 테스트/특수 노드에서 보상 없이 진행해야 할 때 사용합니다.
    /// 정식 Combat Reward UI에서는 SelectReward를 우선 사용합니다.
    /// </summary>
    public void SkipReward()
    {
        if (!runActive || state != BattleRunState.Reward)
            return;

        currentRewardChoices.Clear();
        OpenRoomExitAfterReward();
    }

    public void NotifyPlayerDeath()
    {
        HandlePlayerDeath();
    }

    public void QuitRun()
    {
        if (!runActive) return;
        EndRun(RunEndReason.Quit);
    }

    private void HandlePlayerDeath()
    {
        if (!runActive) return;
        EndRun(RunEndReason.Death);
    }

    private void EnterNode(BattleNodeData node)
    {
        if (node == null)
        {
            Debug.LogError("[BattleRun] Cannot enter a null node.");
            EndRun(RunEndReason.Quit);
            return;
        }

        SetState(BattleRunState.EnteringNode);
        currentNode = node;
        currentContext = BuildContext(node);
        NodeEntered?.Invoke(node);

        switch (node.type)
        {
            case BattleNodeType.Combat:
            case BattleNodeType.Elite:
                if (node.room == null)
                {
                    Debug.LogError($"[BattleRun] Combat node '{node.id}' has no RoomDefinitionSO.");
                    EndRun(RunEndReason.Quit);
                    return;
                }

                SetState(BattleRunState.BuildingRoom);
                roomManager.EnterRoom(node.room, currentContext);
                break;

            case BattleNodeType.Shop:
            case BattleNodeType.Event:
                SetState(BattleRunState.NonCombat);
                NonCombatNodeEntered?.Invoke(node);
                break;
        }
    }

    private BattleContext BuildContext(BattleNodeData node)
    {
        BattleContext context = new();

        float depthHp = Mathf.Max(0.01f, hpByDepth.Evaluate(node.depth));
        float depthDamage = Mathf.Max(0.01f, damageByDepth.Evaluate(node.depth));

        float nodeHp = 1f;
        float nodeDamage = 1f;
        if (node.type == BattleNodeType.Elite)
        {
            nodeHp = Mathf.Max(1f, eliteHpMultiplier);
            nodeDamage = Mathf.Max(1f, eliteDamageMultiplier);
        }

        VillainGrade grade = progress != null
            ? progress.CurrentVillainGrade
            : VillainGrade.C;

        context.Configure(
            node.depth,
            grade,
            clan,
            shootingTheme,
            depthHp,
            depthDamage,
            nodeHp,
            nodeDamage);

        return context;
    }

    private void HandleRoomCombatStarted(RoomDefinitionSO room)
    {
        if (!runActive || currentNode == null || currentNode.room != room)
            return;

        SetState(BattleRunState.Combat);
    }

    private void HandleRoomCombatCleared(RoomDefinitionSO room)
    {
        if (!runActive || currentNode == null || currentNode.room != room)
            return;

        currentRewardChoices.Clear();
        List<BattleEquipmentSO> generated = rewardSystem.GenerateChoices(shootingTheme);

        if (generated.Count == 0)
        {
            Debug.LogWarning("[BattleRun] No reward choices were generated. Opening Room exit directly.");
            OpenRoomExitAfterReward();
            return;
        }

        currentRewardChoices.AddRange(generated);
        SetState(BattleRunState.Reward);
        RewardSelectionRequested?.Invoke(currentRewardChoices);
    }

    private void HandleMonsterDefeated(MonsterController monster)
    {
        if (!runActive || monster == null)
            return;

        int point = 1;
        if (monster.Definition != null)
            point = Mathf.Max(0, monster.Definition.killPointReward);

        progress?.AddMonsterKillPoints(point);
    }

    private void OpenRoomExitAfterReward()
    {
        SetState(BattleRunState.ExitingRoom);
        roomManager.OpenExit();
    }

    private void HandleRoomExited(RoomDefinitionSO room)
    {
        if (!runActive || currentNode == null)
            return;

        if (currentNode.room != room)
            return;

        CompleteCurrentNode();
    }

    private void CompleteCurrentNode()
    {
        if (currentNode == null)
            return;

        if (currentNode.isTerminal)
        {
            EndRun(RunEndReason.Clear);
            return;
        }

        List<BattleNodeData> next = nodeGraph.GetNextNodes(currentNode);
        if (next.Count == 0)
        {
            Debug.LogWarning($"[BattleRun] Node '{currentNode.id}' is not terminal but has no next node. Treating it as run clear.");
            EndRun(RunEndReason.Clear);
            return;
        }

        nextNodeChoices.Clear();
        nextNodeChoices.AddRange(next);
        SetState(BattleRunState.SelectingNode);
        NextNodeSelectionRequested?.Invoke(nextNodeChoices);
    }

    private void EndRun(RunEndReason reason)
    {
        if (!runActive && state == BattleRunState.Ended)
            return;

        runActive = false;
        currentRewardChoices.Clear();
        nextNodeChoices.Clear();

        if (reason != RunEndReason.Clear)
            roomManager?.AbortRoom();

        progress?.EndRun();
        SetState(BattleRunState.Ended);
        RunEnded?.Invoke(reason);
    }

    private void SetState(BattleRunState next)
    {
        if (state == next)
            return;

        state = next;
        StateChanged?.Invoke(state);
    }
}

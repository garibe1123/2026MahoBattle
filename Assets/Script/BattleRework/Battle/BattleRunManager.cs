using System;
using System.Collections.Generic;
using UnityEngine;

public enum RunEndReason
{
    Clear,
    Death,
    Quit
}

/// <summary>
/// 한 런의 Branch/Node 진행을 관리합니다.
/// 웨이브 개념은 사용하지 않습니다.
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

    [Header("Depth Scaling - Open Issue, inspector driven")]
    [SerializeField] private AnimationCurve hpByDepth = AnimationCurve.Linear(0f, 1f, 10f, 1f);
    [SerializeField] private AnimationCurve damageByDepth = AnimationCurve.Linear(0f, 1f, 10f, 1f);

    [Header("Elite Node provisional multiplier")]
    [SerializeField] private float eliteHpMultiplier = 1.5f;
    [SerializeField] private float eliteDamageMultiplier = 1.5f;

    private BattleNodeData currentNode;
    private BattleContext currentContext;
    private bool runActive;
    private bool waitingForNodeSelection;

    public BattleNodeData CurrentNode => currentNode;
    public BattleContext CurrentContext => currentContext;
    public bool RunActive => runActive;
    public bool WaitingForNodeSelection => waitingForNodeSelection;

    public event Action<BattleNodeData> NodeEntered;
    public event Action<IReadOnlyList<BattleNodeData>> NextNodeSelectionRequested;
    public event Action<BattleNodeData> NonCombatNodeEntered;
    public event Action<RunEndReason> RunEnded;

    private void OnEnable()
    {
        if (roomManager != null)
            roomManager.RoomExited += HandleRoomExited;
    }

    private void OnDisable()
    {
        if (roomManager != null)
            roomManager.RoomExited -= HandleRoomExited;
    }

    public void StartRun()
    {
        if (nodeGraph == null)
        {
            Debug.LogError("[BattleRun] NodeGraphSO가 지정되지 않았습니다.");
            return;
        }

        BattleNodeData start = nodeGraph.GetStartNode();
        if (start == null)
        {
            Debug.LogError("[BattleRun] 시작 Node를 찾을 수 없습니다.");
            return;
        }

        runActive = true;
        waitingForNodeSelection = false;
        progress?.BeginRun();
        EnterNode(start);
    }

    public void SelectNextNode(string nodeId)
    {
        if (!runActive || !waitingForNodeSelection) return;

        List<BattleNodeData> available = nodeGraph.GetNextNodes(currentNode);
        BattleNodeData selected = null;

        for (int i = 0; i < available.Count; i++)
        {
            if (available[i].id == nodeId)
            {
                selected = available[i];
                break;
            }
        }

        if (selected == null)
        {
            Debug.LogWarning($"[BattleRun] 현재 Branch에서 선택할 수 없는 Node입니다: {nodeId}");
            return;
        }

        waitingForNodeSelection = false;
        EnterNode(selected);
    }

    /// <summary>
    /// 상점/선택지 이벤트 노드가 외부 UI/시스템에서 해결되었을 때 호출합니다.
    /// </summary>
    public void ResolveNonCombatNode()
    {
        if (!runActive || currentNode == null) return;
        if (currentNode.type == BattleNodeType.Combat || currentNode.type == BattleNodeType.Elite) return;

        CompleteCurrentNode();
    }

    public void NotifyPlayerDeath()
    {
        if (!runActive) return;
        EndRun(RunEndReason.Death);
    }

    public void QuitRun()
    {
        if (!runActive) return;
        EndRun(RunEndReason.Quit);
    }

    private void EnterNode(BattleNodeData node)
    {
        currentNode = node;
        currentContext = BuildContext(node);
        NodeEntered?.Invoke(node);

        switch (node.type)
        {
            case BattleNodeType.Combat:
            case BattleNodeType.Elite:
                if (node.room == null)
                {
                    Debug.LogError($"[BattleRun] Combat Node '{node.id}'에 RoomDefinitionSO가 없습니다.");
                    return;
                }

                roomManager.EnterRoom(node.room, currentContext);
                break;

            case BattleNodeType.Shop:
            case BattleNodeType.Event:
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

    private void HandleRoomExited(RoomDefinitionSO room)
    {
        if (!runActive || currentNode == null) return;
        if (currentNode.room != room) return;

        CompleteCurrentNode();
    }

    private void CompleteCurrentNode()
    {
        if (currentNode.isTerminal)
        {
            EndRun(RunEndReason.Clear);
            return;
        }

        List<BattleNodeData> next = nodeGraph.GetNextNodes(currentNode);
        if (next.Count == 0)
        {
            // 그래프 데이터상 다음 노드가 없으면 terminal 누락으로 간주하고 안전하게 종료합니다.
            Debug.LogWarning($"[BattleRun] Node '{currentNode.id}'는 terminal이 아니지만 다음 Node가 없습니다.");
            EndRun(RunEndReason.Clear);
            return;
        }

        waitingForNodeSelection = true;
        NextNodeSelectionRequested?.Invoke(next);
    }

    private void EndRun(RunEndReason reason)
    {
        runActive = false;
        waitingForNodeSelection = false;
        progress?.EndRun();
        RunEnded?.Invoke(reason);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

public class BattleRunManager : MonoBehaviour
{
    [Header("Run Data")]
    [SerializeField] private NodeGraphSO graph;
    [SerializeField] private ClanDefinitionSO clan;
    [SerializeField] private ShootingThemeSO shootingTheme;

    [Header("References")]
    [SerializeField] private BattleRoomManager roomManager;

    private NodeDefinition currentNode;
    private VillainGrade currentVillainGrade = VillainGrade.C;
    private bool runEnded;

    public NodeDefinition CurrentNode => currentNode;
    public bool RunEnded => runEnded;

    public event Action<NodeDefinition> NodeEntered;
    public event Action<IReadOnlyList<NodeDefinition>> NextNodesPresented;
    public event Action<BattleRunEndReason> RunEndedEvent;

    private void Awake()
    {
        if (roomManager != null)
            roomManager.RoomCleared += HandleRoomCleared;
    }

    private void OnDestroy()
    {
        if (roomManager != null)
            roomManager.RoomCleared -= HandleRoomCleared;
    }

    public void StartRun()
    {
        if (graph == null || roomManager == null)
        {
            Debug.LogError("BattleRunManager: graph 또는 roomManager가 설정되지 않았습니다.");
            return;
        }

        runEnded = false;
        currentVillainGrade = VillainGrade.C;
        EnterNode(graph.startNodeId);
    }

    public void EnterNode(string nodeId)
    {
        if (runEnded) return;

        NodeDefinition node = graph.GetNode(nodeId);
        if (node == null)
        {
            Debug.LogError($"BattleRunManager: Node '{nodeId}'를 찾을 수 없습니다.");
            return;
        }

        currentNode = node;
        NodeEntered?.Invoke(currentNode);

        switch (node.type)
        {
            case BattleNodeType.Combat:
            case BattleNodeType.Elite:
                if (node.room == null)
                {
                    Debug.LogError($"BattleRunManager: 전투 노드 '{node.id}'에 RoomDefinition이 없습니다.");
                    return;
                }

                roomManager.LoadRoom(node.room, BuildContext(node));
                break;

            case BattleNodeType.Shop:
            case BattleNodeType.Event:
                // 상점/선택지 이벤트는 후속 시스템에서 완료 시 CompleteNonCombatNode() 호출.
                break;
        }
    }

    public void CompleteNonCombatNode()
    {
        if (runEnded || currentNode == null) return;
        HandleNodeCompleted();
    }

    private BattleContext BuildContext(NodeDefinition node)
    {
        return new BattleContext
        {
            nodeDepth = node.depth,
            villainGrade = currentVillainGrade,
            clan = clan,
            shootingTheme = shootingTheme
        };
    }

    private void HandleRoomCleared()
    {
        HandleNodeCompleted();
    }

    private void HandleNodeCompleted()
    {
        if (currentNode.isTerminal || currentNode.nextNodeIds == null || currentNode.nextNodeIds.Count == 0)
        {
            EndRun(BattleRunEndReason.Clear);
            return;
        }

        List<NodeDefinition> nextNodes = new();
        foreach (string id in currentNode.nextNodeIds)
        {
            NodeDefinition next = graph.GetNode(id);
            if (next != null) nextNodes.Add(next);
        }

        NextNodesPresented?.Invoke(nextNodes);
    }

    public void SelectNextNode(string nodeId)
    {
        if (runEnded || currentNode == null) return;
        if (!currentNode.nextNodeIds.Contains(nodeId)) return;

        roomManager.ExitCurrentRoom();
        EnterNode(nodeId);
    }

    public void SetVillainGrade(VillainGrade grade)
    {
        currentVillainGrade = grade;
    }

    public void NotifyPlayerDeath()
    {
        EndRun(BattleRunEndReason.BroadcastAccident);
    }

    public void QuitRun()
    {
        EndRun(BattleRunEndReason.Quit);
    }

    private void EndRun(BattleRunEndReason reason)
    {
        if (runEnded) return;
        runEnded = true;
        RunEndedEvent?.Invoke(reason);
    }
}

public enum BattleRunEndReason
{
    Clear,
    BroadcastAccident,
    Quit
}

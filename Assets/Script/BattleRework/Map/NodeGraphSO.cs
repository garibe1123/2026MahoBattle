using System;
using System.Collections.Generic;
using UnityEngine;

public enum BattleNodeType
{
    Combat,
    Elite,
    Shop,
    Event
}

[Serializable]
public class BattleNodeData
{
    public string id;
    public BattleNodeType type = BattleNodeType.Combat;
    [Min(0)] public int depth;
    public bool isTerminal;

    [Header("Combat Room")]
    public RoomDefinitionSO room;

    [Header("Branch")]
    public List<string> nextNodeIds = new();
}

/// <summary>
/// 웨이브를 대체하는 가변 길이 Branch/Node 그래프 데이터입니다.
/// 최하단 노드는 별도 Boss 타입이 아니라 isTerminal 속성으로 종료를 표현합니다.
/// </summary>
[CreateAssetMenu(fileName = "NodeGraph", menuName = "MahoBattle/Node Graph")]
public class NodeGraphSO : ScriptableObject
{
    public string startNodeId;
    public List<BattleNodeData> nodes = new();

    public BattleNodeData FindNode(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) return null;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] != null && nodes[i].id == nodeId)
                return nodes[i];
        }

        return null;
    }

    public BattleNodeData GetStartNode()
    {
        return FindNode(startNodeId);
    }

    public List<BattleNodeData> GetNextNodes(BattleNodeData current)
    {
        List<BattleNodeData> result = new();
        if (current == null) return result;

        for (int i = 0; i < current.nextNodeIds.Count; i++)
        {
            BattleNodeData node = FindNode(current.nextNodeIds[i]);
            if (node != null)
                result.Add(node);
        }

        return result;
    }
}

using System;
using System.Collections.Generic;
using System.Text;
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
/// 테스트 시작 전에 ID/Room뿐 아니라 시작점 기준 cycle과 terminal 도달 가능성도 검사합니다.
/// </summary>
[CreateAssetMenu(fileName = "NodeGraph", menuName = "MahoBattle/Node Graph")]
public class NodeGraphSO : ScriptableObject
{
    public string startNodeId;
    public List<BattleNodeData> nodes = new();

    public BattleNodeData FindNode(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || nodes == null) return null;

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
        if (current == null || current.nextNodeIds == null) return result;

        for (int i = 0; i < current.nextNodeIds.Count; i++)
        {
            BattleNodeData node = FindNode(current.nextNodeIds[i]);
            if (node != null)
                result.Add(node);
        }

        return result;
    }

    public bool ValidateGraph(out string report)
    {
        StringBuilder errors = new();
        StringBuilder warnings = new();
        HashSet<string> ids = new();

        if (nodes == null || nodes.Count == 0)
            errors.AppendLine("Node list is empty.");

        if (string.IsNullOrWhiteSpace(startNodeId))
            errors.AppendLine("startNodeId is empty.");

        if (nodes != null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                BattleNodeData node = nodes[i];
                if (node == null)
                {
                    errors.AppendLine($"nodes[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.id))
                {
                    errors.AppendLine($"nodes[{i}] has an empty id.");
                    continue;
                }

                if (!ids.Add(node.id))
                    errors.AppendLine($"Duplicate node id: {node.id}");

                bool combatNode = node.type == BattleNodeType.Combat || node.type == BattleNodeType.Elite;
                if (combatNode && node.room == null)
                {
                    errors.AppendLine($"Combat node '{node.id}' has no RoomDefinitionSO.");
                }
                else if (combatNode)
                {
                    bool roomValid = node.room.ValidateDefinition(out string roomReport);
                    if (!roomValid)
                    {
                        errors.AppendLine($"Room '{node.room.name}' used by node '{node.id}' is invalid:");
                        errors.AppendLine(roomReport);
                    }
                    else if (!string.IsNullOrWhiteSpace(roomReport))
                    {
                        warnings.AppendLine($"Room '{node.room.name}' used by node '{node.id}':");
                        warnings.AppendLine(roomReport);
                    }
                }

                if (node.nextNodeIds == null)
                    node.nextNodeIds = new List<string>();

                if (node.isTerminal && node.nextNodeIds.Count > 0)
                    warnings.AppendLine($"Terminal node '{node.id}' still has nextNodeIds. They will be ignored.");
            }
        }

        BattleNodeData startNode = !string.IsNullOrWhiteSpace(startNodeId)
            ? FindNode(startNodeId)
            : null;

        if (!string.IsNullOrWhiteSpace(startNodeId) && startNode == null)
            errors.AppendLine($"Start node '{startNodeId}' does not exist.");

        if (nodes != null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                BattleNodeData node = nodes[i];
                if (node == null || node.nextNodeIds == null) continue;

                for (int n = 0; n < node.nextNodeIds.Count; n++)
                {
                    string nextId = node.nextNodeIds[n];
                    if (string.IsNullOrWhiteSpace(nextId))
                    {
                        errors.AppendLine($"Node '{node.id}' has an empty next node id.");
                        continue;
                    }

                    if (FindNode(nextId) == null)
                        errors.AppendLine($"Node '{node.id}' references missing next node '{nextId}'.");
                }

                if (!node.isTerminal && node.nextNodeIds.Count == 0)
                    warnings.AppendLine($"Node '{node.id}' is not terminal but has no next node. Runtime will treat it as clear.");
            }
        }

        if (startNode != null)
            ValidateReachability(startNode, errors, warnings);

        StringBuilder combined = new();
        if (errors.Length > 0)
        {
            combined.AppendLine("[Errors]");
            combined.Append(errors);
        }

        if (warnings.Length > 0)
        {
            combined.AppendLine("[Warnings]");
            combined.Append(warnings);
        }

        report = combined.ToString().TrimEnd();
        return errors.Length == 0;
    }

    private void ValidateReachability(
        BattleNodeData startNode,
        StringBuilder errors,
        StringBuilder warnings)
    {
        HashSet<string> visited = new();
        HashSet<string> visiting = new();
        bool terminalReachable = false;
        bool cycleDetected = false;

        Traverse(startNode, visited, visiting, ref terminalReachable, ref cycleDetected);

        if (!terminalReachable)
            errors.AppendLine("No terminal node is reachable from startNodeId.");

        if (cycleDetected)
            errors.AppendLine("A reachable NodeGraph cycle was detected. Vertical-slice runs must be finite.");

        if (nodes == null)
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            BattleNodeData node = nodes[i];
            if (node == null || string.IsNullOrWhiteSpace(node.id)) continue;
            if (!visited.Contains(node.id))
                warnings.AppendLine($"Node '{node.id}' is unreachable from startNodeId.");
        }
    }

    private void Traverse(
        BattleNodeData node,
        HashSet<string> visited,
        HashSet<string> visiting,
        ref bool terminalReachable,
        ref bool cycleDetected)
    {
        if (node == null || string.IsNullOrWhiteSpace(node.id))
            return;

        if (visiting.Contains(node.id))
        {
            cycleDetected = true;
            return;
        }

        if (visited.Contains(node.id))
            return;

        visiting.Add(node.id);
        visited.Add(node.id);

        if (node.isTerminal)
            terminalReachable = true;
        else if (node.nextNodeIds != null)
        {
            for (int i = 0; i < node.nextNodeIds.Count; i++)
            {
                BattleNodeData next = FindNode(node.nextNodeIds[i]);
                if (next != null)
                    Traverse(next, visited, visiting, ref terminalReachable, ref cycleDetected);
            }
        }

        visiting.Remove(node.id);
    }
}

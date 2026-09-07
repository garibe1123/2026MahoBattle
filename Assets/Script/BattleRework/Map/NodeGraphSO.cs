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

    [Header("Stage Map Lane")]
    [Tooltip("Optional vertical lane hint for the horizontal Stage Map. X is used as the vertical lane value; depth always flows from left to right.")]
    public bool useExplicitMapPosition;
    public Vector2Int mapPosition;

    [Header("Combat Room")]
    public RoomDefinitionSO room;

    [Header("Branch")]
    public List<string> nextNodeIds = new();
}

/// <summary>
/// Finite roguelite branch graph.
/// Stage depth grows from left to right and same-depth nodes are vertical alternatives.
/// Multiple start nodes are supported so the first selection can also be a real branch.
/// </summary>
[CreateAssetMenu(fileName = "NodeGraph", menuName = "MahoBattle/Node Graph")]
public class NodeGraphSO : ScriptableObject
{
    [Header("Start Choices")]
    [Tooltip("Preferred start choices. All valid entries are selectable from the initial 4x4 Base.")]
    public List<string> startNodeIds = new();

    [Tooltip("Legacy single-start fallback. Used only when startNodeIds is empty or invalid.")]
    public string startNodeId;

    public List<BattleNodeData> nodes = new();

    public BattleNodeData FindNode(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || nodes == null)
            return null;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] != null && nodes[i].id == nodeId)
                return nodes[i];
        }
        return null;
    }

    public List<BattleNodeData> GetStartNodes()
    {
        List<BattleNodeData> result = new();
        HashSet<string> used = new();

        if (startNodeIds != null)
        {
            for (int i = 0; i < startNodeIds.Count; i++)
            {
                BattleNodeData node = FindNode(startNodeIds[i]);
                if (node != null && used.Add(node.id))
                    result.Add(node);
            }
        }

        if (result.Count == 0)
        {
            BattleNodeData legacy = FindNode(startNodeId);
            if (legacy != null)
                result.Add(legacy);
        }

        return result;
    }

    public BattleNodeData GetStartNode()
    {
        List<BattleNodeData> starts = GetStartNodes();
        return starts.Count > 0 ? starts[0] : null;
    }

    public List<BattleNodeData> GetNextNodes(BattleNodeData current)
    {
        List<BattleNodeData> result = new();
        if (current == null || current.nextNodeIds == null)
            return result;

        HashSet<string> used = new();
        for (int i = 0; i < current.nextNodeIds.Count; i++)
        {
            BattleNodeData node = FindNode(current.nextNodeIds[i]);
            if (node != null && used.Add(node.id))
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

                node.nextNodeIds ??= new List<string>();
                if (node.isTerminal && node.nextNodeIds.Count > 0)
                    warnings.AppendLine($"Terminal node '{node.id}' still has nextNodeIds. They will be ignored.");
            }
        }

        List<BattleNodeData> starts = GetStartNodes();
        if (starts.Count == 0)
            errors.AppendLine("No valid start node exists. Add at least one startNodeIds entry or a valid legacy startNodeId.");

        if (startNodeIds != null)
        {
            for (int i = 0; i < startNodeIds.Count; i++)
            {
                string id = startNodeIds[i];
                if (string.IsNullOrWhiteSpace(id))
                    warnings.AppendLine($"startNodeIds[{i}] is empty.");
                else if (FindNode(id) == null)
                    errors.AppendLine($"Start node '{id}' does not exist.");
            }
        }

        if (nodes != null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                BattleNodeData node = nodes[i];
                if (node == null || node.nextNodeIds == null)
                    continue;

                for (int n = 0; n < node.nextNodeIds.Count; n++)
                {
                    string nextId = node.nextNodeIds[n];
                    if (string.IsNullOrWhiteSpace(nextId))
                    {
                        errors.AppendLine($"Node '{node.id}' has an empty next node id.");
                        continue;
                    }

                    BattleNodeData next = FindNode(nextId);
                    if (next == null)
                    {
                        errors.AppendLine($"Node '{node.id}' references missing next node '{nextId}'.");
                        continue;
                    }

                    if (next.depth <= node.depth)
                        warnings.AppendLine($"Node '{node.id}' -> '{next.id}' does not increase depth. Horizontal Stage Map expects rightward progression.");
                }

                if (!node.isTerminal && node.nextNodeIds.Count == 0)
                    warnings.AppendLine($"Node '{node.id}' is not terminal but has no next node. Runtime will treat it as clear.");
            }
        }

        if (starts.Count > 0)
            ValidateReachability(starts, errors, warnings);

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

    private void ValidateReachability(IReadOnlyList<BattleNodeData> startNodes, StringBuilder errors, StringBuilder warnings)
    {
        HashSet<string> visited = new();
        bool terminalReachable = false;
        bool cycleDetected = false;

        for (int i = 0; i < startNodes.Count; i++)
        {
            HashSet<string> visiting = new();
            Traverse(startNodes[i], visited, visiting, ref terminalReachable, ref cycleDetected);
        }

        if (!terminalReachable)
            errors.AppendLine("No terminal node is reachable from the configured start choices.");
        if (cycleDetected)
            errors.AppendLine("A reachable NodeGraph cycle was detected. Battle runs must be finite.");

        if (nodes == null)
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            BattleNodeData node = nodes[i];
            if (node != null && !string.IsNullOrWhiteSpace(node.id) && !visited.Contains(node.id))
                warnings.AppendLine($"Node '{node.id}' is unreachable from every start choice.");
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
        {
            terminalReachable = true;
        }
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

#if UNITY_EDITOR
/// <summary>
/// Keeps the generated vertical-slice TEST_NodeGraph representative of the real branching UI.
/// This touches only the generated TEST asset; authored production NodeGraphs are never rewritten.
/// </summary>
[UnityEditor.InitializeOnLoad]
internal static class BattleTestBranchGraphEditorUpgrade
{
    private const string TestGraphPath = "Assets/Resources/BattleTestDefaults/TEST_NodeGraph.asset";
    private static double nextCheck;

    static BattleTestBranchGraphEditorUpgrade()
    {
        UnityEditor.EditorApplication.update += Tick;
        UnityEditor.EditorApplication.delayCall += Apply;
    }

    private static void Tick()
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (UnityEditor.EditorApplication.timeSinceStartup < nextCheck)
            return;

        nextCheck = UnityEditor.EditorApplication.timeSinceStartup + 1.0d;
        Apply();
    }

    private static void Apply()
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        NodeGraphSO graph = UnityEditor.AssetDatabase.LoadAssetAtPath<NodeGraphSO>(TestGraphPath);
        if (graph == null || graph.nodes == null)
            return;

        RoomDefinitionSO roomA = FindRoom(graph, "TEST_A") ?? FindRoom(graph, "TEST_A_LEFT");
        RoomDefinitionSO roomB = FindRoom(graph, "TEST_B") ?? FindRoom(graph, "TEST_B_RIGHT");
        RoomDefinitionSO roomElite = FindRoom(graph, "TEST_ELITE") ?? FindRoom(graph, "TEST_ELITE_MID");
        if (roomA == null || roomB == null || roomElite == null)
            return;

        bool alreadyBranched = graph.startNodeIds != null && graph.startNodeIds.Count >= 2 && graph.nodes.Count >= 7;
        if (alreadyBranched)
            return;

        graph.startNodeId = "TEST_A_LEFT";
        graph.startNodeIds = new List<string> { "TEST_A_LEFT", "TEST_B_RIGHT" };
        graph.nodes = new List<BattleNodeData>
        {
            Make("TEST_A_LEFT", BattleNodeType.Combat, 0, roomA, false, -1, "TEST_A_MID", "TEST_ELITE_MID"),
            Make("TEST_B_RIGHT", BattleNodeType.Combat, 0, roomB, false, 1, "TEST_B_MID", "TEST_ELITE_MID"),

            Make("TEST_A_MID", BattleNodeType.Combat, 1, roomA, false, -1, "TEST_FINAL_A", "TEST_FINAL_ELITE"),
            Make("TEST_ELITE_MID", BattleNodeType.Elite, 1, roomElite, false, 0, "TEST_FINAL_ELITE"),
            Make("TEST_B_MID", BattleNodeType.Combat, 1, roomB, false, 1, "TEST_FINAL_A", "TEST_FINAL_ELITE"),

            Make("TEST_FINAL_A", BattleNodeType.Combat, 2, roomB, true, -1),
            Make("TEST_FINAL_ELITE", BattleNodeType.Elite, 2, roomElite, true, 1)
        };

        UnityEditor.EditorUtility.SetDirty(graph);
        UnityEditor.AssetDatabase.SaveAssets();
    }

    private static RoomDefinitionSO FindRoom(NodeGraphSO graph, string id)
    {
        BattleNodeData node = graph.FindNode(id);
        return node != null ? node.room : null;
    }

    private static BattleNodeData Make(
        string id,
        BattleNodeType type,
        int depth,
        RoomDefinitionSO room,
        bool terminal,
        int lane,
        params string[] next)
    {
        return new BattleNodeData
        {
            id = id,
            type = type,
            depth = depth,
            room = room,
            isTerminal = terminal,
            useExplicitMapPosition = true,
            mapPosition = new Vector2Int(lane, 0),
            nextNodeIds = next != null ? new List<string>(next) : new List<string>()
        };
    }
}
#endif

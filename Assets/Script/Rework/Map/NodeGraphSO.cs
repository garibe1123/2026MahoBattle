using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Rework/Node Graph", fileName = "NodeGraph")]
public class NodeGraphSO : ScriptableObject
{
    public string startNodeId;
    public List<NodeDefinition> nodes = new();

    public NodeDefinition GetNode(string id)
    {
        return nodes.Find(node => node.id == id);
    }
}

[System.Serializable]
public class NodeDefinition
{
    public string id;
    public BattleNodeType type = BattleNodeType.Combat;
    [Min(0)] public int depth;
    public bool isTerminal;
    public RoomDefinitionSO room;
    public List<string> nextNodeIds = new();
}

public enum BattleNodeType
{
    Combat,
    Elite,
    Shop,
    Event
}

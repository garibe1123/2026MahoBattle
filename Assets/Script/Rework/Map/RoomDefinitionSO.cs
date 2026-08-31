using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Rework/Room Definition", fileName = "RoomDefinition")]
public class RoomDefinitionSO : ScriptableObject
{
    [Header("Block Layout")]
    [Min(4)] public int widthInBlocks = 4;
    [Min(4)] public int heightInBlocks = 4;
    public List<MapBlockPlacement> blocks = new();

    [Header("Combat")]
    public List<MonsterSpawnEntry> monsterSpawns = new();

    [Header("Clear Presentation")]
    public MapBlock highlightBlockPrefab;
}

[System.Serializable]
public struct MapBlockPlacement
{
    public MapBlock prefab;
    public Vector2Int gridPosition;
}

[System.Serializable]
public struct MonsterSpawnEntry
{
    public MonsterDefinitionSO monster;
    public Vector2Int blockPosition;
    public Vector2 localOffset;
    [Min(1)] public int count;
}

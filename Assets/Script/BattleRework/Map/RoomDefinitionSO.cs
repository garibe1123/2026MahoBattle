using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class MapBlockPlacement
{
    public MapBlock prefab;
    public Vector2Int gridPosition;
    public Vector2 entryDirection = Vector2.right;
}

[Serializable]
public class ObstaclePlacement
{
    public BattleObstacle prefab;
    public Vector2 localPosition;
    public float rotationZ;
}

[Serializable]
public class MonsterSpawnEntry
{
    public MonsterDefinitionSO monster;
    public Vector2 localPosition;
    [Min(1)] public int count = 1;
    public float scatterRadius;
}

/// <summary>
/// 하나의 Node에서 사용되는 전투 Room 데이터입니다.
/// Room은 MapBlock 여러 개 + 장애물 + 고정 몬스터 스폰 정보의 조합입니다.
/// </summary>
[CreateAssetMenu(fileName = "RoomDefinition", menuName = "MahoBattle/Room Definition")]
public class RoomDefinitionSO : ScriptableObject
{
    [Header("Room")]
    public string roomId;
    public Vector2Int recommendedGridSize = new(4, 4);

    [Header("Block Layout")]
    public List<MapBlockPlacement> blocks = new();

    [Header("Obstacles")]
    public List<ObstaclePlacement> obstacles = new();

    [Header("Fixed Spawn Points")]
    public List<MonsterSpawnEntry> monsterSpawns = new();

    [Header("Clear Presentation")]
    public MapBlock highlightBlockPrefab;
    public Vector2 highlightBlockOffset = Vector2.zero;
}

using System;
using System.Collections.Generic;
using System.Text;
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
    [Min(0f)] public float scatterRadius;
}

/// <summary>
/// 하나의 Node에서 사용되는 전투 Room 데이터입니다.
/// Room은 MapBlock 여러 개 + 장애물 + 고정 몬스터 스폰 정보의 조합입니다.
/// recommendedGridSize는 Room 템플릿의 기준 Block 개수이며,
/// MapBlock.BlockWorldSize와 결합해 런타임 Base의 실제 월드 크기를 계산합니다.
/// </summary>
[CreateAssetMenu(fileName = "RoomDefinition", menuName = "MahoBattle/Room Definition")]
public class RoomDefinitionSO : ScriptableObject
{
    [Header("Room Template")]
    public string roomId;
    [Tooltip("Room 템플릿의 가로/세로 MapBlock 개수입니다. 기본 4x4이며 MapBlock 하나는 2x2 world unit입니다.")]
    public Vector2Int recommendedGridSize = new(4, 4);

    [Header("Runtime Base")]
    [Tooltip("수동으로 씬에 깔아둔 테스트 Base 대신 Room 진입 시 템플릿 크기의 Base를 자동 생성합니다.")]
    public bool useRuntimeBase = true;
    [Tooltip("기준 Base 크기에 추가할 양쪽 여백(world unit)입니다. (1,1)이면 총 크기가 가로/세로 각각 2씩 증가합니다.")]
    public Vector2 basePaddingWorld = Vector2.zero;
    [Tooltip("현재 grid 좌표계는 (0,0) Block 중심이 roomOrigin입니다. 필요할 때 Base 시각물만 추가로 이동시키는 Offset입니다.")]
    public Vector2 baseOffset = Vector2.zero;

    [Header("Player Entry")]
    [Tooltip("새 Room 진입 시 roomOrigin 기준 플레이어 시작 위치입니다. Room 전환 후 이전 Exit 위치에 남는 문제를 방지합니다.")]
    public Vector2 playerEntryOffset = Vector2.zero;
    public bool repositionPlayerOnEnter = true;

    [Header("Block Layout")]
    public List<MapBlockPlacement> blocks = new();

    [Header("Obstacles")]
    public List<ObstaclePlacement> obstacles = new();

    [Header("Fixed Spawn Points")]
    public List<MonsterSpawnEntry> monsterSpawns = new();

    [Header("Clear Presentation")]
    public MapBlock highlightBlockPrefab;
    [Tooltip("roomOrigin 기준 Highlight Block Offset입니다.")]
    public Vector2 highlightBlockOffset = new(4f, 0f);

    public Vector2Int GetSafeGridSize()
    {
        return new Vector2Int(
            Mathf.Max(1, recommendedGridSize.x),
            Mathf.Max(1, recommendedGridSize.y));
    }

    /// <summary>
    /// 템플릿 전체 Block 영역의 월드 크기입니다.
    /// 기본 4x4라면 8x8 world unit = 64px/unit 기준 512x512px입니다.
    /// </summary>
    public Vector2 GetTemplateWorldSize()
    {
        Vector2Int grid = GetSafeGridSize();
        return new Vector2(
            grid.x * MapBlock.BlockWorldSize.x,
            grid.y * MapBlock.BlockWorldSize.y);
    }

    public Vector2 GetRuntimeBaseWorldSize()
    {
        Vector2 size = GetTemplateWorldSize();
        Vector2 padding = new(
            Mathf.Max(0f, basePaddingWorld.x),
            Mathf.Max(0f, basePaddingWorld.y));

        return size + padding * 2f;
    }

    /// <summary>
    /// 현재 BattleRoomManager 좌표 규칙과 일치하는 Base 중심 Offset입니다.
    /// grid (0,0)의 Block 중심이 roomOrigin이므로 4x4 템플릿은 (3,3)이 Base 중심입니다.
    /// </summary>
    public Vector2 GetTemplateCenterOffset()
    {
        Vector2Int grid = GetSafeGridSize();
        return new Vector2(
            (grid.x - 1) * MapBlock.BlockWorldSize.x * 0.5f,
            (grid.y - 1) * MapBlock.BlockWorldSize.y * 0.5f);
    }

    public Vector2 GetRuntimeBaseCenterOffset()
    {
        return GetTemplateCenterOffset() + baseOffset;
    }

    public Vector2 GetBlockLocalPosition(Vector2Int gridPosition)
    {
        return new Vector2(
            gridPosition.x * MapBlock.BlockWorldSize.x,
            gridPosition.y * MapBlock.BlockWorldSize.y);
    }

    public bool IsInsideTemplateGrid(Vector2Int gridPosition)
    {
        Vector2Int grid = GetSafeGridSize();
        return gridPosition.x >= 0 &&
               gridPosition.y >= 0 &&
               gridPosition.x < grid.x &&
               gridPosition.y < grid.y;
    }

    public bool ValidateDefinition(out string report)
    {
        StringBuilder errors = new();
        StringBuilder warnings = new();

        if (string.IsNullOrWhiteSpace(roomId))
            warnings.AppendLine("roomId is empty.");

        if (recommendedGridSize.x < 1 || recommendedGridSize.y < 1)
            errors.AppendLine("recommendedGridSize must be at least 1x1.");

        if (basePaddingWorld.x < 0f || basePaddingWorld.y < 0f)
            errors.AppendLine("basePaddingWorld cannot contain negative values.");

        if (blocks == null || blocks.Count == 0)
        {
            warnings.AppendLine("No MapBlock placements. The Room may have no generated walkable floor.");
        }
        else
        {
            HashSet<Vector2Int> occupied = new();
            for (int i = 0; i < blocks.Count; i++)
            {
                MapBlockPlacement placement = blocks[i];
                if (placement == null)
                {
                    errors.AppendLine($"blocks[{i}] is null.");
                    continue;
                }

                if (placement.prefab == null)
                    errors.AppendLine($"blocks[{i}] has no prefab.");

                if (!occupied.Add(placement.gridPosition))
                    warnings.AppendLine($"Duplicate MapBlock grid position: {placement.gridPosition}");

                if (!IsInsideTemplateGrid(placement.gridPosition))
                {
                    warnings.AppendLine(
                        $"blocks[{i}] grid {placement.gridPosition} is outside template " +
                        $"0..{Mathf.Max(0, recommendedGridSize.x - 1)}, 0..{Mathf.Max(0, recommendedGridSize.y - 1)}. " +
                        "It will still spawn, but the runtime Base will not automatically expand to that outlier.");
                }
            }
        }

        if (obstacles != null)
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                if (obstacles[i] == null)
                {
                    errors.AppendLine($"obstacles[{i}] is null.");
                    continue;
                }

                if (obstacles[i].prefab == null)
                    errors.AppendLine($"obstacles[{i}] has no prefab.");
            }
        }

        if (monsterSpawns == null || monsterSpawns.Count == 0)
        {
            warnings.AppendLine("No monster spawns. A combat node using this Room will clear immediately.");
        }
        else
        {
            for (int i = 0; i < monsterSpawns.Count; i++)
            {
                MonsterSpawnEntry spawn = monsterSpawns[i];
                if (spawn == null)
                {
                    errors.AppendLine($"monsterSpawns[{i}] is null.");
                    continue;
                }

                if (spawn.monster == null)
                    errors.AppendLine($"monsterSpawns[{i}] has no MonsterDefinitionSO.");

                if (spawn.count < 1)
                    errors.AppendLine($"monsterSpawns[{i}] count must be at least 1.");

                if (spawn.scatterRadius > Mathf.Max(recommendedGridSize.x, recommendedGridSize.y) * 2f)
                    warnings.AppendLine($"monsterSpawns[{i}] scatterRadius is very large for this Room and may push spawns outside the intended area.");
            }
        }

        if (highlightBlockPrefab != null && highlightBlockOffset.sqrMagnitude < 0.01f)
            warnings.AppendLine("highlightBlockOffset is near zero. ExitPad may appear directly under the player entry area.");

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
}

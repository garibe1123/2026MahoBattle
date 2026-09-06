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
///
/// recommendedGridSize는 항상 존재하는 Start Base의 기준 격자입니다.
/// 기본 4x4 = 8x8 world unit이며, 이 영역은 Room 교체 때도 사라지지 않는 전투 기준면입니다.
/// blocks 목록에서 이 격자 안 Placement는 구형 데이터 호환용 Base Cell로 간주되어 런타임 조립에서 생략되고,
/// 격자 밖 Placement만 추가/확장 Block으로 들어와 도킹 연출을 수행합니다.
///
/// Monster Spawn Point는 완성된 Room 내부 좌표입니다.
/// BattleRoomManager가 Room 조립과 NavMesh Bake를 끝낸 뒤 MonsterPool이 해당 좌표 주변의 유효 NavMesh 위치를 찾습니다.
/// </summary>
[CreateAssetMenu(fileName = "RoomDefinition", menuName = "MahoBattle/Room Definition")]
public class RoomDefinitionSO : ScriptableObject
{
    [Header("Persistent Start Base")]
    [Tooltip("기본 4x4 템플릿 영역을 Room마다 재생성하지 않는 영구 Start Base로 사용합니다.")]
    public bool usePersistentStartBase = true;

    [HideInInspector]
    [Tooltip("이전 외곽 Spawn 실험의 직렬화 호환용 값입니다. 현재 런타임에서는 사용하지 않습니다.")]
    public bool forbidMonsterSpawnInsideStartBase = false;

    [Header("Room Template")]
    public string roomId;
    [Tooltip("Start Base의 가로/세로 MapBlock 개수입니다. 기본 4x4이며 MapBlock 하나는 2x2 world unit입니다.")]
    public Vector2Int recommendedGridSize = new(4, 4);

    [Header("Runtime Base")]
    [Tooltip("수동으로 씬에 깔아둔 테스트 Base 대신 Start Base를 자동 생성합니다.")]
    public bool useRuntimeBase = true;
    [Tooltip("기준 Base 크기에 추가할 양쪽 여백(world unit)입니다. (1,1)이면 총 크기가 가로/세로 각각 2씩 증가합니다.")]
    public Vector2 basePaddingWorld = Vector2.zero;
    [Tooltip("grid (0,0) Block 중심이 roomOrigin입니다. 필요할 때 Start Base 시각물만 추가로 이동시키는 Offset입니다.")]
    public Vector2 baseOffset = Vector2.zero;

    [Header("Player Entry")]
    [Tooltip("새 Room 진입 시 roomOrigin 기준 플레이어 시작 위치입니다. Room 전환 후 이전 Exit 위치에 남는 문제를 방지합니다.")]
    public Vector2 playerEntryOffset = Vector2.zero;
    public bool repositionPlayerOnEnter = true;

    [Header("Extension Block Layout")]
    [Tooltip("Start Base 격자 밖 Placement만 실제 조립/도킹 Block으로 사용됩니다. 격자 안 Placement는 구형 4x4 데이터 호환용으로 무시됩니다.")]
    public List<MapBlockPlacement> blocks = new();

    [Header("Obstacles")]
    public List<ObstaclePlacement> obstacles = new();

    [Header("Monster Spawn Points")]
    [Tooltip("완성된 Room 내부의 Spawn 기준점입니다. 런타임에서 가장 가까운 유효 NavMesh 위치로만 보정되며 벽 밖으로 투영하지 않습니다.")]
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
    /// Start Base 전체 Block 영역의 월드 크기입니다.
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
    /// grid (0,0)의 Block 중심이 roomOrigin이므로 4x4 Start Base 중심은 (3,3)입니다.
    /// 실제 4x4 Base 경계는 local -1..7 입니다.
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

    public bool IsStartBaseGridPosition(Vector2Int gridPosition)
    {
        return usePersistentStartBase && IsInsideTemplateGrid(gridPosition);
    }

    /// <summary>
    /// roomOrigin 기준 Start Base local Rect를 반환합니다.
    /// 기본 4x4면 x/y 모두 -1..7 범위입니다.
    /// </summary>
    public Rect GetStartBaseLocalRect(float extraPadding = 0f)
    {
        float padding = Mathf.Max(0f, extraPadding);
        Vector2 center = GetTemplateCenterOffset();
        Vector2 size = GetTemplateWorldSize() + Vector2.one * padding * 2f;
        return new Rect(center - size * 0.5f, size);
    }

    public bool IsInsideStartBaseLocal(Vector2 localPosition, float extraPadding = 0f)
    {
        Rect rect = GetStartBaseLocalRect(extraPadding);
        return localPosition.x >= rect.xMin && localPosition.x <= rect.xMax &&
               localPosition.y >= rect.yMin && localPosition.y <= rect.yMax;
    }

    /// <summary>
    /// 이전 외곽 Spawn 실험과의 API 호환용입니다.
    /// 현재 MonsterPool은 이 함수를 사용하지 않습니다.
    /// </summary>
    public Vector2 ProjectOutsideStartBaseLocal(Vector2 localPosition, float outsideDistance)
    {
        Rect rect = GetStartBaseLocalRect();
        if (!IsInsideStartBaseLocal(localPosition))
            return localPosition;

        float distance = Mathf.Max(0.1f, outsideDistance);
        float left = localPosition.x - rect.xMin;
        float right = rect.xMax - localPosition.x;
        float bottom = localPosition.y - rect.yMin;
        float top = rect.yMax - localPosition.y;
        float nearest = Mathf.Min(left, right, bottom, top);

        Vector2 projected = localPosition;
        if (Mathf.Approximately(nearest, left))
            projected.x = rect.xMin - distance;
        else if (Mathf.Approximately(nearest, right))
            projected.x = rect.xMax + distance;
        else if (Mathf.Approximately(nearest, bottom))
            projected.y = rect.yMin - distance;
        else
            projected.y = rect.yMax + distance;

        return projected;
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

        if (blocks != null)
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

                if (usePersistentStartBase && IsInsideTemplateGrid(placement.gridPosition))
                {
                    warnings.AppendLine(
                        $"blocks[{i}] grid {placement.gridPosition} is inside the Persistent Start Base and will not spawn as an incoming block. " +
                        "Keep it only for legacy layout compatibility or remove it from the Room asset.");
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
            Rect roomRect = GetStartBaseLocalRect();

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
                    warnings.AppendLine($"monsterSpawns[{i}] scatterRadius is very large for this Room.");

                if (usePersistentStartBase && !IsInsideStartBaseLocal(spawn.localPosition, spawn.scatterRadius))
                {
                    warnings.AppendLine(
                        $"monsterSpawns[{i}] at {spawn.localPosition} is outside the current Start Base bounds {roomRect}. " +
                        "Make sure an Extension Block provides walkable NavMesh there, otherwise the spawn can be rejected.");
                }
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

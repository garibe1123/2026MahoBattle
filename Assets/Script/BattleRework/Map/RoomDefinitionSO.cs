using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum RoomLargePieceShape
{
    Auto,
    Rectangle,
    LShape,
    TShape,
    Cross,
    Custom
}

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
/// 하나의 Node에서 사용되는 Gameplay Room 데이터입니다.
///
/// Start Area의 4x4 Base와 Gameplay Room은 서로 다른 공간입니다.
/// recommendedGridSize는 구형 2x2 MapBlock 기준 호환 크기로 유지하지만,
/// 새 기본 표현은 여러 작은 Block을 하나씩 조립하지 않고 큰 Room Piece 하나가 통째로 도킹합니다.
///
/// 큰 Room Piece는 Rectangle / L / T / Cross / Custom cell mask를 지원합니다.
/// 내부적으로 여러 2x2 cell을 사용해도 하나의 Root Transform으로 묶여 한 번에 '쾅' 들어옵니다.
/// 실제 전용 Prefab을 사용하는 기존 blocks 데이터도 계속 호환됩니다.
/// </summary>
[CreateAssetMenu(fileName = "RoomDefinition", menuName = "MahoBattle/Room Definition")]
public class RoomDefinitionSO : ScriptableObject
{
    [Header("Legacy Persistent Base Compatibility")]
    [Tooltip("구형 Room 데이터 호환용입니다. 실제 Start Area는 BattleRunManager가 Room과 별도로 관리합니다.")]
    public bool usePersistentStartBase = true;

    [HideInInspector]
    public bool forbidMonsterSpawnInsideStartBase = false;

    [Header("Room Template")]
    public string roomId;
    [Tooltip("구형 MapBlock 기준 크기입니다. 기본 4x4 = 8x8 world. Large Piece Size가 0이면 이 값을 사용합니다.")]
    public Vector2Int recommendedGridSize = new(4, 4);

    [Header("Large Room Piece")]
    [Tooltip("켜면 BattleSpatialMapController가 기존 4x4 낱개 Block 대신 큰 Room Piece 하나로 조립합니다.")]
    public bool useLargeRoomPiece = true;
    [Tooltip("Auto는 안전하게 Rectangle을 사용합니다. 실제 Room에서는 L/T/Cross/Custom을 직접 지정할 수 있습니다.")]
    public RoomLargePieceShape largePieceShape = RoomLargePieceShape.Auto;
    [Tooltip("큰 조각의 cell 크기입니다. 0 이하 값은 기존 recommendedGridSize로 자동 fallback합니다.")]
    public Vector2Int largePieceGridSize = new(4, 4);
    [Tooltip("Custom일 때 포함할 cell 좌표 목록입니다. 전체가 하나의 Root로 움직입니다.")]
    public List<Vector2Int> customLargePieceCells = new();
    [Tooltip("큰 Room Piece가 어느 방향에서 날아와 도킹할지 지정합니다. 0이면 기본 방향을 사용합니다.")]
    public Vector2 largePieceEntryDirection = Vector2.zero;
    [Min(0.05f)] public float largePieceEntryDuration = 0.72f;
    [Min(0.5f)] public float largePieceEntryOffset = 8f;

    [Header("Runtime Base Compatibility")]
    [Tooltip("구형 RoomBaseTemplate/테스트 호환용입니다.")]
    public bool useRuntimeBase = true;
    public Vector2 basePaddingWorld = Vector2.zero;
    public Vector2 baseOffset = Vector2.zero;

    [Header("Player Entry")]
    public Vector2 playerEntryOffset = Vector2.zero;
    public bool repositionPlayerOnEnter = true;

    [Header("Legacy / Custom Prefab Room Pieces")]
    [Tooltip("전용 MapBlock Prefab을 직접 배치할 때 사용합니다. Large Room Piece가 켜져 있으면 기본 4x4 테스트 배치는 런타임에서 큰 조각으로 치환됩니다.")]
    public List<MapBlockPlacement> blocks = new();

    [Header("Obstacles")]
    public List<ObstaclePlacement> obstacles = new();

    [Header("Monster Spawn Points")]
    [Tooltip("완성된 Gameplay Room 내부 Spawn 기준점입니다.")]
    public List<MonsterSpawnEntry> monsterSpawns = new();

    [Header("Clear Presentation")]
    public MapBlock highlightBlockPrefab;
    public Vector2 highlightBlockOffset = new(4f, 0f);

    public Vector2Int GetSafeGridSize()
    {
        return new Vector2Int(
            Mathf.Max(1, recommendedGridSize.x),
            Mathf.Max(1, recommendedGridSize.y));
    }

    public Vector2Int GetLargePieceGridSize()
    {
        Vector2Int fallback = GetSafeGridSize();
        return new Vector2Int(
            largePieceGridSize.x > 0 ? largePieceGridSize.x : fallback.x,
            largePieceGridSize.y > 0 ? largePieceGridSize.y : fallback.y);
    }

    public Vector2 GetTemplateWorldSize()
    {
        Vector2Int grid = GetSafeGridSize();
        return new Vector2(
            grid.x * MapBlock.BlockWorldSize.x,
            grid.y * MapBlock.BlockWorldSize.y);
    }

    public Vector2 GetLargePieceWorldSize()
    {
        Vector2Int grid = GetLargePieceGridSize();
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

    public Vector2 GetTemplateCenterOffset()
    {
        Vector2Int grid = GetSafeGridSize();
        return new Vector2(
            (grid.x - 1) * MapBlock.BlockWorldSize.x * 0.5f,
            (grid.y - 1) * MapBlock.BlockWorldSize.y * 0.5f);
    }

    public Vector2 GetLargePieceCenterOffset()
    {
        Vector2Int grid = GetLargePieceGridSize();
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
        return gridPosition.x >= 0 && gridPosition.y >= 0 &&
               gridPosition.x < grid.x && gridPosition.y < grid.y;
    }

    public bool IsStartBaseGridPosition(Vector2Int gridPosition)
    {
        return usePersistentStartBase && IsInsideTemplateGrid(gridPosition);
    }

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
        if (Mathf.Approximately(nearest, left)) projected.x = rect.xMin - distance;
        else if (Mathf.Approximately(nearest, right)) projected.x = rect.xMax + distance;
        else if (Mathf.Approximately(nearest, bottom)) projected.y = rect.yMin - distance;
        else projected.y = rect.yMax + distance;
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

        if (largePieceGridSize.x < 1 || largePieceGridSize.y < 1)
            warnings.AppendLine("largePieceGridSize is unset/legacy; recommendedGridSize will be used as fallback.");

        if (largePieceShape == RoomLargePieceShape.Custom &&
            (customLargePieceCells == null || customLargePieceCells.Count == 0))
        {
            errors.AppendLine("Custom Large Room Piece requires at least one customLargePieceCells entry.");
        }

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
            }
        }

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

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
    Irregular,
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
/// Gameplay Room 데이터.
///
/// 신규 공간 규칙:
/// - 시각 Tile 기준은 32px = 1 world unit.
/// - Player 기준 시각 크기는 48px = 1.5 world unit.
/// - Start Area는 기본 3x3 tile의 독립 공간이며 Room이 아닙니다.
/// - Combat Room은 기본 최소 6x6 tile이며 Rectangle/L/T/Cross/Irregular/Custom mask를 지원합니다.
/// - 실제 Room Piece는 여러 tile을 포함해도 Root 하나가 통째로 도킹합니다.
///
/// recommendedGridSize / blocks는 기존 2x2 world MapBlock 데이터 호환용으로 유지합니다.
/// </summary>
[CreateAssetMenu(fileName = "RoomDefinition", menuName = "MahoBattle/Room Definition")]
public class RoomDefinitionSO : ScriptableObject
{
    public const float ProceduralTileWorldSize = 1f;

    [Header("Legacy Persistent Base Compatibility")]
    [Tooltip("구형 Room 데이터 호환용입니다. 실제 Start Area는 BattleRunManager가 Room과 별도로 관리합니다.")]
    public bool usePersistentStartBase = true;

    [HideInInspector]
    public bool forbidMonsterSpawnInsideStartBase = false;

    [Header("Room Identity / Legacy Template")]
    public string roomId;
    [Tooltip("구형 2x2 MapBlock 배치용 크기. 신규 절차형 Room 크기와는 별개입니다.")]
    public Vector2Int recommendedGridSize = new(4, 4);

    [Header("32px Tile / Start Area")]
    [Tooltip("Start Area 바닥 크기. 전투가 시작되지 않는 독립 출발 공간입니다. 최소 3x3을 권장합니다.")]
    public Vector2Int startBaseTileSize = new(3, 3);

    [Header("Procedural Gameplay Room")]
    [Tooltip("켜면 32px tile cell mask를 이용해 Room Shape를 절차적으로 구성합니다.")]
    public bool useProceduralRoom = true;
    [Tooltip("Combat Room 최소 tile 크기. 기본 6x6.")]
    public Vector2Int proceduralMinTileSize = new(6, 6);
    [Tooltip("Combat Room 최대 tile 크기. 매 Room seed에 따라 이 범위 안에서 변합니다.")]
    public Vector2Int proceduralMaxTileSize = new(9, 9);
    [Tooltip("0이면 roomId/node id 기반 deterministic seed를 사용합니다. 0이 아니면 이 값을 seed에 섞습니다.")]
    public int proceduralSeed;
    [Range(0f, 1f)]
    [Tooltip("높을수록 외곽을 깎고 Chunk를 붙여 자유형 실루엣을 만듭니다. 중앙 전투 공간과 연결성은 보존합니다.")]
    public float proceduralComplexity = 0.35f;
    [Range(0f, 0.45f)]
    [Tooltip("Irregular/Auto Room에서 외곽 tile을 깎을 확률 계수입니다.")]
    public float proceduralIndentChance = 0.16f;
    [Range(0f, 0.65f)]
    [Tooltip("Irregular/Auto Room에서 외곽에 작은 덩어리를 붙일 확률 계수입니다.")]
    public float proceduralExtensionChance = 0.28f;

    [Header("Large Room Piece Presentation")]
    [Tooltip("켜면 낱개 Block이 아니라 큰 Room Piece 하나로 도킹합니다.")]
    public bool useLargeRoomPiece = true;
    [Tooltip("Auto는 절차 생성일 때 Irregular 계열, 비절차형이면 Rectangle로 처리합니다.")]
    public RoomLargePieceShape largePieceShape = RoomLargePieceShape.Auto;
    [Tooltip("비절차형 Large Piece용 cell 크기. 0이면 recommendedGridSize fallback.")]
    public Vector2Int largePieceGridSize = new(4, 4);
    [Tooltip("Custom일 때 포함할 32px tile 좌표 목록입니다.")]
    public List<Vector2Int> customLargePieceCells = new();
    [Tooltip("Room Piece가 날아오는 연출 방향. 0이면 접근 방향의 반대쪽에서 들어옵니다.")]
    public Vector2 largePieceEntryDirection = Vector2.zero;
    [Min(0.05f)] public float largePieceEntryDuration = 0.72f;
    [Min(0.5f)] public float largePieceEntryOffset = 8f;

    [Header("Runtime Base Compatibility")]
    public bool useRuntimeBase = true;
    public Vector2 basePaddingWorld = Vector2.zero;
    public Vector2 baseOffset = Vector2.zero;

    [Header("Player Entry")]
    public Vector2 playerEntryOffset = Vector2.zero;
    public bool repositionPlayerOnEnter = true;

    [Header("Legacy / Custom Prefab Room Pieces")]
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

    public Vector2Int GetStartBaseTileSize()
    {
        return new Vector2Int(
            Mathf.Max(3, startBaseTileSize.x),
            Mathf.Max(3, startBaseTileSize.y));
    }

    public Vector2Int GetProceduralMinTileSize()
    {
        return new Vector2Int(
            Mathf.Max(6, proceduralMinTileSize.x),
            Mathf.Max(6, proceduralMinTileSize.y));
    }

    public Vector2Int GetProceduralMaxTileSize()
    {
        Vector2Int min = GetProceduralMinTileSize();
        return new Vector2Int(
            Mathf.Max(min.x, proceduralMaxTileSize.x),
            Mathf.Max(min.y, proceduralMaxTileSize.y));
    }

    public Vector2Int GetLargePieceGridSize()
    {
        if (useProceduralRoom)
            return GetProceduralMinTileSize();

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

    public Vector2 GetStartBaseWorldSize()
    {
        Vector2Int size = GetStartBaseTileSize();
        return new Vector2(size.x, size.y) * ProceduralTileWorldSize;
    }

    public Vector2 GetLargePieceWorldSize()
    {
        Vector2Int grid = GetLargePieceGridSize();
        float tile = useProceduralRoom ? ProceduralTileWorldSize : MapBlock.BlockWorldSize.x;
        return new Vector2(grid.x * tile, grid.y * tile);
    }

    public Vector2 GetRuntimeBaseWorldSize()
    {
        Vector2 size = useProceduralRoom ? GetStartBaseWorldSize() : GetTemplateWorldSize();
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

    public Vector2 GetStartBaseCenterOffset()
    {
        Vector2Int size = GetStartBaseTileSize();
        return new Vector2(
            (size.x - 1) * ProceduralTileWorldSize * 0.5f,
            (size.y - 1) * ProceduralTileWorldSize * 0.5f);
    }

    public Vector2 GetLargePieceCenterOffset()
    {
        Vector2Int grid = GetLargePieceGridSize();
        float tile = useProceduralRoom ? ProceduralTileWorldSize : MapBlock.BlockWorldSize.x;
        return new Vector2(
            (grid.x - 1) * tile * 0.5f,
            (grid.y - 1) * tile * 0.5f);
    }

    public Vector2 GetRuntimeBaseCenterOffset()
    {
        return (useProceduralRoom ? GetStartBaseCenterOffset() : GetTemplateCenterOffset()) + baseOffset;
    }

    public Vector2 GetBlockLocalPosition(Vector2Int gridPosition)
    {
        return new Vector2(
            gridPosition.x * MapBlock.BlockWorldSize.x,
            gridPosition.y * MapBlock.BlockWorldSize.y);
    }

    public Vector2 GetProceduralTileLocalPosition(Vector2Int tilePosition)
    {
        return new Vector2(tilePosition.x, tilePosition.y) * ProceduralTileWorldSize;
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
        Vector2 center = useProceduralRoom ? GetStartBaseCenterOffset() : GetTemplateCenterOffset();
        Vector2 size = (useProceduralRoom ? GetStartBaseWorldSize() : GetTemplateWorldSize()) + Vector2.one * padding * 2f;
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

        if (startBaseTileSize.x < 3 || startBaseTileSize.y < 3)
            warnings.AppendLine("startBaseTileSize below 3x3 will be clamped to 3x3 at runtime.");

        if (useProceduralRoom)
        {
            if (proceduralMinTileSize.x < 6 || proceduralMinTileSize.y < 6)
                warnings.AppendLine("proceduralMinTileSize below 6x6 will be clamped to 6x6 at runtime.");

            if (proceduralMaxTileSize.x < proceduralMinTileSize.x || proceduralMaxTileSize.y < proceduralMinTileSize.y)
                warnings.AppendLine("proceduralMaxTileSize is smaller than min size and will be clamped at runtime.");
        }

        if (!useProceduralRoom && (largePieceGridSize.x < 1 || largePieceGridSize.y < 1))
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

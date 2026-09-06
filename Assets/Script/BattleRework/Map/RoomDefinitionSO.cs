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
/// Gameplay Room data.
///
/// Current spatial rules:
/// - 32px = exactly 1 tile = exactly 1 world unit.
/// - Player art baseline is 48px = 1.5 world units.
/// - Start Area is an independent 3x3+ tile platform and is not a Combat Room.
/// - Combat Rooms are 6x6+.
/// - Rectangle is the default room shape. L/T/Cross/Irregular/Custom are optional variants.
/// - Any generated positive arm / bay / neck is at least 4 tiles thick.
/// - Procedural Room cells use integer tile coordinates only.
/// - A Room Piece may contain many tiles, but docks as one root object.
/// - World bridges/corridors are not part of RoomDefinitionSO. Stage progression is NodeGraph selection UI.
///
/// recommendedGridSize / blocks remain only for legacy 2x2-world MapBlock compatibility.
/// </summary>
[CreateAssetMenu(fileName = "RoomDefinition", menuName = "MahoBattle/Room Definition")]
public class RoomDefinitionSO : ScriptableObject
{
    public const float ProceduralTileWorldSize = 1f;
    public const int MinimumStartBaseTiles = 3;
    public const int MinimumRoomChunkTiles = 4;
    public const int MinimumCombatRoomTiles = 6;

    [Header("Legacy Persistent Base Compatibility")]
    [Tooltip("Legacy Room compatibility only. The real Start Area is managed separately by BattleRunManager.")]
    public bool usePersistentStartBase = true;

    [HideInInspector]
    public bool forbidMonsterSpawnInsideStartBase = false;

    [Header("Room Identity / Legacy Template")]
    public string roomId;
    [Tooltip("Legacy 2x2 MapBlock grid size. It is separate from the 32px procedural tile size.")]
    public Vector2Int recommendedGridSize = new(4, 4);

    [Header("32px Tile / Start Area")]
    [Tooltip("Independent non-combat Start Area floor. Runtime minimum is 3x3 tiles.")]
    public Vector2Int startBaseTileSize = new(3, 3);

    [Header("Procedural Gameplay Room")]
    [Tooltip("Build the Room from an integer 32px tile mask.")]
    public bool useProceduralRoom = true;
    [Tooltip("Combat Room minimum tile size. Runtime minimum is 6x6.")]
    public Vector2Int proceduralMinTileSize = new(6, 6);
    [Tooltip("Combat Room maximum tile size.")]
    public Vector2Int proceduralMaxTileSize = new(10, 10);
    [Tooltip("Minimum thickness/size of any generated positive room chunk. Runtime minimum is 4 tiles.")]
    [Min(MinimumRoomChunkTiles)]
    public int proceduralMinChunkTileSize = MinimumRoomChunkTiles;
    [Tooltip("0 uses deterministic node/room IDs. Non-zero is mixed into the seed.")]
    public int proceduralSeed;
    [Range(0f, 1f)]
    [Tooltip("Used only by Irregular rooms. Rectangle is the default and ignores this value.")]
    public float proceduralComplexity = 0.25f;
    [Range(0f, 0.45f)]
    [Tooltip("Used only by Irregular rooms. Cuts are chunk-based and never create sub-4-tile arms.")]
    public float proceduralIndentChance = 0.14f;
    [Range(0f, 0.65f)]
    [Tooltip("Used only by Irregular rooms. Generation never uses single-tile noise.")]
    public float proceduralExtensionChance = 0.22f;

    [Header("Large Room Piece Presentation")]
    [Tooltip("Dock one large Room root instead of individual tiny blocks.")]
    public bool useLargeRoomPiece = true;
    [Tooltip("Rectangle is the default. Auto is also resolved as Rectangle in the current stage-select flow.")]
    public RoomLargePieceShape largePieceShape = RoomLargePieceShape.Rectangle;
    [Tooltip("Legacy/non-procedural large piece size. Runtime minimum is 4x4.")]
    public Vector2Int largePieceGridSize = new(4, 4);
    [Tooltip("Custom 32px integer tile coordinates.")]
    public List<Vector2Int> customLargePieceCells = new();
    [Tooltip("Docking presentation direction. Zero uses a default vertical drop/slide presentation.")]
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
    [Tooltip("Spawn positions inside the completed Gameplay Room.")]
    public List<MonsterSpawnEntry> monsterSpawns = new();

    [Header("Clear Presentation")]
    public MapBlock highlightBlockPrefab;
    public Vector2 highlightBlockOffset = new(4f, 0f);

    public int GetMinimumRoomChunkTiles() => Mathf.Max(MinimumRoomChunkTiles, proceduralMinChunkTileSize);

    public Vector2Int GetSafeGridSize()
    {
        return new Vector2Int(
            Mathf.Max(1, recommendedGridSize.x),
            Mathf.Max(1, recommendedGridSize.y));
    }

    public Vector2Int GetStartBaseTileSize()
    {
        return new Vector2Int(
            Mathf.Max(MinimumStartBaseTiles, startBaseTileSize.x),
            Mathf.Max(MinimumStartBaseTiles, startBaseTileSize.y));
    }

    public Vector2Int GetProceduralMinTileSize()
    {
        return new Vector2Int(
            Mathf.Max(MinimumCombatRoomTiles, proceduralMinTileSize.x),
            Mathf.Max(MinimumCombatRoomTiles, proceduralMinTileSize.y));
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
            Mathf.Max(MinimumRoomChunkTiles, largePieceGridSize.x > 0 ? largePieceGridSize.x : fallback.x),
            Mathf.Max(MinimumRoomChunkTiles, largePieceGridSize.y > 0 ? largePieceGridSize.y : fallback.y));
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
            errors.AppendLine("recommendedGridSize must be at least 1x1 for legacy data.");

        if (startBaseTileSize.x < MinimumStartBaseTiles || startBaseTileSize.y < MinimumStartBaseTiles)
            warnings.AppendLine("startBaseTileSize below 3x3 will be clamped to 3x3 at runtime.");

        if (proceduralMinChunkTileSize < MinimumRoomChunkTiles)
            warnings.AppendLine("proceduralMinChunkTileSize below 4 will be clamped to 4 at runtime.");

        if (useProceduralRoom)
        {
            if (proceduralMinTileSize.x < MinimumCombatRoomTiles || proceduralMinTileSize.y < MinimumCombatRoomTiles)
                warnings.AppendLine("proceduralMinTileSize below 6x6 will be clamped to 6x6 at runtime.");

            if (proceduralMaxTileSize.x < proceduralMinTileSize.x || proceduralMaxTileSize.y < proceduralMinTileSize.y)
                warnings.AppendLine("proceduralMaxTileSize is smaller than min size and will be clamped at runtime.");
        }

        if (!useProceduralRoom &&
            (largePieceGridSize.x < MinimumRoomChunkTiles || largePieceGridSize.y < MinimumRoomChunkTiles))
        {
            warnings.AppendLine("largePieceGridSize below 4x4 will be clamped to 4x4 at runtime.");
        }

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

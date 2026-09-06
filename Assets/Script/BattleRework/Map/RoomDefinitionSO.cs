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
/// Spatial rules:
/// - 32px = 1 tile = 1 world unit; Player baseline = 48px = 1.5 world.
/// - Persistent Base is always exactly 4x4 and is the seed of every generated stage.
/// - Combat / Elite rooms are at least 14x14 by data contract; runtime may choose larger free aspect ratios.
/// - Traversable passages/necks/chunks never become 1 tile wide. Minimum structural width is 2 tiles.
/// - Auto shape may resolve to Rectangle/L/T/Cross/Irregular; Custom remains opt-in.
/// - Incoming presentation pieces are separate from topology and may be 1x2, 2x1, 2x2 or larger connected chunks.
/// </summary>
[CreateAssetMenu(fileName = "RoomDefinition", menuName = "MahoBattle/Room Definition")]
public class RoomDefinitionSO : ScriptableObject
{
    public const float ProceduralTileWorldSize = 1f;
    public const int MinimumStartBaseTiles = 4;
    public const int MinimumPassageTiles = 2;
    public const int MinimumRoomChunkTiles = 2;
    public const int MinimumCombatRoomTiles = 14;

    [Header("Legacy Persistent Base Compatibility")]
    [Tooltip("Compatibility flag only. RoomBaseTemplate owns the real persistent 4x4 Base.")]
    public bool usePersistentStartBase = true;

    [HideInInspector] public bool forbidMonsterSpawnInsideStartBase = false;

    [Header("Room Identity / Legacy Template")]
    public string roomId;
    [Tooltip("Legacy 2x2 MapBlock grid size. Separate from the 32px procedural tile size.")]
    public Vector2Int recommendedGridSize = new(4, 4);

    [Header("32px Tile / Persistent Base")]
    [Tooltip("Compatibility value. Runtime persistent Base is fixed to 4x4 tiles.")]
    public Vector2Int startBaseTileSize = new(4, 4);

    [Header("Procedural Gameplay Room")]
    [Tooltip("Build the Room from an integer 32px tile mask.")]
    public bool useProceduralRoom = true;
    [Tooltip("Combat / Elite minimum. Runtime minimum is 14x14.")]
    public Vector2Int proceduralMinTileSize = new(14, 14);
    [Tooltip("Preferred upper range. Width/height are independent; rooms need not be square or even-sized.")]
    public Vector2Int proceduralMaxTileSize = new(22, 20);
    [Tooltip("Minimum structural thickness. Values below 2 are clamped to 2.")]
    [Min(MinimumRoomChunkTiles)] public int proceduralMinChunkTileSize = MinimumRoomChunkTiles;
    [Tooltip("0 uses deterministic node/room IDs. Non-zero is mixed into the seed.")]
    public int proceduralSeed;
    [Range(0f, 1f)] public float proceduralComplexity = 0.35f;
    [Range(0f, 0.45f)] public float proceduralIndentChance = 0.20f;
    [Range(0f, 0.65f)] public float proceduralExtensionChance = 0.28f;

    [Header("Large Room Piece Presentation")]
    public bool useLargeRoomPiece = true;
    [Tooltip("Auto intentionally varies the final stage silhouette. Explicit shapes remain deterministic choices.")]
    public RoomLargePieceShape largePieceShape = RoomLargePieceShape.Auto;
    [Tooltip("Legacy/non-procedural large-piece size.")]
    public Vector2Int largePieceGridSize = new(4, 4);
    [Tooltip("Custom 32px integer cells. Invalid one-tile-thin or disconnected layouts fall back safely.")]
    public List<Vector2Int> customLargePieceCells = new();
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
    public List<MonsterSpawnEntry> monsterSpawns = new();

    [Header("Clear Presentation")]
    public MapBlock highlightBlockPrefab;
    public Vector2 highlightBlockOffset = new(4f, 0f);

    public int GetMinimumPassageTiles() => MinimumPassageTiles;
    public int GetMinimumRoomChunkTiles() => Mathf.Max(MinimumRoomChunkTiles, proceduralMinChunkTileSize);

    public Vector2Int GetSafeGridSize()
    {
        return new Vector2Int(Mathf.Max(1, recommendedGridSize.x), Mathf.Max(1, recommendedGridSize.y));
    }

    public Vector2Int GetStartBaseTileSize() => new(MinimumStartBaseTiles, MinimumStartBaseTiles);

    public Vector2Int GetProceduralMinTileSize()
    {
        return new Vector2Int(
            Mathf.Max(MinimumCombatRoomTiles, proceduralMinTileSize.x),
            Mathf.Max(MinimumCombatRoomTiles, proceduralMinTileSize.y));
    }

    public Vector2Int GetProceduralMaxTileSize()
    {
        Vector2Int min = GetProceduralMinTileSize();
        return new Vector2Int(Mathf.Max(min.x, proceduralMaxTileSize.x), Mathf.Max(min.y, proceduralMaxTileSize.y));
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
        return new Vector2(grid.x * MapBlock.BlockWorldSize.x, grid.y * MapBlock.BlockWorldSize.y);
    }

    public Vector2 GetStartBaseWorldSize() => Vector2.one * MinimumStartBaseTiles * ProceduralTileWorldSize;

    public Vector2 GetLargePieceWorldSize()
    {
        Vector2Int grid = GetLargePieceGridSize();
        float tile = useProceduralRoom ? ProceduralTileWorldSize : MapBlock.BlockWorldSize.x;
        return new Vector2(grid.x * tile, grid.y * tile);
    }

    public Vector2 GetRuntimeBaseWorldSize()
    {
        Vector2 size = useProceduralRoom ? GetStartBaseWorldSize() : GetTemplateWorldSize();
        Vector2 padding = new(Mathf.Max(0f, basePaddingWorld.x), Mathf.Max(0f, basePaddingWorld.y));
        return size + padding * 2f;
    }

    public Vector2 GetTemplateCenterOffset()
    {
        Vector2Int grid = GetSafeGridSize();
        return new Vector2(
            (grid.x - 1) * MapBlock.BlockWorldSize.x * 0.5f,
            (grid.y - 1) * MapBlock.BlockWorldSize.y * 0.5f);
    }

    public Vector2 GetStartBaseCenterOffset() =>
        Vector2.one * ((MinimumStartBaseTiles - 1) * ProceduralTileWorldSize * 0.5f);

    public Vector2 GetLargePieceCenterOffset()
    {
        Vector2Int grid = GetLargePieceGridSize();
        float tile = useProceduralRoom ? ProceduralTileWorldSize : MapBlock.BlockWorldSize.x;
        return new Vector2((grid.x - 1) * tile * 0.5f, (grid.y - 1) * tile * 0.5f);
    }

    public Vector2 GetRuntimeBaseCenterOffset() =>
        (useProceduralRoom ? GetStartBaseCenterOffset() : GetTemplateCenterOffset()) + baseOffset;

    public Vector2 GetBlockLocalPosition(Vector2Int gridPosition) =>
        new(gridPosition.x * MapBlock.BlockWorldSize.x, gridPosition.y * MapBlock.BlockWorldSize.y);

    public Vector2 GetProceduralTileLocalPosition(Vector2Int tilePosition) =>
        new Vector2(tilePosition.x, tilePosition.y) * ProceduralTileWorldSize;

    public bool IsInsideTemplateGrid(Vector2Int gridPosition)
    {
        Vector2Int grid = GetSafeGridSize();
        return gridPosition.x >= 0 && gridPosition.y >= 0 && gridPosition.x < grid.x && gridPosition.y < grid.y;
    }

    public bool IsStartBaseGridPosition(Vector2Int gridPosition) => usePersistentStartBase && IsInsideTemplateGrid(gridPosition);

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
        if (startBaseTileSize.x != 4 || startBaseTileSize.y != 4)
            warnings.AppendLine("startBaseTileSize is compatibility data only; runtime Base is exactly 4x4.");
        if (proceduralMinChunkTileSize < 2)
            warnings.AppendLine("proceduralMinChunkTileSize below 2 will be clamped to 2.");

        if (useProceduralRoom)
        {
            if (proceduralMinTileSize.x < 14 || proceduralMinTileSize.y < 14)
                warnings.AppendLine("proceduralMinTileSize below 14x14 will be clamped at runtime.");
            if (proceduralMaxTileSize.x < proceduralMinTileSize.x || proceduralMaxTileSize.y < proceduralMinTileSize.y)
                warnings.AppendLine("proceduralMaxTileSize is smaller than min and will be clamped.");
        }

        if (largePieceShape == RoomLargePieceShape.Custom &&
            (customLargePieceCells == null || customLargePieceCells.Count == 0))
            errors.AppendLine("Custom shape requires customLargePieceCells.");

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
            for (int i = 0; i < obstacles.Count; i++)
            {
                if (obstacles[i] == null) errors.AppendLine($"obstacles[{i}] is null.");
                else if (obstacles[i].prefab == null) errors.AppendLine($"obstacles[{i}] has no prefab.");
            }

        if (monsterSpawns == null || monsterSpawns.Count == 0)
            warnings.AppendLine("No monster spawns. Combat node may clear immediately.");
        else
            for (int i = 0; i < monsterSpawns.Count; i++)
            {
                MonsterSpawnEntry spawn = monsterSpawns[i];
                if (spawn == null) errors.AppendLine($"monsterSpawns[{i}] is null.");
                else
                {
                    if (spawn.monster == null) errors.AppendLine($"monsterSpawns[{i}] has no MonsterDefinitionSO.");
                    if (spawn.count < 1) errors.AppendLine($"monsterSpawns[{i}] count must be at least 1.");
                }
            }

        StringBuilder combined = new();
        if (errors.Length > 0) { combined.AppendLine("[Errors]"); combined.Append(errors); }
        if (warnings.Length > 0) { combined.AppendLine("[Warnings]"); combined.Append(warnings); }
        report = combined.ToString().TrimEnd();
        return errors.Length == 0;
    }
}

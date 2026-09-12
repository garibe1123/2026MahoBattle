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
    [Tooltip("Legacy/Custom Room에서 배치할 MapBlock Prefab입니다.")]
    public MapBlock prefab;
    [Tooltip("Legacy 2x2 MapBlock Grid 기준 배치 좌표입니다.")]
    public Vector2Int gridPosition;
    [Tooltip("해당 Block이 화면 밖에서 진입할 기본 방향입니다.")]
    public Vector2 entryDirection = Vector2.right;
}

[Serializable]
public class ObstaclePlacement
{
    [Tooltip("Room에 생성할 장애물 Prefab입니다.")]
    public BattleObstacle prefab;
    [Tooltip("RoomOrigin 기준 로컬 위치입니다.")]
    public Vector2 localPosition;
    [Tooltip("장애물의 Z축 회전 각도입니다. 단위는 도(°)입니다.")]
    public float rotationZ;
}

[Serializable]
public class MonsterSpawnEntry
{
    [Tooltip("이 Spawn Entry에서 생성할 MonsterDefinitionSO입니다.")]
    public MonsterDefinitionSO monster;
    [Tooltip("RoomOrigin 기준 몬스터 생성 중심 위치입니다.")]
    public Vector2 localPosition;
    [Tooltip("이 Entry에서 생성할 몬스터 수입니다.")]
    [Min(1)] public int count = 1;
    [Tooltip("여러 마리를 생성할 때 중심 위치 주변으로 흩어질 최대 반경입니다. 0이면 같은 위치를 기준으로 생성합니다.")]
    [Min(0f)] public float scatterRadius;
}

/// <summary>
/// Gameplay Room data.
/// Spatial rules:
/// - 32px = 1 tile = 1 world unit; Player baseline = 48px = 1.5 world.
/// - Persistent Base is always exactly 4x4 and is the seed of every generated stage.
/// - Combat / Elite rooms are at least 20x20 by runtime contract and normally vary up toward 30+ tiles per axis.
/// - Traversable passages never become 1 tile wide. Absolute passage minimum remains 2 tiles.
/// - Automatic L/T/Cross silhouettes deliberately use a much thicker body (7+ tiles) so they read as battle spaces, not corridors.
/// - Auto shape may resolve to Rectangle/L/T/Cross/Irregular; Custom remains opt-in.
/// - Incoming presentation pieces are separate from topology and are assembled around the existing 4x4 Base.
/// </summary>
[CreateAssetMenu(fileName = "RoomDefinition", menuName = "MahoBattle/Room Definition")]
public class RoomDefinitionSO : ScriptableObject
{
    public const float ProceduralTileWorldSize = 1f;
    public const int MinimumStartBaseTiles = 4;
    public const int MinimumPassageTiles = 2;
    public const int MinimumRoomChunkTiles = 7;
    public const int MinimumCombatRoomTiles = 20;
    public const int MinimumPreferredMaxRoomTiles = 30;
    public const int MinimumRoomSizeVariation = 8;

    [Header("COMPATIBILITY — 기존 데이터 호환")]
    [Tooltip("호환용 플래그입니다. 실제 Persistent 4x4 Base의 생성과 유지 규칙은 BattleTemplate의 RoomBaseTemplate이 소유합니다.")]
    public bool usePersistentStartBase = true;

    [HideInInspector] public bool forbidMonsterSpawnInsideStartBase = false;

    [Header("ROOM IDENTITY — 전투 템플릿 식별")]
    [Tooltip("Room을 로그/검증/Seed 계산에서 구분하기 위한 ID입니다. 같은 역할의 Room끼리도 가능하면 고유하게 지정하세요.")]
    public string roomId;
    [Tooltip("Legacy 2x2 MapBlock 방식에서 사용하는 권장 Grid 크기입니다. Procedural 32px Tile 크기와는 별개입니다.")]
    public Vector2Int recommendedGridSize = new(4, 4);

    [Header("PERSISTENT BASE — 32px / 4x4 호환값")]
    [Tooltip("호환용 값입니다. Runtime Persistent Base는 항상 4x4로 강제됩니다.")]
    public Vector2Int startBaseTileSize = new(4, 4);

    [Header("PROCEDURAL ROOM — 전투 필드 크기 / 복잡도")]
    [Tooltip("켜면 32px 정수 Tile Mask를 기반으로 Procedural 전투 Room을 생성합니다. 현재 일반 Combat/Elite의 기본 방식입니다.")]
    public bool useProceduralRoom = true;
    [Tooltip("Procedural Room의 최소 가로/세로 Tile 수입니다. Runtime은 Combat/Elite에서 최소 20x20을 보장합니다.")]
    public Vector2Int proceduralMinTileSize = new(20, 20);
    [Tooltip("Procedural Room의 선호 최대 가로/세로 Tile 수입니다. 실제 생성 시 최소값과 함께 안전 범위로 보정됩니다.")]
    public Vector2Int proceduralMaxTileSize = new(32, 30);
    [Tooltip("L/T/Cross/Irregular 같은 자동 실루엣의 몸통 최소 두께입니다. 너무 낮으면 복도처럼 보이므로 Runtime에서 7 이상을 보장합니다.")]
    [Min(MinimumRoomChunkTiles)] public int proceduralMinChunkTileSize = MinimumRoomChunkTiles;
    [Tooltip("Procedural Seed 보정값입니다. 0이면 Node/Room ID 기반 결정적 Seed를 사용하고, 0이 아니면 이 값을 추가로 섞습니다.")]
    public int proceduralSeed;
    [Tooltip("Room 실루엣이 얼마나 복합적으로 분기/굴곡될지 결정하는 전체 복잡도입니다. 0은 단순, 1은 복잡한 형태에 가깝습니다.")]
    [Range(0f, 1f)] public float proceduralComplexity = 0.35f;
    [Tooltip("기본 실루엣에서 안쪽으로 파인 영역이 생길 확률입니다. 값이 높을수록 오목한 모양이 늘어납니다.")]
    [Range(0f, 0.45f)] public float proceduralIndentChance = 0.20f;
    [Tooltip("기본 실루엣 바깥으로 추가 덩어리가 확장될 확률입니다. 값이 높을수록 외곽 돌출이 늘어납니다.")]
    [Range(0f, 0.65f)] public float proceduralExtensionChance = 0.28f;

    [Header("ENTRY PRESENTATION — 큰 타일 조립 / 진입")]
    [Tooltip("Room을 화면 밖에서 큰 Assembly Piece 단위로 조립하는 진입 연출을 사용할지 결정합니다.")]
    public bool useLargeRoomPiece = true;
    [Tooltip("전투 필드의 대표 실루엣 형태입니다. Auto는 Rectangle/L/T/Cross/Irregular 중 안전한 형태를 자동 선택합니다.")]
    public RoomLargePieceShape largePieceShape = RoomLargePieceShape.Auto;
    [Tooltip("Legacy/non-procedural 방식에서 사용할 큰 Piece Grid 크기입니다. Procedural Room에서는 Procedural 크기 규칙이 우선합니다.")]
    public Vector2Int largePieceGridSize = new(4, 4);
    [Tooltip("Large Piece Shape이 Custom일 때 사용할 32px 정수 Tile Cell 목록입니다. 끊기거나 1칸 두께로만 구성된 잘못된 형태는 Runtime에서 안전하게 보정됩니다.")]
    public List<Vector2Int> customLargePieceCells = new();
    [Tooltip("큰 Piece의 기본 진입 방향입니다. (1,0)=오른쪽 방향 기준, (-1,0)=왼쪽, (0,1)=위쪽, (0,-1)=아래쪽입니다. Vector2.zero면 자동 결정할 수 있습니다.")]
    public Vector2 largePieceEntryDirection = Vector2.zero;
    [Tooltip("화면 밖 Rail에서 도킹 지점까지 이동하는 접근 시간입니다. Dock 반동/정착 시간은 별도입니다.")]
    [Min(0.05f)] public float largePieceEntryDuration = 0.72f;
    [Tooltip("큰 Piece가 도킹 지점에서 얼마나 멀리 떨어진 화면 밖 Rail에서 시작할지 결정하는 월드 거리입니다.")]
    [Min(0.5f)] public float largePieceEntryOffset = 8f;

    [Header("RUNTIME BASE COMPATIBILITY — Legacy Base 보정")]
    [Tooltip("Legacy Room에서 Runtime Base 크기 보정 경로를 사용할지 결정합니다. Persistent 4x4 자체의 존재 여부와는 별개입니다.")]
    public bool useRuntimeBase = true;
    [Tooltip("Legacy Runtime Base 바깥에 추가할 월드 단위 여백입니다. 음수는 허용하지 않습니다.")]
    public Vector2 basePaddingWorld = Vector2.zero;
    [Tooltip("Legacy Runtime Base 중심에 추가할 월드 단위 위치 오프셋입니다.")]
    public Vector2 baseOffset = Vector2.zero;

    [Header("PLAYER ENTRY — 전투 시작 위치")]
    [Tooltip("RoomOrigin을 기준으로 Player를 배치할 로컬 위치 오프셋입니다.")]
    public Vector2 playerEntryOffset = Vector2.zero;
    [Tooltip("Room 진입 때 Player를 Player Entry Offset 위치로 재배치할지 결정합니다.")]
    public bool repositionPlayerOnEnter = true;

    [Header("CUSTOM ROOM PIECES — Legacy / 직접 배치")]
    [Tooltip("Procedural Room을 사용하지 않을 때 직접 배치할 MapBlock 목록입니다.")]
    public List<MapBlockPlacement> blocks = new();

    [Header("OBSTACLES — 장애물 배치")]
    [Tooltip("RoomOrigin 기준으로 생성할 장애물 목록입니다.")]
    public List<ObstaclePlacement> obstacles = new();

    [Header("MONSTER SPAWNS — 적 생성")]
    [Tooltip("이 Room에서 생성할 몬스터 종류/위치/수량 목록입니다. 비어 있으면 전투가 즉시 Clear될 수 있습니다.")]
    public List<MonsterSpawnEntry> monsterSpawns = new();

    [Header("CLEAR PRESENTATION — 클리어 표시")]
    [Tooltip("Legacy Clear Highlight에 사용할 MapBlock Prefab입니다. 비어 있으면 해당 Highlight 연출을 생략할 수 있습니다.")]
    public MapBlock highlightBlockPrefab;
    [Tooltip("Clear Highlight Block의 RoomOrigin 기준 위치 오프셋입니다.")]
    public Vector2 highlightBlockOffset = new(4f, 0f);

    public int GetMinimumPassageTiles() => MinimumPassageTiles;

    /// <summary>
    /// Thickness used by generated L/T/Cross/Irregular room bodies.
    /// This is deliberately NOT the same thing as the hard two-tile passage minimum.
    /// </summary>
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

        int requiredX = Mathf.Max(MinimumPreferredMaxRoomTiles, min.x + MinimumRoomSizeVariation);
        int requiredY = Mathf.Max(MinimumPreferredMaxRoomTiles, min.y + MinimumRoomSizeVariation);
        return new Vector2Int(
            Mathf.Max(requiredX, proceduralMaxTileSize.x),
            Mathf.Max(requiredY, proceduralMaxTileSize.y));
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
        if (proceduralMinChunkTileSize < MinimumRoomChunkTiles)
            warnings.AppendLine($"proceduralMinChunkTileSize below {MinimumRoomChunkTiles} will be widened at runtime for generated Room silhouettes.");

        if (useProceduralRoom)
        {
            if (proceduralMinTileSize.x < MinimumCombatRoomTiles || proceduralMinTileSize.y < MinimumCombatRoomTiles)
                warnings.AppendLine($"proceduralMinTileSize below {MinimumCombatRoomTiles}x{MinimumCombatRoomTiles} will be clamped at runtime.");

            Vector2Int safeMin = GetProceduralMinTileSize();
            int requiredX = Mathf.Max(MinimumPreferredMaxRoomTiles, safeMin.x + MinimumRoomSizeVariation);
            int requiredY = Mathf.Max(MinimumPreferredMaxRoomTiles, safeMin.y + MinimumRoomSizeVariation);
            if (proceduralMaxTileSize.x < requiredX || proceduralMaxTileSize.y < requiredY)
                warnings.AppendLine($"proceduralMaxTileSize is below the expanded battle-room range and will be raised to at least {requiredX}x{requiredY} at runtime.");
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
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                if (obstacles[i] == null) errors.AppendLine($"obstacles[{i}] is null.");
                else if (obstacles[i].prefab == null) errors.AppendLine($"obstacles[{i}] has no prefab.");
            }
        }

        if (monsterSpawns == null || monsterSpawns.Count == 0)
        {
            warnings.AppendLine("No monster spawns. Combat node may clear immediately.");
        }
        else
        {
            for (int i = 0; i < monsterSpawns.Count; i++)
            {
                MonsterSpawnEntry spawn = monsterSpawns[i];
                if (spawn == null)
                {
                    errors.AppendLine($"monsterSpawns[{i}] is null.");
                }
                else
                {
                    if (spawn.monster == null) errors.AppendLine($"monsterSpawns[{i}] has no MonsterDefinitionSO.");
                    if (spawn.count < 1) errors.AppendLine($"monsterSpawns[{i}] count must be at least 1.");
                }
            }
        }

        StringBuilder combined = new();
        if (errors.Length > 0) { combined.AppendLine("[Errors]"); combined.Append(errors); }
        if (warnings.Length > 0) { combined.AppendLine("[Warnings]"); combined.Append(warnings); }
        report = combined.ToString().TrimEnd();
        return errors.Length == 0;
    }
}

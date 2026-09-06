using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NavMeshPlus.Components;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// Sephiria-like spatial battle map presentation layer.
///
/// - Start Area를 (0,0)으로 두고 NodeGraph Room을 상/하/좌/우 떨어진 위치에 배치합니다.
/// - Room이 바로 옆에 붙지 않고 사이에 3개 기본 Corridor Piece가 순차 도킹합니다.
/// - Gameplay Room은 2x2 Block 16개가 아니라 하나의 큰 Root Piece로 도킹합니다.
/// - Rectangle / L / T / Cross / Custom cell mask가 모두 하나의 Transform으로 움직입니다.
/// - NodeGraph의 동일 좌표를 우측 상단 Mini Map에 표시합니다.
///
/// 기존 BattleRunManager/BattleRoomManager API를 바꾸지 않고 NodeEntered 직전에 Room 데이터를
/// 한 프레임 동안 presentation용 synthetic piece로 치환합니다. 원본 SO 데이터는 즉시 복구됩니다.
/// </summary>
[DefaultExecutionOrder(-20000)]
public sealed class BattleSpatialMapController : MonoBehaviour
{
    [Header("World Map")]
    [SerializeField, Min(10f)] private float roomWorldSpacing = 14f;
    [SerializeField, Min(1f)] private float corridorWidth = 2.4f;
    [SerializeField, Range(1, 6)] private int corridorPieceCount = 3;
    [SerializeField, Min(0.05f)] private float corridorEntryDuration = 0.34f;
    [SerializeField, Min(0.5f)] private float corridorEntryOffset = 5f;
    [SerializeField, Min(0f)] private float corridorPieceStagger = 0.09f;

    [Header("Large Room Piece")]
    [SerializeField] private Color roomFloorColor = new(0.18f, 0.21f, 0.25f, 1f);
    [SerializeField] private Color roomEdgeColor = new(0.31f, 0.35f, 0.41f, 1f);
    [SerializeField, Min(0.05f)] private float roomEdgeThickness = 0.16f;
    [SerializeField, Min(0.6f)] private float roomDoorWidth = 2.1f;
    [SerializeField, Range(0.1f, 1.5f)] private float roomImpactStrength = 0.95f;

    [Header("Mini Map")]
    [SerializeField] private Vector2 miniMapSize = new(220f, 180f);
    [SerializeField, Min(16f)] private float miniMapCellSpacing = 34f;
    [SerializeField, Min(8f)] private float miniMapNodeSize = 18f;
    [SerializeField] private Color miniMapBackground = new(0.04f, 0.055f, 0.075f, 0.82f);
    [SerializeField] private Color miniMapUnknown = new(0.28f, 0.31f, 0.36f, 0.92f);
    [SerializeField] private Color miniMapVisited = new(0.62f, 0.68f, 0.75f, 1f);
    [SerializeField] private Color miniMapCurrent = new(0.35f, 0.92f, 1f, 1f);
    [SerializeField] private Color miniMapAvailable = new(1f, 0.80f, 0.26f, 1f);
    [SerializeField] private Color miniMapLink = new(0.36f, 0.40f, 0.46f, 0.95f);

    [Header("Default Test Character Readability")]
    [SerializeField, Min(1f)] private float testPlayerScale = 2.8f;
    [SerializeField, Min(0.1f)] private float testPlayerColliderRadius = 0.28f;
    [SerializeField, Min(1f)] private float testMonsterScale = 1.8f;
    [SerializeField, Min(0.1f)] private float testMonsterColliderRadius = 0.38f;

    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;
    private NodeGraphSO graph;

    private readonly Dictionary<string, Vector2Int> resolvedMapPositions = new();
    private readonly HashSet<string> visitedNodeIds = new();
    private readonly HashSet<string> builtRouteKeys = new();
    private readonly List<GameObject> persistentRouteObjects = new();

    private BattleNodeData lastEnteredNode;
    private Vector2Int lastMapPosition;
    private RoomDefinitionSO lastRoom;
    private Vector3 startOrigin;
    private bool startOriginResolved;
    private float nextCharacterSizingCheck;

    private Canvas miniMapCanvas;
    private RectTransform miniMapPanel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleSpatialMapController>() != null)
            return;

        GameObject host = new("BattleSpatialMapRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleSpatialMapController>();
    }

    private void OnEnable()
    {
        StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private IEnumerator BindWhenReady()
    {
        while (runManager == null)
        {
            TryResolveSystems();
            if (runManager == null)
                yield return null;
        }

        Subscribe();
        DisableLegacyAutoShell();
        EnsureMiniMapUI();
        BuildResolvedLayout();
        RefreshMiniMap();
    }

    private void TryResolveSystems()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();

        if (runManager != null && graph == null)
        {
            FieldInfo graphField = typeof(BattleRunManager).GetField("nodeGraph", InstanceFields);
            graph = graphField != null ? graphField.GetValue(runManager) as NodeGraphSO : null;
        }
    }

    private void Subscribe()
    {
        if (runManager == null)
            return;

        runManager.NodeEntered -= HandleNodeEntered;
        runManager.NodeEntered += HandleNodeEntered;
        runManager.StateChanged -= HandleStateChanged;
        runManager.StateChanged += HandleStateChanged;
        runManager.NextNodeSelectionRequested -= HandleNextNodeSelectionRequested;
        runManager.NextNodeSelectionRequested += HandleNextNodeSelectionRequested;
        runManager.RunEnded -= HandleRunEnded;
        runManager.RunEnded += HandleRunEnded;
    }

    private void Unsubscribe()
    {
        if (runManager == null)
            return;

        runManager.NodeEntered -= HandleNodeEntered;
        runManager.StateChanged -= HandleStateChanged;
        runManager.NextNodeSelectionRequested -= HandleNextNodeSelectionRequested;
        runManager.RunEnded -= HandleRunEnded;
    }

    private void DisableLegacyAutoShell()
    {
        if (baseTemplate == null)
            return;

        FieldInfo field = typeof(RoomBaseTemplate).GetField("autoCreateShellWhenNoExtensionBlocks", InstanceFields);
        if (field != null)
            field.SetValue(baseTemplate, false);
    }

    private void Update()
    {
        if (runManager == null || roomManager == null)
        {
            TryResolveSystems();
            if (runManager != null)
            {
                Subscribe();
                DisableLegacyAutoShell();
                EnsureMiniMapUI();
                BuildResolvedLayout();
            }
        }

        if (Time.unscaledTime >= nextCharacterSizingCheck)
        {
            nextCharacterSizingCheck = Time.unscaledTime + 0.35f;
            ApplyReadableDefaultCharacterSizes();
        }
    }

    private void HandleStateChanged(BattleRunState state)
    {
        if (!startOriginResolved && runManager != null && runManager.IsInStartArea)
            ResolveStartOriginFromBase();

        RefreshMiniMap();
    }

    private void HandleNextNodeSelectionRequested(IReadOnlyList<BattleNodeData> _)
    {
        RefreshMiniMap();
    }

    private void HandleRunEnded(RunEndReason _)
    {
        visitedNodeIds.Clear();
        lastEnteredNode = null;
        lastMapPosition = Vector2Int.zero;
        lastRoom = null;
        ClearPersistentRoutes();
        RefreshMiniMap();
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node == null || roomManager == null)
            return;

        ResolveStartOriginFromBase(node.room);
        BuildResolvedLayout();

        Vector2Int targetMapPosition = ResolveNodeMapPosition(node);
        Vector3 targetRoomOrigin = startOrigin + new Vector3(
            targetMapPosition.x * roomWorldSpacing,
            targetMapPosition.y * roomWorldSpacing,
            0f);

        RoomDefinitionSO previousRoom = lastRoom != null ? lastRoom : node.room;
        Vector3 previousOrigin = startOrigin + new Vector3(
            lastMapPosition.x * roomWorldSpacing,
            lastMapPosition.y * roomWorldSpacing,
            0f);

        Vector2 travelDirection = CardinalDirection(targetMapPosition - lastMapPosition);
        if (travelDirection == Vector2.zero)
            travelDirection = Vector2.right;

        roomManager.RoomOrigin.position = targetRoomOrigin;

        BuildConnectionCorridor(
            previousOrigin,
            previousRoom,
            targetRoomOrigin,
            node.room,
            travelDirection,
            lastMapPosition,
            targetMapPosition);

        // Start Area Exit 시 기존 BattleRunManager가 이미 Player를 첫 Room 쪽으로 옮겼더라도
        // NodeEntered 시점에 다시 통로 시작점으로 복귀시켜 직접 걸어가게 합니다.
        if (lastEnteredNode == null)
            PlacePlayerAtRouteStart(previousOrigin, previousRoom, travelDirection);

        PrepareLargeRoomPiece(node, travelDirection);

        visitedNodeIds.Add(node.id);
        lastEnteredNode = node;
        lastMapPosition = targetMapPosition;
        lastRoom = node.room;
        RefreshMiniMap();
    }

    private void ResolveStartOriginFromBase(RoomDefinitionSO fallbackRoom = null)
    {
        if (startOriginResolved)
            return;

        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();

        if (baseTemplate != null && baseTemplate.ActiveBase != null)
        {
            RoomDefinitionSO basis = baseTemplate.ActiveRoom != null ? baseTemplate.ActiveRoom : fallbackRoom;
            if (basis != null)
            {
                startOrigin = baseTemplate.ActiveBase.transform.position - (Vector3)basis.GetRuntimeBaseCenterOffset();
                startOrigin.z = roomManager != null && roomManager.RoomOrigin != null
                    ? roomManager.RoomOrigin.position.z
                    : 0f;
                startOriginResolved = true;
                return;
            }
        }

        if (roomManager != null && roomManager.RoomOrigin != null)
        {
            startOrigin = roomManager.RoomOrigin.position;
            startOriginResolved = true;
        }
    }

    private void BuildResolvedLayout()
    {
        if (graph == null)
        {
            TryResolveSystems();
            if (graph == null)
                return;
        }

        resolvedMapPositions.Clear();
        HashSet<Vector2Int> occupied = new() { Vector2Int.zero };

        if (graph.nodes != null)
        {
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                BattleNodeData node = graph.nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.id) || !node.useExplicitMapPosition)
                    continue;

                Vector2Int position = node.mapPosition;
                if (position == Vector2Int.zero || occupied.Contains(position))
                    continue;

                resolvedMapPositions[node.id] = position;
                occupied.Add(position);
            }
        }

        BattleNodeData startNode = graph.GetStartNode();
        if (startNode == null)
            return;

        Queue<BattleNodeData> queue = new();
        if (!resolvedMapPositions.ContainsKey(startNode.id))
        {
            Vector2Int first = FindFreePosition(Vector2Int.zero, occupied, 0);
            resolvedMapPositions[startNode.id] = first;
            occupied.Add(first);
        }
        queue.Enqueue(startNode);

        HashSet<string> visited = new();
        while (queue.Count > 0)
        {
            BattleNodeData parent = queue.Dequeue();
            if (parent == null || !visited.Add(parent.id))
                continue;

            Vector2Int parentPosition = resolvedMapPositions.TryGetValue(parent.id, out Vector2Int p)
                ? p
                : Vector2Int.zero;

            List<BattleNodeData> next = graph.GetNextNodes(parent);
            for (int i = 0; i < next.Count; i++)
            {
                BattleNodeData child = next[i];
                if (child == null)
                    continue;

                if (!resolvedMapPositions.ContainsKey(child.id))
                {
                    Vector2Int candidate = FindFreePosition(parentPosition, occupied, i);
                    resolvedMapPositions[child.id] = candidate;
                    occupied.Add(candidate);
                }

                queue.Enqueue(child);
            }
        }
    }

    private static Vector2Int FindFreePosition(Vector2Int origin, HashSet<Vector2Int> occupied, int preference)
    {
        Vector2Int[] directions =
        {
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left
        };

        for (int offset = 0; offset < directions.Length; offset++)
        {
            Vector2Int candidate = origin + directions[(preference + offset) % directions.Length];
            if (!occupied.Contains(candidate))
                return candidate;
        }

        for (int distance = 2; distance < 16; distance++)
        {
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2Int candidate = origin + directions[i] * distance;
                if (!occupied.Contains(candidate))
                    return candidate;
            }
        }

        return origin + Vector2Int.right * 16;
    }

    private Vector2Int ResolveNodeMapPosition(BattleNodeData node)
    {
        if (node == null)
            return Vector2Int.zero;

        if (resolvedMapPositions.TryGetValue(node.id, out Vector2Int position))
            return position;

        return node.useExplicitMapPosition ? node.mapPosition : Vector2Int.right;
    }

    private static Vector2 CardinalDirection(Vector2Int delta)
    {
        if (delta == Vector2Int.zero)
            return Vector2.zero;

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            return new Vector2(Mathf.Sign(delta.x), 0f);

        return new Vector2(0f, Mathf.Sign(delta.y));
    }

    private void BuildConnectionCorridor(
        Vector3 fromOrigin,
        RoomDefinitionSO fromRoom,
        Vector3 toOrigin,
        RoomDefinitionSO toRoom,
        Vector2 direction,
        Vector2Int fromMap,
        Vector2Int toMap)
    {
        string key = RouteKey(fromMap, toMap);
        if (!builtRouteKeys.Add(key))
            return;

        Vector2 fromSize = fromRoom != null ? fromRoom.GetLargePieceWorldSize() : new Vector2(8f, 8f);
        Vector2 toSize = toRoom != null ? toRoom.GetLargePieceWorldSize() : new Vector2(8f, 8f);
        Vector2 fromCenterOffset = fromRoom != null ? fromRoom.GetLargePieceCenterOffset() : new Vector2(3f, 3f);
        Vector2 toCenterOffset = toRoom != null ? toRoom.GetLargePieceCenterOffset() : new Vector2(3f, 3f);

        Vector2 fromCenter = (Vector2)fromOrigin + fromCenterOffset;
        Vector2 toCenter = (Vector2)toOrigin + toCenterOffset;

        if (Mathf.Abs(direction.x) > 0.5f)
        {
            Vector2 start = fromCenter + new Vector2(direction.x * fromSize.x * 0.5f, 0f);
            Vector2 end = new Vector2(toCenter.x - direction.x * toSize.x * 0.5f, start.y);
            BuildStraightCorridor(start, end, true);
        }
        else
        {
            Vector2 start = fromCenter + new Vector2(0f, direction.y * fromSize.y * 0.5f);
            Vector2 end = new Vector2(start.x, toCenter.y - direction.y * toSize.y * 0.5f);
            BuildStraightCorridor(start, end, false);
        }
    }

    private void BuildStraightCorridor(Vector2 start, Vector2 end, bool horizontal)
    {
        float totalLength = horizontal ? Mathf.Abs(end.x - start.x) : Mathf.Abs(end.y - start.y);
        if (totalLength <= 0.2f)
            return;

        int count = Mathf.Max(1, corridorPieceCount);
        float pieceLength = totalLength / count;
        float sign = horizontal ? Mathf.Sign(end.x - start.x) : Mathf.Sign(end.y - start.y);

        for (int i = 0; i < count; i++)
        {
            float distance = pieceLength * (i + 0.5f);
            Vector2 center = horizontal
                ? start + Vector2.right * sign * distance
                : start + Vector2.up * sign * distance;

            Vector2 size = horizontal
                ? new Vector2(pieceLength + 0.10f, corridorWidth)
                : new Vector2(corridorWidth, pieceLength + 0.10f);

            Vector2 incoming = horizontal
                ? (i % 2 == 0 ? Vector2.down : Vector2.up)
                : (i % 2 == 0 ? Vector2.left : Vector2.right);

            GameObject piece = CreateCorridorPiece($"RoutePiece_{persistentRouteObjects.Count}", center, size, horizontal);
            MapBlock block = piece.GetComponent<MapBlock>();
            block.PlayEnter(piece.transform.position, incoming, i * corridorPieceStagger);
            persistentRouteObjects.Add(piece);
        }
    }

    private GameObject CreateCorridorPiece(string objectName, Vector2 destination, Vector2 size, bool horizontal)
    {
        GameObject root = new(objectName);
        root.transform.position = destination;

        GameObject floor = new("Floor");
        floor.transform.SetParent(root.transform, false);
        SpriteRenderer floorRenderer = floor.AddComponent<SpriteRenderer>();
        floorRenderer.sprite = SpatialRuntimeSpriteCache.Solid;
        floorRenderer.drawMode = SpriteDrawMode.Tiled;
        floorRenderer.size = size;
        floorRenderer.color = new Color(0.20f, 0.23f, 0.27f, 1f);
        floorRenderer.sortingOrder = -30;

        NavMeshModifier floorModifier = floor.AddComponent<NavMeshModifier>();
        floorModifier.ignoreFromBuild = false;
        floorModifier.overrideArea = false;

        float rail = 0.16f;
        if (horizontal)
        {
            CreateRail(root.transform, new Vector2(0f, size.y * 0.5f), new Vector2(size.x, rail));
            CreateRail(root.transform, new Vector2(0f, -size.y * 0.5f), new Vector2(size.x, rail));
        }
        else
        {
            CreateRail(root.transform, new Vector2(size.x * 0.5f, 0f), new Vector2(rail, size.y));
            CreateRail(root.transform, new Vector2(-size.x * 0.5f, 0f), new Vector2(rail, size.y));
        }

        MapBlock mapBlock = root.AddComponent<MapBlock>();
        mapBlock.ConfigureRuntimeDockingBlock(root.transform, true, 0.62f, corridorEntryDuration, corridorEntryOffset);
        return root;
    }

    private void CreateRail(Transform parent, Vector2 localPosition, Vector2 size)
    {
        GameObject rail = new("Edge");
        rail.transform.SetParent(parent, false);
        rail.transform.localPosition = localPosition;

        SpriteRenderer renderer = rail.AddComponent<SpriteRenderer>();
        renderer.sprite = SpatialRuntimeSpriteCache.Solid;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.size = size;
        renderer.color = roomEdgeColor;
        renderer.sortingOrder = 3;

        BoxCollider2D collider = rail.AddComponent<BoxCollider2D>();
        collider.size = size;
        collider.isTrigger = false;

        NavMeshModifier modifier = rail.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = false;
        modifier.overrideArea = true;
        modifier.area = 1;
    }

    private void PlacePlayerAtRouteStart(Vector3 fromOrigin, RoomDefinitionSO room, Vector2 direction)
    {
        if (player == null || room == null)
            return;

        Vector2 size = room.GetRuntimeBaseWorldSize();
        Vector2 center = (Vector2)fromOrigin + room.GetRuntimeBaseCenterOffset();
        Vector2 position = center;

        if (Mathf.Abs(direction.x) > 0.5f)
            position.x += direction.x * (size.x * 0.5f - 0.55f);
        else
            position.y += direction.y * (size.y * 0.5f - 0.55f);

        Vector3 world = new(position.x, position.y, player.transform.position.z);
        player.transform.position = world;

        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
            body.linearVelocity = Vector2.zero;
    }

    private void PrepareLargeRoomPiece(BattleNodeData node, Vector2 approachDirection)
    {
        if (node == null || node.room == null || !node.room.useLargeRoomPiece)
            return;

        RoomDefinitionSO room = node.room;
        List<MapBlockPlacement> originalBlocks = room.blocks;
        bool originalReposition = room.repositionPlayerOnEnter;

        MapBlock prototype = CreateLargeRoomPrototype(room, node, approachDirection);
        if (prototype == null)
            return;

        Vector2Int grid = room.GetSafeGridSize();
        Vector2Int anchorGrid = new(grid.x + 2, 0);
        Vector2 entry = room.largePieceEntryDirection.sqrMagnitude > 0.001f
            ? room.largePieceEntryDirection.normalized
            : Vector2.down;

        room.blocks = new List<MapBlockPlacement>
        {
            new()
            {
                prefab = prototype,
                gridPosition = anchorGrid,
                entryDirection = entry
            }
        };

        // 모든 Room 이동은 통로를 직접 걸어가는 방식이므로 자동 순간이동을 막습니다.
        room.repositionPlayerOnEnter = false;
        StartCoroutine(RestoreRoomPresentationDataNextFrame(room, originalBlocks, originalReposition, prototype.gameObject));
    }

    private MapBlock CreateLargeRoomPrototype(RoomDefinitionSO room, BattleNodeData node, Vector2 approachDirection)
    {
        Vector2Int grid = room.GetLargePieceGridSize();
        HashSet<Vector2Int> cells = BuildRoomCells(room, node, grid);
        if (cells.Count == 0)
            return null;

        Vector2Int anchorGrid = new(room.GetSafeGridSize().x + 2, 0);
        Vector2 anchorWorld = room.GetBlockLocalPosition(anchorGrid);

        GameObject root = new($"__LargeRoomPiecePrototype_{room.roomId}");
        root.transform.position = new Vector3(10000f, 10000f, 0f);

        foreach (Vector2Int cell in cells)
        {
            GameObject floor = new($"Floor_{cell.x}_{cell.y}");
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = (Vector3)(room.GetBlockLocalPosition(cell) - anchorWorld);

            SpriteRenderer renderer = floor.AddComponent<SpriteRenderer>();
            renderer.sprite = SpatialRuntimeSpriteCache.Solid;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.size = MapBlock.BlockWorldSize * 0.98f;
            renderer.color = roomFloorColor;
            renderer.sortingOrder = -20;

            NavMeshModifier modifier = floor.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = false;
            modifier.overrideArea = false;
        }

        Vector2 entranceNormal = -approachDirection;
        Vector2Int entranceCell = FindEntranceCell(cells, grid, entranceNormal);
        Vector2Int[] directions = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        foreach (Vector2Int cell in cells)
        {
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2Int edge = directions[i];
                if (cells.Contains(cell + edge))
                    continue;

                bool entranceEdge = entranceCell == cell &&
                                    Vector2.Dot((Vector2)edge, entranceNormal) > 0.9f;
                if (entranceEdge)
                    continue;

                CreateRoomEdge(root.transform, room.GetBlockLocalPosition(cell) - anchorWorld, edge);
            }
        }

        MapBlock block = root.AddComponent<MapBlock>();
        block.ConfigureRuntimeDockingBlock(
            root.transform,
            true,
            roomImpactStrength,
            room.largePieceEntryDuration,
            room.largePieceEntryOffset);
        return block;
    }

    private HashSet<Vector2Int> BuildRoomCells(RoomDefinitionSO room, BattleNodeData node, Vector2Int grid)
    {
        HashSet<Vector2Int> cells = new();
        RoomLargePieceShape shape = room.largePieceShape == RoomLargePieceShape.Auto
            ? RoomLargePieceShape.Rectangle
            : room.largePieceShape;

        if (shape == RoomLargePieceShape.Custom)
        {
            if (room.customLargePieceCells != null)
            {
                for (int i = 0; i < room.customLargePieceCells.Count; i++)
                    cells.Add(room.customLargePieceCells[i]);
            }
            return cells;
        }

        for (int y = 0; y < grid.y; y++)
        {
            for (int x = 0; x < grid.x; x++)
            {
                bool include = shape switch
                {
                    RoomLargePieceShape.LShape => x < Mathf.Max(1, grid.x / 2) || y < Mathf.Max(1, grid.y / 2),
                    RoomLargePieceShape.TShape => y >= Mathf.Max(0, grid.y - Mathf.Max(1, grid.y / 2)) ||
                                                  (x >= grid.x / 3 && x <= (grid.x - 1) - grid.x / 3),
                    RoomLargePieceShape.Cross =>
                        (x >= grid.x / 3 && x <= (grid.x - 1) - grid.x / 3) ||
                        (y >= grid.y / 3 && y <= (grid.y - 1) - grid.y / 3),
                    _ => true
                };

                if (include)
                    cells.Add(new Vector2Int(x, y));
            }
        }

        return cells;
    }

    private static Vector2Int FindEntranceCell(HashSet<Vector2Int> cells, Vector2Int grid, Vector2 entranceNormal)
    {
        Vector2 center = new((grid.x - 1) * 0.5f, (grid.y - 1) * 0.5f);
        Vector2Int best = default;
        float bestScore = float.MaxValue;
        bool found = false;

        foreach (Vector2Int cell in cells)
        {
            Vector2Int neighbor = cell + new Vector2Int(Mathf.RoundToInt(entranceNormal.x), Mathf.RoundToInt(entranceNormal.y));
            if (cells.Contains(neighbor))
                continue;

            float centerDistance = Vector2.Distance(cell, center);
            if (centerDistance < bestScore)
            {
                bestScore = centerDistance;
                best = cell;
                found = true;
            }
        }

        return found ? best : Vector2Int.zero;
    }

    private void CreateRoomEdge(Transform root, Vector2 cellLocalCenter, Vector2Int edge)
    {
        bool vertical = edge.x != 0;
        Vector2 normal = edge;
        Vector2 position = cellLocalCenter + normal * MapBlock.BlockWorldSize.x * 0.5f;
        Vector2 size = vertical
            ? new Vector2(roomEdgeThickness, MapBlock.BlockWorldSize.y + roomEdgeThickness)
            : new Vector2(MapBlock.BlockWorldSize.x + roomEdgeThickness, roomEdgeThickness);

        GameObject edgeObject = new("RoomEdge");
        edgeObject.transform.SetParent(root, false);
        edgeObject.transform.localPosition = position;

        SpriteRenderer renderer = edgeObject.AddComponent<SpriteRenderer>();
        renderer.sprite = SpatialRuntimeSpriteCache.Solid;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.size = size;
        renderer.color = roomEdgeColor;
        renderer.sortingOrder = 2;

        BoxCollider2D collider = edgeObject.AddComponent<BoxCollider2D>();
        collider.size = size;
        collider.isTrigger = false;

        NavMeshModifier modifier = edgeObject.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = false;
        modifier.overrideArea = true;
        modifier.area = 1;
    }

    private IEnumerator RestoreRoomPresentationDataNextFrame(
        RoomDefinitionSO room,
        List<MapBlockPlacement> originalBlocks,
        bool originalReposition,
        GameObject prototype)
    {
        yield return null;

        if (room != null)
        {
            room.blocks = originalBlocks ?? new List<MapBlockPlacement>();
            room.repositionPlayerOnEnter = originalReposition;
        }

        if (prototype != null)
            Destroy(prototype);
    }

    private static string RouteKey(Vector2Int a, Vector2Int b)
    {
        if (a.x < b.x || (a.x == b.x && a.y <= b.y))
            return $"{a.x},{a.y}>{b.x},{b.y}";
        return $"{b.x},{b.y}>{a.x},{a.y}";
    }

    private void ClearPersistentRoutes()
    {
        for (int i = 0; i < persistentRouteObjects.Count; i++)
        {
            if (persistentRouteObjects[i] != null)
                Destroy(persistentRouteObjects[i]);
        }
        persistentRouteObjects.Clear();
        builtRouteKeys.Clear();
    }

    private void ApplyReadableDefaultCharacterSizes()
    {
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();

        if (player != null && player.spriteSO != null && player.spriteSO.name.StartsWith("TEST_", StringComparison.OrdinalIgnoreCase))
        {
            float scale = Mathf.Max(1f, testPlayerScale);
            player.transform.localScale = new Vector3(scale, scale, 1f);
            CircleCollider2D circle = player.GetComponent<CircleCollider2D>();
            if (circle != null)
                circle.radius = Mathf.Max(0.1f, testPlayerColliderRadius);
        }

        MonsterController[] monsters = FindObjectsByType<MonsterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < monsters.Length; i++)
        {
            MonsterController monster = monsters[i];
            if (monster == null || monster.Definition == null ||
                string.IsNullOrEmpty(monster.Definition.monsterId) ||
                !monster.Definition.monsterId.StartsWith("TEST_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            float scale = Mathf.Max(1f, testMonsterScale);
            monster.transform.localScale = new Vector3(scale, scale, 1f);

            CircleCollider2D circle = monster.GetComponent<CircleCollider2D>();
            if (circle != null)
                circle.radius = Mathf.Max(0.1f, testMonsterColliderRadius);

            NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
            if (agent != null)
                agent.radius = Mathf.Max(agent.radius, 0.40f);
        }
    }

    private void EnsureMiniMapUI()
    {
        if (miniMapCanvas != null)
            return;

        GameObject canvasObject = new("BattleMiniMapCanvas");
        DontDestroyOnLoad(canvasObject);
        miniMapCanvas = canvasObject.AddComponent<Canvas>();
        miniMapCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        miniMapCanvas.sortingOrder = 600;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panelObject = new("MiniMapPanel");
        panelObject.transform.SetParent(canvasObject.transform, false);
        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = miniMapBackground;
        panelImage.raycastTarget = false;

        miniMapPanel = panelObject.GetComponent<RectTransform>();
        miniMapPanel.anchorMin = Vector2.one;
        miniMapPanel.anchorMax = Vector2.one;
        miniMapPanel.pivot = Vector2.one;
        miniMapPanel.anchoredPosition = new Vector2(-22f, -22f);
        miniMapPanel.sizeDelta = miniMapSize;
    }

    private void RefreshMiniMap()
    {
        if (miniMapPanel == null || graph == null)
            return;

        for (int i = miniMapPanel.childCount - 1; i >= 0; i--)
            Destroy(miniMapPanel.GetChild(i).gameObject);

        BuildResolvedLayout();

        List<Vector2Int> allPositions = new() { Vector2Int.zero };
        if (graph.nodes != null)
        {
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                BattleNodeData node = graph.nodes[i];
                if (node != null)
                    allPositions.Add(ResolveNodeMapPosition(node));
            }
        }

        Vector2 mapCenter = CalculateMapCenter(allPositions);
        float spacing = Mathf.Min(miniMapCellSpacing, CalculateMiniMapFitSpacing(allPositions));

        DrawMiniMapLink(Vector2Int.zero, ResolveNodeMapPosition(graph.GetStartNode()), mapCenter, spacing);

        if (graph.nodes != null)
        {
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                BattleNodeData node = graph.nodes[i];
                if (node == null || node.nextNodeIds == null)
                    continue;

                Vector2Int from = ResolveNodeMapPosition(node);
                List<BattleNodeData> next = graph.GetNextNodes(node);
                for (int n = 0; n < next.Count; n++)
                    DrawMiniMapLink(from, ResolveNodeMapPosition(next[n]), mapCenter, spacing);
            }
        }

        bool startCurrent = runManager != null && runManager.IsInStartArea;
        DrawMiniMapNode(Vector2Int.zero, startCurrent ? miniMapCurrent : miniMapVisited, mapCenter, spacing, 0.85f);

        HashSet<string> available = new();
        if (runManager != null)
        {
            IReadOnlyList<BattleNodeData> next = runManager.NextNodeChoices;
            for (int i = 0; i < next.Count; i++)
            {
                if (next[i] != null)
                    available.Add(next[i].id);
            }
        }

        if (graph.nodes != null)
        {
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                BattleNodeData node = graph.nodes[i];
                if (node == null)
                    continue;

                Color color = miniMapUnknown;
                if (runManager != null && runManager.CurrentNode == node)
                    color = miniMapCurrent;
                else if (available.Contains(node.id))
                    color = miniMapAvailable;
                else if (visitedNodeIds.Contains(node.id))
                    color = miniMapVisited;

                DrawMiniMapNode(ResolveNodeMapPosition(node), color, mapCenter, spacing, node.type == BattleNodeType.Elite ? 1.18f : 1f);
            }
        }
    }

    private float CalculateMiniMapFitSpacing(List<Vector2Int> positions)
    {
        if (positions == null || positions.Count == 0)
            return miniMapCellSpacing;

        int minX = positions[0].x;
        int maxX = positions[0].x;
        int minY = positions[0].y;
        int maxY = positions[0].y;
        for (int i = 1; i < positions.Count; i++)
        {
            minX = Mathf.Min(minX, positions[i].x);
            maxX = Mathf.Max(maxX, positions[i].x);
            minY = Mathf.Min(minY, positions[i].y);
            maxY = Mathf.Max(maxY, positions[i].y);
        }

        float rangeX = Mathf.Max(1, maxX - minX);
        float rangeY = Mathf.Max(1, maxY - minY);
        float fitX = (miniMapSize.x - 42f) / rangeX;
        float fitY = (miniMapSize.y - 42f) / rangeY;
        return Mathf.Max(18f, Mathf.Min(fitX, fitY));
    }

    private static Vector2 CalculateMapCenter(List<Vector2Int> positions)
    {
        if (positions == null || positions.Count == 0)
            return Vector2.zero;

        int minX = positions[0].x;
        int maxX = positions[0].x;
        int minY = positions[0].y;
        int maxY = positions[0].y;
        for (int i = 1; i < positions.Count; i++)
        {
            minX = Mathf.Min(minX, positions[i].x);
            maxX = Mathf.Max(maxX, positions[i].x);
            minY = Mathf.Min(minY, positions[i].y);
            maxY = Mathf.Max(maxY, positions[i].y);
        }
        return new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
    }

    private void DrawMiniMapNode(Vector2Int mapPosition, Color color, Vector2 mapCenter, float spacing, float sizeMultiplier)
    {
        GameObject node = new("MapNode");
        node.transform.SetParent(miniMapPanel, false);
        Image image = node.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        RectTransform rect = node.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(
            (mapPosition.x - mapCenter.x) * spacing,
            (mapPosition.y - mapCenter.y) * spacing);
        rect.sizeDelta = Vector2.one * miniMapNodeSize * sizeMultiplier;
    }

    private void DrawMiniMapLink(Vector2Int from, Vector2Int to, Vector2 mapCenter, float spacing)
    {
        if (from == to)
            return;

        Vector2 a = new((from.x - mapCenter.x) * spacing, (from.y - mapCenter.y) * spacing);
        Vector2 b = new((to.x - mapCenter.x) * spacing, (to.y - mapCenter.y) * spacing);
        Vector2 delta = b - a;

        if (Mathf.Abs(delta.x) > 0.01f)
            CreateMiniMapBar(new Vector2((a.x + b.x) * 0.5f, a.y), new Vector2(Mathf.Abs(delta.x), 4f));
        if (Mathf.Abs(delta.y) > 0.01f)
            CreateMiniMapBar(new Vector2(b.x, (a.y + b.y) * 0.5f), new Vector2(4f, Mathf.Abs(delta.y)));
    }

    private void CreateMiniMapBar(Vector2 position, Vector2 size)
    {
        GameObject bar = new("MapLink");
        bar.transform.SetParent(miniMapPanel, false);
        Image image = bar.AddComponent<Image>();
        image.color = miniMapLink;
        image.raycastTarget = false;

        RectTransform rect = bar.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        bar.transform.SetAsFirstSibling();
    }
}

internal static class SpatialRuntimeSpriteCache
{
    private static Sprite solid;
    public static Sprite Solid => solid != null ? solid : solid = CreateSolid();

    private static Sprite CreateSolid()
    {
        Texture2D texture = new(2, 2, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, 2f, 2f),
            new Vector2(0.5f, 0.5f),
            2f,
            0,
            SpriteMeshType.FullRect);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        sprite.name = "SpatialRuntimeSolid";
        return sprite;
    }
}

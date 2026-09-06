using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NavMeshPlus.Components;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// <summary>
/// Sephiria-like spatial battle map layer.
///
/// Rules:
/// - Start Area is an independent 3x3+ tile platform, not a Combat Room.
/// - Gameplay Rooms are 6x6+ 32px-tile masks and may be Rectangle/L/T/Cross/Irregular/Custom.
/// - A Room is one large docking piece even though it contains many tiles.
/// - Current-room exits are represented by world Direction Markers.
/// - Selecting/entering a node reserves logical navigation, but visible corridor/room pieces are revealed only
///   when their destination enters the camera range (+ margin).
/// - Mini Map uses the same NodeGraph coordinates as the world map.
/// </summary>
[DefaultExecutionOrder(-20000)]
public sealed class BattleSpatialMapController : MonoBehaviour
{
    [Header("World Map")]
    [SerializeField, Min(12f)] private float roomWorldSpacing = 18f;
    [SerializeField, Min(1f)] private float corridorWidth = 2f;
    [SerializeField, Range(2, 7)] private int corridorPieceCount = 3;
    [SerializeField, Min(0.05f)] private float corridorEntryDuration = 0.34f;
    [SerializeField, Min(0.5f)] private float corridorEntryOffset = 5f;
    [SerializeField, Min(0f)] private float corridorPieceStagger = 0.05f;

    [Header("Camera Reveal")]
    [Tooltip("Piece destination이 카메라 경계에서 이 거리 안으로 들어오면 실제 오브젝트를 생성합니다.")]
    [SerializeField, Min(0f)] private float revealMarginWorld = 1.6f;
    [SerializeField, Min(0.02f)] private float revealPollInterval = 0.04f;

    [Header("Large Room Piece")]
    [SerializeField] private Color roomFloorColor = new(0.18f, 0.21f, 0.25f, 1f);
    [SerializeField] private Color roomEdgeColor = new(0.31f, 0.35f, 0.41f, 1f);
    [SerializeField, Min(0.03f)] private float roomEdgeThickness = 0.12f;
    [SerializeField, Range(0.1f, 1.5f)] private float roomImpactStrength = 0.95f;

    [Header("Direction Marker")]
    [SerializeField, Min(0.2f)] private float markerWorldSize = 0.48f;
    [SerializeField, Min(0.1f)] private float markerTriggerRadius = 0.52f;
    [SerializeField, Min(0f)] private float markerInset = 0.34f;
    [SerializeField] private Color markerAvailableColor = new(0.25f, 0.95f, 1f, 0.92f);
    [SerializeField] private Color markerEliteColor = new(1f, 0.62f, 0.18f, 0.96f);

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

    [Header("32px Tile / 48px Character Test Scale")]
    [Tooltip("32px tile = 1 world, 48px player = 1.5 world.")]
    [SerializeField, Min(0.5f)] private float testPlayerWorldHeight = 1.5f;
    [SerializeField, Min(0.1f)] private float testPlayerWorldColliderRadius = 0.42f;
    [SerializeField, Min(0.5f)] private float testMonsterWorldHeight = 1.5f;
    [SerializeField, Min(0.1f)] private float testMonsterWorldColliderRadius = 0.40f;

    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;
    private NodeGraphSO graph;

    private readonly Dictionary<string, Vector2Int> resolvedMapPositions = new();
    private readonly Dictionary<string, ProceduralRoomLayout> roomLayouts = new();
    private readonly HashSet<string> visitedNodeIds = new();
    private readonly HashSet<string> reservedRouteKeys = new();
    private readonly List<GameObject> persistentRouteObjects = new();
    private readonly List<GameObject> logicalNavigationObjects = new();
    private readonly List<GameObject> directionMarkers = new();

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
        ClearDirectionMarkers();
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
        SyncStartDirectionWithGraph();
        RefreshMiniMap();
        RefreshWorldDirectionMarkers();
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
        if (runManager == null || roomManager == null || graph == null)
        {
            TryResolveSystems();
            if (runManager != null)
            {
                Subscribe();
                DisableLegacyAutoShell();
                EnsureMiniMapUI();
                BuildResolvedLayout();
                SyncStartDirectionWithGraph();
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
        RefreshWorldDirectionMarkers();
    }

    private void HandleNextNodeSelectionRequested(IReadOnlyList<BattleNodeData> _)
    {
        RefreshMiniMap();
        RefreshWorldDirectionMarkers();
    }

    private void HandleRunEnded(RunEndReason _)
    {
        visitedNodeIds.Clear();
        lastEnteredNode = null;
        lastMapPosition = Vector2Int.zero;
        lastRoom = null;
        roomLayouts.Clear();
        ClearPersistentRoutes();
        ClearLogicalNavigation();
        ClearDirectionMarkers();
        RefreshMiniMap();
    }

    /// <summary>
    /// NodeEntered는 "다음 공간이 선택되었다"는 의미입니다.
    /// 여기서 visible Room 전체를 즉시 만들지 않습니다.
    /// Logical navigation만 먼저 예약하고 visible corridor/room은 Camera Reveal coroutine이 담당합니다.
    /// </summary>
    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node == null || node.room == null || roomManager == null)
            return;

        ClearDirectionMarkers();
        ResolveStartOriginFromBase(node.room);
        BuildResolvedLayout();

        Vector2Int targetMapPosition = ResolveNodeMapPosition(node);
        Vector3 targetRoomOrigin = MapPositionToWorld(targetMapPosition);
        Vector3 previousOrigin = MapPositionToWorld(lastMapPosition);
        RoomDefinitionSO previousRoom = lastRoom != null ? lastRoom : node.room;

        Vector2 travelDirection = CardinalDirection(targetMapPosition - lastMapPosition);
        if (travelDirection == Vector2.zero)
            travelDirection = Vector2.right;

        ProceduralRoomLayout targetLayout = GetOrCreateLayout(node, travelDirection);
        ProceduralRoomLayout previousLayout = lastEnteredNode != null
            ? GetOrCreateLayout(lastEnteredNode, -travelDirection)
            : CreateStartLayout(node.room);

        roomManager.RoomOrigin.position = targetRoomOrigin;

        RouteGeometry route = CalculateRouteGeometry(
            previousOrigin,
            previousRoom,
            previousLayout,
            targetRoomOrigin,
            node.room,
            targetLayout,
            travelDirection);

        ReserveLogicalRouteAndRoom(route, node, targetRoomOrigin, targetLayout, travelDirection);
        SuppressLegacyRoomPiecesForCurrentEnter(node.room);

        // BattleRunManager가 Start -> first room 전환 직전에 Player를 Room으로 순간이동시키는 legacy 동작을 하므로
        // 첫 transition에서는 다시 Start 출구 앞에 놓아 실제 Corridor를 걸어가게 합니다.
        if (lastEnteredNode == null)
            PlacePlayerAtRouteStart(route.start, travelDirection);

        StartCoroutine(RevealRouteAndRoomRoutine(route, node, targetRoomOrigin, targetLayout, travelDirection));

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

    private Vector3 MapPositionToWorld(Vector2Int mapPosition)
    {
        return startOrigin + new Vector3(
            mapPosition.x * roomWorldSpacing,
            mapPosition.y * roomWorldSpacing,
            0f);
    }

    private void BuildResolvedLayout()
    {
        if (graph == null)
            return;

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

            Vector2Int parentPosition = ResolveNodeMapPosition(parent);
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
            Vector2Int.left,
            Vector2Int.down
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

    private void SyncStartDirectionWithGraph()
    {
        if (runManager == null || graph == null)
            return;

        BattleNodeData first = graph.GetStartNode();
        if (first == null)
            return;

        BuildResolvedLayout();
        Vector2 direction = CardinalDirection(ResolveNodeMapPosition(first));
        if (direction == Vector2.zero)
            direction = Vector2.right;

        FieldInfo directionField = typeof(BattleRunManager).GetField("firstRoomDirection", InstanceFields);
        if (directionField != null)
            directionField.SetValue(runManager, direction);
    }

    private ProceduralRoomLayout CreateStartLayout(RoomDefinitionSO room)
    {
        Vector2Int size = room != null ? room.GetStartBaseTileSize() : new Vector2Int(3, 3);
        HashSet<Vector2Int> cells = new();
        for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
                cells.Add(new Vector2Int(x, y));
        return new ProceduralRoomLayout(size, cells);
    }

    private ProceduralRoomLayout GetOrCreateLayout(BattleNodeData node, Vector2 approachDirection)
    {
        if (node == null || node.room == null)
            return new ProceduralRoomLayout(new Vector2Int(6, 6), new HashSet<Vector2Int>());

        if (roomLayouts.TryGetValue(node.id, out ProceduralRoomLayout cached))
            return cached;

        HashSet<Vector2Int> requiredDirections = new();
        Vector2Int approach = DirectionToInt(-approachDirection);
        if (approach != Vector2Int.zero)
            requiredDirections.Add(approach);

        if (graph != null)
        {
            Vector2Int current = ResolveNodeMapPosition(node);
            List<BattleNodeData> next = graph.GetNextNodes(node);
            for (int i = 0; i < next.Count; i++)
            {
                Vector2 direction = CardinalDirection(ResolveNodeMapPosition(next[i]) - current);
                Vector2Int edge = DirectionToInt(direction);
                if (edge != Vector2Int.zero)
                    requiredDirections.Add(edge);
            }
        }

        ProceduralRoomLayout layout = GenerateProceduralLayout(node, requiredDirections);
        roomLayouts[node.id] = layout;
        return layout;
    }

    private ProceduralRoomLayout GenerateProceduralLayout(BattleNodeData node, HashSet<Vector2Int> requiredEdges)
    {
        RoomDefinitionSO room = node.room;
        if (!room.useProceduralRoom)
        {
            Vector2Int grid = room.GetLargePieceGridSize();
            HashSet<Vector2Int> fixedCells = BuildPresetCells(room, grid);
            EnsureRequiredEdges(fixedCells, grid, requiredEdges);
            return new ProceduralRoomLayout(grid, fixedCells);
        }

        Vector2Int min = room.GetProceduralMinTileSize();
        Vector2Int max = room.GetProceduralMaxTileSize();
        int seed = StableHash(node.id) ^ StableHash(room.roomId) ^ room.proceduralSeed;
        System.Random random = new(seed);

        Vector2Int size = new(
            random.Next(min.x, max.x + 1),
            random.Next(min.y, max.y + 1));

        RoomLargePieceShape shape = room.largePieceShape;
        if (shape == RoomLargePieceShape.Auto)
            shape = RoomLargePieceShape.Irregular;

        HashSet<Vector2Int> cells = BuildPresetCells(room, size, shape);

        if (shape == RoomLargePieceShape.Irregular)
        {
            float complexity = Mathf.Clamp01(room.proceduralComplexity);
            float carveChance = Mathf.Clamp01(room.proceduralIndentChance + complexity * 0.18f);
            int passes = 1 + Mathf.RoundToInt(complexity * 4f);

            for (int pass = 0; pass < passes; pass++)
            {
                List<Vector2Int> candidates = new(cells);
                Shuffle(candidates, random);

                for (int i = 0; i < candidates.Count; i++)
                {
                    Vector2Int cell = candidates[i];
                    if (!IsPerimeter(cell, size) || IsCentralSafeCell(cell, size))
                        continue;
                    if (random.NextDouble() > carveChance)
                        continue;

                    cells.Remove(cell);
                    if (!IsConnected(cells))
                        cells.Add(cell);
                }
            }

            // 작은 bay/돌출부 느낌을 위해 깎인 외곽을 일부 다시 살립니다.
            float extensionChance = Mathf.Clamp01(room.proceduralExtensionChance + complexity * 0.15f);
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    Vector2Int cell = new(x, y);
                    if (cells.Contains(cell) || !IsPerimeter(cell, size))
                        continue;
                    if (random.NextDouble() > extensionChance)
                        continue;
                    if (CountCardinalNeighbors(cells, cell) >= 2)
                        cells.Add(cell);
                }
            }
        }

        EnsureRequiredEdges(cells, size, requiredEdges);
        EnsureCentralCombatArea(cells, size);
        return new ProceduralRoomLayout(size, cells);
    }

    private HashSet<Vector2Int> BuildPresetCells(RoomDefinitionSO room, Vector2Int grid, RoomLargePieceShape? forced = null)
    {
        HashSet<Vector2Int> cells = new();
        RoomLargePieceShape shape = forced ?? room.largePieceShape;
        if (shape == RoomLargePieceShape.Auto)
            shape = RoomLargePieceShape.Rectangle;

        if (shape == RoomLargePieceShape.Custom)
        {
            if (room.customLargePieceCells != null)
            {
                for (int i = 0; i < room.customLargePieceCells.Count; i++)
                {
                    Vector2Int c = room.customLargePieceCells[i];
                    if (c.x >= 0 && c.y >= 0 && c.x < grid.x && c.y < grid.y)
                        cells.Add(c);
                }
            }
            return cells;
        }

        for (int y = 0; y < grid.y; y++)
        {
            for (int x = 0; x < grid.x; x++)
            {
                bool include = shape switch
                {
                    RoomLargePieceShape.LShape => x < Mathf.Max(2, grid.x / 2) || y < Mathf.Max(2, grid.y / 2),
                    RoomLargePieceShape.TShape => y >= grid.y - Mathf.Max(2, grid.y / 3) ||
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

    private static void EnsureCentralCombatArea(HashSet<Vector2Int> cells, Vector2Int size)
    {
        int minX = Mathf.Max(0, size.x / 2 - 2);
        int maxX = Mathf.Min(size.x - 1, minX + 3);
        int minY = Mathf.Max(0, size.y / 2 - 2);
        int maxY = Mathf.Min(size.y - 1, minY + 3);

        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                cells.Add(new Vector2Int(x, y));
    }

    private static void EnsureRequiredEdges(HashSet<Vector2Int> cells, Vector2Int size, HashSet<Vector2Int> edges)
    {
        if (edges == null)
            return;

        foreach (Vector2Int edge in edges)
        {
            Vector2Int door;
            if (edge == Vector2Int.right) door = new Vector2Int(size.x - 1, size.y / 2);
            else if (edge == Vector2Int.left) door = new Vector2Int(0, size.y / 2);
            else if (edge == Vector2Int.up) door = new Vector2Int(size.x / 2, size.y - 1);
            else if (edge == Vector2Int.down) door = new Vector2Int(size.x / 2, 0);
            else continue;

            cells.Add(door);
            Vector2Int center = new(size.x / 2, size.y / 2);
            Vector2Int cursor = door;
            while (cursor != center)
            {
                if (cursor.x != center.x)
                    cursor.x += Math.Sign(center.x - cursor.x);
                else if (cursor.y != center.y)
                    cursor.y += Math.Sign(center.y - cursor.y);
                cells.Add(cursor);
            }
        }
    }

    private static bool IsCentralSafeCell(Vector2Int cell, Vector2Int size)
    {
        Vector2 center = new((size.x - 1) * 0.5f, (size.y - 1) * 0.5f);
        return Mathf.Abs(cell.x - center.x) <= 1.5f && Mathf.Abs(cell.y - center.y) <= 1.5f;
    }

    private static bool IsPerimeter(Vector2Int cell, Vector2Int size)
    {
        return cell.x == 0 || cell.y == 0 || cell.x == size.x - 1 || cell.y == size.y - 1;
    }

    private static int CountCardinalNeighbors(HashSet<Vector2Int> cells, Vector2Int cell)
    {
        int count = 0;
        if (cells.Contains(cell + Vector2Int.right)) count++;
        if (cells.Contains(cell + Vector2Int.left)) count++;
        if (cells.Contains(cell + Vector2Int.up)) count++;
        if (cells.Contains(cell + Vector2Int.down)) count++;
        return count;
    }

    private static bool IsConnected(HashSet<Vector2Int> cells)
    {
        if (cells == null || cells.Count == 0)
            return false;

        Vector2Int first = default;
        foreach (Vector2Int c in cells) { first = c; break; }

        Queue<Vector2Int> queue = new();
        HashSet<Vector2Int> visited = new();
        queue.Enqueue(first);
        visited.Add(first);

        Vector2Int[] dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector2Int n = c + dirs[i];
                if (cells.Contains(n) && visited.Add(n))
                    queue.Enqueue(n);
            }
        }
        return visited.Count == cells.Count;
    }

    private static void Shuffle<T>(List<T> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            int hash = 23;
            if (text != null)
                for (int i = 0; i < text.Length; i++)
                    hash = hash * 31 + text[i];
            return hash;
        }
    }

    private static Vector2Int DirectionToInt(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return Vector2Int.zero;
        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
            return direction.x >= 0f ? Vector2Int.right : Vector2Int.left;
        return direction.y >= 0f ? Vector2Int.up : Vector2Int.down;
    }

    private RouteGeometry CalculateRouteGeometry(
        Vector3 fromOrigin,
        RoomDefinitionSO fromRoom,
        ProceduralRoomLayout fromLayout,
        Vector3 toOrigin,
        RoomDefinitionSO toRoom,
        ProceduralRoomLayout toLayout,
        Vector2 direction)
    {
        Vector2 fromCenter = (Vector2)fromOrigin + fromLayout.CenterOffset;
        Vector2 toCenter = (Vector2)toOrigin + toLayout.CenterOffset;
        Vector2 fromHalf = fromLayout.WorldSize * 0.5f;
        Vector2 toHalf = toLayout.WorldSize * 0.5f;

        Vector2 start;
        Vector2 end;
        bool horizontal = Mathf.Abs(direction.x) > 0.5f;
        if (horizontal)
        {
            start = fromCenter + new Vector2(direction.x * fromHalf.x, 0f);
            end = new Vector2(toCenter.x - direction.x * toHalf.x, start.y);
        }
        else
        {
            start = fromCenter + new Vector2(0f, direction.y * fromHalf.y);
            end = new Vector2(start.x, toCenter.y - direction.y * toHalf.y);
        }

        return new RouteGeometry(start, end, horizontal);
    }

    private void ReserveLogicalRouteAndRoom(
        RouteGeometry route,
        BattleNodeData node,
        Vector3 roomOrigin,
        ProceduralRoomLayout layout,
        Vector2 approachDirection)
    {
        string key = RouteKey(lastMapPosition, ResolveNodeMapPosition(node));
        if (reservedRouteKeys.Add(key))
            BuildLogicalCorridor(route);

        BuildLogicalRoom(node, roomOrigin, layout, approachDirection);
    }

    private void BuildLogicalCorridor(RouteGeometry route)
    {
        float length = route.Length;
        if (length <= 0.1f)
            return;

        GameObject root = new($"LogicalRoute_{logicalNavigationObjects.Count}");
        root.transform.position = route.Center;

        SpriteRenderer floor = root.AddComponent<SpriteRenderer>();
        floor.sprite = SpatialRuntimeSpriteCache.Solid;
        floor.drawMode = SpriteDrawMode.Tiled;
        floor.size = route.horizontal
            ? new Vector2(length, corridorWidth)
            : new Vector2(corridorWidth, length);
        floor.color = new Color(1f, 1f, 1f, 0f);
        floor.sortingOrder = -1000;

        NavMeshModifier modifier = root.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = false;
        modifier.overrideArea = false;

        logicalNavigationObjects.Add(root);
    }

    private void BuildLogicalRoom(BattleNodeData node, Vector3 roomOrigin, ProceduralRoomLayout layout, Vector2 approachDirection)
    {
        GameObject root = new($"LogicalRoom_{node.id}");
        root.transform.position = roomOrigin;

        HashSet<Vector2Int> doorwayNormals = GetDoorwayNormals(node, approachDirection);

        foreach (Vector2Int cell in layout.cells)
        {
            GameObject floor = new($"NavFloor_{cell.x}_{cell.y}");
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = (Vector3)((Vector2)cell * RoomDefinitionSO.ProceduralTileWorldSize);

            SpriteRenderer renderer = floor.AddComponent<SpriteRenderer>();
            renderer.sprite = SpatialRuntimeSpriteCache.Solid;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.size = Vector2.one * RoomDefinitionSO.ProceduralTileWorldSize;
            renderer.color = new Color(1f, 1f, 1f, 0f);
            renderer.sortingOrder = -1000;

            NavMeshModifier modifier = floor.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = false;
            modifier.overrideArea = false;
        }

        BuildBoundaryEdges(root.transform, layout, doorwayNormals, false, true);
        logicalNavigationObjects.Add(root);
    }

    private IEnumerator RevealRouteAndRoomRoutine(
        RouteGeometry route,
        BattleNodeData node,
        Vector3 targetRoomOrigin,
        ProceduralRoomLayout layout,
        Vector2 travelDirection)
    {
        float totalLength = route.Length;
        int count = Mathf.Max(1, corridorPieceCount);
        float pieceLength = totalLength / count;
        float sign = route.horizontal ? Mathf.Sign(route.end.x - route.start.x) : Mathf.Sign(route.end.y - route.start.y);

        for (int i = 0; i < count; i++)
        {
            float distance = pieceLength * (i + 0.5f);
            Vector2 center = route.horizontal
                ? route.start + Vector2.right * sign * distance
                : route.start + Vector2.up * sign * distance;

            while (i > 0 && !IsPointWithinCameraReveal(center))
                yield return new WaitForSecondsRealtime(revealPollInterval);

            Vector2 size = route.horizontal
                ? new Vector2(pieceLength + 0.06f, corridorWidth)
                : new Vector2(corridorWidth, pieceLength + 0.06f);

            Vector2 incoming = route.horizontal
                ? (i % 2 == 0 ? Vector2.down : Vector2.up)
                : (i % 2 == 0 ? Vector2.left : Vector2.right);

            GameObject piece = CreateVisibleCorridorPiece($"RoutePiece_{persistentRouteObjects.Count}", center, size, route.horizontal);
            MapBlock block = piece.GetComponent<MapBlock>();
            block.PlayEnter(piece.transform.position, incoming, i == 0 ? 0f : corridorPieceStagger);
            persistentRouteObjects.Add(piece);
        }

        Rect roomBounds = new(
            (Vector2)targetRoomOrigin - Vector2.one * 0.5f,
            layout.WorldSize);

        while (!IsRectWithinCameraReveal(roomBounds))
            yield return new WaitForSecondsRealtime(revealPollInterval);

        GameObject roomPiece = CreateVisibleRoomPiece(node, targetRoomOrigin, layout, travelDirection);
        persistentRouteObjects.Add(roomPiece);
    }

    private GameObject CreateVisibleCorridorPiece(string objectName, Vector2 destination, Vector2 size, bool horizontal)
    {
        GameObject root = new(objectName);
        root.transform.position = destination;

        GameObject floor = new("Floor");
        floor.transform.SetParent(root.transform, false);
        SpriteRenderer renderer = floor.AddComponent<SpriteRenderer>();
        renderer.sprite = SpatialRuntimeSpriteCache.Solid;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.size = size;
        renderer.color = new Color(0.20f, 0.23f, 0.27f, 1f);
        renderer.sortingOrder = -30;

        NavMeshModifier modifier = floor.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = false;
        modifier.overrideArea = false;

        CreateVisibleRail(root.transform,
            horizontal ? new Vector2(0f, size.y * 0.5f) : new Vector2(size.x * 0.5f, 0f),
            horizontal ? new Vector2(size.x, roomEdgeThickness) : new Vector2(roomEdgeThickness, size.y));
        CreateVisibleRail(root.transform,
            horizontal ? new Vector2(0f, -size.y * 0.5f) : new Vector2(-size.x * 0.5f, 0f),
            horizontal ? new Vector2(size.x, roomEdgeThickness) : new Vector2(roomEdgeThickness, size.y));

        MapBlock block = root.AddComponent<MapBlock>();
        block.ConfigureRuntimeDockingBlock(root.transform, true, 0.62f, corridorEntryDuration, corridorEntryOffset);
        return root;
    }

    private void CreateVisibleRail(Transform parent, Vector2 localPosition, Vector2 size)
    {
        GameObject rail = new("EdgeVisual");
        rail.transform.SetParent(parent, false);
        rail.transform.localPosition = localPosition;

        SpriteRenderer renderer = rail.AddComponent<SpriteRenderer>();
        renderer.sprite = SpatialRuntimeSpriteCache.Solid;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.size = size;
        renderer.color = roomEdgeColor;
        renderer.sortingOrder = 3;
    }

    private GameObject CreateVisibleRoomPiece(
        BattleNodeData node,
        Vector3 targetOrigin,
        ProceduralRoomLayout layout,
        Vector2 approachDirection)
    {
        GameObject root = new($"RoomPiece_{node.id}");
        root.transform.position = targetOrigin;

        foreach (Vector2Int cell in layout.cells)
        {
            GameObject floor = new($"Floor_{cell.x}_{cell.y}");
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = (Vector3)((Vector2)cell * RoomDefinitionSO.ProceduralTileWorldSize);

            SpriteRenderer renderer = floor.AddComponent<SpriteRenderer>();
            renderer.sprite = SpatialRuntimeSpriteCache.Solid;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.size = Vector2.one * 0.98f * RoomDefinitionSO.ProceduralTileWorldSize;
            renderer.color = roomFloorColor;
            renderer.sortingOrder = -20;

            NavMeshModifier modifier = floor.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = false;
            modifier.overrideArea = false;
        }

        BuildBoundaryEdges(root.transform, layout, GetDoorwayNormals(node, approachDirection), true, false);

        MapBlock block = root.AddComponent<MapBlock>();
        block.ConfigureRuntimeDockingBlock(
            root.transform,
            true,
            roomImpactStrength,
            node.room.largePieceEntryDuration,
            node.room.largePieceEntryOffset);

        Vector2 entry = node.room.largePieceEntryDirection.sqrMagnitude > 0.001f
            ? node.room.largePieceEntryDirection.normalized
            : Vector2.down;
        block.PlayEnter(targetOrigin, entry);
        return root;
    }

    private HashSet<Vector2Int> GetDoorwayNormals(BattleNodeData node, Vector2 approachDirection)
    {
        HashSet<Vector2Int> result = new();
        Vector2Int approach = DirectionToInt(-approachDirection);
        if (approach != Vector2Int.zero)
            result.Add(approach);

        if (node != null && graph != null)
        {
            Vector2Int current = ResolveNodeMapPosition(node);
            List<BattleNodeData> next = graph.GetNextNodes(node);
            for (int i = 0; i < next.Count; i++)
            {
                Vector2Int d = DirectionToInt(CardinalDirection(ResolveNodeMapPosition(next[i]) - current));
                if (d != Vector2Int.zero)
                    result.Add(d);
            }
        }

        return result;
    }

    private void BuildBoundaryEdges(
        Transform root,
        ProceduralRoomLayout layout,
        HashSet<Vector2Int> doorwayNormals,
        bool visible,
        bool logicalCollision)
    {
        Vector2Int[] directions = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        Dictionary<Vector2Int, Vector2Int> doorwayCells = new();

        if (doorwayNormals != null)
        {
            foreach (Vector2Int normal in doorwayNormals)
                doorwayCells[normal] = FindEntranceCell(layout.cells, layout.size, normal);
        }

        foreach (Vector2Int cell in layout.cells)
        {
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2Int edge = directions[i];
                if (layout.cells.Contains(cell + edge))
                    continue;

                if (doorwayCells.TryGetValue(edge, out Vector2Int doorCell) && doorCell == cell)
                    continue;

                CreateBoundaryEdge(root, cell, edge, visible, logicalCollision);
            }
        }
    }

    private void CreateBoundaryEdge(Transform root, Vector2Int cell, Vector2Int edge, bool visible, bool logicalCollision)
    {
        bool vertical = edge.x != 0;
        Vector2 cellCenter = (Vector2)cell * RoomDefinitionSO.ProceduralTileWorldSize;
        Vector2 position = cellCenter + (Vector2)edge * RoomDefinitionSO.ProceduralTileWorldSize * 0.5f;
        Vector2 size = vertical
            ? new Vector2(roomEdgeThickness, RoomDefinitionSO.ProceduralTileWorldSize + roomEdgeThickness)
            : new Vector2(RoomDefinitionSO.ProceduralTileWorldSize + roomEdgeThickness, roomEdgeThickness);

        GameObject edgeObject = new(visible ? "RoomEdgeVisual" : "RoomEdgeLogical");
        edgeObject.transform.SetParent(root, false);
        edgeObject.transform.localPosition = position;

        if (visible)
        {
            SpriteRenderer renderer = edgeObject.AddComponent<SpriteRenderer>();
            renderer.sprite = SpatialRuntimeSpriteCache.Solid;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.size = size;
            renderer.color = roomEdgeColor;
            renderer.sortingOrder = 2;
        }

        if (logicalCollision)
        {
            BoxCollider2D collider = edgeObject.AddComponent<BoxCollider2D>();
            collider.size = size;
            collider.isTrigger = false;

            NavMeshModifier modifier = edgeObject.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = false;
            modifier.overrideArea = true;
            modifier.area = 1;
        }
    }

    private static Vector2Int FindEntranceCell(HashSet<Vector2Int> cells, Vector2Int size, Vector2Int normal)
    {
        Vector2 center = new((size.x - 1) * 0.5f, (size.y - 1) * 0.5f);
        Vector2Int best = default;
        float bestScore = float.MaxValue;
        bool found = false;

        foreach (Vector2Int cell in cells)
        {
            if (cells.Contains(cell + normal))
                continue;

            bool onRequestedSide = normal == Vector2Int.right ? cell.x == size.x - 1 :
                                   normal == Vector2Int.left ? cell.x == 0 :
                                   normal == Vector2Int.up ? cell.y == size.y - 1 :
                                   normal == Vector2Int.down && cell.y == 0;
            if (!onRequestedSide)
                continue;

            float centerDistance = Vector2.Distance(cell, center);
            if (centerDistance < bestScore)
            {
                bestScore = centerDistance;
                best = cell;
                found = true;
            }
        }

        return found ? best : new Vector2Int(size.x / 2, size.y / 2);
    }

    private void SuppressLegacyRoomPiecesForCurrentEnter(RoomDefinitionSO room)
    {
        if (room == null)
            return;

        List<MapBlockPlacement> originalBlocks = room.blocks;
        bool originalReposition = room.repositionPlayerOnEnter;
        room.blocks = new List<MapBlockPlacement>();
        room.repositionPlayerOnEnter = false;
        StartCoroutine(RestoreRoomDataNextFrame(room, originalBlocks, originalReposition));
    }

    private IEnumerator RestoreRoomDataNextFrame(RoomDefinitionSO room, List<MapBlockPlacement> blocks, bool reposition)
    {
        yield return null;
        if (room == null)
            yield break;
        room.blocks = blocks ?? new List<MapBlockPlacement>();
        room.repositionPlayerOnEnter = reposition;
    }

    private void PlacePlayerAtRouteStart(Vector2 routeStart, Vector2 direction)
    {
        if (player == null)
            return;

        Vector2 position = routeStart - direction.normalized * 0.42f;
        player.transform.position = new Vector3(position.x, position.y, player.transform.position.z);
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
            body.linearVelocity = Vector2.zero;
    }

    private bool IsPointWithinCameraReveal(Vector2 point)
    {
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic)
            return true;

        Vector2 center = camera.transform.position;
        float halfH = camera.orthographicSize + revealMarginWorld;
        float halfW = camera.orthographicSize * camera.aspect + revealMarginWorld;
        return Mathf.Abs(point.x - center.x) <= halfW && Mathf.Abs(point.y - center.y) <= halfH;
    }

    private bool IsRectWithinCameraReveal(Rect rect)
    {
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic)
            return true;

        Vector2 center = camera.transform.position;
        float halfH = camera.orthographicSize + revealMarginWorld;
        float halfW = camera.orthographicSize * camera.aspect + revealMarginWorld;
        Rect cameraRect = new(center - new Vector2(halfW, halfH), new Vector2(halfW * 2f, halfH * 2f));
        return cameraRect.Overlaps(rect, true);
    }

    private void RefreshWorldDirectionMarkers()
    {
        ClearDirectionMarkers();
        if (runManager == null || graph == null || player == null)
            return;

        if (runManager.IsInStartArea)
        {
            BattleNodeData first = graph.GetStartNode();
            if (first == null || first.room == null)
                return;

            BuildResolvedLayout();
            Vector2 direction = CardinalDirection(ResolveNodeMapPosition(first));
            if (direction == Vector2.zero)
                direction = Vector2.right;

            Vector2 center = GetStartAreaCenter(first.room);
            Vector2 half = first.room.GetStartBaseWorldSize() * 0.5f;
            Vector2 markerPosition = EdgeMarkerPosition(center, half, direction);
            CreateDirectionMarker(markerPosition, direction, first, false, null);
            return;
        }

        if (!runManager.WaitingForNodeSelection || runManager.CurrentNode == null)
            return;

        BattleNodeData currentNode = runManager.CurrentNode;
        ProceduralRoomLayout currentLayout = GetOrCreateLayout(currentNode, Vector2.zero);
        Vector3 currentOrigin = MapPositionToWorld(ResolveNodeMapPosition(currentNode));
        Vector2 centerCurrent = (Vector2)currentOrigin + currentLayout.CenterOffset;
        Vector2 halfCurrent = currentLayout.WorldSize * 0.5f;

        IReadOnlyList<BattleNodeData> choices = runManager.NextNodeChoices;
        Vector2Int currentMap = ResolveNodeMapPosition(currentNode);
        for (int i = 0; i < choices.Count; i++)
        {
            BattleNodeData next = choices[i];
            if (next == null)
                continue;

            Vector2 direction = CardinalDirection(ResolveNodeMapPosition(next) - currentMap);
            if (direction == Vector2.zero)
                continue;

            Vector2 markerPosition = EdgeMarkerPosition(centerCurrent, halfCurrent, direction);
            CreateDirectionMarker(
                markerPosition,
                direction,
                next,
                next.type == BattleNodeType.Elite,
                () => runManager.SelectNextNode(next.id));
        }
    }

    private Vector2 GetStartAreaCenter(RoomDefinitionSO room)
    {
        ResolveStartOriginFromBase(room);
        return (Vector2)startOrigin + room.GetStartBaseCenterOffset();
    }

    private Vector2 EdgeMarkerPosition(Vector2 center, Vector2 half, Vector2 direction)
    {
        Vector2 position = center;
        if (Mathf.Abs(direction.x) > 0.5f)
            position.x += direction.x * Mathf.Max(0.1f, half.x - markerInset);
        else
            position.y += direction.y * Mathf.Max(0.1f, half.y - markerInset);
        return position;
    }

    private void CreateDirectionMarker(
        Vector2 worldPosition,
        Vector2 direction,
        BattleNodeData node,
        bool elite,
        Action onTriggered)
    {
        GameObject marker = new($"DirectionMarker_{node?.id ?? "Start"}");
        marker.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);

        SpriteRenderer renderer = marker.AddComponent<SpriteRenderer>();
        renderer.sprite = SpatialRuntimeSpriteCache.Arrow;
        renderer.color = elite ? markerEliteColor : markerAvailableColor;
        renderer.sortingOrder = 90;
        marker.transform.localScale = Vector3.one * markerWorldSize;
        marker.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);

        CircleCollider2D trigger = marker.AddComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = markerTriggerRadius / Mathf.Max(0.01f, markerWorldSize);

        SpatialDirectionMarkerTrigger markerTrigger = marker.AddComponent<SpatialDirectionMarkerTrigger>();
        markerTrigger.Arm(player.transform, onTriggered);
        directionMarkers.Add(marker);
    }

    private void ClearDirectionMarkers()
    {
        for (int i = 0; i < directionMarkers.Count; i++)
            if (directionMarkers[i] != null)
                Destroy(directionMarkers[i]);
        directionMarkers.Clear();
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
            if (persistentRouteObjects[i] != null)
                Destroy(persistentRouteObjects[i]);
        persistentRouteObjects.Clear();
        reservedRouteKeys.Clear();
    }

    private void ClearLogicalNavigation()
    {
        for (int i = 0; i < logicalNavigationObjects.Count; i++)
            if (logicalNavigationObjects[i] != null)
                Destroy(logicalNavigationObjects[i]);
        logicalNavigationObjects.Clear();
    }

    private void ApplyReadableDefaultCharacterSizes()
    {
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();

        if (player != null && player.spriteSO != null && player.spriteSO.name.StartsWith("TEST_", StringComparison.OrdinalIgnoreCase))
        {
            SpriteRenderer renderer = player.GetComponent<SpriteRenderer>();
            float scale = ResolveScaleForWorldHeight(renderer, testPlayerWorldHeight);
            player.transform.localScale = new Vector3(scale, scale, 1f);

            CircleCollider2D circle = player.GetComponent<CircleCollider2D>();
            if (circle != null)
                circle.radius = testPlayerWorldColliderRadius / Mathf.Max(0.01f, scale);
        }

        MonsterController[] monsters = FindObjectsByType<MonsterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < monsters.Length; i++)
        {
            MonsterController monster = monsters[i];
            if (monster == null || monster.Definition == null ||
                string.IsNullOrEmpty(monster.Definition.monsterId) ||
                !monster.Definition.monsterId.StartsWith("TEST_", StringComparison.OrdinalIgnoreCase))
                continue;

            SpriteRenderer renderer = monster.GetComponent<SpriteRenderer>();
            float scale = ResolveScaleForWorldHeight(renderer, testMonsterWorldHeight);
            monster.transform.localScale = new Vector3(scale, scale, 1f);

            CircleCollider2D circle = monster.GetComponent<CircleCollider2D>();
            if (circle != null)
                circle.radius = testMonsterWorldColliderRadius / Mathf.Max(0.01f, scale);

            NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
            if (agent != null)
                agent.radius = Mathf.Max(0.30f, testMonsterWorldColliderRadius * 0.8f);
        }
    }

    private static float ResolveScaleForWorldHeight(SpriteRenderer renderer, float targetHeight)
    {
        if (renderer == null || renderer.sprite == null)
            return Mathf.Max(0.1f, targetHeight);

        float spriteHeight = Mathf.Max(0.01f, renderer.sprite.bounds.size.y);
        return Mathf.Max(0.1f, targetHeight / spriteHeight);
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
                if (graph.nodes[i] != null)
                    allPositions.Add(ResolveNodeMapPosition(graph.nodes[i]));
        }

        Vector2 mapCenter = CalculateMapCenter(allPositions);
        float spacing = Mathf.Min(miniMapCellSpacing, CalculateMiniMapFitSpacing(allPositions));

        BattleNodeData startNode = graph.GetStartNode();
        if (startNode != null)
            DrawMiniMapLink(Vector2Int.zero, ResolveNodeMapPosition(startNode), mapCenter, spacing);

        if (graph.nodes != null)
        {
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                BattleNodeData node = graph.nodes[i];
                if (node == null)
                    continue;

                List<BattleNodeData> next = graph.GetNextNodes(node);
                for (int n = 0; n < next.Count; n++)
                    DrawMiniMapLink(ResolveNodeMapPosition(node), ResolveNodeMapPosition(next[n]), mapCenter, spacing);
            }
        }

        bool startCurrent = runManager != null && runManager.IsInStartArea;
        DrawMiniMapNode(Vector2Int.zero, startCurrent ? miniMapCurrent : miniMapVisited, mapCenter, spacing, 0.85f);

        HashSet<string> available = new();
        if (runManager != null)
        {
            IReadOnlyList<BattleNodeData> next = runManager.NextNodeChoices;
            for (int i = 0; i < next.Count; i++)
                if (next[i] != null)
                    available.Add(next[i].id);
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

                DrawMiniMapNode(ResolveNodeMapPosition(node), color, mapCenter, spacing,
                    node.type == BattleNodeType.Elite ? 1.18f : 1f);
            }
        }
    }

    private float CalculateMiniMapFitSpacing(List<Vector2Int> positions)
    {
        if (positions == null || positions.Count == 0)
            return miniMapCellSpacing;

        int minX = positions[0].x, maxX = positions[0].x;
        int minY = positions[0].y, maxY = positions[0].y;
        for (int i = 1; i < positions.Count; i++)
        {
            minX = Mathf.Min(minX, positions[i].x);
            maxX = Mathf.Max(maxX, positions[i].x);
            minY = Mathf.Min(minY, positions[i].y);
            maxY = Mathf.Max(maxY, positions[i].y);
        }

        float rangeX = Mathf.Max(1, maxX - minX);
        float rangeY = Mathf.Max(1, maxY - minY);
        return Mathf.Max(18f, Mathf.Min((miniMapSize.x - 42f) / rangeX, (miniMapSize.y - 42f) / rangeY));
    }

    private static Vector2 CalculateMapCenter(List<Vector2Int> positions)
    {
        if (positions == null || positions.Count == 0)
            return Vector2.zero;

        int minX = positions[0].x, maxX = positions[0].x;
        int minY = positions[0].y, maxY = positions[0].y;
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
        rect.anchoredPosition = new Vector2((mapPosition.x - mapCenter.x) * spacing, (mapPosition.y - mapCenter.y) * spacing);
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

    private readonly struct RouteGeometry
    {
        public readonly Vector2 start;
        public readonly Vector2 end;
        public readonly bool horizontal;
        public RouteGeometry(Vector2 start, Vector2 end, bool horizontal)
        {
            this.start = start;
            this.end = end;
            this.horizontal = horizontal;
        }
        public float Length => horizontal ? Mathf.Abs(end.x - start.x) : Mathf.Abs(end.y - start.y);
        public Vector2 Center => (start + end) * 0.5f;
    }

    private sealed class ProceduralRoomLayout
    {
        public readonly Vector2Int size;
        public readonly HashSet<Vector2Int> cells;
        public ProceduralRoomLayout(Vector2Int size, HashSet<Vector2Int> cells)
        {
            this.size = size;
            this.cells = cells ?? new HashSet<Vector2Int>();
        }
        public Vector2 WorldSize => new(size.x, size.y);
        public Vector2 CenterOffset => new((size.x - 1) * 0.5f, (size.y - 1) * 0.5f);
    }
}

[RequireComponent(typeof(Collider2D))]
internal sealed class SpatialDirectionMarkerTrigger : MonoBehaviour
{
    private Transform player;
    private Action onTriggered;
    private bool armed;

    public void Arm(Transform playerTarget, Action callback)
    {
        player = playerTarget;
        onTriggered = callback;
        armed = callback != null;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!armed || player == null || other == null)
            return;
        Transform t = other.transform;
        if (t != player && !t.IsChildOf(player))
            return;
        armed = false;
        Action callback = onTriggered;
        onTriggered = null;
        callback?.Invoke();
    }
}

internal static class SpatialRuntimeSpriteCache
{
    private static Sprite solid;
    private static Sprite arrow;

    public static Sprite Solid => solid != null ? solid : solid = CreateSolid();
    public static Sprite Arrow => arrow != null ? arrow : arrow = CreateArrow();

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

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 2f, 0, SpriteMeshType.FullRect);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        sprite.name = "SpatialRuntimeSolid";
        return sprite;
    }

    private static Sprite CreateArrow()
    {
        const int w = 12;
        const int h = 8;
        Texture2D texture = new(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool shaft = x <= 7 && y >= 3 && y <= 4;
                int dx = x - 7;
                bool head = x >= 6 && Mathf.Abs(y - 3.5f) <= (w - x) * 0.55f;
                texture.SetPixel(x, y, shaft || head ? Color.white : Color.clear);
            }
        }
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 12f, 0, SpriteMeshType.FullRect);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        sprite.name = "SpatialDirectionArrow";
        return sprite;
    }
}

#if UNITY_EDITOR
[UnityEditor.InitializeOnLoad]
internal static class BattleProceduralTestDefaultsEditor
{
    static BattleProceduralTestDefaultsEditor()
    {
        UnityEditor.EditorApplication.delayCall += Apply;
    }

    private static void Apply()
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        ConfigureRoom("Assets/Resources/BattleTestDefaults/TEST_Room_A.asset",
            RoomLargePieceShape.Irregular, new Vector2Int(6, 6), new Vector2Int(7, 7), 0.28f);
        ConfigureRoom("Assets/Resources/BattleTestDefaults/TEST_Room_B.asset",
            RoomLargePieceShape.LShape, new Vector2Int(7, 6), new Vector2Int(8, 7), 0.20f);
        ConfigureRoom("Assets/Resources/BattleTestDefaults/TEST_Room_ELITE.asset",
            RoomLargePieceShape.Cross, new Vector2Int(8, 8), new Vector2Int(9, 9), 0.12f);

        NodeGraphSO graph = UnityEditor.AssetDatabase.LoadAssetAtPath<NodeGraphSO>(
            "Assets/Resources/BattleTestDefaults/TEST_NodeGraph.asset");
        if (graph != null && graph.nodes != null)
        {
            Vector2Int[] positions = { Vector2Int.right, Vector2Int.right + Vector2Int.up, Vector2Int.right * 2 + Vector2Int.up };
            for (int i = 0; i < graph.nodes.Count && i < positions.Length; i++)
            {
                if (graph.nodes[i] == null) continue;
                graph.nodes[i].useExplicitMapPosition = true;
                graph.nodes[i].mapPosition = positions[i];
            }
            UnityEditor.EditorUtility.SetDirty(graph);
        }

        UnityEditor.AssetDatabase.SaveAssets();
    }

    private static void ConfigureRoom(
        string path,
        RoomLargePieceShape shape,
        Vector2Int min,
        Vector2Int max,
        float complexity)
    {
        RoomDefinitionSO room = UnityEditor.AssetDatabase.LoadAssetAtPath<RoomDefinitionSO>(path);
        if (room == null)
            return;

        room.startBaseTileSize = new Vector2Int(3, 3);
        room.useProceduralRoom = true;
        room.useLargeRoomPiece = true;
        room.largePieceShape = shape;
        room.proceduralMinTileSize = min;
        room.proceduralMaxTileSize = max;
        room.proceduralComplexity = complexity;
        room.repositionPlayerOnEnter = false;
        UnityEditor.EditorUtility.SetDirty(room);
    }
}
#endif

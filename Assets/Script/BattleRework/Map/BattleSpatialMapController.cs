using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NavMeshPlus.Components;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Stage-selection + procedural Room presentation.
///
/// Current rules:
/// - NO world bridge/corridor traversal.
/// - NO next-Room world placement or camera-range reveal.
/// - NodeGraph is presented as a Slay-the-Spire-style Stage Map.
/// - Available next nodes are clicked directly in the Stage Map UI.
/// - Selecting a Combat/Elite node builds that Room at the single battle origin.
/// - 32px = 1 tile = 1 world unit. All floor tile centers use integer tile coordinates.
/// - Start Base is handled independently by RoomBaseTemplate and is at least 3x3.
/// - Combat Room is at least 6x6. Rectangle is the default shape.
/// </summary>
[DefaultExecutionOrder(-20000)]
public sealed class BattleSpatialMapController : MonoBehaviour
{
    [Header("Procedural Room")]
    [SerializeField] private Color roomFloorColor = new(0.18f, 0.21f, 0.25f, 1f);
    [SerializeField] private Color roomEdgeColor = new(0.31f, 0.35f, 0.41f, 1f);
    [SerializeField, Min(0.03f)] private float roomEdgeThickness = 0.12f;
    [SerializeField, Range(0.1f, 1.5f)] private float roomImpactStrength = 0.95f;

    [Header("Stage Map")]
    [SerializeField] private Vector2 compactMapSize = new(240f, 190f);
    [SerializeField] private Vector2 selectionMapSize = new(560f, 390f);
    [SerializeField, Min(18f)] private float mapCellSpacing = 68f;
    [SerializeField, Min(12f)] private float mapNodeSize = 28f;
    [SerializeField] private Color mapBackground = new(0.035f, 0.05f, 0.075f, 0.94f);
    [SerializeField] private Color mapUnknown = new(0.24f, 0.28f, 0.34f, 0.96f);
    [SerializeField] private Color mapVisited = new(0.60f, 0.66f, 0.72f, 1f);
    [SerializeField] private Color mapCurrent = new(0.30f, 0.90f, 1f, 1f);
    [SerializeField] private Color mapAvailable = new(1f, 0.78f, 0.22f, 1f);
    [SerializeField] private Color mapElite = new(1f, 0.44f, 0.22f, 1f);
    [SerializeField] private Color mapLink = new(0.34f, 0.39f, 0.46f, 0.96f);

    [Header("32px Tile / 48px Character Test Scale")]
    [SerializeField, Min(0.5f)] private float testPlayerWorldHeight = 1.5f;
    [SerializeField, Min(0.1f)] private float testPlayerWorldColliderRadius = 0.60f;
    [SerializeField, Min(0.5f)] private float testMonsterWorldHeight = 1.5f;
    [SerializeField, Min(0.1f)] private float testMonsterWorldColliderRadius = 0.55f;

    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private PlayerController player;
    private NodeGraphSO graph;

    private readonly Dictionary<string, Vector2Int> resolvedMapPositions = new();
    private readonly Dictionary<string, ProceduralRoomLayout> roomLayouts = new();
    private readonly HashSet<string> visitedNodeIds = new();

    private Canvas stageMapCanvas;
    private RectTransform stageMapPanel;
    private Image stageMapPanelImage;
    private float nextCharacterSizingCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleSpatialMapController>() != null)
            return;

        GameObject host = new("BattleStageMapRuntime");
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
            ResolveSystems();
            if (runManager == null)
                yield return null;
        }

        Subscribe();
        EnsureStageMapUI();
        BuildResolvedLayout();
        RefreshStageMap();
    }

    private void ResolveSystems()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();

        if (runManager != null && graph == null)
        {
            FieldInfo field = typeof(BattleRunManager).GetField("nodeGraph", InstanceFields);
            graph = field != null ? field.GetValue(runManager) as NodeGraphSO : null;
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

    private void Update()
    {
        if (runManager == null || roomManager == null || graph == null)
        {
            ResolveSystems();
            if (runManager != null)
            {
                Subscribe();
                EnsureStageMapUI();
                BuildResolvedLayout();
            }
        }

        if (Time.unscaledTime >= nextCharacterSizingCheck)
        {
            nextCharacterSizingCheck = Time.unscaledTime + 0.35f;
            ApplyReadableDefaultCharacterSizes();
        }
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node == null)
            return;

        visitedNodeIds.Add(node.id);

        if ((node.type == BattleNodeType.Combat || node.type == BattleNodeType.Elite) && node.room != null)
            PrepareProceduralRoomPresentation(node);

        RefreshStageMap();
    }

    private void HandleStateChanged(BattleRunState _)
    {
        RefreshStageMap();
    }

    private void HandleNextNodeSelectionRequested(IReadOnlyList<BattleNodeData> _)
    {
        RefreshStageMap();
    }

    private void HandleRunEnded(RunEndReason _)
    {
        visitedNodeIds.Clear();
        roomLayouts.Clear();
        RefreshStageMap();
    }

    // ---------------------------------------------------------------------
    // Room generation
    // ---------------------------------------------------------------------

    private void PrepareProceduralRoomPresentation(BattleNodeData node)
    {
        RoomDefinitionSO room = node.room;
        ProceduralRoomLayout layout = GetOrCreateLayout(node);
        if (layout == null || layout.cells.Count == 0)
            return;

        MapBlock prototype = CreateRoomPrototype(node, layout);
        if (prototype == null)
            return;

        List<MapBlockPlacement> oldBlocks = room.blocks;
        bool oldReposition = room.repositionPlayerOnEnter;
        Vector2 oldEntry = room.playerEntryOffset;

        Vector2 entryDirection = room.largePieceEntryDirection.sqrMagnitude > 0.001f
            ? room.largePieceEntryDirection.normalized
            : Vector2.down;

        room.blocks = new List<MapBlockPlacement>
        {
            new()
            {
                prefab = prototype,
                gridPosition = Vector2Int.zero,
                entryDirection = entryDirection
            }
        };

        // Player always enters the integer center tile of the newly selected stage.
        room.repositionPlayerOnEnter = true;
        room.playerEntryOffset = layout.CenterTile;

        StartCoroutine(RestoreRoomDataNextFrame(room, oldBlocks, oldReposition, oldEntry, prototype.gameObject));
    }

    private IEnumerator RestoreRoomDataNextFrame(
        RoomDefinitionSO room,
        List<MapBlockPlacement> blocks,
        bool reposition,
        Vector2 entry,
        GameObject prototype)
    {
        yield return null;

        if (room != null)
        {
            room.blocks = blocks ?? new List<MapBlockPlacement>();
            room.repositionPlayerOnEnter = reposition;
            room.playerEntryOffset = entry;
        }

        if (prototype != null)
            Destroy(prototype);
    }

    private MapBlock CreateRoomPrototype(BattleNodeData node, ProceduralRoomLayout layout)
    {
        GameObject root = new($"__RuntimeRoomPrototype_{node.id}");
        root.transform.position = new Vector3(10000f, 10000f, 0f);

        foreach (Vector2Int cell in layout.cells)
        {
            GameObject floor = new($"Tile_{cell.x}_{cell.y}");
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(cell.x, cell.y, 0f);

            SpriteRenderer renderer = floor.AddComponent<SpriteRenderer>();
            renderer.sprite = SpatialRuntimeSpriteCache.FloorTile32;
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.color = roomFloorColor;
            renderer.sortingOrder = -20;

            NavMeshModifier modifier = floor.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = false;
            modifier.overrideArea = false;
        }

        BuildClosedBoundary(root.transform, layout);

        MapBlock block = root.AddComponent<MapBlock>();
        block.ConfigureRuntimeDockingBlock(
            root.transform,
            true,
            roomImpactStrength,
            node.room.largePieceEntryDuration,
            node.room.largePieceEntryOffset);
        return block;
    }

    private void BuildClosedBoundary(Transform root, ProceduralRoomLayout layout)
    {
        Vector2Int[] dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        foreach (Vector2Int cell in layout.cells)
        {
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector2Int edge = dirs[i];
                if (layout.cells.Contains(cell + edge))
                    continue;

                CreateBoundaryEdge(root, cell, edge);
            }
        }
    }

    private void CreateBoundaryEdge(Transform root, Vector2Int cell, Vector2Int edge)
    {
        bool vertical = edge.x != 0;
        Vector2 position = (Vector2)cell + (Vector2)edge * 0.5f;
        Vector2 size = vertical
            ? new Vector2(roomEdgeThickness, 1f + roomEdgeThickness)
            : new Vector2(1f + roomEdgeThickness, roomEdgeThickness);

        GameObject wall = new("RoomEdge");
        wall.transform.SetParent(root, false);
        wall.transform.localPosition = position;

        SpriteRenderer renderer = wall.AddComponent<SpriteRenderer>();
        renderer.sprite = SpatialRuntimeSpriteCache.Solid32;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.size = size;
        renderer.color = roomEdgeColor;
        renderer.sortingOrder = 3;

        BoxCollider2D collider = wall.AddComponent<BoxCollider2D>();
        collider.size = size;
        collider.isTrigger = false;

        NavMeshModifier modifier = wall.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = false;
        modifier.overrideArea = true;
        modifier.area = 1;
    }

    private ProceduralRoomLayout GetOrCreateLayout(BattleNodeData node)
    {
        if (node == null || node.room == null)
            return null;

        if (roomLayouts.TryGetValue(node.id, out ProceduralRoomLayout cached))
            return cached;

        ProceduralRoomLayout layout = GenerateLayout(node);
        roomLayouts[node.id] = layout;
        return layout;
    }

    private ProceduralRoomLayout GenerateLayout(BattleNodeData node)
    {
        RoomDefinitionSO room = node.room;
        Vector2Int min = room.GetProceduralMinTileSize();
        Vector2Int max = room.GetProceduralMaxTileSize();

        int seed = StableHash(node.id) ^ StableHash(room.roomId) ^ room.proceduralSeed;
        System.Random random = new(seed);
        Vector2Int size = room.useProceduralRoom
            ? new Vector2Int(random.Next(min.x, max.x + 1), random.Next(min.y, max.y + 1))
            : room.GetLargePieceGridSize();

        RoomLargePieceShape shape = room.largePieceShape;
        if (shape == RoomLargePieceShape.Auto)
            shape = RoomLargePieceShape.Rectangle;

        HashSet<Vector2Int> cells = BuildShape(room, size, shape, random);
        int chunk = room.GetMinimumRoomChunkTiles();

        if (cells.Count == 0 || !IsConnected(cells) || !EveryCellBelongsToChunk(cells, chunk))
            cells = BuildRectangle(size);

        return new ProceduralRoomLayout(size, cells);
    }

    private HashSet<Vector2Int> BuildShape(
        RoomDefinitionSO room,
        Vector2Int size,
        RoomLargePieceShape shape,
        System.Random random)
    {
        if (shape == RoomLargePieceShape.Custom)
        {
            HashSet<Vector2Int> custom = new();
            if (room.customLargePieceCells != null)
            {
                for (int i = 0; i < room.customLargePieceCells.Count; i++)
                {
                    Vector2Int c = room.customLargePieceCells[i];
                    if (c.x >= 0 && c.y >= 0 && c.x < size.x && c.y < size.y)
                        custom.Add(c);
                }
            }
            return custom;
        }

        if (shape == RoomLargePieceShape.Rectangle)
            return BuildRectangle(size);

        int chunk = Mathf.Min(room.GetMinimumRoomChunkTiles(), Mathf.Min(size.x, size.y));
        HashSet<Vector2Int> cells = new();

        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                bool include = shape switch
                {
                    RoomLargePieceShape.LShape => x < chunk || y < chunk,
                    RoomLargePieceShape.TShape => y >= size.y - chunk ||
                                                  (x >= (size.x - chunk) / 2 && x < (size.x - chunk) / 2 + chunk),
                    RoomLargePieceShape.Cross =>
                        (x >= (size.x - chunk) / 2 && x < (size.x - chunk) / 2 + chunk) ||
                        (y >= (size.y - chunk) / 2 && y < (size.y - chunk) / 2 + chunk),
                    RoomLargePieceShape.Irregular => true,
                    _ => true
                };

                if (include)
                    cells.Add(new Vector2Int(x, y));
            }
        }

        if (shape == RoomLargePieceShape.Irregular)
            ApplySafeCornerNotch(cells, size, chunk, room, random);

        return cells;
    }

    private static HashSet<Vector2Int> BuildRectangle(Vector2Int size)
    {
        HashSet<Vector2Int> cells = new();
        for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
                cells.Add(new Vector2Int(x, y));
        return cells;
    }

    private static void ApplySafeCornerNotch(
        HashSet<Vector2Int> cells,
        Vector2Int size,
        int chunk,
        RoomDefinitionSO room,
        System.Random random)
    {
        if (size.x < chunk + 2 || size.y < chunk + 2)
            return;

        double chance = Mathf.Clamp01(room.proceduralIndentChance + room.proceduralComplexity * 0.20f);
        if (random.NextDouble() > chance)
            return;

        int maxCutX = Mathf.Max(1, size.x - chunk);
        int maxCutY = Mathf.Max(1, size.y - chunk);
        int cutX = random.Next(1, maxCutX + 1);
        int cutY = random.Next(1, maxCutY + 1);
        int corner = random.Next(4);

        for (int y = 0; y < cutY; y++)
        {
            for (int x = 0; x < cutX; x++)
            {
                int px = (corner == 1 || corner == 3) ? size.x - 1 - x : x;
                int py = (corner >= 2) ? size.y - 1 - y : y;
                cells.Remove(new Vector2Int(px, py));
            }
        }
    }

    /// <summary>
    /// Prevents single-tile/2-tile appendages. Every positive tile must belong to at least one fully-filled
    /// chunk x chunk square (default 4x4).
    /// </summary>
    private static bool EveryCellBelongsToChunk(HashSet<Vector2Int> cells, int chunk)
    {
        if (cells == null || cells.Count == 0)
            return false;

        chunk = Mathf.Max(1, chunk);
        foreach (Vector2Int cell in cells)
        {
            bool belongs = false;
            for (int oy = cell.y - chunk + 1; oy <= cell.y && !belongs; oy++)
            {
                for (int ox = cell.x - chunk + 1; ox <= cell.x && !belongs; ox++)
                {
                    bool full = true;
                    for (int y = 0; y < chunk && full; y++)
                    {
                        for (int x = 0; x < chunk; x++)
                        {
                            if (!cells.Contains(new Vector2Int(ox + x, oy + y)))
                            {
                                full = false;
                                break;
                            }
                        }
                    }
                    if (full)
                        belongs = true;
                }
            }

            if (!belongs)
                return false;
        }
        return true;
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

    // ---------------------------------------------------------------------
    // Stage map UI
    // ---------------------------------------------------------------------

    private void EnsureStageMapUI()
    {
        if (stageMapCanvas != null)
            return;

        EnsureEventSystem();

        GameObject canvasObject = new("BattleStageMapCanvas");
        DontDestroyOnLoad(canvasObject);
        stageMapCanvas = canvasObject.AddComponent<Canvas>();
        stageMapCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        stageMapCanvas.sortingOrder = 650;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject panel = new("StageMapPanel");
        panel.transform.SetParent(canvasObject.transform, false);
        stageMapPanelImage = panel.AddComponent<Image>();
        stageMapPanelImage.color = mapBackground;
        stageMapPanel = panel.GetComponent<RectTransform>();
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject go = new("BattleStageMapEventSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    private void RefreshStageMap()
    {
        if (stageMapPanel == null || graph == null)
            return;

        bool selecting = runManager != null && runManager.WaitingForNodeSelection;
        ConfigureStageMapPanel(selecting);

        for (int i = stageMapPanel.childCount - 1; i >= 0; i--)
            Destroy(stageMapPanel.GetChild(i).gameObject);

        BuildResolvedLayout();

        if (selecting)
            CreateStageMapTitle();

        List<Vector2Int> positions = new();
        if (graph.nodes != null)
        {
            for (int i = 0; i < graph.nodes.Count; i++)
                if (graph.nodes[i] != null)
                    positions.Add(ResolveNodeMapPosition(graph.nodes[i]));
        }

        Vector2 mapCenter = CalculateMapCenter(positions);
        float spacing = CalculateMapSpacing(positions, selecting);

        // Links first, so nodes render over them.
        if (graph.nodes != null)
        {
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                BattleNodeData node = graph.nodes[i];
                if (node == null)
                    continue;

                List<BattleNodeData> next = graph.GetNextNodes(node);
                for (int n = 0; n < next.Count; n++)
                    DrawMapLink(ResolveNodeMapPosition(node), ResolveNodeMapPosition(next[n]), mapCenter, spacing);
            }
        }

        HashSet<string> available = new();
        if (runManager != null)
        {
            IReadOnlyList<BattleNodeData> choices = runManager.NextNodeChoices;
            for (int i = 0; i < choices.Count; i++)
                if (choices[i] != null)
                    available.Add(choices[i].id);
        }

        if (graph.nodes == null)
            return;

        for (int i = 0; i < graph.nodes.Count; i++)
        {
            BattleNodeData node = graph.nodes[i];
            if (node == null)
                continue;

            bool selectable = selecting && available.Contains(node.id);
            Color color = mapUnknown;
            if (runManager != null && runManager.CurrentNode == node)
                color = mapCurrent;
            else if (selectable)
                color = node.type == BattleNodeType.Elite ? mapElite : mapAvailable;
            else if (visitedNodeIds.Contains(node.id))
                color = mapVisited;

            DrawStageNode(node, ResolveNodeMapPosition(node), color, mapCenter, spacing, selectable, selecting);
        }
    }

    private void ConfigureStageMapPanel(bool selecting)
    {
        if (selecting)
        {
            stageMapPanel.anchorMin = stageMapPanel.anchorMax = new Vector2(0.5f, 0.5f);
            stageMapPanel.pivot = new Vector2(0.5f, 0.5f);
            stageMapPanel.anchoredPosition = Vector2.zero;
            stageMapPanel.sizeDelta = selectionMapSize;
            stageMapPanelImage.color = mapBackground;
        }
        else
        {
            stageMapPanel.anchorMin = stageMapPanel.anchorMax = Vector2.one;
            stageMapPanel.pivot = Vector2.one;
            stageMapPanel.anchoredPosition = new Vector2(-22f, -22f);
            stageMapPanel.sizeDelta = compactMapSize;
            Color c = mapBackground;
            c.a *= 0.80f;
            stageMapPanelImage.color = c;
        }
    }

    private void CreateStageMapTitle()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        GameObject title = new("Title");
        title.transform.SetParent(stageMapPanel, false);
        Text text = title.AddComponent<Text>();
        text.font = font;
        text.text = "SELECT NEXT STAGE";
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 22;
        text.color = Color.white;
        text.raycastTarget = false;

        RectTransform rect = title.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -12f);
        rect.sizeDelta = new Vector2(0f, 34f);
    }

    private void DrawStageNode(
        BattleNodeData node,
        Vector2Int position,
        Color color,
        Vector2 mapCenter,
        float spacing,
        bool selectable,
        bool expanded)
    {
        GameObject go = new($"StageNode_{node.id}");
        go.transform.SetParent(stageMapPanel, false);

        Image image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = selectable;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(
            (position.x - mapCenter.x) * spacing,
            (position.y - mapCenter.y) * spacing - (expanded ? 10f : 0f));
        float size = mapNodeSize * (node.type == BattleNodeType.Elite ? 1.18f : 1f);
        rect.sizeDelta = Vector2.one * size;

        if (selectable && runManager != null)
        {
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.82f, 0.86f, 0.92f, 1f);
            button.colors = colors;
            string id = node.id;
            button.onClick.AddListener(() => runManager.SelectNextNode(id));
        }

        if (expanded)
            AddNodeLabel(go.transform, node);
    }

    private static void AddNodeLabel(Transform parent, BattleNodeData node)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        GameObject label = new("Label");
        label.transform.SetParent(parent, false);
        Text text = label.AddComponent<Text>();
        text.font = font;
        text.text = node.type.ToString().ToUpperInvariant();
        text.fontSize = 10;
        text.alignment = TextAnchor.UpperCenter;
        text.color = Color.white;
        text.raycastTarget = false;

        RectTransform rect = label.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -5f);
        rect.sizeDelta = new Vector2(90f, 20f);
    }

    private void BuildResolvedLayout()
    {
        if (graph == null)
            return;

        resolvedMapPositions.Clear();
        HashSet<Vector2Int> occupied = new();

        if (graph.nodes != null)
        {
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                BattleNodeData node = graph.nodes[i];
                if (node == null || string.IsNullOrWhiteSpace(node.id) || !node.useExplicitMapPosition)
                    continue;

                Vector2Int pos = node.mapPosition;
                if (occupied.Add(pos))
                    resolvedMapPositions[node.id] = pos;
            }
        }

        BattleNodeData start = graph.GetStartNode();
        if (start == null)
            return;

        if (!resolvedMapPositions.ContainsKey(start.id))
        {
            Vector2Int p = FindFreeStagePosition(Vector2Int.zero, 0, occupied);
            resolvedMapPositions[start.id] = p;
            occupied.Add(p);
        }

        Queue<BattleNodeData> queue = new();
        HashSet<string> visited = new();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            BattleNodeData parent = queue.Dequeue();
            if (parent == null || !visited.Add(parent.id))
                continue;

            Vector2Int parentPos = ResolveNodeMapPosition(parent);
            List<BattleNodeData> next = graph.GetNextNodes(parent);
            for (int i = 0; i < next.Count; i++)
            {
                BattleNodeData child = next[i];
                if (child == null)
                    continue;

                if (!resolvedMapPositions.ContainsKey(child.id))
                {
                    Vector2Int candidate = FindFreeStagePosition(parentPos, i, occupied);
                    resolvedMapPositions[child.id] = candidate;
                    occupied.Add(candidate);
                }
                queue.Enqueue(child);
            }
        }
    }

    private static Vector2Int FindFreeStagePosition(Vector2Int parent, int siblingIndex, HashSet<Vector2Int> occupied)
    {
        int[] xOffsets = { 0, -1, 1, -2, 2, -3, 3 };
        for (int depthStep = 1; depthStep < 20; depthStep++)
        {
            for (int i = 0; i < xOffsets.Length; i++)
            {
                int index = (i + siblingIndex) % xOffsets.Length;
                Vector2Int candidate = new(parent.x + xOffsets[index], parent.y + depthStep);
                if (!occupied.Contains(candidate))
                    return candidate;
            }
        }
        return new Vector2Int(parent.x, parent.y + 20);
    }

    private Vector2Int ResolveNodeMapPosition(BattleNodeData node)
    {
        if (node == null)
            return Vector2Int.zero;
        if (resolvedMapPositions.TryGetValue(node.id, out Vector2Int pos))
            return pos;
        return node.useExplicitMapPosition ? node.mapPosition : Vector2Int.zero;
    }

    private float CalculateMapSpacing(List<Vector2Int> positions, bool expanded)
    {
        if (positions == null || positions.Count <= 1)
            return mapCellSpacing;

        int minX = positions[0].x, maxX = positions[0].x;
        int minY = positions[0].y, maxY = positions[0].y;
        for (int i = 1; i < positions.Count; i++)
        {
            minX = Mathf.Min(minX, positions[i].x);
            maxX = Mathf.Max(maxX, positions[i].x);
            minY = Mathf.Min(minY, positions[i].y);
            maxY = Mathf.Max(maxY, positions[i].y);
        }

        Vector2 panelSize = expanded ? selectionMapSize : compactMapSize;
        float rangeX = Mathf.Max(1, maxX - minX);
        float rangeY = Mathf.Max(1, maxY - minY);
        float fitX = (panelSize.x - 90f) / rangeX;
        float fitY = (panelSize.y - (expanded ? 110f : 55f)) / rangeY;
        return Mathf.Clamp(Mathf.Min(mapCellSpacing, fitX, fitY), 28f, mapCellSpacing);
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

    private void DrawMapLink(Vector2Int from, Vector2Int to, Vector2 center, float spacing)
    {
        Vector2 a = new((from.x - center.x) * spacing, (from.y - center.y) * spacing);
        Vector2 b = new((to.x - center.x) * spacing, (to.y - center.y) * spacing);
        Vector2 delta = b - a;
        float length = delta.magnitude;
        if (length < 1f)
            return;

        GameObject go = new("StageLink");
        go.transform.SetParent(stageMapPanel, false);
        Image image = go.AddComponent<Image>();
        image.color = mapLink;
        image.raycastTarget = false;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = (a + b) * 0.5f;
        rect.sizeDelta = new Vector2(length, 4f);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        go.transform.SetAsFirstSibling();
    }

    // ---------------------------------------------------------------------
    // Test character sizing
    // ---------------------------------------------------------------------

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
                agent.radius = Mathf.Max(0.35f, testMonsterWorldColliderRadius * 0.75f);
        }
    }

    private static float ResolveScaleForWorldHeight(SpriteRenderer renderer, float targetHeight)
    {
        if (renderer == null || renderer.sprite == null)
            return Mathf.Max(0.1f, targetHeight);

        float spriteHeight = Mathf.Max(0.01f, renderer.sprite.bounds.size.y);
        return Mathf.Max(0.1f, targetHeight / spriteHeight);
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

        public Vector2 CenterTile => new(size.x / 2, size.y / 2);
    }
}

internal static class SpatialRuntimeSpriteCache
{
    private static Sprite floorTile32;
    private static Sprite solid32;

    public static Sprite FloorTile32 => floorTile32 != null ? floorTile32 : floorTile32 = CreateFloorTile32();
    public static Sprite Solid32 => solid32 != null ? solid32 : solid32 = CreateSolid32();

    private static Sprite CreateFloorTile32()
    {
        const int pixels = 32;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color inner = Color.white;
        Color edge = new(0.82f, 0.84f, 0.88f, 1f);
        for (int y = 0; y < pixels; y++)
        {
            for (int x = 0; x < pixels; x++)
            {
                bool line = x == 0 || y == 0;
                texture.SetPixel(x, y, line ? edge : inner);
            }
        }
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, pixels, pixels),
            new Vector2(0.5f, 0.5f),
            pixels,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "RuntimeFloorTile_32px_1World";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static Sprite CreateSolid32()
    {
        const int pixels = 32;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] colors = new Color[pixels * pixels];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = Color.white;
        texture.SetPixels(colors);
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, pixels, pixels),
            new Vector2(0.5f, 0.5f),
            pixels,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "RuntimeSolid_32px_1World";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}

#if UNITY_EDITOR
[UnityEditor.InitializeOnLoad]
internal static class BattleStageSelectTestDefaultsEditor
{
    static BattleStageSelectTestDefaultsEditor()
    {
        UnityEditor.EditorApplication.delayCall += Apply;
    }

    private static void Apply()
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        ConfigureRoom("Assets/Resources/BattleTestDefaults/TEST_Room_A.asset", new Vector2Int(6, 6), new Vector2Int(7, 7));
        ConfigureRoom("Assets/Resources/BattleTestDefaults/TEST_Room_B.asset", new Vector2Int(7, 7), new Vector2Int(8, 8));
        ConfigureRoom("Assets/Resources/BattleTestDefaults/TEST_Room_ELITE.asset", new Vector2Int(8, 8), new Vector2Int(9, 9));

        UnityEditor.AssetDatabase.SaveAssets();
    }

    private static void ConfigureRoom(string path, Vector2Int min, Vector2Int max)
    {
        RoomDefinitionSO room = UnityEditor.AssetDatabase.LoadAssetAtPath<RoomDefinitionSO>(path);
        if (room == null)
            return;

        room.startBaseTileSize = new Vector2Int(3, 3);
        room.useProceduralRoom = true;
        room.useLargeRoomPiece = true;
        room.largePieceShape = RoomLargePieceShape.Rectangle;
        room.proceduralMinTileSize = min;
        room.proceduralMaxTileSize = max;
        room.proceduralMinChunkTileSize = 4;
        room.repositionPlayerOnEnter = true;
        UnityEditor.EditorUtility.SetDirty(room);
    }
}
#endif

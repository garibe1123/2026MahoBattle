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
/// Single owner of battle-field spatial planning and Stage Map presentation.
///
/// Spatial invariant:
/// - 32px = 1 tile = 1 world unit.
/// - The existing persistent 4x4 Base is INPUT, never an incoming Room product.
/// - Room generation creates the desired FINAL target mask around that Base.
/// - Incoming cells are calculated once as: extensionCells = targetCells - baseCells.
/// - Only extensionCells are split into MapBlock pieces. No central 4x4 floor prototype exists.
/// - Assembly order is planned from the real occupied frontier: Base -> attached piece -> next attached piece.
/// - Every traversable Room is 10x10+ and shape validation keeps the existing topology constraints.
///
/// BattleRoomManager only executes the already-planned pieces. It does not reinterpret Room geometry.
/// </summary>
[DefaultExecutionOrder(-20000)]
public sealed class BattleSpatialMapController : MonoBehaviour
{
    [Header("Procedural Room")]
    [SerializeField] private Color roomFloorColor = new(0.18f, 0.21f, 0.25f, 1f);
    [SerializeField] private Color roomEdgeColor = new(0.31f, 0.35f, 0.41f, 1f);
    [SerializeField, Min(0.03f)] private float roomEdgeThickness = 0.12f;
    [SerializeField, Range(0.1f, 1.5f)] private float roomImpactStrength = 0.95f;

    [Header("Extension Piece Assembly")]
    [Tooltip("Room topology size and incoming-piece size are independent. Incoming pieces may be as small as 1 tile.")]
    [SerializeField, Range(1, 4)] private int maximumPieceTileSpan = 4;
    [SerializeField, Range(12, 96)] private int maximumPieceCount = 48;

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

    private static readonly Vector2Int[] Cardinal =
    {
        Vector2Int.right,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.down
    };

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;
    private NodeGraphSO graph;

    private readonly Dictionary<string, Vector2Int> resolvedMapPositions = new();
    private readonly Dictionary<string, ProceduralRoomLayout> roomLayouts = new();
    private readonly HashSet<string> visitedNodeIds = new();

    private readonly HashSet<Vector2Int> currentTargetLocalTiles = new();
    private Vector2Int currentBaseWorldTile;

    private Canvas stageMapCanvas;
    private RectTransform stageMapPanel;
    private Image stageMapPanelImage;
    private float nextCharacterSizingCheck;

    public Vector3 CurrentBaseOriginWorld => baseTemplate != null
        ? baseTemplate.FixedTileOriginWorld
        : new Vector3(currentBaseWorldTile.x, currentBaseWorldTile.y, 0f);

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
        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
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
        if (runManager == null || roomManager == null || graph == null || baseTemplate == null)
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
        currentTargetLocalTiles.Clear();
        RefreshStageMap();
    }

    // ---------------------------------------------------------------------
    // Base-seeded Room generation
    // ---------------------------------------------------------------------

    private void PrepareProceduralRoomPresentation(BattleNodeData node)
    {
        ResolveSystems();
        RoomDefinitionSO room = node.room;
        if (room == null || roomManager == null || baseTemplate == null)
            return;

        baseTemplate.EnsurePersistentBase();
        Vector3 baseOriginWorld = baseTemplate.FixedTileOriginWorld;
        currentBaseWorldTile = WorldToTile(baseOriginWorld);
        SetRoomOrigin(baseOriginWorld);

        ProceduralRoomLayout layout = GetOrCreateLayout(node);
        if (layout == null || layout.targetCells.Count <= RoomBaseTemplate.FixedBaseTiles * RoomBaseTemplate.FixedBaseTiles)
            return;

        HashSet<Vector2Int> baseCells = CreateBaseCells();
        HashSet<Vector2Int> extensionCells = new(layout.targetCells);
        extensionCells.ExceptWith(baseCells);

        if (extensionCells.Count == 0)
        {
            Debug.LogError($"[BattleSpatial] Room '{room.roomId}' produced no extension cells outside the persistent 4x4 Base.");
            return;
        }

        int seed = StableHash(node.id) ^ StableHash(room.roomId) ^ room.proceduralSeed ^ 0x51A7;
        List<PiecePlan> plans = BuildFrontierAssemblyPlan(extensionCells, baseCells, seed);
        if (plans.Count == 0)
        {
            Debug.LogError($"[BattleSpatial] Room '{room.roomId}' could not build an extension plan from the 4x4 Base.");
            return;
        }

        List<MapBlockPlacement> oldBlocks = room.blocks;
        bool oldReposition = room.repositionPlayerOnEnter;
        Vector2 oldEntry = room.playerEntryOffset;

        List<MapBlockPlacement> runtimePlacements = new(plans.Count);
        List<GameObject> runtimePrototypes = new(plans.Count);

        for (int i = 0; i < plans.Count; i++)
        {
            PiecePlan plan = plans[i];
            MapBlock prototype = CreateExtensionPrototype(node, layout.targetCells, plan.cells, i);
            if (prototype == null)
                continue;

            runtimePrototypes.Add(prototype.gameObject);
            runtimePlacements.Add(new MapBlockPlacement
            {
                gridPosition = Vector2Int.zero,
                prefab = prototype,
                entryDirection = plan.entryDirection
            });
        }

        if (runtimePlacements.Count == 0)
        {
            DestroyPrototypeList(runtimePrototypes);
            return;
        }

        currentTargetLocalTiles.Clear();
        currentTargetLocalTiles.UnionWith(layout.targetCells);

        // Only extension prototypes exist. The real 4x4 Base stays untouched at local (0..3, 0..3).
        room.blocks = runtimePlacements;
        room.repositionPlayerOnEnter = false;
        room.playerEntryOffset = Vector2.zero;

        StartCoroutine(RestoreRoomDataNextFrame(
            room,
            oldBlocks,
            oldReposition,
            oldEntry,
            runtimePrototypes));
    }

    private void SetRoomOrigin(Vector3 baseOriginWorld)
    {
        if (roomManager == null || roomManager.RoomOrigin == null)
            return;

        Vector3 origin = baseOriginWorld;
        origin.z = roomManager.RoomOrigin.position.z;
        roomManager.RoomOrigin.position = origin;
    }

    private IEnumerator RestoreRoomDataNextFrame(
        RoomDefinitionSO room,
        List<MapBlockPlacement> blocks,
        bool reposition,
        Vector2 entry,
        List<GameObject> prototypes)
    {
        yield return null;

        if (room != null)
        {
            room.blocks = blocks ?? new List<MapBlockPlacement>();
            room.repositionPlayerOnEnter = reposition;
            room.playerEntryOffset = entry;
        }

        DestroyPrototypeList(prototypes);
    }

    private static void DestroyPrototypeList(List<GameObject> prototypes)
    {
        if (prototypes == null)
            return;

        for (int i = 0; i < prototypes.Count; i++)
        {
            if (prototypes[i] != null)
                Destroy(prototypes[i]);
        }
    }

    private ProceduralRoomLayout GetOrCreateLayout(BattleNodeData node)
    {
        if (node == null || node.room == null)
            return null;

        if (roomLayouts.TryGetValue(node.id, out ProceduralRoomLayout cached))
            return cached;

        ProceduralRoomLayout layout = GenerateBaseSeededLayout(node);
        roomLayouts[node.id] = layout;
        return layout;
    }

    private ProceduralRoomLayout GenerateBaseSeededLayout(BattleNodeData node)
    {
        RoomDefinitionSO room = node.room;
        Vector2Int min = room.GetProceduralMinTileSize();
        Vector2Int max = room.GetProceduralMaxTileSize();

        int seed = StableHash(node.id) ^ StableHash(room.roomId) ^ room.proceduralSeed;
        System.Random random = new(seed);

        Vector2Int size;
        if (room.useProceduralRoom)
        {
            size = new Vector2Int(
                ChooseEvenDimension(min.x, max.x, random),
                ChooseEvenDimension(min.y, max.y, random));
        }
        else
        {
            Vector2Int requested = room.GetLargePieceGridSize();
            size = new Vector2Int(
                Mathf.Max(RoomDefinitionSO.MinimumCombatRoomTiles, MakeEven(requested.x)),
                Mathf.Max(RoomDefinitionSO.MinimumCombatRoomTiles, MakeEven(requested.y)));
        }

        RoomLargePieceShape shape = room.largePieceShape == RoomLargePieceShape.Auto
            ? RoomLargePieceShape.Rectangle
            : room.largePieceShape;

        HashSet<Vector2Int> positiveTarget = BuildShape(room, size, shape, random);
        Vector2Int positiveBaseStart = new(
            (size.x - RoomBaseTemplate.FixedBaseTiles) / 2,
            (size.y - RoomBaseTemplate.FixedBaseTiles) / 2);

        // The Base is the seed. Every valid target MUST include all 16 Base cells before any extension is calculated.
        for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
        {
            for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                positiveTarget.Add(positiveBaseStart + new Vector2Int(x, y));
        }

        int chunk = room.GetMinimumRoomChunkTiles();
        if (positiveTarget.Count == 0 || !IsConnected(positiveTarget) || !EveryCellBelongsToChunk(positiveTarget, chunk))
        {
            positiveTarget = BuildRectangle(size);
        }

        HashSet<Vector2Int> targetLocal = new();
        foreach (Vector2Int cell in positiveTarget)
            targetLocal.Add(cell - positiveBaseStart);

        // A hard invariant, not a presentation fix: local 0..3 are exactly the already-existing Base.
        HashSet<Vector2Int> baseCells = CreateBaseCells();
        if (!baseCells.IsSubsetOf(targetLocal))
        {
            Debug.LogError($"[BattleSpatial] Generated target for '{room.roomId}' lost the persistent Base. Falling back to Rectangle.");
            targetLocal.Clear();
            foreach (Vector2Int cell in BuildRectangle(size))
                targetLocal.Add(cell - positiveBaseStart);
        }

        return new ProceduralRoomLayout(size, targetLocal);
    }

    private static int ChooseEvenDimension(int min, int max, System.Random random)
    {
        min = Mathf.Max(RoomDefinitionSO.MinimumCombatRoomTiles, min);
        max = Mathf.Max(min, max);

        int firstEven = (min & 1) == 0 ? min : min + 1;
        int lastEven = (max & 1) == 0 ? max : max - 1;
        if (firstEven > lastEven)
            return firstEven;

        int count = ((lastEven - firstEven) / 2) + 1;
        return firstEven + random.Next(count) * 2;
    }

    private static int MakeEven(int value)
    {
        value = Mathf.Max(1, value);
        return (value & 1) == 0 ? value : value + 1;
    }

    // ---------------------------------------------------------------------
    // Extension-only piece planning
    // ---------------------------------------------------------------------

    private List<PiecePlan> BuildFrontierAssemblyPlan(
        HashSet<Vector2Int> extensionCells,
        HashSet<Vector2Int> baseCells,
        int seed)
    {
        List<PiecePlan> result = new();
        if (extensionCells == null || extensionCells.Count == 0)
            return result;

        HashSet<Vector2Int> remaining = new(extensionCells);
        HashSet<Vector2Int> occupied = new(baseCells);
        System.Random random = new(seed);
        int maxSpan = Mathf.Clamp(maximumPieceTileSpan, 1, 4);
        int safety = 0;

        while (remaining.Count > 0 && result.Count < maximumPieceCount && safety++ < 1024)
        {
            List<Vector2Int> frontier = CollectFrontierCells(remaining, occupied);
            if (frontier.Count == 0)
            {
                Debug.LogError("[BattleSpatial] Extension target became disconnected from the existing 4x4 occupied floor.");
                break;
            }

            Vector2Int seedCell = frontier[random.Next(frontier.Count)];
            int targetCount = ChoosePieceCellCount(random, maxSpan);
            HashSet<Vector2Int> piece = GrowConnectedPiece(seedCell, remaining, targetCount, maxSpan, random);
            if (piece.Count == 0)
            {
                remaining.Remove(seedCell);
                continue;
            }

            Vector2 entryDirection = ResolveContactOutwardDirection(piece, occupied, result.Count);
            result.Add(new PiecePlan(piece, entryDirection));

            foreach (Vector2Int cell in piece)
            {
                remaining.Remove(cell);
                occupied.Add(cell);
            }
        }

        // Safety path: keep connectivity even if a highly irregular custom Room exceeds the piece-count cap.
        while (remaining.Count > 0 && safety++ < 4096)
        {
            List<Vector2Int> frontier = CollectFrontierCells(remaining, occupied);
            if (frontier.Count == 0)
                break;

            Vector2Int seedCell = frontier[0];
            HashSet<Vector2Int> piece = GrowConnectedPiece(seedCell, remaining, 16, 4, random);
            if (piece.Count == 0)
                piece.Add(seedCell);

            Vector2 entryDirection = ResolveContactOutwardDirection(piece, occupied, result.Count);
            result.Add(new PiecePlan(piece, entryDirection));
            foreach (Vector2Int cell in piece)
            {
                remaining.Remove(cell);
                occupied.Add(cell);
            }
        }

        return result;
    }

    private static List<Vector2Int> CollectFrontierCells(
        HashSet<Vector2Int> remaining,
        HashSet<Vector2Int> occupied)
    {
        List<Vector2Int> result = new();
        foreach (Vector2Int cell in remaining)
        {
            for (int i = 0; i < Cardinal.Length; i++)
            {
                if (!occupied.Contains(cell + Cardinal[i]))
                    continue;

                result.Add(cell);
                break;
            }
        }
        return result;
    }

    private static int ChoosePieceCellCount(System.Random random, int maxSpan)
    {
        int roll = random.Next(100);
        int span = roll < 14 ? 1 : roll < 58 ? 2 : roll < 84 ? 3 : 4;
        span = Mathf.Clamp(span, 1, maxSpan);
        return span * span;
    }

    private static HashSet<Vector2Int> GrowConnectedPiece(
        Vector2Int start,
        HashSet<Vector2Int> remaining,
        int targetCount,
        int maximumSpan,
        System.Random random)
    {
        HashSet<Vector2Int> piece = new();
        if (!remaining.Contains(start))
            return piece;

        List<Vector2Int> candidates = new() { start };
        HashSet<Vector2Int> queued = new() { start };
        int minX = start.x;
        int maxX = start.x;
        int minY = start.y;
        int maxY = start.y;

        while (candidates.Count > 0 && piece.Count < Mathf.Max(1, targetCount))
        {
            int candidateIndex = random.Next(candidates.Count);
            Vector2Int current = candidates[candidateIndex];
            candidates.RemoveAt(candidateIndex);

            int nextMinX = Mathf.Min(minX, current.x);
            int nextMaxX = Mathf.Max(maxX, current.x);
            int nextMinY = Mathf.Min(minY, current.y);
            int nextMaxY = Mathf.Max(maxY, current.y);
            if (nextMaxX - nextMinX + 1 > maximumSpan || nextMaxY - nextMinY + 1 > maximumSpan)
                continue;
            if (!remaining.Contains(current))
                continue;

            piece.Add(current);
            minX = nextMinX;
            maxX = nextMaxX;
            minY = nextMinY;
            maxY = nextMaxY;

            int rotation = random.Next(Cardinal.Length);
            for (int d = 0; d < Cardinal.Length; d++)
            {
                Vector2Int next = current + Cardinal[(d + rotation) % Cardinal.Length];
                if (remaining.Contains(next) && queued.Add(next))
                    candidates.Add(next);
            }
        }

        return piece;
    }

    private static Vector2 ResolveContactOutwardDirection(
        HashSet<Vector2Int> piece,
        HashSet<Vector2Int> occupied,
        int fallbackIndex)
    {
        Vector2 outward = Vector2.zero;
        int contacts = 0;

        foreach (Vector2Int cell in piece)
        {
            for (int i = 0; i < Cardinal.Length; i++)
            {
                Vector2Int towardOccupied = Cardinal[i];
                if (!occupied.Contains(cell + towardOccupied))
                    continue;

                outward += -(Vector2)towardOccupied;
                contacts++;
            }
        }

        if (contacts > 0 && outward.sqrMagnitude > 0.001f)
        {
            if (Mathf.Abs(outward.x) >= Mathf.Abs(outward.y))
                return outward.x >= 0f ? Vector2.right : Vector2.left;
            return outward.y >= 0f ? Vector2.up : Vector2.down;
        }

        Vector2 center = CalculateCenter(piece);
        Vector2 delta = center - new Vector2(1.5f, 1.5f);
        if (delta.sqrMagnitude > 0.001f)
        {
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
                return delta.x >= 0f ? Vector2.right : Vector2.left;
            return delta.y >= 0f ? Vector2.up : Vector2.down;
        }

        Vector2[] fallback = { Vector2.left, Vector2.right, Vector2.down, Vector2.up };
        return fallback[Mathf.Abs(fallbackIndex) % fallback.Length];
    }

    private MapBlock CreateExtensionPrototype(
        BattleNodeData node,
        HashSet<Vector2Int> fullTargetCells,
        HashSet<Vector2Int> pieceCells,
        int pieceIndex)
    {
        if (pieceCells == null || pieceCells.Count == 0)
            return null;

        GameObject root = new($"__RuntimeRoomPiecePrototype_{node.id}_{pieceIndex}");
        root.transform.position = new Vector3(10000f, 10000f, 0f);

        foreach (Vector2Int cell in pieceCells)
        {
            // There is intentionally no branch for Base cells here: pieceCells came from target - base.
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

        BuildOuterBoundaryForPiece(root.transform, fullTargetCells, pieceCells);

        MapBlock block = root.AddComponent<MapBlock>();
        block.ConfigureRuntimeDockingBlock(
            root.transform,
            true,
            roomImpactStrength,
            node.room.largePieceEntryDuration,
            node.room.largePieceEntryOffset);
        return block;
    }

    private void BuildOuterBoundaryForPiece(
        Transform root,
        HashSet<Vector2Int> fullTargetCells,
        HashSet<Vector2Int> pieceCells)
    {
        foreach (Vector2Int cell in pieceCells)
        {
            for (int i = 0; i < Cardinal.Length; i++)
            {
                Vector2Int edge = Cardinal[i];
                // Base cells are in fullTargetCells, so no seam wall is created against the persistent 4x4.
                if (fullTargetCells.Contains(cell + edge))
                    continue;

                CreateBoundaryEdge(root, cell, edge);
            }
        }
    }

    private void CreateBoundaryEdge(Transform root, Vector2Int cell, Vector2Int edge)
    {
        bool vertical = edge.x != 0;
        Vector2 size = vertical
            ? new Vector2(roomEdgeThickness, 1f + roomEdgeThickness)
            : new Vector2(1f + roomEdgeThickness, roomEdgeThickness);

        GameObject wall = new("RoomEdge");
        wall.transform.SetParent(root, false);
        wall.transform.localPosition = (Vector2)cell + (Vector2)edge * 0.5f;

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

    private static HashSet<Vector2Int> CreateBaseCells()
    {
        HashSet<Vector2Int> cells = new();
        for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
        {
            for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                cells.Add(new Vector2Int(x, y));
        }
        return cells;
    }

    private static Vector2 CalculateCenter(HashSet<Vector2Int> cells)
    {
        Vector2 sum = Vector2.zero;
        if (cells == null || cells.Count == 0)
            return sum;
        foreach (Vector2Int cell in cells)
            sum += (Vector2)cell;
        return sum / cells.Count;
    }

    private static Vector2Int WorldToTile(Vector3 world)
    {
        return new Vector2Int(
            Mathf.RoundToInt(world.x / RoomBaseTemplate.TileWorldSize),
            Mathf.RoundToInt(world.y / RoomBaseTemplate.TileWorldSize));
    }

    // ---------------------------------------------------------------------
    // Room topology
    // ---------------------------------------------------------------------

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
                int py = corner >= 2 ? size.y - 1 - y : y;
                cells.Remove(new Vector2Int(px, py));
            }
        }
    }

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
        foreach (Vector2Int cell in cells)
        {
            first = cell;
            break;
        }

        Queue<Vector2Int> queue = new();
        HashSet<Vector2Int> visited = new();
        queue.Enqueue(first);
        visited.Add(first);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            for (int i = 0; i < Cardinal.Length; i++)
            {
                Vector2Int next = current + Cardinal[i];
                if (cells.Contains(next) && visited.Add(next))
                    queue.Enqueue(next);
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
            {
                for (int i = 0; i < text.Length; i++)
                    hash = hash * 31 + text[i];
            }
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
        public readonly HashSet<Vector2Int> targetCells;

        public ProceduralRoomLayout(Vector2Int size, HashSet<Vector2Int> targetCells)
        {
            this.size = size;
            this.targetCells = targetCells ?? new HashSet<Vector2Int>();
        }
    }

    private sealed class PiecePlan
    {
        public readonly HashSet<Vector2Int> cells;
        public readonly Vector2 entryDirection;

        public PiecePlan(HashSet<Vector2Int> cells, Vector2 entryDirection)
        {
            this.cells = cells ?? new HashSet<Vector2Int>();
            this.entryDirection = entryDirection;
        }
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

        ConfigureRoom("Assets/Resources/BattleTestDefaults/TEST_Room_A.asset", new Vector2Int(10, 10), new Vector2Int(12, 12));
        ConfigureRoom("Assets/Resources/BattleTestDefaults/TEST_Room_B.asset", new Vector2Int(10, 10), new Vector2Int(14, 14));
        ConfigureRoom("Assets/Resources/BattleTestDefaults/TEST_Room_ELITE.asset", new Vector2Int(12, 12), new Vector2Int(14, 14));

        UnityEditor.AssetDatabase.SaveAssets();
    }

    private static void ConfigureRoom(string path, Vector2Int min, Vector2Int max)
    {
        RoomDefinitionSO room = UnityEditor.AssetDatabase.LoadAssetAtPath<RoomDefinitionSO>(path);
        if (room == null)
            return;

        room.startBaseTileSize = new Vector2Int(4, 4);
        room.useProceduralRoom = true;
        room.useLargeRoomPiece = true;
        room.largePieceShape = RoomLargePieceShape.Rectangle;
        room.proceduralMinTileSize = min;
        room.proceduralMaxTileSize = max;
        room.proceduralMinChunkTileSize = 4;
        room.repositionPlayerOnEnter = false;
        UnityEditor.EditorUtility.SetDirty(room);
    }
}
#endif

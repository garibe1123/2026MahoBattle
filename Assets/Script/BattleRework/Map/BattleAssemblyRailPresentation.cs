using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using NavMeshPlus.Components;
using UnityEngine;

/// <summary>
/// Base-centered presentation layer for procedural stage assembly.
///
/// Coordinate rule:
/// - Persistent 4x4 Base owns local tile coordinates (0,0) ~ (3,3).
/// - Every generated Room is translated into that coordinate space BEFORE it is split into pieces.
/// - Example 10x10 Room: X/Y = -3,-2,-1, 0,1,2,3, 4,5,6.
///   Therefore the existing 4x4 Base is physically at the middle and the Room expands around it.
///
/// Presentation rule:
/// - Room topology constraints and presentation-piece size are separate concerns.
/// - Incoming pieces may be 1x1 / 1x2 / 2x2 / 3x3 / 4x4-ish connected chunks.
/// - Pieces closer to the Base are assembled first and every piece travels on one cardinal rail axis.
/// - Clear exit uses the same cardinal, center-relative rail concept. No spin/tumble.
/// </summary>
[DefaultExecutionOrder(-19000)]
public sealed class BattleAssemblyRailPresentation : MonoBehaviour
{
    [Header("Incoming Piece Granularity")]
    [SerializeField, Min(1)] private int minimumPieceTileCount = 1;
    [SerializeField, Range(1, 4)] private int maximumPieceTileSpan = 4;
    [SerializeField, Range(16, 128)] private int maximumPieceCount = 64;

    [Header("Rail Exit")]
    [SerializeField] private Ease railExitEase = Ease.InCubic;
    [SerializeField] private bool disableLegacyExitSpin = true;

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo ActiveBlocksField =
        typeof(BattleRoomManager).GetField("activeBlocks", PrivateInstance);
    private static readonly FieldInfo TransitionSpinField =
        typeof(BattleStageTransitionController).GetField("exitGhostSpinDegrees", PrivateInstance);
    private static readonly FieldInfo TransitionScaleField =
        typeof(BattleStageTransitionController).GetField("exitGhostEndScale", PrivateInstance);
    private static readonly FieldInfo MapBlockExitEaseField =
        typeof(MapBlock).GetField("exitEase", PrivateInstance);

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private BattleSpatialMapController spatialMap;
    private RoomBaseTemplate baseTemplate;
    private BattleStageTransitionController stageTransition;
    private PlayerController player;
    private bool subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleAssemblyRailPresentation>() != null)
            return;

        GameObject host = new("BattleAssemblyRailPresentationRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleAssemblyRailPresentation>();
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
        while (runManager == null || spatialMap == null || roomManager == null || baseTemplate == null)
        {
            ResolveSystems();
            yield return null;
        }

        Subscribe();
        ApplyRailExitPresentationSettings();
    }

    private void Update()
    {
        ResolveSystems();
        ApplyRailExitPresentationSettings();
    }

    private void ResolveSystems()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (spatialMap == null)
            spatialMap = FindFirstObjectByType<BattleSpatialMapController>();
        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (stageTransition == null)
            stageTransition = FindFirstObjectByType<BattleStageTransitionController>();
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
    }

    private void Subscribe()
    {
        if (subscribed || runManager == null)
            return;

        runManager.NodeEntered += HandleNodeEntered;
        runManager.RewardSelectionRequested += HandleRewardSelectionRequested;
        runManager.RunEnded += HandleRunEnded;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || runManager == null)
            return;

        runManager.NodeEntered -= HandleNodeEntered;
        runManager.RewardSelectionRequested -= HandleRewardSelectionRequested;
        runManager.RunEnded -= HandleRunEnded;
        subscribed = false;
    }

    private void ApplyRailExitPresentationSettings()
    {
        if (!disableLegacyExitSpin || stageTransition == null)
            return;

        // Runtime zero intentionally means no tumble. The outgoing stage only slides away on rails.
        TransitionSpinField?.SetValue(stageTransition, 0f);
        TransitionScaleField?.SetValue(stageTransition, 1f);
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node == null || node.room == null)
            return;
        if (node.type != BattleNodeType.Combat && node.type != BattleNodeType.Elite)
            return;

        RewriteRuntimeRoomPieces(node);
    }

    /// <summary>
    /// BattleSpatialMapController already generated a Room in positive 0..N tile coordinates.
    /// This method immediately translates those tiles so the existing persistent Base becomes
    /// coordinates 0..3 and all other Room cells extend into negative/positive coordinates around it.
    /// </summary>
    private void RewriteRuntimeRoomPieces(BattleNodeData node)
    {
        RoomDefinitionSO room = node.room;
        if (room.blocks == null || room.blocks.Count == 0)
            return;

        Dictionary<Vector2Int, GameObject> originalSourceTiles = new();
        HashSet<Vector2Int> originalCells = new();
        WallStyle wallStyle = default;
        bool hasWallStyle = false;

        for (int i = 0; i < room.blocks.Count; i++)
        {
            MapBlockPlacement placement = room.blocks[i];
            if (placement == null || placement.prefab == null)
                continue;

            GameObject prototypeRoot = placement.prefab.gameObject;
            if (!prototypeRoot.name.StartsWith("__RuntimeRoomPiecePrototype_", StringComparison.Ordinal))
                continue;

            Transform[] children = prototypeRoot.GetComponentsInChildren<Transform>(true);
            for (int c = 0; c < children.Length; c++)
            {
                Transform child = children[c];
                if (child == null)
                    continue;

                if (child.name.StartsWith("Tile_", StringComparison.Ordinal))
                {
                    Vector2Int cell = new(
                        Mathf.RoundToInt(child.localPosition.x),
                        Mathf.RoundToInt(child.localPosition.y));

                    originalCells.Add(cell);
                    if (!originalSourceTiles.ContainsKey(cell))
                        originalSourceTiles[cell] = child.gameObject;
                }
                else if (!hasWallStyle && child.name.StartsWith("RoomEdge", StringComparison.Ordinal))
                {
                    SpriteRenderer wallRenderer = child.GetComponent<SpriteRenderer>();
                    BoxCollider2D wallCollider = child.GetComponent<BoxCollider2D>();
                    if (wallRenderer != null)
                    {
                        wallStyle = WallStyle.From(wallRenderer, wallCollider);
                        hasWallStyle = true;
                    }
                }
            }
        }

        if (originalCells.Count == 0 || originalSourceTiles.Count == 0)
            return;

        GetBounds(originalCells, out int minX, out int minY, out int maxX, out int maxY);
        int width = maxX - minX + 1;
        int height = maxY - minY + 1;

        // Find where a centered 4x4 would have lived in the old positive coordinate room.
        // Then translate THAT location to 0..3. This is the key that removes the lower-left anchoring bug.
        int oldBaseStartX = minX + Mathf.Max(0, (width - RoomBaseTemplate.FixedBaseTiles) / 2);
        int oldBaseStartY = minY + Mathf.Max(0, (height - RoomBaseTemplate.FixedBaseTiles) / 2);
        Vector2Int toBaseSpace = new(-oldBaseStartX, -oldBaseStartY);

        HashSet<Vector2Int> centeredFullCells = new();
        Dictionary<Vector2Int, GameObject> centeredSourceTiles = new();

        foreach (Vector2Int oldCell in originalCells)
        {
            Vector2Int centeredCell = oldCell + toBaseSpace;
            centeredFullCells.Add(centeredCell);

            if (originalSourceTiles.TryGetValue(oldCell, out GameObject source) && source != null)
                centeredSourceTiles[centeredCell] = source;
        }

        // Existing 4x4 Start Base is the actual center floor. Incoming tiles are only the outside cells.
        HashSet<Vector2Int> incomingCells = new(centeredFullCells);
        for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
        {
            for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                incomingCells.Remove(new Vector2Int(x, y));
        }

        if (incomingCells.Count == 0)
            return;

        // The Base center is always 1.5,1.5 in this coordinate system, regardless of Room size.
        Vector2 baseCenter = new(1.5f, 1.5f);

        int seed = StableHash(node.id) ^ StableHash(room.roomId) ^ room.proceduralSeed;
        List<HashSet<Vector2Int>> groups = BuildGranularGroups(incomingCells, baseCenter, seed);
        if (groups.Count == 0)
            return;

        if (!hasWallStyle)
            wallStyle = WallStyle.Fallback(centeredSourceTiles);

        List<MapBlockPlacement> rewrittenPlacements = new(groups.Count);
        List<GameObject> newPrototypes = new(groups.Count);

        for (int i = 0; i < groups.Count; i++)
        {
            HashSet<Vector2Int> group = groups[i];
            if (group == null || group.Count == 0)
                continue;

            MapBlock block = CreateGranularPrototype(
                node,
                group,
                centeredFullCells,
                centeredSourceTiles,
                wallStyle,
                i);

            if (block == null)
                continue;

            Vector2 pieceCenter = CalculateCenter(group);
            Vector2 outward = GetDominantOutwardDirection(pieceCenter - baseCenter, i);

            rewrittenPlacements.Add(new MapBlockPlacement
            {
                prefab = block,
                gridPosition = Vector2Int.zero,
                // PlayEnter starts OUTSIDE along this vector, then travels inward to its final Base-relative cell position.
                entryDirection = outward
            });
            newPrototypes.Add(block.gameObject);
        }

        if (rewrittenPlacements.Count == 0)
            return;

        room.blocks = rewrittenPlacements;
        StartCoroutine(DestroyOurPrototypesNextFrame(newPrototypes));
    }

    private List<HashSet<Vector2Int>> BuildGranularGroups(
        HashSet<Vector2Int> cells,
        Vector2 baseCenter,
        int seed)
    {
        List<HashSet<Vector2Int>> groups = new();
        if (cells == null || cells.Count == 0)
            return groups;

        HashSet<Vector2Int> remaining = new(cells);
        List<Vector2Int> ordered = new(cells);
        ordered.Sort((a, b) =>
        {
            float da = ((Vector2)a - baseCenter).sqrMagnitude;
            float db = ((Vector2)b - baseCenter).sqrMagnitude;
            int cmp = da.CompareTo(db); // Base-near cells dock first.
            if (cmp != 0) return cmp;
            cmp = a.y.CompareTo(b.y);
            return cmp != 0 ? cmp : a.x.CompareTo(b.x);
        });

        System.Random random = new(seed);
        int cursor = 0;

        while (remaining.Count > 0 && groups.Count < maximumPieceCount)
        {
            while (cursor < ordered.Count && !remaining.Contains(ordered[cursor]))
                cursor++;

            Vector2Int start;
            if (cursor < ordered.Count)
            {
                start = ordered[cursor];
            }
            else
            {
                start = default;
                foreach (Vector2Int cell in remaining)
                {
                    start = cell;
                    break;
                }
            }

            int targetCount = ChooseTargetPieceCellCount(random);
            HashSet<Vector2Int> group = GrowConnectedGroup(
                start,
                remaining,
                baseCenter,
                targetCount,
                Mathf.Clamp(maximumPieceTileSpan, 1, 4));

            if (group.Count == 0)
            {
                remaining.Remove(start);
                continue;
            }

            groups.Add(group);
        }

        // Safety-cap remainder is merged into the nearest piece. Normal 10~14 rooms should rarely reach this path.
        if (remaining.Count > 0)
        {
            foreach (Vector2Int cell in remaining)
            {
                int best = FindNearestGroup(groups, cell);
                if (best >= 0)
                    groups[best].Add(cell);
                else
                    groups.Add(new HashSet<Vector2Int> { cell });
            }
        }

        return groups;
    }

    private int ChooseTargetPieceCellCount(System.Random random)
    {
        int maxSpan = Mathf.Clamp(maximumPieceTileSpan, 1, 4);
        int roll = random.Next(100);

        int span;
        if (roll < 14) span = 1;
        else if (roll < 58) span = 2;
        else if (roll < 84) span = 3;
        else span = 4;

        span = Mathf.Clamp(span, 1, maxSpan);
        return Mathf.Max(minimumPieceTileCount, span * span);
    }

    private static HashSet<Vector2Int> GrowConnectedGroup(
        Vector2Int start,
        HashSet<Vector2Int> remaining,
        Vector2 baseCenter,
        int targetCount,
        int maximumSpan)
    {
        HashSet<Vector2Int> group = new();
        if (!remaining.Contains(start))
            return group;

        List<Vector2Int> frontier = new() { start };
        HashSet<Vector2Int> queued = new() { start };
        Vector2Int[] dirs =
        {
            Vector2Int.right,
            Vector2Int.left,
            Vector2Int.up,
            Vector2Int.down
        };

        int minX = start.x;
        int maxX = start.x;
        int minY = start.y;
        int maxY = start.y;

        while (frontier.Count > 0 && group.Count < Mathf.Max(1, targetCount))
        {
            frontier.Sort((a, b) =>
            {
                // Prefer approximately the same ring around the 4x4 Base so chunks read as attached layers.
                float ra = Mathf.Abs(((Vector2)a - baseCenter).magnitude - ((Vector2)start - baseCenter).magnitude);
                float rb = Mathf.Abs(((Vector2)b - baseCenter).magnitude - ((Vector2)start - baseCenter).magnitude);
                return ra.CompareTo(rb);
            });

            int acceptedIndex = -1;
            for (int i = 0; i < frontier.Count; i++)
            {
                Vector2Int candidate = frontier[i];
                int nextMinX = Mathf.Min(minX, candidate.x);
                int nextMaxX = Mathf.Max(maxX, candidate.x);
                int nextMinY = Mathf.Min(minY, candidate.y);
                int nextMaxY = Mathf.Max(maxY, candidate.y);

                if (nextMaxX - nextMinX + 1 <= maximumSpan &&
                    nextMaxY - nextMinY + 1 <= maximumSpan)
                {
                    acceptedIndex = i;
                    break;
                }
            }

            if (acceptedIndex < 0)
                break;

            Vector2Int current = frontier[acceptedIndex];
            frontier.RemoveAt(acceptedIndex);
            if (!remaining.Remove(current))
                continue;

            group.Add(current);
            minX = Mathf.Min(minX, current.x);
            maxX = Mathf.Max(maxX, current.x);
            minY = Mathf.Min(minY, current.y);
            maxY = Mathf.Max(maxY, current.y);

            for (int d = 0; d < dirs.Length; d++)
            {
                Vector2Int next = current + dirs[d];
                if (remaining.Contains(next) && queued.Add(next))
                    frontier.Add(next);
            }
        }

        return group;
    }

    private MapBlock CreateGranularPrototype(
        BattleNodeData node,
        HashSet<Vector2Int> group,
        HashSet<Vector2Int> centeredFullCells,
        Dictionary<Vector2Int, GameObject> centeredSourceTiles,
        WallStyle wallStyle,
        int index)
    {
        GameObject root = new($"__RuntimeRoomPiecePrototype_{node.id}_Rail_{index}");
        root.transform.position = new Vector3(10000f, 10000f, 0f);

        foreach (Vector2Int cell in group)
        {
            if (!centeredSourceTiles.TryGetValue(cell, out GameObject source) || source == null)
                continue;

            GameObject tile = Instantiate(source, root.transform);
            tile.name = $"Tile_{cell.x}_{cell.y}";
            tile.transform.localPosition = new Vector3(cell.x, cell.y, 0f);
            tile.transform.localRotation = Quaternion.identity;
        }

        BuildOuterWalls(root.transform, group, centeredFullCells, wallStyle);

        MapBlock block = root.AddComponent<MapBlock>();
        block.ConfigureRuntimeDockingBlock(
            root.transform,
            true,
            0.95f,
            node.room.largePieceEntryDuration,
            node.room.largePieceEntryOffset);

        MapBlockExitEaseField?.SetValue(block, railExitEase);
        return block;
    }

    private static void BuildOuterWalls(
        Transform root,
        HashSet<Vector2Int> group,
        HashSet<Vector2Int> fullCells,
        WallStyle style)
    {
        Vector2Int[] dirs =
        {
            Vector2Int.right,
            Vector2Int.left,
            Vector2Int.up,
            Vector2Int.down
        };

        foreach (Vector2Int cell in group)
        {
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector2Int edge = dirs[i];
                if (fullCells.Contains(cell + edge))
                    continue;

                CreateWall(root, cell, edge, style);
            }
        }
    }

    private static void CreateWall(Transform root, Vector2Int cell, Vector2Int edge, WallStyle style)
    {
        bool vertical = edge.x != 0;
        float thickness = Mathf.Max(0.03f, style.thickness);
        Vector2 size = vertical
            ? new Vector2(thickness, 1f + thickness)
            : new Vector2(1f + thickness, thickness);

        GameObject wall = new("RoomEdge");
        wall.transform.SetParent(root, false);
        wall.transform.localPosition = (Vector2)cell + (Vector2)edge * 0.5f;

        SpriteRenderer renderer = wall.AddComponent<SpriteRenderer>();
        renderer.sprite = style.sprite;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.size = size;
        renderer.color = style.color;
        renderer.sortingOrder = style.sortingOrder;
        if (style.material != null)
            renderer.sharedMaterial = style.material;

        BoxCollider2D collider = wall.AddComponent<BoxCollider2D>();
        collider.size = size;
        collider.isTrigger = false;

        NavMeshModifier modifier = wall.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = false;
        modifier.overrideArea = true;
        modifier.area = 1;
    }

    private void HandleRewardSelectionRequested(IReadOnlyList<BattleEquipmentSO> _)
    {
        // StageTransition calculates outgoing direction from each MapBlock root to the player.
        // Projecting that root onto a single dominant axis makes the resulting exit strictly rail-like.
        PrepareActiveBlockRootsForCardinalRailExit();
        ApplyRailExitPresentationSettings();
    }

    private void PrepareActiveBlockRootsForCardinalRailExit()
    {
        if (roomManager == null || ActiveBlocksField == null)
            return;
        if (ActiveBlocksField.GetValue(roomManager) is not List<MapBlock> blocks)
            return;

        Vector2 exitCenter;
        if (player != null)
            exitCenter = player.transform.position;
        else if (baseTemplate != null)
            exitCenter = baseTemplate.FixedCenterWorld;
        else
            exitCenter = roomManager.RoomOrigin != null ? roomManager.RoomOrigin.position : Vector2.zero;

        for (int i = 0; i < blocks.Count; i++)
        {
            MapBlock block = blocks[i];
            if (block == null)
                continue;
            if (!TryGetRendererBounds(block.gameObject, out Bounds bounds))
                continue;

            Vector2 actualCenter = bounds.center;
            Vector2 outward = GetDominantOutwardDirection(actualCenter - exitCenter, i);
            float axialDistance = Mathf.Abs(outward.x) > 0.5f
                ? Mathf.Abs(actualCenter.x - exitCenter.x)
                : Mathf.Abs(actualCenter.y - exitCenter.y);
            axialDistance = Mathf.Max(0.5f, axialDistance);

            Vector3 desiredRootPosition = (Vector3)(exitCenter + outward * axialDistance);
            desiredRootPosition.z = block.transform.position.z;
            RecenterRootWithoutMovingChildren(block.transform, desiredRootPosition);
            MapBlockExitEaseField?.SetValue(block, railExitEase);
        }
    }

    private static void RecenterRootWithoutMovingChildren(Transform root, Vector3 desiredRootPosition)
    {
        if (root == null)
            return;

        int childCount = root.childCount;
        Vector3[] worldPositions = new Vector3[childCount];
        Quaternion[] worldRotations = new Quaternion[childCount];

        for (int i = 0; i < childCount; i++)
        {
            Transform child = root.GetChild(i);
            worldPositions[i] = child.position;
            worldRotations[i] = child.rotation;
        }

        root.position = desiredRootPosition;

        for (int i = 0; i < childCount; i++)
        {
            Transform child = root.GetChild(i);
            child.position = worldPositions[i];
            child.rotation = worldRotations[i];
        }
    }

    private void HandleRunEnded(RunEndReason _)
    {
    }

    private IEnumerator DestroyOurPrototypesNextFrame(List<GameObject> prototypes)
    {
        yield return null;
        if (prototypes == null)
            yield break;

        for (int i = 0; i < prototypes.Count; i++)
        {
            if (prototypes[i] != null)
                Destroy(prototypes[i]);
        }
    }

    private static int FindNearestGroup(List<HashSet<Vector2Int>> groups, Vector2Int cell)
    {
        if (groups == null || groups.Count == 0)
            return -1;

        int bestIndex = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i] == null || groups[i].Count == 0)
                continue;

            Vector2 center = CalculateCenter(groups[i]);
            float distance = ((Vector2)cell - center).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
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

    private static Vector2 GetDominantOutwardDirection(Vector2 delta, int fallbackIndex)
    {
        if (delta.sqrMagnitude < 0.0001f)
        {
            Vector2[] fallback = { Vector2.left, Vector2.right, Vector2.down, Vector2.up };
            return fallback[Mathf.Abs(fallbackIndex) % fallback.Length];
        }

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            return delta.x >= 0f ? Vector2.right : Vector2.left;
        return delta.y >= 0f ? Vector2.up : Vector2.down;
    }

    private static void GetBounds(
        HashSet<Vector2Int> cells,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        minX = int.MaxValue;
        minY = int.MaxValue;
        maxX = int.MinValue;
        maxY = int.MinValue;

        foreach (Vector2Int cell in cells)
        {
            minX = Mathf.Min(minX, cell.x);
            minY = Mathf.Min(minY, cell.y);
            maxX = Mathf.Max(maxX, cell.x);
            maxY = Mathf.Max(maxY, cell.y);
        }
    }

    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
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

    private readonly struct WallStyle
    {
        public readonly Sprite sprite;
        public readonly Material material;
        public readonly Color color;
        public readonly int sortingOrder;
        public readonly float thickness;

        public WallStyle(Sprite sprite, Material material, Color color, int sortingOrder, float thickness)
        {
            this.sprite = sprite;
            this.material = material;
            this.color = color;
            this.sortingOrder = sortingOrder;
            this.thickness = thickness;
        }

        public static WallStyle From(SpriteRenderer renderer, BoxCollider2D collider)
        {
            float thickness = 0.12f;
            if (collider != null)
                thickness = Mathf.Min(Mathf.Abs(collider.size.x), Mathf.Abs(collider.size.y));
            else if (renderer != null)
                thickness = Mathf.Min(Mathf.Abs(renderer.size.x), Mathf.Abs(renderer.size.y));

            return new WallStyle(
                renderer != null ? renderer.sprite : null,
                renderer != null ? renderer.sharedMaterial : null,
                renderer != null ? renderer.color : new Color(0.31f, 0.35f, 0.41f, 1f),
                renderer != null ? renderer.sortingOrder : 3,
                Mathf.Max(0.03f, thickness));
        }

        public static WallStyle Fallback(Dictionary<Vector2Int, GameObject> tiles)
        {
            Sprite sprite = null;
            Material material = null;
            if (tiles != null)
            {
                foreach (KeyValuePair<Vector2Int, GameObject> pair in tiles)
                {
                    if (pair.Value == null)
                        continue;

                    SpriteRenderer renderer = pair.Value.GetComponent<SpriteRenderer>();
                    if (renderer == null)
                        continue;

                    sprite = renderer.sprite;
                    material = renderer.sharedMaterial;
                    break;
                }
            }

            return new WallStyle(
                sprite,
                material,
                new Color(0.31f, 0.35f, 0.41f, 1f),
                3,
                0.12f);
        }
    }
}

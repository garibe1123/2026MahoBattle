using NavMeshPlus.Components;
using UnityEngine;

/// <summary>
/// Mandatory persistent 4x4 battle anchor.
///
/// Rules:
/// - 32px = 1 tile = 1 world unit.
/// - The persistent base is ALWAYS exactly 4x4 tiles.
/// - The first Start Base uses the same 4x4 rule as later stage-transition bases.
/// - The base is not optional per RoomDefinitionSO. It must exist before any generated Room
///   is allowed to reserve / hide its center cells.
/// - On stage clear the base can be re-anchored around the player's current tile.
/// - Gameplay Room pieces attach around / over this anchor; the base itself never exits.
/// </summary>
public class RoomBaseTemplate : MonoBehaviour
{
    public const int FixedBaseTiles = 4;
    public const float TileWorldSize = 1f;

    [Header("Systems")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRoomManager roomManager;

    [Header("Base Transform")]
    [SerializeField] private Transform baseOrigin;
    [SerializeField] private Transform baseRoot;

    [Header("Persistent Start Base")]
    [SerializeField] private bool keepAcrossRooms = true;
    [SerializeField] private bool baseProvidesWalkableNavMesh = true;

    [Header("Real Base Visual - both null = runtime dummy")]
    [SerializeField] private GameObject basePrefab;
    [SerializeField] private Sprite baseSprite;
    [SerializeField] private Material baseMaterial;
    [SerializeField] private int sortingOrder = -19;

    [Header("Sizing")]
    [SerializeField] private bool tileSpriteToTemplate = true;
    [SerializeField] private bool scalePrefabToTemplate = true;

    [Header("Dummy Base")]
    [SerializeField] private Color dummyBaseColor = new(0.18f, 0.21f, 0.25f, 1f);

    private GameObject activeBase;
    private RoomDefinitionSO activeRoom;
    private Vector2 activeWorldSize = new(FixedBaseTiles, FixedBaseTiles);
    private bool subscribed;
    private bool hasRuntimeAnchor;
    private Vector3 runtimeTileOrigin;

    public GameObject ActiveBase => activeBase;
    public RoomDefinitionSO ActiveRoom => activeRoom;
    public Vector2 ActiveWorldSize => activeWorldSize;
    public bool HasPersistentBase => activeBase != null && activeBase.activeInHierarchy;
    public Vector3 FixedTileOriginWorld => ResolveTileOriginWorld();
    public Vector3 FixedCenterWorld => ResolveTileOriginWorld() + new Vector3(1.5f, 1.5f, 0f);

    private void Awake()
    {
        ResolveSystems();
        ResolveOrigin();
        EnsurePersistentBase();
    }

    private void OnEnable()
    {
        ResolveSystems();
        ResolveOrigin();
        EnsurePersistentBase();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void ResolveSystems()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
    }

    private void ResolveOrigin()
    {
        if (baseOrigin != null)
            return;

        if (roomManager != null)
        {
            Transform named = roomManager.RoomOrigin;
            baseOrigin = named != null ? named : roomManager.transform;
        }
        else
        {
            baseOrigin = transform;
        }
    }

    private void Subscribe()
    {
        if (subscribed)
            return;

        if (runManager != null)
            runManager.NodeEntered += HandleNodeEntered;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (runManager != null)
            runManager.NodeEntered -= HandleNodeEntered;
        subscribed = false;
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node != null && node.room != null)
            activeRoom = node.room;

        // Persistent base is a scene invariant, not a Room option.
        EnsurePersistentBase();
        MoveExistingBaseToResolvedAnchor();
        EnsureVisibleBase();
        EnsureWalkableBaseSource();
    }

    public void BuildBase(RoomDefinitionSO room)
    {
        BuildBaseInternal(room, false);
    }

    /// <summary>
    /// Guarantees that a visible, walkable 4x4 base exists even when no RoomDefinitionSO is active yet.
    /// </summary>
    public bool EnsurePersistentBase()
    {
        if (activeBase == null)
            BuildBaseInternal(activeRoom, false);
        else
        {
            ApplyExact4x4Sizing(activeBase);
            MoveExistingBaseToResolvedAnchor();
            EnsureVisibleBase();
            EnsureWalkableBaseSource();
        }

        return HasPersistentBase;
    }

    /// <summary>
    /// Used by stage presentation code before it suppresses duplicated generated center-floor gameplay sources.
    /// </summary>
    public bool EnsureVisibleAtTileOrigin(Vector3 lowerLeftTileCenterWorld, RoomDefinitionSO room = null)
    {
        if (room != null)
            activeRoom = room;

        runtimeTileOrigin = new Vector3(
            Mathf.Round(lowerLeftTileCenterWorld.x / TileWorldSize) * TileWorldSize,
            Mathf.Round(lowerLeftTileCenterWorld.y / TileWorldSize) * TileWorldSize,
            ResolveZ());
        hasRuntimeAnchor = true;

        BuildBaseInternal(activeRoom, activeBase == null);
        MoveExistingBaseToResolvedAnchor();
        EnsureVisibleBase();
        EnsureWalkableBaseSource();
        return HasPersistentBase;
    }

    [ContextMenu("Rebuild Current 4x4 Base")]
    public void RebuildCurrentBase()
    {
        RoomDefinitionSO room = activeRoom;
        if (room == null && runManager != null && runManager.CurrentNode != null)
            room = runManager.CurrentNode.room;
        if (room == null && roomManager != null && roomManager.CurrentRoom != null)
            room = roomManager.CurrentRoom;

        BuildBaseInternal(room, true);
    }

    /// <summary>
    /// Promotes the 4x4 tile area around the player's current tile into the next persistent base.
    /// Returns the world position of the lower-left tile CENTER of that 4x4 base.
    /// </summary>
    public Vector3 ReanchorAroundPlayer(Vector3 playerWorldPosition)
    {
        ResolveOrigin();

        float tileX = Mathf.Round(playerWorldPosition.x / TileWorldSize) * TileWorldSize;
        float tileY = Mathf.Round(playerWorldPosition.y / TileWorldSize) * TileWorldSize;

        // Even-sized 4x4 base: keep the player's nearest tile in the inner 2x2 area.
        runtimeTileOrigin = new Vector3(tileX - 1f, tileY - 1f, ResolveZ());
        hasRuntimeAnchor = true;

        RoomDefinitionSO room = activeRoom;
        if (room == null && roomManager != null)
            room = roomManager.CurrentRoom;
        if (room == null && runManager != null && runManager.CurrentNode != null)
            room = runManager.CurrentNode.room;
        if (room != null)
            activeRoom = room;

        BuildBaseInternal(activeRoom, true);
        return runtimeTileOrigin;
    }

    public Vector3 ReanchorToTileOrigin(Vector3 lowerLeftTileCenterWorld)
    {
        runtimeTileOrigin = new Vector3(
            Mathf.Round(lowerLeftTileCenterWorld.x / TileWorldSize) * TileWorldSize,
            Mathf.Round(lowerLeftTileCenterWorld.y / TileWorldSize) * TileWorldSize,
            ResolveZ());
        hasRuntimeAnchor = true;

        BuildBaseInternal(activeRoom, activeBase == null);
        MoveExistingBaseToResolvedAnchor();
        EnsureVisibleBase();
        EnsureWalkableBaseSource();
        return runtimeTileOrigin;
    }

    private void BuildBaseInternal(RoomDefinitionSO room, bool forceRebuild)
    {
        if (room != null)
            activeRoom = room;

        activeWorldSize = new Vector2(FixedBaseTiles, FixedBaseTiles);

        if (activeBase != null && keepAcrossRooms && !forceRebuild)
        {
            ApplyExact4x4Sizing(activeBase);
            MoveExistingBaseToResolvedAnchor();
            EnsureVisibleBase();
            EnsureWalkableBaseSource();
            return;
        }

        if (activeBase != null)
            DestroyBaseObject();

        ResolveOrigin();
        Vector3 center = FixedCenterWorld;
        Transform parent = baseRoot != null
            ? baseRoot
            : (roomManager != null ? roomManager.transform : transform);

        if (basePrefab != null)
            BuildPrefabBase(parent, center, activeWorldSize);
        else
            BuildSpriteBase(parent, center, activeWorldSize);

        EnsureVisibleBase();
        EnsureWalkableBaseSource();
    }

    [ContextMenu("Clear Start Base")]
    public void ClearBase()
    {
        DestroyBaseObject();
        activeBase = null;
        activeRoom = null;
        activeWorldSize = new Vector2(FixedBaseTiles, FixedBaseTiles);
        hasRuntimeAnchor = false;
        runtimeTileOrigin = Vector3.zero;
    }

    private void DestroyBaseObject()
    {
        if (activeBase == null)
            return;

        if (Application.isPlaying)
            Destroy(activeBase);
        else
            DestroyImmediate(activeBase);
        activeBase = null;
    }

    private Vector3 ResolveTileOriginWorld()
    {
        if (hasRuntimeAnchor)
            return runtimeTileOrigin;

        ResolveOrigin();
        Vector3 origin = baseOrigin != null ? baseOrigin.position : transform.position;
        return new Vector3(
            Mathf.Round(origin.x / TileWorldSize) * TileWorldSize,
            Mathf.Round(origin.y / TileWorldSize) * TileWorldSize,
            origin.z);
    }

    private float ResolveZ()
    {
        if (baseOrigin != null)
            return baseOrigin.position.z;
        if (activeBase != null)
            return activeBase.transform.position.z;
        return transform.position.z;
    }

    private void MoveExistingBaseToResolvedAnchor()
    {
        if (activeBase == null)
            return;

        activeBase.transform.position = FixedCenterWorld;
        ApplyExact4x4Sizing(activeBase);
        RefreshSupportCollider(activeBase);
    }

    private void EnsureVisibleBase()
    {
        if (activeBase == null)
            return;

        if (!activeBase.activeSelf)
            activeBase.SetActive(true);

        SpriteRenderer[] renderers = activeBase.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null)
                continue;

            // BattleShowPresentationManager가 조립한 4x4 SO 아트는
            // 부품별 Sorting Order를 자체 규칙으로 관리합니다.
            // 여기서 Base 기본값으로 다시 덮어쓰지 않습니다.
            if (IsPresentationRenderer(renderer.transform))
                continue;

            renderer.enabled = true;
            renderer.sortingOrder = sortingOrder;
        }
    }

    private static bool IsPresentationRenderer(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            if (current.name.StartsWith("PresentationTemplate", System.StringComparison.Ordinal))
                return true;
            current = current.parent;
        }

        return false;
    }

    private void BuildPrefabBase(Transform parent, Vector3 center, Vector2 targetSize)
    {
        activeBase = Instantiate(basePrefab, center, Quaternion.identity, parent);
        activeBase.name = "PersistentStartBase_4x4";

        if (scalePrefabToTemplate && TryGetRendererBounds(activeBase, out Bounds bounds))
        {
            float width = Mathf.Max(0.001f, bounds.size.x);
            float height = Mathf.Max(0.001f, bounds.size.y);
            Vector3 scale = activeBase.transform.localScale;
            scale.x *= targetSize.x / width;
            scale.y *= targetSize.y / height;
            activeBase.transform.localScale = scale;

            if (TryGetRendererBounds(activeBase, out Bounds resized))
            {
                Vector3 correction = center - resized.center;
                correction.z = 0f;
                activeBase.transform.position += correction;
            }
        }

        ApplyExact4x4Sizing(activeBase);
    }

    private void BuildSpriteBase(Transform parent, Vector3 center, Vector2 targetSize)
    {
        activeBase = new GameObject("PersistentStartBase_4x4");
        activeBase.transform.SetParent(parent, true);
        activeBase.transform.position = center;

        SpriteRenderer renderer = activeBase.AddComponent<SpriteRenderer>();
        bool dummy = baseSprite == null;
        renderer.sprite = dummy ? RuntimeStartBaseSpriteCache.Grid32 : baseSprite;
        renderer.sortingOrder = sortingOrder;
        renderer.color = dummy ? dummyBaseColor : Color.white;

        if (baseMaterial != null)
            renderer.sharedMaterial = baseMaterial;

        if (tileSpriteToTemplate)
        {
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.size = targetSize;
        }
        else
        {
            Vector2 spriteSize = renderer.sprite != null ? renderer.sprite.bounds.size : Vector2.one;
            activeBase.transform.localScale = new Vector3(
                targetSize.x / Mathf.Max(0.001f, spriteSize.x),
                targetSize.y / Mathf.Max(0.001f, spriteSize.y),
                1f);
        }
    }

    private static void ApplyExact4x4Sizing(GameObject root)
    {
        if (root == null)
            return;

        SpriteRenderer renderer = root.GetComponent<SpriteRenderer>();
        if (renderer != null && renderer.drawMode != SpriteDrawMode.Simple)
            renderer.size = new Vector2(FixedBaseTiles, FixedBaseTiles);
    }

    private void EnsureWalkableBaseSource()
    {
        if (!baseProvidesWalkableNavMesh || activeBase == null)
            return;

        SpriteRenderer[] renderers = activeBase.GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer best = null;
        float bestArea = -1f;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null)
                continue;

            float area = Mathf.Abs(renderer.bounds.size.x * renderer.bounds.size.y);
            if (area > bestArea)
            {
                bestArea = area;
                best = renderer;
            }
        }

        if (best == null)
            return;

        NavMeshModifier modifier = best.GetComponent<NavMeshModifier>();
        if (modifier == null)
            modifier = best.gameObject.AddComponent<NavMeshModifier>();

        modifier.ignoreFromBuild = false;
        modifier.overrideArea = false;
        BattleWalkableField.Ensure(best);
        RefreshSupportCollider(activeBase);
    }

    private static void RefreshSupportCollider(GameObject root)
    {
        if (root == null)
            return;

        BattleWalkableField field = root.GetComponentInChildren<BattleWalkableField>(true);
        SpriteRenderer renderer = field != null ? field.GetComponent<SpriteRenderer>() : null;
        if (field == null || renderer == null)
            return;

        BoxCollider2D support = field.GetComponent<BoxCollider2D>();
        if (support == null || !support.isTrigger)
            return;

        support.enabled = true;
        support.size = renderer.drawMode == SpriteDrawMode.Simple
            ? (Vector2)renderer.sprite.bounds.size
            : renderer.size;
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
            if (renderer == null)
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

    private static class RuntimeStartBaseSpriteCache
    {
        private static Sprite grid32;
        public static Sprite Grid32 => grid32 != null ? grid32 : grid32 = CreateGrid32();

        private static Sprite CreateGrid32()
        {
            const int pixels = 32;
            Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
            {
                name = "PersistentStartBase_32pxTile",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color inner = new(0.96f, 0.96f, 0.96f, 1f);
            Color line = new(0.82f, 0.84f, 0.87f, 1f);
            for (int y = 0; y < pixels; y++)
            {
                for (int x = 0; x < pixels; x++)
                {
                    bool border = x == 0 || y == 0;
                    texture.SetPixel(x, y, border ? line : inner);
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
            sprite.name = "PersistentStartBase_32pxTile";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}

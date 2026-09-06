using NavMeshPlus.Components;
using UnityEngine;

/// <summary>
/// Persistent non-combat Start Base only.
///
/// Rules:
/// - 32px = 1 tile = 1 world unit.
/// - Start Base is at least 3x3 tiles.
/// - Start Base exists independently from Gameplay Rooms.
/// - This component does not build Room walls, corridors, bridges, exits, or Gameplay Room shells.
/// - Gameplay Room presentation is owned by BattleSpatialMapController/BattleRoomManager.
/// </summary>
public class RoomBaseTemplate : MonoBehaviour
{
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
    [SerializeField] private int sortingOrder = -100;

    [Header("Sizing")]
    [SerializeField] private bool tileSpriteToTemplate = true;
    [SerializeField] private bool scalePrefabToTemplate = true;

    [Header("Dummy Base")]
    [SerializeField] private Color dummyBaseColor = new(0.16f, 0.18f, 0.22f, 1f);

    private GameObject activeBase;
    private RoomDefinitionSO activeRoom;
    private Vector2 activeWorldSize;
    private bool subscribed;

    public GameObject ActiveBase => activeBase;
    public RoomDefinitionSO ActiveRoom => activeRoom;
    public Vector2 ActiveWorldSize => activeWorldSize;
    public bool HasPersistentBase => activeBase != null;

    private void Awake()
    {
        ResolveSystems();
        ResolveOrigin();
    }

    private void OnEnable()
    {
        ResolveSystems();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        ClearBase();
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
            Transform named = roomManager.transform.Find("RoomOrigin");
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
        if (node == null || node.room == null)
            return;

        // The Start Base is not rebuilt as stages change. It only remembers the latest Room data
        // for inspector/debug context while retaining the original Start Base size and transform.
        if (activeBase != null && keepAcrossRooms)
            activeRoom = node.room;
    }

    public void BuildBase(RoomDefinitionSO room)
    {
        BuildBaseInternal(room, false);
    }

    [ContextMenu("Rebuild Current Start Base")]
    public void RebuildCurrentBase()
    {
        RoomDefinitionSO room = activeRoom;
        if (room == null && runManager != null && runManager.CurrentNode != null)
            room = runManager.CurrentNode.room;
        if (room == null && roomManager != null && roomManager.CurrentRoom != null)
            room = roomManager.CurrentRoom;

        if (room != null)
            BuildBaseInternal(room, true);
    }

    private void BuildBaseInternal(RoomDefinitionSO room, bool forceRebuild)
    {
        if (room == null || !room.useRuntimeBase)
            return;

        if (activeBase != null && keepAcrossRooms && !forceRebuild)
        {
            activeRoom = room;
            return;
        }

        if (activeBase != null)
            ClearBase();

        ResolveOrigin();
        activeRoom = room;
        activeWorldSize = room.GetStartBaseWorldSize();

        Vector3 originPosition = baseOrigin != null ? baseOrigin.position : transform.position;
        Vector3 center = originPosition + (Vector3)room.GetStartBaseCenterOffset();
        center.z = originPosition.z;

        Transform parent = baseRoot != null
            ? baseRoot
            : (roomManager != null ? roomManager.transform : transform);

        if (basePrefab != null)
            BuildPrefabBase(parent, center, activeWorldSize);
        else
            BuildSpriteBase(parent, center, activeWorldSize);

        EnsureWalkableBaseSource();
    }

    [ContextMenu("Clear Start Base")]
    public void ClearBase()
    {
        if (activeBase != null)
        {
            if (Application.isPlaying)
                Destroy(activeBase);
            else
                DestroyImmediate(activeBase);
        }

        activeBase = null;
        activeRoom = null;
        activeWorldSize = Vector2.zero;
    }

    private void BuildPrefabBase(Transform parent, Vector3 center, Vector2 targetSize)
    {
        activeBase = Instantiate(basePrefab, center, Quaternion.identity, parent);
        activeBase.name = "PersistentStartBase";

        if (!scalePrefabToTemplate)
            return;

        if (!TryGetRendererBounds(activeBase, out Bounds bounds))
            return;

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

    private void BuildSpriteBase(Transform parent, Vector3 center, Vector2 targetSize)
    {
        activeBase = new GameObject("PersistentStartBase");
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

            Bounds bounds = renderer.bounds;
            float area = Mathf.Abs(bounds.size.x * bounds.size.y);
            if (area <= bestArea)
                continue;

            bestArea = area;
            best = renderer;
        }

        if (best == null)
            return;

        NavMeshModifier modifier = best.GetComponent<NavMeshModifier>();
        if (modifier == null)
            modifier = best.gameObject.AddComponent<NavMeshModifier>();

        modifier.ignoreFromBuild = false;
        modifier.overrideArea = false;
        BattleWalkableField.Ensure(best);
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

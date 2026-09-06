using NavMeshPlus.Components;
using UnityEngine;

/// <summary>
/// 전투의 영구 Start Base를 관리합니다.
///
/// 기본 규칙:
/// - MapBlock 1개 = 2x2 world unit
/// - 기본 Start Base = 4x4 block = 8x8 world
/// - grid (0,0) block 중심 = roomOrigin
/// - 4x4 Base 중심 = roomOrigin + (3,3)
///
/// Start Base는 첫 Combat/Elite 진입 때 한 번 만들어지고 BattleScene이 살아 있는 동안 유지됩니다.
/// Room이 끝나거나 Run이 Clear/Death/Quit 되어도 제거하지 않습니다.
/// 이후 Room의 blocks는 이 Base 밖에 붙는 확장/도킹 Block만 담당합니다.
///
/// Base 자체가 NavMeshPlus 2D Walkable Source가 되므로 기본 4x4 셀을 별도 MapBlock 16개로 생성할 필요가 없습니다.
/// </summary>
public class RoomBaseTemplate : MonoBehaviour
{
    [Header("Systems")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRoomManager roomManager;

    [Header("Base Transform")]
    [Tooltip("BattleRoomManager의 roomOrigin과 동일한 Transform을 지정하는 것을 권장합니다.")]
    [SerializeField] private Transform baseOrigin;
    [SerializeField] private Transform baseRoot;

    [Header("Persistent Start Base")]
    [Tooltip("첫 Combat Room에서 생성한 Base를 이후 Room/Run 종료에도 유지합니다.")]
    [SerializeField] private bool keepAcrossRooms = true;
    [Tooltip("Start Base의 대표 SpriteRenderer를 NavMeshPlus Walkable Source로 등록합니다.")]
    [SerializeField] private bool baseProvidesWalkableNavMesh = true;

    [Header("Real Base Visual - 둘 다 null이면 Dummy")]
    [Tooltip("완성된 Start Base Prefab이 있다면 지정합니다. Sprite보다 우선 사용합니다.")]
    [SerializeField] private GameObject basePrefab;
    [Tooltip("Base용 Sprite만 사용할 경우 지정합니다. null이면 코드 생성 Grid Dummy를 사용합니다.")]
    [SerializeField] private Sprite baseSprite;
    [SerializeField] private Material baseMaterial;
    [SerializeField] private int sortingOrder = -100;

    [Header("Sizing")]
    [Tooltip("Sprite 모드일 때 SpriteRenderer Tiled를 사용해 Start Base 크기에 맞춥니다.")]
    [SerializeField] private bool tileSpriteToTemplate = true;
    [Tooltip("Prefab 모드일 때 Prefab의 Renderer Bounds를 측정해 Start Base 크기에 맞게 Root Scale을 조절합니다.")]
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
        // Component/Scene 자체가 내려갈 때만 정리합니다.
        // RoomExited / RunEnded에서는 Start Base를 지우지 않습니다.
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
            Transform namedRoomOrigin = roomManager.transform.Find("RoomOrigin");
            baseOrigin = namedRoomOrigin != null
                ? namedRoomOrigin
                : roomManager.transform;
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
        bool combatNode = node != null &&
                          (node.type == BattleNodeType.Combat || node.type == BattleNodeType.Elite);

        if (!combatNode || node.room == null || !node.room.useRuntimeBase)
            return;

        if (activeBase != null && keepAcrossRooms)
        {
            activeRoom = node.room;

            Vector2 requestedSize = node.room.GetRuntimeBaseWorldSize();
            if ((requestedSize - activeWorldSize).sqrMagnitude > 0.001f)
            {
                Debug.LogWarning(
                    $"[RoomBaseTemplate] Persistent Start Base is already {activeWorldSize}, but Room '{node.room.roomId}' requests {requestedSize}. " +
                    "The first Base size is kept for this BattleScene. Use the same Start Base size across a run or call RebuildCurrentBase explicitly.",
                    this);
            }
            return;
        }

        BuildBase(node.room);
    }

    /// <summary>
    /// Start Base를 생성합니다. keepAcrossRooms가 켜져 있고 이미 존재하면 기존 Base를 유지합니다.
    /// </summary>
    public void BuildBase(RoomDefinitionSO room)
    {
        BuildBaseInternal(room, false);
    }

    [ContextMenu("Rebuild Current Start Base")]
    public void RebuildCurrentBase()
    {
        RoomDefinitionSO room = null;

        if (roomManager != null && roomManager.CurrentRoom != null)
            room = roomManager.CurrentRoom;
        else if (runManager != null && runManager.CurrentNode != null)
            room = runManager.CurrentNode.room;
        else
            room = activeRoom;

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
        activeWorldSize = room.GetRuntimeBaseWorldSize();

        Vector3 originPosition = baseOrigin != null
            ? baseOrigin.position
            : transform.position;

        Vector3 center = originPosition + (Vector3)room.GetRuntimeBaseCenterOffset();
        center.z = originPosition.z;

        Transform parent = baseRoot != null
            ? baseRoot
            : (roomManager != null ? roomManager.transform : transform);

        if (basePrefab != null)
            BuildPrefabBase(parent, center, activeWorldSize, room);
        else
            BuildSpriteBase(parent, center, activeWorldSize, room);

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

    private void BuildPrefabBase(
        Transform parent,
        Vector3 center,
        Vector2 targetSize,
        RoomDefinitionSO room)
    {
        activeBase = Instantiate(basePrefab, center, Quaternion.identity, parent);
        activeBase.name = "PersistentStartBase";

        if (!scalePrefabToTemplate)
            return;

        if (!TryGetRendererBounds(activeBase, out Bounds bounds))
        {
            Debug.LogWarning(
                $"[RoomBaseTemplate] Base prefab '{basePrefab.name}' has no Renderer. Automatic Start Base scaling was skipped.",
                this);
            return;
        }

        float width = Mathf.Max(0.001f, bounds.size.x);
        float height = Mathf.Max(0.001f, bounds.size.y);

        Vector3 scale = activeBase.transform.localScale;
        scale.x *= targetSize.x / width;
        scale.y *= targetSize.y / height;
        activeBase.transform.localScale = scale;

        if (TryGetRendererBounds(activeBase, out Bounds resizedBounds))
        {
            Vector3 correction = center - resizedBounds.center;
            correction.z = 0f;
            activeBase.transform.position += correction;
        }
    }

    private void BuildSpriteBase(
        Transform parent,
        Vector3 center,
        Vector2 targetSize,
        RoomDefinitionSO room)
    {
        activeBase = new GameObject("PersistentStartBase");
        activeBase.transform.SetParent(parent, true);
        activeBase.transform.position = center;

        SpriteRenderer renderer = activeBase.AddComponent<SpriteRenderer>();
        bool dummy = baseSprite == null;
        renderer.sprite = dummy
            ? RuntimeRoomBaseSpriteCache.Grid
            : baseSprite;
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
            Vector2 spriteSize = renderer.sprite != null
                ? renderer.sprite.bounds.size
                : Vector2.one;

            float width = Mathf.Max(0.001f, spriteSize.x);
            float height = Mathf.Max(0.001f, spriteSize.y);
            activeBase.transform.localScale = new Vector3(
                targetSize.x / width,
                targetSize.y / height,
                1f);
        }
    }

    /// <summary>
    /// NavMeshPlus 2D는 NavMeshModifier가 붙은 SpriteRenderer를 RenderMesh source로 수집합니다.
    /// Start Base가 실제 기본 바닥을 담당하므로 가장 큰 SpriteRenderer 하나를 Walkable Source로 등록합니다.
    /// </summary>
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
        {
            Debug.LogWarning(
                "[RoomBaseTemplate] Persistent Start Base has no SpriteRenderer to use as a NavMeshPlus 2D source. " +
                "The Base will remain visual-only until a floor SpriteRenderer is provided.",
                this);
            return;
        }

        NavMeshModifier modifier = best.GetComponent<NavMeshModifier>();
        if (modifier == null)
            modifier = best.gameObject.AddComponent<NavMeshModifier>();

        modifier.ignoreFromBuild = false;
        modifier.overrideArea = false;
    }

    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private static class RuntimeRoomBaseSpriteCache
    {
        private static Sprite grid;
        public static Sprite Grid => grid != null ? grid : grid = CreateGridSprite();

        private static Sprite CreateGridSprite()
        {
            const int size = 16;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            {
                name = "PersistentStartBaseGridTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color inner = new(0.68f, 0.68f, 0.68f, 1f);
            Color border = Color.white;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    texture.SetPixel(x, y, edge ? border : inner);
                }
            }

            texture.Apply(false, false);

            // 16px / 8 PPU = 2 world. Dummy Grid 한 칸 = MapBlock 한 칸.
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                8f,
                0,
                SpriteMeshType.FullRect);

            sprite.name = "PersistentStartBaseGridSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}

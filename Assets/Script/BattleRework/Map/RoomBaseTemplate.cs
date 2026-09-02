using UnityEngine;

/// <summary>
/// 기존 테스트 씬에 수동으로 배치하던 Field Base를 대체합니다.
/// Combat/Elite Node 진입 시 RoomDefinitionSO의 템플릿 크기에 맞는 Base를 자동 생성하고,
/// Room 종료 시 자동 제거합니다.
///
/// 기본 좌표 규칙:
/// - MapBlock 1개 = 2x2 world unit = 128x128px (64px/unit 기준)
/// - Room 4x4 block = 8x8 world unit = 512x512px
/// - grid (0,0) block 중심 = roomOrigin
/// - 따라서 4x4 Base 중심은 roomOrigin + (3,3)
///
/// Base는 기본적으로 시각적 기준면입니다. 실제 이동/NavMesh 바닥은 MapBlock Prefab이 담당합니다.
/// </summary>
public class RoomBaseTemplate : MonoBehaviour
{
    [Header("Systems")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRoomManager roomManager;

    [Header("Base Transform")]
    [Tooltip("BattleRoomManager의 roomOrigin과 동일한 Transform을 지정하는 것을 권장합니다. null이면 RoomManager Transform/RoomOrigin 이름의 자식을 자동 탐색합니다.")]
    [SerializeField] private Transform baseOrigin;
    [SerializeField] private Transform baseRoot;

    [Header("Real Base Visual - 둘 다 null이면 Dummy")]
    [Tooltip("완성된 Base Prefab이 있다면 지정합니다. Sprite보다 우선 사용합니다.")]
    [SerializeField] private GameObject basePrefab;
    [Tooltip("Base용 Sprite만 사용할 경우 지정합니다. null이면 코드 생성 Grid Dummy를 사용합니다.")]
    [SerializeField] private Sprite baseSprite;
    [SerializeField] private Material baseMaterial;
    [SerializeField] private int sortingOrder = -100;

    [Header("Sizing")]
    [Tooltip("Sprite 모드일 때 SpriteRenderer Tiled를 사용해 템플릿 크기에 맞춥니다.")]
    [SerializeField] private bool tileSpriteToTemplate = true;
    [Tooltip("Prefab 모드일 때 Prefab의 Renderer Bounds를 측정해 템플릿 크기에 맞게 Root Scale을 조절합니다.")]
    [SerializeField] private bool scalePrefabToTemplate = true;

    [Header("Dummy Base")]
    [SerializeField] private Color dummyBaseColor = new(0.16f, 0.18f, 0.22f, 1f);
    [SerializeField] private Color dummyGridColor = new(0.42f, 0.45f, 0.50f, 1f);

    private GameObject activeBase;
    private RoomDefinitionSO activeRoom;
    private Vector2 activeWorldSize;
    private bool subscribed;

    public GameObject ActiveBase => activeBase;
    public RoomDefinitionSO ActiveRoom => activeRoom;
    public Vector2 ActiveWorldSize => activeWorldSize;

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
        {
            runManager.NodeEntered += HandleNodeEntered;
            runManager.RunEnded += HandleRunEnded;
        }

        if (roomManager != null)
            roomManager.RoomExited += HandleRoomExited;

        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (runManager != null)
        {
            runManager.NodeEntered -= HandleNodeEntered;
            runManager.RunEnded -= HandleRunEnded;
        }

        if (roomManager != null)
            roomManager.RoomExited -= HandleRoomExited;

        subscribed = false;
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        bool combatNode = node != null &&
                          (node.type == BattleNodeType.Combat || node.type == BattleNodeType.Elite);

        if (!combatNode || node.room == null || !node.room.useRuntimeBase)
        {
            ClearBase();
            return;
        }

        BuildBase(node.room);
    }

    private void HandleRoomExited(RoomDefinitionSO room)
    {
        if (room == null || activeRoom == null || room == activeRoom)
            ClearBase();
    }

    private void HandleRunEnded(RunEndReason reason)
    {
        ClearBase();
    }

    public void BuildBase(RoomDefinitionSO room)
    {
        ClearBase();

        if (room == null || !room.useRuntimeBase)
            return;

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
        {
            BuildPrefabBase(parent, center, activeWorldSize, room);
            return;
        }

        BuildSpriteBase(parent, center, activeWorldSize, room);
    }

    public void RebuildCurrentBase()
    {
        if (roomManager != null && roomManager.CurrentRoom != null)
        {
            BuildBase(roomManager.CurrentRoom);
            return;
        }

        if (runManager != null && runManager.CurrentNode != null)
            HandleNodeEntered(runManager.CurrentNode);
    }

    public void ClearBase()
    {
        if (activeBase != null)
            Destroy(activeBase);

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
        activeBase.name = $"RuntimeRoomBase_{SafeRoomName(room)}";

        if (!scalePrefabToTemplate)
            return;

        if (!TryGetRendererBounds(activeBase, out Bounds bounds))
        {
            Debug.LogWarning(
                $"[RoomBaseTemplate] Base prefab '{basePrefab.name}' has no Renderer. " +
                "Automatic template scaling was skipped.");
            return;
        }

        float width = Mathf.Max(0.001f, bounds.size.x);
        float height = Mathf.Max(0.001f, bounds.size.y);

        Vector3 scale = activeBase.transform.localScale;
        scale.x *= targetSize.x / width;
        scale.y *= targetSize.y / height;
        activeBase.transform.localScale = scale;

        // Renderer pivot/child offset이 있는 Prefab도 최종 Bounds 중심이 템플릿 중심에 오도록 한 번 더 보정합니다.
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
        activeBase = new GameObject($"RuntimeRoomBase_{SafeRoomName(room)}");
        activeBase.transform.SetParent(parent, true);
        activeBase.transform.position = center;

        SpriteRenderer renderer = activeBase.AddComponent<SpriteRenderer>();
        renderer.sprite = baseSprite != null
            ? baseSprite
            : RuntimeRoomBaseSpriteCache.Grid;
        renderer.sortingOrder = sortingOrder;

        if (baseMaterial != null)
            renderer.sharedMaterial = baseMaterial;

        bool dummy = baseSprite == null;
        renderer.color = dummy ? Color.white : Color.white;

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

        if (dummy)
            ApplyDummyColors(renderer);
    }

    private void ApplyDummyColors(SpriteRenderer renderer)
    {
        if (renderer == null)
            return;

        // Dummy Sprite 자체는 grayscale grid입니다. Renderer 색으로 Base tone을 결정합니다.
        Color baseColor = dummyBaseColor;
        float gridBoost = Mathf.Clamp01(
            (dummyGridColor.r + dummyGridColor.g + dummyGridColor.b) / 3f);

        float boost = Mathf.Lerp(0.85f, 1.25f, gridBoost);
        renderer.color = new Color(
            Mathf.Clamp01(baseColor.r * boost),
            Mathf.Clamp01(baseColor.g * boost),
            Mathf.Clamp01(baseColor.b * boost),
            baseColor.a);
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

    private static string SafeRoomName(RoomDefinitionSO room)
    {
        if (room == null)
            return "Unknown";

        return string.IsNullOrWhiteSpace(room.roomId)
            ? room.name
            : room.roomId;
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
                name = "RuntimeRoomBaseGridTexture",
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

            texture.Apply(false, true);

            // 16px / 8 PPU = 2 world unit. 즉 Dummy Grid 한 칸이 MapBlock 하나와 정확히 같은 크기입니다.
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                8f,
                0,
                SpriteMeshType.FullRect);

            sprite.name = "RuntimeRoomBaseGridSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}

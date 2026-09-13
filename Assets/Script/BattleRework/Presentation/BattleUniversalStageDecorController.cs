using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene 이름과 무관하게 실제 World Floor가 존재하는 곳에 촬영장 치장 Carrier를 배치합니다.
///
/// 규칙:
/// - 1x1 / 2x2 / 2x3(3x2) / 3x3 Carrier를 지원합니다.
/// - Camera / Light Rig는 2x2를 정식 footprint로 사용합니다.
/// - Light는 BattleUniversalStageLightRigSO의 Base + Head 조립만 사용합니다.
/// - Camera/Light를 포함한 장비는 상하 반전/180도 회전하지 않습니다. 허용되는 랜덤 반전은 좌우 Mirror뿐입니다.
/// - Cable은 Spawn Entry에서 제외합니다. Cable은 CarrierSkinController가 Camera/Light 바닥 장식으로만 배치합니다.
/// - 실제 Field 외곽에서 최소 1 Floor 간격을 두고 화면 밖 Cardinal Rail에서 진입합니다.
/// - NavMesh / Walkable에는 참여하지 않습니다.
/// - 가까운 MapBlock을 Owner로 기억하고 Owner 퇴장 방향을 따라 자연스럽게 빠집니다.
/// </summary>
[DefaultExecutionOrder(30150)]
[DisallowMultipleComponent]
public sealed class BattleUniversalStageDecorController : MonoBehaviour
{
    private const string RuntimeObjectName = "BattleUniversalStageDecorRuntime";
    private const string ClusterPrefix = "UniversalStageDecor_";

    private static BattleUniversalStageDecorController instance;

    [Header("PROFILE")]
    [Tooltip("Scene Config가 없을 때 Resources에서 찾을 공용 Profile 경로입니다.")]
    [SerializeField] private string resourcesProfilePath = "BattleUniversalStageDecorProfile";
    [SerializeField] private BattleUniversalStageDecorProfileSO fallbackProfile;
    [Tooltip("Profile이 없을 때 이미 로드된 Camera/Monitor/Case/Fence 계열 Sprite만 보수적으로 자동 탐색합니다. Cable/Light는 자동 독립 Spawn하지 않습니다.")]
    [SerializeField] private bool autoDiscoverLoadedDecorSprites = true;

    [Header("PLACEMENT")]
    [Tooltip("실제 Field와 장식 Carrier 사이에 비워 두는 Floor 칸 수입니다.")]
    [SerializeField, Min(1f)] private float fieldGapTiles = 1f;
    [SerializeField, Min(0.25f)] private float defaultCellWorldSize = 1f;

    [Tooltip("1x1 Prop 수. 해당 footprint 소스가 없으면 생성하지 않습니다.")]
    [SerializeField, Range(0, 6)] private int minimumSmallCount = 1;
    [SerializeField, Range(0, 8)] private int maximumSmallCount = 3;

    [Tooltip("Camera / Light용 2x2 Carrier 수. 해당 footprint 소스가 없으면 생성하지 않습니다.")]
    [SerializeField, Range(0, 4)] private int minimumRigCount = 1;
    [SerializeField, Range(0, 5)] private int maximumRigCount = 2;

    [Tooltip("Monitor 등 2x3 / 3x2 Prop 수.")]
    [SerializeField, Range(0, 4)] private int minimumMediumCount = 0;
    [SerializeField, Range(0, 5)] private int maximumMediumCount = 1;

    [Tooltip("3x3 대형 Prop 수.")]
    [SerializeField, Range(0, 2)] private int minimumLargeCount = 0;
    [SerializeField, Range(0, 3)] private int maximumLargeCount = 1;

    [SerializeField, Range(0f, 1f)] private float placementPaddingTiles = 0.28f;
    [SerializeField, Range(8, 64)] private int placementAttemptsPerCluster = 28;

    [Header("ENTRY")]
    [SerializeField, Range(0.20f, 1.5f)] private float entryDuration = 0.58f;
    [SerializeField, Min(8f)] private float minimumOffscreenRail = 24f;
    [SerializeField, Range(0.25f, 4f)] private float offscreenMargin = 1.5f;
    [SerializeField, Range(0f, 2f)] private float entryRumbleDegrees = 0.40f;
    [SerializeField, Range(1, 20)] private int entryRumbleVibrato = 7;

    [Header("EXIT")]
    [SerializeField, Range(0.04f, 0.25f)] private float exitAnticipationDuration = 0.085f;
    [SerializeField, Range(0.02f, 0.30f)] private float exitAnticipationDistance = 0.10f;
    [SerializeField, Range(0.25f, 1.8f)] private float fallbackExitDuration = 0.58f;
    [SerializeField, Range(0.002f, 0.08f)] private float ownerExitMotionThreshold = 0.012f;
    [SerializeField, Range(0.05f, 0.5f)] private float transitionExitFallbackDelay = 0.16f;

    [Header("SCENE / REBUILD")]
    [SerializeField, Range(0.05f, 1.5f)] private float sceneFloorResolveDelay = 0.30f;
    [SerializeField, Range(0.05f, 1.0f)] private float postTransitionResolveDelay = 0.12f;

    private sealed class FloorSource
    {
        public SpriteRenderer renderer;
        public MapBlock owner;
        public Bounds bounds;
    }

    private sealed class RuntimeDecorEntry
    {
        public string label;
        public BattleUniversalStageDecorCategory category;
        public Sprite sprite;
        public GameObject prefab;
        public BattleUniversalStageLightRigSO lightRig;
        public Vector2Int footprint;
        public int weight;
        public Vector2 localOffset;
        public Vector2 localScale;
        public int sortingOrderOffset;
        public bool randomFlipX;
    }

    private sealed class DecorCluster
    {
        public GameObject rootObject;
        public Transform root;
        public Transform visualRoot;
        public MapBlock owner;
        public Vector3 ownerLastPosition;
        public Vector3 finalPosition;
        public Vector2 outwardDirection;
        public Bounds carrierBounds;
        public Vector2Int footprint;
        public int sceneHandle;
        public bool exiting;
        public Sequence sequence;
    }

    private readonly List<DecorCluster> clusters = new();
    private readonly List<RuntimeDecorEntry> runtimeFallbackEntries = new();
    private readonly List<Bounds> placementBounds = new();

    private Scene targetScene;
    private int targetSceneHandle = -1;
    private BattleRoomManager roomManager;
    private BattleUniversalStageDecorProfileSO activeProfile;
    private bool sceneDecorDisabled;

    private bool rebuildPending;
    private float rebuildAt;
    private int lastFieldSignature = int.MinValue;
    private int serial;

    private bool transitionStateInitialized;
    private bool lastTransitioning;
    private float transitionFallbackExitAt = float.PositiveInfinity;

    public static BattleUniversalStageDecorController Instance => instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleUniversalStageDecorController>(FindObjectsInactive.Include) != null)
            return;

        GameObject host = new(RuntimeObjectName);
        DontDestroyOnLoad(host);
        host.AddComponent<BattleUniversalStageDecorController>();
    }

    public static void RequestRefresh(float delay = 0.10f)
    {
        if (instance != null)
            instance.QueueRebuild(Mathf.Max(0f, delay));
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    private void Start()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.IsValid() && active.isLoaded)
            AdoptScene(active, sceneFloorResolveDelay, false);
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        instance = null;
    }

    private void Update()
    {
        CleanupDestroyedClusters();
        MonitorClusterOwners();
        UpdateRoomTransitionState();

        if (!rebuildPending || Time.unscaledTime < rebuildAt)
            return;
        if (!targetScene.IsValid() || !targetScene.isLoaded)
            return;

        if (roomManager != null && roomManager.gameObject.scene == targetScene && roomManager.IsTransitioning)
        {
            rebuildAt = Time.unscaledTime + 0.12f;
            return;
        }

        rebuildPending = false;
        ReconcileCurrentScene();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        if (mode == LoadSceneMode.Single && targetSceneHandle >= 0 && targetSceneHandle != scene.handle)
            BeginExitAll(Vector2.zero, fallbackExitDuration);

        if (scene == SceneManager.GetActiveScene() || mode == LoadSceneMode.Single)
            AdoptScene(scene, sceneFloorResolveDelay, true);
    }

    private void HandleSceneUnloaded(Scene scene)
    {
        if (scene.handle != targetSceneHandle)
            return;

        BeginExitAll(Vector2.zero, fallbackExitDuration);
        targetSceneHandle = -1;
        targetScene = default;
        roomManager = null;
        rebuildPending = false;
        lastFieldSignature = int.MinValue;
    }

    private void HandleActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        if (!newScene.IsValid() || !newScene.isLoaded || newScene.handle == targetSceneHandle)
            return;

        BeginExitAll(Vector2.zero, fallbackExitDuration);
        AdoptScene(newScene, sceneFloorResolveDelay, true);
    }

    private void AdoptScene(Scene scene, float delay, bool resetSignature)
    {
        targetScene = scene;
        targetSceneHandle = scene.handle;
        ResolveSceneConfiguration(scene);
        ResolveRoomManager(scene);

        if (resetSignature)
            lastFieldSignature = int.MinValue;

        transitionStateInitialized = roomManager != null;
        lastTransitioning = roomManager != null && roomManager.IsTransitioning;
        transitionFallbackExitAt = float.PositiveInfinity;
        QueueRebuild(delay);
    }

    private void ResolveSceneConfiguration(Scene scene)
    {
        sceneDecorDisabled = false;
        activeProfile = fallbackProfile;

        BattleUniversalStageDecorSceneConfig[] configs = FindObjectsByType<BattleUniversalStageDecorSceneConfig>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < configs.Length; i++)
        {
            BattleUniversalStageDecorSceneConfig config = configs[i];
            if (config == null || config.gameObject.scene != scene)
                continue;

            sceneDecorDisabled = config.DisableUniversalStageDecor;
            if (config.Profile != null)
                activeProfile = config.Profile;
            break;
        }

        if (activeProfile == null && !string.IsNullOrWhiteSpace(resourcesProfilePath))
            activeProfile = Resources.Load<BattleUniversalStageDecorProfileSO>(resourcesProfilePath);
    }

    private void ResolveRoomManager(Scene scene)
    {
        roomManager = null;
        BattleRoomManager[] managers = FindObjectsByType<BattleRoomManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleRoomManager candidate = managers[i];
            if (candidate != null && candidate.gameObject.scene == scene)
            {
                roomManager = candidate;
                return;
            }
        }
    }

    private void QueueRebuild(float delay)
    {
        rebuildPending = true;
        rebuildAt = Mathf.Max(rebuildAt, Time.unscaledTime + Mathf.Max(0f, delay));
    }

    private void UpdateRoomTransitionState()
    {
        if (roomManager == null || roomManager.gameObject.scene.handle != targetSceneHandle)
            return;

        bool transitioning = roomManager.IsTransitioning;
        if (!transitionStateInitialized)
        {
            transitionStateInitialized = true;
            lastTransitioning = transitioning;
            return;
        }

        if (transitioning == lastTransitioning)
        {
            if (transitioning && Time.unscaledTime >= transitionFallbackExitAt)
            {
                BeginExitAll(Vector2.zero, fallbackExitDuration);
                transitionFallbackExitAt = float.PositiveInfinity;
            }
            return;
        }

        lastTransitioning = transitioning;
        if (transitioning)
        {
            transitionFallbackExitAt = Time.unscaledTime + Mathf.Max(0.05f, transitionExitFallbackDelay);
            return;
        }

        transitionFallbackExitAt = float.PositiveInfinity;
        lastFieldSignature = int.MinValue;
        QueueRebuild(postTransitionResolveDelay);
    }

    private void MonitorClusterOwners()
    {
        float threshold = Mathf.Max(0.002f, ownerExitMotionThreshold);
        float thresholdSqr = threshold * threshold;

        for (int i = 0; i < clusters.Count; i++)
        {
            DecorCluster cluster = clusters[i];
            if (cluster == null || cluster.exiting || cluster.root == null)
                continue;

            if (cluster.owner == null)
            {
                if (cluster.sceneHandle == targetSceneHandle && roomManager != null && roomManager.IsTransitioning)
                    PlayExit(cluster, cluster.outwardDirection, fallbackExitDuration);
                continue;
            }

            if (!cluster.owner.gameObject.activeInHierarchy)
            {
                PlayExit(cluster, cluster.outwardDirection, cluster.owner.ExitDuration);
                QueueRebuild(cluster.owner.ExitDuration + postTransitionResolveDelay);
                continue;
            }

            Vector3 current = cluster.owner.transform.position;
            Vector3 delta = current - cluster.ownerLastPosition;
            cluster.ownerLastPosition = current;
            if (delta.sqrMagnitude <= thresholdSqr)
                continue;

            PlayExit(cluster, Cardinalize(delta), cluster.owner.ExitDuration);
            QueueRebuild(cluster.owner.ExitDuration + postTransitionResolveDelay);
        }
    }

    private void ReconcileCurrentScene()
    {
        if (sceneDecorDisabled)
        {
            BeginExitAll(Vector2.zero, fallbackExitDuration);
            return;
        }

        List<FloorSource> sources = CollectFloorSources(targetScene);
        if (sources.Count == 0)
        {
            BeginExitAll(Vector2.zero, fallbackExitDuration);
            lastFieldSignature = int.MinValue;
            return;
        }

        int signature = ComputeFieldSignature(sources);
        if (signature == lastFieldSignature && HasLivingClustersForScene(targetSceneHandle))
            return;

        if (clusters.Count > 0)
        {
            BeginExitAll(Vector2.zero, fallbackExitDuration);
            lastFieldSignature = int.MinValue;
            QueueRebuild(fallbackExitDuration + exitAnticipationDuration + 0.08f);
            return;
        }

        BuildDressing(sources, signature);
    }

    private void BuildDressing(List<FloorSource> sources, int signature)
    {
        Bounds fieldBounds = sources[0].bounds;
        for (int i = 1; i < sources.Count; i++)
            fieldBounds.Encapsulate(sources[i].bounds);

        SpriteRenderer floorTemplate = ResolveFloorTemplate(sources);
        float cell = ResolveCellWorldSize(floorTemplate);
        List<RuntimeDecorEntry> entries = ResolveDecorEntries();
        if (entries.Count == 0)
        {
            lastFieldSignature = signature;
            return;
        }

        System.Random random = new(unchecked(
            targetSceneHandle * 73856093 ^ signature * 19349663 ^ Environment.TickCount));

        List<Vector2Int> plans = BuildFootprintPlans(entries, random);
        placementBounds.Clear();

        for (int i = 0; i < plans.Count; i++)
        {
            Vector2Int footprint = plans[i];
            RuntimeDecorEntry entry = PickEntry(entries, footprint, random);
            if (entry == null)
                continue;

            if (!TryResolvePlacement(
                    fieldBounds,
                    footprint,
                    cell,
                    random,
                    out Vector3 target,
                    out Vector2 outward,
                    out Bounds carrierBounds))
                continue;

            MapBlock owner = ResolveClosestOwner(target, sources);
            DecorCluster cluster = CreateCluster(
                target,
                outward,
                carrierBounds,
                footprint,
                cell,
                floorTemplate,
                entry,
                owner,
                random);

            if (cluster == null)
                continue;

            clusters.Add(cluster);
            placementBounds.Add(carrierBounds);
            PlayEnter(cluster);
        }

        lastFieldSignature = signature;
    }

    private List<Vector2Int> BuildFootprintPlans(List<RuntimeDecorEntry> entries, System.Random random)
    {
        List<Vector2Int> result = new();

        AppendPlanCount(result, entries, Vector2Int.one,
            NextInclusive(random, Mathf.Min(minimumSmallCount, maximumSmallCount), Mathf.Max(minimumSmallCount, maximumSmallCount)));

        AppendPlanCount(result, entries, new Vector2Int(2, 2),
            NextInclusive(random, Mathf.Min(minimumRigCount, maximumRigCount), Mathf.Max(minimumRigCount, maximumRigCount)));

        int medium = NextInclusive(random,
            Mathf.Min(minimumMediumCount, maximumMediumCount),
            Mathf.Max(minimumMediumCount, maximumMediumCount));
        if (HasEntryForFootprint(entries, new Vector2Int(2, 3)))
        {
            for (int i = 0; i < medium; i++)
                result.Add(random.NextDouble() < 0.5 ? new Vector2Int(2, 3) : new Vector2Int(3, 2));
        }

        AppendPlanCount(result, entries, new Vector2Int(3, 3),
            NextInclusive(random, Mathf.Min(minimumLargeCount, maximumLargeCount), Mathf.Max(minimumLargeCount, maximumLargeCount)));

        // 큰 것부터 배치해 공간 충돌을 줄입니다.
        result.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
        return result;
    }

    private static void AppendPlanCount(
        List<Vector2Int> result,
        List<RuntimeDecorEntry> entries,
        Vector2Int footprint,
        int count)
    {
        if (!HasEntryForFootprint(entries, footprint))
            return;
        for (int i = 0; i < count; i++)
            result.Add(footprint);
    }

    private static bool HasEntryForFootprint(List<RuntimeDecorEntry> entries, Vector2Int footprint)
    {
        if (entries == null)
            return false;
        for (int i = 0; i < entries.Count; i++)
        {
            RuntimeDecorEntry entry = entries[i];
            if (entry != null && FootprintEquivalent(entry.footprint, footprint))
                return true;
        }
        return false;
    }

    private bool TryResolvePlacement(
        Bounds field,
        Vector2Int footprint,
        float cell,
        System.Random random,
        out Vector3 target,
        out Vector2 outward,
        out Bounds candidateBounds)
    {
        target = Vector3.zero;
        outward = Vector2.zero;
        candidateBounds = default;

        float width = Mathf.Max(cell, footprint.x * cell);
        float height = Mathf.Max(cell, footprint.y * cell);
        float gap = Mathf.Max(cell, fieldGapTiles * cell);
        int attempts = Mathf.Clamp(placementAttemptsPerCluster, 8, 64);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            int side = random.Next(0, 4);
            float x;
            float y;

            switch (side)
            {
                case 0:
                    outward = Vector2.left;
                    x = field.min.x - gap - width * 0.5f;
                    y = SnapToCell(RandomRange(random, field.min.y, field.max.y), cell);
                    break;
                case 1:
                    outward = Vector2.right;
                    x = field.max.x + gap + width * 0.5f;
                    y = SnapToCell(RandomRange(random, field.min.y, field.max.y), cell);
                    break;
                case 2:
                    outward = Vector2.up;
                    x = SnapToCell(RandomRange(random, field.min.x, field.max.x), cell);
                    y = field.max.y + gap + height * 0.5f;
                    break;
                default:
                    outward = Vector2.down;
                    x = SnapToCell(RandomRange(random, field.min.x, field.max.x), cell);
                    y = field.min.y - gap - height * 0.5f;
                    break;
            }

            target = new Vector3(x, y, field.center.z);
            candidateBounds = new Bounds(target, new Vector3(width, height, 0.2f));
            if (!OverlapsExistingPlacement(candidateBounds, cell))
                return true;
        }

        return false;
    }

    private bool OverlapsExistingPlacement(Bounds candidate, float cell)
    {
        float padding = Mathf.Max(0f, placementPaddingTiles) * cell;
        for (int i = 0; i < placementBounds.Count; i++)
        {
            Bounds other = placementBounds[i];
            bool overlapX = Mathf.Abs(candidate.center.x - other.center.x) <
                            candidate.extents.x + other.extents.x + padding;
            bool overlapY = Mathf.Abs(candidate.center.y - other.center.y) <
                            candidate.extents.y + other.extents.y + padding;
            if (overlapX && overlapY)
                return true;
        }
        return false;
    }

    private DecorCluster CreateCluster(
        Vector3 target,
        Vector2 outward,
        Bounds carrierBounds,
        Vector2Int footprint,
        float cell,
        SpriteRenderer floorTemplate,
        RuntimeDecorEntry entry,
        MapBlock owner,
        System.Random random)
    {
        GameObject rootObject = new($"{ClusterPrefix}{++serial:000}_{footprint.x}x{footprint.y}");
        rootObject.transform.SetParent(transform, true);
        rootObject.transform.position = target;

        BattleUniversalStageDecorRuntimeMarker marker = rootObject.AddComponent<BattleUniversalStageDecorRuntimeMarker>();
        marker.Configure(entry.category, footprint, entry.label);

        GameObject visualObject = new("Visual");
        visualObject.transform.SetParent(rootObject.transform, false);
        Transform visualRoot = visualObject.transform;

        BuildCarrierFloor(visualRoot, footprint, cell, floorTemplate);
        BuildDecorVisual(visualRoot, entry, floorTemplate, random);

        return new DecorCluster
        {
            rootObject = rootObject,
            root = rootObject.transform,
            visualRoot = visualRoot,
            owner = owner,
            ownerLastPosition = owner != null ? owner.transform.position : Vector3.zero,
            finalPosition = target,
            outwardDirection = Cardinalize(outward),
            carrierBounds = carrierBounds,
            footprint = footprint,
            sceneHandle = targetSceneHandle
        };
    }

    private static void BuildCarrierFloor(
        Transform visualRoot,
        Vector2Int footprint,
        float cell,
        SpriteRenderer template)
    {
        if (visualRoot == null || template == null || template.sprite == null)
            return;

        Sprite sprite = template.sprite;
        Vector3 spriteSize = sprite.bounds.size;
        float sx = cell / Mathf.Max(0.01f, spriteSize.x);
        float sy = cell / Mathf.Max(0.01f, spriteSize.y);
        float x0 = -(footprint.x - 1) * cell * 0.5f;
        float y0 = -(footprint.y - 1) * cell * 0.5f;

        for (int y = 0; y < footprint.y; y++)
        {
            for (int x = 0; x < footprint.x; x++)
            {
                GameObject tile = new($"DecorCarrierTile_{x}_{y}");
                tile.transform.SetParent(visualRoot, false);
                tile.transform.localPosition = new Vector3(x0 + x * cell, y0 + y * cell, 0f);
                tile.transform.localScale = new Vector3(sx, sy, 1f);

                SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = template.color;
                renderer.sharedMaterial = template.sharedMaterial;
                renderer.sortingLayerID = template.sortingLayerID;
                renderer.sortingOrder = template.sortingOrder;
            }
        }
    }

    private static void BuildDecorVisual(
        Transform visualRoot,
        RuntimeDecorEntry entry,
        SpriteRenderer floorTemplate,
        System.Random random)
    {
        if (visualRoot == null || entry == null)
            return;

        Transform decorTransform = null;
        bool prefabVisual = false;

        if (entry.lightRig != null && entry.lightRig.IsValid)
        {
            decorTransform = entry.lightRig.BuildRuntime(
                visualRoot,
                floorTemplate,
                entry.sortingOrderOffset,
                random);
        }
        else if (entry.prefab != null)
        {
            GameObject clone = Instantiate(entry.prefab, visualRoot, false);
            clone.name = "DecorObject_" + SafeName(entry.label);
            decorTransform = clone.transform;
            prefabVisual = true;
            StripGameplayComponents(clone);

            SpriteRenderer[] renderers = clone.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                if (floorTemplate != null)
                    renderer.sortingLayerID = floorTemplate.sortingLayerID;
                renderer.sortingOrder += entry.sortingOrderOffset;
            }
        }
        else if (entry.sprite != null)
        {
            GameObject decor = new("DecorObject_" + SafeName(entry.label));
            decor.transform.SetParent(visualRoot, false);
            SpriteRenderer renderer = decor.AddComponent<SpriteRenderer>();
            renderer.sprite = entry.sprite;
            if (floorTemplate != null)
            {
                renderer.sortingLayerID = floorTemplate.sortingLayerID;
                renderer.sharedMaterial = floorTemplate.sharedMaterial;
            }
            renderer.sortingOrder = (floorTemplate != null ? floorTemplate.sortingOrder : 0) + entry.sortingOrderOffset;
            renderer.flipX = entry.randomFlipX && random.NextDouble() < 0.5;
            decorTransform = decor.transform;
        }

        if (decorTransform == null)
            return;

        decorTransform.localPosition += new Vector3(entry.localOffset.x, entry.localOffset.y, -0.01f);
        Vector3 scale = decorTransform.localScale;
        scale.x *= Mathf.Approximately(entry.localScale.x, 0f) ? 1f : entry.localScale.x;
        scale.y *= Mathf.Approximately(entry.localScale.y, 0f) ? 1f : entry.localScale.y;

        // Prefab은 SpriteRenderer.flipX를 통일하기 어려우므로 Root X scale만 반전합니다.
        // 어떤 경우에도 Z 180도 회전/상하 반전은 하지 않습니다.
        if (prefabVisual && entry.randomFlipX && random.NextDouble() < 0.5)
            scale.x = -Mathf.Abs(scale.x);

        decorTransform.localScale = scale;
        decorTransform.localRotation = Quaternion.identity;
    }

    private static void StripGameplayComponents(GameObject root)
    {
        if (root == null)
            return;

        Collider2D[] colliders = root.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
                colliders[i].enabled = false;

        Rigidbody2D[] rigidbodies = root.GetComponentsInChildren<Rigidbody2D>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
            if (rigidbodies[i] != null)
                rigidbodies[i].simulated = false;

        BattleWalkableField[] walkables = root.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < walkables.Length; i++)
            if (walkables[i] != null)
                walkables[i].enabled = false;

        MapBlock[] blocks = root.GetComponentsInChildren<MapBlock>(true);
        for (int i = 0; i < blocks.Length; i++)
            if (blocks[i] != null)
                blocks[i].enabled = false;
    }

    private void PlayEnter(DecorCluster cluster)
    {
        if (cluster?.root == null)
            return;

        Vector2 outward = Cardinalize(cluster.outwardDirection);
        float distance = ResolveOffscreenDistance(cluster.carrierBounds, outward);
        Vector3 start = cluster.finalPosition + (Vector3)(outward * distance);
        Vector2 travel = -outward;

        cluster.root.position = start;
        cluster.sequence?.Kill(false);

        float area = Mathf.Max(1f, cluster.footprint.x * cluster.footprint.y);
        float duration = Mathf.Max(0.20f, entryDuration) * Mathf.Lerp(0.92f, 1.12f, Mathf.InverseLerp(1f, 9f, area));

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Append(cluster.root.DOMove(cluster.finalPosition, duration).SetEase(Ease.InCubic));

        BattleTileDockingPresentationManager docking = BattleTileDockingPresentationManager.Instance;
        if (docking != null)
        {
            docking.AppendDockSettle(
                sequence,
                cluster.root,
                cluster.finalPosition,
                travel,
                ResolveContactPoint(cluster.carrierBounds, travel),
                Mathf.Lerp(0.45f, 0.78f, Mathf.InverseLerp(1f, 9f, area)),
                false,
                false);
        }
        else
        {
            Vector3 rebound = cluster.finalPosition - (Vector3)(travel * 0.045f);
            sequence.Append(cluster.root.DOMove(rebound, 0.035f).SetEase(Ease.OutQuad));
            sequence.Append(cluster.root.DOMove(cluster.finalPosition, 0.055f).SetEase(Ease.OutCubic));
        }

        if (cluster.visualRoot != null && entryRumbleDegrees > 0f)
        {
            cluster.visualRoot
                .DOShakeRotation(
                    duration,
                    new Vector3(0f, 0f, entryRumbleDegrees),
                    Mathf.Max(1, entryRumbleVibrato),
                    18f,
                    false)
                .SetEase(Ease.Linear)
                .SetUpdate(true);
        }

        sequence.OnComplete(() =>
        {
            if (cluster.root == null)
                return;
            cluster.root.position = cluster.finalPosition;
            if (cluster.visualRoot != null)
                cluster.visualRoot.localRotation = Quaternion.identity;
            if (cluster.owner != null)
                cluster.ownerLastPosition = cluster.owner.transform.position;
        });

        cluster.sequence = sequence;
    }

    private void PlayExit(DecorCluster cluster, Vector2 requestedDirection, float requestedDuration)
    {
        if (cluster == null || cluster.exiting || cluster.root == null)
            return;

        cluster.exiting = true;
        cluster.sequence?.Kill(false);
        cluster.root.DOKill(false);
        if (cluster.visualRoot != null)
            cluster.visualRoot.DOKill(false);

        Vector2 direction = requestedDirection.sqrMagnitude > 0.001f
            ? Cardinalize(requestedDirection)
            : Cardinalize(cluster.outwardDirection);

        Bounds currentBounds = cluster.carrierBounds;
        currentBounds.center = cluster.root.position;
        float distance = ResolveOffscreenDistance(currentBounds, direction);
        Vector3 current = cluster.root.position;
        Vector3 anticipation = current - (Vector3)(direction * Mathf.Max(0.02f, exitAnticipationDistance));
        Vector3 destination = current + (Vector3)(direction * distance);
        float duration = Mathf.Max(0.20f, requestedDuration > 0.01f ? requestedDuration : fallbackExitDuration);

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Append(cluster.root.DOMove(anticipation, Mathf.Max(0.04f, exitAnticipationDuration)).SetEase(Ease.OutQuad));
        sequence.Append(cluster.root.DOMove(destination, duration).SetEase(Ease.InCubic));

        // 퇴장에서도 장비 자체는 뒤집지 않습니다. 아주 작은 기울기만 적용합니다.
        if (cluster.visualRoot != null)
        {
            float sign = direction.x + direction.y >= 0f ? -1f : 1f;
            cluster.visualRoot
                .DOLocalRotate(new Vector3(0f, 0f, sign * 1.4f), duration + exitAnticipationDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true);
        }

        sequence.OnComplete(() =>
        {
            cluster.sequence = null;
            if (cluster.rootObject != null)
                Destroy(cluster.rootObject);
        });

        cluster.sequence = sequence;
    }

    private void BeginExitAll(Vector2 preferredDirection, float duration)
    {
        for (int i = 0; i < clusters.Count; i++)
        {
            DecorCluster cluster = clusters[i];
            if (cluster == null || cluster.root == null || cluster.exiting)
                continue;
            PlayExit(
                cluster,
                preferredDirection.sqrMagnitude > 0.001f ? preferredDirection : cluster.outwardDirection,
                duration);
        }
    }

    private void CleanupDestroyedClusters()
    {
        for (int i = clusters.Count - 1; i >= 0; i--)
        {
            DecorCluster cluster = clusters[i];
            if (cluster == null || cluster.root == null)
                clusters.RemoveAt(i);
        }
    }

    private bool HasLivingClustersForScene(int sceneHandle)
    {
        for (int i = 0; i < clusters.Count; i++)
        {
            DecorCluster cluster = clusters[i];
            if (cluster != null && cluster.root != null && !cluster.exiting && cluster.sceneHandle == sceneHandle)
                return true;
        }
        return false;
    }

    private List<FloorSource> CollectFloorSources(Scene scene)
    {
        List<FloorSource> result = new();
        HashSet<int> rendererIds = new();

        BattleWalkableField[] fields = FindObjectsByType<BattleWalkableField>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < fields.Length; i++)
        {
            BattleWalkableField field = fields[i];
            if (field == null || field.gameObject.scene != scene)
                continue;
            SpriteRenderer renderer = field.GetComponent<SpriteRenderer>();
            if (renderer == null || !renderer.enabled || renderer.sprite == null)
                continue;
            AddFloorSource(result, rendererIds, renderer, ResolveOutermostMapBlock(renderer.transform));
        }

        if (result.Count > 0)
            return result;

        MapBlock[] blocks = FindObjectsByType<MapBlock>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (block == null || block.gameObject.scene != scene || !block.ContributesWalkableNavMesh)
                continue;

            SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                SpriteRenderer renderer = renderers[r];
                if (renderer == null || !renderer.enabled || renderer.sprite == null)
                    continue;
                string n = renderer.name;
                if (!n.StartsWith("Tile_", StringComparison.Ordinal) && renderer.GetComponent<BattleWalkableField>() == null)
                    continue;
                AddFloorSource(result, rendererIds, renderer, ResolveOutermostMapBlock(renderer.transform));
            }
        }

        if (result.Count > 0)
            return result;

        SpriteRenderer[] generic = FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < generic.Length; i++)
        {
            SpriteRenderer renderer = generic[i];
            if (renderer == null || renderer.gameObject.scene != scene || !renderer.enabled || renderer.sprite == null)
                continue;
            string n = renderer.name;
            if (!n.StartsWith("Tile_", StringComparison.Ordinal) &&
                !n.StartsWith("Floor_", StringComparison.Ordinal) &&
                !n.StartsWith("ShowTile_", StringComparison.Ordinal))
                continue;
            AddFloorSource(result, rendererIds, renderer, ResolveOutermostMapBlock(renderer.transform));
        }

        return result;
    }

    private static void AddFloorSource(
        List<FloorSource> output,
        HashSet<int> rendererIds,
        SpriteRenderer renderer,
        MapBlock owner)
    {
        if (renderer == null || !rendererIds.Add(renderer.GetInstanceID()))
            return;
        output.Add(new FloorSource { renderer = renderer, owner = owner, bounds = renderer.bounds });
    }

    private static MapBlock ResolveOutermostMapBlock(Transform source)
    {
        if (source == null)
            return null;
        MapBlock[] parents = source.GetComponentsInParent<MapBlock>(true);
        return parents == null || parents.Length == 0 ? null : parents[parents.Length - 1];
    }

    private static SpriteRenderer ResolveFloorTemplate(List<FloorSource> sources)
    {
        for (int i = 0; i < sources.Count; i++)
        {
            SpriteRenderer renderer = sources[i]?.renderer;
            if (renderer != null && renderer.sprite != null)
                return renderer;
        }
        return null;
    }

    private float ResolveCellWorldSize(SpriteRenderer template)
    {
        if (activeProfile != null)
            return activeProfile.CellWorldSize;

        if (template != null)
        {
            float inferred = Mathf.Min(
                Mathf.Max(0.01f, template.bounds.size.x),
                Mathf.Max(0.01f, template.bounds.size.y));
            if (inferred >= 0.25f && inferred <= 4f)
                return inferred;
        }

        return Mathf.Max(0.25f, defaultCellWorldSize);
    }

    private List<RuntimeDecorEntry> ResolveDecorEntries()
    {
        List<RuntimeDecorEntry> result = new();

        if (activeProfile != null && activeProfile.Entries != null)
        {
            IReadOnlyList<BattleUniversalStageDecorEntry> authored = activeProfile.Entries;
            for (int i = 0; i < authored.Count; i++)
            {
                BattleUniversalStageDecorEntry entry = authored[i];
                if (entry == null || !entry.HasVisual ||
                    entry.Category == BattleUniversalStageDecorCategory.Chair ||
                    entry.Category == BattleUniversalStageDecorCategory.Cable)
                    continue;

                // Light는 Base가 없는 단일 Sprite/Prefab을 독립 생성하지 않습니다.
                if (entry.Category == BattleUniversalStageDecorCategory.Light &&
                    (entry.LightRig == null || !entry.LightRig.IsValid))
                    continue;

                result.Add(new RuntimeDecorEntry
                {
                    label = entry.Label,
                    category = entry.Category,
                    sprite = entry.Sprite,
                    prefab = entry.Prefab,
                    lightRig = entry.LightRig,
                    footprint = entry.Category == BattleUniversalStageDecorCategory.Camera ||
                                entry.Category == BattleUniversalStageDecorCategory.Light
                        ? new Vector2Int(2, 2)
                        : entry.Footprint,
                    weight = entry.Weight,
                    localOffset = entry.LocalOffset,
                    localScale = entry.LocalScale,
                    sortingOrderOffset = entry.SortingOrderOffset,
                    randomFlipX = entry.RandomFlipX
                });
            }
        }

        if (result.Count > 0 || !autoDiscoverLoadedDecorSprites)
            return result;

        if (runtimeFallbackEntries.Count == 0)
            DiscoverLoadedDecorSprites(runtimeFallbackEntries);
        result.AddRange(runtimeFallbackEntries);
        return result;
    }

    private static void DiscoverLoadedDecorSprites(List<RuntimeDecorEntry> output)
    {
        Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        HashSet<int> used = new();

        for (int i = 0; i < sprites.Length && output.Count < 48; i++)
        {
            Sprite sprite = sprites[i];
            if (sprite == null || !used.Add(sprite.GetInstanceID()))
                continue;

            string lower = sprite.name.ToLowerInvariant();
            if (lower.Contains("chair") || lower.Contains("seat") ||
                lower.Contains("cable") || lower.Contains("wire") || lower.Contains("cord") ||
                lower.Contains("light") || lower.Contains("lamp") || lower.Contains("spot") ||
                lower.Contains("icon") || lower.Contains("ui_") || lower.Contains("button"))
                continue;

            if (!TryClassifySprite(lower, out BattleUniversalStageDecorCategory category, out Vector2Int footprint))
                continue;

            output.Add(new RuntimeDecorEntry
            {
                label = sprite.name,
                category = category,
                sprite = sprite,
                footprint = footprint,
                weight = 1,
                localScale = Vector2.one,
                sortingOrderOffset = 12,
                randomFlipX = category == BattleUniversalStageDecorCategory.Camera
            });
        }
    }

    private static bool TryClassifySprite(
        string lowerName,
        out BattleUniversalStageDecorCategory category,
        out Vector2Int footprint)
    {
        if (lowerName.Contains("camera") || lowerName.Contains("cam_") || lowerName.Contains("tripod_camera"))
        {
            category = BattleUniversalStageDecorCategory.Camera;
            footprint = new Vector2Int(2, 2);
            return true;
        }
        if (lowerName.Contains("monitor") || lowerName.Contains("screen"))
        {
            category = BattleUniversalStageDecorCategory.Monitor;
            footprint = new Vector2Int(2, 3);
            return true;
        }
        if (lowerName.Contains("case") || lowerName.Contains("crate") || lowerName.Contains("equipment"))
        {
            category = BattleUniversalStageDecorCategory.Case;
            footprint = Vector2Int.one;
            return true;
        }
        if (lowerName.Contains("fence") || lowerName.Contains("barrier"))
        {
            category = BattleUniversalStageDecorCategory.Fence;
            footprint = new Vector2Int(3, 3);
            return true;
        }

        category = default;
        footprint = Vector2Int.one;
        return false;
    }

    private static RuntimeDecorEntry PickEntry(
        List<RuntimeDecorEntry> entries,
        Vector2Int footprint,
        System.Random random)
    {
        List<RuntimeDecorEntry> pool = new();
        int totalWeight = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            RuntimeDecorEntry entry = entries[i];
            if (entry == null || !FootprintEquivalent(entry.footprint, footprint))
                continue;
            pool.Add(entry);
            totalWeight += Mathf.Max(1, entry.weight);
        }

        if (pool.Count == 0 || totalWeight <= 0)
            return null;

        int roll = random.Next(0, totalWeight);
        for (int i = 0; i < pool.Count; i++)
        {
            RuntimeDecorEntry entry = pool[i];
            roll -= Mathf.Max(1, entry.weight);
            if (roll < 0)
                return entry;
        }
        return pool[0];
    }

    private static bool FootprintEquivalent(Vector2Int a, Vector2Int b)
    {
        int aMin = Mathf.Min(a.x, a.y);
        int aMax = Mathf.Max(a.x, a.y);
        int bMin = Mathf.Min(b.x, b.y);
        int bMax = Mathf.Max(b.x, b.y);
        return aMin == bMin && aMax == bMax;
    }

    private static MapBlock ResolveClosestOwner(Vector3 target, List<FloorSource> sources)
    {
        MapBlock best = null;
        float bestDistance = float.PositiveInfinity;
        HashSet<int> seen = new();

        for (int i = 0; i < sources.Count; i++)
        {
            FloorSource source = sources[i];
            MapBlock owner = source?.owner;
            if (owner == null || !seen.Add(owner.GetInstanceID()))
                continue;
            float distance = (source.bounds.ClosestPoint(target) - target).sqrMagnitude;
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            best = owner;
        }
        return best;
    }

    private int ComputeFieldSignature(List<FloorSource> sources)
    {
        unchecked
        {
            int sum = sources.Count * 486187739;
            int xor = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                FloorSource source = sources[i];
                Bounds b = source.bounds;
                int h = 17;
                h = h * 31 + Mathf.RoundToInt(b.center.x * 10f);
                h = h * 31 + Mathf.RoundToInt(b.center.y * 10f);
                h = h * 31 + Mathf.RoundToInt(b.size.x * 10f);
                h = h * 31 + Mathf.RoundToInt(b.size.y * 10f);
                if (source.owner != null)
                    h = h * 31 + source.owner.GetInstanceID();
                sum += h;
                xor ^= h;
            }
            return sum ^ (xor * 16777619);
        }
    }

    private float ResolveOffscreenDistance(Bounds bounds, Vector2 direction)
    {
        Vector2 dir = Cardinalize(direction);
        float distance = Mathf.Max(8f, minimumOffscreenRail);
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic)
            return distance;

        float halfHeight = camera.orthographicSize;
        float halfWidth = halfHeight * Mathf.Max(0.1f, camera.aspect);
        Vector3 center = camera.transform.position;
        float margin = Mathf.Max(0.25f, offscreenMargin);

        if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y))
        {
            if (dir.x >= 0f)
                distance = Mathf.Max(distance, center.x + halfWidth + margin - bounds.min.x);
            else
                distance = Mathf.Max(distance, bounds.max.x - (center.x - halfWidth - margin));
        }
        else
        {
            if (dir.y >= 0f)
                distance = Mathf.Max(distance, center.y + halfHeight + margin - bounds.min.y);
            else
                distance = Mathf.Max(distance, bounds.max.y - (center.y - halfHeight - margin));
        }
        return Mathf.Max(0.5f, distance);
    }

    private static Vector3 ResolveContactPoint(Bounds bounds, Vector2 travelDirection)
    {
        Vector2 direction = travelDirection.sqrMagnitude > 0.001f ? travelDirection.normalized : Vector2.down;
        float support = Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.y) * bounds.extents.y;
        return bounds.center + (Vector3)(direction * Mathf.Max(0f, support - 0.02f));
    }

    private static Vector2 Cardinalize(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return Vector2.right;
        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? (direction.x >= 0f ? Vector2.right : Vector2.left)
            : (direction.y >= 0f ? Vector2.up : Vector2.down);
    }

    private static int NextInclusive(System.Random random, int min, int max)
    {
        return max <= min ? min : random.Next(min, max + 1);
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        return max <= min ? min : min + (float)random.NextDouble() * (max - min);
    }

    private static float SnapToCell(float value, float cell)
    {
        float safe = Mathf.Max(0.01f, cell);
        return Mathf.Round(value / safe) * safe;
    }

    private static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Decor";
        return value.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
    }
}

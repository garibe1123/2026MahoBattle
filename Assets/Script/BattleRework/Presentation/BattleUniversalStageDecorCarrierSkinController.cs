using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Universal Stage Decor의 단일 Runtime Owner입니다.
/// Side Authoring 기준은 Field 왼쪽 설치 + 장비가 오른쪽을 바라보는 모습입니다.
/// 오른쪽 Side에서는 authored Part의 Position / Rotation / Sprite Flip / Light2D가 자동 Mirror됩니다.
/// Auto 배치는 가능한 4방향의 사용 횟수를 균등하게 맞추고, 같은 Visual이 같은 방향에 반복되면
/// 두 번째부터 원본/좌우반전을 번갈아 사용해 반복감을 줄입니다.
/// Field와 Decor 사이 간격은 1~3 Tile 범위에서 Decor마다 랜덤하게 선택됩니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30150)]
public sealed class BattleUniversalStageDecorCarrierSkinController : MonoBehaviour
{
    private const string ClusterPrefix = "BattleDecorRuntime_";
    private const string VisualRootName = "Visual";
    private const string PartsRootName = "DecorParts";
    private const string FrameRootName = "DecorMechanicalFrame";
    private const string CarrierTilePrefix = "DecorCarrierTile_";
    private const string LightObjectName = "RuntimeLight2D";

    private static readonly FieldInfo LightSortingLayersField = typeof(Light2D).GetField(
        "m_ApplyToSortingLayers",
        BindingFlags.Instance | BindingFlags.NonPublic);
    private static bool warnedRuntimeLightFailure;

    [Header("BATTLE DECOR DESIGNS")]
    [SerializeField] private List<BattleDecorSO> battleDecorDesigns = new();

    [Header("PLACEMENT")]
    [Tooltip("Field 외곽에서 Decor까지 떨어질 최소 거리입니다. Tile 단위이며 1~3 범위에서 사용합니다.")]
    [SerializeField, Range(1, 3)] private int minimumFieldGapTiles = 1;
    [Tooltip("Field 외곽에서 Decor까지 떨어질 최대 거리입니다. 각 Decor는 최소~최대 사이의 정수 Tile 거리를 랜덤하게 사용합니다.")]
    [SerializeField, Range(1, 3)] private int maximumFieldGapTiles = 3;
    [SerializeField, Range(0, 8)] private int minimumDecorCount = 2;
    [SerializeField, Range(0, 10)] private int maximumDecorCount = 4;
    [SerializeField, Range(0f, 1f)] private float placementPaddingTiles = 0.25f;
    [SerializeField, Range(8, 64)] private int placementAttempts = 28;
    [SerializeField, Min(0.25f)] private float fallbackCellWorldSize = 1f;

    [Header("DISTRIBUTION")]
    [Tooltip("Auto는 상/하/좌/우, Side는 좌/우 중 현재 사용 횟수가 적은 방향부터 배치를 시도합니다.")]
    [SerializeField] private bool balanceAcrossAvailableDirections = true;
    [Tooltip("같은 대표 Sprite를 쓰는 Decor가 같은 방향에 반복되면 원본/좌우반전을 번갈아 사용합니다.")]
    [SerializeField] private bool alternateDuplicateHorizontalFlip = true;

    [Header("FIELD SCALED DECOR COUNT")]
    [SerializeField] private bool scaleDecorCountWithFloor = true;
    [SerializeField, Min(1f)] private float exposedEdgeTilesPerDecor = 5f;
    [SerializeField, Range(1, 32)] private int maximumScaledDecorCount = 16;
    [SerializeField, Range(0, 3)] private int scaledDecorCountJitter = 1;

    [Header("BUILD BUDGET")]
    [Tooltip("한 프레임에 실제 GameObject/SpriteRenderer/Light2D를 생성하거나 Pool에서 활성화할 Decor 수입니다.")]
    [SerializeField, Range(1, 8)] private int maxDecorCreatesPerFrame = 4;

    [Header("POOL")]
    [Tooltip("퇴장 완료 후 재사용을 위해 보관할 완성 Decor Cluster 최대 수입니다. 0이면 Pooling을 사용하지 않습니다.")]
    [SerializeField, Range(0, 64)] private int maxPooledDecorClusters = 24;

    [Header("ENTRY")]
    [SerializeField, Range(0.20f, 1.5f)] private float entryDuration = 0.58f;
    [SerializeField, Min(8f)] private float minimumOffscreenRail = 24f;
    [SerializeField, Range(0.25f, 4f)] private float offscreenMargin = 1.5f;
    [SerializeField, Range(0f, 2f)] private float entryRumbleDegrees = 0.38f;
    [SerializeField, Range(1, 20)] private int entryRumbleVibrato = 7;

    [Header("EXIT")]
    [SerializeField, Range(0.04f, 0.25f)] private float exitAnticipationDuration = 0.085f;
    [SerializeField, Range(0.02f, 0.30f)] private float exitAnticipationDistance = 0.10f;
    [SerializeField, Range(0.25f, 1.8f)] private float fallbackExitDuration = 0.58f;
    [SerializeField, Range(0.002f, 0.08f)] private float ownerExitMotionThreshold = 0.012f;
    [SerializeField, Range(0.05f, 0.5f)] private float transitionExitFallbackDelay = 0.16f;

    [Header("FIELD WATCH")]
    [SerializeField, Range(0.10f, 1.0f)] private float fieldScanInterval = 0.25f;
    [SerializeField, Min(0f)] private float stableFieldRescanInterval = 0f;
    [SerializeField, Range(0.05f, 1.0f)] private float postTransitionRebuildDelay = 0.12f;
    [SerializeField, Range(0.25f, 5f)] private float missingRoomManagerRetryInterval = 1f;

    private struct FloorSource
    {
        public SpriteRenderer renderer;
        public MapBlock owner;
        public Bounds bounds;
    }

    private struct DecorSpawnPlan
    {
        public BattleDecorSO design;
        public Vector3 target;
        public Vector2 outward;
        public Bounds carrierBounds;
        public Vector2Int footprint;
        public MapBlock owner;
        public int randomSeed;
        public bool variantFlipX;
    }

    private readonly struct DecorPoolKey : IEquatable<DecorPoolKey>
    {
        private readonly int designId;
        private readonly int materialId;
        private readonly int sourceSpriteId;
        private readonly int sortingLayerId;
        private readonly int sortingOrder;
        private readonly int colorRgba;
        private readonly int cellMilli;
        private readonly bool mirrorRight;
        private readonly bool variantFlipX;

        public DecorPoolKey(
            BattleDecorSO design,
            SpriteRenderer floorReference,
            float cell,
            Vector2 outward,
            bool flipVariantX)
        {
            designId = design != null ? design.GetInstanceID() : 0;
            materialId = floorReference != null && floorReference.sharedMaterial != null
                ? floorReference.sharedMaterial.GetInstanceID()
                : 0;
            sourceSpriteId = floorReference != null && floorReference.sprite != null
                ? floorReference.sprite.GetInstanceID()
                : 0;
            sortingLayerId = floorReference != null ? floorReference.sortingLayerID : 0;
            sortingOrder = floorReference != null ? floorReference.sortingOrder : 0;
            colorRgba = floorReference != null ? PackColor(floorReference.color) : 0;
            cellMilli = Mathf.RoundToInt(Mathf.Max(0.01f, cell) * 1000f);
            mirrorRight = Cardinalize(outward).x > 0.5f;
            variantFlipX = flipVariantX;
        }

        public bool Equals(DecorPoolKey other)
        {
            return designId == other.designId &&
                   materialId == other.materialId &&
                   sourceSpriteId == other.sourceSpriteId &&
                   sortingLayerId == other.sortingLayerId &&
                   sortingOrder == other.sortingOrder &&
                   colorRgba == other.colorRgba &&
                   cellMilli == other.cellMilli &&
                   mirrorRight == other.mirrorRight &&
                   variantFlipX == other.variantFlipX;
        }

        public override bool Equals(object obj)
        {
            return obj is DecorPoolKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + designId;
                hash = hash * 31 + materialId;
                hash = hash * 31 + sourceSpriteId;
                hash = hash * 31 + sortingLayerId;
                hash = hash * 31 + sortingOrder;
                hash = hash * 31 + colorRgba;
                hash = hash * 31 + cellMilli;
                hash = hash * 31 + (mirrorRight ? 1 : 0);
                hash = hash * 31 + (variantFlipX ? 1 : 0);
                return hash;
            }
        }
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
        public DecorPoolKey poolKey;
        public bool exiting;
        public bool pooled;
        public Sequence sequence;
    }

    private readonly List<DecorCluster> clusters = new();
    private readonly List<Bounds> placementBounds = new();
    private readonly List<BattleDecorSO> validDesigns = new();
    private readonly List<FloorSource> floorSourceBuffer = new();
    private readonly List<BattleDecorFloorSource> registeredFloorSourceBuffer = new();
    private readonly List<Bounds> floorBoundsBuffer = new();
    private readonly List<DecorSpawnPlan> spawnPlanBuffer = new();
    private readonly HashSet<int> ownerIdBuffer = new();
    private readonly Dictionary<DecorPoolKey, Stack<DecorCluster>> decorPool = new();
    private readonly int[] placementDirectionCounts = new int[4];
    private readonly List<Vector2> placementDirectionOrder = new(4);
    private readonly Dictionary<ulong, int> visualDirectionUseCounts = new();

    private BattleRoomManager roomManager;
    private BattleRunManager runManager;
    private Coroutine decorBuildRoutine;
    private bool decorBuildInProgress;
    private int pooledClusterCount;
    private float nextFieldScanAt;
    private float nextRoomManagerResolveAt;
    private int lastFieldSignature = int.MinValue;
    private bool rebuildPending;
    private float rebuildAt;
    private bool transitionStateInitialized;
    private bool lastTransitioning;
    private float transitionFallbackExitAt = float.PositiveInfinity;
    private bool stageRetirementRequested;
    private bool warnedNoValidDesigns;
    private bool warnedNoFloorSources;
    private bool warnedZeroDecorCount;
    private bool warnedNoPlacement;
    private int serial;

    public IReadOnlyList<BattleDecorSO> BattleDecorDesigns => battleDecorDesigns;
    public bool StageRetirementRequested => stageRetirementRequested;

    public bool HasActiveDecor
    {
        get
        {
            for (int i = 0; i < clusters.Count; i++)
            {
                DecorCluster cluster = clusters[i];
                if (cluster != null && cluster.root != null && !cluster.pooled)
                    return true;
            }
            return false;
        }
    }

    public void RequestStageRetirement()
    {
        stageRetirementRequested = true;
        rebuildPending = false;
        transitionFallbackExitAt = float.PositiveInfinity;
        CancelDecorBuild();
        BeginExitAll(fallbackExitDuration);
    }

    public void ReleaseStageRetirementGate()
    {
        stageRetirementRequested = false;
        nextFieldScanAt = 0f;
        lastFieldSignature = int.MinValue;
        rebuildPending = true;
        // Runtime Show Floor marker가 Hierarchy 변경을 반영할 한 프레임을 확보합니다.
        rebuildAt = Time.unscaledTime + 0.02f;
    }

    private void Awake()
    {
        ResolveRoomManager();
        ResolveRunManager();
        RefreshDesignCache();
    }

    private void OnEnable()
    {
        ResolveRoomManager();
        ResolveRunManager();
        RefreshDesignCache();
        nextFieldScanAt = 0f;
        nextRoomManagerResolveAt = 0f;
        lastFieldSignature = int.MinValue;
        rebuildPending = true;
        rebuildAt = Time.unscaledTime + 0.10f;
        transitionStateInitialized = roomManager != null;
        lastTransitioning = roomManager != null && roomManager.IsTransitioning;
        transitionFallbackExitAt = float.PositiveInfinity;
        stageRetirementRequested = false;
        decorBuildRoutine = null;
        decorBuildInProgress = false;
        spawnPlanBuffer.Clear();
        ResetDistributionState();
        warnedNoValidDesigns = false;
        warnedNoFloorSources = false;
        warnedZeroDecorCount = false;
        warnedNoPlacement = false;
    }

    private void OnDisable()
    {
        CancelDecorBuild();
        for (int i = clusters.Count - 1; i >= 0; i--)
        {
            DecorCluster cluster = clusters[i];
            if (cluster == null)
                continue;
            cluster.sequence?.Kill(false);
            if (cluster.root != null)
                cluster.root.DOKill(false);
            if (cluster.rootObject != null)
                Destroy(cluster.rootObject);
        }
        clusters.Clear();
        ClearDecorPool();
        placementBounds.Clear();
        floorSourceBuffer.Clear();
        registeredFloorSourceBuffer.Clear();
        floorBoundsBuffer.Clear();
        spawnPlanBuffer.Clear();
        ownerIdBuffer.Clear();
        ResetDistributionState();
        stageRetirementRequested = false;
    }

    private void OnValidate()
    {
        minimumFieldGapTiles = Mathf.Clamp(minimumFieldGapTiles, 1, 3);
        maximumFieldGapTiles = Mathf.Clamp(maximumFieldGapTiles, minimumFieldGapTiles, 3);
        minimumDecorCount = Mathf.Max(0, minimumDecorCount);
        maximumDecorCount = Mathf.Max(minimumDecorCount, maximumDecorCount);
        exposedEdgeTilesPerDecor = Mathf.Max(1f, exposedEdgeTilesPerDecor);
        maximumScaledDecorCount = Mathf.Max(minimumDecorCount, maximumScaledDecorCount);
        scaledDecorCountJitter = Mathf.Clamp(scaledDecorCountJitter, 0, 3);
        maxDecorCreatesPerFrame = Mathf.Clamp(maxDecorCreatesPerFrame, 1, 8);
        maxPooledDecorClusters = Mathf.Clamp(maxPooledDecorClusters, 0, 64);
        fallbackCellWorldSize = Mathf.Max(0.25f, fallbackCellWorldSize);
        placementAttempts = Mathf.Clamp(placementAttempts, 8, 64);
        fieldScanInterval = Mathf.Max(0.10f, fieldScanInterval);
        stableFieldRescanInterval = Mathf.Max(0f, stableFieldRescanInterval);
        missingRoomManagerRetryInterval = Mathf.Max(0.25f, missingRoomManagerRetryInterval);

        if (Application.isPlaying && !stageRetirementRequested)
        {
            RefreshDesignCache();
            QueueRebuild(0.05f, true);
        }
    }

    private void Update()
    {
        CleanupDestroyedClusters();
        if (stageRetirementRequested)
        {
            if (runManager == null)
                ResolveRunManager();
            if (runManager != null && runManager.IsInStartArea)
                ReleaseStageRetirementGate();
            else
                return;
        }

        MonitorClusterOwners();
        UpdateRoomTransitionState();
        float now = Time.unscaledTime;
        bool transitioning = roomManager != null && roomManager.IsTransitioning;
        if (transitioning)
            return;

        if (rebuildPending && now >= rebuildAt)
        {
            rebuildPending = false;
            ReconcileCurrentField(true);
            return;
        }

        bool hasLivingDecor = HasLivingClusters();
        if (hasLivingDecor)
        {
            if (stableFieldRescanInterval <= 0f || now < nextFieldScanAt)
                return;
            nextFieldScanAt = now + stableFieldRescanInterval;
            ReconcileCurrentField(false);
            return;
        }

        if (decorBuildInProgress || clusters.Count > 0 || now < nextFieldScanAt)
            return;

        nextFieldScanAt = now + Mathf.Max(0.10f, fieldScanInterval);
        if (roomManager == null && now >= nextRoomManagerResolveAt)
        {
            ResolveRoomManager();
            nextRoomManagerResolveAt = now + missingRoomManagerRetryInterval;
        }
        ReconcileCurrentField(false);
    }

    private void ResolveRoomManager()
    {
        roomManager = null;
        BattleRoomManager[] managers = FindObjectsByType<BattleRoomManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            BattleRoomManager candidate = managers[i];
            if (candidate != null && candidate.gameObject.scene == gameObject.scene)
            {
                roomManager = candidate;
                return;
            }
        }
    }

    private void ResolveRunManager()
    {
        runManager = null;
        BattleRunManager[] managers = FindObjectsByType<BattleRunManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            BattleRunManager candidate = managers[i];
            if (candidate != null && candidate.gameObject.scene == gameObject.scene)
            {
                runManager = candidate;
                return;
            }
        }
    }

    private void RefreshDesignCache()
    {
        validDesigns.Clear();
        if (battleDecorDesigns == null)
            return;
        for (int i = 0; i < battleDecorDesigns.Count; i++)
        {
            BattleDecorSO design = battleDecorDesigns[i];
            if (design != null && design.HasVisual)
                validDesigns.Add(design);
        }
    }

    private void QueueRebuild(float delay, bool forceSignatureReset)
    {
        if (stageRetirementRequested)
            return;
        rebuildPending = true;
        rebuildAt = Time.unscaledTime + Mathf.Max(0f, delay);
        if (forceSignatureReset)
            lastFieldSignature = int.MinValue;
    }

    private void CancelDecorBuild()
    {
        if (decorBuildRoutine != null)
            StopCoroutine(decorBuildRoutine);
        decorBuildRoutine = null;
        decorBuildInProgress = false;
        spawnPlanBuffer.Clear();
    }

    private void UpdateRoomTransitionState()
    {
        if (roomManager == null)
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
                BeginExitAll(fallbackExitDuration);
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
        nextFieldScanAt = 0f;
        QueueRebuild(postTransitionRebuildDelay, true);
    }

    private void MonitorClusterOwners()
    {
        float threshold = Mathf.Max(0.002f, ownerExitMotionThreshold);
        float thresholdSqr = threshold * threshold;
        for (int i = 0; i < clusters.Count; i++)
        {
            DecorCluster cluster = clusters[i];
            if (cluster == null || cluster.pooled || cluster.exiting || cluster.root == null)
                continue;
            if (cluster.owner == null)
            {
                if (roomManager != null && roomManager.IsTransitioning)
                    PlayExit(cluster, fallbackExitDuration);
                continue;
            }
            if (!cluster.owner.gameObject.activeInHierarchy)
            {
                PlayExit(cluster, cluster.owner.ExitDuration);
                continue;
            }
            Vector3 current = cluster.owner.transform.position;
            Vector3 delta = current - cluster.ownerLastPosition;
            cluster.ownerLastPosition = current;
            if (delta.sqrMagnitude > thresholdSqr)
                PlayExit(cluster, cluster.owner.ExitDuration);
        }
    }

    private void ReconcileCurrentField(bool force)
    {
        if (stageRetirementRequested)
            return;

        if (decorBuildInProgress)
        {
            if (!force)
                return;
            CancelDecorBuild();
        }

        RefreshDesignCache();
        if (validDesigns.Count == 0)
        {
            if (!warnedNoValidDesigns)
            {
                warnedNoValidDesigns = true;
                Debug.LogWarning("[BattleDecor] Decor를 생성하지 못했습니다. Battle Decor Designs 또는 Sprite Part를 확인합니다.", this);
            }
            BeginExitAll(fallbackExitDuration);
            return;
        }
        warnedNoValidDesigns = false;

        List<FloorSource> sources = CollectFloorSources();
        if (sources.Count == 0)
        {
            if (!warnedNoFloorSources)
            {
                warnedNoFloorSources = true;
                Debug.LogWarning("[BattleDecor] 등록된 활성 Field/Floor source를 찾지 못했습니다.", this);
            }
            BeginExitAll(fallbackExitDuration);
            lastFieldSignature = int.MinValue;
            return;
        }
        warnedNoFloorSources = false;

        int signature = ComputeFieldSignature(sources);
        bool living = HasLivingClusters();
        if (!force && signature == lastFieldSignature && living)
            return;
        if (clusters.Count > 0)
        {
            BeginExitAll(fallbackExitDuration);
            QueueRebuild(fallbackExitDuration + exitAnticipationDuration + 0.08f, true);
            return;
        }
        BuildDressing(sources, signature);
    }

    private void BuildDressing(List<FloorSource> sources, int signature)
    {
        if (stageRetirementRequested || decorBuildInProgress || sources == null || sources.Count == 0 || validDesigns.Count == 0)
            return;

        Bounds fieldBounds = sources[0].bounds;
        for (int i = 1; i < sources.Count; i++)
            fieldBounds.Encapsulate(sources[i].bounds);

        SpriteRenderer floorReference = ResolveFloorReference(sources);
        if (floorReference == null)
            return;

        float cell = ResolveCellWorldSize(sources, floorReference);
        System.Random random = new(unchecked(gameObject.scene.handle * 73856093 ^ signature * 19349663 ^ Environment.TickCount));
        int targetCount = BattleDecorSpatialPlanner.ResolveTargetCount(
            CopyFloorBounds(sources), cell,
            minimumDecorCount, maximumDecorCount,
            scaleDecorCountWithFloor, exposedEdgeTilesPerDecor,
            maximumScaledDecorCount, scaledDecorCountJitter, random);
        placementBounds.Clear();
        spawnPlanBuffer.Clear();
        ResetDistributionState();

        if (targetCount <= 0)
        {
            if (!warnedZeroDecorCount)
            {
                warnedZeroDecorCount = true;
                Debug.LogWarning("[BattleDecor] 계산된 Decor Count가 0입니다.", this);
            }
            lastFieldSignature = signature;
            return;
        }

        warnedZeroDecorCount = false;
        for (int i = 0; i < targetCount; i++)
        {
            BattleDecorSO design = PickWeightedDesign(random);
            if (design == null)
                continue;

            Vector2Int footprint = design.Footprint;
            if (!TryResolvePlacement(
                    design,
                    fieldBounds,
                    footprint,
                    cell,
                    random,
                    out Vector3 target,
                    out Vector2 outward,
                    out Bounds carrierBounds))
            {
                continue;
            }

            int directionIndex = GetDirectionIndex(outward);
            placementDirectionCounts[directionIndex]++;

            MapBlock owner = ResolveClosestOwner(target, sources);
            spawnPlanBuffer.Add(new DecorSpawnPlan
            {
                design = design,
                target = target,
                outward = outward,
                carrierBounds = carrierBounds,
                footprint = footprint,
                owner = owner,
                randomSeed = random.Next(),
                variantFlipX = alternateDuplicateHorizontalFlip && ResolveDuplicateVariantFlip(design, outward)
            });
            placementBounds.Add(carrierBounds);
        }

        if (spawnPlanBuffer.Count == 0)
        {
            if (!warnedNoPlacement)
            {
                warnedNoPlacement = true;
                Debug.LogWarning("[BattleDecor] 배치 가능한 Decor 위치를 만들지 못했습니다.", this);
            }
            lastFieldSignature = signature;
            return;
        }

        warnedNoPlacement = false;
        lastFieldSignature = signature;
        if (stableFieldRescanInterval > 0f)
            nextFieldScanAt = Time.unscaledTime + stableFieldRescanInterval;

        decorBuildInProgress = true;
        Coroutine routine = StartCoroutine(BuildDressingRoutine(floorReference, cell));
        if (decorBuildInProgress)
            decorBuildRoutine = routine;
    }

    private IEnumerator BuildDressingRoutine(SpriteRenderer floorReference, float cell)
    {
        int budget = Mathf.Clamp(maxDecorCreatesPerFrame, 1, 8);
        int createdThisFrame = 0;

        for (int i = 0; i < spawnPlanBuffer.Count; i++)
        {
            if (stageRetirementRequested || floorReference == null)
                break;

            DecorSpawnPlan plan = spawnPlanBuffer[i];
            System.Random spawnRandom = new(plan.randomSeed);
            DecorCluster cluster = AcquireCluster(plan, cell, floorReference, spawnRandom);

            if (cluster != null)
            {
                clusters.Add(cluster);
                PlayEnter(cluster);
            }

            createdThisFrame++;
            if (createdThisFrame >= budget && i < spawnPlanBuffer.Count - 1)
            {
                createdThisFrame = 0;
                yield return null;
            }
        }

        spawnPlanBuffer.Clear();
        decorBuildInProgress = false;
        decorBuildRoutine = null;
    }

    private BattleDecorSO PickWeightedDesign(System.Random random)
    {
        int totalWeight = 0;
        for (int i = 0; i < validDesigns.Count; i++)
            totalWeight += Mathf.Max(1, validDesigns[i].Weight);
        if (totalWeight <= 0)
            return null;
        int roll = random.Next(0, totalWeight);
        for (int i = 0; i < validDesigns.Count; i++)
        {
            BattleDecorSO design = validDesigns[i];
            roll -= Mathf.Max(1, design.Weight);
            if (roll < 0)
                return design;
        }
        return validDesigns[validDesigns.Count - 1];
    }

    private bool TryResolvePlacement(
        BattleDecorSO design,
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
        if (design == null)
            return false;

        float width = Mathf.Max(cell, footprint.x * cell);
        float height = Mathf.Max(cell, footprint.y * cell);
        int attempts = Mathf.Clamp(placementAttempts, 8, 64);
        Vector2 authoredOffset = design.AttachOffsetTiles * cell;
        int minGapTiles = Mathf.Clamp(minimumFieldGapTiles, 1, 3);
        int maxGapTiles = Mathf.Clamp(maximumFieldGapTiles, minGapTiles, 3);

        BuildPlacementDirectionOrder(design.AttachSide, random);
        if (placementDirectionOrder.Count == 0)
            return false;

        int attemptsPerDirection = Mathf.Max(2, Mathf.CeilToInt(attempts / (float)placementDirectionOrder.Count));
        for (int directionIndex = 0; directionIndex < placementDirectionOrder.Count; directionIndex++)
        {
            outward = placementDirectionOrder[directionIndex];

            for (int attempt = 0; attempt < attemptsPerDirection; attempt++)
            {
                float sidePosition = design.UseFixedAttachPosition
                    ? design.AttachPosition01
                    : (float)random.NextDouble();
                int gapTiles = random.Next(minGapTiles, maxGapTiles + 1);
                float gap = gapTiles * cell;

                if (Mathf.Abs(outward.x) > 0.5f)
                {
                    float y = SnapToCell(Mathf.Lerp(field.min.y, field.max.y, sidePosition), cell);
                    target = new Vector3(
                        outward.x < 0f ? field.min.x - gap - width * 0.5f : field.max.x + gap + width * 0.5f,
                        y,
                        field.center.z);
                }
                else
                {
                    float x = SnapToCell(Mathf.Lerp(field.min.x, field.max.x, sidePosition), cell);
                    target = new Vector3(
                        x,
                        outward.y < 0f ? field.min.y - gap - height * 0.5f : field.max.y + gap + height * 0.5f,
                        field.center.z);
                }

                Vector2 placementOffset = authoredOffset;
                if (outward.x > 0.5f)
                    placementOffset.x = -placementOffset.x;
                target += new Vector3(placementOffset.x, placementOffset.y, 0f);

                candidateBounds = new Bounds(target, new Vector3(width, height, 0.20f));
                if (!OverlapsExistingPlacement(candidateBounds, cell))
                    return true;
            }
        }

        return false;
    }

    private void BuildPlacementDirectionOrder(BattleDecorAttachSide side, System.Random random)
    {
        placementDirectionOrder.Clear();
        switch (side)
        {
            case BattleDecorAttachSide.Top:
                placementDirectionOrder.Add(Vector2.up);
                break;
            case BattleDecorAttachSide.Bottom:
                placementDirectionOrder.Add(Vector2.down);
                break;
            case BattleDecorAttachSide.Side:
                placementDirectionOrder.Add(Vector2.left);
                placementDirectionOrder.Add(Vector2.right);
                break;
            default:
                placementDirectionOrder.Add(Vector2.left);
                placementDirectionOrder.Add(Vector2.right);
                placementDirectionOrder.Add(Vector2.up);
                placementDirectionOrder.Add(Vector2.down);
                break;
        }

        ShuffleDirections(placementDirectionOrder, random);
        if (!balanceAcrossAvailableDirections || placementDirectionOrder.Count <= 1)
            return;

        // 먼저 Shuffle해 동률일 때 특정 방향이 항상 앞서는 편향을 제거한 뒤,
        // 현재 Stage에서 실제 사용 횟수가 적은 방향부터 시도합니다.
        for (int i = 0; i < placementDirectionOrder.Count - 1; i++)
        {
            int best = i;
            int bestCount = placementDirectionCounts[GetDirectionIndex(placementDirectionOrder[i])];
            for (int j = i + 1; j < placementDirectionOrder.Count; j++)
            {
                int candidateCount = placementDirectionCounts[GetDirectionIndex(placementDirectionOrder[j])];
                if (candidateCount >= bestCount)
                    continue;
                best = j;
                bestCount = candidateCount;
            }

            if (best == i)
                continue;
            Vector2 temp = placementDirectionOrder[i];
            placementDirectionOrder[i] = placementDirectionOrder[best];
            placementDirectionOrder[best] = temp;
        }
    }

    private static void ShuffleDirections(List<Vector2> directions, System.Random random)
    {
        if (directions == null || random == null)
            return;

        for (int i = directions.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(0, i + 1);
            Vector2 temp = directions[i];
            directions[i] = directions[swapIndex];
            directions[swapIndex] = temp;
        }
    }

    private bool ResolveDuplicateVariantFlip(BattleDecorSO design, Vector2 outward)
    {
        int visualId = ResolvePrimaryVisualId(design);
        int directionIndex = GetDirectionIndex(outward);
        ulong key = ((ulong)(uint)visualId << 3) | (uint)directionIndex;

        visualDirectionUseCounts.TryGetValue(key, out int useCount);
        visualDirectionUseCounts[key] = useCount + 1;
        return (useCount & 1) == 1;
    }

    private static int ResolvePrimaryVisualId(BattleDecorSO design)
    {
        if (design == null)
            return 0;

        IReadOnlyList<BattleDecorPart> parts = design.Parts;
        if (parts != null)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                BattleDecorPart part = parts[i];
                if (part == null || part.IsStandaloneLight || part.Sprite == null)
                    continue;
                return part.Sprite.GetInstanceID();
            }
        }

        return design.GetInstanceID();
    }

    private void ResetDistributionState()
    {
        for (int i = 0; i < placementDirectionCounts.Length; i++)
            placementDirectionCounts[i] = 0;
        placementDirectionOrder.Clear();
        visualDirectionUseCounts.Clear();
    }

    private static int GetDirectionIndex(Vector2 direction)
    {
        Vector2 cardinal = Cardinalize(direction);
        if (cardinal.x < -0.5f)
            return 0; // Left
        if (cardinal.x > 0.5f)
            return 1; // Right
        if (cardinal.y > 0.5f)
            return 2; // Up
        return 3; // Down
    }

    private bool OverlapsExistingPlacement(Bounds candidate, float cell)
    {
        float padding = Mathf.Max(0f, placementPaddingTiles) * cell;
        for (int i = 0; i < placementBounds.Count; i++)
        {
            Bounds other = placementBounds[i];
            bool overlapX = Mathf.Abs(candidate.center.x - other.center.x) < candidate.extents.x + other.extents.x + padding;
            bool overlapY = Mathf.Abs(candidate.center.y - other.center.y) < candidate.extents.y + other.extents.y + padding;
            if (overlapX && overlapY)
                return true;
        }
        return false;
    }

    private DecorCluster AcquireCluster(
        DecorSpawnPlan plan,
        float cell,
        SpriteRenderer floorReference,
        System.Random random)
    {
        if (plan.design == null || floorReference == null)
            return null;

        DecorPoolKey key = new(plan.design, floorReference, cell, plan.outward, plan.variantFlipX);
        if (decorPool.TryGetValue(key, out Stack<DecorCluster> stack))
        {
            while (stack.Count > 0)
            {
                DecorCluster pooled = stack.Pop();
                pooledClusterCount = Mathf.Max(0, pooledClusterCount - 1);
                if (pooled == null || pooled.rootObject == null || pooled.root == null)
                    continue;

                PrepareClusterForUse(pooled, plan, key);
                return pooled;
            }
        }

        return CreateCluster(
            plan.design,
            plan.target,
            plan.outward,
            plan.carrierBounds,
            plan.footprint,
            cell,
            floorReference,
            plan.owner,
            random,
            plan.variantFlipX,
            key);
    }

    private void PrepareClusterForUse(DecorCluster cluster, DecorSpawnPlan plan, DecorPoolKey key)
    {
        cluster.sequence?.Kill(false);
        cluster.sequence = null;
        cluster.root.DOKill(false);
        if (cluster.visualRoot != null)
            cluster.visualRoot.DOKill(false);

        cluster.root.SetParent(transform, true);
        cluster.root.position = plan.target;
        cluster.root.localRotation = Quaternion.identity;
        cluster.root.localScale = Vector3.one;
        if (cluster.visualRoot != null)
        {
            cluster.visualRoot.localRotation = Quaternion.identity;
            cluster.visualRoot.localScale = Vector3.one;
        }

        cluster.owner = plan.owner;
        cluster.ownerLastPosition = plan.owner != null ? plan.owner.transform.position : Vector3.zero;
        cluster.finalPosition = plan.target;
        cluster.outwardDirection = Cardinalize(plan.outward);
        cluster.carrierBounds = plan.carrierBounds;
        cluster.footprint = plan.footprint;
        cluster.poolKey = key;
        cluster.exiting = false;
        cluster.pooled = false;
        cluster.rootObject.SetActive(true);
    }

    private DecorCluster CreateCluster(
        BattleDecorSO design,
        Vector3 target,
        Vector2 outward,
        Bounds carrierBounds,
        Vector2Int footprint,
        float cell,
        SpriteRenderer floorReference,
        MapBlock owner,
        System.Random random,
        bool variantFlipX,
        DecorPoolKey poolKey)
    {
        if (design == null || floorReference == null)
            return null;
        GameObject rootObject = new($"{ClusterPrefix}{++serial:000}_{design.name}");
        rootObject.transform.SetParent(transform, true);
        rootObject.transform.position = target;
        GameObject visualObject = new(VisualRootName);
        visualObject.transform.SetParent(rootObject.transform, false);
        Transform visualRoot = visualObject.transform;

        int floorSorting = BuildCarrierFloor(visualRoot, design, footprint, cell, floorReference, random);
        if (design.FloorTemplate != null)
            BuildMechanicalFrame(visualRoot, design.FloorTemplate, footprint, cell, floorReference, floorSorting);
        BuildAuthoredParts(visualRoot, design, outward, variantFlipX, cell, floorReference, floorSorting);

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
            poolKey = poolKey,
            exiting = false,
            pooled = false
        };
    }

    private static int BuildCarrierFloor(Transform visualRoot, BattleDecorSO design, Vector2Int footprint, float cell,
        SpriteRenderer source, System.Random random)
    {
        BattleShowFloorTemplateSO template = design.FloorTemplate;
        Sprite[] variants = template != null ? template.FloorVariants : null;
        int floorSorting = source.sortingOrder;
        if (template != null)
        {
            int highestPart = Mathf.Max(template.UpperPlateSortingOrder, Mathf.Max(template.LowerPlateSortingOrder, template.HandleSortingOrder));
            floorSorting = Mathf.Max(source.sortingOrder, Mathf.Max(template.FloorSortingOrder, highestPart + 1));
        }
        float x0 = -(footprint.x - 1) * cell * 0.5f;
        float y0 = -(footprint.y - 1) * cell * 0.5f;
        for (int y = 0; y < footprint.y; y++)
        for (int x = 0; x < footprint.x; x++)
        {
            Sprite sprite = PickFloorSprite(variants, source.sprite, random);
            if (sprite == null)
                continue;
            GameObject tileObject = new($"{CarrierTilePrefix}{x}_{y}");
            tileObject.transform.SetParent(visualRoot, false);
            tileObject.transform.localPosition = new Vector3(x0 + x * cell, y0 + y * cell, 0f);
            SpriteRenderer renderer = tileObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = template != null ? template.FloorTint : source.color;
            renderer.sharedMaterial = source.sharedMaterial;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = floorSorting;
            ScaleRendererToSize(renderer, cell, cell);
        }
        return floorSorting;
    }

    private static void BuildAuthoredParts(
        Transform visualRoot,
        BattleDecorSO design,
        Vector2 outward,
        bool variantFlipX,
        float cell,
        SpriteRenderer floorReference,
        int floorSorting)
    {
        GameObject partsObject = new(PartsRootName);
        partsObject.transform.SetParent(visualRoot, false);
        Transform partsRoot = partsObject.transform;
        partsRoot.localScale = new Vector3(cell, cell, 1f);

        bool mirrorForRightSide = Cardinalize(outward).x > 0.5f;
        bool mirrorVisualHorizontally = mirrorForRightSide ^ variantFlipX;
        int targetSortingLayerId = floorReference.sortingLayerID;
        IReadOnlyList<BattleDecorPart> parts = design.Parts;
        if (parts == null)
            return;

        for (int i = 0; i < parts.Count; i++)
        {
            BattleDecorPart part = parts[i];
            if (part == null)
                continue;
            if (part.IsStandaloneLight)
            {
                try
                {
                    BuildStandaloneLight2D(partsRoot, part, mirrorVisualHorizontally, i, targetSortingLayerId);
                }
                catch (Exception exception)
                {
                    ReportRuntimeLightFailure(exception);
                }
                continue;
            }
            if (part.Sprite == null)
                continue;

            Vector2 authoredPosition = part.LocalPosition;
            float authoredRotation = part.RotationDegrees;
            if (mirrorVisualHorizontally)
            {
                authoredPosition.x = -authoredPosition.x;
                authoredRotation = -authoredRotation;
            }

            GameObject partObject = new($"Part_{i:00}_{SafeName(part.Label)}");
            partObject.transform.SetParent(partsRoot, false);
            partObject.transform.localPosition = new Vector3(authoredPosition.x, authoredPosition.y, -0.01f);
            partObject.transform.localRotation = Quaternion.Euler(0f, 0f, authoredRotation);
            Vector2 localScale = part.LocalScale;
            partObject.transform.localScale = new Vector3(localScale.x, localScale.y, 1f);
            SpriteRenderer renderer = partObject.AddComponent<SpriteRenderer>();
            renderer.sprite = part.Sprite;
            renderer.color = part.Tint;
            renderer.flipX = part.FlipX ^ mirrorVisualHorizontally;
            renderer.sharedMaterial = floorReference.sharedMaterial;
            renderer.sortingLayerID = floorReference.sortingLayerID;
            renderer.sortingOrder = floorSorting + part.SortingOffset;
            if (part.AddLight2D)
            {
                try
                {
                    BuildPartLight2D(partObject.transform, part, mirrorVisualHorizontally, targetSortingLayerId);
                }
                catch (Exception exception)
                {
                    ReportRuntimeLightFailure(exception);
                }
            }
        }
    }

    private static void BuildStandaloneLight2D(
        Transform partsRoot,
        BattleDecorPart part,
        bool mirrorHorizontally,
        int index,
        int targetSortingLayerId)
    {
        Vector2 position = part.LocalPosition;
        float rotation = part.LightRotationDegrees;
        if (mirrorHorizontally)
        {
            position.x = -position.x;
            rotation = -rotation;
        }
        GameObject lightObject = new($"{LightObjectName}_{index:00}_{SafeName(part.Label)}");
        lightObject.transform.SetParent(partsRoot, false);
        lightObject.transform.localPosition = new Vector3(position.x, position.y, -0.02f);
        lightObject.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
        ConfigureLight2D(lightObject.AddComponent<Light2D>(), part, targetSortingLayerId);
    }

    private static void BuildPartLight2D(
        Transform partTransform,
        BattleDecorPart part,
        bool mirrorHorizontally,
        int targetSortingLayerId)
    {
        GameObject lightObject = new(LightObjectName);
        lightObject.transform.SetParent(partTransform, false);
        Vector2 offset = part.LightLocalOffset;
        float rotation = part.LightRotationDegrees;
        if (mirrorHorizontally)
        {
            offset.x = -offset.x;
            rotation = -rotation;
        }
        lightObject.transform.localPosition = new Vector3(offset.x, offset.y, -0.02f);
        lightObject.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
        ConfigureLight2D(lightObject.AddComponent<Light2D>(), part, targetSortingLayerId);
    }

    private static void ConfigureLight2D(Light2D light, BattleDecorPart part, int targetSortingLayerId)
    {
        if (light == null || part == null)
            return;
        light.lightType = Light2D.LightType.Point;
        light.color = part.LightColor;
        light.intensity = part.LightIntensity;
        light.falloffIntensity = part.LightFalloffIntensity;
        light.pointLightOuterRadius = part.LightOuterRadius;
        light.pointLightInnerRadius = part.LightInnerRadius;
        light.pointLightOuterAngle = part.LightOuterAngle;
        light.pointLightInnerAngle = part.LightInnerAngle;
        light.blendStyleIndex = part.LightBlendStyleIndex;
        light.lightOrder = part.LightOrder;
        light.shadowsEnabled = part.LightShadowsEnabled;
        light.shadowIntensity = part.LightShadowIntensity;
        TryApplyTargetSortingLayer(light, targetSortingLayerId);
        light.enabled = true;
    }

    private static void TryApplyTargetSortingLayer(Light2D light, int targetSortingLayerId)
    {
        if (light == null || LightSortingLayersField == null)
            return;
        try
        {
            LightSortingLayersField.SetValue(light, new[] { targetSortingLayerId });
        }
        catch (Exception exception)
        {
            ReportRuntimeLightFailure(exception);
        }
    }

    private static void ReportRuntimeLightFailure(Exception exception)
    {
        if (warnedRuntimeLightFailure)
            return;
        warnedRuntimeLightFailure = true;
        Debug.LogError($"[BattleDecor] Runtime Light2D 생성/설정에 실패했습니다. Decor는 계속 생성합니다.\n{exception}");
    }

    private static void BuildMechanicalFrame(
        Transform visualRoot,
        BattleShowFloorTemplateSO template,
        Vector2Int footprint,
        float cell,
        SpriteRenderer reference,
        int floorSorting)
    {
        GameObject frameObject = new(FrameRootName);
        frameObject.transform.SetParent(visualRoot, false);
        Transform frame = frameObject.transform;
        float minCenterX = -(footprint.x - 1) * cell * 0.5f;
        float maxCenterX = (footprint.x - 1) * cell * 0.5f;
        float minCenterY = -(footprint.y - 1) * cell * 0.5f;
        float maxCenterY = (footprint.y - 1) * cell * 0.5f;
        int lowerSorting = Mathf.Min(floorSorting - 2, template.LowerPlateSortingOrder);
        int handleSorting = Mathf.Clamp(template.HandleSortingOrder, lowerSorting + 1, floorSorting - 1);
        for (int x = 0; x < footprint.x; x++)
        {
            Sprite lower = ResolveLowerPlateSprite(template, x, footprint.x);
            if (lower == null)
                continue;
            float px = footprint.x <= 1 ? 0f : Mathf.Lerp(minCenterX, maxCenterX, x / (float)(footprint.x - 1));
            CreateFramePart(frame, $"LowerPlate_{x}", lower, new Vector3(px, minCenterY - cell, 0f), reference, template.PlateTint, lowerSorting, cell);
        }
        if (template.HandlePlacement == BattleShowHandlePlacementMode.None)
            return;
        Sprite upper = template.UpperHandleSprite32 != null ? template.UpperHandleSprite32 : template.UpperPlateSprite32;
        CreateHorizontalFaceHandles(frame, "UpperHandle", upper, maxCenterY + cell, minCenterX, maxCenterX, reference, template.HandleTint, handleSorting, cell);
        CreateHorizontalFaceHandles(frame, "LowerHandle", template.LowerHandleSprite32, minCenterY - cell, minCenterX, maxCenterX, reference, template.HandleTint, handleSorting, cell);
        CreateVerticalFaceHandles(frame, "LeftHandle", template.LeftHandleSprite32, minCenterX - cell, minCenterY, maxCenterY, reference, template.HandleTint, handleSorting, cell);
        CreateVerticalFaceHandles(frame, "RightHandle", template.RightHandleSprite32, maxCenterX + cell, minCenterY, maxCenterY, reference, template.HandleTint, handleSorting, cell);
    }

    private void PlayEnter(DecorCluster cluster)
    {
        if (cluster == null || cluster.root == null)
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
            docking.AppendDockSettle(sequence, cluster.root, cluster.finalPosition, travel, ResolveContactPoint(cluster.carrierBounds, travel),
                Mathf.Lerp(0.45f, 0.76f, Mathf.InverseLerp(1f, 9f, area)), false, false);
        else
        {
            Vector3 rebound = cluster.finalPosition - (Vector3)(travel * 0.045f);
            sequence.Append(cluster.root.DOMove(rebound, 0.035f).SetEase(Ease.OutQuad));
            sequence.Append(cluster.root.DOMove(cluster.finalPosition, 0.055f).SetEase(Ease.OutCubic));
        }
        if (cluster.visualRoot != null && entryRumbleDegrees > 0f)
            cluster.visualRoot.DOShakeRotation(duration, new Vector3(0f, 0f, entryRumbleDegrees), Mathf.Max(1, entryRumbleVibrato), 18f, false)
                .SetEase(Ease.Linear).SetUpdate(true);
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

    /// <summary>
    /// Decor는 Entry 때 사용한 outward rail을 그대로 역방향으로 되짚어 화면 밖으로 복귀합니다.
    /// 전투/Show Floor는 Decor 퇴장 이후에 움직이므로 Exit에서 Floor 검색이나 경로 탐색을 하지 않습니다.
    /// </summary>
    private void PlayExit(DecorCluster cluster, float requestedDuration)
    {
        if (cluster == null || cluster.pooled || cluster.exiting || cluster.root == null)
            return;

        cluster.exiting = true;
        cluster.sequence?.Kill(false);
        cluster.root.DOKill(false);
        if (cluster.visualRoot != null)
            cluster.visualRoot.DOKill(false);

        Vector2 outward = Cardinalize(cluster.outwardDirection);
        Vector3 current = cluster.root.position;
        Bounds currentBounds = cluster.carrierBounds;
        currentBounds.center = current;
        float distance = ResolveOffscreenDistance(currentBounds, outward);
        Vector3 destination = current + (Vector3)(outward * distance);
        float duration = Mathf.Max(0.20f, requestedDuration > 0.01f ? requestedDuration : fallbackExitDuration);

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        if (exitAnticipationDistance > 0.001f && exitAnticipationDuration > 0.001f)
        {
            Vector3 anticipation = current - (Vector3)(outward * Mathf.Max(0.02f, exitAnticipationDistance));
            sequence.Append(
                cluster.root.DOMove(anticipation, Mathf.Max(0.04f, exitAnticipationDuration))
                    .SetEase(Ease.OutQuad));
        }

        sequence.Append(cluster.root.DOMove(destination, duration).SetEase(Ease.InCubic));

        if (cluster.visualRoot != null)
        {
            float sign = outward.x + outward.y >= 0f ? -1f : 1f;
            cluster.visualRoot
                .DOLocalRotate(new Vector3(0f, 0f, sign * 1.2f), duration + exitAnticipationDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true);
        }

        sequence.OnComplete(() =>
        {
            cluster.sequence = null;
            ReturnClusterToPool(cluster);
        });
        cluster.sequence = sequence;
    }

    private void BeginExitAll(float duration)
    {
        for (int i = 0; i < clusters.Count; i++)
        {
            DecorCluster cluster = clusters[i];
            if (cluster == null || cluster.pooled || cluster.root == null || cluster.exiting)
                continue;

            PlayExit(cluster, duration);
        }
    }

    private void ReturnClusterToPool(DecorCluster cluster)
    {
        if (cluster == null || cluster.rootObject == null)
            return;

        cluster.sequence?.Kill(false);
        cluster.sequence = null;
        if (cluster.root != null)
            cluster.root.DOKill(false);
        if (cluster.visualRoot != null)
        {
            cluster.visualRoot.DOKill(false);
            cluster.visualRoot.localRotation = Quaternion.identity;
        }

        clusters.Remove(cluster);
        cluster.owner = null;
        cluster.exiting = false;
        cluster.pooled = true;

        if (maxPooledDecorClusters <= 0 || pooledClusterCount >= maxPooledDecorClusters)
        {
            Destroy(cluster.rootObject);
            return;
        }

        cluster.rootObject.SetActive(false);
        if (!decorPool.TryGetValue(cluster.poolKey, out Stack<DecorCluster> stack))
        {
            stack = new Stack<DecorCluster>();
            decorPool.Add(cluster.poolKey, stack);
        }
        stack.Push(cluster);
        pooledClusterCount++;
    }

    private void ClearDecorPool()
    {
        foreach (KeyValuePair<DecorPoolKey, Stack<DecorCluster>> pair in decorPool)
        {
            Stack<DecorCluster> stack = pair.Value;
            if (stack == null)
                continue;
            while (stack.Count > 0)
            {
                DecorCluster cluster = stack.Pop();
                if (cluster != null && cluster.rootObject != null)
                    Destroy(cluster.rootObject);
            }
        }
        decorPool.Clear();
        pooledClusterCount = 0;
    }

    private List<Bounds> CopyFloorBounds(List<FloorSource> sources)
    {
        floorBoundsBuffer.Clear();
        if (sources != null)
            for (int i = 0; i < sources.Count; i++)
                floorBoundsBuffer.Add(sources[i].bounds);
        return floorBoundsBuffer;
    }

    private void CleanupDestroyedClusters()
    {
        for (int i = clusters.Count - 1; i >= 0; i--)
            if (clusters[i] == null || clusters[i].root == null)
                clusters.RemoveAt(i);
    }

    private bool HasLivingClusters()
    {
        for (int i = 0; i < clusters.Count; i++)
        {
            DecorCluster cluster = clusters[i];
            if (cluster != null && cluster.root != null && !cluster.pooled && !cluster.exiting)
                return true;
        }
        return false;
    }

    private List<FloorSource> CollectFloorSources()
    {
        floorSourceBuffer.Clear();
        BattleDecorFloorSource.CopyActive(registeredFloorSourceBuffer);

        for (int i = 0; i < registeredFloorSourceBuffer.Count; i++)
        {
            BattleDecorFloorSource source = registeredFloorSourceBuffer[i];
            if (source == null || source.gameObject.scene != gameObject.scene)
                continue;

            SpriteRenderer renderer = source.Renderer;
            if (renderer == null || !renderer.gameObject.activeInHierarchy || !renderer.enabled || renderer.sprite == null)
                continue;

            floorSourceBuffer.Add(new FloorSource
            {
                renderer = renderer,
                owner = source.Owner,
                bounds = renderer.bounds
            });
        }

        return floorSourceBuffer;
    }

    private static SpriteRenderer ResolveFloorReference(List<FloorSource> sources)
    {
        for (int i = 0; i < sources.Count; i++)
        {
            SpriteRenderer renderer = sources[i].renderer;
            if (renderer != null && renderer.sprite != null)
                return renderer;
        }
        return null;
    }

    private float ResolveCellWorldSize(List<FloorSource> sources, SpriteRenderer fallback)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < sources.Count; i++)
        {
            SpriteRenderer renderer = sources[i].renderer;
            if (renderer == null || renderer.sprite == null)
                continue;

            float size = Mathf.Min(Mathf.Abs(renderer.bounds.size.x), Mathf.Abs(renderer.bounds.size.y));
            if (size >= 0.20f && size <= 2f)
                best = Mathf.Min(best, size);
        }
        if (!float.IsInfinity(best))
            return best;

        if (fallback != null)
        {
            float size = Mathf.Min(Mathf.Abs(fallback.bounds.size.x), Mathf.Abs(fallback.bounds.size.y));
            if (size >= 0.25f && size <= 2f)
                return size;
        }
        return Mathf.Max(0.25f, fallbackCellWorldSize);
    }

    private MapBlock ResolveClosestOwner(Vector3 target, List<FloorSource> sources)
    {
        MapBlock best = null;
        float bestDistance = float.PositiveInfinity;
        ownerIdBuffer.Clear();
        for (int i = 0; i < sources.Count; i++)
        {
            FloorSource source = sources[i];
            MapBlock owner = source.owner;
            if (owner == null || !ownerIdBuffer.Add(owner.GetInstanceID()))
                continue;
            float distance = (source.bounds.ClosestPoint(target) - target).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = owner;
            }
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
            distance = dir.x >= 0f
                ? Mathf.Max(distance, center.x + halfWidth + margin - bounds.min.x)
                : Mathf.Max(distance, bounds.max.x - (center.x - halfWidth - margin));
        else
            distance = dir.y >= 0f
                ? Mathf.Max(distance, center.y + halfHeight + margin - bounds.min.y)
                : Mathf.Max(distance, bounds.max.y - (center.y - halfHeight - margin));
        return Mathf.Max(0.5f, distance);
    }

    private static Vector3 ResolveContactPoint(Bounds bounds, Vector2 travelDirection)
    {
        Vector2 direction = travelDirection.sqrMagnitude > 0.001f ? travelDirection.normalized : Vector2.down;
        float support = Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.y) * bounds.extents.y;
        return bounds.center + (Vector3)(direction * Mathf.Max(0f, support - 0.02f));
    }

    private static Sprite PickFloorSprite(Sprite[] variants, Sprite fallback, System.Random random)
    {
        if (variants == null || variants.Length == 0)
            return fallback;
        int start = random.Next(0, variants.Length);
        for (int i = 0; i < variants.Length; i++)
        {
            Sprite sprite = variants[(start + i) % variants.Length];
            if (sprite != null)
                return sprite;
        }
        return fallback;
    }

    private static Sprite ResolveLowerPlateSprite(BattleShowFloorTemplateSO template, int x, int columns)
    {
        if (template == null)
            return null;
        if (columns <= 1)
            return template.LowerPlateCenterSprite32 != null
                ? template.LowerPlateCenterSprite32
                : template.LowerPlateLeftSprite32 != null
                    ? template.LowerPlateLeftSprite32
                    : template.LowerPlateRightSprite32;
        if (x <= 0)
            return template.LowerPlateLeftSprite32 != null ? template.LowerPlateLeftSprite32 : template.LowerPlateCenterSprite32;
        if (x >= columns - 1)
            return template.LowerPlateRightSprite32 != null ? template.LowerPlateRightSprite32 : template.LowerPlateCenterSprite32;
        return template.LowerPlateCenterSprite32;
    }

    private static void CreateHorizontalFaceHandles(
        Transform parent,
        string prefix,
        Sprite sprite,
        float y,
        float leftX,
        float rightX,
        SpriteRenderer reference,
        Color tint,
        int sortingOrder,
        float cell)
    {
        if (sprite == null)
            return;
        if (Mathf.Abs(rightX - leftX) < cell * 0.5f)
        {
            CreateFramePart(parent, prefix, sprite, new Vector3(0f, y, 0f), reference, tint, sortingOrder, cell);
            return;
        }
        CreateFramePart(parent, prefix + "_L", sprite, new Vector3(leftX, y, 0f), reference, tint, sortingOrder, cell);
        CreateFramePart(parent, prefix + "_R", sprite, new Vector3(rightX, y, 0f), reference, tint, sortingOrder, cell);
    }

    private static void CreateVerticalFaceHandles(
        Transform parent,
        string prefix,
        Sprite sprite,
        float x,
        float bottomY,
        float topY,
        SpriteRenderer reference,
        Color tint,
        int sortingOrder,
        float cell)
    {
        if (sprite == null)
            return;
        if (Mathf.Abs(topY - bottomY) < cell * 0.5f)
        {
            CreateFramePart(parent, prefix, sprite, new Vector3(x, 0f, 0f), reference, tint, sortingOrder, cell);
            return;
        }
        CreateFramePart(parent, prefix + "_B", sprite, new Vector3(x, bottomY, 0f), reference, tint, sortingOrder, cell);
        CreateFramePart(parent, prefix + "_T", sprite, new Vector3(x, topY, 0f), reference, tint, sortingOrder, cell);
    }

    private static void CreateFramePart(
        Transform parent,
        string objectName,
        Sprite sprite,
        Vector3 localPosition,
        SpriteRenderer reference,
        Color tint,
        int sortingOrder,
        float cell)
    {
        if (parent == null || sprite == null)
            return;
        GameObject go = new(objectName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = tint;
        renderer.sharedMaterial = reference.sharedMaterial;
        renderer.sortingLayerID = reference.sortingLayerID;
        renderer.sortingOrder = sortingOrder;
        ScaleRendererToSize(renderer, cell, cell);
    }

    private static void ScaleRendererToSize(SpriteRenderer renderer, float width, float height)
    {
        if (renderer == null || renderer.sprite == null)
            return;
        Vector3 size = renderer.sprite.bounds.size;
        renderer.transform.localScale = new Vector3(
            width / Mathf.Max(0.001f, size.x),
            height / Mathf.Max(0.001f, size.y),
            1f);
    }

    private static Vector2 ResolveAttachDirection(BattleDecorAttachSide side, System.Random random)
    {
        switch (side)
        {
            case BattleDecorAttachSide.Top:
                return Vector2.up;
            case BattleDecorAttachSide.Side:
                return random.Next(0, 2) == 0 ? Vector2.left : Vector2.right;
            case BattleDecorAttachSide.Bottom:
                return Vector2.down;
            default:
                switch (random.Next(0, 4))
                {
                    case 0:
                        return Vector2.left;
                    case 1:
                        return Vector2.right;
                    case 2:
                        return Vector2.up;
                    default:
                        return Vector2.down;
                }
        }
    }

    private static Vector2 Cardinalize(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return Vector2.right;
        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? (direction.x >= 0f ? Vector2.right : Vector2.left)
            : (direction.y >= 0f ? Vector2.up : Vector2.down);
    }

    private static int PackColor(Color color)
    {
        Color32 c = color;
        unchecked
        {
            return c.r | (c.g << 8) | (c.b << 16) | (c.a << 24);
        }
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

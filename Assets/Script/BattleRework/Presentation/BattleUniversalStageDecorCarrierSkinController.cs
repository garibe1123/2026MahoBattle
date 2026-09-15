using System;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Universal Stage Decor의 단일 Runtime Owner입니다.
/// Side Authoring 기준은 Field 왼쪽 설치 + 장비가 오른쪽을 바라보는 모습입니다.
/// 오른쪽 Side에서는 authored Part의 Position / Rotation / Sprite Flip / Light2D가 자동 Mirror됩니다.
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

    private static bool warnedRuntimeLightFailure;

    [Header("BATTLE DECOR DESIGNS")]
    [SerializeField] private List<BattleDecorSO> battleDecorDesigns = new();

    [Header("PLACEMENT")]
    [SerializeField, Min(0f)] private float fieldGapTiles = 0f;
    [SerializeField, Range(0, 8)] private int minimumDecorCount = 2;
    [SerializeField, Range(0, 10)] private int maximumDecorCount = 4;
    [SerializeField, Range(0f, 1f)] private float placementPaddingTiles = 0.25f;
    [SerializeField, Range(8, 64)] private int placementAttempts = 28;
    [SerializeField, Min(0.25f)] private float fallbackCellWorldSize = 1f;

    [Header("FIELD SCALED DECOR COUNT")]
    [SerializeField] private bool scaleDecorCountWithFloor = true;
    [SerializeField, Min(1f)] private float exposedEdgeTilesPerDecor = 5f;
    [SerializeField, Range(1, 32)] private int maximumScaledDecorCount = 16;
    [SerializeField, Range(0, 3)] private int scaledDecorCountJitter = 1;

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

    [Header("SAFE EXIT PATH")]
    [SerializeField, Range(256, 8192)] private int exitPathSearchNodeLimit = 4096;
    [SerializeField, Range(0f, 0.25f)] private float exitCollisionClearanceTiles = 0.06f;

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
        public bool exiting;
        public Sequence sequence;
    }

    private readonly List<DecorCluster> clusters = new();
    private readonly List<Bounds> placementBounds = new();
    private readonly List<BattleDecorSO> validDesigns = new();
    private readonly List<FloorSource> floorSourceBuffer = new();
    private readonly HashSet<int> floorRendererIdBuffer = new();
    private readonly List<Bounds> floorBoundsBuffer = new();

    private BattleRoomManager roomManager;
    private BattleRunManager runManager;
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
    private bool warnedNoSafeExitRoute;
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
                if (cluster != null && cluster.root != null)
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
        BeginExitAll(Vector2.zero, fallbackExitDuration);
    }

    public void ReleaseStageRetirementGate()
    {
        stageRetirementRequested = false;
        nextFieldScanAt = 0f;
        lastFieldSignature = int.MinValue;
        rebuildPending = true;
        rebuildAt = Time.unscaledTime;
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
        warnedNoValidDesigns = false;
        warnedNoFloorSources = false;
        warnedZeroDecorCount = false;
        warnedNoPlacement = false;
        warnedNoSafeExitRoute = false;
    }

    private void OnDisable()
    {
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
        placementBounds.Clear();
        floorSourceBuffer.Clear();
        floorRendererIdBuffer.Clear();
        floorBoundsBuffer.Clear();
        stageRetirementRequested = false;
    }

    private void OnValidate()
    {
        fieldGapTiles = Mathf.Max(0f, fieldGapTiles);
        minimumDecorCount = Mathf.Max(0, minimumDecorCount);
        maximumDecorCount = Mathf.Max(minimumDecorCount, maximumDecorCount);
        exposedEdgeTilesPerDecor = Mathf.Max(1f, exposedEdgeTilesPerDecor);
        maximumScaledDecorCount = Mathf.Max(minimumDecorCount, maximumScaledDecorCount);
        scaledDecorCountJitter = Mathf.Clamp(scaledDecorCountJitter, 0, 3);
        exitPathSearchNodeLimit = Mathf.Clamp(exitPathSearchNodeLimit, 256, 8192);
        exitCollisionClearanceTiles = Mathf.Clamp(exitCollisionClearanceTiles, 0f, 0.25f);
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

        if (clusters.Count > 0 || now < nextFieldScanAt)
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
            if (cluster == null || cluster.exiting || cluster.root == null)
                continue;
            if (cluster.owner == null)
            {
                if (roomManager != null && roomManager.IsTransitioning)
                    PlayExit(cluster, cluster.outwardDirection, fallbackExitDuration);
                continue;
            }
            if (!cluster.owner.gameObject.activeInHierarchy)
            {
                PlayExit(cluster, cluster.outwardDirection, cluster.owner.ExitDuration);
                continue;
            }
            Vector3 current = cluster.owner.transform.position;
            Vector3 delta = current - cluster.ownerLastPosition;
            cluster.ownerLastPosition = current;
            if (delta.sqrMagnitude > thresholdSqr)
                PlayExit(cluster, Cardinalize(delta), cluster.owner.ExitDuration);
        }
    }

    private void ReconcileCurrentField(bool force)
    {
        if (stageRetirementRequested)
            return;
        RefreshDesignCache();
        if (validDesigns.Count == 0)
        {
            if (!warnedNoValidDesigns)
            {
                warnedNoValidDesigns = true;
                Debug.LogWarning("[BattleDecor] Decor를 생성하지 못했습니다. Battle Decor Designs 또는 Sprite Part를 확인합니다.", this);
            }
            BeginExitAll(Vector2.zero, fallbackExitDuration);
            return;
        }
        warnedNoValidDesigns = false;

        List<FloorSource> sources = CollectFloorSources();
        if (sources.Count == 0)
        {
            if (!warnedNoFloorSources)
            {
                warnedNoFloorSources = true;
                Debug.LogWarning("[BattleDecor] 활성 Field/Floor source를 찾지 못했습니다.", this);
            }
            BeginExitAll(Vector2.zero, fallbackExitDuration);
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
            BeginExitAll(Vector2.zero, fallbackExitDuration);
            QueueRebuild(fallbackExitDuration + exitAnticipationDuration + 0.08f, true);
            return;
        }
        BuildDressing(sources, signature);
    }

    private void BuildDressing(List<FloorSource> sources, int signature)
    {
        if (stageRetirementRequested || sources == null || sources.Count == 0 || validDesigns.Count == 0)
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
        int createdCount = 0;
        for (int i = 0; i < targetCount; i++)
        {
            BattleDecorSO design = PickWeightedDesign(random);
            if (design == null)
                continue;
            Vector2Int footprint = design.Footprint;
            if (!TryResolvePlacement(design, fieldBounds, footprint, cell, random,
                    out Vector3 target, out Vector2 outward, out Bounds carrierBounds))
                continue;

            MapBlock owner = ResolveClosestOwner(target, sources);
            DecorCluster cluster = CreateCluster(design, target, outward, carrierBounds, footprint, cell, floorReference, owner, random);
            if (cluster == null)
                continue;
            clusters.Add(cluster);
            placementBounds.Add(carrierBounds);
            PlayEnter(cluster);
            createdCount++;
        }

        if (createdCount == 0)
        {
            if (!warnedNoPlacement)
            {
                warnedNoPlacement = true;
                Debug.LogWarning("[BattleDecor] 배치 가능한 Decor 위치를 만들지 못했습니다.", this);
            }
        }
        else
        {
            warnedNoPlacement = false;
        }

        lastFieldSignature = signature;
        if (stableFieldRescanInterval > 0f)
            nextFieldScanAt = Time.unscaledTime + stableFieldRescanInterval;
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

    private bool TryResolvePlacement(BattleDecorSO design, Bounds field, Vector2Int footprint, float cell,
        System.Random random, out Vector3 target, out Vector2 outward, out Bounds candidateBounds)
    {
        target = Vector3.zero;
        outward = Vector2.zero;
        candidateBounds = default;
        if (design == null)
            return false;

        float width = Mathf.Max(cell, footprint.x * cell);
        float height = Mathf.Max(cell, footprint.y * cell);
        float gap = Mathf.Max(0f, fieldGapTiles * cell);
        int attempts = Mathf.Clamp(placementAttempts, 8, 64);
        Vector2 authoredOffset = design.AttachOffsetTiles * cell;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            outward = ResolveAttachDirection(design.AttachSide, random);
            float sidePosition = design.UseFixedAttachPosition ? design.AttachPosition01 : (float)random.NextDouble();
            if (Mathf.Abs(outward.x) > 0.5f)
            {
                float y = SnapToCell(Mathf.Lerp(field.min.y, field.max.y, sidePosition), cell);
                target = new Vector3(outward.x < 0f ? field.min.x - gap - width * 0.5f : field.max.x + gap + width * 0.5f, y, field.center.z);
            }
            else
            {
                float x = SnapToCell(Mathf.Lerp(field.min.x, field.max.x, sidePosition), cell);
                target = new Vector3(x, outward.y < 0f ? field.min.y - gap - height * 0.5f : field.max.y + gap + height * 0.5f, field.center.z);
            }
            Vector2 placementOffset = authoredOffset;
            if (outward.x > 0.5f)
                placementOffset.x = -placementOffset.x;
            target += new Vector3(placementOffset.x, placementOffset.y, 0f);
            candidateBounds = new Bounds(target, new Vector3(width, height, 0.20f));
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
            bool overlapX = Mathf.Abs(candidate.center.x - other.center.x) < candidate.extents.x + other.extents.x + padding;
            bool overlapY = Mathf.Abs(candidate.center.y - other.center.y) < candidate.extents.y + other.extents.y + padding;
            if (overlapX && overlapY)
                return true;
        }
        return false;
    }

    private DecorCluster CreateCluster(BattleDecorSO design, Vector3 target, Vector2 outward, Bounds carrierBounds,
        Vector2Int footprint, float cell, SpriteRenderer floorReference, MapBlock owner, System.Random random)
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
        BuildAuthoredParts(visualRoot, design, outward, cell, floorReference, floorSorting);

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
            footprint = footprint
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

    private static void BuildAuthoredParts(Transform visualRoot, BattleDecorSO design, Vector2 outward, float cell,
        SpriteRenderer floorReference, int floorSorting)
    {
        GameObject partsObject = new(PartsRootName);
        partsObject.transform.SetParent(visualRoot, false);
        Transform partsRoot = partsObject.transform;
        partsRoot.localScale = new Vector3(cell, cell, 1f);
        bool mirrorForRightSide = Cardinalize(outward).x > 0.5f;
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
                try { BuildStandaloneLight2D(partsRoot, part, mirrorForRightSide, i, targetSortingLayerId); }
                catch (Exception exception) { ReportRuntimeLightFailure(exception); }
                continue;
            }
            if (part.Sprite == null)
                continue;
            Vector2 authoredPosition = part.LocalPosition;
            float authoredRotation = part.RotationDegrees;
            if (mirrorForRightSide)
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
            renderer.flipX = part.FlipX ^ mirrorForRightSide;
            renderer.sharedMaterial = floorReference.sharedMaterial;
            renderer.sortingLayerID = floorReference.sortingLayerID;
            renderer.sortingOrder = floorSorting + part.SortingOffset;
            if (part.AddLight2D)
            {
                try { BuildPartLight2D(partObject.transform, part, mirrorForRightSide, targetSortingLayerId); }
                catch (Exception exception) { ReportRuntimeLightFailure(exception); }
            }
        }
    }

    private static void BuildStandaloneLight2D(Transform partsRoot, BattleDecorPart part, bool mirrorForRightSide,
        int index, int targetSortingLayerId)
    {
        Vector2 position = part.LocalPosition;
        float rotation = part.LightRotationDegrees;
        if (mirrorForRightSide) { position.x = -position.x; rotation = -rotation; }
        GameObject lightObject = new($"{LightObjectName}_{index:00}_{SafeName(part.Label)}");
        lightObject.transform.SetParent(partsRoot, false);
        lightObject.transform.localPosition = new Vector3(position.x, position.y, -0.02f);
        lightObject.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
        ConfigureLight2D(lightObject.AddComponent<Light2D>(), part, targetSortingLayerId);
    }

    private static void BuildPartLight2D(Transform partTransform, BattleDecorPart part, bool mirrorForRightSide, int targetSortingLayerId)
    {
        GameObject lightObject = new(LightObjectName);
        lightObject.transform.SetParent(partTransform, false);
        Vector2 offset = part.LightLocalOffset;
        float rotation = part.LightRotationDegrees;
        if (mirrorForRightSide) { offset.x = -offset.x; rotation = -rotation; }
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
        if (light == null)
            return;
        try
        {
            FieldInfo field = typeof(Light2D).GetField("m_ApplyToSortingLayers", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(light, new[] { targetSortingLayerId });
        }
        catch (Exception exception) { ReportRuntimeLightFailure(exception); }
    }

    private static void ReportRuntimeLightFailure(Exception exception)
    {
        if (warnedRuntimeLightFailure)
            return;
        warnedRuntimeLightFailure = true;
        Debug.LogError($"[BattleDecor] Runtime Light2D 생성/설정에 실패했습니다. Decor는 계속 생성합니다.\n{exception}");
    }

    private static void BuildMechanicalFrame(Transform visualRoot, BattleShowFloorTemplateSO template, Vector2Int footprint,
        float cell, SpriteRenderer reference, int floorSorting)
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
            if (cluster.root == null) return;
            cluster.root.position = cluster.finalPosition;
            if (cluster.visualRoot != null) cluster.visualRoot.localRotation = Quaternion.identity;
            if (cluster.owner != null) cluster.ownerLastPosition = cluster.owner.transform.position;
        });
        cluster.sequence = sequence;
    }

    private void PlayExit(DecorCluster cluster, Vector2 requestedDirection, float requestedDuration, IReadOnlyList<Bounds> preparedObstacles = null)
    {
        if (cluster == null || cluster.exiting || cluster.root == null)
            return;
        cluster.exiting = true;
        cluster.sequence?.Kill(false);
        cluster.root.DOKill(false);
        if (cluster.visualRoot != null)
            cluster.visualRoot.DOKill(false);

        Vector2 preferred = requestedDirection.sqrMagnitude > 0.001f ? Cardinalize(requestedDirection) : Cardinalize(cluster.outwardDirection);
        float cell = ResolveClusterCellSize(cluster);
        IReadOnlyList<Bounds> obstacles = preparedObstacles ?? CopyFloorBounds(CollectFloorSources());
        if (!BattleDecorSpatialPlanner.TryResolveShortestExitRoute(
                cluster.carrierBounds, cluster.root.position, preferred, obstacles, cell,
                Camera.main, offscreenMargin, exitPathSearchNodeLimit, exitCollisionClearanceTiles,
                out List<Vector3> route))
        {
            PlayExitFadeFallback(cluster, requestedDuration);
            return;
        }

        float duration = Mathf.Max(0.20f, requestedDuration > 0.01f ? requestedDuration : fallbackExitDuration);
        Vector3 current = cluster.root.position;
        Vector3 first = route.Count > 0 ? route[0] : current;
        Vector2 firstDirection = ((Vector2)(first - current)).sqrMagnitude > 0.001f ? ((Vector2)(first - current)).normalized : preferred;
        Sequence sequence = DOTween.Sequence().SetUpdate(true);

        Vector3 anticipation = current - (Vector3)(firstDirection * Mathf.Max(0.02f, exitAnticipationDistance));
        Bounds anticipationBounds = cluster.carrierBounds;
        anticipationBounds.center = anticipation;
        float clearance = exitCollisionClearanceTiles * cell;
        bool useAnticipation = BattleDecorSpatialPlanner.CanOccupy(anticipationBounds, obstacles, clearance);
        if (useAnticipation)
            sequence.Append(cluster.root.DOMove(anticipation, Mathf.Max(0.04f, exitAnticipationDuration)).SetEase(Ease.OutQuad));

        float totalDistance = 0f;
        Vector3 previous = useAnticipation ? anticipation : current;
        for (int i = 0; i < route.Count; i++)
        {
            totalDistance += Vector2.Distance(previous, route[i]);
            previous = route[i];
        }
        totalDistance = Mathf.Max(0.01f, totalDistance);
        previous = useAnticipation ? anticipation : current;
        for (int i = 0; i < route.Count; i++)
        {
            float segmentDistance = Mathf.Max(0.001f, Vector2.Distance(previous, route[i]));
            float segmentDuration = Mathf.Max(0.025f, duration * segmentDistance / totalDistance);
            sequence.Append(cluster.root.DOMove(route[i], segmentDuration).SetEase(i == route.Count - 1 ? Ease.InCubic : Ease.Linear));
            previous = route[i];
        }

        if (cluster.visualRoot != null)
        {
            float sign = firstDirection.x + firstDirection.y >= 0f ? -1f : 1f;
            cluster.visualRoot.DOLocalRotate(new Vector3(0f, 0f, sign * 1.2f), duration + exitAnticipationDuration)
                .SetEase(Ease.InQuad).SetUpdate(true);
        }
        sequence.OnComplete(() =>
        {
            cluster.sequence = null;
            if (cluster.rootObject != null) Destroy(cluster.rootObject);
        });
        cluster.sequence = sequence;
    }

    private void PlayExitFadeFallback(DecorCluster cluster, float requestedDuration)
    {
        if (!warnedNoSafeExitRoute)
        {
            warnedNoSafeExitRoute = true;
            Debug.LogWarning("[BattleDecor] 안전한 퇴장 경로가 없는 Decor는 타일을 관통하지 않고 제자리 Fade 제거합니다.", this);
        }
        float duration = Mathf.Clamp((requestedDuration > 0.01f ? requestedDuration : fallbackExitDuration) * 0.35f, 0.10f, 0.28f);
        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        SpriteRenderer[] renderers = cluster.root.GetComponentsInChildren<SpriteRenderer>(true);
        bool added = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled) continue;
            Tween fade = renderer.DOFade(0f, duration).SetEase(Ease.InQuad);
            if (!added) { sequence.Append(fade); added = true; }
            else sequence.Join(fade);
        }
        if (!added) sequence.AppendInterval(duration);
        sequence.OnComplete(() =>
        {
            cluster.sequence = null;
            if (cluster.rootObject != null) Destroy(cluster.rootObject);
        });
        cluster.sequence = sequence;
    }

    private void BeginExitAll(Vector2 preferredDirection, float duration)
    {
        IReadOnlyList<Bounds> obstacles = CopyFloorBounds(CollectFloorSources());
        for (int i = 0; i < clusters.Count; i++)
        {
            DecorCluster cluster = clusters[i];
            if (cluster == null || cluster.root == null || cluster.exiting)
                continue;
            Vector2 direction = preferredDirection.sqrMagnitude > 0.001f ? preferredDirection : cluster.outwardDirection;
            PlayExit(cluster, direction, duration, obstacles);
        }
    }

    private List<Bounds> CopyFloorBounds(List<FloorSource> sources)
    {
        floorBoundsBuffer.Clear();
        if (sources != null)
            for (int i = 0; i < sources.Count; i++) floorBoundsBuffer.Add(sources[i].bounds);
        return floorBoundsBuffer;
    }

    private static float ResolveClusterCellSize(DecorCluster cluster)
    {
        float x = cluster.carrierBounds.size.x / Mathf.Max(1, cluster.footprint.x);
        float y = cluster.carrierBounds.size.y / Mathf.Max(1, cluster.footprint.y);
        return Mathf.Max(0.25f, Mathf.Min(Mathf.Abs(x), Mathf.Abs(y)));
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
            if (cluster != null && cluster.root != null && !cluster.exiting)
                return true;
        }
        return false;
    }

    private List<FloorSource> CollectFloorSources()
    {
        floorSourceBuffer.Clear();
        floorRendererIdBuffer.Clear();
        BattleWalkableField[] fields = FindObjectsByType<BattleWalkableField>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < fields.Length; i++)
        {
            BattleWalkableField field = fields[i];
            if (field == null || field.gameObject.scene != gameObject.scene) continue;
            SpriteRenderer renderer = field.GetComponent<SpriteRenderer>();
            if (renderer == null || !renderer.enabled || renderer.sprite == null) continue;
            AddFloorSource(floorSourceBuffer, floorRendererIdBuffer, renderer, ResolveOutermostMapBlock(renderer.transform));
        }

        MapBlock[] blocks = FindObjectsByType<MapBlock>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (block == null || block.gameObject.scene != gameObject.scene || !block.ContributesWalkableNavMesh) continue;
            SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                SpriteRenderer renderer = renderers[r];
                if (renderer == null || !renderer.gameObject.activeInHierarchy || !renderer.enabled || renderer.sprite == null) continue;
                string n = renderer.name;
                if (!n.StartsWith("Tile_", StringComparison.Ordinal) && !n.StartsWith("ShowTile_", StringComparison.Ordinal) && renderer.GetComponent<BattleWalkableField>() == null) continue;
                AddFloorSource(floorSourceBuffer, floorRendererIdBuffer, renderer, ResolveOutermostMapBlock(renderer.transform));
            }
        }

        SpriteRenderer[] generic = FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < generic.Length; i++)
        {
            SpriteRenderer renderer = generic[i];
            if (renderer == null || renderer.gameObject.scene != gameObject.scene || !renderer.enabled || renderer.sprite == null) continue;
            string n = renderer.name;
            if (!n.StartsWith("Tile_", StringComparison.Ordinal) && !n.StartsWith("Floor_", StringComparison.Ordinal) &&
                !n.StartsWith("ShowTile_", StringComparison.Ordinal) && !n.StartsWith("BaseFloor_", StringComparison.Ordinal)) continue;
            AddFloorSource(floorSourceBuffer, floorRendererIdBuffer, renderer, ResolveOutermostMapBlock(renderer.transform));
        }
        return floorSourceBuffer;
    }

    private static void AddFloorSource(List<FloorSource> output, HashSet<int> rendererIds, SpriteRenderer renderer, MapBlock owner)
    {
        if (renderer == null || !rendererIds.Add(renderer.GetInstanceID())) return;
        output.Add(new FloorSource { renderer = renderer, owner = owner, bounds = renderer.bounds });
    }

    private static MapBlock ResolveOutermostMapBlock(Transform source)
    {
        if (source == null) return null;
        MapBlock[] parents = source.GetComponentsInParent<MapBlock>(true);
        return parents == null || parents.Length == 0 ? null : parents[parents.Length - 1];
    }

    private static SpriteRenderer ResolveFloorReference(List<FloorSource> sources)
    {
        for (int i = 0; i < sources.Count; i++)
        {
            SpriteRenderer renderer = sources[i].renderer;
            if (renderer != null && renderer.sprite != null) return renderer;
        }
        return null;
    }

    private float ResolveCellWorldSize(List<FloorSource> sources, SpriteRenderer fallback)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < sources.Count; i++)
        {
            SpriteRenderer renderer = sources[i].renderer;
            if (renderer == null || renderer.sprite == null) continue;
            string n = renderer.name;
            if (!n.StartsWith("Tile_", StringComparison.Ordinal) && !n.StartsWith("ShowTile_", StringComparison.Ordinal) && !n.StartsWith("BaseFloor_", StringComparison.Ordinal)) continue;
            float size = Mathf.Min(Mathf.Abs(renderer.bounds.size.x), Mathf.Abs(renderer.bounds.size.y));
            if (size >= 0.20f) best = Mathf.Min(best, size);
        }
        if (!float.IsInfinity(best) && best <= 4f) return best;
        if (fallback != null)
        {
            float size = Mathf.Min(Mathf.Abs(fallback.bounds.size.x), Mathf.Abs(fallback.bounds.size.y));
            if (size >= 0.25f && size <= 2f) return size;
        }
        return Mathf.Max(0.25f, fallbackCellWorldSize);
    }

    private static MapBlock ResolveClosestOwner(Vector3 target, List<FloorSource> sources)
    {
        MapBlock best = null;
        float bestDistance = float.PositiveInfinity;
        HashSet<int> seen = new();
        for (int i = 0; i < sources.Count; i++)
        {
            FloorSource source = sources[i];
            MapBlock owner = source.owner;
            if (owner == null || !seen.Add(owner.GetInstanceID())) continue;
            float distance = (source.bounds.ClosestPoint(target) - target).sqrMagnitude;
            if (distance < bestDistance) { bestDistance = distance; best = owner; }
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
                if (source.owner != null) h = h * 31 + source.owner.GetInstanceID();
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
        if (camera == null || !camera.orthographic) return distance;
        float halfHeight = camera.orthographicSize;
        float halfWidth = halfHeight * Mathf.Max(0.1f, camera.aspect);
        Vector3 center = camera.transform.position;
        float margin = Mathf.Max(0.25f, offscreenMargin);
        if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.y))
            distance = dir.x >= 0f ? Mathf.Max(distance, center.x + halfWidth + margin - bounds.min.x) : Mathf.Max(distance, bounds.max.x - (center.x - halfWidth - margin));
        else
            distance = dir.y >= 0f ? Mathf.Max(distance, center.y + halfHeight + margin - bounds.min.y) : Mathf.Max(distance, bounds.max.y - (center.y - halfHeight - margin));
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
        if (variants == null || variants.Length == 0) return fallback;
        int start = random.Next(0, variants.Length);
        for (int i = 0; i < variants.Length; i++)
        {
            Sprite sprite = variants[(start + i) % variants.Length];
            if (sprite != null) return sprite;
        }
        return fallback;
    }

    private static Sprite ResolveLowerPlateSprite(BattleShowFloorTemplateSO template, int x, int columns)
    {
        if (template == null) return null;
        if (columns <= 1)
            return template.LowerPlateCenterSprite32 != null ? template.LowerPlateCenterSprite32 : template.LowerPlateLeftSprite32 != null ? template.LowerPlateLeftSprite32 : template.LowerPlateRightSprite32;
        if (x <= 0) return template.LowerPlateLeftSprite32 != null ? template.LowerPlateLeftSprite32 : template.LowerPlateCenterSprite32;
        if (x >= columns - 1) return template.LowerPlateRightSprite32 != null ? template.LowerPlateRightSprite32 : template.LowerPlateCenterSprite32;
        return template.LowerPlateCenterSprite32;
    }

    private static void CreateHorizontalFaceHandles(Transform parent, string prefix, Sprite sprite, float y, float leftX, float rightX,
        SpriteRenderer reference, Color tint, int sortingOrder, float cell)
    {
        if (sprite == null) return;
        if (Mathf.Abs(rightX - leftX) < cell * 0.5f)
        {
            CreateFramePart(parent, prefix, sprite, new Vector3(0f, y, 0f), reference, tint, sortingOrder, cell);
            return;
        }
        CreateFramePart(parent, prefix + "_L", sprite, new Vector3(leftX, y, 0f), reference, tint, sortingOrder, cell);
        CreateFramePart(parent, prefix + "_R", sprite, new Vector3(rightX, y, 0f), reference, tint, sortingOrder, cell);
    }

    private static void CreateVerticalFaceHandles(Transform parent, string prefix, Sprite sprite, float x, float bottomY, float topY,
        SpriteRenderer reference, Color tint, int sortingOrder, float cell)
    {
        if (sprite == null) return;
        if (Mathf.Abs(topY - bottomY) < cell * 0.5f)
        {
            CreateFramePart(parent, prefix, sprite, new Vector3(x, 0f, 0f), reference, tint, sortingOrder, cell);
            return;
        }
        CreateFramePart(parent, prefix + "_B", sprite, new Vector3(x, bottomY, 0f), reference, tint, sortingOrder, cell);
        CreateFramePart(parent, prefix + "_T", sprite, new Vector3(x, topY, 0f), reference, tint, sortingOrder, cell);
    }

    private static void CreateFramePart(Transform parent, string objectName, Sprite sprite, Vector3 localPosition,
        SpriteRenderer reference, Color tint, int sortingOrder, float cell)
    {
        if (parent == null || sprite == null) return;
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
        if (renderer == null || renderer.sprite == null) return;
        Vector3 size = renderer.sprite.bounds.size;
        renderer.transform.localScale = new Vector3(width / Mathf.Max(0.001f, size.x), height / Mathf.Max(0.001f, size.y), 1f);
    }

    private static Vector2 ResolveAttachDirection(BattleDecorAttachSide side, System.Random random)
    {
        switch (side)
        {
            case BattleDecorAttachSide.Top: return Vector2.up;
            case BattleDecorAttachSide.Side: return random.Next(0, 2) == 0 ? Vector2.left : Vector2.right;
            case BattleDecorAttachSide.Bottom: return Vector2.down;
            default:
                switch (random.Next(0, 4))
                {
                    case 0: return Vector2.left;
                    case 1: return Vector2.right;
                    case 2: return Vector2.up;
                    default: return Vector2.down;
                }
        }
    }

    private static Vector2 Cardinalize(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.001f) return Vector2.right;
        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? (direction.x >= 0f ? Vector2.right : Vector2.left)
            : (direction.y >= 0f ? Vector2.up : Vector2.down);
    }

    private static float SnapToCell(float value, float cell)
    {
        float safe = Mathf.Max(0.01f, cell);
        return Mathf.Round(value / safe) * safe;
    }

    private static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Decor";
        return value.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
    }
}

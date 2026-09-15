using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Universal Stage Decor의 단일 Runtime Owner입니다.
///
/// Inspector에는 BattleDecorSO 리스트만 넣습니다.
/// 각 SO는 Floor / Frame / Camera / Light / Cable 등 완성된 데코 배치를 Prefab처럼 저장하며,
/// 이 Controller는 다음만 담당합니다.
/// - 현재 Field 외곽에 BattleDecorSO를 랜덤 선택/배치
/// - 화면 밖 Entry Rail에서 굴러와 정착
/// - 가까운 MapBlock이 빠질 때 같은 방향으로 자연 퇴장
/// - SO의 Floor Template으로 Carrier Floor / Mechanical Frame 생성
/// - SO Parts를 저장된 Position / Rotation / Scale / Sorting 그대로 조립
///
/// 별도의 SceneConfig / Profile / LightRig / Cable Runtime Manager는 사용하지 않습니다.
/// Runtime 랜덤 변형은 선택적으로 전체 좌우 Mirror만 허용하며 상하 반전/랜덤 180도 회전은 하지 않습니다.
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

    [Header("BATTLE DECOR DESIGNS")]
    [Tooltip("여기에 완성된 BattleDecorSO만 넣습니다. Camera / Light / Cable 등은 각 SO 내부 Parts에서 직접 조립합니다.")]
    [SerializeField] private List<BattleDecorSO> battleDecorDesigns = new();

    [Header("PLACEMENT")]
    [Tooltip("현재 Field와 Decor Carrier 사이에 비워둘 Floor 칸 수입니다. 0이면 Field Floor에 바로 붙습니다.")]
    [SerializeField, Min(0f)] private float fieldGapTiles = 0f;
    [SerializeField, Range(0, 8)] private int minimumDecorCount = 2;
    [SerializeField, Range(0, 10)] private int maximumDecorCount = 4;
    [SerializeField, Range(0f, 1f)] private float placementPaddingTiles = 0.25f;
    [SerializeField, Range(8, 64)] private int placementAttempts = 28;
    [SerializeField, Min(0.25f)] private float fallbackCellWorldSize = 1f;

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
    [SerializeField, Range(0.05f, 0.75f)] private float fieldScanInterval = 0.15f;
    [SerializeField, Range(0.05f, 1.0f)] private float postTransitionRebuildDelay = 0.12f;

    private sealed class FloorSource
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

    private BattleRoomManager roomManager;
    private float nextFieldScanAt;
    private int lastFieldSignature = int.MinValue;
    private bool rebuildPending;
    private float rebuildAt;
    private bool transitionStateInitialized;
    private bool lastTransitioning;
    private float transitionFallbackExitAt = float.PositiveInfinity;
    private int serial;

    public IReadOnlyList<BattleDecorSO> BattleDecorDesigns => battleDecorDesigns;

    private void Awake()
    {
        ResolveRoomManager();
        RefreshDesignCache();
    }

    private void OnEnable()
    {
        ResolveRoomManager();
        RefreshDesignCache();
        nextFieldScanAt = 0f;
        lastFieldSignature = int.MinValue;
        rebuildPending = true;
        rebuildAt = Time.unscaledTime + 0.10f;
        transitionStateInitialized = roomManager != null;
        lastTransitioning = roomManager != null && roomManager.IsTransitioning;
        transitionFallbackExitAt = float.PositiveInfinity;
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
    }

    private void OnValidate()
    {
        fieldGapTiles = Mathf.Max(0f, fieldGapTiles);
        minimumDecorCount = Mathf.Max(0, minimumDecorCount);
        maximumDecorCount = Mathf.Max(minimumDecorCount, maximumDecorCount);
        fallbackCellWorldSize = Mathf.Max(0.25f, fallbackCellWorldSize);
        placementAttempts = Mathf.Clamp(placementAttempts, 8, 64);

        if (Application.isPlaying)
        {
            RefreshDesignCache();
            QueueRebuild(0.05f, true);
        }
    }

    private void Update()
    {
        CleanupDestroyedClusters();
        MonitorClusterOwners();
        UpdateRoomTransitionState();

        bool transitioning = roomManager != null && roomManager.IsTransitioning;
        if (transitioning)
            return;

        if (rebuildPending && Time.unscaledTime >= rebuildAt)
        {
            rebuildPending = false;
            ReconcileCurrentField(true);
            return;
        }

        if (Time.unscaledTime < nextFieldScanAt)
            return;

        nextFieldScanAt = Time.unscaledTime + Mathf.Max(0.05f, fieldScanInterval);
        if (roomManager == null)
            ResolveRoomManager();
        ReconcileCurrentField(false);
    }

    private void ResolveRoomManager()
    {
        roomManager = null;
        BattleRoomManager[] managers = FindObjectsByType<BattleRoomManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

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

    private void RefreshDesignCache()
    {
        validDesigns.Clear();
        if (battleDecorDesigns == null)
            return;

        for (int i = 0; i < battleDecorDesigns.Count; i++)
        {
            BattleDecorSO design = battleDecorDesigns[i];
            if (design == null || !design.HasVisual)
                continue;
            validDesigns.Add(design);
        }
    }

    private void QueueRebuild(float delay, bool forceSignatureReset)
    {
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
            if (delta.sqrMagnitude <= thresholdSqr)
                continue;

            PlayExit(cluster, Cardinalize(delta), cluster.owner.ExitDuration);
        }
    }

    private void ReconcileCurrentField(bool force)
    {
        RefreshDesignCache();
        if (validDesigns.Count == 0)
        {
            BeginExitAll(Vector2.zero, fallbackExitDuration);
            return;
        }

        List<FloorSource> sources = CollectFloorSources();
        if (sources.Count == 0)
        {
            BeginExitAll(Vector2.zero, fallbackExitDuration);
            lastFieldSignature = int.MinValue;
            return;
        }

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
        if (sources == null || sources.Count == 0 || validDesigns.Count == 0)
            return;

        Bounds fieldBounds = sources[0].bounds;
        for (int i = 1; i < sources.Count; i++)
            fieldBounds.Encapsulate(sources[i].bounds);

        SpriteRenderer floorReference = ResolveFloorReference(sources);
        float cell = ResolveCellWorldSize(sources, floorReference);
        int minCount = Mathf.Min(minimumDecorCount, maximumDecorCount);
        int maxCount = Mathf.Max(minimumDecorCount, maximumDecorCount);

        System.Random random = new(unchecked(
            gameObject.scene.handle * 73856093 ^ signature * 19349663 ^ Environment.TickCount));
        int targetCount = maxCount <= minCount ? minCount : random.Next(minCount, maxCount + 1);
        placementBounds.Clear();

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
                continue;

            MapBlock owner = ResolveClosestOwner(target, sources);
            DecorCluster cluster = CreateCluster(
                design,
                target,
                outward,
                carrierBounds,
                footprint,
                cell,
                floorReference,
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
        float gap = Mathf.Max(0f, fieldGapTiles * cell);
        int attempts = Mathf.Clamp(placementAttempts, 8, 64);
        Vector2 authoredOffset = design.AttachOffsetTiles * cell;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            outward = ResolveAttachDirection(design.AttachSide, random);
            float sidePosition = design.UseFixedAttachPosition
                ? design.AttachPosition01
                : (float)random.NextDouble();

            if (Mathf.Abs(outward.x) > 0.5f)
            {
                float y = Mathf.Lerp(field.min.y, field.max.y, sidePosition);
                y = SnapToCell(y, cell);
                target = new Vector3(
                    outward.x < 0f
                        ? field.min.x - gap - width * 0.5f
                        : field.max.x + gap + width * 0.5f,
                    y,
                    field.center.z);
            }
            else
            {
                float x = Mathf.Lerp(field.min.x, field.max.x, sidePosition);
                x = SnapToCell(x, cell);
                target = new Vector3(
                    x,
                    outward.y < 0f
                        ? field.min.y - gap - height * 0.5f
                        : field.max.y + gap + height * 0.5f,
                    field.center.z);
            }

            target += new Vector3(authoredOffset.x, authoredOffset.y, 0f);
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
        BattleDecorSO design,
        Vector3 target,
        Vector2 outward,
        Bounds carrierBounds,
        Vector2Int footprint,
        float cell,
        SpriteRenderer floorReference,
        MapBlock owner,
        System.Random random)
    {
        if (design == null || floorReference == null)
            return null;

        GameObject rootObject = new($"{ClusterPrefix}{++serial:000}_{design.name}");
        rootObject.transform.SetParent(transform, true);
        rootObject.transform.position = target;

        GameObject visualObject = new(VisualRootName);
        visualObject.transform.SetParent(rootObject.transform, false);
        Transform visualRoot = visualObject.transform;

        int floorSorting = BuildCarrierFloor(
            visualRoot,
            design,
            footprint,
            cell,
            floorReference,
            random);

        if (design.FloorTemplate != null)
            BuildMechanicalFrame(
                visualRoot,
                design.FloorTemplate,
                footprint,
                cell,
                floorReference,
                floorSorting);

        BuildAuthoredParts(
            visualRoot,
            design,
            cell,
            floorReference,
            floorSorting,
            random);

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

    private static int BuildCarrierFloor(
        Transform visualRoot,
        BattleDecorSO design,
        Vector2Int footprint,
        float cell,
        SpriteRenderer source,
        System.Random random)
    {
        BattleShowFloorTemplateSO template = design.FloorTemplate;
        Sprite[] variants = template != null ? template.FloorVariants : null;
        int floorSorting = source.sortingOrder;

        if (template != null)
        {
            int highestPart = Mathf.Max(
                template.UpperPlateSortingOrder,
                Mathf.Max(template.LowerPlateSortingOrder, template.HandleSortingOrder));
            floorSorting = Mathf.Max(source.sortingOrder, Mathf.Max(template.FloorSortingOrder, highestPart + 1));
        }

        float x0 = -(footprint.x - 1) * cell * 0.5f;
        float y0 = -(footprint.y - 1) * cell * 0.5f;

        for (int y = 0; y < footprint.y; y++)
        {
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
        }

        return floorSorting;
    }

    private static void BuildAuthoredParts(
        Transform visualRoot,
        BattleDecorSO design,
        float cell,
        SpriteRenderer floorReference,
        int floorSorting,
        System.Random random)
    {
        GameObject partsObject = new(PartsRootName);
        partsObject.transform.SetParent(visualRoot, false);
        Transform partsRoot = partsObject.transform;

        bool mirror = design.AllowRandomMirrorX && random.NextDouble() < 0.5;
        partsRoot.localScale = new Vector3(mirror ? -cell : cell, cell, 1f);

        IReadOnlyList<BattleDecorPart> parts = design.Parts;
        if (parts == null)
            return;

        for (int i = 0; i < parts.Count; i++)
        {
            BattleDecorPart part = parts[i];
            if (part == null || part.Sprite == null)
                continue;

            GameObject partObject = new($"Part_{i:00}_{SafeName(part.Label)}");
            partObject.transform.SetParent(partsRoot, false);
            partObject.transform.localPosition = new Vector3(part.LocalPosition.x, part.LocalPosition.y, -0.01f);
            partObject.transform.localRotation = Quaternion.Euler(0f, 0f, part.RotationDegrees);
            Vector2 localScale = part.LocalScale;
            partObject.transform.localScale = new Vector3(localScale.x, localScale.y, 1f);

            SpriteRenderer renderer = partObject.AddComponent<SpriteRenderer>();
            renderer.sprite = part.Sprite;
            renderer.color = part.Tint;
            renderer.flipX = part.FlipX;
            renderer.sharedMaterial = floorReference.sharedMaterial;
            renderer.sortingLayerID = floorReference.sortingLayerID;
            renderer.sortingOrder = floorSorting + part.SortingOffset;
        }
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

            float px = footprint.x <= 1
                ? 0f
                : Mathf.Lerp(minCenterX, maxCenterX, x / (float)(footprint.x - 1));
            CreateFramePart(
                frame,
                $"LowerPlate_{x}",
                lower,
                new Vector3(px, minCenterY - cell, 0f),
                reference,
                template.PlateTint,
                lowerSorting,
                cell);
        }

        if (template.HandlePlacement == BattleShowHandlePlacementMode.None)
            return;

        Sprite upper = template.UpperHandleSprite32 != null
            ? template.UpperHandleSprite32
            : template.UpperPlateSprite32;

        CreateHorizontalFaceHandles(
            frame, "UpperHandle", upper,
            maxCenterY + cell,
            minCenterX, maxCenterX,
            reference, template.HandleTint, handleSorting, cell);

        CreateHorizontalFaceHandles(
            frame, "LowerHandle", template.LowerHandleSprite32,
            minCenterY - cell,
            minCenterX, maxCenterX,
            reference, template.HandleTint, handleSorting, cell);

        CreateVerticalFaceHandles(
            frame, "LeftHandle", template.LeftHandleSprite32,
            minCenterX - cell,
            minCenterY, maxCenterY,
            reference, template.HandleTint, handleSorting, cell);

        CreateVerticalFaceHandles(
            frame, "RightHandle", template.RightHandleSprite32,
            maxCenterX + cell,
            minCenterY, maxCenterY,
            reference, template.HandleTint, handleSorting, cell);
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
        float duration = Mathf.Max(0.20f, entryDuration) *
                         Mathf.Lerp(0.92f, 1.12f, Mathf.InverseLerp(1f, 9f, area));

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
                Mathf.Lerp(0.45f, 0.76f, Mathf.InverseLerp(1f, 9f, area)),
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

        Bounds bounds = cluster.carrierBounds;
        bounds.center = cluster.root.position;
        float distance = ResolveOffscreenDistance(bounds, direction);
        Vector3 current = cluster.root.position;
        Vector3 anticipation = current - (Vector3)(direction * Mathf.Max(0.02f, exitAnticipationDistance));
        Vector3 destination = current + (Vector3)(direction * distance);
        float duration = Mathf.Max(0.20f, requestedDuration > 0.01f ? requestedDuration : fallbackExitDuration);

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Append(
            cluster.root.DOMove(anticipation, Mathf.Max(0.04f, exitAnticipationDuration))
                .SetEase(Ease.OutQuad));
        sequence.Append(cluster.root.DOMove(destination, duration).SetEase(Ease.InCubic));

        if (cluster.visualRoot != null)
        {
            float sign = direction.x + direction.y >= 0f ? -1f : 1f;
            cluster.visualRoot
                .DOLocalRotate(new Vector3(0f, 0f, sign * 1.2f), duration + exitAnticipationDuration)
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

            Vector2 direction = preferredDirection.sqrMagnitude > 0.001f
                ? preferredDirection
                : cluster.outwardDirection;
            PlayExit(cluster, direction, duration);
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
        List<FloorSource> result = new();
        HashSet<int> rendererIds = new();

        BattleWalkableField[] fields = FindObjectsByType<BattleWalkableField>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < fields.Length; i++)
        {
            BattleWalkableField field = fields[i];
            if (field == null || field.gameObject.scene != gameObject.scene)
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
            if (block == null || block.gameObject.scene != gameObject.scene || !block.ContributesWalkableNavMesh)
                continue;

            SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                SpriteRenderer renderer = renderers[r];
                if (renderer == null || !renderer.enabled || renderer.sprite == null)
                    continue;

                string n = renderer.name;
                if (!n.StartsWith("Tile_", StringComparison.Ordinal) &&
                    !n.StartsWith("ShowTile_", StringComparison.Ordinal) &&
                    renderer.GetComponent<BattleWalkableField>() == null)
                    continue;

                AddFloorSource(result, rendererIds, renderer, ResolveOutermostMapBlock(renderer.transform));
            }
        }

        if (result.Count > 0)
            return result;

        SpriteRenderer[] generic = FindObjectsByType<SpriteRenderer>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < generic.Length; i++)
        {
            SpriteRenderer renderer = generic[i];
            if (renderer == null || renderer.gameObject.scene != gameObject.scene ||
                !renderer.enabled || renderer.sprite == null)
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

        output.Add(new FloorSource
        {
            renderer = renderer,
            owner = owner,
            bounds = renderer.bounds
        });
    }

    private static MapBlock ResolveOutermostMapBlock(Transform source)
    {
        if (source == null)
            return null;

        MapBlock[] parents = source.GetComponentsInParent<MapBlock>(true);
        return parents == null || parents.Length == 0 ? null : parents[parents.Length - 1];
    }

    private static SpriteRenderer ResolveFloorReference(List<FloorSource> sources)
    {
        for (int i = 0; i < sources.Count; i++)
        {
            SpriteRenderer renderer = sources[i]?.renderer;
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
            SpriteRenderer renderer = sources[i]?.renderer;
            if (renderer == null || renderer.sprite == null)
                continue;

            string n = renderer.name;
            if (!n.StartsWith("Tile_", StringComparison.Ordinal) &&
                !n.StartsWith("ShowTile_", StringComparison.Ordinal))
                continue;

            float size = Mathf.Min(
                Mathf.Abs(renderer.bounds.size.x),
                Mathf.Abs(renderer.bounds.size.y));
            if (size >= 0.20f)
                best = Mathf.Min(best, size);
        }

        if (!float.IsInfinity(best) && best <= 4f)
            return best;

        if (fallback != null)
        {
            float size = Mathf.Min(
                Mathf.Abs(fallback.bounds.size.x),
                Mathf.Abs(fallback.bounds.size.y));
            if (size >= 0.25f && size <= 2f)
                return size;
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
        Vector2 direction = travelDirection.sqrMagnitude > 0.001f
            ? travelDirection.normalized
            : Vector2.down;
        float support = Mathf.Abs(direction.x) * bounds.extents.x +
                        Mathf.Abs(direction.y) * bounds.extents.y;
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
            return template.LowerPlateLeftSprite32 != null
                ? template.LowerPlateLeftSprite32
                : template.LowerPlateCenterSprite32;

        if (x >= columns - 1)
            return template.LowerPlateRightSprite32 != null
                ? template.LowerPlateRightSprite32
                : template.LowerPlateCenterSprite32;

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
            case BattleDecorAttachSide.Right:
                return Vector2.right;
            case BattleDecorAttachSide.Bottom:
                return Vector2.down;
            case BattleDecorAttachSide.Left:
                return Vector2.left;
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
        if (direction.sqrMagnitude <= 0.001f)
            return Vector2.right;

        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? (direction.x >= 0f ? Vector2.right : Vector2.left)
            : (direction.y >= 0f ? Vector2.up : Vector2.down);
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

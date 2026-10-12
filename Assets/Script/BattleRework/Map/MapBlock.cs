using System;
using System.Collections.Generic;
using DG.Tweening;
using NavMeshPlus.Components;
using UnityEngine;

public enum MapBlockEntryType
{
    WheelSlide,
    CeilingDrop,
    RiseFromFloor,
    Static
}

/// <summary>
/// Runtime Map / Corridor / Large Room Piece의 도킹 연출을 담당합니다.
/// contributesWalkableNavMesh가 켜진 Block은 실제 Walkable NavMesh Renderer만
/// BattleWalkableField로 등록하여 Player가 '실제로 존재하는 바닥' 위에서만 이동하게 합니다.
///
/// WheelSlide는 이미 도킹 예정/도킹 완료된 바닥과 Persistent 4x4를 관통하는 Rail을 사용하지 않습니다.
/// 기본 방향이 막히면 다른 Cardinal Rail을 찾고, 네 방향 모두 막히면 관통 대신 제자리 Snap을 사용합니다.
///
/// Entry Rail 판정은 Scene 전체를 후보 방향마다 재검색하지 않습니다.
/// 활성 MapBlock Registry와 실제 Floor Cell Bounds를 공유하여 자유형/Assembly의 빈 공간을
/// 하나의 거대한 사각 충돌체로 오인하지 않도록 합니다.
///
/// 목적지에 닿은 뒤의 반동/VFX/카메라 충격은 BattleTileDockingPresentationManager가 공통 관리합니다.
/// 따라서 전투 필드, 대기실 Screen Carrier, Reward/Map Show Carrier가 동일한 도킹 감각을 사용합니다.
/// </summary>
public class MapBlock : MonoBehaviour
{
    public const float UnitWorldSize = 1f;
    public static readonly Vector2 BlockWorldSize = new(2f, 2f);

    private static readonly HashSet<MapBlock> ActiveBlocks = new();
    private static RoomBaseTemplate cachedRoomBaseTemplate;
    private static int cachedRoomBaseSceneHandle = int.MinValue;

    [Header("Entry")]
    [SerializeField] private MapBlockEntryType entryType = MapBlockEntryType.WheelSlide;
    [SerializeField, Min(0f)] private float entryDuration = 0.62f;
    [SerializeField, Min(0f)] private float entryOffset = 8f;
    [SerializeField] private Ease entryEase = Ease.InCubic;

    [Header("Entry Sequence Readability")]
    [Tooltip("BattleRoomManager가 주는 조립 Stagger를 실제 전투 바닥에서 더 분명하게 보이게 하는 배율입니다. Show Floor처럼 walkable=false인 연출용 판에는 적용하지 않습니다.")]
    [SerializeField, Min(1f)] private float walkableEntryDelayScale = 2.25f;

    [Header("Entry Collision Safety")]
    [Tooltip("켜면 WheelSlide가 이미 존재하는 Tile/Persistent 4x4를 관통하지 않도록 Cardinal Rail을 다시 선택합니다.")]
    [SerializeField] private bool avoidExistingTilePenetration = true;
    [SerializeField, Range(4, 16)] private int entryPathSamples = 8;
    [SerializeField, Range(0f, 0.25f)] private float entryPathClearance = 0.06f;

    [Header("Navigation Surface")]
    [Tooltip("실제로 플레이어/몬스터가 설 수 있는 바닥이면 켭니다. 벽/장식 전용 블록이면 끕니다.")]
    [SerializeField] private bool contributesWalkableNavMesh = true;
    [SerializeField] private NavMeshModifier walkableNavModifier;

    [Header("Approach Presentation")]
    [SerializeField] private Transform presentationRoot;
    [SerializeField] private Transform wheelRoot;
    [SerializeField, Min(0f)] private float approachRumbleDegrees = 0.55f;
    [SerializeField, Range(1, 40)] private int approachRumbleVibrato = 8;
    [SerializeField] private float wheelSpinDegreesPerWorldUnit = 180f;

    [Header("Docking Impact")]
    [Tooltip("공용 Docking Manager가 없는 비상 fallback에서만 사용합니다.")]
    [SerializeField, Min(0f)] private float impactReboundDistance = 0.055f;
    [Tooltip("공용 Docking Manager가 없는 비상 fallback에서만 사용합니다.")]
    [SerializeField, Min(0.01f)] private float impactSettleDuration = 0.11f;
    [SerializeField, Range(0f, 0.15f)] private float impactPunchScale = 0.045f;
    [SerializeField, Min(0f)] private float impactStrength = 1f;
    [SerializeField, Min(0f)] private float impactFaceInset = 0.02f;

    [Header("Exit")]
    [SerializeField, Min(0f)] private float exitDuration = 0.6f;
    [SerializeField] private Ease exitEase = Ease.InQuad;

    private readonly List<Bounds> entryBlockerBuffer = new();
    private readonly List<Bounds> entryMovingFloorBuffer = new();

    private Quaternion presentationBaseRotation;
    private Vector3 presentationBaseScale = Vector3.one;
    private Vector3 wheelBaseEuler;
    private Vector3 entryDestination;
    private bool hasEntryDestination;
    private Vector2 lastEntrySourceDirection;
    private bool hasEntrySourceDirection;

    private bool cachedFinalFloorBoundsValid;
    private Vector3 cachedFinalFloorRootPosition;
    private Bounds cachedFinalFloorBounds;

    public MapBlockEntryType EntryType => entryType;
    public bool WillImpact => entryType != MapBlockEntryType.Static;
    public bool ContributesWalkableNavMesh => contributesWalkableNavMesh;
    public float EntryDuration => entryType == MapBlockEntryType.Static
        ? 0f
        : entryDuration + ResolveDockSettleDuration();
    public float ExitDuration => exitDuration;
    public float ExitTravelDistance => entryOffset;
    public bool HasEntryDestination => hasEntryDestination;
    public Vector3 EntryDestination => hasEntryDestination ? entryDestination : transform.position;
    public bool HasEntrySourceDirection => hasEntrySourceDirection;
    public Vector2 LastEntrySourceDirection => hasEntrySourceDirection ? lastEntrySourceDirection : Vector2.zero;

    public event Action<MapBlock, Vector3, Vector2, float> Impacted;

    private void Awake()
    {
        ResolvePresentationRoot();
        EnsureWalkableNavMeshSource();
        CachePresentationPose();
        InvalidateFinalFloorBoundsCache();
    }

    private void OnEnable()
    {
        ActiveBlocks.Add(this);
        InvalidateFinalFloorBoundsCache();

        if (contributesWalkableNavMesh)
            EnsureWalkableNavMeshSource();
    }

    private void OnDisable()
    {
        ActiveBlocks.Remove(this);
        KillTweens();
    }

    private void OnDestroy()
    {
        ActiveBlocks.Remove(this);
    }

    public void ConfigureRuntimeDockingBlock(
        Transform visualRoot,
        bool walkable,
        float strength = 0.6f,
        float duration = 0.50f,
        float offset = 5.5f)
    {
        presentationRoot = visualRoot != null ? visualRoot : transform;
        contributesWalkableNavMesh = walkable;
        if (!walkable)
            BattleDecorFloorHierarchyRegistrar.Ensure(presentationRoot);
        entryType = MapBlockEntryType.WheelSlide;
        entryDuration = Mathf.Max(0.05f, duration);
        entryOffset = Mathf.Max(0f, offset);
        exitDuration = entryDuration;
        exitEase = Ease.InOutCubic;
        impactStrength = Mathf.Max(0f, strength);
        approachRumbleDegrees = Mathf.Min(approachRumbleDegrees, 0.35f);
        impactReboundDistance = Mathf.Min(impactReboundDistance, 0.04f);
        impactPunchScale = Mathf.Min(impactPunchScale, 0.035f);
        ResolvePresentationRoot();
        EnsureWalkableNavMeshSource();
        CachePresentationPose();
        InvalidateFinalFloorBoundsCache();
    }

    private void ResolvePresentationRoot()
    {
        if (presentationRoot != null)
            return;

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].transform != transform)
            {
                presentationRoot = renderers[i].transform;
                break;
            }
        }

        if (presentationRoot == null)
        {
            SpriteRenderer ownRenderer = GetComponent<SpriteRenderer>();
            if (ownRenderer != null)
                presentationRoot = ownRenderer.transform;
        }
    }

    private void EnsureWalkableNavMeshSource()
    {
        if (!contributesWalkableNavMesh)
            return;

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        bool foundWalkableRenderer = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null)
                continue;

            NavMeshModifier modifier = renderer.GetComponent<NavMeshModifier>();
            if (modifier == null)
                continue;

            if (modifier.ignoreFromBuild || modifier.overrideArea)
                continue;

            if (walkableNavModifier == null)
                walkableNavModifier = modifier;

            BattleWalkableField.Ensure(renderer);
            foundWalkableRenderer = true;
        }

        if (foundWalkableRenderer)
            return;

        SpriteRenderer sourceRenderer = presentationRoot != null
            ? presentationRoot.GetComponent<SpriteRenderer>()
            : null;

        if (sourceRenderer == null)
            sourceRenderer = GetComponentInChildren<SpriteRenderer>(true);

        if (sourceRenderer == null)
        {
            Debug.LogWarning(
                $"[MapBlock] '{name}' is walkable but no SpriteRenderer was found. Player movement will not be allowed on this block.",
                this);
            return;
        }

        walkableNavModifier = sourceRenderer.GetComponent<NavMeshModifier>();
        if (walkableNavModifier == null)
            walkableNavModifier = sourceRenderer.gameObject.AddComponent<NavMeshModifier>();

        walkableNavModifier.ignoreFromBuild = false;
        walkableNavModifier.overrideArea = false;
        BattleWalkableField.Ensure(sourceRenderer);
    }

    private void CachePresentationPose()
    {
        if (presentationRoot != null)
        {
            presentationBaseRotation = presentationRoot.localRotation;
            presentationBaseScale = presentationRoot.localScale;
        }

        if (wheelRoot != null)
            wheelBaseEuler = wheelRoot.localEulerAngles;
    }

    private void RestorePresentationPose()
    {
        if (presentationRoot != null)
        {
            presentationRoot.localRotation = presentationBaseRotation;
            presentationRoot.localScale = presentationBaseScale;
        }
    }

    private void KillTweens()
    {
        transform.DOKill();
        if (presentationRoot != null)
            presentationRoot.DOKill();
        if (wheelRoot != null && wheelRoot != presentationRoot)
            wheelRoot.DOKill();
    }

    public void SnapTo(Vector3 worldPosition)
    {
        KillTweens();
        entryDestination = worldPosition;
        hasEntryDestination = true;
        transform.position = worldPosition;
        RestorePresentationPose();
        InvalidateFinalFloorBoundsCache();
    }

    private float ResolveEntryDelay(float requestedDelay)
    {
        float delay = Mathf.Max(0f, requestedDelay);
        if (!contributesWalkableNavMesh || delay <= 0f)
            return delay;

        return delay * Mathf.Max(1f, walkableEntryDelayScale);
    }

    private float ResolveDockSettleDuration()
    {
        return BattleTileDockingPresentationManager.Instance != null
            ? BattleTileDockingPresentationManager.SharedSettleDuration
            : Mathf.Max(0.01f, impactSettleDuration);
    }

    public float GetEntryDelayParameterForStart(float startAt)
    {
        return contributesWalkableNavMesh
            ? Mathf.Max(0f, startAt) / Mathf.Max(1f, walkableEntryDelayScale)
            : Mathf.Max(0f, startAt);
    }

    public float GetEntryDuration(float delay = 0f)
    {
        if (entryType == MapBlockEntryType.Static)
            return 0f;

        return ResolveEntryDelay(delay) + EntryDuration;
    }

    public Vector3 GetImpactContactPoint(Vector3 destination, Vector2 travelDirection)
    {
        Vector2 direction = travelDirection.sqrMagnitude > 0.001f
            ? travelDirection.normalized
            : Vector2.down;

        if (TryGetPresentationBounds(out Bounds bounds))
        {
            float supportDistance =
                Mathf.Abs(direction.x) * bounds.extents.x +
                Mathf.Abs(direction.y) * bounds.extents.y;
            supportDistance = Mathf.Max(0f, supportDistance - impactFaceInset);
            return bounds.center + (Vector3)(direction * supportDistance);
        }

        float fallbackDistance =
            Mathf.Abs(direction.x) * BlockWorldSize.x * 0.5f +
            Mathf.Abs(direction.y) * BlockWorldSize.y * 0.5f;
        fallbackDistance = Mathf.Max(0f, fallbackDistance - impactFaceInset);
        return destination + (Vector3)(direction * fallbackDistance);
    }

    public float GetImpactFaceLength(Vector2 travelDirection)
    {
        Vector2 direction = travelDirection.sqrMagnitude > 0.001f
            ? travelDirection.normalized
            : Vector2.down;

        if (TryGetPresentationBounds(out Bounds bounds))
        {
            return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
                ? bounds.size.y
                : bounds.size.x;
        }

        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? BlockWorldSize.y
            : BlockWorldSize.x;
    }

    private bool TryGetPresentationBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        Renderer[] renderers = presentationRoot != null
            ? presentationRoot.GetComponentsInChildren<Renderer>(true)
            : GetComponentsInChildren<Renderer>(true);

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

        if (found)
            return true;

        Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.enabled)
                continue;

            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return found;
    }

    private static Vector2 Cardinalize(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return Vector2.right;

        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? (direction.x >= 0f ? Vector2.right : Vector2.left)
            : (direction.y >= 0f ? Vector2.up : Vector2.down);
    }

    private Vector2 ResolveSafeWheelSlideSourceDirection(Vector3 destination, Vector2 preferredDirection)
    {
        Vector2 primary = Cardinalize(preferredDirection);
        if (!avoidExistingTilePenetration || entryOffset <= 0.01f)
            return primary;

        entryMovingFloorBuffer.Clear();
        if (!AppendFloorBoundsAtRootPosition(this, destination, entryMovingFloorBuffer))
            return primary;

        CollectFinalFloorBlockers(entryBlockerBuffer);
        if (entryBlockerBuffer.Count == 0)
            return primary;

        Vector2 perpendicularA = new(-primary.y, primary.x);
        Vector2 perpendicularB = -perpendicularA;
        Vector2 opposite = -primary;

        Vector2[] candidates =
        {
            primary,
            perpendicularA,
            perpendicularB,
            opposite
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            if (IsEntryPathClear(destination, candidates[i], entryMovingFloorBuffer, entryBlockerBuffer))
                return candidates[i];
        }

        // Attempt a collision-free multi-leg rail before falling back to a staged approach.
        return Vector2.zero;
    }

    private bool TryBuildDetourEntryRoute(
        Vector3 destination,
        Vector2 preferredDirection,
        out List<Vector3> entryWaypoints,
        out Vector2 finalTravelDirection)
    {
        entryWaypoints = null;
        finalTravelDirection = Vector2.zero;

        HashSet<Vector2Int> moving = new();
        HashSet<Vector2Int> blockers = new();
        for (int i = 0; i < entryMovingFloorBuffer.Count; i++)
        {
            Bounds bounds = entryMovingFloorBuffer[i];
            if (bounds.size.x > UnitWorldSize * 1.5f ||
                bounds.size.y > UnitWorldSize * 1.5f)
                return false; // Legacy multi-tile decorations are not tile rails.
            moving.Add(new Vector2Int(
                Mathf.RoundToInt(bounds.center.x / UnitWorldSize),
                Mathf.RoundToInt(bounds.center.y / UnitWorldSize)));
        }

        for (int i = 0; i < entryBlockerBuffer.Count; i++)
        {
            Bounds bounds = entryBlockerBuffer[i];
            int minX = Mathf.CeilToInt((bounds.min.x + 0.05f) / UnitWorldSize);
            int maxX = Mathf.FloorToInt((bounds.max.x - 0.05f) / UnitWorldSize);
            int minY = Mathf.CeilToInt((bounds.min.y + 0.05f) / UnitWorldSize);
            int maxY = Mathf.FloorToInt((bounds.max.y - 0.05f) / UnitWorldSize);
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    blockers.Add(new Vector2Int(x, y));
        }

        Vector2 cardinal = Cardinalize(preferredDirection);
        Vector2Int preferCell = new(Mathf.RoundToInt(cardinal.x), Mathf.RoundToInt(cardinal.y));
        if (!BattleTileRailPlanner.TryFindExitRoute(
                moving, blockers, preferCell,
                out List<Vector2Int> outwardOffsets,
                out Vector2Int outward))
            return false;

        entryWaypoints = new List<Vector3>(outwardOffsets.Count + 2);
        // Begin outside the occupied stage, then follow the rigid tile rail in reverse.
        for (int i = outwardOffsets.Count - 1; i >= 0; i--)
        {
            Vector2Int offset = outwardOffsets[i];
            entryWaypoints.Add(destination + new Vector3(
                offset.x * UnitWorldSize, offset.y * UnitWorldSize, 0f));
        }
        entryWaypoints.Add(destination);

        Vector2Int nearDestination = outwardOffsets[0];
        finalTravelDirection = -new Vector2(nearDestination.x, nearDestination.y).normalized;
        entryWaypoints.Insert(0, entryWaypoints[0] + new Vector3(
            outward.x * entryOffset, outward.y * entryOffset, 0f));
        return true;
    }

    /// <summary>
    /// Called only by the procedural assembly composer before committing a whole group.
    /// If a rigid group cannot arrive without crossing another tile, the composer
    /// subdivides that group into its constituent movable pieces.
    /// </summary>
    public bool HasCollisionFreeEntry(Vector3 destination, Vector2 preferredDirection)
    {
        if (entryType != MapBlockEntryType.WheelSlide ||
            !avoidExistingTilePenetration || entryOffset <= 0.01f)
            return true;

        Vector2 primary = Cardinalize(preferredDirection);
        if (ResolveSafeWheelSlideSourceDirection(destination, primary).sqrMagnitude > 0.001f)
            return true;

        return TryBuildDetourEntryRoute(
            destination, primary, out _, out _);
    }

    private bool IsEntryPathClear(
        Vector3 destination,
        Vector2 sourceDirection,
        IReadOnlyList<Bounds> ownFinalFloorBounds,
        IReadOnlyList<Bounds> blockers)
    {
        if (blockers == null || blockers.Count == 0)
            return true;
        if (ownFinalFloorBounds == null || ownFinalFloorBounds.Count == 0)
            return true;

        Vector2 direction = Cardinalize(sourceDirection);
        Vector3 startRoot = destination + (Vector3)(direction * entryOffset);
        int samples = Mathf.Clamp(entryPathSamples, 4, 16);

        // t=1은 최종 도킹 지점이므로 검사하지 않습니다.
        // 인접한 바닥과 Edge가 맞닿는 정상 도킹까지 충돌로 오인하지 않기 위함입니다.
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)samples;
            Vector3 rootPosition = Vector3.Lerp(startRoot, destination, t);
            Vector3 delta = rootPosition - destination;

            for (int m = 0; m < ownFinalFloorBounds.Count; m++)
            {
                Bounds moving = ownFinalFloorBounds[m];
                moving.center += delta;

                for (int b = 0; b < blockers.Count; b++)
                {
                    if (Overlaps2D(moving, blockers[b], entryPathClearance))
                        return false;
                }
            }
        }

        return true;
    }

    private void CollectFinalFloorBlockers(List<Bounds> blockers)
    {
        blockers.Clear();

        RoomBaseTemplate baseTemplate = ResolveRoomBaseTemplate();
        if (baseTemplate != null && baseTemplate.HasPersistentBase)
        {
            Vector3 baseOrigin = baseTemplate.FixedTileOriginWorld;
            for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
            {
                for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                {
                    blockers.Add(new Bounds(
                        baseOrigin + new Vector3(x, y, 0f),
                        new Vector3(UnitWorldSize, UnitWorldSize, 0.1f)));
                }
            }
        }

        foreach (MapBlock other in ActiveBlocks)
        {
            if (other == null || other == this || !other.gameObject.activeInHierarchy)
                continue;
            if (other.gameObject.scene != gameObject.scene)
                continue;
            // Authoring prototypes live far away (10,000 world units) until the
            // next frame. They are never docked geometry and must not obstruct
            // the search or expand its escape bounds.
            if (other.name.StartsWith("__RuntimeRoomPiecePrototype_", StringComparison.Ordinal) &&
                !other.name.EndsWith("(Clone)", StringComparison.Ordinal))
                continue;

            // Procedural Assembly 내부 구성요소를 자기 자신의 외부 장애물로 취급하지 않습니다.
            if (other.transform.IsChildOf(transform) || transform.IsChildOf(other.transform))
                continue;

            Vector3 finalRoot = other.HasEntryDestination
                ? other.EntryDestination
                : other.transform.position;

            AppendFloorBoundsAtRootPosition(other, finalRoot, blockers);
        }
    }

    /// <summary>
    /// Appends the actual floor-cell bounds instead of one aggregate rectangle. This is important for
    /// L/freeform/assembly shapes: an aggregate rectangle can cover the empty persistent-base hole and
    /// falsely report every cardinal rail as blocked.
    /// </summary>
    private static bool AppendFloorBoundsAtRootPosition(
        MapBlock block,
        Vector3 rootPosition,
        List<Bounds> output)
    {
        if (block == null || output == null)
            return false;

        int before = output.Count;
        Vector3 delta = rootPosition - block.transform.position;
        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || renderer.sprite == null)
                continue;

            string rendererName = renderer.name;
            bool floorRenderer =
                rendererName.StartsWith("Tile_", StringComparison.Ordinal) ||
                rendererName.StartsWith("ShowTile_", StringComparison.Ordinal) ||
                renderer.GetComponent<BattleWalkableField>() != null;

            if (!floorRenderer)
                continue;

            Bounds shifted = renderer.bounds;
            shifted.center += delta;
            output.Add(shifted);
        }

        if (output.Count > before)
            return true;

        if (!block.TryGetPresentationBounds(out Bounds fallback))
            return false;

        fallback.center += delta;
        output.Add(fallback);
        return true;
    }

    private RoomBaseTemplate ResolveRoomBaseTemplate()
    {
        int sceneHandle = gameObject.scene.handle;
        if (cachedRoomBaseTemplate != null &&
            cachedRoomBaseSceneHandle == sceneHandle &&
            cachedRoomBaseTemplate.gameObject.scene == gameObject.scene)
        {
            return cachedRoomBaseTemplate;
        }

        cachedRoomBaseTemplate = null;
        cachedRoomBaseSceneHandle = sceneHandle;

        RoomBaseTemplate[] templates = FindObjectsByType<RoomBaseTemplate>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < templates.Length; i++)
        {
            RoomBaseTemplate candidate = templates[i];
            if (candidate == null || candidate.gameObject.scene != gameObject.scene)
                continue;

            cachedRoomBaseTemplate = candidate;
            break;
        }

        return cachedRoomBaseTemplate;
    }

    private void InvalidateFinalFloorBoundsCache()
    {
        cachedFinalFloorBoundsValid = false;
    }

    private bool TryGetCachedFloorBoundsAtRootPosition(Vector3 rootPosition, out Bounds bounds)
    {
        if (cachedFinalFloorBoundsValid &&
            (cachedFinalFloorRootPosition - rootPosition).sqrMagnitude <= 0.0000001f)
        {
            bounds = cachedFinalFloorBounds;
            return true;
        }

        if (!TryGetFloorBoundsAtRootPositionUncached(this, rootPosition, out bounds))
            return false;

        cachedFinalFloorBounds = bounds;
        cachedFinalFloorRootPosition = rootPosition;
        cachedFinalFloorBoundsValid = true;
        return true;
    }

    private static bool TryGetFloorBoundsAtRootPositionUncached(MapBlock block, Vector3 rootPosition, out Bounds bounds)
    {
        bounds = default;
        if (block == null)
            return false;

        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        bool found = false;
        Vector3 delta = rootPosition - block.transform.position;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || renderer.sprite == null)
                continue;

            string rendererName = renderer.name;
            bool floorRenderer =
                rendererName.StartsWith("Tile_", StringComparison.Ordinal) ||
                rendererName.StartsWith("ShowTile_", StringComparison.Ordinal) ||
                renderer.GetComponent<BattleWalkableField>() != null;

            if (!floorRenderer)
                continue;

            Bounds shifted = renderer.bounds;
            shifted.center += delta;

            if (!found)
            {
                bounds = shifted;
                found = true;
            }
            else
            {
                bounds.Encapsulate(shifted);
            }
        }

        if (found)
            return true;

        if (!block.TryGetPresentationBounds(out Bounds fallback))
            return false;

        fallback.center += delta;
        bounds = fallback;
        return true;
    }

    private static bool Overlaps2D(Bounds a, Bounds b, float clearance)
    {
        float safe = Mathf.Max(0f, clearance);
        float aHalfX = Mathf.Max(0.01f, a.extents.x - safe);
        float aHalfY = Mathf.Max(0.01f, a.extents.y - safe);
        float bHalfX = Mathf.Max(0.01f, b.extents.x - safe);
        float bHalfY = Mathf.Max(0.01f, b.extents.y - safe);

        return Mathf.Abs(a.center.x - b.center.x) < aHalfX + bHalfX &&
               Mathf.Abs(a.center.y - b.center.y) < aHalfY + bHalfY;
    }

    public Tween PlayEnter(Vector3 destination, Vector2 preferredDirection, float delay = 0f)
    {
        KillTweens();
        RestorePresentationPose();
        entryDestination = destination;
        hasEntryDestination = true;
        hasEntrySourceDirection = false;
        lastEntrySourceDirection = Vector2.zero;
        InvalidateFinalFloorBoundsCache();

        if (entryType == MapBlockEntryType.Static)
        {
            transform.position = destination;
            return transform.DOMove(destination, 0f);
        }

        Vector3 start = destination;
        Vector2 sourceDirection;
        List<Vector3> entryWaypoints = null;
        Vector2 finalApproachDirection = Vector2.zero;
        Vector2 preferred = preferredDirection.sqrMagnitude > 0.001f
            ? preferredDirection.normalized
            : Vector2.right;

        switch (entryType)
        {
            case MapBlockEntryType.CeilingDrop:
                start += Vector3.up * entryOffset;
                sourceDirection = Vector2.up;
                break;

            case MapBlockEntryType.RiseFromFloor:
                start += Vector3.down * entryOffset;
                sourceDirection = Vector2.down;
                break;

            default:
                sourceDirection = ResolveSafeWheelSlideSourceDirection(destination, preferred);
                if (sourceDirection.sqrMagnitude <= 0.001f)
                {
                    if (TryBuildDetourEntryRoute(
                            destination, preferred, out entryWaypoints,
                            out finalApproachDirection))
                    {
                        start = entryWaypoints[0];
                        sourceDirection = (start - destination).normalized;
                    }
                    else
                    {
                        // No rigid translation path exists: retain visible motion,
                        // without the old instant snap, behind the stage floor.
                        Debug.LogWarning(
                            $"[MapBlock] '{name}' has no collision-free entry route. " +
                            "Using a staged approach rather than snapping.", this);
                        sourceDirection = preferred;
                        start += (Vector3)(sourceDirection * entryOffset);
                    }
                }
                else
                {
                    start += (Vector3)(sourceDirection * entryOffset);
                }
                break;
        }

        if (sourceDirection.sqrMagnitude > 0.001f)
        {
            lastEntrySourceDirection = Cardinalize(sourceDirection);
            hasEntrySourceDirection = true;
        }

        Vector2 travelDirection = entryWaypoints != null
            ? finalApproachDirection
            : ((Vector2)destination - (Vector2)start).normalized;
        if (travelDirection.sqrMagnitude <= 0.001f)
            travelDirection = -sourceDirection;

        transform.position = start;

        float safeDelay = ResolveEntryDelay(delay);
        float approachTime = Mathf.Max(0.01f, entryDuration);
        float settleTime = ResolveDockSettleDuration();
        BattleTileDockingPresentationManager dockingPresentation = BattleTileDockingPresentationManager.Instance;

        if (presentationRoot != null && approachRumbleDegrees > 0f)
        {
            presentationRoot
                .DOShakeRotation(
                    approachTime,
                    new Vector3(0f, 0f, approachRumbleDegrees),
                    Mathf.Max(1, approachRumbleVibrato),
                    20f,
                    false)
                .SetDelay(safeDelay)
                .SetEase(Ease.Linear);
        }

        if (entryType == MapBlockEntryType.WheelSlide && wheelRoot != null)
        {
            float distance = Vector2.Distance(start, destination);
            float sign = travelDirection.x >= 0f ? -1f : 1f;
            Vector3 targetEuler = wheelBaseEuler +
                                  new Vector3(0f, 0f, distance * wheelSpinDegreesPerWorldUnit * sign);

            wheelRoot
                .DOLocalRotate(targetEuler, approachTime, RotateMode.FastBeyond360)
                .SetDelay(safeDelay)
                .SetEase(Ease.Linear);
        }

        Sequence sequence = DOTween.Sequence();
        if (safeDelay > 0f)
            sequence.AppendInterval(safeDelay);

        if (entryWaypoints != null)
        {
            float totalLength = 0f;
            Vector3 previousPosition = start;
            for (int i = 1; i < entryWaypoints.Count; i++)
            {
                totalLength += Vector3.Distance(previousPosition, entryWaypoints[i]);
                previousPosition = entryWaypoints[i];
            }
            previousPosition = start;
            for (int i = 1; i < entryWaypoints.Count; i++)
            {
                Vector3 nextPosition = entryWaypoints[i];
                float portion = Vector3.Distance(previousPosition, nextPosition) /
                                Mathf.Max(0.001f, totalLength);
                sequence.Append(
                    transform.DOMove(nextPosition, Mathf.Max(0.001f, approachTime * portion))
                        .SetEase(i == entryWaypoints.Count - 1 ? entryEase : Ease.Linear));
                previousPosition = nextPosition;
            }
        }
        else
        {
            sequence.Append(transform.DOMove(destination, approachTime).SetEase(entryEase));
        }

        Vector3 contactPoint = GetImpactContactPoint(destination, travelDirection);
        bool hasLegacyImpactListener = Impacted != null;
        sequence.AppendCallback(() =>
        {
            transform.position = destination;
            AnimateDirectionalCompression(travelDirection, settleTime);
            Impacted?.Invoke(this, contactPoint, travelDirection, Mathf.Max(0f, impactStrength));
        });

        if (dockingPresentation != null)
        {
            // BattleRoomManager가 Impacted를 구독한 Legacy Room은 기존 VFX 호출을 유지하고
            // 그 외(대기실/Show/Highlight 등)는 공용 Manager가 VFX까지 담당합니다.
            // 물리 반동은 모든 경로에서 동일하게 공용 Manager를 사용합니다.
            dockingPresentation.AppendDockSettle(
                sequence,
                transform,
                destination,
                travelDirection,
                contactPoint,
                Mathf.Max(0f, impactStrength),
                !hasLegacyImpactListener);
        }
        else
        {
            // Runtime Manager 생성 실패 시 기존 동작을 유지하는 보수적 fallback.
            if (impactReboundDistance > 0f)
            {
                Vector3 rebound = destination - (Vector3)(travelDirection * impactReboundDistance);
                sequence.Append(
                    transform.DOMove(rebound, settleTime * 0.30f)
                        .SetEase(Ease.OutQuad));
            }

            sequence.Append(
                transform.DOMove(destination, settleTime * 0.70f)
                    .SetEase(Ease.OutCubic));
        }

        sequence.OnComplete(() =>
        {
            transform.position = destination;
            RestorePresentationPose();
        });

        return sequence;
    }

    private void AnimateDirectionalCompression(Vector2 travelDirection, float settleTime)
    {
        if (presentationRoot == null || impactPunchScale <= 0f)
            return;

        presentationRoot.DOKill();
        presentationRoot.localRotation = presentationBaseRotation;

        Vector3 compressed = presentationBaseScale;
        float compression = Mathf.Clamp(impactPunchScale, 0f, 0.15f);
        float counterStretch = compression * 0.16f;

        if (Mathf.Abs(travelDirection.x) >= Mathf.Abs(travelDirection.y))
        {
            compressed.x *= 1f - compression;
            compressed.y *= 1f + counterStretch;
        }
        else
        {
            compressed.y *= 1f - compression;
            compressed.x *= 1f + counterStretch;
        }

        presentationRoot.localScale = compressed;
        presentationRoot
            .DOScale(presentationBaseScale, Mathf.Max(0.03f, settleTime * 0.82f))
            .SetEase(Ease.OutCubic);
    }

    public Tween PlayExit(Vector2 direction)
    {
        KillTweens();
        RestorePresentationPose();

        Vector2 dir = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
        Vector3 destination = transform.position + (Vector3)(dir * entryOffset);
        return transform.DOMove(destination, exitDuration).SetEase(exitEase);
    }
}


/// <summary>
/// Grid-space rail search shared by entry and exit. A straight rail is preferred;
/// crowded/freeform layouts may use a short orthogonal detour instead of teleport/fade.
/// The persistent Base is supplied as blocked tiles by the caller.
/// </summary>
internal static class BattleTileRailPlanner
{
    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down
    };

    public static bool TryFindExitRoute(
        IReadOnlyCollection<Vector2Int> moving,
        HashSet<Vector2Int> blockers,
        Vector2Int preferred,
        out List<Vector2Int> offsets,
        out Vector2Int escapeDirection)
    {
        offsets = null;
        escapeDirection = Vector2Int.zero;
        if (moving == null || moving.Count == 0)
            return false;

        HashSet<Vector2Int> shape = new(moving);
        HashSet<Vector2Int> occupied = blockers != null
            ? new HashSet<Vector2Int>(blockers)
            : new HashSet<Vector2Int>();
        occupied.ExceptWith(shape);

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector2Int c in occupied)
        {
            minX = Mathf.Min(minX, c.x);
            minY = Mathf.Min(minY, c.y);
            maxX = Mathf.Max(maxX, c.x);
            maxY = Mathf.Max(maxY, c.y);
        }

        if (occupied.Count == 0)
        {
            escapeDirection = preferred == Vector2Int.zero ? Vector2Int.right : preferred;
            offsets = new List<Vector2Int> { escapeDirection };
            return true;
        }

        // Room sizes are small (typically 20-40 tiles). Search rigid translation
        // offsets, not individual tile paths, so every tile moves as one connected plate.
        int shapeMinX = int.MaxValue, shapeMinY = int.MaxValue;
        int shapeMaxX = int.MinValue, shapeMaxY = int.MinValue;
        foreach (Vector2Int c in shape)
        {
            shapeMinX = Mathf.Min(shapeMinX, c.x);
            shapeMinY = Mathf.Min(shapeMinY, c.y);
            shapeMaxX = Mathf.Max(shapeMaxX, c.x);
            shapeMaxY = Mathf.Max(shapeMaxY, c.y);
        }

        int limit = Mathf.Clamp(
            Mathf.Max(maxX - minX, maxY - minY) +
            Mathf.Max(shapeMaxX - shapeMinX, shapeMaxY - shapeMinY) + 8,
            12, 96);
        Vector2Int[] directions = PreferredFirst(preferred);
        Queue<Vector2Int> frontier = new();
        Dictionary<Vector2Int, Vector2Int> previous = new();
        Vector2Int origin = Vector2Int.zero;
        frontier.Enqueue(origin);
        previous.Add(origin, origin);
        const int maxVisited = 18000;

        while (frontier.Count > 0 && previous.Count < maxVisited)
        {
            Vector2Int offset = frontier.Dequeue();
            bool outsideRight = shapeMinX + offset.x > maxX + 1;
            bool outsideLeft = shapeMaxX + offset.x < minX - 1;
            bool outsideUp = shapeMinY + offset.y > maxY + 1;
            bool outsideDown = shapeMaxY + offset.y < minY - 1;

            if (offset != origin && (outsideRight || outsideLeft || outsideUp || outsideDown))
            {
                escapeDirection = outsideRight ? Vector2Int.right :
                                  outsideLeft ? Vector2Int.left :
                                  outsideUp ? Vector2Int.up : Vector2Int.down;
                List<Vector2Int> reversed = new();
                while (offset != origin)
                {
                    reversed.Add(offset);
                    offset = previous[offset];
                }
                reversed.Reverse();
                offsets = reversed;
                return true;
            }

            for (int d = 0; d < directions.Length; d++)
            {
                Vector2Int next = offset + directions[d];
                if (Mathf.Abs(next.x) > limit || Mathf.Abs(next.y) > limit ||
                    previous.ContainsKey(next))
                    continue;

                bool clear = true;
                foreach (Vector2Int cell in shape)
                {
                    if (!occupied.Contains(cell + next))
                        continue;
                    clear = false;
                    break;
                }

                if (!clear)
                    continue;
                previous.Add(next, offset);
                frontier.Enqueue(next);
            }
        }
        return false;
    }

    private static Vector2Int[] PreferredFirst(Vector2Int preferred)
    {
        Vector2Int[] result = new Vector2Int[Directions.Length];
        int index = 0;
        for (int i = 0; i < Directions.Length; i++)
        {
            if (Directions[i] != preferred)
                continue;
            result[index++] = Directions[i];
            break;
        }
        for (int i = 0; i < Directions.Length; i++)
        {
            if (Directions[i] != preferred)
                result[index++] = Directions[i];
        }
        return result;
    }
}

/// <summary>
/// Player 이동 허용 여부를 결정하는 실제 바닥 Marker입니다.
/// Trigger Collider는 물리 충돌용이 아니라 '여기에 실제 Field가 존재하는가' 판정 전용입니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleWalkableField : MonoBehaviour
{
    private static readonly HashSet<BattleWalkableField> ActiveFields = new();

    [SerializeField] private Collider2D supportCollider;

    public static int ActiveCount => ActiveFields.Count;

    private void OnEnable()
    {
        ActiveFields.Add(this);
        SpriteRenderer renderer = GetComponent<SpriteRenderer>();
        if (renderer != null && renderer.sprite != null)
            BattleDecorFloorSource.Ensure(renderer);
    }

    private void OnDisable()
    {
        ActiveFields.Remove(this);
    }

    public bool Contains(Vector2 worldPoint)
    {
        return isActiveAndEnabled &&
               supportCollider != null &&
               supportCollider.enabled &&
               supportCollider.OverlapPoint(worldPoint);
    }

    public static BattleWalkableField Ensure(SpriteRenderer renderer)
    {
        if (renderer == null || renderer.sprite == null)
            return null;

        BattleWalkableField marker = renderer.GetComponent<BattleWalkableField>();
        if (marker == null)
            marker = renderer.gameObject.AddComponent<BattleWalkableField>();

        if (marker.supportCollider == null)
        {
            BoxCollider2D support = renderer.gameObject.AddComponent<BoxCollider2D>();
            support.isTrigger = true;
            support.usedByEffector = false;

            Vector2 localSize = renderer.drawMode == SpriteDrawMode.Simple
                ? (Vector2)renderer.sprite.bounds.size
                : renderer.size;

            support.size = new Vector2(
                Mathf.Max(0.02f, localSize.x),
                Mathf.Max(0.02f, localSize.y));
            marker.supportCollider = support;
        }

        BattleDecorFloorSource.Ensure(renderer);
        ActiveFields.Add(marker);
        return marker;
    }

    public static bool HasSupport(Vector2 worldPoint)
    {
        if (ActiveFields.Count == 0)
            return false;

        foreach (BattleWalkableField field in ActiveFields)
        {
            if (field != null && field.Contains(worldPoint))
                return true;
        }

        return false;
    }
}
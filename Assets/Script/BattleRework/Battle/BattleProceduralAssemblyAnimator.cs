using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// BattleRoomManager가 만든 ProceduralAssemblyGroup_*의 표시 전용 조립기입니다.
///
/// 3-Group을 하나의 거대한 Bounds로 직선 이동시키면 Persistent 4x4를 둘러싼 Group은
/// 안전한 Cardinal Rail을 찾을 수 없어 MapBlock이 즉시 Snap하는 경우가 있습니다.
/// 이 컴포넌트는 Group 자체를 움직이지 않고, Group 안의 실제 AssemblySubPiece_*를
/// 3개의 순차 Wave로 화면 밖에서 도킹시킵니다.
///
/// 규칙:
/// - Group 1 -> Group 2 -> Group 3 순서로 완전히 분리된 Wave를 사용합니다.
/// - 각 SubPiece는 Persistent 4x4 바깥 방향을 우선 Rail로 사용합니다.
/// - 이미 도킹한 이전 Group의 최종 바닥과 Persistent 4x4를 관통하는 Rail은 사용하지 않습니다.
/// - 긴 Rail이 막히면 같은 방향의 더 짧은 안전 Rail을 찾습니다.
/// - 어떤 Cardinal Rail도 안전하지 않은 극단적인 내부 조각만 Scale-in fallback을 사용합니다.
///   기존 타일을 관통하거나 최종 위치로 순간이동하는 fallback은 사용하지 않습니다.
/// </summary>
[DefaultExecutionOrder(30000)]
[DisallowMultipleComponent]
public sealed class BattleProceduralAssemblyAnimator : MonoBehaviour
{
    [SerializeField, Min(8f)] private float preferredRailDistance = 36f;
    [SerializeField, Min(0.5f)] private float minimumRailDistance = 1.25f;
    [SerializeField, Min(0.01f)] private float railSearchStep = 1f;
    [SerializeField, Range(4, 20)] private int pathSamples = 12;
    [SerializeField, Range(0f, 0.2f)] private float pathClearance = 0.055f;
    [SerializeField, Min(0f)] private float waveGap = 0.06f;
    [SerializeField, Min(0.05f)] private float fallbackScaleDuration = 0.22f;

    private readonly HashSet<int> processedGroups = new();
    private RoomBaseTemplate baseTemplate;
    private BattleRoomManager roomManager;

    private sealed class SubPiecePlan
    {
        public MapBlock block;
        public Vector3 finalPosition;
        public Bounds finalBounds;
    }

    private sealed class GroupPlan
    {
        public MapBlock group;
        public int index;
        public readonly List<SubPiecePlan> pieces = new();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallRuntimeHost()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        if (FindFirstObjectByType<BattleProceduralAssemblyAnimator>() != null)
            return;

        BattleSceneManager battleManager = FindFirstObjectByType<BattleSceneManager>();
        bool battleScene = scene.name == BattleSceneEntry.DefaultBattleSceneName ||
                           (battleManager != null && battleManager.gameObject.scene == scene);
        if (!battleScene)
            return;

        GameObject host = new("BattleProceduralAssemblyAnimatorRuntime");
        host.AddComponent<BattleProceduralAssemblyAnimator>();
    }

    private void ResolveReferences()
    {
        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
    }

    private void LateUpdate()
    {
        ResolveReferences();

        MapBlock[] blocks = FindObjectsByType<MapBlock>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        List<GroupPlan> newGroups = new();
        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (block == null || !block.gameObject.activeInHierarchy)
                continue;
            if (!block.name.StartsWith("ProceduralAssemblyGroup_", StringComparison.Ordinal))
                continue;
            if (processedGroups.Contains(block.GetInstanceID()))
                continue;

            GroupPlan plan = BuildGroupPlan(block);
            if (plan != null && plan.pieces.Count > 0)
                newGroups.Add(plan);
        }

        if (newGroups.Count == 0)
            return;

        newGroups.Sort((a, b) => a.index.CompareTo(b.index));

        // 모든 Group Root를 먼저 최종 기준점에 고정합니다.
        // 자식 Piece의 finalPosition을 정확히 얻은 뒤에만 실제 Piece를 화면 밖으로 보냅니다.
        for (int i = 0; i < newGroups.Count; i++)
        {
            GroupPlan plan = newGroups[i];
            MapBlock group = plan.group;
            if (group == null)
                continue;

            group.transform.DOKill(false);
            if (group.HasEntryDestination)
                group.transform.position = group.EntryDestination;

            RefreshFinalPieceData(plan);
        }

        List<Bounds> previousGroupBlockers = new();
        if (baseTemplate != null && baseTemplate.HasPersistentBase)
        {
            previousGroupBlockers.Add(new Bounds(
                baseTemplate.FixedCenterWorld,
                new Vector3(
                    RoomBaseTemplate.FixedBaseTiles,
                    RoomBaseTemplate.FixedBaseTiles,
                    0.1f)));
        }

        float roomMoveDuration = roomManager != null && roomManager.CurrentRoom != null
            ? Mathf.Max(0.05f, roomManager.CurrentRoom.largePieceEntryDuration)
            : 0.72f;
        float waveLength = roomMoveDuration + 0.11f + Mathf.Max(0f, waveGap);

        for (int i = 0; i < newGroups.Count; i++)
        {
            GroupPlan plan = newGroups[i];
            if (plan.group == null)
                continue;

            float waveDelay = Mathf.Max(0, plan.index - 1) * waveLength;
            AnimateGroup(plan, previousGroupBlockers, waveDelay, roomMoveDuration);

            for (int p = 0; p < plan.pieces.Count; p++)
                previousGroupBlockers.Add(plan.pieces[p].finalBounds);

            processedGroups.Add(plan.group.GetInstanceID());
        }
    }

    private GroupPlan BuildGroupPlan(MapBlock group)
    {
        if (group == null)
            return null;

        GroupPlan plan = new()
        {
            group = group,
            index = ParseGroupIndex(group.name)
        };

        MapBlock[] children = group.GetComponentsInChildren<MapBlock>(true);
        for (int i = 0; i < children.Length; i++)
        {
            MapBlock child = children[i];
            if (child == null || child == group)
                continue;
            if (!child.name.StartsWith("AssemblySubPiece_", StringComparison.Ordinal))
                continue;

            plan.pieces.Add(new SubPiecePlan
            {
                block = child,
                finalPosition = child.transform.position
            });
        }

        return plan;
    }

    private static int ParseGroupIndex(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return 1;

        const string prefix = "ProceduralAssemblyGroup_";
        int start = objectName.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return 1;

        start += prefix.Length;
        int end = objectName.IndexOf('_', start);
        string number = end > start
            ? objectName.Substring(start, end - start)
            : objectName.Substring(start);

        return int.TryParse(number, out int value) ? Mathf.Max(1, value) : 1;
    }

    private void RefreshFinalPieceData(GroupPlan plan)
    {
        if (plan == null)
            return;

        for (int i = 0; i < plan.pieces.Count; i++)
        {
            SubPiecePlan piece = plan.pieces[i];
            if (piece == null || piece.block == null)
                continue;

            piece.finalPosition = piece.block.transform.position;
            if (!TryGetFloorBounds(piece.block.transform, out Bounds bounds))
                bounds = new Bounds(piece.finalPosition, Vector3.one * 0.95f);
            piece.finalBounds = bounds;
        }
    }

    private void AnimateGroup(
        GroupPlan plan,
        List<Bounds> blockers,
        float delay,
        float duration)
    {
        Vector2 baseCenter = baseTemplate != null && baseTemplate.HasPersistentBase
            ? (Vector2)baseTemplate.FixedCenterWorld
            : Vector2.zero;

        for (int i = 0; i < plan.pieces.Count; i++)
        {
            SubPiecePlan piece = plan.pieces[i];
            if (piece == null || piece.block == null)
                continue;

            Transform pieceTransform = piece.block.transform;
            pieceTransform.DOKill(false);

            Vector2 radial = (Vector2)piece.finalBounds.center - baseCenter;
            Vector2 primary = Cardinalize(radial);

            if (TryResolveSafeRail(piece.finalBounds, piece.finalPosition, primary, blockers, out Vector2 sourceDirection, out float railDistance))
            {
                Vector3 start = piece.finalPosition + (Vector3)(sourceDirection * railDistance);
                pieceTransform.position = start;

                pieceTransform
                    .DOMove(piece.finalPosition, Mathf.Max(0.05f, duration))
                    .SetDelay(Mathf.Max(0f, delay))
                    .SetEase(Ease.InCubic)
                    .OnComplete(() =>
                    {
                        if (pieceTransform != null)
                            pieceTransform.position = piece.finalPosition;
                    });
            }
            else
            {
                // 내부가 완전히 둘러싸인 Piece는 물리적으로 어떤 직선 Rail도 만들 수 없습니다.
                // 기존 타일을 관통하거나 순간이동시키지 않고 최종 위치에서 짧은 Scale-in만 사용합니다.
                pieceTransform.position = piece.finalPosition;
                Vector3 finalScale = pieceTransform.localScale;
                pieceTransform.localScale = new Vector3(finalScale.x * 0.82f, finalScale.y * 0.82f, finalScale.z);
                pieceTransform
                    .DOScale(finalScale, Mathf.Max(0.05f, fallbackScaleDuration))
                    .SetDelay(Mathf.Max(0f, delay))
                    .SetEase(Ease.OutBack);

                Debug.LogWarning(
                    $"[BattleProceduralAssemblyAnimator] '{piece.block.name}' has no collision-free straight rail. " +
                    "Used a non-tunneling scale-in fallback.",
                    piece.block);
            }
        }
    }

    private bool TryResolveSafeRail(
        Bounds finalBounds,
        Vector3 finalRoot,
        Vector2 preferred,
        List<Bounds> blockers,
        out Vector2 direction,
        out float distance)
    {
        Vector2 primary = Cardinalize(preferred);
        Vector2 perpendicularA = new(-primary.y, primary.x);
        Vector2 perpendicularB = -perpendicularA;
        Vector2 opposite = -primary;

        Vector2[] directions =
        {
            primary,
            perpendicularA,
            perpendicularB,
            opposite
        };

        float maxDistance = Mathf.Max(minimumRailDistance, preferredRailDistance);
        float minDistance = Mathf.Max(0.5f, minimumRailDistance);
        float step = Mathf.Max(0.1f, railSearchStep);

        for (int d = 0; d < directions.Length; d++)
        {
            Vector2 candidate = Cardinalize(directions[d]);
            for (float rail = maxDistance; rail >= minDistance; rail -= step)
            {
                if (!IsPathClear(finalBounds, finalRoot, candidate, rail, blockers))
                    continue;

                direction = candidate;
                distance = rail;
                return true;
            }

            if (IsPathClear(finalBounds, finalRoot, candidate, minDistance, blockers))
            {
                direction = candidate;
                distance = minDistance;
                return true;
            }
        }

        direction = Vector2.zero;
        distance = 0f;
        return false;
    }

    private bool IsPathClear(
        Bounds finalBounds,
        Vector3 finalRoot,
        Vector2 sourceDirection,
        float distance,
        List<Bounds> blockers)
    {
        if (blockers == null || blockers.Count == 0)
            return true;

        Vector3 startRoot = finalRoot + (Vector3)(sourceDirection * distance);
        int samples = Mathf.Clamp(pathSamples, 4, 20);

        // 마지막 샘플은 정상적인 Edge-to-Edge 도킹이므로 제외합니다.
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)samples;
            Vector3 root = Vector3.Lerp(startRoot, finalRoot, t);
            Bounds moving = finalBounds;
            moving.center += root - finalRoot;

            for (int b = 0; b < blockers.Count; b++)
            {
                if (Overlaps2D(moving, blockers[b], pathClearance))
                    return false;
            }
        }

        return true;
    }

    private static Vector2 Cardinalize(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return Vector2.right;

        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? (direction.x >= 0f ? Vector2.right : Vector2.left)
            : (direction.y >= 0f ? Vector2.up : Vector2.down);
    }

    private static bool TryGetFloorBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        bool found = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || renderer.sprite == null)
                continue;

            string objectName = renderer.name;
            bool floor = objectName.StartsWith("Tile_", StringComparison.Ordinal) ||
                         objectName.StartsWith("ShowTile_", StringComparison.Ordinal) ||
                         renderer.GetComponent<BattleWalkableField>() != null;
            if (!floor)
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
}

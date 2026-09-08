using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Procedural Room의 진입 조립만 담당합니다.
///
/// 핵심 규칙:
/// - 최종 Room 형태 / 개별 MapBlock 형태는 변경하지 않습니다.
/// - 퇴장 로직도 변경하지 않습니다.
/// - 다만 화면 밖에서 '이동해서 들어오는 운송 단위'는 최소 2x2 Footprint를 보장합니다.
/// - 1x1 / 1xN / Nx1 Piece는 혼자 들어오지 않고, 인접 Piece와 임시 Entry Transport로 묶여 함께 이동합니다.
/// - 도킹 완료 후에는 임시 Transport만 해제되고 원래 MapBlock 부모 구조로 복귀합니다.
/// - 이미 도킹한 이전 Wave와 Persistent 4x4를 관통하는 Rail은 사용하지 않습니다.
/// - 충돌 이후의 쾅 / 반동 / VFX / 카메라 충격은 BattleTileDockingPresentationManager가 공통 처리합니다.
/// </summary>
[DefaultExecutionOrder(30000)]
[DisallowMultipleComponent]
public sealed class BattleProceduralAssemblyAnimator : MonoBehaviour
{
    [Header("Entry Transport Minimum Footprint")]
    [Tooltip("들어오는 이동 단위의 최소 가로 크기입니다. 실제 MapBlock 자체 크기는 제한하지 않습니다.")]
    [SerializeField, Min(1f)] private float minimumEntryWidth = 2f;
    [Tooltip("들어오는 이동 단위의 최소 세로 크기입니다. 실제 MapBlock 자체 크기는 제한하지 않습니다.")]
    [SerializeField, Min(1f)] private float minimumEntryHeight = 2f;
    [Tooltip("작은 Piece를 인접 Transport와 합칠 때 Edge 접촉으로 보는 여유값입니다.")]
    [SerializeField, Range(0f, 0.35f)] private float mergeAdjacencyTolerance = 0.16f;

    [Header("Safe Rail")]
    [SerializeField, Min(8f)] private float preferredRailDistance = 36f;
    [SerializeField, Min(0.5f)] private float minimumRailDistance = 1.25f;
    [SerializeField, Min(0.01f)] private float railSearchStep = 1f;
    [SerializeField, Range(4, 20)] private int pathSamples = 12;
    [SerializeField, Range(0f, 0.2f)] private float pathClearance = 0.055f;
    [SerializeField, Min(0f)] private float waveGap = 0f;
    [SerializeField, Min(0.05f)] private float fallbackScaleDuration = 0.22f;

    private readonly HashSet<int> processedGroups = new();
    private RoomBaseTemplate baseTemplate;
    private BattleRoomManager roomManager;
    private int transportSerial;

    private sealed class SubPiecePlan
    {
        public MapBlock block;
        public Transform originalParent;
        public Vector3 finalPosition;
        public Bounds finalBounds;
        public int sourceWave;
    }

    private sealed class GroupPlan
    {
        public MapBlock group;
        public int index;
        public readonly List<SubPiecePlan> pieces = new();
    }

    /// <summary>
    /// Entry에서만 존재하는 임시 운송 단위입니다.
    /// 도킹이 끝나면 pieces는 각자의 원래 부모로 복귀합니다.
    /// </summary>
    private sealed class EntryUnitPlan
    {
        public readonly List<SubPiecePlan> pieces = new();
        public int wave;
        public Bounds finalBounds;
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

        // BattleRoomManager가 Group Root에 걸어 둔 임시 Tween은 제거하고 최종 기준점에 고정합니다.
        // 실제 화면 진입은 아래의 2x2+ Entry Transport만 수행합니다.
        for (int i = 0; i < newGroups.Count; i++)
        {
            GroupPlan plan = newGroups[i];
            if (plan.group == null)
                continue;

            plan.group.transform.DOKill(false);
            if (plan.group.HasEntryDestination)
                plan.group.transform.position = plan.group.EntryDestination;

            RefreshFinalPieceData(plan);
        }

        List<EntryUnitPlan> entryUnits = BuildEntryUnits(newGroups);
        EnsureMinimumIncomingFootprint(entryUnits);
        if (entryUnits.Count == 0)
            return;

        entryUnits.Sort(CompareEntryUnits);

        List<Bounds> previousWaveBlockers = new();
        if (baseTemplate != null && baseTemplate.HasPersistentBase)
        {
            previousWaveBlockers.Add(new Bounds(
                baseTemplate.FixedCenterWorld,
                new Vector3(
                    RoomBaseTemplate.FixedBaseTiles,
                    RoomBaseTemplate.FixedBaseTiles,
                    0.1f)));
        }

        float roomMoveDuration = roomManager != null && roomManager.CurrentRoom != null
            ? Mathf.Max(0.05f, roomManager.CurrentRoom.largePieceEntryDuration)
            : 0.72f;
        float settleDuration = BattleTileDockingPresentationManager.SharedSettleDuration;
        float waveLength = roomMoveDuration + settleDuration + Mathf.Max(0f, waveGap);

        List<int> waveOrder = CollectWaveOrder(entryUnits);
        int finalWave = waveOrder.Count > 0 ? waveOrder[waveOrder.Count - 1] : 1;
        EntryUnitPlan finalImpactUnit = FindLargestUnitInWave(entryUnits, finalWave);

        for (int waveOrdinal = 0; waveOrdinal < waveOrder.Count; waveOrdinal++)
        {
            int wave = waveOrder[waveOrdinal];
            float waveDelay = waveOrdinal * waveLength;

            for (int i = 0; i < entryUnits.Count; i++)
            {
                EntryUnitPlan unit = entryUnits[i];
                if (unit == null || unit.wave != wave)
                    continue;

                AnimateEntryUnit(
                    unit,
                    previousWaveBlockers,
                    waveDelay,
                    roomMoveDuration,
                    unit == finalImpactUnit);
            }

            // 같은 Wave끼리는 동시에 접근하므로 서로를 장애물로 잡지 않습니다.
            // Wave가 끝난 뒤에만 다음 Wave의 blocker로 추가합니다.
            for (int i = 0; i < entryUnits.Count; i++)
            {
                EntryUnitPlan unit = entryUnits[i];
                if (unit != null && unit.wave == wave)
                    previousWaveBlockers.Add(unit.finalBounds);
            }
        }

        for (int i = 0; i < newGroups.Count; i++)
        {
            if (newGroups[i].group != null)
                processedGroups.Add(newGroups[i].group.GetInstanceID());
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
                originalParent = child.transform.parent,
                finalPosition = child.transform.position,
                sourceWave = plan.index
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

            piece.originalParent = piece.block.transform.parent;
            piece.finalPosition = piece.block.transform.position;
            if (!TryGetFloorBounds(piece.block.transform, out Bounds bounds))
                bounds = new Bounds(piece.finalPosition, Vector3.one * 0.95f);
            piece.finalBounds = bounds;
        }
    }

    private static List<EntryUnitPlan> BuildEntryUnits(List<GroupPlan> groups)
    {
        List<EntryUnitPlan> units = new();
        if (groups == null)
            return units;

        for (int g = 0; g < groups.Count; g++)
        {
            GroupPlan group = groups[g];
            if (group == null)
                continue;

            for (int p = 0; p < group.pieces.Count; p++)
            {
                SubPiecePlan piece = group.pieces[p];
                if (piece == null || piece.block == null)
                    continue;

                EntryUnitPlan unit = new()
                {
                    wave = Mathf.Max(1, piece.sourceWave),
                    finalBounds = piece.finalBounds
                };
                unit.pieces.Add(piece);
                units.Add(unit);
            }
        }

        return units;
    }

    /// <summary>
    /// 실제 MapBlock을 합치지 않고 Entry에서만 임시 운송 단위를 병합합니다.
    /// 작은 Piece를 미래 Wave Piece와 묶는 경우 wave=max를 사용하므로,
    /// 미래 Piece를 앞당겨 보내지 않고 작은 Piece가 뒤로 기다렸다가 같이 들어옵니다.
    /// </summary>
    private void EnsureMinimumIncomingFootprint(List<EntryUnitPlan> units)
    {
        if (units == null || units.Count <= 0)
            return;

        int safety = 0;
        while (safety++ < 2048)
        {
            int smallIndex = FindFirstUndersizedUnit(units);
            if (smallIndex < 0)
                break;

            if (units.Count == 1)
            {
                Debug.LogError(
                    "[BattleProceduralAssemblyAnimator] Procedural entry has only one transport and it is smaller than the required 2x2 footprint. " +
                    "It will remain at its final position instead of violating the incoming-size invariant.");
                break;
            }

            int mergeIndex = FindBestMergeCandidate(units, smallIndex);
            if (mergeIndex < 0)
                break;

            EntryUnitPlan small = units[smallIndex];
            EntryUnitPlan target = units[mergeIndex];

            target.pieces.AddRange(small.pieces);
            target.wave = Mathf.Max(target.wave, small.wave);
            target.finalBounds.Encapsulate(small.finalBounds);

            units.RemoveAt(smallIndex);
        }
    }

    private int FindFirstUndersizedUnit(List<EntryUnitPlan> units)
    {
        for (int i = 0; i < units.Count; i++)
        {
            EntryUnitPlan unit = units[i];
            if (unit != null && !MeetsEntryFootprint(unit.finalBounds))
                return i;
        }
        return -1;
    }

    private bool MeetsEntryFootprint(Bounds bounds)
    {
        return bounds.size.x + 0.001f >= Mathf.Max(1f, minimumEntryWidth) &&
               bounds.size.y + 0.001f >= Mathf.Max(1f, minimumEntryHeight);
    }

    private int FindBestMergeCandidate(List<EntryUnitPlan> units, int sourceIndex)
    {
        EntryUnitPlan source = units[sourceIndex];
        if (source == null)
            return -1;

        int best = -1;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < units.Count; i++)
        {
            if (i == sourceIndex || units[i] == null)
                continue;

            EntryUnitPlan candidate = units[i];
            Bounds merged = candidate.finalBounds;
            merged.Encapsulate(source.finalBounds);

            float gap = BoundsGap2D(source.finalBounds, candidate.finalBounds);
            bool adjacent = gap <= Mathf.Max(0f, mergeAdjacencyTolerance);
            bool becomesValid = MeetsEntryFootprint(merged);

            // 인접 연결을 최우선으로 하고, 그 다음 바로 2x2 이상이 되는 조합,
            // 그 뒤 가까운 거리 / 기존 큰 운송 단위를 선호합니다.
            float score = adjacent ? 10000f : 0f;
            if (becomesValid)
                score += 2500f;
            score -= gap * 100f;
            score += Mathf.Min(merged.size.x * merged.size.y, 64f);

            // 작은 Piece 때문에 미래 Wave가 앞당겨지는 것을 막습니다.
            // 같은 조건이면 source와 같거나 더 늦은 Wave를 우선합니다.
            if (candidate.wave >= source.wave)
                score += 120f;

            if (score <= bestScore)
                continue;

            bestScore = score;
            best = i;
        }

        return best;
    }

    private static float BoundsGap2D(Bounds a, Bounds b)
    {
        float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
        float dy = Mathf.Max(0f, Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y));
        return dx + dy;
    }

    private static int CompareEntryUnits(EntryUnitPlan a, EntryUnitPlan b)
    {
        if (ReferenceEquals(a, b))
            return 0;
        if (a == null)
            return 1;
        if (b == null)
            return -1;

        int wave = a.wave.CompareTo(b.wave);
        if (wave != 0)
            return wave;

        float areaA = a.finalBounds.size.x * a.finalBounds.size.y;
        float areaB = b.finalBounds.size.x * b.finalBounds.size.y;
        return areaB.CompareTo(areaA);
    }

    private static List<int> CollectWaveOrder(List<EntryUnitPlan> units)
    {
        List<int> waves = new();
        for (int i = 0; i < units.Count; i++)
        {
            EntryUnitPlan unit = units[i];
            if (unit == null || waves.Contains(unit.wave))
                continue;
            waves.Add(unit.wave);
        }
        waves.Sort();
        return waves;
    }

    private static EntryUnitPlan FindLargestUnitInWave(List<EntryUnitPlan> units, int wave)
    {
        EntryUnitPlan best = null;
        float bestArea = -1f;
        for (int i = 0; i < units.Count; i++)
        {
            EntryUnitPlan unit = units[i];
            if (unit == null || unit.wave != wave)
                continue;

            float area = Mathf.Max(0.01f, unit.finalBounds.size.x * unit.finalBounds.size.y);
            if (area <= bestArea)
                continue;

            best = unit;
            bestArea = area;
        }
        return best;
    }

    private void AnimateEntryUnit(
        EntryUnitPlan unit,
        List<Bounds> blockers,
        float delay,
        float duration,
        bool finalImpact)
    {
        if (unit == null || unit.pieces.Count == 0)
            return;

        // 하드 invariant: 이동해서 들어오는 단위는 최소 2x2 Footprint.
        if (!MeetsEntryFootprint(unit.finalBounds))
        {
            RestoreUnitPiecesImmediate(unit);
            return;
        }

        Vector2 baseCenter = baseTemplate != null && baseTemplate.HasPersistentBase
            ? (Vector2)baseTemplate.FixedCenterWorld
            : Vector2.zero;

        Vector2 radial = (Vector2)unit.finalBounds.center - baseCenter;
        Vector2 primary = Cardinalize(radial);
        Vector3 finalRoot = unit.finalBounds.center;

        if (!TryResolveSafeRail(
                unit.finalBounds,
                finalRoot,
                primary,
                blockers,
                out Vector2 sourceDirection,
                out float railDistance))
        {
            PlayScaleFallback(unit, delay);
            return;
        }

        Transform transport = CreateTransport(unit, finalRoot);
        if (transport == null)
            return;

        Vector3 startRoot = finalRoot + (Vector3)(sourceDirection * railDistance);
        Vector2 travelDirection = -sourceDirection.normalized;
        transport.position = startRoot;

        Sequence sequence = DOTween.Sequence();
        if (delay > 0f)
            sequence.AppendInterval(delay);

        sequence.Append(
            transport.DOMove(finalRoot, Mathf.Max(0.05f, duration))
                .SetEase(Ease.InCubic));

        BattleTileDockingPresentationManager dockingPresentation = BattleTileDockingPresentationManager.Instance;
        if (dockingPresentation != null)
        {
            dockingPresentation.AppendDockSettle(
                sequence,
                transport,
                finalRoot,
                travelDirection,
                ResolveContactPoint(unit.finalBounds, travelDirection),
                ResolveImpactStrength(unit),
                true,
                finalImpact);
        }
        else
        {
            Vector3 rebound = finalRoot - (Vector3)(travelDirection * 0.055f);
            sequence.Append(transport.DOMove(rebound, 0.035f).SetEase(Ease.OutQuad));
            sequence.Append(transport.DOMove(finalRoot, 0.055f).SetEase(Ease.OutCubic));
        }

        sequence.OnComplete(() =>
        {
            if (transport != null)
                transport.position = finalRoot;
            RestorePiecesAndDestroyTransport(unit, transport);
        });
    }

    private Transform CreateTransport(EntryUnitPlan unit, Vector3 finalRoot)
    {
        if (unit == null || unit.pieces.Count == 0)
            return null;

        GameObject transportObject = new($"ProceduralEntryTransport_{unit.wave}_{++transportSerial}");
        Transform transport = transportObject.transform;
        transport.position = finalRoot;

        for (int i = 0; i < unit.pieces.Count; i++)
        {
            SubPiecePlan piece = unit.pieces[i];
            if (piece == null || piece.block == null)
                continue;

            Transform pieceTransform = piece.block.transform;
            piece.originalParent = pieceTransform.parent;
            pieceTransform.DOKill(false);
            pieceTransform.position = piece.finalPosition;
            pieceTransform.SetParent(transport, true);
        }

        return transport;
    }

    private void RestorePiecesAndDestroyTransport(EntryUnitPlan unit, Transform transport)
    {
        if (unit != null)
        {
            for (int i = 0; i < unit.pieces.Count; i++)
            {
                SubPiecePlan piece = unit.pieces[i];
                if (piece == null || piece.block == null)
                    continue;

                Transform pieceTransform = piece.block.transform;
                pieceTransform.SetParent(piece.originalParent, true);
                pieceTransform.position = piece.finalPosition;
            }
        }

        if (transport != null)
            Destroy(transport.gameObject);
    }

    private void RestoreUnitPiecesImmediate(EntryUnitPlan unit)
    {
        if (unit == null)
            return;

        for (int i = 0; i < unit.pieces.Count; i++)
        {
            SubPiecePlan piece = unit.pieces[i];
            if (piece == null || piece.block == null)
                continue;

            piece.block.transform.position = piece.finalPosition;
        }
    }

    private void PlayScaleFallback(EntryUnitPlan unit, float delay)
    {
        Vector3 finalRoot = unit.finalBounds.center;
        Transform transport = CreateTransport(unit, finalRoot);
        if (transport == null)
            return;

        Vector3 finalScale = transport.localScale;
        transport.localScale = new Vector3(finalScale.x * 0.86f, finalScale.y * 0.86f, finalScale.z);
        transport
            .DOScale(finalScale, Mathf.Max(0.05f, fallbackScaleDuration))
            .SetDelay(Mathf.Max(0f, delay))
            .SetEase(Ease.OutBack)
            .OnComplete(() => RestorePiecesAndDestroyTransport(unit, transport));

        Debug.LogWarning(
            $"[BattleProceduralAssemblyAnimator] 2x2+ Entry Transport has no collision-free straight rail. " +
            "Used scale-in fallback without crossing existing tiles.");
    }

    private static float ResolveImpactStrength(EntryUnitPlan unit)
    {
        if (unit == null)
            return 1f;

        float area = Mathf.Max(1f, unit.finalBounds.size.x * unit.finalBounds.size.y);
        return Mathf.Lerp(0.90f, 1.45f, Mathf.InverseLerp(4f, 32f, area));
    }

    private static Vector3 ResolveContactPoint(Bounds bounds, Vector2 travelDirection)
    {
        Vector2 direction = travelDirection.sqrMagnitude > 0.001f
            ? travelDirection.normalized
            : Vector2.down;
        float support = Mathf.Abs(direction.x) * bounds.extents.x +
                        Mathf.Abs(direction.y) * bounds.extents.y;
        support = Mathf.Max(0f, support - 0.02f);
        return bounds.center + (Vector3)(direction * support);
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

        // 마지막 샘플은 정상적인 Edge-to-Edge 도킹 지점이므로 검사하지 않습니다.
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

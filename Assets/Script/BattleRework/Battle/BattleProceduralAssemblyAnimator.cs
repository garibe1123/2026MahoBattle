using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Procedural Room의 진입 조립만 담당합니다.
///
/// Invariant:
/// - 최종 Room 형태 / 개별 MapBlock 형태 / 퇴장 로직은 변경하지 않습니다.
/// - 들어오는 운송 단위는 최소 2x2 Footprint를 보장합니다.
/// - 모든 Entry Transport는 어떤 진입 Tween도 시작하기 전에 화면 밖 Staging 위치로 이동합니다.
/// - 필드 내부에서 '차례를 기다리는' 새 타일은 허용하지 않습니다.
/// - 짧은 Rail / 제자리 Scale-in / 최종 위치 대기 fallback을 사용하지 않습니다.
/// - 이전 Wave와 Persistent 4x4를 관통하지 않는 완전한 Offscreen Cardinal Rail만 허용합니다.
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

    [Header("Offscreen Staging / Safe Rail")]
    [Tooltip("카메라가 없어도 보장할 최소 외부 대기 거리입니다. 짧은 Rail로 줄이지 않습니다.")]
    [SerializeField, Min(8f)] private float preferredRailDistance = 36f;
    [Tooltip("Transport 전체가 화면 밖으로 빠져 있도록 추가하는 여백입니다.")]
    [SerializeField, Min(0.25f)] private float offscreenMargin = 2f;
    [SerializeField, Range(6, 24)] private int pathSamples = 14;
    [SerializeField, Range(0f, 0.2f)] private float pathClearance = 0.055f;
    [SerializeField, Min(0f)] private float waveGap = 0f;

    private readonly HashSet<MapBlock> processedGroups = new();
    private RoomBaseTemplate baseTemplate;
    private BattleRoomManager roomManager;
    private int transportSerial;
    private bool scanArmed = true;

    private sealed class SubPiecePlan
    {
        public MapBlock block;
        public Transform originalParent;
        public Vector3 finalPosition;
        public Bounds finalBounds;
        public int sourceWave;
        public readonly List<Bounds> floorBounds = new();
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
        public readonly List<Bounds> floorBounds = new();
        public int wave;
        public Bounds finalBounds;

        public bool railResolved;
        public Vector2 sourceDirection;
        public Vector2 travelDirection;
        public float railDistance;
        public Vector3 finalRoot;
        public Vector3 stagedRoot;
        public Transform transport;
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
        if (roomManager == null)
            return;

        // 전투 중에는 전역 MapBlock 검색을 하지 않습니다.
        // Room 진입 Transition 당 정확히 한 번만 Procedural Group을 스캔합니다.
        if (!roomManager.IsTransitioning)
        {
            scanArmed = true;
            processedGroups.RemoveWhere(group => group == null);
            return;
        }

        if (!scanArmed || roomManager.CurrentRoom == null || !roomManager.CurrentRoom.useProceduralRoom)
            return;

        scanArmed = false;
        ProcessCurrentEntryBatch();
    }

    private void ProcessCurrentEntryBatch()
    {
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
            if (processedGroups.Contains(block))
                continue;

            GroupPlan plan = BuildGroupPlan(block);
            if (plan != null && plan.pieces.Count > 0)
                newGroups.Add(plan);
        }

        if (newGroups.Count == 0)
            return;

        newGroups.Sort((a, b) => a.index.CompareTo(b.index));

        // BattleRoomManager가 Group Root에 걸어 둔 기존 Tween은 여기서 완전히 제거합니다.
        // 이후 실제 화면 이동은 Entry Transport만 수행합니다.
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
        ResolveRailsWithEarlierWavePromotion(entryUnits);

        // 핵심: 모든 Unit을 먼저 화면 밖으로 보낸 뒤에만 Tween을 시작합니다.
        // 따라서 뒤 Wave 타일이 필드에서 미리 기다리는 프레임이 존재하지 않습니다.
        StageAllUnitsOffscreen(entryUnits);
        AnimateStagedUnits(entryUnits);

        for (int i = 0; i < newGroups.Count; i++)
        {
            if (newGroups[i].group != null)
                processedGroups.Add(newGroups[i].group);
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
            piece.floorBounds.Clear();

            if (!CollectFloorBounds(piece.block.transform, piece.floorBounds, out Bounds aggregate))
            {
                aggregate = new Bounds(piece.finalPosition, Vector3.one * 0.95f);
                piece.floorBounds.Add(aggregate);
            }

            piece.finalBounds = aggregate;
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
                unit.floorBounds.AddRange(piece.floorBounds);
                units.Add(unit);
            }
        }

        return units;
    }

    /// <summary>
    /// 실제 MapBlock을 합치지 않고 Entry에서만 임시 운송 단위를 병합합니다.
    /// 1x1 / 1xN / Nx1은 독립 진입하지 않습니다.
    /// 병합은 실제로 맞닿은 Piece끼리만 허용하며, 합친 Transport도 Persistent 4x4를
    /// 관통하지 않고 화면 밖에서 들어올 수 있는 조합만 선택합니다.
    /// </summary>
    private void EnsureMinimumIncomingFootprint(List<EntryUnitPlan> units)
    {
        if (units == null || units.Count == 0)
            return;

        int safety = 0;
        while (safety++ < 2048)
        {
            int smallIndex = FindFirstUndersizedUnit(units);
            if (smallIndex < 0)
                return;

            int mergeIndex = FindBestMergeCandidate(units, smallIndex);
            if (mergeIndex < 0)
            {
                Debug.LogError(
                    "[BattleProceduralAssemblyAnimator] Could not build a connected 2x2+ incoming transport. " +
                    "The invalid unit will be kept offscreen instead of appearing inside the field.");
                return;
            }

            EntryUnitPlan small = units[smallIndex];
            EntryUnitPlan target = units[mergeIndex];

            target.pieces.AddRange(small.pieces);
            target.floorBounds.AddRange(small.floorBounds);
            target.wave = Mathf.Min(target.wave, small.wave);
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
        List<Bounds> baseBlockers = BuildPersistentBaseBlockers();

        for (int i = 0; i < units.Count; i++)
        {
            if (i == sourceIndex || units[i] == null)
                continue;

            EntryUnitPlan candidate = units[i];
            float gap = BoundsGap2D(source.finalBounds, candidate.finalBounds);
            if (gap > Mathf.Max(0f, mergeAdjacencyTolerance))
                continue;

            EntryUnitPlan mergedPreview = CreateMergedPreview(source, candidate);
            if (!CanResolveAnyOffscreenRail(mergedPreview, baseBlockers))
                continue;

            bool becomesValid = MeetsEntryFootprint(mergedPreview.finalBounds);
            float area = mergedPreview.finalBounds.size.x * mergedPreview.finalBounds.size.y;
            float score = becomesValid ? 5000f : 0f;
            score += source.wave == candidate.wave ? 600f : 0f;
            score -= area * 2f; // 같은 조건이면 지나치게 큰 운송 단위로 합치지 않습니다.

            if (score <= bestScore)
                continue;

            bestScore = score;
            best = i;
        }

        return best;
    }

    private static EntryUnitPlan CreateMergedPreview(EntryUnitPlan a, EntryUnitPlan b)
    {
        EntryUnitPlan merged = new()
        {
            wave = Mathf.Min(a.wave, b.wave),
            finalBounds = a.finalBounds
        };
        merged.finalBounds.Encapsulate(b.finalBounds);
        merged.floorBounds.AddRange(a.floorBounds);
        merged.floorBounds.AddRange(b.floorBounds);
        return merged;
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

    /// <summary>
    /// 기존 Wave에서 완전한 Offscreen Rail이 막히는 Unit은 더 이른 Wave로만 당겨 봅니다.
    /// 블로커는 시간이 갈수록 늘어나므로 '나중으로 미루기'는 해결책이 아닙니다.
    /// 이 과정은 Unit 형태를 바꾸지 않으며 관통 fallback도 만들지 않습니다.
    /// </summary>
    private void ResolveRailsWithEarlierWavePromotion(List<EntryUnitPlan> units)
    {
        if (units == null || units.Count == 0)
            return;

        bool changed;
        int safety = 0;
        do
        {
            changed = false;
            units.Sort(CompareEntryUnits);

            for (int i = 0; i < units.Count; i++)
            {
                EntryUnitPlan unit = units[i];
                if (unit == null || !MeetsEntryFootprint(unit.finalBounds))
                    continue;

                int originalWave = unit.wave;
                int resolvedWave = -1;
                Vector2 resolvedDirection = Vector2.zero;
                float resolvedDistance = 0f;

                for (int candidateWave = originalWave; candidateWave >= 1; candidateWave--)
                {
                    List<Bounds> blockers = BuildEarlierWaveBlockers(units, unit, candidateWave);
                    if (!TryResolveOffscreenRail(
                            unit,
                            blockers,
                            out Vector2 sourceDirection,
                            out float railDistance))
                        continue;

                    resolvedWave = candidateWave;
                    resolvedDirection = sourceDirection;
                    resolvedDistance = railDistance;
                    break;
                }

                if (resolvedWave < 0)
                {
                    unit.railResolved = false;
                    continue;
                }

                if (resolvedWave != originalWave)
                {
                    unit.wave = resolvedWave;
                    changed = true;
                }

                unit.railResolved = true;
                unit.sourceDirection = resolvedDirection;
                unit.travelDirection = -resolvedDirection.normalized;
                unit.railDistance = resolvedDistance;
            }
        }
        while (changed && ++safety < 16);

        // 최종 Wave 구성이 정해진 뒤 한 번 더 계산하여 blocker 상태와 Rail을 동기화합니다.
        units.Sort(CompareEntryUnits);
        for (int i = 0; i < units.Count; i++)
        {
            EntryUnitPlan unit = units[i];
            if (unit == null || !MeetsEntryFootprint(unit.finalBounds))
            {
                if (unit != null)
                    unit.railResolved = false;
                continue;
            }

            List<Bounds> blockers = BuildEarlierWaveBlockers(units, unit, unit.wave);
            unit.railResolved = TryResolveOffscreenRail(
                unit,
                blockers,
                out Vector2 sourceDirection,
                out float railDistance);

            if (!unit.railResolved)
                continue;

            unit.sourceDirection = sourceDirection;
            unit.travelDirection = -sourceDirection.normalized;
            unit.railDistance = railDistance;
        }
    }

    private List<Bounds> BuildEarlierWaveBlockers(
        List<EntryUnitPlan> units,
        EntryUnitPlan self,
        int wave)
    {
        List<Bounds> blockers = BuildPersistentBaseBlockers();
        for (int i = 0; i < units.Count; i++)
        {
            EntryUnitPlan other = units[i];
            if (other == null || other == self || other.wave >= wave)
                continue;

            AppendUnitBlockers(other, blockers);
        }
        return blockers;
    }

    private List<Bounds> BuildPersistentBaseBlockers()
    {
        List<Bounds> blockers = new();
        if (baseTemplate == null || !baseTemplate.HasPersistentBase)
            return blockers;

        blockers.Add(new Bounds(
            baseTemplate.FixedCenterWorld,
            new Vector3(
                RoomBaseTemplate.FixedBaseTiles,
                RoomBaseTemplate.FixedBaseTiles,
                0.1f)));
        return blockers;
    }

    private static void AppendUnitBlockers(EntryUnitPlan unit, List<Bounds> target)
    {
        if (unit == null || target == null)
            return;

        if (unit.floorBounds.Count > 0)
        {
            target.AddRange(unit.floorBounds);
            return;
        }

        target.Add(unit.finalBounds);
    }

    private bool CanResolveAnyOffscreenRail(EntryUnitPlan unit, List<Bounds> blockers)
    {
        return TryResolveOffscreenRail(unit, blockers, out _, out _);
    }

    private bool TryResolveOffscreenRail(
        EntryUnitPlan unit,
        List<Bounds> blockers,
        out Vector2 direction,
        out float distance)
    {
        if (unit == null)
        {
            direction = Vector2.zero;
            distance = 0f;
            return false;
        }

        Vector2 baseCenter = baseTemplate != null && baseTemplate.HasPersistentBase
            ? (Vector2)baseTemplate.FixedCenterWorld
            : Vector2.zero;
        Vector2 primary = Cardinalize((Vector2)unit.finalBounds.center - baseCenter);
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
            Vector2 candidate = Cardinalize(candidates[i]);
            float requiredDistance = ResolveFullOffscreenDistance(unit.finalBounds, candidate);
            if (!IsPathClear(unit, candidate, requiredDistance, blockers))
                continue;

            direction = candidate;
            distance = requiredDistance;
            return true;
        }

        direction = Vector2.zero;
        distance = 0f;
        return false;
    }

    private float ResolveFullOffscreenDistance(Bounds finalBounds, Vector2 sourceDirection)
    {
        float distance = Mathf.Max(8f, preferredRailDistance);
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic)
            return distance;

        float halfHeight = camera.orthographicSize;
        float halfWidth = halfHeight * Mathf.Max(0.1f, camera.aspect);
        Vector3 center = camera.transform.position;
        float margin = Mathf.Max(0.25f, offscreenMargin);

        if (Mathf.Abs(sourceDirection.x) >= Mathf.Abs(sourceDirection.y))
        {
            if (sourceDirection.x >= 0f)
            {
                float right = center.x + halfWidth + margin;
                distance = Mathf.Max(distance, right - finalBounds.min.x);
            }
            else
            {
                float left = center.x - halfWidth - margin;
                distance = Mathf.Max(distance, finalBounds.max.x - left);
            }
        }
        else
        {
            if (sourceDirection.y >= 0f)
            {
                float top = center.y + halfHeight + margin;
                distance = Mathf.Max(distance, top - finalBounds.min.y);
            }
            else
            {
                float bottom = center.y - halfHeight - margin;
                distance = Mathf.Max(distance, finalBounds.max.y - bottom);
            }
        }

        return Mathf.Max(0.5f, distance);
    }

    private bool IsPathClear(
        EntryUnitPlan unit,
        Vector2 sourceDirection,
        float distance,
        List<Bounds> blockers)
    {
        if (unit == null || blockers == null || blockers.Count == 0)
            return true;

        Vector3 finalRoot = unit.finalBounds.center;
        Vector3 startRoot = finalRoot + (Vector3)(sourceDirection * distance);
        int samples = Mathf.Clamp(pathSamples, 6, 24);

        for (int sample = 0; sample < samples; sample++)
        {
            // t=1은 정상적인 최종 도킹 지점이므로 제외합니다.
            float t = sample / (float)samples;
            Vector3 root = Vector3.Lerp(startRoot, finalRoot, t);
            Vector3 delta = root - finalRoot;

            if (unit.floorBounds.Count == 0)
            {
                Bounds moving = unit.finalBounds;
                moving.center += delta;
                if (OverlapsAny(moving, blockers))
                    return false;
                continue;
            }

            for (int f = 0; f < unit.floorBounds.Count; f++)
            {
                Bounds moving = unit.floorBounds[f];
                moving.center += delta;
                if (OverlapsAny(moving, blockers))
                    return false;
            }
        }

        return true;
    }

    private bool OverlapsAny(Bounds moving, List<Bounds> blockers)
    {
        for (int i = 0; i < blockers.Count; i++)
        {
            if (Overlaps2D(moving, blockers[i], pathClearance))
                return true;
        }
        return false;
    }

    private void StageAllUnitsOffscreen(List<EntryUnitPlan> units)
    {
        for (int i = 0; i < units.Count; i++)
        {
            EntryUnitPlan unit = units[i];
            if (unit == null || unit.pieces.Count == 0)
                continue;

            unit.finalRoot = unit.finalBounds.center;

            if (!MeetsEntryFootprint(unit.finalBounds) || !unit.railResolved)
            {
                StageInvalidUnitOffscreen(unit);
                continue;
            }

            unit.transport = CreateTransport(unit, unit.finalRoot);
            if (unit.transport == null)
            {
                unit.railResolved = false;
                continue;
            }

            unit.stagedRoot = unit.finalRoot + (Vector3)(unit.sourceDirection * unit.railDistance);
            unit.transport.position = unit.stagedRoot;
        }
    }

    private void StageInvalidUnitOffscreen(EntryUnitPlan unit)
    {
        if (unit == null || unit.pieces.Count == 0)
            return;

        Vector2 baseCenter = baseTemplate != null && baseTemplate.HasPersistentBase
            ? (Vector2)baseTemplate.FixedCenterWorld
            : Vector2.zero;
        Vector2 sourceDirection = Cardinalize((Vector2)unit.finalBounds.center - baseCenter);
        float distance = ResolveFullOffscreenDistance(unit.finalBounds, sourceDirection);

        unit.transport = CreateTransport(unit, unit.finalBounds.center);
        if (unit.transport != null)
            unit.transport.position = unit.finalBounds.center + (Vector3)(sourceDirection * distance);

        unit.railResolved = false;
        Debug.LogError(
            "[BattleProceduralAssemblyAnimator] Incoming transport had no valid full offscreen rail. " +
            "It was quarantined outside the camera instead of waiting or popping inside the field.");
    }

    private void AnimateStagedUnits(List<EntryUnitPlan> units)
    {
        List<int> waveOrder = CollectResolvedWaveOrder(units);
        if (waveOrder.Count == 0)
            return;

        float roomMoveDuration = roomManager != null && roomManager.CurrentRoom != null
            ? Mathf.Max(0.05f, roomManager.CurrentRoom.largePieceEntryDuration)
            : 0.72f;
        float waveLength = roomMoveDuration +
                           BattleTileDockingPresentationManager.SharedSettleDuration +
                           Mathf.Max(0f, waveGap);

        int finalWave = waveOrder[waveOrder.Count - 1];
        EntryUnitPlan finalImpactUnit = FindLargestResolvedUnitInWave(units, finalWave);

        for (int waveOrdinal = 0; waveOrdinal < waveOrder.Count; waveOrdinal++)
        {
            int wave = waveOrder[waveOrdinal];
            float delay = waveOrdinal * waveLength;

            for (int i = 0; i < units.Count; i++)
            {
                EntryUnitPlan unit = units[i];
                if (unit == null || !unit.railResolved || unit.transport == null || unit.wave != wave)
                    continue;

                PlayStagedDockSequence(
                    unit,
                    delay,
                    roomMoveDuration,
                    unit == finalImpactUnit);
            }
        }
    }

    private void PlayStagedDockSequence(
        EntryUnitPlan unit,
        float delay,
        float duration,
        bool finalImpact)
    {
        Sequence sequence = DOTween.Sequence();
        if (delay > 0f)
            sequence.AppendInterval(delay);

        sequence.Append(
            unit.transport.DOMove(unit.finalRoot, Mathf.Max(0.05f, duration))
                .SetEase(Ease.InCubic));

        BattleTileDockingPresentationManager dockingPresentation = BattleTileDockingPresentationManager.Instance;
        if (dockingPresentation != null)
        {
            dockingPresentation.AppendDockSettle(
                sequence,
                unit.transport,
                unit.finalRoot,
                unit.travelDirection,
                ResolveContactPoint(unit.finalBounds, unit.travelDirection),
                ResolveImpactStrength(unit),
                true,
                finalImpact);
        }
        else
        {
            Vector3 rebound = unit.finalRoot - (Vector3)(unit.travelDirection * 0.055f);
            sequence.Append(unit.transport.DOMove(rebound, 0.035f).SetEase(Ease.OutQuad));
            sequence.Append(unit.transport.DOMove(unit.finalRoot, 0.055f).SetEase(Ease.OutCubic));
        }

        sequence.OnComplete(() => RestorePiecesAndDestroyTransport(unit));
    }

    private Transform CreateTransport(EntryUnitPlan unit, Vector3 finalRoot)
    {
        if (unit == null || unit.pieces.Count == 0)
            return null;

        GameObject transportObject = new($"ProceduralEntryTransport_{unit.wave}_{++transportSerial}");
        Transform transport = transportObject.transform;
        transport.position = finalRoot;

        // Transport를 첫 Group 아래에 두면 예외적으로 Entry가 중단되어도 Room Clear 시 같이 정리됩니다.
        Transform lifecycleParent = unit.pieces[0] != null ? unit.pieces[0].originalParent : null;
        if (lifecycleParent != null)
            transport.SetParent(lifecycleParent, true);

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

    private void RestorePiecesAndDestroyTransport(EntryUnitPlan unit)
    {
        if (unit == null)
            return;

        Transform transport = unit.transport;
        for (int i = 0; i < unit.pieces.Count; i++)
        {
            SubPiecePlan piece = unit.pieces[i];
            if (piece == null || piece.block == null)
                continue;

            Transform pieceTransform = piece.block.transform;
            pieceTransform.SetParent(piece.originalParent, true);
            pieceTransform.position = piece.finalPosition;
        }

        unit.transport = null;
        if (transport != null)
            Destroy(transport.gameObject);
    }

    private static List<int> CollectResolvedWaveOrder(List<EntryUnitPlan> units)
    {
        List<int> waves = new();
        for (int i = 0; i < units.Count; i++)
        {
            EntryUnitPlan unit = units[i];
            if (unit == null || !unit.railResolved || unit.transport == null || waves.Contains(unit.wave))
                continue;
            waves.Add(unit.wave);
        }
        waves.Sort();
        return waves;
    }

    private static EntryUnitPlan FindLargestResolvedUnitInWave(List<EntryUnitPlan> units, int wave)
    {
        EntryUnitPlan best = null;
        float bestArea = -1f;
        for (int i = 0; i < units.Count; i++)
        {
            EntryUnitPlan unit = units[i];
            if (unit == null || !unit.railResolved || unit.transport == null || unit.wave != wave)
                continue;

            float area = Mathf.Max(0.01f, unit.finalBounds.size.x * unit.finalBounds.size.y);
            if (area <= bestArea)
                continue;

            best = unit;
            bestArea = area;
        }
        return best;
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

    private static Vector2 Cardinalize(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return Vector2.right;

        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? (direction.x >= 0f ? Vector2.right : Vector2.left)
            : (direction.y >= 0f ? Vector2.up : Vector2.down);
    }

    private static bool CollectFloorBounds(Transform root, List<Bounds> output, out Bounds aggregate)
    {
        aggregate = default;
        if (root == null || output == null)
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

            Bounds bounds = renderer.bounds;
            output.Add(bounds);
            if (!found)
            {
                aggregate = bounds;
                found = true;
            }
            else
            {
                aggregate.Encapsulate(bounds);
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
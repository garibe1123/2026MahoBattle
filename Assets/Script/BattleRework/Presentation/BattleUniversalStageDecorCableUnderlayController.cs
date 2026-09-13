using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Universal Stage Decor의 Cable을 독립 Carrier가 아닌 순수 바닥 치장으로만 렌더링합니다.
///
/// 규칙:
/// - Camera / Light 2x2 Carrier에서만 생성됩니다.
/// - Floor보다 앞, 실제 Camera/Light보다 뒤 Sorting에 배치됩니다.
/// - Cable은 NavMesh / Collider / 입력 / Stage lifecycle을 전혀 소유하지 않습니다.
/// - 전선 Sprite는 Carrier 외곽에서 장비 중심 쪽으로 향하도록 랜덤 배치됩니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30240)]
public sealed class BattleUniversalStageDecorCableUnderlayController : MonoBehaviour
{
    private const string ClusterPrefix = "UniversalStageDecor_";
    private const string VisualRootName = "Visual";
    private const string CarrierTilePrefix = "DecorCarrierTile_";
    private const string DecorObjectPrefix = "DecorObject_";
    private const string CableRootName = "CableUnderlay";

    [Header("AUTO REFERENCES")]
    [SerializeField] private BattleUniversalStageDecorSceneConfig sceneConfig;

    [Header("CABLE UNDERLAY")]
    [Tooltip("Camera / Light Carrier 하나에 배치할 최소 전선 수입니다.")]
    [SerializeField, Range(0, 6)] private int minimumCableCount = 2;
    [Tooltip("Camera / Light Carrier 하나에 배치할 최대 전선 수입니다.")]
    [SerializeField, Range(0, 8)] private int maximumCableCount = 4;
    [Tooltip("전선 Sprite의 긴 변이 Floor 1칸에 대해 차지하는 기본 크기입니다.")]
    [SerializeField, Range(0.35f, 1.50f)] private float cableCellScale = 0.88f;
    [Tooltip("전선 배치 크기 랜덤 범위입니다.")]
    [SerializeField, Range(0f, 0.40f)] private float cableScaleRandomness = 0.16f;
    [Tooltip("전선이 장비 중심으로 너무 완벽하게 모이지 않도록 주는 위치 흔들림입니다. Floor 1칸 비율입니다.")]
    [SerializeField, Range(0f, 0.45f)] private float cablePositionJitter = 0.16f;
    [Tooltip("전선 진행 방향에 더하는 회전 흔들림 각도입니다.")]
    [SerializeField, Range(0f, 35f)] private float cableAngleJitter = 10f;
    [Tooltip("Floor Sorting Order보다 앞에 배치할 상대값입니다. 실제 장비보다 뒤로 자동 보정됩니다.")]
    [SerializeField, Range(1, 6)] private int cableSortingOffset = 1;

    [Header("RUNTIME REFRESH")]
    [SerializeField, Range(0.04f, 0.50f)] private float scanInterval = 0.10f;

    private readonly HashSet<int> processedClusters = new();
    private float nextScanAt;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        InvalidateAll();
    }

    private void OnDisable()
    {
        processedClusters.Clear();
    }

    private void OnValidate()
    {
        minimumCableCount = Mathf.Max(0, minimumCableCount);
        maximumCableCount = Mathf.Max(minimumCableCount, maximumCableCount);
        scanInterval = Mathf.Max(0.04f, scanInterval);

        if (Application.isPlaying)
            InvalidateAll();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextScanAt)
            return;

        nextScanAt = Time.unscaledTime + Mathf.Max(0.04f, scanInterval);
        ResolveReferences();
        ProcessNewClusters();
    }

    private void ResolveReferences()
    {
        if (sceneConfig == null)
            sceneConfig = GetComponent<BattleUniversalStageDecorSceneConfig>();
    }

    private void InvalidateAll()
    {
        processedClusters.Clear();
        nextScanAt = 0f;
    }

    private void ProcessNewClusters()
    {
        if (sceneConfig == null || sceneConfig.CableDecorationSprites == null || sceneConfig.CableDecorationSprites.Count == 0)
            return;

        BattleUniversalStageDecorController universal = BattleUniversalStageDecorController.Instance;
        if (universal == null)
            return;

        Transform runtimeRoot = universal.transform;
        for (int i = 0; i < runtimeRoot.childCount; i++)
        {
            Transform cluster = runtimeRoot.GetChild(i);
            if (cluster == null || !cluster.name.StartsWith(ClusterPrefix, StringComparison.Ordinal))
                continue;

            BattleUniversalStageDecorRuntimeMarker marker = cluster.GetComponent<BattleUniversalStageDecorRuntimeMarker>();
            if (marker == null ||
                (marker.Category != BattleUniversalStageDecorCategory.Camera &&
                 marker.Category != BattleUniversalStageDecorCategory.Light))
                continue;

            int id = cluster.GetInstanceID();
            if (!processedClusters.Add(id))
                continue;

            BuildCableUnderlay(cluster, marker);
        }
    }

    private void BuildCableUnderlay(Transform cluster, BattleUniversalStageDecorRuntimeMarker marker)
    {
        Transform visual = cluster != null ? cluster.Find(VisualRootName) : null;
        if (visual == null)
            return;

        List<SpriteRenderer> tiles = CollectCarrierTiles(visual);
        if (tiles.Count == 0 || !TryResolveCarrierGeometry(tiles, out CarrierGeometry geometry))
            return;

        Transform old = visual.Find(CableRootName);
        if (old != null)
        {
            old.name = CableRootName + "_Removing";
            old.gameObject.SetActive(false);
            Destroy(old.gameObject);
        }

        Transform decorRoot = FindDirectChildByPrefix(visual, DecorObjectPrefix);
        int floorSorting = ResolveHighestFloorSorting(tiles);
        int cableSorting = floorSorting + Mathf.Max(1, cableSortingOffset);
        if (decorRoot != null)
            EnsureDecorAboveSorting(decorRoot, cableSorting + 1);

        List<Sprite> usable = BuildUsableCableList(sceneConfig.CableDecorationSprites);
        if (usable.Count == 0)
            return;

        GameObject rootObject = new(CableRootName);
        rootObject.transform.SetParent(visual, false);
        Transform root = rootObject.transform;

        SpriteRenderer reference = tiles[0];
        int min = Mathf.Min(minimumCableCount, maximumCableCount);
        int max = Mathf.Max(minimumCableCount, maximumCableCount);

        int seed = unchecked(cluster.GetInstanceID() * 397 ^ (marker.SourceLabel != null ? marker.SourceLabel.GetHashCode() : 0));
        System.Random random = new(seed);
        int count = max <= min ? min : random.Next(min, max + 1);

        for (int i = 0; i < count; i++)
        {
            Sprite sprite = usable[random.Next(0, usable.Count)];
            if (sprite == null)
                continue;

            CreateCablePiece(root, sprite, reference, geometry, cableSorting, random, i, count);
        }
    }

    private void CreateCablePiece(
        Transform parent,
        Sprite sprite,
        SpriteRenderer floorReference,
        CarrierGeometry geometry,
        int sortingOrder,
        System.Random random,
        int index,
        int count)
    {
        Vector2 center = geometry.localBounds.center;
        float halfW = geometry.localBounds.extents.x * 0.92f;
        float halfH = geometry.localBounds.extents.y * 0.92f;

        int side = random.Next(0, 4);
        Vector2 edge;
        switch (side)
        {
            case 0:
                edge = new Vector2(center.x - halfW, RandomRange(random, center.y - halfH, center.y + halfH));
                break;
            case 1:
                edge = new Vector2(center.x + halfW, RandomRange(random, center.y - halfH, center.y + halfH));
                break;
            case 2:
                edge = new Vector2(RandomRange(random, center.x - halfW, center.x + halfW), center.y + halfH);
                break;
            default:
                edge = new Vector2(RandomRange(random, center.x - halfW, center.x + halfW), center.y - halfH);
                break;
        }

        Vector2 target = center + new Vector2(
            RandomRange(random, -geometry.cell * 0.20f, geometry.cell * 0.20f),
            RandomRange(random, -geometry.cell * 0.20f, geometry.cell * 0.20f));

        float ordinal = count <= 1 ? 0.58f : Mathf.Lerp(0.36f, 0.72f, index / (float)(count - 1));
        float t = Mathf.Clamp01(ordinal + RandomRange(random, -0.10f, 0.10f));
        Vector2 position = Vector2.Lerp(edge, target, t);
        float jitter = geometry.cell * Mathf.Max(0f, cablePositionJitter);
        position += new Vector2(
            RandomRange(random, -jitter, jitter),
            RandomRange(random, -jitter, jitter));

        Vector2 direction = target - edge;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg +
                      RandomRange(random, -cableAngleJitter, cableAngleJitter);

        GameObject go = new($"Cable_{index:00}_{SafeName(sprite.name)}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(position.x, position.y, -0.005f);
        go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerID = floorReference != null ? floorReference.sortingLayerID : 0;
        renderer.sharedMaterial = floorReference != null ? floorReference.sharedMaterial : null;
        renderer.sortingOrder = sortingOrder;

        Vector3 size = sprite.bounds.size;
        float longSide = Mathf.Max(0.001f, Mathf.Max(size.x, size.y));
        float randomScale = 1f + RandomRange(random, -cableScaleRandomness, cableScaleRandomness);
        float fit = geometry.cell * Mathf.Max(0.05f, cableCellScale) * randomScale / longSide;
        float mirror = random.NextDouble() < 0.5 ? -1f : 1f;
        go.transform.localScale = new Vector3(fit * mirror, fit, 1f);
    }

    private static void EnsureDecorAboveSorting(Transform decorRoot, int minimumSorting)
    {
        if (decorRoot == null)
            return;

        SpriteRenderer[] renderers = decorRoot.GetComponentsInChildren<SpriteRenderer>(true);
        int currentMinimum = int.MaxValue;
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer != null)
                currentMinimum = Mathf.Min(currentMinimum, renderer.sortingOrder);
        }

        if (currentMinimum == int.MaxValue || currentMinimum >= minimumSorting)
            return;

        int delta = minimumSorting - currentMinimum;
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer != null)
                renderer.sortingOrder += delta;
        }
    }

    private static int ResolveHighestFloorSorting(List<SpriteRenderer> tiles)
    {
        int sorting = int.MinValue;
        for (int i = 0; i < tiles.Count; i++)
        {
            SpriteRenderer renderer = tiles[i];
            if (renderer != null)
                sorting = Mathf.Max(sorting, renderer.sortingOrder);
        }
        return sorting == int.MinValue ? 0 : sorting;
    }

    private static List<Sprite> BuildUsableCableList(IReadOnlyList<Sprite> sprites)
    {
        List<Sprite> result = new();
        if (sprites == null)
            return result;

        for (int i = 0; i < sprites.Count; i++)
        {
            Sprite sprite = sprites[i];
            if (sprite != null)
                result.Add(sprite);
        }
        return result;
    }

    private static List<SpriteRenderer> CollectCarrierTiles(Transform visual)
    {
        List<SpriteRenderer> result = new();
        for (int i = 0; i < visual.childCount; i++)
        {
            Transform child = visual.GetChild(i);
            if (child == null || !child.name.StartsWith(CarrierTilePrefix, StringComparison.Ordinal))
                continue;
            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            if (renderer != null)
                result.Add(renderer);
        }
        return result;
    }

    private static bool TryResolveCarrierGeometry(List<SpriteRenderer> tiles, out CarrierGeometry geometry)
    {
        geometry = default;
        if (tiles == null || tiles.Count == 0)
            return false;

        bool initialized = false;
        Bounds bounds = default;
        float cell = 0f;

        for (int i = 0; i < tiles.Count; i++)
        {
            SpriteRenderer tile = tiles[i];
            if (tile == null || tile.sprite == null)
                continue;

            Vector3 spriteSize = tile.sprite.bounds.size;
            Vector3 scale = tile.transform.localScale;
            float width = Mathf.Abs(spriteSize.x * scale.x);
            float height = Mathf.Abs(spriteSize.y * scale.y);
            float candidateCell = Mathf.Max(0.01f, Mathf.Min(width, height));
            cell = cell <= 0f ? candidateCell : Mathf.Min(cell, candidateCell);

            Bounds tileBounds = new(tile.transform.localPosition, new Vector3(width, height, 0.01f));
            if (!initialized)
            {
                bounds = tileBounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(tileBounds);
            }
        }

        if (!initialized)
            return false;

        geometry = new CarrierGeometry
        {
            localBounds = bounds,
            cell = Mathf.Max(0.01f, cell)
        };
        return true;
    }

    private static Transform FindDirectChildByPrefix(Transform parent, string prefix)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name.StartsWith(prefix, StringComparison.Ordinal))
                return child;
        }
        return null;
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        if (max <= min)
            return min;
        return min + (float)random.NextDouble() * (max - min);
    }

    private static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Cable";
        return value.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
    }

    private struct CarrierGeometry
    {
        public Bounds localBounds;
        public float cell;
    }
}

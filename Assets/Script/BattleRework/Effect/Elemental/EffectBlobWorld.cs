using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EffectBlobWorld : MonoBehaviour
{
    public const float MinPixelSize = 0.5f, MaxPixelSize = 4f, DefaultPixelSize = 1.3f;
    public const float MinQuality = 10f, MaxQuality = 30f, DefaultQuality = 23f;
    public const float DefaultRoughness = 0.8f;
    [Range(0.00390625f, 1f)] public float worldUnitsPerPixel = 0.03125f;
    [Tooltip("Use the source sheet's Pixels Per Unit instead of World Units Per Pixel.")]
    public bool useSourcePixelsPerUnit = true;
    [Tooltip("1 preserves the source pixel size; larger values produce coarser pixels.")]
    [Range(MinPixelSize, MaxPixelSize)] public float pixelSizeMultiplier = DefaultPixelSize;
    [Tooltip("0 preserves smooth edges and colors; 1 sharpens edges and reduces color steps. Lifetime fading is preserved.")]
    [Range(0f, 1f)] public float surfaceRoughness = DefaultRoughness;
    [Range(MinQuality, MaxQuality)] public float refreshRate = DefaultQuality;
    [Range(1, 256)] public int maxVisibleTiles = 64;
    [Range(64, 8192)] public int maxActiveVisuals = 4096;

    private struct Entry
    {
        public EffectVisual visual;
        public EffectDefinitionSO definition;
        public int execution;
    }

    private struct BlobGroup
    {
        public EffectBlobSample sample;
        public EffectDefinitionSO definition;
        public float weight;
    }

    private readonly struct TileKey : IEquatable<TileKey>
    {
        private readonly int definition, x, y;
        public TileKey(EffectDefinitionSO data, int tileX, int tileY)
        {
            definition = data.GetInstanceID();
            x = tileX;
            y = tileY;
        }
        public bool Equals(TileKey other) => definition == other.definition && x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is TileKey other && Equals(other);
        public override int GetHashCode() => unchecked((definition * 397 ^ x) * 397 ^ y);
    }

    private static EffectBlobWorld instance;
    private readonly List<Entry> active = new();
    private readonly Dictionary<EffectVisual, int> indices = new();
    private readonly Dictionary<TileKey, EffectBlobRenderer> visibleTiles = new();
    private readonly List<EffectBlobRenderer> tiles = new();
    private readonly Dictionary<TileKey, int> bins = new();
    private readonly List<BlobGroup> groups = new();
    private Camera renderCamera;
    private bool preview;
    private float nextRefresh;

    internal struct DebugCounts
    {
        public int fire, liquid, gas, endingFire, endingLiquid, endingGas, tiles, samples;
    }

    internal static DebugCounts CaptureDebugCounts()
    {
        DebugCounts counts = default;
        if (instance == null || !instance.isActiveAndEnabled) return counts;
        for (int i = 0; i < instance.active.Count; i++)
        {
            Entry entry = instance.active[i];
            if (entry.visual == null || entry.definition == null || !entry.visual.IsPlaying ||
                entry.visual.ExecutionId != entry.execution) continue;
            bool ending = !entry.visual.IsAirborne && entry.visual.Phase == EffectPhase.End;
            if (entry.definition.fireParticles)
            {
                counts.fire++;
                if (ending) counts.endingFire++;
            }
            else if (entry.definition.family == EffectFamily.Liquid)
            {
                counts.liquid++;
                if (ending) counts.endingLiquid++;
            }
            else if (entry.definition.family == EffectFamily.Gas)
            {
                counts.gas++;
                if (ending) counts.endingGas++;
            }
        }
        foreach (EffectBlobRenderer tile in instance.tiles)
        {
            int samples = tile != null ? tile.VisibleSampleCount : 0;
            if (samples == 0) continue;
            counts.tiles++;
            counts.samples += samples;
        }
        return counts;
    }

    private void Awake()
    {
        renderCamera = Camera.main;
        if (!Application.isPlaying) return;
        if (instance == null) instance = this;
        else if (instance != this) enabled = false;
    }

    public static float SnapPixelSize(float value) => Mathf.Round(Mathf.Clamp(
        EffectNumbers.Positive(value, DefaultPixelSize), MinPixelSize, MaxPixelSize) * 10f) / 10f;

    private void OnValidate() { pixelSizeMultiplier = SnapPixelSize(pixelSizeMultiplier); }

    private static EffectBlobWorld Ensure()
    {
        if (instance == null)
        {
            GameObject host = new("Elemental Blob World");
            instance = host.AddComponent<EffectBlobWorld>();
        }
        return instance;
    }

    internal static bool CanSpawn => Ensure().active.Count < Mathf.Clamp(instance.maxActiveVisuals, 64, 8192);

    internal static bool Register(EffectVisual visual, EffectDefinitionSO data, int execution)
    {
        return Ensure().RegisterVisual(visual, data, execution);
    }

    internal bool RegisterVisual(EffectVisual visual, EffectDefinitionSO data, int execution)
    {
        Entry entry = new() { visual = visual, definition = data, execution = execution };
        if (indices.TryGetValue(visual, out int index)) { active[index] = entry; return true; }
        if (active.Count >= Mathf.Clamp(maxActiveVisuals, 64, 8192)) return false;
        indices.Add(visual, active.Count);
        active.Add(entry);
        return true;
    }

    internal static void Unregister(EffectVisual visual)
    {
        if (instance != null) instance.UnregisterVisual(visual);
    }

    internal void UnregisterVisual(EffectVisual visual)
    {
        if (indices.TryGetValue(visual, out int index)) RemoveAt(index);
    }

    private void RemoveAt(int index)
    {
        indices.Remove(active[index].visual);
        int last = active.Count - 1;
        if (index != last)
        {
            active[index] = active[last];
            indices[active[index].visual] = index;
        }
        active.RemoveAt(last);
    }

    private bool TryGetView(out Rect view)
    {
        view = default;
        if (renderCamera == null || (!preview && !renderCamera.isActiveAndEnabled)) return false;
        Plane plane = new(Vector3.forward, new Vector3(0f, 0f, transform.position.z));
        Vector2 low = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 high = new(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            Ray ray = renderCamera.ViewportPointToRay(new Vector3(i & 1, i >> 1, 0f));
            if (!plane.Raycast(ray, out float distance)) return false;
            Vector2 point = ray.GetPoint(distance);
            low = Vector2.Min(low, point);
            high = Vector2.Max(high, point);
        }
        view = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        return view.width > 0f && view.height > 0f;
    }

    private void LateUpdate()
    {
        if (!preview) Tick(Time.time);
    }

    public void InitializePreview(Camera camera)
    {
        preview = true;
        renderCamera = camera;
    }

    public void DisposePreview()
    {
        ClearTiles(0);
        foreach (EffectBlobRenderer tile in tiles)
            if (tile != null) tile.ReleaseResources();
        active.Clear();
        indices.Clear();
        visibleTiles.Clear();
        bins.Clear();
        groups.Clear();
    }

    public void Tick(float time)
    {
        bool refresh = time >= nextRefresh;
        if (refresh) visibleTiles.Clear();
        bins.Clear();
        groups.Clear();
        if (!TryGetView(out Rect view)) { nextRefresh = 0f; ClearTiles(0); return; }
        int tileLimit = Mathf.Clamp(maxVisibleTiles, 1, 256);
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Entry entry = active[i];
            if (entry.visual == null || entry.definition == null || !entry.visual.IsPlaying ||
                entry.visual.ExecutionId != entry.execution) { RemoveAt(i); continue; }
            if (entry.definition.fireParticles)
            {
                if (!refresh) continue;
                for (int particle = 0; particle < entry.visual.FireSampleCount; particle++)
                    if (entry.visual.CaptureFire(particle, out EffectBlobSample fireSample))
                        AddToTiles(new BlobGroup { sample = fireSample, definition = entry.definition },
                            view, tileLimit);
                continue;
            }
            if (!refresh)
            {
                entry.visual.HideBlobSources(entry.definition);
                continue;
            }
            for (int piece = 1; piece < entry.visual.BlobSampleCount; piece++)
                if (entry.visual.CaptureBlob(entry.definition, piece, out EffectBlobSample fragment))
                    AddToTiles(new BlobGroup { sample = fragment, definition = entry.definition }, view, tileLimit);
            if (!entry.visual.CaptureBlob(entry.definition, out EffectBlobSample sample)) continue;
            float step = PixelStep(entry.definition);
            Vector2 center = sample.center;
            Vector2 extent = sample.radius * 2f + Vector2.one * step;
            Vector2 low = Vector2.Max(center - extent, view.min);
            Vector2 high = Vector2.Min(center + extent, view.max);
            if (low.x >= high.x || low.y >= high.y) continue;
            float cellSize = step * 8f;
            TileKey bin = new(entry.definition, Mathf.FloorToInt(center.x / cellSize), Mathf.FloorToInt(center.y / cellSize));
            if (bins.TryGetValue(bin, out int index))
            {
                BlobGroup group = groups[index];
                float total = group.weight + sample.weight;
                float amount = total > 0f ? sample.weight / total : 0f;
                group.sample.center = Vector3.Lerp(group.sample.center, sample.center, amount);
                group.sample.radius = Vector2.Max(group.sample.radius, sample.radius);
                group.sample.dissolve = Mathf.Lerp(group.sample.dissolve, sample.dissolve, amount);
                group.sample.ripple = Mathf.Lerp(group.sample.ripple, sample.ripple, amount);
                group.sample.impact = Mathf.Max(group.sample.impact, sample.impact);
                group.sample.weight = Mathf.Min(total, 8f);
                group.weight = total;
                groups[index] = group;
            }
            else
            {
                bins.Add(bin, groups.Count);
                groups.Add(new BlobGroup { sample = sample, definition = entry.definition, weight = sample.weight });
            }
        }
        if (!refresh) return;
        nextRefresh = time + 1f / Mathf.Clamp(EffectNumbers.Positive(refreshRate, DefaultQuality), MinQuality, MaxQuality);
        for (int i = 0; i < groups.Count; i++) AddToTiles(groups[i], view, tileLimit);
        for (int i = 0; i < visibleTiles.Count; i++) tiles[i].End(time);
        ClearTiles(visibleTiles.Count);
    }

    private float PixelStep(EffectDefinitionSO data)
    {
        float step = Mathf.Clamp(EffectNumbers.Positive(worldUnitsPerPixel, 0.03125f), 0.00390625f, 1f);
        if (useSourcePixelsPerUnit && data.sourceSheet != null)
            step = 1f / EffectNumbers.Positive(data.sourceSheet.pixelsPerUnit, 32f);
        return step * SnapPixelSize(pixelSizeMultiplier);
    }

    internal float DetailPixelSize(EffectDefinitionSO data) => PixelStep(data);

    internal static float DefaultDetailPixelSize(EffectDefinitionSO data)
    {
        if (instance != null) return instance.PixelStep(data);
        float step = data.sourceSheet != null ? 1f / EffectNumbers.Positive(data.sourceSheet.pixelsPerUnit, 32f) : 0.03125f;
        return step * DefaultPixelSize;
    }

    private void AddToTiles(BlobGroup group, Rect view, int tileLimit)
    {
        float step = PixelStep(group.definition);
        float tileSize = step * EffectBlobRenderer.Resolution;
        EffectBlobSample sample = group.sample;
        Vector2 center = sample.center;
        Vector2 extent = sample.radius * 2f + Vector2.one * step;
        Vector2 low = Vector2.Max(center - extent, view.min);
        Vector2 high = Vector2.Min(center + extent, view.max);
        if (low.x >= high.x || low.y >= high.y) return;
        int left = Mathf.FloorToInt(low.x / tileSize);
        int right = Mathf.CeilToInt(high.x / tileSize) - 1;
        int bottom = Mathf.FloorToInt(low.y / tileSize);
        int top = Mathf.CeilToInt(high.y / tileSize) - 1;
        for (int y = bottom; y <= top; y++)
            for (int x = left; x <= right; x++)
            {
                TileKey key = new(group.definition, x, y);
                if (!visibleTiles.TryGetValue(key, out EffectBlobRenderer tile))
                {
                    int slot = visibleTiles.Count;
                    if (slot >= tileLimit) continue;
                    if (slot == tiles.Count)
                    {
                        GameObject host = new("Elemental Surface Tile") { hideFlags = gameObject.hideFlags };
                        host.transform.SetParent(transform, false);
                        EffectBlobRenderer created = host.AddComponent<EffectBlobRenderer>();
                        created.InitializeResources();
                        tiles.Add(created);
                    }
                    tile = tiles[slot];
                    Rect bounds = new(x * tileSize, y * tileSize, tileSize, tileSize);
                    Rect clipped = Rect.MinMaxRect(Mathf.Max(bounds.xMin, view.xMin), Mathf.Max(bounds.yMin, view.yMin),
                        Mathf.Min(bounds.xMax, view.xMax), Mathf.Min(bounds.yMax, view.yMax));
                    tile.BeginTile(group.definition, bounds, clipped, step, refreshRate, surfaceRoughness);
                    visibleTiles.Add(key, tile);
                }
                tile.AddSample(sample);
            }
    }

    private void ClearTiles(int start)
    {
        for (int i = start; i < tiles.Count; i++)
            if (tiles[i] != null) tiles[i].Clear();
    }

    private void OnDisable() { nextRefresh = 0f; ClearTiles(0); }
    private void OnDestroy() { if (instance == this) instance = null; }
}

using System;
using UnityEngine;

internal struct EffectBlobSample
{
    public Vector3 center;
    public Vector2 radius;
    public float weight;
    public float dissolve;
    public SpriteRenderer source;
}

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class EffectBlobRenderer : MonoBehaviour
{
    private const int Resolution = 128;
    private const int MaxSamples = 256;
    private readonly EffectBlobSample[] samples = new EffectBlobSample[MaxSamples];
    private readonly float[] density = new float[Resolution * Resolution];
    private readonly Color32[] pixels = new Color32[Resolution * Resolution];
    private readonly MaterialPropertyBlock properties = new();
    private SpriteRenderer surface;
    private Texture2D texture;
    private Sprite sprite;
    private Camera renderCamera;
    private EffectDefinitionSO definition;
    private Rect view;
    private Vector2 minimum, maximum;
    private int count;
    private float nextRefresh;
    private bool ready;

    private void Awake()
    {
        surface = GetComponent<SpriteRenderer>();
        surface.enabled = false;
        renderCamera = Camera.main;
        texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
        {
            name = "Elemental Blob Surface",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        sprite = Sprite.Create(texture, new Rect(0, 0, Resolution, Resolution),
            new Vector2(0.5f, 0.5f), Resolution, 0, SpriteMeshType.FullRect);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        surface.sprite = sprite;
    }

    public void Begin(EffectDefinitionSO data)
    {
        if (definition != data) nextRefresh = 0f;
        definition = data;
        count = 0;
        ready = data != null && data.mergeNearby && renderCamera != null && renderCamera.isActiveAndEnabled;
        if (!ready) { Clear(); return; }
        Plane plane = new(Vector3.forward, new Vector3(0f, 0f, transform.position.z));
        minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            Ray ray = renderCamera.ViewportPointToRay(new Vector3(i & 1, i >> 1, 0f));
            if (!plane.Raycast(ray, out float distance)) { ready = false; Clear(); return; }
            Vector2 point = ray.GetPoint(distance);
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }
        view = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
    }

    public void Add(EffectVisual visual)
    {
        if (!ready || count >= MaxSamples || !visual.CaptureBlob(definition, out EffectBlobSample sample)) return;
        Vector2 center = sample.center;
        Vector2 extent = sample.radius * 2f;
        Vector2 low = Vector2.Max(center - extent, view.min);
        Vector2 high = Vector2.Min(center + extent, view.max);
        if (low.x >= high.x || low.y >= high.y) return;
        minimum = Vector2.Min(minimum, low);
        maximum = Vector2.Max(maximum, high);
        samples[count++] = sample;
    }

    public void End()
    {
        if (!ready || count == 0) { Clear(); return; }
        if (Time.time < nextRefresh && surface.enabled) return;
        nextRefresh = Time.time + 1f / Mathf.Clamp(definition.mergeRefreshRate, 5f, 30f);
        SpriteRenderer source = samples[0].source;
        float ppu = source.sprite.pixelsPerUnit;
        float length = Mathf.Max(maximum.x - minimum.x, maximum.y - minimum.y);
        float step = Mathf.Ceil(Mathf.Max(1f, (length * ppu + 2f) / Resolution)) / ppu;
        float size = step * Resolution;
        Vector2 origin = (minimum + maximum - Vector2.one * size) * 0.5f;
        origin.x = Mathf.Floor(origin.x / step) * step;
        origin.y = Mathf.Floor(origin.y / step) * step;
        RectInt clip = new RectInt(
            Mathf.Clamp(Mathf.FloorToInt((view.xMin - origin.x) / step), 0, Resolution),
            Mathf.Clamp(Mathf.FloorToInt((view.yMin - origin.y) / step), 0, Resolution), 0, 0);
        clip.xMax = Mathf.Clamp(Mathf.CeilToInt((view.xMax - origin.x) / step), clip.xMin, Resolution);
        clip.yMax = Mathf.Clamp(Mathf.CeilToInt((view.yMax - origin.y) / step), clip.yMin, Resolution);
        Array.Clear(density, 0, density.Length);
        Array.Clear(pixels, 0, pixels.Length);
        float opacity = 0f;
        Color tint = Color.black;
        for (int i = 0; i < count; i++)
        {
            EffectBlobField.Accumulate(density, Resolution, origin, step, clip, samples[i]);
            Color color = samples[i].source.color;
            opacity = Mathf.Max(opacity, color.a);
            tint.r = Mathf.Max(tint.r, color.r);
            tint.g = Mathf.Max(tint.g, color.g);
            tint.b = Mathf.Max(tint.b, color.b);
        }
        EffectBlobField.Colorize(density, pixels, Resolution, origin, step, clip,
            Mathf.Clamp(definition.mergeSoftness, 0.02f, 0.5f), definition.family == EffectFamily.Gas, Time.time);
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        if (Mathf.Abs(parentScale.x) < 0.0001f || Mathf.Abs(parentScale.y) < 0.0001f) { Clear(); return; }
        transform.SetPositionAndRotation(new Vector3(origin.x + size * 0.5f, origin.y + size * 0.5f,
            samples[0].center.z), Quaternion.identity);
        transform.localScale = new Vector3(size / parentScale.x, size / parentScale.y, 1f);
        surface.sharedMaterial = source.sharedMaterial;
        source.GetPropertyBlock(properties);
        surface.SetPropertyBlock(properties);
        tint.a = opacity;
        surface.color = tint;
        surface.sortingLayerID = definition.sortingLayerId;
        surface.sortingOrder = definition.sortingOrder;
        surface.enabled = true;
    }

    public void Clear()
    {
        if (surface != null) surface.enabled = false;
        nextRefresh = 0f;
        Array.Clear(samples, 0, samples.Length);
        count = 0;
    }

    private void OnDisable() { Clear(); }

    private void OnDestroy()
    {
        if (sprite != null) Destroy(sprite);
        if (texture != null) Destroy(texture);
    }
}

internal static class EffectBlobField
{
    public static void Accumulate(float[] field, int resolution, Vector2 origin, float step, RectInt clip, EffectBlobSample sample)
    {
        if (clip.width == 0 || clip.height == 0) return;
        Vector2 center = ((Vector2)sample.center - origin) / step;
        Vector2 radius = sample.radius / step;
        int left = Mathf.Clamp(Mathf.FloorToInt(center.x - radius.x * 2f), clip.xMin, clip.xMax - 1);
        int right = Mathf.Clamp(Mathf.CeilToInt(center.x + radius.x * 2f), clip.xMin, clip.xMax - 1);
        int bottom = Mathf.Clamp(Mathf.FloorToInt(center.y - radius.y * 2f), clip.yMin, clip.yMax - 1);
        int top = Mathf.Clamp(Mathf.CeilToInt(center.y + radius.y * 2f), clip.yMin, clip.yMax - 1);
        for (int y = bottom; y <= top; y++)
        {
            float dy = (y + 0.5f - center.y) / radius.y;
            for (int x = left; x <= right; x++)
            {
                float dx = (x + 0.5f - center.x) / radius.x;
                float distance = dx * dx + dy * dy;
                if (distance >= 4f) continue;
                float contribution = sample.weight / (1f + 3f * distance * distance);
                if (sample.dissolve > 0f)
                {
                    contribution *= Mathf.SmoothStep(0f, 1f,
                        (distance - sample.dissolve * sample.dissolve * 1.4f) / 0.2f);
                    float fragments = Mathf.Clamp01((Mathf.Sin(dx * 8f + dy * 7f) + 0.5f) * 2f);
                    contribution *= Mathf.Lerp(1f, fragments, sample.dissolve);
                }
                field[y * resolution + x] += contribution;
            }
        }
    }

    public static void Colorize(float[] field, Color32[] pixels, int resolution, Vector2 origin,
        float step, RectInt clip, float softness, bool smoke, float time)
    {
        for (int y = clip.yMin; y < clip.yMax; y++)
            for (int x = clip.xMin; x < clip.xMax; x++)
            {
                int index = y * resolution + x;
                float value = field[index];
                if (value <= 0.3f - softness * 0.5f) { pixels[index] = default; continue; }
                float noise = Mathf.PerlinNoise((origin.x + x * step) * 1.7f + time * 0.13f,
                    (origin.y + y * step) * 1.7f - time * 0.19f);
                float coverage = Mathf.SmoothStep(0f, 1f, (value - 0.3f) / softness + 0.5f);
                float shade = smoke ? Mathf.Lerp(0.65f, 1f, noise) :
                    value < 0.45f || Mathf.Repeat(value * 0.6f + noise * 0.25f + time * 0.12f, 1f) > 0.9f ? 1f : 0.7f;
                if (smoke) coverage *= Mathf.Lerp(0.6f, 1f, noise);
                byte color = (byte)Mathf.RoundToInt(shade * 255f);
                pixels[index] = new Color32(color, color, color, (byte)Mathf.RoundToInt(coverage * 255f));
            }
    }
}

using System;
using UnityEngine;

internal struct EffectBlobSample
{
    public Vector3 center;
    public Vector2 radius;
    public float weight;
    public float dissolve;
    public float impact;
    public float ripple;
    public Color fireColor;
    public SpriteRenderer source;
}

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class EffectBlobRenderer : MonoBehaviour
{
    internal const int Resolution = 128;
    private const int MaxSamples = 1024;
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int AlphaTexId = Shader.PropertyToID("_AlphaTex");
    private static readonly int ExternalAlphaId = Shader.PropertyToID("_EnableExternalAlpha");
    private static readonly int RendererColorId = Shader.PropertyToID("_RendererColor");
    private readonly EffectBlobSample[] samples = new EffectBlobSample[MaxSamples];
    // The extra border keeps surface normals continuous between tiles.
    private readonly float[] density = new float[(Resolution + 2) * (Resolution + 2)];
    private readonly float[] foam = new float[(Resolution + 2) * (Resolution + 2)];
    private readonly Color32[] pixels = new Color32[Resolution * Resolution];
    private Color[] fireColors;
    private readonly float[] noiseGrid = new float[(Resolution / 8 + 3) * (Resolution / 8 + 3)];
    private MaterialPropertyBlock properties;
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
    private bool fixedGrid;
    private Rect tileBounds;
    private float pixelStep;
    private float tileRefreshRate;

    private void Awake()
    {
        properties = new MaterialPropertyBlock();
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
        fixedGrid = false;
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
        AddSample(sample);
    }

    internal void BeginTile(EffectDefinitionSO data, Rect tile, Rect visible, float step, float refreshRate)
    {
        if (!fixedGrid || definition != data || tileBounds != tile || pixelStep != step) nextRefresh = 0f;
        fixedGrid = ready = true;
        definition = data;
        tileBounds = tile;
        pixelStep = step;
        tileRefreshRate = EffectNumbers.Positive(refreshRate, 20f);
        view = visible;
        count = 0;
        minimum = tile.min;
        maximum = tile.max;
    }

    internal void AddSample(EffectBlobSample sample)
    {
        if (!ready || count >= MaxSamples) return;
        Vector2 center = sample.center;
        Vector2 extent = sample.radius * 2f;
        Vector2 border = fixedGrid ? Vector2.one * pixelStep : Vector2.zero;
        Vector2 low = Vector2.Max(center - extent, view.min - border);
        Vector2 high = Vector2.Min(center + extent, view.max + border);
        if (low.x >= high.x || low.y >= high.y) return;
        minimum = Vector2.Min(minimum, low);
        maximum = Vector2.Max(maximum, high);
        samples[count++] = sample;
    }

    public void End()
    {
        if (!ready || count == 0) { Clear(); return; }
        if (Time.time < nextRefresh && surface.enabled) return;
        float rate = EffectNumbers.Positive(definition.mergeRefreshRate, 20f);
        if (fixedGrid) rate = Mathf.Min(rate, tileRefreshRate);
        nextRefresh = Time.time + 1f / Mathf.Clamp(rate, 5f, 30f);
        SpriteRenderer source = samples[0].source;
        float ppu = source.sprite.pixelsPerUnit;
        float length = Mathf.Max(maximum.x - minimum.x, maximum.y - minimum.y);
        float step = Mathf.Ceil(Mathf.Max(1f, (length * ppu + 2f) / Resolution)) / ppu;
        if (fixedGrid) step = pixelStep;
        float size = step * Resolution;
        Vector2 origin = (minimum + maximum - Vector2.one * size) * 0.5f;
        origin.x = Mathf.Floor(origin.x / step) * step;
        origin.y = Mathf.Floor(origin.y / step) * step;
        if (fixedGrid) origin = tileBounds.min;
        RectInt clip = new RectInt(
            Mathf.Clamp(Mathf.FloorToInt((view.xMin - origin.x) / step), 0, Resolution),
            Mathf.Clamp(Mathf.FloorToInt((view.yMin - origin.y) / step), 0, Resolution), 0, 0);
        clip.xMax = Mathf.Clamp(Mathf.CeilToInt((view.xMax - origin.x) / step), clip.xMin, Resolution);
        clip.yMax = Mathf.Clamp(Mathf.CeilToInt((view.yMax - origin.y) / step), clip.yMin, Resolution);
        Array.Clear(density, 0, density.Length);
        Array.Clear(foam, 0, foam.Length);
        Array.Clear(pixels, 0, pixels.Length);
        float opacity = 0f;
        Color tint = Color.black;
        if (definition.fireParticles)
        {
            fireColors ??= new Color[density.Length];
            Array.Clear(fireColors, 0, fireColors.Length);
        }
        for (int i = 0; i < count; i++)
        {
            RectInt fieldClip = new(clip.xMin, clip.yMin, clip.width + 2, clip.height + 2);
            if (definition.fireParticles)
                EffectFireField.Accumulate(density, fireColors, Resolution + 2,
                    origin - Vector2.one * step, step, fieldClip, samples[i]);
            else
                EffectBlobField.Accumulate(density, foam, Resolution + 2, origin - Vector2.one * step, step,
                    fieldClip, samples[i]);
            Color color = samples[i].source.color;
            opacity = Mathf.Max(opacity, color.a);
            tint.r = Mathf.Max(tint.r, color.r);
            tint.g = Mathf.Max(tint.g, color.g);
            tint.b = Mathf.Max(tint.b, color.b);
        }
        if (definition.fireParticles)
        {
            EffectFireField.Colorize(density, fireColors, pixels, Resolution, clip, definition);
            tint = Color.white;
        }
        else
            EffectBlobField.Colorize(density, foam, pixels, noiseGrid, Resolution, origin, step, clip,
                definition, Time.time, opacity);
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        if (Mathf.Abs(parentScale.x) < 0.0001f || Mathf.Abs(parentScale.y) < 0.0001f) { Clear(); return; }
        transform.SetPositionAndRotation(new Vector3(origin.x + size * 0.5f, origin.y + size * 0.5f,
            samples[0].center.z), Quaternion.identity);
        transform.localScale = new Vector3(size / parentScale.x, size / parentScale.y, 1f);
        surface.sharedMaterial = source.sharedMaterial;
        properties ??= new MaterialPropertyBlock();
        source.GetPropertyBlock(properties);
        // SpriteRenderer's source texture bindings must not replace the generated surface.
        Material material = surface.sharedMaterial;
        if (material != null)
        {
            if (material.HasProperty(MainTexId)) properties.SetTexture(MainTexId, texture);
            if (material.HasProperty(BaseMapId)) properties.SetTexture(BaseMapId, texture);
            if (material.HasProperty(AlphaTexId)) properties.SetTexture(AlphaTexId, Texture2D.whiteTexture);
            if (material.HasProperty(ExternalAlphaId)) properties.SetFloat(ExternalAlphaId, 0f);
            if (material.HasProperty(RendererColorId))
                properties.SetColor(RendererColorId, new Color(tint.r, tint.g, tint.b, 1f));
        }
        surface.SetPropertyBlock(properties);
        tint.a = 1f;
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
    public static void Accumulate(float[] field, float[] foam, int resolution, Vector2 origin, float step, RectInt clip, EffectBlobSample sample)
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
                    float first = (dx + 0.65f) * (dx + 0.65f) + (dy - 0.35f) * (dy - 0.35f);
                    float second = (dx - 0.6f) * (dx - 0.6f) + (dy + 0.45f) * (dy + 0.45f);
                    float fragments = Mathf.Clamp01((0.7f - Mathf.Min(first, second)) / 0.35f);
                    contribution *= Mathf.Lerp(1f, fragments, sample.dissolve);
                }
                field[y * resolution + x] += contribution;
                if (sample.impact > 0f)
                {
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Sqrt(distance) -
                        Mathf.Lerp(0.25f, 1.65f, sample.ripple)) / 0.2f);
                    float froth = Mathf.Clamp01(1f - distance * 2f) * (1f - sample.ripple);
                    int index = y * resolution + x;
                    foam[index] = Mathf.Max(foam[index], (ring + froth) * sample.impact);
                }
            }
        }
    }

    public static void Colorize(float[] field, float[] foam, Color32[] pixels, float[] noiseGrid,
        int resolution, Vector2 origin, float step, RectInt clip, EffectDefinitionSO definition, float time, float opacity)
    {
        if (clip.width == 0 || clip.height == 0) return;
        bool smoke = definition.family == EffectFamily.Gas;
        float softness = Mathf.Clamp(definition.mergeSoftness, 0.02f, 0.5f);
        float noiseScale = EffectNumbers.Positive(definition.surfaceNoiseScale, 0.65f);
        float noiseStrength = Mathf.Clamp01(EffectNumbers.NonNegative(definition.surfaceNoiseStrength));
        float noiseSpeed = EffectNumbers.NonNegative(definition.surfaceNoiseSpeed);
        float relief = EffectNumbers.NonNegative(definition.surfaceRelief);
        float specularStrength = EffectNumbers.NonNegative(definition.surfaceSpecular);
        float shininess = Mathf.Clamp(definition.surfaceShininess, 4f, 128f);
        Color bodyColor = definition.surfaceBodyColor;
        Color highlightColor = definition.surfaceHighlightColor;
        float highlightStrength = Mathf.Clamp01(definition.surfaceHighlightStrength);
        Vector3 light = definition.surfaceLightDirection.sqrMagnitude > 0.0001f
            ? definition.surfaceLightDirection.normalized : Vector3.forward;
        Vector3 halfLight = (light + Vector3.forward).normalized;
        int stride = resolution / 8 + 3;
        int fieldStride = resolution + 2;
        for (int y = clip.yMin / 8; y <= (clip.yMax - 1) / 8 + 3; y++)
            for (int x = clip.xMin / 8; x <= (clip.xMax - 1) / 8 + 3; x++)
            {
                float worldX = origin.x + (x - 1) * 8f * step;
                float worldY = origin.y + (y - 1) * 8f * step;
                float drift = time * noiseSpeed;
                noiseGrid[y * stride + x] = smoke || specularStrength > 0f
                    ? Mathf.PerlinNoise(worldX * noiseScale + drift, worldY * noiseScale - drift * 0.7f) * 0.75f +
                        Mathf.PerlinNoise(worldX * noiseScale * 2.13f + 17.3f - drift * 0.4f,
                            worldY * noiseScale * 2.13f + 43.1f + drift * 0.6f) * 0.25f
                    : Mathf.PerlinNoise(worldX * 1.7f + time * 0.13f, worldY * 1.7f - time * 0.19f);
            }
        for (int y = clip.yMin; y < clip.yMax; y++)
            for (int x = clip.xMin; x < clip.xMax; x++)
            {
                int index = y * resolution + x;
                int fieldIndex = (y + 1) * fieldStride + x + 1;
                float value = field[fieldIndex];
                float cutoff = (0.3f - softness * 0.5f) / (smoke ? 1f + noiseStrength * 0.5f : 1f);
                if (value <= cutoff && foam[fieldIndex] <= 0.01f) { pixels[index] = default; continue; }
                int cell = (y / 8 + 1) * stride + x / 8 + 1;
                float horizontal = Mathf.SmoothStep(0f, 1f, x % 8 / 8f);
                float vertical = Mathf.SmoothStep(0f, 1f, y % 8 / 8f);
                float noise = Mathf.Lerp(Mathf.Lerp(noiseGrid[cell], noiseGrid[cell + 1], horizontal),
                    Mathf.Lerp(noiseGrid[cell + stride], noiseGrid[cell + stride + 1], horizontal), vertical);
                if (smoke) value *= 1f + (noise - 0.5f) * noiseStrength;
                float coverage = Mathf.SmoothStep(0f, 1f, (value - 0.3f) / softness + 0.5f);
                float shade = smoke ? Mathf.Lerp(1f - noiseStrength, 1f,
                    Mathf.Min(2f, Mathf.Floor((noise * 0.7f + Mathf.Clamp01(value) * 0.3f) * 3f)) * 0.5f) :
                    value < 0.45f || Mathf.Repeat(value * 0.6f + noise * 0.25f + time * 0.12f, 1f) > 0.9f ? 1f : 0.7f;
                if (smoke) coverage *= Mathf.Lerp(1f - noiseStrength, 1f, noise);
                Color color = bodyColor * shade;
                Vector3 normal = Vector3.forward;
                if (relief > 0f)
                {
                    float slopeX = (Height(field[fieldIndex - 1]) - Height(field[fieldIndex + 1])) / (2f * step);
                    float slopeY = (Height(field[fieldIndex - fieldStride]) - Height(field[fieldIndex + fieldStride])) / (2f * step);
                    float noiseX = Mathf.Lerp(
                        Mathf.Lerp(noiseGrid[cell + 1] - noiseGrid[cell - 1], noiseGrid[cell + 2] - noiseGrid[cell], horizontal),
                        Mathf.Lerp(noiseGrid[cell + stride + 1] - noiseGrid[cell + stride - 1],
                            noiseGrid[cell + stride + 2] - noiseGrid[cell + stride], horizontal), vertical) / (16f * step);
                    float noiseY = Mathf.Lerp(
                        Mathf.Lerp(noiseGrid[cell + stride] - noiseGrid[cell - stride],
                            noiseGrid[cell + stride + 1] - noiseGrid[cell - stride + 1], horizontal),
                        Mathf.Lerp(noiseGrid[cell + stride * 2] - noiseGrid[cell],
                            noiseGrid[cell + stride * 2 + 1] - noiseGrid[cell + 1], horizontal), vertical) / (16f * step);
                    normal = new Vector3((slopeX - noiseX * noiseStrength) * relief,
                        (slopeY - noiseY * noiseStrength) * relief, 1f).normalized;
                    if (smoke)
                    {
                        float lighting = Mathf.Clamp01(0.18f + Mathf.Max(0f, Vector3.Dot(normal, light)) * 0.82f);
                        color = Color.Lerp(definition.surfaceShadowColor, bodyColor, lighting) *
                            Mathf.Lerp(0.75f, 1f, Height(value));
                    }
                }
                float reflection = 0f;
                float foamCoverage = 0f;
                if (!smoke && highlightStrength > 0f)
                {
                    float shore = 1f - Mathf.SmoothStep(0.32f, 0.55f, value);
                    reflection = Mathf.Clamp01(Mathf.Pow(Mathf.Max(0f, Vector3.Dot(normal, halfLight)), shininess) * specularStrength);
                    foamCoverage = Mathf.Clamp01(Mathf.Max(foam[fieldIndex] * Mathf.Lerp(0.55f, 1f, noise),
                        shore * Mathf.SmoothStep(0.3f, 0.65f, noise) * 0.3f) * highlightStrength);
                    color = Color.Lerp(bodyColor * Mathf.Lerp(0.65f, 1f, noise), highlightColor, reflection);
                    color = Color.Lerp(color, highlightColor, foamCoverage);
                }
                color.a = Mathf.Clamp01(Mathf.Max(coverage * Mathf.Lerp(opacity, Mathf.Max(opacity, 0.65f), reflection),
                    foamCoverage * Mathf.Clamp01(definition.foamOpacity)));
                pixels[index] = color;
            }
    }

    private static float Height(float density) => density / (0.65f + density);
}

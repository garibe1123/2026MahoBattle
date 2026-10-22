using System;
using UnityEngine;

public sealed class FieldBeamEffectVisual : IDisposable
{
    private const int MaxSegments = 32;
    private readonly Vector2[] points = new Vector2[MaxSegments + 1];
    private readonly Transform parent;
    private readonly SpriteRenderer surface;
    private float seed;
    private bool resetSeed = true;
    private Texture2D texture;
    private Sprite sprite;
    private Color32[] pixels;
    private float nextRefresh;
    private bool wasCharging;
    private int textureWidth, textureHeight;
    public Vector3 Center { get; private set; }
    public float Intensity { get; private set; }

    public FieldBeamEffectVisual(Transform parent, Material material)
    {
        this.parent = parent;
        GameObject host = new("Field Beam Surface") { hideFlags = parent.gameObject.hideFlags };
        host.transform.SetParent(parent, false);
        surface = host.AddComponent<SpriteRenderer>();
        surface.sharedMaterial = material;
        surface.enabled = false;
    }

    public void Render(EffectBeamSettings data, EffectRenderSort sorting, float age, float size,
        float worldPixelStep, Camera camera, float envelope, Material material)
    {
        if (surface.sharedMaterial != material) surface.sharedMaterial = material;
        sorting.Apply(surface);
        if (resetSeed)
        {
            seed = UnityEngine.Random.Range(0f, 1000f);
            resetSeed = false;
        }
        bool laser = data.kind == EffectBeamKind.Laser;
        float flowTime = age * EffectNumbers.NonNegative(data.flowSpeed);
        float pulse = 1f + (Mathf.PerlinNoise(seed, age * data.pulseFrequency) * 2f - 1f) *
            Mathf.Clamp01(data.pulseStrength);
        float chargeTime = laser ? EffectNumbers.NonNegative(data.chargeDuration) : 0f;
        float charge = chargeTime > 0f ? Mathf.Clamp01(age / chargeTime) : 1f;
        bool charging = age < chargeTime;
        bool chargingChanged = charging != wasCharging;
        wasCharging = charging;
        float extension = laser && data.extensionDuration > 0f ?
            Mathf.Clamp01((age - chargeTime) / data.extensionDuration) : charging ? 0f : 1f;
        Intensity = envelope * pulse * (charging ? charge * 0.5f : 1f);
        if (size <= 0f || envelope <= 0f) { surface.enabled = false; return; }
        if (age < nextRefresh && surface.enabled && !chargingChanged && envelope >= 1f && extension >= 1f) return;
        nextRefresh = age + 1f / EffectNumbers.Positive(data.refreshRate, 20f);
        Vector3 parentScale = parent.lossyScale;
        float scale = size * Mathf.Max(Mathf.Abs(parentScale.x), Mathf.Abs(parentScale.y));
        Vector2 direction = data.direction.sqrMagnitude > 0.0001f ? data.direction.normalized : Vector2.right;
        direction = parent.TransformDirection(direction);
        if (data.kind == EffectBeamKind.LightningStrike) direction = Vector2.down;
        Vector2 normal = new(-direction.y, direction.x);
        float length = EffectNumbers.Positive(data.length, 4f) * scale;
        Vector2 start = parent.position;
        if (data.kind == EffectBeamKind.LightningStrike) start -= direction * length;
        if (laser) length *= extension;
        Vector2 end = start + direction * length;
        Center = new Vector3((start.x + end.x) * 0.5f, (start.y + end.y) * 0.5f, parent.position.z);
        float width = EffectNumbers.Positive(data.width, 0.12f) * scale;
        float glow = EffectNumbers.NonNegative(data.glowRadius) * scale;
        float jitter = data.kind == EffectBeamKind.Laser ? 0f : EffectNumbers.NonNegative(data.zigzag) * scale;
        float chargeRadius = EffectNumbers.NonNegative(data.chargeRadius) * scale;
        float sparkSpread = EffectNumbers.NonNegative(data.sparkSpread) * scale;
        float margin = Mathf.Max(width + glow + jitter + data.wobble * scale,
            chargeRadius * 2f + sparkSpread);
        Vector2 low = Vector2.Min(start, end) - Vector2.one * margin;
        Vector2 high = Vector2.Max(start, end) + Vector2.one * margin;
        if (camera != null && TryGetView(camera, parent.position.z, out Rect view))
        {
            low = Vector2.Max(low, view.min);
            high = Vector2.Min(high, view.max);
        }
        if (low.x >= high.x || low.y >= high.y) { surface.enabled = false; return; }
        float step = worldPixelStep;
        step = EffectNumbers.Positive(step, 0.03125f);
        // 화면과 교차하는 영역만 최대 256×256 픽셀로 계산합니다.
        step *= Mathf.Max(1f, Mathf.Ceil(Mathf.Max(high.x - low.x, high.y - low.y) / (step * 254f)));
        low = new Vector2(Mathf.Floor(low.x / step) * step, Mathf.Floor(low.y / step) * step);
        int columns = Mathf.Clamp(Mathf.CeilToInt((high.x - low.x) / step), 1, 256);
        int rows = Mathf.Clamp(Mathf.CeilToInt((high.y - low.y) / step), 1, 256);
        EnsureTexture(columns, rows);
        Array.Clear(pixels, 0, pixels.Length);
        int count = laser ? 1 : Mathf.Clamp(data.segments, 2, MaxSegments);
        float flash = Mathf.Floor(age * EffectNumbers.Positive(data.refreshRate, 20f));
        for (int i = 0; i <= count; i++)
        {
            float fraction = (float)i / count;
            float along = length * fraction;
            float shift = i == 0 || i == count ? 0f :
                (Noise(i * 13.7f + flash * 31.3f) * 2f - 1f) * jitter *
                Mathf.Sin(fraction * Mathf.PI);
            Vector2 point = start + direction * along + normal * shift;
            points[i] = new Vector2(Mathf.Round(point.x / step) * step, Mathf.Round(point.y / step) * step);
        }
        float sourceRadius = chargeRadius * (charging ? Mathf.Lerp(0.25f, 0.7f, charge) :
            Mathf.Lerp(0.7f, 0.3f, extension)) * pulse;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                Vector2 point = low + new Vector2(x + 0.5f, y + 0.5f) * step;
                float distanceSquared = float.PositiveInfinity;
                Vector2 closest = start;
                for (int i = 0; i < count; i++)
                {
                    Vector2 segment = points[i + 1] - points[i];
                    float t = Mathf.Clamp01(Vector2.Dot(point - points[i], segment) / Mathf.Max(0.000001f, segment.sqrMagnitude));
                    Vector2 candidate = points[i] + segment * t;
                    float distance = (point - candidate).sqrMagnitude;
                    if (distance >= distanceSquared) continue;
                    distanceSquared = distance;
                    closest = candidate;
                }
                float along = Vector2.Dot(closest - start, direction);
                float flow = Mathf.PerlinNoise(seed + along * 2.3f / Mathf.Max(0.001f, scale), flowTime);
                float radius = width * 0.5f * pulse * Mathf.Lerp(0.85f, 1.15f, flow);
                float distanceFromLine = Mathf.Sqrt(distanceSquared);
                float body = charging ? 0f : Coverage(distanceFromLine, radius, step);
                float halo = glow > 0f ? Mathf.Clamp01(1f - Mathf.Max(0f, distanceFromLine - radius) / glow) : 0f;
                halo = charging ? 0f : halo * halo * Mathf.Clamp01(data.glowOpacity);
                float sourceDistance = (point - start).magnitude;
                float source = Coverage(sourceDistance, sourceRadius, step);
                float sourceHalo = sourceRadius + glow > 0f ?
                    Mathf.Pow(Mathf.Clamp01(1f - sourceDistance / (sourceRadius + glow)), 2f) * data.glowOpacity : 0f;
                float sourceCore = Coverage(sourceDistance, sourceRadius * 0.4f, step) * source;
                body = Mathf.Max(body, source);
                halo = Mathf.Max(halo, sourceHalo);
                if (body <= 0f && halo <= 0f) continue;
                float shift = (flow * 2f - 1f) * Mathf.Min(data.wobble * scale, radius * 0.3f);
                float lateral = Vector2.Dot(point - closest, normal);
                float core = charging ? 0f : Coverage(Mathf.Abs(lateral - shift),
                    Mathf.Max(step * 0.5f, radius * Mathf.Clamp(data.coreWidth, 0.05f, 1f)), step) * body;
                core = Mathf.Max(core, sourceCore);
                Color color = Color.Lerp(data.edge, data.core, core);
                color.a *= Mathf.Max(body, halo) * envelope;
                pixels[y * textureWidth + x] = color;
            }
        int sparks = Mathf.Clamp(data.sparkCount, 0, 32);
        float lifetime = EffectNumbers.Positive(data.sparkLifetime, 0.18f);
        for (int i = 0; i < sparks; i++)
        {
            float cycle = age / lifetime + Noise(i * 7.3f);
            float generation = Mathf.Floor(cycle);
            float t = cycle - generation;
            float random = i * 17.1f + generation * 43.7f;
            float side = Noise(random) * 2f - 1f;
            Vector2 position;
            if (charging)
            {
                float angle = Noise(random + 3f) * Mathf.PI * 2f;
                Vector2 radial = new(Mathf.Cos(angle), Mathf.Sin(angle));
                position = start + radial * chargeRadius * (0.4f + (1f - t) * 1.6f) * (1f - charge);
            }
            else
            {
                float travel = Mathf.Clamp01(Noise(random + 9f) * 0.7f + t * 0.3f);
                position = start + direction * (length * travel) + normal *
                    (side * (width + jitter + sparkSpread * t * t));
                position -= direction * (sparkSpread * t * (1f - t));
            }
            float opacity = charging ? Mathf.Sin(t * Mathf.PI) * charge : (1f - t) * (1f - t);
            Color color = Color.Lerp(data.core, data.edge, t);
            color.a *= opacity * envelope;
            DrawSpark(position, color, low, step, columns, rows);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        surface.transform.position = new Vector3(low.x, low.y, parent.position.z);
        surface.transform.rotation = Quaternion.identity;
        surface.transform.localScale = new Vector3(step / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            step / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)), 1f);
        surface.color = Color.white;
        surface.enabled = true;
    }

    private static float Coverage(float distance, float radius, float step) =>
        Mathf.Clamp01((radius - distance) / Mathf.Max(0.0001f, step) + 0.5f);

    private float Noise(float value) => Mathf.Repeat(Mathf.Sin(seed + value * 12.9898f) * 43758.5453f, 1f);

    private void DrawSpark(Vector2 position, Color color, Vector2 low, float step, int columns, int rows)
    {
        int x = Mathf.FloorToInt((position.x - low.x) / step);
        int y = Mathf.FloorToInt((position.y - low.y) / step);
        if (x < 0 || x >= columns || y < 0 || y >= rows) return;
        int index = y * textureWidth + x;
        Color under = pixels[index];
        float alpha = color.a + under.a * (1f - color.a);
        if (alpha <= 0f) return;
        Color result = (color * color.a + under * (under.a * (1f - color.a))) / alpha;
        result.a = alpha;
        pixels[index] = result;
    }

    private void EnsureTexture(int columns, int rows)
    {
        if (texture != null && columns <= textureWidth && rows <= textureHeight) return;
        ReleaseTexture();
        textureWidth = Mathf.Max(textureWidth, Mathf.NextPowerOfTwo(columns));
        textureHeight = Mathf.Max(textureHeight, Mathf.NextPowerOfTwo(rows));
        texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
        {
            name = "Pixel Field Beam", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        pixels = new Color32[textureWidth * textureHeight];
        sprite = Sprite.Create(texture, new Rect(0, 0, textureWidth, textureHeight), Vector2.zero, 1f, 0, SpriteMeshType.FullRect);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        surface.sprite = sprite;
    }

    private static bool TryGetView(Camera camera, float z, out Rect view)
    {
        Plane plane = new(Vector3.forward, new Vector3(0f, 0f, z));
        Vector2 low = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 high = new(float.NegativeInfinity, float.NegativeInfinity);
        view = default;
        for (int i = 0; i < 4; i++)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3(i & 1, i >> 1, 0f));
            if (!plane.Raycast(ray, out float distance)) return false;
            Vector2 point = ray.GetPoint(distance);
            low = Vector2.Min(low, point);
            high = Vector2.Max(high, point);
        }
        view = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        return true;
    }

    public void Clear() { if (surface != null) surface.enabled = false; nextRefresh = 0f; Intensity = 0f; resetSeed = true; wasCharging = false; }

    private void ReleaseTexture()
    {
        if (Application.isPlaying)
        {
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
        else
        {
            if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        }
        sprite = null;
        texture = null;
    }

    public void Dispose() { Clear(); ReleaseTexture(); }
}

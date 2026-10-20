using System;
using UnityEngine;

public sealed class FieldBeamEffectVisual : IDisposable
{
    private const int MaxSegments = 32;
    private readonly Vector2[] points = new Vector2[MaxSegments + 1];
    private readonly Transform parent;
    private readonly SpriteRenderer surface;
    private readonly float seed = UnityEngine.Random.Range(0f, 1000f);
    private Texture2D texture;
    private Sprite sprite;
    private Color32[] pixels;
    private float nextRefresh;
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
        float pulse = 1f + Mathf.Sin(age * data.pulseFrequency * Mathf.PI * 2f) * Mathf.Clamp01(data.pulseStrength);
        Intensity = envelope * pulse;
        if (size <= 0f || envelope <= 0f) { surface.enabled = false; return; }
        if (age < nextRefresh && surface.enabled) return;
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
        Vector2 end = start + direction * length;
        Center = new Vector3((start.x + end.x) * 0.5f, (start.y + end.y) * 0.5f, parent.position.z);
        float width = EffectNumbers.Positive(data.width, 0.12f) * scale;
        float glow = EffectNumbers.NonNegative(data.glowRadius) * scale;
        float jitter = data.kind == EffectBeamKind.Laser ? 0f : EffectNumbers.NonNegative(data.zigzag) * scale;
        float margin = width + glow + jitter + data.wobble * scale;
        Vector2 low = Vector2.Min(start, end) - Vector2.one * margin;
        Vector2 high = Vector2.Max(start, end) + Vector2.one * margin;
        if (camera != null && TryGetView(camera, parent.position.z, out Rect view))
        {
            low = Vector2.Max(low, view.min);
            high = Vector2.Min(high, view.max);
        }
        if (low.x >= high.x || low.y >= high.y) { surface.enabled = false; return; }
        float step = data.useWorldPixels ? worldPixelStep : EffectNumbers.Positive(data.pixelSize, worldPixelStep);
        step = EffectNumbers.Positive(step, 0.03125f);
        // 화면과 교차하는 영역만 최대 256×256 픽셀로 계산합니다.
        step *= Mathf.Max(1f, Mathf.Ceil(Mathf.Max(high.x - low.x, high.y - low.y) / (step * 254f)));
        low = new Vector2(Mathf.Floor(low.x / step) * step, Mathf.Floor(low.y / step) * step);
        int columns = Mathf.Clamp(Mathf.CeilToInt((high.x - low.x) / step), 1, 256);
        int rows = Mathf.Clamp(Mathf.CeilToInt((high.y - low.y) / step), 1, 256);
        EnsureTexture(columns, rows);
        Array.Clear(pixels, 0, pixels.Length);
        int count = data.kind == EffectBeamKind.Laser ? 1 : Mathf.Clamp(data.segments, 2, MaxSegments);
        for (int i = 0; i <= count; i++)
        {
            float along = length * i / count;
            float shift = i == 0 || i == count ? 0f :
                (Mathf.PerlinNoise(seed + i * 2.7f, age * data.flowSpeed) * 2f - 1f) * jitter;
            Vector2 point = start + direction * along + normal * shift;
            points[i] = new Vector2(Mathf.Round(point.x / step) * step, Mathf.Round(point.y / step) * step);
        }
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
                float flow = Mathf.Sin(along * 18f / Mathf.Max(0.001f, scale) - age * data.flowSpeed * 6f + seed);
                float radius = width * 0.5f * (pulse + flow * 0.08f);
                float distanceFromLine = Mathf.Sqrt(distanceSquared);
                float body = Coverage(distanceFromLine, radius, step);
                float halo = glow > 0f ? Mathf.Clamp01(1f - Mathf.Max(0f, distanceFromLine - radius) / glow) : 0f;
                halo = halo * halo * Mathf.Clamp01(data.glowOpacity);
                if (body <= 0f && halo <= 0f) continue;
                float shift = flow * Mathf.Min(data.wobble * scale, radius * 0.3f);
                float lateral = Vector2.Dot(point - closest, normal);
                float core = Coverage(Mathf.Abs(lateral - shift),
                    Mathf.Max(step, radius * Mathf.Clamp(data.coreWidth, 0.05f, 1f)), step) * body;
                Color color = Color.Lerp(data.edge, data.core, core);
                color.a *= Mathf.Max(body, halo) * envelope;
                pixels[y * textureWidth + x] = color;
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

    public void Clear() { if (surface != null) surface.enabled = false; nextRefresh = 0f; Intensity = 0f; }

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

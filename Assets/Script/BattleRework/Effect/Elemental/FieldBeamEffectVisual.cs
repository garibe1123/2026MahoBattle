using System;
using UnityEngine;

public sealed class FieldBeamEffectVisual : IDisposable
{
    private const int MaxSegments = 32;
    private readonly SpriteRenderer[] edges = new SpriteRenderer[MaxSegments];
    private readonly SpriteRenderer[] cores = new SpriteRenderer[MaxSegments];
    private readonly Vector2[] points = new Vector2[MaxSegments + 1];
    private readonly Transform root;
    private readonly Sprite pixel;
    private float nextRefresh;
    private int count;

    public FieldBeamEffectVisual(Transform parent, Material material)
    {
        GameObject host = new("Field Beam") { hideFlags = parent.gameObject.hideFlags };
        root = host.transform;
        root.SetParent(parent, false);
        pixel = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        pixel.hideFlags = HideFlags.HideAndDontSave;
        for (int i = 0; i < MaxSegments; i++)
        {
            edges[i] = CreateRenderer("Edge " + i, material);
            cores[i] = CreateRenderer("Core " + i, material);
        }
    }

    private SpriteRenderer CreateRenderer(string name, Material material)
    {
        GameObject host = new(name) { hideFlags = root.gameObject.hideFlags };
        host.transform.SetParent(root, false);
        SpriteRenderer renderer = host.AddComponent<SpriteRenderer>();
        renderer.sprite = pixel;
        renderer.sharedMaterial = material;
        renderer.enabled = false;
        return renderer;
    }

    public void Render(EffectBeamSettings data, EffectRenderSort sorting, float age, bool visible, float size)
    {
        root.localScale = Vector3.one * size;
        if (!visible) { Hide(); return; }
        int segments = data.kind == EffectBeamKind.Laser ? 1 : Mathf.Clamp(data.segments, 2, MaxSegments);
        if (count != segments || age >= nextRefresh)
        {
            count = segments;
            nextRefresh = age + 1f / EffectNumbers.Positive(data.refreshRate, 20f);
            Vector2 direction = data.kind == EffectBeamKind.LightningStrike ? Vector2.down :
                data.direction.sqrMagnitude > 0.0001f ? data.direction.normalized : Vector2.right;
            float length = EffectNumbers.Positive(data.length, 4f);
            Vector2 start = data.kind == EffectBeamKind.LightningStrike ? Vector2.up * length : Vector2.zero;
            Vector2 normal = new(-direction.y, direction.x);
            float step = EffectNumbers.Positive(data.pixelSize, 0.03125f);
            for (int i = 0; i <= count; i++)
            {
                Vector2 point = start + direction * (length * i / count);
                if (i > 0 && i < count)
                    point += normal * UnityEngine.Random.Range(-data.zigzag, data.zigzag);
                points[i] = new Vector2(Mathf.Round(point.x / step) * step, Mathf.Round(point.y / step) * step);
            }
        }
        float width = EffectNumbers.Positive(data.width, 0.12f);
        for (int i = 0; i < MaxSegments; i++)
        {
            bool active = i < count && size > 0f;
            edges[i].enabled = cores[i].enabled = active;
            if (!active) continue;
            Place(edges[i], points[i], points[i + 1], width, data.edge);
            Place(cores[i], points[i], points[i + 1], width * Mathf.Clamp(data.coreWidth, 0.05f, 1f), data.core);
            sorting.Apply(edges[i]);
            sorting.Apply(cores[i], 1);
        }
    }

    private static void Place(SpriteRenderer renderer, Vector2 start, Vector2 end, float width, Color color)
    {
        Vector2 delta = end - start;
        renderer.transform.localPosition = (start + end) * 0.5f;
        renderer.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        renderer.transform.localScale = new Vector3(delta.magnitude + width * 0.5f, width, 1f);
        renderer.color = color;
    }

    private void Hide()
    {
        for (int i = 0; i < MaxSegments; i++) edges[i].enabled = cores[i].enabled = false;
    }

    public void Clear() { Hide(); count = 0; nextRefresh = 0f; }

    public void Dispose()
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(pixel);
        else UnityEngine.Object.DestroyImmediate(pixel);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal static class BattleSpriteAlphaGeometry
{
    private sealed class Entry
    {
        public Texture2D texture;
        public int mipLimit;
        public Vector2[] hull;
    }

    private static readonly Dictionary<Sprite, Entry> Entries = new();
    private static int generation;
    private static bool unsupportedReported;

    internal static bool TryGet(Sprite sprite, out Vector2[] hull)
    {
        Texture2D splitAlpha = sprite.associatedAlphaSplitTexture;
        Texture2D texture = splitAlpha != null ? splitAlpha : sprite.texture;
        int mipLimit = texture.activeMipmapLimit;
        if (!Entries.TryGetValue(sprite, out Entry entry) || entry.texture != texture || entry.mipLimit != mipLimit)
        {
            entry = new Entry { texture = texture, mipLimit = mipLimit };
            Entries[sprite] = entry;
            Request(sprite, entry, splitAlpha != null);
        }
        hull = entry.hull;
        return hull != null && hull.Length > 0;
    }

    private static void Request(Sprite sprite, Entry entry, bool redAlpha)
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            entry.hull = Array.Empty<Vector2>();
            if (!unsupportedReported)
                Debug.LogWarning("[BattleShadow] Alpha contact detection requires GPU readback support.");
            unsupportedReported = true;
            return;
        }

        Vector2[] uv = sprite.uv;
        Vector2[] vertices = sprite.vertices;
        ushort[] triangles = sprite.triangles;
        Vector2 uvMin = Vector2.one;
        Vector2 uvMax = Vector2.zero;
        for (int i = 0; i < uv.Length; i++)
        {
            uvMin = Vector2.Min(uvMin, uv[i]);
            uvMax = Vector2.Max(uvMax, uv[i]);
        }
        int textureWidth = Mathf.Max(1, entry.texture.width >> entry.mipLimit);
        int textureHeight = Mathf.Max(1, entry.texture.height >> entry.mipLimit);
        int x = Mathf.Clamp(Mathf.FloorToInt(uvMin.x * textureWidth), 0, textureWidth - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(uvMin.y * textureHeight), 0, textureHeight - 1);
        int width = Mathf.Clamp(Mathf.CeilToInt(uvMax.x * textureWidth) - x, 1, textureWidth - x);
        int height = Mathf.Clamp(Mathf.CeilToInt(uvMax.y * textureHeight) - y, 1, textureHeight - y);
        int requestGeneration = generation;

        try
        {
            AsyncGPUReadback.Request(entry.texture, 0, x, width, y, height, 0, 1, TextureFormat.RGBA32, request =>
            {
                if (requestGeneration != generation || sprite == null)
                    return;
                if (request.hasError)
                {
                    entry.hull = Array.Empty<Vector2>();
                    Debug.LogWarning("[BattleShadow] Could not read sprite alpha: " + sprite.name, sprite);
                    return;
                }

                int firstTriangle = -1;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    if (Mathf.Abs(Cross(uv[triangles[i + 1]] - uv[triangles[i]],
                        uv[triangles[i + 2]] - uv[triangles[i]])) <= 0.000000000001f)
                        continue;
                    firstTriangle = i;
                    break;
                }
                if (firstTriangle < 0)
                {
                    entry.hull = Array.Empty<Vector2>();
                    return;
                }

                int a = triangles[firstTriangle];
                int b = triangles[firstTriangle + 1];
                int c = triangles[firstTriangle + 2];
                Vector2 uvX = uv[b] - uv[a];
                Vector2 uvY = uv[c] - uv[a];
                float determinant = Cross(uvX, uvY);
                Vector2 Local(float pixelX, float pixelY)
                {
                    Vector2 delta = new Vector2(pixelX / textureWidth, pixelY / textureHeight) - uv[a];
                    return vertices[a] + (vertices[b] - vertices[a]) * (Cross(delta, uvY) / determinant) +
                        (vertices[c] - vertices[a]) * (Cross(uvX, delta) / determinant);
                }

                var pixels = request.GetData<Color32>();
                List<Vector2> points = new(height * 4);
                for (int row = 0; row < height; row++)
                {
                    int first = width;
                    int last = -1;
                    for (int column = 0; column < width; column++)
                    {
                        Color32 pixel = pixels[row * width + column];
                        if ((redAlpha ? pixel.r : pixel.a) == 0)
                            continue;
                        Vector2 sample = new((x + column + 0.5f) / textureWidth, (y + row + 0.5f) / textureHeight);
                        if (!InsideMesh(sample, uv, triangles))
                            continue;
                        first = Mathf.Min(first, column);
                        last = column;
                    }
                    if (last < first)
                        continue;
                    points.Add(Local(x + first, y + row));
                    points.Add(Local(x + last + 1, y + row));
                    points.Add(Local(x + first, y + row + 1));
                    points.Add(Local(x + last + 1, y + row + 1));
                }
                entry.hull = ConvexHull(points);
            });
        }
        catch (Exception exception)
        {
            entry.hull = Array.Empty<Vector2>();
            Debug.LogWarning("[BattleShadow] Could not request sprite alpha: " + exception.Message, sprite);
        }
    }

    private static bool InsideMesh(Vector2 point, Vector2[] uv, ushort[] triangles)
    {
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector2 origin = uv[triangles[i]];
            Vector2 x = uv[triangles[i + 1]] - origin;
            Vector2 y = uv[triangles[i + 2]] - origin;
            float determinant = Cross(x, y);
            if (Mathf.Abs(determinant) <= 0.000000000001f)
                continue;
            Vector2 delta = point - origin;
            float u = Cross(delta, y) / determinant;
            float v = Cross(x, delta) / determinant;
            if (u >= -0.0001f && v >= -0.0001f && u + v <= 1.0001f)
                return true;
        }
        return false;
    }

    private static Vector2[] ConvexHull(List<Vector2> points)
    {
        if (points.Count == 0)
            return Array.Empty<Vector2>();
        points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        List<Vector2> hull = new(points.Count * 2);
        for (int i = 0; i < points.Count; i++)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2],
                points[i] - hull[hull.Count - 1]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(points[i]);
        }
        int lowerCount = hull.Count;
        for (int i = points.Count - 2; i >= 0; i--)
        {
            while (hull.Count > lowerCount && Cross(hull[hull.Count - 1] - hull[hull.Count - 2],
                points[i] - hull[hull.Count - 1]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(points[i]);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull.ToArray();
    }

    internal static Bounds WorldBounds(SpriteRenderer renderer, Vector2[] hull, out Vector3 bottomLeft, out Vector3 bottomRight)
    {
        Matrix4x4 matrix = renderer.transform.localToWorldMatrix * Matrix4x4.Scale(
            new Vector3(renderer.flipX ? -1f : 1f, renderer.flipY ? -1f : 1f, 1f));
        bool sliced = renderer.drawMode != SpriteDrawMode.Simple;
        Sprite sprite = renderer.sprite;
        Bounds original = sliced ? sprite.bounds : default;
        Vector4 border = sliced ? sprite.border / sprite.pixelsPerUnit : Vector4.zero;
        Vector2 targetSize = sliced ? renderer.size : Vector2.zero;
        Bounds bounds = new(WorldPoint(hull[0]), Vector3.zero);
        for (int i = 1; i < hull.Length; i++)
            bounds.Encapsulate(WorldPoint(hull[i]));
        bottomLeft = new Vector3(float.PositiveInfinity, bounds.min.y, bounds.center.z);
        bottomRight = new Vector3(float.NegativeInfinity, bounds.min.y, bounds.center.z);
        for (int i = 0; i < hull.Length; i++)
        {
            Vector3 point = WorldPoint(hull[i]);
            if (point.y - bounds.min.y > 0.00001f)
                continue;
            bottomLeft.x = Mathf.Min(bottomLeft.x, point.x);
            bottomRight.x = Mathf.Max(bottomRight.x, point.x);
        }
        return bounds;

        Vector3 WorldPoint(Vector2 point)
        {
            if (sliced)
            {
                point.x = SlicedCoordinate(point.x, original.min.x, original.size.x, targetSize.x, border.x, border.z);
                point.y = SlicedCoordinate(point.y, original.min.y, original.size.y, targetSize.y, border.y, border.w);
            }
            return matrix.MultiplyPoint3x4(point);
        }
    }

    private static float SlicedCoordinate(float point, float min, float size, float target, float lower, float upper)
    {
        float targetMin = min / Mathf.Max(0.0001f, size) * target;
        float borderScale = Mathf.Min(1f, target / Mathf.Max(0.0001f, lower + upper));
        float offset = point - min;
        if (offset <= lower)
            return targetMin + offset * borderScale;
        if (offset >= size - upper)
            return targetMin + target - (size - offset) * borderScale;
        return targetMin + lower * borderScale + (offset - lower) / Mathf.Max(0.0001f, size - lower - upper) *
            Mathf.Max(0f, target - (lower + upper) * borderScale);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear()
    {
        generation++;
        Entries.Clear();
        unsupportedReported = false;
    }
}

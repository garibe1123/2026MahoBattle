using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal static class BattleSpriteAlphaGeometry
{
    internal sealed class Volume
    {
        public Texture2D texture;
        public Vector4 rect;
        public float radius;
        public float maxRadius;
    }

    private sealed class Entry
    {
        public Texture2D texture;
        public int mipLimit;
        public Vector2[] hull;
        public Volume volume;
        public Sprite sprite;
        public Vector2 size;
        public SpriteTileMode tileMode;
        public float threshold;
    }

    private static readonly Dictionary<Sprite, Entry> Entries = new();
    private static readonly Dictionary<SpriteRenderer, Entry> TiledEntries = new();
    private static int generation;
    private static bool unsupportedReported;

    internal static void ReleaseTiled(SpriteRenderer renderer)
    {
        if (ReferenceEquals(renderer, null) || !TiledEntries.TryGetValue(renderer, out Entry entry))
            return;
        TiledEntries.Remove(renderer);
        if (entry.volume?.texture != null)
            UnityEngine.Object.Destroy(entry.volume.texture);
    }

    internal static bool TryGet(SpriteRenderer renderer, out Vector2[] hull, out Volume volume)
    {
        if (renderer.drawMode != SpriteDrawMode.Tiled)
            return TryGet(renderer.sprite, out hull, out volume);
        Sprite sprite = renderer.sprite;
        Texture2D splitAlpha = sprite.associatedAlphaSplitTexture;
        Texture2D texture = splitAlpha != null ? splitAlpha : sprite.texture;
        int mipLimit = texture.activeMipmapLimit;
        if (!TiledEntries.TryGetValue(renderer, out Entry entry) || entry.sprite != sprite ||
            entry.texture != texture || entry.mipLimit != mipLimit || entry.size != renderer.size ||
            entry.tileMode != renderer.tileMode || entry.threshold != renderer.adaptiveModeThreshold)
        {
            if (entry?.volume?.texture != null)
                UnityEngine.Object.Destroy(entry.volume.texture);
            entry = new Entry
            {
                sprite = sprite, texture = texture, mipLimit = mipLimit, size = renderer.size,
                tileMode = renderer.tileMode, threshold = renderer.adaptiveModeThreshold
            };
            TiledEntries[renderer] = entry;
            RequestTiled(renderer, entry);
        }
        hull = entry.hull;
        volume = entry.volume;
        return hull != null && hull.Length > 0 && volume != null;
    }

    private static void RequestTiled(SpriteRenderer renderer, Entry entry)
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            entry.hull = Array.Empty<Vector2>();
            if (!unsupportedReported)
                Debug.LogWarning("[BattleShadow] GPU readback unavailable; using sprite projection fallback.");
            unsupportedReported = true;
            return;
        }
        float resolution = entry.sprite.pixelsPerUnit / Mathf.Pow(2f, entry.mipLimit);
        float pixelWidth = Mathf.Ceil(entry.size.x * resolution);
        float pixelHeight = Mathf.Ceil(entry.size.y * resolution);
        if (entry.size.x <= 0f || entry.size.y <= 0f || pixelWidth > SystemInfo.maxTextureSize ||
            pixelHeight + 2f > SystemInfo.maxTextureSize || pixelWidth + 2f > SystemInfo.maxTextureSize ||
            (pixelWidth + 2f) * (pixelHeight + 2f) > 4194304f)
        {
            entry.hull = Array.Empty<Vector2>();
            Debug.LogWarning("[BattleShadow] Tiled alpha volume exceeds the readback budget; using sprite projection fallback.", renderer);
            return;
        }
        int columns = Mathf.Max(1, (int)pixelWidth);
        int rows = Mathf.Max(1, (int)pixelHeight);
        Vector2 minimum = new(-entry.sprite.pivot.x / entry.sprite.rect.width * entry.size.x,
            -entry.sprite.pivot.y / entry.sprite.rect.height * entry.size.y);
        int requestGeneration = generation;
        RenderTexture capture = null;
        RenderTexture previousTarget = RenderTexture.active;
        CommandBuffer commands = new() { name = "BattleTiledShadowAlpha" };
        try
        {
            capture = RenderTexture.GetTemporary(columns, rows, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            commands.SetRenderTarget(capture);
            commands.SetViewport(new Rect(0, 0, columns, rows));
            commands.ClearRenderTarget(false, true, Color.clear);
            commands.SetGlobalVector("_VolumeBakeRect", new Vector4(minimum.x, minimum.y, 1f / entry.size.x, 1f / entry.size.y));
            // Capture Unity's tiled mesh so adaptive repeats, borders and partial tiles share the same coordinates.
            commands.DrawRenderer(renderer, BattleSpriteShadow.SharedMaterial, 0, 4);
            Graphics.ExecuteCommandBuffer(commands);
            RenderTexture pendingCapture = capture;
            AsyncGPUReadback.Request(capture, 0, 0, columns, 0, rows, 0, 1, TextureFormat.RGBA32, request =>
            {
                try
                {
                    if (requestGeneration != generation || renderer == null ||
                        !TiledEntries.TryGetValue(renderer, out Entry current) || current != entry)
                        return;
                    if (request.hasError)
                    {
                        entry.hull = Array.Empty<Vector2>();
                        Debug.LogWarning("[BattleShadow] Could not read tiled alpha; using sprite projection fallback.", renderer);
                        return;
                    }
                    var pixels = request.GetData<Color32>();
                    Color32[] samples = new Color32[columns * rows];
                    List<Vector2> points = new(rows * 4);
                    Vector2 pixelSize = new(entry.size.x / columns, entry.size.y / rows);
                    for (int row = 0; row < rows; row++)
                    {
                        int first = columns;
                        int last = -1;
                        for (int column = 0; column < columns; column++)
                        {
                            int index = row * columns + column;
                            samples[index] = pixels[index];
                            if (pixels[index].a == 0)
                                continue;
                            first = Mathf.Min(first, column);
                            last = column;
                        }
                        if (last < first)
                            continue;
                        points.Add(minimum + Vector2.Scale(new Vector2(first, row), pixelSize));
                        points.Add(minimum + Vector2.Scale(new Vector2(last + 1, row), pixelSize));
                        points.Add(minimum + Vector2.Scale(new Vector2(first, row + 1), pixelSize));
                        points.Add(minimum + Vector2.Scale(new Vector2(last + 1, row + 1), pixelSize));
                    }
                    entry.hull = ConvexHull(points);
                    if (entry.hull.Length > 0)
                        entry.volume = CreateVolume(samples, columns, rows, minimum, entry.size, entry.sprite.name);
                }
                catch (Exception exception)
                {
                    entry.hull = Array.Empty<Vector2>();
                    Debug.LogWarning("[BattleShadow] Could not build tiled volume: " + exception.Message, renderer);
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(pendingCapture);
                }
            });
            capture = null;
        }
        catch (Exception exception)
        {
            entry.hull = Array.Empty<Vector2>();
            Debug.LogWarning("[BattleShadow] Could not capture tiled alpha: " + exception.Message, renderer);
        }
        finally
        {
            RenderTexture.active = previousTarget;
            commands.Release();
            if (capture != null)
                RenderTexture.ReleaseTemporary(capture);
        }
    }

    internal static bool TryGet(Sprite sprite, out Vector2[] hull, out Volume volume)
    {
        Texture2D splitAlpha = sprite.associatedAlphaSplitTexture;
        Texture2D texture = splitAlpha != null ? splitAlpha : sprite.texture;
        int mipLimit = texture.activeMipmapLimit;
        if (!Entries.TryGetValue(sprite, out Entry entry) || entry.texture != texture || entry.mipLimit != mipLimit)
        {
            if (entry?.volume?.texture != null)
                UnityEngine.Object.Destroy(entry.volume.texture);
            entry = new Entry { texture = texture, mipLimit = mipLimit };
            Entries[sprite] = entry;
            Request(sprite, entry, splitAlpha != null);
        }
        hull = entry.hull;
        volume = entry.volume;
        return hull != null && hull.Length > 0 && volume != null;
    }

    private static void Request(Sprite sprite, Entry entry, bool redAlpha)
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            entry.hull = Array.Empty<Vector2>();
            if (!unsupportedReported)
                Debug.LogWarning("[BattleShadow] GPU readback unavailable; using sprite projection fallback.");
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
                if (requestGeneration != generation || sprite == null ||
                    !Entries.TryGetValue(sprite, out Entry current) || current != entry)
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
                if (entry.hull.Length == 0)
                    return;

                Bounds bounds = sprite.bounds;
                float resolution = sprite.pixelsPerUnit / Mathf.Pow(2f, entry.mipLimit);
                int columns = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x * resolution));
                int rows = Mathf.Max(1, Mathf.CeilToInt(bounds.size.y * resolution));
                float pixelWidth = bounds.size.x / columns;
                float pixelHeight = bounds.size.y / rows;
                Color32[] samples = new Color32[columns * rows];
                Vector2 localX = vertices[b] - vertices[a];
                Vector2 localY = vertices[c] - vertices[a];
                float localDeterminant = Cross(localX, localY);
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        Vector2 delta = new Vector2(bounds.min.x + (column + 0.5f) * pixelWidth,
                            bounds.min.y + (row + 0.5f) * pixelHeight) - vertices[a];
                        Vector2 sample = uv[a] + uvX * (Cross(delta, localY) / localDeterminant) +
                            uvY * (Cross(localX, delta) / localDeterminant);
                        int px = Mathf.FloorToInt(sample.x * textureWidth) - x;
                        int py = Mathf.FloorToInt(sample.y * textureHeight) - y;
                        if (px < 0 || px >= width || py < 0 || py >= height || !InsideMesh(sample, uv, triangles))
                            continue;
                        Color32 pixel = pixels[py * width + px];
                        samples[row * columns + column] = new Color32(0, 0, 0, redAlpha ? pixel.r : pixel.a);
                    }
                }
                entry.volume = CreateVolume(samples, columns, rows, bounds.min, bounds.size, sprite.name);
            });
        }
        catch (Exception exception)
        {
            entry.hull = Array.Empty<Vector2>();
            Debug.LogWarning("[BattleShadow] Could not request sprite alpha: " + exception.Message, sprite);
        }
    }

    private static Volume CreateVolume(Color32[] samples, int columns, int rows, Vector2 minimum, Vector2 size, string name)
    {
        float pixelWidth = size.x / columns;
        float pixelHeight = size.y / rows;
        float maxRadius = 0f;
        Color32[] depthPixels = new Color32[(columns + 2) * (rows + 2)];
        for (int row = 0; row < rows; row++)
        {
            int first = 0;
            while (first < columns)
            {
                if (samples[row * columns + first].a == 0)
                {
                    first++;
                    continue;
                }
                int end = first + 1;
                while (end < columns && samples[row * columns + end].a != 0)
                    end++;
                maxRadius = Mathf.Max(maxRadius, (end - first) * pixelWidth * 0.5f);
                for (int column = first; column < end; column++)
                {
                    // Revolve each separate opaque run into a circular cross-section.
                    float depth = Mathf.Sqrt((column + 0.5f - first) * (end - column - 0.5f));
                    int packed = Mathf.Clamp(Mathf.RoundToInt(depth * 2f / columns * 65535f), 0, 65535);
                    depthPixels[(row + 1) * (columns + 2) + column + 1] = new Color32((byte)(packed >> 8),
                        (byte)(packed & 255), samples[row * columns + column].a, 255);
                }
                first = end;
            }
        }
        Texture2D volumeTexture = new(columns + 2, rows + 2, TextureFormat.RGBA32, false, true)
        {
            name = name + " ShadowVolume", filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
        };
        volumeTexture.SetPixels32(depthPixels);
        volumeTexture.Apply(false, true);
        return new Volume
        {
            texture = volumeTexture, radius = size.x * 0.5f, maxRadius = maxRadius,
            rect = new Vector4(minimum.x - pixelWidth, minimum.y - pixelHeight,
                1f / ((columns + 2) * pixelWidth), 1f / ((rows + 2) * pixelHeight))
        };
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
        bool sliced = renderer.drawMode == SpriteDrawMode.Sliced;
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
        foreach (Entry entry in Entries.Values)
            if (entry.volume?.texture != null)
                UnityEngine.Object.Destroy(entry.volume.texture);
        foreach (Entry entry in TiledEntries.Values)
            if (entry.volume?.texture != null)
                UnityEngine.Object.Destroy(entry.volume.texture);
        Entries.Clear();
        TiledEntries.Clear();
        unsupportedReported = false;
    }
}

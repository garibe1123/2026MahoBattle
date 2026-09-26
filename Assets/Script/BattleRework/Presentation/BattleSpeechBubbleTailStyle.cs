using System;
using UnityEngine;

/// <summary>
/// 전투 / Reward / Map이 공통으로 사용하는 말풍선 꼬리 스타일.
///
/// Shape는 Root + 선택적인 P1/P2/P3 + Tip으로 정의합니다.
/// pivotCount를 0~3 사이에서 조절해 실제 사용되는 꺾임점 수를 줄일 수 있습니다.
/// 0이면 Root -> Tip 두 점만 사용하므로 단순 삼각형 꼬리가 됩니다.
///
/// Width는 각 중심점의 반폭이며 Texture Height 대비 정규화 값입니다.
/// Stroke는 각 중심점에서 검은 외곽선의 픽셀 두께입니다.
/// Root/Tip의 Stroke 값을 다르게 주면 좌→우로 굵어지거나 얇아지는 외곽선을 만들 수 있습니다.
/// </summary>
[Serializable]
public sealed class BattleSpeechBubbleTailStyle
{
    [Header("UI Placement")]
    [Tooltip("실제 UI에서 꼬리가 차지하는 크기입니다. X=전체 길이, Y=전체 높이.")]
    public Vector2 uiSize = new(176f, 84f);

    [Tooltip("말풍선 본체 안으로 꼬리가 들어가는 길이입니다. 이 값이 클수록 접합부가 더 깊게 겹칩니다.")]
    [Range(0f, 96f)]
    public float overlap = 42f;

    [Tooltip("꼬리 Root가 말풍선 위/아래 모서리에 너무 붙지 않도록 하는 여백입니다.")]
    [Range(0f, 120f)]
    public float edgePadding = 34f;

    [Header("Center Line Pivots (Normalized)")]
    [Tooltip("Root와 Tip 사이에서 실제 사용할 중간 Pivot 수입니다. 0=단순 삼각형, 3=기존 번개형 최대 구성.")]
    [Range(0, 3)]
    public int pivotCount = 3;

    [Tooltip("말풍선과 맞닿는 Root 중심의 Y 위치입니다. 0=아래, 0.5=중앙, 1=위.")]
    [Range(0f, 1f)]
    public float rootY = 0.50f;

    [Tooltip("첫 번째 꺾임점. Pivot Count가 1 이상일 때 사용합니다.")]
    public Vector2 pivot1 = new(0.28f, 0.50f);

    [Tooltip("두 번째 꺾임점. Pivot Count가 2 이상일 때 사용합니다.")]
    public Vector2 pivot2 = new(0.52f, 0.50f);

    [Tooltip("세 번째 꺾임점. Pivot Count가 3일 때 사용합니다.")]
    public Vector2 pivot3 = new(0.76f, 0.50f);

    [Tooltip("최종 뾰족점. Y를 올리면 끝점이 위로 치켜 올라갑니다.")]
    public Vector2 tip = new(0.985f, 0.62f);

    [Header("Half Width by Pivot")]
    [Tooltip("Root의 반폭. Texture Height 대비 비율입니다.")]
    [Range(0.08f, 0.50f)]
    public float rootHalfWidth = 0.46f;

    [Range(0.04f, 0.50f)]
    public float pivot1HalfWidth = 0.36f;

    [Range(0.03f, 0.45f)]
    public float pivot2HalfWidth = 0.27f;

    [Range(0.01f, 0.35f)]
    public float pivot3HalfWidth = 0.14f;

    [Header("Stroke Thickness by Pivot (Texture Pixels)")]
    [Tooltip("Root 쪽 사선의 Stroke 두께입니다. Root의 세로 캡 자체는 열어두어 말풍선과 자연스럽게 이어집니다.")]
    [Range(1f, 48f)]
    public float strokeRoot = 15f;

    [Range(1f, 48f)]
    public float strokePivot1 = 15f;

    [Range(1f, 48f)]
    public float strokePivot2 = 15f;

    [Range(1f, 48f)]
    public float strokePivot3 = 15f;

    [Tooltip("Tip의 검은 외곽 두께입니다. 크게 할수록 끝부분이 더 굵고 만화적으로 보입니다.")]
    [Range(1f, 48f)]
    public float strokeTip = 15f;

    [Header("Color")]
    public Color outlineColor = new(0.015f, 0.015f, 0.02f, 1f);

    // 기존 씬/프리팹에는 pivotCount 필드가 없으므로 최초 로드 시
    // 예전 5점 구조(ROOT+P1+P2+P3+TIP)를 그대로 유지하도록 마이그레이션합니다.
    [SerializeField, HideInInspector]
    private bool pivotCountInitialized;

    public int ActivePivotCount
    {
        get
        {
            EnsurePivotCountDefaults();
            return Mathf.Clamp(pivotCount, 0, 3);
        }
    }

    public int ActivePointCount => ActivePivotCount + 2;

    public void EnsurePivotCountDefaults()
    {
        if (!pivotCountInitialized)
        {
            pivotCount = 3;
            pivotCountInitialized = true;
        }

        pivotCount = Mathf.Clamp(pivotCount, 0, 3);
    }

    public void ApplyTrianglePreset()
    {
        pivotCount = 0;
        pivotCountInitialized = true;

        uiSize = new Vector2(176f, 84f);
        overlap = 42f;
        edgePadding = 34f;

        rootY = 0.50f;
        pivot1 = new Vector2(0.28f, 0.50f);
        pivot2 = new Vector2(0.52f, 0.50f);
        pivot3 = new Vector2(0.76f, 0.50f);
        tip = new Vector2(0.985f, 0.50f);

        rootHalfWidth = 0.46f;
        pivot1HalfWidth = 0.36f;
        pivot2HalfWidth = 0.27f;
        pivot3HalfWidth = 0.14f;

        strokeRoot = 15f;
        strokePivot1 = 15f;
        strokePivot2 = 15f;
        strokePivot3 = 15f;
        strokeTip = 15f;
    }

    public void ApplyLightningPreset()
    {
        pivotCount = 3;
        pivotCountInitialized = true;

        uiSize = new Vector2(196f, 92f);
        overlap = 46f;
        edgePadding = 34f;

        rootY = 0.50f;
        pivot1 = new Vector2(0.27f, 0.66f);
        pivot2 = new Vector2(0.50f, 0.34f);
        pivot3 = new Vector2(0.75f, 0.62f);
        tip = new Vector2(0.99f, 0.56f);

        rootHalfWidth = 0.44f;
        pivot1HalfWidth = 0.33f;
        pivot2HalfWidth = 0.25f;
        pivot3HalfWidth = 0.13f;

        strokeRoot = 14f;
        strokePivot1 = 16f;
        strokePivot2 = 18f;
        strokePivot3 = 20f;
        strokeTip = 22f;
    }

    public int ComputeHash()
    {
        EnsurePivotCountDefaults();

        unchecked
        {
            int hash = 17;
            hash = hash * 31 + pivotCount.GetHashCode();
            hash = hash * 31 + uiSize.GetHashCode();
            hash = hash * 31 + overlap.GetHashCode();
            hash = hash * 31 + edgePadding.GetHashCode();
            hash = hash * 31 + rootY.GetHashCode();
            hash = hash * 31 + pivot1.GetHashCode();
            hash = hash * 31 + pivot2.GetHashCode();
            hash = hash * 31 + pivot3.GetHashCode();
            hash = hash * 31 + tip.GetHashCode();
            hash = hash * 31 + rootHalfWidth.GetHashCode();
            hash = hash * 31 + pivot1HalfWidth.GetHashCode();
            hash = hash * 31 + pivot2HalfWidth.GetHashCode();
            hash = hash * 31 + pivot3HalfWidth.GetHashCode();
            hash = hash * 31 + strokeRoot.GetHashCode();
            hash = hash * 31 + strokePivot1.GetHashCode();
            hash = hash * 31 + strokePivot2.GetHashCode();
            hash = hash * 31 + strokePivot3.GetHashCode();
            hash = hash * 31 + strokeTip.GetHashCode();
            hash = hash * 31 + outlineColor.GetHashCode();
            return hash;
        }
    }
}

/// <summary>
/// Runtime과 Editor Preview가 동일하게 사용하는 Tail Texture Builder.
/// 외부 PNG를 사용하지 않고 투명 Texture에 Outline/Fill Polygon을 직접 Rasterize합니다.
/// </summary>
public static class BattleSpeechBubbleTailTextureBuilder
{
    public const int TextureWidth = 384;
    public const int TextureHeight = 192;

    public static Texture2D BuildTexture(
        BattleSpeechBubbleTailStyle sourceStyle,
        Color fillColor,
        string textureName = "BattleSpeechTailRuntimeTexture")
    {
        BattleSpeechBubbleTailStyle style =
            sourceStyle ?? new BattleSpeechBubbleTailStyle();

        style.EnsurePivotCountDefaults();

        Texture2D texture = new(
            TextureWidth,
            TextureHeight,
            TextureFormat.RGBA32,
            false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
            name = textureName
        };

        Color32[] pixels =
            new Color32[TextureWidth * TextureHeight];

        Color32 transparent =
            new(0, 0, 0, 0);

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = transparent;

        Vector2[] centers =
            GetCenterPointsPixels(style);

        float[] outerHalfWidths =
            GetOuterHalfWidthsPixels(style);

        float[] strokes =
            GetStrokePixels(style);

        BuildRibbonPolygon(
            centers,
            outerHalfWidths,
            out Vector2[] outerPolygon);

        RasterizePolygon(
            pixels,
            outerPolygon,
            style.outlineColor);

        Vector2[] innerCenters =
            (Vector2[])centers.Clone();

        int pointCount =
            centers.Length;

        // Root 캡은 열어두고, Tip만 뒤로 당겨 끝부분 Stroke를 만듭니다.
        Vector2 tipTangent =
            ResolveTangent(
                centers,
                pointCount - 1);

        innerCenters[pointCount - 1] -=
            tipTangent *
            Mathf.Max(
                1f,
                strokes[pointCount - 1] * 1.25f);

        float[] innerHalfWidths =
            new float[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            // Root는 Outer와 같은 폭으로 시작해 세로 캡 Stroke가 생기지 않게 합니다.
            if (i == 0)
            {
                innerHalfWidths[i] =
                    outerHalfWidths[i];
            }
            else
            {
                innerHalfWidths[i] =
                    Mathf.Max(
                        0f,
                        outerHalfWidths[i] -
                        Mathf.Max(1f, strokes[i]));
            }
        }

        BuildRibbonPolygon(
            innerCenters,
            innerHalfWidths,
            out Vector2[] innerPolygon);

        RasterizePolygon(
            pixels,
            innerPolygon,
            fillColor);

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    public static Vector2[] GetCenterPointsNormalized(
        BattleSpeechBubbleTailStyle sourceStyle)
    {
        BattleSpeechBubbleTailStyle style =
            sourceStyle ?? new BattleSpeechBubbleTailStyle();

        style.EnsurePivotCountDefaults();

        int pivotCount =
            style.ActivePivotCount;

        Vector2[] result =
            new Vector2[pivotCount + 2];

        int index = 0;

        result[index++] =
            new Vector2(
                0f,
                Mathf.Clamp01(style.rootY));

        if (pivotCount >= 1)
            result[index++] = ClampPoint(style.pivot1);

        if (pivotCount >= 2)
            result[index++] = ClampPoint(style.pivot2);

        if (pivotCount >= 3)
            result[index++] = ClampPoint(style.pivot3);

        result[index] =
            ClampPoint(style.tip);

        return result;
    }

    private static Vector2[] GetCenterPointsPixels(
        BattleSpeechBubbleTailStyle style)
    {
        Vector2[] normalized =
            GetCenterPointsNormalized(style);

        Vector2[] pixels =
            new Vector2[normalized.Length];

        for (int i = 0; i < normalized.Length; i++)
        {
            pixels[i] =
                new Vector2(
                    normalized[i].x * (TextureWidth - 1f),
                    normalized[i].y * (TextureHeight - 1f));
        }

        return pixels;
    }

    private static float[] GetOuterHalfWidthsPixels(
        BattleSpeechBubbleTailStyle style)
    {
        int pivotCount =
            style.ActivePivotCount;

        float[] result =
            new float[pivotCount + 2];

        int index = 0;

        result[index++] =
            Mathf.Clamp(
                style.rootHalfWidth,
                0.02f,
                0.50f) *
            TextureHeight;

        if (pivotCount >= 1)
        {
            result[index++] =
                Mathf.Clamp(
                    style.pivot1HalfWidth,
                    0.01f,
                    0.50f) *
                TextureHeight;
        }

        if (pivotCount >= 2)
        {
            result[index++] =
                Mathf.Clamp(
                    style.pivot2HalfWidth,
                    0.01f,
                    0.50f) *
                TextureHeight;
        }

        if (pivotCount >= 3)
        {
            result[index++] =
                Mathf.Clamp(
                    style.pivot3HalfWidth,
                    0.005f,
                    0.50f) *
                TextureHeight;
        }

        result[index] = 0f;

        return result;
    }

    private static float[] GetStrokePixels(
        BattleSpeechBubbleTailStyle style)
    {
        int pivotCount =
            style.ActivePivotCount;

        float[] result =
            new float[pivotCount + 2];

        int index = 0;

        result[index++] =
            Mathf.Max(
                1f,
                style.strokeRoot);

        if (pivotCount >= 1)
        {
            result[index++] =
                Mathf.Max(
                    1f,
                    style.strokePivot1);
        }

        if (pivotCount >= 2)
        {
            result[index++] =
                Mathf.Max(
                    1f,
                    style.strokePivot2);
        }

        if (pivotCount >= 3)
        {
            result[index++] =
                Mathf.Max(
                    1f,
                    style.strokePivot3);
        }

        result[index] =
            Mathf.Max(
                1f,
                style.strokeTip);

        return result;
    }

    private static void BuildRibbonPolygon(
        Vector2[] centers,
        float[] halfWidths,
        out Vector2[] polygon)
    {
        int pointCount =
            centers != null
                ? centers.Length
                : 0;

        if (pointCount < 2 ||
            halfWidths == null ||
            halfWidths.Length != pointCount)
        {
            polygon = Array.Empty<Vector2>();
            return;
        }

        Vector2[] upper =
            new Vector2[pointCount];

        Vector2[] lower =
            new Vector2[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            Vector2 tangent =
                ResolveTangent(
                    centers,
                    i);

            Vector2 normal =
                new(-tangent.y, tangent.x);

            upper[i] =
                centers[i] +
                normal * halfWidths[i];

            lower[i] =
                centers[i] -
                normal * halfWidths[i];
        }

        polygon =
            new Vector2[pointCount * 2];

        for (int i = 0; i < pointCount; i++)
            polygon[i] = upper[i];

        for (int i = 0; i < pointCount; i++)
        {
            polygon[pointCount + i] =
                lower[pointCount - 1 - i];
        }
    }

    private static Vector2 ResolveTangent(
        Vector2[] centers,
        int index)
    {
        if (centers == null ||
            centers.Length < 2)
        {
            return Vector2.right;
        }

        int last =
            centers.Length - 1;

        Vector2 tangent;

        if (index <= 0)
        {
            tangent =
                centers[1] -
                centers[0];
        }
        else if (index >= last)
        {
            tangent =
                centers[last] -
                centers[last - 1];
        }
        else
        {
            Vector2 previous =
                (centers[index] -
                 centers[index - 1]).normalized;

            Vector2 next =
                (centers[index + 1] -
                 centers[index]).normalized;

            tangent =
                previous + next;

            if (tangent.sqrMagnitude <= 0.0001f)
                tangent = next;
        }

        if (tangent.sqrMagnitude <= 0.0001f)
            return Vector2.right;

        return tangent.normalized;
    }

    private static Vector2 ClampPoint(Vector2 point)
    {
        return new Vector2(
            Mathf.Clamp01(point.x),
            Mathf.Clamp01(point.y));
    }

    private static void RasterizePolygon(
        Color32[] pixels,
        Vector2[] polygon,
        Color32 color)
    {
        if (polygon == null ||
            polygon.Length < 3)
        {
            return;
        }

        int minX = TextureWidth - 1;
        int maxX = 0;
        int minY = TextureHeight - 1;
        int maxY = 0;

        for (int i = 0; i < polygon.Length; i++)
        {
            minX =
                Mathf.Min(
                    minX,
                    Mathf.FloorToInt(
                        polygon[i].x));

            maxX =
                Mathf.Max(
                    maxX,
                    Mathf.CeilToInt(
                        polygon[i].x));

            minY =
                Mathf.Min(
                    minY,
                    Mathf.FloorToInt(
                        polygon[i].y));

            maxY =
                Mathf.Max(
                    maxY,
                    Mathf.CeilToInt(
                        polygon[i].y));
        }

        minX = Mathf.Clamp(minX, 0, TextureWidth - 1);
        maxX = Mathf.Clamp(maxX, 0, TextureWidth - 1);
        minY = Mathf.Clamp(minY, 0, TextureHeight - 1);
        maxY = Mathf.Clamp(maxY, 0, TextureHeight - 1);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 point =
                    new(x + 0.5f, y + 0.5f);

                if (PointInPolygon(
                    point,
                    polygon))
                {
                    pixels[
                        y * TextureWidth +
                        x] = color;
                }
            }
        }
    }

    private static bool PointInPolygon(
        Vector2 point,
        Vector2[] polygon)
    {
        bool inside = false;
        int previous =
            polygon.Length - 1;

        for (int current = 0;
             current < polygon.Length;
             current++)
        {
            Vector2 a =
                polygon[current];

            Vector2 b =
                polygon[previous];

            bool crosses =
                ((a.y > point.y) !=
                 (b.y > point.y));

            if (crosses)
            {
                float intersectionX =
                    (b.x - a.x) *
                    (point.y - a.y) /
                    (b.y - a.y) +
                    a.x;

                if (point.x < intersectionX)
                    inside = !inside;
            }

            previous = current;
        }

        return inside;
    }
}

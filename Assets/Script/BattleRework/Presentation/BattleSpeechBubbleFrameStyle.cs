using System;
using UnityEngine;
using UnityEngine.Serialization;


/// <summary>
/// 말풍선이 열릴 때 한 번만 샘플링하는 미세 런타임 변형 범위입니다.
/// 원본 Style을 직접 변경하지 않고 복제본에만 적용합니다.
/// </summary>
[Serializable]
public sealed class BattleSpeechBubbleRuntimeVariationSettings
{
    [Tooltip("켜면 대사가 열릴 때마다 Frame/Tail의 런타임 복제본을 미세하게 변형합니다.")]
    public bool enabled = true;

    [Header("Frame")]
    [Tooltip("검은 OUTLINE 네 꼭짓점의 최대 픽셀 흔들림입니다.")]
    [Range(0f, 8f)]
    public float frameOutlineCornerJitter = 2.2f;

    [Tooltip("흰 INNER 네 꼭짓점의 최대 픽셀 흔들림입니다. OUTLINE과 별도로 흔들려 Stroke 비율도 조금씩 달라집니다.")]
    [Range(0f, 6f)]
    public float frameInnerCornerJitter = 1.2f;

    [Tooltip("런타임 변형 시 기본 Frame Stroke보다 평균적으로 얼마나 더 굵게 만들지 정합니다. OUTLINE은 바깥으로, INNER는 안쪽으로 절반씩 이동해 총 간격을 늘립니다.")]
    [Range(0f, 8f)]
    public float frameStrokeBiasPixels = 2.5f;

    [Tooltip("말풍선 전체 회전값에 더하는 최대 각도 편차입니다.")]
    [Range(0f, 2f)]
    public float frameRotationJitter = 0.30f;

    [Header("Tail")]
    [Tooltip("P1/P2/P3 중간 Pivot의 X/Y 최대 정규화 편차입니다. ROOT와 TIP 위치는 변경하지 않습니다.")]
    [Range(0f, 0.15f)]
    public float tailPivotPositionJitter = 0.035f;

    [Tooltip("Tail 각 구간 반폭의 비율 편차입니다. 0.10이면 기준값의 ±10%입니다.")]
    [Range(0f, 0.35f)]
    public float tailWidthRatioJitter = 0.10f;

    [Tooltip("Tail Stroke를 랜덤 흔들기 전에 적용하는 기본 배율입니다. 1.15면 원본보다 15% 굵은 값을 중심으로 랜덤이 적용됩니다.")]
    [Range(0.75f, 1.75f)]
    public float tailStrokeBaseMultiplier = 1.15f;

    [Tooltip("Tail Stroke 두께의 비율 편차입니다. 0.12면 위 기본 배율을 적용한 값에서 ±12%입니다.")]
    [Range(0f, 0.40f)]
    public float tailStrokeRatioJitter = 0.12f;

    [Tooltip("활성 Pivot 수를 기준값에서 최대 몇 단계까지 바꿀지 정합니다. 기본 1이면 3 Pivot 꼬리는 2~3 사이에서만 변합니다.")]
    [Range(0, 3)]
    public int tailPivotCountVariation = 1;

    [Tooltip("Pivot 수 자체를 바꿀 확률입니다. 실패하면 Pivot 수는 원본 그대로 유지합니다.")]
    [Range(0f, 1f)]
    public float tailPivotCountChangeChance = 0.20f;
}

/// <summary>
/// UnityEngine.Random 전역 상태와 분리된 말풍선 전용 난수 유틸리티입니다.
/// UI 변형이 드랍/전투 등의 게임플레이 난수 순서를 바꾸지 않게 합니다.
/// </summary>
internal static class BattleSpeechBubbleVariationRandom
{
    private static int sequence;

    public static int NextSeed(int salt = 0)
    {
        unchecked
        {
            sequence++;
            return
                Environment.TickCount ^
                (sequence * 7919) ^
                (salt * 486187739);
        }
    }

    public static float Range(
        System.Random random,
        float min,
        float max)
    {
        if (random == null ||
            max <= min)
        {
            return min;
        }

        return
            min +
            (float)random.NextDouble() *
            (max - min);
    }

    public static float Signed(
        System.Random random,
        float magnitude)
    {
        float amount =
            Mathf.Max(0f, magnitude);

        return Range(
            random,
            -amount,
            amount);
    }

    public static Vector2 Jitter(
        System.Random random,
        Vector2 value,
        float magnitude)
    {
        return value +
            new Vector2(
                Signed(random, magnitude),
                Signed(random, magnitude));
    }

    public static float Ratio(
        System.Random random,
        float value,
        float ratioJitter)
    {
        float ratio =
            Mathf.Max(0f, ratioJitter);

        return
            value *
            (1f + Signed(random, ratio));
    }
}

/// <summary>
/// 말풍선 본체를 두 개의 독립된 4점 사변형으로 정의합니다.
///
/// RED / OUTLINE:
///   outlineTopLeft / TopRight / BottomRight / BottomLeft
///   Root 네 꼭짓점에서 바깥 검은 사변형 꼭짓점까지의 Offset.
///
/// GREEN / INNER:
///   fillTopLeft / TopRight / BottomRight / BottomLeft
///   Root 네 꼭짓점에서 안쪽 흰 사변형 꼭짓점까지의 Offset.
///
/// Stroke 두께는 별도 L/R/T/B 값이 아니라,
/// 같은 위치의 RED와 GREEN 사변형 사이 거리 자체로 결정됩니다.
/// </summary>
[Serializable]
public sealed class BattleSpeechBubbleFrameStyle
{
    [Header("Frame Shape")]
    [Tooltip("말풍선 전체 회전 각도입니다. 창 Size/Scale은 고정값을 사용합니다.")]
    [Range(-12f, 12f)]
    public float rotation = -1.5f;

    [Header("RED - Outline Corner Offset")]
    [Tooltip("Root 좌상단에서 검은 OUTLINE 좌상단 꼭짓점까지의 Offset.")]
    public Vector2 outlineTopLeft = new(-8f, 9f);

    [Tooltip("Root 우상단에서 검은 OUTLINE 우상단 꼭짓점까지의 Offset.")]
    public Vector2 outlineTopRight = new(8f, 9f);

    [Tooltip("Root 우하단에서 검은 OUTLINE 우하단 꼭짓점까지의 Offset.")]
    public Vector2 outlineBottomRight = new(8f, -9f);

    [Tooltip("Root 좌하단에서 검은 OUTLINE 좌하단 꼭짓점까지의 Offset.")]
    public Vector2 outlineBottomLeft = new(-8f, -9f);

    [Header("GREEN - Inner Fill Corner Offset")]
    [Tooltip("Root 좌상단에서 흰 INNER 좌상단 꼭짓점까지의 Offset.")]
    public Vector2 fillTopLeft = new(2f, -2f);

    [Tooltip("Root 우상단에서 흰 INNER 우상단 꼭짓점까지의 Offset.")]
    public Vector2 fillTopRight = new(-2f, -2f);

    [Tooltip("Root 우하단에서 흰 INNER 우하단 꼭짓점까지의 Offset.")]
    public Vector2 fillBottomRight = new(-2f, 2f);

    [Tooltip("Root 좌하단에서 흰 INNER 좌하단 꼭짓점까지의 Offset.")]
    public Vector2 fillBottomLeft = new(2f, 2f);

    [Header("Color")]
    public Color outlineColor =
        new(0.012f, 0.012f, 0.018f, 0.99f);

    public Color fillColor =
        new(0.97f, 0.97f, 0.94f, 1f);

    // ------------------------------------------------------------------
    // Legacy migration
    // ------------------------------------------------------------------

    [FormerlySerializedAs("outlineLeft")]
    [SerializeField, HideInInspector]
    private float legacyOutlineLeft;

    [FormerlySerializedAs("outlineBottom")]
    [SerializeField, HideInInspector]
    private float legacyOutlineBottom;

    [FormerlySerializedAs("outlineRight")]
    [SerializeField, HideInInspector]
    private float legacyOutlineRight;

    [FormerlySerializedAs("outlineTop")]
    [SerializeField, HideInInspector]
    private float legacyOutlineTop;

    [FormerlySerializedAs("strokeTopLeft")]
    [SerializeField, HideInInspector]
    private float legacyStrokeTopLeft;

    [FormerlySerializedAs("strokeBottomLeft")]
    [SerializeField, HideInInspector]
    private float legacyStrokeBottomLeft;

    [FormerlySerializedAs("strokeTopRight")]
    [SerializeField, HideInInspector]
    private float legacyStrokeTopRight;

    [FormerlySerializedAs("strokeBottomRight")]
    [SerializeField, HideInInspector]
    private float legacyStrokeBottomRight;

    [SerializeField, HideInInspector]
    private bool cornerPointsInitialized = true;

    public static BattleSpeechBubbleFrameStyle CreateSelectionDefault()
    {
        return new BattleSpeechBubbleFrameStyle
        {
            rotation = -1.5f,
            outlineTopLeft = new Vector2(-8f, 9f),
            outlineTopRight = new Vector2(8f, 9f),
            outlineBottomRight = new Vector2(8f, -9f),
            outlineBottomLeft = new Vector2(-8f, -9f),
            fillTopLeft = new Vector2(2f, -2f),
            fillTopRight = new Vector2(-2f, -2f),
            fillBottomRight = new Vector2(-2f, 2f),
            fillBottomLeft = new Vector2(2f, 2f),
            cornerPointsInitialized = true
        };
    }

    public static BattleSpeechBubbleFrameStyle CreateCombatDefault()
    {
        return new BattleSpeechBubbleFrameStyle
        {
            rotation = -2.5f,
            outlineTopLeft = new Vector2(-7f, 8f),
            outlineTopRight = new Vector2(7f, 8f),
            outlineBottomRight = new Vector2(7f, -8f),
            outlineBottomLeft = new Vector2(-7f, -8f),
            fillTopLeft = new Vector2(2f, -2f),
            fillTopRight = new Vector2(-2f, -2f),
            fillBottomRight = new Vector2(-2f, 2f),
            fillBottomLeft = new Vector2(2f, 2f),
            cornerPointsInitialized = true
        };
    }

    public void EnsureCornerPointDefaults()
    {
        if (cornerPointsInitialized)
            return;

        float left =
            legacyOutlineLeft > 0f
                ? legacyOutlineLeft
                : 8f;

        float bottom =
            legacyOutlineBottom > 0f
                ? legacyOutlineBottom
                : 9f;

        float right =
            legacyOutlineRight > 0f
                ? legacyOutlineRight
                : 8f;

        float top =
            legacyOutlineTop > 0f
                ? legacyOutlineTop
                : 9f;

        outlineTopLeft =
            new Vector2(-left, top);

        outlineTopRight =
            new Vector2(right, top);

        outlineBottomRight =
            new Vector2(right, -bottom);

        outlineBottomLeft =
            new Vector2(-left, -bottom);

        float stl =
            legacyStrokeTopLeft > 0f
                ? legacyStrokeTopLeft
                : Mathf.Max(left, top) + 2f;

        float sbl =
            legacyStrokeBottomLeft > 0f
                ? legacyStrokeBottomLeft
                : Mathf.Max(left, bottom) + 2f;

        float str =
            legacyStrokeTopRight > 0f
                ? legacyStrokeTopRight
                : Mathf.Max(right, top) + 2f;

        float sbr =
            legacyStrokeBottomRight > 0f
                ? legacyStrokeBottomRight
                : Mathf.Max(right, bottom) + 2f;

        fillTopLeft =
            new Vector2(
                Mathf.Max(0f, stl - left),
                -Mathf.Max(0f, stl - top));

        fillTopRight =
            new Vector2(
                -Mathf.Max(0f, str - right),
                -Mathf.Max(0f, str - top));

        fillBottomRight =
            new Vector2(
                -Mathf.Max(0f, sbr - right),
                Mathf.Max(0f, sbr - bottom));

        fillBottomLeft =
            new Vector2(
                Mathf.Max(0f, sbl - left),
                Mathf.Max(0f, sbl - bottom));

        cornerPointsInitialized = true;
    }

    public Vector2[] GetOutlineCorners(
        Vector2 runtimeSize)
    {
        EnsureCornerPointDefaults();

        float width =
            Mathf.Max(1f, runtimeSize.x);

        float height =
            Mathf.Max(1f, runtimeSize.y);

        return new[]
        {
            // TL
            new Vector2(0f, height) +
            outlineTopLeft,

            // TR
            new Vector2(width, height) +
            outlineTopRight,

            // BR
            new Vector2(width, 0f) +
            outlineBottomRight,

            // BL
            new Vector2(0f, 0f) +
            outlineBottomLeft
        };
    }

    public Vector2[] GetFillCorners(
        Vector2 runtimeSize)
    {
        EnsureCornerPointDefaults();

        float width =
            Mathf.Max(1f, runtimeSize.x);

        float height =
            Mathf.Max(1f, runtimeSize.y);

        return new[]
        {
            // TL
            new Vector2(0f, height) +
            fillTopLeft,

            // TR
            new Vector2(width, height) +
            fillTopRight,

            // BR
            new Vector2(width, 0f) +
            fillBottomRight,

            // BL
            new Vector2(0f, 0f) +
            fillBottomLeft
        };
    }

    public Rect GetLocalBounds(
        Vector2 runtimeSize)
    {
        Vector2[] outline =
            GetOutlineCorners(runtimeSize);

        Vector2[] fill =
            GetFillCorners(runtimeSize);

        float minX = 0f;
        float maxX = Mathf.Max(1f, runtimeSize.x);
        float minY = 0f;
        float maxY = Mathf.Max(1f, runtimeSize.y);

        AccumulateBounds(
            outline,
            ref minX,
            ref maxX,
            ref minY,
            ref maxY);

        AccumulateBounds(
            fill,
            ref minX,
            ref maxX,
            ref minY,
            ref maxY);

        return Rect.MinMaxRect(
            minX,
            minY,
            maxX,
            maxY);
    }

    private static void AccumulateBounds(
        Vector2[] points,
        ref float minX,
        ref float maxX,
        ref float minY,
        ref float maxY)
    {
        if (points == null)
            return;

        for (int i = 0; i < points.Length; i++)
        {
            minX = Mathf.Min(minX, points[i].x);
            maxX = Mathf.Max(maxX, points[i].x);
            minY = Mathf.Min(minY, points[i].y);
            maxY = Mathf.Max(maxY, points[i].y);
        }
    }

    public BattleSpeechBubbleFrameStyle CreateRuntimeVariant(
        BattleSpeechBubbleRuntimeVariationSettings variation,
        int seed)
    {
        EnsureCornerPointDefaults();

        BattleSpeechBubbleFrameStyle result =
            new BattleSpeechBubbleFrameStyle
            {
                rotation = rotation,
                outlineTopLeft = outlineTopLeft,
                outlineTopRight = outlineTopRight,
                outlineBottomRight = outlineBottomRight,
                outlineBottomLeft = outlineBottomLeft,
                fillTopLeft = fillTopLeft,
                fillTopRight = fillTopRight,
                fillBottomRight = fillBottomRight,
                fillBottomLeft = fillBottomLeft,
                outlineColor = outlineColor,
                fillColor = fillColor,
                cornerPointsInitialized = true
            };

        if (variation == null ||
            !variation.enabled)
        {
            return result;
        }

        System.Random random =
            new(seed);

        result.rotation =
            Mathf.Clamp(
                rotation +
                BattleSpeechBubbleVariationRandom.Signed(
                    random,
                    variation.frameRotationJitter),
                -12f,
                12f);

        float outlineJitter =
            Mathf.Max(
                0f,
                variation.frameOutlineCornerJitter);

        float innerJitter =
            Mathf.Max(
                0f,
                variation.frameInnerCornerJitter);

        float strokeBias =
            Mathf.Max(
                0f,
                variation.frameStrokeBiasPixels);

        float halfStrokeBias =
            strokeBias * 0.5f;

        // 기본 Stroke보다 굵은 쪽을 중심으로 변형합니다.
        // OUTLINE은 Root 바깥 방향, INNER는 Root 안쪽 방향으로
        // 각각 절반씩 벌려 총 Stroke 간격을 늘립니다.
        result.outlineTopLeft =
            BattleSpeechBubbleVariationRandom.Jitter(
                random,
                outlineTopLeft +
                new Vector2(
                    -halfStrokeBias,
                    halfStrokeBias),
                outlineJitter);

        result.outlineTopRight =
            BattleSpeechBubbleVariationRandom.Jitter(
                random,
                outlineTopRight +
                new Vector2(
                    halfStrokeBias,
                    halfStrokeBias),
                outlineJitter);

        result.outlineBottomRight =
            BattleSpeechBubbleVariationRandom.Jitter(
                random,
                outlineBottomRight +
                new Vector2(
                    halfStrokeBias,
                    -halfStrokeBias),
                outlineJitter);

        result.outlineBottomLeft =
            BattleSpeechBubbleVariationRandom.Jitter(
                random,
                outlineBottomLeft +
                new Vector2(
                    -halfStrokeBias,
                    -halfStrokeBias),
                outlineJitter);

        result.fillTopLeft =
            BattleSpeechBubbleVariationRandom.Jitter(
                random,
                fillTopLeft +
                new Vector2(
                    halfStrokeBias,
                    -halfStrokeBias),
                innerJitter);

        result.fillTopRight =
            BattleSpeechBubbleVariationRandom.Jitter(
                random,
                fillTopRight +
                new Vector2(
                    -halfStrokeBias,
                    -halfStrokeBias),
                innerJitter);

        result.fillBottomRight =
            BattleSpeechBubbleVariationRandom.Jitter(
                random,
                fillBottomRight +
                new Vector2(
                    -halfStrokeBias,
                    halfStrokeBias),
                innerJitter);

        result.fillBottomLeft =
            BattleSpeechBubbleVariationRandom.Jitter(
                random,
                fillBottomLeft +
                new Vector2(
                    halfStrokeBias,
                    halfStrokeBias),
                innerJitter);

        return result;
    }

    public int ComputeHash()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + rotation.GetHashCode();
            hash = hash * 31 + outlineTopLeft.GetHashCode();
            hash = hash * 31 + outlineTopRight.GetHashCode();
            hash = hash * 31 + outlineBottomRight.GetHashCode();
            hash = hash * 31 + outlineBottomLeft.GetHashCode();
            hash = hash * 31 + fillTopLeft.GetHashCode();
            hash = hash * 31 + fillTopRight.GetHashCode();
            hash = hash * 31 + fillBottomRight.GetHashCode();
            hash = hash * 31 + fillBottomLeft.GetHashCode();
            hash = hash * 31 + outlineColor.GetHashCode();
            hash = hash * 31 + fillColor.GetHashCode();
            return hash;
        }
    }
}

/// <summary>
/// OUTLINE 사변형(검정)을 먼저 그리고 INNER 사변형(흰색)을 그 위에 Rasterize합니다.
/// Preview와 Runtime이 이 Builder 하나를 공유합니다.
/// </summary>
public static class BattleSpeechBubbleFrameTextureBuilder
{
    private const int TextureHeight = 192;

    public static Texture2D BuildFrameTexture(
        BattleSpeechBubbleFrameStyle style,
        Vector2 runtimeSize,
        string textureName,
        out Rect localBounds)
    {
        style ??=
            BattleSpeechBubbleFrameStyle.CreateCombatDefault();

        style.EnsureCornerPointDefaults();

        localBounds =
            style.GetLocalBounds(
                runtimeSize);

        float boundsWidth =
            Mathf.Max(
                1f,
                localBounds.width);

        float boundsHeight =
            Mathf.Max(
                1f,
                localBounds.height);

        float aspect =
            Mathf.Max(
                0.25f,
                boundsWidth /
                boundsHeight);

        int width =
            Mathf.Clamp(
                Mathf.RoundToInt(
                    TextureHeight * aspect),
                128,
                2048);

        int height =
            TextureHeight;

        Texture2D texture =
            new(
                width,
                height,
                TextureFormat.RGBA32,
                false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
                name = textureName
            };

        Color32[] pixels =
            new Color32[width * height];

        Color32 transparent =
            new(0, 0, 0, 0);

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = transparent;

        Vector2[] outline =
            ConvertToTexture(
                style.GetOutlineCorners(runtimeSize),
                localBounds,
                width,
                height);

        Vector2[] fill =
            ConvertToTexture(
                style.GetFillCorners(runtimeSize),
                localBounds,
                width,
                height);

        RasterizePolygon(
            pixels,
            width,
            height,
            outline,
            style.outlineColor);

        RasterizePolygon(
            pixels,
            width,
            height,
            fill,
            style.fillColor);

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        return texture;
    }

    private static Vector2[] ConvertToTexture(
        Vector2[] localPoints,
        Rect bounds,
        int width,
        int height)
    {
        Vector2[] result =
            new Vector2[localPoints.Length];

        float scaleX =
            (width - 1f) /
            Mathf.Max(
                1f,
                bounds.width);

        float scaleY =
            (height - 1f) /
            Mathf.Max(
                1f,
                bounds.height);

        for (int i = 0; i < localPoints.Length; i++)
        {
            result[i] =
                new Vector2(
                    (localPoints[i].x -
                     bounds.xMin) *
                    scaleX,
                    (localPoints[i].y -
                     bounds.yMin) *
                    scaleY);
        }

        return result;
    }

    private static void RasterizePolygon(
        Color32[] pixels,
        int width,
        int height,
        Vector2[] polygon,
        Color32 color)
    {
        if (polygon == null ||
            polygon.Length < 3)
        {
            return;
        }

        int minX = width - 1;
        int maxX = 0;
        int minY = height - 1;
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

        minX = Mathf.Clamp(minX, 0, width - 1);
        maxX = Mathf.Clamp(maxX, 0, width - 1);
        minY = Mathf.Clamp(minY, 0, height - 1);
        maxY = Mathf.Clamp(maxY, 0, height - 1);

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
                        y * width +
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

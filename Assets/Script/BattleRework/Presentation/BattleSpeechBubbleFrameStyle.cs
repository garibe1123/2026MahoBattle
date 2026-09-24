using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 말풍선 본체의 형태 스타일.
///
/// 실제 창 Size와 Pop Scale은 각 Presenter Controller의 고정 디자인 값과
/// CanvasScaler가 담당합니다.
///
/// Outline Outset:
///   검은 바탕이 Root 바깥으로 얼마나 나가는지 L/B/R/T로 조절합니다.
///
/// Corner Stroke:
///   최종 검은 Stroke 두께를 네 꼭짓점에서 정의합니다.
///   TL/BL을 크게, TR/BR을 작게 하면 왼쪽이 굵고 오른쪽으로 갈수록
///   자연스럽게 얇아지는 식으로 각 변 내부에서 선형 보간됩니다.
/// </summary>
[Serializable]
public sealed class BattleSpeechBubbleFrameStyle
{
    [Header("Frame Shape")]
    [Tooltip("말풍선 전체 회전 각도입니다. 창 Size/Scale은 고정값을 사용합니다.")]
    [Range(-12f, 12f)]
    public float rotation = -1.5f;

    [Header("Outline Outset")]
    [Range(0f, 48f)]
    public float outlineLeft = 8f;

    [Range(0f, 48f)]
    public float outlineBottom = 9f;

    [Range(0f, 48f)]
    public float outlineRight = 8f;

    [Range(0f, 48f)]
    public float outlineTop = 9f;

    [Header("Corner Stroke Thickness")]
    [Tooltip("좌상단 꼭짓점에서의 최종 Stroke 두께입니다.")]
    [Range(0f, 96f)]
    public float strokeTopLeft = 11f;

    [Tooltip("좌하단 꼭짓점에서의 최종 Stroke 두께입니다.")]
    [Range(0f, 96f)]
    public float strokeBottomLeft = 11f;

    [Tooltip("우상단 꼭짓점에서의 최종 Stroke 두께입니다.")]
    [Range(0f, 96f)]
    public float strokeTopRight = 11f;

    [Tooltip("우하단 꼭짓점에서의 최종 Stroke 두께입니다.")]
    [Range(0f, 96f)]
    public float strokeBottomRight = 11f;

    [Header("Color")]
    public Color outlineColor =
        new(0.012f, 0.012f, 0.018f, 0.99f);

    public Color fillColor =
        new(0.97f, 0.97f, 0.94f, 1f);

    // 이전 L/B/R/T Stroke 데이터를 최초 1회 Corner 값으로 승계하기 위한 필드.
    [FormerlySerializedAs("strokeLeft")]
    [SerializeField, HideInInspector]
    private float legacyStrokeLeft;

    [FormerlySerializedAs("strokeBottom")]
    [SerializeField, HideInInspector]
    private float legacyStrokeBottom;

    [FormerlySerializedAs("strokeRight")]
    [SerializeField, HideInInspector]
    private float legacyStrokeRight;

    [FormerlySerializedAs("strokeTop")]
    [SerializeField, HideInInspector]
    private float legacyStrokeTop;

    [SerializeField, HideInInspector]
    private bool cornerStrokeInitialized;

    public static BattleSpeechBubbleFrameStyle CreateSelectionDefault()
    {
        return new BattleSpeechBubbleFrameStyle
        {
            rotation = -1.5f,
            outlineLeft = 8f,
            outlineBottom = 9f,
            outlineRight = 8f,
            outlineTop = 9f,
            strokeTopLeft = 11f,
            strokeBottomLeft = 11f,
            strokeTopRight = 11f,
            strokeBottomRight = 11f,
            cornerStrokeInitialized = true
        };
    }

    public static BattleSpeechBubbleFrameStyle CreateCombatDefault()
    {
        return new BattleSpeechBubbleFrameStyle
        {
            rotation = -2.5f,
            outlineLeft = 7f,
            outlineBottom = 8f,
            outlineRight = 7f,
            outlineTop = 8f,
            strokeTopLeft = 10f,
            strokeBottomLeft = 10f,
            strokeTopRight = 10f,
            strokeBottomRight = 10f,
            cornerStrokeInitialized = true
        };
    }

    public void EnsureCornerStrokeDefaults()
    {
        if (cornerStrokeInitialized)
        {
            ClampCornerStrokeMinimums();
            return;
        }

        float left =
            Mathf.Max(
                legacyStrokeLeft,
                outlineLeft + 2f);

        float bottom =
            Mathf.Max(
                legacyStrokeBottom,
                outlineBottom + 2f);

        float right =
            Mathf.Max(
                legacyStrokeRight,
                outlineRight + 2f);

        float top =
            Mathf.Max(
                legacyStrokeTop,
                outlineTop + 2f);

        strokeTopLeft =
            Mathf.Max(left, top);

        strokeBottomLeft =
            Mathf.Max(left, bottom);

        strokeTopRight =
            Mathf.Max(right, top);

        strokeBottomRight =
            Mathf.Max(right, bottom);

        cornerStrokeInitialized = true;
        ClampCornerStrokeMinimums();
    }

    public void ClampCornerStrokeMinimums()
    {
        strokeTopLeft =
            Mathf.Max(
                strokeTopLeft,
                Mathf.Max(
                    outlineLeft,
                    outlineTop));

        strokeBottomLeft =
            Mathf.Max(
                strokeBottomLeft,
                Mathf.Max(
                    outlineLeft,
                    outlineBottom));

        strokeTopRight =
            Mathf.Max(
                strokeTopRight,
                Mathf.Max(
                    outlineRight,
                    outlineTop));

        strokeBottomRight =
            Mathf.Max(
                strokeBottomRight,
                Mathf.Max(
                    outlineRight,
                    outlineBottom));
    }

    /// <summary>
    /// Root 사각형 내부에서 흰 Fill의 네 꼭짓점을 반환합니다.
    /// 좌표계는 왼쪽 아래가 (0,0)인 UI 로컬 좌표입니다.
    /// </summary>
    public Vector2[] GetFillCorners(
        Vector2 runtimeSize)
    {
        EnsureCornerStrokeDefaults();

        float width =
            Mathf.Max(1f, runtimeSize.x);

        float height =
            Mathf.Max(1f, runtimeSize.y);

        float tlInsetX =
            Mathf.Max(
                0f,
                strokeTopLeft - outlineLeft);

        float tlInsetY =
            Mathf.Max(
                0f,
                strokeTopLeft - outlineTop);

        float blInsetX =
            Mathf.Max(
                0f,
                strokeBottomLeft - outlineLeft);

        float blInsetY =
            Mathf.Max(
                0f,
                strokeBottomLeft - outlineBottom);

        float trInsetX =
            Mathf.Max(
                0f,
                strokeTopRight - outlineRight);

        float trInsetY =
            Mathf.Max(
                0f,
                strokeTopRight - outlineTop);

        float brInsetX =
            Mathf.Max(
                0f,
                strokeBottomRight - outlineRight);

        float brInsetY =
            Mathf.Max(
                0f,
                strokeBottomRight - outlineBottom);

        return new[]
        {
            // TL
            new Vector2(
                Mathf.Clamp(
                    tlInsetX,
                    0f,
                    width * 0.48f),
                height -
                Mathf.Clamp(
                    tlInsetY,
                    0f,
                    height * 0.48f)),

            // TR
            new Vector2(
                width -
                Mathf.Clamp(
                    trInsetX,
                    0f,
                    width * 0.48f),
                height -
                Mathf.Clamp(
                    trInsetY,
                    0f,
                    height * 0.48f)),

            // BR
            new Vector2(
                width -
                Mathf.Clamp(
                    brInsetX,
                    0f,
                    width * 0.48f),
                Mathf.Clamp(
                    brInsetY,
                    0f,
                    height * 0.48f)),

            // BL
            new Vector2(
                Mathf.Clamp(
                    blInsetX,
                    0f,
                    width * 0.48f),
                Mathf.Clamp(
                    blInsetY,
                    0f,
                    height * 0.48f))
        };
    }

    public int ComputeHash()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + rotation.GetHashCode();
            hash = hash * 31 + outlineLeft.GetHashCode();
            hash = hash * 31 + outlineBottom.GetHashCode();
            hash = hash * 31 + outlineRight.GetHashCode();
            hash = hash * 31 + outlineTop.GetHashCode();
            hash = hash * 31 + strokeTopLeft.GetHashCode();
            hash = hash * 31 + strokeBottomLeft.GetHashCode();
            hash = hash * 31 + strokeTopRight.GetHashCode();
            hash = hash * 31 + strokeBottomRight.GetHashCode();
            hash = hash * 31 + outlineColor.GetHashCode();
            hash = hash * 31 + fillColor.GetHashCode();
            return hash;
        }
    }
}

/// <summary>
/// Corner Stroke 기반 흰 Fill 사변형을 런타임 Texture로 생성합니다.
/// Custom MaskableGraphic은 사용하지 않고 일반 UI Image Sprite로 표시합니다.
/// </summary>
public static class BattleSpeechBubbleFrameTextureBuilder
{
    private const int TextureHeight = 128;

    public static Texture2D BuildFillTexture(
        BattleSpeechBubbleFrameStyle style,
        Vector2 runtimeSize,
        string textureName)
    {
        style ??=
            BattleSpeechBubbleFrameStyle.CreateCombatDefault();

        style.EnsureCornerStrokeDefaults();

        float aspect =
            Mathf.Max(
                0.25f,
                runtimeSize.x /
                Mathf.Max(
                    1f,
                    runtimeSize.y));

        int width =
            Mathf.Clamp(
                Mathf.RoundToInt(
                    TextureHeight * aspect),
                128,
                1024);

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

        Vector2[] runtimeCorners =
            style.GetFillCorners(
                runtimeSize);

        Vector2[] textureCorners =
            new Vector2[runtimeCorners.Length];

        float scaleX =
            (width - 1f) /
            Mathf.Max(
                1f,
                runtimeSize.x);

        float scaleY =
            (height - 1f) /
            Mathf.Max(
                1f,
                runtimeSize.y);

        for (int i = 0;
             i < runtimeCorners.Length;
             i++)
        {
            textureCorners[i] =
                new Vector2(
                    runtimeCorners[i].x *
                    scaleX,
                    runtimeCorners[i].y *
                    scaleY);
        }

        RasterizePolygon(
            pixels,
            width,
            height,
            textureCorners,
            style.fillColor);

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        return texture;
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

        for (int i = 0;
             i < polygon.Length;
             i++)
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

        minX =
            Mathf.Clamp(
                minX,
                0,
                width - 1);

        maxX =
            Mathf.Clamp(
                maxX,
                0,
                width - 1);

        minY =
            Mathf.Clamp(
                minY,
                0,
                height - 1);

        maxY =
            Mathf.Clamp(
                maxY,
                0,
                height - 1);

        for (int y = minY;
             y <= maxY;
             y++)
        {
            for (int x = minX;
                 x <= maxX;
                 x++)
            {
                Vector2 point =
                    new(
                        x + 0.5f,
                        y + 0.5f);

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

                if (point.x <
                    intersectionX)
                {
                    inside = !inside;
                }
            }

            previous = current;
        }

        return inside;
    }
}

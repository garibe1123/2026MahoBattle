using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 말풍선 옆에 붙는 단순 삼각형 꼬리.
///
/// IMPORTANT:
/// Custom MaskableGraphic / OnPopulateMesh를 사용하지 않습니다.
/// 이 프로젝트의 Overlay/World Canvas 조합에서 custom UI mesh가 사라질 수 있으므로,
/// 코드에서 런타임 Sprite를 생성하고 Unity 기본 Image로 렌더링합니다.
///
/// 외부 PNG 에셋은 사용하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubbleTailTriangleController : MonoBehaviour
{
    private const int RuntimeTextureWidth = 128;
    private const int RuntimeTextureHeight = 64;

    private static Sprite cachedTriangleSprite;
    private static Texture2D cachedTriangleTexture;

    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;

    [Header("Triangle")]
    [Tooltip("X는 꼬리 길이, Y는 말풍선에 붙는 밑변 폭입니다.")]
    [SerializeField] private Vector2 triangleSize = new(138f, 64f);

    [Tooltip("말풍선 안쪽으로 꼬리를 겹치는 깊이입니다. 밑변의 검은 이음선을 BubbleFace 뒤로 숨깁니다.")]
    [SerializeField, Range(0f, 48f)] private float overlap = 30f;

    [Tooltip("꼬리가 말풍선 위/아래 모서리에 너무 가까워지지 않도록 제한합니다.")]
    [SerializeField, Range(0f, 80f)] private float edgePadding = 34f;

    [Header("Stroke")]
    [Tooltip("런타임 Sprite에서 사용하는 검은 외곽선 두께(텍스처 픽셀 기준)입니다. 말풍선 본체 외곽선과 비슷한 체감 두께로 맞춥니다.")]
    [SerializeField, Range(1, 16)] private int outlinePixels = 9;

    [Header("Tip Shape")]
    [Tooltip("뾰족한 끝을 삼각형 중심보다 위로 올리는 양입니다. 양수일수록 위쪽을 향한 만화식 꼬리 느낌이 강해집니다.")]
    [SerializeField, Range(-16f, 16f)] private float tipVerticalBiasPixels = 8f;
    [SerializeField] private Color outlineColor = new(0.015f, 0.015f, 0.02f, 1f);
    [SerializeField] private Color fillColor = new(0.97f, 0.97f, 0.94f, 1f);

    private RectTransform triangleRect;
    private Image triangleImage;
    private int appliedOutlinePixels = -1;
    private float appliedTipVerticalBiasPixels = float.NaN;
    private Color appliedOutlineColor;
    private Color appliedFillColor;

    public void Configure(
        RectTransform bubble,
        RectTransform target,
        Color color)
    {
        bubbleRect = bubble;
        targetPivot = target;
        fillColor = color;

        EnsureTriangle();
        RefreshSpriteIfNeeded(force: true);
        RefreshTriangle();
    }

    public void SetTarget(RectTransform target)
    {
        targetPivot = target;
        RefreshTriangle();
    }

    private void Awake()
    {
        EnsureTriangle();
        RefreshSpriteIfNeeded(force: true);
    }

    private void OnEnable()
    {
        EnsureTriangle();
        RefreshSpriteIfNeeded(force: true);
        RefreshTriangle();
    }

    private void LateUpdate()
    {
        RefreshSpriteIfNeeded(force: false);
        RefreshTriangle();
    }

    private void EnsureTriangle()
    {
        if (triangleRect != null &&
            triangleImage != null)
        {
            return;
        }

        GameObject tail =
            new("TailTriangle", typeof(RectTransform));

        tail.transform.SetParent(transform, false);

        triangleRect =
            tail.GetComponent<RectTransform>();

        Vector2 parentPivot =
            bubbleRect != null
                ? bubbleRect.pivot
                : new Vector2(0.5f, 0.5f);

        triangleRect.anchorMin =
            triangleRect.anchorMax =
                parentPivot;

        triangleRect.pivot =
            new Vector2(0.5f, 0.5f);

        triangleRect.sizeDelta =
            triangleSize;

        triangleImage =
            tail.AddComponent<Image>();

        triangleImage.type =
            Image.Type.Simple;

        triangleImage.preserveAspect =
            false;

        triangleImage.raycastTarget =
            false;

        triangleImage.color =
            Color.white;

        // BubbleInk / BubbleFace보다 뒤에 둡니다.
        // overlap 영역은 말풍선 본체가 덮어서 접합부가 자연스럽게 이어집니다.
        tail.transform.SetAsFirstSibling();
    }

    private void RefreshSpriteIfNeeded(bool force)
    {
        if (triangleImage == null)
            return;

        if (!force &&
            appliedOutlinePixels == outlinePixels &&
            Mathf.Approximately(
                appliedTipVerticalBiasPixels,
                tipVerticalBiasPixels) &&
            ColorsApproximatelyEqual(appliedOutlineColor, outlineColor) &&
            ColorsApproximatelyEqual(appliedFillColor, fillColor) &&
            cachedTriangleSprite != null)
        {
            if (triangleImage.sprite != cachedTriangleSprite)
                triangleImage.sprite = cachedTriangleSprite;

            return;
        }

        appliedOutlinePixels = outlinePixels;
        appliedTipVerticalBiasPixels = tipVerticalBiasPixels;
        appliedOutlineColor = outlineColor;
        appliedFillColor = fillColor;

        RebuildRuntimeTriangleSprite();
        triangleImage.sprite = cachedTriangleSprite;
    }

    private void RefreshTriangle()
    {
        if (triangleRect == null ||
            triangleImage == null ||
            bubbleRect == null ||
            targetPivot == null)
        {
            if (triangleImage != null)
                triangleImage.enabled = false;

            return;
        }

        triangleImage.enabled = true;

        triangleRect.anchorMin =
            triangleRect.anchorMax =
                bubbleRect.pivot;

        triangleRect.sizeDelta =
            triangleSize;

        Vector3 bubbleCenterWorld =
            bubbleRect.TransformPoint(
                bubbleRect.rect.center);

        Vector3 localDelta3 =
            bubbleRect.InverseTransformVector(
                targetPivot.position -
                bubbleCenterWorld);

        Vector2 localDelta =
            new Vector2(
                localDelta3.x,
                localDelta3.y);

        Rect rect =
            bubbleRect.rect;

        // 꼬리는 항상 말풍선 좌/우 옆면에만 붙입니다.
        bool placeRight =
            localDelta.x >= 0f;

        PlaceHorizontal(
            placeRight,
            localDelta.y,
            rect);
    }

    private void PlaceHorizontal(
        bool right,
        float targetLocalY,
        Rect bubble)
    {
        float padding =
            Mathf.Min(
                edgePadding,
                bubble.height * 0.45f);

        float y =
            Mathf.Clamp(
                targetLocalY,
                bubble.yMin + padding,
                bubble.yMax - padding);

        float outside =
            triangleSize.x * 0.5f -
            Mathf.Max(0f, overlap);

        triangleRect.anchoredPosition =
            new Vector2(
                right
                    ? bubble.xMax + outside
                    : bubble.xMin - outside,
                y);

        triangleRect.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                right ? 0f : 180f);
    }

    private void RebuildRuntimeTriangleSprite()
    {
        if (cachedTriangleSprite != null)
        {
            Destroy(cachedTriangleSprite);
            cachedTriangleSprite = null;
        }

        if (cachedTriangleTexture != null)
        {
            Destroy(cachedTriangleTexture);
            cachedTriangleTexture = null;
        }

        cachedTriangleTexture =
            new Texture2D(
                RuntimeTextureWidth,
                RuntimeTextureHeight,
                TextureFormat.RGBA32,
                false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
                name = "BattleSpeechTriangleTailRuntimeTexture"
            };

        Color32[] pixels =
            new Color32[
                RuntimeTextureWidth *
                RuntimeTextureHeight];

        Color32 transparent =
            new Color32(0, 0, 0, 0);

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = transparent;

        Color32 outer =
            outlineColor;

        Color32 inner =
            fillColor;

        // 바깥 검은 삼각형.
        Vector2 outerA =
            new Vector2(0f, 2f);

        Vector2 outerB =
            new Vector2(
                0f,
                RuntimeTextureHeight - 3f);

        float tipY =
            Mathf.Clamp(
                (RuntimeTextureHeight - 1f) * 0.5f +
                tipVerticalBiasPixels,
                4f,
                RuntimeTextureHeight - 5f);

        Vector2 outerTip =
            new Vector2(
                RuntimeTextureWidth - 1f,
                tipY);

        RasterizeTriangle(
            pixels,
            outerA,
            outerB,
            outerTip,
            outer);

        // 안쪽 흰 삼각형.
        // 밑변도 약간 inset되지만 실제 UI에서는 overlap으로 말풍선 뒤에 숨습니다.
        float inset =
            Mathf.Clamp(
                outlinePixels,
                1,
                RuntimeTextureHeight / 4);

        Vector2 innerA =
            new Vector2(
                inset,
                2f + inset);

        Vector2 innerB =
            new Vector2(
                inset,
                RuntimeTextureHeight - 3f - inset);

        Vector2 innerTip =
            new Vector2(
                RuntimeTextureWidth - 1f - inset * 1.65f,
                tipY);

        RasterizeTriangle(
            pixels,
            innerA,
            innerB,
            innerTip,
            inner);

        cachedTriangleTexture.SetPixels32(pixels);
        cachedTriangleTexture.Apply(false, true);

        cachedTriangleSprite =
            Sprite.Create(
                cachedTriangleTexture,
                new Rect(
                    0f,
                    0f,
                    RuntimeTextureWidth,
                    RuntimeTextureHeight),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);

        cachedTriangleSprite.name =
            "BattleSpeechTriangleTailRuntimeSprite";

        cachedTriangleSprite.hideFlags =
            HideFlags.HideAndDontSave;
    }

    private static void RasterizeTriangle(
        Color32[] pixels,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Color32 color)
    {
        float minX =
            Mathf.Floor(
                Mathf.Min(
                    a.x,
                    Mathf.Min(b.x, c.x)));

        float maxX =
            Mathf.Ceil(
                Mathf.Max(
                    a.x,
                    Mathf.Max(b.x, c.x)));

        float minY =
            Mathf.Floor(
                Mathf.Min(
                    a.y,
                    Mathf.Min(b.y, c.y)));

        float maxY =
            Mathf.Ceil(
                Mathf.Max(
                    a.y,
                    Mathf.Max(b.y, c.y)));

        int xMin =
            Mathf.Clamp(
                Mathf.FloorToInt(minX),
                0,
                RuntimeTextureWidth - 1);

        int xMax =
            Mathf.Clamp(
                Mathf.CeilToInt(maxX),
                0,
                RuntimeTextureWidth - 1);

        int yMin =
            Mathf.Clamp(
                Mathf.FloorToInt(minY),
                0,
                RuntimeTextureHeight - 1);

        int yMax =
            Mathf.Clamp(
                Mathf.CeilToInt(maxY),
                0,
                RuntimeTextureHeight - 1);

        float area =
            Edge(a, b, c);

        if (Mathf.Abs(area) <= 0.0001f)
            return;

        for (int y = yMin; y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                Vector2 p =
                    new Vector2(
                        x + 0.5f,
                        y + 0.5f);

                float w0 =
                    Edge(b, c, p);

                float w1 =
                    Edge(c, a, p);

                float w2 =
                    Edge(a, b, p);

                bool inside =
                    area > 0f
                        ? w0 >= 0f &&
                          w1 >= 0f &&
                          w2 >= 0f
                        : w0 <= 0f &&
                          w1 <= 0f &&
                          w2 <= 0f;

                if (inside)
                {
                    pixels[
                        y * RuntimeTextureWidth +
                        x] = color;
                }
            }
        }
    }

    private static float Edge(
        Vector2 a,
        Vector2 b,
        Vector2 p)
    {
        return
            (p.x - a.x) *
            (b.y - a.y) -
            (p.y - a.y) *
            (b.x - a.x);
    }

    private static bool ColorsApproximatelyEqual(
        Color a,
        Color b)
    {
        const float epsilon = 0.001f;

        return
            Mathf.Abs(a.r - b.r) < epsilon &&
            Mathf.Abs(a.g - b.g) < epsilon &&
            Mathf.Abs(a.b - b.b) < epsilon &&
            Mathf.Abs(a.a - b.a) < epsilon;
    }
}

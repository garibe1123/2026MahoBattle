using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 말풍선과 사회자 Pivot 사이를 하나의 닫힌 Ribbon Sprite로 연결합니다.
///
/// 커스텀 UI Mesh나 여러 선분을 쓰지 않고, 런타임에서 생성한 하나의
/// 흰색 면 + 검은 외곽 Sprite를 Unity 기본 Image로 표시합니다.
///
/// RectTransform 전체를 base -> target 방향으로 회전/스케일하므로
/// 아이템 선택 / 스테이지 선택 / 전투 리액션에서 동일하게 사용할 수 있습니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubbleTailRibbonGraphic : MonoBehaviour
{
    private const int TextureWidth = 320;
    private const int TextureHeight = 96;

    private static Sprite cachedRibbonSprite;
    private static Texture2D cachedRibbonTexture;

    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;

    [Header("Ribbon")]
    [Tooltip("꼬리 높이입니다. 레퍼런스처럼 말풍선 일부로 보이도록 너무 얇게 만들지 않습니다.")]
    [SerializeField, Min(24f)] private float ribbonHeight = 74f;
    [Tooltip("꼬리가 말풍선 안쪽으로 살짝 파고들어 자연스럽게 붙도록 하는 값입니다.")]
    [SerializeField, Range(0f, 24f)] private float bubbleOverlap = 10f;
    [Tooltip("Pivot까지 실제로 닿지 않고, 이 최소 길이 이상만 짧게 튀어나옵니다.")]
    [SerializeField, Min(40f)] private float minRibbonLength = 130f;
    [Tooltip("멀리 있는 Pivot을 향해도 꼬리가 창처럼 길어지지 않도록 최대 길이를 제한합니다.")]
    [SerializeField, Min(60f)] private float maxRibbonLength = 210f;
    [Tooltip("Pivot 거리 중 꼬리 길이로 사용할 비율입니다. 최종 길이는 Min/Max로 Clamp됩니다.")]
    [SerializeField, Range(0.15f, 0.55f)] private float ribbonDistanceRatio = 0.34f;
    [SerializeField, Range(0.05f, 0.42f)] private float edgeCornerPadding = 0.14f;

    private RectTransform ribbonRect;
    private Image ribbonImage;
    private readonly Vector3[] bubbleWorldCorners = new Vector3[4];

    public void Configure(
        RectTransform bubble,
        RectTransform target,
        Color fill,
        Color outline,
        float width = 72f,
        float outlineThickness = 8f)
    {
        bubbleRect = bubble;
        targetPivot = target;

        // Existing caller values are retained as style hints.
        ribbonHeight = Mathf.Clamp(width * 0.90f, 54f, 92f);

        EnsureVisual();
        RebuildRibbon();
    }

    public void SetTarget(RectTransform target)
    {
        targetPivot = target;
        RebuildRibbon();
    }

    private void Awake()
    {
        EnsureVisual();
    }

    private void OnEnable()
    {
        EnsureVisual();
        RebuildRibbon();
    }

    private void LateUpdate()
    {
        RebuildRibbon();
    }

    private void EnsureVisual()
    {
        if (ribbonRect != null && ribbonImage != null)
            return;

        GameObject visual = new("RibbonVisual", typeof(RectTransform));
        visual.transform.SetParent(transform, false);

        ribbonRect = visual.GetComponent<RectTransform>();
        ribbonRect.anchorMin = ribbonRect.anchorMax = new Vector2(0.5f, 0.5f);
        ribbonRect.pivot = new Vector2(0f, 0.5f);

        ribbonImage = visual.AddComponent<Image>();
        ribbonImage.sprite = ResolveRibbonSprite();
        ribbonImage.type = Image.Type.Simple;
        ribbonImage.preserveAspect = false;
        ribbonImage.raycastTarget = false;
        ribbonImage.color = Color.white;
    }

    private void RebuildRibbon()
    {
        RectTransform root = transform as RectTransform;

        if (root == null ||
            ribbonRect == null ||
            bubbleRect == null ||
            targetPivot == null)
        {
            SetVisible(false);
            return;
        }

        bubbleRect.GetWorldCorners(bubbleWorldCorners);

        Vector2[] corners = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            corners[i] =
                root.InverseTransformPoint(
                    bubbleWorldCorners[i]);
        }

        Vector2 target =
            root.InverseTransformPoint(
                targetPivot.position);

        ResolveBestEdge(
            corners,
            target,
            out Vector2 basePoint);

        Vector2 delta = target - basePoint;
        float distance = delta.magnitude;

        if (distance <= 18f)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        Vector2 forward = delta / distance;

        // IMPORTANT:
        // targetPivot은 꼬리 "방향"만 결정합니다.
        // 꼬리를 Pivot까지 전부 늘리면 말풍선 꼬리가 아니라 창/연결선처럼 보이므로,
        // 짧은 고정 비율 길이로 제한합니다.
        Vector2 start =
            basePoint -
            forward * Mathf.Max(0f, bubbleOverlap);

        float minimum =
            Mathf.Max(40f, minRibbonLength);

        float maximum =
            Mathf.Max(minimum, maxRibbonLength);

        float desiredLength =
            distance *
            Mathf.Clamp(ribbonDistanceRatio, 0.15f, 0.55f);

        float resolvedLength =
            Mathf.Clamp(
                desiredLength,
                minimum,
                maximum);

        // Pivot이 아주 가까운 경우에는 Pivot을 지나치지 않도록 한 번 더 제한합니다.
        resolvedLength =
            Mathf.Min(
                resolvedLength,
                Mathf.Max(24f, distance - 8f));

        if (resolvedLength <= 1f)
        {
            SetVisible(false);
            return;
        }

        float angle =
            Mathf.Atan2(forward.y, forward.x) *
            Mathf.Rad2Deg;

        ribbonRect.anchoredPosition = start;
        ribbonRect.localRotation =
            Quaternion.Euler(0f, 0f, angle);

        ribbonRect.sizeDelta =
            new Vector2(
                resolvedLength,
                Mathf.Max(24f, ribbonHeight));
    }

    private void ResolveBestEdge(
        Vector2[] corners,
        Vector2 target,
        out Vector2 basePoint)
    {
        float bestDistance =
            float.PositiveInfinity;

        basePoint = Vector2.zero;

        for (int i = 0; i < 4; i++)
        {
            Vector2 a = corners[i];
            Vector2 b =
                corners[(i + 1) % 4];

            Vector2 edge = b - a;
            float lengthSq = edge.sqrMagnitude;

            if (lengthSq <= 0.0001f)
                continue;

            float t =
                Vector2.Dot(
                    target - a,
                    edge) /
                lengthSq;

            float padding =
                Mathf.Clamp(
                    edgeCornerPadding,
                    0.05f,
                    0.42f);

            t = Mathf.Clamp(
                t,
                padding,
                1f - padding);

            Vector2 candidate =
                Vector2.Lerp(a, b, t);

            float distanceSq =
                (target - candidate).sqrMagnitude;

            if (distanceSq >= bestDistance)
                continue;

            bestDistance = distanceSq;
            basePoint = candidate;
        }
    }

    private void SetVisible(bool visible)
    {
        if (ribbonRect != null)
            ribbonRect.gameObject.SetActive(visible);
    }

    private static Sprite ResolveRibbonSprite()
    {
        if (cachedRibbonSprite != null)
            return cachedRibbonSprite;

        cachedRibbonTexture = new Texture2D(
            TextureWidth,
            TextureHeight,
            TextureFormat.RGBA32,
            false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
            name = "BattleSpeechTailRibbonRuntimeTexture"
        };

        Color32 transparent = new(0, 0, 0, 0);
        Color32 white = new(248, 248, 242, 255);
        Color32 black = new(5, 5, 8, 255);

        Color32[] pixels =
            new Color32[TextureWidth * TextureHeight];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = transparent;

        Vector2[] outer =
        {
            // Bubble-side connection: broad body with a comic-style notch.
            new Vector2(0.00f, 0.34f),
            new Vector2(0.12f, 0.39f),
            new Vector2(0.085f, 0.20f),
            new Vector2(0.27f, 0.39f),

            // Main ribbon body.
            new Vector2(0.72f, 0.36f),
            new Vector2(0.86f, 0.40f),

            // Short pointed speaker-facing tip.
            new Vector2(1.00f, 0.50f),

            // Bottom edge intentionally differs from the top edge for a hand-cut look.
            new Vector2(0.86f, 0.62f),
            new Vector2(0.68f, 0.66f),
            new Vector2(0.27f, 0.63f),

            // Matching lower notch.
            new Vector2(0.085f, 0.82f),
            new Vector2(0.12f, 0.61f),
            new Vector2(0.00f, 0.66f)
        };

        Vector2 center =
            ComputePolygonCenter(outer);

        // Outer solid black silhouette.
        RasterizePolygon(
            pixels,
            TextureWidth,
            TextureHeight,
            outer,
            black);

        // Inset the same silhouette toward its center to create a clean outline.
        Vector2[] inner =
            new Vector2[outer.Length];

        const float insetScale = 0.905f;
        for (int i = 0; i < outer.Length; i++)
        {
            inner[i] =
                center +
                (outer[i] - center) *
                insetScale;
        }

        RasterizePolygon(
            pixels,
            TextureWidth,
            TextureHeight,
            inner,
            white);

        cachedRibbonTexture.SetPixels32(pixels);
        cachedRibbonTexture.Apply(false, true);

        cachedRibbonSprite = Sprite.Create(
            cachedRibbonTexture,
            new Rect(
                0f,
                0f,
                TextureWidth,
                TextureHeight),
            new Vector2(0f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);

        cachedRibbonSprite.name =
            "BattleSpeechTailRibbonRuntimeSprite";

        cachedRibbonSprite.hideFlags =
            HideFlags.HideAndDontSave;

        return cachedRibbonSprite;
    }

    private static Vector2 ComputePolygonCenter(
        Vector2[] polygon)
    {
        Vector2 center = Vector2.zero;

        for (int i = 0; i < polygon.Length; i++)
            center += polygon[i];

        return center /
               Mathf.Max(1, polygon.Length);
    }

    private static void RasterizePolygon(
        Color32[] pixels,
        int width,
        int height,
        Vector2[] polygon,
        Color32 color)
    {
        for (int y = 0; y < height; y++)
        {
            float py =
                (y + 0.5f) /
                height;

            for (int x = 0; x < width; x++)
            {
                float px =
                    (x + 0.5f) /
                    width;

                if (!PointInPolygon(
                        new Vector2(px, py),
                        polygon))
                {
                    continue;
                }

                pixels[y * width + x] =
                    color;
            }
        }
    }

    private static bool PointInPolygon(
        Vector2 point,
        Vector2[] polygon)
    {
        bool inside = false;

        int j = polygon.Length - 1;

        for (int i = 0;
             i < polygon.Length;
             i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];

            bool intersects =
                ((a.y > point.y) !=
                 (b.y > point.y)) &&
                (point.x <
                 (b.x - a.x) *
                 (point.y - a.y) /
                 (b.y - a.y) +
                 a.x);

            if (intersects)
                inside = !inside;

            j = i;
        }

        return inside;
    }
}

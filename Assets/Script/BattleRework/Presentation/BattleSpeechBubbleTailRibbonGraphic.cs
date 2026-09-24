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
    [SerializeField, Min(24f)] private float ribbonHeight = 74f;
    [SerializeField, Range(0f, 18f)] private float bubbleOverlap = 8f;
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
        Vector2 start =
            basePoint -
            forward * Mathf.Max(0f, bubbleOverlap);

        Vector2 end = target;
        Vector2 line = end - start;

        float length = line.magnitude;
        if (length <= 1f)
        {
            SetVisible(false);
            return;
        }

        float angle =
            Mathf.Atan2(line.y, line.x) *
            Mathf.Rad2Deg;

        ribbonRect.anchoredPosition = start;
        ribbonRect.localRotation =
            Quaternion.Euler(0f, 0f, angle);

        ribbonRect.sizeDelta =
            new Vector2(
                length,
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
            // Bubble-side notch / fork.
            new Vector2(0.00f, 0.50f),
            new Vector2(0.075f, 0.78f),
            new Vector2(0.135f, 0.68f),

            // Long asymmetric upper body.
            new Vector2(0.22f, 0.76f),
            new Vector2(0.70f, 0.70f),
            new Vector2(0.86f, 0.66f),

            // Speaker-side pointed tip.
            new Vector2(1.00f, 0.50f),

            // Lower body comes back with a different angle.
            new Vector2(0.88f, 0.35f),
            new Vector2(0.66f, 0.29f),
            new Vector2(0.20f, 0.31f),

            // Lower notch.
            new Vector2(0.135f, 0.42f),
            new Vector2(0.075f, 0.32f)
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

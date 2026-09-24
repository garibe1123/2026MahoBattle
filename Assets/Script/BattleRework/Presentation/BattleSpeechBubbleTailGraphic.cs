using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 말풍선 Edge와 캐릭터 Target Pivot 사이를 Unity 기본 Image 조각으로 연결합니다.
///
/// 이전 Custom Graphic Mesh 방식은 중첩 Canvas/회전/스케일 조합에서 렌더링이
/// 누락될 수 있어 사용하지 않습니다. 이 버전은 4개의 실제 UI Image 세그먼트를
/// 사용하므로 전투/Reward/Map 어디서든 동일하게 확실히 렌더링됩니다.
///
/// 각 세그먼트는 검은 Outer Image + 흰 Inner Image 두 겹이며,
/// 중간 중심점을 좌우로 꺾어서 레퍼런스의 각진 말풍선 꼬리 느낌을 만듭니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubbleTailGraphic : MonoBehaviour
{
    private const int SegmentCount = 4;

    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;

    [Header("Tail Shape")]
    [SerializeField, Min(16f)] private float baseWidth = 68f;
    [SerializeField, Min(1f)] private float outlineWidth = 8f;
    [SerializeField, Min(0f)] private float jagDepth = 22f;
    [SerializeField, Range(0.05f, 0.42f)] private float edgeCornerPadding = 0.14f;
    [SerializeField, Min(0f)] private float segmentOverlap = 9f;

    [SerializeField] private Color fillColor =
        new(0.97f, 0.97f, 0.94f, 1f);

    [SerializeField] private Color outlineColor =
        new(0.015f, 0.015f, 0.02f, 1f);

    private readonly Vector3[] worldCorners = new Vector3[4];
    private readonly RectTransform[] outerRects = new RectTransform[SegmentCount];
    private readonly RectTransform[] innerRects = new RectTransform[SegmentCount];
    private readonly Image[] outerImages = new Image[SegmentCount];
    private readonly Image[] innerImages = new Image[SegmentCount];

    private bool visualsBuilt;

    public void Configure(
        RectTransform bubble,
        RectTransform target,
        Color fill,
        Color outline,
        float width = 68f,
        float outlineThickness = 8f)
    {
        bubbleRect = bubble;
        targetPivot = target;
        fillColor = fill;
        outlineColor = outline;
        baseWidth = Mathf.Max(16f, width);
        outlineWidth = Mathf.Max(1f, outlineThickness);

        EnsureVisuals();
        ApplyColors();
        RebuildTail();
    }

    public void SetTarget(RectTransform target)
    {
        targetPivot = target;
        RebuildTail();
    }

    private void Awake()
    {
        EnsureVisuals();
    }

    private void OnEnable()
    {
        EnsureVisuals();
        RebuildTail();
    }

    private void LateUpdate()
    {
        RebuildTail();
    }

    private void EnsureVisuals()
    {
        if (visualsBuilt)
            return;

        visualsBuilt = true;

        for (int i = 0; i < SegmentCount; i++)
        {
            GameObject outerObject =
                new($"TailOuter_{i + 1}", typeof(RectTransform));

            outerObject.transform.SetParent(transform, false);
            RectTransform outer = outerObject.GetComponent<RectTransform>();
            outer.anchorMin = outer.anchorMax = new Vector2(0.5f, 0.5f);
            outer.pivot = new Vector2(0.5f, 0.5f);

            Image outerImage = outerObject.AddComponent<Image>();
            outerImage.sprite = BattleHudSpriteCache.DefaultSprite;
            outerImage.type = Image.Type.Simple;
            outerImage.raycastTarget = false;

            GameObject innerObject =
                new($"TailInner_{i + 1}", typeof(RectTransform));

            innerObject.transform.SetParent(outer, false);
            RectTransform inner = innerObject.GetComponent<RectTransform>();
            inner.anchorMin = inner.anchorMax = new Vector2(0.5f, 0.5f);
            inner.pivot = new Vector2(0.5f, 0.5f);
            inner.anchoredPosition = Vector2.zero;

            Image innerImage = innerObject.AddComponent<Image>();
            innerImage.sprite = BattleHudSpriteCache.DefaultSprite;
            innerImage.type = Image.Type.Simple;
            innerImage.raycastTarget = false;

            outerRects[i] = outer;
            innerRects[i] = inner;
            outerImages[i] = outerImage;
            innerImages[i] = innerImage;
        }

        ApplyColors();
    }

    private void ApplyColors()
    {
        for (int i = 0; i < SegmentCount; i++)
        {
            if (outerImages[i] != null)
                outerImages[i].color = outlineColor;

            if (innerImages[i] != null)
                innerImages[i].color = fillColor;
        }
    }

    private void RebuildTail()
    {
        if (!visualsBuilt ||
            bubbleRect == null ||
            targetPivot == null ||
            transform is not RectTransform root)
        {
            SetVisible(false);
            return;
        }

        bubbleRect.GetWorldCorners(worldCorners);

        Vector2[] corners = new Vector2[4];
        for (int i = 0; i < 4; i++)
            corners[i] = root.InverseTransformPoint(worldCorners[i]);

        Vector2 target =
            root.InverseTransformPoint(targetPivot.position);

        ResolveBestEdge(
            corners,
            target,
            out Vector2 baseCenter,
            out Vector2 edgeDirection,
            out float edgeLength);

        Vector2 delta = target - baseCenter;
        float distance = delta.magnitude;

        if (edgeLength <= 0.001f || distance <= 8f)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        Vector2 side = edgeDirection.normalized;
        float jag = Mathf.Min(
            Mathf.Max(0f, jagDepth),
            distance * 0.20f);

        // First point is pulled a few pixels into the bubble so the tail visibly
        // joins the white face instead of looking detached.
        Vector2 forward = delta / distance;
        Vector2 p0 = baseCenter - forward * 5f;
        Vector2 p1 = Vector2.Lerp(baseCenter, target, 0.30f) - side * jag;
        Vector2 p2 = Vector2.Lerp(baseCenter, target, 0.56f) + side * jag * 0.72f;
        Vector2 p3 = Vector2.Lerp(baseCenter, target, 0.79f) - side * jag * 0.34f;
        Vector2 p4 = target;

        Vector2[] points = { p0, p1, p2, p3, p4 };

        float maxWidth =
            Mathf.Max(
                18f,
                edgeLength *
                (0.5f - Mathf.Clamp(edgeCornerPadding, 0.05f, 0.42f)) *
                0.75f);

        float startWidth = Mathf.Min(baseWidth, maxWidth);

        float[] widthScale =
        {
            1.00f,
            0.74f,
            0.50f,
            0.27f
        };

        for (int i = 0; i < SegmentCount; i++)
        {
            float width =
                Mathf.Max(8f, startWidth * widthScale[i]);

            LayoutSegment(
                outerRects[i],
                innerRects[i],
                points[i],
                points[i + 1],
                width,
                outlineWidth,
                segmentOverlap);
        }
    }

    private void ResolveBestEdge(
        Vector2[] corners,
        Vector2 target,
        out Vector2 baseCenter,
        out Vector2 edgeDirection,
        out float edgeLength)
    {
        float bestDistance = float.PositiveInfinity;
        baseCenter = Vector2.zero;
        edgeDirection = Vector2.right;
        edgeLength = 0f;

        for (int i = 0; i < 4; i++)
        {
            Vector2 a = corners[i];
            Vector2 b = corners[(i + 1) % 4];
            Vector2 edge = b - a;
            float lengthSq = edge.sqrMagnitude;

            if (lengthSq <= 0.0001f)
                continue;

            float t =
                Vector2.Dot(target - a, edge) /
                lengthSq;

            float padding =
                Mathf.Clamp(edgeCornerPadding, 0.05f, 0.42f);

            t = Mathf.Clamp(t, padding, 1f - padding);

            Vector2 point = Vector2.Lerp(a, b, t);
            float sqrDistance = (target - point).sqrMagnitude;

            if (sqrDistance >= bestDistance)
                continue;

            bestDistance = sqrDistance;
            baseCenter = point;
            edgeLength = Mathf.Sqrt(lengthSq);
            edgeDirection = edge / edgeLength;
        }
    }

    private static void LayoutSegment(
        RectTransform outer,
        RectTransform inner,
        Vector2 from,
        Vector2 to,
        float outerWidth,
        float outline,
        float overlap)
    {
        if (outer == null || inner == null)
            return;

        Vector2 delta = to - from;
        float length = delta.magnitude;

        if (length <= 0.1f)
        {
            outer.gameObject.SetActive(false);
            return;
        }

        outer.gameObject.SetActive(true);

        Vector2 direction = delta / length;
        Vector2 midpoint = (from + to) * 0.5f;

        float angle =
            Mathf.Atan2(direction.y, direction.x) *
            Mathf.Rad2Deg;

        outer.anchoredPosition = midpoint;
        outer.localRotation = Quaternion.Euler(0f, 0f, angle);
        outer.sizeDelta =
            new Vector2(
                length + Mathf.Max(0f, overlap),
                Mathf.Max(4f, outerWidth));

        float innerWidth =
            Mathf.Max(2f, outerWidth - outline * 2f);

        inner.sizeDelta =
            new Vector2(
                Mathf.Max(2f, length + Mathf.Max(0f, overlap) - outline * 0.75f),
                innerWidth);

        inner.anchoredPosition = Vector2.zero;
        inner.localRotation = Quaternion.identity;
    }

    private void SetVisible(bool visible)
    {
        for (int i = 0; i < SegmentCount; i++)
        {
            if (outerRects[i] != null)
                outerRects[i].gameObject.SetActive(visible);
        }
    }
}

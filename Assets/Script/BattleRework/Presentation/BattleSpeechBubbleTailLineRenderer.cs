using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ScreenSpaceOverlay UI에서 확실히 보이는 point-driven speech tail renderer.
///
/// P0 = 말풍선 가장자리
/// P1/P2/P3 = 각진 중간 control point
/// P4 = 사회자 Target Pivot
///
/// 커스텀 Mesh/OnPopulateMesh를 사용하지 않고 Unity 기본 Image 선분 4개를 사용합니다.
/// 각 선분은 검은 Outer Line + 흰 Inner Line을 겹쳐 그려 하나의 연속 라인처럼 보입니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubbleTailLineRenderer : MonoBehaviour
{
    private const int PointCount = 5;
    private const int SegmentCount = PointCount - 1;

    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;

    [Header("Line")]
    [SerializeField, Min(2f)] private float outerLineWidth = 12f;
    [SerializeField, Min(1f)] private float innerLineWidth = 5f;
    [SerializeField, Min(0f)] private float segmentOverlap = 7f;
    [SerializeField, Range(0f, 12f)] private float bubbleOverlap = 5f;

    [Header("Control Points")]
    [SerializeField, Min(0f)] private float jagDepth = 34f;
    [SerializeField, Range(0.12f, 0.38f)] private float firstKinkRatio = 0.27f;
    [SerializeField, Range(0.38f, 0.64f)] private float secondKinkRatio = 0.52f;
    [SerializeField, Range(0.64f, 0.88f)] private float thirdKinkRatio = 0.76f;
    [SerializeField, Range(0.05f, 0.42f)] private float edgeCornerPadding = 0.14f;

    [SerializeField] private Color innerColor =
        new(0.97f, 0.97f, 0.94f, 1f);

    [SerializeField] private Color outerColor =
        new(0.015f, 0.015f, 0.02f, 1f);

    private readonly Vector3[] bubbleWorldCorners = new Vector3[4];
    private readonly Vector2[] points = new Vector2[PointCount];

    private readonly RectTransform[] outerLines =
        new RectTransform[SegmentCount];

    private readonly RectTransform[] innerLines =
        new RectTransform[SegmentCount];

    private readonly Image[] outerImages =
        new Image[SegmentCount];

    private readonly Image[] innerImages =
        new Image[SegmentCount];

    private bool visualsBuilt;

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

        innerColor = fill;
        outerColor = outline;

        // 기존 Configure signature를 유지하되, width는 이제 "굵은 꼬리 폭"이 아니라
        // line 스타일의 가독성에 맞는 제한된 stroke 값으로 변환합니다.
        outerLineWidth = Mathf.Clamp(
            Mathf.Max(outlineThickness + 4f, width * 0.16f),
            8f,
            18f);

        innerLineWidth = Mathf.Clamp(
            outerLineWidth - Mathf.Max(3f, outlineThickness * 0.65f),
            3f,
            outerLineWidth - 2f);

        EnsureVisuals();
        ApplyColors();
        RebuildLine();
    }

    public void SetTarget(RectTransform target)
    {
        targetPivot = target;
        RebuildLine();
    }

    private void Awake()
    {
        EnsureVisuals();
    }

    private void OnEnable()
    {
        EnsureVisuals();
        RebuildLine();
    }

    private void LateUpdate()
    {
        RebuildLine();
    }

    private void EnsureVisuals()
    {
        if (visualsBuilt)
            return;

        visualsBuilt = true;

        for (int i = 0; i < SegmentCount; i++)
        {
            GameObject outerObject =
                new($"LineOuter_{i + 1}", typeof(RectTransform));

            outerObject.transform.SetParent(transform, false);

            RectTransform outer =
                outerObject.GetComponent<RectTransform>();

            outer.anchorMin =
                outer.anchorMax =
                    new Vector2(0.5f, 0.5f);

            outer.pivot =
                new Vector2(0.5f, 0.5f);

            Image outerImage =
                outerObject.AddComponent<Image>();

            outerImage.sprite =
                BattleHudSpriteCache.DefaultSprite;

            outerImage.type = Image.Type.Simple;
            outerImage.raycastTarget = false;

            GameObject innerObject =
                new($"LineInner_{i + 1}", typeof(RectTransform));

            innerObject.transform.SetParent(outer, false);

            RectTransform inner =
                innerObject.GetComponent<RectTransform>();

            inner.anchorMin =
                inner.anchorMax =
                    new Vector2(0.5f, 0.5f);

            inner.pivot =
                new Vector2(0.5f, 0.5f);

            inner.anchoredPosition = Vector2.zero;

            Image innerImage =
                innerObject.AddComponent<Image>();

            innerImage.sprite =
                BattleHudSpriteCache.DefaultSprite;

            innerImage.type = Image.Type.Simple;
            innerImage.raycastTarget = false;

            outerLines[i] = outer;
            innerLines[i] = inner;
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
                outerImages[i].color = outerColor;

            if (innerImages[i] != null)
                innerImages[i].color = innerColor;
        }
    }

    private void RebuildLine()
    {
        RectTransform root =
            transform as RectTransform;

        if (!visualsBuilt ||
            root == null ||
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

        Vector2 delta =
            target - basePoint;

        float distance =
            delta.magnitude;

        if (distance <= 10f)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        Vector2 forward =
            delta / distance;

        Vector2 side =
            new Vector2(
                -forward.y,
                forward.x);

        float jag =
            Mathf.Min(
                Mathf.Max(0f, jagDepth),
                distance * 0.17f);

        points[0] =
            basePoint -
            forward * Mathf.Max(0f, bubbleOverlap);

        points[1] =
            Vector2.Lerp(
                basePoint,
                target,
                firstKinkRatio) -
            side * jag;

        points[2] =
            Vector2.Lerp(
                basePoint,
                target,
                secondKinkRatio) +
            side * jag * 0.78f;

        points[3] =
            Vector2.Lerp(
                basePoint,
                target,
                thirdKinkRatio) -
            side * jag * 0.34f;

        points[4] =
            target;

        for (int i = 0; i < SegmentCount; i++)
        {
            LayoutLine(
                outerLines[i],
                innerLines[i],
                points[i],
                points[i + 1]);
        }
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

            Vector2 edge =
                b - a;

            float lengthSq =
                edge.sqrMagnitude;

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

            t =
                Mathf.Clamp(
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

    private void LayoutLine(
        RectTransform outer,
        RectTransform inner,
        Vector2 from,
        Vector2 to)
    {
        if (outer == null ||
            inner == null)
        {
            return;
        }

        Vector2 delta =
            to - from;

        float length =
            delta.magnitude;

        if (length <= 0.5f)
        {
            outer.gameObject.SetActive(false);
            return;
        }

        outer.gameObject.SetActive(true);

        Vector2 midpoint =
            (from + to) * 0.5f;

        float angle =
            Mathf.Atan2(
                delta.y,
                delta.x) *
            Mathf.Rad2Deg;

        outer.anchoredPosition =
            midpoint;

        outer.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                angle);

        outer.sizeDelta =
            new Vector2(
                length + segmentOverlap,
                outerLineWidth);

        inner.anchoredPosition =
            Vector2.zero;

        inner.localRotation =
            Quaternion.identity;

        inner.sizeDelta =
            new Vector2(
                Mathf.Max(
                    2f,
                    length +
                    segmentOverlap -
                    1.5f),
                innerLineWidth);
    }

    private void SetVisible(bool visible)
    {
        for (int i = 0;
             i < SegmentCount;
             i++)
        {
            if (outerLines[i] != null)
            {
                outerLines[i]
                    .gameObject
                    .SetActive(visible);
            }
        }
    }
}

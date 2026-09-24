using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen Space UI 전용 point-driven speech bubble tail renderer.
///
/// UnityEngine.LineRenderer는 ScreenSpaceOverlay Canvas와 sorting order를 공유할 수 없으므로,
/// 동일한 개념(여러 control point를 하나의 연속 line으로 잇는 방식)을 UI Graphic으로 구현합니다.
///
/// P0 = 말풍선 가장자리
/// P1/P2/P3 = 각진 중간 꺾임점
/// P4 = 사회자 쪽 Target Pivot
///
/// 검은 Outer Stroke와 흰 Inner Stroke를 한 Mesh 안에 연속 Strip으로 그려
/// 기존 Image 조각 방식처럼 마디가 끊겨 보이지 않습니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubbleTailLineRenderer : MaskableGraphic
{
    private const int PointCount = 5;

    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;

    [Header("Tail Shape")]
    [SerializeField, Min(16f)] private float baseWidth = 72f;
    [SerializeField, Min(1f)] private float outlineWidth = 8f;
    [SerializeField, Min(0f)] private float jagDepth = 24f;
    [SerializeField, Range(0.05f, 0.42f)] private float edgeCornerPadding = 0.14f;
    [SerializeField, Range(0f, 16f)] private float bubbleOverlap = 5f;

    [Header("Tail Points")]
    [SerializeField, Range(0.10f, 0.40f)] private float firstKinkRatio = 0.26f;
    [SerializeField, Range(0.35f, 0.65f)] private float secondKinkRatio = 0.52f;
    [SerializeField, Range(0.60f, 0.88f)] private float thirdKinkRatio = 0.76f;

    [SerializeField] private Color fillColor =
        new(0.97f, 0.97f, 0.94f, 1f);

    [SerializeField] private Color outlineColor =
        new(0.015f, 0.015f, 0.02f, 1f);

    private readonly Vector3[] bubbleWorldCorners = new Vector3[4];
    private readonly Vector2[] points = new Vector2[PointCount];
    private readonly float[] widthScale =
    {
        1.00f,
        0.80f,
        0.61f,
        0.38f,
        0.06f
    };

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

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
        fillColor = fill;
        outlineColor = outline;
        baseWidth = Mathf.Max(16f, width);
        outlineWidth = Mathf.Max(1f, outlineThickness);
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void SetTarget(RectTransform target)
    {
        targetPivot = target;
        SetVerticesDirty();
    }

    private void LateUpdate()
    {
        if (bubbleRect != null && targetPivot != null)
            SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        RectTransform root = rectTransform;
        if (root == null || bubbleRect == null || targetPivot == null)
            return;

        bubbleRect.GetWorldCorners(bubbleWorldCorners);

        Vector2[] corners = new Vector2[4];
        for (int i = 0; i < 4; i++)
            corners[i] = root.InverseTransformPoint(bubbleWorldCorners[i]);

        Vector2 target =
            root.InverseTransformPoint(targetPivot.position);

        ResolveBestEdge(
            corners,
            target,
            out Vector2 baseCenter,
            out float edgeLength);

        Vector2 toTarget = target - baseCenter;
        float distance = toTarget.magnitude;

        if (edgeLength <= 0.001f || distance <= 8f)
            return;

        Vector2 forward = toTarget / distance;
        Vector2 side = new(-forward.y, forward.x);

        float jag =
            Mathf.Min(
                Mathf.Max(0f, jagDepth),
                distance * 0.18f);

        // Start a few pixels inside the bubble so the tail visually grows from the body.
        points[0] =
            baseCenter -
            forward * Mathf.Max(0f, bubbleOverlap);

        points[1] =
            Vector2.Lerp(baseCenter, target, firstKinkRatio) -
            side * jag;

        points[2] =
            Vector2.Lerp(baseCenter, target, secondKinkRatio) +
            side * jag * 0.72f;

        points[3] =
            Vector2.Lerp(baseCenter, target, thirdKinkRatio) -
            side * jag * 0.30f;

        points[4] = target;

        float edgeLimitedWidth =
            Mathf.Max(
                20f,
                edgeLength *
                (0.5f - Mathf.Clamp(edgeCornerPadding, 0.05f, 0.42f)) *
                0.80f);

        float resolvedBaseWidth =
            Mathf.Min(baseWidth, edgeLimitedWidth);

        AddStroke(
            vh,
            points,
            resolvedBaseWidth + outlineWidth * 2f,
            outlineColor);

        AddStroke(
            vh,
            points,
            resolvedBaseWidth,
            fillColor);
    }

    private void ResolveBestEdge(
        Vector2[] corners,
        Vector2 target,
        out Vector2 baseCenter,
        out float edgeLength)
    {
        float bestDistance = float.PositiveInfinity;
        baseCenter = Vector2.zero;
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

            Vector2 candidate = Vector2.Lerp(a, b, t);
            float distanceSq = (target - candidate).sqrMagnitude;

            if (distanceSq >= bestDistance)
                continue;

            bestDistance = distanceSq;
            baseCenter = candidate;
            edgeLength = Mathf.Sqrt(lengthSq);
        }
    }

    private void AddStroke(
        VertexHelper vh,
        Vector2[] sourcePoints,
        float strokeBaseWidth,
        Color strokeColor)
    {
        int startVertex = vh.currentVertCount;

        for (int i = 0; i < PointCount; i++)
        {
            Vector2 tangent = ResolveTangent(sourcePoints, i);
            if (tangent.sqrMagnitude <= 0.0001f)
                tangent = Vector2.right;

            tangent.Normalize();

            Vector2 normal =
                new(-tangent.y, tangent.x);

            float halfWidth =
                Mathf.Max(
                    i == PointCount - 1 ? 0.75f : 1.5f,
                    strokeBaseWidth *
                    widthScale[i] *
                    0.5f);

            Vector2 left =
                sourcePoints[i] -
                normal * halfWidth;

            Vector2 right =
                sourcePoints[i] +
                normal * halfWidth;

            AddVertex(vh, left, strokeColor);
            AddVertex(vh, right, strokeColor);
        }

        for (int i = 0; i < PointCount - 1; i++)
        {
            int a = startVertex + i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;

            vh.AddTriangle(a, b, c);
            vh.AddTriangle(b, d, c);
        }
    }

    private static Vector2 ResolveTangent(
        Vector2[] sourcePoints,
        int index)
    {
        if (index <= 0)
            return sourcePoints[1] - sourcePoints[0];

        if (index >= PointCount - 1)
        {
            return
                sourcePoints[PointCount - 1] -
                sourcePoints[PointCount - 2];
        }

        Vector2 previous =
            (sourcePoints[index] -
             sourcePoints[index - 1]).normalized;

        Vector2 next =
            (sourcePoints[index + 1] -
             sourcePoints[index]).normalized;

        Vector2 combined = previous + next;

        if (combined.sqrMagnitude <= 0.0001f)
            return next;

        return combined.normalized;
    }

    private static void AddVertex(
        VertexHelper vh,
        Vector2 position,
        Color color)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        vh.AddVert(vertex);
    }
}

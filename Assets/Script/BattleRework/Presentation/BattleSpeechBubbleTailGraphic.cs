using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 말풍선 Edge와 캐릭터 쪽 Target Pivot을 꺾인 리본형 Mesh로 연결합니다.
///
/// 레퍼런스처럼 곧은 삼각형이 아니라 중간이 한두 번 꺾이는 Jagged Tail을 만들며,
/// Bubble/Target이 이동·회전·스케일되어도 매 프레임 자동으로 다시 계산합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubbleTailGraphic : MaskableGraphic
{
    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;

    [Header("Tail Shape")]
    [SerializeField, Min(12f)] private float baseWidth = 56f;
    [SerializeField, Min(0f)] private float outlineWidth = 7f;
    [SerializeField, Min(0f)] private float jagDepth = 18f;
    [SerializeField, Range(0.05f, 0.45f)] private float edgeCornerPadding = 0.14f;

    [SerializeField] private Color fillColor = new(0.97f, 0.97f, 0.94f, 1f);
    [SerializeField] private Color outlineColor = new(0.015f, 0.015f, 0.02f, 1f);

    private readonly Vector3[] worldCorners = new Vector3[4];

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
        float width = 56f,
        float outlineThickness = 7f)
    {
        bubbleRect = bubble;
        targetPivot = target;
        fillColor = fill;
        outlineColor = outline;
        baseWidth = Mathf.Max(12f, width);
        outlineWidth = Mathf.Max(0f, outlineThickness);
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

        if (bubbleRect == null || targetPivot == null || rectTransform == null)
            return;

        bubbleRect.GetWorldCorners(worldCorners);

        Vector2[] corners = new Vector2[4];
        for (int i = 0; i < 4; i++)
            corners[i] = rectTransform.InverseTransformPoint(worldCorners[i]);

        Vector2 target =
            rectTransform.InverseTransformPoint(targetPivot.position);

        ResolveBestEdge(
            corners,
            target,
            out Vector2 baseCenter,
            out Vector2 edgeDirection,
            out float edgeLength);

        if (edgeLength <= 0.001f)
            return;

        Vector2 toTarget = target - baseCenter;
        float distance = toTarget.magnitude;
        if (distance <= 1f)
            return;

        Vector2 forward = toTarget / distance;
        Vector2 side = edgeDirection.normalized;

        float maxHalfWidth =
            edgeLength *
            (0.5f - Mathf.Clamp(edgeCornerPadding, 0.05f, 0.45f));

        float outerBaseWidth =
            Mathf.Min(baseWidth, Mathf.Max(12f, maxHalfWidth * 2f));

        // Outer black ink ribbon.
        AddJaggedRibbon(
            vh,
            baseCenter,
            target,
            side,
            forward,
            outerBaseWidth,
            Mathf.Max(0f, jagDepth),
            outlineColor);

        // Inner white ribbon leaves a black outline all the way to the point.
        float innerInset = Mathf.Max(1f, outlineWidth);
        float innerBaseWidth =
            Mathf.Max(8f, outerBaseWidth - innerInset * 2f);

        Vector2 innerStart =
            baseCenter + forward * (innerInset * 0.35f);

        Vector2 innerEnd =
            target - forward * Mathf.Min(
                innerInset * 1.15f,
                distance * 0.16f);

        AddJaggedRibbon(
            vh,
            innerStart,
            innerEnd,
            side,
            forward,
            innerBaseWidth,
            Mathf.Max(0f, jagDepth - innerInset * 0.55f),
            fillColor);
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
                Mathf.Clamp(edgeCornerPadding, 0.05f, 0.45f);

            t = Mathf.Clamp(t, padding, 1f - padding);

            Vector2 point = Vector2.Lerp(a, b, t);
            float distanceSq = (target - point).sqrMagnitude;

            if (distanceSq >= bestDistance)
                continue;

            bestDistance = distanceSq;
            baseCenter = point;
            edgeLength = Mathf.Sqrt(lengthSq);
            edgeDirection = edge / edgeLength;
        }
    }

    private static void AddJaggedRibbon(
        VertexHelper vh,
        Vector2 start,
        Vector2 end,
        Vector2 side,
        Vector2 forward,
        float startWidth,
        float jag,
        Color color)
    {
        const int SectionCount = 6;

        float[] t =
        {
            0f,
            0.20f,
            0.42f,
            0.64f,
            0.82f,
            1f
        };

        float[] widthScale =
        {
            1f,
            0.70f,
            0.60f,
            0.43f,
            0.28f,
            0.035f
        };

        // Alternating lateral offsets make the tail kink like the supplied comic reference.
        float[] lateral =
        {
            0f,
            -0.58f,
            0.42f,
            -0.30f,
            0.14f,
            0f
        };

        Vector2[] left = new Vector2[SectionCount];
        Vector2[] right = new Vector2[SectionCount];

        for (int i = 0; i < SectionCount; i++)
        {
            Vector2 center =
                Vector2.Lerp(start, end, t[i]) +
                side * (jag * lateral[i]);

            float half =
                Mathf.Max(
                    i == SectionCount - 1 ? 0.75f : 2f,
                    startWidth * widthScale[i] * 0.5f);

            // Small forward bite at the two middle joints gives the silhouette
            // a sharper arrow/comic cut rather than a smooth ribbon.
            if (i == 2)
                center -= forward * Mathf.Min(5f, jag * 0.28f);
            else if (i == 3)
                center += forward * Mathf.Min(4f, jag * 0.20f);

            left[i] = center - side * half;
            right[i] = center + side * half;
        }

        int startVertex = vh.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;

        for (int i = 0; i < SectionCount; i++)
        {
            vertex.position = left[i];
            vh.AddVert(vertex);

            vertex.position = right[i];
            vh.AddVert(vertex);
        }

        for (int i = 0; i < SectionCount - 1; i++)
        {
            int a = startVertex + i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;

            vh.AddTriangle(a, b, c);
            vh.AddTriangle(b, d, c);
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 말풍선의 가장자리와 별도 Target Pivot을 실제 삼각형 Mesh로 연결합니다.
///
/// - Bubble Rect가 회전/스케일되어도 World Corner를 다시 계산합니다.
/// - Target Pivot이 캐릭터 모션을 따라 움직이면 꼬리 끝도 자동으로 따라갑니다.
/// - Base는 Target에 가장 가까운 Bubble Edge 위에서 자동으로 선택됩니다.
/// - 바깥 Outline Triangle + 안쪽 Fill Triangle 두 겹으로 말풍선 본체와 자연스럽게 이어집니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubbleTailGraphic : MaskableGraphic
{
    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;

    [SerializeField, Min(8f)] private float baseWidth = 48f;
    [SerializeField, Min(0f)] private float outlineWidth = 6f;
    [SerializeField, Range(0f, 24f)] private float tipInset = 4f;
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
        float width = 48f,
        float outlineThickness = 6f)
    {
        bubbleRect = bubble;
        targetPivot = target;
        fillColor = fill;
        outlineColor = outline;
        baseWidth = Mathf.Max(8f, width);
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
        {
            corners[i] = rectTransform.InverseTransformPoint(worldCorners[i]);
        }

        Vector2 target =
            rectTransform.InverseTransformPoint(targetPivot.position);

        int bestEdge = 0;
        float bestDistance = float.PositiveInfinity;
        Vector2 bestBaseCenter = Vector2.zero;
        Vector2 bestEdgeA = Vector2.zero;
        Vector2 bestEdgeB = Vector2.zero;

        for (int i = 0; i < 4; i++)
        {
            Vector2 a = corners[i];
            Vector2 b = corners[(i + 1) % 4];
            Vector2 edge = b - a;
            float lengthSq = edge.sqrMagnitude;
            if (lengthSq <= 0.0001f)
                continue;

            float t = Vector2.Dot(target - a, edge) / lengthSq;
            float padding = Mathf.Clamp(edgeCornerPadding, 0.05f, 0.45f);
            t = Mathf.Clamp(t, padding, 1f - padding);

            Vector2 point = Vector2.Lerp(a, b, t);
            float distance = (target - point).sqrMagnitude;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestEdge = i;
                bestBaseCenter = point;
                bestEdgeA = a;
                bestEdgeB = b;
            }
        }

        _ = bestEdge;

        Vector2 edgeVector = bestEdgeB - bestEdgeA;
        float edgeLength = edgeVector.magnitude;
        if (edgeLength <= 0.001f)
            return;

        Vector2 edgeDirection = edgeVector / edgeLength;
        float maxHalfWidth = edgeLength * (0.5f - Mathf.Clamp(edgeCornerPadding, 0.05f, 0.45f));
        float outerHalfWidth = Mathf.Min(baseWidth * 0.5f, Mathf.Max(4f, maxHalfWidth));

        Vector2 outerA = bestBaseCenter - edgeDirection * outerHalfWidth;
        Vector2 outerB = bestBaseCenter + edgeDirection * outerHalfWidth;
        Vector2 outerTip = target;

        AddTriangle(vh, outerA, outerB, outerTip, outlineColor);

        float innerHalfWidth = Mathf.Max(2f, outerHalfWidth - outlineWidth);
        Vector2 toBase = bestBaseCenter - target;
        float distanceToBase = toBase.magnitude;
        Vector2 innerTip = distanceToBase > 0.001f
            ? target + toBase.normalized * Mathf.Min(tipInset + outlineWidth, distanceToBase * 0.35f)
            : target;

        Vector2 innerA = bestBaseCenter - edgeDirection * innerHalfWidth;
        Vector2 innerB = bestBaseCenter + edgeDirection * innerHalfWidth;

        // Slightly pull the inner base toward the bubble center so the black outline
        // remains visible while the fill visually merges into the Bubble Face.
        Vector2 bubbleCenter = Vector2.zero;
        for (int i = 0; i < 4; i++)
            bubbleCenter += corners[i];
        bubbleCenter *= 0.25f;

        Vector2 inward = (bubbleCenter - bestBaseCenter).normalized;
        innerA += inward * outlineWidth * 0.45f;
        innerB += inward * outlineWidth * 0.45f;

        AddTriangle(vh, innerA, innerB, innerTip, fillColor);
    }

    private static void AddTriangle(
        VertexHelper vh,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Color vertexColor)
    {
        int start = vh.currentVertCount;

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = vertexColor;

        vertex.position = a;
        vh.AddVert(vertex);

        vertex.position = b;
        vh.AddVert(vertex);

        vertex.position = c;
        vh.AddVert(vertex);

        vh.AddTriangle(start, start + 1, start + 2);
    }
}

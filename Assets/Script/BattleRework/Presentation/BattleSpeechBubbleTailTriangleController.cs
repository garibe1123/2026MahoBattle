using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 말풍선 가장자리에 붙는 단순 삼각형 꼬리.
///
/// Target Pivot은 삼각형이 붙을 면(좌/우/상/하)과 그 면에서의 위치만 정합니다.
/// 꼬리는 코드로 직접 생성하며, 검은 외곽선 + 흰 내부 면의 두 겹 삼각형으로 구성합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubbleTailTriangleController : MonoBehaviour
{
    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;

    [Header("Triangle")]
    [Tooltip("삼각형 몸통 크기입니다. X는 기본 길이, Y는 밑변 폭입니다.")]
    [SerializeField] private Vector2 triangleSize = new(66f, 46f);

    [Tooltip("뾰족한 끝만 추가로 연장하는 길이입니다.")]
    [SerializeField, Range(0f, 48f)] private float tipExtension = 32f;

    [Tooltip("말풍선 안쪽으로 꼬리를 겹치는 깊이입니다. 접합부가 자연스럽게 숨겨집니다.")]
    [SerializeField, Range(0f, 36f)] private float overlap = 22f;

    [SerializeField, Range(0f, 80f)] private float edgePadding = 34f;

    [Header("Stroke")]
    [SerializeField, Range(1f, 12f)] private float outlineWidth = 5f;
    [SerializeField] private Color outlineColor = new(0.015f, 0.015f, 0.02f, 1f);
    [SerializeField] private Color fillColor = new(0.97f, 0.97f, 0.94f, 1f);

    private RectTransform triangleRect;
    private BattleSpeechBubbleTailTriangleGraphic triangleGraphic;

    public void Configure(
        RectTransform bubble,
        RectTransform target,
        Color color)
    {
        bubbleRect = bubble;
        targetPivot = target;
        fillColor = color;

        EnsureTriangle();
        ApplyStyle();
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
        ApplyStyle();
    }

    private void OnEnable()
    {
        EnsureTriangle();
        ApplyStyle();
        RefreshTriangle();
    }

    private void LateUpdate()
    {
        RefreshTriangle();
    }

    private Vector2 ResolvedTriangleSize =>
        new(
            Mathf.Max(8f, triangleSize.x + tipExtension),
            Mathf.Max(8f, triangleSize.y));

    private void EnsureTriangle()
    {
        if (triangleRect != null &&
            triangleGraphic != null)
        {
            return;
        }

        GameObject tail =
            new("TailTriangle", typeof(RectTransform));

        tail.transform.SetParent(transform, false);

        triangleRect =
            tail.GetComponent<RectTransform>();

        // bubble.rect의 좌표는 부모 RectTransform의 Pivot 원점을 기준으로 합니다.
        // 따라서 Tail의 Anchor도 부모 Bubble Pivot과 동일하게 맞춰야
        // rect.xMin/xMax/yMin/yMax 값을 anchoredPosition에 그대로 사용할 수 있습니다.
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
            ResolvedTriangleSize;

        triangleGraphic =
            tail.AddComponent<BattleSpeechBubbleTailTriangleGraphic>();

        triangleGraphic.raycastTarget = false;

        // 꼬리를 BubbleInk/Face 뒤에 둡니다.
        // 안쪽 overlap 영역이 말풍선 본체에 가려지므로 밑변 이음새가 보이지 않습니다.
        tail.transform.SetAsFirstSibling();
    }

    private void ApplyStyle()
    {
        if (triangleGraphic == null)
            return;

        triangleGraphic.SetStyle(
            fillColor,
            outlineColor,
            outlineWidth);
    }

    private void RefreshTriangle()
    {
        if (triangleRect == null ||
            triangleGraphic == null ||
            bubbleRect == null ||
            targetPivot == null)
        {
            if (triangleGraphic != null)
                triangleGraphic.enabled = false;

            return;
        }

        triangleGraphic.enabled = true;

        triangleRect.anchorMin =
            triangleRect.anchorMax =
                bubbleRect.pivot;

        triangleRect.sizeDelta = ResolvedTriangleSize;
        ApplyStyle();

        Vector3 bubbleCenterWorld =
            bubbleRect.TransformPoint(
                bubbleRect.rect.center);

        Vector3 localDelta3 =
            bubbleRect.InverseTransformVector(
                targetPivot.position -
                bubbleCenterWorld);

        Vector2 localDelta =
            new(localDelta3.x, localDelta3.y);

        Rect rect = bubbleRect.rect;

        // Speech tail은 항상 말풍선의 좌/우 "옆면"에만 붙입니다.
        // 현재 대화창은 화면 하단에 붙어 있어 위/아래 면을 허용하면
        // 삼각형이 화면 밖으로 빠지거나 BubbleFace 뒤에 가려질 수 있습니다.
        // Target Pivot은 좌/우 방향과 Y 위치만 결정합니다.
        bool placeRight = localDelta.x >= 0f;

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
        Vector2 size =
            ResolvedTriangleSize;

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
            size.x * 0.5f -
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

}

/// <summary>
/// 오른쪽을 향하는 삼각형 꼬리를 직접 생성합니다.
///
/// Outer Triangle = 검은 Stroke
/// Inner Triangle = 말풍선 Fill
///
/// Inner Triangle의 밑변은 Outer와 같은 X에서 시작하므로,
/// 말풍선 본체와 겹쳤을 때 검은 세로 이음선이 생기지 않습니다.
/// </summary>
public sealed class BattleSpeechBubbleTailTriangleGraphic : MaskableGraphic
{
    [SerializeField, Range(1f, 12f)] private float outlineWidth = 5f;
    [SerializeField] private Color outlineColor = new(0.015f, 0.015f, 0.02f, 1f);
    [SerializeField] private Color fillColor = new(0.97f, 0.97f, 0.94f, 1f);

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    public void SetStyle(
        Color fill,
        Color outline,
        float width)
    {
        fillColor = fill;
        outlineColor = outline;
        outlineWidth = Mathf.Max(1f, width);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(
        VertexHelper vh)
    {
        vh.Clear();

        Rect r =
            rectTransform.rect;

        float stroke =
            Mathf.Clamp(
                outlineWidth,
                1f,
                Mathf.Max(
                    1f,
                    r.height * 0.28f));

        // Outer black triangle.
        AddTriangle(
            vh,
            new Vector2(r.xMin, r.yMin),
            new Vector2(r.xMin, r.yMax),
            new Vector2(r.xMax, r.center.y),
            outlineColor);

        // Inner white triangle.
        //
        // Base X를 Outer와 동일하게 유지하여 말풍선과 겹치는 밑변에는
        // 검은 세로선이 생기지 않게 합니다.
        // Tip만 살짝 안쪽으로 들어와 양쪽 사선/끝부분에 Stroke가 남습니다.
        float innerTipX =
            r.xMax -
            stroke * 1.30f;

        float innerBottom =
            r.yMin +
            stroke;

        float innerTop =
            r.yMax -
            stroke;

        if (innerTop > innerBottom &&
            innerTipX > r.xMin)
        {
            AddTriangle(
                vh,
                new Vector2(r.xMin, innerBottom),
                new Vector2(r.xMin, innerTop),
                new Vector2(innerTipX, r.center.y),
                fillColor);
        }
    }

    private static void AddTriangle(
        VertexHelper vh,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Color vertexColor)
    {
        int start =
            vh.currentVertCount;

        UIVertex vertex =
            UIVertex.simpleVert;

        vertex.color =
            vertexColor;

        vertex.position =
            a;
        vh.AddVert(vertex);

        vertex.position =
            b;
        vh.AddVert(vertex);

        vertex.position =
            c;
        vh.AddVert(vertex);

        vh.AddTriangle(
            start,
            start + 1,
            start + 2);
    }
}

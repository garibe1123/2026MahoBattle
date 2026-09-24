using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 말풍선 가장자리에 붙는 단순한 흰 삼각형 꼬리.
///
/// Target Pivot은 삼각형이 붙을 면(좌/우/상/하)과 그 면에서의 위치만 정합니다.
/// 꼬리 길이, Sprite 프리셋, PNG, 라인, 리본은 사용하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubbleTailTriangleController : MonoBehaviour
{
    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;

    [Header("Triangle")]
    [SerializeField] private Vector2 triangleSize = new(56f, 38f);
    [SerializeField, Range(0f, 24f)] private float overlap = 9f;
    [SerializeField, Range(0f, 80f)] private float edgePadding = 34f;
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
        ApplyColor();
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
        ApplyColor();
    }

    private void OnEnable()
    {
        EnsureTriangle();
        ApplyColor();
        RefreshTriangle();
    }

    private void LateUpdate()
    {
        RefreshTriangle();
    }

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

        triangleRect.anchorMin =
            triangleRect.anchorMax =
                new Vector2(0.5f, 0.5f);

        triangleRect.pivot =
            new Vector2(0.5f, 0.5f);

        triangleRect.sizeDelta =
            triangleSize;

        triangleGraphic =
            tail.AddComponent<BattleSpeechBubbleTailTriangleGraphic>();

        triangleGraphic.raycastTarget = false;

        // BubbleFace 뒤가 아니라 위에 놓아 검은 외곽선과 겹치는 접합부를 흰색으로 덮습니다.
        tail.transform.SetAsLastSibling();
    }

    private void ApplyColor()
    {
        if (triangleGraphic != null)
            triangleGraphic.color = fillColor;
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
        triangleRect.sizeDelta = triangleSize;

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

        float halfWidth =
            Mathf.Max(1f, rect.width * 0.5f);

        float halfHeight =
            Mathf.Max(1f, rect.height * 0.5f);

        // 가로/세로 크기가 다른 말풍선에서도 올바른 면을 고르도록 정규화해서 비교합니다.
        float xWeight =
            Mathf.Abs(localDelta.x) / halfWidth;

        float yWeight =
            Mathf.Abs(localDelta.y) / halfHeight;

        if (xWeight >= yWeight)
        {
            PlaceHorizontal(
                localDelta.x >= 0f,
                localDelta.y,
                rect);
        }
        else
        {
            PlaceVertical(
                localDelta.y >= 0f,
                localDelta.x,
                rect);
        }
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

    private void PlaceVertical(
        bool up,
        float targetLocalX,
        Rect bubble)
    {
        float padding =
            Mathf.Min(
                edgePadding,
                bubble.width * 0.45f);

        float x =
            Mathf.Clamp(
                targetLocalX,
                bubble.xMin + padding,
                bubble.xMax - padding);

        float outside =
            triangleSize.x * 0.5f -
            Mathf.Max(0f, overlap);

        triangleRect.anchoredPosition =
            new Vector2(
                x,
                up
                    ? bubble.yMax + outside
                    : bubble.yMin - outside);

        triangleRect.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                up ? 90f : -90f);
    }
}

/// <summary>
/// 오른쪽을 향하는 단순 삼각형 한 개만 그립니다.
/// 자신의 작은 Rect 내부에서만 Mesh를 생성하므로 Canvas 바깥 좌표를 사용하지 않습니다.
/// </summary>
public sealed class BattleSpeechBubbleTailTriangleGraphic : MaskableGraphic
{
    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(
        VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;

        UIVertex vertex =
            UIVertex.simpleVert;

        vertex.color = color;

        vertex.position =
            new Vector3(
                r.xMin,
                r.yMin,
                0f);
        vh.AddVert(vertex);

        vertex.position =
            new Vector3(
                r.xMin,
                r.yMax,
                0f);
        vh.AddVert(vertex);

        vertex.position =
            new Vector3(
                r.xMax,
                r.center.y,
                0f);
        vh.AddVert(vertex);

        vh.AddTriangle(0, 1, 2);
    }
}

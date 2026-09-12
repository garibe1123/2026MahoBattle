using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Solid UI mesh used by the Reward PACK DONE/NEXT presentation.
/// It can draw either a tapered right-facing arrow or a simple tapered trapezoid.
/// The shape is generated from the current RectTransform, so width/height animation does not
/// stretch a source sprite and the right side can stay visibly narrower than the left.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleDoneNextArrowGraphic : MaskableGraphic
{
    [SerializeField, Range(0.12f, 1f)] private float rightHeightRatio = 0.58f;
    [SerializeField, Range(0f, 0.45f)] private float headLengthRatio = 0.22f;
    [SerializeField, Range(0.12f, 1f)] private float headBaseHeightRatio = 0.70f;
    [SerializeField] private bool drawArrowHead = true;

    public void ConfigureShape(
        bool withArrowHead,
        float taperedRightHeightRatio,
        float arrowHeadLengthRatio = 0.22f,
        float arrowHeadBaseHeightRatio = 0.70f)
    {
        drawArrowHead = withArrowHead;
        rightHeightRatio = Mathf.Clamp(taperedRightHeightRatio, 0.12f, 1f);
        headLengthRatio = Mathf.Clamp(arrowHeadLengthRatio, 0f, 0.45f);
        headBaseHeightRatio = Mathf.Clamp(arrowHeadBaseHeightRatio, 0.12f, 1f);
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0.001f || rect.height <= 0.001f)
            return;

        float centerY = rect.center.y;
        float leftHalfHeight = rect.height * 0.5f;
        float rightHalfHeight = leftHalfHeight * Mathf.Clamp(rightHeightRatio, 0.12f, 1f);
        float resolvedHeadRatio = drawArrowHead ? Mathf.Clamp(headLengthRatio, 0f, 0.45f) : 0f;
        float bodyRightX = rect.xMax - rect.width * resolvedHeadRatio;

        Vector2 leftBottom = new(rect.xMin, centerY - leftHalfHeight);
        Vector2 rightBottom = new(bodyRightX, centerY - rightHalfHeight);
        Vector2 rightTop = new(bodyRightX, centerY + rightHalfHeight);
        Vector2 leftTop = new(rect.xMin, centerY + leftHalfHeight);

        AddQuad(vh, leftBottom, rightBottom, rightTop, leftTop);

        if (!drawArrowHead || resolvedHeadRatio <= 0.001f)
            return;

        float headHalfHeight = leftHalfHeight * Mathf.Clamp(headBaseHeightRatio, 0.12f, 1f);
        Vector2 headBottom = new(bodyRightX, centerY - headHalfHeight);
        Vector2 tip = new(rect.xMax, centerY);
        Vector2 headTop = new(bodyRightX, centerY + headHalfHeight);
        AddTriangle(vh, headBottom, tip, headTop);
    }

    private void AddQuad(
        VertexHelper vh,
        Vector2 bottomLeft,
        Vector2 bottomRight,
        Vector2 topRight,
        Vector2 topLeft)
    {
        int start = vh.currentVertCount;
        AddVertex(vh, bottomLeft);
        AddVertex(vh, bottomRight);
        AddVertex(vh, topRight);
        AddVertex(vh, topLeft);
        vh.AddTriangle(start + 0, start + 1, start + 2);
        vh.AddTriangle(start + 0, start + 2, start + 3);
    }

    private void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c)
    {
        int start = vh.currentVertCount;
        AddVertex(vh, a);
        AddVertex(vh, b);
        AddVertex(vh, c);
        vh.AddTriangle(start + 0, start + 1, start + 2);
    }

    private void AddVertex(VertexHelper vh, Vector2 position)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        vertex.uv0 = new Vector2(0.5f, 0.5f);
        vh.AddVert(vertex);
    }
}

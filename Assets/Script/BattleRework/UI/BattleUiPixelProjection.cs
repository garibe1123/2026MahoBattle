using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal static class BattleUiPixelProjection
{
    internal static void Project(VertexHelper mesh, List<UIVertex> triangles,
        RectTransform rect, Camera camera, Rect viewport, float pixel)
    {
        triangles.Clear();
        mesh.GetUIVertexStream(triangles);
        if (triangles.Count / 3 > 16250) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, viewport.min, camera, out Vector2 bottomLeft) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, viewport.max, camera, out Vector2 topRight) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, new Vector2(viewport.xMin, viewport.yMax), camera, out Vector2 topLeft) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, new Vector2(viewport.xMax, viewport.yMin), camera, out Vector2 bottomRight)) return;
        Vector2 min = Vector2.Min(Vector2.Min(bottomLeft, topRight), Vector2.Min(topLeft, bottomRight));
        Vector2 max = Vector2.Max(Vector2.Max(bottomLeft, topRight), Vector2.Max(topLeft, bottomRight));
        mesh.Clear();
        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            UIVertex a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector2 pa = a.position;
            Vector2 pb = b.position;
            Vector2 pc = c.position;
            float area = Cross(pb - pa, pc - pa);
            if (Mathf.Abs(area) < 0.001f) continue;
            float left = Mathf.Max(min.x, Mathf.Floor(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x)) / pixel) * pixel);
            float bottom = Mathf.Max(min.y, Mathf.Floor(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y)) / pixel) * pixel);
            float right = Mathf.Min(max.x, Mathf.Ceil(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x)) / pixel) * pixel);
            float top = Mathf.Min(max.y, Mathf.Ceil(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y)) / pixel) * pixel);
            if (left >= right || bottom >= top) continue;
            Vector4 edges = new(Edge(pb, pc, area), Edge(pc, pa, area), Edge(pa, pb, area), 1f);
            int first = mesh.currentVertCount;
            Add(mesh, new Vector2(left, bottom), a, b, c, pa, pb, pc, area, edges, pixel);
            Add(mesh, new Vector2(left, top), a, b, c, pa, pb, pc, area, edges, pixel);
            Add(mesh, new Vector2(right, top), a, b, c, pa, pb, pc, area, edges, pixel);
            Add(mesh, new Vector2(right, bottom), a, b, c, pa, pb, pc, area, edges, pixel);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static float Edge(Vector2 a, Vector2 b, float area)
    {
        Vector2 direction = area > 0f ? b - a : a - b;
        return direction.y > 0f || (direction.y == 0f && direction.x < 0f) ? 1f : -1f;
    }

    private static void Add(VertexHelper mesh, Vector2 local, UIVertex a, UIVertex b, UIVertex c,
        Vector2 pa, Vector2 pb, Vector2 pc, float area, Vector4 edges, float pixel)
    {
        float x = Cross(pb - local, pc - local) / area;
        float y = Cross(pc - local, pa - local) / area;
        float z = 1f - x - y;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = local;
        vertex.color = (Color)a.color * x + (Color)b.color * y + (Color)c.color * z;
        vertex.uv0 = a.uv0 * x + b.uv0 * y + c.uv0 * z;
        vertex.uv1 = new Vector4(x, y, pixel, 0f);
        vertex.uv2 = edges;
        mesh.AddVert(vertex);
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class BattleUiJapanTvDrawing
{
    internal const float LargeTitleMinSize = 32f;
    internal static bool IsAlert(string name) => name.Contains("Notification") || name.Contains("Alert") ||
        name.Contains("ReactionTag") || name.Contains("Announcement");

    internal static Color Role(Transform target, BattleUiStyleSettings style)
    {
        for (Transform node = target; node != null; node = node.parent)
        {
            string name = node.name;
            if (name.Contains("Rule") || name.Contains("Roulette")) return style.japanTvGreen;
            if (name.Contains("Reward") || name.Contains("Item") || name.Contains("Inventory")) return style.japanTvPurple;
            if (name.Contains("Menu") || name.Contains("Pause") || name.Contains("Settings")) return style.japanTvPink;
        }
        return style.japanTvCyan;
    }

    internal static Color Tint(Color color, float alpha) { color.a *= alpha; return color; }

    internal static void Frame(VertexHelper vh, Rect rect, BattleUiStyleSettings style, Color role, float alpha, bool selected)
    {
        float border = Mathf.Min(style.japanTvBorder, Mathf.Min(rect.width, rect.height) * 0.1f);
        BattleUiStyleMeshEffect.Quad(vh, rect, Tint(style.japanTvInk, alpha));
        BattleUiStyleMeshEffect.Quad(vh, BattleUiStyleMeshEffect.Inset(rect, border), Tint(selected ? style.japanTvRed : role, alpha));
        BattleUiStyleMeshEffect.Quad(vh, BattleUiStyleMeshEffect.Inset(rect, border * 2f), Tint(style.background, alpha));
        Rect band = new(rect.xMin + border * 2f, rect.yMax - border * 3f, Mathf.Max(0f, rect.width - border * 4f), border);
        BattleUiStyleMeshEffect.Quad(vh, band, Tint(style.japanTvYellow, alpha));
    }

    internal static void Burst(VertexHelper vh, Rect rect, Color color, float scale)
    {
        const int points = 32;
        int first = vh.currentVertCount;
        vh.AddVert(rect.center, color, Vector2.zero);
        for (int i = 0; i < points; i++)
        {
            float angle = i * Mathf.PI * 2f / points;
            float radius = (i % 2 == 0 ? 1f : 0.69f) * scale;
            Vector2 point = rect.center + new Vector2(Mathf.Cos(angle) * rect.width, Mathf.Sin(angle) * rect.height) * (radius * 0.5f);
            vh.AddVert(point, color, Vector2.zero);
        }
        for (int i = 0; i < points; i++) vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % points);
    }

    internal static void Disc(VertexHelper vh, Vector2 center, float radius, Color color)
    {
        int first = vh.currentVertCount;
        vh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI / 6f;
            vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
        }
        for (int i = 0; i < 12; i++) vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % 12);
    }
}

[AddComponentMenu("")]
public sealed class BattleUiJapanTvTitlePlate : MaskableGraphic
{
    private BattleUiStyleSettings style;
    private bool alert, marquee, captions;
    private int bulbPhase = -1;
    private float alpha;
    private Color role;
    private Rect bounds;
    private readonly Vector3[] corners = new Vector3[4];
    private readonly System.Collections.Generic.List<Rect> boxes = new();
    private readonly VertexHelper captionGeometry = new();
    private Mesh captionMesh;

    internal void Configure(MaskableGraphic source, BattleUiStyleSettings settings, bool isAlert, bool isMarquee, bool isCaption, Color accent, float sourceAlpha)
    {
        Rect previousBounds = bounds;
        bool changed = style != settings || alert != isAlert || marquee != isMarquee || captions != isCaption || role != accent;
        style = settings;
        alert = isAlert;
        marquee = isMarquee;
        captions = isCaption;
        role = accent;
        maskable = source.maskable;
        bounds = rectTransform.rect;
        if (source.transform.parent is RectTransform parent)
        {
            parent.GetWorldCorners(corners);
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            foreach (Vector3 corner in corners)
            {
                Vector2 point = rectTransform.InverseTransformPoint(corner);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            bounds = Rect.MinMaxRect(Mathf.Max(bounds.xMin, min.x), Mathf.Max(bounds.yMin, min.y),
                Mathf.Min(bounds.xMax, max.x), Mathf.Min(bounds.yMax, max.y));
        }
        int phase = (int)(Time.unscaledTime * 2f / Mathf.Max(0.1f, style.japanTvBulbPeriod)) & 1;
        if (changed || previousBounds != bounds || alpha != sourceAlpha || (marquee && bulbPhase != phase))
        {
            alpha = sourceAlpha;
            bulbPhase = phase;
            SetVerticesDirty();
        }
        canvasRenderer.SetColor(source.canvasRenderer.GetColor());
    }

    internal void SetCharacters(TMP_TextInfo info)
    {
        boxes.Clear();
        if (!captions) return;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];
            if (!character.isVisible) continue;
            Vector3[] vertices = info.meshInfo[character.materialReferenceIndex].vertices;
            Vector2 min = vertices[character.vertexIndex], max = min;
            for (int j = 1; j < 4; j++)
            {
                min = Vector2.Min(min, vertices[character.vertexIndex + j]);
                max = Vector2.Max(max, vertices[character.vertexIndex + j]);
            }
            boxes.Add(Rect.MinMaxRect(Mathf.Max(bounds.xMin, min.x - 2f), Mathf.Max(bounds.yMin, min.y - 2f),
                Mathf.Min(bounds.xMax, max.x + 2f), Mathf.Min(bounds.yMax, max.y + 2f)));
        }
        if (captionMesh == null) captionMesh = new Mesh { name = "JapanTvUi Caption", hideFlags = HideFlags.HideAndDontSave };
        OnPopulateMesh(captionGeometry);
        captionGeometry.FillMesh(captionMesh);
        canvasRenderer.SetMesh(captionMesh);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (style == null || bounds.width <= 0f || bounds.height <= 0f) return;
        if (alert)
        {
            BattleUiJapanTvDrawing.Burst(vh, bounds, BattleUiJapanTvDrawing.Tint(style.japanTvBurstOuter, alpha), 1f);
            BattleUiJapanTvDrawing.Burst(vh, bounds, BattleUiJapanTvDrawing.Tint(style.japanTvYellow, alpha), 0.81f);
        }
        else if (marquee) DrawMarquee(vh);
        else if (!captions) BattleUiJapanTvDrawing.Frame(vh, bounds, style, role, alpha, false);
        for (int i = 0; i < boxes.Count; i++)
        {
            Color color = i % 4 == 0 ? style.japanTvCyan : i % 4 == 1 ? style.japanTvPink :
                i % 4 == 2 ? style.japanTvPurple : style.japanTvGreen;
            if (boxes[i].width > 0f && boxes[i].height > 0f)
                BattleUiStyleMeshEffect.Quad(vh, boxes[i], BattleUiJapanTvDrawing.Tint(color, alpha));
        }
    }

    private void DrawMarquee(VertexHelper vh)
    {
        Vector2 center = bounds.center;
        const int rays = 24;
        int first = vh.currentVertCount;
        for (int i = 0; i < rays; i++)
        {
            Color tint = BattleUiJapanTvDrawing.Tint(i % 2 == 0 ? style.japanTvRayPink : style.japanTvRayCyan, alpha);
            vh.AddVert(center, tint, Vector2.zero);
            for (int j = 0; j < 2; j++)
            {
                float angle = (i + j) * Mathf.PI * 2f / rays;
                Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
                float distance = Mathf.Min(bounds.width * 0.5f / Mathf.Max(0.0001f, Mathf.Abs(direction.x)),
                    bounds.height * 0.5f / Mathf.Max(0.0001f, Mathf.Abs(direction.y)));
                vh.AddVert(center + direction * distance, tint, Vector2.zero);
            }
            vh.AddTriangle(first + i * 3, first + i * 3 + 1, first + i * 3 + 2);
        }
        Rect sign = BattleUiStyleMeshEffect.Inset(bounds, Mathf.Min(9f, bounds.height * 0.12f));
        const int columns = 16, rows = 4;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                Rect cell = new(sign.xMin + sign.width * x / columns, sign.yMin + sign.height * y / rows, sign.width / columns, sign.height / rows);
                BattleUiStyleMeshEffect.Quad(vh, cell, BattleUiJapanTvDrawing.Tint((x + y) % 2 == 0 ? style.japanTvCheckDark : style.japanTvCheckPink, alpha));
            }
        int count = Mathf.Clamp(Mathf.CeilToInt((sign.width + sign.height) * 2f / Mathf.Max(12f, style.japanTvBulbSpacing)), 12, 80);
        float perimeter = (sign.width + sign.height) * 2f;
        float radius = Mathf.Min(style.japanTvBulbRadius, bounds.height * 0.055f);
        for (int i = 0; i < count; i++)
        {
            float distance = perimeter * i / count;
            Vector2 position;
            if (distance < sign.width) position = new Vector2(sign.xMin + distance, sign.yMin);
            else if ((distance -= sign.width) < sign.height) position = new Vector2(sign.xMax, sign.yMin + distance);
            else if ((distance -= sign.height) < sign.width) position = new Vector2(sign.xMax - distance, sign.yMax);
            else position = new Vector2(sign.xMin, sign.yMax - (distance - sign.width));
            BattleUiJapanTvDrawing.Disc(vh, position, radius * 1.7f, BattleUiJapanTvDrawing.Tint(style.japanTvPink, alpha * 0.35f));
            float brightness = ((i + bulbPhase) & 1) == 0 ? 1f : 0.45f;
            Color bulb = Color.Lerp(style.japanTvShadow, style.japanTvBulb, brightness);
            BattleUiJapanTvDrawing.Disc(vh, position, radius, BattleUiJapanTvDrawing.Tint(bulb, alpha));
        }
    }

    protected override void OnDestroy()
    {
        captionGeometry.Dispose();
        if (captionMesh != null)
        {
            if (Application.isPlaying) Destroy(captionMesh);
            else DestroyImmediate(captionMesh);
        }
        base.OnDestroy();
    }
}

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
        Sign(vh, rect, Tint(style.japanTvShadow, alpha), style.japanTvCutRatio);
        Rect face = rect;
        face.yMin += border;
        Sign(vh, face, Tint(style.japanTvInk, alpha), style.japanTvCutRatio);
        Sign(vh, BattleUiStyleMeshEffect.Inset(face, border), Tint(selected ? style.japanTvRed : role, alpha), style.japanTvCutRatio);
        Rect inside = BattleUiStyleMeshEffect.Inset(face, border * 2f);
        Sign(vh, inside, Tint(style.background, alpha), style.japanTvCutRatio);
        Matte(vh, BattleUiStyleMeshEffect.Inset(inside, Mathf.Min(inside.width, inside.height) * style.japanTvCutRatio), style, alpha);
        float cut = Mathf.Min(rect.width, rect.height) * style.japanTvCutRatio;
        Rect band = new(rect.xMin + cut + border, rect.yMax - border * 3f, Mathf.Max(0f, rect.width - cut * 2f - border * 2f), border);
        Sign(vh, band, Tint(style.japanTvYellow, alpha), style.japanTvCutRatio);
    }

    internal static void Sign(VertexHelper vh, Rect rect, Color color, float cutRatio)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        float cut = Mathf.Min(rect.width, rect.height) * cutRatio;
        float skew = cut * 0.6f;
        int first = vh.currentVertCount;
        vh.AddVert(rect.center, color, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMin + skew + cut, rect.yMin), color, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMax - cut, rect.yMin), color, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMax, rect.yMin + cut), color, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMax - skew, rect.yMax - cut), color, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMax - skew - cut, rect.yMax), color, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMin + cut, rect.yMax), color, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMin, rect.yMax - cut), color, Vector2.zero);
        vh.AddVert(new Vector2(rect.xMin + skew, rect.yMin + cut), color, Vector2.zero);
        for (int i = 0; i < 8; i++) vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % 8);
    }

    internal static void Matte(VertexHelper vh, Rect rect, BattleUiStyleSettings style, float alpha)
    {
        if (rect.width <= 0f || rect.height <= 0f || style.japanTvMatteGrain <= 0f) return;
        int count = Mathf.Clamp(Mathf.CeilToInt(rect.width * rect.height / 2400f), 4, 36);
        Color tint = Tint(style.japanTvPaper, alpha * style.japanTvMatteGrain);
        for (int i = 0; i < count; i++)
        {
            float x = Mathf.Repeat(i * 0.618034f + 0.17f, 1f);
            float y = Mathf.Repeat(i * 0.414214f + 0.31f, 1f);
            Disc(vh, new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, x), Mathf.Lerp(rect.yMin, rect.yMax, y)),
                Mathf.Min(0.8f, Mathf.Min(rect.width, rect.height) * 0.02f), tint);
        }
    }

    internal static void Burst(VertexHelper vh, Rect rect, Color color, float scale)
    {
        const int points = 32;
        int first = vh.currentVertCount;
        vh.AddVert(rect.center, color, Vector2.zero);
        for (int i = 0; i < points; i++)
        {
            float angle = i * Mathf.PI * 2f / points;
            float radius = (i % 2 == 0 ? 0.9f + 0.1f * Mathf.Sin(i * 2.3f) : 0.58f + 0.1f * Mathf.Cos(i * 1.7f)) * scale;
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
    private TextMeshProUGUI ribbon;
    private TMP_FontAsset checkedRibbonFont;
    private string checkedRibbonText;
    private bool japaneseRibbon;
    private readonly Vector2[] clipA = new Vector2[8], clipB = new Vector2[8];

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
        ConfigureRibbon(source);
    }

    private Rect RibbonRect()
    {
        float height = bounds.height * style.japanTvRibbonRatio;
        float width = Mathf.Min(bounds.width * 0.4f, style.japanTvDecorationFontSize * 6.5f);
        return new Rect(bounds.center.x - width * 0.5f, bounds.yMax - height * 1.1f, width, height);
    }

    private void ConfigureRibbon(MaskableGraphic source)
    {
        if (!alert && !marquee)
        {
            if (ribbon != null) ribbon.gameObject.SetActive(false);
            return;
        }
        TMP_FontAsset font = BattleUiThemeFonts.LoadJapanTvDecoration();
        string japanese = alert ? "速報" : role == style.japanTvPurple ? "特報" : role == style.japanTvGreen ? "運命" : "バラエティ";
        if (font != checkedRibbonFont || japanese != checkedRibbonText)
        {
            checkedRibbonFont = font;
            checkedRibbonText = japanese;
            japaneseRibbon = font != null && font.HasCharacters(japanese, out uint[] _, true, true);
        }
        if (!japaneseRibbon) font = BattleUiStyleController.ThemeTmpFont;
        if (font == null) return;
        if (ribbon == null)
        {
            GameObject child = new("JapanTvUiRibbon", typeof(RectTransform), typeof(LayoutElement));
            child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            child.GetComponent<LayoutElement>().ignoreLayout = true;
            ribbon = child.AddComponent<TextMeshProUGUI>();
            ribbon.raycastTarget = false;
            ribbon.alignment = TextAlignmentOptions.Center;
            ribbon.fontStyle = FontStyles.Normal;
            ribbon.textWrappingMode = TextWrappingModes.NoWrap;
            ribbon.overflowMode = TextOverflowModes.Truncate;
            ribbon.enableAutoSizing = true;
            ribbon.fontSizeMin = 8f;
            BattleUiOpticsCanvas.Register(ribbon);
        }
        Rect rect = RibbonRect();
        ribbon.rectTransform.anchorMin = ribbon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        ribbon.rectTransform.anchoredPosition = rect.center - rectTransform.rect.center;
        ribbon.rectTransform.sizeDelta = rect.size;
        ribbon.font = font;
        ribbon.fontSize = ribbon.fontSizeMax = style.japanTvDecorationFontSize;
        ribbon.text = japaneseRibbon ? japanese : alert ? "SPECIAL" : "VARIETY";
        ribbon.color = BattleUiJapanTvDrawing.Tint(style.japanTvInk, alpha);
        ribbon.maskable = source.maskable;
        ribbon.canvasRenderer.SetColor(source.canvasRenderer.GetColor());
        ribbon.gameObject.SetActive(true);
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
                BattleUiJapanTvDrawing.Sign(vh, boxes[i], BattleUiJapanTvDrawing.Tint(color, alpha), style.japanTvCutRatio);
        }
        if (alert || marquee)
            BattleUiJapanTvDrawing.Sign(vh, RibbonRect(), BattleUiJapanTvDrawing.Tint(style.japanTvPaper, alpha), style.japanTvCutRatio);
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
                float distance = 1f / (Mathf.Abs(direction.x) / (bounds.width * 0.5f) +
                    Mathf.Abs(direction.y) / (bounds.height * 0.5f));
                vh.AddVert(center + direction * distance, tint, Vector2.zero);
            }
            vh.AddTriangle(first + i * 3, first + i * 3 + 1, first + i * 3 + 2);
        }
        Rect sign = BattleUiStyleMeshEffect.Inset(bounds, Mathf.Min(9f, Mathf.Min(bounds.width, bounds.height) * 0.12f));
        const int columns = 16, rows = 4;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                Rect cell = new(sign.xMin + sign.width * x / columns, sign.yMin + sign.height * y / rows, sign.width / columns, sign.height / rows);
                DiamondCell(vh, cell, sign, BattleUiJapanTvDrawing.Tint((x + y) % 2 == 0 ? style.japanTvCheckDark : style.japanTvCheckPink, alpha));
            }
        Rect grain = new(sign.center.x - sign.width * 0.2f, sign.center.y - sign.height * 0.15f, sign.width * 0.4f, sign.height * 0.3f);
        BattleUiJapanTvDrawing.Matte(vh, grain, style, alpha);
        int count = Mathf.Clamp(2 * Mathf.CeilToInt((sign.width + sign.height) / Mathf.Max(12f, style.japanTvBulbSpacing)), 12, 80);
        float radius = Mathf.Min(style.japanTvBulbRadius, Mathf.Min(bounds.width, bounds.height) * 0.055f);
        for (int i = 0; i < count; i++)
        {
            float edge = i * 4f / count;
            int side = Mathf.FloorToInt(edge);
            Vector2 a = side == 0 ? new Vector2(sign.center.x, sign.yMax) : side == 1 ? new Vector2(sign.xMax, sign.center.y) :
                side == 2 ? new Vector2(sign.center.x, sign.yMin) : new Vector2(sign.xMin, sign.center.y);
            Vector2 b = side == 0 ? new Vector2(sign.xMax, sign.center.y) : side == 1 ? new Vector2(sign.center.x, sign.yMin) :
                side == 2 ? new Vector2(sign.xMin, sign.center.y) : new Vector2(sign.center.x, sign.yMax);
            Vector2 position = Vector2.Lerp(a, b, edge - side);
            BattleUiJapanTvDrawing.Disc(vh, position, radius * 1.4f, BattleUiJapanTvDrawing.Tint(style.japanTvShadow, alpha));
            float brightness = ((i + bulbPhase) & 1) == 0 ? 1f : 0.45f;
            Color bulb = Color.Lerp(style.japanTvShadow, style.japanTvBulb, brightness);
            BattleUiJapanTvDrawing.Disc(vh, position, radius, BattleUiJapanTvDrawing.Tint(bulb, alpha));
        }
    }

    private void DiamondCell(VertexHelper vh, Rect cell, Rect sign, Color tint)
    {
        Vector2[] input = clipA, output = clipB;
        input[0] = new Vector2(cell.xMin, cell.yMin);
        input[1] = new Vector2(cell.xMin, cell.yMax);
        input[2] = new Vector2(cell.xMax, cell.yMax);
        input[3] = new Vector2(cell.xMax, cell.yMin);
        int count = 4;
        for (int edge = 0; edge < 4 && count > 0; edge++)
        {
            Vector2 normal = new((edge < 2 ? 1f : -1f) / (sign.width * 0.5f),
                (edge % 2 == 0 ? 1f : -1f) / (sign.height * 0.5f));
            int nextCount = 0;
            Vector2 previous = input[count - 1];
            float previousDistance = Vector2.Dot(previous - sign.center, normal) - 1f;
            for (int i = 0; i < count; i++)
            {
                Vector2 current = input[i];
                float distance = Vector2.Dot(current - sign.center, normal) - 1f;
                if ((previousDistance <= 0f) != (distance <= 0f))
                    output[nextCount++] = Vector2.LerpUnclamped(previous, current, previousDistance / (previousDistance - distance));
                if (distance <= 0f) output[nextCount++] = current;
                previous = current;
                previousDistance = distance;
            }
            count = nextCount;
            (input, output) = (output, input);
        }
        int first = vh.currentVertCount;
        for (int i = 0; i < count; i++) vh.AddVert(input[i], tint, Vector2.zero);
        for (int i = 1; i + 1 < count; i++) vh.AddTriangle(first, first + i, first + i + 1);
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

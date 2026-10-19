using UnityEngine;
using UnityEngine.UI;

public sealed class BattleUiPanelDecoration : MaskableGraphic
{
    public static readonly Color Purple = new(0.62f, 0.43f, 0.89f, 1f);
    public static readonly Color Mint = new(0.36f, 0.88f, 0.73f, 1f);
    public static readonly Color Yellow = new(0.98f, 0.86f, 0.31f, 1f);

    private Color accent;
    private bool lightSurface;
    private bool emphasis;
    private bool selected;
    private bool animate;
    private float pulseUntil;
    private float nextRefresh;
    private float drift;
    private float pulse;

    public static Color Neutral(Color value)
    {
        float grey = value.grayscale;
        return new Color(grey, grey, grey, value.a);
    }

    public static BattleUiPanelDecoration Attach(RectTransform parent, Color accent,
        bool lightSurface = false, bool animate = true)
    {
        Transform existing = parent.Find("PanelDecoration");
        if (existing != null)
            return existing.GetComponent<BattleUiPanelDecoration>();

        GameObject child = new("PanelDecoration", typeof(RectTransform));
        child.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)child.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.SetAsFirstSibling();
        BattleUiPanelDecoration decoration = child.AddComponent<BattleUiPanelDecoration>();
        decoration.accent = accent;
        decoration.lightSurface = lightSurface;
        decoration.animate = animate;
        decoration.raycastTarget = false;
        decoration.SetVerticesDirty();
        return decoration;
    }

    public static void Round(Image image)
    {
        image.sprite = BattleHudSpriteCache.RoundedPanel;
        image.type = Image.Type.Sliced;
        image.color = Neutral(image.color);
    }

    public void SetEmphasis(bool value, bool selected = false)
    {
        if (emphasis == value && this.selected == selected)
            return;
        emphasis = value;
        this.selected = selected;
        pulseUntil = Time.unscaledTime + 0.18f;
        nextRefresh = 0f;
        SetVerticesDirty();
    }

    private void Update()
    {
        if ((!animate && Time.unscaledTime >= pulseUntil && pulse <= 0f) ||
            canvasRenderer.cull || canvasRenderer.GetInheritedAlpha() <= 0.01f ||
            Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + 0.1f;
        drift = animate ? Mathf.Sin(Time.unscaledTime * 0.45f) * 0.7f : 0f;
        pulse = Mathf.Clamp01((pulseUntil - Time.unscaledTime) / 0.18f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect face = rectTransform.rect;
        if (face.width < 8f || face.height < 8f)
            return;

        Ring(vh, Expand(face, 3f, new Vector2(4f + pulse, -5f - pulse)), 5f,
            new Color(0f, 0f, 0f, 0.26f));
        Ring(vh, Expand(face, 5f, new Vector2(-3f, 2f + drift)), 3f, Purple);
        Color edge = emphasis ? Yellow : accent;
        edge.a = emphasis ? 1f : 0.72f;
        Ring(vh, Expand(face, 3f + pulse * 2f, new Vector2(2f, -1f)), 2f, edge);
        Ring(vh, face, 1.3f, lightSurface
            ? new Color(0.16f, 0.16f, 0.16f, 0.85f)
            : new Color(0.88f, 0.88f, 0.88f, 0.70f));

        Color dot = lightSurface ? new Color(0f, 0f, 0f, 0.12f)
            : new Color(1f, 1f, 1f, 0.12f);
        for (int row = 0; row < 2; row++)
            for (int col = 0; col < 7; col++)
                Disc(vh, new Vector2(face.xMax - 22f - col * 7f,
                    face.yMin - 10f - row * 6f), 1.2f, dot);
        if (selected)
        {
            Vector2 badge = new(face.xMin - 7f, face.yMax - 12f);
            Disc(vh, badge, 7f, new Color(0.95f, 0.95f, 0.95f, 1f));
            int start = vh.currentVertCount;
            Color mark = new(0.10f, 0.10f, 0.10f, 1f);
            vh.AddVert(badge + new Vector2(-2f, -4f), mark, Vector2.zero);
            vh.AddVert(badge + new Vector2(4f, 0f), mark, Vector2.zero);
            vh.AddVert(badge + new Vector2(-2f, 4f), mark, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
        }
    }

    private static Rect Expand(Rect rect, float amount, Vector2 offset)
    {
        return new Rect(rect.xMin - amount + offset.x, rect.yMin - amount + offset.y,
            rect.width + amount * 2f, rect.height + amount * 2f);
    }

    private static Vector2 Contour(Rect rect, int corner, int step)
    {
        float radius = Mathf.Min(24f, Mathf.Min(rect.width, rect.height) * 0.22f);
        radius *= corner == 0 || corner == 2 ? 1f : 0.60f;
        Vector2 center = new(corner < 2 ? rect.xMax - radius : rect.xMin + radius,
            corner == 0 || corner == 3 ? rect.yMin + radius : rect.yMax - radius);
        float angle = (-90f + corner * 90f + step * 15f) * Mathf.Deg2Rad;
        return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    private static void Ring(VertexHelper vh, Rect rect, float width, Color tint)
    {
        Rect inner = Expand(rect, -width, Vector2.zero);
        int start = vh.currentVertCount;
        const int count = 28;
        for (int corner = 0; corner < 4; corner++)
            for (int step = 0; step <= 6; step++)
            {
                vh.AddVert(Contour(rect, corner, step), tint, Vector2.zero);
                vh.AddVert(Contour(inner, corner, step), tint, Vector2.zero);
            }
        for (int i = 0; i < count; i++)
        {
            int a = start + i * 2;
            int b = start + ((i + 1) % count) * 2;
            vh.AddTriangle(a, b, a + 1);
            vh.AddTriangle(a + 1, b, b + 1);
        }
    }

    private static void Disc(VertexHelper vh, Vector2 center, float radius, Color tint)
    {
        int start = vh.currentVertCount;
        vh.AddVert(center, tint, Vector2.zero);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI * 0.25f;
            vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                tint, Vector2.zero);
        }
        for (int i = 0; i < 8; i++)
            vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 8);
    }
}

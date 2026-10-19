using UnityEngine;
using UnityEngine.UI;

public sealed class BattleUiPanelDecoration : MaskableGraphic
{
    public static readonly Color Purple = new(0.62f, 0.43f, 0.89f, 1f);
    public static readonly Color Mint = new(0.36f, 0.88f, 0.73f, 1f);
    public static readonly Color Yellow = new(0.98f, 0.86f, 0.31f, 1f);
    public static readonly Color Surface = new(0.075f, 0.125f, 0.14f, 0.94f);
    public static readonly Color Paper = new(0.90f, 0.94f, 0.92f, 1f);
    public static readonly Color Muted = new(0.62f, 0.70f, 0.70f, 1f);
    public static readonly Color SelectedSurface = new(0.16f, 0.26f, 0.27f, 0.98f);
    public static readonly Color EquippedSurface = new(0.10f, 0.19f, 0.20f, 0.98f);
    public static readonly Color LockedSurface = new(0.045f, 0.075f, 0.085f, 0.96f);
    public static readonly Vector2 CompactPackSize = new(430f, 176f);
    public static readonly Vector2 ExpandedPackSize = new(632f, 696f);
    public static readonly Vector2 MiniPackSize = new(304f, 335f);
    public const float MiniCellSize = 78f;
    public const float MiniCellGap = 7f;
    public const float FullCellSize = 184f;
    public const float FullCellGap = 18f;
    public const float MiniIconPixels = 36f;
    public const float FullIconPixels = 88f;
    public const int MiniNameFontSize = 8;
    public const int FullNameFontSize = 13;
    public static readonly Vector2 CursorTilt = new(5f, 7f);
    public static readonly Vector2 GatherTilt = new(1.5f, 2.5f);
    public static readonly Quaternion PanelPose = Quaternion.Euler(4.5f, -7f, -1.1f);
    public const float DepthStep = 12f;
    public const float Padding = 16f;
    public const float HeaderHeight = 36f;
    public static Vector2 CompactPackPosition => new(-28f - CompactPackSize.x * 0.5f, 24f + CompactPackSize.y * 0.5f);

    public static float PackReveal(float progress)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.70f, 0.94f, progress));
    }

    private Color accent;
    private bool softFill;
    private bool hasHeader;
    private bool emphasis;
    private bool selected;
    private bool animate;
    private float pulseUntil;
    private float nextRefresh;
    private float pulse;

    public static Color Neutral(Color value)
    {
        float grey = value.grayscale;
        return new Color(grey, grey, grey, value.a);
    }

    public static void Bubble(BattleSpeechBubbleFrameStyle frame, BattleSpeechBubbleTailStyle tail)
    {
        frame.fillColor = Surface;
        frame.outlineColor = Color.clear;
        frame.rotation = 0f;
        tail.outlineColor = Color.clear;
        tail.strokeRoot = tail.strokePivot1 = tail.strokePivot2 = tail.strokePivot3 = tail.strokeTip = 0.75f;
    }

    public static BattleUiPanelDecoration Attach(RectTransform parent, Color accent,
        bool softFill = false, bool animate = true)
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
        decoration.softFill = softFill;
        decoration.animate = animate;
        decoration.raycastTarget = false;
        foreach (Outline outline in parent.GetComponents<Outline>())
            outline.enabled = false;
        decoration.SetVerticesDirty();
        return decoration;
    }

    public static void Header(RectTransform parent, string title, Color accent)
    {
        BattleUiPanelDecoration decoration = Attach(parent, accent);
        decoration.hasHeader = true;
        GameObject label = new("AppHeader", typeof(RectTransform));
        label.transform.SetParent(parent, false);
        Text text = label.AddComponent<Text>();
        text.font = BattleFeedbackFont.Resolve();
        text.fontSize = 12;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Paper;
        text.text = title;
        text.raycastTarget = false;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(Padding + 18f, -HeaderHeight);
        rect.offsetMax = new Vector2(-Padding - 36f, -6f);
        decoration.SetVerticesDirty();
    }

    public static void Round(Image image)
    {
        image.sprite = BattleHudSpriteCache.RoundedPanel;
        image.type = Image.Type.Sliced;
        image.color = new Color(Surface.r, Surface.g, Surface.b, image.color.a > 0f ? Surface.a : 0f);
    }

    public void SetEmphasis(bool value, bool selected = false)
    {
        if (emphasis == value && this.selected == selected)
            return;
        emphasis = value;
        this.selected = selected;
        pulseUntil = animate ? Time.unscaledTime + 0.18f : 0f;
        nextRefresh = 0f;
        SetVerticesDirty();
    }

    private void Update()
    {
        if ((Time.unscaledTime >= pulseUntil && pulse <= 0f) ||
            canvasRenderer.cull || canvasRenderer.GetInheritedAlpha() <= 0.01f ||
            Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + 0.033f;
        pulse = Mathf.Clamp01((pulseUntil - Time.unscaledTime) / 0.18f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect face = rectTransform.rect;
        if (face.width < 8f || face.height < 8f)
            return;

        Fill(vh, Expand(face, 0f, new Vector2(3f + pulse, -4f - pulse)),
            new Color(0f, 0f, 0f, 0.12f), new Color(0f, 0f, 0f, 0.12f));
        float frost = softFill ? 0.08f : 0.14f;
        Fill(vh, face, new Color(0.76f, 0.88f, 0.87f, 0.025f),
            new Color(0.76f, 0.88f, 0.87f, frost + pulse * 0.05f));
        if (hasHeader)
        {
            Rect header = new(face.xMin + 5f, face.yMax - HeaderHeight, face.width - 10f, HeaderHeight - 5f);
            Fill(vh, header, new Color(0.30f, 0.48f, 0.48f, 0.08f),
                new Color(0.30f, 0.48f, 0.48f, 0.16f));
            Disc(vh, new Vector2(face.xMin + Padding + 5f, face.yMax - 21f), 6f, accent);
            for (int i = 0; i < 3; i++)
                Disc(vh, new Vector2(face.xMax - Padding - i * 8f, face.yMax - 21f), 1.5f, Muted);
        }
        if (emphasis)
        {
            Color tint = accent;
            tint.a = 0.13f + pulse * 0.08f;
            Fill(vh, Expand(face, -2f, Vector2.zero), tint, tint);
            Rect tab = new(face.xMin + 5f, face.center.y - 13f, 4f, 26f);
            Fill(vh, tab, accent, accent);
        }
        if (selected)
        {
            Vector2 badge = new(face.xMax - 14f, face.yMin + 14f);
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
        Vector2 center = new(corner < 2 ? rect.xMax - radius : rect.xMin + radius,
            corner == 0 || corner == 3 ? rect.yMin + radius : rect.yMax - radius);
        float angle = (-90f + corner * 90f + step * 15f) * Mathf.Deg2Rad;
        return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    private static void Fill(VertexHelper vh, Rect rect, Color bottom, Color top)
    {
        int start = vh.currentVertCount;
        const int count = 28;
        vh.AddVert(rect.center, Color.Lerp(bottom, top, 0.5f), Vector2.zero);
        for (int corner = 0; corner < 4; corner++)
            for (int step = 0; step <= 6; step++)
            {
                Vector2 point = Contour(rect, corner, step);
                vh.AddVert(point, Color.Lerp(bottom, top, Mathf.InverseLerp(rect.yMin, rect.yMax, point.y)), Vector2.zero);
            }
        for (int i = 0; i < count; i++)
            vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % count);
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

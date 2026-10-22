using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BattleUiStyleMeshEffect : BaseMeshEffect
{
    private Graphic target;
    private Text label;
    private Image image;
    private bool panel, speechFrame, hiddenFill, tailFill;
    private Material originalMaterial, appliedMaterial;
    private Font originalFont, appliedFont, requestedFont;
    private string lastText;
    private bool materialOverridden, fontOverridden, wasPixel;
    private FontStyle originalFontStyle;
    private bool fontStyleOverridden;

    internal void Bind(Graphic value)
    {
        target = value;
        label = value as Text;
        image = value as Image;
        panel = value is BattleUiPanelDecoration ||
            (image != null && image.sprite == BattleHudSpriteCache.RoundedPanel);
        originalMaterial = CaptureMaterial();
        originalFont = label != null ? label.font : null;
        originalFontStyle = label != null ? label.fontStyle : FontStyle.Normal;
        if (image != null)
        {
            speechFrame = image.TryGetComponent<BattleSpeechBubbleFrameFillController>(out _);
            hiddenFill = image.name == "BubbleFrameFill";
            tailFill = image.name == "TailFill";
            panel |= speechFrame;
        }
    }

    internal void SetSpeechFrame(bool fill)
    {
        speechFrame = !fill;
        hiddenFill = fill;
        panel = !fill;
        target.SetVerticesDirty();
    }

    internal void SetPanel()
    {
        panel = true;
        target.SetVerticesDirty();
    }

    internal void ApplyAppearance(bool pixel, BattleUiStyleSettings settings,
        Material pixelPanel, Material pixelText, Font bitmap)
    {
        if (materialOverridden && target.material != appliedMaterial)
            originalMaterial = CaptureMaterial();
        if (label != null && fontOverridden && label.font != appliedFont)
        {
            originalFont = label.font;
            lastText = null;
        }
        if (label != null)
        {
            if (pixel)
            {
                if (!fontStyleOverridden || label.fontStyle != FontStyle.Normal)
                    originalFontStyle = label.fontStyle;
                if (label.fontStyle != FontStyle.Normal) label.fontStyle = FontStyle.Normal;
                fontStyleOverridden = true;
            }
            else RestoreFontStyle();
        }
        Material next = label != null ? settings.textMaterial : panel ? settings.panelMaterial : null;
        bool statusFill = image != null && image.type == Image.Type.Filled;
        if (pixel && next == null)
            next = label != null || tailFill || statusFill ? pixelText : panel ? pixelPanel : null;
        if (next != null)
        {
            if (!materialOverridden) originalMaterial = CaptureMaterial();
            if (target.material != next) target.material = next;
            appliedMaterial = target.material;
            materialOverridden = true;
        }
        else RestoreMaterial();

        if (label != null && (pixel || settings.font != null))
        {
            if (!fontOverridden) originalFont = label.font;
            if (!fontOverridden || lastText != label.text || requestedFont != settings.font || wasPixel != pixel)
            {
                Font preferred = settings.font != null ? settings.font : bitmap;
                appliedFont = Supports(preferred, label.text) ? preferred :
                    Supports(originalFont, label.text) ? originalFont : BattleFeedbackFont.Resolve();
                lastText = label.text;
                requestedFont = settings.font;
            }
            if (label.font != appliedFont) label.font = appliedFont;
            fontOverridden = true;
        }
        else RestoreFont();
        if (wasPixel != pixel) target.SetVerticesDirty();
        wasPixel = pixel;
    }

    private static bool Supports(Font font, string content)
    {
        if (font == null) return false;
        foreach (char c in content)
            if (!char.IsControl(c) && !font.HasCharacter(c)) return false;
        return true;
    }

    internal void ReleaseMaterialOverride() => RestoreMaterial();

    private Material CaptureMaterial() => target.material == target.defaultMaterial ? null : target.material;

    private void RestoreMaterial()
    {
        if (materialOverridden && target != null && target.material == appliedMaterial)
            target.material = originalMaterial;
        materialOverridden = false;
    }

    private void RestoreFont()
    {
        if (fontOverridden && label != null && label.font == appliedFont) label.font = originalFont;
        fontOverridden = false;
        lastText = null;
    }

    internal void RestoreAppearance()
    {
        RestoreMaterial();
        RestoreFont();
        RestoreFontStyle();
        if (target != null) target.SetVerticesDirty();
    }

    private void RestoreFontStyle()
    {
        if (fontStyleOverridden && label != null && label.fontStyle == FontStyle.Normal)
            label.fontStyle = originalFontStyle;
        fontStyleOverridden = false;
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || !BattleUiStyleController.IsPixel || target == null) return;
        BattleUiStyleSettings settings = BattleUiStyleController.Settings;
        if (hiddenFill) { vh.Clear(); return; }
        bool statusFill = image != null && image.type == Image.Type.Filled;
        if (panel && image != null && !statusFill)
        {
            vh.Clear();
            Color fill = settings.background;
            fill.a *= speechFrame ? 1f : image.color.a;
            DrawFrame(vh, target.rectTransform.rect, settings, fill);
            return;
        }
        if (label == null && !tailFill && !statusFill) return;
        UIVertex vertex = default;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            Color tint = tailFill ? settings.background : statusFill ? settings.status : settings.innerLine;
            tint.a *= vertex.color.a / 255f;
            vertex.color = tint;
            if (tailFill)
            {
                float pixel = Mathf.Max(1f, settings.logicalPixelSize);
                Vector3 point = vertex.position;
                point.x = Mathf.Round(point.x / pixel) * pixel;
                point.y = Mathf.Round(point.y / pixel) * pixel;
                vertex.position = point;
            }
            vh.SetUIVertex(vertex, i);
        }
    }

    internal static void DrawFrame(VertexHelper vh, Rect rect, BattleUiStyleSettings settings, Color fill)
    {
        float pixel = Mathf.Max(1f, settings.logicalPixelSize);
        Color outer = settings.outerFrame, inner = settings.innerLine;
        outer.a *= fill.a;
        inner.a *= fill.a;
        StepFill(vh, rect, pixel, outer);
        rect = Inset(rect, 2f * pixel);
        StepFill(vh, rect, pixel, inner);
        StepFill(vh, Inset(rect, pixel), pixel, fill);
    }

    internal static Rect Inset(Rect rect, float amount) => new(rect.x + amount, rect.y + amount,
        Mathf.Max(0f, rect.width - amount * 2f), Mathf.Max(0f, rect.height - amount * 2f));

    internal static void StepFill(VertexHelper vh, Rect rect, float pixel, Color tint)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        float cut = Mathf.Min(pixel * 2f, Mathf.Min(rect.width, rect.height) * 0.25f);
        Quad(vh, new Rect(rect.xMin + cut, rect.yMin, rect.width - 2f * cut, cut), tint);
        Quad(vh, new Rect(rect.xMin + pixel, rect.yMin + cut, rect.width - 2f * pixel, pixel), tint);
        Quad(vh, new Rect(rect.xMin, rect.yMin + cut + pixel, rect.width, rect.height - 2f * (cut + pixel)), tint);
        Quad(vh, new Rect(rect.xMin + pixel, rect.yMax - cut - pixel, rect.width - 2f * pixel, pixel), tint);
        Quad(vh, new Rect(rect.xMin + cut, rect.yMax - cut, rect.width - 2f * cut, cut), tint);
    }

    internal static void Quad(VertexHelper vh, Rect rect, Color tint)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        int first = vh.currentVertCount;
        vh.AddVert(new Vector3(rect.xMin, rect.yMin, 0f), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMin, rect.yMax, 0f), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax, rect.yMax, 0f), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax, rect.yMin, 0f), tint, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }
}

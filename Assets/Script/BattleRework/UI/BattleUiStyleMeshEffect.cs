using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BattleUiStyleMeshEffect : BaseMeshEffect
{
    private Graphic target;
    private Text label;
    private Image image;
    private bool panel, speechFrame, hiddenFill, tailFill, decoratedSurface;
    private Material originalMaterial, appliedMaterial;
    private Font originalFont, appliedFont, requestedFont;
    private string lastText;
    private bool materialOverridden, fontOverridden, wasPixel;
    private FontStyle originalFontStyle;
    private bool fontStyleOverridden;
    private bool wasComic;
    private bool projectPixels;
    private readonly List<UIVertex> triangles = new();
    private Matrix4x4 lastTransform, lastCamera;
    private Rect lastViewport;
    private Camera renderCamera;
    private Rect viewport;

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
        decoratedSurface = true;
        target.SetVerticesDirty();
    }

    internal void ApplyAppearance(bool pixel, BattleUiStyleSettings settings,
        Material pixelPanel, Material pixelText, Material pixelSprite, Font bitmap)
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
            next = label != null ? pixelText : panel && !statusFill ? pixelPanel : pixelSprite;
        bool comic = BattleUiStyleController.IsComic;
        if (comic && next == null && panel) next = pixelPanel;
        if (next != null)
        {
            if (!materialOverridden) originalMaterial = CaptureMaterial();
            if (target.material != next) target.material = next;
            appliedMaterial = target.material;
            materialOverridden = true;
        }
        else RestoreMaterial();
        projectPixels = pixel && next != null &&
            ((pixelPanel != null && next.shader == pixelPanel.shader) ||
             (pixelText != null && next.shader == pixelText.shader));

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
        if (wasPixel != pixel || wasComic != comic) target.SetVerticesDirty();
        wasPixel = pixel;
        wasComic = comic;
        if (projectPixels && target.canvas != null)
        {
            Canvas canvas = target.canvas;
            const AdditionalCanvasShaderChannels channels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            if ((canvas.additionalShaderChannels & channels) != channels)
                canvas.additionalShaderChannels |= channels;
            renderCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            viewport = renderCamera != null ? renderCamera.pixelRect : canvas.pixelRect;
            Matrix4x4 transformMatrix = target.rectTransform.localToWorldMatrix;
            Matrix4x4 cameraMatrix = renderCamera != null ? renderCamera.projectionMatrix * renderCamera.worldToCameraMatrix : Matrix4x4.identity;
            if (lastTransform != transformMatrix || lastCamera != cameraMatrix || lastViewport != viewport)
            {
                lastTransform = transformMatrix;
                lastCamera = cameraMatrix;
                lastViewport = viewport;
                target.SetVerticesDirty();
            }
        }
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
        if (!IsActive() || target == null) return;
        bool pixel = BattleUiStyleController.IsPixel;
        bool comic = BattleUiStyleController.IsComic;
        if (!pixel && !comic) return;
        BattleUiStyleSettings settings = BattleUiStyleController.Settings;
        if (hiddenFill || decoratedSurface) { vh.Clear(); return; }
        bool statusFill = image != null && image.type == Image.Type.Filled;
        if (panel && image != null && !statusFill)
        {
            vh.Clear();
            Color fill = settings.background;
            fill.a *= speechFrame ? 1f : image.color.a;
            if (comic) DrawComicFrame(vh, target.rectTransform.rect, settings, fill.a);
            else DrawFrame(vh, target.rectTransform.rect, settings, fill, PixelStep(target.rectTransform, target.canvas, settings));
        }
        else if (label != null || tailFill || statusFill)
        {
            UIVertex vertex = default;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                Color source = vertex.color;
                Color tint = comic ? (tailFill ? settings.comicPaper : label != null ? settings.comicPaper : settings.comicAccent) :
                    tailFill ? Surface(settings) : Palette(source, settings, statusFill);
                tint.a *= source.a;
                vertex.color = tint;
                vh.SetUIVertex(vertex, i);
            }
        }
        if (projectPixels && target.canvas != null)
            BattleUiPixelProjection.Project(vh, triangles, target.rectTransform, renderCamera, viewport,
                Mathf.Max(1f, Mathf.Round(label != null ? settings.textPixelSize : settings.logicalPixelSize * BattleUiStyleController.PixelScale)));
    }

    internal static Color Surface(BattleUiStyleSettings settings)
    {
        Color tint = settings.background;
        tint.r *= settings.surfaceShade;
        tint.g *= settings.surfaceShade;
        tint.b *= settings.surfaceShade;
        return tint;
    }

    private static Color Palette(Color source, BattleUiStyleSettings settings, bool status)
    {
        float high = Mathf.Max(source.r, Mathf.Max(source.g, source.b));
        float low = Mathf.Min(source.r, Mathf.Min(source.g, source.b));
        if (high - low < 0.18f) return status ? settings.status : settings.innerLine;
        if (source.r > source.g * 1.25f && source.r > source.b) return settings.danger;
        if (source.b > source.r && source.b > source.g * 0.9f) return settings.energy;
        if (source.r > source.b * 1.3f && source.g > source.b * 1.3f) return settings.highlight;
        return settings.status;
    }

    internal static float PixelStep(RectTransform rect, Canvas canvas, BattleUiStyleSettings settings)
    {
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 origin = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(Vector3.zero));
        Vector2 right = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(Vector3.right));
        return Mathf.Max(1f, Mathf.Round(settings.logicalPixelSize * BattleUiStyleController.PixelScale)) / Mathf.Max(0.001f, Vector2.Distance(origin, right));
    }

    internal static void DrawFrame(VertexHelper vh, Rect rect, BattleUiStyleSettings settings, Color fill, float pixel)
    {
        Color outer = settings.outerFrame;
        outer.r *= settings.frameShade;
        outer.g *= settings.frameShade;
        outer.b *= settings.frameShade;
        outer.a *= fill.a;
        Color surface = Surface(settings);
        surface.a = fill.a;
        StepFill(vh, rect, pixel, outer);
        StepFill(vh, Inset(rect, settings.framePixels * pixel), pixel, surface);
    }

    internal static void DrawComicFrame(VertexHelper vh, Rect rect, BattleUiStyleSettings settings, float alpha)
    {
        Color red = settings.comicAccent, paper = settings.comicPaper, ink = settings.comicBackground;
        red.a *= alpha;
        paper.a *= alpha;
        ink.a *= alpha;
        Slant(vh, new Rect(rect.x + 5f, rect.y - 5f, rect.width, rect.height), red);
        Slant(vh, rect, paper);
        Slant(vh, Inset(rect, 3f), ink);
    }

    internal static void Slant(VertexHelper vh, Rect rect, Color tint)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        float cut = Mathf.Min(12f, rect.width * 0.08f);
        int first = vh.currentVertCount;
        vh.AddVert(new Vector3(rect.xMin, rect.yMin, 0f), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMin + cut, rect.yMax, 0f), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax, rect.yMax, 0f), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax - cut, rect.yMin, 0f), tint, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
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

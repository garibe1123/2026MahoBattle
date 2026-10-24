using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BattleUiStyleMeshEffect : BaseMeshEffect
{
    private Graphic target;
    private Text label;
    private Image image;
    private BattleUiPanelDecoration decoration;
    private bool panel, speechFrame, hiddenFill, tailFill, decoratedSurface;
    private Material originalMaterial, appliedMaterial;
    private Font originalFont, appliedFont, requestedFont;
    private string lastText;
    private bool materialOverridden, fontOverridden, wasPixel;
    private FontStyle originalFontStyle;
    private bool fontStyleOverridden;
    private bool wasComic;
    private bool wasMaho;
    private bool wasStylish, wasBirthday, title;
    private BattleUiStyleTmpTitle tmpTitle;
    private bool projectPixels;
    private readonly List<UIVertex> triangles = new();
    private Matrix4x4 lastTransform, lastCamera;
    private Rect lastViewport;
    private Camera renderCamera;
    private Rect viewport;

    internal void Bind(Graphic value)
    {
        BattleUiOpticsCanvas.Register(value);
        target = value;
        label = value as Text;
        title = label != null && (value.name == "AppHeader" || value.name == "Title");
        image = value as Image;
        decoration = value as BattleUiPanelDecoration;
        panel = value is BattleUiPanelDecoration ||
            (image != null && image.sprite == BattleHudSpriteCache.RoundedPanel);
        originalMaterial = CaptureMaterial();
        originalFont = label != null ? label.font : null;
        originalFontStyle = label != null ? label.fontStyle : FontStyle.Normal;
        if (image != null)
        {
            Transform child = image.transform.Find("PanelDecoration");
            if (child != null) decoration = child.GetComponent<BattleUiPanelDecoration>();
            decoratedSurface = decoration != null;
            panel |= decoratedSurface;
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

    internal void SetPanel(bool decorated = true)
    {
        panel = true;
        decoratedSurface = decorated;
        if (decorated && decoration == null && image != null)
        {
            Transform child = image.transform.Find("PanelDecoration");
            if (child != null) decoration = child.GetComponent<BattleUiPanelDecoration>();
        }
        target.SetVerticesDirty();
    }

    internal void ApplyAppearance(bool pixel, BattleUiStyleSettings settings,
        Material pixelPanel, Material pixelText, Material pixelSprite, Font bitmap, Material mahoMaterial, Font themeFont)
    {
        if (decoration != null) decoration.RefreshBackgroundOpacity();
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
        bool maho = BattleUiStyleController.IsMahoShojo;
        if (maho && next == null) next = panel && !statusFill ? pixelPanel : mahoMaterial;
        bool stylish = BattleUiStyleController.IsStylish;
        bool birthday = BattleUiStyleController.IsBirthday;
        if ((stylish || birthday) && next == null && panel && !statusFill) next = pixelPanel;
        if (HasAssignedMaterial()) next = null;
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

        Font preferredFont = settings.font != null ? settings.font : themeFont;
        if (label != null && (pixel || preferredFont != null))
        {
            if (!fontOverridden) originalFont = label.font;
            if (!fontOverridden || lastText != label.text || requestedFont != preferredFont || wasPixel != pixel)
            {
                Font preferred = preferredFont != null ? preferredFont : bitmap;
                appliedFont = Supports(preferred, label.text) ? preferred :
                    Supports(originalFont, label.text) ? originalFont : BattleFeedbackFont.Resolve();
                lastText = label.text;
                requestedFont = preferredFont;
            }
            if (label.font != appliedFont) label.font = appliedFont;
            fontOverridden = true;
        }
        else RestoreFont();
        if (wasPixel != pixel || wasComic != comic || wasMaho != maho ||
            wasStylish != stylish || wasBirthday != birthday) target.SetVerticesDirty();
        wasPixel = pixel;
        wasComic = comic;
        wasMaho = maho;
        wasStylish = stylish;
        wasBirthday = birthday;
        bool decoratedTitle = title && (stylish || birthday);
        if (isActiveAndEnabled && label != null && settings.textMaterial == null &&
            (settings.font == null || settings.fontAsset != null || (decoratedTitle && settings.titleFont != null)) && !HasAssignedMaterial())
        {
            TMPro.TMP_FontAsset font = decoratedTitle && settings.titleFont != null ? settings.titleFont :
                settings.fontAsset != null ? settings.fontAsset : BattleUiStyleController.ThemeTmpFont;
            if (font != null && (tmpTitle != null || label.canvas == null ||
                label.canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay))
            {
                if (tmpTitle == null) tmpTitle = new BattleUiStyleTmpTitle(label);
                tmpTitle.Apply(font, settings, birthday, decoratedTitle);
            }
            else HideTmpTitle();
        }
        else HideTmpTitle();
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

    private bool HasAssignedMaterial()
    {
        return BattleUiMaterialController.IsAssignedMaterial(target.material) ||
            (BattleShowPresentationManager.Instance != null &&
             BattleShowPresentationManager.Instance.HasAssignedUiMaterial(target));
    }

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
        HideTmpTitle();
        RestoreMaterial();
        RestoreFont();
        RestoreFontStyle();
        if (target != null) target.SetVerticesDirty();
    }

    internal void HideTmpTitle() => tmpTitle?.Hide();

    protected override void OnDisable()
    {
        HideTmpTitle();
        base.OnDisable();
    }

    protected override void OnDestroy()
    {
        tmpTitle?.Dispose();
        base.OnDestroy();
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
        ModifyStyledMesh(vh);
        if (image != null && decoration != null)
            decoration.ApplyBackgroundOpacity(vh, vh.currentVertCount, image.color.a);
    }

    private void ModifyStyledMesh(VertexHelper vh)
    {
        if (!IsActive() || target == null || HasAssignedMaterial()) return;
        if (tmpTitle != null && tmpTitle.Visible) { vh.Clear(); return; }
        bool pixel = BattleUiStyleController.IsPixel;
        bool comic = BattleUiStyleController.IsComic;
        bool maho = BattleUiStyleController.IsMahoShojo;
        bool stylish = BattleUiStyleController.IsStylish;
        bool birthday = BattleUiStyleController.IsBirthday;
        if (!pixel && !comic && !maho && !stylish && !birthday) return;
        BattleUiStyleSettings settings = BattleUiStyleController.Settings;
        // 접합 계산에 쓰는 프레임 마스크와 표시되는 외곽을 일치시킵니다.
        if (hiddenFill || speechFrame) return;
        if (decoratedSurface) { vh.Clear(); return; }
        bool statusFill = image != null && image.type == Image.Type.Filled;
        if (panel && image != null && !statusFill)
        {
            vh.Clear();
            Color fill = settings.background;
            fill.a *= speechFrame ? 1f : image.color.a;
            if (stylish || birthday)
                DrawAccentFrame(vh, target.rectTransform.rect, settings, birthday, speechFrame ? 1f : image.color.a);
            else if (maho) DrawMahoFrame(vh, target.rectTransform.rect, settings, fill.a);
            else if (comic) DrawComicFrame(vh, target.rectTransform.rect, settings, fill.a);
            else DrawFrame(vh, target.rectTransform.rect, settings, fill, PixelStep(target.canvas, settings));
        }
        else if (label != null || tailFill || statusFill || (maho && image != null))
        {
            UIVertex vertex = default;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                Color source = vertex.color;
                Color tint = stylish || birthday ? (tailFill ? settings.background :
                    label != null ? (stylish && title && label.name == "AppHeader" ? settings.background : settings.innerLine) :
                    Palette(source, settings, true)) :
                    maho ? (tailFill || (image != null && image.name == "TailShape") ? settings.background :
                    label != null ? settings.innerLine : statusFill ? settings.energy :
                    image.sprite == null ? settings.status : settings.danger) :
                    comic ? (tailFill ? settings.comicPaper : label != null ? settings.comicPaper : settings.comicAccent) :
                    tailFill ? Surface(settings) : Palette(source, settings, statusFill);
                tint.a *= source.a;
                vertex.color = tint;
                vh.SetUIVertex(vertex, i);
            }
        }
        if (stylish && title) ShearTitle(vh, settings.stylishTitleShear);
        if (projectPixels && target.canvas != null)
            BattleUiPixelProjection.Project(vh, triangles, target.rectTransform, renderCamera, viewport,
                Mathf.Max(1f, Mathf.Round(label != null ? settings.textPixelSize : settings.logicalPixelSize * BattleUiStyleController.PixelScale)) /
                Mathf.Max(0.001f, target.canvas.scaleFactor));
    }

    private void ShearTitle(VertexHelper vh, float shear)
    {
        Rect bounds = target.rectTransform.rect;
        if (vh.currentVertCount == 0 || bounds.width <= 0f || shear == 0f) return;
        UIVertex vertex = default;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            minY = Mathf.Min(minY, vertex.position.y);
            maxY = Mathf.Max(maxY, vertex.position.y);
        }
        float midY = (minY + maxY) * 0.5f;
        float minX = float.MaxValue, maxX = float.MinValue;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            float x = vertex.position.x + shear * (vertex.position.y - midY);
            minX = Mathf.Min(minX, x);
            maxX = Mathf.Max(maxX, x);
        }
        float width = maxX - minX;
        float scale = width > 0f ? Mathf.Min(1f, bounds.width / width) : 1f;
        float center = (minX + maxX) * 0.5f;
        float fittedCenter = Mathf.Clamp(center, bounds.xMin + width * scale * 0.5f,
            bounds.xMax - width * scale * 0.5f);
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            Vector3 position = vertex.position;
            position.x = (position.x + shear * (position.y - midY) - center) * scale + fittedCenter;
            vertex.position = position;
            vh.SetUIVertex(vertex, i);
        }
    }

    internal static Color Surface(BattleUiStyleSettings settings)
    {
        Color tint = settings.background;
        tint.r *= settings.surfaceShade;
        tint.g *= settings.surfaceShade;
        tint.b *= settings.surfaceShade;
        return tint;
    }

    internal static Color TextColor(Color source, BattleUiStyleSettings settings)
    {
        Color tint = BattleUiStyleController.IsComic ? settings.comicPaper :
            BattleUiStyleController.IsMahoShojo || BattleUiStyleController.IsStylish || BattleUiStyleController.IsBirthday
                ? settings.innerLine : BattleUiStyleController.IsPixel ? Palette(source, settings, false) : source;
        if (BattleUiStyleController.IsPixel || BattleUiStyleController.IsComic || BattleUiStyleController.IsMahoShojo ||
            BattleUiStyleController.IsStylish || BattleUiStyleController.IsBirthday) tint.a *= source.a;
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

    internal static float PixelStep(Canvas canvas, BattleUiStyleSettings settings)
    {
        return Mathf.Max(1f, Mathf.Round(settings.logicalPixelSize * BattleUiStyleController.PixelScale)) /
            Mathf.Max(0.001f, canvas != null ? canvas.scaleFactor : 1f);
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

    internal static void DrawAccentFrame(VertexHelper vh, Rect rect, BattleUiStyleSettings settings,
        bool birthday, float alpha)
    {
        Color rim = settings.outerFrame, fill = settings.background;
        rim.a *= alpha;
        fill.a *= alpha;
        if (birthday)
        {
            Color pink = settings.status, mint = settings.energy;
            pink.a *= alpha;
            mint.a *= alpha;
            rect = Border(vh, rect, settings.birthdayBorderWidth, rim);
            rect = Border(vh, rect, settings.birthdayBorderWidth, pink);
            rect = Border(vh, rect, settings.birthdayBorderWidth, mint);
        }
        else rect = Border(vh, rect, settings.stylishBorderWidth, rim);
        if (birthday)
        {
            Color top = Color.Lerp(fill, settings.outerFrame, 0.65f);
            top.a = fill.a;
            int first = vh.currentVertCount;
            vh.AddVert(new Vector3(rect.xMin, rect.yMin, 0f), fill, Vector2.zero);
            vh.AddVert(new Vector3(rect.xMin, rect.yMax, 0f), top, Vector2.zero);
            vh.AddVert(new Vector3(rect.xMax, rect.yMax, 0f), top, Vector2.zero);
            vh.AddVert(new Vector3(rect.xMax, rect.yMin, 0f), fill, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first, first + 2, first + 3);
            float step = Mathf.Min(12f, rect.width / 8f);
            float size = Mathf.Min(3f, Mathf.Min(rect.height * 0.05f, step));
            for (int i = 0; i < 6 && size > 0f; i++)
            {
                Color confetti = i % 3 == 0 ? settings.status : i % 3 == 1 ? settings.energy : settings.highlight;
                confetti.a *= alpha * Mathf.Clamp01(settings.surfacePatternAlpha * 3f);
                Vector2 center = new(rect.xMax - step * (i + 1), rect.yMin + size * 2f);
                RoundedFill(vh, new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size),
                    i % 2 == 0 ? 0f : size * 0.5f, confetti);
            }
        }
        else
        {
            Quad(vh, rect, fill);
            Color ink = settings.outerFrame;
            ink.a *= alpha * settings.surfacePatternAlpha;
            float step = Mathf.Max(4f, settings.stylishDotSpacing);
            float radius = Mathf.Clamp(settings.stylishDotRadius, 0f, step * 0.2f);
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 10; column++)
                {
                    float x = rect.xMax - step * (column + 0.5f);
                    float y = rect.yMin + step * (row + 0.5f);
                    if (x - radius < rect.center.x || y + radius > rect.yMin + rect.height * 0.3f) continue;
                    DrawDot(vh, new Vector2(x, y), radius, ink);
                }
            Color pink = settings.status;
            pink.a *= alpha;
            Slant(vh, new Rect(rect.xMax - rect.width * 0.22f, rect.yMin,
                rect.width * 0.22f, Mathf.Min(8f, rect.height * 0.06f)), pink);
        }
    }

    private static void DrawDot(VertexHelper vh, Vector2 center, float radius, Color tint)
    {
        if (radius <= 0f || tint.a <= 0f) return;
        int first = vh.currentVertCount;
        vh.AddVert(center, tint, Vector2.zero);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.PI / 3f;
            vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
        }
        for (int i = 0; i < 6; i++)
            vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % 6);
    }

    private static Rect Border(VertexHelper vh, Rect rect, float width, Color tint)
    {
        width = Mathf.Clamp(width, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
        Rect inner = Inset(rect, width);
        Quad(vh, new Rect(rect.xMin, rect.yMin, rect.width, width), tint);
        Quad(vh, new Rect(rect.xMin, rect.yMax - width, rect.width, width), tint);
        Quad(vh, new Rect(rect.xMin, inner.yMin, width, inner.height), tint);
        Quad(vh, new Rect(rect.xMax - width, inner.yMin, width, inner.height), tint);
        return inner;
    }

    internal static void DrawMahoFrame(VertexHelper vh, Rect rect, BattleUiStyleSettings settings, float alpha)
    {
        Color shadow = settings.mahoShadow, rim = settings.outerFrame, paper = settings.background;
        shadow.a *= alpha * 0.5f;
        rim.a *= alpha;
        paper.a *= alpha;
        Vector2 offset = settings.mahoShadowOffset;
        RoundedFill(vh, new Rect(rect.position + offset * 2f, rect.size), settings.mahoCornerRadius, shadow);
        shadow.a *= 2f;
        RoundedFill(vh, new Rect(rect.position + offset, rect.size), settings.mahoCornerRadius, shadow);
        RoundedFill(vh, rect, settings.mahoCornerRadius, rim);
        RoundedFill(vh, Inset(rect, settings.mahoBorderWidth),
            Mathf.Max(0f, settings.mahoCornerRadius - settings.mahoBorderWidth), paper);
    }

    internal static void RoundedFill(VertexHelper vh, Rect rect, float radius, Color tint)
    {
        if (rect.width <= 0f || rect.height <= 0f || tint.a <= 0f) return;
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
        if (radius <= 0f) { Quad(vh, rect, tint); return; }
        int first = vh.currentVertCount;
        const int steps = 8, count = 4 * (steps + 1);
        vh.AddVert(rect.center, tint, Vector2.zero);
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 center = new(corner < 2 ? rect.xMax - radius : rect.xMin + radius,
                corner == 0 || corner == 3 ? rect.yMin + radius : rect.yMax - radius);
            for (int step = 0; step <= steps; step++)
            {
                float angle = (-90f + corner * 90f + step * 90f / steps) * Mathf.Deg2Rad;
                vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
            }
        }
        for (int i = 0; i < count; i++)
            vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % count);
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

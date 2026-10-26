using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal enum BattlePaperRole { Default, Inventory, ItemDetail }

[DisallowMultipleComponent]
public sealed class BattleUiStyleMeshEffect : BaseMeshEffect
{
    private Graphic target;
    private bool lockMark;
    private Text label;
    private TMPro.TextMeshProUGUI nativeText;
    private Color originalTmpColor, appliedTmpColor;
    private bool tmpOverridden;
    private TMPro.TMP_FontAsset originalCmdFont, appliedCmdFont;
    private TMPro.TMP_FontAsset checkedCmdFont;
    private string checkedCmdText;
    private readonly List<TMPro.TMP_SubMeshUI> nativeSubMeshes = new();
    private bool nativeSubMeshesPending;
    private Material originalCmdMaterial;
    private TMPro.TextAlignmentOptions originalCmdAlignment;
    private TMPro.FontStyles originalCmdFontStyle;
    private bool cmdNativeOverridden, wasCmd;
    private BattleUiCmdFrame cmdFrame;
    private BattleUiPanelDecoration cmdSelection;
    private RectTransform speechSurface;
    private bool speechBadge;
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
    private bool wasStylish, wasBirthday, wasPaper, title;
    private uint paperSeed;
    private bool paperBadge, paperBadgeInner, paperBadgeIcon;
    private BattlePaperRole paperRole;
    private bool paperSheet;
    private BattleUiStyleTmpTitle tmpTitle;
    private bool titleMotionCandidate;
    private BattleUiStyleInteraction interaction;
    private bool stylishButton, stylishSurface, stylishCard, stylishSpotlight;
    private float enteredAt = -1f;
    private float entrance = 1f;
    internal bool HasOverflowTitle => TitleMotion(BattleUiStyleController.Settings)?.allowOverflow == true;
    internal bool HasReplacementText => tmpTitle != null && tmpTitle.Visible;
    internal TMPro.TextMeshProUGUI CmdText => tmpTitle != null && tmpTitle.Visible ? tmpTitle.Text : null;
    private bool projectPixels;
    private readonly List<UIVertex> triangles = new();
    private Matrix4x4 lastTransform, lastCamera;
    private Rect lastViewport;
    private Camera renderCamera;
    private Rect viewport;

    internal void Bind(Graphic value)
    {
        target = value;
        lockMark = value is Image && value.name.StartsWith("LockMark_", System.StringComparison.Ordinal);
        paperSeed = PaperSeed(value.transform);
        Transform paperOwner = value is BattleUiPanelDecoration ? value.transform.parent : value.transform;
        paperRole = PaperRole(paperOwner);
        paperSheet = IsPaperSheet(paperOwner);
        label = value as Text;
        nativeText = value as TMPro.TextMeshProUGUI;
        if (nativeText != null)
        {
            nativeText.OnPreRenderText -= PrepareNativeSubMeshes;
            nativeText.OnPreRenderText += PrepareNativeSubMeshes;
        }
        title = (label != null || nativeText != null) && (value.name == "AppHeader" || value.name.EndsWith("Title", System.StringComparison.Ordinal) ||
            value.name.EndsWith("Header", System.StringComparison.Ordinal));
        titleMotionCandidate = title && label != null && label.GetComponentInParent<ScrollRect>(true) == null;
        if (label != null)
            for (Transform node = label.transform.parent; node != null; node = node.parent)
            {
                Transform child = node.Find("PanelDecoration");
                if (child != null && IsCard(node)) { cmdSelection = child.GetComponent<BattleUiPanelDecoration>(); break; }
                if (node.TryGetComponent<Canvas>(out _)) break;
            }
        image = value as Image;
        speechBadge = image != null && (image.name == "PresenterNameBadge" || image.name == "ReactionTagBadge");
        if (image != null)
        {
            Transform badge = image.name == "Inner" ? image.transform.parent :
                image.name == "Icon" ? image.transform.parent?.parent : image.transform;
            if (badge != null && badge.name.StartsWith("Badge_", System.StringComparison.Ordinal) &&
                badge.parent != null && badge.parent.name == "ItemAttributeBadges")
            {
                paperBadge = image.transform == badge;
                paperBadgeInner = image.name == "Inner";
                paperBadgeIcon = image.name == "Icon";
                paperSeed = PaperSeed(badge);
            }
        }
        stylishCard = IsCard(value.transform);
        stylishSpotlight = IsSpotlight(value.transform);
        stylishSurface = image != null && IsSurface(image);
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
        BindInteraction();
    }

    internal static bool IsCard(Transform value) => value != null &&
        (value.name.Contains("Slot") || value.name.Contains("Card") || value.name.Contains("BackpackCell"));

    internal static bool IsSpotlight(Transform value)
    {
        for (Transform node = value; node != null; node = node.parent)
            if (node.name.Contains("Result") || node.name.Contains("Inspect") || node.name.Contains("Detail")) return true;
        return false;
    }

    private static bool IsSurface(Image value)
    {
        if (value.type == Image.Type.Filled || value.GetComponent<Mask>() != null) return false;
        string key = value.name;
        if (key.Contains("Dim") || key.Contains("Overlay") || key.Contains("Fade") ||
            key.Contains("Viewport") || key.Contains("Icon") || key.Contains("Thumbnail")) return false;
        if (value.sprite != null) return false;
        Rect rect = value.rectTransform.rect;
        return rect.width >= 120f && rect.height >= 48f && rect.height < 1000f;
    }

    internal void BindInteraction()
    {
        if (target == null) return;
        interaction = BattleUiStyleInteraction.Bind(target);
        if (image != null && interaction != null && image.transform == interaction.transform)
        {
            stylishButton = true;
            Transform child = image.transform.Find("PanelDecoration");
            if (child != null) decoration = child.GetComponent<BattleUiPanelDecoration>();
            decoratedSurface = decoration != null;
            panel |= decoratedSurface;
        }
        if (target is BattleUiPanelDecoration panelDecoration) panelDecoration.SetStyleInteraction(interaction);
    }

    internal void SetSpeechFrame(bool fill, RectTransform surface)
    {
        speechFrame = !fill;
        hiddenFill = fill;
        panel = !fill;
        SetSpeechSurface(surface);
    }

    internal void SetSpeechSurface(RectTransform surface)
    {
        speechSurface = surface;
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
        Material pixelPanel, Material pixelText, Material pixelSprite, Font bitmap, Material mahoMaterial, Font themeFont,
        Material paperMaterial, Material paperMaskMaterial)
    {
        bool cmd = BattleUiStyleController.IsCmd;
        if (cmd && !PreserveBackdrop() && (decoration == target || (image != null && !decoratedSurface &&
            (panel || speechBadge || (image.sprite == null && (stylishSurface || stylishButton))))))
        {
            if (cmdFrame == null) cmdFrame = new BattleUiCmdFrame(target);
            cmdFrame.Apply(target, settings, decoration != null ? decoration.CmdSelected : interaction != null && interaction.Highlighted);
        }
        else cmdFrame?.Hide();
        if (nativeText != null)
        {
            ApplyNativeText(settings);
            wasCmd = cmd;
            return;
        }
        if (PreserveBackdrop())
        {
            RestoreMaterial();
            projectPixels = false;
            return;
        }
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
        bool paper = BattleUiStyleController.IsPaper;
        float previousEntrance = entrance;
        entrance = 1f;
        if (Application.isPlaying && stylish && (panel || stylishSurface || stylishButton || label != null || decoration != null))
        {
            if (enteredAt < 0f) enteredAt = Time.unscaledTime;
            entrance = Mathf.SmoothStep(0.02f, 1f,
                Mathf.Clamp01((Time.unscaledTime - enteredAt) / Mathf.Max(0.01f, settings.stylishEnterDuration)));
        }
        else enteredAt = -1f;
        if (entrance != previousEntrance || entrance < 1f) target.SetVerticesDirty();
        if ((stylish || birthday) && next == null && (panel || (stylish && (stylishButton || stylishSurface))) && !statusFill) next = pixelPanel;
        if (paper && (panel || paperBadge || speechBadge || (image != null && image.sprite == null && (stylishSurface || stylishButton))) && !statusFill)
            next = paperMaterial != null ? paperMaterial : pixelPanel;
        if (paper && (speechFrame || hiddenFill || tailFill || (image != null && image.name == "TailShape")))
            next = paperMaskMaterial != null ? paperMaskMaterial : target.defaultMaterial;
        if (cmd && (label != null || panel || speechBadge || hiddenFill || tailFill ||
            (image != null && (image.name == "TailShape" || (image.sprite == null && (stylishSurface || stylishButton))))))
            next = target.defaultMaterial;
        if (HasAssignedMaterial()) next = null;
        if (lockMark) next = target.defaultMaterial;
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
            wasStylish != stylish || wasBirthday != birthday || wasPaper != paper || wasCmd != cmd) target.SetVerticesDirty();
        wasPixel = pixel;
        wasComic = comic;
        wasMaho = maho;
        wasStylish = stylish;
        wasBirthday = birthday;
        wasPaper = paper;
        wasCmd = cmd;
        bool decoratedTitle = title && (stylish || birthday || paper || cmd);
        if (isActiveAndEnabled && label != null && settings.textMaterial == null &&
            (settings.font == null || settings.fontAsset != null || (decoratedTitle && settings.titleFont != null)) && !HasAssignedMaterial())
        {
            TMPro.TMP_FontAsset font = decoratedTitle && settings.titleFont != null ? settings.titleFont :
                settings.fontAsset != null ? settings.fontAsset : BattleUiStyleController.ThemeTmpFont;
            if (font != null && (cmd || stylish || paper || BattleUiOpticsCanvas.ActiveCapture != null || label.canvas == null ||
                label.canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay))
            {
                if (tmpTitle == null) tmpTitle = new BattleUiStyleTmpTitle(label);
                BattleUiTitleMotionSettings motion = TitleMotion(settings);
                tmpTitle.Apply(font, settings, birthday, decoratedTitle, motion, motion != null && motion.allowOverflow,
                    stylish ? interaction?.Scale ?? 1f : 1f, entrance, stylish && interaction != null && interaction.Primary,
                    interaction != null || (cmdSelection != null && title),
                    (interaction != null && interaction.Highlighted) || (cmdSelection != null && cmdSelection.CmdSelected));
            }
            else HideTmpTitle();
        }
        else HideTmpTitle();
        if (paper && next != null && (next == paperMaterial || next == paperMaskMaterial) && target.canvas != null)
        {
            Canvas canvas = target.canvas;
            if ((canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1) == 0)
                canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            Matrix4x4 transformMatrix = PaperCanvasTransform(target);
            if (lastTransform != transformMatrix)
            {
                lastTransform = transformMatrix;
                target.SetVerticesDirty();
            }
        }
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

    private void PrepareNativeSubMeshes(TMPro.TMP_TextInfo info)
    {
        if (!BattleUiStyleController.IsCmd || info.materialCount <= 1) return;
        nativeText.GetComponentsInChildren(true, nativeSubMeshes);
        foreach (TMPro.TMP_SubMeshUI subMesh in nativeSubMeshes)
        {
            subMesh.raycastTarget = false;
            subMesh.maskable = nativeText.maskable;
        }
        nativeSubMeshesPending = true;
    }

    private void ApplyNativeText(BattleUiStyleSettings settings)
    {
        if (nativeSubMeshesPending)
        {
            foreach (TMPro.TMP_SubMeshUI subMesh in nativeSubMeshes)
            {
                if (subMesh == null) continue;
                subMesh.gameObject.layer = nativeText.gameObject.layer;
                BattleUiOpticsCanvas.Register(subMesh);
            }
            nativeSubMeshesPending = false;
        }
        bool cmd = BattleUiStyleController.IsCmd;
        if (!cmd) RestoreCmdNative();
        if (!BattleUiStyleController.IsPaper && !cmd)
        {
            RestoreNativeText();
            return;
        }
        if (!tmpOverridden || nativeText.color != appliedTmpColor) originalTmpColor = nativeText.color;
        appliedTmpColor = settings.innerLine;
        appliedTmpColor.a *= originalTmpColor.a;
        if (nativeText.color != appliedTmpColor) nativeText.color = appliedTmpColor;
        tmpOverridden = true;
        if (!cmd) return;
        TMPro.TMP_FontAsset font = settings.fontAsset != null ? settings.fontAsset : BattleUiThemeFonts.LoadTmp(BattleUiStyle.CMD);
        if (font == null) return;
        if (checkedCmdFont != font || checkedCmdText != nativeText.text || appliedCmdFont == null)
        {
            checkedCmdFont = font;
            checkedCmdText = nativeText.text;
            appliedCmdFont = BattleUiThemeFonts.ResolveCmdTmp(font, checkedCmdText);
        }
        font = appliedCmdFont;
        if (!cmdNativeOverridden)
        {
            originalCmdFont = nativeText.font;
            originalCmdMaterial = nativeText.fontSharedMaterial;
            originalCmdAlignment = nativeText.alignment;
            originalCmdFontStyle = nativeText.fontStyle;
        }
        if (nativeText.font != font) nativeText.font = font;
        if (nativeText.fontSharedMaterial != font.material) nativeText.fontSharedMaterial = font.material;
        appliedCmdFont = font;
        nativeText.fontStyle = TMPro.FontStyles.Normal;
        if (title || nativeText.name == "PresenterName") nativeText.alignment = TMPro.TextAlignmentOptions.Left;
        cmdNativeOverridden = true;
    }

    private void RestoreCmdNative()
    {
        if (!cmdNativeOverridden || nativeText == null) return;
        if (nativeText.font == appliedCmdFont)
        {
            nativeText.font = originalCmdFont;
            nativeText.fontSharedMaterial = originalCmdMaterial;
        }
        nativeText.alignment = originalCmdAlignment;
        nativeText.fontStyle = originalCmdFontStyle;
        cmdNativeOverridden = false;
    }

    private void RestoreNativeText()
    {
        RestoreCmdNative();
        if (!tmpOverridden || nativeText == null) return;
        if (nativeText.color == appliedTmpColor) nativeText.color = originalTmpColor;
        tmpOverridden = false;
    }

    internal void ReleaseMaterialOverride() => RestoreMaterial();

    private bool HasAssignedMaterial()
    {
        if (BattleUiStyleController.IsCmd && (label != null || panel || speechBadge || hiddenFill || tailFill ||
            (image != null && (image.name == "TailShape" || (image.sprite == null && (stylishSurface || stylishButton)))))) return false;
        if (BattleUiStyleController.IsStylish && (panel || stylishSurface || stylishButton || label != null)) return false;
        if (BattleUiStyleController.IsPaper && (panel || speechFrame || hiddenFill || tailFill ||
            (image != null && image.name == "TailShape") || paperBadge || paperBadgeInner || paperBadgeIcon || speechBadge || label != null ||
            (image != null && image.sprite == null && (stylishSurface || stylishButton)))) return false;
        return BattleUiMaterialController.IsAssignedMaterial(target.material) ||
            (BattleShowPresentationManager.Instance != null &&
             BattleShowPresentationManager.Instance.HasAssignedUiMaterial(target));
    }

    private bool PreserveBackdrop()
    {
        if (image == null || target.canvas == null) return false;
        string key = image.name;
        if (key.Contains("Dim") || key.Contains("Overlay") || key.Contains("Fade") || key.Contains("Viewport")) return true;
        Rect bounds = ((RectTransform)target.canvas.rootCanvas.transform).rect;
        Rect rect = image.rectTransform.rect;
        return bounds.width > 0f && bounds.height > 0f &&
            rect.width >= bounds.width * 0.9f && rect.height >= bounds.height * 0.9f;
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
        cmdFrame?.Hide();
        RestoreNativeText();
        enteredAt = -1f;
        entrance = 1f;
        HideTmpTitle();
        RestoreMaterial();
        RestoreFont();
        RestoreFontStyle();
        if (target != null) target.SetVerticesDirty();
    }

    internal void HideTmpTitle() => tmpTitle?.Hide();
    internal void HideAppearance()
    {
        enteredAt = -1f;
        HideTmpTitle();
        cmdFrame?.Hide();
        if (wasCmd)
        {
            RestoreNativeText();
            RestoreMaterial();
            RestoreFont();
            RestoreFontStyle();
        }
    }
    internal void RefreshTmpTitle() => tmpTitle?.Refresh();

    private BattleUiTitleMotionSettings TitleMotion(BattleUiStyleSettings settings)
    {
        BattleUiTitleMotionSettings motion = settings?.stylishTitleMotion;
        return titleMotionCandidate && BattleUiStyleController.IsStylish && motion != null && motion.enabled &&
            target.name == motion.targetName && target.transform.parent != null && target.transform.parent.name == motion.targetParent
            ? motion : null;
    }

    protected override void OnDisable()
    {
        cmdFrame?.Hide();
        RestoreNativeText();
        HideAppearance();
        base.OnDisable();
    }

    protected override void OnDestroy()
    {
        if (nativeText != null) nativeText.OnPreRenderText -= PrepareNativeSubMeshes;
        cmdFrame?.Dispose();
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
        if (lockMark)
        {
            DrawLockMark(vh);
            return;
        }
        ModifyStyledMesh(vh);
        if (image != null && decoration != null)
            decoration.ApplyBackgroundOpacity(vh, vh.currentVertCount, image.color.a);
        if (BattleUiStyleController.IsStylish && !HasAssignedMaterial())
        {
            float scale = interaction != null ? interaction.Scale : 1f;
            if (panel || stylishSurface || stylishButton || target is BattleUiPanelDecoration) scale *= Mathf.Lerp(0.94f, 1f, entrance);
            Vector3 center = interaction != null
                ? target.rectTransform.InverseTransformPoint(interaction.transform.TransformPoint(((RectTransform)interaction.transform).rect.center))
                : (Vector3)target.rectTransform.rect.center;
            UIVertex vertex = default;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                vertex.position = center + (vertex.position - center) * scale;
                Color color = vertex.color;
                color.a *= entrance;
                vertex.color = color;
                vh.SetUIVertex(vertex, i);
            }
        }
    }

    private void ModifyStyledMesh(VertexHelper vh)
    {
        if (!IsActive() || target == null || nativeText != null || PreserveBackdrop() || HasAssignedMaterial()) return;
        if (tmpTitle != null && tmpTitle.Visible) { vh.Clear(); return; }
        if (BattleUiStyleController.IsCmd) { ModifyCmdMesh(vh); return; }
        bool pixel = BattleUiStyleController.IsPixel;
        bool comic = BattleUiStyleController.IsComic;
        bool maho = BattleUiStyleController.IsMahoShojo;
        bool stylish = BattleUiStyleController.IsStylish;
        bool birthday = BattleUiStyleController.IsBirthday;
        bool paper = BattleUiStyleController.IsPaper;
        if (!pixel && !comic && !maho && !stylish && !birthday && !paper) return;
        BattleUiStyleSettings settings = BattleUiStyleController.Settings;
        if (paper && paperBadgeInner) { vh.Clear(); return; }
        if (paper && (speechFrame || hiddenFill || tailFill || (image != null && image.name == "TailShape")))
        {
            RectTransform surface = speechSurface != null ? speechSurface : target.rectTransform;
            Matrix4x4 canvas = surface == target.rectTransform ? PaperCanvasTransform(target) :
                (target.canvas != null ? target.canvas.rootCanvas.transform.worldToLocalMatrix : Matrix4x4.identity) * surface.localToWorldMatrix;
            Vector3 across = canvas.MultiplyVector(Vector3.right);
            float width = surface.rect.width * across.magnitude;
            across.Normalize();
            Vector3 down = new(across.y, -across.x, 0f);
            Vector3 origin = canvas.MultiplyPoint3x4(new Vector3(surface.rect.xMin, surface.rect.yMax, 0f));
            Matrix4x4 vertices = PaperCanvasTransform(target);
            uint seed = PaperSeed(surface);
            UIVertex vertex = default;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                Color tint = speechFrame || (image != null && image.name == "TailShape")
                    ? settings.outerFrame : settings.background;
                tint.a *= vertex.color.a / 255f;
                vertex.color = tint;
                Vector3 offset = vertices.MultiplyPoint3x4(vertex.position) - origin;
                vertex.uv0 = new Vector4(vertex.uv0.x, vertex.uv0.y, PaperVariation(seed, 24) * 0.99f, PaperVariation(seed, 25));
                vertex.uv1 = new Vector4(Vector3.Dot(offset, across), Vector3.Dot(offset, down), width,
                    speechFrame || hiddenFill ? 1f : 0f);
                vh.SetUIVertex(vertex, i);
            }
            return;
        }
        // 접합 계산에 쓰는 프레임 마스크와 표시되는 외곽을 일치시킵니다.
        if (hiddenFill || tailFill || (image != null && image.name == "TailShape") || (speechFrame && !stylish)) return;
        if (decoratedSurface) { vh.Clear(); return; }
        bool statusFill = image != null && image.type == Image.Type.Filled;
        if ((panel || (paper && (paperBadge || speechBadge)) || ((stylish || paper) && (stylishButton || stylishSurface))) && image != null && !statusFill &&
            (!paper || image.sprite == null || panel || paperBadge))
        {
            vh.Clear();
            Color fill = settings.background;
            fill.a *= speechFrame ? 1f : image.color.a;
            if (paper)
                DrawPaperFrame(vh, target.rectTransform.rect, settings, image.color.a, paperSeed,
                    paperBadge, interaction != null && interaction.Highlighted, target, paperRole, paperSheet);
            else if (stylish || birthday)
                DrawAccentFrame(vh, target.rectTransform.rect, settings, birthday, speechFrame ? 1f : image.color.a,
                    (stylish && stylishSpotlight) || (interaction != null && interaction.Highlighted),
                    interaction != null && interaction.Primary, stylishCard);
            else if (maho) DrawMahoFrame(vh, target.rectTransform.rect, settings, fill.a);
            else if (comic) DrawComicFrame(vh, target.rectTransform.rect, settings, fill.a);
            else DrawFrame(vh, target.rectTransform.rect, settings, fill, PixelStep(target.canvas, settings));
        }
        else if (label != null || tailFill || statusFill || (maho && image != null) || (paper && paperBadgeIcon))
        {
            UIVertex vertex = default;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                Color source = vertex.color;
                Color tint = paper ? (label != null || paperBadgeIcon ? settings.innerLine : Palette(source, settings, true)) :
                    stylish || birthday ? (tailFill ? settings.background :
                    label != null ? (stylish && interaction != null && interaction.Primary ? settings.outerFrame : settings.innerLine) :
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

    private void ModifyCmdMesh(VertexHelper vh)
    {
        if (target is BattleUiPanelDecoration) return;
        BattleUiStyleSettings settings = BattleUiStyleController.Settings;
        bool tailOutline = image != null && image.name == "TailShape";
        if (speechFrame || hiddenFill || tailFill || tailOutline)
        {
            Color tint = speechFrame || tailOutline ? settings.outerFrame : settings.background;
            UIVertex vertex = default;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                Color color = tint;
                color.a *= vertex.color.a / 255f;
                vertex.color = color;
                vh.SetUIVertex(vertex, i);
            }
            return;
        }
        if (decoratedSurface)
        {
            vh.Clear();
            return;
        }
        if (image != null && image.type != Image.Type.Filled &&
            (panel || speechBadge || (image.sprite == null && (stylishSurface || stylishButton))))
        {
            vh.Clear();
            Color fill = settings.background;
            fill.a *= image.color.a;
            Quad(vh, target.rectTransform.rect, fill);
        }
        else if (label != null || (image != null && image.type == Image.Type.Filled))
        {
            UIVertex vertex = default;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                Color tint = label != null ? settings.innerLine : settings.status;
                tint.a *= vertex.color.a / 255f;
                vertex.color = tint;
                vh.SetUIVertex(vertex, i);
            }
        }
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
            BattleUiStyleController.IsMahoShojo || BattleUiStyleController.IsStylish || BattleUiStyleController.IsBirthday || BattleUiStyleController.IsPaper || BattleUiStyleController.IsCmd
                ? settings.innerLine : BattleUiStyleController.IsPixel ? Palette(source, settings, false) : source;
        if (BattleUiStyleController.IsPixel || BattleUiStyleController.IsComic || BattleUiStyleController.IsMahoShojo ||
            BattleUiStyleController.IsStylish || BattleUiStyleController.IsBirthday || BattleUiStyleController.IsPaper || BattleUiStyleController.IsCmd) tint.a *= source.a;
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
        bool birthday, float alpha, bool selected = false, bool primary = false, bool card = false)
    {
        if (!birthday)
        {
            DrawStylishFrame(vh, rect, settings, alpha, selected, primary, card);
            return;
        }
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

    private static void DrawStylishFrame(VertexHelper vh, Rect rect, BattleUiStyleSettings settings,
        float alpha, bool selected, bool primary, bool card)
    {
        float border = Mathf.Min(Mathf.Clamp(settings.stylishBorderWidth, 3f, 6f), Mathf.Min(rect.width, rect.height) * 0.12f);
        float shadowOffset = Mathf.Clamp(settings.stylishShadowOffset, 4f, 8f);
        Rect shadow = new(rect.x + shadowOffset, rect.y - shadowOffset, rect.width, rect.height);
        Color ink = settings.outerFrame;
        ink.a *= alpha;
        StylishFill(vh, shadow, settings, ink, card || primary);
        if (selected)
        {
            float glow = Mathf.Clamp(settings.stylishGlowWidth, 6f, 12f);
            for (int i = 2; i > 0; i--)
            {
                Color light = settings.stylishNeon;
                light.a *= alpha * (i == 2 ? 0.08f : 0.18f);
                float spread = glow * i * 0.5f;
                StylishFill(vh, new Rect(rect.x - spread, rect.y - spread,
                    rect.width + spread * 2f, rect.height + spread * 2f), settings, light, card || primary);
            }
        }
        StylishFill(vh, rect, settings, ink, card || primary);
        Rect rim = Inset(rect, border);
        Color neon = selected ? settings.stylishNeon : primary ? settings.highlight : settings.energy;
        neon.a *= alpha;
        StylishFill(vh, rim, settings, neon, card || primary);
        Rect face = Inset(rim, Mathf.Max(1f, border * 0.4f));
        Color fill = primary ? settings.stylishCta :
            selected ? settings.stylishMagenta : settings.background;
        fill.a *= alpha;
        StylishFill(vh, face, settings, fill, card || primary);
        if (!primary && face.width > 240f && face.height > 120f)
        {
            Color mint = settings.energy;
            mint.a *= alpha * settings.stylishColorBandAlpha;
            DrawColorWave(vh, Inset(face, border * 2f), settings.stylishColorBandHeight, mint);
            Color yellow = settings.highlight;
            yellow.a *= alpha * 0.8f;
            Rect ribbon = new(face.xMax - face.width * 0.24f, face.yMax - border * 4f,
                face.width * 0.18f, border * 1.5f);
            AccentSlant(vh, ribbon, settings.stylishPanelAngle, yellow);
            Color pinkBand = settings.status;
            pinkBand.a *= alpha * settings.stylishColorBandAlpha;
            AccentSlant(vh, new Rect(face.xMax - face.width * 0.2f, face.yMin + border,
                face.width * 0.16f, face.height * 0.42f), settings.stylishPanelAngle, pinkBand);
        }
        Color pink = settings.status;
        pink.a *= alpha;
        AccentSlant(vh, new Rect(face.xMax - face.width * 0.18f, face.yMin,
            face.width * 0.18f, Mathf.Min(8f, face.height * 0.12f)), settings.stylishPanelAngle, pink);
        float step = Mathf.Max(4f, settings.stylishDotSpacing);
        Color dot = settings.innerLine;
        dot.a *= alpha * settings.surfacePatternAlpha;
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 8; column++)
            {
                Vector2 point = new(face.xMax - step * (column + 1), face.yMin + step * (row + 1));
                if (point.x < face.center.x || point.y > face.center.y) continue;
                DrawDot(vh, point, settings.stylishDotRadius, dot);
            }
    }

    private static void StylishFill(VertexHelper vh, Rect rect, BattleUiStyleSettings settings, Color tint, bool rounded)
    {
        if (rounded) RoundedFill(vh, rect, settings.stylishCardRadius, tint);
        else AccentSlant(vh, rect, settings.stylishPanelAngle, tint);
    }

    private static void DrawColorWave(VertexHelper vh, Rect rect, float height, Color tint)
    {
        if (rect.width <= 0f || rect.height <= 0f || tint.a <= 0f) return;
        const int segments = 20;
        int first = vh.currentVertCount;
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float x = Mathf.Lerp(rect.xMin, rect.xMax, t);
            float y = rect.yMin + rect.height * height * (0.65f + 0.35f * Mathf.Sin(t * Mathf.PI * 2f));
            vh.AddVert(new Vector3(x, rect.yMin, 0f), tint, Vector2.zero);
            vh.AddVert(new Vector3(x, y, 0f), tint, Vector2.zero);
            if (i == 0) continue;
            int index = first + i * 2;
            vh.AddTriangle(index - 2, index - 1, index + 1);
            vh.AddTriangle(index - 2, index + 1, index);
        }
    }

    internal static void AccentSlant(VertexHelper vh, Rect rect, float angle, Color tint)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        float cut = Mathf.Min(rect.width * 0.12f, rect.height * Mathf.Tan(angle * Mathf.Deg2Rad));
        int first = vh.currentVertCount;
        vh.AddVert(new Vector3(rect.xMin, rect.yMin, 0f), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMin + cut, rect.yMax, 0f), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax, rect.yMax, 0f), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax - cut, rect.yMin, 0f), tint, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
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

    internal static uint PaperSeed(Transform value)
    {
        unchecked
        {
            uint seed = 2166136261u;
            for (Transform node = value; node != null; node = node.parent)
            {
                foreach (char c in node.name) seed = (seed ^ c) * 16777619u;
                seed = (seed ^ (uint)node.GetSiblingIndex()) * 16777619u;
            }
            return seed;
        }
    }

    private static float PaperVariation(uint seed, int corner)
    {
        unchecked
        {
            uint value = seed + (uint)(corner + 1) * 2654435761u;
            value = (value ^ (value >> 16)) * 2246822519u;
            return (value & 65535u) / 65535f;
        }
    }

    internal static float PaperAngle(uint seed, float maximum) =>
        (PaperVariation(seed, 8) * 2f - 1f) * Mathf.Clamp(maximum, 0f, 2f);

    internal static BattlePaperRole PaperRole(Transform value)
    {
        for (Transform node = value; node != null; node = node.parent)
        {
            string key = node.name;
            if (key.Contains("Detail") || key.Contains("Inspect") || key == "CurrentItem")
                return BattlePaperRole.ItemDetail;
            if (key == "BackpackMiniGrid" || key == "CurrentLoadoutChip" || key == "GridBoard" || key == "BoardBack")
                return BattlePaperRole.Inventory;
        }
        return BattlePaperRole.Default;
    }

    internal static bool IsPaperSheet(Transform value)
    {
        if (value == null || value.GetComponent<Button>() != null) return false;
        string key = value.name;
        if (key.Contains("Slot") || key.Contains("Cell")) return false;
        return key == "BackpackMiniGrid" || key == "CurrentLoadoutChip" || key == "BoardBack" || key == "CurrentItem" ||
            key.EndsWith("Panel", System.StringComparison.Ordinal) || key.EndsWith("Board", System.StringComparison.Ordinal) ||
            key.Contains("Card");
    }

    private static Matrix4x4 PaperCanvasTransform(Graphic surface) => surface != null && surface.canvas != null
        ? surface.canvas.rootCanvas.transform.worldToLocalMatrix * surface.rectTransform.localToWorldMatrix
        : Matrix4x4.identity;

    internal static Color PaperColor(BattleUiStyleSettings settings, BattlePaperRole role) => role switch
    {
        BattlePaperRole.Inventory => settings.paperInventoryColor,
        BattlePaperRole.ItemDetail => settings.paperDetailColor,
        _ => settings.background
    };

    internal static void DrawPaperFrame(VertexHelper vh, Rect rect, BattleUiStyleSettings settings,
        float alpha, uint seed, bool badge = false, bool highlighted = false, Graphic surface = null,
        BattlePaperRole role = BattlePaperRole.Default, bool sheet = false)
    {
        float shortSide = Mathf.Min(rect.width, rect.height);
        if (shortSide <= 0f) return;
        float scale = Mathf.Min(1f, shortSide / 80f);
        float border = Mathf.Clamp(settings.paperBorderWidth * scale, 0f, shortSide * 0.2f);
        int first = vh.currentVertCount;
        sheet &= !badge;
        Color paper = PaperColor(settings, role);
        if (!badge) border *= settings.paperSheetBorderScale;
        Rect shadowRect = rect;
        shadowRect.position += settings.paperShadowOffset * scale;
        Color shadow = settings.paperShadow;
        shadow.a *= alpha;
        PaperFill(vh, shadowRect, settings, shadow, seed, badge, rect);
        if (sheet)
        {
            Rect backingRect = rect;
            backingRect.position += new Vector2(4f, -4f) * scale;
            Color backing = Color.Lerp(paper, settings.outerFrame, 0.12f);
            backing.a *= alpha;
            PaperFill(vh, backingRect, settings, backing, seed, sheetRect: rect);
        }
        Color rim = !badge ? Color.Lerp(paper, settings.outerFrame, highlighted ? 0.65f : 0.35f) : settings.outerFrame;
        rim.a *= alpha;
        PaperFill(vh, rect, settings, rim, seed, badge);
        Color fill = highlighted ? Color.Lerp(paper, settings.highlight, 0.14f) : paper;
        fill.a *= alpha;
        PaperFill(vh, Inset(rect, border), settings, fill, seed, badge, rect);
        ApplyPaperCoordinates(vh, first, rect, settings, seed, surface, role, sheet);
    }

    internal static void ApplyPaperCoordinates(VertexHelper vh, int first, Rect rect, BattleUiStyleSettings settings,
        uint seed, Graphic surface, BattlePaperRole role, bool punched)
    {
        float angle = PaperAngle(seed, settings.paperDecorationAngle) * Mathf.Deg2Rad;
        float cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle);
        float fit = Mathf.Min(rect.width / (rect.width * Mathf.Abs(cosine) + rect.height * Mathf.Abs(sine)),
            rect.height / (rect.height * Mathf.Abs(cosine) + rect.width * Mathf.Abs(sine)));
        Matrix4x4 canvas = PaperCanvasTransform(surface);
        Vector3 across = canvas.MultiplyVector(new Vector3(cosine, sine, 0f));
        across.z = 0f;
        float width = rect.width * fit * across.magnitude;
        across.Normalize();
        Vector3 down = new(across.y, -across.x, 0f);
        if (Vector3.Dot(down, canvas.MultiplyVector(new Vector3(sine, -cosine, 0f))) < 0f) down = -down;
        Vector2 topLeft = rect.center + new Vector2(-rect.width * cosine - rect.height * sine,
            -rect.width * sine + rect.height * cosine) * (0.5f * fit);
        Vector3 origin = canvas.MultiplyPoint3x4(topLeft);
        UIVertex vertex = default;
        for (int i = first; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            Vector3 offset = canvas.MultiplyPoint3x4(vertex.position) - origin;
            vertex.uv0 = new Vector4(Vector3.Dot(offset, across), Vector3.Dot(offset, down), vertex.uv0.z, vertex.uv0.w);
            vertex.uv1 = new Vector4(width, punched ? 1f : 0f, (float)role, 0f);
            vh.SetUIVertex(vertex, i);
        }
    }

    internal static void PaperFill(VertexHelper vh, Rect rect, BattleUiStyleSettings settings,
        Color tint, uint seed, bool badge = false, Rect sheetRect = default)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        float shortSide = Mathf.Min(rect.width, rect.height);
        if (sheetRect.width <= 0f) sheetRect = rect;
        float angle = PaperAngle(seed, settings.paperDecorationAngle) * Mathf.Deg2Rad;
        float cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle);
        float fit = Mathf.Min(sheetRect.width / (sheetRect.width * Mathf.Abs(cosine) + sheetRect.height * Mathf.Abs(sine)),
            sheetRect.height / (sheetRect.height * Mathf.Abs(cosine) + sheetRect.width * Mathf.Abs(sine)));
        float min = Mathf.Clamp(settings.paperCornerMin, 0.02f, 0.05f);
        float max = Mathf.Clamp(settings.paperCornerMax, min, 0.05f);
        int first = vh.currentVertCount;
        int count = badge ? 7 + (int)((seed >> 3) % 3u) : 8;
        Vector2 textureSeed = new(PaperVariation(seed, 24) * 0.99f, PaperVariation(seed, 25));
        PaperSheetVertex(vh, rect.center, sheetRect, cosine, sine, fit, tint, textureSeed);
        for (int i = 0; i < count; i++)
        {
            Vector2 a = PaperCorner(rect, seed, i, count, badge, min, max);
            Vector2 b = PaperCorner(rect, seed, (i + 1) % count, count, badge, min, max);
            int steps = badge || i % 2 == 0 ? 1 : Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(a, b) / 70f), 2, 8);
            Vector2 normal = new Vector2(-(b.y - a.y), b.x - a.x).normalized;
            float wave = shortSide * Mathf.Lerp(min, max, PaperVariation(seed, i + 32)) * 0.5f;
            for (int step = 0; step < steps; step++)
            {
                float t = (float)step / steps;
                Vector2 point = Vector2.Lerp(a, b, t);
                point += normal * wave * Mathf.Sin(t * Mathf.PI) *
                    (0.6f + 0.4f * Mathf.Sin(t * Mathf.PI * 2f + PaperVariation(seed, i + 40) * Mathf.PI));
                PaperSheetVertex(vh, point, sheetRect, cosine, sine, fit, tint, textureSeed);
            }
        }
        int edgeCount = vh.currentVertCount - first - 1;
        for (int i = 0; i < edgeCount; i++)
            vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % edgeCount);
    }

    private static Vector2 PaperCorner(Rect rect, uint seed, int i, int count, bool badge, float min, float max)
    {
        float shortSide = Mathf.Min(rect.width, rect.height);
        float cut = shortSide * Mathf.Lerp(min, max, PaperVariation(seed, i));
        float edge = shortSide * Mathf.Lerp(min, max, PaperVariation(seed, i + 12));
        if (badge)
        {
            float phase = (i * 360f / count + 22.5f + (PaperVariation(seed, i + 12) - 0.5f) * 8f) * Mathf.Deg2Rad;
            return rect.center + new Vector2(Mathf.Cos(phase) * (rect.width * 0.5f - cut),
                Mathf.Sin(phase) * (rect.height * 0.5f - edge));
        }
        return i switch
        {
            0 => new Vector2(rect.xMin + edge * 0.15f, rect.yMin + cut),
            1 => new Vector2(rect.xMin + cut, rect.yMin + edge * 0.15f),
            2 => new Vector2(rect.xMax - cut, rect.yMin + edge * 0.15f),
            3 => new Vector2(rect.xMax - edge * 0.15f, rect.yMin + cut),
            4 => new Vector2(rect.xMax - edge * 0.15f, rect.yMax - cut),
            5 => new Vector2(rect.xMax - cut, rect.yMax - edge * 0.15f),
            6 => new Vector2(rect.xMin + cut, rect.yMax - edge * 0.15f),
            _ => new Vector2(rect.xMin + edge * 0.15f, rect.yMax - cut)
        };
    }

    private static void PaperSheetVertex(VertexHelper vh, Vector2 point, Rect rect, float cosine, float sine,
        float fit, Color tint, Vector2 seed)
    {
        Vector2 uv = new(point.x - rect.xMin, rect.yMax - point.y);
        point -= rect.center;
        point = rect.center + new Vector2(point.x * cosine - point.y * sine,
            point.x * sine + point.y * cosine) * fit;
        PaperVertex(vh, point, tint, uv, seed);
    }

    private static void PaperVertex(VertexHelper vh, Vector2 point, Color tint, Vector2 uv, Vector2 seed)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = point;
        vertex.color = tint;
        vertex.uv0 = new Vector4(uv.x, uv.y, seed.x, seed.y);
        vh.AddVert(vertex);
    }

    private void DrawLockMark(VertexHelper vh)
    {
        const int PixelSteps = 9, CmdRows = 7, CmdColumns = 5, PaperSegments = 8;
        const float ComicWeight = 2f, StylishWeight = 0.55f, BirthdayWeight = 1.8f;
        const float PaperWave = 0.018f, PaperWidthVariation = 0.16f;
        vh.Clear();
        BattleUiStyleSettings settings = BattleUiStyleController.Settings;
        if (settings == null) return;
        Rect rect = target.rectTransform.rect;
        float size = Mathf.Min(rect.width, rect.height) * settings.lockMarkSizeRatio;
        if (size <= 0f) return;
        Color tint = BattleUiStyleController.IsComic ? settings.comicPaper : settings.innerLine;
        tint.a *= settings.lockMarkAlpha * target.color.a;
        Vector2 center = rect.center;
        if (BattleUiStyleController.IsPixel || BattleUiStyleController.IsCmd)
        {
            bool cmd = BattleUiStyleController.IsCmd;
            int steps = cmd ? CmdRows : PixelSteps;
            float step = size / steps;
            for (int i = 0; i < steps; i++)
            {
                int column = cmd ? Mathf.RoundToInt(i * (CmdColumns - 1f) / (CmdRows - 1f)) : i;
                float glyphWidth = cmd ? CmdColumns * step : size;
                float x = center.x - glyphWidth * 0.5f + column * step;
                float y = center.y - size * 0.5f + i * step;
                Quad(vh, new Rect(x, y, step, step), tint);
                if (i != steps / 2)
                    Quad(vh, new Rect(center.x + glyphWidth * 0.5f - (column + 1) * step, y, step, step), tint);
            }
            return;
        }
        bool paper = BattleUiStyleController.IsPaper;
        bool round = paper || BattleUiStyleController.IsMahoShojo || BattleUiStyleController.IsBirthday;
        float weight = BattleUiStyleController.IsComic ? ComicWeight :
            BattleUiStyleController.IsStylish ? StylishWeight :
            BattleUiStyleController.IsBirthday ? BirthdayWeight : 1f;
        float width = size * settings.lockMarkStrokeRatio * weight;
        float half = Mathf.Max(0f, (size - width) * 0.5f);
        for (int stroke = 0; stroke < 2; stroke++)
        {
            Vector2 start = new(-half, stroke == 0 ? -half : half);
            Vector2 end = -start;
            if (paper)
            {
                start = Vector2.Scale(start, stroke == 0 ? new Vector2(0.92f, 1f) : new Vector2(1f, 0.85f));
                end = Vector2.Scale(end, stroke == 0 ? new Vector2(1f, 0.89f) : new Vector2(0.86f, 0.96f));
            }
            int segments = paper ? PaperSegments : 1;
            Vector2 previous = center + start;
            Vector2 normal = new Vector2(-(end - start).y, (end - start).x).normalized;
            for (int segment = 1; segment <= segments; segment++)
            {
                float t = (float)segment / segments;
                Vector2 point = center + Vector2.Lerp(start, end, t);
                if (paper) point += normal * (Mathf.Sin(t * Mathf.PI * 2f + stroke) * size * PaperWave);
                float thickness = width * (paper ? 1f + PaperWidthVariation * Mathf.Sin(t * Mathf.PI * 2f + stroke) : 1f);
                LockStroke(vh, previous, point, thickness, tint);
                if (round)
                {
                    LockCap(vh, previous, thickness * 0.5f, tint);
                    LockCap(vh, point, thickness * 0.5f, tint);
                }
                previous = point;
            }
        }
    }

    private static void LockStroke(VertexHelper vh, Vector2 start, Vector2 end, float width, Color tint)
    {
        Vector2 direction = (end - start).normalized;
        Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);
        int first = vh.currentVertCount;
        vh.AddVert(start - normal, tint, Vector2.zero);
        vh.AddVert(start + normal, tint, Vector2.zero);
        vh.AddVert(end + normal, tint, Vector2.zero);
        vh.AddVert(end - normal, tint, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }

    private static void LockCap(VertexHelper vh, Vector2 center, float radius, Color tint)
    {
        const int Sides = 12;
        int first = vh.currentVertCount;
        vh.AddVert(center, tint, Vector2.zero);
        for (int i = 0; i < Sides; i++)
        {
            float angle = i * Mathf.PI * 2f / Sides;
            vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
        }
        for (int i = 0; i < Sides; i++)
            vh.AddTriangle(first, first + 1 + (i + 1) % Sides, first + 1 + i);
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

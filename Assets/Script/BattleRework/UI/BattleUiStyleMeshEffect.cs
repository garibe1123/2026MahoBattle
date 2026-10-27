using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal enum BattlePaperRole { Default, Inventory, ItemDetail }

[DisallowMultipleComponent]
public sealed class BattleUiStyleMeshEffect : BaseMeshEffect
{
    private Graphic target;
    private bool lockMark;
    private BattleUiPanelDecoration lockDecoration;
    private uint lockSeed;
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
    private BattleSpeechBubbleFrameFillController speechGeometry;
    private Vector2[] speechCorners;
    private Vector2[] speechTailCorners;
    private readonly Vector2[] comicSpeechContour = new Vector2[13];
    private readonly int[] comicSpeechIndices = new int[13];
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
    internal bool UsesInkBackground => decoration != null && decoration == target ? decoration.UsesInkBackground :
        (panel || stylishSurface || stylishButton) && !stylishCard && !(interaction != null && interaction.Primary) &&
        !(image != null && decoratedSurface) && target != null &&
        Mathf.Min(target.rectTransform.rect.width, target.rectTransform.rect.height) >= 96f;
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
        if (lockMark && value.transform.parent != null)
            lockDecoration = value.transform.parent.GetComponentInChildren<BattleUiPanelDecoration>(true);
        if (lockMark && int.TryParse(value.name.Substring("LockMark_".Length), out int slotIndex))
            lockSeed = unchecked((uint)(slotIndex + 1) * 2654435761u);
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
            speechFrame = image.TryGetComponent(out speechGeometry);
            if (speechFrame) speechCorners = new Vector2[4];
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
        Material paperMaterial, Material paperMaskMaterial, Material lockMarkMaterial, Material inkPanelMaterial)
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
        if (comic && next == null && (panel || speechFrame || speechBadge || stylishButton || stylishSurface)) next = pixelPanel;
        if (comic && label != null && interaction != null &&
            (interaction.name == "StartBattleButton" || interaction.name == "RewardPackDone"))
            next = target.defaultMaterial;
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
        if ((stylish || birthday) && next == null && (panel || (stylish && (stylishButton || stylishSurface))) && !statusFill)
            next = stylish && inkPanelMaterial != null ? inkPanelMaterial : pixelPanel;
        if (paper && (panel || paperBadge || speechBadge || (image != null && image.sprite == null && (stylishSurface || stylishButton))) && !statusFill)
            next = paperMaterial != null ? paperMaterial : pixelPanel;
        if (paper && (speechFrame || hiddenFill || tailFill || (image != null && image.name == "TailShape")))
            next = paperMaskMaterial != null ? paperMaskMaterial : target.defaultMaterial;
        if (cmd && (label != null || panel || speechBadge || hiddenFill || tailFill ||
            (image != null && (image.name == "TailShape" || (image.sprite == null && (stylishSurface || stylishButton))))))
            next = target.defaultMaterial;
        if (HasAssignedMaterial()) next = null;
        if (lockMark) next = !pixel && !cmd && lockMarkMaterial != null ? lockMarkMaterial : target.defaultMaterial;
        if (comic && (tailFill || (image != null && image.name == "TailShape")))
            next = pixelPanel;
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
        bool comicStartLabel = comic && label != null && interaction != null &&
            (interaction.name == "StartBattleButton" || interaction.name == "RewardPackDone");
        bool decoratedTitle = !comicStartLabel && (title || ((comic || stylish) && interaction != null && interaction.Primary)) &&
            (comic || stylish || birthday || paper || cmd);
        if (isActiveAndEnabled && label != null && !comicStartLabel && settings.textMaterial == null &&
            (settings.font == null || settings.fontAsset != null || (decoratedTitle && settings.titleFont != null)) && !HasAssignedMaterial())
        {
            TMPro.TMP_FontAsset posterFont = decoratedTitle && (comic || stylish)
                ? (comic ? settings.comicTitleFont : settings.inkTitleFont) : null;
            if (decoratedTitle && (comic || stylish) && posterFont == null)
                posterFont = BattleUiThemeFonts.LoadTitle(comic ? BattleUiStyle.comicStyleUi : BattleUiStyle.Stylish);
            TMPro.TMP_FontAsset font = posterFont != null ? posterFont : decoratedTitle && settings.titleFont != null ? settings.titleFont :
                settings.fontAsset != null ? settings.fontAsset : BattleUiStyleController.ThemeTmpFont;
            if (font != null && (comic || cmd || stylish || paper || BattleUiOpticsCanvas.ActiveCapture != null || label.canvas == null ||
                label.canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay))
            {
                if (tmpTitle == null) tmpTitle = new BattleUiStyleTmpTitle(label);
                BattleUiTitleMotionSettings motion = comicStartLabel ? null : TitleMotion(settings);
                tmpTitle.Apply(font, settings, birthday, decoratedTitle, motion, motion != null && motion.allowOverflow,
                    stylish ? interaction?.Scale ?? 1f : 1f, entrance, stylish && interaction != null && interaction.Primary,
                    interaction != null || (cmdSelection != null && title),
                    (interaction != null && interaction.Highlighted) || (cmdSelection != null && cmdSelection.CmdSelected), topOverflow: title);
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
        if ((BattleUiStyleController.IsComic || BattleUiStyleController.IsStylish) && settings.textMaterial == null)
        {
            RestoreCmdNative();
            if (!tmpOverridden || nativeText.color != appliedTmpColor) originalTmpColor = nativeText.color;
            bool comic = BattleUiStyleController.IsComic;
            bool decoratedTitle = title || (interaction != null && interaction.Primary);
            TMPro.TMP_FontAsset posterFont = decoratedTitle ? (comic ? settings.comicTitleFont : settings.inkTitleFont) : settings.fontAsset;
            if (posterFont == null) posterFont = decoratedTitle
                ? BattleUiThemeFonts.LoadTitle(comic ? BattleUiStyle.comicStyleUi : BattleUiStyle.Stylish)
                : BattleUiStyleController.ThemeTmpFont;
            if (posterFont == null) { HideTmpTitle(); RestoreNativeText(); return; }
            if (tmpTitle == null) tmpTitle = new BattleUiStyleTmpTitle(nativeText);
            tmpTitle.Apply(posterFont, settings, false, decoratedTitle, TitleMotion(settings),
                feedbackScale: !comic ? interaction?.Scale ?? 1f : 1f,
                primary: !comic && interaction != null && interaction.Primary, sourceTint: originalTmpColor, topOverflow: title);
            if (!tmpTitle.Visible) { RestoreNativeText(); return; }
            appliedTmpColor = new Color(originalTmpColor.r, originalTmpColor.g, originalTmpColor.b, 0f);
            if (nativeText.color != appliedTmpColor) nativeText.color = appliedTmpColor;
            tmpOverridden = true;
            return;
        }
        HideTmpTitle();
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
        if (BattleUiStyleController.IsComic && (panel || speechBadge || hiddenFill || tailFill || label != null ||
            (image != null && (image.name == "TailShape" || stylishSurface || stylishButton)))) return false;
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
        if (title && BattleUiStyleController.IsComic)
            return settings?.comicTitleMotion?.enabled == true ? settings.comicTitleMotion : null;
        BattleUiTitleMotionSettings motion = settings?.stylishTitleMotion;
        if (!BattleUiStyleController.IsStylish) return null;
        return titleMotionCandidate && motion != null && motion.enabled &&
            target.name == motion.targetName && target.transform.parent != null && target.transform.parent.name == motion.targetParent
            ? motion : title && settings?.inkTitleMotion?.enabled == true ? settings.inkTitleMotion : null;
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
            if (BattleUiStyleController.IsComic && lockDecoration != null && lockDecoration.HasComicInventory)
                lockDecoration.FitComicInventory(vh, BattleUiStyleController.Settings, target.rectTransform);
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
        if (comic && (speechFrame || hiddenFill || tailFill || (image != null && image.name == "TailShape")))
        {
            if (hiddenFill) { vh.Clear(); return; }
            if (speechFrame)
            {
                vh.Clear();
                if (speechGeometry != null && speechGeometry.TryGetFillCorners(speechCorners))
                    DrawComicSpeech(vh, speechCorners, settings, image.color.a);
                else DrawComicFrame(vh, target.rectTransform.rect, settings, image.color.a, speech: true, seed: paperSeed);
            }
            else
            {
                vh.Clear();
                DrawComicSpeechTail(vh, target.rectTransform.rect, settings, image.color.a, tailFill);
            }
            return;
        }
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
        if ((panel || (comic && speechBadge) || (paper && (paperBadge || speechBadge)) || ((comic || stylish || paper) && (stylishButton || stylishSurface))) && image != null && !statusFill &&
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
                    interaction != null && interaction.Highlighted,
                    interaction != null && interaction.Primary, stylishCard);
            else if (maho) DrawMahoFrame(vh, target.rectTransform.rect, settings, fill.a);
            else if (comic) DrawComicFrame(vh, target.rectTransform.rect, settings,
                interaction != null && interaction.name == "StartBattleButton" ? image.color.a : fill.a,
                interaction != null && interaction.Highlighted, interaction != null && interaction.Primary,
                seed: paperSeed, direction: ComicDirection(target),
                opaquePalette: interaction != null && interaction.name == "StartBattleButton");
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
                    comic ? (label != null ? settings.comicWhite : settings.comicRed) :
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
        Color tint = BattleUiStyleController.IsComic ? settings.comicWhite :
            BattleUiStyleController.IsStylish ? settings.inkText :
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

    internal static void DrawComicFrame(VertexHelper vh, Rect rect, BattleUiStyleSettings settings, float alpha,
        bool selected = false, bool primary = false, bool speech = false, uint seed = 0, bool rotate = true,
        Vector2 direction = default, float perspectiveScale = 1f, bool tiledFrame = false, bool opaquePalette = false)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        int first = vh.currentVertCount;
        Color red = settings.comicRed, paper = settings.comicWhite, ink = settings.comicInk;
        if (opaquePalette) red.a = paper.a = ink.a = 1f;
        red.a *= alpha;
        paper.a *= alpha;
        ink.a *= alpha;
        float outer = Mathf.Min(Mathf.Clamp(settings.comicOuterWidth, 8f, 12f), Mathf.Min(rect.width, rect.height) * 0.12f);
        float inner = Mathf.Min(Mathf.Clamp(settings.comicInnerWidth, 4f, 6f), outer * 0.5f);
        if (!tiledFrame && !speech)
        {
            float offset = Mathf.Clamp(settings.comicShadowOffset, 6f, 10f);
            Rect shadow = new Rect(rect.position + new Vector2(offset, -offset), rect.size);
            ComicFill(vh, shadow, settings, ink, false, seed, direction, perspectiveScale: perspectiveScale);
        }
        if (tiledFrame)
        {
            // Rings avoid white underpainting when inventory opacity is reduced.
            for (int layer = 0; layer < 2; layer++)
            {
                float start = layer == 0 ? 0f : outer;
                float end = layer == 0 ? outer : outer + inner;
                for (int i = 0; i < 4; i++)
                {
                    int next = (i + 1) % 4;
                    int firstRing = vh.currentVertCount;
                    Color tint = layer == 0 ? ink : paper;
                    vh.AddVert(ComicInsetPoint(rect, settings, seed, direction, false, i, start, perspectiveScale), tint, Vector2.zero);
                    vh.AddVert(ComicInsetPoint(rect, settings, seed, direction, false, next, start, perspectiveScale), tint, Vector2.zero);
                    vh.AddVert(ComicInsetPoint(rect, settings, seed, direction, false, next, end, perspectiveScale), tint, Vector2.zero);
                    vh.AddVert(ComicInsetPoint(rect, settings, seed, direction, false, i, end, perspectiveScale), tint, Vector2.zero);
                    vh.AddTriangle(firstRing, firstRing + 1, firstRing + 2);
                    vh.AddTriangle(firstRing, firstRing + 2, firstRing + 3);
                }
            }
        }
        else
        {
            ComicFill(vh, rect, settings, ink, speech, seed, direction, perspectiveScale: perspectiveScale);
            ComicFill(vh, rect, settings, paper, speech, seed, direction, outer, perspectiveScale);
        }
        ComicFill(vh, rect, settings, primary || selected ? red : ink, speech, seed, direction, outer + inner, perspectiveScale);
        if (selected)
        {
            Color selection = settings.ComicSelectionColor;
            if (opaquePalette) selection.a = 1f;
            selection.a *= alpha;
            float size = Mathf.Min(outer * 2.2f, Mathf.Min(rect.width, rect.height) * 0.18f);
            Vector2 point = ComicInsetPoint(rect, settings, seed, direction, speech, 1, outer + inner, perspectiveScale);
            Vector2 previous = ComicInsetPoint(rect, settings, seed, direction, speech, 0, outer + inner, perspectiveScale);
            Vector2 next = ComicInsetPoint(rect, settings, seed, direction, speech, 2, outer + inner, perspectiveScale);
            int corner = vh.currentVertCount;
            vh.AddVert(point, paper, Vector2.zero);
            vh.AddVert(point + (next - point).normalized * size, paper, Vector2.zero);
            vh.AddVert(point + (previous - point).normalized * size, paper, Vector2.zero);
            vh.AddTriangle(corner, corner + 1, corner + 2);
            Vector2 alongNext = (next - point).normalized;
            Vector2 alongPrevious = (previous - point).normalized;
            Vector2 insetPoint = point + (alongNext + alongPrevious) * (size * 0.12f);
            corner = vh.currentVertCount;
            vh.AddVert(insetPoint, selection, Vector2.zero);
            vh.AddVert(insetPoint + alongNext * (size * 0.55f), selection, Vector2.zero);
            vh.AddVert(insetPoint + alongPrevious * (size * 0.55f), selection, Vector2.zero);
            vh.AddTriangle(corner, corner + 1, corner + 2);
        }
        if (!speech && rotate) RotateComicFrame(vh, first, rect, settings, seed);
    }

    internal static void ComicFill(VertexHelper vh, Rect rect, BattleUiStyleSettings settings, Color tint, bool spike,
        uint seed = 0, Vector2 direction = default, float inset = 0f, float perspectiveScale = 1f)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        int first = vh.currentVertCount;
        int count = spike ? 7 : 4;
        vh.AddVert(rect.center, tint, Vector2.zero);
        for (int i = 0; i < count; i++)
            vh.AddVert(ComicInsetPoint(rect, settings, seed, direction, spike, i, inset, perspectiveScale), tint, Vector2.zero);
        for (int i = 0; i < count; i++) vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % count);
    }

    internal static Vector2 ComicDirection(Graphic surface)
    {
        if (surface.canvas == null || !(surface.canvas.rootCanvas.transform is RectTransform canvasRect)) return Vector2.zero;
        Vector3 center = surface.rectTransform.TransformPoint(surface.rectTransform.rect.center);
        Vector3 origin = canvasRect.TransformPoint(canvasRect.rect.center);
        return surface.rectTransform.InverseTransformVector(center - origin).normalized;
    }

    private static Vector2 ComicPoint(Rect rect, BattleUiStyleSettings settings, uint seed, Vector2 direction, int index, float perspectiveScale)
    {
        if (direction.sqrMagnitude < 0.001f)
            direction = new Vector2(PaperVariation(seed, 2) < 0.5f ? -1f : 1f, PaperVariation(seed, 3) - 0.5f);
        float taper = Mathf.Min(Mathf.Clamp(settings.comicPerspectiveMax, 0f, 24f),
            Mathf.Min(rect.width, rect.height) * Mathf.Clamp(settings.comicPerspectiveRatio, 0f, 0.2f));
        taper = Mathf.Min(taper * Mathf.Max(0f, perspectiveScale), Mathf.Min(rect.width, rect.height) * 0.24f);
        if (Mathf.Abs(direction.y) > Mathf.Abs(direction.x))
        {
            float horizontal = Mathf.Clamp(direction.x, -0.75f, 0.75f);
            float bottom = direction.y >= 0f ? taper : 0f;
            float top = direction.y < 0f ? taper : 0f;
            return index switch
            {
                0 => new Vector2(rect.xMin + bottom * (1f + horizontal), rect.yMin),
                1 => new Vector2(rect.xMin + top * (1f + horizontal), rect.yMax),
                2 => new Vector2(rect.xMax - top * (1f - horizontal), rect.yMax),
                3 => new Vector2(rect.xMax - bottom * (1f - horizontal), rect.yMin),
                4 => new Vector2(Mathf.Lerp(rect.xMin + bottom * (1f + horizontal), rect.xMax - bottom * (1f - horizontal), 0.72f), rect.yMin),
                5 => new Vector2(rect.xMin + rect.width * 0.58f, rect.yMin - rect.height * Mathf.Clamp(settings.comicSpikeRatio, 0f, 0.2f)),
                _ => new Vector2(Mathf.Lerp(rect.xMin + bottom * (1f + horizontal), rect.xMax - bottom * (1f - horizontal), 0.54f), rect.yMin)
            };
        }
        float bias = Mathf.Clamp(direction.y, -0.75f, 0.75f);
        float left = direction.x >= 0f ? taper : 0f;
        float right = direction.x < 0f ? taper : 0f;
        return index switch
        {
            0 => new Vector2(rect.xMin, rect.yMin + left * (1f + bias)),
            1 => new Vector2(rect.xMin, rect.yMax - left * (1f - bias)),
            2 => new Vector2(rect.xMax, rect.yMax - right * (1f - bias)),
            3 => new Vector2(rect.xMax, rect.yMin + right * (1f + bias)),
            4 => new Vector2(rect.xMin + rect.width * 0.72f, rect.yMin + Mathf.Lerp(left, right, 0.72f) * (1f + bias)),
            5 => new Vector2(rect.xMin + rect.width * 0.58f, rect.yMin - rect.height * Mathf.Clamp(settings.comicSpikeRatio, 0f, 0.2f)),
            _ => new Vector2(rect.xMin + rect.width * 0.54f, rect.yMin + Mathf.Lerp(left, right, 0.54f) * (1f + bias))
        };
    }

    private static Vector2 ComicInsetPoint(Rect rect, BattleUiStyleSettings settings, uint seed, Vector2 direction,
        bool spike, int index, float inset, float perspectiveScale = 1f)
    {
        Vector2 point = ComicPoint(rect, settings, seed, direction, index, perspectiveScale);
        if (inset <= 0f) return point;
        int count = spike ? 7 : 4;
        Vector2 previous = (point - ComicPoint(rect, settings, seed, direction, (index + count - 1) % count, perspectiveScale)).normalized;
        Vector2 next = (ComicPoint(rect, settings, seed, direction, (index + 1) % count, perspectiveScale) - point).normalized;
        Vector2 previousNormal = new(previous.y, -previous.x);
        Vector2 nextNormal = new(next.y, -next.x);
        Vector2 bisector = (previousNormal + nextNormal).normalized;
        return point + bisector * (inset / Mathf.Max(0.35f, Vector2.Dot(bisector, previousNormal)));
    }

    internal static void DrawComicHeader(VertexHelper vh, Rect rect, BattleUiStyleSettings settings,
        float height, Color tint, uint seed, Vector2 direction, float perspectiveScale = 1f)
    {
        float outer = Mathf.Min(Mathf.Clamp(settings.comicOuterWidth, 8f, 12f), Mathf.Min(rect.width, rect.height) * 0.12f);
        float inset = outer + Mathf.Min(Mathf.Clamp(settings.comicInnerWidth, 4f, 6f), outer * 0.5f);
        float bottom = rect.yMax - height;
        int first = vh.currentVertCount;
        Vector2 previous = ComicInsetPoint(rect, settings, seed, direction, false, 3, inset, perspectiveScale);
        for (int i = 0; i < 4; i++)
        {
            Vector2 point = ComicInsetPoint(rect, settings, seed, direction, false, i, inset, perspectiveScale);
            bool previousInside = previous.y >= bottom, inside = point.y >= bottom;
            if (previousInside != inside)
                vh.AddVert(Vector2.Lerp(previous, point, (bottom - previous.y) / (point.y - previous.y)), tint, Vector2.zero);
            if (inside) vh.AddVert(point, tint, Vector2.zero);
            previous = point;
        }
        int headerEnd = vh.currentVertCount;
        for (int i = first + 1; i + 1 < headerEnd; i++) vh.AddTriangle(first, i, i + 1);
        if (headerEnd - first < 3) return;
        float spacing = Mathf.Max(Mathf.Clamp(settings.comicDotSpacing, 6f, 20f),
            Mathf.Max(rect.width, height) / 64f);
        float radius = Mathf.Clamp(settings.comicDotRadius, 0.5f, 2f);
        Color dot = settings.comicInk;
        dot.a *= tint.a;
        UIVertex a = default, b = default;
        for (float y = Mathf.Max(bottom, rect.yMin) + spacing; y < rect.yMax; y += spacing)
        for (float x = rect.xMin + spacing; x < rect.xMax; x += spacing)
        {
            bool inside = true;
            for (int i = first; i < headerEnd; i++)
            {
                vh.PopulateUIVertex(ref a, i);
                vh.PopulateUIVertex(ref b, i + 1 < headerEnd ? i + 1 : first);
                Vector2 edge = (Vector2)b.position - (Vector2)a.position;
                if (Cross(edge, new Vector2(x, y) - (Vector2)a.position) > -radius * edge.magnitude)
                { inside = false; break; }
            }
            if (!inside) continue;
            int index = vh.currentVertCount;
            vh.AddVert(new Vector2(x - radius, y), dot, Vector2.zero);
            vh.AddVert(new Vector2(x, y + radius), dot, Vector2.zero);
            vh.AddVert(new Vector2(x + radius, y), dot, Vector2.zero);
            vh.AddVert(new Vector2(x, y - radius), dot, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }
    }

    internal static Vector2 ComicInventoryCorner(Rect rect, BattleUiStyleSettings settings, Vector2 direction, int index, bool expanded)
    {
        bool top = index == 1 || index == 2;
        bool left = index == 0 || index == 1;
        Vector2 point = new(left ? rect.xMin : rect.xMax, top ? rect.yMax : rect.yMin);
        if (expanded)
        {
            if (!top) point.x += (left ? 1f : -1f) * rect.width * Mathf.Clamp(settings.comicInventoryExpandedInset, 0f, 0.24f) * Mathf.Clamp01(settings.comicInventoryExpandedTaperScale);
            return point;
        }
        float scale = top ? settings.comicInventoryTopPerspective : settings.comicInventoryBottomPerspective;
        float taper = Mathf.Min(rect.width, rect.height) * Mathf.Clamp(settings.comicPerspectiveRatio * scale, 0f, 0.24f);
        if (!top) taper *= Mathf.Clamp01(settings.comicInventoryCompactBottomAngleScale);
        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
        {
            bool inner = left ? direction.x > 0f : direction.x < 0f;
            if (inner) point.y += (top ? -1f : 1f) * taper * (1f + (top ? -direction.y : direction.y) * 0.5f);
        }
        else
        {
            bool inner = top ? direction.y < 0f : direction.y > 0f;
            if (inner) point.x += (left ? 1f : -1f) * taper * (1f + (left ? direction.x : -direction.x) * 0.5f);
        }
        return point;
    }

    internal static void RotateComicFrame(VertexHelper vh, int first, Rect rect, BattleUiStyleSettings settings, uint seed)
    {
        float rawAngle = settings.comicPanelAngle +
            (PaperVariation(seed, 7) * 2f - 1f) * settings.comicAngleVariation;
        float angle = (rawAngle < 0f ? -1f : 1f) * Mathf.Clamp(Mathf.Abs(rawAngle), 3f, 7f);
        angle *= Mathf.Deg2Rad;
        float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
        float fit = Mathf.Min(rect.width / Mathf.Max(0.001f, rect.width * cos + rect.height * Mathf.Abs(sin)),
            rect.height / Mathf.Max(0.001f, rect.height * cos + rect.width * Mathf.Abs(sin)));
        UIVertex vertex = default;
        for (int i = first; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            Vector2 point = (Vector2)vertex.position - rect.center;
            point = rect.center + new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos) * fit;
            vertex.position = new Vector3(point.x, point.y, vertex.position.z);
            vh.SetUIVertex(vertex, i);
        }
    }

    private void DrawComicSpeech(VertexHelper vh, Vector2[] corners, BattleUiStyleSettings settings, float alpha)
    {
        if (speechGeometry.ComicTail != null && speechGeometry.ComicTail.TryGetComicTip(
            target.rectTransform, out Vector2 tip, out float attachment, out Vector2 tailSize))
        {
            int edge = 0;
            Vector2 center = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
            bool right = tip.x >= center.x;
            for (int i = 1; i < 4; i++)
                if (right ? corners[i].x + corners[(i + 1) % 4].x > corners[edge].x + corners[(edge + 1) % 4].x
                    : corners[i].x + corners[(i + 1) % 4].x < corners[edge].x + corners[(edge + 1) % 4].x)
                    edge = i;
            Vector2 start = corners[edge], end = corners[(edge + 1) % 4];
            float along = start.y < end.y ? attachment : 1f - attachment;
            float half = Mathf.Min(0.24f, tailSize.y / Mathf.Max(1f, Vector2.Distance(start, end)) * 0.5f);
            Vector2 root = Vector2.Lerp(start, end, along);
            Vector2 direction = tip - root;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized;
            Vector2 rootA = Vector2.Lerp(start, end, Mathf.Clamp01(along - half));
            Vector2 rootB = Vector2.Lerp(start, end, Mathf.Clamp01(along + half));
            if (Vector2.Dot(normal, rootA - root) < 0f) normal = -normal;
            float width = Vector2.Distance(rootA, rootB) * 0.5f;
            bool hostTail = speechGeometry.ComicTail.IsComicHostTail;
            float bend = hostTail ? width * 0.65f : Mathf.Min(width * 0.2f, direction.magnitude * 0.08f);
            comicSpeechContour[0] = start;
            comicSpeechContour[1] = rootA;
            for (int i = 0; i < 3; i++)
            {
                float bendPosition = speechGeometry.ComicTail.ComicBendPosition;
                float alongTail = i == 0 ? bendPosition * 0.6f : i == 1 ? bendPosition :
                    Mathf.Lerp(bendPosition, 1f, 0.4f);
                float offset = i == 0 ? bend : i == 1 ? -bend : 0f;
                Vector2 pivotCenter = root + direction * alongTail + normal * offset;
                float radius = width * (hostTail ? (i == 0 ? 0.7f : i == 1 ? 0.5f : 0.3f) : 1f - alongTail);
                comicSpeechContour[2 + i] = pivotCenter + normal * radius;
                comicSpeechContour[8 - i] = pivotCenter - normal * radius;
            }
            comicSpeechContour[5] = tip;
            comicSpeechContour[9] = rootB;
            for (int i = 1; i < 4; i++) comicSpeechContour[i + 9] = corners[(edge + i) % 4];
            DrawComicSpeechPolygon(vh, comicSpeechContour, settings, alpha);
            return;
        }
        DrawComicSpeechPolygon(vh, corners, settings, alpha);
    }

    private void DrawComicSpeechTail(VertexHelper vh, Rect rect, BattleUiStyleSettings settings, float alpha, bool fillOnly)
    {
        float halfHeight = rect.height / BattleSpeechBubbleTailTextureBuilder.VerticalDisplayScale * 0.46f;
        if (speechTailCorners == null) speechTailCorners = new Vector2[3];
        speechTailCorners[0] = new Vector2(rect.xMin, rect.center.y - halfHeight);
        speechTailCorners[1] = new Vector2(rect.xMin, rect.center.y + halfHeight);
        speechTailCorners[2] = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, 0.985f), rect.center.y);
        if (fillOnly)
        {
            Color ink = settings.comicInk;
            ink.a *= alpha;
            for (int i = 0; i < speechTailCorners.Length; i++)
                vh.AddVert(speechTailCorners[i], ink, Vector2.zero);
            vh.AddTriangle(0, 1, 2);
        }
        else DrawComicSpeechPolygon(vh, speechTailCorners, settings, alpha);
    }

    private void DrawComicSpeechPolygon(VertexHelper vh, Vector2[] corners, BattleUiStyleSettings settings, float alpha)
    {
        float area = 0f;
        for (int i = 0; i < corners.Length; i++)
            area += Cross(corners[i], corners[(i + 1) % corners.Length]);
        float winding = area >= 0f ? 1f : -1f;
        float outer = Mathf.Clamp(settings.comicOuterWidth, 8f, 12f);
        float inner = Mathf.Clamp(settings.comicInnerWidth, 4f, 6f);
        for (int layer = 0; layer < 3; layer++)
        {
            Color tint = layer == 1 ? settings.comicWhite : settings.comicInk;
            tint.a *= alpha;
            float spread = layer == 0 ? outer + inner : layer == 1 ? inner : 0f;
            int first = vh.currentVertCount;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 point = corners[i];
                Vector2 previous = (corners[(i + corners.Length - 1) % corners.Length] - point).normalized;
                Vector2 next = (corners[(i + 1) % corners.Length] - point).normalized;
                Vector2 previousNormal = new(-previous.y, previous.x);
                Vector2 nextNormal = new(-next.y, next.x);
                previousNormal *= winding;
                nextNormal *= -winding;
                Vector2 bisector = (previousNormal + nextNormal).normalized;
                float miter = Mathf.Min(spread * 1.5f,
                    spread / Mathf.Max(0.25f, Vector2.Dot(bisector, previousNormal)));
                vh.AddVert(point + bisector * miter, tint, Vector2.zero);
            }
            int remaining = corners.Length;
            for (int i = 0; i < remaining; i++) comicSpeechIndices[i] = i;
            UIVertex vertex = default;
            int attempts = remaining * remaining;
            while (remaining > 2 && attempts-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < remaining; i++)
                {
                    int a = comicSpeechIndices[(i + remaining - 1) % remaining];
                    int b = comicSpeechIndices[i];
                    int c = comicSpeechIndices[(i + 1) % remaining];
                    vh.PopulateUIVertex(ref vertex, first + a); Vector2 pa = vertex.position;
                    vh.PopulateUIVertex(ref vertex, first + b); Vector2 pb = vertex.position;
                    vh.PopulateUIVertex(ref vertex, first + c); Vector2 pc = vertex.position;
                    if (Cross(pb - pa, pc - pb) * winding <= 0.0001f) continue;
                    bool contains = false;
                    for (int j = 0; j < remaining; j++)
                    {
                        int index = comicSpeechIndices[j];
                        if (index == a || index == b || index == c) continue;
                        vh.PopulateUIVertex(ref vertex, first + index); Vector2 p = vertex.position;
                        if (Cross(pb - pa, p - pa) * winding >= 0f &&
                            Cross(pc - pb, p - pb) * winding >= 0f &&
                            Cross(pa - pc, p - pc) * winding >= 0f) { contains = true; break; }
                    }
                    if (contains) continue;
                    vh.AddTriangle(first + a, first + b, first + c);
                    for (int j = i; j < remaining - 1; j++) comicSpeechIndices[j] = comicSpeechIndices[j + 1];
                    remaining--;
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
        }
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

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
        if (rect.width <= 0f || rect.height <= 0f) return;
        float border = Mathf.Min(Mathf.Clamp(settings.inkOutlineWidth, 3f, 5f), Mathf.Min(rect.width, rect.height) * 0.12f);
        float glow = selected ? Mathf.Min(Mathf.Clamp(settings.inkGlowWidth, 8f, 14f), rect.height * 0.12f) : 0f;
        float shadowOffset = Mathf.Min(Mathf.Clamp(settings.stylishShadowOffset, 4f, 8f), rect.height * 0.06f);
        Rect body = Inset(rect, glow);
        body = new Rect(body.x, body.y + shadowOffset, body.width - shadowOffset, body.height - shadowOffset);
        bool glass = !card && !primary && Mathf.Min(rect.width, rect.height) >= 96f;
        bool featured = (primary || (selected && card)) && rect.width >= 240f && rect.height >= 96f;
        Rect shadow = new(body.x + shadowOffset, body.y - shadowOffset, body.width, body.height);
        Color ink = settings.inkBackground;
        ink.a *= alpha;
        Color shade = ink;
        shade.a *= glass ? 0.12f : 0.36f;
        if (glass) StylishRing(vh, shadow, settings, shadowOffset, shade);
        else StylishFill(vh, shadow, settings, shade, true, featured);
        if (selected)
        {
            for (int i = 3; i > 0; i--)
            {
                Color light = i == 3 ? settings.inkMagenta : settings.inkCyan;
                light.a *= alpha * (0.16f / i);
                float spread = glow * i / 3f;
                Rect halo = new(body.x - spread, body.y - spread, body.width + spread * 2f, body.height + spread * 2f);
                if (glass) StylishRing(vh, halo, settings, spread + border, light);
                else StylishFill(vh, halo, settings, light, true, featured);
            }
        }
        Color rim = selected ? settings.inkCyan : Color.Lerp(ink, settings.inkText, card ? 0.18f : 0.05f);
        rim.a = ink.a * (glass && !selected ? 0.18f : 1f);
        if (glass) StylishRing(vh, body, settings, selected ? border : Mathf.Min(1.5f, border), rim);
        else StylishFill(vh, body, settings, rim, true, featured);
        Rect face = Inset(body, selected ? border : Mathf.Min(1.5f, border));
        Color fill = primary ? settings.inkLime : settings.inkBackground;
        fill.a *= alpha;
        int firstFace = vh.currentVertCount;
        StylishFill(vh, face, settings, fill, true, featured);
        if (!glass) return;
        UIVertex vertex = default;
        for (int i = firstFace; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            vertex.uv0.w = 1f;
            vh.SetUIVertex(vertex, i);
        }
    }

    private static void StylishFill(VertexHelper vh, Rect rect, BattleUiStyleSettings settings, Color tint, bool rounded, bool drips = false)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        const int steps = 6;
        float depth = drips && rect.width >= 160f && rect.height >= 72f
            ? Mathf.Min(18f, rect.height * Mathf.Clamp(settings.inkLobeRatio, 0f, 0.12f) * 2f) : 0f;
        Rect body = new(rect.x, rect.y + depth, rect.width, rect.height - depth);
        float radius = Mathf.Min(rounded ? settings.stylishCardRadius : 8f, body.height * (rounded ? 0.22f : 0.12f));
        radius = Mathf.Clamp(radius, 0f, body.width * 0.25f);
        RoundedFill(vh, body, radius, tint);
        for (int drip = 0; drip < 2 && depth > 0f; drip++)
        {
            float length = depth * (drip == 0 ? 1f : 0.65f);
            float half = length * 0.42f;
            float x = body.xMin + body.width * (drip == 0 ? 0.63f : 0.74f);
            Quad(vh, new Rect(x - half, body.yMin - length + half, half * 2f, length - half), tint);
            int first = vh.currentVertCount;
            vh.AddVert(new Vector2(x, body.yMin - length + half), tint, Vector2.zero);
            for (int step = 0; step <= steps; step++)
            {
                float angle = (180f + step * 180f / steps) * Mathf.Deg2Rad;
                vh.AddVert(new Vector2(x + Mathf.Cos(angle) * half,
                    body.yMin - length + half + Mathf.Sin(angle) * half), tint, Vector2.zero);
            }
            for (int step = 0; step < steps; step++)
                vh.AddTriangle(first, first + step + 1, first + step + 2);
        }
    }

    private static void StylishRing(VertexHelper vh, Rect rect, BattleUiStyleSettings settings, float width, Color tint)
    {
        if (rect.width <= 0f || rect.height <= 0f || tint.a <= 0f) return;
        width = Mathf.Clamp(width, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
        Rect inner = Inset(rect, width);
        float outerRadius = Mathf.Clamp(Mathf.Min(settings.stylishCardRadius, rect.height * 0.22f), 0f, rect.width * 0.25f);
        float innerRadius = Mathf.Clamp(Mathf.Min(settings.stylishCardRadius, inner.height * 0.22f), 0f, inner.width * 0.25f);
        const int steps = 8, count = 4 * (steps + 1);
        int first = vh.currentVertCount;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 outerCenter = new(corner < 2 ? rect.xMax - outerRadius : rect.xMin + outerRadius,
                corner == 0 || corner == 3 ? rect.yMin + outerRadius : rect.yMax - outerRadius);
            Vector2 innerCenter = new(corner < 2 ? inner.xMax - innerRadius : inner.xMin + innerRadius,
                corner == 0 || corner == 3 ? inner.yMin + innerRadius : inner.yMax - innerRadius);
            for (int step = 0; step <= steps; step++)
            {
                float angle = (-90f + corner * 90f + step * 90f / steps) * Mathf.Deg2Rad;
                Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
                vh.AddVert(outerCenter + direction * outerRadius, tint, Vector2.zero);
                vh.AddVert(innerCenter + direction * innerRadius, tint, Vector2.zero);
            }
        }
        for (int i = 0; i < count; i++)
        {
            int a = first + i * 2, b = first + (i + 1) % count * 2;
            vh.AddTriangle(a, b, b + 1);
            vh.AddTriangle(a, b + 1, a + 1);
        }
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
        float cut = Mathf.Min(rect.width * 0.12f, rect.height * Mathf.Abs(Mathf.Tan(Mathf.Clamp(angle, -4f, 4f) * Mathf.Deg2Rad)));
        int first = vh.currentVertCount;
        vh.AddVert(new Vector3(rect.xMin + (angle < 0f ? cut : 0f), rect.yMin, 0f), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMin + (angle < 0f ? 0f : cut), rect.yMax, 0f), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax - (angle < 0f ? cut : 0f), rect.yMax, 0f), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax - (angle < 0f ? 0f : cut), rect.yMin, 0f), tint, Vector2.zero);
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
        const int PixelSteps = 9, CmdSteps = 7;
        vh.Clear();
        BattleUiStyleSettings settings = BattleUiStyleController.Settings;
        if (settings == null) return;
        Rect rect = target.rectTransform.rect;
        float size = Mathf.Min(rect.width, rect.height) * settings.lockMarkSizeRatio;
        if (BattleUiStyleController.IsComic && lockDecoration != null && lockDecoration.HasComicInventory)
        {
            float padding = Mathf.Min(Mathf.Clamp(settings.comicOuterWidth, 8f, 12f), Mathf.Min(rect.width, rect.height) * 0.12f);
            padding += Mathf.Min(Mathf.Clamp(settings.comicInnerWidth, 4f, 6f), padding * 0.5f) + 4f;
            size = Mathf.Min(size, Mathf.Max(0f, Mathf.Min(rect.width, rect.height) - padding * 2f));
        }
        if (size <= 0f) return;
        Color tint = BattleUiStyleController.IsComic ? settings.comicWhite :
            BattleUiStyleController.IsStylish ? settings.inkCyan : settings.innerLine;
        tint.a *= settings.lockMarkAlpha * target.color.a;
        Vector2 center = rect.center;
        float variation = settings.lockMarkVariation;
        if (BattleUiStyleController.IsPixel || BattleUiStyleController.IsCmd)
        {
            bool cmd = BattleUiStyleController.IsCmd;
            int steps = cmd ? CmdSteps : PixelSteps + (variation > 0f ? ((int)(PaperVariation(lockSeed, 1) * 3f) - 1) * 2 : 0);
            float step = size / steps;
            for (int i = 0; i < steps; i++)
            {
                float x = center.x - size * 0.5f + i * step;
                float y = center.y - size * 0.5f + i * step;
                bool serif = cmd && (i == 0 || i == steps - 1);
                float width = serif ? step * 2f : step;
                float inset = serif && i == steps - 1 ? step : 0f;
                Quad(vh, new Rect(x - inset, y, width, step), tint);
                if (i != steps / 2)
                    Quad(vh, new Rect(center.x + size * 0.5f - (i + 1) * step - (serif ? step - inset : 0f),
                        y, width, step), tint);
            }
            return;
        }
        if (target.material == target.defaultMaterial)
        {
            float half = size * 0.45f, width = size * settings.lockMarkStrokeRatio;
            LockStroke(vh, center - new Vector2(half, half), center + new Vector2(half, half),
                new Vector2(-1f, 1f).normalized, width, width, tint);
            LockStroke(vh, center - new Vector2(half, -half), center + new Vector2(half, -half),
                new Vector2(1f, 1f).normalized, width, width, tint);
            return;
        }
        int first = vh.currentVertCount;
        Vector2 seed = new(PaperVariation(lockSeed, 0), PaperVariation(lockSeed, 1));
        for (int i = 0; i < 4; i++)
        {
            Vector2 uv = new(i >= 2 ? 1f : 0f, i == 1 || i == 2 ? 1f : 0f);
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = center + (uv - Vector2.one * 0.5f) * size;
            vertex.color = tint;
            vertex.uv0 = new Vector4(uv.x, uv.y, seed.x, seed.y);
            vh.AddVert(vertex);
        }
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }

    private static void LockStroke(VertexHelper vh, Vector2 start, Vector2 end, Vector2 normal,
        float startWidth, float endWidth, Color tint)
    {
        Vector2 startNormal = normal * (startWidth * 0.5f), endNormal = normal * (endWidth * 0.5f);
        int first = vh.currentVertCount;
        vh.AddVert(start - startNormal, tint, Vector2.zero);
        vh.AddVert(start + startNormal, tint, Vector2.zero);
        vh.AddVert(end + endNormal, tint, Vector2.zero);
        vh.AddVert(end - endNormal, tint, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
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

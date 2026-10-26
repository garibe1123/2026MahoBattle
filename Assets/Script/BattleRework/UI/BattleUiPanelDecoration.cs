using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleUiPanelDecoration : MaskableGraphic
{
    public static readonly Color Purple = new(0.62f, 0.43f, 0.89f, 1f);
    public static readonly Color Mint = new(0.36f, 0.88f, 0.73f, 1f);
    public static readonly Color Yellow = new(0.98f, 0.86f, 0.31f, 1f);
    public static readonly Color Surface = new(0.10f, 0.10f, 0.10f, 0.94f);
    public static readonly Color Paper = new(0.94f, 0.94f, 0.94f, 1f);
    public static readonly Color Muted = new(0.68f, 0.68f, 0.68f, 1f);
    public static readonly Color SelectedSurface = new(0.16f, 0.26f, 0.27f, 0.98f);
    public static readonly Color EquippedSurface = new(0.10f, 0.19f, 0.20f, 0.98f);
    public static readonly Color LockedSurface = new(0.045f, 0.075f, 0.085f, 0.96f);
    public static readonly Vector2 CompactPackSize = new(430f, 176f);
    public static readonly Vector2 ExpandedPackSize = new(632f, 696f);
    public static readonly Vector2 MiniPackSize = new(304f, 335f);
    public const float FullCellSize = 184f;
    public const float FullCellGap = 18f;
    public const float MiniCellSize = FullCellSize * 304f / 632f;
    public const float MiniCellGap = FullCellGap * 304f / 632f;
    public const float MiniIconPixels = FullIconPixels * 304f / 632f;
    public const float FullIconPixels = 88f;
    public const int MiniNameFontSize = 8;
    public const int FullNameFontSize = 16;
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
    private bool softFill, stylishCard, stylishSpotlight;
    private bool hasHeader;
    private bool emphasis;
    private bool selected;
    private bool animate;
    private float pulseUntil;
    private float nextRefresh;
    private float pulse;
    private Text headerLabel;
    private Image surfaceImage;
    private CanvasRenderer surfaceRenderer;
    private Material originalMaterial;
    private Material appliedMaterial;
    private bool materialApplied;
    private float surfaceOpacity = 1f;
    private bool inventoryBackground, lockedSlot;
    internal bool UsesInkBackground => !stylishCard && !(styleInteraction != null && styleInteraction.Primary) &&
        Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) >= 96f;
    private int backgroundVertexCount;
    private float backgroundBaseAlpha;
    private bool wasInventoryDimmed;
    private float lastCombatAlpha = -1f, lastLockedAlpha = -1f;
    private BattleUiStyleInteraction styleInteraction;
    private uint paperSeed;
    private BattlePaperRole paperRole;
    private bool paperSheet;
    private RectTransform comicInventoryRoot;
    private float comicInventoryHeight = 0.5f;

    public void SetComicInventorySlot(RectTransform root, float relativeHeight)
    {
        comicInventoryRoot = root;
        comicInventoryHeight = Mathf.Clamp01(relativeHeight);
        SetVerticesDirty();
    }

    internal void SetStyleInteraction(BattleUiStyleInteraction interaction) => styleInteraction = interaction;
    internal bool CmdSelected => selected || emphasis || (styleInteraction != null && styleInteraction.Highlighted);

    public void SetInventoryBackground(bool locked = false)
    {
        if (inventoryBackground && lockedSlot == locked) return;
        inventoryBackground = true;
        lockedSlot = locked;
        SetVerticesDirty();
        if (surfaceImage != null) surfaceImage.SetVerticesDirty();
    }

    internal void RefreshBackgroundOpacity()
    {
        if (!inventoryBackground) return;
        bool dimmed = BattleUnifiedInventoryInspectController.DimInventoryBackgrounds;
        BattleUiStyleSettings settings = BattleUiStyleController.Settings;
        float combatAlpha = settings != null ? settings.combatBackgroundAlpha : BattleUiStyleSettings.DefaultCombatBackgroundAlpha;
        float lockedAlpha = settings != null ? settings.lockedSlotAlpha : BattleUiStyleSettings.DefaultLockedSlotAlpha;
        if (wasInventoryDimmed == dimmed && lastCombatAlpha == combatAlpha && lastLockedAlpha == lockedAlpha) return;
        wasInventoryDimmed = dimmed;
        lastCombatAlpha = combatAlpha;
        lastLockedAlpha = lockedAlpha;
        SetVerticesDirty();
        if (surfaceImage != null) surfaceImage.SetVerticesDirty();
    }

    internal void ApplyBackgroundOpacity(VertexHelper vh, int backgroundCount, float baseAlpha)
    {
        if (!inventoryBackground) return;
        BattleUiStyleSettings settings = BattleUiStyleController.Settings;
        float lockedAlpha = lockedSlot ? (settings != null ? settings.lockedSlotAlpha : BattleUiStyleSettings.DefaultLockedSlotAlpha) : 1f;
        float backgroundScale = BattleUnifiedInventoryInspectController.DimInventoryBackgrounds && baseAlpha > 0f
            ? (settings != null ? settings.combatBackgroundAlpha : BattleUiStyleSettings.DefaultCombatBackgroundAlpha) / baseAlpha : 1f;
        if (lockedAlpha == 1f && backgroundScale == 1f) return;
        UIVertex vertex = default;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            Color tint = vertex.color;
            tint.a *= lockedAlpha * (i < backgroundCount ? backgroundScale : 1f);
            vertex.color = tint;
            vh.SetUIVertex(vertex, i);
        }
    }

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
        GameObject child = existing != null ? existing.gameObject :
            new GameObject("PanelDecoration", typeof(RectTransform), typeof(CanvasRenderer));
        if (child.GetComponent<CanvasRenderer>() == null)
            child.AddComponent<CanvasRenderer>();
        child.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)child.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.SetAsFirstSibling();
        BattleUiPanelDecoration decoration = child.GetComponent<BattleUiPanelDecoration>();
        if (decoration == null)
            decoration = child.AddComponent<BattleUiPanelDecoration>();
        decoration.accent = accent;
        decoration.softFill = softFill;
        decoration.stylishCard = BattleUiStyleMeshEffect.IsCard(parent);
        decoration.stylishSpotlight = BattleUiStyleMeshEffect.IsSpotlight(parent);
        decoration.paperSeed = BattleUiStyleMeshEffect.PaperSeed(parent);
        decoration.paperRole = BattleUiStyleMeshEffect.PaperRole(parent);
        decoration.paperSheet = BattleUiStyleMeshEffect.IsPaperSheet(parent);
        decoration.animate = animate;
        decoration.raycastTarget = false;
        if (decoration.surfaceImage == null && parent.TryGetComponent(out Image surface))
        {
            decoration.surfaceImage = surface;
            decoration.surfaceRenderer = surface.canvasRenderer;
            decoration.originalMaterial = surface.material;
        }
        decoration.ApplySurfaceStyle();
        foreach (Outline outline in parent.GetComponents<Outline>())
            outline.enabled = false;
        decoration.SetVerticesDirty();
        BattleUiStyleController.RegisterGraphic(decoration);
        BattleUiStyleController.RegisterPanel(decoration.surfaceImage);
        return decoration;
    }

    public static void Header(RectTransform parent, string title, Color accent, bool inventoryBackground = false)
    {
        BattleUiPanelDecoration decoration = Attach(parent, accent);
        if (inventoryBackground) decoration.SetInventoryBackground();
        decoration.hasHeader = true;
        Text text = decoration.headerLabel;
        if (text == null)
        {
            GameObject label = new("AppHeader", typeof(RectTransform));
            label.transform.SetParent(parent, false);
            text = label.AddComponent<Text>();
            decoration.headerLabel = text;
        }
        text.font = BattleFeedbackFont.Resolve();
        text.fontSize = 12;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Paper;
        text.text = title;
        text.raycastTarget = false;
        decoration.LayoutHeader();
        BattleUiStyleController.RegisterGraphic(text);
        decoration.SetVerticesDirty();
    }

    private void LayoutHeader()
    {
        if (headerLabel == null)
            return;
        float scale = Mathf.Clamp(rectTransform.rect.width / ExpandedPackSize.x, 0.5f, 1f);
        RectTransform rect = headerLabel.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2((Padding + 18f) * scale, -HeaderHeight * scale);
        rect.offsetMax = new Vector2((-Padding - 36f) * scale, -4f * scale);
        headerLabel.fontSize = Mathf.Max(8, Mathf.RoundToInt(12f * scale));
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        LayoutHeader();
    }

    public static Text EquippedBar(RectTransform parent)
    {
        GameObject bar = new("EquippedBar", typeof(RectTransform));
        bar.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)bar.transform;
        rect.anchorMin = new Vector2(0.07f, 0.83f);
        rect.anchorMax = new Vector2(0.93f, 0.96f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Image fill = bar.AddComponent<Image>();
        fill.sprite = BattleHudSpriteCache.RoundedPanel;
        fill.type = Image.Type.Sliced;
        fill.color = new Color(Mint.r, Mint.g, Mint.b, 0.16f);
        fill.raycastTarget = false;
        GameObject label = new("Label", typeof(RectTransform));
        label.transform.SetParent(bar.transform, false);
        Text text = label.AddComponent<Text>();
        text.font = BattleFeedbackFont.Resolve();
        text.fontSize = 10;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Paper;
        text.text = "[equive]";
        text.raycastTarget = false;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        bar.SetActive(false);
        BattleUiStyleController.RegisterRoot(bar.transform);
        return text;
    }

    private void ApplySurfaceStyle()
    {
        Material shared = BattleHUD.SharedUiMaterial;
        if (surfaceImage != null && (!materialApplied || appliedMaterial != shared))
        {
            surfaceImage.material = shared != null ? shared : originalMaterial;
            appliedMaterial = shared;
            materialApplied = true;
        }
        float targetOpacity = BattleHUD.UiSurfaceOpacity;
        float nextOpacity = Mathf.Abs(surfaceOpacity - targetOpacity) < 0.001f ? targetOpacity :
            Mathf.Lerp(surfaceOpacity, targetOpacity, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        if (nextOpacity != surfaceOpacity)
        {
            surfaceOpacity = nextOpacity;
            if (surfaceRenderer != null)
                surfaceRenderer.SetAlpha(surfaceOpacity);
            if (canvasRenderer != null)
                canvasRenderer.SetAlpha(surfaceOpacity);
        }
    }

    public static void Round(Image image)
    {
        image.sprite = BattleHudSpriteCache.RoundedPanel;
        image.type = Image.Type.Sliced;
        image.color = new Color(Surface.r, Surface.g, Surface.b, image.color.a > 0f ? Surface.a : 0f);
        BattleUiStyleController.RegisterGraphic(image);
    }

    public void SetEmphasis(bool value, bool selected = false)
    {
        if (emphasis == value && this.selected == selected)
            return;
        bool confirmed = selected && !this.selected;
        emphasis = value;
        this.selected = selected;
        float duration = BattleUiStyleController.IsStylish
            ? BattleUiStyleController.Settings.stylishConfirmDuration : 0.18f;
        pulseUntil = animate ? Time.unscaledTime + duration : 0f;
        if (confirmed && animate) BattleUiStyleBurst.Play(rectTransform);
        nextRefresh = 0f;
        SetVerticesDirty();
    }

    private void Update()
    {
        if (canvasRenderer == null || canvasRenderer.cull ||
            (canvasRenderer.GetInheritedAlpha() <= 0.01f && surfaceOpacity > 0.01f))
            return;
        ApplySurfaceStyle();
        if ((Time.unscaledTime >= pulseUntil && pulse <= 0f) ||
            Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + 0.033f;
        float duration = BattleUiStyleController.IsStylish
            ? BattleUiStyleController.Settings.stylishConfirmDuration : 0.18f;
        pulse = Mathf.Clamp01((pulseUntil - Time.unscaledTime) / Mathf.Max(0.01f, duration));
        SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        pulseUntil = nextRefresh = pulse = 0f;
        base.OnDisable();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        backgroundVertexCount = 0;
        backgroundBaseAlpha = surfaceImage != null ? surfaceImage.color.a : 1f;
        PopulatePanelMesh(vh);
        ApplyBackgroundOpacity(vh, backgroundVertexCount, backgroundBaseAlpha);
    }

    private void PopulatePanelMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect face = rectTransform.rect;
        if (face.width < 8f || face.height < 8f)
            return;

        if (BattleUiStyleController.IsCmd)
        {
            Color fill = BattleUiStyleController.Settings.background;
            fill.a *= softFill ? 0.94f : 1f;
            BattleUiStyleMeshEffect.Quad(vh, face, fill);
            backgroundBaseAlpha = fill.a;
            backgroundVertexCount = vh.currentVertCount;
            return;
        }

        if (BattleUiStyleController.IsPaper)
        {
            BattleUiStyleSettings style = BattleUiStyleController.Settings;
            float opacity = softFill ? 0.94f : 1f;
            BattleUiStyleMeshEffect.DrawPaperFrame(vh, face, style, opacity, paperSeed, false,
                emphasis || (styleInteraction != null && styleInteraction.Highlighted), this, paperRole, paperSheet);
            backgroundBaseAlpha = BattleUiStyleMeshEffect.PaperColor(style, paperRole).a * opacity;
            if (hasHeader)
            {
                float scale = Mathf.Clamp(face.width / ExpandedPackSize.x, 0.5f, 1f);
                Rect header = new(face.xMin + 6f * scale, face.yMax - HeaderHeight * scale,
                    face.width - 12f * scale, (HeaderHeight - 5f) * scale);
                BattleUiStyleMeshEffect.DrawPaperFrame(vh, header, style, opacity, paperSeed + 1u,
                    surface: this, role: paperRole);
                float size = 18f * scale;
                Rect badge = new(face.xMin + Padding * scale, header.center.y - size * 0.5f, size, size);
                BattleUiStyleMeshEffect.DrawPaperFrame(vh, badge, style, opacity, paperSeed + 2u, true, surface: this);
            }
            BattleUiStyleMeshEffect.ApplyPaperCoordinates(vh, 0, face, style, paperSeed, this, paperRole, paperSheet);
            backgroundVertexCount = vh.currentVertCount;
            if (selected)
            {
                float size = Mathf.Min(18f, Mathf.Min(face.width, face.height) * 0.2f);
                Rect badge = new(face.xMax - size - 6f, face.yMin + 6f, size, size);
                Color mark = style.innerLine;
                mark.a *= opacity;
                BattleUiStyleMeshEffect.PaperFill(vh, badge, style, mark, paperSeed + 3u, true);
            }
            return;
        }

        if (BattleUiStyleController.IsStylish || BattleUiStyleController.IsBirthday)
        {
            BattleUiStyleSettings style = BattleUiStyleController.Settings;
            bool birthday = BattleUiStyleController.IsBirthday;
            float opacity = softFill ? 0.94f : 1f;
            float border = Mathf.Clamp(birthday ? style.birthdayBorderWidth * 3f : style.inkOutlineWidth,
                0f, Mathf.Min(face.width, face.height) * 0.5f);
            float scale = Mathf.Clamp(face.width / ExpandedPackSize.x, 0.5f, 1f);
            BattleUiStyleMeshEffect.DrawAccentFrame(vh, face, style, birthday, opacity,
                selected || emphasis || (styleInteraction != null && styleInteraction.Highlighted),
                styleInteraction != null && styleInteraction.Primary, stylishCard);
            backgroundBaseAlpha = (birthday ? style.background.a : style.inkBackground.a) * opacity;
            if (hasHeader)
            {
                float height = Mathf.Min(HeaderHeight * scale, face.height - border);
                Rect header = new(face.xMin + border, face.yMax - height,
                    face.width - border * 2f, height - border);
                Color rim = birthday ? style.status : style.inkLime;
                rim.a *= opacity;
                Color paper = birthday ? style.outerFrame : style.inkMagenta;
                paper.a *= opacity;
                if (birthday)
                {
                    BattleUiStyleMeshEffect.RoundedFill(vh, header, 6f * scale, rim);
                    BattleUiStyleMeshEffect.RoundedFill(vh, BattleUiStyleMeshEffect.Inset(header, 2f * scale),
                        4f * scale, paper);
                }
                else
                {
                    Color ink = style.inkBackground;
                    ink.a *= opacity;
                    BattleUiStyleMeshEffect.RoundedFill(vh, header, 4f * scale, ink);
                    BattleUiStyleMeshEffect.AccentSlant(vh,
                        new Rect(header.xMin, header.yMin + 3f * scale, 4f * scale, header.height - 6f * scale),
                        Mathf.Clamp(style.inkPanelAngle, -4f, 4f), rim);
                    BattleUiStyleMeshEffect.Quad(vh,
                        new Rect(header.xMin + 8f * scale, header.yMin, header.width - 16f * scale, 2f * scale), paper);
                }
            }
            backgroundVertexCount = vh.currentVertCount;
            if (emphasis)
            {
                Color tint = style.innerLine;
                tint.a *= 0.08f + pulse * 0.08f;
                BattleUiStyleMeshEffect.Quad(vh, BattleUiStyleMeshEffect.Inset(face, border), tint);
            }
            if (selected)
                BattleUiStyleMeshEffect.Quad(vh,
                    new Rect(face.xMin + border, face.yMin + border,
                        face.width - border * 2f, Mathf.Min(3f, face.height - border * 2f)), birthday ? style.highlight : style.inkLime);
            if (birthday && hasHeader) DrawBirthdayStars(vh, face, style, border, scale, opacity);
            return;
        }

        if (BattleUiStyleController.IsMahoShojo)
        {
            BattleUiStyleSettings style = BattleUiStyleController.Settings;
            BattleUiStyleMeshEffect.DrawMahoFrame(vh, face, style, softFill ? 0.94f : 1f);
            backgroundVertexCount = vh.currentVertCount;
            backgroundBaseAlpha = style.background.a * (softFill ? 0.94f : 1f);
            float scale = Mathf.Clamp(face.width / ExpandedPackSize.x, 0.5f, 1f);
            if (emphasis)
            {
                Color tint = style.innerLine;
                tint.a *= 0.04f + pulse * 0.04f;
                BattleUiStyleMeshEffect.RoundedFill(vh, BattleUiStyleMeshEffect.Inset(face, 2f),
                    Mathf.Max(0f, style.mahoCornerRadius - 2f), tint);
            }
            if (hasHeader)
            {
                Rect header = new(face.xMin + 8f * scale, face.yMax - HeaderHeight * scale,
                    face.width - 16f * scale, (HeaderHeight - 8f) * scale);
                BattleUiStyleMeshEffect.RoundedFill(vh, header, style.mahoCornerRadius * 0.5f, style.status);
                float radius = 5f * scale;
                Vector2 badge = new(face.xMin + (Padding + 5f) * scale, header.center.y);
                BattleUiStyleMeshEffect.RoundedFill(vh,
                    new Rect(badge.x - radius, badge.y - radius, radius * 2f, radius * 2f), radius, style.energy);
                for (int i = 0; i < 3; i++)
                    BattleUiStyleMeshEffect.RoundedFill(vh,
                        new Rect(face.xMax - (Padding + 5f + i * 9f) * scale, header.center.y - 2f * scale,
                            4f * scale, 4f * scale), 2f * scale, style.highlight);
            }
            if (selected)
                BattleUiStyleMeshEffect.RoundedFill(vh,
                    new Rect(face.xMin + Padding * scale, face.yMin + 8f * scale,
                        face.width - Padding * 2f * scale, 3f * scale), 1.5f * scale, style.innerLine);
            return;
        }

        if (BattleUiStyleController.IsPixel)
        {
            BattleUiStyleSettings style = BattleUiStyleController.Settings;
            float pixel = BattleUiStyleMeshEffect.PixelStep(canvas, style);
            Color fill = style.background;
            fill.a = softFill ? 0.90f : 1f;
            BattleUiStyleMeshEffect.DrawFrame(vh, face, style, fill, pixel);
            backgroundBaseAlpha = fill.a;
            if (hasHeader)
            {
                float scale = Mathf.Clamp(face.width / ExpandedPackSize.x, 0.5f, 1f);
                Rect header = new(face.xMin + 4f * pixel, face.yMax - HeaderHeight * scale,
                    face.width - 8f * pixel, HeaderHeight * scale - 4f * pixel);
                Color headerTint = style.status;
                headerTint.a = 0.18f;
                BattleUiStyleMeshEffect.Quad(vh, header, headerTint);
                BattleUiStyleMeshEffect.Quad(vh,
                    new Rect(header.xMin, header.yMin, pixel, header.height), style.status);
            }
            backgroundVertexCount = vh.currentVertCount;
            if (emphasis)
            {
                Color tint = style.status;
                tint.a = 0.13f + pulse * 0.08f;
                BattleUiStyleMeshEffect.StepFill(vh,
                    BattleUiStyleMeshEffect.Inset(face, 3f * pixel), pixel, tint);
            }
            if (selected)
                BattleUiStyleMeshEffect.Quad(vh,
                    new Rect(face.xMax - 7f * pixel, face.yMin + 4f * pixel, 3f * pixel, 3f * pixel), style.innerLine);
            return;
        }

        if (BattleUiStyleController.IsComic)
        {
            BattleUiStyleSettings style = BattleUiStyleController.Settings;
            Vector2 direction = BattleUiStyleMeshEffect.ComicDirection(this);
            float perspectiveScale = 1f;
            if (comicInventoryRoot != null)
            {
                Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
                Camera camera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? rootCanvas.worldCamera : null;
                Vector2 screenCenter = camera != null ? camera.pixelRect.center : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenCenter, camera, out Vector2 origin))
                    direction = (rectTransform.rect.center - origin).normalized;
                perspectiveScale = comicInventoryHeight <= 0.5f
                    ? Mathf.Lerp(style.comicInventoryBottomPerspective, style.comicInventoryMiddlePerspective, comicInventoryHeight * 2f)
                    : Mathf.Lerp(style.comicInventoryMiddlePerspective, style.comicInventoryTopPerspective, (comicInventoryHeight - 0.5f) * 2f);
                if (direction.sqrMagnitude < 0.001f) perspectiveScale = 0f;
            }
            BattleUiStyleMeshEffect.DrawComicFrame(vh, face, style, softFill ? 0.94f : 1f,
                selected || emphasis || (styleInteraction != null && styleInteraction.Highlighted),
                styleInteraction != null && styleInteraction.Primary, seed: paperSeed, rotate: false, direction: direction,
                perspectiveScale: perspectiveScale);
            backgroundBaseAlpha = style.comicInk.a * (softFill ? 0.94f : 1f);
            if (hasHeader)
            {
                float scale = Mathf.Clamp(face.width / ExpandedPackSize.x, 0.5f, 1f);
                Color tint = style.comicRed;
                tint.a *= softFill ? 0.94f : 1f;
                BattleUiStyleMeshEffect.DrawComicHeader(vh, face, style, HeaderHeight * scale, tint, paperSeed, direction, perspectiveScale);
            }
            backgroundVertexCount = vh.currentVertCount;
            if (emphasis)
            {
                BattleUiStyleMeshEffect.Slant(vh,
                    new Rect(face.xMin + 4f, face.center.y - 18f, 8f, 36f), style.comicSelection);
            }
            if (selected)
                BattleUiStyleMeshEffect.Slant(vh,
                    new Rect(face.xMax - 23f, face.yMin + 7f, 16f, 12f), style.comicWhite);
            if (comicInventoryRoot != null)
                BattleUiStyleMeshEffect.FitComicDecoration(vh, backgroundVertexCount, face, style, paperSeed, direction, perspectiveScale);
            BattleUiStyleMeshEffect.RotateComicFrame(vh, 0, face, style, paperSeed);
            return;
        }

        Fill(vh, Expand(face, 0f, new Vector2(3f + pulse, -4f - pulse)),
            new Color(0f, 0f, 0f, 0.12f), new Color(0f, 0f, 0f, 0.12f));
        float frost = softFill ? 0.035f : 0.06f;
        Fill(vh, face, new Color(1f, 1f, 1f, 0.01f),
            new Color(1f, 1f, 1f, frost + pulse * 0.03f));
        if (hasHeader)
        {
            float scale = Mathf.Clamp(face.width / ExpandedPackSize.x, 0.5f, 1f);
            float height = HeaderHeight * scale;
            Rect header = new(face.xMin + 5f * scale, face.yMax - height, face.width - 10f * scale, height - 4f * scale);
            Fill(vh, header, new Color(0.25f, 0.25f, 0.25f, 0.64f),
                new Color(0.32f, 0.32f, 0.32f, 0.72f));
            Disc(vh, new Vector2(face.xMin + (Padding + 5f) * scale, face.yMax - height * 0.55f), 5f * scale, accent);
            for (int i = 0; i < 3; i++)
            {
                Rect control = new(face.xMax - (Padding + 5f + i * 12f) * scale,
                    face.yMax - height * 0.65f, 7f * scale, 5f * scale);
                Fill(vh, control, Muted, Muted);
            }
        }
        backgroundVertexCount = vh.currentVertCount;
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

    private static void DrawBirthdayStars(VertexHelper vh, Rect face, BattleUiStyleSettings style,
        float border, float scale, float opacity)
    {
        float shortSide = Mathf.Min(face.width, face.height);
        if (shortSide < style.birthdayStarMinPanelSize) return;
        float size = Mathf.Min(shortSide * style.birthdayStarSizeRatio, style.birthdayStarMaxSize);
        float inset = border + 2f * scale;
        if (size <= 0f || inset + size > Mathf.Min(HeaderHeight * scale, face.height - border)) return;
        Color tint = style.highlight;
        tint.a *= opacity;
        int count = 0;
        for (int corner = 0; corner < 2 && count < Mathf.Clamp(style.birthdayStarCount, 0, 2); corner++)
        {
            bool right = corner == 0;
            float gutter = (Padding + (right ? 36f : 18f)) * scale;
            if (inset + size > gutter - 2f * scale) continue;
            Vector2 center = new(right ? face.xMax - inset - size * 0.5f : face.xMin + inset + size * 0.5f,
                face.yMax - inset - size * 0.5f);
            for (int layer = 0; layer < 2; layer++)
            {
                Color star = layer == 0 ? style.outerFrame : tint;
                if (layer == 0) star.a *= opacity;
                int first = vh.currentVertCount;
                vh.AddVert(center, star, Vector2.zero);
                for (int i = 0; i < 10; i++)
                {
                    float angle = (90f + i * 36f) * Mathf.Deg2Rad;
                    float radius = size * (layer == 0 ? 1f : 0.72f) * (i % 2 == 0 ? 0.5f : 0.22f);
                    vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, star, Vector2.zero);
                }
                for (int i = 0; i < 10; i++)
                    vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % 10);
            }
            count++;
        }
    }

    private static Rect Expand(Rect rect, float amount, Vector2 offset)
    {
        return new Rect(rect.xMin - amount + offset.x, rect.yMin - amount + offset.y,
            rect.width + amount * 2f, rect.height + amount * 2f);
    }

    private static Vector2 Contour(Rect rect, int corner, int step)
    {
        float radius = Mathf.Min(corner == 1 ? 28f : corner == 3 ? 18f : 12f,
            Mathf.Min(rect.width, rect.height) * 0.22f);
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

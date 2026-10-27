using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public enum BattleUiStyle { pixelStyleUi, comicStyleUi, VectorStyleUi, MahoShojo, Stylish, Birthday, Paper, CMD, JapanTvUi }

[Serializable]
public sealed class BattleUiTitleCharacterTransform
{
    public Vector2 offset;
    public Vector2 scale = Vector2.one;
    public float rotation;
    public float shear;
}

[Serializable]
public sealed class BattleUiTitleMotionSettings
{
    public bool enabled = true;
    public string targetParent = "MissionDetail";
    public string targetName = "Title";
    public bool allowOverflow = true;
    [Min(0f)] public float overflowMargin = 0.15f;
    public Vector2 scale = new(0.85f, 1f);
    public bool transformCharacters = true;
    [Tooltip("Repeating character transforms. Offsets use character height as the unit.")]
    public BattleUiTitleCharacterTransform[] characters =
    {
        new() { offset = new Vector2(0f, 0.025f), scale = new Vector2(1.04f, 1.04f), rotation = -5f, shear = 0.03f },
        new() { offset = new Vector2(0f, -0.025f), scale = new Vector2(0.96f, 0.96f), rotation = 5f, shear = -0.03f }
    };
    public bool curveLines;
    public float curveHeight = 0.08f;
    public bool bounce = true;
    [Min(0f)] public float bounceAmplitude = 0.08f;
    [Min(0f)] public float characterDelay = 0.04f;
    [Min(0.01f)] public float bounceDuration = 0.35f;
    public bool accentCharacters = true;
    public Color cyan = new Color32(0x00, 0xE7, 0xDF, 255);
    public Color magenta = new Color32(0xEF, 0x16, 0xEB, 255);
    public Color yellow = new Color32(0xFF, 0xF0, 0x2B, 255);
}

[Serializable]
public sealed class BattleUiStyleSettings
{
    public const float DefaultCombatBackgroundAlpha = 0.5f;
    public const float DefaultLockedSlotAlpha = 0.4f;
    [Header("Inventory Lock Mark")]
    [Range(0.1f, 0.9f)] public float lockMarkSizeRatio = 0.55f;
    [Range(0.02f, 0.2f)] public float lockMarkStrokeRatio = 0.075f;
    [Range(0f, 1f)] public float lockMarkAlpha = 0.75f;
    [Range(0f, 1f)] public float lockMarkVariation = 0.65f;
    [Range(0f, 1f)] public float lockMarkInkNoise = 0.35f;
    [Range(0f, 1f)] public float combatBackgroundAlpha = DefaultCombatBackgroundAlpha;
    [Range(0f, 1f)] public float lockedSlotAlpha = DefaultLockedSlotAlpha;
    [Tooltip("기준 픽셀 크기입니다. UI의 회전·확대·변형을 따라 격자도 함께 변형됩니다.")]
    [Min(1f)] public float logicalPixelSize = 2f;
    [Min(1f)] public float textPixelSize = 1f;
    [Range(0.2f, 1f)] public float surfaceShade = 0.55f;
    [Range(0.2f, 1f)] public float frameShade = 0.6f;
    [Range(1, 3)] public int framePixels = 1;
    public Color background = new Color32(0x2D, 0x1C, 0x20, 255);
    public Color innerLine = new Color32(0xF3, 0xEB, 0xDD, 255);
    public Color outerFrame = new Color32(0xA4, 0x9E, 0x98, 255);
    public Color status = new Color32(0x41, 0x9A, 0x50, 255);
    [Tooltip("한글 픽셀 글꼴을 지정할 수 있습니다. 없는 글자는 기존 한글 글꼴로 표시합니다.")]
    public Font font;
    public TMPro.TMP_FontAsset fontAsset;
    public Material panelMaterial;
    public Material textMaterial;
    public Color danger = new Color32(0xEE, 0x44, 0x55, 255);
    public Color energy = new Color32(0x40, 0xA8, 0xEF, 255);
    public Color highlight = new Color32(0xF5, 0xC8, 0x57, 255);
    public Color comicBackground = new Color32(0x12, 0x10, 0x16, 255);
    public Color comicPaper = new Color32(0xFA, 0xF5, 0xE9, 255);
    public Color comicAccent = new Color32(0xE6, 0x23, 0x32, 255);
    [Header("Cartoon Poster")]
    public Color comicInk = new Color32(0x10, 0x10, 0x10, 255);
    public Color comicWhite = new Color32(0xFF, 0xF8, 0xE8, 255);
    public Color comicRed = new Color32(0xE5, 0x25, 0x20, 255);
    public Color comicYellow = new Color32(0xFF, 0xC4, 0x00, 255);
    [Range(6f, 10f)] public float comicShadowOffset = 8f;
    [Range(6f, 20f)] public float comicDotSpacing = 10f;
    [Range(0.5f, 2f)] public float comicDotRadius = 1.2f;
    public Color comicSelection = new Color32(0xE6, 0x23, 0x32, 255);
    internal Color ComicSelectionColor => ((Color32)comicSelection).Equals(new Color32(0x80, 0xFF, 0x00, 255))
        ? new Color32(0xE6, 0x23, 0x32, 255) : comicSelection;
    [Range(8f, 12f)] public float comicOuterWidth = 10f;
    [Range(4f, 6f)] public float comicInnerWidth = 5f;
    [Range(-7f, 7f)] public float comicPanelAngle = -5f;
    [Range(0f, 4f)] public float comicAngleVariation = 2f;
    [Range(0f, 0.2f)] public float comicPerspectiveRatio = 0.14f;
    [Range(0f, 24f)] public float comicPerspectiveMax = 18f;
    [Range(0f, 2f)] public float comicInventoryTopPerspective = 0.6f;
    [Range(0f, 2f)] public float comicInventoryMiddlePerspective = 1f;
    [Range(0f, 2f)] public float comicInventoryBottomPerspective = 1.6f;
    [Range(0f, 0.24f)] public float comicInventoryExpandedInset = 0.18f;
    [Range(0f, 1f)] public float comicInventoryExpandedTaperScale = 0.5f;
    [Range(0f, 1f)] public float comicInventoryCompactBottomAngleScale = 0.5f;
    [Range(0f, 0.2f)] public float comicSpikeRatio = 0.12f;
    public TMPro.TMP_FontAsset comicTitleFont;
    public BattleUiTitleMotionSettings comicTitleMotion = new()
    {
        scale = Vector2.one, allowOverflow = false, bounce = false, accentCharacters = false,
        characters = new BattleUiTitleCharacterTransform[]
        {
            new() { scale = new Vector2(1.25f, 1.25f), rotation = -10f },
            new() { scale = new Vector2(0.85f, 0.85f), rotation = 7f },
            new() { scale = new Vector2(1.1f, 1.1f), rotation = -4f },
            new() { scale = Vector2.one, rotation = 10f }
        }
    };
    [Header("Stylish Ink Neon")]
    public Color inkBackground = new Color32(0x10, 0x10, 0x18, 255);
    public Color inkLime = new Color32(0xB8, 0xFF, 0x00, 255);
    public Color inkMagenta = new Color32(0xFF, 0x39, 0xC8, 255);
    public Color inkCyan = new Color32(0x22, 0xE8, 0xFF, 255);
    public Color inkText = Color.white;
    [Range(0.4f, 0.95f)] public float inkGlassOpacity = 0.72f;
    [Range(0f, 24f)] public float inkBackdropBlur = 12f;
    [Range(-4f, 4f)] public float inkPanelAngle = 3f;
    [Range(3f, 5f)] public float inkOutlineWidth = 4f;
    [Range(8f, 14f)] public float inkGlowWidth = 10f;
    [Range(0f, 0.12f)] public float inkLobeRatio = 0.055f;
    public TMPro.TMP_FontAsset inkTitleFont;
    public BattleUiTitleMotionSettings inkTitleMotion = new()
    {
        scale = Vector2.one, allowOverflow = false, accentCharacters = false
    };
    [Header("Cartoon / Stylish Titles")]
    [Range(1.8f, 2.4f)] public float posterTitleBodyRatio = 2.1f;
    [Range(0.06f, 0.1f)] public float posterTitleStrokeRatio = 0.08f;
    [Range(0f, 0.15f)] public float posterTitleTopOverflow = 0.15f;
    [Min(0f)] public float mahoCornerRadius = 14f;
    [Min(0f)] public float mahoBorderWidth = 1f;
    public Vector2 mahoShadowOffset = new(3f, -4f);
    public Color mahoShadow = new(0f, 0f, 0f, 0.08f);
    [Min(0f)] public float stylishBorderWidth = 4f;
    [Range(0f, 0.5f)] public float stylishTitleShear = 0.18f;
    [Min(0f)] public float birthdayBorderWidth = 2f;
    [Range(0, 2)] public int birthdayStarCount = 2;
    [Range(0f, 0.25f)] public float birthdayStarSizeRatio = 0.12f;
    [Min(0f)] public float birthdayStarMaxSize = 24f;
    [Min(0f)] public float birthdayStarMinPanelSize = 120f;
    public TMPro.TMP_FontAsset titleFont;
    [Range(0f, 0.4f)] public float titleOutlineWidth = 0.16f;
    public Vector2 titleShadowOffset = new(1f, -1f);
    [Min(0f)] public float titlePadding = 2f;
    [Range(1f, 1.8f)] public float titleSizeMultiplier = 1.4f;
    public BattleUiTitleMotionSettings stylishTitleMotion = new();
    [Min(4f)] public float stylishDotSpacing = 10f;
    [Range(0f, 2f)] public float stylishDotRadius = 0.8f;
    [Range(0f, 0.25f)] public float surfacePatternAlpha = 0.12f;
    [Header("Stylish Arcade")]
    public Color stylishMagenta = new Color32(0xA5, 0x1C, 0x69, 255);
    public Color stylishNeon = new Color32(0x25, 0xE5, 0xF4, 255);
    public Color stylishCta = new Color32(0x20, 0xDE, 0xD5, 255);
    [Range(6f, 10f)] public float stylishPanelAngle = 8f;
    [Range(6f, 12f)] public float stylishGlowWidth = 8f;
    [Range(4f, 8f)] public float stylishShadowOffset = 6f;
    [Range(-12f, -8f)] public float stylishTitleAngle = -10f;
    [Min(0.01f)] public float stylishHoverDuration = 0.12f;
    [Min(0.01f)] public float stylishPressDuration = 0.08f;
    [Min(0.01f)] public float stylishEnterDuration = 0.25f;
    [Min(0.01f)] public float stylishCloseDuration = 0.12f;
    [Range(1f, 1.1f)] public float stylishHoverScale = 1.04f;
    [Range(0.9f, 1f)] public float stylishPressScale = 0.96f;
    [Range(0.25f, 0.45f)] public float stylishConfirmDuration = 0.35f;
    [Min(0f)] public float stylishCardRadius = 24f;
    [Range(0f, 0.5f)] public float stylishColorBandAlpha = 0.32f;
    [Range(0.1f, 0.35f)] public float stylishColorBandHeight = 0.24f;
    [Header("Paper")]
    [Min(0f)] public float paperBorderWidth = 6f;
    public Vector2 paperShadowOffset = new(2f, -3f);
    public Color paperShadow = new(0f, 0f, 0f, 0.1f);
    [Range(0f, 2f)] public float paperDecorationAngle = 2f;
    [Range(0f, 1f)] public float paperLabelAngle = 1f;
    [Range(0.02f, 0.05f)] public float paperCornerMin = 0.02f;
    [Range(0.02f, 0.05f)] public float paperCornerMax = 0.05f;
    [Range(0f, 0.2f)] public float paperGrainStrength = 0.035f;
    [Range(0f, 0.3f)] public float paperStainStrength = 0.025f;
    [Range(0f, 0.4f)] public float paperCreaseStrength = 0.22f;
    [Range(0f, 0.3f)] public float paperFoldSize = 0.18f;
    [Min(0.1f)] public float paperFiberScale = 0.7f;
    [Range(0f, 1f)] public float paperSheetBorderScale = 0.22f;
    [Min(0f)] public float paperPunchRadius = 2.5f;
    [Min(12f)] public float paperPunchSpacing = 28f;
    [Min(0f)] public float paperPunchInset = 9f;
    [Min(0f)] public float paperPunchEdgeInset = 14f;
    public Color paperInventoryColor = new Color32(0xE5, 0xEB, 0xD9, 255);
    public Color paperDetailColor = new Color32(0xE8, 0xE2, 0xEF, 255);
    [Min(1f)] public float paperPatternSpacing = 16f;
    [Range(0.04f, 0.07f)] public float paperPatternAlpha = 0.055f;
    [Min(0f)] public float paperLineWidth = 0.6f;
    [Min(0f)] public float paperDotRadius = 0.8f;
    [Header("CMD")]
    [Min(8f)] public float cmdFrameFontSize = 14f;
    [Min(0.05f)] public float cmdCursorBlinkSeconds = 0.5f;
    [Header("JapanTvUi")]
    public Color japanTvBurstOuter = new Color32(0xEF, 0x26, 0x38, 255);
    public Color japanTvYellow = new Color32(0xFF, 0xF1, 0x2B, 255);
    public Color japanTvOrange = new Color32(0xFF, 0x96, 0x00, 255);
    public Color japanTvRed = new Color32(0xE5, 0x1B, 0x2B, 255);
    public Color japanTvShadow = new Color32(0x54, 0x10, 0x19, 255);
    public Color japanTvInk = new Color32(0x16, 0x12, 0x18, 255);
    public Color japanTvCyan = new Color32(0x00, 0xBF, 0xCB, 255);
    public Color japanTvPink = new Color32(0xF3, 0x29, 0x8B, 255);
    public Color japanTvPurple = new Color32(0x8C, 0x35, 0xB6, 255);
    public Color japanTvGreen = new Color32(0x20, 0xA6, 0x4B, 255);
    public Color japanTvRayPink = new Color32(0xF0, 0x00, 0x87, 255);
    public Color japanTvRayCyan = new Color32(0x19, 0x7F, 0xA8, 255);
    public Color japanTvCheckDark = new Color32(0x24, 0x15, 0x27, 255);
    public Color japanTvCheckPink = new Color32(0xBD, 0x17, 0x6B, 255);
    public Color japanTvBulb = new Color32(0xFF, 0xF2, 0xB0, 255);
    [Range(-15f, 0f)] public float japanTvTitleAngle = -8f;
    [Range(1.6f, 2f)] public float japanTvTitleBodyRatio = 1.8f;
    [Range(0.04f, 0.12f)] public float japanTvStrokeRatio = 0.08f;
    [Min(1f)] public float japanTvOuterStrokeScale = 1.8f;
    public Vector2 japanTvDepthOffset = new(0.3f, -0.7f);
    [Min(0.01f)] public float japanTvEnterSeconds = 0.28f;
    public float japanTvEnterScale = 0.85f;
    public float japanTvOvershootScale = 1.08f;
    [Min(0.1f)] public float japanTvBulbPeriod = 1.2f;
    [Min(1f)] public float japanTvBorder = 4f;
    [Min(12f)] public float japanTvBulbSpacing = 26f;
    [Min(1f)] public float japanTvBulbRadius = 4f;
}

[DisallowMultipleComponent]
public sealed class BattleUiStyleController : MonoBehaviour
{
    private static BattleUiStyleController active;
    private Canvas screenToneReferenceCanvas;

    public bool EnableScreenTone
    {
        get => presentation != null && presentation.EnableScreenTone;
        set { if (presentation != null) presentation.SetScreenToneEnabled(value); }
    }

    public BattleUiScreenToneSettings ScreenToneSettings => presentation != null ? presentation.ScreenToneSettings : null;
    internal static BattleUiScreenToneSettings ScreenTone => active != null && active.isActiveAndEnabled &&
        active.EnableScreenTone && active.ScreenToneSettings != null && active.ScreenToneSettings.ToneStrength > 0f
        ? active.ScreenToneSettings : null;
    internal static float ScreenToneCanvasScale => active != null && active.screenToneReferenceCanvas != null
        ? active.screenToneReferenceCanvas.scaleFactor : 1f;
    private readonly Dictionary<Graphic, BattleUiStyleMeshEffect> targets = new();
    private readonly List<Graphic> removed = new();
    private BattleShowPresentationManager presentation;
    private BattleScreenGridOverlay grid;
    private Material panelMaterial, textMaterial, spriteMaterial;
    private Material mahoMaterial;
    private Material paperMaterial, paperMaskMaterial;
    private Material inkPanelMaterial;
    private RTHandle inkBackground;
    private Camera inkCamera;
    private BattleColorGradingController inkGrading;
    private bool inkBackgroundVisible;
    private readonly Material[] lockMarkMaterials = new Material[6];
    private Font bitmapFont;
    private Texture2D bitmapTexture;
    private Material bitmapMaterial;

    internal static bool IsPixel => active != null && active.isActiveAndEnabled &&
        active.presentation != null && active.presentation.UiStyle == BattleUiStyle.pixelStyleUi;
    internal static bool IsComic => active != null && active.isActiveAndEnabled &&
        active.presentation != null && active.presentation.UiStyle == BattleUiStyle.comicStyleUi;
    internal static bool IsMahoShojo => active != null && active.isActiveAndEnabled &&
        active.presentation != null && active.presentation.UiStyle == BattleUiStyle.MahoShojo;
    internal static bool IsStylish => active != null && active.isActiveAndEnabled &&
        active.presentation != null && active.presentation.UiStyle == BattleUiStyle.Stylish;
    internal static bool IsBirthday => active != null && active.isActiveAndEnabled &&
        active.presentation != null && active.presentation.UiStyle == BattleUiStyle.Birthday;
    internal static bool IsPaper => active != null && active.isActiveAndEnabled &&
        active.presentation != null && active.presentation.UiStyle == BattleUiStyle.Paper;
    internal static bool IsCmd => active != null && active.isActiveAndEnabled &&
        active.presentation != null && active.presentation.UiStyle == BattleUiStyle.CMD;
    internal static bool IsJapanTvUi => active != null && active.isActiveAndEnabled &&
        active.presentation != null && active.presentation.UiStyle == BattleUiStyle.JapanTvUi;
    internal static BattleUiStyleSettings Settings => active != null && active.presentation != null
        ? active.presentation.UiStyleSettings : null;
    internal static TMPro.TMP_FontAsset ThemeTmpFont => active != null && active.isActiveAndEnabled && active.presentation != null
        ? BattleUiThemeFonts.LoadTmp(active.presentation.UiStyle) : null;
    internal static float PixelScale => active != null && active.presentation != null
        ? active.presentation.UiPixelScale : 1f;

    public void Configure(BattleShowPresentationManager owner)
    {
        presentation = owner;
        active = this;
        grid = GetComponent<BattleScreenGridOverlay>();
        if (grid == null) grid = gameObject.AddComponent<BattleScreenGridOverlay>();
        RefreshSettings();
    }

    private void Start()
    {
        inkCamera = Camera.main;
        inkGrading = FindFirstObjectByType<BattleColorGradingController>();
        // Existing UI may have been built before SpriteManager.Awake.
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (canvas.name.StartsWith("Battle", System.StringComparison.Ordinal) || canvas.sortingOrder >= 500)
                RegisterRoot(canvas.transform);
        RefreshSettings();
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
        active = this;
        Canvas.preWillRenderCanvases += ApplyVisibleTargets;
        if (presentation != null) RefreshSettings();
    }

    public static void RegisterRoot(Transform root)
    {
        if (active == null || root == null) return;
        foreach (Graphic target in root.GetComponentsInChildren<Graphic>(true))
            RegisterGraphic(target);
    }

    public static void RegisterGraphic(Graphic target)
    {
        if (active == null || target == null || target.name.StartsWith("Cmd", StringComparison.Ordinal) ||
            target.name == "StyleTitleTMP" || target.name.StartsWith("JapanTvUi", StringComparison.Ordinal) ||
            target is BattleUiTitleBacking || target.GetComponentInParent<BattleDebugUI>(true) != null) return;
        BattleUiOpticsCanvas.Register(target);
        // Unframed rule captions need their own contrast against the world.
        if (target is Text && target.name == "FallbackLabel" && target.transform.parent != null &&
            target.transform.parent.name == "VisualRoot") return;
        if (active.screenToneReferenceCanvas == null && target.canvas != null &&
            target.canvas.rootCanvas.sortingOrder >= 500)
            active.screenToneReferenceCanvas = target.canvas.rootCanvas;
        if (active.targets.TryGetValue(target, out var registered))
        {
            registered.BindInteraction();
            return;
        }
        if (!(target is Text) && !(target is TMPro.TextMeshProUGUI) && !(target is Image) && !(target is BattleUiPanelDecoration)) return;
        BattleUiStyleMeshEffect effect = target.GetComponent<BattleUiStyleMeshEffect>();
        if (effect == null) effect = target.gameObject.AddComponent<BattleUiStyleMeshEffect>();
        effect.Bind(target);
        active.targets.Add(target, effect);
        if (target is Text)
        {
            Button owner = target.GetComponentInParent<Button>(true);
            if (owner != null) RegisterRoot(owner.transform);
            for (Transform parent = target.transform.parent; parent != null; parent = parent.parent)
            {
                if (parent.TryGetComponent(out Image surface)) RegisterGraphic(surface);
                if (parent.TryGetComponent<Canvas>(out _)) break;
            }
        }
    }

    public static void RegisterSpeechFrame(Image outline, Image fill)
    {
        if (active == null) return;
        RegisterGraphic(outline);
        RegisterGraphic(fill);
        if (outline != null) active.targets[outline].SetSpeechFrame(false, outline.rectTransform);
        if (fill != null) active.targets[fill].SetSpeechFrame(true, outline != null ? outline.rectTransform : null);
    }

    public static void RegisterSpeechTail(Image outline, Image fill, RectTransform frame)
    {
        if (active == null) return;
        RegisterGraphic(outline);
        RegisterGraphic(fill);
        if (outline != null) active.targets[outline].SetSpeechSurface(frame);
        if (fill != null) active.targets[fill].SetSpeechSurface(frame);
    }

    public static void RegisterPanel(Image image, bool decoratedSurface = true)
    {
        RegisterGraphic(image);
        if (active != null && image != null) active.targets[image].SetPanel(decoratedSurface);
    }

    internal static void PrepareMaterialTarget(Graphic target)
    {
        if (active != null && target != null && active.targets.TryGetValue(target, out var effect))
            effect.ReleaseMaterialOverride();
    }

    internal static TMPro.TextMeshProUGUI CmdText(Graphic target) => active != null && target != null &&
        active.targets.TryGetValue(target, out var effect) ? effect.CmdText : null;

    public void RefreshSettings()
    {
        if (presentation == null) return;
        if (grid != null) grid.SetEffect(isActiveAndEnabled && presentation.ScreenGridEnabled,
            presentation.ScreenGridContrast);
        foreach (var entry in targets)
            if (entry.Key != null)
            {
                entry.Key.SetVerticesDirty();
                entry.Value.RefreshTmpTitle();
            }
        ApplyVisibleTargets();
    }

    private void ApplyVisibleTargets()
    {
        if (presentation == null) return;
        BattleUiStyleSettings settings = presentation.UiStyleSettings;
        Font themeFont = BattleUiThemeFonts.Load(presentation.UiStyle);
        bool pixel = IsPixel;
        if ((pixel || IsComic || IsMahoShojo || IsStylish || IsBirthday || IsPaper) && panelMaterial == null)
        {
            panelMaterial = CreateMaterial("BattlePixelPanel");
            textMaterial = CreateMaterial("BattlePixelText");
            spriteMaterial = CreateMaterial("BattlePixelText");
            bitmapFont = BattleUiPixelFont.Create(out bitmapTexture, out bitmapMaterial);
        }
        if (IsMahoShojo && mahoMaterial == null)
            mahoMaterial = CreateMaterial("BattleMahoUi");
        if (IsStylish)
        {
            if (inkPanelMaterial == null) inkPanelMaterial = CreateMaterial("BattleInkPanel");
            if (inkPanelMaterial != null)
            {
                inkPanelMaterial.SetFloat("_InkOpacity", Mathf.Clamp(settings.inkGlassOpacity, 0.4f, 0.95f));
                inkPanelMaterial.SetFloat("_InkBlurPixels", Mathf.Clamp(settings.inkBackdropBlur, 0f, 24f));
            }
        }
        if (IsPaper)
        {
            if (paperMaterial == null) paperMaterial = CreateMaterial("BattlePaperPanel");
            if (paperMaskMaterial == null)
            {
                paperMaskMaterial = CreateMaterial("BattlePaperPanel");
                if (paperMaskMaterial != null) paperMaskMaterial.SetFloat("_UseSpriteMask", 1f);
            }
            Vector4 texture = new(settings.paperGrainStrength, settings.paperStainStrength,
                settings.paperCreaseStrength, Mathf.Max(0.1f, settings.paperFiberScale));
            if (paperMaterial != null)
            {
                paperMaterial.SetVector("_PaperTexture", texture);
                paperMaterial.SetVector("_PaperPunch", new Vector4(settings.paperPunchRadius,
                    Mathf.Max(12f, settings.paperPunchSpacing), settings.paperPunchInset, settings.paperPunchEdgeInset));
                paperMaterial.SetVector("_PaperPattern", new Vector4(Mathf.Max(1f, settings.paperPatternSpacing),
                    Mathf.Clamp(settings.paperPatternAlpha, 0.04f, 0.07f), settings.paperLineWidth, settings.paperDotRadius));
            }
            if (paperMaskMaterial != null)
            {
                paperMaskMaterial.SetVector("_PaperTexture", texture);
                paperMaskMaterial.SetVector("_PaperPunch", new Vector4(settings.paperPunchRadius,
                    Mathf.Max(12f, settings.paperPunchSpacing), settings.paperPunchInset, settings.paperPunchEdgeInset));
            }
        }
        int lockStyle = IsComic ? 1 : IsMahoShojo ? 2 : IsStylish ? 3 : IsBirthday ? 4 : IsPaper ? 5 : 0;
        if (lockMarkMaterials[lockStyle] == null)
            lockMarkMaterials[lockStyle] = CreateMaterial("BattleInventoryLockMark");
        Material lockMarkMaterial = lockMarkMaterials[lockStyle];
        if (lockMarkMaterial != null)
        {
            lockMarkMaterial.SetVector("_LockStyle", new Vector4(lockStyle, settings.lockMarkStrokeRatio,
                settings.lockMarkVariation, settings.lockMarkInkNoise));
        }
        removed.Clear();
        bool visibleInk = false;
        foreach (var entry in targets)
        {
            Graphic target = entry.Key;
            if (target == null) { removed.Add(target); continue; }
            if (!target.isActiveAndEnabled || target.canvasRenderer.GetInheritedAlpha() <= 0.001f ||
                (target.canvasRenderer.cull && !entry.Value.HasOverflowTitle && !entry.Value.HasReplacementText))
            {
                entry.Value.HideAppearance();
                continue;
            }
            entry.Value.ApplyAppearance(pixel, settings, panelMaterial, textMaterial, spriteMaterial, bitmapFont, mahoMaterial, themeFont,
                paperMaterial, paperMaskMaterial, lockMarkMaterial, inkPanelMaterial);
            visibleInk |= IsStylish && settings.inkBackdropBlur > 0f && entry.Value.UsesInkBackground;
        }
        foreach (Graphic target in removed) targets.Remove(target);
        inkBackgroundVisible = visibleInk;
        if (!visibleInk) ReleaseInkBackground();
    }

    internal static bool WantsInkBackground(Camera camera)
    {
        if (!Application.isPlaying || !IsStylish || !active.inkBackgroundVisible || camera == null) return false;
        Camera target = active.inkGrading != null && active.inkGrading.TargetCamera != null
            ? active.inkGrading.TargetCamera : active.inkCamera;
        return target == camera;
    }

    internal static RTHandle PrepareInkBackground(Camera camera, RenderTextureDescriptor descriptor)
    {
        if (!WantsInkBackground(camera)) return null;
        Vector4 screen = new(descriptor.width, descriptor.height, 1f / descriptor.width, 1f / descriptor.height);
        descriptor.width = Mathf.Max(1, (descriptor.width + 3) / 4);
        descriptor.height = Mathf.Max(1, (descriptor.height + 3) / 4);
        descriptor.depthBufferBits = 0;
        descriptor.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
        descriptor.msaaSamples = 1;
        descriptor.bindMS = false;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        descriptor.memoryless = RenderTextureMemoryless.None;
        RenderingUtils.ReAllocateHandleIfNeeded(ref active.inkBackground, descriptor,
            FilterMode.Bilinear, TextureWrapMode.Clamp, name: "BattleInkBackground");
        Shader.SetGlobalTexture("_BattleInkBackground", active.inkBackground.rt);
        Shader.SetGlobalVector("_BattleInkScreenSize", screen);
        Shader.SetGlobalFloat("_BattleInkBackgroundReady", 1f);
        return active.inkBackground;
    }

    private void ReleaseInkBackground()
    {
        bool owned = inkBackground != null;
        inkBackground?.Release();
        inkBackground = null;
        if (!owned || active != this) return;
        Shader.SetGlobalTexture("_BattleInkBackground", Texture2D.blackTexture);
        Shader.SetGlobalFloat("_BattleInkBackgroundReady", 0f);
    }

    private static Material CreateMaterial(string resource)
    {
        Shader shader = Resources.Load<Shader>(resource);
        return shader != null ? new Material(shader) { hideFlags = HideFlags.HideAndDontSave } : null;
    }

#if UNITY_EDITOR
    public void ReleaseEditorPreview()
    {
        if (!Application.isPlaying) OnDestroy();
    }

    public void DrawEditorPreview(BattleShowPresentationManager owner, Transform root, System.Action render)
    {
        if (Application.isPlaying) return;
        BattleUiStyleController previous = active;
        try
        {
            active = this;
            presentation = owner;
            RegisterRoot(root);
            RefreshSettings();
            Canvas.ForceUpdateCanvases();
            render();
        }
        finally { active = previous; }
    }
#endif

    private void OnDisable()
    {
        inkBackgroundVisible = false;
        ReleaseInkBackground();
        Canvas.preWillRenderCanvases -= ApplyVisibleTargets;
        foreach (var entry in targets)
            if (entry.Value != null) entry.Value.RestoreAppearance();
        if (grid != null) grid.SetEffect(false, 0f);
        if (active == this) active = null;
        BattleUiOpticsCanvas.RefreshScreenTone();
    }

    private void OnDestroy()
    {
        ReleaseInkBackground();
        Release(inkPanelMaterial);
        Release(panelMaterial);
        Release(textMaterial);
        Release(spriteMaterial);
        Release(mahoMaterial);
        Release(paperMaterial);
        foreach (Material material in lockMarkMaterials) Release(material);
        Release(paperMaskMaterial);
        Release(bitmapFont);
        Release(bitmapTexture);
        Release(bitmapMaterial);
    }

    private static void Release(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}

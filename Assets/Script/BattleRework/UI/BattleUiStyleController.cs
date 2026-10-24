using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum BattleUiStyle { pixelStyleUi, comicStyleUi, VectorStyleUi, MahoShojo, Stylish, Birthday }

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
}

[DisallowMultipleComponent]
public sealed class BattleUiStyleController : MonoBehaviour
{
    private static BattleUiStyleController active;
    private readonly Dictionary<Graphic, BattleUiStyleMeshEffect> targets = new();
    private readonly List<Graphic> removed = new();
    private BattleShowPresentationManager presentation;
    private BattleScreenGridOverlay grid;
    private Material panelMaterial, textMaterial, spriteMaterial;
    private Material mahoMaterial;
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
        // Existing UI may have been built before SpriteManager.Awake.
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (canvas.name.StartsWith("Battle", System.StringComparison.Ordinal) || canvas.sortingOrder >= 500)
                RegisterRoot(canvas.transform);
        RefreshSettings();
    }

    private void OnEnable()
    {
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
        if (active == null || target == null || target.GetComponentInParent<BattleDebugUI>(true) != null) return;
        if (active.targets.TryGetValue(target, out var registered))
        {
            registered.BindInteraction();
            return;
        }
        if (!(target is Text) && !(target is Image) && !(target is BattleUiPanelDecoration)) return;
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
        if (outline != null) active.targets[outline].SetSpeechFrame(false);
        if (fill != null) active.targets[fill].SetSpeechFrame(true);
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
        if ((pixel || IsComic || IsMahoShojo || IsStylish || IsBirthday) && panelMaterial == null)
        {
            panelMaterial = CreateMaterial("BattlePixelPanel");
            textMaterial = CreateMaterial("BattlePixelText");
            spriteMaterial = CreateMaterial("BattlePixelText");
            bitmapFont = BattleUiPixelFont.Create(out bitmapTexture, out bitmapMaterial);
        }
        if (IsMahoShojo && mahoMaterial == null)
            mahoMaterial = CreateMaterial("BattleMahoUi");
        removed.Clear();
        foreach (var entry in targets)
        {
            Graphic target = entry.Key;
            if (target == null) { removed.Add(target); continue; }
            if (!target.isActiveAndEnabled || target.canvasRenderer.GetInheritedAlpha() <= 0.001f ||
                (target.canvasRenderer.cull && !entry.Value.HasOverflowTitle))
            {
                entry.Value.HideAppearance();
                continue;
            }
            entry.Value.ApplyAppearance(pixel, settings, panelMaterial, textMaterial, spriteMaterial, bitmapFont, mahoMaterial, themeFont);
        }
        foreach (Graphic target in removed) targets.Remove(target);
    }

    private static Material CreateMaterial(string resource)
    {
        Shader shader = Resources.Load<Shader>(resource);
        return shader != null ? new Material(shader) { hideFlags = HideFlags.HideAndDontSave } : null;
    }

    private void OnDisable()
    {
        Canvas.preWillRenderCanvases -= ApplyVisibleTargets;
        foreach (var entry in targets)
            if (entry.Value != null) entry.Value.RestoreAppearance();
        if (grid != null) grid.SetEffect(false, 0f);
        if (active == this) active = null;
    }

    private void OnDestroy()
    {
        if (panelMaterial != null) Destroy(panelMaterial);
        if (textMaterial != null) Destroy(textMaterial);
        if (spriteMaterial != null) Destroy(spriteMaterial);
        if (mahoMaterial != null) Destroy(mahoMaterial);
        if (bitmapFont != null) Destroy(bitmapFont);
        if (bitmapTexture != null) Destroy(bitmapTexture);
        if (bitmapMaterial != null) Destroy(bitmapMaterial);
    }
}

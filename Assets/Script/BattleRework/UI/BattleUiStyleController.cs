using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum BattleUiStyle { pixelStyleUi, comicStyleUi, VectorStyleUi }

[Serializable]
public sealed class BattleUiStyleSettings
{
    [Min(1f)] public float logicalPixelSize = 2f;
    public Color background = new Color32(0x2D, 0x1C, 0x20, 255);
    public Color innerLine = new Color32(0xF3, 0xEB, 0xDD, 255);
    public Color outerFrame = new Color32(0xA4, 0x9E, 0x98, 255);
    public Color status = new Color32(0x41, 0x9A, 0x50, 255);
    [Tooltip("한글 픽셀 글꼴을 지정할 수 있습니다. 없는 글자는 기존 한글 글꼴로 표시합니다.")]
    public Font font;
    public Material panelMaterial;
    public Material textMaterial;
}

[DisallowMultipleComponent]
public sealed class BattleUiStyleController : MonoBehaviour
{
    private static BattleUiStyleController active;
    private readonly Dictionary<Graphic, BattleUiStyleMeshEffect> targets = new();
    private readonly List<Graphic> removed = new();
    private BattleShowPresentationManager presentation;
    private BattleScreenGridOverlay grid;
    private Material panelMaterial, textMaterial;
    private Font bitmapFont;
    private Texture2D bitmapTexture;
    private Material bitmapMaterial;

    internal static bool IsPixel => active != null && active.isActiveAndEnabled &&
        active.presentation != null && active.presentation.UiStyle == BattleUiStyle.pixelStyleUi;
    internal static BattleUiStyleSettings Settings => active != null && active.presentation != null
        ? active.presentation.UiStyleSettings : null;

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
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.sortingOrder >= 500)
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
        if (active == null || target == null || active.targets.ContainsKey(target)) return;
        if (!(target is Text) && !(target is Image) && !(target is BattleUiPanelDecoration)) return;
        BattleUiStyleMeshEffect effect = target.GetComponent<BattleUiStyleMeshEffect>();
        if (effect == null) effect = target.gameObject.AddComponent<BattleUiStyleMeshEffect>();
        effect.Bind(target);
        active.targets.Add(target, effect);
    }

    public static void RegisterSpeechFrame(Image outline, Image fill)
    {
        if (active == null) return;
        RegisterGraphic(outline);
        RegisterGraphic(fill);
        if (outline != null) active.targets[outline].SetSpeechFrame(false);
        if (fill != null) active.targets[fill].SetSpeechFrame(true);
    }

    public static void RegisterPanel(Image image)
    {
        RegisterGraphic(image);
        if (active != null && image != null) active.targets[image].SetPanel();
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
            if (entry.Key != null) entry.Key.SetVerticesDirty();
        ApplyVisibleTargets();
    }

    private void ApplyVisibleTargets()
    {
        if (presentation == null) return;
        BattleUiStyleSettings settings = presentation.UiStyleSettings;
        bool pixel = IsPixel;
        if (pixel && panelMaterial == null)
        {
            panelMaterial = CreateMaterial("BattlePixelPanel");
            textMaterial = CreateMaterial("BattlePixelText");
            bitmapFont = BattleUiPixelFont.Create(out bitmapTexture, out bitmapMaterial);
        }
        removed.Clear();
        foreach (var entry in targets)
        {
            Graphic target = entry.Key;
            if (target == null) { removed.Add(target); continue; }
            if (!target.isActiveAndEnabled || target.canvasRenderer.cull) continue;
            entry.Value.ApplyAppearance(pixel, settings, panelMaterial, textMaterial, bitmapFont);
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
        if (bitmapFont != null) Destroy(bitmapFont);
        if (bitmapTexture != null) Destroy(bitmapTexture);
        if (bitmapMaterial != null) Destroy(bitmapMaterial);
    }
}

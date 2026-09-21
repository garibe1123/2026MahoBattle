using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BattleUIThemeController의 현재 Context Palette를 UI/Sprite Material instance에 전달합니다.
/// sharedMaterial을 직접 수정하지 않으며, 비활성화/파괴 시 원본 Material을 복원합니다.
/// AllIn1SpriteShader의 색 계열 Property만 설정하고 FX keyword/toggle은 건드리지 않습니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleUIShaderThemeBinder : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Graphic graphicTarget;
    [SerializeField] private Renderer rendererTarget;
    [SerializeField] private BattleUIThemeColorRole baseColorRole = BattleUIThemeColorRole.Glass;

    [Header("Theme Mapping")]
    [SerializeField, Range(0f, 1f)] private float alphaMultiplier = 1f;
    [SerializeField, Range(0f, 1f)] private float overlayStrength = 0.55f;
    [SerializeField, Range(0f, 1f)] private float gradientKeyInfluence = 0.18f;
    [SerializeField] private bool applyOverlay = true;
    [SerializeField] private bool applyGradient = true;

    private BattleUIThemeController themeController;
    private BattleUIThemeController subscribedTheme;
    private Material originalMaterial;
    private Material runtimeMaterial;
    private bool targetWasGraphic;

    public Material RuntimeMaterial => runtimeMaterial;

    public void Configure(BattleUIThemeColorRole role, float alpha = 1f)
    {
        baseColorRole = role;
        alphaMultiplier = Mathf.Clamp01(alpha);
        ApplyTheme();
    }

    private void Awake()
    {
        ResolveTargets();
        ResolveThemeController();
    }

    private void OnEnable()
    {
        ResolveTargets();
        ResolveThemeController();
        EnsureMaterialInstance();
        Subscribe();
        ApplyTheme();
    }

    private void Update()
    {
        if (themeController == null)
        {
            ResolveThemeController();
            Subscribe();
            ApplyTheme();
        }

        if (runtimeMaterial == null)
        {
            EnsureMaterialInstance();
            ApplyTheme();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
        RestoreOriginalMaterial();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        RestoreOriginalMaterial();
    }

    private void ResolveTargets()
    {
        if (graphicTarget == null)
            graphicTarget = GetComponent<Graphic>();

        if (graphicTarget == null && rendererTarget == null)
            rendererTarget = GetComponent<Renderer>();
    }

    private void ResolveThemeController()
    {
        if (themeController != null)
            return;

        themeController = BattleUIThemeController.Instance != null
            ? BattleUIThemeController.Instance
            : FindFirstObjectByType<BattleUIThemeController>(FindObjectsInactive.Include);
    }

    private void Subscribe()
    {
        if (subscribedTheme == themeController)
            return;

        Unsubscribe();
        subscribedTheme = themeController;
        if (subscribedTheme != null)
            subscribedTheme.ThemeChanged += HandleThemeChanged;
    }

    private void Unsubscribe()
    {
        if (subscribedTheme != null)
            subscribedTheme.ThemeChanged -= HandleThemeChanged;
        subscribedTheme = null;
    }

    private void HandleThemeChanged(BattleUIThemeProfile _)
    {
        ApplyTheme();
    }

    private void EnsureMaterialInstance()
    {
        if (runtimeMaterial != null)
            return;

        Material source = null;
        targetWasGraphic = graphicTarget != null;

        if (targetWasGraphic)
            source = graphicTarget.material;
        else if (rendererTarget != null)
            source = rendererTarget.sharedMaterial;

        if (source == null)
            return;

        originalMaterial = source;
        runtimeMaterial = new Material(source)
        {
            name = source.name + " [BattleTheme Instance]"
        };

        if (targetWasGraphic)
            graphicTarget.material = runtimeMaterial;
        else if (rendererTarget != null)
            rendererTarget.sharedMaterial = runtimeMaterial;
    }

    private void ApplyTheme()
    {
        if (runtimeMaterial == null)
            return;

        BattleUIThemeProfile theme = themeController != null
            ? themeController.CurrentProfile
            : null;
        if (theme == null)
            return;

        Color baseColor = ResolveRole(theme, baseColorRole);
        baseColor.a *= alphaMultiplier;

        if (runtimeMaterial.HasProperty("_Alpha"))
        {
            Color shaderColor = baseColor;
            shaderColor.a = 1f;
            SetColorIfPresent("_Color", shaderColor);
            SetFloatIfPresent("_Alpha", Mathf.Clamp01(baseColor.a));
        }
        else
        {
            SetColorIfPresent("_Color", baseColor);
        }

        if (applyOverlay)
        {
            Color overlay = theme.glassTint;
            overlay.a *= overlayStrength;
            SetColorIfPresent("_OverlayColor", overlay);
        }

        SetColorIfPresent("_ShadowColor", theme.depthShadow);

        Color glow = theme.keySoft;
        glow.a *= 0.35f;
        SetColorIfPresent("_GlowColor", glow);

        SetColorIfPresent("_ColorChangeNewCol", theme.keyColor);
        SetFloatIfPresent("_HsvShift", 0f);

        if (applyGradient)
        {
            Color gradLeft = Color.Lerp(theme.surface, theme.glassTint, 0.55f);
            Color gradRight = Color.Lerp(gradLeft, theme.keySoft, gradientKeyInfluence);
            SetColorIfPresent("_GradTopLeftCol", gradLeft);
            SetColorIfPresent("_GradTopRightCol", gradRight);
        }
    }

    private void SetColorIfPresent(string propertyName, Color value)
    {
        if (runtimeMaterial != null && runtimeMaterial.HasProperty(propertyName))
            runtimeMaterial.SetColor(propertyName, value);
    }

    private void SetFloatIfPresent(string propertyName, float value)
    {
        if (runtimeMaterial != null && runtimeMaterial.HasProperty(propertyName))
            runtimeMaterial.SetFloat(propertyName, value);
    }

    private void RestoreOriginalMaterial()
    {
        if (runtimeMaterial == null)
            return;

        if (targetWasGraphic && graphicTarget != null)
            graphicTarget.material = originalMaterial;
        else if (!targetWasGraphic && rendererTarget != null)
            rendererTarget.sharedMaterial = originalMaterial;

        if (Application.isPlaying)
            Destroy(runtimeMaterial);
        else
            DestroyImmediate(runtimeMaterial);

        runtimeMaterial = null;
        originalMaterial = null;
    }

    private static Color ResolveRole(BattleUIThemeProfile theme, BattleUIThemeColorRole role)
    {
        return role switch
        {
            BattleUIThemeColorRole.Background => theme.background,
            BattleUIThemeColorRole.Surface => theme.surface,
            BattleUIThemeColorRole.Glass => theme.glassTint,
            BattleUIThemeColorRole.TextPrimary => theme.textPrimary,
            BattleUIThemeColorRole.TextMuted => theme.textMuted,
            BattleUIThemeColorRole.Key => theme.keyColor,
            BattleUIThemeColorRole.KeySoft => theme.keySoft,
            BattleUIThemeColorRole.DepthShadow => theme.depthShadow,
            _ => theme.glassTint
        };
    }
}

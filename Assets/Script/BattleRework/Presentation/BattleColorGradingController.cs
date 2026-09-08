using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Runtime battle look controller.
///
/// The battle image keeps the authored sprite palette mostly intact and gets its punch from
/// exposure / contrast / saturation instead of a heavy teal-orange recolor.
/// A second, very subtle analog-TV layer is split into two parts:
/// - URP post effects: lens distortion, chromatic aberration and film grain.
/// - A transparent overlay: scanlines, tiny noise and a slow rolling sync band.
///
/// Reward / Map selection still fades this look almost completely out so the existing
/// show-stage dim / rectangular TV focus remains authoritative.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleColorGradingController : MonoBehaviour
{
    private const string AnalogOverlayShaderName = "UI/BattleAnalogTvOverlay";
    private const int AnalogOverlaySortingOrder = 430;

    private Camera targetCamera;
    private Volume volume;
    private VolumeProfile runtimeProfile;

    private ColorAdjustments colorAdjustments;
    private ShadowsMidtonesHighlights shadowsMidtonesHighlights;
    private SplitToning splitToning;
    private Bloom bloom;
    private Vignette vignette;
    private Tonemapping tonemapping;
    private ChromaticAberration chromaticAberration;
    private LensDistortion lensDistortion;
    private FilmGrain filmGrain;

    private Canvas analogCanvas;
    private Image analogOverlay;
    private Material analogOverlayMaterial;

    private BattleLightingProfileSO lightingProfile;
    private float currentWeight;
    private float targetWeight;
    private bool combatMode;
    private bool showMode;

    public float CurrentWeight => currentWeight;

    public void Configure(Camera camera, BattleLightingProfileSO profile)
    {
        if (camera != null)
            targetCamera = camera;

        EnsureVolume();
        EnsureAnalogOverlay();
        EnableCameraPostProcessing();

        if (profile != null && lightingProfile != profile)
            ApplyProfile(profile);
        else
            RefreshTargetWeight();
    }

    public void ApplyProfile(BattleLightingProfileSO profile)
    {
        if (profile == null)
            return;

        lightingProfile = profile;
        EnsureVolume();
        EnsureAnalogOverlay();

        colorAdjustments.postExposure.Override(profile.postExposure);
        colorAdjustments.contrast.Override(profile.contrast);
        colorAdjustments.colorFilter.Override(profile.colorFilter);
        colorAdjustments.hueShift.Override(profile.hueShift);
        colorAdjustments.saturation.Override(profile.saturation);

        shadowsMidtonesHighlights.shadows.Override(profile.shadows);
        shadowsMidtonesHighlights.midtones.Override(profile.midtones);
        shadowsMidtonesHighlights.highlights.Override(profile.highlights);
        shadowsMidtonesHighlights.shadowsStart.Override(profile.shadowsStart);
        shadowsMidtonesHighlights.shadowsEnd.Override(profile.shadowsEnd);
        shadowsMidtonesHighlights.highlightsStart.Override(profile.highlightsStart);
        shadowsMidtonesHighlights.highlightsEnd.Override(profile.highlightsEnd);

        splitToning.shadows.Override(profile.splitShadows);
        splitToning.highlights.Override(profile.splitHighlights);
        splitToning.balance.Override(profile.splitBalance);

        bloom.threshold.Override(profile.bloomThreshold);
        bloom.intensity.Override(profile.bloomIntensity);
        bloom.scatter.Override(profile.bloomScatter);
        bloom.tint.Override(profile.bloomTint);

        vignette.color.Override(profile.vignetteColor);
        vignette.center.Override(new Vector2(0.5f, 0.5f));
        vignette.intensity.Override(profile.vignetteIntensity);
        vignette.smoothness.Override(profile.vignetteSmoothness);
        vignette.rounded.Override(false);

        tonemapping.mode.Override(profile.tonemappingMode);

        chromaticAberration.intensity.Override(profile.chromaticAberrationIntensity);

        lensDistortion.intensity.Override(profile.lensDistortionIntensity);
        lensDistortion.scale.Override(profile.lensDistortionScale);

        filmGrain.intensity.Override(profile.filmGrainIntensity);
        filmGrain.response.Override(profile.filmGrainResponse);

        ApplyAnalogMaterialParameters();
        RefreshTargetWeight();
    }

    public void SetPresentationMode(bool combat, bool show)
    {
        combatMode = combat;
        showMode = show;
        RefreshTargetWeight();
    }

    public void SetImmediateWeight(float weight)
    {
        currentWeight = Mathf.Clamp01(weight);
        targetWeight = currentWeight;

        if (volume != null)
            volume.weight = currentWeight;

        UpdateAnalogOverlay();
    }

    private void Awake()
    {
        EnsureVolume();
        EnsureAnalogOverlay();
    }

    private void Update()
    {
        if (volume == null || lightingProfile == null)
            return;

        float sharpness = Mathf.Max(0.1f, lightingProfile.volumeFadeSharpness);
        float t = 1f - Mathf.Exp(-sharpness * Time.unscaledDeltaTime);
        currentWeight = Mathf.Lerp(currentWeight, targetWeight, t);

        if (Mathf.Abs(currentWeight - targetWeight) < 0.001f)
            currentWeight = targetWeight;

        volume.weight = currentWeight;
        UpdateAnalogOverlay();
        EnableCameraPostProcessing();
    }

    private void OnDestroy()
    {
        if (analogOverlayMaterial != null)
            Destroy(analogOverlayMaterial);

        if (runtimeProfile != null)
            Destroy(runtimeProfile);
    }

    private void RefreshTargetWeight()
    {
        if (lightingProfile == null)
        {
            targetWeight = 0f;
            return;
        }

        targetWeight = showMode
            ? Mathf.Clamp01(lightingProfile.showVolumeWeight)
            : combatMode
                ? Mathf.Clamp01(lightingProfile.combatVolumeWeight)
                : Mathf.Clamp01(lightingProfile.idleVolumeWeight);
    }

    private void EnsureVolume()
    {
        if (volume == null)
        {
            GameObject volumeObject = new("BattleColorGradingVolume");
            volumeObject.transform.SetParent(transform, false);

            volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 500f;
            volume.weight = currentWeight;
        }

        if (runtimeProfile != null)
            return;

        runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeProfile.name = "BattleColorGrading_Runtime";
        runtimeProfile.hideFlags = HideFlags.HideAndDontSave;
        volume.sharedProfile = runtimeProfile;

        colorAdjustments = runtimeProfile.Add<ColorAdjustments>(true);
        shadowsMidtonesHighlights = runtimeProfile.Add<ShadowsMidtonesHighlights>(true);
        splitToning = runtimeProfile.Add<SplitToning>(true);
        bloom = runtimeProfile.Add<Bloom>(true);
        vignette = runtimeProfile.Add<Vignette>(true);
        tonemapping = runtimeProfile.Add<Tonemapping>(true);
        chromaticAberration = runtimeProfile.Add<ChromaticAberration>(true);
        lensDistortion = runtimeProfile.Add<LensDistortion>(true);
        filmGrain = runtimeProfile.Add<FilmGrain>(true);
    }

    private void EnsureAnalogOverlay()
    {
        if (analogCanvas == null)
        {
            GameObject canvasObject = new("BattleAnalogTvOverlayCanvas");
            canvasObject.transform.SetParent(transform, false);

            analogCanvas = canvasObject.AddComponent<Canvas>();
            analogCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            analogCanvas.overrideSorting = true;
            analogCanvas.sortingOrder = AnalogOverlaySortingOrder;

            GameObject overlayObject = new("BattleAnalogTvOverlay", typeof(RectTransform));
            overlayObject.transform.SetParent(canvasObject.transform, false);

            RectTransform rect = overlayObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            analogOverlay = overlayObject.AddComponent<Image>();
            analogOverlay.raycastTarget = false;
            analogOverlay.color = Color.white;
        }

        if (analogOverlayMaterial == null)
        {
            Shader shader = Shader.Find(AnalogOverlayShaderName);
            if (shader == null)
                shader = Resources.Load<Shader>("BattleAnalogTvOverlay");

            if (shader != null)
            {
                analogOverlayMaterial = new Material(shader)
                {
                    name = "BattleAnalogTvOverlay_Runtime",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        if (analogOverlay != null)
            analogOverlay.material = analogOverlayMaterial;
    }

    private void ApplyAnalogMaterialParameters()
    {
        if (analogOverlayMaterial == null || lightingProfile == null)
            return;

        analogOverlayMaterial.SetFloat("_ScanlineStrength", lightingProfile.scanlineStrength);
        analogOverlayMaterial.SetFloat("_ScanlineSpacing", lightingProfile.scanlineSpacingPixels);
        analogOverlayMaterial.SetFloat("_NoiseStrength", lightingProfile.analogNoiseStrength);
        analogOverlayMaterial.SetFloat("_RollingBandStrength", lightingProfile.rollingBandStrength);
        analogOverlayMaterial.SetColor("_OverlayTint", lightingProfile.analogOverlayTint);
    }

    private void UpdateAnalogOverlay()
    {
        if (analogOverlay == null)
            return;

        if (lightingProfile == null || analogOverlayMaterial == null)
        {
            analogOverlay.enabled = false;
            return;
        }

        ApplyAnalogMaterialParameters();

        float strength = Mathf.Clamp01(currentWeight * lightingProfile.analogOverlayStrength);
        analogOverlayMaterial.SetFloat("_Strength", strength);
        analogOverlay.enabled = strength > 0.001f;
    }

    private void EnableCameraPostProcessing()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera == null)
            return;

        UniversalAdditionalCameraData data =
            targetCamera.GetComponent<UniversalAdditionalCameraData>();

        if (data == null)
            data = targetCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();

        data.renderPostProcessing = true;
    }
}

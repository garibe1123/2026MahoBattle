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
    private const int AnalogOverlaySortingOrder = 470;

    private Camera targetCamera;
    private UniversalAdditionalCameraData cameraData;
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

    [Header("CAMERA OPTICS REACTION")]
    [SerializeField, Range(0f, 0.40f)] private float reactionExposureKick = 0.16f;
    [SerializeField, Range(0f, 1f)] private float reactionBloomKick = 0.22f;
    [SerializeField, Range(0f, 0.20f)] private float reactionChromaticKick = 0.05f;
    [SerializeField, Range(0f, 0.10f)] private float reactionLensDistortionKick = 0.018f;
    [SerializeField, Min(0.05f)] private float defaultReactionDuration = 0.18f;

    private BattleLightingProfileSO lightingProfile;
    private BattleLightingProfileSO previewProfile;
    private bool previewEnabled;

    public BattleLightingProfileSO SourceLightingProfile => lightingProfile;
    public BattleLightingProfileSO PreviewProfile => previewProfile;
    public Camera TargetCamera => targetCamera;
    public int CameraVolumeMask => cameraData != null ? cameraData.volumeLayerMask.value : 0;
    public int RuntimeVolumeLayer => volume != null ? volume.gameObject.layer : -1;
    public bool PreviewEnabled => previewEnabled;
    private float currentWeight;
    private float targetWeight;
    private bool combatMode;
    private bool showMode;
    private float selectionBlend;
    private float combatTabBlend;
    private float lastKillEmphasis;
    private float lowHpEmphasis;

    private float cameraReactionPeak;
    private float cameraReactionStartedAt = -1f;
    private float cameraReactionEndsAt = -1f;
    private float currentCameraReaction;

    public float CurrentWeight => currentWeight;
    internal float CurrentLensDistortion => lensDistortion != null ? lensDistortion.intensity.value : 0f;
    internal float CurrentVolumeWeight => volume != null ? volume.weight : 0f;
    private float EffectiveVolumeWeight => Mathf.Max(currentWeight, Mathf.Clamp01(selectionBlend));

    public void Configure(Camera camera, BattleLightingProfileSO profile)
    {
        if (camera != null && targetCamera != camera)
        {
            targetCamera = camera;
            cameraData = null;
        }

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

        if (previewEnabled && previewProfile != null)
        {
            splitToning.shadows.Override(previewProfile.splitShadows);
            splitToning.highlights.Override(previewProfile.splitHighlights);
            splitToning.balance.Override(previewProfile.splitBalance);
            bloom.threshold.Override(previewProfile.bloomThreshold);
            bloom.scatter.Override(previewProfile.bloomScatter);
            bloom.tint.Override(previewProfile.bloomTint);
        }

        ApplyDynamicOverrides();
        ApplyAnalogMaterialParameters();
        RefreshTargetWeight();
    }

    public void SetPreviewEnabled(bool enabled)
    {
        if (!Application.isPlaying || lightingProfile == null)
            return;

        if (enabled && previewProfile == null)
            ResetPreview();
        previewEnabled = enabled;
        RefreshPreview();
    }

    public void ResetPreview()
    {
        if (!Application.isPlaying || lightingProfile == null)
            return;

        if (previewProfile != null)
            Destroy(previewProfile);
        previewProfile = Instantiate(lightingProfile);
        previewProfile.name = "BattleLightingProfile_ColorPreview";
        previewProfile.hideFlags = HideFlags.HideAndDontSave;
        previewProfile.splitShadows = new Color(0.40f, 0.46f, 0.60f, 1f);
        previewProfile.splitHighlights = new Color(0.60f, 0.55f, 0.43f, 1f);
        previewProfile.splitBalance = 0f;
        previewProfile.bloomIntensity = 0.25f;
        previewProfile.bloomThreshold = 0.95f;
        previewProfile.bloomScatter = 0.52f;
        RefreshPreview();
    }

    public void RefreshPreview()
    {
        if (Application.isPlaying && lightingProfile != null)
            ApplyProfile(lightingProfile);
    }

    public void SetLastKillEmphasis(float amount)
    {
        lastKillEmphasis = Mathf.Clamp01(amount);
        ApplyDynamicOverrides();
        ApplyAnalogMaterialParameters();
    }

    public void SetLowHpEmphasis(float amount)
    {
        lowHpEmphasis = Mathf.Clamp01(amount);
        ApplyDynamicOverrides();
        ApplyAnalogMaterialParameters();
    }

    /// <summary>
    /// 짧은 카메라 렌즈 반응을 누적합니다.
    /// Profile의 기본 룩은 유지하고 Exposure / Bloom / CA / Lens Distortion만
    /// transient envelope로 잠깐 가산한 뒤 자동 복귀합니다.
    /// </summary>
    public void PushCameraReaction(float strength, float duration = -1f)
    {
        float normalizedStrength = Mathf.Clamp01(strength);
        if (normalizedStrength <= 0f)
            return;

        float safeDuration = duration > 0f
            ? Mathf.Max(0.05f, duration)
            : Mathf.Max(0.05f, defaultReactionDuration);

        float now = Time.unscaledTime;
        cameraReactionPeak = Mathf.Max(currentCameraReaction, normalizedStrength);
        cameraReactionStartedAt = now;
        cameraReactionEndsAt = now + safeDuration;
        currentCameraReaction = cameraReactionPeak;

        ApplyDynamicOverrides();
    }

    private void ApplyDynamicOverrides()
    {
        if (lightingProfile == null ||
            colorAdjustments == null ||
            bloom == null ||
            vignette == null ||
            chromaticAberration == null ||
            lensDistortion == null)
        {
            return;
        }

        float combinedSaturationLoss =
            20f * lastKillEmphasis +
            12f * lowHpEmphasis;

        colorAdjustments.postExposure.Override(
            lightingProfile.postExposure +
            reactionExposureKick * currentCameraReaction);

        colorAdjustments.saturation.Override(
            Mathf.Clamp(
                lightingProfile.saturation - combinedSaturationLoss,
                -100f,
                100f));

        bloom.intensity.Override(
            Mathf.Max(
                0f,
                (previewEnabled && previewProfile != null
                    ? previewProfile.bloomIntensity : lightingProfile.bloomIntensity) +
                reactionBloomKick * currentCameraReaction));

        vignette.intensity.Override(
            Mathf.Clamp01(
                lightingProfile.vignetteIntensity +
                0.08f * lastKillEmphasis +
                0.10f * lowHpEmphasis));

        chromaticAberration.intensity.Override(
            Mathf.Lerp(Mathf.Clamp01(
                lightingProfile.chromaticAberrationIntensity +
                0.08f * lowHpEmphasis +
                reactionChromaticKick * currentCameraReaction),
                lightingProfile.crtSelectionChromaticAberration,
                Mathf.Clamp01(selectionBlend)));

        lensDistortion.intensity.Override(
            Mathf.Clamp(
                lightingProfile.lensDistortionIntensity -
                lightingProfile.crtCurvature * lightingProfile.crtSelectionCurvatureMultiplier * combatTabBlend -
                reactionLensDistortionKick * currentCameraReaction,
                -1f,
                1f));

        splitToning.shadows.Override(Color.Lerp(
            previewEnabled && previewProfile != null ? previewProfile.splitShadows : lightingProfile.splitShadows,
            lightingProfile.crtShadowTint,
            lightingProfile.crtColorStrength * Mathf.Clamp01(selectionBlend)));
    }

    internal void SetSelectionBlend(float value, bool combatTabHeld)
    {
        selectionBlend = value;
        combatTabBlend = isActiveAndEnabled && combatTabHeld ? Mathf.Clamp01(value) : 0f;
        if (volume != null)
            volume.weight = EffectiveVolumeWeight;
        ApplyDynamicOverrides();
        UpdateAnalogOverlay();
    }

    public void ClearCameraReaction()
    {
        cameraReactionPeak = 0f;
        cameraReactionStartedAt = -1f;
        cameraReactionEndsAt = -1f;
        currentCameraReaction = 0f;
        ApplyDynamicOverrides();
    }

    public void SetPresentationMode(bool combat, bool show)
    {
        combatMode = combat;
        showMode = show;
        if (!combatMode)
            combatTabBlend = 0f;

        // Combat에서 발생한 과노출/CA/Lens kick이 Reward/Map/대기 단계로
        // 한두 프레임이라도 넘어가지 않게 상태 전환 시 명시적으로 원복합니다.
        if (!combatMode || showMode)
            ClearCameraReaction();

        RefreshTargetWeight();
    }

    public void SetImmediateWeight(float weight)
    {
        currentWeight = Mathf.Clamp01(weight);
        targetWeight = currentWeight;

        if (volume != null)
            volume.weight = EffectiveVolumeWeight;

        UpdateAnalogOverlay();
    }

    private void Awake()
    {
        EnsureVolume();
        EnsureAnalogOverlay();
    }

    private void Start()
    {
        EnableCameraPostProcessing();
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

        volume.weight = EffectiveVolumeWeight;
        UpdateCameraReactionEnvelope();
        UpdateAnalogOverlay();
        if (cameraData != null)
            cameraData.renderPostProcessing = true;
    }

    private void UpdateCameraReactionEnvelope()
    {
        float nextReaction = 0f;

        if (cameraReactionEndsAt > cameraReactionStartedAt &&
            Time.unscaledTime < cameraReactionEndsAt)
        {
            float duration = Mathf.Max(
                0.05f,
                cameraReactionEndsAt - cameraReactionStartedAt);
            float elapsed = Mathf.Clamp01(
                (Time.unscaledTime - cameraReactionStartedAt) / duration);
            float remaining = 1f - elapsed;
            float smoothRemaining =
                remaining * remaining * (3f - 2f * remaining);
            nextReaction = cameraReactionPeak * smoothRemaining;
        }
        else
        {
            cameraReactionPeak = 0f;
            cameraReactionStartedAt = -1f;
            cameraReactionEndsAt = -1f;
        }

        if (Mathf.Abs(currentCameraReaction - nextReaction) <= 0.0001f)
            return;

        currentCameraReaction = nextReaction;
        ApplyDynamicOverrides();
    }

    private void OnDisable()
    {
        combatTabBlend = 0f;
        ApplyDynamicOverrides();
    }

    private void OnDestroy()
    {
        if (previewProfile != null)
            Destroy(previewProfile);

        if (analogOverlayMaterial != null)
            Destroy(analogOverlayMaterial);

        if (runtimeProfile != null)
        {
            if (volume != null && volume.sharedProfile == runtimeProfile)
                volume.sharedProfile = null;
            foreach (VolumeComponent component in runtimeProfile.components)
                if (component != null)
                    Destroy(component);
            runtimeProfile.components.Clear();
            Destroy(runtimeProfile);
        }
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
            analogOverlay.enabled = false;
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
        analogOverlayMaterial.SetFloat(
            "_NoiseStrength",
            Mathf.Clamp01(
                lightingProfile.analogNoiseStrength +
                0.22f * lowHpEmphasis));
        analogOverlayMaterial.SetFloat(
            "_RollingBandStrength",
            Mathf.Clamp01(
                lightingProfile.rollingBandStrength +
                0.16f * lowHpEmphasis));
        analogOverlayMaterial.SetColor("_OverlayTint", lightingProfile.analogOverlayTint);
        float selection = Mathf.Clamp01(selectionBlend);
        analogOverlayMaterial.SetFloat("_Curvature", lightingProfile.crtCurvature * lightingProfile.crtSelectionCurvatureMultiplier * selection);
        analogOverlayMaterial.SetFloat("_EdgeWidth", lightingProfile.crtEdgeWidth);
        analogOverlayMaterial.SetFloat("_EdgeStrength", lightingProfile.crtEdgeStrength * selection);
        analogOverlayMaterial.SetFloat("_CenterGlow", lightingProfile.crtCenterGlow * selection);
        analogOverlayMaterial.SetColor("_GlassTint", lightingProfile.crtShadowTint);
        analogOverlayMaterial.SetFloat("_GlassTintStrength", lightingProfile.crtColorStrength * selection);
        analogOverlayMaterial.SetFloat("_SelectionNoiseStrength", lightingProfile.crtSelectionNoiseStrength * selection);
        analogOverlayMaterial.SetFloat("_SelectionNoiseRate", lightingProfile.crtSelectionNoiseRate);
        analogOverlayMaterial.SetFloat("_SelectionNoisePixelSize", lightingProfile.crtSelectionNoisePixelSize);
        BattleUiMaterialController.UpdateUnscaledTime(analogOverlayMaterial);
    }

    private void UpdateAnalogOverlay()
    {
        float selection = Mathf.Clamp01(selectionBlend);
        if (filmGrain != null && lightingProfile != null)
            filmGrain.intensity.Override(lightingProfile.filmGrainIntensity * (1f - selection));
        if (analogOverlay == null)
            return;

        if (lightingProfile == null || analogOverlayMaterial == null)
        {
            analogOverlay.enabled = false;
            return;
        }

        ApplyAnalogMaterialParameters();

        float weight = Mathf.Max(currentWeight, lightingProfile.crtSelectionOverlayWeight * selection);
        float strength = Mathf.Clamp01(weight * lightingProfile.analogOverlayStrength);
        analogOverlayMaterial.SetFloat("_Strength", strength);
        analogOverlay.enabled = strength > 0.001f;
    }

    private void EnableCameraPostProcessing()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera == null)
            return;

        if (cameraData == null)
            cameraData = targetCamera.GetComponent<UniversalAdditionalCameraData>();

        if (cameraData == null)
            cameraData = targetCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();

        cameraData.renderPostProcessing = true;
        if (volume != null)
            for (int layer = 0; layer < 32; layer++)
                if ((cameraData.volumeLayerMask.value & (1 << layer)) != 0)
                {
                    volume.gameObject.layer = layer;
                    break;
                }
    }
}

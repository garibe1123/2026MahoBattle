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
    private const float MaxUiOpticsOffsetPixels = 8f;
    private static BattleColorGradingController uiOpticsSource;

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

    [Header("UI OPTICS")]
    [SerializeField, Range(0f, 1f)] private float uiChromaticMultiplier = 0.2f;
    [SerializeField, Range(0f, 1f)] private float uiLensMultiplier = 0.2f;

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
    private float lastKillEmphasis;
    private float lowHpEmphasis;

    private float cameraReactionPeak;
    private float cameraReactionStartedAt = -1f;
    private float cameraReactionEndsAt = -1f;
    private float currentCameraReaction;
    private float playerHitPeak;
    private float playerHitStartedAt = -1f;
    private float playerHitDuration;
    private float currentPlayerHit;
    private float playerHpFraction = 1f;
    private float playerLowHpStartedAt = -1f;
    private float currentPlayerLowHpPulse;

    internal Vector2 PlayerDamageOptics
    {
        get
        {
            if (lightingProfile == null)
                return Vector2.zero;

            Vector2 budget = BattleShowBroadcastNoiseController.GetPlayerDamageOpticsBudget(targetCamera, lightingProfile);
            budget *= Mathf.Clamp(lightingProfile.playerDamageTabStrengthLimit, 0f, 0.35f);
            budget.x = Mathf.Min(budget.x, 1f - Mathf.Clamp01(lightingProfile.chromaticAberrationIntensity +
                0.08f * lowHpEmphasis + reactionChromaticKick * currentCameraReaction));
            budget.y = Mathf.Min(budget.y, 1f + Mathf.Clamp(lightingProfile.lensDistortionIntensity -
                reactionLensDistortionKick * currentCameraReaction, -1f, 1f));
            return new Vector2(
                Mathf.Min(budget.x, Mathf.Max(0f, lightingProfile.playerHitChromaticAddition) * currentPlayerHit +
                    Mathf.Max(0f, lightingProfile.playerLowHpChromaticAddition) * currentPlayerLowHpPulse),
                Mathf.Min(budget.y, Mathf.Max(0f, -lightingProfile.playerHitLensAddition) * currentPlayerHit));
        }
    }

    public float CurrentWeight => currentWeight;
    internal Volume RuntimeVolume => volume;
    internal VolumeProfile RuntimeProfile => runtimeProfile;
    internal Image RuntimeAnalogOverlay => analogOverlay;
    internal Material RuntimeAnalogMaterial => analogOverlayMaterial;

    internal static bool TryGetUiOptics(out Vector4 settings, out Vector2 pixelLimits)
    {
        settings = Vector4.zero;
        pixelLimits = Vector2.zero;
        BattleColorGradingController source = uiOpticsSource;
        if (source == null || !source.isActiveAndEnabled || source.lightingProfile == null ||
            source.volume == null || !source.volume.isActiveAndEnabled || source.cameraData == null ||
            !source.cameraData.renderPostProcessing ||
            (source.CameraVolumeMask & (1 << source.RuntimeVolumeLayer)) == 0)
            return false;

        float chromatic = Mathf.Clamp01(source.uiChromaticMultiplier);
        float lens = Mathf.Clamp01(source.uiLensMultiplier);
        float weight = Mathf.Clamp01(source.volume.weight);
        if (source.chromaticAberration.active && source.chromaticAberration.IsActive())
            settings.x = source.chromaticAberration.intensity.value * weight * chromatic;
        if (source.lensDistortion.active && source.lensDistortion.IsActive())
        {
            settings.y = source.lensDistortion.intensity.value * weight * lens;
            settings.z = (source.lensDistortion.scale.value - 1f) * weight * lens;
        }
        if (BattleShowBroadcastNoiseController.TryGetCameraDistortion(source.targetCamera, out Vector4 tab))
            settings.w = tab.x * lens;
        pixelLimits = new Vector2(chromatic, lens) * MaxUiOpticsOffsetPixels;
        return settings.sqrMagnitude > 0.00000001f;
    }

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
    public void PushCameraReaction(float strength, float duration, float playerDamageFraction)
    {
        if (lightingProfile == null || !combatMode || showMode || playerDamageFraction <= 0f || strength <= 0f)
            return;

        bool heavyHit = playerDamageFraction >= Mathf.Clamp01(lightingProfile.playerHeavyHitHpFraction);
        float peak = heavyHit ? Mathf.Max(1f, lightingProfile.playerHeavyHitOpticsMultiplier) : 1f;
        playerHitPeak = Mathf.Max(currentPlayerHit, peak * Mathf.Clamp01(strength));
        playerHitStartedAt = Time.unscaledTime;
        playerHitDuration = Mathf.Max(0.01f, duration > 0f ? duration : (heavyHit
            ? lightingProfile.playerHeavyHitOpticsDuration : lightingProfile.playerHitOpticsDuration));
        currentPlayerHit = playerHitPeak;
        ApplyDynamicOverrides();
    }

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

        Vector2 playerDamage = PlayerDamageOptics;
        chromaticAberration.intensity.Override(
            Mathf.Clamp01(
                lightingProfile.chromaticAberrationIntensity +
                0.08f * lowHpEmphasis +
                reactionChromaticKick * currentCameraReaction + playerDamage.x));

        lensDistortion.intensity.Override(
            Mathf.Clamp(
                lightingProfile.lensDistortionIntensity -
                reactionLensDistortionKick * currentCameraReaction - playerDamage.y,
                -1f,
                1f));

        lensDistortion.scale.Override(lightingProfile.lensDistortionScale);

        splitToning.shadows.Override(
            previewEnabled && previewProfile != null ? previewProfile.splitShadows : lightingProfile.splitShadows);
    }

    internal void RestoreRuntimePresentation()
    {
        if (volume != null)
            volume.weight = currentWeight;
        ApplyDynamicOverrides();
        UpdateAnalogOverlay();
    }

    public void ClearCameraReaction()
    {
        cameraReactionPeak = 0f;
        cameraReactionStartedAt = -1f;
        cameraReactionEndsAt = -1f;
        currentCameraReaction = 0f;
        playerHitPeak = 0f;
        playerHitStartedAt = -1f;
        playerHitDuration = 0f;
        currentPlayerHit = 0f;
        playerHpFraction = 1f;
        playerLowHpStartedAt = -1f;
        currentPlayerLowHpPulse = 0f;
        ApplyDynamicOverrides();
    }

    public void SetPlayerHealth(float hp, float maxHp)
    {
        playerHpFraction = Mathf.Clamp01(hp / Mathf.Max(1f, maxHp));
        UpdatePlayerDamageEnvelope();
    }

    public void SetPresentationMode(bool combat, bool show)
    {
        combatMode = combat;
        showMode = show;

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
            volume.weight = currentWeight;

        UpdateAnalogOverlay();
    }

    private void Awake()
    {
        uiOpticsSource = this;
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

        volume.weight = currentWeight;
        UpdatePlayerDamageEnvelope();
        UpdateCameraReactionEnvelope();
        UpdateAnalogOverlay();
        if (cameraData != null)
            cameraData.renderPostProcessing = true;
    }

    private void UpdatePlayerDamageEnvelope()
    {
        if (lightingProfile == null)
            return;

        float hit = playerHitStartedAt >= 0f && playerHitDuration > 0f
            ? Mathf.SmoothStep(playerHitPeak, 0f, (Time.unscaledTime - playerHitStartedAt) / playerHitDuration)
            : 0f;
        float pulse = 0f;
        bool lowHp = combatMode && !showMode && playerHpFraction > 0f &&
            playerHpFraction <= Mathf.Clamp01(lightingProfile.playerLowHpFraction);
        if (lowHp)
        {
            if (playerLowHpStartedAt < 0f)
                playerLowHpStartedAt = Time.unscaledTime;
            float phase = (Time.unscaledTime - playerLowHpStartedAt) /
                Mathf.Max(0.01f, lightingProfile.playerLowHpOpticsPeriod);
            pulse = (1f - Mathf.Cos(phase * Mathf.PI * 2f)) * 0.5f;
        }
        else
        {
            playerLowHpStartedAt = -1f;
        }

        bool changed = currentPlayerHit != hit || currentPlayerLowHpPulse != pulse;
        currentPlayerHit = hit;
        currentPlayerLowHpPulse = pulse;
        if (changed)
            ApplyDynamicOverrides();
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
        ClearCameraReaction();
        RestoreRuntimePresentation();
    }

    private void OnDestroy()
    {
        if (uiOpticsSource == this)
            uiOpticsSource = null;
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
        analogOverlayMaterial.SetFloat("_Curvature", 0f);
        analogOverlayMaterial.SetFloat("_EdgeStrength", 0f);
        analogOverlayMaterial.SetFloat("_CenterGlow", 0f);
        analogOverlayMaterial.SetFloat("_GlassTintStrength", 0f);
        analogOverlayMaterial.SetFloat("_SelectionNoiseStrength", 0f);
        BattleUiMaterialController.UpdateUnscaledTime(analogOverlayMaterial);
    }

    private void UpdateAnalogOverlay()
    {
        if (filmGrain != null && lightingProfile != null)
        {
            filmGrain.intensity.Override(lightingProfile.filmGrainIntensity);
            filmGrain.response.Override(lightingProfile.filmGrainResponse);
        }
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

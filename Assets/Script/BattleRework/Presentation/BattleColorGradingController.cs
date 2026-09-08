using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Runtime global Volume for the battle camera.
/// It intentionally grades Combat strongly while keeping Reward / Map-selection almost untouched,
/// because the show presentation already owns its own dim/focus language.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleColorGradingController : MonoBehaviour
{
    private Camera targetCamera;
    private Volume volume;
    private VolumeProfile runtimeProfile;

    private ColorAdjustments colorAdjustments;
    private ShadowsMidtonesHighlights shadowsMidtonesHighlights;
    private SplitToning splitToning;
    private Bloom bloom;
    private Vignette vignette;
    private Tonemapping tonemapping;

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
    }

    private void Awake()
    {
        EnsureVolume();
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
        EnableCameraPostProcessing();
    }

    private void OnDestroy()
    {
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

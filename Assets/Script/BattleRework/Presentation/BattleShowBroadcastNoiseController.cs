using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Reward / Map-selection broadcast treatment.
/// Adds a dedicated show-only Volume and analog surface overlay so selection screens read
/// like a live TV feed without pushing Combat color grading into the show presentation.
/// </summary>
[DefaultExecutionOrder(25500)]
[DisallowMultipleComponent]
public sealed class BattleShowBroadcastNoiseController : MonoBehaviour
{
    private const string AnalogOverlayShaderName = "UI/BattleAnalogTvOverlay";
    private const int OverlaySortingOrder = 460;

    private static BattleShowBroadcastNoiseController instance;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;

    [Header("Show Broadcast Volume")]
    [SerializeField, Range(0f, 1f)] private float filmGrainIntensity = 0.16f;
    [SerializeField, Range(0f, 1f)] private float filmGrainResponse = 0.72f;
    [SerializeField, Range(0f, 1f)] private float chromaticAberrationIntensity = 0.032f;
    [SerializeField, Range(-1f, 1f)] private float lensDistortionIntensity = -0.020f;
    [SerializeField, Range(0.01f, 5f)] private float lensDistortionScale = 1.008f;

    [Header("Show Lens Vignette")]
    [Tooltip("CRT/lens-like falloff that darkens only the outer corners without crushing the TV content in the center.")]
    [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.13f;
    [SerializeField, Range(0.01f, 1f)] private float vignetteSmoothness = 0.48f;
    [SerializeField] private Color vignetteColor = Color.black;

    [Header("Analog Surface")]
    [SerializeField, Range(0f, 1f)] private float overlayStrength = 0.72f;
    [SerializeField, Range(0f, 0.25f)] private float scanlineStrength = 0.050f;
    [SerializeField, Range(1f, 12f)] private float scanlineSpacingPixels = 3f;
    [SerializeField, Range(0f, 0.15f)] private float noiseStrength = 0.034f;
    [SerializeField, Range(0f, 0.15f)] private float rollingBandStrength = 0.026f;
    [SerializeField] private Color overlayTint = new(0.012f, 0.016f, 0.020f, 1f);

    [Header("Blend")]
    [SerializeField, Min(0.1f)] private float fadeSharpness = 7.5f;
    [SerializeField, Range(0f, 0.15f)] private float signalFlicker = 0.035f;
    [SerializeField, Min(0.1f)] private float signalFlickerSpeed = 6.2f;

    private Volume showVolume;
    private VolumeProfile runtimeProfile;
    private FilmGrain filmGrain;
    private ChromaticAberration chromaticAberration;
    private LensDistortion lensDistortion;
    private Vignette vignette;

    private Canvas overlayCanvas;
    private Image overlayImage;
    private Material overlayMaterial;

    private float currentBlend;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ResolveReferences();
        EnsureVolume();
        EnsureOverlay();
        ApplyImmediate(0f);
    }

    private void Update()
    {
        ResolveReferences();
        EnsureVolume();
        EnsureOverlay();
        EnsureCameraPostProcessing();

        float target = IsShowSelection() ? 1f : 0f;
        float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, fadeSharpness) * Time.unscaledDeltaTime);
        currentBlend = Mathf.Lerp(currentBlend, target, t);

        if (Mathf.Abs(currentBlend - target) < 0.001f)
            currentBlend = target;

        ApplyBlend();
    }

    private void OnDisable()
    {
        ApplyImmediate(0f);
    }

    private void OnDestroy()
    {
        if (overlayMaterial != null)
            Destroy(overlayMaterial);
        if (runtimeProfile != null)
            Destroy(runtimeProfile);

        if (instance == this)
            instance = null;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
    }

    private bool IsShowSelection()
    {
        if (runManager == null || !runManager.RunActive)
            return false;

        return runManager.State == BattleRunState.Reward ||
               runManager.State == BattleRunState.SelectingNode;
    }

    private void EnsureVolume()
    {
        if (showVolume == null)
        {
            GameObject volumeObject = new("BattleShowBroadcastVolume");
            volumeObject.transform.SetParent(transform, false);

            showVolume = volumeObject.AddComponent<Volume>();
            showVolume.isGlobal = true;
            showVolume.priority = 650f;
            showVolume.weight = currentBlend;
        }

        if (runtimeProfile != null)
            return;

        runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeProfile.name = "BattleShowBroadcast_Runtime";
        runtimeProfile.hideFlags = HideFlags.HideAndDontSave;
        showVolume.sharedProfile = runtimeProfile;

        filmGrain = runtimeProfile.Add<FilmGrain>(true);
        filmGrain.intensity.Override(filmGrainIntensity);
        filmGrain.response.Override(filmGrainResponse);

        chromaticAberration = runtimeProfile.Add<ChromaticAberration>(true);
        chromaticAberration.intensity.Override(chromaticAberrationIntensity);

        lensDistortion = runtimeProfile.Add<LensDistortion>(true);
        lensDistortion.intensity.Override(lensDistortionIntensity);
        lensDistortion.scale.Override(lensDistortionScale);

        vignette = runtimeProfile.Add<Vignette>(true);
        vignette.color.Override(vignetteColor);
        vignette.center.Override(new Vector2(0.5f, 0.5f));
        vignette.intensity.Override(vignetteIntensity);
        vignette.smoothness.Override(vignetteSmoothness);
        vignette.rounded.Override(false);
    }

    private void EnsureOverlay()
    {
        if (overlayCanvas == null)
        {
            GameObject canvasObject = new("BattleShowBroadcastNoiseCanvas");
            canvasObject.transform.SetParent(transform, false);

            overlayCanvas = canvasObject.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder = OverlaySortingOrder;

            GameObject imageObject = new("BattleShowBroadcastNoise", typeof(RectTransform));
            imageObject.transform.SetParent(canvasObject.transform, false);

            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            overlayImage = imageObject.AddComponent<Image>();
            overlayImage.raycastTarget = false;
            overlayImage.color = Color.white;
        }

        if (overlayMaterial == null)
        {
            Shader shader = Shader.Find(AnalogOverlayShaderName);
            if (shader == null)
                shader = Resources.Load<Shader>("BattleAnalogTvOverlay");

            if (shader != null)
            {
                overlayMaterial = new Material(shader)
                {
                    name = "BattleShowBroadcastNoise_Runtime",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        if (overlayImage != null)
            overlayImage.material = overlayMaterial;
    }

    private void ApplyBlend()
    {
        if (showVolume != null)
            showVolume.weight = Mathf.Clamp01(currentBlend);

        if (overlayImage == null || overlayMaterial == null)
            return;

        float flicker = 1f + Mathf.Sin(Time.unscaledTime * signalFlickerSpeed) * signalFlicker;
        float strength = Mathf.Clamp01(currentBlend * overlayStrength * flicker);

        overlayMaterial.SetFloat("_Strength", strength);
        overlayMaterial.SetFloat("_ScanlineStrength", scanlineStrength);
        overlayMaterial.SetFloat("_ScanlineSpacing", scanlineSpacingPixels);
        overlayMaterial.SetFloat("_NoiseStrength", noiseStrength);
        overlayMaterial.SetFloat("_RollingBandStrength", rollingBandStrength);
        overlayMaterial.SetColor("_OverlayTint", overlayTint);

        overlayImage.enabled = strength > 0.001f;
    }

    private void ApplyImmediate(float value)
    {
        currentBlend = Mathf.Clamp01(value);

        if (showVolume != null)
            showVolume.weight = currentBlend;

        if (overlayMaterial != null)
            overlayMaterial.SetFloat("_Strength", currentBlend * overlayStrength);

        if (overlayImage != null)
            overlayImage.enabled = currentBlend > 0.001f;
    }

    private static void EnsureCameraPostProcessing()
    {
        Camera camera = Camera.main;
        if (camera == null)
            return;

        UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
        if (data == null)
            data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();

        data.renderPostProcessing = true;
    }
}

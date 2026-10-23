using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[DefaultExecutionOrder(25500)]
[DisallowMultipleComponent]
public sealed class BattleShowBroadcastNoiseController : MonoBehaviour
{
    private const int OverlaySortingOrder = 460;

    private static BattleShowBroadcastNoiseController instance;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;
    [SerializeField] private BattleShowPresentationManager presentation;
    [SerializeField] private Camera targetCamera;
    private UniversalAdditionalCameraData cameraData;
    private BattleColorGradingController colorGrading;

    [Header("Shared Selection Lens")]
    [SerializeField, Range(0f, 1f)] private float filmGrainIntensity = 0.055f;
    [SerializeField, Range(0f, 1f)] private float filmGrainResponse = 0.78f;
    [SerializeField, Range(0f, 1f)] private float chromaticAberrationIntensity = 0.12f;
    [SerializeField, Range(-1f, 1f)] private float lensDistortionIntensity = -0.055f;
    [SerializeField, Range(0.01f, 5f)] private float lensDistortionScale = 1.015f;
    [Tooltip("Additional combat TAB curvature when no lighting profile is available.")]
    [SerializeField, Range(0f, 1f)] private float tabLensDistortion = 0.14f;

    [Header("Show Lens Vignette")]
    [Tooltip("CRT/lens-like falloff that darkens only the outer corners without crushing the TV content in the center.")]
    [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.13f;
    [SerializeField, Range(0.01f, 1f)] private float vignetteSmoothness = 0.48f;
    [SerializeField] private Color vignetteColor = Color.black;

    [Header("Blend")]
    [SerializeField] private Color tabBackgroundTint = new(0.45f, 0.50f, 0.60f, 1f);
    [SerializeField, Min(0.1f)] private float fadeSharpness = 7.5f;
    [SerializeField] private bool debugCombatTabDistortion = true;

    [Header("TAB Effect Tuning")]
    [SerializeField] private bool useTabEffectTuning;
    [SerializeField, Range(0f, 0.15f)] private float tabCameraNoiseStrength = 0.018f;
    [SerializeField, Range(0f, 1f)] private float tabStaticOpacity = 0.075f;
    [SerializeField, Range(1f, 120f)] private float tabStaticRate = 60f;
    [SerializeField, Range(1f, 4f)] private float tabStaticPixelSize = 1.25f;
    [SerializeField, Range(0f, 0.25f)] private float tabScanlineStrength = 0.035f;
    [SerializeField, Range(0f, 1f)] private float tabBackgroundOpacity = 0.30f;
    private BattleLightingProfileSO tabTuningProfile;
    private BattleLightingProfileSO tabTuningSource;
    private bool tabPreview;
    public bool TabPreview => tabPreview;

    public void SetTabPreview(bool enabled)
    {
        tabPreview = Application.isPlaying && isActiveAndEnabled && enabled;
        if (!tabPreview) ApplyImmediate(0f);
    }

    private BattleLightingProfileSO GetTabProfile()
    {
        BattleLightingProfileSO source = colorGrading != null ? colorGrading.SourceLightingProfile : null;
        if (!useTabEffectTuning || source == null) return source;
        if (tabTuningProfile == null || tabTuningSource != source)
        {
            if (tabTuningProfile != null) Destroy(tabTuningProfile);
            tabTuningSource = source;
            tabTuningProfile = Instantiate(source);
            tabTuningProfile.name = "BattleTAB_Tuning_Runtime";
            tabTuningProfile.hideFlags = HideFlags.HideAndDontSave;
        }
        tabTuningProfile.filmGrainIntensity = filmGrainIntensity;
        tabTuningProfile.filmGrainResponse = filmGrainResponse;
        tabTuningProfile.crtSelectionChromaticAberration = chromaticAberrationIntensity;
        tabTuningProfile.lensDistortionIntensity = lensDistortionIntensity;
        tabTuningProfile.lensDistortionScale = lensDistortionScale;
        tabTuningProfile.crtCurvature = tabLensDistortion / Mathf.Max(1f, tabTuningProfile.crtSelectionCurvatureMultiplier);
        tabTuningProfile.analogNoiseStrength = tabCameraNoiseStrength;
        tabTuningProfile.crtSelectionNoiseStrength = tabStaticOpacity;
        tabTuningProfile.crtSelectionNoiseOpacityScale = 1f;
        tabTuningProfile.crtSelectionNoiseRate = tabStaticRate;
        tabTuningProfile.crtSelectionNoisePixelSize = tabStaticPixelSize;
        tabTuningProfile.scanlineStrength = tabScanlineStrength;
        tabTuningProfile.crtSelectionBackgroundOpacity = tabBackgroundOpacity;
        return tabTuningProfile;
    }

    private Volume showVolume;
    private VolumeProfile runtimeProfile;
    private FilmGrain filmGrain;
    private ChromaticAberration chromaticAberration;
    private LensDistortion lensDistortion;
    private Vignette vignette;

    private Canvas overlayCanvas;
    private CanvasGroup overlayGroup;
    private RawImage overlayImage;
    private Material overlayMaterial;
    private Material overlaySource;
    private RTHandle backgroundCapture;
    private Coroutine bindRoutine;
    private bool combatTabWasHeld;

    private float currentBlend;
    private bool selectionWasOpen;
    private float returnStartedAt = -1f;
    private float returnStartBlend;
    internal static bool IsBackgroundActive => instance != null && instance.isActiveAndEnabled &&
        (instance.currentBlend > 0.001f || instance.IsShowSelection());

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

    private void OnEnable()
    {
        if (instance == this && bindRoutine == null)
            bindRoutine = StartCoroutine(BindWhenReady());
    }

    private IEnumerator BindWhenReady()
    {
        // Presentation controllers and the camera can be created after this component's Awake.
        do
        {
            yield return null;
            ResolveReferences();
            if (targetCamera != null && colorGrading != null && colorGrading.TargetCamera != targetCamera)
                colorGrading.Configure(targetCamera, colorGrading.SourceLightingProfile);
            EnsureVolume();
            ApplyBlend();
        }
        while (runManager == null || kineticLoadout == null || presentation == null ||
               targetCamera == null || colorGrading == null ||
               colorGrading.TargetCamera != targetCamera || colorGrading.SourceLightingProfile == null);
        bindRoutine = null;
    }

    private void Update()
    {
        EnsureOverlay();
        if (runManager == null || !runManager.RunActive || runManager.State != BattleRunState.Combat)
            tabPreview = false;

        bool selectionOpen = IsShowSelection();
        BattleLightingProfileSO profile = colorGrading != null ? colorGrading.SourceLightingProfile : null;
        if (!selectionOpen && selectionWasOpen)
        {
            returnStartedAt = Time.unscaledTime;
            returnStartBlend = currentBlend;
        }
        if (selectionOpen)
        {
            returnStartedAt = -1f;
            float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, fadeSharpness) * Time.unscaledDeltaTime);
            currentBlend = Mathf.Lerp(currentBlend, 1f, t);
            if (Mathf.Abs(currentBlend - 1f) < 0.001f) currentBlend = 1f;
        }
        else if (returnStartedAt >= 0f)
        {
            float duration = profile != null ? profile.crtSelectionReturnDuration : 0.48f;
            float t = Mathf.Clamp01((Time.unscaledTime - returnStartedAt) / Mathf.Max(0.05f, duration));
            float x = t - 1f;
            float rebound = profile != null ? profile.crtSelectionReturnRebound : 1.3f;
            currentBlend = -returnStartBlend * x * x * ((rebound + 1f) * x + rebound);
            if (t >= 1f)
            {
                currentBlend = 0f;
                returnStartedAt = -1f;
            }
        }
        selectionWasOpen = selectionOpen;

        ApplyBlend();
    }

    private void OnDisable()
    {
        tabPreview = false;
        if (bindRoutine != null)
            StopCoroutine(bindRoutine);
        bindRoutine = null;
        ApplyImmediate(0f);
        ReleaseBackgroundCapture();
    }

    private void OnDestroy()
    {
        if (tabTuningProfile != null) Destroy(tabTuningProfile);
        ReleaseBackgroundCapture();
        if (overlayMaterial != null)
            Destroy(overlayMaterial);
        if (runtimeProfile != null)
        {
            if (showVolume != null && showVolume.sharedProfile == runtimeProfile)
                showVolume.sharedProfile = null;
            foreach (VolumeComponent component in runtimeProfile.components)
                if (component != null)
                    Destroy(component);
            runtimeProfile.components.Clear();
            Destroy(runtimeProfile);
        }
        if (overlayCanvas != null) Destroy(overlayCanvas.gameObject);
        if (showVolume != null) Destroy(showVolume.gameObject);

        if (instance == this)
            instance = null;
    }

    private void ResolveReferences()
    {
        if (colorGrading == null)
            colorGrading = FindFirstObjectByType<BattleColorGradingController>();
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>();
        if (presentation == null)
            presentation = BattleShowPresentationManager.Instance ?? FindFirstObjectByType<BattleShowPresentationManager>();
        if (colorGrading != null && colorGrading.TargetCamera != null &&
            targetCamera != colorGrading.TargetCamera)
        {
            targetCamera = colorGrading.TargetCamera;
            cameraData = null;
            ReleaseBackgroundCapture();
        }
        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera != null && cameraData == null)
        {
            cameraData = targetCamera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
                cameraData = targetCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        }
    }

    private bool IsShowSelection()
    {
        if (runManager == null || !runManager.RunActive)
            return false;

        return runManager.State == BattleRunState.Reward ||
               runManager.State == BattleRunState.SelectingNode ||
               (tabPreview && runManager.State == BattleRunState.Combat) ||
               (kineticLoadout != null && kineticLoadout.isActiveAndEnabled && kineticLoadout.IsSwitchBoardOpen);
    }

    private bool IsCombatTabHeld()
    {
        return isActiveAndEnabled && runManager != null && runManager.RunActive &&
               runManager.State == BattleRunState.Combat && kineticLoadout != null &&
               kineticLoadout.isActiveAndEnabled && (kineticLoadout.IsSwitchBoardOpen || tabPreview);
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
        if (cameraData != null)
            for (int layer = 0; layer < 32; layer++)
                if ((cameraData.volumeLayerMask.value & (1 << layer)) != 0)
                {
                    showVolume.gameObject.layer = layer;
                    break;
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
            overlayGroup = canvasObject.AddComponent<CanvasGroup>();
            overlayGroup.alpha = 0f;
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable = false;

            GameObject imageObject = new("BattleShowBroadcastNoise", typeof(RectTransform));
            imageObject.transform.SetParent(canvasObject.transform, false);

            RectTransform rect = (RectTransform)imageObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            overlayImage = imageObject.AddComponent<RawImage>();
            overlayImage.raycastTarget = false;
            overlayImage.color = tabBackgroundTint;
            overlayImage.enabled = false;
        }

        Material source = presentation != null ? presentation.TabBackgroundMaterial : null;
        if (source != overlaySource)
        {
            ReleaseBackgroundCapture();
            if (overlayImage != null) overlayImage.material = null;
            if (overlayMaterial != null) Destroy(overlayMaterial);
            overlaySource = source;
            overlayMaterial = null;
            if (source != null && source.HasProperty("_MainTex"))
            {
                overlayMaterial = new Material(source)
                {
                    name = "BattleTabBackground_Runtime",
                    hideFlags = HideFlags.HideAndDontSave
                };
                BattleUiMaterialController.ConfigureUnscaledTime(overlayMaterial);
                // Disable scrolling surface effects only on the runtime TAB material.
                overlayMaterial.DisableKeyword("TEXTURESCROLL_ON");
                overlayMaterial.DisableKeyword("WAVEUV_ON");
                overlayMaterial.DisableKeyword("ROUNDWAVEUV_ON");
                overlayMaterial.DisableKeyword("DISTORT_ON");
                overlayMaterial.DisableKeyword("WARP_ON");
                overlayMaterial.DisableKeyword("GLITCH_ON");
                overlayMaterial.DisableKeyword("OVERLAY_ON");
                overlayMaterial.DisableKeyword("OVERLAYMULT_ON");
                if (overlayMaterial.HasProperty("_WarpStrength")) overlayMaterial.SetFloat("_WarpStrength", 0f);
                if (overlayMaterial.HasProperty("_DistortAmount")) overlayMaterial.SetFloat("_DistortAmount", 0f);
                if (overlayMaterial.HasProperty("_OverlayBlend")) overlayMaterial.SetFloat("_OverlayBlend", 0f);
            }
        }

        if (overlayImage != null)
        {
            overlayImage.color = tabBackgroundTint;
            overlayImage.material = overlayMaterial;
        }
    }

    internal static bool WantsBackgroundCapture(Camera camera)
    {
        return instance != null && instance.isActiveAndEnabled &&
            instance.targetCamera == camera && instance.overlayMaterial != null &&
            instance.currentBlend > 0.001f;
    }

    internal static bool TryGetCameraDistortion(Camera camera, out Vector4 settings)
    {
        settings = Vector4.zero;
        if (instance == null || instance.targetCamera != camera || !instance.IsCombatTabHeld())
            return false;

        BattleColorGradingController grading = instance.colorGrading;
        BattleLightingProfileSO profile = instance.GetTabProfile();
        float curvature = grading != null && profile != null
            ? grading.CurrentTabCurvature : instance.tabLensDistortion * Mathf.Clamp01(instance.currentBlend);
        settings = new Vector4(curvature, profile != null ? profile.crtEdgeWidth : 0.1f,
            profile != null ? profile.crtEdgeStrength : 0.86f, Time.unscaledTime);
        return curvature > 0.0001f;
    }

    internal static RTHandle PrepareBackgroundCapture(Camera camera, RenderTextureDescriptor descriptor)
    {
        if (!WantsBackgroundCapture(camera)) return null;
        descriptor.depthBufferBits = 0;
        descriptor.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
        descriptor.msaaSamples = 1;
        descriptor.bindMS = false;
        descriptor.useMipMap = false;
        descriptor.autoGenerateMips = false;
        descriptor.memoryless = RenderTextureMemoryless.None;
        RenderingUtils.ReAllocateHandleIfNeeded(ref instance.backgroundCapture, descriptor,
            FilterMode.Bilinear, TextureWrapMode.Clamp, name: "BattleTabBackgroundCapture");
        instance.overlayImage.texture = instance.backgroundCapture.rt;
        return instance.backgroundCapture;
    }

    private void ReleaseBackgroundCapture()
    {
        if (overlayImage != null)
        {
            overlayImage.enabled = false;
            overlayImage.texture = null;
        }
        backgroundCapture?.Release();
        backgroundCapture = null;
    }

    private void ApplyBlend()
    {
        BattleUiMaterialController.UpdateUnscaledTime(overlayMaterial);
        BattleLightingProfileSO profile = GetTabProfile();
        bool combatTabHeld = IsCombatTabHeld();
        if (colorGrading != null) colorGrading.SetSelectionBlend(currentBlend, combatTabHeld,
            useTabEffectTuning ? profile : null);
        bool useColorGrading = colorGrading != null && colorGrading.isActiveAndEnabled &&
            colorGrading.TargetCamera == targetCamera && profile != null;
        if (filmGrain != null)
        {
            filmGrain.active = !useColorGrading;
            filmGrain.intensity.Override(profile != null ? profile.filmGrainIntensity : filmGrainIntensity);
            filmGrain.response.Override(profile != null ? profile.filmGrainResponse : filmGrainResponse);
        }
        if (chromaticAberration != null) chromaticAberration.active = !useColorGrading;
        if (lensDistortion != null) lensDistortion.active = !useColorGrading;
        if (showVolume != null)
            showVolume.weight = Mathf.Clamp01(currentBlend);
        if (chromaticAberration != null) chromaticAberration.intensity.Override(profile != null
            ? profile.crtSelectionChromaticAberration : chromaticAberrationIntensity);
        if (lensDistortion != null)
        {
            lensDistortion.intensity.Override(profile != null
                ? profile.lensDistortionIntensity : lensDistortionIntensity);
            lensDistortion.scale.Override(profile != null ? profile.lensDistortionScale : lensDistortionScale);
        }
        if (vignette != null)
        {
            vignette.color.Override(vignetteColor);
            vignette.intensity.Override(vignetteIntensity);
            vignette.smoothness.Override(vignetteSmoothness);
        }
        if (cameraData != null && currentBlend > 0.001f)
            cameraData.renderPostProcessing = true;
        if (overlayGroup != null) overlayGroup.alpha = Mathf.Clamp01(currentBlend) *
            (profile != null ? profile.crtSelectionBackgroundOpacity : 0.30f);
        if (overlayImage != null)
            overlayImage.enabled = overlayMaterial != null && backgroundCapture != null && currentBlend > 0.001f;

        if (debugCombatTabDistortion && combatTabHeld && !combatTabWasHeld)
        {
            ScriptableRenderer renderer = cameraData != null ? cameraData.scriptableRenderer : null;
            float appliedLens = useColorGrading ? colorGrading.CurrentLensDistortion :
                (lensDistortion != null ? lensDistortion.intensity.value : 0f);
            float appliedWeight = useColorGrading ? colorGrading.CurrentVolumeWeight :
                (showVolume != null ? showVolume.weight : 0f);
            Debug.Log($"[BattleTAB] BattleShowBroadcastNoiseController.ApplyBlend -> " +
                $"BattleColorGradingController.SetSelectionBlend: frame={Time.frameCount}, " +
                $"camera={(targetCamera != null ? targetCamera.name : "null")}, " +
                $"cameraId={(targetCamera != null ? targetCamera.GetInstanceID() : 0)}, " +
                $"state={(runManager != null ? runManager.State.ToString() : "null")}, " +
                $"blend={currentBlend:F4}, lens={appliedLens:F4}, volumeWeight={appliedWeight:F4}, " +
                $"cameraWarp={(TryGetCameraDistortion(targetCamera, out Vector4 warp) ? warp.x : 0f):F4}, " +
                $"setSelectionBlendCalled={colorGrading != null}, colorGradingBound={useColorGrading}, " +
                $"postProcessing={(cameraData != null && cameraData.renderPostProcessing)}, " +
                $"renderer={(renderer != null ? renderer.GetType().Name : "null")}, " +
                $"volumeMask={(cameraData != null ? cameraData.volumeLayerMask.value : 0)}, " +
                $"gradingLayer={(colorGrading != null ? colorGrading.RuntimeVolumeLayer : -1)}. " +
                "Camera warp runs in BattleTabBackgroundCaptureFeature before TAB capture; Transform/FOV stays unchanged.", this);
        }
        combatTabWasHeld = combatTabHeld;
    }

    private void ApplyImmediate(float value)
    {
        selectionWasOpen = false;
        returnStartedAt = -1f;
        currentBlend = Mathf.Clamp01(value);
        ApplyBlend();
    }
}

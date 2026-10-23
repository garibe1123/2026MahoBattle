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
    internal static event System.Action CaptureStopped;

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
    [Tooltip("Combat TAB camera curvature.")]
    [SerializeField, Range(0f, 1f)] private float tabLensDistortion = 0.14f;

    [Header("Show Lens Vignette")]
    [Tooltip("CRT/lens-like falloff that darkens only the outer corners without crushing the TV content in the center.")]
    [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.13f;
    [SerializeField, Range(0.01f, 1f)] private float vignetteSmoothness = 0.48f;
    [SerializeField] private Color vignetteColor = Color.black;

    [Header("Blend")]
    [SerializeField] private Color tabBackgroundTint = new(0.45f, 0.50f, 0.60f, 1f);
    [SerializeField, Min(0.1f)] private float fadeSharpness = 7.5f;

    [Header("TAB Effect Tuning")]
    [SerializeField, HideInInspector] private bool useTabEffectTuning;
    [SerializeField, Range(0f, 0.15f)] private float tabCameraNoiseStrength = 0.018f;
    [SerializeField, Range(0f, 1f)] private float tabStaticOpacity = 0.075f;
    [SerializeField, Range(1f, 120f)] private float tabStaticRate = 60f;
    [SerializeField, Range(1f, 4f)] private float tabStaticPixelSize = 1.25f;
    [SerializeField, Range(0f, 0.25f)] private float tabScanlineStrength = 0.035f;
    [SerializeField, Range(0f, 1f)] private float tabBackgroundOpacity = 0.30f;
    [SerializeField] private Material tabBackgroundMaterial;
    [SerializeField, Range(0f, 1f)] private float tabOverlayWeight = 0.65f;
    [SerializeField, Range(0f, 1f)] private float tabOverlayStrength = 1f;
    [SerializeField, Range(1f, 12f)] private float tabScanlineSpacing = 3f;
    [SerializeField] private Color tabStaticTint = new(0.12f, 0.17f, 0.24f, 1f);
    [SerializeField, Range(0.01f, 0.25f)] private float tabEdgeWidth = 0.10f;
    [SerializeField, Range(0f, 1f)] private float tabEdgeStrength = 0.86f;
    [SerializeField, Range(0f, 0.1f)] private float tabCenterGlow = 0.025f;
    [SerializeField, Range(0f, 1f)] private float tabColorStrength = 0.25f;
    [SerializeField] private Color tabShadowTint = new(0.40f, 0.46f, 0.60f, 1f);
    [SerializeField, Min(0.05f)] private float tabReturnDuration = 0.48f;
    [SerializeField, Range(0f, 3f)] private float tabReturnRebound = 1.3f;
    [SerializeField, HideInInspector] private bool tabSettingsMigrated;
    [SerializeField, HideInInspector] private bool tabMaterialMigrated;
    [SerializeField, HideInInspector] private bool preserveLensReaction;
    private bool tabPreview;
    public bool TabPreview => tabPreview;

    public void SetTabPreview(bool enabled)
    {
        tabPreview = Application.isPlaying && isActiveAndEnabled && enabled;
        if (!tabPreview) ApplyImmediate(0f);
    }

    public bool MigrateLegacyTabSettings(BattleLightingProfileSO source, BattleShowPresentationManager legacyPresentation)
    {
        bool changed = false;
        if (!tabMaterialMigrated && legacyPresentation != null)
        {
            if (tabBackgroundMaterial == null)
                tabBackgroundMaterial = legacyPresentation.LegacyTabBackgroundMaterial;
            tabMaterialMigrated = true;
            changed = true;
        }
        if (tabSettingsMigrated || source == null) return changed;
        if (!useTabEffectTuning)
        {
            filmGrainIntensity = source.filmGrainIntensity;
            filmGrainResponse = source.filmGrainResponse;
            chromaticAberrationIntensity = source.crtSelectionChromaticAberration;
            lensDistortionIntensity = source.lensDistortionIntensity;
            lensDistortionScale = source.lensDistortionScale;
            tabLensDistortion = source.crtCurvature * source.crtSelectionCurvatureMultiplier;
            tabCameraNoiseStrength = source.analogNoiseStrength;
            tabStaticOpacity = source.crtSelectionNoiseStrength * source.crtSelectionNoiseOpacityScale;
            tabStaticRate = source.crtSelectionNoiseRate;
            tabStaticPixelSize = source.crtSelectionNoisePixelSize;
            tabScanlineStrength = source.scanlineStrength;
            tabBackgroundOpacity = source.crtSelectionBackgroundOpacity;
        }
        tabOverlayWeight = source.crtSelectionOverlayWeight;
        tabOverlayStrength = source.analogOverlayStrength;
        tabScanlineSpacing = source.scanlineSpacingPixels;
        tabStaticTint = source.crtSelectionNoiseTint;
        tabEdgeWidth = source.crtEdgeWidth;
        tabEdgeStrength = source.crtEdgeStrength;
        tabCenterGlow = source.crtCenterGlow;
        tabColorStrength = source.crtColorStrength;
        tabShadowTint = source.crtShadowTint;
        tabReturnDuration = source.crtSelectionReturnDuration;
        tabReturnRebound = source.crtSelectionReturnRebound;
        preserveLensReaction = !useTabEffectTuning;
        tabSettingsMigrated = true;
        return true;
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
        if (instance != this) return;
        EnsureVolume();
        EnsureOverlay();
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
            float t = Mathf.Clamp01((Time.unscaledTime - returnStartedAt) / Mathf.Max(0.05f, tabReturnDuration));
            float x = t - 1f;
            float rebound = tabReturnRebound;
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
        ReleaseOverlayMaterial();
    }

    private void OnDestroy()
    {
        ReleaseOverlayMaterial();
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
        MigrateLegacyTabSettings(colorGrading != null ? colorGrading.SourceLightingProfile : null, presentation);
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

        Material source = isActiveAndEnabled && (currentBlend > 0.001f || IsShowSelection())
            ? tabBackgroundMaterial : null;
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

        float curvature = instance.tabLensDistortion * Mathf.Clamp01(instance.currentBlend);
        settings = new Vector4(curvature, instance.tabEdgeWidth,
            instance.tabEdgeStrength, Time.unscaledTime);
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

    private void ReleaseOverlayMaterial()
    {
        ReleaseBackgroundCapture();
        if (overlayImage != null) overlayImage.material = null;
        if (overlayMaterial != null) Destroy(overlayMaterial);
        overlayMaterial = null;
        overlaySource = null;
        CaptureStopped?.Invoke();
    }

    private void ApplyGrading(float selection, bool combatTabHeld)
    {
        Volume volume = colorGrading.RuntimeVolume;
        VolumeProfile profile = colorGrading.RuntimeProfile;
        BattleLightingProfileSO source = colorGrading.SourceLightingProfile;
        if (volume != null) volume.weight = Mathf.Max(colorGrading.CurrentWeight, selection);
        if (profile.TryGet(out ChromaticAberration chromatic))
            chromatic.intensity.Override(Mathf.Lerp(chromatic.intensity.value, chromaticAberrationIntensity, selection));
        if (profile.TryGet(out FilmGrain grain))
        {
            grain.intensity.Override(Mathf.Lerp(grain.intensity.value, filmGrainIntensity, selection));
            grain.response.Override(Mathf.Lerp(grain.response.value, filmGrainResponse, selection));
        }
        if (profile.TryGet(out LensDistortion lens))
        {
            float target = lensDistortionIntensity;
            if (preserveLensReaction) target += lens.intensity.value - source.lensDistortionIntensity;
            lens.intensity.Override(Mathf.Lerp(lens.intensity.value, target, selection));
            lens.scale.Override(Mathf.Lerp(lens.scale.value, lensDistortionScale, selection));
        }
        if (profile.TryGet(out SplitToning toning))
            toning.shadows.Override(Color.Lerp(toning.shadows.value, tabShadowTint, tabColorStrength * selection));

        Image image = colorGrading.RuntimeAnalogOverlay;
        Material material = colorGrading.RuntimeAnalogMaterial;
        if (image == null || material == null) return;
        material.SetFloat("_ScanlineStrength", Mathf.Lerp(source.scanlineStrength, tabScanlineStrength, selection));
        material.SetFloat("_ScanlineSpacing", Mathf.Lerp(source.scanlineSpacingPixels, tabScanlineSpacing, selection));
        material.SetFloat("_NoiseStrength", Mathf.Clamp01(material.GetFloat("_NoiseStrength") +
            (tabCameraNoiseStrength - source.analogNoiseStrength) * selection));
        material.SetFloat("_RollingBandStrength", material.GetFloat("_RollingBandStrength") *
            (1f - (combatTabHeld ? selection : 0f)));
        material.SetFloat("_Curvature", tabLensDistortion * selection);
        material.SetFloat("_EdgeWidth", tabEdgeWidth);
        material.SetFloat("_EdgeStrength", tabEdgeStrength * selection);
        material.SetFloat("_CenterGlow", tabCenterGlow * selection);
        material.SetColor("_GlassTint", tabShadowTint);
        material.SetFloat("_GlassTintStrength", tabColorStrength * selection);
        material.SetFloat("_SelectionNoiseStrength", tabStaticOpacity * selection);
        material.SetColor("_SelectionNoiseTint", tabStaticTint);
        material.SetFloat("_SelectionNoiseRate", tabStaticRate);
        material.SetFloat("_SelectionNoisePixelSize", tabStaticPixelSize);
        float strength = Mathf.Clamp01(Mathf.Max(colorGrading.CurrentWeight, tabOverlayWeight * selection) *
            Mathf.Lerp(source.analogOverlayStrength, tabOverlayStrength, selection));
        material.SetFloat("_Strength", strength);
        image.enabled = strength > 0.001f;
    }

    private void ApplyBlend()
    {
        MigrateLegacyTabSettings(colorGrading != null ? colorGrading.SourceLightingProfile : null, presentation);
        BattleUiMaterialController.UpdateUnscaledTime(overlayMaterial);
        bool combatTabHeld = IsCombatTabHeld();
        float selection = Mathf.Clamp01(currentBlend);
        if (colorGrading != null) colorGrading.RestoreRuntimePresentation();
        bool useColorGrading = colorGrading != null && colorGrading.isActiveAndEnabled &&
            colorGrading.TargetCamera == targetCamera && colorGrading.SourceLightingProfile != null &&
            colorGrading.RuntimeProfile != null;
        if (useColorGrading && selection > 0f) ApplyGrading(selection, combatTabHeld);
        if (filmGrain != null)
        {
            filmGrain.active = !useColorGrading;
            filmGrain.intensity.Override(filmGrainIntensity);
            filmGrain.response.Override(filmGrainResponse);
        }
        if (chromaticAberration != null) chromaticAberration.active = !useColorGrading;
        if (lensDistortion != null) lensDistortion.active = !useColorGrading;
        if (showVolume != null)
            showVolume.weight = selection;
        if (chromaticAberration != null) chromaticAberration.intensity.Override(chromaticAberrationIntensity);
        if (lensDistortion != null)
        {
            lensDistortion.intensity.Override(lensDistortionIntensity);
            lensDistortion.scale.Override(lensDistortionScale);
        }
        if (vignette != null)
        {
            vignette.color.Override(vignetteColor);
            vignette.intensity.Override(vignetteIntensity);
            vignette.smoothness.Override(vignetteSmoothness);
        }
        if (cameraData != null && currentBlend > 0.001f)
            cameraData.renderPostProcessing = true;
        if (overlayGroup != null) overlayGroup.alpha = selection * tabBackgroundOpacity;
        if (overlayImage != null)
            overlayImage.enabled = overlayMaterial != null && backgroundCapture != null && currentBlend > 0.001f;

        if (selection <= 0.001f) ReleaseOverlayMaterial();
    }

    private void ApplyImmediate(float value)
    {
        selectionWasOpen = false;
        returnStartedAt = -1f;
        currentBlend = Mathf.Clamp01(value);
        ApplyBlend();
    }
}

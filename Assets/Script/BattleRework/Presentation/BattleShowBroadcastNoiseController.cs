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

    [Header("Shared Selection Lens")]
    [SerializeField, Range(0f, 1f)] private float chromaticAberrationIntensity = 0.12f;
    [SerializeField, Range(-1f, 1f)] private float lensDistortionIntensity = -0.055f;
    [SerializeField, Range(0.01f, 5f)] private float lensDistortionScale = 1.015f;

    [Header("Show Lens Vignette")]
    [Tooltip("CRT/lens-like falloff that darkens only the outer corners without crushing the TV content in the center.")]
    [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.13f;
    [SerializeField, Range(0.01f, 1f)] private float vignetteSmoothness = 0.48f;
    [SerializeField] private Color vignetteColor = Color.black;

    [Header("Blend")]
    [SerializeField, Min(0.1f)] private float fadeSharpness = 7.5f;

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

    private float currentBlend;
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

    private void Start()
    {
        ResolveReferences();
        EnsureVolume();
    }

    private void Update()
    {
        EnsureOverlay();

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
        ReleaseBackgroundCapture();
    }

    private void OnDestroy()
    {
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
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>();
        if (presentation == null)
            presentation = BattleShowPresentationManager.Instance ?? FindFirstObjectByType<BattleShowPresentationManager>();
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
               (kineticLoadout != null && kineticLoadout.isActiveAndEnabled && kineticLoadout.IsSwitchBoardOpen);
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
        filmGrain.intensity.Override(0f);

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
            overlayImage.color = Color.white;
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
            }
        }

        if (overlayImage != null)
            overlayImage.material = overlayMaterial;
    }

    internal static bool WantsBackgroundCapture(Camera camera)
    {
        return instance != null && instance.isActiveAndEnabled &&
            instance.targetCamera == camera && instance.overlayMaterial != null &&
            instance.currentBlend > 0.001f;
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
        if (showVolume != null)
            showVolume.weight = Mathf.Clamp01(currentBlend);
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
        if (overlayGroup != null) overlayGroup.alpha = Mathf.Clamp01(currentBlend);
        if (overlayImage != null)
            overlayImage.enabled = overlayMaterial != null && backgroundCapture != null && currentBlend > 0.001f;
    }

    private void ApplyImmediate(float value)
    {
        currentBlend = Mathf.Clamp01(value);
        ApplyBlend();
    }
}

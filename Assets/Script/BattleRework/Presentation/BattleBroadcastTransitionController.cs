using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Battle scene-level broadcast transition.
///
/// This is intentionally NOT a room/stage transition effect.
/// - The scene is covered in black immediately.
/// - The intro reveal starts only after the battle runtime has finished its bootstrap work.
/// - Once ready: diagonal reveal wipe + strong LensDistortion settles like a broadcast/camera powering on.
/// - During the entire battle run: no scanline, static, CRT noise, LIVE bug, or per-stage effect.
/// - Once when the whole battle run reaches Ended: diagonal black wipe covers the scene and stays black.
///
/// The black plate doubles as a bootstrap cover: runtime component installation, first-run setup,
/// and the first Canvas rebuild happen before the player sees the scene.
/// </summary>
[DefaultExecutionOrder(34000)]
[DisallowMultipleComponent]
public sealed class BattleBroadcastTransitionController : MonoBehaviour
{
    private const int CanvasSortingOrder = 2400;
    private static BattleBroadcastTransitionController instance;

    [Header("Scene Start")]
    [Tooltip("Bootstrap ready 이후에도 검정을 추가 유지할 시간입니다. 보통 0이면 충분합니다.")]
    [SerializeField, Min(0f)] private float sceneStartBlackHold = 0f;
    [SerializeField, Min(0.01f)] private float sceneRevealWipeDuration = 0.24f;
    [SerializeField, Min(0.01f)] private float distortionSettleDuration = 0.72f;
    [Tooltip("이 시간 안에 전투 Runtime이 준비되지 않으면 화면을 열지 않고 오류만 기록합니다.")]
    [SerializeField, Min(1f)] private float bootstrapReadyTimeout = 10f;

    [Header("Strong Broadcast Distortion")]
    [SerializeField, Range(-1f, 1f)] private float distortionPeak = -0.92f;
    [SerializeField, Range(0f, 0.5f)] private float distortionWobble = 0.24f;
    [SerializeField, Range(0.45f, 1f)] private float distortionStartScale = 0.72f;
    [SerializeField, Range(0f, 0.25f)] private float distortionCenterWobble = 0.055f;
    [SerializeField, Range(0.1f, 1f)] private float distortionStartYMultiplier = 0.62f;
    [SerializeField, Range(1f, 8f)] private float distortionOscillations = 3.4f;

    [Header("Scene End")]
    [SerializeField, Min(0.01f)] private float sceneEndWipeDuration = 0.28f;

    [Header("Black Wipe")]
    [SerializeField] private float wipeRotation = -9f;
    [SerializeField, Min(1200f)] private float wipeTravel = 2700f;
    [SerializeField] private Vector2 wipeSize = new(3300f, 1900f);

    private BattleStageTransitionController stageFlow;
    private Coroutine bindRoutine;
    private Coroutine bootstrapRoutine;
    private Coroutine transitionRoutine;

    private Canvas overlayCanvas;
    private Image blackFill;
    private RectTransform wipeRect;
    private Image wipeImage;

    private Volume distortionVolume;
    private VolumeProfile distortionProfile;
    private LensDistortion lensDistortion;

    private bool sceneIntroPlayed;
    private bool sceneOutroStarted;

    public static BattleBroadcastTransitionController Instance => instance;
    public bool IsTransitioning => transitionRoutine != null;
    public bool IsSceneCovered => blackFill != null && blackFill.gameObject.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterSceneLoad()
    {
        TryInstallForScene(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode _)
    {
        TryInstallForScene(scene);
    }

    private static void TryInstallForScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        BattleSceneManager[] managers = FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        bool isBattleScene = false;
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.gameObject.scene == scene)
            {
                isBattleScene = true;
                break;
            }
        }

        if (!isBattleScene)
            return;

        BattleBroadcastTransitionController[] existing =
            FindObjectsByType<BattleBroadcastTransitionController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        for (int i = 0; i < existing.Length; i++)
        {
            BattleBroadcastTransitionController controller = existing[i];
            if (controller != null && controller.gameObject.scene == scene)
                return;
        }

        GameObject host = new("BattleSceneBroadcastTransition");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<BattleBroadcastTransitionController>();
    }

    private void Awake()
    {
        instance = this;
        EnsureOverlay();
        EnsureDistortionVolume();
        SetBlackImmediate(true);
        ResetDistortion();
    }

    private void Start()
    {
        if (bootstrapRoutine == null)
            bootstrapRoutine = StartCoroutine(WaitForBootstrapThenReveal());
    }

    private void OnEnable()
    {
        EnsureOverlay();
        EnsureDistortionVolume();
        SetBlackImmediate(true);

        if (bindRoutine == null)
            bindRoutine = StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
            StopCoroutine(bindRoutine);
        if (bootstrapRoutine != null)
            StopCoroutine(bootstrapRoutine);
        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);

        bindRoutine = null;
        bootstrapRoutine = null;
        transitionRoutine = null;
        UnbindStageFlow();
        ResetDistortion();
    }

    private void OnDestroy()
    {
        UnbindStageFlow();
        ResetDistortion();

        if (distortionProfile != null)
            Destroy(distortionProfile);

        if (instance == this)
            instance = null;
    }

    private IEnumerator WaitForBootstrapThenReveal()
    {
        SetBlackImmediate(true);
        ResetDistortion();

        float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(1f, bootstrapReadyTimeout);
        while (enabled && !sceneOutroStarted && Time.realtimeSinceStartup < timeoutAt)
        {
            if (IsBattleRuntimeReady())
                break;

            yield return null;
        }

        if (!enabled || sceneOutroStarted)
        {
            bootstrapRoutine = null;
            yield break;
        }

        if (!IsBattleRuntimeReady())
        {
            Debug.LogError(
                "[BattleBroadcastTransition] Battle runtime did not become ready before timeout. " +
                "Keeping the scene covered in black instead of revealing an incomplete frame.",
                this);
            bootstrapRoutine = null;
            yield break;
        }

        // Flush the first expensive UI rebuild while the scene is still completely covered.
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();

        if (!enabled || sceneOutroStarted)
        {
            bootstrapRoutine = null;
            yield break;
        }

        bootstrapRoutine = null;
        PlaySceneStartOnce();
    }

    private bool IsBattleRuntimeReady()
    {
        BattleSceneManager manager = BattleSceneManager.Instance != null
            ? BattleSceneManager.Instance
            : FindFirstObjectByType<BattleSceneManager>(FindObjectsInactive.Include);
        if (manager == null || manager.RunManager == null || manager.Player == null)
            return false;

        // Normal battle entry is auto-started by BattleSceneEntry. Requiring RunActive here means
        // all synchronous StartRun work (including initial room setup) finishes under the black cover.
        if (!manager.RunManager.RunActive)
            return false;

        if (Camera.main == null)
            return false;

        BattleStageTransitionController resolvedStageFlow = BattleStageTransitionController.Instance != null
            ? BattleStageTransitionController.Instance
            : FindFirstObjectByType<BattleStageTransitionController>(FindObjectsInactive.Include);
        if (resolvedStageFlow == null)
            return false;

        if (FindFirstObjectByType<BattleCameraController>(FindObjectsInactive.Include) == null)
            return false;
        if (FindFirstObjectByType<BattleHUD>(FindObjectsInactive.Include) == null)
            return false;
        if (FindFirstObjectByType<BattleShowWorldSetController>(FindObjectsInactive.Include) == null)
            return false;
        if (FindFirstObjectByType<BattleSpotlightBeamDirectionController>(FindObjectsInactive.Include) == null)
            return false;

        return true;
    }

    private IEnumerator BindWhenReady()
    {
        while (enabled)
        {
            BattleStageTransitionController resolved = BattleStageTransitionController.Instance != null
                ? BattleStageTransitionController.Instance
                : FindFirstObjectByType<BattleStageTransitionController>(FindObjectsInactive.Include);

            if (resolved != null)
            {
                BindStageFlow(resolved);
                break;
            }

            yield return null;
        }

        bindRoutine = null;
    }

    private void BindStageFlow(BattleStageTransitionController resolved)
    {
        if (stageFlow == resolved)
            return;

        UnbindStageFlow();
        stageFlow = resolved;
        stageFlow.FlowStateChanged += HandleFlowStateChanged;

        if (stageFlow.FlowState == BattleStageFlowState.Ended)
            PlaySceneEndWipeOnce();
    }

    private void UnbindStageFlow()
    {
        if (stageFlow != null)
            stageFlow.FlowStateChanged -= HandleFlowStateChanged;
        stageFlow = null;
    }

    private void HandleFlowStateChanged(BattleStageFlowState next)
    {
        if (next == BattleStageFlowState.Ended)
            PlaySceneEndWipeOnce();
    }

    public void PlaySceneStartOnce()
    {
        if (sceneIntroPlayed || sceneOutroStarted)
            return;

        sceneIntroPlayed = true;
        StopTransition();
        transitionRoutine = StartCoroutine(SceneStartRoutine());
    }

    public void PlaySceneEndWipeOnce()
    {
        if (sceneOutroStarted)
            return;

        sceneOutroStarted = true;

        if (bootstrapRoutine != null)
        {
            StopCoroutine(bootstrapRoutine);
            bootstrapRoutine = null;
        }

        StopTransition();
        transitionRoutine = StartCoroutine(SceneEndRoutine());
    }

    private void StopTransition()
    {
        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);
        transitionRoutine = null;
    }

    private IEnumerator SceneStartRoutine()
    {
        EnsureOverlay();
        EnsureDistortionVolume();

        SetBlackImmediate(true);
        wipeImage.gameObject.SetActive(false);
        ResetDistortion();

        if (sceneStartBlackHold > 0f)
            yield return WaitRealtime(sceneStartBlackHold);

        blackFill.gameObject.SetActive(false);
        wipeImage.gameObject.SetActive(true);
        wipeRect.localRotation = Quaternion.Euler(0f, 0f, wipeRotation);
        wipeRect.anchoredPosition = Vector2.zero;

        if (distortionVolume != null)
            distortionVolume.weight = 1f;

        float totalDuration = Mathf.Max(
            Mathf.Max(0.01f, sceneRevealWipeDuration),
            Mathf.Max(0.01f, distortionSettleDuration));
        float elapsed = 0f;

        while (elapsed < totalDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            float wipeT = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, sceneRevealWipeDuration));
            float wipeEase = EaseInOutCubic(wipeT);
            wipeRect.anchoredPosition = new Vector2(
                Mathf.Lerp(0f, Mathf.Abs(wipeTravel), wipeEase),
                0f);

            float distortionT = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, distortionSettleDuration));
            ApplyStartupDistortion(distortionT);
            yield return null;
        }

        wipeImage.gameObject.SetActive(false);
        ResetDistortion();
        transitionRoutine = null;
    }

    private IEnumerator SceneEndRoutine()
    {
        EnsureOverlay();
        ResetDistortion();

        blackFill.gameObject.SetActive(false);
        wipeImage.gameObject.SetActive(true);
        wipeRect.localRotation = Quaternion.Euler(0f, 0f, wipeRotation);
        wipeRect.anchoredPosition = new Vector2(-Mathf.Abs(wipeTravel), 0f);

        float duration = Mathf.Max(0.01f, sceneEndWipeDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = EaseOutCubic(t);
            wipeRect.anchoredPosition = new Vector2(
                Mathf.Lerp(-Mathf.Abs(wipeTravel), 0f, eased),
                0f);
            yield return null;
        }

        SetBlackImmediate(true);
        wipeImage.gameObject.SetActive(false);
        transitionRoutine = null;
    }

    private void ApplyStartupDistortion(float normalizedTime)
    {
        if (lensDistortion == null || distortionVolume == null)
            return;

        float t = Mathf.Clamp01(normalizedTime);
        float settle = Smooth01(t);
        float envelope = 1f - settle;
        envelope *= envelope;

        float wave = Mathf.Sin(t * Mathf.PI * 2f * Mathf.Max(1f, distortionOscillations));
        float secondaryWave = Mathf.Sin(t * Mathf.PI * 2f * (distortionOscillations * 0.63f) + 0.7f);

        float intensity = distortionPeak * envelope + wave * distortionWobble * envelope;
        float scale = Mathf.Lerp(distortionStartScale, 1f, settle) + wave * 0.11f * envelope;
        float centerX = wave * distortionCenterWobble * envelope;
        float centerY = secondaryWave * distortionCenterWobble * 0.45f * envelope;

        lensDistortion.intensity.value = Mathf.Clamp(intensity, -1f, 1f);
        lensDistortion.scale.value = Mathf.Clamp(scale, 0.45f, 1.35f);
        lensDistortion.center.value = new Vector2(centerX, centerY);
        lensDistortion.xMultiplier.value = 1f;
        lensDistortion.yMultiplier.value = Mathf.Lerp(distortionStartYMultiplier, 1f, settle);
        distortionVolume.weight = 1f;
    }

    private void EnsureDistortionVolume()
    {
        if (distortionVolume != null && lensDistortion != null)
            return;

        distortionVolume = GetComponent<Volume>();
        if (distortionVolume == null)
            distortionVolume = gameObject.AddComponent<Volume>();

        distortionVolume.isGlobal = true;
        distortionVolume.priority = 10000f;
        distortionVolume.weight = 0f;

        if (distortionProfile == null)
        {
            distortionProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            distortionProfile.name = "BattleSceneBroadcastDistortion_Runtime";
            distortionProfile.hideFlags = HideFlags.HideAndDontSave;
        }

        distortionVolume.sharedProfile = distortionProfile;

        if (!distortionProfile.TryGet(out lensDistortion))
            lensDistortion = distortionProfile.Add<LensDistortion>(true);

        lensDistortion.active = true;
        lensDistortion.intensity.overrideState = true;
        lensDistortion.xMultiplier.overrideState = true;
        lensDistortion.yMultiplier.overrideState = true;
        lensDistortion.center.overrideState = true;
        lensDistortion.scale.overrideState = true;

        ResetDistortion();
    }

    private void ResetDistortion()
    {
        if (lensDistortion != null)
        {
            lensDistortion.intensity.value = 0f;
            lensDistortion.xMultiplier.value = 1f;
            lensDistortion.yMultiplier.value = 1f;
            lensDistortion.center.value = Vector2.zero;
            lensDistortion.scale.value = 1f;
        }

        if (distortionVolume != null)
            distortionVolume.weight = 0f;
    }

    private void EnsureOverlay()
    {
        if (overlayCanvas != null && blackFill != null && wipeRect != null && wipeImage != null)
            return;

        GameObject canvasObject = new("BattleSceneBroadcastCanvas");
        canvasObject.transform.SetParent(transform, false);

        overlayCanvas = canvasObject.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = CanvasSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform blackRect = CreateRect(canvasObject.transform, "SceneBlackFill", Vector2.zero);
        Stretch(blackRect);
        blackFill = blackRect.gameObject.AddComponent<Image>();
        blackFill.color = Color.black;
        blackFill.raycastTarget = false;

        wipeRect = CreateRect(canvasObject.transform, "SceneBlackWipe", wipeSize);
        wipeRect.anchorMin = wipeRect.anchorMax = new Vector2(0.5f, 0.5f);
        wipeRect.pivot = new Vector2(0.5f, 0.5f);
        wipeRect.localRotation = Quaternion.Euler(0f, 0f, wipeRotation);
        wipeImage = wipeRect.gameObject.AddComponent<Image>();
        wipeImage.color = Color.black;
        wipeImage.raycastTarget = false;
    }

    private void SetBlackImmediate(bool visible)
    {
        EnsureOverlay();
        blackFill.gameObject.SetActive(visible);

        if (visible)
            blackFill.transform.SetAsLastSibling();
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static IEnumerator WaitRealtime(float duration)
    {
        float end = Time.unscaledTime + Mathf.Max(0f, duration);
        while (Time.unscaledTime < end)
            yield return null;
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    private static float EaseInOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }
}

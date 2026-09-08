using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Reward / Map Selection 전용 Show Focus Mask.
///
/// 규칙:
/// - Normal Battle에서는 완전히 OFF.
/// - Player / Presenter는 캐릭터 하부 쪽으로 내려간 타원형 Stage Focus.
/// - TV / Screen은 실제 WorldSpace RectTransform을 기준으로 사각형 Focus.
/// - Reward Item / Map 대상에는 별도 천장 Spotlight를 만들지 않음.
/// - 첫 Map 선택은 TV 도킹 완료를 기다리지 않고 카메라 이동과 암전을 먼저 시작.
/// - Reward는 기존보다 강한 쇼 암전을 유지하고, 첫 Map은 Base가 조금 더 읽히도록 약하게 암전.
/// </summary>
[DefaultExecutionOrder(26000)]
[DisallowMultipleComponent]
public sealed class BattleShowFocusController : MonoBehaviour
{
    private const string ShaderName = "UI/BattleShowFocusMask";
    private const string MountedTvName = "BattleShowMountedTV";

    private static BattleShowFocusController instance;

    [Header("References")]
    [SerializeField] private BattleCameraController battleCamera;
    [SerializeField] private BattleShowWorldSetController showWorldSet;
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private PlayerController player;

    [Header("Overlay")]
    [Tooltip("BattleHUD(기본 500)보다 뒤에 두어 월드만 암전시키고 HUD는 그대로 유지합니다.")]
    [SerializeField] private int overlaySortingOrder = 450;
    [SerializeField] private Color dimColor = Color.black;

    [Header("Reward / Normal Map Enter")]
    [SerializeField, Min(0f)] private float cameraLeadBeforeDim = 0.10f;
    [SerializeField, Min(0.01f)] private float dimFadeInDuration = 0.20f;
    [SerializeField, Min(0f)] private float focusLeadAfterDim = 0.08f;
    [SerializeField, Min(0.01f)] private float focusFadeInDuration = 0.16f;

    [Header("Opening Map Enter")]
    [Tooltip("첫 맵 선택은 TV Carrier가 도킹하기 전부터 카메라/암전이 시작됩니다.")]
    [SerializeField, Min(0f)] private float openingMapCameraLeadBeforeDim = 0.025f;
    [SerializeField, Min(0.01f)] private float openingMapDimFadeInDuration = 0.30f;
    [SerializeField, Min(0f)] private float openingMapFocusLeadAfterDim = 0.09f;
    [SerializeField, Min(0.01f)] private float openingMapFocusFadeInDuration = 0.19f;

    [Header("Show Exit Order")]
    [SerializeField, Min(0.01f)] private float exitFocusFadeDuration = 0.12f;
    [SerializeField, Min(0.01f)] private float exitDimFadeDuration = 0.18f;
    [SerializeField, Min(0f)] private float cameraReturnDelay = 0.05f;

    [Header("Reward / Normal Map Background Dim")]
    [SerializeField, Range(0f, 1f)] private float nearDimAlpha = 0.62f;
    [SerializeField, Range(0f, 1f)] private float farDimAlpha = 0.96f;
    [SerializeField, Range(0.05f, 1.5f)] private float dimFalloffRadius = 0.58f;

    [Header("Opening Map Background Dim")]
    [Tooltip("첫 4x4 Base가 완전히 사라져 보이지 않도록 Reward보다 덜 어둡게 유지합니다.")]
    [SerializeField, Range(0f, 1f)] private float openingMapNearDimAlpha = 0.48f;
    [SerializeField, Range(0f, 1f)] private float openingMapFarDimAlpha = 0.92f;
    [SerializeField, Range(0.05f, 1.5f)] private float openingMapDimFalloffRadius = 0.66f;

    [Header("Character Stage Focus")]
    [SerializeField, Min(0.1f)] private float playerFocusRadiusWorld = 1.48f;
    [SerializeField, Min(0.1f)] private float presenterFocusRadiusWorld = 1.92f;
    [Tooltip("1이면 원형, 작을수록 위아래로 눌린 타원형입니다.")]
    [SerializeField, Range(0.2f, 1f)] private float characterVerticalRatio = 0.48f;
    [Tooltip("Focus 중심을 Sprite 중심보다 아래로 내립니다. 발밑 Stage Light 느낌을 강화합니다.")]
    [SerializeField, Range(0f, 1f)] private float characterLowerOffset = 0.20f;
    [SerializeField, Range(0.001f, 0.08f)] private float characterFeather = 0.018f;

    [Header("Screen Rect Focus")]
    [Tooltip("0에 가까울수록 TV 실제 화면 경계에 딱 맞는 직사각형입니다.")]
    [SerializeField, Range(0.0001f, 0.04f)] private float rectFeather = 0.0035f;
    [SerializeField, Range(-0.05f, 0.05f)] private float screenRectPadding = 0.002f;

    private Canvas overlayCanvas;
    private Image overlayImage;
    private Material runtimeMaterial;
    private Shader focusShader;
    private RectTransform tvFocusRect;

    private bool runSubscribed;
    private bool selectionShowRequested;
    private float showStageBecameActiveAt = -1f;
    private float currentDimBlend;
    private float currentFocusBlend;

    private bool hasLastShowFrame;
    private Vector3 lastShowTarget;
    private float lastShowZoom;
    private int exitCameraHoldRequest;

    public static BattleShowFocusController Instance => instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        bool battleScene = scene.name == BattleSceneEntry.DefaultBattleSceneName;
        if (!battleScene)
        {
            BattleSceneManager manager = Object.FindFirstObjectByType<BattleSceneManager>();
            battleScene = manager != null && manager.gameObject.scene == scene;
        }

        if (!battleScene || Object.FindFirstObjectByType<BattleShowFocusController>() != null)
            return;

        GameObject host = new("BattleShowFocusRuntime");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<BattleShowFocusController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        ResolveReferences();
        SubscribeRunState();
        EnsureOverlay();
        ApplyHiddenImmediate();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeRunState();
        EnsureOverlay();
    }

    private void OnDisable()
    {
        UnsubscribeRunState();
        ReleaseExitCameraHold();
        ApplyHiddenImmediate();
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);

        if (instance == this)
            instance = null;
    }

    private void LateUpdate()
    {
        ResolveReferences();
        SubscribeRunState();
        ResolveTvFocusRect();
        EnsureOverlay();

        Camera camera = Camera.main;
        if (runtimeMaterial == null || camera == null)
            return;

        float now = Time.unscaledTime;
        float deltaTime = Time.unscaledDeltaTime;

        bool stageActive = selectionShowRequested && showWorldSet != null && showWorldSet.IsShowActive;
        if (stageActive)
        {
            if (showStageBecameActiveAt < 0f)
                showStageBecameActiveAt = now;

            lastShowTarget = showWorldSet.CameraTargetWorld;
            lastShowZoom = showWorldSet.ShowCameraSize;
            hasLastShowFrame = true;
        }

        bool openingMap = IsOpeningMapShow();

        if (selectionShowRequested && showStageBecameActiveAt >= 0f)
        {
            float elapsed = now - showStageBecameActiveAt;
            float lead = openingMap ? openingMapCameraLeadBeforeDim : cameraLeadBeforeDim;
            float dimDuration = openingMap ? openingMapDimFadeInDuration : dimFadeInDuration;
            float focusLead = openingMap ? openingMapFocusLeadAfterDim : focusLeadAfterDim;
            float focusDuration = openingMap ? openingMapFocusFadeInDuration : focusFadeInDuration;

            float dimTarget = SmoothRange(
                elapsed,
                lead,
                lead + Mathf.Max(0.01f, dimDuration));

            float focusStart = lead + Mathf.Max(0f, focusLead);
            float focusTarget = SmoothRange(
                elapsed,
                focusStart,
                focusStart + Mathf.Max(0.01f, focusDuration));

            currentDimBlend = MoveTowards01(currentDimBlend, dimTarget, dimDuration, deltaTime);
            currentFocusBlend = MoveTowards01(currentFocusBlend, focusTarget, focusDuration, deltaTime);
        }
        else if (!selectionShowRequested)
        {
            currentFocusBlend = MoveTowards01(
                currentFocusBlend,
                0f,
                exitFocusFadeDuration,
                deltaTime);

            if (currentFocusBlend <= 0.001f)
            {
                currentDimBlend = MoveTowards01(
                    currentDimBlend,
                    0f,
                    exitDimFadeDuration,
                    deltaTime);
            }
        }

        Vector2 playerUv = new(0.5f, 0.5f);
        float playerRadiusUv = 0f;
        bool playerVisible = TryProjectCharacter(
            camera,
            player != null && player.IsAlive ? player.transform : null,
            playerFocusRadiusWorld,
            out playerUv,
            out playerRadiusUv);

        Transform presenter = showWorldSet != null ? showWorldSet.PresenterWorldTransform : null;
        Vector2 presenterUv = new(0.5f, 0.5f);
        float presenterRadiusUv = 0f;
        bool presenterVisible = TryProjectCharacter(
            camera,
            presenter,
            presenterFocusRadiusWorld,
            out presenterUv,
            out presenterRadiusUv);

        Vector4 screenRect = new(0.5f, 0.5f, 0.5f, 0.5f);
        bool screenVisible = TryProjectScreenRect(camera, out screenRect);

        float activeNearDim = openingMap ? openingMapNearDimAlpha : nearDimAlpha;
        float activeFarDim = openingMap ? openingMapFarDimAlpha : farDimAlpha;
        float activeDimRadius = openingMap ? openingMapDimFalloffRadius : dimFalloffRadius;

        runtimeMaterial.SetColor("_MaskColor", dimColor);
        runtimeMaterial.SetFloat("_Presentation", currentDimBlend);

        runtimeMaterial.SetVector("_DimCenter", new Vector4(playerUv.x, playerUv.y, 0f, 0f));
        runtimeMaterial.SetFloat("_NearDimAlpha", activeNearDim);
        runtimeMaterial.SetFloat("_FarDimAlpha", activeFarDim);
        runtimeMaterial.SetFloat("_DimRadius", Mathf.Max(0.001f, activeDimRadius));

        runtimeMaterial.SetVector("_PlayerCenter", new Vector4(playerUv.x, playerUv.y, 0f, 0f));
        runtimeMaterial.SetFloat("_PlayerRadius", playerRadiusUv);
        runtimeMaterial.SetFloat("_PlayerStrength", playerVisible ? currentFocusBlend : 0f);

        runtimeMaterial.SetVector("_PresenterCenter", new Vector4(presenterUv.x, presenterUv.y, 0f, 0f));
        runtimeMaterial.SetFloat("_PresenterRadius", presenterRadiusUv);
        runtimeMaterial.SetFloat("_PresenterStrength", presenterVisible ? currentFocusBlend : 0f);

        runtimeMaterial.SetVector("_ScreenRect", screenRect);
        runtimeMaterial.SetFloat("_ScreenStrength", screenVisible ? currentFocusBlend : 0f);

        runtimeMaterial.SetFloat("_CharacterVerticalRatio", Mathf.Clamp(characterVerticalRatio, 0.2f, 1f));
        runtimeMaterial.SetFloat("_CharacterLowerOffset", Mathf.Clamp01(characterLowerOffset));
        runtimeMaterial.SetFloat("_CircleFeather", Mathf.Max(0.0001f, characterFeather));
        runtimeMaterial.SetFloat("_RectFeather", Mathf.Max(0.0001f, rectFeather));

        if (overlayImage != null)
            overlayImage.enabled = currentDimBlend > 0.0001f;
    }

    private void ResolveReferences()
    {
        if (battleCamera == null)
            battleCamera = FindFirstObjectByType<BattleCameraController>();
        if (showWorldSet == null)
            showWorldSet = FindFirstObjectByType<BattleShowWorldSetController>();
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
    }

    private void SubscribeRunState()
    {
        if (runSubscribed || runManager == null)
            return;

        runManager.StateChanged += HandleRunStateChanged;
        runSubscribed = true;
        selectionShowRequested = IsSelectionShowRequested();
    }

    private void UnsubscribeRunState()
    {
        if (!runSubscribed)
            return;

        if (runManager != null)
            runManager.StateChanged -= HandleRunStateChanged;

        runSubscribed = false;
    }

    private void HandleRunStateChanged(BattleRunState _)
    {
        bool nextShow = IsSelectionShowRequested();
        if (nextShow == selectionShowRequested)
            return;

        if (nextShow)
        {
            ReleaseExitCameraHold();
            showStageBecameActiveAt = -1f;
        }
        else
        {
            HoldLastShowCameraFrame();
            showStageBecameActiveAt = -1f;
        }

        selectionShowRequested = nextShow;
    }

    private bool IsSelectionShowRequested()
    {
        if (runManager == null || !runManager.RunActive)
            return false;

        return runManager.State == BattleRunState.Reward ||
               runManager.State == BattleRunState.SelectingNode;
    }

    private bool IsOpeningMapShow()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.SelectingNode &&
               runManager.IsInStartArea;
    }

    private void HoldLastShowCameraFrame()
    {
        if (battleCamera == null)
            return;

        ReleaseExitCameraHold();

        Camera camera = Camera.main;
        Vector3 holdTarget = hasLastShowFrame
            ? lastShowTarget
            : camera != null
                ? camera.transform.position
                : Vector3.zero;
        float holdZoom = hasLastShowFrame
            ? lastShowZoom
            : camera != null
                ? camera.orthographicSize
                : 0f;

        float holdDuration = Mathf.Max(0.01f, exitFocusFadeDuration) +
                             Mathf.Max(0.01f, exitDimFadeDuration) +
                             Mathf.Max(0f, cameraReturnDelay);

        exitCameraHoldRequest = battleCamera.FocusPosition(
            holdTarget,
            holdDuration,
            holdZoom,
            BattleCameraFocusPriority.Show);
    }

    private void ReleaseExitCameraHold()
    {
        if (exitCameraHoldRequest == 0)
            return;

        battleCamera?.ReleaseFocus(exitCameraHoldRequest);
        exitCameraHoldRequest = 0;
    }

    private void ResolveTvFocusRect()
    {
        if (tvFocusRect != null)
            return;
        if (showWorldSet == null)
            return;

        RectTransform[] rects = showWorldSet.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect != null && rect.name == MountedTvName)
            {
                tvFocusRect = rect;
                return;
            }
        }
    }

    private void EnsureOverlay()
    {
        if (overlayCanvas != null && overlayImage != null && runtimeMaterial != null)
            return;

        if (focusShader == null)
            focusShader = Shader.Find(ShaderName);
        if (focusShader == null)
            focusShader = Resources.Load<Shader>("BattleShowFocusMask");

        if (focusShader == null)
        {
            Debug.LogError(
                $"[BattleShowFocusController] Shader '{ShaderName}'를 찾지 못했습니다. " +
                "Assets/Resources/BattleShowFocusMask.shader를 확인하세요.",
                this);
            enabled = false;
            return;
        }

        if (overlayCanvas == null)
        {
            GameObject canvasObject = new("BattleShowFocusCanvas");
            canvasObject.transform.SetParent(transform, false);

            overlayCanvas = canvasObject.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder = overlaySortingOrder;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (overlayImage == null)
        {
            GameObject imageObject = new("ShowFocusMask");
            imageObject.transform.SetParent(overlayCanvas.transform, false);

            RectTransform rect = imageObject.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            overlayImage = imageObject.AddComponent<Image>();
            overlayImage.raycastTarget = false;
            overlayImage.color = Color.white;
        }

        if (runtimeMaterial == null)
        {
            runtimeMaterial = new Material(focusShader)
            {
                name = "BattleShowFocusMask_Runtime"
            };
            overlayImage.material = runtimeMaterial;
        }
    }

    private bool TryProjectCharacter(
        Camera camera,
        Transform target,
        float radiusWorld,
        out Vector2 centerUv,
        out float radiusUv)
    {
        centerUv = new Vector2(0.5f, 0.5f);
        radiusUv = 0f;

        if (camera == null || target == null || !target.gameObject.activeInHierarchy)
            return false;

        Vector3 centerScreen = camera.WorldToScreenPoint(target.position);
        if (centerScreen.z <= 0f)
            return false;

        Vector3 edgeWorld = target.position + camera.transform.right * Mathf.Max(0.01f, radiusWorld);
        Vector3 edgeScreen = camera.WorldToScreenPoint(edgeWorld);

        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);

        centerUv = new Vector2(
            Mathf.Clamp01(centerScreen.x / width),
            Mathf.Clamp01(centerScreen.y / height));

        radiusUv = Mathf.Abs(edgeScreen.x - centerScreen.x) / height;
        radiusUv = Mathf.Max(0.0001f, radiusUv);
        return true;
    }

    private bool TryProjectScreenRect(Camera camera, out Vector4 rectUv)
    {
        rectUv = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);

        if (camera == null || tvFocusRect == null || !tvFocusRect.gameObject.activeInHierarchy)
            return false;

        Vector3[] corners = new Vector3[4];
        tvFocusRect.GetWorldCorners(corners);

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);

        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 screen = camera.WorldToScreenPoint(corners[i]);
            if (screen.z <= 0f)
                return false;

            float x = screen.x / width;
            float y = screen.y / height;

            minX = Mathf.Min(minX, x);
            minY = Mathf.Min(minY, y);
            maxX = Mathf.Max(maxX, x);
            maxY = Mathf.Max(maxY, y);
        }

        minX -= screenRectPadding;
        minY -= screenRectPadding;
        maxX += screenRectPadding;
        maxY += screenRectPadding;

        rectUv = new Vector4(
            Mathf.Clamp01(minX),
            Mathf.Clamp01(minY),
            Mathf.Clamp01(maxX),
            Mathf.Clamp01(maxY));

        return rectUv.z > rectUv.x && rectUv.w > rectUv.y;
    }

    private void ApplyHiddenImmediate()
    {
        currentDimBlend = 0f;
        currentFocusBlend = 0f;
        showStageBecameActiveAt = -1f;

        if (runtimeMaterial != null)
        {
            runtimeMaterial.SetFloat("_Presentation", 0f);
            runtimeMaterial.SetFloat("_PlayerStrength", 0f);
            runtimeMaterial.SetFloat("_PresenterStrength", 0f);
            runtimeMaterial.SetFloat("_ScreenStrength", 0f);
        }

        if (overlayImage != null)
            overlayImage.enabled = false;
    }

    private static float MoveTowards01(float current, float target, float duration, float deltaTime)
    {
        if (duration <= 0.0001f)
            return Mathf.Clamp01(target);

        return Mathf.MoveTowards(
            Mathf.Clamp01(current),
            Mathf.Clamp01(target),
            Mathf.Max(0f, deltaTime) / duration);
    }

    private static float SmoothRange(float value, float from, float to)
    {
        if (to <= from)
            return value >= to ? 1f : 0f;

        float t = Mathf.InverseLerp(from, to, value);
        return t * t * (3f - 2f * t);
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Combat-only player focus mask.
///
/// This is deliberately separate from:
/// - BattleCombatCornerVignetteController: lens/corner darkening only.
/// - BattleShowFocusController: Reward / Map / Presenter / TV focus only.
///
/// The combat mask is a uniform translucent darkness with one soft, slightly flattened
/// player hole. The hole trails the player slightly and stretches/rotates from the
/// fake-3D spotlight rig state.
/// </summary>
[DefaultExecutionOrder(24700)]
[DisallowMultipleComponent]
public sealed class BattleCombatFocusMaskController : MonoBehaviour
{
    private const string ShaderName = "UI/BattleCombatFocusMask";
    private const int OverlaySortingOrder = 440;

    private static BattleCombatFocusMaskController instance;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleStageTransitionController stageFlow;
    [SerializeField] private PlayerController player;

    [Header("Combat Dim")]
    [SerializeField] private Color dimColor = Color.black;
    [Tooltip("비네팅과 분리된 균일 암전입니다. 값이 낮을수록 더 투명합니다.")]
    [SerializeField, Range(0f, 1f)] private float dimAlpha = 0.24f;
    [SerializeField, Min(0.1f)] private float focusRadiusWorld = 1.78f;
    [Tooltip("1은 원입니다. Combat 기본은 0.72로 위아래가 확실히 눌린 탑다운용 타원입니다.")]
    [SerializeField, Range(0.55f, 1f)] private float verticalRatio = 0.72f;
    [SerializeField, Range(0.001f, 0.10f)] private float feather = 0.040f;
    [SerializeField, Min(0.1f)] private float fadeSharpness = 8f;

    [Header("Focus Motion")]
    [Tooltip("Focus hole은 Player보다 약간 늦게 따라옵니다.")]
    [SerializeField, Min(0.1f)] private float followSharpness = 5.4f;
    [SerializeField, Min(0f)] private float maxLagWorld = 0.42f;
    [Tooltip("가상 광원이 기울수록 타원이 이동 방향으로 더 길어집니다.")]
    [SerializeField, Range(0f, 0.45f)] private float maxDirectionalElongation = 0.28f;

    private Canvas overlayCanvas;
    private Image overlayImage;
    private Material runtimeMaterial;

    private float currentBlend;
    private Vector3 currentFocusWorld;
    private bool focusInitialized;

    public static BattleCombatFocusMaskController Instance => instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallCurrentScene()
    {
        EnsureInstalled();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.IsValid() && scene.isLoaded)
            EnsureInstalled();
    }

    private static void EnsureInstalled()
    {
        if (FindFirstObjectByType<BattleCombatFocusMaskController>(FindObjectsInactive.Include) != null)
            return;

        BattleRunManager run =
            FindFirstObjectByType<BattleRunManager>(FindObjectsInactive.Include);
        if (run != null)
        {
            run.gameObject.AddComponent<BattleCombatFocusMaskController>();
            return;
        }

        BattleSceneManager manager =
            FindFirstObjectByType<BattleSceneManager>(FindObjectsInactive.Include);
        if (manager != null)
            manager.gameObject.AddComponent<BattleCombatFocusMaskController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ResolveReferences();
        EnsureOverlay();
        ApplyImmediate(0f);
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureOverlay();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        EnsureOverlay();

        bool combat = IsCombat();
        bool hasPlayer =
            player != null &&
            player.IsAlive &&
            player.gameObject.activeInHierarchy;

        float target = combat && hasPlayer ? 1f : 0f;
        float t = 1f - Mathf.Exp(
            -Mathf.Max(0.1f, fadeSharpness) *
            Time.unscaledDeltaTime);

        currentBlend = Mathf.Lerp(
            currentBlend,
            target,
            t);

        if (Mathf.Abs(currentBlend - target) < 0.001f)
            currentBlend = target;

        if (!hasPlayer)
        {
            focusInitialized = false;
            ApplyVisual(
                Camera.main,
                Vector3.zero,
                Vector2.right,
                0f);
            return;
        }

        Vector3 targetWorld = ResolvePlayerVisualCenter();
        currentFocusWorld = ResolveFocusWorld(
            targetWorld,
            Time.unscaledDeltaTime);

        Vector2 directionWorld = Vector2.right;
        float tilt01 = 0f;
        BattleSpotlightBeamDirectionController.TryGetCombatProjection(
            out directionWorld,
            out tilt01);

        ApplyVisual(
            Camera.main,
            currentFocusWorld,
            directionWorld,
            tilt01);

        if (!combat && currentBlend <= 0.001f)
            focusInitialized = false;
    }

    private void OnDisable()
    {
        focusInitialized = false;
        ApplyImmediate(0f);
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);

        if (instance == this)
            instance = null;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
        {
            runManager = GetComponent<BattleRunManager>();
            if (runManager == null)
                runManager = FindFirstObjectByType<BattleRunManager>();
        }

        if (stageFlow == null)
        {
            stageFlow = BattleStageTransitionController.Instance != null
                ? BattleStageTransitionController.Instance
                : FindFirstObjectByType<BattleStageTransitionController>();
        }

        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
    }

    private bool IsCombat()
    {
        bool stageCombat =
            stageFlow != null &&
            stageFlow.IsCombatPhase;

        bool runCombat =
            runManager != null &&
            runManager.RunActive &&
            runManager.State == BattleRunState.Combat;

        return stageCombat || runCombat;
    }

    private Vector3 ResolvePlayerVisualCenter()
    {
        if (player == null)
            return Vector3.zero;

        SpriteRenderer[] renderers =
            player.GetComponentsInChildren<SpriteRenderer>(true);

        SpriteRenderer best = null;
        float bestArea = -1f;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null ||
                renderer.name == BattleCharacterLightVisual.KeyRendererName ||
                renderer.name == BattleCharacterLightVisual.PoolRendererName ||
                renderer.name == BattleCharacterLightVisual.GlowRendererName)
            {
                continue;
            }

            float area =
                Mathf.Abs(
                    renderer.bounds.size.x *
                    renderer.bounds.size.y);

            if (area <= bestArea)
                continue;

            bestArea = area;
            best = renderer;
        }

        return best != null
            ? best.bounds.center
            : player.transform.position;
    }

    private Vector3 ResolveFocusWorld(
        Vector3 targetWorld,
        float deltaTime)
    {
        if (!focusInitialized)
        {
            currentFocusWorld = targetWorld;
            focusInitialized = true;
            return currentFocusWorld;
        }

        float followT =
            1f -
            Mathf.Exp(
                -Mathf.Max(0.1f, followSharpness) *
                Mathf.Max(0f, deltaTime));

        Vector3 next =
            Vector3.Lerp(
                currentFocusWorld,
                targetWorld,
                followT);

        Vector3 lag = next - targetWorld;
        float maxLag = Mathf.Max(0f, maxLagWorld);

        if (maxLag > 0f && lag.magnitude > maxLag)
        {
            next =
                targetWorld +
                lag.normalized *
                maxLag;
        }

        return next;
    }

    private void ApplyVisual(
        Camera camera,
        Vector3 focusWorld,
        Vector2 directionWorld,
        float tilt01)
    {
        if (overlayImage == null || runtimeMaterial == null)
            return;

        if (camera == null ||
            currentBlend <= 0.0001f ||
            !TryProjectWorldPoint(
                camera,
                focusWorld,
                focusRadiusWorld,
                out Vector2 centerUv,
                out float radiusUv))
        {
            runtimeMaterial.SetFloat("_Presentation", 0f);
            overlayImage.enabled = false;
            return;
        }

        float rawAngle = ResolveScreenDirectionAngle(
            camera,
            focusWorld,
            directionWorld);

        // Moderate rig tilt should already read clearly on screen.
        // sqrt boosts the middle of the response without making the maximum unstable.
        float visualTilt =
            Mathf.Sqrt(
                Mathf.Clamp01(tilt01));

        // At rest the footprint must always read as a horizontally flattened top-down ellipse.
        // Only rotate toward the movement/light direction once the fake-3D rig actually tilts.
        float angle =
            rawAngle *
            Mathf.SmoothStep(
                0f,
                1f,
                visualTilt);

        float elongation =
            1f +
            visualTilt *
            Mathf.Max(0f, maxDirectionalElongation);

        float dynamicVerticalRatio =
            Mathf.Clamp(
                verticalRatio *
                Mathf.Lerp(
                    1f,
                    0.90f,
                    visualTilt),
                0.55f,
                1f);

        runtimeMaterial.SetColor("_MaskColor", dimColor);
        runtimeMaterial.SetFloat("_Presentation", currentBlend);
        runtimeMaterial.SetFloat("_DimAlpha", Mathf.Clamp01(dimAlpha));
        runtimeMaterial.SetVector(
            "_Center",
            new Vector4(centerUv.x, centerUv.y, 0f, 0f));
        runtimeMaterial.SetFloat("_Radius", radiusUv);
        runtimeMaterial.SetFloat(
            "_VerticalRatio",
            dynamicVerticalRatio);
        runtimeMaterial.SetFloat(
            "_Elongation",
            Mathf.Clamp(elongation, 1f, 1.5f));
        runtimeMaterial.SetFloat("_RotationRadians", angle);
        runtimeMaterial.SetFloat(
            "_Feather",
            Mathf.Max(0.0001f, feather));

        overlayImage.enabled = true;
    }

    private static float ResolveScreenDirectionAngle(
        Camera camera,
        Vector3 centerWorld,
        Vector2 directionWorld)
    {
        if (camera == null ||
            directionWorld.sqrMagnitude < 0.0001f)
        {
            return 0f;
        }

        Vector3 centerScreen =
            camera.WorldToScreenPoint(centerWorld);
        Vector3 directionScreen =
            camera.WorldToScreenPoint(
                centerWorld +
                (Vector3)directionWorld.normalized);

        Vector2 screenDelta =
            new(
                directionScreen.x - centerScreen.x,
                directionScreen.y - centerScreen.y);

        if (screenDelta.sqrMagnitude < 0.0001f)
            return 0f;

        return Mathf.Atan2(
            screenDelta.y,
            screenDelta.x);
    }

    private static bool TryProjectWorldPoint(
        Camera camera,
        Vector3 worldPoint,
        float radiusWorld,
        out Vector2 centerUv,
        out float radiusUv)
    {
        centerUv = new Vector2(0.5f, 0.5f);
        radiusUv = 0f;

        if (camera == null)
            return false;

        Vector3 centerScreen =
            camera.WorldToScreenPoint(worldPoint);
        if (centerScreen.z <= 0f)
            return false;

        Vector3 edgeWorld =
            worldPoint +
            camera.transform.right *
            Mathf.Max(0.01f, radiusWorld);

        Vector3 edgeScreen =
            camera.WorldToScreenPoint(edgeWorld);

        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);

        centerUv =
            new Vector2(
                Mathf.Clamp01(centerScreen.x / width),
                Mathf.Clamp01(centerScreen.y / height));

        radiusUv =
            Mathf.Abs(
                edgeScreen.x -
                centerScreen.x) /
            height;

        radiusUv =
            Mathf.Max(
                0.0001f,
                radiusUv);

        return true;
    }

    private void EnsureOverlay()
    {
        if (overlayCanvas == null)
        {
            GameObject canvasObject =
                new("BattleCombatFocusCanvas");

            canvasObject.transform.SetParent(
                transform,
                false);

            overlayCanvas =
                canvasObject.AddComponent<Canvas>();

            overlayCanvas.renderMode =
                RenderMode.ScreenSpaceOverlay;
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder =
                OverlaySortingOrder;

            CanvasScaler scaler =
                canvasObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution =
                new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject imageObject =
                new(
                    "BattleCombatFocusMask",
                    typeof(RectTransform));

            imageObject.transform.SetParent(
                canvasObject.transform,
                false);

            RectTransform rect =
                imageObject.GetComponent<RectTransform>();

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            overlayImage =
                imageObject.AddComponent<Image>();

            overlayImage.raycastTarget = false;
            overlayImage.color = Color.white;
        }

        if (runtimeMaterial == null)
        {
            Shader shader =
                Shader.Find(ShaderName);

            if (shader == null)
                shader =
                    Resources.Load<Shader>(
                        "BattleCombatFocusMask");

            if (shader != null)
            {
                runtimeMaterial =
                    new Material(shader)
                    {
                        name =
                            "BattleCombatFocusMask_Runtime",
                        hideFlags =
                            HideFlags.HideAndDontSave
                    };
            }
        }

        if (overlayImage != null)
            overlayImage.material = runtimeMaterial;
    }

    private void ApplyImmediate(float value)
    {
        currentBlend = Mathf.Clamp01(value);

        if (runtimeMaterial != null)
            runtimeMaterial.SetFloat(
                "_Presentation",
                currentBlend);

        if (overlayImage != null)
            overlayImage.enabled =
                currentBlend > 0.001f;
    }
}

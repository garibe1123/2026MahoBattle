using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Owns only the dark pre-combat stage language.
///
/// State rules:
/// - None / EnteringNode / BuildingRoom: dark stage + Player focus + floor spotlight.
/// - Combat: all Player-specific spotlights are removed; the bright world light / Volume own the image.
/// - Reward / SelectingNode: BattleShowFocusController and the existing show lighting remain authoritative.
///
/// It also makes sure a scene Global Light2D cannot remain disabled during battle setup.
/// </summary>
[DefaultExecutionOrder(-3200)]
[DisallowMultipleComponent]
public sealed class BattlePlayerStageLightingController : MonoBehaviour
{
    private const string FocusShaderName = "UI/BattleShowFocusMask";
    private const string AdditiveLightShaderName = "Sprites/BattleSoftKeyLight";
    private const string FloorSpotlightName = "BattlePlayerFloorSpotlight";

    private static BattlePlayerStageLightingController instance;
    private static Sprite sharedFloorSprite;
    private static Material sharedFloorMaterial;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private PlayerController player;

    [Header("Global Light Safety")]
    [Tooltip("Battle scene의 Global Light2D가 비활성 상태로 남아 있으면 다시 활성화합니다.")]
    [SerializeField] private bool forceGlobalLightActive = true;
    [SerializeField, Min(0.05f)] private float globalLightProbeInterval = 0.25f;

    [Header("Pre-Combat Player Floor Spotlight")]
    [SerializeField] private Color floorSpotlightColor = new(1f, 0.92f, 0.76f, 1f);
    [Tooltip("전투가 실제로 시작되기 전 어두운 Stage에서만 사용합니다.")]
    [SerializeField, Range(0f, 1f)] private float preCombatFloorAlpha = 0.46f;
    [SerializeField, Min(0.1f)] private float floorWidthMultiplier = 1.85f;
    [SerializeField, Range(0.05f, 0.55f)] private float floorHeightRatio = 0.20f;

    [Header("Pre-Combat Dark Stage")]
    [Tooltip("BattleHUD보다 뒤, 기존 Show Focus보다 살짝 뒤에 둡니다.")]
    [SerializeField] private int overlaySortingOrder = 445;
    [SerializeField] private Color dimColor = Color.black;
    [SerializeField, Range(0f, 1f)] private float nearDimAlpha = 0.68f;
    [SerializeField, Range(0f, 1f)] private float farDimAlpha = 0.97f;
    [SerializeField, Range(0.05f, 1.5f)] private float dimFalloffRadius = 0.56f;
    [SerializeField, Min(0.1f)] private float playerFocusRadiusWorld = 1.62f;
    [SerializeField, Range(0.2f, 1f)] private float characterVerticalRatio = 0.46f;
    [SerializeField, Range(0f, 1f)] private float characterLowerOffset = 0.22f;
    [SerializeField, Range(0.001f, 0.08f)] private float characterFeather = 0.018f;
    [SerializeField, Min(0.01f)] private float preCombatFadeInDuration = 0.14f;
    [SerializeField, Min(0.01f)] private float combatRevealDuration = 0.22f;

    private Canvas overlayCanvas;
    private Image overlayImage;
    private Material overlayMaterial;

    private SpriteRenderer floorRenderer;
    private Transform floorTransform;
    private PlayerController boundPlayer;

    private float currentStageDim;
    private float nextGlobalLightProbe;

    public static BattlePlayerStageLightingController Instance => instance;

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

        if (!battleScene || Object.FindFirstObjectByType<BattlePlayerStageLightingController>() != null)
            return;

        GameObject host = new("BattlePlayerStageLightingRuntime");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<BattlePlayerStageLightingController>();
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
        EnsureOverlay();
        EnsureFloorSpotlight();
        ApplyOverlayImmediate(0f);
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureOverlay();
        EnsureFloorSpotlight();
    }

    private void OnDisable()
    {
        SetExistingPlayerPresentationLightsSuppressed(false);
        ApplyOverlayImmediate(0f);

        if (floorRenderer != null)
            floorRenderer.enabled = false;
    }

    private void OnDestroy()
    {
        if (overlayMaterial != null)
            Destroy(overlayMaterial);

        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        ResolveReferences();
        EnsureOverlay();
        EnsureFloorSpotlight();

        if (forceGlobalLightActive && Time.unscaledTime >= nextGlobalLightProbe)
        {
            nextGlobalLightProbe = Time.unscaledTime + Mathf.Max(0.05f, globalLightProbeInterval);
            EnsureGlobalLightIsActive();
        }

        bool preCombat = IsPreCombatStage();
        bool combat = IsCombat();

        float stageTarget = preCombat ? 1f : 0f;
        float stageDuration = stageTarget > currentStageDim
            ? Mathf.Max(0.01f, preCombatFadeInDuration)
            : Mathf.Max(0.01f, combatRevealDuration);

        currentStageDim = Mathf.MoveTowards(
            currentStageDim,
            stageTarget,
            Time.unscaledDeltaTime / stageDuration);

        SetExistingPlayerPresentationLightsSuppressed(preCombat || combat);
    }

    private void LateUpdate()
    {
        ResolveReferences();
        EnsureFloorSpotlight();
        UpdateFloorSpotlight();
        UpdatePreCombatOverlay();

        SetExistingPlayerPresentationLightsSuppressed(IsPreCombatStage() || IsCombat());
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
    }

    private bool IsPreCombatStage()
    {
        if (runManager == null)
            return false;

        BattleRunState state = runManager.State;
        if (state == BattleRunState.None)
            return true;
        if (!runManager.RunActive)
            return false;

        return state == BattleRunState.EnteringNode ||
               state == BattleRunState.BuildingRoom;
    }

    private bool IsCombat()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Combat;
    }

    private void EnsureGlobalLightIsActive()
    {
        Light2D[] lights = FindObjectsByType<Light2D>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        Light2D preferred = null;
        Light2D fallback = null;

        for (int i = 0; i < lights.Length; i++)
        {
            Light2D light = lights[i];
            if (light == null || light.lightType != Light2D.LightType.Global)
                continue;
            if (light.gameObject.scene != gameObject.scene)
                continue;

            fallback ??= light;
            if (light.name == "BattleWorldGlobalLight")
            {
                preferred = light;
                break;
            }
            if (preferred == null && light.gameObject.activeInHierarchy)
                preferred = light;
        }

        Light2D target = preferred != null ? preferred : fallback;
        if (target == null)
            return;

        if (!target.gameObject.activeSelf)
            target.gameObject.SetActive(true);
        target.enabled = true;
    }

    private void EnsureFloorSpotlight()
    {
        if (player == null)
            return;

        if (boundPlayer != player)
        {
            if (floorRenderer != null)
                Destroy(floorRenderer.gameObject);
            floorRenderer = null;
            floorTransform = null;
            boundPlayer = player;
        }

        if (floorRenderer != null)
            return;

        Transform existing = player.transform.Find(FloorSpotlightName);
        GameObject floorObject = existing != null ? existing.gameObject : new GameObject(FloorSpotlightName);
        floorObject.transform.SetParent(player.transform, true);
        floorTransform = floorObject.transform;

        floorRenderer = floorObject.GetComponent<SpriteRenderer>();
        if (floorRenderer == null)
            floorRenderer = floorObject.AddComponent<SpriteRenderer>();

        floorRenderer.sprite = GetOrCreateFloorSprite();
        floorRenderer.sharedMaterial = GetOrCreateFloorMaterial();
        floorRenderer.enabled = false;
    }

    private void UpdateFloorSpotlight()
    {
        if (floorRenderer == null || floorTransform == null || player == null)
            return;

        float alpha = Mathf.Clamp01(preCombatFloorAlpha * currentStageDim);
        if (alpha <= 0.001f)
        {
            floorRenderer.enabled = false;
            return;
        }

        SpriteRenderer targetRenderer = ResolvePrimarySpriteRenderer(player.gameObject);
        if (targetRenderer == null || !targetRenderer.gameObject.activeInHierarchy)
        {
            floorRenderer.enabled = false;
            return;
        }

        Bounds bounds = targetRenderer.bounds;
        float spriteWidth = Mathf.Max(0.2f, bounds.size.x);
        float spriteHeight = Mathf.Max(0.2f, bounds.size.y);
        float poolWidth = Mathf.Max(0.85f, spriteWidth * floorWidthMultiplier);
        float poolHeight = Mathf.Max(0.13f, poolWidth * floorHeightRatio);
        float poolY = bounds.min.y + Mathf.Max(0.015f, spriteHeight * 0.025f);

        floorTransform.position = new Vector3(bounds.center.x, poolY, targetRenderer.transform.position.z);
        floorTransform.rotation = Quaternion.identity;

        ApplyWorldSizeToChild(
            floorTransform,
            floorRenderer.sprite,
            poolWidth,
            poolHeight,
            player.transform.lossyScale);

        floorRenderer.sortingLayerID = targetRenderer.sortingLayerID;
        floorRenderer.sortingOrder = targetRenderer.sortingOrder - 1;
        floorRenderer.sharedMaterial = GetOrCreateFloorMaterial();

        Color color = floorSpotlightColor;
        color.a = alpha;
        floorRenderer.color = color;
        floorRenderer.enabled = true;
    }

    private void SetExistingPlayerPresentationLightsSuppressed(bool suppressed)
    {
        if (player == null)
            return;

        SetChildActiveIfFound(player.transform, BattleCharacterLightVisual.KeyRendererName, !suppressed);
        SetChildActiveIfFound(player.transform, BattleCharacterLightVisual.PoolRendererName, !suppressed);
        SetChildActiveIfFound(player.transform, BattleCharacterLightVisual.GlowRendererName, !suppressed);
    }

    private static void SetChildActiveIfFound(Transform root, string childName, bool active)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return;

        Transform child = root.Find(childName);
        if (child != null && child.gameObject.activeSelf != active)
            child.gameObject.SetActive(active);
    }

    private void EnsureOverlay()
    {
        if (overlayCanvas != null && overlayImage != null && overlayMaterial != null)
            return;

        Shader shader = Shader.Find(FocusShaderName);
        if (shader == null)
            shader = Resources.Load<Shader>("BattleShowFocusMask");
        if (shader == null)
        {
            Debug.LogError($"[BattlePlayerStageLightingController] Shader '{FocusShaderName}'를 찾지 못했습니다.", this);
            enabled = false;
            return;
        }

        if (overlayCanvas == null)
        {
            GameObject canvasObject = new("BattlePreCombatFocusCanvas");
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
            GameObject imageObject = new("PreCombatFocusMask");
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

        if (overlayMaterial == null)
        {
            overlayMaterial = new Material(shader)
            {
                name = "BattlePreCombatFocusMask_Runtime",
                hideFlags = HideFlags.HideAndDontSave
            };
            overlayImage.material = overlayMaterial;
        }
    }

    private void UpdatePreCombatOverlay()
    {
        if (overlayMaterial == null || overlayImage == null)
            return;

        Camera camera = Camera.main;
        if (camera == null)
        {
            overlayImage.enabled = false;
            return;
        }

        Vector2 playerUv = new(0.5f, 0.5f);
        float playerRadiusUv = 0f;
        bool playerVisible = TryProjectPlayer(
            camera,
            player != null && player.IsAlive ? player.transform : null,
            playerFocusRadiusWorld,
            out playerUv,
            out playerRadiusUv);

        overlayMaterial.SetColor("_MaskColor", dimColor);
        overlayMaterial.SetFloat("_Presentation", currentStageDim);
        overlayMaterial.SetVector("_DimCenter", new Vector4(playerUv.x, playerUv.y, 0f, 0f));
        overlayMaterial.SetFloat("_NearDimAlpha", nearDimAlpha);
        overlayMaterial.SetFloat("_FarDimAlpha", farDimAlpha);
        overlayMaterial.SetFloat("_DimRadius", Mathf.Max(0.001f, dimFalloffRadius));
        overlayMaterial.SetVector("_PlayerCenter", new Vector4(playerUv.x, playerUv.y, 0f, 0f));
        overlayMaterial.SetFloat("_PlayerRadius", playerRadiusUv);
        overlayMaterial.SetFloat("_PlayerStrength", playerVisible ? currentStageDim : 0f);
        overlayMaterial.SetFloat("_PresenterStrength", 0f);
        overlayMaterial.SetFloat("_ScreenStrength", 0f);
        overlayMaterial.SetFloat("_CharacterVerticalRatio", Mathf.Clamp(characterVerticalRatio, 0.2f, 1f));
        overlayMaterial.SetFloat("_CharacterLowerOffset", Mathf.Clamp01(characterLowerOffset));
        overlayMaterial.SetFloat("_CircleFeather", Mathf.Max(0.0001f, characterFeather));
        overlayMaterial.SetFloat("_RectFeather", 0.0035f);

        overlayImage.enabled = currentStageDim > 0.0001f;
    }

    private bool TryProjectPlayer(
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
        centerUv = new Vector2(Mathf.Clamp01(centerScreen.x / width), Mathf.Clamp01(centerScreen.y / height));
        radiusUv = Mathf.Max(0.0001f, Mathf.Abs(edgeScreen.x - centerScreen.x) / height);
        return true;
    }

    private static SpriteRenderer ResolvePrimarySpriteRenderer(GameObject target)
    {
        if (target == null)
            return null;

        SpriteRenderer direct = target.GetComponent<SpriteRenderer>();
        if (IsUsableRenderer(direct))
            return direct;

        SpriteRenderer[] renderers = target.GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer best = null;
        float bestArea = -1f;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (!IsUsableRenderer(renderer))
                continue;

            float area = Mathf.Abs(renderer.bounds.size.x * renderer.bounds.size.y);
            if (area <= bestArea)
                continue;

            best = renderer;
            bestArea = area;
        }

        return best;
    }

    private static bool IsUsableRenderer(SpriteRenderer renderer)
    {
        if (renderer == null)
            return false;

        string objectName = renderer.name;
        return objectName != FloorSpotlightName &&
               objectName != BattleCharacterLightVisual.KeyRendererName &&
               objectName != BattleCharacterLightVisual.PoolRendererName &&
               objectName != BattleCharacterLightVisual.GlowRendererName;
    }

    private static void ApplyWorldSizeToChild(
        Transform child,
        Sprite sprite,
        float width,
        float height,
        Vector3 parentWorldScale)
    {
        if (child == null || sprite == null)
            return;

        float inverseX = Mathf.Abs(parentWorldScale.x) > 0.0001f ? 1f / Mathf.Abs(parentWorldScale.x) : 1f;
        float inverseY = Mathf.Abs(parentWorldScale.y) > 0.0001f ? 1f / Mathf.Abs(parentWorldScale.y) : 1f;
        Vector2 spriteSize = sprite.bounds.size;
        float sourceWidth = Mathf.Max(0.0001f, spriteSize.x);
        float sourceHeight = Mathf.Max(0.0001f, spriteSize.y);

        child.localScale = new Vector3(
            width / sourceWidth * inverseX,
            height / sourceHeight * inverseY,
            1f);
    }

    private void ApplyOverlayImmediate(float value)
    {
        currentStageDim = Mathf.Clamp01(value);

        if (overlayMaterial != null)
        {
            overlayMaterial.SetFloat("_Presentation", currentStageDim);
            overlayMaterial.SetFloat("_PlayerStrength", currentStageDim);
            overlayMaterial.SetFloat("_PresenterStrength", 0f);
            overlayMaterial.SetFloat("_ScreenStrength", 0f);
        }

        if (overlayImage != null)
            overlayImage.enabled = currentStageDim > 0.0001f;
    }

    private static Material GetOrCreateFloorMaterial()
    {
        if (sharedFloorMaterial != null)
            return sharedFloorMaterial;

        Shader shader = Shader.Find(AdditiveLightShaderName);
        if (shader == null)
            shader = Resources.Load<Shader>("BattleSoftKeyLight");
        if (shader == null)
            return null;

        sharedFloorMaterial = new Material(shader)
        {
            name = "BattlePlayerFloorSpotlight_Runtime",
            hideFlags = HideFlags.HideAndDontSave
        };
        return sharedFloorMaterial;
    }

    private static Sprite GetOrCreateFloorSprite()
    {
        if (sharedFloorSprite != null)
            return sharedFloorSprite;

        const int width = 96;
        const int height = 32;
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "BattlePlayerFloorSpotlightTexture_Runtime",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = ((y + 0.5f) / height) * 2f - 1f;
            for (int x = 0; x < width; x++)
            {
                float nx = ((x + 0.5f) / width) * 2f - 1f;
                float distance = Mathf.Sqrt(nx * nx + ny * ny);
                float edge = Mathf.Clamp01(1f - distance);
                float alpha = Mathf.SmoothStep(0f, 1f, edge);
                alpha = alpha * alpha * (0.84f + 0.16f * edge);
                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        sharedFloorSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            32f,
            0,
            SpriteMeshType.FullRect);
        sharedFloorSprite.name = "BattlePlayerFloorSpotlightSprite_Runtime";
        sharedFloorSprite.hideFlags = HideFlags.HideAndDontSave;
        return sharedFloorSprite;
    }
}

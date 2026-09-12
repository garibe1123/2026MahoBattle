using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Owns only the dark pre-combat stage language.
///
/// State rules:
/// - None / EnteringNode / BuildingRoom: dark stage + the same unified Player spotlight rig used by Show.
/// - Combat: all Player-specific spotlights are removed; the bright world light / Volume own the image.
/// - Reward / SelectingNode: BattleInverseWorldSpotlightController drives the same rig from World Light intensity.
///
/// There is intentionally no separate pre-combat floor-only light anymore. Beam and floor pool are always
/// BattleCharacterLightVisual children and therefore open/close together.
/// </summary>
[DefaultExecutionOrder(-3200)]
[DisallowMultipleComponent]
public sealed class BattlePlayerStageLightingController : MonoBehaviour
{
    private const string FocusShaderName = "UI/BattleShowFocusMask";
    private const string LegacyFloorSpotlightName = "BattlePlayerFloorSpotlight";

    private static BattlePlayerStageLightingController instance;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private PlayerController player;

    [Header("Global Light Safety")]
    [Tooltip("Battle scene의 Global Light2D가 비활성 상태로 남아 있으면 다시 활성화합니다.")]
    [SerializeField] private bool forceGlobalLightActive = true;
    [SerializeField, Min(0.05f)] private float globalLightProbeInterval = 0.25f;

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

    private BattleCharacterLightVisual playerSpotlightVisual;
    private PlayerController spotlightBoundPlayer;

    private float currentStageDim;
    private float nextGlobalLightProbe;

    public static BattlePlayerStageLightingController Instance => instance;

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
        DisableLegacyFloorSpotlight();
        ApplyOverlayImmediate(0f);
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureOverlay();
        DisableLegacyFloorSpotlight();
    }

    private void OnDisable()
    {
        SetExistingPlayerPresentationLightsSuppressed(false);
        DisableLegacyFloorSpotlight();
        ApplyOverlayImmediate(0f);
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
        DisableLegacyFloorSpotlight();

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

        // Pre-combat now uses the exact same Beam + Pool rig as Reward/Map.
        if (preCombat)
        {
            SetExistingPlayerPresentationLightsSuppressed(false);
            DriveUnifiedPlayerSpotlight(currentStageDim);
        }
        else if (combat)
        {
            SetExistingPlayerPresentationLightsSuppressed(true);
        }
    }

    private void LateUpdate()
    {
        ResolveReferences();
        DisableLegacyFloorSpotlight();
        UpdatePreCombatOverlay();

        bool preCombat = IsPreCombatStage();
        bool combat = IsCombat();

        if (preCombat)
        {
            SetExistingPlayerPresentationLightsSuppressed(false);
            DriveUnifiedPlayerSpotlight(currentStageDim);
        }
        else if (combat)
        {
            SetExistingPlayerPresentationLightsSuppressed(true);
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();

        if (player == null)
            player = FindFirstObjectByType<PlayerController>();

        if (spotlightBoundPlayer != player)
        {
            spotlightBoundPlayer = player;
            playerSpotlightVisual = null;
        }
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

    private void DriveUnifiedPlayerSpotlight(float strength)
    {
        if (player == null || !player.IsAlive || !player.gameObject.activeInHierarchy)
            return;

        if (playerSpotlightVisual == null)
            playerSpotlightVisual = player.GetComponent<BattleCharacterLightVisual>();

        // BattleFieldCinematicDirector owns configuration/profile values and creates this component.
        // Until it is bound, do not create a second partially-configured light rig here.
        if (playerSpotlightVisual == null)
            return;

        playerSpotlightVisual.SetTarget(true, Mathf.Clamp01(strength));
    }

    private void DisableLegacyFloorSpotlight()
    {
        if (player == null)
            return;

        Transform legacy = FindRecursive(player.transform, LegacyFloorSpotlightName);
        if (legacy == null)
            return;

        SpriteRenderer renderer = legacy.GetComponent<SpriteRenderer>();
        if (renderer != null)
            renderer.enabled = false;

        if (legacy.gameObject.activeSelf)
            legacy.gameObject.SetActive(false);
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
        Transform child = FindRecursive(root, childName);
        if (child != null && child.gameObject.activeSelf != active)
            child.gameObject.SetActive(active);
    }

    private static Transform FindRecursive(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        if (root.name == targetName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindRecursive(root.GetChild(i), targetName);
            if (found != null)
                return found;
        }

        return null;
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

        float height = Mathf.Max(1f, Screen.height);
        centerUv = new Vector2(
            Mathf.Clamp01(centerScreen.x / Mathf.Max(1f, Screen.width)),
            Mathf.Clamp01(centerScreen.y / height));
        radiusUv = Mathf.Max(0.0001f, Mathf.Abs(edgeScreen.x - centerScreen.x) / height);
        return true;
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
}

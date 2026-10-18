using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using System.IO;
#endif

/// <summary>
/// Central battle lighting / show presentation director.
///
/// Combat:
/// - Brighter Global Light so the whole arena remains readable.
/// - A strong color-grade carries the Eastward-like cool-shadow / warm-highlight palette.
/// - Player owns the only permanent key spotlight: tall shaft + foot pool + top-light wash.
/// - Enemies stay readable through the brighter world light and use only a weak contact pool.
///
/// Rule Roulette / Reward / Map Selection:
/// - Rule Roulette reuses the same Show world-light target as Map Selection so the field never goes visually dead.
/// - Existing show dim / TV rectangular focus stays authoritative when the TV Show itself is active.
/// - Combat color grading fades almost completely out so the current show look is preserved.
/// </summary>
[DefaultExecutionOrder(-4000)]
[DisallowMultipleComponent]
public sealed class BattleFieldCinematicDirector : MonoBehaviour
{
    private sealed class EnemyLightBinding
    {
        public MonsterController monster;
        public BattleCharacterLightVisual visual;
    }

    private static BattleFieldCinematicDirector instance;

    [Header("References")]
    [SerializeField] private BattleCameraController battleCamera;
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private PlayerController player;
    [SerializeField] private BattleShowWorldSetController showWorldSet;

    [Header("Lighting Profile")]
    [Tooltip("Optional authored fallback. Room-specific profiles can be placed in Resources/BattleLightingProfiles and bind by roomId.")]
    [SerializeField] private BattleLightingProfileSO defaultLightingProfile;

    [Header("Battle Camera Background")]
    [SerializeField] private Color battleBackgroundColor = Color.black;

    [Header("Enemy Scan")]
    [SerializeField, Min(0.05f)] private float enemyLightScanInterval = 0.22f;

    [Header("Combat Start Beat")]
    [SerializeField] private bool focusPlayerOnCombatStart = true;
    [SerializeField, Min(0f)] private float combatStartFocusDuration = 0.58f;
    [SerializeField, Min(0f)] private float combatStartZoom = 5.25f;

    private readonly List<EnemyLightBinding> enemyLightBindings = new();
    private readonly List<Light2D> cachedPointLights = new();
    private readonly List<MapBlock> litRoomBlocks = new();
    private readonly HashSet<int> litRoomSortingLayers = new();

    private Light2D fieldGlobalLight;
    private Light2D cameraFillLight;
    private Camera sceneCamera;
    private bool cameraFillChecked;
    private bool ownsCameraFill;
    private bool warnedCamera3DLight;
    private BattleColorGradingController colorGrading;
    private BattleCharacterLightVisual playerLightVisual;
    private BattleCharacterLightVisual presenterLightVisual;
    private Transform lastPresenterTarget;

    private BattleLightingProfileSO runtimeDefaultProfile;
    private BattleLightingProfileSO activeLightingProfile;
    private BattleLightingProfileSO[] discoveredLightingProfiles;

    private Coroutine bindRoutine;
    private bool subscribed;
    private bool combatLightingRequested;
    private bool showLightingRequested;
    private bool worldLightInitialized;
    private float nextEnemyLightScan;
    private float nextLocalLightScan;
    private float nextSurfaceLightUpdate;
    private float nextLitFaceScan;
    private int remainingLitFaceScans;
    private int combatStartFocusRequest;

    public static BattleFieldCinematicDirector Instance => instance;
    public bool IsFieldLightingActive => fieldGlobalLight != null && fieldGlobalLight.intensity > 0.001f;
    public BattleLightingProfileSO ActiveLightingProfile => activeLightingProfile;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ResolveReferences();
        EnsureLightingProfile();
#if UNITY_EDITOR
        LoadPlayLightingPreset();
#endif
        EnsureWorldLight();
        EnsureColorGrading();
        ConfigureCameraBackground();
        EnsureCameraFillLight();
    }

    private void OnEnable()
    {
        if (bindRoutine == null)
            bindRoutine = StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
            StopCoroutine(bindRoutine);
        bindRoutine = null;

        Unsubscribe();
        ReleaseCombatStartFocus();
        SetCharacterLightsImmediate(false);
#if UNITY_EDITOR
        SavePlayLightingPresetIfDirty(true);
        RestorePlayLightSortingLayers();
#endif
        if (ownsCameraFill && cameraFillLight != null)
        {
            cameraFillLight.intensity = 0f;
            cameraFillLight.enabled = false;
        }
#if UNITY_EDITOR
        else if (cameraFillLight != null)
            RestoreAuthoredCameraLight();
#endif

        if (colorGrading != null)
            colorGrading.SetImmediateWeight(0f);
    }

    private void OnDestroy()
    {
        if (ownsCameraFill && cameraFillLight != null)
            Destroy(cameraFillLight.gameObject);
        if (runtimeDefaultProfile != null)
            Destroy(runtimeDefaultProfile);

        if (instance == this)
            instance = null;
    }

    private IEnumerator BindWhenReady()
    {
        while (enabled)
        {
            ResolveReferences();
            EnsureLightingProfile();
            EnsureWorldLight();
            EnsureColorGrading();
            ConfigureCameraBackground();
            EnsureCameraFillLight();

            if (battleCamera != null && runManager != null && roomManager != null && player != null)
                break;

            yield return null;
        }

        if (!enabled)
        {
            bindRoutine = null;
            yield break;
        }

        Subscribe();
        ApplyRunState(runManager.State);
        EnsurePlayerLightVisual();
        RefreshEnemyLightVisuals(true);
        bindRoutine = null;
    }

    private void ResolveReferences()
    {
        if (battleCamera == null)
            battleCamera = FindFirstObjectByType<BattleCameraController>();
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
        if (showWorldSet == null)
            showWorldSet = FindFirstObjectByType<BattleShowWorldSetController>();
        if (sceneCamera == null)
            sceneCamera = Camera.main;

        if (transform.parent == null)
        {
            BattleSceneManager manager = FindFirstObjectByType<BattleSceneManager>();
            if (manager != null)
                transform.SetParent(manager.transform, true);
        }
    }

    private void EnsureLightingProfile()
    {
        if (runtimeDefaultProfile == null)
            runtimeDefaultProfile = BattleLightingProfileSO.CreateRuntimeDefault();

        if (activeLightingProfile == null)
            activeLightingProfile = defaultLightingProfile != null
                ? defaultLightingProfile
                : runtimeDefaultProfile;

        if (discoveredLightingProfiles == null)
        {
            discoveredLightingProfiles =
                Resources.LoadAll<BattleLightingProfileSO>("BattleLightingProfiles");
        }
    }

    private BattleLightingProfileSO ResolveLightingProfile(RoomDefinitionSO room)
    {
        EnsureLightingProfile();

        if (room != null && discoveredLightingProfiles != null)
        {
            for (int i = 0; i < discoveredLightingProfiles.Length; i++)
            {
                BattleLightingProfileSO profile = discoveredLightingProfiles[i];
                if (profile != null && profile.MatchesRoom(room))
                    return profile;
            }
        }

        return defaultLightingProfile != null
            ? defaultLightingProfile
            : runtimeDefaultProfile;
    }

    private void SetActiveLightingProfile(BattleLightingProfileSO profile)
    {
        if (profile == null)
            profile = runtimeDefaultProfile;

        WorldLight.ApplyInitialProfile(profile);

        if (activeLightingProfile == profile)
        {
            EnsureColorGrading();
            colorGrading?.ApplyProfile(profile);
            return;
        }

        activeLightingProfile = profile;
        worldLightInitialized = fieldGlobalLight != null;

        EnsureColorGrading();
        colorGrading?.ApplyProfile(profile);

        ReconfigureCharacterLights();
    }

    private void ConfigureCameraBackground()
    {
        if (sceneCamera == null)
            sceneCamera = Camera.main;
        if (sceneCamera == null)
            return;

        sceneCamera.clearFlags = CameraClearFlags.SolidColor;
        sceneCamera.backgroundColor = battleBackgroundColor;
    }

    private void EnsureWorldLight()
    {
        if (fieldGlobalLight != null)
            return;

        Light2D[] lights = FindObjectsByType<Light2D>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        Light2D authoredWorldGlobal = null;
        Light2D cameraGlobal = null;

        for (int i = 0; i < lights.Length; i++)
        {
            Light2D candidate = lights[i];
            if (candidate == null ||
                candidate.lightType != Light2D.LightType.Global ||
                candidate.gameObject.scene != gameObject.scene)
                continue;

            if (candidate.name == "BattleWorldGlobalLight")
            {
                fieldGlobalLight = candidate;
                break;
            }

            if (sceneCamera != null &&
                candidate.transform.IsChildOf(sceneCamera.transform))
                cameraGlobal ??= candidate;
            else
                authoredWorldGlobal ??= candidate;
        }

        if (fieldGlobalLight == null)
            fieldGlobalLight = authoredWorldGlobal != null
                ? authoredWorldGlobal : cameraGlobal;

        if (cameraGlobal != null && fieldGlobalLight == cameraGlobal)
        {
            Debug.LogWarning(
                "[BattleLighting] A camera-mounted Global Light2D is world ambience, " +
                "not a local spotlight. The battle lighting profile controls its intensity. " +
                "Use Point Light2D for a localized camera light.", cameraGlobal);
        }

        if (fieldGlobalLight == null)
        {
            GameObject lightObject = new("BattleWorldGlobalLight");
            lightObject.transform.SetParent(transform, false);

            fieldGlobalLight = lightObject.AddComponent<Light2D>();
            fieldGlobalLight.lightType = Light2D.LightType.Global;
            fieldGlobalLight.blendStyleIndex = 0;
        }

        EnsureLightingProfile();

        fieldGlobalLight.color = activeLightingProfile.worldLightColor;

        if (!worldLightInitialized)
        {
            fieldGlobalLight.intensity =
                Mathf.Max(0f, activeLightingProfile.worldLightStartIntensity);
            worldLightInitialized = true;
        }
    }

    /// <summary>
    /// Local Light2D is required by URP's Renderer2D. A regular 3D Light on
    /// Main Camera does not illuminate Sprite-Lit-Default materials.
    /// Respect authored camera Light2D components; install a small optional
    /// warm fill only if the camera has no authored local 2D light.
    /// </summary>
    private void EnsureCameraFillLight()
    {
        if (sceneCamera == null || activeLightingProfile == null)
            return;

        if (!warnedCamera3DLight && sceneCamera.GetComponentInChildren<Light>(true) != null)
        {
            warnedCamera3DLight = true;
            Debug.LogWarning(
                "[BattleLighting] Main Camera contains a 3D Light. A 2D Renderer " +
                "does not illuminate sprites with this component. Use Light2D (Point) " +
                "and Sprite-Lit-Default on receiving sprites instead.", sceneCamera);
        }

        if (cameraFillChecked)
            return;
        if (!activeLightingProfile.autoCameraCombatFill)
            return;

        cameraFillChecked = true;
        Light2D[] existingLights = sceneCamera.GetComponentsInChildren<Light2D>(true);
        for (int i = 0; i < existingLights.Length; i++)
        {
            Light2D existing = existingLights[i];
            if (existing == null || existing.lightType != Light2D.LightType.Point)
                continue;

            // In Play Mode the tuning panel can preview the authored light.
            // Original settings are restored when combat ends or Play stops.
            cameraFillLight = existing;
            ownsCameraFill = false;
#if UNITY_EDITOR
            authoredCameraIntensity = existing.intensity;
            authoredCameraRadius = existing.pointLightOuterRadius;
            authoredCameraColor = existing.color;
            authoredCameraEnabled = existing.enabled;
            authoredCameraCaptured = true;
#endif
            return;
        }

        GameObject fillObject = new("BattleCameraWarmFill2D");
        fillObject.transform.SetParent(sceneCamera.transform, false);
        // The orthographic camera sits at world z=-10. Keep the actual 2D light
        // on the floor/sprite z plane; follow only the camera's XY position.
        fillObject.transform.localPosition = new Vector3(0f, 0f, 10f);
        cameraFillLight = fillObject.AddComponent<Light2D>();
        cameraFillLight.lightType = Light2D.LightType.Point;
        cameraFillLight.blendStyleIndex = 0;
        cameraFillLight.pointLightInnerAngle = 360f;
        cameraFillLight.pointLightOuterAngle = 360f;
        cameraFillLight.shadowsEnabled = false;
        cameraFillLight.enabled = false;
        ownsCameraFill = true;
    }

    private void UpdateCameraFillLight()
    {
        if (cameraFillLight == null || activeLightingProfile == null)
            return;

        BattleLightingProfileSO profile = activeLightingProfile;
        bool combat = combatLightingRequested && !showLightingRequested;
        if (!ownsCameraFill && !combat)
        {
#if UNITY_EDITOR
            RestoreAuthoredCameraLight();
#endif
            return;
        }
        float target = profile.autoCameraCombatFill && combat
            ? Mathf.Max(0f, profile.cameraCombatFillIntensity) : 0f;
        float radius = profile.cameraCombatFillRadius;
#if UNITY_EDITOR
        if (playLighting != null && combat)
        {
            target = playLighting.cameraIntensity;
            radius = playLighting.cameraRadius;
        }
#endif
        cameraFillLight.color = profile.cameraCombatFillColor;
        cameraFillLight.pointLightInnerRadius = Mathf.Max(0.1f, radius * 0.22f);
        cameraFillLight.pointLightOuterRadius = Mathf.Max(
            cameraFillLight.pointLightInnerRadius + 0.05f, radius);
        cameraFillLight.intensity = Mathf.MoveTowards(
            cameraFillLight.intensity, target, 3f * Time.unscaledDeltaTime);
        cameraFillLight.enabled = cameraFillLight.intensity > 0.001f;
    }

    private void EnsureColorGrading()
    {
        EnsureLightingProfile();

        if (colorGrading == null)
        {
            colorGrading = GetComponent<BattleColorGradingController>();
            if (colorGrading == null)
                colorGrading = gameObject.AddComponent<BattleColorGradingController>();
        }

        colorGrading.Configure(sceneCamera, activeLightingProfile);
        colorGrading.SetPresentationMode(combatLightingRequested, showLightingRequested);
    }

    private void Subscribe()
    {
        if (subscribed || runManager == null || roomManager == null)
            return;

        runManager.StateChanged += HandleRunStateChanged;
        roomManager.RoomCombatStarted += HandleRoomCombatStarted;
        roomManager.RoomCombatCleared += HandleRoomCombatCleared;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (runManager != null)
            runManager.StateChanged -= HandleRunStateChanged;

        if (roomManager != null)
        {
            roomManager.RoomCombatStarted -= HandleRoomCombatStarted;
            roomManager.RoomCombatCleared -= HandleRoomCombatCleared;
        }

        subscribed = false;
    }

    private void HandleRunStateChanged(BattleRunState state)
    {
        ApplyRunState(state);
    }

    private void ApplyRunState(BattleRunState state)
    {
        bool runActive = runManager != null && runManager.RunActive;
        combatLightingRequested = runActive && state == BattleRunState.Combat;
        if (combatLightingRequested)
            remainingLitFaceScans = 3;
        showLightingRequested =
            runActive &&
            (state == BattleRunState.RuleRoulette ||
             state == BattleRunState.Reward ||
             state == BattleRunState.SelectingNode);

        EnsureColorGrading();
        colorGrading.SetPresentationMode(combatLightingRequested, showLightingRequested);

        if (!combatLightingRequested)
        {
            SetEnemyLightsTarget(false);
            ReleaseCombatStartFocus();
            battleCamera?.ReleaseAllFieldFocus();
        }
        else
        {
            RefreshEnemyLightVisuals(true);
        }
    }

    private void HandleRoomCombatStarted(RoomDefinitionSO room)
    {
        SetActiveLightingProfile(ResolveLightingProfile(room));

        combatLightingRequested = true;
        showLightingRequested = false;
        remainingLitFaceScans = 3;

        EnsureColorGrading();
        colorGrading.SetPresentationMode(true, false);

        RefreshEnemyLightVisuals(true);

        if (!focusPlayerOnCombatStart || player == null || battleCamera == null)
            return;

        ReleaseCombatStartFocus();
        combatStartFocusRequest = battleCamera.FocusTarget(
            player.transform,
            combatStartFocusDuration,
            combatStartZoom,
            BattleCameraFocusPriority.FieldCombat);
    }

    private void HandleRoomCombatCleared(RoomDefinitionSO _)
    {
        combatLightingRequested = false;
        SetEnemyLightsTarget(false);
        ReleaseCombatStartFocus();
        battleCamera?.ReleaseAllFieldFocus();

        EnsureColorGrading();
        colorGrading.SetPresentationMode(false, showLightingRequested);
    }

    private void Update()
    {
        ResolveReferences();
        EnsureLightingProfile();
        ConfigureCameraBackground();
        EnsureWorldLight();
        EnsureColorGrading();

        UpdateWorldLight();
        UpdateCameraFillLight();
        if (combatLightingRequested && remainingLitFaceScans > 0 &&
            Time.unscaledTime >= nextLitFaceScan)
        {
            nextLitFaceScan = Time.unscaledTime + 0.30f;
            remainingLitFaceScans--;
            EnsureStageSpritesReceiveLight();
        }
#if UNITY_EDITOR
        SavePlayLightingPresetIfDirty(false);
#endif
        EnsurePlayerLightVisual();
        EnsurePresenterLightVisual();

        if (Time.unscaledTime >= nextEnemyLightScan)
        {
            nextEnemyLightScan =
                Time.unscaledTime + Mathf.Max(0.05f, enemyLightScanInterval);
            RefreshEnemyLightVisuals(false);
        }

        if (Time.unscaledTime >= nextSurfaceLightUpdate)
        {
            nextSurfaceLightUpdate = Time.unscaledTime + 0.10f;
            UpdateAutomaticCharacterLighting();
        }

        bool playerActive =
            player != null &&
            player.IsAlive &&
            player.gameObject.activeInHierarchy &&
            (combatLightingRequested || showLightingRequested);

        if (playerLightVisual != null)
        {
            float strength =
                combatLightingRequested && !showLightingRequested
                    ? Mathf.Clamp01(activeLightingProfile.playerCombatStrength)
                    : 1f;

            playerLightVisual.SetTarget(playerActive, strength);
        }

        bool presenterActive =
            showLightingRequested &&
            presenterLightVisual != null &&
            lastPresenterTarget != null &&
            lastPresenterTarget.gameObject.activeInHierarchy;

        if (presenterLightVisual != null)
            presenterLightVisual.SetTarget(presenterActive, 1f);

        for (int i = enemyLightBindings.Count - 1; i >= 0; i--)
        {
            EnemyLightBinding binding = enemyLightBindings[i];
            if (binding == null || binding.monster == null || binding.visual == null)
            {
                enemyLightBindings.RemoveAt(i);
                continue;
            }

            binding.visual.SetTarget(
                combatLightingRequested && binding.monster.IsAlive,
                Mathf.Clamp01(activeLightingProfile.enemyCombatStrength));
        }
    }

    /// <summary>
    /// Tile roofs, walls and sides all use the same URP 2D Lit material.
    /// Runs only on room entry (three bounded passes for staged entry),
    /// never each frame; custom effects and Show-only carriers are untouched.
    /// </summary>
    private void EnsureStageSpritesReceiveLight()
    {
        Material lit = SpatialRuntimeSpriteCache.FloorLitMaterial;
        if (lit == null)
            return;

        if (roomManager == null)
            return;
        litRoomSortingLayers.Clear();
        roomManager.CopyActiveRoomBlocks(litRoomBlocks);
        for (int i = 0; i < litRoomBlocks.Count; i++)
        {
            MapBlock block = litRoomBlocks[i];
            if (block == null)
                continue;

            SpriteRenderer[] surfaces = block.GetComponentsInChildren<SpriteRenderer>(true);
            for (int j = 0; j < surfaces.Length; j++)
            {
                SpriteRenderer surface = surfaces[j];
                if (surface == null || surface.sprite == null)
                    continue;
                litRoomSortingLayers.Add(surface.sortingLayerID);
                // Do not touch animation-specific materials: just make the
                // stage floor, side and roof ordinary 2D-lit sprites.
                Material original = surface.sharedMaterial;
                string shaderName = original != null && original.shader != null
                    ? original.shader.name : "";
                if (original == null || shaderName == "Sprites/Default" ||
                    shaderName.Contains("Sprite-Unlit") ||
                    shaderName.Contains("Sprite-Lit") ||
                    shaderName.StartsWith("Unlit/"))
                    surface.sharedMaterial = lit;
            }
        }

#if UNITY_EDITOR
        // A Light2D can target only Default while field pieces use other
        // sorting layers. Apply all stage sorting layers to the runtime
        // lights during editor preview, without modifying scene YAML.
        EnsurePlayLightSortingLayers(fieldGlobalLight);
        EnsurePlayLightSortingLayers(cameraFillLight);
        BattleCombatLightPolicyController policy =
            FindFirstObjectByType<BattleCombatLightPolicyController>();
        if (policy != null)
            EnsurePlayLightSortingLayers(policy.RuntimePlayerLight);
#endif
    }

    private void UpdateWorldLight()
    {
        if (fieldGlobalLight == null || activeLightingProfile == null)
            return;

        float target = Mathf.Max(0f, activeLightingProfile.worldLightIdleIntensity);

        if (combatLightingRequested)
            target = Mathf.Max(0f, activeLightingProfile.worldLightCombatIntensity);
        else if (showLightingRequested)
            target = Mathf.Max(0f, activeLightingProfile.worldLightShowIntensity);

        Color targetColor = combatLightingRequested
            ? activeLightingProfile.worldLightCombatColor
            : activeLightingProfile.worldLightColor;
#if UNITY_EDITOR
        if (playLighting != null && combatLightingRequested && !showLightingRequested)
        {
            target = playLighting.ambient;
            targetColor = Color.Lerp(
                new Color(0.79f, 0.88f, 1f, 1f),
                new Color(1f, 0.91f, 0.79f, 1f),
                playLighting.warmth);
        }
#endif
        float blend = 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime);
        fieldGlobalLight.color = Color.Lerp(fieldGlobalLight.color, targetColor, blend);

        float current = fieldGlobalLight.intensity;
        float duration =
            target >= current
                ? Mathf.Max(0.05f, activeLightingProfile.worldLightRiseDuration)
                : Mathf.Max(0.05f, activeLightingProfile.worldLightFallDuration);

        float maxDelta = 1.5f / duration * Time.unscaledDeltaTime;

        fieldGlobalLight.intensity = Mathf.MoveTowards(
            current,
            target,
            maxDelta);
    }

    private void EnsurePlayerLightVisual()
    {
        if (player == null || activeLightingProfile == null)
            return;

        if (playerLightVisual == null)
        {
            playerLightVisual = EnsureCharacterLightVisual(player.gameObject);
            if (playerLightVisual != null)
                ConfigurePlayerVisual(playerLightVisual);
        }
    }

    private void ConfigurePlayerVisual(BattleCharacterLightVisual visual)
    {
        if (visual == null || activeLightingProfile == null)
            return;

        visual.Configure(
            ResolvePrimarySpriteRenderer(player != null ? player.gameObject : visual.gameObject),
            activeLightingProfile.playerPoolColor,
            activeLightingProfile.playerTopLightColor,
            activeLightingProfile.playerPoolAlpha,
            activeLightingProfile.playerPoolWidthMultiplier,
            activeLightingProfile.playerPoolHeightRatio,
            activeLightingProfile.playerTopLightStrength,
            activeLightingProfile.characterLightFadeSharpness);

        // Player sprites can be offset children. Keep the stage beam/pool horizontally
        // centered on the gameplay Player transform instead of SpriteRenderer.bounds.center.x.
        visual.SetPlacementAnchor(player != null ? player.transform : visual.transform, true);

        visual.ConfigureAutoSurface(
            activeLightingProfile.enableAutoSpriteVolume,
            activeLightingProfile.enableAutoContactShadow,
            activeLightingProfile.playerAutoVolumeStrength,
            activeLightingProfile.contactShadowOpacity,
            activeLightingProfile.contactShadowMaxOffsetWorld);

        visual.ConfigureKeyLight(
            true,
            activeLightingProfile.playerKeyLightColor,
            activeLightingProfile.playerKeyLightAlpha,
            activeLightingProfile.playerKeyLightWidthMultiplier,
            activeLightingProfile.playerKeyLightHeightMultiplier,
            activeLightingProfile.playerKeyLightVerticalOffsetRatio);
    }

    private void EnsurePresenterLightVisual()
    {
        if (activeLightingProfile == null)
            return;

        Transform presenterTarget =
            showWorldSet != null
                ? showWorldSet.PresenterWorldTransform
                : null;

        if (presenterTarget == null)
        {
            lastPresenterTarget = null;
            presenterLightVisual = null;
            return;
        }

        if (presenterTarget == lastPresenterTarget && presenterLightVisual != null)
            return;

        lastPresenterTarget = presenterTarget;
        presenterLightVisual = EnsureCharacterLightVisual(presenterTarget.gameObject);

        if (presenterLightVisual != null)
        {
            ConfigurePresenterVisual(presenterLightVisual, presenterTarget.gameObject);
            presenterLightVisual.SetImmediate(0f);
        }
    }

    private void ConfigurePresenterVisual(
        BattleCharacterLightVisual visual,
        GameObject presenterObject)
    {
        if (visual == null || activeLightingProfile == null)
            return;

        visual.Configure(
            ResolvePrimarySpriteRenderer(presenterObject),
            activeLightingProfile.presenterPoolColor,
            activeLightingProfile.presenterTopLightColor,
            activeLightingProfile.presenterPoolAlpha,
            activeLightingProfile.presenterPoolWidthMultiplier,
            activeLightingProfile.presenterPoolHeightRatio,
            activeLightingProfile.presenterTopLightStrength,
            activeLightingProfile.characterLightFadeSharpness);

        visual.ConfigureKeyLight(
            true,
            activeLightingProfile.presenterKeyLightColor,
            activeLightingProfile.presenterKeyLightAlpha,
            activeLightingProfile.presenterKeyLightWidthMultiplier,
            activeLightingProfile.presenterKeyLightHeightMultiplier,
            activeLightingProfile.presenterKeyLightVerticalOffsetRatio);
    }

    private void RefreshEnemyLightVisuals(bool force)
    {
        for (int i = enemyLightBindings.Count - 1; i >= 0; i--)
        {
            EnemyLightBinding binding = enemyLightBindings[i];
            if (binding == null || binding.monster == null || binding.visual == null)
                enemyLightBindings.RemoveAt(i);
        }

        if (!force && !combatLightingRequested)
            return;

        MonsterController[] monsters = FindObjectsByType<MonsterController>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < monsters.Length; i++)
        {
            MonsterController monster = monsters[i];
            if (monster == null)
                continue;

            EnemyLightBinding binding = FindEnemyBinding(monster);
            if (binding == null)
            {
                BattleCharacterLightVisual visual =
                    EnsureCharacterLightVisual(monster.gameObject);

                if (visual == null)
                    continue;

                ConfigureEnemyVisual(visual, monster.gameObject);
                visual.SetImmediate(0f);

                binding = new EnemyLightBinding
                {
                    monster = monster,
                    visual = visual
                };
                enemyLightBindings.Add(binding);
            }

            binding.visual.SetTarget(
                combatLightingRequested && monster.IsAlive,
                Mathf.Clamp01(activeLightingProfile.enemyCombatStrength));
        }
    }

    private void ConfigureEnemyVisual(
        BattleCharacterLightVisual visual,
        GameObject monsterObject)
    {
        if (visual == null || activeLightingProfile == null)
            return;

        visual.Configure(
            ResolvePrimarySpriteRenderer(monsterObject),
            activeLightingProfile.enemyPoolColor,
            activeLightingProfile.enemyTopLightColor,
            activeLightingProfile.enemyPoolAlpha,
            activeLightingProfile.enemyPoolWidthMultiplier,
            activeLightingProfile.enemyPoolHeightRatio,
            activeLightingProfile.enemyTopLightStrength,
            activeLightingProfile.characterLightFadeSharpness);

        visual.ConfigureKeyLight(
            false,
            activeLightingProfile.enemyPoolColor,
            0f,
            1f,
            1f,
            0f);

        visual.ConfigureAutoSurface(
            activeLightingProfile.enableAutoSpriteVolume,
            activeLightingProfile.enableAutoContactShadow,
            activeLightingProfile.enemyAutoVolumeStrength,
            activeLightingProfile.contactShadowOpacity,
            activeLightingProfile.contactShadowMaxOffsetWorld);
    }

    /// <summary>
    /// One coarse Point Light2D query for all actors, at most once every 0.75s.
    /// Each actor samples only the strongest nearby candidate every 0.10s.
    /// No ShadowCaster, sprite pixel reads or allocations per animation frame.
    /// </summary>
    private void UpdateAutomaticCharacterLighting()
    {
        if (activeLightingProfile == null)
            return;

        bool combat = combatLightingRequested && !showLightingRequested;
        if (combat && Time.unscaledTime >= nextLocalLightScan)
        {
            nextLocalLightScan = Time.unscaledTime + 0.75f;
            cachedPointLights.Clear();
            Light2D[] lights = FindObjectsByType<Light2D>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                Light2D light = lights[i];
                if (light == null || light.lightType != Light2D.LightType.Point ||
                    !light.isActiveAndEnabled || light.gameObject.scene != gameObject.scene)
                    continue;
                cachedPointLights.Add(light);
                if (cachedPointLights.Count >= 64)
                    break;
            }
        }
        else if (!combat)
        {
            cachedPointLights.Clear();
            nextLocalLightScan = 0f;
        }

        if (playerLightVisual != null)
            ApplyAutomaticLight(
                playerLightVisual,
                combat && player != null && player.IsAlive);
        for (int i = 0; i < enemyLightBindings.Count; i++)
        {
            EnemyLightBinding binding = enemyLightBindings[i];
            if (binding == null || binding.monster == null || binding.visual == null)
                continue;
            ApplyAutomaticLight(
                binding.visual,
                combat && binding.monster.IsAlive);
        }
    }

    private void ApplyAutomaticLight(BattleCharacterLightVisual visual, bool active)
    {
        if (visual == null || activeLightingProfile == null)
            return;

        BattleLightingProfileSO profile = activeLightingProfile;
        Vector2 direction = profile.spriteVolumeFillDirection;
        if (direction.sqrMagnitude < 0.0001f)
            direction = new Vector2(-0.55f, 0.85f);
        direction.Normalize();

        Color color = profile.spriteVolumeFillColor;
        float strongestWeight = 0f;
        Light2D strongest = null;
        Vector2 characterPosition = visual.transform.position;

        if (active)
        {
            for (int i = 0; i < cachedPointLights.Count; i++)
            {
                Light2D light = cachedPointLights[i];
                if (light == null || !light.isActiveAndEnabled ||
                    light.intensity <= 0.001f)
                    continue;

                Vector2 delta = (Vector2)light.transform.position - characterPosition;
                float range = Mathf.Max(0.01f, light.pointLightOuterRadius);
                float normalizedDistance = delta.magnitude / range;
                if (normalizedDistance >= 1f)
                    continue;

                float weight = Mathf.Clamp01(
                    (1f - normalizedDistance) * light.intensity *
                    Mathf.Max(0.1f, profile.localLightResponse));
                if (weight <= strongestWeight)
                    continue;

                strongestWeight = weight;
                strongest = light;
            }

            if (strongest != null)
            {
                Vector2 delta = (Vector2)strongest.transform.position - characterPosition;
                if (delta.sqrMagnitude > 0.001f)
                    direction = (direction * 0.65f +
                        delta.normalized * (1.9f * strongestWeight)).normalized;
                color = Color.Lerp(color, strongest.color,
                    Mathf.Clamp01(strongestWeight * 0.8f));
            }
        }

        visual.SetAutoSurfaceLight(
            active, direction, color,
            Mathf.Lerp(0.85f, 1.25f, strongestWeight), strongestWeight);
    }

    private void ReconfigureCharacterLights()
    {
        if (playerLightVisual != null)
            ConfigurePlayerVisual(playerLightVisual);

        if (presenterLightVisual != null && lastPresenterTarget != null)
            ConfigurePresenterVisual(
                presenterLightVisual,
                lastPresenterTarget.gameObject);

        for (int i = enemyLightBindings.Count - 1; i >= 0; i--)
        {
            EnemyLightBinding binding = enemyLightBindings[i];
            if (binding == null || binding.monster == null || binding.visual == null)
            {
                enemyLightBindings.RemoveAt(i);
                continue;
            }

            ConfigureEnemyVisual(binding.visual, binding.monster.gameObject);
        }
    }

    private EnemyLightBinding FindEnemyBinding(MonsterController monster)
    {
        for (int i = 0; i < enemyLightBindings.Count; i++)
        {
            EnemyLightBinding binding = enemyLightBindings[i];
            if (binding != null && binding.monster == monster)
                return binding;
        }

        return null;
    }

    private void SetEnemyLightsTarget(bool active)
    {
        if (activeLightingProfile == null)
            return;

        for (int i = enemyLightBindings.Count - 1; i >= 0; i--)
        {
            EnemyLightBinding binding = enemyLightBindings[i];
            if (binding == null || binding.monster == null || binding.visual == null)
            {
                enemyLightBindings.RemoveAt(i);
                continue;
            }

            binding.visual.SetTarget(
                active && binding.monster.IsAlive,
                Mathf.Clamp01(activeLightingProfile.enemyCombatStrength));
        }
    }

    private static BattleCharacterLightVisual EnsureCharacterLightVisual(GameObject target)
    {
        if (target == null)
            return null;

        BattleCharacterLightVisual visual =
            target.GetComponent<BattleCharacterLightVisual>();

        if (visual == null)
            visual = target.AddComponent<BattleCharacterLightVisual>();

        return visual;
    }

    private static SpriteRenderer ResolvePrimarySpriteRenderer(GameObject target)
    {
        if (target == null)
            return null;

        SpriteRenderer direct = target.GetComponent<SpriteRenderer>();
        if (IsValidPrimaryRenderer(direct))
            return direct;

        SpriteRenderer[] renderers = target.GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer best = null;
        float bestArea = -1f;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (!IsValidPrimaryRenderer(renderer))
                continue;

            float area = Mathf.Abs(renderer.bounds.size.x * renderer.bounds.size.y);
            if (area <= bestArea)
                continue;

            best = renderer;
            bestArea = area;
        }

        return best;
    }

    private static bool IsValidPrimaryRenderer(SpriteRenderer renderer)
    {
        return renderer != null &&
               renderer.name != BattleCharacterLightVisual.KeyRendererName &&
               renderer.name != BattleCharacterLightVisual.PoolRendererName &&
               renderer.name != BattleCharacterLightVisual.GlowRendererName &&
               renderer.name != BattleCharacterLightVisual.ContactShadowRendererName;
    }

    private void SetCharacterLightsImmediate(bool active)
    {
        float visibility = active ? 1f : 0f;

        if (playerLightVisual != null)
            playerLightVisual.SetImmediate(visibility);

        if (presenterLightVisual != null)
            presenterLightVisual.SetImmediate(visibility);

        for (int i = 0; i < enemyLightBindings.Count; i++)
        {
            EnemyLightBinding binding = enemyLightBindings[i];
            if (binding != null && binding.visual != null)
                binding.visual.SetImmediate(visibility);
        }
    }

    private void ReleaseCombatStartFocus()
    {
        if (combatStartFocusRequest == 0)
            return;

        battleCamera?.ReleaseFocus(combatStartFocusRequest);
        combatStartFocusRequest = 0;
    }

    public int FocusTarget(
        Transform target,
        float duration,
        float zoom = 0f,
        BattleCameraFocusPriority priority = BattleCameraFocusPriority.FieldCombat)
    {
        return battleCamera != null
            ? battleCamera.FocusTarget(target, duration, zoom, priority)
            : 0;
    }

    public int FocusBounds(
        Bounds bounds,
        float duration,
        float padding = -1f,
        BattleCameraFocusPriority priority = BattleCameraFocusPriority.FieldCombat)
    {
        return battleCamera != null
            ? battleCamera.FocusBounds(bounds, duration, padding, priority)
            : 0;
    }

    public int FocusPosition(
        Vector3 worldPosition,
        float duration,
        float zoom = 0f,
        BattleCameraFocusPriority priority = BattleCameraFocusPriority.FieldCombat)
    {
        return battleCamera != null
            ? battleCamera.FocusPosition(worldPosition, duration, zoom, priority)
            : 0;
    }

    public void ReleaseCameraFocus(int requestId)
    {
        battleCamera?.ReleaseFocus(requestId);
    }

    public void PushCameraImpulse(Vector2 direction, float strength, float duration = 0.13f)
    {
        battleCamera?.PushCameraImpulse(direction, strength, duration);
    }

#if UNITY_EDITOR
    // Editor Game-view only. JSON is intentionally saved instead of editing a
    // .cs file: touching C# during Play would trigger a domain reload.
    [System.Serializable]
    private sealed class PlayLightingPreset
    {
        public float ambient = 0.68f;
        public float warmth = 0.42f;
        public float cameraIntensity = 0.40f;
        public float cameraRadius = 9f;
        public float playerIntensity = 0.86f;
        public float playerRadius = 4.6f;
    }

    private const string PlayLightingPresetPath =
        "Resources/BattleLightingPlayTune.json";
    private PlayLightingPreset playLighting;
    private bool playLightingOpen = true;
    private bool playLightingDirty;
    private float nextPlayLightingSave;
    private Rect playLightingWindow = new(12f, 12f, 330f, 312f);
    private static readonly string[] PlayLightingTabs = { "Lighting", "Shadows" };
    private static readonly string[] PlayShadowModes = { "None", "Contact", "Projected" };
    private int playLightingTab;
    private readonly List<BattleSpriteShadow> playShadowTargets = new();
    private BattleSpriteShadow playShadowTarget;
    private readonly List<BattleSpriteShadow> playShadowCasters = new();
    private Vector2 playShadowScroll;
    private string playShadowOverlapHint;
    private float authoredCameraIntensity;
    private float authoredCameraRadius;
    private Color authoredCameraColor;
    private bool authoredCameraEnabled;
    private bool authoredCameraCaptured;
    private readonly Dictionary<Light2D, int[]> originalLightSortingLayers = new();

    public bool TryGetPlayerPlayTuning(out float intensity, out float radius)
    {
        intensity = playLighting != null ? playLighting.playerIntensity : 0f;
        radius = playLighting != null ? playLighting.playerRadius : 0f;
        return Application.isPlaying && playLighting != null;
    }

    private void LoadPlayLightingPreset()
    {
        if (playLighting != null)
            return;

        playLighting = new PlayLightingPreset();
        string fullPath = Path.Combine(Application.dataPath, PlayLightingPresetPath);
        if (!File.Exists(fullPath))
            return;

        try
        {
            PlayLightingPreset saved = JsonUtility.FromJson<PlayLightingPreset>(
                File.ReadAllText(fullPath));
            if (saved != null)
                playLighting = saved;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[BattleLighting] Could not read Play lighting preset: " +
                e.Message, this);
        }
    }

    private void SavePlayLightingPresetIfDirty(bool force)
    {
        if (!playLightingDirty || playLighting == null ||
            (!force && Time.realtimeSinceStartup < nextPlayLightingSave))
            return;

        try
        {
            string path = Path.Combine(Application.dataPath, PlayLightingPresetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(playLighting, true) + "\n");
            playLightingDirty = false;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[BattleLighting] Could not save Play lighting preset: " +
                e.Message, this);
            nextPlayLightingSave = Time.realtimeSinceStartup + 2f;
        }
    }

    private void MarkPlayLightingDirty()
    {
        playLightingDirty = true;
        nextPlayLightingSave = Time.realtimeSinceStartup + 0.40f;
    }

    private float PlayLightingSlider(string title, float value, float min, float max, bool save = true)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(title, GUILayout.Width(154f));
        float next = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.Label(next.ToString("0.00"), GUILayout.Width(45f));
        GUILayout.EndHorizontal();
        if (save && Mathf.Abs(next - value) > 0.0001f)
            MarkPlayLightingDirty();
        return next;
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || instance != this)
            return;

        if (Event.current.type == EventType.KeyDown &&
            Event.current.keyCode == KeyCode.F8)
        {
            playLightingOpen = !playLightingOpen;
            Event.current.Use();
        }

        WorldLight shadowLight = WorldLight.Instance;
        if (shadowLight != null)
            shadowLight.ShadowDebugTarget = null;
        if (!playLightingOpen)
        {
            if (GUI.Button(new Rect(12f, 12f, 165f, 27f),
                    "PLAY LIGHT TUNER [F8]"))
                playLightingOpen = true;
            return;
        }

        LoadPlayLightingPreset();
        if (playLightingTab == 1)
        {
            BattleSpriteShadow.CopyDebugTargets(playShadowTargets);
            if (playShadowTarget == null || !playShadowTargets.Contains(playShadowTarget))
            {
                playShadowTarget = playShadowTargets.Count > 0 ? playShadowTargets[0] : null;
                for (int i = 0; player != null && i < playShadowTargets.Count; i++)
                {
                    if (!playShadowTargets[i].transform.IsChildOf(player.transform))
                        continue;
                    playShadowTarget = playShadowTargets[i];
                    break;
                }
            }
            if (shadowLight != null)
                shadowLight.ShadowDebugTarget = playShadowTarget;
            playShadowCasters.Clear();
            if (playShadowTarget != null && shadowLight != null)
                playShadowTarget.CopyReceiverDebugCasters(shadowLight.DebugCamera, playShadowCasters);
            DrawShadowDebugRays();
        }
        playLightingWindow.height = playLightingTab == 1 ? Mathf.Min(720f, Mathf.Max(180f, Screen.height - 24f)) : 342f;
        if (playLightingTab == 1)
            playLightingWindow.y = Mathf.Clamp(playLightingWindow.y, 0f, Mathf.Max(0f, Screen.height - playLightingWindow.height));
        playLightingWindow = GUI.Window(
            74292, playLightingWindow, DrawPlayLightingWindow,
            "PLAY LIGHT TUNER  [F8]");
    }

    private void DrawPlayLightingWindow(int id)
    {
        GUILayout.Space(5f);
        playLightingTab = GUILayout.Toolbar(playLightingTab, PlayLightingTabs);
        if (playLightingTab == 1)
        {
            playShadowScroll = GUILayout.BeginScrollView(playShadowScroll,
                GUILayout.Height(playLightingWindow.height - 70f));
            DrawPlayShadowWindow();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 330f, 23f));
            return;
        }
        GUILayout.Label("Combat preview only  |  Values saved to JSON");
        playLighting.ambient = PlayLightingSlider(
            "Ambient", playLighting.ambient, 0.15f, 1.4f);
        playLighting.warmth = PlayLightingSlider(
            "Ambient warmth", playLighting.warmth, 0f, 1f);
        playLighting.cameraIntensity = PlayLightingSlider(
            "Camera light power", playLighting.cameraIntensity, 0f, 1.5f);
        playLighting.cameraRadius = PlayLightingSlider(
            "Camera light radius", playLighting.cameraRadius, 1f, 24f);
        playLighting.playerIntensity = PlayLightingSlider(
            "Player light power", playLighting.playerIntensity, 0f, 2f);
        playLighting.playerRadius = PlayLightingSlider(
            "Player light radius", playLighting.playerRadius, 0.5f, 12f);

        GUILayout.Space(5f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save now", GUILayout.Width(95f)))
        {
            MarkPlayLightingDirty();
            SavePlayLightingPresetIfDirty(true);
        }
        if (GUILayout.Button("Reset", GUILayout.Width(80f)))
        {
            playLighting = new PlayLightingPreset();
            MarkPlayLightingDirty();
        }
        GUILayout.Label(playLightingDirty ? "Saving..." : "Saved");
        GUILayout.EndHorizontal();
        GUILayout.Label("F8: Show/Hide  |  Drag title bar to move");
        GUI.DragWindow(new Rect(0f, 0f, 330f, 23f));
    }

    private void DrawPlayShadowWindow()
    {
        WorldLight light = WorldLight.Instance;
        if (light == null)
        {
            GUILayout.Label("WorldLight is not active.");
            return;
        }

        GUILayout.Label("Live preview | Play-session values only");
        light.shadowMode = (BattleShadowMode)GUILayout.Toolbar((int)light.shadowMode, PlayShadowModes);
        light.previewConnectSpriteBottom = GUILayout.Toggle(light.previewConnectSpriteBottom,
            "Test bottom connection (grounded only)");
        light.showShadowDebugRays = GUILayout.Toggle(light.showShadowDebugRays, "Show floor / receiver rays + mask");
        float angle = Mathf.Atan2(light.ShadowDirection.y, light.ShadowDirection.x) * Mathf.Rad2Deg;
        float nextAngle = PlayLightingSlider("Shadow direction", angle, -180f, 180f, false);
        float length = PlayLightingSlider("Shadow length", light.shadowLength, 0f, 5f, false);
        float width = PlayLightingSlider("Shadow spread", light.shadowWidth, 0.05f, 3f, false);
        float opacity = PlayLightingSlider("Shadow opacity", light.shadowOpacity, 0f, 1f, false);
        light.previewBottomInset = PlayLightingSlider("Bottom inset (world)", light.previewBottomInset, 0f, 0.5f, false);
        light.shadowDebugRayLength = PlayLightingSlider("Bottom guide length", light.shadowDebugRayLength, 0.05f, 3f, false);
        if (Mathf.Abs(nextAngle - angle) > 0.0001f || length != light.shadowLength ||
            width != light.shadowWidth || opacity != light.shadowOpacity)
        {
            light.lightDirection = -new Vector2(Mathf.Cos(nextAngle * Mathf.Deg2Rad), Mathf.Sin(nextAngle * Mathf.Deg2Rad));
            light.shadowLength = length;
            light.shadowWidth = width;
            light.shadowOpacity = opacity;
            light.overrideProfileDefaults = true;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(playShadowTarget != null ? playShadowTarget.name : "No visible projected target");
        if (GUILayout.Button("Next", GUILayout.Width(48f)) && playShadowTargets.Count > 0)
        {
            int next = (playShadowTargets.IndexOf(playShadowTarget) + 1) % playShadowTargets.Count;
            playShadowTarget = playShadowTargets[next];
        }
        GUILayout.EndHorizontal();
        if (playShadowTarget != null && playShadowTarget.TryGetDebugGeometry(out BattleSpriteShadow.DebugGeometry geometry))
        {
            float gap = Vector2.Distance(geometry.spriteBottom, geometry.shadowStart);
            GUILayout.Label((geometry.grounded ? "Grounded" : "Airborne (connection unchanged)") +
                " | Start offset: " + gap.ToString("0.000"));
            if (GUILayout.Button("Recapture selected ground anchor"))
                playShadowTarget.ResetGroundAnchorCapture();
        }
        if (GUILayout.Button("Find receiver candidate"))
        {
            playShadowOverlapHint = "No sorted overlap candidates.\nMove a front caster across a receiver.";
            for (int i = 1; i <= playShadowTargets.Count; i++)
            {
                int index = (playShadowTargets.IndexOf(playShadowTarget) + i) % playShadowTargets.Count;
                BattleSpriteShadow candidate = playShadowTargets[index];
                if (candidate == null || !candidate.CopyReceiverDebugCasters(light.DebugCamera, playShadowCasters) ||
                    playShadowCasters.Count == 0)
                    continue;
                playShadowTarget = candidate;
                light.ShadowDebugTarget = candidate;
                playShadowOverlapHint = null;
                break;
            }
        }
        playShadowCasters.Clear();
        bool receiverVisible = playShadowTarget != null &&
            playShadowTarget.CopyReceiverDebugCasters(light.DebugCamera, playShadowCasters);
        if (receiverVisible && playShadowCasters.Count > 0)
            playShadowOverlapHint = null;
        if (!string.IsNullOrEmpty(playShadowOverlapHint))
            GUILayout.Label(playShadowOverlapHint);
        GUILayout.Label(receiverVisible ? "Caster candidates: " + playShadowCasters.Count : "Receiver not visible / not rendered yet");
        for (int i = 0; i < Mathf.Min(3, playShadowCasters.Count); i++)
            GUILayout.Label("From: " + playShadowCasters[i].name);
        GUILayout.Label("Blue: FLOOR (full projection)");
        GUILayout.Label("Magenta: projection clipped to receiver bounds");
        GUILayout.Label("Floor / receiver share origin + length.");
        GUILayout.Label("Ray tips mark the full projection length.");
        GUILayout.Label("Receiver outline / clips = bounds only.");
        GUILayout.Label("Self / back-sorted casters are excluded.");
        GUILayout.Label("Sorting layer / order, then camera sort distance.");
        if (receiverVisible && playShadowTarget != null && light.showShadowDebugRays && light.shadowMode == BattleShadowMode.Projected &&
            light.ShadowDebugMaskTarget == playShadowTarget && light.ShadowDebugMask != null)
        {
            GUILayout.Label("Received alpha x4 (display gain only)");
            Rect preview = GUILayoutUtility.GetRect(128f, 128f, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(preview, light.ShadowDebugMask, ScaleMode.ScaleToFit, false);
            GUILayout.Label("Bright = shadow; black = none.");
        }
        else
            GUILayout.Label("Mask needs Projected mode + visible target.");
        GUILayout.Label("Yellow: alpha bottom");
        GUILayout.Label("Red: gap | Orange: captured anchor");
        GUILayout.Label("Bottom inset shifts the alpha contact.");
        GUILayout.Label("F8: Show/Hide | Drag title bar to move");
    }

    private void DrawShadowDebugRays()
    {
        WorldLight light = WorldLight.Instance;
        if (light == null || !light.showShadowDebugRays || light.shadowMode != BattleShadowMode.Projected ||
            light.DebugCamera == null || playShadowTarget == null)
            return;

        Camera camera = light.DebugCamera;
        if (playShadowTarget.TryGetDebugGeometry(out BattleSpriteShadow.DebugGeometry geometry))
        {
            DrawShadowDebugLine(camera, geometry.spriteBottomLeft, geometry.spriteBottomRight, Color.yellow);
            DrawShadowDebugLine(camera, geometry.spriteBottom,
                geometry.spriteBottom + Vector3.down * light.shadowDebugRayLength, Color.yellow);
            DrawShadowDebugLine(camera, geometry.spriteBottom, geometry.shadowStart, Color.red);
            DrawShadowDebugRay(camera, geometry.shadowStart, geometry.shadowEnd, new Color(0f, 0.6f, 1f));
            Vector3 marker = Vector3.right * 0.06f;
            DrawShadowDebugLine(camera, geometry.capturedAnchor - marker, geometry.capturedAnchor + marker,
                new Color(1f, 0.5f, 0f));
            DrawShadowDebugLine(camera, geometry.shadowStart - Vector3.up * 0.06f,
                geometry.shadowStart + Vector3.up * 0.06f, Color.green);
        }
        Bounds bounds = playShadowTarget.Source.bounds;
        for (int i = 0; i < Mathf.Min(8, playShadowCasters.Count); i++)
        {
            BattleSpriteShadow caster = playShadowCasters[i];
            if (caster == null || !caster.TryGetDebugGeometry(out BattleSpriteShadow.DebugGeometry incoming))
                continue;
            DrawShadowDebugRay(camera, incoming.shadowStart, incoming.shadowEnd, new Color(0f, 0.6f, 1f));
            if (BattleShadowProjection.ClipSegmentXY(incoming.receiverShadowStart, incoming.receiverShadowEnd, bounds,
                out Vector3 start, out Vector3 end))
                DrawShadowDebugLine(camera, start, end, Color.magenta);
        }
        if (playShadowCasters.Count > 0)
        {
            Vector3 bottomLeft = new(bounds.min.x, bounds.min.y, bounds.center.z);
            Vector3 bottomRight = new(bounds.max.x, bounds.min.y, bounds.center.z);
            Vector3 topLeft = new(bounds.min.x, bounds.max.y, bounds.center.z);
            Vector3 topRight = new(bounds.max.x, bounds.max.y, bounds.center.z);
            DrawShadowDebugLine(camera, bottomLeft, bottomRight, Color.magenta);
            DrawShadowDebugLine(camera, bottomRight, topRight, Color.magenta);
            DrawShadowDebugLine(camera, topRight, topLeft, Color.magenta);
            DrawShadowDebugLine(camera, topLeft, bottomLeft, Color.magenta);
        }
    }

    private static void DrawShadowDebugRay(Camera camera, Vector3 from, Vector3 to, Color color)
    {
        DrawShadowDebugLine(camera, from, to, color);
        Vector3 direction = to - from;
        if (direction.sqrMagnitude < 0.000001f)
            return;
        float tipLength = Mathf.Min(0.10f, direction.magnitude * 0.25f);
        direction.Normalize();
        Vector3 side = new(-direction.y, direction.x, 0f);
        DrawShadowDebugLine(camera, to, to - direction * tipLength + side * tipLength * 0.5f, color);
        DrawShadowDebugLine(camera, to, to - direction * tipLength - side * tipLength * 0.5f, color);
    }

    private static void DrawShadowDebugLine(Camera camera, Vector3 from, Vector3 to, Color color)
    {
        Vector3 screenFrom = camera.WorldToScreenPoint(from);
        Vector3 screenTo = camera.WorldToScreenPoint(to);
        if (screenFrom.z <= 0f || screenTo.z <= 0f)
            return;
        Vector2 start = new(screenFrom.x, Screen.height - screenFrom.y);
        Vector2 end = new(screenTo.x, Screen.height - screenTo.y);
        Vector2 delta = end - start;
        if (delta.sqrMagnitude < 0.01f)
            return;
        Matrix4x4 matrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
        GUI.DrawTexture(new Rect(start.x, start.y - 1f, delta.magnitude, 2f), Texture2D.whiteTexture);
        GUI.matrix = matrix;
        GUI.color = previousColor;
    }

    private void RestoreAuthoredCameraLight()
    {
        if (ownsCameraFill || cameraFillLight == null ||
            !authoredCameraCaptured)
            return;

        cameraFillLight.intensity = authoredCameraIntensity;
        cameraFillLight.pointLightOuterRadius = authoredCameraRadius;
        cameraFillLight.color = authoredCameraColor;
        cameraFillLight.enabled = authoredCameraEnabled;
    }

    private void EnsurePlayLightSortingLayers(Light2D light)
    {
        if (light == null)
            return;

        UnityEditor.SerializedObject serialized = new(light);
        UnityEditor.SerializedProperty target =
            serialized.FindProperty("m_ApplyToSortingLayers");
        if (target == null)
            return;

        // Preserve the light's original targets and add only this Room's
        // active sprite layers. Do not blindly illuminate UI/TV sorting layers.
        HashSet<int> merged = new(litRoomSortingLayers);
        for (int i = 0; i < target.arraySize; i++)
            merged.Add(target.GetArrayElementAtIndex(i).intValue);

        List<int> layers = new(merged);
        layers.Sort();
        bool same = target.arraySize == layers.Count;
        if (same)
        {
            for (int i = 0; i < layers.Count; i++)
            {
                if (target.GetArrayElementAtIndex(i).intValue == layers[i])
                    continue;
                same = false;
                break;
            }
        }
        if (same)
            return;

        if (!originalLightSortingLayers.ContainsKey(light))
        {
            int[] original = new int[target.arraySize];
            for (int i = 0; i < original.Length; i++)
                original[i] = target.GetArrayElementAtIndex(i).intValue;
            originalLightSortingLayers.Add(light, original);
        }

        target.arraySize = layers.Count;
        for (int i = 0; i < layers.Count; i++)
            target.GetArrayElementAtIndex(i).intValue = layers[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private void RestorePlayLightSortingLayers()
    {
        foreach (KeyValuePair<Light2D, int[]> entry in originalLightSortingLayers)
        {
            if (entry.Key == null)
                continue;

            UnityEditor.SerializedObject serialized =
                new UnityEditor.SerializedObject(entry.Key);
            UnityEditor.SerializedProperty target =
                serialized.FindProperty("m_ApplyToSortingLayers");
            if (target == null)
                continue;
            target.arraySize = entry.Value.Length;
            for (int i = 0; i < entry.Value.Length; i++)
                target.GetArrayElementAtIndex(i).intValue = entry.Value[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        originalLightSortingLayers.Clear();
    }
#endif
}

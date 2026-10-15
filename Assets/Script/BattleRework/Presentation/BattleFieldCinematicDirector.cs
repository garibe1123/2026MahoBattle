using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

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
            if (existing == null || existing.lightType == Light2D.LightType.Global)
                continue;

            // Never take over the user's authored camera lights or overwrite
            // their intensity, radius, color, or enabled state.
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
        float target = profile.autoCameraCombatFill &&
            combatLightingRequested && !showLightingRequested
            ? Mathf.Max(0f, profile.cameraCombatFillIntensity)
            : 0f;

        cameraFillLight.color = profile.cameraCombatFillColor;
        cameraFillLight.pointLightInnerRadius =
            Mathf.Max(0.1f, profile.cameraCombatFillRadius * 0.22f);
        cameraFillLight.pointLightOuterRadius =
            Mathf.Max(cameraFillLight.pointLightInnerRadius + 0.05f,
                profile.cameraCombatFillRadius);
        cameraFillLight.intensity = Mathf.MoveTowards(
            cameraFillLight.intensity, target,
            2.0f * Time.unscaledDeltaTime);
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
}

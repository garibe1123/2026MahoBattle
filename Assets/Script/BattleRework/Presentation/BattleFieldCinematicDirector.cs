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
/// Reward / Map Selection:
/// - Existing show dim / TV rectangular focus stays authoritative.
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

    private Light2D fieldGlobalLight;
    private Camera sceneCamera;
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
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < lights.Length; i++)
        {
            Light2D candidate = lights[i];
            if (candidate == null || candidate.lightType != Light2D.LightType.Global)
                continue;
            if (candidate.gameObject.scene != gameObject.scene)
                continue;

            fieldGlobalLight = candidate;
            break;
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
            (state == BattleRunState.Reward || state == BattleRunState.SelectingNode);

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
        EnsurePlayerLightVisual();
        EnsurePresenterLightVisual();

        if (Time.unscaledTime >= nextEnemyLightScan)
        {
            nextEnemyLightScan =
                Time.unscaledTime + Mathf.Max(0.05f, enemyLightScanInterval);
            RefreshEnemyLightVisuals(false);
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

        fieldGlobalLight.color = activeLightingProfile.worldLightColor;

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
               renderer.name != BattleCharacterLightVisual.GlowRendererName;
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

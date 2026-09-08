using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Battle field / show lighting director.
///
/// Lighting rules:
/// - Battle scene background is always solid black.
/// - A single Global Light 2D provides the dim base world light.
/// - The Global Light fades in instead of switching on instantly.
/// - Player / Presenter / Enemy use a compressed ellipse light pool near the feet.
/// - A weak local Light2D and sprite overlay make the character itself glow slightly.
/// - TV / Screen never receives a character spotlight. Rect focus stays in BattleShowFocusController.
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

    [Header("Battle Camera Background")]
    [SerializeField] private Color battleBackgroundColor = Color.black;

    [Header("Battle World Light")]
    [SerializeField] private Color worldLightColor = new(0.94f, 0.96f, 1f, 1f);
    [SerializeField, Range(0f, 1f)] private float worldLightStartIntensity = 0.12f;
    [SerializeField, Range(0f, 1f)] private float worldLightCombatIntensity = 0.68f;
    [SerializeField, Range(0f, 1f)] private float worldLightShowIntensity = 0.50f;
    [SerializeField, Range(0f, 1f)] private float worldLightIdleIntensity = 0.42f;
    [SerializeField, Min(0.1f)] private float worldLightFadeSharpness = 1.85f;
    [SerializeField] private bool replayWorldLightFadeOnCombatStart = true;

    [Header("Player Light")]
    [SerializeField] private Color playerPoolColor = new(1f, 0.92f, 0.70f, 1f);
    [SerializeField] private Color playerBodyLightColor = new(1f, 0.94f, 0.78f, 1f);
    [SerializeField, Range(0f, 1f)] private float playerPoolAlpha = 0.38f;
    [SerializeField, Min(0.1f)] private float playerPoolWidthMultiplier = 1.45f;
    [SerializeField, Range(0.08f, 0.8f)] private float playerPoolHeightRatio = 0.27f;
    [SerializeField, Min(0f)] private float playerBodyLightIntensity = 0.20f;
    [SerializeField, Min(0.1f)] private float playerBodyLightRadiusMultiplier = 0.86f;
    [SerializeField, Range(0f, 0.5f)] private float playerSpriteGlowAlpha = 0.10f;

    [Header("Enemy Light")]
    [SerializeField] private Color enemyPoolColor = new(0.78f, 0.87f, 1f, 1f);
    [SerializeField] private Color enemyBodyLightColor = new(0.82f, 0.90f, 1f, 1f);
    [SerializeField, Range(0f, 1f)] private float enemyPoolAlpha = 0.24f;
    [SerializeField, Min(0.1f)] private float enemyPoolWidthMultiplier = 1.28f;
    [SerializeField, Range(0.08f, 0.8f)] private float enemyPoolHeightRatio = 0.24f;
    [SerializeField, Min(0f)] private float enemyBodyLightIntensity = 0.13f;
    [SerializeField, Min(0.1f)] private float enemyBodyLightRadiusMultiplier = 0.72f;
    [SerializeField, Range(0f, 0.5f)] private float enemySpriteGlowAlpha = 0.055f;
    [SerializeField, Min(0.05f)] private float enemyLightScanInterval = 0.22f;

    [Header("Presenter Light")]
    [SerializeField] private Color presenterPoolColor = new(1f, 0.86f, 0.62f, 1f);
    [SerializeField] private Color presenterBodyLightColor = new(1f, 0.90f, 0.72f, 1f);
    [SerializeField, Range(0f, 1f)] private float presenterPoolAlpha = 0.36f;
    [SerializeField, Min(0.1f)] private float presenterPoolWidthMultiplier = 1.36f;
    [SerializeField, Range(0.08f, 0.8f)] private float presenterPoolHeightRatio = 0.25f;
    [SerializeField, Min(0f)] private float presenterBodyLightIntensity = 0.18f;
    [SerializeField, Min(0.1f)] private float presenterBodyLightRadiusMultiplier = 0.82f;
    [SerializeField, Range(0f, 0.5f)] private float presenterSpriteGlowAlpha = 0.09f;

    [Header("Character Light Fade")]
    [SerializeField, Min(0.1f)] private float characterLightFadeSharpness = 7.5f;

    [Header("Combat Start Beat")]
    [SerializeField] private bool focusPlayerOnCombatStart = true;
    [SerializeField, Min(0f)] private float combatStartFocusDuration = 0.65f;
    [SerializeField, Min(0f)] private float combatStartZoom = 5.15f;

    private readonly List<EnemyLightBinding> enemyLightBindings = new();

    private Light2D fieldGlobalLight;
    private Camera sceneCamera;
    private BattleCharacterLightVisual playerLightVisual;
    private BattleCharacterLightVisual presenterLightVisual;
    private Transform lastPresenterTarget;

    private Coroutine bindRoutine;
    private bool subscribed;
    private bool combatLightingRequested;
    private bool showLightingRequested;
    private bool worldLightInitialized;
    private float nextEnemyLightScan;
    private int combatStartFocusRequest;

    public static BattleFieldCinematicDirector Instance => instance;
    public bool IsFieldLightingActive => fieldGlobalLight != null && fieldGlobalLight.intensity > 0.001f;

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

        if (!battleScene || Object.FindFirstObjectByType<BattleFieldCinematicDirector>() != null)
            return;

        GameObject host = new("BattleFieldCinematicRuntime");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<BattleFieldCinematicDirector>();
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
        EnsureWorldLight();
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
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private IEnumerator BindWhenReady()
    {
        while (enabled)
        {
            ResolveReferences();
            EnsureWorldLight();
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

        fieldGlobalLight.color = worldLightColor;

        if (!worldLightInitialized)
        {
            fieldGlobalLight.intensity = Mathf.Clamp01(worldLightStartIntensity);
            worldLightInitialized = true;
        }
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
        showLightingRequested = runActive &&
                                (state == BattleRunState.Reward || state == BattleRunState.SelectingNode);

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

    private void HandleRoomCombatStarted(RoomDefinitionSO _)
    {
        combatLightingRequested = true;

        if (fieldGlobalLight != null && replayWorldLightFadeOnCombatStart)
        {
            fieldGlobalLight.intensity = Mathf.Min(
                fieldGlobalLight.intensity,
                Mathf.Clamp01(worldLightStartIntensity));
        }

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
    }

    private void Update()
    {
        ResolveReferences();
        ConfigureCameraBackground();
        EnsureWorldLight();

        UpdateWorldLight();
        EnsurePlayerLightVisual();
        EnsurePresenterLightVisual();

        if (Time.unscaledTime >= nextEnemyLightScan)
        {
            nextEnemyLightScan = Time.unscaledTime + Mathf.Max(0.05f, enemyLightScanInterval);
            RefreshEnemyLightVisuals(false);
        }

        bool playerActive = player != null &&
                            player.IsAlive &&
                            player.gameObject.activeInHierarchy &&
                            (combatLightingRequested || showLightingRequested);

        if (playerLightVisual != null)
            playerLightVisual.SetTarget(playerActive, 1f);

        bool presenterActive = showLightingRequested &&
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

            binding.visual.SetTarget(combatLightingRequested && binding.monster.IsAlive, 1f);
        }
    }

    private void UpdateWorldLight()
    {
        if (fieldGlobalLight == null)
            return;

        float target = Mathf.Clamp01(worldLightIdleIntensity);
        if (combatLightingRequested)
            target = Mathf.Clamp01(worldLightCombatIntensity);
        else if (showLightingRequested)
            target = Mathf.Clamp01(worldLightShowIntensity);

        float t = ExponentialT(worldLightFadeSharpness);
        fieldGlobalLight.color = Color.Lerp(fieldGlobalLight.color, worldLightColor, t);
        fieldGlobalLight.intensity = Mathf.Lerp(fieldGlobalLight.intensity, target, t);

        if (Mathf.Abs(fieldGlobalLight.intensity - target) < 0.001f)
            fieldGlobalLight.intensity = target;
    }

    private void EnsurePlayerLightVisual()
    {
        if (player == null)
            return;

        if (playerLightVisual == null)
        {
            playerLightVisual = EnsureCharacterLightVisual(player.gameObject);
            if (playerLightVisual != null)
            {
                playerLightVisual.Configure(
                    ResolvePrimarySpriteRenderer(player.gameObject),
                    playerPoolColor,
                    playerBodyLightColor,
                    playerPoolAlpha,
                    playerPoolWidthMultiplier,
                    playerPoolHeightRatio,
                    playerBodyLightIntensity,
                    playerBodyLightRadiusMultiplier,
                    playerSpriteGlowAlpha,
                    characterLightFadeSharpness);
            }
        }
    }

    private void EnsurePresenterLightVisual()
    {
        Transform presenterTarget = showWorldSet != null
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
            presenterLightVisual.Configure(
                ResolvePrimarySpriteRenderer(presenterTarget.gameObject),
                presenterPoolColor,
                presenterBodyLightColor,
                presenterPoolAlpha,
                presenterPoolWidthMultiplier,
                presenterPoolHeightRatio,
                presenterBodyLightIntensity,
                presenterBodyLightRadiusMultiplier,
                presenterSpriteGlowAlpha,
                characterLightFadeSharpness);
        }
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
                BattleCharacterLightVisual visual = EnsureCharacterLightVisual(monster.gameObject);
                if (visual == null)
                    continue;

                visual.Configure(
                    ResolvePrimarySpriteRenderer(monster.gameObject),
                    enemyPoolColor,
                    enemyBodyLightColor,
                    enemyPoolAlpha,
                    enemyPoolWidthMultiplier,
                    enemyPoolHeightRatio,
                    enemyBodyLightIntensity,
                    enemyBodyLightRadiusMultiplier,
                    enemySpriteGlowAlpha,
                    characterLightFadeSharpness);

                binding = new EnemyLightBinding
                {
                    monster = monster,
                    visual = visual
                };
                enemyLightBindings.Add(binding);
            }

            binding.visual.SetTarget(combatLightingRequested && monster.IsAlive, 1f);
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
        for (int i = enemyLightBindings.Count - 1; i >= 0; i--)
        {
            EnemyLightBinding binding = enemyLightBindings[i];
            if (binding == null || binding.monster == null || binding.visual == null)
            {
                enemyLightBindings.RemoveAt(i);
                continue;
            }

            binding.visual.SetTarget(active && binding.monster.IsAlive, 1f);
        }
    }

    private static BattleCharacterLightVisual EnsureCharacterLightVisual(GameObject target)
    {
        if (target == null)
            return null;

        BattleCharacterLightVisual visual = target.GetComponent<BattleCharacterLightVisual>();
        if (visual == null)
            visual = target.AddComponent<BattleCharacterLightVisual>();

        return visual;
    }

    private static SpriteRenderer ResolvePrimarySpriteRenderer(GameObject target)
    {
        if (target == null)
            return null;

        SpriteRenderer direct = target.GetComponent<SpriteRenderer>();
        if (direct != null)
            return direct;

        SpriteRenderer[] renderers = target.GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer best = null;
        float bestArea = -1f;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null ||
                renderer.name == BattleCharacterLightVisual.PoolRendererName ||
                renderer.name == BattleCharacterLightVisual.GlowRendererName)
            {
                continue;
            }

            float area = Mathf.Abs(renderer.bounds.size.x * renderer.bounds.size.y);
            if (area <= bestArea)
                continue;

            best = renderer;
            bestArea = area;
        }

        return best;
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

    private float ExponentialT(float sharpness)
    {
        return 1f - Mathf.Exp(-Mathf.Max(0.01f, sharpness) * Time.unscaledDeltaTime);
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

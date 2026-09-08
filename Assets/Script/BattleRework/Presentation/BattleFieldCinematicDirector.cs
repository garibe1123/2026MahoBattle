using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 전투 Logic과 분리된 Field 연출 진입점입니다.
///
/// 현재 1차 책임:
/// - BattleRunState에 맞춰 Field 조명을 켜고 Show 진입 전 원상 복구
/// - Player를 따라가는 무대형 Point Light 2D
/// - BattleCameraController의 Focus / Impulse API에 대한 공용 요청 창구
///
/// Camera Transform, Player, Monster, Tile 좌표는 직접 수정하지 않습니다.
/// 이 컴포넌트가 없어도 전투 Logic은 정상 진행되어야 합니다.
/// </summary>
[DefaultExecutionOrder(-4000)]
[DisallowMultipleComponent]
public sealed class BattleFieldCinematicDirector : MonoBehaviour
{
    private static BattleFieldCinematicDirector instance;

    [Header("References")]
    [SerializeField] private BattleCameraController battleCamera;
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private PlayerController player;

    [Header("Field Global Light")]
    [SerializeField] private Color generatedGlobalLightColor = Color.white;
    [SerializeField, Range(0.55f, 1f)] private float combatGlobalIntensityMultiplier = 0.78f;
    [SerializeField, Min(0f)] private float globalLightBlendSharpness = 5.5f;

    [Header("Player Stage Spotlight")]
    [SerializeField] private bool enablePlayerSpotlight = true;
    [SerializeField] private Color playerSpotlightColor = new(1f, 0.94f, 0.72f, 1f);
    [SerializeField, Min(0f)] private float playerSpotlightIntensity = 1.10f;
    [SerializeField, Min(0f)] private float playerSpotlightInnerRadius = 1.45f;
    [SerializeField, Min(0.1f)] private float playerSpotlightOuterRadius = 3.15f;
    [SerializeField, Range(0f, 1f)] private float playerSpotlightFalloff = 0.28f;
    [SerializeField, Min(0f)] private float spotlightBlendSharpness = 7.5f;
    [SerializeField, Min(0f)] private float spotlightTrackingSharpness = 11f;
    [SerializeField, Min(0f)] private float spotlightLeadDistance = 0.34f;
    [SerializeField, Min(0.1f)] private float playerSpeedForFullLead = 5f;

    [Header("Combat Start Beat")]
    [SerializeField] private bool focusPlayerOnCombatStart = true;
    [SerializeField, Min(0f)] private float combatStartFocusDuration = 0.65f;
    [SerializeField, Min(0f)] private float combatStartZoom = 5.15f;

    private Light2D fieldGlobalLight;
    private Light2D playerSpotlight;
    private Transform playerSpotlightTransform;
    private Rigidbody2D playerBody;
    private Coroutine bindRoutine;
    private bool subscribed;
    private bool combatLightingRequested;
    private bool spotlightPositionInitialized;
    private float baseGlobalLightIntensity = 1f;
    private int combatStartFocusRequest;

    public static BattleFieldCinematicDirector Instance => instance;
    public bool IsFieldLightingActive => combatLightingRequested;

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
        EnsureLightingRig();
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
        RestoreLightingImmediate();
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
            EnsureLightingRig();

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

        if (player != null && playerBody == null)
            playerBody = player.GetComponent<Rigidbody2D>();

        if (transform.parent == null)
        {
            BattleSceneManager manager = FindFirstObjectByType<BattleSceneManager>();
            if (manager != null)
                transform.SetParent(manager.transform, true);
        }
    }

    private void EnsureLightingRig()
    {
        if (fieldGlobalLight == null)
            ResolveOrCreateGlobalLight();
        if (playerSpotlight == null)
            CreatePlayerSpotlight();
    }

    private void ResolveOrCreateGlobalLight()
    {
        Light2D[] lights = FindObjectsByType<Light2D>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < lights.Length; i++)
        {
            Light2D light = lights[i];
            if (light == null || light.lightType != Light2D.LightType.Global)
                continue;
            if (light.gameObject.scene != gameObject.scene)
                continue;

            fieldGlobalLight = light;
            baseGlobalLightIntensity = Mathf.Max(0f, light.intensity);
            return;
        }

        GameObject lightObject = new("FieldGlobalLight");
        lightObject.transform.SetParent(transform, false);
        // URP 17.0.3 Light2D.Awake가 현재 존재하는 Sorting Layer 전체를 자동 등록합니다.
        fieldGlobalLight = lightObject.AddComponent<Light2D>();
        fieldGlobalLight.lightType = Light2D.LightType.Global;
        fieldGlobalLight.blendStyleIndex = 0;
        fieldGlobalLight.color = generatedGlobalLightColor;
        fieldGlobalLight.intensity = 1f;
        baseGlobalLightIntensity = fieldGlobalLight.intensity;
    }

    private void CreatePlayerSpotlight()
    {
        GameObject lightObject = new("PlayerStageSpotlight");
        lightObject.transform.SetParent(transform, true);
        playerSpotlightTransform = lightObject.transform;

        // targetSortingLayers는 이 패키지 버전의 public API가 아니므로 기본 전체 Layer 등록을 사용합니다.
        playerSpotlight = lightObject.AddComponent<Light2D>();
        playerSpotlight.lightType = Light2D.LightType.Point;
        playerSpotlight.blendStyleIndex = 0;
        playerSpotlight.color = playerSpotlightColor;
        playerSpotlight.intensity = 0f;
        playerSpotlight.falloffIntensity = playerSpotlightFalloff;
        playerSpotlight.pointLightInnerAngle = 360f;
        playerSpotlight.pointLightOuterAngle = 360f;
        playerSpotlight.pointLightInnerRadius = Mathf.Max(0f, playerSpotlightInnerRadius);
        playerSpotlight.pointLightOuterRadius = Mathf.Max(
            playerSpotlight.pointLightInnerRadius + 0.1f,
            playerSpotlightOuterRadius);
        playerSpotlight.overlapOperation = Light2D.OverlapOperation.Additive;
        playerSpotlight.shadowsEnabled = false;
        playerSpotlight.volumetricEnabled = false;
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
        bool combat = runManager != null && runManager.RunActive && state == BattleRunState.Combat;
        combatLightingRequested = combat;

        if (combat)
        {
            InitializeSpotlightPosition();
            return;
        }

        spotlightPositionInitialized = false;
        if (state == BattleRunState.Reward || state == BattleRunState.SelectingNode)
            RestoreLightingImmediate();
        ReleaseCombatStartFocus();
        battleCamera?.ReleaseAllFieldFocus();
    }

    private void HandleRoomCombatStarted(RoomDefinitionSO _)
    {
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
        ReleaseCombatStartFocus();
        battleCamera?.ReleaseAllFieldFocus();
    }

    private void LateUpdate()
    {
        if (fieldGlobalLight == null || playerSpotlight == null)
            return;

        bool playerAvailable = player != null && player.IsAlive && player.gameObject.activeInHierarchy;
        bool spotlightActive = enablePlayerSpotlight && combatLightingRequested && playerAvailable;

        float globalTarget = combatLightingRequested
            ? baseGlobalLightIntensity * Mathf.Clamp(combatGlobalIntensityMultiplier, 0.55f, 1f)
            : baseGlobalLightIntensity;
        float globalT = ExponentialT(globalLightBlendSharpness);
        fieldGlobalLight.intensity = Mathf.Lerp(fieldGlobalLight.intensity, globalTarget, globalT);

        float spotlightTarget = spotlightActive ? Mathf.Max(0f, playerSpotlightIntensity) : 0f;
        float spotlightT = ExponentialT(spotlightBlendSharpness);
        playerSpotlight.intensity = Mathf.Lerp(playerSpotlight.intensity, spotlightTarget, spotlightT);

        if (!spotlightActive)
        {
            if (playerSpotlight.intensity < 0.001f)
                playerSpotlight.intensity = 0f;
            return;
        }

        InitializeSpotlightPosition();
        Vector2 targetPosition = ResolvePlayerSpotlightPosition();
        float trackingT = ExponentialT(spotlightTrackingSharpness);
        Vector3 current = playerSpotlightTransform.position;
        playerSpotlightTransform.position = new Vector3(
            Mathf.Lerp(current.x, targetPosition.x, trackingT),
            Mathf.Lerp(current.y, targetPosition.y, trackingT),
            player.transform.position.z);
    }

    private void InitializeSpotlightPosition()
    {
        if (spotlightPositionInitialized || playerSpotlightTransform == null || player == null)
            return;

        playerSpotlightTransform.position = player.transform.position;
        spotlightPositionInitialized = true;
    }

    private Vector2 ResolvePlayerSpotlightPosition()
    {
        Vector2 position = player != null ? (Vector2)player.transform.position : Vector2.zero;
        if (playerBody == null)
            return position;

        Vector2 velocity = playerBody.linearVelocity;
        float speed = velocity.magnitude;
        if (speed <= 0.01f)
            return position;

        float amount = Mathf.Clamp01(speed / Mathf.Max(0.1f, playerSpeedForFullLead));
        return position + velocity.normalized * (spotlightLeadDistance * amount);
    }

    private float ExponentialT(float sharpness)
    {
        return 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * Time.unscaledDeltaTime);
    }

    private void ReleaseCombatStartFocus()
    {
        if (combatStartFocusRequest == 0)
            return;

        battleCamera?.ReleaseFocus(combatStartFocusRequest);
        combatStartFocusRequest = 0;
    }

    private void RestoreLightingImmediate()
    {
        combatLightingRequested = false;
        spotlightPositionInitialized = false;

        if (fieldGlobalLight != null)
            fieldGlobalLight.intensity = baseGlobalLightIntensity;
        if (playerSpotlight != null)
            playerSpotlight.intensity = 0f;
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

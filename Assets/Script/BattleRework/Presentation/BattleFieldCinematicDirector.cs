using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 전투 Logic과 분리된 Field 연출 진입점입니다.
///
/// 현재 1차 책임:
/// - BattleRunState에 맞춰 Combat / Show Field 조명을 전환
/// - Player와 Presenter를 따라가는 상부 무대형 Point Light 2D
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
    [SerializeField] private BattleShowWorldSetController showWorldSet;

    [Header("Field Global Light")]
    [SerializeField] private Color generatedGlobalLightColor = Color.white;
    [SerializeField, Range(0.55f, 1f)] private float combatGlobalIntensityMultiplier = 0.78f;
    [SerializeField, Min(0f)] private float globalLightBlendSharpness = 5.5f;

    [Header("Player Stage Spotlight")]
    [SerializeField] private bool enablePlayerSpotlight = true;
    [SerializeField] private Color playerSpotlightColor = new(1f, 0.94f, 0.72f, 1f);
    [SerializeField, Min(0f)] private float playerSpotlightIntensity = 1.10f;
    [SerializeField, Min(0f)] private float showPlayerSpotlightIntensity = 1.25f;
    [SerializeField] private Vector2 playerSpotlightSourceOffset = new(0f, 5.6f);
    [SerializeField, Range(0f, 360f)] private float playerSpotlightInnerAngle = 18f;
    [SerializeField, Range(0f, 360f)] private float playerSpotlightOuterAngle = 38f;
    [SerializeField, Min(0f)] private float playerSpotlightInnerRadius = 0.20f;
    [SerializeField, Min(0.1f)] private float playerSpotlightOuterRadius = 8.2f;
    [SerializeField] private float spotlightAngleOffset = -90f;
    [SerializeField, Range(0f, 1f)] private float playerSpotlightFalloff = 0.28f;
    [SerializeField, Min(0f)] private float spotlightBlendSharpness = 7.5f;
    [SerializeField, Min(0f)] private float spotlightTrackingSharpness = 11f;
    [SerializeField, Min(0f)] private float spotlightLeadDistance = 0.34f;
    [SerializeField, Min(0.1f)] private float playerSpeedForFullLead = 5f;

    [Header("Reward / Map Show Lighting")]
    [SerializeField, Range(0f, 0.35f)] private float showGlobalIntensityMultiplier = 0.025f;
    [SerializeField] private Color showFloorWashColor = new(0.58f, 0.60f, 0.64f, 1f);
    [SerializeField, Min(0f)] private float showFloorWashIntensity = 0.20f;
    [SerializeField, Min(0f)] private float showFloorWashInnerRadius = 2.6f;
    [SerializeField, Min(0.1f)] private float showFloorWashOuterRadius = 11.5f;
    [SerializeField, Range(0f, 1f)] private float showFloorWashFalloff = 0.88f;
    [SerializeField] private Color showPresenterSpotlightColor = new(1f, 0.88f, 0.66f, 1f);
    [SerializeField, Min(0f)] private float showPresenterSpotlightIntensity = 1.25f;
    [SerializeField] private Vector2 showPresenterLightSourceOffset = new(0f, 5.4f);
    [SerializeField, Min(0f)] private float showBackgroundBlendSharpness = 4.5f;
    [SerializeField] private Color showCameraBackgroundColor = Color.black;

    [Header("Combat Start Beat")]
    [SerializeField] private bool focusPlayerOnCombatStart = true;
    [SerializeField, Min(0f)] private float combatStartFocusDuration = 0.65f;
    [SerializeField, Min(0f)] private float combatStartZoom = 5.15f;

    private Light2D fieldGlobalLight;
    private Light2D playerSpotlight;
    private Transform playerSpotlightTransform;
    private Light2D showPresenterSpotlight;
    private Transform showPresenterSpotlightTransform;
    private Light2D showFloorWash;
    private Transform showFloorWashTransform;
    private Rigidbody2D playerBody;
    private Camera sceneCamera;
    private Coroutine bindRoutine;
    private bool subscribed;
    private bool combatLightingRequested;
    private bool showLightingRequested;
    private bool spotlightPositionInitialized;
    private bool presenterSpotlightPositionInitialized;
    private bool cameraBackgroundCaptured;
    private float baseGlobalLightIntensity = 1f;
    private Color baseCameraBackgroundColor;
    private int combatStartFocusRequest;

    public static BattleFieldCinematicDirector Instance => instance;
    public bool IsFieldLightingActive => combatLightingRequested || showLightingRequested;

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

            if (battleCamera != null && runManager != null && roomManager != null &&
                player != null && showWorldSet != null)
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
        if (showWorldSet == null)
            showWorldSet = FindFirstObjectByType<BattleShowWorldSetController>();

        if (player != null && playerBody == null)
            playerBody = player.GetComponent<Rigidbody2D>();

        if (sceneCamera == null)
        {
            sceneCamera = Camera.main;
            if (sceneCamera != null && !cameraBackgroundCaptured)
            {
                baseCameraBackgroundColor = sceneCamera.backgroundColor;
                cameraBackgroundCaptured = true;
            }
        }

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
        if (showPresenterSpotlight == null)
            CreateShowPresenterSpotlight();
        if (showFloorWash == null)
            CreateShowFloorWash();
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
        playerSpotlight.pointLightInnerAngle = Mathf.Min(playerSpotlightInnerAngle, playerSpotlightOuterAngle);
        playerSpotlight.pointLightOuterAngle = Mathf.Max(playerSpotlightInnerAngle, playerSpotlightOuterAngle);
        playerSpotlight.pointLightInnerRadius = Mathf.Max(0f, playerSpotlightInnerRadius);
        playerSpotlight.pointLightOuterRadius = Mathf.Max(
            playerSpotlight.pointLightInnerRadius + 0.1f,
            playerSpotlightOuterRadius);
        playerSpotlight.overlapOperation = Light2D.OverlapOperation.Additive;
        playerSpotlight.shadowsEnabled = false;
        playerSpotlight.volumetricEnabled = true;
        playerSpotlightTransform.rotation = Quaternion.Euler(0f, 0f, spotlightAngleOffset);
    }

    private void CreateShowPresenterSpotlight()
    {
        GameObject lightObject = new("ShowPresenterStageSpotlight");
        lightObject.transform.SetParent(transform, true);
        showPresenterSpotlightTransform = lightObject.transform;

        showPresenterSpotlight = lightObject.AddComponent<Light2D>();
        showPresenterSpotlight.lightType = Light2D.LightType.Point;
        showPresenterSpotlight.blendStyleIndex = 0;
        showPresenterSpotlight.color = showPresenterSpotlightColor;
        showPresenterSpotlight.intensity = 0f;
        showPresenterSpotlight.falloffIntensity = playerSpotlightFalloff;
        showPresenterSpotlight.pointLightInnerAngle = Mathf.Min(playerSpotlightInnerAngle, playerSpotlightOuterAngle);
        showPresenterSpotlight.pointLightOuterAngle = Mathf.Max(playerSpotlightInnerAngle, playerSpotlightOuterAngle);
        showPresenterSpotlight.pointLightInnerRadius = Mathf.Max(0f, playerSpotlightInnerRadius);
        showPresenterSpotlight.pointLightOuterRadius = Mathf.Max(
            showPresenterSpotlight.pointLightInnerRadius + 0.1f,
            playerSpotlightOuterRadius);
        showPresenterSpotlight.overlapOperation = Light2D.OverlapOperation.Additive;
        showPresenterSpotlight.shadowsEnabled = false;
        showPresenterSpotlight.volumetricEnabled = true;
        showPresenterSpotlightTransform.rotation = Quaternion.Euler(0f, 0f, spotlightAngleOffset);
    }

    private void CreateShowFloorWash()
    {
        GameObject lightObject = new("ShowFloorWash");
        lightObject.transform.SetParent(transform, true);
        showFloorWashTransform = lightObject.transform;

        showFloorWash = lightObject.AddComponent<Light2D>();
        showFloorWash.lightType = Light2D.LightType.Point;
        showFloorWash.blendStyleIndex = 0;
        showFloorWash.color = showFloorWashColor;
        showFloorWash.intensity = 0f;
        showFloorWash.falloffIntensity = showFloorWashFalloff;
        showFloorWash.pointLightInnerAngle = 360f;
        showFloorWash.pointLightOuterAngle = 360f;
        showFloorWash.pointLightInnerRadius = Mathf.Max(0f, showFloorWashInnerRadius);
        showFloorWash.pointLightOuterRadius = Mathf.Max(
            showFloorWash.pointLightInnerRadius + 0.1f,
            showFloorWashOuterRadius);
        showFloorWash.overlapOperation = Light2D.OverlapOperation.Additive;
        showFloorWash.shadowsEnabled = false;
        showFloorWash.volumetricEnabled = false;
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
        bool show = runManager != null && runManager.RunActive &&
                    (state == BattleRunState.Reward || state == BattleRunState.SelectingNode);
        combatLightingRequested = combat;
        showLightingRequested = show;

        if (combat)
        {
            InitializeSpotlightPosition();
            presenterSpotlightPositionInitialized = false;
            return;
        }

        spotlightPositionInitialized = false;
        presenterSpotlightPositionInitialized = false;
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
        if (fieldGlobalLight == null || playerSpotlight == null ||
            showPresenterSpotlight == null || showFloorWash == null)
            return;

        bool playerAvailable = player != null && player.IsAlive && player.gameObject.activeInHierarchy;
        bool spotlightActive = enablePlayerSpotlight &&
                               (combatLightingRequested || showLightingRequested) &&
                               playerAvailable;
        Transform presenterTarget = showWorldSet != null ? showWorldSet.PresenterWorldTransform : null;
        bool presenterSpotlightActive = showLightingRequested && presenterTarget != null;

        float globalTarget = baseGlobalLightIntensity;
        if (showLightingRequested)
            globalTarget *= Mathf.Clamp(showGlobalIntensityMultiplier, 0f, 0.35f);
        else if (combatLightingRequested)
            globalTarget *= Mathf.Clamp(combatGlobalIntensityMultiplier, 0.55f, 1f);

        float globalT = ExponentialT(globalLightBlendSharpness);
        fieldGlobalLight.intensity = Mathf.Lerp(fieldGlobalLight.intensity, globalTarget, globalT);

        float requestedPlayerIntensity = showLightingRequested
            ? showPlayerSpotlightIntensity
            : playerSpotlightIntensity;
        float spotlightTarget = spotlightActive ? Mathf.Max(0f, requestedPlayerIntensity) : 0f;
        float presenterSpotlightTarget = presenterSpotlightActive
            ? Mathf.Max(0f, showPresenterSpotlightIntensity)
            : 0f;
        float spotlightT = ExponentialT(spotlightBlendSharpness);
        playerSpotlight.intensity = Mathf.Lerp(playerSpotlight.intensity, spotlightTarget, spotlightT);
        showPresenterSpotlight.intensity = Mathf.Lerp(
            showPresenterSpotlight.intensity,
            presenterSpotlightTarget,
            spotlightT);
        float floorWashTarget = showLightingRequested ? Mathf.Max(0f, showFloorWashIntensity) : 0f;
        showFloorWash.intensity = Mathf.Lerp(showFloorWash.intensity, floorWashTarget, spotlightT);

        UpdateShowCameraBackground();
        UpdateShowFloorWashPosition();

        if (presenterSpotlightActive)
            UpdatePresenterSpotlight(presenterTarget);
        else
            presenterSpotlightPositionInitialized = false;

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

        Vector2 position = ResolvePlayerSpotlightPosition();
        playerSpotlightTransform.position = new Vector3(position.x, position.y, player.transform.position.z);
        spotlightPositionInitialized = true;
    }

    private Vector2 ResolvePlayerSpotlightPosition()
    {
        Vector2 position = player != null ? (Vector2)player.transform.position : Vector2.zero;
        Vector2 lead = Vector2.zero;

        if (playerBody != null)
        {
            Vector2 velocity = playerBody.linearVelocity;
            float speed = velocity.magnitude;
            if (speed > 0.01f)
            {
                float amount = Mathf.Clamp01(speed / Mathf.Max(0.1f, playerSpeedForFullLead));
                lead = velocity.normalized * (spotlightLeadDistance * amount);
            }
        }

        return position + lead + playerSpotlightSourceOffset;
    }

    private void UpdatePresenterSpotlight(Transform presenterTarget)
    {
        if (showPresenterSpotlightTransform == null || presenterTarget == null)
            return;

        Vector2 targetPosition = (Vector2)presenterTarget.position + showPresenterLightSourceOffset;
        if (!presenterSpotlightPositionInitialized)
        {
            showPresenterSpotlightTransform.position = new Vector3(
                targetPosition.x,
                targetPosition.y,
                presenterTarget.position.z);
            presenterSpotlightPositionInitialized = true;
            return;
        }

        float trackingT = ExponentialT(spotlightTrackingSharpness);
        Vector3 current = showPresenterSpotlightTransform.position;
        showPresenterSpotlightTransform.position = new Vector3(
            Mathf.Lerp(current.x, targetPosition.x, trackingT),
            Mathf.Lerp(current.y, targetPosition.y, trackingT),
            presenterTarget.position.z);
    }

    private void UpdateShowCameraBackground()
    {
        if (sceneCamera == null || !cameraBackgroundCaptured)
            return;

        Color target = showLightingRequested ? showCameraBackgroundColor : baseCameraBackgroundColor;
        sceneCamera.backgroundColor = Color.Lerp(
            sceneCamera.backgroundColor,
            target,
            ExponentialT(showBackgroundBlendSharpness));
    }

    private void UpdateShowFloorWashPosition()
    {
        if (!showLightingRequested || showFloorWashTransform == null || showWorldSet == null)
            return;

        Vector3 target = showWorldSet.HasCameraAnchor
            ? showWorldSet.CameraTargetWorld
            : player != null
                ? player.transform.position
                : showWorldSet.CameraTargetWorld;
        if (player != null)
            target.y = player.transform.position.y;
        showFloorWashTransform.position = target;
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
        showLightingRequested = false;
        spotlightPositionInitialized = false;
        presenterSpotlightPositionInitialized = false;

        if (fieldGlobalLight != null)
            fieldGlobalLight.intensity = baseGlobalLightIntensity;
        if (playerSpotlight != null)
            playerSpotlight.intensity = 0f;
        if (showPresenterSpotlight != null)
            showPresenterSpotlight.intensity = 0f;
        if (showFloorWash != null)
            showFloorWash.intensity = 0f;
        if (sceneCamera != null && cameraBackgroundCaptured)
            sceneCamera.backgroundColor = baseCameraBackgroundColor;
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

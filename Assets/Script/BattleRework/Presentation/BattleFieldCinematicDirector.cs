using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 전투 Logic과 분리된 Field / Show 카메라 연출 진입점입니다.
///
/// 최종 조명 규칙:
/// - Normal Battle에서는 Presentation 조명을 사용하지 않습니다.
/// - Reward / Map Selection에서만 Player / Presenter에 360도 원형 Light Pool을 사용합니다.
/// - Screen / TV에는 Light2D를 만들지 않습니다. Screen 노출은 BattleShowFocusController의 Rect Focus가 담당합니다.
/// - Field Global Light와 Camera Background는 건드리지 않습니다.
///
/// Camera Transform, Player, Monster, Tile 좌표는 직접 수정하지 않습니다.
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

    [Header("Character Circle Spotlight")]
    [SerializeField] private bool enableCharacterSpotlights = true;
    [SerializeField] private Color playerSpotlightColor = new(1f, 0.94f, 0.72f, 1f);
    [SerializeField] private Color presenterSpotlightColor = new(1f, 0.88f, 0.66f, 1f);
    [SerializeField, Min(0f)] private float playerSpotlightIntensity = 0.85f;
    [SerializeField, Min(0f)] private float presenterSpotlightIntensity = 0.85f;
    [SerializeField, Min(0.1f)] private float playerSpotlightOuterRadius = 2.15f;
    [SerializeField, Min(0.1f)] private float presenterSpotlightOuterRadius = 2.35f;
    [SerializeField, Range(0f, 1f)] private float spotlightFalloff = 0.62f;
    [SerializeField, Min(0f)] private float spotlightBlendSharpness = 8.5f;
    [SerializeField, Min(0f)] private float spotlightTrackingSharpness = 14f;
    [SerializeField] private Vector2 playerSpotlightOffset = new(0f, -0.10f);
    [SerializeField] private Vector2 presenterSpotlightOffset = new(0f, -0.10f);

    [Header("Combat Start Beat")]
    [SerializeField] private bool focusPlayerOnCombatStart = true;
    [SerializeField, Min(0f)] private float combatStartFocusDuration = 0.65f;
    [SerializeField, Min(0f)] private float combatStartZoom = 5.15f;

    private Light2D playerSpotlight;
    private Transform playerSpotlightTransform;
    private Light2D presenterSpotlight;
    private Transform presenterSpotlightTransform;

    private Coroutine bindRoutine;
    private bool subscribed;
    private bool showLightingRequested;
    private bool playerPositionInitialized;
    private bool presenterPositionInitialized;
    private int combatStartFocusRequest;

    public static BattleFieldCinematicDirector Instance => instance;
    public bool IsFieldLightingActive => showLightingRequested;

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

        if (transform.parent == null)
        {
            BattleSceneManager manager = FindFirstObjectByType<BattleSceneManager>();
            if (manager != null)
                transform.SetParent(manager.transform, true);
        }
    }

    private void EnsureLightingRig()
    {
        if (playerSpotlight == null)
            CreateCircleSpotlight(
                "PlayerShowCircleSpotlight",
                playerSpotlightColor,
                playerSpotlightOuterRadius,
                out playerSpotlight,
                out playerSpotlightTransform);

        if (presenterSpotlight == null)
            CreateCircleSpotlight(
                "PresenterShowCircleSpotlight",
                presenterSpotlightColor,
                presenterSpotlightOuterRadius,
                out presenterSpotlight,
                out presenterSpotlightTransform);
    }

    private void CreateCircleSpotlight(
        string objectName,
        Color color,
        float outerRadius,
        out Light2D light,
        out Transform lightTransform)
    {
        GameObject lightObject = new(objectName);
        lightObject.transform.SetParent(transform, true);
        lightTransform = lightObject.transform;

        light = lightObject.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.blendStyleIndex = 0;
        light.color = color;
        light.intensity = 0f;
        light.falloffIntensity = spotlightFalloff;

        // 360도 Point Light만 사용합니다.
        // 천장 -> 대상 방향의 원뿔 / 빛기둥은 만들지 않습니다.
        light.pointLightInnerAngle = 360f;
        light.pointLightOuterAngle = 360f;
        light.pointLightInnerRadius = Mathf.Max(0.05f, outerRadius * 0.36f);
        light.pointLightOuterRadius = Mathf.Max(light.pointLightInnerRadius + 0.1f, outerRadius);
        light.overlapOperation = Light2D.OverlapOperation.Additive;
        light.shadowsEnabled = false;
        light.volumetricEnabled = false;
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
        showLightingRequested = runManager != null && runManager.RunActive &&
                                (state == BattleRunState.Reward || state == BattleRunState.SelectingNode);

        if (!showLightingRequested)
        {
            playerPositionInitialized = false;
            presenterPositionInitialized = false;
        }

        // 일반 전투에는 Spotlight를 사용하지 않지만, 기존 Combat Start Camera Beat는 유지합니다.
        if (!combat)
        {
            ReleaseCombatStartFocus();
            battleCamera?.ReleaseAllFieldFocus();
        }
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
        ReleaseCombatStartFocus();
        battleCamera?.ReleaseAllFieldFocus();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        EnsureLightingRig();

        if (playerSpotlight == null || presenterSpotlight == null)
            return;

        bool playerActive = enableCharacterSpotlights && showLightingRequested &&
                            player != null && player.IsAlive && player.gameObject.activeInHierarchy;

        Transform presenterTarget = showWorldSet != null
            ? showWorldSet.PresenterWorldTransform
            : null;
        bool presenterActive = enableCharacterSpotlights && showLightingRequested &&
                               presenterTarget != null && presenterTarget.gameObject.activeInHierarchy;

        float intensityT = ExponentialT(spotlightBlendSharpness);
        float playerTargetIntensity = playerActive ? Mathf.Max(0f, playerSpotlightIntensity) : 0f;
        float presenterTargetIntensity = presenterActive ? Mathf.Max(0f, presenterSpotlightIntensity) : 0f;

        playerSpotlight.intensity = Mathf.Lerp(
            playerSpotlight.intensity,
            playerTargetIntensity,
            intensityT);
        presenterSpotlight.intensity = Mathf.Lerp(
            presenterSpotlight.intensity,
            presenterTargetIntensity,
            intensityT);

        if (playerSpotlight.intensity < 0.001f && !playerActive)
            playerSpotlight.intensity = 0f;
        if (presenterSpotlight.intensity < 0.001f && !presenterActive)
            presenterSpotlight.intensity = 0f;

        if (playerActive)
            UpdateSpotlightPosition(
                playerSpotlightTransform,
                player.transform,
                playerSpotlightOffset,
                ref playerPositionInitialized);
        else
            playerPositionInitialized = false;

        if (presenterActive)
            UpdateSpotlightPosition(
                presenterSpotlightTransform,
                presenterTarget,
                presenterSpotlightOffset,
                ref presenterPositionInitialized);
        else
            presenterPositionInitialized = false;
    }

    private void UpdateSpotlightPosition(
        Transform lightTransform,
        Transform target,
        Vector2 offset,
        ref bool initialized)
    {
        if (lightTransform == null || target == null)
            return;

        Vector3 desired = target.position + (Vector3)offset;
        if (!initialized)
        {
            lightTransform.position = desired;
            initialized = true;
            return;
        }

        float trackingT = ExponentialT(spotlightTrackingSharpness);
        Vector3 current = lightTransform.position;
        lightTransform.position = new Vector3(
            Mathf.Lerp(current.x, desired.x, trackingT),
            Mathf.Lerp(current.y, desired.y, trackingT),
            desired.z);
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
        showLightingRequested = false;
        playerPositionInitialized = false;
        presenterPositionInitialized = false;

        if (playerSpotlight != null)
            playerSpotlight.intensity = 0f;
        if (presenterSpotlight != null)
            presenterSpotlight.intensity = 0f;
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

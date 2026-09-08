using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

/// <summary>
/// 일반 전투에서는 Player를 따라가고, Reward / Map에서는 BattleShowWorldSetController의
/// 단일 Camera Anchor를 사용합니다. Reward와 Map의 카메라 위치/크기를 따로 계산하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
public class BattleCameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera controlledCamera;
    [SerializeField] private Transform followTarget;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleShowWorldSetController showStage;
    [SerializeField] private Transform movementRoot;

    [Header("Player Follow")]
    [SerializeField, Min(0f)] private float followSharpness = 11f;
    [SerializeField, Min(0f)] private float returnFromInspectionSharpness = 7f;

    [Header("Mouse Wheel Zoom")]
    [SerializeField, Min(0.1f)] private float minZoom = 4.2f;
    [SerializeField, Min(0.1f)] private float maxZoom = 9.5f;
    [SerializeField, Min(0.05f)] private float zoomStep = 0.8f;
    [SerializeField, Min(0f)] private float zoomSharpness = 12f;

    [Header("Shared Show Camera")]
    [FormerlySerializedAs("rewardShowSharpness")]
    [SerializeField, Min(0f)] private float showFollowSharpness = 7.2f;
    [SerializeField, Min(0.5f)] private float showTransitionSharpness = 3.2f;
    [SerializeField, Min(1f)] private float showTransitionMaxSpeed = 14f;

    [Header("Shared TV Cursor Tracking")]
    [FormerlySerializedAs("mapCursorPanDistance")]
    [SerializeField] private Vector2 showCursorPanDistance = new(2.45f, 1.35f);
    [FormerlySerializedAs("mapCursorTrackingSharpness")]
    [SerializeField, Min(1f)] private float showCursorTrackingSharpness = 5.6f;

    [Header("Map Inspection")]
    [SerializeField] private int inspectionMouseButton = 2;
    [SerializeField, Min(0f)] private float inspectionPanMultiplier = 1f;
    [SerializeField, Min(0f)] private float maxInspectionDistance = 12f;
    [SerializeField] private KeyCode recenterKey = KeyCode.F;

    private Vector2 panOffset;
    private Vector2 previousMousePosition;
    private float targetZoom;
    private float zoomBeforeShow;
    private bool inspecting;
    private bool initialized;
    private bool rigResolved;
    private bool showFraming;
    private float showBlend;

    private bool showCursorTracking;
    private Vector2 requestedShowCursorDirection;
    private Vector2 currentShowCursorPan;

    private float selectionShakeStartedAt = -1f;
    private float selectionShakeDuration;
    private float selectionShakeAmplitude;
    private Vector2 lastSelectionShakeOffset;

    public float CurrentZoom => controlledCamera != null ? controlledCamera.orthographicSize : 0f;
    public bool IsInspecting => inspecting;
    public bool IsRewardFraming => showFraming;

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

        if (!battleScene || Object.FindFirstObjectByType<BattleCameraController>() != null)
            return;

        GameObject host = new("BattleCameraRuntime");
        host.AddComponent<BattleCameraController>();
    }

    private void Awake()
    {
        ResolveReferences();
        InitializeState();
    }

    private void OnEnable()
    {
        ResolveReferences();
        InitializeState();
    }

    private void OnDisable()
    {
        ClearSelectionShake();
        SetShowCursorTracking(false, Vector2.zero);
    }

    public void Configure(Camera camera, Transform target, BattleRoomManager manager)
    {
        if (camera != null)
            controlledCamera = camera;
        if (target != null)
            followTarget = target;
        if (manager != null)
            roomManager = manager;

        rigResolved = false;
        ResolveMovementRoot();
        InitializeState();
    }

    public void PlaySelectionConfirmShake(float amplitude, float duration)
    {
        selectionShakeAmplitude = Mathf.Max(0f, amplitude);
        selectionShakeDuration = Mathf.Max(0.05f, duration);
        selectionShakeStartedAt = Time.unscaledTime;
    }

    /// <summary>Reward / Map 공용 TV 커서 추적 입력입니다.</summary>
    public void SetShowCursorTracking(bool active, Vector2 normalizedDirection)
    {
        showCursorTracking = active;
        requestedShowCursorDirection = active
            ? new Vector2(
                Mathf.Clamp(normalizedDirection.x, -1f, 1f),
                Mathf.Clamp(normalizedDirection.y, -1f, 1f))
            : Vector2.zero;
    }

    /// <summary>
    /// 기존 BattleSpatialMapController 호환용입니다. 실제 처리는 공용 TV 추적으로 통합됩니다.
    /// </summary>
    public void SetMapCursorTracking(bool active, Vector2 normalizedDirection)
    {
        SetShowCursorTracking(active, normalizedDirection);
    }

    private void ResolveReferences()
    {
        if (controlledCamera == null)
            controlledCamera = Camera.main;

        if (followTarget == null)
        {
            PlayerController player = FindFirstObjectByType<PlayerController>();
            if (player != null)
                followTarget = player.transform;
        }

        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (showStage == null)
            showStage = FindFirstObjectByType<BattleShowWorldSetController>();

        BattleSceneManager manager = FindFirstObjectByType<BattleSceneManager>();
        if (manager != null && transform.parent == null)
            transform.SetParent(manager.transform, true);

        ResolveMovementRoot();
    }

    private void ResolveMovementRoot()
    {
        if (controlledCamera == null || rigResolved)
            return;

        Transform cameraTransform = controlledCamera.transform;
        Transform shakePivot = cameraTransform.parent != null && cameraTransform.parent.name == "CameraShakePivot"
            ? cameraTransform.parent
            : null;

        if (shakePivot == null)
        {
            movementRoot = cameraTransform;
            rigResolved = true;
            return;
        }

        if (shakePivot.parent != null && shakePivot.parent.name == "CameraRig")
        {
            movementRoot = shakePivot.parent;
            rigResolved = true;
            return;
        }

        Transform oldParent = shakePivot.parent;
        GameObject rigObject = new("CameraRig");
        Transform rig = rigObject.transform;
        rig.position = shakePivot.position;
        rig.rotation = Quaternion.identity;
        rig.localScale = Vector3.one;
        if (oldParent != null)
            rig.SetParent(oldParent, true);

        shakePivot.SetParent(rig, true);
        movementRoot = rig;
        rigResolved = true;
    }

    private void InitializeState()
    {
        if (initialized || controlledCamera == null)
            return;

        controlledCamera.orthographic = true;
        minZoom = Mathf.Max(0.1f, minZoom);
        maxZoom = Mathf.Max(minZoom, maxZoom);
        targetZoom = Mathf.Clamp(controlledCamera.orthographicSize, minZoom, maxZoom);
        zoomBeforeShow = targetZoom;
        initialized = true;
    }

    private void Update()
    {
        ResolveReferences();
        if (controlledCamera == null)
            return;

        UpdateShowMode();

        if (showFraming)
        {
            inspecting = false;
            panOffset = Vector2.zero;
            return;
        }

        bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (!pointerOverUi)
            HandleZoomInput();

        HandleInspectionInput(pointerOverUi);

        if (Input.GetKeyDown(recenterKey))
        {
            panOffset = Vector2.zero;
            inspecting = false;
            SnapToPlayer();
        }
    }

    private void UpdateShowMode()
    {
        bool shouldShow = runManager != null &&
                          (runManager.State == BattleRunState.Reward ||
                           runManager.State == BattleRunState.SelectingNode);

        if (shouldShow == showFraming)
            return;

        showFraming = shouldShow;
        inspecting = false;
        panOffset = Vector2.zero;

        if (showFraming)
        {
            zoomBeforeShow = targetZoom;
        }
        else
        {
            targetZoom = Mathf.Clamp(zoomBeforeShow, minZoom, maxZoom);
            SetShowCursorTracking(false, Vector2.zero);
        }
    }

    private void HandleZoomInput()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) <= 0.001f)
            return;

        targetZoom = Mathf.Clamp(targetZoom - scroll * zoomStep, minZoom, maxZoom);
    }

    private void HandleInspectionInput(bool pointerOverUi)
    {
        if (Input.GetMouseButtonDown(inspectionMouseButton) && !pointerOverUi)
        {
            inspecting = true;
            previousMousePosition = Input.mousePosition;
        }

        if (Input.GetMouseButtonUp(inspectionMouseButton))
            inspecting = false;
        if (!inspecting)
            return;

        Vector2 currentMouse = Input.mousePosition;
        Vector2 pixelDelta = currentMouse - previousMousePosition;
        previousMousePosition = currentMouse;

        float screenHeight = Mathf.Max(1f, Screen.height);
        float worldPerPixel = controlledCamera.orthographicSize * 2f / screenHeight;
        panOffset -= pixelDelta * worldPerPixel * inspectionPanMultiplier;

        if (maxInspectionDistance > 0f)
            panOffset = Vector2.ClampMagnitude(panOffset, maxInspectionDistance);
    }

    private void LateUpdate()
    {
        if (controlledCamera == null || movementRoot == null)
            return;

        if (followTarget == null)
        {
            ResolveReferences();
            if (followTarget == null)
                return;
        }

        float targetBlend = showFraming ? 1f : 0f;
        float blendT = 1f - Mathf.Exp(-Mathf.Max(0.5f, showTransitionSharpness) * Time.unscaledDeltaTime);
        showBlend = Mathf.Lerp(showBlend, targetBlend, blendT);
        if (Mathf.Abs(showBlend - targetBlend) < 0.001f)
            showBlend = targetBlend;

        float cursorT = 1f - Mathf.Exp(-Mathf.Max(1f, showCursorTrackingSharpness) * Time.unscaledDeltaTime);
        Vector2 targetCursorPan = showFraming && showCursorTracking
            ? Vector2.Scale(requestedShowCursorDirection, showCursorPanDistance)
            : Vector2.zero;
        currentShowCursorPan = Vector2.Lerp(currentShowCursorPan, targetCursorPan, cursorT);

        float normalZoom = targetZoom;
        float showZoom = showStage != null && showStage.HasCameraAnchor
            ? showStage.ShowCameraSize
            : normalZoom;
        float desiredZoom = Mathf.Lerp(normalZoom, Mathf.Clamp(showZoom, minZoom, maxZoom), showBlend);
        float zoomSpeed = Mathf.Lerp(zoomSharpness, showFollowSharpness, showBlend);
        float zoomT = 1f - Mathf.Exp(-Mathf.Max(0f, zoomSpeed) * Time.unscaledDeltaTime);
        controlledCamera.orthographicSize = Mathf.Lerp(controlledCamera.orthographicSize, desiredZoom, zoomT);

        if (!showFraming && !inspecting && panOffset.sqrMagnitude > 0.0001f)
        {
            float returnT = 1f - Mathf.Exp(-returnFromInspectionSharpness * Time.unscaledDeltaTime);
            panOffset = Vector2.Lerp(panOffset, Vector2.zero, returnT);
            if (panOffset.sqrMagnitude < 0.0001f)
                panOffset = Vector2.zero;
        }

        Vector2 normalTarget = (Vector2)followTarget.position + panOffset;
        Vector2 showTarget = showStage != null && showStage.HasCameraAnchor
            ? (Vector2)showStage.CameraTargetWorld + currentShowCursorPan
            : normalTarget;
        Vector2 desiredTarget = Vector2.Lerp(normalTarget, showTarget, showBlend);

        Vector3 current = movementRoot.position;
        Vector2 unshakenCurrent = (Vector2)current - lastSelectionShakeOffset;
        float followSpeed = Mathf.Lerp(followSharpness, showFollowSharpness, showBlend);
        float followT = !showFraming && inspecting
            ? 1f
            : 1f - Mathf.Exp(-Mathf.Max(0f, followSpeed) * Time.unscaledDeltaTime);
        Vector2 next = Vector2.Lerp(unshakenCurrent, desiredTarget, followT);

        if ((showFraming || showBlend > 0f) && showTransitionMaxSpeed > 0f)
        {
            next = Vector2.MoveTowards(
                unshakenCurrent,
                next,
                showTransitionMaxSpeed * Time.unscaledDeltaTime);
        }

        lastSelectionShakeOffset = EvaluateSelectionShakeOffset();
        Vector2 shaken = next + lastSelectionShakeOffset;
        movementRoot.position = new Vector3(shaken.x, shaken.y, current.z);
    }

    private Vector2 EvaluateSelectionShakeOffset()
    {
        if (selectionShakeStartedAt < 0f || selectionShakeAmplitude <= 0f)
            return Vector2.zero;

        float elapsed = Time.unscaledTime - selectionShakeStartedAt;
        float duration = Mathf.Max(0.05f, selectionShakeDuration);
        if (elapsed >= duration)
        {
            selectionShakeStartedAt = -1f;
            selectionShakeAmplitude = 0f;
            return Vector2.zero;
        }

        float t = Mathf.Clamp01(elapsed / duration);
        float decay = (1f - t) * (1f - t);
        float x = Mathf.Sin(elapsed * 83f) + Mathf.Sin(elapsed * 137f + 0.55f) * 0.38f;
        float y = Mathf.Sin(elapsed * 109f + 1.1f) + Mathf.Sin(elapsed * 151f) * 0.32f;
        return new Vector2(x, y * 0.72f) * (selectionShakeAmplitude * decay);
    }

    private void ClearSelectionShake()
    {
        if (movementRoot != null && lastSelectionShakeOffset.sqrMagnitude > 0f)
        {
            Vector3 position = movementRoot.position;
            movementRoot.position = new Vector3(
                position.x - lastSelectionShakeOffset.x,
                position.y - lastSelectionShakeOffset.y,
                position.z);
        }

        lastSelectionShakeOffset = Vector2.zero;
        selectionShakeStartedAt = -1f;
        selectionShakeDuration = 0f;
        selectionShakeAmplitude = 0f;
    }

    private void SnapToPlayer()
    {
        if (movementRoot == null || followTarget == null)
            return;

        ClearSelectionShake();
        Vector3 current = movementRoot.position;
        movementRoot.position = new Vector3(followTarget.position.x, followTarget.position.y, current.z);
    }
}

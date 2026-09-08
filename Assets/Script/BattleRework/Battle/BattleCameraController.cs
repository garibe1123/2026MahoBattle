using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public enum BattleCameraFocusPriority
{
    PlayerFollow = 0,
    FieldCombat = 10,
    ForcedEvent = 20,
    StageTransition = 50,
    Show = 100
}

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

    [Header("Player Camera Lead")]
    [SerializeField, Min(0f)] private float playerLeadDistance = 0.80f;
    [SerializeField, Min(0.1f)] private float playerSpeedForFullLead = 5f;
    [SerializeField, Min(0f)] private float playerLeadSharpness = 6f;

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

    [Header("Field Cinematic Focus")]
    [SerializeField, Min(0f)] private float cinematicFollowSharpness = 7.5f;
    [SerializeField, Min(0f)] private float cinematicZoomSharpness = 8f;
    [SerializeField, Min(0f)] private float defaultBoundsPadding = 0.85f;

    [Header("Directional Camera Impulse")]
    [SerializeField, Min(1f)] private float impulseSpring = 72f;
    [SerializeField, Min(0f)] private float impulseDamping = 15f;
    [SerializeField, Min(0.01f)] private float maxImpulseOffset = 0.18f;
    [SerializeField, Min(0.01f)] private float maxImpulseVelocity = 5.2f;

    private sealed class CameraFocusRequest
    {
        public int id;
        public int priority;
        public Transform target;
        public bool tracksTarget;
        public Vector2 position;
        public Bounds bounds;
        public bool usesBounds;
        public float requestedZoom;
        public float boundsPadding;
        public float expiresAt;
    }

    private Vector2 panOffset;
    private Vector2 previousMousePosition;
    private float targetZoom;
    private float zoomBeforeShow;
    private bool inspecting;
    private bool initialized;
    private bool rigResolved;
    private bool showFraming;
    private float showBlend;
    private BattleSceneManager battleSceneManager;
    private Rigidbody2D followBody;
    private Transform resolvedFollowBodyTarget;
    private Vector2 currentPlayerLead;

    private readonly List<CameraFocusRequest> focusRequests = new();
    private int nextFocusRequestId = 1;

    private bool showCursorTracking;
    private Vector2 requestedShowCursorDirection;
    private Vector2 currentShowCursorPan;

    private float selectionShakeStartedAt = -1f;
    private float selectionShakeDuration;
    private float selectionShakeAmplitude;
    private Vector2 directionalImpulseOffset;
    private Vector2 directionalImpulseVelocity;
    private float directionalImpulseEndTime;
    private Vector2 lastCameraEffectOffset;

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
        ClearCameraEffects();
        focusRequests.Clear();
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

    /// <summary>
    /// 카메라의 최종 Impulse Layer에 방향성 충격을 누적합니다.
    /// direction은 카메라가 처음 밀려날 방향입니다.
    /// </summary>
    public void PushCameraImpulse(Vector2 direction, float strength, float duration = 0.13f)
    {
        float safeStrength = Mathf.Max(0f, strength);
        if (safeStrength <= 0f)
            return;

        Vector2 resolvedDirection = direction.sqrMagnitude > 0.001f
            ? direction.normalized
            : Random.insideUnitCircle.normalized;
        if (resolvedDirection.sqrMagnitude <= 0.001f)
            resolvedDirection = Vector2.down;

        directionalImpulseVelocity += resolvedDirection * (safeStrength * 28f);
        directionalImpulseVelocity = Vector2.ClampMagnitude(
            directionalImpulseVelocity,
            Mathf.Max(0.01f, maxImpulseVelocity));
        directionalImpulseEndTime = Mathf.Max(
            directionalImpulseEndTime,
            Time.unscaledTime + Mathf.Max(0.01f, duration));
    }

    public int FocusTarget(
        Transform target,
        float duration,
        float zoom = 0f,
        BattleCameraFocusPriority priority = BattleCameraFocusPriority.FieldCombat)
    {
        if (target == null)
            return 0;

        return AddFocusRequest(new CameraFocusRequest
        {
            target = target,
            tracksTarget = true,
            position = target.position,
            requestedZoom = zoom,
            priority = (int)priority,
            expiresAt = ResolveExpiry(duration)
        });
    }

    public int FocusPosition(
        Vector3 worldPosition,
        float duration,
        float zoom = 0f,
        BattleCameraFocusPriority priority = BattleCameraFocusPriority.FieldCombat)
    {
        return AddFocusRequest(new CameraFocusRequest
        {
            position = worldPosition,
            requestedZoom = zoom,
            priority = (int)priority,
            expiresAt = ResolveExpiry(duration)
        });
    }

    public int FocusBounds(
        Bounds bounds,
        float duration,
        float padding = -1f,
        BattleCameraFocusPriority priority = BattleCameraFocusPriority.FieldCombat)
    {
        return AddFocusRequest(new CameraFocusRequest
        {
            position = bounds.center,
            bounds = bounds,
            usesBounds = true,
            boundsPadding = padding >= 0f ? padding : defaultBoundsPadding,
            priority = (int)priority,
            expiresAt = ResolveExpiry(duration)
        });
    }

    public void ReleaseFocus(int requestId)
    {
        if (requestId <= 0)
            return;

        for (int i = focusRequests.Count - 1; i >= 0; i--)
        {
            if (focusRequests[i].id == requestId)
                focusRequests.RemoveAt(i);
        }
    }

    public void ReleaseAllFieldFocus()
    {
        for (int i = focusRequests.Count - 1; i >= 0; i--)
        {
            if (focusRequests[i].priority < (int)BattleCameraFocusPriority.StageTransition)
                focusRequests.RemoveAt(i);
        }
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

        ResolveFollowBody();

        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (showStage == null)
            showStage = FindFirstObjectByType<BattleShowWorldSetController>();

        if (battleSceneManager == null && transform.parent == null)
            battleSceneManager = FindFirstObjectByType<BattleSceneManager>();
        if (battleSceneManager != null && transform.parent == null)
            transform.SetParent(battleSceneManager.transform, true);

        ResolveMovementRoot();
    }

    private void ResolveFollowBody()
    {
        if (followTarget == resolvedFollowBodyTarget)
            return;

        resolvedFollowBodyTarget = followTarget;
        followBody = followTarget != null ? followTarget.GetComponent<Rigidbody2D>() : null;
        currentPlayerLead = Vector2.zero;
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

        ResolveFollowBody();

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

        UpdatePlayerLead();

        CameraFocusRequest activeFocus = showFraming ? null : ResolveActiveFocusRequest();
        if (inspecting && activeFocus != null &&
            activeFocus.priority < (int)BattleCameraFocusPriority.ForcedEvent)
        {
            activeFocus = null;
        }

        float normalZoom = ResolveFocusZoom(activeFocus);
        float showZoom = showStage != null && showStage.HasCameraAnchor
            ? showStage.ShowCameraSize
            : normalZoom;
        float desiredZoom = Mathf.Lerp(normalZoom, Mathf.Clamp(showZoom, minZoom, maxZoom), showBlend);
        float normalZoomSpeed = activeFocus != null ? cinematicZoomSharpness : zoomSharpness;
        float zoomSpeed = Mathf.Lerp(normalZoomSpeed, showFollowSharpness, showBlend);
        float zoomT = 1f - Mathf.Exp(-Mathf.Max(0f, zoomSpeed) * Time.unscaledDeltaTime);
        controlledCamera.orthographicSize = Mathf.Lerp(controlledCamera.orthographicSize, desiredZoom, zoomT);

        if (!showFraming && !inspecting && panOffset.sqrMagnitude > 0.0001f)
        {
            float returnT = 1f - Mathf.Exp(-returnFromInspectionSharpness * Time.unscaledDeltaTime);
            panOffset = Vector2.Lerp(panOffset, Vector2.zero, returnT);
            if (panOffset.sqrMagnitude < 0.0001f)
                panOffset = Vector2.zero;
        }

        Vector2 normalTarget = activeFocus != null
            ? ResolveFocusPosition(activeFocus)
            : (Vector2)followTarget.position + panOffset + currentPlayerLead;
        Vector2 showTarget = showStage != null && showStage.HasCameraAnchor
            ? (Vector2)showStage.CameraTargetWorld + currentShowCursorPan
            : normalTarget;
        Vector2 desiredTarget = Vector2.Lerp(normalTarget, showTarget, showBlend);

        Vector3 current = movementRoot.position;
        Vector2 unaffectedCurrent = (Vector2)current - lastCameraEffectOffset;
        float normalFollowSpeed = activeFocus != null ? cinematicFollowSharpness : followSharpness;
        float followSpeed = Mathf.Lerp(normalFollowSpeed, showFollowSharpness, showBlend);
        float followT = !showFraming && inspecting && activeFocus == null
            ? 1f
            : 1f - Mathf.Exp(-Mathf.Max(0f, followSpeed) * Time.unscaledDeltaTime);
        Vector2 next = Vector2.Lerp(unaffectedCurrent, desiredTarget, followT);

        if ((showFraming || showBlend > 0f) && showTransitionMaxSpeed > 0f)
        {
            next = Vector2.MoveTowards(
                unaffectedCurrent,
                next,
                showTransitionMaxSpeed * Time.unscaledDeltaTime);
        }

        UpdateDirectionalImpulse();
        lastCameraEffectOffset = directionalImpulseOffset + EvaluateSelectionShakeOffset();
        Vector2 shaken = next + lastCameraEffectOffset;
        movementRoot.position = new Vector3(shaken.x, shaken.y, current.z);
    }

    private void UpdatePlayerLead()
    {
        bool combatFollow = runManager != null && runManager.State == BattleRunState.Combat &&
                            !showFraming && !inspecting && followBody != null;
        Vector2 targetLead = Vector2.zero;

        if (combatFollow)
        {
            Vector2 velocity = followBody.linearVelocity;
            float speed = velocity.magnitude;
            if (speed > 0.01f)
            {
                float amount = Mathf.Clamp01(speed / Mathf.Max(0.1f, playerSpeedForFullLead));
                targetLead = velocity.normalized * (playerLeadDistance * amount);
            }
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(0f, playerLeadSharpness) * Time.unscaledDeltaTime);
        currentPlayerLead = Vector2.Lerp(currentPlayerLead, targetLead, t);
        if (currentPlayerLead.sqrMagnitude < 0.000001f && targetLead == Vector2.zero)
            currentPlayerLead = Vector2.zero;
    }

    private int AddFocusRequest(CameraFocusRequest request)
    {
        if (request == null)
            return 0;

        if (nextFocusRequestId <= 0)
            nextFocusRequestId = 1;

        request.id = nextFocusRequestId++;
        focusRequests.Add(request);
        return request.id;
    }

    private static float ResolveExpiry(float duration)
    {
        return duration > 0f ? Time.unscaledTime + duration : -1f;
    }

    private CameraFocusRequest ResolveActiveFocusRequest()
    {
        float now = Time.unscaledTime;
        CameraFocusRequest best = null;

        for (int i = focusRequests.Count - 1; i >= 0; i--)
        {
            CameraFocusRequest request = focusRequests[i];
            bool expired = request.expiresAt >= 0f && now >= request.expiresAt;
            bool lostTarget = request.tracksTarget &&
                              (request.target == null || !request.target.gameObject.activeInHierarchy);
            if (expired || lostTarget)
            {
                focusRequests.RemoveAt(i);
                continue;
            }

            if (best == null || request.priority > best.priority ||
                (request.priority == best.priority && request.id > best.id))
            {
                best = request;
            }
        }

        return best;
    }

    private Vector2 ResolveFocusPosition(CameraFocusRequest request)
    {
        if (request == null)
            return followTarget != null ? (Vector2)followTarget.position : Vector2.zero;
        if (request.target != null)
            return request.target.position;
        return request.usesBounds ? (Vector2)request.bounds.center : request.position;
    }

    private float ResolveFocusZoom(CameraFocusRequest request)
    {
        if (request == null)
            return targetZoom;

        if (request.usesBounds)
        {
            float padding = Mathf.Max(0f, request.boundsPadding);
            float aspect = controlledCamera != null && controlledCamera.aspect > 0.01f
                ? controlledCamera.aspect
                : 16f / 9f;
            float sizeByHeight = request.bounds.extents.y + padding;
            float sizeByWidth = (request.bounds.extents.x + padding) / Mathf.Max(0.1f, aspect);
            return Mathf.Clamp(Mathf.Max(sizeByHeight, sizeByWidth), minZoom, maxZoom);
        }

        return request.requestedZoom > 0f
            ? Mathf.Clamp(request.requestedZoom, minZoom, maxZoom)
            : targetZoom;
    }

    private void UpdateDirectionalImpulse()
    {
        if (directionalImpulseOffset == Vector2.zero && directionalImpulseVelocity == Vector2.zero)
            return;

        float dt = Mathf.Min(0.033f, Mathf.Max(0.001f, Time.unscaledDeltaTime));
        Vector2 acceleration =
            -directionalImpulseOffset * Mathf.Max(1f, impulseSpring) -
            directionalImpulseVelocity * Mathf.Max(0f, impulseDamping);

        directionalImpulseVelocity += acceleration * dt;
        directionalImpulseVelocity = Vector2.ClampMagnitude(
            directionalImpulseVelocity,
            Mathf.Max(0.01f, maxImpulseVelocity));
        directionalImpulseOffset += directionalImpulseVelocity * dt;
        directionalImpulseOffset = Vector2.ClampMagnitude(
            directionalImpulseOffset,
            Mathf.Max(0.01f, maxImpulseOffset));

        bool timeDone = Time.unscaledTime >= directionalImpulseEndTime;
        bool settled = directionalImpulseOffset.sqrMagnitude < 0.000004f &&
                       directionalImpulseVelocity.sqrMagnitude < 0.0004f;
        if (timeDone && settled)
        {
            directionalImpulseOffset = Vector2.zero;
            directionalImpulseVelocity = Vector2.zero;
            directionalImpulseEndTime = 0f;
        }
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

    private void ClearCameraEffects()
    {
        if (movementRoot != null && lastCameraEffectOffset.sqrMagnitude > 0f)
        {
            Vector3 position = movementRoot.position;
            movementRoot.position = new Vector3(
                position.x - lastCameraEffectOffset.x,
                position.y - lastCameraEffectOffset.y,
                position.z);
        }

        lastCameraEffectOffset = Vector2.zero;
        directionalImpulseOffset = Vector2.zero;
        directionalImpulseVelocity = Vector2.zero;
        directionalImpulseEndTime = 0f;
        selectionShakeStartedAt = -1f;
        selectionShakeDuration = 0f;
        selectionShakeAmplitude = 0f;
    }

    private void SnapToPlayer()
    {
        if (movementRoot == null || followTarget == null)
            return;

        ClearCameraEffects();
        currentPlayerLead = Vector2.zero;
        Vector3 current = movementRoot.position;
        movementRoot.position = new Vector3(followTarget.position.x, followTarget.position.y, current.z);
    }
}

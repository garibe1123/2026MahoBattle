using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Player-follow battle camera with a dedicated selection-show framing mode.
/// Gameplay never follows RoomOrigin. Item/Map selection switches to a wider studio shot:
/// the player stays clearly visible near the lower-left foreground while the prize screen
/// and presenter occupy the upper/right stage area.
/// </summary>
[DisallowMultipleComponent]
public class BattleCameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera controlledCamera;
    [SerializeField] private Transform followTarget;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private Transform movementRoot;

    [Header("Player Follow")]
    [SerializeField, Min(0f)] private float followSharpness = 11f;
    [SerializeField, Min(0f)] private float returnFromInspectionSharpness = 7f;

    [Header("Mouse Wheel Zoom")]
    [SerializeField, Min(0.1f)] private float minZoom = 4.2f;
    [SerializeField, Min(0.1f)] private float maxZoom = 9.5f;
    [SerializeField, Min(0.05f)] private float zoomStep = 0.8f;
    [SerializeField, Min(0f)] private float zoomSharpness = 12f;

    [Header("Selection Talk Show Framing")]
    [Tooltip("아이템 선택과 맵 선택에서 카메라를 플레이어보다 오른쪽/위로 이동시켜 플레이어를 화면 왼쪽에 남기는 스튜디오 와이드 샷입니다.")]
    [SerializeField] private Vector2 rewardShowOffset = new(5.7f, 2.35f);
    [Tooltip("Reward Show stays wide enough for Player + screen + presenter + reward stage to read as one set.")]
    [SerializeField, Min(0.1f)] private float rewardShowZoom = 6.1f;
    [SerializeField, Min(0f)] private float rewardShowSharpness = 6f;
    [Tooltip("선택 화면 진입/이탈 구도가 즉시 바뀌지 않도록 적용하는 전환 속도입니다.")]
    [SerializeField, Min(0.5f)] private float selectionTransitionSharpness = 3.2f;
    [Tooltip("선택 화면 전환 중 카메라가 한 프레임에 과도하게 이동하지 않도록 제한하는 초당 월드 거리입니다.")]
    [SerializeField, Min(1f)] private float selectionTransitionMaxSpeed = 14f;

    [Header("Map Cursor Camera Tracking")]
    [Tooltip("커서가 맵 화면 안에 있을 때 사용하는 실제 카메라 줌입니다. 기본 맵 구도는 Reward Show 비율을 그대로 사용합니다.")]
    [SerializeField, Min(0.1f)] private float mapCursorZoom = 5.15f;
    [Tooltip("맵 화면 안의 커서 방향으로 카메라가 이동하는 최대 월드 거리입니다.")]
    [SerializeField] private Vector2 mapCursorPanDistance = new(1.65f, 0.9f);
    [SerializeField, Min(1f)] private float mapCursorTrackingSharpness = 3.8f;

    [Header("Map Inspection")]
    [SerializeField] private int inspectionMouseButton = 2;
    [SerializeField, Min(0f)] private float inspectionPanMultiplier = 1f;
    [SerializeField, Min(0f)] private float maxInspectionDistance = 12f;
    [SerializeField] private KeyCode recenterKey = KeyCode.F;

    private Vector2 panOffset;
    private Vector2 previousMousePosition;
    private float targetZoom;
    private float zoomBeforeReward;
    private bool inspecting;
    private bool initialized;
    private bool rigResolved;
    private bool rewardFraming;
    private float selectionShakeStartedAt = -1f;
    private float selectionShakeDuration;
    private float selectionShakeAmplitude;
    private Vector2 lastSelectionShakeOffset;
    private bool mapCursorTracking;
    private Vector2 requestedMapCursorDirection;
    private Vector2 currentMapCursorPan;
    private float selectionFramingBlend;

    public float CurrentZoom => controlledCamera != null ? controlledCamera.orthographicSize : 0f;
    public bool IsInspecting => inspecting;
    public bool IsRewardFraming => rewardFraming;

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
    }

    /// <summary>
    /// 맵 노드 확정 순간 UI가 아니라 실제 전투 카메라에 감쇠 진동을 적용합니다.
    /// LateUpdate의 추적 결과에 오프셋을 합성하므로 Reward/Map framing과 충돌하지 않습니다.
    /// </summary>
    public void PlaySelectionConfirmShake(float amplitude, float duration)
    {
        selectionShakeAmplitude = Mathf.Max(0f, amplitude);
        selectionShakeDuration = Mathf.Max(0.05f, duration);
        selectionShakeStartedAt = Time.unscaledTime;
    }

    public void SetMapCursorTracking(bool active, Vector2 normalizedDirection)
    {
        mapCursorTracking = active;
        requestedMapCursorDirection = active
            ? new Vector2(
                Mathf.Clamp(normalizedDirection.x, -1f, 1f),
                Mathf.Clamp(normalizedDirection.y, -1f, 1f))
            : Vector2.zero;
    }

    public void Configure(Camera camera, Transform target, BattleRoomManager manager)
    {
        if (camera != null) controlledCamera = camera;
        if (target != null) followTarget = target;
        if (manager != null) roomManager = manager;

        rigResolved = false;
        ResolveMovementRoot();
        InitializeState();
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
        zoomBeforeReward = targetZoom;
        initialized = true;
    }

    private void Update()
    {
        ResolveReferences();
        if (controlledCamera == null)
            return;

        UpdateRewardMode();
        if (rewardFraming)
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

    private void UpdateRewardMode()
    {
        bool shouldRewardFrame = runManager != null &&
                                 (runManager.State == BattleRunState.Reward ||
                                  runManager.State == BattleRunState.SelectingNode);
        if (shouldRewardFrame == rewardFraming)
            return;

        rewardFraming = shouldRewardFrame;
        inspecting = false;
        panOffset = Vector2.zero;

        if (rewardFraming)
            zoomBeforeReward = targetZoom;
        else
        {
            targetZoom = Mathf.Clamp(zoomBeforeReward, minZoom, maxZoom);
            SetMapCursorTracking(false, Vector2.zero);
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

        bool mapSelectionFraming = rewardFraming && runManager != null &&
                                   runManager.State == BattleRunState.SelectingNode;
        bool activeMapTracking = mapSelectionFraming && mapCursorTracking;
        float framingTarget = rewardFraming ? 1f : 0f;
        float framingT = 1f - Mathf.Exp(
            -Mathf.Max(0.5f, selectionTransitionSharpness) * Time.unscaledDeltaTime);
        selectionFramingBlend = Mathf.Lerp(selectionFramingBlend, framingTarget, framingT);
        if (Mathf.Abs(selectionFramingBlend - framingTarget) < 0.001f)
            selectionFramingBlend = framingTarget;

        float trackingT = 1f - Mathf.Exp(
            -Mathf.Max(1f, mapCursorTrackingSharpness) * Time.unscaledDeltaTime);
        Vector2 targetMapPan = activeMapTracking
            ? Vector2.Scale(requestedMapCursorDirection, mapCursorPanDistance)
            : Vector2.zero;
        currentMapCursorPan = Vector2.Lerp(currentMapCursorPan, targetMapPan, trackingT);

        float selectionZoom = Mathf.Clamp(
            activeMapTracking ? mapCursorZoom : rewardShowZoom,
            minZoom,
            maxZoom);
        float desiredZoom = Mathf.Lerp(targetZoom, selectionZoom, selectionFramingBlend);
        float zoomSpeed = Mathf.Lerp(zoomSharpness, rewardShowSharpness, selectionFramingBlend);
        float zoomT = 1f - Mathf.Exp(-Mathf.Max(0f, zoomSpeed) * Time.unscaledDeltaTime);
        controlledCamera.orthographicSize = Mathf.Lerp(controlledCamera.orthographicSize, desiredZoom, zoomT);

        if (!rewardFraming && !inspecting && panOffset.sqrMagnitude > 0.0001f)
        {
            float returnT = 1f - Mathf.Exp(-returnFromInspectionSharpness * Time.unscaledDeltaTime);
            panOffset = Vector2.Lerp(panOffset, Vector2.zero, returnT);
            if (panOffset.sqrMagnitude < 0.0001f)
                panOffset = Vector2.zero;
        }

        Vector2 selectionOffset = rewardShowOffset + currentMapCursorPan;
        Vector2 desiredOffset = Vector2.Lerp(panOffset, selectionOffset, selectionFramingBlend);
        Vector2 desired = (Vector2)followTarget.position + desiredOffset;
        Vector3 current = movementRoot.position;
        Vector2 unshakenCurrent = (Vector2)current - lastSelectionShakeOffset;
        float followSpeed = Mathf.Lerp(followSharpness, rewardShowSharpness, selectionFramingBlend);
        float followT = !rewardFraming && inspecting
            ? 1f
            : 1f - Mathf.Exp(-Mathf.Max(0f, followSpeed) * Time.unscaledDeltaTime);
        Vector2 next = Vector2.Lerp(unshakenCurrent, desired, followT);
        if ((rewardFraming || selectionFramingBlend > 0f) && selectionTransitionMaxSpeed > 0f)
        {
            next = Vector2.MoveTowards(
                unshakenCurrent,
                next,
                selectionTransitionMaxSpeed * Time.unscaledDeltaTime);
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

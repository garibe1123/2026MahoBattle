using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 전투 카메라의 기본 Follow / Mouse Wheel Zoom / Map Inspection Pan을 담당합니다.
///
/// 조작:
/// - Mouse Wheel: Zoom In/Out
/// - Middle Mouse Drag: 전투 중 맵을 훑어보기
/// - F: 즉시 Player 쪽으로 시점 복귀
///
/// CameraShakePivot이 있으면 그 상위 CameraRig를 움직여 Camera Shake와 Follow가 서로 덮어쓰지 않게 합니다.
/// </summary>
[DisallowMultipleComponent]
public class BattleCameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera controlledCamera;
    [SerializeField] private Transform followTarget;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private Transform movementRoot;

    [Header("Follow")]
    [SerializeField, Min(0f)] private float followSharpness = 9f;
    [SerializeField, Min(0f)] private float returnFromInspectionSharpness = 6f;

    [Header("Mouse Wheel Zoom")]
    [SerializeField, Min(0.1f)] private float minZoom = 4.2f;
    [SerializeField, Min(0.1f)] private float maxZoom = 9.5f;
    [SerializeField, Min(0.05f)] private float zoomStep = 0.8f;
    [SerializeField, Min(0f)] private float zoomSharpness = 12f;

    [Header("Map Inspection")]
    [Tooltip("가운데 마우스 버튼을 누르고 드래그해서 맵 주변을 확인합니다.")]
    [SerializeField] private int inspectionMouseButton = 2;
    [SerializeField, Min(0f)] private float inspectionPanMultiplier = 1f;
    [Tooltip("4x4 Base 밖의 Monster 진입 공간도 확인할 수 있도록 Camera 중심 Clamp에 추가하는 여유입니다.")]
    [SerializeField, Min(0f)] private float outsideInspectionMargin = 3.5f;
    [SerializeField] private KeyCode recenterKey = KeyCode.F;

    private Vector2 panOffset;
    private Vector2 previousMousePosition;
    private float targetZoom;
    private bool inspecting;
    private bool initialized;

    public float CurrentZoom => controlledCamera != null ? controlledCamera.orthographicSize : 0f;
    public bool IsInspecting => inspecting;

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

    public void Configure(Camera camera, Transform target, BattleRoomManager manager)
    {
        if (camera != null)
            controlledCamera = camera;
        if (target != null)
            followTarget = target;
        if (manager != null)
            roomManager = manager;

        ResolveMovementRoot();
        InitializeState();
    }

    private void ResolveReferences()
    {
        if (controlledCamera == null)
            controlledCamera = GetComponentInChildren<Camera>(true);
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

        ResolveMovementRoot();
    }

    private void ResolveMovementRoot()
    {
        if (movementRoot != null)
            return;

        if (controlledCamera == null)
        {
            movementRoot = transform;
            return;
        }

        Transform cameraTransform = controlledCamera.transform;
        Transform shakePivot = cameraTransform.parent != null && cameraTransform.parent.name == "CameraShakePivot"
            ? cameraTransform.parent
            : null;

        if (shakePivot != null && shakePivot.parent != null && shakePivot.parent.name == "CameraRig")
        {
            movementRoot = shakePivot.parent;
            return;
        }

        if (name == "CameraRig")
        {
            movementRoot = transform;
            return;
        }

        movementRoot = cameraTransform;
    }

    private void InitializeState()
    {
        if (initialized || controlledCamera == null)
            return;

        controlledCamera.orthographic = true;
        minZoom = Mathf.Max(0.1f, minZoom);
        maxZoom = Mathf.Max(minZoom, maxZoom);
        targetZoom = Mathf.Clamp(controlledCamera.orthographicSize, minZoom, maxZoom);
        initialized = true;
    }

    private void Update()
    {
        ResolveReferences();
        if (controlledCamera == null)
            return;

        bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (!pointerOverUi)
            HandleZoomInput();

        HandleInspectionInput(pointerOverUi);

        if (Input.GetKeyDown(recenterKey))
        {
            panOffset = Vector2.zero;
            inspecting = false;
        }
    }

    private void HandleZoomInput()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) <= 0.001f)
            return;

        targetZoom = Mathf.Clamp(
            targetZoom - scroll * zoomStep,
            minZoom,
            maxZoom);
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
    }

    private void LateUpdate()
    {
        if (controlledCamera == null || movementRoot == null)
            return;

        float zoomT = 1f - Mathf.Exp(-zoomSharpness * Time.unscaledDeltaTime);
        controlledCamera.orthographicSize = Mathf.Lerp(
            controlledCamera.orthographicSize,
            targetZoom,
            zoomT);

        if (!inspecting && panOffset.sqrMagnitude > 0.0001f)
        {
            float returnT = 1f - Mathf.Exp(-returnFromInspectionSharpness * Time.unscaledDeltaTime);
            panOffset = Vector2.Lerp(panOffset, Vector2.zero, returnT);
        }

        Vector2 followPosition = followTarget != null
            ? followTarget.position
            : (Vector2)movementRoot.position;
        Vector2 desired = followPosition + panOffset;
        desired = ClampToCurrentRoom(desired);

        Vector3 current = movementRoot.position;
        float followT = inspecting
            ? 1f
            : 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
        Vector2 next = Vector2.Lerp(current, desired, followT);
        movementRoot.position = new Vector3(next.x, next.y, current.z);
    }

    private Vector2 ClampToCurrentRoom(Vector2 position)
    {
        if (roomManager == null || roomManager.CurrentRoom == null)
            return position;

        RoomDefinitionSO room = roomManager.CurrentRoom;
        Transform origin = roomManager.transform.Find("RoomOrigin");
        Vector2 originPosition = origin != null
            ? (Vector2)origin.position
            : (Vector2)roomManager.transform.position;

        Vector2 center = originPosition + room.GetRuntimeBaseCenterOffset();
        Vector2 half = room.GetRuntimeBaseWorldSize() * 0.5f + Vector2.one * outsideInspectionMargin;

        return new Vector2(
            Mathf.Clamp(position.x, center.x - half.x, center.x + half.x),
            Mathf.Clamp(position.y, center.y - half.y, center.y + half.y));
    }
}

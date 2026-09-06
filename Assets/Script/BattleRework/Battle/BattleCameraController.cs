using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// 전투 카메라의 Player Follow / Mouse Wheel Zoom / Map Inspection Pan을 담당합니다.
///
/// 핵심 규칙:
/// - RoomOrigin / 현재 Room 중심은 카메라 위치에 절대 관여하지 않습니다.
/// - Gameplay Room이 멀리 생성되거나 도킹되어도 Camera는 Player를 계속 추적합니다.
/// - Mouse Wheel은 Zoom만 변경합니다.
/// - Middle Mouse Drag 동안만 Player 기준 Pan Offset을 줄 수 있습니다.
/// - 드래그를 놓으면 Offset은 자동으로 Player에게 복귀합니다.
/// - F는 즉시 Player 중심으로 복귀합니다.
///
/// CameraShakePivot이 있으면 상위 CameraRig를 만들어 Follow와 Shake를 분리합니다.
/// </summary>
[DisallowMultipleComponent]
public class BattleCameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera controlledCamera;
    [SerializeField] private Transform followTarget;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private Transform movementRoot;

    [Header("Player Follow")]
    [SerializeField, Min(0f)] private float followSharpness = 11f;
    [SerializeField, Min(0f)] private float returnFromInspectionSharpness = 7f;

    [Header("Mouse Wheel Zoom")]
    [SerializeField, Min(0.1f)] private float minZoom = 4.2f;
    [SerializeField, Min(0.1f)] private float maxZoom = 9.5f;
    [SerializeField, Min(0.05f)] private float zoomStep = 0.8f;
    [SerializeField, Min(0f)] private float zoomSharpness = 12f;

    [Header("Map Inspection")]
    [Tooltip("가운데 마우스 버튼을 누르고 드래그하면 Player를 기준으로 잠시 주변을 확인합니다.")]
    [SerializeField] private int inspectionMouseButton = 2;
    [SerializeField, Min(0f)] private float inspectionPanMultiplier = 1f;
    [Tooltip("Player로부터 수동 Pan할 수 있는 최대 거리입니다. Room 위치와는 무관합니다.")]
    [SerializeField, Min(0f)] private float maxInspectionDistance = 12f;
    [SerializeField] private KeyCode recenterKey = KeyCode.F;

    private Vector2 panOffset;
    private Vector2 previousMousePosition;
    private float targetZoom;
    private bool inspecting;
    private bool initialized;
    private bool rigResolved;

    public float CurrentZoom => controlledCamera != null ? controlledCamera.orthographicSize : 0f;
    public bool IsInspecting => inspecting;

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
            SnapToPlayer();
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

        float zoomT = 1f - Mathf.Exp(-zoomSharpness * Time.unscaledDeltaTime);
        controlledCamera.orthographicSize = Mathf.Lerp(controlledCamera.orthographicSize, targetZoom, zoomT);

        if (!inspecting && panOffset.sqrMagnitude > 0.0001f)
        {
            float returnT = 1f - Mathf.Exp(-returnFromInspectionSharpness * Time.unscaledDeltaTime);
            panOffset = Vector2.Lerp(panOffset, Vector2.zero, returnT);

            if (panOffset.sqrMagnitude < 0.0001f)
                panOffset = Vector2.zero;
        }

        // 중요: RoomOrigin / CurrentRoom / Room Bounds는 여기서 전혀 조회하지 않습니다.
        // Gameplay Room이 멀리 생성되어도 카메라는 오직 Player + 수동 Pan Offset만 추적합니다.
        Vector2 desired = (Vector2)followTarget.position + panOffset;
        Vector3 current = movementRoot.position;
        float followT = inspecting
            ? 1f
            : 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
        Vector2 next = Vector2.Lerp((Vector2)current, desired, followT);
        movementRoot.position = new Vector3(next.x, next.y, current.z);
    }

    private void SnapToPlayer()
    {
        if (movementRoot == null || followTarget == null)
            return;

        Vector3 current = movementRoot.position;
        movementRoot.position = new Vector3(
            followTarget.position.x,
            followTarget.position.y,
            current.z);
    }
}

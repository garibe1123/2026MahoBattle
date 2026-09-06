using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Player-follow battle camera with a dedicated Reward Show framing mode.
/// Gameplay never follows RoomOrigin; Reward temporarily reframes the shot so the
/// player sits toward the lower-left while the large prize screen owns most of the view.
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

    [Header("Reward Show Framing")]
    [Tooltip("Camera center moves to +X/+Y from Player. +X intentionally places Player farther LEFT on screen; +Y keeps Player lower in frame.")]
    [SerializeField] private Vector2 rewardShowOffset = new(3.4f, 3.9f);
    [SerializeField, Min(0.1f)] private float rewardShowZoom = 5.0f;
    [SerializeField, Min(0f)] private float rewardShowSharpness = 7f;

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
        bool shouldRewardFrame = runManager != null && runManager.State == BattleRunState.Reward;
        if (shouldRewardFrame == rewardFraming)
            return;

        rewardFraming = shouldRewardFrame;
        inspecting = false;
        panOffset = Vector2.zero;

        if (rewardFraming)
            zoomBeforeReward = targetZoom;
        else
            targetZoom = Mathf.Clamp(zoomBeforeReward, minZoom, maxZoom);
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

        float desiredZoom = rewardFraming
            ? Mathf.Clamp(rewardShowZoom, minZoom, maxZoom)
            : targetZoom;
        float zoomSpeed = rewardFraming ? rewardShowSharpness : zoomSharpness;
        float zoomT = 1f - Mathf.Exp(-Mathf.Max(0f, zoomSpeed) * Time.unscaledDeltaTime);
        controlledCamera.orthographicSize = Mathf.Lerp(controlledCamera.orthographicSize, desiredZoom, zoomT);

        if (!rewardFraming && !inspecting && panOffset.sqrMagnitude > 0.0001f)
        {
            float returnT = 1f - Mathf.Exp(-returnFromInspectionSharpness * Time.unscaledDeltaTime);
            panOffset = Vector2.Lerp(panOffset, Vector2.zero, returnT);
            if (panOffset.sqrMagnitude < 0.0001f)
                panOffset = Vector2.zero;
        }

        Vector2 desired = (Vector2)followTarget.position + (rewardFraming ? rewardShowOffset : panOffset);
        Vector3 current = movementRoot.position;
        float followSpeed = rewardFraming ? rewardShowSharpness : followSharpness;
        float followT = !rewardFraming && inspecting
            ? 1f
            : 1f - Mathf.Exp(-Mathf.Max(0f, followSpeed) * Time.unscaledDeltaTime);
        Vector2 next = Vector2.Lerp((Vector2)current, desired, followT);
        movementRoot.position = new Vector3(next.x, next.y, current.z);
    }

    private void SnapToPlayer()
    {
        if (movementRoot == null || followTarget == null)
            return;

        Vector3 current = movementRoot.position;
        movementRoot.position = new Vector3(followTarget.position.x, followTarget.position.y, current.z);
    }
}

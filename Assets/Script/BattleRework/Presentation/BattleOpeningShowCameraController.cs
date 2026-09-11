using UnityEngine;

/// <summary>
/// Handles only the opening Start Area -> first Map selection camera lead.
///
/// The normal BattleCameraController intentionally waits for BattleShowWorldSetController.HasCameraAnchor,
/// which becomes true after the first Screen Carrier finishes entering. For the opening only, this controller
/// temporarily owns the camera rig while the carrier is moving, then hands control back as soon as the normal
/// show anchor becomes available.
/// </summary>
[DefaultExecutionOrder(25000)]
[DisallowMultipleComponent]
public sealed class BattleOpeningShowCameraController : MonoBehaviour
{
    private static BattleOpeningShowCameraController instance;

    [Header("Opening Camera Lead")]
    [SerializeField, Min(0.1f)] private float positionSharpness = 3.8f;
    [SerializeField, Min(0.1f)] private float zoomSharpness = 4.2f;
    [SerializeField, Min(0.1f)] private float maxMoveSpeed = 12f;

    private BattleRunManager runManager;
    private BattleShowWorldSetController showWorldSet;
    private BattleCameraController battleCamera;
    private Camera sceneCamera;
    private Transform movementRoot;
    private bool ownsCamera;
    private bool disabledBattleCameraByUs;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ResolveReferences();
    }

    private void OnDisable()
    {
        ReleaseCamera();
    }

    private void OnDestroy()
    {
        ReleaseCamera();
        if (instance == this)
            instance = null;
    }

    private void LateUpdate()
    {
        ResolveReferences();

        bool openingLead = ShouldOwnOpeningCamera();
        if (!openingLead)
        {
            ReleaseCamera();
            return;
        }

        AcquireCamera();
        if (!ownsCamera || sceneCamera == null || movementRoot == null || showWorldSet == null)
            return;

        Vector3 desiredWorld = showWorldSet.CameraTargetWorld;
        Vector3 currentWorld = movementRoot.position;

        float positionT = 1f - Mathf.Exp(-Mathf.Max(0.1f, positionSharpness) * Time.unscaledDeltaTime);
        Vector2 lerped = Vector2.Lerp(currentWorld, desiredWorld, positionT);
        Vector2 limited = Vector2.MoveTowards(
            currentWorld,
            lerped,
            Mathf.Max(0.1f, maxMoveSpeed) * Time.unscaledDeltaTime);

        movementRoot.position = new Vector3(limited.x, limited.y, currentWorld.z);

        float targetSize = Mathf.Max(0.1f, showWorldSet.ShowCameraSize);
        float zoomT = 1f - Mathf.Exp(-Mathf.Max(0.1f, zoomSharpness) * Time.unscaledDeltaTime);
        sceneCamera.orthographicSize = Mathf.Lerp(sceneCamera.orthographicSize, targetSize, zoomT);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (showWorldSet == null)
            showWorldSet = FindFirstObjectByType<BattleShowWorldSetController>();
        if (battleCamera == null)
            battleCamera = FindFirstObjectByType<BattleCameraController>();
        if (sceneCamera == null)
            sceneCamera = Camera.main;

        if (movementRoot == null && sceneCamera != null)
            movementRoot = ResolveMovementRoot(sceneCamera.transform);
    }

    private bool ShouldOwnOpeningCamera()
    {
        if (runManager == null || showWorldSet == null || sceneCamera == null)
            return false;
        if (!runManager.RunActive || !runManager.IsInStartArea)
            return false;
        if (runManager.State != BattleRunState.SelectingNode)
            return false;
        if (!showWorldSet.IsShowActive)
            return false;

        // Normal camera takes over immediately once the actual docked show anchor is ready.
        return !showWorldSet.HasCameraAnchor;
    }

    private void AcquireCamera()
    {
        if (ownsCamera)
            return;

        ownsCamera = true;
        disabledBattleCameraByUs = battleCamera != null && battleCamera.enabled;
        if (disabledBattleCameraByUs)
            battleCamera.enabled = false;
    }

    private void ReleaseCamera()
    {
        if (!ownsCamera)
            return;

        if (disabledBattleCameraByUs && battleCamera != null && !battleCamera.enabled)
            battleCamera.enabled = true;

        disabledBattleCameraByUs = false;
        ownsCamera = false;
    }

    private static Transform ResolveMovementRoot(Transform cameraTransform)
    {
        if (cameraTransform == null)
            return null;

        Transform shakePivot = cameraTransform.parent != null && cameraTransform.parent.name == "CameraShakePivot"
            ? cameraTransform.parent
            : null;

        if (shakePivot == null)
            return cameraTransform;

        if (shakePivot.parent != null && shakePivot.parent.name == "CameraRig")
            return shakePivot.parent;

        return shakePivot;
    }
}

using UnityEngine;

/// <summary>
/// Reward / Map show camera hand-off only.
///
/// Spotlight beam, focus mask, combat follow and Reward/Map idle animation are owned by
/// BattleSpotlightController. This component only preserves the show camera frame while
/// focus visuals fade out during state transitions.
/// </summary>
[DefaultExecutionOrder(26000)]
[DisallowMultipleComponent]
public sealed class BattleShowFocusController : MonoBehaviour
{
    private static BattleShowFocusController instance;

    [Header("References")]
    [SerializeField] private BattleCameraController battleCamera;
    [SerializeField] private BattleShowWorldSetController showWorldSet;
    [SerializeField] private BattleRunManager runManager;

    [Header("Show Exit Camera Hold")]
    [SerializeField, Min(0.01f)] private float exitFocusFadeDuration = 0.12f;
    [SerializeField, Min(0.01f)] private float exitDimFadeDuration = 0.18f;
    [SerializeField, Min(0f)] private float cameraReturnDelay = 0.05f;

    private bool runSubscribed;
    private bool selectionShowRequested;
    private bool hasLastShowFrame;
    private Vector3 lastShowTarget;
    private float lastShowZoom;
    private int exitCameraHoldRequest;

    public static BattleShowFocusController Instance => instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ResolveReferences();
        SubscribeRunState();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeRunState();
    }

    private void OnDisable()
    {
        UnsubscribeRunState();
        ReleaseExitCameraHold();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void LateUpdate()
    {
        ResolveReferences();
        SubscribeRunState();

        if (!selectionShowRequested ||
            showWorldSet == null ||
            !showWorldSet.IsShowActive)
        {
            return;
        }

        lastShowTarget = showWorldSet.CameraTargetWorld;
        lastShowZoom = showWorldSet.ShowCameraSize;
        hasLastShowFrame = true;
    }

    private void ResolveReferences()
    {
        if (battleCamera == null)
            battleCamera = FindFirstObjectByType<BattleCameraController>();

        if (showWorldSet == null)
            showWorldSet = FindFirstObjectByType<BattleShowWorldSetController>();

        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
    }

    private void SubscribeRunState()
    {
        if (runSubscribed || runManager == null)
            return;

        runManager.StateChanged += HandleRunStateChanged;
        runSubscribed = true;
        selectionShowRequested = IsSelectionShowRequested();
    }

    private void UnsubscribeRunState()
    {
        if (!runSubscribed)
            return;

        if (runManager != null)
            runManager.StateChanged -= HandleRunStateChanged;

        runSubscribed = false;
    }

    private void HandleRunStateChanged(BattleRunState _)
    {
        bool nextShow = IsSelectionShowRequested();

        if (nextShow == selectionShowRequested)
            return;

        if (nextShow)
            ReleaseExitCameraHold();
        else
            HoldLastShowCameraFrame();

        selectionShowRequested = nextShow;
    }

    private bool IsSelectionShowRequested()
    {
        if (runManager == null || !runManager.RunActive)
            return false;

        return runManager.State == BattleRunState.Reward ||
               runManager.State == BattleRunState.SelectingNode;
    }

    private void HoldLastShowCameraFrame()
    {
        if (battleCamera == null)
            return;

        ReleaseExitCameraHold();

        Camera camera = Camera.main;

        Vector3 holdTarget =
            hasLastShowFrame
                ? lastShowTarget
                : camera != null
                    ? camera.transform.position
                    : Vector3.zero;

        float holdZoom =
            hasLastShowFrame
                ? lastShowZoom
                : camera != null
                    ? camera.orthographicSize
                    : 0f;

        float holdDuration =
            Mathf.Max(0.01f, exitFocusFadeDuration) +
            Mathf.Max(0.01f, exitDimFadeDuration) +
            Mathf.Max(0f, cameraReturnDelay);

        exitCameraHoldRequest =
            battleCamera.FocusPosition(
                holdTarget,
                holdDuration,
                holdZoom,
                BattleCameraFocusPriority.Show);
    }

    private void ReleaseExitCameraHold()
    {
        if (exitCameraHoldRequest == 0)
            return;

        battleCamera?.ReleaseFocus(exitCameraHoldRequest);
        exitCameraHoldRequest = 0;
    }
}

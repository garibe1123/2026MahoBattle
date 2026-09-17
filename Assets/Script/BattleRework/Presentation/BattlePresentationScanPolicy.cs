using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps BattleShowPresentationManager's compatibility scan available without polling the whole scene
/// throughout stable combat/show states.
///
/// The presentation manager still owns decoration. This policy only opens a short scan window when the
/// room/show hierarchy can actually change, then restores polling to off. Authored auto-apply=false is
/// respected and never forced on.
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class BattlePresentationScanPolicy : MonoBehaviour
{
    private const float MinimumScanInterval = 0.12f;
    private const float DefaultMutationWindow = 1.35f;

    private static readonly BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo AutoApplyField =
        typeof(BattleShowPresentationManager).GetField("autoApplyTemplateToSlidingField", InstanceFields);
    private static readonly FieldInfo ScanIntervalField =
        typeof(BattleShowPresentationManager).GetField("fieldTemplateScanInterval", InstanceFields);

    private BattleShowPresentationManager presentation;
    private BattleRunManager runManager;
    private BattleStageTransitionController stageFlow;
    private Coroutine bindRoutine;

    private bool subscribed;
    private bool authoredAutoApply;
    private bool authoredStateCaptured;
    private float scanUntil;

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
        RestoreAuthoredSetting();
    }

    private IEnumerator BindWhenReady()
    {
        while (enabled)
        {
            ResolveReferences();
            if (presentation != null && runManager != null)
                break;
            yield return null;
        }

        if (!enabled || presentation == null)
        {
            bindRoutine = null;
            yield break;
        }

        CaptureAuthoredSetting();
        RaiseMinimumInterval();
        Subscribe();
        OpenMutationWindow(DefaultMutationWindow);
        bindRoutine = null;
    }

    private void Update()
    {
        if (presentation == null)
        {
            ResolveReferences();
            if (presentation == null)
                return;
        }

        if (!authoredStateCaptured)
            CaptureAuthoredSetting();

        bool allowCompatibilityScan = authoredAutoApply && Time.unscaledTime < scanUntil;
        SetAutoApply(allowCompatibilityScan);
    }

    private void ResolveReferences()
    {
        if (presentation == null)
            presentation = BattleShowPresentationManager.Instance != null
                ? BattleShowPresentationManager.Instance
                : FindFirstObjectByType<BattleShowPresentationManager>();
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (stageFlow == null)
            stageFlow = BattleStageTransitionController.Instance != null
                ? BattleStageTransitionController.Instance
                : FindFirstObjectByType<BattleStageTransitionController>();
    }

    private void CaptureAuthoredSetting()
    {
        if (authoredStateCaptured || presentation == null || AutoApplyField == null)
            return;

        object value = AutoApplyField.GetValue(presentation);
        authoredAutoApply = value is bool enabled && enabled;
        authoredStateCaptured = true;
    }

    private void RaiseMinimumInterval()
    {
        if (presentation == null || ScanIntervalField == null)
            return;

        object value = ScanIntervalField.GetValue(presentation);
        float current = value is float interval ? interval : MinimumScanInterval;
        if (current < MinimumScanInterval)
            ScanIntervalField.SetValue(presentation, MinimumScanInterval);
    }

    private void Subscribe()
    {
        if (subscribed)
            return;

        if (runManager != null)
        {
            runManager.StateChanged += HandleRunStateChanged;
            runManager.NodeEntered += HandleNodeEntered;
        }

        if (stageFlow != null)
            stageFlow.FlowStateChanged += HandleStageFlowChanged;

        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (runManager != null)
        {
            runManager.StateChanged -= HandleRunStateChanged;
            runManager.NodeEntered -= HandleNodeEntered;
        }

        if (stageFlow != null)
            stageFlow.FlowStateChanged -= HandleStageFlowChanged;

        subscribed = false;
    }

    private void HandleNodeEntered(BattleNodeData _)
    {
        OpenMutationWindow(DefaultMutationWindow);
    }

    private void HandleRunStateChanged(BattleRunState state)
    {
        switch (state)
        {
            case BattleRunState.EnteringNode:
            case BattleRunState.BuildingRoom:
            case BattleRunState.Reward:
            case BattleRunState.SelectingNode:
                OpenMutationWindow(DefaultMutationWindow);
                break;
        }
    }

    private void HandleStageFlowChanged(BattleStageFlowState state)
    {
        switch (state)
        {
            case BattleStageFlowState.ShowEntering:
            case BattleStageFlowState.RoomEntering:
            case BattleStageFlowState.RoomExiting:
                OpenMutationWindow(DefaultMutationWindow);
                break;
        }
    }

    private void OpenMutationWindow(float duration)
    {
        if (!authoredAutoApply && authoredStateCaptured)
            return;

        scanUntil = Mathf.Max(scanUntil, Time.unscaledTime + Mathf.Max(0.1f, duration));
        if (authoredStateCaptured && authoredAutoApply)
            SetAutoApply(true);
    }

    private void SetAutoApply(bool value)
    {
        if (presentation == null || AutoApplyField == null)
            return;

        object current = AutoApplyField.GetValue(presentation);
        if (current is bool enabled && enabled == value)
            return;

        AutoApplyField.SetValue(presentation, value);
    }

    private void RestoreAuthoredSetting()
    {
        if (!authoredStateCaptured || presentation == null)
            return;

        SetAutoApply(authoredAutoApply);
    }
}

internal static class BattlePresentationScanPolicyInstaller
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode _)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        bool battleScene = scene.name == BattleSceneEntry.DefaultBattleSceneName;
        if (!battleScene)
        {
            BattleSceneManager manager = Object.FindFirstObjectByType<BattleSceneManager>();
            battleScene = manager != null && manager.gameObject.scene == scene;
        }

        if (!battleScene || Object.FindFirstObjectByType<BattlePresentationScanPolicy>() != null)
            return;

        GameObject host = new("BattlePresentationScanPolicy");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<BattlePresentationScanPolicy>();
    }
}
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// BattleRewardFlow와 BattleRunManager 사이의 임시 completion adapter입니다.
///
/// Phase 5부터 Reward 카드 선택/확정/포기 UI는 BattleRewardCardActionController가 직접
/// BattleRewardFlow의 공개 API를 사용합니다. 이 클래스는 Reward UI를 생성하거나 수정하지 않습니다.
///
/// 남은 책임:
/// - Inventory DONE 요청을 최종 Run 진행으로 연결
/// - Reward Show 카메라의 임시 vertical bias 적용
///
/// BattleRunManager.CompleteRewardSelection이 public API로 승격되면 마지막 Reflection도 제거합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(44000)]
public sealed class BattleRewardDecisionFlowController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("Reward Camera")]
    [SerializeField] private float rewardCameraBiasY = -0.72f;

    private BattleRunManager runManager;
    private BattleRewardFlow rewardFlow;
    private BattleInventoryInteractionController inventoryInteraction;
    private BattleInventoryInteractionController subscribedInventoryInteraction;
    private BattleSelectionLayoutPolicyController selectionLayout;
    private MethodInfo completeRewardSelectionMethod;

    private void Awake()
    {
        ResolveReferences();
        EnsureInventorySubscription();
        CacheCompletionBridge();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureInventorySubscription();
        CacheCompletionBridge();
    }

    private void OnDisable()
    {
        UnsubscribeInventory();
    }

    private void Update()
    {
        ResolveReferences();
        EnsureInventorySubscription();
        CacheCompletionBridge();
        rewardFlow?.RefreshFromRunState();

        if (!IsReward())
            return;

        selectionLayout?.SetRewardCameraBiasY(rewardCameraBiasY);

        if (rewardFlow != null && rewardFlow.Phase == BattleRewardPhase.PackEditing)
            rewardFlow.SyncChosenRewardLocation();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (rewardFlow == null)
        {
            rewardFlow = GetComponent<BattleRewardFlow>();
            if (rewardFlow == null)
                rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
            if (rewardFlow == null && Application.isPlaying)
                rewardFlow = gameObject.AddComponent<BattleRewardFlow>();
        }
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        if (selectionLayout == null)
            selectionLayout = FindFirstObjectByType<BattleSelectionLayoutPolicyController>(FindObjectsInactive.Include);
    }

    private void EnsureInventorySubscription()
    {
        if (subscribedInventoryInteraction == inventoryInteraction)
            return;

        UnsubscribeInventory();
        subscribedInventoryInteraction = inventoryInteraction;
        if (subscribedInventoryInteraction != null)
            subscribedInventoryInteraction.RewardDoneRequested += HandleRewardDoneRequested;
    }

    private void UnsubscribeInventory()
    {
        if (subscribedInventoryInteraction != null)
            subscribedInventoryInteraction.RewardDoneRequested -= HandleRewardDoneRequested;
        subscribedInventoryInteraction = null;
    }

    private void CacheCompletionBridge()
    {
        if (runManager != null && completeRewardSelectionMethod == null)
            completeRewardSelectionMethod = typeof(BattleRunManager).GetMethod("CompleteRewardSelection", PrivateInstance);
    }

    private void HandleRewardDoneRequested()
    {
        if (!IsReward() || rewardFlow == null || !rewardFlow.CanComplete)
            return;

        rewardFlow.SyncChosenRewardLocation();
        if (!rewardFlow.CanComplete)
            return;

        if (!rewardFlow.ChosenRewardCommitted)
        {
            rewardFlow.SkipReward();
            return;
        }

        BattleEquipmentSO selected = rewardFlow.ChosenReward;
        if (selected == null)
            return;

        CacheCompletionBridge();
        if (completeRewardSelectionMethod == null)
        {
            Debug.LogError("[BattleReward] CompleteRewardSelection bridge is unavailable. Reward completion was not advanced.");
            return;
        }

        completeRewardSelectionMethod.Invoke(runManager, new object[] { selected });
    }

    private bool IsReward()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
    }
}

public static class BattleRewardDecisionFlowAutoInstaller
{
#if UNITY_EDITOR
    private static bool installQueued;

    [InitializeOnLoadMethod]
    private static void InitializeEditorInstaller()
    {
        EditorApplication.hierarchyChanged -= QueueInstall;
        EditorApplication.hierarchyChanged += QueueInstall;
        QueueInstall();
    }

    private static void QueueInstall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || installQueued)
            return;
        installQueued = true;
        EditorApplication.delayCall += EnsureEditorComponent;
    }

    private static void EnsureEditorComponent()
    {
        installQueued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null || EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded)
                continue;

            if (manager.GetComponent<BattleRewardFlow>() == null)
                Undo.AddComponent<BattleRewardFlow>(manager.gameObject);

            if (manager.GetComponent<BattleRewardDecisionFlowController>() == null)
                Undo.AddComponent<BattleRewardDecisionFlowController>(manager.gameObject);

            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponent()
    {
        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null)
                continue;

            if (manager.GetComponent<BattleRewardFlow>() == null)
                manager.gameObject.AddComponent<BattleRewardFlow>();

            if (manager.GetComponent<BattleRewardDecisionFlowController>() == null)
                manager.gameObject.AddComponent<BattleRewardDecisionFlowController>();
        }
    }
}

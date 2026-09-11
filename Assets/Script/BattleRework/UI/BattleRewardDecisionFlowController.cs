using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// BattleRewardFlow와 기존 Reward 카드 UI 사이의 임시 compatibility adapter입니다.
///
/// 이 클래스는 PACK 슬롯 입력, Hand 입력/표시, DONE/TRASH 표시를 소유하지 않습니다.
/// 해당 책임은 BattleInventoryInteractionController 하나로 통합되었습니다.
///
/// 남은 책임:
/// - 구형 BattleHUD의 카드 선택값을 BattleRewardFlow에 전달
/// - 구형 Reward Drag / Description UI 억제
/// - Reward 카메라 compatibility 보정
/// - BattleRunManager의 구형 private 완료 API와의 마지막 method bridge
///
/// BattleInventoryInteractionController의 private field는 Reflection으로 접근하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(44000)]
public sealed class BattleRewardDecisionFlowController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ObsoleteDescriptionBarName = "RewardActiveDescriptionBar";

    [Header("Reward Camera")]
    [SerializeField] private float rewardCameraBiasY = -0.72f;

    private BattleRunManager runManager;
    private BattleRewardFlow rewardFlow;
    private BattleInventoryInteractionController inventoryInteraction;
    private BattleInventoryInteractionController subscribedInventoryInteraction;
    private BattleHUD battleHud;
    private BattleSelectionLayoutPolicyController selectionLayout;

    // Phase 5의 Reward UI 교체 및 Run API 공개화 때 제거할 compatibility reflection입니다.
    private FieldInfo pendingRewardIndexField;
    private FieldInfo rewardCameraBiasField;
    private MethodInfo completeRewardSelectionMethod;

    private RectTransform screenInner;
    private RectTransform prizeChoices;
    private readonly List<RectTransform> obsoleteChoiceBars = new();

    private float nextResolveTime;
    private bool wasReward;

    private void Awake()
    {
        ResolveReferences();
        EnsureInventorySubscription();
        CacheCompatibilityReflection();
        ResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureInventorySubscription();
        CacheCompatibilityReflection();
        ResolveUi();
        nextResolveTime = 0f;
    }

    private void OnDisable()
    {
        UnsubscribeInventory();
        SetRewardCardDragEnabled(!IsReward());
    }

    private void Update()
    {
        ResolveReferences();
        EnsureInventorySubscription();
        CacheCompatibilityReflection();
        rewardFlow?.RefreshFromRunState();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveUi();
        }

        bool reward = IsReward();
        if (!reward)
        {
            if (wasReward)
                ResetPresentationState();
            wasReward = false;
            return;
        }

        wasReward = true;
        ApplyLowerRewardCameraBias();
        SyncChoiceFromLegacyHud();

        if (rewardFlow == null || rewardFlow.Phase == BattleRewardPhase.Inactive)
            return;

        if (rewardFlow.Phase == BattleRewardPhase.Choosing)
            MaintainChoiceStage();
        else if (rewardFlow.Phase == BattleRewardPhase.PackEditing)
            MaintainPackStage();
    }

    private void LateUpdate()
    {
        if (!IsReward() || rewardFlow == null || rewardFlow.Phase != BattleRewardPhase.Choosing)
            return;

        ResolveUi();
        HideObsoleteChoiceUi();
        ApplyChoiceCopyOnly();
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
        if (battleHud == null)
            battleHud = FindFirstObjectByType<BattleHUD>(FindObjectsInactive.Include);
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

    private void CacheCompatibilityReflection()
    {
        if (battleHud != null && pendingRewardIndexField == null)
            pendingRewardIndexField = typeof(BattleHUD).GetField("pendingRewardIndex", PrivateInstance);

        if (selectionLayout != null && rewardCameraBiasField == null)
            rewardCameraBiasField = typeof(BattleSelectionLayoutPolicyController).GetField("rewardCameraBiasWorld", PrivateInstance);

        if (runManager != null && completeRewardSelectionMethod == null)
            completeRewardSelectionMethod = typeof(BattleRunManager).GetMethod("CompleteRewardSelection", PrivateInstance);
    }

    private void ResolveUi()
    {
        RectTransform rewardScreen = FindRect("PrizeSelectionScreen");
        screenInner = rewardScreen != null
            ? rewardScreen.Find("ScreenInner") as RectTransform
            : FindRect("ScreenInner");

        prizeChoices = screenInner != null
            ? screenInner.Find("PrizeChoices") as RectTransform
            : FindRect("PrizeChoices");

        ResolveObsoleteChoiceBars();
    }

    private void ResolveObsoleteChoiceBars()
    {
        obsoleteChoiceBars.Clear();
        RectTransform[] all = UnityEngine.Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == ObsoleteDescriptionBarName)
                obsoleteChoiceBars.Add(rect);
        }
    }

    private void HideObsoleteChoiceUi()
    {
        for (int i = 0; i < obsoleteChoiceBars.Count; i++)
        {
            RectTransform bar = obsoleteChoiceBars[i];
            if (bar == null)
                continue;

            CanvasGroup group = bar.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            Image image = bar.GetComponent<Image>();
            if (image != null)
                image.raycastTarget = false;

            if (bar.gameObject.activeSelf)
                bar.gameObject.SetActive(false);
        }
    }

    private void SyncChoiceFromLegacyHud()
    {
        if (rewardFlow == null || rewardFlow.Phase != BattleRewardPhase.Choosing)
            return;

        int legacyIndex = GetPendingRewardIndex();
        if (legacyIndex >= 0 && TryGetReward(legacyIndex, out _))
        {
            if (rewardFlow.SelectedChoiceIndex != legacyIndex)
                rewardFlow.SelectChoice(legacyIndex);
        }
        else if (legacyIndex < 0 && rewardFlow.SelectedChoiceIndex >= 0)
        {
            rewardFlow.ClearChoice();
        }
    }

    private void MaintainChoiceStage()
    {
        SetPrizeChoicesInteractable(true);
        SetRewardCardDragEnabled(false);
        HideObsoleteChoiceUi();

        bool valid = rewardFlow != null && rewardFlow.CanConfirmChoice;
        if (valid && !BattlePauseController.IsPaused &&
            (Input.GetKeyDown(KeyCode.Return) ||
             Input.GetKeyDown(KeyCode.Space) ||
             Input.GetKeyDown(KeyCode.JoystickButton0)))
        {
            ConfirmSelectedReward();
        }
    }

    // BattleRewardCardActionController가 아직 compatibility reflection으로 호출합니다.
    // 실제 상태 변경은 BattleRewardFlow의 공개 API만 사용합니다.
    private void ConfirmSelectedReward()
    {
        if (!IsReward() || rewardFlow == null || rewardFlow.Phase != BattleRewardPhase.Choosing)
            return;

        SyncChoiceFromLegacyHud();
        if (!rewardFlow.ConfirmSelectedChoice())
            return;

        SetPendingRewardIndex(-1);
        SetPrizeChoicesInteractable(false);
        SetRewardCardDragEnabled(false);
        HideObsoleteChoiceUi();
    }

    private void MaintainPackStage()
    {
        SetPrizeChoicesInteractable(false);
        SetRewardCardDragEnabled(false);
        HideObsoleteChoiceUi();
        rewardFlow?.SyncChosenRewardLocation();
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

        if (completeRewardSelectionMethod == null && runManager != null)
            completeRewardSelectionMethod = typeof(BattleRunManager).GetMethod("CompleteRewardSelection", PrivateInstance);

        if (completeRewardSelectionMethod == null)
        {
            Debug.LogError("[BattleReward] CompleteRewardSelection compatibility bridge is unavailable. Reward completion was not advanced.");
            return;
        }

        completeRewardSelectionMethod.Invoke(runManager, new object[] { selected });
    }

    private void ApplyChoiceCopyOnly()
    {
        if (screenInner == null)
            return;

        Text[] texts = screenInner.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null)
                continue;

            string value = text.text ?? string.Empty;
            if (value.Contains("SELECT") && value.Contains("DRAG") && value.Contains("DROP"))
                text.text = "SELECT  •  INSPECT  •  DECIDE";
            else if (value == "CLICK / DRAG")
                text.text = "CLICK TO SELECT";
        }
    }

    private void SetPrizeChoicesInteractable(bool interactable)
    {
        if (prizeChoices == null)
            return;

        CanvasGroup group = prizeChoices.GetComponent<CanvasGroup>();
        if (group == null)
            group = prizeChoices.gameObject.AddComponent<CanvasGroup>();
        group.alpha = interactable ? 1f : 0.34f;
        group.blocksRaycasts = interactable;
        group.interactable = interactable;
    }

    private void SetRewardCardDragEnabled(bool enabled)
    {
        if (prizeChoices == null)
            return;

        RewardPrizeDrag[] drags = prizeChoices.GetComponentsInChildren<RewardPrizeDrag>(true);
        for (int i = 0; i < drags.Length; i++)
        {
            RewardPrizeDrag drag = drags[i];
            if (drag != null)
                drag.enabled = enabled;
        }
    }

    private void ApplyLowerRewardCameraBias()
    {
        if (selectionLayout == null)
            return;
        if (rewardCameraBiasField == null)
            rewardCameraBiasField = typeof(BattleSelectionLayoutPolicyController).GetField("rewardCameraBiasWorld", PrivateInstance);
        if (rewardCameraBiasField == null)
            return;

        object raw = rewardCameraBiasField.GetValue(selectionLayout);
        if (raw is not Vector2 current)
            return;

        if (current.y > rewardCameraBiasY)
            rewardCameraBiasField.SetValue(selectionLayout, new Vector2(current.x, rewardCameraBiasY));
    }

    private void ResetPresentationState()
    {
        SetPrizeChoicesInteractable(true);
        SetRewardCardDragEnabled(true);
    }

    private bool TryGetReward(int index, out BattleEquipmentSO reward)
    {
        reward = null;
        if (runManager == null || index < 0 || index >= runManager.CurrentRewardChoices.Count)
            return false;
        reward = runManager.CurrentRewardChoices[index];
        return reward != null;
    }

    private int GetPendingRewardIndex()
    {
        if (battleHud == null || pendingRewardIndexField == null)
            return -1;
        object raw = pendingRewardIndexField.GetValue(battleHud);
        return raw is int index ? index : -1;
    }

    private void SetPendingRewardIndex(int value)
    {
        if (battleHud != null && pendingRewardIndexField != null)
            pendingRewardIndexField.SetValue(battleHud, value);
    }

    private bool IsReward()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = UnityEngine.Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }
        return null;
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

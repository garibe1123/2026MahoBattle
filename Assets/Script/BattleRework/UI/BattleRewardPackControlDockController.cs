using UnityEngine;

/// <summary>
/// Reward PACK 편집의 DONE/NEXT 및 TRASH를 실제 GridBoard 오른쪽 Edge에 고정합니다.
///
/// BattleUnifiedInventoryInspectController가 버튼의 기능/가시성을 계속 소유하고,
/// 이 컴포넌트는 최종 Presentation 좌표만 GridBoard 기준으로 보정합니다.
/// 상세 패널이나 Screen Safe Area에는 의존하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(65040)]
public sealed class BattleRewardPackControlDockController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float boardEdgeGap = 8f;
    [SerializeField, Min(0f)] private float controlGap = 8f;

    private BattleRunManager runManager;
    private BattleRewardFlow rewardFlow;
    private BattleKineticLoadoutUI kineticLoadout;
    private RectTransform boardRoot;
    private RectTransform doneRoot;
    private RectTransform trashRoot;
    private float nextResolveTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        BattleUnifiedInventoryInspectController owner =
            FindFirstObjectByType<BattleUnifiedInventoryInspectController>(FindObjectsInactive.Include);
        if (owner == null || owner.GetComponent<BattleRewardPackControlDockController>() != null)
            return;

        owner.gameObject.AddComponent<BattleRewardPackControlDockController>();
    }

    private void OnEnable()
    {
        nextResolveTime = 0f;
        ResolveReferences();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.12f;
            ResolveReferences();
        }

        if (!IsRewardPackEditing() || boardRoot == null || doneRoot == null || trashRoot == null)
            return;

        DockControlsToBoard();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (rewardFlow == null)
            rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);

        if (kineticLoadout != null)
            boardRoot = kineticLoadout.GridBoard;

        doneRoot ??= FindRect("RewardPackDone");
        trashRoot ??= FindRect("InventoryTrash");
    }

    private bool IsRewardPackEditing()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward &&
               rewardFlow != null && rewardFlow.Phase == BattleRewardPhase.PackEditing;
    }

    private void DockControlsToBoard()
    {
        float doneHeight = Mathf.Max(1f, doneRoot.rect.height);
        float trashHeight = Mathf.Max(1f, trashRoot.rect.height);
        float gap = Mathf.Max(0f, controlGap);

        // 두 버튼 묶음 전체를 PACK 오른쪽 중앙에 맞춥니다.
        float doneCenterY = (trashHeight + gap) * 0.5f;
        float trashCenterY = -(doneHeight + gap) * 0.5f;

        DockControl(doneRoot, doneCenterY);
        DockControl(trashRoot, trashCenterY);
    }

    private void DockControl(RectTransform control, float boardLocalY)
    {
        if (control == null || boardRoot == null)
            return;

        // Pivot을 왼쪽 중앙으로 두어 PACK 오른쪽 Edge + gap 지점이 버튼의 왼쪽 Edge가 됩니다.
        control.pivot = new Vector2(0f, 0.5f);

        Vector3 boardLocalPoint = new(
            boardRoot.rect.xMax + Mathf.Max(0f, boardEdgeGap),
            boardLocalY,
            0f);

        control.position = boardRoot.TransformPoint(boardLocalPoint);
        control.rotation = boardRoot.rotation;
        control.localScale = Vector3.one;
        control.SetAsLastSibling();
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }

        return null;
    }
}

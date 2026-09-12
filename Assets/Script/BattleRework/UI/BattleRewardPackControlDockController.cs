using UnityEngine;

/// <summary>
/// Reward PACK 편집의 DONE/NEXT 및 TRASH를 실제 GridBoard 하단 Edge에 고정합니다.
///
/// BattleUnifiedInventoryInspectController가 버튼의 기능/가시성을 계속 소유하고,
/// 이 컴포넌트는 최종 Presentation 좌표만 GridBoard 기준으로 보정합니다.
/// 두 버튼은 PACK 하단에 가로 한 줄로 붙고, GridBoard의 회전/스케일을 그대로 따라갑니다.
/// 상세 패널이나 Screen Safe Area에는 의존하지 않습니다.
///
/// DONE/NEXT의 좌측 Motion Accent는 긴 물결처럼 보이지 않도록 짧고 비스듬한 2개의 사선으로 정리합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(65040)]
public sealed class BattleRewardPackControlDockController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float boardEdgeGap = 8f;
    [SerializeField, Min(0f)] private float controlGap = 8f;

    [Header("DONE / NEXT Accent")]
    [SerializeField] private Vector2 accentMainSize = new(34f, 5f);
    [SerializeField] private Vector2 accentMainPosition = new(-7f, 12f);
    [SerializeField, Range(-30f, 30f)] private float accentMainRotation = -13f;
    [SerializeField] private Vector2 accentSmallSize = new(22f, 4f);
    [SerializeField] private Vector2 accentSmallPosition = new(-1f, -11f);
    [SerializeField, Range(-30f, 30f)] private float accentSmallRotation = 8f;

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

        DockControlsUnderBoard();
        PolishDoneAccent();
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

    private void DockControlsUnderBoard()
    {
        if (boardRoot == null || doneRoot == null || trashRoot == null)
            return;

        // UnifiedInventoryInspectController가 먼저 fullRoot 기준으로 배치하더라도
        // 이 컨트롤러가 더 늦게 실행되어 두 버튼을 실제 PACK 보드의 자식으로 되돌립니다.
        // 따라서 PACK의 기울기/스케일/이동을 그대로 따라갑니다.
        ReparentToBoard(trashRoot);
        ReparentToBoard(doneRoot);

        float trashWidth = Mathf.Max(1f, trashRoot.rect.width);
        float doneWidth = Mathf.Max(1f, doneRoot.rect.width);
        float maxHeight = Mathf.Max(
            Mathf.Max(1f, trashRoot.rect.height),
            Mathf.Max(1f, doneRoot.rect.height));
        float gap = Mathf.Max(0f, controlGap);

        float totalWidth = trashWidth + gap + doneWidth;
        float left = -totalWidth * 0.5f;
        float centerY = boardRoot.rect.yMin - Mathf.Max(0f, boardEdgeGap) - maxHeight * 0.5f;

        // PACK 하단에 TRASH | DONE/NEXT 순서로 한 줄 배치합니다.
        PlaceControl(
            trashRoot,
            new Vector2(left + trashWidth * 0.5f, centerY));
        PlaceControl(
            doneRoot,
            new Vector2(left + trashWidth + gap + doneWidth * 0.5f, centerY));
    }

    private void PolishDoneAccent()
    {
        if (doneRoot == null)
            return;

        RectTransform visual = doneRoot.Find("DoneNextArrowVisual") as RectTransform;
        if (visual == null)
            return;

        RectTransform main = visual.Find("ArrowSpeedLine") as RectTransform;
        if (main != null)
        {
            main.anchorMin = main.anchorMax = new Vector2(0f, 0.5f);
            main.pivot = new Vector2(1f, 0.5f);
            main.sizeDelta = accentMainSize;
            main.anchoredPosition = accentMainPosition;
            main.localRotation = Quaternion.Euler(0f, 0f, accentMainRotation);
        }

        RectTransform small = visual.Find("ArrowSpeedLineSmall") as RectTransform;
        if (small != null)
        {
            small.anchorMin = small.anchorMax = new Vector2(0f, 0.5f);
            small.pivot = new Vector2(1f, 0.5f);
            small.sizeDelta = accentSmallSize;
            small.anchoredPosition = accentSmallPosition;
            small.localRotation = Quaternion.Euler(0f, 0f, accentSmallRotation);
        }
    }

    private void ReparentToBoard(RectTransform control)
    {
        if (control == null || boardRoot == null)
            return;

        if (control.parent != boardRoot)
            control.SetParent(boardRoot, false);
    }

    private static void PlaceControl(RectTransform control, Vector2 localCenter)
    {
        if (control == null)
            return;

        control.anchorMin = control.anchorMax = new Vector2(0.5f, 0.5f);
        control.pivot = new Vector2(0.5f, 0.5f);
        control.anchoredPosition = localCenter;
        control.localRotation = Quaternion.identity;
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

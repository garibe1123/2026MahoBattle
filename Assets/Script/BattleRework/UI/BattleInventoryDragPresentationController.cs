using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 좌측 하단 PACK의 드래그 사용성을 보강합니다.
/// - Combat / Reward에서 PACK 셀 Raycast 유지
/// - Reward에서는 PACK을 크게 열어 실제 편집 보드처럼 사용
/// - Drag 중에는 PACK을 조금 더 안쪽/크게 이동
/// - Drag Ghost는 PACK보다 높은 Sorting Order 유지
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32920)]
public sealed class BattleInventoryDragPresentationController : MonoBehaviour
{
    private const string PackName = "BackpackMiniGrid";
    private const string TrashName = "InventoryTrash";
    private const string InventoryGhostName = "InventoryDragGhost";
    private const string RewardGhostName = "RewardDragGhost";
    private const int DragSortingOrder = 2200;
    private const float MinimumRewardPackScale = 1.28f;
    private const float MinimumDraggingPackScale = 1.34f;

    [Header("PACK Position")]
    [SerializeField] private Vector2 combatPackPosition = new(58f, 38f);
    [SerializeField] private Vector2 rewardPackPosition = new(112f, 64f);
    [SerializeField] private Vector2 draggingPackPosition = new(146f, 76f);
    [SerializeField, Range(1f, 1.45f)] private float rewardPackScale = 1.28f;
    [SerializeField, Range(1f, 1.50f)] private float draggingPackScale = 1.34f;
    [SerializeField, Min(1f)] private float movementSharpness = 11f;

    [Header("Dragged Item")]
    [SerializeField] private Vector2 inventoryGhostSize = new(106f, 106f);
    [SerializeField] private Vector2 inventoryGhostIconSize = new(84f, 84f);
    [SerializeField, Range(1f, 1.25f)] private float rewardGhostScale = 1.08f;

    [Header("TRASH Follow")]
    [SerializeField] private Vector2 trashOffsetFromPack = new(414f, 8f);

    private BattleRunManager runManager;
    private RectTransform packRoot;
    private CanvasGroup packGroup;
    private RectTransform trashRoot;
    private RectTransform inventoryGhost;
    private RectTransform rewardGhost;

    private Vector2 currentPackPosition;
    private float currentPackScale = 1f;
    private bool packMotionInitialized;
    private float nextResolveTime;

    private void Awake()
    {
        ResolveReferences();
        ResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveUi();
        nextResolveTime = 0f;
        packMotionInitialized = false;
    }

    private void Update()
    {
        ResolveReferences();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveUi();
        }

        MaintainPackRaycasts();
    }

    private void LateUpdate()
    {
        ResolveUi();

        bool reward = IsReward();
        bool combat = IsCombat();
        bool dragging = IsAnyItemDragging();

        if (reward || combat)
            AnimatePack(reward, dragging);

        PromoteDragGhost(inventoryGhost, true);
        PromoteDragGhost(rewardGhost, false);
        FollowTrash(reward);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
    }

    private void ResolveUi()
    {
        if (packRoot == null)
        {
            packRoot = FindRect(PackName);
            if (packRoot != null)
            {
                packGroup = packRoot.GetComponent<CanvasGroup>();
                currentPackPosition = packRoot.anchoredPosition;
                currentPackScale = Mathf.Max(0.01f, packRoot.localScale.x);
                packMotionInitialized = true;
            }
        }

        if (trashRoot == null)
            trashRoot = FindRect(TrashName);

        if (inventoryGhost == null || !inventoryGhost.gameObject.activeInHierarchy)
            inventoryGhost = FindRect(InventoryGhostName);

        if (rewardGhost == null || !rewardGhost.gameObject.activeInHierarchy)
            rewardGhost = FindRect(RewardGhostName);
    }

    private void MaintainPackRaycasts()
    {
        if (packRoot == null)
            return;

        if (packGroup == null)
            packGroup = packRoot.GetComponent<CanvasGroup>();

        bool interactive = !BattlePauseController.IsPaused && (IsCombat() || IsReward());
        if (packGroup != null)
        {
            packGroup.blocksRaycasts = interactive;
            packGroup.interactable = interactive;
        }

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            RectTransform slot = packRoot.Find($"BackpackCells/BackpackCell_{i}") as RectTransform;
            if (slot == null)
                continue;

            Image image = slot.GetComponent<Image>();
            if (image != null)
                image.raycastTarget = interactive;
        }
    }

    private void AnimatePack(bool reward, bool dragging)
    {
        if (packRoot == null)
            return;

        Vector2 targetPosition;
        float targetScale;

        if (dragging)
        {
            targetPosition = draggingPackPosition;
            targetScale = Mathf.Max(MinimumDraggingPackScale, draggingPackScale);
        }
        else if (reward)
        {
            targetPosition = rewardPackPosition;
            targetScale = Mathf.Max(MinimumRewardPackScale, rewardPackScale);
        }
        else
        {
            targetPosition = combatPackPosition;
            targetScale = 1f;
        }

        if (!packMotionInitialized)
        {
            currentPackPosition = targetPosition;
            currentPackScale = targetScale;
            packMotionInitialized = true;
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(1f, movementSharpness) * Time.unscaledDeltaTime);
        currentPackPosition = Vector2.Lerp(currentPackPosition, targetPosition, t);
        currentPackScale = Mathf.Lerp(currentPackScale, targetScale, t);

        packRoot.anchorMin = packRoot.anchorMax = Vector2.zero;
        packRoot.pivot = Vector2.zero;
        packRoot.anchoredPosition = currentPackPosition;
        packRoot.localScale = Vector3.one * currentPackScale;
    }

    private bool IsAnyItemDragging()
    {
        bool inventoryDragging = inventoryGhost != null && inventoryGhost.gameObject.activeInHierarchy;
        bool rewardDragging = rewardGhost != null && rewardGhost.gameObject.activeInHierarchy;
        return inventoryDragging || rewardDragging;
    }

    private void PromoteDragGhost(RectTransform ghost, bool inventoryStyle)
    {
        if (ghost == null || !ghost.gameObject.activeInHierarchy)
            return;

        Canvas ghostCanvas = ghost.GetComponent<Canvas>();
        if (ghostCanvas == null)
            ghostCanvas = ghost.gameObject.AddComponent<Canvas>();
        ghostCanvas.overrideSorting = true;
        ghostCanvas.sortingOrder = DragSortingOrder;

        CanvasGroup canvasGroup = ghost.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = ghost.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        Graphic[] graphics = ghost.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
            if (graphics[i] != null)
                graphics[i].raycastTarget = false;

        ghost.SetAsLastSibling();

        if (inventoryStyle)
        {
            ghost.sizeDelta = inventoryGhostSize;
            ghost.localScale = Vector3.one;

            RectTransform icon = ghost.Find("Icon") as RectTransform;
            if (icon != null)
            {
                icon.sizeDelta = inventoryGhostIconSize;
                icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
                icon.anchoredPosition = Vector2.zero;
            }
        }
        else
        {
            ghost.localScale = Vector3.one * rewardGhostScale;
        }
    }

    private void FollowTrash(bool reward)
    {
        if (trashRoot == null || !reward)
            return;

        trashRoot.anchorMin = trashRoot.anchorMax = Vector2.zero;
        trashRoot.pivot = Vector2.zero;
        trashRoot.anchoredPosition = currentPackPosition + trashOffsetFromPack;
    }

    private bool IsCombat()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Combat;
    }

    private bool IsReward()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Reward;
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

#if UNITY_EDITOR
[InitializeOnLoad]
internal static class BattleInventoryDragPresentationEditorInstaller
{
    private static bool queued;

    static BattleInventoryDragPresentationEditorInstaller()
    {
        EditorApplication.hierarchyChanged -= QueueInstall;
        EditorApplication.hierarchyChanged += QueueInstall;
        QueueInstall();
    }

    private static void QueueInstall()
    {
        if (queued || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        queued = true;
        EditorApplication.delayCall += Install;
    }

    private static void Install()
    {
        queued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null ||
                EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid() ||
                !manager.gameObject.scene.isLoaded)
                continue;

            if (manager.GetComponent<BattleInventoryDragPresentationController>() != null)
                continue;

            Undo.AddComponent<BattleInventoryDragPresentationController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
}
#endif

internal static class BattleInventoryDragPresentationRuntimeInstaller
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null)
                continue;

            if (manager.GetComponent<BattleInventoryDragPresentationController>() == null)
                manager.gameObject.AddComponent<BattleInventoryDragPresentationController>();
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 좌측 하단 PACK의 드래그 사용성을 보강합니다.
///
/// - Combat / Reward에서 PACK 셀을 실제 Drag Source / Drop Target으로 사용할 수 있게 Raycast를 유지합니다.
/// - Reward에서는 PACK을 화면 모서리에 고정하지 않고 조금 안쪽으로 이동시킵니다.
/// - 실제 Drag 중에는 PACK이 한 번 더 안쪽으로 들어오고 살짝 커져 Drop Target으로 읽히게 합니다.
/// - InventoryDragGhost / RewardDragGhost는 PACK보다 높은 Sorting Order로 올려 아이템 아이콘이 가방 위를 떠다니게 합니다.
/// - 기존 장비 SO / Sprite / Scene 직렬화 데이터는 변경하지 않습니다.
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

    [Header("PACK Position")]
    [SerializeField] private Vector2 combatPackPosition = new(58f, 38f);
    [SerializeField] private Vector2 rewardPackPosition = new(112f, 64f);
    [SerializeField] private Vector2 draggingPackPosition = new(158f, 78f);
    [SerializeField, Range(1f, 1.20f)] private float rewardPackScale = 1.04f;
    [SerializeField, Range(1f, 1.25f)] private float draggingPackScale = 1.10f;
    [SerializeField, Min(1f)] private float movementSharpness = 11f;

    [Header("Dragged Item")]
    [SerializeField] private Vector2 inventoryGhostSize = new(106f, 106f);
    [SerializeField] private Vector2 inventoryGhostIconSize = new(84f, 84f);
    [SerializeField, Range(1f, 1.25f)] private float rewardGhostScale = 1.08f;

    [Header("TRASH Follow")]
    [SerializeField] private Vector2 trashOffsetFromPack = new(326f, 8f);

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
        FollowTrash(reward, dragging);
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
            // 미니 PACK 자체에서 장비를 집어 Drag / Swap할 수 있게 합니다.
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
            targetScale = draggingPackScale;
        }
        else if (reward)
        {
            targetPosition = rewardPackPosition;
            targetScale = rewardPackScale;
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

        // 앞 단계의 Layout Controller가 매 프레임 기본 위치를 다시 써도 이 컨트롤러가 최종 Presentation을 소유합니다.
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

        // RewardDragGhost는 BattleHUD Canvas(낮은 Sorting Order)에서 생성되므로
        // nested Canvas로 승격하지 않으면 좌측 PACK 뒤로 들어갈 수 있습니다.
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
            // Reward 카드 Drag도 PACK 위로 올라오되, 기존 이름/힌트 구성은 유지합니다.
            ghost.localScale = Vector3.one * rewardGhostScale;
        }
    }

    private void FollowTrash(bool reward, bool dragging)
    {
        if (trashRoot == null || !reward)
            return;

        // PACK이 안쪽으로 이동/확대될 때 TRASH도 같이 따라와 한 묶음으로 읽히게 합니다.
        Vector2 basePosition = dragging ? currentPackPosition : currentPackPosition;
        trashRoot.anchorMin = trashRoot.anchorMax = Vector2.zero;
        trashRoot.pivot = Vector2.zero;
        trashRoot.anchoredPosition = basePosition + trashOffsetFromPack;
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

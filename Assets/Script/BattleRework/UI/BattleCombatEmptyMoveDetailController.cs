using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Combat TAB에서 아이템을 빈 PACK 슬롯으로 이동할 때는 2-column Swap Compare를 사용하지 않고,
/// 드래그 중인 원본 아이템의 기존 Equipment Detail만 유지합니다.
///
/// 규칙:
/// - Item -> Item: BattleCombatPackSwapCompareController의 SWAP POSITION 비교 UI 유지.
/// - Item -> Empty: Compare UI 숨김 + Source Item의 기존 단일 Detail만 표시.
/// - 실제 이동/교환은 기존 BattleInventoryInteractionController / BattleEquipmentSystem이 계속 소유합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(61050)]
public sealed class BattleCombatEmptyMoveDetailController : MonoBehaviour
{
    [Header("AUTO REFERENCES")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleInventoryInteractionController inventoryInteraction;
    [SerializeField] private BattleCombatHudInputBridge combatInput;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;

    private BattleInventorySlotPointer[] slotPointers;
    private RectTransform compareRoot;
    private CanvasGroup compareGroup;
    private CanvasGroup detailGroup;
    private float nextResolveAt;
    private int lastSourceIndex = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TryInstall();
    }

    private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        TryInstall();
    }

    private static void TryInstall()
    {
        if (UnityEngine.Object.FindFirstObjectByType<BattleCombatEmptyMoveDetailController>(FindObjectsInactive.Include) != null)
            return;

        BattleInventoryInteractionController interaction =
            UnityEngine.Object.FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        BattleRunManager run =
            UnityEngine.Object.FindFirstObjectByType<BattleRunManager>(FindObjectsInactive.Include);

        GameObject host = interaction != null
            ? interaction.gameObject
            : run != null
                ? run.gameObject
                : GameObject.Find("BattleSystems");

        if (host != null)
            host.AddComponent<BattleCombatEmptyMoveDetailController>();
    }

    private void Awake()
    {
        ResolveReferences(true);
        ResolveRuntimeUi(true);
        ResolveSlotPointers();
    }

    private void OnEnable()
    {
        ResolveReferences(true);
        ResolveRuntimeUi(true);
        ResolveSlotPointers();
        nextResolveAt = 0f;
        lastSourceIndex = -1;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextResolveAt)
            return;

        nextResolveAt = Time.unscaledTime + 0.15f;
        ResolveReferences(false);
        ResolveRuntimeUi(false);

        if (slotPointers == null || slotPointers.Length == 0)
            ResolveSlotPointers();
    }

    private void LateUpdate()
    {
        if (!TryResolveEmptyMove(out int sourceIndex, out int targetIndex))
        {
            lastSourceIndex = -1;
            return;
        }

        ResolveRuntimeUi(false);

        // Combat Swap Compare가 같은 프레임에 빈 칸용 MOVE UI를 띄웠더라도
        // 이 presentation layer가 마지막에 숨겨 단일 Detail만 남깁니다.
        if (compareGroup != null)
        {
            compareGroup.alpha = 0f;
            compareGroup.blocksRaycasts = false;
            compareGroup.interactable = false;
        }

        // Hover 중인 빈 칸이 아니라, 실제로 들고 이동 중인 Source Item의 설명을 유지합니다.
        if (detailController != null &&
            (lastSourceIndex != sourceIndex || detailController.DisplayedSlot != sourceIndex))
        {
            detailController.ShowSlot(sourceIndex);
        }

        detailGroup = detailController != null ? detailController.Group : detailGroup;
        if (detailGroup != null)
        {
            detailGroup.alpha = 1f;
            detailGroup.blocksRaycasts = false;
            detailGroup.interactable = false;
        }

        // SWAP 전용 강조 프레임은 빈 칸 이동에서는 사용하지 않습니다.
        HideSwapPreviewFrames();

        lastSourceIndex = sourceIndex;
    }

    private bool TryResolveEmptyMove(out int sourceIndex, out int targetIndex)
    {
        sourceIndex = -1;
        targetIndex = -1;

        if (runManager == null || !runManager.RunActive || runManager.State != BattleRunState.Combat ||
            equipmentSystem == null || inventoryInteraction == null || combatInput == null ||
            kineticLoadout == null || !kineticLoadout.IsSwitchBoardOpen || !inventoryInteraction.IsDraggingItem)
            return false;

        sourceIndex = FindDraggingSlotIndex();
        targetIndex = combatInput.HoveredSlot;

        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex ||
            !equipmentSystem.IsSlotUnlocked(sourceIndex) || !equipmentSystem.IsSlotUnlocked(targetIndex))
            return false;

        if (!equipmentSystem.TryGetSlot(sourceIndex, out BattleEquipmentSlot source) ||
            source == null || source.equipment == null)
            return false;

        if (!equipmentSystem.TryGetSlot(targetIndex, out BattleEquipmentSlot target) || target == null)
            return false;

        return target.equipment == null;
    }

    private int FindDraggingSlotIndex()
    {
        if (slotPointers == null || slotPointers.Length == 0)
            ResolveSlotPointers();

        if (slotPointers == null)
            return -1;

        for (int i = 0; i < slotPointers.Length; i++)
        {
            BattleInventorySlotPointer pointer = slotPointers[i];
            if (pointer != null && pointer.IsDragging)
                return pointer.SlotIndex;
        }

        ResolveSlotPointers();
        for (int i = 0; i < slotPointers.Length; i++)
        {
            BattleInventorySlotPointer pointer = slotPointers[i];
            if (pointer != null && pointer.IsDragging)
                return pointer.SlotIndex;
        }

        return -1;
    }

    private void ResolveSlotPointers()
    {
        slotPointers = FindObjectsByType<BattleInventorySlotPointer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
    }

    private void ResolveReferences(bool force)
    {
        if (force || runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (force || equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>(FindObjectsInactive.Include);
        if (force || inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        if (force || combatInput == null)
            combatInput = FindFirstObjectByType<BattleCombatHudInputBridge>(FindObjectsInactive.Include);
        if (force || detailController == null)
            detailController = FindFirstObjectByType<BattleEquipmentDetailPanelController>(FindObjectsInactive.Include);
        if (force || kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
    }

    private void ResolveRuntimeUi(bool force)
    {
        if (force || detailGroup == null)
            detailGroup = detailController != null ? detailController.Group : null;

        if (!force && compareRoot != null && compareGroup != null)
            return;

        RectTransform detailRoot = detailController != null ? detailController.Root : null;
        RectTransform parent = detailRoot != null ? detailRoot.parent as RectTransform : null;
        compareRoot = parent != null ? parent.Find("RewardEquipmentCompareDetail") as RectTransform : null;
        compareGroup = compareRoot != null ? compareRoot.GetComponent<CanvasGroup>() : null;
    }

    private static void HideSwapPreviewFrames()
    {
        RectTransform[] all = FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect == null || rect.name != "CombatSwapPreviewFrame")
                continue;

            if (rect.gameObject.activeSelf)
                rect.gameObject.SetActive(false);
        }
    }
}

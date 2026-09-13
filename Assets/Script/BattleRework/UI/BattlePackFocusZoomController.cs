using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Combat TAB PACK의 focus scale presentation만 담당합니다.
///
/// - GridBoard의 authoritative anchor / position은 BattleUnifiedInventoryInspectController가 그대로 소유합니다.
/// - 이 클래스는 Combat TAB에서 Equipment Detail이 실제로 표시 중일 때 localScale만 후처리합니다.
/// - PACK 전체가 확대되고, 현재 Detail을 띄우는 슬롯은 한 번 더 확대됩니다.
/// - Reward PACK / 슬롯 데이터 / Equip 규칙은 변경하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33540)]
public sealed class BattlePackFocusZoomController : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;

    [Header("AUTO REFERENCES")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;
    [SerializeField] private BattleUnifiedInventoryInspectController unifiedInspect;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;

    [Header("COMBAT PACK FOCUS ZOOM")]
    [Tooltip("설명 패널이 떠 있는 동안 Combat PACK 전체 확대 배율입니다.")]
    [SerializeField, Range(1.00f, 1.20f)] private float focusedBoardScale = 1.10f;
    [Tooltip("PACK 전체 확대에 더해 현재 설명 대상 슬롯에 추가로 적용되는 배율입니다.")]
    [SerializeField, Range(1.00f, 1.18f)] private float focusedSlotScale = 1.08f;
    [Tooltip("확대/복귀 반응 속도입니다. 높을수록 빠르게 따라옵니다.")]
    [SerializeField, Range(4f, 30f)] private float focusTweenSharpness = 15f;

    private RectTransform boardRoot;
    private readonly RectTransform[] slotRects = new RectTransform[SlotCount];
    private float nextResolveAt;

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
        if (UnityEngine.Object.FindFirstObjectByType<BattlePackFocusZoomController>(FindObjectsInactive.Include) != null)
            return;

        BattleUnifiedInventoryInspectController unified =
            UnityEngine.Object.FindFirstObjectByType<BattleUnifiedInventoryInspectController>(FindObjectsInactive.Include);
        BattleKineticLoadoutUI loadout =
            UnityEngine.Object.FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
        BattleRunManager run =
            UnityEngine.Object.FindFirstObjectByType<BattleRunManager>(FindObjectsInactive.Include);

        GameObject host = unified != null
            ? unified.gameObject
            : loadout != null
                ? loadout.gameObject
                : run != null
                    ? run.gameObject
                    : GameObject.Find("BattleSystems");

        if (host != null)
            host.AddComponent<BattlePackFocusZoomController>();
    }

    private void Awake()
    {
        ResolveReferences(true);
        ResolveBoard(true);
    }

    private void OnEnable()
    {
        ResolveReferences(true);
        ResolveBoard(true);
        nextResolveAt = 0f;
    }

    private void OnDisable()
    {
        RestoreScales();
    }

    private void OnDestroy()
    {
        RestoreScales();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextResolveAt)
        {
            nextResolveAt = Time.unscaledTime + 0.15f;
            ResolveReferences(false);
            ResolveBoard(false);
        }

        if (boardRoot == null)
            return;

        bool combatTabOpen = runManager != null && runManager.RunActive &&
                             runManager.State == BattleRunState.Combat &&
                             kineticLoadout != null && kineticLoadout.IsSwitchBoardOpen;

        int focusSlot = ResolveFocusSlot(combatTabOpen);
        bool focused = focusSlot >= 0;

        float t = 1f - Mathf.Exp(-Mathf.Max(4f, focusTweenSharpness) * Time.unscaledDeltaTime);
        float boardTarget = focused ? Mathf.Max(1f, focusedBoardScale) : 1f;
        float boardScale = Mathf.Lerp(boardRoot.localScale.x, boardTarget, t);
        if (Mathf.Abs(boardScale - boardTarget) <= 0.001f)
            boardScale = boardTarget;
        boardRoot.localScale = Vector3.one * boardScale;

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slot = slotRects[i];
            if (slot == null)
                continue;

            float slotTarget = focused && i == focusSlot ? Mathf.Max(1f, focusedSlotScale) : 1f;
            float slotScale = Mathf.Lerp(slot.localScale.x, slotTarget, t);
            if (Mathf.Abs(slotScale - slotTarget) <= 0.001f)
                slotScale = slotTarget;
            slot.localScale = Vector3.one * slotScale;
        }
    }

    private int ResolveFocusSlot(bool combatTabOpen)
    {
        if (!combatTabOpen || unifiedInspect == null || detailController == null)
            return -1;

        int active = unifiedInspect.ActiveInspectSlot;
        if (active < 0 || active >= SlotCount || detailController.DisplayedSlot != active)
            return -1;

        CanvasGroup detailGroup = detailController.Group;
        if (detailGroup == null || detailGroup.alpha <= 0.01f)
            return -1;

        return slotRects[active] != null ? active : -1;
    }

    private void ResolveReferences(bool force)
    {
        if (force || runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (force || kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
        if (force || unifiedInspect == null)
            unifiedInspect = FindFirstObjectByType<BattleUnifiedInventoryInspectController>(FindObjectsInactive.Include);
        if (force || detailController == null)
            detailController = FindFirstObjectByType<BattleEquipmentDetailPanelController>(FindObjectsInactive.Include);
    }

    private void ResolveBoard(bool force)
    {
        if (force || boardRoot == null)
            boardRoot = kineticLoadout != null ? kineticLoadout.GridBoard : null;

        if (boardRoot == null)
            return;

        for (int i = 0; i < SlotCount; i++)
        {
            if (!force && slotRects[i] != null)
                continue;

            slotRects[i] = boardRoot.Find($"GridSlot_{i}") as RectTransform;
        }
    }

    private void RestoreScales()
    {
        if (boardRoot != null)
            boardRoot.localScale = Vector3.one;

        for (int i = 0; i < SlotCount; i++)
            if (slotRects[i] != null)
                slotRects[i].localScale = Vector3.one;
    }
}

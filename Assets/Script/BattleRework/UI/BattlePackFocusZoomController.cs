using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Combat TAB PACK의 focus zoom presentation만 담당합니다.
///
/// - GridBoard의 authoritative anchor / layout은 BattleUnifiedInventoryInspectController가 그대로 소유합니다.
/// - Combat TAB에서 Equipment Detail이 실제로 표시 중인 슬롯을 확대의 기준점으로 사용합니다.
/// - 포커스 슬롯의 위치는 거의 고정된 채 PACK 전체가 그 칸을 중심으로 바깥쪽으로 확대됩니다.
/// - 현재 Detail 대상 슬롯은 PACK 확대에 더해 한 번 더 크게 팝업됩니다.
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
    [SerializeField, Range(1.00f, 1.24f)] private float focusedBoardScale = 1.11f;
    [Tooltip("PACK 전체 확대에 더해 현재 설명 대상 슬롯에 추가로 적용되는 배율입니다.")]
    [SerializeField, Range(1.00f, 1.25f)] private float focusedSlotScale = 1.16f;
    [Tooltip("확대/복귀 반응 속도입니다. 높을수록 빠르게 따라옵니다.")]
    [SerializeField, Range(4f, 30f)] private float focusTweenSharpness = 16f;
    [Tooltip("커서를 다른 슬롯으로 옮겼을 때 확대 기준점이 새 슬롯으로 따라가는 속도입니다.")]
    [SerializeField, Range(4f, 30f)] private float focusAnchorTweenSharpness = 20f;

    private RectTransform boardRoot;
    private readonly RectTransform[] slotRects = new RectTransform[SlotCount];
    private float nextResolveAt;

    private Vector3 focusAnchorLocal;
    private bool focusAnchorInitialized;

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
        focusAnchorInitialized = false;
    }

    private void OnDisable()
    {
        RestoreScales();
        focusAnchorInitialized = false;
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

        float scaleT = 1f - Mathf.Exp(-Mathf.Max(4f, focusTweenSharpness) * Time.unscaledDeltaTime);
        float anchorT = 1f - Mathf.Exp(-Mathf.Max(4f, focusAnchorTweenSharpness) * Time.unscaledDeltaTime);

        UpdateFocusAnchor(focusSlot, focused, anchorT);
        ApplyBoardScaleAroundFocus(focused, scaleT);
        ApplyFocusedSlotScale(focusSlot, focused, scaleT);
    }

    private void UpdateFocusAnchor(int focusSlot, bool focused, float t)
    {
        if (!focused || focusSlot < 0 || focusSlot >= SlotCount || slotRects[focusSlot] == null)
            return;

        Vector3 targetAnchor = slotRects[focusSlot].localPosition;
        if (!focusAnchorInitialized)
        {
            focusAnchorLocal = targetAnchor;
            focusAnchorInitialized = true;
            return;
        }

        focusAnchorLocal = Vector3.Lerp(focusAnchorLocal, targetAnchor, t);
        if ((focusAnchorLocal - targetAnchor).sqrMagnitude <= 0.01f)
            focusAnchorLocal = targetAnchor;
    }

    private void ApplyBoardScaleAroundFocus(bool focused, float t)
    {
        // UnifiedInventory / CombatPresentation이 이 Controller보다 먼저 authoritative 위치를 적용합니다.
        // 여기서 읽은 localPosition을 기준 위치로 보고, 확대 때문에 생기는 이동량만 마지막에 더합니다.
        Vector3 authoritativeLocalPosition = boardRoot.localPosition;

        float targetScale = focused ? Mathf.Max(1f, focusedBoardScale) : 1f;
        float nextScale = Mathf.Lerp(boardRoot.localScale.x, targetScale, t);
        if (Mathf.Abs(nextScale - targetScale) <= 0.001f)
            nextScale = targetScale;

        boardRoot.localScale = Vector3.one * nextScale;

        if (!focusAnchorInitialized)
            return;

        // local point A를 기준으로 S배 확대할 때 pivot 이동량은 R * A * (1-S)입니다.
        // 따라서 선택 슬롯의 중심은 화면상 거의 같은 자리에 남고 나머지 PACK이 그 칸에서 퍼져나갑니다.
        Vector3 compensation = boardRoot.localRotation * (focusAnchorLocal * (1f - nextScale));
        boardRoot.localPosition = authoritativeLocalPosition + compensation;

        if (!focused && Mathf.Abs(nextScale - 1f) <= 0.001f)
            focusAnchorInitialized = false;
    }

    private void ApplyFocusedSlotScale(int focusSlot, bool focused, float t)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slot = slotRects[i];
            if (slot == null)
                continue;

            bool isFocusedSlot = focused && i == focusSlot;
            float targetScale = isFocusedSlot ? Mathf.Max(1f, focusedSlotScale) : 1f;
            float nextScale = Mathf.Lerp(slot.localScale.x, targetScale, t);
            if (Mathf.Abs(nextScale - targetScale) <= 0.001f)
                nextScale = targetScale;

            slot.localScale = Vector3.one * nextScale;
        }

        // 커진 슬롯이 인접 칸 아래에 깔리지 않도록 현재 포커스 슬롯만 최상단으로 올립니다.
        if (focused && focusSlot >= 0 && focusSlot < SlotCount && slotRects[focusSlot] != null)
            slotRects[focusSlot].SetAsLastSibling();
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

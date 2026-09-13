using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Combat TAB PACK의 focus zoom / detail callout presentation을 담당합니다.
///
/// - GridBoard의 authoritative anchor / layout은 BattleUnifiedInventoryInspectController가 그대로 소유합니다.
/// - Combat TAB에서 현재 Inspect 슬롯을 확대 기준점으로 사용합니다.
/// - 우측 Mission / Chat 포커스에서는 Detail alpha와 무관하게 Focus Zoom을 안정적으로 해제합니다.
/// - 포커스 슬롯과 Equipment Detail 사이에 말풍선 꼬리 형태의 삼각형 연결선을 표시합니다.
/// - Reward PACK / 슬롯 데이터 / Equip 규칙은 변경하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33540)]
public sealed class BattlePackFocusZoomController : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const float RightPanelEnterX = 0.61f;
    private const float RightPanelReturnX = 0.50f;

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

    [Header("DETAIL CALLOUT")]
    [Tooltip("설명 패널 왼쪽에 붙는 삼각형 꼬리의 절반 너비입니다.")]
    [SerializeField, Range(12f, 54f)] private float calloutHalfWidth = 28f;
    [Tooltip("삼각형 끝점이 슬롯 중심에서 설명 패널 방향으로 나가는 거리입니다.")]
    [SerializeField, Range(45f, 110f)] private float calloutSlotTipInset = 82f;
    [Tooltip("슬롯을 옮길 때 말풍선 꼬리가 따라가는 속도입니다.")]
    [SerializeField, Range(4f, 30f)] private float calloutTweenSharpness = 20f;
    [SerializeField] private Color calloutColor = new(1f, 0.80f, 0.08f, 0.72f);
    [SerializeField] private Color calloutShadowColor = new(0.025f, 0.022f, 0.040f, 0.82f);

    private RectTransform boardRoot;
    private RectTransform missionPanel;
    private RectTransform chatPanel;
    private readonly RectTransform[] slotRects = new RectTransform[SlotCount];
    private float nextResolveAt;

    private Vector3 focusAnchorLocal;
    private bool focusAnchorInitialized;
    private bool rightPanelSuppressed;

    private RectTransform connectorRoot;
    private BattlePackDetailConnectorGraphic connectorGraphic;
    private Outline connectorOutline;
    private Vector2 connectorTipVisual;
    private Vector2 connectorBaseAVisual;
    private Vector2 connectorBaseBVisual;
    private bool connectorVisualInitialized;

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
        ResolveUi(true);
    }

    private void OnEnable()
    {
        ResolveReferences(true);
        ResolveUi(true);
        nextResolveAt = 0f;
        focusAnchorInitialized = false;
        rightPanelSuppressed = false;
        connectorVisualInitialized = false;
    }

    private void OnDisable()
    {
        RestorePresentation();
    }

    private void OnDestroy()
    {
        RestorePresentation();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextResolveAt)
        {
            nextResolveAt = Time.unscaledTime + 0.15f;
            ResolveReferences(false);
            ResolveUi(false);
        }

        if (boardRoot == null)
            return;

        bool combatTabOpen = runManager != null && runManager.RunActive &&
                             runManager.State == BattleRunState.Combat &&
                             kineticLoadout != null && kineticLoadout.IsSwitchBoardOpen;

        UpdateRightPanelSuppression(combatTabOpen);

        int focusSlot = ResolveFocusSlot(combatTabOpen);
        bool focused = focusSlot >= 0;

        float scaleT = 1f - Mathf.Exp(-Mathf.Max(4f, focusTweenSharpness) * Time.unscaledDeltaTime);
        float anchorT = 1f - Mathf.Exp(-Mathf.Max(4f, focusAnchorTweenSharpness) * Time.unscaledDeltaTime);

        UpdateFocusAnchor(focusSlot, focused, anchorT);
        ApplyBoardScaleAroundFocus(focused, scaleT);
        ApplyFocusedSlotScale(focusSlot, focused, scaleT);
        UpdateDetailConnector(focusSlot, focused);
    }

    private void UpdateRightPanelSuppression(bool combatTabOpen)
    {
        if (!combatTabOpen || !Input.mousePresent)
        {
            rightPanelSuppressed = false;
            return;
        }

        Vector2 mouse = Input.mousePosition;
        bool insideMission = missionPanel != null && missionPanel.gameObject.activeInHierarchy &&
                             RectTransformUtility.RectangleContainsScreenPoint(missionPanel, mouse, null);
        bool insideChat = chatPanel != null && chatPanel.gameObject.activeInHierarchy &&
                          RectTransformUtility.RectangleContainsScreenPoint(chatPanel, mouse, null);

        // 우측 패널 위에서는 PACK의 현재 크기/경계와 상관없이 우측 포커스를 우선합니다.
        // 확대된 PACK이 패널 아래까지 겹쳐도 Focus가 매 프레임 뒤집히지 않습니다.
        if (insideMission || insideChat)
        {
            rightPanelSuppressed = true;
            return;
        }

        bool insidePack = boardRoot.gameObject.activeInHierarchy &&
                          RectTransformUtility.RectangleContainsScreenPoint(boardRoot, mouse, null);
        if (insidePack)
        {
            rightPanelSuppressed = false;
            return;
        }

        float normalizedX = Screen.width > 0 ? mouse.x / Screen.width : 0f;
        if (!rightPanelSuppressed && normalizedX >= RightPanelEnterX)
            rightPanelSuppressed = true;
        else if (rightPanelSuppressed && normalizedX <= RightPanelReturnX)
            rightPanelSuppressed = false;
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
        float targetScale = focused ? Mathf.Max(1f, focusedBoardScale) : 1f;
        float nextScale = Mathf.Lerp(boardRoot.localScale.x, targetScale, t);
        if (Mathf.Abs(nextScale - targetScale) <= 0.001f)
            nextScale = targetScale;

        boardRoot.localScale = Vector3.one * nextScale;

        // Combat Grid의 authoritative anchoredPosition은 Unified/Polish 단계에서 항상 0입니다.
        // 이전 프레임의 시각 보정 위치를 다시 읽어 누적하지 않고, 0을 기준으로 매 프레임 재계산합니다.
        Vector2 visualOffset = Vector2.zero;
        if (focusAnchorInitialized)
        {
            Vector3 compensation = boardRoot.localRotation * (focusAnchorLocal * (1f - nextScale));
            visualOffset = new Vector2(compensation.x, compensation.y);
        }

        boardRoot.anchoredPosition = visualOffset;

        if (!focused && Mathf.Abs(nextScale - 1f) <= 0.001f)
        {
            boardRoot.anchoredPosition = Vector2.zero;
            focusAnchorInitialized = false;
        }
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

        if (focused && focusSlot >= 0 && focusSlot < SlotCount && slotRects[focusSlot] != null)
            slotRects[focusSlot].SetAsLastSibling();
    }

    private int ResolveFocusSlot(bool combatTabOpen)
    {
        if (!combatTabOpen || rightPanelSuppressed || unifiedInspect == null || detailController == null)
            return -1;

        int active = unifiedInspect.ActiveInspectSlot;
        if (active < 0 || active >= SlotCount || detailController.DisplayedSlot != active)
            return -1;

        // Detail alpha는 BattleCombatTabPresentationPolishController가 우측 포커스에서 변경합니다.
        // 여기서는 alpha를 Focus 조건으로 다시 사용하지 않아 두 시스템이 서로 토글시키는 루프를 막습니다.
        return slotRects[active] != null ? active : -1;
    }

    private void UpdateDetailConnector(int focusSlot, bool focused)
    {
        RectTransform detailRoot = detailController != null ? detailController.Root : null;
        if (!focused || detailRoot == null || focusSlot < 0 || focusSlot >= SlotCount || slotRects[focusSlot] == null)
        {
            SetConnectorVisible(false);
            connectorVisualInitialized = false;
            return;
        }

        EnsureConnector(detailRoot);
        if (connectorRoot == null || connectorGraphic == null)
            return;

        RectTransform slot = slotRects[focusSlot];
        Vector3 slotCenterWorld = slot.TransformPoint(slot.rect.center);

        Vector3 slotInDetail = detailRoot.InverseTransformPoint(slotCenterWorld);
        float margin = Mathf.Max(42f, calloutHalfWidth + 12f);
        float baseY = Mathf.Clamp(slotInDetail.y, detailRoot.rect.yMin + margin, detailRoot.rect.yMax - margin);

        Vector3 baseCenterWorld = detailRoot.TransformPoint(new Vector3(detailRoot.rect.xMin, baseY, 0f));
        Vector3 baseAWorld = detailRoot.TransformPoint(new Vector3(detailRoot.rect.xMin, baseY - calloutHalfWidth, 0f));
        Vector3 baseBWorld = detailRoot.TransformPoint(new Vector3(detailRoot.rect.xMin, baseY + calloutHalfWidth, 0f));

        Vector2 slotCenter = connectorRoot.InverseTransformPoint(slotCenterWorld);
        Vector2 baseCenter = connectorRoot.InverseTransformPoint(baseCenterWorld);
        Vector2 baseA = connectorRoot.InverseTransformPoint(baseAWorld);
        Vector2 baseB = connectorRoot.InverseTransformPoint(baseBWorld);

        Vector2 towardDetail = baseCenter - slotCenter;
        if (towardDetail.sqrMagnitude > 0.001f)
            towardDetail.Normalize();
        else
            towardDetail = Vector2.right;

        Vector2 tip = slotCenter + towardDetail * Mathf.Max(20f, calloutSlotTipInset);

        float t = 1f - Mathf.Exp(-Mathf.Max(4f, calloutTweenSharpness) * Time.unscaledDeltaTime);
        if (!connectorVisualInitialized)
        {
            connectorTipVisual = tip;
            connectorBaseAVisual = baseA;
            connectorBaseBVisual = baseB;
            connectorVisualInitialized = true;
        }
        else
        {
            connectorTipVisual = Vector2.Lerp(connectorTipVisual, tip, t);
            connectorBaseAVisual = Vector2.Lerp(connectorBaseAVisual, baseA, t);
            connectorBaseBVisual = Vector2.Lerp(connectorBaseBVisual, baseB, t);
        }

        connectorGraphic.color = calloutColor;
        if (connectorOutline != null)
            connectorOutline.effectColor = calloutShadowColor;
        connectorGraphic.SetTriangle(connectorTipVisual, connectorBaseAVisual, connectorBaseBVisual);
        SetConnectorVisible(true);
    }

    private void EnsureConnector(RectTransform detailRoot)
    {
        if (detailRoot == null)
            return;

        RectTransform canvasRoot = detailRoot.parent as RectTransform;
        if (canvasRoot == null)
            return;

        if (connectorRoot != null && connectorRoot.parent != canvasRoot)
        {
            Destroy(connectorRoot.gameObject);
            connectorRoot = null;
            connectorGraphic = null;
            connectorOutline = null;
            connectorVisualInitialized = false;
        }

        if (connectorRoot == null)
        {
            GameObject go = new("EquipmentDetailCalloutTail");
            go.transform.SetParent(canvasRoot, false);
            connectorRoot = go.AddComponent<RectTransform>();
            connectorRoot.anchorMin = Vector2.zero;
            connectorRoot.anchorMax = Vector2.one;
            connectorRoot.offsetMin = Vector2.zero;
            connectorRoot.offsetMax = Vector2.zero;
            connectorRoot.localScale = Vector3.one;
            connectorRoot.localRotation = Quaternion.identity;

            connectorGraphic = go.AddComponent<BattlePackDetailConnectorGraphic>();
            connectorGraphic.raycastTarget = false;
            connectorGraphic.color = calloutColor;

            connectorOutline = go.AddComponent<Outline>();
            connectorOutline.useGraphicAlpha = false;
            connectorOutline.effectColor = calloutShadowColor;
            connectorOutline.effectDistance = new Vector2(4f, -4f);
        }

        connectorRoot.SetAsFirstSibling();
        detailRoot.SetAsLastSibling();
    }

    private void SetConnectorVisible(bool visible)
    {
        if (connectorRoot != null && connectorRoot.gameObject.activeSelf != visible)
            connectorRoot.gameObject.SetActive(visible);
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

    private void ResolveUi(bool force)
    {
        if (force || boardRoot == null)
            boardRoot = kineticLoadout != null ? kineticLoadout.GridBoard : null;

        if (boardRoot != null)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (!force && slotRects[i] != null)
                    continue;
                slotRects[i] = boardRoot.Find($"GridSlot_{i}") as RectTransform;
            }
        }

        RectTransform fullRoot = kineticLoadout != null ? kineticLoadout.FullRoot : null;
        RectTransform dashboard = fullRoot != null ? fullRoot.Find("BroadcastDashboard") as RectTransform : null;
        if (dashboard != null)
        {
            if (force || missionPanel == null)
                missionPanel = dashboard.Find("MissionPanel") as RectTransform;
            if (force || chatPanel == null)
                chatPanel = dashboard.Find("LiveChatPanel") as RectTransform;
        }
    }

    private void RestorePresentation()
    {
        if (boardRoot != null)
        {
            boardRoot.localScale = Vector3.one;
            boardRoot.anchoredPosition = Vector2.zero;
        }

        for (int i = 0; i < SlotCount; i++)
            if (slotRects[i] != null)
                slotRects[i].localScale = Vector3.one;

        focusAnchorInitialized = false;
        rightPanelSuppressed = false;
        connectorVisualInitialized = false;
        SetConnectorVisible(false);
    }
}

/// <summary>
/// Equipment Detail의 말풍선 꼬리용 단순 삼각형 UI Graphic입니다.
/// 별도 Sprite 없이 세 점만으로 삼각형을 그립니다.
/// </summary>
internal sealed class BattlePackDetailConnectorGraphic : MaskableGraphic
{
    private Vector2 tip;
    private Vector2 baseA;
    private Vector2 baseB;

    public void SetTriangle(Vector2 nextTip, Vector2 nextBaseA, Vector2 nextBaseB)
    {
        tip = nextTip;
        baseA = nextBaseA;
        baseB = nextBaseB;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;

        vertex.position = tip;
        vh.AddVert(vertex);
        vertex.position = baseA;
        vh.AddVert(vertex);
        vertex.position = baseB;
        vh.AddVert(vertex);

        vh.AddTriangle(0, 1, 2);
    }
}

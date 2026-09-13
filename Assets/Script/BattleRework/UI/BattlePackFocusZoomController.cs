using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Combat TAB PACK의 focus zoom / detail callout presentation을 담당합니다.
///
/// 핵심 규칙:
/// - GridBoard / GridSlot은 Pointer hitbox이므로 Scale / Position을 절대 변경하지 않습니다.
/// - 확대 연출은 BoardBack / SynergyLinks / 슬롯 내부 Graphic에만 visual-only transform으로 적용합니다.
/// - BattleCombatHudInputBridge의 명시적인 Hover 슬롯만 확대 기준점으로 사용합니다.
/// - 우측 Mission / Chat 포커스에서는 Focus Zoom을 안정적으로 해제합니다.
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
    private const string VisualBackName = "FocusZoomVisualBack";

    [Header("AUTO REFERENCES")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;
    [SerializeField] private BattleUnifiedInventoryInspectController unifiedInspect;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;
    [SerializeField] private BattleCombatHudInputBridge inputBridge;

    [Header("COMBAT PACK FOCUS ZOOM")]
    [Tooltip("커서가 실제 아이템 슬롯 위에 있을 때 PACK이 시각적으로 확대되는 배율입니다. 입력 Rect는 커지지 않습니다.")]
    [SerializeField, Range(1.00f, 1.24f)] private float focusedBoardScale = 1.11f;
    [Tooltip("현재 Hover 슬롯의 시각 요소만 추가로 강조하는 배율입니다. 입력 Rect에는 적용되지 않습니다.")]
    [SerializeField, Range(1.00f, 1.20f)] private float focusedSlotExtraScale = 1.08f;
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

    private sealed class VisualChildState
    {
        public RectTransform rect;
        public Vector3 baseLocalPosition;
        public Vector3 baseLocalScale;
    }

    private RectTransform boardRoot;
    private RectTransform boardBack;
    private RectTransform synergyLinks;
    private Vector3 boardBackBasePosition;
    private Vector3 boardBackBaseScale = Vector3.one;
    private Vector3 synergyBasePosition;
    private Vector3 synergyBaseScale = Vector3.one;
    private bool boardVisualBaseCaptured;

    private RectTransform missionPanel;
    private RectTransform chatPanel;
    private readonly RectTransform[] slotRects = new RectTransform[SlotCount];
    private readonly Image[] slotHitImages = new Image[SlotCount];
    private readonly Outline[] slotHitOutlines = new Outline[SlotCount];
    private readonly Image[] slotVisualBacks = new Image[SlotCount];
    private readonly Outline[] slotVisualBackOutlines = new Outline[SlotCount];
    private readonly Color[] slotLastVisualColors = new Color[SlotCount];
    private readonly bool[] slotVisualColorCaptured = new bool[SlotCount];
    private readonly List<VisualChildState>[] slotVisualChildren = new List<VisualChildState>[SlotCount];

    private float nextResolveAt;
    private float visualPackScale = 1f;
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
        visualPackScale = 1f;
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

        // Pointer hitbox의 부모는 매 프레임 완전히 고정합니다.
        // 이 값은 다음 프레임 EventSystem.Update에서도 그대로 유지됩니다.
        boardRoot.localScale = Vector3.one;
        boardRoot.anchoredPosition = Vector2.zero;

        bool combatTabOpen = runManager != null && runManager.RunActive &&
                             runManager.State == BattleRunState.Combat &&
                             kineticLoadout != null && kineticLoadout.IsSwitchBoardOpen;

        UpdateRightPanelSuppression(combatTabOpen);

        int focusSlot = ResolveFocusSlot(combatTabOpen);
        bool focused = focusSlot >= 0;

        float scaleT = 1f - Mathf.Exp(-Mathf.Max(4f, focusTweenSharpness) * Time.unscaledDeltaTime);
        float anchorT = 1f - Mathf.Exp(-Mathf.Max(4f, focusAnchorTweenSharpness) * Time.unscaledDeltaTime);

        UpdateFocusAnchor(focusSlot, focused, anchorT);
        ApplyVisualOnlyZoom(focusSlot, focused, scaleT);
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

        if (insideMission || insideChat)
        {
            rightPanelSuppressed = true;
            return;
        }

        // PACK focus의 단일 기준은 고정 hitbox에서 발생한 Pointer Enter/Exit 상태입니다.
        if (inputBridge != null && inputBridge.HoveredSlot >= 0)
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

    private void ApplyVisualOnlyZoom(int focusSlot, bool focused, float t)
    {
        float targetScale = focused ? Mathf.Max(1f, focusedBoardScale) : 1f;
        visualPackScale = Mathf.Lerp(visualPackScale, targetScale, t);
        if (Mathf.Abs(visualPackScale - targetScale) <= 0.001f)
            visualPackScale = targetScale;

        Vector3 anchor = focusAnchorInitialized ? focusAnchorLocal : Vector3.zero;
        Vector3 boardCompensation = anchor * (1f - visualPackScale);

        ApplyBoardDecorZoom(boardCompensation, visualPackScale);

        for (int i = 0; i < SlotCount; i++)
            ApplySlotVisualZoom(i, focusSlot, focused, anchor, visualPackScale);

        if (!focused && Mathf.Abs(visualPackScale - 1f) <= 0.001f)
        {
            visualPackScale = 1f;
            focusAnchorInitialized = false;
        }
    }

    private void ApplyBoardDecorZoom(Vector3 compensation, float scale)
    {
        if (boardBack != null && boardVisualBaseCaptured)
        {
            boardBack.localPosition = boardBackBasePosition + compensation;
            boardBack.localScale = boardBackBaseScale * scale;
        }

        if (synergyLinks != null && boardVisualBaseCaptured)
        {
            synergyLinks.localPosition = synergyBasePosition + compensation;
            synergyLinks.localScale = synergyBaseScale * scale;
        }
    }

    private void ApplySlotVisualZoom(int index, int focusSlot, bool focused, Vector3 anchor, float packScale)
    {
        if (index < 0 || index >= SlotCount)
            return;

        RectTransform slot = slotRects[index];
        if (slot == null)
            return;

        EnsureSlotVisualLayer(index);
        CaptureNewDirectVisualChildren(index);
        SyncSlotBackground(index);

        Vector3 slotCenter = slot.localPosition;
        Vector3 targetCenter = anchor + (slotCenter - anchor) * packScale;
        Vector3 deltaBoard = targetCenter - slotCenter;
        Vector3 deltaLocal = Quaternion.Inverse(slot.localRotation) * deltaBoard;

        float extra = focused && index == focusSlot ? Mathf.Max(1f, focusedSlotExtraScale) : 1f;
        float visualScale = packScale * extra;

        Image visualBack = slotVisualBacks[index];
        if (visualBack != null)
        {
            RectTransform backRect = visualBack.rectTransform;
            backRect.localPosition = deltaLocal;
            backRect.localScale = Vector3.one * visualScale;
        }

        List<VisualChildState> children = slotVisualChildren[index];
        if (children == null)
            return;

        for (int i = children.Count - 1; i >= 0; i--)
        {
            VisualChildState state = children[i];
            if (state == null || state.rect == null)
            {
                children.RemoveAt(i);
                continue;
            }

            state.rect.localPosition = deltaLocal + state.baseLocalPosition * visualScale;
            state.rect.localScale = state.baseLocalScale * visualScale;
        }
    }

    private void EnsureSlotVisualLayer(int index)
    {
        if (index < 0 || index >= SlotCount || slotRects[index] == null)
            return;

        RectTransform slot = slotRects[index];
        Image hitImage = slot.GetComponent<Image>();
        slotHitImages[index] = hitImage;

        Outline hitOutline = slot.GetComponent<Outline>();
        slotHitOutlines[index] = hitOutline;

        if (slotVisualBacks[index] == null)
        {
            RectTransform existing = slot.Find(VisualBackName) as RectTransform;
            if (existing == null)
            {
                GameObject go = new(VisualBackName);
                go.transform.SetParent(slot, false);
                existing = go.AddComponent<RectTransform>();
                existing.anchorMin = Vector2.zero;
                existing.anchorMax = Vector2.one;
                existing.offsetMin = Vector2.zero;
                existing.offsetMax = Vector2.zero;
                existing.localRotation = Quaternion.identity;
                existing.SetAsFirstSibling();
            }

            Image visualBack = existing.GetComponent<Image>();
            if (visualBack == null)
                visualBack = existing.gameObject.AddComponent<Image>();
            visualBack.raycastTarget = false;
            slotVisualBacks[index] = visualBack;

            Outline visualOutline = existing.GetComponent<Outline>();
            if (visualOutline == null)
                visualOutline = existing.gameObject.AddComponent<Outline>();
            visualOutline.useGraphicAlpha = false;
            slotVisualBackOutlines[index] = visualOutline;
        }

        if (hitOutline != null)
        {
            Outline visualOutline = slotVisualBackOutlines[index];
            if (visualOutline != null)
            {
                visualOutline.effectColor = hitOutline.effectColor;
                visualOutline.effectDistance = hitOutline.effectDistance;
                visualOutline.useGraphicAlpha = hitOutline.useGraphicAlpha;
                visualOutline.enabled = true;
            }
            hitOutline.enabled = false;
        }

        if (slotVisualChildren[index] == null)
            slotVisualChildren[index] = new List<VisualChildState>();
    }

    private void CaptureNewDirectVisualChildren(int index)
    {
        RectTransform slot = slotRects[index];
        List<VisualChildState> list = slotVisualChildren[index];
        if (slot == null || list == null)
            return;

        for (int childIndex = 0; childIndex < slot.childCount; childIndex++)
        {
            RectTransform child = slot.GetChild(childIndex) as RectTransform;
            if (child == null || child.name == VisualBackName)
                continue;

            bool known = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].rect == child)
                {
                    known = true;
                    break;
                }
            }

            if (known)
                continue;

            list.Add(new VisualChildState
            {
                rect = child,
                baseLocalPosition = child.localPosition,
                baseLocalScale = child.localScale
            });
        }
    }

    private void SyncSlotBackground(int index)
    {
        Image hitImage = slotHitImages[index];
        Image visualBack = slotVisualBacks[index];
        if (hitImage == null || visualBack == null)
            return;

        Color source = hitImage.color;
        if (source.a > 0.001f)
        {
            slotLastVisualColors[index] = source;
            slotVisualColorCaptured[index] = true;
        }

        if (slotVisualColorCaptured[index])
            visualBack.color = slotLastVisualColors[index];

        // 투명한 Image도 raycastTarget=true이면 Pointer hitbox로 동작합니다.
        // 따라서 입력 Rect는 그대로 두고 시각 배경만 별도 Image가 담당합니다.
        Color hidden = hitImage.color;
        hidden.a = 0f;
        hitImage.color = hidden;
        hitImage.raycastTarget = true;
    }

    private int ResolveFocusSlot(bool combatTabOpen)
    {
        if (!combatTabOpen || rightPanelSuppressed || inputBridge == null)
            return -1;

        int hovered = inputBridge.HoveredSlot;
        if (hovered < 0 || hovered >= SlotCount || slotRects[hovered] == null)
            return -1;

        return hovered;
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

        // Focus 슬롯의 시각 중심은 visual-only zoom에서도 기준점 자리에 유지됩니다.
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
        if (force || inputBridge == null)
            inputBridge = FindFirstObjectByType<BattleCombatHudInputBridge>(FindObjectsInactive.Include);
    }

    private void ResolveUi(bool force)
    {
        if (force || boardRoot == null)
            boardRoot = kineticLoadout != null ? kineticLoadout.GridBoard : null;

        if (boardRoot != null)
        {
            // 이전 버전의 transform zoom 잔상을 즉시 제거합니다.
            boardRoot.localScale = Vector3.one;
            boardRoot.anchoredPosition = Vector2.zero;

            if (force || boardBack == null)
                boardBack = boardRoot.Find("BoardBack") as RectTransform;
            if (force || synergyLinks == null)
                synergyLinks = boardRoot.Find("SynergyLinks") as RectTransform;

            if (!boardVisualBaseCaptured)
            {
                if (boardBack != null)
                {
                    boardBackBasePosition = boardBack.localPosition;
                    boardBackBaseScale = boardBack.localScale;
                }
                if (synergyLinks != null)
                {
                    synergyBasePosition = synergyLinks.localPosition;
                    synergyBaseScale = synergyLinks.localScale;
                }
                boardVisualBaseCaptured = true;
            }

            for (int i = 0; i < SlotCount; i++)
            {
                if (force || slotRects[i] == null)
                    slotRects[i] = boardRoot.Find($"GridSlot_{i}") as RectTransform;

                if (slotRects[i] == null)
                    continue;

                // 입력 RectTransform은 항상 원래 상태로 고정합니다.
                slotRects[i].localScale = Vector3.one;
                EnsureSlotVisualLayer(i);
                CaptureNewDirectVisualChildren(i);
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

        if (boardBack != null && boardVisualBaseCaptured)
        {
            boardBack.localPosition = boardBackBasePosition;
            boardBack.localScale = boardBackBaseScale;
        }
        if (synergyLinks != null && boardVisualBaseCaptured)
        {
            synergyLinks.localPosition = synergyBasePosition;
            synergyLinks.localScale = synergyBaseScale;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slot = slotRects[i];
            if (slot != null)
                slot.localScale = Vector3.one;

            if (slotVisualBacks[i] != null)
                slotVisualBacks[i].gameObject.SetActive(false);

            Image hitImage = slotHitImages[i];
            if (hitImage != null && slotVisualColorCaptured[i])
            {
                hitImage.color = slotLastVisualColors[i];
                hitImage.raycastTarget = true;
            }

            Outline hitOutline = slotHitOutlines[i];
            if (hitOutline != null)
                hitOutline.enabled = true;

            List<VisualChildState> children = slotVisualChildren[i];
            if (children == null)
                continue;

            for (int j = 0; j < children.Count; j++)
            {
                VisualChildState state = children[j];
                if (state == null || state.rect == null)
                    continue;
                state.rect.localPosition = state.baseLocalPosition;
                state.rect.localScale = state.baseLocalScale;
            }
        }

        visualPackScale = 1f;
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

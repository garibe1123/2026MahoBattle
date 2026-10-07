using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Combat Tab과 Reward PACK 편집이 공유하는 Inventory UI의 authoritative layout owner입니다.
///
/// 이 클래스만 다음 RectTransform / CanvasGroup 상태를 씁니다.
/// - 좌측 하단 Mini PACK의 위치/크기/표시 상태
/// - Reward 편집 중 LoadoutSwitchFull / GridBoard의 위치와 스케일
/// - Reward 편집 중 외부 EquipmentDetailPanel의 고정 위치
/// - Reward TRASH / DONE의 화면 안전영역 위치
/// - Reward Full Grid의 단일 선택/호버 Stroke
///
/// Combat Full PACK의 위치/깊이/상세 패널은 BattleKineticLoadoutUI가 단독 소유합니다.
///
/// 슬롯 데이터와 교환 규칙은 BattleEquipmentSystem,
/// Reward 상태는 BattleRewardFlow,
/// 슬롯 입력은 BattleInventoryInteractionController가 소유합니다.
/// Reward 편집 slow-motion은 BattleTimeScaleController에 요청하며 Time.timeScale을 직접 쓰지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33380)]
public sealed class BattleUnifiedInventoryInspectController : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const int DismissCanvasSortingOrder = 779;
    private const float MiniCellSize = 78f;
    private const float MiniCellGap = 7f;
    private const float MiniGridTotal = MiniCellSize * 3f + MiniCellGap * 2f;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleRewardFlow rewardFlow;
    [SerializeField] private BattleInventoryInteractionController inventoryInteraction;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;
    [SerializeField] private BattleTimeScaleController timeScaleController;

    [Header("Mini PACK")]
    [SerializeField] private Vector2 miniPackSize = new(304f, 326f);
    [SerializeField] private Vector2 combatMiniPackPosition = new(22f, 22f);
    [SerializeField] private Vector2 rewardChoiceMiniPackPosition = new(34f, 28f);
    [SerializeField] private Vector2 miniGridOffset = new(20f, 18f);
    [SerializeField] private Vector2 miniHeaderSize = new(126f, 40f);
    [SerializeField] private Vector2 miniIconSize = new(64f, 64f);
    [SerializeField, Range(0.45f, 0.90f)] private float rewardChoiceMiniPackScale = 0.68f;
    [SerializeField, Range(0.5f, 1f)] private float rewardChoiceMiniPackAlpha = 0.82f;

    [Header("Full Inventory")]
    [SerializeField] private Vector2 boardAnchor = new(0.31f, 0.59f);
    [SerializeField, Range(0.90f, 1.08f)] private float fullGridScale = 1f;

    [Header("Reward Controls")]
    [SerializeField] private Vector2 trashAttachOffset = new(-8f, 0f);
    [SerializeField] private Vector2 rewardDoneBelowBoardOffset = new(-128f, -18f);
    [SerializeField] private Vector2 rewardTrashBelowBoardOffset = new(128f, -18f);

    [Header("Reward Edit Time")]
    [SerializeField, Range(0.02f, 0.20f)] private float rewardInventoryTimeScale = 0.05f;

    [Header("Selection")]
    [SerializeField] private Color selectedAccent = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Color hoverAccent = new(0.14f, 0.92f, 0.94f, 1f);
    [SerializeField] private Color pickedAccent = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField, Range(0.06f, 0.30f)] private float selectedFillAlpha = 0.18f;
    [SerializeField] private Vector2 selectionCancelSize = new(190f, 38f);
    [SerializeField, Min(0f)] private float selectionCancelGap = 12f;

    private RectTransform fullRoot;
    private CanvasGroup fullGroup;
    private RectTransform boardRoot;
    private CanvasGroup boardGroup;
    private RectTransform builtInDetailRoot;

    private RectTransform miniPackRoot;
    private RectTransform miniPackCells;
    private CanvasGroup miniPackGroup;

    private RectTransform detailRoot;
    private CanvasGroup detailGroup;
    private Outline detailOutline;

    private RectTransform trashRoot;
    private RectTransform doneRoot;
    private Text fullTitle;
    private Text fullSubtitle;

    private readonly GameObject[] fullSelectionFrames = new GameObject[SlotCount];
    private readonly RectTransform[] fullSlotRects = new RectTransform[SlotCount];

    private Canvas dismissCanvas;
    private RectTransform selectionCancelRoot;
    private CanvasGroup selectionCancelGroup;
    private Button selectionCancelButton;
    private Text selectionCancelLabel;

    private bool selectionSuppressed;
    private int suppressedSourceSlot = -1;
    private int activeInspectSlot = -1;
    private bool inspectContextWasActive;
    private bool combatTabOpen;
    private bool combatActive;
    private BattleKineticLoadoutUI subscribedCombatLoadout;
    private BattleRunManager subscribedRunManager;
    private float nextResolveTime;
    private bool rewardUiValidationPending;
    private float rewardUiValidationAt;

    public int ActiveInspectSlot => activeInspectSlot;

    private void Awake()
    {
        ResolveReferences();
        ResolveUi();
        EnsureDismissCanvas();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeRunState();
        SubscribeCombatLoadout();
        combatActive = ResolveCombatState();
        combatTabOpen = kineticLoadout != null && kineticLoadout.IsSwitchBoardOpen;
        ResolveUi();
        EnsureDismissCanvas();
        nextResolveTime = 0f;
    }

    private void OnDisable()
    {
        UnsubscribeRunState();
        UnsubscribeCombatLoadout();
        combatActive = false;
        combatTabOpen = false;
        inspectContextWasActive = false;
        selectionSuppressed = false;
        suppressedSourceSlot = -1;
        SetActiveInspectSlot(-1);
        RestoreBulletTime(true);
    }

    private void Update()
    {
        ResolveReferences();
        rewardFlow?.RefreshFromRunState();
        EnsureDismissCanvas();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.10f;
            ResolveUi();
            if (IsRewardEdit())
                EnsureFullSelectionFrames();
        }

        bool rewardEdit = IsRewardEdit();
        bool inspectContext = rewardEdit;

        HandleInspectContextTransition(inspectContext);

        if (rewardEdit)
            MaintainRewardBulletTime();
        else
            RestoreBulletTime(false);

        // Combat selection/detail is owned by BattleKineticLoadoutUI.
        SyncSelection(rewardEdit, false);
        HandleCancelInput(inspectContext);
        HandleMouseOutsideCancel(inspectContext);

        // 바깥 클릭 취소는 HandleMouseOutsideCancel()의 Screen Rect 판정만 사용합니다.
    }

    private void LateUpdate()
    {
        ResolveUi();

        bool rewardChoice = IsRewardChoice();
        bool rewardEdit = IsRewardEdit();
        bool rewardPackVisible = IsRewardPackVisible();
        bool combatTab = combatTabOpen;
        bool combat = combatActive;

        ApplyMiniPackGeometry();
        ApplyMiniPackContext(rewardChoice, rewardPackVisible, combatTab, combat);
        ApplyFullInventoryLayout(rewardPackVisible);
        AttachContextControls(rewardEdit, combat);
        ApplySelectionFrames(rewardEdit, rewardEdit);
        ApplyRewardInspectTooltip(rewardEdit);
        UpdateSelectionCancelButton(rewardEdit);

        // Reward TV/Presenter보다 PACK을 앞에, Detail을 그보다 더 앞에 고정합니다.
        // Spatial coordinator가 매 프레임 sorting을 계산하더라도 이 Controller가
        // 더 늦은 ExecutionOrder에서 Reward 전용 절대 순서를 최종 확정합니다.
        ApplyRewardForegroundSorting(rewardPackVisible);
        ValidateRewardUiOnce(rewardPackVisible);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();

        if (isActiveAndEnabled)
            SubscribeRunState();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (rewardFlow == null)
            rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);

        SubscribeCombatLoadout();
        if (detailController == null)
            detailController = FindFirstObjectByType<BattleEquipmentDetailPanelController>(FindObjectsInactive.Include);
        if (timeScaleController == null)
            timeScaleController = BattleTimeScaleController.ResolveOrCreate(this);
    }

    private bool ResolveCombatState()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Combat;
    }

    private void SubscribeRunState()
    {
        if (subscribedRunManager == runManager)
            return;

        if (subscribedRunManager != null)
            subscribedRunManager.StateChanged -= HandleRunStateChanged;

        subscribedRunManager = runManager;
        if (subscribedRunManager != null)
            subscribedRunManager.StateChanged += HandleRunStateChanged;
    }

    private void UnsubscribeRunState()
    {
        if (subscribedRunManager != null)
            subscribedRunManager.StateChanged -= HandleRunStateChanged;

        subscribedRunManager = null;
    }

    private void HandleRunStateChanged(BattleRunState _)
    {
        combatActive = ResolveCombatState();
        if (!combatActive)
            combatTabOpen = false;
    }

    private bool IsRewardChoice()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward &&
               (rewardFlow == null || rewardFlow.Phase == BattleRewardPhase.Choosing);
    }

    private bool IsRewardEdit()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward &&
               rewardFlow != null && rewardFlow.Phase == BattleRewardPhase.PackEditing;
    }

    private bool IsRewardPackVisible()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward &&
               rewardFlow != null &&
               (rewardFlow.Phase == BattleRewardPhase.Transferring ||
                rewardFlow.Phase == BattleRewardPhase.PackEditing);
    }

    private void SubscribeCombatLoadout()
    {
        if (subscribedCombatLoadout == kineticLoadout)
            return;

        if (subscribedCombatLoadout != null)
            subscribedCombatLoadout.SwitchBoardVisibilityChanged -= HandleCombatBoardVisibilityChanged;

        subscribedCombatLoadout = kineticLoadout;
        if (subscribedCombatLoadout != null)
        {
            subscribedCombatLoadout.SwitchBoardVisibilityChanged += HandleCombatBoardVisibilityChanged;
            combatTabOpen = subscribedCombatLoadout.IsSwitchBoardOpen;
        }
    }

    private void UnsubscribeCombatLoadout()
    {
        if (subscribedCombatLoadout != null)
            subscribedCombatLoadout.SwitchBoardVisibilityChanged -= HandleCombatBoardVisibilityChanged;

        subscribedCombatLoadout = null;
    }

    private void HandleCombatBoardVisibilityChanged(bool visible)
    {
        combatTabOpen = visible;
    }

    private void ResolveUi()
    {
        if (kineticLoadout != null)
        {
            fullRoot ??= kineticLoadout.FullRoot;
            fullGroup ??= kineticLoadout.FullGroup;
            boardRoot ??= kineticLoadout.GridBoard;
            if (boardRoot != null && boardGroup == null)
                boardGroup = boardRoot.GetComponent<CanvasGroup>();
        }

        if (fullRoot == null)
        {
            fullRoot = FindRect("LoadoutSwitchFull");
            if (fullRoot != null)
            {
                fullGroup = fullRoot.GetComponent<CanvasGroup>();
                boardRoot = FindChildRect(fullRoot, "GridBoard");
                if (boardRoot != null)
                    boardGroup = boardRoot.GetComponent<CanvasGroup>();
            }
        }

        if (fullRoot != null && builtInDetailRoot == null)
        {
            builtInDetailRoot = FindChildRect(fullRoot, "DetailPanel");
            ResolveFullHeaderTexts();
        }

        if (miniPackRoot == null)
        {
            miniPackRoot = FindRect("BackpackMiniGrid");
            if (miniPackRoot != null)
            {
                miniPackGroup = miniPackRoot.GetComponent<CanvasGroup>();
                miniPackCells = miniPackRoot.Find("BackpackCells") as RectTransform;
            }
        }

        if (detailRoot == null)
        {
            detailRoot = FindRect("EquipmentDetailPanel");
            if (detailRoot != null)
            {
                detailGroup = detailRoot.GetComponent<CanvasGroup>();
                detailOutline = detailRoot.GetComponent<Outline>();
            }
        }

        trashRoot ??= FindRect("InventoryTrash");
        doneRoot ??= FindRect("RewardPackDone");
    }

    private void ResolveFullHeaderTexts()
    {
        if (fullRoot == null)
            return;

        Text[] texts = fullRoot.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null)
                continue;

            string value = text.text ?? string.Empty;
            if (value == "LOADOUT // SHIFT" || value == "PACK // EDIT")
                fullTitle = text;
            else if (value.Contains("HOLD TAB / LB") || value.Contains("MOVE / SWAP") ||
                     value.Contains("CLICK INSPECT") || value.Contains("STICK MOVE"))
                fullSubtitle = text;
        }
    }

    private void ApplyMiniPackGeometry()
    {
        if (miniPackRoot == null)
            return;

        miniPackRoot.anchorMin = miniPackRoot.anchorMax = Vector2.zero;
        miniPackRoot.pivot = Vector2.zero;
        miniPackRoot.sizeDelta = miniPackSize;

        RectTransform header = miniPackRoot.Find("PackHeaderTag") as RectTransform;
        if (header != null)
        {
            header.sizeDelta = miniHeaderSize;
            header.anchoredPosition = new Vector2(10f, -5f);
        }

        Text[] headerTexts = miniPackRoot.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < headerTexts.Length; i++)
        {
            Text text = headerTexts[i];
            if (text == null)
                continue;
            if (text.text == "PACK")
                text.fontSize = 18;
            else if (text.text != null && text.text.Contains("/ 9"))
                text.fontSize = 12;
            else if (text.text != null && (text.text.Contains("TAB") || text.text.Contains("LB")))
                text.fontSize = 9;
        }

        miniPackCells ??= miniPackRoot.Find("BackpackCells") as RectTransform;
        if (miniPackCells == null)
            return;

        miniPackCells.anchorMin = miniPackCells.anchorMax = Vector2.zero;
        miniPackCells.pivot = Vector2.zero;
        miniPackCells.sizeDelta = new Vector2(MiniGridTotal, MiniGridTotal);
        miniPackCells.anchoredPosition = miniGridOffset;

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slot = miniPackCells.Find($"BackpackCell_{i}") as RectTransform;
            if (slot == null)
                continue;

            int x = i % BattleEquipmentSystem.GridSize;
            int y = i / BattleEquipmentSystem.GridSize;
            slot.sizeDelta = new Vector2(MiniCellSize, MiniCellSize);
            slot.anchorMin = slot.anchorMax = Vector2.zero;
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.anchoredPosition = new Vector2(
                x * (MiniCellSize + MiniCellGap) + MiniCellSize * 0.5f,
                MiniGridTotal - (y * (MiniCellSize + MiniCellGap) + MiniCellSize * 0.5f));

            RectTransform icon = slot.Find("Icon") as RectTransform;
            if (icon != null)
            {
                icon.sizeDelta = miniIconSize;
                icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
                icon.anchoredPosition = Vector2.zero;
            }

            RectTransform accent = slot.Find("CellAccent") as RectTransform;
            if (accent != null)
            {
                accent.sizeDelta = new Vector2(4f, MiniCellSize - 12f);
                accent.anchoredPosition = new Vector2(4f, 0f);
            }
        }
    }

    private void ApplyMiniPackContext(bool rewardChoice, bool rewardEdit, bool combatTab, bool combat)
    {
        if (miniPackRoot == null || miniPackGroup == null)
            return;

        float morphProgress =
            combat && kineticLoadout != null
                ? kineticLoadout.PackMorphProgress
                : 0f;

        if (combat && morphProgress > 0.001f)
        {
            ApplyCombatMiniPackMorph(morphProgress);
            return;
        }

        Vector2 targetPosition = combatMiniPackPosition;
        float targetScale = 0.92f;
        float targetAlpha = 0f;
        float targetZ = 24f;
        Quaternion targetRotation = Quaternion.Euler(2.4f, -5f, 0.8f);
        bool interactive = false;

        if (rewardEdit)
        {
            targetPosition = combatMiniPackPosition + new Vector2(-10f, -6f);
            targetScale = 0.90f;
            targetAlpha = 0f;
            targetZ = 30f;
            targetRotation = Quaternion.Euler(3f, -6f, 1.0f);
        }
        else if (rewardChoice)
        {
            targetPosition = rewardChoiceMiniPackPosition;
            targetScale = rewardChoiceMiniPackScale;
            targetAlpha = rewardChoiceMiniPackAlpha;
            targetZ = 8f;
            targetRotation = Quaternion.Euler(1.0f, -2.2f, -0.25f);
        }
        else if (combat)
        {
            targetPosition = combatMiniPackPosition;
            targetScale = 1f;
            targetAlpha = 1f;
            targetZ = -4f;
            targetRotation = Quaternion.Euler(0.35f, -1.2f, -0.15f);
            interactive = !combatTab && !BattlePauseController.IsPaused;
        }

        float t = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);

        miniPackRoot.anchoredPosition = Vector2.Lerp(
            miniPackRoot.anchoredPosition,
            targetPosition,
            t);

        miniPackRoot.localScale = Vector3.Lerp(
            miniPackRoot.localScale,
            Vector3.one * targetScale,
            t);

        miniPackRoot.localRotation = Quaternion.Slerp(
            miniPackRoot.localRotation,
            targetRotation,
            t);

        Vector3 local = miniPackRoot.localPosition;
        local.z = Mathf.Lerp(local.z, targetZ, t);
        miniPackRoot.localPosition = local;

        miniPackGroup.alpha = Mathf.Lerp(
            miniPackGroup.alpha,
            targetAlpha,
            t);

        bool canRaycast = interactive && miniPackGroup.alpha >= 0.92f;
        miniPackGroup.blocksRaycasts = canRaycast;
        miniPackGroup.interactable = canRaycast;
    }

    private void ApplyCombatMiniPackMorph(float progress)
    {
        if (miniPackRoot == null || miniPackGroup == null || boardRoot == null)
            return;

        float eased = SmoothPackMorph(progress);
        float targetScale = ResolveMiniPackToBoardScale();

        Vector2 targetPosition = ResolveMiniPackMorphTarget(targetScale);
        miniPackRoot.anchoredPosition = Vector2.Lerp(
            combatMiniPackPosition,
            targetPosition,
            eased);

        miniPackRoot.localScale = Vector3.one *
                                  Mathf.Lerp(1f, targetScale, eased);

        Quaternion startRotation = Quaternion.Euler(0.35f, -1.2f, -0.15f);
        Quaternion targetRotation = boardRoot.localRotation;
        miniPackRoot.localRotation = Quaternion.Slerp(
            startRotation,
            targetRotation,
            eased);

        Vector3 local = miniPackRoot.localPosition;
        local.z = Mathf.Lerp(-4f, -14f, eased);
        miniPackRoot.localPosition = local;

        float fade = 1f - SmoothPackRange(progress, 0.58f, 0.96f);
        miniPackGroup.alpha = fade;
        miniPackGroup.blocksRaycasts = false;
        miniPackGroup.interactable = false;
    }

    private float ResolveMiniPackToBoardScale()
    {
        if (miniPackRoot == null || boardRoot == null)
            return 1f;

        Vector3[] boardCorners = new Vector3[4];
        boardRoot.GetWorldCorners(boardCorners);

        Vector2 boardBottomLeft = RectTransformUtility.WorldToScreenPoint(null, boardCorners[0]);
        Vector2 boardTopRight = RectTransformUtility.WorldToScreenPoint(null, boardCorners[2]);

        float boardWidth = Mathf.Abs(boardTopRight.x - boardBottomLeft.x);
        float boardHeight = Mathf.Abs(boardTopRight.y - boardBottomLeft.y);

        Canvas miniCanvas = miniPackRoot.GetComponentInParent<Canvas>();
        float canvasScale = miniCanvas != null
            ? Mathf.Max(0.0001f, miniCanvas.scaleFactor)
            : 1f;

        float miniWidth = Mathf.Max(1f, miniPackRoot.rect.width * canvasScale);
        float miniHeight = Mathf.Max(1f, miniPackRoot.rect.height * canvasScale);

        float scale = Mathf.Min(
            boardWidth / miniWidth,
            boardHeight / miniHeight);

        return Mathf.Clamp(scale, 1.35f, 2.35f);
    }

    private Vector2 ResolveMiniPackMorphTarget(float targetScale)
    {
        if (miniPackRoot == null || boardRoot == null)
            return combatMiniPackPosition;

        RectTransform parent = miniPackRoot.parent as RectTransform;
        if (parent == null)
            return combatMiniPackPosition;

        Vector3 boardCenterWorld = boardRoot.TransformPoint(boardRoot.rect.center);
        Vector2 boardCenterScreen = RectTransformUtility.WorldToScreenPoint(
            null,
            boardCenterWorld);

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent,
                boardCenterScreen,
                null,
                out Vector2 boardCenterLocal))
        {
            return combatMiniPackPosition;
        }

        Vector2 scaledCenterOffset = miniPackRoot.rect.center * targetScale;
        Vector2 targetPivotLocal = boardCenterLocal - scaledCenterOffset;

        Vector2 anchor = miniPackRoot.anchorMin;
        Rect parentRect = parent.rect;
        Vector2 anchorLocal = new(
            Mathf.Lerp(parentRect.xMin, parentRect.xMax, anchor.x),
            Mathf.Lerp(parentRect.yMin, parentRect.yMax, anchor.y));

        return targetPivotLocal - anchorLocal;
    }

    private static float SmoothPackMorph(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static float SmoothPackRange(float value, float start, float end)
    {
        if (end <= start + 0.0001f)
            return value >= end ? 1f : 0f;

        float t = Mathf.Clamp01((value - start) / (end - start));
        return t * t * (3f - 2f * t);
    }

    private void ApplyRewardForegroundSorting(bool rewardPackVisible)
    {
        if (!rewardPackVisible)
            return;

        if (boardRoot != null)
        {
            Canvas boardCanvas =
                boardRoot.GetComponent<Canvas>();

            if (boardCanvas == null)
                boardCanvas = boardRoot.gameObject.AddComponent<Canvas>();

            boardCanvas.overrideSorting =
                true;

            boardCanvas.sortingOrder =
                BattleUiSortingContract.RewardPack;

            // PACK은 실제 Slot click/drag Owner이므로 Raycaster를 보장합니다.
            if (boardRoot.GetComponent<GraphicRaycaster>() == null)
                boardRoot.gameObject.AddComponent<GraphicRaycaster>();
        }

        if (builtInDetailRoot != null)
        {
            Canvas detailCanvas =
                builtInDetailRoot.GetComponent<Canvas>();

            if (detailCanvas == null)
                detailCanvas = builtInDetailRoot.gameObject.AddComponent<Canvas>();

            detailCanvas.overrideSorting =
                true;

            detailCanvas.sortingOrder =
                BattleUiSortingContract.RewardDetail;

            // Tooltip은 클릭을 먹지 않습니다.
            GraphicRaycaster detailRaycaster =
                builtInDetailRoot.GetComponent<GraphicRaycaster>();

            if (detailRaycaster != null)
                detailRaycaster.enabled = false;

            builtInDetailRoot.SetAsLastSibling();
        }
    }

    private void ApplyFullInventoryLayout(bool rewardEdit)
    {
        if (rewardEdit)
        {
            if (boardRoot != null)
            {
                boardRoot.anchorMin = boardRoot.anchorMax = boardAnchor;
                boardRoot.pivot = new Vector2(0.5f, 0.5f);
                boardRoot.anchoredPosition = Vector2.zero;
                boardRoot.localRotation = Quaternion.identity;

                Vector3 boardLocal = boardRoot.localPosition;
                boardLocal.z = 0f;
                boardRoot.localPosition = boardLocal;
                boardRoot.localScale = Vector3.one;

                // Combat morph owns this CanvasGroup during battle.
                // Reward PACK editing explicitly takes ownership so the 3x3 grid
                // cannot remain transparent after the combat animation closed it.
                if (boardGroup == null)
                    boardGroup = boardRoot.GetComponent<CanvasGroup>();
                if (boardGroup != null)
                {
                    boardGroup.alpha = 1f;
                    boardGroup.blocksRaycasts = !BattlePauseController.IsPaused;
                    boardGroup.interactable = !BattlePauseController.IsPaused;
                }
            }

            if (fullRoot != null)
            {
                fullRoot.localScale = Vector3.one * fullGridScale;
                fullRoot.localRotation = Quaternion.identity;

                Vector3 rootLocal = fullRoot.localPosition;
                rootLocal.z = 0f;
                fullRoot.localPosition = rootLocal;
            }

            if (fullRoot != null && fullGroup != null)
            {
                fullRoot.gameObject.SetActive(true);
                fullGroup.alpha = 1f;
                fullGroup.blocksRaycasts = !BattlePauseController.IsPaused;
                fullGroup.interactable = !BattlePauseController.IsPaused;
            }
        }

        if (fullTitle != null)
            fullTitle.text = rewardEdit ? string.Empty : "LOADOUT // SHIFT";

        if (fullSubtitle != null)
        {
            if (rewardEdit)
            {
                bool pad = inventoryInteraction != null && inventoryInteraction.PadModeActive;
                fullSubtitle.text = pad
                    ? "STICK MOVE  •  A PICK / PLACE  •  B CANCEL  •  Y TRASH  •  MENU DONE"
                    : "CLICK INSPECT  •  DRAG MOVE / SWAP  •  TRASH  •  DONE";
            }
            else
            {
                fullSubtitle.text = "HOLD TAB / LB  •  SELECT SLOT  •  RELEASE TO EQUIP";
            }
        }

        // Combat and Reward PACK inspect now share the same built-in tooltip.
        if (builtInDetailRoot != null && rewardEdit)
            builtInDetailRoot.gameObject.SetActive(true);
    }

    private void AttachContextControls(bool rewardEdit, bool combat)
    {
        if (rewardEdit && boardRoot != null)
        {
            AttachRewardControlBelowBoard(doneRoot, boardRoot, rewardDoneBelowBoardOffset);
            AttachRewardControlBelowBoard(trashRoot, boardRoot, rewardTrashBelowBoardOffset);
            return;
        }

        if (combat && inventoryInteraction != null && inventoryInteraction.IsDraggingItem && miniPackRoot != null)
            AttachControl(trashRoot, miniPackRoot, trashAttachOffset);
    }

    private static void AttachRewardControlBelowBoard(
        RectTransform control,
        RectTransform board,
        Vector2 offset)
    {
        if (control == null || board == null)
            return;

        if (control.parent != board)
            control.SetParent(board, false);

        control.anchorMin = control.anchorMax = new Vector2(0.5f, 0f);
        control.pivot = new Vector2(0.5f, 1f);
        control.anchoredPosition = offset;
        control.localRotation = Quaternion.identity;
        control.localScale = Vector3.one;
        control.SetAsLastSibling();
    }

    private static void AttachControl(RectTransform control, RectTransform parent, Vector2 localOffset)
    {
        if (control == null || parent == null)
            return;

        if (control.parent != parent)
            control.SetParent(parent, false);

        control.anchorMin = control.anchorMax = new Vector2(1f, 0f);
        control.pivot = new Vector2(0f, 0f);
        control.anchoredPosition = localOffset;
        control.localRotation = Quaternion.identity;
        control.localScale = Vector3.one;
        control.SetAsLastSibling();
    }

    private void HandleInspectContextTransition(bool inspectContext)
    {
        if (inspectContextWasActive == inspectContext)
            return;

        inspectContextWasActive = inspectContext;

        // PACK/Reward Edit를 닫았다가 다시 여는 것은 새 Inspect 세션입니다.
        // 이전 세션에서 명시적으로 걸린 suppression을 다음 세션까지
        // 유지하면 KineticLoadout이 같은 SelectedIndex를 복원해도 상세가 영구히 막힙니다.
        selectionSuppressed = false;
        suppressedSourceSlot = -1;

        rewardUiValidationPending =
            inspectContext;

        rewardUiValidationAt =
            Time.unscaledTime +
            0.75f;

        if (!inspectContext)
            SetActiveInspectSlot(-1);
    }

    private void ValidateRewardUiOnce(bool rewardPackVisible)
    {
        if (!rewardPackVisible ||
            !rewardUiValidationPending ||
            Time.unscaledTime < rewardUiValidationAt)
        {
            return;
        }

        rewardUiValidationPending =
            false;

        ResolveUi();
        EnsureFullSelectionFrames();

        string missing =
            string.Empty;

        void AddMissing(string label)
        {
            if (missing.Length > 0)
                missing += ", ";

            missing += label;
        }

        if (fullRoot == null)
            AddMissing("LoadoutSwitchFull");

        if (boardRoot == null)
            AddMissing("GridBoard");

        if (builtInDetailRoot == null)
            AddMissing("DetailPanel");

        if (trashRoot == null)
            AddMissing("InventoryTrash");

        if (doneRoot == null)
            AddMissing("RewardPackDone");

        for (int i = 0; i < SlotCount; i++)
        {
            if (fullSlotRects[i] == null)
                AddMissing($"GridSlot_{i}");
        }

        if (missing.Length == 0)
            return;

        Debug.LogError(
            $"[BattleInventoryUI] Reward PACK 필수 참조를 찾지 못했습니다: {missing}. " +
            "Hierarchy 이름 변경 또는 UI 생성 순서를 확인하세요.",
            this);
    }

    private void SyncSelection(bool rewardEdit, bool combatTab)
    {
        int source = -1;

        if (rewardEdit && inventoryInteraction != null)
        {
            int pad = inventoryInteraction.PadSelectedSlot;
            int mouse = inventoryInteraction.SelectedRewardSlot;
            int hover = inventoryInteraction.HoveredSlot;

            if (inventoryInteraction.PadModeActive)
            {
                source = HasItem(pad) ? pad : -1;
            }
            else if (inventoryInteraction.DraggingSlot >= 0)
            {
                // Mouse Drag 중에는 Hover 대상이 아니라 "들고 있는 Source 아이템"을
                // 상세 정보의 Owner로 고정합니다.
                // 그래야 다른 슬롯 위를 지나도 Target 아이템 정보로 교차되지 않습니다.
                int dragging =
                    inventoryInteraction.DraggingSlot;

                source =
                    HasItem(dragging)
                        ? dragging
                        : -1;
            }
            else if (HasItem(mouse))
            {
                source = mouse;
            }
            else
            {
                source = HasItem(hover) ? hover : -1;
            }
        }
        else if (combatTab && kineticLoadout != null)
        {
            source = kineticLoadout.SelectedIndex;
        }

        if (selectionSuppressed)
        {
            if (source >= 0 && source != suppressedSourceSlot)
            {
                selectionSuppressed = false;
                suppressedSourceSlot = -1;
            }
            else
            {
                SetActiveInspectSlot(-1);
                return;
            }
        }

        SetActiveInspectSlot(HasItem(source) ? source : -1);
    }

    private void SetActiveInspectSlot(int slotIndex)
    {
        if (activeInspectSlot == slotIndex)
            return;

        activeInspectSlot = slotIndex;

        if (IsRewardEdit())
        {
            detailController?.Hide();
            if (slotIndex >= 0)
                kineticLoadout?.ShowRewardInspectTooltip(slotIndex);
            else
                kineticLoadout?.HideRewardInspectTooltip();
        }
        else
        {
            detailController?.SelectSlotFromPointer(slotIndex);
        }
    }

    public void CancelSelection()
    {
        int current = activeInspectSlot;
        if (current < 0 && kineticLoadout != null)
            current = kineticLoadout.SelectedIndex;
        if (current < 0 && inventoryInteraction != null)
            current = inventoryInteraction.SelectedRewardSlot;

        selectionSuppressed = true;
        suppressedSourceSlot = current;
        activeInspectSlot = -1;

        inventoryInteraction?.ClearInspectSelection();
        kineticLoadout?.ClearExternalSelection();
        kineticLoadout?.HideRewardInspectTooltip();
        detailController?.SelectSlotFromPointer(-1);

        if (detailGroup != null)
            detailGroup.alpha = 0f;
    }

    private void HandleCancelInput(bool inspectContext)
    {
        if (!inspectContext || BattlePauseController.IsPaused)
            return;

        if (Input.GetKeyDown(KeyCode.JoystickButton1))
            CancelSelection();
    }

    private void HandleMouseOutsideCancel(bool inspectContext)
    {
        if (!inspectContext ||
            BattlePauseController.IsPaused ||
            !Input.mousePresent ||
            !Input.GetMouseButtonDown(0) ||
            inventoryInteraction == null ||
            inventoryInteraction.PadModeActive ||
            inventoryInteraction.DraggingSlot >= 0 ||
            inventoryInteraction.SelectedRewardSlot < 0)
        {
            return;
        }

        Vector2 pointer =
            Input.mousePosition;

        // PACK 조작 영역과 현재 선택 관련 컨트롤은 모두 Selection 내부로 봅니다.
        // 이 영역들을 클릭할 때 Cancel이 먼저 실행되면 Slot/Trash/Done/Button 입력이 깨집니다.
        if (IsPointerInsideRect(boardRoot, pointer) ||
            IsPointerInsideRect(builtInDetailRoot, pointer) ||
            IsVisiblePointerInsideRect(selectionCancelRoot, selectionCancelGroup, pointer) ||
            IsPointerInsideRect(trashRoot, pointer) ||
            IsPointerInsideRect(doneRoot, pointer))
        {
            return;
        }

        // 다른 실제 UI 위를 누른 경우에도 해당 UI의 입력을 우선합니다.
        // Selection 취소는 정말 빈 화면을 클릭했을 때만 수행합니다.
        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        CancelSelection();
    }

    private static bool IsVisiblePointerInsideRect(
        RectTransform rect,
        CanvasGroup group,
        Vector2 pointer)
    {
        return rect != null &&
               group != null &&
               group.alpha > 0.01f &&
               rect.gameObject.activeInHierarchy &&
               IsPointerInsideRect(
                   rect,
                   pointer);
    }

    private static bool IsPointerInsideRect(
        RectTransform rect,
        Vector2 pointer)
    {
        if (rect == null ||
            !rect.gameObject.activeInHierarchy)
        {
            return false;
        }

        Canvas canvas =
            rect.GetComponentInParent<Canvas>();

        Camera eventCamera =
            canvas != null &&
            canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

        return RectTransformUtility.RectangleContainsScreenPoint(
            rect,
            pointer,
            eventCamera);
    }

    private void EnsureFullSelectionFrames()
    {
        if (boardRoot == null)
            return;

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slot = fullSlotRects[i];
            if (slot == null)
            {
                slot = FindChildRect(boardRoot, $"GridSlot_{i}");
                fullSlotRects[i] = slot;
            }
            if (slot == null)
                continue;

            if (fullSelectionFrames[i] != null)
                continue;

            RectTransform frame = slot.Find("UnifiedSelectionFrame") as RectTransform;
            if (frame == null)
            {
                frame = CreateRect(slot, "UnifiedSelectionFrame", Vector2.zero);
                Stretch(frame);
            }

            DisableLegacyOutlineFrame(frame);
            EnsureStrokeEdges(frame);

            fullSelectionFrames[i] = frame.gameObject;
            frame.gameObject.SetActive(false);
        }
    }

    private void ApplySelectionFrames(bool inspectContext, bool rewardEdit)
    {
        EnsureFullSelectionFrames();
        float pulse01 = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6.2f);

        bool dragging =
            rewardEdit &&
            inventoryInteraction != null &&
            !inventoryInteraction.PadModeActive &&
            inventoryInteraction.DraggingSlot >= 0;

        int dragSourceSlot =
            dragging
                ? inventoryInteraction.DraggingSlot
                : -1;

        int dragTargetSlot =
            dragging
                ? inventoryInteraction.HoveredSlot
                : -1;

        for (int i = 0; i < SlotCount; i++)
        {
            GameObject frame = fullSelectionFrames[i];
            if (frame == null)
                continue;

            bool picked = rewardEdit && inventoryInteraction != null &&
                          inventoryInteraction.PadPickedSlot == i;

            bool padSelected = rewardEdit && inventoryInteraction != null &&
                               inventoryInteraction.PadModeActive &&
                               inventoryInteraction.PadSelectedSlot == i;

            bool mouseSelected = rewardEdit && inventoryInteraction != null &&
                                 !inventoryInteraction.PadModeActive &&
                                 inventoryInteraction.SelectedRewardSlot == i;

            bool hovered = rewardEdit && inventoryInteraction != null &&
                           !inventoryInteraction.PadModeActive &&
                           inventoryInteraction.HoveredSlot == i;

            bool previewSelected =
                inspectContext &&
                !selectionSuppressed &&
                i == activeInspectSlot &&
                HasItem(i);

            bool dragSource =
                dragging &&
                i == dragSourceSlot;

            bool dragTarget =
                dragging &&
                i == dragTargetSlot &&
                i != dragSourceSlot;

            bool targetUnlocked =
                equipmentSystem != null &&
                equipmentSystem.IsSlotUnlocked(i);

            bool targetOccupied =
                HasItem(i);

            bool visible =
                (HasItem(i) &&
                 (picked ||
                  padSelected ||
                  mouseSelected ||
                  previewSelected)) ||
                dragSource ||
                dragTarget;

            if (frame.activeSelf != visible)
                frame.SetActive(visible);

            if (!visible)
                continue;

            Color color =
                dragTarget
                    ? !targetUnlocked
                        ? pickedAccent
                        : targetOccupied
                            ? selectedAccent
                            : hoverAccent
                    : dragSource
                        ? selectedAccent
                        : picked
                            ? pickedAccent
                            : mouseSelected || padSelected
                                ? selectedAccent
                                : hovered
                                    ? hoverAccent
                                    : selectedAccent;

            float thickness =
                dragTarget
                    ? Mathf.Lerp(5.4f, 7.2f, pulse01)
                    : dragSource
                        ? Mathf.Lerp(4.6f, 6.0f, pulse01)
                        : picked
                            ? Mathf.Lerp(5.5f, 7f, pulse01)
                            : mouseSelected || padSelected
                                ? Mathf.Lerp(5.2f, 6.8f, pulse01)
                                : hovered
                                    ? 3f
                                    : Mathf.Lerp(3.8f, 5f, pulse01);

            RectTransform frameRect =
                frame.GetComponent<RectTransform>();

            ApplyStrokeEdges(
                frameRect,
                color,
                thickness);

            ApplySelectionDecor(
                frameRect,
                color,
                mouseSelected || padSelected || picked || dragSource,
                picked,
                pulse01);

            ApplyDragStateDecor(
                frameRect,
                dragSource,
                dragTarget,
                targetUnlocked,
                targetOccupied,
                pulse01);
        }
    }

    private void ApplySelectionDecor(
        RectTransform frame,
        Color color,
        bool lockedSelection,
        bool picked,
        float pulse01)
    {
        if (frame == null)
            return;

        RectTransform fill =
            frame.Find("SelectionFill") as RectTransform;

        if (fill == null)
        {
            fill =
                CreateRect(
                    frame,
                    "SelectionFill",
                    Vector2.zero);

            Stretch(fill);
            fill.SetAsFirstSibling();

            Image fillImage =
                fill.gameObject.AddComponent<Image>();

            fillImage.raycastTarget =
                false;
        }

        Image fillGraphic =
            fill.GetComponent<Image>();

        if (fillGraphic != null)
        {
            float alpha =
                lockedSelection
                    ? Mathf.Lerp(
                        selectedFillAlpha * 0.72f,
                        selectedFillAlpha,
                        pulse01)
                    : 0.035f;

            if (picked)
                alpha = Mathf.Max(alpha, 0.16f);

            fillGraphic.color =
                new Color(
                    color.r,
                    color.g,
                    color.b,
                    alpha);
        }

        RectTransform selectedBar =
            frame.Find("SelectedStateBar") as RectTransform;

        if (selectedBar == null)
        {
            selectedBar =
                CreateRect(
                    frame,
                    "SelectedStateBar",
                    Vector2.zero);

            selectedBar.anchorMin =
                new Vector2(
                    0f,
                    0f);

            selectedBar.anchorMax =
                new Vector2(
                    1f,
                    0f);

            selectedBar.pivot =
                new Vector2(
                    0.5f,
                    0f);

            selectedBar.sizeDelta =
                new Vector2(
                    0f,
                    7f);

            selectedBar.anchoredPosition =
                Vector2.zero;

            Image barImage =
                selectedBar.gameObject.AddComponent<Image>();

            barImage.raycastTarget =
                false;
        }

        selectedBar.gameObject.SetActive(
            lockedSelection);

        Image selectedBarImage =
            selectedBar.GetComponent<Image>();

        if (selectedBarImage != null)
        {
            selectedBarImage.color =
                new Color(
                    color.r,
                    color.g,
                    color.b,
                    lockedSelection
                        ? Mathf.Lerp(
                            0.76f,
                            1f,
                            pulse01)
                        : 0f);
        }
    }

    private void ApplyDragStateDecor(
        RectTransform frame,
        bool dragSource,
        bool dragTarget,
        bool targetUnlocked,
        bool targetOccupied,
        float pulse01)
    {
        if (frame == null)
            return;

        RectTransform layer = frame.Find("DragStateFill") as RectTransform;
        if (layer == null)
        {
            layer = CreateRect(frame, "DragStateFill", Vector2.zero);
            Stretch(layer);
            layer.SetAsFirstSibling();
            Image image = layer.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
        }

        layer.gameObject.SetActive(dragSource || dragTarget);

        Image graphic = layer.GetComponent<Image>();
        if (graphic == null)
            return;

        if (dragSource)
        {
            graphic.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.30f, 0.42f, pulse01));
            return;
        }

        Color color = !targetUnlocked
            ? pickedAccent
            : targetOccupied
                ? selectedAccent
                : hoverAccent;

        graphic.color = new Color(
            color.r,
            color.g,
            color.b,
            dragTarget ? Mathf.Lerp(0.07f, 0.14f, pulse01) : 0f);
    }

    private static void DisableLegacyOutlineFrame(RectTransform frame)
    {
        if (frame == null)
            return;

        Image image = frame.GetComponent<Image>();
        if (image != null)
            image.enabled = false;

        Outline outline = frame.GetComponent<Outline>();
        if (outline != null)
            outline.enabled = false;
    }

    private static void EnsureStrokeEdges(RectTransform frame)
    {
        if (frame == null)
            return;

        ConfigureStrokeEdge(frame, "Top", Color.white, 4f,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -4f), Vector2.zero);
        ConfigureStrokeEdge(frame, "Bottom", Color.white, 4f,
            Vector2.zero, new Vector2(1f, 0f),
            Vector2.zero, new Vector2(0f, 4f));
        ConfigureStrokeEdge(frame, "Left", Color.white, 4f,
            Vector2.zero, new Vector2(0f, 1f),
            Vector2.zero, new Vector2(4f, 0f));
        ConfigureStrokeEdge(frame, "Right", Color.white, 4f,
            new Vector2(1f, 0f), Vector2.one,
            new Vector2(-4f, 0f), Vector2.zero);
    }

    private static void ApplyStrokeEdges(RectTransform frame, Color color, float thickness)
    {
        if (frame == null)
            return;

        thickness = Mathf.Max(1f, thickness);
        ConfigureStrokeEdge(frame, "Top", color, thickness,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -thickness), Vector2.zero);
        ConfigureStrokeEdge(frame, "Bottom", color, thickness,
            Vector2.zero, new Vector2(1f, 0f),
            Vector2.zero, new Vector2(0f, thickness));
        ConfigureStrokeEdge(frame, "Left", color, thickness,
            Vector2.zero, new Vector2(0f, 1f),
            Vector2.zero, new Vector2(thickness, 0f));
        ConfigureStrokeEdge(frame, "Right", color, thickness,
            new Vector2(1f, 0f), Vector2.one,
            new Vector2(-thickness, 0f), Vector2.zero);
    }

    private static void ConfigureStrokeEdge(
        RectTransform root,
        string edgeName,
        Color color,
        float thickness,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        RectTransform edge = root.Find(edgeName) as RectTransform;
        if (edge == null)
        {
            GameObject edgeObject = new(edgeName);
            edgeObject.transform.SetParent(root, false);
            edge = edgeObject.AddComponent<RectTransform>();
            Image edgeImage = edgeObject.AddComponent<Image>();
            edgeImage.raycastTarget = false;
        }

        edge.anchorMin = anchorMin;
        edge.anchorMax = anchorMax;
        edge.offsetMin = offsetMin;
        edge.offsetMax = offsetMax;
        edge.localScale = Vector3.one;
        edge.localRotation = Quaternion.identity;

        Image image = edge.GetComponent<Image>();
        if (image != null)
        {
            image.enabled = true;
            image.color = color;
            image.raycastTarget = false;
        }
    }

    private void ApplyRewardInspectTooltip(bool rewardEdit)
    {
        if (!rewardEdit)
        {
            // Reward Choosing의 월드 상품 Hover Preview는
            // BattleRewardCardActionController가 외부 Detail Panel을 소유합니다.
            // 이 경우에는 여기서 매 프레임 숨기지 않습니다.
            bool rewardChoicePreview =
                IsRewardChoice() &&
                detailController != null &&
                detailController.IsRewardPreviewActive;

            if (rewardChoicePreview)
                return;

            // Combat TAB tooltip은 BattleKineticLoadoutUI가 별도로 소유합니다.
            if (detailGroup != null)
                detailGroup.alpha = 0f;

            detailController?.Hide();
            return;
        }

        if (selectionSuppressed ||
            activeInspectSlot < 0 ||
            !HasItem(activeInspectSlot))
        {
            kineticLoadout?.HideRewardInspectTooltip();
            if (detailGroup != null)
                detailGroup.alpha = 0f;
            return;
        }

        if (detailGroup != null)
            detailGroup.alpha = 0f;
        detailController?.Hide();

        // Drag 중에도 다른 "점유 슬롯" 위에 올라가면 SWAP 비교를 즉시 보여줍니다.
        // Source는 항상 DraggingSlot으로 고정되므로, 이전처럼 Target 정보가
        // Source 상세를 덮어써서 교차되는 문제 없이 양쪽 카드가 명시적으로 분리됩니다.
        int compareSource =
            ResolveRewardCompareSourceSlot();

        int compareTarget =
            Input.mousePresent && kineticLoadout != null
                ? kineticLoadout.ResolveOccupiedSlotUnderPointer(
                    Input.mousePosition)
                : inventoryInteraction != null
                    ? inventoryInteraction.HoveredSlot
                    : -1;

        if (compareSource >= 0 &&
            compareTarget >= 0 &&
            compareSource != compareTarget &&
            HasItem(compareSource) &&
            HasItem(compareTarget))
        {
            kineticLoadout?.ShowRewardCompareTooltip(
                compareSource,
                compareTarget);

            return;
        }

        // 빈 슬롯 위에서는 SWAP이 아니므로 드래그 Source 아이템의 상세만 유지합니다.
        kineticLoadout?.ShowRewardInspectTooltip(
            activeInspectSlot);
    }

    private int ResolveRewardCompareSourceSlot()
    {
        if (inventoryInteraction == null)
            return -1;

        if (inventoryInteraction.DraggingSlot >= 0)
            return inventoryInteraction.DraggingSlot;

        if (inventoryInteraction.PadModeActive &&
            inventoryInteraction.PadPickedSlot >= 0)
        {
            return inventoryInteraction.PadPickedSlot;
        }

        return -1;
    }

    private void EnsureDismissCanvas()
    {
        if (dismissCanvas != null)
            return;

        GameObject canvasObject = new("BattleInventoryDismissCanvas");
        canvasObject.transform.SetParent(transform, false);
        dismissCanvas = canvasObject.AddComponent<Canvas>();
        dismissCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        dismissCanvas.overrideSorting = true;
        dismissCanvas.sortingOrder = DismissCanvasSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        // 이 Canvas는 CANCEL SELECTION 버튼의 독립 Sorting 용도입니다.
        // 바깥 클릭은 별도 Screen Rect 판정으로 처리합니다.
        BuildSelectionCancelButton(canvasObject.transform);
    }

    private void BuildSelectionCancelButton(Transform parent)
    {
        selectionCancelRoot =
            CreateRect(
                parent,
                "InventorySelectionCancel",
                selectionCancelSize);

        selectionCancelRoot.anchorMin =
            selectionCancelRoot.anchorMax =
                new Vector2(
                    0f,
                    0f);

        selectionCancelRoot.pivot =
            new Vector2(
                0.5f,
                1f);

        Canvas buttonCanvas =
            selectionCancelRoot.gameObject.AddComponent<Canvas>();

        buttonCanvas.overrideSorting =
            true;

        buttonCanvas.sortingOrder =
            BattleUiSortingContract.RewardSelectionCancel;

        selectionCancelRoot.gameObject.AddComponent<GraphicRaycaster>();

        Image back =
            selectionCancelRoot.gameObject.AddComponent<Image>();

        back.color =
            new Color(
                0.035f,
                0.030f,
                0.055f,
                0.97f);

        back.raycastTarget =
            true;

        Outline outline =
            selectionCancelRoot.gameObject.AddComponent<Outline>();

        outline.effectColor =
            selectedAccent;

        outline.effectDistance =
            new Vector2(
                3f,
                -3f);

        selectionCancelButton =
            selectionCancelRoot.gameObject.AddComponent<Button>();

        selectionCancelButton.transition =
            Selectable.Transition.None;

        selectionCancelButton.targetGraphic =
            back;

        selectionCancelButton.onClick.AddListener(
            CancelSelection);

        RectTransform labelRect =
            CreateRect(
                selectionCancelRoot,
                "Label",
                Vector2.zero);

        Stretch(
            labelRect);

        selectionCancelLabel =
            labelRect.gameObject.AddComponent<Text>();

        selectionCancelLabel.font =
            Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");

        selectionCancelLabel.text =
            "CANCEL SELECTION";

        selectionCancelLabel.fontSize =
            12;

        selectionCancelLabel.fontStyle =
            FontStyle.Bold;

        selectionCancelLabel.alignment =
            TextAnchor.MiddleCenter;

        selectionCancelLabel.color =
            selectedAccent;

        selectionCancelLabel.raycastTarget =
            false;

        selectionCancelGroup =
            selectionCancelRoot.gameObject.AddComponent<CanvasGroup>();

        selectionCancelGroup.alpha =
            0f;

        selectionCancelGroup.blocksRaycasts =
            false;

        selectionCancelGroup.interactable =
            false;

        selectionCancelRoot.gameObject.SetActive(
            true);
    }

    private void UpdateSelectionCancelButton(bool rewardEdit)
    {
        if (selectionCancelRoot == null ||
            selectionCancelGroup == null)
        {
            return;
        }

        bool mouseSelection =
            rewardEdit &&
            inventoryInteraction != null &&
            !inventoryInteraction.PadModeActive &&
            inventoryInteraction.DraggingSlot < 0 &&
            inventoryInteraction.SelectedRewardSlot >= 0 &&
            HasItem(
                inventoryInteraction.SelectedRewardSlot);

        selectionCancelGroup.alpha =
            mouseSelection
                ? 1f
                : 0f;

        selectionCancelGroup.blocksRaycasts =
            mouseSelection;

        selectionCancelGroup.interactable =
            mouseSelection;

        if (!mouseSelection ||
            builtInDetailRoot == null)
        {
            return;
        }

        Vector3[] corners =
            new Vector3[4];

        builtInDetailRoot.GetWorldCorners(
            corners);

        Vector2 bottomCenterScreen =
            new Vector2(
                (corners[0].x + corners[3].x) * 0.5f,
                Mathf.Min(
                    corners[0].y,
                    corners[3].y));

        bottomCenterScreen.y -=
            Mathf.Max(
                0f,
                selectionCancelGap);

        RectTransform canvasRect =
            dismissCanvas != null
                ? dismissCanvas.transform as RectTransform
                : null;

        if (canvasRect != null &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                bottomCenterScreen,
                null,
                out Vector2 localPoint))
        {
            selectionCancelRoot.anchoredPosition =
                localPoint;
        }

        selectionCancelRoot.SetAsLastSibling();
    }

    private void MaintainRewardBulletTime()
    {
        ResolveReferences();
        if (timeScaleController == null)
            return;

        float scale = Mathf.Clamp(rewardInventoryTimeScale, 0.02f, 0.20f);
        timeScaleController.Request(BattleTimeScaleController.Owner.RewardInventory, scale);
    }

    private void RestoreBulletTime(bool force)
    {
        timeScaleController?.Release(BattleTimeScaleController.Owner.RewardInventory);
    }

    private bool HasItem(int slotIndex)
    {
        return equipmentSystem != null && slotIndex >= 0 && slotIndex < equipmentSystem.Slots.Count &&
               equipmentSystem.IsSlotUnlocked(slotIndex) && equipmentSystem.Slots[slotIndex] != null &&
               equipmentSystem.Slots[slotIndex].equipment != null;
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }
        return null;
    }

    private static RectTransform FindChildRect(Transform parent, string objectName)
    {
        if (parent == null)
            return null;

        RectTransform[] all = parent.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].name == objectName)
                return all[i];
        return null;
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

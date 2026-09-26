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
    private CanvasGroup dismissGroup;
    private RectTransform dismissRoot;

    private bool selectionSuppressed;
    private int suppressedSourceSlot = -1;
    private int activeInspectSlot = -1;
    private bool mouseWasInsideBoard;
    private bool inspectContextWasActive;
    private bool combatTabOpen;
    private bool combatActive;
    private BattleKineticLoadoutUI subscribedCombatLoadout;
    private BattleRunManager subscribedRunManager;
    private float nextResolveTime;

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
        mouseWasInsideBoard = false;
        SetActiveInspectSlot(-1);
        RestoreBulletTime(true);
        SetDismissActive(false);
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
        TrackMouseLeavingBoard(inspectContext);
        SetDismissActive(inspectContext);
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
        // 이전 세션에서 빈 공간 클릭/보드 이탈로 걸린 suppression을 다음 세션까지
        // 유지하면 KineticLoadout이 같은 SelectedIndex를 복원해도 상세가 영구히 막힙니다.
        selectionSuppressed = false;
        suppressedSourceSlot = -1;
        mouseWasInsideBoard = false;

        if (!inspectContext)
            SetActiveInspectSlot(-1);
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
            else if (hover >= 0)
            {
                // Mouse/Keyboard에서는 현재 마우스 아래 슬롯이 가장 우선입니다.
                // EMPTY/LOCKED 위에 있으면 상세 선택도 즉시 비웁니다.
                source = HasItem(hover) ? hover : -1;
            }
            else if (HasItem(mouse))
            {
                // 클릭한 아이템은 Hover가 없을 때만 고정 선택으로 남습니다.
                source = mouse;
            }
            else if (!selectionSuppressed && rewardFlow != null && rewardFlow.ChosenRewardCommitted &&
                     HasItem(rewardFlow.ChosenRewardSlot))
            {
                source = rewardFlow.ChosenRewardSlot;
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

    private void TrackMouseLeavingBoard(bool inspectContext)
    {
        if (!inspectContext || boardRoot == null || !Input.mousePresent)
        {
            mouseWasInsideBoard = false;
            return;
        }

        if (inventoryInteraction != null && inventoryInteraction.PadModeActive)
        {
            mouseWasInsideBoard = false;
            return;
        }

        bool inside = RectTransformUtility.RectangleContainsScreenPoint(boardRoot, Input.mousePosition, null);
        if (inside)
        {
            mouseWasInsideBoard = true;
            return;
        }

        if (mouseWasInsideBoard)
        {
            mouseWasInsideBoard = false;
            CancelSelection();
        }
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

            // 이전 Interaction controller가 만들던 Full Grid selection frame은 더 이상 렌더하지 않습니다.
            // Full Grid 선택 표현은 이 클래스의 UnifiedSelectionFrame 하나만 소유합니다.
            Transform legacyInteractionFrame = slot.Find("InteractionFullSelectionFrame");
            if (legacyInteractionFrame != null && legacyInteractionFrame.gameObject.activeSelf)
                legacyInteractionFrame.gameObject.SetActive(false);

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

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slot = fullSlotRects[i];
            if (slot != null)
            {
                Transform legacyInteractionFrame = slot.Find("InteractionFullSelectionFrame");
                if (legacyInteractionFrame != null && legacyInteractionFrame.gameObject.activeSelf)
                    legacyInteractionFrame.gameObject.SetActive(false);
            }

            GameObject frame = fullSelectionFrames[i];
            if (frame == null)
                continue;

            bool selected = inspectContext && !selectionSuppressed && i == activeInspectSlot && HasItem(i);
            if (frame.activeSelf != selected)
                frame.SetActive(selected);
            if (!selected)
                continue;

            bool picked = rewardEdit && inventoryInteraction != null && inventoryInteraction.PadPickedSlot == i;
            bool mouseSelected = rewardEdit && inventoryInteraction != null &&
                                 !inventoryInteraction.PadModeActive && inventoryInteraction.SelectedRewardSlot == i;
            bool hovered = rewardEdit && inventoryInteraction != null &&
                           !inventoryInteraction.PadModeActive && inventoryInteraction.HoveredSlot == i;

            Color color = picked
                ? pickedAccent
                : mouseSelected
                    ? selectedAccent
                    : hovered
                        ? hoverAccent
                        : selectedAccent;

            float thickness = picked
                ? Mathf.Lerp(5.5f, 7f, pulse01)
                : mouseSelected
                    ? Mathf.Lerp(4f, 5.4f, pulse01)
                    : hovered
                        ? 3f
                        : Mathf.Lerp(3.8f, 5f, pulse01);

            ApplyStrokeEdges(frame.GetComponent<RectTransform>(), color, thickness);
        }
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

        int compareSource = ResolveRewardCompareSourceSlot();
        int compareTarget =
            Input.mousePresent && kineticLoadout != null
                ? kineticLoadout.ResolveOccupiedSlotUnderPointer(Input.mousePosition)
                : inventoryInteraction != null
                    ? inventoryInteraction.HoveredSlot
                    : -1;

        if (compareSource >= 0 &&
            compareTarget >= 0 &&
            compareSource != compareTarget &&
            HasItem(compareSource) &&
            HasItem(compareTarget))
        {
            kineticLoadout?.ShowRewardCompareTooltip(compareSource, compareTarget);
            return;
        }

        kineticLoadout?.ShowRewardInspectTooltip(activeInspectSlot);
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

        return inventoryInteraction.SelectedRewardSlot;
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
        canvasObject.AddComponent<GraphicRaycaster>();

        dismissRoot = CreateRect(canvasObject.transform, "InventoryDismissBackground", Vector2.zero);
        Stretch(dismissRoot);
        Image image = dismissRoot.gameObject.AddComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = true;

        BattleUnifiedInventoryDismissRelay relay = dismissRoot.gameObject.AddComponent<BattleUnifiedInventoryDismissRelay>();
        relay.Configure(this);

        dismissGroup = dismissRoot.gameObject.AddComponent<CanvasGroup>();
        dismissGroup.alpha = 0f;
        dismissGroup.blocksRaycasts = false;
        dismissGroup.interactable = false;
    }

    private void SetDismissActive(bool active)
    {
        if (dismissGroup == null)
            return;

        bool interactable = active && !BattlePauseController.IsPaused;
        dismissGroup.alpha = 0f;
        dismissGroup.blocksRaycasts = interactable;
        dismissGroup.interactable = interactable;
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

internal sealed class BattleUnifiedInventoryDismissRelay : MonoBehaviour, IPointerClickHandler
{
    private BattleUnifiedInventoryInspectController owner;

    public void Configure(BattleUnifiedInventoryInspectController controller)
    {
        owner = controller;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            owner?.CancelSelection();
    }
}
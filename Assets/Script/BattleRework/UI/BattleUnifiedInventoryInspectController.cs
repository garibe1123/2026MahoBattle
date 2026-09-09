using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Combat Tab 인벤토리와 Reward PACK 편집을 같은 LoadoutSwitchFull UI로 통합합니다.
///
/// 핵심 규칙:
/// - Reward 아이템을 PACK에 넣고 Selection Locked가 되면 기존 Combat Tab의 LoadoutSwitchFull을 그대로 사용합니다.
/// - Reward용 별도 Full Inspect 레이아웃은 비활성화하여 RectTransform 소유권 충돌을 막습니다.
/// - 선택 슬롯이 오른쪽 열이면 상세 패널은 왼쪽, 그 외에는 오른쪽에 배치합니다.
/// - Grid 밖으로 마우스를 빼거나 빈 배경을 클릭하거나 패드 B를 누르면 선택/상세 패널이 해제됩니다.
/// - Reward 편집에서는 Time.timeScale 0.05를 사용해 실제 필드 오브젝트도 함께 Bullet Time 처리합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33380)]
public sealed class BattleUnifiedInventoryInspectController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int DismissCanvasSortingOrder = 779;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleInventoryInteractionController inventoryInteraction;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;

    [Header("Unified Full Grid")]
    [SerializeField] private Vector2 boardAnchor = new(0.50f, 0.53f);
    [SerializeField, Range(0.82f, 1.10f)] private float fullGridScale = 0.96f;
    [SerializeField] private Vector2 rightDetailOffset = new(-42f, 0f);
    [SerializeField] private Vector2 leftDetailOffset = new(42f, 0f);
    [SerializeField, Range(0.78f, 1.08f)] private float detailScale = 0.94f;

    [Header("Reward Edit")]
    [SerializeField, Range(0.02f, 0.20f)] private float rewardBulletTimeScale = 0.05f;
    [SerializeField] private Vector2 rewardTrashPosition = new(1330f, 92f);
    [SerializeField] private Vector2 rewardDonePosition = new(1124f, 92f);
    [SerializeField] private Vector2 rewardStatusPosition = new(560f, 100f);

    [Header("Theme")]
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Color accentCyan = new(0.14f, 0.92f, 0.94f, 1f);

    private RectTransform fullRoot;
    private CanvasGroup fullGroup;
    private RectTransform boardRoot;
    private RectTransform builtInDetailRoot;
    private RectTransform miniPackRoot;
    private CanvasGroup miniPackGroup;
    private RectTransform detailRoot;
    private CanvasGroup detailGroup;
    private Outline detailOutline;
    private RectTransform trashRoot;
    private RectTransform doneRoot;
    private RectTransform statusRoot;

    private Text fullTitle;
    private Text fullSubtitle;

    private Canvas dismissCanvas;
    private CanvasGroup dismissGroup;
    private RectTransform dismissRoot;

    private BattleRewardFullInspectController oldRewardFullInspect;
    private BattleEquipmentDetailContextLayoutController oldContextLayout;

    private FieldInfo inventoryStagedSlotField;
    private FieldInfo inventorySelectedSlotField;
    private FieldInfo inventoryHoveredSlotField;
    private FieldInfo inventoryPadSelectedField;
    private FieldInfo inventoryPadPickedField;
    private FieldInfo inventoryPadModeField;

    private FieldInfo loadoutSelectedIndexField;
    private FieldInfo loadoutSwitchHeldField;
    private FieldInfo loadoutBoardShownField;
    private MethodInfo loadoutRefreshMethod;

    private bool rewardReuseActive;
    private bool selectionSuppressed;
    private int suppressedSourceSlot = -1;
    private int activeInspectSlot = -1;
    private int lastVisualSlot = -999;
    private bool mouseWasInsideBoard;
    private float nextResolveTime;

    private bool bulletTimeOwned;
    private float previousTimeScale = 1f;
    private float previousFixedDeltaTime = 0.02f;

    private void Awake()
    {
        ResolveReferences();
        CacheReflection();
        EnsureDismissCanvas();
        ResolveUi();
        DisableSupersededControllers();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheReflection();
        EnsureDismissCanvas();
        ResolveUi();
        DisableSupersededControllers();
        nextResolveTime = 0f;
    }

    private void OnDisable()
    {
        RestoreBulletTime(true);
        SetDismissActive(false);
        RestoreLegacyControllers();
    }

    private void Update()
    {
        ResolveReferences();
        CacheReflection();
        EnsureDismissCanvas();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveUi();
            InstallSlotClickRelays();
            DisableSupersededControllers();
        }

        bool rewardEdit = IsRewardEdit();
        bool combatTab = IsCombatTabOpen();
        bool inspectContext = rewardEdit || combatTab;

        if (rewardEdit)
        {
            rewardReuseActive = true;
            MaintainRewardBulletTime();
        }
        else
        {
            rewardReuseActive = false;
            if (bulletTimeOwned && !BattlePauseController.IsPaused)
                RestoreBulletTime(false);
        }

        SyncSelection(rewardEdit, combatTab);
        HandleCancelInput(inspectContext);
        TrackMouseLeavingBoard(inspectContext);
        SetDismissActive(inspectContext);
    }

    private void LateUpdate()
    {
        ResolveUi();

        bool rewardEdit = IsRewardEdit();
        bool combatTab = IsCombatTabOpen();
        bool inspectContext = rewardEdit || combatTab;

        if (rewardEdit)
            ForceRewardToReuseTabUi();
        else if (!combatTab)
            RestoreNonInspectLayout();

        if (inspectContext)
        {
            ForceSharedBoardLayout();
            ApplySelectionVisual(rewardEdit);
            PositionDetailPanel();
        }
        else
        {
            HideDetailIfSuppressed();
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>();
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>();
        if (detailController == null)
            detailController = FindFirstObjectByType<BattleEquipmentDetailPanelController>();

        if (oldRewardFullInspect == null)
            oldRewardFullInspect = FindFirstObjectByType<BattleRewardFullInspectController>(FindObjectsInactive.Include);
        if (oldContextLayout == null)
            oldContextLayout = FindFirstObjectByType<BattleEquipmentDetailContextLayoutController>(FindObjectsInactive.Include);
    }

    private void CacheReflection()
    {
        inventoryStagedSlotField ??= typeof(BattleInventoryInteractionController).GetField("stagedRewardSlot", PrivateInstance);
        inventorySelectedSlotField ??= typeof(BattleInventoryInteractionController).GetField("selectedRewardSlot", PrivateInstance);
        inventoryHoveredSlotField ??= typeof(BattleInventoryInteractionController).GetField("hoveredSlot", PrivateInstance);
        inventoryPadSelectedField ??= typeof(BattleInventoryInteractionController).GetField("padSelectedSlot", PrivateInstance);
        inventoryPadPickedField ??= typeof(BattleInventoryInteractionController).GetField("padPickedSlot", PrivateInstance);
        inventoryPadModeField ??= typeof(BattleInventoryInteractionController).GetField("padModeActive", PrivateInstance);

        loadoutSelectedIndexField ??= typeof(BattleKineticLoadoutUI).GetField("selectedIndex", PrivateInstance);
        loadoutSwitchHeldField ??= typeof(BattleKineticLoadoutUI).GetField("switchHeld", PrivateInstance);
        loadoutBoardShownField ??= typeof(BattleKineticLoadoutUI).GetField("boardWasShown", PrivateInstance);
        loadoutRefreshMethod ??= typeof(BattleKineticLoadoutUI).GetMethod("RefreshAll", PrivateInstance);
    }

    private void DisableSupersededControllers()
    {
        // Reward 편집을 별도 레이아웃으로 만들던 두 컨트롤러는 이제 이 통합 컨트롤러가 대체합니다.
        if (oldRewardFullInspect != null && oldRewardFullInspect.enabled)
            oldRewardFullInspect.enabled = false;
        if (oldContextLayout != null && oldContextLayout.enabled)
            oldContextLayout.enabled = false;
    }

    private void RestoreLegacyControllers()
    {
        // 이 컴포넌트가 제거/비활성화될 경우에만 기존 안전망을 되살립니다.
        if (oldRewardFullInspect != null)
            oldRewardFullInspect.enabled = true;
        if (oldContextLayout != null)
            oldContextLayout.enabled = true;
    }

    private bool IsRewardEdit()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward &&
               inventoryInteraction != null && inventoryInteraction.IsRewardPackEditing;
    }

    private bool IsCombatTabOpen()
    {
        if (runManager == null || !runManager.RunActive || runManager.State != BattleRunState.Combat)
            return false;

        bool held = ReadBool(loadoutSwitchHeldField, kineticLoadout);
        bool shown = ReadBool(loadoutBoardShownField, kineticLoadout);
        if (held && shown)
            return true;

        return fullGroup != null && fullGroup.alpha > 0.08f;
    }

    private void ResolveUi()
    {
        if (fullRoot == null)
        {
            fullRoot = FindRect("LoadoutSwitchFull");
            if (fullRoot != null)
            {
                fullGroup = fullRoot.GetComponent<CanvasGroup>();
                boardRoot = FindChildRect(fullRoot, "GridBoard");
                builtInDetailRoot = FindChildRect(fullRoot, "DetailPanel");
                ResolveFullHeaderTexts();
            }
        }

        if (miniPackRoot == null)
        {
            miniPackRoot = FindRect("BackpackMiniGrid");
            if (miniPackRoot != null)
                miniPackGroup = miniPackRoot.GetComponent<CanvasGroup>();
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

        if (trashRoot == null)
            trashRoot = FindRect("InventoryTrash");
        if (doneRoot == null)
            doneRoot = FindRect("RewardPackDone");

        if (statusRoot == null)
        {
            Text[] texts = UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < texts.Length; i++)
            {
                Text text = texts[i];
                if (text != null && text.text != null && text.text.Contains("ITEM IN PACK"))
                {
                    statusRoot = text.rectTransform;
                    break;
                }
            }
        }
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

            if (text.text == "LOADOUT // SHIFT" || text.text == "PACK // EDIT")
                fullTitle = text;
            else if (text.text != null && (text.text.Contains("HOLD TAB / LB") || text.text.Contains("MOVE / SWAP")))
                fullSubtitle = text;
        }
    }

    private void ForceRewardToReuseTabUi()
    {
        if (fullRoot == null || fullGroup == null)
            return;

        fullRoot.gameObject.SetActive(true);
        fullGroup.alpha = 1f;
        fullGroup.blocksRaycasts = !BattlePauseController.IsPaused;
        fullGroup.interactable = !BattlePauseController.IsPaused;
        fullRoot.localScale = Vector3.one * fullGridScale;

        if (fullTitle != null)
            fullTitle.text = "PACK // EDIT";
        if (fullSubtitle != null)
            fullSubtitle.text = "MOVE / SWAP  •  B CANCEL  •  TRASH  •  DONE";

        if (miniPackGroup != null)
        {
            miniPackGroup.alpha = 0f;
            miniPackGroup.blocksRaycasts = false;
            miniPackGroup.interactable = false;
        }

        if (builtInDetailRoot != null)
            builtInDetailRoot.gameObject.SetActive(false);

        PositionRewardControls();
    }

    private void ForceSharedBoardLayout()
    {
        if (boardRoot != null)
        {
            boardRoot.anchorMin = boardRoot.anchorMax = boardAnchor;
            boardRoot.anchoredPosition = Vector2.zero;
        }

        if (builtInDetailRoot != null)
            builtInDetailRoot.gameObject.SetActive(false);

        if (miniPackGroup != null && (rewardReuseActive || IsCombatTabOpen()))
        {
            miniPackGroup.alpha = 0f;
            miniPackGroup.blocksRaycasts = false;
            miniPackGroup.interactable = false;
        }
    }

    private void RestoreNonInspectLayout()
    {
        if (boardRoot != null)
        {
            boardRoot.anchorMin = boardRoot.anchorMax = new Vector2(0.72f, 0.53f);
            boardRoot.anchoredPosition = Vector2.zero;
        }

        if (builtInDetailRoot != null)
            builtInDetailRoot.gameObject.SetActive(true);

        if (fullTitle != null)
            fullTitle.text = "LOADOUT // SHIFT";
        if (fullSubtitle != null)
            fullSubtitle.text = "HOLD TAB / LB   •   MOVE   •   RELEASE TO EQUIP";
    }

    private void PositionRewardControls()
    {
        if (trashRoot != null)
        {
            trashRoot.anchorMin = trashRoot.anchorMax = Vector2.zero;
            trashRoot.pivot = Vector2.zero;
            trashRoot.anchoredPosition = rewardTrashPosition;
        }

        if (doneRoot != null)
        {
            doneRoot.anchorMin = doneRoot.anchorMax = Vector2.zero;
            doneRoot.pivot = Vector2.zero;
            doneRoot.anchoredPosition = rewardDonePosition;
        }

        if (statusRoot != null)
        {
            statusRoot.anchorMin = statusRoot.anchorMax = Vector2.zero;
            statusRoot.pivot = Vector2.zero;
            statusRoot.anchoredPosition = rewardStatusPosition;
            statusRoot.sizeDelta = new Vector2(780f, 46f);
        }
    }

    private void SyncSelection(bool rewardEdit, bool combatTab)
    {
        int source = -1;

        if (rewardEdit)
        {
            bool padMode = ReadBool(inventoryPadModeField, inventoryInteraction);
            int pad = ReadInt(inventoryPadSelectedField, inventoryInteraction, -1);
            int mouse = ReadInt(inventorySelectedSlotField, inventoryInteraction, -1);
            int staged = ReadInt(inventoryStagedSlotField, inventoryInteraction, -1);

            if (padMode && HasItem(pad))
                source = pad;
            else if (HasItem(mouse))
                source = mouse;
            else if (activeInspectSlot >= 0 && HasItem(activeInspectSlot))
                source = activeInspectSlot;
            else if (!selectionSuppressed && HasItem(staged))
                source = staged;

            if (source >= 0)
                WriteInt(loadoutSelectedIndexField, kineticLoadout, source);
        }
        else if (combatTab)
        {
            source = ReadInt(loadoutSelectedIndexField, kineticLoadout, -1);
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
                activeInspectSlot = -1;
                return;
            }
        }

        if (source >= 0 && HasItem(source))
        {
            if (activeInspectSlot != source)
            {
                activeInspectSlot = source;
                detailController?.SelectSlotFromPointer(source);
                lastVisualSlot = -999;
            }
        }
        else
        {
            activeInspectSlot = -1;
        }
    }

    public void SelectSlot(int slotIndex)
    {
        if (!HasItem(slotIndex))
        {
            CancelSelection();
            return;
        }

        selectionSuppressed = false;
        suppressedSourceSlot = -1;
        activeInspectSlot = slotIndex;

        WriteInt(loadoutSelectedIndexField, kineticLoadout, slotIndex);
        if (IsRewardEdit())
            WriteInt(inventorySelectedSlotField, inventoryInteraction, slotIndex);

        detailController?.SelectSlotFromPointer(slotIndex);
        InvokeLoadoutRefresh();
        lastVisualSlot = -999;
    }

    public void CancelSelection()
    {
        int current = activeInspectSlot >= 0
            ? activeInspectSlot
            : ReadInt(loadoutSelectedIndexField, kineticLoadout, -1);

        selectionSuppressed = true;
        suppressedSourceSlot = current;
        activeInspectSlot = -1;
        lastVisualSlot = -999;

        WriteInt(inventorySelectedSlotField, inventoryInteraction, -1);
        WriteInt(inventoryHoveredSlotField, inventoryInteraction, -1);
        WriteInt(inventoryPadPickedField, inventoryInteraction, -1);
        WriteBool(inventoryPadModeField, inventoryInteraction, false);

        // Combat Tab에서도 전체 선택 프레임이 즉시 사라지도록 selectedIndex 자체를 비웁니다.
        WriteInt(loadoutSelectedIndexField, kineticLoadout, -1);
        detailController?.SelectSlotFromPointer(-1);
        InvokeLoadoutRefresh();

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

    private void ApplySelectionVisual(bool rewardEdit)
    {
        if (!rewardEdit || kineticLoadout == null)
            return;

        if (lastVisualSlot == activeInspectSlot)
            return;

        InvokeLoadoutRefresh();

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            RectTransform slot = FindRect($"GridSlot_{i}");
            if (slot == null)
                continue;

            if (i == activeInspectSlot && !selectionSuppressed && HasItem(i))
            {
                Image back = slot.GetComponent<Image>();
                if (back != null)
                    back.color = accentYellow;
                slot.localScale = Vector3.one * 1.10f;
            }
            else
            {
                slot.localScale = Vector3.one;
            }
        }

        lastVisualSlot = activeInspectSlot;
    }

    private void PositionDetailPanel()
    {
        if (detailRoot == null || detailGroup == null)
            return;

        bool show = activeInspectSlot >= 0 && !selectionSuppressed && HasItem(activeInspectSlot);
        if (!show)
        {
            detailGroup.alpha = 0f;
            return;
        }

        int column = activeInspectSlot % BattleEquipmentSystem.GridSize;
        bool moveLeft = column == BattleEquipmentSystem.GridSize - 1;

        detailRoot.localScale = Vector3.one * detailScale;
        detailRoot.localRotation = Quaternion.identity;

        if (moveLeft)
        {
            detailRoot.anchorMin = detailRoot.anchorMax = new Vector2(0f, 0.5f);
            detailRoot.pivot = new Vector2(0f, 0.5f);
            detailRoot.anchoredPosition = leftDetailOffset;
        }
        else
        {
            detailRoot.anchorMin = detailRoot.anchorMax = new Vector2(1f, 0.5f);
            detailRoot.pivot = new Vector2(1f, 0.5f);
            detailRoot.anchoredPosition = rightDetailOffset;
        }

        detailGroup.alpha = 1f;
        detailGroup.blocksRaycasts = false;
        detailGroup.interactable = false;

        if (detailOutline != null)
        {
            detailOutline.effectColor = moveLeft ? accentCyan : accentYellow;
            detailOutline.effectDistance = new Vector2(6f, -6f);
        }
    }

    private void HideDetailIfSuppressed()
    {
        if (selectionSuppressed && detailGroup != null)
            detailGroup.alpha = 0f;
    }

    private void InstallSlotClickRelays()
    {
        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            RectTransform slot = FindRect($"GridSlot_{i}");
            if (slot == null)
                continue;

            Image image = slot.GetComponent<Image>();
            if (image != null)
                image.raycastTarget = true;

            BattleUnifiedInventorySlotRelay relay = slot.GetComponent<BattleUnifiedInventorySlotRelay>();
            if (relay == null)
                relay = slot.gameObject.AddComponent<BattleUnifiedInventorySlotRelay>();
            relay.Configure(this, i);
        }
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
        dismissGroup.alpha = 0f;
        dismissGroup.blocksRaycasts = active && !BattlePauseController.IsPaused;
        dismissGroup.interactable = active && !BattlePauseController.IsPaused;
    }

    private void MaintainRewardBulletTime()
    {
        if (BattlePauseController.IsPaused)
            return;

        if (!bulletTimeOwned)
        {
            previousTimeScale = Time.timeScale;
            previousFixedDeltaTime = Time.fixedDeltaTime;
            if (previousTimeScale <= 0f)
                return;
            bulletTimeOwned = true;
        }

        float scale = Mathf.Clamp(rewardBulletTimeScale, 0.02f, 0.20f);
        float target = previousTimeScale * scale;
        if (!Mathf.Approximately(Time.timeScale, target))
        {
            Time.timeScale = target;
            Time.fixedDeltaTime = Mathf.Max(0.0001f, previousFixedDeltaTime * scale);
        }
    }

    private void RestoreBulletTime(bool force)
    {
        if (!bulletTimeOwned)
            return;
        if (!force && BattlePauseController.IsPaused)
            return;

        Time.timeScale = previousTimeScale;
        Time.fixedDeltaTime = previousFixedDeltaTime;
        bulletTimeOwned = false;
    }

    private bool HasItem(int slotIndex)
    {
        return equipmentSystem != null && slotIndex >= 0 && slotIndex < equipmentSystem.Slots.Count &&
               equipmentSystem.IsSlotUnlocked(slotIndex) && equipmentSystem.Slots[slotIndex] != null &&
               equipmentSystem.Slots[slotIndex].equipment != null;
    }

    private void InvokeLoadoutRefresh()
    {
        if (kineticLoadout != null && loadoutRefreshMethod != null)
            loadoutRefreshMethod.Invoke(kineticLoadout, null);
    }

    private static int ReadInt(FieldInfo field, object owner, int fallback)
    {
        if (field == null || owner == null)
            return fallback;
        object value = field.GetValue(owner);
        return value is int result ? result : fallback;
    }

    private static bool ReadBool(FieldInfo field, object owner)
    {
        if (field == null || owner == null)
            return false;
        object value = field.GetValue(owner);
        return value is bool result && result;
    }

    private static void WriteInt(FieldInfo field, object owner, int value)
    {
        if (field != null && owner != null)
            field.SetValue(owner, value);
    }

    private static void WriteBool(FieldInfo field, object owner, bool value)
    {
        if (field != null && owner != null)
            field.SetValue(owner, value);
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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

internal sealed class BattleUnifiedInventorySlotRelay : MonoBehaviour, IPointerClickHandler
{
    private BattleUnifiedInventoryInspectController owner;
    private int slotIndex;

    public void Configure(BattleUnifiedInventoryInspectController controller, int index)
    {
        owner = controller;
        slotIndex = index;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            owner?.SelectSlot(slotIndex);
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

public static class BattleUnifiedInventoryInspectAutoInstaller
{
#if UNITY_EDITOR
    private static bool installQueued;

    [InitializeOnLoadMethod]
    private static void InitializeEditorInstaller()
    {
        EditorApplication.hierarchyChanged -= QueueInstall;
        EditorApplication.hierarchyChanged += QueueInstall;
        QueueInstall();
    }

    private static void QueueInstall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || installQueued)
            return;
        installQueued = true;
        EditorApplication.delayCall += EnsureEditorComponent;
    }

    private static void EnsureEditorComponent()
    {
        installQueued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null || EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded)
                continue;

            if (manager.GetComponent<BattleUnifiedInventoryInspectController>() != null)
                continue;

            Undo.AddComponent<BattleUnifiedInventoryInspectController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponent()
    {
        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleUnifiedInventoryInspectController>() == null)
                manager.gameObject.AddComponent<BattleUnifiedInventoryInspectController>();
        }
    }
}

using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// BattleRewardFlow의 입력/Presentation 어댑터입니다.
///
/// Reward의 business state는 BattleRewardFlow가 단독 소유합니다.
/// 이 컴포넌트는 기존 UI를 유지하는 동안 다음만 담당합니다.
/// - 구형 BattleHUD 카드 선택을 RewardFlow에 전달
/// - Hand의 마우스/패드 입력과 Ghost 표시
/// - 기존 PACK UI와의 임시 호환 bridge
/// - 구형 Drag/Description UI 억제
///
/// BattleInventoryInteractionController의 private field 접근은 Phase 3에서 제거할
/// 임시 compatibility bridge이며 Reward의 authoritative state로 사용하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(44000)]
public sealed class BattleRewardDecisionFlowController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const string ObsoleteDescriptionBarName = "RewardActiveDescriptionBar";

    [Header("Hand")]
    [SerializeField] private Vector2 handMouseOffset = new(72f, -72f);
    [SerializeField] private Vector2 handPadOffset = new(92f, 0f);
    [SerializeField, Range(0.25f, 0.95f)] private float padAxisThreshold = 0.55f;
    [SerializeField, Range(0.05f, 0.8f)] private float padAxisReleaseThreshold = 0.22f;

    [Header("Reward Camera")]
    [SerializeField] private float rewardCameraBiasY = -0.72f;

    private BattleRunManager runManager;
    private BattleEquipmentSystem equipmentSystem;
    private BattleRewardFlow rewardFlow;
    private BattleInventoryInteractionController inventoryInteraction;
    private BattleHUD battleHud;
    private BattleSelectionLayoutPolicyController selectionLayout;
    private BattleKineticLoadoutUI kineticLoadout;

    // Compatibility reflection only. Reward business state lives in BattleRewardFlow.
    private FieldInfo pendingRewardIndexField;
    private FieldInfo rewardStagedField;
    private FieldInfo stagedRewardField;
    private FieldInfo stagedRewardSlotField;
    private FieldInfo selectedRewardSlotField;
    private FieldInfo hoveredSlotField;
    private FieldInfo padSelectedSlotField;
    private FieldInfo padPickedSlotField;
    private FieldInfo padModeField;
    private FieldInfo inventoryLastStateField;
    private FieldInfo rewardEditStatusField;
    private FieldInfo rewardCameraBiasField;
    private MethodInfo loadoutRefreshMethod;

    private RectTransform screenInner;
    private RectTransform prizeChoices;
    private RectTransform handGhost;
    private Image handGhostIcon;
    private RectTransform trashRoot;
    private BattleInventoryTrashDropTarget legacyTrashTarget;
    private RectTransform doneRoot;
    private Button doneButton;
    private Text rewardEditStatus;

    private readonly List<RectTransform> obsoleteChoiceBars = new();

    // Presentation/input-only transient state.
    private bool inventoryDisabledByThis;
    private bool doneBound;
    private bool padAxisLatched;
    private bool handPadMode;
    private int handPadSlot;
    private Vector3 lastMousePosition;
    private bool lastMousePositionValid;
    private float nextResolveTime;
    private bool wasReward;
    private bool initialPackSelectionMirrored;

    private void Awake()
    {
        ResolveReferences();
        CacheCompatibilityReflection();
        ResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheCompatibilityReflection();
        ResolveUi();
        nextResolveTime = 0f;
    }

    private void OnDisable()
    {
        RestoreSuppressedInventory();
        SetLegacyPackHandlersEnabled(true);
        SetRewardCardDragEnabled(!IsReward());
        HideHandGhost();
        HideRewardEditStatus();
    }

    private void Update()
    {
        ResolveReferences();
        CacheCompatibilityReflection();
        rewardFlow?.RefreshFromRunState();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveUi();
        }

        bool reward = IsReward();
        if (!reward)
        {
            if (wasReward)
                ResetPresentationState();
            wasReward = false;
            return;
        }

        wasReward = true;
        ApplyLowerRewardCameraBias();
        HideRewardEditStatus();
        SyncChoiceFromLegacyHud();

        if (rewardFlow == null || rewardFlow.Phase == BattleRewardPhase.Inactive)
            return;

        if (rewardFlow.Phase == BattleRewardPhase.Choosing)
            MaintainChoiceStage();
        else if (rewardFlow.Phase == BattleRewardPhase.PackEditing)
            MaintainPackStage();
    }

    private void LateUpdate()
    {
        if (!IsReward() || rewardFlow == null)
            return;

        ResolveUi();
        HideRewardEditStatus();

        if (rewardFlow.Phase == BattleRewardPhase.Choosing)
        {
            HideObsoleteChoiceUi();
            ApplyChoiceCopyOnly();
            HideLegacyPackControlsDuringChoice();
            return;
        }

        if (rewardFlow.Phase == BattleRewardPhase.PackEditing)
        {
            ApplyDoneState();
            ApplyHandStateVisuals();
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (rewardFlow == null)
        {
            rewardFlow = GetComponent<BattleRewardFlow>();
            if (rewardFlow == null)
                rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
            if (rewardFlow == null && Application.isPlaying)
                rewardFlow = gameObject.AddComponent<BattleRewardFlow>();
        }
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        if (battleHud == null)
            battleHud = FindFirstObjectByType<BattleHUD>(FindObjectsInactive.Include);
        if (selectionLayout == null)
            selectionLayout = FindFirstObjectByType<BattleSelectionLayoutPolicyController>(FindObjectsInactive.Include);
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
    }

    private void CacheCompatibilityReflection()
    {
        if (battleHud != null && pendingRewardIndexField == null)
            pendingRewardIndexField = typeof(BattleHUD).GetField("pendingRewardIndex", PrivateInstance);

        if (inventoryInteraction != null)
        {
            System.Type interactionType = typeof(BattleInventoryInteractionController);
            rewardStagedField ??= interactionType.GetField("rewardStaged", PrivateInstance);
            stagedRewardField ??= interactionType.GetField("stagedReward", PrivateInstance);
            stagedRewardSlotField ??= interactionType.GetField("stagedRewardSlot", PrivateInstance);
            selectedRewardSlotField ??= interactionType.GetField("selectedRewardSlot", PrivateInstance);
            hoveredSlotField ??= interactionType.GetField("hoveredSlot", PrivateInstance);
            padSelectedSlotField ??= interactionType.GetField("padSelectedSlot", PrivateInstance);
            padPickedSlotField ??= interactionType.GetField("padPickedSlot", PrivateInstance);
            padModeField ??= interactionType.GetField("padModeActive", PrivateInstance);
            inventoryLastStateField ??= interactionType.GetField("lastState", PrivateInstance);
            rewardEditStatusField ??= interactionType.GetField("rewardEditStatus", PrivateInstance);
        }

        if (selectionLayout != null && rewardCameraBiasField == null)
            rewardCameraBiasField = typeof(BattleSelectionLayoutPolicyController).GetField("rewardCameraBiasWorld", PrivateInstance);

        if (kineticLoadout != null && loadoutRefreshMethod == null)
            loadoutRefreshMethod = typeof(BattleKineticLoadoutUI).GetMethod("RefreshAll", PrivateInstance);
    }

    private void ResolveUi()
    {
        RectTransform rewardScreen = FindRect("PrizeSelectionScreen");
        screenInner = rewardScreen != null
            ? rewardScreen.Find("ScreenInner") as RectTransform
            : FindRect("ScreenInner");

        prizeChoices = screenInner != null
            ? screenInner.Find("PrizeChoices") as RectTransform
            : FindRect("PrizeChoices");

        if (handGhost == null)
        {
            handGhost = FindRect("InventoryDragGhost");
            handGhostIcon = handGhost != null ? handGhost.Find("Icon")?.GetComponent<Image>() : null;
        }

        RectTransform nextTrash = FindRect("InventoryTrash");
        if (nextTrash != trashRoot)
        {
            trashRoot = nextTrash;
            legacyTrashTarget = trashRoot != null ? trashRoot.GetComponent<BattleInventoryTrashDropTarget>() : null;
            if (trashRoot != null)
            {
                BattleRewardHandTrashRelay relay = trashRoot.GetComponent<BattleRewardHandTrashRelay>();
                if (relay == null)
                    relay = trashRoot.gameObject.AddComponent<BattleRewardHandTrashRelay>();
                relay.Configure(this);
            }
        }

        RectTransform nextDone = FindRect("RewardPackDone");
        if (nextDone != doneRoot)
        {
            doneRoot = nextDone;
            doneButton = doneRoot != null ? doneRoot.GetComponent<Button>() : null;
            doneBound = false;
        }

        if (doneButton != null && !doneBound)
        {
            doneButton.onClick.AddListener(HandleDoneButtonPressed);
            doneBound = true;
        }

        if (inventoryInteraction != null && rewardEditStatusField != null)
            rewardEditStatus = rewardEditStatusField.GetValue(inventoryInteraction) as Text;

        ResolveObsoleteChoiceBars();

        for (int i = 0; i < SlotCount; i++)
        {
            RectTransform slot = FindRect($"GridSlot_{i}");
            if (slot == null)
                continue;

            Image image = slot.GetComponent<Image>();
            if (image != null)
                image.raycastTarget = true;

            BattleRewardHandSlotRelay relay = slot.GetComponent<BattleRewardHandSlotRelay>();
            if (relay == null)
                relay = slot.gameObject.AddComponent<BattleRewardHandSlotRelay>();
            relay.Configure(this, i);
        }
    }

    private void SyncChoiceFromLegacyHud()
    {
        if (rewardFlow == null || rewardFlow.Phase != BattleRewardPhase.Choosing)
            return;

        int pending = GetPendingRewardIndex();
        if (pending >= 0)
            rewardFlow.SelectChoice(pending);
        else if (rewardFlow.SelectedChoiceIndex >= 0)
            rewardFlow.ClearChoice();
    }

    private void MaintainChoiceStage()
    {
        initialPackSelectionMirrored = false;
        MirrorChoiceStageToLegacyInventory();
        SuppressInventoryForStaticRewardChoice();
        SetRewardCardDragEnabled(false);
        HideHandGhost();
        HideRewardEditStatus();
        HideObsoleteChoiceUi();

        if (rewardFlow != null && rewardFlow.CanConfirmChoice && !BattlePauseController.IsPaused &&
            (Input.GetKeyDown(KeyCode.Return) ||
             Input.GetKeyDown(KeyCode.Space) ||
             Input.GetKeyDown(KeyCode.JoystickButton0)))
        {
            ConfirmSelectedReward();
        }
    }

    // BattleRewardCardActionController의 기존 연결을 깨지 않기 위한 adapter entry point입니다.
    // 실제 Reward 상태 변경은 BattleRewardFlow에서만 수행합니다.
    private void ConfirmSelectedReward()
    {
        if (!IsReward() || rewardFlow == null)
            return;

        SyncChoiceFromLegacyHud();
        if (!rewardFlow.ConfirmSelectedChoice())
            return;

        SetPendingRewardIndex(-1);
        SetPrizeChoicesInteractable(false);
        SetRewardCardDragEnabled(false);

        handPadMode = false;
        handPadSlot = rewardFlow.ChosenRewardSlot >= 0
            ? rewardFlow.ChosenRewardSlot
            : FindFirstUnlockedSlot();
        padAxisLatched = false;
        initialPackSelectionMirrored = false;

        MirrorFlowToLegacyInventory(!rewardFlow.HasHand && rewardFlow.ChosenRewardCommitted);
        initialPackSelectionMirrored = !rewardFlow.HasHand && rewardFlow.ChosenRewardCommitted;

        if (rewardFlow.HasHand)
        {
            SuppressInventoryForHand();
        }
        else
        {
            RestoreSuppressedInventory();
            SetLegacyPackHandlersEnabled(true);
        }

        HideRewardEditStatus();
        HideObsoleteChoiceUi();
        InvokeLoadoutRefresh();
    }

    private void MaintainPackStage()
    {
        if (rewardFlow == null)
            return;

        SetPrizeChoicesInteractable(false);
        SetRewardCardDragEnabled(false);
        HideRewardEditStatus();

        rewardFlow.SyncChosenRewardLocation();
        bool forceInitialSelection = !initialPackSelectionMirrored &&
                                     !rewardFlow.HasHand &&
                                     rewardFlow.ChosenRewardCommitted;
        MirrorFlowToLegacyInventory(forceInitialSelection);
        if (forceInitialSelection)
            initialPackSelectionMirrored = true;

        if (rewardFlow.HasHand)
        {
            SuppressInventoryForHand();
            HandleHandPadInput();
        }
        else
        {
            RestoreSuppressedInventory();
            SetLegacyPackHandlersEnabled(true);

            if (!rewardFlow.ChosenRewardCommitted && !BattlePauseController.IsPaused &&
                Input.GetKeyDown(KeyCode.JoystickButton7))
                FinishWithoutReward();
        }
    }

    private void HandleHandPadInput()
    {
        if (rewardFlow == null || !rewardFlow.HasHand || equipmentSystem == null || BattlePauseController.IsPaused)
            return;

        if (Input.mousePresent)
        {
            Vector3 mouse = Input.mousePosition;
            if (!lastMousePositionValid)
            {
                lastMousePosition = mouse;
                lastMousePositionValid = true;
            }
            else if ((mouse - lastMousePosition).sqrMagnitude > 4f)
            {
                handPadMode = false;
                lastMousePosition = mouse;
            }
        }

        int dx = 0;
        int dy = 0;
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) dx = -1;
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) dx = 1;
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dy = -1;
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dy = 1;

        float axisX = Input.GetAxisRaw("Horizontal");
        float axisY = Input.GetAxisRaw("Vertical");
        float magnitude = Mathf.Max(Mathf.Abs(axisX), Mathf.Abs(axisY));
        if (!padAxisLatched && magnitude >= padAxisThreshold)
        {
            if (Mathf.Abs(axisX) >= Mathf.Abs(axisY))
                dx = axisX > 0f ? 1 : -1;
            else
                dy = axisY > 0f ? -1 : 1;
            padAxisLatched = true;
            handPadMode = true;
        }
        else if (padAxisLatched && magnitude <= padAxisReleaseThreshold)
        {
            padAxisLatched = false;
        }

        if (dx != 0 || dy != 0)
        {
            MoveHandPadSelection(dx, dy);
            handPadMode = true;
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton0) ||
            Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.Space))
        {
            handPadMode = true;
            HandleHandSlotClick(handPadSlot);
        }

        if (Input.GetKeyDown(KeyCode.JoystickButton3))
        {
            handPadMode = true;
            DiscardHand();
        }
    }

    private void MoveHandPadSelection(int dx, int dy)
    {
        if (equipmentSystem == null)
            return;

        Vector2Int p = BattleEquipmentSystem.SlotIndexToGrid(Mathf.Clamp(handPadSlot, 0, SlotCount - 1));
        int nx = Mathf.Clamp(p.x + dx, 0, BattleEquipmentSystem.GridSize - 1);
        int ny = Mathf.Clamp(p.y + dy, 0, BattleEquipmentSystem.GridSize - 1);
        int next = BattleEquipmentSystem.GridToSlotIndex(nx, ny);
        if (next >= 0 && equipmentSystem.IsSlotUnlocked(next))
            handPadSlot = next;
    }

    internal void HandleHandSlotClick(int slotIndex)
    {
        if (!IsReward() || rewardFlow == null || !rewardFlow.HasHand || equipmentSystem == null ||
            !equipmentSystem.IsSlotUnlocked(slotIndex))
            return;

        if (!rewardFlow.ExchangeHandWithSlot(slotIndex))
            return;

        AfterHandChanged(slotIndex);
    }

    internal void HandleHandTrashClick()
    {
        if (rewardFlow != null && rewardFlow.HasHand)
            DiscardHand();
    }

    private void DiscardHand()
    {
        if (rewardFlow == null || !rewardFlow.DiscardHand())
            return;

        AfterHandChanged(rewardFlow.ChosenRewardSlot);
    }

    private void AfterHandChanged(int focusSlot)
    {
        handPadSlot = focusSlot >= 0 ? focusSlot : FindFirstUnlockedSlot();
        rewardFlow?.SyncChosenRewardLocation();
        MirrorFlowToLegacyInventory(!HasHand && rewardFlow != null && rewardFlow.ChosenRewardCommitted);
        initialPackSelectionMirrored = !HasHand && rewardFlow != null && rewardFlow.ChosenRewardCommitted;
        InvokeLoadoutRefresh();
        HideRewardEditStatus();

        if (HasHand)
        {
            SuppressInventoryForHand();
        }
        else
        {
            HideHandGhost();
            RestoreSuppressedInventory();
            SetLegacyPackHandlersEnabled(true);
        }
    }

    private void MirrorChoiceStageToLegacyInventory()
    {
        if (inventoryInteraction == null)
            return;

        WriteBool(rewardStagedField, inventoryInteraction, false);
        stagedRewardField?.SetValue(inventoryInteraction, null);
        WriteInt(stagedRewardSlotField, inventoryInteraction, -1);
        WriteInt(selectedRewardSlotField, inventoryInteraction, -1);
        WriteInt(hoveredSlotField, inventoryInteraction, -1);
        WriteInt(padPickedSlotField, inventoryInteraction, -1);
        WriteBool(padModeField, inventoryInteraction, false);
    }

    /// <summary>
    /// Phase 3에서 제거할 compatibility mirror입니다.
    /// BattleInventoryInteractionController가 아직 rewardStaged/stagedReward를 자체 필드로 기대하므로
    /// BattleRewardFlow의 상태를 한 방향으로만 복사합니다. 반대 방향으로 읽지는 않습니다.
    /// </summary>
    private void MirrorFlowToLegacyInventory(bool forceRewardSelection)
    {
        if (inventoryInteraction == null || rewardFlow == null)
            return;

        bool packEditing = rewardFlow.Phase == BattleRewardPhase.PackEditing;
        WriteBool(rewardStagedField, inventoryInteraction, packEditing);
        stagedRewardField?.SetValue(
            inventoryInteraction,
            packEditing && !rewardFlow.HasHand && rewardFlow.ChosenRewardCommitted
                ? rewardFlow.ChosenReward
                : null);
        WriteInt(
            stagedRewardSlotField,
            inventoryInteraction,
            packEditing && rewardFlow.ChosenRewardCommitted
                ? rewardFlow.ChosenRewardSlot
                : -1);
        WriteInt(hoveredSlotField, inventoryInteraction, -1);
        WriteInt(padSelectedSlotField, inventoryInteraction, Mathf.Clamp(handPadSlot, 0, SlotCount - 1));
        WriteInt(padPickedSlotField, inventoryInteraction, -1);
        WriteBool(padModeField, inventoryInteraction, false);

        if (rewardFlow.HasHand)
            WriteInt(selectedRewardSlotField, inventoryInteraction, -1);
        else if (forceRewardSelection && rewardFlow.ChosenRewardCommitted)
            WriteInt(selectedRewardSlotField, inventoryInteraction, rewardFlow.ChosenRewardSlot);
    }

    private void SuppressInventoryForStaticRewardChoice()
    {
        if (inventoryInteraction != null && inventoryInteraction.enabled)
        {
            inventoryInteraction.enabled = false;
            inventoryDisabledByThis = true;
        }

        SetLegacyPackHandlersEnabled(false);
        SetPrizeChoicesInteractable(true);
    }

    private void SuppressInventoryForHand()
    {
        if (inventoryInteraction != null && inventoryInteraction.enabled)
        {
            inventoryInteraction.enabled = false;
            inventoryDisabledByThis = true;
        }

        SetLegacyPackHandlersEnabled(false);
        SetPrizeChoicesInteractable(false);
    }

    private void RestoreSuppressedInventory()
    {
        if (inventoryInteraction != null && inventoryDisabledByThis)
        {
            if (inventoryLastStateField != null && runManager != null)
                inventoryLastStateField.SetValue(inventoryInteraction, runManager.State);

            inventoryInteraction.enabled = true;
            inventoryDisabledByThis = false;
        }
    }

    private void ResolveObsoleteChoiceBars()
    {
        obsoleteChoiceBars.Clear();
        RectTransform[] all = UnityEngine.Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == ObsoleteDescriptionBarName)
                obsoleteChoiceBars.Add(rect);
        }
    }

    private void HideObsoleteChoiceUi()
    {
        for (int i = 0; i < obsoleteChoiceBars.Count; i++)
        {
            RectTransform bar = obsoleteChoiceBars[i];
            if (bar == null)
                continue;

            CanvasGroup group = bar.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            Image image = bar.GetComponent<Image>();
            if (image != null)
                image.raycastTarget = false;

            if (bar.gameObject.activeSelf)
                bar.gameObject.SetActive(false);
        }
    }

    private void SetLegacyPackHandlersEnabled(bool enabled)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            ToggleSlotPointer(FindRect($"BackpackCell_{i}"), enabled);
            ToggleSlotPointer(FindRect($"GridSlot_{i}"), enabled);
            ToggleSlotPointer(FindRect($"RewardLoadoutSlot_{i + 1}"), enabled);
        }

        RectTransform miniPack = FindRect("BackpackMiniGrid");
        if (miniPack != null)
        {
            BattleInventoryPackDropTarget drop = miniPack.GetComponent<BattleInventoryPackDropTarget>();
            if (drop != null)
                drop.enabled = enabled;
        }

        if (legacyTrashTarget != null)
            legacyTrashTarget.enabled = enabled;
    }

    private static void ToggleSlotPointer(RectTransform slot, bool enabled)
    {
        if (slot == null)
            return;

        BattleInventorySlotPointer pointer = slot.GetComponent<BattleInventorySlotPointer>();
        if (pointer != null)
            pointer.enabled = enabled;
    }

    private void SetRewardCardDragEnabled(bool enabled)
    {
        if (prizeChoices == null)
            return;

        RewardPrizeDrag[] drags = prizeChoices.GetComponentsInChildren<RewardPrizeDrag>(true);
        for (int i = 0; i < drags.Length; i++)
        {
            RewardPrizeDrag drag = drags[i];
            if (drag != null)
                drag.enabled = enabled;
        }
    }

    private void ApplyChoiceCopyOnly()
    {
        if (screenInner == null)
            return;

        Text[] texts = screenInner.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null)
                continue;

            string value = text.text ?? string.Empty;
            if (value.Contains("SELECT") && value.Contains("DRAG") && value.Contains("DROP"))
                text.text = "SELECT  •  INSPECT  •  DECIDE";
            else if (value == "CLICK / DRAG")
                text.text = "CLICK TO SELECT";
        }
    }

    private void HideLegacyPackControlsDuringChoice()
    {
        if (trashRoot != null && trashRoot.gameObject.activeSelf)
            trashRoot.gameObject.SetActive(false);
        if (doneRoot != null && doneRoot.gameObject.activeSelf)
            doneRoot.gameObject.SetActive(false);
        HideHandGhost();
    }

    private void ApplyDoneState()
    {
        bool ready = rewardFlow != null && rewardFlow.CanComplete;

        if (doneRoot != null)
        {
            if (!doneRoot.gameObject.activeSelf)
                doneRoot.gameObject.SetActive(true);
            if (doneButton != null)
                doneButton.interactable = ready;
        }

        if (trashRoot != null && !trashRoot.gameObject.activeSelf)
            trashRoot.gameObject.SetActive(true);
    }

    private void ApplyHandStateVisuals()
    {
        if (rewardFlow == null || !rewardFlow.HasHand)
        {
            HideHandGhost();
            if (legacyTrashTarget != null)
                legacyTrashTarget.enabled = true;
            return;
        }

        if (legacyTrashTarget != null)
            legacyTrashTarget.enabled = false;

        if (handGhost == null)
        {
            ResolveUi();
            if (handGhost == null)
                return;
        }

        BattleEquipmentStack hand = rewardFlow.Hand;
        handGhost.gameObject.SetActive(true);
        if (handGhostIcon != null)
        {
            handGhostIcon.sprite = hand.equipment != null ? hand.equipment.icon : null;
            handGhostIcon.enabled = hand.equipment != null && hand.equipment.icon != null;
        }

        if (handPadMode)
        {
            RectTransform slot = FindRect($"GridSlot_{Mathf.Clamp(handPadSlot, 0, SlotCount - 1)}");
            if (slot != null)
                handGhost.position = slot.position + (Vector3)handPadOffset;
        }
        else if (Input.mousePresent)
        {
            handGhost.position = Input.mousePosition + (Vector3)handMouseOffset;
        }

        handGhost.SetAsLastSibling();
    }

    private void HideHandGhost()
    {
        if (handGhost != null && handGhost.gameObject.activeSelf)
            handGhost.gameObject.SetActive(false);
    }

    private void HandleDoneButtonPressed()
    {
        if (!IsReward() || rewardFlow == null || !rewardFlow.CanComplete)
            return;

        rewardFlow.SyncChosenRewardLocation();
        if (!rewardFlow.ChosenRewardCommitted)
            FinishWithoutReward();
        // Reward가 PACK에 남아 있는 경우에는 현재 단계에서 기존 InventoryInteraction의
        // DONE listener가 RunManager 완료를 수행합니다. Phase 3에서 이 마지막 bridge도 제거합니다.
    }

    private void FinishWithoutReward()
    {
        if (rewardFlow == null || !rewardFlow.CanComplete || rewardFlow.ChosenRewardCommitted)
            return;

        RestoreSuppressedInventory();
        SetLegacyPackHandlersEnabled(true);
        HideHandGhost();
        HideRewardEditStatus();
        rewardFlow.SkipReward();
    }

    private void SetPrizeChoicesInteractable(bool interactable)
    {
        if (prizeChoices == null)
            return;

        CanvasGroup group = prizeChoices.GetComponent<CanvasGroup>();
        if (group == null)
            group = prizeChoices.gameObject.AddComponent<CanvasGroup>();
        group.alpha = interactable ? 1f : 0.34f;
        group.blocksRaycasts = interactable;
        group.interactable = interactable;
    }

    private void HideRewardEditStatus()
    {
        if (rewardEditStatus == null && inventoryInteraction != null && rewardEditStatusField != null)
            rewardEditStatus = rewardEditStatusField.GetValue(inventoryInteraction) as Text;

        if (rewardEditStatus == null)
            return;

        rewardEditStatus.text = string.Empty;
        if (rewardEditStatus.gameObject.activeSelf)
            rewardEditStatus.gameObject.SetActive(false);
    }

    private void ApplyLowerRewardCameraBias()
    {
        if (selectionLayout == null)
            return;
        if (rewardCameraBiasField == null)
            rewardCameraBiasField = typeof(BattleSelectionLayoutPolicyController).GetField("rewardCameraBiasWorld", PrivateInstance);
        if (rewardCameraBiasField == null)
            return;

        object raw = rewardCameraBiasField.GetValue(selectionLayout);
        if (raw is not Vector2 current)
            return;

        if (current.y > rewardCameraBiasY)
            rewardCameraBiasField.SetValue(selectionLayout, new Vector2(current.x, rewardCameraBiasY));
    }

    private void ResetPresentationState()
    {
        initialPackSelectionMirrored = false;
        handPadMode = false;
        padAxisLatched = false;
        lastMousePositionValid = false;

        RestoreSuppressedInventory();
        SetLegacyPackHandlersEnabled(true);
        SetRewardCardDragEnabled(true);
        HideHandGhost();
        HideRewardEditStatus();
    }

    private int FindFirstUnlockedSlot()
    {
        if (equipmentSystem == null)
            return 0;
        for (int i = 0; i < SlotCount; i++)
            if (equipmentSystem.IsSlotUnlocked(i))
                return i;
        return 0;
    }

    private int GetPendingRewardIndex()
    {
        if (battleHud == null || pendingRewardIndexField == null)
            return -1;
        object raw = pendingRewardIndexField.GetValue(battleHud);
        return raw is int index ? index : -1;
    }

    private void SetPendingRewardIndex(int value)
    {
        if (battleHud != null && pendingRewardIndexField != null)
            pendingRewardIndexField.SetValue(battleHud, value);
    }

    private void InvokeLoadoutRefresh()
    {
        if (kineticLoadout != null && loadoutRefreshMethod != null)
            loadoutRefreshMethod.Invoke(kineticLoadout, null);
    }

    private bool IsReward()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
    }

    private bool HasHand => rewardFlow != null && rewardFlow.HasHand;

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

internal sealed class BattleRewardHandSlotRelay : MonoBehaviour, IPointerClickHandler
{
    private BattleRewardDecisionFlowController owner;
    private int slotIndex;

    public void Configure(BattleRewardDecisionFlowController controller, int index)
    {
        owner = controller;
        slotIndex = index;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            owner?.HandleHandSlotClick(slotIndex);
    }
}

internal sealed class BattleRewardHandTrashRelay : MonoBehaviour, IPointerClickHandler
{
    private BattleRewardDecisionFlowController owner;

    public void Configure(BattleRewardDecisionFlowController controller)
    {
        owner = controller;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            owner?.HandleHandTrashClick();
    }
}

/// <summary>
/// Phase 2부터 RewardFlow와 기존 UI adapter를 같은 Composition Root에 설치합니다.
/// 별도의 RewardFlow AutoInstaller를 추가하지 않습니다.
/// </summary>
public static class BattleRewardDecisionFlowAutoInstaller
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

            if (manager.GetComponent<BattleRewardFlow>() == null)
                Undo.AddComponent<BattleRewardFlow>(manager.gameObject);
            if (manager.GetComponent<BattleRewardDecisionFlowController>() == null)
                Undo.AddComponent<BattleRewardDecisionFlowController>(manager.gameObject);

            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponent()
    {
        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null)
                continue;

            if (manager.GetComponent<BattleRewardFlow>() == null)
                manager.gameObject.AddComponent<BattleRewardFlow>();
            if (manager.GetComponent<BattleRewardDecisionFlowController>() == null)
                manager.gameObject.AddComponent<BattleRewardDecisionFlowController>();
        }
    }
}
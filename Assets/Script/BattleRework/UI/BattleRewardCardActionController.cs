using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reward 후보 선택 화면의 authoritative UI owner입니다.
///
/// 책임:
/// - Reward 월드 아이템 Hover / Click / 선택 상태
/// - 월드 아이템 위에 커서를 올렸을 때 표시되는 PACK 스타일 설명 Tooltip
/// - Hover Tooltip의 위치 / 크기 / Pivot / Auto Flip 제어
/// - 아이템 획득 포기 버튼
/// - PACK 편집 진입 뒤 Selection Locked 오버레이
/// - 구형 Reward 카드 UI는 fallback 호환용으로만 유지
///
/// Reward business state는 BattleRewardFlow가 단독 소유합니다.
/// 이 클래스는 BattleHUD.pendingRewardIndex나 다른 Controller의 private field를 Reflection으로 읽지 않습니다.
/// RewardPrizeIndex marker만 읽어 카드의 stable index를 유지합니다.
///
/// 정적인 카드 Text/Layout/Graphic은 선택/Hover 상태가 바뀔 때만 다시 적용합니다.
/// 카드 크기/위치 Tween만 필요한 동안 프레임 단위로 갱신하여 World Space TV Canvas rebuild 비용을 제한합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(60000)]
public sealed class BattleRewardCardActionController : MonoBehaviour
{
    private static BattleRewardCardActionController instance;

    [Header("Card Layout")]
    [SerializeField] private Vector2 normalCardSize = new(330f, 340f);
    [SerializeField] private Vector2 inactiveCardSize = new(270f, 292f);
    [SerializeField] private Vector2 selectedCardSize = new(330f, 548f);
    [SerializeField, Min(0f)] private float cardGap = 42f;
    [SerializeField, Min(1f)] private float tweenSharpness = 18f;

    [Header("Actions")]
    [SerializeField] private Vector2 decideSize = new(202f, 46f);
    [SerializeField] private Vector2 skipSize = new(304f, 54f);

    [Header("Theme")]
    [SerializeField] private Color neutralCard = new(0.055f, 0.057f, 0.066f, 0.995f);
    [SerializeField] private Color selectedCard = new(0.145f, 0.045f, 0.115f, 0.995f);
    [SerializeField] private Color inactiveIconColor = new(0.48f, 0.49f, 0.52f, 0.52f);
    [SerializeField] private Color ink = new(0.012f, 0.013f, 0.016f, 0.995f);
    [SerializeField] private Color paper = new(0.96f, 0.96f, 0.96f, 1f);
    [SerializeField] private Color muted = new(0.58f, 0.59f, 0.62f, 1f);
    [SerializeField] private Color selectAccent = new(1f, 0.79f, 0.08f, 1f);
    [SerializeField] private Color hoverCyan = new(0.12f, 0.88f, 0.92f, 1f);

    [Header("Reward Item Hover Description (커서 올리면 뜨는 설명창)")]
    [Tooltip("Reward 선택 중 월드 아이템/Base 위에 커서를 올렸을 때 나타나는 설명창 크기입니다.")]
    [SerializeField] private Vector2 worldInspectSize = new(420f, 360f);
    [Tooltip("아이템에서 화면 중앙 쪽으로 설명창을 얼마나 끌어당길지 정합니다.")]
    [SerializeField, Range(0f, 1f)] private float worldInspectCenterBias = 0.72f;
    [Tooltip("아이템 중심과 설명창 사이에 확보할 최소 여백입니다.")]
    [SerializeField, Min(0f)] private float worldInspectTargetClearance = 76f;
    [Tooltip("설명창이 화면 바깥으로 잘리지 않도록 유지할 최소 여백입니다.")]
    [SerializeField, Min(0f)] private float worldInspectScreenMargin = 28f;
    [SerializeField, Range(0.7f, 1f)] private float worldInspectPopupStartScale = 0.88f;
    [SerializeField, Range(1f, 1.15f)] private float worldInspectPopupOvershootScale = 1.045f;
    [SerializeField, Range(0.05f, 0.30f)] private float worldInspectPopupDuration = 0.15f;
    [SerializeField, Min(0f)] private float worldInspectPopupTravel = 24f;

    [SerializeField] private Vector2 worldSkipSize = new(214f, 40f);

    [Header("Selection Locked")]
    [SerializeField] private Color lockedBack = new(0.006f, 0.008f, 0.012f, 0.90f);

    private BattleRunManager runManager;
    private BattleRewardFlow rewardFlow;
    private BattleEquipmentDetailPanelController equipmentDetailPanel;
    private BattleInventoryInteractionController inventoryInteraction;
    private BattleShowWorldSetController showWorldSet;
    private BattleShowPresentationManager presentation;

    private RectTransform rewardScreen;
    private RectTransform rewardInner;
    private RectTransform rewardCardRoot;
    private CanvasGroup rewardCardGroup;
    private RectTransform rewardNoticeRect;
    private GameObject rewardNoticeObject;

    private Text legacyFocusName;
    private Text legacyFocusStats;

    private RectTransform inlineDetailRoot;
    private Text descriptionText;
    private Text effectsText;
    private Text tagsText;

    private RectTransform decideRoot;
    private Button decideButton;

    private Button skipButton;
    private Text skipLabel;
    private Image skipBack;
    private bool skipHovered;

    private RectTransform lockedOverlay;
    private CanvasGroup lockedGroup;
    private Text lockedTitle;
    private Text lockedMessage;

    private bool detailPanelWasEnabled;
    private bool detailPanelSuppressed;

    private Canvas worldInspectCanvas;
    private RectTransform worldInspectRoot;
    private CanvasGroup worldInspectGroup;
    private RectTransform worldInspectTargetPivot;
    private Image worldInspectFrameImage;
    private BattleSpeechBubbleFrameFillController worldInspectFrameController;
    private BattleSpeechBubbleTailTriangleController worldInspectTailController;
    private BattleSpeechBubbleFrameStyle worldInspectRuntimeFrameStyle;
    private BattleSpeechBubbleTailStyle worldInspectRuntimeTailStyle;
    private bool worldInspectPopupAnimating;
    private float worldInspectPopupTime;
    private int worldInspectPopupContext = -1;
    private Vector2 worldInspectPopupStartPosition;
    private Vector2 worldInspectPopupTargetPosition;
    private BattleItemHeroThumbnail worldInspectHeroThumbnail;
    private BattleEquipmentBadgeStrip worldInspectBadgeStrip;
    private Text worldInspectTitle;
    private Text worldInspectMeta;
    private Text worldInspectDescription;
    private Text worldInspectTags;
    private Text worldInspectStats;
    private Text worldInspectHint;
    private RectTransform worldSkipRoot;
    private CanvasGroup worldSkipGroup;
    private Button worldSkipButton;
    private Text worldSkipLabel;

    private int hoveredRewardIndex = -1;
    private RectTransform cachedCardRoot;
    private int cachedCardCount = -1;
    private float nextResolveTime;

    private bool choicePresentationDirty = true;
    private int lastChoiceSelectedIndex = int.MinValue;
    private int lastChoiceHoveredIndex = int.MinValue;
    private bool lastChoiceHadSelection;
    private BattleRewardPhase lastPresentationPhase = (BattleRewardPhase)(-1);

    private readonly List<CardRef> cards = new();
    private readonly Dictionary<int, Vector2> animatedSizes = new();
    private readonly Dictionary<int, Vector2> animatedPositions = new();

    private struct RectSnapshot
    {
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 pivot;
        public Vector2 sizeDelta;
        public Vector2 anchoredPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    private sealed class CardRef
    {
        public int index;
        public RectTransform rect;
        public RewardPrizeIndex marker;
        public Image background;
        public Image icon;
        public Outline outline;
        public CanvasGroup group;
        public Button button;
        public readonly Dictionary<RectTransform, RectSnapshot> childLayout = new();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleRewardCardActionController>() != null)
            return;

        GameObject host = new("BattleRewardUIRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleRewardCardActionController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            return;
        }

        instance = this;
        ResolveReferences();
        ResolveUi(true);
    }

    private void OnEnable()
    {
        if (instance != null && instance != this)
            return;

        ResolveReferences();
        ResolveUi(true);
        nextResolveTime = 0f;
        choicePresentationDirty = true;
        lastChoiceSelectedIndex = int.MinValue;
        lastChoiceHoveredIndex = int.MinValue;
        lastChoiceHadSelection = false;
        lastPresentationPhase = (BattleRewardPhase)(-1);
    }

    private void OnDisable()
    {
        RestoreEquipmentDetailPanel();
        equipmentDetailPanel?.ClearRewardPreview();
        showWorldSet?.SetRewardShowcaseSelectedIndex(-1);
        HideWorldShowcaseUi(true);
        HideChoiceOnlyUi();
        SetLockedVisible(false);
    }

    private void OnDestroy()
    {
        RestoreEquipmentDetailPanel();
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        ResolveReferences();
        rewardFlow?.RefreshFromRunState();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.10f;
            ResolveUi(false);
        }

        if (!IsReward())
        {
            hoveredRewardIndex = -1;
            lastPresentationPhase = BattleRewardPhase.Inactive;
            choicePresentationDirty = true;
            SetLockedVisible(false);
            SetEquipmentDetailPanelSuppressed(false);
            equipmentDetailPanel?.ClearRewardPreview();
            showWorldSet?.SetRewardShowcaseSelectedIndex(-1);
            HideWorldShowcaseUi(true);
            HideChoiceOnlyUi();
            return;
        }

        BattleRewardPhase phase = rewardFlow != null
            ? rewardFlow.Phase
            : BattleRewardPhase.Inactive;
        bool phaseChanged = phase != lastPresentationPhase;
        if (phaseChanged)
        {
            lastPresentationPhase = phase;
            choicePresentationDirty = true;
            DisableLegacyCardMotion();
        }

        bool choice = phase == BattleRewardPhase.Choosing;
        bool transferring = phase == BattleRewardPhase.Transferring;
        bool packEdit = phase == BattleRewardPhase.PackEditing;

        bool worldShowcaseExpected =
            choice &&
            showWorldSet != null;

        bool worldShowcaseChoice =
            worldShowcaseExpected &&
            IsWorldShowcaseChoice();

        // Reward 선택에서는 구형 ITEM // DATA 상세창을 절대 사용하지 않습니다.
        // 월드 Showcase는 PACK 스타일의 전용 경량 Tooltip을 사용합니다.
        SetEquipmentDetailPanelSuppressed(
            choice ||
            transferring);

        if (choice)
        {
            SetLockedVisible(false);

            if (worldShowcaseExpected)
            {
                // 첫 Reward 프레임에 Showcase가 아직 Build 중이어도
                // 예전 TV Card가 한 프레임 번쩍 보이지 않게 먼저 숨깁니다.
                SetWorldShowcaseCardUiHidden();

                if (worldShowcaseChoice)
                    HandleWorldShowcaseInput();
            }
            else
            {
                SetChoiceInteractable(true);
            }

            HandleConfirmShortcut();
        }
        else if (transferring)
        {
            hoveredRewardIndex = -1;
            equipmentDetailPanel?.ClearRewardPreview();
            showWorldSet?.SetRewardShowcaseSelectedIndex(-1);
            HideWorldShowcaseUi(true);
            SetChoiceInteractable(false);
            HideChoiceOnlyUi();
            SetLockedVisible(true);
            SetLockedCopy("INSTALLING REWARD", "TRANSFER TO PACK");
        }
        else if (packEdit)
        {
            hoveredRewardIndex = -1;
            equipmentDetailPanel?.ClearRewardPreview();
            showWorldSet?.SetRewardShowcaseSelectedIndex(-1);
            HideWorldShowcaseUi(true);
            SetChoiceInteractable(false);
            HideChoiceOnlyUi();
            SetLockedVisible(true);
            SetLockedCopy("SELECTION LOCKED", "PACK EDIT IN PROGRESS");
        }
    }

    private void LateUpdate()
    {
        if (!IsReward() || rewardFlow == null)
            return;

        UpdateWorldInspectPopupAnimation();

        if (rewardFlow.Phase == BattleRewardPhase.Choosing)
        {
            if (showWorldSet != null)
            {
                SetWorldShowcaseCardUiHidden();

                if (IsWorldShowcaseChoice())
                    ApplyWorldShowcasePresentation();
                else
                    HideWorldShowcaseUi(false);

                return;
            }

            int selectedIndex = rewardFlow.SelectedChoiceIndex;
            bool hasSelection = rewardFlow.SelectedChoice != null;
            bool refreshStatic =
                choicePresentationDirty ||
                selectedIndex != lastChoiceSelectedIndex ||
                hoveredRewardIndex != lastChoiceHoveredIndex ||
                hasSelection != lastChoiceHadSelection;

            ApplyChoicePresentation(refreshStatic);

            lastChoiceSelectedIndex = selectedIndex;
            lastChoiceHoveredIndex = hoveredRewardIndex;
            lastChoiceHadSelection = hasSelection;
            choicePresentationDirty = false;
        }
        else if (rewardFlow.Phase == BattleRewardPhase.Transferring ||
                 rewardFlow.Phase == BattleRewardPhase.PackEditing)
        {
            AnimateLockedOverlay();
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (rewardFlow == null)
            rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        if (equipmentDetailPanel == null)
            equipmentDetailPanel = FindFirstObjectByType<BattleEquipmentDetailPanelController>(FindObjectsInactive.Include);
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        if (showWorldSet == null)
            showWorldSet = FindFirstObjectByType<BattleShowWorldSetController>(FindObjectsInactive.Include);
        if (presentation == null)
            presentation = FindFirstObjectByType<BattleShowPresentationManager>(FindObjectsInactive.Include);
    }

    private void ResolveUi(bool forceCards)
    {
        // PrizeSelectionScreen은 Show 동안 같은 공용 TV Frame을 유지합니다.
        // 이미 바인딩된 뒤에는 전체 RectTransform을 다시 검색하지 않습니다.
        RectTransform resolvedScreen = rewardScreen;
        if (resolvedScreen == null)
            resolvedScreen = FindRect("PrizeSelectionScreen");

        if (resolvedScreen != rewardScreen)
        {
            rewardScreen = resolvedScreen;
            rewardInner = null;
            rewardCardRoot = null;
            rewardCardGroup = null;
            rewardNoticeRect = null;
            rewardNoticeObject = null;
            legacyFocusName = null;
            legacyFocusStats = null;
            lockedOverlay = null;
            lockedGroup = null;
            lockedTitle = null;
            cachedCardRoot = null;
            cachedCardCount = -1;
            forceCards = true;
            choicePresentationDirty = true;
        }

        if (rewardScreen == null)
            return;

        rewardInner = rewardScreen.Find("ScreenInner") as RectTransform;
        if (rewardInner == null)
            return;

        RectTransform resolvedCards = rewardInner.Find("PrizeChoices") as RectTransform;
        if (resolvedCards != rewardCardRoot)
        {
            rewardCardRoot = resolvedCards;
            rewardCardGroup = rewardCardRoot != null ? rewardCardRoot.GetComponent<CanvasGroup>() : null;
            if (rewardCardRoot != null && rewardCardGroup == null)
                rewardCardGroup = rewardCardRoot.gameObject.AddComponent<CanvasGroup>();
            forceCards = true;
            choicePresentationDirty = true;
        }

        rewardNoticeRect = rewardInner.Find("PlacementNotice") as RectTransform;
        rewardNoticeObject = rewardNoticeRect != null ? rewardNoticeRect.gameObject : null;

        ResolveLegacyFocusTexts();
        EnsureLockedOverlay();

        int count = rewardCardRoot != null ? rewardCardRoot.childCount : -1;
        if (forceCards || rewardCardRoot != cachedCardRoot || count != cachedCardCount)
            RebuildCards();
    }

    private void ResolveLegacyFocusTexts()
    {
        if (rewardInner == null || (legacyFocusName != null && legacyFocusStats != null))
            return;

        for (int i = 0; i < rewardInner.childCount; i++)
        {
            Transform child = rewardInner.GetChild(i);
            Text text = child.GetComponent<Text>();
            if (text == null)
                continue;

            string value = text.text ?? string.Empty;
            if (legacyFocusName == null && value == "SELECT A PRIZE")
                legacyFocusName = text;
            else if (legacyFocusStats == null && value.Contains("Hover to inspect"))
                legacyFocusStats = text;
        }
    }

    private void RebuildCards()
    {
        cards.Clear();
        animatedSizes.Clear();
        animatedPositions.Clear();
        hoveredRewardIndex = -1;
        cachedCardRoot = rewardCardRoot;
        cachedCardCount = rewardCardRoot != null ? rewardCardRoot.childCount : -1;
        choicePresentationDirty = true;

        if (rewardCardRoot == null)
            return;

        for (int childIndex = 0; childIndex < rewardCardRoot.childCount; childIndex++)
        {
            RectTransform rect = rewardCardRoot.GetChild(childIndex) as RectTransform;
            if (rect == null)
                continue;

            RewardPrizeIndex marker = rect.GetComponent<RewardPrizeIndex>();
            int stableIndex = marker != null ? marker.RewardIndex : childIndex;

            DisableOtherRewardHoverRelays(rect);

            Image background = rect.GetComponent<Image>();
            Outline outline = rect.GetComponent<Outline>();
            if (outline == null)
                outline = rect.gameObject.AddComponent<Outline>();
            CanvasGroup group = rect.GetComponent<CanvasGroup>();
            if (group == null)
                group = rect.gameObject.AddComponent<CanvasGroup>();
            Button button = rect.GetComponent<Button>();
            if (button == null)
                button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.RemoveAllListeners();
            int captured = stableIndex;
            button.onClick.AddListener(() => SelectReward(captured));

            BattleRewardCardUiRelay relay = rect.GetComponent<BattleRewardCardUiRelay>();
            if (relay == null)
                relay = rect.gameObject.AddComponent<BattleRewardCardUiRelay>();
            relay.Configure(this, stableIndex);

            CardRef card = new()
            {
                index = stableIndex,
                rect = rect,
                marker = marker,
                background = background,
                icon = rect.Find("PrizeIcon")?.GetComponent<Image>(),
                outline = outline,
                group = group,
                button = button
            };

            RectTransform[] descendants = rect.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                RectTransform child = descendants[i];
                if (child == null || child == rect)
                    continue;
                card.childLayout[child] = Capture(child);
            }

            cards.Add(card);
        }

        cards.Sort((a, b) => a.index.CompareTo(b.index));
        DisableLegacyCardMotion();
    }

    private static void DisableOtherRewardHoverRelays(RectTransform card)
    {
        MonoBehaviour[] behaviours = card.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour is BattleRewardCardUiRelay)
                continue;

            string typeName = behaviour.GetType().Name;
            if (typeName == "BattleMonochromeRewardHoverRelay" ||
                typeName == "BattleRewardSelectionVisualPointer")
            {
                behaviour.enabled = false;
            }
        }
    }

    private void DisableLegacyCardMotion()
    {
        if (rewardCardRoot == null)
            return;

                for (int i = 0; i < cards.Count; i++)
        {
            CardRef card = cards[i];
            if (card == null || card.rect == null)
                continue;
            // Reward card interaction is owned here; legacy drag/hover relays were removed.
        }
    }

    private bool IsWorldShowcaseChoice()
    {
        return
            showWorldSet != null &&
            showWorldSet.HasRewardShowcase &&
            rewardFlow != null &&
            rewardFlow.Phase == BattleRewardPhase.Choosing;
    }

    private void SetWorldShowcaseCardUiHidden()
    {
        if (rewardCardGroup != null)
        {
            rewardCardGroup.alpha = 0f;
            rewardCardGroup.blocksRaycasts = false;
            rewardCardGroup.interactable = false;
        }

        for (int i = 0;
             i < cards.Count;
             i++)
        {
            CardRef card =
                cards[i];

            if (card?.button != null)
                card.button.interactable = false;
        }

        if (inlineDetailRoot != null &&
            inlineDetailRoot.gameObject.activeSelf)
        {
            inlineDetailRoot.gameObject.SetActive(false);
        }

        if (decideRoot != null &&
            decideRoot.gameObject.activeSelf)
        {
            decideRoot.gameObject.SetActive(false);
        }

        HideLegacyChoiceDescription();
        EnsureWorldShowcaseUi();
        SetWorldSkipVisible(true);
    }

    private void HandleWorldShowcaseInput()
    {
        if (showWorldSet == null ||
            rewardFlow == null ||
            BattlePauseController.IsPaused)
        {
            return;
        }

        // 전체 EventSystem UI를 이유로 차단하면 Hover 설명창/호스트 대화창처럼
        // Raycast와 무관한 Screen UI가 존재하는 것만으로 월드 상품 클릭까지 막힐 수 있습니다.
        // 실제로 클릭을 먹어야 하는 Reward Screen UI만 명시적으로 차단합니다.
        if (IsPointerOverWorldSkip())
            return;

        int hovered =
            showWorldSet.RewardHoveredIndex;

        if (hovered < 0 ||
            !Input.GetMouseButtonDown(0))
        {
            return;
        }

        if (rewardFlow.SelectedChoiceIndex == hovered &&
            rewardFlow.CanConfirmChoice)
        {
            ConfirmSelectedReward();
            return;
        }

        SelectReward(
            hovered);
    }

    private bool IsPointerOverWorldSkip()
    {
        if (worldSkipRoot == null ||
            worldSkipGroup == null ||
            !worldSkipRoot.gameObject.activeInHierarchy ||
            worldSkipGroup.alpha <= 0.01f ||
            !Input.mousePresent)
        {
            return false;
        }

        Canvas skipCanvas =
            worldSkipRoot.GetComponentInParent<Canvas>();

        Camera eventCamera =
            skipCanvas != null &&
            skipCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? skipCanvas.worldCamera
                : null;

        return RectTransformUtility.RectangleContainsScreenPoint(
            worldSkipRoot,
            Input.mousePosition,
            eventCamera);
    }

    /// <summary>
    /// Reward 월드 쇼케이스의 Hover 표시를 갱신합니다.
    /// BattleShowWorldSetController.RewardHoveredIndex가 0 이상이면
    /// 해당 아이템에 커서가 올라가 있는 상태이며, 이때 Hover 설명창을 표시합니다.
    /// 커서가 상품에서 빠지면 설명창을 즉시 숨깁니다.
    /// </summary>
    private void ApplyWorldShowcasePresentation()
    {
        if (showWorldSet == null ||
            rewardFlow == null)
        {
            return;
        }

        SetWorldShowcaseCardUiHidden();

        int hovered =
            showWorldSet.RewardHoveredIndex;

        int previousHover =
            hoveredRewardIndex;

        hoveredRewardIndex =
            hovered;

        int selectedIndex =
            rewardFlow.SelectedChoiceIndex;

        showWorldSet.SetRewardShowcaseSelectedIndex(
            selectedIndex);

        if (hovered >= 0)
        {
            if (showWorldSet.TryGetRewardShowcaseScreenPoint(
                    hovered,
                    out Vector2 screenPoint))
            {
                ShowWorldRewardInspect(
                    hovered,
                    screenPoint,
                    showWorldSet.ShouldPlaceRewardDetailRight(hovered),
                    selectedIndex == hovered);
            }

            if (hovered != previousHover &&
                runManager != null &&
                hovered <
                runManager.CurrentRewardChoices.Count)
            {
                BattleScreenPresenterPrototypeController.NotifyRewardHover(
                    runManager.CurrentRewardChoices[hovered]);
            }
        }
        else
        {
            HideWorldRewardInspect();
        }

        lastChoiceSelectedIndex =
            selectedIndex;

        lastChoiceHoveredIndex =
            hoveredRewardIndex;

        lastChoiceHadSelection =
            rewardFlow.SelectedChoice != null;

        choicePresentationDirty =
            false;
    }

    private void EnsureWorldShowcaseUi()
    {
        if (worldInspectCanvas != null)
            return;

        GameObject canvasObject =
            new("BattleRewardWorldInspectCanvas");

        canvasObject.transform.SetParent(
            transform,
            false);

        worldInspectCanvas =
            canvasObject.AddComponent<Canvas>();

        worldInspectCanvas.renderMode =
            RenderMode.ScreenSpaceOverlay;

        worldInspectCanvas.overrideSorting =
            true;

        // BattleKineticLoadoutUI(780)와 같은 UI 언어를 쓰되,
        // Reward Hover 정보가 그 위에서 읽히도록 한 단계만 올립니다.
        worldInspectCanvas.sortingOrder =
            790;

        CanvasScaler scaler =
            canvasObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;

        scaler.referenceResolution =
            new Vector2(
                1920f,
                1080f);

        scaler.matchWidthOrHeight =
            0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        Color packInk =
            new(
                0.028f,
                0.030f,
                0.036f,
                0.965f);

        Color packPaper =
            new(
                0.92f,
                0.94f,
                0.97f,
                1f);

        Color packCyan =
            new(
                0.15f,
                0.88f,
                0.92f,
                1f);

        worldInspectTargetPivot =
            CreateRect(
                canvasObject.transform,
                "RewardWorldItemTooltipTarget",
                Vector2.zero);

        worldInspectTargetPivot.anchorMin =
            worldInspectTargetPivot.anchorMax =
                new Vector2(
                    0.5f,
                    0.5f);

        worldInspectTargetPivot.pivot =
            new Vector2(
                0.5f,
                0.5f);

        worldInspectRoot =
            CreateRect(
                canvasObject.transform,
                "RewardWorldItemTooltip",
                worldInspectSize);

        worldInspectRoot.anchorMin =
            worldInspectRoot.anchorMax =
                new Vector2(
                    0.5f,
                    0.5f);

        worldInspectRoot.pivot =
            new Vector2(
                0.5f,
                0.5f);

        Image back =
            worldInspectRoot.gameObject.AddComponent<Image>();

        back.color =
            Color.clear;

        back.raycastTarget =
            false;

        GameObject frameObject =
            new(
                "RewardWorldItemTooltipFrame",
                typeof(RectTransform));

        frameObject.transform.SetParent(
            worldInspectRoot,
            false);

        worldInspectFrameImage =
            frameObject.AddComponent<Image>();

        worldInspectFrameImage.raycastTarget =
            false;

        worldInspectFrameController =
            frameObject.AddComponent<
                BattleSpeechBubbleFrameFillController>();

        worldInspectTailController =
            worldInspectRoot.gameObject.AddComponent<
                BattleSpeechBubbleTailTriangleController>();

        worldInspectTailController.SetExactTargetMode(
            true);

        frameObject.transform.SetAsFirstSibling();

        worldInspectGroup =
            worldInspectRoot.gameObject.AddComponent<CanvasGroup>();

        worldInspectGroup.alpha =
            0f;

        worldInspectGroup.blocksRaycasts =
            false;

        worldInspectGroup.interactable =
            false;

        RectTransform worldVisualViewport =
            CreateRect(
                worldInspectRoot,
                "ItemVisualViewport",
                Vector2.zero);

        SetAnchors(
            worldVisualViewport,
            new Vector2(
                0.06f,
                0.58f),
            new Vector2(
                0.94f,
                0.93f));

        Image worldVisualBack =
            worldVisualViewport.gameObject.AddComponent<Image>();

        worldVisualBack.color =
            new Color(
                packPaper.r,
                packPaper.g,
                packPaper.b,
                0.055f);

        worldVisualBack.raycastTarget =
            false;

        Outline worldVisualOutline =
            worldVisualViewport.gameObject.AddComponent<Outline>();

        worldVisualOutline.effectColor =
            new Color(
                packPaper.r,
                packPaper.g,
                packPaper.b,
                0.16f);

        worldVisualOutline.effectDistance =
            new Vector2(
                2f,
                -2f);

        worldInspectHeroThumbnail =
            BattleItemHeroThumbnail.Attach(
                worldVisualViewport,
                packPaper,
                packInk,
                packCyan);

        worldInspectTitle =
            CreateText(
                worldInspectRoot,
                "ITEM",
                20,
                FontStyle.Bold,
                TextAnchor.UpperLeft,
                packPaper);

        SetAnchors(
            worldInspectTitle.rectTransform,
            new Vector2(
                0.06f,
                0.505f),
            new Vector2(
                0.94f,
                0.595f));

        worldInspectMeta =
            CreateText(
                worldInspectRoot,
                "COMMON / MANUAL",
                10,
                FontStyle.Bold,
                TextAnchor.UpperLeft,
                packCyan);

        SetAnchors(
            worldInspectMeta.rectTransform,
            new Vector2(
                0.06f,
                0.415f),
            new Vector2(
                0.94f,
                0.48f));

        worldInspectDescription =
            CreateText(
                worldInspectRoot,
                "NO DESCRIPTION",
                12,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                packPaper);

        SetAnchors(
            worldInspectDescription.rectTransform,
            new Vector2(
                0.06f,
                0.245f),
            new Vector2(
                0.94f,
                0.405f));

        worldInspectDescription.horizontalOverflow =
            HorizontalWrapMode.Wrap;

        worldInspectDescription.verticalOverflow =
            VerticalWrapMode.Truncate;

        worldInspectTags =
            CreateText(
                worldInspectRoot,
                "NO TAG",
                10,
                FontStyle.Bold,
                TextAnchor.UpperLeft,
                packCyan);

        SetAnchors(
            worldInspectTags.rectTransform,
            new Vector2(
                0.06f,
                0.165f),
            new Vector2(
                0.94f,
                0.235f));

        worldInspectStats =
            CreateText(
                worldInspectRoot,
                "DMG ×1.00   MOVE ×1.00   RANGE ×1.00",
                10,
                FontStyle.Bold,
                TextAnchor.LowerLeft,
                packPaper);

        SetAnchors(
            worldInspectStats.rectTransform,
            new Vector2(
                0.06f,
                0.085f),
            new Vector2(
                0.94f,
                0.155f));

        worldInspectHint =
            CreateText(
                worldInspectRoot,
                "CLICK TO SELECT",
                9,
                FontStyle.Bold,
                TextAnchor.LowerRight,
                packCyan);

        SetAnchors(
            worldInspectHint.rectTransform,
            new Vector2(
                0.06f,
                0.025f),
            new Vector2(
                0.94f,
                0.08f));

        worldInspectBadgeStrip =
            BattleEquipmentBadgeStrip.Attach(
                worldInspectRoot,
                packPaper,
                new Color(
                    packInk.r,
                    packInk.g,
                    packInk.b,
                    1f),
                packPaper,
                46f,
                58f);

        worldInspectRoot.gameObject.SetActive(
            false);

        worldSkipRoot =
            CreateRect(
                canvasObject.transform,
                "RewardWorldSkip",
                worldSkipSize);

        worldSkipRoot.anchorMin =
            worldSkipRoot.anchorMax =
                new Vector2(
                    1f,
                    1f);

        worldSkipRoot.pivot =
            new Vector2(
                1f,
                1f);

        worldSkipRoot.anchoredPosition =
            new Vector2(
                -26f,
                -24f);

        Image skipBackground =
            worldSkipRoot.gameObject.AddComponent<Image>();

        skipBackground.color =
            new Color(
                packInk.r,
                packInk.g,
                packInk.b,
                0.90f);

        skipBackground.raycastTarget =
            true;

        Outline skipOutline =
            worldSkipRoot.gameObject.AddComponent<Outline>();

        skipOutline.effectColor =
            new Color(
                packPaper.r,
                packPaper.g,
                packPaper.b,
                0.24f);

        skipOutline.effectDistance =
            new Vector2(
                2f,
                -2f);

        worldSkipGroup =
            worldSkipRoot.gameObject.AddComponent<CanvasGroup>();

        worldSkipButton =
            worldSkipRoot.gameObject.AddComponent<Button>();

        worldSkipButton.targetGraphic =
            skipBackground;

        worldSkipButton.onClick.AddListener(
            SkipReward);

        worldSkipLabel =
            CreateText(
                worldSkipRoot,
                "SKIP REWARD",
                11,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                packPaper);

        Stretch(
            worldSkipLabel.rectTransform);

        worldSkipRoot.gameObject.SetActive(
            false);

        BattleUiAvoidanceResolver.RegisterZone(
            worldSkipRoot,
            80,
            18f,
            () =>
                worldSkipRoot != null &&
                worldSkipRoot.gameObject.activeInHierarchy &&
                worldSkipGroup != null &&
                worldSkipGroup.alpha > 0.01f);
    }

    /// <summary>
    /// Reward 아이템 위에 커서가 올라갔을 때 호출되는 설명창 표시 함수입니다.
    /// 아이템 이름 / 등급 / 타입 / 설명 / 태그 / DMG / MOVE / RANGE를
    /// PACK 스타일의 작은 Screen Space Tooltip으로 표시합니다.
    /// </summary>
    private void ShowWorldRewardInspect(
        int rewardIndex,
        Vector2 itemScreenPoint,
        bool placeRight,
        bool selected)
    {
        EnsureWorldShowcaseUi();

        BattleEquipmentSO equipment =
            GetReward(
                rewardIndex);

        if (equipment == null ||
            worldInspectRoot == null ||
            worldInspectGroup == null)
        {
            HideWorldRewardInspect();
            return;
        }

        worldInspectHeroThumbnail?.Show(
            equipment);

        worldInspectTitle.text =
            equipment.GetDisplayName().ToUpperInvariant();

        worldInspectMeta.text =
            $"{equipment.rarity.ToString().ToUpperInvariant()} / {equipment.type.ToString().ToUpperInvariant()}";

        worldInspectDescription.text =
            !string.IsNullOrWhiteSpace(
                equipment.description)
                ? equipment.description.Trim()
                : "NO DESCRIPTION";

        worldInspectTags.text =
            equipment.tags != null &&
            equipment.tags.Count > 0
                ? string.Join(
                    "  /  ",
                    equipment.tags).ToUpperInvariant()
                : "NO TAG";

        worldInspectStats.text =
            $"DMG ×{equipment.damageMultiplier:0.00}   " +
            $"MOVE ×{equipment.moveSpeedMultiplier:0.00}   " +
            $"RANGE ×{equipment.rangeMultiplier:0.00}";

        worldInspectHint.text =
            selected
                ? "SELECTED  //  CLICK AGAIN TO TAKE"
                : "CLICK TO SELECT";

        worldInspectHint.color =
            selected
                ? selectAccent
                : new Color(
                    0.15f,
                    0.88f,
                    0.92f,
                    1f);

        worldInspectBadgeStrip?.Show(
            equipment);

        PlaceWorldInspect(
            itemScreenPoint,
            placeRight);

        if (!worldInspectRoot.gameObject.activeSelf)
            worldInspectRoot.gameObject.SetActive(true);

        worldInspectRoot.SetAsLastSibling();

        PrepareWorldInspectPopup(
            rewardIndex);

        worldInspectGroup.blocksRaycasts = false;
        worldInspectGroup.interactable = false;
    }

    /// <summary>
    /// Hover 설명창을 현재 아이템의 Screen Point 기준으로 배치합니다.
    /// 실제 튜닝 값은 Inspector의 Reward Item Hover Description 섹션에서 조절합니다.
    /// worldInspectOffset / worldInspectPivot / Auto Flip 설정이 여기서 적용됩니다.
    /// </summary>
    private void PlaceWorldInspect(
        Vector2 itemScreenPoint,
        bool placeRight)
    {
        if (worldInspectCanvas == null ||
            worldInspectRoot == null ||
            worldInspectTargetPivot == null)
        {
            return;
        }

        RectTransform canvasRect =
            worldInspectCanvas.transform as RectTransform;

        if (canvasRect == null ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                itemScreenPoint,
                null,
                out Vector2 itemLocal))
        {
            return;
        }

        worldInspectTargetPivot.anchoredPosition =
            itemLocal;

        worldInspectRoot.anchorMin =
            worldInspectRoot.anchorMax =
                new Vector2(
                    0.5f,
                    0.5f);

        worldInspectRoot.pivot =
            new Vector2(
                0.5f,
                0.5f);

        worldInspectRoot.sizeDelta =
            new Vector2(
                Mathf.Max(
                    80f,
                    worldInspectSize.x),
                Mathf.Max(
                    60f,
                    worldInspectSize.y));

        Vector2 screenCenter =
            canvasRect.rect.center;

        Vector2 towardCenter =
            screenCenter -
            itemLocal;

        if (towardCenter.sqrMagnitude < 0.001f)
            towardCenter = Vector2.right;

        towardCenter.Normalize();

        Vector2 target =
            Vector2.Lerp(
                itemLocal,
                screenCenter,
                Mathf.Clamp01(
                    worldInspectCenterBias));

        Vector2 activeSize =
            worldInspectRoot.sizeDelta;

        float halfAlongDirection =
            Mathf.Abs(towardCenter.x) *
            activeSize.x * 0.5f +
            Mathf.Abs(towardCenter.y) *
            activeSize.y * 0.5f;

        float minimumDistance =
            halfAlongDirection +
            Mathf.Max(
                0f,
                worldInspectTargetClearance);

        Vector2 fromItem =
            target -
            itemLocal;

        if (fromItem.magnitude < minimumDistance)
        {
            target =
                itemLocal +
                towardCenter *
                minimumDistance;
        }

        target =
            BattleUiAvoidanceResolver.Resolve(
                canvasRect,
                target,
                activeSize,
                worldInspectRoot.pivot,
                worldInspectScreenMargin,
                itemScreenPoint);

        worldInspectPopupTargetPosition =
            target;

        if (!worldInspectPopupAnimating)
            worldInspectRoot.anchoredPosition =
                target;

        worldInspectRoot.localRotation =
            Quaternion.identity;
    }

    private void ConfigureWorldInspectBubble(
        int seed)
    {
        if (worldInspectRoot == null ||
            worldInspectTargetPivot == null ||
            worldInspectFrameController == null ||
            worldInspectTailController == null)
        {
            return;
        }

        BattleSpeechBubbleFrameStyle baseFrame =
            presentation != null
                ? presentation.SelectionSpeechBubbleFrameStyle
                : BattleSpeechBubbleFrameStyle.CreateSelectionDefault();

        BattleSpeechBubbleTailStyle baseTail =
            presentation != null
                ? presentation.SelectionSpeechBubbleTailStyle
                : new BattleSpeechBubbleTailStyle();

        BattleSpeechBubbleRuntimeVariationSettings variation =
            presentation != null
                ? presentation.SelectionSpeechBubbleVariation
                : null;

        worldInspectRuntimeFrameStyle =
            baseFrame.CreateRuntimeVariant(
                variation,
                seed ^ 0x3F21A7);

        worldInspectRuntimeTailStyle =
            baseTail.CreateRuntimeVariant(
                variation,
                seed ^ 0x71B3C9);

        worldInspectRuntimeFrameStyle.fillColor =
            new Color(
                0.028f,
                0.030f,
                0.036f,
                0.985f);

        worldInspectRuntimeFrameStyle.outlineColor =
            paper;

        worldInspectRuntimeFrameStyle.rotation =
            0f;

        Material strokeMaterial =
            presentation != null
                ? presentation.SpeechBubbleStrokeMaterial
                : null;

        worldInspectFrameController.Configure(
            worldInspectFrameImage,
            worldInspectRuntimeFrameStyle,
            worldInspectRoot.sizeDelta,
            strokeMaterial);

        worldInspectTailController.SetExactTargetMode(
            true);

        worldInspectTailController.Configure(
            worldInspectRoot,
            worldInspectTargetPivot,
            worldInspectRuntimeFrameStyle.fillColor,
            worldInspectRuntimeTailStyle,
            strokeMaterial);
    }

    private void PrepareWorldInspectPopup(
        int rewardIndex)
    {
        if (worldInspectRoot == null ||
            worldInspectGroup == null ||
            worldInspectTargetPivot == null)
        {
            return;
        }

        bool changed =
            worldInspectPopupContext !=
            rewardIndex;

        worldInspectPopupContext =
            rewardIndex;

        Vector2 itemLocal =
            worldInspectTargetPivot.anchoredPosition;

        Vector2 travelDirection =
            worldInspectPopupTargetPosition -
            itemLocal;

        if (travelDirection.sqrMagnitude < 0.001f)
            travelDirection = Vector2.right;

        travelDirection.Normalize();

        worldInspectPopupStartPosition =
            worldInspectPopupTargetPosition -
            travelDirection *
            Mathf.Max(
                0f,
                worldInspectPopupTravel);

        worldInspectTailController?.SetTarget(
            worldInspectTargetPivot);

        if (changed)
        {
            ConfigureWorldInspectBubble(
                rewardIndex);

            worldInspectPopupTime =
                0f;

            worldInspectPopupAnimating =
                true;

            worldInspectRoot.anchoredPosition =
                worldInspectPopupStartPosition;

            worldInspectRoot.localScale =
                Vector3.one *
                worldInspectPopupStartScale;

            worldInspectGroup.alpha =
                0f;
        }
        else if (!worldInspectPopupAnimating)
        {
            worldInspectRoot.anchoredPosition =
                worldInspectPopupTargetPosition;

            worldInspectRoot.localScale =
                Vector3.one;

            worldInspectGroup.alpha =
                1f;
        }
    }

    private void UpdateWorldInspectPopupAnimation()
    {
        if (!worldInspectPopupAnimating ||
            worldInspectRoot == null ||
            worldInspectGroup == null)
        {
            return;
        }

        worldInspectPopupTime +=
            Time.unscaledDeltaTime;

        float t =
            Mathf.Clamp01(
                worldInspectPopupTime /
                Mathf.Max(
                    0.01f,
                    worldInspectPopupDuration));

        const float overshootPoint =
            0.64f;

        float scale;

        if (t < overshootPoint)
        {
            float localT =
                t /
                overshootPoint;

            localT =
                1f -
                Mathf.Pow(
                    1f -
                    Mathf.Clamp01(
                        localT),
                    3f);

            scale =
                Mathf.Lerp(
                    worldInspectPopupStartScale,
                    worldInspectPopupOvershootScale,
                    localT);
        }
        else
        {
            float localT =
                (t -
                 overshootPoint) /
                (1f -
                 overshootPoint);

            localT =
                localT *
                localT *
                (3f -
                 2f * localT);

            scale =
                Mathf.Lerp(
                    worldInspectPopupOvershootScale,
                    1f,
                    localT);
        }

        float moveT =
            1f -
            Mathf.Pow(
                1f - t,
                3f);

        worldInspectRoot.anchoredPosition =
            Vector2.Lerp(
                worldInspectPopupStartPosition,
                worldInspectPopupTargetPosition,
                moveT);

        worldInspectRoot.localScale =
            Vector3.one *
            scale;

        worldInspectGroup.alpha =
            Mathf.Clamp01(
                t * 2.8f);

        if (t >= 1f)
        {
            worldInspectPopupAnimating =
                false;

            worldInspectRoot.anchoredPosition =
                worldInspectPopupTargetPosition;

            worldInspectRoot.localScale =
                Vector3.one;

            worldInspectGroup.alpha =
                1f;
        }
    }

    /// <summary>
    /// Reward 아이템에서 커서가 빠졌을 때 Hover 설명창을 숨깁니다.
    /// </summary>
    private void HideWorldRewardInspect()
    {
        worldInspectPopupAnimating =
            false;

        worldInspectPopupContext =
            -1;

        if (worldInspectTailController != null)
            worldInspectTailController.SetTarget(null);

        if (worldInspectGroup != null)
        {
            worldInspectGroup.alpha =
                0f;

            worldInspectGroup.blocksRaycasts =
                false;

            worldInspectGroup.interactable =
                false;
        }

        if (worldInspectRoot != null &&
            worldInspectRoot.gameObject.activeSelf)
        {
            worldInspectRoot.gameObject.SetActive(false);
        }
    }

    private void SetWorldSkipVisible(
        bool visible)
    {
        if (visible)
            EnsureWorldShowcaseUi();

        if (worldSkipRoot == null ||
            worldSkipGroup == null)
        {
            return;
        }

        if (worldSkipRoot.gameObject.activeSelf != visible)
            worldSkipRoot.gameObject.SetActive(visible);

        worldSkipGroup.alpha =
            visible ? 1f : 0f;

        worldSkipGroup.blocksRaycasts =
            visible &&
            !BattlePauseController.IsPaused;

        worldSkipGroup.interactable =
            visible &&
            !BattlePauseController.IsPaused;
    }

    private void HideWorldShowcaseUi(
        bool hideSkip)
    {
        HideWorldRewardInspect();

        if (hideSkip)
            SetWorldSkipVisible(false);
    }

    private void ApplyChoicePresentation(bool refreshStatic)
    {
        if (rewardCardRoot == null || rewardInner == null || rewardFlow == null)
            return;

        if (refreshStatic)
        {
            LayoutRewardCardArea();
            LayoutSkipButton();
            HideLegacyChoiceDescription();
            SetChoiceInteractable(true);
        }

        int selectedIndex = rewardFlow.SelectedChoiceIndex;
        bool hasSelection = rewardFlow.SelectedChoice != null;

        float totalWidth = 0f;
        for (int i = 0; i < cards.Count; i++)
        {
            bool selected = hasSelection && cards[i].index == selectedIndex;
            totalWidth += !hasSelection
                ? normalCardSize.x
                : selected ? selectedCardSize.x : inactiveCardSize.x;
        }
        totalWidth += Mathf.Max(0, cards.Count - 1) * cardGap;

        float cursor = -totalWidth * 0.5f;
        float blend = 1f - Mathf.Exp(-Mathf.Max(1f, tweenSharpness) * Time.unscaledDeltaTime);
        CardRef selectedCardRef = null;

        for (int i = 0; i < cards.Count; i++)
        {
            CardRef card = cards[i];
            if (card == null || card.rect == null)
                continue;

            bool selected = hasSelection && card.index == selectedIndex;
            bool hovered = !hasSelection && card.index == hoveredRewardIndex;
            Vector2 targetSize = !hasSelection
                ? normalCardSize
                : selected ? selectedCardSize : inactiveCardSize;
            Vector2 targetPosition = new(cursor + targetSize.x * 0.5f, 0f);
            cursor += targetSize.x + cardGap;

            int id = card.rect.GetInstanceID();
            if (!animatedSizes.TryGetValue(id, out Vector2 currentSize))
                currentSize = card.rect.sizeDelta.sqrMagnitude > 1f ? card.rect.sizeDelta : normalCardSize;
            if (!animatedPositions.TryGetValue(id, out Vector2 currentPosition))
                currentPosition = card.rect.anchoredPosition;

            currentSize = Vector2.Lerp(currentSize, targetSize, blend);
            currentPosition = Vector2.Lerp(currentPosition, targetPosition, blend);

            // Lerp의 미세한 꼬리를 매 프레임 Canvas dirty로 만들지 않도록 목표 근처에서 정확히 스냅합니다.
            if ((currentSize - targetSize).sqrMagnitude <= 0.01f)
                currentSize = targetSize;
            if ((currentPosition - targetPosition).sqrMagnitude <= 0.01f)
                currentPosition = targetPosition;

            animatedSizes[id] = currentSize;
            animatedPositions[id] = currentPosition;

            if (refreshStatic)
            {
                card.rect.anchorMin = card.rect.anchorMax = new Vector2(0.5f, 0.5f);
                card.rect.pivot = new Vector2(0.5f, 0.5f);
                card.rect.localScale = Vector3.one;
                card.rect.localRotation = Quaternion.identity;
            }

            if ((card.rect.sizeDelta - currentSize).sqrMagnitude > 0.0001f)
                card.rect.sizeDelta = currentSize;
            if ((card.rect.anchoredPosition - currentPosition).sqrMagnitude > 0.0001f)
                card.rect.anchoredPosition = currentPosition;

            if (refreshStatic)
            {
                RestoreChildLayout(card);
                BattleEquipmentSO equipment = GetReward(card.index);
                ConfigureBuiltInCardContent(card.rect, equipment, selected);

                if (card.group != null)
                    card.group.alpha = !hasSelection ? 0.82f : selected ? 1f : 0.46f;
                if (card.background != null)
                    card.background.color = selected ? this.selectedCard : neutralCard;

                if (card.outline != null)
                {
                    card.outline.enabled = selected || hovered;
                    if (selected)
                    {
                        card.outline.effectColor = selectAccent;
                        card.outline.effectDistance = new Vector2(5f, -5f);
                    }
                    else if (hovered)
                    {
                        card.outline.effectColor = hoverCyan;
                        card.outline.effectDistance = new Vector2(4f, -4f);
                    }
                }

                if (card.icon != null)
                {
                    card.icon.material = null;
                    card.icon.color = selected || hovered ? Color.white : inactiveIconColor;
                }
            }

            if (selected)
                selectedCardRef = card;
        }

        if (!hasSelection || selectedCardRef == null)
        {
            if (refreshStatic)
            {
                if (inlineDetailRoot != null && inlineDetailRoot.gameObject.activeSelf)
                    inlineDetailRoot.gameObject.SetActive(false);
                if (decideRoot != null && decideRoot.gameObject.activeSelf)
                    decideRoot.gameObject.SetActive(false);
            }
            return;
        }

        if (refreshStatic || inlineDetailRoot == null || decideRoot == null || inlineDetailRoot.parent != selectedCardRef.rect)
        {
            BattleEquipmentSO selectedEquipment = GetReward(selectedIndex);
            EnsureInlineDetail(selectedCardRef.rect);
            RefreshInlineDetail(selectedEquipment);
            EnsureDecisionButton(selectedCardRef.rect);
        }
    }

    private void LayoutRewardCardArea()
    {
        rewardCardRoot.anchorMin = new Vector2(0.055f, 0.17f);
        rewardCardRoot.anchorMax = new Vector2(0.945f, 0.84f);
        rewardCardRoot.offsetMin = Vector2.zero;
        rewardCardRoot.offsetMax = Vector2.zero;
        rewardCardRoot.pivot = new Vector2(0.5f, 0.5f);

        Text[] texts = rewardInner.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text != null && (text.text ?? string.Empty).Contains("CHOOSE YOUR PRIZE"))
                text.fontSize = 38;
        }
    }

    private void RestoreChildLayout(CardRef card)
    {
        foreach (KeyValuePair<RectTransform, RectSnapshot> pair in card.childLayout)
        {
            RectTransform rect = pair.Key;
            if (rect == null || IsChildOf(rect, inlineDetailRoot) || IsChildOf(rect, decideRoot))
                continue;
            ApplySnapshot(rect, pair.Value);
        }
    }

    private void ConfigureBuiltInCardContent(RectTransform card, BattleEquipmentSO equipment, bool selected)
    {
        if (card == null || equipment == null)
            return;

        RectTransform icon = card.Find("PrizeIcon") as RectTransform;
        if (icon != null && selected)
        {
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.80f);
            icon.anchoredPosition = Vector2.zero;
            icon.sizeDelta = new Vector2(126f, 126f);
        }

        string displayName = equipment.GetDisplayName();
        string rarity = equipment.rarity.ToString().ToUpperInvariant();
        string type = equipment.type.ToString().ToUpperInvariant();

        Text[] texts = card.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null || IsChildOf(text.transform, inlineDetailRoot) || IsChildOf(text.transform, decideRoot))
                continue;

            string value = text.text ?? string.Empty;
            bool action = value.Contains("CLICK") || value.Contains("DRAG");
            bool itemName = value == displayName;
            bool itemType = value == type;
            bool itemRarity = value == rarity || (value.StartsWith("LV.") && value.Contains(rarity));

            if (action)
            {
                text.gameObject.SetActive(!selected);
                if (!selected)
                    text.text = "CLICK TO SELECT";
                continue;
            }

            text.gameObject.SetActive(true);
            if (!selected)
                continue;

            if (itemRarity)
                SetAnchors(text.rectTransform, new Vector2(0.08f, 0.675f), new Vector2(0.92f, 0.72f));
            else if (itemName)
            {
                SetAnchors(text.rectTransform, new Vector2(0.06f, 0.61f), new Vector2(0.94f, 0.675f));
                text.fontSize = Mathf.Max(16, text.fontSize);
            }
            else if (itemType)
                SetAnchors(text.rectTransform, new Vector2(0.08f, 0.56f), new Vector2(0.92f, 0.61f));
        }
    }

    private void EnsureInlineDetail(RectTransform selectedCardRect)
    {
        if (selectedCardRect == null)
            return;

        if (inlineDetailRoot == null)
        {
            inlineDetailRoot = CreateRect(selectedCardRect, "RewardSelectedInlineDetail", Vector2.zero);

            Text descriptionTitle = CreateText(inlineDetailRoot, "DESCRIPTION", 10, FontStyle.Bold, TextAnchor.MiddleLeft, muted);
            SetAnchors(descriptionTitle.rectTransform, new Vector2(0f, 0.86f), new Vector2(1f, 1f));

            descriptionText = CreateText(inlineDetailRoot, string.Empty, 10, FontStyle.Normal, TextAnchor.UpperLeft, paper);
            descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descriptionText.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(descriptionText.rectTransform, new Vector2(0f, 0.58f), new Vector2(1f, 0.86f));

            Text effectsTitle = CreateText(inlineDetailRoot, "EFFECTS", 10, FontStyle.Bold, TextAnchor.MiddleLeft, muted);
            SetAnchors(effectsTitle.rectTransform, new Vector2(0f, 0.48f), new Vector2(1f, 0.58f));

            effectsText = CreateText(inlineDetailRoot, string.Empty, 10, FontStyle.Bold, TextAnchor.UpperLeft, paper);
            effectsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            effectsText.verticalOverflow = VerticalWrapMode.Truncate;
            effectsText.lineSpacing = 1.08f;
            SetAnchors(effectsText.rectTransform, new Vector2(0f, 0.15f), new Vector2(1f, 0.48f));

            tagsText = CreateText(inlineDetailRoot, string.Empty, 9, FontStyle.Bold, TextAnchor.MiddleLeft, muted);
            tagsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tagsText.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(tagsText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.15f));
        }

        if (inlineDetailRoot.parent != selectedCardRect)
            inlineDetailRoot.SetParent(selectedCardRect, false);

        inlineDetailRoot.anchorMin = new Vector2(0.085f, 0.17f);
        inlineDetailRoot.anchorMax = new Vector2(0.915f, 0.52f);
        inlineDetailRoot.offsetMin = Vector2.zero;
        inlineDetailRoot.offsetMax = Vector2.zero;
        inlineDetailRoot.localScale = Vector3.one;
        inlineDetailRoot.localRotation = Quaternion.identity;
        inlineDetailRoot.gameObject.SetActive(true);
        inlineDetailRoot.SetAsLastSibling();
    }

    private void RefreshInlineDetail(BattleEquipmentSO equipment)
    {
        if (equipment == null || inlineDetailRoot == null)
            return;

        if (descriptionText != null)
        {
            descriptionText.text = !string.IsNullOrWhiteSpace(equipment.description)
                ? equipment.description.Trim()
                : equipment.shootingData != null ? "Manual weapon." : "Equipment item.";
        }

        if (effectsText != null)
        {
            effectsText.text =
                $"• DAMAGE        ×{equipment.damageMultiplier:0.00}\n" +
                $"• MOVE SPEED    ×{equipment.moveSpeedMultiplier:0.00}\n" +
                $"• RANGE         ×{equipment.rangeMultiplier:0.00}";
        }

        if (tagsText != null)
            tagsText.text = BuildTagLine(equipment);
    }

    private void EnsureDecisionButton(RectTransform selectedCardRect)
    {
        if (selectedCardRect == null)
            return;

        if (decideRoot == null)
        {
            decideRoot = CreateRect(selectedCardRect, "RewardInlineDecide", decideSize);
            Image back = decideRoot.gameObject.AddComponent<Image>();
            back.color = ink;
            back.raycastTarget = true;

            Outline outline = decideRoot.gameObject.AddComponent<Outline>();
            outline.effectColor = selectAccent;
            outline.effectDistance = new Vector2(4f, -4f);

            Text label = CreateText(decideRoot, "결정", 15, FontStyle.Bold, TextAnchor.MiddleCenter, paper);
            Stretch(label.rectTransform);

            decideButton = decideRoot.gameObject.AddComponent<Button>();
            decideButton.targetGraphic = back;
            decideButton.onClick.AddListener(ConfirmSelectedReward);
        }

        if (decideRoot.parent != selectedCardRect)
            decideRoot.SetParent(selectedCardRect, false);

        decideRoot.anchorMin = decideRoot.anchorMax = new Vector2(0.5f, 0.065f);
        decideRoot.pivot = new Vector2(0.5f, 0.5f);
        decideRoot.sizeDelta = decideSize;
        decideRoot.anchoredPosition = Vector2.zero;
        decideRoot.localScale = Vector3.one;
        decideRoot.localRotation = Quaternion.identity;
        decideRoot.gameObject.SetActive(true);
        decideRoot.SetAsLastSibling();
    }

    private void HandleConfirmShortcut()
    {
        if (BattlePauseController.IsPaused || rewardFlow == null || !rewardFlow.CanConfirmChoice)
            return;

        if (Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.Space) ||
            Input.GetKeyDown(KeyCode.JoystickButton0))
        {
            ConfirmSelectedReward();
        }
    }

    private void SelectReward(int index)
    {
        if (!IsChoiceStage() || rewardFlow == null)
            return;

        int previous = rewardFlow.SelectedChoiceIndex;
        if (rewardFlow.SelectChoice(index) && previous != index)
        {
            inventoryInteraction?.PlayRewardSelectFeedback();
            BattleScreenPresenterPrototypeController.NotifyRewardSelected(rewardFlow.SelectedChoice);
        }

        choicePresentationDirty = true;
    }

    private void ConfirmSelectedReward()
    {
        if (!IsChoiceStage() || rewardFlow == null || !rewardFlow.CanConfirmChoice)
            return;

        int selectedIndex = rewardFlow.SelectedChoiceIndex;
        BattleEquipmentSO selectedReward = rewardFlow.SelectedChoice;

        Vector2 worldSourceScreen =
            Vector2.zero;

        bool hasWorldSource =
            showWorldSet != null &&
            showWorldSet.TryGetRewardShowcaseScreenPoint(
                selectedIndex,
                out worldSourceScreen);

        CardRef selectedCardRef =
            FindCard(
                selectedIndex);

        RectTransform sourceRect =
            selectedCardRef != null &&
            selectedCardRef.icon != null
                ? selectedCardRef.icon.rectTransform
                : selectedCardRef?.rect;

        if (!rewardFlow.ConfirmSelectedChoice())
            return;

        BattleScreenPresenterPrototypeController.NotifyRewardConfirm(selectedReward);

        hoveredRewardIndex = -1;
        choicePresentationDirty = true;
        HideChoiceOnlyUi();
        SetChoiceInteractable(false);
        SetLockedVisible(true);
        SetLockedCopy("INSTALLING REWARD", "TRANSFER TO PACK");

        bool transferStarted =
            inventoryInteraction != null &&
            selectedReward != null &&
            (hasWorldSource
                ? inventoryInteraction.PlayRewardTransfer(
                    worldSourceScreen,
                    selectedReward,
                    rewardFlow.TransferTargetSlot,
                    rewardFlow.TransferToHand,
                    selectedReward.rarity,
                    CommitRewardTransferArrival,
                    CompleteRewardTransfer)
                : inventoryInteraction.PlayRewardTransfer(
                    sourceRect,
                    selectedReward,
                    rewardFlow.TransferTargetSlot,
                    rewardFlow.TransferToHand,
                    selectedReward.rarity,
                    CommitRewardTransferArrival,
                    CompleteRewardTransfer));

        if (!transferStarted)
        {
            CommitRewardTransferArrival();
            CompleteRewardTransfer();
        }
    }

    private void CommitRewardTransferArrival()
    {
        if (rewardFlow == null || rewardFlow.Phase != BattleRewardPhase.Transferring)
            return;

        rewardFlow.CommitTransferArrival();
    }

    private void CompleteRewardTransfer()
    {
        if (rewardFlow == null || rewardFlow.Phase != BattleRewardPhase.Transferring)
            return;

        rewardFlow.CompleteTransfer();
        choicePresentationDirty = true;
        SetLockedCopy("SELECTION LOCKED", "PACK EDIT IN PROGRESS");
    }

    private CardRef FindCard(int index)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null && cards[i].index == index)
                return cards[i];
        }
        return null;
    }

    private void LayoutSkipButton()
    {
        if (rewardNoticeRect == null || rewardNoticeObject == null)
            return;

        rewardNoticeObject.SetActive(true);
        rewardNoticeRect.anchorMin = rewardNoticeRect.anchorMax = new Vector2(0.5f, 0.055f);
        rewardNoticeRect.pivot = new Vector2(0.5f, 0.5f);
        rewardNoticeRect.sizeDelta = skipSize;
        rewardNoticeRect.anchoredPosition = Vector2.zero;
        rewardNoticeRect.localScale = Vector3.one;
        rewardNoticeRect.localRotation = Quaternion.Euler(0f, 0f, -1.4f);

        skipBack ??= rewardNoticeObject.GetComponent<Image>();
        if (skipBack != null)
        {
            skipBack.color = skipHovered ? new Color(0.095f, 0.10f, 0.115f, 1f) : ink;
            skipBack.raycastTarget = true;
        }

        Outline outline = rewardNoticeObject.GetComponent<Outline>();
        if (outline == null)
            outline = rewardNoticeObject.AddComponent<Outline>();
        outline.effectColor = skipHovered ? paper : new Color(paper.r, paper.g, paper.b, 0.68f);
        outline.effectDistance = skipHovered ? new Vector2(4f, -4f) : new Vector2(3f, -3f);

        skipLabel = FindFirstDirectText(rewardNoticeRect);
        if (skipLabel == null)
        {
            skipLabel = CreateText(rewardNoticeRect, "아이템 획득 포기하기", 14, FontStyle.Bold, TextAnchor.MiddleCenter, paper);
            Stretch(skipLabel.rectTransform);
        }
        skipLabel.gameObject.SetActive(true);
        skipLabel.text = "아이템 획득 포기하기";
        skipLabel.fontSize = 14;
        skipLabel.fontStyle = FontStyle.Bold;
        skipLabel.alignment = TextAnchor.MiddleCenter;
        skipLabel.color = paper;
        Stretch(skipLabel.rectTransform);

        skipButton ??= rewardNoticeObject.GetComponent<Button>();
        if (skipButton == null)
        {
            skipButton = rewardNoticeObject.AddComponent<Button>();
            skipButton.targetGraphic = skipBack;
        }
        skipButton.onClick.RemoveAllListeners();
        skipButton.onClick.AddListener(SkipReward);

        BattleRewardSkipHoverRelay hover = rewardNoticeObject.GetComponent<BattleRewardSkipHoverRelay>();
        if (hover == null)
            hover = rewardNoticeObject.AddComponent<BattleRewardSkipHoverRelay>();
        hover.Configure(this);
    }

    internal void SetSkipHover(bool hovered)
    {
        if (skipHovered == hovered)
            return;

        skipHovered = hovered;
        LayoutSkipButton();
    }

    private void SkipReward()
    {
        if (!IsChoiceStage() || rewardFlow == null)
            return;

        rewardFlow.ClearChoice();
        rewardFlow.SkipReward();
        choicePresentationDirty = true;
    }

    internal void SetCardHover(int rewardIndex, bool entered)
    {
        if (!IsChoiceStage())
            return;

        int previous = hoveredRewardIndex;
        if (rewardFlow != null && rewardFlow.SelectedChoiceIndex >= 0)
        {
            hoveredRewardIndex = -1;
        }
        else if (entered)
        {
            hoveredRewardIndex = rewardIndex;
        }
        else if (hoveredRewardIndex == rewardIndex)
        {
            hoveredRewardIndex = -1;
        }

        if (previous != hoveredRewardIndex)
        {
            choicePresentationDirty = true;

            if (hoveredRewardIndex >= 0 &&
                runManager != null &&
                hoveredRewardIndex < runManager.CurrentRewardChoices.Count)
            {
                BattleScreenPresenterPrototypeController.NotifyRewardHover(
                    runManager.CurrentRewardChoices[hoveredRewardIndex]);
            }
        }
    }

    private void SetChoiceInteractable(bool interactable)
    {
        if (rewardCardGroup != null)
        {
            rewardCardGroup.alpha = interactable ? 1f : 0.16f;
            rewardCardGroup.blocksRaycasts = interactable && !BattlePauseController.IsPaused;
            rewardCardGroup.interactable = interactable && !BattlePauseController.IsPaused;
        }

        for (int i = 0; i < cards.Count; i++)
            if (cards[i]?.button != null)
                cards[i].button.interactable = interactable && !BattlePauseController.IsPaused;
    }

    private void HideLegacyChoiceDescription()
    {
        if (legacyFocusName != null)
            legacyFocusName.gameObject.SetActive(false);
        if (legacyFocusStats != null)
            legacyFocusStats.gameObject.SetActive(false);

        RectTransform descriptionBar = rewardInner != null
            ? rewardInner.Find("RewardActiveDescriptionBar") as RectTransform
            : null;
        if (descriptionBar != null && descriptionBar.gameObject.activeSelf)
            descriptionBar.gameObject.SetActive(false);

        RectTransform oldDecide = FindChildByName(rewardInner, "RewardDecisionConfirm");
        if (oldDecide != null && oldDecide != decideRoot && oldDecide.gameObject.activeSelf)
            oldDecide.gameObject.SetActive(false);
    }

    private void HideChoiceOnlyUi()
    {
        if (inlineDetailRoot != null && inlineDetailRoot.gameObject.activeSelf)
            inlineDetailRoot.gameObject.SetActive(false);
        if (decideRoot != null && decideRoot.gameObject.activeSelf)
            decideRoot.gameObject.SetActive(false);
        if (rewardNoticeObject != null && rewardNoticeObject.activeSelf)
            rewardNoticeObject.SetActive(false);
        skipHovered = false;
    }

    private void SetEquipmentDetailPanelSuppressed(bool suppress)
    {
        if (equipmentDetailPanel == null)
            return;

        if (suppress)
        {
            if (!detailPanelSuppressed)
            {
                detailPanelWasEnabled = equipmentDetailPanel.enabled;
                detailPanelSuppressed = true;
            }
            if (equipmentDetailPanel.enabled)
                equipmentDetailPanel.enabled = false;
        }
        else
        {
            RestoreEquipmentDetailPanel();
        }
    }

    private void RestoreEquipmentDetailPanel()
    {
        if (!detailPanelSuppressed || equipmentDetailPanel == null)
            return;
        equipmentDetailPanel.enabled = detailPanelWasEnabled;
        detailPanelSuppressed = false;
    }

    private void EnsureLockedOverlay()
    {
        if (rewardInner == null || lockedOverlay != null)
            return;

        lockedOverlay = CreateRect(rewardInner, "RewardSelectionLockedOverlay", Vector2.zero);
        Stretch(lockedOverlay);
        Image back = lockedOverlay.gameObject.AddComponent<Image>();
        back.color = lockedBack;
        back.raycastTarget = false;

        lockedGroup = lockedOverlay.gameObject.AddComponent<CanvasGroup>();
        lockedGroup.blocksRaycasts = false;
        lockedGroup.interactable = false;

        lockedTitle = CreateText(lockedOverlay, "SELECTION LOCKED", 42, FontStyle.Bold, TextAnchor.MiddleCenter, paper);
        SetAnchors(lockedTitle.rectTransform, new Vector2(0.12f, 0.47f), new Vector2(0.88f, 0.63f));
        lockedMessage = CreateText(lockedOverlay, "PACK EDIT IN PROGRESS", 14, FontStyle.Bold, TextAnchor.MiddleCenter, hoverCyan);
        SetAnchors(lockedMessage.rectTransform, new Vector2(0.12f, 0.38f), new Vector2(0.88f, 0.47f));

        lockedOverlay.gameObject.SetActive(false);
    }

    private void SetLockedCopy(string title, string message)
    {
        if (lockedTitle != null)
            lockedTitle.text = title;
        if (lockedMessage != null)
            lockedMessage.text = message;
    }

    private void SetLockedVisible(bool visible)
    {
        if (lockedOverlay == null)
            EnsureLockedOverlay();
        if (lockedOverlay == null)
            return;

        bool changed = lockedOverlay.gameObject.activeSelf != visible;
        if (changed)
            lockedOverlay.gameObject.SetActive(visible);

        if (visible && lockedOverlay.GetSiblingIndex() != lockedOverlay.parent.childCount - 1)
            lockedOverlay.SetAsLastSibling();
    }

    private void AnimateLockedOverlay()
    {
        // Intentionally static: Selection Locked communicates state only.
        // Decorative stripes/jitter were removed because they carried no interaction meaning.
    }

    private bool IsReward()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
    }

    private bool IsChoiceStage()
    {
        return IsReward() && rewardFlow != null && rewardFlow.Phase == BattleRewardPhase.Choosing;
    }

    private BattleEquipmentSO GetReward(int index)
    {
        if (runManager == null || index < 0 || index >= runManager.CurrentRewardChoices.Count)
            return null;
        return runManager.CurrentRewardChoices[index];
    }

    private static string BuildTagLine(BattleEquipmentSO equipment)
    {
        if (equipment == null || equipment.tags == null || equipment.tags.Count == 0)
            return "TAGS  //  --";

        StringBuilder builder = new("TAGS  //  ");
        for (int i = 0; i < equipment.tags.Count; i++)
        {
            if (i > 0)
                builder.Append("  ·  ");
            builder.Append(equipment.tags[i].ToString().ToUpperInvariant());
        }
        return builder.ToString();
    }

    private static RectSnapshot Capture(RectTransform rect)
    {
        return new RectSnapshot
        {
            anchorMin = rect.anchorMin,
            anchorMax = rect.anchorMax,
            pivot = rect.pivot,
            sizeDelta = rect.sizeDelta,
            anchoredPosition = rect.anchoredPosition,
            localRotation = rect.localRotation,
            localScale = rect.localScale
        };
    }

    private static void ApplySnapshot(RectTransform rect, RectSnapshot snapshot)
    {
        rect.anchorMin = snapshot.anchorMin;
        rect.anchorMax = snapshot.anchorMax;
        rect.pivot = snapshot.pivot;
        rect.sizeDelta = snapshot.sizeDelta;
        rect.anchoredPosition = snapshot.anchoredPosition;
        rect.localRotation = snapshot.localRotation;
        rect.localScale = snapshot.localScale;
    }

    private static bool IsChildOf(Transform child, Transform parent)
    {
        if (child == null || parent == null)
            return false;
        Transform current = child;
        while (current != null)
        {
            if (current == parent)
                return true;
            current = current.parent;
        }
        return false;
    }

    private static RectTransform FindChildByName(Transform root, string objectName)
    {
        if (root == null)
            return null;
        RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < rects.Length; i++)
            if (rects[i] != null && rects[i].name == objectName)
                return rects[i];
        return null;
    }

    private static Text FindFirstDirectText(Transform root)
    {
        if (root == null)
            return null;
        for (int i = 0; i < root.childCount; i++)
        {
            Text text = root.GetChild(i).GetComponent<Text>();
            if (text != null)
                return text;
        }
        return root.GetComponentInChildren<Text>(true);
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

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static Image CreateImage(Transform parent, string name, Vector2 size)
    {
        RectTransform rect = CreateRect(parent, name, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static Text CreateText(Transform parent, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        RectTransform rect = CreateRect(parent, "Text", Vector2.zero);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

internal sealed class BattleRewardCardUiRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private BattleRewardCardActionController owner;
    private int rewardIndex;

    public void Configure(BattleRewardCardActionController controller, int index)
    {
        owner = controller;
        rewardIndex = index;
    }

    public void OnPointerEnter(PointerEventData eventData) => owner?.SetCardHover(rewardIndex, true);
    public void OnPointerExit(PointerEventData eventData) => owner?.SetCardHover(rewardIndex, false);
}

internal sealed class BattleRewardSkipHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private BattleRewardCardActionController owner;

    public void Configure(BattleRewardCardActionController controller)
    {
        owner = controller;
    }

    public void OnPointerEnter(PointerEventData eventData) => owner?.SetSkipHover(true);
    public void OnPointerExit(PointerEventData eventData) => owner?.SetSkipHover(false);
}

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reward 후보 선택 화면의 authoritative UI owner입니다.
///
/// 책임:
/// - Reward 카드 Hover / Click / 선택 상태
/// - 카드 크기/위치/Outline/아이콘 강조
/// - 선택 카드 내부 DESCRIPTION / EFFECTS / TAGS / 결정 버튼
/// - 아이템 획득 포기 버튼
/// - PACK 편집 진입 뒤 Selection Locked 오버레이
///
/// Reward business state는 BattleRewardFlow가 단독 소유합니다.
/// 이 클래스는 BattleHUD.pendingRewardIndex나 다른 Controller의 private field를 Reflection으로 읽지 않습니다.
/// RewardPrizeDrag는 BattleHUD가 아직 생성하므로 stable index marker로만 읽고 즉시 비활성화합니다.
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
    [SerializeField] private Color hoverPink = new(1f, 0.16f, 0.50f, 1f);

    [Header("Selection Locked")]
    [SerializeField] private Color lockedBack = new(0.006f, 0.008f, 0.012f, 0.90f);
    [SerializeField] private Color lockedStripe = new(0.88f, 0.90f, 0.94f, 0.16f);

    private BattleRunManager runManager;
    private BattleRewardFlow rewardFlow;
    private BattleHUD battleHud;
    private BattleEquipmentDetailPanelController equipmentDetailPanel;

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
    private Image skipLeftAccent;
    private Image skipRightAccent;
    private bool skipHovered;

    private RectTransform lockedOverlay;
    private CanvasGroup lockedGroup;
    private Text lockedTitle;
    private readonly List<RectTransform> lockedStripes = new();

    private bool detailPanelWasEnabled;
    private bool detailPanelSuppressed;
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
        public RewardPrizeDrag drag;
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
            DisableLegacyCardMotionAndDrag();
        }

        bool choice = phase == BattleRewardPhase.Choosing;
        bool packEdit = phase == BattleRewardPhase.PackEditing;

        SetEquipmentDetailPanelSuppressed(choice);

        if (choice)
        {
            SetLockedVisible(false);
            SetChoiceInteractable(true);
            HandleConfirmShortcut();
        }
        else if (packEdit)
        {
            hoveredRewardIndex = -1;
            SetChoiceInteractable(false);
            HideChoiceOnlyUi();
            SetLockedVisible(true);
        }
    }

    private void LateUpdate()
    {
        if (!IsReward() || rewardFlow == null)
            return;

        if (rewardFlow.Phase == BattleRewardPhase.Choosing)
        {
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
        else if (rewardFlow.Phase == BattleRewardPhase.PackEditing)
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
        if (battleHud == null)
            battleHud = FindFirstObjectByType<BattleHUD>(FindObjectsInactive.Include);
        if (equipmentDetailPanel == null)
            equipmentDetailPanel = FindFirstObjectByType<BattleEquipmentDetailPanelController>(FindObjectsInactive.Include);
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
            lockedStripes.Clear();
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

            RewardPrizeDrag drag = rect.GetComponent<RewardPrizeDrag>();
            int stableIndex = drag != null ? drag.RewardIndex : childIndex;
            if (drag != null)
                drag.enabled = false;

            RewardCardHover legacyHover = rect.GetComponent<RewardCardHover>();
            if (legacyHover != null)
                legacyHover.enabled = false;

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
                drag = drag,
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
        DisableLegacyCardMotionAndDrag();
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

    private void DisableLegacyCardMotionAndDrag()
    {
        if (rewardCardRoot == null)
            return;

        battleHud?.EndRewardDrag();
        for (int i = 0; i < cards.Count; i++)
        {
            CardRef card = cards[i];
            if (card == null || card.rect == null)
                continue;
            if (card.drag != null)
                card.drag.enabled = false;
            RewardCardHover hover = card.rect.GetComponent<RewardCardHover>();
            if (hover != null)
                hover.enabled = false;
        }
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
        rewardFlow.SelectChoice(index);
        choicePresentationDirty = true;
    }

    private void ConfirmSelectedReward()
    {
        if (!IsChoiceStage() || rewardFlow == null || !rewardFlow.CanConfirmChoice)
            return;

        if (rewardFlow.ConfirmSelectedChoice())
        {
            hoveredRewardIndex = -1;
            choicePresentationDirty = true;
            HideChoiceOnlyUi();
            SetChoiceInteractable(false);
            SetLockedVisible(true);
        }
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

        EnsureSkipDecor();
        ApplySkipDecor();

        BattleRewardSkipHoverRelay hover = rewardNoticeObject.GetComponent<BattleRewardSkipHoverRelay>();
        if (hover == null)
            hover = rewardNoticeObject.AddComponent<BattleRewardSkipHoverRelay>();
        hover.Configure(this);
    }

    private void EnsureSkipDecor()
    {
        if (rewardNoticeRect == null)
            return;

        if (skipLeftAccent == null)
        {
            RectTransform existing = rewardNoticeRect.Find("SkipAccentLeft") as RectTransform;
            if (existing != null)
                skipLeftAccent = existing.GetComponent<Image>();
            else
            {
                RectTransform rect = CreateRect(rewardNoticeRect, "SkipAccentLeft", new Vector2(12f, 60f));
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(-3f, 0f);
                rect.localRotation = Quaternion.Euler(0f, 0f, 7f);
                skipLeftAccent = rect.gameObject.AddComponent<Image>();
                skipLeftAccent.raycastTarget = false;
            }
        }

        if (skipRightAccent == null)
        {
            RectTransform existing = rewardNoticeRect.Find("SkipAccentRight") as RectTransform;
            if (existing != null)
                skipRightAccent = existing.GetComponent<Image>();
            else
            {
                RectTransform rect = CreateRect(rewardNoticeRect, "SkipAccentRight", new Vector2(76f, 6f));
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.anchoredPosition = new Vector2(5f, 2f);
                rect.localRotation = Quaternion.Euler(0f, 0f, -4f);
                skipRightAccent = rect.gameObject.AddComponent<Image>();
                skipRightAccent.raycastTarget = false;
            }
        }
    }

    private void ApplySkipDecor()
    {
        if (skipLeftAccent != null)
            skipLeftAccent.color = skipHovered ? hoverCyan : new Color(paper.r, paper.g, paper.b, 0.76f);
        if (skipRightAccent != null)
            skipRightAccent.color = skipHovered ? hoverPink : new Color(muted.r, muted.g, muted.b, 0.72f);
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
            choicePresentationDirty = true;
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

        for (int i = 0; i < 6; i++)
        {
            RectTransform stripe = CreateRect(lockedOverlay, $"StaticStripe_{i}", new Vector2(0f, 4f + i));
            stripe.anchorMin = new Vector2(0f, 0.5f);
            stripe.anchorMax = new Vector2(1f, 0.5f);
            stripe.offsetMin = Vector2.zero;
            stripe.offsetMax = Vector2.zero;
            Image image = stripe.gameObject.AddComponent<Image>();
            image.color = lockedStripe;
            image.raycastTarget = false;
            lockedStripes.Add(stripe);
        }

        lockedTitle = CreateText(lockedOverlay, "SELECTION LOCKED", 42, FontStyle.Bold, TextAnchor.MiddleCenter, paper);
        SetAnchors(lockedTitle.rectTransform, new Vector2(0.12f, 0.47f), new Vector2(0.88f, 0.63f));
        Text message = CreateText(lockedOverlay, "PACK EDIT IN PROGRESS", 14, FontStyle.Bold, TextAnchor.MiddleCenter, hoverCyan);
        SetAnchors(message.rectTransform, new Vector2(0.12f, 0.38f), new Vector2(0.88f, 0.47f));

        lockedOverlay.gameObject.SetActive(false);
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
        if (lockedOverlay == null || !lockedOverlay.gameObject.activeInHierarchy)
            return;

        float time = Time.unscaledTime;
        for (int i = 0; i < lockedStripes.Count; i++)
        {
            RectTransform stripe = lockedStripes[i];
            if (stripe == null)
                continue;
            float phase = Mathf.Repeat(time * (0.21f + i * 0.017f) + i * 0.143f, 1f);
            stripe.anchorMin = new Vector2(0f, phase);
            stripe.anchorMax = new Vector2(1f, phase);
            stripe.anchoredPosition = new Vector2(Mathf.Sin(time * 17f + i) * 9f, 0f);
        }

        if (lockedTitle != null)
        {
            float jitter = Mathf.Sin(time * 38f) * 1.2f;
            lockedTitle.rectTransform.anchoredPosition = new Vector2(jitter, 0f);
        }
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

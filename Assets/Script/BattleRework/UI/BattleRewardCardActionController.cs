using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reward 후보 선택 화면의 최종 카드 레이아웃 소유자입니다.
///
/// BattleHUD가 실제로 생성한 rewardCardRoot / rewardNoticePanel을 reflection으로 직접 받아 사용합니다.
/// 이름 기반 전역 FindRect에 의존하지 않으므로 다른 비활성/복제 UI를 잘못 잡지 않습니다.
///
/// 규칙:
/// - 선택 전 Hover만 임시 Preview 강조를 허용합니다.
/// - 클릭 선택(pendingRewardIndex) 이후에는 선택 카드만 Yellow 고정 강조를 유지합니다.
/// - 선택 이후 다른 카드 Hover는 Stroke / Color 상태를 절대 바꾸지 않습니다.
/// - 클릭 선택된 카드만 세로로 크게 펼칩니다.
/// - 비선택 카드는 실제 sizeDelta를 줄입니다. localScale을 사용하지 않습니다.
/// - 선택 카드 안에 DESCRIPTION / EFFECTS / TAGS / [결정]을 직접 배치합니다.
/// - Reward 후보 선택 중에는 우측 EquipmentDetailPanel을 완전히 비활성화합니다.
/// - Reward 후보 카드는 Drag하지 않습니다. RewardPrizeDrag는 index marker로만 유지하고 입력은 항상 비활성화합니다.
/// - Reward PACK 편집 중 하단 상태문구는 표시하지 않습니다. DONE / NEXT, TRASH, Hand만 상태를 전달합니다.
/// - RewardActiveDescriptionBar, focusedRewardName/focusedRewardStats는 Reward 선택 중 사용하지 않습니다.
/// - 기존 PlacementNotice 자체를 작은 [아이템 획득 포기하기] 버튼으로 재사용합니다.
/// - 구형 RewardSelectionPresentation은 선택 단계에서 잠시 꺼서 설명 바가 다시 살아나는 충돌을 막습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(60000)]
public sealed class BattleRewardCardActionController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static BattleRewardCardActionController instance;

    [Header("Card Layout")]
    [SerializeField] private Vector2 normalCardSize = new(330f, 340f);
    [SerializeField] private Vector2 inactiveCardSize = new(270f, 292f);
    [SerializeField] private Vector2 selectedCardSize = new(330f, 548f);
    [SerializeField] private float cardGap = 42f;
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

    private BattleRunManager runManager;
    private BattleHUD battleHud;
    private BattleInventoryInteractionController inventoryInteraction;
    private BattleRewardDecisionFlowController decisionFlow;
    private BattleRewardSelectionPresentationController legacySelectionPresentation;
    private BattleEquipmentDetailPanelController equipmentDetailPanel;

    private FieldInfo pendingRewardIndexField;
    private FieldInfo rewardCardRootField;
    private FieldInfo rewardNoticePanelField;
    private FieldInfo focusedRewardNameField;
    private FieldInfo focusedRewardStatsField;
    private FieldInfo rewardEditStatusField;
    private MethodInfo confirmSelectedRewardMethod;

    private RectTransform rewardCardRoot;
    private RectTransform rewardInner;
    private RectTransform rewardNoticeRect;
    private GameObject rewardNoticeObject;
    private Text focusedRewardName;
    private Text focusedRewardStats;

    private RectTransform inlineDetailRoot;
    private Text descriptionTitle;
    private Text descriptionText;
    private Text effectsTitle;
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

    private bool legacyWasEnabled;
    private bool legacySuppressed;
    private bool detailPanelWasEnabled;
    private bool detailPanelSuppressed;
    private bool decideBound;
    private bool skipBound;

    private readonly Dictionary<int, Vector2> animatedSizes = new();
    private readonly Dictionary<int, Vector2> animatedPositions = new();
    private float nextResolveTime;

    private sealed class CardRef
    {
        public RectTransform rect;
        public RewardPrizeDrag drag;
        public Image background;
        public Outline outline;
        public CanvasGroup group;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleRewardCardActionController>() != null)
            return;

        GameObject host = new("BattleRewardCardActionRuntime");
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
        CacheReflection();
        ResolveHudUi();
    }

    private void OnEnable()
    {
        if (instance != null && instance != this)
            return;

        ResolveReferences();
        CacheReflection();
        ResolveHudUi();
        nextResolveTime = 0f;
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
        Canvas.willRenderCanvases += HandleWillRenderCanvases;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
        RestoreLegacySelectionPresentation();
        RestoreEquipmentDetailPanel();
        HideGeneratedChoiceUi();
    }

    private void OnDestroy()
    {
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
        RestoreLegacySelectionPresentation();
        RestoreEquipmentDetailPanel();
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        ResolveReferences();
        CacheReflection();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveHudUi();
        }

        bool choice = IsChoiceStage();
        bool packEditing = IsRewardPackEditing();
        SetLegacySelectionPresentationSuppressed(choice);
        SetEquipmentDetailPanelSuppressed(choice);

        if (choice)
        {
            DisableLegacyRewardDrag();
        }
        else
        {
            HideGeneratedChoiceUi();
        }

        if (packEditing)
            HidePackEditStatus();
    }

    private void LateUpdate()
    {
        if (IsRewardPackEditing())
            HidePackEditStatus();
        ApplyFinalChoicePresentation(true);
    }

    private void HandleWillRenderCanvases()
    {
        if (!isActiveAndEnabled)
            return;

        if (IsRewardPackEditing())
            HidePackEditStatus();
        ApplyFinalChoicePresentation(false);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (battleHud == null)
            battleHud = FindFirstObjectByType<BattleHUD>(FindObjectsInactive.Include);
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        if (decisionFlow == null)
            decisionFlow = FindFirstObjectByType<BattleRewardDecisionFlowController>(FindObjectsInactive.Include);
        if (legacySelectionPresentation == null)
            legacySelectionPresentation = FindFirstObjectByType<BattleRewardSelectionPresentationController>(FindObjectsInactive.Include);
        if (equipmentDetailPanel == null)
            equipmentDetailPanel = FindFirstObjectByType<BattleEquipmentDetailPanelController>(FindObjectsInactive.Include);
    }

    private void CacheReflection()
    {
        if (battleHud != null)
        {
            System.Type hudType = typeof(BattleHUD);
            pendingRewardIndexField ??= hudType.GetField("pendingRewardIndex", PrivateInstance);
            rewardCardRootField ??= hudType.GetField("rewardCardRoot", PrivateInstance);
            rewardNoticePanelField ??= hudType.GetField("rewardNoticePanel", PrivateInstance);
            focusedRewardNameField ??= hudType.GetField("focusedRewardName", PrivateInstance);
            focusedRewardStatsField ??= hudType.GetField("focusedRewardStats", PrivateInstance);
        }

        if (inventoryInteraction != null && rewardEditStatusField == null)
            rewardEditStatusField = typeof(BattleInventoryInteractionController).GetField("rewardEditStatus", PrivateInstance);

        if (decisionFlow != null && confirmSelectedRewardMethod == null)
            confirmSelectedRewardMethod = typeof(BattleRewardDecisionFlowController)
                .GetMethod("ConfirmSelectedReward", PrivateInstance);
    }

    private void ResolveHudUi()
    {
        if (battleHud == null)
            return;

        RectTransform actualCardRoot = rewardCardRootField?.GetValue(battleHud) as RectTransform;
        if (actualCardRoot != null && actualCardRoot != rewardCardRoot)
        {
            rewardCardRoot = actualCardRoot;
            rewardInner = rewardCardRoot.parent as RectTransform;
            inlineDetailRoot = null;
            decideRoot = null;
            decideButton = null;
            decideBound = false;
            animatedSizes.Clear();
            animatedPositions.Clear();
        }

        GameObject actualNotice = rewardNoticePanelField?.GetValue(battleHud) as GameObject;
        if (actualNotice != null && actualNotice != rewardNoticeObject)
        {
            rewardNoticeObject = actualNotice;
            rewardNoticeRect = actualNotice.GetComponent<RectTransform>();
            skipButton = null;
            skipLabel = null;
            skipBack = null;
            skipLeftAccent = null;
            skipRightAccent = null;
            skipBound = false;
        }

        focusedRewardName = focusedRewardNameField?.GetValue(battleHud) as Text;
        focusedRewardStats = focusedRewardStatsField?.GetValue(battleHud) as Text;
    }

    private bool IsChoiceStage()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Reward &&
               (inventoryInteraction == null || !inventoryInteraction.IsRewardPackEditing);
    }

    private bool IsRewardPackEditing()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Reward &&
               inventoryInteraction != null &&
               inventoryInteraction.IsRewardPackEditing;
    }

    private int GetPendingRewardIndex()
    {
        if (battleHud == null || pendingRewardIndexField == null)
            return -1;
        object raw = pendingRewardIndexField.GetValue(battleHud);
        return raw is int value ? value : -1;
    }

    private void SetPendingRewardIndex(int value)
    {
        if (battleHud != null && pendingRewardIndexField != null)
            pendingRewardIndexField.SetValue(battleHud, value);
    }

    private void DisableLegacyRewardDrag()
    {
        if (rewardCardRoot != null)
        {
            RewardPrizeDrag[] drags = rewardCardRoot.GetComponentsInChildren<RewardPrizeDrag>(true);
            for (int i = 0; i < drags.Length; i++)
            {
                if (drags[i] != null && drags[i].enabled)
                    drags[i].enabled = false;
            }
        }

        // 이미 BeginDrag가 시작된 한 프레임까지 남아 있더라도 즉시 제거합니다.
        battleHud?.EndRewardDrag();
    }

    private void HidePackEditStatus()
    {
        if (inventoryInteraction == null || rewardEditStatusField == null)
            return;

        Text status = rewardEditStatusField.GetValue(inventoryInteraction) as Text;
        if (status == null)
            return;

        status.text = string.Empty;
        if (status.gameObject.activeSelf)
            status.gameObject.SetActive(false);
    }

    private void ApplyFinalChoicePresentation(bool advanceTween)
    {
        if (!IsChoiceStage())
            return;

        ResolveHudUi();
        if (rewardCardRoot == null || rewardInner == null)
            return;

        DisableLegacyRewardDrag();
        SetLegacySelectionPresentationSuppressed(true);
        SetEquipmentDetailPanelSuppressed(true);
        HideLegacyDescriptionUi();
        LayoutRewardCardArea();
        LayoutSkipButton();

        int selectedIndex = GetPendingRewardIndex();
        BattleEquipmentSO selectedEquipment = GetReward(selectedIndex);
        bool hasSelection = selectedEquipment != null;

        List<CardRef> cards = GetCards();
        LayoutCards(cards, selectedIndex, hasSelection, advanceTween);

        if (!hasSelection)
        {
            if (inlineDetailRoot != null)
                inlineDetailRoot.gameObject.SetActive(false);
            if (decideRoot != null)
                decideRoot.gameObject.SetActive(false);
            return;
        }

        CardRef selected = cards.Find(c => c != null && c.drag != null && c.drag.RewardIndex == selectedIndex);
        if (selected == null || selected.rect == null)
            return;

        EnsureInlineDetail(selected.rect);
        RefreshInlineDetail(selectedEquipment);
        EnsureDecisionButton(selected.rect);
        ApplySelectedCardVisual(selected);
    }

    private void LayoutRewardCardArea()
    {
        rewardCardRoot.anchorMin = new Vector2(0.055f, 0.17f);
        rewardCardRoot.anchorMax = new Vector2(0.945f, 0.84f);
        rewardCardRoot.offsetMin = Vector2.zero;
        rewardCardRoot.offsetMax = Vector2.zero;
        rewardCardRoot.pivot = new Vector2(0.5f, 0.5f);
    }

    private List<CardRef> GetCards()
    {
        List<CardRef> result = new();
        if (rewardCardRoot == null)
            return result;

        RewardPrizeDrag[] drags = rewardCardRoot.GetComponentsInChildren<RewardPrizeDrag>(true);
        for (int i = 0; i < drags.Length; i++)
        {
            RewardPrizeDrag drag = drags[i];
            RectTransform rect = drag != null ? drag.transform as RectTransform : null;
            if (rect == null)
                continue;

            // RewardPrizeDrag는 여러 기존 코드가 RewardIndex marker로 읽으므로 제거하지 않고 입력만 죽입니다.
            drag.enabled = false;

            result.Add(new CardRef
            {
                rect = rect,
                drag = drag,
                background = rect.GetComponent<Image>(),
                outline = rect.GetComponent<Outline>(),
                group = rect.GetComponent<CanvasGroup>()
            });
        }

        result.Sort((a, b) => a.drag.RewardIndex.CompareTo(b.drag.RewardIndex));
        return result;
    }

    private void LayoutCards(List<CardRef> cards, int selectedIndex, bool hasSelection, bool advanceTween)
    {
        if (cards == null || cards.Count == 0)
            return;

        float totalWidth = 0f;
        Vector2[] targetSizes = new Vector2[cards.Count];
        for (int i = 0; i < cards.Count; i++)
        {
            bool selected = hasSelection && cards[i].drag.RewardIndex == selectedIndex;
            targetSizes[i] = !hasSelection
                ? normalCardSize
                : selected ? selectedCardSize : inactiveCardSize;
            totalWidth += targetSizes[i].x;
        }
        totalWidth += Mathf.Max(0, cards.Count - 1) * cardGap;

        float cursor = -totalWidth * 0.5f;
        float blend = advanceTween
            ? 1f - Mathf.Exp(-Mathf.Max(1f, tweenSharpness) * Time.unscaledDeltaTime)
            : 0f;

        for (int i = 0; i < cards.Count; i++)
        {
            CardRef card = cards[i];
            if (card == null || card.rect == null || card.drag == null)
                continue;

            Vector2 targetSize = targetSizes[i];
            Vector2 targetPosition = new(cursor + targetSize.x * 0.5f, 0f);
            cursor += targetSize.x + cardGap;

            int id = card.rect.GetInstanceID();
            if (!animatedSizes.TryGetValue(id, out Vector2 currentSize))
                currentSize = normalCardSize;
            if (!animatedPositions.TryGetValue(id, out Vector2 currentPosition))
                currentPosition = card.rect.anchoredPosition;

            if (advanceTween)
            {
                currentSize = Vector2.Lerp(currentSize, targetSize, blend);
                currentPosition = Vector2.Lerp(currentPosition, targetPosition, blend);
                animatedSizes[id] = currentSize;
                animatedPositions[id] = currentPosition;
            }

            card.rect.anchorMin = card.rect.anchorMax = new Vector2(0.5f, 0.5f);
            card.rect.pivot = new Vector2(0.5f, 0.5f);
            card.rect.sizeDelta = currentSize;
            card.rect.anchoredPosition = currentPosition;
            card.rect.localScale = Vector3.one;
            card.rect.localRotation = Quaternion.identity;

            bool isSelected = hasSelection && card.drag.RewardIndex == selectedIndex;
            ConfigureBuiltInCardContent(card.rect, GetReward(card.drag.RewardIndex), isSelected);

            if (card.group != null)
                card.group.alpha = !hasSelection || isSelected ? 1f : 0.48f;

            if (card.background != null)
                card.background.color = isSelected ? selectedCard : neutralCard;

            if (hasSelection && !isSelected)
            {
                if (card.outline != null)
                    card.outline.enabled = false;

                Image inactiveIcon = card.rect.Find("PrizeIcon")?.GetComponent<Image>();
                if (inactiveIcon != null)
                    inactiveIcon.color = inactiveIconColor;
            }
        }
    }

    private void ConfigureBuiltInCardContent(RectTransform card, BattleEquipmentSO equipment, bool selected)
    {
        if (card == null || equipment == null)
            return;

        RectTransform icon = card.Find("PrizeIcon") as RectTransform;
        if (icon != null)
        {
            icon.anchorMin = icon.anchorMax = selected
                ? new Vector2(0.5f, 0.80f)
                : new Vector2(0.5f, 0.67f);
            icon.anchoredPosition = Vector2.zero;
            icon.sizeDelta = selected ? new Vector2(126f, 126f) : new Vector2(150f, 150f);
        }

        string displayName = equipment.GetDisplayName();
        string rarity = equipment.rarity.ToString().ToUpperInvariant();
        string type = equipment.type.ToString().ToUpperInvariant();

        Text[] texts = card.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null ||
                IsChildOf(text.transform, inlineDetailRoot) ||
                IsChildOf(text.transform, decideRoot))
                continue;

            string value = text.text ?? string.Empty;
            bool action = value.Contains("CLICK") || value.Contains("DRAG");
            bool itemName = value == displayName;
            bool itemType = value == type;
            bool itemRarity = value == rarity || (value.StartsWith("LV.") && value.Contains(rarity));

            if (action)
            {
                text.gameObject.SetActive(!selected);
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
            GameObject go = new("RewardSelectedInlineDetail");
            inlineDetailRoot = go.AddComponent<RectTransform>();

            descriptionTitle = CreateText(inlineDetailRoot, "DESCRIPTION", 10, FontStyle.Bold, TextAnchor.MiddleLeft, muted);
            SetAnchors(descriptionTitle.rectTransform, new Vector2(0f, 0.86f), new Vector2(1f, 1f));

            descriptionText = CreateText(inlineDetailRoot, string.Empty, 10, FontStyle.Normal, TextAnchor.UpperLeft, paper);
            descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descriptionText.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(descriptionText.rectTransform, new Vector2(0f, 0.58f), new Vector2(1f, 0.86f));

            effectsTitle = CreateText(inlineDetailRoot, "EFFECTS", 10, FontStyle.Bold, TextAnchor.MiddleLeft, muted);
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
                : equipment.shootingData != null
                    ? "Manual weapon."
                    : "Equipment item.";
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
            GameObject go = new("RewardInlineDecide");
            decideRoot = go.AddComponent<RectTransform>();
            Image back = go.AddComponent<Image>();
            back.color = ink;
            back.raycastTarget = true;

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = selectAccent;
            outline.effectDistance = new Vector2(4f, -4f);

            Text label = CreateText(decideRoot, "결정", 15, FontStyle.Bold, TextAnchor.MiddleCenter, paper);
            Stretch(label.rectTransform);

            decideButton = go.AddComponent<Button>();
            decideButton.targetGraphic = back;
        }

        if (decideButton != null && !decideBound)
        {
            decideButton.onClick.AddListener(ConfirmSelectedReward);
            decideBound = true;
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

        // DecisionFlow가 만든 구형 화면 중앙 버튼은 사용하지 않습니다.
        RectTransform oldDecide = FindChildByName(rewardInner, "RewardDecisionConfirm");
        if (oldDecide != null && oldDecide != decideRoot)
            oldDecide.gameObject.SetActive(false);
    }

    private void ConfirmSelectedReward()
    {
        if (!IsChoiceStage() || GetPendingRewardIndex() < 0)
            return;

        ResolveReferences();
        CacheReflection();
        if (decisionFlow != null && confirmSelectedRewardMethod != null)
            confirmSelectedRewardMethod.Invoke(decisionFlow, null);
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
            skipBack.color = skipHovered
                ? new Color(0.095f, 0.10f, 0.115f, 1f)
                : ink;
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
        if (!skipBound)
        {
            skipButton.onClick.AddListener(SkipReward);
            skipBound = true;
        }

        EnsureSkipDecor();
        ApplySkipDecor();

        BattleRewardSkipHoverRelay hover = rewardNoticeObject.GetComponent<BattleRewardSkipHoverRelay>();
        if (hover == null)
            hover = rewardNoticeObject.AddComponent<BattleRewardSkipHoverRelay>();
        hover.Configure(this);

        // 예전 별도 RewardDecisionSkip 버튼이 남아 있으면 중복 표시하지 않습니다.
        RectTransform oldSkip = FindChildByName(rewardNoticeRect, "RewardDecisionSkip");
        if (oldSkip != null)
            oldSkip.gameObject.SetActive(false);
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
                GameObject go = new("SkipAccentLeft");
                go.transform.SetParent(rewardNoticeRect, false);
                RectTransform rect = go.AddComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(12f, 60f);
                rect.anchoredPosition = new Vector2(-3f, 0f);
                rect.localRotation = Quaternion.Euler(0f, 0f, 7f);
                skipLeftAccent = go.AddComponent<Image>();
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
                GameObject go = new("SkipAccentRight");
                go.transform.SetParent(rewardNoticeRect, false);
                RectTransform rect = go.AddComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.sizeDelta = new Vector2(76f, 6f);
                rect.anchoredPosition = new Vector2(5f, 2f);
                rect.localRotation = Quaternion.Euler(0f, 0f, -4f);
                skipRightAccent = go.AddComponent<Image>();
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
        skipHovered = hovered;
        ApplySkipDecor();
    }

    private void SkipReward()
    {
        if (!IsChoiceStage() || runManager == null)
            return;

        SetPendingRewardIndex(-1);
        runManager.SkipReward();
    }

    private void ApplySelectedCardVisual(CardRef selected)
    {
        if (selected == null || selected.rect == null)
            return;

        if (selected.group != null)
            selected.group.alpha = 1f;

        if (selected.background != null)
            selected.background.color = selectedCard;

        if (selected.outline == null)
            selected.outline = selected.rect.gameObject.AddComponent<Outline>();
        selected.outline.enabled = true;
        selected.outline.effectColor = selectAccent;
        selected.outline.effectDistance = new Vector2(5f, -5f);

        Image icon = selected.rect.Find("PrizeIcon")?.GetComponent<Image>();
        if (icon != null)
        {
            icon.material = null;
            icon.color = Color.white;
        }
    }

    private void HideLegacyDescriptionUi()
    {
        if (focusedRewardName != null)
            focusedRewardName.gameObject.SetActive(false);
        if (focusedRewardStats != null)
            focusedRewardStats.gameObject.SetActive(false);

        if (rewardInner != null)
        {
            RectTransform descriptionBar = rewardInner.Find("RewardActiveDescriptionBar") as RectTransform;
            if (descriptionBar != null)
                descriptionBar.gameObject.SetActive(false);
        }
    }

    private void SetLegacySelectionPresentationSuppressed(bool suppress)
    {
        if (legacySelectionPresentation == null)
            return;

        if (suppress)
        {
            if (!legacySuppressed)
            {
                legacyWasEnabled = legacySelectionPresentation.enabled;
                legacySuppressed = true;
            }
            if (legacySelectionPresentation.enabled)
                legacySelectionPresentation.enabled = false;
        }
        else
        {
            RestoreLegacySelectionPresentation();
        }
    }

    private void RestoreLegacySelectionPresentation()
    {
        if (!legacySuppressed || legacySelectionPresentation == null)
            return;

        legacySelectionPresentation.enabled = legacyWasEnabled;
        legacySuppressed = false;
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

    private void HideGeneratedChoiceUi()
    {
        if (inlineDetailRoot != null)
            inlineDetailRoot.gameObject.SetActive(false);
        if (decideRoot != null)
            decideRoot.gameObject.SetActive(false);
        skipHovered = false;
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
            Transform child = root.GetChild(i);
            Text text = child.GetComponent<Text>();
            if (text != null)
                return text;
        }
        return root.GetComponentInChildren<Text>(true);
    }

    private static Text CreateText(Transform parent, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        GameObject go = new("Text");
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        Text text = go.AddComponent<Text>();
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
        if (rect == null)
            return;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rect)
    {
        if (rect == null)
            return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

internal sealed class BattleRewardSkipHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private BattleRewardCardActionController owner;

    public void Configure(BattleRewardCardActionController controller)
    {
        owner = controller;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        owner?.SetSkipHover(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.SetSkipHover(false);
    }
}

using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward 후보 선택 화면의 최종 레이아웃 소유자입니다.
///
/// - Runtime host로 항상 존재합니다. Scene auto installer에 의존하지 않습니다.
/// - Hover는 Transform을 바꾸지 않습니다.
/// - 클릭 선택된 카드만 세로로 길게 펼치고, 나머지 카드는 작게 줄입니다.
/// - 설명/효과/TAGS/[결정]은 선택 카드 내부에만 표시합니다.
/// - 기존 RewardActiveDescriptionBar 및 BattleHUD의 하단 상세 텍스트는 사용하지 않습니다.
/// - 기존 PlacementNotice 자체를 작은 [아이템 획득 포기하기] 버튼으로 재사용합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50000)]
public sealed class BattleRewardCardActionController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static BattleRewardCardActionController instance;

    [Header("Cards")]
    [SerializeField] private Vector2 normalCardSize = new(330f, 340f);
    [SerializeField] private Vector2 selectedCardSize = new(330f, 540f);
    [SerializeField, Range(0.65f, 0.95f)] private float inactiveCardScale = 0.80f;
    [SerializeField] private float cardGap = 44f;
    [SerializeField, Min(1f)] private float tweenSharpness = 17f;

    [Header("Actions")]
    [SerializeField] private Vector2 decideSize = new(196f, 44f);
    [SerializeField] private Vector2 skipSize = new(286f, 44f);

    [Header("Theme")]
    [SerializeField] private Color ink = new(0.018f, 0.019f, 0.022f, 0.99f);
    [SerializeField] private Color paper = new(0.96f, 0.96f, 0.96f, 1f);
    [SerializeField] private Color muted = new(0.60f, 0.61f, 0.64f, 1f);
    [SerializeField] private Color selectedAccent = new(1f, 0.79f, 0.08f, 1f);
    [SerializeField] private Color skipBackColor = new(0.24f, 0.24f, 0.25f, 0.98f);

    private BattleRunManager runManager;
    private BattleHUD battleHud;
    private BattleInventoryInteractionController inventoryInteraction;
    private BattleRewardDecisionFlowController decisionFlow;
    private BattleRewardSelectionPresentationController legacySelectionPresentation;

    private FieldInfo pendingRewardIndexField;
    private FieldInfo focusedRewardNameField;
    private FieldInfo focusedRewardStatsField;
    private MethodInfo confirmRewardMethod;

    private RectTransform rewardInner;
    private RectTransform prizeChoices;
    private RectTransform placementNotice;
    private Image placementNoticeImage;
    private Text placementNoticeText;
    private Button skipButton;
    private bool skipListenerBound;

    private RectTransform decideRoot;
    private Button decideButton;
    private bool fallbackDecideListenerBound;

    private RectTransform detailRoot;
    private Text detailDescriptionTitle;
    private Text detailDescription;
    private Text detailEffectsTitle;
    private Text detailEffects;
    private Text detailTags;

    private bool legacyDisabledByThis;
    private float nextResolveTime;

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
        ResolveUi();
    }

    private void OnEnable()
    {
        if (instance != null && instance != this)
            return;

        ResolveReferences();
        ResolveUi();
        nextResolveTime = 0f;
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
        Canvas.willRenderCanvases += HandleWillRenderCanvases;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
        RestoreLegacyController();
        HideInlineUi();
    }

    private void OnDestroy()
    {
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
        RestoreLegacyController();
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        ResolveReferences();
        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveUi();
        }

        bool choice = IsChoiceStage();
        SetLegacySelectionPresentationSuppressed(choice);
        if (choice)
            HideLegacyDetails();
        else
            HideInlineUi();
    }

    private void LateUpdate()
    {
        ApplyFinalPresentation(true);
    }

    private void HandleWillRenderCanvases()
    {
        if (isActiveAndEnabled)
            ApplyFinalPresentation(false);
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

        if (battleHud != null)
        {
            pendingRewardIndexField ??= typeof(BattleHUD).GetField("pendingRewardIndex", PrivateInstance);
            focusedRewardNameField ??= typeof(BattleHUD).GetField("focusedRewardName", PrivateInstance);
            focusedRewardStatsField ??= typeof(BattleHUD).GetField("focusedRewardStats", PrivateInstance);
        }

        if (decisionFlow != null && confirmRewardMethod == null)
            confirmRewardMethod = typeof(BattleRewardDecisionFlowController).GetMethod("ConfirmSelectedReward", PrivateInstance);
    }

    private void ResolveUi()
    {
        RectTransform rewardScreen = FindRect("PrizeSelectionScreen");
        rewardInner = rewardScreen != null ? rewardScreen.Find("ScreenInner") as RectTransform : null;
        prizeChoices = rewardInner != null ? rewardInner.Find("PrizeChoices") as RectTransform : FindRect("PrizeChoices");
        placementNotice = rewardInner != null ? rewardInner.Find("PlacementNotice") as RectTransform : FindRect("PlacementNotice");

        if (placementNotice != null)
        {
            placementNoticeImage = placementNotice.GetComponent<Image>();
            placementNoticeText = ResolvePlacementNoticeText();
            EnsureSkipButtonOnPlacementNotice();
        }

        RectTransform resolvedDecide = FindRect("RewardDecisionConfirm");
        if (resolvedDecide != null)
        {
            decideRoot = resolvedDecide;
            decideButton = decideRoot.GetComponent<Button>();
        }
    }

    private void SetLegacySelectionPresentationSuppressed(bool suppress)
    {
        if (legacySelectionPresentation == null)
            return;

        if (suppress)
        {
            if (legacySelectionPresentation.enabled)
            {
                legacySelectionPresentation.enabled = false;
                legacyDisabledByThis = true;
            }
        }
        else
        {
            RestoreLegacyController();
        }
    }

    private void RestoreLegacyController()
    {
        if (legacySelectionPresentation != null && legacyDisabledByThis)
            legacySelectionPresentation.enabled = true;
        legacyDisabledByThis = false;
    }

    private void ApplyFinalPresentation(bool animate)
    {
        if (!IsChoiceStage())
            return;

        ResolveUi();
        HideLegacyDetails();
        ConfigureSkipButton();

        int selectedIndex = GetPendingRewardIndex();
        BattleEquipmentSO selectedEquipment = GetReward(selectedIndex);
        bool hasSelection = selectedEquipment != null;

        LayoutCards(selectedIndex, hasSelection, animate);

        RectTransform selectedCard = hasSelection ? FindRewardCard(selectedIndex) : null;
        if (selectedCard == null)
        {
            if (detailRoot != null)
                detailRoot.gameObject.SetActive(false);
            if (decideRoot != null)
                decideRoot.gameObject.SetActive(false);
            return;
        }

        EnsureInlineDetail(selectedCard);
        LayoutSelectedCardContent(selectedCard, selectedEquipment);
        RefreshInlineDetail(selectedEquipment);
        EnsureDecisionButton(selectedCard);
        ApplySelectedAccent(selectedCard);
    }

    private void LayoutCards(int selectedIndex, bool hasSelection, bool animate)
    {
        if (prizeChoices == null)
            return;

        RewardPrizeDrag[] cards = prizeChoices.GetComponentsInChildren<RewardPrizeDrag>(true);
        if (cards.Length == 0)
            return;

        float width = normalCardSize.x;
        float total = cards.Length * width + Mathf.Max(0, cards.Length - 1) * cardGap;
        float start = -total * 0.5f + width * 0.5f;
        float blend = animate
            ? 1f - Mathf.Exp(-Mathf.Max(1f, tweenSharpness) * Time.unscaledDeltaTime)
            : 1f;

        for (int i = 0; i < cards.Length; i++)
        {
            RewardPrizeDrag drag = cards[i];
            RectTransform card = drag != null ? drag.transform as RectTransform : null;
            if (card == null)
                continue;

            int index = Mathf.Clamp(drag.RewardIndex, 0, cards.Length - 1);
            bool selected = hasSelection && drag.RewardIndex == selectedIndex;
            Vector2 targetSize = selected ? selectedCardSize : normalCardSize;
            float targetScale = !hasSelection || selected ? 1f : inactiveCardScale;
            Vector2 targetPosition = new(start + index * (width + cardGap), 0f);

            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = Vector2.Lerp(card.sizeDelta, targetSize, blend);
            card.localScale = Vector3.Lerp(card.localScale, Vector3.one * targetScale, blend);
            card.anchoredPosition = Vector2.Lerp(card.anchoredPosition, targetPosition, blend);
            card.localRotation = Quaternion.identity;

            if (!selected)
                LayoutNormalCardContent(card, GetReward(drag.RewardIndex));
        }
    }

    private void LayoutNormalCardContent(RectTransform card, BattleEquipmentSO equipment)
    {
        if (card == null)
            return;

        RectTransform icon = card.Find("PrizeIcon") as RectTransform;
        if (icon != null)
        {
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.67f);
            icon.anchoredPosition = Vector2.zero;
            icon.sizeDelta = new Vector2(150f, 150f);
        }

        if (equipment == null)
            return;

        ConfigureBaseTexts(card, equipment, false);
    }

    private void LayoutSelectedCardContent(RectTransform card, BattleEquipmentSO equipment)
    {
        RectTransform icon = card.Find("PrizeIcon") as RectTransform;
        if (icon != null)
        {
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.815f);
            icon.anchoredPosition = Vector2.zero;
            icon.sizeDelta = new Vector2(126f, 126f);
        }

        ConfigureBaseTexts(card, equipment, true);
    }

    private void ConfigureBaseTexts(RectTransform card, BattleEquipmentSO equipment, bool selected)
    {
        if (card == null || equipment == null)
            return;

        string displayName = equipment.GetDisplayName();
        string rarity = equipment.rarity.ToString().ToUpperInvariant();
        string type = equipment.type.ToString().ToUpperInvariant();

        Text[] texts = card.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null ||
                IsChildOf(text.transform, detailRoot) ||
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
                if (!selected)
                    SetAnchors(text.rectTransform, new Vector2(0.08f, 0.025f), new Vector2(0.92f, 0.12f));
                continue;
            }

            text.gameObject.SetActive(true);
            if (itemRarity)
            {
                SetAnchors(text.rectTransform,
                    selected ? new Vector2(0.08f, 0.705f) : new Vector2(0.08f, 0.39f),
                    selected ? new Vector2(0.92f, 0.75f) : new Vector2(0.92f, 0.47f));
            }
            else if (itemName)
            {
                SetAnchors(text.rectTransform,
                    selected ? new Vector2(0.06f, 0.645f) : new Vector2(0.06f, 0.22f),
                    selected ? new Vector2(0.94f, 0.705f) : new Vector2(0.94f, 0.39f));
                if (selected)
                    text.fontSize = Mathf.Max(text.fontSize, 16);
            }
            else if (itemType)
            {
                SetAnchors(text.rectTransform,
                    selected ? new Vector2(0.08f, 0.595f) : new Vector2(0.08f, 0.14f),
                    selected ? new Vector2(0.92f, 0.645f) : new Vector2(0.92f, 0.22f));
            }
        }
    }

    private void EnsureInlineDetail(RectTransform selectedCard)
    {
        if (selectedCard == null)
            return;

        if (detailRoot == null)
            detailRoot = FindRect("RewardSelectedVerticalDetail");

        if (detailRoot == null)
        {
            GameObject root = new("RewardSelectedVerticalDetail");
            root.transform.SetParent(selectedCard, false);
            detailRoot = root.AddComponent<RectTransform>();

            detailDescriptionTitle = CreateText(detailRoot, "DescriptionTitle", "DESCRIPTION", 10, FontStyle.Bold, TextAnchor.MiddleLeft, muted);
            SetAnchors(detailDescriptionTitle.rectTransform, new Vector2(0f, 0.84f), new Vector2(1f, 1f));

            detailDescription = CreateText(detailRoot, "Description", string.Empty, 11, FontStyle.Normal, TextAnchor.UpperLeft, paper);
            detailDescription.horizontalOverflow = HorizontalWrapMode.Wrap;
            detailDescription.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(detailDescription.rectTransform, new Vector2(0f, 0.58f), new Vector2(1f, 0.84f));

            detailEffectsTitle = CreateText(detailRoot, "EffectsTitle", "EFFECTS", 10, FontStyle.Bold, TextAnchor.MiddleLeft, muted);
            SetAnchors(detailEffectsTitle.rectTransform, new Vector2(0f, 0.48f), new Vector2(1f, 0.58f));

            detailEffects = CreateText(detailRoot, "Effects", string.Empty, 10, FontStyle.Bold, TextAnchor.UpperLeft, paper);
            detailEffects.horizontalOverflow = HorizontalWrapMode.Wrap;
            detailEffects.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(detailEffects.rectTransform, new Vector2(0f, 0.15f), new Vector2(1f, 0.48f));

            detailTags = CreateText(detailRoot, "Tags", string.Empty, 9, FontStyle.Bold, TextAnchor.MiddleLeft, muted);
            detailTags.horizontalOverflow = HorizontalWrapMode.Wrap;
            detailTags.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(detailTags.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.15f));
        }

        if (detailRoot.parent != selectedCard)
            detailRoot.SetParent(selectedCard, false);

        detailRoot.anchorMin = new Vector2(0.08f, 0.18f);
        detailRoot.anchorMax = new Vector2(0.92f, 0.56f);
        detailRoot.offsetMin = Vector2.zero;
        detailRoot.offsetMax = Vector2.zero;
        detailRoot.localScale = Vector3.one;
        detailRoot.localRotation = Quaternion.identity;
        detailRoot.gameObject.SetActive(true);
    }

    private void RefreshInlineDetail(BattleEquipmentSO equipment)
    {
        if (equipment == null || detailRoot == null)
            return;

        if (detailDescription != null)
        {
            detailDescription.text = !string.IsNullOrWhiteSpace(equipment.description)
                ? equipment.description.Trim()
                : equipment.shootingData != null
                    ? "Manual weapon. Fires using its configured shooting pattern."
                    : "Equipment item. Its effects apply while it is active in the loadout.";
        }

        if (detailEffects != null)
        {
            detailEffects.text =
                $"• DAMAGE        ×{equipment.damageMultiplier:0.00}\n" +
                $"• MOVE SPEED    ×{equipment.moveSpeedMultiplier:0.00}\n" +
                $"• RANGE         ×{equipment.rangeMultiplier:0.00}";
        }

        if (detailTags != null)
            detailTags.text = BuildTagLine(equipment);
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

    private void EnsureDecisionButton(RectTransform selectedCard)
    {
        if (selectedCard == null)
            return;

        if (decideRoot == null)
            decideRoot = FindRect("RewardDecisionConfirm");

        if (decideRoot == null)
        {
            GameObject go = new("RewardDecisionConfirm");
            go.transform.SetParent(selectedCard, false);
            decideRoot = go.AddComponent<RectTransform>();

            Image back = go.AddComponent<Image>();
            back.color = ink;
            back.raycastTarget = true;

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = selectedAccent;
            outline.effectDistance = new Vector2(4f, -4f);

            Text label = CreateText(decideRoot, "Text", "결정", 15, FontStyle.Bold, TextAnchor.MiddleCenter, paper);
            Stretch(label.rectTransform);

            decideButton = go.AddComponent<Button>();
            decideButton.targetGraphic = back;
            decideButton.onClick.AddListener(ConfirmRewardFallback);
            fallbackDecideListenerBound = true;
        }
        else
        {
            decideButton ??= decideRoot.GetComponent<Button>();
        }

        if (decideRoot.parent != selectedCard)
            decideRoot.SetParent(selectedCard, false);

        decideRoot.anchorMin = decideRoot.anchorMax = new Vector2(0.5f, 0.075f);
        decideRoot.pivot = new Vector2(0.5f, 0.5f);
        decideRoot.sizeDelta = decideSize;
        decideRoot.anchoredPosition = Vector2.zero;
        decideRoot.localScale = Vector3.one;
        decideRoot.localRotation = Quaternion.identity;
        decideRoot.gameObject.SetActive(true);
        decideRoot.SetAsLastSibling();

        Image buttonBack = decideRoot.GetComponent<Image>();
        if (buttonBack != null)
            buttonBack.color = ink;

        Text buttonText = decideRoot.GetComponentInChildren<Text>(true);
        if (buttonText != null)
        {
            buttonText.text = "결정";
            buttonText.color = paper;
        }
    }

    private void ConfirmRewardFallback()
    {
        ResolveReferences();
        if (decisionFlow != null && confirmRewardMethod != null)
            confirmRewardMethod.Invoke(decisionFlow, null);
    }

    private void EnsureSkipButtonOnPlacementNotice()
    {
        if (placementNotice == null)
            return;

        skipButton = placementNotice.GetComponent<Button>();
        if (skipButton == null)
            skipButton = placementNotice.gameObject.AddComponent<Button>();

        if (placementNoticeImage != null)
            skipButton.targetGraphic = placementNoticeImage;

        if (!skipListenerBound)
        {
            skipButton.onClick.AddListener(SkipReward);
            skipListenerBound = true;
        }

        RectTransform staleNestedSkip = placementNotice.Find("RewardDecisionSkip") as RectTransform;
        if (staleNestedSkip != null)
            staleNestedSkip.gameObject.SetActive(false);

        RectTransform globalStaleSkip = FindRect("RewardDecisionSkip");
        if (globalStaleSkip != null && globalStaleSkip != placementNotice)
            globalStaleSkip.gameObject.SetActive(false);
    }

    private void ConfigureSkipButton()
    {
        if (placementNotice == null)
            return;

        placementNotice.anchorMin = placementNotice.anchorMax = new Vector2(0.5f, 0.065f);
        placementNotice.pivot = new Vector2(0.5f, 0.5f);
        placementNotice.sizeDelta = skipSize;
        placementNotice.anchoredPosition = Vector2.zero;
        placementNotice.localScale = Vector3.one;
        placementNotice.localRotation = Quaternion.identity;
        placementNotice.gameObject.SetActive(true);

        if (placementNoticeImage != null)
            placementNoticeImage.color = skipBackColor;

        if (placementNoticeText == null)
            placementNoticeText = ResolvePlacementNoticeText();
        if (placementNoticeText != null)
        {
            placementNoticeText.gameObject.SetActive(true);
            placementNoticeText.text = "아이템 획득 포기하기";
            placementNoticeText.fontSize = 14;
            placementNoticeText.fontStyle = FontStyle.Bold;
            placementNoticeText.alignment = TextAnchor.MiddleCenter;
            placementNoticeText.color = paper;
            Stretch(placementNoticeText.rectTransform);
        }

        if (skipButton != null)
            skipButton.interactable = !BattlePauseController.IsPaused;
    }

    private Text ResolvePlacementNoticeText()
    {
        if (placementNotice == null)
            return null;

        Text[] texts = placementNotice.GetComponentsInChildren<Text>(true);
        return texts.Length > 0 ? texts[0] : null;
    }

    private void HideLegacyDetails()
    {
        RectTransform descriptionBar = rewardInner != null
            ? rewardInner.Find("RewardActiveDescriptionBar") as RectTransform
            : FindRect("RewardActiveDescriptionBar");
        if (descriptionBar != null && descriptionBar.gameObject.activeSelf)
            descriptionBar.gameObject.SetActive(false);

        RectTransform oldInline = FindRect("RewardSelectedInlineDetail");
        if (oldInline != null && oldInline != detailRoot && oldInline.gameObject.activeSelf)
            oldInline.gameObject.SetActive(false);

        if (battleHud != null)
        {
            Text legacyName = focusedRewardNameField?.GetValue(battleHud) as Text;
            Text legacyStats = focusedRewardStatsField?.GetValue(battleHud) as Text;
            if (legacyName != null && legacyName.gameObject.activeSelf)
                legacyName.gameObject.SetActive(false);
            if (legacyStats != null && legacyStats.gameObject.activeSelf)
                legacyStats.gameObject.SetActive(false);
        }

        RectTransform externalDetail = FindRect("EquipmentDetailPanel");
        if (externalDetail != null)
        {
            CanvasGroup group = externalDetail.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
        }
    }

    private void ApplySelectedAccent(RectTransform selectedCard)
    {
        Outline outline = selectedCard != null ? selectedCard.GetComponent<Outline>() : null;
        if (outline != null)
        {
            outline.enabled = true;
            outline.effectColor = selectedAccent;
            outline.effectDistance = new Vector2(5f, -5f);
        }

        Image icon = selectedCard != null ? selectedCard.Find("PrizeIcon")?.GetComponent<Image>() : null;
        if (icon != null)
        {
            icon.material = null;
            icon.color = Color.white;
        }
    }

    private void SkipReward()
    {
        if (!IsChoiceStage() || runManager == null)
            return;

        if (battleHud != null && pendingRewardIndexField != null)
            pendingRewardIndexField.SetValue(battleHud, -1);

        HideInlineUi();
        runManager.SkipReward();
    }

    private void HideInlineUi()
    {
        if (detailRoot != null && detailRoot.gameObject.activeSelf)
            detailRoot.gameObject.SetActive(false);
        if (decideRoot != null && decideRoot.gameObject.activeSelf)
            decideRoot.gameObject.SetActive(false);
    }

    private int GetPendingRewardIndex()
    {
        if (battleHud == null || pendingRewardIndexField == null)
            return -1;
        object raw = pendingRewardIndexField.GetValue(battleHud);
        return raw is int value ? value : -1;
    }

    private BattleEquipmentSO GetReward(int index)
    {
        if (runManager == null || index < 0 || index >= runManager.CurrentRewardChoices.Count)
            return null;
        return runManager.CurrentRewardChoices[index];
    }

    private RectTransform FindRewardCard(int rewardIndex)
    {
        if (prizeChoices == null || rewardIndex < 0)
            return null;

        RewardPrizeDrag[] cards = prizeChoices.GetComponentsInChildren<RewardPrizeDrag>(true);
        for (int i = 0; i < cards.Length; i++)
        {
            RewardPrizeDrag drag = cards[i];
            if (drag != null && drag.RewardIndex == rewardIndex)
                return drag.transform as RectTransform;
        }
        return null;
    }

    private bool IsChoiceStage()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Reward &&
               (inventoryInteraction == null || !inventoryInteraction.IsRewardPackEditing);
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

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = Object.FindObjectsByType<RectTransform>(
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

    private static Text CreateText(
        Transform parent,
        string objectName,
        string value,
        int fontSize,
        FontStyle fontStyle,
        TextAnchor alignment,
        Color color)
    {
        GameObject go = new(objectName);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        Text text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
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

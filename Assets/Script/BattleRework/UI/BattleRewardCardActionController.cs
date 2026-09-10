using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Reward 선택 단계의 카드 액션/상세 표시를 최종 렌더 단계에서 관리합니다.
///
/// 규칙:
/// - Hover는 카드 Transform을 절대 변경하지 않습니다.
/// - 클릭되어 pendingRewardIndex가 된 카드만 위/아래로 길게 펼칩니다.
/// - 선택 카드 내부에 설명 / 효과 목록 / 태그 / [결정]을 세로로 배치합니다.
/// - 비선택 카드는 작게 줄여 선택 카드의 정보 밀도를 확보합니다.
/// - 하단의 기존 설명 바는 사용하지 않습니다.
/// - PlacementNotice는 안내 바가 아니라 작은 [아이템 획득 포기하기] 버튼의 자리로만 사용합니다.
///
/// 카드 크기 변화는 pendingRewardIndex에만 의존하므로 PointerEnter/Exit 경계 진동이 없습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(44100)]
public sealed class BattleRewardCardActionController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("Selected Card")]
    [SerializeField] private float selectedHeightMultiplier = 1.56f;
    [SerializeField, Range(0.98f, 1.05f)] private float selectedCardScale = 1f;
    [SerializeField, Range(0.68f, 0.98f)] private float inactiveCardScale = 0.82f;
    [SerializeField, Min(1f)] private float cardTransitionSharpness = 18f;

    [Header("Decision")]
    [SerializeField] private Vector2 decideSize = new(190f, 42f);
    [SerializeField] private Vector2 decideAnchor = new(0.5f, 0.065f);

    [Header("Skip Reward")]
    [SerializeField] private Vector2 skipSize = new(286f, 42f);
    [SerializeField] private Vector2 skipAnchor = new(0.5f, 0.5f);

    [Header("Monochrome")]
    [SerializeField] private Color inkColor = new(0.015f, 0.016f, 0.019f, 0.99f);
    [SerializeField] private Color paperColor = new(0.96f, 0.96f, 0.96f, 1f);
    [SerializeField] private Color mutedColor = new(0.58f, 0.59f, 0.62f, 1f);
    [SerializeField] private Color activeColor = new(1f, 0.78f, 0.08f, 1f);
    [SerializeField] private Color skipBackColor = new(0.20f, 0.20f, 0.21f, 0.98f);

    private BattleRunManager runManager;
    private BattleHUD battleHud;
    private BattleInventoryInteractionController inventoryInteraction;
    private FieldInfo pendingRewardIndexField;

    private RectTransform rewardInner;
    private RectTransform prizeChoices;
    private RectTransform placementNotice;
    private Image placementNoticeImage;
    private Text placementNoticeMainText;
    private RectTransform rewardDescriptionBar;
    private CanvasGroup rewardDescriptionGroup;
    private RectTransform externalDetailRoot;
    private CanvasGroup externalDetailGroup;

    private RectTransform decideRoot;
    private RectTransform skipRoot;
    private Button skipButton;
    private Image skipBack;
    private Text skipLabel;

    private RectTransform inlineDetailRoot;
    private Text inlineDescriptionTitle;
    private Text inlineDescription;
    private Text inlineEffectsTitle;
    private Text inlineEffects;
    private Text inlineTags;

    private readonly Dictionary<int, Vector2> baseCardSizes = new();
    private readonly Dictionary<int, Vector2> animatedCardSizes = new();
    private readonly Dictionary<int, float> animatedCardScales = new();

    private RectTransform lastSelectedCard;
    private int lastSelectedIndex = int.MinValue;
    private float nextResolveTime;

    private void Awake()
    {
        ResolveReferences();
        ResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveUi();
        nextResolveTime = 0f;
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
        Canvas.willRenderCanvases += HandleWillRenderCanvases;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
        RestoreCardsImmediate();
        HideChoiceUi();
    }

    private void OnDestroy()
    {
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
    }

    private void Update()
    {
        ResolveReferences();
        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveUi();
        }
    }

    private void LateUpdate()
    {
        ApplyPresentation(true);
    }

    private void HandleWillRenderCanvases()
    {
        if (isActiveAndEnabled)
            ApplyPresentation(false);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (battleHud == null)
            battleHud = FindFirstObjectByType<BattleHUD>(FindObjectsInactive.Include);
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);

        pendingRewardIndexField ??= typeof(BattleHUD).GetField("pendingRewardIndex", PrivateInstance);
    }

    private void ResolveUi()
    {
        RectTransform rewardScreen = FindRect("PrizeSelectionScreen");
        RectTransform resolvedInner = rewardScreen != null
            ? rewardScreen.Find("ScreenInner") as RectTransform
            : null;

        if (resolvedInner != rewardInner)
        {
            rewardInner = resolvedInner;
            prizeChoices = null;
            placementNotice = null;
            placementNoticeImage = null;
            placementNoticeMainText = null;
            rewardDescriptionBar = null;
            rewardDescriptionGroup = null;
        }

        if (rewardInner != null)
        {
            prizeChoices = rewardInner.Find("PrizeChoices") as RectTransform;
            placementNotice = rewardInner.Find("PlacementNotice") as RectTransform;
            rewardDescriptionBar = rewardInner.Find("RewardActiveDescriptionBar") as RectTransform;
        }
        else
        {
            prizeChoices ??= FindRect("PrizeChoices");
            placementNotice ??= FindRect("PlacementNotice");
            rewardDescriptionBar ??= FindRect("RewardActiveDescriptionBar");
        }

        rewardDescriptionGroup = rewardDescriptionBar != null
            ? rewardDescriptionBar.GetComponent<CanvasGroup>()
            : null;

        if (placementNotice != null)
        {
            placementNoticeImage ??= placementNotice.GetComponent<Image>();
            placementNoticeMainText ??= FindPlacementNoticeMainText();
        }

        RectTransform resolvedDecide = FindRect("RewardDecisionConfirm");
        if (resolvedDecide != null)
            decideRoot = resolvedDecide;

        if (externalDetailRoot == null)
        {
            externalDetailRoot = FindRect("EquipmentDetailPanel");
            externalDetailGroup = externalDetailRoot != null
                ? externalDetailRoot.GetComponent<CanvasGroup>()
                : null;
        }

        EnsureSkipButton();
    }

    private void EnsureSkipButton()
    {
        if (placementNotice == null)
            return;

        if (skipRoot == null)
        {
            RectTransform existing = FindRect("RewardDecisionSkip");
            if (existing != null)
            {
                skipRoot = existing;
                skipButton = skipRoot.GetComponent<Button>();
                skipBack = skipRoot.GetComponent<Image>();
                skipLabel = skipRoot.GetComponentInChildren<Text>(true);
            }
        }

        if (skipRoot == null)
        {
            GameObject go = new("RewardDecisionSkip");
            go.transform.SetParent(placementNotice, false);
            skipRoot = go.AddComponent<RectTransform>();

            skipBack = go.AddComponent<Image>();
            skipBack.raycastTarget = true;

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(paperColor.r, paperColor.g, paperColor.b, 0.52f);
            outline.effectDistance = new Vector2(2f, -2f);

            RectTransform textRect = new GameObject("Text").AddComponent<RectTransform>();
            textRect.SetParent(skipRoot, false);
            Stretch(textRect);

            skipLabel = textRect.gameObject.AddComponent<Text>();
            skipLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            skipLabel.fontSize = 14;
            skipLabel.fontStyle = FontStyle.Bold;
            skipLabel.alignment = TextAnchor.MiddleCenter;
            skipLabel.raycastTarget = false;

            skipButton = go.AddComponent<Button>();
            skipButton.targetGraphic = skipBack;
            skipButton.onClick.AddListener(SkipReward);

            ColorBlock colors = skipButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.pressedColor = new Color(0.68f, 0.68f, 0.68f, 1f);
            colors.selectedColor = colors.highlightedColor;
            skipButton.colors = colors;
        }

        if (skipRoot.parent != placementNotice)
            skipRoot.SetParent(placementNotice, false);

        if (skipBack != null)
            skipBack.color = skipBackColor;
        if (skipLabel != null)
        {
            skipLabel.text = "아이템 획득 포기하기";
            skipLabel.color = paperColor;
        }
    }

    private void EnsureInlineDetail(RectTransform selectedCard)
    {
        if (selectedCard == null)
            return;

        if (inlineDetailRoot == null)
            inlineDetailRoot = FindRect("RewardSelectedInlineDetail");

        if (inlineDetailRoot == null)
        {
            GameObject go = new("RewardSelectedInlineDetail");
            go.transform.SetParent(selectedCard, false);
            inlineDetailRoot = go.AddComponent<RectTransform>();

            inlineDescriptionTitle = CreateText(inlineDetailRoot, "DESCRIPTION", 10, FontStyle.Bold, TextAnchor.MiddleLeft, mutedColor);
            SetAnchors(inlineDescriptionTitle.rectTransform, new Vector2(0f, 0.84f), new Vector2(1f, 1f));

            inlineDescription = CreateText(inlineDetailRoot, string.Empty, 10, FontStyle.Normal, TextAnchor.UpperLeft, paperColor);
            inlineDescription.horizontalOverflow = HorizontalWrapMode.Wrap;
            inlineDescription.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(inlineDescription.rectTransform, new Vector2(0f, 0.58f), new Vector2(1f, 0.84f));

            inlineEffectsTitle = CreateText(inlineDetailRoot, "EFFECTS", 10, FontStyle.Bold, TextAnchor.MiddleLeft, mutedColor);
            SetAnchors(inlineEffectsTitle.rectTransform, new Vector2(0f, 0.48f), new Vector2(1f, 0.58f));

            inlineEffects = CreateText(inlineDetailRoot, string.Empty, 10, FontStyle.Bold, TextAnchor.UpperLeft, paperColor);
            inlineEffects.horizontalOverflow = HorizontalWrapMode.Wrap;
            inlineEffects.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(inlineEffects.rectTransform, new Vector2(0f, 0.16f), new Vector2(1f, 0.48f));

            inlineTags = CreateText(inlineDetailRoot, string.Empty, 9, FontStyle.Bold, TextAnchor.MiddleLeft, mutedColor);
            inlineTags.horizontalOverflow = HorizontalWrapMode.Wrap;
            inlineTags.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(inlineTags.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.16f));
        }

        if (inlineDetailRoot.parent != selectedCard)
            inlineDetailRoot.SetParent(selectedCard, false);

        inlineDetailRoot.anchorMin = new Vector2(0.08f, 0.17f);
        inlineDetailRoot.anchorMax = new Vector2(0.92f, 0.52f);
        inlineDetailRoot.offsetMin = Vector2.zero;
        inlineDetailRoot.offsetMax = Vector2.zero;
        inlineDetailRoot.localScale = Vector3.one;
        inlineDetailRoot.localRotation = Quaternion.identity;
        inlineDetailRoot.gameObject.SetActive(true);
    }

    private void ApplyPresentation(bool advanceTween)
    {
        if (!IsChoiceStage())
        {
            RestoreCardsImmediate();
            HideChoiceUi();
            return;
        }

        ResolveUi();
        EnsureSkipButton();
        SuppressLegacyDescriptionPanels();
        LayoutCompactSkipButton();

        int selectedIndex = GetPendingRewardIndex();
        BattleEquipmentSO selectedEquipment = GetReward(selectedIndex);
        RectTransform selectedCard = FindRewardCard(selectedIndex);
        bool valid = selectedCard != null && selectedEquipment != null;

        ApplyCardTransforms(selectedIndex, valid, advanceTween);

        if (!valid)
        {
            if (inlineDetailRoot != null)
                inlineDetailRoot.gameObject.SetActive(false);
            if (decideRoot != null)
                decideRoot.gameObject.SetActive(false);
            lastSelectedCard = null;
            lastSelectedIndex = selectedIndex;
            return;
        }

        EnsureInlineDetail(selectedCard);
        ConfigureCardContentLayout(selectedCard, selectedEquipment, true);
        RefreshInlineDetail(selectedEquipment);
        LayoutDecisionButton(selectedCard);
        ApplySelectedCardAccent(selectedCard);

        if (lastSelectedCard != null && lastSelectedCard != selectedCard)
            ConfigureCardContentLayout(lastSelectedCard, GetReward(lastSelectedIndex), false);

        lastSelectedCard = selectedCard;
        lastSelectedIndex = selectedIndex;
    }

    private void ApplyCardTransforms(int selectedIndex, bool hasSelection, bool advanceTween)
    {
        if (prizeChoices == null)
            return;

        RewardPrizeDrag[] cards = prizeChoices.GetComponentsInChildren<RewardPrizeDrag>(true);
        float blend = advanceTween
            ? 1f - Mathf.Exp(-Mathf.Max(1f, cardTransitionSharpness) * Time.unscaledDeltaTime)
            : 0f;

        for (int i = 0; i < cards.Length; i++)
        {
            RewardPrizeDrag drag = cards[i];
            RectTransform card = drag != null ? drag.transform as RectTransform : null;
            if (card == null)
                continue;

            int id = card.GetInstanceID();
            if (!baseCardSizes.TryGetValue(id, out Vector2 baseSize))
            {
                baseSize = card.sizeDelta;
                baseCardSizes[id] = baseSize;
                animatedCardSizes[id] = baseSize;
                animatedCardScales[id] = 1f;
            }

            bool selected = hasSelection && drag.RewardIndex == selectedIndex;
            Vector2 targetSize = selected
                ? new Vector2(baseSize.x, baseSize.y * Mathf.Max(1.15f, selectedHeightMultiplier))
                : baseSize;
            float targetScale = !hasSelection
                ? 1f
                : selected ? selectedCardScale : inactiveCardScale;

            Vector2 currentSize = animatedCardSizes.TryGetValue(id, out Vector2 sizeState)
                ? sizeState
                : baseSize;
            float currentScale = animatedCardScales.TryGetValue(id, out float scaleState)
                ? scaleState
                : 1f;

            if (advanceTween)
            {
                currentSize = Vector2.Lerp(currentSize, targetSize, blend);
                currentScale = Mathf.Lerp(currentScale, targetScale, blend);
                animatedCardSizes[id] = currentSize;
                animatedCardScales[id] = currentScale;
            }

            card.sizeDelta = currentSize;
            card.localScale = Vector3.one * currentScale;
            card.localRotation = Quaternion.identity;

            ConfigureCardContentLayout(card, GetReward(drag.RewardIndex), selected);
        }
    }

    private void ConfigureCardContentLayout(RectTransform card, BattleEquipmentSO equipment, bool selected)
    {
        if (card == null)
            return;

        RectTransform icon = card.Find("PrizeIcon") as RectTransform;
        if (icon != null)
        {
            icon.anchorMin = icon.anchorMax = selected
                ? new Vector2(0.5f, 0.80f)
                : new Vector2(0.5f, 0.67f);
            icon.anchoredPosition = Vector2.zero;
            icon.sizeDelta = selected ? new Vector2(122f, 122f) : new Vector2(150f, 150f);
        }

        if (equipment == null)
            return;

        string displayName = equipment.GetDisplayName();
        string rarity = equipment.rarity.ToString().ToUpperInvariant();
        string type = equipment.type.ToString().ToUpperInvariant();

        Text[] texts = card.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null ||
                (inlineDetailRoot != null && IsChildOf(text.transform, inlineDetailRoot)) ||
                (decideRoot != null && IsChildOf(text.transform, decideRoot)))
                continue;

            string value = text.text ?? string.Empty;
            bool action = value.Contains("CLICK") || value.Contains("DRAG");
            bool itemName = value == displayName;
            bool itemType = value == type;
            bool itemRarity = value == rarity || (value.Contains(rarity) && value.StartsWith("LV."));

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
                    selected ? new Vector2(0.08f, 0.675f) : new Vector2(0.08f, 0.39f),
                    selected ? new Vector2(0.92f, 0.72f) : new Vector2(0.92f, 0.47f));
            }
            else if (itemName)
            {
                SetAnchors(text.rectTransform,
                    selected ? new Vector2(0.06f, 0.61f) : new Vector2(0.06f, 0.22f),
                    selected ? new Vector2(0.94f, 0.675f) : new Vector2(0.94f, 0.39f));
                if (selected)
                    text.fontSize = Mathf.Max(16, text.fontSize);
            }
            else if (itemType)
            {
                SetAnchors(text.rectTransform,
                    selected ? new Vector2(0.08f, 0.56f) : new Vector2(0.08f, 0.14f),
                    selected ? new Vector2(0.92f, 0.61f) : new Vector2(0.92f, 0.22f));
            }
        }
    }

    private void RefreshInlineDetail(BattleEquipmentSO equipment)
    {
        if (equipment == null || inlineDetailRoot == null)
            return;

        if (inlineDescription != null)
        {
            inlineDescription.text = !string.IsNullOrWhiteSpace(equipment.description)
                ? equipment.description.Trim()
                : equipment.shootingData != null
                    ? "Manual weapon."
                    : "Equipment item.";
        }

        if (inlineEffects != null)
        {
            inlineEffects.text =
                $"• DAMAGE        ×{equipment.damageMultiplier:0.00}\n" +
                $"• MOVE SPEED    ×{equipment.moveSpeedMultiplier:0.00}\n" +
                $"• RANGE         ×{equipment.rangeMultiplier:0.00}";
        }

        if (inlineTags != null)
            inlineTags.text = BuildTagLine(equipment);
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

    private void LayoutDecisionButton(RectTransform selectedCard)
    {
        if (decideRoot == null || selectedCard == null)
            return;

        if (decideRoot.parent != selectedCard)
            decideRoot.SetParent(selectedCard, false);

        decideRoot.anchorMin = decideRoot.anchorMax = decideAnchor;
        decideRoot.pivot = new Vector2(0.5f, 0.5f);
        decideRoot.sizeDelta = decideSize;
        decideRoot.anchoredPosition = Vector2.zero;
        decideRoot.localScale = Vector3.one;
        decideRoot.localRotation = Quaternion.identity;
        decideRoot.gameObject.SetActive(true);
        decideRoot.SetAsLastSibling();

        Image back = decideRoot.GetComponent<Image>();
        if (back != null)
            back.color = inkColor;

        Text label = decideRoot.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = "결정";
            label.color = paperColor;
        }
    }

    private void LayoutCompactSkipButton()
    {
        if (placementNotice == null || skipRoot == null)
            return;

        // 기존 넓은 회색 안내 바는 시각적으로 제거하고,
        // 그 자리에는 작은 '아이템 획득 포기하기' 버튼만 남깁니다.
        if (placementNoticeImage != null)
            placementNoticeImage.color = Color.clear;

        placementNoticeMainText ??= FindPlacementNoticeMainText();
        if (placementNoticeMainText != null)
            placementNoticeMainText.gameObject.SetActive(false);

        if (skipRoot.parent != placementNotice)
            skipRoot.SetParent(placementNotice, false);

        skipRoot.anchorMin = skipRoot.anchorMax = skipAnchor;
        skipRoot.pivot = new Vector2(0.5f, 0.5f);
        skipRoot.sizeDelta = skipSize;
        skipRoot.anchoredPosition = Vector2.zero;
        skipRoot.localScale = Vector3.one;
        skipRoot.localRotation = Quaternion.identity;
        skipRoot.gameObject.SetActive(true);
        skipRoot.SetAsLastSibling();
    }

    private Text FindPlacementNoticeMainText()
    {
        if (placementNotice == null)
            return null;

        Text[] texts = placementNotice.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text text = texts[i];
            if (text == null)
                continue;
            if (skipRoot != null && IsChildOf(text.transform, skipRoot))
                continue;
            return text;
        }
        return null;
    }

    private void ApplySelectedCardAccent(RectTransform selectedCard)
    {
        if (selectedCard == null)
            return;

        Outline outline = selectedCard.GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = true;
            outline.effectColor = activeColor;
            outline.effectDistance = new Vector2(5f, -5f);
        }

        Image icon = selectedCard.Find("PrizeIcon")?.GetComponent<Image>();
        if (icon != null)
        {
            icon.material = null;
            icon.color = Color.white;
        }
    }

    private void SuppressLegacyDescriptionPanels()
    {
        // Reward 후보 설명은 이제 선택 카드 안에만 존재합니다.
        if (rewardDescriptionBar != null && rewardDescriptionBar.gameObject.activeSelf)
            rewardDescriptionBar.gameObject.SetActive(false);

        if (rewardDescriptionGroup != null)
        {
            rewardDescriptionGroup.alpha = 0f;
            rewardDescriptionGroup.blocksRaycasts = false;
            rewardDescriptionGroup.interactable = false;
        }

        if (externalDetailGroup != null)
        {
            externalDetailGroup.alpha = 0f;
            externalDetailGroup.blocksRaycasts = false;
            externalDetailGroup.interactable = false;
        }
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

    private BattleEquipmentSO GetReward(int rewardIndex)
    {
        if (runManager == null || rewardIndex < 0 || rewardIndex >= runManager.CurrentRewardChoices.Count)
            return null;
        return runManager.CurrentRewardChoices[rewardIndex];
    }

    private bool IsChoiceStage()
    {
        if (runManager == null || !runManager.RunActive || runManager.State != BattleRunState.Reward)
            return false;

        return inventoryInteraction == null || !inventoryInteraction.IsRewardPackEditing;
    }

    private int GetPendingRewardIndex()
    {
        if (battleHud == null || pendingRewardIndexField == null)
            return -1;
        object raw = pendingRewardIndexField.GetValue(battleHud);
        return raw is int index ? index : -1;
    }

    private void SkipReward()
    {
        if (!IsChoiceStage() || runManager == null)
            return;

        if (pendingRewardIndexField != null && battleHud != null)
            pendingRewardIndexField.SetValue(battleHud, -1);

        RestoreCardsImmediate();
        HideChoiceUi();
        runManager.SkipReward();
    }

    private void RestoreCardsImmediate()
    {
        if (prizeChoices == null)
            return;

        RewardPrizeDrag[] cards = prizeChoices.GetComponentsInChildren<RewardPrizeDrag>(true);
        for (int i = 0; i < cards.Length; i++)
        {
            RewardPrizeDrag drag = cards[i];
            RectTransform card = drag != null ? drag.transform as RectTransform : null;
            if (card == null)
                continue;

            int id = card.GetInstanceID();
            if (baseCardSizes.TryGetValue(id, out Vector2 baseSize))
                card.sizeDelta = baseSize;
            card.localScale = Vector3.one;
            card.localRotation = Quaternion.identity;
            ConfigureCardContentLayout(card, GetReward(drag.RewardIndex), false);
        }

        animatedCardSizes.Clear();
        animatedCardScales.Clear();
        lastSelectedCard = null;
        lastSelectedIndex = int.MinValue;
    }

    private void HideChoiceUi()
    {
        if (decideRoot != null && decideRoot.gameObject.activeSelf)
            decideRoot.gameObject.SetActive(false);
        if (skipRoot != null && skipRoot.gameObject.activeSelf)
            skipRoot.gameObject.SetActive(false);
        if (inlineDetailRoot != null && inlineDetailRoot.gameObject.activeSelf)
            inlineDetailRoot.gameObject.SetActive(false);

        // PACK 편집 단계에서 PlacementNotice를 다시 상태 표시용으로 쓸 수 있게 복원합니다.
        if (placementNoticeMainText != null)
            placementNoticeMainText.gameObject.SetActive(true);
        if (placementNoticeImage != null)
            placementNoticeImage.color = new Color(0.035f, 0.037f, 0.042f, 0.88f);
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

    private static Text CreateText(Transform parent, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        RectTransform rect = new GameObject("Text").AddComponent<RectTransform>();
        rect.SetParent(parent, false);
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
        if (rect == null)
            return;
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

public static class BattleRewardCardActionAutoInstaller
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

            if (manager.GetComponent<BattleRewardCardActionController>() != null)
                continue;

            Undo.AddComponent<BattleRewardCardActionController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponent()
    {
        BattleSceneManager[] managers = Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleRewardCardActionController>() == null)
                manager.gameObject.AddComponent<BattleRewardCardActionController>();
        }
    }
}

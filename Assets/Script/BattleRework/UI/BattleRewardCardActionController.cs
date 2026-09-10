using System.Collections.Generic;
using System.Text;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Reward 선택 단계의 카드 액션/강조를 최종 렌더 단계에서 관리합니다.
///
/// 규칙:
/// - Hover는 카드 Transform을 절대 변경하지 않습니다.
/// - 클릭되어 pendingRewardIndex가 된 카드만 크게 펼치고, 나머지 카드는 작게 줄입니다.
/// - 선택 카드 내부 우측에 설명 / PARAMETER / TAG 정보를 직접 표시합니다.
/// - [결정]은 선택 카드 내부 상세 영역에만 표시합니다.
/// - [아이템 획득 포기]는 카드 내부가 아니라 하단 PlacementNotice 회색 바 우측에 고정합니다.
/// - Reward 후보 선택 중에는 외부 EquipmentDetailPanel / RewardActiveDescriptionBar를 숨깁니다.
///
/// 카드 확대/축소는 pendingRewardIndex에만 의존합니다. PointerEnter/Exit에 따라 크기를 바꾸지 않으므로
/// 이전 Hover 경계 진동 문제가 다시 발생하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(44100)]
public sealed class BattleRewardCardActionController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("Selected Card")]
    [SerializeField] private Vector2 selectedSizeMultiplier = new(1.30f, 1.12f);
    [SerializeField, Range(1f, 1.12f)] private float selectedCardScale = 1.03f;
    [SerializeField, Range(0.68f, 0.98f)] private float inactiveCardScale = 0.84f;
    [SerializeField, Min(1f)] private float cardTransitionSharpness = 18f;

    [Header("Selected Card Action")]
    [SerializeField] private Vector2 decideSize = new(192f, 42f);
    [SerializeField] private Vector2 decidePosition = new(0f, 24f);

    [Header("Skip Action In Bottom Bar")]
    [SerializeField] private Vector2 skipSize = new(238f, 40f);
    [SerializeField] private Vector2 skipAnchor = new(0.88f, 0.5f);

    [Header("Monochrome")]
    [SerializeField] private Color inkColor = new(0.015f, 0.016f, 0.019f, 0.99f);
    [SerializeField] private Color paperColor = new(0.96f, 0.96f, 0.96f, 1f);
    [SerializeField] private Color mutedColor = new(0.58f, 0.59f, 0.62f, 1f);
    [SerializeField] private Color activeColor = new(1f, 0.78f, 0.08f, 1f);

    private BattleRunManager runManager;
    private BattleHUD battleHud;
    private BattleInventoryInteractionController inventoryInteraction;

    private FieldInfo pendingRewardIndexField;

    private RectTransform rewardInner;
    private RectTransform prizeChoices;
    private RectTransform placementNotice;
    private RectTransform rewardDescriptionBar;
    private CanvasGroup rewardDescriptionGroup;
    private RectTransform externalDetailRoot;
    private CanvasGroup externalDetailGroup;

    private RectTransform decideRoot;
    private RectTransform skipRoot;
    private Button skipButton;

    private RectTransform inlineDetailRoot;
    private Image inlineDetailBack;
    private Text inlineTitle;
    private Text inlineBody;
    private Text inlineStats;
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
        HideActions();
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
        ApplySelectedCardPresentation(true);
    }

    private void HandleWillRenderCanvases()
    {
        if (isActiveAndEnabled)
            ApplySelectedCardPresentation(false);
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
            rewardDescriptionBar = null;
            rewardDescriptionGroup = null;
        }

        if (rewardInner != null)
        {
            prizeChoices = rewardInner.Find("PrizeChoices") as RectTransform;
            placementNotice = rewardInner.Find("PlacementNotice") as RectTransform;
            rewardDescriptionBar = rewardInner.Find("RewardActiveDescriptionBar") as RectTransform;
            rewardDescriptionGroup = rewardDescriptionBar != null
                ? rewardDescriptionBar.GetComponent<CanvasGroup>()
                : null;
        }
        else
        {
            prizeChoices ??= FindRect("PrizeChoices");
            placementNotice ??= FindRect("PlacementNotice");
            rewardDescriptionBar ??= FindRect("RewardActiveDescriptionBar");
            rewardDescriptionGroup ??= rewardDescriptionBar != null
                ? rewardDescriptionBar.GetComponent<CanvasGroup>()
                : null;
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
        if (skipRoot == null)
        {
            RectTransform existing = FindRect("RewardDecisionSkip");
            if (existing != null)
            {
                skipRoot = existing;
                skipButton = skipRoot.GetComponent<Button>();
            }
        }

        if (skipRoot == null)
        {
            if (placementNotice == null)
                return;

            GameObject go = new("RewardDecisionSkip");
            go.transform.SetParent(placementNotice, false);
            skipRoot = go.AddComponent<RectTransform>();
            skipRoot.sizeDelta = skipSize;

            Image back = go.AddComponent<Image>();
            back.color = inkColor;
            back.raycastTarget = true;

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(paperColor.r, paperColor.g, paperColor.b, 0.54f);
            outline.effectDistance = new Vector2(2f, -2f);

            RectTransform textRect = new GameObject("Text").AddComponent<RectTransform>();
            textRect.SetParent(skipRoot, false);
            Stretch(textRect);

            Text label = textRect.gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = "아이템 획득 포기";
            label.fontSize = 14;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = paperColor;
            label.raycastTarget = false;

            skipButton = go.AddComponent<Button>();
            skipButton.targetGraphic = back;
            skipButton.onClick.AddListener(SkipReward);

            ColorBlock colors = skipButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
            colors.pressedColor = new Color(0.68f, 0.68f, 0.68f, 1f);
            colors.selectedColor = colors.highlightedColor;
            skipButton.colors = colors;
        }

        if (placementNotice != null && skipRoot.parent != placementNotice)
            skipRoot.SetParent(placementNotice, false);
    }

    private void EnsureInlineDetail(RectTransform selectedCard)
    {
        if (selectedCard == null)
            return;

        if (inlineDetailRoot == null)
        {
            GameObject go = new("RewardSelectedInlineDetail");
            go.transform.SetParent(selectedCard, false);
            inlineDetailRoot = go.AddComponent<RectTransform>();
            inlineDetailBack = go.AddComponent<Image>();
            inlineDetailBack.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.94f);
            inlineDetailBack.raycastTarget = false;

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(paperColor.r, paperColor.g, paperColor.b, 0.22f);
            outline.effectDistance = new Vector2(2f, -2f);

            inlineTitle = CreateText(inlineDetailRoot, "ITEM DATA", 11, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
            SetAnchors(inlineTitle.rectTransform, new Vector2(0.06f, 0.84f), new Vector2(0.94f, 0.96f));

            inlineBody = CreateText(inlineDetailRoot, string.Empty, 10, FontStyle.Normal, TextAnchor.UpperLeft, paperColor);
            inlineBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            inlineBody.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(inlineBody.rectTransform, new Vector2(0.06f, 0.48f), new Vector2(0.94f, 0.83f));

            inlineStats = CreateText(inlineDetailRoot, string.Empty, 9, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
            SetAnchors(inlineStats.rectTransform, new Vector2(0.06f, 0.31f), new Vector2(0.94f, 0.47f));

            inlineTags = CreateText(inlineDetailRoot, string.Empty, 8, FontStyle.Bold, TextAnchor.MiddleLeft, mutedColor);
            inlineTags.horizontalOverflow = HorizontalWrapMode.Wrap;
            inlineTags.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(inlineTags.rectTransform, new Vector2(0.06f, 0.18f), new Vector2(0.94f, 0.31f));
        }

        if (inlineDetailRoot.parent != selectedCard)
            inlineDetailRoot.SetParent(selectedCard, false);

        inlineDetailRoot.anchorMin = new Vector2(0.48f, 0.07f);
        inlineDetailRoot.anchorMax = new Vector2(0.97f, 0.93f);
        inlineDetailRoot.offsetMin = Vector2.zero;
        inlineDetailRoot.offsetMax = Vector2.zero;
        inlineDetailRoot.localScale = Vector3.one;
        inlineDetailRoot.localRotation = Quaternion.identity;
        inlineDetailRoot.gameObject.SetActive(true);
    }

    private void ApplySelectedCardPresentation(bool advanceTween)
    {
        if (!IsChoiceStage())
        {
            RestoreCardsImmediate();
            HideActions();
            return;
        }

        ResolveUi();
        EnsureSkipButton();
        SuppressLegacyDescriptionPanels();
        LayoutSkipInBottomBar();

        int selectedIndex = GetPendingRewardIndex();
        BattleEquipmentSO selectedEquipment = GetReward(selectedIndex);
        RectTransform selectedCard = FindRewardCard(selectedIndex);
        bool valid = selectedCard != null && selectedEquipment != null;

        ApplyCardSizes(selectedIndex, valid, advanceTween);

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
        LayoutDecideButton();
        ApplySelectedCardAccent(selectedCard);

        if (lastSelectedCard != null && lastSelectedCard != selectedCard)
        {
            BattleEquipmentSO oldEquipment = GetReward(lastSelectedIndex);
            ConfigureCardContentLayout(lastSelectedCard, oldEquipment, false);
        }

        lastSelectedCard = selectedCard;
        lastSelectedIndex = selectedIndex;
    }

    private void ApplyCardSizes(int selectedIndex, bool hasSelection, bool advanceTween)
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
                ? new Vector2(baseSize.x * selectedSizeMultiplier.x, baseSize.y * selectedSizeMultiplier.y)
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

            BattleEquipmentSO equipment = GetReward(drag.RewardIndex);
            ConfigureCardContentLayout(card, equipment, selected);
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
                ? new Vector2(0.235f, 0.69f)
                : new Vector2(0.5f, 0.67f);
            icon.anchoredPosition = Vector2.zero;
            icon.sizeDelta = selected ? new Vector2(138f, 138f) : new Vector2(150f, 150f);
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
            bool itemRarity = value == rarity || value.Contains(rarity) && value.StartsWith("LV.");

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
                    selected ? new Vector2(0.05f, 0.39f) : new Vector2(0.08f, 0.39f),
                    selected ? new Vector2(0.43f, 0.47f) : new Vector2(0.92f, 0.47f));
            }
            else if (itemName)
            {
                SetAnchors(text.rectTransform,
                    selected ? new Vector2(0.04f, 0.22f) : new Vector2(0.06f, 0.22f),
                    selected ? new Vector2(0.44f, 0.39f) : new Vector2(0.94f, 0.39f));
            }
            else if (itemType)
            {
                SetAnchors(text.rectTransform,
                    selected ? new Vector2(0.05f, 0.14f) : new Vector2(0.08f, 0.14f),
                    selected ? new Vector2(0.43f, 0.22f) : new Vector2(0.92f, 0.22f));
            }
        }
    }

    private void RefreshInlineDetail(BattleEquipmentSO equipment)
    {
        if (equipment == null || inlineDetailRoot == null)
            return;

        if (inlineTitle != null)
            inlineTitle.text = $"LV.1  //  {equipment.rarity.ToString().ToUpperInvariant()}";

        if (inlineBody != null)
        {
            string body = !string.IsNullOrWhiteSpace(equipment.description)
                ? equipment.description.Trim()
                : equipment.shootingData != null ? "Manual weapon." : "Equipment item.";
            inlineBody.text = body;
        }

        if (inlineStats != null)
        {
            inlineStats.text =
                $"DMG ×{equipment.damageMultiplier:0.00}\n" +
                $"MOVE ×{equipment.moveSpeedMultiplier:0.00}   RANGE ×{equipment.rangeMultiplier:0.00}";
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

    private void LayoutDecideButton()
    {
        if (decideRoot == null || inlineDetailRoot == null)
            return;

        if (decideRoot.parent != inlineDetailRoot)
            decideRoot.SetParent(inlineDetailRoot, false);

        decideRoot.anchorMin = decideRoot.anchorMax = new Vector2(0.5f, 0f);
        decideRoot.pivot = new Vector2(0.5f, 0.5f);
        decideRoot.sizeDelta = decideSize;
        decideRoot.anchoredPosition = decidePosition;
        decideRoot.localScale = Vector3.one;
        decideRoot.localRotation = Quaternion.identity;
        decideRoot.gameObject.SetActive(true);
        decideRoot.SetAsLastSibling();
    }

    private void LayoutSkipInBottomBar()
    {
        if (placementNotice == null || skipRoot == null)
            return;

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

        Text mainText = FindPlacementNoticeMainText();
        if (mainText != null)
        {
            mainText.alignment = TextAnchor.MiddleLeft;
            mainText.rectTransform.anchorMin = new Vector2(0.025f, 0f);
            mainText.rectTransform.anchorMax = new Vector2(0.72f, 1f);
            mainText.rectTransform.offsetMin = Vector2.zero;
            mainText.rectTransform.offsetMax = Vector2.zero;
        }
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
        HideActions();
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
            ConfigureCardContentLayout(card, GetReward(drag.RewardIndex), false);
        }

        animatedCardSizes.Clear();
        animatedCardScales.Clear();
        lastSelectedCard = null;
        lastSelectedIndex = int.MinValue;
    }

    private void HideActions()
    {
        if (decideRoot != null && decideRoot.gameObject.activeSelf)
            decideRoot.gameObject.SetActive(false);
        if (skipRoot != null && skipRoot.gameObject.activeSelf)
            skipRoot.gameObject.SetActive(false);
        if (inlineDetailRoot != null && inlineDetailRoot.gameObject.activeSelf)
            inlineDetailRoot.gameObject.SetActive(false);
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

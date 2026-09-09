using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reward TV의 아이템 선택 가독성을 최종 단계에서 정리합니다.
///
/// - 기본 Reward 카드는 비활성 톤으로 표시합니다.
/// - Hover / Click으로 현재 활성화된 카드 하나에만 테두리를 표시합니다.
/// - TV 하단에는 반투명 설명 바를 두고 선택 아이템 설명 / 스탯을 표시합니다.
/// - Reward를 PACK에 Drop해 획득이 잠기는 순간 TV 전체를 강한 아날로그 Static 화면으로 전환합니다.
/// - Static 화면이 켜진 뒤에는 PrizeChoices가 입력을 받지 않으며 PACK 편집 / TRASH / DONE은 그대로 사용할 수 있습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33150)]
public sealed class BattleRewardSelectionPresentationController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string AnalogShaderName = "UI/BattleAnalogTvOverlay";

    [Header("Inactive Reward Cards")]
    [SerializeField, Range(0.15f, 1f)] private float inactiveCardAlpha = 0.52f;
    [SerializeField, Range(0f, 1f)] private float inactiveBackgroundBlend = 0.72f;
    [SerializeField] private Color inactiveBackground = new(0.025f, 0.028f, 0.038f, 1f);
    [SerializeField] private Color inactiveIconColor = new(0.58f, 0.60f, 0.64f, 0.58f);
    [SerializeField] private Color selectedOutlineColor = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color hoverOutlineColor = new(0.15f, 0.88f, 0.92f, 1f);

    [Header("Description Bar")]
    [SerializeField] private Color descriptionBarColor = new(0.012f, 0.016f, 0.026f, 0.78f);
    [SerializeField] private Color descriptionTitleColor = new(1f, 0.82f, 0.18f, 1f);
    [SerializeField] private Color descriptionBodyColor = new(0.88f, 0.90f, 0.94f, 1f);
    [SerializeField] private Color descriptionAccentColor = new(0.15f, 0.88f, 0.92f, 0.92f);

    [Header("Selection Locked Static")]
    [SerializeField, Range(0f, 1f)] private float lockBackgroundAlpha = 0.92f;
    [SerializeField, Range(0f, 0.4f)] private float lockNoiseStrength = 0.20f;
    [SerializeField, Range(0f, 0.3f)] private float lockScanlineStrength = 0.14f;
    [SerializeField, Range(0f, 0.3f)] private float lockRollingBandStrength = 0.12f;
    [SerializeField, Min(0.01f)] private float lockFadeDuration = 0.065f;
    [SerializeField, Min(0.05f)] private float lockBurstDuration = 0.32f;

    private BattleRunManager runManager;
    private BattleHUD battleHud;
    private BattleInventoryInteractionController inventoryInteraction;

    private FieldInfo pendingRewardIndexField;
    private FieldInfo focusedRewardNameField;
    private FieldInfo focusedRewardStatsField;

    private RectTransform rewardScreen;
    private RectTransform screenInner;
    private RectTransform prizeChoices;

    private RectTransform descriptionBar;
    private CanvasGroup descriptionGroup;
    private Text descriptionTitle;
    private Text descriptionBody;
    private Image descriptionAccent;

    private RectTransform lockedOverlay;
    private CanvasGroup lockedGroup;
    private Material lockedStaticMaterial;
    private RawImage lockedStaticImage;
    private RectTransform lockedSweepBar;
    private Image lockedSweepImage;
    private Text lockedTitle;
    private Text lockedMessage;

    private readonly List<CardVisual> cards = new();
    private RectTransform cachedPrizeChoices;
    private int cachedPrizeChildCount = -1;
    private int hoveredRewardIndex = -1;
    private bool wasLocked;
    private float lockStartedAt = -1f;
    private float nextResolveTime;

    private sealed class CardVisual
    {
        public RectTransform root;
        public RewardPrizeDrag drag;
        public Image background;
        public Image icon;
        public Outline outline;
        public CanvasGroup group;
        public Color baseBackground;
        public Color baseIcon;
    }

    private void Awake()
    {
        ResolveReferences();
        CacheHudReflection();
        ResolveUi(true);
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheHudReflection();
        nextResolveTime = 0f;
    }

    private void OnDisable()
    {
        RestoreCardDefaults();
        RestoreLegacyFocusTexts();
        SetLockedImmediate(false);
        hoveredRewardIndex = -1;
        wasLocked = false;
    }

    private void OnDestroy()
    {
        if (lockedStaticMaterial != null)
            Destroy(lockedStaticMaterial);
    }

    private void Update()
    {
        ResolveReferences();
        CacheHudReflection();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveUi(false);
        }

        bool reward = IsReward();
        if (!reward)
        {
            if (wasLocked)
                SetLockedImmediate(false);
            wasLocked = false;
            hoveredRewardIndex = -1;
            return;
        }

        bool locked = inventoryInteraction != null && inventoryInteraction.IsRewardPackEditing;
        if (locked != wasLocked)
        {
            if (locked)
                BeginSelectionLock();
            else
                SetLockedImmediate(false);
            wasLocked = locked;
        }

        UpdateLockedStatic(locked);
    }

    private void LateUpdate()
    {
        if (!IsReward())
            return;

        ResolveUi(false);
        HideLegacyFocusTexts();

        bool locked = inventoryInteraction != null && inventoryInteraction.IsRewardPackEditing;
        int pending = GetPendingRewardIndex();
        int active = locked ? -1 : hoveredRewardIndex >= 0 ? hoveredRewardIndex : pending;

        ApplyCardVisuals(active, pending, locked);
        UpdateDescription(active);

        if (lockedOverlay != null && locked)
            lockedOverlay.SetAsLastSibling();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (battleHud == null)
            battleHud = FindFirstObjectByType<BattleHUD>();
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>();
    }

    private void CacheHudReflection()
    {
        if (battleHud == null)
            return;

        pendingRewardIndexField ??= typeof(BattleHUD).GetField("pendingRewardIndex", PrivateInstance);
        focusedRewardNameField ??= typeof(BattleHUD).GetField("focusedRewardName", PrivateInstance);
        focusedRewardStatsField ??= typeof(BattleHUD).GetField("focusedRewardStats", PrivateInstance);
    }

    private bool IsReward()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Reward;
    }

    private void ResolveUi(bool force)
    {
        if (rewardScreen == null)
            rewardScreen = FindRect("PrizeSelectionScreen");

        RectTransform resolvedInner = rewardScreen != null
            ? rewardScreen.Find("ScreenInner") as RectTransform
            : null;

        if (resolvedInner != screenInner)
        {
            screenInner = resolvedInner;
            descriptionBar = null;
            descriptionGroup = null;
            descriptionTitle = null;
            descriptionBody = null;
            descriptionAccent = null;
            lockedOverlay = null;
            lockedGroup = null;
            lockedStaticImage = null;
            lockedSweepBar = null;
            lockedSweepImage = null;
            lockedTitle = null;
            lockedMessage = null;
        }

        if (screenInner != null)
        {
            prizeChoices = screenInner.Find("PrizeChoices") as RectTransform;
            EnsureDescriptionBar();
            EnsureLockedOverlay();
        }
        else
        {
            prizeChoices = null;
        }

        if (force || prizeChoices != cachedPrizeChoices ||
            (prizeChoices != null && prizeChoices.childCount != cachedPrizeChildCount))
        {
            RebuildCardCache();
        }
    }

    private void RebuildCardCache()
    {
        RestoreCardDefaults();
        cards.Clear();
        cachedPrizeChoices = prizeChoices;
        cachedPrizeChildCount = prizeChoices != null ? prizeChoices.childCount : -1;
        hoveredRewardIndex = -1;

        if (prizeChoices == null)
            return;

        for (int i = 0; i < prizeChoices.childCount; i++)
        {
            RectTransform cardRoot = prizeChoices.GetChild(i) as RectTransform;
            if (cardRoot == null)
                continue;

            RewardPrizeDrag drag = cardRoot.GetComponent<RewardPrizeDrag>();
            if (drag == null)
                continue;

            Image background = cardRoot.GetComponent<Image>();
            if (background == null)
                continue;

            Outline outline = cardRoot.GetComponent<Outline>();
            if (outline == null)
                outline = cardRoot.gameObject.AddComponent<Outline>();

            CanvasGroup group = cardRoot.GetComponent<CanvasGroup>();
            if (group == null)
                group = cardRoot.gameObject.AddComponent<CanvasGroup>();

            Image icon = cardRoot.Find("PrizeIcon")?.GetComponent<Image>();

            BattleRewardSelectionVisualPointer pointer = cardRoot.GetComponent<BattleRewardSelectionVisualPointer>();
            if (pointer == null)
                pointer = cardRoot.gameObject.AddComponent<BattleRewardSelectionVisualPointer>();
            pointer.Configure(this, drag.RewardIndex);

            cards.Add(new CardVisual
            {
                root = cardRoot,
                drag = drag,
                background = background,
                icon = icon,
                outline = outline,
                group = group,
                baseBackground = background.color,
                baseIcon = icon != null ? icon.color : Color.white
            });
        }
    }

    private void ApplyCardVisuals(int activeIndex, int pendingIndex, bool locked)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            CardVisual card = cards[i];
            if (card == null || card.root == null || card.drag == null)
                continue;

            int index = card.drag.RewardIndex;
            bool active = !locked && index == activeIndex;
            bool hovered = active && index == hoveredRewardIndex;
            bool selected = active && index == pendingIndex;

            if (card.group != null)
                card.group.alpha = active ? 1f : inactiveCardAlpha;

            if (card.background != null)
            {
                card.background.color = active
                    ? card.baseBackground
                    : Color.Lerp(card.baseBackground, inactiveBackground, inactiveBackgroundBlend);
            }

            if (card.icon != null)
                card.icon.color = active ? card.baseIcon : inactiveIconColor;

            if (card.outline != null)
            {
                card.outline.enabled = active;
                if (active)
                {
                    card.outline.effectColor = hovered ? hoverOutlineColor : selectedOutlineColor;
                    card.outline.effectDistance = hovered
                        ? new Vector2(5f, -5f)
                        : new Vector2(6f, -6f);
                }
            }
        }
    }

    private void RestoreCardDefaults()
    {
        for (int i = 0; i < cards.Count; i++)
        {
            CardVisual card = cards[i];
            if (card == null || card.root == null)
                continue;
            if (card.group != null)
                card.group.alpha = 1f;
            if (card.background != null)
                card.background.color = card.baseBackground;
            if (card.icon != null)
                card.icon.color = card.baseIcon;
            if (card.outline != null)
                card.outline.enabled = true;
        }
    }

    internal void HandleCardHover(int rewardIndex, bool entered)
    {
        if (!IsReward() || (inventoryInteraction != null && inventoryInteraction.IsRewardPackEditing))
            return;

        if (entered)
            hoveredRewardIndex = rewardIndex;
        else if (hoveredRewardIndex == rewardIndex)
            hoveredRewardIndex = -1;
    }

    private int GetPendingRewardIndex()
    {
        if (battleHud == null || pendingRewardIndexField == null)
            return -1;
        object value = pendingRewardIndexField.GetValue(battleHud);
        return value is int index ? index : -1;
    }

    private void EnsureDescriptionBar()
    {
        if (screenInner == null)
            return;

        if (descriptionBar == null)
        {
            Transform existing = screenInner.Find("RewardActiveDescriptionBar");
            descriptionBar = existing as RectTransform;
        }

        if (descriptionBar == null)
        {
            descriptionBar = CreateRect(screenInner, "RewardActiveDescriptionBar", Vector2.zero);
            descriptionBar.anchorMin = new Vector2(0.045f, 0.155f);
            descriptionBar.anchorMax = new Vector2(0.955f, 0.305f);
            descriptionBar.offsetMin = Vector2.zero;
            descriptionBar.offsetMax = Vector2.zero;

            Image background = descriptionBar.gameObject.AddComponent<Image>();
            background.color = descriptionBarColor;
            background.raycastTarget = false;

            descriptionGroup = descriptionBar.gameObject.AddComponent<CanvasGroup>();
            descriptionGroup.blocksRaycasts = false;
            descriptionGroup.interactable = false;

            RectTransform accent = CreateRect(descriptionBar, "Accent", Vector2.zero);
            accent.anchorMin = new Vector2(0f, 0f);
            accent.anchorMax = new Vector2(0.008f, 1f);
            accent.offsetMin = Vector2.zero;
            accent.offsetMax = Vector2.zero;
            descriptionAccent = accent.gameObject.AddComponent<Image>();
            descriptionAccent.color = descriptionAccentColor;
            descriptionAccent.raycastTarget = false;

            descriptionTitle = CreateText(
                descriptionBar,
                "SELECT AN ITEM",
                16,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                descriptionTitleColor);
            SetAnchors(descriptionTitle.rectTransform, new Vector2(0.035f, 0.52f), new Vector2(0.97f, 0.92f));

            descriptionBody = CreateText(
                descriptionBar,
                "카드를 가리키거나 선택하면 아이템 설명이 표시됩니다.",
                10,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                descriptionBodyColor);
            descriptionBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            descriptionBody.verticalOverflow = VerticalWrapMode.Truncate;
            SetAnchors(descriptionBody.rectTransform, new Vector2(0.035f, 0.08f), new Vector2(0.97f, 0.54f));

            Transform choices = screenInner.Find("PrizeChoices");
            if (choices != null)
                descriptionBar.SetSiblingIndex(Mathf.Min(screenInner.childCount - 1, choices.GetSiblingIndex() + 1));
        }
        else
        {
            descriptionGroup ??= descriptionBar.GetComponent<CanvasGroup>();
            descriptionAccent ??= descriptionBar.Find("Accent")?.GetComponent<Image>();
        }
    }

    private void UpdateDescription(int activeIndex)
    {
        if (descriptionBar == null || descriptionTitle == null || descriptionBody == null)
            return;

        bool valid = runManager != null &&
                     activeIndex >= 0 &&
                     activeIndex < runManager.CurrentRewardChoices.Count;
        BattleEquipmentSO reward = valid ? runManager.CurrentRewardChoices[activeIndex] : null;

        if (reward == null)
        {
            descriptionTitle.text = "SELECT AN ITEM";
            descriptionBody.text = "카드를 가리키거나 선택하면 아이템 설명이 표시됩니다.";
            if (descriptionGroup != null)
                descriptionGroup.alpha = 0.58f;
            if (descriptionAccent != null)
                descriptionAccent.color = new Color(descriptionAccentColor.r, descriptionAccentColor.g, descriptionAccentColor.b, 0.30f);
            return;
        }

        descriptionTitle.text = reward.GetDisplayName();
        descriptionBody.text = BuildRewardDescription(reward);
        if (descriptionGroup != null)
            descriptionGroup.alpha = 1f;
        if (descriptionAccent != null)
            descriptionAccent.color = descriptionAccentColor;
    }

    private static string BuildRewardDescription(BattleEquipmentSO reward)
    {
        if (reward == null)
            return string.Empty;

        string stats =
            $"{reward.rarity.ToString().ToUpperInvariant()} / {reward.type.ToString().ToUpperInvariant()}   " +
            $"DMG ×{reward.damageMultiplier:0.00}   MOVE ×{reward.moveSpeedMultiplier:0.00}   RANGE ×{reward.rangeMultiplier:0.00}";

        string tagLine = string.Empty;
        if (reward.tags != null && reward.tags.Count > 0)
        {
            List<string> names = new();
            for (int i = 0; i < reward.tags.Count; i++)
                names.Add(reward.tags[i].ToString().ToUpperInvariant());
            tagLine = "   //   " + string.Join(" · ", names);
        }

        if (!string.IsNullOrWhiteSpace(reward.description))
            return reward.description.Trim() + "\n" + stats + tagLine;

        return stats + tagLine;
    }

    private void HideLegacyFocusTexts()
    {
        if (battleHud == null)
            return;

        Text legacyName = focusedRewardNameField?.GetValue(battleHud) as Text;
        Text legacyStats = focusedRewardStatsField?.GetValue(battleHud) as Text;
        if (legacyName != null && legacyName.gameObject.activeSelf)
            legacyName.gameObject.SetActive(false);
        if (legacyStats != null && legacyStats.gameObject.activeSelf)
            legacyStats.gameObject.SetActive(false);
    }

    private void RestoreLegacyFocusTexts()
    {
        if (battleHud == null)
            return;

        Text legacyName = focusedRewardNameField?.GetValue(battleHud) as Text;
        Text legacyStats = focusedRewardStatsField?.GetValue(battleHud) as Text;
        if (legacyName != null)
            legacyName.gameObject.SetActive(true);
        if (legacyStats != null)
            legacyStats.gameObject.SetActive(true);
    }

    private void EnsureLockedOverlay()
    {
        if (screenInner == null)
            return;

        if (lockedOverlay == null)
        {
            Transform existing = screenInner.Find("RewardSelectionLockedStatic");
            lockedOverlay = existing as RectTransform;
        }

        if (lockedOverlay != null)
        {
            lockedGroup ??= lockedOverlay.GetComponent<CanvasGroup>();
            return;
        }

        lockedOverlay = CreateRect(screenInner, "RewardSelectionLockedStatic", Vector2.zero);
        Stretch(lockedOverlay);

        Image blackout = lockedOverlay.gameObject.AddComponent<Image>();
        blackout.color = new Color(0.006f, 0.008f, 0.012f, lockBackgroundAlpha);
        blackout.raycastTarget = true;

        lockedGroup = lockedOverlay.gameObject.AddComponent<CanvasGroup>();
        lockedGroup.alpha = 0f;
        lockedGroup.interactable = false;
        lockedGroup.blocksRaycasts = false;

        RectTransform staticRect = CreateRect(lockedOverlay, "StaticNoise", Vector2.zero);
        Stretch(staticRect);
        lockedStaticImage = staticRect.gameObject.AddComponent<RawImage>();
        lockedStaticImage.color = Color.white;
        lockedStaticImage.raycastTarget = false;

        Shader shader = Shader.Find(AnalogShaderName);
        if (shader == null)
            shader = Resources.Load<Shader>("BattleAnalogTvOverlay");
        if (shader != null)
        {
            lockedStaticMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            lockedStaticImage.material = lockedStaticMaterial;
        }

        lockedSweepBar = CreateRect(lockedOverlay, "StaticSweepBar", Vector2.zero);
        lockedSweepBar.anchorMin = new Vector2(0f, 0.5f);
        lockedSweepBar.anchorMax = new Vector2(1f, 0.5f);
        lockedSweepBar.sizeDelta = new Vector2(0f, 16f);
        lockedSweepImage = lockedSweepBar.gameObject.AddComponent<Image>();
        lockedSweepImage.color = new Color(0.86f, 0.94f, 1f, 0.24f);
        lockedSweepImage.raycastTarget = false;

        lockedTitle = CreateText(
            lockedOverlay,
            "SELECTION LOCKED",
            34,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Color(1f, 0.84f, 0.18f, 1f));
        SetAnchors(lockedTitle.rectTransform, new Vector2(0.12f, 0.52f), new Vector2(0.88f, 0.68f));

        lockedMessage = CreateText(
            lockedOverlay,
            "더 이상 아이템을 선택할 수 없습니다.\nPACK 정리는 계속 가능합니다  //  DONE / START로 확정",
            15,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Color(0.90f, 0.94f, 0.98f, 1f));
        SetAnchors(lockedMessage.rectTransform, new Vector2(0.10f, 0.34f), new Vector2(0.90f, 0.52f));

        lockedOverlay.SetAsLastSibling();
        lockedOverlay.gameObject.SetActive(false);
    }

    private void BeginSelectionLock()
    {
        hoveredRewardIndex = -1;
        lockStartedAt = Time.unscaledTime;
        if (lockedOverlay != null)
        {
            lockedOverlay.gameObject.SetActive(true);
            lockedOverlay.SetAsLastSibling();
        }
        if (lockedGroup != null)
        {
            lockedGroup.alpha = 0f;
            lockedGroup.blocksRaycasts = true;
        }
    }

    private void UpdateLockedStatic(bool locked)
    {
        if (lockedOverlay == null || lockedGroup == null)
            return;

        if (!locked)
        {
            SetLockedImmediate(false);
            return;
        }

        if (!lockedOverlay.gameObject.activeSelf)
            BeginSelectionLock();

        float elapsed = Mathf.Max(0f, Time.unscaledTime - lockStartedAt);
        float fade = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, lockFadeDuration));
        lockedGroup.alpha = fade;
        lockedGroup.blocksRaycasts = true;

        float burst = 1f - Mathf.Clamp01(elapsed / Mathf.Max(0.05f, lockBurstDuration));
        float jitter = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 91f);

        if (lockedStaticMaterial != null)
        {
            lockedStaticMaterial.SetFloat("_Strength", 1f);
            lockedStaticMaterial.SetFloat("_NoiseStrength", lockNoiseStrength + burst * 0.16f * jitter);
            lockedStaticMaterial.SetFloat("_ScanlineStrength", lockScanlineStrength + burst * 0.08f);
            lockedStaticMaterial.SetFloat("_ScanlineSpacing", burst > 0.1f ? 2f : 3f);
            lockedStaticMaterial.SetFloat("_RollingBandStrength", lockRollingBandStrength + burst * 0.10f);
            lockedStaticMaterial.SetColor("_OverlayTint", new Color(0.68f, 0.76f, 0.82f, 1f));
        }

        if (lockedSweepBar != null)
        {
            float y = Mathf.Lerp(-245f, 245f, Mathf.Repeat(elapsed * 4.6f, 1f));
            lockedSweepBar.anchoredPosition = new Vector2(0f, y);
        }
        if (lockedSweepImage != null)
        {
            float alpha = 0.10f + (burst * 0.34f) + jitter * 0.08f;
            lockedSweepImage.color = new Color(0.86f, 0.94f, 1f, Mathf.Clamp01(alpha));
        }

        if (lockedTitle != null)
        {
            float x = burst > 0.01f ? Mathf.Sin(Time.unscaledTime * 117f) * 5f * burst : 0f;
            lockedTitle.rectTransform.anchoredPosition = new Vector2(x, 0f);
        }
    }

    private void SetLockedImmediate(bool visible)
    {
        if (lockedOverlay != null)
            lockedOverlay.gameObject.SetActive(visible);
        if (lockedGroup != null)
        {
            lockedGroup.alpha = visible ? 1f : 0f;
            lockedGroup.blocksRaycasts = visible;
        }
        if (!visible)
            lockStartedAt = -1f;
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

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static Text CreateText(
        Transform parent,
        string value,
        int fontSize,
        FontStyle style,
        TextAnchor alignment,
        Color color)
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

internal sealed class BattleRewardSelectionVisualPointer : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private BattleRewardSelectionPresentationController owner;
    private int rewardIndex;

    public void Configure(BattleRewardSelectionPresentationController controller, int index)
    {
        owner = controller;
        rewardIndex = index;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        owner?.HandleCardHover(rewardIndex, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.HandleCardHover(rewardIndex, false);
    }
}

internal static class BattleRewardSelectionPresentationRuntimeInstaller
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        BattleSceneManager[] managers = Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null)
                continue;

            if (manager.GetComponent<BattleRewardSelectionPresentationController>() == null)
                manager.gameObject.AddComponent<BattleRewardSelectionPresentationController>();
        }
    }
}

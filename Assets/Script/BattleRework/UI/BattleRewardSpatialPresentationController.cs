using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward 카드의 공간 배치/Depth/Glass/Detail/Confirm 이동 연출만 담당합니다.
/// 선택/확정/Pack 데이터 변경은 BattleRewardCardActionController와 BattleRewardFlow가 계속 authoritative owner입니다.
/// </summary>
[DefaultExecutionOrder(61000)]
[DisallowMultipleComponent]
public sealed class BattleRewardSpatialPresentationController : MonoBehaviour
{
    private static BattleRewardSpatialPresentationController instance;

    [Header("Card Composition")]
    [SerializeField] private Vector2 cardSize = new(278f, 304f);
    [SerializeField, Min(120f)] private float cardSpacing = 252f;
    [SerializeField] private float presenterOpenBias = -34f;
    [SerializeField] private float idleVerticalStagger = 12f;
    [SerializeField] private float idleDepthStep = 6f;
    [SerializeField] private Vector3 selectedPosition = new(-160f, 8f, -26f);

    [Header("Motion")]
    [SerializeField, Min(0.05f)] private float stateTweenDuration = 0.24f;
    [SerializeField, Min(0.08f)] private float confirmTravelDuration = 0.42f;
    [SerializeField] private Ease stateEase = Ease.OutCubic;

    [Header("Detail")]
    [SerializeField] private Vector2 detailSize = new(350f, 252f);
    [SerializeField] private Vector3 detailPosition = new(174f, -2f, -30f);
    [SerializeField, Min(0f)] private float connectorThickness = 4f;

    private BattleRunManager runManager;
    private BattleRewardFlow rewardFlow;
    private BattleRewardCardActionController actionController;
    private BattleWorldScreenPresenter worldPresenter;

    private RectTransform rewardScreen;
    private RectTransform rewardInner;
    private RectTransform rewardCardRoot;
    private RectTransform detailRoot;
    private RectTransform connectorRoot;
    private BattleSpatialUIElement detailElement;
    private Text detailName;
    private Text detailGrade;
    private Text detailDescription;
    private Text detailEffects;
    private Text detailTags;

    private readonly List<CardVisual> cards = new();
    private RectTransform cachedCardRoot;
    private int cachedCardCount = -1;
    private BattleRewardPhase lastPhase = BattleRewardPhase.Inactive;
    private int lastSelectedIndex = -1;
    private int lastDetailIndex = -1;
    private bool transferActive;
    private float transferEndTime;
    private CanvasGroup lockedOverlayGroup;

    private sealed class CardVisual
    {
        public int index;
        public RectTransform rect;
        public BattleSpatialUIElement element;
        public BattleSpatialGlassPanel glass;
        public Image background;
        public Image icon;
        public Text nameText;
        public Text gradeText;
    }

    public static bool IsActiveFor(RectTransform cardRoot)
    {
        return instance != null &&
               instance.enabled &&
               instance.gameObject.activeInHierarchy &&
               cardRoot != null;
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

        instance = this;
        ResolveReferences();
        ResolveUi(true);
    }

    private void OnDisable()
    {
        HideDetail(true);
        RestoreLockedOverlay();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        ResolveReferences();
        ResolveUi(false);

        if (!IsRewardActive() || rewardFlow == null || rewardCardRoot == null)
        {
            lastPhase = BattleRewardPhase.Inactive;
            lastSelectedIndex = -1;
            transferActive = false;
            HideDetail(false);
            RestoreLockedOverlay();
            SetAll(SpatialUIState.Hidden);
            return;
        }

        BattleRewardPhase phase = rewardFlow.Phase;

        if (phase == BattleRewardPhase.Choosing)
        {
            if (rewardFlow.SelectedChoiceIndex >= 0)
                lastSelectedIndex = rewardFlow.SelectedChoiceIndex;
            else
                lastSelectedIndex = -1;

            transferActive = false;
            RestoreLockedOverlay();
            ApplyChoiceStates();
        }
        else if (phase == BattleRewardPhase.PackEditing)
        {
            if (lastPhase == BattleRewardPhase.Choosing)
                BeginConfirmTransfer(lastSelectedIndex);

            UpdateConfirmTransfer();
        }

        lastPhase = phase;
    }

    private void LateUpdate()
    {
        if (!IsRewardActive() || rewardFlow == null)
            return;

        if (rewardFlow.Phase == BattleRewardPhase.Choosing)
            UpdateConnector();
        else if (rewardFlow.Phase == BattleRewardPhase.PackEditing)
            UpdateLockedOverlayForTransfer();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (rewardFlow == null)
            rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        if (actionController == null)
            actionController = GetComponent<BattleRewardCardActionController>() ??
                               FindFirstObjectByType<BattleRewardCardActionController>(FindObjectsInactive.Include);
        if (worldPresenter == null)
            worldPresenter = FindFirstObjectByType<BattleWorldScreenPresenter>(FindObjectsInactive.Include);
    }

    private void ResolveUi(bool force)
    {
        RectTransform resolvedScreen = rewardScreen;
        if (resolvedScreen == null)
            resolvedScreen = FindRect("PrizeSelectionScreen");

        if (resolvedScreen != rewardScreen)
        {
            rewardScreen = resolvedScreen;
            rewardInner = null;
            rewardCardRoot = null;
            detailRoot = null;
            connectorRoot = null;
            detailElement = null;
            lockedOverlayGroup = null;
            cachedCardRoot = null;
            cachedCardCount = -1;
            force = true;
        }

        if (rewardScreen == null)
            return;

        rewardInner ??= rewardScreen.Find("ScreenInner") as RectTransform;
        if (rewardInner == null)
            return;

        RectTransform resolvedCards = FindChildByName(rewardInner, "PrizeChoices");
        if (resolvedCards != rewardCardRoot)
        {
            rewardCardRoot = resolvedCards;
            force = true;
        }

        int childCount = rewardCardRoot != null ? rewardCardRoot.childCount : -1;
        if (force || rewardCardRoot != cachedCardRoot || childCount != cachedCardCount)
            BindCards();

        EnsureDetailSurface();
    }

    private void BindCards()
    {
        cards.Clear();
        cachedCardRoot = rewardCardRoot;
        cachedCardCount = rewardCardRoot != null ? rewardCardRoot.childCount : -1;
        lastDetailIndex = -1;

        if (rewardCardRoot == null)
            return;

        rewardCardRoot.anchorMin = new Vector2(0.06f, 0.20f);
        rewardCardRoot.anchorMax = new Vector2(0.94f, 0.79f);
        rewardCardRoot.offsetMin = Vector2.zero;
        rewardCardRoot.offsetMax = Vector2.zero;
        rewardCardRoot.pivot = new Vector2(0.5f, 0.5f);

        int count = rewardCardRoot.childCount;
        for (int childIndex = 0; childIndex < count; childIndex++)
        {
            RectTransform rect = rewardCardRoot.GetChild(childIndex) as RectTransform;
            if (rect == null)
                continue;

            RewardPrizeDrag marker = rect.GetComponent<RewardPrizeDrag>();
            int stableIndex = marker != null ? marker.RewardIndex : childIndex;
            BattleEquipmentSO equipment = GetReward(stableIndex);

            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = cardSize;

            BattleSpatialUIElement element = rect.GetComponent<BattleSpatialUIElement>();
            if (element == null)
                element = rect.gameObject.AddComponent<BattleSpatialUIElement>();
            element.SetTransition(stateTweenDuration, stateEase, true);

            ConfigureCardPoses(element, childIndex, count);

            BattleSpatialGlassPanel glass = rect.GetComponent<BattleSpatialGlassPanel>();
            if (glass == null)
                glass = rect.gameObject.AddComponent<BattleSpatialGlassPanel>();
            glass.Configure(childIndex % 2 == 0, 0.11f, childIndex % 2 == 0 ? 0.035f : -0.035f);
            glass.SetSpatialState(0.10f, -0.05f);

            Image background = rect.GetComponent<Image>();
            if (background != null)
            {
                Color c = background.color;
                c.a = 0.10f;
                background.color = c;
            }

            Image icon = rect.Find("PrizeIcon")?.GetComponent<Image>();
            Text name = null;
            Text grade = null;
            Text[] texts = rect.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                Text text = texts[i];
                if (text == null || equipment == null)
                    continue;

                if (text.text == equipment.GetDisplayName())
                    name = text;
                else if (text.text == equipment.rarity.ToString().ToUpperInvariant() ||
                         (text.text ?? string.Empty).Contains(equipment.rarity.ToString().ToUpperInvariant()))
                    grade ??= text;
            }

            ApplyChildDepth(icon != null ? icon.rectTransform : null, -8f);
            ApplyChildDepth(name != null ? name.rectTransform : null, -10f);
            ApplyChildDepth(grade != null ? grade.rectTransform : null, -12f);

            cards.Add(new CardVisual
            {
                index = stableIndex,
                rect = rect,
                element = element,
                glass = glass,
                background = background,
                icon = icon,
                nameText = name,
                gradeText = grade
            });

            element.SnapToState(SpatialUIState.Hidden);
        }

        cards.Sort((a, b) => a.index.CompareTo(b.index));
    }

    private void ConfigureCardPoses(BattleSpatialUIElement element, int visualIndex, int count)
    {
        float center = (count - 1) * 0.5f;
        float offset = visualIndex - center;
        float x = offset * cardSpacing + presenterOpenBias;
        float y = (visualIndex % 2 == 0 ? 1f : -1f) * idleVerticalStagger;
        float z = Mathf.Abs(offset) * idleDepthStep;
        float yRotation = -offset * 4.0f;
        float xRotation = visualIndex % 2 == 0 ? 1.8f : -1.2f;
        float zRotation = visualIndex % 2 == 0 ? -0.6f : 0.6f;

        SpatialUIPose idle = new(
            new Vector3(x, y, z),
            new Vector3(xRotation, yRotation, zRotation),
            new Vector3(0.96f, 0.96f, 1f),
            0.92f);

        SpatialUIPose hover = new(
            new Vector3(x, y + 14f, z - 14f),
            new Vector3(xRotation * 0.35f, yRotation * 0.35f, zRotation * 0.25f),
            new Vector3(1.015f, 1.015f, 1f),
            1f);

        SpatialUIPose selected = new(
            selectedPosition,
            Vector3.zero,
            new Vector3(1.06f, 1.06f, 1f),
            1f);

        SpatialUIPose disabled = new(
            new Vector3(x + Mathf.Sign(offset) * 20f, y - 8f, z + 24f),
            new Vector3(xRotation + 1f, yRotation * 1.2f, zRotation),
            new Vector3(0.94f, 0.94f, 1f),
            0.46f);

        SpatialUIPose hidden = new(
            new Vector3(x, y - 10f, z + 34f),
            new Vector3(xRotation + 3f, yRotation, zRotation),
            new Vector3(0.90f, 0.90f, 1f),
            0f);

        element.SetPose(SpatialUIState.Idle, idle);
        element.SetPose(SpatialUIState.Hovered, hover);
        element.SetPose(SpatialUIState.Focused, hover);
        element.SetPose(SpatialUIState.Selected, selected);
        element.SetPose(SpatialUIState.Disabled, disabled);
        element.SetPose(SpatialUIState.Hidden, hidden);
        element.SetPose(SpatialUIState.Exiting, hidden);
    }

    private void ApplyChoiceStates()
    {
        int selectedIndex = rewardFlow.SelectedChoiceIndex;
        bool hasSelection = rewardFlow.SelectedChoice != null;
        int hoveredIndex = actionController != null ? actionController.HoveredRewardIndex : -1;

        BattleUIThemeProfile theme = BattleUIThemeController.Instance != null
            ? BattleUIThemeController.Instance.CurrentProfile
            : null;

        CardVisual selected = null;
        for (int i = 0; i < cards.Count; i++)
        {
            CardVisual card = cards[i];
            if (card == null || card.element == null)
                continue;

            SpatialUIState state;
            if (hasSelection)
                state = card.index == selectedIndex ? SpatialUIState.Selected : SpatialUIState.Disabled;
            else
                state = card.index == hoveredIndex ? SpatialUIState.Hovered : SpatialUIState.Idle;

            card.element.SetState(state);

            if (card.glass != null)
            {
                if (state == SpatialUIState.Selected)
                    card.glass.SetSpatialState(1f, 0.85f);
                else if (state == SpatialUIState.Hovered)
                    card.glass.SetSpatialState(0.72f, 0.45f);
                else if (state == SpatialUIState.Disabled)
                    card.glass.SetSpatialState(0.02f, -0.80f);
                else
                    card.glass.SetSpatialState(0.18f, -0.05f);
            }

            if (card.background != null)
            {
                Color surface = theme != null
                    ? theme.surface
                    : new Color(0.12f, 0.13f, 0.15f, 1f);
                surface.a = state switch
                {
                    SpatialUIState.Selected => 0.20f,
                    SpatialUIState.Hovered => 0.15f,
                    SpatialUIState.Disabled => 0.07f,
                    _ => 0.10f
                };
                card.background.color = surface;
            }

            if (card.icon != null)
            {
                card.icon.color = state == SpatialUIState.Disabled
                    ? new Color(0.72f, 0.75f, 0.80f, 0.68f)
                    : Color.white;
            }

            if (card.index == selectedIndex)
                selected = card;
        }

        if (hasSelection && selected != null)
        {
            ShowDetail(selected);
            RefreshDetail(selectedIndex);
        }
        else
        {
            HideDetail(false);
        }
    }

    private void BeginConfirmTransfer(int confirmedIndex)
    {
        HideDetail(false);

        CardVisual confirmed = FindCard(confirmedIndex);
        for (int i = 0; i < cards.Count; i++)
        {
            CardVisual card = cards[i];
            if (card == null || card.element == null)
                continue;

            if (card != confirmed)
            {
                card.element.SetState(SpatialUIState.Hidden);
                continue;
            }

            Vector3 target = ResolveConfirmTargetLocal();
            SpatialUIPose exitPose = new(
                target,
                Vector3.zero,
                new Vector3(0.82f, 0.82f, 1f),
                0.10f);

            card.element.SetTransition(confirmTravelDuration, Ease.InOutCubic, true);
            card.element.SetPose(SpatialUIState.Exiting, exitPose);
            card.element.SetState(SpatialUIState.Exiting);

            if (card.glass != null)
                card.glass.SetSpatialState(0f, -1f);

            if (card.background != null)
            {
                Color c = card.background.color;
                c.a = 0.025f;
                card.background.color = c;
            }
        }

        transferActive = confirmed != null;
        transferEndTime = Time.unscaledTime + confirmTravelDuration;
        UpdateLockedOverlayForTransfer();
    }

    private void UpdateConfirmTransfer()
    {
        if (!transferActive)
        {
            SetAll(SpatialUIState.Hidden);
            RestoreLockedOverlay();
            return;
        }

        if (Time.unscaledTime < transferEndTime)
            return;

        transferActive = false;
        SetAll(SpatialUIState.Hidden);
        RestoreLockedOverlay();
    }

    private Vector3 ResolveConfirmTargetLocal()
    {
        if (rewardCardRoot == null)
            return new Vector3(330f, -170f, -34f);

        if (rewardFlow != null && rewardFlow.ChosenRewardCommitted && rewardFlow.ChosenRewardSlot >= 0)
        {
            RectTransform equipmentDock = FindRect("EquipmentDock");
            RectTransform slot = equipmentDock != null
                ? equipmentDock.Find($"Slot_{rewardFlow.ChosenRewardSlot + 1}") as RectTransform
                : null;
            if (slot != null && TryConvertToCardRoot(slot, out Vector2 local))
                return new Vector3(local.x, local.y, -34f);
        }

        float width = Mathf.Max(1f, rewardCardRoot.rect.width);
        float height = Mathf.Max(1f, rewardCardRoot.rect.height);
        return new Vector3(width * 0.38f, -height * 0.34f, -36f);
    }

    private bool TryConvertToCardRoot(RectTransform target, out Vector2 local)
    {
        local = Vector2.zero;
        if (rewardCardRoot == null || target == null)
            return false;

        Canvas targetCanvas = target.GetComponentInParent<Canvas>();
        Canvas cardCanvas = rewardCardRoot.GetComponentInParent<Canvas>();

        Camera targetCamera = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? targetCanvas.worldCamera != null ? targetCanvas.worldCamera : Camera.main
            : null;
        Camera cardCamera = cardCanvas != null && cardCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? cardCanvas.worldCamera != null ? cardCanvas.worldCamera : Camera.main
            : null;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(targetCamera, target.position);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rewardCardRoot,
            screenPoint,
            cardCamera,
            out local);
    }

    private void EnsureDetailSurface()
    {
        if (rewardInner == null)
            return;

        RectTransform targetParent = worldPresenter != null && worldPresenter.GetFocusLayer() != null
            ? worldPresenter.GetFocusLayer()
            : rewardInner;

        if (detailRoot != null && detailRoot.parent != targetParent)
        {
            detailRoot.SetParent(targetParent, false);
            connectorRoot?.SetParent(targetParent, false);
        }

        if (detailRoot != null)
            return;

        GameObject detail = new("RewardSpatialDetailSurface");
        detail.transform.SetParent(targetParent, false);
        detailRoot = detail.AddComponent<RectTransform>();
        detailRoot.anchorMin = detailRoot.anchorMax = new Vector2(0.5f, 0.5f);
        detailRoot.pivot = new Vector2(0.5f, 0.5f);
        detailRoot.sizeDelta = detailSize;

        Image detailHitSurface = detail.AddComponent<Image>();
        detailHitSurface.color = new Color(1f, 1f, 1f, 0.012f);
        detailHitSurface.raycastTarget = false;

        BattleSpatialGlassPanel glass = detail.AddComponent<BattleSpatialGlassPanel>();
        glass.Configure(false, 0.12f, -0.035f);
        glass.SetSpatialState(0.48f, 0.62f);

        detailElement = detail.AddComponent<BattleSpatialUIElement>();
        detailElement.SetTransition(stateTweenDuration, stateEase, true);
        detailElement.SetPose(
            SpatialUIState.Hidden,
            new SpatialUIPose(
                detailPosition + new Vector3(24f, -6f, 18f),
                new Vector3(2f, -4f, 0f),
                new Vector3(0.94f, 0.94f, 1f),
                0f));
        detailElement.SetPose(
            SpatialUIState.Focused,
            new SpatialUIPose(
                detailPosition,
                new Vector3(0.5f, -1.5f, 0f),
                Vector3.one,
                1f));
        detailElement.SnapToState(SpatialUIState.Hidden);

        detailGrade = CreateText(detailRoot, "GRADE", 10, FontStyle.Bold, TextAnchor.MiddleLeft);
        BindTheme(detailGrade, BattleUIThemeColorRole.Key);
        SetAnchors(detailGrade.rectTransform, new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.94f));

        detailName = CreateText(detailRoot, "ITEM", 24, FontStyle.Bold, TextAnchor.MiddleLeft);
        BindTheme(detailName, BattleUIThemeColorRole.TextPrimary);
        SetAnchors(detailName.rectTransform, new Vector2(0.08f, 0.66f), new Vector2(0.94f, 0.84f));

        detailDescription = CreateText(detailRoot, string.Empty, 11, FontStyle.Normal, TextAnchor.UpperLeft);
        BindTheme(detailDescription, BattleUIThemeColorRole.TextPrimary);
        detailDescription.horizontalOverflow = HorizontalWrapMode.Wrap;
        detailDescription.verticalOverflow = VerticalWrapMode.Truncate;
        SetAnchors(detailDescription.rectTransform, new Vector2(0.08f, 0.39f), new Vector2(0.92f, 0.65f));

        detailEffects = CreateText(detailRoot, string.Empty, 10, FontStyle.Bold, TextAnchor.UpperLeft);
        BindTheme(detailEffects, BattleUIThemeColorRole.TextPrimary);
        detailEffects.horizontalOverflow = HorizontalWrapMode.Wrap;
        detailEffects.verticalOverflow = VerticalWrapMode.Truncate;
        SetAnchors(detailEffects.rectTransform, new Vector2(0.08f, 0.15f), new Vector2(0.92f, 0.38f));

        detailTags = CreateText(detailRoot, string.Empty, 9, FontStyle.Bold, TextAnchor.MiddleLeft);
        BindTheme(detailTags, BattleUIThemeColorRole.TextMuted);
        SetAnchors(detailTags.rectTransform, new Vector2(0.08f, 0.04f), new Vector2(0.92f, 0.15f));

        GameObject connector = new("RewardDetailConnector");
        connector.transform.SetParent(targetParent, false);
        connectorRoot = connector.AddComponent<RectTransform>();
        connectorRoot.anchorMin = connectorRoot.anchorMax = new Vector2(0.5f, 0.5f);
        connectorRoot.pivot = new Vector2(0f, 0.5f);
        Image connectorImage = connector.AddComponent<Image>();
        connectorImage.raycastTarget = false;
        BattleUIThemeColorBinding binding = connector.AddComponent<BattleUIThemeColorBinding>();
        binding.Configure(BattleUIThemeColorRole.KeySoft, 0.82f);
        connector.SetActive(false);
    }

    private void ShowDetail(CardVisual selected)
    {
        if (detailElement == null || detailRoot == null || selected == null)
            return;

        detailRoot.gameObject.SetActive(true);
        detailElement.SetState(SpatialUIState.Focused);
        if (connectorRoot != null)
            connectorRoot.gameObject.SetActive(true);
    }

    private void HideDetail(bool immediate)
    {
        if (detailElement != null)
            detailElement.SetState(SpatialUIState.Hidden, immediate);

        if (connectorRoot != null)
            connectorRoot.gameObject.SetActive(false);
        lastDetailIndex = -1;
    }

    private void RefreshDetail(int rewardIndex)
    {
        if (rewardIndex == lastDetailIndex)
            return;

        BattleEquipmentSO equipment = GetReward(rewardIndex);
        if (equipment == null)
            return;

        lastDetailIndex = rewardIndex;

        if (detailName != null)
            detailName.text = equipment.GetDisplayName();
        if (detailGrade != null)
            detailGrade.text = $"{equipment.rarity.ToString().ToUpperInvariant()}  /  {equipment.type.ToString().ToUpperInvariant()}";
        if (detailDescription != null)
        {
            detailDescription.text = !string.IsNullOrWhiteSpace(equipment.description)
                ? equipment.description.Trim()
                : equipment.shootingData != null ? "Manual weapon." : "Equipment item.";
        }
        if (detailEffects != null)
        {
            detailEffects.text =
                $"DAMAGE  ×{equipment.damageMultiplier:0.00}\n" +
                $"MOVE    ×{equipment.moveSpeedMultiplier:0.00}\n" +
                $"RANGE   ×{equipment.rangeMultiplier:0.00}";
        }
        if (detailTags != null)
            detailTags.text = BuildTagLine(equipment);
    }

    private void UpdateConnector()
    {
        if (connectorRoot == null || detailRoot == null || !connectorRoot.gameObject.activeSelf)
            return;

        CardVisual selected = FindCard(rewardFlow != null ? rewardFlow.SelectedChoiceIndex : -1);
        if (selected == null || selected.rect == null)
        {
            connectorRoot.gameObject.SetActive(false);
            return;
        }

        RectTransform parent = connectorRoot.parent as RectTransform;
        if (parent == null)
            return;

        Vector3 fromWorld = selected.rect.TransformPoint(new Vector3(selected.rect.rect.xMax, 0f, 0f));
        Vector3 toWorld = detailRoot.TransformPoint(new Vector3(detailRoot.rect.xMin, 0f, 0f));
        Vector2 from = parent.InverseTransformPoint(fromWorld);
        Vector2 to = parent.InverseTransformPoint(toWorld);
        Vector2 delta = to - from;

        if (delta.sqrMagnitude < 1f)
            return;

        connectorRoot.anchoredPosition = from;
        connectorRoot.sizeDelta = new Vector2(delta.magnitude, connectorThickness);
        connectorRoot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private void UpdateLockedOverlayForTransfer()
    {
        ResolveLockedOverlay();
        if (lockedOverlayGroup != null)
            lockedOverlayGroup.alpha = transferActive ? 0f : 1f;
    }

    private void RestoreLockedOverlay()
    {
        ResolveLockedOverlay();
        if (lockedOverlayGroup != null)
            lockedOverlayGroup.alpha = 1f;
    }

    private void ResolveLockedOverlay()
    {
        if (lockedOverlayGroup != null || rewardInner == null)
            return;

        RectTransform locked = FindChildByName(rewardInner, "RewardSelectionLockedOverlay");
        if (locked != null)
            lockedOverlayGroup = locked.GetComponent<CanvasGroup>();
    }

    private void SetAll(SpatialUIState state)
    {
        for (int i = 0; i < cards.Count; i++)
            cards[i]?.element?.SetState(state);
    }

    private CardVisual FindCard(int index)
    {
        for (int i = 0; i < cards.Count; i++)
            if (cards[i] != null && cards[i].index == index)
                return cards[i];
        return null;
    }

    private BattleEquipmentSO GetReward(int index)
    {
        if (runManager == null || index < 0 || index >= runManager.CurrentRewardChoices.Count)
            return null;
        return runManager.CurrentRewardChoices[index];
    }

    private bool IsRewardActive()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
    }

    private static void ApplyChildDepth(RectTransform rect, float z)
    {
        if (rect == null)
            return;

        Vector3 local = rect.localPosition;
        local.z = z;
        rect.localPosition = local;
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

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = FindObjectsByType<RectTransform>(
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

    private static RectTransform FindChildByName(Transform root, string objectName)
    {
        if (root == null)
            return null;

        RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }

        return null;
    }

    private static Text CreateText(Transform parent, string value, int fontSize, FontStyle style, TextAnchor alignment)
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
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void BindTheme(Text text, BattleUIThemeColorRole role)
    {
        if (text == null)
            return;

        BattleUIThemeColorBinding binding = text.GetComponent<BattleUIThemeColorBinding>();
        if (binding == null)
            binding = text.gameObject.AddComponent<BattleUIThemeColorBinding>();
        binding.Configure(role);
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

using System.Text;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// PACK / Combat Tab의 공용 장비 상세 View입니다.
///
/// Phase 7 소유권 규칙:
/// - 어떤 슬롯을 Inspect할지는 BattleUnifiedInventoryInspectController가 결정합니다.
/// - Reward 후보 선택/설명은 BattleRewardCardActionController가 결정합니다.
/// - 이 클래스는 전달받은 Equipment/Slot 내용을 렌더링하는 View 역할만 합니다.
/// - 다른 Controller의 private field를 Reflection으로 읽지 않습니다.
/// - 슬롯/Reward 카드에 입력 Relay를 자동 설치하지 않습니다.
///
/// 공개 API:
/// - ShowSlot(slotIndex)
/// - PreviewReward(rewardIndex)
/// - ClearRewardPreview()
/// - Hide()
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30920)]
public sealed class BattleEquipmentDetailPanelController : MonoBehaviour
{
    private const int CanvasSortingOrder = 1660;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleGridSynergyController gridSynergy;

    [Header("Panel")]
    [SerializeField] private Vector2 panelSize = new(430f, 570f);

    [Header("Theme")]
    [SerializeField] private Color inkColor = new(0.025f, 0.022f, 0.040f, 0.985f);
    [SerializeField] private Color paperColor = new(0.95f, 0.91f, 0.78f, 1f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Color accentCyan = new(0.14f, 0.92f, 0.94f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.16f, 0.50f, 1f);

    private Canvas canvas;
    private RectTransform root;
    private CanvasGroup group;
    private Image icon;
    private Text slotLabel;
    private Text title;
    private Text rarityType;
    private Text stateLabel;
    private Text description;
    private Text statText;
    private Text tagText;
    private Text synergyTitle;
    private Text synergyText;

    private BattleEquipmentSystem subscribedEquipmentSystem;
    private BattleGridSynergyController subscribedGridSynergy;

    private int displayedSlot = -1;
    private int previewRewardIndex = -1;
    private bool rewardPreviewActive;

    public RectTransform Root => root;
    public CanvasGroup Group => group;
    public int DisplayedSlot => displayedSlot;
    public bool IsRewardPreviewActive => rewardPreviewActive;

    private void Awake()
    {
        ResolveReferences();
        EnsureUi();
        EnsureSubscriptions();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureUi();
        EnsureSubscriptions();
    }

    private void OnDisable()
    {
        Hide();
        Unsubscribe();
    }

    private void Update()
    {
        // Runtime 설치 순서 때문에 첫 Awake에서 System을 찾지 못한 경우만 보완합니다.
        if (equipmentSystem == null || gridSynergy == null)
        {
            ResolveReferences();
            EnsureSubscriptions();
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (gridSynergy == null)
            gridSynergy = FindFirstObjectByType<BattleGridSynergyController>();
    }

    private void EnsureSubscriptions()
    {
        if (subscribedEquipmentSystem != equipmentSystem)
        {
            if (subscribedEquipmentSystem != null)
            {
                subscribedEquipmentSystem.InventoryChanged -= HandleEquipmentChanged;
                subscribedEquipmentSystem.SlotCapacityChanged -= HandleSlotCapacityChanged;
                subscribedEquipmentSystem.EquippedSlotChanged -= HandleEquippedSlotChanged;
            }

            subscribedEquipmentSystem = equipmentSystem;
            if (subscribedEquipmentSystem != null)
            {
                subscribedEquipmentSystem.InventoryChanged += HandleEquipmentChanged;
                subscribedEquipmentSystem.SlotCapacityChanged += HandleSlotCapacityChanged;
                subscribedEquipmentSystem.EquippedSlotChanged += HandleEquippedSlotChanged;
            }
        }

        if (subscribedGridSynergy != gridSynergy)
        {
            if (subscribedGridSynergy != null)
                subscribedGridSynergy.GridSynergiesChanged -= HandleGridSynergiesChanged;

            subscribedGridSynergy = gridSynergy;
            if (subscribedGridSynergy != null)
                subscribedGridSynergy.GridSynergiesChanged += HandleGridSynergiesChanged;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedEquipmentSystem != null)
        {
            subscribedEquipmentSystem.InventoryChanged -= HandleEquipmentChanged;
            subscribedEquipmentSystem.SlotCapacityChanged -= HandleSlotCapacityChanged;
            subscribedEquipmentSystem.EquippedSlotChanged -= HandleEquippedSlotChanged;
        }

        if (subscribedGridSynergy != null)
            subscribedGridSynergy.GridSynergiesChanged -= HandleGridSynergiesChanged;

        subscribedEquipmentSystem = null;
        subscribedGridSynergy = null;
    }

    private void HandleEquipmentChanged()
    {
        RefreshCurrentContent();
    }

    private void HandleSlotCapacityChanged(int _)
    {
        RefreshCurrentContent();
    }

    private void HandleEquippedSlotChanged(int _)
    {
        RefreshCurrentContent();
    }

    private void HandleGridSynergiesChanged()
    {
        RefreshCurrentContent();
    }

    /// <summary>
    /// PACK / Full Grid 슬롯을 상세 패널에 표시합니다.
    /// 선택 상태의 소유권은 호출자에게 있으며, 이 메서드는 슬롯을 선택하지 않습니다.
    /// </summary>
    public bool ShowSlot(int slotIndex)
    {
        ResolveReferences();
        EnsureUi();
        EnsureSubscriptions();

        if (!TryGetEquipment(slotIndex, out BattleEquipmentSlot runtimeSlot, out BattleEquipmentSO equipment))
        {
            Hide();
            return false;
        }

        rewardPreviewActive = false;
        previewRewardIndex = -1;
        displayedSlot = slotIndex;
        RefreshPanel(slotIndex, runtimeSlot, equipment);
        return true;
    }

    /// <summary>
    /// PACK에 아직 들어가지 않은 Reward SO를 임시 Preview합니다.
    /// 현재 Reward UI는 카드 내부 상세를 사용하므로 기본 흐름에서는 호출하지 않습니다.
    /// </summary>
    public bool PreviewReward(int rewardIndex)
    {
        ResolveReferences();
        EnsureUi();

        if (!TryGetReward(rewardIndex, out BattleEquipmentSO equipment))
        {
            ClearRewardPreview();
            return false;
        }

        displayedSlot = -1;
        previewRewardIndex = rewardIndex;
        rewardPreviewActive = true;
        RefreshRewardPreview(rewardIndex, equipment);
        return true;
    }

    public void ClearRewardPreview()
    {
        if (!rewardPreviewActive)
            return;

        rewardPreviewActive = false;
        previewRewardIndex = -1;
        displayedSlot = -1;
    }

    public void Hide()
    {
        displayedSlot = -1;
        previewRewardIndex = -1;
        rewardPreviewActive = false;

        // Visibility의 최종 Layout/Context 판단은 UnifiedInventory가 담당하지만,
        // 비활성화/명시적 Hide 시 잔상이 남지 않도록 한 번만 즉시 숨깁니다.
        if (group != null)
            group.alpha = 0f;
    }

    /// <summary>
    /// Phase 4~6 호출부 호환용 wrapper입니다.
    /// 신규 코드는 ShowSlot / Hide를 직접 사용합니다.
    /// </summary>
    public void SelectSlotFromPointer(int slotIndex)
    {
        if (slotIndex >= 0)
            ShowSlot(slotIndex);
        else
            Hide();
    }

    /// <summary>
    /// 구형 Reward Hover 호출부 호환용 wrapper입니다.
    /// 신규 Reward 선택 화면은 BattleRewardCardActionController의 카드 내부 상세를 사용합니다.
    /// </summary>
    internal void SetRewardPreviewHover(int rewardIndex, bool entered)
    {
        if (entered)
            PreviewReward(rewardIndex);
        else if (rewardPreviewActive && previewRewardIndex == rewardIndex)
            ClearRewardPreview();
    }

    private void RefreshCurrentContent()
    {
        if (rewardPreviewActive)
        {
            if (TryGetReward(previewRewardIndex, out BattleEquipmentSO reward))
                RefreshRewardPreview(previewRewardIndex, reward);
            else
                ClearRewardPreview();
            return;
        }

        if (displayedSlot < 0)
            return;

        if (TryGetEquipment(displayedSlot, out BattleEquipmentSlot runtimeSlot, out BattleEquipmentSO equipment))
            RefreshPanel(displayedSlot, runtimeSlot, equipment);
        else
            Hide();
    }

    private bool TryGetReward(int rewardIndex, out BattleEquipmentSO equipment)
    {
        equipment = null;
        if (runManager == null || !runManager.RunActive || runManager.State != BattleRunState.Reward ||
            rewardIndex < 0 || rewardIndex >= runManager.CurrentRewardChoices.Count)
            return false;

        equipment = runManager.CurrentRewardChoices[rewardIndex];
        return equipment != null;
    }

    private bool TryGetEquipment(int slotIndex, out BattleEquipmentSlot runtimeSlot, out BattleEquipmentSO equipment)
    {
        runtimeSlot = null;
        equipment = null;

        if (equipmentSystem == null || slotIndex < 0 || slotIndex >= equipmentSystem.Slots.Count ||
            !equipmentSystem.IsSlotUnlocked(slotIndex))
            return false;

        runtimeSlot = equipmentSystem.Slots[slotIndex];
        equipment = runtimeSlot?.equipment;
        return equipment != null;
    }

    private void RefreshPanel(int slotIndex, BattleEquipmentSlot runtimeSlot, BattleEquipmentSO equipment)
    {
        if (equipment == null || runtimeSlot == null)
            return;

        if (slotLabel != null)
        {
            Vector2Int grid = BattleEquipmentSystem.SlotIndexToGrid(slotIndex);
            slotLabel.text = $"PACK // {grid.x + 1}-{grid.y + 1}";
        }

        ApplyCommonEquipmentInfo(equipment);

        if (stateLabel != null)
        {
            bool equipped = equipmentSystem != null && equipmentSystem.EquippedSlotIndex == slotIndex;
            stateLabel.text = equipped
                ? $"ACTIVE  //  LV.{runtimeSlot.grade}"
                : $"LV.{runtimeSlot.grade}  //  COPY {runtimeSlot.copies}/3";
            stateLabel.color = equipped ? accentYellow : accentCyan;
        }

        if (synergyTitle != null)
            synergyTitle.text = "GRID LINK";
        if (synergyText != null)
            synergyText.text = BuildSynergies(slotIndex);
    }

    private void RefreshRewardPreview(int rewardIndex, BattleEquipmentSO equipment)
    {
        if (equipment == null)
            return;

        if (slotLabel != null)
            slotLabel.text = $"REWARD // {rewardIndex + 1}";

        ApplyCommonEquipmentInfo(equipment);

        if (stateLabel != null)
        {
            stateLabel.text = "LV.1  //  PREVIEW";
            stateLabel.color = accentCyan;
        }

        if (synergyTitle != null)
            synergyTitle.text = "PACK / MERGE";
        if (synergyText != null)
            synergyText.text = BuildRewardPackHint(equipment);
    }

    private void ApplyCommonEquipmentInfo(BattleEquipmentSO equipment)
    {
        if (equipment == null)
            return;

        if (title != null)
            title.text = equipment.GetDisplayName().ToUpperInvariant();

        if (rarityType != null)
            rarityType.text = $"{equipment.rarity.ToString().ToUpperInvariant()}  /  {equipment.type.ToString().ToUpperInvariant()}";

        if (icon != null)
        {
            icon.sprite = equipment.icon;
            icon.enabled = equipment.icon != null;
        }

        if (description != null)
        {
            description.text = !string.IsNullOrWhiteSpace(equipment.description)
                ? equipment.description.Trim()
                : BuildFallbackDescription(equipment);
        }

        if (statText != null)
        {
            statText.text =
                $"DMG      ×{equipment.damageMultiplier:0.00}\n" +
                $"MOVE     ×{equipment.moveSpeedMultiplier:0.00}\n" +
                $"RANGE    ×{equipment.rangeMultiplier:0.00}";
        }

        if (tagText != null)
            tagText.text = BuildTags(equipment);
    }

    private static string BuildFallbackDescription(BattleEquipmentSO equipment)
    {
        if (equipment == null)
            return string.Empty;

        StringBuilder sb = new();
        if (equipment.shootingData != null)
            sb.Append("Manual weapon. ");
        else
            sb.Append("Passive equipment. ");

        if (equipment.damageMultiplier > 1.001f)
            sb.Append($"Damage +{(equipment.damageMultiplier - 1f) * 100f:0}%. ");
        if (equipment.moveSpeedMultiplier > 1.001f)
            sb.Append($"Move +{(equipment.moveSpeedMultiplier - 1f) * 100f:0}%. ");
        if (equipment.rangeMultiplier > 1.001f)
            sb.Append($"Range +{(equipment.rangeMultiplier - 1f) * 100f:0}%. ");

        if (sb.Length == 0)
            sb.Append("No additional description has been authored yet.");
        return sb.ToString().Trim();
    }

    private static string BuildTags(BattleEquipmentSO equipment)
    {
        if (equipment == null || equipment.tags == null || equipment.tags.Count == 0)
            return "NO TAG";

        StringBuilder sb = new();
        for (int i = 0; i < equipment.tags.Count; i++)
        {
            if (i > 0)
                sb.Append("  •  ");
            sb.Append(equipment.tags[i].ToString().ToUpperInvariant());
        }
        return sb.ToString();
    }

    private string BuildSynergies(int slotIndex)
    {
        if (gridSynergy == null || gridSynergy.ActiveLinks == null)
            return "NO ACTIVE GRID LINK";

        StringBuilder sb = new();
        for (int i = 0; i < gridSynergy.ActiveLinks.Count; i++)
        {
            BattleGridSynergyLink link = gridSynergy.ActiveLinks[i];
            if (link == null || (link.slotA != slotIndex && link.slotB != slotIndex))
                continue;

            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append("+ ");
            sb.Append(link.displayName);
            if (link.damageMultiplier > 1.001f)
                sb.Append($"   ×{link.damageMultiplier:0.000}");
        }

        return sb.Length > 0 ? sb.ToString() : "NO ACTIVE GRID LINK";
    }

    private string BuildRewardPackHint(BattleEquipmentSO equipment)
    {
        if (equipmentSystem == null || equipment == null)
            return "PLACE INTO PACK TO ACQUIRE";

        for (int i = 0; i < equipmentSystem.Slots.Count; i++)
        {
            BattleEquipmentSlot slot = equipmentSystem.Slots[i];
            if (slot == null || slot.equipment != equipment)
                continue;

            return $"SAME ITEM IN PACK  //  LV.{slot.grade}  COPY {slot.copies}/3\nPLACE INTO PACK TO MERGE / STORE";
        }

        return "NEW ITEM  //  PLACE INTO PACK TO ACQUIRE";
    }

    private void EnsureUi()
    {
        if (canvas != null)
            return;

        GameObject canvasObject = new("BattleEquipmentDetailCanvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = CanvasSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root = CreateRect(canvas.transform, "EquipmentDetailPanel", panelSize);
        root.anchorMin = root.anchorMax = new Vector2(1f, 0.5f);
        root.pivot = new Vector2(1f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.localRotation = Quaternion.identity;

        Image back = root.gameObject.AddComponent<Image>();
        back.color = inkColor;
        back.raycastTarget = false;

        Outline outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = accentCyan;
        outline.effectDistance = new Vector2(5f, -5f);

        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        BuildInkAccents();
        BuildHeader();
        BuildBody();
    }

    private void BuildInkAccents()
    {
        RectTransform pinkSlash = CreateRect(root, "PinkSlash", new Vector2(22f, 146f));
        pinkSlash.anchorMin = pinkSlash.anchorMax = new Vector2(0f, 0.82f);
        pinkSlash.anchoredPosition = new Vector2(-8f, 0f);
        pinkSlash.localRotation = Quaternion.Euler(0f, 0f, 10f);
        Image pink = pinkSlash.gameObject.AddComponent<Image>();
        pink.color = accentPink;
        pink.raycastTarget = false;

        RectTransform cyanStroke = CreateRect(root, "CyanStroke", new Vector2(286f, 7f));
        cyanStroke.anchorMin = cyanStroke.anchorMax = new Vector2(1f, 1f);
        cyanStroke.pivot = new Vector2(1f, 1f);
        cyanStroke.anchoredPosition = new Vector2(-18f, -18f);
        cyanStroke.localRotation = Quaternion.Euler(0f, 0f, -1.4f);
        Image cyan = cyanStroke.gameObject.AddComponent<Image>();
        cyan.color = accentCyan;
        cyan.raycastTarget = false;

        CreateCornerTab(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 18f));
        CreateCornerTab(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-18f, 18f));
    }

    private void CreateCornerTab(Vector2 anchor, Vector2 pivot, Vector2 position)
    {
        RectTransform tab = CreateRect(root, "CornerTab", new Vector2(28f, 28f));
        tab.anchorMin = tab.anchorMax = anchor;
        tab.pivot = pivot;
        tab.anchoredPosition = position;
        Image image = tab.gameObject.AddComponent<Image>();
        image.color = accentYellow;
        image.raycastTarget = false;
    }

    private void BuildHeader()
    {
        RectTransform headerTag = CreateRect(root, "HeaderTag", new Vector2(170f, 42f));
        headerTag.anchorMin = headerTag.anchorMax = new Vector2(0f, 1f);
        headerTag.pivot = new Vector2(0f, 1f);
        headerTag.anchoredPosition = new Vector2(22f, -24f);
        headerTag.localRotation = Quaternion.Euler(0f, 0f, -3.2f);
        Image tagBack = headerTag.gameObject.AddComponent<Image>();
        tagBack.color = accentYellow;
        tagBack.raycastTarget = false;

        Text header = CreateText(headerTag, "ITEM // DATA", 15, FontStyle.Bold, TextAnchor.MiddleCenter, inkColor);
        Stretch(header.rectTransform);

        slotLabel = CreateText(root, "PACK // 1-1", 10, FontStyle.Bold, TextAnchor.MiddleRight, accentCyan);
        SetAnchors(slotLabel.rectTransform, new Vector2(0.54f, 0.90f), new Vector2(0.94f, 0.96f));

        icon = CreateImage(root, "EquipmentIcon", new Vector2(104f, 104f));
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.18f, 0.75f);
        icon.rectTransform.anchoredPosition = Vector2.zero;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        RectTransform iconBack = CreateRect(root, "IconInkBack", new Vector2(126f, 116f));
        iconBack.anchorMin = iconBack.anchorMax = new Vector2(0.18f, 0.75f);
        iconBack.anchoredPosition = Vector2.zero;
        iconBack.localRotation = Quaternion.Euler(0f, 0f, -4f);
        Image ib = iconBack.gameObject.AddComponent<Image>();
        ib.color = new Color(accentPink.r, accentPink.g, accentPink.b, 0.22f);
        ib.raycastTarget = false;
        icon.transform.SetAsLastSibling();

        title = CreateText(root, "ITEM NAME", 24, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
        SetAnchors(title.rectTransform, new Vector2(0.36f, 0.72f), new Vector2(0.94f, 0.84f));

        rarityType = CreateText(root, "COMMON / MANUAL", 11, FontStyle.Bold, TextAnchor.MiddleLeft, accentCyan);
        SetAnchors(rarityType.rectTransform, new Vector2(0.36f, 0.65f), new Vector2(0.94f, 0.72f));

        stateLabel = CreateText(root, "ACTIVE // LV.1", 10, FontStyle.Bold, TextAnchor.MiddleLeft, accentYellow);
        SetAnchors(stateLabel.rectTransform, new Vector2(0.36f, 0.59f), new Vector2(0.94f, 0.65f));
    }

    private void BuildBody()
    {
        RectTransform descBack = CreateRect(root, "DescriptionBack", Vector2.zero);
        SetAnchors(descBack, new Vector2(0.07f, 0.39f), new Vector2(0.93f, 0.57f));
        Image db = descBack.gameObject.AddComponent<Image>();
        db.color = new Color(0f, 0f, 0f, 0.42f);
        db.raycastTarget = false;

        description = CreateText(descBack, "DESCRIPTION", 13, FontStyle.Normal, TextAnchor.UpperLeft, paperColor);
        SetAnchors(description.rectTransform, new Vector2(0.05f, 0.10f), new Vector2(0.95f, 0.90f));
        description.horizontalOverflow = HorizontalWrapMode.Wrap;
        description.verticalOverflow = VerticalWrapMode.Truncate;

        Text statHeader = CreateText(root, "PARAMETERS", 11, FontStyle.Bold, TextAnchor.MiddleLeft, accentYellow);
        SetAnchors(statHeader.rectTransform, new Vector2(0.07f, 0.335f), new Vector2(0.45f, 0.385f));

        statText = CreateText(root, "DMG\nMOVE\nRANGE", 13, FontStyle.Bold, TextAnchor.UpperLeft, paperColor);
        SetAnchors(statText.rectTransform, new Vector2(0.07f, 0.17f), new Vector2(0.45f, 0.335f));

        Text tagHeader = CreateText(root, "TAGS", 11, FontStyle.Bold, TextAnchor.MiddleLeft, accentPink);
        SetAnchors(tagHeader.rectTransform, new Vector2(0.50f, 0.335f), new Vector2(0.91f, 0.385f));

        tagText = CreateText(root, "NO TAG", 11, FontStyle.Bold, TextAnchor.UpperLeft, paperColor);
        SetAnchors(tagText.rectTransform, new Vector2(0.50f, 0.245f), new Vector2(0.93f, 0.335f));
        tagText.horizontalOverflow = HorizontalWrapMode.Wrap;
        tagText.verticalOverflow = VerticalWrapMode.Truncate;

        synergyTitle = CreateText(root, "GRID LINK", 11, FontStyle.Bold, TextAnchor.MiddleLeft, accentCyan);
        SetAnchors(synergyTitle.rectTransform, new Vector2(0.50f, 0.19f), new Vector2(0.91f, 0.24f));

        synergyText = CreateText(root, "NO ACTIVE GRID LINK", 11, FontStyle.Bold, TextAnchor.UpperLeft, paperColor);
        SetAnchors(synergyText.rectTransform, new Vector2(0.50f, 0.055f), new Vector2(0.93f, 0.19f));
        synergyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        synergyText.verticalOverflow = VerticalWrapMode.Truncate;

        RectTransform bottomStroke = CreateRect(root, "BottomStroke", Vector2.zero);
        SetAnchors(bottomStroke, new Vector2(0.07f, 0.035f), new Vector2(0.93f, 0.045f));
        Image bs = bottomStroke.gameObject.AddComponent<Image>();
        bs.color = accentYellow;
        bs.raycastTarget = false;
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
        image.preserveAspect = true;
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

public static class BattleEquipmentDetailPanelAutoInstaller
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

            if (manager.GetComponent<BattleEquipmentDetailPanelController>() != null)
                continue;

            Undo.AddComponent<BattleEquipmentDetailPanelController>(manager.gameObject);
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
            if (manager != null && manager.GetComponent<BattleEquipmentDetailPanelController>() == null)
                manager.gameObject.AddComponent<BattleEquipmentDetailPanelController>();
        }
    }
}

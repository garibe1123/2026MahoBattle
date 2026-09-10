using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// PACK / Combat Tab / Reward 후보에서 현재 Inspect 대상의 상세 정보를 공용 패널로 표시합니다.
///
/// 원칙:
/// - 아무 아이템도 선택/호버하지 않았으면 패널을 숨깁니다.
/// - Reward 후보는 PointerEnter 동안만 기존 PACK 상세 패널을 그대로 Preview로 사용합니다.
/// - Reward Hover는 카드의 위치/크기/회전/Sibling 순서를 절대 변경하지 않습니다.
/// - Reward PACK / Combat Tab에서는 실제 선택된 슬롯을 표시합니다.
/// - 장착/시너지 상태만으로는 패널을 열지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30920)]
public sealed class BattleEquipmentDetailPanelController : MonoBehaviour
{
    private const int CanvasSortingOrder = 1660;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleGridSynergyController gridSynergy;
    [SerializeField] private BattleInventoryInteractionController inventoryInteraction;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;

    [Header("Panel")]
    [SerializeField] private Vector2 panelSize = new(430f, 570f);
    [SerializeField] private Vector2 visibleOffset = new(-34f, 0f);
    [SerializeField] private Vector2 hiddenOffset = new(54f, 0f);
    [SerializeField, Min(1f)] private float animationSharpness = 18f;

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

    // 다른 Inventory 컨트롤러가 reflection으로 읽고 있으므로 필드명은 유지합니다.
    private int manualSelectedSlot = -1;
    private int hoveredRewardIndex = -1;
    private int renderedSlot = -999;
    private int renderedRewardIndex = -999;
    private bool renderedRewardPreview;
    private bool renderedVisible;
    private BattleRunState lastState = (BattleRunState)(-1);
    private float nextRelayInstall;

    private FieldInfo rewardSelectedSlotField;
    private FieldInfo rewardPadSelectedSlotField;
    private FieldInfo rewardPadModeField;
    private FieldInfo loadoutSelectedIndexField;
    private FieldInfo loadoutSwitchHeldField;
    private FieldInfo loadoutBoardShownField;

    private void Awake()
    {
        ResolveReferences();
        CacheReflection();
        EnsureUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheReflection();
        EnsureUi();
        nextRelayInstall = 0f;
    }

    private void OnDisable()
    {
        hoveredRewardIndex = -1;
        manualSelectedSlot = -1;
        renderedSlot = -999;
        renderedRewardIndex = -999;
        renderedRewardPreview = false;
        renderedVisible = false;
        if (group != null)
            group.alpha = 0f;
    }

    private void Update()
    {
        ResolveReferences();
        EnsureUi();

        if (runManager != null && runManager.State != lastState)
        {
            manualSelectedSlot = -1;
            hoveredRewardIndex = -1;
            renderedSlot = -999;
            renderedRewardIndex = -999;
            renderedRewardPreview = false;
            lastState = runManager.State;
        }

        if (Time.unscaledTime >= nextRelayInstall)
        {
            nextRelayInstall = Time.unscaledTime + 0.12f;
            InstallInspectRelays();
        }

        // Reward 후보 Hover가 최우선 Inspect 대상입니다.
        // PACK에 넣기 전의 후보 SO이므로 실제 슬롯을 만들거나 데이터를 변경하지 않습니다.
        if (TryGetHoveredReward(out int rewardIndex, out BattleEquipmentSO reward))
        {
            if (!renderedRewardPreview || rewardIndex != renderedRewardIndex || !renderedVisible)
                RefreshRewardPreview(rewardIndex, reward);

            renderedRewardPreview = true;
            renderedRewardIndex = rewardIndex;
            renderedSlot = -999;
            renderedVisible = true;
            Animate(true);
            return;
        }

        renderedRewardPreview = false;
        renderedRewardIndex = -999;

        bool contextVisible = IsRewardContext() || IsCombatTabContext();
        if (!contextVisible)
            manualSelectedSlot = -1;

        int slot = contextVisible ? ResolveSelectedSlot() : -1;
        bool hasItem = TryGetEquipment(slot, out BattleEquipmentSlot runtimeSlot, out BattleEquipmentSO equipment);
        bool visible = contextVisible && hasItem;

        if (visible && (slot != renderedSlot || !renderedVisible))
            RefreshPanel(slot, runtimeSlot, equipment);

        renderedSlot = visible ? slot : -999;
        renderedVisible = visible;
        Animate(visible);
    }

    public void SelectSlotFromPointer(int slotIndex)
    {
        if (!IsRewardContext() && !IsCombatTabContext())
            return;

        if (!TryGetEquipment(slotIndex, out _, out _))
        {
            manualSelectedSlot = -1;
            renderedSlot = -999;
            return;
        }

        manualSelectedSlot = slotIndex;
        renderedSlot = -999;
    }

    internal void SetRewardPreviewHover(int rewardIndex, bool entered)
    {
        if (!IsRewardChoiceContext())
        {
            hoveredRewardIndex = -1;
            return;
        }

        if (entered)
        {
            if (TryGetReward(rewardIndex, out _))
            {
                hoveredRewardIndex = rewardIndex;
                renderedRewardIndex = -999;
            }
            return;
        }

        if (hoveredRewardIndex == rewardIndex)
        {
            hoveredRewardIndex = -1;
            renderedRewardIndex = -999;
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
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>();
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>();
    }

    private void CacheReflection()
    {
        rewardSelectedSlotField ??= typeof(BattleInventoryInteractionController).GetField("selectedRewardSlot", PrivateInstance);
        rewardPadSelectedSlotField ??= typeof(BattleInventoryInteractionController).GetField("padSelectedSlot", PrivateInstance);
        rewardPadModeField ??= typeof(BattleInventoryInteractionController).GetField("padModeActive", PrivateInstance);

        loadoutSelectedIndexField ??= typeof(BattleKineticLoadoutUI).GetField("selectedIndex", PrivateInstance);
        loadoutSwitchHeldField ??= typeof(BattleKineticLoadoutUI).GetField("switchHeld", PrivateInstance);
        loadoutBoardShownField ??= typeof(BattleKineticLoadoutUI).GetField("boardWasShown", PrivateInstance);
    }

    private bool IsRewardContext()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
    }

    private bool IsRewardChoiceContext()
    {
        return IsRewardContext() &&
               (inventoryInteraction == null || !inventoryInteraction.IsRewardPackEditing);
    }

    private bool IsCombatTabContext()
    {
        if (runManager == null || !runManager.RunActive || runManager.State != BattleRunState.Combat || kineticLoadout == null)
            return false;

        bool held = ReadBool(loadoutSwitchHeldField, kineticLoadout);
        bool shown = ReadBool(loadoutBoardShownField, kineticLoadout);
        if (held && shown)
            return true;

        RectTransform full = FindRect("LoadoutSwitchFull");
        if (full == null || !full.gameObject.activeInHierarchy)
            return false;

        CanvasGroup cg = full.GetComponent<CanvasGroup>();
        return cg != null && cg.alpha > 0.10f;
    }

    private int ResolveSelectedSlot()
    {
        if (IsRewardContext())
        {
            if (manualSelectedSlot >= 0 && TryGetEquipment(manualSelectedSlot, out _, out _))
                return manualSelectedSlot;

            if (inventoryInteraction != null)
            {
                int selected = ReadInt(rewardSelectedSlotField, inventoryInteraction, -1);
                if (selected >= 0 && TryGetEquipment(selected, out _, out _))
                    return selected;

                bool padMode = ReadBool(rewardPadModeField, inventoryInteraction);
                int padSelected = ReadInt(rewardPadSelectedSlotField, inventoryInteraction, -1);
                if (padMode && padSelected >= 0 && TryGetEquipment(padSelected, out _, out _))
                    return padSelected;
            }

            return -1;
        }

        if (IsCombatTabContext())
        {
            if (manualSelectedSlot >= 0 && TryGetEquipment(manualSelectedSlot, out _, out _))
                return manualSelectedSlot;

            if (kineticLoadout != null)
            {
                int selected = ReadInt(loadoutSelectedIndexField, kineticLoadout, -1);
                if (selected >= 0 && TryGetEquipment(selected, out _, out _))
                    return selected;
            }
        }

        return -1;
    }

    private bool TryGetHoveredReward(out int rewardIndex, out BattleEquipmentSO equipment)
    {
        rewardIndex = hoveredRewardIndex;
        equipment = null;

        if (!IsRewardChoiceContext() || rewardIndex < 0)
            return false;

        return TryGetReward(rewardIndex, out equipment);
    }

    private bool TryGetReward(int rewardIndex, out BattleEquipmentSO equipment)
    {
        equipment = null;
        if (runManager == null || rewardIndex < 0 || rewardIndex >= runManager.CurrentRewardChoices.Count)
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
            stateLabel.text = "LV.1  //  HOVER PREVIEW";
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
            return "DROP INTO PACK TO ACQUIRE";

        for (int i = 0; i < equipmentSystem.Slots.Count; i++)
        {
            BattleEquipmentSlot slot = equipmentSystem.Slots[i];
            if (slot == null || slot.equipment != equipment)
                continue;

            return $"SAME ITEM IN PACK  //  LV.{slot.grade}  COPY {slot.copies}/3\nDROP INTO PACK TO MERGE / PLACE";
        }

        return "NEW ITEM  //  DROP INTO PACK TO ACQUIRE";
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
        root.anchoredPosition = hiddenOffset;
        root.localRotation = Quaternion.Euler(0f, 0f, 0.7f);

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

    private void Animate(bool visible)
    {
        if (root == null || group == null)
            return;

        float t = 1f - Mathf.Exp(-Mathf.Max(1f, animationSharpness) * Time.unscaledDeltaTime);
        group.alpha = Mathf.Lerp(group.alpha, visible ? 1f : 0f, t);
        root.anchoredPosition = Vector2.Lerp(root.anchoredPosition, visible ? visibleOffset : hiddenOffset, t);

        if (!visible && group.alpha < 0.002f)
            group.alpha = 0f;
    }

    private void InstallInspectRelays()
    {
        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            AttachSlotRelay(FindRect($"BackpackCell_{i}"), i);
            AttachSlotRelay(FindRect($"GridSlot_{i}"), i);
        }

        RewardPrizeDrag[] rewardCards = UnityEngine.Object.FindObjectsByType<RewardPrizeDrag>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < rewardCards.Length; i++)
        {
            RewardPrizeDrag drag = rewardCards[i];
            if (drag == null)
                continue;

            BattleRewardInspectHoverRelay relay = drag.GetComponent<BattleRewardInspectHoverRelay>();
            if (relay == null)
                relay = drag.gameObject.AddComponent<BattleRewardInspectHoverRelay>();
            relay.Configure(this, drag.RewardIndex);
        }
    }

    private void AttachSlotRelay(RectTransform rect, int slotIndex)
    {
        if (rect == null)
            return;

        Image image = rect.GetComponent<Image>();
        if (image != null)
            image.raycastTarget = true;

        BattleEquipmentInspectClickRelay relay = rect.GetComponent<BattleEquipmentInspectClickRelay>();
        if (relay == null)
            relay = rect.gameObject.AddComponent<BattleEquipmentInspectClickRelay>();
        relay.Configure(this, slotIndex);
    }

    private static int ReadInt(FieldInfo field, object owner, int fallback)
    {
        if (field == null || owner == null)
            return fallback;
        object value = field.GetValue(owner);
        return value is int result ? result : fallback;
    }

    private static bool ReadBool(FieldInfo field, object owner)
    {
        if (field == null || owner == null)
            return false;
        object value = field.GetValue(owner);
        return value is bool result && result;
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

internal sealed class BattleEquipmentInspectClickRelay : MonoBehaviour, IPointerClickHandler
{
    private BattleEquipmentDetailPanelController owner;
    private int slotIndex;

    public void Configure(BattleEquipmentDetailPanelController controller, int index)
    {
        owner = controller;
        slotIndex = index;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            owner?.SelectSlotFromPointer(slotIndex);
    }
}

internal sealed class BattleRewardInspectHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private BattleEquipmentDetailPanelController owner;
    private int rewardIndex;

    public void Configure(BattleEquipmentDetailPanelController controller, int index)
    {
        owner = controller;
        rewardIndex = index;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        owner?.SetRewardPreviewHover(rewardIndex, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.SetRewardPreviewHover(rewardIndex, false);
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
        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(
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

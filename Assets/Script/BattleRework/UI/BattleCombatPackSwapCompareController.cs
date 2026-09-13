using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Combat TAB의 PACK Drag & Drop 위치 교환을 Reward Compare Detail과 같은 문법으로 보여줍니다.
///
/// 데이터 소유권:
/// - 실제 위치 교환은 기존 BattleInventoryInteractionController -> BattleEquipmentSystem.SwapSlots 경로가 수행합니다.
/// - 이 클래스는 Drag source / Hover target을 읽고 비교 UI만 표시합니다.
/// - GridBoard / GridSlot RectTransform은 이동/Scale하지 않아 Pointer hitbox를 흔들지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(61040)]
public sealed class BattleCombatPackSwapCompareController : MonoBehaviour
{
    [Header("AUTO REFERENCES")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleInventoryInteractionController inventoryInteraction;
    [SerializeField] private BattleCombatHudInputBridge combatInput;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;

    [Header("COMPARE DETAIL")]
    [SerializeField] private Vector2 preferredPanelSize = new(900f, 610f);
    [SerializeField, Range(620f, 900f)] private float minimumPanelWidth = 720f;
    [SerializeField, Range(0.78f, 1.05f)] private float compareScale = 0.96f;
    [SerializeField, Range(16f, 70f)] private float screenMargin = 28f;

    [Header("THEME")]
    [SerializeField] private Color ink = new(0.024f, 0.025f, 0.034f, 0.995f);
    [SerializeField] private Color paper = new(0.95f, 0.92f, 0.80f, 1f);
    [SerializeField] private Color cyan = new(0.12f, 0.90f, 0.94f, 1f);
    [SerializeField] private Color yellow = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Color pink = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField] private Color muted = new(0.57f, 0.60f, 0.67f, 1f);

    private RectTransform compareRoot;
    private CanvasGroup compareGroup;
    private CanvasGroup detailGroup;

    private RectTransform bannerRoot;
    private Image bannerBack;
    private Outline bannerOutline;
    private Image badgeBack;
    private Text badgeText;
    private Text bannerHeadline;
    private Text bannerSlot;
    private Image bannerAccent;
    private Text actionLabel;

    private SideView sourceSide;
    private SideView targetSide;

    private BattleInventorySlotPointer[] slotPointers;
    private float nextResolveAt;
    private bool compareActive;
    private int lastSource = int.MinValue;
    private int lastTarget = int.MinValue;
    private BattleEquipmentSO lastSourceEquipment;
    private BattleEquipmentSO lastTargetEquipment;

    private sealed class SideView
    {
        public RectTransform root;
        public Image icon;
        public Text eyebrow;
        public Text name;
        public Text meta;
        public Text level;
        public Text description;
        public Text paramTitle;
        public Text stats;
        public Text tags;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TryInstall();
    }

    private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        TryInstall();
    }

    private static void TryInstall()
    {
        if (UnityEngine.Object.FindFirstObjectByType<BattleCombatPackSwapCompareController>(FindObjectsInactive.Include) != null)
            return;

        BattleInventoryInteractionController interaction =
            UnityEngine.Object.FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        BattleRunManager run = UnityEngine.Object.FindFirstObjectByType<BattleRunManager>(FindObjectsInactive.Include);

        GameObject host = interaction != null
            ? interaction.gameObject
            : run != null
                ? run.gameObject
                : GameObject.Find("BattleSystems");

        if (host != null)
            host.AddComponent<BattleCombatPackSwapCompareController>();
    }

    private void Awake()
    {
        ResolveReferences(true);
        ResolveUi(true);
        ResolveSlotPointers();
    }

    private void OnEnable()
    {
        ResolveReferences(true);
        ResolveUi(true);
        ResolveSlotPointers();
        nextResolveAt = 0f;
        compareActive = false;
        InvalidateSnapshot();
    }

    private void OnDisable()
    {
        HideCompareImmediate();
        RestoreDetailVisibility();
        ClearCombatSlotFrames();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextResolveAt)
            return;

        nextResolveAt = Time.unscaledTime + 0.15f;
        ResolveReferences(false);
        ResolveUi(false);

        if (slotPointers == null || slotPointers.Length == 0)
            ResolveSlotPointers();
    }

    private void LateUpdate()
    {
        ResolveUi(false);

        bool active = TryResolveSwapPreview(
            out int sourceIndex,
            out int targetIndex,
            out BattleEquipmentSlot sourceSlot,
            out BattleEquipmentSlot targetSlot);

        if (!active)
        {
            if (compareActive)
            {
                HideCompareImmediate();
                RestoreDetailVisibility();
                ClearCombatSlotFrames();
            }

            compareActive = false;
            InvalidateSnapshot();
            return;
        }

        if (!BindCompareUi())
            return;

        LayoutComparePanelInsideScreen();
        SuppressOriginalDetail();
        ShowCompareImmediate();
        RefreshIfChanged(sourceIndex, targetIndex, sourceSlot, targetSlot);
        ApplyCombatSlotFrames(sourceIndex, targetIndex, targetSlot != null && targetSlot.equipment != null);
        compareActive = true;
    }

    private bool TryResolveSwapPreview(
        out int sourceIndex,
        out int targetIndex,
        out BattleEquipmentSlot sourceSlot,
        out BattleEquipmentSlot targetSlot)
    {
        sourceIndex = -1;
        targetIndex = -1;
        sourceSlot = null;
        targetSlot = null;

        if (runManager == null || !runManager.RunActive || runManager.State != BattleRunState.Combat ||
            equipmentSystem == null || inventoryInteraction == null || combatInput == null ||
            kineticLoadout == null || !kineticLoadout.IsSwitchBoardOpen || !inventoryInteraction.IsDraggingItem)
            return false;

        sourceIndex = FindDraggingSlotIndex();
        targetIndex = combatInput.HoveredSlot;

        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex ||
            !equipmentSystem.IsSlotUnlocked(sourceIndex) || !equipmentSystem.IsSlotUnlocked(targetIndex))
            return false;

        if (!equipmentSystem.TryGetSlot(sourceIndex, out sourceSlot) ||
            sourceSlot == null || sourceSlot.equipment == null)
            return false;

        equipmentSystem.TryGetSlot(targetIndex, out targetSlot);
        return true;
    }

    private int FindDraggingSlotIndex()
    {
        if (slotPointers == null || slotPointers.Length == 0)
            ResolveSlotPointers();

        if (slotPointers == null)
            return -1;

        for (int i = 0; i < slotPointers.Length; i++)
        {
            BattleInventorySlotPointer pointer = slotPointers[i];
            if (pointer != null && pointer.IsDragging)
                return pointer.SlotIndex;
        }

        // Runtime install 직후 Pointer 배열이 오래된 경우 한 번만 즉시 갱신합니다.
        ResolveSlotPointers();
        for (int i = 0; i < slotPointers.Length; i++)
        {
            BattleInventorySlotPointer pointer = slotPointers[i];
            if (pointer != null && pointer.IsDragging)
                return pointer.SlotIndex;
        }

        return -1;
    }

    private void ResolveSlotPointers()
    {
        slotPointers = FindObjectsByType<BattleInventorySlotPointer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    private void RefreshIfChanged(
        int sourceIndex,
        int targetIndex,
        BattleEquipmentSlot sourceSlot,
        BattleEquipmentSlot targetSlot)
    {
        BattleEquipmentSO sourceEquipment = sourceSlot != null ? sourceSlot.equipment : null;
        BattleEquipmentSO targetEquipment = targetSlot != null ? targetSlot.equipment : null;

        if (sourceIndex == lastSource && targetIndex == lastTarget &&
            sourceEquipment == lastSourceEquipment && targetEquipment == lastTargetEquipment)
            return;

        lastSource = sourceIndex;
        lastTarget = targetIndex;
        lastSourceEquipment = sourceEquipment;
        lastTargetEquipment = targetEquipment;

        Vector2Int sourceGrid = BattleEquipmentSystem.SlotIndexToGrid(sourceIndex);
        Vector2Int targetGrid = BattleEquipmentSystem.SlotIndexToGrid(targetIndex);
        bool targetHasItem = targetEquipment != null;

        ApplyActionBanner(sourceGrid, targetGrid, targetHasItem);

        FillEquipmentSide(
            sourceSide,
            $"DRAGGED // FROM {sourceGrid.x + 1}-{sourceGrid.y + 1}",
            sourceEquipment,
            sourceSlot.grade,
            sourceSlot.copies,
            cyan,
            "SOURCE EFFECT SPEC");

        if (targetHasItem)
        {
            FillEquipmentSide(
                targetSide,
                $"TARGET // PACK {targetGrid.x + 1}-{targetGrid.y + 1}",
                targetEquipment,
                targetSlot.grade,
                targetSlot.copies,
                yellow,
                "TARGET EFFECT SPEC");
        }
        else
        {
            FillEmptyDestination(targetSide, targetGrid);
        }
    }

    private void ApplyActionBanner(Vector2Int sourceGrid, Vector2Int targetGrid, bool targetHasItem)
    {
        Color modeColor = targetHasItem ? yellow : cyan;

        if (bannerBack != null)
            bannerBack.color = new Color(modeColor.r, modeColor.g, modeColor.b, 0.13f);
        if (bannerOutline != null)
            bannerOutline.effectColor = new Color(modeColor.r, modeColor.g, modeColor.b, 0.96f);
        if (badgeBack != null)
            badgeBack.color = modeColor;
        if (badgeText != null)
        {
            badgeText.text = targetHasItem ? "SWAP POSITION" : "MOVE POSITION";
            badgeText.color = ink;
        }
        if (bannerHeadline != null)
        {
            bannerHeadline.text = targetHasItem
                ? "EXCHANGE THESE TWO PACK SLOTS  //  DROP TO CONFIRM"
                : "MOVE ITEM INTO EMPTY SLOT  //  DROP TO CONFIRM";
            bannerHeadline.color = paper;
        }
        if (bannerSlot != null)
        {
            bannerSlot.text = $"{sourceGrid.x + 1}-{sourceGrid.y + 1}  →  {targetGrid.x + 1}-{targetGrid.y + 1}";
            bannerSlot.color = modeColor;
        }
        if (bannerAccent != null)
            bannerAccent.color = modeColor;

        if (actionLabel != null)
        {
            actionLabel.text = targetHasItem ? "↔\nSWAP" : "→\nMOVE";
            actionLabel.color = targetHasItem ? pink : cyan;
            actionLabel.fontSize = 15;
            actionLabel.fontStyle = FontStyle.Bold;
        }
    }

    private void FillEquipmentSide(
        SideView side,
        string eyebrow,
        BattleEquipmentSO equipment,
        int grade,
        int copies,
        Color accent,
        string paramTitle)
    {
        if (side == null || equipment == null)
            return;

        SetSideActive(side, true);

        if (side.eyebrow != null)
        {
            side.eyebrow.text = eyebrow;
            side.eyebrow.color = accent;
        }
        if (side.icon != null)
        {
            side.icon.sprite = equipment.icon;
            side.icon.enabled = equipment.icon != null;
            side.icon.color = paper;
        }
        if (side.name != null)
        {
            side.name.text = equipment.GetDisplayName().ToUpperInvariant();
            side.name.color = paper;
        }
        if (side.meta != null)
        {
            side.meta.text = $"{equipment.rarity.ToString().ToUpperInvariant()}  /  {equipment.type.ToString().ToUpperInvariant()}";
            side.meta.color = accent;
        }
        if (side.level != null)
        {
            side.level.text = $"LV.{Mathf.Clamp(grade, 1, 3)}   COPY {Mathf.Clamp(copies, 1, 2)}/3";
            side.level.color = yellow;
        }
        if (side.description != null)
        {
            side.description.text = string.IsNullOrWhiteSpace(equipment.description)
                ? BuildFallbackDescription(equipment)
                : equipment.description.Trim();
            side.description.color = paper;
        }
        if (side.paramTitle != null)
        {
            side.paramTitle.text = paramTitle;
            side.paramTitle.color = accent;
        }
        if (side.stats != null)
        {
            side.stats.text = BuildStats(equipment);
            side.stats.color = paper;
            side.stats.fontSize = 13;
            side.stats.lineSpacing = 1.18f;
        }
        if (side.tags != null)
        {
            side.tags.text = BuildTags(equipment);
            side.tags.color = muted;
        }
    }

    private void FillEmptyDestination(SideView side, Vector2Int targetGrid)
    {
        if (side == null)
            return;

        SetSideActive(side, true);

        if (side.eyebrow != null)
        {
            side.eyebrow.text = $"DESTINATION // PACK {targetGrid.x + 1}-{targetGrid.y + 1}";
            side.eyebrow.color = cyan;
        }
        if (side.icon != null)
        {
            side.icon.sprite = null;
            side.icon.enabled = false;
        }
        if (side.name != null)
        {
            side.name.text = "EMPTY SLOT";
            side.name.color = paper;
        }
        if (side.meta != null)
        {
            side.meta.text = "AVAILABLE // POSITION";
            side.meta.color = cyan;
        }
        if (side.level != null)
        {
            side.level.text = "DROP HERE TO MOVE";
            side.level.color = cyan;
        }
        if (side.description != null)
        {
            side.description.text = "This PACK slot is empty. Dropping the dragged item here moves it without replacing another item.";
            side.description.color = paper;
        }
        if (side.paramTitle != null)
        {
            side.paramTitle.text = "POSITION RESULT";
            side.paramTitle.color = cyan;
        }
        if (side.stats != null)
        {
            side.stats.text = "ITEM      MOVES HERE\nSOURCE    BECOMES EMPTY\nSTATS     PRESERVED";
            side.stats.color = paper;
            side.stats.fontSize = 13;
            side.stats.lineSpacing = 1.18f;
        }
        if (side.tags != null)
        {
            side.tags.text = "DROP TO CONFIRM MOVE";
            side.tags.color = muted;
        }
    }

    private static string BuildStats(BattleEquipmentSO equipment)
    {
        if (equipment == null)
            return string.Empty;

        return $"DMG      ×{equipment.damageMultiplier:0.00}\n" +
               $"MOVE     ×{equipment.moveSpeedMultiplier:0.00}\n" +
               $"RANGE    ×{equipment.rangeMultiplier:0.00}";
    }

    private static string BuildTags(BattleEquipmentSO equipment)
    {
        if (equipment == null || equipment.tags == null || equipment.tags.Count == 0)
            return "TAGS  //  NONE";

        StringBuilder sb = new("TAGS  //  ");
        for (int i = 0; i < equipment.tags.Count; i++)
        {
            if (i > 0)
                sb.Append(" · ");
            sb.Append(equipment.tags[i].ToString().ToUpperInvariant());
        }
        return sb.ToString();
    }

    private static string BuildFallbackDescription(BattleEquipmentSO equipment)
    {
        if (equipment == null)
            return string.Empty;

        StringBuilder sb = new();
        sb.Append(equipment.shootingData != null ? "Manual weapon. " : "Passive equipment. ");
        if (equipment.damageMultiplier > 1.001f)
            sb.Append($"Damage +{(equipment.damageMultiplier - 1f) * 100f:0}%. ");
        if (equipment.moveSpeedMultiplier > 1.001f)
            sb.Append($"Move +{(equipment.moveSpeedMultiplier - 1f) * 100f:0}%. ");
        if (equipment.rangeMultiplier > 1.001f)
            sb.Append($"Range +{(equipment.rangeMultiplier - 1f) * 100f:0}%. ");
        return sb.ToString().Trim();
    }

    private void ResolveReferences(bool force)
    {
        if (force || runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (force || equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>(FindObjectsInactive.Include);
        if (force || inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        if (force || combatInput == null)
            combatInput = FindFirstObjectByType<BattleCombatHudInputBridge>(FindObjectsInactive.Include);
        if (force || detailController == null)
            detailController = FindFirstObjectByType<BattleEquipmentDetailPanelController>(FindObjectsInactive.Include);
        if (force || kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
    }

    private void ResolveUi(bool force)
    {
        RectTransform detailRoot = detailController != null ? detailController.Root : null;
        RectTransform parent = detailRoot != null ? detailRoot.parent as RectTransform : null;
        RectTransform resolved = parent != null ? parent.Find("RewardEquipmentCompareDetail") as RectTransform : null;

        if (!force && resolved == compareRoot)
            return;

        compareRoot = resolved;
        compareGroup = compareRoot != null ? compareRoot.GetComponent<CanvasGroup>() : null;
        detailGroup = detailController != null ? detailController.Group : null;
        sourceSide = null;
        targetSide = null;
        bannerRoot = null;
        bannerBack = null;
        bannerOutline = null;
        badgeBack = null;
        badgeText = null;
        bannerHeadline = null;
        bannerSlot = null;
        bannerAccent = null;
        actionLabel = null;

        if (compareRoot != null)
            BindCompareUi();
    }

    private bool BindCompareUi()
    {
        if (compareRoot == null)
            return false;

        if (compareGroup == null)
            compareGroup = compareRoot.GetComponent<CanvasGroup>();
        if (compareGroup == null)
            return false;

        Text oldHeader = FindText(compareRoot, "Header");
        Text oldSubHeader = FindText(compareRoot, "SubHeader");
        if (oldHeader != null)
            oldHeader.enabled = false;
        if (oldSubHeader != null)
            oldSubHeader.enabled = false;

        RectTransform currentRoot = compareRoot.Find("CurrentSide") as RectTransform;
        RectTransform incomingRoot = compareRoot.Find("IncomingSide") as RectTransform;
        if (currentRoot == null || incomingRoot == null)
            return false;

        sourceSide ??= BindSide(currentRoot);
        targetSide ??= BindSide(incomingRoot);
        actionLabel ??= FindText(compareRoot, "Action");

        EnsureActionBanner();
        return sourceSide != null && targetSide != null && bannerRoot != null;
    }

    private SideView BindSide(RectTransform root)
    {
        if (root == null)
            return null;

        RectTransform iconRect = root.Find("IconBack/Icon") as RectTransform;
        return new SideView
        {
            root = root,
            icon = iconRect != null ? iconRect.GetComponent<Image>() : null,
            eyebrow = FindText(root, "Eyebrow"),
            name = FindText(root, "Name"),
            meta = FindText(root, "Meta"),
            level = FindText(root, "Level"),
            description = FindText(root, "DescriptionBack/Description"),
            paramTitle = FindText(root, "ParamTitle"),
            stats = FindText(root, "Stats"),
            tags = FindText(root, "Tags")
        };
    }

    private void EnsureActionBanner()
    {
        if (compareRoot == null)
            return;

        bannerRoot = compareRoot.Find("ActionModeBanner") as RectTransform;
        if (bannerRoot == null)
        {
            bannerRoot = CreateRect(compareRoot, "ActionModeBanner", Vector2.zero);
            SetAnchors(bannerRoot, new Vector2(0.035f, 0.902f), new Vector2(0.965f, 0.985f));

            bannerBack = bannerRoot.gameObject.AddComponent<Image>();
            bannerBack.raycastTarget = false;

            bannerOutline = bannerRoot.gameObject.AddComponent<Outline>();
            bannerOutline.effectDistance = new Vector2(4f, -4f);

            RectTransform badge = CreateRect(bannerRoot, "ModeBadge", Vector2.zero);
            SetAnchors(badge, new Vector2(0.012f, 0.12f), new Vector2(0.245f, 0.88f));
            badgeBack = badge.gameObject.AddComponent<Image>();
            badgeBack.raycastTarget = false;

            badgeText = CreateText(badge, "SWAP POSITION", 20, FontStyle.Bold, TextAnchor.MiddleCenter, ink, "Text");
            SetAnchors(badgeText.rectTransform, Vector2.zero, Vector2.one);
            badgeText.resizeTextForBestFit = true;
            badgeText.resizeTextMinSize = 11;
            badgeText.resizeTextMaxSize = 20;

            bannerHeadline = CreateText(
                bannerRoot,
                "EXCHANGE THESE TWO PACK SLOTS  //  DROP TO CONFIRM",
                13,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                paper,
                "Headline");
            SetAnchors(bannerHeadline.rectTransform, new Vector2(0.27f, 0.12f), new Vector2(0.76f, 0.88f));
            bannerHeadline.resizeTextForBestFit = true;
            bannerHeadline.resizeTextMinSize = 9;
            bannerHeadline.resizeTextMaxSize = 13;

            bannerSlot = CreateText(bannerRoot, "1-1 → 1-2", 12, FontStyle.Bold, TextAnchor.MiddleRight, yellow, "Slot");
            SetAnchors(bannerSlot.rectTransform, new Vector2(0.76f, 0.12f), new Vector2(0.978f, 0.88f));

            RectTransform accent = CreateRect(bannerRoot, "Accent", Vector2.zero);
            SetAnchors(accent, new Vector2(0f, 0f), new Vector2(1f, 0.075f));
            bannerAccent = accent.gameObject.AddComponent<Image>();
            bannerAccent.raycastTarget = false;
        }
        else
        {
            bannerBack = bannerRoot.GetComponent<Image>();
            bannerOutline = bannerRoot.GetComponent<Outline>();
            RectTransform badge = bannerRoot.Find("ModeBadge") as RectTransform;
            if (badge != null)
            {
                badgeBack = badge.GetComponent<Image>();
                badgeText = FindText(badge, "Text");
            }
            bannerHeadline = FindText(bannerRoot, "Headline");
            bannerSlot = FindText(bannerRoot, "Slot");
            RectTransform accent = bannerRoot.Find("Accent") as RectTransform;
            bannerAccent = accent != null ? accent.GetComponent<Image>() : null;
        }
    }

    private void LayoutComparePanelInsideScreen()
    {
        if (compareRoot == null || compareRoot.parent is not RectTransform canvasRoot)
            return;

        float safeMargin = Mathf.Max(24f, screenMargin);
        float physicalAvailableWidth = Mathf.Max(520f, canvasRoot.rect.width - safeMargin * 2f);
        float dynamicMinimum = Mathf.Min(minimumPanelWidth, physicalAvailableWidth);
        float width = Mathf.Clamp(
            Mathf.Min(preferredPanelSize.x, physicalAvailableWidth),
            dynamicMinimum,
            preferredPanelSize.x);
        float height = Mathf.Min(
            preferredPanelSize.y,
            Mathf.Max(450f, canvasRoot.rect.height - safeMargin * 2f));

        compareRoot.sizeDelta = new Vector2(width, height);
        compareRoot.anchorMin = compareRoot.anchorMax = new Vector2(1f, 0.5f);
        compareRoot.pivot = new Vector2(1f, 0.5f);
        compareRoot.anchoredPosition = new Vector2(-safeMargin, 0f);
        compareRoot.localScale = Vector3.one * compareScale;
        compareRoot.SetAsLastSibling();
    }

    private void ShowCompareImmediate()
    {
        if (compareRoot != null && !compareRoot.gameObject.activeSelf)
            compareRoot.gameObject.SetActive(true);

        if (compareGroup != null)
        {
            compareGroup.alpha = 1f;
            compareGroup.blocksRaycasts = false;
            compareGroup.interactable = false;
        }
    }

    private void HideCompareImmediate()
    {
        if (compareGroup != null)
        {
            compareGroup.alpha = 0f;
            compareGroup.blocksRaycasts = false;
            compareGroup.interactable = false;
        }
    }

    private void SuppressOriginalDetail()
    {
        detailGroup = detailController != null ? detailController.Group : detailGroup;
        if (detailGroup == null)
            return;

        detailGroup.alpha = 0f;
        detailGroup.blocksRaycasts = false;
        detailGroup.interactable = false;
    }

    private void RestoreDetailVisibility()
    {
        detailGroup = detailController != null ? detailController.Group : detailGroup;
        if (detailGroup == null)
            return;

        bool shouldShow = detailController != null && detailController.DisplayedSlot >= 0 &&
                          kineticLoadout != null && kineticLoadout.IsSwitchBoardOpen;
        detailGroup.alpha = shouldShow ? 1f : 0f;
        detailGroup.blocksRaycasts = false;
        detailGroup.interactable = false;
    }

    private void ApplyCombatSlotFrames(int sourceIndex, int targetIndex, bool targetHasItem)
    {
        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            RectTransform slot = FindRect($"GridSlot_{i}");
            if (slot == null)
                continue;

            RectTransform frame = slot.Find("CombatSwapPreviewFrame") as RectTransform;
            if (frame == null)
            {
                frame = CreateRect(slot, "CombatSwapPreviewFrame", Vector2.zero);
                SetAnchors(frame, Vector2.zero, Vector2.one);
                Image image = frame.gameObject.AddComponent<Image>();
                image.color = Color.clear;
                image.raycastTarget = false;
                Outline outline = frame.gameObject.AddComponent<Outline>();
                outline.useGraphicAlpha = false;
                frame.SetAsLastSibling();
            }

            bool active = i == sourceIndex || i == targetIndex;
            if (frame.gameObject.activeSelf != active)
                frame.gameObject.SetActive(active);
            if (!active)
                continue;

            Outline frameOutline = frame.GetComponent<Outline>();
            if (frameOutline == null)
                continue;

            if (i == sourceIndex)
            {
                frameOutline.effectColor = pink;
                frameOutline.effectDistance = new Vector2(7f, -7f);
            }
            else
            {
                frameOutline.effectColor = targetHasItem ? yellow : cyan;
                frameOutline.effectDistance = new Vector2(7f, -7f);
            }
        }
    }

    private void ClearCombatSlotFrames()
    {
        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            RectTransform slot = FindRect($"GridSlot_{i}");
            RectTransform frame = slot != null ? slot.Find("CombatSwapPreviewFrame") as RectTransform : null;
            if (frame != null && frame.gameObject.activeSelf)
                frame.gameObject.SetActive(false);
        }
    }

    private void InvalidateSnapshot()
    {
        lastSource = int.MinValue;
        lastTarget = int.MinValue;
        lastSourceEquipment = null;
        lastTargetEquipment = null;
    }

    private static void SetSideActive(SideView side, bool active)
    {
        if (side?.root != null && side.root.gameObject.activeSelf != active)
            side.root.gameObject.SetActive(active);
    }

    private static Text FindText(Transform root, string path)
    {
        if (root == null)
            return null;
        Transform found = root.Find(path);
        return found != null ? found.GetComponent<Text>() : null;
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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
        Color color,
        string objectName)
    {
        RectTransform rect = CreateRect(parent, objectName, Vector2.zero);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.supportRichText = true;
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
}

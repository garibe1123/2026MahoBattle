using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Reward PACK 편집 중 Hand 아이템과 현재 PACK 슬롯을 기존 우측 Detail 영역에서 비교합니다.
///
/// 핵심 규칙:
/// - 하단에 별도 비교창을 만들지 않습니다.
/// - 비교 중에는 EquipmentDetailPanel과 같은 Canvas에서 2-column Compare Detail이 그 자리를 대신합니다.
/// - CURRENT / OUT과 NEW / IN을 한눈에 비교합니다.
/// - Compare Detail과 PACK 사이의 실제 Screen-space 여백을 계산하고, 부족한 만큼만 PACK을 왼쪽으로 이동합니다.
/// - Reward / Equipment 데이터는 읽기만 하며 실제 교환은 BattleRewardFlow가 계속 단독 소유합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(61020)]
public sealed class BattleRewardItemCompareController : MonoBehaviour
{
    private const float DefaultGapPixels = 34f;
    private const float DefaultScreenMarginPixels = 24f;

    [Header("AUTO REFERENCES")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRewardFlow rewardFlow;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleInventoryInteractionController inventoryInteraction;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;

    [Header("COMPARE DETAIL")]
    [SerializeField] private Vector2 preferredPanelSize = new(900f, 610f);
    [SerializeField, Range(620f, 900f)] private float minimumPanelWidth = 720f;
    [SerializeField, Range(0.75f, 1.10f)] private float compareScale = 0.96f;
    [SerializeField, Range(8f, 30f)] private float compareTweenSharpness = 18f;
    [SerializeField, Range(16f, 70f)] private float screenMargin = 28f;

    [Header("PACK SPACE POLICY")]
    [Tooltip("Compare Detail과 PACK 사이에 유지할 최소 Screen-space 간격(px)입니다.")]
    [SerializeField, Range(16f, 90f)] private float packCompareGapPixels = DefaultGapPixels;
    [Tooltip("자동으로 PACK을 왼쪽으로 이동할 수 있는 최대 local UI 거리입니다.")]
    [SerializeField, Range(80f, 520f)] private float maxPackLeftShift = 360f;
    [SerializeField, Range(6f, 30f)] private float packShiftSharpness = 16f;

    [Header("THEME")]
    [SerializeField] private Color ink = new(0.024f, 0.025f, 0.034f, 0.995f);
    [SerializeField] private Color currentInk = new(0.050f, 0.052f, 0.066f, 0.995f);
    [SerializeField] private Color incomingInk = new(0.066f, 0.047f, 0.068f, 0.995f);
    [SerializeField] private Color paper = new(0.95f, 0.92f, 0.80f, 1f);
    [SerializeField] private Color cyan = new(0.12f, 0.90f, 0.94f, 1f);
    [SerializeField] private Color yellow = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Color pink = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField] private Color muted = new(0.57f, 0.60f, 0.67f, 1f);

    private RectTransform detailRoot;
    private CanvasGroup detailGroup;
    private RectTransform compareRoot;
    private CanvasGroup compareGroup;
    private Text header;
    private Text subHeader;
    private Text actionLabel;
    private CompareSide currentSide;
    private CompareSide incomingSide;

    private RectTransform boardRoot;
    private float nextResolveAt;
    private float compareVisualAlpha;
    private float compareVisualScale = 0.96f;
    private float packLeftShiftVisual;
    private float lastAppliedPackShift;
    private Vector2 lastAppliedBoardPosition;
    private bool compareWasActive;
    private bool legacyBottomRemoved;

    private int lastTargetIndex = int.MinValue;
    private BattleEquipmentSO lastCurrentEquipment;
    private BattleEquipmentSO lastIncomingEquipment;
    private int lastCurrentGrade = int.MinValue;
    private int lastCurrentCopies = int.MinValue;
    private int lastIncomingGrade = int.MinValue;
    private int lastIncomingCopies = int.MinValue;
    private bool lastMergeMode;
    private bool lastMergeBlocked;

    private readonly Vector3[] worldCorners = new Vector3[4];

    private sealed class CompareSide
    {
        public RectTransform root;
        public Image icon;
        public Text eyebrow;
        public Text name;
        public Text meta;
        public Text level;
        public Text description;
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
        if (UnityEngine.Object.FindFirstObjectByType<BattleRewardItemCompareController>(FindObjectsInactive.Include) != null)
            return;

        BattleRewardFlow flow = UnityEngine.Object.FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        BattleInventoryInteractionController interaction =
            UnityEngine.Object.FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        BattleRunManager run = UnityEngine.Object.FindFirstObjectByType<BattleRunManager>(FindObjectsInactive.Include);

        GameObject host = flow != null
            ? flow.gameObject
            : interaction != null
                ? interaction.gameObject
                : run != null
                    ? run.gameObject
                    : GameObject.Find("BattleSystems");

        if (host != null)
            host.AddComponent<BattleRewardItemCompareController>();
    }

    private void Awake()
    {
        ResolveReferences(true);
        ResolveUi(true);
        RemoveLegacyBottomCompareCanvas();
    }

    private void OnEnable()
    {
        ResolveReferences(true);
        ResolveUi(true);
        legacyBottomRemoved = false;
        RemoveLegacyBottomCompareCanvas();
        nextResolveAt = 0f;
        compareVisualAlpha = 0f;
        compareVisualScale = Mathf.Max(0.84f, compareScale - 0.035f);
        packLeftShiftVisual = 0f;
        lastAppliedPackShift = 0f;
        compareWasActive = false;
        InvalidateSnapshot();
    }

    private void OnDisable()
    {
        RestoreOriginalDetailVisibility();
        SetCompareImmediateHidden();
        RestoreBoardIfStillShifted();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextResolveAt)
            return;

        nextResolveAt = Time.unscaledTime + 0.15f;
        ResolveReferences(false);
        ResolveUi(false);
    }

    private void LateUpdate()
    {
        ResolveUi(false);

        bool active = TryResolveComparison(
            out int targetIndex,
            out BattleEquipmentSlot current,
            out BattleEquipmentStack incoming,
            out bool mergeMode,
            out bool mergeBlocked);

        if (active)
        {
            EnsureCompareUi();
            LayoutComparePanelInsideScreen();

            if (detailController != null && detailController.DisplayedSlot != targetIndex)
                detailController.ShowSlot(targetIndex);

            RefreshIfChanged(targetIndex, current, incoming, mergeMode, mergeBlocked);
            SuppressOriginalDetail();
            AnimateCompare(true);
            ApplyPackSpacePolicy(true);
            compareWasActive = true;
            return;
        }

        if (compareWasActive)
            RestoreOriginalDetailVisibility();

        AnimateCompare(false);
        ApplyPackSpacePolicy(false);
        compareWasActive = false;
        InvalidateSnapshot();
    }

    private bool TryResolveComparison(
        out int targetIndex,
        out BattleEquipmentSlot current,
        out BattleEquipmentStack incoming,
        out bool mergeMode,
        out bool mergeBlocked)
    {
        targetIndex = -1;
        current = null;
        incoming = default;
        mergeMode = false;
        mergeBlocked = false;

        if (runManager == null || !runManager.RunActive || runManager.State != BattleRunState.Reward ||
            rewardFlow == null || rewardFlow.Phase != BattleRewardPhase.PackEditing ||
            !rewardFlow.HasHand || equipmentSystem == null || inventoryInteraction == null)
            return false;

        incoming = rewardFlow.Hand;
        if (incoming.IsEmpty || incoming.equipment == null)
            return false;

        if (inventoryInteraction.PadModeActive)
        {
            targetIndex = inventoryInteraction.PadSelectedSlot;
        }
        else
        {
            targetIndex = inventoryInteraction.HoveredSlot;
            if (targetIndex < 0)
                return false;
        }

        if (!equipmentSystem.TryGetSlot(targetIndex, out current) ||
            current == null || current.equipment == null)
            return false;

        mergeMode = rewardFlow.HandIsChosenReward &&
                    rewardFlow.ChosenReward != null &&
                    incoming.equipment == current.equipment &&
                    current.equipment == rewardFlow.ChosenReward;
        mergeBlocked = mergeMode && current.grade >= 3;
        return true;
    }

    private void RefreshIfChanged(
        int targetIndex,
        BattleEquipmentSlot current,
        BattleEquipmentStack incoming,
        bool mergeMode,
        bool mergeBlocked)
    {
        if (compareRoot == null || currentSide == null || incomingSide == null)
            return;

        if (targetIndex == lastTargetIndex &&
            current.equipment == lastCurrentEquipment &&
            incoming.equipment == lastIncomingEquipment &&
            current.grade == lastCurrentGrade &&
            current.copies == lastCurrentCopies &&
            incoming.grade == lastIncomingGrade &&
            incoming.copies == lastIncomingCopies &&
            mergeMode == lastMergeMode &&
            mergeBlocked == lastMergeBlocked)
            return;

        lastTargetIndex = targetIndex;
        lastCurrentEquipment = current.equipment;
        lastIncomingEquipment = incoming.equipment;
        lastCurrentGrade = current.grade;
        lastCurrentCopies = current.copies;
        lastIncomingGrade = incoming.grade;
        lastIncomingCopies = incoming.copies;
        lastMergeMode = mergeMode;
        lastMergeBlocked = mergeBlocked;

        Vector2Int grid = BattleEquipmentSystem.SlotIndexToGrid(targetIndex);
        if (mergeMode)
        {
            header.text = mergeBlocked ? "MERGE // MAX GRADE" : "MERGE // COMPARE";
            header.color = mergeBlocked ? pink : cyan;
            subHeader.text = mergeBlocked
                ? $"PACK {grid.x + 1}-{grid.y + 1}  //  THIS STACK CANNOT MERGE MORE"
                : $"PACK {grid.x + 1}-{grid.y + 1}  //  SAME ITEM / RESULT PREVIEW";
            actionLabel.text = mergeBlocked ? "×\nMAX" : "+\nMERGE";
            actionLabel.color = mergeBlocked ? pink : cyan;

            FillSide(currentSide, "CURRENT // PACK", current.equipment, current.grade, current.copies, null);

            int resultGrade = current.grade;
            int resultCopies = current.copies;
            if (!mergeBlocked)
            {
                resultCopies++;
                if (resultCopies >= 3)
                {
                    resultGrade = Mathf.Min(3, resultGrade + 1);
                    resultCopies = 1;
                }
            }

            FillSide(
                incomingSide,
                mergeBlocked ? "INCOMING // BLOCKED" : "RESULT // AFTER MERGE",
                incoming.equipment,
                resultGrade,
                resultCopies,
                null);
            return;
        }

        header.text = "REPLACE // COMPARE";
        header.color = yellow;
        subHeader.text = $"PACK {grid.x + 1}-{grid.y + 1}  //  REVIEW BEFORE REPLACE";
        actionLabel.text = "→\nSWAP";
        actionLabel.color = pink;

        FillSide(currentSide, "CURRENT // OUT", current.equipment, current.grade, current.copies, null);
        FillSide(incomingSide, "NEW // IN", incoming.equipment, incoming.grade, incoming.copies, current.equipment);
    }

    private void FillSide(
        CompareSide side,
        string eyebrowValue,
        BattleEquipmentSO equipment,
        int grade,
        int copies,
        BattleEquipmentSO compareAgainst)
    {
        if (side == null || equipment == null)
            return;

        side.eyebrow.text = eyebrowValue;
        side.icon.sprite = equipment.icon;
        side.icon.enabled = equipment.icon != null;
        side.name.text = equipment.GetDisplayName().ToUpperInvariant();
        side.meta.text = $"{equipment.rarity.ToString().ToUpperInvariant()}  /  {equipment.type.ToString().ToUpperInvariant()}";
        side.level.text = $"LV.{Mathf.Clamp(grade, 1, 3)}   COPY {Mathf.Clamp(copies, 1, 2)}/3";
        side.description.text = string.IsNullOrWhiteSpace(equipment.description)
            ? BuildFallbackDescription(equipment)
            : equipment.description.Trim();
        side.stats.text = BuildStats(equipment, compareAgainst);
        side.tags.text = BuildTags(equipment);
    }

    private string BuildStats(BattleEquipmentSO equipment, BattleEquipmentSO compareAgainst)
    {
        if (equipment == null)
            return string.Empty;

        StringBuilder sb = new();
        AppendStat(sb, "DMG", equipment.damageMultiplier,
            compareAgainst != null ? compareAgainst.damageMultiplier : (float?)null);
        AppendStat(sb, "MOVE", equipment.moveSpeedMultiplier,
            compareAgainst != null ? compareAgainst.moveSpeedMultiplier : (float?)null);
        AppendStat(sb, "RANGE", equipment.rangeMultiplier,
            compareAgainst != null ? compareAgainst.rangeMultiplier : (float?)null);
        return sb.ToString();
    }

    private static void AppendStat(StringBuilder sb, string label, float value, float? oldValue)
    {
        if (sb.Length > 0)
            sb.Append('\n');

        sb.Append(label.PadRight(7));
        sb.Append($"×{value:0.00}");

        if (!oldValue.HasValue)
            return;

        float delta = value - oldValue.Value;
        if (Mathf.Abs(delta) < 0.0005f)
        {
            sb.Append("   <color=#8F98A8>— SAME</color>");
        }
        else if (delta > 0f)
        {
            sb.Append($"   <color=#20E6EC>▲ +{delta:0.00}</color>");
        }
        else
        {
            sb.Append($"   <color=#FF3A7D>▼ {delta:0.00}</color>");
        }
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
        if (force || rewardFlow == null)
            rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        if (force || equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>(FindObjectsInactive.Include);
        if (force || inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);
        if (force || detailController == null)
            detailController = FindFirstObjectByType<BattleEquipmentDetailPanelController>(FindObjectsInactive.Include);
        if (force || kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
    }

    private void ResolveUi(bool force)
    {
        RectTransform resolvedDetail = detailController != null ? detailController.Root : null;
        if (force || detailRoot != resolvedDetail)
        {
            if (detailRoot != resolvedDetail)
            {
                detailRoot = resolvedDetail;
                detailGroup = detailController != null ? detailController.Group : null;
                compareRoot = null;
                compareGroup = null;
                currentSide = null;
                incomingSide = null;
            }
        }

        if (force || boardRoot == null)
            boardRoot = kineticLoadout != null ? kineticLoadout.GridBoard : null;

        if (detailRoot != null)
            EnsureCompareUi();
    }

    private void EnsureCompareUi()
    {
        if (detailRoot == null || compareRoot != null)
            return;

        RectTransform parent = detailRoot.parent as RectTransform;
        if (parent == null)
            return;

        RectTransform existing = parent.Find("RewardEquipmentCompareDetail") as RectTransform;
        if (existing != null)
            Destroy(existing.gameObject);

        compareRoot = CreateRect(parent, "RewardEquipmentCompareDetail", preferredPanelSize);
        compareRoot.anchorMin = compareRoot.anchorMax = new Vector2(1f, 0.5f);
        compareRoot.pivot = new Vector2(1f, 0.5f);
        compareRoot.anchoredPosition = new Vector2(-screenMargin, 0f);
        compareRoot.localRotation = Quaternion.identity;
        compareRoot.localScale = Vector3.one * compareScale;
        compareRoot.SetAsLastSibling();

        Image back = compareRoot.gameObject.AddComponent<Image>();
        back.color = ink;
        back.raycastTarget = false;

        Outline outline = compareRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(paper.r, paper.g, paper.b, 0.72f);
        outline.effectDistance = new Vector2(5f, -5f);

        compareGroup = compareRoot.gameObject.AddComponent<CanvasGroup>();
        compareGroup.alpha = 0f;
        compareGroup.blocksRaycasts = false;
        compareGroup.interactable = false;

        header = CreateText(compareRoot, "REPLACE // COMPARE", 19, FontStyle.Bold, TextAnchor.MiddleLeft, yellow, "Header");
        SetAnchors(header.rectTransform, new Vector2(0.035f, 0.915f), new Vector2(0.56f, 0.975f));

        subHeader = CreateText(compareRoot, "PACK 1-1 // REVIEW BEFORE REPLACE", 11, FontStyle.Bold, TextAnchor.MiddleRight, muted, "SubHeader");
        SetAnchors(subHeader.rectTransform, new Vector2(0.50f, 0.922f), new Vector2(0.965f, 0.968f));

        RectTransform topStroke = CreateRect(compareRoot, "TopStroke", Vector2.zero);
        SetAnchors(topStroke, new Vector2(0.035f, 0.895f), new Vector2(0.965f, 0.905f));
        Image topStrokeImage = topStroke.gameObject.AddComponent<Image>();
        topStrokeImage.color = yellow;
        topStrokeImage.raycastTarget = false;

        currentSide = BuildSide(compareRoot, "CurrentSide", new Vector2(0.035f, 0.065f), new Vector2(0.475f, 0.875f), currentInk, cyan);
        incomingSide = BuildSide(compareRoot, "IncomingSide", new Vector2(0.525f, 0.065f), new Vector2(0.965f, 0.875f), incomingInk, yellow);

        RectTransform centerRail = CreateRect(compareRoot, "CenterRail", Vector2.zero);
        SetAnchors(centerRail, new Vector2(0.494f, 0.075f), new Vector2(0.506f, 0.865f));
        Image railImage = centerRail.gameObject.AddComponent<Image>();
        railImage.color = new Color(paper.r, paper.g, paper.b, 0.18f);
        railImage.raycastTarget = false;

        actionLabel = CreateText(compareRoot, "→\nSWAP", 14, FontStyle.Bold, TextAnchor.MiddleCenter, pink, "Action");
        SetAnchors(actionLabel.rectTransform, new Vector2(0.462f, 0.435f), new Vector2(0.538f, 0.565f));
        actionLabel.lineSpacing = 0.85f;

        compareRoot.gameObject.SetActive(true);
    }

    private CompareSide BuildSide(
        RectTransform parent,
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color background,
        Color accent)
    {
        RectTransform sideRoot = CreateRect(parent, objectName, Vector2.zero);
        SetAnchors(sideRoot, anchorMin, anchorMax);

        Image sideBack = sideRoot.gameObject.AddComponent<Image>();
        sideBack.color = background;
        sideBack.raycastTarget = false;

        Outline sideOutline = sideRoot.gameObject.AddComponent<Outline>();
        sideOutline.effectColor = new Color(accent.r, accent.g, accent.b, 0.54f);
        sideOutline.effectDistance = new Vector2(3f, -3f);

        CompareSide side = new() { root = sideRoot };

        side.eyebrow = CreateText(sideRoot, objectName, 11, FontStyle.Bold, TextAnchor.MiddleLeft, accent, "Eyebrow");
        SetAnchors(side.eyebrow.rectTransform, new Vector2(0.055f, 0.910f), new Vector2(0.945f, 0.970f));

        RectTransform iconBack = CreateRect(sideRoot, "IconBack", Vector2.zero);
        SetAnchors(iconBack, new Vector2(0.055f, 0.715f), new Vector2(0.285f, 0.895f));
        Image iconBackImage = iconBack.gameObject.AddComponent<Image>();
        iconBackImage.color = new Color(0f, 0f, 0f, 0.42f);
        iconBackImage.raycastTarget = false;

        RectTransform iconRect = CreateRect(iconBack, "Icon", Vector2.zero);
        SetAnchors(iconRect, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));
        side.icon = iconRect.gameObject.AddComponent<Image>();
        side.icon.color = paper;
        side.icon.preserveAspect = true;
        side.icon.raycastTarget = false;

        side.name = CreateText(sideRoot, "ITEM NAME", 20, FontStyle.Bold, TextAnchor.MiddleLeft, paper, "Name");
        SetAnchors(side.name.rectTransform, new Vector2(0.325f, 0.800f), new Vector2(0.945f, 0.900f));
        side.name.resizeTextForBestFit = true;
        side.name.resizeTextMinSize = 13;
        side.name.resizeTextMaxSize = 20;

        side.meta = CreateText(sideRoot, "COMMON / MANUAL", 10, FontStyle.Bold, TextAnchor.MiddleLeft, accent, "Meta");
        SetAnchors(side.meta.rectTransform, new Vector2(0.325f, 0.745f), new Vector2(0.945f, 0.805f));

        side.level = CreateText(sideRoot, "LV.1 COPY 1/3", 10, FontStyle.Bold, TextAnchor.MiddleLeft, yellow, "Level");
        SetAnchors(side.level.rectTransform, new Vector2(0.325f, 0.690f), new Vector2(0.945f, 0.750f));

        RectTransform descBack = CreateRect(sideRoot, "DescriptionBack", Vector2.zero);
        SetAnchors(descBack, new Vector2(0.055f, 0.430f), new Vector2(0.945f, 0.665f));
        Image descBackImage = descBack.gameObject.AddComponent<Image>();
        descBackImage.color = new Color(0f, 0f, 0f, 0.40f);
        descBackImage.raycastTarget = false;

        side.description = CreateText(descBack, "DESCRIPTION", 12, FontStyle.Normal, TextAnchor.UpperLeft, paper, "Description");
        SetAnchors(side.description.rectTransform, new Vector2(0.045f, 0.08f), new Vector2(0.955f, 0.92f));
        side.description.horizontalOverflow = HorizontalWrapMode.Wrap;
        side.description.verticalOverflow = VerticalWrapMode.Truncate;
        side.description.lineSpacing = 1.05f;

        Text paramTitle = CreateText(sideRoot, "PARAMETERS", 10, FontStyle.Bold, TextAnchor.MiddleLeft, accent, "ParamTitle");
        SetAnchors(paramTitle.rectTransform, new Vector2(0.055f, 0.365f), new Vector2(0.945f, 0.420f));

        side.stats = CreateText(sideRoot, "DMG\nMOVE\nRANGE", 13, FontStyle.Bold, TextAnchor.UpperLeft, paper, "Stats");
        SetAnchors(side.stats.rectTransform, new Vector2(0.055f, 0.165f), new Vector2(0.945f, 0.365f));
        side.stats.supportRichText = true;
        side.stats.lineSpacing = 1.18f;

        side.tags = CreateText(sideRoot, "TAGS // NONE", 10, FontStyle.Bold, TextAnchor.UpperLeft, muted, "Tags");
        SetAnchors(side.tags.rectTransform, new Vector2(0.055f, 0.040f), new Vector2(0.945f, 0.145f));
        side.tags.horizontalOverflow = HorizontalWrapMode.Wrap;
        side.tags.verticalOverflow = VerticalWrapMode.Truncate;

        return side;
    }

    private void LayoutComparePanelInsideScreen()
    {
        if (compareRoot == null || compareRoot.parent is not RectTransform canvasRoot)
            return;

        float safeMargin = Mathf.Max(DefaultScreenMarginPixels, screenMargin);
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
        compareRoot.SetAsLastSibling();
    }

    private void SuppressOriginalDetail()
    {
        if (detailGroup == null && detailController != null)
            detailGroup = detailController.Group;

        if (detailGroup == null)
            return;

        detailGroup.alpha = 0f;
        detailGroup.blocksRaycasts = false;
        detailGroup.interactable = false;
    }

    private void RestoreOriginalDetailVisibility()
    {
        if (detailGroup == null && detailController != null)
            detailGroup = detailController.Group;

        if (detailGroup == null)
            return;

        bool shouldShow = detailController != null && detailController.DisplayedSlot >= 0;
        detailGroup.alpha = shouldShow ? 1f : 0f;
        detailGroup.blocksRaycasts = false;
        detailGroup.interactable = false;
    }

    private void AnimateCompare(bool visible)
    {
        if (compareRoot == null || compareGroup == null)
            return;

        float targetAlpha = visible ? 1f : 0f;
        float targetScale = visible ? compareScale : Mathf.Max(0.84f, compareScale - 0.035f);
        float t = 1f - Mathf.Exp(-Mathf.Max(8f, compareTweenSharpness) * Time.unscaledDeltaTime);

        compareVisualAlpha = Mathf.Lerp(compareVisualAlpha, targetAlpha, t);
        compareVisualScale = Mathf.Lerp(compareVisualScale, targetScale, t);

        if (Mathf.Abs(compareVisualAlpha - targetAlpha) <= 0.002f)
            compareVisualAlpha = targetAlpha;
        if (Mathf.Abs(compareVisualScale - targetScale) <= 0.002f)
            compareVisualScale = targetScale;

        compareGroup.alpha = compareVisualAlpha;
        compareGroup.blocksRaycasts = false;
        compareGroup.interactable = false;
        compareRoot.localScale = Vector3.one * compareVisualScale;
    }

    private void ApplyPackSpacePolicy(bool compareActive)
    {
        if (boardRoot == null)
        {
            boardRoot = kineticLoadout != null ? kineticLoadout.GridBoard : null;
            if (boardRoot == null)
                return;
        }

        Vector2 authoritativePosition = RecoverAuthoritativeBoardPosition();
        float targetShift = 0f;

        if (compareActive && compareRoot != null && compareVisualAlpha > 0.02f)
        {
            if (TryGetScreenHorizontalBounds(boardRoot, out float boardLeft, out float boardRight) &&
                TryGetScreenHorizontalBounds(compareRoot, out float compareLeft, out _))
            {
                float requiredPixels = Mathf.Max(0f, boardRight + Mathf.Max(16f, packCompareGapPixels) - compareLeft);
                float leftRoomPixels = Mathf.Max(0f, boardLeft - Mathf.Max(DefaultScreenMarginPixels, screenMargin));
                float usablePixels = Mathf.Min(requiredPixels, leftRoomPixels);
                targetShift = Mathf.Min(maxPackLeftShift, ScreenPixelsToParentUnits(boardRoot, usablePixels));
            }
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(6f, packShiftSharpness) * Time.unscaledDeltaTime);
        packLeftShiftVisual = Mathf.Lerp(packLeftShiftVisual, targetShift, t);
        if (Mathf.Abs(packLeftShiftVisual - targetShift) <= 0.15f)
            packLeftShiftVisual = targetShift;

        Vector2 applied = authoritativePosition + new Vector2(-packLeftShiftVisual, 0f);
        boardRoot.anchoredPosition = applied;
        lastAppliedPackShift = packLeftShiftVisual;
        lastAppliedBoardPosition = applied;
    }

    private Vector2 RecoverAuthoritativeBoardPosition()
    {
        if (boardRoot == null)
            return Vector2.zero;

        Vector2 current = boardRoot.anchoredPosition;

        // UnifiedInventory가 이 프레임에 이미 원래 위치를 다시 썼다면 현재 값이 authoritative입니다.
        // 다른 이유로 이전 프레임의 Compare shift가 아직 남아 있다면 그 shift만 되돌립니다.
        if (lastAppliedPackShift > 0.001f &&
            (current - lastAppliedBoardPosition).sqrMagnitude <= 0.25f)
        {
            current.x += lastAppliedPackShift;
        }

        return current;
    }

    private void RestoreBoardIfStillShifted()
    {
        if (boardRoot == null || lastAppliedPackShift <= 0.001f)
            return;

        Vector2 current = boardRoot.anchoredPosition;
        if ((current - lastAppliedBoardPosition).sqrMagnitude <= 0.25f)
        {
            current.x += lastAppliedPackShift;
            boardRoot.anchoredPosition = current;
        }

        lastAppliedPackShift = 0f;
        packLeftShiftVisual = 0f;
    }

    private bool TryGetScreenHorizontalBounds(RectTransform rect, out float left, out float right)
    {
        left = float.PositiveInfinity;
        right = float.NegativeInfinity;
        if (rect == null)
            return false;

        rect.GetWorldCorners(worldCorners);
        Camera camera = ResolveCanvasCamera(rect);
        for (int i = 0; i < worldCorners.Length; i++)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, worldCorners[i]);
            left = Mathf.Min(left, screen.x);
            right = Mathf.Max(right, screen.x);
        }

        return !float.IsInfinity(left) && !float.IsInfinity(right);
    }

    private static float ScreenPixelsToParentUnits(RectTransform rect, float screenPixels)
    {
        if (rect == null || screenPixels <= 0f || rect.parent is not RectTransform parent)
            return 0f;

        Camera camera = ResolveCanvasCamera(rect);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Vector2.zero, camera, out Vector2 localA) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(screenPixels, 0f), camera, out Vector2 localB))
            return screenPixels;

        return Mathf.Abs(localB.x - localA.x);
    }

    private static Camera ResolveCanvasCamera(RectTransform rect)
    {
        Canvas canvas = rect != null ? rect.GetComponentInParent<Canvas>() : null;
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;
        return canvas.worldCamera;
    }

    private void RemoveLegacyBottomCompareCanvas()
    {
        if (legacyBottomRemoved)
            return;

        legacyBottomRemoved = true;
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas candidate = canvases[i];
            if (candidate == null || candidate.name != "BattleRewardItemCompareCanvas")
                continue;
            candidate.gameObject.SetActive(false);
        }
    }

    private void SetCompareImmediateHidden()
    {
        compareVisualAlpha = 0f;
        if (compareGroup != null)
            compareGroup.alpha = 0f;
    }

    private void InvalidateSnapshot()
    {
        lastTargetIndex = int.MinValue;
        lastCurrentEquipment = null;
        lastIncomingEquipment = null;
        lastCurrentGrade = int.MinValue;
        lastCurrentCopies = int.MinValue;
        lastIncomingGrade = int.MinValue;
        lastIncomingCopies = int.MinValue;
        lastMergeMode = false;
        lastMergeBlocked = false;
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
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

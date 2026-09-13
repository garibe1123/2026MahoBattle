using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Reward PACK 편집 중 Hand 아이템을 기존 슬롯과 교체하기 전에 두 아이템을 나란히 비교합니다.
///
/// 표시 조건:
/// - Reward / PackEditing
/// - Reward Hand에 아이템이 있음
/// - Mouse Hover 또는 Pad Cursor가 실제 아이템이 들어 있는 PACK 슬롯을 가리킴
///
/// 이 클래스는 비교 정보만 표시하며 Reward/Equipment 데이터를 변경하지 않습니다.
/// 실제 교환은 BattleRewardFlow.ExchangeHandWithSlot()이 계속 단독 소유합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33460)]
public sealed class BattleRewardItemCompareController : MonoBehaviour
{
    private const int CanvasSortingOrder = 1660;

    [Header("AUTO REFERENCES")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRewardFlow rewardFlow;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleInventoryInteractionController inventoryInteraction;

    [Header("LAYOUT")]
    [SerializeField] private Vector2 panelSize = new(860f, 306f);
    [SerializeField] private Vector2 panelAnchor = new(0.64f, 0.19f);
    [SerializeField, Range(0.70f, 1.10f)] private float visibleScale = 1f;
    [SerializeField, Range(8f, 30f)] private float tweenSharpness = 18f;

    [Header("THEME")]
    [SerializeField] private Color ink = new(0.025f, 0.026f, 0.036f, 0.985f);
    [SerializeField] private Color panelInk = new(0.055f, 0.057f, 0.072f, 0.985f);
    [SerializeField] private Color paper = new(0.95f, 0.92f, 0.80f, 1f);
    [SerializeField] private Color cyan = new(0.12f, 0.90f, 0.94f, 1f);
    [SerializeField] private Color yellow = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Color pink = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField] private Color muted = new(0.58f, 0.61f, 0.68f, 1f);

    private Canvas canvas;
    private RectTransform root;
    private CanvasGroup group;
    private Text header;
    private Text subHeader;
    private Text centerLabel;

    private CompareSide currentSide;
    private CompareSide incomingSide;

    private float nextResolveAt;
    private int lastTargetIndex = int.MinValue;
    private BattleEquipmentSO lastCurrentEquipment;
    private BattleEquipmentSO lastIncomingEquipment;
    private int lastCurrentGrade = int.MinValue;
    private int lastCurrentCopies = int.MinValue;
    private int lastIncomingGrade = int.MinValue;
    private int lastIncomingCopies = int.MinValue;
    private bool lastMergeMode;
    private bool lastMergeBlocked;

    private sealed class CompareSide
    {
        public RectTransform root;
        public Image icon;
        public Text eyebrow;
        public Text name;
        public Text meta;
        public Text level;
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
        EnsureUi();
    }

    private void OnEnable()
    {
        ResolveReferences(true);
        EnsureUi();
        nextResolveAt = 0f;
        InvalidateSnapshot();
    }

    private void OnDisable()
    {
        SetImmediateHidden();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextResolveAt)
        {
            nextResolveAt = Time.unscaledTime + 0.15f;
            ResolveReferences(false);
            EnsureUi();
        }

        bool visible = TryResolveComparison(
            out int targetIndex,
            out BattleEquipmentSlot current,
            out BattleEquipmentStack incoming,
            out bool mergeMode,
            out bool mergeBlocked);

        if (visible)
        {
            RefreshIfChanged(targetIndex, current, incoming, mergeMode, mergeBlocked);
            AnimateVisible(true);
        }
        else
        {
            AnimateVisible(false);
            InvalidateSnapshot();
        }
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

        if (!equipmentSystem.TryGetSlot(targetIndex, out current) || current == null || current.equipment == null)
            return false;

        mergeMode = rewardFlow.HandIsChosenReward &&
                    rewardFlow.ChosenReward != null &&
                    current.equipment == rewardFlow.ChosenReward &&
                    incoming.equipment == current.equipment;
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

        if (mergeMode)
            RefreshMerge(targetIndex, current, incoming, mergeBlocked);
        else
            RefreshReplace(targetIndex, current, incoming);
    }

    private void RefreshReplace(int targetIndex, BattleEquipmentSlot current, BattleEquipmentStack incoming)
    {
        Vector2Int grid = BattleEquipmentSystem.SlotIndexToGrid(targetIndex);
        header.text = "REPLACE // COMPARE";
        header.color = yellow;
        subHeader.text = $"PACK {grid.x + 1}-{grid.y + 1}  //  CHECK BEFORE SWAP";
        centerLabel.text = "→\nREPLACE";
        centerLabel.color = pink;

        FillSide(currentSide, "CURRENT // OUT", current.equipment, current.grade, current.copies, false, null);
        FillSide(incomingSide, "NEW // IN", incoming.equipment, incoming.grade, incoming.copies, true, current.equipment);
    }

    private void RefreshMerge(
        int targetIndex,
        BattleEquipmentSlot current,
        BattleEquipmentStack incoming,
        bool blocked)
    {
        Vector2Int grid = BattleEquipmentSystem.SlotIndexToGrid(targetIndex);
        header.text = blocked ? "MERGE // MAX GRADE" : "MERGE // PREVIEW";
        header.color = blocked ? pink : cyan;
        subHeader.text = blocked
            ? $"PACK {grid.x + 1}-{grid.y + 1}  //  THIS SLOT CANNOT MERGE MORE"
            : $"PACK {grid.x + 1}-{grid.y + 1}  //  SAME ITEM COPY";
        centerLabel.text = blocked ? "×\nMAX" : "+\nMERGE";
        centerLabel.color = blocked ? pink : cyan;

        FillSide(currentSide, "CURRENT", current.equipment, current.grade, current.copies, false, null);

        int resultGrade = current.grade;
        int resultCopies = current.copies;
        if (!blocked)
        {
            resultCopies++;
            if (resultCopies >= 3)
            {
                resultGrade = Mathf.Min(3, resultGrade + 1);
                resultCopies = 1;
            }
        }

        FillSide(incomingSide, blocked ? "NEW COPY // BLOCKED" : "RESULT // AFTER MERGE",
            incoming.equipment, resultGrade, resultCopies, false, null);
    }

    private void FillSide(
        CompareSide side,
        string eyebrowValue,
        BattleEquipmentSO equipment,
        int grade,
        int copies,
        bool showDelta,
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
        side.stats.text = BuildStats(equipment, showDelta ? compareAgainst : null);
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
    }

    private void EnsureUi()
    {
        if (canvas != null)
            return;

        GameObject canvasObject = new("BattleRewardItemCompareCanvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = CanvasSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root = CreateRect(canvas.transform, "RewardItemComparePanel", panelSize);
        root.anchorMin = root.anchorMax = panelAnchor;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.localRotation = Quaternion.Euler(0f, 0f, -0.65f);

        Image back = root.gameObject.AddComponent<Image>();
        back.color = ink;
        back.raycastTarget = false;
        Outline outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(paper.r, paper.g, paper.b, 0.72f);
        outline.effectDistance = new Vector2(5f, -5f);

        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        RectTransform topStroke = CreateRect(root, "TopStroke", Vector2.zero);
        SetAnchors(topStroke, new Vector2(0.025f, 0.945f), new Vector2(0.975f, 0.975f));
        Image topStrokeImage = topStroke.gameObject.AddComponent<Image>();
        topStrokeImage.color = yellow;
        topStrokeImage.raycastTarget = false;

        header = CreateText(root, "REPLACE // COMPARE", 17, FontStyle.Bold, TextAnchor.MiddleLeft, yellow, "Header");
        SetAnchors(header.rectTransform, new Vector2(0.035f, 0.82f), new Vector2(0.56f, 0.94f));

        subHeader = CreateText(root, "PACK 1-1", 10, FontStyle.Bold, TextAnchor.MiddleRight, muted, "SubHeader");
        SetAnchors(subHeader.rectTransform, new Vector2(0.52f, 0.83f), new Vector2(0.965f, 0.93f));

        currentSide = BuildSide(root, "CurrentSide", new Vector2(0.03f, 0.08f), new Vector2(0.47f, 0.80f), cyan);
        incomingSide = BuildSide(root, "IncomingSide", new Vector2(0.53f, 0.08f), new Vector2(0.97f, 0.80f), yellow);

        centerLabel = CreateText(root, "→\nREPLACE", 13, FontStyle.Bold, TextAnchor.MiddleCenter, pink, "CenterLabel");
        SetAnchors(centerLabel.rectTransform, new Vector2(0.465f, 0.30f), new Vector2(0.535f, 0.62f));

        RectTransform centerLine = CreateRect(root, "CenterLine", Vector2.zero);
        SetAnchors(centerLine, new Vector2(0.497f, 0.10f), new Vector2(0.503f, 0.78f));
        Image centerLineImage = centerLine.gameObject.AddComponent<Image>();
        centerLineImage.color = new Color(paper.r, paper.g, paper.b, 0.17f);
        centerLineImage.raycastTarget = false;

        root.gameObject.SetActive(true);
        root.localScale = Vector3.one * 0.94f;
    }

    private CompareSide BuildSide(Transform parent, string objectName, Vector2 min, Vector2 max, Color accent)
    {
        RectTransform sideRoot = CreateRect(parent, objectName, Vector2.zero);
        SetAnchors(sideRoot, min, max);

        Image back = sideRoot.gameObject.AddComponent<Image>();
        back.color = panelInk;
        back.raycastTarget = false;
        Outline outline = sideRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.62f);
        outline.effectDistance = new Vector2(3f, -3f);

        Text eyebrow = CreateText(sideRoot, "CURRENT", 10, FontStyle.Bold, TextAnchor.MiddleLeft, accent, "Eyebrow");
        SetAnchors(eyebrow.rectTransform, new Vector2(0.045f, 0.86f), new Vector2(0.95f, 0.98f));

        Image icon = CreateImage(sideRoot, "Icon", new Vector2(76f, 76f));
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.16f, 0.67f);
        icon.rectTransform.anchoredPosition = Vector2.zero;
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        Text name = CreateText(sideRoot, "ITEM", 18, FontStyle.Bold, TextAnchor.MiddleLeft, paper, "Name");
        SetAnchors(name.rectTransform, new Vector2(0.31f, 0.66f), new Vector2(0.95f, 0.82f));

        Text meta = CreateText(sideRoot, "COMMON / TRAIT", 9, FontStyle.Bold, TextAnchor.MiddleLeft, muted, "Meta");
        SetAnchors(meta.rectTransform, new Vector2(0.31f, 0.55f), new Vector2(0.95f, 0.67f));

        Text level = CreateText(sideRoot, "LV.1 COPY 1/3", 10, FontStyle.Bold, TextAnchor.MiddleLeft, accent, "Level");
        SetAnchors(level.rectTransform, new Vector2(0.31f, 0.44f), new Vector2(0.95f, 0.56f));

        Text stats = CreateText(sideRoot, "DMG\nMOVE\nRANGE", 12, FontStyle.Bold, TextAnchor.UpperLeft, paper, "Stats");
        SetAnchors(stats.rectTransform, new Vector2(0.055f, 0.15f), new Vector2(0.95f, 0.43f));
        stats.supportRichText = true;
        stats.lineSpacing = 1.10f;

        Text tags = CreateText(sideRoot, "TAGS // NONE", 9, FontStyle.Bold, TextAnchor.LowerLeft, muted, "Tags");
        SetAnchors(tags.rectTransform, new Vector2(0.055f, 0.025f), new Vector2(0.95f, 0.15f));
        tags.horizontalOverflow = HorizontalWrapMode.Wrap;
        tags.verticalOverflow = VerticalWrapMode.Truncate;

        return new CompareSide
        {
            root = sideRoot,
            icon = icon,
            eyebrow = eyebrow,
            name = name,
            meta = meta,
            level = level,
            stats = stats,
            tags = tags
        };
    }

    private void AnimateVisible(bool visible)
    {
        if (group == null || root == null)
            return;

        float t = 1f - Mathf.Exp(-Mathf.Max(8f, tweenSharpness) * Time.unscaledDeltaTime);
        float targetAlpha = visible ? 1f : 0f;
        float targetScale = visible ? visibleScale : visibleScale * 0.94f;

        group.alpha = Mathf.Lerp(group.alpha, targetAlpha, t);
        root.localScale = Vector3.Lerp(root.localScale, Vector3.one * targetScale, t);

        if (Mathf.Abs(group.alpha - targetAlpha) <= 0.005f)
            group.alpha = targetAlpha;
    }

    private void SetImmediateHidden()
    {
        if (group != null)
            group.alpha = 0f;
        if (root != null)
            root.localScale = Vector3.one * visibleScale * 0.94f;
        InvalidateSnapshot();
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

    private static Image CreateImage(Transform parent, string name, Vector2 size)
    {
        RectTransform rect = CreateRect(parent, name, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.preserveAspect = true;
        return image;
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

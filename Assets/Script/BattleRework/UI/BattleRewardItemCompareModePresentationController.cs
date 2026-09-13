using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// BattleRewardItemCompareController 위에 얹는 순수 Presentation 보강 레이어입니다.
///
/// 책임:
/// - 비교창 상단에 MERGE / REPLACE를 즉시 식별할 수 있는 강한 Action Banner를 표시합니다.
/// - MERGE에서는 우측 칼럼을 Incoming 아이템이 아니라 실제 합성 후 예상 결과로 설명합니다.
/// - Grade / Copy 진행과 현재 Runtime Effect 수치를 함께 보여줍니다.
/// - 실제 장비 교체/합성 데이터는 변경하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(61030)]
public sealed class BattleRewardItemCompareModePresentationController : MonoBehaviour
{
    [Header("AUTO REFERENCES")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRewardFlow rewardFlow;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleInventoryInteractionController inventoryInteraction;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;

    [Header("THEME")]
    [SerializeField] private Color ink = new(0.024f, 0.025f, 0.034f, 0.995f);
    [SerializeField] private Color paper = new(0.95f, 0.92f, 0.80f, 1f);
    [SerializeField] private Color cyan = new(0.12f, 0.90f, 0.94f, 1f);
    [SerializeField] private Color yellow = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Color pink = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField] private Color muted = new(0.57f, 0.60f, 0.67f, 1f);

    private RectTransform compareRoot;
    private RectTransform currentSideRoot;
    private RectTransform incomingSideRoot;

    private RectTransform bannerRoot;
    private Image bannerBack;
    private Outline bannerOutline;
    private Image badgeBack;
    private Text badgeText;
    private Text bannerHeadline;
    private Text bannerSlot;
    private Image bannerAccent;

    private Text oldHeader;
    private Text oldSubHeader;
    private RectTransform oldTopStroke;
    private Text actionLabel;

    private Text currentEyebrow;
    private Text currentParamTitle;
    private Text incomingEyebrow;
    private Text incomingLevel;
    private Text incomingDescription;
    private Text incomingParamTitle;
    private Text incomingStats;

    private float nextResolveAt;

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
        if (UnityEngine.Object.FindFirstObjectByType<BattleRewardItemCompareModePresentationController>(FindObjectsInactive.Include) != null)
            return;

        BattleRewardFlow flow = UnityEngine.Object.FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        BattleRunManager run = UnityEngine.Object.FindFirstObjectByType<BattleRunManager>(FindObjectsInactive.Include);
        BattleInventoryInteractionController interaction =
            UnityEngine.Object.FindFirstObjectByType<BattleInventoryInteractionController>(FindObjectsInactive.Include);

        GameObject host = flow != null
            ? flow.gameObject
            : interaction != null
                ? interaction.gameObject
                : run != null
                    ? run.gameObject
                    : GameObject.Find("BattleSystems");

        if (host != null)
            host.AddComponent<BattleRewardItemCompareModePresentationController>();
    }

    private void Awake()
    {
        ResolveReferences(true);
        ResolveCompareUi(true);
    }

    private void OnEnable()
    {
        ResolveReferences(true);
        ResolveCompareUi(true);
        nextResolveAt = 0f;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextResolveAt)
            return;

        nextResolveAt = Time.unscaledTime + 0.15f;
        ResolveReferences(false);
        ResolveCompareUi(false);
    }

    private void LateUpdate()
    {
        ResolveCompareUi(false);

        if (!TryResolveComparison(
                out int targetIndex,
                out BattleEquipmentSlot current,
                out BattleEquipmentStack incoming,
                out bool mergeMode,
                out bool mergeBlocked))
            return;

        EnsureBannerUi();
        if (bannerRoot == null)
            return;

        Vector2Int grid = BattleEquipmentSystem.SlotIndexToGrid(targetIndex);
        ApplyActionBanner(grid, mergeMode, mergeBlocked);

        if (mergeMode)
        {
            ApplyMergeResultPreview(current, incoming, mergeBlocked);
        }
        else
        {
            ApplyReplacePresentation();
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

        targetIndex = inventoryInteraction.PadModeActive
            ? inventoryInteraction.PadSelectedSlot
            : inventoryInteraction.HoveredSlot;

        if (targetIndex < 0 ||
            !equipmentSystem.TryGetSlot(targetIndex, out current) ||
            current == null || current.equipment == null)
            return false;

        mergeMode = rewardFlow.HandIsChosenReward &&
                    rewardFlow.ChosenReward != null &&
                    incoming.equipment == current.equipment &&
                    current.equipment == rewardFlow.ChosenReward;
        mergeBlocked = mergeMode && current.grade >= 3;
        return true;
    }

    private void ApplyActionBanner(Vector2Int grid, bool mergeMode, bool mergeBlocked)
    {
        Color modeColor;
        string badge;
        string headline;

        if (mergeMode)
        {
            if (mergeBlocked)
            {
                modeColor = pink;
                badge = "MERGE MAX";
                headline = "MERGE BLOCKED  //  MAX GRADE";
            }
            else
            {
                modeColor = cyan;
                badge = "MERGE";
                headline = "SAME ITEM  //  EXPECTED RESULT AFTER MERGE";
            }
        }
        else
        {
            modeColor = yellow;
            badge = "REPLACE";
            headline = "CURRENT ITEM OUT  //  NEW ITEM IN";
        }

        bannerBack.color = new Color(modeColor.r, modeColor.g, modeColor.b, 0.13f);
        bannerOutline.effectColor = new Color(modeColor.r, modeColor.g, modeColor.b, 0.96f);
        badgeBack.color = modeColor;
        badgeText.text = badge;
        badgeText.color = ink;
        bannerHeadline.text = headline;
        bannerHeadline.color = paper;
        bannerSlot.text = $"PACK {grid.x + 1}-{grid.y + 1}";
        bannerSlot.color = modeColor;
        bannerAccent.color = modeColor;

        if (actionLabel != null)
        {
            actionLabel.text = mergeMode
                ? mergeBlocked ? "×\nMAX" : "+\nMERGE"
                : "→\nREPLACE";
            actionLabel.color = mergeMode ? (mergeBlocked ? pink : cyan) : pink;
            actionLabel.fontSize = mergeMode ? 16 : 13;
            actionLabel.fontStyle = FontStyle.Bold;
        }
    }

    private void ApplyMergeResultPreview(
        BattleEquipmentSlot current,
        BattleEquipmentStack incoming,
        bool mergeBlocked)
    {
        if (current == null || current.equipment == null || incoming.equipment == null)
            return;

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

        if (currentEyebrow != null)
            currentEyebrow.text = "CURRENT // BEFORE MERGE";
        if (currentParamTitle != null)
            currentParamTitle.text = "CURRENT EFFECT SPEC";

        if (incomingEyebrow != null)
        {
            incomingEyebrow.text = mergeBlocked
                ? "MERGED RESULT // BLOCKED"
                : "MERGED RESULT // EXPECTED";
            incomingEyebrow.color = mergeBlocked ? pink : cyan;
        }

        if (incomingLevel != null)
        {
            string progress = mergeBlocked
                ? "MAX GRADE // NO CHANGE"
                : resultGrade > current.grade
                    ? "LEVEL UP"
                    : "STACK +1";

            incomingLevel.text = $"AFTER  LV.{resultGrade}   COPY {resultCopies}/3   //   {progress}";
            incomingLevel.color = mergeBlocked ? pink : cyan;
        }

        if (incomingParamTitle != null)
        {
            incomingParamTitle.text = "EXPECTED EFFECT SPEC";
            incomingParamTitle.color = mergeBlocked ? pink : cyan;
        }

        if (incomingStats != null)
        {
            incomingStats.text = BuildExpectedMergeStats(
                current.equipment,
                current.grade,
                current.copies,
                resultGrade,
                resultCopies,
                mergeBlocked);
            incomingStats.fontSize = 12;
            incomingStats.lineSpacing = 1.08f;
        }

        if (incomingDescription != null)
        {
            string description = string.IsNullOrWhiteSpace(current.equipment.description)
                ? BuildFallbackDescription(current.equipment)
                : current.equipment.description.Trim();

            incomingDescription.text = mergeBlocked
                ? $"MAX GRADE // MERGE DOES NOT APPLY\n{description}"
                : $"PROJECTED STACK  //  LV.{current.grade} COPY {current.copies}/3  →  LV.{resultGrade} COPY {resultCopies}/3\n{description}";
        }
    }

    private void ApplyReplacePresentation()
    {
        if (currentEyebrow != null)
            currentEyebrow.text = "CURRENT // OUT";
        if (currentParamTitle != null)
            currentParamTitle.text = "CURRENT EFFECT SPEC";

        if (incomingEyebrow != null)
            incomingEyebrow.text = "NEW ITEM // IN";
        if (incomingParamTitle != null)
            incomingParamTitle.text = "INCOMING EFFECT SPEC";

        if (incomingStats != null)
        {
            incomingStats.fontSize = 13;
            incomingStats.lineSpacing = 1.18f;
        }
    }

    private static string BuildExpectedMergeStats(
        BattleEquipmentSO equipment,
        int currentGrade,
        int currentCopies,
        int resultGrade,
        int resultCopies,
        bool mergeBlocked)
    {
        StringBuilder sb = new();
        sb.Append($"DMG      ×{equipment.damageMultiplier:0.00}   <color=#8F98A8>— SAME</color>\n");
        sb.Append($"MOVE     ×{equipment.moveSpeedMultiplier:0.00}   <color=#8F98A8>— SAME</color>\n");
        sb.Append($"RANGE    ×{equipment.rangeMultiplier:0.00}   <color=#8F98A8>— SAME</color>\n");

        if (mergeBlocked)
        {
            sb.Append($"LV       {currentGrade} → {resultGrade}   <color=#FF3A7D>× MAX</color>\n");
            sb.Append($"COPY     {currentCopies}/3 → {resultCopies}/3   <color=#8F98A8>— NO CHANGE</color>");
            return sb.ToString();
        }

        if (resultGrade > currentGrade)
            sb.Append($"LV       {currentGrade} → {resultGrade}   <color=#20E6EC>▲ LEVEL UP</color>\n");
        else
            sb.Append($"LV       {currentGrade} → {resultGrade}   <color=#8F98A8>— SAME</color>\n");

        if (resultGrade > currentGrade)
            sb.Append($"COPY     {currentCopies}/3 → {resultCopies}/3   <color=#20E6EC>↗ CONVERTED</color>");
        else
            sb.Append($"COPY     {currentCopies}/3 → {resultCopies}/3   <color=#20E6EC>▲ +1</color>");

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
    }

    private void ResolveCompareUi(bool force)
    {
        RectTransform detailRoot = detailController != null ? detailController.Root : null;
        RectTransform parent = detailRoot != null ? detailRoot.parent as RectTransform : null;
        RectTransform resolved = parent != null ? parent.Find("RewardEquipmentCompareDetail") as RectTransform : null;

        if (!force && compareRoot == resolved)
            return;

        compareRoot = resolved;
        currentSideRoot = null;
        incomingSideRoot = null;
        bannerRoot = null;
        bannerBack = null;
        bannerOutline = null;
        badgeBack = null;
        badgeText = null;
        bannerHeadline = null;
        bannerSlot = null;
        bannerAccent = null;
        oldHeader = null;
        oldSubHeader = null;
        oldTopStroke = null;
        actionLabel = null;
        currentEyebrow = null;
        currentParamTitle = null;
        incomingEyebrow = null;
        incomingLevel = null;
        incomingDescription = null;
        incomingParamTitle = null;
        incomingStats = null;

        if (compareRoot == null)
            return;

        currentSideRoot = compareRoot.Find("CurrentSide") as RectTransform;
        incomingSideRoot = compareRoot.Find("IncomingSide") as RectTransform;

        oldHeader = FindText(compareRoot, "Header");
        oldSubHeader = FindText(compareRoot, "SubHeader");
        oldTopStroke = compareRoot.Find("TopStroke") as RectTransform;
        actionLabel = FindText(compareRoot, "Action");

        if (currentSideRoot != null)
        {
            currentEyebrow = FindText(currentSideRoot, "Eyebrow");
            currentParamTitle = FindText(currentSideRoot, "ParamTitle");
        }

        if (incomingSideRoot != null)
        {
            incomingEyebrow = FindText(incomingSideRoot, "Eyebrow");
            incomingLevel = FindText(incomingSideRoot, "Level");
            incomingDescription = FindText(incomingSideRoot, "DescriptionBack/Description");
            incomingParamTitle = FindText(incomingSideRoot, "ParamTitle");
            incomingStats = FindText(incomingSideRoot, "Stats");
        }

        EnsureBannerUi();
    }

    private void EnsureBannerUi()
    {
        if (compareRoot == null)
            return;

        if (oldHeader != null)
            oldHeader.enabled = false;
        if (oldSubHeader != null)
            oldSubHeader.enabled = false;

        if (oldTopStroke != null)
            SetAnchors(oldTopStroke, new Vector2(0.035f, 0.885f), new Vector2(0.965f, 0.895f));

        if (bannerRoot != null)
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
            SetAnchors(badge, new Vector2(0.012f, 0.12f), new Vector2(0.225f, 0.88f));
            badgeBack = badge.gameObject.AddComponent<Image>();
            badgeBack.raycastTarget = false;

            badgeText = CreateText(badge, "REPLACE", 22, FontStyle.Bold, TextAnchor.MiddleCenter, ink, "Text");
            SetAnchors(badgeText.rectTransform, Vector2.zero, Vector2.one);
            badgeText.resizeTextForBestFit = true;
            badgeText.resizeTextMinSize = 13;
            badgeText.resizeTextMaxSize = 22;

            bannerHeadline = CreateText(
                bannerRoot,
                "CURRENT ITEM OUT  //  NEW ITEM IN",
                14,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                paper,
                "Headline");
            SetAnchors(bannerHeadline.rectTransform, new Vector2(0.25f, 0.12f), new Vector2(0.75f, 0.88f));
            bannerHeadline.resizeTextForBestFit = true;
            bannerHeadline.resizeTextMinSize = 10;
            bannerHeadline.resizeTextMaxSize = 14;

            bannerSlot = CreateText(bannerRoot, "PACK 1-1", 12, FontStyle.Bold, TextAnchor.MiddleRight, yellow, "Slot");
            SetAnchors(bannerSlot.rectTransform, new Vector2(0.75f, 0.12f), new Vector2(0.978f, 0.88f));

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

    private static Text FindText(Transform root, string path)
    {
        if (root == null)
            return null;

        Transform found = root.Find(path);
        return found != null ? found.GetComponent<Text>() : null;
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

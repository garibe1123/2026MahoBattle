using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum BattleRulePolarity
{
    Benefit,
    Penalty,
    Mixed,
    RuleChange
}

/// <summary>
/// 룰렛 한 칸의 데이터입니다.
/// 별 등급은 룰의 "개수"를 정하고, low/high weight는 높은 별에서 위험 룰이 조금 더 자주
/// 등장하도록 평균만 기울입니다. 높은 별이어도 GOOD 룰은 계속 등장할 수 있습니다.
/// </summary>
[Serializable]
public sealed class BattleRuleDefinition
{
    public string id;
    public string displayName;
    [TextArea] public string description;
    [Tooltip("룰 슬롯에 표시할 아이콘. 비어 있으면 짧은 룰 이름을 표시합니다.")]
    public Sprite icon;
    public BattleRulePolarity polarity;
    public string exclusiveGroup;
    [Range(1, 5)] public int minimumStars = 1;
    [Min(0f)] public float lowStarWeight = 1f;
    [Min(0f)] public float highStarWeight = 1f;

    [Header("Runtime Multipliers")]
    [Min(0f)] public float playerDamageMultiplier = 1f;
    [Min(0f)] public float playerMoveSpeedMultiplier = 1f;
    [Min(0f)] public float enemyHpMultiplier = 1f;
    [Min(0f)] public float enemyDamageMultiplier = 1f;
    [Min(0f)] public float enemyMoveSpeedMultiplier = 1f;
    [Min(0f)] public float healingMultiplier = 1f;
    [Min(0f)] public float killPointMultiplier = 1f;
    [Range(0f, 1f)] public float preCombatHealFraction;
}

/// <summary>
/// 한 Encounter에 실제로 확정된 룰과 합산 결과.
/// 룰은 합연산이 아니라 곱연산으로 누적해 서로 다른 조합이 자연스럽게 중첩됩니다.
/// </summary>
[Serializable]
public sealed class BattleRuleSet
{
    [SerializeField] private int starRating = 1;
    [SerializeField] private List<BattleRuleDefinition> selectedRules = new();

    [SerializeField] private float playerDamageMultiplier = 1f;
    [SerializeField] private float playerMoveSpeedMultiplier = 1f;
    [SerializeField] private float enemyHpMultiplier = 1f;
    [SerializeField] private float enemyDamageMultiplier = 1f;
    [SerializeField] private float enemyMoveSpeedMultiplier = 1f;
    [SerializeField] private float healingMultiplier = 1f;
    [SerializeField] private float killPointMultiplier = 1f;
    [SerializeField] private float preCombatHealFraction;

    public int StarRating => Mathf.Clamp(starRating, 1, 5);
    public IReadOnlyList<BattleRuleDefinition> SelectedRules => selectedRules;
    public float PlayerDamageMultiplier => playerDamageMultiplier;
    public float PlayerMoveSpeedMultiplier => playerMoveSpeedMultiplier;
    public float EnemyHpMultiplier => enemyHpMultiplier;
    public float EnemyDamageMultiplier => enemyDamageMultiplier;
    public float EnemyMoveSpeedMultiplier => enemyMoveSpeedMultiplier;
    public float HealingMultiplier => healingMultiplier;
    public float KillPointMultiplier => killPointMultiplier;
    public float PreCombatHealFraction => preCombatHealFraction;

    public BattleRuleSet(int stars)
    {
        starRating = Mathf.Clamp(stars, 1, 5);
    }

    internal void Add(BattleRuleDefinition rule)
    {
        if (rule == null)
            return;

        selectedRules.Add(rule);
        playerDamageMultiplier *= Mathf.Max(0f, rule.playerDamageMultiplier);
        playerMoveSpeedMultiplier *= Mathf.Max(0f, rule.playerMoveSpeedMultiplier);
        enemyHpMultiplier *= Mathf.Max(0.01f, rule.enemyHpMultiplier);
        enemyDamageMultiplier *= Mathf.Max(0f, rule.enemyDamageMultiplier);
        enemyMoveSpeedMultiplier *= Mathf.Max(0f, rule.enemyMoveSpeedMultiplier);
        healingMultiplier *= Mathf.Max(0f, rule.healingMultiplier);
        killPointMultiplier *= Mathf.Max(0f, rule.killPointMultiplier);
        preCombatHealFraction += Mathf.Clamp01(rule.preCombatHealFraction);
    }
}

/// <summary>
/// 전투 직전 Battle Rating 룰렛의 Runtime Owner.
/// 별 개수만큼 룰을 뽑고, 간단한 방송 Overlay로 한 칸씩 공개한 뒤 BattleRunManager에 결과를 돌려줍니다.
/// 별이 높을수록 평균적으로 Penalty 쪽 가중치가 올라가지만 GOOD/MIXED 룰도 계속 포함됩니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleRuleRouletteController : MonoBehaviour
{
    [Header("Roulette Timing")]
    [SerializeField, Min(0f)] private float ratingIntroDuration = 0.55f;
    [SerializeField, Min(0.05f)] private float spinDurationPerRule = 0.48f;
    [SerializeField, Min(0f)] private float revealDurationPerRule = 0.58f;
    [SerializeField, Min(0f)] private float summaryDuration = 0.85f;

    [Header("Rules")]
    [SerializeField] private List<BattleRuleDefinition> rules = new();

    [Header("Optional Rule UI Art")]
    [SerializeField] private Sprite ruleSlotFrameSprite;
    [SerializeField] private Sprite spinButtonSprite;
    [SerializeField] private Sprite starOffSprite;
    [SerializeField] private Sprite starOnSprite;

    [Header("Rule Slot Layout")]
    [SerializeField, Min(48f)] private float ruleSlotSize = 104f;
    [SerializeField, Min(0f)] private float ruleSlotSpacing = 14f;

    [Header("Combat HUD Transition")]
    [SerializeField, Min(0.05f)] private float hudTransitionDuration = 0.28f;
    [SerializeField, Range(0.45f, 1f)] private float combatHudScale = 0.72f;
    [SerializeField] private Vector2 combatHudMargin = new(42f, 38f);
    [SerializeField, Range(1f, 1.35f)] private float ruleHoverScale = 1.14f;

    [Header("Rule Confirm Punch")]
    [SerializeField, Range(1f, 1.4f)] private float ruleConfirmScale = 1.18f;
    [SerializeField, Min(0.04f)] private float ruleConfirmGrowDuration = 0.08f;
    [SerializeField, Min(0.04f)] private float ruleConfirmReturnDuration = 0.14f;

    [Header("Rule Detail Hover")]
    [SerializeField, Min(12f)] private float ruleDetailButtonDrop = 140f;
    [SerializeField, Min(0.05f)] private float ruleDetailTweenDuration = 0.18f;

    private GameObject uiRoot;
    private RectTransform machineTab;
    private RectTransform winningRuleTab;
    private RectTransform resultListTab;
    private RectTransform controlTab;
    private RectTransform ruleSlotRow;

    private readonly List<Image> ratingStarImages = new();
    private readonly List<RuleSlotView> ruleSlotViews = new();

    private Text ratingText;
    private Text progressText;
    private Text winningRuleTypeText;
    private Text winningRuleNameText;
    private Text winningRuleDescriptionText;
    private Text spinButtonText;
    private Button spinButton;
    private Image spinButtonImage;
    private Image backdropImage;
    private BattleRuleDefinition lastRevealedRule;
    private Coroutine detailLayoutTweenRoutine;
    private Vector2 controlTabRestPosition;
    private bool cancelRequested;
    private bool startBattleConfirmed;
    private bool combatHudMode;
    private bool finalReviewMode;

    private sealed class RuleSlotView
    {
        public RectTransform root;
        public Image frame;
        public Image icon;
        public Text fallbackLabel;
        public BattleRuleSlotPointerFeedback pointerFeedback;
        public Coroutine confirmPunchRoutine;
    }

    public IReadOnlyList<BattleRuleDefinition> Rules => rules;
    public RectTransform MachineTab => machineTab;
    public RectTransform WinningRuleTab => winningRuleTab;
    public RectTransform ResultListTab => resultListTab;
    public RectTransform ControlTab => controlTab;

    public static BattleRuleRouletteController ResolveOrCreate(BattleRunManager owner)
    {
        if (owner == null)
            return null;

        BattleRuleRouletteController controller = owner.GetComponent<BattleRuleRouletteController>();
        if (controller == null)
            controller = owner.gameObject.AddComponent<BattleRuleRouletteController>();

        controller.EnsureDefaultRules();
        return controller;
    }

    private void Awake()
    {
        EnsureDefaultRules();
    }

    private void OnDisable()
    {
        CancelPresentation();
    }

    public void CancelPresentation()
    {
        cancelRequested = true;
        startBattleConfirmed = false;
        combatHudMode = false;
        finalReviewMode = false;
        lastRevealedRule = null;

        if (detailLayoutTweenRoutine != null)
        {
            StopCoroutine(detailLayoutTweenRoutine);
            detailLayoutTweenRoutine = null;
        }

        SetSpinButtonInteractable(false);

        if (uiRoot != null)
            uiRoot.SetActive(false);
    }

    public IEnumerator PlayRoulette(int requestedStars, Action<BattleRuleSet> onComplete)
    {
        cancelRequested = false;
        startBattleConfirmed = false;

        EnsureDefaultRules();
        EnsureUi();

        int stars = Mathf.Clamp(requestedStars, 1, 5);
        BattleRuleSet result = Roll(stars);

        uiRoot.SetActive(true);
        ResetPresentationLayout();
        SetRating(stars);
        BuildRuleSlots(stars);
        ClearWinningRule();

        ratingText.text = $"BATTLE RATING\n{BuildStars(stars)}";
        progressText.text = "RULE ROULETTE";
        SetSpinButtonLabel("RULE SPIN");
        SetSpinButtonInteractable(false);

        if (ratingIntroDuration > 0f)
            yield return new WaitForSecondsRealtime(ratingIntroDuration);

        for (int i = 0; i < result.SelectedRules.Count; i++)
        {
            if (cancelRequested)
                yield break;

            BattleRuleDefinition selected = result.SelectedRules[i];
            progressText.text = $"RULE {i + 1} / {result.SelectedRules.Count}";
            SetSpinButtonLabel("SPINNING");

            float end = Time.unscaledTime + Mathf.Max(0.05f, spinDurationPerRule);
            while (Time.unscaledTime < end)
            {
                BattleRuleDefinition preview = GetRandomPreview(stars);
                if (preview != null)
                    ShowRuleInSlot(i, preview, previewOnly: true);

                yield return new WaitForSecondsRealtime(0.055f);
            }

            if (cancelRequested)
                yield break;

            ShowRuleInSlot(i, selected, previewOnly: false);
            ShowWinningRule(selected);

            SetSpinButtonLabel(
                i + 1 < result.SelectedRules.Count
                    ? "NEXT SPIN"
                    : "CHECK RULES");

            if (revealDurationPerRule > 0f)
                yield return new WaitForSecondsRealtime(revealDurationPerRule);
        }

        if (cancelRequested)
            yield break;

        // 최종 확인 상태에서는 START BATTLE을 기본으로 보여주고,
        // 상세 설명은 룰 아이콘 Hover 중에만 노출합니다.
        progressText.text = "THIS BATTLE";
        EnterFinalReviewMode();
        SetSpinButtonLabel("START BATTLE");
        SetSpinButtonInteractable(true);

        while (!startBattleConfirmed)
        {
            if (cancelRequested)
            {
                SetSpinButtonInteractable(false);
                yield break;
            }

            yield return null;
        }

        SetSpinButtonInteractable(false);
        finalReviewMode = false;
        HideRuleDetail();
        ResetControlTabPositionImmediate();

        // 결과 아이콘은 꺼버리지 않고 좌측 상단 HUD로 자연스럽게 이동시킵니다.
        yield return TransitionToCombatHud();

        onComplete?.Invoke(result);
    }

    public BattleRuleSet Roll(int requestedStars)
    {
        EnsureDefaultRules();

        int stars = Mathf.Clamp(requestedStars, 1, 5);
        BattleRuleSet result = new(stars);
        HashSet<string> usedIds = new();
        HashSet<string> usedGroups = new();

        for (int draw = 0; draw < stars; draw++)
        {
            BattleRuleDefinition selected = PickWeightedRule(stars, usedIds, usedGroups);
            if (selected == null)
                break;

            result.Add(selected);

            if (!string.IsNullOrWhiteSpace(selected.id))
                usedIds.Add(selected.id);
            if (!string.IsNullOrWhiteSpace(selected.exclusiveGroup))
                usedGroups.Add(selected.exclusiveGroup);
        }

        return result;
    }

    private BattleRuleDefinition PickWeightedRule(
        int stars,
        HashSet<string> usedIds,
        HashSet<string> usedGroups)
    {
        float total = 0f;
        float t = (Mathf.Clamp(stars, 1, 5) - 1f) / 4f;

        for (int i = 0; i < rules.Count; i++)
        {
            BattleRuleDefinition rule = rules[i];
            if (!CanSelect(rule, stars, usedIds, usedGroups))
                continue;

            total += Mathf.Max(0f, Mathf.Lerp(rule.lowStarWeight, rule.highStarWeight, t));
        }

        if (total <= 0.0001f)
            return null;

        float roll = UnityEngine.Random.value * total;
        for (int i = 0; i < rules.Count; i++)
        {
            BattleRuleDefinition rule = rules[i];
            if (!CanSelect(rule, stars, usedIds, usedGroups))
                continue;

            roll -= Mathf.Max(0f, Mathf.Lerp(rule.lowStarWeight, rule.highStarWeight, t));
            if (roll <= 0f)
                return rule;
        }

        return null;
    }

    private static bool CanSelect(
        BattleRuleDefinition rule,
        int stars,
        HashSet<string> usedIds,
        HashSet<string> usedGroups)
    {
        if (rule == null || string.IsNullOrWhiteSpace(rule.id))
            return false;
        if (stars < Mathf.Clamp(rule.minimumStars, 1, 5))
            return false;
        if (usedIds.Contains(rule.id))
            return false;
        if (!string.IsNullOrWhiteSpace(rule.exclusiveGroup) && usedGroups.Contains(rule.exclusiveGroup))
            return false;
        return true;
    }

    private BattleRuleDefinition GetRandomPreview(int stars)
    {
        List<BattleRuleDefinition> candidates = new();
        for (int i = 0; i < rules.Count; i++)
        {
            BattleRuleDefinition rule = rules[i];
            if (rule != null && stars >= Mathf.Clamp(rule.minimumStars, 1, 5))
                candidates.Add(rule);
        }

        return candidates.Count > 0
            ? candidates[UnityEngine.Random.Range(0, candidates.Count)]
            : null;
    }

    private void EnsureDefaultRules()
    {
        if (rules != null && rules.Count > 0)
            return;

        rules ??= new List<BattleRuleDefinition>();
        rules.Clear();

        rules.Add(Make(
            "power_play", "Power Play", "Player damage +25%",
            BattleRulePolarity.Benefit, "PlayerPower", 1, 1.45f, 0.72f,
            playerDamage: 1.25f));

        rules.Add(Make(
            "second_wind", "Second Wind", "Recover 15% max HP before combat",
            BattleRulePolarity.Benefit, "Healing", 1, 1.30f, 0.55f,
            preHeal: 0.15f));

        rules.Add(Make(
            "bounty_night", "Bounty Night", "Monster Kill Point x1.75",
            BattleRulePolarity.Benefit, "Economy", 1, 1.20f, 0.82f,
            killPoint: 1.75f));

        rules.Add(Make(
            "light_feet", "Light Feet", "Player move speed +18%",
            BattleRulePolarity.Benefit, "Movement", 1, 1.15f, 0.62f,
            playerMove: 1.18f));

        rules.Add(Make(
            "armored_cast", "Armored Cast", "Enemy HP +25%",
            BattleRulePolarity.Penalty, "EnemyBody", 1, 0.58f, 1.35f,
            enemyHp: 1.25f));

        rules.Add(Make(
            "hard_hitters", "Hard Hitters", "Enemy damage +25%",
            BattleRulePolarity.Penalty, "EnemyDamage", 1, 0.55f, 1.50f,
            enemyDamage: 1.25f));

        rules.Add(Make(
            "rushing_mob", "Rushing Mob", "Enemy move speed +22%",
            BattleRulePolarity.Penalty, "Movement", 1, 0.62f, 1.28f,
            enemyMove: 1.22f));

        rules.Add(Make(
            "no_rest", "No Rest", "Healing effectiveness -50%",
            BattleRulePolarity.Penalty, "Healing", 2, 0.22f, 1.12f,
            healing: 0.50f));

        rules.Add(Make(
            "glass_cannon", "Glass Cannon", "Player damage +45%, enemy damage +45%",
            BattleRulePolarity.Mixed, "DamageTrade", 1, 0.72f, 1.12f,
            playerDamage: 1.45f, enemyDamage: 1.45f));

        rules.Add(Make(
            "blood_money", "Blood Money", "Enemy damage +20%, Kill Point x2",
            BattleRulePolarity.Mixed, "Economy", 1, 0.78f, 1.12f,
            enemyDamage: 1.20f, killPoint: 2f));

        rules.Add(Make(
            "short_fuse", "Short Fuse", "Enemy HP -20%, enemy damage +35%",
            BattleRulePolarity.Mixed, "EnemyBody", 1, 0.72f, 1.02f,
            enemyHp: 0.80f, enemyDamage: 1.35f));

        rules.Add(Make(
            "frenzy", "Frenzy", "Everyone moves faster",
            BattleRulePolarity.RuleChange, "Movement", 2, 0.52f, 1.02f,
            playerMove: 1.18f, enemyMove: 1.30f));

        rules.Add(Make(
            "no_heal_show", "No-Heal Show", "Healing disabled, Kill Point x1.5",
            BattleRulePolarity.RuleChange, "Healing", 3, 0.12f, 0.88f,
            healing: 0f, killPoint: 1.5f));
    }

    private static BattleRuleDefinition Make(
        string id,
        string name,
        string description,
        BattleRulePolarity polarity,
        string group,
        int minStars,
        float lowWeight,
        float highWeight,
        float playerDamage = 1f,
        float playerMove = 1f,
        float enemyHp = 1f,
        float enemyDamage = 1f,
        float enemyMove = 1f,
        float healing = 1f,
        float killPoint = 1f,
        float preHeal = 0f)
    {
        return new BattleRuleDefinition
        {
            id = id,
            displayName = name,
            description = description,
            polarity = polarity,
            exclusiveGroup = group,
            minimumStars = minStars,
            lowStarWeight = lowWeight,
            highStarWeight = highWeight,
            playerDamageMultiplier = playerDamage,
            playerMoveSpeedMultiplier = playerMove,
            enemyHpMultiplier = enemyHp,
            enemyDamageMultiplier = enemyDamage,
            enemyMoveSpeedMultiplier = enemyMove,
            healingMultiplier = healing,
            killPointMultiplier = killPoint,
            preCombatHealFraction = preHeal
        };
    }

    private void EnsureUi()
    {
        if (uiRoot != null)
            return;

        uiRoot = new GameObject("BattleRuleRouletteCanvas");
        uiRoot.transform.SetParent(transform, false);

        Canvas canvas = uiRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2400;

        CanvasScaler scaler = uiRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        uiRoot.AddComponent<GraphicRaycaster>();

        RectTransform backdrop = CreateRect(uiRoot.transform, "Backdrop");
        Stretch(backdrop);

        backdropImage = backdrop.gameObject.AddComponent<Image>();
        backdropImage.color = new Color(0f, 0f, 0f, 0.68f);
        backdropImage.raycastTarget = true;

        machineTab = CreateBareTab(
            backdrop,
            "MachineTab",
            new Vector2(0.5f, 0.70f),
            new Vector2(680f, 150f));

        resultListTab = CreateBareTab(
            backdrop,
            "ResultListTab",
            new Vector2(0.5f, 0.53f),
            new Vector2(680f, Mathf.Max(48f, ruleSlotSize)));

        winningRuleTab = CreateBareTab(
            backdrop,
            "WinningRuleTab",
            new Vector2(0.5f, 0.37f),
            new Vector2(620f, 88f));

        Image detailBar = winningRuleTab.gameObject.AddComponent<Image>();
        detailBar.color = new Color(0.025f, 0.028f, 0.038f, 0.94f);
        detailBar.raycastTarget = false;

        controlTab = CreateBareTab(
            backdrop,
            "ControlTab",
            new Vector2(0.5f, 0.42f),
            new Vector2(260f, 86f));
        controlTabRestPosition = controlTab.anchoredPosition;

        ratingText = CreateText(machineTab, "Rating", 28, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetRect(ratingText.rectTransform, new Vector2(0.05f, 0.48f), new Vector2(0.95f, 0.96f));

        RectTransform starsRoot = CreateRect(machineTab, "RatingStars");
        starsRoot.anchorMin = starsRoot.anchorMax = new Vector2(0.5f, 0.45f);
        starsRoot.pivot = new Vector2(0.5f, 0.5f);
        starsRoot.sizeDelta = new Vector2(330f, 40f);
        starsRoot.anchoredPosition = Vector2.zero;
        BuildStarImages(starsRoot);

        progressText = CreateText(machineTab, "Progress", 15, FontStyle.Bold, TextAnchor.MiddleCenter);
        progressText.color = new Color(0.16f, 0.86f, 0.92f, 1f);
        SetRect(progressText.rectTransform, new Vector2(0.10f, 0.02f), new Vector2(0.90f, 0.25f));

        ruleSlotRow = resultListTab;
        HorizontalLayoutGroup layout = resultListTab.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = Mathf.Max(0f, ruleSlotSpacing);
        // 슬롯 개수와 관계없이 "슬롯 묶음 전체"를 중앙 기준으로 배치합니다.
        // 1개면 정중앙, 2개면 중앙을 기준으로 좌우 대칭, 5개면 동일 간격으로 펼쳐집니다.
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        winningRuleTypeText = CreateText(winningRuleTab, "RuleType", 13, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetRect(winningRuleTypeText.rectTransform, new Vector2(0.05f, 0.68f), new Vector2(0.95f, 0.94f));

        winningRuleNameText = CreateText(winningRuleTab, "RuleName", 20, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetRect(winningRuleNameText.rectTransform, new Vector2(0.05f, 0.34f), new Vector2(0.95f, 0.70f));

        winningRuleDescriptionText = CreateText(winningRuleTab, "RuleDescription", 13, FontStyle.Normal, TextAnchor.MiddleCenter);
        SetRect(winningRuleDescriptionText.rectTransform, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.38f));

        RectTransform buttonRect = CreateRect(controlTab, "StartBattleButton");
        Stretch(buttonRect);

        spinButtonImage = buttonRect.gameObject.AddComponent<Image>();
        spinButtonImage.sprite = spinButtonSprite;
        spinButtonImage.type = spinButtonSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        spinButtonImage.color = spinButtonSprite != null
            ? Color.white
            : new Color(0.92f, 0.25f, 0.12f, 1f);
        spinButtonImage.raycastTarget = true;

        spinButton = buttonRect.gameObject.AddComponent<Button>();
        spinButton.interactable = false;
        spinButton.transition = Selectable.Transition.None;
        spinButton.onClick.AddListener(ConfirmStartBattle);

        spinButtonText = CreateText(buttonRect, "Label", 20, FontStyle.Bold, TextAnchor.MiddleCenter);
        Stretch(spinButtonText.rectTransform);

        uiRoot.SetActive(false);
    }

    private static RectTransform CreateBareTab(
        Transform parent,
        string name,
        Vector2 normalizedAnchor,
        Vector2 size)
    {
        RectTransform rect = CreateRect(parent, name);
        rect.anchorMin = rect.anchorMax = normalizedAnchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        return rect;
    }

    private void BuildRuleSlots(int count)
    {
        count = Mathf.Clamp(count, 1, 5);

        for (int i = ruleSlotViews.Count - 1; i >= 0; i--)
        {
            RuleSlotView view = ruleSlotViews[i];
            if (view?.confirmPunchRoutine != null)
                StopCoroutine(view.confirmPunchRoutine);
            if (view?.root != null)
                Destroy(view.root.gameObject);
        }
        ruleSlotViews.Clear();

        if (ruleSlotRow == null)
            return;

        for (int i = 0; i < count; i++)
        {
            RectTransform root = CreateRect(ruleSlotRow, $"RuleSlot_{i + 1}");
            root.sizeDelta = Vector2.one * Mathf.Max(48f, ruleSlotSize);

            LayoutElement element = root.gameObject.AddComponent<LayoutElement>();
            element.minWidth = ruleSlotSize;
            element.preferredWidth = ruleSlotSize;
            element.minHeight = ruleSlotSize;
            element.preferredHeight = ruleSlotSize;

            Image frame = root.gameObject.AddComponent<Image>();
            frame.sprite = ruleSlotFrameSprite;
            frame.type = ruleSlotFrameSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            frame.color = ruleSlotFrameSprite != null
                ? Color.white
                : new Color(0.08f, 0.085f, 0.11f, 0.98f);
            frame.raycastTarget = true;

            BattleRuleSlotPointerFeedback pointerFeedback =
                root.gameObject.AddComponent<BattleRuleSlotPointerFeedback>();
            pointerFeedback.Configure(this, root);

            RectTransform iconRect = CreateRect(root, "Icon");
            iconRect.anchorMin = new Vector2(0.16f, 0.16f);
            iconRect.anchorMax = new Vector2(0.84f, 0.84f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            Image icon = iconRect.gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;

            Text fallbackLabel = CreateText(root, "FallbackLabel", 11, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetRect(fallbackLabel.rectTransform, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));
            fallbackLabel.text = "?";
            fallbackLabel.color = new Color(1f, 1f, 1f, 0.30f);

            ruleSlotViews.Add(new RuleSlotView
            {
                root = root,
                frame = frame,
                icon = icon,
                fallbackLabel = fallbackLabel,
                pointerFeedback = pointerFeedback
            });
        }
    }

    private void ShowRuleInSlot(int index, BattleRuleDefinition rule, bool previewOnly)
    {
        if (rule == null || index < 0 || index >= ruleSlotViews.Count)
            return;

        RuleSlotView view = ruleSlotViews[index];
        if (view == null)
            return;

        Color polarityColor = GetPolarityColor(rule.polarity);

        if (view.frame != null && ruleSlotFrameSprite == null)
        {
            float mix = previewOnly ? 0.10f : 0.24f;
            view.frame.color = new Color(
                Mathf.Lerp(0.08f, polarityColor.r, mix),
                Mathf.Lerp(0.085f, polarityColor.g, mix),
                Mathf.Lerp(0.11f, polarityColor.b, mix),
                0.98f);
        }

        if (view.icon != null)
        {
            view.icon.sprite = rule.icon;
            view.icon.enabled = rule.icon != null;
            view.icon.color = previewOnly
                ? new Color(1f, 1f, 1f, 0.48f)
                : Color.white;
        }

        if (view.fallbackLabel != null)
        {
            bool fallback = rule.icon == null;
            view.fallbackLabel.gameObject.SetActive(fallback);
            if (fallback)
            {
                view.fallbackLabel.text = ShortRuleLabel(rule.displayName);
                view.fallbackLabel.color = previewOnly
                    ? new Color(1f, 1f, 1f, 0.42f)
                    : Color.white;
            }
        }

        if (!previewOnly)
        {
            if (view.pointerFeedback != null)
                view.pointerFeedback.Bind(rule);

            PlayRuleConfirmPunch(view);
        }
    }

    private void PlayRuleConfirmPunch(RuleSlotView view)
    {
        if (view?.root == null)
            return;

        if (view.confirmPunchRoutine != null)
            StopCoroutine(view.confirmPunchRoutine);

        view.confirmPunchRoutine = StartCoroutine(RuleConfirmPunchRoutine(view));
    }

    private IEnumerator RuleConfirmPunchRoutine(RuleSlotView view)
    {
        if (view?.root == null)
            yield break;

        RectTransform root = view.root;
        Vector3 baseScale = Vector3.one;
        Vector3 peakScale = Vector3.one * Mathf.Max(1f, ruleConfirmScale);

        float growDuration = Mathf.Max(0.04f, ruleConfirmGrowDuration);
        float elapsed = 0f;

        while (elapsed < growDuration && root != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / growDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            root.localScale = Vector3.Lerp(baseScale, peakScale, eased);
            yield return null;
        }

        float returnDuration = Mathf.Max(0.04f, ruleConfirmReturnDuration);
        elapsed = 0f;

        while (elapsed < returnDuration && root != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / returnDuration);
            float eased = t * t * (3f - 2f * t);
            root.localScale = Vector3.Lerp(peakScale, baseScale, eased);
            yield return null;
        }

        if (root != null)
            root.localScale = baseScale;

        if (view != null)
            view.confirmPunchRoutine = null;
    }

    private static string ShortRuleLabel(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "?";

        string text = source.Trim().ToUpperInvariant();
        return text.Length <= 12 ? text : text.Substring(0, 12);
    }

    private void BuildStarImages(Transform parent)
    {
        ratingStarImages.Clear();

        const int count = 5;
        const float spacing = 56f;
        for (int i = 0; i < count; i++)
        {
            RectTransform starRect = CreateRect(parent, $"Star_{i + 1}");
            starRect.anchorMin = starRect.anchorMax = new Vector2(0.5f, 0.5f);
            starRect.pivot = new Vector2(0.5f, 0.5f);
            starRect.sizeDelta = new Vector2(40f, 40f);
            starRect.anchoredPosition = new Vector2((i - 2) * spacing, 0f);

            Image star = starRect.gameObject.AddComponent<Image>();
            star.raycastTarget = false;
            star.preserveAspect = true;
            ratingStarImages.Add(star);
        }
    }

    private void SetRating(int stars)
    {
        stars = Mathf.Clamp(stars, 1, 5);
        bool useSpriteStars = starOffSprite != null || starOnSprite != null;

        for (int i = 0; i < ratingStarImages.Count; i++)
        {
            Image star = ratingStarImages[i];
            if (star == null)
                continue;

            if (!useSpriteStars)
            {
                star.enabled = false;
                continue;
            }

            bool on = i < stars;
            star.enabled = true;
            star.sprite = on
                ? (starOnSprite != null ? starOnSprite : starOffSprite)
                : (starOffSprite != null ? starOffSprite : starOnSprite);
            star.color = on ? Color.white : new Color(1f, 1f, 1f, 0.28f);
        }
    }

    private void ClearWinningRule()
    {
        if (winningRuleTypeText != null)
            winningRuleTypeText.text = string.Empty;
        if (winningRuleNameText != null)
            winningRuleNameText.text = string.Empty;
        if (winningRuleDescriptionText != null)
            winningRuleDescriptionText.text = string.Empty;
    }

    private void ShowWinningRule(BattleRuleDefinition rule)
    {
        if (rule == null)
            return;

        if (winningRuleTab != null && !winningRuleTab.gameObject.activeSelf)
            winningRuleTab.gameObject.SetActive(true);

        lastRevealedRule = rule;
        Color color = GetPolarityColor(rule.polarity);

        if (winningRuleTypeText != null)
        {
            winningRuleTypeText.text = GetPolarityLabel(rule.polarity);
            winningRuleTypeText.color = color;
        }

        if (winningRuleNameText != null)
        {
            winningRuleNameText.text = rule.displayName.ToUpperInvariant();
            winningRuleNameText.color = Color.white;
        }

        if (winningRuleDescriptionText != null)
        {
            winningRuleDescriptionText.text = rule.description;
            winningRuleDescriptionText.color = new Color(0.86f, 0.84f, 0.76f, 1f);
        }
    }

    private void ResetPresentationLayout()
    {
        combatHudMode = false;
        finalReviewMode = false;
        lastRevealedRule = null;

        if (detailLayoutTweenRoutine != null)
        {
            StopCoroutine(detailLayoutTweenRoutine);
            detailLayoutTweenRoutine = null;
        }

        if (backdropImage != null)
        {
            backdropImage.color = new Color(0f, 0f, 0f, 0.68f);
            backdropImage.raycastTarget = true;
        }

        if (machineTab != null)
        {
            machineTab.gameObject.SetActive(true);
            machineTab.localScale = Vector3.one;
        }

        if (controlTab != null)
        {
            controlTab.gameObject.SetActive(true);
            controlTab.localScale = Vector3.one;
            controlTab.anchoredPosition = controlTabRestPosition;
        }

        if (resultListTab != null)
        {
            resultListTab.anchorMin = resultListTab.anchorMax = new Vector2(0.5f, 0.53f);
            resultListTab.pivot = new Vector2(0.5f, 0.5f);
            resultListTab.anchoredPosition = Vector2.zero;
            resultListTab.sizeDelta = new Vector2(680f, Mathf.Max(48f, ruleSlotSize));
            resultListTab.localScale = Vector3.one;

            HorizontalLayoutGroup layout = resultListTab.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.spacing = Mathf.Max(0f, ruleSlotSpacing);
                layout.childAlignment = TextAnchor.MiddleCenter;
            }
        }

        if (winningRuleTab != null)
        {
            winningRuleTab.anchorMin = winningRuleTab.anchorMax = new Vector2(0.5f, 0.37f);
            winningRuleTab.pivot = new Vector2(0.5f, 0.5f);
            winningRuleTab.anchoredPosition = Vector2.zero;
            winningRuleTab.sizeDelta = new Vector2(620f, 88f);
            winningRuleTab.localScale = Vector3.one;
            winningRuleTab.gameObject.SetActive(true);
        }
    }

    private IEnumerator TransitionToCombatHud()
    {
        if (resultListTab == null || winningRuleTab == null)
            yield break;

        combatHudMode = true;

        int count = Mathf.Max(1, ruleSlotViews.Count);
        float activeWidth =
            count * Mathf.Max(48f, ruleSlotSize) +
            Mathf.Max(0, count - 1) * Mathf.Max(0f, ruleSlotSpacing);
        float targetScale = Mathf.Clamp(combatHudScale, 0.45f, 1f);

        Vector2 resultTargetSize = new(activeWidth, Mathf.Max(48f, ruleSlotSize));
        Vector2 detailTargetSize = new(Mathf.Max(340f, activeWidth), 86f);

        RectTransform backdrop = resultListTab.parent as RectTransform;
        if (backdrop == null)
            yield break;

        HorizontalLayoutGroup layout = resultListTab.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
            layout.childAlignment = TextAnchor.MiddleLeft;

        Vector3 startResultWorld = resultListTab.position;
        Vector3 startDetailWorld = winningRuleTab.position;
        Vector3 startResultScale = resultListTab.localScale;
        Vector3 startDetailScale = winningRuleTab.localScale;

        Vector2 resultTargetLocal = new(
            -backdrop.rect.width * 0.5f +
            Mathf.Max(0f, combatHudMargin.x) +
            resultTargetSize.x * targetScale * 0.5f,
            backdrop.rect.height * 0.5f -
            Mathf.Max(0f, combatHudMargin.y) -
            resultTargetSize.y * targetScale * 0.5f);

        Vector2 detailTargetLocal = new(
            -backdrop.rect.width * 0.5f +
            Mathf.Max(0f, combatHudMargin.x) +
            detailTargetSize.x * targetScale * 0.5f,
            backdrop.rect.height * 0.5f -
            Mathf.Max(0f, combatHudMargin.y) -
            resultTargetSize.y * targetScale -
            12f -
            detailTargetSize.y * targetScale * 0.5f);

        Vector3 resultTargetWorld = backdrop.TransformPoint(resultTargetLocal);
        Vector3 detailTargetWorld = backdrop.TransformPoint(detailTargetLocal);
        Color backdropStart = backdropImage != null
            ? backdropImage.color
            : Color.clear;

        float duration = Mathf.Max(0.05f, hudTransitionDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            resultListTab.position = Vector3.Lerp(startResultWorld, resultTargetWorld, eased);
            winningRuleTab.position = Vector3.Lerp(startDetailWorld, detailTargetWorld, eased);
            resultListTab.localScale = Vector3.Lerp(
                startResultScale,
                Vector3.one * targetScale,
                eased);
            winningRuleTab.localScale = Vector3.Lerp(
                startDetailScale,
                Vector3.one * targetScale,
                eased);

            if (backdropImage != null)
            {
                Color faded = backdropStart;
                faded.a = Mathf.Lerp(backdropStart.a, 0f, eased);
                backdropImage.color = faded;
            }

            yield return null;
        }

        // 해상도가 바뀌어도 좌측 상단 여백을 유지하도록 최종 Anchor를 고정합니다.
        resultListTab.anchorMin = resultListTab.anchorMax = new Vector2(0f, 1f);
        resultListTab.pivot = new Vector2(0.5f, 0.5f);
        resultListTab.sizeDelta = resultTargetSize;
        resultListTab.localScale = Vector3.one * targetScale;
        resultListTab.anchoredPosition = new Vector2(
            Mathf.Max(0f, combatHudMargin.x) + resultTargetSize.x * targetScale * 0.5f,
            -Mathf.Max(0f, combatHudMargin.y) - resultTargetSize.y * targetScale * 0.5f);

        winningRuleTab.anchorMin = winningRuleTab.anchorMax = new Vector2(0f, 1f);
        winningRuleTab.pivot = new Vector2(0.5f, 0.5f);
        winningRuleTab.sizeDelta = detailTargetSize;
        winningRuleTab.localScale = Vector3.one * targetScale;
        winningRuleTab.anchoredPosition = new Vector2(
            Mathf.Max(0f, combatHudMargin.x) + detailTargetSize.x * targetScale * 0.5f,
            -Mathf.Max(0f, combatHudMargin.y) -
            resultTargetSize.y * targetScale -
            12f -
            detailTargetSize.y * targetScale * 0.5f);

        if (machineTab != null)
            machineTab.gameObject.SetActive(false);
        if (controlTab != null)
            controlTab.gameObject.SetActive(false);

        if (backdropImage != null)
        {
            backdropImage.color = Color.clear;
            backdropImage.raycastTarget = false;
        }

        HideRuleDetail();
    }

    private void EnterFinalReviewMode()
    {
        finalReviewMode = true;
        ResetControlTabPositionImmediate();
        HideRuleDetail();
    }

    private void HideRuleDetail()
    {
        if (winningRuleTab != null)
            winningRuleTab.gameObject.SetActive(false);
    }

    private void ShowRuleDetail(BattleRuleDefinition rule)
    {
        if (rule == null || winningRuleTab == null)
            return;

        winningRuleTab.gameObject.SetActive(true);
        ShowWinningRule(rule);
    }

    private void ResetControlTabPositionImmediate()
    {
        if (detailLayoutTweenRoutine != null)
        {
            StopCoroutine(detailLayoutTweenRoutine);
            detailLayoutTweenRoutine = null;
        }

        if (controlTab != null)
            controlTab.anchoredPosition = controlTabRestPosition;
    }

    private void TweenControlTabForDetail(bool detailVisible)
    {
        if (!finalReviewMode || controlTab == null)
            return;

        Vector2 target = controlTabRestPosition;
        if (detailVisible)
            target.y -= Mathf.Max(12f, ruleDetailButtonDrop);

        if (detailLayoutTweenRoutine != null)
            StopCoroutine(detailLayoutTweenRoutine);

        detailLayoutTweenRoutine = StartCoroutine(TweenControlTabRoutine(target));
    }

    private IEnumerator TweenControlTabRoutine(Vector2 target)
    {
        if (controlTab == null)
            yield break;

        Vector2 start = controlTab.anchoredPosition;
        float duration = Mathf.Max(0.05f, ruleDetailTweenDuration);
        float elapsed = 0f;

        while (elapsed < duration && controlTab != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // SmoothStep 계열. 설명이 펼쳐질 때 버튼이 튀지 않고 자연스럽게 밀립니다.
            float eased = t * t * (3f - 2f * t);
            controlTab.anchoredPosition = Vector2.Lerp(start, target, eased);
            yield return null;
        }

        if (controlTab != null)
            controlTab.anchoredPosition = target;

        detailLayoutTweenRoutine = null;
    }

    internal float GetRuleHoverScale() => Mathf.Max(1f, ruleHoverScale);

    internal void HandleRuleSlotPointerEnter(BattleRuleDefinition rule)
    {
        if (rule == null)
            return;

        ShowRuleDetail(rule);

        if (finalReviewMode)
            TweenControlTabForDetail(true);
    }

    internal void HandleRuleSlotPointerExit(BattleRuleDefinition rule)
    {
        if (finalReviewMode)
        {
            HideRuleDetail();
            TweenControlTabForDetail(false);
            return;
        }

        if (combatHudMode)
        {
            HideRuleDetail();
            return;
        }

        // 룰렛 추첨 중에는 방금 확정된 결과 설명을 유지합니다.
        if (lastRevealedRule != null)
            ShowWinningRule(lastRevealedRule);
    }

    private void SetSpinButtonLabel(string label)
    {
        if (spinButtonText != null)
            spinButtonText.text = label ?? string.Empty;
    }

    private void SetSpinButtonInteractable(bool interactable)
    {
        if (spinButton == null)
            return;

        spinButton.interactable = interactable;

        if (spinButtonImage != null)
        {
            spinButtonImage.raycastTarget = interactable;
            spinButtonImage.color = interactable
                ? (spinButtonSprite != null ? Color.white : new Color(0.92f, 0.25f, 0.12f, 1f))
                : (spinButtonSprite != null
                    ? new Color(1f, 1f, 1f, 0.42f)
                    : new Color(0.34f, 0.20f, 0.18f, 0.80f));
        }

        if (spinButtonText != null)
        {
            spinButtonText.color = interactable
                ? Color.white
                : new Color(1f, 1f, 1f, 0.48f);
        }
    }

    private void ConfirmStartBattle()
    {
        if (cancelRequested || spinButton == null || !spinButton.interactable)
            return;

        startBattleConfirmed = true;
        SetSpinButtonInteractable(false);
        SetSpinButtonLabel("STARTING");
    }

    private static RectTransform CreateRect(Transform parent, string name)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private static Text CreateText(
        Transform parent,
        string name,
        int fontSize,
        FontStyle style,
        TextAnchor alignment)
    {
        RectTransform rect = CreateRect(parent, name);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static string BuildStars(int stars)
    {
        stars = Mathf.Clamp(stars, 1, 5);
        return new string('★', stars) + new string('☆', 5 - stars);
    }

    private static string BuildSummary(BattleRuleSet set, int count)
    {
        if (set == null || set.SelectedRules == null || count <= 0)
            return string.Empty;

        int safeCount = Mathf.Min(count, set.SelectedRules.Count);
        System.Text.StringBuilder builder = new();
        for (int i = 0; i < safeCount; i++)
        {
            BattleRuleDefinition rule = set.SelectedRules[i];
            if (rule == null)
                continue;

            if (builder.Length > 0)
                builder.AppendLine();

            builder.Append(GetPolarityLabel(rule.polarity));
            builder.Append("  ");
            builder.Append(rule.displayName.ToUpperInvariant());
            builder.Append(" — ");
            builder.Append(rule.description);
        }

        return builder.ToString();
    }

    private static string GetPolarityLabel(BattleRulePolarity polarity)
    {
        return polarity switch
        {
            BattleRulePolarity.Benefit => "GOOD",
            BattleRulePolarity.Penalty => "BAD",
            BattleRulePolarity.Mixed => "MIXED",
            _ => "RULE"
        };
    }

    private static Color GetPolarityColor(BattleRulePolarity polarity)
    {
        return polarity switch
        {
            BattleRulePolarity.Benefit => new Color(0.18f, 0.95f, 0.62f, 1f),
            BattleRulePolarity.Penalty => new Color(1f, 0.20f, 0.34f, 1f),
            BattleRulePolarity.Mixed => new Color(1f, 0.80f, 0.10f, 1f),
            _ => new Color(0.30f, 0.82f, 1f, 1f)
        };
    }
}

public sealed class BattleRuleSlotPointerFeedback :
    MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private BattleRuleRouletteController owner;
    private RectTransform root;
    private BattleRuleDefinition rule;
    private bool hovered;

    public void Configure(BattleRuleRouletteController controller, RectTransform targetRoot)
    {
        owner = controller;
        root = targetRoot;
    }

    public void Bind(BattleRuleDefinition boundRule)
    {
        rule = boundRule;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (rule == null)
            return;

        hovered = true;
        owner?.HandleRuleSlotPointerEnter(rule);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        owner?.HandleRuleSlotPointerExit(rule);
    }

    private void Update()
    {
        if (root == null)
            return;

        float target = hovered && rule != null && owner != null
            ? owner.GetRuleHoverScale()
            : 1f;

        float blend = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
        root.localScale = Vector3.Lerp(
            root.localScale,
            Vector3.one * target,
            blend);
    }

    private void OnDisable()
    {
        hovered = false;
        if (root != null)
            root.localScale = Vector3.one;
    }
}

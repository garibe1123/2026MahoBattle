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

    [Header("Roulette Field Visibility")]
    [Tooltip("룰렛 중 월드 필드/조명이 보이도록 남겨두는 화면 암막 Alpha입니다. 실제 Dim은 Show Lighting이 소유합니다.")]
    [SerializeField, Range(0f, 0.40f)] private float rouletteBackdropAlpha = 0.14f;

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
    [SerializeField, Range(0.30f, 1f)] private float combatHudScale = 0.56f;
    [Tooltip("TAB을 열었지만 RULES에 아직 커서를 올리지 않았을 때의 중간 Scale입니다.")]
    [SerializeField, Range(0.40f, 1f)] private float combatHudTabScale = 0.78f;
    [SerializeField, Range(0.15f, 1f)] private float combatHudIdleAlpha = 0.72f;
    [SerializeField, Range(0.20f, 1f)] private float combatHudTabAlpha = 0.90f;
    [SerializeField, Range(1f, 1.35f)] private float ruleHoverScale = 1.16f;

    [Header("Combat TAB Rule Panel")]
    [SerializeField, Min(70f)] private float combatRuleCompactMinWidth = 88f;
    [SerializeField, Min(70f)] private float combatRuleCompactHeight = 94f;
    [Tooltip("TAB Open 상태에서 RULES 프레임이 한 단계 커질 때의 높이입니다.")]
    [SerializeField, Min(80f)] private float combatRuleTabHeight = 110f;
    [SerializeField, Min(0f)] private float combatRuleHorizontalPadding = 36f;
    [SerializeField, Min(0f)] private float combatRuleTabExtraWidth = 28f;
    [SerializeField, Min(260f)] private float combatRuleFocusedMinWidth = 420f;
    [SerializeField, Min(160f)] private float combatRuleFocusedHeight = 250f;
    [SerializeField, Min(0f)] private float combatRuleFocusedExtraWidth = 60f;
    [Tooltip("RULES가 Focus된 동안 프레임의 오른쪽만 추가로 늘리는 비율입니다. 0.10 = 10%.")]
    [SerializeField, Range(0f, 0.30f)] private float combatRuleFocusedRightExpansion = 0.06f;
    [Tooltip("평상시 룰 아이콘 Row의 화면 좌측 상단 여백입니다.")]
    [SerializeField] private Vector2 combatRulePersistentTopLeftMargin = new(34f, 34f);
    [Tooltip("TAB에서 PACK GridBoard 윗면과 룰 모듈 사이 세로 여백입니다.")]
    [SerializeField, Min(0f)] private float combatRulePackGap = 22f;
    [Tooltip("PACK 우측 상단 모서리를 기준으로 룰 모듈을 미세 조정하는 Offset입니다.")]
    [SerializeField] private Vector2 combatRulePackTopRightOffset = new(-18f, 8f);
    [SerializeField, Min(0f)] private float combatRuleIdlePackGap = 28f;
    [SerializeField, Min(0f)] private float combatRuleFocusedPackGap = 5f;
    [SerializeField] private Vector2 combatRuleInactiveCornerOffset = new(108f, 64f);
    [Tooltip("RULES Focus/Detail이 커져도 화면 밖으로 잘리지 않도록 유지하는 안전 여백입니다.")]
    [SerializeField, Min(0f)] private float combatRuleScreenMargin = 28f;
    [SerializeField, Range(4f, 30f)] private float combatRulePanelSharpness = 13f;
    [SerializeField, Range(0.30f, 1.15f)] private float combatRuleFocusedIconScale = 0.96f;

    [Header("Rule Confirm Punch")]
    [SerializeField, Range(1f, 1.4f)] private float ruleConfirmScale = 1.18f;
    [SerializeField, Min(0.04f)] private float ruleConfirmGrowDuration = 0.08f;
    [SerializeField, Min(0.04f)] private float ruleConfirmReturnDuration = 0.14f;

    [Header("Rule Detail Hover")]
    [SerializeField, Min(12f)] private float ruleDetailButtonDrop = 172f;
    [SerializeField, Min(0.05f)] private float ruleDetailTweenDuration = 0.18f;
    [SerializeField, Range(0.65f, 0.98f)] private float ruleDetailHiddenScale = 0.88f;
    [SerializeField, Min(0.05f)] private float ruleDetailScaleTweenDuration = 0.16f;

    private GameObject uiRoot;
    private RectTransform rouletteBackdrop;
    private RectTransform combatRulePanel;
    private CanvasGroup combatRulePanelGroup;
    private Image combatRulePanelBack;
    private Outline combatRulePanelOutline;
    private Image combatRulePanelPlate;
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
    private Text combatRuleStateText;
    private Text spinButtonText;
    private Button spinButton;
    private Image spinButtonImage;
    private Image backdropImage;
    private Image ruleDetailBarImage;
    private CanvasGroup ruleDetailGroup;
    private CanvasGroup ruleHudGroup;
    private RectTransform combatRuleDockRoot;
    private BattleKineticLoadoutUI kineticLoadout;
    private BattleBroadcastDashboardController dashboardController;
    private BattleKineticLoadoutUI subscribedCombatLoadout;
    private BattleCombatTabFocus tabFocus = BattleCombatTabFocus.None;
    private Coroutine detailLayoutTweenRoutine;
    private Coroutine detailScaleTweenRoutine;
    private Vector2 controlTabRestPosition;
    private bool cancelRequested;
    private bool startBattleConfirmed;
    private bool combatHudMode;
    private bool finalReviewMode;
    private bool combatTabOpen;
    private bool combatRulePanelFocused;
    private float combatRuleDrawerVisualAlpha;
    private BattleRuleDefinition combatLastInspectedRule;
    private BattleRuleSlotPointerFeedback activeRuleSlotHover;
    private Vector2 resolvedCombatRuleCompactSize;
    private Vector2 resolvedCombatRuleFocusedSize;

    private sealed class RuleSlotView
    {
        public RectTransform root;
        public RectTransform visualRoot;
        public Image frame;
        public Image icon;
        public Text fallbackLabel;
        public BattleRuleDefinition boundRule;
        public BattleRuleSlotPointerFeedback pointerFeedback;
        public Coroutine confirmPunchRoutine;
    }

    public IReadOnlyList<BattleRuleDefinition> Rules => rules;
    public RectTransform MachineTab => machineTab;
    public RectTransform WinningRuleTab => winningRuleTab;
    public RectTransform ResultListTab => resultListTab;
    public RectTransform ControlTab => controlTab;
    public bool CombatRuleFocused =>
        combatHudMode &&
        combatTabOpen &&
        combatRulePanelFocused;

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

    private void OnEnable()
    {
        ResolveCombatTabReferences();
        SubscribeCombatTabEvents();
    }

    private void OnDisable()
    {
        UnsubscribeCombatTabEvents();
        CancelPresentation();
    }

    private void Update()
    {
        if (!combatHudMode || resultListTab == null || combatRulePanel == null)
            return;

        // Combat/TAB state discovery is event-driven.
        // Update only interpolates the already-selected visual state.
        AnimateCombatRulePanel();

        if (combatRulePanelGroup != null)
        {
            combatRulePanelGroup.blocksRaycasts = combatTabOpen;
            combatRulePanelGroup.interactable = combatTabOpen;
        }

        if (combatRulePanelBack != null)
            combatRulePanelBack.raycastTarget = combatTabOpen;

        if (ruleHudGroup != null)
        {
            ruleHudGroup.alpha = 1f;
            ruleHudGroup.blocksRaycasts = combatTabOpen;
            ruleHudGroup.interactable = combatTabOpen;
        }
    }

    public void CancelPresentation()
    {
        cancelRequested = true;
        startBattleConfirmed = false;
        combatHudMode = false;
        finalReviewMode = false;
        combatTabOpen = false;
        ApplyCombatRuleFocusFromCoordinator(false);
        combatRulePanelFocused = false;
        combatRuleDrawerVisualAlpha = 0f;
        combatLastInspectedRule = null;
        ClearActiveRuleSlotHover();
        resolvedCombatRuleCompactSize = Vector2.zero;
        resolvedCombatRuleFocusedSize = Vector2.zero;

        if (detailLayoutTweenRoutine != null)
        {
            StopCoroutine(detailLayoutTweenRoutine);
            detailLayoutTweenRoutine = null;
        }

        if (detailScaleTweenRoutine != null)
        {
            StopCoroutine(detailScaleTweenRoutine);
            detailScaleTweenRoutine = null;
        }

        SetSpinButtonInteractable(false);

        RestoreCombatRulePanelToOverlay();

        if (combatRulePanel != null)
            combatRulePanel.gameObject.SetActive(false);

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
        HideRuleDetailImmediate();

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
        HideRuleDetailImmediate();
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
        rouletteBackdrop = backdrop;

        backdropImage = backdrop.gameObject.AddComponent<Image>();
        backdropImage.color = new Color(0f, 0f, 0f, Mathf.Clamp(rouletteBackdropAlpha, 0f, 0.40f));
        backdropImage.raycastTarget = true;

        combatRulePanel = CreateCombatRulePanel(backdrop);
        combatRulePanel.gameObject.SetActive(false);

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

        ruleDetailBarImage = winningRuleTab.gameObject.AddComponent<Image>();
        ruleDetailBarImage.color = new Color(0.025f, 0.028f, 0.038f, 0.94f);
        ruleDetailBarImage.raycastTarget = false;

        ruleDetailGroup = winningRuleTab.gameObject.AddComponent<CanvasGroup>();
        ruleDetailGroup.alpha = 0f;
        ruleDetailGroup.blocksRaycasts = false;
        ruleDetailGroup.interactable = false;

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
        ruleHudGroup = resultListTab.gameObject.AddComponent<CanvasGroup>();
        ruleHudGroup.alpha = 1f;
        ruleHudGroup.blocksRaycasts = true;
        ruleHudGroup.interactable = true;

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
        spinButtonImage.sprite = spinButtonSprite != null
            ? spinButtonSprite
            : BattleRuleRuntimeUiSprites.RoundedButton;
        spinButtonImage.type = Image.Type.Sliced;
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

            // Root는 Layout + Pointer hitbox만 담당합니다.
            // Hover/Punch에서 절대 Scale하지 않아 EventSystem 판정 영역이 흔들리지 않습니다.
            Image hitbox = root.gameObject.AddComponent<Image>();
            hitbox.color = Color.clear;
            hitbox.raycastTarget = true;

            RectTransform visualRoot = CreateRect(root, "VisualRoot");
            Stretch(visualRoot);
            visualRoot.pivot = new Vector2(0.5f, 0.5f);
            visualRoot.localScale = Vector3.one;

            Image frame = visualRoot.gameObject.AddComponent<Image>();
            frame.sprite = ruleSlotFrameSprite;
            frame.type = ruleSlotFrameSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            frame.color = ruleSlotFrameSprite != null
                ? Color.white
                : new Color(0.08f, 0.085f, 0.11f, 0.98f);
            frame.raycastTarget = false;

            BattleRuleSlotPointerFeedback pointerFeedback =
                root.gameObject.AddComponent<BattleRuleSlotPointerFeedback>();
            pointerFeedback.Configure(this, visualRoot);

            RectTransform iconRect = CreateRect(visualRoot, "Icon");
            iconRect.anchorMin = new Vector2(0.12f, 0.12f);
            iconRect.anchorMax = new Vector2(0.88f, 0.88f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            Image icon = iconRect.gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;

            Text fallbackLabel = CreateText(visualRoot, "FallbackLabel", 11, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetRect(fallbackLabel.rectTransform, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));
            fallbackLabel.text = "?";
            fallbackLabel.color = new Color(1f, 1f, 1f, 0.30f);

            ruleSlotViews.Add(new RuleSlotView
            {
                root = root,
                visualRoot = visualRoot,
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
            view.boundRule = rule;

            if (view.pointerFeedback != null)
                view.pointerFeedback.Bind(rule);

            PlayRuleConfirmPunch(view);
        }
    }

    private void PlayRuleConfirmPunch(RuleSlotView view)
    {
        if (view?.visualRoot == null)
            return;

        if (view.confirmPunchRoutine != null)
            StopCoroutine(view.confirmPunchRoutine);

        view.confirmPunchRoutine = StartCoroutine(RuleConfirmPunchRoutine(view));
    }

    private IEnumerator RuleConfirmPunchRoutine(RuleSlotView view)
    {
        if (view?.visualRoot == null)
            yield break;

        RectTransform root = view.visualRoot;
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
        combatTabOpen = false;
        combatRulePanelFocused = false;
        combatRuleDrawerVisualAlpha = 0f;
        combatLastInspectedRule = null;
        ApplyCombatRuleFocusFromCoordinator(false);
        ApplyRuleDetailVisualMode(false);

        RestoreCombatRulePanelToOverlay();

        if (combatRulePanel != null)
        {
            combatRulePanel.gameObject.SetActive(false);
            combatRulePanel.sizeDelta = ResolveCombatRuleCompactSize();
            combatRulePanel.anchorMin = combatRulePanel.anchorMax = new Vector2(0f, 1f);
            combatRulePanel.pivot = new Vector2(0f, 1f);
            combatRulePanel.anchoredPosition = ResolveCombatRulePersistentPosition();
            combatRulePanel.localRotation = Quaternion.identity;
            combatRulePanel.localScale = Vector3.one;
        }

        if (combatRulePanelGroup != null)
            combatRulePanelGroup.alpha = 0f;

        if (combatRulePanelBack != null)
        {
            Color color = combatRulePanelBack.color;
            color.a = 0f;
            combatRulePanelBack.color = color;
        }
        if (combatRulePanelOutline != null)
        {
            Color color = combatRulePanelOutline.effectColor;
            color.a = 0f;
            combatRulePanelOutline.effectColor = color;
        }
        if (combatRulePanelPlate != null)
        {
            Color color = combatRulePanelPlate.color;
            color.a = 0f;
            combatRulePanelPlate.color = color;
        }

        if (rouletteBackdrop != null)
        {
            if (resultListTab != null && resultListTab.parent != rouletteBackdrop)
                resultListTab.SetParent(rouletteBackdrop, false);

            if (winningRuleTab != null && winningRuleTab.parent != rouletteBackdrop)
                winningRuleTab.SetParent(rouletteBackdrop, false);
        }

        if (detailLayoutTweenRoutine != null)
        {
            StopCoroutine(detailLayoutTweenRoutine);
            detailLayoutTweenRoutine = null;
        }

        if (detailScaleTweenRoutine != null)
        {
            StopCoroutine(detailScaleTweenRoutine);
            detailScaleTweenRoutine = null;
        }

        if (backdropImage != null)
        {
            backdropImage.color = new Color(0f, 0f, 0f, Mathf.Clamp(rouletteBackdropAlpha, 0f, 0.40f));
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
            if (ruleHudGroup != null)
            {
                ruleHudGroup.alpha = 1f;
                ruleHudGroup.blocksRaycasts = true;
                ruleHudGroup.interactable = true;
            }

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
        if (resultListTab == null ||
            winningRuleTab == null ||
            combatRulePanel == null ||
            rouletteBackdrop == null)
        {
            yield break;
        }

        combatHudMode = true;
        combatRulePanelFocused = false;
        combatRuleDrawerVisualAlpha = 0f;
        ResolveCombatTabReferences();

        int count = Mathf.Max(1, ruleSlotViews.Count);
        float activeWidth =
            count * Mathf.Max(48f, ruleSlotSize) +
            Mathf.Max(0, count - 1) * Mathf.Max(0f, ruleSlotSpacing);

        resolvedCombatRuleCompactSize = ResolveCombatRuleCompactSize(activeWidth);
        resolvedCombatRuleFocusedSize = ResolveCombatRuleFocusedSize(activeWidth);

        HorizontalLayoutGroup layout = resultListTab.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
            layout.childAlignment = TextAnchor.MiddleCenter;

        Vector3 startResultWorld = resultListTab.position;
        Vector3 startResultScale = resultListTab.localScale;

        RestoreCombatRulePanelToOverlay();
        combatRulePanel.gameObject.SetActive(true);
        combatRulePanel.sizeDelta = resolvedCombatRuleCompactSize;
        combatRulePanel.anchorMin = combatRulePanel.anchorMax = new Vector2(0f, 1f);
        combatRulePanel.pivot = new Vector2(0f, 1f);
        combatRulePanel.anchoredPosition = ResolveCombatRulePersistentPosition();
        combatRulePanel.localRotation = Quaternion.identity;
        combatRulePanel.localScale = Vector3.one;

        if (combatRulePanelGroup != null)
            combatRulePanelGroup.alpha = 0f;

        resultListTab.SetParent(combatRulePanel, true);
        resultListTab.anchorMin = resultListTab.anchorMax = new Vector2(0f, 1f);
        resultListTab.pivot = new Vector2(0f, 1f);
        resultListTab.sizeDelta = new Vector2(activeWidth, Mathf.Max(48f, ruleSlotSize));
        resultListTab.anchoredPosition = new Vector2(8f, -8f);
        resultListTab.localRotation = Quaternion.identity;

        float compactIconScale = Mathf.Clamp(combatHudScale, 0.30f, 1f);
        Vector3 targetResultScale = Vector3.one * compactIconScale;
        Vector3 targetResultWorld = resultListTab.position;

        // 룰렛 결과 Row가 전투 HUD의 좌측 상단 아이콘 Row로 이동하는 장면은 유지합니다.
        resultListTab.position = startResultWorld;
        resultListTab.localScale = startResultScale;

        winningRuleTab.SetParent(combatRulePanel, false);
        winningRuleTab.anchorMin = winningRuleTab.anchorMax = new Vector2(0.5f, 0f);
        winningRuleTab.pivot = new Vector2(0.5f, 0f);
        winningRuleTab.sizeDelta = new Vector2(
            Mathf.Max(220f, resolvedCombatRuleCompactSize.x - 32f),
            82f);
        winningRuleTab.anchoredPosition = new Vector2(0f, 10f);
        winningRuleTab.localScale = Vector3.one;
        HideRuleDetailImmediate();

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

            resultListTab.position = Vector3.Lerp(
                startResultWorld,
                targetResultWorld,
                eased);
            resultListTab.localScale = Vector3.Lerp(
                startResultScale,
                targetResultScale,
                eased);

            if (combatRulePanelGroup != null)
            {
                combatRulePanelGroup.alpha = Mathf.Lerp(
                    0f,
                    Mathf.Clamp01(combatHudIdleAlpha),
                    eased);
            }

            if (backdropImage != null)
            {
                Color faded = backdropStart;
                faded.a = Mathf.Lerp(backdropStart.a, 0f, eased);
                backdropImage.color = faded;
            }

            yield return null;
        }

        resultListTab.position = targetResultWorld;
        resultListTab.localScale = targetResultScale;

        if (machineTab != null)
            machineTab.gameObject.SetActive(false);
        if (controlTab != null)
            controlTab.gameObject.SetActive(false);

        if (backdropImage != null)
        {
            backdropImage.color = Color.clear;
            backdropImage.raycastTarget = false;
        }

        ResolveCombatTabReferences();
        combatTabOpen = kineticLoadout != null && kineticLoadout.IsSwitchBoardOpen;
        combatRulePanelFocused = false;
        RefreshCombatRuleStateText();
        combatRuleDrawerVisualAlpha = 0f;
        ApplyRuleDetailVisualMode(true);

        if (combatRulePanelGroup != null)
        {
            combatRulePanelGroup.alpha = combatTabOpen
                ? Mathf.Clamp01(combatHudTabAlpha)
                : Mathf.Clamp01(combatHudIdleAlpha);
            combatRulePanelGroup.blocksRaycasts = combatTabOpen;
            combatRulePanelGroup.interactable = combatTabOpen;
        }

        if (ruleHudGroup != null)
        {
            ruleHudGroup.alpha = 1f;
            ruleHudGroup.blocksRaycasts = combatTabOpen;
            ruleHudGroup.interactable = combatTabOpen;
        }

        HideRuleDetailImmediate();
    }

    private void EnterFinalReviewMode()
    {
        finalReviewMode = true;
        ApplyRuleDetailVisualMode(false);
        ResetControlTabPositionImmediate();
        HideRuleDetailImmediate();
    }

    private void HideRuleDetail()
    {
        if (winningRuleTab == null)
            return;

        if (detailScaleTweenRoutine != null)
            StopCoroutine(detailScaleTweenRoutine);

        if (!winningRuleTab.gameObject.activeSelf)
        {
            detailScaleTweenRoutine = null;
            return;
        }

        detailScaleTweenRoutine = StartCoroutine(
            TweenRuleDetailScale(show: false));
    }

    private void HideRuleDetailImmediate()
    {
        if (detailScaleTweenRoutine != null)
        {
            StopCoroutine(detailScaleTweenRoutine);
            detailScaleTweenRoutine = null;
        }

        if (winningRuleTab == null)
            return;

        float baseScale = GetRuleDetailBaseScale();
        winningRuleTab.localScale =
            Vector3.one * baseScale * Mathf.Clamp(ruleDetailHiddenScale, 0.65f, 0.98f);

        Vector3 hiddenPosition = winningRuleTab.localPosition;
        hiddenPosition.z = 18f;
        winningRuleTab.localPosition = hiddenPosition;
        winningRuleTab.localRotation = Quaternion.Euler(2.2f, -4.5f, 0.6f);

        if (ruleDetailGroup != null)
            ruleDetailGroup.alpha = 0f;

        winningRuleTab.gameObject.SetActive(false);
    }

    private void ShowRuleDetail(BattleRuleDefinition rule)
    {
        if (rule == null || winningRuleTab == null)
            return;

        if (detailScaleTweenRoutine != null)
        {
            StopCoroutine(detailScaleTweenRoutine);
            detailScaleTweenRoutine = null;
        }

        ApplyRuleDetailVisualMode(combatHudMode);
        ShowWinningRule(rule);

        float baseScale = GetRuleDetailBaseScale();
        float hiddenScale = baseScale * Mathf.Clamp(ruleDetailHiddenScale, 0.65f, 0.98f);

        winningRuleTab.gameObject.SetActive(true);
        winningRuleTab.localScale = Vector3.one * hiddenScale;

        Vector3 hiddenPosition = winningRuleTab.localPosition;
        hiddenPosition.z = 18f;
        winningRuleTab.localPosition = hiddenPosition;
        winningRuleTab.localRotation = Quaternion.Euler(2.2f, -4.5f, 0.6f);

        if (ruleDetailGroup != null)
            ruleDetailGroup.alpha = 0f;

        detailScaleTweenRoutine = StartCoroutine(
            TweenRuleDetailScale(show: true));
    }

    private IEnumerator TweenRuleDetailScale(bool show)
    {
        if (winningRuleTab == null)
            yield break;

        float baseScale = GetRuleDetailBaseScale();
        float hiddenScale = baseScale * Mathf.Clamp(ruleDetailHiddenScale, 0.65f, 0.98f);
        Vector3 startScale = winningRuleTab.localScale;
        Vector3 targetScale = Vector3.one * (show ? baseScale : hiddenScale);
        Vector3 startPosition = winningRuleTab.localPosition;
        Vector3 targetPosition = startPosition;
        targetPosition.z = show ? -14f : 18f;
        Quaternion startRotation = winningRuleTab.localRotation;
        Quaternion targetRotation = Quaternion.Euler(
            show ? 0f : 2.2f,
            show ? 0f : -4.5f,
            show ? 0f : 0.6f);
        float startAlpha = ruleDetailGroup != null ? ruleDetailGroup.alpha : (show ? 0f : 1f);
        float targetAlpha = show ? 1f : 0f;

        float duration = Mathf.Max(0.05f, ruleDetailScaleTweenDuration);
        float elapsed = 0f;

        while (elapsed < duration && winningRuleTab != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = show
                ? 1f - Mathf.Pow(1f - t, 3f)
                : t * t * (3f - 2f * t);

            winningRuleTab.localScale = Vector3.Lerp(startScale, targetScale, eased);
            winningRuleTab.localPosition = Vector3.Lerp(startPosition, targetPosition, eased);
            winningRuleTab.localRotation = Quaternion.Slerp(startRotation, targetRotation, eased);

            if (ruleDetailGroup != null)
                ruleDetailGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, eased);

            yield return null;
        }

        if (winningRuleTab != null)
        {
            winningRuleTab.localScale = targetScale;
            winningRuleTab.localPosition = targetPosition;
            winningRuleTab.localRotation = targetRotation;

            if (ruleDetailGroup != null)
                ruleDetailGroup.alpha = targetAlpha;

            if (!show)
                winningRuleTab.gameObject.SetActive(false);
        }

        detailScaleTweenRoutine = null;
    }

    private float GetRuleDetailBaseScale()
    {
        return 1f;
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

    internal void HandleRuleSlotPointerEnter(
        BattleRuleDefinition rule,
        BattleRuleSlotPointerFeedback source)
    {
        if (rule == null)
            return;

        if (combatHudMode && !combatTabOpen)
            return;

        if (combatHudMode)
        {
            // Panel compact -> focused Tween 중 슬롯 Rect가 이동해도,
            // 한 번 들어온 아이콘은 다른 아이콘/Panel Exit 전까지 Hover를 유지합니다.
            if (activeRuleSlotHover != null && activeRuleSlotHover != source)
                activeRuleSlotHover.SetHoveredFromOwner(false);

            activeRuleSlotHover = source;
            activeRuleSlotHover?.SetHoveredFromOwner(true);
            combatLastInspectedRule = rule;
        }

        ShowRuleDetail(rule);

        if (finalReviewMode)
            TweenControlTabForDetail(true);
    }

    internal void HandleRuleSlotPointerExit(
        BattleRuleDefinition rule,
        BattleRuleSlotPointerFeedback source)
    {
        // Combat TAB에서는 패널 자체가 Focus인 동안 slot hover를 sticky하게 유지합니다.
        // compact -> focused 레이아웃 Tween 때문에 EventSystem이 일시적으로 Exit를 보내도
        // 아이콘 확대가 풀리지 않게 보장합니다.
        if (combatHudMode && combatRulePanelFocused)
            return;

        if (activeRuleSlotHover == source)
            activeRuleSlotHover = null;

        source?.SetHoveredFromOwner(false);
        HideRuleDetail();

        if (finalReviewMode)
            TweenControlTabForDetail(false);
    }

    private void ClearActiveRuleSlotHover()
    {
        if (activeRuleSlotHover != null)
            activeRuleSlotHover.SetHoveredFromOwner(false);

        activeRuleSlotHover = null;
    }

    private RectTransform CreateCombatRulePanel(RectTransform parent)
    {
        RectTransform panel = CreateRect(parent, "CombatRulePanel");
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0f, 1f);
        panel.sizeDelta = ResolveCombatRuleCompactSize();
        panel.anchoredPosition = new Vector2(-926f, 1046f);
        panel.localRotation = Quaternion.identity;

        combatRulePanelBack = panel.gameObject.AddComponent<Image>();
        combatRulePanelBack.color = new Color(0.025f, 0.028f, 0.045f, 0f);
        combatRulePanelBack.raycastTarget = true;

        BattleCombatRuleFocusPointerRelay focusRelay =
            panel.gameObject.AddComponent<BattleCombatRuleFocusPointerRelay>();
        focusRelay.Configure(this);

        combatRulePanelOutline = panel.gameObject.AddComponent<Outline>();
        combatRulePanelOutline.effectColor = new Color(0.94f, 0.95f, 0.97f, 0f);
        combatRulePanelOutline.effectDistance = new Vector2(4f, -4f);

        combatRulePanelGroup = panel.gameObject.AddComponent<CanvasGroup>();
        combatRulePanelGroup.alpha = 0f;
        combatRulePanelGroup.blocksRaycasts = false;
        combatRulePanelGroup.interactable = false;

        combatRulePanelPlate = null;

        combatRuleStateText = CreateText(
            panel,
            "RuleState",
            10,
            FontStyle.Bold,
            TextAnchor.MiddleLeft);
        combatRuleStateText.color = new Color(0.62f, 0.67f, 0.76f, 1f);
        SetRect(
            combatRuleStateText.rectTransform,
            new Vector2(0.04f, 0.87f),
            new Vector2(0.96f, 0.98f));
        combatRuleStateText.text = "RULES // HOLD TAB TO INSPECT";

        return panel;
    }

    private void AnimateCombatRulePanel()
    {
        if (combatRulePanel == null || !combatRulePanel.gameObject.activeSelf)
            return;

        bool focused =
            combatTabOpen &&
            (combatRulePanelFocused || tabFocus == BattleCombatTabFocus.Rules);
        bool suppressed =
            combatTabOpen &&
            tabFocus != BattleCombatTabFocus.None &&
            tabFocus != BattleCombatTabFocus.Rules;

        EnsureCombatRulePanelParentForMode();

        Vector2 compactSize = resolvedCombatRuleCompactSize.sqrMagnitude > 0.01f
            ? resolvedCombatRuleCompactSize
            : ResolveCombatRuleCompactSize();
        Vector2 tabSize = ResolveCombatRuleTabSize();
        Vector2 focusedSize = resolvedCombatRuleFocusedSize.sqrMagnitude > 0.01f
            ? resolvedCombatRuleFocusedSize
            : ResolveCombatRuleFocusedSize();

        Vector2 targetSize = focused
            ? focusedSize
            : combatTabOpen
                ? tabSize
                : compactSize;

        // Panel pivot이 오른쪽(1,0)이므로 폭만 키우면 왼쪽으로 늘어납니다.
        // 사용자가 원하는 것은 "기존 왼쪽 경계 유지 + 오른쪽만 10% 확장"이므로
        // Focus 시 증가한 폭만큼 pivot 위치도 오른쪽으로 이동시킵니다.
        float focusedRightExtra = focused
            ? ResolveCombatRuleFocusedRightExtra()
            : 0f;

        float targetAlpha = combatTabOpen
            ? focused
                ? 1f
                : suppressed
                    ? 0.34f
                    : 0.72f
            : Mathf.Clamp01(combatHudIdleAlpha);

        float targetIconScale = focused
            ? Mathf.Clamp(combatRuleFocusedIconScale, 0.30f, 1f)
            : combatTabOpen
                ? suppressed
                    ? Mathf.Clamp(combatHudTabScale * 0.78f, 0.30f, 1f)
                    : Mathf.Clamp(combatHudTabScale * 0.92f, 0.40f, 1f)
                : Mathf.Clamp(combatHudScale, 0.30f, 1f);

        float t = 1f - Mathf.Exp(
            -Mathf.Max(4f, combatRulePanelSharpness) * Time.unscaledDeltaTime);

        combatRulePanel.sizeDelta = Vector2.Lerp(
            combatRulePanel.sizeDelta,
            targetSize,
            t);

        Vector2 targetPosition;
        float targetRotation;

        if (combatTabOpen && kineticLoadout != null && kineticLoadout.GridBoard != null)
        {
            RectTransform board = kineticLoadout.GridBoard;
            RectTransform packDock = ResolveCombatRuleDockRoot();

            if (packDock != null)
            {
                // 움직이고 축소된 현재 GridBoard의 실제 우측 상단 모서리를
                // 같은 PackDock 좌표계로 변환합니다.
                // 따라서 PACK이 이동/축소되어도 RULES는 항상 PACK 우측 상단에 붙어 따라갑니다.
                Vector3 boardTopRightWorld = board.TransformPoint(
                    new Vector3(board.rect.xMax, board.rect.yMax, 0f));
                Vector3 boardTopRightLocal = packDock.InverseTransformPoint(boardTopRightWorld);

                float resolvedGap = focused
                    ? Mathf.Max(0f, combatRuleFocusedPackGap)
                    : Mathf.Max(
                        Mathf.Max(0f, combatRulePackGap),
                        Mathf.Max(0f, combatRuleIdlePackGap));

                targetPosition = new Vector2(
                    boardTopRightLocal.x +
                    combatRulePackTopRightOffset.x +
                    focusedRightExtra +
                    (focused ? 10f : 0f),
                    boardTopRightLocal.y +
                    resolvedGap +
                    combatRulePackTopRightOffset.y);

                if (suppressed)
                    targetPosition += combatRuleInactiveCornerOffset;
            }
            else
            {
                targetPosition =
                    ResolveCombatRulePersistentPosition() +
                    new Vector2(focusedRightExtra, 0f);
            }

            targetRotation =
                Mathf.Repeat(board.eulerAngles.z + 180f, 360f) - 180f;
        }
        else
        {
            targetPosition = ResolveCombatRulePersistentPosition();
            targetRotation = 0f;
        }

        RectTransform panelParent = combatRulePanel.parent as RectTransform;
        targetPosition = ClampCombatRulePanelPosition(
            targetPosition,
            targetSize,
            panelParent);

        combatRulePanel.anchoredPosition = Vector2.Lerp(
            combatRulePanel.anchoredPosition,
            targetPosition,
            t);

        float currentPanelRotation =
            Mathf.Repeat(combatRulePanel.localEulerAngles.z + 180f, 360f) - 180f;
        float nextPanelRotation =
            Mathf.LerpAngle(currentPanelRotation, targetRotation, t);

        Quaternion spatialRotation = Quaternion.Euler(
            combatTabOpen ? (focused ? 0.2f : 1.2f) : 2.8f,
            combatTabOpen ? (focused ? -0.8f : -3.0f) : -6.0f,
            nextPanelRotation);
        combatRulePanel.localRotation = Quaternion.Slerp(
            combatRulePanel.localRotation,
            spatialRotation,
            t);

        Vector3 panelLocal = combatRulePanel.localPosition;
        panelLocal.z = Mathf.Lerp(
            panelLocal.z,
            combatTabOpen ? (focused ? -18f : -6f) : 24f,
            t);
        combatRulePanel.localPosition = panelLocal;

        combatRulePanel.localScale = Vector3.Lerp(
            combatRulePanel.localScale,
            Vector3.one * (
                combatTabOpen
                    ? focused ? 1.055f : suppressed ? 0.80f : 0.92f
                    : 0.92f),
            t);

        if (combatRulePanelGroup != null)
            combatRulePanelGroup.alpha = Mathf.Lerp(
                combatRulePanelGroup.alpha,
                targetAlpha,
                t);

        float drawerTarget = combatTabOpen
            ? (focused ? 1f : 0.88f)
            : 0f;
        combatRuleDrawerVisualAlpha = Mathf.Lerp(
            combatRuleDrawerVisualAlpha,
            drawerTarget,
            t);

        if (Mathf.Abs(combatRuleDrawerVisualAlpha - drawerTarget) <= 0.002f)
            combatRuleDrawerVisualAlpha = drawerTarget;

        if (combatRulePanelBack != null)
        {
            Color color = combatRulePanelBack.color;
            color.r = 0.025f;
            color.g = 0.028f;
            color.b = 0.045f;
            color.a = 0.94f * combatRuleDrawerVisualAlpha;
            combatRulePanelBack.color = color;
        }

        if (combatRulePanelOutline != null)
        {
            combatRulePanelOutline.effectColor = new Color(
                0.94f,
                0.95f,
                0.97f,
                0.58f * combatRuleDrawerVisualAlpha);
        }

        if (combatRulePanelPlate != null)
        {
            Color color = combatRulePanelPlate.color;
            color.r = 0.94f;
            color.g = 0.95f;
            color.b = 0.97f;
            color.a = 0.10f * combatRuleDrawerVisualAlpha;
            combatRulePanelPlate.color = color;
        }

        HorizontalLayoutGroup layout = resultListTab != null
            ? resultListTab.GetComponent<HorizontalLayoutGroup>()
            : null;

        if (resultListTab != null)
        {
            resultListTab.localScale = Vector3.Lerp(
                resultListTab.localScale,
                Vector3.one * targetIconScale,
                t);

            Vector2 rowTarget;
            if (combatTabOpen)
            {
                if (layout != null)
                    layout.childAlignment = TextAnchor.MiddleCenter;

                resultListTab.anchorMin = resultListTab.anchorMax = new Vector2(0.5f, 1f);
                resultListTab.pivot = new Vector2(0.5f, 1f);
                rowTarget = focused
                    ? new Vector2(0f, -42f)
                    : new Vector2(0f, -36f);
            }
            else
            {
                if (layout != null)
                    layout.childAlignment = TextAnchor.MiddleLeft;

                resultListTab.anchorMin = resultListTab.anchorMax = new Vector2(0f, 1f);
                resultListTab.pivot = new Vector2(0f, 1f);
                rowTarget = new Vector2(8f, -24f);
            }

            resultListTab.anchoredPosition = Vector2.Lerp(
                resultListTab.anchoredPosition,
                rowTarget,
                t);
        }

        if (winningRuleTab != null)
        {
            Vector2 detailSize = new(
                Mathf.Max(280f, combatRulePanel.sizeDelta.x - 56f),
                focused ? 148f : 82f);

            winningRuleTab.sizeDelta = Vector2.Lerp(
                winningRuleTab.sizeDelta,
                detailSize,
                t);
            winningRuleTab.anchoredPosition = Vector2.Lerp(
                winningRuleTab.anchoredPosition,
                focused
                    ? new Vector2(0f, 18f)
                    : new Vector2(0f, 10f),
                t);
        }
    }

    internal void HandleCombatRulePanelPointerEnter()
    {
        if (!combatHudMode || !combatTabOpen)
            return;

        ResolveCombatTabReferences();

        // RULES와 PACK이 동시에 Active Focus를 갖지 않습니다.
        dashboardController?.SetRulesFocus(true);
        kineticLoadout?.ClearPackHoverImmediate();
        kineticLoadout?.ClearExternalSelection();
        ApplyCombatRuleFocusFromCoordinator(true);
    }

    internal void HandleCombatRulePanelPointerExit()
    {
        dashboardController?.SetRulesFocus(false);
        ApplyCombatRuleFocusFromCoordinator(false);
    }

    public void SetTabFocusState(BattleCombatTabFocus next)
    {
        tabFocus = combatTabOpen ? next : BattleCombatTabFocus.None;

        if (tabFocus != BattleCombatTabFocus.Rules &&
            combatRulePanelFocused)
        {
            ApplyCombatRuleFocusFromCoordinator(false);
        }
    }

    public void ApplyCombatRuleFocusFromCoordinator(bool focused)
    {
        bool resolved = focused && combatHudMode && combatTabOpen;
        if (combatRulePanelFocused == resolved)
            return;

        combatRulePanelFocused = resolved;
        RefreshCombatRuleStateText();

        if (combatRulePanelFocused)
        {
            ShowCombatRuleDetailDefault();
        }
        else
        {
            ClearActiveRuleSlotHover();
            HideRuleDetail();
        }
    }



    private void RefreshCombatRuleStateText()
    {
        if (combatRuleStateText == null)
            return;

        if (!combatHudMode)
        {
            combatRuleStateText.text = string.Empty;
            return;
        }

        if (!combatTabOpen)
        {
            combatRuleStateText.text = "RULES // HOLD TAB TO INSPECT";
            combatRuleStateText.color = new Color(0.48f, 0.52f, 0.60f, 1f);
            return;
        }

        if (combatRulePanelFocused)
        {
            combatRuleStateText.text = "RULES // DETAIL ACTIVE";
            combatRuleStateText.color = new Color(0.16f, 0.86f, 0.92f, 1f);
            return;
        }

        combatRuleStateText.text = "RULES // HOVER FOR DETAIL";
        combatRuleStateText.color = new Color(0.72f, 0.76f, 0.82f, 1f);
    }

    private Vector2 ResolveCombatRulePersistentPosition()
    {
        return new Vector2(
            Mathf.Abs(combatRulePersistentTopLeftMargin.x),
            -Mathf.Abs(combatRulePersistentTopLeftMargin.y));
    }

    private Vector2 ClampCombatRulePanelPosition(
        Vector2 anchoredPosition,
        Vector2 targetSize,
        RectTransform parent)
    {
        if (combatRulePanel == null || parent == null)
            return anchoredPosition;

        Rect parentRect = parent.rect;
        Vector2 anchor = combatRulePanel.anchorMin;
        Vector2 pivot = combatRulePanel.pivot;
        Vector2 anchorPoint = new(
            Mathf.Lerp(parentRect.xMin, parentRect.xMax, anchor.x),
            Mathf.Lerp(parentRect.yMin, parentRect.yMax, anchor.y));

        Vector2 pivotPosition = anchorPoint + anchoredPosition;
        float width = Mathf.Max(1f, targetSize.x);
        float height = Mathf.Max(1f, targetSize.y);
        float margin = Mathf.Max(0f, combatRuleScreenMargin);

        float minPivotX = parentRect.xMin + margin + width * pivot.x;
        float maxPivotX = parentRect.xMax - margin - width * (1f - pivot.x);
        float minPivotY = parentRect.yMin + margin + height * pivot.y;
        float maxPivotY = parentRect.yMax - margin - height * (1f - pivot.y);

        if (minPivotX <= maxPivotX)
            pivotPosition.x = Mathf.Clamp(pivotPosition.x, minPivotX, maxPivotX);
        if (minPivotY <= maxPivotY)
            pivotPosition.y = Mathf.Clamp(pivotPosition.y, minPivotY, maxPivotY);

        return pivotPosition - anchorPoint;
    }

    private void EnsureCombatRulePanelParentForMode()
    {
        if (combatRulePanel == null)
            return;

        if (combatTabOpen)
        {
            RectTransform board = kineticLoadout != null
                ? kineticLoadout.GridBoard
                : null;
            RectTransform packDock = ResolveCombatRuleDockRoot();

            if (board == null || packDock == null)
                return;

            if (combatRulePanel.parent != packDock)
            {
                ReparentCombatRulePanelPreservingWorld(
                    packDock,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(1f, 0f));
                combatRulePanel.SetAsLastSibling();
            }
            else
            {
                combatRulePanel.anchorMin =
                    combatRulePanel.anchorMax =
                        new Vector2(0.5f, 0.5f);
                combatRulePanel.pivot = new Vector2(1f, 0f);
            }

            // The persistent RULES HUD physically flies from its combat position
            // into the PACK dock. No replacement panel is spawned.
            return;
        }

        RestoreCombatRulePanelToOverlay();
    }

    private RectTransform ResolveCombatRuleDockRoot()
    {
        RectTransform full = kineticLoadout != null
            ? kineticLoadout.FullRoot
            : null;

        if (full == null)
            return null;

        if (combatRuleDockRoot != null && combatRuleDockRoot.parent == full)
            return combatRuleDockRoot;

        combatRuleDockRoot = full.Find("CombatRuleDock") as RectTransform;
        if (combatRuleDockRoot == null)
        {
            combatRuleDockRoot = CreateRect(full, "CombatRuleDock");
            Stretch(combatRuleDockRoot);
            combatRuleDockRoot.localPosition = Vector3.zero;
            combatRuleDockRoot.localRotation = Quaternion.identity;
            combatRuleDockRoot.localScale = Vector3.one;
        }

        return combatRuleDockRoot;
    }

    private void RestoreCombatRulePanelToOverlay()
    {
        if (combatRulePanel == null || rouletteBackdrop == null)
            return;

        if (combatRulePanel.parent != rouletteBackdrop)
        {
            ReparentCombatRulePanelPreservingWorld(
                rouletteBackdrop,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f));
        }
        else
        {
            combatRulePanel.anchorMin = combatRulePanel.anchorMax = new Vector2(0f, 1f);
            combatRulePanel.pivot = new Vector2(0f, 1f);
        }
    }

    private void ReparentCombatRulePanelPreservingWorld(
        RectTransform nextParent,
        Vector2 anchor,
        Vector2 pivot)
    {
        if (combatRulePanel == null || nextParent == null)
            return;

        Vector3 worldPosition = combatRulePanel.position;
        Quaternion worldRotation = combatRulePanel.rotation;
        Vector3 worldScale = combatRulePanel.lossyScale;

        combatRulePanel.SetParent(nextParent, true);
        combatRulePanel.anchorMin = combatRulePanel.anchorMax = anchor;
        combatRulePanel.pivot = pivot;
        combatRulePanel.position = worldPosition;
        combatRulePanel.rotation = worldRotation;

        Vector3 parentScale = nextParent.lossyScale;
        combatRulePanel.localScale = new Vector3(
            Mathf.Abs(parentScale.x) > 0.0001f ? worldScale.x / parentScale.x : worldScale.x,
            Mathf.Abs(parentScale.y) > 0.0001f ? worldScale.y / parentScale.y : worldScale.y,
            Mathf.Abs(parentScale.z) > 0.0001f ? worldScale.z / parentScale.z : worldScale.z);
    }

    private void ShowCombatRuleDetailDefault()
    {
        if (!combatHudMode || !combatTabOpen || !combatRulePanelFocused)
            return;

        if (combatLastInspectedRule != null)
        {
            ShowRuleDetail(combatLastInspectedRule);
            return;
        }

        for (int i = 0; i < ruleSlotViews.Count; i++)
        {
            BattleRuleDefinition rule = ruleSlotViews[i]?.boundRule;
            if (rule == null)
                continue;

            combatLastInspectedRule = rule;
            ShowRuleDetail(rule);
            return;
        }
    }

    private float ResolveActiveRuleWidth()
    {
        int count = Mathf.Clamp(ruleSlotViews.Count, 1, 5);
        return
            count * Mathf.Max(48f, ruleSlotSize) +
            Mathf.Max(0, count - 1) * Mathf.Max(0f, ruleSlotSpacing);
    }

    private Vector2 ResolveCombatRuleCompactSize(float activeWidth = -1f)
    {
        if (activeWidth < 0f)
            activeWidth = ResolveActiveRuleWidth();

        float iconScale = Mathf.Clamp(combatHudScale, 0.30f, 1f);
        float width =
            activeWidth * iconScale +
            Mathf.Max(0f, combatRuleHorizontalPadding);

        return new Vector2(
            Mathf.Max(combatRuleCompactMinWidth, width),
            Mathf.Max(70f, combatRuleCompactHeight));
    }

    private Vector2 ResolveCombatRuleTabSize(float activeWidth = -1f)
    {
        if (activeWidth < 0f)
            activeWidth = ResolveActiveRuleWidth();

        float iconScale = Mathf.Clamp(combatHudTabScale, 0.40f, 1f);
        float width =
            activeWidth * iconScale +
            Mathf.Max(0f, combatRuleHorizontalPadding) +
            Mathf.Max(0f, combatRuleTabExtraWidth);

        return new Vector2(
            Mathf.Max(combatRuleCompactMinWidth, width),
            Mathf.Max(combatRuleCompactHeight, combatRuleTabHeight));
    }

    private Vector2 ResolveCombatRuleFocusedBaseSize(float activeWidth = -1f)
    {
        if (activeWidth < 0f)
            activeWidth = ResolveActiveRuleWidth();

        float iconScale = Mathf.Clamp(combatRuleFocusedIconScale, 0.30f, 1f);
        float iconWidth =
            activeWidth * iconScale +
            Mathf.Max(0f, combatRuleHorizontalPadding) +
            Mathf.Max(0f, combatRuleFocusedExtraWidth);

        return new Vector2(
            Mathf.Max(combatRuleFocusedMinWidth, iconWidth),
            Mathf.Max(160f, combatRuleFocusedHeight));
    }

    private Vector2 ResolveCombatRuleFocusedSize(float activeWidth = -1f)
    {
        Vector2 baseSize = ResolveCombatRuleFocusedBaseSize(activeWidth);
        float rightExtra =
            baseSize.x * Mathf.Clamp(combatRuleFocusedRightExpansion, 0f, 0.30f);

        return new Vector2(
            baseSize.x + rightExtra,
            baseSize.y);
    }

    private float ResolveCombatRuleFocusedRightExtra(float activeWidth = -1f)
    {
        Vector2 baseSize = ResolveCombatRuleFocusedBaseSize(activeWidth);
        return baseSize.x * Mathf.Clamp(combatRuleFocusedRightExpansion, 0f, 0.30f);
    }

    private void ResolveCombatTabReferences()
    {
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
        if (dashboardController == null)
            dashboardController = FindFirstObjectByType<BattleBroadcastDashboardController>(FindObjectsInactive.Include);

        SubscribeCombatTabEvents();
    }

    private void SubscribeCombatTabEvents()
    {
        if (subscribedCombatLoadout == kineticLoadout)
            return;

        if (subscribedCombatLoadout != null)
            subscribedCombatLoadout.SwitchBoardVisibilityChanged -= HandleCombatPackVisibilityChanged;

        subscribedCombatLoadout = kineticLoadout;
        if (subscribedCombatLoadout != null)
            subscribedCombatLoadout.SwitchBoardVisibilityChanged += HandleCombatPackVisibilityChanged;
    }

    private void UnsubscribeCombatTabEvents()
    {
        if (subscribedCombatLoadout != null)
            subscribedCombatLoadout.SwitchBoardVisibilityChanged -= HandleCombatPackVisibilityChanged;

        subscribedCombatLoadout = null;
    }

    private void HandleCombatPackVisibilityChanged(bool visible)
    {
        if (!combatHudMode)
            return;

        combatTabOpen = visible;
        if (!combatTabOpen)
            tabFocus = BattleCombatTabFocus.None;
        RefreshCombatRuleStateText();

        if (!combatTabOpen)
        {
            ApplyCombatRuleFocusFromCoordinator(false);
            HideRuleDetailImmediate();
        }
    }

    private void ApplyRuleDetailVisualMode(bool combatTabStyle)
    {
        if (winningRuleTypeText != null)
            winningRuleTypeText.fontSize = combatTabStyle ? 13 : 13;
        if (winningRuleNameText != null)
            winningRuleNameText.fontSize = combatTabStyle ? 22 : 20;
        if (winningRuleDescriptionText != null)
            winningRuleDescriptionText.fontSize = combatTabStyle ? 14 : 13;

        if (ruleDetailBarImage != null)
        {
            ruleDetailBarImage.color = combatTabStyle
                ? new Color(0.018f, 0.022f, 0.030f, 0.94f)
                : new Color(0.025f, 0.028f, 0.038f, 0.94f);
        }

        if (winningRuleTypeText != null)
            winningRuleTypeText.alignment = TextAnchor.MiddleCenter;

        if (winningRuleNameText != null)
            winningRuleNameText.alignment = TextAnchor.MiddleCenter;

        if (winningRuleDescriptionText != null)
            winningRuleDescriptionText.alignment = TextAnchor.MiddleCenter;

        if (combatTabStyle)
        {
            if (winningRuleTypeText != null)
                SetRect(winningRuleTypeText.rectTransform, new Vector2(0.06f, 0.74f), new Vector2(0.94f, 0.94f));
            if (winningRuleNameText != null)
                SetRect(winningRuleNameText.rectTransform, new Vector2(0.06f, 0.43f), new Vector2(0.94f, 0.76f));
            if (winningRuleDescriptionText != null)
                SetRect(winningRuleDescriptionText.rectTransform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.44f));
        }
        else
        {
            if (winningRuleTypeText != null)
                SetRect(winningRuleTypeText.rectTransform, new Vector2(0.05f, 0.68f), new Vector2(0.95f, 0.94f));
            if (winningRuleNameText != null)
                SetRect(winningRuleNameText.rectTransform, new Vector2(0.05f, 0.34f), new Vector2(0.95f, 0.70f));
            if (winningRuleDescriptionText != null)
                SetRect(winningRuleDescriptionText.rectTransform, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.38f));
        }
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
        owner?.HandleRuleSlotPointerEnter(rule, this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.HandleRuleSlotPointerExit(rule, this);
    }

    public void SetHoveredFromOwner(bool value)
    {
        hovered = value;
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

internal static class BattleRuleRuntimeUiSprites
{
    private static Sprite roundedButton;

    public static Sprite RoundedButton =>
        roundedButton != null
            ? roundedButton
            : roundedButton = CreateRoundedButton();

    private static Sprite CreateRoundedButton()
    {
        const int size = 32;
        const float radius = 8f;
        const int supersample = 4;

        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            name = "RuntimeBattleRuleRoundedButton",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int insideCount = 0;

                for (int sy = 0; sy < supersample; sy++)
                {
                    for (int sx = 0; sx < supersample; sx++)
                    {
                        float px = x + (sx + 0.5f) / supersample;
                        float py = y + (sy + 0.5f) / supersample;

                        float nearestX = Mathf.Clamp(px, radius, size - radius);
                        float nearestY = Mathf.Clamp(py, radius, size - radius);
                        float dx = px - nearestX;
                        float dy = py - nearestY;

                        if (dx * dx + dy * dy <= radius * radius)
                            insideCount++;
                    }
                }

                float alpha = insideCount / (float)(supersample * supersample);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));

        sprite.name = "RuntimeBattleRuleRoundedButton";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}


internal sealed class BattleCombatRuleFocusPointerRelay : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private BattleRuleRouletteController owner;

    public void Configure(BattleRuleRouletteController controller)
    {
        owner = controller;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        owner?.HandleCombatRulePanelPointerEnter();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.HandleCombatRulePanelPointerExit();
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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

    [Header("Optional Slot Machine Art")]
    [Tooltip("비어 있으면 현재 단색 Placeholder 패널을 사용합니다.")]
    [SerializeField] private Sprite machineFrameSprite;
    [SerializeField] private Sprite reelWindowSprite;
    [SerializeField] private Sprite winningRuleBannerSprite;
    [SerializeField] private Sprite resultListPanelSprite;
    [SerializeField] private Sprite spinButtonSprite;
    [SerializeField] private Sprite starOffSprite;
    [SerializeField] private Sprite starOnSprite;

    private GameObject uiRoot;
    private RectTransform machineTab;
    private RectTransform winningRuleTab;
    private RectTransform resultListTab;
    private RectTransform controlTab;

    private Image machineFrameImage;
    private Image winningRuleBannerImage;
    private Image resultListPanelImage;
    private Image spinButtonImage;
    private readonly List<Image> ratingStarImages = new();

    private Text ratingText;
    private Text progressText;
    private Text reelText;
    private Text winningRuleTypeText;
    private Text winningRuleNameText;
    private Text winningRuleDescriptionText;
    private Text summaryText;
    private Text spinButtonText;
    private Button spinButton;
    private bool cancelRequested;
    private bool startBattleConfirmed;

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
        ApplyOptionalArt();
        SetTabsForIntro();
        SetRating(stars);
        SetSpinButtonInteractable(false);

        ratingText.text = $"BATTLE RATING\n{BuildStars(stars)}";
        progressText.text = "RULE ROULETTE";
        reelText.text = "READY";
        reelText.color = Color.white;
        summaryText.text = string.Empty;
        SetSpinButtonLabel("RULE SPIN");

        if (ratingIntroDuration > 0f)
            yield return new WaitForSecondsRealtime(ratingIntroDuration);

        // 룰 추첨 자체는 전부 자동으로 진행합니다.
        for (int i = 0; i < result.SelectedRules.Count; i++)
        {
            if (cancelRequested)
                yield break;

            BattleRuleDefinition selected = result.SelectedRules[i];
            progressText.text = $"RULE {i + 1} / {result.SelectedRules.Count}";
            SetTabVisible(winningRuleTab, false);
            SetTabVisible(resultListTab, i > 0);
            SetSpinButtonLabel("SPINNING");

            float end = Time.unscaledTime + Mathf.Max(0.05f, spinDurationPerRule);
            while (Time.unscaledTime < end)
            {
                BattleRuleDefinition preview = GetRandomPreview(stars);
                if (preview != null)
                    reelText.text = preview.displayName.ToUpperInvariant();

                yield return new WaitForSecondsRealtime(0.055f);
            }

            if (cancelRequested)
                yield break;

            reelText.text = selected.displayName.ToUpperInvariant();
            ShowWinningRule(selected);
            summaryText.text = BuildSummary(result, i + 1);

            SetTabVisible(winningRuleTab, true);
            SetTabVisible(resultListTab, true);

            SetSpinButtonLabel(
                i + 1 < result.SelectedRules.Count
                    ? "NEXT SPIN"
                    : "CHECK RULES");

            if (revealDurationPerRule > 0f)
                yield return new WaitForSecondsRealtime(revealDurationPerRule);
        }

        if (cancelRequested)
            yield break;

        // 최종 결과를 모두 보여준 뒤에는 자동으로 전투를 시작하지 않습니다.
        // 플레이어가 내용을 확인하고 START BATTLE 버튼을 눌러야 다음 단계로 넘어갑니다.
        progressText.text = "THIS BATTLE";
        reelText.text = $"{BuildStars(stars)}\nRULES LOCKED";
        reelText.color = Color.white;
        summaryText.text = BuildSummary(result, result.SelectedRules.Count);

        SetTabVisible(winningRuleTab, false);
        SetTabVisible(resultListTab, true);
        SetTabVisible(controlTab, true);

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

        // 버튼 입력 직후 약간의 확인 템포만 둡니다.
        if (summaryDuration > 0f)
            yield return new WaitForSecondsRealtime(Mathf.Min(summaryDuration, 0.18f));

        if (uiRoot != null)
            uiRoot.SetActive(false);

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
        Image backdropImage = backdrop.gameObject.AddComponent<Image>();
        backdropImage.color = new Color(0.015f, 0.016f, 0.022f, 0.82f);
        backdropImage.raycastTarget = true;

        // 각 영역은 같은 부모 아래의 독립 탭입니다.
        // 추후 Sprite Sheet / Animator를 탭별로 따로 연결할 수 있도록 서로 중첩 소유하지 않습니다.
        machineTab = CreatePanelTab(
            backdrop,
            "MachineTab",
            new Vector2(0.5f, 0.62f),
            new Vector2(940f, 500f),
            new Color(0.035f, 0.038f, 0.052f, 0.985f),
            out machineFrameImage);

        winningRuleTab = CreatePanelTab(
            backdrop,
            "WinningRuleTab",
            new Vector2(0.50f, 0.29f),
            new Vector2(760f, 150f),
            new Color(0.055f, 0.058f, 0.074f, 0.98f),
            out winningRuleBannerImage);

        resultListTab = CreatePanelTab(
            backdrop,
            "ResultListTab",
            new Vector2(0.17f, 0.32f),
            new Vector2(420f, 300f),
            new Color(0.030f, 0.032f, 0.042f, 0.96f),
            out resultListPanelImage);

        controlTab = CreatePanelTab(
            backdrop,
            "ControlTab",
            new Vector2(0.83f, 0.18f),
            new Vector2(300f, 150f),
            Color.clear,
            out _);

        // MachineTab: Battle Rating + 룰렛 릴만 담당.
        ratingText = CreateText(machineTab, "Rating", 34, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetRect(ratingText.rectTransform, new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.95f));

        RectTransform starsRoot = CreateRect(machineTab, "RatingStars");
        starsRoot.anchorMin = starsRoot.anchorMax = new Vector2(0.5f, 0.68f);
        starsRoot.pivot = new Vector2(0.5f, 0.5f);
        starsRoot.sizeDelta = new Vector2(360f, 52f);
        starsRoot.anchoredPosition = Vector2.zero;
        BuildStarImages(starsRoot);

        progressText = CreateText(machineTab, "Progress", 18, FontStyle.Bold, TextAnchor.MiddleCenter);
        progressText.color = new Color(0.16f, 0.86f, 0.92f, 1f);
        SetRect(progressText.rectTransform, new Vector2(0.08f, 0.55f), new Vector2(0.92f, 0.65f));

        RectTransform reelWindow = CreateRect(machineTab, "ReelWindow");
        reelWindow.anchorMin = new Vector2(0.12f, 0.12f);
        reelWindow.anchorMax = new Vector2(0.88f, 0.53f);
        reelWindow.offsetMin = Vector2.zero;
        reelWindow.offsetMax = Vector2.zero;

        Image reelWindowImage = reelWindow.gameObject.AddComponent<Image>();
        reelWindowImage.color = new Color(0.82f, 0.80f, 0.72f, 0.98f);
        reelWindowImage.raycastTarget = false;
        if (reelWindowSprite != null)
        {
            reelWindowImage.sprite = reelWindowSprite;
            reelWindowImage.type = Image.Type.Sliced;
            reelWindowImage.color = Color.white;
        }

        reelText = CreateText(reelWindow, "ReelText", 30, FontStyle.Bold, TextAnchor.MiddleCenter);
        Stretch(reelText.rectTransform);

        // WinningRuleTab: 당첨된 한 개 룰만 표시.
        winningRuleTypeText = CreateText(winningRuleTab, "RuleType", 16, FontStyle.Bold, TextAnchor.MiddleLeft);
        SetRect(winningRuleTypeText.rectTransform, new Vector2(0.06f, 0.68f), new Vector2(0.94f, 0.94f));

        winningRuleNameText = CreateText(winningRuleTab, "RuleName", 26, FontStyle.Bold, TextAnchor.MiddleLeft);
        SetRect(winningRuleNameText.rectTransform, new Vector2(0.06f, 0.34f), new Vector2(0.94f, 0.72f));

        winningRuleDescriptionText = CreateText(winningRuleTab, "RuleDescription", 15, FontStyle.Normal, TextAnchor.MiddleLeft);
        SetRect(winningRuleDescriptionText.rectTransform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.38f));

        // ResultListTab: 누적 결과만 표시.
        summaryText = CreateText(resultListTab, "ResultList", 15, FontStyle.Bold, TextAnchor.UpperLeft);
        summaryText.color = new Color(0.86f, 0.84f, 0.76f, 1f);
        SetRect(summaryText.rectTransform, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));

        // ControlTab: 추첨은 자동이지만 최종 전투 진입은 플레이어 확인 버튼으로만 진행합니다.
        RectTransform buttonRect = CreateRect(controlTab, "RuleSpinButton");
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(280f, 110f);
        buttonRect.anchoredPosition = Vector2.zero;

        spinButtonImage = buttonRect.gameObject.AddComponent<Image>();
        spinButtonImage.color = new Color(0.92f, 0.25f, 0.12f, 1f);
        spinButtonImage.raycastTarget = true;

        spinButton = buttonRect.gameObject.AddComponent<Button>();
        spinButton.interactable = false;
        spinButton.transition = Selectable.Transition.None;
        spinButton.onClick.AddListener(ConfirmStartBattle);

        spinButtonText = CreateText(buttonRect, "Label", 24, FontStyle.Bold, TextAnchor.MiddleCenter);
        Stretch(spinButtonText.rectTransform);

        ApplyOptionalArt();
        SetTabsForIntro();
        uiRoot.SetActive(false);
    }

    private RectTransform CreatePanelTab(
        Transform parent,
        string name,
        Vector2 normalizedAnchor,
        Vector2 size,
        Color fallbackColor,
        out Image image)
    {
        RectTransform rect = CreateRect(parent, name);
        rect.anchorMin = rect.anchorMax = normalizedAnchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;

        image = rect.gameObject.AddComponent<Image>();
        image.color = fallbackColor;
        image.raycastTarget = false;
        return rect;
    }

    private void BuildStarImages(Transform parent)
    {
        ratingStarImages.Clear();

        const int count = 5;
        const float spacing = 62f;
        for (int i = 0; i < count; i++)
        {
            RectTransform starRect = CreateRect(parent, $"Star_{i + 1}");
            starRect.anchorMin = starRect.anchorMax = new Vector2(0.5f, 0.5f);
            starRect.pivot = new Vector2(0.5f, 0.5f);
            starRect.sizeDelta = new Vector2(48f, 48f);
            starRect.anchoredPosition = new Vector2((i - 2) * spacing, 0f);

            Image star = starRect.gameObject.AddComponent<Image>();
            star.raycastTarget = false;
            star.preserveAspect = true;
            ratingStarImages.Add(star);
        }
    }

    private void ApplyOptionalArt()
    {
        ApplySprite(machineFrameImage, machineFrameSprite, new Color(0.035f, 0.038f, 0.052f, 0.985f));
        ApplySprite(winningRuleBannerImage, winningRuleBannerSprite, new Color(0.055f, 0.058f, 0.074f, 0.98f));
        ApplySprite(resultListPanelImage, resultListPanelSprite, new Color(0.030f, 0.032f, 0.042f, 0.96f));
        ApplySprite(spinButtonImage, spinButtonSprite, new Color(0.92f, 0.25f, 0.12f, 1f));
    }

    private static void ApplySprite(Image image, Sprite sprite, Color fallbackColor)
    {
        if (image == null)
            return;

        image.sprite = sprite;
        image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = sprite != null ? Color.white : fallbackColor;
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

            bool enabledStar = i < stars;
            star.enabled = true;
            star.sprite = enabledStar
                ? (starOnSprite != null ? starOnSprite : starOffSprite)
                : (starOffSprite != null ? starOffSprite : starOnSprite);
            star.color = enabledStar
                ? Color.white
                : new Color(1f, 1f, 1f, 0.32f);
        }
    }

    private void ShowWinningRule(BattleRuleDefinition rule)
    {
        if (rule == null)
            return;

        Color polarityColor = GetPolarityColor(rule.polarity);

        if (winningRuleTypeText != null)
        {
            winningRuleTypeText.text = GetPolarityLabel(rule.polarity);
            winningRuleTypeText.color = polarityColor;
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

        if (winningRuleBannerSprite == null && winningRuleBannerImage != null)
        {
            winningRuleBannerImage.color = new Color(
                polarityColor.r * 0.22f,
                polarityColor.g * 0.22f,
                polarityColor.b * 0.22f,
                0.98f);
        }
    }

    private void SetTabsForIntro()
    {
        SetTabVisible(machineTab, true);
        SetTabVisible(winningRuleTab, false);
        SetTabVisible(resultListTab, false);
        SetTabVisible(controlTab, true);
    }

    private static void SetTabVisible(RectTransform tab, bool visible)
    {
        if (tab != null && tab.gameObject.activeSelf != visible)
            tab.gameObject.SetActive(visible);
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
                ? (spinButtonSprite != null
                    ? Color.white
                    : new Color(0.92f, 0.25f, 0.12f, 1f))
                : (spinButtonSprite != null
                    ? new Color(1f, 1f, 1f, 0.45f)
                    : new Color(0.34f, 0.20f, 0.18f, 0.82f));
        }

        if (spinButtonText != null)
        {
            spinButtonText.color = interactable
                ? Color.white
                : new Color(1f, 1f, 1f, 0.50f);
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

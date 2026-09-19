using System;
using UnityEngine;

public enum VillainGrade
{
    C,
    B,
    A,
    S
}

/// <summary>
/// 현재 런/노드에서 전투 오브젝트가 공통으로 참조하는 런타임 문맥입니다.
/// SO 자체를 변형하지 않고, 노드 깊이/노드 종류/빌런 등급에 따른 실제 전투 배율을 전달합니다.
/// 빌런 등급 배율은 기획서 기준으로 Boss 카테고리에만 추가 적용합니다.
/// </summary>
[Serializable]
public class BattleContext
{
    [SerializeField] private int nodeDepth;
    [SerializeField] private VillainGrade villainGrade = VillainGrade.C;
    [SerializeField] private ClanDefinitionSO clan;
    [SerializeField] private ShootingThemeSO shootingTheme;

    [SerializeField] private float depthHpMultiplier = 1f;
    [SerializeField] private float depthDamageMultiplier = 1f;
    [SerializeField] private float nodeTypeHpMultiplier = 1f;
    [SerializeField] private float nodeTypeDamageMultiplier = 1f;

    [Header("Battle Rule Roulette")]
    [SerializeField] private float ruleEnemyHpMultiplier = 1f;
    [SerializeField] private float ruleEnemyDamageMultiplier = 1f;
    [SerializeField] private float ruleEnemyMoveSpeedMultiplier = 1f;
    [SerializeField] private float rulePlayerDamageMultiplier = 1f;
    [SerializeField] private float rulePlayerMoveSpeedMultiplier = 1f;
    [SerializeField] private float ruleHealingMultiplier = 1f;
    [SerializeField] private float ruleKillPointMultiplier = 1f;

    [NonSerialized] private BattleRuleSet battleRules;

    public int NodeDepth => nodeDepth;
    public VillainGrade VillainGrade => villainGrade;
    public ClanDefinitionSO Clan => clan;
    public ShootingThemeSO ShootingTheme => shootingTheme;

    public BattleRuleSet BattleRules => battleRules;
    public float PlayerDamageMultiplier => rulePlayerDamageMultiplier;
    public float PlayerMoveSpeedMultiplier => rulePlayerMoveSpeedMultiplier;
    public float HealingMultiplier => ruleHealingMultiplier;
    public float KillPointMultiplier => ruleKillPointMultiplier;
    public float MonsterMoveSpeedMultiplier => ruleEnemyMoveSpeedMultiplier;

    public float BaseMonsterHpMultiplier =>
        depthHpMultiplier * nodeTypeHpMultiplier * ruleEnemyHpMultiplier;
    public float BaseMonsterDamageMultiplier =>
        depthDamageMultiplier * nodeTypeDamageMultiplier * ruleEnemyDamageMultiplier;

    public float GetMonsterHpMultiplier(MonsterCategory category)
    {
        float multiplier = BaseMonsterHpMultiplier;
        if (category == MonsterCategory.Boss)
            multiplier *= GetVillainGradeMultiplier(villainGrade);
        return multiplier;
    }

    public float GetMonsterDamageMultiplier(MonsterCategory category)
    {
        float multiplier = BaseMonsterDamageMultiplier;
        if (category == MonsterCategory.Boss)
            multiplier *= GetVillainGradeMultiplier(villainGrade);
        return multiplier;
    }

    public void Configure(
        int depth,
        VillainGrade grade,
        ClanDefinitionSO clanDefinition,
        ShootingThemeSO theme,
        float depthHp,
        float depthDamage,
        float nodeHp = 1f,
        float nodeDamage = 1f)
    {
        nodeDepth = Mathf.Max(0, depth);
        villainGrade = grade;
        clan = clanDefinition;
        shootingTheme = theme;
        depthHpMultiplier = Mathf.Max(0.01f, depthHp);
        depthDamageMultiplier = Mathf.Max(0.01f, depthDamage);
        nodeTypeHpMultiplier = Mathf.Max(0.01f, nodeHp);
        nodeTypeDamageMultiplier = Mathf.Max(0.01f, nodeDamage);
        ApplyBattleRules(null);
    }

    public void ApplyBattleRules(BattleRuleSet rules)
    {
        battleRules = rules;
        ruleEnemyHpMultiplier = rules != null ? Mathf.Max(0.01f, rules.EnemyHpMultiplier) : 1f;
        ruleEnemyDamageMultiplier = rules != null ? Mathf.Max(0f, rules.EnemyDamageMultiplier) : 1f;
        ruleEnemyMoveSpeedMultiplier = rules != null ? Mathf.Max(0f, rules.EnemyMoveSpeedMultiplier) : 1f;
        rulePlayerDamageMultiplier = rules != null ? Mathf.Max(0f, rules.PlayerDamageMultiplier) : 1f;
        rulePlayerMoveSpeedMultiplier = rules != null ? Mathf.Max(0f, rules.PlayerMoveSpeedMultiplier) : 1f;
        ruleHealingMultiplier = rules != null ? Mathf.Max(0f, rules.HealingMultiplier) : 1f;
        ruleKillPointMultiplier = rules != null ? Mathf.Max(0f, rules.KillPointMultiplier) : 1f;
    }

    public static float GetVillainGradeMultiplier(VillainGrade grade)
    {
        return grade switch
        {
            VillainGrade.B => 1.3f,
            VillainGrade.A => 1.7f,
            VillainGrade.S => 2.2f,
            _ => 1f
        };
    }
}

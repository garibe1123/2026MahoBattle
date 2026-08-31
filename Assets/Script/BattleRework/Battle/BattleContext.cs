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
/// SO 자체를 변형하지 않고, 노드 깊이/빌런 등급에 따른 실제 전투 배율을 전달합니다.
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

    public int NodeDepth => nodeDepth;
    public VillainGrade VillainGrade => villainGrade;
    public ClanDefinitionSO Clan => clan;
    public ShootingThemeSO ShootingTheme => shootingTheme;

    public float MonsterHpMultiplier =>
        depthHpMultiplier * nodeTypeHpMultiplier * GetVillainGradeMultiplier(villainGrade);

    public float MonsterDamageMultiplier =>
        depthDamageMultiplier * nodeTypeDamageMultiplier * GetVillainGradeMultiplier(villainGrade);

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

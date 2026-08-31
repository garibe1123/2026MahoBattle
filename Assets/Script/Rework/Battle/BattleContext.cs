using UnityEngine;

[System.Serializable]
public struct BattleContext
{
    [Min(0)] public int nodeDepth;
    public VillainGrade villainGrade;
    public ClanDefinitionSO clan;
    public ShootingThemeSO shootingTheme;

    public float GetDepthStatMultiplier()
    {
        return 1f + Mathf.Max(0, nodeDepth) * 0.08f;
    }

    public float GetVillainGradeMultiplier()
    {
        return villainGrade switch
        {
            VillainGrade.B => 1.3f,
            VillainGrade.A => 1.7f,
            VillainGrade.S => 2.2f,
            _ => 1f
        };
    }
}

public enum VillainGrade
{
    C,
    B,
    A,
    S
}

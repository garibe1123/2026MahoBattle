using UnityEngine;

[CreateAssetMenu(menuName = "Game/Rework/Fan Mission", fileName = "FanMission")]
public class FanMissionSO : ScriptableObject
{
    public string missionName;
    public FanMissionType type;
    [TextArea] public string description;

    [Header("Goal")]
    public int targetCount = 1;
    public float duration = 0f;

    [Header("Rewards / Penalties")]
    public int successPopularity = 20;
    public int successFanPoints = 50;
    public int failPopularity = -15;
    public int failFanPoints = 0;
}

public enum FanMissionType
{
    WeaponRestriction,
    TargetMonster,
    MovementRestriction,
    Penalty
}

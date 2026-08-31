using UnityEngine;

[CreateAssetMenu(menuName = "Game/Rework/Battle Equipment", fileName = "BattleEquipment")]
public class BattleEquipmentSO : ScriptableObject
{
    public string equipmentName;
    public Sprite icon;
    public BattleEquipmentType type;

    [Header("Weapon")]
    public PlayerShootingSO shootingData;

    [Header("Trait")]
    public float damageMultiplier = 1f;
    public float moveSpeedMultiplier = 1f;
    public float rangeMultiplier = 1f;
}

public enum BattleEquipmentType
{
    Manual,
    Trait,
    Auto,
    ControlException
}

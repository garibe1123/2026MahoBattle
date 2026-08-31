using System.Collections.Generic;
using UnityEngine;

public enum BattleEquipmentType
{
    Manual,
    Trait,
    Auto,
    ControlException
}

public enum EquipmentRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Unique
}

public enum EquipmentTag
{
    Projectile,
    Melee,
    Break,
    Explosion,
    Precision,
    Critical,
    Area,
    Dash,
    Burn,
    Shock,
    Summon,
    Sustain,
    Defense,
    Heal,
    Resource,
    Control,
    OddWeapon
}

/// <summary>
/// 런 중 획득하는 전투장비 데이터입니다.
/// 장비의 세부 효과를 클래스 조합으로 쪼개기 전에 Tag를 공통 규격으로 먼저 정의해
/// 촬영 스테이지 Reward Bias와 향후 Synergy 시스템이 같은 데이터를 사용하도록 합니다.
/// </summary>
[CreateAssetMenu(menuName = "MahoBattle/Battle Equipment", fileName = "BattleEquipment")]
public class BattleEquipmentSO : ScriptableObject
{
    [Header("Identity")]
    public string equipmentId;
    public string equipmentName;
    public Sprite icon;
    public BattleEquipmentType type;
    public EquipmentRarity rarity = EquipmentRarity.Common;

    [Header("Meta / Reward")]
    [Min(0)] public int unlockLevel;
    [Min(0.01f)] public float baseRewardWeight = 1f;
    public List<EquipmentTag> tags = new();

    [Header("Weapon")]
    public PlayerShootingSO shootingData;

    [Header("Runtime Trait")]
    [Min(0f)] public float damageMultiplier = 1f;
    [Min(0f)] public float moveSpeedMultiplier = 1f;
    [Min(0f)] public float rangeMultiplier = 1f;

    public bool HasTag(EquipmentTag tag)
    {
        return tags != null && tags.Contains(tag);
    }

    public string GetDisplayName()
    {
        return string.IsNullOrWhiteSpace(equipmentName) ? name : equipmentName;
    }
}

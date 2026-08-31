using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Rework/Monster Definition", fileName = "MonsterDefinition")]
public class MonsterDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public string monsterName;
    public MonsterCategory category = MonsterCategory.Aggressive;
    public MonsterMoveType moveType = MonsterMoveType.Chase;

    [Header("Base Stats")]
    public float maxHp = 50f;
    public float defense = 0f;
    public float moveSpeed = 3.5f;
    public float acceleration = 8f;
    public float detectionRange = 8f;

    [Header("Movement")]
    public float stoppingDistance = 0.5f;
    public float minKitingDistance = 3f;
    public float maxKitingDistance = 7f;
    public float dashTriggerRange = 5f;
    public float dashSpeed = 12f;
    public float dashDuration = 0.5f;
    public LayerMask wallLayer;

    [Header("Skills")]
    public List<MonsterSkillConfig> skills = new();

    [Header("Visual")]
    public EnemyVisualSO visual;
}

[System.Serializable]
public class MonsterSkillConfig
{
    public MonsterSkillType type;
    public float cooldown = 1f;
    public float damage = 10f;
    public float range = 1.5f;
    public float duration = 1f;
    public float value = 1f;
    public ProjectileSO projectile;
    public float shieldDurability = 50f;
}

public enum MonsterCategory
{
    Aggressive,
    Defensive,
    Neutral,
    Elite,
    Boss
}

public enum MonsterMoveType
{
    Stationary,
    Chase,
    KeepDistance,
    DashThenChase
}

public enum MonsterSkillType
{
    Melee,
    Projectile,
    Shield,
    ConditionalInvincible,
    AreaBuff,
    AreaDebuff,
    SelfBuff
}

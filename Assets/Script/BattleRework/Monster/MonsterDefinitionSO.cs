using System;
using System.Collections.Generic;
using UnityEngine;

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

[Serializable]
public class MonsterSkillConfig
{
    public MonsterSkillType type;

    [Header("Common")]
    public float cooldown = 1f;
    public float range = 1.5f;
    public float damage = 10f;
    public float windup = 0.25f;
    public float duration = 1f;
    public float value = 1f;

    [Header("Melee")]
    [Tooltip("공격 방향과 대상 방향의 Dot 최소값. 0이면 전방 180도, 0.5면 전방 약 120도 범위입니다.")]
    [Range(-1f, 1f)] public float meleeFrontDot = 0f;

    [Header("Projectile")]
    public ProjectileSO projectileData;

    [Header("Shield")]
    public float shieldDurability = 30f;
    [Range(-1f, 1f)] public float shieldFrontDot = 0.1f;
    public float meleeDamageToShieldMultiplier = 1.5f;

    [Header("Area Skill")]
    public LayerMask targetLayer;
}

/// <summary>
/// 몬스터는 Category + MoveType + SkillConfig[] 조합으로 정의합니다.
/// 기존 EnemyAI_Type처럼 한 enum이 전체 행동을 고정하지 않습니다.
/// </summary>
[CreateAssetMenu(fileName = "MonsterDefinition", menuName = "MahoBattle/Monster Definition")]
public class MonsterDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public string monsterId;
    public string displayName;
    public MonsterCategory category = MonsterCategory.Aggressive;

    [Header("Base Stats")]
    public float maxHp = 50f;
    public float defense = 0f;
    public float moveSpeed = 3.5f;
    public float acceleration = 8f;
    public float detectionRange = 8f;

    [Header("Run Reward")]
    [Min(0)] public int killPointReward = 1;

    [Header("Movement")]
    public MonsterMoveType moveType = MonsterMoveType.Chase;
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

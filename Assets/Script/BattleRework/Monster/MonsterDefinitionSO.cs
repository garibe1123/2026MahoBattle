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

/// <summary>
/// Unity Animator Controller를 사용하지 않는 Enemy Sprite 상태입니다.
/// 기존 값(Idle/Move/Attack/Die)은 직렬화 호환을 위해 앞쪽 순서를 유지합니다.
/// </summary>
public enum EnemyAnimState
{
    Idle = 0,
    Move = 1,
    Attack = 2,
    Die = 3,
    RangedAttack = 4,
    Dash = 5,
    Guard = 6,
    Skill = 7,
    Hit = 8,
    Break = 9
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
/// Enemy SO 내부에서 바로 Sprite를 연결하기 위한 시각 데이터입니다.
/// 별도의 EnemyVisualSO / Unity Animator Controller / AnimationClip asset을 만들 필요가 없습니다.
///
/// 기본 제작 권장:
/// Idle, Move, Attack, Hit, Die
///
/// 행동 확장용:
/// RangedAttack, Dash, Guard, Skill, Break
///
/// 비어 있는 상태는 MonsterSprite Animator가 Idle / Preview Sprite 순으로 안전하게 fallback 합니다.
/// </summary>
[Serializable]
public class MonsterVisualConfig
{
    [Header("Preview / Static Fallback")]
    [Tooltip("애니메이션 Sprite가 아직 없을 때 표시할 정적 Sprite입니다. Enemy SO를 만드는 초기 단계에서도 바로 화면에 표시할 수 있습니다.")]
    public Sprite previewSprite;

    [Header("Core Animation - 기본 몬스터 권장")]
    [Tooltip("대기. 보통 4~6프레임 정도의 호흡/무게중심 변화가 적당합니다.")]
    public Sprite[] idleSprites;
    [Tooltip("이동. 보통 6~8프레임. Chase/KeepDistance 공통 이동 애니메이션으로 사용합니다.")]
    public Sprite[] moveSprites;
    [Tooltip("근접 공격. 현재 MonsterController의 Melee 공격에서 사용합니다.")]
    public Sprite[] attackSprites;
    [Tooltip("피격. 짧은 2~4프레임 반응용입니다. 현재는 Flash와 함께 사용할 수 있도록 준비된 상태입니다.")]
    public Sprite[] hitSprites;
    [Tooltip("사망. 마지막 프레임을 유지한 뒤 Pool로 반환됩니다.")]
    public Sprite[] dieSprites;

    [Header("Action Animation - 행동별 확장")]
    [Tooltip("Projectile 공격용. 활/총/마법 투사체처럼 발사 자세가 근접 공격과 다른 적에게 사용합니다.")]
    public Sprite[] rangedAttackSprites;
    [Tooltip("DashThenChase 이동용. 돌진 자세가 일반 Move와 다를 때 사용합니다.")]
    public Sprite[] dashSprites;
    [Tooltip("Shield 행동용. 방패를 올리거나 방어 자세를 잡는 적에게 사용합니다.")]
    public Sprite[] guardSprites;
    [Tooltip("SelfBuff / AreaBuff / AreaDebuff 등 특수 행동용 공통 Sprite입니다.")]
    public Sprite[] skillSprites;
    [Tooltip("향후 Break/Stagger 시스템에서 쓰는 무력화/경직 Sprite입니다.")]
    public Sprite[] breakSprites;

    [Header("Playback")]
    [Min(1f)] public float fps = 10f;
    [Tooltip("원본 Sprite가 오른쪽을 보고 제작되었다면 true. 좌우 방향은 Transform Scale이 아니라 SpriteRenderer.flipX로 처리합니다.")]
    public bool sourceFacesRight = true;

    [Header("Renderer / Hit Flash")]
    public Material customMaterial;
    public Color spriteColor = Color.white;
    [Tooltip("기본 SpriteRenderer 색상 Flash입니다. 전용 Flash Shader가 없어도 동작합니다.")]
    public Color hitFlashColor = new(1f, 0.55f, 0.55f, 1f);
    [Min(0f)] public float hitFlashDuration = 0.08f;

    public Sprite[] GetFrames(EnemyAnimState state)
    {
        return state switch
        {
            EnemyAnimState.Idle => idleSprites,
            EnemyAnimState.Move => moveSprites,
            EnemyAnimState.Attack => attackSprites,
            EnemyAnimState.Die => dieSprites,
            EnemyAnimState.RangedAttack => rangedAttackSprites,
            EnemyAnimState.Dash => dashSprites,
            EnemyAnimState.Guard => guardSprites,
            EnemyAnimState.Skill => skillSprites,
            EnemyAnimState.Hit => hitSprites,
            EnemyAnimState.Break => breakSprites,
            _ => null
        };
    }

    public Sprite GetFallbackSprite()
    {
        if (previewSprite != null)
            return previewSprite;

        if (idleSprites != null && idleSprites.Length > 0 && idleSprites[0] != null)
            return idleSprites[0];

        if (moveSprites != null && moveSprites.Length > 0 && moveSprites[0] != null)
            return moveSprites[0];

        if (attackSprites != null && attackSprites.Length > 0 && attackSprites[0] != null)
            return attackSprites[0];

        return null;
    }

    public bool HasAnySprite()
    {
        if (GetFallbackSprite() != null)
            return true;

        Array[] arrays =
        {
            idleSprites,
            moveSprites,
            attackSprites,
            hitSprites,
            dieSprites,
            rangedAttackSprites,
            dashSprites,
            guardSprites,
            skillSprites,
            breakSprites
        };

        for (int i = 0; i < arrays.Length; i++)
        {
            if (arrays[i] != null && arrays[i].Length > 0)
                return true;
        }

        return false;
    }
}

/// <summary>
/// Enemy 하나를 완전히 정의하는 SO입니다.
/// 전투 수치 + AI 이동 + Skill + Sprite Animation 데이터를 한 Asset에서 설정합니다.
/// </summary>
[CreateAssetMenu(fileName = "EnemyDefinition", menuName = "MahoBattle/Enemy Definition")]
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

    [Header("Sprite Visual / Animation")]
    [Tooltip("별도 Visual SO 없이 이 EnemyDefinition 안에서 Sprite를 직접 연결합니다.")]
    public MonsterVisualConfig visual = new();

    public bool ValidateDefinition(out string report)
    {
        List<string> warnings = new();
        List<string> errors = new();

        if (string.IsNullOrWhiteSpace(monsterId))
            warnings.Add("monsterId is empty.");

        if (maxHp <= 0f)
            errors.Add("maxHp must be greater than 0.");

        if (visual == null)
            errors.Add("visual is null.");
        else if (!visual.HasAnySprite())
            warnings.Add("No visual Sprite is assigned. Assign Preview Sprite or at least one animation frame array.");

        report = string.Empty;
        if (errors.Count > 0)
            report += "[Errors]\n" + string.Join("\n", errors);

        if (warnings.Count > 0)
        {
            if (!string.IsNullOrEmpty(report))
                report += "\n";
            report += "[Warnings]\n" + string.Join("\n", warnings);
        }

        return errors.Count == 0;
    }
}

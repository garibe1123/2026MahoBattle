using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(EnemyAnimator))]
public class MonsterController : MonoBehaviour, IDamageable
{
    private MonsterDefinitionSO definition;
    private BattleContext context;
    private Transform target;
    private ProjectilePooler projectilePool;
    private Action<MonsterController> onDeath;

    private NavMeshAgent agent;
    private EnemyAnimator animator;

    private float currentHp;
    private float currentDefense;
    private float runtimeDamageMultiplier = 1f;
    private float runtimeMoveMultiplier = 1f;
    private bool dying;

    private Vector2 facing = Vector2.right;
    private readonly List<float> skillCooldowns = new();
    private float actionLockTimer;

    private bool isDashing;
    private float dashTimer;
    private float dashCooldown;
    private Vector2 dashDirection;
    private Vector3 dashStartPosition;

    private bool shieldEnabled;
    private float shieldDurability;
    private MonsterSkillConfig shieldConfig;

    public MonsterDefinitionSO Definition => definition;
    public bool IsAlive => !dying && currentHp > 0f;
    public float Defense => currentDefense;
    public float CurrentHp => currentHp;
    public bool ShieldEnabled => shieldEnabled;
    public float ShieldDurability => shieldDurability;
    public Vector2 Facing => facing;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<EnemyAnimator>();

        agent.updateRotation = false;
        agent.updateUpAxis = false;
    }

    public void Setup(
        MonsterDefinitionSO monsterDefinition,
        BattleContext battleContext,
        Transform playerTarget,
        ProjectilePooler enemyProjectilePool,
        Action<MonsterController> deathCallback)
    {
        if (monsterDefinition == null)
        {
            Debug.LogError("[Monster] Setup failed: MonsterDefinitionSO is null.");
            dying = true;
            return;
        }

        definition = monsterDefinition;
        context = battleContext;
        target = playerTarget;
        projectilePool = enemyProjectilePool;
        onDeath = deathCallback;

        dying = false;
        isDashing = false;
        dashCooldown = 0f;
        actionLockTimer = 0f;
        runtimeDamageMultiplier = 1f;
        runtimeMoveMultiplier = 1f;
        facing = Vector2.right;

        float hpMultiplier = context != null
            ? context.GetMonsterHpMultiplier(definition.category)
            : 1f;

        currentHp = Mathf.Max(1f, definition.maxHp * hpMultiplier);
        currentDefense = Mathf.Max(0f, definition.defense);

        ConfigureAgent();
        ConfigureSkills();
        ConfigureVisual();
    }

    private void ConfigureAgent()
    {
        if (agent == null || definition == null)
            return;

        if (definition.moveType == MonsterMoveType.Stationary)
        {
            agent.enabled = false;
            return;
        }

        agent.enabled = true;
        agent.speed = Mathf.Max(0f, definition.moveSpeed);
        agent.acceleration = Mathf.Max(0f, definition.acceleration);
        agent.stoppingDistance = Mathf.Max(0f, definition.stoppingDistance);
        agent.isStopped = false;
    }

    private void ConfigureSkills()
    {
        skillCooldowns.Clear();
        shieldEnabled = false;
        shieldDurability = 0f;
        shieldConfig = null;

        if (definition.skills == null)
            return;

        for (int i = 0; i < definition.skills.Count; i++)
        {
            MonsterSkillConfig skill = definition.skills[i];
            skillCooldowns.Add(0f);

            if (skill != null && skill.type == MonsterSkillType.Shield && shieldConfig == null)
            {
                shieldConfig = skill;
                shieldDurability = Mathf.Max(0f, skill.shieldDurability);
                shieldEnabled = shieldDurability > 0f;
            }
        }
    }

    private void ConfigureVisual()
    {
        if (animator == null || definition == null)
            return;

        animator.SetupVisual(definition.visual);
        animator.SetFacing(facing.x);
        PlayIdleAnimation();
    }

    private void Update()
    {
        if (!IsAlive || definition == null || target == null)
            return;

        if (agent.enabled && !agent.isOnNavMesh)
            return;

        TickCooldowns();
        UpdateFacing();

        float distance = Vector2.Distance(transform.position, target.position);
        if (distance > definition.detectionRange)
        {
            StopMovement();
            UpdateAnimation(false);
            return;
        }

        if (isDashing)
        {
            UpdateDash();
        }
        else if (actionLockTimer > 0f)
        {
            // 공격/스킬 애니메이션 중에는 NavMesh 이동을 멈춰 Sprite가 미끄러지지 않게 합니다.
            StopMovement();
        }
        else
        {
            UpdateMovement(distance);
            TryUseAvailableSkill(distance);
        }

        UpdateAnimation(IsMoving());
    }

    private void TickCooldowns()
    {
        for (int i = 0; i < skillCooldowns.Count; i++)
            skillCooldowns[i] = Mathf.Max(0f, skillCooldowns[i] - Time.deltaTime);

        dashCooldown = Mathf.Max(0f, dashCooldown - Time.deltaTime);
        actionLockTimer = Mathf.Max(0f, actionLockTimer - Time.deltaTime);
    }

    private void UpdateFacing()
    {
        Vector2 toTarget = (Vector2)target.position - (Vector2)transform.position;
        if (toTarget.sqrMagnitude <= 0.001f)
            return;

        facing = toTarget.normalized;
        animator?.SetFacing(facing.x);
    }

    private void UpdateMovement(float distance)
    {
        switch (definition.moveType)
        {
            case MonsterMoveType.Stationary:
                break;

            case MonsterMoveType.Chase:
                MoveTo(target.position);
                break;

            case MonsterMoveType.KeepDistance:
                UpdateKeepDistance(distance);
                break;

            case MonsterMoveType.DashThenChase:
                if (distance <= definition.dashTriggerRange && dashCooldown <= 0f)
                    BeginDash();
                else
                    MoveTo(target.position);
                break;
        }
    }

    private void UpdateKeepDistance(float distance)
    {
        if (!agent.enabled)
            return;

        if (distance < definition.minKitingDistance)
        {
            Vector2 away = ((Vector2)transform.position - (Vector2)target.position).normalized;
            MoveTo((Vector2)transform.position + away * 3f);
        }
        else if (distance > definition.maxKitingDistance)
        {
            MoveTo(target.position);
        }
        else if (agent.isOnNavMesh)
        {
            agent.ResetPath();
        }
    }

    private void MoveTo(Vector2 destination)
    {
        if (!agent.enabled || !agent.isOnNavMesh)
            return;

        agent.speed = Mathf.Max(0f, definition.moveSpeed * runtimeMoveMultiplier);
        agent.SetDestination(destination);
    }

    private void StopMovement()
    {
        if (agent.enabled && agent.isOnNavMesh)
            agent.ResetPath();
    }

    private void BeginDash()
    {
        Vector2 direction = ((Vector2)target.position - (Vector2)transform.position).normalized;
        if (direction.sqrMagnitude <= 0.001f)
            return;

        isDashing = true;
        dashTimer = Mathf.Max(0.01f, definition.dashDuration);
        dashDirection = direction;
        dashStartPosition = transform.position;
        facing = direction;
        animator?.SetFacing(facing.x);
        animator?.Play(EnemyAnimState.Dash, true, restart: true);

        if (agent.enabled)
            agent.enabled = false;
    }

    private void UpdateDash()
    {
        float step = Mathf.Max(0f, definition.dashSpeed) * Time.deltaTime;
        float castRadius = agent != null ? Mathf.Max(0.08f, agent.radius * 0.5f) : 0.12f;

        RaycastHit2D wallHit = Physics2D.CircleCast(
            transform.position,
            castRadius,
            dashDirection,
            step + 0.05f,
            definition.wallLayer);

        if (wallHit.collider != null)
        {
            Vector2 safePosition = wallHit.point - dashDirection * castRadius;
            transform.position = safePosition;
            FinishDash();
            return;
        }

        transform.position += (Vector3)(dashDirection * step);
        dashTimer -= Time.deltaTime;

        if (dashTimer <= 0f)
            FinishDash();
    }

    private void FinishDash()
    {
        isDashing = false;
        dashCooldown = 1f;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            transform.position = hit.position;
            agent.enabled = true;
            if (agent.isOnNavMesh)
                agent.isStopped = false;
            return;
        }

        if (NavMesh.SamplePosition(dashStartPosition, out hit, 1.5f, NavMesh.AllAreas))
        {
            transform.position = hit.position;
            agent.enabled = true;
            if (agent.isOnNavMesh)
                agent.isStopped = false;
            Debug.LogWarning($"[Monster] Dash ended off NavMesh and was restored to dash start: {name}");
            return;
        }

        agent.enabled = false;
        Debug.LogError($"[Monster] Dash recovery failed. Disabling movement for diagnostic safety: {name}");
    }

    private void TryUseAvailableSkill(float distance)
    {
        if (actionLockTimer > 0f || definition.skills == null)
            return;

        for (int i = 0; i < definition.skills.Count; i++)
        {
            MonsterSkillConfig skill = definition.skills[i];
            if (skill == null || i >= skillCooldowns.Count || skillCooldowns[i] > 0f)
                continue;
            if (skill.type == MonsterSkillType.Shield || skill.type == MonsterSkillType.ConditionalInvincible)
                continue;
            if (distance > skill.range)
                continue;

            if (!ExecuteSkill(skill))
                continue;

            skillCooldowns[i] = Mathf.Max(0.01f, skill.cooldown);
            EnemyAnimState state = GetSkillAnimationState(skill.type);
            actionLockTimer = GetActionLockDuration(skill, state);
            break;
        }
    }

    private bool ExecuteSkill(MonsterSkillConfig skill)
    {
        switch (skill.type)
        {
            case MonsterSkillType.Melee:
                StartCoroutine(MeleeRoutine(skill, facing));
                return true;

            case MonsterSkillType.Projectile:
                if (projectilePool == null || skill.projectileData == null || target == null)
                    return false;
                StartCoroutine(ProjectileRoutine(skill, facing));
                return true;

            case MonsterSkillType.SelfBuff:
                StartCoroutine(SelfBuffRoutine(skill));
                return true;

            case MonsterSkillType.AreaBuff:
            case MonsterSkillType.AreaDebuff:
                // 실제 Area 효과 로직이 구현될 때 Skill Sprite 상태를 사용합니다.
                return false;

            default:
                return false;
        }
    }

    private IEnumerator MeleeRoutine(MonsterSkillConfig skill, Vector2 attackFacing)
    {
        const EnemyAnimState state = EnemyAnimState.Attack;
        PlayActionAnimation(state);
        yield return WaitForSkillImpact(skill, state);

        if (!IsAlive || target == null || definition == null)
            yield break;

        Collider2D[] hits = Physics2D.OverlapCircleAll(
            transform.position,
            Mathf.Max(0.05f, skill.range),
            skill.targetLayer);

        float battleMultiplier = context != null
            ? context.GetMonsterDamageMultiplier(definition.category)
            : 1f;

        DamageContext damage = new(
            gameObject,
            transform.position,
            skill.damage,
            battleMultiplier * runtimeDamageMultiplier,
            0f,
            DamageKind.Melee);

        Vector2 normalizedFacing = attackFacing.sqrMagnitude > 0.001f
            ? attackFacing.normalized
            : facing.normalized;

        HashSet<IDamageable> damagedTargets = new();
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null)
                continue;
            if (!CombatDamage.TryFindDamageable(hit.transform, out IDamageable damageable))
                continue;
            if (!damageable.IsAlive || !damagedTargets.Add(damageable))
                continue;

            Vector2 toTarget = ((Vector2)hit.bounds.center - (Vector2)transform.position).normalized;
            if (toTarget.sqrMagnitude > 0.001f &&
                Vector2.Dot(normalizedFacing, toTarget) < skill.meleeFrontDot)
            {
                continue;
            }

            float finalDamage = CombatDamage.Calculate(damage, damageable.Defense);
            damageable.ReceiveDamage(damage, finalDamage);
        }
    }

    private IEnumerator ProjectileRoutine(MonsterSkillConfig skill, Vector2 attackFacing)
    {
        const EnemyAnimState state = EnemyAnimState.RangedAttack;
        PlayActionAnimation(state);
        yield return WaitForSkillImpact(skill, state);

        if (!IsAlive || target == null || definition == null)
            yield break;

        FireProjectile(skill, attackFacing);
    }

    private IEnumerator WaitForSkillImpact(MonsterSkillConfig skill, EnemyAnimState state)
    {
        if (skill == null)
            yield break;

        bool useSpriteFrame = skill.impactFrame >= 0 && animator != null && HasAnimation(state);
        if (!useSpriteFrame)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, skill.windup));
            yield break;
        }

        Sprite[] frames = definition.visual.GetFrames(state);
        int targetFrame = Mathf.Clamp(skill.impactFrame, 0, frames.Length - 1);

        while (IsAlive &&
               animator != null &&
               animator.currentState == state &&
               animator.CurrentFrameIndex < targetFrame)
        {
            yield return null;
        }
    }

    private bool FireProjectile(MonsterSkillConfig skill, Vector2 shotDirection)
    {
        if (projectilePool == null || skill.projectileData == null || target == null)
            return false;

        Projectile projectile = projectilePool.Get();
        if (projectile == null)
            return false;

        float battleMultiplier = context != null
            ? context.GetMonsterDamageMultiplier(definition.category)
            : 1f;

        Vector2 dir = shotDirection.sqrMagnitude > 0.001f
            ? shotDirection.normalized
            : ((Vector2)target.position - (Vector2)transform.position).normalized;

        if (dir.sqrMagnitude <= 0.001f)
            dir = Vector2.right;

        projectile.transform.position = transform.position;
        projectile.Setup(
            skill.projectileData,
            dir,
            projectilePool,
            target,
            target.position,
            gameObject,
            battleMultiplier * runtimeDamageMultiplier);
        return true;
    }

    private IEnumerator SelfBuffRoutine(MonsterSkillConfig skill)
    {
        PlaySkillAnimation();

        float oldMove = runtimeMoveMultiplier;
        float oldDamage = runtimeDamageMultiplier;
        float multiplier = Mathf.Max(0.01f, skill.value);

        runtimeMoveMultiplier *= multiplier;
        runtimeDamageMultiplier *= multiplier;

        yield return new WaitForSeconds(Mathf.Max(0f, skill.duration));

        if (!IsAlive)
            yield break;

        runtimeMoveMultiplier = oldMove;
        runtimeDamageMultiplier = oldDamage;
    }

    public void ReceiveDamage(DamageContext damageContext, float finalDamage)
    {
        if (!IsAlive || finalDamage <= 0f)
            return;

        if (HasConditionalInvincibility() && isDashing)
            return;

        if (TryBlockWithShield(damageContext, finalDamage))
        {
            PlayGuardAnimation();
            return;
        }

        currentHp = Mathf.Max(0f, currentHp - finalDamage);
        animator?.Flash();

        if (currentHp <= 0f)
        {
            Die();
            return;
        }

        PlayHitAnimation();
    }

    private bool TryBlockWithShield(DamageContext damageContext, float damage)
    {
        if (!shieldEnabled || shieldConfig == null || shieldDurability <= 0f)
            return false;

        Vector2 sourceDirection = (damageContext.SourcePosition - (Vector2)transform.position).normalized;
        float frontDot = Vector2.Dot(facing, sourceDirection);
        if (frontDot < shieldConfig.shieldFrontDot)
            return false;

        float shieldDamage = damage;
        if (damageContext.Kind == DamageKind.Melee)
            shieldDamage *= Mathf.Max(1f, shieldConfig.meleeDamageToShieldMultiplier);

        shieldDurability -= shieldDamage;
        if (shieldDurability <= 0f)
        {
            shieldDurability = 0f;
            shieldEnabled = false;
        }

        return true;
    }

    private bool HasConditionalInvincibility()
    {
        if (definition == null || definition.skills == null)
            return false;

        for (int i = 0; i < definition.skills.Count; i++)
        {
            if (definition.skills[i] != null &&
                definition.skills[i].type == MonsterSkillType.ConditionalInvincible)
            {
                return true;
            }
        }

        return false;
    }

    private void Die()
    {
        if (dying)
            return;

        dying = true;
        actionLockTimer = 0f;
        StopAllCoroutines();

        if (agent.enabled)
        {
            if (agent.isOnNavMesh)
                agent.isStopped = true;

            agent.enabled = false;
        }

        if (animator != null && HasAnimation(EnemyAnimState.Die))
        {
            animator.Play(
                EnemyAnimState.Die,
                false,
                NotifyDeath,
                true);
        }
        else
        {
            NotifyDeath();
        }
    }

    private void NotifyDeath()
    {
        Action<MonsterController> callback = onDeath;
        onDeath = null;
        callback?.Invoke(this);
    }

    private bool IsMoving()
    {
        return isDashing ||
               (actionLockTimer <= 0f &&
                agent.enabled &&
                agent.isOnNavMesh &&
                agent.velocity.sqrMagnitude > 0.01f);
    }

    private void UpdateAnimation(bool moving)
    {
        if (animator == null || definition == null || definition.visual == null)
            return;

        if (animator.currentState == EnemyAnimState.Die || IsOneShotActionPlaying())
            return;

        if (isDashing)
        {
            animator.Play(EnemyAnimState.Dash, true);
            return;
        }

        if (moving)
        {
            animator.Play(EnemyAnimState.Move, true);
            return;
        }

        if (shieldEnabled && HasAnimation(EnemyAnimState.Guard))
        {
            animator.Play(EnemyAnimState.Guard, true);
            return;
        }

        PlayIdleAnimation();
    }

    private bool IsOneShotActionPlaying()
    {
        if (animator == null || !animator.IsPlaying)
            return false;

        return animator.currentState == EnemyAnimState.Attack ||
               animator.currentState == EnemyAnimState.RangedAttack ||
               animator.currentState == EnemyAnimState.Skill ||
               animator.currentState == EnemyAnimState.Hit ||
               animator.currentState == EnemyAnimState.Break;
    }

    private void PlayActionAnimation(EnemyAnimState state)
    {
        if (animator == null || definition == null || definition.visual == null)
            return;

        animator.Play(state, false, PlayIdleAnimation, true);
    }

    private void PlaySkillAnimation()
    {
        PlayActionAnimation(EnemyAnimState.Skill);
    }

    private void PlayGuardAnimation()
    {
        if (animator == null || definition == null || definition.visual == null)
            return;

        if (HasAnimation(EnemyAnimState.Guard))
            animator.Play(EnemyAnimState.Guard, true, restart: true);
    }

    private void PlayHitAnimation()
    {
        if (animator == null || definition == null || definition.visual == null)
            return;

        if (animator.currentState == EnemyAnimState.Die ||
            animator.currentState == EnemyAnimState.Attack ||
            animator.currentState == EnemyAnimState.RangedAttack ||
            animator.currentState == EnemyAnimState.Skill)
        {
            return;
        }

        if (HasAnimation(EnemyAnimState.Hit))
            animator.Play(EnemyAnimState.Hit, false, PlayIdleAnimation, true);
    }

    private void PlayIdleAnimation()
    {
        if (animator == null || definition == null || definition.visual == null)
            return;

        if (shieldEnabled && HasAnimation(EnemyAnimState.Guard))
        {
            animator.Play(EnemyAnimState.Guard, true);
            return;
        }

        animator.Play(EnemyAnimState.Idle, true);
    }

    private EnemyAnimState GetSkillAnimationState(MonsterSkillType skillType)
    {
        return skillType switch
        {
            MonsterSkillType.Melee => EnemyAnimState.Attack,
            MonsterSkillType.Projectile => EnemyAnimState.RangedAttack,
            MonsterSkillType.SelfBuff => EnemyAnimState.Skill,
            MonsterSkillType.AreaBuff => EnemyAnimState.Skill,
            MonsterSkillType.AreaDebuff => EnemyAnimState.Skill,
            MonsterSkillType.Shield => EnemyAnimState.Guard,
            _ => EnemyAnimState.Idle
        };
    }

    private float GetActionLockDuration(MonsterSkillConfig skill, EnemyAnimState state)
    {
        if (skill == null)
            return 0.05f;

        float animationDuration = animator != null
            ? animator.GetStateDuration(state)
            : 0f;

        return Mathf.Max(0.05f, Mathf.Max(Mathf.Max(0f, skill.windup), animationDuration));
    }

    private bool HasAnimation(EnemyAnimState state)
    {
        if (definition == null || definition.visual == null)
            return false;

        Sprite[] frames = definition.visual.GetFrames(state);
        if (frames == null || frames.Length == 0)
            return false;

        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null)
                return true;
        }

        return false;
    }

    public void PrepareForPool()
    {
        StopAllCoroutines();
        onDeath = null;
        target = null;
        definition = null;
        context = null;
        projectilePool = null;
        dying = true;
        shieldEnabled = false;
        shieldDurability = 0f;
        shieldConfig = null;
        isDashing = false;
        actionLockTimer = 0f;
        skillCooldowns.Clear();

        animator?.Stop(false);

        if (agent != null)
            agent.enabled = false;
    }
}

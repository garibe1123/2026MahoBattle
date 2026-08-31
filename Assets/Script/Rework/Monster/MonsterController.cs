using System;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider2D))]
public class MonsterController : MonoBehaviour, IDamageable
{
    [Header("Combat Layers")]
    [SerializeField] private LayerMask targetLayer;

    private MonsterDefinitionSO definition;
    private BattleContext context;
    private Transform target;
    private ProjectilePooler projectilePool;
    private MonsterPool ownerPool;
    private NavMeshAgent agent;

    private float currentHp;
    private float[] skillTimers;
    private bool dead;
    private bool dashing;
    private float dashTimer;
    private Vector3 dashDirection;

    public bool IsAlive => !dead && currentHp > 0f;
    public MonsterDefinitionSO Definition => definition;
    public event Action<MonsterController> Died;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        agent.updateUpAxis = false;
    }

    public void Setup(
        MonsterDefinitionSO data,
        BattleContext battleContext,
        Transform playerTarget,
        ProjectilePooler bulletPool,
        MonsterPool pool)
    {
        definition = data;
        context = battleContext;
        target = playerTarget;
        projectilePool = bulletPool;
        ownerPool = pool;
        dead = false;
        dashing = false;

        float scale = context.GetDepthStatMultiplier();
        if (definition.category == MonsterCategory.Boss)
            scale *= context.GetVillainGradeMultiplier();
        else if (definition.category == MonsterCategory.Elite)
            scale *= 1.5f;

        currentHp = definition.maxHp * scale;
        skillTimers = new float[definition.skills.Count];

        agent.enabled = definition.moveType != MonsterMoveType.Stationary;
        if (agent.enabled)
        {
            agent.speed = definition.moveSpeed;
            agent.acceleration = definition.acceleration;
            agent.stoppingDistance = definition.stoppingDistance;
            agent.isStopped = false;
        }
    }

    private void Update()
    {
        if (!IsAlive || definition == null || target == null) return;
        if (agent.enabled && !agent.isOnNavMesh) return;

        TickSkillTimers();
        UpdateMovement();
        TryUseSkills();
    }

    private void TickSkillTimers()
    {
        for (int i = 0; i < skillTimers.Length; i++)
            skillTimers[i] -= Time.deltaTime;
    }

    private void UpdateMovement()
    {
        float distance = Vector2.Distance(transform.position, target.position);
        if (distance > definition.detectionRange) return;

        switch (definition.moveType)
        {
            case MonsterMoveType.Stationary:
                return;

            case MonsterMoveType.Chase:
                agent.SetDestination(target.position);
                break;

            case MonsterMoveType.KeepDistance:
                if (distance < definition.minKitingDistance)
                {
                    Vector3 flee = (transform.position - target.position).normalized;
                    agent.SetDestination(transform.position + flee * 3f);
                }
                else if (distance > definition.maxKitingDistance)
                {
                    agent.SetDestination(target.position);
                }
                else
                {
                    agent.ResetPath();
                }
                break;

            case MonsterMoveType.DashThenChase:
                UpdateDashThenChase(distance);
                break;
        }
    }

    private void UpdateDashThenChase(float distance)
    {
        if (dashing)
        {
            dashTimer -= Time.deltaTime;
            transform.position += dashDirection * (definition.dashSpeed * Time.deltaTime);

            bool hitWall = Physics2D.Raycast(transform.position, dashDirection, 0.35f, definition.wallLayer);
            if (dashTimer <= 0f || hitWall)
            {
                dashing = false;
                agent.enabled = true;
            }
            return;
        }

        if (distance <= definition.dashTriggerRange)
        {
            dashDirection = (target.position - transform.position).normalized;
            dashTimer = definition.dashDuration;
            dashing = true;
            agent.enabled = false;
            return;
        }

        agent.SetDestination(target.position);
    }

    private void TryUseSkills()
    {
        float distance = Vector2.Distance(transform.position, target.position);

        for (int i = 0; i < definition.skills.Count; i++)
        {
            if (skillTimers[i] > 0f) continue;

            MonsterSkillConfig skill = definition.skills[i];
            if (distance > skill.range) continue;

            switch (skill.type)
            {
                case MonsterSkillType.Melee:
                    ResolveMelee(skill);
                    break;
                case MonsterSkillType.Projectile:
                    FireProjectile(skill);
                    break;
                default:
                    continue;
            }

            skillTimers[i] = skill.cooldown;
        }
    }

    // Animation Event에서도 호출할 수 있도록 public으로 유지.
    public void ResolveMelee(MonsterSkillConfig skill)
    {
        Collider2D hit = Physics2D.OverlapCircle(transform.position, skill.range, targetLayer);
        if (hit == null) return;

        DamageContext damage = new DamageContext(gameObject, skill.damage * GetAttackScale());
        CombatDamage.Apply(hit, damage);
    }

    private void FireProjectile(MonsterSkillConfig skill)
    {
        if (skill.projectile == null || projectilePool == null) return;

        Vector2 direction = (target.position - transform.position).normalized;
        Projectile projectile = projectilePool.Get();
        projectile.transform.position = transform.position;
        projectile.Setup(skill.projectile, direction, projectilePool, target, target.position);
    }

    public void ReceiveDamage(DamageContext damage)
    {
        if (!IsAlive) return;

        float finalDamage = CombatDamage.Calculate(damage, definition.defense);
        currentHp -= finalDamage;

        if (currentHp <= 0f)
            Die();
    }

    private float GetAttackScale()
    {
        float scale = context.GetDepthStatMultiplier();
        if (definition.category == MonsterCategory.Boss)
            scale *= context.GetVillainGradeMultiplier();
        else if (definition.category == MonsterCategory.Elite)
            scale *= 1.5f;
        return scale;
    }

    private void Die()
    {
        if (dead) return;
        dead = true;

        if (agent.enabled)
            agent.isStopped = true;

        Died?.Invoke(this);
        ownerPool?.Return(this);
    }
}

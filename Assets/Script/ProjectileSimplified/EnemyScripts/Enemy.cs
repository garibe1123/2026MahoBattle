using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using static UnityEngine.GraphicsBuffer;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider2D))]
public class Enemy : MonoBehaviour
{
    private EnemySO so;
    private NavMeshAgent agent;
    private Transform player;
    private EnemyAnimator anim;

    // AI 상태 관리
    private enum State { Idle, Chase, Attack, Flee, Dashing }
    private State currentState;

    // 타이머 및 상태 변수
    private float attackTimer;
    private float dashTimer;
    private Vector3 dashDirection;
    private bool isEnraged = false; // Ambusher 전용
    private float spawnTimer;
    // (기존 변수들 아래에 추가)
    private float kitingDir = 1f;         // 1(우측) 또는 -1(좌측) 무빙
    private float kitingChangeTimer = 0f; // 무빙 방향을 바꿀 타이머

    private float currentHp;

    private ProjectilePooler projectilePooler;

    [Header("hit Projectile layer")]
    public LayerMask HitbulletLayer; 


    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        // ★ 2D NavMesh 세팅: Z축 회전과 Y축 이동 방지
        agent.updateRotation = false;
        agent.updateUpAxis = false;

        anim = GetComponent<EnemyAnimator>(); // ★ 애니메이터 겟
    }

    // Enemy.cs 의 Setup() 함수 안을 이렇게 고쳐주세요!
    public void Setup(EnemySO enemyData, Transform playerTarget, ProjectilePooler bulletPool)
    {
        so = enemyData;
        player = playerTarget;
        currentHp = so.maxHp;
        projectilePooler = bulletPool;

        // ★ 버그 수정 1: 이전 생애(?)에 NavMesh가 꺼졌을 수도 있으니 무조건 켜고 시작!
        agent.enabled = true;

        agent.speed = so.baseMoveSpeed;
        agent.acceleration = so.acceleration;
        agent.stoppingDistance = so.stoppingDistance;

        currentState = State.Idle;
        isEnraged = false;
        attackTimer = 0f;
        spawnTimer = 0f;

        // 고정형이거나 둥지면 NavMesh 비활성화 (켜놓고 다시 끄는 방식)
        if (so.aiType == EnemyAI_Type.Turret || so.aiType == EnemyAI_Type.Nest)
            agent.enabled = false;

        if (so.visual != null)
        {
            anim.SetupVisual(so.visual);
            anim.Play(EnemyAnimState.Idle, so.visual.idleSprites, so.visual.fps, true);
        }
    }

    // ★ 새롭게 추가할 데미지 받는 함수! 
    // (이전에 만든 레이어나 이펙트 틱 데미지 쪽에서 이 함수를 호출해주면 됩니다)
    public void TakeDamage(float amount)
    {
        if (currentHp <= 0) return; // 이미 죽은 놈이면 무시

        currentHp -= amount;
        Debug.Log($"{gameObject.name} 피격! 남은 체력: {currentHp}");

        if (anim != null) anim.Flash(); // ★ 피격 시 경직 없이 하얗게 번쩍!

        if (currentHp <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        Debug.Log($"{gameObject.name} 사망!");

        if (ItemSpawner.Instance != null)
        {
            // 1. 기존의 일반 랜덤 드랍 판정 (잡몹용)
            ItemSpawner.Instance.TryDropLootFromEnemy(transform.position);

            // 2. ★ [새로 추가] 엘리트/보스 확정 드랍 판정!
            // SO에 확정 아이템이 들어있다면 무조건 스폰합니다.
            if (so.guaranteedItemDrop != null)
            {
                ItemSpawner.Instance.SpawnItem(transform.position, so.guaranteedItemDrop);
            }

            // SO에 확정 무기가 들어있다면 무조건 스폰합니다.
            if (so.guaranteedWeaponDrop != null)
            {
                ItemSpawner.Instance.SpawnWeapon(transform.position + Vector3.right * 0.5f, so.guaranteedWeaponDrop);
            }
        }

        // --- 사망 애니메이션 및 풀링 반환 로직 ---
        if (so.visual != null && so.visual.dieSprites != null && so.visual.dieSprites.Length > 0)
        {
            agent.isStopped = true;
            anim.Play(EnemyAnimState.Die, so.visual.dieSprites, so.visual.fps, false, () => {
                EnemyPooler.Instance.ReturnToPool(this);
            });
        }
        else
        {
            EnemyPooler.Instance.ReturnToPool(this);
        }
    }

    void Update()
    {
        if (so == null || player == null) return;

        // ★ 방어막 1: Agent가 켜져 있는데 아직 바닥(NavMesh)을 인식 못 했다면 이번 프레임은 스킵!
        if (agent.enabled && !agent.isOnNavMesh) return;

        attackTimer -= Time.deltaTime;
        switch (so.aiType)
        {
            case EnemyAI_Type.Swarm: HandleSwarm(); break;
            case EnemyAI_Type.Ranged: HandleRanged(); break;
            case EnemyAI_Type.Turret: HandleTurret(); break;
            case EnemyAI_Type.Juggernaut: HandleJuggernaut(); break;
            case EnemyAI_Type.Ambusher: HandleAmbusher(); break;
            case EnemyAI_Type.Nest: HandleNest(); break;
        }

        UpdateAnimation(); // ★ 매 프레임 애니메이션 갱신 처리
    }

    // 4. UpdateAnimation() 함수 새로 추가 (위치: Update 함수 아래쪽)
    void UpdateAnimation()
    {
        if (so.visual == null) return;

        // 공격 중이거나 죽는 중이면 걷기/대기 애니메이션으로 덮어쓰지 않음
        if (anim.currentState == EnemyAnimState.Attack ||
            anim.currentState == EnemyAnimState.Die) return;

        // 이동 중이면 Move, 멈춰있으면 Idle 재생
        if (agent.velocity.sqrMagnitude > 0.01f || currentState == State.Dashing)
        {
            anim.Play(EnemyAnimState.Move, so.visual.moveSprites, so.visual.fps, true);

            // X축 이동 방향에 따라 스프라이트 좌우 반전
            if (agent.velocity.x < 0 || dashDirection.x < 0) transform.localScale = new Vector3(-1, 1, 1);
            else if (agent.velocity.x > 0 || dashDirection.x > 0) transform.localScale = new Vector3(1, 1, 1);
        }
        else
        {
            anim.Play(EnemyAnimState.Idle, so.visual.idleSprites, so.visual.fps, true);
        }
    }

    // 1. 맹목적 추격 (좀비)
    void HandleSwarm()
    {
        agent.SetDestination(player.position);

        if (Vector2.Distance(transform.position, player.position) <= so.attackRange)
        {
            if (attackTimer <= 0) TryAttack();
        }
    }

    // 2. 카이팅 (거리 조절 및 무빙샷)
    void HandleRanged()
    {
        float dist = Vector2.Distance(transform.position, player.position);

        if (dist < so.minKitingDistance)
        {
            // 너무 가까움 -> 원래 속도로 도망
            agent.speed = so.baseMoveSpeed;
            Vector3 fleeDir = (transform.position - player.position).normalized;
            agent.SetDestination(transform.position + fleeDir * 3f);
        }
        else if (dist > so.maxKitingDistance)
        {
            // 너무 멀음 -> 원래 속도로 추격
            agent.speed = so.baseMoveSpeed;
            agent.SetDestination(player.position);
        }
        else
        {
            // ★ 적정 거리 진입! (Sweet Spot)
            // 1. 이동 속도를 본래 속도의 2/3로 감소
            agent.speed = so.baseMoveSpeed * 0.666f;

            // 2. 무빙 방향(좌/우) 랜덤 전환 타이머
            kitingChangeTimer -= Time.deltaTime;
            if (kitingChangeTimer <= 0f)
            {
                kitingDir = Random.value > 0.5f ? 1f : -1f; // 50% 확률로 방향 반전
                kitingChangeTimer = Random.Range(1.5f, 3f); // 1.5 ~ 3초마다 방향 고민함
            }

            // 3. 플레이어를 바라보는 방향의 90도 수직(직각) 벡터 계산 -> 원운동!
            Vector2 dirToPlayer = (player.position - transform.position).normalized;
            Vector2 strafeDir = new Vector2(-dirToPlayer.y, dirToPlayer.x) * kitingDir;

            // 슬금슬금 게걸음 치도록 NavMesh 목적지 갱신
            agent.SetDestination(transform.position + (Vector3)strafeDir * 2f);

            // 4. 무빙하면서 쿨타임 차면 사격
            if (attackTimer <= 0)
            {
                FireProjectile();
            }
        }
    }


    // 3. 고정 포대
    void HandleTurret()
    {
        float dist = Vector2.Distance(transform.position, player.position);
        if (dist <= so.aggroRadius)
        {
            if (attackTimer <= 0) FireProjectile();
        }
    }

    // 4. 돌격형 (부아악!)
    void HandleJuggernaut()
    {
        if (currentState == State.Dashing)
        {
            dashTimer -= Time.deltaTime;

            // 수동으로 직선 이동 (NavMesh 무시)
            transform.position += dashDirection * (so.dashSpeed * Time.deltaTime);

            // 벽에 박았는지 체크 (Raycast)
            bool hitWall = Physics2D.Raycast(transform.position, dashDirection, 0.5f, so.wallLayer);

            if (dashTimer <= 0 || hitWall)
            {
                currentState = State.Idle;
                agent.enabled = true; // 다시 NavMesh 켜기
                attackTimer = so.attackCooldown; // 박치기 후 딜레이
            }
            return;
        }

        // 평소엔 추격
        float dist = Vector2.Distance(transform.position, player.position);
        agent.SetDestination(player.position);

        // 돌진 사거리 진입 시
        if (dist <= so.dashTriggerRange && attackTimer <= 0)
        {
            currentState = State.Dashing;
            dashTimer = so.dashDuration;
            dashDirection = (player.position - transform.position).normalized;
            agent.enabled = false; // 돌진 중엔 NavMesh 끄고 직선으로 쏨
        }
    }

    // 5. 잠복형 (레포데 위치)
    void HandleAmbusher()
    {
        float dist = Vector2.Distance(transform.position, player.position);

        if (!isEnraged)
        {
            // 가만히 대기
            agent.isStopped = true;

            if (dist <= so.aggroRadius)
            {
                isEnraged = true;
                agent.isStopped = false;
                agent.speed = so.baseMoveSpeed * so.enrageSpeedMult; // 미친 속도로 파바바박!
            }
        }
        else
        {
            // 발작 추격
            agent.SetDestination(player.position);
            if (dist <= so.attackRange && attackTimer <= 0) TryAttack();
        }
    }

    // 6. 둥지 (스포너)
    void HandleNest()
    {
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= so.spawnInterval)
        {
            SpawnEnemy();
            spawnTimer -= so.spawnInterval;
        }
    }

    // --- 액션 함수들 ---
    void TryAttack()
    {
        Debug.Log($"{gameObject.name} 근접 타격! {so.damage} 피해!");
        attackTimer = so.attackCooldown;
    }

    void FireProjectile()
    {
        if (so.projectileData != null)
        {
            Vector2 dir = (player.position - transform.position).normalized;

            var p = projectilePooler.Get();
            p.transform.position = transform.position;

            // ★ 수정된 부분 ★
            // 1. so -> so.projectileData (적 데이터가 아니라 총알 데이터 전달!)
            // 2. target -> player (추적할 타겟은 플레이어!)
            // 3. mouse -> player.position (목표 좌표도 플레이어 위치!)
            p.Setup(so.projectileData, dir, projectilePooler, player, player.position);
        }
        attackTimer = so.attackCooldown;
    }


    // Enemy.cs 내부의 둥지 소환 함수만 이렇게 바꿔주세요!
    void SpawnEnemy()
    {
        if (so.spawnEnemySO != null && EnemyPooler.Instance != null)
        {
            // 위치와 "무슨 몹 소환할지(SO)"만 넘겨주면, 풀러가 플레이어까지 묶어서 출고해 줍니다!
            Vector3 spawnPos = transform.position + (Vector3)Random.insideUnitCircle * 1.5f;
            EnemyPooler.Instance.Get(spawnPos, so.spawnEnemySO);

            Debug.Log($"[{gameObject.name}] 둥지에서 {so.spawnEnemySO.name} 소환 완료!");
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (((1 << collision.gameObject.layer) & HitbulletLayer) != 0)
        {
            Projectile projectile = collision.GetComponent<Projectile>();

            if (projectile != null)
            {
                TakeDamage(projectile.DamageRead());
            }
        }
    }
}

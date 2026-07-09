using UnityEngine;

[RequireComponent(typeof(Collider2D))]

public class Projectile : MonoBehaviour
{
    [SerializeField] CircleCollider2D col;
    [SerializeField] private Transform visual;

    private ProjectileSO so;
    private Vector2 baseDir;
    private float timer;
    private ProjectilePooler pool;
    private ProjectileAnimator anim;

    private Vector2 velocity;
    private float sineTime;
    private bool dying;

    private Vector2 startPos;
    private Vector2 targetPos;
    private float travelTime;

    private Transform homingTarget;
    private int bounceCount;
    private bool hasTurned;
    private int currentPierce;

    [Header("BulletType")]
    [SerializeField] LayerMask HitLayer;

    void Awake()
    {
        anim = GetComponent<ProjectileAnimator>();
        visual.gameObject.SetActive(false);
    }

    public void Setup(
        ProjectileSO so,
        Vector2 dir,
        ProjectilePooler pool,
        Transform target = null,
        Vector2? explicitTargetPos = null
    )
    {
        hasTurned = false;
        this.so = so;
        this.pool = pool;
        this.homingTarget = target;
        currentPierce = so.basePierceCount;

        col.radius = so.colliderRadius;

        // ✅ 반드시 활성화 (풀에서 꺼낸게 inactive일 수 있음)
        gameObject.SetActive(true);

        // ✅ 상태 초기화
        timer = 0f;
        sineTime = 0f;
        dying = false;
        bounceCount = 0;
        col.enabled = true;

        startPos = transform.position;
        baseDir = (dir.sqrMagnitude < 0.0001f) ? Vector2.right : dir.normalized;

        //float distance = Vector2.Distance(startPos, targetPos);


        // ✅ 타겟 포지션 모드면 타겟Pos 먼저 확정
        if (so.useTargetPosition)
        {
            targetPos = explicitTargetPos ??
                        (target != null ? (Vector2)target.position : startPos + baseDir * 5f);

            float distance = Vector2.Distance(startPos, targetPos); // ✅ 여기서 계산
            travelTime = distance / Mathf.Max(0.01f, so.speed);
            travelTime = Mathf.Max(0.05f, travelTime);

            // shadow(visual) 초기화
            if (visual != null)
            {
                visual.gameObject.SetActive(true);      // Arc에서만 쓸거면 OK
                visual.localPosition = Vector3.zero;    // 지면 고정
                visual.localScale = Vector3.one;        // 시작 1배
            }

            if (so.telegraphPrefab != null)
            {
                var tele = Instantiate(so.telegraphPrefab, targetPos, Quaternion.identity);
                Destroy(tele, so.telegraphDuration);
            }
        }
        else
        {
            if (so.movement == MovementType.Arc)
            {
                velocity = baseDir * so.speed;
            }
        }

        // ✅ 비주얼 시작(원래 있던거 복구)
        if (anim != null && so.visual != null)
        {
            var v = so.visual;

            if (v.startSprites != null && v.startSprites.Length > 0)
            {
                anim.PlayOnce(v.startSprites, v.fps, () =>
                {
                    if (v.idleSprites != null && v.idleSprites.Length > 0)
                        anim.PlayLoop(v.idleSprites, v.fps);
                });
            }
            else if (v.idleSprites != null && v.idleSprites.Length > 0)
            {
                anim.PlayLoop(v.idleSprites, v.fps);
            }
        }

        if (so.movement == MovementType.Arc && so.useTargetPosition)
        {
            col.enabled = false; // 공중 충돌 없음
        }
    }

    void Update()
    {
        if (dying || so == null) return;

        if (!so.useTargetPosition)
        {
            timer += Time.deltaTime;

            if (timer >= so.lifetime)
            {
                Impact();
                return;
            }
        }

        switch (so.movement)
        {
            case MovementType.Straight:
                Move(baseDir, so.speed);
                break;

            case MovementType.Arc:

                if (so.useTargetPosition)
                {
                    timer += Time.deltaTime;

                    float rawT = timer / travelTime;
                    rawT = Mathf.Clamp01(rawT);

                    float t = Mathf.SmoothStep(0f, 1f, rawT);
                    t = Mathf.Clamp01(t);

                    // 1️⃣ 지면 위치 계산
                    Vector2 groundPos = Vector2.Lerp(startPos, targetPos, t);

                    // 2️⃣ 아크 높이 계산
                    float distance = Vector2.Distance(startPos, targetPos);
                    float dynamicHeight = distance * 0.25f; // 거리 비례
                    float height = dynamicHeight * Mathf.Sin(t * Mathf.PI);

                    // ⭐ 불릿 본체 = 아크 적용
                    transform.position = groundPos + Vector2.up * height;

                    // ⭐ 그림자 = 지면 직선 이동
                    if (visual != null)
                    {
                        visual.position = groundPos - new Vector2(0, .5f);

                        float heightFactor = Mathf.Sin(t * Mathf.PI);
                        float s = Mathf.Lerp(1f, 0.5f, heightFactor);
                        visual.localScale = new Vector3(s, s*.5f, 1f);
                    }

                    if (t >= 1f)
                    {
                        if (visual != null)
                        {
                            visual.localScale = new Vector3(1f, .5f);
                            visual.gameObject.SetActive(false);
                        }

                        transform.position = targetPos;
                        Impact();
                        return;
                    }
                }
                break;

            case MovementType.Sine:
                sineTime += Time.deltaTime;
                float a = Mathf.Sin(sineTime * so.sineFrequency * Mathf.PI * 2f) * so.sineAmplitudeDeg;
                Vector2 nd = Rotate(baseDir, a);
                Move(nd, so.speed);
                break;

            case MovementType.HomingHard:
                if (homingTarget != null)
                {
                    baseDir = ((Vector2)homingTarget.position - (Vector2)transform.position).normalized;

                    // 🎯 도달 체크
                    if (Vector2.Distance(transform.position, homingTarget.position) <= .1f)
                    {
                        Impact();
                        return;
                    }
                }
                Move(baseDir, so.speed);
                break;

            case MovementType.HomingSoft:
                if (homingTarget != null)
                {
                    Vector2 toTarget =
                        ((Vector2)homingTarget.position - (Vector2)transform.position).normalized;

                    baseDir = Vector2.Lerp(
                        baseDir,
                        toTarget,
                        so.homingTurnSpeed * Time.deltaTime
                    ).normalized;

                    float alignment = Vector2.Dot(baseDir, toTarget);
                    float speedMul = Mathf.Lerp(
                        so.homingSlowFactor,
                        1f,
                        alignment
                    );

                    Move(baseDir, so.speed * speedMul);
                }
                else
                {
                    Move(baseDir, so.speed);
                }
                break;

            case MovementType.DelayRush:
                if (timer < so.delayTime) return;
                Move(baseDir, so.speed);
                break;

            case MovementType.Bounce:
                Move(baseDir, so.speed);
                break;

            case MovementType.Boomerang:
                {
                    timer += Time.deltaTime;

                    float rt = so.boomerangReturnTime;
                    float slowDuration = 0.15f;
                    float accelDuration = 0.25f;

                    if (timer < rt)
                    {
                        Move(baseDir, so.speed);
                        break;
                    }

                    if (timer < rt + slowDuration)
                    {
                        float t = (timer - rt) / slowDuration;
                        float speedMul = Mathf.Lerp(1f, 0.05f, t);
                        Move(baseDir, so.speed * speedMul);
                        break;
                    }

                    // ⭐ 여기서 단 한번만 방향 변경
                    if (!hasTurned)
                    {
                        hasTurned = true;

                        if (homingTarget != null)
                        {
                            baseDir =
                                ((Vector2)homingTarget.position - (Vector2)transform.position).normalized;
                        }
                    }

                    if (timer < rt + slowDuration + accelDuration)
                    {
                        float t = (timer - (rt + slowDuration)) / accelDuration;
                        float speedMul = Mathf.Lerp(0.05f, 1f, t);
                        Move(baseDir, so.speed * speedMul);
                        break;
                    }

                    Move(baseDir, so.speed);

                    if (homingTarget != null &&
                        Vector2.Distance(transform.position, homingTarget.position) < 0.2f)
                    {
                        //Impact();
                        //return;
                    }

                    if(timer > so.lifetime)
                    {
                        Impact();
                    }

                    break;
                }
        }
    }

    void Move(Vector2 dir, float speed)
    {
        transform.position += (Vector3)(dir * speed * Time.deltaTime);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (dying) return; //

        // 1. 벽 레이어 체크 (벽은 관통 수치와 상관없이 충돌 처리)
        if (other.gameObject.layer == LayerMask.NameToLayer("Wall"))
        {
            if (so.movement == MovementType.Bounce && bounceCount < so.maxBounceCount) //
            {
                Vector2 normal = (transform.position - other.transform.position).normalized; //
                baseDir = Vector2.Reflect(baseDir, normal).normalized; //
                bounceCount++; //
                return; //
            }
            else
            {
                Impact(); // 벽에 부딪히면 무조건 소멸
                return; //
            }
        }

        // 2. 타겟 레이어(HitLayer) 체크
        if (((1 << other.gameObject.layer) & HitLayer) != 0)
        {
            string layerName = LayerMask.LayerToName(other.gameObject.layer); //

            switch (layerName)
            {
                case "Enemy":
                    if (other.TryGetComponent<Enemy>(out Enemy enemy))
                    {
                        enemy.TakeDamage(so.damage); //
                    }

                    // 관통 체크
                    if (currentPierce > 0)
                    {
                        currentPierce--; // 관통 횟수 차감
                                         // Impact()를 호출하지 않아 그대로 통과함
                    }
                    else
                    {
                        Impact(); // 관통력이 다하면 소멸
                    }
                    break;

                case "Player":
                    if (other.TryGetComponent<PlayerController>(out PlayerController player))
                    {
                        player.TakeDamage(so.damage); //
                    }
                    Impact(); // 플레이어 피격은 보통 즉시 소멸 처리
                    break;

                default:
                    break;
            }
        }
    }

    public void AddExtraPierce(int amount)
    {
        // 음수가 들어오지 않도록 방어하고 기존 값에 더함
        currentPierce += Mathf.Max(0, amount);
    }

    void Impact()
    {
        if (dying) return;

        dying = true;
        col.enabled = false;

        if (so.explosionRadius > 0)
        {
            Collider2D[] hits =
                Physics2D.OverlapCircleAll(transform.position, so.explosionRadius, so.damageLayer);

            foreach (var h in hits)
            {
                // 여기에 데미지 인터페이스 호출 가능
            }
        }

        if (so.impact == ImpactType.Split && so.splitChildSO != null)
        {
            for (int i = 0; i < so.splitCount; i++)
            {
                float angle = (-so.splitAngle * 0.5f) +
                              (so.splitAngle / (so.splitCount - 1)) * i;

                Vector2 newDir = Rotate(baseDir, angle);

                var p = pool.Get();
                p.transform.position = transform.position;
                p.Setup(so.splitChildSO, newDir, pool);
            }
        }

        // ★ 변경점: EffectSO의 Spawn 기능을 직접 호출! (총알의 크기도 스케일로 넘겨줌)
        if (so.impact == ImpactType.Explode || so.impact == ImpactType.ExplodeAndGround)
        {
            if (so.explosionEffectSO != null)
            {
                // 순서: 위치, 방향, 타겟, 스케일
                so.explosionEffectSO.Spawn(transform.position, baseDir, homingTarget, transform.localScale.x);
            }
        }

        if (so.impact == ImpactType.SpawnGround || so.impact == ImpactType.ExplodeAndGround)
        {
            if (so.groundEffectSO != null)
            {
                // 순서: 위치, 방향, 타겟, 스케일
                so.groundEffectSO.Spawn(transform.position, baseDir, homingTarget, transform.localScale.x);
            }
        }

        if (anim != null && so.visual != null &&
            so.visual.hitSprites != null &&
            so.visual.hitSprites.Length > 0)
        {
            anim.PlayOnce(so.visual.hitSprites, so.visual.fps, () =>
            {
                pool.Return(this);
            });
        }
        else
        {
            pool.Return(this);
        }
    }

    void SpawnEffect(GameObject prefab)
    {
        if (prefab == null) return;
        Instantiate(prefab, transform.position, Quaternion.identity);
    }

    static Vector2 Rotate(Vector2 v, float deg)
    {
        float rad = deg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(
            v.x * cos - v.y * sin,
            v.x * sin + v.y * cos
        ).normalized;
    }

    public int DamageRead()
    {
        return so.damage;
        Impact();
    }
}

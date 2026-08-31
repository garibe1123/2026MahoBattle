using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
public class Projectile : MonoBehaviour
{
    [SerializeField] private CircleCollider2D col;
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

    private GameObject damageSource;
    private float damageMultiplier = 1f;
    private float fanMissionModifier;

    [Header("BulletType")]
    [SerializeField] private LayerMask HitLayer;

    private void Awake()
    {
        if (col == null)
            col = GetComponent<CircleCollider2D>();

        anim = GetComponent<ProjectileAnimator>();
    }

    public void Setup(
        ProjectileSO projectileData,
        Vector2 dir,
        ProjectilePooler projectilePool,
        Transform target = null,
        Vector2? explicitTargetPos = null,
        GameObject source = null,
        float runtimeDamageMultiplier = 1f,
        float runtimeFanMissionModifier = 0f)
    {
        if (projectileData == null)
        {
            Debug.LogError("[Projectile] Setup failed: ProjectileSO is null.");
            if (projectilePool != null)
                projectilePool.Return(this);
            else
                gameObject.SetActive(false);
            return;
        }

        so = projectileData;
        pool = projectilePool;
        homingTarget = target;
        damageSource = source;
        damageMultiplier = Mathf.Max(0f, runtimeDamageMultiplier);
        fanMissionModifier = runtimeFanMissionModifier;

        hasTurned = false;
        currentPierce = Mathf.Max(0, so.basePierceCount);
        bounceCount = 0;
        timer = 0f;
        sineTime = 0f;
        dying = false;
        velocity = Vector2.zero;

        if (col == null)
            col = GetComponent<CircleCollider2D>();

        if (col == null)
        {
            Debug.LogError("[Projectile] CircleCollider2D is missing.");
            gameObject.SetActive(false);
            return;
        }

        col.radius = Mathf.Max(0.001f, so.colliderRadius);
        col.enabled = true;
        gameObject.SetActive(true);

        if (visual != null)
        {
            visual.gameObject.SetActive(true);
            visual.localPosition = Vector3.zero;
            visual.localScale = Vector3.one;
        }

        startPos = transform.position;
        baseDir = dir.sqrMagnitude < 0.0001f ? Vector2.right : dir.normalized;

        if (so.useTargetPosition)
        {
            targetPos = explicitTargetPos ??
                        (target != null ? (Vector2)target.position : startPos + baseDir * 5f);

            float distance = Vector2.Distance(startPos, targetPos);
            travelTime = Mathf.Max(0.05f, distance / Mathf.Max(0.01f, so.speed));

            if (so.telegraphPrefab != null)
            {
                GameObject telegraph = Instantiate(so.telegraphPrefab, targetPos, Quaternion.identity);
                Destroy(telegraph, Mathf.Max(0f, so.telegraphDuration));
            }
        }
        else if (so.movement == MovementType.Arc)
        {
            velocity = baseDir * so.speed;
        }

        StartVisual();

        if (so.movement == MovementType.Arc && so.useTargetPosition)
            col.enabled = false;
    }

    private void StartVisual()
    {
        if (anim == null || so == null || so.visual == null) return;

        ProjectileVisualSO v = so.visual;
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

    private void Update()
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
                UpdateArc();
                break;

            case MovementType.Sine:
                sineTime += Time.deltaTime;
                float angle = Mathf.Sin(sineTime * so.sineFrequency * Mathf.PI * 2f) * so.sineAmplitudeDeg;
                Move(Rotate(baseDir, angle), so.speed);
                break;

            case MovementType.HomingHard:
                UpdateHardHoming();
                break;

            case MovementType.HomingSoft:
                UpdateSoftHoming();
                break;

            case MovementType.DelayRush:
                if (timer >= so.delayTime)
                    Move(baseDir, so.speed);
                break;

            case MovementType.Bounce:
                Move(baseDir, so.speed);
                break;

            case MovementType.Boomerang:
                UpdateBoomerang();
                break;
        }
    }

    private void UpdateArc()
    {
        if (!so.useTargetPosition) return;

        timer += Time.deltaTime;
        float rawT = Mathf.Clamp01(timer / travelTime);
        float t = Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, rawT));

        Vector2 groundPos = Vector2.Lerp(startPos, targetPos, t);
        float distance = Vector2.Distance(startPos, targetPos);
        float height = distance * 0.25f * Mathf.Sin(t * Mathf.PI);

        transform.position = groundPos + Vector2.up * height;

        if (visual != null)
        {
            visual.position = groundPos - new Vector2(0f, 0.5f);
            float heightFactor = Mathf.Sin(t * Mathf.PI);
            float scale = Mathf.Lerp(1f, 0.5f, heightFactor);
            visual.localScale = new Vector3(scale, scale * 0.5f, 1f);
        }

        if (t < 1f) return;

        if (visual != null)
        {
            visual.localScale = new Vector3(1f, 0.5f, 1f);
            visual.gameObject.SetActive(false);
        }

        transform.position = targetPos;
        Impact();
    }

    private void UpdateHardHoming()
    {
        if (homingTarget != null)
        {
            Vector2 toTarget = (Vector2)homingTarget.position - (Vector2)transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
                baseDir = toTarget.normalized;

            if (Vector2.Distance(transform.position, homingTarget.position) <= 0.1f)
            {
                Impact();
                return;
            }
        }

        Move(baseDir, so.speed);
    }

    private void UpdateSoftHoming()
    {
        if (homingTarget == null)
        {
            Move(baseDir, so.speed);
            return;
        }

        Vector2 toTarget = ((Vector2)homingTarget.position - (Vector2)transform.position).normalized;
        if (toTarget.sqrMagnitude > 0.0001f)
            baseDir = Vector2.Lerp(baseDir, toTarget, so.homingTurnSpeed * Time.deltaTime).normalized;

        float alignment = Vector2.Dot(baseDir, toTarget);
        float speedMultiplier = Mathf.Lerp(so.homingSlowFactor, 1f, alignment);
        Move(baseDir, so.speed * speedMultiplier);
    }

    private void UpdateBoomerang()
    {
        // timer는 Update() 공통 경로에서 이미 증가합니다.
        float returnTime = so.boomerangReturnTime;
        const float slowDuration = 0.15f;
        const float accelDuration = 0.25f;

        if (timer < returnTime)
        {
            Move(baseDir, so.speed);
            return;
        }

        if (timer < returnTime + slowDuration)
        {
            float t = (timer - returnTime) / slowDuration;
            Move(baseDir, so.speed * Mathf.Lerp(1f, 0.05f, t));
            return;
        }

        if (!hasTurned)
        {
            hasTurned = true;
            if (homingTarget != null)
            {
                Vector2 returnDir = (Vector2)homingTarget.position - (Vector2)transform.position;
                if (returnDir.sqrMagnitude > 0.0001f)
                    baseDir = returnDir.normalized;
            }
        }

        if (timer < returnTime + slowDuration + accelDuration)
        {
            float t = (timer - returnTime - slowDuration) / accelDuration;
            Move(baseDir, so.speed * Mathf.Lerp(0.05f, 1f, t));
            return;
        }

        Move(baseDir, so.speed);
    }

    private void Move(Vector2 dir, float speed)
    {
        transform.position += (Vector3)(dir * speed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (dying || so == null || other == null) return;

        if (other.gameObject.layer == LayerMask.NameToLayer("Wall"))
        {
            BattleObstacle obstacle = other.GetComponentInParent<BattleObstacle>();

            // LowWall 및 아직 활성화되지 않은 ConditionalWall은 이동만 막고 투사체는 통과합니다.
            if (obstacle != null && !obstacle.BlocksProjectiles)
                return;

            CombatDamage.TryApply(other, BuildDamageContext(DamageKind.Projectile));

            if (so.movement == MovementType.Bounce && bounceCount < so.maxBounceCount)
            {
                Vector2 normal = ((Vector2)transform.position - (Vector2)other.transform.position).normalized;
                if (normal.sqrMagnitude <= 0.001f)
                    normal = -baseDir;

                baseDir = Vector2.Reflect(baseDir, normal).normalized;
                bounceCount++;
                return;
            }

            Impact();
            return;
        }

        if (((1 << other.gameObject.layer) & HitLayer) == 0)
            return;

        DamageContext hitContext = BuildDamageContext(DamageKind.Projectile);
        bool applied = CombatDamage.TryApply(other, hitContext);

        if (!applied && other.TryGetComponent<Enemy>(out Enemy legacyEnemy))
        {
            legacyEnemy.TakeDamage(CombatDamage.Calculate(hitContext, 0f));
            applied = true;
        }

        if (!applied && other.TryGetComponent<PlayerController>(out PlayerController legacyPlayer))
        {
            legacyPlayer.TakeDamage(CombatDamage.Calculate(hitContext, legacyPlayer.Defense));
            applied = true;
        }

        if (!applied)
            return;

        string layerName = LayerMask.LayerToName(other.gameObject.layer);
        if (layerName == "Enemy" && currentPierce > 0)
        {
            currentPierce--;
            return;
        }

        Impact();
    }

    public void AddExtraPierce(int amount)
    {
        currentPierce += Mathf.Max(0, amount);
    }

    private void Impact()
    {
        if (dying || so == null) return;

        dying = true;
        if (col != null)
            col.enabled = false;

        ResolveExplosionDamage();
        ResolveSplit();
        SpawnImpactEffects();
        ReturnAfterHitAnimation();
    }

    private void ResolveExplosionDamage()
    {
        bool explosiveImpact = so.impact == ImpactType.Explode || so.impact == ImpactType.ExplodeAndGround;
        if (!explosiveImpact || so.explosionRadius <= 0f)
            return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(
            transform.position,
            so.explosionRadius,
            so.damageLayer);

        HashSet<IDamageable> damagedTargets = new();
        HashSet<Enemy> damagedLegacyEnemies = new();
        DamageContext context = BuildDamageContext(DamageKind.Area);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null) continue;

            if (CombatDamage.TryFindDamageable(hit.transform, out IDamageable damageable))
            {
                if (!damageable.IsAlive || !damagedTargets.Add(damageable))
                    continue;

                float finalDamage = CombatDamage.Calculate(context, damageable.Defense);
                damageable.ReceiveDamage(context, finalDamage);
                continue;
            }

            Enemy legacyEnemy = hit.GetComponentInParent<Enemy>();
            if (legacyEnemy != null && damagedLegacyEnemies.Add(legacyEnemy))
                legacyEnemy.TakeDamage(CombatDamage.Calculate(context, 0f));
        }
    }

    private void ResolveSplit()
    {
        if (so.impact != ImpactType.Split || so.splitChildSO == null || pool == null)
            return;

        int splitCount = Mathf.Max(1, so.splitCount);
        for (int i = 0; i < splitCount; i++)
        {
            float angle = splitCount == 1
                ? 0f
                : (-so.splitAngle * 0.5f) + (so.splitAngle / (splitCount - 1)) * i;

            Vector2 newDir = Rotate(baseDir, angle);
            Projectile child = pool.Get();
            if (child == null) continue;

            child.transform.position = transform.position;
            child.Setup(
                so.splitChildSO,
                newDir,
                pool,
                null,
                null,
                damageSource,
                damageMultiplier,
                fanMissionModifier);
        }
    }

    private void SpawnImpactEffects()
    {
        if ((so.impact == ImpactType.Explode || so.impact == ImpactType.ExplodeAndGround) &&
            so.explosionEffectSO != null)
        {
            so.explosionEffectSO.Spawn(
                transform.position,
                baseDir,
                homingTarget,
                transform.localScale.x);
        }

        if ((so.impact == ImpactType.SpawnGround || so.impact == ImpactType.ExplodeAndGround) &&
            so.groundEffectSO != null)
        {
            so.groundEffectSO.Spawn(
                transform.position,
                baseDir,
                homingTarget,
                transform.localScale.x);
        }
    }

    private void ReturnAfterHitAnimation()
    {
        if (pool == null)
        {
            gameObject.SetActive(false);
            return;
        }

        if (anim != null && so.visual != null &&
            so.visual.hitSprites != null && so.visual.hitSprites.Length > 0)
        {
            anim.PlayOnce(so.visual.hitSprites, so.visual.fps, () => pool.Return(this));
        }
        else
        {
            pool.Return(this);
        }
    }

    private DamageContext BuildDamageContext(DamageKind kind)
    {
        return new DamageContext(
            damageSource != null ? damageSource : gameObject,
            transform.position,
            so != null ? so.damage : 0f,
            damageMultiplier,
            fanMissionModifier,
            kind);
    }

    private static Vector2 Rotate(Vector2 v, float deg)
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
        return so != null ? so.damage : 0;
    }
}

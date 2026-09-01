using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(EffectAnimator))]
[RequireComponent(typeof(Collider2D))]
public class Effect : MonoBehaviour
{
    private EffectSO so;
    private EffectAnimator anim;
    private Collider2D myCollider;
    private LineRenderer lr;
    private ContactFilter2D contactFilter;
    private Transform attachTarget;

    private float lifeTimer;
    private float tickTimer;
    private float spawnTimer;
    private float mineDelayTimer;

    private static readonly List<Effect> activeMines = new();
    private bool mineTriggered;
    private bool isEnding;

    private Collider2D[] hitResults;
    private Transform lookTarget;
    private float scaleMultiplier = 1f;
    private float currentLaserLength;

    private GameObject damageSource;
    private float runtimeDamageMultiplier = 1f;
    private float runtimeFanMissionModifier;

    private void Awake()
    {
        anim = GetComponent<EffectAnimator>();
        myCollider = GetComponent<Collider2D>();
        myCollider.isTrigger = true;

        lr = GetComponent<LineRenderer>();
        if (lr != null)
            lr.enabled = false;
    }

    public void Setup(
        EffectSO effectSO,
        Transform target = null,
        float scaleMultiplier = 1f,
        GameObject damageSource = null,
        float runtimeDamageMultiplier = 1f,
        float runtimeFanMissionModifier = 0f)
    {
        if (effectSO == null)
        {
            Debug.LogError($"[Effect] Setup failed on '{name}': EffectSO is null.");
            Destroy(gameObject);
            return;
        }

        so = effectSO;
        attachTarget = so.isAttached ? target : null;
        this.scaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);
        this.damageSource = damageSource;
        this.runtimeDamageMultiplier = Mathf.Max(0f, runtimeDamageMultiplier);
        this.runtimeFanMissionModifier = runtimeFanMissionModifier;

        lifeTimer = 0f;
        tickTimer = 0f;
        spawnTimer = 0f;
        mineDelayTimer = 0f;
        mineTriggered = false;
        lookTarget = target;
        currentLaserLength = 0f;
        isEnding = false;

        if (lookTarget != null)
            FaceTarget(lookTarget.position);

        if (so.visual != null)
            anim.SetupVisual(so.visual);

        contactFilter.useTriggers = true;
        contactFilter.SetLayerMask(so.targetLayer);
        contactFilter.useLayerMask = true;

        hitResults = new Collider2D[Mathf.Max(1, so.maxTargetsPerTick)];
        transform.localScale = Vector3.one * (so.startScale * this.scaleMultiplier);

        if (so.effectType == EffectTypeEnum.Mine && !activeMines.Contains(this))
            activeMines.Add(this);

        if (so.visual != null && so.visual.startSprites != null && so.visual.startSprites.Length > 0)
        {
            anim.PlayOnce(AnimPhase.Start, so.visual.startSprites, so.visual.fps, () =>
            {
                if (so == null || so.visual == null) return;
                if (so.visual.idleSprites != null && so.visual.idleSprites.Length > 0)
                    anim.PlayLoop(AnimPhase.Idle, so.visual.idleSprites, so.visual.fps);
            });
        }
        else if (so.visual != null && so.visual.idleSprites != null && so.visual.idleSprites.Length > 0)
        {
            anim.PlayLoop(AnimPhase.Idle, so.visual.idleSprites, so.visual.fps);
        }
    }

    private void Update()
    {
        if (so == null || isEnding)
            return;

        HandleLifeAndModifiers();
        if (isEnding) return;

        HandleMovement();
        HandleRotation();
        HandleScale();

        if (so.effectType == EffectTypeEnum.Laser)
        {
            HandleLaserDamage();
            return;
        }

        if (myCollider == null || !myCollider.enabled)
            return;

        switch (so.effectType)
        {
            case EffectTypeEnum.Zone:
                HandleTickDamage();
                break;

            case EffectTypeEnum.Mine:
                HandleMineLogic();
                break;

            case EffectTypeEnum.Spawner:
                HandleSpawnerLogic();
                break;
        }
    }

    private void HandleLifeAndModifiers()
    {
        if (so.isAttached && attachTarget != null)
            transform.position = attachTarget.position;

        if (mineTriggered)
            return;

        lifeTimer += Time.deltaTime;
        if (lifeTimer >= Mathf.Max(0f, so.duration))
            EndEffect();
    }

    private void HandleMovement()
    {
        if (so.movementType == EffectMovementType.MoveForward)
            transform.position += transform.right * (so.moveSpeed * Time.deltaTime);
    }

    private void HandleRotation()
    {
        if (so.rotationType == EffectRotationType.ContinuousSpin)
        {
            transform.Rotate(0f, 0f, so.spinSpeed * Time.deltaTime);
            return;
        }

        if (so.rotationType != EffectRotationType.LookAtTarget)
            return;

        if (lookTarget == null || !lookTarget.gameObject.activeInHierarchy)
            FindNearestTarget();

        if (lookTarget == null)
            return;

        Vector3 dir = lookTarget.position - transform.position;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.AngleAxis(angle, Vector3.forward),
            so.spinSpeed * Time.deltaTime);
    }

    private void FaceTarget(Vector3 worldPosition)
    {
        Vector3 dir = worldPosition - transform.position;
        if (dir.sqrMagnitude <= 0.0001f)
            return;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
    }

    private void FindNearestTarget()
    {
        float searchRadius = so.effectType == EffectTypeEnum.Laser
            ? 50f
            : Mathf.Max(0.1f, so.radius * 3f);

        int hits = Physics2D.OverlapCircleNonAlloc(
            transform.position,
            searchRadius,
            hitResults,
            so.targetLayer);

        float minDistance = float.MaxValue;
        Transform nearest = null;

        for (int i = 0; i < hits; i++)
        {
            Collider2D hit = hitResults[i];
            if (hit == null) continue;

            float dist = Vector2.SqrMagnitude((Vector2)transform.position - (Vector2)hit.transform.position);
            if (dist >= minDistance) continue;

            minDistance = dist;
            nearest = hit.transform;
        }

        lookTarget = nearest;
    }

    private void HandleScale()
    {
        if (so.scaleType == EffectScaleType.Fixed)
            return;

        float duration = Mathf.Max(0.0001f, so.duration);
        float t = Mathf.Clamp01(lifeTimer / duration);
        float s = Mathf.Lerp(so.startScale, so.targetScale, t);
        transform.localScale = Vector3.one * (s * scaleMultiplier);
    }

    private void HandleTickDamage()
    {
        tickTimer += Time.deltaTime;
        float interval = Mathf.Max(0.01f, so.tickRate);
        if (tickTimer < interval)
            return;

        int hits = myCollider.Overlap(contactFilter, hitResults);
        ApplyDamageToHits(hits, DamageKind.Area);
        tickTimer %= interval;
    }

    private void HandleLaserDamage()
    {
        Vector2 origin = transform.position;
        Vector2 dir = transform.right;

        float targetMaxLength = Mathf.Max(0f, so.laserSize.x * transform.localScale.x);
        RaycastHit2D blockHit = Physics2D.Raycast(origin, dir, targetMaxLength, so.blockingLayer);
        if (blockHit.collider != null)
            targetMaxLength = blockHit.distance;

        if (anim.currentPhase == AnimPhase.Start && anim.currentFrameIndex == 0)
        {
            currentLaserLength = 0f;
        }
        else if (so.laserExtensionSpeed <= 0f)
        {
            currentLaserLength = targetMaxLength;
        }
        else
        {
            currentLaserLength = Mathf.MoveTowards(
                currentLaserLength,
                targetMaxLength,
                so.laserExtensionSpeed * Time.deltaTime);
        }

        if (lr != null)
        {
            lr.enabled = currentLaserLength > 0.1f;
            if (lr.enabled)
            {
                Vector2 endPos = origin + dir * currentLaserLength;
                lr.useWorldSpace = true;
                lr.SetPosition(0, origin);
                lr.SetPosition(1, endPos);
            }
        }

        if (currentLaserLength <= 0.1f)
            return;

        tickTimer += Time.deltaTime;
        float interval = Mathf.Max(0.01f, so.tickRate);
        if (tickTimer < interval)
            return;

        Vector2 centerPos = origin + dir * (currentLaserLength * 0.5f);
        Vector2 currentSize = new(
            currentLaserLength,
            Mathf.Max(0.01f, so.laserSize.y * transform.localScale.y));

        int hits = Physics2D.OverlapBoxNonAlloc(
            centerPos,
            currentSize,
            transform.eulerAngles.z,
            hitResults,
            so.targetLayer);

        ApplyDamageToHits(hits, DamageKind.Area);
        tickTimer %= interval;
    }

    private void HandleMineLogic()
    {
        if (!mineTriggered)
        {
            int hits = myCollider.Overlap(contactFilter, hitResults);
            if (hits > 0)
                TriggerMine();
            return;
        }

        mineDelayTimer += Time.deltaTime;
        if (mineDelayTimer >= Mathf.Max(0f, so.mineDelay))
            ExplodeMine();
    }

    public void TriggerMine()
    {
        if (mineTriggered || isEnding)
            return;

        mineTriggered = true;
        mineDelayTimer = 0f;
    }

    private void ExplodeMine()
    {
        float currentRadius = Mathf.Max(0f, so.mineExplosionRadius * transform.localScale.x);
        int hits = Physics2D.OverlapCircleNonAlloc(
            transform.position,
            currentRadius,
            hitResults,
            so.targetLayer);

        ApplyDamageToHits(hits, DamageKind.Area);

        if (so.chainReaction)
        {
            // TriggerMine/EndEffect가 activeMines를 수정할 수 있으므로 snapshot을 사용합니다.
            Effect[] snapshot = activeMines.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                Effect mine = snapshot[i];
                if (mine == null || mine == this || mine.mineTriggered)
                    continue;

                if (Vector2.Distance(transform.position, mine.transform.position) <= currentRadius)
                    mine.TriggerMine();
            }
        }

        EndEffect();
    }

    private void HandleSpawnerLogic()
    {
        HandleTickDamage();

        spawnTimer += Time.deltaTime;
        float interval = Mathf.Max(0.01f, so.spawnInterval);
        if (spawnTimer < interval)
            return;

        if (so.spawnPrefab != null)
            Instantiate(so.spawnPrefab, transform.position, transform.rotation);

        spawnTimer %= interval;
    }

    private void ApplyDamageToHits(int hitCount, DamageKind kind)
    {
        if (so == null || so.damage <= 0f || hitCount <= 0)
            return;

        DamageContext context = new(
            damageSource != null ? damageSource : gameObject,
            transform.position,
            so.damage,
            runtimeDamageMultiplier,
            runtimeFanMissionModifier,
            kind);

        HashSet<IDamageable> damagedTargets = new();
        HashSet<Enemy> damagedLegacyEnemies = new();

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D hit = hitResults[i];
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

    private void EndEffect()
    {
        if (isEnding)
            return;

        isEnding = true;
        activeMines.Remove(this);

        if (lr != null)
            lr.enabled = false;

        if (so != null && so.visual != null &&
            so.visual.endSprites != null && so.visual.endSprites.Length > 0)
        {
            anim.PlayOnce(
                AnimPhase.End,
                so.visual.endSprites,
                so.visual.fps,
                () => Destroy(gameObject));
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        activeMines.Remove(this);
    }

    private void OnDrawGizmosSelected()
    {
        if (so == null || so.effectType != EffectTypeEnum.Mine)
            return;

        float currentScaleX = Application.isPlaying ? transform.localScale.x : so.startScale;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, so.mineExplosionRadius * currentScaleX);
    }
}

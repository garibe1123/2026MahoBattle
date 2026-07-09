using UnityEngine;
using System.Collections.Generic;

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

    private static List<Effect> activeMines = new List<Effect>();
    private bool mineTriggered = false;
    private float mineDelayTimer;

    private Collider2D[] hitResults;
    private Transform lookTarget;
    private float scaleMultiplier = 1f;

    // ★ 레이저의 현재 길이를 저장하는 변수
    private float currentLaserLength = 0f;

    private bool isEnding = false;

    void Awake()
    {
        anim = GetComponent<EffectAnimator>();
        myCollider = GetComponent<Collider2D>();
        myCollider.isTrigger = true;

        lr = GetComponent<LineRenderer>();
        if (lr != null) lr.enabled = false;
    }

    public void Setup(EffectSO effectSO, Transform target = null, float scaleMultiplier = 1f)
    {
        so = effectSO;
        attachTarget = so.isAttached ? target : null;
        this.scaleMultiplier = scaleMultiplier;

        lifeTimer = 0f;
        tickTimer = 0f;
        spawnTimer = 0f;
        mineTriggered = false;
        lookTarget = null;
        currentLaserLength = 0f; // 레이저 길이 초기화
        isEnding = false; // ★ 추가: 새로 태어날 때 다시 false로 초기화

        // ★ 1. 총알이 넘겨준 타겟을 내 목표물(lookTarget)로 저장!
        lookTarget = target;

        // ★ 2. 만약 지정된 타겟이 있다면, 태어나자마자 그쪽을 바라보도록 멱살을 잡고 돌립니다!
        if (lookTarget != null)
        {
            Vector3 dir = lookTarget.position - transform.position;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }

        // 애니메이터에 VisualSO(머티리얼 데이터) 넘겨주기
        if (so.visual != null) anim.SetupVisual(so.visual);

        contactFilter.useTriggers = true;
        contactFilter.SetLayerMask(so.targetLayer);
        contactFilter.useLayerMask = true;

        hitResults = new Collider2D[Mathf.Max(1, so.maxTargetsPerTick)];
        transform.localScale = Vector3.one * (so.startScale * scaleMultiplier);

        if (so.effectType == EffectTypeEnum.Mine)
            activeMines.Add(this);

        if (so.visual != null && so.visual.startSprites != null && so.visual.startSprites.Length > 0)
        {
            anim.PlayOnce(AnimPhase.Start, so.visual.startSprites, so.visual.fps, () =>
            {
                if (so.visual.idleSprites != null && so.visual.idleSprites.Length > 0)
                    anim.PlayLoop(AnimPhase.Idle, so.visual.idleSprites, so.visual.fps);
            });
        }
        else if (so.visual != null && so.visual.idleSprites != null && so.visual.idleSprites.Length > 0)
        {
            anim.PlayLoop(AnimPhase.Idle, so.visual.idleSprites, so.visual.fps);
        }
    }

    void Update()
    {
        if (so == null) return;

        HandleLifeAndModifiers();
        HandleMovement();
        HandleRotation();
        HandleScale();

        // 레이저는 콜라이더(myCollider) 여부와 상관없이 독자적인 수학 연산을 하므로 무조건 돌림
        if (so.effectType == EffectTypeEnum.Laser)
        {
            HandleLaserDamage();
        }
        else if (myCollider != null && myCollider.enabled)
        {
            // 장판, 지뢰, 스포너는 애니메이터가 켜준 콜라이더 기반으로 동작
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
    }

    void HandleLifeAndModifiers()
    {
        if (so.isAttached && attachTarget != null)
        {
            transform.position = attachTarget.position;
        }

        // ★ 수정: isEnding이 아닐 때만 수명 타이머 굴러가게 변경
        if (!mineTriggered && !isEnding)
        {
            lifeTimer += Time.deltaTime;
            if (lifeTimer >= so.duration)
            {
                EndEffect();
            }
        }
    }

    void HandleMovement()
    {
        if (so.movementType == EffectMovementType.MoveForward)
            transform.position += transform.right * (so.moveSpeed * Time.deltaTime);
    }

    void HandleRotation()
    {
        if (so.rotationType == EffectRotationType.ContinuousSpin)
            transform.Rotate(0f, 0f, so.spinSpeed * Time.deltaTime);
        else if (so.rotationType == EffectRotationType.LookAtTarget)
        {
            if (lookTarget == null || !lookTarget.gameObject.activeInHierarchy) FindNearestTarget();
            if (lookTarget != null)
            {
                Vector3 dir = lookTarget.position - transform.position;
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.AngleAxis(angle, Vector3.forward), so.spinSpeed * Time.deltaTime);
            }
        }
    }

    void FindNearestTarget()
    {
        // ★ 수정: 레이저면 탐색 반경을 크게(50f) 주고, 아니면 radius의 3배로 줍니다.
        float searchRadius = (so.effectType == EffectTypeEnum.Laser) ? 50f : so.radius * 3f;

        int hits = Physics2D.OverlapCircleNonAlloc(transform.position, searchRadius, hitResults, so.targetLayer);
        float minDistance = float.MaxValue;

        for (int i = 0; i < hits; i++)
        {
            float dist = Vector2.Distance(transform.position, hitResults[i].transform.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                lookTarget = hitResults[i].transform;
            }
        }
    }

    void HandleScale()
    {
        if (so.scaleType == EffectScaleType.Fixed) return;
        float t = Mathf.Clamp01(lifeTimer / so.duration);
        float s = Mathf.Lerp(so.startScale, so.targetScale, t);
        transform.localScale = Vector3.one * (s * scaleMultiplier);
    }

    void HandleTickDamage()
    {
        tickTimer += Time.deltaTime;
        if (tickTimer >= so.tickRate)
        {
            int hits = myCollider.Overlap(contactFilter, hitResults);
            ApplyDamageToHits(hits);
            tickTimer -= so.tickRate;
        }
    }

    void HandleLaserDamage()
    {
        Vector2 origin = transform.position;
        Vector2 dir = transform.right;

        // 1. 목표(Target) 도달 길이 계산 (벽에 막히는지 검사)
        float targetMaxLength = so.laserSize.x * transform.localScale.x;
        RaycastHit2D blockHit = Physics2D.Raycast(origin, dir, targetMaxLength, so.blockingLayer);

        if (blockHit.collider != null)
        {
            targetMaxLength = blockHit.distance; // 벽에 막히면 거기가 최대 길이
        }

        // ★ 2. 애니메이션 상태에 따른 레이저 뻗어나감 처리 (유저님 아이디어 적용!)
        // Start 상태이면서 첫 번째 그림(인덱스 0)일 때만 레이저 숨김
        if (anim.currentPhase == AnimPhase.Start && anim.currentFrameIndex == 0)
        {
            currentLaserLength = 0f; // 1프레임(예열) 중에는 뻗지 않음
        }
        else
        {
            // Start의 2프레임(인덱스 1)부터이거나, Idle, End, 혹은 애니메이션이 없는 경우 무조건 발사!
            if (so.laserExtensionSpeed <= 0f)
            {
                currentLaserLength = targetMaxLength; // 즉시 끝까지 도달
            }
            else
            {
                // 서서히 자라남
                currentLaserLength = Mathf.MoveTowards(currentLaserLength, targetMaxLength, so.laserExtensionSpeed * Time.deltaTime);
            }
        }

        // 3. LineRenderer에 점 찍기
        if (lr != null)
        {
            if (!lr.enabled && currentLaserLength > 0.1f) lr.enabled = true;
            else if (currentLaserLength <= 0.1f) lr.enabled = false;

            if (lr.enabled)
            {
                Vector2 endPos = origin + dir * currentLaserLength;
                lr.useWorldSpace = true;
                lr.SetPosition(0, origin);
                lr.SetPosition(1, endPos);
            }
        }

        // 4. 데미지 판정 (길이가 조금이라도 자라났을 때만)
        if (currentLaserLength > 0.1f)
        {
            tickTimer += Time.deltaTime;
            if (tickTimer >= so.tickRate)
            {
                Vector2 centerPos = origin + dir * (currentLaserLength * 0.5f);
                Vector2 currentSize = new Vector2(currentLaserLength, so.laserSize.y * transform.localScale.y);

                int hits = Physics2D.OverlapBoxNonAlloc(centerPos, currentSize, transform.eulerAngles.z, hitResults, so.targetLayer);
                ApplyDamageToHits(hits);
                tickTimer -= so.tickRate;
            }
        }
    }

    void HandleMineLogic()
    {
        if (!mineTriggered)
        {
            int hits = myCollider.Overlap(contactFilter, hitResults);
            if (hits > 0) TriggerMine();
        }
        else
        {
            mineDelayTimer += Time.deltaTime;
            if (mineDelayTimer >= so.mineDelay) ExplodeMine();
        }
    }

    public void TriggerMine()
    {
        if (mineTriggered) return;
        mineTriggered = true;
        mineDelayTimer = 0f;
    }

    void ExplodeMine()
    {
        float currentExpRadius = so.mineExplosionRadius * transform.localScale.x;
        int hits = Physics2D.OverlapCircleNonAlloc(transform.position, currentExpRadius, hitResults, so.targetLayer);
        ApplyDamageToHits(hits);

        if (so.chainReaction)
        {
            foreach (var mine in activeMines)
            {
                if (mine != this && !mine.mineTriggered)
                {
                    if (Vector2.Distance(transform.position, mine.transform.position) <= currentExpRadius)
                        mine.TriggerMine();
                }
            }
        }
        EndEffect();
    }

    void HandleSpawnerLogic()
    {
        HandleTickDamage();
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= so.spawnInterval)
        {
            if (so.spawnPrefab != null) Instantiate(so.spawnPrefab, transform.position, transform.rotation);
            spawnTimer -= so.spawnInterval;
        }
    }

    void ApplyDamageToHits(int hitCount)
    {
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D enemy = hitResults[i];
            if (so.damage > 0) Debug.Log($"[{so.effectType}] hit: {enemy.name} / Dmg: {so.damage}");
        }
    }

    void EndEffect()
    {
        // ★ 수정: 이미 끝나는 중이면 두 번 실행 안 되게 컷!
        if (isEnding) return;
        isEnding = true;

        if (so != null && so.effectType == EffectTypeEnum.Mine)
        {
            activeMines.Remove(this);
        }

        if (so.visual != null && so.visual.endSprites != null && so.visual.endSprites.Length > 0)
        {
            anim.PlayOnce(AnimPhase.End, so.visual.endSprites, so.visual.fps, () =>
            {
                Destroy(gameObject);
            });
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        if (so != null && so.effectType == EffectTypeEnum.Mine) activeMines.Remove(this);
    }

    void OnDrawGizmosSelected()
    {
        if (so == null) return;
        if (so.effectType == EffectTypeEnum.Mine)
        {
            float currentScaleX = Application.isPlaying ? transform.localScale.x : so.startScale;
            Gizmos.color = new Color(1, 0.5f, 0, 0.3f);
            Gizmos.DrawWireSphere(transform.position, so.mineExplosionRadius * currentScaleX);
        }
    }
}
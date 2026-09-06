using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 멀리 있는 Monster의 NavMesh/A* 비용을 줄이는 Crowd LOD Agent입니다.
///
/// Base 외부 Spawn으로 Simple Entry가 시작된 Monster는 즉시 Player에게 돌진하지 않습니다.
/// 먼저 Spawn Bay에서 Idle로 대기하고, Player가 가까이 접근했을 때만 단순 이동을 시작합니다.
/// 이후 NavMesh에 붙을 수 있는 지점에 도달하면 MonsterController의 정밀 AI로 전환합니다.
///
/// Simple Movement도 wallLayer를 CircleCast해서 Room 벽을 관통하지 않습니다.
/// MonsterController보다 뒤에 Update해 4방향 Facing Debug 표시도 최종 이동 방향 기준으로 유지합니다.
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class MonsterCrowdAgent : MonoBehaviour
{
    [Header("Outside Spawn Staging")]
    [Tooltip("Base 밖 Simple Entry Monster가 Spawn 직후 중앙으로 달려오지 않고 Player가 가까워질 때까지 대기합니다.")]
    [SerializeField] private bool holdSimpleEntryUntilPlayerNear = true;
    [SerializeField, Min(0.5f)] private float simpleEntryWakeDistance = 3.4f;
    [SerializeField, Min(0f)] private float simpleCollisionPadding = 0.05f;

    private MonsterPool owner;
    private MonsterController controller;
    private NavMeshAgent navAgent;
    private EnemyAnimator spriteAnimator;
    private Collider2D bodyCollider;
    private Transform target;

    private bool simpleMovement;
    private bool forcedSimpleEntry;
    private bool waitingForActivation;
    private float nextNavAttachCheck;
    private float failedAttachElapsed;
    private bool warnedAttachFailure;

    public bool IsSimpleMovement => simpleMovement;
    public bool IsWaitingForActivation => waitingForActivation;
    public Transform Target => target;

    private void Awake()
    {
        ResolveComponents();
    }

    public void Configure(MonsterPool pool, Transform playerTarget, bool startSimple)
    {
        ResolveComponents();
        owner = pool;
        target = playerTarget;
        forcedSimpleEntry = startSimple;
        waitingForActivation = startSimple && holdSimpleEntryUntilPlayerNear;
        failedAttachElapsed = 0f;
        warnedAttachFailure = false;
        nextNavAttachCheck = Time.time + Random.Range(0f, 0.12f);

        ApplyGeneratedTestSizing();

        if (owner != null)
            owner.RegisterCrowdAgent(this, target);

        if (startSimple)
            EnterSimpleMovement(waitingForActivation);
        else
            ExitSimpleMovementWithoutSnap();
    }

    private void ApplyGeneratedTestSizing()
    {
        if (controller == null || controller.Definition == null)
            return;

        string id = controller.Definition.monsterId;
        if (string.IsNullOrEmpty(id) || !id.StartsWith("TEST_"))
            return;

        // TEST 더미는 8x8 world Start Base에서 읽히도록 확실하게 키웁니다.
        // 실제 Enemy Prefab/SO의 크기는 건드리지 않습니다.
        transform.localScale = Vector3.one * 1.6f;
        CircleCollider2D circle = GetComponent<CircleCollider2D>();
        if (circle != null)
            circle.radius = Mathf.Max(circle.radius, 0.46f);
    }

    public void PrepareForPool()
    {
        if (owner != null)
            owner.UnregisterCrowdAgent(this);

        owner = null;
        target = null;
        simpleMovement = false;
        forcedSimpleEntry = false;
        waitingForActivation = false;
        failedAttachElapsed = 0f;
        warnedAttachFailure = false;
    }

    private void ResolveComponents()
    {
        if (controller == null)
            controller = GetComponent<MonsterController>();
        if (navAgent == null)
            navAgent = GetComponent<NavMeshAgent>();
        if (spriteAnimator == null)
            spriteAnimator = GetComponent<EnemyAnimator>();
        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();
    }

    private void Update()
    {
        if (owner == null || target == null || controller == null || !controller.IsAlive)
            return;

        Vector2 toPlayer = (Vector2)target.position - (Vector2)transform.position;
        float distance = toPlayer.magnitude;

        if (toPlayer.sqrMagnitude > 0.001f)
            spriteAnimator?.SetFacing(toPlayer);

        if (!simpleMovement && owner.ShouldUseSimpleMovement(distance, forcedSimpleEntry))
            EnterSimpleMovement(false);

        if (!simpleMovement)
            return;

        if (waitingForActivation)
        {
            spriteAnimator?.Play(EnemyAnimState.Idle, true);

            if (distance > Mathf.Max(0.5f, simpleEntryWakeDistance))
                return;

            waitingForActivation = false;
            spriteAnimator?.Play(EnemyAnimState.Move, true);
        }

        UpdateSimpleMovement();
        TryReturnToPreciseMovement(distance);
    }

    private void UpdateSimpleMovement()
    {
        if (owner == null || target == null || controller == null || controller.Definition == null)
            return;

        Vector2 destination = owner.GetCrowdMoveTarget(this, target.position);
        Vector2 direction = destination - (Vector2)transform.position;
        if (direction.sqrMagnitude <= 0.001f)
            return;

        direction.Normalize();
        float speed = Mathf.Max(0f, controller.Definition.moveSpeed) * owner.SimpleMoveSpeedMultiplier;
        float moveDistance = speed * Time.deltaTime;
        if (moveDistance <= 0f)
            return;

        if (WouldHitRoomWall(direction, moveDistance))
        {
            spriteAnimator?.Play(EnemyAnimState.Idle, true);
            return;
        }

        transform.position += (Vector3)(direction * moveDistance);
        spriteAnimator?.SetFacing(direction);
        spriteAnimator?.Play(EnemyAnimState.Move, true);
    }

    private bool WouldHitRoomWall(Vector2 direction, float moveDistance)
    {
        if (controller == null || controller.Definition == null)
            return false;

        LayerMask wallMask = controller.Definition.wallLayer;
        if (wallMask.value == 0)
            return false;

        float radius = 0.18f;
        if (bodyCollider != null)
        {
            Vector3 extents = bodyCollider.bounds.extents;
            radius = Mathf.Max(0.08f, Mathf.Min(extents.x, extents.y) * 0.72f);
        }

        RaycastHit2D hit = Physics2D.CircleCast(
            transform.position,
            radius,
            direction,
            moveDistance + Mathf.Max(0f, simpleCollisionPadding),
            wallMask);

        if (hit.collider == null)
            return false;

        Transform hitTransform = hit.collider.transform;
        return hitTransform != transform && !hitTransform.IsChildOf(transform);
    }

    private void TryReturnToPreciseMovement(float distance)
    {
        if (waitingForActivation || owner == null || Time.time < nextNavAttachCheck)
            return;

        nextNavAttachCheck = Time.time + owner.NavAttachCheckInterval + Random.Range(0f, 0.05f);

        if (!owner.ShouldReturnToPreciseMovement(distance, forcedSimpleEntry))
            return;

        if (owner.TryGetNavAttachPosition(transform.position, out Vector3 navPosition))
        {
            transform.position = navPosition;
            forcedSimpleEntry = false;
            failedAttachElapsed = 0f;
            ExitSimpleMovementWithoutSnap();
            return;
        }

        failedAttachElapsed += owner.NavAttachCheckInterval;
        if (!warnedAttachFailure && failedAttachElapsed >= 2.5f && distance <= owner.PreciseNavDistance + 1f)
        {
            warnedAttachFailure = true;
            Debug.LogWarning(
                $"[MonsterCrowd] '{name}' reached the precise-AI zone but no NavMesh was found nearby. " +
                "Check Start Base / Room Wall / NavMeshSurface setup. It will keep Simple Movement for diagnostics.",
                this);
        }
    }

    private void EnterSimpleMovement(bool holdAtSpawn)
    {
        simpleMovement = true;
        waitingForActivation = holdAtSpawn;

        if (controller != null)
            controller.enabled = false;

        if (navAgent != null && navAgent.enabled)
            navAgent.enabled = false;

        spriteAnimator?.Play(waitingForActivation ? EnemyAnimState.Idle : EnemyAnimState.Move, true);
    }

    private void ExitSimpleMovementWithoutSnap()
    {
        simpleMovement = false;
        waitingForActivation = false;

        if (controller == null || controller.Definition == null)
            return;

        MonsterDefinitionSO definition = controller.Definition;
        if (definition.moveType != MonsterMoveType.Stationary && navAgent != null)
        {
            navAgent.enabled = true;
            navAgent.updateRotation = false;
            navAgent.updateUpAxis = false;
            navAgent.speed = Mathf.Max(0f, definition.moveSpeed);
            navAgent.acceleration = Mathf.Max(0f, definition.acceleration);
            navAgent.stoppingDistance = Mathf.Max(0f, definition.stoppingDistance);

            if (navAgent.isOnNavMesh)
                navAgent.isStopped = false;
        }

        controller.enabled = true;
    }
}

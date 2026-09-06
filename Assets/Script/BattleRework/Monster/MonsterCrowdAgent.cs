using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Monster가 멀리 있거나 Base 밖에서 진입하는 동안 NavMesh/A* 갱신을 끄고
/// MonsterPool이 계산한 Group Target을 향해 단순 직선 이동시키는 Crowd LOD Agent입니다.
///
/// 가까워지면 NavMesh 위로 붙여 MonsterController의 정밀 AI를 다시 켭니다.
/// 이 컴포넌트 하나로 수십~수백 마리 상황에서 모든 개체가 매 프레임 SetDestination을 호출하는 것을 피합니다.
/// </summary>
[DisallowMultipleComponent]
public class MonsterCrowdAgent : MonoBehaviour
{
    private MonsterPool owner;
    private MonsterController controller;
    private NavMeshAgent navAgent;
    private EnemyAnimator spriteAnimator;
    private Transform target;

    private bool simpleMovement;
    private bool forcedSimpleEntry;
    private float nextNavAttachCheck;
    private float failedAttachElapsed;
    private bool warnedAttachFailure;

    public bool IsSimpleMovement => simpleMovement;
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
        failedAttachElapsed = 0f;
        warnedAttachFailure = false;
        nextNavAttachCheck = Time.time + Random.Range(0f, 0.12f);

        ApplyGeneratedTestSizing();

        if (owner != null)
            owner.RegisterCrowdAgent(this, target);

        if (startSimple)
            EnterSimpleMovement();
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

        // 자동 생성 더미는 기존 0.75 world 크기가 너무 작았기 때문에
        // 4x4 / 8x8 world 기준에서 읽히는 약 1 world 크기로 올립니다.
        transform.localScale = Vector3.one;
        CircleCollider2D circle = GetComponent<CircleCollider2D>();
        if (circle != null)
            circle.radius = Mathf.Max(circle.radius, 0.42f);
    }

    public void PrepareForPool()
    {
        if (owner != null)
            owner.UnregisterCrowdAgent(this);

        owner = null;
        target = null;
        simpleMovement = false;
        forcedSimpleEntry = false;
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
            EnterSimpleMovement();

        if (!simpleMovement)
            return;

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
        transform.position += (Vector3)(direction * speed * Time.deltaTime);
        spriteAnimator?.SetFacing(direction);
        spriteAnimator?.Play(EnemyAnimState.Move, true);
    }

    private void TryReturnToPreciseMovement(float distance)
    {
        if (owner == null || Time.time < nextNavAttachCheck)
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
                "Check MapBlock NavMesh sources / NavMeshSurface. It will keep simple movement for diagnostics.",
                this);
        }
    }

    private void EnterSimpleMovement()
    {
        if (simpleMovement)
            return;

        simpleMovement = true;

        if (controller != null)
            controller.enabled = false;

        if (navAgent != null && navAgent.enabled)
            navAgent.enabled = false;

        spriteAnimator?.Play(EnemyAnimState.Move, true);
    }

    private void ExitSimpleMovementWithoutSnap()
    {
        simpleMovement = false;

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

/// <summary>
/// 생성된 TEST Room의 기존 내부 Spawn Point를 런타임에서 Base 외곽으로 이동시킵니다.
/// 정식 Room Asset은 건드리지 않고 TEST_NodeGraph에만 적용합니다.
/// </summary>
internal static class BattleTestSpawnPerimeterMigration
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void MoveGeneratedTestSpawnsOutsideBase()
    {
        NodeGraphSO graph = Resources.Load<NodeGraphSO>("BattleTestDefaults/TEST_NodeGraph");
        if (graph == null || graph.nodes == null)
            return;

        HashSet<RoomDefinitionSO> visited = new();
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            BattleNodeData node = graph.nodes[i];
            RoomDefinitionSO room = node != null ? node.room : null;
            if (room == null || !visited.Add(room) || room.monsterSpawns == null)
                continue;

            MoveRoomSpawns(room);
        }
    }

    private static void MoveRoomSpawns(RoomDefinitionSO room)
    {
        Vector2 center = room.GetTemplateCenterOffset();
        Vector2 half = room.GetTemplateWorldSize() * 0.5f;
        Vector2 min = center - half;
        Vector2 max = center + half;
        const float outsideDistance = 1.8f;

        for (int i = 0; i < room.monsterSpawns.Count; i++)
        {
            MonsterSpawnEntry spawn = room.monsterSpawns[i];
            if (spawn == null)
                continue;

            Vector2 p = spawn.localPosition;
            bool inside = p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y;
            if (!inside)
                continue;

            float left = p.x - min.x;
            float right = max.x - p.x;
            float bottom = p.y - min.y;
            float top = max.y - p.y;
            float nearest = Mathf.Min(left, right, bottom, top);

            if (Mathf.Approximately(nearest, left))
                p.x = min.x - outsideDistance;
            else if (Mathf.Approximately(nearest, right))
                p.x = max.x + outsideDistance;
            else if (Mathf.Approximately(nearest, bottom))
                p.y = min.y - outsideDistance;
            else
                p.y = max.y + outsideDistance;

            spawn.localPosition = p;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 몬스터 생성/반환만 담당합니다.
/// 언제/어디에/무엇을 스폰할지는 BattleRoomManager / RoomDefinitionSO가 결정합니다.
///
/// Spawn rule:
/// - Room SO의 localPosition을 그대로 기준점으로 사용합니다.
/// - Spawn은 현재 완성된 Room의 NavMesh 위에서만 허용합니다.
/// - Player까지 완전한 NavMesh 경로가 없는 위치는 거부합니다.
/// - Start Base 안쪽 Spawn을 임의로 벽 밖/외곽으로 투영하지 않습니다.
/// </summary>
public class MonsterPool : MonoBehaviour
{
    [SerializeField] private MonsterController monsterPrefab;
    [SerializeField] private int initialPoolSize = 20;
    [SerializeField] private ProjectilePooler enemyProjectilePool;

    [Header("Spawn Safety")]
    [SerializeField, Min(0.1f)] private float navMeshSampleRadius = 1.25f;
    [SerializeField, Min(0)] private int reachableSpawnRetryCount = 6;
    [SerializeField, Min(0f)] private float retryScatterRadius = 1.0f;

    private readonly Queue<MonsterController> pool = new();
    private bool initialized;

    public bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();

        if (monsterPrefab == null)
            errors.Add("monsterPrefab is null");

        report = string.Join("\n", errors);
        return errors.Count == 0;
    }

    private void Awake()
    {
        TryInitialize();
    }

    public void Configure(MonsterController prefab, ProjectilePooler projectilePool = null)
    {
        if (prefab != null)
            monsterPrefab = prefab;

        if (projectilePool != null)
            enemyProjectilePool = projectilePool;

        TryInitialize();
    }

    private void TryInitialize()
    {
        if (initialized || monsterPrefab == null)
            return;

        int preloadCount = Mathf.Max(0, initialPoolSize);
        for (int i = pool.Count; i < preloadCount; i++)
        {
            MonsterController monster = CreateNew();
            if (monster == null)
                break;

            Return(monster);
        }

        initialized = true;
    }

    private MonsterController CreateNew()
    {
        if (monsterPrefab == null)
            return null;

        MonsterController monster = Instantiate(monsterPrefab, transform);
        monster.gameObject.SetActive(false);
        return monster;
    }

    public MonsterController Get(
        Vector3 requestedPosition,
        MonsterDefinitionSO definition,
        BattleContext context,
        Transform playerTarget,
        System.Action<MonsterController> onDeath)
    {
        if (definition == null)
        {
            Debug.LogError("[MonsterPool] Get failed: MonsterDefinitionSO is null.");
            return null;
        }

        if (playerTarget == null)
        {
            Debug.LogError($"[MonsterPool] Get failed for '{definition.name}': playerTarget is null.");
            return null;
        }

        TryInitialize();
        if (monsterPrefab == null)
        {
            Debug.LogError("[MonsterPool] Get failed: pool has no monsterPrefab.");
            return null;
        }

        if (!TryResolveSpawnPosition(
                requestedPosition,
                playerTarget.position,
                definition.moveType,
                out Vector3 spawnPosition))
        {
            Debug.LogError(
                $"[MonsterPool] Spawn rejected for '{definition.displayName}' at {requestedPosition}. " +
                "No reachable Room NavMesh position could be found. The monster will not be counted by the Room.");
            return null;
        }

        MonsterController monster = pool.Count > 0 ? pool.Dequeue() : CreateNew();
        if (monster == null)
            return null;

        NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
        if (agent != null)
            agent.enabled = false;

        monster.enabled = true;
        monster.gameObject.SetActive(false);
        spawnPosition.z = 0f;
        monster.transform.position = spawnPosition;
        monster.gameObject.SetActive(true);
        monster.Setup(definition, context, playerTarget, enemyProjectilePool, onDeath);

        ApplyGeneratedTestSizing(monster, definition);

        if (agent != null && agent.enabled && !agent.isOnNavMesh)
        {
            Debug.LogError(
                $"[MonsterPool] '{definition.displayName}' Setup enabled its NavMeshAgent off-mesh. " +
                "Returning it to the pool to prevent a room soft-lock.");
            Return(monster);
            return null;
        }

        return monster;
    }

    private bool TryResolveSpawnPosition(
        Vector3 requestedPosition,
        Vector3 playerPosition,
        MonsterMoveType moveType,
        out Vector3 resolved)
    {
        resolved = requestedPosition;

        bool movingMonster = moveType != MonsterMoveType.Stationary;
        bool hasTargetSample = NavMesh.SamplePosition(
            playerPosition,
            out NavMeshHit targetHit,
            Mathf.Max(navMeshSampleRadius, 0.1f) * 2f,
            NavMesh.AllAreas);

        if (movingMonster && !hasTargetSample)
        {
            Debug.LogError(
                $"[MonsterPool] Player position {playerPosition} is not near the current Room NavMesh. " +
                "Moving monsters cannot be validated.");
            return false;
        }

        int attempts = Mathf.Max(1, reachableSpawnRetryCount + 1);
        for (int i = 0; i < attempts; i++)
        {
            Vector2 jitter = i == 0 || retryScatterRadius <= 0f
                ? Vector2.zero
                : Random.insideUnitCircle * retryScatterRadius;

            Vector3 candidate = requestedPosition + (Vector3)jitter;
            if (!NavMesh.SamplePosition(
                    candidate,
                    out NavMeshHit spawnHit,
                    Mathf.Max(navMeshSampleRadius, 0.1f),
                    NavMesh.AllAreas))
            {
                continue;
            }

            if (!movingMonster)
            {
                resolved = spawnHit.position;
                return true;
            }

            NavMeshPath path = new();
            bool pathCalculated = NavMesh.CalculatePath(
                spawnHit.position,
                targetHit.position,
                NavMesh.AllAreas,
                path);

            if (!pathCalculated || path.status != NavMeshPathStatus.PathComplete)
                continue;

            resolved = spawnHit.position;
            return true;
        }

        return false;
    }

    private static void ApplyGeneratedTestSizing(
        MonsterController monster,
        MonsterDefinitionSO definition)
    {
        if (monster == null || definition == null)
            return;

        if (string.IsNullOrEmpty(definition.monsterId) ||
            !definition.monsterId.StartsWith("TEST_"))
        {
            return;
        }

        // 자동 생성 테스트 더미만 크게 유지합니다.
        // 실제 Enemy Prefab은 제작자가 설정한 Transform/Collider 크기를 그대로 사용합니다.
        monster.transform.localScale = Vector3.one * 1.6f;

        CircleCollider2D circle = monster.GetComponent<CircleCollider2D>();
        if (circle != null)
            circle.radius = Mathf.Max(circle.radius, 0.46f);
    }

    public void Return(MonsterController monster)
    {
        if (monster == null)
            return;

        monster.PrepareForPool();
        monster.gameObject.SetActive(false);
        monster.transform.SetParent(transform);

        if (!pool.Contains(monster))
            pool.Enqueue(monster);
    }
}

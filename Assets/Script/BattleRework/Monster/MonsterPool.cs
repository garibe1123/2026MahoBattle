using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 몬스터 풀은 생성/반환만 담당합니다.
/// 언제/어디에/무엇을 스폰할지는 BattleRoomManager가 결정합니다.
/// Spawn 시 NavMesh와 Player까지의 도달 가능성을 검사해 Room soft-lock을 방지합니다.
/// BattleScene runtime bootstrap을 위해 prefab 배선 전 AddComponent도 허용하고 실제 초기화는 지연합니다.
/// </summary>
public class MonsterPool : MonoBehaviour
{
    [SerializeField] private MonsterController monsterPrefab;
    [SerializeField] private int initialPoolSize = 20;
    [SerializeField] private ProjectilePooler enemyProjectilePool;

    [Header("Spawn Safety")]
    [SerializeField, Min(0.1f)] private float navMeshSampleRadius = 2f;
    [SerializeField, Min(0)] private int reachableSpawnRetryCount = 6;
    [SerializeField, Min(0f)] private float retryScatterRadius = 1.5f;

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
        // BattleSceneEntry가 최소 Scene에서 런타임 구조를 조립할 때는
        // prefab이 이 Awake 이후 BattleSceneManager에서 배선될 수 있습니다.
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

        if (!TryResolveSpawnPosition(requestedPosition, playerTarget.position, definition.moveType, out Vector3 spawnPosition))
        {
            Debug.LogError(
                $"[MonsterPool] Spawn rejected for '{definition.displayName}' at {requestedPosition}. " +
                "No reachable NavMesh position could be found. The monster will not be counted by the Room.");
            return null;
        }

        MonsterController monster = pool.Count > 0 ? pool.Dequeue() : CreateNew();
        if (monster == null)
            return null;

        NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
        if (agent != null)
            agent.enabled = false;

        monster.gameObject.SetActive(false);
        spawnPosition.z = 0f;
        monster.transform.position = spawnPosition;
        monster.gameObject.SetActive(true);
        monster.Setup(definition, context, playerTarget, enemyProjectilePool, onDeath);

        if (agent != null && agent.enabled && !agent.isOnNavMesh)
        {
            Debug.LogError(
                $"[MonsterPool] '{definition.displayName}' Setup enabled its NavMeshAgent off-mesh. " +
                "Returning it to the pool to prevent a soft-lock.");
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
            Debug.LogError($"[MonsterPool] Player position {playerPosition} is not near a NavMesh. Moving monsters cannot be validated.");
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

    public void Return(MonsterController monster)
    {
        if (monster == null) return;

        monster.PrepareForPool();
        monster.gameObject.SetActive(false);
        monster.transform.SetParent(transform);

        if (!pool.Contains(monster))
            pool.Enqueue(monster);
    }
}

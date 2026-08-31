using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 몬스터 풀은 생성/반환만 담당합니다.
/// 언제/어디에/무엇을 스폰할지는 BattleRoomManager가 결정합니다.
/// </summary>
public class MonsterPool : MonoBehaviour
{
    [SerializeField] private MonsterController monsterPrefab;
    [SerializeField] private int initialPoolSize = 20;
    [SerializeField] private ProjectilePooler enemyProjectilePool;

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
        if (!ValidateConfiguration(out string report))
        {
            Debug.LogError($"[MonsterPool] Invalid configuration.\n{report}");
            return;
        }

        int preloadCount = Mathf.Max(0, initialPoolSize);
        for (int i = 0; i < preloadCount; i++)
        {
            MonsterController monster = CreateNew();
            if (monster == null) break;
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

        if (!initialized && monsterPrefab == null)
        {
            Debug.LogError("[MonsterPool] Get failed: pool has no monsterPrefab.");
            return null;
        }

        MonsterController monster = pool.Count > 0 ? pool.Dequeue() : CreateNew();
        if (monster == null)
            return null;

        NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
        if (agent != null)
            agent.enabled = false;

        monster.gameObject.SetActive(false);

        Vector3 spawnPosition = requestedPosition;
        bool sampled = NavMesh.SamplePosition(requestedPosition, out NavMeshHit hit, 4f, NavMesh.AllAreas);
        if (sampled)
        {
            spawnPosition = hit.position;
        }
        else
        {
            Debug.LogWarning($"[MonsterPool] NavMesh.SamplePosition failed for '{definition.displayName}' at {requestedPosition}. Spawn will use the requested position for diagnostics.");
        }

        spawnPosition.z = 0f;
        monster.transform.position = spawnPosition;
        monster.gameObject.SetActive(true);
        monster.Setup(definition, context, playerTarget, enemyProjectilePool, onDeath);

        if (agent != null && agent.enabled && !agent.isOnNavMesh)
            Debug.LogWarning($"[MonsterPool] '{definition.displayName}' spawned but its NavMeshAgent is not on NavMesh.");

        return monster;
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

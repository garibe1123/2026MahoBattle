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
    [SerializeField] private int initialPoolSize = 40;
    [SerializeField] private ProjectilePooler enemyProjectilePool;

    private readonly Queue<MonsterController> pool = new();

    private void Awake()
    {
        for (int i = 0; i < initialPoolSize; i++)
        {
            MonsterController monster = CreateNew();
            Return(monster);
        }
    }

    private MonsterController CreateNew()
    {
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
        if (definition == null || playerTarget == null)
            return null;

        MonsterController monster = pool.Count > 0 ? pool.Dequeue() : CreateNew();
        NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();

        if (agent != null)
            agent.enabled = false;

        monster.gameObject.SetActive(false);

        Vector3 spawnPosition = requestedPosition;
        if (NavMesh.SamplePosition(requestedPosition, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            spawnPosition = hit.position;

        spawnPosition.z = 0f;
        monster.transform.position = spawnPosition;
        monster.gameObject.SetActive(true);
        monster.Setup(definition, context, playerTarget, enemyProjectilePool, onDeath);
        return monster;
    }

    public void Return(MonsterController monster)
    {
        if (monster == null) return;

        monster.PrepareForPool();
        monster.gameObject.SetActive(false);
        monster.transform.SetParent(transform);
        pool.Enqueue(monster);
    }
}

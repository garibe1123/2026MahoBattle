using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class MonsterPool : MonoBehaviour
{
    [SerializeField] private MonsterController monsterPrefab;
    [SerializeField] private int initialPoolSize = 40;
    [SerializeField] private Transform playerTarget;
    [SerializeField] private ProjectilePooler enemyProjectilePool;

    private readonly Queue<MonsterController> pool = new();

    private void Awake()
    {
        for (int i = 0; i < initialPoolSize; i++)
            pool.Enqueue(CreateInstance());
    }

    private MonsterController CreateInstance()
    {
        MonsterController monster = Instantiate(monsterPrefab, transform);
        NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;
        monster.gameObject.SetActive(false);
        return monster;
    }

    public MonsterController Get(Vector3 position, MonsterDefinitionSO definition, BattleContext context)
    {
        MonsterController monster = pool.Count > 0 ? pool.Dequeue() : CreateInstance();
        NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();

        if (agent != null) agent.enabled = false;
        monster.transform.position = position;
        monster.gameObject.SetActive(true);

        if (definition.moveType != MonsterMoveType.Stationary)
        {
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(position, out hit, 4f, NavMesh.AllAreas))
            {
                Return(monster);
                return null;
            }
            monster.transform.position = hit.position;
        }

        monster.Setup(definition, context, playerTarget, enemyProjectilePool, this);
        return monster;
    }

    public void Return(MonsterController monster)
    {
        if (monster == null || pool.Contains(monster)) return;

        NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        monster.gameObject.SetActive(false);
        pool.Enqueue(monster);
    }
}

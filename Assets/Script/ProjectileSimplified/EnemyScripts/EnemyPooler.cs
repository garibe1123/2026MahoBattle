using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[System.Serializable]
public class EnemySpawnerSetting
{
    public ProjectilePooler ProjectilePool;
    public EnemySO enemySO;
    public float spawnInterval;
    [HideInInspector] public float currentTimer;
}

public class EnemyPooler : MonoBehaviour
{
    public static EnemyPooler Instance { get; private set; }

    [Header("Pool Settings")]
    public Enemy enemyPrefab;
    public int initialPoolSize = 50;

    [Header("Targeting")]
    public Transform playerTarget;

    // ★ 추가: 몹들에게 쥐여줄 총알 창고
    [Header("Bullet Pool (적 전용)")]
    public ProjectilePooler enemyBulletPool;


    [Header("Spawn Settings")]
    public EnemySpawnerSetting[] enemySpawnerSettings;

    [Header("Spawn Radius (도넛 형태)")]
    public float minSpawnDistance = 12f;
    public float maxSpawnDistance = 18f;

    private Queue<Enemy> pool = new Queue<Enemy>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        for (int i = 0; i < initialPoolSize; i++)
        {
            Enemy newEnemy = Instantiate(enemyPrefab, transform);

            // 처음 만들어질 때 무조건 수면제(비활성화) 먹이고 창고에 넣기
            NavMeshAgent tempAgent = newEnemy.GetComponent<NavMeshAgent>();
            if (tempAgent != null) tempAgent.enabled = false;

            newEnemy.gameObject.SetActive(false);
            pool.Enqueue(newEnemy);
        }
    }

    void Update()
    {
        if (playerTarget == null || enemySpawnerSettings == null || enemySpawnerSettings.Length == 0) return;

        for (int i = 0; i < enemySpawnerSettings.Length; i++)
        {
            enemySpawnerSettings[i].currentTimer += Time.deltaTime;

            if (enemySpawnerSettings[i].currentTimer >= enemySpawnerSettings[i].spawnInterval)
            {
                SpawnSpecificEnemy(enemySpawnerSettings[i].enemySO);
                enemySpawnerSettings[i].currentTimer -= enemySpawnerSettings[i].spawnInterval;
            }
        }
    }

    void SpawnSpecificEnemy(EnemySO selectedSO)
    {
        Vector2 randomDirection = Random.insideUnitCircle.normalized;
        float randomDistance = Random.Range(minSpawnDistance, maxSpawnDistance);
        Vector3 spawnPos = playerTarget.position + (Vector3)(randomDirection * randomDistance);

        // 여기 있던 안전검사를 없앴습니다. Get() 안에서 무조건 검사할 거니까요!
        Get(spawnPos, selectedSO);
    }

    // ★ 세상에서 제일 안전하게 NavMesh Agent를 소환하는 로직 
    public Enemy Get(Vector3 position, EnemySO enemyData)
    {
        Enemy enemy;
        if (pool.Count > 0) enemy = pool.Dequeue();
        else enemy = Instantiate(enemyPrefab, transform);

        NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();

        // 1. 깨우기 전에 무조건 수면제(끄기) 먹임! (에러 방지 1순위)
        if (agent != null) agent.enabled = false;
        enemy.gameObject.SetActive(false);

        // 2. 둥지가 부르든 도넛이 부르든, 목표 위치 근처 '10 반경 이내'의 가장 완벽한 바닥을 찾습니다.
        NavMeshHit hit;
        if (NavMesh.SamplePosition(position, out hit, 10f, NavMesh.AllAreas))
        {
            position = hit.position; // 벽 속이 아닌, 진짜 밟을 수 있는 바닥으로 좌표 강제 보정!
        }
        else
        {
            // 방이 너무 작아서 10 범위 안에서도 바닥을 도저히 못 찾았다면?
            // 에러를 내뿜지 않고 그냥 이번 스폰을 포기하고 창고에 다시 넣습니다.
            Debug.LogWarning($"[{enemyData.name}] 소환 취소: 스폰 위치 근처에 밟을 수 있는 NavMesh 바닥이 없습니다! (에디터에서 스폰 거리를 줄여주세요)");
            pool.Enqueue(enemy);
            return null;
        }

        // 3. 2D 환경 전용 강제 평면(Z축 0) 고정!
        position.z = 0f;

        // 4. 안전한 바닥 위치로 먼저 이동시킨 다음, 오브젝트를 켭니다.
        enemy.transform.position = position;
        enemy.gameObject.SetActive(true);

        // 5. 발이 완벽하게 바닥에 닿은 이 시점에 에이전트를 켭니다. (Warp 안 씀!)
        if (agent != null)
        {
            agent.enabled = true;
        }

        // 데이터 셋업
        if (playerTarget != null)
        {
            // ★ 수정: Setup에 enemyBulletPool 도 같이 넘겨줌!
            enemy.Setup(enemyData, playerTarget, enemyBulletPool);
        }

        return enemy;
    }

    public void ReturnToPool(Enemy enemy)
    {
        NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        enemy.gameObject.SetActive(false);
        pool.Enqueue(enemy);
    }

    void OnDrawGizmosSelected()
    {
        if (playerTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(playerTarget.position, minSpawnDistance);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(playerTarget.position, maxSpawnDistance);
        }
    }
}
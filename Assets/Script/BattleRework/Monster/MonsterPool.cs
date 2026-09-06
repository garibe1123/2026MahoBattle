using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

/// <summary>
/// 몬스터 생성/반환 + 대규모 Crowd LOD 그룹 정보를 관리합니다.
///
/// Spawn rule:
/// - Persistent Start Base 내부에서는 Monster를 생성하지 않습니다.
/// - Room SO의 Spawn Point가 Base 내부면 가장 가까운 외곽 면 밖으로 자동 투영합니다.
/// - 이동형 Monster는 Base 밖에서 Simple Movement로 진입한 뒤 가까워지면 NavMesh 정밀 AI로 전환합니다.
/// - Stationary Monster도 Base 내부 좌표는 외부로 이동되지만, 고정형이므로 그 위치에서 그대로 동작합니다.
///
/// Crowd rule:
/// - 대규모 몬스터는 멀리 있을 때 NavMeshAgent/A* 갱신을 끕니다.
/// - 여러 Monster가 Player Target을 개별 계산하지 않고 Group Target 캐시를 공유합니다.
/// </summary>
public class MonsterPool : MonoBehaviour
{
    [SerializeField] private MonsterController monsterPrefab;
    [SerializeField] private int initialPoolSize = 20;
    [SerializeField] private ProjectilePooler enemyProjectilePool;

    [Header("Persistent Start Base Spawn Exclusion")]
    [FormerlySerializedAs("forceMovingSpawnsOutsideBase")]
    [Tooltip("Start Base 내부의 모든 Monster Spawn Point를 가장 가까운 외곽 면 밖으로 이동합니다.")]
    [SerializeField] private bool forceSpawnsOutsideBase = true;
    [SerializeField, Min(0.1f)] private float outsideSpawnDistance = 1.8f;
    [SerializeField] private BattleRoomManager roomManager;

    [Header("Spawn Safety")]
    [SerializeField, Min(0.1f)] private float navMeshSampleRadius = 2f;
    [SerializeField, Min(0)] private int reachableSpawnRetryCount = 6;
    [SerializeField, Min(0f)] private float retryScatterRadius = 1.5f;

    [Header("Crowd LOD")]
    [SerializeField] private bool enableCrowdOptimization = true;
    [Tooltip("이 수 이하에서는 기존 정밀 AI를 우선합니다. Base 밖 진입 몬스터는 수와 관계없이 Simple Entry를 사용합니다.")]
    [SerializeField, Min(1)] private int optimizationThreshold = 10;
    [Tooltip("공유 Target을 사용하는 몬스터 그룹당 최대 인원입니다.")]
    [SerializeField, Range(2, 32)] private int membersPerGroup = 10;
    [Tooltip("Player와 이 거리보다 멀고 몬스터 수가 많으면 NavMesh/A* 대신 Simple Movement를 사용합니다.")]
    [SerializeField, Min(1f)] private float preciseNavDistance = 6.2f;
    [SerializeField, Min(0.02f)] private float groupTargetRefreshInterval = 0.14f;
    [SerializeField, Range(0.25f, 1.5f)] private float simpleMoveSpeedMultiplier = 0.92f;
    [SerializeField, Min(0.1f)] private float navAttachSampleRadius = 1.25f;
    [SerializeField, Min(0.03f)] private float navAttachCheckInterval = 0.16f;
    [Tooltip("같은 Group 멤버가 정확히 같은 점으로 겹치지 않도록 Player 주변에 주는 작은 Formation 반경입니다.")]
    [SerializeField, Min(0f)] private float groupFormationRadius = 0.9f;

    private readonly Queue<MonsterController> pool = new();
    private readonly Dictionary<MonsterCrowdAgent, CrowdMemberRecord> crowdMembers = new();
    private readonly List<CrowdGroup> crowdGroups = new();

    private bool initialized;
    private int nextCrowdGroupId;
    private float nextCrowdTargetRefresh;

    public int ActiveCrowdCount => crowdMembers.Count;
    public float PreciseNavDistance => Mathf.Max(1f, preciseNavDistance);
    public float SimpleMoveSpeedMultiplier => Mathf.Max(0.25f, simpleMoveSpeedMultiplier);
    public float NavAttachCheckInterval => Mathf.Max(0.03f, navAttachCheckInterval);

    private sealed class CrowdMemberRecord
    {
        public CrowdGroup group;
        public Vector2 slotOffset;
    }

    private sealed class CrowdGroup
    {
        public int id;
        public Transform target;
        public Vector2 cachedTarget;
        public readonly List<MonsterCrowdAgent> members = new();
    }

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
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();

        TryInitialize();
    }

    private void Update()
    {
        if (Time.time < nextCrowdTargetRefresh)
            return;

        nextCrowdTargetRefresh = Time.time + Mathf.Max(0.02f, groupTargetRefreshInterval);

        for (int i = crowdGroups.Count - 1; i >= 0; i--)
        {
            CrowdGroup group = crowdGroups[i];
            if (group == null || group.members.Count == 0)
            {
                crowdGroups.RemoveAt(i);
                continue;
            }

            if (group.target != null)
                group.cachedTarget = group.target.position;
        }
    }

    public void Configure(MonsterController prefab, ProjectilePooler projectilePool = null)
    {
        if (prefab != null)
            monsterPrefab = prefab;
        if (projectilePool != null)
            enemyProjectilePool = projectilePool;

        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();

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
        if (monster.GetComponent<MonsterCrowdAgent>() == null)
            monster.gameObject.AddComponent<MonsterCrowdAgent>();
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

        bool movingMonster = definition.moveType != MonsterMoveType.Stationary;
        requestedPosition = ResolveRoomPerimeterSpawn(requestedPosition, out bool projectedOutsideBase);

        bool nearRequestedNavMesh = NavMesh.SamplePosition(
            requestedPosition,
            out _,
            0.35f,
            NavMesh.AllAreas);

        Vector3 spawnPosition;
        bool startSimpleMovement = movingMonster && projectedOutsideBase;

        // Base 외곽 진입은 NavMesh 유무/몬스터 수와 관계없이 Simple Entry로 시작합니다.
        // Start Base 안쪽으로 순간 Snap되어 보이는 것을 막고, 외부 진입 연출과 Crowd LOD를 동일한 경로로 처리합니다.
        if (startSimpleMovement)
        {
            spawnPosition = requestedPosition;
        }
        else if (!movingMonster && projectedOutsideBase)
        {
            // 고정형은 NavMesh가 없는 Base 외부에도 놓일 수 있습니다.
            spawnPosition = requestedPosition;
        }
        else if (movingMonster && enableCrowdOptimization && !nearRequestedNavMesh)
        {
            spawnPosition = requestedPosition;
            startSimpleMovement = true;
        }
        else
        {
            if (!TryResolveSpawnPosition(
                    requestedPosition,
                    playerTarget.position,
                    definition.moveType,
                    out spawnPosition))
            {
                Debug.LogError(
                    $"[MonsterPool] Spawn rejected for '{definition.displayName}' at {requestedPosition}. " +
                    "No reachable NavMesh position could be found. The monster will not be counted by the Room.");
                return null;
            }

            // Safety: NavMesh Sample이 다시 Start Base 안으로 끌어당겼다면 내부 Spawn을 허용하지 않습니다.
            if (IsInsideCurrentStartBase(spawnPosition))
            {
                spawnPosition = ResolveRoomPerimeterSpawn(spawnPosition, out bool safetyProjected);
                if (movingMonster && safetyProjected)
                    startSimpleMovement = true;
            }

            float distance = Vector2.Distance(spawnPosition, playerTarget.position);
            if (!startSimpleMovement)
            {
                startSimpleMovement = movingMonster &&
                                      enableCrowdOptimization &&
                                      crowdMembers.Count + 1 > Mathf.Max(1, optimizationThreshold) &&
                                      distance > PreciseNavDistance;
            }
        }

        MonsterController monster = pool.Count > 0 ? pool.Dequeue() : CreateNew();
        if (monster == null)
            return null;

        NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
        if (agent != null)
            agent.enabled = false;

        Vector3 setupPosition = spawnPosition;
        if (startSimpleMovement && movingMonster &&
            NavMesh.SamplePosition(
                playerTarget.position,
                out NavMeshHit safeSetupHit,
                Mathf.Max(1f, navMeshSampleRadius) * 2f,
                NavMesh.AllAreas))
        {
            // MonsterController.Setup이 NavMeshAgent를 구성할 수 있도록 잠깐 안전한 내부 NavMesh 위치에서 Setup한 뒤
            // Crowd Agent를 Simple 상태로 바꾸고 실제 외곽 Spawn 위치로 옮깁니다.
            setupPosition = safeSetupHit.position;
        }

        monster.enabled = true;
        monster.gameObject.SetActive(false);
        setupPosition.z = 0f;
        monster.transform.position = setupPosition;
        monster.gameObject.SetActive(true);
        monster.Setup(definition, context, playerTarget, enemyProjectilePool, onDeath);

        MonsterCrowdAgent crowdAgent = monster.GetComponent<MonsterCrowdAgent>();
        if (crowdAgent == null)
            crowdAgent = monster.gameObject.AddComponent<MonsterCrowdAgent>();
        crowdAgent.Configure(this, playerTarget, startSimpleMovement);

        if (startSimpleMovement)
        {
            spawnPosition.z = 0f;
            monster.transform.position = spawnPosition;
        }

        if (!startSimpleMovement && agent != null && agent.enabled && !agent.isOnNavMesh)
        {
            Debug.LogError(
                $"[MonsterPool] '{definition.displayName}' Setup enabled its NavMeshAgent off-mesh. " +
                "Returning it to the pool to prevent a soft-lock.");
            Return(monster);
            return null;
        }

        return monster;
    }

    private Vector3 ResolveRoomPerimeterSpawn(Vector3 requestedPosition, out bool projectedOutsideBase)
    {
        projectedOutsideBase = false;

        if (!forceSpawnsOutsideBase)
            return requestedPosition;

        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();

        RoomDefinitionSO room = roomManager != null ? roomManager.CurrentRoom : null;
        if (room == null || !room.usePersistentStartBase || !room.forbidMonsterSpawnInsideStartBase)
            return requestedPosition;

        Vector2 origin = GetRoomOriginPosition(roomManager);
        Vector2 local = (Vector2)requestedPosition - origin;
        if (!room.IsInsideStartBaseLocal(local))
            return requestedPosition;

        Vector2 projectedLocal = room.ProjectOutsideStartBaseLocal(local, outsideSpawnDistance);
        Vector2 world = origin + projectedLocal;
        projectedOutsideBase = true;

        return new Vector3(world.x, world.y, requestedPosition.z);
    }

    private bool IsInsideCurrentStartBase(Vector3 worldPosition)
    {
        if (!forceSpawnsOutsideBase)
            return false;

        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();

        RoomDefinitionSO room = roomManager != null ? roomManager.CurrentRoom : null;
        if (room == null || !room.usePersistentStartBase || !room.forbidMonsterSpawnInsideStartBase)
            return false;

        Vector2 origin = GetRoomOriginPosition(roomManager);
        return room.IsInsideStartBaseLocal((Vector2)worldPosition - origin);
    }

    private static Vector2 GetRoomOriginPosition(BattleRoomManager manager)
    {
        if (manager == null)
            return Vector2.zero;

        Transform originTransform = manager.transform.Find("RoomOrigin");
        return originTransform != null
            ? (Vector2)originTransform.position
            : (Vector2)manager.transform.position;
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

    public void RegisterCrowdAgent(MonsterCrowdAgent crowdAgent, Transform playerTarget)
    {
        if (crowdAgent == null)
            return;

        UnregisterCrowdAgent(crowdAgent);

        CrowdGroup group = null;
        int maxPerGroup = Mathf.Max(2, membersPerGroup);
        for (int i = 0; i < crowdGroups.Count; i++)
        {
            CrowdGroup candidate = crowdGroups[i];
            if (candidate != null &&
                candidate.target == playerTarget &&
                candidate.members.Count < maxPerGroup)
            {
                group = candidate;
                break;
            }
        }

        if (group == null)
        {
            group = new CrowdGroup
            {
                id = nextCrowdGroupId++,
                target = playerTarget,
                cachedTarget = playerTarget != null ? (Vector2)playerTarget.position : Vector2.zero
            };
            crowdGroups.Add(group);
        }

        int slot = group.members.Count;
        group.members.Add(crowdAgent);
        crowdMembers[crowdAgent] = new CrowdMemberRecord
        {
            group = group,
            slotOffset = ComputeFormationOffset(slot, maxPerGroup, group.id)
        };
    }

    public void UnregisterCrowdAgent(MonsterCrowdAgent crowdAgent)
    {
        if (crowdAgent == null || !crowdMembers.TryGetValue(crowdAgent, out CrowdMemberRecord record))
            return;

        crowdMembers.Remove(crowdAgent);
        if (record.group != null)
        {
            record.group.members.Remove(crowdAgent);
            if (record.group.members.Count == 0)
                crowdGroups.Remove(record.group);
        }
    }

    public Vector2 GetCrowdMoveTarget(MonsterCrowdAgent crowdAgent, Vector2 fallbackTarget)
    {
        if (crowdAgent == null || !crowdMembers.TryGetValue(crowdAgent, out CrowdMemberRecord record))
            return fallbackTarget;

        CrowdGroup group = record.group;
        if (group == null)
            return fallbackTarget;

        return group.cachedTarget + record.slotOffset;
    }

    public bool ShouldUseSimpleMovement(float distanceToPlayer, bool forcedSimpleEntry)
    {
        if (forcedSimpleEntry)
            return true;

        if (!enableCrowdOptimization)
            return false;

        return crowdMembers.Count > Mathf.Max(1, optimizationThreshold) &&
               distanceToPlayer > PreciseNavDistance;
    }

    public bool ShouldReturnToPreciseMovement(float distanceToPlayer, bool forcedSimpleEntry)
    {
        if (distanceToPlayer <= PreciseNavDistance)
            return true;

        if (forcedSimpleEntry)
            return false;

        if (!enableCrowdOptimization)
            return true;

        return crowdMembers.Count <= Mathf.Max(1, optimizationThreshold);
    }

    public bool TryGetNavAttachPosition(Vector3 currentPosition, out Vector3 resolved)
    {
        resolved = currentPosition;
        if (!NavMesh.SamplePosition(
                currentPosition,
                out NavMeshHit hit,
                Mathf.Max(0.1f, navAttachSampleRadius),
                NavMesh.AllAreas))
        {
            return false;
        }

        resolved = hit.position;
        resolved.z = 0f;
        return true;
    }

    private Vector2 ComputeFormationOffset(int slot, int capacity, int groupId)
    {
        if (groupFormationRadius <= 0f || capacity <= 1)
            return Vector2.zero;

        float normalized = slot / (float)Mathf.Max(1, capacity);
        float angle = normalized * 360f + (groupId * 47f) % 360f;
        float radians = angle * Mathf.Deg2Rad;
        float ring = Mathf.Lerp(0.35f, 1f, (slot % 4) / 3f) * groupFormationRadius;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * ring;
    }

    public void Return(MonsterController monster)
    {
        if (monster == null) return;

        MonsterCrowdAgent crowdAgent = monster.GetComponent<MonsterCrowdAgent>();
        crowdAgent?.PrepareForPool();

        monster.PrepareForPool();
        monster.enabled = true;
        monster.gameObject.SetActive(false);
        monster.transform.SetParent(transform);

        if (!pool.Contains(monster))
            pool.Enqueue(monster);
    }
}

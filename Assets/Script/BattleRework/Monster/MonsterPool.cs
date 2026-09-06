using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Monster pooling + field-entry presentation.
///
/// Spawn contract:
/// - BattleRoomManager asks for monsters only after the new field finished docking and NavMesh was rebuilt.
/// - Get() resolves a reachable spawn point immediately, but the monster stays inactive.
/// - Requests created in the same frame are batched, shuffled, and revealed in a staggered random order.
/// - A visible warning marker telegraphs each spawn before the monster becomes active, giving the player reaction time.
/// - Returning a pending monster cancels its warning/reveal cleanly.
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

    [Header("Field Spawn Telegraph")]
    [Tooltip("필드 조립/베이크가 끝난 뒤 첫 생성 예고가 뜨기까지의 최소 대기입니다.")]
    [SerializeField, Min(0f)] private float firstWarningDelay = 0.18f;
    [Tooltip("몬스터마다 예고가 시작되는 간격 범위입니다. 같은 프레임의 생성 요청은 랜덤 순서로 섞입니다.")]
    [SerializeField] private Vector2 warningStaggerRange = new(0.14f, 0.32f);
    [Tooltip("예고 이펙트가 유지된 뒤 실제 몬스터가 나타나기까지의 시간입니다.")]
    [SerializeField, Min(0.15f)] private float telegraphDuration = 0.68f;
    [SerializeField, Min(0.2f)] private float telegraphWorldSize = 0.95f;
    [SerializeField] private Color telegraphOuterColor = new(1f, 0.28f, 0.18f, 0.82f);
    [SerializeField] private Color telegraphInnerColor = new(1f, 0.82f, 0.30f, 0.95f);
    [SerializeField] private int telegraphSortingOrder = 90;

    private readonly Queue<MonsterController> pool = new();
    private readonly List<PendingSpawn> pendingBatch = new();
    private readonly HashSet<MonsterController> pendingMonsters = new();
    private readonly Dictionary<MonsterController, Coroutine> revealRoutines = new();
    private readonly Dictionary<MonsterController, GameObject> warningObjects = new();

    private Coroutine batchRoutine;
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

    private void OnDisable()
    {
        CancelAllPendingSpawns();
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

    /// <summary>
    /// Reserves a pooled monster immediately so BattleRoomManager can count it as part of the room,
    /// but actual activation is delayed until its telegraph finishes.
    /// </summary>
    public MonsterController Get(
        Vector3 requestedPosition,
        MonsterDefinitionSO definition,
        BattleContext context,
        Transform playerTarget,
        Action<MonsterController> onDeath)
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

        PrepareReservedMonster(monster, spawnPosition);

        pendingMonsters.Add(monster);
        pendingBatch.Add(new PendingSpawn(
            monster,
            definition,
            context,
            playerTarget,
            onDeath,
            spawnPosition));

        if (batchRoutine == null)
            batchRoutine = StartCoroutine(DispatchPendingBatchNextFrame());

        return monster;
    }

    private static void PrepareReservedMonster(MonsterController monster, Vector3 spawnPosition)
    {
        NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
        if (agent != null)
            agent.enabled = false;

        monster.enabled = true;
        monster.gameObject.SetActive(false);
        spawnPosition.z = 0f;
        monster.transform.position = spawnPosition;
    }

    /// <summary>
    /// BattleRoomManager submits all fixed spawns synchronously after NavMesh bake.
    /// Waiting one frame lets us collect that whole wave, shuffle it, then assign readable stagger slots.
    /// </summary>
    private IEnumerator DispatchPendingBatchNextFrame()
    {
        yield return null;

        List<PendingSpawn> batch = new(pendingBatch);
        pendingBatch.Clear();
        batchRoutine = null;

        for (int i = batch.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (batch[i], batch[j]) = (batch[j], batch[i]);
        }

        float accumulatedDelay = Mathf.Max(0f, firstWarningDelay);
        float minStagger = Mathf.Max(0.02f, Mathf.Min(warningStaggerRange.x, warningStaggerRange.y));
        float maxStagger = Mathf.Max(minStagger, Mathf.Max(warningStaggerRange.x, warningStaggerRange.y));

        for (int i = 0; i < batch.Count; i++)
        {
            PendingSpawn request = batch[i];
            if (request.monster == null || !pendingMonsters.Contains(request.monster))
                continue;

            if (i > 0)
                accumulatedDelay += UnityEngine.Random.Range(minStagger, maxStagger);

            Coroutine routine = StartCoroutine(RevealMonsterRoutine(request, accumulatedDelay));
            revealRoutines[request.monster] = routine;
        }
    }

    private IEnumerator RevealMonsterRoutine(PendingSpawn request, float warningDelay)
    {
        MonsterController monster = request.monster;
        if (monster == null)
            yield break;

        if (warningDelay > 0f)
            yield return new WaitForSeconds(warningDelay);

        if (monster == null || !pendingMonsters.Contains(monster))
            yield break;

        GameObject warning = CreateSpawnWarning(request.spawnPosition);
        if (warning != null)
            warningObjects[monster] = warning;

        float duration = Mathf.Max(0.15f, telegraphDuration);
        float elapsed = 0f;
        SpriteRenderer warningRenderer = warning != null ? warning.GetComponent<SpriteRenderer>() : null;
        Vector3 baseScale = warning != null ? warning.transform.localScale : Vector3.one;

        while (elapsed < duration)
        {
            if (monster == null || !pendingMonsters.Contains(monster))
            {
                DestroyWarning(monster);
                yield break;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float pulse = 0.82f + Mathf.Sin(t * Mathf.PI * 5f) * 0.10f + t * 0.18f;

            if (warning != null)
            {
                warning.transform.localScale = baseScale * pulse;
                warning.transform.Rotate(0f, 0f, 180f * Time.deltaTime);
            }

            if (warningRenderer != null)
            {
                Color color = Color.Lerp(telegraphOuterColor, telegraphInnerColor, t);
                color.a *= Mathf.Lerp(0.68f, 1f, Mathf.PingPong(t * 3f, 1f));
                warningRenderer.color = color;
            }

            yield return null;
        }

        DestroyWarning(monster);
        if (monster == null || !pendingMonsters.Remove(monster))
            yield break;

        revealRoutines.Remove(monster);

        monster.gameObject.SetActive(true);
        monster.Setup(
            request.definition,
            request.context,
            request.playerTarget,
            enemyProjectilePool,
            request.onDeath);

        ApplyGeneratedTestSizing(monster, request.definition);

        NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled && !agent.isOnNavMesh)
        {
            Debug.LogError(
                $"[MonsterPool] '{request.definition.displayName}' appeared off NavMesh after telegraph. Returning it to pool.");
            // This should be prevented by TryResolveSpawnPosition. Keep the object safe if prefab Setup changes.
            Return(monster);
        }
    }

    private GameObject CreateSpawnWarning(Vector3 position)
    {
        GameObject go = new("MonsterSpawnTelegraph");
        go.transform.SetParent(transform, true);
        go.transform.position = new Vector3(position.x, position.y, 0f);

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = MonsterSpawnRuntimeSpriteCache.WarningRing;
        renderer.color = telegraphOuterColor;
        renderer.sortingOrder = telegraphSortingOrder;

        Vector2 spriteSize = renderer.sprite != null ? renderer.sprite.bounds.size : Vector2.one;
        float max = Mathf.Max(0.001f, Mathf.Max(spriteSize.x, spriteSize.y));
        float scale = Mathf.Max(0.01f, telegraphWorldSize / max);
        go.transform.localScale = new Vector3(scale, scale, 1f);
        return go;
    }

    private void DestroyWarning(MonsterController monster)
    {
        if (monster == null)
            return;

        if (!warningObjects.TryGetValue(monster, out GameObject warning))
            return;

        warningObjects.Remove(monster);
        if (warning != null)
            Destroy(warning);
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
                : UnityEngine.Random.insideUnitCircle * retryScatterRadius;

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

    private static void ApplyGeneratedTestSizing(MonsterController monster, MonsterDefinitionSO definition)
    {
        if (monster == null || definition == null)
            return;

        if (string.IsNullOrEmpty(definition.monsterId) || !definition.monsterId.StartsWith("TEST_"))
            return;

        monster.transform.localScale = Vector3.one * 1.6f;

        CircleCollider2D circle = monster.GetComponent<CircleCollider2D>();
        if (circle != null)
            circle.radius = Mathf.Max(circle.radius, 0.46f);
    }

    public void Return(MonsterController monster)
    {
        if (monster == null)
            return;

        CancelPendingSpawn(monster);
        monster.PrepareForPool();
        monster.gameObject.SetActive(false);
        monster.transform.SetParent(transform);

        if (!pool.Contains(monster))
            pool.Enqueue(monster);
    }

    private void CancelPendingSpawn(MonsterController monster)
    {
        if (monster == null)
            return;

        pendingMonsters.Remove(monster);
        for (int i = pendingBatch.Count - 1; i >= 0; i--)
        {
            if (pendingBatch[i].monster == monster)
                pendingBatch.RemoveAt(i);
        }

        if (revealRoutines.TryGetValue(monster, out Coroutine routine))
        {
            revealRoutines.Remove(monster);
            if (routine != null)
                StopCoroutine(routine);
        }

        DestroyWarning(monster);
    }

    private void CancelAllPendingSpawns()
    {
        if (batchRoutine != null)
        {
            StopCoroutine(batchRoutine);
            batchRoutine = null;
        }

        List<MonsterController> monsters = new(pendingMonsters);
        for (int i = 0; i < monsters.Count; i++)
            CancelPendingSpawn(monsters[i]);

        pendingBatch.Clear();
        pendingMonsters.Clear();
    }

    private readonly struct PendingSpawn
    {
        public readonly MonsterController monster;
        public readonly MonsterDefinitionSO definition;
        public readonly BattleContext context;
        public readonly Transform playerTarget;
        public readonly Action<MonsterController> onDeath;
        public readonly Vector3 spawnPosition;

        public PendingSpawn(
            MonsterController monster,
            MonsterDefinitionSO definition,
            BattleContext context,
            Transform playerTarget,
            Action<MonsterController> onDeath,
            Vector3 spawnPosition)
        {
            this.monster = monster;
            this.definition = definition;
            this.context = context;
            this.playerTarget = playerTarget;
            this.onDeath = onDeath;
            this.spawnPosition = spawnPosition;
        }
    }
}

internal static class MonsterSpawnRuntimeSpriteCache
{
    private static Sprite warningRing;
    public static Sprite WarningRing => warningRing != null ? warningRing : warningRing = CreateWarningRing();

    private static Sprite CreateWarningRing()
    {
        const int pixels = 32;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            name = "RuntimeMonsterSpawnWarning",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Vector2 center = new((pixels - 1) * 0.5f, (pixels - 1) * 0.5f);
        for (int y = 0; y < pixels; y++)
        {
            for (int x = 0; x < pixels; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                bool outer = d >= 10.2f && d <= 13.4f;
                bool inner = d >= 5.6f && d <= 7.1f;
                bool cross = (Mathf.Abs(x - center.x) <= 0.8f || Mathf.Abs(y - center.y) <= 0.8f) && d <= 9.4f;
                texture.SetPixel(x, y, outer || inner || cross ? Color.white : Color.clear);
            }
        }

        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, pixels, pixels),
            new Vector2(0.5f, 0.5f),
            pixels,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "RuntimeMonsterSpawnWarning";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}

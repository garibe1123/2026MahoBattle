using System;
using System.Collections;
using System.Collections.Generic;
using NavMeshPlus.Components;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Room Lifecycle:
/// Build Blocks -> impact presentation -> Build NavMesh -> Spawn Fixed Monsters -> Combat -> Cleared
/// -> (external reward flow) -> OpenExit -> Highlight Pad -> Exit Blocks.
///
/// MapBlock 충돌 연출의 중앙 관리자 역할도 담당합니다.
/// 실제 Impact/Dust Sprite가 비어 있으면 코드 기반 Dummy VFX를 자동 사용합니다.
/// </summary>
public class BattleRoomManager : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Transform roomOrigin;
    [SerializeField] private Transform mapRoot;
    [SerializeField] private Transform obstacleRoot;
    [SerializeField] private Transform monsterRoot;
    [SerializeField] private Transform playerTarget;
    [SerializeField] private NavMeshSurface navSurface;
    [SerializeField] private MonsterPool monsterPool;

    [Header("Map Assembly Timing")]
    [Tooltip("RoomDefinitionSO의 Block 배열 순서대로 진입 시차를 줍니다.")]
    [SerializeField] private float blockEntryStagger = 0.055f;
    [SerializeField] private float maxBlockEntryStagger = 0.40f;

    [Header("Map Impact VFX - Sprite가 없으면 Dummy")]
    [SerializeField] private Transform impactVfxRoot;
    [Tooltip("블록이 쾅 하고 고정되는 순간의 Sprite Animation. 비어 있으면 코드 Dummy Flash/Ring.")]
    [SerializeField] private Sprite[] impactSprites;
    [Tooltip("충돌 먼지 Sprite Animation. 비어 있으면 코드 Dummy Dust.")]
    [SerializeField] private Sprite[] dustSprites;
    [SerializeField, Min(1f)] private float impactVfxFps = 16f;
    [SerializeField, Min(0.05f)] private float impactVfxWorldScale = 1f;
    [SerializeField] private Material impactVfxMaterial;
    [SerializeField] private int impactVfxSortingOrder = 90;
    [SerializeField] private Color dummyImpactColor = new(1f, 0.92f, 0.72f, 1f);
    [SerializeField] private Color dummyDustColor = new(0.70f, 0.67f, 0.60f, 0.85f);
    [SerializeField, Min(1f)] private float finalBlockImpactMultiplier = 1.45f;

    [Header("Map Impact Camera Shake")]
    [Tooltip("권장: MainCamera 자체보다 CameraShakePivot 같은 전용 부모 Transform을 지정하세요. null이면 MainCamera를 fallback으로 사용합니다.")]
    [SerializeField] private Transform cameraShakeTarget;
    [SerializeField, Min(0f)] private float cameraShakeAmplitude = 0.075f;
    [SerializeField, Min(0.01f)] private float cameraShakeDuration = 0.12f;
    [SerializeField, Min(0f)] private float maxCameraShakeAmplitude = 0.16f;
    [SerializeField, Min(0.01f)] private float maxCameraShakeDuration = 0.24f;

    [Header("Optional Impact Sound")]
    [SerializeField] private AudioSource impactAudioSource;
    [SerializeField] private AudioClip impactClip;
    [SerializeField, Range(0f, 1f)] private float impactVolume = 0.55f;
    [SerializeField, Range(0f, 0.3f)] private float impactPitchRandomness = 0.06f;

    private readonly List<MapBlock> activeBlocks = new();
    private readonly List<BattleObstacle> activeObstacles = new();
    private readonly List<MonsterController> activeMonsters = new();

    private RoomDefinitionSO currentRoom;
    private BattleContext currentContext;
    private bool roomTransitioning;
    private bool combatCleared;
    private bool exitOpened;
    private bool exitRoutineStarted;
    private Coroutine navMeshRebuildRoutine;

    private int expectedAssemblyImpacts;
    private int receivedAssemblyImpacts;

    private Coroutine cameraShakeRoutine;
    private Transform activeShakeTarget;
    private Vector3 lastCameraShakeOffset;
    private float requestedShakeAmplitude;
    private float requestedShakeEndTime;

    public event Action<RoomDefinitionSO> RoomCombatStarted;
    public event Action<RoomDefinitionSO> RoomCombatCleared;
    public event Action<RoomDefinitionSO> RoomExited;
    public event Action<MonsterController> MonsterDefeated;

    public RoomDefinitionSO CurrentRoom => currentRoom;
    public bool IsRoomActive => currentRoom != null;
    public bool IsTransitioning => roomTransitioning;
    public bool IsCombatCleared => combatCleared;
    public bool IsExitOpen => exitOpened;
    public int AliveMonsterCount => activeMonsters.Count;

    private void Awake()
    {
        if (roomOrigin == null)
            roomOrigin = transform;

        if (impactVfxRoot == null)
            impactVfxRoot = mapRoot != null ? mapRoot : transform;
    }

    private void OnDisable()
    {
        StopCameraShakeImmediate();
    }

    public bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();

        if (roomOrigin == null) errors.Add("roomOrigin is null");
        if (navSurface == null) errors.Add("navSurface is null");
        if (monsterPool == null) errors.Add("monsterPool is null");
        if (playerTarget == null) errors.Add("playerTarget is null");

        report = string.Join("\n", errors);
        return errors.Count == 0;
    }

    public void EnterRoom(RoomDefinitionSO room, BattleContext context)
    {
        if (room == null)
        {
            Debug.LogError("[BattleRoom] EnterRoom called with null RoomDefinitionSO.");
            return;
        }

        if (roomTransitioning)
        {
            Debug.LogWarning("[BattleRoom] Room transition is already in progress.");
            return;
        }

        StartCoroutine(EnterRoomRoutine(room, context));
    }

    private IEnumerator EnterRoomRoutine(RoomDefinitionSO room, BattleContext context)
    {
        roomTransitioning = true;

        if (currentRoom != null || activeBlocks.Count > 0 || activeMonsters.Count > 0)
            ClearImmediate();

        ResetRoomFlags();
        roomTransitioning = true;
        currentRoom = room;
        currentContext = context;

        RepositionPlayerForRoom(room);

        float longestEntry = BuildMapBlocks(room);
        BuildObstacles(room);

        if (longestEntry > 0f)
            yield return new WaitForSeconds(longestEntry);

        // Tween의 부동소수 오차가 NavMesh Source에 남지 않도록 모든 블록은 이미 MapBlock에서 최종 Snap됩니다.
        RebuildNavMesh();
        SpawnFixedMonsters(room);

        roomTransitioning = false;
        RoomCombatStarted?.Invoke(room);

        if (activeMonsters.Count == 0)
            HandleCombatCleared();
    }

    private void RepositionPlayerForRoom(RoomDefinitionSO room)
    {
        if (room == null || playerTarget == null || !room.repositionPlayerOnEnter)
            return;

        Vector3 destination = roomOrigin.position + (Vector3)room.playerEntryOffset;
        destination.z = playerTarget.position.z;
        playerTarget.position = destination;

        Rigidbody2D playerBody = playerTarget.GetComponent<Rigidbody2D>();
        if (playerBody == null)
            playerBody = playerTarget.GetComponentInChildren<Rigidbody2D>();

        if (playerBody != null)
        {
            playerBody.linearVelocity = Vector2.zero;
            playerBody.angularVelocity = 0f;
        }
    }

    private float BuildMapBlocks(RoomDefinitionSO room)
    {
        float longest = 0f;
        expectedAssemblyImpacts = 0;
        receivedAssemblyImpacts = 0;

        if (room.blocks == null)
            return longest;

        // 마지막 충돌을 정확히 판별하기 위해 먼저 Impact 대상 수를 계산합니다.
        for (int i = 0; i < room.blocks.Count; i++)
        {
            MapBlockPlacement placement = room.blocks[i];
            if (placement != null && placement.prefab != null && placement.prefab.WillImpact)
                expectedAssemblyImpacts++;
        }

        int validIndex = 0;
        for (int i = 0; i < room.blocks.Count; i++)
        {
            MapBlockPlacement placement = room.blocks[i];
            if (placement == null || placement.prefab == null) continue;

            Transform parent = mapRoot != null ? mapRoot : transform;
            MapBlock block = Instantiate(placement.prefab, parent);

            Vector3 destination = roomOrigin.position + new Vector3(
                placement.gridPosition.x * MapBlock.BlockWorldSize.x,
                placement.gridPosition.y * MapBlock.BlockWorldSize.y,
                0f);

            float delay = Mathf.Min(
                Mathf.Max(0f, maxBlockEntryStagger),
                Mathf.Max(0f, blockEntryStagger) * validIndex);

            if (block.WillImpact)
                block.Impacted += HandleMapBlockImpact;

            block.PlayEnter(destination, placement.entryDirection, delay);
            activeBlocks.Add(block);
            longest = Mathf.Max(longest, block.GetEntryDuration(delay));
            validIndex++;
        }

        return longest;
    }

    private void HandleMapBlockImpact(
        MapBlock block,
        Vector3 impactPosition,
        Vector2 travelDirection,
        float blockStrength)
    {
        if (block == null)
            return;

        receivedAssemblyImpacts++;
        bool finalImpact = expectedAssemblyImpacts > 0 &&
                           receivedAssemblyImpacts >= expectedAssemblyImpacts;

        float finalMultiplier = finalImpact
            ? Mathf.Max(1f, finalBlockImpactMultiplier)
            : 1f;

        float strength = Mathf.Max(0.05f, blockStrength) * finalMultiplier;

        SpawnMapImpactVfx(
            impactPosition,
            travelDirection,
            strength,
            finalImpact);

        RequestCameraShake(strength, finalImpact);
        PlayImpactSound(strength, finalImpact);
    }

    private void SpawnMapImpactVfx(
        Vector3 position,
        Vector2 travelDirection,
        float strength,
        bool finalImpact)
    {
        Transform parent = impactVfxRoot != null
            ? impactVfxRoot
            : (mapRoot != null ? mapRoot : transform);

        GameObject go = new(finalImpact ? "MapImpactVFX_Final" : "MapImpactVFX");
        go.transform.SetParent(parent, true);
        go.transform.position = position;

        MapImpactVfxInstance instance = go.AddComponent<MapImpactVfxInstance>();
        instance.Play(
            impactSprites,
            dustSprites,
            impactVfxFps,
            impactVfxWorldScale * Mathf.Lerp(0.85f, 1.25f, Mathf.Clamp01(strength)),
            impactVfxMaterial,
            impactVfxSortingOrder,
            dummyImpactColor,
            dummyDustColor,
            travelDirection,
            finalImpact);
    }

    private void PlayImpactSound(float strength, bool finalImpact)
    {
        if (impactAudioSource == null || impactClip == null)
            return;

        float oldPitch = impactAudioSource.pitch;
        float randomPitch = UnityEngine.Random.Range(
            -impactPitchRandomness,
            impactPitchRandomness);

        impactAudioSource.pitch = Mathf.Clamp(1f + randomPitch, 0.5f, 2f);
        float volume = impactVolume * Mathf.Clamp(strength, 0.35f, finalImpact ? 1.35f : 1f);
        impactAudioSource.PlayOneShot(impactClip, Mathf.Clamp01(volume));
        impactAudioSource.pitch = oldPitch;
    }

    private void RequestCameraShake(float strength, bool finalImpact)
    {
        Transform target = ResolveCameraShakeTarget();
        if (target == null || cameraShakeAmplitude <= 0f)
            return;

        activeShakeTarget = target;

        float amplitude = cameraShakeAmplitude * Mathf.Max(0.1f, strength);
        if (finalImpact)
            amplitude *= 1.15f;

        requestedShakeAmplitude = Mathf.Clamp(
            Mathf.Max(requestedShakeAmplitude, amplitude),
            0f,
            Mathf.Max(cameraShakeAmplitude, maxCameraShakeAmplitude));

        float duration = cameraShakeDuration * (finalImpact ? 1.25f : 1f);
        duration = Mathf.Min(Mathf.Max(0.01f, duration), Mathf.Max(0.01f, maxCameraShakeDuration));
        requestedShakeEndTime = Mathf.Max(
            requestedShakeEndTime,
            Time.unscaledTime + duration);

        if (cameraShakeRoutine == null)
            cameraShakeRoutine = StartCoroutine(CameraShakeRoutine());
    }

    private Transform ResolveCameraShakeTarget()
    {
        if (cameraShakeTarget != null)
            return cameraShakeTarget;

        Camera main = Camera.main;
        return main != null ? main.transform : null;
    }

    private IEnumerator CameraShakeRoutine()
    {
        lastCameraShakeOffset = Vector3.zero;

        while (activeShakeTarget != null && Time.unscaledTime < requestedShakeEndTime)
        {
            // 이전 프레임에 우리가 더했던 Offset만 제거한 뒤 새 Offset을 더합니다.
            activeShakeTarget.localPosition -= lastCameraShakeOffset;

            float remaining = Mathf.Max(0f, requestedShakeEndTime - Time.unscaledTime);
            float fadeWindow = Mathf.Max(0.01f, cameraShakeDuration);
            float fade = Mathf.Clamp01(remaining / fadeWindow);

            Vector2 random = UnityEngine.Random.insideUnitCircle;
            lastCameraShakeOffset = new Vector3(
                random.x,
                random.y,
                0f) * requestedShakeAmplitude * fade;

            activeShakeTarget.localPosition += lastCameraShakeOffset;
            yield return null;
        }

        if (activeShakeTarget != null)
            activeShakeTarget.localPosition -= lastCameraShakeOffset;

        lastCameraShakeOffset = Vector3.zero;
        requestedShakeAmplitude = 0f;
        requestedShakeEndTime = 0f;
        activeShakeTarget = null;
        cameraShakeRoutine = null;
    }

    private void StopCameraShakeImmediate()
    {
        if (cameraShakeRoutine != null)
        {
            StopCoroutine(cameraShakeRoutine);
            cameraShakeRoutine = null;
        }

        if (activeShakeTarget != null)
            activeShakeTarget.localPosition -= lastCameraShakeOffset;

        lastCameraShakeOffset = Vector3.zero;
        requestedShakeAmplitude = 0f;
        requestedShakeEndTime = 0f;
        activeShakeTarget = null;
    }

    private void BuildObstacles(RoomDefinitionSO room)
    {
        if (room.obstacles == null)
            return;

        for (int i = 0; i < room.obstacles.Count; i++)
        {
            ObstaclePlacement placement = room.obstacles[i];
            if (placement == null || placement.prefab == null) continue;

            Transform parent = obstacleRoot != null ? obstacleRoot : transform;
            Vector3 position = roomOrigin.position + (Vector3)placement.localPosition;
            Quaternion rotation = Quaternion.Euler(0f, 0f, placement.rotationZ);

            BattleObstacle obstacle = Instantiate(placement.prefab, position, rotation, parent);
            obstacle.Broken += HandleObstacleBroken;
            activeObstacles.Add(obstacle);
        }
    }

    private void HandleObstacleBroken(BattleObstacle obstacle)
    {
        if (obstacle == null || currentRoom == null)
            return;

        if (navMeshRebuildRoutine != null)
            StopCoroutine(navMeshRebuildRoutine);

        navMeshRebuildRoutine = StartCoroutine(RebuildNavMeshNextFrame());
    }

    private IEnumerator RebuildNavMeshNextFrame()
    {
        yield return null;
        navMeshRebuildRoutine = null;

        if (currentRoom != null)
            RebuildNavMesh();
    }

    private void SpawnFixedMonsters(RoomDefinitionSO room)
    {
        if (monsterPool == null || playerTarget == null)
        {
            Debug.LogError("[BattleRoom] Cannot spawn monsters: MonsterPool or playerTarget is missing.");
            return;
        }

        if (room.monsterSpawns == null)
            return;

        int requestedCount = 0;
        int spawnedCount = 0;

        for (int i = 0; i < room.monsterSpawns.Count; i++)
        {
            MonsterSpawnEntry entry = room.monsterSpawns[i];
            if (entry == null || entry.monster == null) continue;

            int count = Mathf.Max(1, entry.count);
            requestedCount += count;

            for (int c = 0; c < count; c++)
            {
                Vector2 scatter = entry.scatterRadius > 0f
                    ? UnityEngine.Random.insideUnitCircle * entry.scatterRadius
                    : Vector2.zero;

                Vector3 position = roomOrigin.position + (Vector3)(entry.localPosition + scatter);
                MonsterController monster = monsterPool.Get(
                    position,
                    entry.monster,
                    currentContext,
                    playerTarget,
                    HandleMonsterDeath);

                if (monster == null)
                {
                    Debug.LogError($"[BattleRoom] Failed to spawn monster '{entry.monster.name}'. It will not be added to alive count.");
                    continue;
                }

                Transform parent = monsterRoot != null ? monsterRoot : transform;
                monster.transform.SetParent(parent);
                activeMonsters.Add(monster);
                spawnedCount++;
            }
        }

        if (requestedCount > 0 && spawnedCount == 0)
        {
            Debug.LogError(
                $"[BattleRoom] Room '{room.roomId}' requested {requestedCount} monsters but none could spawn. " +
                "The Room will clear for diagnostic safety instead of soft-locking.");
        }
    }

    private void HandleMonsterDeath(MonsterController monster)
    {
        if (monster == null) return;
        if (!activeMonsters.Remove(monster)) return;

        MonsterDefeated?.Invoke(monster);
        monsterPool?.Return(monster);

        if (activeMonsters.Count == 0)
            HandleCombatCleared();
    }

    private void HandleCombatCleared()
    {
        if (currentRoom == null || combatCleared)
            return;

        combatCleared = true;
        RoomCombatCleared?.Invoke(currentRoom);
    }

    public void OpenExit()
    {
        if (currentRoom == null)
        {
            Debug.LogWarning("[BattleRoom] OpenExit ignored because there is no active Room.");
            return;
        }

        if (!combatCleared)
        {
            Debug.LogWarning("[BattleRoom] OpenExit ignored because combat is not cleared yet.");
            return;
        }

        if (exitOpened || exitRoutineStarted)
            return;

        exitOpened = true;
        SpawnHighlightPad();
    }

    private void SpawnHighlightPad()
    {
        if (currentRoom.highlightBlockPrefab == null)
        {
            Debug.LogWarning($"[BattleRoom] Room '{currentRoom.roomId}' has no highlightBlockPrefab. Auto-exiting for test safety.");
            StartCoroutine(ExitRoomRoutine());
            return;
        }

        Transform parent = mapRoot != null ? mapRoot : transform;
        MapBlock highlight = Instantiate(currentRoom.highlightBlockPrefab, parent);
        Vector3 destination = roomOrigin.position + (Vector3)currentRoom.highlightBlockOffset;

        // Highlight는 Room 조립 충격 카운트에는 포함하지 않지만 자체 반동 연출은 그대로 사용합니다.
        highlight.PlayEnter(destination, Vector2.down);
        activeBlocks.Add(highlight);

        RoomExitPad exitPad = highlight.GetComponent<RoomExitPad>();
        if (exitPad != null && playerTarget != null)
        {
            exitPad.Arm(playerTarget, () => StartCoroutine(ExitRoomRoutine()));
        }
        else
        {
            Debug.LogWarning(
                $"[BattleRoom] Highlight prefab '{currentRoom.highlightBlockPrefab.name}' has no RoomExitPad. " +
                "Auto-exiting after its entry animation so the test run does not soft-lock.");
            StartCoroutine(AutoExitAfterHighlight(highlight.EntryDuration));
        }
    }

    private IEnumerator AutoExitAfterHighlight(float delay)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delay));
        yield return ExitRoomRoutine();
    }

    private IEnumerator ExitRoomRoutine()
    {
        if (exitRoutineStarted)
            yield break;

        exitRoutineStarted = true;
        roomTransitioning = true;

        float longestExit = 0f;

        for (int i = 0; i < activeBlocks.Count; i++)
        {
            MapBlock block = activeBlocks[i];
            if (block == null) continue;

            Vector2 direction = ((Vector2)block.transform.position - (Vector2)roomOrigin.position).normalized;
            block.PlayExit(direction);
            longestExit = Mathf.Max(longestExit, block.ExitDuration);
        }

        if (longestExit > 0f)
            yield return new WaitForSeconds(longestExit);

        RoomDefinitionSO finishedRoom = currentRoom;
        ClearImmediate();
        ResetRoomFlags();
        RoomExited?.Invoke(finishedRoom);
    }

    public void AbortRoom()
    {
        StopCameraShakeImmediate();
        StopAllCoroutines();
        navMeshRebuildRoutine = null;
        ClearImmediate();
        ResetRoomFlags();
    }

    private void RebuildNavMesh()
    {
        if (navSurface == null)
        {
            Debug.LogError("[BattleRoom] NavMeshSurface is missing.");
            return;
        }

        List<NavMeshAgent> agentsToRestore = new();

        for (int i = 0; i < activeMonsters.Count; i++)
        {
            MonsterController monster = activeMonsters[i];
            if (monster == null || !monster.gameObject.activeInHierarchy) continue;

            NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
            if (agent == null || !agent.enabled) continue;

            agentsToRestore.Add(agent);
            agent.enabled = false;
        }

        navSurface.RemoveData();
        navSurface.BuildNavMesh();

        for (int i = 0; i < agentsToRestore.Count; i++)
        {
            NavMeshAgent agent = agentsToRestore[i];
            if (agent == null || !agent.gameObject.activeInHierarchy) continue;

            if (NavMesh.SamplePosition(agent.transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                agent.transform.position = hit.position;
                agent.enabled = true;
                if (agent.isOnNavMesh)
                    agent.isStopped = false;
            }
            else
            {
                Debug.LogWarning($"[BattleRoom] Could not restore NavMeshAgent after rebuild: {agent.name}");
            }
        }
    }

    private void ClearImmediate()
    {
        for (int i = 0; i < activeMonsters.Count; i++)
        {
            if (activeMonsters[i] != null)
                monsterPool?.Return(activeMonsters[i]);
        }
        activeMonsters.Clear();

        for (int i = 0; i < activeObstacles.Count; i++)
        {
            BattleObstacle obstacle = activeObstacles[i];
            if (obstacle == null) continue;

            obstacle.Broken -= HandleObstacleBroken;
            Destroy(obstacle.gameObject);
        }
        activeObstacles.Clear();

        for (int i = 0; i < activeBlocks.Count; i++)
        {
            MapBlock block = activeBlocks[i];
            if (block == null) continue;

            block.Impacted -= HandleMapBlockImpact;
            Destroy(block.gameObject);
        }
        activeBlocks.Clear();

        expectedAssemblyImpacts = 0;
        receivedAssemblyImpacts = 0;
        currentRoom = null;
        currentContext = null;
    }

    private void ResetRoomFlags()
    {
        roomTransitioning = false;
        combatCleared = false;
        exitOpened = false;
        exitRoutineStarted = false;
    }
}

/// <summary>
/// BattleRoomManager가 충돌 순간에 런타임 생성하는 일회성 VFX Player.
/// 실제 Sprite 배열이 있으면 Sprite Animation을 사용하고,
/// 각 배열이 비어 있으면 그 부분만 코드 생성 Dummy VFX로 fallback 합니다.
/// </summary>
internal sealed class MapImpactVfxInstance : MonoBehaviour
{
    private SpriteRenderer impactRenderer;
    private SpriteRenderer flashRenderer;
    private SpriteRenderer dustRenderer;

    private Sprite[] impactFrames;
    private Sprite[] dustFrames;
    private float fps;
    private float elapsed;
    private float duration;
    private float worldScale;
    private bool dummyImpact;
    private bool dummyDust;
    private Color impactColor;
    private Color dustColor;

    private readonly List<Transform> dummyDustTransforms = new();
    private readonly List<SpriteRenderer> dummyDustRenderers = new();
    private readonly List<Vector2> dummyDustVelocities = new();
    private readonly List<float> dummyDustSpin = new();

    public void Play(
        Sprite[] realImpactFrames,
        Sprite[] realDustFrames,
        float animationFps,
        float scale,
        Material material,
        int sortingOrder,
        Color fallbackImpactColor,
        Color fallbackDustColor,
        Vector2 travelDirection,
        bool finalImpact)
    {
        impactFrames = realImpactFrames;
        dustFrames = realDustFrames;
        fps = Mathf.Max(1f, animationFps);
        worldScale = Mathf.Max(0.05f, scale) * (finalImpact ? 1.12f : 1f);
        impactColor = fallbackImpactColor;
        dustColor = fallbackDustColor;

        dummyImpact = impactFrames == null || impactFrames.Length == 0;
        dummyDust = dustFrames == null || dustFrames.Length == 0;

        Vector2 direction = travelDirection.sqrMagnitude > 0.001f
            ? travelDirection.normalized
            : Vector2.down;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        CreateImpactVisual(material, sortingOrder);
        CreateDustVisual(material, sortingOrder - 1, direction, finalImpact);

        float impactDuration = dummyImpact
            ? 0.34f
            : impactFrames.Length / fps;
        float dustDuration = dummyDust
            ? 0.48f
            : dustFrames.Length / fps;

        duration = Mathf.Max(0.18f, impactDuration, dustDuration);
    }

    private void CreateImpactVisual(Material material, int sortingOrder)
    {
        GameObject impactGo = new("Impact");
        impactGo.transform.SetParent(transform, false);
        impactRenderer = impactGo.AddComponent<SpriteRenderer>();
        impactRenderer.sortingOrder = sortingOrder;
        if (material != null)
            impactRenderer.sharedMaterial = material;

        if (!dummyImpact)
        {
            impactRenderer.sprite = impactFrames[0];
            impactGo.transform.localScale = Vector3.one * worldScale;
            return;
        }

        impactRenderer.sprite = MapImpactDummySpriteCache.Ring;
        impactRenderer.color = impactColor;
        impactGo.transform.localScale = Vector3.one * worldScale * 0.30f;

        GameObject flashGo = new("Flash");
        flashGo.transform.SetParent(transform, false);
        flashRenderer = flashGo.AddComponent<SpriteRenderer>();
        flashRenderer.sprite = MapImpactDummySpriteCache.Flash;
        flashRenderer.color = impactColor;
        flashRenderer.sortingOrder = sortingOrder + 1;
        if (material != null)
            flashRenderer.sharedMaterial = material;
        flashGo.transform.localScale = Vector3.one * worldScale * 0.65f;
    }

    private void CreateDustVisual(
        Material material,
        int sortingOrder,
        Vector2 travelDirection,
        bool finalImpact)
    {
        if (!dummyDust)
        {
            GameObject dustGo = new("Dust");
            dustGo.transform.SetParent(transform, false);
            dustRenderer = dustGo.AddComponent<SpriteRenderer>();
            dustRenderer.sprite = dustFrames[0];
            dustRenderer.sortingOrder = sortingOrder;
            if (material != null)
                dustRenderer.sharedMaterial = material;
            dustGo.transform.localScale = Vector3.one * worldScale;
            return;
        }

        int count = finalImpact ? 9 : 6;
        Vector2 backward = -travelDirection;
        if (backward.sqrMagnitude <= 0.001f)
            backward = Vector2.up;

        for (int i = 0; i < count; i++)
        {
            GameObject dustGo = new($"Dust_{i}");
            dustGo.transform.SetParent(transform, false);

            SpriteRenderer renderer = dustGo.AddComponent<SpriteRenderer>();
            renderer.sprite = MapImpactDummySpriteCache.Dust;
            renderer.color = dustColor;
            renderer.sortingOrder = sortingOrder;
            if (material != null)
                renderer.sharedMaterial = material;

            float spread = count <= 1
                ? 0f
                : Mathf.Lerp(-70f, 70f, i / (float)(count - 1));
            float randomJitter = UnityEngine.Random.Range(-16f, 16f);
            Vector2 dir = Rotate(backward, spread + randomJitter);
            float speed = UnityEngine.Random.Range(0.9f, 2.1f) * worldScale;

            dustGo.transform.localScale = Vector3.one * worldScale *
                                          UnityEngine.Random.Range(0.12f, 0.28f);

            dummyDustTransforms.Add(dustGo.transform);
            dummyDustRenderers.Add(renderer);
            dummyDustVelocities.Add(dir * speed);
            dummyDustSpin.Add(UnityEngine.Random.Range(-280f, 280f));
        }
    }

    private void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);

        if (dummyImpact)
            UpdateDummyImpact(t);
        else
            UpdateRealAnimation(impactRenderer, impactFrames);

        if (dummyDust)
            UpdateDummyDust(t);
        else
            UpdateRealAnimation(dustRenderer, dustFrames);

        if (elapsed >= duration)
            Destroy(gameObject);
    }

    private void UpdateRealAnimation(SpriteRenderer renderer, Sprite[] frames)
    {
        if (renderer == null || frames == null || frames.Length == 0)
            return;

        int index = Mathf.Min(
            frames.Length - 1,
            Mathf.FloorToInt(elapsed * fps));
        renderer.sprite = frames[index];
    }

    private void UpdateDummyImpact(float t)
    {
        if (impactRenderer != null)
        {
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            impactRenderer.transform.localScale = Vector3.one * worldScale *
                                                  Mathf.Lerp(0.30f, 1.55f, eased);
            Color color = impactColor;
            color.a *= 1f - t;
            impactRenderer.color = color;
        }

        if (flashRenderer != null)
        {
            float flashT = Mathf.Clamp01(t * 2.7f);
            flashRenderer.transform.localScale = Vector3.one * worldScale *
                                                 Mathf.Lerp(0.55f, 1.18f, flashT);
            Color color = impactColor;
            color.a *= 1f - flashT;
            flashRenderer.color = color;
        }
    }

    private void UpdateDummyDust(float t)
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < dummyDustTransforms.Count; i++)
        {
            Transform dust = dummyDustTransforms[i];
            SpriteRenderer renderer = dummyDustRenderers[i];
            if (dust == null || renderer == null) continue;

            Vector2 velocity = dummyDustVelocities[i];
            velocity *= Mathf.Pow(0.12f, dt);
            dummyDustVelocities[i] = velocity;

            dust.position += (Vector3)(velocity * dt);
            dust.Rotate(0f, 0f, dummyDustSpin[i] * dt);

            Color color = dustColor;
            color.a *= 1f - t;
            renderer.color = color;
        }
    }

    private static Vector2 Rotate(Vector2 value, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(
            value.x * cos - value.y * sin,
            value.x * sin + value.y * cos);
    }
}

internal static class MapImpactDummySpriteCache
{
    private static Sprite ring;
    private static Sprite flash;
    private static Sprite dust;

    public static Sprite Ring => ring != null ? ring : ring = CreateRing();
    public static Sprite Flash => flash != null ? flash : flash = CreateFlash();
    public static Sprite Dust => dust != null ? dust : dust = CreateDust();

    private static Sprite CreateRing()
    {
        const int size = 32;
        Texture2D tex = CreateTexture(size);
        Vector2 center = new((size - 1) * 0.5f, (size - 1) * 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                bool visible = distance >= 10.5f && distance <= 13.5f;
                tex.SetPixel(x, y, visible ? Color.white : Color.clear);
            }
        }

        return Finish(tex, size, 32f);
    }

    private static Sprite CreateFlash()
    {
        const int size = 32;
        Texture2D tex = CreateTexture(size);
        int center = size / 2;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int dx = Mathf.Abs(x - center);
                int dy = Mathf.Abs(y - center);
                bool cross = dx <= 1 || dy <= 1;
                bool diagonal = Mathf.Abs(dx - dy) <= 1 && dx <= 10;
                bool core = dx + dy <= 5;
                tex.SetPixel(x, y, cross || diagonal || core ? Color.white : Color.clear);
            }
        }

        return Finish(tex, size, 32f);
    }

    private static Sprite CreateDust()
    {
        const int size = 8;
        Texture2D tex = CreateTexture(size);
        Vector2 center = new((size - 1) * 0.5f, (size - 1) * 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                tex.SetPixel(x, y, distance <= 3.25f ? Color.white : Color.clear);
            }
        }

        return Finish(tex, size, 16f);
    }

    private static Texture2D CreateTexture(int size)
    {
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        return texture;
    }

    private static Sprite Finish(Texture2D texture, int size, float pixelsPerUnit)
    {
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using NavMeshPlus.Components;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Room Lifecycle:
/// Persistent Start Base -> Build Extension Blocks -> NavMesh -> Spawn Monsters Outside Base -> Combat
/// -> Reward -> Highlight Pad -> Exit Extension Blocks.
///
/// 기본 4x4 Start Base는 RoomBaseTemplate이 영구 유지합니다.
/// RoomDefinitionSO.blocks 중 Start Base 격자 안 Placement는 생성하지 않고,
/// 격자 밖 Extension Block만 이동/도킹 연출을 수행합니다.
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

    [Header("Extension Block Assembly")]
    [Tooltip("Start Base 밖 Extension Block 배열 순서대로 진입 시차를 줍니다.")]
    [SerializeField] private float blockEntryStagger = 0.055f;
    [SerializeField] private float maxBlockEntryStagger = 0.32f;

    [Header("Docking Impact VFX - custom Sprite가 없으면 Procedural")]
    [SerializeField] private Transform impactVfxRoot;
    [Tooltip("직접 제작한 접촉면 Impact Sprite Animation. 비어 있으면 세련된 면 기반 Procedural FX를 사용합니다.")]
    [SerializeField] private Sprite[] impactSprites;
    [Tooltip("직접 제작한 먼지/파편 Sprite Animation. 비어 있으면 접촉면을 따라 퍼지는 Procedural FX를 사용합니다.")]
    [SerializeField] private Sprite[] dustSprites;
    [SerializeField, Min(1f)] private float impactVfxFps = 20f;
    [SerializeField, Min(0.05f)] private float impactVfxWorldScale = 1f;
    [SerializeField] private Material impactVfxMaterial;
    [SerializeField] private int impactVfxSortingOrder = -12;
    [SerializeField] private Color dummyImpactColor = new(0.88f, 0.96f, 1f, 1f);
    [SerializeField] private Color dummyDustColor = new(0.52f, 0.58f, 0.64f, 0.70f);
    [SerializeField, Min(1f)] private float finalBlockImpactMultiplier = 1.28f;

    [Header("Directional Camera Impulse")]
    [Tooltip("랜덤 Shake가 아니라 충돌 진행 방향 반대로 짧게 밀렸다 복귀하는 Impulse입니다.")]
    [SerializeField] private Transform cameraShakeTarget;
    [SerializeField, Min(0f)] private float cameraShakeAmplitude = 0.060f;
    [SerializeField, Min(0.01f)] private float cameraShakeDuration = 0.13f;
    [SerializeField, Min(0f)] private float maxCameraShakeAmplitude = 0.13f;
    [SerializeField, Min(0.01f)] private float maxCameraShakeDuration = 0.22f;
    [SerializeField, Min(1f)] private float cameraImpulseSpring = 72f;
    [SerializeField, Min(0f)] private float cameraImpulseDamping = 15f;
    [SerializeField, Range(0f, 0.25f)] private float cameraImpulseTangentNoise = 0.06f;

    [Header("Optional Impact Sound")]
    [SerializeField] private AudioSource impactAudioSource;
    [SerializeField] private AudioClip impactClip;
    [SerializeField, Range(0f, 1f)] private float impactVolume = 0.48f;
    [SerializeField, Range(0f, 0.3f)] private float impactPitchRandomness = 0.04f;

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
    private Vector2 cameraImpulseOffset;
    private Vector2 cameraImpulseVelocity;
    private float requestedShakeEndTime;

    public event Action<RoomDefinitionSO> RoomCombatStarted;
    public event Action<RoomDefinitionSO> RoomCombatCleared;
    public event Action<RoomDefinitionSO> RoomExited;
    public event Action<MonsterController> MonsterDefeated;

    public RoomDefinitionSO CurrentRoom => currentRoom;
    public Transform RoomOrigin => roomOrigin;
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

        float longestEntry = BuildExtensionBlocks(room);
        BuildObstacles(room);

        if (longestEntry > 0f)
            yield return new WaitForSeconds(longestEntry);

        // Persistent Start Base + Extension Block이 모두 최종 위치에 정착한 뒤 NavMesh를 굽습니다.
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

    /// <summary>
    /// Start Base 안쪽 Placement는 RoomBaseTemplate이 이미 담당하므로 생성하지 않습니다.
    /// 격자 밖 Placement만 실제 Incoming/Extension Block으로 생성합니다.
    /// </summary>
    private float BuildExtensionBlocks(RoomDefinitionSO room)
    {
        float longest = 0f;
        expectedAssemblyImpacts = 0;
        receivedAssemblyImpacts = 0;

        if (room == null || room.blocks == null)
            return longest;

        for (int i = 0; i < room.blocks.Count; i++)
        {
            MapBlockPlacement placement = room.blocks[i];
            if (!ShouldBuildPlacement(room, placement))
                continue;

            if (placement.prefab.WillImpact)
                expectedAssemblyImpacts++;
        }

        int validIndex = 0;
        for (int i = 0; i < room.blocks.Count; i++)
        {
            MapBlockPlacement placement = room.blocks[i];
            if (!ShouldBuildPlacement(room, placement))
                continue;

            Transform parent = mapRoot != null ? mapRoot : transform;
            MapBlock block = Instantiate(placement.prefab, parent);

            Vector3 destination = roomOrigin.position + (Vector3)room.GetBlockLocalPosition(placement.gridPosition);
            destination.z = roomOrigin.position.z;

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

    private static bool ShouldBuildPlacement(RoomDefinitionSO room, MapBlockPlacement placement)
    {
        if (room == null || placement == null || placement.prefab == null)
            return false;

        if (room.useRuntimeBase && room.usePersistentStartBase && room.IsStartBaseGridPosition(placement.gridPosition))
            return false;

        return true;
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

        SpawnMapImpactVfx(impactPosition, travelDirection, strength, finalImpact);
        RequestCameraImpulse(travelDirection, strength, finalImpact);
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

        GameObject go = new(finalImpact ? "DockImpactFX_Final" : "DockImpactFX");
        go.transform.SetParent(parent, true);
        go.transform.position = position;

        MapImpactVfxInstance instance = go.AddComponent<MapImpactVfxInstance>();
        instance.Play(
            impactSprites,
            dustSprites,
            impactVfxFps,
            impactVfxWorldScale * Mathf.Lerp(0.88f, 1.16f, Mathf.Clamp01(strength)),
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
        float randomPitch = UnityEngine.Random.Range(-impactPitchRandomness, impactPitchRandomness);
        impactAudioSource.pitch = Mathf.Clamp(1f + randomPitch, 0.5f, 2f);

        float volume = impactVolume * Mathf.Clamp(strength, 0.35f, finalImpact ? 1.20f : 1f);
        impactAudioSource.PlayOneShot(impactClip, Mathf.Clamp01(volume));
        impactAudioSource.pitch = oldPitch;
    }

    /// <summary>
    /// 진행 방향 반대로 CameraShakePivot을 밀어낸 뒤 spring으로 복귀시킵니다.
    /// 랜덤 원형 Shake를 사용하지 않아 도킹 방향이 읽히고 연출이 덜 산만합니다.
    /// </summary>
    private void RequestCameraImpulse(Vector2 travelDirection, float strength, bool finalImpact)
    {
        Transform target = ResolveCameraShakeTarget();
        if (target == null || cameraShakeAmplitude <= 0f)
            return;

        Vector2 direction = travelDirection.sqrMagnitude > 0.001f
            ? travelDirection.normalized
            : Vector2.down;
        Vector2 tangent = new(-direction.y, direction.x);

        float amplitude = cameraShakeAmplitude * Mathf.Max(0.1f, strength);
        if (finalImpact)
            amplitude *= 1.10f;
        amplitude = Mathf.Min(amplitude, Mathf.Max(cameraShakeAmplitude, maxCameraShakeAmplitude));

        float tangentAmount = UnityEngine.Random.Range(-cameraImpulseTangentNoise, cameraImpulseTangentNoise);
        Vector2 impulseDirection = (-direction + tangent * tangentAmount).normalized;

        activeShakeTarget = target;
        cameraImpulseVelocity += impulseDirection * amplitude * 28f;
        cameraImpulseVelocity = Vector2.ClampMagnitude(
            cameraImpulseVelocity,
            Mathf.Max(0.01f, maxCameraShakeAmplitude) * 34f);

        float duration = cameraShakeDuration * (finalImpact ? 1.18f : 1f);
        requestedShakeEndTime = Mathf.Max(
            requestedShakeEndTime,
            Time.unscaledTime + Mathf.Min(duration, Mathf.Max(0.01f, maxCameraShakeDuration)));

        if (cameraShakeRoutine == null)
            cameraShakeRoutine = StartCoroutine(CameraImpulseRoutine());
    }

    private Transform ResolveCameraShakeTarget()
    {
        if (cameraShakeTarget != null)
            return cameraShakeTarget;

        Camera main = Camera.main;
        return main != null ? main.transform : null;
    }

    private IEnumerator CameraImpulseRoutine()
    {
        lastCameraShakeOffset = Vector3.zero;
        cameraImpulseOffset = Vector2.zero;

        while (activeShakeTarget != null)
        {
            activeShakeTarget.localPosition -= lastCameraShakeOffset;

            float dt = Mathf.Min(0.033f, Mathf.Max(0.001f, Time.unscaledDeltaTime));
            Vector2 acceleration =
                -cameraImpulseOffset * Mathf.Max(1f, cameraImpulseSpring) -
                cameraImpulseVelocity * Mathf.Max(0f, cameraImpulseDamping);

            cameraImpulseVelocity += acceleration * dt;
            cameraImpulseOffset += cameraImpulseVelocity * dt;
            cameraImpulseOffset = Vector2.ClampMagnitude(
                cameraImpulseOffset,
                Mathf.Max(0.01f, maxCameraShakeAmplitude));

            lastCameraShakeOffset = new Vector3(cameraImpulseOffset.x, cameraImpulseOffset.y, 0f);
            activeShakeTarget.localPosition += lastCameraShakeOffset;

            bool timeDone = Time.unscaledTime >= requestedShakeEndTime;
            bool settled = cameraImpulseOffset.sqrMagnitude < 0.000004f &&
                           cameraImpulseVelocity.sqrMagnitude < 0.0004f;
            if (timeDone && settled)
                break;

            yield return null;
        }

        if (activeShakeTarget != null)
            activeShakeTarget.localPosition -= lastCameraShakeOffset;

        lastCameraShakeOffset = Vector3.zero;
        cameraImpulseOffset = Vector2.zero;
        cameraImpulseVelocity = Vector2.zero;
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
        cameraImpulseOffset = Vector2.zero;
        cameraImpulseVelocity = Vector2.zero;
        requestedShakeEndTime = 0f;
        activeShakeTarget = null;
    }

    private void BuildObstacles(RoomDefinitionSO room)
    {
        if (room == null || room.obstacles == null)
            return;

        for (int i = 0; i < room.obstacles.Count; i++)
        {
            ObstaclePlacement placement = room.obstacles[i];
            if (placement == null || placement.prefab == null)
                continue;

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

        if (room == null || room.monsterSpawns == null)
            return;

        int requestedCount = 0;
        int spawnedCount = 0;

        for (int i = 0; i < room.monsterSpawns.Count; i++)
        {
            MonsterSpawnEntry entry = room.monsterSpawns[i];
            if (entry == null || entry.monster == null)
                continue;

            int count = Mathf.Max(1, entry.count);
            requestedCount += count;

            for (int c = 0; c < count; c++)
            {
                Vector2 scatter = entry.scatterRadius > 0f
                    ? UnityEngine.Random.insideUnitCircle * entry.scatterRadius
                    : Vector2.zero;

                Vector3 requestedPosition = roomOrigin.position + (Vector3)(entry.localPosition + scatter);
                MonsterController monster = monsterPool.Get(
                    requestedPosition,
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
                "Combat will remain active through BattleRunManager diagnostic safety. Check Start Base NavMesh / Monster prefab setup.");
        }
    }

    private void HandleMonsterDeath(MonsterController monster)
    {
        if (monster == null)
            return;
        if (!activeMonsters.Remove(monster))
            return;

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

        // Highlight는 Start Base나 조립 Impact Count에 포함되지 않는 별도 출구 Block입니다.
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

        // activeBlocks에는 Extension/Highlight만 있으므로 Persistent Start Base는 절대 Exit되지 않습니다.
        for (int i = 0; i < activeBlocks.Count; i++)
        {
            MapBlock block = activeBlocks[i];
            if (block == null)
                continue;

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
            if (monster == null || !monster.gameObject.activeInHierarchy)
                continue;

            NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
            if (agent == null || !agent.enabled)
                continue;

            agentsToRestore.Add(agent);
            agent.enabled = false;
        }

        navSurface.RemoveData();
        navSurface.BuildNavMesh();

        for (int i = 0; i < agentsToRestore.Count; i++)
        {
            NavMeshAgent agent = agentsToRestore[i];
            if (agent == null || !agent.gameObject.activeInHierarchy)
                continue;

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
            if (obstacle == null)
                continue;

            obstacle.Broken -= HandleObstacleBroken;
            Destroy(obstacle.gameObject);
        }
        activeObstacles.Clear();

        for (int i = 0; i < activeBlocks.Count; i++)
        {
            MapBlock block = activeBlocks[i];
            if (block == null)
                continue;

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
/// 도킹 접촉면 전용 일회성 VFX Player.
/// 실제 Sprite 배열이 있으면 그대로 재생하고, 비어 있으면 원형 폭발 없이
/// Contact Core + Soft Glow + Tangent Sparks + Low Dust 레이어를 Procedural로 생성합니다.
/// </summary>
internal sealed class MapImpactVfxInstance : MonoBehaviour
{
    private SpriteRenderer impactRenderer;
    private SpriteRenderer glowRenderer;
    private SpriteRenderer dustRenderer;

    private Sprite[] impactFrames;
    private Sprite[] dustFrames;
    private float fps;
    private float elapsed;
    private float duration;
    private float worldScale;
    private bool proceduralImpact;
    private bool proceduralDust;
    private Color impactColor;
    private Color dustColor;

    private readonly List<ParticleVisual> particles = new();

    private sealed class ParticleVisual
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public Vector2 localVelocity;
        public float spin;
        public float baseAlpha;
        public float drag;
    }

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
        worldScale = Mathf.Max(0.05f, scale) * (finalImpact ? 1.06f : 1f);
        impactColor = fallbackImpactColor;
        dustColor = fallbackDustColor;

        proceduralImpact = !HasFrames(impactFrames);
        proceduralDust = !HasFrames(dustFrames);

        Vector2 direction = travelDirection.sqrMagnitude > 0.001f
            ? travelDirection.normalized
            : Vector2.down;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        // Procedural fallback은 타일 본체보다 아래 Sorting에 두어 접촉면/하부에서만 보이게 합니다.
        int resolvedOrder = proceduralImpact && proceduralDust
            ? Mathf.Min(sortingOrder, -12)
            : sortingOrder;

        CreateImpactVisual(material, resolvedOrder, finalImpact);
        CreateDustVisual(material, resolvedOrder - 1, finalImpact);

        float impactDuration = proceduralImpact
            ? (finalImpact ? 0.28f : 0.22f)
            : impactFrames.Length / fps;
        float dustDuration = proceduralDust
            ? (finalImpact ? 0.34f : 0.28f)
            : dustFrames.Length / fps;

        duration = Mathf.Max(0.16f, impactDuration, dustDuration);
    }

    private void CreateImpactVisual(Material material, int sortingOrder, bool finalImpact)
    {
        GameObject impactGo = new("ContactCore");
        impactGo.transform.SetParent(transform, false);
        impactRenderer = impactGo.AddComponent<SpriteRenderer>();
        impactRenderer.sortingOrder = sortingOrder;
        if (material != null)
            impactRenderer.sharedMaterial = material;

        if (!proceduralImpact)
        {
            impactRenderer.sprite = impactFrames[0];
            impactGo.transform.localScale = Vector3.one * worldScale;
            return;
        }

        impactRenderer.sprite = MapImpactProceduralSpriteCache.ContactCore;
        impactRenderer.color = impactColor;
        impactGo.transform.localScale = new Vector3(
            worldScale * 0.42f,
            worldScale * (finalImpact ? 1.12f : 1f),
            1f);

        GameObject glowGo = new("ContactGlow");
        glowGo.transform.SetParent(transform, false);
        glowRenderer = glowGo.AddComponent<SpriteRenderer>();
        glowRenderer.sprite = MapImpactProceduralSpriteCache.ContactGlow;
        Color glowColor = impactColor;
        glowColor.a *= 0.44f;
        glowRenderer.color = glowColor;
        glowRenderer.sortingOrder = sortingOrder - 1;
        if (material != null)
            glowRenderer.sharedMaterial = material;
        glowGo.transform.localScale = new Vector3(
            worldScale * 0.55f,
            worldScale * (finalImpact ? 1.20f : 1.06f),
            1f);

        CreateSparks(material, sortingOrder + 1, finalImpact);
    }

    private void CreateSparks(Material material, int sortingOrder, bool finalImpact)
    {
        int count = finalImpact ? 7 : 4;
        for (int i = 0; i < count; i++)
        {
            GameObject go = new($"Spark_{i}");
            go.transform.SetParent(transform, false);

            float alongFace = count <= 1
                ? 0f
                : Mathf.Lerp(-0.78f, 0.78f, i / (float)(count - 1));
            alongFace += UnityEngine.Random.Range(-0.10f, 0.10f);
            go.transform.localPosition = new Vector3(0f, alongFace * worldScale, 0f);

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = MapImpactProceduralSpriteCache.Spark;
            renderer.sortingOrder = sortingOrder;
            Color color = impactColor;
            color.a = UnityEngine.Random.Range(0.65f, 1f);
            renderer.color = color;
            if (material != null)
                renderer.sharedMaterial = material;

            float particleScale = UnityEngine.Random.Range(0.055f, 0.095f) * worldScale;
            go.transform.localScale = new Vector3(particleScale * 1.8f, particleScale, 1f);

            particles.Add(new ParticleVisual
            {
                transform = go.transform,
                renderer = renderer,
                localVelocity = new Vector2(
                    UnityEngine.Random.Range(-1.65f, -0.65f),
                    UnityEngine.Random.Range(-1.45f, 1.45f)) * worldScale,
                spin = UnityEngine.Random.Range(-240f, 240f),
                baseAlpha = color.a,
                drag = 7.5f
            });
        }
    }

    private void CreateDustVisual(Material material, int sortingOrder, bool finalImpact)
    {
        if (!proceduralDust)
        {
            GameObject dustGo = new("DustAnimation");
            dustGo.transform.SetParent(transform, false);
            dustRenderer = dustGo.AddComponent<SpriteRenderer>();
            dustRenderer.sprite = dustFrames[0];
            dustRenderer.sortingOrder = sortingOrder;
            if (material != null)
                dustRenderer.sharedMaterial = material;
            dustGo.transform.localScale = Vector3.one * worldScale;
            return;
        }

        int count = finalImpact ? 7 : 5;
        for (int i = 0; i < count; i++)
        {
            GameObject go = new($"DustWisp_{i}");
            go.transform.SetParent(transform, false);

            float alongFace = count <= 1
                ? 0f
                : Mathf.Lerp(-0.82f, 0.82f, i / (float)(count - 1));
            alongFace += UnityEngine.Random.Range(-0.12f, 0.12f);
            go.transform.localPosition = new Vector3(
                UnityEngine.Random.Range(-0.02f, 0.02f),
                alongFace * worldScale,
                0f);

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = MapImpactProceduralSpriteCache.DustWisp;
            renderer.sortingOrder = sortingOrder;
            Color color = dustColor;
            color.a *= UnityEngine.Random.Range(0.55f, 0.90f);
            renderer.color = color;
            if (material != null)
                renderer.sharedMaterial = material;

            float xScale = UnityEngine.Random.Range(0.16f, 0.28f) * worldScale;
            float yScale = UnityEngine.Random.Range(0.08f, 0.16f) * worldScale;
            go.transform.localScale = new Vector3(xScale, yScale, 1f);

            particles.Add(new ParticleVisual
            {
                transform = go.transform,
                renderer = renderer,
                localVelocity = new Vector2(
                    UnityEngine.Random.Range(-0.58f, -0.22f),
                    UnityEngine.Random.Range(-0.26f, 0.26f)) * worldScale,
                spin = UnityEngine.Random.Range(-22f, 22f),
                baseAlpha = color.a,
                drag = 4.2f
            });
        }
    }

    private void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);

        if (proceduralImpact)
            UpdateProceduralImpact(t);
        else
            UpdateRealAnimation(impactRenderer, impactFrames);

        if (!proceduralDust)
            UpdateRealAnimation(dustRenderer, dustFrames);

        UpdateParticles(t);

        if (elapsed >= duration)
            Destroy(gameObject);
    }

    private void UpdateRealAnimation(SpriteRenderer renderer, Sprite[] frames)
    {
        if (renderer == null || !HasFrames(frames))
            return;

        int index = Mathf.Min(frames.Length - 1, Mathf.FloorToInt(elapsed * fps));
        if (frames[index] != null)
            renderer.sprite = frames[index];
    }

    private void UpdateProceduralImpact(float t)
    {
        float sharp = Mathf.Clamp01(t * 4.8f);
        float fade = 1f - Mathf.SmoothStep(0.15f, 1f, t);

        if (impactRenderer != null)
        {
            Vector3 scale = impactRenderer.transform.localScale;
            scale.x = worldScale * Mathf.Lerp(0.18f, 0.52f, 1f - Mathf.Pow(1f - sharp, 3f));
            impactRenderer.transform.localScale = scale;

            Color color = impactColor;
            color.a *= fade;
            impactRenderer.color = color;
        }

        if (glowRenderer != null)
        {
            float glowT = Mathf.Clamp01(t * 3.6f);
            Vector3 scale = glowRenderer.transform.localScale;
            scale.x = worldScale * Mathf.Lerp(0.28f, 0.82f, glowT);
            glowRenderer.transform.localScale = scale;

            Color color = impactColor;
            color.a *= 0.42f * (1f - glowT);
            glowRenderer.color = color;
        }
    }

    private void UpdateParticles(float t)
    {
        float dt = Mathf.Min(0.033f, Time.unscaledDeltaTime);
        float fade = 1f - Mathf.SmoothStep(0.20f, 1f, t);

        for (int i = 0; i < particles.Count; i++)
        {
            ParticleVisual particle = particles[i];
            if (particle == null || particle.transform == null || particle.renderer == null)
                continue;

            particle.localVelocity *= Mathf.Exp(-particle.drag * dt);
            particle.transform.localPosition += (Vector3)(particle.localVelocity * dt);
            particle.transform.Rotate(0f, 0f, particle.spin * dt);

            Color color = particle.renderer.color;
            color.a = particle.baseAlpha * fade;
            particle.renderer.color = color;
        }
    }

    private static bool HasFrames(Sprite[] frames)
    {
        if (frames == null || frames.Length == 0)
            return false;

        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null)
                return true;
        }

        return false;
    }
}

internal static class MapImpactProceduralSpriteCache
{
    private static Sprite contactCore;
    private static Sprite contactGlow;
    private static Sprite spark;
    private static Sprite dustWisp;

    public static Sprite ContactCore => contactCore != null ? contactCore : contactCore = CreateContactCore();
    public static Sprite ContactGlow => contactGlow != null ? contactGlow : contactGlow = CreateContactGlow();
    public static Sprite Spark => spark != null ? spark : spark = CreateSpark();
    public static Sprite DustWisp => dustWisp != null ? dustWisp : dustWisp = CreateDustWisp();

    private static Sprite CreateContactCore()
    {
        const int width = 12;
        const int height = 48;
        Texture2D texture = CreateTexture(width, height, false);
        float centerX = (width - 1) * 0.5f;
        float centerY = (height - 1) * 0.5f;

        for (int y = 0; y < height; y++)
        {
            float vertical = 1f - Mathf.Abs(y - centerY) / (height * 0.5f);
            vertical = Mathf.SmoothStep(0f, 1f, vertical);
            for (int x = 0; x < width; x++)
            {
                float horizontal = 1f - Mathf.Abs(x - centerX) / 2.2f;
                float alpha = Mathf.Clamp01(horizontal) * vertical;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        return Finish(texture, width, height, 24f);
    }

    private static Sprite CreateContactGlow()
    {
        const int width = 28;
        const int height = 48;
        Texture2D texture = CreateTexture(width, height, true);
        float centerX = (width - 1) * 0.5f;
        float centerY = (height - 1) * 0.5f;

        for (int y = 0; y < height; y++)
        {
            float vertical = 1f - Mathf.Abs(y - centerY) / (height * 0.5f);
            vertical = Mathf.Pow(Mathf.Clamp01(vertical), 0.65f);
            for (int x = 0; x < width; x++)
            {
                float horizontal = 1f - Mathf.Abs(x - centerX) / (width * 0.5f);
                float alpha = Mathf.Pow(Mathf.Clamp01(horizontal), 2.2f) * vertical * 0.70f;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        return Finish(texture, width, height, 24f);
    }

    private static Sprite CreateSpark()
    {
        const int width = 12;
        const int height = 4;
        Texture2D texture = CreateTexture(width, height, true);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float longitudinal = 1f - x / (float)(width - 1);
                float vertical = 1f - Mathf.Abs(y - (height - 1) * 0.5f) / 2f;
                float alpha = Mathf.Clamp01(longitudinal * vertical);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        return Finish(texture, width, height, 24f);
    }

    private static Sprite CreateDustWisp()
    {
        const int width = 24;
        const int height = 12;
        Texture2D texture = CreateTexture(width, height, true);
        Vector2 center = new((width - 1) * 0.5f, (height - 1) * 0.5f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2 p = new((x - center.x) / (width * 0.5f), (y - center.y) / (height * 0.5f));
                float d = p.sqrMagnitude;
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - d), 1.8f) * 0.78f;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        return Finish(texture, width, height, 24f);
    }

    private static Texture2D CreateTexture(int width, int height, bool smooth)
    {
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = smooth ? FilterMode.Bilinear : FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] clear = new Color[width * height];
        texture.SetPixels(clear);
        return texture;
    }

    private static Sprite Finish(Texture2D texture, int width, int height, float pixelsPerUnit)
    {
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}

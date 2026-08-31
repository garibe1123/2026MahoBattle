using System;
using System.Collections;
using System.Collections.Generic;
using NavMeshPlus.Components;
using UnityEngine;

/// <summary>
/// Room Lifecycle:
/// Build Blocks -> Build NavMesh -> Spawn Fixed Monsters -> Combat -> Cleared
/// -> (external reward flow) -> OpenExit -> Highlight Pad -> Exit Blocks.
///
/// 보상/팬미션/다음 Node 결정은 BattleRunManager의 책임이며,
/// 이 클래스는 현재 Room의 생성/전투/철거만 담당합니다.
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

    private readonly List<MapBlock> activeBlocks = new();
    private readonly List<BattleObstacle> activeObstacles = new();
    private readonly List<MonsterController> activeMonsters = new();

    private RoomDefinitionSO currentRoom;
    private BattleContext currentContext;
    private bool roomTransitioning;
    private bool combatCleared;
    private bool exitOpened;
    private bool exitRoutineStarted;

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

        float longestEntry = BuildMapBlocks(room);
        BuildObstacles(room);

        if (longestEntry > 0f)
            yield return new WaitForSeconds(longestEntry);

        RebuildNavMesh();
        SpawnFixedMonsters(room);

        roomTransitioning = false;
        RoomCombatStarted?.Invoke(room);

        // 상점/이벤트용 빈 Room이나 테스트 Room은 전투 시작 직후 클리어 상태가 됩니다.
        if (activeMonsters.Count == 0)
            HandleCombatCleared();
    }

    private float BuildMapBlocks(RoomDefinitionSO room)
    {
        float longest = 0f;

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

            block.PlayEnter(destination, placement.entryDirection);
            activeBlocks.Add(block);
            longest = Mathf.Max(longest, block.EntryDuration);
        }

        return longest;
    }

    private void BuildObstacles(RoomDefinitionSO room)
    {
        for (int i = 0; i < room.obstacles.Count; i++)
        {
            ObstaclePlacement placement = room.obstacles[i];
            if (placement == null || placement.prefab == null) continue;

            Transform parent = obstacleRoot != null ? obstacleRoot : transform;
            Vector3 position = roomOrigin.position + (Vector3)placement.localPosition;
            Quaternion rotation = Quaternion.Euler(0f, 0f, placement.rotationZ);

            BattleObstacle obstacle = Instantiate(placement.prefab, position, rotation, parent);
            activeObstacles.Add(obstacle);
        }
    }

    private void SpawnFixedMonsters(RoomDefinitionSO room)
    {
        if (monsterPool == null || playerTarget == null)
        {
            Debug.LogError("[BattleRoom] Cannot spawn monsters: MonsterPool or playerTarget is missing.");
            return;
        }

        for (int i = 0; i < room.monsterSpawns.Count; i++)
        {
            MonsterSpawnEntry entry = room.monsterSpawns[i];
            if (entry == null || entry.monster == null) continue;

            int count = Mathf.Max(1, entry.count);
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
                    Debug.LogError($"[BattleRoom] Failed to spawn monster '{entry.monster.name}'.");
                    continue;
                }

                Transform parent = monsterRoot != null ? monsterRoot : transform;
                monster.transform.SetParent(parent);
                activeMonsters.Add(monster);
            }
        }
    }

    private void HandleMonsterDeath(MonsterController monster)
    {
        if (monster == null) return;
        if (!activeMonsters.Remove(monster)) return;

        // Pool 반환 전에 알립니다. 외부에서 Definition/보상값을 읽을 수 있어야 합니다.
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

    /// <summary>
    /// 보상 선택/정산이 끝난 뒤 BattleRunManager가 호출합니다.
    /// Combat Clear 이전에는 열리지 않습니다.
    /// </summary>
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
            // 테스트 Room은 Highlight Prefab 없이도 전체 Flow 검증이 가능해야 합니다.
            StartCoroutine(ExitRoomRoutine());
            return;
        }

        Transform parent = mapRoot != null ? mapRoot : transform;
        MapBlock highlight = Instantiate(currentRoom.highlightBlockPrefab, parent);
        Vector3 destination = playerTarget != null
            ? playerTarget.position + (Vector3)currentRoom.highlightBlockOffset
            : roomOrigin.position + (Vector3)currentRoom.highlightBlockOffset;

        highlight.PlayEnter(destination, Vector2.down);
        activeBlocks.Add(highlight);

        RoomExitPad exitPad = highlight.GetComponent<RoomExitPad>();
        if (exitPad != null && playerTarget != null)
        {
            exitPad.Arm(playerTarget, () => StartCoroutine(ExitRoomRoutine()));
        }
        else
        {
            // ExitPad가 없는 프로토타입 프리팹은 등장 연출 후 자동 퇴장합니다.
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
        StopAllCoroutines();
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

        navSurface.RemoveData();
        navSurface.BuildNavMesh();
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
            if (activeObstacles[i] != null)
                Destroy(activeObstacles[i].gameObject);
        }
        activeObstacles.Clear();

        for (int i = 0; i < activeBlocks.Count; i++)
        {
            if (activeBlocks[i] != null)
                Destroy(activeBlocks[i].gameObject);
        }
        activeBlocks.Clear();

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

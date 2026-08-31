using System;
using System.Collections;
using System.Collections.Generic;
using NavMeshPlus.Components;
using UnityEngine;

/// <summary>
/// Room Lifecycle:
/// Build Blocks -> Build NavMesh -> Spawn Fixed Monsters -> Combat -> Highlight Pad -> Exit Blocks.
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

    public event Action<RoomDefinitionSO> RoomCombatStarted;
    public event Action<RoomDefinitionSO> RoomCombatCleared;
    public event Action<RoomDefinitionSO> RoomExited;

    public bool IsRoomActive => currentRoom != null;
    public int AliveMonsterCount => activeMonsters.Count;

    private void Awake()
    {
        if (roomOrigin == null)
            roomOrigin = transform;
    }

    public void EnterRoom(RoomDefinitionSO room, BattleContext context)
    {
        if (room == null || roomTransitioning)
            return;

        StartCoroutine(EnterRoomRoutine(room, context));
    }

    private IEnumerator EnterRoomRoutine(RoomDefinitionSO room, BattleContext context)
    {
        roomTransitioning = true;

        if (currentRoom != null)
            ClearImmediate();

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

        // 상점/이벤트 등 전투가 없는 RoomDefinition을 사용할 경우 즉시 클리어 처리 가능.
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
            return;

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

                if (monster == null) continue;

                Transform parent = monsterRoot != null ? monsterRoot : transform;
                monster.transform.SetParent(parent);
                activeMonsters.Add(monster);
            }
        }
    }

    private void HandleMonsterDeath(MonsterController monster)
    {
        if (monster == null) return;

        activeMonsters.Remove(monster);
        monsterPool.Return(monster);

        if (activeMonsters.Count == 0)
            HandleCombatCleared();
    }

    private void HandleCombatCleared()
    {
        if (currentRoom == null || roomTransitioning)
            return;

        roomTransitioning = true;
        RoomCombatCleared?.Invoke(currentRoom);
        SpawnHighlightPad();
    }

    private void SpawnHighlightPad()
    {
        if (currentRoom.highlightBlockPrefab == null)
        {
            // 하이라이트 블록이 아직 준비되지 않은 테스트 Room은 즉시 퇴장 가능하게 처리.
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
            // ExitPad가 없는 프로토타입 프리팹은 연출 완료 후 자동 퇴장.
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
        roomTransitioning = false;
        RoomExited?.Invoke(finishedRoom);
    }

    private void RebuildNavMesh()
    {
        if (navSurface == null) return;

        navSurface.RemoveData();
        navSurface.BuildNavMesh();
    }

    private void ClearImmediate()
    {
        for (int i = 0; i < activeMonsters.Count; i++)
        {
            if (activeMonsters[i] != null)
                monsterPool.Return(activeMonsters[i]);
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
}

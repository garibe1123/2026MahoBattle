using System;
using System.Collections.Generic;
using DG.Tweening;
using NavMeshPlus.Components;
using UnityEngine;

public class BattleRoomManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NavMeshSurface navSurface;
    [SerializeField] private MonsterPool monsterPool;
    [SerializeField] private Transform player;
    [SerializeField] private Transform roomRoot;

    [Header("Grid")]
    [SerializeField] private float worldUnitsPerBlock = 2f;
    [SerializeField] private float assemblyStagger = 0.04f;

    private readonly List<MapBlock> activeBlocks = new();
    private readonly HashSet<MonsterController> activeMonsters = new();
    private RoomDefinitionSO currentRoom;
    private BattleContext currentContext;
    private bool clearing;

    public bool IsCombatActive { get; private set; }
    public event Action RoomCleared;

    public void LoadRoom(RoomDefinitionSO room, BattleContext context)
    {
        if (room == null) return;

        ClearCurrentRoomImmediate();
        currentRoom = room;
        currentContext = context;
        clearing = false;
        IsCombatActive = false;

        AssembleRoom();
    }

    private void AssembleRoom()
    {
        Sequence sequence = DOTween.Sequence();

        for (int i = 0; i < currentRoom.blocks.Count; i++)
        {
            MapBlockPlacement placement = currentRoom.blocks[i];
            if (placement.prefab == null) continue;

            Vector3 target = GridToWorld(placement.gridPosition);
            MapBlock block = Instantiate(placement.prefab, target, Quaternion.identity, roomRoot);
            activeBlocks.Add(block);

            Vector3 start = GetEntryStart(block, target);
            block.transform.position = start;

            float duration = block.entryType == BlockEntryType.Static ? 0f : block.entryDuration;
            sequence.Insert(i * assemblyStagger,
                block.transform.DOMove(target, duration).SetEase(Ease.OutCubic));
        }

        sequence.OnComplete(OnAssemblyCompleted);
    }

    private Vector3 GetEntryStart(MapBlock block, Vector3 target)
    {
        Vector2 offset = block.entryOffset;

        return block.entryType switch
        {
            BlockEntryType.CeilingRail => target + Vector3.up * Mathf.Abs(offset.y == 0f ? 8f : offset.y),
            BlockEntryType.RiseFromFloor => target + Vector3.down * Mathf.Abs(offset.y == 0f ? 5f : offset.y),
            BlockEntryType.Drop => target + Vector3.up * Mathf.Abs(offset.y == 0f ? 6f : offset.y),
            BlockEntryType.Static => target,
            _ => target + (Vector3)(offset == Vector2.zero ? new Vector2(8f, 0f) : offset)
        };
    }

    private void OnAssemblyCompleted()
    {
        RebuildNavigation();
        SpawnRoomMonsters();
        IsCombatActive = activeMonsters.Count > 0;

        if (activeMonsters.Count == 0)
            CompleteRoom();
    }

    private void RebuildNavigation()
    {
        if (navSurface == null) return;
        navSurface.RemoveData();
        navSurface.BuildNavMesh();
    }

    private void SpawnRoomMonsters()
    {
        activeMonsters.Clear();

        foreach (MonsterSpawnEntry entry in currentRoom.monsterSpawns)
        {
            if (entry.monster == null) continue;
            int count = Mathf.Max(1, entry.count);

            for (int i = 0; i < count; i++)
            {
                Vector3 spawnPosition = GridToWorld(entry.blockPosition) + (Vector3)entry.localOffset;
                if (count > 1)
                    spawnPosition += (Vector3)UnityEngine.Random.insideUnitCircle * 0.35f;

                MonsterController monster = monsterPool.Get(spawnPosition, entry.monster, currentContext);
                if (monster == null) continue;

                monster.Died += OnMonsterDied;
                activeMonsters.Add(monster);
            }
        }
    }

    private void OnMonsterDied(MonsterController monster)
    {
        if (monster != null)
            monster.Died -= OnMonsterDied;

        activeMonsters.Remove(monster);

        if (!clearing && activeMonsters.Count == 0)
            CompleteRoom();
    }

    private void CompleteRoom()
    {
        clearing = true;
        IsCombatActive = false;

        SpawnHighlightBlock();
        RoomCleared?.Invoke();
    }

    private void SpawnHighlightBlock()
    {
        if (currentRoom == null || currentRoom.highlightBlockPrefab == null || player == null) return;

        Vector3 position = player.position;
        MapBlock block = Instantiate(currentRoom.highlightBlockPrefab,
            position + Vector3.up * 6f,
            Quaternion.identity,
            roomRoot);
        activeBlocks.Add(block);

        block.transform.DOMove(position, 0.45f).SetEase(Ease.InQuad);
    }

    public void ExitCurrentRoom()
    {
        Sequence sequence = DOTween.Sequence();

        for (int i = 0; i < activeBlocks.Count; i++)
        {
            MapBlock block = activeBlocks[i];
            if (block == null) continue;

            Vector3 exit = block.transform.position + Vector3.right * 10f;
            sequence.Insert(i * 0.02f, block.transform.DOMove(exit, 0.5f).SetEase(Ease.InQuad));
        }

        sequence.OnComplete(ClearCurrentRoomImmediate);
    }

    private Vector3 GridToWorld(Vector2Int position)
    {
        Vector3 origin = roomRoot != null ? roomRoot.position : Vector3.zero;
        return origin + new Vector3(position.x * worldUnitsPerBlock, position.y * worldUnitsPerBlock, 0f);
    }

    private void ClearCurrentRoomImmediate()
    {
        foreach (MonsterController monster in activeMonsters)
        {
            if (monster == null) continue;
            monster.Died -= OnMonsterDied;
            monsterPool.Return(monster);
        }
        activeMonsters.Clear();

        foreach (MapBlock block in activeBlocks)
        {
            if (block != null) Destroy(block.gameObject);
        }
        activeBlocks.Clear();

        currentRoom = null;
        IsCombatActive = false;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 현재 필드의 최종 점유 셀을 기준으로 도킹 손잡이 가시성을 정리합니다.
///
/// 규칙:
/// - Persistent 4x4 Base는 손잡이를 사용하지 않습니다. 생성되어 있더라도 항상 숨깁니다.
/// - 일반 전투 MapBlock / Reward Show Floor는 다른 바닥과 맞닿는 내부 접합면의 손잡이만 숨깁니다.
/// - 바닥 Sprite/랜덤 아트/하판은 다시 만들지 않습니다.
/// </summary>
[DefaultExecutionOrder(22000)]
[DisallowMultipleComponent]
public sealed class BattleDockHandleVisibilityController : MonoBehaviour
{
    [SerializeField, Min(0.02f)] private float refreshInterval = 0.06f;

    private float nextRefreshAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleDockHandleVisibilityController>() != null)
            return;

        GameObject host = new("BattleDockHandleVisibilityRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleDockHandleVisibilityController>();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
            return;

        nextRefreshAt = Time.unscaledTime + Mathf.Max(0.02f, refreshInterval);
        RefreshNow();
    }

    public static void RefreshNow()
    {
        RoomBaseTemplate baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        MapBlock[] blocks = FindObjectsByType<MapBlock>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        HashSet<Vector2Int> occupied = new();
        HashSet<Vector2Int> baseCells = new();

        if (baseTemplate != null && baseTemplate.ActiveBase != null)
        {
            Vector2Int origin = WorldToCell(baseTemplate.FixedTileOriginWorld);
            for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
            {
                for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                {
                    Vector2Int cell = origin + new Vector2Int(x, y);
                    baseCells.Add(cell);
                    occupied.Add(cell);
                }
            }
        }

        Dictionary<MapBlock, HashSet<Vector2Int>> blockCells = new();
        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (!IsLiveFloorBlock(block))
                continue;

            HashSet<Vector2Int> cells = CollectBlockCells(block);
            if (cells.Count == 0)
                continue;

            blockCells[block] = cells;
            foreach (Vector2Int cell in cells)
                occupied.Add(cell);
        }

        // Persistent 4x4 Base에는 손잡이를 사용하지 않습니다.
        // PresentationTemplate이 재구축되어 손잡이 그룹이 다시 생겨도 즉시 숨깁니다.
        if (baseTemplate != null && baseTemplate.ActiveBase != null)
        {
            Transform template = baseTemplate.ActiveBase.transform.Find("PresentationTemplate");
            if (template != null)
                HideAllHandles(template);
        }

        foreach (KeyValuePair<MapBlock, HashSet<Vector2Int>> pair in blockCells)
        {
            MapBlock block = pair.Key;
            if (block == null)
                continue;

            Transform visual = block.transform.Find("Visual");
            if (visual == null)
                visual = block.transform;

            Transform template = visual.Find("PresentationTemplate");
            if (template != null)
                ApplyContactVisibility(template, pair.Value, occupied);
        }
    }

    private static bool IsLiveFloorBlock(MapBlock block)
    {
        if (block == null || !block.gameObject.activeInHierarchy)
            return false;

        string blockName = block.name;
        bool rawPrototype =
            blockName.StartsWith("__RuntimeRoomPiecePrototype_", StringComparison.Ordinal) &&
            !blockName.EndsWith("(Clone)", StringComparison.Ordinal);

        return !rawPrototype && !blockName.StartsWith("Outgoing_", StringComparison.Ordinal);
    }

    private static HashSet<Vector2Int> CollectBlockCells(MapBlock block)
    {
        HashSet<Vector2Int> cells = new();
        if (block == null)
            return cells;

        Transform[] transforms = block.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform tile = transforms[i];
            if (!IsFloorTile(tile))
                continue;

            Vector3 world = tile.position;
            if (block.HasEntryDestination)
                world += block.EntryDestination - block.transform.position;

            cells.Add(WorldToCell(world));
        }

        return cells;
    }

    private static bool IsFloorTile(Transform target)
    {
        if (target == null)
            return false;

        return target.name.StartsWith("Tile_", StringComparison.Ordinal) ||
               target.name.StartsWith("ShowTile_", StringComparison.Ordinal);
    }

    private static void HideAllHandles(Transform templateRoot)
    {
        if (templateRoot == null)
            return;

        SetGroupVisible(templateRoot, "DockHandle_Left", false);
        SetGroupVisible(templateRoot, "DockHandle_Right", false);
        SetGroupVisible(templateRoot, "DockHandle_Upper", false);
        SetGroupVisible(templateRoot, "DockHandle_Lower", false);
    }

    private static void ApplyContactVisibility(
        Transform templateRoot,
        HashSet<Vector2Int> ownCells,
        HashSet<Vector2Int> occupied)
    {
        if (templateRoot == null || ownCells == null || ownCells.Count == 0)
            return;

        SetGroupVisible(
            templateRoot,
            "DockHandle_Left",
            !TouchesOtherFloor(ownCells, occupied, Vector2Int.left));
        SetGroupVisible(
            templateRoot,
            "DockHandle_Right",
            !TouchesOtherFloor(ownCells, occupied, Vector2Int.right));
        SetGroupVisible(
            templateRoot,
            "DockHandle_Upper",
            !TouchesOtherFloor(ownCells, occupied, Vector2Int.up));
        SetGroupVisible(
            templateRoot,
            "DockHandle_Lower",
            !TouchesOtherFloor(ownCells, occupied, Vector2Int.down));
    }

    private static bool TouchesOtherFloor(
        HashSet<Vector2Int> ownCells,
        HashSet<Vector2Int> occupied,
        Vector2Int side)
    {
        foreach (Vector2Int cell in ownCells)
        {
            Vector2Int neighbor = cell + side;
            if (!ownCells.Contains(neighbor) && occupied.Contains(neighbor))
                return true;
        }

        return false;
    }

    private static void SetGroupVisible(Transform templateRoot, string groupName, bool visible)
    {
        Transform group = templateRoot.Find(groupName);
        if (group != null && group.gameObject.activeSelf != visible)
            group.gameObject.SetActive(visible);
    }

    private static Vector2Int WorldToCell(Vector3 world)
    {
        float size = Mathf.Max(0.0001f, RoomBaseTemplate.TileWorldSize);
        return new Vector2Int(
            Mathf.RoundToInt(world.x / size),
            Mathf.RoundToInt(world.y / size));
    }
}

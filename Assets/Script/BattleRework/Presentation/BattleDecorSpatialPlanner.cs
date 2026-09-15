using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BattleDecor의 Field 크기 기반 배치 개수만 계산합니다.
/// Decor Exit는 Runtime Controller가 저장된 Entry Rail을 그대로 역주행하므로
/// 경로 탐색/충돌 회피는 이 Planner의 책임이 아닙니다.
/// </summary>
public static class BattleDecorSpatialPlanner
{
    public static int ResolveTargetCount(
        IReadOnlyList<Bounds> floorBounds,
        float cell,
        int minimumCount,
        int legacyMaximumCount,
        bool scaleWithFloor,
        float exposedEdgeTilesPerDecor,
        int maximumScaledCount,
        int jitter,
        System.Random random)
    {
        int minCount = Mathf.Max(0, minimumCount);
        int legacyMax = Mathf.Max(minCount, legacyMaximumCount);
        if (!scaleWithFloor)
            return legacyMax <= minCount ? minCount : random.Next(minCount, legacyMax + 1);

        HashSet<Vector2Int> occupied = BuildFloorCellSet(floorBounds, cell);
        int exposedEdges = CountExposedFloorEdges(occupied);
        int target = Mathf.RoundToInt(exposedEdges / Mathf.Max(1f, exposedEdgeTilesPerDecor));

        int safeJitter = Mathf.Clamp(jitter, 0, 3);
        if (safeJitter > 0)
            target += random.Next(-safeJitter, safeJitter + 1);

        return Mathf.Clamp(target, minCount, Mathf.Max(minCount, maximumScaledCount));
    }

    private static HashSet<Vector2Int> BuildFloorCellSet(IReadOnlyList<Bounds> floorBounds, float cell)
    {
        HashSet<Vector2Int> occupied = new();
        if (floorBounds == null)
            return occupied;

        float safeCell = Mathf.Max(0.01f, cell);
        float inset = safeCell * 0.08f;

        for (int i = 0; i < floorBounds.Count; i++)
        {
            Bounds bounds = floorBounds[i];
            int minX = Mathf.CeilToInt((bounds.min.x + inset) / safeCell);
            int maxX = Mathf.FloorToInt((bounds.max.x - inset) / safeCell);
            int minY = Mathf.CeilToInt((bounds.min.y + inset) / safeCell);
            int maxY = Mathf.FloorToInt((bounds.max.y - inset) / safeCell);

            if (minX > maxX || minY > maxY)
            {
                occupied.Add(new Vector2Int(
                    Mathf.RoundToInt(bounds.center.x / safeCell),
                    Mathf.RoundToInt(bounds.center.y / safeCell)));
                continue;
            }

            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                occupied.Add(new Vector2Int(x, y));
        }

        return occupied;
    }

    private static int CountExposedFloorEdges(HashSet<Vector2Int> occupied)
    {
        int exposed = 0;
        foreach (Vector2Int cell in occupied)
        {
            if (!occupied.Contains(cell + Vector2Int.left)) exposed++;
            if (!occupied.Contains(cell + Vector2Int.right)) exposed++;
            if (!occupied.Contains(cell + Vector2Int.up)) exposed++;
            if (!occupied.Contains(cell + Vector2Int.down)) exposed++;
        }
        return exposed;
    }
}

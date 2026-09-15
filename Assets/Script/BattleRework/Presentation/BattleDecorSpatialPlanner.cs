using System;
using System.Collections.Generic;
using UnityEngine;

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

    /// <summary>
    /// Decor Entry는 Field 바깥에서 authored outward 방향의 직선 Rail로 들어옵니다.
    /// Exit에서는 별도의 길찾기를 하지 않고 같은 Rail을 반대로 되짚어 화면 밖으로 복귀합니다.
    /// 기존 Controller 호출부 호환을 위해 메서드 시그니처는 유지합니다.
    /// </summary>
    public static bool TryResolveShortestExitRoute(
        Bounds carrierBounds,
        Vector3 startWorld,
        Vector2 preferredDirection,
        IReadOnlyList<Bounds> obstacles,
        float cell,
        Camera camera,
        float offscreenMargin,
        int nodeLimit,
        float clearanceTiles,
        out List<Vector3> route)
    {
        route = new List<Vector3>();

        Vector2 outward = Cardinalize(preferredDirection);
        Bounds currentBounds = carrierBounds;
        currentBounds.center = startWorld;

        float distance = ResolveReverseEntryRailDistance(
            currentBounds,
            outward,
            obstacles,
            Mathf.Max(0.25f, cell),
            camera,
            offscreenMargin);

        if (distance <= 0.001f)
            return false;

        route.Add(startWorld + (Vector3)(outward * distance));
        return true;
    }

    public static bool CanOccupy(Bounds candidate, IReadOnlyList<Bounds> obstacles, float clearance)
    {
        if (obstacles == null)
            return true;

        for (int i = 0; i < obstacles.Count; i++)
        {
            Bounds obstacle = obstacles[i];
            float thresholdX = Mathf.Max(0f, candidate.extents.x + obstacle.extents.x - clearance);
            float thresholdY = Mathf.Max(0f, candidate.extents.y + obstacle.extents.y - clearance);
            bool overlapX = Mathf.Abs(candidate.center.x - obstacle.center.x) < thresholdX;
            bool overlapY = Mathf.Abs(candidate.center.y - obstacle.center.y) < thresholdY;
            if (overlapX && overlapY)
                return false;
        }

        return true;
    }

    private static float ResolveReverseEntryRailDistance(
        Bounds carrierBounds,
        Vector2 outward,
        IReadOnlyList<Bounds> obstacles,
        float cell,
        Camera camera,
        float offscreenMargin)
    {
        float margin = Mathf.Max(0.25f, offscreenMargin);
        float distance = Mathf.Max(8f, cell * 4f);

        if (camera != null && camera.orthographic)
        {
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * Mathf.Max(0.1f, camera.aspect);
            Vector3 cameraCenter = camera.transform.position;

            if (outward.x > 0.5f)
            {
                float targetMinX = cameraCenter.x + halfWidth + margin;
                distance = Mathf.Max(distance, targetMinX - carrierBounds.min.x);
            }
            else if (outward.x < -0.5f)
            {
                float targetMaxX = cameraCenter.x - halfWidth - margin;
                distance = Mathf.Max(distance, carrierBounds.max.x - targetMaxX);
            }
            else if (outward.y > 0.5f)
            {
                float targetMinY = cameraCenter.y + halfHeight + margin;
                distance = Mathf.Max(distance, targetMinY - carrierBounds.min.y);
            }
            else
            {
                float targetMaxY = cameraCenter.y - halfHeight - margin;
                distance = Mathf.Max(distance, carrierBounds.max.y - targetMaxY);
            }

            return Mathf.Max(0.5f, distance);
        }

        if (obstacles != null && obstacles.Count > 0)
        {
            Bounds aggregate = obstacles[0];
            for (int i = 1; i < obstacles.Count; i++)
                aggregate.Encapsulate(obstacles[i]);

            if (outward.x > 0.5f)
                distance = Mathf.Max(distance, aggregate.max.x + margin - carrierBounds.min.x);
            else if (outward.x < -0.5f)
                distance = Mathf.Max(distance, carrierBounds.max.x - (aggregate.min.x - margin));
            else if (outward.y > 0.5f)
                distance = Mathf.Max(distance, aggregate.max.y + margin - carrierBounds.min.y);
            else
                distance = Mathf.Max(distance, carrierBounds.max.y - (aggregate.min.y - margin));
        }

        return Mathf.Max(0.5f, distance);
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

    private static Vector2 Cardinalize(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return Vector2.right;
        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? (direction.x >= 0f ? Vector2.right : Vector2.left)
            : (direction.y >= 0f ? Vector2.up : Vector2.down);
    }
}

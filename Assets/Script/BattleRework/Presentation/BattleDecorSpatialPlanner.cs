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
        float safeCell = Mathf.Max(0.25f, cell);
        float clearance = Mathf.Max(0f, clearanceTiles) * safeCell;

        Bounds startBounds = carrierBounds;
        startBounds.center = startWorld;
        if (IsOutsideExitRegion(startBounds, obstacles, safeCell, camera, offscreenMargin))
        {
            route.Add(startWorld);
            return true;
        }

        Vector2Int[] steps = BuildStepOrder(preferredDirection);
        Queue<Vector2Int> open = new();
        HashSet<Vector2Int> visited = new();
        Dictionary<Vector2Int, Vector2Int> parent = new();
        Vector2Int start = Vector2Int.zero;
        open.Enqueue(start);
        visited.Add(start);

        int maxNodes = Mathf.Clamp(nodeLimit, 256, 8192);
        int maxRadius = ResolveSearchRadiusCells(safeCell, obstacles, camera, offscreenMargin);
        int expanded = 0;
        Vector2Int goal = default;
        bool found = false;

        while (open.Count > 0 && expanded < maxNodes)
        {
            Vector2Int current = open.Dequeue();
            expanded++;

            for (int i = 0; i < steps.Length; i++)
            {
                Vector2Int next = current + steps[i];
                if (Mathf.Abs(next.x) > maxRadius || Mathf.Abs(next.y) > maxRadius)
                    continue;
                if (!visited.Add(next))
                    continue;

                Vector3 world = startWorld + new Vector3(next.x * safeCell, next.y * safeCell, 0f);
                Bounds candidate = carrierBounds;
                candidate.center = world;
                if (!CanOccupy(candidate, obstacles, clearance))
                    continue;

                parent[next] = current;
                if (IsOutsideExitRegion(candidate, obstacles, safeCell, camera, offscreenMargin))
                {
                    goal = next;
                    found = true;
                    open.Clear();
                    break;
                }

                open.Enqueue(next);
            }
        }

        if (!found)
            return false;

        List<Vector2Int> raw = new();
        Vector2Int cursor = goal;
        while (cursor != start)
        {
            raw.Add(cursor);
            if (!parent.TryGetValue(cursor, out cursor))
                return false;
        }
        raw.Reverse();

        Vector2Int previous = start;
        Vector2Int lastDirection = Vector2Int.zero;
        for (int i = 0; i < raw.Count; i++)
        {
            Vector2Int point = raw[i];
            Vector2Int direction = point - previous;
            if (i > 0 && direction != lastDirection)
            {
                Vector2Int corner = raw[i - 1];
                route.Add(startWorld + new Vector3(corner.x * safeCell, corner.y * safeCell, 0f));
            }

            lastDirection = direction;
            previous = point;
        }

        Vector2Int finalPoint = raw[raw.Count - 1];
        route.Add(startWorld + new Vector3(finalPoint.x * safeCell, finalPoint.y * safeCell, 0f));
        return route.Count > 0;
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

    private static Vector2Int[] BuildStepOrder(Vector2 preferredDirection)
    {
        Vector2 preferred = Cardinalize(preferredDirection);
        if (preferred.x > 0.5f)
            return new[] { Vector2Int.right, Vector2Int.up, Vector2Int.down, Vector2Int.left };
        if (preferred.x < -0.5f)
            return new[] { Vector2Int.left, Vector2Int.up, Vector2Int.down, Vector2Int.right };
        if (preferred.y > 0.5f)
            return new[] { Vector2Int.up, Vector2Int.left, Vector2Int.right, Vector2Int.down };
        return new[] { Vector2Int.down, Vector2Int.left, Vector2Int.right, Vector2Int.up };
    }

    private static int ResolveSearchRadiusCells(
        float cell,
        IReadOnlyList<Bounds> obstacles,
        Camera camera,
        float offscreenMargin)
    {
        float span = 12f;
        if (camera != null && camera.orthographic)
        {
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * Mathf.Max(0.1f, camera.aspect);
            span = Mathf.Max(span, Mathf.Max(halfWidth, halfHeight) * 2f + offscreenMargin * 2f);
        }
        else if (obstacles != null && obstacles.Count > 0)
        {
            Bounds aggregate = obstacles[0];
            for (int i = 1; i < obstacles.Count; i++)
                aggregate.Encapsulate(obstacles[i]);
            span = Mathf.Max(span, Mathf.Max(aggregate.size.x, aggregate.size.y) + offscreenMargin * 2f);
        }

        return Mathf.Clamp(Mathf.CeilToInt(span / Mathf.Max(0.01f, cell)) + 6, 12, 96);
    }

    private static bool IsOutsideExitRegion(
        Bounds candidate,
        IReadOnlyList<Bounds> obstacles,
        float cell,
        Camera camera,
        float offscreenMargin)
    {
        float margin = Mathf.Max(0.25f, offscreenMargin);
        if (camera != null && camera.orthographic)
        {
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * Mathf.Max(0.1f, camera.aspect);
            Vector3 center = camera.transform.position;
            return candidate.max.x < center.x - halfWidth - margin ||
                   candidate.min.x > center.x + halfWidth + margin ||
                   candidate.max.y < center.y - halfHeight - margin ||
                   candidate.min.y > center.y + halfHeight + margin;
        }

        if (obstacles == null || obstacles.Count == 0)
            return true;

        Bounds aggregate = obstacles[0];
        for (int i = 1; i < obstacles.Count; i++)
            aggregate.Encapsulate(obstacles[i]);
        aggregate.Expand(new Vector3(cell * 2f + margin * 2f, cell * 2f + margin * 2f, 0f));
        return candidate.max.x < aggregate.min.x ||
               candidate.min.x > aggregate.max.x ||
               candidate.max.y < aggregate.min.y ||
               candidate.min.y > aggregate.max.y;
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

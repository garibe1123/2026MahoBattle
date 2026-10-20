using UnityEngine;

internal sealed class EffectEmissionPath
{
    private Vector3 previous;
    private float distanceUntilNext;

    public void Reset(Vector3 position, float spacing)
    {
        previous = position;
        distanceUntilNext = EffectNumbers.Positive(spacing, 0.2f);
    }

    public int Capture(Vector3 position, float spacing, Vector3[] points)
    {
        Vector3 start = previous;
        Vector3 movement = position - start;
        previous = position;
        float distance = movement.magnitude;
        if (distance <= 0f) return 0;
        float step = EffectNumbers.Positive(spacing, 0.2f);
        float next = distanceUntilNext;
        int count = 0;
        while (next <= distance && count < points.Length)
        {
            points[count++] = start + movement * (next / distance);
            next += step;
        }
        // Drop excess points instead of building an emission backlog after a large move.
        if (next <= distance) next += (Mathf.Floor((distance - next) / step) + 1f) * step;
        distanceUntilNext = next - distance;
        return count;
    }
}

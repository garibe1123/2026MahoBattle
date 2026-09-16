using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight scene-local pool for short-lived prefab instances such as combat effects and telegraphs.
/// Specialized pools (ProjectilePooler / MonsterPool) remain responsible for their own gameplay objects.
/// </summary>
public static class PoolService
{
    private static PoolServiceHost host;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        host = null;
    }

    public static GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent = null)
    {
        if (prefab == null)
            return null;

        return GetHost().Spawn(prefab, position, rotation, parent);
    }

    public static T Spawn<T>(
        T prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent = null)
        where T : Component
    {
        if (prefab == null)
            return null;

        GameObject instance = Spawn(prefab.gameObject, position, rotation, parent);
        return instance != null ? instance.GetComponent<T>() : null;
    }

    public static void Release(GameObject instance)
    {
        if (instance == null)
            return;

        if (host != null && host.Release(instance))
            return;

        Object.Destroy(instance);
    }

    public static void Release(GameObject instance, float delay)
    {
        if (instance == null)
            return;

        if (delay <= 0f)
        {
            Release(instance);
            return;
        }

        PoolServiceHost currentHost = GetHost();
        if (currentHost.ScheduleRelease(instance, delay))
            return;

        Object.Destroy(instance, delay);
    }

    internal static void NotifyHostDestroyed(PoolServiceHost destroyedHost)
    {
        if (host == destroyedHost)
            host = null;
    }

    private static PoolServiceHost GetHost()
    {
        if (host != null)
            return host;

        GameObject go = new("[PoolService]");
        host = go.AddComponent<PoolServiceHost>();
        return host;
    }
}

internal sealed class PoolServiceHost : MonoBehaviour
{
    private sealed class PoolBucket
    {
        public readonly Queue<GameObject> inactive = new();
    }

    private readonly struct TimedRelease
    {
        public readonly GameObject instance;
        public readonly int generation;
        public readonly float releaseAt;

        public TimedRelease(GameObject instance, int generation, float releaseAt)
        {
            this.instance = instance;
            this.generation = generation;
            this.releaseAt = releaseAt;
        }
    }

    private readonly Dictionary<GameObject, PoolBucket> buckets = new();
    private readonly Dictionary<GameObject, GameObject> prefabByInstance = new();
    private readonly Dictionary<GameObject, int> generationByInstance = new();
    private readonly HashSet<GameObject> inactiveInstances = new();
    private readonly List<TimedRelease> timedReleases = new();

    public GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent)
    {
        if (!buckets.TryGetValue(prefab, out PoolBucket bucket))
        {
            bucket = new PoolBucket();
            buckets.Add(prefab, bucket);
        }

        GameObject instance = null;
        while (bucket.inactive.Count > 0 && instance == null)
            instance = bucket.inactive.Dequeue();

        if (instance == null)
        {
            instance = Instantiate(prefab, position, rotation, parent);
            instance.name = prefab.name + " (Pooled)";
            prefabByInstance[instance] = prefab;
            generationByInstance[instance] = 0;
        }
        else
        {
            inactiveInstances.Remove(instance);
            instance.transform.SetParent(parent, true);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
        }

        int generation = generationByInstance.TryGetValue(instance, out int previous)
            ? previous + 1
            : 1;
        generationByInstance[instance] = generation;
        return instance;
    }

    public bool Release(GameObject instance)
    {
        if (instance == null || !prefabByInstance.TryGetValue(instance, out GameObject prefab))
            return false;

        if (inactiveInstances.Contains(instance))
            return true;

        generationByInstance[instance] = generationByInstance.TryGetValue(instance, out int previous)
            ? previous + 1
            : 1;

        instance.SetActive(false);
        instance.transform.SetParent(transform, false);

        if (!buckets.TryGetValue(prefab, out PoolBucket bucket))
        {
            bucket = new PoolBucket();
            buckets.Add(prefab, bucket);
        }

        inactiveInstances.Add(instance);
        bucket.inactive.Enqueue(instance);
        return true;
    }

    public bool ScheduleRelease(GameObject instance, float delay)
    {
        if (instance == null ||
            !prefabByInstance.ContainsKey(instance) ||
            inactiveInstances.Contains(instance))
        {
            return false;
        }

        int generation = generationByInstance.TryGetValue(instance, out int current)
            ? current
            : 0;

        timedReleases.Add(new TimedRelease(
            instance,
            generation,
            Time.time + Mathf.Max(0f, delay)));
        return true;
    }

    private void Update()
    {
        float now = Time.time;
        for (int i = timedReleases.Count - 1; i >= 0; i--)
        {
            TimedRelease pending = timedReleases[i];
            if (pending.instance == null)
            {
                timedReleases.RemoveAt(i);
                continue;
            }

            if (now < pending.releaseAt)
                continue;

            timedReleases.RemoveAt(i);
            if (!generationByInstance.TryGetValue(pending.instance, out int currentGeneration) ||
                currentGeneration != pending.generation)
            {
                continue;
            }

            Release(pending.instance);
        }
    }

    private void OnDestroy()
    {
        timedReleases.Clear();
        buckets.Clear();
        prefabByInstance.Clear();
        generationByInstance.Clear();
        inactiveInstances.Clear();
        PoolService.NotifyHostDestroyed(this);
    }
}

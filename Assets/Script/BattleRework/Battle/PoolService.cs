using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-local pool for short-lived prefab instances such as combat effects and telegraphs.
/// Specialized pools (ProjectilePooler / MonsterPool) remain responsible for their own gameplay objects.
/// </summary>
public sealed class PoolService : MonoBehaviour
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

    private static PoolService service;

    private readonly Dictionary<GameObject, PoolBucket> buckets = new();
    private readonly Dictionary<GameObject, GameObject> prefabByInstance = new();
    private readonly Dictionary<GameObject, int> generationByInstance = new();
    private readonly HashSet<GameObject> inactiveInstances = new();
    private readonly List<TimedRelease> timedReleases = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        service = null;
    }

    private void Awake()
    {
        if (service == null)
        {
            service = this;
            return;
        }

        if (service != this)
            enabled = false;
    }

    public static GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent = null)
    {
        if (prefab == null)
            return null;

        return GetOrCreate().SpawnInternal(prefab, position, rotation, parent);
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

        if (service != null && service.ReleaseInternal(instance))
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

        PoolService current = GetOrCreate();
        if (current.ScheduleReleaseInternal(instance, delay))
            return;

        Object.Destroy(instance, delay);
    }

    private static PoolService GetOrCreate()
    {
        if (service != null)
            return service;

        GameObject go = new("[PoolService]");
        service = go.AddComponent<PoolService>();
        return service;
    }

    private GameObject SpawnInternal(
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
        }

        generationByInstance[instance] = generationByInstance.TryGetValue(instance, out int previous)
            ? previous + 1
            : 1;

        if (!instance.activeSelf)
            instance.SetActive(true);

        return instance;
    }

    private bool ReleaseInternal(GameObject instance)
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

    private bool ScheduleReleaseInternal(GameObject instance, float delay)
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

            ReleaseInternal(pending.instance);
        }
    }

    private void OnDestroy()
    {
        timedReleases.Clear();
        buckets.Clear();
        prefabByInstance.Clear();
        generationByInstance.Clear();
        inactiveInstances.Clear();

        if (service == this)
            service = null;
    }
}

using System.Collections.Generic;
using UnityEngine;

public class ProjectilePooler : MonoBehaviour
{
    public Projectile projectilePrefab;
    [Min(0)] public int initialSize = 100;

    private readonly Queue<Projectile> queue = new();
    private readonly HashSet<Projectile> pooled = new();
    private bool initialized;

    private void Awake()
    {
        // BattleScene은 런타임 self-repair를 지원하므로 AddComponent 직후에는
        // prefab이 아직 배선되지 않았을 수 있습니다. null을 오류로 확정하지 않고
        // Configure/Get 시점까지 초기화를 지연합니다.
        TryInitialize();
    }

    public void Configure(Projectile prefab, int? preloadSize = null)
    {
        if (prefab != null)
            projectilePrefab = prefab;

        if (preloadSize.HasValue)
            initialSize = Mathf.Max(0, preloadSize.Value);

        TryInitialize();
    }

    private void TryInitialize()
    {
        if (initialized || projectilePrefab == null)
            return;

        int preloadCount = Mathf.Max(0, initialSize);
        for (int i = queue.Count; i < preloadCount; i++)
        {
            if (CreateOne() == null)
                break;
        }

        initialized = true;
    }

    private Projectile CreateOne()
    {
        if (projectilePrefab == null)
            return null;

        Projectile projectile = Instantiate(projectilePrefab, transform);
        projectile.gameObject.SetActive(false);
        queue.Enqueue(projectile);
        pooled.Add(projectile);
        return projectile;
    }

    public Projectile Get()
    {
        TryInitialize();

        if (projectilePrefab == null)
        {
            Debug.LogError($"[ProjectilePool] Cannot Get projectile because projectilePrefab is null on '{name}'.");
            return null;
        }

        if (queue.Count == 0 && CreateOne() == null)
            return null;

        Projectile projectile = queue.Dequeue();
        pooled.Remove(projectile);
        return projectile;
    }

    public void Return(Projectile projectile)
    {
        if (projectile == null || pooled.Contains(projectile))
            return;

        projectile.gameObject.SetActive(false);
        projectile.transform.SetParent(transform);
        queue.Enqueue(projectile);
        pooled.Add(projectile);
    }
}

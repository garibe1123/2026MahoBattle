using System.Collections.Generic;
using UnityEngine;

public class ProjectilePooler : MonoBehaviour
{
    public Projectile projectilePrefab;
    [Min(0)] public int initialSize = 100;

    private readonly Queue<Projectile> queue = new();
    private readonly HashSet<Projectile> pooled = new();

    private void Awake()
    {
        if (projectilePrefab == null)
        {
            Debug.LogError($"[ProjectilePool] projectilePrefab is null on '{name}'.");
            return;
        }

        for (int i = 0; i < initialSize; i++)
            CreateOne();
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

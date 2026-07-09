using System.Collections.Generic;
using UnityEngine;

public class ProjectilePooler : MonoBehaviour
{
    public Projectile projectilePrefab;
    public int initialSize = 200;

    private readonly Queue<Projectile> q = new Queue<Projectile>();

    void Awake()
    {
        for (int i = 0; i < initialSize; i++)
            CreateOne();
    }

    Projectile CreateOne()
    {
        var p = Instantiate(projectilePrefab, transform);
        p.gameObject.SetActive(false);
        q.Enqueue(p);
        return p;
    }

    public Projectile Get()
    {
        if (q.Count == 0) CreateOne();
        return q.Dequeue();
    }

    public void Return(Projectile p)
    {
        p.gameObject.SetActive(false);
        q.Enqueue(p);
    }
}

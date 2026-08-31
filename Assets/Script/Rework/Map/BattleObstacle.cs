using UnityEngine;

public class BattleObstacle : MonoBehaviour, IDamageable
{
    public ObstacleType obstacleType = ObstacleType.HighWall;

    [Header("Durability")]
    [Min(1f)] public float maxDurability = 100f;
    [Min(0f)] public float conditionalBreakThreshold = 40f;

    public bool IsAlive => !isDestroyed;

    private float currentDurability;
    private bool isDestroyed;
    private bool isBreakable;

    private void Awake()
    {
        currentDurability = maxDurability;
        isBreakable = obstacleType == ObstacleType.BreakableWall;
    }

    public bool BlocksMovement => !isDestroyed;
    public bool BlocksProjectile => !isDestroyed && obstacleType != ObstacleType.LowWall;

    public void ActivateConditionalBreak()
    {
        if (obstacleType == ObstacleType.ConditionalWall)
            isBreakable = true;
    }

    public void ReceiveDamage(DamageContext context)
    {
        if (isDestroyed) return;

        if (obstacleType == ObstacleType.ConditionalWall && !isBreakable)
        {
            if (context.BaseDamage >= conditionalBreakThreshold)
                ActivateConditionalBreak();
            else
                return;
        }

        if (!isBreakable) return;

        currentDurability -= Mathf.Max(0f, context.BaseDamage * context.DamageMultiplier);
        if (currentDurability <= 0f)
            Break();
    }

    private void Break()
    {
        isDestroyed = true;

        foreach (Collider2D col in GetComponentsInChildren<Collider2D>())
            col.enabled = false;

        gameObject.SetActive(false);
    }
}

public enum ObstacleType
{
    HighWall,
    LowWall,
    BreakableWall,
    ConditionalWall
}

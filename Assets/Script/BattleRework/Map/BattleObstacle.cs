using UnityEngine;

public enum BattleObstacleType
{
    HighWall,
    LowWall,
    BreakableWall,
    ConditionalWall
}

/// <summary>
/// 맵 설계서의 4종 장애물을 하나의 컴포넌트로 처리합니다.
/// HighWall: 이동/투사체 차단, 파괴 불가
/// LowWall: 이동만 차단
/// BreakableWall: 이동/투사체 차단, 내구도 파괴
/// ConditionalWall: 처음에는 LowWall, 활성화 후 BreakableWall
/// </summary>
public class BattleObstacle : MonoBehaviour, IDamageable
{
    [SerializeField] private BattleObstacleType obstacleType;
    [SerializeField] private Collider2D movementCollider;
    [SerializeField] private Collider2D projectileBlocker;

    [Header("Durability - Open Issue values are inspector driven")]
    [SerializeField] private float maxDurability = 100f;
    [SerializeField] private float conditionalMeleeThreshold = 20f;

    private float currentDurability;
    private bool conditionalActivated;
    private bool destroyed;

    public BattleObstacleType Type => obstacleType;
    public bool IsAlive => !destroyed;
    public float Defense => 0f;
    public bool IsConditionalActivated => conditionalActivated;

    private void Awake()
    {
        ResetObstacle();
    }

    public void ResetObstacle()
    {
        destroyed = false;
        conditionalActivated = obstacleType != BattleObstacleType.ConditionalWall;
        currentDurability = Mathf.Max(1f, maxDurability);

        if (movementCollider != null)
            movementCollider.enabled = true;

        RefreshProjectileBlocking();
    }

    public void ActivateConditionalBreakable()
    {
        if (obstacleType != BattleObstacleType.ConditionalWall || destroyed)
            return;

        conditionalActivated = true;
        RefreshProjectileBlocking();
    }

    public void ReceiveDamage(DamageContext context, float finalDamage)
    {
        if (destroyed) return;

        if (obstacleType == BattleObstacleType.HighWall || obstacleType == BattleObstacleType.LowWall)
            return;

        if (obstacleType == BattleObstacleType.ConditionalWall && !conditionalActivated)
        {
            if (context.Kind != DamageKind.Melee || finalDamage < conditionalMeleeThreshold)
                return;

            ActivateConditionalBreakable();
        }

        currentDurability -= finalDamage;
        if (currentDurability <= 0f)
            Break();
    }

    private void Break()
    {
        destroyed = true;

        if (movementCollider != null)
            movementCollider.enabled = false;

        if (projectileBlocker != null)
            projectileBlocker.enabled = false;

        // 파괴 애니메이션/파편은 비주얼 레이어에서 후속 연결합니다.
    }

    private void RefreshProjectileBlocking()
    {
        if (projectileBlocker == null) return;

        projectileBlocker.enabled = obstacleType switch
        {
            BattleObstacleType.HighWall => true,
            BattleObstacleType.LowWall => false,
            BattleObstacleType.BreakableWall => true,
            BattleObstacleType.ConditionalWall => conditionalActivated,
            _ => false
        };
    }
}

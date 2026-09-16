using UnityEngine;

[CreateAssetMenu(menuName = "Game/Projectile/Projectile SO")]
public class ProjectileSO : ScriptableObject
{
    [Header("Base")]
    public float speed = 10f;
    public float lifetime = 3f;

    [Header("Movement")]
    public MovementType movement;

    public float arcGravity = 30f;

    public float sineAmplitudeDeg = 20f;
    public float sineFrequency = 2f;

    [Header("Homing")]
    public float homingTurnSpeed = 5f;
    public float homingSlowFactor = 0.7f;

    [Header("DelayRush")]
    public float delayTime = 0.5f;

    [Header("Bounce")]
    public int maxBounceCount = 3;

    [Header("Impact")]
    public ImpactType impact;

    //public GameObject explosionEffectPrefab;
    //public GameObject groundEffectPrefab;
    public EffectSO explosionEffectSO;
    public EffectSO groundEffectSO;

    [Header("Split")]
    public int splitCount = 3;
    public float splitAngle = 60f;
    public ProjectileSO splitChildSO;

    [Header("Explosion Damage")]
    public float explosionRadius = 2f;
    public LayerMask damageLayer;

    [Header("Visual")]
    public ProjectileVisualSO visual;

    [Header("Arc Target Mode")]
    public bool useTargetPosition;
    public float arcHeight = 3f;

    [Header("Telegraph")]
    public GameObject telegraphPrefab;
    public float telegraphDuration = 1f;

    [Header("폴리곤 너비 지정")]
    public float colliderRadius = .5f;

    [Header("부메랑 복귀시점 초")]
    public float boomerangReturnTime = 1f;

    [Header("데미지")]
    public int damage = 1;

    [Header("Pierce Settings")]
    [Tooltip("0이면 즉시 소멸, 양수면 해당 수만큼 적을 뚫고 지나갑니다.")]
    public int basePierceCount = 0; //
}

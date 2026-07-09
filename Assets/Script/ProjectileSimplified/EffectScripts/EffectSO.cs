using UnityEngine;

[CreateAssetMenu(menuName = "Game/Effect/Effect SO")]
public class EffectSO : ScriptableObject
{
    [Header("프리팹 (껍데기)")]
    public Effect prefab; // ★ 이 SO가 생성할 실제 프리팹 객체

    [Header("Base")]
    public EffectTypeEnum effectType;
    public EffectVisualSO visual;
    public float duration = 5f;

    [Header("Damage & Targeting")]
    public LayerMask targetLayer;
    public float damage = 10f;
    public float tickRate = 0.5f;
    public float radius = 2f;
    public int maxTargetsPerTick = 50;

    [Header("Movement & Rotation")]
    public EffectMovementType movementType;
    public float moveSpeed = 5f;
    public EffectRotationType rotationType;
    public float spinSpeed = 180f;

    [Header("Scale Dynamics")]
    public EffectScaleType scaleType;
    public float startScale = 1f;
    public float targetScale = 3f;

    [Header("Modifiers")]
    public bool isAttached = false;
    public bool applySlow = false;

    [Header("Laser Setting")]
    public Vector2 laserSize = new Vector2(10f, 1f);
    public LayerMask blockingLayer;
    public float laserExtensionSpeed = 0f;

    [Header("Mine Setting")]
    public float mineDelay = 0.5f;
    public float mineExplosionRadius = 3f;
    public bool chainReaction = true;

    [Header("Spawner Setting")]
    public GameObject spawnPrefab;
    public float spawnInterval = 1f;

    public Effect Spawn(Vector3 position, Vector2 direction, Transform target = null, float scaleMultiplier = 1f)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"[{name}] EffectSO에 프리팹이 등록되지 않았습니다!");
            return null;
        }

        // 1. 방향 벡터(x, y)를 유니티의 회전값(Quaternion)으로 변환합니다.
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Quaternion startRotation = Quaternion.Euler(0, 0, angle);

        // 2. 변환된 회전값을 적용해서 생성! (이제 기본적으로 총알이 날아가던 방향을 봅니다)
        Effect instance = Instantiate(prefab, position, startRotation);

        instance.Setup(this, target, scaleMultiplier);
        return instance;
    }
}
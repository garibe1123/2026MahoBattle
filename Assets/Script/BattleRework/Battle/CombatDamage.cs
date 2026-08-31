using UnityEngine;

public enum DamageKind
{
    Projectile,
    Melee,
    Area,
    Environment
}

public readonly struct DamageContext
{
    public readonly GameObject Source;
    public readonly Vector2 SourcePosition;
    public readonly float BaseDamage;
    public readonly float DamageMultiplier;
    public readonly float FanMissionModifier;
    public readonly DamageKind Kind;
    public readonly bool IgnoreDefense;

    public DamageContext(
        GameObject source,
        Vector2 sourcePosition,
        float baseDamage,
        float damageMultiplier = 1f,
        float fanMissionModifier = 0f,
        DamageKind kind = DamageKind.Projectile,
        bool ignoreDefense = false)
    {
        Source = source;
        SourcePosition = sourcePosition;
        BaseDamage = Mathf.Max(0f, baseDamage);
        DamageMultiplier = Mathf.Max(0f, damageMultiplier);
        FanMissionModifier = fanMissionModifier;
        Kind = kind;
        IgnoreDefense = ignoreDefense;
    }
}

public interface IDamageable
{
    bool IsAlive { get; }
    float Defense { get; }
    void ReceiveDamage(DamageContext context, float finalDamage);
}

/// <summary>
/// 공통 데미지 계산과 IDamageable 탐색을 한 곳에서 처리합니다.
/// Final = Base × Runtime Multiplier × (1 + FanMission Modifier) - Defense
/// </summary>
public static class CombatDamage
{
    public static float Calculate(DamageContext context, float defense)
    {
        float raw = context.BaseDamage
                    * context.DamageMultiplier
                    * Mathf.Max(0f, 1f + context.FanMissionModifier);

        if (!context.IgnoreDefense)
            raw -= Mathf.Max(0f, defense);

        return Mathf.Max(0f, raw);
    }

    public static bool TryApply(Collider2D target, DamageContext context)
    {
        if (target == null) return false;
        return TryApply(target.transform, context);
    }

    public static bool TryApply(GameObject target, DamageContext context)
    {
        if (target == null) return false;
        return TryApply(target.transform, context);
    }

    public static bool TryApply(Component target, DamageContext context)
    {
        if (target == null) return false;
        return TryApply(target.transform, context);
    }

    private static bool TryApply(Transform targetTransform, DamageContext context)
    {
        if (!TryFindDamageable(targetTransform, out IDamageable damageable))
            return false;

        if (!damageable.IsAlive)
            return false;

        float finalDamage = Calculate(context, damageable.Defense);
        damageable.ReceiveDamage(context, finalDamage);
        return true;
    }

    /// <summary>
    /// Collider가 자식 오브젝트에 붙어 있어도 부모의 Player/Monster IDamageable을 찾습니다.
    /// </summary>
    public static bool TryFindDamageable(Transform start, out IDamageable damageable)
    {
        damageable = null;
        if (start == null) return false;

        Transform current = start;
        while (current != null)
        {
            MonoBehaviour[] behaviours = current.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IDamageable found)
                {
                    damageable = found;
                    return true;
                }
            }

            current = current.parent;
        }

        return false;
    }
}

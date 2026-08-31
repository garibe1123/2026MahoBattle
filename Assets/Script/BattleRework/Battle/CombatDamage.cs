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
/// 기획서의 공통 데미지 공식을 한 곳에서 처리합니다.
/// Final = Base × Item/Runtime Multiplier × (1 + FanMission Modifier) - Defense
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
        return TryApply(target.gameObject, context);
    }

    public static bool TryApply(GameObject target, DamageContext context)
    {
        if (target == null) return false;

        MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is not IDamageable damageable || !damageable.IsAlive)
                continue;

            float finalDamage = Calculate(context, damageable.Defense);
            damageable.ReceiveDamage(context, finalDamage);
            return true;
        }

        return false;
    }
}

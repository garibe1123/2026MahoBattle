using UnityEngine;

public interface IDamageable
{
    bool IsAlive { get; }
    void ReceiveDamage(DamageContext context);
}

public readonly struct DamageContext
{
    public readonly GameObject Source;
    public readonly float BaseDamage;
    public readonly float DamageMultiplier;
    public readonly float MissionModifier;
    public readonly bool IgnoreDefense;

    public DamageContext(
        GameObject source,
        float baseDamage,
        float damageMultiplier = 1f,
        float missionModifier = 0f,
        bool ignoreDefense = false)
    {
        Source = source;
        BaseDamage = baseDamage;
        DamageMultiplier = damageMultiplier;
        MissionModifier = missionModifier;
        IgnoreDefense = ignoreDefense;
    }
}

public static class CombatDamage
{
    public static float Calculate(DamageContext context, float defense)
    {
        float raw = context.BaseDamage
                    * Mathf.Max(0f, context.DamageMultiplier)
                    * Mathf.Max(0f, 1f + context.MissionModifier);

        if (!context.IgnoreDefense)
            raw -= Mathf.Max(0f, defense);

        return Mathf.Max(0f, raw);
    }

    public static bool Apply(Component target, DamageContext context)
    {
        if (target == null) return false;

        IDamageable damageable = target.GetComponent<IDamageable>();
        if (damageable == null || !damageable.IsAlive) return false;

        damageable.ReceiveDamage(context);
        return true;
    }
}

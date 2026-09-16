using System;

/// <summary>
/// Shared post-damage events for systems that should observe combat results without owning damage rules.
/// FanMission and presentation systems can subscribe here instead of adding target-specific branches.
/// </summary>
public static class CombatEvents
{
    public static event Action<DamageContext, IDamageable, float> DamageApplied;
    public static event Action<DamageContext, IDamageable> TargetKilled;

    public static void Damaged(in DamageContext context, IDamageable target, float finalDamage)
    {
        if (target == null)
            return;

        DamageApplied?.Invoke(context, target, finalDamage);
    }

    public static void Killed(in DamageContext context, IDamageable target)
    {
        if (target == null)
            return;

        TargetKilled?.Invoke(context, target);
    }
}

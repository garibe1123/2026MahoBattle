using UnityEngine;

public enum DamageKind
{
    Projectile,
    Area,
    Contact,
    Hazard,
    Other
}

/// <summary>
/// Source metadata for the unified combat damage pipeline.
/// BaseDamage is the source-authored value. The runtime modifiers are applied by CombatDamage.Calculate.
/// WeaponId / EquipmentTag are optional semantic identifiers for systems such as FanMission.
/// </summary>
public readonly struct DamageContext
{
    public readonly GameObject Source;
    public readonly Vector2 HitPoint;
    public readonly float BaseDamage;
    public readonly float DamageMultiplier;
    public readonly float FanMissionModifier;
    public readonly DamageKind Kind;
    public readonly string WeaponId;
    public readonly string EquipmentTag;

    public DamageContext(
        GameObject source,
        Vector2 hitPoint,
        float baseDamage,
        float damageMultiplier = 1f,
        float fanMissionModifier = 0f,
        DamageKind kind = DamageKind.Other,
        string weaponId = null,
        string equipmentTag = null)
    {
        Source = source;
        HitPoint = hitPoint;
        BaseDamage = Mathf.Max(0f, baseDamage);
        DamageMultiplier = Mathf.Max(0f, damageMultiplier);
        FanMissionModifier = fanMissionModifier;
        Kind = kind;
        WeaponId = weaponId ?? string.Empty;
        EquipmentTag = equipmentTag ?? string.Empty;
    }
}

/// <summary>
/// Implemented by every runtime target that participates in shared projectile/effect damage.
/// </summary>
public interface IDamageable
{
    bool IsAlive { get; }
    float Defense { get; }
    void ReceiveDamage(DamageContext context, float finalDamage);
}

public static class CombatDamage
{
    public static float Calculate(in DamageContext context, float defense)
    {
        float scaled = context.BaseDamage * context.DamageMultiplier;
        float afterDefense = Mathf.Max(0f, scaled - Mathf.Max(0f, defense));
        return Mathf.Max(0f, afterDefense + context.FanMissionModifier);
    }

    /// <summary>
    /// Finds the nearest IDamageable without allocating a MonoBehaviour array for every hit.
    /// The non-generic Component lookup accepts interface types and returns the implementing Component.
    /// </summary>
    public static bool TryFindDamageable(Transform start, out IDamageable damageable)
    {
        if (start == null)
        {
            damageable = null;
            return false;
        }

        Component component = start.GetComponentInParent(typeof(IDamageable));
        damageable = component as IDamageable;
        return damageable != null;
    }

    /// <summary>
    /// Applies one resolved DamageContext to an already identified target and emits the shared result events.
    /// Returns false only when there is no live target to receive the damage.
    /// </summary>
    public static bool Apply(IDamageable target, in DamageContext context)
    {
        if (target == null || !target.IsAlive)
            return false;

        float finalDamage = Calculate(context, target.Defense);
        target.ReceiveDamage(context, finalDamage);
        CombatEvents.Damaged(context, target, finalDamage);

        if (!target.IsAlive)
            CombatEvents.Killed(context, target);

        return true;
    }

    public static bool TryApply(Collider2D target, in DamageContext context)
    {
        return target != null &&
               TryFindDamageable(target.transform, out IDamageable damageable) &&
               Apply(damageable, context);
    }
}

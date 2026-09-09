using System;
using System.Collections.Generic;
using UnityEngine;

public enum BattleGridSynergyKind
{
    SharedTag,
    PrecisionCircuit,
    DetonationChain,
    ShockControl,
    RushMelee,
    SustainGuard
}

[Serializable]
public sealed class BattleGridSynergyLink
{
    public int slotA;
    public int slotB;
    public BattleGridSynergyKind kind;
    public string displayName;
    public float damageMultiplier = 1f;

    public BattleGridSynergyLink(int a, int b, BattleGridSynergyKind resolvedKind, string name, float multiplier)
    {
        slotA = a;
        slotB = b;
        kind = resolvedKind;
        displayName = name;
        damageMultiplier = multiplier;
    }
}

/// <summary>
/// 9개의 기존 BattleEquipment 슬롯을 3x3 공간 인벤토리로 해석합니다.
/// 상하좌우로 맞닿은 장비의 Tag 조합을 읽어 Backpack-style Grid Link를 만들고,
/// 기존 SynergyManager의 전역 Tag 시너지와 함께 현재 무기 Damage Multiplier에 합산합니다.
///
/// 기존 BattleEquipmentSO나 사용자 제작 Asset은 수정하지 않습니다.
/// 슬롯의 위치 자체가 빌드 데이터가 되므로 Reward에서 어느 칸에 장비를 놓는지가 의미를 가집니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1200)]
public sealed class BattleGridSynergyController : MonoBehaviour
{
    public const int GridSize = 3;

    [Header("References")]
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private SynergyManager synergyManager;
    [SerializeField] private PlayerShootingSystem shootingSystem;

    [Header("Adjacency Link Damage")]
    [SerializeField, Range(1f, 1.15f)] private float sharedTagDamage = 1.025f;
    [SerializeField, Range(1f, 1.25f)] private float precisionCircuitDamage = 1.07f;
    [SerializeField, Range(1f, 1.25f)] private float detonationChainDamage = 1.09f;
    [SerializeField, Range(1f, 1.25f)] private float shockControlDamage = 1.07f;
    [SerializeField, Range(1f, 1.25f)] private float rushMeleeDamage = 1.06f;
    [SerializeField, Range(1f, 1.20f)] private float sustainGuardDamage = 1.03f;
    [SerializeField, Range(1f, 2f)] private float maximumGridDamageMultiplier = 1.45f;

    private readonly List<BattleGridSynergyLink> activeLinks = new();
    private bool subscribed;

    public IReadOnlyList<BattleGridSynergyLink> ActiveLinks => activeLinks;
    public float GridDamageMultiplier { get; private set; } = 1f;
    public event Action GridSynergiesChanged;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        ResolveGridSynergies();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (equipmentSystem == null || shootingSystem == null)
        {
            ResolveReferences();
            Subscribe();
        }
    }

    private void ResolveReferences()
    {
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (synergyManager == null)
            synergyManager = FindFirstObjectByType<SynergyManager>();
        if (shootingSystem == null)
            shootingSystem = FindFirstObjectByType<PlayerShootingSystem>();
    }

    private void Subscribe()
    {
        if (subscribed || equipmentSystem == null)
            return;

        equipmentSystem.InventoryChanged += ResolveGridSynergies;
        equipmentSystem.EquippedSlotChanged += HandleEquippedSlotChanged;
        if (synergyManager != null)
            synergyManager.SynergiesChanged += HandleLegacySynergiesChanged;
        if (shootingSystem != null)
            shootingSystem.WeaponChanged += HandleWeaponChanged;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (equipmentSystem != null)
        {
            equipmentSystem.InventoryChanged -= ResolveGridSynergies;
            equipmentSystem.EquippedSlotChanged -= HandleEquippedSlotChanged;
        }
        if (synergyManager != null)
            synergyManager.SynergiesChanged -= HandleLegacySynergiesChanged;
        if (shootingSystem != null)
            shootingSystem.WeaponChanged -= HandleWeaponChanged;
        subscribed = false;
    }

    private void HandleEquippedSlotChanged(int _)
    {
        ApplyRuntimeDamageMultiplier();
    }

    private void HandleLegacySynergiesChanged()
    {
        ApplyRuntimeDamageMultiplier();
    }

    private void HandleWeaponChanged(PlayerShootingSO _)
    {
        ApplyRuntimeDamageMultiplier();
    }

    [ContextMenu("Resolve 3x3 Grid Synergies")]
    public void ResolveGridSynergies()
    {
        ResolveReferences();
        activeLinks.Clear();
        GridDamageMultiplier = 1f;

        if (equipmentSystem == null)
        {
            GridSynergiesChanged?.Invoke();
            return;
        }

        IReadOnlyList<BattleEquipmentSlot> slots = equipmentSystem.Slots;
        int unlocked = Mathf.Min(equipmentSystem.UnlockedSlotCount, BattleEquipmentSystem.MaxSlotCount);

        for (int index = 0; index < unlocked; index++)
        {
            BattleEquipmentSO equipment = slots[index]?.equipment;
            if (equipment == null)
                continue;

            int x = index % GridSize;
            int y = index / GridSize;

            if (x + 1 < GridSize)
                TryCreateLink(index, index + 1, slots, unlocked);
            if (y + 1 < GridSize)
                TryCreateLink(index, index + GridSize, slots, unlocked);
        }

        float multiplier = 1f;
        for (int i = 0; i < activeLinks.Count; i++)
            multiplier *= Mathf.Max(1f, activeLinks[i].damageMultiplier);
        GridDamageMultiplier = Mathf.Min(Mathf.Max(1f, maximumGridDamageMultiplier), multiplier);

        ApplyRuntimeDamageMultiplier();
        GridSynergiesChanged?.Invoke();
    }

    private void TryCreateLink(
        int a,
        int b,
        IReadOnlyList<BattleEquipmentSlot> slots,
        int unlocked)
    {
        if (a < 0 || b < 0 || a >= unlocked || b >= unlocked || a >= slots.Count || b >= slots.Count)
            return;

        BattleEquipmentSO first = slots[a]?.equipment;
        BattleEquipmentSO second = slots[b]?.equipment;
        if (first == null || second == null)
            return;

        if (!TryResolveKind(first, second, out BattleGridSynergyKind kind, out string name, out float damage))
            return;

        activeLinks.Add(new BattleGridSynergyLink(a, b, kind, name, damage));
    }

    private bool TryResolveKind(
        BattleEquipmentSO a,
        BattleEquipmentSO b,
        out BattleGridSynergyKind kind,
        out string name,
        out float damage)
    {
        if (MatchesEither(a, b, EquipmentTag.Projectile, EquipmentTag.Precision) ||
            MatchesEither(a, b, EquipmentTag.Projectile, EquipmentTag.Critical) ||
            MatchesEither(a, b, EquipmentTag.Precision, EquipmentTag.Critical))
        {
            kind = BattleGridSynergyKind.PrecisionCircuit;
            name = "PRECISION CIRCUIT";
            damage = precisionCircuitDamage;
            return true;
        }

        if (MatchesEither(a, b, EquipmentTag.Explosion, EquipmentTag.Break) ||
            MatchesEither(a, b, EquipmentTag.Explosion, EquipmentTag.Area) ||
            MatchesEither(a, b, EquipmentTag.Explosion, EquipmentTag.Burn))
        {
            kind = BattleGridSynergyKind.DetonationChain;
            name = "DETONATION CHAIN";
            damage = detonationChainDamage;
            return true;
        }

        if (MatchesEither(a, b, EquipmentTag.Shock, EquipmentTag.Control))
        {
            kind = BattleGridSynergyKind.ShockControl;
            name = "SHOCK CONTROL";
            damage = shockControlDamage;
            return true;
        }

        if (MatchesEither(a, b, EquipmentTag.Dash, EquipmentTag.Melee))
        {
            kind = BattleGridSynergyKind.RushMelee;
            name = "RUSH LINK";
            damage = rushMeleeDamage;
            return true;
        }

        if (MatchesEither(a, b, EquipmentTag.Sustain, EquipmentTag.Defense) ||
            MatchesEither(a, b, EquipmentTag.Heal, EquipmentTag.Defense))
        {
            kind = BattleGridSynergyKind.SustainGuard;
            name = "SUSTAIN GUARD";
            damage = sustainGuardDamage;
            return true;
        }

        if (ShareAnyTag(a, b))
        {
            kind = BattleGridSynergyKind.SharedTag;
            name = "TAG LINK";
            damage = sharedTagDamage;
            return true;
        }

        kind = default;
        name = null;
        damage = 1f;
        return false;
    }

    private static bool MatchesEither(BattleEquipmentSO a, BattleEquipmentSO b, EquipmentTag first, EquipmentTag second)
    {
        return (a.HasTag(first) && b.HasTag(second)) ||
               (a.HasTag(second) && b.HasTag(first));
    }

    private static bool ShareAnyTag(BattleEquipmentSO a, BattleEquipmentSO b)
    {
        if (a.tags == null || b.tags == null)
            return false;

        for (int i = 0; i < a.tags.Count; i++)
            if (b.HasTag(a.tags[i]))
                return true;
        return false;
    }

    private void ApplyRuntimeDamageMultiplier()
    {
        if (equipmentSystem == null || shootingSystem == null)
            return;

        int equippedIndex = equipmentSystem.EquippedSlotIndex;
        BattleEquipmentSO equipped = null;
        if (equippedIndex >= 0 && equippedIndex < equipmentSystem.Slots.Count)
            equipped = equipmentSystem.Slots[equippedIndex]?.equipment;

        float equipmentDamage = equipped != null ? Mathf.Max(0f, equipped.damageMultiplier) : 1f;
        float legacySynergyDamage = synergyManager != null
            ? Mathf.Max(0f, synergyManager.ActiveDamageMultiplier)
            : 1f;

        shootingSystem.RuntimeDamageMultiplier =
            equipmentDamage * legacySynergyDamage * Mathf.Max(1f, GridDamageMultiplier);
    }
}

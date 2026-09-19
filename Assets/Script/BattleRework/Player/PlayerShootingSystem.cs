using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerShootingSystem : MonoBehaviour
{
    [Header("Settings")]
    public ProjectilePooler playerProjectilePool;
    public WeaponDisplay weaponDisplay;

    [Header("Weapon Inventory - legacy runtime bridge")]
    public List<PlayerShootingSO> unlockedWeapons = new();

    [HideInInspector] public PlayerShootingSO currentWeaponSO;
    public int currentWeaponIndex;
    public int extraPierce;

    [Header("Runtime Modifiers")]
    [SerializeField] private float runtimeDamageMultiplier = 1f;
    [SerializeField] private float battleRuleDamageMultiplier = 1f;
    [SerializeField] private float runtimeFanMissionModifier;

    private readonly List<int> ammoInventory = new();

    public int currentAmmo { get; private set; }
    public bool IsReloading => isReloading;

    public float RuntimeDamageMultiplier
    {
        get => runtimeDamageMultiplier;
        set => runtimeDamageMultiplier = Mathf.Max(0f, value);
    }

    public float BattleRuleDamageMultiplier
    {
        get => battleRuleDamageMultiplier;
        set => battleRuleDamageMultiplier = Mathf.Max(0f, value);
    }

    public float RuntimeFanMissionModifier
    {
        get => runtimeFanMissionModifier;
        set => runtimeFanMissionModifier = value;
    }

    public event Action<PlayerShootingSO> WeaponChanged;

    private bool isReloading;
    private float nextFireTime;
    private Coroutine reloadCoroutine;
    private Action shootAnimationCompleteCallback;
    private SpriteClipPlayer weaponClipPlayer;

    private void Awake()
    {
        shootAnimationCompleteCallback = HandleShootAnimationComplete;
        weaponClipPlayer = new SpriteClipPlayer(ApplyWeaponSprite);
    }

    private void Start()
    {
        EnsureWeaponInventoryInitialized();

        if (unlockedWeapons.Count > 0)
            EquipWeapon(Mathf.Clamp(currentWeaponIndex, 0, unlockedWeapons.Count - 1));
    }

    private void Update()
    {
        weaponClipPlayer?.Tick(Time.deltaTime);
    }

    private void EnsureWeaponInventoryInitialized()
    {
        unlockedWeapons ??= new List<PlayerShootingSO>();

        while (ammoInventory.Count < unlockedWeapons.Count)
        {
            PlayerShootingSO weapon = unlockedWeapons[ammoInventory.Count];
            ammoInventory.Add(weapon != null ? Mathf.Max(0, weapon.maxAmmo) : 0);
        }

        while (ammoInventory.Count > unlockedWeapons.Count)
            ammoInventory.RemoveAt(ammoInventory.Count - 1);
    }

    public void ResetRuntimeWeapons()
    {
        CancelReload();
        StopWeaponAnimation();

        unlockedWeapons ??= new List<PlayerShootingSO>();
        unlockedWeapons.Clear();
        ammoInventory.Clear();

        currentWeaponSO = null;
        currentWeaponIndex = 0;
        currentAmmo = 0;
        nextFireTime = 0f;
        runtimeDamageMultiplier = 1f;
        battleRuleDamageMultiplier = 1f;
        runtimeFanMissionModifier = 0f;

        WeaponChanged?.Invoke(null);
    }

    /// <summary>
    /// 장비 슬롯에서 빠진 무기를 ShootingSystem에서도 제거합니다.
    /// 현재 장착 무기를 제거할 때는 제거 전 탄약을 다른 무기 슬롯에 잘못 write-back하지 않도록
    /// current weapon 상태를 먼저 비운 뒤 다음 무기를 장착합니다.
    /// </summary>
    public bool UnregisterWeapon(PlayerShootingSO weapon)
    {
        if (weapon == null)
            return false;

        EnsureWeaponInventoryInitialized();
        int index = unlockedWeapons.IndexOf(weapon);
        if (index < 0)
            return false;

        bool wasCurrent = currentWeaponSO == weapon || currentWeaponIndex == index;

        if (wasCurrent)
        {
            CancelReload();
            StopWeaponAnimation();
            currentWeaponSO = null;
            currentAmmo = 0;
        }

        unlockedWeapons.RemoveAt(index);
        if (index < ammoInventory.Count)
            ammoInventory.RemoveAt(index);

        if (unlockedWeapons.Count == 0)
        {
            currentWeaponSO = null;
            currentWeaponIndex = 0;
            currentAmmo = 0;
            runtimeDamageMultiplier = 1f;
            WeaponChanged?.Invoke(null);
            return true;
        }

        if (!wasCurrent)
        {
            if (currentWeaponIndex > index)
                currentWeaponIndex--;
            return true;
        }

        currentWeaponIndex = Mathf.Clamp(index, 0, unlockedWeapons.Count - 1);
        EquipWeapon(currentWeaponIndex);
        return true;
    }

    public int RegisterWeapon(PlayerShootingSO weapon)
    {
        if (weapon == null)
            return -1;

        EnsureWeaponInventoryInitialized();

        int index = unlockedWeapons.IndexOf(weapon);
        if (index >= 0)
            return index;

        unlockedWeapons.Add(weapon);
        ammoInventory.Add(Mathf.Max(0, weapon.maxAmmo));
        return unlockedWeapons.Count - 1;
    }

    public bool RegisterWeaponAndEquip(PlayerShootingSO weapon)
    {
        int index = RegisterWeapon(weapon);
        if (index < 0)
            return false;

        EquipWeapon(index);
        return currentWeaponSO == weapon;
    }

    public void EquipWeapon(int index)
    {
        EnsureWeaponInventoryInitialized();
        if (index < 0 || index >= unlockedWeapons.Count)
            return;
        if (unlockedWeapons[index] == null)
            return;

        if (currentWeaponSO != null &&
            currentWeaponIndex >= 0 &&
            currentWeaponIndex < ammoInventory.Count)
        {
            ammoInventory[currentWeaponIndex] = currentAmmo;
        }

        CancelReload();
        StopWeaponAnimation();

        currentWeaponIndex = index;
        currentWeaponSO = unlockedWeapons[index];
        currentAmmo = Mathf.Clamp(ammoInventory[index], 0, Mathf.Max(0, currentWeaponSO.maxAmmo));
        nextFireTime = Time.time;

        WeaponChanged?.Invoke(currentWeaponSO);

        if (currentAmmo <= 0 && currentWeaponSO.maxAmmo > 0)
            ReloadFuncCall();
        else
            PlayWeaponAnimation(currentWeaponSO.idleSprites, true);
    }

    public void TryShoot(Vector3 mousePos, Transform target = null)
    {
        if (currentWeaponSO == null || isReloading || Time.time < nextFireTime)
            return;

        if (currentAmmo <= 0)
        {
            ReloadFuncCall();
            return;
        }

        if (playerProjectilePool == null)
        {
            Debug.LogError("[PlayerShooting] playerProjectilePool is null.");
            return;
        }

        Shoot(mousePos, target);
    }

    private void Shoot(Vector3 mousePos, Transform target)
    {
        PlayerShootingSO weapon = currentWeaponSO;
        if (weapon == null)
            return;

        currentAmmo--;
        if (currentWeaponIndex >= 0 && currentWeaponIndex < ammoInventory.Count)
            ammoInventory[currentWeaponIndex] = currentAmmo;

        nextFireTime = Time.time + Mathf.Max(0f, weapon.fireRate);

        Vector2 baseDir = (mousePos - transform.position).normalized;
        if (baseDir.sqrMagnitude <= 0.0001f)
            baseDir = Vector2.right;

        int shotCount = Mathf.Max(1, weapon.projectilesPerShot);
        float spread = weapon.spreadAngle;
        Vector3 spawnPosition = weaponDisplay != null
            ? weaponDisplay.transform.position
            : transform.position;
        int pierceBonus = Mathf.Max(0, extraPierce);

        for (int i = 0; i < shotCount; i++)
        {
            float angleOffset = shotCount == 1
                ? 0f
                : Mathf.Lerp(-spread, spread, (float)i / (shotCount - 1));

            Vector2 finalDir = Quaternion.Euler(0f, 0f, angleOffset) * baseDir;
            Projectile projectile = playerProjectilePool.Get();
            if (projectile == null)
                continue;

            projectile.transform.position = spawnPosition;
            projectile.Setup(
                weapon.projectileData,
                finalDir,
                playerProjectilePool,
                target,
                mousePos,
                gameObject,
                runtimeDamageMultiplier * battleRuleDamageMultiplier,
                runtimeFanMissionModifier);

            projectile.AddExtraPierce(pierceBonus);
        }

        if (currentAmmo <= 0)
        {
            ReloadFuncCall();
        }
        else
        {
            PlayWeaponAnimation(weapon.shootSprites, false, shootAnimationCompleteCallback);
        }
    }

    private void HandleShootAnimationComplete()
    {
        if (!isReloading && currentWeaponSO != null)
            PlayWeaponAnimation(currentWeaponSO.idleSprites, true);
    }

    public void ReloadFuncCall()
    {
        if (isReloading || currentWeaponSO == null)
            return;

        if (currentAmmo >= currentWeaponSO.maxAmmo)
            return;

        reloadCoroutine = StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        isReloading = true;
        PlayWeaponAnimation(currentWeaponSO.reloadSprites, true);

        float remaining = Mathf.Max(0f, currentWeaponSO.reloadTime);
        while (remaining > 0f)
        {
            remaining -= Time.deltaTime;
            yield return null;
        }

        if (currentWeaponSO != null)
        {
            currentAmmo = Mathf.Max(0, currentWeaponSO.maxAmmo);
            if (currentWeaponIndex >= 0 && currentWeaponIndex < ammoInventory.Count)
                ammoInventory[currentWeaponIndex] = currentAmmo;
        }

        isReloading = false;
        reloadCoroutine = null;

        if (currentWeaponSO != null)
            PlayWeaponAnimation(currentWeaponSO.idleSprites, true);
    }

    private void CancelReload()
    {
        if (reloadCoroutine != null)
        {
            StopCoroutine(reloadCoroutine);
            reloadCoroutine = null;
        }

        isReloading = false;
    }

    private void StopWeaponAnimation()
    {
        EnsureWeaponClipPlayer();
        weaponClipPlayer.Stop(clearClip: true);
    }

    private void PlayWeaponAnimation(Sprite[] frames, bool loop, Action onComplete = null)
    {
        EnsureWeaponClipPlayer();
        weaponClipPlayer.Stop(clearClip: true);

        if (frames == null || frames.Length == 0)
        {
            if (weaponDisplay != null && currentWeaponSO != null)
                weaponDisplay.UpdateWeaponSprite(currentWeaponSO.weaponSprite);

            onComplete?.Invoke();
            return;
        }

        float fps = currentWeaponSO != null
            ? Mathf.Max(1f, currentWeaponSO.animFps)
            : 12f;

        weaponClipPlayer.Play(
            frames,
            fps,
            loop,
            onComplete,
            completeEmptyImmediately: true);
    }

    private void EnsureWeaponClipPlayer()
    {
        if (weaponClipPlayer == null)
            weaponClipPlayer = new SpriteClipPlayer(ApplyWeaponSprite);
    }

    private void ApplyWeaponSprite(Sprite sprite)
    {
        if (weaponDisplay != null && sprite != null)
            weaponDisplay.UpdateWeaponSprite(sprite);
    }
}

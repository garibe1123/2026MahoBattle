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
    [SerializeField] private float runtimeFanMissionModifier;

    private readonly List<int> ammoInventory = new();

    public int currentAmmo { get; private set; }
    public bool IsReloading => isReloading;
    public float RuntimeDamageMultiplier
    {
        get => runtimeDamageMultiplier;
        set => runtimeDamageMultiplier = Mathf.Max(0f, value);
    }

    public float RuntimeFanMissionModifier
    {
        get => runtimeFanMissionModifier;
        set => runtimeFanMissionModifier = value;
    }

    public event Action<PlayerShootingSO> WeaponChanged;

    private bool isReloading;
    private float nextFireTime;
    private Coroutine currentAnimCoroutine;
    private Coroutine reloadCoroutine;

    private void Start()
    {
        EnsureWeaponInventoryInitialized();

        if (unlockedWeapons.Count > 0)
            EquipWeapon(Mathf.Clamp(currentWeaponIndex, 0, unlockedWeapons.Count - 1));
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

    /// <summary>
    /// 새 로그라이트 런이 시작될 때 BattleEquipmentSystem이 호출합니다.
    /// 구형 unlockedWeapons 브리지를 비워 이전 런의 무기가 남지 않도록 합니다.
    /// </summary>
    public void ResetRuntimeWeapons()
    {
        CancelReload();

        if (currentAnimCoroutine != null)
        {
            StopCoroutine(currentAnimCoroutine);
            currentAnimCoroutine = null;
        }

        unlockedWeapons ??= new List<PlayerShootingSO>();
        unlockedWeapons.Clear();
        ammoInventory.Clear();

        currentWeaponSO = null;
        currentWeaponIndex = 0;
        currentAmmo = 0;
        nextFireTime = 0f;
        runtimeDamageMultiplier = 1f;
        runtimeFanMissionModifier = 0f;

        WeaponChanged?.Invoke(null);
    }

    /// <summary>
    /// 버리기/교체로 장비 슬롯에서 빠진 무기를 구형 Shooting 브리지에서도 제거합니다.
    /// 현재 장착 중인 무기였다면 남은 첫 무기로 자동 전환합니다.
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
        unlockedWeapons.RemoveAt(index);
        if (index >= 0 && index < ammoInventory.Count)
            ammoInventory.RemoveAt(index);

        if (unlockedWeapons.Count == 0)
        {
            CancelReload();
            currentWeaponSO = null;
            currentWeaponIndex = 0;
            currentAmmo = 0;
            runtimeDamageMultiplier = 1f;
            WeaponChanged?.Invoke(null);
            return true;
        }

        if (currentWeaponIndex > index)
            currentWeaponIndex--;

        if (wasCurrent)
            EquipWeapon(Mathf.Clamp(index, 0, unlockedWeapons.Count - 1));

        return true;
    }

    /// <summary>
    /// BattleEquipmentSystem이 런 중 획득한 무기를 안전하게 ShootingSystem에 등록할 때 사용합니다.
    /// Start 이후 무기가 추가되어도 ammoInventory 길이가 어긋나지 않습니다.
    /// </summary>
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
        if (index < 0 || index >= unlockedWeapons.Count) return;
        if (unlockedWeapons[index] == null) return;

        if (currentWeaponSO != null &&
            currentWeaponIndex >= 0 &&
            currentWeaponIndex < ammoInventory.Count)
        {
            ammoInventory[currentWeaponIndex] = currentAmmo;
        }

        CancelReload();

        currentWeaponIndex = index;
        currentWeaponSO = unlockedWeapons[index];
        currentAmmo = ammoInventory[index];

        PlayWeaponAnimation(currentWeaponSO.idleSprites, true);
        WeaponChanged?.Invoke(currentWeaponSO);
    }

    public void TryShoot(Vector3 mousePos, Transform target = null)
    {
        if (isReloading || Time.time < nextFireTime || currentAmmo <= 0 || currentWeaponSO == null)
            return;

        if (playerProjectilePool == null)
        {
            Debug.LogError("[PlayerShooting] playerProjectilePool is null.");
            return;
        }

        Shoot(mousePos, target);
    }

    private void Shoot(Vector3 mousePos, Transform target)
    {
        currentAmmo--;
        if (currentWeaponIndex >= 0 && currentWeaponIndex < ammoInventory.Count)
            ammoInventory[currentWeaponIndex] = currentAmmo;

        nextFireTime = Time.time + Mathf.Max(0f, currentWeaponSO.fireRate);

        Vector2 baseDir = (mousePos - transform.position).normalized;
        if (baseDir.sqrMagnitude <= 0.0001f)
            baseDir = Vector2.right;

        int shotCount = Mathf.Max(1, currentWeaponSO.projectilesPerShot);
        float spread = currentWeaponSO.spreadAngle;

        for (int i = 0; i < shotCount; i++)
        {
            float angleOffset = shotCount == 1
                ? 0f
                : Mathf.Lerp(-spread, spread, (float)i / (shotCount - 1));

            Vector2 finalDir = Quaternion.Euler(0f, 0f, angleOffset) * baseDir;
            Projectile projectile = playerProjectilePool.Get();
            if (projectile == null) continue;

            Vector3 spawnPosition = weaponDisplay != null
                ? weaponDisplay.transform.position
                : transform.position;

            projectile.transform.position = spawnPosition;
            projectile.Setup(
                currentWeaponSO.projectileData,
                finalDir,
                playerProjectilePool,
                target,
                mousePos,
                gameObject,
                runtimeDamageMultiplier,
                runtimeFanMissionModifier);

            projectile.AddExtraPierce(Mathf.Max(0, extraPierce));
        }

        if (currentAmmo <= 0)
        {
            ReloadFuncCall();
        }
        else
        {
            PlayWeaponAnimation(currentWeaponSO.shootSprites, false, () =>
            {
                if (!isReloading)
                    PlayWeaponAnimation(currentWeaponSO.idleSprites, true);
            });
        }
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

        yield return new WaitForSeconds(Mathf.Max(0f, currentWeaponSO.reloadTime));

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

    private void PlayWeaponAnimation(Sprite[] frames, bool loop, Action onComplete = null)
    {
        if (currentAnimCoroutine != null)
        {
            StopCoroutine(currentAnimCoroutine);
            currentAnimCoroutine = null;
        }

        if (frames == null || frames.Length == 0)
        {
            if (weaponDisplay != null && currentWeaponSO != null)
                weaponDisplay.UpdateWeaponSprite(currentWeaponSO.weaponSprite);

            onComplete?.Invoke();
            return;
        }

        currentAnimCoroutine = StartCoroutine(AnimRoutine(frames, loop, onComplete));
    }

    private IEnumerator AnimRoutine(Sprite[] frames, bool loop, Action onComplete)
    {
        float fps = currentWeaponSO != null ? Mathf.Max(1f, currentWeaponSO.animFps) : 12f;
        float delay = 1f / fps;
        int index = 0;

        while (frames != null && frames.Length > 0)
        {
            if (weaponDisplay != null)
                weaponDisplay.UpdateWeaponSprite(frames[index]);

            yield return new WaitForSeconds(delay);
            index++;

            if (index < frames.Length)
                continue;

            if (loop)
                index = 0;
            else
                break;
        }

        currentAnimCoroutine = null;
        onComplete?.Invoke();
    }
}

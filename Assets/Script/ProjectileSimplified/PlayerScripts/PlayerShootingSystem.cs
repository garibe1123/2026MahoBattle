using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerShootingSystem : MonoBehaviour
{
    [Header("Settings")]
    public ProjectilePooler playerProjectilePool;
    public WeaponDisplay weaponDisplay;

    [Header("Weapon Inventory")]
    public List<PlayerShootingSO> unlockedWeapons;

    // ★ 추가됨: 각 무기별 남은 총알을 기억할 리스트!
    private List<int> ammoInventory = new List<int>();

    [HideInInspector] public PlayerShootingSO currentWeaponSO;
    public int currentWeaponIndex = 0;
    public int extraPierce = 0;

    // UI에서 읽어갈 수 있도록 public get으로 열어둡니다.
    public int currentAmmo { get; private set; }

    private bool isReloading;
    private float nextFireTime;

    private Coroutine currentAnimCoroutine;
    private Coroutine reloadCoroutine; // ★ 추가됨: 장전 취소를 위한 추적용

    void Start()
    {
        // ★ 시작할 때, 가지고 있는 무기 수만큼 탄창 주머니를 만들고 꽉 채워둡니다.
        for (int i = 0; i < unlockedWeapons.Count; i++)
        {
            ammoInventory.Add(unlockedWeapons[i].maxAmmo);
        }

        if (unlockedWeapons.Count > 0) EquipWeapon(0);
    }

    public void EquipWeapon(int index)
    {
        if (index < 0 || index >= unlockedWeapons.Count) return;

        // ★ 1. 기존에 들고 있던 무기가 있다면, 현재 총알을 탄창 주머니에 저장!
        if (currentWeaponSO != null)
        {
            ammoInventory[currentWeaponIndex] = currentAmmo;
        }

        // ★ 2. 장전 중에 무기를 스왑하면 장전 취소!
        if (isReloading && reloadCoroutine != null)
        {
            StopCoroutine(reloadCoroutine);
            isReloading = false;
        }

        // 3. 무기 교체
        currentWeaponIndex = index;
        currentWeaponSO = unlockedWeapons[index];

        // ★ 4. 새로 꺼낸 무기의 예전 총알 수 불러오기!
        currentAmmo = ammoInventory[index];

        PlayWeaponAnimation(currentWeaponSO.idleSprites, true);
    }

    public void TryShoot(Vector3 mousePos, Transform target = null)
    {
        if (isReloading || Time.time < nextFireTime || currentAmmo <= 0 || currentWeaponSO == null) return;
        Shoot(mousePos, target);
    }

    private void Shoot(Vector3 mousePos, Transform target)
    {
        currentAmmo--;

        // ★ 쏠 때마다 탄창 주머니도 실시간 업데이트 (UI 연동 등 대비)
        ammoInventory[currentWeaponIndex] = currentAmmo;

        nextFireTime = Time.time + currentWeaponSO.fireRate;

        Vector2 baseDir = (mousePos - transform.position).normalized;
        int shotCount = currentWeaponSO.projectilesPerShot;
        float spread = currentWeaponSO.spreadAngle;

        for (int i = 0; i < shotCount; i++)
        {
            float angleOffset = shotCount == 1 ? 0f : Mathf.Lerp(-spread, spread, (float)i / (shotCount - 1));
            Vector2 finalDir = Quaternion.Euler(0, 0, angleOffset) * baseDir;

            var p = playerProjectilePool.Get();
            p.transform.position = weaponDisplay.transform.position;
            p.Setup(currentWeaponSO.projectileData, finalDir, playerProjectilePool, target, mousePos);
            p.AddExtraPierce(Mathf.Max(0, extraPierce));
        }

        if (currentAmmo <= 0)
        {
            ReloadFuncCall(); // ★ 코루틴 직접 호출 대신 함수를 거치게 수정
        }
        else
        {
            PlayWeaponAnimation(currentWeaponSO.shootSprites, false, () =>
            {
                if (!isReloading) PlayWeaponAnimation(currentWeaponSO.idleSprites, true);
            });
        }
    }

    public void ReloadFuncCall()
    {
        if (isReloading || currentWeaponSO == null) return; // 방어 코드 추가

        if (currentAmmo < currentWeaponSO.maxAmmo)
        {
            // ★ 장전 코루틴을 추적 변수에 담아서 실행 (스왑 시 취소하기 위해)
            reloadCoroutine = StartCoroutine(ReloadRoutine());
        }
        else
        {
            Debug.Log("총알 가득 차 있음 리로드 거부.");
        }
    }

    IEnumerator ReloadRoutine()
    {
        Debug.Log("재장전 시작");
        isReloading = true;

        PlayWeaponAnimation(currentWeaponSO.reloadSprites, true);

        yield return new WaitForSeconds(currentWeaponSO.reloadTime);

        currentAmmo = currentWeaponSO.maxAmmo;
        ammoInventory[currentWeaponIndex] = currentAmmo; // ★ 주머니도 업데이트
        isReloading = false;

        PlayWeaponAnimation(currentWeaponSO.idleSprites, true);
    }

    // ==========================================
    // ★ Custom Weapon Animator 로직 (기존과 동일)
    // ==========================================
    private void PlayWeaponAnimation(Sprite[] frames, bool loop, System.Action onComplete = null)
    {
        if (currentAnimCoroutine != null) StopCoroutine(currentAnimCoroutine);

        if (frames == null || frames.Length == 0)
        {
            if (weaponDisplay != null && currentWeaponSO != null)
                weaponDisplay.UpdateWeaponSprite(currentWeaponSO.weaponSprite);
            return;
        }

        currentAnimCoroutine = StartCoroutine(AnimRoutine(frames, loop, onComplete));
    }

    private IEnumerator AnimRoutine(Sprite[] frames, bool loop, System.Action onComplete)
    {
        float delay = 1f / currentWeaponSO.animFps;
        int index = 0;

        while (true)
        {
            if (weaponDisplay != null)
                weaponDisplay.UpdateWeaponSprite(frames[index]);

            yield return new WaitForSeconds(delay);
            index++;

            if (index >= frames.Length)
            {
                if (loop) index = 0;
                else break;
            }
        }

        onComplete?.Invoke();
    }
}

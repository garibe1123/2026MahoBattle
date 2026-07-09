using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class PlayerInventoryUI : MonoBehaviour
{
    [Header("System References")]
    public PlayerShootingSystem shootingSystem;

    [Header("Radial Menu UI")]
    public GameObject radialMenuPanel;   // 무기 휠 전체를 담을 부모 패널 (정중앙 앵커)
    public GameObject weaponIconPrefab;  // 무기 아이콘 프리팹 (Image 컴포넌트 필수)
    public float radialRadius = 150f;    // 중심에서 아이콘들이 떨어져 있을 거리
    public float highlightScale = 1.5f;  // 선택된 아이콘이 커질 배율
    public float deadZoneRadius = 50f;   // [추가됨] 중앙에서 마우스를 이만큼 움직여야 선택 변경

    [Header("Tooltip & Ammo UI")]
    public GameObject itemTooltipPanel;
    public TextMeshProUGUI tooltipText;
    public TextMeshProUGUI ammoText;

    [Header("Bullet Time Settings")]
    public float bulletTimeScale = 0.1f;

    private bool isMenuOpen = false;
    private int selectedWeaponIndex = 0;

    // 원래의 fixedDeltaTime을 기억할 변수 [추가됨]
    private float initialFixedDeltaTime;

    // 생성된 무기 아이콘들을 담아둘 리스트
    private List<RectTransform> spawnedIcons = new List<RectTransform>();

    void Start()
    {
        // 프로젝트의 기본 fixedDeltaTime 저장
        initialFixedDeltaTime = Time.fixedDeltaTime;

        if (radialMenuPanel != null) radialMenuPanel.SetActive(false);
        if (itemTooltipPanel != null) itemTooltipPanel.SetActive(false);

        // 메뉴 구성
        RefreshRadialMenu();
    }

    void Update()
    {
        UpdateAmmoUI();

        if (Input.GetKeyDown(KeyCode.Tab)) OpenMenu();

        if (isMenuOpen && Input.GetKey(KeyCode.Tab)) HandleRadialSelection();

        if (Input.GetKeyUp(KeyCode.Tab)) CloseMenu();
    }

    // ==========================================
    // ⚙️ UI 자동 생성 로직 (무기 획득 시에도 호출 가능하도록 분리)
    // ==========================================
    public void RefreshRadialMenu()
    {
        if (shootingSystem == null || weaponIconPrefab == null || radialMenuPanel == null) return;

        // 기존에 생성된 아이콘들 지우기 (초기화)
        foreach (var icon in spawnedIcons)
        {
            if (icon != null) Destroy(icon.gameObject);
        }
        spawnedIcons.Clear();

        int weaponCount = shootingSystem.unlockedWeapons.Count;
        if (weaponCount == 0) return;

        float angleStep = 360f / weaponCount;

        for (int i = 0; i < weaponCount; i++)
        {
            GameObject iconObj = Instantiate(weaponIconPrefab, radialMenuPanel.transform);
            RectTransform rect = iconObj.GetComponent<RectTransform>();

            Image iconImage = iconObj.GetComponent<Image>();
            iconImage.sprite = shootingSystem.unlockedWeapons[i].weaponSprite;

            float angle = i * angleStep;
            float rad = angle * Mathf.Deg2Rad;

            float x = Mathf.Cos(rad) * radialRadius;
            float y = Mathf.Sin(rad) * radialRadius;

            rect.anchoredPosition = new Vector2(x, y);
            spawnedIcons.Add(rect);
        }
    }

    // ==========================================
    // ⏱️ 불릿 타임 및 패널 열기/닫기
    // ==========================================
    private void OpenMenu()
    {
        isMenuOpen = true;
        if (radialMenuPanel != null) radialMenuPanel.SetActive(true);

        // 열 때마다 최신 무기 리스트 반영을 원한다면 주석 해제하세요.
        // RefreshRadialMenu(); 

        Time.timeScale = bulletTimeScale;
        Time.fixedDeltaTime = initialFixedDeltaTime * Time.timeScale;
    }

    private void CloseMenu()
    {
        isMenuOpen = false;
        if (radialMenuPanel != null) radialMenuPanel.SetActive(false);

        Time.timeScale = 1f;
        Time.fixedDeltaTime = initialFixedDeltaTime; // 원래 값으로 안전하게 복구

        if (shootingSystem != null && spawnedIcons.Count > 0)
        {
            shootingSystem.EquipWeapon(selectedWeaponIndex);
        }
    }

    // ==========================================
    // 🎯 휠 메뉴 하이라이트 애니메이션
    // ==========================================
    private void HandleRadialSelection()
    {
        if (spawnedIcons.Count == 0) return;

        Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Vector2 mousePos = Input.mousePosition;
        Vector2 direction = mousePos - screenCenter;

        // [수정됨] 데드존 확인: 마우스가 중앙에서 일정 거리 이상 벗어났을 때만 선택 갱신
        if (direction.sqrMagnitude > deadZoneRadius * deadZoneRadius)
        {
            Vector2 normalizedDir = direction.normalized;
            float angle = Mathf.Atan2(normalizedDir.y, normalizedDir.x) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360f;

            int weaponCount = spawnedIcons.Count;
            float anglePerSlice = 360f / weaponCount;

            selectedWeaponIndex = Mathf.RoundToInt(angle / anglePerSlice) % weaponCount;
        }

        HighlightSelectedWeapon();
    }

    private void HighlightSelectedWeapon()
    {
        float lerpSpeed = 15f * Time.unscaledDeltaTime;

        for (int i = 0; i < spawnedIcons.Count; i++)
        {
            if (spawnedIcons[i] == null) continue;

            float targetScale = (i == selectedWeaponIndex) ? highlightScale : 1.0f;
            Vector3 currentScale = spawnedIcons[i].localScale;

            spawnedIcons[i].localScale = Vector3.Lerp(currentScale, new Vector3(targetScale, targetScale, 1f), lerpSpeed);

            Image img = spawnedIcons[i].GetComponent<Image>();
            Color c = img.color;
            c.a = Mathf.Lerp(c.a, (i == selectedWeaponIndex) ? 1f : 0.4f, lerpSpeed);
            img.color = c;
        }
    }

    private void UpdateAmmoUI()
    {
        // 1. 참조값(시스템, 무기 데이터, 텍스트UI)이 모두 정상인지 확인 (에러 방지)
        if (shootingSystem != null && shootingSystem.currentWeaponSO != null && ammoText != null)
        {
            // 2. 현재 총알 수와 최대 총알 수를 가져와서 텍스트로 찍어줍니다!
            ammoText.text = $"{shootingSystem.currentAmmo} / {shootingSystem.currentWeaponSO.maxAmmo}";

            // (보너스 디테일) 총알이 0발이면 텍스트를 빨간색으로, 아니면 하얀색으로 변경!
            if (shootingSystem.currentAmmo <= 0)
            {
                ammoText.color = Color.red;
            }
            else
            {
                ammoText.color = Color.white;
            }
        }
    }

}
using UnityEngine;

public class WeaponDisplay : MonoBehaviour
{
    public SpriteRenderer weaponSpriteRenderer;
    public Transform pivot; // 총이 회전할 중심축 (플레이어 어깨나 중심)

    public void UpdateWeaponSprite(Sprite newSprite)
    {
        weaponSpriteRenderer.sprite = newSprite;
    }

    void Update()
    {
        // 불릿 타임(Tab)으로 시간이 멈췄을 때는 총구도 회전하지 않음 (선택 사항)
        // if (Time.timeScale <= 0.1f) return; 

        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mousePos.z = 0f;

        Vector3 aimDirection = (mousePos - pivot.position).normalized;
        float angle = Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg;

        // 총기 회전
        pivot.eulerAngles = new Vector3(0, 0, angle);

        // ★ 각도가 왼쪽을 향할 때 (90도 넘어가면) 이미지가 뒤집히는 것을 방지
        if (angle > 90 || angle < -90)
        {
            weaponSpriteRenderer.flipY = true;
        }
        else
        {
            weaponSpriteRenderer.flipY = false;
        }
    }
}
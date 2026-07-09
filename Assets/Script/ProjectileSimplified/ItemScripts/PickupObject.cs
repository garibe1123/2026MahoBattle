using UnityEngine;

public enum PickupType { Weapon, Consumable, Active, Passive }

public class PickupObject : MonoBehaviour
{
    [Header("Pickup Settings")]
    public PickupType pickupType;

    public PlayerShootingSO weaponData;
    public ItemBaseSO itemData; // Consumable, Passive, Active 모두 이거 하나로 받습니다! (다형성)

    private SpriteRenderer sr;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        UpdateVisuals(); // 시작할 때 SO 데이터를 읽고 외형을 바꿈!
    }

    private void UpdateVisuals()
    {
        if (sr == null) return;

        if (pickupType == PickupType.Weapon && weaponData != null)
        {
            // 총기일 경우
            if (weaponData.weaponSprite != null) sr.sprite = weaponData.weaponSprite;
        }
        else if (itemData != null)
        {
            // 일반 아이템일 경우
            if (itemData.itemSprite != null) sr.sprite = itemData.itemSprite;

            // ★ 유저님 아이디어 적용: Material이 비어있지 않다면, 그 Material(셰이더)로 교체!
            // 비어있다면 프리팹에 기본으로 달려있는 Material(보통 Sprite-Default)을 그대로 유지합니다.
            if (itemData.itemMaterial != null)
            {
                sr.material = itemData.itemMaterial;
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerController player = other.GetComponent<PlayerController>();
            if (player != null)
            {
                ProcessPickup(player);
                Destroy(gameObject); // 아이템 먹고 삭제!
            }
        }
    }

    private void ProcessPickup(PlayerController player)
    {
        switch (pickupType)
        {
            case PickupType.Weapon:
                if (weaponData != null && !player.shootingSystem.unlockedWeapons.Contains(weaponData))
                {
                    player.shootingSystem.unlockedWeapons.Add(weaponData);
                    Debug.Log($"[{weaponData.weaponName}] 획득!");
                    // TODO: UI 갱신 로직 호출 (inventoryUI.RefreshRadialMenu())
                }
                break;

            case PickupType.Consumable:
            case PickupType.Active:
                if (itemData != null && itemData is ActiveItemSO activeItem)
                {
                    player.GetComponent<PlayerItemSystem>().EquipActiveItem(activeItem);
                }
                break;
            case PickupType.Passive:
                // ★ SO 내부의 ApplyEffect 함수 호출! (소모품인지 패시브인지 알아서 작동함)
                if (itemData != null)
                {
                    itemData.ApplyEffect(player);

                    // 패시브 아이템이면 인벤토리(우측 하단 UI)에 추가하는 로직도 나중에 여기에 쏙!
                }
                break;
        }
    }
}
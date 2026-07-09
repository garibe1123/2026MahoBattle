using UnityEngine;

[CreateAssetMenu(menuName = "Game/Item/Consumable Item")]
public class ConsumableItemSO : ItemBaseSO
{
    [Header("Consumable Effects")]
    public float healAmount = 0f;
    public float staminaRestore = 0f;
    public float invincibleDuration = 0f; // 일시적 무적
    public int shieldAmount = 0; // 1회 방어막 등

    // 부모의 빈 껍데기 함수를 가져와서 진짜 효과를 구현(override)합니다!
    public override void ApplyEffect(PlayerController player)
    {
        if (healAmount > 0) player.Heal(healAmount);
        if (staminaRestore > 0) player.RestoreStamina(staminaRestore);
        if (invincibleDuration > 0) player.StartInvincible(invincibleDuration);
        if (shieldAmount > 0) player.AddShield(shieldAmount);

        Debug.Log($"[소모품 사용] {itemName} 효과 발동!");
    }
}

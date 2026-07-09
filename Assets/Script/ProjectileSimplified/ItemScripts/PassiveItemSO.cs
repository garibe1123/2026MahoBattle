using UnityEngine;

[CreateAssetMenu(menuName = "Game/Item/Passive Item")]
public class PassiveItemSO : ItemBaseSO
{
    [Header("Passive Effects")]
    public GameObject passivePrefab; // 소환할 펫이나 장판(Aura) 프리팹
    public float extraMoveSpeed = 0f; // 영구 이속 증가량
    public int extraPierce = 0; // 추가 관통력

    public override void ApplyEffect(PlayerController player)
    {
        // 1. 영구 스탯 적용
        player.moveSpeed += extraMoveSpeed;
        player.shootingSystem.extraPierce += extraPierce;

        // 2. 장판이나 펫 소환 (플레이어를 부모로 설정해서 따라다니게 함)
        if (passivePrefab != null)
        {
            GameObject auraOrPet = Instantiate(passivePrefab, player.transform.position, Quaternion.identity);
            auraOrPet.transform.SetParent(player.transform); // 플레이어 따라다니기!
        }

        Debug.Log($"[패시브 획득] {itemName} 장착 완료!");
    }
}

using UnityEngine;

[CreateAssetMenu(menuName = "Game/Item/Active Item")]
public class ActiveItemSO : ItemBaseSO
{
    [Header("Active Settings")]
    public float cooldownTime = 15f; // 아이템별 고유 쿨타임

    public override void ApplyEffect(PlayerController player)
    {
        // 여기는 빈 껍데기로 두고,
        // 실제 아이템들(공포탄, 무적 보호막 등)이 이 클래스를 다시 상속받아서 
        // 각자의 특수 효과를 구현하게 만들 겁니다!
    }
}

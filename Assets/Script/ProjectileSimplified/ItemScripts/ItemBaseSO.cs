using UnityEngine;

public abstract class ItemBaseSO : ScriptableObject
{
    [Header("Item Info")]
    public string itemName;
    [TextArea(3, 5)]
    public string itemDescription;

    [Header("Visual Settings")]
    public Sprite itemSprite;       // 필드에 떨어져 있을 때 & UI에 띄울 이미지
    public Material itemMaterial;   // (선택 사항) 외곽선 셰이더, 반짝임 셰이더 등 특수 머티리얼

    // 아이템 효과 발동 함수
    public abstract void ApplyEffect(PlayerController player);
}

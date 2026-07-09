using UnityEngine;

[CreateAssetMenu(menuName = "Game/Player/Player Item SO")]
public class PlayerItemSO : ScriptableObject
{
    public string itemName;
    public float hpBonus;
    public float speedBonus;
    public float damageMultiplier = 1f;
}

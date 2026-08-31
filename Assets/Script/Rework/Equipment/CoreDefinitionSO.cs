using UnityEngine;

[CreateAssetMenu(menuName = "Game/Rework/Core Definition", fileName = "CoreDefinition")]
public class CoreDefinitionSO : ScriptableObject
{
    public string coreName;
    public Sprite icon;
    [TextArea] public string synergyDescription;

    public float damageMultiplier = 1f;
    public float moveSpeedMultiplier = 1f;
    public float maxHpBonus = 0f;
}

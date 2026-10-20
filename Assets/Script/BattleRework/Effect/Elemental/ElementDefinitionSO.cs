using UnityEngine;

[CreateAssetMenu(menuName = "Game/Effect/Element Identifier")]
public sealed class ElementDefinitionSO : ScriptableObject
{
    public string displayName;
    public Color previewColor = Color.white;
}

using UnityEngine;

[CreateAssetMenu(menuName = "Game/Rework/Shooting Theme", fileName = "ShootingTheme")]
public class ShootingThemeSO : ScriptableObject
{
    public string themeName;
    public Color hudTint = Color.white;

    [Header("Presentation")]
    public Material postProcessMaterial;
    [TextArea] public string audienceToneMemo;
}

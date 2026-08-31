using UnityEngine;

/// <summary>
/// 촬영장 테마는 최신 기획 기준으로 HUD/화면 필터/팬 반응 톤 등 연출 축만 담당합니다.
/// 맵 아트와 몬스터 로스터는 ClanDefinitionSO의 책임입니다.
/// </summary>
[CreateAssetMenu(fileName = "ShootingTheme", menuName = "MahoBattle/Shooting Theme")]
public class ShootingThemeSO : ScriptableObject
{
    public string themeId;
    public string displayName;

    [Header("Presentation")]
    public Color hudAccentColor = Color.white;
    public Material postProcessMaterial;
    public AudioClip audienceReactionBank;

    [TextArea]
    public string fanReactionTone;
}

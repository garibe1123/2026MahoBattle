using UnityEngine;

[CreateAssetMenu(menuName = "Game/Player/Player Shooting SO")]
public class PlayerShootingSO : ScriptableObject
{
    [Header("Weapon UI & Basic Info")]
    public string weaponName = "기본 권총";
    public Sprite weaponSprite; // 인벤토리 UI용 대표 이미지
    [TextArea] public string description;

    [Header("Weapon Animations (Sprites)")]
    public Sprite[] idleSprites;
    public Sprite[] shootSprites;
    public Sprite[] reloadSprites;
    public float animFps = 12f; // 초당 프레임 수 (애니메이션 속도)

    [Header("Combat Stats")]
    public ProjectileSO projectileData;
    public int maxAmmo = 6;
    public float reloadTime = 1.2f;
    public float fireRate = 0.25f;
    public float knockbackForce = 2f;

    [Header("Shotgun / Spread Settings")]
    public int projectilesPerShot = 1;
    public float spreadAngle = 15f;
}
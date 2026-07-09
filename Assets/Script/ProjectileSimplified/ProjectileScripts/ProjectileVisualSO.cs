using UnityEngine;

[CreateAssetMenu(menuName = "Game/Projectile/Visual SO")]
public class ProjectileVisualSO : ScriptableObject
{
    public Sprite[] startSprites;
    public Sprite[] idleSprites;
    public Sprite[] hitSprites;
    public float fps = 12f;
}

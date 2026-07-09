using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enemy/Visual SO")]
public class EnemyVisualSO : ScriptableObject
{
    public Sprite[] idleSprites;
    public Sprite[] moveSprites;
    public Sprite[] attackSprites;
    public Sprite[] dieSprites;

    public float fps = 12f;

    [Header("Material & Flash")]
    public Material customMaterial;
    [ColorUsage(true, true)]
    public Color hitFlashColor = Color.white; // 피격 시 번쩍일 색상
}

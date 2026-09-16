using UnityEngine;

[CreateAssetMenu(menuName = "Game/Player/Player Sprite SO")]
public class PlayerSpriteSO : ScriptableObject
{
    public Sprite[] idleSprites;
    public Sprite[] moveSprites;
    public Sprite[] rollSprites;
    public float fps = 10f;
}

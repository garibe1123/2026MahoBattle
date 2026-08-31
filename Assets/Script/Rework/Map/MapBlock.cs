using UnityEngine;

public class MapBlock : MonoBehaviour
{
    public const float UnitPixels = 64f;
    public const float BlockPixels = 128f;

    [Header("Assembly")]
    public BlockEntryType entryType = BlockEntryType.Wheel;
    public float entryDuration = 0.65f;
    public Vector2 entryOffset = new Vector2(8f, 0f);

    [Header("Navigation")]
    public bool contributesToNavigation = true;

    public Vector3 GetWorldPosition(Vector2Int gridPosition, float worldUnitsPerBlock)
    {
        return new Vector3(gridPosition.x * worldUnitsPerBlock, gridPosition.y * worldUnitsPerBlock, 0f);
    }
}

public enum BlockEntryType
{
    Wheel,
    CeilingRail,
    RiseFromFloor,
    Drop,
    Slide,
    Static
}

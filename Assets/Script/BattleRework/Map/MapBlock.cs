using DG.Tweening;
using UnityEngine;

public enum MapBlockEntryType
{
    WheelSlide,
    CeilingDrop,
    RiseFromFloor,
    Static
}

/// <summary>
/// 2x2 유닛(기본 128x128px) 맵 블록 하나를 표현합니다.
/// 방(Room)은 이 블록 여러 개를 격자로 조립해서 구성합니다.
/// </summary>
public class MapBlock : MonoBehaviour
{
    public const float UnitWorldSize = 1f;
    public static readonly Vector2 BlockWorldSize = new(2f, 2f);

    [Header("Entry")]
    [SerializeField] private MapBlockEntryType entryType = MapBlockEntryType.WheelSlide;
    [SerializeField] private float entryDuration = 0.7f;
    [SerializeField] private float entryOffset = 8f;
    [SerializeField] private Ease entryEase = Ease.InQuad;

    [Header("Exit")]
    [SerializeField] private float exitDuration = 0.6f;
    [SerializeField] private Ease exitEase = Ease.InQuad;

    public float EntryDuration => entryDuration;
    public float ExitDuration => exitDuration;

    public void SnapTo(Vector3 worldPosition)
    {
        transform.DOKill();
        transform.position = worldPosition;
    }

    public Tween PlayEnter(Vector3 destination, Vector2 preferredDirection)
    {
        transform.DOKill();

        if (entryType == MapBlockEntryType.Static)
        {
            transform.position = destination;
            return transform.DOMove(destination, 0f);
        }

        Vector3 start = destination;
        Vector2 dir = preferredDirection.sqrMagnitude > 0.001f
            ? preferredDirection.normalized
            : Vector2.right;

        switch (entryType)
        {
            case MapBlockEntryType.CeilingDrop:
                start += Vector3.up * entryOffset;
                break;
            case MapBlockEntryType.RiseFromFloor:
                start += Vector3.down * entryOffset;
                break;
            default:
                start += (Vector3)(dir * entryOffset);
                break;
        }

        transform.position = start;
        return transform.DOMove(destination, entryDuration).SetEase(entryEase);
    }

    public Tween PlayExit(Vector2 direction)
    {
        transform.DOKill();
        Vector2 dir = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
        Vector3 destination = transform.position + (Vector3)(dir * entryOffset);
        return transform.DOMove(destination, exitDuration).SetEase(exitEase);
    }
}

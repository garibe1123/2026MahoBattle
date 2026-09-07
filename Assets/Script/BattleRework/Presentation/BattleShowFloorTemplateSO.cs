using UnityEngine;

public enum BattleShowHandlePlacementMode
{
    ContactSideOnly,
    AllFourSides,
    None
}

/// <summary>
/// 굴러오는 쇼 바닥 한 세트의 32px 아트 템플릿입니다.
/// Project 창에서 Create > Battle > Show > Floor Template 로 만든 뒤
/// BattleShowPresentationManager의 Default Floor Template에 할당해서 사용합니다.
/// </summary>
[CreateAssetMenu(
    fileName = "BattleShowFloorTemplate_Default",
    menuName = "Battle/Show/Floor Template",
    order = 10)]
public sealed class BattleShowFloorTemplateSO : ScriptableObject
{
    [Header("기본 바닥 - 32px")]
    [Tooltip("굴러오는 판 내부를 채우는 32x32 바닥 이미지 목록입니다. 각 바닥 칸마다 이 목록 중 하나가 랜덤으로 선택됩니다. 권장 Import 설정은 PPU 32입니다.")]
    [SerializeField] private Sprite[] floorVariants;

    [Header("판 외곽 - 32px")]
    [Tooltip("필수 위 판 이미지입니다. 바닥 본체의 최상단보다 정확히 1타일 위에 가로로 반복 배치됩니다. 비어 있으면 테스트용 32px 기본 사각형을 대신 사용합니다.")]
    [SerializeField] private Sprite upperPlateSprite32;

    [Tooltip("아래 판 이미지입니다. 바닥 본체의 최하단보다 정확히 1타일 아래에 가로로 반복 배치됩니다. 필요하지 않으면 비워둘 수 있습니다.")]
    [SerializeField] private Sprite lowerPlateSprite32;

    [Header("연결 핸들 - 32px")]
    [Tooltip("판의 왼쪽 면이 기존 바닥과 연결될 때 사용하는 좌측 핸들 이미지입니다. 현재 Reward 판은 오른쪽에서 들어오므로 기본 접촉면은 왼쪽입니다.")]
    [SerializeField] private Sprite leftHandleSprite32;

    [Tooltip("판의 오른쪽 면이 기존 바닥과 연결될 때 사용하는 우측 핸들 이미지입니다.")]
    [SerializeField] private Sprite rightHandleSprite32;

    [Tooltip("판의 위쪽 면이 기존 바닥과 연결될 때 사용하는 위 핸들 이미지입니다.")]
    [SerializeField] private Sprite upperHandleSprite32;

    [Tooltip("판의 아래쪽 면이 기존 바닥과 연결될 때 사용하는 아래 핸들 이미지입니다.")]
    [SerializeField] private Sprite lowerHandleSprite32;

    [Tooltip("핸들을 어느 면에 표시할지 정합니다. ContactSideOnly는 실제 연결면만, AllFourSides는 네 방향 모두, None은 핸들을 표시하지 않습니다.")]
    [SerializeField] private BattleShowHandlePlacementMode handlePlacement = BattleShowHandlePlacementMode.ContactSideOnly;

    [Header("색상")]
    [Tooltip("랜덤 바닥 SpriteRenderer에 곱해지는 색입니다. 원본 색을 그대로 쓰려면 흰색으로 둡니다.")]
    [SerializeField] private Color floorTint = Color.white;

    [Tooltip("위 판/아래 판에 곱해지는 색입니다. 원본 색을 그대로 쓰려면 흰색으로 둡니다.")]
    [SerializeField] private Color plateTint = Color.white;

    [Tooltip("좌/우/위/아래 핸들에 곱해지는 색입니다. 원본 색을 그대로 쓰려면 흰색으로 둡니다.")]
    [SerializeField] private Color handleTint = Color.white;

    [Header("Sorting Order")]
    [Tooltip("바닥 본체의 SpriteRenderer Sorting Order입니다.")]
    [SerializeField] private int floorSortingOrder = -18;

    [Tooltip("아래 판의 Sorting Order입니다.")]
    [SerializeField] private int lowerPlateSortingOrder = -17;

    [Tooltip("위 판의 Sorting Order입니다. 코드에서 바닥보다 최소 2단계 위가 되도록 강제 보정됩니다.")]
    [SerializeField] private int upperPlateSortingOrder = -15;

    [Tooltip("핸들의 Sorting Order입니다. 코드에서 위 판보다 최소 1단계 위가 되도록 강제 보정됩니다.")]
    [SerializeField] private int handleSortingOrder = -14;

    [Header("핸들 위치")]
    [Tooltip("좌/우 핸들이 바닥 외곽에서 얼마나 떨어져 배치될지 정합니다. 단위는 Tile이며 1이면 정확히 32px 한 칸 바깥입니다.")]
    [SerializeField, Range(0.5f, 1.5f)] private float sideHandleDistance = 1f;

    [Tooltip("위/아래 핸들이 위 판/아래 판에서 얼마나 더 떨어져 배치될지 정합니다. 단위는 Tile입니다.")]
    [SerializeField, Range(0.5f, 1.5f)] private float verticalHandleDistance = 1f;

    [Header("찰칵 연결 반동")]
    [Tooltip("판이 도착해 연결되는 순간 접촉 핸들이 잠깐 튀는 크기입니다. 0이면 핸들 반동을 사용하지 않습니다.")]
    [SerializeField, Range(0f, 0.35f)] private float dockHandlePunch = 0.14f;

    [Tooltip("접촉 핸들의 찰칵 반동이 재생되는 시간입니다.")]
    [SerializeField, Min(0.03f)] private float dockHandlePunchDuration = 0.14f;

    [Tooltip("찰칵 반동 중 작은 진동 횟수입니다. 값이 높을수록 더 빠르게 떨리는 느낌이 납니다.")]
    [SerializeField, Range(1, 12)] private int dockHandlePunchVibrato = 4;

    public Sprite[] FloorVariants => floorVariants;
    public Sprite UpperPlateSprite32 => upperPlateSprite32;
    public Sprite LowerPlateSprite32 => lowerPlateSprite32;
    public Sprite LeftHandleSprite32 => leftHandleSprite32;
    public Sprite RightHandleSprite32 => rightHandleSprite32;
    public Sprite UpperHandleSprite32 => upperHandleSprite32;
    public Sprite LowerHandleSprite32 => lowerHandleSprite32;
    public BattleShowHandlePlacementMode HandlePlacement => handlePlacement;
    public Color FloorTint => floorTint;
    public Color PlateTint => plateTint;
    public Color HandleTint => handleTint;
    public int FloorSortingOrder => floorSortingOrder;
    public int LowerPlateSortingOrder => lowerPlateSortingOrder;
    public int UpperPlateSortingOrder => upperPlateSortingOrder;
    public int HandleSortingOrder => handleSortingOrder;
    public float SideHandleDistance => sideHandleDistance;
    public float VerticalHandleDistance => verticalHandleDistance;
    public float DockHandlePunch => dockHandlePunch;
    public float DockHandlePunchDuration => dockHandlePunchDuration;
    public int DockHandlePunchVibrato => dockHandlePunchVibrato;

#if UNITY_EDITOR
    private void OnValidate()
    {
        Validate32PxSprite(upperPlateSprite32, "위 판");
        Validate32PxSprite(lowerPlateSprite32, "아래 판");
        Validate32PxSprite(leftHandleSprite32, "좌측 핸들");
        Validate32PxSprite(rightHandleSprite32, "우측 핸들");
        Validate32PxSprite(upperHandleSprite32, "위 핸들");
        Validate32PxSprite(lowerHandleSprite32, "아래 핸들");

        if (floorVariants == null)
            return;

        for (int i = 0; i < floorVariants.Length; i++)
            Validate32PxSprite(floorVariants[i], $"바닥 Variant {i}");
    }

    private void Validate32PxSprite(Sprite sprite, string label)
    {
        if (sprite == null)
            return;

        if (Mathf.RoundToInt(sprite.rect.width) == 32 && Mathf.RoundToInt(sprite.rect.height) == 32)
            return;

        Debug.LogWarning(
            $"[BattleShowFloorTemplateSO] {label}은 32x32px Sprite를 권장합니다: {sprite.name}",
            this);
    }
#endif
}

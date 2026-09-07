using UnityEngine;
using UnityEngine.Serialization;

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

    [Header("위 판 - 32px")]
    [Tooltip("상단 외곽 핸들의 대체 이미지입니다. Upper Handle이 비어 있을 때만 최상단의 왼쪽/오른쪽 끝에 최대 2개 배치되며, 전체 폭에 반복하지 않습니다.")]
    [SerializeField] private Sprite upperPlateSprite32;

    [Header("하판 - 32px / 좌·중앙·우")]
    [Tooltip("하판의 좌측 끝 전용 이미지입니다. 하판 길이가 2칸 이상이면 가장 왼쪽 칸에 한 번만 배치됩니다.")]
    [SerializeField] private Sprite lowerPlateLeftSprite32;

    [Tooltip("하판의 중앙 반복 이미지입니다. 하판이 좌우 끝보다 길 때 그 사이 칸을 이 이미지로 반복해서 채웁니다. 하판 폭이 1칸뿐이면 이 이미지를 우선 사용합니다.")]
    [FormerlySerializedAs("lowerPlateSprite32")]
    [SerializeField] private Sprite lowerPlateCenterSprite32;

    [Tooltip("하판의 우측 끝 전용 이미지입니다. 하판 길이가 2칸 이상이면 가장 오른쪽 칸에 한 번만 배치됩니다.")]
    [SerializeField] private Sprite lowerPlateRightSprite32;

    [Header("연결 핸들 - 32px")]
    [Tooltip("판의 왼쪽 면이 기존 바닥과 연결될 때 사용하는 좌측 핸들 이미지입니다. 핸들은 바닥 본체의 왼쪽 가장자리에 바로 붙여 배치합니다.")]
    [SerializeField] private Sprite leftHandleSprite32;

    [Tooltip("판의 오른쪽 면이 기존 바닥과 연결될 때 사용하는 우측 핸들 이미지입니다. 핸들은 바닥 본체의 오른쪽 가장자리에 바로 붙여 배치합니다.")]
    [SerializeField] private Sprite rightHandleSprite32;

    [Tooltip("판의 위쪽 면이 기존 바닥과 연결될 때 사용하는 위 핸들 이미지입니다. 핸들은 바닥 본체의 위쪽 가장자리에 바로 붙여 배치합니다.")]
    [SerializeField] private Sprite upperHandleSprite32;

    [Tooltip("판의 아래쪽 면이 기존 바닥과 연결될 때 사용하는 아래 핸들 이미지입니다. 핸들은 바닥 본체의 아래쪽 가장자리에 바로 붙여 배치합니다.")]
    [SerializeField] private Sprite lowerHandleSprite32;

    [Tooltip("핸들 표시를 켜고 끄는 설정입니다. None이 아니면 완성된 전체 바닥에서 실제로 노출된 외곽면에만 손잡이가 생성됩니다. 위/아래 면은 왼쪽·오른쪽 끝, 좌/우 면은 아래·위 끝에 각 면 최대 2개만 배치되며 한 칸 면은 1개로 합쳐집니다.")]
    [SerializeField] private BattleShowHandlePlacementMode handlePlacement = BattleShowHandlePlacementMode.ContactSideOnly;

    [Header("색상")]
    [Tooltip("랜덤 바닥 SpriteRenderer에 곱해지는 색입니다. 원본 색을 그대로 쓰려면 흰색으로 둡니다.")]
    [SerializeField] private Color floorTint = Color.white;

    [Tooltip("위 판과 하판에 곱해지는 색입니다. 원본 색을 그대로 쓰려면 흰색으로 둡니다.")]
    [SerializeField] private Color plateTint = Color.white;

    [Tooltip("좌/우/위/아래 핸들에 곱해지는 색입니다. 원본 색을 그대로 쓰려면 흰색으로 둡니다.")]
    [SerializeField] private Color handleTint = Color.white;

    [Header("Sorting Order")]
    [Tooltip("바닥 본체의 SpriteRenderer Sorting Order 기준값입니다. 코드에서 핸들·상판·하판보다 항상 앞에 보이도록 자동 보정됩니다.")]
    [SerializeField] private int floorSortingOrder = -18;

    [Tooltip("하판의 Sorting Order입니다. 코드에서 바닥 본체와 핸들보다 뒤에 보이도록 자동 보정됩니다.")]
    [SerializeField] private int lowerPlateSortingOrder = -19;

    [Tooltip("위 판의 Sorting Order 기준값입니다. 바닥 본체보다 뒤에 보이도록 자동 보정됩니다.")]
    [SerializeField] private int upperPlateSortingOrder = -15;

    [Tooltip("핸들의 Sorting Order 기준값입니다. 하판보다 앞, 바닥 본체보다 뒤에 보이도록 자동 보정됩니다.")]
    [SerializeField] private int handleSortingOrder = -14;

    [Header("찰칵 연결 반동")]
    [Tooltip("판이 도착해 연결되는 순간 실제 접촉 핸들이 잠깐 튀는 크기입니다. 0이면 핸들 반동을 사용하지 않습니다.")]
    [SerializeField, Range(0f, 0.35f)] private float dockHandlePunch = 0.14f;

    [Tooltip("접촉 핸들의 찰칵 반동이 재생되는 시간입니다.")]
    [SerializeField, Min(0.03f)] private float dockHandlePunchDuration = 0.14f;

    [Tooltip("찰칵 반동 중 작은 진동 횟수입니다. 값이 높을수록 더 빠르게 떨리는 느낌이 납니다.")]
    [SerializeField, Range(1, 12)] private int dockHandlePunchVibrato = 4;

    public Sprite[] FloorVariants => floorVariants;
    public Sprite UpperPlateSprite32 => upperPlateSprite32;
    public Sprite LowerPlateLeftSprite32 => lowerPlateLeftSprite32;
    public Sprite LowerPlateCenterSprite32 => lowerPlateCenterSprite32;
    public Sprite LowerPlateRightSprite32 => lowerPlateRightSprite32;
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
    public float DockHandlePunch => dockHandlePunch;
    public float DockHandlePunchDuration => dockHandlePunchDuration;
    public int DockHandlePunchVibrato => dockHandlePunchVibrato;

#if UNITY_EDITOR
    private void OnValidate()
    {
        Validate32PxSprite(upperPlateSprite32, "위 판");
        Validate32PxSprite(lowerPlateLeftSprite32, "하판 좌측 끝");
        Validate32PxSprite(lowerPlateCenterSprite32, "하판 중앙");
        Validate32PxSprite(lowerPlateRightSprite32, "하판 우측 끝");
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

using System;
using System.Collections.Generic;
using UnityEngine;

public enum BattleUniversalStageDecorCategory
{
    Cable,
    Camera,
    LightStand,
    Light,
    Monitor,
    Equipment,
    Case,
    Fence,
    Misc,
    Chair
}

/// <summary>
/// 모든 Field Scene에서 공용으로 사용하는 촬영장 치장물 풀입니다.
/// Chair는 Reward / Map Show 전용으로 남겨 두기 위해 Universal Field Dressing에서는 자동 제외됩니다.
/// </summary>
[CreateAssetMenu(menuName = "MahoBattle/Presentation/Universal Stage Decor Profile", fileName = "BattleUniversalStageDecorProfile")]
public sealed class BattleUniversalStageDecorProfileSO : ScriptableObject
{
    [SerializeField, Min(0.25f)] private float cellWorldSize = 1f;
    [SerializeField] private List<BattleUniversalStageDecorEntry> entries = new();

    public float CellWorldSize => Mathf.Max(0.25f, cellWorldSize);
    public IReadOnlyList<BattleUniversalStageDecorEntry> Entries => entries;
}

[Serializable]
public sealed class BattleUniversalStageDecorEntry
{
    [SerializeField] private string label = "Stage Decor";
    [SerializeField] private BattleUniversalStageDecorCategory category = BattleUniversalStageDecorCategory.Misc;

    [Header("Visual")]
    [Tooltip("간단한 단일 Sprite 장식이면 이 값을 사용합니다.")]
    [SerializeField] private Sprite sprite;
    [Tooltip("여러 Sprite / 애니메이션으로 구성된 장식이면 Prefab을 사용합니다. Presentation 전용 Prefab을 권장합니다.")]
    [SerializeField] private GameObject prefab;

    [Header("Carrier Footprint")]
    [Tooltip("Universal Field Dressing은 1x1 / 2x3 / 3x2 / 3x3만 사용합니다.")]
    [SerializeField] private Vector2Int footprint = Vector2Int.one;
    [SerializeField, Min(1)] private int weight = 1;

    [Header("Placement")]
    [SerializeField] private Vector2 localOffset;
    [SerializeField] private Vector2 localScale = Vector2.one;
    [SerializeField] private int sortingOrderOffset = 12;
    [SerializeField] private bool randomFlipX;
    [SerializeField] private bool allowHalfTurn = true;

    public string Label => string.IsNullOrWhiteSpace(label) ? category.ToString() : label;
    public BattleUniversalStageDecorCategory Category => category;
    public Sprite Sprite => sprite;
    public GameObject Prefab => prefab;
    public Vector2Int Footprint => ResolveFootprint(footprint);
    public int Weight => Mathf.Max(1, weight);
    public Vector2 LocalOffset => localOffset;
    public Vector2 LocalScale => new(
        Mathf.Approximately(localScale.x, 0f) ? 1f : localScale.x,
        Mathf.Approximately(localScale.y, 0f) ? 1f : localScale.y);
    public int SortingOrderOffset => sortingOrderOffset;
    public bool RandomFlipX => randomFlipX;
    public bool AllowHalfTurn => allowHalfTurn;
    public bool HasVisual => sprite != null || prefab != null;

    public static Vector2Int ResolveFootprint(Vector2Int value)
    {
        int x = Mathf.Max(1, value.x);
        int y = Mathf.Max(1, value.y);

        if (x >= 3 && y >= 3)
            return new Vector2Int(3, 3);

        if ((x >= 3 && y >= 2) || (x >= 2 && y >= 3))
            return x >= y ? new Vector2Int(3, 2) : new Vector2Int(2, 3);

        return Vector2Int.one;
    }
}

/// <summary>
/// 씬별로 같은 공용 Manager에 Profile만 공급하고 싶을 때 사용하는 선택 컴포넌트입니다.
/// 이 컴포넌트가 없어도 Universal Stage Dressing은 Floor를 발견하면 기본 모드로 동작합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleUniversalStageDecorSceneConfig : MonoBehaviour
{
    [SerializeField] private BattleUniversalStageDecorProfileSO profile;
    [SerializeField] private bool disableUniversalStageDecor;

    public BattleUniversalStageDecorProfileSO Profile => profile;
    public bool DisableUniversalStageDecor => disableUniversalStageDecor;
}

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
///
/// Scene의 SpriteManager에 저장된 BattleUniversalStageDecorSceneConfig가 직접 Sprite/Prefab 소스를 가지고 있는 경우,
/// 런타임에 이 SO의 transient instance를 만들어 기존 Universal Dressing 파이프라인에 그대로 공급할 수 있습니다.
/// </summary>
[CreateAssetMenu(menuName = "MahoBattle/Presentation/Universal Stage Decor Profile", fileName = "BattleUniversalStageDecorProfile")]
public sealed class BattleUniversalStageDecorProfileSO : ScriptableObject
{
    [SerializeField, Min(0.25f)] private float cellWorldSize = 1f;
    [SerializeField] private List<BattleUniversalStageDecorEntry> entries = new();

    public float CellWorldSize => Mathf.Max(0.25f, cellWorldSize);
    public IReadOnlyList<BattleUniversalStageDecorEntry> Entries => entries;

    public void ConfigureRuntime(float cellSize, IList<BattleUniversalStageDecorEntry> runtimeEntries)
    {
        cellWorldSize = Mathf.Max(0.25f, cellSize);
        entries ??= new List<BattleUniversalStageDecorEntry>();
        entries.Clear();

        if (runtimeEntries == null)
            return;

        for (int i = 0; i < runtimeEntries.Count; i++)
        {
            BattleUniversalStageDecorEntry entry = runtimeEntries[i];
            if (entry != null)
                entries.Add(entry);
        }
    }
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
    [Tooltip("Light는 Base가 필수이므로 단일 Sprite 대신 이 Rig SO 사용을 권장합니다.")]
    [SerializeField] private BattleUniversalStageLightRigSO lightRig;

    [Header("Carrier Footprint")]
    [Tooltip("Universal Field Dressing은 1x1 / 2x2 / 2x3 / 3x2 / 3x3을 지원합니다.")]
    [SerializeField] private Vector2Int footprint = Vector2Int.one;
    [SerializeField, Min(1)] private int weight = 1;

    [Header("Placement")]
    [SerializeField] private Vector2 localOffset;
    [SerializeField] private Vector2 localScale = Vector2.one;
    [SerializeField] private int sortingOrderOffset = 12;
    [SerializeField] private bool randomFlipX;
    [Tooltip("Legacy 옵션입니다. Universal Decor에서는 더 이상 180도 회전을 사용하지 않습니다.")]
    [SerializeField] private bool allowHalfTurn;

    public string Label => string.IsNullOrWhiteSpace(label) ? category.ToString() : label;
    public BattleUniversalStageDecorCategory Category => category;
    public Sprite Sprite => sprite;
    public GameObject Prefab => prefab;
    public BattleUniversalStageLightRigSO LightRig => lightRig;
    public Vector2Int Footprint => ResolveFootprint(footprint);
    public int Weight => Mathf.Max(1, weight);
    public Vector2 LocalOffset => localOffset;
    public Vector2 LocalScale => new(
        Mathf.Approximately(localScale.x, 0f) ? 1f : localScale.x,
        Mathf.Approximately(localScale.y, 0f) ? 1f : localScale.y);
    public int SortingOrderOffset => sortingOrderOffset;
    public bool RandomFlipX => randomFlipX;
    public bool AllowHalfTurn => false;
    public bool HasVisual => sprite != null || prefab != null || (lightRig != null && lightRig.IsValid);

    public static BattleUniversalStageDecorEntry CreateRuntime(
        string sourceLabel,
        BattleUniversalStageDecorCategory sourceCategory,
        Sprite sourceSprite,
        GameObject sourcePrefab,
        Vector2Int sourceFootprint,
        int sourceWeight = 1,
        Vector2 sourceLocalOffset = default,
        Vector2 sourceLocalScale = default,
        int sourceSortingOrderOffset = 12,
        bool sourceRandomFlipX = false,
        bool sourceAllowHalfTurn = false,
        BattleUniversalStageLightRigSO sourceLightRig = null)
    {
        BattleUniversalStageDecorEntry entry = new();
        entry.label = string.IsNullOrWhiteSpace(sourceLabel) ? sourceCategory.ToString() : sourceLabel;
        entry.category = sourceCategory;
        entry.sprite = sourceSprite;
        entry.prefab = sourcePrefab;
        entry.lightRig = sourceLightRig;
        entry.footprint = ResolveFootprint(sourceFootprint);
        entry.weight = Mathf.Max(1, sourceWeight);
        entry.localOffset = sourceLocalOffset;
        entry.localScale = new Vector2(
            Mathf.Approximately(sourceLocalScale.x, 0f) ? 1f : sourceLocalScale.x,
            Mathf.Approximately(sourceLocalScale.y, 0f) ? 1f : sourceLocalScale.y);
        entry.sortingOrderOffset = sourceSortingOrderOffset;
        entry.randomFlipX = sourceRandomFlipX;
        entry.allowHalfTurn = false;
        return entry;
    }

    public static Vector2Int ResolveFootprint(Vector2Int value)
    {
        int x = Mathf.Max(1, value.x);
        int y = Mathf.Max(1, value.y);

        if (x >= 3 && y >= 3)
            return new Vector2Int(3, 3);

        if (x >= 2 && y >= 2)
        {
            if (x >= 3 || y >= 3)
                return x >= y ? new Vector2Int(3, 2) : new Vector2Int(2, 3);
            return new Vector2Int(2, 2);
        }

        return Vector2Int.one;
    }
}

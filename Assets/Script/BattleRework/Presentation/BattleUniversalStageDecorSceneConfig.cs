using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SpriteManager에 저장되는 Universal Stage Dressing의 씬별 Authoring 컴포넌트입니다.
///
/// Profile Asset을 별도로 만들지 않아도 Camera / Light / Cable 등의 Sprite 또는 Prefab을
/// 이 컴포넌트 Inspector에 직접 넣고 Scene에 저장할 수 있습니다.
/// 실행 시 저장된 직접 소스들을 transient BattleUniversalStageDecorProfileSO로 변환하여
/// 공용 BattleUniversalStageDecorController에 공급합니다.
///
/// Chair는 Reward / Map Show 전용이므로 이 Config에는 의도적으로 포함하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleUniversalStageDecorSceneConfig : MonoBehaviour
{
    [Header("MASTER")]
    [SerializeField] private bool disableUniversalStageDecor;
    [Tooltip("고급 설정용 선택 Profile입니다. 직접 소스가 하나도 없으면 이 Profile을 그대로 사용합니다.")]
    [SerializeField] private BattleUniversalStageDecorProfileSO profile;
    [Tooltip("직접 Sprite/Prefab 소스와 Profile의 Entries를 함께 랜덤 풀에 넣습니다.")]
    [SerializeField] private bool combineDirectSourcesWithProfile;

    [Header("CARRIER / COMMON")]
    [Tooltip("0이면 Profile의 CellWorldSize 또는 실제 Floor 크기를 사용합니다. 보통 기본값 0을 유지하면 됩니다.")]
    [SerializeField, Min(0f)] private float cellWorldSizeOverride;
    [Tooltip("아래 직접 Sprite/Prefab 소스에 공통으로 적용할 Sorting Order Offset입니다.")]
    [SerializeField] private int directSourceSortingOrderOffset = 12;
    [Tooltip("아래 직접 소스의 공통 Scale입니다. 개별 보정이 필요하면 Custom Sources를 사용하세요.")]
    [SerializeField] private Vector2 directSourceScale = Vector2.one;
    [Tooltip("장식물이 반대 방향으로 180도 회전되어 배치되는 것을 허용합니다.")]
    [SerializeField] private bool allowHalfTurn = true;

    [Header("1x1 SPRITE SOURCES")]
    [SerializeField] private List<Sprite> cableSprites = new();
    [SerializeField] private List<Sprite> lightSprites = new();
    [SerializeField] private List<Sprite> equipmentSprites = new();
    [SerializeField] private List<Sprite> caseSprites = new();
    [SerializeField] private List<Sprite> miscSprites = new();

    [Header("2x3 SPRITE SOURCES")]
    [Tooltip("카메라/삼각대 계열. Carrier는 2x3 또는 회전된 3x2로 배치됩니다.")]
    [SerializeField] private List<Sprite> cameraSprites = new();
    [SerializeField] private List<Sprite> lightStandSprites = new();
    [SerializeField] private List<Sprite> monitorSprites = new();

    [Header("3x3 SPRITE SOURCES")]
    [SerializeField] private List<Sprite> fenceSprites = new();
    [SerializeField] private List<Sprite> largeEquipmentSprites = new();

    [Header("OPTIONAL PREFAB SOURCES")]
    [Tooltip("Sprite 하나로 표현하기 어려운 카메라 세트/애니메이션 오브젝트를 넣습니다.")]
    [SerializeField] private List<GameObject> cameraPrefabs = new();
    [SerializeField] private List<GameObject> lightStandPrefabs = new();
    [SerializeField] private List<GameObject> lightPrefabs = new();
    [SerializeField] private List<GameObject> cablePrefabs = new();
    [SerializeField] private List<GameObject> monitorPrefabs = new();
    [SerializeField] private List<GameObject> equipmentPrefabs = new();
    [SerializeField] private List<GameObject> casePrefabs = new();
    [SerializeField] private List<GameObject> fencePrefabs = new();
    [SerializeField] private List<GameObject> miscPrefabs = new();

    [Header("CUSTOM SOURCES")]
    [Tooltip("기본 1x1/2x3/3x3 분류와 다른 Offset/Scale/Weight가 필요한 소스만 여기에 추가합니다.")]
    [SerializeField] private List<BattleUniversalStageDecorCustomSource> customSources = new();

    [NonSerialized] private BattleUniversalStageDecorProfileSO runtimeDirectProfile;
    [NonSerialized] private readonly List<BattleUniversalStageDecorEntry> runtimeEntries = new();

    public bool DisableUniversalStageDecor => disableUniversalStageDecor;

    /// <summary>
    /// Universal Runtime Manager가 읽는 최종 Profile입니다.
    /// 직접 소스가 있으면 Scene에 저장된 값으로 runtime profile을 만들어 반환합니다.
    /// </summary>
    public BattleUniversalStageDecorProfileSO Profile
    {
        get
        {
            if (!HasAnyDirectSource())
                return profile;

            EnsureRuntimeDirectProfile();
            RebuildRuntimeDirectProfile();
            return runtimeDirectProfile;
        }
    }

    private void OnEnable()
    {
        if (Application.isPlaying && HasAnyDirectSource())
        {
            EnsureRuntimeDirectProfile();
            RebuildRuntimeDirectProfile();
        }

        BattleUniversalStageDecorController.RequestRefresh(0.05f);
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
            BattleUniversalStageDecorController.RequestRefresh(0.05f);
    }

    private void OnDestroy()
    {
        if (runtimeDirectProfile == null)
            return;

        if (Application.isPlaying)
            Destroy(runtimeDirectProfile);
        else
            DestroyImmediate(runtimeDirectProfile);
        runtimeDirectProfile = null;
    }

    private void OnValidate()
    {
        directSourceScale = new Vector2(
            Mathf.Approximately(directSourceScale.x, 0f) ? 1f : directSourceScale.x,
            Mathf.Approximately(directSourceScale.y, 0f) ? 1f : directSourceScale.y);

        if (!Application.isPlaying)
            return;

        if (HasAnyDirectSource())
        {
            EnsureRuntimeDirectProfile();
            RebuildRuntimeDirectProfile();
        }

        BattleUniversalStageDecorController.RequestRefresh(0.05f);
    }

    private void EnsureRuntimeDirectProfile()
    {
        if (runtimeDirectProfile != null)
            return;

        runtimeDirectProfile = ScriptableObject.CreateInstance<BattleUniversalStageDecorProfileSO>();
        runtimeDirectProfile.name = $"{name}_RuntimeStageDecorProfile";
        runtimeDirectProfile.hideFlags = HideFlags.DontSave;
    }

    private void RebuildRuntimeDirectProfile()
    {
        if (runtimeDirectProfile == null)
            return;

        runtimeEntries.Clear();

        if (combineDirectSourcesWithProfile && profile != null && profile.Entries != null)
        {
            IReadOnlyList<BattleUniversalStageDecorEntry> profileEntries = profile.Entries;
            for (int i = 0; i < profileEntries.Count; i++)
            {
                BattleUniversalStageDecorEntry entry = profileEntries[i];
                if (entry == null || entry.Category == BattleUniversalStageDecorCategory.Chair || !entry.HasVisual)
                    continue;

                runtimeEntries.Add(BattleUniversalStageDecorEntry.CreateRuntime(
                    entry.Label,
                    entry.Category,
                    entry.Sprite,
                    entry.Prefab,
                    entry.Footprint,
                    entry.Weight,
                    entry.LocalOffset,
                    entry.LocalScale,
                    entry.SortingOrderOffset,
                    entry.RandomFlipX,
                    entry.AllowHalfTurn));
            }
        }

        // 1x1
        AppendSprites(cableSprites, BattleUniversalStageDecorCategory.Cable, Vector2Int.one, true);
        AppendSprites(lightSprites, BattleUniversalStageDecorCategory.Light, Vector2Int.one, false);
        AppendSprites(equipmentSprites, BattleUniversalStageDecorCategory.Equipment, Vector2Int.one, false);
        AppendSprites(caseSprites, BattleUniversalStageDecorCategory.Case, Vector2Int.one, false);
        AppendSprites(miscSprites, BattleUniversalStageDecorCategory.Misc, Vector2Int.one, false);

        // 2x3 / 3x2 Carrier
        AppendSprites(cameraSprites, BattleUniversalStageDecorCategory.Camera, new Vector2Int(2, 3), false);
        AppendSprites(lightStandSprites, BattleUniversalStageDecorCategory.LightStand, new Vector2Int(2, 3), false);
        AppendSprites(monitorSprites, BattleUniversalStageDecorCategory.Monitor, new Vector2Int(2, 3), false);

        // 3x3
        AppendSprites(fenceSprites, BattleUniversalStageDecorCategory.Fence, new Vector2Int(3, 3), false);
        AppendSprites(largeEquipmentSprites, BattleUniversalStageDecorCategory.Equipment, new Vector2Int(3, 3), false);

        // Prefab defaults
        AppendPrefabs(cameraPrefabs, BattleUniversalStageDecorCategory.Camera, new Vector2Int(2, 3), false);
        AppendPrefabs(lightStandPrefabs, BattleUniversalStageDecorCategory.LightStand, new Vector2Int(2, 3), false);
        AppendPrefabs(lightPrefabs, BattleUniversalStageDecorCategory.Light, Vector2Int.one, false);
        AppendPrefabs(cablePrefabs, BattleUniversalStageDecorCategory.Cable, Vector2Int.one, true);
        AppendPrefabs(monitorPrefabs, BattleUniversalStageDecorCategory.Monitor, new Vector2Int(2, 3), false);
        AppendPrefabs(equipmentPrefabs, BattleUniversalStageDecorCategory.Equipment, Vector2Int.one, false);
        AppendPrefabs(casePrefabs, BattleUniversalStageDecorCategory.Case, Vector2Int.one, false);
        AppendPrefabs(fencePrefabs, BattleUniversalStageDecorCategory.Fence, new Vector2Int(3, 3), false);
        AppendPrefabs(miscPrefabs, BattleUniversalStageDecorCategory.Misc, Vector2Int.one, false);

        if (customSources != null)
        {
            for (int i = 0; i < customSources.Count; i++)
            {
                BattleUniversalStageDecorCustomSource source = customSources[i];
                if (source == null || !source.HasVisual || source.Category == BattleUniversalStageDecorCategory.Chair)
                    continue;
                runtimeEntries.Add(source.CreateRuntimeEntry());
            }
        }

        float cell = cellWorldSizeOverride >= 0.25f
            ? cellWorldSizeOverride
            : profile != null
                ? profile.CellWorldSize
                : 1f;

        runtimeDirectProfile.ConfigureRuntime(cell, runtimeEntries);
    }

    private void AppendSprites(
        List<Sprite> sprites,
        BattleUniversalStageDecorCategory category,
        Vector2Int footprint,
        bool randomFlipX)
    {
        if (sprites == null)
            return;

        for (int i = 0; i < sprites.Count; i++)
        {
            Sprite sprite = sprites[i];
            if (sprite == null)
                continue;

            runtimeEntries.Add(BattleUniversalStageDecorEntry.CreateRuntime(
                $"{category}_{sprite.name}",
                category,
                sprite,
                null,
                footprint,
                1,
                Vector2.zero,
                directSourceScale,
                directSourceSortingOrderOffset,
                randomFlipX,
                allowHalfTurn));
        }
    }

    private void AppendPrefabs(
        List<GameObject> prefabs,
        BattleUniversalStageDecorCategory category,
        Vector2Int footprint,
        bool randomFlipX)
    {
        if (prefabs == null)
            return;

        for (int i = 0; i < prefabs.Count; i++)
        {
            GameObject prefab = prefabs[i];
            if (prefab == null)
                continue;

            runtimeEntries.Add(BattleUniversalStageDecorEntry.CreateRuntime(
                $"{category}_{prefab.name}",
                category,
                null,
                prefab,
                footprint,
                1,
                Vector2.zero,
                directSourceScale,
                directSourceSortingOrderOffset,
                randomFlipX,
                allowHalfTurn));
        }
    }

    private bool HasAnyDirectSource()
    {
        return HasAny(cameraSprites) || HasAny(lightStandSprites) || HasAny(lightSprites) ||
               HasAny(cableSprites) || HasAny(monitorSprites) || HasAny(equipmentSprites) ||
               HasAny(caseSprites) || HasAny(fenceSprites) || HasAny(miscSprites) ||
               HasAny(largeEquipmentSprites) ||
               HasAny(cameraPrefabs) || HasAny(lightStandPrefabs) || HasAny(lightPrefabs) ||
               HasAny(cablePrefabs) || HasAny(monitorPrefabs) || HasAny(equipmentPrefabs) ||
               HasAny(casePrefabs) || HasAny(fencePrefabs) || HasAny(miscPrefabs) ||
               HasAnyCustomSource();
    }

    private bool HasAnyCustomSource()
    {
        if (customSources == null)
            return false;

        for (int i = 0; i < customSources.Count; i++)
            if (customSources[i] != null && customSources[i].HasVisual)
                return true;
        return false;
    }

    private static bool HasAny<T>(List<T> list) where T : UnityEngine.Object
    {
        if (list == null)
            return false;

        for (int i = 0; i < list.Count; i++)
            if (list[i] != null)
                return true;
        return false;
    }
}

/// <summary>
/// SpriteManager의 기본 카테고리 슬롯으로 표현하기 어려운 개별 Stage Decor 소스용 설정입니다.
/// </summary>
[Serializable]
public sealed class BattleUniversalStageDecorCustomSource
{
    [SerializeField] private string label = "Custom Decor";
    [SerializeField] private BattleUniversalStageDecorCategory category = BattleUniversalStageDecorCategory.Misc;
    [SerializeField] private Sprite sprite;
    [SerializeField] private GameObject prefab;
    [SerializeField] private Vector2Int footprint = Vector2Int.one;
    [SerializeField, Min(1)] private int weight = 1;
    [SerializeField] private Vector2 localOffset;
    [SerializeField] private Vector2 localScale = Vector2.one;
    [SerializeField] private int sortingOrderOffset = 12;
    [SerializeField] private bool randomFlipX;
    [SerializeField] private bool allowHalfTurn = true;

    public BattleUniversalStageDecorCategory Category => category;
    public bool HasVisual => sprite != null || prefab != null;

    public BattleUniversalStageDecorEntry CreateRuntimeEntry()
    {
        return BattleUniversalStageDecorEntry.CreateRuntime(
            label,
            category,
            sprite,
            prefab,
            footprint,
            weight,
            localOffset,
            localScale,
            sortingOrderOffset,
            randomFlipX,
            allowHalfTurn);
    }
}

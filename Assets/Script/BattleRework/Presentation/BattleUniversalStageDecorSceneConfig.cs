using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// SpriteManager에 저장되는 Universal Stage Dressing의 씬별 Authoring 컴포넌트입니다.
/// Camera/Light 등의 실제 Carrier 소스와 Cable 장식을 분리합니다.
/// Cable은 절대 독립 Carrier로 Spawn되지 않고 Camera/Light Carrier 내부의 Floor 장식으로만 사용됩니다.
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
    [SerializeField] private int directSourceSortingOrderOffset = 12;
    [SerializeField] private Vector2 directSourceScale = Vector2.one;

    [Header("CABLE DECORATION ONLY - NEVER SPAWNS ALONE")]
    [Tooltip("Camera/Light Carrier의 Floor 위, 장비 아래에 랜덤 배치되는 전선 Sprite입니다. 독립 타일/Carrier로 생성되지 않습니다.")]
    [SerializeField] private List<Sprite> cableSprites = new();

    [Header("1x1 PROP SOURCES")]
    [SerializeField] private List<Sprite> equipmentSprites = new();
    [SerializeField] private List<Sprite> caseSprites = new();
    [SerializeField] private List<Sprite> miscSprites = new();

    [Header("2x2 CAMERA / LIGHT SOURCES")]
    [Tooltip("카메라는 2x2 Carrier 안에 배치됩니다. 좌우 반전만 허용하며 180도 회전하지 않습니다.")]
    [SerializeField] private List<Sprite> cameraSprites = new();
    [Tooltip("Light는 Base + Head 연결 Anchor를 가진 Light Rig SO로만 구성하는 것을 권장합니다.")]
    [SerializeField] private List<BattleUniversalStageLightRigSO> lightRigs = new();

    [Header("2x3 PROP SOURCES")]
    [SerializeField] private List<Sprite> monitorSprites = new();

    [Header("3x3 PROP SOURCES")]
    [SerializeField] private List<Sprite> fenceSprites = new();
    [SerializeField] private List<Sprite> largeEquipmentSprites = new();

    [Header("OPTIONAL PREFAB SOURCES")]
    [SerializeField] private List<GameObject> cameraPrefabs = new();
    [SerializeField] private List<GameObject> monitorPrefabs = new();
    [SerializeField] private List<GameObject> equipmentPrefabs = new();
    [SerializeField] private List<GameObject> casePrefabs = new();
    [SerializeField] private List<GameObject> fencePrefabs = new();
    [SerializeField] private List<GameObject> miscPrefabs = new();

    [Header("CUSTOM SOURCES")]
    [Tooltip("기본 분류와 다른 Offset/Scale/Weight가 필요한 소스만 사용합니다. Cable은 여기 넣어도 독립 Spawn에서 제외됩니다.")]
    [SerializeField] private List<BattleUniversalStageDecorCustomSource> customSources = new();

    // 기존 Scene 직렬화 호환용입니다. 더 이상 독립 Light/Stand/Cable Prefab으로 Spawn하지 않습니다.
    [FormerlySerializedAs("lightSprites"), SerializeField, HideInInspector] private List<Sprite> legacyLightSprites = new();
    [FormerlySerializedAs("lightStandSprites"), SerializeField, HideInInspector] private List<Sprite> legacyLightStandSprites = new();
    [FormerlySerializedAs("lightPrefabs"), SerializeField, HideInInspector] private List<GameObject> legacyLightPrefabs = new();
    [FormerlySerializedAs("lightStandPrefabs"), SerializeField, HideInInspector] private List<GameObject> legacyLightStandPrefabs = new();
    [FormerlySerializedAs("cablePrefabs"), SerializeField, HideInInspector] private List<GameObject> legacyCablePrefabs = new();

    [NonSerialized] private BattleUniversalStageDecorProfileSO runtimeDirectProfile;
    [NonSerialized] private readonly List<BattleUniversalStageDecorEntry> runtimeEntries = new();

    public bool DisableUniversalStageDecor => disableUniversalStageDecor;
    public IReadOnlyList<Sprite> CableDecorationSprites => cableSprites;

    public BattleUniversalStageDecorProfileSO Profile
    {
        get
        {
            if (!HasAnyDirectSpawnSource())
                return profile;

            EnsureRuntimeDirectProfile();
            RebuildRuntimeDirectProfile();
            return runtimeDirectProfile;
        }
    }

    private void OnEnable()
    {
        if (Application.isPlaying && HasAnyDirectSpawnSource())
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

        if (HasAnyDirectSpawnSource())
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
                if (entry == null ||
                    entry.Category == BattleUniversalStageDecorCategory.Chair ||
                    entry.Category == BattleUniversalStageDecorCategory.Cable ||
                    !entry.HasVisual)
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
                    false,
                    entry.LightRig));
            }
        }

        // 1x1 props. Cable은 여기서 절대 추가하지 않습니다.
        AppendSprites(equipmentSprites, BattleUniversalStageDecorCategory.Equipment, Vector2Int.one, false);
        AppendSprites(caseSprites, BattleUniversalStageDecorCategory.Case, Vector2Int.one, false);
        AppendSprites(miscSprites, BattleUniversalStageDecorCategory.Misc, Vector2Int.one, false);

        // Camera / Light = 2x2.
        AppendSprites(cameraSprites, BattleUniversalStageDecorCategory.Camera, new Vector2Int(2, 2), true);
        AppendLightRigs(lightRigs);

        // 기타 Props.
        AppendSprites(monitorSprites, BattleUniversalStageDecorCategory.Monitor, new Vector2Int(2, 3), false);
        AppendSprites(fenceSprites, BattleUniversalStageDecorCategory.Fence, new Vector2Int(3, 3), false);
        AppendSprites(largeEquipmentSprites, BattleUniversalStageDecorCategory.Equipment, new Vector2Int(3, 3), false);

        AppendPrefabs(cameraPrefabs, BattleUniversalStageDecorCategory.Camera, new Vector2Int(2, 2), true);
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
                if (source == null || !source.HasVisual ||
                    source.Category == BattleUniversalStageDecorCategory.Chair ||
                    source.Category == BattleUniversalStageDecorCategory.Cable)
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

    private void AppendLightRigs(List<BattleUniversalStageLightRigSO> rigs)
    {
        if (rigs == null)
            return;

        for (int i = 0; i < rigs.Count; i++)
        {
            BattleUniversalStageLightRigSO rig = rigs[i];
            if (rig == null || !rig.IsValid)
                continue;

            runtimeEntries.Add(BattleUniversalStageDecorEntry.CreateRuntime(
                $"LightRig_{rig.name}",
                BattleUniversalStageDecorCategory.Light,
                null,
                null,
                new Vector2Int(2, 2),
                1,
                Vector2.zero,
                directSourceScale,
                directSourceSortingOrderOffset,
                false,
                false,
                rig));
        }
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
                false));
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
                false));
        }
    }

    private bool HasAnyDirectSpawnSource()
    {
        return HasAny(cameraSprites) || HasAny(lightRigs) || HasAny(monitorSprites) ||
               HasAny(equipmentSprites) || HasAny(caseSprites) || HasAny(fenceSprites) ||
               HasAny(miscSprites) || HasAny(largeEquipmentSprites) ||
               HasAny(cameraPrefabs) || HasAny(monitorPrefabs) || HasAny(equipmentPrefabs) ||
               HasAny(casePrefabs) || HasAny(fencePrefabs) || HasAny(miscPrefabs) ||
               HasAnyCustomSource();
    }

    private bool HasAnyCustomSource()
    {
        if (customSources == null)
            return false;

        for (int i = 0; i < customSources.Count; i++)
        {
            BattleUniversalStageDecorCustomSource source = customSources[i];
            if (source != null && source.HasVisual && source.Category != BattleUniversalStageDecorCategory.Cable)
                return true;
        }
        return false;
    }

    private static bool HasAny<T>(IReadOnlyList<T> list) where T : UnityEngine.Object
    {
        if (list == null)
            return false;

        for (int i = 0; i < list.Count; i++)
            if (list[i] != null)
                return true;
        return false;
    }
}

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
            false);
    }
}

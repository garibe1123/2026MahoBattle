using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Room/Show 타일이 이동·도킹하는 동안 Floor가 Handle/Base보다 항상 앞에 렌더되도록 보장합니다.
/// 기존 Handle 접합면 숨김 로직은 그대로 유지하지만, 시각적으로는 접합 판정이 늦더라도
/// 겹친 Hardware가 Floor 위로 튀어나오지 않게 같은 프레임 LateUpdate에서 Sorting Order를 정리합니다.
///
/// 안정 상태에서는 동작하지 않으므로 전투 중 상시 Scene Scan 비용은 발생하지 않습니다.
/// </summary>
[DefaultExecutionOrder(32600)]
[DisallowMultipleComponent]
public sealed class BattleFieldHardwareSortingGuard : MonoBehaviour
{
    private const string ExitBasePrefix = "ExitPieceBase_";

    private enum HardwareKind
    {
        None,
        Handle,
        Base
    }

    private static BattleFieldHardwareSortingGuard instance;

    private readonly List<SpriteRenderer> renderers = new();
    private readonly HashSet<int> seenRendererIds = new();
    private readonly Dictionary<int, int> minimumFloorOrderByLayer = new();

    private BattleStageTransitionController stageFlow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (instance != null || FindFirstObjectByType<BattleFieldHardwareSortingGuard>() != null)
            return;

        GameObject host = new("BattleFieldHardwareSortingGuardRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleFieldHardwareSortingGuard>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        ResolveStageFlow();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void LateUpdate()
    {
        if (stageFlow == null)
            ResolveStageFlow();

        // 실제 이동/도킹 중에만 같은 프레임으로 Sorting을 강제합니다.
        // 안정된 Combat/Reward/Map 상태의 최종 정리는 기존
        // BattleDockHandleVisibilityController.RefreshNow()가 담당합니다.
        if (stageFlow == null || !stageFlow.IsStageTransitioning)
            return;

        NormalizeFieldHardwareSorting();
    }

    private void ResolveStageFlow()
    {
        stageFlow = BattleStageTransitionController.Instance != null
            ? BattleStageTransitionController.Instance
            : FindFirstObjectByType<BattleStageTransitionController>();
    }

    private void NormalizeFieldHardwareSorting()
    {
        renderers.Clear();
        seenRendererIds.Clear();
        minimumFloorOrderByLayer.Clear();

        RoomBaseTemplate baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (baseTemplate != null && baseTemplate.ActiveBase != null)
            AddRenderers(baseTemplate.ActiveBase);

        MapBlock[] blocks = FindObjectsByType<MapBlock>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (!IsLiveFloorBlock(block))
                continue;

            AddRenderers(block.gameObject);
        }

        // 같은 Sorting Layer에 있는 모든 Floor 중 가장 낮은 Order를 기준으로 잡습니다.
        // Hardware를 그보다 낮추면 어떤 Floor 조각과 겹치더라도 Floor가 반드시 앞에 옵니다.
        for (int i = 0; i < renderers.Count; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (!IsActiveRenderer(renderer) || !IsFloorRenderer(renderer))
                continue;

            int layerId = renderer.sortingLayerID;
            if (!minimumFloorOrderByLayer.TryGetValue(layerId, out int current) ||
                renderer.sortingOrder < current)
            {
                minimumFloorOrderByLayer[layerId] = renderer.sortingOrder;
            }
        }

        for (int i = 0; i < renderers.Count; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (!IsActiveRenderer(renderer) || IsFloorRenderer(renderer))
                continue;

            HardwareKind kind = ResolveHardwareKind(renderer.transform);
            if (kind == HardwareKind.None)
                continue;

            if (!minimumFloorOrderByLayer.TryGetValue(renderer.sortingLayerID, out int floorOrder))
                continue;

            int maximumOrder = kind == HardwareKind.Base
                ? floorOrder - 2
                : floorOrder - 1;

            if (renderer.sortingOrder > maximumOrder)
                renderer.sortingOrder = maximumOrder;
        }
    }

    private void AddRenderers(GameObject root)
    {
        if (root == null)
            return;

        SpriteRenderer[] found = root.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < found.Length; i++)
        {
            SpriteRenderer renderer = found[i];
            if (renderer == null || !seenRendererIds.Add(renderer.GetInstanceID()))
                continue;

            renderers.Add(renderer);
        }
    }

    private static HardwareKind ResolveHardwareKind(Transform source)
    {
        if (source == null)
            return HardwareKind.None;

        string sourceName = source.name;
        if (sourceName.StartsWith("LowerPlate_", StringComparison.Ordinal) ||
            sourceName.StartsWith("BaseLowerPlate_", StringComparison.Ordinal) ||
            sourceName.StartsWith(ExitBasePrefix, StringComparison.Ordinal))
        {
            return HardwareKind.Base;
        }

        Transform cursor = source;
        while (cursor != null)
        {
            string name = cursor.name;
            if (name.StartsWith("DockHandle_", StringComparison.Ordinal))
                return HardwareKind.Handle;

            if (name == "PresentationTemplate")
                break;

            cursor = cursor.parent;
        }

        return HardwareKind.None;
    }

    private static bool IsActiveRenderer(SpriteRenderer renderer)
    {
        return renderer != null &&
               renderer.enabled &&
               renderer.sprite != null &&
               renderer.gameObject.activeInHierarchy;
    }

    private static bool IsFloorRenderer(SpriteRenderer renderer)
    {
        if (renderer == null)
            return false;

        string objectName = renderer.gameObject.name;
        return objectName.StartsWith("Tile_", StringComparison.Ordinal) ||
               objectName.StartsWith("ShowTile_", StringComparison.Ordinal) ||
               objectName.StartsWith("BaseFloor_", StringComparison.Ordinal) ||
               renderer.GetComponent<BattleWalkableField>() != null;
    }

    private static bool IsLiveFloorBlock(MapBlock block)
    {
        if (block == null || !block.gameObject.activeInHierarchy)
            return false;

        string blockName = block.name;
        bool rawPrototype =
            blockName.StartsWith("__RuntimeRoomPiecePrototype_", StringComparison.Ordinal) &&
            !blockName.EndsWith("(Clone)", StringComparison.Ordinal);

        return !rawPrototype && !blockName.StartsWith("Outgoing_", StringComparison.Ordinal);
    }
}

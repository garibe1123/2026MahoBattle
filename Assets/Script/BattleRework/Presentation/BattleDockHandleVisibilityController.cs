using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 현재 필드의 최종 점유 셀을 기준으로 도킹 손잡이 가시성과 Field Hardware의 렌더 우선순위를 정리합니다.
///
/// 규칙:
/// - Persistent 4x4 Base는 손잡이를 사용하지 않습니다. 생성되어 있더라도 항상 숨깁니다.
/// - 일반 전투 MapBlock / Reward Show Floor는 다른 바닥과 맞닿는 내부 접합면의 손잡이만 숨깁니다.
/// - Floor Renderer는 같은 Sorting Layer의 Base/LowerPlate/Handle/기타 Hardware보다 항상 앞에 렌더됩니다.
/// - RoomExiting이 시작되는 순간, Piece가 분리/이동되기 전에 각 Floor에 Exit 전용 Base를 의무적으로 붙입니다.
/// - Persistent 4x4에 흡수되어 Floor가 숨겨진 셀의 Base/LowerPlate/Handle도 같이 숨깁니다.
///   따라서 퇴장 중에는 "Floor만 있는 Piece"와 "Floor 없이 하드웨어만 있는 Piece"가 모두 금지됩니다.
/// </summary>
[DefaultExecutionOrder(22000)]
[DisallowMultipleComponent]
public sealed class BattleDockHandleVisibilityController : MonoBehaviour
{
    private const string ExitBasePrefix = "ExitPieceBase_";

    [SerializeField, Min(0.02f)] private float refreshInterval = 0.06f;

    private float nextRefreshAt;
    private BattleStageTransitionController boundStageFlow;
    private Coroutine bindRoutine;
    private bool warnedMissingExitBaseSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleDockHandleVisibilityController>() != null)
            return;

        GameObject host = new("BattleDockHandleVisibilityRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleDockHandleVisibilityController>();
    }

    private void OnEnable()
    {
        if (bindRoutine == null)
            bindRoutine = StartCoroutine(BindStageFlowWhenReady());
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
            StopCoroutine(bindRoutine);
        bindRoutine = null;
        UnbindStageFlow();
    }

    private IEnumerator BindStageFlowWhenReady()
    {
        while (enabled)
        {
            BattleStageTransitionController resolved = BattleStageTransitionController.Instance;
            if (resolved != null)
            {
                BindStageFlow(resolved);
                break;
            }

            yield return null;
        }

        bindRoutine = null;
    }

    private void BindStageFlow(BattleStageTransitionController stageFlow)
    {
        if (boundStageFlow == stageFlow)
            return;

        UnbindStageFlow();
        boundStageFlow = stageFlow;
        boundStageFlow.FlowStateChanged += HandleStageFlowChanged;
    }

    private void UnbindStageFlow()
    {
        if (boundStageFlow != null)
            boundStageFlow.FlowStateChanged -= HandleStageFlowChanged;
        boundStageFlow = null;
    }

    private void HandleStageFlowChanged(BattleStageFlowState next)
    {
        if (next != BattleStageFlowState.RoomExiting)
            return;

        // SetFlowState(RoomExiting)는 Collapse 코루틴을 시작하기 전에 동기적으로 호출됩니다.
        // 여기서 Base를 먼저 붙이면 이후 Assembly가 Piece로 분리되어도 Base가 Piece와 함께 이동합니다.
        PrepareMandatoryExitPieceBases();
    }

    private void Update()
    {
        if (boundStageFlow == null && bindRoutine == null)
            bindRoutine = StartCoroutine(BindStageFlowWhenReady());

        if (Time.unscaledTime < nextRefreshAt)
            return;

        nextRefreshAt = Time.unscaledTime + Mathf.Max(0.02f, refreshInterval);
        RefreshNow();
    }

    private void LateUpdate()
    {
        BattleStageTransitionController stageFlow = BattleStageTransitionController.Instance;
        if (stageFlow == null || stageFlow.FlowState != BattleStageFlowState.RoomExiting)
            return;

        // StageTransition이 같은 프레임에 Persistent 4x4 중복 Floor를 숨길 수 있습니다.
        // LateUpdate에서 그 결과를 보고, Floor가 사라진 셀의 Base/하판/손잡이까지 함께 정리합니다.
        CullOrphanedPresentationHardware();

        // Exit Tween이 시작되는 바로 그 프레임에도 Floor가 Base/Handle 뒤로 내려가지 않도록
        // 최종 렌더 단계에서 Sorting invariant를 다시 적용합니다.
        EnforceFloorAboveHardware();
    }

    /// <summary>
    /// 퇴장 시점의 실제 MapBlock Piece마다 보이는 Floor를 기준으로 Base를 사전 설치합니다.
    /// Base는 각 Floor의 한 칸 아래에 Piece 자식으로 생성되므로 Piece 분리 후에도 반드시 같이 이동합니다.
    /// 기존 LowerPlate는 비활성화하여 오래된 Assembly 형상과 새 Piece 형상이 겹치지 않게 합니다.
    /// </summary>
    private void PrepareMandatoryExitPieceBases()
    {
        BattleShowFloorTemplateSO template = BattleShowPresentationManager.Instance != null
            ? BattleShowPresentationManager.Instance.ActiveFloorTemplate
            : null;

        MapBlock[] blocks = FindObjectsByType<MapBlock>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (!IsLiveFloorBlock(block))
                continue;

            PrepareMandatoryExitBasesForBlock(block, template);
        }
    }

    private void PrepareMandatoryExitBasesForBlock(
        MapBlock block,
        BattleShowFloorTemplateSO template)
    {
        if (block == null)
            return;

        Transform visual = block.transform.Find("Visual");
        if (visual == null)
            visual = block.transform;

        List<SpriteRenderer> floorRenderers = CollectOwnedFloorRenderers(block);
        if (floorRenderers.Count == 0)
            return;

        // 이전 시도/Hot Reload에서 남은 Exit Base가 있으면 중복 생성하지 않습니다.
        RemoveGeneratedExitBases(visual);

        Sprite existingLowerPlateSprite = FindExistingLowerPlateSprite(block);
        DisableExistingLowerPlates(block);

        HashSet<Vector2Int> floorCells = new();
        Dictionary<Vector2Int, SpriteRenderer> floorByCell = new();
        for (int i = 0; i < floorRenderers.Count; i++)
        {
            SpriteRenderer floor = floorRenderers[i];
            if (!IsActiveRenderer(floor))
                continue;

            Vector2Int cell = WorldToCell(floor.bounds.center);
            floorCells.Add(cell);
            floorByCell[cell] = floor;
        }

        foreach (KeyValuePair<Vector2Int, SpriteRenderer> pair in floorByCell)
        {
            Vector2Int cell = pair.Key;
            SpriteRenderer floor = pair.Value;
            bool hasLeft = floorCells.Contains(cell + Vector2Int.left);
            bool hasRight = floorCells.Contains(cell + Vector2Int.right);

            Sprite baseSprite = ResolveExitBaseSprite(
                template,
                existingLowerPlateSprite,
                floor.sprite,
                hasLeft,
                hasRight);
            if (baseSprite == null)
                continue;

            GameObject baseObject = new($"{ExitBasePrefix}{cell.x}_{cell.y}");
            Transform baseTransform = baseObject.transform;
            baseTransform.SetParent(visual, true);
            baseTransform.position = floor.transform.position + Vector3.down * RoomBaseTemplate.TileWorldSize;
            baseTransform.rotation = floor.transform.rotation;

            SpriteRenderer baseRenderer = baseObject.AddComponent<SpriteRenderer>();
            baseRenderer.sprite = baseSprite;
            baseRenderer.color = template != null
                ? template.PlateTint
                : Color.Lerp(floor.color, Color.black, 0.25f);
            baseRenderer.sortingLayerID = floor.sortingLayerID;

            int requestedOrder = template != null
                ? template.LowerPlateSortingOrder
                : floor.sortingOrder - 2;
            baseRenderer.sortingOrder = Mathf.Min(requestedOrder, floor.sortingOrder - 2);
        }
    }

    private Sprite ResolveExitBaseSprite(
        BattleShowFloorTemplateSO template,
        Sprite existingLowerPlateSprite,
        Sprite floorFallback,
        bool hasLeft,
        bool hasRight)
    {
        Sprite resolved = null;
        if (template != null)
        {
            if (!hasLeft && hasRight)
            {
                resolved = template.LowerPlateLeftSprite32 != null
                    ? template.LowerPlateLeftSprite32
                    : template.LowerPlateCenterSprite32;
            }
            else if (hasLeft && !hasRight)
            {
                resolved = template.LowerPlateRightSprite32 != null
                    ? template.LowerPlateRightSprite32
                    : template.LowerPlateCenterSprite32;
            }
            else
            {
                resolved = template.LowerPlateCenterSprite32;
            }

            resolved ??= template.LowerPlateLeftSprite32;
            resolved ??= template.LowerPlateRightSprite32;
        }

        resolved ??= existingLowerPlateSprite;
        if (resolved != null)
            return resolved;

        // SO/기존 하판이 모두 비어 있어도 Floor-only Piece는 허용하지 않습니다.
        // 최후 fallback으로 Floor sprite를 하판 위치에 사용하고 경고를 한 번 남깁니다.
        if (!warnedMissingExitBaseSprite && floorFallback != null)
        {
            warnedMissingExitBaseSprite = true;
            Debug.LogWarning(
                "[BattleDockHandleVisibility] Exit Piece용 LowerPlate Sprite가 없어 Floor Sprite를 Base fallback으로 사용합니다. " +
                "BattleShowFloorTemplateSO의 Lower Plate 이미지를 지정하는 것을 권장합니다.",
                this);
        }

        return floorFallback;
    }

    private static List<SpriteRenderer> CollectOwnedFloorRenderers(MapBlock block)
    {
        List<SpriteRenderer> result = new();
        if (block == null)
            return result;

        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (!IsActiveRenderer(renderer) || !IsFloorRenderer(renderer))
                continue;
            if (renderer.GetComponentInParent<MapBlock>() != block)
                continue;

            result.Add(renderer);
        }

        return result;
    }

    private static Sprite FindExistingLowerPlateSprite(MapBlock block)
    {
        if (block == null)
            return null;

        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null)
                continue;
            if (renderer.GetComponentInParent<MapBlock>() != block)
                continue;
            if (!renderer.name.StartsWith("LowerPlate_", StringComparison.Ordinal))
                continue;

            return renderer.sprite;
        }

        return null;
    }

    private static void DisableExistingLowerPlates(MapBlock block)
    {
        if (block == null)
            return;

        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.GetComponentInParent<MapBlock>() != block)
                continue;
            if (renderer.name.StartsWith("LowerPlate_", StringComparison.Ordinal))
                renderer.enabled = false;
        }
    }

    private static void RemoveGeneratedExitBases(Transform visual)
    {
        if (visual == null)
            return;

        for (int i = visual.childCount - 1; i >= 0; i--)
        {
            Transform child = visual.GetChild(i);
            if (child == null || !child.name.StartsWith(ExitBasePrefix, StringComparison.Ordinal))
                continue;

            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }

    public static void RefreshNow()
    {
        RoomBaseTemplate baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        MapBlock[] blocks = FindObjectsByType<MapBlock>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        HashSet<Vector2Int> occupied = new();
        HashSet<Vector2Int> baseCells = new();

        if (baseTemplate != null && baseTemplate.ActiveBase != null)
        {
            Vector2Int origin = WorldToCell(baseTemplate.FixedTileOriginWorld);
            for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
            {
                for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                {
                    Vector2Int cell = origin + new Vector2Int(x, y);
                    baseCells.Add(cell);
                    occupied.Add(cell);
                }
            }
        }

        Dictionary<MapBlock, HashSet<Vector2Int>> blockCells = new();
        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (!IsLiveFloorBlock(block))
                continue;

            HashSet<Vector2Int> cells = CollectBlockCells(block);
            if (cells.Count == 0)
                continue;

            blockCells[block] = cells;
            foreach (Vector2Int cell in cells)
                occupied.Add(cell);
        }

        // Persistent 4x4 Base에는 손잡이를 사용하지 않습니다.
        // PresentationTemplate이 재구축되어 손잡이 그룹이 다시 생겨도 즉시 숨깁니다.
        if (baseTemplate != null && baseTemplate.ActiveBase != null)
        {
            Transform template = baseTemplate.ActiveBase.transform.Find("PresentationTemplate");
            if (template != null)
                HideAllHandles(template);
        }

        foreach (KeyValuePair<MapBlock, HashSet<Vector2Int>> pair in blockCells)
        {
            MapBlock block = pair.Key;
            if (block == null)
                continue;

            Transform visual = block.transform.Find("Visual");
            if (visual == null)
                visual = block.transform;

            Transform template = visual.Find("PresentationTemplate");
            if (template != null)
                ApplyContactVisibility(template, pair.Value, occupied);
        }

        BattleStageTransitionController stageFlow = BattleStageTransitionController.Instance;
        if (stageFlow != null && stageFlow.FlowState == BattleStageFlowState.RoomExiting)
            CullOrphanedPresentationHardware();

        EnforceFloorAboveHardware();
    }

    /// <summary>
    /// Room collapse에서 Floor가 Persistent 4x4로 흡수되어 renderer.enabled=false가 된 뒤에도
    /// PresentationTemplate의 LowerPlate/Handle 또는 Exit Base는 별도 오브젝트라 살아 있을 수 있습니다.
    /// 각 Hardware가 실제로 기대하는 "지원 Floor 셀"이 같은 MapBlock에 보이는지 검사하여
    /// 지원 Floor가 없으면 Hardware renderer도 즉시 숨깁니다.
    /// </summary>
    private static void CullOrphanedPresentationHardware()
    {
        MapBlock[] blocks = FindObjectsByType<MapBlock>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (!IsLiveFloorBlock(block))
                continue;

            CullOrphanedHardwareForBlock(block);
        }
    }

    private static void CullOrphanedHardwareForBlock(MapBlock block)
    {
        if (block == null)
            return;

        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        HashSet<Vector2Int> visibleFloorCells = new();

        // Assembly root가 nested Piece까지 포함해 검색해도, renderer의 가장 가까운 MapBlock이
        // 현재 block인 경우만 취급합니다. 따라서 Piece끼리 서로의 Floor를 지원 셀로 오인하지 않습니다.
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (!IsActiveRenderer(renderer) || !IsFloorRenderer(renderer))
                continue;
            if (renderer.GetComponentInParent<MapBlock>() != block)
                continue;

            visibleFloorCells.Add(WorldToCell(renderer.bounds.center));
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null || IsFloorRenderer(renderer))
                continue;
            if (renderer.GetComponentInParent<MapBlock>() != block)
                continue;
            if (!TryResolveHardwareSupportOffset(renderer.transform, block.transform, out Vector2Int supportOffset))
                continue;

            Vector2Int hardwareCell = WorldToCell(renderer.bounds.center);
            Vector2Int supportCell = hardwareCell + supportOffset;
            if (!visibleFloorCells.Contains(supportCell))
                renderer.enabled = false;
        }
    }

    private static bool TryResolveHardwareSupportOffset(
        Transform rendererTransform,
        Transform blockRoot,
        out Vector2Int supportOffset)
    {
        supportOffset = Vector2Int.zero;
        if (rendererTransform == null)
            return false;

        // LowerPlate_*과 퇴장용 ExitPieceBase_*는 자신보다 한 칸 위의 Floor가 있어야만 존재할 수 있습니다.
        if (rendererTransform.name.StartsWith("LowerPlate_", StringComparison.Ordinal) ||
            rendererTransform.name.StartsWith(ExitBasePrefix, StringComparison.Ordinal))
        {
            supportOffset = Vector2Int.up;
            return true;
        }

        Transform current = rendererTransform.parent;
        while (current != null && current != blockRoot)
        {
            switch (current.name)
            {
                case "DockHandle_Lower":
                    supportOffset = Vector2Int.up;
                    return true;
                case "DockHandle_Upper":
                    supportOffset = Vector2Int.down;
                    return true;
                case "DockHandle_Left":
                    supportOffset = Vector2Int.right;
                    return true;
                case "DockHandle_Right":
                    supportOffset = Vector2Int.left;
                    return true;
            }

            current = current.parent;
        }

        return false;
    }

    /// <summary>
    /// 같은 Sorting Layer 안에서 가장 낮은 Floor order보다 Hardware가 앞으로 올라오지 못하게 내립니다.
    /// Floor 자체를 위로 올리지 않으므로 Player/VFX 등 다른 시스템의 렌더 우선순위를 침범하지 않습니다.
    /// </summary>
    private static void EnforceFloorAboveHardware()
    {
        List<SpriteRenderer> renderers = CollectFieldRenderers();
        if (renderers.Count == 0)
            return;

        Dictionary<int, int> minimumFloorOrderByLayer = new();
        for (int i = 0; i < renderers.Count; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (!IsActiveRenderer(renderer) || !IsFloorRenderer(renderer))
                continue;

            int layer = renderer.sortingLayerID;
            if (!minimumFloorOrderByLayer.TryGetValue(layer, out int current) || renderer.sortingOrder < current)
                minimumFloorOrderByLayer[layer] = renderer.sortingOrder;
        }

        if (minimumFloorOrderByLayer.Count == 0)
            return;

        for (int i = 0; i < renderers.Count; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (!IsActiveRenderer(renderer) || IsFloorRenderer(renderer))
                continue;

            if (!minimumFloorOrderByLayer.TryGetValue(renderer.sortingLayerID, out int floorOrder))
                continue;

            int maxHardwareOrder = floorOrder - 1;
            if (renderer.sortingOrder > maxHardwareOrder)
                renderer.sortingOrder = maxHardwareOrder;
        }
    }

    private static List<SpriteRenderer> CollectFieldRenderers()
    {
        List<SpriteRenderer> result = new();
        HashSet<int> seen = new();

        RoomBaseTemplate baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (baseTemplate != null && baseTemplate.ActiveBase != null)
            AddRenderers(baseTemplate.ActiveBase, result, seen);

        MapBlock[] blocks = FindObjectsByType<MapBlock>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (block == null || !block.gameObject.activeInHierarchy)
                continue;
            AddRenderers(block.gameObject, result, seen);
        }

        return result;
    }

    private static void AddRenderers(GameObject root, List<SpriteRenderer> result, HashSet<int> seen)
    {
        if (root == null)
            return;

        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !seen.Add(renderer.GetInstanceID()))
                continue;
            result.Add(renderer);
        }
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

    private static HashSet<Vector2Int> CollectBlockCells(MapBlock block)
    {
        HashSet<Vector2Int> cells = new();
        if (block == null)
            return cells;

        Transform[] transforms = block.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform tile = transforms[i];
            if (!IsFloorTile(tile))
                continue;

            SpriteRenderer renderer = tile.GetComponent<SpriteRenderer>();
            if (renderer == null || !renderer.enabled || renderer.sprite == null || !tile.gameObject.activeInHierarchy)
                continue;

            // nested Piece를 가진 Assembly root가 다른 Piece의 Tile까지 자기 셀로 잡지 않게 합니다.
            if (tile.GetComponentInParent<MapBlock>() != block)
                continue;

            Vector3 world = tile.position;
            if (block.HasEntryDestination)
                world += block.EntryDestination - block.transform.position;

            cells.Add(WorldToCell(world));
        }

        return cells;
    }

    private static bool IsFloorTile(Transform target)
    {
        if (target == null)
            return false;

        return target.name.StartsWith("Tile_", StringComparison.Ordinal) ||
               target.name.StartsWith("ShowTile_", StringComparison.Ordinal);
    }

    private static void HideAllHandles(Transform templateRoot)
    {
        if (templateRoot == null)
            return;

        SetGroupVisible(templateRoot, "DockHandle_Left", false);
        SetGroupVisible(templateRoot, "DockHandle_Right", false);
        SetGroupVisible(templateRoot, "DockHandle_Upper", false);
        SetGroupVisible(templateRoot, "DockHandle_Lower", false);
    }

    private static void ApplyContactVisibility(
        Transform templateRoot,
        HashSet<Vector2Int> ownCells,
        HashSet<Vector2Int> occupied)
    {
        if (templateRoot == null || ownCells == null || ownCells.Count == 0)
            return;

        SetGroupVisible(
            templateRoot,
            "DockHandle_Left",
            !TouchesOtherFloor(ownCells, occupied, Vector2Int.left));
        SetGroupVisible(
            templateRoot,
            "DockHandle_Right",
            !TouchesOtherFloor(ownCells, occupied, Vector2Int.right));
        SetGroupVisible(
            templateRoot,
            "DockHandle_Upper",
            !TouchesOtherFloor(ownCells, occupied, Vector2Int.up));
        SetGroupVisible(
            templateRoot,
            "DockHandle_Lower",
            !TouchesOtherFloor(ownCells, occupied, Vector2Int.down));
    }

    private static bool TouchesOtherFloor(
        HashSet<Vector2Int> ownCells,
        HashSet<Vector2Int> occupied,
        Vector2Int side)
    {
        foreach (Vector2Int cell in ownCells)
        {
            Vector2Int neighbor = cell + side;
            if (!ownCells.Contains(neighbor) && occupied.Contains(neighbor))
                return true;
        }

        return false;
    }

    private static void SetGroupVisible(Transform templateRoot, string groupName, bool visible)
    {
        Transform group = templateRoot.Find(groupName);
        if (group != null && group.gameObject.activeSelf != visible)
            group.gameObject.SetActive(visible);
    }

    private static Vector2Int WorldToCell(Vector3 world)
    {
        float size = Mathf.Max(0.0001f, RoomBaseTemplate.TileWorldSize);
        return new Vector2Int(
            Mathf.RoundToInt(world.x / size),
            Mathf.RoundToInt(world.y / size));
    }
}

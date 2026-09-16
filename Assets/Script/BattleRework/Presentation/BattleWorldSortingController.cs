using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Battle World Sorting의 Runtime Owner입니다.
///
/// 고정 Field 하드웨어는 Transition 중 같은 프레임 LateUpdate에서 정규화하고,
/// Player/Monster에는 BattleWorldSortAnchor를 자동 부착합니다.
/// 안정된 Combat 상태에서는 Scene 전체를 계속 Scan하지 않습니다.
/// </summary>
[DefaultExecutionOrder(32500)]
[DisallowMultipleComponent]
public sealed class BattleWorldSortingController : MonoBehaviour
{
    private const string ExitBasePrefix = "ExitPieceBase_";

    private static BattleWorldSortingController instance;

    private readonly List<SpriteRenderer> rendererBuffer = new();
    private readonly HashSet<int> seenRendererIds = new();

    private BattleStageTransitionController stageFlow;
    private bool stageFlowSubscribed;
    private bool normalizeRequested = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (instance != null || FindFirstObjectByType<BattleWorldSortingController>() != null)
            return;

        GameObject host = new("BattleWorldSortingRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleWorldSortingController>();
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

    private void OnEnable()
    {
        normalizeRequested = true;
        ResolveStageFlow();
    }

    private void OnDisable()
    {
        UnsubscribeStageFlow();
    }

    private void OnDestroy()
    {
        UnsubscribeStageFlow();
        if (instance == this)
            instance = null;
    }

    private void LateUpdate()
    {
        ResolveStageFlow();

        bool transitioning = stageFlow != null && stageFlow.IsStageTransitioning;
        if (!normalizeRequested && !transitioning)
            return;

        NormalizeWorldSorting();
        normalizeRequested = false;
    }

    public void RequestNormalize()
    {
        normalizeRequested = true;
    }

    private void ResolveStageFlow()
    {
        BattleStageTransitionController resolved = BattleStageTransitionController.Instance != null
            ? BattleStageTransitionController.Instance
            : FindFirstObjectByType<BattleStageTransitionController>();

        if (resolved == stageFlow)
        {
            SubscribeStageFlow();
            return;
        }

        UnsubscribeStageFlow();
        stageFlow = resolved;
        SubscribeStageFlow();
        normalizeRequested = true;
    }

    private void SubscribeStageFlow()
    {
        if (stageFlow == null || stageFlowSubscribed)
            return;

        stageFlow.FlowStateChanged += HandleFlowStateChanged;
        stageFlowSubscribed = true;
    }

    private void UnsubscribeStageFlow()
    {
        if (!stageFlowSubscribed || stageFlow == null)
        {
            stageFlowSubscribed = false;
            return;
        }

        stageFlow.FlowStateChanged -= HandleFlowStateChanged;
        stageFlowSubscribed = false;
    }

    private void HandleFlowStateChanged(BattleStageFlowState _)
    {
        normalizeRequested = true;
    }

    private void NormalizeWorldSorting()
    {
        EnsureCurrentActorAnchors();

        rendererBuffer.Clear();
        seenRendererIds.Clear();

        RoomBaseTemplate baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        GameObject activeBase = baseTemplate != null ? baseTemplate.ActiveBase : null;
        bool activeBaseHasPresentationFloor = activeBase != null && HasPresentationFloor(activeBase.transform);

        if (activeBase != null)
            AddRenderers(activeBase);

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

        for (int i = 0; i < rendererBuffer.Count; i++)
        {
            SpriteRenderer renderer = rendererBuffer[i];
            if (!IsActiveRenderer(renderer))
                continue;

            if (!TryResolveFixedOrder(renderer, activeBase, activeBaseHasPresentationFloor, out int order))
                continue;

            if (renderer.sortingOrder != order)
                renderer.sortingOrder = order;
        }
    }

    private static void EnsureCurrentActorAnchors()
    {
        PlayerController[] players = FindObjectsByType<PlayerController>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerController player = players[i];
            if (player != null)
                BattleWorldSortAnchor.Ensure(player.gameObject);
        }

        MonsterController[] monsters = FindObjectsByType<MonsterController>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < monsters.Length; i++)
        {
            MonsterController monster = monsters[i];
            if (monster != null)
                BattleWorldSortAnchor.Ensure(monster.gameObject);
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

            rendererBuffer.Add(renderer);
        }
    }

    private static bool TryResolveFixedOrder(
        SpriteRenderer renderer,
        GameObject activeBase,
        bool activeBaseHasPresentationFloor,
        out int order)
    {
        order = 0;
        if (renderer == null)
            return false;

        Transform target = renderer.transform;
        string objectName = target.name;

        // Persistent Base의 원본 Renderer는 Walkable marker가 붙어 있어도
        // Presentation Floor가 존재할 때는 Base 층으로 남겨야 합니다.
        if (activeBase != null && target.IsChildOf(activeBase.transform))
        {
            bool underPresentation = IsUnderPresentationTemplate(target);
            if (!underPresentation)
            {
                order = activeBaseHasPresentationFloor
                    ? BattleWorldSorting.BaseOrder
                    : BattleWorldSorting.FloorOrder;
                return true;
            }
        }

        if (IsFloorRenderer(renderer))
        {
            order = BattleWorldSorting.FloorOrder;
            return true;
        }

        if (IsLowerBasePart(objectName))
        {
            order = BattleWorldSorting.LowerBaseOrder;
            return true;
        }

        if (IsHandlePart(target))
        {
            order = BattleWorldSorting.HandleOrder;
            return true;
        }

        if (IsBasePart(objectName))
        {
            order = BattleWorldSorting.BaseOrder;
            return true;
        }

        return false;
    }

    private static bool IsLowerBasePart(string objectName)
    {
        return objectName.StartsWith("LowerPlate_", StringComparison.Ordinal) ||
               objectName.StartsWith("BaseLowerPlate_", StringComparison.Ordinal) ||
               objectName.StartsWith(ExitBasePrefix, StringComparison.Ordinal) ||
               objectName.IndexOf("LowerPlate", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsBasePart(string objectName)
    {
        return objectName.StartsWith("UpperPlate_", StringComparison.Ordinal) ||
               objectName.StartsWith("BaseUpperPlate_", StringComparison.Ordinal) ||
               objectName.IndexOf("UpperPlate", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsHandlePart(Transform source)
    {
        Transform cursor = source;
        while (cursor != null)
        {
            if (cursor.name.StartsWith("DockHandle_", StringComparison.Ordinal))
                return true;

            if (cursor.name.StartsWith("PresentationTemplate", StringComparison.Ordinal))
                break;

            cursor = cursor.parent;
        }

        return false;
    }

    private static bool IsFloorRenderer(SpriteRenderer renderer)
    {
        if (renderer == null)
            return false;

        string objectName = renderer.gameObject.name;
        if (objectName.StartsWith("Tile_", StringComparison.Ordinal) ||
            objectName.StartsWith("ShowTile_", StringComparison.Ordinal) ||
            objectName.StartsWith("BaseFloor_", StringComparison.Ordinal) ||
            objectName.StartsWith("DecorCarrierTile_", StringComparison.Ordinal))
        {
            return true;
        }

        return renderer.GetComponent<BattleWalkableField>() != null;
    }

    private static bool HasPresentationFloor(Transform root)
    {
        if (root == null)
            return false;

        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer != null && renderer.name.StartsWith("BaseFloor_", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsUnderPresentationTemplate(Transform source)
    {
        Transform cursor = source;
        while (cursor != null)
        {
            if (cursor.name.StartsWith("PresentationTemplate", StringComparison.Ordinal))
                return true;
            cursor = cursor.parent;
        }

        return false;
    }

    private static bool IsActiveRenderer(SpriteRenderer renderer)
    {
        return renderer != null &&
               renderer.enabled &&
               renderer.sprite != null &&
               renderer.gameObject.activeInHierarchy;
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

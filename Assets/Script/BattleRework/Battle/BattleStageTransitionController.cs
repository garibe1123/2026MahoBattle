using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전투 종료 -> Player 기준 4x4 확정 -> 나머지 Room Field 순차 퇴장 -> Show Stage 개방
/// 순서만 담당합니다.
///
/// Show의 TV/사회자/쇼 바닥 배치는 BattleShowWorldSetController가 담당하고,
/// 이 클래스는 그 컨트롤러에 확정된 4x4 중심만 전달합니다.
/// </summary>
[DefaultExecutionOrder(-15000)]
[DisallowMultipleComponent]
public sealed class BattleStageTransitionController : MonoBehaviour
{
    private enum CollapseExitSide
    {
        Left,
        Right,
        Down,
        Up
    }

    private sealed class CollapseExitPlan
    {
        public MapBlock block;
        public Vector2 direction;
        public float outwardDistance;
    }

    [Header("Persistent Base")]
    [SerializeField] private int persistentBaseFloorSorting = -19;

    [Header("Combat Clear Collapse")]
    [Tooltip("4x4 밖 Room Block이 바깥쪽부터 다음 묶음으로 빠져나가기 시작하는 간격입니다.")]
    [SerializeField, Min(0f)] private float collapseExitStagger = 0.10f;
    [Tooltip("마지막 Block 퇴장 뒤 Show Floor를 열기 전 짧은 간격입니다.")]
    [SerializeField, Min(0f)] private float showOpenDelayAfterCollapse = 0.08f;

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;
    private BattleShowWorldSetController showStage;

    private Vector3 preservedBaseTileOrigin;
    private Vector3 showAnchorCenter;
    private bool hasPreservedBaseOrigin;
    private bool hasShowAnchor;
    private bool preparedForIncomingNode;
    private bool selectionCollapsePrepared;
    private bool subscribed;
    private bool showStageGateHeld;

    private Coroutine bindRoutine;
    private Coroutine collapseRoutine;

    public bool HasShowAnchor => hasShowAnchor;
    public Vector3 ShowAnchorCenter => showAnchorCenter;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleStageTransitionController>() != null)
            return;

        GameObject host = new("BattleStageTransitionRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleStageTransitionController>();
    }

    private void OnEnable()
    {
        if (bindRoutine == null)
            bindRoutine = StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
            StopCoroutine(bindRoutine);
        if (collapseRoutine != null)
            StopCoroutine(collapseRoutine);

        bindRoutine = null;
        collapseRoutine = null;
        ReleaseShowStageGate();
        Unsubscribe();
    }

    private IEnumerator BindWhenReady()
    {
        while (enabled)
        {
            ResolveSystems();
            if (runManager != null && roomManager != null && baseTemplate != null && player != null)
                break;
            yield return null;
        }

        if (!enabled)
        {
            bindRoutine = null;
            yield break;
        }

        Subscribe();
        baseTemplate.EnsurePersistentBase();
        CaptureCurrentBase();
        CaptureShowAnchorFromBase();
        EnsureBaseVisible();
        EnsurePlayerVisible();
        bindRoutine = null;
    }

    private void ResolveSystems()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
        if (showStage == null)
            showStage = FindFirstObjectByType<BattleShowWorldSetController>();
    }

    private void Subscribe()
    {
        if (subscribed || runManager == null)
            return;

        runManager.StateChanged += HandleStateChanged;
        runManager.NodeEntered += HandleNodeEntered;
        runManager.RewardSelectionRequested += HandleRewardSelectionRequested;
        runManager.RunEnded += HandleRunEnded;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || runManager == null)
            return;

        runManager.StateChanged -= HandleStateChanged;
        runManager.NodeEntered -= HandleNodeEntered;
        runManager.RewardSelectionRequested -= HandleRewardSelectionRequested;
        runManager.RunEnded -= HandleRunEnded;
        subscribed = false;
    }

    private void HandleStateChanged(BattleRunState next)
    {
        ResolveSystems();

        if (next == BattleRunState.Reward)
        {
            EnsureBaseVisible();
            EnsurePlayerVisible();
            BeginSelectionCollapseIfNeeded();
            return;
        }

        if (next == BattleRunState.SelectingNode)
        {
            EnsureBaseVisible();
            EnsurePlayerVisible();

            if (roomManager != null && roomManager.IsRoomActive)
                BeginSelectionCollapseIfNeeded();
            else
            {
                CaptureShowAnchorFromBase();
                ReleaseShowStageGate();
            }
            return;
        }

        if (next != BattleRunState.EnteringNode)
            return;

        if (roomManager != null && roomManager.IsRoomActive && !preparedForIncomingNode)
            PromoteNextBaseAroundPlayer();

        if (!hasPreservedBaseOrigin)
            CaptureCurrentBase();

        EnsureBaseVisible();
        EnsurePlayerVisible();
        ApplyBaseOriginToRoomManager();
    }

    private void HandleRewardSelectionRequested(IReadOnlyList<BattleEquipmentSO> _)
    {
        BeginSelectionCollapseIfNeeded();
    }

    private void BeginSelectionCollapseIfNeeded()
    {
        ResolveSystems();

        if (selectionCollapsePrepared || collapseRoutine != null)
            return;

        if (roomManager == null || !roomManager.IsRoomActive || player == null || baseTemplate == null)
        {
            CaptureShowAnchorFromBase();
            ReleaseShowStageGate();
            return;
        }

        HoldShowStageGate();
        collapseRoutine = StartCoroutine(CollapseClearedRoomToPlayerBase());
    }

    private IEnumerator CollapseClearedRoomToPlayerBase()
    {
        // 1) Player가 실제로 설 수 있는 현재 Field 안에서 4x4를 확정합니다.
        baseTemplate.EnsurePersistentBase();
        Vector3 nextOrigin = FindBestFourByFourOrigin(player.transform.position);
        nextOrigin.z = baseTemplate.FixedTileOriginWorld.z;

        preservedBaseTileOrigin = baseTemplate.PromoteToNewBaseAtTileOrigin(nextOrigin);
        hasPreservedBaseOrigin = true;
        preparedForIncomingNode = true;
        selectionCollapsePrepared = true;

        EnsureBaseVisible();
        EnsurePlayerVisible();
        BattleShowPresentationManager.Instance?.RefreshPersistentBaseArt();
        ApplyBaseOriginToRoomManager();
        CaptureShowAnchorFromBase();
        BattleDockHandleVisibilityController.RefreshNow();

        // 2) 기존 Room Block을 4x4의 좌/우/아래/위 네 방향으로 분류합니다.
        // 각 방향은 바깥쪽 Block부터 빠지고, 한 Wave에서 각 방향 최대 1개씩 출발합니다.
        // 따라서 한 방향으로 몰려 겹치는 현상을 줄이고, 4x4를 가로질러 반대편으로 빠지는 경로도 만들지 않습니다.
        List<MapBlock> outgoingBlocks = CollectCurrentRoomWalkableBlocks();
        Vector2 baseCenter = showAnchorCenter;
        Bounds baseBounds = CreatePersistentBaseBounds(baseCenter);
        List<CollapseExitPlan>[] groups = BuildExitGroups(outgoingBlocks, baseBounds);

        int waveCount = 0;
        for (int side = 0; side < groups.Length; side++)
            waveCount = Mathf.Max(waveCount, groups[side].Count);

        float stagger = Mathf.Max(0f, collapseExitStagger);
        float lastExitDuration = 0f;

        for (int wave = 0; wave < waveCount; wave++)
        {
            bool startedAny = false;

            for (int side = 0; side < groups.Length; side++)
            {
                List<CollapseExitPlan> group = groups[side];
                if (wave >= group.Count)
                    continue;

                CollapseExitPlan plan = group[wave];
                if (plan == null || plan.block == null)
                    continue;

                // 새 4x4와 겹쳐 있던 기존 Piece가 빠질 때 Base 위로 떠 보이지 않게
                // 기존 Piece 전체를 Base보다 뒤로 내린 후 이동시킵니다.
                PushOutgoingRenderersBehindBase(plan.block);
                DisableOutgoingWalkable(plan.block);
                plan.block.PlayExit(plan.direction);
                lastExitDuration = Mathf.Max(lastExitDuration, plan.block.ExitDuration);
                startedAny = true;
            }

            if (startedAny && stagger > 0f && wave < waveCount - 1)
                yield return new WaitForSecondsRealtime(stagger);
        }

        if (lastExitDuration > 0f)
            yield return new WaitForSecondsRealtime(lastExitDuration);

        for (int i = 0; i < outgoingBlocks.Count; i++)
        {
            MapBlock block = outgoingBlocks[i];
            if (block != null)
                Destroy(block.gameObject);
        }

        BattleDockHandleVisibilityController.RefreshNow();

        if (showOpenDelayAfterCollapse > 0f)
            yield return new WaitForSecondsRealtime(showOpenDelayAfterCollapse);

        collapseRoutine = null;

        // 3) 4x4 기준점이 확정된 뒤에만 Show를 엽니다.
        ReleaseShowStageGate();
    }

    private List<MapBlock> CollectCurrentRoomWalkableBlocks()
    {
        MapBlock[] blocks = FindObjectsByType<MapBlock>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        List<MapBlock> result = new();

        for (int i = 0; i < blocks.Length; i++)
        {
            MapBlock block = blocks[i];
            if (block == null || !block.gameObject.activeInHierarchy)
                continue;
            if (!block.ContributesWalkableNavMesh)
                continue;

            string blockName = block.name;
            bool rawPrototype =
                blockName.StartsWith("__RuntimeRoomPiecePrototype_", System.StringComparison.Ordinal) &&
                !blockName.EndsWith("(Clone)", System.StringComparison.Ordinal);
            if (rawPrototype || blockName.StartsWith("Outgoing_", System.StringComparison.Ordinal))
                continue;

            if (showStage != null && block.transform.IsChildOf(showStage.transform))
                continue;

            result.Add(block);
        }

        return result;
    }

    private Bounds CreatePersistentBaseBounds(Vector2 baseCenter)
    {
        float size = RoomBaseTemplate.FixedBaseTiles * RoomBaseTemplate.TileWorldSize;
        return new Bounds(
            new Vector3(baseCenter.x, baseCenter.y, 0f),
            new Vector3(size, size, 0.1f));
    }

    private List<CollapseExitPlan>[] BuildExitGroups(List<MapBlock> blocks, Bounds baseBounds)
    {
        List<CollapseExitPlan>[] groups =
        {
            new List<CollapseExitPlan>(), // Left
            new List<CollapseExitPlan>(), // Right
            new List<CollapseExitPlan>(), // Down
            new List<CollapseExitPlan>()  // Up
        };

        for (int i = 0; i < blocks.Count; i++)
        {
            MapBlock block = blocks[i];
            if (block == null)
                continue;

            Bounds blockBounds = ResolveBlockBounds(block);
            CollapseExitSide side = ResolveExitSide(blockBounds, baseBounds);
            Vector2 direction = DirectionFor(side);

            groups[(int)side].Add(new CollapseExitPlan
            {
                block = block,
                direction = direction,
                outwardDistance = ResolveOutwardDistance(blockBounds, baseBounds, side)
            });
        }

        // 같은 방향 안에서는 가장 바깥쪽 Block부터 먼저 보내야 뒤 Block이 앞 Block을 추월하지 않습니다.
        for (int i = 0; i < groups.Length; i++)
            groups[i].Sort((a, b) => b.outwardDistance.CompareTo(a.outwardDistance));

        return groups;
    }

    private static CollapseExitSide ResolveExitSide(Bounds blockBounds, Bounds baseBounds)
    {
        const float epsilon = 0.02f;

        bool fullyLeft = blockBounds.max.x <= baseBounds.min.x + epsilon;
        bool fullyRight = blockBounds.min.x >= baseBounds.max.x - epsilon;
        bool fullyDown = blockBounds.max.y <= baseBounds.min.y + epsilon;
        bool fullyUp = blockBounds.min.y >= baseBounds.max.y - epsilon;

        // 이미 Base 바깥에 있는 Block은 절대로 Base 반대편으로 보내지 않습니다.
        if (fullyLeft || fullyRight || fullyDown || fullyUp)
        {
            float bestGap = float.NegativeInfinity;
            CollapseExitSide bestSide = CollapseExitSide.Down;

            if (fullyLeft)
                SelectIfGreater(baseBounds.min.x - blockBounds.max.x, CollapseExitSide.Left, ref bestGap, ref bestSide);
            if (fullyRight)
                SelectIfGreater(blockBounds.min.x - baseBounds.max.x, CollapseExitSide.Right, ref bestGap, ref bestSide);
            if (fullyDown)
                SelectIfGreater(baseBounds.min.y - blockBounds.max.y, CollapseExitSide.Down, ref bestGap, ref bestSide);
            if (fullyUp)
                SelectIfGreater(blockBounds.min.y - baseBounds.max.y, CollapseExitSide.Up, ref bestGap, ref bestSide);

            return bestSide;
        }

        // 새 Persistent Base와 겹쳐 있는 기존 Piece는 가장 가까운 Base 외곽면으로 빠집니다.
        // 이 경우 Renderer를 Base 뒤로 내리므로 이동 중에도 4x4 위를 가로지르는 것처럼 보이지 않습니다.
        float moveLeft = Mathf.Max(0f, blockBounds.max.x - baseBounds.min.x);
        float moveRight = Mathf.Max(0f, baseBounds.max.x - blockBounds.min.x);
        float moveDown = Mathf.Max(0f, blockBounds.max.y - baseBounds.min.y);
        float moveUp = Mathf.Max(0f, baseBounds.max.y - blockBounds.min.y);

        float bestMove = moveLeft;
        CollapseExitSide result = CollapseExitSide.Left;

        SelectIfSmaller(moveRight, CollapseExitSide.Right, ref bestMove, ref result);
        SelectIfSmaller(moveDown, CollapseExitSide.Down, ref bestMove, ref result);
        SelectIfSmaller(moveUp, CollapseExitSide.Up, ref bestMove, ref result);
        return result;
    }

    private static void SelectIfGreater(
        float value,
        CollapseExitSide side,
        ref float bestValue,
        ref CollapseExitSide bestSide)
    {
        if (value <= bestValue)
            return;
        bestValue = value;
        bestSide = side;
    }

    private static void SelectIfSmaller(
        float value,
        CollapseExitSide side,
        ref float bestValue,
        ref CollapseExitSide bestSide)
    {
        if (value >= bestValue)
            return;
        bestValue = value;
        bestSide = side;
    }

    private static Vector2 DirectionFor(CollapseExitSide side)
    {
        switch (side)
        {
            case CollapseExitSide.Left: return Vector2.left;
            case CollapseExitSide.Right: return Vector2.right;
            case CollapseExitSide.Up: return Vector2.up;
            default: return Vector2.down;
        }
    }

    private static float ResolveOutwardDistance(Bounds blockBounds, Bounds baseBounds, CollapseExitSide side)
    {
        switch (side)
        {
            case CollapseExitSide.Left:
                return Mathf.Max(0f, baseBounds.min.x - blockBounds.center.x);
            case CollapseExitSide.Right:
                return Mathf.Max(0f, blockBounds.center.x - baseBounds.max.x);
            case CollapseExitSide.Up:
                return Mathf.Max(0f, blockBounds.center.y - baseBounds.max.y);
            default:
                return Mathf.Max(0f, baseBounds.min.y - blockBounds.center.y);
        }
    }

    private static Bounds ResolveBlockBounds(MapBlock block)
    {
        bool found = false;
        Bounds bounds = default;

        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (found)
            return bounds;

        Collider2D[] colliders = block.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.enabled)
                continue;

            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (found)
            return bounds;

        return new Bounds(block.transform.position, MapBlock.BlockWorldSize);
    }

    private void PushOutgoingRenderersBehindBase(MapBlock block)
    {
        if (block == null)
            return;

        SpriteRenderer[] renderers = block.GetComponentsInChildren<SpriteRenderer>(true);
        if (renderers.Length == 0)
            return;

        int highest = int.MinValue;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                highest = Mathf.Max(highest, renderers[i].sortingOrder);
        }

        if (highest == int.MinValue)
            return;

        int targetHighest = persistentBaseFloorSorting - 2;
        int shift = targetHighest - highest;
        if (shift >= 0)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].sortingOrder += shift;
        }
    }

    private static void DisableOutgoingWalkable(MapBlock block)
    {
        if (block == null)
            return;

        BattleWalkableField[] fields = block.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i] != null)
                fields[i].enabled = false;
        }
    }

    private void HoldShowStageGate()
    {
        ResolveSystems();
        showStageGateHeld = true;
        showStage?.SetExternalGate(true);
    }

    private void ReleaseShowStageGate()
    {
        ResolveSystems();

        if (hasShowAnchor)
            showStage?.SetStageAnchor(showAnchorCenter);

        showStageGateHeld = false;
        showStage?.SetExternalGate(false);
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node == null || node.room == null)
            return;
        if (node.type != BattleNodeType.Combat && node.type != BattleNodeType.Elite)
            return;

        ResolveSystems();
        if (baseTemplate == null)
            return;

        if (!hasPreservedBaseOrigin)
        {
            baseTemplate.EnsurePersistentBase();
            CaptureCurrentBase();
        }

        if (hasPreservedBaseOrigin)
            baseTemplate.EnsureVisibleAtTileOrigin(preservedBaseTileOrigin, node.room);

        ApplyBaseOriginToRoomManager();
        EnsureBaseVisible();
        EnsurePlayerVisible();
        BattleDockHandleVisibilityController.RefreshNow();

        node.room.repositionPlayerOnEnter = false;
        preparedForIncomingNode = false;
        selectionCollapsePrepared = false;
    }

    private void HandleRunEnded(RunEndReason _)
    {
        if (collapseRoutine != null)
        {
            StopCoroutine(collapseRoutine);
            collapseRoutine = null;
        }

        ResolveSystems();
        EnsureBaseVisible();
        EnsurePlayerVisible();
        preparedForIncomingNode = false;
        selectionCollapsePrepared = false;
        ReleaseShowStageGate();
    }

    private void PromoteNextBaseAroundPlayer()
    {
        if (baseTemplate == null || player == null)
            return;

        baseTemplate.EnsurePersistentBase();

        Vector3 nextOrigin = FindBestFourByFourOrigin(player.transform.position);
        nextOrigin.z = baseTemplate.FixedTileOriginWorld.z;

        preservedBaseTileOrigin = baseTemplate.PromoteToNewBaseAtTileOrigin(nextOrigin);
        hasPreservedBaseOrigin = true;
        preparedForIncomingNode = true;

        EnsureBaseVisible();
        BattleShowPresentationManager.Instance?.RefreshPersistentBaseArt();
        ApplyBaseOriginToRoomManager();
        CaptureShowAnchorFromBase();
        BattleDockHandleVisibilityController.RefreshNow();
    }

    private void CaptureCurrentBase()
    {
        if (baseTemplate == null)
            return;

        baseTemplate.EnsurePersistentBase();
        if (!baseTemplate.HasPersistentBase)
            return;

        preservedBaseTileOrigin = baseTemplate.FixedTileOriginWorld;
        hasPreservedBaseOrigin = true;
    }

    private void CaptureShowAnchorFromBase()
    {
        if (baseTemplate == null || !baseTemplate.HasPersistentBase)
            return;

        showAnchorCenter = baseTemplate.FixedCenterWorld;
        hasShowAnchor = true;
    }

    private void ApplyBaseOriginToRoomManager()
    {
        if (!hasPreservedBaseOrigin || roomManager == null || roomManager.RoomOrigin == null)
            return;

        Vector3 position = preservedBaseTileOrigin;
        position.z = roomManager.RoomOrigin.position.z;
        roomManager.RoomOrigin.position = position;
    }

    private void EnsureBaseVisible()
    {
        if (baseTemplate == null)
            return;

        baseTemplate.EnsurePersistentBase();
        GameObject baseObject = baseTemplate.ActiveBase;
        if (baseObject == null)
            return;

        if (!baseObject.activeSelf)
            baseObject.SetActive(true);

        SpriteRenderer[] renderers = baseObject.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null)
                continue;
            renderer.enabled = true;
            if (renderer.sortingOrder < persistentBaseFloorSorting)
                renderer.sortingOrder = persistentBaseFloorSorting;
        }

        Collider2D[] colliders = baseObject.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null && colliders[i].isTrigger)
                colliders[i].enabled = true;

        BattleWalkableField[] fields = baseObject.GetComponentsInChildren<BattleWalkableField>(true);
        for (int i = 0; i < fields.Length; i++)
            if (fields[i] != null)
                fields[i].enabled = true;
    }

    private void EnsurePlayerVisible()
    {
        if (player == null)
            return;

        if (!player.gameObject.activeSelf)
            player.gameObject.SetActive(true);

        SpriteRenderer[] renderers = player.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                renderers[i].enabled = true;
    }

    private static Vector3 FindBestFourByFourOrigin(Vector3 playerWorld)
    {
        float tileSize = RoomBaseTemplate.TileWorldSize;
        Vector2Int playerTile = WorldToTile(playerWorld);

        Vector2Int bestOrigin = new(playerTile.x - 1, playerTile.y - 1);
        int bestCoverage = -1;
        float bestDistance = float.MaxValue;

        for (int oy = playerTile.y - (RoomBaseTemplate.FixedBaseTiles - 1); oy <= playerTile.y; oy++)
        {
            for (int ox = playerTile.x - (RoomBaseTemplate.FixedBaseTiles - 1); ox <= playerTile.x; ox++)
            {
                int coverage = 0;
                for (int y = 0; y < RoomBaseTemplate.FixedBaseTiles; y++)
                {
                    for (int x = 0; x < RoomBaseTemplate.FixedBaseTiles; x++)
                    {
                        Vector2 probe = new((ox + x) * tileSize, (oy + y) * tileSize);
                        if (BattleWalkableField.HasSupport(probe))
                            coverage++;
                    }
                }

                Vector2 candidateCenter = new(
                    (ox + (RoomBaseTemplate.FixedBaseTiles - 1) * 0.5f) * tileSize,
                    (oy + (RoomBaseTemplate.FixedBaseTiles - 1) * 0.5f) * tileSize);
                float distance = ((Vector2)playerWorld - candidateCenter).sqrMagnitude;

                if (coverage > bestCoverage ||
                    coverage == bestCoverage && distance < bestDistance)
                {
                    bestCoverage = coverage;
                    bestDistance = distance;
                    bestOrigin = new Vector2Int(ox, oy);
                }
            }
        }

        return new Vector3(bestOrigin.x * tileSize, bestOrigin.y * tileSize, playerWorld.z);
    }

    private static Vector2Int WorldToTile(Vector3 world)
    {
        float tileSize = Mathf.Max(0.0001f, RoomBaseTemplate.TileWorldSize);
        return new Vector2Int(
            Mathf.RoundToInt(world.x / tileSize),
            Mathf.RoundToInt(world.y / tileSize));
    }
}

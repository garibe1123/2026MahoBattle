using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전투 스테이지 전환의 공간 순서를 담당합니다.
///
/// 전투 클리어 시:
/// 1) Player가 서 있는 현재 Field에서 가장 적합한 4x4를 확정합니다.
/// 2) 그 위치에 Persistent Base를 먼저 승격해 Player 발밑을 보존합니다.
/// 3) 기존 Room의 나머지 walkable MapBlock을 바깥 레일 방향으로 퇴장시킵니다.
/// 4) 퇴장이 끝난 뒤 BattleShowWorldSetController를 다시 활성화합니다.
///
/// 따라서 Reward / Map TV와 Show Camera는 정리 전의 거대한 Field가 아니라
/// 최종 4x4 Base를 기준으로 위치를 잡습니다.
/// </summary>
[DefaultExecutionOrder(-15000)]
[DisallowMultipleComponent]
public sealed class BattleStageTransitionController : MonoBehaviour
{
    [Header("Persistent Base")]
    [SerializeField] private int persistentBaseFloorSorting = -19;

    [Header("Combat Clear Collapse")]
    [Tooltip("마지막 Room Block이 퇴장한 뒤 쇼 세트를 열기 전 추가 여유 시간입니다.")]
    [SerializeField, Min(0f)] private float showOpenDelayAfterCollapse = 0.08f;

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;
    private BattleShowWorldSetController showStage;

    private Vector3 preservedBaseTileOrigin;
    private bool hasPreservedBaseOrigin;
    private bool preparedForIncomingNode;
    private bool selectionCollapsePrepared;
    private bool subscribed;
    private bool showStageGateHeld;

    private Coroutine bindRoutine;
    private Coroutine collapseRoutine;

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

            // 보상이 0개인 Room은 Reward 상태를 거치지 않고 바로 맵 선택으로 갈 수 있습니다.
            // 그 경우에도 동일하게 전장을 4x4로 접은 뒤 쇼를 엽니다.
            if (roomManager != null && roomManager.IsRoomActive)
                BeginSelectionCollapseIfNeeded();
            else
                ReleaseShowStageGate();
            return;
        }

        if (next != BattleRunState.EnteringNode)
            return;

        // 정상적인 전투 클리어 경로에서는 Reward 진입 시 이미 4x4가 준비되어 있습니다.
        // 비정상/레거시 진입만 여기서 한 번 보정합니다.
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

        // 최초 Start Area의 Map Select에는 정리할 Combat Room이 없습니다.
        if (roomManager == null || !roomManager.IsRoomActive || player == null || baseTemplate == null)
        {
            ReleaseShowStageGate();
            return;
        }

        HoldShowStageGate();
        collapseRoutine = StartCoroutine(CollapseClearedRoomToPlayerBase());
    }

    private IEnumerator CollapseClearedRoomToPlayerBase()
    {
        // 1. Player가 실제로 서 있는 Field를 기준으로 최적의 4x4를 먼저 확정합니다.
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

        // 2. Persistent Base는 MapBlock이 아니므로 그대로 남습니다.
        // 현재 씬의 실제 walkable MapBlock만 수집해 전부 바깥으로 퇴장시키면
        // 결과적으로 Player 발밑의 새 4x4만 남습니다.
        List<MapBlock> outgoingBlocks = CollectCurrentRoomWalkableBlocks();
        Vector2 baseCenter = baseTemplate.FixedCenterWorld;
        float longestExit = 0f;

        for (int i = 0; i < outgoingBlocks.Count; i++)
        {
            MapBlock block = outgoingBlocks[i];
            if (block == null)
                continue;

            Vector2 direction = ResolveOutwardCardinal(block.transform.position, baseCenter);
            block.PlayExit(direction);
            longestExit = Mathf.Max(longestExit, block.ExitDuration);
        }

        if (longestExit > 0f)
            yield return new WaitForSecondsRealtime(longestExit);

        // BattleRoomManager의 activeBlocks에는 Destroy된 Unity null이 잠시 남을 수 있지만,
        // 다음 EnterRoom의 ClearImmediate에서 안전하게 정리됩니다.
        for (int i = 0; i < outgoingBlocks.Count; i++)
        {
            MapBlock block = outgoingBlocks[i];
            if (block != null)
                Destroy(block.gameObject);
        }

        if (showOpenDelayAfterCollapse > 0f)
            yield return new WaitForSecondsRealtime(showOpenDelayAfterCollapse);

        collapseRoutine = null;

        // 3. 이제 WorldSet이 Field Bounds를 읽어도 살아있는 Walkable Field는 새 4x4 Base뿐입니다.
        // 이 시점부터 TV / Presenter / Show Camera가 같은 4x4 기준으로 등장합니다.
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

            // BattleShowSharedStage의 MapBlock은 walkable=false지만,
            // 혹시 설정이 바뀌어도 전투 Field 정리에 섞이지 않도록 명시적으로 제외합니다.
            if (showStage != null && block.transform.IsChildOf(showStage.transform))
                continue;

            result.Add(block);
        }

        return result;
    }

    private void HoldShowStageGate()
    {
        ResolveSystems();
        if (showStage == null || showStageGateHeld)
            return;

        showStageGateHeld = true;
        if (showStage.enabled)
            showStage.enabled = false;
    }

    private void ReleaseShowStageGate()
    {
        if (!showStageGateHeld)
            return;

        showStageGateHeld = false;
        ResolveSystems();
        if (showStage != null && !showStage.enabled)
            showStage.enabled = true;
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
    }

    /// <summary>
    /// Player를 포함할 수 있는 4x4 후보를 검사해 현재 Walkable Field와 가장 많이 겹치는 후보를 고릅니다.
    /// 동률이면 Player가 4x4 중심에 더 가까운 후보를 사용합니다.
    /// </summary>
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

    private static Vector2 ResolveOutwardCardinal(Vector3 blockWorld, Vector2 baseCenter)
    {
        Vector2 delta = (Vector2)blockWorld - baseCenter;
        if (delta.sqrMagnitude <= 0.0001f)
            return Vector2.down;

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            return delta.x >= 0f ? Vector2.right : Vector2.left;

        return delta.y >= 0f ? Vector2.up : Vector2.down;
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

    private static Vector2Int WorldToTile(Vector3 world)
    {
        float tileSize = Mathf.Max(0.0001f, RoomBaseTemplate.TileWorldSize);
        return new Vector2Int(
            Mathf.RoundToInt(world.x / tileSize),
            Mathf.RoundToInt(world.y / tileSize));
    }
}

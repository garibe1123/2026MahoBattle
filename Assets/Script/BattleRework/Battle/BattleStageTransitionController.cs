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
    [Header("Persistent Base")]
    [SerializeField] private int persistentBaseFloorSorting = -19;

    [Header("Combat Clear Collapse")]
    [Tooltip("4x4 밖 Room Block이 한 장씩 빠져나가기 시작하는 간격입니다.")]
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

            // 보상이 없는 Room도 같은 전환을 거칩니다.
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

        // 정상 경로에서는 Reward 진입 때 이미 준비되어 있습니다.
        // 레거시/예외 경로만 여기서 즉시 보정합니다.
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

        // 2) 새 Persistent Base는 MapBlock이 아니므로 남습니다.
        // 기존 Room의 walkable MapBlock만 바깥쪽부터 한 장씩 퇴장시킵니다.
        List<MapBlock> outgoingBlocks = CollectCurrentRoomWalkableBlocks();
        Vector2 baseCenter = showAnchorCenter;
        outgoingBlocks.Sort((a, b) =>
        {
            float da = a == null ? -1f : ((Vector2)a.transform.position - baseCenter).sqrMagnitude;
            float db = b == null ? -1f : ((Vector2)b.transform.position - baseCenter).sqrMagnitude;
            return db.CompareTo(da);
        });

        float stagger = Mathf.Max(0f, collapseExitStagger);
        float lastExitDuration = 0f;

        for (int i = 0; i < outgoingBlocks.Count; i++)
        {
            MapBlock block = outgoingBlocks[i];
            if (block == null)
                continue;

            Vector2 direction = ResolveOutwardCardinal(block.transform.position, baseCenter);
            block.PlayExit(direction);
            lastExitDuration = Mathf.Max(lastExitDuration, block.ExitDuration);

            if (stagger > 0f && i < outgoingBlocks.Count - 1)
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

    private static Vector2 ResolveOutwardCardinal(Vector3 blockWorld, Vector2 baseCenter)
    {
        Vector2 delta = (Vector2)blockWorld - baseCenter;
        if (delta.sqrMagnitude <= 0.0001f)
            return Vector2.down;

        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            return delta.x >= 0f ? Vector2.right : Vector2.left;

        return delta.y >= 0f ? Vector2.up : Vector2.down;
    }

    private static Vector2Int WorldToTile(Vector3 world)
    {
        float tileSize = Mathf.Max(0.0001f, RoomBaseTemplate.TileWorldSize);
        return new Vector2Int(
            Mathf.RoundToInt(world.x / tileSize),
            Mathf.RoundToInt(world.y / tileSize));
    }
}

using System.Collections;
using UnityEngine;

/// <summary>
/// 전투 스테이지 사이에서 Persistent 4x4 Base만 관리합니다.
///
/// 규칙:
/// - 전투 클리어 -> Reward -> Map 선택 동안 현재 Field와 실제 Player를 절대 숨기거나 교체하지 않습니다.
/// - 다음 노드를 실제로 선택해 EnteringNode로 넘어갈 때만 Player 근처의 4x4 영역을 다음 Base로 승격합니다.
/// - Reward 전용 임시 바닥, Exit Ghost, Reward 시점의 Field Collapse는 사용하지 않습니다.
/// - Run 종료 시에도 이 컨트롤러는 Field/Player를 제거하지 않습니다.
///
/// 실제 Room extension의 생성/정리는 BattleRoomManager가 담당하고,
/// 이 클래스는 다음 Room에 넘길 Persistent Base 위치만 결정합니다.
/// </summary>
[DefaultExecutionOrder(-15000)]
[DisallowMultipleComponent]
public sealed class BattleStageTransitionController : MonoBehaviour
{
    [Header("Persistent Base")]
    [SerializeField] private int persistentBaseFloorSorting = -19;

    private BattleRunManager runManager;
    private BattleRoomManager roomManager;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;

    private Vector3 preservedBaseTileOrigin;
    private bool hasPreservedBaseOrigin;
    private bool preparedForIncomingNode;
    private bool subscribed;
    private Coroutine bindRoutine;

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
        bindRoutine = null;
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
    }

    private void Subscribe()
    {
        if (subscribed || runManager == null)
            return;

        runManager.StateChanged += HandleStateChanged;
        runManager.NodeEntered += HandleNodeEntered;
        runManager.RunEnded += HandleRunEnded;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || runManager == null)
            return;

        runManager.StateChanged -= HandleStateChanged;
        runManager.NodeEntered -= HandleNodeEntered;
        runManager.RunEnded -= HandleRunEnded;
        subscribed = false;
    }

    private void HandleStateChanged(BattleRunState next)
    {
        ResolveSystems();

        // Reward와 Map 선택은 현재 전장을 그대로 사용하는 쇼 상태입니다.
        // 여기서는 어떤 Field도 숨기거나 파괴하거나 새 임시 바닥으로 교체하지 않습니다.
        if (next == BattleRunState.Reward || next == BattleRunState.SelectingNode)
        {
            EnsureBaseVisible();
            EnsurePlayerVisible();
            return;
        }

        if (next != BattleRunState.EnteringNode)
            return;

        // 실제 다음 노드로 넘어가는 순간에만 다음 4x4 Base를 결정합니다.
        // 최초 Start Area -> 첫 노드에서는 아직 활성 Room이 없으므로 기존 Base를 그대로 사용합니다.
        if (roomManager != null && roomManager.IsRoomActive && !preparedForIncomingNode)
            PromoteNextBaseAroundPlayer();

        if (!hasPreservedBaseOrigin)
            CaptureCurrentBase();

        EnsureBaseVisible();
        EnsurePlayerVisible();
        ApplyBaseOriginToRoomManager();
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

        // Player는 현재 월드 위치를 유지합니다. Room SO가 다시 강제 재배치하지 않게 합니다.
        node.room.repositionPlayerOnEnter = false;
        preparedForIncomingNode = false;
    }

    private void HandleRunEnded(RunEndReason _)
    {
        // 결과 화면/다음 Scene이 처리되기 전까지 마지막 전장을 그대로 보여줍니다.
        // 이 클래스에서는 절대로 Player나 Persistent Base를 끄지 않습니다.
        ResolveSystems();
        EnsureBaseVisible();
        EnsurePlayerVisible();
        preparedForIncomingNode = false;
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
    /// Player를 포함할 수 있는 4x4 후보 16개를 검사해 현재 Walkable Field와 가장 많이 겹치는 후보를 고릅니다.
    /// 완전한 4x4가 있으면 그중 Player가 중앙에 가까운 후보를 우선합니다.
    /// 별도의 Room 내부 리스트/Reflection에 의존하지 않으므로 전환 코드가 Field 구현에 덜 결합됩니다.
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

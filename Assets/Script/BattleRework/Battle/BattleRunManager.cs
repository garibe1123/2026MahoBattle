using System;
using System.Collections.Generic;
using UnityEngine;

public enum RunEndReason
{
    Clear,
    Death,
    Quit
}

public enum BattleRunState
{
    None,
    EnteringNode,
    BuildingRoom,
    Combat,
    Reward,
    ExitingRoom,
    SelectingNode,
    NonCombat,
    Ended
}

/// <summary>
/// 한 런의 최상위 Flow를 관리합니다.
///
/// 핵심 규칙:
/// - Start Area는 Room이 아닙니다.
/// - Start Area에서는 BattleRoomManager.EnterRoom을 절대 호출하지 않습니다.
/// - Start Area에는 영구 4x4 Start Base + Player만 존재합니다.
/// - Start Area에는 Monster / Obstacle / Room Wall / Incoming Block / Reward가 없습니다.
/// - Start Area 출구를 밟은 뒤에야 NodeGraph의 첫 실제 Room을 조립합니다.
/// - 실제 Room의 4x4 바닥은 Start Base와 별개의 Room Floor이므로 다시 전부 조립됩니다.
///
/// 이후 Node 진입 -> Room 조립 -> Combat -> Reward -> Exit -> 다음 Node 선택을
/// 상태 머신으로 관리합니다.
/// </summary>
public class BattleRunManager : MonoBehaviour
{
    [Header("Run Definition")]
    [SerializeField] private NodeGraphSO nodeGraph;
    [SerializeField] private ClanDefinitionSO clan;
    [SerializeField] private ShootingThemeSO shootingTheme;

    [Header("Systems")]
    [SerializeField] private BattleSceneManager sceneManager;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private RunProgressSystem progress;
    [SerializeField] private BattleRewardSystem rewardSystem;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private FanMissionSystem fanMissionSystem;
    [SerializeField] private PlayerController playerController;

    [Header("Empty Start Area")]
    [Tooltip("첫 Gameplay Room 앞에 전투가 전혀 없는 4x4 Start Area를 둡니다.")]
    [SerializeField] private bool useEmptyStartArea = true;
    [Tooltip("Start Base 밖으로 빠지는 것을 막는 보이지 않는 Collider 두께입니다.")]
    [SerializeField, Min(0.05f)] private float startBoundaryThickness = 0.28f;
    [Tooltip("Start Area에서 첫 Room으로 넘어가는 보이지 않는 출구 폭입니다.")]
    [SerializeField, Min(0.5f)] private float startExitWidth = 2.2f;
    [Tooltip("첫 실제 Room이 Start Base 어느 방향에 붙을지 지정합니다. 기본은 오른쪽입니다.")]
    [SerializeField] private Vector2 firstRoomDirection = Vector2.right;

    [Header("Depth Scaling - inspector driven")]
    [SerializeField] private AnimationCurve hpByDepth = AnimationCurve.Linear(0f, 1f, 10f, 1f);
    [SerializeField] private AnimationCurve damageByDepth = AnimationCurve.Linear(0f, 1f, 10f, 1f);

    [Header("Elite Node provisional multiplier")]
    [SerializeField] private float eliteHpMultiplier = 1.5f;
    [SerializeField] private float eliteDamageMultiplier = 1.5f;

    private readonly List<BattleNodeData> nextNodeChoices = new();
    private readonly List<BattleEquipmentSO> currentRewardChoices = new();
    private readonly List<GameObject> startAreaObjects = new();

    private BattleNodeData currentNode;
    private BattleNodeData pendingFirstGameplayNode;
    private BattleContext currentContext;
    private BattleRunState state = BattleRunState.None;
    private bool runActive;
    private bool roomStartedWithMonsters;
    private bool startAreaActive;
    private bool transitioningFromStartArea;
    private bool startOriginCaptured;
    private Vector3 startRoomOriginPosition;
    private RunEndReason? lastEndReason;

    public BattleNodeData CurrentNode => currentNode;
    public BattleContext CurrentContext => currentContext;
    public BattleRunState State => state;
    public bool RunActive => runActive;
    public bool WaitingForNodeSelection => state == BattleRunState.SelectingNode;
    public IReadOnlyList<BattleNodeData> NextNodeChoices => nextNodeChoices;
    public IReadOnlyList<BattleEquipmentSO> CurrentRewardChoices => currentRewardChoices;
    public ShootingThemeSO ShootingTheme => shootingTheme;
    public ClanDefinitionSO Clan => clan;
    public RunProgressSystem Progress => progress;
    public RunEndReason? LastEndReason => lastEndReason;
    public BattleSceneManager SceneManager => sceneManager;
    public bool IsInStartArea => startAreaActive;

    public event Action<BattleRunState> StateChanged;
    public event Action<BattleNodeData> NodeEntered;
    public event Action<IReadOnlyList<BattleNodeData>> NextNodeSelectionRequested;
    public event Action<BattleNodeData> NonCombatNodeEntered;
    public event Action<IReadOnlyList<BattleEquipmentSO>> RewardSelectionRequested;
    public event Action<BattleEquipmentSO> RewardSelected;
    public event Action<RunEndReason> RunEnded;

    private void Awake()
    {
        if (sceneManager == null)
            sceneManager = FindFirstObjectByType<BattleSceneManager>();

        if (fanMissionSystem == null)
            fanMissionSystem = FindFirstObjectByType<FanMissionSystem>();

        CaptureStartRoomOrigin();
    }

    private void OnEnable()
    {
        if (roomManager != null)
        {
            roomManager.RoomCombatStarted += HandleRoomCombatStarted;
            roomManager.RoomCombatCleared += HandleRoomCombatCleared;
            roomManager.RoomExited += HandleRoomExited;
            roomManager.MonsterDefeated += HandleMonsterDefeated;
        }

        if (playerController != null)
            playerController.Died += HandlePlayerDeath;
    }

    private void OnDisable()
    {
        if (roomManager != null)
        {
            roomManager.RoomCombatStarted -= HandleRoomCombatStarted;
            roomManager.RoomCombatCleared -= HandleRoomCombatCleared;
            roomManager.RoomExited -= HandleRoomExited;
            roomManager.MonsterDefeated -= HandleMonsterDefeated;
        }

        if (playerController != null)
            playerController.Died -= HandlePlayerDeath;

        ClearStartAreaObjects();
    }

    public bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();

        if (sceneManager == null)
            sceneManager = FindFirstObjectByType<BattleSceneManager>();

        if (sceneManager == null)
        {
            errors.Add("BattleSceneManager is missing. Add BattleSceneManager to BattleSystems and run Install / Repair Battle Scene.");
        }
        else if (!sceneManager.ValidateStartGate(out string sceneReport))
        {
            errors.Add($"BattleSceneManager start gate failed:\n{sceneReport}");
        }

        if (nodeGraph == null)
            errors.Add("nodeGraph is null");
        else if (!nodeGraph.ValidateGraph(out string graphReport))
            errors.Add($"NodeGraph invalid:\n{graphReport}");

        if (roomManager == null)
            errors.Add("roomManager is null");
        else if (!roomManager.ValidateConfiguration(out string roomReport))
            errors.Add($"BattleRoomManager invalid:\n{roomReport}");

        if (progress == null)
            errors.Add("progress is null");

        if (rewardSystem == null)
            errors.Add("rewardSystem is null");
        else if (!rewardSystem.ValidateConfiguration(out string rewardReport))
            errors.Add($"BattleRewardSystem invalid:\n{rewardReport}");

        if (equipmentSystem == null)
            errors.Add("equipmentSystem is null");
        else if (!equipmentSystem.ValidateConfiguration(out string equipmentReport))
            errors.Add($"BattleEquipmentSystem invalid:\n{equipmentReport}");

        if (playerController == null)
            errors.Add("playerController is null");

        report = string.Join("\n", errors);
        return errors.Count == 0;
    }

    public bool SetShootingThemeForNextRun(ShootingThemeSO nextTheme)
    {
        if (runActive)
            return false;

        shootingTheme = nextTheme;
        return true;
    }

    public bool SetClanForNextRun(ClanDefinitionSO nextClan)
    {
        if (runActive)
            return false;

        clan = nextClan;
        return true;
    }

    public void StartRun()
    {
        if (!ValidateConfiguration(out string report))
        {
            Debug.LogError($"[BattleRun] START BLOCKED. Required battle scene setup is incomplete.\n{report}");
            return;
        }

        if (Time.timeScale <= 0f)
            Time.timeScale = 1f;

        if (runActive || roomManager.IsRoomActive)
            roomManager.AbortRoom();

        CaptureStartRoomOrigin();
        RestoreStartRoomOrigin();
        ClearStartAreaObjects();

        BattleNodeData start = nodeGraph.GetStartNode();
        if (start == null)
        {
            Debug.LogError("[BattleRun] Start node could not be resolved.");
            return;
        }

        currentRewardChoices.Clear();
        nextNodeChoices.Clear();
        currentNode = null;
        currentContext = null;
        pendingFirstGameplayNode = null;
        roomStartedWithMonsters = false;
        startAreaActive = false;
        transitioningFromStartArea = false;
        lastEndReason = null;

        equipmentSystem.ResetForRun();
        fanMissionSystem?.ResetForRun();
        playerController.ResetForRun();
        progress.BeginRun();

        runActive = true;

        if (useEmptyStartArea && CanUseNodeRoomAsStartBase(start))
            EnterEmptyStartArea(start);
        else
            EnterNode(start);
    }

    public void RestartRun()
    {
        roomManager?.AbortRoom();
        ClearStartAreaObjects();
        RestoreStartRoomOrigin();
        startAreaActive = false;
        transitioningFromStartArea = false;
        pendingFirstGameplayNode = null;
        runActive = false;
        roomStartedWithMonsters = false;
        SetState(BattleRunState.None);
        StartRun();
    }

    private static bool CanUseNodeRoomAsStartBase(BattleNodeData node)
    {
        return node != null &&
               node.room != null &&
               (node.type == BattleNodeType.Combat || node.type == BattleNodeType.Elite);
    }

    /// <summary>
    /// Start Area는 Room이 아닙니다.
    /// 여기서는 BattleRoomManager.EnterRoom / NodeEntered를 호출하지 않습니다.
    /// 따라서 Monster Spawn / Room Wall / Obstacle / Reward / Combat 이벤트 경로가 존재하지 않습니다.
    /// </summary>
    private void EnterEmptyStartArea(BattleNodeData firstGameplayNode)
    {
        if (firstGameplayNode == null || firstGameplayNode.room == null)
        {
            EnterNode(firstGameplayNode);
            return;
        }

        pendingFirstGameplayNode = firstGameplayNode;
        startAreaActive = true;
        transitioningFromStartArea = false;
        currentNode = null;
        currentContext = null;
        roomStartedWithMonsters = false;

        RestoreStartRoomOrigin();

        RoomDefinitionSO room = firstGameplayNode.room;
        RoomBaseTemplate baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (baseTemplate != null)
            baseTemplate.BuildBase(room);
        else
            Debug.LogWarning("[BattleRun] RoomBaseTemplate not found. Start Area may have no visible Base.");

        PositionPlayerAtStartBaseCenter(room);
        BuildStartAreaBoundaryAndExit(room);
        SetState(BattleRunState.EnteringNode);
    }

    private void CaptureStartRoomOrigin()
    {
        if (startOriginCaptured || roomManager == null || roomManager.RoomOrigin == null)
            return;

        startRoomOriginPosition = roomManager.RoomOrigin.position;
        startOriginCaptured = true;
    }

    private void RestoreStartRoomOrigin()
    {
        if (!startOriginCaptured || roomManager == null || roomManager.RoomOrigin == null)
            return;

        roomManager.RoomOrigin.position = startRoomOriginPosition;
    }

    private void PositionPlayerAtStartBaseCenter(RoomDefinitionSO room)
    {
        if (room == null || playerController == null || roomManager == null || roomManager.RoomOrigin == null)
            return;

        Vector3 destination = roomManager.RoomOrigin.position + (Vector3)room.GetRuntimeBaseCenterOffset();
        destination.z = playerController.transform.position.z;
        playerController.transform.position = destination;

        Rigidbody2D body = playerController.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    private void BuildStartAreaBoundaryAndExit(RoomDefinitionSO room)
    {
        ClearStartAreaObjects();

        if (room == null || roomManager == null || roomManager.RoomOrigin == null || playerController == null)
            return;

        Vector2 size = room.GetRuntimeBaseWorldSize();
        Vector2 center = (Vector2)roomManager.RoomOrigin.position + room.GetRuntimeBaseCenterOffset();
        float thickness = Mathf.Max(0.05f, startBoundaryThickness);

        CreateStartBoundary(
            "StartBoundary_Left",
            new Vector2(center.x - size.x * 0.5f - thickness * 0.5f, center.y),
            new Vector2(thickness, size.y + thickness * 2f));
        CreateStartBoundary(
            "StartBoundary_Right",
            new Vector2(center.x + size.x * 0.5f + thickness * 0.5f, center.y),
            new Vector2(thickness, size.y + thickness * 2f));
        CreateStartBoundary(
            "StartBoundary_Bottom",
            new Vector2(center.x, center.y - size.y * 0.5f - thickness * 0.5f),
            new Vector2(size.x, thickness));
        CreateStartBoundary(
            "StartBoundary_Top",
            new Vector2(center.x, center.y + size.y * 0.5f + thickness * 0.5f),
            new Vector2(size.x, thickness));

        Vector2 direction = GetCardinalDirection(firstRoomDirection);
        Vector2 triggerPosition = center;
        Vector2 triggerSize;

        if (Mathf.Abs(direction.x) > 0.5f)
        {
            triggerPosition.x += direction.x * (size.x * 0.5f - 0.34f);
            triggerSize = new Vector2(0.72f, Mathf.Min(size.y - 0.6f, Mathf.Max(0.8f, startExitWidth)));
        }
        else
        {
            triggerPosition.y += direction.y * (size.y * 0.5f - 0.34f);
            triggerSize = new Vector2(Mathf.Min(size.x - 0.6f, Mathf.Max(0.8f, startExitWidth)), 0.72f);
        }

        GameObject exit = new("StartAreaExitTrigger");
        exit.transform.SetParent(roomManager.transform, true);
        exit.transform.position = new Vector3(triggerPosition.x, triggerPosition.y, 0f);

        BoxCollider2D trigger = exit.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = triggerSize;

        StartAreaExitTrigger exitTrigger = exit.AddComponent<StartAreaExitTrigger>();
        exitTrigger.Arm(playerController.transform, HandleStartAreaExitTriggered);
        startAreaObjects.Add(exit);
    }

    private void CreateStartBoundary(string objectName, Vector2 worldPosition, Vector2 colliderSize)
    {
        GameObject boundary = new(objectName);
        boundary.transform.SetParent(roomManager != null ? roomManager.transform : transform, true);
        boundary.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);

        int defaultLayer = LayerMask.NameToLayer("Default");
        boundary.layer = defaultLayer >= 0 ? defaultLayer : 0;

        BoxCollider2D collider = boundary.AddComponent<BoxCollider2D>();
        collider.isTrigger = false;
        collider.size = colliderSize;
        startAreaObjects.Add(boundary);
    }

    private void HandleStartAreaExitTriggered()
    {
        if (!runActive || !startAreaActive || transitioningFromStartArea)
            return;

        BattleNodeData firstGameplayNode = pendingFirstGameplayNode;
        if (firstGameplayNode == null || firstGameplayNode.room == null)
        {
            Debug.LogError("[BattleRun] Start Area exit was triggered but the first Gameplay Node is missing.");
            EndRun(RunEndReason.Quit);
            return;
        }

        startAreaActive = false;
        transitioningFromStartArea = true;
        pendingFirstGameplayNode = null;

        PositionFirstGameplayRoomNextToStart(firstGameplayNode.room);
        EnterNode(firstGameplayNode, true);
    }

    private void PositionFirstGameplayRoomNextToStart(RoomDefinitionSO room)
    {
        if (!startOriginCaptured || room == null || roomManager == null || roomManager.RoomOrigin == null)
            return;

        Vector2 direction = GetCardinalDirection(firstRoomDirection);
        Vector2 size = room.GetTemplateWorldSize();
        float distance = Mathf.Abs(direction.x) > 0.5f ? size.x : size.y;

        Vector3 position = startRoomOriginPosition + (Vector3)(direction * distance);
        position.z = startRoomOriginPosition.z;
        roomManager.RoomOrigin.position = position;
    }

    private static Vector2 GetCardinalDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.001f)
            return Vector2.right;

        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? new Vector2(Mathf.Sign(direction.x), 0f)
            : new Vector2(0f, Mathf.Sign(direction.y));
    }

    private void ClearStartAreaObjects()
    {
        for (int i = 0; i < startAreaObjects.Count; i++)
        {
            if (startAreaObjects[i] != null)
                Destroy(startAreaObjects[i]);
        }

        startAreaObjects.Clear();
    }

    public void SelectNextNode(string nodeId)
    {
        if (!runActive || state != BattleRunState.SelectingNode)
            return;

        BattleNodeData selected = null;
        for (int i = 0; i < nextNodeChoices.Count; i++)
        {
            if (nextNodeChoices[i] != null && nextNodeChoices[i].id == nodeId)
            {
                selected = nextNodeChoices[i];
                break;
            }
        }

        if (selected == null)
        {
            Debug.LogWarning($"[BattleRun] Node '{nodeId}' is not selectable from the current branch.");
            return;
        }

        nextNodeChoices.Clear();
        EnterNode(selected);
    }

    public void ResolveNonCombatNode()
    {
        if (!runActive || currentNode == null || state != BattleRunState.NonCombat)
            return;

        CompleteCurrentNode();
    }

    public bool SelectReward(int rewardIndex)
    {
        if (!TryGetReward(rewardIndex, out BattleEquipmentSO selected))
            return false;

        if (!equipmentSystem.TryAcquire(selected))
        {
            Debug.LogWarning("[BattleRun] Reward could not be acquired. Inventory may be full. Replace/discard a slot before selecting again.");
            return false;
        }

        CompleteRewardSelection(selected);
        return true;
    }

    public bool ReplaceRewardIntoSlot(int rewardIndex, int slotIndex)
    {
        if (!TryGetReward(rewardIndex, out BattleEquipmentSO selected))
            return false;

        if (!equipmentSystem.ReplaceSlot(slotIndex, selected))
            return false;

        CompleteRewardSelection(selected);
        return true;
    }

    private bool TryGetReward(int rewardIndex, out BattleEquipmentSO selected)
    {
        selected = null;

        if (!runActive || state != BattleRunState.Reward)
            return false;

        if (rewardIndex < 0 || rewardIndex >= currentRewardChoices.Count)
            return false;

        selected = currentRewardChoices[rewardIndex];
        return selected != null;
    }

    private void CompleteRewardSelection(BattleEquipmentSO selected)
    {
        currentRewardChoices.Clear();
        RewardSelected?.Invoke(selected);
        OpenRoomExitAfterReward();
    }

    public void SkipReward()
    {
        if (!runActive || state != BattleRunState.Reward)
            return;

        currentRewardChoices.Clear();
        OpenRoomExitAfterReward();
    }

    public void NotifyPlayerDeath() => HandlePlayerDeath();

    public void QuitRun()
    {
        if (!runActive)
            return;

        EndRun(RunEndReason.Quit);
    }

    private void HandlePlayerDeath()
    {
        if (!runActive)
            return;

        EndRun(RunEndReason.Death);
    }

    private void EnterNode(BattleNodeData node, bool fromStartArea = false)
    {
        if (node == null)
        {
            Debug.LogError("[BattleRun] Cannot enter a null node.");
            EndRun(RunEndReason.Quit);
            return;
        }

        roomStartedWithMonsters = false;
        SetState(BattleRunState.EnteringNode);
        currentNode = node;
        currentContext = BuildContext(node);

        // RoomBaseTemplate은 이 시점에 실제 Gameplay Room의 Wall/Extension 준비만 수행합니다.
        // Start Area에서는 이 이벤트 자체를 호출하지 않습니다.
        NodeEntered?.Invoke(node);

        switch (node.type)
        {
            case BattleNodeType.Combat:
            case BattleNodeType.Elite:
                if (node.room == null)
                {
                    Debug.LogError($"[BattleRun] Combat node '{node.id}' has no RoomDefinitionSO.");
                    EndRun(RunEndReason.Quit);
                    return;
                }

                SetState(BattleRunState.BuildingRoom);

                // 실제 Gameplay Room의 4x4 바닥은 Persistent Start Base와 다른 공간입니다.
                // BattleRoomManager의 구형 "Start Base 내부 Cell 생략" 조건을 런타임 호출 동안만 해제해
                // Room의 4x4 Floor Block을 전부 쿵 하고 조립합니다.
                bool savedPersistentFlag = node.room.usePersistentStartBase;
                bool savedReposition = node.room.repositionPlayerOnEnter;
                node.room.usePersistentStartBase = false;

                if (fromStartArea)
                    node.room.repositionPlayerOnEnter = false;

                try
                {
                    roomManager.EnterRoom(node.room, currentContext);
                }
                finally
                {
                    node.room.usePersistentStartBase = savedPersistentFlag;
                    node.room.repositionPlayerOnEnter = savedReposition;
                }
                break;

            case BattleNodeType.Shop:
            case BattleNodeType.Event:
                SetState(BattleRunState.NonCombat);
                NonCombatNodeEntered?.Invoke(node);
                break;
        }
    }

    private BattleContext BuildContext(BattleNodeData node)
    {
        BattleContext context = new();

        float depthHp = Mathf.Max(0.01f, hpByDepth.Evaluate(node.depth));
        float depthDamage = Mathf.Max(0.01f, damageByDepth.Evaluate(node.depth));

        float nodeHp = 1f;
        float nodeDamage = 1f;
        if (node.type == BattleNodeType.Elite)
        {
            nodeHp = Mathf.Max(1f, eliteHpMultiplier);
            nodeDamage = Mathf.Max(1f, eliteDamageMultiplier);
        }

        VillainGrade grade = progress != null
            ? progress.CurrentVillainGrade
            : VillainGrade.C;

        context.Configure(
            node.depth,
            grade,
            clan,
            shootingTheme,
            depthHp,
            depthDamage,
            nodeHp,
            nodeDamage);

        return context;
    }

    private void HandleRoomCombatStarted(RoomDefinitionSO room)
    {
        if (!runActive || currentNode == null || currentNode.room != room)
            return;

        if (transitioningFromStartArea)
        {
            transitioningFromStartArea = false;
            ClearStartAreaObjects();
            TeleportPlayerToCurrentRoomEntry(room);
        }

        roomStartedWithMonsters = roomManager != null && roomManager.AliveMonsterCount > 0;
        SetState(BattleRunState.Combat);
    }

    private void TeleportPlayerToCurrentRoomEntry(RoomDefinitionSO room)
    {
        if (room == null || playerController == null || roomManager == null || roomManager.RoomOrigin == null)
            return;

        Vector3 destination = roomManager.RoomOrigin.position + (Vector3)room.playerEntryOffset;
        destination.z = playerController.transform.position.z;
        playerController.transform.position = destination;

        Rigidbody2D body = playerController.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    private void HandleRoomCombatCleared(RoomDefinitionSO room)
    {
        if (!runActive || currentNode == null || currentNode.room != room)
            return;

        if (!roomStartedWithMonsters && RoomRequestsMonsters(room))
        {
            Debug.LogError(
                $"[BattleRun] Room '{room.roomId}' entered Combat with zero spawned monsters although monster spawns are configured. " +
                "Combat is kept active for diagnostics. Check Room Floor NavMesh / Monster prefab setup.");
            SetState(BattleRunState.Combat);
            return;
        }

        currentRewardChoices.Clear();
        List<BattleEquipmentSO> generated = rewardSystem.GenerateChoices(shootingTheme);

        if (generated.Count == 0)
        {
            Debug.LogWarning("[BattleRun] No reward choices were generated. Opening Room exit directly.");
            OpenRoomExitAfterReward();
            return;
        }

        currentRewardChoices.AddRange(generated);
        SetState(BattleRunState.Reward);
        RewardSelectionRequested?.Invoke(currentRewardChoices);
    }

    private static bool RoomRequestsMonsters(RoomDefinitionSO room)
    {
        if (room == null || room.monsterSpawns == null)
            return false;

        for (int i = 0; i < room.monsterSpawns.Count; i++)
        {
            MonsterSpawnEntry entry = room.monsterSpawns[i];
            if (entry != null && entry.monster != null && entry.count > 0)
                return true;
        }

        return false;
    }

    private void HandleMonsterDefeated(MonsterController monster)
    {
        if (!runActive || monster == null)
            return;

        int point = monster.Definition != null
            ? Mathf.Max(0, monster.Definition.killPointReward)
            : 1;

        progress?.AddMonsterKillPoints(point);
    }

    private void OpenRoomExitAfterReward()
    {
        SetState(BattleRunState.ExitingRoom);
        roomManager.OpenExit();
    }

    private void HandleRoomExited(RoomDefinitionSO room)
    {
        if (!runActive || currentNode == null || currentNode.room != room)
            return;

        CompleteCurrentNode();
    }

    private void CompleteCurrentNode()
    {
        if (currentNode == null)
            return;

        if (currentNode.isTerminal)
        {
            EndRun(RunEndReason.Clear);
            return;
        }

        List<BattleNodeData> next = nodeGraph.GetNextNodes(currentNode);
        if (next.Count == 0)
        {
            Debug.LogWarning($"[BattleRun] Node '{currentNode.id}' is not terminal but has no next node. Treating it as run clear.");
            EndRun(RunEndReason.Clear);
            return;
        }

        nextNodeChoices.Clear();
        nextNodeChoices.AddRange(next);
        SetState(BattleRunState.SelectingNode);
        NextNodeSelectionRequested?.Invoke(nextNodeChoices);
    }

    private void EndRun(RunEndReason reason)
    {
        if (!runActive && state == BattleRunState.Ended)
            return;

        runActive = false;
        roomStartedWithMonsters = false;
        startAreaActive = false;
        transitioningFromStartArea = false;
        pendingFirstGameplayNode = null;
        lastEndReason = reason;
        currentRewardChoices.Clear();
        nextNodeChoices.Clear();
        ClearStartAreaObjects();

        if (reason != RunEndReason.Clear)
            roomManager?.AbortRoom();

        progress?.EndRun();
        SetState(BattleRunState.Ended);
        RunEnded?.Invoke(reason);
    }

    private void SetState(BattleRunState next)
    {
        if (state == next)
            return;

        state = next;
        StateChanged?.Invoke(state);
    }
}

/// <summary>
/// Start Area 전용 보이지 않는 전환 Trigger입니다.
/// Start Area는 Room이 아니므로 RoomExitPad/BattleRoomManager를 재사용하지 않습니다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
internal sealed class StartAreaExitTrigger : MonoBehaviour
{
    private Transform player;
    private Action onTriggered;
    private bool armed;

    public void Arm(Transform playerTarget, Action callback)
    {
        player = playerTarget;
        onTriggered = callback;
        armed = true;

        Collider2D collider = GetComponent<Collider2D>();
        if (collider != null)
            collider.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!armed || player == null || other == null)
            return;

        Transform otherTransform = other.transform;
        if (otherTransform != player && !otherTransform.IsChildOf(player))
            return;

        armed = false;
        Action callback = onTriggered;
        onTriggered = null;
        callback?.Invoke();
    }
}

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
/// 런 시작 규칙:
/// 1) NodeGraph의 첫 Gameplay Node를 즉시 실행하지 않습니다.
/// 2) 첫 Combat Room의 Base 규격만 빌려 EMPTY START AREA를 먼저 만듭니다.
/// 3) Start Area에서는 Extension Block / Obstacle / Monster를 절대 생성하지 않습니다.
/// 4) Start Area 출구를 밟아 RoomExited가 발생한 뒤에야 원래 NodeGraph의 첫 Node로 진입합니다.
///
/// 이후 Node 진입 -> Room 생성 -> Combat -> Reward -> Exit -> 다음 Node 선택을
/// 명시적인 상태 머신으로 관리하며, Room 내부 구현과 보상/진행 로직을 분리합니다.
///
/// BattleSceneManager 설치/필수 Prefab/SO 검증을 통과하지 못하면
/// StartRun 자체가 실행되지 않습니다.
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
    [Tooltip("런 시작 시 첫 Gameplay Room보다 앞에 몬스터/벽/장애물이 없는 Start Area를 강제로 둡니다.")]
    [SerializeField] private bool useEmptyStartArea = true;
    [Tooltip("보이지 않는 Start Area 경계 Collider 두께입니다. Base 밖으로 걸어나가는 것을 막습니다.")]
    [SerializeField, Min(0.05f)] private float startBoundaryThickness = 0.35f;

    [Header("Depth Scaling - inspector driven")]
    [SerializeField] private AnimationCurve hpByDepth = AnimationCurve.Linear(0f, 1f, 10f, 1f);
    [SerializeField] private AnimationCurve damageByDepth = AnimationCurve.Linear(0f, 1f, 10f, 1f);

    [Header("Elite Node provisional multiplier")]
    [SerializeField] private float eliteHpMultiplier = 1.5f;
    [SerializeField] private float eliteDamageMultiplier = 1.5f;

    private readonly List<BattleNodeData> nextNodeChoices = new();
    private readonly List<BattleEquipmentSO> currentRewardChoices = new();
    private readonly List<GameObject> startAreaBoundaries = new();

    private BattleNodeData currentNode;
    private BattleNodeData pendingFirstGameplayNode;
    private BattleContext currentContext;
    private BattleRunState state = BattleRunState.None;
    private bool runActive;
    private bool roomStartedWithMonsters;
    private bool startAreaActive;
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

        ClearStartAreaBoundaries();
    }

    public bool ValidateConfiguration(out string report)
    {
        List<string> errors = new();

        if (sceneManager == null)
            sceneManager = FindFirstObjectByType<BattleSceneManager>();

        if (sceneManager == null)
        {
            errors.Add("BattleSceneManager is missing. Add BattleSceneManager to the BattleSystems GameObject and run Install / Repair Battle Scene.");
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

        ClearStartAreaBoundaries();
        startAreaActive = false;
        pendingFirstGameplayNode = null;

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
        roomStartedWithMonsters = false;
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
        ClearStartAreaBoundaries();
        startAreaActive = false;
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
    /// 첫 Gameplay Room의 Base 크기/출구 데이터만 빌려 EMPTY START AREA를 만듭니다.
    /// 이 단계에서는 NodeEntered를 호출하지 않기 때문에 RoomBaseTemplate의 전투용 Shell도 생성되지 않습니다.
    /// Room의 Block / Obstacle / Monster 목록을 EnterRoom 호출 동안만 비워서
    /// BattleRoomManager 레벨에서도 Monster Spawn 경로를 완전히 차단합니다.
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
        roomStartedWithMonsters = false;
        currentNode = null;
        currentContext = BuildContext(firstGameplayNode);
        SetState(BattleRunState.EnteringNode);

        RoomDefinitionSO room = firstGameplayNode.room;

        RoomBaseTemplate baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (baseTemplate != null)
            baseTemplate.BuildBase(room);
        else
            Debug.LogWarning("[BattleRun] RoomBaseTemplate not found. Empty Start Area may have no visible Base.");

        BuildStartAreaBoundaries(room);

        List<MapBlockPlacement> savedBlocks = room.blocks;
        List<ObstaclePlacement> savedObstacles = room.obstacles;
        List<MonsterSpawnEntry> savedSpawns = room.monsterSpawns;

        // Start Area는 '아무 것도 없는 시작 구역'입니다.
        // 기존 첫 Room 데이터를 잠깐 비워 BattleRoomManager가 Block/Obstacle/Monster를 하나도 만들 수 없게 합니다.
        room.blocks = new List<MapBlockPlacement>();
        room.obstacles = new List<ObstaclePlacement>();
        room.monsterSpawns = new List<MonsterSpawnEntry>();

        try
        {
            roomManager.EnterRoom(room, currentContext);
        }
        finally
        {
            // EnterRoomRoutine은 Block이 0개이면 첫 yield 없이 NavMesh/Spawn/Room 이벤트까지 처리합니다.
            // 원본 SO 데이터는 즉시 복구하여 실제 첫 Combat Room에서 그대로 사용합니다.
            room.blocks = savedBlocks ?? new List<MapBlockPlacement>();
            room.obstacles = savedObstacles ?? new List<ObstaclePlacement>();
            room.monsterSpawns = savedSpawns ?? new List<MonsterSpawnEntry>();
        }
    }

    private void BuildStartAreaBoundaries(RoomDefinitionSO room)
    {
        ClearStartAreaBoundaries();

        if (room == null || roomManager == null)
            return;

        Transform origin = roomManager.RoomOrigin != null
            ? roomManager.RoomOrigin
            : roomManager.transform;

        Vector2 size = room.GetRuntimeBaseWorldSize();
        Vector2 centerLocal = room.GetRuntimeBaseCenterOffset();
        Vector2 centerWorld = (Vector2)origin.position + centerLocal;
        float thickness = Mathf.Max(0.05f, startBoundaryThickness);

        CreateStartBoundary(
            "StartBoundary_Left",
            new Vector2(centerWorld.x - size.x * 0.5f - thickness * 0.5f, centerWorld.y),
            new Vector2(thickness, size.y + thickness * 2f));

        CreateStartBoundary(
            "StartBoundary_Right",
            new Vector2(centerWorld.x + size.x * 0.5f + thickness * 0.5f, centerWorld.y),
            new Vector2(thickness, size.y + thickness * 2f));

        CreateStartBoundary(
            "StartBoundary_Bottom",
            new Vector2(centerWorld.x, centerWorld.y - size.y * 0.5f - thickness * 0.5f),
            new Vector2(size.x, thickness));

        CreateStartBoundary(
            "StartBoundary_Top",
            new Vector2(centerWorld.x, centerWorld.y + size.y * 0.5f + thickness * 0.5f),
            new Vector2(size.x, thickness));
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

        startAreaBoundaries.Add(boundary);
    }

    private void ClearStartAreaBoundaries()
    {
        for (int i = 0; i < startAreaBoundaries.Count; i++)
        {
            GameObject boundary = startAreaBoundaries[i];
            if (boundary != null)
                Destroy(boundary);
        }

        startAreaBoundaries.Clear();
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

    public void NotifyPlayerDeath()
    {
        HandlePlayerDeath();
    }

    public void QuitRun()
    {
        if (!runActive) return;
        EndRun(RunEndReason.Quit);
    }

    private void HandlePlayerDeath()
    {
        if (!runActive) return;
        EndRun(RunEndReason.Death);
    }

    private void EnterNode(BattleNodeData node)
    {
        if (node == null)
        {
            Debug.LogError("[BattleRun] Cannot enter a null node.");
            EndRun(RunEndReason.Quit);
            return;
        }

        startAreaActive = false;
        roomStartedWithMonsters = false;
        SetState(BattleRunState.EnteringNode);
        currentNode = node;
        currentContext = BuildContext(node);
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
                roomManager.EnterRoom(node.room, currentContext);
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
        if (!runActive)
            return;

        if (startAreaActive)
        {
            // BattleRoomManager의 일반 Room 초기화 루틴은 재사용하지만 Start Area는 전투가 아닙니다.
            // PlayerController의 EnteringNode 입력 규칙(이동/구르기 O, 사격 X)을 그대로 유지합니다.
            roomStartedWithMonsters = false;
            SetState(BattleRunState.EnteringNode);
            return;
        }

        if (currentNode == null || currentNode.room != room)
            return;

        roomStartedWithMonsters = roomManager != null && roomManager.AliveMonsterCount > 0;
        SetState(BattleRunState.Combat);
    }

    private void HandleRoomCombatCleared(RoomDefinitionSO room)
    {
        if (!runActive)
            return;

        if (startAreaActive)
        {
            // Start Area는 Monster 0이 정상입니다. Reward로 가지 않고 출구만 즉시 엽니다.
            roomManager.OpenExit();
            return;
        }

        if (currentNode == null || currentNode.room != room)
            return;

        if (!roomStartedWithMonsters && RoomRequestsMonsters(room))
        {
            Debug.LogError(
                $"[BattleRun] Room '{room.roomId}' entered Combat with zero spawned monsters although monster spawns are configured. " +
                "Combat is being kept active for diagnostics. Check MapBlock NavMeshModifier / NavMeshSurface / Monster prefab setup.");
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
            if (entry != null && entry.monster != null && entry.count != 0)
                return true;
        }

        return false;
    }

    private void HandleMonsterDefeated(MonsterController monster)
    {
        if (!runActive || startAreaActive || monster == null)
            return;

        int point = 1;
        if (monster.Definition != null)
            point = Mathf.Max(0, monster.Definition.killPointReward);

        progress?.AddMonsterKillPoints(point);
    }

    private void OpenRoomExitAfterReward()
    {
        SetState(BattleRunState.ExitingRoom);
        roomManager.OpenExit();
    }

    private void HandleRoomExited(RoomDefinitionSO room)
    {
        if (!runActive)
            return;

        if (startAreaActive)
        {
            BattleNodeData firstGameplayNode = pendingFirstGameplayNode;
            pendingFirstGameplayNode = null;
            startAreaActive = false;
            currentNode = null;
            currentContext = null;
            ClearStartAreaBoundaries();

            if (firstGameplayNode == null)
            {
                Debug.LogError("[BattleRun] Start Area exited but the first Gameplay Node was lost.");
                EndRun(RunEndReason.Quit);
                return;
            }

            // 여기서부터 처음으로 실제 NodeGraph의 첫 Room이 시작됩니다.
            EnterNode(firstGameplayNode);
            return;
        }

        if (currentNode == null || currentNode.room != room)
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
        pendingFirstGameplayNode = null;
        lastEndReason = reason;
        currentRewardChoices.Clear();
        nextNodeChoices.Clear();
        ClearStartAreaBoundaries();

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

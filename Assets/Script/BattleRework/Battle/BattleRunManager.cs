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
    ExitingRoom, // legacy compatibility; stage-select flow does not use physical room exits.
    SelectingNode,
    NonCombat,
    Ended
}

/// <summary>
/// Top-level run flow.
///
/// Current rules:
/// - Start Area is not a Room.
/// - Start Area contains only the persistent fixed 4x4 Base and Player.
/// - No bridge/corridor/world traversal exists between stages.
/// - Stage progression is selected by clicking a vertical NodeGraph.
/// - Multiple initial nodes are supported; the first decision is not forced to a single preselected Room.
/// - The current persistent 4x4 Base is the authoritative spatial anchor for every incoming stage.
/// - Combat -> Reward decision -> Stage selection -> Next Stage.
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

    [Header("Start Stage Selection")]
    [Tooltip("Run starts on an empty non-combat 4x4 Base and asks the player to click one of the configured start nodes.")]
    [SerializeField] private bool useEmptyStartArea = true;
    [Tooltip("오프닝 대기실 4x4 Base 중심에서 플레이어를 옮길 월드 좌표 오프셋입니다. 기본값은 화면 왼쪽 아래 쪽입니다.")]
    [SerializeField] private Vector2 startPlayerWaitingRoomOffset = new(-0.9f, -0.35f);

    [Header("Depth Scaling - inspector driven")]
    [SerializeField] private AnimationCurve hpByDepth = AnimationCurve.Linear(0f, 1f, 10f, 1f);
    [SerializeField] private AnimationCurve damageByDepth = AnimationCurve.Linear(0f, 1f, 10f, 1f);

    [Header("Elite Node provisional multiplier")]
    [SerializeField] private float eliteHpMultiplier = 1.5f;
    [SerializeField] private float eliteDamageMultiplier = 1.5f;

    private readonly List<BattleNodeData> nextNodeChoices = new();
    private readonly List<BattleEquipmentSO> currentRewardChoices = new();

    private BattleNodeData currentNode;
    private BattleContext currentContext;
    private BattleRunState state = BattleRunState.None;
    private bool runActive;
    private bool roomStartedWithMonsters;
    private bool startAreaActive;
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

        List<BattleNodeData> starts = nodeGraph.GetStartNodes();
        if (starts.Count == 0)
        {
            Debug.LogError("[BattleRun] No start node choices could be resolved.");
            return;
        }

        currentRewardChoices.Clear();
        nextNodeChoices.Clear();
        currentNode = null;
        currentContext = null;
        roomStartedWithMonsters = false;
        startAreaActive = false;
        lastEndReason = null;

        equipmentSystem.ResetForRun();
        fanMissionSystem?.ResetForRun();
        playerController.ResetForRun();
        progress.BeginRun();
        runActive = true;

        if (useEmptyStartArea && TryGetStartBaseRoom(starts, out RoomDefinitionSO baseRoom))
            EnterEmptyStartArea(starts, baseRoom);
        else
            EnterNode(starts[0]);
    }

    public void RestartRun()
    {
        roomManager?.AbortRoom();
        RestoreStartRoomOrigin();
        startAreaActive = false;
        runActive = false;
        roomStartedWithMonsters = false;
        SetState(BattleRunState.None);
        StartRun();
    }

    private static bool TryGetStartBaseRoom(IReadOnlyList<BattleNodeData> choices, out RoomDefinitionSO room)
    {
        room = null;
        if (choices == null)
            return false;

        for (int i = 0; i < choices.Count; i++)
        {
            BattleNodeData node = choices[i];
            if (node == null || node.room == null)
                continue;
            if (node.type != BattleNodeType.Combat && node.type != BattleNodeType.Elite)
                continue;

            room = node.room;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds only the persistent 4x4 Start Base and exposes every configured start node as a clickable choice.
    /// </summary>
    private void EnterEmptyStartArea(IReadOnlyList<BattleNodeData> startingChoices, RoomDefinitionSO baseRoom)
    {
        if (startingChoices == null || startingChoices.Count == 0 || baseRoom == null)
        {
            Debug.LogError("[BattleRun] Empty Start Area requires at least one start choice and one combat Room for Base presentation.");
            EndRun(RunEndReason.Quit);
            return;
        }

        startAreaActive = true;
        currentNode = null;
        currentContext = null;
        roomStartedWithMonsters = false;
        RestoreStartRoomOrigin();

        RoomBaseTemplate baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (baseTemplate != null)
            baseTemplate.BuildBase(baseRoom);

        PositionPlayerAtStartBaseCenter(baseRoom);

        nextNodeChoices.Clear();
        for (int i = 0; i < startingChoices.Count; i++)
        {
            BattleNodeData node = startingChoices[i];
            if (node != null && !nextNodeChoices.Contains(node))
                nextNodeChoices.Add(node);
        }

        SetState(BattleRunState.SelectingNode);
        NextNodeSelectionRequested?.Invoke(nextNodeChoices);
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

    private void AlignRoomOriginToPersistentBase()
    {
        if (roomManager == null || roomManager.RoomOrigin == null)
            return;

        RoomBaseTemplate baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (baseTemplate == null)
            return;

        baseTemplate.EnsurePersistentBase();
        Vector3 origin = baseTemplate.FixedTileOriginWorld;
        origin.z = roomManager.RoomOrigin.position.z;
        roomManager.RoomOrigin.position = origin;
    }

    private void PositionPlayerAtStartBaseCenter(RoomDefinitionSO room)
    {
        if (room == null || playerController == null || roomManager == null || roomManager.RoomOrigin == null)
            return;

        RoomBaseTemplate baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        Vector3 destination = baseTemplate != null
            ? baseTemplate.FixedCenterWorld
            : roomManager.RoomOrigin.position + (Vector3)room.GetStartBaseCenterOffset();
        float maxOffset = (RoomBaseTemplate.FixedBaseTiles - 1) * 0.5f - 0.15f;
        Vector2 waitingRoomOffset = new(
            Mathf.Clamp(startPlayerWaitingRoomOffset.x, -maxOffset, maxOffset),
            Mathf.Clamp(startPlayerWaitingRoomOffset.y, -maxOffset, maxOffset));
        destination += (Vector3)waitingRoomOffset;
        destination.z = playerController.transform.position.z;
        playerController.transform.position = destination;

        Rigidbody2D body = playerController.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
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

        startAreaActive = false;
        nextNodeChoices.Clear();
        AlignRoomOriginToPersistentBase();
        EnterNode(selected);
    }

    public void ResolveNonCombatNode()
    {
        if (!runActive || currentNode == null || state != BattleRunState.NonCombat)
            return;
        CompleteCurrentNode();
    }

    // Legacy quick-acquire API kept for compatibility with old callers.
    public bool SelectReward(int rewardIndex)
    {
        if (!TryGetReward(rewardIndex, out BattleEquipmentSO selected))
            return false;

        if (!equipmentSystem.TryAcquire(selected))
        {
            Debug.LogWarning("[BattleRun] Reward could not be acquired. Inventory may be full. Place it into an unlocked slot instead.");
            return false;
        }

        CompleteRewardSelection(selected);
        return true;
    }

    /// <summary>
    /// Reward Show의 기본 획득 경로.
    /// 사용자가 선택한 보상을 직접 지정한 슬롯에 Drop해야만 획득이 확정됩니다.
    /// 빈 슬롯은 배치, 같은 장비는 합성, 다른 장비가 있으면 그 장비를 버리고 교체합니다.
    /// </summary>
    public bool PlaceRewardIntoSlot(int rewardIndex, int slotIndex)
    {
        if (!TryGetReward(rewardIndex, out BattleEquipmentSO selected))
            return false;
        if (!equipmentSystem.PlaceIntoSlot(slotIndex, selected))
            return false;

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
        CompleteCurrentNode();
    }

    public void SkipReward()
    {
        if (!runActive || state != BattleRunState.Reward)
            return;
        currentRewardChoices.Clear();
        CompleteCurrentNode();
    }

    public void NotifyPlayerDeath() => HandlePlayerDeath();

    public void QuitRun()
    {
        if (runActive)
            EndRun(RunEndReason.Quit);
    }

    private void HandlePlayerDeath()
    {
        if (runActive)
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
                AlignRoomOriginToPersistentBase();

                bool savedPersistentFlag = node.room.usePersistentStartBase;
                node.room.usePersistentStartBase = false;
                try
                {
                    roomManager.EnterRoom(node.room, currentContext);
                }
                finally
                {
                    node.room.usePersistentStartBase = savedPersistentFlag;
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

        VillainGrade grade = progress != null ? progress.CurrentVillainGrade : VillainGrade.C;
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

        roomStartedWithMonsters = roomManager != null && roomManager.AliveMonsterCount > 0;
        SetState(BattleRunState.Combat);
    }

    private void HandleRoomCombatCleared(RoomDefinitionSO room)
    {
        if (!runActive || currentNode == null || currentNode.room != room)
            return;

        if (!roomStartedWithMonsters && RoomRequestsMonsters(room))
        {
            Debug.LogError(
                $"[BattleRun] Room '{room.roomId}' entered Combat with zero spawned monsters although monster spawns are configured. " +
                "Combat is kept active for diagnostics. Check procedural Room NavMesh / Monster prefab setup.");
            SetState(BattleRunState.Combat);
            return;
        }

        currentRewardChoices.Clear();
        List<BattleEquipmentSO> generated = rewardSystem.GenerateChoices(shootingTheme);
        if (generated.Count == 0)
        {
            CompleteCurrentNode();
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

    // Legacy compatibility only. The stage-select flow never opens a physical room exit.
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
        lastEndReason = reason;
        currentRewardChoices.Clear();
        nextNodeChoices.Clear();
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

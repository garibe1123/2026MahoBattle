using UnityEngine;

/// <summary>
/// BattleDecor의 Stage 생명주기를 BattleStageTransitionController와 동기화합니다.
///
/// 목적:
/// - 이전 Stage Decor는 기존 StageFlow가 퇴장을 요청하고 실제 소멸까지 기다립니다.
/// - 새 Stage가 물리적으로 안정된 뒤에만 Decor retirement gate를 해제합니다.
/// - EnteringNode / BuildingRoom 동안에는 gate를 다시 닫아 부분 조립 Field에 Decor가 먼저 생기는 것을 막습니다.
/// - Combat / NonCombat 확정 시 실제 완성된 Field 기준으로 한 번 재빌드합니다.
/// - Reward / Map Show도 Show 진입이 끝난 뒤 Decor를 다시 생성합니다.
///
/// Scene 전체 검색은 참조가 없을 때만 낮은 빈도로 수행합니다.
/// 정상 동작 중에는 enum 상태만 확인하므로 기존 FIELD WATCH의 고빈도 검색을 되살리지 않습니다.
/// </summary>
[DefaultExecutionOrder(30200)]
[DisallowMultipleComponent]
public sealed class BattleDecorStageLifecycleSync : MonoBehaviour
{
    private const float ResolveRetryInterval = 0.75f;

    private static BattleDecorStageLifecycleSync instance;

    private BattleStageTransitionController stageFlow;
    private BattleUniversalStageDecorCarrierSkinController decorStage;
    private BattleRunManager runManager;

    private BattleStageFlowState lastFlowState = (BattleStageFlowState)(-1);
    private BattleRunState lastRunState = (BattleRunState)(-1);
    private float nextResolveAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleDecorStageLifecycleSync>() != null)
            return;

        GameObject host = new("BattleDecorStageLifecycleSync");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleDecorStageLifecycleSync>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        nextResolveAt = 0f;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        ResolveSystemsIfNeeded();
        if (decorStage == null)
            return;

        // 새 Run의 대기실은 기존 retirement gate가 남아 있을 이유가 없습니다.
        if (runManager != null && runManager.IsInStartArea)
        {
            if (decorStage.StageRetirementRequested)
                decorStage.ReleaseStageRetirementGate();
        }
        // 기존 StageFlow가 BuildingRoom 진입 순간 gate를 먼저 풀더라도 실제 Room 조립이 끝나기 전에는
        // Decor를 만들지 않습니다. 이 구간은 다음 Combat / NonCombat 확정 상태까지 닫힌 채 유지합니다.
        else if (runManager != null &&
                 (runManager.State == BattleRunState.EnteringNode ||
                  runManager.State == BattleRunState.BuildingRoom))
        {
            if (!decorStage.StageRetirementRequested)
                decorStage.RequestStageRetirement();
            return;
        }

        if (stageFlow != null)
        {
            BattleStageFlowState currentFlow = stageFlow.FlowState;
            if (currentFlow != lastFlowState)
            {
                HandleFlowStateChanged(currentFlow);
                lastFlowState = currentFlow;
            }

            // FlowStateChanged를 놓쳤거나 Decor Controller가 늦게 연결된 경우에도
            // 안정된 Stage라면 retirement gate를 즉시 복구합니다.
            if (decorStage.StageRetirementRequested && IsStableDecorPhase(currentFlow))
                decorStage.ReleaseStageRetirementGate();

            return;
        }

        // StageFlow가 아직 없는 예외 경로에서는 Combat / NonCombat만 안전하게 복구합니다.
        // Reward / SelectingNode는 Show 진입 중일 수 있으므로 여기서 성급하게 gate를 풀지 않습니다.
        if (runManager == null)
            return;

        BattleRunState currentRun = runManager.State;
        if (currentRun == lastRunState)
            return;

        lastRunState = currentRun;
        if (currentRun == BattleRunState.Combat || currentRun == BattleRunState.NonCombat)
            decorStage.ReleaseStageRetirementGate();
    }

    private void HandleFlowStateChanged(BattleStageFlowState state)
    {
        if (decorStage == null)
            return;

        switch (state)
        {
            case BattleStageFlowState.RewardShow:
            case BattleStageFlowState.MapShow:
                // Combat Decor 퇴장이 끝난 뒤 Show가 실제로 도킹 완료된 시점입니다.
                // 여기서 gate를 풀어 Item / Map 선택 Stage에도 새 Decor를 만듭니다.
                if (decorStage.StageRetirementRequested)
                    decorStage.ReleaseStageRetirementGate();
                break;

            case BattleStageFlowState.Combat:
            case BattleStageFlowState.NonCombat:
                // 완성된 Room 상태에서 다시 호출해 Field Signature를 리셋합니다.
                // BuildingRoom 시점에 발생할 수 있는 조기 Release도 여기서 최종 정리됩니다.
                decorStage.ReleaseStageRetirementGate();
                break;
        }
    }

    private static bool IsStableDecorPhase(BattleStageFlowState state)
    {
        return state == BattleStageFlowState.RewardShow ||
               state == BattleStageFlowState.MapShow ||
               state == BattleStageFlowState.Combat ||
               state == BattleStageFlowState.NonCombat;
    }

    private void ResolveSystemsIfNeeded()
    {
        bool missing = stageFlow == null || decorStage == null || runManager == null;
        if (!missing || Time.unscaledTime < nextResolveAt)
            return;

        nextResolveAt = Time.unscaledTime + ResolveRetryInterval;

        bool stageFlowWasMissing = stageFlow == null;
        bool runManagerWasMissing = runManager == null;

        if (stageFlow == null)
        {
            stageFlow = BattleStageTransitionController.Instance != null
                ? BattleStageTransitionController.Instance
                : FindFirstObjectByType<BattleStageTransitionController>();
        }

        if (decorStage == null)
            decorStage = FindFirstObjectByType<BattleUniversalStageDecorCarrierSkinController>();

        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();

        // 참조가 새로 연결된 순간에만 현재 상태를 한 번 다시 처리합니다.
        if (stageFlowWasMissing && stageFlow != null)
            lastFlowState = (BattleStageFlowState)(-1);
        if (runManagerWasMissing && runManager != null)
            lastRunState = (BattleRunState)(-1);
    }
}

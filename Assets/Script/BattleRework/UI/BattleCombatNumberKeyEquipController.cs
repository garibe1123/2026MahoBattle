using UnityEngine;

/// <summary>
/// Combat 장비 슬롯 입력을 BattleInputRouter에서 받아 EquipSlot API로 전달합니다.
/// 숫자키를 직접 읽지 않으므로 Reward/Map/UI 상태에서 전투 장비 입력이 새어 나오지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30100)]
public sealed class BattleCombatNumberKeyEquipController : MonoBehaviour
{
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleInputRouter inputRouter;

    private bool subscribed;

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        // Runtime self-repair로 Router가 늦게 생성되는 경우만 복구합니다.
        if (inputRouter == null || !subscribed)
        {
            ResolveReferences();
            Subscribe();
        }
    }

    private void HandleSlotPressed(int index)
    {
        ResolveReferences();
        if (runManager == null || equipmentSystem == null || BattlePauseController.IsPaused)
            return;
        if (!runManager.RunActive || runManager.State != BattleRunState.Combat)
            return;
        if (index < 0 || index >= equipmentSystem.UnlockedSlotCount)
            return;

        equipmentSystem.EquipSlot(index);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (inputRouter == null)
            inputRouter = BattleInputRouter.ResolveOrCreate(this);
    }

    private void Subscribe()
    {
        if (subscribed || inputRouter == null)
            return;

        inputRouter.SlotPressed += HandleSlotPressed;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (inputRouter != null)
            inputRouter.SlotPressed -= HandleSlotPressed;
        subscribed = false;
    }
}

using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(PlayerController))]
public class PlayerItemSystem : MonoBehaviour
{
    [Header("Active Item (사용형)")]
    public ActiveItemSO currentActiveItem; // ★ 이제 전용 ActiveItemSO를 씁니다.
    private float activeCooldownTimer = 0f;

    [Header("Passive Items (소지형)")]
    public List<ItemBaseSO> passiveItems = new List<ItemBaseSO>();

    private PlayerController playerController;

    void Awake()
    {
        playerController = GetComponent<PlayerController>();
    }

    void Update()
    {
        // 1. 액티브 쿨타임 계산
        if (activeCooldownTimer > 0)
        {
            activeCooldownTimer -= Time.deltaTime;
        }

        // 2. F키로 액티브 아이템 발동!
        if (Input.GetKeyDown(KeyCode.F))
        {
            TryUseActiveItem();
        }
    }

    // ==========================================
    // 획득(루팅) 시 호출될 함수들
    // ==========================================

    public void EquipActiveItem(ActiveItemSO newItem)
    {
        // ★ 핵심: 이미 액티브 아이템을 들고 있다면? 바닥에 뱉어냅니다!
        if (currentActiveItem != null)
        {
            Debug.Log($"[{currentActiveItem.itemName}]을(를) 바닥에 버립니다!");

            if (ItemSpawner.Instance != null)
            {
                // 우리가 아까 만든 뽕! 하고 튀어나오는 스폰 함수 재활용!
                ItemSpawner.Instance.SpawnItem(transform.position, currentActiveItem);
            }
        }

        // 새로운 아이템 장착 및 쿨타임 초기화
        currentActiveItem = newItem;
        activeCooldownTimer = 0f;

        Debug.Log($"[{newItem.itemName}] 액티브 아이템 장착! (F키로 사용)");
        // TODO: UI 매니저에게 "우측 하단 아이콘 바꿔줘!" 라고 신호 보내기
    }

    public void AddPassiveItem(PassiveItemSO newItem)
    {
        passiveItems.Add(newItem);
        newItem.ApplyEffect(playerController);
    }

    // ==========================================
    // 액티브 아이템 사용 로직
    // ==========================================

    private void TryUseActiveItem()
    {
        if (currentActiveItem == null) return;

        if (activeCooldownTimer <= 0f)
        {
            // ★ SO에 정의된 효과 발동!
            currentActiveItem.ApplyEffect(playerController);

            // ★ SO에서 지정한 고유 쿨타임 적용
            activeCooldownTimer = currentActiveItem.cooldownTime;

            Debug.Log($"[{currentActiveItem.itemName}] 사용 완료! (쿨타임 {activeCooldownTimer}초)");
        }
        else
        {
            Debug.Log($"아직 쿨타임입니다! 남은 시간: {activeCooldownTimer:F1}초");
        }
    }
}

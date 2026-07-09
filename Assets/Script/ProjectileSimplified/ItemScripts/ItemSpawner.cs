using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ItemSpawner : MonoBehaviour
{
    public static ItemSpawner Instance { get; private set; }

    [Header("Base Prefab")]
    public GameObject pickupPrefab;

    [Header("Loot Pools (일반 랜덤용)")]
    public List<PlayerShootingSO> weaponPool;
    public List<ItemBaseSO> itemPool;

    [Header("Drop Probability Settings")]
    public float baseDropChance = 0.15f;    // 기본 드랍률 (15%)
    public float dropMultiplier = 1.0f;     // 드랍 배율 (피버 타임 때 2.0f 로 뻥튀기!)
    public float flatBonusChance = 0.0f;    // 고정 보너스 확률 (행운 아이템 먹었을 때 +0.05f)

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // ==========================================
    // 📦 1. 다중 아이템 드랍 (배열/리스트로 받아서 뽕! 뽕! 뽕! 연출)
    // ==========================================
    public void DropLootBox(Vector3 originPos, List<ItemBaseSO> items, List<PlayerShootingSO> weapons)
    {
        StartCoroutine(DropSequenceRoutine(originPos, items, weapons));
    }

    private IEnumerator DropSequenceRoutine(Vector3 originPos, List<ItemBaseSO> items, List<PlayerShootingSO> weapons)
    {
        // 1. 무기 먼저 뱉기
        if (weapons != null)
        {
            foreach (var weapon in weapons)
            {
                SpawnWeapon(originPos, weapon);
                yield return new WaitForSeconds(0.15f); // 0.15초마다 하나씩 뽕!
            }
        }

        // 2. 그 다음 아이템들 뱉기
        if (items != null)
        {
            foreach (var item in items)
            {
                SpawnItem(originPos, item);
                yield return new WaitForSeconds(0.15f); // 0.15초마다 하나씩 뽕!
            }
        }
    }

    // ==========================================
    // 단일 생성 함수 (내부적으로 Pop 애니메이션 호출)
    // ==========================================
    public void SpawnItem(Vector3 position, ItemBaseSO itemData)
    {
        if (pickupPrefab == null || itemData == null) return;

        GameObject obj = Instantiate(pickupPrefab, position, Quaternion.identity);
        PickupObject pickup = obj.GetComponent<PickupObject>();

        pickup.pickupType = GetPickupType(itemData);
        pickup.itemData = itemData;

        // ★ 생성되자마자 튀어오르는 연출 시작!
        StartCoroutine(PopAnimationRoutine(obj.transform));
    }

    public void SpawnWeapon(Vector3 position, PlayerShootingSO weaponData)
    {
        if (pickupPrefab == null || weaponData == null) return;

        GameObject obj = Instantiate(pickupPrefab, position, Quaternion.identity);
        PickupObject pickup = obj.GetComponent<PickupObject>();

        pickup.pickupType = PickupType.Weapon;
        pickup.weaponData = weaponData;

        // ★ 생성되자마자 튀어오르는 연출 시작!
        StartCoroutine(PopAnimationRoutine(obj.transform));
    }

    // ==========================================
    // 🌟 뽕! 튀어나오는 포물선 연출 (Pop-out Animation)
    // ==========================================
    private IEnumerator PopAnimationRoutine(Transform target)
    {
        Vector3 startPos = target.position;

        // 1. 튀어나갈 랜덤 방향과 도착 지점 계산 (주변 1.5f 반경 이내)
        Vector2 randomDir = Random.insideUnitCircle.normalized;
        Vector3 endPos = startPos + (Vector3)randomDir * Random.Range(1.0f, 2.0f);

        float duration = 0.4f; // 체공 시간 (0.4초 동안 날아감)
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // 날아가는 도중에 플레이어가 먹어버려서 파괴됐으면 코루틴 즉시 종료!
            if (target == null) yield break;

            elapsed += Time.deltaTime;
            float t = elapsed / duration; // 0.0 ~ 1.0 진행도

            // ★ 핵심: Sin 그래프를 이용해 위로 솟구쳤다가(최대 1.5 높이) 떨어지는 Y축 포물선 계산
            float height = Mathf.Sin(t * Mathf.PI) * 1.5f;

            // X, Z축으론 목적지까지 부드럽게 이동(Lerp)하고, Y축으론 포물선(height) 높이를 더해줍니다.
            target.position = Vector3.Lerp(startPos, endPos, t) + new Vector3(0, height, 0);

            yield return null;
        }
    }

    private PickupType GetPickupType(ItemBaseSO item)
    {
        if (item is ConsumableItemSO) return PickupType.Consumable;
        if (item is PassiveItemSO) return PickupType.Passive;
        return PickupType.Consumable;
    }

    // ==========================================
    // 💀 몬스터 처치 시 호출되는 드랍 판정기 (Centralized)
    // ==========================================
    public void TryDropLootFromEnemy(Vector3 position)
    {
        // 1. 최종 확률 계산 = (기본 확률 + 아이템 보너스) * 피버 타임 배율
        float finalChance = (baseDropChance + flatBonusChance) * dropMultiplier;

        // 최대 확률은 100%(1.0f)를 넘지 않게 고정
        finalChance = Mathf.Clamp01(finalChance);

        // 2. 주사위 굴리기! (0.0 ~ 1.0 사이 난수 뽑기)
        if (Random.value <= finalChance)
        {
            // 당첨! 이제 여기서 무엇을 뱉을지 결정합니다.
            // 예: 10% 확률로 무기, 90% 확률로 일반 아이템
            if (Random.value < 0.1f && weaponPool.Count > 0)
            {
                int randIndex = Random.Range(0, weaponPool.Count);
                SpawnWeapon(position, weaponPool[randIndex]);
            }
            else if (itemPool.Count > 0)
            {
                int randIndex = Random.Range(0, itemPool.Count);
                SpawnItem(position, itemPool[randIndex]);
            }
        }
    }

    // ==========================================
    // 🔥 외부에서 확률을 조작할 수 있는 헬퍼 함수들
    // ==========================================

    // 피버 타임 발동! (예: FanMissionSystem에서 미션 성공 시 호출)
    public void SetFeverTime(float multiplier, float duration)
    {
        StartCoroutine(FeverTimeRoutine(multiplier, duration));
    }

    private IEnumerator FeverTimeRoutine(float multiplier, float duration)
    {
        dropMultiplier = multiplier;
        Debug.Log($"🔥 피버 타임 시작! 드랍률 {multiplier}배!");

        yield return new WaitForSeconds(duration);

        dropMultiplier = 1.0f; // 원상 복구
        Debug.Log("❄️ 피버 타임 종료.");
    }

    // 행운 아이템 획득! (예: PassiveItemSO에서 호출)
    public void AddBonusChance(float amount)
    {
        flatBonusChance += amount;
        Debug.Log($"🍀 행운 증가! 현재 고정 보너스: {flatBonusChance * 100}%");
    }
}
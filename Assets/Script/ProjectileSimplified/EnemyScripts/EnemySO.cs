using UnityEngine;

public enum EnemyAI_Type { Swarm, Ranged, Turret, Juggernaut, Ambusher, Nest }

[CreateAssetMenu(menuName = "Game/Enemy/Enemy SO")]
public class EnemySO : ScriptableObject
{
    [Header("Base Stats")]
    public EnemyAI_Type aiType;
    public float maxHp = 50f;
    public float baseMoveSpeed = 3.5f;

    public EnemyVisualSO visual; // ★ 새로 추가된 비주얼 데이터 슬롯

    [Header("NavMesh Settings")]
    public float stoppingDistance = 0.5f;
    public float acceleration = 8f;

    [Header("Combat & Targeting")]
    public float aggroRadius = 10f;       // 플레이어 인지 거리 (위치, 포대 등)
    public float attackRange = 1.5f;      // 기본 공격 사거리
    public float attackCooldown = 1f;
    public float damage = 10f;

    [Header("Ranged (원거리 카이팅형)")]
    public float minKitingDistance = 3f;  // 이보다 가까우면 도망
    public float maxKitingDistance = 7f;  // 이보다 멀면 추격
    public ProjectileSO projectileData;   // 발사할 산성 침 등

    [Header("Juggernaut (돌격형)")]
    public float dashTriggerRange = 5f;   // 돌진 시작 거리
    public float dashSpeed = 15f;         // 부아악! 속도
    public float dashDuration = 1f;       // 돌진 유지 시간
    public LayerMask wallLayer;           // 박치기할 벽 레이어

    [Header("Ambusher (잠복/위치형)")]
    public float enrageSpeedMult = 2.5f;  // 발작 시 속도 배율 (파바바박!)

    [Header("Nest (둥지형)")]
    public EnemySO spawnEnemySO;          // 뱉어낼 몬스터 데이터 (주로 Swarm)
    public float spawnInterval = 3f;      // 스폰 주기
    public int maxSpawnCount = 5;         // 최대 스폰 제한

    [Header("Loot & Drops (엘리트/보스 전용)")]
    [Tooltip("이 몬스터가 죽을 때 100% 확률로 확정 드랍할 아이템")]
    public ItemBaseSO guaranteedItemDrop;

    [Tooltip("이 몬스터가 죽을 때 100% 확률로 확정 드랍할 무기")]
    public PlayerShootingSO guaranteedWeaponDrop;
}

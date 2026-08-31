using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 최신 기획 기준의 클랜 데이터입니다.
/// 클랜은 맵 디자인/장애물 비주얼/몬스터 로스터/빌런 풀을 소유합니다.
/// 촬영장 테마(HUD/필터/팬 반응)는 ShootingThemeSO로 분리합니다.
/// </summary>
[CreateAssetMenu(fileName = "ClanDefinition", menuName = "MahoBattle/Clan Definition")]
public class ClanDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public string clanId;
    public string displayName;

    [TextArea]
    public string visualConcept;

    [TextArea]
    public string combatTendency;

    [Header("Map")]
    public List<MapBlock> mapBlockPool = new();
    public List<BattleObstacle> obstaclePool = new();
    public List<RoomDefinitionSO> roomPool = new();

    [Header("Monster Roster")]
    public List<MonsterDefinitionSO> normalMonsterPool = new();
    public List<MonsterDefinitionSO> eliteMonsterPool = new();
    public List<MonsterDefinitionSO> villainPool = new();
}

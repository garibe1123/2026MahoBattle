using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Rework/Clan Definition", fileName = "ClanDefinition")]
public class ClanDefinitionSO : ScriptableObject
{
    [Header("Identity")]
    public string clanName;

    [Header("Map Ownership")]
    public List<RoomDefinitionSO> combatRooms = new();
    public List<RoomDefinitionSO> eliteRooms = new();

    [Header("Monster Roster")]
    public List<MonsterDefinitionSO> normalMonsters = new();
    public List<MonsterDefinitionSO> eliteMonsters = new();
    public List<MonsterDefinitionSO> bossMonsters = new();
}

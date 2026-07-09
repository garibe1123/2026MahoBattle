using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewClanData", menuName = "Áö·Ú¸¶¹ý¼Ò³à/Clan Data")]
public class ClanDataSO : ScriptableObject
{
    public string clanName;
    public Color themeColor = Color.white;

    [Header("Small Modules (1x1)")]
    public List<MapModule> smallTop;
    public List<MapModule> smallBottom;
    public List<MapModule> smallLeft;
    public List<MapModule> smallRight;

    [Header("Medium Modules (2x2)")]
    public List<MapModule> mediumTop;
    public List<MapModule> mediumBottom;
    public List<MapModule> mediumLeft;
    public List<MapModule> mediumRight;

    [Header("Boss Special")]
    public List<MapModule> bossModules;
}

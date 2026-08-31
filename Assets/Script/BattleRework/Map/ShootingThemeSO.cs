using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ThemeRewardBias
{
    public EquipmentTag tag;
    [Min(0f)] public float weightBonus = 0.25f;
}

/// <summary>
/// 촬영장 테마는 맵 아트가 아니라 촬영 연출과 플레이 성향을 담당합니다.
/// 맵/몬스터 로스터는 ClanDefinitionSO가 담당하고,
/// ShootingTheme은 HUD/필터/팬 반응 + 보상 태그 가중치로 런의 빌드 방향을 살짝 유도합니다.
/// 강제 빌드가 아니라 Reward Weight만 조정합니다.
/// </summary>
[CreateAssetMenu(fileName = "ShootingTheme", menuName = "MahoBattle/Shooting Theme")]
public class ShootingThemeSO : ScriptableObject
{
    public string themeId;
    public string displayName;

    [Header("Presentation")]
    public Color hudAccentColor = Color.white;
    public Material postProcessMaterial;
    public AudioClip audienceReactionBank;

    [TextArea]
    public string fanReactionTone;

    [Header("Build / Reward Bias")]
    [Tooltip("이 촬영장에서 조금 더 자주 제시될 장비 태그입니다. 해당 태그 외 장비도 정상 출현합니다.")]
    public List<ThemeRewardBias> rewardBiases = new();

    public float GetRewardWeight(BattleEquipmentSO equipment)
    {
        if (equipment == null)
            return 0f;

        float weight = Mathf.Max(0.01f, equipment.baseRewardWeight);

        for (int i = 0; i < rewardBiases.Count; i++)
        {
            ThemeRewardBias bias = rewardBiases[i];
            if (bias == null || !equipment.HasTag(bias.tag))
                continue;

            weight += Mathf.Max(0f, bias.weightBonus);
        }

        return Mathf.Max(0.01f, weight);
    }
}

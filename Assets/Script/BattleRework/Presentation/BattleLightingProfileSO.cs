using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Reusable battle-lighting look.
/// Put authored assets under Resources/BattleLightingProfiles when a Room should
/// automatically select a specific look by roomId. When no asset matches,
/// BattleFieldCinematicDirector uses the built-in runtime default.
/// </summary>
[CreateAssetMenu(fileName = "BattleLightingProfile", menuName = "MahoBattle/Battle Lighting Profile")]
public sealed class BattleLightingProfileSO : ScriptableObject
{
    [Header("Identity / Room Binding")]
    public string profileId = "EastwardLikeDefault";
    [Tooltip("Optional RoomDefinitionSO.roomId values that should use this profile.")]
    public List<string> roomIds = new();

    [Header("World Light")]
    public Color worldLightColor = new(0.88f, 0.96f, 1.00f, 1f);
    [Range(0f, 1.5f)] public float worldLightStartIntensity = 0.18f;
    [Range(0f, 1.5f)] public float worldLightIdleIntensity = 0.52f;
    [Range(0f, 1.5f)] public float worldLightCombatIntensity = 0.78f;
    [Range(0f, 1.5f)] public float worldLightShowIntensity = 0.36f;
    [Min(0.05f)] public float worldLightRiseDuration = 0.72f;
    [Min(0.05f)] public float worldLightFallDuration = 0.50f;

    [Header("Volume Blend")]
    [Range(0f, 1f)] public float idleVolumeWeight = 0.45f;
    [Range(0f, 1f)] public float combatVolumeWeight = 1f;
    [Range(0f, 1f)] public float showVolumeWeight = 0.10f;
    [Min(0.1f)] public float volumeFadeSharpness = 4.8f;

    [Header("Color Adjustments")]
    [Tooltip("A very subtle cool-green filter keeps the field coherent without crushing sprite colors.")]
    public Color colorFilter = new(0.92f, 1.00f, 0.97f, 1f);
    [Range(-4f, 4f)] public float postExposure = 0.12f;
    [Range(-100f, 100f)] public float contrast = 14f;
    [Range(-180f, 180f)] public float hueShift = 0f;
    [Range(-100f, 100f)] public float saturation = -6f;

    [Header("Shadow / Midtone / Highlight Trackballs")]
    [Tooltip("RGB multiplier-like trackball value. Neutral is (1,1,1,0).")]
    public Vector4 shadows = new(0.82f, 1.02f, 1.06f, 0f);
    public Vector4 midtones = new(0.98f, 1.00f, 0.98f, 0f);
    public Vector4 highlights = new(1.08f, 1.02f, 0.88f, 0f);
    [Range(0f, 1f)] public float shadowsStart = 0f;
    [Range(0f, 1f)] public float shadowsEnd = 0.32f;
    [Range(0f, 1f)] public float highlightsStart = 0.55f;
    [Range(0f, 1f)] public float highlightsEnd = 1f;

    [Header("Split Toning")]
    public Color splitShadows = new(0.43f, 0.54f, 0.56f, 1f);
    public Color splitHighlights = new(0.58f, 0.52f, 0.43f, 1f);
    [Range(-100f, 100f)] public float splitBalance = 8f;

    [Header("Bloom")]
    [Range(0f, 5f)] public float bloomIntensity = 0.16f;
    [Range(0f, 10f)] public float bloomThreshold = 0.90f;
    [Range(0f, 1f)] public float bloomScatter = 0.58f;
    public Color bloomTint = new(1f, 0.94f, 0.80f, 1f);

    [Header("Vignette")]
    [Range(0f, 1f)] public float vignetteIntensity = 0.14f;
    [Range(0.01f, 1f)] public float vignetteSmoothness = 0.36f;
    public Color vignetteColor = new(0.015f, 0.035f, 0.04f, 1f);

    [Header("Tonemapping")]
    public TonemappingMode tonemappingMode = TonemappingMode.Neutral;

    [Header("Player Key Spotlight")]
    public Color playerPoolColor = new(1f, 0.88f, 0.63f, 1f);
    public Color playerKeyLightColor = new(1f, 0.86f, 0.58f, 1f);
    public Color playerTopLightColor = new(1f, 0.93f, 0.72f, 1f);
    [Range(0f, 1f)] public float playerPoolAlpha = 0.24f;
    [Min(0.1f)] public float playerPoolWidthMultiplier = 1.58f;
    [Range(0.05f, 0.55f)] public float playerPoolHeightRatio = 0.16f;
    [Range(0f, 1f)] public float playerKeyLightAlpha = 0.24f;
    [Min(0.2f)] public float playerKeyLightWidthMultiplier = 1.72f;
    [Min(0.2f)] public float playerKeyLightHeightMultiplier = 1.72f;
    [Range(-1f, 1f)] public float playerKeyLightVerticalOffsetRatio = 0.18f;
    [Range(0f, 0.35f)] public float playerTopLightStrength = 0.10f;
    [Range(0f, 1f)] public float playerCombatStrength = 1f;

    [Header("Enemy Contact Light")]
    public Color enemyPoolColor = new(0.60f, 0.78f, 0.86f, 1f);
    public Color enemyTopLightColor = new(0.75f, 0.88f, 0.92f, 1f);
    [Range(0f, 1f)] public float enemyPoolAlpha = 0.055f;
    [Min(0.1f)] public float enemyPoolWidthMultiplier = 1.12f;
    [Range(0.05f, 0.55f)] public float enemyPoolHeightRatio = 0.12f;
    [Range(0f, 0.35f)] public float enemyTopLightStrength = 0f;
    [Range(0f, 1f)] public float enemyCombatStrength = 0.52f;

    [Header("Presenter Stage Light")]
    public Color presenterPoolColor = new(1f, 0.82f, 0.55f, 1f);
    public Color presenterKeyLightColor = new(1f, 0.80f, 0.52f, 1f);
    public Color presenterTopLightColor = new(1f, 0.91f, 0.68f, 1f);
    [Range(0f, 1f)] public float presenterPoolAlpha = 0.26f;
    [Min(0.1f)] public float presenterPoolWidthMultiplier = 1.48f;
    [Range(0.05f, 0.55f)] public float presenterPoolHeightRatio = 0.16f;
    [Range(0f, 1f)] public float presenterKeyLightAlpha = 0.22f;
    [Min(0.2f)] public float presenterKeyLightWidthMultiplier = 1.58f;
    [Min(0.2f)] public float presenterKeyLightHeightMultiplier = 1.58f;
    [Range(-1f, 1f)] public float presenterKeyLightVerticalOffsetRatio = 0.15f;
    [Range(0f, 0.35f)] public float presenterTopLightStrength = 0.08f;

    [Header("Shared Character Fade")]
    [Min(0.1f)] public float characterLightFadeSharpness = 6.8f;

    public bool MatchesRoom(RoomDefinitionSO room)
    {
        if (room == null || roomIds == null || roomIds.Count == 0 || string.IsNullOrWhiteSpace(room.roomId))
            return false;

        for (int i = 0; i < roomIds.Count; i++)
        {
            if (string.Equals(roomIds[i], room.roomId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static BattleLightingProfileSO CreateRuntimeDefault()
    {
        BattleLightingProfileSO profile = CreateInstance<BattleLightingProfileSO>();
        profile.name = "BattleLightingProfile_RuntimeDefault";
        profile.hideFlags = HideFlags.HideAndDontSave;
        return profile;
    }
}

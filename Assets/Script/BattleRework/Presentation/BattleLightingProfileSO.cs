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
    public string profileId = "VividAnalogDefault";
    [Tooltip("Optional RoomDefinitionSO.roomId values that should use this profile.")]
    public List<string> roomIds = new();

    [Header("World Light")]
    [Tooltip("Near-neutral world light. The scene should feel bright and colorful, not color-filtered.")]
    public Color worldLightColor = new(1.00f, 0.985f, 0.96f, 1f);
    [Range(0f, 1.5f)] public float worldLightStartIntensity = 0.22f;
    [Range(0f, 1.5f)] public float worldLightIdleIntensity = 0.68f;
    [Range(0f, 1.5f)] public float worldLightCombatIntensity = 0.90f;
    [Range(0f, 1.5f)] public float worldLightShowIntensity = 0.38f;
    [Min(0.05f)] public float worldLightRiseDuration = 0.68f;
    [Min(0.05f)] public float worldLightFallDuration = 0.48f;

    [Header("Volume Blend")]
    [Range(0f, 1f)] public float idleVolumeWeight = 0.38f;
    [Range(0f, 1f)] public float combatVolumeWeight = 1f;
    [Range(0f, 1f)] public float showVolumeWeight = 0.05f;
    [Min(0.1f)] public float volumeFadeSharpness = 5.2f;

    [Header("Color Adjustments - Vivid / Palette Preserving")]
    [Tooltip("Keep this close to white. Punch comes from exposure, contrast and saturation instead of a strong tint.")]
    public Color colorFilter = Color.white;
    [Range(-4f, 4f)] public float postExposure = 0.18f;
    [Range(-100f, 100f)] public float contrast = 12f;
    [Range(-180f, 180f)] public float hueShift = 0f;
    [Range(-100f, 100f)] public float saturation = 10f;

    [Header("Shadow / Midtone / Highlight Trackballs")]
    public Vector4 shadows = new(1f, 1f, 1f, 0f);
    public Vector4 midtones = new(1f, 1f, 1f, 0f);
    public Vector4 highlights = new(1f, 1f, 1f, 0f);
    [Range(0f, 1f)] public float shadowsStart = 0f;
    [Range(0f, 1f)] public float shadowsEnd = 0.30f;
    [Range(0f, 1f)] public float highlightsStart = 0.58f;
    [Range(0f, 1f)] public float highlightsEnd = 1f;

    [Header("Split Toning")]
    public Color splitShadows = new(0.5f, 0.5f, 0.5f, 1f);
    public Color splitHighlights = new(0.5f, 0.5f, 0.5f, 1f);
    [Range(-100f, 100f)] public float splitBalance = 0f;

    [Header("Bloom")]
    [Range(0f, 5f)] public float bloomIntensity = 0.12f;
    [Range(0f, 10f)] public float bloomThreshold = 0.95f;
    [Range(0f, 1f)] public float bloomScatter = 0.52f;
    public Color bloomTint = Color.white;

    [Header("Vignette")]
    [Tooltip("Combat Volume에서 화면 구석만 살짝 눌러 중앙 전장을 읽기 쉽게 합니다.")]
    [Range(0f, 1f)] public float vignetteIntensity = 0.10f;
    [Range(0.01f, 1f)] public float vignetteSmoothness = 0.36f;
    public Color vignetteColor = Color.black;

    [Header("Tonemapping")]
    public TonemappingMode tonemappingMode = TonemappingMode.Neutral;

    [Header("Analog TV - Camera Distortion")]
    [Range(0f, 1f)] public float chromaticAberrationIntensity = 0.04f;
    [Range(-1f, 1f)] public float lensDistortionIntensity = -0.055f;
    [Range(0.01f, 5f)] public float lensDistortionScale = 1.015f;
    [Range(0f, 1f)] public float filmGrainIntensity = 0.055f;
    [Range(0f, 1f)] public float filmGrainResponse = 0.78f;

    [Header("Analog TV - Screen Surface")]
    [Range(0f, 1f)] public float analogOverlayStrength = 1f;
    [Range(0f, 0.25f)] public float scanlineStrength = 0.035f;
    [Range(1f, 12f)] public float scanlineSpacingPixels = 3f;
    [Range(0f, 0.15f)] public float analogNoiseStrength = 0.018f;
    [Range(0f, 0.15f)] public float rollingBandStrength = 0.018f;
    public Color analogOverlayTint = new(0.015f, 0.02f, 0.025f, 1f);

    [Header("Player Key Spotlight")]
    public Color playerPoolColor = new(1f, 0.90f, 0.72f, 1f);
    public Color playerKeyLightColor = new(1f, 0.94f, 0.80f, 1f);
    public Color playerTopLightColor = new(1f, 0.97f, 0.86f, 1f);
    [Range(0f, 1f)] public float playerPoolAlpha = 0.22f;
    [Min(0.1f)] public float playerPoolWidthMultiplier = 1.62f;
    [Range(0.05f, 0.55f)] public float playerPoolHeightRatio = 0.15f;
    [Range(0f, 1f)] public float playerKeyLightAlpha = 0.26f;
    [Min(0.2f)] public float playerKeyLightWidthMultiplier = 1.78f;
    [Min(0.2f)] public float playerKeyLightHeightMultiplier = 2.30f;
    [Range(-1f, 1f)] public float playerKeyLightVerticalOffsetRatio = 0.45f;
    [Range(0f, 0.35f)] public float playerTopLightStrength = 0.08f;
    [Range(0f, 1f)] public float playerCombatStrength = 1f;

    [Header("Enemy Contact Light")]
    public Color enemyPoolColor = new(0.92f, 0.95f, 1f, 1f);
    public Color enemyTopLightColor = Color.white;
    [Range(0f, 1f)] public float enemyPoolAlpha = 0.04f;
    [Min(0.1f)] public float enemyPoolWidthMultiplier = 1.10f;
    [Range(0.05f, 0.55f)] public float enemyPoolHeightRatio = 0.11f;
    [Range(0f, 0.35f)] public float enemyTopLightStrength = 0f;
    [Range(0f, 1f)] public float enemyCombatStrength = 0.45f;

    [Header("Presenter Stage Light")]
    public Color presenterPoolColor = new(1f, 0.84f, 0.60f, 1f);
    public Color presenterKeyLightColor = new(1f, 0.90f, 0.72f, 1f);
    public Color presenterTopLightColor = new(1f, 0.95f, 0.82f, 1f);
    [Range(0f, 1f)] public float presenterPoolAlpha = 0.24f;
    [Min(0.1f)] public float presenterPoolWidthMultiplier = 1.48f;
    [Range(0.05f, 0.55f)] public float presenterPoolHeightRatio = 0.15f;
    [Range(0f, 1f)] public float presenterKeyLightAlpha = 0.22f;
    [Min(0.2f)] public float presenterKeyLightWidthMultiplier = 1.58f;
    [Min(0.2f)] public float presenterKeyLightHeightMultiplier = 2.05f;
    [Range(-1f, 1f)] public float presenterKeyLightVerticalOffsetRatio = 0.38f;
    [Range(0f, 0.35f)] public float presenterTopLightStrength = 0.07f;

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

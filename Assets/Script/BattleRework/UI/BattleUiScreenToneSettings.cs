using System;
using UnityEngine;

[Serializable]
public sealed class BattleUiScreenToneSettings
{
    [Tooltip("Maximum dot radius in Canvas pixels, before PatternScale.")]
    [Min(0f)] public float DotSize = 5.7f;
    [Tooltip("Grid center distance in Canvas pixels, before PatternScale.")]
    [Min(0.1f)] public float DotSpacing = 8f;
    [Tooltip("Fraction of active cells in a fixed, time-independent grid.")]
    [Range(0f, 1f)] public float DotDensity = 1f;
    [Min(0.01f)] public float PatternScale = 1f;
    [Tooltip("Grid rotation in degrees.")]
    [Range(-180f, 180f)] public float PatternRotation = 15f;
    [Range(0f, 1f)] public float ToneStrength = 0.18f;
    public Color ToneColor = Color.black;
    [Range(0f, 1f)] public float VignetteStrength = 0.25f;
    [Tooltip("Start radius from screen center; 1 reaches the middle of the shorter edge.")]
    [Range(0f, 2f)] public float VignetteRadius = 0.65f;
    [Tooltip("Positive transition width in normalized radius units.")]
    [Min(0.001f)] public float VignetteSoftness = 0.6f;

    public void Apply(Material material, float canvasScale)
    {
        float rotation = PatternRotation * Mathf.Deg2Rad;
        material.SetVector("_ToneDots", new Vector4(Mathf.Max(0f, DotSize),
            Mathf.Max(0.1f, DotSpacing), Mathf.Clamp01(DotDensity), Mathf.Max(0.01f, PatternScale)));
        material.SetVector("_TonePattern", new Vector4(Mathf.Cos(rotation), Mathf.Sin(rotation),
            Mathf.Clamp01(ToneStrength), Mathf.Max(0.001f, canvasScale)));
        material.SetColor("_ToneColor", ToneColor);
        material.SetVector("_ToneVignette", new Vector4(Mathf.Clamp01(VignetteStrength),
            Mathf.Clamp(VignetteRadius, 0f, 2f), Mathf.Max(0.001f, VignetteSoftness), 0f));
    }
}

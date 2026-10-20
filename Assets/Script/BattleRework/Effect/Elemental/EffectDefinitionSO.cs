using System;
using System.Collections.Generic;
using UnityEngine;

public enum EffectPhase { None, Intro, Idle, End, Finished }
public enum EffectFamily { Gas, Liquid }
public enum EffectUsage { Standalone, AttachToProjectile, SpawnOnImpact, ModifyProjectile }
public enum EffectIdleEnd { LoopBoundary, Immediate }
public enum EffectShortLife { AccelerateIntroAndEnd, EndOnly }
public enum EffectIntroStop { Immediate, AfterIntro }
public enum EffectLightMode { Steady, Flicker }

[Serializable]
public sealed class EffectClipDefinition
{
    public bool useSheet = true;
    [Min(0)] public int startIndex;
    [Min(0)] public int frameCount = 4;
    [Min(0.01f)] public float fps = 12f;
    public Sprite[] sprites = Array.Empty<Sprite>();
    public float SafeFps => EffectNumbers.Positive(fps, 12f);
}

[CreateAssetMenu(menuName = "Game/Effect/Elemental Visual")]
public sealed class EffectDefinitionSO : ScriptableObject
{
    public ElementDefinitionSO element;
    public EffectFamily family;
    public EffectUsage usage;
    public ElementDefinitionSO[] additionalElements = Array.Empty<ElementDefinitionSO>();
    [Header("Sheet: indices run left to right, top to bottom")]
    public Sprite sourceSheet;
    public Vector2Int cellSize = new(32, 32);
    public Vector2Int layout = new(4, 4);
    public EffectClipDefinition intro = new() { startIndex = 0, frameCount = 4 };
    public EffectClipDefinition idle = new() { startIndex = 4, frameCount = 8 };
    public EffectClipDefinition end = new() { startIndex = 12, frameCount = 4 };
    [Header("Lifetime includes Intro and End; -1 waits for Stop")]
    public float duration = 2f;
    public EffectIdleEnd idleEnd;
    public EffectShortLife shortLife;
    public EffectIntroStop introStop;
    [Header("Material properties are applied only if supported")]
    public Material customMaterial;
    public Color tint = Color.white;
    public float introBrightness = 1.15f;
    public float idleBrightness = 1f;
    public float endBrightness = 0.65f;
    public bool fadeOnEnd;
    [Header("Procedural fire: pooled round particles share one surface")]
    public bool fireParticles;
    [Range(8, 96)] public int fireParticleCapacity = 48;
    [Min(0f)] public float fireEmissionRate = 32f;
    public Vector2 fireParticleLifetime = new(0.8f, 1.3f);
    public Vector2 fireParticleRadius = new(0.09f, 0.19f);
    [Min(0f)] public float fireSpread = 0.45f;
    [Range(0f, 0.9f)] public float fireFadeStart = 0.25f;
    [Min(0f)] public float fireSmokeSpread = 1.6f;
    [Min(0f)] public float fireSmokeExpansion = 0.65f;
    [Range(0f, 0.4f)] public float fireContourVariation = 0.22f;
    [Range(0.01f, 0.6f)] public float fireEdgeWidth = 0.28f;
    [Range(0f, 1f)] public float fireEdgeStrength = 0.85f;
    public Color fireHotColor = new(1f, 0.9f, 0.3f);
    public Color fireWarmColor = new(0.95f, 0.27f, 0.045f);
    public Color fireSootColor = new(0.065f, 0.055f, 0.075f);
    [ColorUsage(true, true)] public Color emissionColor = Color.white;
    [Min(0f)] public float emissionStrength = 1f;
    public string emissionColorProperty = "_EmissionColor";
    public string emissionStrengthProperty = "_Glow";
    public string lightResponseProperty = "_LightResponse";
    [Range(0f, 1f)] public float lightResponse = 1f;
    public string rimProperty = "_RimStrength";
    public float rimStrength;
    public string centerProperty = "_CenterStrength";
    public float centerStrength;
    [Header("Gas / trailing layers (1 = no fragments)")]
    [Range(1, 5)] public int pieceCount = 1;
    [Min(0f)] public float pieceDelay = 0.07f;
    [Range(0f, 1f)] public float pieceAttenuation = 0.75f;
    public Color tailTint = Color.white;
    public Color spentTint = Color.white;
    [Min(0f)] public float tailExpansion = 1f;
    [Min(0f)] public float edgeSpread = 0.1f;
    public float gasRise = 0.4f;
    public float gasExpansion = 0.4f;
    public Vector2 tailOffset = new(-0.15f, 0.1f);
    [Min(0f)] public float visualScale = 1f;
    [Header("Per-play motion and shape variation (0 = unchanged)")]
    [Min(0f)] public float motionWander;
    [Min(0.01f)] public float variationFrequency = 1.5f;
    [Range(0f, 0.5f)] public float sizeVariation;
    [Range(0f, 0.3f)] public float shapeVariation;
    [Header("Nearby blobs share one translucent surface")]
    public bool mergeNearby;
    [Range(0.2f, 1f)] public float mergeRadius = 0.7f;
    [Range(0.02f, 0.5f)] public float mergeSoftness = 0.18f;
    [Range(5f, 30f)] public float mergeRefreshRate = 20f;
    [Min(0.01f)] public float surfaceNoiseScale = 0.65f;
    [Range(0f, 1f)] public float surfaceNoiseStrength = 0.15f;
    [Min(0f)] public float surfaceNoiseSpeed = 0.08f;
    public Color surfaceBodyColor = Color.white;
    public Color surfaceHighlightColor = Color.white;
    [Range(0f, 1f)] public float surfaceHighlightStrength;
    [Range(0f, 2f)] public float surfaceRelief;
    public Color surfaceShadowColor = new(0.24f, 0.27f, 0.33f);
    public Vector3 surfaceLightDirection = new(-0.45f, 0.65f, 0.65f);
    [Range(0f, 2f)] public float surfaceSpecular;
    [Range(4f, 128f)] public float surfaceShininess = 48f;
    [Min(0.01f)] public float impactFoamDuration = 0.65f;
    [Range(0f, 2f)] public float impactFoamStrength;
    [Range(0f, 1f)] public float foamOpacity = 0.85f;
    [Header("Smoke layers at the end of the fire trail")]
    public EffectDefinitionSO smokeTail;
    [Range(1, 4)] public int smokeTailPieces = 2;
    [Header("Liquid flight; landing starts Intro")]
    [Min(0f)] public float airborneDuration = 0.25f;
    public float airborneHeight = 0.5f;
    public Vector2 puddleScale = new(1.4f, 0.7f);
    [Header("Independent local Light2D")]
    public bool useLight;
    public EffectLightMode lightMode;
    public Color lightColor = Color.white;
    [Min(0f)] public float lightIntensity = 1f;
    [Min(0f)] public float lightRadius = 2f;
    [Range(0f, 1f)] public float flickerAmount = 0.15f;
    [Min(0f)] public float flickerFrequency = 12f;
    public int sortingLayerId;
    public int sortingOrder = 20;
    [Header("Future integration data only")]
    public Vector2 attachmentOffset;
    public bool spawnOnImpact = true;
    public float projectileVisualScale = 1f;

    [NonSerialized] private Sprite[][] clips;
    [NonSerialized] private List<Sprite> ownedSprites;

    public Sprite[] GetFrames(EffectPhase phase)
    {
        if (clips == null)
        {
            if (!Application.isPlaying) ClearClips();
            ownedSprites ??= new List<Sprite>();
            clips = new[] { BuildFrames(intro), BuildFrames(idle), BuildFrames(end) };
        }
        int index = (int)phase - (int)EffectPhase.Intro;
        return index >= 0 && index < clips.Length ? clips[index] : Array.Empty<Sprite>();
    }

    public float GetFps(EffectPhase phase)
    {
        EffectClipDefinition clip = phase == EffectPhase.Intro ? intro : phase == EffectPhase.Idle ? idle : end;
        return clip != null ? clip.SafeFps : 12f;
    }

    private Sprite[] BuildFrames(EffectClipDefinition clip)
    {
        if (clip == null || clip.frameCount <= 0 || clip.startIndex < 0)
            return Array.Empty<Sprite>();
        var result = new List<Sprite>();
        if (!clip.useSheet)
        {
            if (clip.sprites == null || clip.startIndex >= clip.sprites.Length)
                return Array.Empty<Sprite>();
            int count = Math.Min(clip.frameCount, clip.sprites.Length - clip.startIndex);
            for (int i = 0; i < count; i++)
                if (clip.sprites[clip.startIndex + i] != null)
                    result.Add(clip.sprites[clip.startIndex + i]);
            return result.ToArray();
        }
        if (sourceSheet == null || sourceSheet.packed || cellSize.x <= 0 || cellSize.y <= 0 || layout.x <= 0 || layout.y <= 0)
            return Array.Empty<Sprite>();
        Rect rect = sourceSheet.rect;
        if ((long)layout.x * cellSize.x > rect.width || (long)layout.y * cellSize.y > rect.height)
            return Array.Empty<Sprite>();
        long available = (long)layout.x * layout.y - clip.startIndex;
        int frameCount = (int)Math.Min(clip.frameCount, Math.Max(0L, available));
        Vector2 pivot = new(sourceSheet.pivot.x / rect.width, sourceSheet.pivot.y / rect.height);
        for (int i = 0; i < frameCount; i++)
        {
            int index = clip.startIndex + i;
            Rect cell = new(rect.x + index % layout.x * cellSize.x,
                rect.yMax - (index / layout.x + 1) * cellSize.y, cellSize.x, cellSize.y);
            Sprite sprite = Sprite.Create(sourceSheet.texture, cell, pivot, sourceSheet.pixelsPerUnit,
                0, SpriteMeshType.FullRect);
            sprite.name = name + "_" + index;
            ownedSprites.Add(sprite);
            result.Add(sprite);
        }
        return result.ToArray();
    }

    private void OnDisable() { ClearClips(); }
    private void OnValidate() { clips = null; }

    private void ClearClips()
    {
        if (ownedSprites != null)
            foreach (Sprite sprite in ownedSprites)
                if (sprite != null)
                {
                    if (Application.isPlaying) Destroy(sprite);
                    else DestroyImmediate(sprite);
                }
        ownedSprites = null;
        clips = null;
    }
}

internal static class EffectNumbers
{
    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static float Positive(float value, float fallback) => Finite(value) && value > 0f ? value : fallback;
    public static float NonNegative(float value) => Finite(value) ? Mathf.Max(0f, value) : 0f;
}

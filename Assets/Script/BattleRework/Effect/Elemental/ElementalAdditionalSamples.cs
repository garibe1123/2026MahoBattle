using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ElementalAdditionalSamples : IDisposable
{
    public static readonly string[] Names =
        { "PoisonCharacter", "FrostCharacter", "BurnCharacter", "LightningStrike", "ElectricBeam", "LaserBeam", "FrostSpark" };
    public EffectDefinitionSO[] Definitions { get; } = new EffectDefinitionSO[7];
    private readonly List<UnityEngine.Object> owned = new();
    private readonly float pixelsPerUnit;

    public ElementalAdditionalSamples(EffectDefinitionSO fire)
    {
        pixelsPerUnit = fire != null && fire.sourceSheet != null ? fire.sourceSheet.pixelsPerUnit : ElementalTestSamples.PixelsPerUnit;
        Shader beamShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        Material beamMaterial = beamShader != null ? Own(new Material(beamShader) { name = "FieldBeamUnlit" }) : null;
        for (int i = 0; i < Definitions.Length; i++)
        {
            EffectDefinitionSO data = Own(i == 2 && fire != null ? UnityEngine.Object.Instantiate(fire) :
                ScriptableObject.CreateInstance<EffectDefinitionSO>());
            data.name = Names[i];
            data.duration = i == 3 ? 0.22f : i >= 4 && i <= 5 ? 0.5f : 0.6f;
            data.idleEnd = EffectIdleEnd.Immediate;
            data.sortingMode = i < 3 || i == 6 ? EffectSortMode.Attached : EffectSortMode.SpawnDepth;
            data.character = new EffectCharacterSettings();
            data.beam = new EffectBeamSettings();
            if (i != 2)
            {
                Sprite sprite = i == 0 ? null : CreateSprite(i);
                data.sourceSheet = sprite;
                data.intro = new EffectClipDefinition { useSheet = false, sprites = new[] { sprite }, fps = i >= 3 && i <= 5 ? 100f : 10f };
                data.idle = new EffectClipDefinition { useSheet = false, sprites = new[] { sprite }, fps = 100f };
                data.end = new EffectClipDefinition { useSheet = false, sprites = new[] { sprite }, fps = i >= 3 && i <= 5 ? 100f : 10f };
                data.fadeOnEnd = true;
                data.introBrightness = data.idleBrightness = data.endBrightness = 1f;
                data.sizeVariation = 0.08f;
                data.gasExpansion = 0f;
                data.mixedPieceSizes = false;
                data.tailOffset = Vector2.zero;
                data.gasRise = i < 3 || i == 6 ? 0.35f : 0f;
                data.embers.enabled = data.waterDetails.enabled = data.gasDetails.enabled = false;
                if (i == 0)
                {
                    data.family = EffectFamily.Gas;
                    data.pieceCount = 1;
                    data.shapeVariation = 0f;
                    data.motionWander = 0.06f;
                    Sprite[][] clips = CreatePoisonClips();
                    data.sourceSheet = clips[0][0];
                    data.intro.sprites = clips[0];
                    data.intro.fps = 24f;
                    data.idle.sprites = clips[1];
                    data.end.sprites = clips[2];
                    data.end.fps = 30f;
                }
            }
            if (i == 0) data.character.tint = new Color32(155, 221, 39, 255);
            if (i == 1 || i == 6) data.character.tint = new Color32(131, 223, 255, 255);
            if (i == 2)
            {
                data.character.tint = new Color32(255, 117, 56, 255);
                data.fireParticles = true;
                data.intro.fps = data.end.fps = 48f;
                data.character.particleSize = new Vector2(0.22f, 0.3f);
                data.useLight = false;
                data.embers = new EffectDetailSettings { enabled = false };
            }
            if (i >= 3 && i <= 5)
            {
                data.customMaterial = beamMaterial;
                data.beam.kind = i == 3 ? EffectBeamKind.LightningStrike : i == 4 ? EffectBeamKind.ElectricBeam : EffectBeamKind.Laser;
                data.sizeVariation = 0f;
                if (i == 5)
                {
                    data.beam.core = Color.white;
                    data.beam.edge = new Color32(255, 69, 96, 255);
                    data.beam.width = 0.18f;
                    data.beam.zigzag = 0f;
                }
            }
            Definitions[i] = data;
        }
        Definitions[1].character.sparkle = Definitions[6];
    }

    private T Own<T>(T value) where T : UnityEngine.Object
    {
        value.hideFlags = HideFlags.HideAndDontSave;
        owned.Add(value);
        return value;
    }

    private Sprite CreateSprite(int sample)
    {
        int size = sample == 6 ? 3 : 8;
        Texture2D texture = Own(new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = Names[sample] + "Pixels", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int center = size / 2;
                int dx = Mathf.Abs(x - center), dy = Mathf.Abs(y - center);
                Color color = Color.clear;
                if (sample == 1)
                {
                    bool ice = y >= 1 && y <= 7 && Mathf.Abs(x - 3) <= (7 - y) / 2;
                    ice |= y >= 1 && y <= 3 && x >= 4 && x <= 6;
                    ice |= y == 1 && x >= 1 && x <= 7;
                    if (ice) color = new Color32(131, 223, 255, 255);
                    if (ice && x == 3 && y >= 2 && y <= 6) color = Color.white;
                }
                else if (sample == 6)
                {
                    if (dx == 0 || dy == 0) color = new Color32(131, 223, 255, 255);
                    if (dx == 0 && dy == 0) color = Color.white;
                }
                else color = Color.white;
                pixels[y * size + x] = color;
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        Sprite sprite = Own(Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect));
        sprite.name = Names[sample] + "Sprite";
        return sprite;
    }

    private Sprite[][] CreatePoisonClips()
    {
        const int size = 10, count = 14;
        Texture2D texture = Own(new Texture2D(size * count, size, TextureFormat.RGBA32, false)
        { name = "PoisonGrowAndPop", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        Color32[] pixels = new Color32[size * size * count];
        Color core = new Color32(155, 221, 39, 255), edge = new Color32(53, 93, 24, 255);
        for (int frame = 0; frame < count; frame++)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 offset = new(x - 4.5f, y - 4f);
                    float distance = offset.magnitude;
                    Color color = Color.clear;
                    if (frame <= 4)
                    {
                        float radius = frame < 3 ? 1.1f + frame * 0.65f : 2.7f;
                        if (distance <= radius) color = distance < radius - 0.8f ? core : edge;
                        if (distance < radius && x == 4 && y == 5) color = new Color32(222, 255, 151, 255);
                        color.a *= 0.85f;
                    }
                    else
                    {
                        float t = (frame - 5f) / (count - 6f);
                        float radius = 2.8f + t * 0.7f;
                        // Only a broken lower crescent survives the opening of the bubble.
                        if (frame <= 7 && offset.y < 0.5f - t * 3f &&
                            distance > radius - 0.65f && distance < radius && x != 3)
                            color = Color.Lerp(core, edge, 0.35f);
                        for (int chip = 0; chip < 3; chip++)
                        {
                            float side = chip == 0 ? -1f : chip == 1 ? 1f : 0.2f;
                            int cx = Mathf.RoundToInt(4.5f + side * (2f + t * (chip + 1)));
                            int cy = Mathf.RoundToInt(5f + chip * 0.6f + t * (2f + chip));
                            if (x == cx && y == cy && t < 0.8f) color = chip == 2 ? edge : core;
                        }
                        color.a *= 0.65f * (1f - Mathf.SmoothStep(0f, 1f, t));
                    }
                    pixels[y * size * count + frame * size + x] = color;
                }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        Sprite[] frames = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = Own(Sprite.Create(texture, new Rect(i * size, 0, size, size), new Vector2(0.5f, 0.5f),
                pixelsPerUnit, 0, SpriteMeshType.FullRect));
            frames[i].name = "PoisonPop" + i;
        }
        return new[] { new[] { frames[0], frames[1], frames[2] }, new[] { frames[3], frames[4] },
            new[] { frames[5], frames[6], frames[7], frames[8], frames[9], frames[10], frames[11], frames[12], frames[13] } };
    }

    public void Dispose()
    {
        for (int i = owned.Count - 1; i >= 0; i--)
        {
            UnityEngine.Object value = owned[i];
            if (value == null || value.hideFlags != HideFlags.HideAndDontSave) continue;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
        owned.Clear();
    }
}

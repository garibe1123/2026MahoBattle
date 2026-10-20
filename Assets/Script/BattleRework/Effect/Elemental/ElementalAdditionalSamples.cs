using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ElementalAdditionalSamples : IDisposable
{
    public static readonly string[] Names =
        { "PoisonCharacter", "FrostCharacter", "BurnCharacter", "LightningStrike", "ElectricBeam", "LaserBeam", "FrostSpark" };
    public EffectDefinitionSO[] Definitions { get; } = new EffectDefinitionSO[7];
    private readonly List<UnityEngine.Object> owned = new();

    public ElementalAdditionalSamples(EffectDefinitionSO fire)
    {
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
                Sprite sprite = CreateSprite(i);
                data.sourceSheet = null;
                data.intro = new EffectClipDefinition { useSheet = false, sprites = new[] { sprite }, fps = i >= 3 && i <= 5 ? 100f : 10f };
                data.idle = new EffectClipDefinition { useSheet = false, sprites = new[] { sprite }, fps = 100f };
                data.end = new EffectClipDefinition { useSheet = false, sprites = new[] { sprite }, fps = i >= 3 && i <= 5 ? 100f : 10f };
                data.fadeOnEnd = true;
                data.introBrightness = data.idleBrightness = data.endBrightness = 1f;
                data.sizeVariation = 0.08f;
                data.gasRise = i < 3 || i == 6 ? 1.2f : 0f;
                data.gasExpansion = 0f;
                data.embers.enabled = data.waterDetails.enabled = data.gasDetails.enabled = false;
            }
            if (i == 0) data.character.tint = new Color32(155, 221, 39, 255);
            if (i == 1 || i == 6) data.character.tint = new Color32(131, 223, 255, 255);
            if (i == 2)
            {
                data.character.tint = new Color32(255, 117, 56, 255);
                data.visualScale = 1f;
                data.fireParticles = true;
                data.fireSourceCount = Vector2Int.one;
                data.fireSourceSpread = data.fireSpread = 0.1f;
                data.fireParticleCapacity = 8;
                data.fireEmissionRate = 14f;
                data.fireParticleLifetime = new Vector2(0.4f, 0.8f);
                data.fireParticleRadius = new Vector2(0.45f, 0.65f);
                data.useLight = false;
                data.embers = new EffectDetailSettings { enabled = false };
            }
            if (i >= 3 && i <= 5)
            {
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
        const int size = 16;
        Texture2D texture = Own(new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = Names[sample] + "Pixels", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int dx = Mathf.Abs(x - 7), dy = Mathf.Abs(y - 7);
                Color color = Color.clear;
                if (sample == 0)
                {
                    int distance = (x - 7) * (x - 7) + (y - 7) * (y - 7);
                    if (distance <= 43) color = new Color32(53, 93, 24, 255);
                    if (distance <= 24) color = new Color32(155, 221, 39, 255);
                    if (x >= 4 && x <= 6 && y >= 9 && y <= 10) color = new Color32(222, 255, 151, 255);
                }
                else if (sample == 1)
                {
                    bool ice = y >= 2 && y <= 14 && dx <= (14 - y) / 2 + 1;
                    ice |= y >= 2 && y <= 7 && x >= 9 && x <= 14 - y / 3;
                    if (ice) color = new Color32(131, 223, 255, 255);
                    if (ice && x >= 6 && x <= 8 && y >= 4 && y <= 11) color = Color.white;
                }
                else if (sample == 6)
                {
                    if ((dx <= 1 && dy <= 6) || (dy <= 1 && dx <= 6)) color = new Color32(131, 223, 255, 255);
                    if ((dx == 0 && dy <= 4) || (dy == 0 && dx <= 4)) color = Color.white;
                }
                else color = Color.white;
                pixels[y * size + x] = color;
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        Sprite sprite = Own(Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect));
        sprite.name = Names[sample] + "Sprite";
        return sprite;
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

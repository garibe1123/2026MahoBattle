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
                data.sourceSheet = null;
                data.intro = new EffectClipDefinition { useSheet = false, frameCount = i < 3 || i == 6 ? 3 : 1,
                    fps = i >= 3 && i <= 5 ? 100f : 24f };
                data.idle = new EffectClipDefinition { useSheet = false, frameCount = 1, fps = i >= 3 && i <= 5 ? 100f : 10f };
                data.end = new EffectClipDefinition { useSheet = false, frameCount = i == 0 ? 9 : i == 1 || i == 6 ? 4 : 1,
                    fps = i >= 3 && i <= 5 ? 100f : i == 0 ? 30f : 16f };
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
                    data.character.shape = EffectCharacterShape.PoisonBubble;
                }
            }
            if (i == 0) data.character.tint = new Color32(155, 221, 39, 255);
            if (i == 1 || i == 6)
            {
                data.character.tint = new Color32(131, 223, 255, 255);
                data.character.shape = i == 1 ? EffectCharacterShape.FrostShard : EffectCharacterShape.FrostSpark;
                data.character.shapeCore = data.character.shapeHighlight = Color.white;
                data.character.shapeEdge = data.character.tint;
                data.character.shapeAspect = i == 1 ? new Vector2(1f, 1.4f) : Vector2.one;
            }
            if (data.HasCharacterShape && fire != null) data.customMaterial = fire.customMaterial;
            if (i == 2)
            {
                data.character.tint = new Color32(255, 117, 56, 255);
                data.fireParticles = true;
                data.fireSourceCount = Vector2Int.one;
                data.fireSourceSpread = 0.04f;
                data.fireSourceScale = new Vector2(0.9f, 1.1f);
                data.fireParticleRadius = new Vector2(0.18f, 0.26f);
                data.fireShapeScale = new Vector2(0.75f, 1.8f);
                data.fireParticleLifetime = new Vector2(0.22f, 0.45f);
                data.fireFlameDuration = 0.82f;
                data.fireSmokeOpacity = 0.06f;
                data.gasRise = 1.1f;
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

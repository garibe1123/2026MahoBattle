using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class HarnessFireSettings
{
    private const string Menu = "Tools/Harness/Reset Selected Fire Settings";
    private static readonly HashSet<string> VisualSettings = new()
    {
        "tint", "visualScale", "gasRise", "gasExpansion", "visualCurves", "variationFrequency",
        "shapeVariation", "sizeVariation", "mergeSoftness", "pieceCount", "smokeTail",
        "motionWander", "lightColor", "lightMode", "lightIntensity", "lightRadius", "useLight",
        "flickerAmount", "flickerFrequency", "emissionColor", "emissionStrength"
    };

    [MenuItem(Menu)]
    private static void ResetSelected() => ResetDefinition(Selection.activeObject as EffectDefinitionSO);

    [MenuItem(Menu, true)]
    private static bool CanResetSelected() => !EditorApplication.isPlayingOrWillChangePlaymode &&
        Selection.activeObject is EffectDefinitionSO data && data.fireParticles;

    public static void ResetDefinition(EffectDefinitionSO data)
    {
        if (data == null || !data.fireParticles || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EffectDefinitionSO defaults = ScriptableObject.CreateInstance<EffectDefinitionSO>();
        defaults.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            ElementalTestSamples.Configure(defaults, 0);
            defaults.sizeVariation = EffectDefinitionSO.DefaultSizeVariation;
            Undo.RecordObject(data, "Restore Fire Settings");
            using (SerializedObject source = new(defaults))
            using (SerializedObject destination = new(data))
            {
                SerializedProperty property = source.GetIterator();
                bool enterChildren = true;
                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (property.name.StartsWith("fire", StringComparison.Ordinal) ||
                        property.name.StartsWith("ember", StringComparison.Ordinal) ||
                        VisualSettings.Contains(property.name))
                        destination.CopyFromSerializedProperty(property);
                }
                destination.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(data);
        }
        finally { UnityEngine.Object.DestroyImmediate(defaults); }
    }
}

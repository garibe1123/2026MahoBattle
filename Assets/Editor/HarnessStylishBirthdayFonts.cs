using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class HarnessStylishBirthdayFonts
{
    private const string Menu = "Tools/Harness/Apply Local Theme Fonts";
    private const string Licenses = "Assets/StreamingAssets/BattleUiFontLicenses";
    private const string PaperMenu = "Tools/Harness/Apply Paper Theme Defaults";

    [MenuItem(Menu, true)]
    private static bool CanApply() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(PaperMenu, true)]
    private static bool CanApplyPaper() => CanApply();

    [MenuItem(PaperMenu)]
    private static void ApplyPaperDefaults()
    {
        BattleUiStyleSettings defaults = new();
        foreach (BattleShowPresentationManager manager in UnityEngine.Object.FindObjectsByType<BattleShowPresentationManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (EditorUtility.IsPersistent(manager) || !manager.gameObject.scene.IsValid()) continue;
            SerializedObject serialized = new(manager);
            serialized.Update();
            SerializedProperty paper = serialized.FindProperty("paper");
            paper.FindPropertyRelative("background").colorValue = new Color32(0xF5, 0xEE, 0xDC, 255);
            paper.FindPropertyRelative("paperShadowOffset").vector2Value = defaults.paperShadowOffset;
            paper.FindPropertyRelative("paperShadow").colorValue = defaults.paperShadow;
            paper.FindPropertyRelative("paperPunchRadius").floatValue = defaults.paperPunchRadius;
            paper.FindPropertyRelative("paperPunchSpacing").floatValue = defaults.paperPunchSpacing;
            paper.FindPropertyRelative("paperPunchInset").floatValue = defaults.paperPunchInset;
            paper.FindPropertyRelative("paperPunchEdgeInset").floatValue = defaults.paperPunchEdgeInset;
            paper.FindPropertyRelative("paperGrainStrength").floatValue = defaults.paperGrainStrength;
            paper.FindPropertyRelative("paperStainStrength").floatValue = defaults.paperStainStrength;
            paper.FindPropertyRelative("paperInventoryColor").colorValue = defaults.paperInventoryColor;
            paper.FindPropertyRelative("paperDetailColor").colorValue = defaults.paperDetailColor;
            paper.FindPropertyRelative("paperPatternSpacing").floatValue = defaults.paperPatternSpacing;
            paper.FindPropertyRelative("paperPatternAlpha").floatValue = defaults.paperPatternAlpha;
            paper.FindPropertyRelative("paperLineWidth").floatValue = defaults.paperLineWidth;
            paper.FindPropertyRelative("paperDotRadius").floatValue = defaults.paperDotRadius;
            serialized.ApplyModifiedProperties();
            manager.SetUiStyle(manager.UiStyle);
        }
    }

    [MenuItem(Menu)]
    private static void Apply()
    {
        try
        {
            ApplyFamily("Assets/Resources/BattleUiFonts/galmuri", "Galmuri9", "galmuri");
            ApplyFamily("Assets/Resources/HarnessFonts/DoHyeon", "DoHyeon-Regular", "dohyeon");
            ApplyFamily("Assets/Resources/HarnessFonts/IBMPlexSansKR", "IBMPlexSansKR-Regular", "ibmplexsanskr");
            ApplyFamily("Assets/Resources/HarnessFonts/GowunBatang", "GowunBatang-Regular", "gowunbatang");
            ApplyFamily("Assets/Resources/HarnessFonts/BlackHanSans", "BlackHanSans-Regular", "blackhansans");
            ApplyFamily("Assets/Resources/HarnessFonts/Jua", "Jua-Regular", "jua");
            ApplyFamily("Assets/Resources/HarnessFonts/NanumPenScript", "NanumPenScript-Regular", "nanumpenscript");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CreatePixelTmpFont();
            AssetDatabase.SaveAssets();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Font>(
                "Assets/Resources/HarnessFonts/IBMPlexSansKR/IBMPlexSansKR-Regular.ttf");
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog("Local UI Fonts", exception.Message, "OK");
        }
    }

    private static void ApplyFamily(string directory, string filename, string family)
    {
        string sourcePath = directory + "/" + filename + ".ttf";
        string licensePath = directory + "/OFL.txt";
        if (!File.Exists(sourcePath) || !File.Exists(licensePath))
            throw new FileNotFoundException("Local font or OFL license missing: " + directory);
        if (!File.ReadAllText(licensePath).Contains("SIL OPEN FONT LICENSE Version 1.1"))
            throw new InvalidDataException("Missing SIL OFL 1.1 license: " + family);
        Directory.CreateDirectory(Licenses);
        File.Copy(licensePath, Licenses + "/" + family + "-OFL.txt", true);
    }

    private static void CreatePixelTmpFont()
    {
        const string path = "Assets/Resources/BattleUiFonts/galmuri/Galmuri9 SDF.asset";
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) != null) return;
        Font source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/BattleUiFonts/galmuri/Galmuri9.ttf");
        if (source == null) throw new InvalidDataException("Galmuri9 import failed.");
        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA,
            1024, 1024, AtlasPopulationMode.Dynamic, true);
        if (asset == null) throw new InvalidDataException("Galmuri9 TMP creation failed.");
        asset.name = "Galmuri9 SDF";
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        foreach (Texture2D atlas in asset.atlasTextures)
            if (atlas != null) AssetDatabase.AddObjectToAsset(atlas, asset);
        EditorUtility.SetDirty(asset);
    }
}

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class HarnessStylishBirthdayFonts
{
    private const string Menu = "Tools/Harness/Download and Apply Stylish Birthday TMP Fonts";
    private const string ThemeMenu = "Tools/Harness/Download and Apply All Theme Fonts";
    private const string Root = "Assets/Resources/BattleUiFonts";
    private static bool downloading;

    [MenuItem(Menu, true)]
    [MenuItem(ThemeMenu, true)]
    private static bool CanDownload() => !downloading && !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(Menu)]
    [MenuItem(ThemeMenu)]
    private static async void Download()
    {
        downloading = true;
        try
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                EditorUtility.DisplayProgressBar("UI Fonts", "Galmuri9 (SIL OFL 1.1)", 0.05f);
                await DownloadFamily(client, "galmuri", "Galmuri9.ttf", null,
                    "https://raw.githubusercontent.com/quiple/galmuri/main/dist/", "LICENSE.txt");
                EditorUtility.DisplayProgressBar("UI Fonts", "Do Hyeon (SIL OFL 1.1)", 0.2f);
                await DownloadFamily(client, "dohyeon", "DoHyeon-Regular.ttf", null);
                EditorUtility.DisplayProgressBar("UI Fonts", "Gothic A1 (SIL OFL 1.1)", 0.35f);
                await DownloadFamily(client, "gothica1", "GothicA1-Regular.ttf", null);
                EditorUtility.DisplayProgressBar("UI Fonts", "Gowun Dodum (SIL OFL 1.1)", 0.5f);
                await DownloadFamily(client, "gowundodum", "GowunDodum-Regular.ttf", null);
                EditorUtility.DisplayProgressBar("UI Fonts", "Black Han Sans (SIL OFL 1.1)", 0.7f);
                await DownloadFamily(client, "blackhansans", "BlackHanSans-Regular.ttf", "Stylish SDF");
                EditorUtility.DisplayProgressBar("UI Fonts", "Jua (SIL OFL 1.1)", 0.85f);
                await DownloadFamily(client, "jua", "Jua-Regular.ttf", "Birthday SDF");
            }
            AssetDatabase.SaveAssets();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/Birthday SDF.asset");
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog("UI Font Download", exception.Message, "OK");
        }
        finally
        {
            downloading = false;
            EditorUtility.ClearProgressBar();
        }
    }

    private static async Task DownloadFamily(HttpClient client, string family, string filename, string assetName,
        string baseUrl = null, string licenseFilename = "OFL.txt")
    {
        string directory = Root + "/" + family;
        string sourcePath = directory + "/" + filename;
        string licensePath = directory + "/OFL.txt";
        if (!File.Exists(sourcePath) || !File.Exists(licensePath))
        {
            string url = baseUrl ?? "https://raw.githubusercontent.com/google/fonts/main/ofl/" + family + "/";
            byte[] data = await client.GetByteArrayAsync(url + filename);
            string license = await client.GetStringAsync(url + licenseFilename);
            if (data.Length < 4 || data[0] != 0 || data[1] != 1 || data[2] != 0 || data[3] != 0 ||
                !license.Contains("SIL OPEN FONT LICENSE Version 1.1"))
                throw new InvalidDataException("Invalid font or missing SIL OFL 1.1 license: " + family);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(sourcePath, data);
            File.WriteAllText(licensePath, license);
        }
        string licenses = "Assets/StreamingAssets/BattleUiFontLicenses";
        Directory.CreateDirectory(licenses);
        File.Copy(licensePath, licenses + "/" + family + "-OFL.txt", true);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (assetName == null) return;
        string assetPath = Root + "/" + assetName + ".asset";
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath) != null) return;
        Font source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
        if (source == null) throw new InvalidDataException("Font import failed: " + sourcePath);
        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA,
            1024, 1024, AtlasPopulationMode.Dynamic, true);
        if (asset == null) throw new InvalidDataException("TMP font creation failed: " + sourcePath);
        asset.name = assetName;
        asset.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 /[]!?+-:");
        AssetDatabase.CreateAsset(asset, assetPath);
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        foreach (Texture2D atlas in asset.atlasTextures)
            if (atlas != null) AssetDatabase.AddObjectToAsset(atlas, asset);
        EditorUtility.SetDirty(asset);
    }
}

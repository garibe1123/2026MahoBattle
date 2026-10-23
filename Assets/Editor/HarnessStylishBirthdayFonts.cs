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
    private const string Root = "Assets/Resources/BattleUiFonts";
    private static bool downloading;

    [MenuItem(Menu, true)]
    private static bool CanDownload() => !downloading && !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(Menu)]
    private static async void Download()
    {
        downloading = true;
        try
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                EditorUtility.DisplayProgressBar("UI Fonts", "Black Han Sans (SIL OFL 1.1)", 0.1f);
                await DownloadFamily(client, "blackhansans", "BlackHanSans-Regular.ttf", "Stylish SDF");
                EditorUtility.DisplayProgressBar("UI Fonts", "Jua (SIL OFL 1.1)", 0.6f);
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

    private static async Task DownloadFamily(HttpClient client, string family, string filename, string assetName)
    {
        string directory = Root + "/" + family;
        string sourcePath = directory + "/" + filename;
        string licensePath = directory + "/OFL.txt";
        if (!File.Exists(sourcePath) || !File.Exists(licensePath))
        {
            string url = "https://raw.githubusercontent.com/google/fonts/main/ofl/" + family + "/";
            byte[] data = await client.GetByteArrayAsync(url + filename);
            string license = await client.GetStringAsync(url + "OFL.txt");
            if (data.Length < 4 || data[0] != 0 || data[1] != 1 || data[2] != 0 || data[3] != 0 ||
                !license.Contains("SIL OPEN FONT LICENSE Version 1.1"))
                throw new InvalidDataException("Invalid font or missing SIL OFL 1.1 license: " + family);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(sourcePath, data);
            File.WriteAllText(licensePath, license);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
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

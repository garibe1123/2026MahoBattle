using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

internal static class BattleUiThemeFonts
{
    private static readonly Dictionary<BattleUiStyle, Font> fonts = new();
    private static readonly Dictionary<BattleUiStyle, TMP_FontAsset> tmpFonts = new();
    private static readonly Dictionary<Font, TMP_FontAsset> cmdFonts = new();
    private static readonly Dictionary<string, TMP_FontAsset> posterFonts = new();
    private static bool retroShopFontLogged;

    static BattleUiThemeFonts()
    {
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Reset;
        UnityEditor.EditorApplication.quitting += Reset;
#else
        Application.quitting += Reset;
#endif
    }

    private static void Reset()
    {
        var generated = new HashSet<TMP_FontAsset>(cmdFonts.Values);
        generated.UnionWith(posterFonts.Values);
        cmdFonts.Clear();
        posterFonts.Clear();
        fonts.Clear();
        tmpFonts.Clear();
        retroShopFontLogged = false;
        // TMP_FontAsset.OnDestroy owns its atlas textures and material.
        foreach (TMP_FontAsset font in generated) BattleUiCmdFrame.Release(font);
    }

    internal static bool IsValid(TMP_FontAsset font) => IsValid(font, new HashSet<TMP_FontAsset>());

    private static bool IsValid(TMP_FontAsset font, HashSet<TMP_FontAsset> visited)
    {
        if (font == null) return false;
        if (!visited.Add(font)) return true;
        Texture2D[] atlases = font.atlasTextures;
        if (font.material == null || font.material.mainTexture == null ||
            atlases == null || atlases.Length < font.atlasTextureCount) return false;
        for (int i = 0; i < font.atlasTextureCount; i++)
            if (atlases[i] == null || (font.atlasPopulationMode != AtlasPopulationMode.Static && !atlases[i].isReadable))
                return false;
        if (font.fallbackFontAssetTable != null)
            foreach (TMP_FontAsset fallback in font.fallbackFontAssetTable)
                if (!IsValid(fallback, visited)) return false;
        return true;
    }

    internal static void ProtectGeneratedAtlases()
    {
        foreach (TMP_FontAsset font in cmdFonts.Values) ProtectAtlases(font);
        foreach (TMP_FontAsset font in posterFonts.Values) ProtectAtlases(font);
    }

    private static void ProtectAtlases(TMP_FontAsset font)
    {
        if (font == null || font.atlasTextures == null) return;
        foreach (Texture2D atlas in font.atlasTextures)
            if (atlas != null) atlas.hideFlags = HideFlags.HideAndDontSave;
    }

    private static void ReplaceGenerated(TMP_FontAsset previous, TMP_FontAsset replacement)
    {
        ProtectAtlases(replacement);
        if (ReferenceEquals(previous, null)) return;
        foreach (BattleUiStyle style in new List<BattleUiStyle>(tmpFonts.Keys))
            if (ReferenceEquals(tmpFonts[style], previous)) tmpFonts[style] = replacement;
        var generated = new HashSet<TMP_FontAsset>(cmdFonts.Values);
        generated.UnionWith(posterFonts.Values);
        foreach (TMP_FontAsset font in generated)
        {
            if (font == null || font.fallbackFontAssetTable == null) continue;
            for (int i = font.fallbackFontAssetTable.Count - 1; i >= 0; i--)
                if (ReferenceEquals(font.fallbackFontAssetTable[i], previous))
                {
                    if (replacement != null && replacement != font) font.fallbackFontAssetTable[i] = replacement;
                    else font.fallbackFontAssetTable.RemoveAt(i);
                }
        }
        foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (!text.gameObject.scene.IsValid()) continue;
            if (ReferenceEquals(text.font, previous)) text.font = replacement;
            text.SetAllDirty();
        }
        BattleUiCmdFrame.Release(previous);
    }

    internal static TMP_FontAsset LoadTmp(BattleUiStyle style)
    {
        if (tmpFonts.TryGetValue(style, out TMP_FontAsset font) && IsValid(font) &&
            style != BattleUiStyle.CMD && style != BattleUiStyle.JapanTvUi) return font;
        if (style == BattleUiStyle.RetroJapanShop)
        {
            font = Resources.Load<TMP_FontAsset>("HarnessFonts/DoHyeon/DoHyeon-Regular SDF");
            if (!SupportsRetroShopTmp(font, "한글"))
                font = Resources.Load<TMP_FontAsset>("HarnessFonts/IBMPlexSansKR/IBMPlexSansKR-Regular SDF");
            if (!IsValid(font)) font = null;
            tmpFonts[style] = font;
            if (!retroShopFontLogged)
            {
                retroShopFontLogged = true;
                Debug.Log("[Harness] 판촉 글꼴 연결: 기본=" + (font != null ? font.name : "없음") +
                    ", 한글 검사=" + (SupportsRetroShopTmp(font, "한글") ? "통과" : "실패") + ", 저장된 글꼴 사용");
            }
            return font;
        }
        if (style == BattleUiStyle.JapanTvUi)
        {
            font = LoadJapanTvTmp("HarnessFonts/BlackHanSans/BlackHanSans-Regular");
            TMP_FontAsset korean = LoadJapanTvTmp("HarnessFonts/IBMPlexSansKR/IBMPlexSansKR-Regular");
            if (font == null) font = korean;
            if (font != null && korean != null && font != korean && !font.fallbackFontAssetTable.Contains(korean))
                font.fallbackFontAssetTable.Add(korean);
            tmpFonts[style] = font;
            return font;
        }
        if (style == BattleUiStyle.CMD)
        {
            font = CreateCmdTmp(Load(BattleUiStyle.CMD));
            TMP_FontAsset korean = CreateCmdTmp(Load(BattleUiStyle.VectorStyleUi));
            if (font == null) font = korean;
            if (font != null && korean != null && font != korean && !font.fallbackFontAssetTable.Contains(korean))
                font.fallbackFontAssetTable.Add(korean);
            tmpFonts[style] = font;
            return font;
        }
        string path = style switch
        {
            BattleUiStyle.pixelStyleUi => "BattleUiFonts/galmuri/Galmuri9 SDF",
            BattleUiStyle.comicStyleUi => "HarnessFonts/DoHyeon/DoHyeon-Regular SDF",
            BattleUiStyle.VectorStyleUi => "HarnessFonts/IBMPlexSansKR/IBMPlexSansKR-Regular SDF",
            BattleUiStyle.MahoShojo => "HarnessFonts/GowunBatang/GowunBatang-Regular SDF",
            BattleUiStyle.Stylish => "HarnessFonts/BlackHanSans/BlackHanSans-Regular SDF",
            BattleUiStyle.Birthday => "HarnessFonts/Jua/Jua-Regular SDF",
            BattleUiStyle.Paper => "HarnessFonts/NanumPenScript/NanumPenScript-Regular SDF",
            BattleUiStyle.JapanTvUi => "HarnessFonts/BlackHanSans/BlackHanSans-Regular SDF",
            _ => null
        };
        bool poster = style == BattleUiStyle.comicStyleUi || style == BattleUiStyle.Stylish;
        font = poster ? LoadPosterTmp(path) : path != null ? Resources.Load<TMP_FontAsset>(path) : null;
        if (poster) AddPosterFallbacks(font);
        if (style == BattleUiStyle.Paper && font != null)
        {
            TMP_FontAsset fallback = Resources.Load<TMP_FontAsset>("HarnessFonts/BlackHanSans/BlackHanSans-Regular SDF");
            if (fallback != null && fallback != font)
            {
                if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!font.fallbackFontAssetTable.Contains(fallback)) font.fallbackFontAssetTable.Add(fallback);
            }
        }
        if (font == null && (style == BattleUiStyle.Stylish || style == BattleUiStyle.Birthday))
            font = BattleUiStyleTmpTitle.LoadFont(style == BattleUiStyle.Birthday);
        tmpFonts[style] = font;
        return font;
    }

    internal static TMP_FontAsset LoadTitle(BattleUiStyle style)
    {
        TMP_FontAsset font = LoadPosterTmp(style == BattleUiStyle.comicStyleUi
            ? "HarnessFonts/BlackHanSans/BlackHanSans-Regular SDF"
            : "HarnessFonts/Jua/Jua-Regular SDF");
        AddPosterFallbacks(font);
        return font != null ? font : LoadTmp(style);
    }

    internal static TMP_FontAsset LoadJapanTvDecoration() => LoadJapanTvTmp("HarnessFonts/NotoSansJP/NotoSansJP-Variable");

    internal static bool SupportsRetroShopTmp(TMP_FontAsset font, string content)
    {
        if (!IsValid(font)) return false;
        if (string.IsNullOrEmpty(content)) return true;
        foreach (char character in content)
            if (!char.IsControl(character) && !char.IsWhiteSpace(character) && !font.HasCharacter(character, false, true))
                return false;
        return true;
    }

    internal static TMP_FontAsset ResolveRetroShopTmp(TMP_FontAsset preferred, string content)
    {
        TMP_FontAsset fallback = LoadTmp(BattleUiStyle.RetroJapanShop);
        if (SupportsRetroShopTmp(fallback, content)) return fallback;
        TMP_FontAsset korean = Resources.Load<TMP_FontAsset>("HarnessFonts/IBMPlexSansKR/IBMPlexSansKR-Regular SDF");
        if (SupportsRetroShopTmp(korean, content)) return korean;
        TMP_FontAsset kanji = Resources.Load<TMP_FontAsset>("HarnessFonts/NotoSansJP/NotoSansJP-Variable SDF");
        if (SupportsRetroShopTmp(kanji, content)) return kanji;
        return SupportsRetroShopTmp(preferred, content) ? preferred : null;
    }

    private static TMP_FontAsset LoadJapanTvTmp(string path)
    {
        string key = "JapanTvUi:" + path;
        if (posterFonts.TryGetValue(key, out TMP_FontAsset font) && IsValid(font)) return font;
        Font source = Resources.Load<Font>(path);
        TMP_FontAsset asset = Resources.Load<TMP_FontAsset>(path + " SDF");
        if (source == null && asset != null) source = asset.sourceFontFile;
        return CreateJapanTvTmp(key, source);
    }

    private static TMP_FontAsset CreateJapanTvTmp(string key, Font source)
    {
        if (posterFonts.TryGetValue(key, out TMP_FontAsset font) && IsValid(font)) return font;
        TMP_FontAsset previous = font;
        font = null;
        if (source != null)
        {
            font = TMP_FontAsset.CreateFontAsset(source, 90, 18, GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (font != null)
            {
                font.name = source.name + " JapanTvUi Runtime SDF";
                font.hideFlags = HideFlags.HideAndDontSave;
                font.material.hideFlags = HideFlags.HideAndDontSave;
                font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            }
        }
        posterFonts[key] = font;
        ReplaceGenerated(previous, font);
        return font;
    }

    internal static TMP_FontAsset ResolveJapanTvTmp(TMP_FontAsset preferred, string content)
    {
        TMP_FontAsset fallback = LoadTmp(BattleUiStyle.JapanTvUi);
        if (preferred != null && preferred.sourceFontFile != null && (!IsValid(preferred) || !preferred.name.EndsWith(" JapanTvUi Runtime SDF")))
        {
            string key = "JapanTvUi:" + preferred.sourceFontFile.GetInstanceID();
            foreach (var entry in posterFonts)
                if (ReferenceEquals(entry.Value, preferred)) { key = entry.Key; break; }
            preferred = CreateJapanTvTmp(key, preferred.sourceFontFile);
        }
        if (!IsValid(preferred)) preferred = null;
        if (SupportsCmdTmp(preferred, content)) return preferred;
        return SupportsCmdTmp(fallback, content) ? fallback : preferred != null ? preferred : fallback;
    }

    private static TMP_FontAsset LoadPosterTmp(string path)
    {
        if (path == null) return null;
        if (posterFonts.TryGetValue(path, out TMP_FontAsset font) && IsValid(font)) return font;
        TMP_FontAsset previous = font;
        TMP_FontAsset source = Resources.Load<TMP_FontAsset>(path);
        font = source != null && source.sourceFontFile != null
            ? TMP_FontAsset.CreateFontAsset(source.sourceFontFile, 90, 12, GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic, true) : null;
        if (font == null)
        {
            posterFonts.Remove(path);
            ReplaceGenerated(previous, null);
            return null;
        }
        font.name = source.name + " Poster Runtime";
        font.hideFlags = HideFlags.HideAndDontSave;
        font.material.hideFlags = HideFlags.HideAndDontSave;
        font.fallbackFontAssetTable = new List<TMP_FontAsset>();
        posterFonts[path] = font;
        ReplaceGenerated(previous, font);
        return font;
    }

    private static void AddPosterFallbacks(TMP_FontAsset font)
    {
        if (font == null) return;
        TMP_FontAsset korean = LoadPosterTmp("HarnessFonts/IBMPlexSansKR/IBMPlexSansKR-Regular SDF");
        if (korean != null && korean != font && !font.fallbackFontAssetTable.Contains(korean))
            font.fallbackFontAssetTable.Add(korean);
        TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;
        if (fallback != null && fallback != font && !font.fallbackFontAssetTable.Contains(fallback))
            font.fallbackFontAssetTable.Add(fallback);
    }

    internal static TMP_FontAsset ResolveCmdTmp(TMP_FontAsset preferred, string content)
    {
        TMP_FontAsset fallback = LoadTmp(BattleUiStyle.CMD);
        // Rebuild CMD atlases from source fonts; serialized SDF tables contain merge conflicts.
        if (preferred != null && preferred.sourceFontFile != null)
            preferred = CreateCmdTmp(preferred.sourceFontFile);
        if (!IsValid(preferred)) preferred = null;
        if (SupportsCmdTmp(preferred, content)) return preferred;
        if (SupportsCmdTmp(fallback, content)) return fallback;
        return preferred != null ? preferred : fallback != null ? fallback : IsValid(TMP_Settings.defaultFontAsset) ? TMP_Settings.defaultFontAsset : null;
    }

    internal static bool SupportsCmdTmp(TMP_FontAsset font, string content)
    {
        if (!IsValid(font)) return false;
        if (string.IsNullOrEmpty(content)) return true;
        foreach (char character in content)
            if (!char.IsControl(character) && !SupportsCharacter(font, character, new HashSet<TMP_FontAsset>())) return false;
        ProtectGeneratedAtlases();
        return true;
    }

    private static bool SupportsCharacter(TMP_FontAsset font, char character, HashSet<TMP_FontAsset> visited)
    {
        if (!IsValid(font) || !visited.Add(font)) return false;
        if (font.HasCharacter(character, false, true)) return true;
        if (font.fallbackFontAssetTable != null)
            foreach (TMP_FontAsset fallback in font.fallbackFontAssetTable)
                if (SupportsCharacter(fallback, character, visited)) return true;
        if (TMP_Settings.fallbackFontAssets != null)
            foreach (TMP_FontAsset fallback in TMP_Settings.fallbackFontAssets)
                if (SupportsCharacter(fallback, character, visited)) return true;
        return SupportsCharacter(TMP_Settings.defaultFontAsset, character, visited);
    }

    private static TMP_FontAsset CreateCmdTmp(Font source)
    {
        if (source == null) return null;
        if (cmdFonts.TryGetValue(source, out TMP_FontAsset font) && IsValid(font)) return font;
        TMP_FontAsset previous = font;
        font = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA,
            1024, 1024, AtlasPopulationMode.Dynamic, true);
        if (font == null)
        {
            cmdFonts.Remove(source);
            ReplaceGenerated(previous, null);
            return null;
        }
        font.name = source.name + " CMD Runtime SDF";
        font.hideFlags = HideFlags.HideAndDontSave;
        font.material.hideFlags = HideFlags.HideAndDontSave;
        font.fallbackFontAssetTable = new List<TMP_FontAsset>();
        cmdFonts[source] = font;
        ReplaceGenerated(previous, font);
        return font;
    }

    internal static Font Load(BattleUiStyle style)
    {
        if (fonts.TryGetValue(style, out Font font) && font != null) return font;
        string path = style switch
        {
            BattleUiStyle.pixelStyleUi => "BattleUiFonts/galmuri/Galmuri9",
            BattleUiStyle.comicStyleUi => "HarnessFonts/DoHyeon/DoHyeon-Regular",
            BattleUiStyle.VectorStyleUi => "HarnessFonts/IBMPlexSansKR/IBMPlexSansKR-Regular",
            BattleUiStyle.MahoShojo => "HarnessFonts/GowunBatang/GowunBatang-Regular",
            BattleUiStyle.Stylish => "HarnessFonts/BlackHanSans/BlackHanSans-Regular",
            BattleUiStyle.Birthday => "HarnessFonts/Jua/Jua-Regular",
            BattleUiStyle.Paper => "HarnessFonts/BlackHanSans/BlackHanSans-Regular",
            BattleUiStyle.CMD => "BattleUiFonts/galmuri/Galmuri9",
            BattleUiStyle.JapanTvUi => "HarnessFonts/BlackHanSans/BlackHanSans-Regular",
            BattleUiStyle.RetroJapanShop => "HarnessFonts/BlackHanSans/BlackHanSans-Regular",
            _ => null
        };
        font = path != null ? Resources.Load<Font>(path) : null;
        if (font == null && style == BattleUiStyle.CMD) font = Load(BattleUiStyle.VectorStyleUi);
        if (font == null)
        {
            string legacyPath = style switch
            {
                BattleUiStyle.comicStyleUi => "dohyeon/DoHyeon-Regular",
                BattleUiStyle.Stylish => "blackhansans/BlackHanSans-Regular",
                BattleUiStyle.Birthday => "jua/Jua-Regular",
                _ => null
            };
            if (legacyPath != null) font = Resources.Load<Font>("BattleUiFonts/" + legacyPath);
        }
        fonts[style] = font;
        return font;
    }
}

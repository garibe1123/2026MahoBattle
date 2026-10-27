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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        foreach (TMP_FontAsset font in cmdFonts.Values)
        {
            if (font == null) continue;
            foreach (Texture2D atlas in font.atlasTextures)
                if (atlas != null) BattleUiCmdFrame.Release(atlas);
            if (font.material != null) BattleUiCmdFrame.Release(font.material);
            BattleUiCmdFrame.Release(font);
        }
        cmdFonts.Clear();
        foreach (TMP_FontAsset font in posterFonts.Values)
        {
            if (font == null) continue;
            foreach (Texture2D atlas in font.atlasTextures)
                if (atlas != null) BattleUiCmdFrame.Release(atlas);
            if (font.material != null) BattleUiCmdFrame.Release(font.material);
            BattleUiCmdFrame.Release(font);
        }
        posterFonts.Clear();
        fonts.Clear();
        tmpFonts.Clear();
    }

    internal static TMP_FontAsset LoadTmp(BattleUiStyle style)
    {
        if (tmpFonts.TryGetValue(style, out TMP_FontAsset font)) return font;
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
            if (font != null && korean != null && font != korean)
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

    private static TMP_FontAsset LoadJapanTvTmp(string path)
    {
        string key = "JapanTvUi:" + path;
        if (posterFonts.TryGetValue(key, out TMP_FontAsset font)) return font;
        Font source = Resources.Load<Font>(path);
        TMP_FontAsset asset = Resources.Load<TMP_FontAsset>(path + " SDF");
        if (source == null && asset != null) source = asset.sourceFontFile;
        return CreateJapanTvTmp(key, source);
    }

    private static TMP_FontAsset CreateJapanTvTmp(string key, Font source)
    {
        if (posterFonts.TryGetValue(key, out TMP_FontAsset font)) return font;
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
        return font;
    }

    internal static TMP_FontAsset ResolveJapanTvTmp(TMP_FontAsset preferred, string content)
    {
        if (preferred != null && preferred.sourceFontFile != null && !preferred.name.EndsWith(" JapanTvUi Runtime SDF"))
            preferred = CreateJapanTvTmp("JapanTvUi:" + preferred.sourceFontFile.GetInstanceID(), preferred.sourceFontFile);
        if (SupportsCmdTmp(preferred, content)) return preferred;
        TMP_FontAsset fallback = LoadTmp(BattleUiStyle.JapanTvUi);
        return SupportsCmdTmp(fallback, content) ? fallback : preferred != null ? preferred : fallback;
    }

    private static TMP_FontAsset LoadPosterTmp(string path)
    {
        if (path == null) return null;
        if (posterFonts.TryGetValue(path, out TMP_FontAsset font)) return font;
        TMP_FontAsset source = Resources.Load<TMP_FontAsset>(path);
        if (source == null || source.sourceFontFile == null) return null;
        font = TMP_FontAsset.CreateFontAsset(source.sourceFontFile, 90, 12, GlyphRenderMode.SDFAA,
            1024, 1024, AtlasPopulationMode.Dynamic, true);
        if (font == null) return null;
        font.name = source.name + " Poster Runtime";
        font.hideFlags = HideFlags.HideAndDontSave;
        font.material.hideFlags = HideFlags.HideAndDontSave;
        font.fallbackFontAssetTable = new List<TMP_FontAsset>();
        posterFonts[path] = font;
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
        // Rebuild CMD atlases from source fonts; serialized SDF tables contain merge conflicts.
        if (preferred != null && preferred.sourceFontFile != null)
            preferred = CreateCmdTmp(preferred.sourceFontFile);
        if (SupportsCmdTmp(preferred, content)) return preferred;
        TMP_FontAsset fallback = LoadTmp(BattleUiStyle.CMD);
        if (SupportsCmdTmp(fallback, content)) return fallback;
        return preferred != null ? preferred : fallback != null ? fallback : TMP_Settings.defaultFontAsset;
    }

    internal static bool SupportsCmdTmp(TMP_FontAsset font, string content)
    {
        if (font == null) return false;
        if (string.IsNullOrEmpty(content)) return true;
        foreach (char character in content)
            if (!char.IsControl(character) && !font.HasCharacter(character, true, true)) return false;
        return true;
    }

    private static TMP_FontAsset CreateCmdTmp(Font source)
    {
        if (source == null) return null;
        if (cmdFonts.TryGetValue(source, out TMP_FontAsset font)) return font;
        font = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA,
            1024, 1024, AtlasPopulationMode.Dynamic, true);
        if (font == null) return null;
        font.name = source.name + " CMD Runtime SDF";
        font.hideFlags = HideFlags.HideAndDontSave;
        font.material.hideFlags = HideFlags.HideAndDontSave;
        font.fallbackFontAssetTable = new List<TMP_FontAsset>();
        cmdFonts[source] = font;
        return font;
    }

    internal static Font Load(BattleUiStyle style)
    {
        if (fonts.TryGetValue(style, out Font font)) return font;
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

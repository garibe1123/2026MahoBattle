using System.Collections.Generic;
using TMPro;
using UnityEngine;

internal static class BattleUiThemeFonts
{
    private static readonly Dictionary<BattleUiStyle, Font> fonts = new();
    private static readonly Dictionary<BattleUiStyle, TMP_FontAsset> tmpFonts = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        fonts.Clear();
        tmpFonts.Clear();
    }

    internal static TMP_FontAsset LoadTmp(BattleUiStyle style)
    {
        if (tmpFonts.TryGetValue(style, out TMP_FontAsset font)) return font;
        string path = style switch
        {
            BattleUiStyle.pixelStyleUi => "BattleUiFonts/galmuri/Galmuri9 SDF",
            BattleUiStyle.comicStyleUi => "HarnessFonts/DoHyeon/DoHyeon-Regular SDF",
            BattleUiStyle.VectorStyleUi => "HarnessFonts/IBMPlexSansKR/IBMPlexSansKR-Regular SDF",
            BattleUiStyle.MahoShojo => "HarnessFonts/GowunBatang/GowunBatang-Regular SDF",
            BattleUiStyle.Stylish => "HarnessFonts/BlackHanSans/BlackHanSans-Regular SDF",
            BattleUiStyle.Birthday => "HarnessFonts/Jua/Jua-Regular SDF",
            BattleUiStyle.Paper => "HarnessFonts/BlackHanSans/BlackHanSans-Regular SDF",
            _ => null
        };
        font = path != null ? Resources.Load<TMP_FontAsset>(path) : null;
        if (font == null && (style == BattleUiStyle.Stylish || style == BattleUiStyle.Birthday))
            font = BattleUiStyleTmpTitle.LoadFont(style == BattleUiStyle.Birthday);
        tmpFonts[style] = font;
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
            _ => null
        };
        font = path != null ? Resources.Load<Font>(path) : null;
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

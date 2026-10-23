using System.Collections.Generic;
using UnityEngine;

internal static class BattleUiThemeFonts
{
    private static readonly Dictionary<BattleUiStyle, Font> fonts = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => fonts.Clear();

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

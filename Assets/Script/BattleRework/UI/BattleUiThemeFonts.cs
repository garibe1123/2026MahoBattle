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
            BattleUiStyle.pixelStyleUi => "galmuri/Galmuri9",
            BattleUiStyle.comicStyleUi => "dohyeon/DoHyeon-Regular",
            BattleUiStyle.VectorStyleUi => "gothica1/GothicA1-Regular",
            BattleUiStyle.MahoShojo => "gowundodum/GowunDodum-Regular",
            BattleUiStyle.Stylish => "blackhansans/BlackHanSans-Regular",
            BattleUiStyle.Birthday => "jua/Jua-Regular",
            _ => null
        };
        font = path != null ? Resources.Load<Font>("BattleUiFonts/" + path) : null;
        fonts[style] = font;
        return font;
    }
}

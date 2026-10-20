#if UNITY_EDITOR
using UnityEngine;

internal static class ElementalFireFrames
{
    private const int Width = 16;
    private const int Height = 16;
    private static readonly Color32 Soot = new(30, 25, 39, 210);
    private static readonly Color32 Ember = new(106, 53, 47, 240);
    private static readonly Color32 Red = new(166, 43, 44, 255);
    private static readonly Color32 Flame = new(234, 79, 29, 255);
    private static readonly Color32 Gold = new(255, 166, 44, 255);
    private static readonly Color32 Core = new(255, 232, 101, 255);
    // Authored blocks break into separate eddies above the bright lower leading edge.
    private static readonly string[] Frames = {
        "././././" +
        "././././" +
        "././.....rr/.....roo/" +
        ".....oyyo/......ywwy/......yww/.......y",
        "././././" +
        ".....s/....srr/.....ree/....reoo/" +
        ".....ooyo/....ooyyo/.....ywwyo/.....ywwwy/" +
        "......www/......ywy/.......y/.",
        "./...ss/...srrs/.....rrs/" +
        "..srree/..reeooe/....eooyoo/...reoyyoo/" +
        "...oooyyyo/....oywwwy/....ywwwwy/.....ywwwy/" +
        "......www/......ywy/.......y/.",
        "..ss....ss/.srrs..srrs/...rrssrrees/..sreereoors/" +
        ".srreeooeers/..reeooyoorrs/...eooyyooor/...oooyyyo/" +
        "....oywwyyo/...oywwwwwy/....ywwwwy/.....ywwwy/" +
        "......www/......ywy/.......y/.",
        "...ss..ss/..srrssrrs/.srrreerss/..srreeoorrs/" +
        "...reeooeerrs/.sreeoyyoorr/..reooyyoooe/...oooywyyo/" +
        "...oywwwwyo/....ywwwwwy/.....ywwwwy/.....ywwwy/" +
        "......www/......ywy/.......y/.",
        ".ss.....ss/.srrs..srrrs/..srressrreers/...reeeoorrs/" +
        "..sreeooeerrs/...reeoyyoorr/.sreooyyyooor/...oooywwyo/" +
        "....oywwwyo/...oywwwwwy/....ywwwwy/.....ywww/" +
        ".....ywww/......wwy/.......y/.",
        "..ss..ss/.srrssrrs/..srrreeers/.srreeooerrs/" +
        "..reeooyoorrs/...rooyyooor/..reoooyyyoo/...ooywwyyo/" +
        "....oywwwyo/....ywwwwwy/.....ywwwwy/......ywww/" +
        "......www/.......wy/.......y/.",
        "...ss.....ss/..srrs..srrs/.srreessrrrs/..srreeoorrs/" +
        "...reeooeers/..reeooyyoorr/...eooyyyooor/..roooywwyyo/" +
        "...oywwwwyo/....ywwwwwy/....ywwwwy/.....ywwwy/" +
        "......www/......ywy/.......y/.",
        ".ss....ss/..srrssrrrs/...rrreeers/.srreeoorrs/" +
        "..reeooeerrs/...reeoyyoorr/..reoooyyoor/...oooywyyo/" +
        "...oywwwwyo/...oywwwwwy/....ywwwwy/.....ywwwy/" +
        "......www/.......wy/.......y/.",
        "...ss..ss/..srrssrrs/.srreeerrrs/..srreeoorrs/" +
        "...reeooeerrs/.sreeoyyooor/..reooyyoooe/...oooywwyo/" +
        "....oywwwwyo/....ywwwwwy/.....ywwwwy/......ywww/" +
        "......www/......ywy/.......y/.",
        "..ss.....ss/.srrs..srrs/..srressrrrs/.srreeeoorrs/" +
        "..reeooeerrs/...reeoyyoorr/.sreooyyyooor/...oooywyyo/" +
        "...oywwwwyo/....ywwwwwy/.....ywwwwy/.....ywwwy/" +
        "......www/.......wy/.......y/.",
        "...ss..ss/..srrssrrs/.srrreerrs/..srreeoorrs/" +
        ".srreeooeerrs/..reeooyyoorr/...eooyyooor/...oooywwyo/" +
        "....oywwwyo/...oywwwwwy/....ywwwwy/.....ywww/" +
        ".....ywww/......wwy/.......y/.",
        "..ss....ss/.srrs..srrs/..srrssrrrs/...srrerrrs/" +
        "..srreeorrs/.srrrooorrs/...rreeoorr/..srrooyoor/" +
        "...rrooyoor/....rooyyo/.....oywyo/......oyyo/" +
        "......roo/.......rr/.......s/.",
        ".ss.....ss/.srrs..ss/..srrs.srrs/...ssrrrss/" +
        "..srrs..rrs/.srrs....rs/...rr...rr/..srrssrrs/" +
        "....rrerr/.....roorr/......roo/......rr/" +
        ".......rs/.......s/./.",
        "..s.....ss/.ssrr....s/..rrs..ss/...ss...rrs/" +
        ".srs.....rs/..ss....ss/....r..rs/....ssrr/" +
        "...rr..rs/....s..ss/......sr/......ss/" +
        ".......s/././.",
        "...s....s/..ss....ss/.........s/....s/" +
        "..s....s/...s/........s/.....s/" +
        "....ss/........s/.....s/./" +
        ".......s/././."
    };
    private static readonly string[][] Rows = SplitFrames();

    private static string[][] SplitFrames()
    {
        var rows = new string[Frames.Length][];
        for (int i = 0; i < rows.Length; i++) rows[i] = Frames[i].Split('/');
        return rows;
    }

    public static Color32 Pixel(int frame, int x, int y, int cell)
    {
        int column = Mathf.Min(Width - 1, x * Width / cell);
        int row = Height - 1 - Mathf.Min(Height - 1, y * Height / cell);
        string line = Rows[frame][row];
        char ink = column < line.Length ? line[column] : '.';
        return ink == 's' ? Soot : ink == 'r' ? Ember : ink == 'e' ? Red : ink == 'o' ? Flame
            : ink == 'y' ? Gold : ink == 'w' ? Core : default;
    }
}
#endif
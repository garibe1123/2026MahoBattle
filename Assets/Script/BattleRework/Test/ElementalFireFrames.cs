#if UNITY_EDITOR
using UnityEngine;

internal static class ElementalFireFrames
{
    private const int Width = 12;
    private const int Height = 16;
    private static readonly Color32 Soot = new(37, 30, 43, 190);
    private static readonly Color32 Ember = new(109, 49, 40, 225);
    private static readonly Color32 Flame = new(227, 76, 29, 255);
    private static readonly Color32 Gold = new(255, 165, 42, 255);
    private static readonly Color32 Core = new(255, 231, 102, 255);
    // Each entry is one hand-drawn frame, from the upper trail to the bright lower tip.
    private static readonly string[] Frames = {
        "............/............/............/............/" +
        "............/............/............/............/" +
        "............/............/............/............/" +
        ".....oo...../....oyyo..../....ywwy..../.....yy.....",
        "............/............/............/............/" +
        "............/............/............/.....r....../" +
        "....roo...../....oyyo..../...oywwyo.../....ywwy..../" +
        "....ywwy..../.....ww...../.....yy...../............",
        "............/............/.....s....../....srs...../" +
        "....rrs...../...roor...../....oyor..../...ooyyo..../" +
        "...oywyo..../....ywwyo.../....ywwwy.../....ywwy..../" +
        ".....ww...../.....yy...../............/............",
        ".....ss...../....srrs..../..s..rrs..../..srroor..../" +
        "...rooor..../..rooyoor.../...royoor.../...ooyyo..../" +
        "...oywwyo.../....ywwyo.../....ywwwy.../....ywwy..../" +
        ".....ww...../.....yy...../............/............",
        "....ss....../...srrs...../..srrrs..s../...roors.s../" +
        "..srooor..../...royoor.../...ooyoor.../..rooyyo..../" +
        "...oywwyo.../....ywwwy.../....ywwy..../.....ww...../" +
        ".....yy...../............/............/............",
        "......ss..../....ssrs..../...srrrs..../..sroors..../" +
        "...rooor..../...royoor.../..rooyyo..../...oywyo..../" +
        "...oywwyo.../...oywwwy.../....ywwy..../....ywwy..../" +
        ".....yy...../............/............/............",
        "...s..ss..../..srrsrs..../...rrrs...../...roors..../" +
        "..srooor..../..rooyoor.../...oyyoor.../...oywwyo.../" +
        "....ywwyo.../....ywwwy.../.....wwy..../.....ww...../" +
        ".....yy...../............/............/............",
        ".....ss...../...ssrrs..../...srors..../..srroor..../" +
        "...rooor..../...royoor.../....oyoor.../...ooyyo..../" +
        "...oywwyo.../...oywwy..../....ywwy..../....ywwy..../" +
        ".....wy...../.....y....../............/............",
        "...ss......./..srrs..s.../...rrrs.s.../..sroors..../" +
        "...rooor..../..rooyoor.../..rooyyo..../...oywyo..../" +
        "....ywwyo.../....ywwwy.../....ywwy..../.....ww...../" +
        ".....yy...../............/............/............",
        "......ss..../....ssrs..../...srrs...../..srorrs..../" +
        "...rooor..../...royoor.../...ooyoor.../....oyyo..../" +
        "...oywwyo.../...oywwwy.../....ywwy..../.....ww...../" +
        ".....yy...../............/............/............",
        "....ss....../...srrs..s../..srrrs..s../...roors..../" +
        "..srooor..../...royoor.../..rooyyo..../...oywwyo.../" +
        "...oywwy..../....ywwwy.../....ywwy..../....yww...../" +
        ".....yy...../............/............/............",
        ".....ss...../....srrs..../..s..rrs..../..srroor..../" +
        "...rooor..../..rooyoor.../...oyyoor.../...oywyo..../" +
        "....ywwyo.../....ywwwy.../.....wwy..../.....ww...../" +
        ".....yy...../............/............/............",
        "....ss....../...ssss..s../..ssrrs..s../...srrrs..../" +
        "..ssrorr..../...rroorr.../....roor..../...rroor..../" +
        "....royo..../....oyyo..../.....oyo..../.....oo...../" +
        "......r...../............/............/............",
        "...ss..s..../...sss.s..../..ssrs....../...srrss..../" +
        "..srrrss..../...ssrrs..../....srrs..../....rrs...../" +
        "...s..rr..../......ro..../.....rr...../.....r....../" +
        "............/............/............/............",
        "...s......../..ss..s...../......ss..../...s...s..../" +
        "..ssr......./...rs..s..../......ss..../....s......./" +
        "....sr....../.....s....../............/.....s....../" +
        "............/............/............/............",
        "............/...s......../.......s..../............/" +
        "..s........./............/......s...../............/" +
        "....s......./............/............/............/" +
        "............/............/............/............"
    };

    public static Color32 Pixel(int frame, int x, int y, int cell)
    {
        int column = Mathf.Min(Width - 1, x * Width / cell);
        int row = Height - 1 - Mathf.Min(Height - 1, y * Height / cell);
        char ink = Frames[frame][row * (Width + 1) + column];
        return ink == 's' ? Soot : ink == 'r' ? Ember : ink == 'o' ? Flame
            : ink == 'y' ? Gold : ink == 'w' ? Core : default;
    }
}
#endif

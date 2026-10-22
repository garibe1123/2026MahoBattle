using UnityEngine;

internal static class BattleUiPixelFont
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private static readonly string[] Rows =
    {
        "0E11111F111111", "1E11111E11111E", "0F10101010100F", "1E11111111111E",
        "1F10101E10101F", "1F10101E101010", "0F10101711110F", "1111111F111111",
        "0E04040404040E", "0702020212120C", "11121418141211", "1010101010101F",
        "111B1515111111", "11191513111111", "0E11111111110E", "1E11111E101010",
        "0E11111115120D", "1E11111E141211", "0F10100E01011E", "1F040404040404",
        "1111111111110E", "11111111110A04", "11111115151B11", "11110A040A1111",
        "11110A04040404", "1F01020408101F", "0E11131519110E", "040C040404040E",
        "0E11010204081F", "1E01010E01011E", "02060A121F0202", "1F10101E01011E",
        "0E10101E11110E", "1F010204080808", "0E11110E11110E", "0E11110F01010E"
    };

    internal static Font Create(out Texture2D texture, out Material material)
    {
        texture = new Texture2D(128, 64, TextureFormat.RGBA32, false)
        {
            name = "BattlePixelFont_Runtime", filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
        };
        Color32[] pixels = new Color32[128 * 64];
        Font font = new() { name = "BattlePixelFont_Runtime", hideFlags = HideFlags.HideAndDontSave };
        float scale = (font.fontSize > 0 ? font.fontSize : 8) / 8f;
        CharacterInfo[] characters = new CharacterInfo[95];
        for (int i = 0; i < characters.Length; i++)
        {
            char character = (char)(i + 32);
            string rows = Glyph(char.ToUpperInvariant(character));
            int x = i % 16 * 8, y = i / 16 * 8;
            for (int row = 0; row < 7; row++)
            {
                int bits = System.Convert.ToInt32(rows.Substring(row * 2, 2), 16);
                for (int column = 0; column < 5; column++)
                    if ((bits & (1 << (4 - column))) != 0)
                        pixels[(y + 6 - row) * 128 + x + column] = new Color32(255, 255, 255, 255);
            }
            characters[i] = new CharacterInfo
            {
                index = character, advance = Mathf.RoundToInt(6f * scale),
                minX = 0, maxX = Mathf.RoundToInt(5f * scale),
                minY = 0, maxY = Mathf.RoundToInt(7f * scale),
                size = Mathf.RoundToInt(8f * scale),
                uvBottomLeft = new Vector2(x / 128f, y / 64f),
                uvBottomRight = new Vector2((x + 5f) / 128f, y / 64f),
                uvTopLeft = new Vector2(x / 128f, (y + 7f) / 64f),
                uvTopRight = new Vector2((x + 5f) / 128f, (y + 7f) / 64f)
            };
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        material = new Material(Resources.Load<Shader>("BattlePixelText"))
        {
            mainTexture = texture, hideFlags = HideFlags.HideAndDontSave
        };
        font.material = material;
        font.characterInfo = characters;
        return font;
    }

    private static string Glyph(char c)
    {
        int index = Alphabet.IndexOf(c);
        if (index >= 0) return Rows[index];
        return c switch
        {
            ' ' => "00000000000000", '.' => "00000000000C0C", ',' => "00000000000408",
            ':' => "000C0C000C0C00", ';' => "000C0C000C0408", '-' => "0000001F000000",
            '_' => "0000000000001F", '/' => "01010204081010", '\\' => "10100804020101",
            '!' => "04040404040004", '?' => "0E110102040004", '+' => "0004041F040400",
            '=' => "00001F001F0000", '%' => "19190204081313", '*' => "00150E1F0E1500",
            '(' => "02040808080402", ')' => "08040202020408", '[' => "0E08080808080E",
            ']' => "0E02020202020E", '<' => "01020408040201", '>' => "10080402040810",
            '\'' => "04040800000000", '"' => "0A0A0000000000", '|' => "04040404040404",
            '#' => "0A1F0A0A1F0A00", '&' => "0C12140C15120D", '@' => "0E11171517100F",
            _ => "0E110102040004"
        };
    }
}

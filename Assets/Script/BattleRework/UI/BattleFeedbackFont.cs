using UnityEngine;

// Shared by the short Korean feedback labels without changing existing HUD fonts.
internal static class BattleFeedbackFont
{
    private static Font font;

    internal static Font Resolve()
    {
        if (font == null)
        {
            font = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic" }, 24);
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        return font;
    }
}

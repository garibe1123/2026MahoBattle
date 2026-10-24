using System.Collections.Generic;
using System.Globalization;

internal sealed class BattleKoreanTypewriter
{
    private const string Initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    private readonly List<(int prefix, string fragment)> steps = new();
    private string content = string.Empty;

    internal int StepCount => steps.Count;

    internal void Reset(string text)
    {
        content = text ?? string.Empty;
        steps.Clear();
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(content);
        while (elements.MoveNext())
        {
            int index = elements.ElementIndex;
            string element = elements.GetTextElement();
            char c = element[0];
            if (c >= '\uAC00' && c <= '\uD7A3')
            {
                int syllable = c - 0xAC00;
                int initial = syllable / 588;
                int vowel = syllable % 588 / 28;
                int final = syllable % 28;
                int basis = 0xAC00 + initial * 588;
                steps.Add((index, Initials[initial].ToString()));
                int simpleVowel = vowel switch
                {
                    9 or 10 or 11 => 8,
                    14 or 15 or 16 => 13,
                    19 => 18,
                    _ => -1
                };
                if (simpleVowel >= 0) steps.Add((index, ((char)(basis + simpleVowel * 28)).ToString()));
                basis += vowel * 28;
                steps.Add((index, ((char)basis).ToString()));
                int simpleFinal = final switch
                {
                    3 => 1,
                    5 or 6 => 4,
                    >= 9 and <= 15 => 8,
                    18 => 17,
                    _ => 0
                };
                if (simpleFinal != 0) steps.Add((index, ((char)(basis + simpleFinal)).ToString()));
                if (final != 0) steps.Add((index, c.ToString()));
                if (element.Length > 1) steps.Add((index, element));
            }
            else steps.Add((index, element));
        }
    }

    internal string Render(int completedSteps)
    {
        if (completedSteps <= 0) return string.Empty;
        if (completedSteps >= steps.Count) return content;
        var step = steps[completedSteps - 1];
        return content.Substring(0, step.prefix) + step.fragment;
    }
}

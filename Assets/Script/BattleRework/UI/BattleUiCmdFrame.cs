using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class BattleUiCmdFrame : IDisposable
{
    private const float FrameInset = 1f;
    private readonly TextMeshProUGUI[] edges = new TextMeshProUGUI[4];
    private Rect lastRect;
    private float lastSize;
    private TMP_FontAsset lastFont;

    internal BattleUiCmdFrame(Graphic owner)
    {
        for (int i = 0; i < edges.Length; i++)
        {
            edges[i] = CreateText(owner.rectTransform, "CmdFrame" + i);
            edges[i].alignment = i == 1 ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.TopLeft;
        }
    }

    internal static TextMeshProUGUI CreateText(RectTransform parent, string name)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        child.AddComponent<LayoutElement>().ignoreLayout = true;
        TextMeshProUGUI text = child.AddComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        text.richText = false;
        text.fontStyle = FontStyles.Normal;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Truncate;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        BattleUiOpticsCanvas.Register(text);
        return text;
    }

    internal void Apply(Graphic owner, BattleUiStyleSettings settings, bool selected)
    {
        TMP_FontAsset font = settings.fontAsset != null ? settings.fontAsset : BattleUiThemeFonts.LoadTmp(BattleUiStyle.CMD);
        Rect rect = owner.rectTransform.rect;
        float size = Mathf.Max(8f, Mathf.Min(settings.cmdFrameFontSize, rect.height / 3f));
        if (font == null || rect.width < size * 3f || rect.height < size * 3f) { Hide(); return; }
        if (lastRect != rect || lastSize != size || lastFont != font)
        {
            lastRect = rect;
            lastSize = size;
            lastFont = font;
            TextMeshProUGUI measure = edges[0];
            if (measure.font != font) { measure.font = font; measure.fontSharedMaterial = font.material; }
            measure.fontSize = size;
            float width = rect.width - FrameInset * 2f;
            int columns = 0, upper = 256;
            while (columns < upper)
            {
                int candidate = (columns + upper + 1) / 2;
                if (measure.GetPreferredValues("+" + new string('-', candidate) + "+").x <= width)
                    columns = candidate;
                else upper = candidate - 1;
            }
            string horizontal = "+" + new string('-', columns) + "+";
            edges[0].text = edges[1].text = horizontal;
            edges[0].rectTransform.offsetMin = edges[1].rectTransform.offsetMin = new Vector2(FrameInset, FrameInset);
            edges[0].rectTransform.offsetMax = edges[1].rectTransform.offsetMax = new Vector2(-FrameInset, -FrameInset);
            Vector2 pipe = measure.GetPreferredValues("|");
            float line = Mathf.Max(size, pipe.y);
            int rows = Mathf.Clamp(Mathf.FloorToInt((rect.height - line * 2f - FrameInset * 2f) / line), 0, 128);
            string vertical = string.Empty;
            for (int row = 0; row < rows; row++) vertical += row == 0 ? "|" : "\n|";
            edges[2].text = edges[3].text = vertical;
            edges[2].rectTransform.offsetMin = new Vector2(FrameInset, line + FrameInset);
            edges[2].rectTransform.offsetMax = new Vector2(-FrameInset, -line - FrameInset);
            edges[3].rectTransform.offsetMin = new Vector2(FrameInset + measure.GetPreferredValues(horizontal).x - pipe.x, line + FrameInset);
            edges[3].rectTransform.offsetMax = new Vector2(-FrameInset, -line - FrameInset);
        }
        foreach (TextMeshProUGUI edge in edges)
        {
            if (edge.font != font) { edge.font = font; edge.fontSharedMaterial = font.material; }
            edge.fontSize = size;
            Color tint = selected ? settings.highlight : settings.outerFrame;
            tint.a *= owner.color.a;
            edge.color = tint;
            edge.maskable = owner is MaskableGraphic maskableOwner && maskableOwner.maskable;
            if (!edge.gameObject.activeSelf) edge.gameObject.SetActive(true);
        }
    }

    internal void Hide()
    {
        foreach (TextMeshProUGUI edge in edges)
            if (edge != null && edge.gameObject.activeSelf) edge.gameObject.SetActive(false);
    }

    public void Dispose()
    {
        foreach (TextMeshProUGUI edge in edges)
            if (edge != null) Release(edge.gameObject);
    }

    internal static void Release(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class BattleUiCmdFrame : IDisposable
{
    private readonly TextMeshProUGUI[] edges = new TextMeshProUGUI[4];
    private Rect lastRect;
    private float lastSize, lastStep;
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
        float scale = size / Mathf.Max(1f, font.faceInfo.pointSize) * font.faceInfo.scale;
        float step = font.characterLookupTable.TryGetValue('-', out TMP_Character dash)
            ? dash.glyph.metrics.horizontalAdvance * scale : size * 0.6f;
        step = Mathf.Max(1f, step);
        if (lastRect != rect || lastSize != size || lastStep != step || lastFont != font)
        {
            lastRect = rect;
            lastSize = size;
            lastStep = step;
            lastFont = font;
            float corner = font.characterLookupTable.TryGetValue('+', out TMP_Character plus)
                ? plus.glyph.metrics.horizontalAdvance * scale : step;
            int columns = Mathf.Clamp(Mathf.FloorToInt((rect.width - corner * 2f) / step), 1, 256);
            string horizontal = "+" + new string('-', columns) + "+";
            edges[0].text = edges[1].text = horizontal;
            float line = Mathf.Max(size, font.faceInfo.lineHeight * scale);
            int rows = Mathf.Clamp(Mathf.FloorToInt((rect.height - size * 2f) / line), 0, 128);
            string vertical = string.Empty;
            for (int row = 0; row < rows; row++) vertical += row == 0 ? "|" : "\n|";
            edges[2].text = edges[3].text = vertical;
            edges[2].rectTransform.offsetMin = new Vector2(0f, size);
            edges[2].rectTransform.offsetMax = new Vector2(0f, -size);
            edges[3].rectTransform.offsetMin = new Vector2(rect.width - step, size);
            edges[3].rectTransform.offsetMax = new Vector2(0f, -size);
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

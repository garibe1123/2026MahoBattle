using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class BattleUiStyleTmpTitle : IDisposable
{
    private static TMP_FontAsset stylishFont, birthdayFont;
    private static bool fontsLoaded;
    private readonly Text source;
    private readonly TextMeshProUGUI text;
    private Material material;
    private string checkedText;
    private TMP_FontAsset checkedFont;
    private bool supported;
    private float shear, padding;
    private Color outline, shadow;
    private float outlineWidth = -1f;
    private Vector2 shadowOffset;
    private bool subMeshesPending;
    private bool decoratedTitle = true;

    internal bool Visible => text != null && text.gameObject.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetFonts()
    {
        stylishFont = birthdayFont = null;
        fontsLoaded = false;
    }

    internal static TMP_FontAsset LoadFont(bool birthday)
    {
        if (!fontsLoaded)
        {
            stylishFont = Resources.Load<TMP_FontAsset>("HarnessFonts/BlackHanSans/BlackHanSans-Regular SDF");
            if (stylishFont == null) stylishFont = Resources.Load<TMP_FontAsset>("BattleUiFonts/Stylish SDF");
            birthdayFont = Resources.Load<TMP_FontAsset>("HarnessFonts/Jua/Jua-Regular SDF");
            if (birthdayFont == null) birthdayFont = Resources.Load<TMP_FontAsset>("BattleUiFonts/Birthday SDF");
            fontsLoaded = true;
        }
        return birthday ? birthdayFont : stylishFont;
    }

    internal BattleUiStyleTmpTitle(Text source)
    {
        this.source = source;
        GameObject child = new("StyleTitleTMP", typeof(RectTransform), typeof(LayoutElement));
        child.layer = source.gameObject.layer;
        child.transform.SetParent(source.transform, false);
        child.GetComponent<LayoutElement>().ignoreLayout = true;
        RectTransform rect = (RectTransform)child.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        text = child.AddComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.OnPreRenderText += FitTitle;
        BattleUiOpticsCanvas.Register(text);
    }

    internal void Apply(TMP_FontAsset font, BattleUiStyleSettings style, bool birthday, bool decorate = true)
    {
        decoratedTitle = decorate;
        string content = source.text ?? string.Empty;
        if (checkedFont != font || checkedText != content)
        {
            checkedFont = font;
            checkedText = content;
            supported = font.HasCharacters(content, out uint[] _, true, true);
        }
        if (!supported || !source.isActiveAndEnabled) { Hide(); return; }
        if (text.font != font || material == null)
        {
            if (material != null) UnityEngine.Object.Destroy(material);
            text.font = font;
            material = new Material(font.material) { hideFlags = HideFlags.HideAndDontSave };
            material.EnableKeyword("UNDERLAY_ON");
            text.fontSharedMaterial = material;
            outlineWidth = -1f;
        }
        Color nextOutline = style.outerFrame;
        Color nextShadow = birthday ? style.energy : style.status;
        float nextWidth = decorate ? style.titleOutlineWidth : 0f;
        if (outline != nextOutline || shadow != nextShadow || outlineWidth != nextWidth ||
            shadowOffset != style.titleShadowOffset)
        {
            outline = nextOutline;
            shadow = nextShadow;
            outlineWidth = nextWidth;
            shadowOffset = style.titleShadowOffset;
            material.SetColor("_OutlineColor", outline);
            material.SetFloat("_OutlineWidth", outlineWidth);
            material.SetColor("_UnderlayColor", shadow);
            material.SetFloat("_UnderlayOffsetX", shadowOffset.x);
            material.SetFloat("_UnderlayOffsetY", shadowOffset.y);
            text.UpdateMeshPadding();
            text.SetMaterialDirty();
            text.SetVerticesDirty();
        }
        if (decorate) material.EnableKeyword("UNDERLAY_ON");
        else material.DisableKeyword("UNDERLAY_ON");
        float nextShear = decorate && !birthday ? style.stylishTitleShear : 0f;
        float nextPadding = decorate ? Mathf.Max(0f, style.titlePadding) : 0f;
        if (shear != nextShear || padding != nextPadding)
        {
            shear = nextShear;
            padding = nextPadding;
            text.margin = new Vector4(padding, padding, padding, padding);
            text.SetVerticesDirty();
        }
        text.text = content;
        text.richText = source.supportRichText;
        text.fontSize = source.fontSize * (decorate ? style.titleSizeMultiplier : 1f);
        text.enableAutoSizing = !decorate && source.resizeTextForBestFit;
        text.fontSizeMin = source.resizeTextMinSize;
        text.fontSizeMax = source.resizeTextMaxSize;
        text.lineSpacing = decorate ? 0f : (source.lineSpacing - 1f) * source.fontSize;
        text.fontStyle = decorate ? FontStyles.Normal : source.fontStyle switch
        {
            FontStyle.Bold => FontStyles.Bold,
            FontStyle.Italic => FontStyles.Italic,
            FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
            _ => FontStyles.Normal
        };
        text.alignment = Alignment(source.alignment);
        text.textWrappingMode = source.horizontalOverflow == HorizontalWrapMode.Wrap
            ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        text.maskable = source.maskable;
        text.overflowMode = decorate || source.verticalOverflow == VerticalWrapMode.Overflow
            ? TextOverflowModes.Overflow : TextOverflowModes.Truncate;
        Color face = decorate ? (birthday ? style.status : style.background) : BattleUiStyleMeshEffect.TextColor(source.color, style);
        if (decorate) face.a *= source.color.a;
        text.color = face;
        text.canvasRenderer.SetColor(source.canvasRenderer.GetColor());
        if (subMeshesPending && text.canvas != null &&
            text.canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            foreach (TMP_SubMeshUI subMesh in text.GetComponentsInChildren<TMP_SubMeshUI>(true))
            {
                subMesh.gameObject.layer = source.gameObject.layer;
                BattleUiOpticsCanvas.Register(subMesh);
            }
            subMeshesPending = false;
        }
        if (!text.gameObject.activeSelf)
        {
            text.gameObject.SetActive(true);
            source.SetVerticesDirty();
        }
    }

    internal void Hide()
    {
        if (!Visible) return;
        text.gameObject.SetActive(false);
        if (source != null) source.SetVerticesDirty();
    }

    private void FitTitle(TMP_TextInfo info)
    {
        if (info.materialCount > 1) subMeshesPending = true;
        if (!decoratedTitle) return;
        Rect rect = text.rectTransform.rect;
        float inset = Mathf.Min(padding, Mathf.Min(rect.width, rect.height) * 0.25f);
        rect = BattleUiStyleMeshEffect.Inset(rect, inset);
        if (rect.width <= 0f || rect.height <= 0f) return;
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];
            if (!character.isVisible) continue;
            Vector3[] vertices = info.meshInfo[character.materialReferenceIndex].vertices;
            for (int j = 0; j < 4; j++)
            {
                Vector3 vertex = vertices[character.vertexIndex + j];
                vertex.x += shear * vertex.y;
                min = Vector2.Min(min, vertex);
                max = Vector2.Max(max, vertex);
            }
        }
        if (min.x == float.MaxValue) return;
        Vector2 size = max - min, center = (min + max) * 0.5f;
        float scale = Mathf.Min(1f, Mathf.Min(rect.width / Mathf.Max(0.001f, size.x),
            rect.height / Mathf.Max(0.001f, size.y)));
        Vector2 fitted = new(Mathf.Clamp(center.x, rect.xMin + size.x * scale * 0.5f, rect.xMax - size.x * scale * 0.5f),
            Mathf.Clamp(center.y, rect.yMin + size.y * scale * 0.5f, rect.yMax - size.y * scale * 0.5f));
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];
            if (!character.isVisible) continue;
            Vector3[] vertices = info.meshInfo[character.materialReferenceIndex].vertices;
            for (int j = 0; j < 4; j++)
            {
                Vector3 vertex = vertices[character.vertexIndex + j];
                Vector2 position = new(vertex.x + shear * vertex.y, vertex.y);
                position = (position - center) * scale + fitted;
                vertices[character.vertexIndex + j] = new Vector3(position.x, position.y, vertex.z);
            }
        }
    }

    private static TextAlignmentOptions Alignment(TextAnchor alignment) => alignment switch
    {
        TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
        TextAnchor.UpperCenter => TextAlignmentOptions.Top,
        TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
        TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
        TextAnchor.MiddleRight => TextAlignmentOptions.Right,
        TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
        TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
        TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
        _ => TextAlignmentOptions.Center
    };

    public void Dispose()
    {
        if (text != null)
        {
            text.OnPreRenderText -= FitTitle;
            UnityEngine.Object.Destroy(text.gameObject);
        }
        if (material != null) UnityEngine.Object.Destroy(material);
    }
}

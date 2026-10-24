using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class BattleUiStyleTmpTitle : IDisposable
{
    private static TMP_FontAsset stylishFont, birthdayFont;
    private static bool fontsLoaded;
    private readonly Text source;
    private readonly BattleUiStyleTitleText text;
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
    private BattleUiTitleMotionSettings motion;
    private bool allowOverflow, animationPending;
    private Vector3[][] originalVertices = Array.Empty<Vector3[]>();
    private Color32[][] originalColors = Array.Empty<Color32[]>();
    private float[] characterStarts = Array.Empty<float>();
    private int startedCharacters, animatedFrame = -1;
    private readonly List<TMP_SubMeshUI> subMeshes = new();

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
        text = child.AddComponent<BattleUiStyleTitleText>();
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.OnPreRenderText += FitTitle;
        BattleUiOpticsCanvas.Register(text);
    }

    internal void Apply(TMP_FontAsset font, BattleUiStyleSettings style, bool birthday, bool decorate = true,
        BattleUiTitleMotionSettings titleMotion = null, bool overflow = false)
    {
        decoratedTitle = decorate;
        string content = source.text ?? string.Empty;
        if (checkedFont != font || checkedText != content)
        {
            checkedFont = font;
            checkedText = content;
            supported = font.HasCharacters(content, out uint[] _, true, true);
        }
        if (!source.isActiveAndEnabled || (titleMotion != null &&
            (source.color.a <= 0f || source.canvasRenderer.GetInheritedAlpha() <= 0f))) { Hide(); return; }
        if (!supported) { Hide(titleMotion != null && motion == titleMotion); return; }
        bool nextOverflow = titleMotion != null && overflow;
        if (motion != titleMotion || allowOverflow != nextOverflow)
        {
            motion = titleMotion;
            allowOverflow = nextOverflow;
            ResetMotion();
            Refresh();
        }
        if (content.Length == 0) ResetMotion();
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
        float margin = nextOverflow ? source.fontSize * style.titleSizeMultiplier * Mathf.Max(0f, motion.overflowMargin) : 0f;
        Vector2 expansion = Vector2.one * margin;
        if (text.rectTransform.offsetMin != -expansion) text.rectTransform.offsetMin = -expansion;
        if (text.rectTransform.offsetMax != expansion) text.rectTransform.offsetMax = expansion;
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
        foreach (TMP_SubMeshUI subMesh in subMeshes)
            if (subMesh != null) subMesh.maskable = source.maskable;
        if (subMeshesPending)
        {
            foreach (TMP_SubMeshUI subMesh in subMeshes)
            {
                if (subMesh == null) continue;
                subMesh.gameObject.layer = source.gameObject.layer;
                subMesh.raycastTarget = false;
                subMesh.maskable = source.maskable;
                BattleUiOpticsCanvas.Register(subMesh);
            }
            subMeshesPending = false;
        }
        if (!text.gameObject.activeSelf)
        {
            text.gameObject.SetActive(true);
            source.SetVerticesDirty();
        }
        Animate();
    }

    internal void Refresh()
    {
        if (text == null) return;
        checkedFont = null;
        text.havePropertiesChanged = true;
        text.SetVerticesDirty();
    }

    internal void Hide(bool preserveAnimation = false)
    {
        if (text == null) return;
        bool wasVisible = Visible;
        if (wasVisible) text.gameObject.SetActive(false);
        ResetMotion(preserveAnimation);
        if (!preserveAnimation)
        {
            motion = null;
            allowOverflow = false;
        }
        if (material != null)
        {
            text.fontSharedMaterial = text.font != null ? text.font.material : null;
            UnityEngine.Object.Destroy(material);
            material = null;
        }
        if (wasVisible && source != null) source.SetVerticesDirty();
    }

    private void FitTitle(TMP_TextInfo info)
    {
        if (info.materialCount > 1)
        {
            text.GetComponentsInChildren(true, subMeshes);
            foreach (TMP_SubMeshUI subMesh in subMeshes)
            {
                subMesh.raycastTarget = false;
                subMesh.maskable = source.maskable;
            }
            subMeshesPending = true;
        }
        if (!decoratedTitle) return;
        if (motion != null)
        {
            CacheOriginal(info);
            TransformCharacters(info);
            if (allowOverflow) return;
        }
        FitToRect(info, motion == null ? shear : 0f);
    }

    private void FitToRect(TMP_TextInfo info, float titleShear)
    {
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
                vertex.x += titleShear * vertex.y;
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
                Vector2 position = new(vertex.x + titleShear * vertex.y, vertex.y);
                position = (position - center) * scale + fitted;
                vertices[character.vertexIndex + j] = new Vector3(position.x, position.y, vertex.z);
            }
        }
    }

    private void ResetMotion(bool preserveAnimation = false)
    {
        if (text != null) text.ClearMotionBounds();
        originalVertices = Array.Empty<Vector3[]>();
        originalColors = Array.Empty<Color32[]>();
        if (!preserveAnimation)
        {
            characterStarts = Array.Empty<float>();
            startedCharacters = 0;
        }
        animationPending = false;
        animatedFrame = -1;
    }

    private void CacheOriginal(TMP_TextInfo info)
    {
        if (originalVertices.Length != info.materialCount)
        {
            originalVertices = new Vector3[info.materialCount][];
            originalColors = new Color32[info.materialCount][];
        }
        for (int i = 0; i < info.materialCount; i++)
        {
            Vector3[] vertices = info.meshInfo[i].vertices;
            Color32[] colors = info.meshInfo[i].colors32;
            if (originalVertices[i] == null || originalVertices[i].Length != vertices.Length)
                originalVertices[i] = new Vector3[vertices.Length];
            if (originalColors[i] == null || originalColors[i].Length != colors.Length)
                originalColors[i] = new Color32[colors.Length];
            int count = info.meshInfo[i].vertexCount;
            Array.Copy(vertices, originalVertices[i], count);
            Array.Clear(originalVertices[i], count, vertices.Length - count);
            Array.Copy(colors, originalColors[i], count);
            Array.Clear(originalColors[i], count, colors.Length - count);
        }
        if (characterStarts.Length < info.characterCount)
            Array.Resize(ref characterStarts, Mathf.NextPowerOfTwo(info.characterCount));
        startedCharacters = Mathf.Min(startedCharacters, info.characterCount);
        float now = Time.unscaledTime;
        for (int i = startedCharacters; i < info.characterCount; i++)
            characterStarts[i] = i == 0 ? now : Mathf.Max(now, characterStarts[i - 1] + Mathf.Max(0f, motion.characterDelay));
        startedCharacters = info.characterCount;
    }

    private void Animate()
    {
        if (motion == null || !animationPending || !text.isActiveAndEnabled || text.canvasRenderer.cull ||
            text.havePropertiesChanged || animatedFrame == Time.frameCount || originalVertices.Length == 0) return;
        animatedFrame = Time.frameCount;
        TransformCharacters(text.textInfo);
        if (!allowOverflow) FitToRect(text.textInfo, 0f);
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
    }

    private void TransformCharacters(TMP_TextInfo info)
    {
        for (int i = 0; i < info.materialCount; i++)
        {
            Array.Copy(originalVertices[i], info.meshInfo[i].vertices, originalVertices[i].Length);
            Array.Copy(originalColors[i], info.meshInfo[i].colors32, originalColors[i].Length);
        }
        animationPending = false;
        Vector3 boundsMin = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 boundsMax = new(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];
            if (!character.isVisible) continue;
            int meshIndex = character.materialReferenceIndex, vertexIndex = character.vertexIndex;
            Vector3[] vertices = info.meshInfo[meshIndex].vertices;
            Color32[] colors = info.meshInfo[meshIndex].colors32;
            Vector3 center = (originalVertices[meshIndex][vertexIndex] + originalVertices[meshIndex][vertexIndex + 2]) * 0.5f;
            float height = Mathf.Max(0.001f, character.ascender - character.descender);
            TMP_LineInfo line = info.lineInfo[character.lineNumber];
            Vector2 lineCenter = (line.lineExtents.min + line.lineExtents.max) * 0.5f;
            float width = Mathf.Max(0.001f, line.lineExtents.max.x - line.lineExtents.min.x);
            float along = Mathf.Clamp01((center.x - line.lineExtents.min.x) / width);
            BattleUiTitleCharacterTransform transform = motion.transformCharacters && motion.characters != null && motion.characters.Length > 0
                ? motion.characters[i % motion.characters.Length] : null;
            Vector2 offset = transform != null ? transform.offset * height : Vector2.zero;
            if (motion.curveLines) offset.y += 4f * along * (1f - along) * motion.curveHeight * height;
            float bounceOffset = 0f;
            float bounceExtent = motion.bounce ? Mathf.Max(0f, motion.bounceAmplitude) * height * Mathf.Abs(motion.scale.y) : 0f;
            if (motion.bounce && motion.bounceAmplitude > 0f)
            {
                float progress = (Time.unscaledTime - characterStarts[i]) / Mathf.Max(0.01f, motion.bounceDuration);
                if (progress < 1f)
                {
                    animationPending = true;
                    float t = Mathf.Clamp01(progress);
                    bounceOffset = Mathf.Sin(t * Mathf.PI * 2f) * (1f - t) * motion.bounceAmplitude * height;
                }
            }
            offset.y += bounceOffset;
            float angle = (transform != null ? transform.rotation : 0f) * Mathf.Deg2Rad;
            float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
            Vector2 glyphScale = transform != null ? transform.scale : Vector2.one;
            float glyphShear = shear + (transform != null ? transform.shear : 0f);
            Color accent = i % 3 == 0 ? motion.cyan : i % 3 == 1 ? motion.magenta : motion.yellow;
            for (int j = 0; j < 4; j++)
            {
                int index = vertexIndex + j;
                Vector3 original = originalVertices[meshIndex][index];
                Vector2 local = original - center;
                local = Vector2.Scale(local, glyphScale);
                local.x += glyphShear * local.y;
                Vector2 rotated = new(local.x * cos - local.y * sin, local.x * sin + local.y * cos);
                Vector2 position = (Vector2)center + rotated + offset;
                position = lineCenter + Vector2.Scale(position - lineCenter, motion.scale);
                vertices[index] = new Vector3(position.x, position.y, original.z);
                float restY = position.y - bounceOffset * motion.scale.y;
                boundsMin = Vector3.Min(boundsMin, new Vector3(position.x, restY - bounceExtent, original.z));
                boundsMax = Vector3.Max(boundsMax, new Vector3(position.x, restY + bounceExtent, original.z));
                if (motion.accentCharacters)
                {
                    Color32 color = accent;
                    color.a = (byte)(originalColors[meshIndex][index].a * accent.a);
                    colors[index] = color;
                }
            }
        }
        if (boundsMin.x == float.MaxValue) text.ClearMotionBounds();
        else if (allowOverflow) text.SetMotionBounds(new Bounds((boundsMin + boundsMax) * 0.5f, boundsMax - boundsMin));
        else
        {
            Rect rect = text.rectTransform.rect;
            text.SetMotionBounds(new Bounds(rect.center, rect.size));
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
        ResetMotion();
        subMeshes.Clear();
    }
}

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class BattleUiStyleTmpTitle : IDisposable
{
    private static TMP_FontAsset stylishFont, birthdayFont;
    private static bool fontsLoaded;
    private readonly MaskableGraphic source;
    private readonly Text label;
    private readonly TextMeshProUGUI native;
    private Color sourceColor;
    private float SourceSize => label != null ? label.fontSize : native.fontSize;
    private readonly BattleUiStyleTitleText text;
    private BattleUiTitleBacking backing;
    private readonly float bodySize;
    private BattleUiStyleSettings posterSettings;
    private bool poster, cartoon;
    private float backingExtent;
    private float fittedTitleScale = 1f;
    private float BackingMargin(float extent) => extent * 2.5f +
        (cartoon ? 0f : Mathf.Clamp(posterSettings.inkGlowWidth, 8f, 14f));
    private Material material;
    private string checkedText;
    private TMP_FontAsset checkedFont, resolvedFont;
    private bool supported, checkedCmd;
    private float shear, padding;
    private float titleAngle;
    private readonly float paperAngle;
    private readonly int posterPatternOffset;
    private Color outline, shadow;
    private float outlineWidth = -1f;
    private float glowWidth = -1f;
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
    internal TextMeshProUGUI Text => text;

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

    internal BattleUiStyleTmpTitle(MaskableGraphic source)
    {
        this.source = source;
        label = source as Text;
        native = source as TextMeshProUGUI;
        bodySize = SourceSize * 0.5f;
        if (source.transform.parent != null)
            foreach (Transform sibling in source.transform.parent)
            {
                if (sibling == source.transform || sibling.name.EndsWith("Title", StringComparison.Ordinal) ||
                    sibling.name.EndsWith("Header", StringComparison.Ordinal)) continue;
                if (sibling.TryGetComponent(out Text body) && body.fontSize > 0)
                { bodySize = body.fontSize; break; }
                if (sibling.TryGetComponent(out TextMeshProUGUI tmpBody) && tmpBody.fontSize > 0f)
                { bodySize = tmpBody.fontSize; break; }
            }
        paperAngle = BattleUiStyleMeshEffect.PaperAngle(BattleUiStyleMeshEffect.PaperSeed(source.transform), 1f);
        posterPatternOffset = (int)(BattleUiStyleMeshEffect.PaperSeed(source.transform) & 3u);
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
        BattleUiTitleMotionSettings titleMotion = null, bool overflow = false, float feedbackScale = 1f, float entrance = 1f,
        bool primary = false, bool cmdOption = false, bool cmdSelected = false, Color? sourceTint = null, bool topOverflow = true)
    {
        sourceColor = sourceTint ?? source.color;
        decoratedTitle = decorate;
        cartoon = BattleUiStyleController.IsComic;
        bool nextPoster = decorate && (cartoon || BattleUiStyleController.IsStylish);
        if (poster != nextPoster) { poster = nextPoster; ResetMotion(); text.SetVerticesDirty(); }
        posterSettings = style;
        if (poster && backing == null)
        {
            GameObject plate = new("StyleTitleBacking", typeof(RectTransform), typeof(LayoutElement));
            plate.layer = source.gameObject.layer;
            plate.transform.SetParent(source.transform, false);
            plate.transform.SetSiblingIndex(text.transform.GetSiblingIndex());
            plate.GetComponent<LayoutElement>().ignoreLayout = true;
            backing = plate.AddComponent<BattleUiTitleBacking>();
            backing.raycastTarget = false;
            BattleUiOpticsCanvas.Register(backing);
        }
        bool cmd = BattleUiStyleController.IsCmd;
        string content = (label != null ? label.text : native.text) ?? string.Empty;
        if (cmd && content.Length > 0)
            content = cmdOption ? (cmdSelected ? "[ " + content + " ]" : "[ ] " + content) : decorate ? "> " + content : content;
        if (checkedFont != font || checkedText != content || checkedCmd != cmd)
        {
            checkedFont = font;
            checkedText = content;
            checkedCmd = cmd;
            resolvedFont = cmd ? BattleUiThemeFonts.ResolveCmdTmp(font, content) : font;
            supported = cmd ? BattleUiThemeFonts.SupportsCmdTmp(resolvedFont, content) :
                resolvedFont != null && resolvedFont.HasCharacters(content, out uint[] _, true, true);
        }
        font = resolvedFont;
        if (!source.isActiveAndEnabled || (titleMotion != null &&
            (sourceColor.a <= 0f || source.canvasRenderer.GetInheritedAlpha() <= 0f))) { Hide(); return; }
        if (!supported) { Hide(titleMotion != null && motion == titleMotion); return; }
        bool nextOverflow = !poster && titleMotion != null && overflow;
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
            if (material != null) Release(material);
            text.font = font;
            material = new Material(font.material) { hideFlags = HideFlags.HideAndDontSave };
            material.EnableKeyword("UNDERLAY_ON");
            text.fontSharedMaterial = material;
            outlineWidth = -1f;
        }
        Color nextOutline = poster ? (cartoon ? style.comicWhite : style.inkCyan) : style.outerFrame;
        Color nextShadow = poster ? (cartoon ? style.comicInk : style.inkBackground) : birthday ? style.energy : style.status;
        bool paper = BattleUiStyleController.IsPaper;
        float nextWidth = poster ? Mathf.Clamp01(Mathf.Clamp(style.posterTitleStrokeRatio, 0.06f, 0.1f) *
            font.faceInfo.pointSize / Mathf.Max(1f, material.GetFloat("_GradientScale"))) :
            decorate && !paper && !cmd ? style.titleOutlineWidth : 0f;
        float nextGlow = poster && !cartoon ? Mathf.Clamp(style.inkGlowWidth, 8f, 14f) : 0f;
        if (outline != nextOutline || shadow != nextShadow || outlineWidth != nextWidth ||
            shadowOffset != style.titleShadowOffset || glowWidth != nextGlow)
        {
            outline = nextOutline;
            shadow = nextShadow;
            outlineWidth = nextWidth;
            glowWidth = nextGlow;
            shadowOffset = style.titleShadowOffset;
            material.SetColor("_OutlineColor", outline);
            material.SetFloat("_OutlineWidth", outlineWidth);
            material.SetColor("_UnderlayColor", shadow);
            material.SetFloat("_UnderlayOffsetX", shadowOffset.x);
            material.SetFloat("_UnderlayOffsetY", shadowOffset.y);
            material.SetFloat("_UnderlayDilate", poster ? Mathf.Clamp01(nextWidth + 0.15f) : font.material.GetFloat("_UnderlayDilate"));
            if (material.HasProperty("_GlowColor"))
            {
                if (nextGlow > 0f)
                {
                    material.EnableKeyword("GLOW_ON");
                    Color halo = style.inkCyan;
                    halo.a *= 0.45f;
                    material.SetColor("_GlowColor", halo);
                    material.SetFloat("_GlowOuter", Mathf.Clamp01(nextGlow / Mathf.Max(1f, SourceSize)));
                }
                else material.DisableKeyword("GLOW_ON");
            }
            text.UpdateMeshPadding();
            text.SetMaterialDirty();
            text.SetVerticesDirty();
        }
        if (decorate && !paper && !cmd) material.EnableKeyword("UNDERLAY_ON");
        else material.DisableKeyword("UNDERLAY_ON");
        float nextShear = poster ? 0f : decorate && !birthday && !paper && !cmd ? style.stylishTitleShear : 0f;
        float nextAngle = poster ? 0f : decorate && !cmd ? (paper ? paperAngle * Mathf.Clamp01(style.paperLabelAngle) :
            !birthday ? style.stylishTitleAngle : 0f) : 0f;
        if (titleAngle != nextAngle) { titleAngle = nextAngle; text.SetVerticesDirty(); }
        Vector3 nextScale = Vector3.one * feedbackScale;
        if (text.rectTransform.localScale != nextScale) text.rectTransform.localScale = nextScale;
        float nextPadding = decorate ? Mathf.Max(0f, style.titlePadding) : 0f;
        float margin = nextOverflow ? SourceSize * style.titleSizeMultiplier * Mathf.Max(0f, motion.overflowMargin) : 0f;
        Vector2 expansion = Vector2.one * margin;
        if (text.rectTransform.offsetMin != -expansion) text.rectTransform.offsetMin = -expansion;
        Vector2 topExpansion = expansion;
        if (poster && topOverflow && source.transform.parent is RectTransform parentRect)
            topExpansion.y = Mathf.Max(0f, Mathf.Min(parentRect.rect.height * Mathf.Clamp(style.posterTitleTopOverflow, 0f, 0.15f),
                source.rectTransform.InverseTransformPoint(parentRect.TransformPoint(new Vector3(0f,
                    parentRect.rect.yMax + parentRect.rect.height * Mathf.Clamp(style.posterTitleTopOverflow, 0f, 0.15f), 0f))).y - source.rectTransform.rect.yMax));
        if (text.rectTransform.offsetMax != topExpansion) text.rectTransform.offsetMax = topExpansion;
        if (poster)
        {
            RectTransform plateRect = backing.rectTransform;
            plateRect.anchorMin = text.rectTransform.anchorMin;
            plateRect.anchorMax = text.rectTransform.anchorMax;
            plateRect.offsetMin = text.rectTransform.offsetMin;
            plateRect.offsetMax = text.rectTransform.offsetMax;
            plateRect.localScale = text.rectTransform.localScale;
            backing.maskable = source.maskable;
        }
        if (shear != nextShear || padding != nextPadding)
        {
            shear = nextShear;
            padding = nextPadding;
            text.margin = new Vector4(padding, padding, padding, padding);
            text.SetVerticesDirty();
        }
        text.text = content;
        text.richText = label != null ? label.supportRichText : native.richText;
        text.fontSize = poster ? bodySize * Mathf.Clamp(style.posterTitleBodyRatio, 1.8f, 2.4f) :
            SourceSize * (decorate ? style.titleSizeMultiplier : 1f);
        backingExtent = poster ? text.fontSize * Mathf.Clamp(style.posterTitleStrokeRatio, 0.06f, 0.1f) : 0f;
        text.enableAutoSizing = !decorate && (label != null ? label.resizeTextForBestFit : native.enableAutoSizing);
        text.fontSizeMin = label != null ? label.resizeTextMinSize : native.fontSizeMin;
        text.fontSizeMax = label != null ? label.resizeTextMaxSize : native.fontSizeMax;
        text.lineSpacing = decorate ? 0f : label != null ? (label.lineSpacing - 1f) * SourceSize : native.lineSpacing;
        text.fontStyle = decorate || cmd ? FontStyles.Normal : label == null ? native.fontStyle : label.fontStyle switch
        {
            FontStyle.Bold => FontStyles.Bold,
            FontStyle.Italic => FontStyles.Italic,
            FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
            _ => FontStyles.Normal
        };
        text.alignment = label != null ? Alignment(cmd && (decorate || cmdOption)
            ? (TextAnchor)((int)label.alignment / 3 * 3) : label.alignment) : native.alignment;
        text.textWrappingMode = label != null ? (label.horizontalOverflow == HorizontalWrapMode.Wrap
            ? TextWrappingModes.Normal : TextWrappingModes.NoWrap) : native.textWrappingMode;
        text.maskable = source.maskable;
        text.overflowMode = decorate ? TextOverflowModes.Overflow : label != null
            ? (label.verticalOverflow == VerticalWrapMode.Overflow ? TextOverflowModes.Overflow : TextOverflowModes.Truncate) : native.overflowMode;
        Color face = decorate ? (birthday ? style.status : style.innerLine) : BattleUiStyleMeshEffect.TextColor(sourceColor, style);
        if (poster) face = cartoon ? style.comicInk : style.inkText;
        if (cmd)
        {
            face = cmdSelected ? style.highlight : decorate ? style.outerFrame : style.innerLine;
            if (!decorate) face.a *= sourceColor.a;
        }
        if (primary) face = BattleUiStyleController.IsStylish ? style.inkBackground :
            new Color(style.outerFrame.r, style.outerFrame.g, style.outerFrame.b, sourceColor.a);
        if (decorate) face.a *= sourceColor.a;
        face.a *= entrance;
        text.color = face;
        if (backing != null) backing.gameObject.SetActive(poster && content.Length > 0 && sourceColor.a > 0f);
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
        if (backing != null) backing.gameObject.SetActive(false);
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
            Release(material);
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
        FinishPoster(info);
    }

    private void FitToRect(TMP_TextInfo info, float titleShear)
    {
        Quaternion rotation = Quaternion.Euler(0f, 0f, titleAngle);
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
                Vector3 vertex = rotation * vertices[character.vertexIndex + j];
                vertex.x += titleShear * vertex.y;
                min = Vector2.Min(min, vertex);
                max = Vector2.Max(max, vertex);
            }
        }
        if (min.x == float.MaxValue) return;
        Vector2 size = max - min, center = (min + max) * 0.5f;
        float extent = poster ? BackingMargin(backingExtent) : 0f;
        float scale = Mathf.Min(1f, Mathf.Min(rect.width / Mathf.Max(0.001f, size.x + extent * 2f),
            rect.height / Mathf.Max(0.001f, size.y + extent * 2f)));
        if (poster) fittedTitleScale = scale;
        Vector2 half = (size * 0.5f + Vector2.one * extent) * scale;
        Vector2 fitted = new(Mathf.Clamp(center.x, rect.xMin + half.x, rect.xMax - half.x),
            Mathf.Clamp(center.y, rect.yMin + half.y, rect.yMax - half.y));
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];
            if (!character.isVisible) continue;
            Vector3[] vertices = info.meshInfo[character.materialReferenceIndex].vertices;
            for (int j = 0; j < 4; j++)
            {
                Vector3 vertex = rotation * vertices[character.vertexIndex + j];
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
        FinishPoster(text.textInfo);
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
                ? motion.characters[(i + (poster && cartoon ? posterPatternOffset : 0)) % motion.characters.Length] : null;
            Vector2 offset = transform != null ? transform.offset * height : Vector2.zero;
            if (motion.curveLines) offset.y += 4f * along * (1f - along) * motion.curveHeight * height;
            float bounceOffset = 0f;
            float motionY = poster ? 1f : motion.scale.y;
            float bounceExtent = motion.bounce ? Mathf.Max(0f, motion.bounceAmplitude) * height * Mathf.Abs(motionY) : 0f;
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
            if (poster && cartoon)
            {
                glyphScale = Vector2.one * Mathf.Clamp(glyphScale.y, 0.85f, 1.25f);
                angle = Mathf.Clamp(transform != null ? transform.rotation : 0f, -10f, 10f) * Mathf.Deg2Rad;
                sin = Mathf.Sin(angle); cos = Mathf.Cos(angle);
            }
            else if (poster)
            {
                glyphScale = Vector2.one * Mathf.Clamp(glyphScale.y, 0.96f, 1.04f);
                angle = Mathf.Clamp(transform != null ? transform.rotation : 0f, -3f, 3f) * Mathf.Deg2Rad;
                sin = Mathf.Sin(angle); cos = Mathf.Cos(angle);
            }
            float glyphShear = poster ? 0f : shear + (transform != null ? transform.shear : 0f);
            Vector2 lineScale = poster ? Vector2.one : motion.scale;
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
                position = lineCenter + Vector2.Scale(position - lineCenter, lineScale);
                vertices[index] = new Vector3(position.x, position.y, original.z);
                float restY = position.y - bounceOffset * motionY;
                boundsMin = Vector3.Min(boundsMin, new Vector3(position.x, restY - bounceExtent, original.z));
                boundsMax = Vector3.Max(boundsMax, new Vector3(position.x, restY + bounceExtent, original.z));
                if (motion.accentCharacters && (!poster || !cartoon))
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

    private void FinishPoster(TMP_TextInfo info)
    {
        if (!poster) return;
        backing.SetGeometry(info, posterSettings, backingExtent * fittedTitleScale, !cartoon,
            new Color(1f, 1f, 1f, text.color.a), fittedTitleScale);
        backing.canvasRenderer.SetColor(source.canvasRenderer.GetColor());
        Vector3 min = new(float.MaxValue, float.MaxValue, 0f), max = new(float.MinValue, float.MinValue, 0f);
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];
            if (!character.isVisible) continue;
            Vector3[] v = info.meshInfo[character.materialReferenceIndex].vertices;
            for (int j = 0; j < 4; j++)
            {
                min = Vector3.Min(min, v[character.vertexIndex + j]);
                max = Vector3.Max(max, v[character.vertexIndex + j]);
            }
        }
        if (min.x == float.MaxValue) { text.ClearMotionBounds(); return; }
        float extent = BackingMargin(backingExtent) * fittedTitleScale;
        Bounds bounds = new((min + max) * 0.5f, max - min);
        bounds.Expand(extent * 2f);
        text.SetMotionBounds(bounds);
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
            Release(text.gameObject);
        }
        if (material != null) Release(material);
        if (backing != null) Release(backing.gameObject);
        ResetMotion();
        subMeshes.Clear();
    }

    private static void Release(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}

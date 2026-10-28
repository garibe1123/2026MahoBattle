using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleUiRetroJapanShopPlate : MaskableGraphic
{
    private BattleUiStyleSettings settings;
    private float opacity;

    internal void Configure(BattleUiStyleSettings style, float alpha)
    {
        bool changed = settings != style || opacity != alpha;
        settings = style;
        opacity = alpha;
        raycastTarget = false;
        if (changed) SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (settings == null || !BattleUiStyleController.IsRetroJapanShop) return;
        BattleUiRetroJapanShopDrawing.Banner(vh, rectTransform.rect, settings, opacity);
    }
}

internal static class BattleUiRetroJapanShopDrawing
{
    internal static bool IsBadge(string name) => name == "ReactionTag" || name == "ReactionTagBadge";

    internal static Color Tint(Color color, float alpha)
    {
        color.a *= alpha;
        return color;
    }

    internal static Rect BadgeRect(Rect rect, BattleUiStyleSettings style)
    {
        float size = Mathf.Min(rect.width, rect.height) * Mathf.Clamp(style.retroShopBadgeSizeRatio, 0.7f, 1f);
        return new Rect(rect.center - Vector2.one * size * 0.5f, Vector2.one * size);
    }

    internal static void Badge(VertexHelper vh, Rect rect, BattleUiStyleSettings style, float alpha)
    {
        rect = BadgeRect(rect, style);
        Star(vh, rect, style, Tint(style.retroShopRed, alpha));
        float border = Mathf.Min(Mathf.Max(0f, style.retroShopBorderWidth), rect.height * 0.15f);
        Star(vh, BattleUiStyleMeshEffect.Inset(rect, border), style, Tint(style.retroShopYellow, alpha));
    }

    private static void Star(VertexHelper vh, Rect rect, BattleUiStyleSettings style, Color tint)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        int count = Mathf.Clamp(style.retroShopBadgeTeeth, 8, 24) * 2;
        int first = vh.currentVertCount;
        vh.AddVert(rect.center, tint, Vector2.zero);
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.PI * 2f / count;
            float radius = (i & 1) == 0 ? 1f : Mathf.Clamp(style.retroShopBadgeInnerRatio, 0.65f, 1f);
            vh.AddVert(rect.center + new Vector2(Mathf.Cos(angle) * rect.width, Mathf.Sin(angle) * rect.height) * (radius * 0.5f), tint, Vector2.zero);
        }
        for (int i = 0; i < count; i++)
            vh.AddTriangle(first, first + 1 + (i + 1) % count, first + 1 + i);
    }

    internal static void Frame(VertexHelper vh, Rect rect, BattleUiStyleSettings style, float alpha,
        bool button = false, bool highlighted = false, bool header = false)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        float width = Mathf.Min(Mathf.Max(0f, style.retroShopBorderWidth), Mathf.Min(rect.width, rect.height) * 0.12f);
        BattleUiStyleMeshEffect.Quad(vh, rect, Tint(style.retroShopInk, alpha));
        BattleUiStyleMeshEffect.Quad(vh, BattleUiStyleMeshEffect.Inset(rect, width), Tint(highlighted ? style.retroShopYellow : style.retroShopRed, alpha));
        Rect inner = BattleUiStyleMeshEffect.Inset(rect, width * 2f);
        BattleUiStyleMeshEffect.Quad(vh, inner, Tint(button ? style.retroShopYellow : style.retroShopWhite, alpha));
        if (!header || button || inner.height <= 0f) return;
        float height = inner.height * Mathf.Clamp(style.retroShopHeaderRatio, 0.1f, 0.3f);
        Rect strip = new(inner.xMin, inner.yMax - height, inner.width, height);
        Banner(vh, strip, style, alpha);
    }

    internal static void Banner(VertexHelper vh, Rect rect, BattleUiStyleSettings style, float alpha)
    {
        if (rect.width <= 0f || rect.height <= 0f) return;
        float angle = Mathf.Clamp(style.retroShopBannerAngle, 5f, 10f) * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        float scale = Mathf.Min(rect.width / (rect.width * cos + rect.height * sin),
            rect.height / (rect.width * sin + rect.height * cos));
        Vector2 size = rect.size * scale;
        float width = Mathf.Min(Mathf.Max(0f, style.retroShopBorderWidth), Mathf.Min(size.x, size.y) * 0.15f);
        Sign(vh, rect.center, size, cos, sin, Tint(style.retroShopRed, alpha));
        Sign(vh, rect.center, size - Vector2.one * width * 2f, cos, sin, Tint(style.retroShopWhite, alpha));
        Sign(vh, rect.center, size - Vector2.one * width * 4f, cos, sin, Tint(style.retroShopYellow, alpha));
    }

    private static void Sign(VertexHelper vh, Vector2 center, Vector2 size, float cos, float sin, Color tint)
    {
        if (size.x <= 0f || size.y <= 0f) return;
        Vector2 across = new Vector2(cos, sin) * size.x * 0.5f;
        Vector2 up = new Vector2(-sin, cos) * size.y * 0.5f;
        int first = vh.currentVertCount;
        vh.AddVert(center - across - up, tint, Vector2.zero);
        vh.AddVert(center - across + up, tint, Vector2.zero);
        vh.AddVert(center + across + up, tint, Vector2.zero);
        vh.AddVert(center + across - up, tint, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }
}

internal sealed class BattleUiRetroJapanShopTitle : IDisposable
{
    private readonly MaskableGraphic source;
    private readonly TextMeshProUGUI outline;
    private readonly BattleUiRetroJapanShopPlate plate;
    private readonly Action<TMP_TextInfo> fit;
    private readonly List<TMP_SubMeshUI> subMeshes = new();
    private Material material;

    internal BattleUiRetroJapanShopTitle(MaskableGraphic owner, TextMeshProUGUI text, Action<TMP_TextInfo> fitOutline)
    {
        source = owner;
        fit = fitOutline;
        GameObject banner = new("RetroJapanShopBanner", typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement));
        banner.layer = source.gameObject.layer;
        banner.transform.SetParent(source.transform, false);
        banner.transform.SetSiblingIndex(text.transform.GetSiblingIndex());
        banner.GetComponent<LayoutElement>().ignoreLayout = true;
        plate = banner.AddComponent<BattleUiRetroJapanShopPlate>();
        GameObject outer = new("RetroJapanShopOutline", typeof(RectTransform), typeof(LayoutElement));
        outer.layer = source.gameObject.layer;
        outer.transform.SetParent(source.transform, false);
        outer.transform.SetSiblingIndex(text.transform.GetSiblingIndex());
        outer.GetComponent<LayoutElement>().ignoreLayout = true;
        outline = outer.AddComponent<TextMeshProUGUI>();
        outline.raycastTarget = false;
        outline.OnPreRenderText += FitOutline;
        BattleUiOpticsCanvas.Register(plate);
        BattleUiOpticsCanvas.Register(outline);
    }

    internal void Apply(TextMeshProUGUI text, BattleUiStyleSettings style, float stroke, float alpha, bool banner)
    {
        if (outline.font != text.font || material == null || material.mainTexture == null)
        {
            if (material != null) Release(material);
            outline.font = text.font;
            material = new Material(text.font.material) { hideFlags = HideFlags.HideAndDontSave };
            material.DisableKeyword("UNDERLAY_ON");
            material.DisableKeyword("GLOW_ON");
            outline.fontSharedMaterial = material;
        }
        float width = Mathf.Clamp01(stroke * Mathf.Clamp(style.retroShopOuterStrokeScale, 1.2f, 2f));
        if (material.GetColor("_OutlineColor") != style.retroShopInk || material.GetFloat("_OutlineWidth") != width)
        {
            material.SetColor("_OutlineColor", style.retroShopInk);
            material.SetFloat("_OutlineWidth", width);
            outline.UpdateMeshPadding();
            outline.SetMaterialDirty();
            outline.SetVerticesDirty();
        }
        CopyRect(outline.rectTransform, text.rectTransform);
        CopyRect(plate.rectTransform, text.rectTransform);
        plate.maskable = source.maskable;
        plate.Configure(style, alpha);
        plate.gameObject.SetActive(banner && text.text.Length > 0 && alpha > 0f);
        plate.canvasRenderer.SetColor(source.canvasRenderer.GetColor());
        outline.fontSize = text.fontSize;
        outline.fontStyle = text.fontStyle;
        outline.alignment = text.alignment;
        outline.textWrappingMode = text.textWrappingMode;
        outline.overflowMode = text.overflowMode;
        outline.margin = text.margin;
        outline.richText = text.richText;
        outline.maskable = source.maskable;
        outline.color = BattleUiRetroJapanShopDrawing.Tint(style.retroShopInk, alpha);
        outline.text = text.text;
        outline.canvasRenderer.SetColor(source.canvasRenderer.GetColor());
        outline.gameObject.SetActive(true);
    }

    private static void CopyRect(RectTransform target, RectTransform from)
    {
        target.anchorMin = from.anchorMin;
        target.anchorMax = from.anchorMax;
        target.pivot = from.pivot;
        target.offsetMin = from.offsetMin;
        target.offsetMax = from.offsetMax;
        target.localScale = from.localScale;
    }

    private void FitOutline(TMP_TextInfo info)
    {
        fit(info);
        outline.GetComponentsInChildren(true, subMeshes);
        foreach (TMP_SubMeshUI subMesh in subMeshes)
        {
            subMesh.raycastTarget = false;
            subMesh.maskable = source.maskable;
        }
    }

    internal void Hide()
    {
        if (outline != null) outline.gameObject.SetActive(false);
        if (plate != null) plate.gameObject.SetActive(false);
    }

    internal void Refresh()
    {
        if (outline != null) outline.SetVerticesDirty();
        if (plate != null) plate.SetVerticesDirty();
    }

    public void Dispose()
    {
        if (outline != null)
        {
            outline.OnPreRenderText -= FitOutline;
            Release(outline.gameObject);
        }
        if (plate != null) Release(plate.gameObject);
        if (material != null) Release(material);
        subMeshes.Clear();
    }

    private static void Release(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}

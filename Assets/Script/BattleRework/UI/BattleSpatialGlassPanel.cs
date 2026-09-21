using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 선/Outline 대신 면 자체의 투명도와 기울기로 Glass Surface를 그립니다.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleGlassSurfaceGraphic : MaskableGraphic
{
    [SerializeField, Range(-0.30f, 0.30f)] private float skew = 0.06f;
    [SerializeField, Range(0f, 1f)] private float topAlpha = 0.70f;
    [SerializeField, Range(0f, 1f)] private float bottomAlpha = 0.36f;

    public void Configure(Color tint, float skewAmount, float top, float bottom)
    {
        color = tint;
        skew = Mathf.Clamp(skewAmount, -0.30f, 0.30f);
        topAlpha = Mathf.Clamp01(top);
        bottomAlpha = Mathf.Clamp01(bottom);
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;
        float shift = r.width * skew;

        Vector2 bl = new(r.xMin, r.yMin);
        Vector2 br = new(r.xMax - shift, r.yMin);
        Vector2 tr = new(r.xMax, r.yMax);
        Vector2 tl = new(r.xMin + shift, r.yMax);

        Color bottom = color;
        bottom.a *= bottomAlpha;

        Color top = color;
        top.a *= topAlpha;

        vh.AddVert(bl, bottom, new Vector2(0f, 0f));
        vh.AddVert(br, bottom, new Vector2(1f, 0f));
        vh.AddVert(tr, top, new Vector2(1f, 1f));
        vh.AddVert(tl, top, new Vector2(0f, 1f));

        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 3);
    }
}

/// <summary>
/// Splatoon 3 계열의 공간감 + Glass 물성을 위한 공용 패널.
/// Frame/Outline을 추가하지 않고 Shadow Surface, Glass Surface, Key Surface 세 개의 면을 깊이차로 배치합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpatialGlassPanel : MonoBehaviour
{
    private const string ShadowName = "__Spatial_Shadow";
    private const string GlassName = "__Spatial_Glass";
    private const string KeyName = "__Spatial_Key";

    [SerializeField, Range(0.08f, 0.45f)] private float keyArea = 0.16f;
    [SerializeField] private bool keyOnLeft = true;
    [SerializeField, Range(-0.24f, 0.24f)] private float skew = 0.055f;
    [SerializeField] private Vector2 shadowOffset = new(10f, -12f);

    private RectTransform shadow;
    private RectTransform glass;
    private RectTransform key;
    private BattleGlassSurfaceGraphic shadowGraphic;
    private BattleGlassSurfaceGraphic glassGraphic;
    private BattleGlassSurfaceGraphic keyGraphic;
    private BattleUIThemeController themeController;

    private float focus;
    private float depthBias;
    private float visualFocus;
    private float visualDepth;

    public float Focus => focus;

    public void Configure(bool leftKey, float keyFraction = 0.16f, float skewAmount = 0.055f)
    {
        keyOnLeft = leftKey;
        keyArea = Mathf.Clamp(keyFraction, 0.08f, 0.45f);
        skew = Mathf.Clamp(skewAmount, -0.24f, 0.24f);
        EnsureLayers();
        ApplyTheme();
        ApplyLayerGeometry();
    }

    public void SetSpatialState(float focus01, float depth)
    {
        focus = Mathf.Clamp01(focus01);
        depthBias = Mathf.Clamp(depth, -1f, 1f);
    }

    private void Awake()
    {
        EnsureLayers();
        ResolveTheme();
        ApplyTheme();
        ApplyLayerGeometry();
    }

    private void OnEnable()
    {
        EnsureLayers();
        ResolveTheme();
        SubscribeTheme();
        ApplyTheme();
    }

    private void OnDisable()
    {
        UnsubscribeTheme();
    }

    private void Update()
    {
        ResolveTheme();
        SubscribeTheme();

        float t = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
        visualFocus = Mathf.Lerp(visualFocus, focus, t);
        visualDepth = Mathf.Lerp(visualDepth, depthBias, t);

        ApplySpatialDepth();
    }

    private void ResolveTheme()
    {
        if (themeController == null)
            themeController = BattleUIThemeController.Instance != null
                ? BattleUIThemeController.Instance
                : FindFirstObjectByType<BattleUIThemeController>(FindObjectsInactive.Include);
    }

    private BattleUIThemeController subscribedTheme;

    private void SubscribeTheme()
    {
        if (subscribedTheme == themeController)
            return;

        UnsubscribeTheme();
        subscribedTheme = themeController;
        if (subscribedTheme != null)
            subscribedTheme.ThemeChanged += HandleThemeChanged;
    }

    private void UnsubscribeTheme()
    {
        if (subscribedTheme != null)
            subscribedTheme.ThemeChanged -= HandleThemeChanged;
        subscribedTheme = null;
    }

    private void HandleThemeChanged(BattleUIThemeProfile _)
    {
        ApplyTheme();
    }

    private void EnsureLayers()
    {
        RectTransform owner = transform as RectTransform;
        if (owner == null)
            return;

        shadow = EnsureLayer(owner, ShadowName, out shadowGraphic);
        glass = EnsureLayer(owner, GlassName, out glassGraphic);
        key = EnsureLayer(owner, KeyName, out keyGraphic);

        shadow.SetAsFirstSibling();
        glass.SetSiblingIndex(1);
        key.SetSiblingIndex(2);

        ApplyLayerGeometry();
    }

    private static RectTransform EnsureLayer(
        RectTransform parent,
        string name,
        out BattleGlassSurfaceGraphic graphic)
    {
        RectTransform rect = parent.Find(name) as RectTransform;
        if (rect == null)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            rect = go.AddComponent<RectTransform>();
            graphic = go.AddComponent<BattleGlassSurfaceGraphic>();
        }
        else
        {
            graphic = rect.GetComponent<BattleGlassSurfaceGraphic>();
            if (graphic == null)
                graphic = rect.gameObject.AddComponent<BattleGlassSurfaceGraphic>();
        }

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        return rect;
    }

    private void ApplyLayerGeometry()
    {
        if (shadow == null || glass == null || key == null)
            return;

        shadow.anchorMin = Vector2.zero;
        shadow.anchorMax = Vector2.one;
        shadow.offsetMin = shadowOffset;
        shadow.offsetMax = shadowOffset;

        glass.anchorMin = Vector2.zero;
        glass.anchorMax = Vector2.one;
        glass.offsetMin = Vector2.zero;
        glass.offsetMax = Vector2.zero;

        if (keyOnLeft)
        {
            key.anchorMin = Vector2.zero;
            key.anchorMax = new Vector2(keyArea, 1f);
            key.offsetMin = new Vector2(-10f, 4f);
            key.offsetMax = new Vector2(2f, -8f);
        }
        else
        {
            key.anchorMin = new Vector2(1f - keyArea, 0f);
            key.anchorMax = Vector2.one;
            key.offsetMin = new Vector2(-2f, 4f);
            key.offsetMax = new Vector2(10f, -8f);
        }

        shadowGraphic?.Configure(Color.black, skew * 0.65f, 0.56f, 0.28f);
        glassGraphic?.Configure(Color.white, skew, 0.76f, 0.44f);
        keyGraphic?.Configure(Color.white, -skew * 0.8f, 0.92f, 0.70f);
    }

    private void ApplyTheme()
    {
        if (shadowGraphic == null || glassGraphic == null || keyGraphic == null)
            return;

        BattleUIThemeProfile theme = themeController != null
            ? themeController.CurrentProfile
            : null;

        Color shadowColor = theme != null
            ? theme.depthShadow
            : new Color(0.01f, 0.012f, 0.018f, 0.48f);

        Color glassColor = theme != null
            ? theme.glassTint
            : new Color(0.80f, 0.84f, 0.88f, 0.18f);

        Color keyColor = theme != null
            ? theme.keyColor
            : new Color(1f, 0.82f, 0.10f, 1f);

        shadowGraphic.Configure(shadowColor, skew * 0.65f, 0.72f, 0.42f);
        glassGraphic.Configure(glassColor, skew, 0.82f, 0.52f);

        keyColor.a *= 0.84f;
        keyGraphic.Configure(keyColor, -skew * 0.8f, 0.96f, 0.72f);
    }

    private void ApplySpatialDepth()
    {
        if (shadow == null || glass == null || key == null)
            return;

        float forward = visualFocus;
        float back = Mathf.Max(0f, -visualDepth);
        float front = Mathf.Max(0f, visualDepth);

        shadow.localPosition = new Vector3(
            shadowOffset.x * (1f + forward * 0.35f),
            shadowOffset.y * (1f + forward * 0.35f),
            16f + back * 10f);

        glass.localPosition = new Vector3(0f, 0f, 8f - front * 4f);
        key.localPosition = new Vector3(
            (keyOnLeft ? -1f : 1f) * forward * 5f,
            forward * 2f,
            -6f - front * 8f);

        float keyScale = 1f + forward * 0.06f;
        key.localScale = new Vector3(keyScale, keyScale, 1f);
    }
}

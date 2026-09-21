using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Persona 4 계열의 강한 비대칭 실루엣을 만드는 공용 UI Graphic입니다.
/// 기존 Image/Outline 계약은 건드리지 않고, 별도 Child Graphic으로 프레임 몸체를 생성합니다.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattlePersona4PanelGraphic : MaskableGraphic
{
    [SerializeField, Min(0f)] private float cutTopLeft = 0f;
    [SerializeField, Min(0f)] private float cutTopRight = 34f;
    [SerializeField, Min(0f)] private float cutBottomRight = 10f;
    [SerializeField, Min(0f)] private float cutBottomLeft = 24f;

    public void Configure(Color fill, float topLeft, float topRight, float bottomRight, float bottomLeft)
    {
        color = fill;
        cutTopLeft = Mathf.Max(0f, topLeft);
        cutTopRight = Mathf.Max(0f, topRight);
        cutBottomRight = Mathf.Max(0f, bottomRight);
        cutBottomLeft = Mathf.Max(0f, bottomLeft);
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;
        float halfMin = Mathf.Max(0f, Mathf.Min(r.width, r.height) * 0.48f);

        float tl = Mathf.Min(cutTopLeft, halfMin);
        float tr = Mathf.Min(cutTopRight, halfMin);
        float br = Mathf.Min(cutBottomRight, halfMin);
        float bl = Mathf.Min(cutBottomLeft, halfMin);

        Vector2[] points =
        {
            new(r.xMin + bl, r.yMin),
            new(r.xMax - br, r.yMin),
            new(r.xMax, r.yMin + br),
            new(r.xMax, r.yMax - tr),
            new(r.xMax - tr, r.yMax),
            new(r.xMin + tl, r.yMax),
            new(r.xMin, r.yMax - tl),
            new(r.xMin, r.yMin + bl)
        };

        Color32 c = color;
        for (int i = 0; i < points.Length; i++)
            vh.AddVert(points[i], c, Vector2.zero);

        for (int i = 1; i < points.Length - 1; i++)
            vh.AddTriangle(0, i, i + 1);
    }
}

/// <summary>
/// 기존 UI 루트에 P4형 3단 레이어 프레임을 입힙니다.
/// 얇은 선 장식이 아니라 Back Plate / Main Body / Accent Block의 면 구조를 사용합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattlePersona4FrameDecorator : MonoBehaviour
{
    private const string BackName = "__P4_BackPlate";
    private const string BodyName = "__P4_MainBody";
    private const string AccentName = "__P4_AccentBlock";

    [SerializeField] private Color bodyColor = new(0.025f, 0.028f, 0.035f, 0.98f);
    [SerializeField] private Color backColor = new(0.94f, 0.90f, 0.16f, 1f);
    [SerializeField] private Color accentColor = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Vector2 backOffset = new(7f, -7f);
    [SerializeField, Range(0.08f, 0.40f)] private float accentWidth = 0.18f;
    [SerializeField] private bool accentOnLeft = true;

    private RectTransform back;
    private RectTransform body;
    private RectTransform accent;

    public void Configure(
        Color body,
        Color backPlate,
        Color accentPlate,
        bool leftAccent,
        float accentFraction = 0.18f,
        Vector2? plateOffset = null)
    {
        bodyColor = body;
        backColor = backPlate;
        accentColor = accentPlate;
        accentOnLeft = leftAccent;
        accentWidth = Mathf.Clamp(accentFraction, 0.08f, 0.40f);
        if (plateOffset.HasValue)
            backOffset = plateOffset.Value;

        EnsureVisuals();
        ApplyVisuals();
    }

    private void Awake()
    {
        EnsureVisuals();
        ApplyVisuals();
    }

    private void OnEnable()
    {
        EnsureVisuals();
        ApplyVisuals();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
            return;

        EnsureVisuals();
        ApplyVisuals();
    }

    private void EnsureVisuals()
    {
        RectTransform owner = transform as RectTransform;
        if (owner == null)
            return;

        back = EnsureLayer(owner, BackName);
        body = EnsureLayer(owner, BodyName);
        accent = EnsureLayer(owner, AccentName);

        back.SetAsFirstSibling();
        body.SetSiblingIndex(1);
        accent.SetSiblingIndex(2);
    }

    private static RectTransform EnsureLayer(RectTransform parent, string name)
    {
        RectTransform existing = parent.Find(name) as RectTransform;
        if (existing != null)
            return existing;

        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        go.AddComponent<BattlePersona4PanelGraphic>();
        return rect;
    }

    private void ApplyVisuals()
    {
        if (back == null || body == null || accent == null)
            return;

        back.anchorMin = Vector2.zero;
        back.anchorMax = Vector2.one;
        back.offsetMin = backOffset;
        back.offsetMax = backOffset;

        BattlePersona4PanelGraphic backGraphic = back.GetComponent<BattlePersona4PanelGraphic>();
        backGraphic.Configure(backColor, 18f, 46f, 8f, 30f);

        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = Vector2.zero;
        body.offsetMax = Vector2.zero;

        BattlePersona4PanelGraphic bodyGraphic = body.GetComponent<BattlePersona4PanelGraphic>();
        bodyGraphic.Configure(bodyColor, 4f, 38f, 12f, 26f);

        if (accentOnLeft)
        {
            accent.anchorMin = Vector2.zero;
            accent.anchorMax = new Vector2(accentWidth, 1f);
            accent.offsetMin = new Vector2(-8f, 0f);
            accent.offsetMax = new Vector2(4f, 0f);
        }
        else
        {
            accent.anchorMin = new Vector2(1f - accentWidth, 0f);
            accent.anchorMax = Vector2.one;
            accent.offsetMin = new Vector2(-4f, 0f);
            accent.offsetMax = new Vector2(8f, 0f);
        }

        BattlePersona4PanelGraphic accentGraphic = accent.GetComponent<BattlePersona4PanelGraphic>();
        accentGraphic.Configure(accentColor, 0f, 22f, 6f, 16f);
    }
}

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reward PACK의 NEXT STAGE 버튼 시각만 담당합니다.
///
/// 입력/클릭은 기존 RewardPackDone Button이 계속 소유합니다.
/// Custom MaskableGraphic은 Canvas/Mask 조합에서 사라지는 문제가 있었기 때문에 사용하지 않고,
/// 런타임에서 생성한 흰색 Sprite를 일반 Image에 넣어 렌더합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(45000)]
public sealed class BattleDoneNextArrowPresentationController : MonoBehaviour
{
    private const string DoneName = "RewardPackDone";
    private const string VisualName = "DoneNextArrowVisual";
    private const string ShadowName = "ArrowShadowGroup";
    private const string FillName = "ArrowFillGroup";
    private const string MainAccentName = "ArrowSpeedLine";
    private const string SmallAccentName = "ArrowSpeedLineSmall";

    [Header("Arrow")]
    [SerializeField] private Vector2 buttonSize = new(232f, 72f);
    [SerializeField] private Color readyColor = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Color disabledColor = new(0.22f, 0.23f, 0.26f, 0.96f);
    [SerializeField] private Color inkColor = new(0.018f, 0.020f, 0.024f, 1f);
    [SerializeField] private Color disabledTextColor = new(0.72f, 0.73f, 0.76f, 1f);

    [Header("Accent Motion")]
    [SerializeField, Min(0.1f)] private float accentPulseSpeed = 2.65f;
    [SerializeField] private Vector2 mainAccentWidthRange = new(30f, 54f);
    [SerializeField] private Vector2 mainAccentThicknessRange = new(3.5f, 6.5f);
    [SerializeField] private Vector2 smallAccentWidthRange = new(18f, 38f);
    [SerializeField] private Vector2 smallAccentThicknessRange = new(2.5f, 5f);
    [SerializeField, Range(1f, 1.5f)] private float hoverAccentScale = 1.12f;

    [Header("Button Motion")]
    [SerializeField, Min(0.1f)] private float wobbleSpeed = 3.9f;
    [SerializeField, Range(0f, 0.10f)] private float squashX = 0.040f;
    [SerializeField, Range(0f, 0.10f)] private float squashY = 0.026f;
    [SerializeField, Range(0f, 8f)] private float travelX = 3.5f;
    [SerializeField, Range(0f, 4f)] private float travelY = 1.4f;
    [SerializeField, Range(0f, 4f)] private float wobbleDegrees = 0.9f;

    private static Sprite sharedArrowSprite;
    private static Sprite sharedAccentSprite;

    private RectTransform doneRoot;
    private RectTransform visualRoot;
    private Image shadowImage;
    private Image fillImage;
    private Image speedLine;
    private Image speedLineSmall;
    private Button button;
    private Image baseImage;
    private Outline baseOutline;
    private Text label;
    private bool hovered;
    private float nextResolveTime;

    private void Awake() => Resolve();

    private void OnEnable()
    {
        nextResolveTime = 0f;
        Resolve();
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
        Canvas.willRenderCanvases += HandleWillRenderCanvases;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
        hovered = false;
    }

    private void OnDestroy()
    {
        Canvas.willRenderCanvases -= HandleWillRenderCanvases;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextResolveTime)
            return;

        nextResolveTime = Time.unscaledTime + 0.12f;
        Resolve();
    }

    private void LateUpdate() => ApplyVisual();

    private void HandleWillRenderCanvases()
    {
        if (isActiveAndEnabled)
            ApplyVisual();
    }

    private void Resolve()
    {
        doneRoot ??= FindRect(DoneName);
        if (doneRoot == null)
            return;

        button ??= doneRoot.GetComponent<Button>();
        baseImage ??= doneRoot.GetComponent<Image>();
        baseOutline ??= doneRoot.GetComponent<Outline>();
        ResolveLabel();

        visualRoot ??= doneRoot.Find(VisualName) as RectTransform;
        if (visualRoot == null)
        {
            visualRoot = CreateRect(doneRoot, VisualName, Vector2.zero);
            Stretch(visualRoot);
        }

        ResolveVisualPieces();
        if (fillImage == null || shadowImage == null || speedLine == null || speedLineSmall == null)
            RebuildVisual();

        if (label != null && label.transform.parent != visualRoot)
            label.transform.SetParent(visualRoot, false);

        BattleDoneNextArrowHoverRelay relay = doneRoot.GetComponent<BattleDoneNextArrowHoverRelay>();
        if (relay == null)
            relay = doneRoot.gameObject.AddComponent<BattleDoneNextArrowHoverRelay>();
        relay.Configure(this);
    }

    private void ResolveLabel()
    {
        if (label != null)
            return;

        Text[] texts = doneRoot.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            Text candidate = texts[i];
            if (candidate == null)
                continue;

            string value = candidate.text ?? string.Empty;
            if (value.Contains("DONE") || value.Contains("NEXT") || value.Contains("START"))
            {
                label = candidate;
                break;
            }
        }

        if (label != null)
            return;

        RectTransform rect = CreateRect(doneRoot, "NextStageLabel", Vector2.zero);
        label = rect.gameObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private void ResolveVisualPieces()
    {
        if (visualRoot == null)
            return;

        shadowImage = visualRoot.Find(ShadowName)?.GetComponent<Image>();
        fillImage = visualRoot.Find(FillName)?.GetComponent<Image>();
        speedLine = visualRoot.Find(MainAccentName)?.GetComponent<Image>();
        speedLineSmall = visualRoot.Find(SmallAccentName)?.GetComponent<Image>();
    }

    private void RebuildVisual()
    {
        if (visualRoot == null)
            return;

        for (int i = visualRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = visualRoot.GetChild(i);
            if ((label != null && child == label.transform) || child.GetComponent<Text>() != null)
                continue;

            // Hot reload 시 기존 Custom Mesh child가 Transform.Find에 다시 잡히지 않게 즉시 이름을 비웁니다.
            child.name += "_Retired";
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        RectTransform shadowRect = CreateRect(visualRoot, ShadowName, Vector2.zero);
        Stretch(shadowRect);
        shadowRect.anchoredPosition = new Vector2(5f, -5f);
        shadowImage = AddShapeImage(shadowRect, GetOrCreateArrowSprite());

        RectTransform fillRect = CreateRect(visualRoot, FillName, Vector2.zero);
        Stretch(fillRect);
        fillImage = AddShapeImage(fillRect, GetOrCreateArrowSprite());

        speedLine = CreateAccent(
            visualRoot,
            MainAccentName,
            new Vector2(42f, 5f),
            new Vector2(-7f, 12f),
            -13f);

        speedLineSmall = CreateAccent(
            visualRoot,
            SmallAccentName,
            new Vector2(28f, 4f),
            new Vector2(-1f, -11f),
            8f);
    }

    private static Image AddShapeImage(RectTransform rect, Sprite sprite)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = false;
        image.raycastTarget = false;
        return image;
    }

    private static Image CreateAccent(
        Transform parent,
        string name,
        Vector2 size,
        Vector2 position,
        float rotation)
    {
        RectTransform rect = CreateRect(parent, name, size);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = position;
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        return AddShapeImage(rect, GetOrCreateAccentSprite());
    }

    private void ApplyVisual()
    {
        Resolve();
        if (doneRoot == null || visualRoot == null)
            return;

        doneRoot.sizeDelta = buttonSize;

        if (baseImage != null)
        {
            baseImage.color = Color.clear;
            baseImage.raycastTarget = true;
        }
        if (baseOutline != null)
            baseOutline.enabled = false;

        bool ready = button == null || button.interactable;
        Color fill = ready ? readyColor : disabledColor;
        if (hovered && ready)
            fill = Color.Lerp(readyColor, Color.white, 0.14f);

        SetImageColor(shadowImage, new Color(inkColor.r, inkColor.g, inkColor.b, ready ? 1f : 0.74f));
        SetImageColor(fillImage, fill);
        SetImageColor(
            speedLine,
            ready ? new Color(1f, 1f, 1f, hovered ? 0.92f : 0.76f) : new Color(1f, 1f, 1f, 0.14f));
        SetImageColor(
            speedLineSmall,
            ready ? new Color(1f, 1f, 1f, hovered ? 0.68f : 0.48f) : new Color(1f, 1f, 1f, 0.08f));

        if (label != null)
        {
            label.gameObject.SetActive(true);
            label.text = "NEXT STAGE";
            label.fontStyle = FontStyle.Bold;
            label.fontSize = 16;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = ready ? inkColor : disabledTextColor;
            label.raycastTarget = false;

            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0.07f, 0.08f);
            labelRect.anchorMax = new Vector2(0.73f, 0.92f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            labelRect.localScale = Vector3.one;
            labelRect.localRotation = Quaternion.identity;
            labelRect.SetAsLastSibling();
        }

        float phase = Time.unscaledTime * Mathf.Max(0.1f, accentPulseSpeed) * Mathf.PI * 2f;
        float mainPulse = Mathf.Sin(phase) * 0.5f + 0.5f;
        float smallPulse = Mathf.Sin(phase * 1.27f + 1.35f) * 0.5f + 0.5f;
        float accentScale = hovered && ready ? hoverAccentScale : 1f;

        ApplyAccentSize(speedLine, mainAccentWidthRange, mainAccentThicknessRange, ready ? mainPulse : 0.2f, accentScale);
        ApplyAccentSize(speedLineSmall, smallAccentWidthRange, smallAccentThicknessRange, ready ? smallPulse : 0.2f, accentScale);

        if (!ready)
        {
            visualRoot.anchoredPosition = Vector2.zero;
            visualRoot.localScale = new Vector3(0.985f, 0.985f, 1f);
            visualRoot.localRotation = Quaternion.identity;
            return;
        }

        float amplitude = hovered ? 1.45f : 1f;
        float wobblePhase = Time.unscaledTime * Mathf.Max(0.1f, wobbleSpeed) * Mathf.PI * 2f;
        float wave = Mathf.Sin(wobblePhase);
        float secondary = Mathf.Sin(wobblePhase * 0.53f + 0.9f);
        float push = (wave + 1f) * 0.5f;

        visualRoot.localScale = new Vector3(
            1f + wave * squashX * amplitude,
            1f - wave * squashY * amplitude,
            1f);
        visualRoot.anchoredPosition = new Vector2(push * travelX * amplitude, secondary * travelY * amplitude);
        visualRoot.localRotation = Quaternion.Euler(0f, 0f, secondary * wobbleDegrees * amplitude);
    }

    private static void ApplyAccentSize(
        Image image,
        Vector2 widthRange,
        Vector2 thicknessRange,
        float pulse,
        float scale)
    {
        if (image == null)
            return;

        float t = Mathf.Clamp01(pulse);
        float width = Mathf.Lerp(Mathf.Min(widthRange.x, widthRange.y), Mathf.Max(widthRange.x, widthRange.y), t);
        float thickness = Mathf.Lerp(
            Mathf.Min(thicknessRange.x, thicknessRange.y),
            Mathf.Max(thicknessRange.x, thicknessRange.y),
            1f - t * 0.45f);

        image.rectTransform.sizeDelta = new Vector2(width, thickness) * Mathf.Max(0.1f, scale);
    }

    private static void SetImageColor(Image image, Color color)
    {
        if (image == null)
            return;

        image.enabled = true;
        image.color = color;
        image.raycastTarget = false;
    }

    private static Sprite GetOrCreateArrowSprite()
    {
        if (sharedArrowSprite != null)
            return sharedArrowSprite;

        // 화살촉 없이, 왼쪽은 전체 높이이고 오른쪽으로 갈수록 좁아지는 4점 사다리꼴입니다.
        Vector2[] polygon =
        {
            new(0.00f, 0.00f),
            new(1.00f, 0.22f),
            new(1.00f, 0.78f),
            new(0.00f, 1.00f)
        };
        sharedArrowSprite = CreatePolygonSprite("RuntimeDoneNextTaperedTrapezoid", 256, 96, polygon);
        return sharedArrowSprite;
    }

    private static Sprite GetOrCreateAccentSprite()
    {
        if (sharedAccentSprite != null)
            return sharedAccentSprite;

        // 오른쪽 끝 높이가 왼쪽의 약 24%인 사다리꼴.
        Vector2[] polygon =
        {
            new(0.00f, 0.00f),
            new(1.00f, 0.38f),
            new(1.00f, 0.62f),
            new(0.00f, 1.00f)
        };
        sharedAccentSprite = CreatePolygonSprite("RuntimeDoneNextTaperedAccent", 128, 32, polygon);
        return sharedAccentSprite;
    }

    private static Sprite CreatePolygonSprite(string name, int width, int height, Vector2[] polygon)
    {
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false, true)
        {
            name = name + "Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float py = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float px = (x + 0.5f) / width;
                bool inside = PointInConvexPolygon(new Vector2(px, py), polygon);
                pixels[y * width + x] = inside ? Color.white : Color.clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
        sprite.name = name + "Sprite";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static bool PointInConvexPolygon(Vector2 point, Vector2[] polygon)
    {
        float windingSign = 0f;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Length];
            Vector2 edge = b - a;
            Vector2 toPoint = point - a;
            float cross = edge.x * toPoint.y - edge.y * toPoint.x;

            if (Mathf.Abs(cross) <= 0.000001f)
                continue;

            float currentSign = Mathf.Sign(cross);
            if (windingSign == 0f)
                windingSign = currentSign;
            else if (currentSign != windingSign)
                return false;
        }

        return true;
    }

    internal void SetHovered(bool value) => hovered = value;

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }
        return null;
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

internal sealed class BattleDoneNextArrowHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private BattleDoneNextArrowPresentationController owner;

    public void Configure(BattleDoneNextArrowPresentationController controller)
    {
        owner = controller;
    }

    public void OnPointerEnter(PointerEventData eventData) => owner?.SetHovered(true);
    public void OnPointerExit(PointerEventData eventData) => owner?.SetHovered(false);
}

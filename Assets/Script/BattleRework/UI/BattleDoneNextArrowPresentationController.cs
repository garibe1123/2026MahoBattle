using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reward PACK의 기존 DONE / NEXT 입력은 그대로 두고 시각만 담당합니다.
///
/// 버튼 Root/클릭 판정은 기존 UI가 계속 소유합니다. 이 컴포넌트는 내부 Visual만 생성하며:
/// - DONE/NEXT 본체를 오른쪽으로 갈수록 좁아지는 사다리꼴 몸통 + 화살촉 Mesh로 그립니다.
/// - 좌측 두 Motion Accent도 단순 직사각형 Image가 아니라 오른쪽이 좁은 사다리꼴 Mesh입니다.
/// - 두 Accent는 서로 다른 위상으로 길이/두께가 계속 변해 정적인 "--"처럼 보이지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(45000)]
public sealed class BattleDoneNextArrowPresentationController : MonoBehaviour
{
    private const string DoneName = "RewardPackDone";
    private const string VisualName = "DoneNextArrowVisual";
    private const string ShadowGroupName = "ArrowShadowGroup";
    private const string FillGroupName = "ArrowFillGroup";
    private const string MainAccentName = "ArrowSpeedLine";
    private const string SmallAccentName = "ArrowSpeedLineSmall";

    [Header("Arrow")]
    [SerializeField] private Vector2 buttonSize = new(232f, 72f);
    [SerializeField, Range(0.2f, 0.9f)] private float bodyRightHeightRatio = 0.56f;
    [SerializeField, Range(0.08f, 0.4f)] private float arrowHeadLengthRatio = 0.24f;
    [SerializeField, Range(0.2f, 1f)] private float arrowHeadBaseHeightRatio = 0.68f;
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
    [SerializeField, Range(0.12f, 0.8f)] private float accentRightHeightRatio = 0.24f;
    [SerializeField, Range(1f, 1.5f)] private float hoverAccentScale = 1.12f;

    [Header("Squish")]
    [SerializeField, Min(0.1f)] private float wobbleSpeed = 3.9f;
    [SerializeField, Range(0f, 0.10f)] private float squashX = 0.040f;
    [SerializeField, Range(0f, 0.10f)] private float squashY = 0.026f;
    [SerializeField, Range(0f, 8f)] private float travelX = 3.5f;
    [SerializeField, Range(0f, 4f)] private float travelY = 1.4f;
    [SerializeField, Range(0f, 4f)] private float wobbleDegrees = 0.9f;

    private RectTransform doneRoot;
    private RectTransform visualRoot;
    private RectTransform shadowGroup;
    private RectTransform fillGroup;

    private BattleDoneNextArrowGraphic shadowGraphic;
    private BattleDoneNextArrowGraphic fillGraphic;
    private BattleDoneNextArrowGraphic speedLine;
    private BattleDoneNextArrowGraphic speedLineSmall;

    private Button button;
    private Image baseImage;
    private Outline baseOutline;
    private Text label;
    private bool hovered;
    private float nextResolveTime;

    private void Awake()
    {
        Resolve();
    }

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
        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.12f;
            Resolve();
        }
    }

    private void LateUpdate()
    {
        ApplyVisual();
    }

    private void HandleWillRenderCanvases()
    {
        if (isActiveAndEnabled)
            ApplyVisual();
    }

    private void Resolve()
    {
        if (doneRoot == null)
            doneRoot = FindRect(DoneName);
        if (doneRoot == null)
            return;

        button ??= doneRoot.GetComponent<Button>();
        baseImage ??= doneRoot.GetComponent<Image>();
        baseOutline ??= doneRoot.GetComponent<Outline>();

        if (visualRoot == null)
            visualRoot = doneRoot.Find(VisualName) as RectTransform;

        if (visualRoot == null)
            BuildArrowVisual();
        else
            ResolveArrowPieces();

        if (label == null)
        {
            Text[] texts = doneRoot.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                Text text = texts[i];
                if (text == null)
                    continue;

                string value = text.text ?? string.Empty;
                if (value.Contains("DONE") || value.Contains("NEXT") || value.Contains("START"))
                {
                    label = text;
                    break;
                }
            }
        }

        if (label != null && visualRoot != null && label.transform.parent != visualRoot)
            label.transform.SetParent(visualRoot, false);

        BattleDoneNextArrowHoverRelay relay = doneRoot.GetComponent<BattleDoneNextArrowHoverRelay>();
        if (relay == null)
            relay = doneRoot.gameObject.AddComponent<BattleDoneNextArrowHoverRelay>();
        relay.Configure(this);
    }

    private void BuildArrowVisual()
    {
        GameObject visualObject = new(VisualName);
        visualObject.transform.SetParent(doneRoot, false);
        visualRoot = visualObject.AddComponent<RectTransform>();
        Stretch(visualRoot);
        BuildArrowVisualIntoExistingRoot();
    }

    private void BuildArrowVisualIntoExistingRoot()
    {
        if (visualRoot == null)
            return;

        shadowGroup = CreateGroup(visualRoot, ShadowGroupName);
        shadowGroup.anchoredPosition = new Vector2(5f, -5f);
        shadowGraphic = BuildArrowGraphic(shadowGroup);

        fillGroup = CreateGroup(visualRoot, FillGroupName);
        fillGraphic = BuildArrowGraphic(fillGroup);

        speedLine = BuildAccentGraphic(
            visualRoot,
            MainAccentName,
            new Vector2(42f, 5f),
            new Vector2(-7f, 12f),
            -13f);

        speedLineSmall = BuildAccentGraphic(
            visualRoot,
            SmallAccentName,
            new Vector2(28f, 4f),
            new Vector2(-1f, -11f),
            8f);
    }

    private BattleDoneNextArrowGraphic BuildArrowGraphic(RectTransform group)
    {
        BattleDoneNextArrowGraphic graphic = group.gameObject.AddComponent<BattleDoneNextArrowGraphic>();
        graphic.ConfigureShape(
            true,
            bodyRightHeightRatio,
            arrowHeadLengthRatio,
            arrowHeadBaseHeightRatio);
        graphic.raycastTarget = false;
        return graphic;
    }

    private BattleDoneNextArrowGraphic BuildAccentGraphic(
        Transform parent,
        string name,
        Vector2 size,
        Vector2 anchoredPosition,
        float rotation)
    {
        RectTransform rect = CreateRect(parent, name, size);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);

        BattleDoneNextArrowGraphic graphic = rect.gameObject.AddComponent<BattleDoneNextArrowGraphic>();
        graphic.ConfigureShape(false, accentRightHeightRatio, 0f, accentRightHeightRatio);
        graphic.raycastTarget = false;
        return graphic;
    }

    private RectTransform CreateGroup(Transform parent, string name)
    {
        RectTransform rect = CreateRect(parent, name, Vector2.zero);
        Stretch(rect);
        return rect;
    }

    private void ResolveArrowPieces()
    {
        if (visualRoot == null)
            return;

        shadowGroup ??= visualRoot.Find(ShadowGroupName) as RectTransform;
        fillGroup ??= visualRoot.Find(FillGroupName) as RectTransform;
        shadowGraphic = shadowGroup != null ? shadowGroup.GetComponent<BattleDoneNextArrowGraphic>() : null;
        fillGraphic = fillGroup != null ? fillGroup.GetComponent<BattleDoneNextArrowGraphic>() : null;
        speedLine = visualRoot.Find(MainAccentName)?.GetComponent<BattleDoneNextArrowGraphic>();
        speedLineSmall = visualRoot.Find(SmallAccentName)?.GetComponent<BattleDoneNextArrowGraphic>();

        bool legacyOrIncomplete =
            shadowGroup == null ||
            fillGroup == null ||
            shadowGraphic == null ||
            fillGraphic == null ||
            speedLine == null ||
            speedLineSmall == null;

        if (!legacyOrIncomplete)
            return;

        // 이전 Image 조합 버전이 Play Mode 재진입 없이 남아 있어도 즉시 새 Mesh 버전으로 교체합니다.
        DestroyVisualChildrenExceptLabel();
        shadowGroup = null;
        fillGroup = null;
        shadowGraphic = null;
        fillGraphic = null;
        speedLine = null;
        speedLineSmall = null;
        BuildArrowVisualIntoExistingRoot();
    }

    private void DestroyVisualChildrenExceptLabel()
    {
        if (visualRoot == null)
            return;

        for (int i = visualRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = visualRoot.GetChild(i);
            bool isLabel =
                (label != null && child == label.transform) ||
                child.GetComponent<Text>() != null;
            if (isLabel)
                continue;

            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }

    private void ApplyVisual()
    {
        Resolve();
        if (doneRoot == null || visualRoot == null)
            return;

        doneRoot.sizeDelta = buttonSize;

        // Root Image는 클릭 판정만 담당합니다. 실제 색/Shape는 커스텀 Graphic이 그립니다.
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

        Color shadow = new(inkColor.r, inkColor.g, inkColor.b, ready ? 1f : 0.74f);
        SetGraphicColor(shadowGraphic, shadow);
        SetGraphicColor(fillGraphic, fill);

        if (shadowGraphic != null)
            shadowGraphic.ConfigureShape(true, bodyRightHeightRatio, arrowHeadLengthRatio, arrowHeadBaseHeightRatio);
        if (fillGraphic != null)
            fillGraphic.ConfigureShape(true, bodyRightHeightRatio, arrowHeadLengthRatio, arrowHeadBaseHeightRatio);
        if (speedLine != null)
            speedLine.ConfigureShape(false, accentRightHeightRatio, 0f, accentRightHeightRatio);
        if (speedLineSmall != null)
            speedLineSmall.ConfigureShape(false, accentRightHeightRatio, 0f, accentRightHeightRatio);

        Color mainAccentColor = ready
            ? new Color(1f, 1f, 1f, hovered ? 0.92f : 0.76f)
            : new Color(1f, 1f, 1f, 0.14f);
        Color smallAccentColor = ready
            ? new Color(1f, 1f, 1f, hovered ? 0.68f : 0.48f)
            : new Color(1f, 1f, 1f, 0.08f);
        SetGraphicColor(speedLine, mainAccentColor);
        SetGraphicColor(speedLineSmall, smallAccentColor);

        if (label != null)
        {
            label.gameObject.SetActive(true);
            label.text = "DONE / NEXT";
            label.fontStyle = FontStyle.Bold;
            label.fontSize = 15;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = ready ? inkColor : disabledTextColor;

            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0.08f, 0.12f);
            labelRect.anchorMax = new Vector2(0.72f, 0.88f);
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

        ApplyAccentSize(
            speedLine,
            mainAccentWidthRange,
            mainAccentThicknessRange,
            ready ? mainPulse : 0.2f,
            accentScale);
        ApplyAccentSize(
            speedLineSmall,
            smallAccentWidthRange,
            smallAccentThicknessRange,
            ready ? smallPulse : 0.2f,
            accentScale);

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

        float sx = 1f + wave * squashX * amplitude;
        float sy = 1f - wave * squashY * amplitude;
        visualRoot.localScale = new Vector3(sx, sy, 1f);
        visualRoot.anchoredPosition = new Vector2(
            push * travelX * amplitude,
            secondary * travelY * amplitude);
        visualRoot.localRotation = Quaternion.Euler(0f, 0f, secondary * wobbleDegrees * amplitude);
    }

    private static void ApplyAccentSize(
        BattleDoneNextArrowGraphic graphic,
        Vector2 widthRange,
        Vector2 thicknessRange,
        float pulse,
        float scale)
    {
        if (graphic == null)
            return;

        float t = Mathf.Clamp01(pulse);
        float width = Mathf.Lerp(
            Mathf.Min(widthRange.x, widthRange.y),
            Mathf.Max(widthRange.x, widthRange.y),
            t) * Mathf.Max(0.1f, scale);
        float thickness = Mathf.Lerp(
            Mathf.Min(thicknessRange.x, thicknessRange.y),
            Mathf.Max(thicknessRange.x, thicknessRange.y),
            1f - t * 0.45f) * Mathf.Max(0.1f, scale);

        graphic.rectTransform.sizeDelta = new Vector2(width, thickness);
        graphic.SetVerticesDirty();
    }

    private static void SetGraphicColor(Graphic graphic, Color color)
    {
        if (graphic == null)
            return;

        graphic.enabled = true;
        graphic.color = color;
        graphic.raycastTarget = false;
    }

    internal void SetHovered(bool value)
    {
        hovered = value;
    }

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

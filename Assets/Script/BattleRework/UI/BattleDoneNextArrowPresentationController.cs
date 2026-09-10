using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Reward PACK의 기존 DONE / NEXT 입력은 그대로 두고 시각만 담당합니다.
/// 버튼 Root는 PACK 우측 하단 부착 레이아웃이 소유하고, 이 컴포넌트는 내부 ArrowVisual만 움직입니다.
/// 따라서 꿀렁임 때문에 PACK 부착 위치가 흔들리거나 다른 레이아웃 컨트롤러와 충돌하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(45000)]
public sealed class BattleDoneNextArrowPresentationController : MonoBehaviour
{
    private const string DoneName = "RewardPackDone";
    private const string VisualName = "DoneNextArrowVisual";

    [Header("Arrow")]
    [SerializeField] private Vector2 buttonSize = new(232f, 72f);
    [SerializeField] private Color readyColor = new(1f, 0.80f, 0.08f, 1f);
    [SerializeField] private Color disabledColor = new(0.22f, 0.23f, 0.26f, 0.96f);
    [SerializeField] private Color inkColor = new(0.018f, 0.020f, 0.024f, 1f);
    [SerializeField] private Color disabledTextColor = new(0.72f, 0.73f, 0.76f, 1f);

    [Header("Squish")]
    [SerializeField, Min(0.1f)] private float wobbleSpeed = 3.9f;
    [SerializeField, Range(0f, 0.10f)] private float squashX = 0.040f;
    [SerializeField, Range(0f, 0.10f)] private float squashY = 0.026f;
    [SerializeField, Range(0f, 8f)] private float travelX = 3.5f;
    [SerializeField, Range(0f, 4f)] private float travelY = 1.4f;
    [SerializeField, Range(0f, 4f)] private float wobbleDegrees = 0.9f;

    private RectTransform doneRoot;
    private RectTransform visualRoot;
    private BattleDoneArrowGraphic shadowGraphic;
    private BattleDoneArrowGraphic fillGraphic;
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
        {
            Transform existing = doneRoot.Find(VisualName);
            visualRoot = existing as RectTransform;
        }

        if (visualRoot == null)
            BuildArrowVisual();

        if (label == null)
        {
            Text[] texts = doneRoot.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                Text text = texts[i];
                if (text == null)
                    continue;
                if (text.text != null && (text.text.Contains("DONE") || text.text.Contains("NEXT") || text.text.Contains("START")))
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

        GameObject shadowObject = new("ArrowShadow");
        shadowObject.transform.SetParent(visualRoot, false);
        RectTransform shadowRect = shadowObject.AddComponent<RectTransform>();
        shadowRect.anchorMin = Vector2.zero;
        shadowRect.anchorMax = Vector2.one;
        shadowRect.offsetMin = new Vector2(-5f, -5f);
        shadowRect.offsetMax = new Vector2(5f, 5f);
        shadowGraphic = shadowObject.AddComponent<BattleDoneArrowGraphic>();
        shadowGraphic.raycastTarget = false;

        GameObject fillObject = new("ArrowFill");
        fillObject.transform.SetParent(visualRoot, false);
        RectTransform fillRect = fillObject.AddComponent<RectTransform>();
        Stretch(fillRect);
        fillGraphic = fillObject.AddComponent<BattleDoneArrowGraphic>();
        fillGraphic.raycastTarget = false;

        RectTransform speedLine = CreateRect(visualRoot, "ArrowSpeedLine", new Vector2(58f, 6f));
        speedLine.anchorMin = speedLine.anchorMax = new Vector2(0f, 0.5f);
        speedLine.pivot = new Vector2(1f, 0.5f);
        speedLine.anchoredPosition = new Vector2(-8f, 13f);
        speedLine.localRotation = Quaternion.Euler(0f, 0f, -4f);
        Image speedImage = speedLine.gameObject.AddComponent<Image>();
        speedImage.color = new Color(1f, 1f, 1f, 0.72f);
        speedImage.raycastTarget = false;

        RectTransform secondLine = CreateRect(visualRoot, "ArrowSpeedLineSmall", new Vector2(36f, 4f));
        secondLine.anchorMin = secondLine.anchorMax = new Vector2(0f, 0.5f);
        secondLine.pivot = new Vector2(1f, 0.5f);
        secondLine.anchoredPosition = new Vector2(-2f, -12f);
        secondLine.localRotation = Quaternion.Euler(0f, 0f, 3f);
        Image secondImage = secondLine.gameObject.AddComponent<Image>();
        secondImage.color = new Color(1f, 1f, 1f, 0.42f);
        secondImage.raycastTarget = false;
    }

    private void ApplyVisual()
    {
        Resolve();
        if (doneRoot == null || visualRoot == null)
            return;

        doneRoot.sizeDelta = buttonSize;

        // 기존 사각형 버튼은 클릭 판정만 남기고 렌더는 ArrowGraphic이 담당합니다.
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

        if (shadowGraphic != null)
            shadowGraphic.color = new Color(inkColor.r, inkColor.g, inkColor.b, ready ? 1f : 0.74f);
        if (fillGraphic != null)
            fillGraphic.color = fill;

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
            labelRect.anchorMax = new Vector2(0.78f, 0.88f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            labelRect.localScale = Vector3.one;
            labelRect.localRotation = Quaternion.identity;
            labelRect.SetAsLastSibling();
        }

        if (!ready)
        {
            visualRoot.anchoredPosition = Vector2.zero;
            visualRoot.localScale = new Vector3(0.985f, 0.985f, 1f);
            visualRoot.localRotation = Quaternion.identity;
            return;
        }

        float amplitude = hovered ? 1.45f : 1f;
        float phase = Time.unscaledTime * Mathf.Max(0.1f, wobbleSpeed) * Mathf.PI * 2f;
        float wave = Mathf.Sin(phase);
        float secondary = Mathf.Sin(phase * 0.53f + 0.9f);
        float push = (wave + 1f) * 0.5f;

        float sx = 1f + wave * squashX * amplitude;
        float sy = 1f - wave * squashY * amplitude;
        visualRoot.localScale = new Vector3(sx, sy, 1f);
        visualRoot.anchoredPosition = new Vector2(
            push * travelX * amplitude,
            secondary * travelY * amplitude);
        visualRoot.localRotation = Quaternion.Euler(0f, 0f, secondary * wobbleDegrees * amplitude);
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

/// <summary>기본 Image Sprite 없이 실제 우향 화살표 실루엣을 그립니다.</summary>
internal sealed class BattleDoneArrowGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = GetPixelAdjustedRect();
        float head = Mathf.Min(r.height * 0.78f, r.width * 0.31f);
        float tailInset = Mathf.Min(r.height * 0.13f, 10f);
        float bodyRight = r.xMax - head;
        Vector2 center = r.center;

        Vector2[] points =
        {
            new(r.xMin, r.yMin + tailInset),
            new(bodyRight, r.yMin + tailInset),
            new(bodyRight, r.yMin),
            new(r.xMax, center.y),
            new(bodyRight, r.yMax),
            new(bodyRight, r.yMax - tailInset),
            new(r.xMin, r.yMax - tailInset)
        };

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = center;
        vh.AddVert(vertex);

        for (int i = 0; i < points.Length; i++)
        {
            vertex.position = points[i];
            vh.AddVert(vertex);
        }

        for (int i = 0; i < points.Length; i++)
        {
            int next = (i + 1) % points.Length;
            vh.AddTriangle(0, i + 1, next + 1);
        }
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

public static class BattleDoneNextArrowPresentationAutoInstaller
{
#if UNITY_EDITOR
    private static bool queued;

    [InitializeOnLoadMethod]
    private static void InitializeEditorInstaller()
    {
        EditorApplication.hierarchyChanged -= QueueInstall;
        EditorApplication.hierarchyChanged += QueueInstall;
        QueueInstall();
    }

    private static void QueueInstall()
    {
        if (queued || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        queued = true;
        EditorApplication.delayCall += InstallEditor;
    }

    private static void InstallEditor()
    {
        queued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null || EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded)
                continue;

            if (manager.GetComponent<BattleDoneNextArrowPresentationController>() != null)
                continue;

            Undo.AddComponent<BattleDoneNextArrowPresentationController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallRuntime()
    {
        BattleSceneManager[] managers = Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleDoneNextArrowPresentationController>() == null)
                manager.gameObject.AddComponent<BattleDoneNextArrowPresentationController>();
        }
    }
}

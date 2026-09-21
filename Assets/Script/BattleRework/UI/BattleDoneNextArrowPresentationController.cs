using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Reward PACK의 기존 DONE / NEXT 입력은 그대로 두고 시각만 담당합니다.
///
/// 주의:
/// - 버튼 Root는 PACK 우측 하단 부착 레이아웃이 소유합니다.
/// - 이 컴포넌트는 내부 DoneNextArrowVisual만 움직입니다.
/// - 커스텀 MaskableGraphic Mesh 대신 Unity 기본 Image 조각을 조합해 화살표를 만듭니다.
///   World/Overlay Canvas, Mask 조합에 따라 커스텀 Mesh만 사라지고 Text/HitBox만 남는 문제를 피합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(45000)]
public sealed class BattleDoneNextArrowPresentationController : MonoBehaviour
{
    private const string DoneName = "RewardPackDone";
    private const string VisualName = "DoneNextArrowVisual";
    private const string ShadowGroupName = "ArrowShadowGroup";
    private const string FillGroupName = "ArrowFillGroup";

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
    private RectTransform shadowGroup;
    private RectTransform fillGroup;

    private Image shadowBody;
    private Image shadowHead;
    private Image fillBody;
    private Image fillHead;
    private Image speedLine;
    private Image speedLineSmall;

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

        shadowGroup = CreateGroup(visualRoot, ShadowGroupName);
        shadowGroup.anchoredPosition = new Vector2(5f, -5f);
        BuildArrowPieces(shadowGroup, true);

        fillGroup = CreateGroup(visualRoot, FillGroupName);
        BuildArrowPieces(fillGroup, false);

        RectTransform speedRect = CreateRect(visualRoot, "ArrowSpeedLine", new Vector2(58f, 6f));
        speedRect.anchorMin = speedRect.anchorMax = new Vector2(0f, 0.5f);
        speedRect.pivot = new Vector2(1f, 0.5f);
        speedRect.anchoredPosition = new Vector2(-8f, 13f);
        speedRect.localRotation = Quaternion.Euler(0f, 0f, -4f);
        speedLine = speedRect.gameObject.AddComponent<Image>();
        speedLine.color = new Color(1f, 1f, 1f, 0.72f);
        speedLine.raycastTarget = false;

        RectTransform smallRect = CreateRect(visualRoot, "ArrowSpeedLineSmall", new Vector2(36f, 4f));
        smallRect.anchorMin = smallRect.anchorMax = new Vector2(0f, 0.5f);
        smallRect.pivot = new Vector2(1f, 0.5f);
        smallRect.anchoredPosition = new Vector2(-2f, -12f);
        smallRect.localRotation = Quaternion.Euler(0f, 0f, 3f);
        speedLineSmall = smallRect.gameObject.AddComponent<Image>();
        speedLineSmall.color = new Color(1f, 1f, 1f, 0.42f);
        speedLineSmall.raycastTarget = false;
    }

    private RectTransform CreateGroup(Transform parent, string name)
    {
        RectTransform rect = CreateRect(parent, name, Vector2.zero);
        Stretch(rect);
        return rect;
    }

    private void BuildArrowPieces(RectTransform group, bool shadow)
    {
        // 몸통은 중앙 높이의 직사각형, 화살촉은 45도 회전한 정사각형입니다.
        // 둘을 겹치면 Sprite/Custom Mesh가 없어도 항상 렌더되는 우향 화살표가 됩니다.
        RectTransform bodyRect = CreateRect(group, shadow ? "ShadowBody" : "FillBody", Vector2.zero);
        bodyRect.anchorMin = new Vector2(0f, 0.12f);
        bodyRect.anchorMax = new Vector2(0.845f, 0.88f);
        bodyRect.offsetMin = Vector2.zero;
        bodyRect.offsetMax = Vector2.zero;
        Image body = bodyRect.gameObject.AddComponent<Image>();
        body.raycastTarget = false;

        float headSize = Mathf.Max(40f, buttonSize.y * 0.70f);
        RectTransform headRect = CreateRect(group, shadow ? "ShadowHead" : "FillHead", Vector2.one * headSize);
        headRect.anchorMin = headRect.anchorMax = new Vector2(0.845f, 0.5f);
        headRect.pivot = new Vector2(0.5f, 0.5f);
        headRect.anchoredPosition = Vector2.zero;
        headRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Image head = headRect.gameObject.AddComponent<Image>();
        head.raycastTarget = false;

        if (shadow)
        {
            shadowBody = body;
            shadowHead = head;
        }
        else
        {
            fillBody = body;
            fillHead = head;
        }
    }

    private void ResolveArrowPieces()
    {
        if (visualRoot == null)
            return;

        shadowGroup ??= visualRoot.Find(ShadowGroupName) as RectTransform;
        fillGroup ??= visualRoot.Find(FillGroupName) as RectTransform;

        if (shadowGroup == null || fillGroup == null)
        {
            // 이전 버전의 Custom Mesh Visual이 Play Mode 재진입 없이 남아 있는 경우도 복구합니다.
            DestroyVisualChildrenExceptLabel();
            BuildArrowVisualIntoExistingRoot();
            return;
        }

        shadowBody ??= shadowGroup.Find("ShadowBody")?.GetComponent<Image>();
        shadowHead ??= shadowGroup.Find("ShadowHead")?.GetComponent<Image>();
        fillBody ??= fillGroup.Find("FillBody")?.GetComponent<Image>();
        fillHead ??= fillGroup.Find("FillHead")?.GetComponent<Image>();
        speedLine ??= visualRoot.Find("ArrowSpeedLine")?.GetComponent<Image>();
        speedLineSmall ??= visualRoot.Find("ArrowSpeedLineSmall")?.GetComponent<Image>();
    }

    private void BuildArrowVisualIntoExistingRoot()
    {
        if (visualRoot == null)
            return;

        shadowGroup = CreateGroup(visualRoot, ShadowGroupName);
        shadowGroup.anchoredPosition = new Vector2(5f, -5f);
        BuildArrowPieces(shadowGroup, true);

        fillGroup = CreateGroup(visualRoot, FillGroupName);
        BuildArrowPieces(fillGroup, false);

        RectTransform speedRect = CreateRect(visualRoot, "ArrowSpeedLine", new Vector2(58f, 6f));
        speedRect.anchorMin = speedRect.anchorMax = new Vector2(0f, 0.5f);
        speedRect.pivot = new Vector2(1f, 0.5f);
        speedRect.anchoredPosition = new Vector2(-8f, 13f);
        speedRect.localRotation = Quaternion.Euler(0f, 0f, -4f);
        speedLine = speedRect.gameObject.AddComponent<Image>();
        speedLine.raycastTarget = false;

        RectTransform smallRect = CreateRect(visualRoot, "ArrowSpeedLineSmall", new Vector2(36f, 4f));
        smallRect.anchorMin = smallRect.anchorMax = new Vector2(0f, 0.5f);
        smallRect.pivot = new Vector2(1f, 0.5f);
        smallRect.anchoredPosition = new Vector2(-2f, -12f);
        smallRect.localRotation = Quaternion.Euler(0f, 0f, 3f);
        speedLineSmall = smallRect.gameObject.AddComponent<Image>();
        speedLineSmall.raycastTarget = false;
    }

    private void DestroyVisualChildrenExceptLabel()
    {
        if (visualRoot == null)
            return;

        for (int i = visualRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = visualRoot.GetChild(i);
            if (label != null && child == label.transform)
                continue;
            Destroy(child.gameObject);
        }
    }

    private void ApplyVisual()
    {
        Resolve();
        if (doneRoot == null || visualRoot == null)
            return;

        doneRoot.sizeDelta = buttonSize;

        // Root Image는 클릭 판정만 담당합니다. 실제 색은 기본 Image 조각이 그립니다.
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
        SetImageColor(shadowBody, shadow);
        SetImageColor(shadowHead, shadow);
        SetImageColor(fillBody, fill);
        SetImageColor(fillHead, fill);

        if (speedLine != null)
            speedLine.color = ready
                ? new Color(1f, 1f, 1f, hovered ? 0.90f : 0.72f)
                : new Color(1f, 1f, 1f, 0.14f);
        if (speedLineSmall != null)
            speedLineSmall.color = ready
                ? new Color(1f, 1f, 1f, hovered ? 0.62f : 0.42f)
                : new Color(1f, 1f, 1f, 0.08f);

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
            labelRect.anchorMax = new Vector2(0.76f, 0.88f);
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

    private static void SetImageColor(Image image, Color color)
    {
        if (image == null)
            return;

        image.enabled = true;
        image.color = color;
        image.raycastTarget = false;
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

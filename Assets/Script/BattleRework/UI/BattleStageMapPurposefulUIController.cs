using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Stage Map UI를 정돈하고, 실제 선택 지점만 강하게 강조합니다.
///
/// - Reward 쪽 장식이 공용 TV Frame에 남아도 Map에서는 정렬/톤을 다시 잡습니다.
/// - MapSelectionContent를 TV Safe Area 전체로 확장합니다.
/// - Map 전용 Mask/RectMask를 잠시 풀어 가장자리 노드 잘림을 줄입니다.
/// - 작은 노드 비주얼은 유지하면서 더 큰 투명 Pointer Hit Area를 사용합니다.
/// - World Space Canvas의 GraphicRaycaster / worldCamera / CanvasGroup 입력 상태를 보강합니다.
/// - 항상 화려한 장식 대신 selectable/current/hover 상태에만 강한 Accent를 사용합니다.
/// - 정적인 Map Frame/Hierarchy는 매 프레임 다시 쓰지 않고 진입/저주기 refresh 때만 갱신합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32580)]
public sealed class BattleStageMapPurposefulUIController : MonoBehaviour
{
    private const string SharedFrameName = "PrizeSelectionScreen";
    private const string ScreenInnerName = "ScreenInner";
    private const string ViewportName = "ShowContentViewport";
    private const string MapContentName = "MapSelectionContent";
    private const string MountedTvName = "BattleShowMountedTV";
    private const string RewardAccentName = "RewardKineticAccentLayer";
    private const string PointerHitAreaName = "MapPointerHitArea";
    private const string DecisionAccentName = "MapDecisionAccent";
    private const string ControlHintName = "MapControlHint";

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;

    [Header("Purposeful Map Theme")]
    [SerializeField] private Color inkColor = new(0.028f, 0.030f, 0.040f, 0.995f);
    [SerializeField] private Color panelColor = new(0.052f, 0.055f, 0.068f, 0.995f);
    [SerializeField] private Color paperColor = new(0.93f, 0.90f, 0.80f, 1f);
    [SerializeField] private Color mutedColor = new(0.36f, 0.39f, 0.46f, 0.76f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.48f, 1f);
    [SerializeField] private Color accentCyan = new(0.16f, 0.86f, 0.92f, 1f);
    [SerializeField, Min(56f)] private float pointerHitSize = 96f;
    [SerializeField, Min(28f)] private float selectableNodeSize = 54f;
    [SerializeField, Min(28f)] private float eliteNodeSize = 60f;
    [SerializeField, Min(0.02f)] private float refreshInterval = 0.08f;

    private RectTransform sharedFrame;
    private RectTransform screenInner;
    private RectTransform viewport;
    private RectTransform mapContent;
    private CanvasGroup viewportGroup;
    private CanvasGroup mapGroup;
    private CanvasGroup tvGroup;
    private Canvas tvCanvas;

    private bool wasMapActive;
    private bool rewardAccentsResolved;
    private float nextRefresh;

    private bool frameStateCaptured;
    private Quaternion originalFrameRotation;
    private Color originalFrameColor;
    private Color originalFrameOutlineColor;
    private Vector2 originalFrameOutlineDistance;
    private Color originalInnerColor;
    private Color originalInnerOutlineColor;
    private Vector2 originalInnerOutlineDistance;

    private readonly Dictionary<Behaviour, bool> clippingStates = new();
    private readonly Dictionary<GameObject, bool> rewardAccentStates = new();

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        nextRefresh = 0f;
    }

    private void OnDisable()
    {
        RestoreMapOnlyState();
    }

    private void OnDestroy()
    {
        RestoreMapOnlyState();
    }

    private void Update()
    {
        ResolveReferences();
        bool mapActive = IsMapActive();

        if (!mapActive)
        {
            if (wasMapActive)
                RestoreMapOnlyState();
            wasMapActive = false;
            return;
        }

        bool enteringMap = !wasMapActive;
        if (!enteringMap && Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + Mathf.Max(0.02f, refreshInterval);
        ResolveUi();

        if (enteringMap)
        {
            CaptureFrameState();
            wasMapActive = true;
        }

        // 이 UI는 대부분 정적인 상태입니다. 이전처럼 Update + LateUpdate에서 같은 Graphic/Rect를
        // 매 프레임 다시 쓰면 World Space TV Canvas가 계속 rebuild됩니다.
        ApplyMapFrame();
        MaintainInteraction();
        ApplyMapHierarchy();
    }

    private bool IsMapActive()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.SelectingNode;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
    }

    private void ResolveUi()
    {
        if (sharedFrame == null)
            sharedFrame = FindRect(SharedFrameName);

        if (screenInner == null && sharedFrame != null)
            screenInner = sharedFrame.Find(ScreenInnerName) as RectTransform;

        if (viewport == null && screenInner != null)
            viewport = screenInner.Find(ViewportName) as RectTransform;

        if (mapContent == null)
            mapContent = FindRect(MapContentName);

        if (viewportGroup == null && viewport != null)
            viewportGroup = viewport.GetComponent<CanvasGroup>();

        if (mapGroup == null && mapContent != null)
            mapGroup = mapContent.GetComponent<CanvasGroup>();

        if (tvCanvas == null && sharedFrame != null)
            tvCanvas = sharedFrame.GetComponentInParent<Canvas>();

        if (tvGroup == null)
        {
            RectTransform mounted = FindRect(MountedTvName);
            if (mounted != null)
                tvGroup = mounted.GetComponent<CanvasGroup>();
        }
    }

    private void CaptureFrameState()
    {
        if (frameStateCaptured || sharedFrame == null)
            return;

        originalFrameRotation = sharedFrame.localRotation;

        Image frameImage = sharedFrame.GetComponent<Image>();
        Outline frameOutline = sharedFrame.GetComponent<Outline>();
        Image innerImage = screenInner != null ? screenInner.GetComponent<Image>() : null;
        Outline innerOutline = screenInner != null ? screenInner.GetComponent<Outline>() : null;

        if (frameImage != null)
            originalFrameColor = frameImage.color;
        if (frameOutline != null)
        {
            originalFrameOutlineColor = frameOutline.effectColor;
            originalFrameOutlineDistance = frameOutline.effectDistance;
        }
        if (innerImage != null)
            originalInnerColor = innerImage.color;
        if (innerOutline != null)
        {
            originalInnerOutlineColor = innerOutline.effectColor;
            originalInnerOutlineDistance = innerOutline.effectDistance;
        }

        frameStateCaptured = true;
    }

    private void ApplyMapFrame()
    {
        if (sharedFrame == null)
            return;

        sharedFrame.localRotation = Quaternion.identity;

        Image frameImage = sharedFrame.GetComponent<Image>();
        if (frameImage != null)
            frameImage.color = inkColor;

        Outline frameOutline = sharedFrame.GetComponent<Outline>();
        if (frameOutline != null)
        {
            frameOutline.effectColor = new Color(paperColor.r, paperColor.g, paperColor.b, 0.34f);
            frameOutline.effectDistance = new Vector2(2f, -2f);
        }

        if (screenInner != null)
        {
            Image innerImage = screenInner.GetComponent<Image>();
            if (innerImage != null)
                innerImage.color = panelColor;

            Outline innerOutline = screenInner.GetComponent<Outline>();
            if (innerOutline != null)
            {
                innerOutline.effectColor = new Color(accentCyan.r, accentCyan.g, accentCyan.b, 0.16f);
                innerOutline.effectDistance = new Vector2(1f, -1f);
            }
        }

        if (mapContent != null)
        {
            mapContent.anchorMin = Vector2.zero;
            mapContent.anchorMax = Vector2.one;
            mapContent.offsetMin = Vector2.zero;
            mapContent.offsetMax = Vector2.zero;
            mapContent.pivot = new Vector2(0.5f, 0.5f);
            mapContent.localRotation = Quaternion.identity;
        }

        DisableMapClipping(viewport);
        DisableMapClipping(mapContent);
        DisableRewardAccentsDuringMap();
    }

    private void MaintainInteraction()
    {
        if (viewportGroup != null)
        {
            viewportGroup.interactable = true;
            viewportGroup.blocksRaycasts = true;
        }

        if (mapGroup != null)
        {
            mapGroup.interactable = true;
            if (mapGroup.alpha >= 0.75f)
                mapGroup.blocksRaycasts = true;
        }

        if (tvGroup != null)
        {
            tvGroup.interactable = true;
            tvGroup.blocksRaycasts = true;
        }

        if (tvCanvas != null)
        {
            if (tvCanvas.renderMode == RenderMode.WorldSpace && tvCanvas.worldCamera != Camera.main)
                tvCanvas.worldCamera = Camera.main;

            if (tvCanvas.GetComponent<GraphicRaycaster>() == null)
                tvCanvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        EnsureEventSystem();
    }

    private void ApplyMapHierarchy()
    {
        if (mapContent == null)
            return;

        RectTransform[] all = mapContent.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect == null)
                continue;

            if (rect.name == "Title")
            {
                StyleTitle(rect);
                continue;
            }

            if (rect.name == "Subtitle")
            {
                StyleSubtitle(rect);
                continue;
            }

            if (rect.name == "StageLink")
            {
                StyleLink(rect);
                continue;
            }

            if (rect.name.StartsWith("StageNode_", System.StringComparison.Ordinal))
                StyleNode(rect);
        }

        EnsureDecisionAccent();
        EnsureControlHint();
    }

    private void StyleTitle(RectTransform rect)
    {
        Text text = rect.GetComponent<Text>();
        if (text == null)
            return;

        bool opening = runManager != null && runManager.IsInStartArea;
        text.text = opening ? "FIRST STAGE" : "NEXT STAGE";
        text.alignment = TextAnchor.MiddleLeft;
        text.fontStyle = FontStyle.Bold;
        text.fontSize = 24;
        text.color = paperColor;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(42f, -16f);
        rect.sizeDelta = new Vector2(-84f, 36f);
        rect.localRotation = Quaternion.identity;
    }

    private void StyleSubtitle(RectTransform rect)
    {
        Text text = rect.GetComponent<Text>();
        if (text == null)
            return;

        text.text = runManager != null && runManager.IsInStartArea
            ? "CHOOSE ONE START ROUTE"
            : "CHOOSE ONE HIGHLIGHTED ROUTE";
        text.alignment = TextAnchor.MiddleLeft;
        text.fontStyle = FontStyle.Bold;
        text.fontSize = 10;
        text.color = new Color(mutedColor.r, mutedColor.g, mutedColor.b, 0.95f);

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(42f, -48f);
        rect.sizeDelta = new Vector2(-84f, 20f);
        rect.localRotation = Quaternion.identity;
    }

    private void StyleLink(RectTransform rect)
    {
        Image image = rect.GetComponent<Image>();
        if (image != null)
            image.color = new Color(paperColor.r, paperColor.g, paperColor.b, 0.20f);

        Vector2 size = rect.sizeDelta;
        size.y = 3f;
        rect.sizeDelta = size;
    }

    private void StyleNode(RectTransform rect)
    {
        Image image = rect.GetComponent<Image>();
        Outline outline = rect.GetComponent<Outline>();
        Button button = rect.GetComponent<Button>();
        Text label = rect.Find("Label")?.GetComponent<Text>();
        if (image == null || outline == null)
            return;

        bool selectable = button != null;
        bool elite = label != null &&
                     (label.text ?? string.Empty).IndexOf("ELITE", System.StringComparison.OrdinalIgnoreCase) >= 0;
        bool current = runManager != null &&
                       runManager.CurrentNode != null &&
                       rect.name == $"StageNode_{runManager.CurrentNode.id}";

        Color accent = elite ? accentPink : accentYellow;

        if (selectable)
        {
            image.color = inkColor;
            outline.effectColor = accent;
            outline.effectDistance = new Vector2(3f, -3f);
            rect.sizeDelta = Vector2.one * (elite ? eliteNodeSize : selectableNodeSize);
            button.transition = Selectable.Transition.None;
            image.raycastTarget = true;

            if (label != null)
            {
                label.text = ExtractNodeType(label.text);
                label.fontStyle = FontStyle.Bold;
                label.fontSize = 10;
                label.color = paperColor;
            }

            EnsurePointerHitArea(rect, image, outline, label, accent);
        }
        else if (current)
        {
            image.color = new Color(
                accentCyan.r * 0.28f,
                accentCyan.g * 0.28f,
                accentCyan.b * 0.28f,
                1f);
            outline.effectColor = accentCyan;
            outline.effectDistance = new Vector2(3f, -3f);

            if (label != null)
            {
                label.text = ExtractNodeType(label.text);
                label.color = accentCyan;
                label.fontStyle = FontStyle.Bold;
                label.fontSize = 10;
            }
        }
        else
        {
            image.color = new Color(0.10f, 0.105f, 0.13f, 0.92f);
            outline.effectColor = new Color(mutedColor.r, mutedColor.g, mutedColor.b, 0.24f);
            outline.effectDistance = new Vector2(1f, -1f);

            if (label != null)
            {
                label.text = ExtractNodeType(label.text);
                label.color = mutedColor;
                label.fontStyle = FontStyle.Normal;
                label.fontSize = 9;
            }
        }
    }

    private void EnsurePointerHitArea(
        RectTransform node,
        Image nodeImage,
        Outline nodeOutline,
        Text label,
        Color accent)
    {
        Transform existing = node.Find(PointerHitAreaName);
        RectTransform hitRect;
        Image hitImage;

        if (existing is RectTransform existingRect)
        {
            hitRect = existingRect;
            hitImage = existing.GetComponent<Image>();
        }
        else
        {
            GameObject hit = new(PointerHitAreaName);
            hit.transform.SetParent(node, false);

            hitRect = hit.AddComponent<RectTransform>();
            hitRect.anchorMin = hitRect.anchorMax = new Vector2(0.5f, 0.5f);
            hitRect.pivot = new Vector2(0.5f, 0.5f);
            hitRect.anchoredPosition = Vector2.zero;

            hitImage = hit.AddComponent<Image>();
            hitImage.color = new Color(1f, 1f, 1f, 0.001f);
            hitImage.raycastTarget = true;
            hit.transform.SetAsLastSibling();
        }

        hitRect.sizeDelta = Vector2.one * Mathf.Max(56f, pointerHitSize);
        if (hitImage != null)
            hitImage.raycastTarget = true;

        BattleStageMapNodePointerFeedback feedback =
            hitRect.GetComponent<BattleStageMapNodePointerFeedback>();
        if (feedback == null)
            feedback = hitRect.gameObject.AddComponent<BattleStageMapNodePointerFeedback>();

        feedback.Configure(
            nodeImage,
            nodeOutline,
            label,
            accent,
            inkColor,
            paperColor);
    }

    private void EnsureDecisionAccent()
    {
        if (mapContent == null || mapContent.Find(DecisionAccentName) != null)
            return;

        RectTransform accent = CreateRect(mapContent, DecisionAccentName, new Vector2(8f, 50f));
        accent.anchorMin = accent.anchorMax = new Vector2(0f, 1f);
        accent.pivot = new Vector2(0f, 1f);
        accent.anchoredPosition = new Vector2(24f, -14f);

        Image image = accent.gameObject.AddComponent<Image>();
        image.color = accentYellow;
        image.raycastTarget = false;
    }

    private void EnsureControlHint()
    {
        if (mapContent == null || mapContent.Find(ControlHintName) != null)
            return;

        RectTransform rect = CreateRect(mapContent, ControlHintName, new Vector2(260f, 24f));
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-28f, 18f);

        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = "POINT  /  CLICK TO CONFIRM";
        text.fontSize = 9;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleRight;
        text.color = accentCyan;
        text.raycastTarget = false;
    }

    private static string ExtractNodeType(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "STAGE";

        string normalized = source.Replace("\r", string.Empty);
        int newline = normalized.IndexOf('\n');
        if (newline >= 0)
            normalized = normalized.Substring(0, newline);

        return normalized.Trim().ToUpperInvariant();
    }

    private void DisableMapClipping(RectTransform root)
    {
        if (root == null)
            return;

        RectMask2D rectMask = root.GetComponent<RectMask2D>();
        if (rectMask != null)
        {
            if (!clippingStates.ContainsKey(rectMask))
                clippingStates.Add(rectMask, rectMask.enabled);
            rectMask.enabled = false;
        }

        Mask mask = root.GetComponent<Mask>();
        if (mask != null)
        {
            if (!clippingStates.ContainsKey(mask))
                clippingStates.Add(mask, mask.enabled);
            mask.enabled = false;
        }
    }

    private void DisableRewardAccentsDuringMap()
    {
        if (rewardAccentsResolved)
            return;

        rewardAccentsResolved = true;
        RectTransform[] all = FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect == null || rect.name != RewardAccentName)
                continue;

            GameObject go = rect.gameObject;
            if (!rewardAccentStates.ContainsKey(go))
                rewardAccentStates.Add(go, go.activeSelf);

            if (go.activeSelf)
                go.SetActive(false);
        }
    }

    private void RestoreMapOnlyState()
    {
        if (frameStateCaptured && sharedFrame != null)
        {
            sharedFrame.localRotation = originalFrameRotation;

            Image frameImage = sharedFrame.GetComponent<Image>();
            if (frameImage != null)
                frameImage.color = originalFrameColor;

            Outline frameOutline = sharedFrame.GetComponent<Outline>();
            if (frameOutline != null)
            {
                frameOutline.effectColor = originalFrameOutlineColor;
                frameOutline.effectDistance = originalFrameOutlineDistance;
            }

            if (screenInner != null)
            {
                Image innerImage = screenInner.GetComponent<Image>();
                if (innerImage != null)
                    innerImage.color = originalInnerColor;

                Outline innerOutline = screenInner.GetComponent<Outline>();
                if (innerOutline != null)
                {
                    innerOutline.effectColor = originalInnerOutlineColor;
                    innerOutline.effectDistance = originalInnerOutlineDistance;
                }
            }
        }

        frameStateCaptured = false;

        foreach (KeyValuePair<Behaviour, bool> pair in clippingStates)
        {
            if (pair.Key != null)
                pair.Key.enabled = pair.Value;
        }
        clippingStates.Clear();

        foreach (KeyValuePair<GameObject, bool> pair in rewardAccentStates)
        {
            if (pair.Key != null)
                pair.Key.SetActive(pair.Value);
        }
        rewardAccentStates.Clear();
        rewardAccentsResolved = false;
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = FindObjectsByType<RectTransform>(
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

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        GameObject go = new("BattleStageMapEventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }
}

/// <summary>
/// 실제 노드는 작게 유지하고 Pointer Target만 넓게 잡습니다.
/// Hover 때만 노드가 강하게 반응합니다.
/// </summary>
internal sealed class BattleStageMapNodePointerFeedback :
    MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private Image nodeImage;
    private Outline nodeOutline;
    private Text label;
    private Color accent;
    private Color baseColor;
    private Color paperColor;
    private string baseLabel = "STAGE";
    private bool hovered;

    public void Configure(
        Image image,
        Outline outline,
        Text nodeLabel,
        Color accentColor,
        Color normalColor,
        Color textColor)
    {
        nodeImage = image;
        nodeOutline = outline;
        label = nodeLabel;
        accent = accentColor;
        baseColor = normalColor;
        paperColor = textColor;

        if (label != null)
            baseLabel = ExtractLabel(label.text);

        if (!hovered)
            ApplyNormal();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        ApplyHover();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        ApplyNormal();
    }

    private void OnDisable()
    {
        hovered = false;
    }

    private void ApplyHover()
    {
        if (nodeImage != null)
            nodeImage.color = accent;

        if (nodeOutline != null)
        {
            nodeOutline.effectColor = paperColor;
            nodeOutline.effectDistance = new Vector2(6f, -6f);
        }

        if (label != null)
        {
            label.text = baseLabel + "\nSELECT";
            label.color = baseColor;
            label.fontStyle = FontStyle.Bold;
        }
    }

    private void ApplyNormal()
    {
        if (nodeImage != null)
            nodeImage.color = baseColor;

        if (nodeOutline != null)
        {
            nodeOutline.effectColor = accent;
            nodeOutline.effectDistance = new Vector2(3f, -3f);
        }

        if (label != null)
        {
            label.text = baseLabel;
            label.color = paperColor;
            label.fontStyle = FontStyle.Bold;
        }
    }

    private static string ExtractLabel(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "STAGE";

        string normalized = source.Replace("\r", string.Empty);
        int newline = normalized.IndexOf('\n');
        if (newline >= 0)
            normalized = normalized.Substring(0, newline);

        return normalized.Trim().ToUpperInvariant();
    }
}

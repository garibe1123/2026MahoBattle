using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Stage Map UI를 정돈하고, 실제 선택 지점만 강하게 강조합니다.
///
/// - Reward 쪽 장식이 공용 TV Frame에 남아도 Map에서는 정렬/톤을 다시 잡습니다.
/// - MapSelectionContent를 TV Safe Area 전체로 확장합니다.
/// - Map 전용 Mask/RectMask를 잠시 풀어 가장자리 노드 잘림을 줄입니다.
/// - 작은 노드 비주얼은 유지하면서 더 큰 투명 Pointer Hit Area를 사용합니다.
/// - World Space Canvas의 GraphicRaycaster / worldCamera / CanvasGroup 입력 상태를 보강합니다.
/// - selectable/current/hover 상태에만 강한 Accent를 사용합니다.
/// - Hover 시 노드가 커지고, 밝은 박스 + 어두운 아이콘/라벨로 반전되어 커서 위치를 즉시 읽을 수 있게 합니다.
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
    [SerializeField] private BattleUIThemeController uiTheme;

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
        if (uiTheme == null)
            uiTheme = BattleUIThemeController.Instance != null
                ? BattleUIThemeController.Instance
                : FindFirstObjectByType<BattleUIThemeController>(FindObjectsInactive.Include);
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
        {
            Canvas nearestCanvas = sharedFrame.GetComponentInParent<Canvas>();
            tvCanvas = nearestCanvas != null ? nearestCanvas.rootCanvas : null;
        }
        else if (tvCanvas != null && tvCanvas.rootCanvas != null && tvCanvas != tvCanvas.rootCanvas)
        {
            tvCanvas = tvCanvas.rootCanvas;
        }

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
            frameImage.color = Color.clear;

        Outline frameOutline = sharedFrame.GetComponent<Outline>();
        if (frameOutline != null)
            frameOutline.enabled = false;

        if (screenInner != null)
        {
            Image innerImage = screenInner.GetComponent<Image>();
            if (innerImage != null)
                innerImage.color = new Color(panelColor.r, panelColor.g, panelColor.b, 0.10f);

            Outline innerOutline = screenInner.GetComponent<Outline>();
            if (innerOutline != null)
                innerOutline.enabled = false;
        }

        if (mapContent != null)
        {
            mapContent.anchorMin = Vector2.zero;
            mapContent.anchorMax = Vector2.one;
            mapContent.offsetMin = Vector2.zero;
            mapContent.offsetMax = Vector2.zero;
            mapContent.pivot = new Vector2(0.5f, 0.5f);
            mapContent.localRotation = Quaternion.Euler(0.8f, -1.8f, 0f);
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

            GraphicRaycaster raycaster = tvCanvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
                raycaster = tvCanvas.gameObject.AddComponent<GraphicRaycaster>();
            raycaster.enabled = true;
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
        BattleUIThemeProfile theme = uiTheme != null ? uiTheme.CurrentProfile : null;
        text.color = theme != null ? theme.textPrimary : paperColor;

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
        {
            BattleUIThemeProfile theme = uiTheme != null ? uiTheme.CurrentProfile : null;
            Color muted = theme != null ? theme.textMuted : mutedColor;
            muted.a = 0.10f;
            image.color = muted;
        }

        Vector2 size = rect.sizeDelta;
        size.y = 2f;
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

        bool selectable = button != null && button.interactable;
        bool elite = label != null &&
                     (label.text ?? string.Empty).IndexOf("ELITE", System.StringComparison.OrdinalIgnoreCase) >= 0;
        bool current = runManager != null &&
                       runManager.CurrentNode != null &&
                       rect.name == $"StageNode_{runManager.CurrentNode.id}";

        BattleUIThemeProfile theme = uiTheme != null ? uiTheme.CurrentProfile : null;
        Color key = theme != null ? theme.keyColor : accentYellow;
        Color textPrimary = theme != null ? theme.textPrimary : paperColor;
        Color textMuted = theme != null ? theme.textMuted : mutedColor;
        string nodeLabel = BuildNodeLabel(rect, label != null ? label.text : string.Empty);

        BattleSpatialGlassPanel glass = rect.GetComponent<BattleSpatialGlassPanel>();
        if (glass == null)
            glass = rect.gameObject.AddComponent<BattleSpatialGlassPanel>();
        glass.Configure(!elite, elite ? 0.18f : 0.13f, elite ? -0.055f : 0.045f);

        image.enabled = true;
        image.color = Color.clear;
        image.raycastTarget = selectable;
        outline.enabled = false;

        if (selectable)
        {
            float resolvedSize = elite
                ? Mathf.Max(76f, eliteNodeSize)
                : Mathf.Max(68f, selectableNodeSize);
            rect.sizeDelta = Vector2.one * resolvedSize;
            rect.localRotation = Quaternion.Euler(
                elite ? -2.0f : 1.2f,
                elite ? 5.5f : -4.0f,
                elite ? 0.8f : -0.4f);
            glass.SetSpatialState(0.48f, 0.34f);
            button.transition = Selectable.Transition.None;

            Vector3 local = rect.localPosition;
            local.z = -6f;
            rect.localPosition = local;

            if (label != null)
            {
                label.text = nodeLabel;
                label.fontStyle = FontStyle.Bold;
                label.fontSize = 9;
                label.color = textPrimary;
            }

            EnsurePointerHitArea(rect, image, outline, label, key);
        }
        else if (current)
        {
            rect.localRotation = Quaternion.Euler(-0.8f, 2.5f, 0f);
            glass.SetSpatialState(0.28f, 0.10f);

            Vector3 local = rect.localPosition;
            local.z = -2f;
            rect.localPosition = local;

            if (label != null)
            {
                label.text = nodeLabel;
                label.color = key;
                label.fontStyle = FontStyle.Bold;
                label.fontSize = 10;
            }
        }
        else
        {
            rect.localRotation = Quaternion.Euler(1.5f, -3.5f, 0f);
            glass.SetSpatialState(0f, -0.42f);

            Vector3 local = rect.localPosition;
            local.z = 7f;
            rect.localPosition = local;

            if (label != null)
            {
                label.text = nodeLabel;
                label.color = textMuted;
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
            hit.transform.SetAsLastSibling();
        }

        hitRect.sizeDelta = Vector2.one * Mathf.Max(104f, pointerHitSize);

        Button nodeButton = node.GetComponent<Button>();
        bool clickable = nodeButton != null && nodeButton.interactable;

        if (hitImage != null)
            hitImage.raycastTarget = clickable;

        // 큰 Pointer Hit Area가 실제 StageNode Button 위에 있기 때문에
        // HitArea 자체가 Button 이벤트를 소유한 뒤 원본 Node Button으로 명시적으로 전달합니다.
        // 이렇게 해야 World-Space Canvas에서도 넓은 클릭 영역과 실제 선택 콜백이 일치합니다.
        Button hitButton = hitRect.GetComponent<Button>();
        if (hitButton == null)
            hitButton = hitRect.gameObject.AddComponent<Button>();

        hitButton.targetGraphic = hitImage;
        hitButton.transition = Selectable.Transition.None;
        hitButton.interactable = clickable;
        hitButton.onClick.RemoveAllListeners();

        if (clickable)
        {
            Button capturedButton = nodeButton;
            hitButton.onClick.AddListener(() =>
            {
                if (capturedButton != null &&
                    capturedButton.IsActive() &&
                    capturedButton.IsInteractable())
                {
                    capturedButton.onClick.Invoke();
                }
            });
        }

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
        if (mapContent == null)
            return;

        Transform existing = mapContent.Find(DecisionAccentName);
        if (existing != null && existing.gameObject.activeSelf)
            existing.gameObject.SetActive(false);
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
        BattleUIThemeProfile theme = uiTheme != null ? uiTheme.CurrentProfile : null;
        text.color = theme != null ? theme.keyColor : accentYellow;
        text.raycastTarget = false;
    }

    private string BuildNodeLabel(RectTransform rect, string source)
    {
        string typeName = ExtractNodeType(source);
        BattleNodeData node = ResolveNodeData(rect);
        if (node == null || (node.type != BattleNodeType.Combat && node.type != BattleNodeType.Elite))
            return typeName;

        int stars = runManager != null
            ? runManager.ResolveBattleRatingStars(node)
            : node.GetBattleRatingStars();

        return typeName + "\n" + BuildStars(stars);
    }

    private BattleNodeData ResolveNodeData(RectTransform rect)
    {
        if (runManager == null || rect == null)
            return null;

        const string prefix = "StageNode_";
        if (!rect.name.StartsWith(prefix, System.StringComparison.Ordinal))
            return null;

        return runManager.FindNode(rect.name.Substring(prefix.Length));
    }

    private static string BuildStars(int stars)
    {
        stars = Mathf.Clamp(stars, 1, 5);
        return new string('★', stars) + new string('☆', 5 - stars);
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
        EventSystem eventSystem = EventSystem.current != null
            ? EventSystem.current
            : FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);

        if (eventSystem == null)
        {
            GameObject go = new("BattleStageMapEventSystem");
            eventSystem = go.AddComponent<EventSystem>();
        }

        InputSystemUIInputModule inputModule =
            eventSystem.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null)
            inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

        StandaloneInputModule legacy =
            eventSystem.GetComponent<StandaloneInputModule>();
        if (legacy != null)
            legacy.enabled = false;
    }
}

/// <summary>
/// 실제 노드는 작게 유지하고 Pointer Target만 넓게 잡습니다.
/// Hover 시 노드를 확대하고 박스/아이콘·라벨 명암을 반전합니다.
/// </summary>
internal sealed class BattleStageMapNodePointerFeedback :
    MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private const float HoverScale = 1.18f;
    private const float HoverScaleSharpness = 18f;

    private RectTransform nodeRect;
    private Image nodeImage;
    private Image nodeIcon;
    private Outline nodeOutline;
    private Text label;
    private Color accent;
    private Color baseColor;
    private Color paperColor;
    private Color baseIconColor = Color.white;
    private string baseLabel = "STAGE";
    private bool hovered;
    private BattleSpatialGlassPanel spatialGlass;
    private BattleUIThemeController uiTheme;

    public void Configure(
        Image image,
        Outline outline,
        Text nodeLabel,
        Color accentColor,
        Color normalColor,
        Color textColor)
    {
        nodeImage = image;
        nodeRect = image != null ? image.rectTransform : null;
        nodeOutline = outline;
        label = nodeLabel;
        accent = accentColor;
        baseColor = normalColor;
        paperColor = textColor;
        spatialGlass = nodeRect != null ? nodeRect.GetComponent<BattleSpatialGlassPanel>() : null;
        uiTheme = BattleUIThemeController.Instance != null
            ? BattleUIThemeController.Instance
            : FindFirstObjectByType<BattleUIThemeController>(FindObjectsInactive.Include);

        Image resolvedIcon = FindNodeIcon(nodeRect, nodeImage);
        if (resolvedIcon != nodeIcon)
        {
            nodeIcon = resolvedIcon;
            if (nodeIcon != null)
                baseIconColor = nodeIcon.color;
        }
        else if (!hovered && nodeIcon != null)
        {
            baseIconColor = nodeIcon.color;
        }

        if (label != null)
            baseLabel = ExtractLabel(label.text);

        if (!hovered)
            ApplyNormal();
        else
            ApplyHover();
    }

    private void Update()
    {
        if (nodeRect == null)
            return;

        float targetScale = hovered ? HoverScale : 1f;
        float t = 1f - Mathf.Exp(-HoverScaleSharpness * Time.unscaledDeltaTime);
        Vector3 target = Vector3.one * targetScale;
        nodeRect.localScale = Vector3.Lerp(nodeRect.localScale, target, t);

        if ((nodeRect.localScale - target).sqrMagnitude <= 0.00001f)
            nodeRect.localScale = target;
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
        ApplyNormal();
        if (nodeRect != null)
            nodeRect.localScale = Vector3.one;
    }

    private void ApplyHover()
    {
        BattleUIThemeProfile theme = uiTheme != null ? uiTheme.CurrentProfile : null;
        Color key = theme != null ? theme.keyColor : accent;
        Color text = theme != null ? theme.textPrimary : paperColor;

        if (nodeImage != null)
        {
            nodeImage.enabled = true;
            nodeImage.color = Color.clear;
        }

        if (nodeOutline != null)
            nodeOutline.enabled = false;

        spatialGlass?.SetSpatialState(1f, 0.95f);

        if (nodeRect != null)
        {
            nodeRect.localRotation = Quaternion.Euler(-2.5f, -1.5f, 0f);
            Vector3 local = nodeRect.localPosition;
            local.z = -16f;
            nodeRect.localPosition = local;
        }

        if (nodeIcon != null)
        {
            nodeIcon.enabled = true;
            nodeIcon.color = text;
        }

        if (label != null)
        {
            label.gameObject.SetActive(true);
            label.text = baseLabel + "\nSELECT";
            label.color = key;
            label.fontStyle = FontStyle.Bold;
        }
    }

    private void ApplyNormal()
    {
        BattleUIThemeProfile theme = uiTheme != null ? uiTheme.CurrentProfile : null;
        Color text = theme != null ? theme.textPrimary : paperColor;

        if (nodeImage != null)
        {
            nodeImage.enabled = true;
            nodeImage.color = Color.clear;
        }

        if (nodeOutline != null)
            nodeOutline.enabled = false;

        spatialGlass?.SetSpatialState(0.48f, 0.34f);

        if (nodeRect != null)
        {
            nodeRect.localRotation = Quaternion.Euler(1.2f, -4f, -0.4f);
            Vector3 local = nodeRect.localPosition;
            local.z = -6f;
            nodeRect.localPosition = local;
        }

        if (nodeIcon != null)
        {
            nodeIcon.enabled = true;
            nodeIcon.color = baseIconColor;
        }

        if (label != null)
        {
            label.gameObject.SetActive(true);
            label.text = baseLabel;
            label.color = text;
            label.fontStyle = FontStyle.Bold;
        }
    }

    private static Image FindNodeIcon(RectTransform node, Image rootImage)
    {
        if (node == null)
            return null;

        Image[] images = node.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image candidate = images[i];
            if (candidate == null || candidate == rootImage)
                continue;
            if (candidate.name == "MapPointerHitArea")
                continue;
            if (candidate.name.IndexOf("Icon", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return candidate;
        }

        return null;
    }

    private static string ExtractLabel(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "STAGE";

        string normalized = source.Replace("\r", string.Empty);
        int selectLine = normalized.IndexOf("\nSELECT", System.StringComparison.OrdinalIgnoreCase);
        if (selectLine >= 0)
            normalized = normalized.Substring(0, selectLine);

        return normalized.Trim().ToUpperInvariant();
    }
}

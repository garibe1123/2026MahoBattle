using System.Collections;
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
/// - 보이는 Node 자체만 Pointer Target으로 사용해 보이지 않는 Hover/Click 영역을 만들지 않습니다.
/// - World Space Canvas의 GraphicRaycaster / worldCamera / CanvasGroup 입력 상태를 보강합니다.
/// - selectable/current/hover 상태에만 강한 Accent를 사용합니다.
/// - Hover 시 노드가 커지고, 밝은 박스 + 어두운 아이콘/라벨로 반전되어 커서 위치를 즉시 읽을 수 있게 합니다.
/// - 정적인 Map Frame/Hierarchy는 매 프레임 다시 쓰지 않고 진입/저주기 refresh 때만 갱신합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32580)]
public sealed class BattleStageMapPurposefulUIController : MonoBehaviour
{
    private enum MapPresentationState
    {
        Hidden,
        Entering,
        Active,
        Exiting
    }
    private const string SharedFrameName = "PrizeSelectionScreen";
    private const string ScreenInnerName = "ScreenInner";
    private const string ViewportName = "ShowContentViewport";
    private const string MapContentName = "MapSelectionContent";
    private const string MountedTvName = "BattleShowMountedTV";
    private const string RewardAccentName = "RewardKineticAccentLayer";
    private const string ControlHintName = "MapControlHint";

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;

    [Header("Purposeful Map Theme")]
    [SerializeField] private Color inkColor = new(0.028f, 0.030f, 0.040f, 0.995f);
    [SerializeField] private Color panelColor = new(0.052f, 0.055f, 0.068f, 0.995f);
    [SerializeField] private Color paperColor = new(0.93f, 0.90f, 0.80f, 1f);
    [SerializeField] private Color mutedColor = new(0.36f, 0.39f, 0.46f, 0.76f);
    [SerializeField] private Color availableColor = new(0.16f, 0.86f, 0.92f, 1f);
    [SerializeField] private Color eliteColor = new(1f, 0.18f, 0.48f, 1f);
    [SerializeField] private Color currentColor = new(0.66f, 0.90f, 0.96f, 1f);
    [SerializeField, Min(56f)] private float selectableNodeSize = 76f;
    [SerializeField, Min(64f)] private float eliteNodeSize = 86f;

    private RectTransform sharedFrame;
    private RectTransform screenInner;
    private RectTransform viewport;
    private RectTransform mapContent;
    private CanvasGroup viewportGroup;
    private CanvasGroup mapGroup;
    private CanvasGroup tvGroup;
    private Canvas tvCanvas;

    private MapPresentationState presentationState = MapPresentationState.Hidden;
    private BattleRunManager subscribedRunManager;
    private Coroutine refreshRoutine;
    private bool rewardAccentsResolved;

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
        SubscribeRunEvents();

        if (IsMapActive())
            EnterMapPresentation();
    }

    private void OnDisable()
    {
        if (refreshRoutine != null)
            StopCoroutine(refreshRoutine);
        refreshRoutine = null;

        UnsubscribeRunEvents();
        ExitMapPresentation();
    }

    private void OnDestroy()
    {
        UnsubscribeRunEvents();
        RestoreMapOnlyState();
    }

    private void SubscribeRunEvents()
    {
        if (subscribedRunManager == runManager)
            return;

        if (subscribedRunManager != null)
        {
            subscribedRunManager.StateChanged -= HandleRunStateChanged;
            subscribedRunManager.NextNodeSelectionRequested -= HandleNextNodeSelectionRequested;
            subscribedRunManager.NodeEntered -= HandleNodeEntered;
        }

        subscribedRunManager = runManager;
        if (subscribedRunManager != null)
        {
            subscribedRunManager.StateChanged += HandleRunStateChanged;
            subscribedRunManager.NextNodeSelectionRequested += HandleNextNodeSelectionRequested;
            subscribedRunManager.NodeEntered += HandleNodeEntered;
        }
    }

    private void UnsubscribeRunEvents()
    {
        if (subscribedRunManager != null)
        {
            subscribedRunManager.StateChanged -= HandleRunStateChanged;
            subscribedRunManager.NextNodeSelectionRequested -= HandleNextNodeSelectionRequested;
            subscribedRunManager.NodeEntered -= HandleNodeEntered;
        }

        subscribedRunManager = null;
    }

    private void HandleRunStateChanged(BattleRunState state)
    {
        if (state == BattleRunState.SelectingNode)
            EnterMapPresentation();
        else if (presentationState != MapPresentationState.Hidden)
            ExitMapPresentation();
    }

    private void HandleNextNodeSelectionRequested(IReadOnlyList<BattleNodeData> _)
    {
        if (presentationState == MapPresentationState.Active ||
            presentationState == MapPresentationState.Entering)
        {
            QueueRefresh();
        }
    }

    private void HandleNodeEntered(BattleNodeData _)
    {
        if (presentationState != MapPresentationState.Hidden)
            ExitMapPresentation();
    }

    private void EnterMapPresentation()
    {
        if (!isActiveAndEnabled)
            return;

        presentationState = MapPresentationState.Entering;
        ResolveUi();
        CaptureFrameState();
        ApplyMapFrame();
        MaintainInteraction();
        ApplyMapHierarchy();
        presentationState = MapPresentationState.Active;
    }

    private void ExitMapPresentation()
    {
        if (presentationState == MapPresentationState.Hidden)
            return;

        presentationState = MapPresentationState.Exiting;
        RestoreMapOnlyState();
        presentationState = MapPresentationState.Hidden;
    }

    private void QueueRefresh()
    {
        if (!isActiveAndEnabled)
            return;

        if (refreshRoutine != null)
            StopCoroutine(refreshRoutine);

        refreshRoutine = StartCoroutine(RefreshAfterLayout());
    }

    private IEnumerator RefreshAfterLayout()
    {
        yield return null;
        refreshRoutine = null;

        if (!IsMapActive())
            yield break;

        ResolveUi();
        ApplyMapFrame();
        MaintainInteraction();
        ApplyMapHierarchy();
        presentationState = MapPresentationState.Active;
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

        SubscribeRunEvents();
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
                innerOutline.effectColor = new Color(currentColor.r, currentColor.g, currentColor.b, 0.16f);
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
            if (tvCanvas.renderMode == RenderMode.WorldSpace && tvCanvas.worldCamera == null)
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

        bool selectable = button != null && button.interactable;
        bool elite = label != null &&
                     (label.text ?? string.Empty).IndexOf(
                         "ELITE",
                         System.StringComparison.OrdinalIgnoreCase) >= 0;
        bool current = runManager != null &&
                       runManager.CurrentNode != null &&
                       rect.name == $"StageNode_{runManager.CurrentNode.id}";

        BattleStageMapNodeVisualState visualState = current
            ? BattleStageMapNodeVisualState.Current
            : selectable
                ? BattleStageMapNodeVisualState.Available
                : BattleStageMapNodeVisualState.Locked;

        Color stateAccent = current
            ? currentColor
            : selectable
                ? elite ? eliteColor : availableColor
                : mutedColor;

        image.enabled = true;
        image.raycastTarget = selectable;

        if (button != null)
        {
            button.transition = Selectable.Transition.None;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
        }

        float resolvedSize = selectable
            ? elite
                ? Mathf.Max(86f, eliteNodeSize)
                : Mathf.Max(76f, selectableNodeSize)
            : current
                ? Mathf.Max(70f, selectableNodeSize * 0.92f)
                : Mathf.Max(60f, selectableNodeSize * 0.82f);
        rect.sizeDelta = Vector2.one * resolvedSize;

        string nodeLabel = BuildNodeLabel(rect, label != null ? label.text : string.Empty);
        if (label != null)
        {
            label.text = nodeLabel;
            label.fontStyle = visualState == BattleStageMapNodeVisualState.Locked
                ? FontStyle.Normal
                : FontStyle.Bold;
            label.fontSize = visualState == BattleStageMapNodeVisualState.Locked ? 9 : 11;
        }

        BattleStageMapNodePointerFeedback feedback =
            rect.GetComponent<BattleStageMapNodePointerFeedback>();
        if (feedback == null)
            feedback = rect.gameObject.AddComponent<BattleStageMapNodePointerFeedback>();

        feedback.Configure(
            image,
            outline,
            label,
            visualState,
            stateAccent,
            inkColor,
            paperColor,
            mutedColor);
        feedback.BindButton(button);
    }

    private void EnsureControlHint()
    {
        if (mapContent == null || mapContent.Find(ControlHintName) != null)
            return;

        RectTransform rect = CreateRect(mapContent, ControlHintName, new Vector2(390f, 28f));
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-28f, 18f);

        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = "HOVER = PREVIEW   //   CLICK = SELECT";
        text.fontSize = 11;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleRight;
        text.color = availableColor;
        text.raycastTarget = false;
    }

    private static string BuildNodeLabel(RectTransform rect, string source)
    {
        // Battle rating is rendered as five Image stars by BattleSpatialMapController.
        // Keep the text label semantic-only so the node does not duplicate the same data.
        return ExtractNodeType(source);
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

internal enum BattleStageMapNodeVisualState
{
    Locked,
    Current,
    Available
}

/// <summary>
/// Map Node의 명시적 상태와 Hover/Selected 공간 연출을 한 곳에서 관리합니다.
/// Transform은 localPosition Z + XYZ rotation + scale을 모두 Tween합니다.
/// </summary>
internal sealed class BattleStageMapNodePointerFeedback :
    MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private const float AnimationSharpness = 18f;

    private RectTransform nodeRect;
    private CanvasGroup nodeGroup;
    private Image nodeImage;
    private Image nodeIcon;
    private Outline nodeOutline;
    private Text label;
    private BattleStageMapNodeVisualState baseState;
    private Color stateAccent;
    private Color inkColor;
    private Color paperColor;
    private Color mutedColor;
    private Color baseIconColor = Color.white;
    private string baseLabel = "STAGE";
    private bool hovered;
    private bool selected;
    private bool trackedHover;
    private Button boundButton;

    public void Configure(
        Image image,
        Outline outline,
        Text nodeLabel,
        BattleStageMapNodeVisualState visualState,
        Color accentColor,
        Color normalInkColor,
        Color textColor,
        Color disabledColor)
    {
        nodeImage = image;
        nodeRect = image != null ? image.rectTransform : null;
        nodeGroup = nodeRect != null ? nodeRect.GetComponent<CanvasGroup>() : null;
        if (nodeRect != null && nodeGroup == null)
            nodeGroup = nodeRect.gameObject.AddComponent<CanvasGroup>();
        nodeOutline = outline;
        label = nodeLabel;
        baseState = visualState;
        stateAccent = accentColor;
        inkColor = normalInkColor;
        paperColor = textColor;
        mutedColor = disabledColor;

        ResolveIcon();

        // The Button RectTransform is the stable hit target. Never spatially tween it:
        // moving a World-Space raycast target under a stationary cursor creates
        // Enter/Exit feedback loops when the map camera also pans.
        if (nodeRect != null)
        {
            nodeRect.localScale = Vector3.one;
            Vector3 stableLocal = nodeRect.localPosition;
            stableLocal.z = 0f;
            nodeRect.localPosition = stableLocal;
            nodeRect.localRotation = Quaternion.identity;
        }

        if (label != null)
            baseLabel = ExtractBaseLabel(label.text);

        if (selected)
            ApplySelected();
        else if (hovered && baseState == BattleStageMapNodeVisualState.Available)
            ApplyHover();
        else
            ApplyBaseVisual();
    }

    private void Update()
    {
        if (nodeRect == null)
            return;

        ResolveIcon();

        float t = 1f - Mathf.Exp(-AnimationSharpness * Time.unscaledDeltaTime);

        // Spatial feedback lives on non-raycast visuals only.
        // The root Button stays perfectly fixed so hover cannot invalidate itself.
        if (nodeIcon != null)
        {
            float iconScale;
            float iconZ;
            Vector3 iconEuler;

            if (selected)
            {
                iconScale = 1.20f;
                iconZ = -14f;
                iconEuler = Vector3.zero;
            }
            else if (hovered && baseState == BattleStageMapNodeVisualState.Available)
            {
                iconScale = 1.12f;
                iconZ = -8f;
                iconEuler = new Vector3(0.2f, -0.8f, -1.2f);
            }
            else if (baseState == BattleStageMapNodeVisualState.Locked)
            {
                iconScale = 0.92f;
                iconZ = 6f;
                iconEuler = new Vector3(1.5f, -3.0f, 0.8f);
            }
            else
            {
                iconScale = 1f;
                iconZ = 0f;
                iconEuler = Vector3.zero;
            }

            RectTransform iconRect = nodeIcon.rectTransform;
            iconRect.localScale = Vector3.Lerp(
                iconRect.localScale,
                Vector3.one * iconScale,
                t);

            Vector3 iconLocal = iconRect.localPosition;
            iconLocal.z = Mathf.Lerp(iconLocal.z, iconZ, t);
            iconRect.localPosition = iconLocal;

            iconRect.localRotation = Quaternion.Slerp(
                iconRect.localRotation,
                Quaternion.Euler(iconEuler),
                t);
        }

        if (label != null)
        {
            float labelScale = selected
                ? 1.06f
                : hovered && baseState == BattleStageMapNodeVisualState.Available
                    ? 1.035f
                    : baseState == BattleStageMapNodeVisualState.Locked
                        ? 0.96f
                        : 1f;

            label.rectTransform.localScale = Vector3.Lerp(
                label.rectTransform.localScale,
                Vector3.one * labelScale,
                t);
        }

        if (nodeGroup != null)
        {
            float targetAlpha = selected || hovered
                ? 1f
                : baseState == BattleStageMapNodeVisualState.Locked
                    ? 0.42f
                    : baseState == BattleStageMapNodeVisualState.Current
                        ? 1f
                        : 0.94f;
            nodeGroup.alpha = Mathf.Lerp(nodeGroup.alpha, targetAlpha, t);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (selected || baseState != BattleStageMapNodeVisualState.Available)
            return;

        hovered = true;
        ApplyHover();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (selected || trackedHover)
            return;

        hovered = false;
        ApplyBaseVisual();
    }

    public void SetTrackedHover(bool value)
    {
        trackedHover = value;

        if (selected || baseState != BattleStageMapNodeVisualState.Available)
            return;

        hovered = value;
        if (hovered)
            ApplyHover();
        else
            ApplyBaseVisual();
    }

    public void BindButton(Button button)
    {
        if (boundButton == button)
            return;

        if (boundButton != null)
            boundButton.onClick.RemoveListener(NotifySelected);

        boundButton = button;
        if (boundButton != null)
        {
            boundButton.transition = Selectable.Transition.None;
            boundButton.onClick.RemoveListener(NotifySelected);
            boundButton.onClick.AddListener(NotifySelected);
        }
    }

    public void NotifySelected()
    {
        if (baseState != BattleStageMapNodeVisualState.Available)
            return;

        selected = true;
        hovered = false;
        ApplySelected();
    }

    private void OnDisable()
    {
        hovered = false;
        selected = false;
        trackedHover = false;
    }

    private void ApplyHover()
    {
        if (nodeImage != null)
        {
            nodeImage.enabled = true;
            nodeImage.color = paperColor;
        }

        if (nodeOutline != null)
        {
            nodeOutline.effectColor = stateAccent;
            nodeOutline.effectDistance = new Vector2(4f, -4f);
        }

        if (nodeIcon != null)
        {
            nodeIcon.enabled = true;
            nodeIcon.color = inkColor;
        }

        if (label != null)
        {
            label.gameObject.SetActive(true);
            label.text = baseLabel + "\nSELECT";
            label.color = inkColor;
            label.fontStyle = FontStyle.Bold;
        }
    }

    private void ApplySelected()
    {
        if (nodeImage != null)
        {
            nodeImage.enabled = true;
            nodeImage.color = new Color(stateAccent.r, stateAccent.g, stateAccent.b, 0.94f);
        }

        if (nodeOutline != null)
        {
            nodeOutline.effectColor = paperColor;
            nodeOutline.effectDistance = new Vector2(4f, -4f);
        }

        if (nodeIcon != null)
        {
            nodeIcon.enabled = true;
            nodeIcon.color = inkColor;
        }

        if (label != null)
        {
            label.gameObject.SetActive(true);
            label.text = baseLabel + "\nSELECTED";
            label.color = inkColor;
            label.fontStyle = FontStyle.Bold;
        }
    }

    private void ApplyBaseVisual()
    {
        if (nodeImage == null)
            return;

        nodeImage.enabled = true;

        switch (baseState)
        {
            case BattleStageMapNodeVisualState.Current:
                nodeImage.color = Color.Lerp(inkColor, stateAccent, 0.20f);
                if (nodeOutline != null)
                {
                    nodeOutline.effectColor = stateAccent;
                    nodeOutline.effectDistance = new Vector2(3f, -3f);
                }
                if (label != null)
                {
                    label.text = baseLabel + "\nCURRENT";
                    label.color = stateAccent;
                    label.fontStyle = FontStyle.Bold;
                }
                if (nodeIcon != null)
                    nodeIcon.color = stateAccent;
                break;

            case BattleStageMapNodeVisualState.Available:
                nodeImage.color = inkColor;
                if (nodeOutline != null)
                {
                    nodeOutline.effectColor = stateAccent;
                    nodeOutline.effectDistance = new Vector2(2f, -2f);
                }
                if (label != null)
                {
                    label.text = baseLabel + "\nAVAILABLE";
                    label.color = paperColor;
                    label.fontStyle = FontStyle.Bold;
                }
                if (nodeIcon != null)
                    nodeIcon.color = baseIconColor;
                break;

            default:
                nodeImage.color = new Color(
                    inkColor.r * 0.78f,
                    inkColor.g * 0.78f,
                    inkColor.b * 0.82f,
                    0.92f);
                if (nodeOutline != null)
                {
                    nodeOutline.effectColor = new Color(
                        mutedColor.r,
                        mutedColor.g,
                        mutedColor.b,
                        0.28f);
                    nodeOutline.effectDistance = new Vector2(1f, -1f);
                }
                if (label != null)
                {
                    label.text = baseLabel + "\nLOCKED";
                    label.color = mutedColor;
                    label.fontStyle = FontStyle.Normal;
                }
                if (nodeIcon != null)
                    nodeIcon.color = mutedColor;
                break;
        }
    }

    private void ResolveIcon()
    {
        Image resolved = FindNodeIcon(nodeRect, nodeImage);
        if (resolved == null)
            return;

        if (resolved != nodeIcon)
        {
            nodeIcon = resolved;
            baseIconColor = nodeIcon.color;

            if (selected)
                ApplySelected();
            else if (hovered && baseState == BattleStageMapNodeVisualState.Available)
                ApplyHover();
            else
                ApplyBaseVisual();
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
            if (candidate.name.IndexOf("Icon", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return candidate;
        }

        return null;
    }

    private static string ExtractBaseLabel(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "STAGE";

        string normalized = source.Replace("\r", string.Empty).Trim();
        string[] states = { "AVAILABLE", "SELECT", "SELECTED", "CURRENT", "LOCKED" };

        for (int i = 0; i < states.Length; i++)
        {
            string suffix = "\n" + states[i];
            if (normalized.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - suffix.Length).TrimEnd();
                break;
            }
        }

        return normalized.ToUpperInvariant();
    }
}

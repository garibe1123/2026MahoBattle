using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
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
    [SerializeField, Min(80f)] private float pointerHitSize = 150f;
    [SerializeField, Min(64f)] private float selectableNodeSize = 112f;
    [SerializeField, Min(72f)] private float eliteNodeSize = 124f;

    private enum MapPresentationState
    {
        Hidden,
        Entering,
        Active,
        Exiting
    }

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
    private bool subscribed;
    private Coroutine bindRoutine;
    private Coroutine refreshRoutine;
    private MapPresentationState presentationState = MapPresentationState.Hidden;

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
        Subscribe();
        QueuePresentationRefresh();

        if (runManager == null && bindRoutine == null)
            bindRoutine = StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
            StopCoroutine(bindRoutine);
        if (refreshRoutine != null)
            StopCoroutine(refreshRoutine);

        bindRoutine = null;
        refreshRoutine = null;
        Unsubscribe();
        RestoreMapOnlyState();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        RestoreMapOnlyState();
    }

    private IEnumerator BindWhenReady()
    {
        while (enabled && runManager == null)
        {
            ResolveReferences();
            yield return null;
        }

        bindRoutine = null;
        if (!enabled)
            yield break;

        Subscribe();
        QueuePresentationRefresh();
    }

    private void Subscribe()
    {
        if (subscribed || runManager == null)
            return;

        runManager.StateChanged += HandleRunStateChanged;
        runManager.NextNodeSelectionRequested += HandleNodeSelectionRequested;
        runManager.NodeEntered += HandleNodeEntered;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || runManager == null)
        {
            subscribed = false;
            return;
        }

        runManager.StateChanged -= HandleRunStateChanged;
        runManager.NextNodeSelectionRequested -= HandleNodeSelectionRequested;
        runManager.NodeEntered -= HandleNodeEntered;
        subscribed = false;
    }

    private void HandleRunStateChanged(BattleRunState _)
    {
        QueuePresentationRefresh();
    }

    private void HandleNodeSelectionRequested(IReadOnlyList<BattleNodeData> _)
    {
        // BattleSpatialMapController가 같은 이벤트에서 노드를 재구축한 뒤 한 프레임 뒤 스타일링합니다.
        QueuePresentationRefresh();
    }

    private void HandleNodeEntered(BattleNodeData _)
    {
        QueuePresentationRefresh();
    }

    private void QueuePresentationRefresh()
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
        RefreshPresentationState();
    }

    private void RefreshPresentationState()
    {
        ResolveReferences();
        ResolveUi();

        bool mapActive = IsMapActive();
        if (!mapActive)
        {
            if (wasMapActive)
            {
                presentationState = MapPresentationState.Exiting;
                RestoreMapOnlyState();
            }

            wasMapActive = false;
            presentationState = MapPresentationState.Hidden;
            return;
        }

        bool enteringMap = !wasMapActive;
        if (enteringMap)
        {
            presentationState = MapPresentationState.Entering;
            CaptureFrameState();
            wasMapActive = true;
        }

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

        // Reward의 거대한 Glass/Key strip을 Map에서 절대 재사용하지 않습니다.
        // 이것이 회색 판 + 노란 세로 장식이 Map 위를 덮던 직접 원인이었습니다.
        DisableLegacySpatialGlassForMap(sharedFrame);
        DisableLegacySpatialGlassForMap(screenInner);

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
                innerImage.color = new Color(0.012f, 0.016f, 0.024f, 0.88f);

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

            // Board 자체도 완전 평면이 아니라 실제 TV 공간에 놓인 얕은 면으로 보이게 합니다.
            mapContent.localRotation = Quaternion.Euler(0.45f, -0.85f, -0.30f);
        }

        DisableMapClipping(viewport);
        DisableMapClipping(mapContent);
        DisableRewardAccentsDuringMap();
    }

    private void DisableLegacySpatialGlassForMap(RectTransform rect)
    {
        if (rect == null)
            return;

        BattleSpatialGlassPanel glass = rect.GetComponent<BattleSpatialGlassPanel>();
        if (glass != null)
        {
            if (!clippingStates.ContainsKey(glass))
                clippingStates.Add(glass, glass.enabled);
            glass.enabled = false;
        }

        string[] names = { "__Spatial_Shadow", "__Spatial_Glass", "__Spatial_Key" };
        for (int i = 0; i < names.Length; i++)
        {
            Transform child = rect.Find(names[i]);
            if (child == null)
                continue;

            GameObject go = child.gameObject;
            if (!rewardAccentStates.ContainsKey(go))
                rewardAccentStates.Add(go, go.activeSelf);

            if (go.activeSelf)
                go.SetActive(false);
        }
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
            muted.a = 0.28f;
            image.color = muted;
        }

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
        Image icon = rect.Find("RoomTypeIcon")?.GetComponent<Image>();
        if (image == null || outline == null)
            return;

        BattleNodeData node = ResolveNodeData(rect);
        bool selectable = button != null && button.interactable;
        bool elite = node != null && node.type == BattleNodeType.Elite;
        bool current = runManager != null &&
                       runManager.CurrentNode != null &&
                       node == runManager.CurrentNode;

        BattleUIThemeProfile theme = uiTheme != null ? uiTheme.CurrentProfile : null;
        Color action = elite
            ? accentPink
            : theme != null ? theme.keyColor : accentYellow;
        Color currentColor = accentCyan;
        Color textPrimary = theme != null ? theme.textPrimary : paperColor;
        Color textMuted = theme != null ? theme.textMuted : mutedColor;
        string nodeLabel = BuildNodeLabel(rect, label != null ? label.text : string.Empty);

        DisableLegacySpatialGlass(rect);

        image.enabled = true;
        // Map click/hover is resolved in BattleShowMapEquipmentPolishController with the
        // actual World-Space Canvas event camera. Disable GraphicRaycaster ownership here
        // to avoid Scene/Game camera mismatch and duplicate click paths.
        image.raycastTarget = false;
        if (button != null)
            button.transition = Selectable.Transition.None;

        if (selectable)
        {
            float resolvedSize = elite
                ? Mathf.Max(124f, eliteNodeSize)
                : Mathf.Max(112f, selectableNodeSize);
            rect.sizeDelta = Vector2.one * resolvedSize;

            image.color = new Color(action.r, action.g, action.b, 0.20f);
            outline.enabled = true;
            outline.effectColor = new Color(action.r, action.g, action.b, 0.92f);
            outline.effectDistance = new Vector2(2f, -2f);

            float side = Mathf.Abs(rect.anchoredPosition.x) < 0.01f
                ? 0f
                : Mathf.Sign(rect.anchoredPosition.x);
            rect.localRotation = Quaternion.Euler(1.2f, -side * 3.2f, side * 0.7f);
            Vector3 local = rect.localPosition;
            local.z = -6f;
            rect.localPosition = local;

            if (label != null)
            {
                label.text = nodeLabel;
                label.fontStyle = FontStyle.Bold;
                label.fontSize = 13;
                label.color = textPrimary;
                label.raycastTarget = false;
                label.rectTransform.sizeDelta = new Vector2(156f, 54f);
                label.rectTransform.anchoredPosition = new Vector2(0f, -10f);
            }

            if (icon != null)
            {
                icon.rectTransform.sizeDelta = Vector2.one * 38f;
                icon.color = action;
            }

            EnsurePointerHitArea(rect, image, outline, label, action);
        }
        else if (current)
        {
            image.color = new Color(currentColor.r, currentColor.g, currentColor.b, 0.16f);
            outline.enabled = true;
            outline.effectColor = new Color(currentColor.r, currentColor.g, currentColor.b, 0.72f);
            outline.effectDistance = new Vector2(2f, -2f);

            rect.localRotation = Quaternion.Euler(0.6f, 1.4f, 0.25f);
            Vector3 local = rect.localPosition;
            local.z = -2f;
            rect.localPosition = local;

            if (label != null)
            {
                label.text = "CURRENT\n" + nodeLabel;
                label.color = currentColor;
                label.fontStyle = FontStyle.Bold;
                label.fontSize = 12;
                label.raycastTarget = false;
                label.rectTransform.sizeDelta = new Vector2(156f, 54f);
                label.rectTransform.anchoredPosition = new Vector2(0f, -10f);
            }

            if (icon != null)
            {
                icon.rectTransform.sizeDelta = Vector2.one * 36f;
                icon.color = currentColor;
            }
        }
        else
        {
            image.color = new Color(panelColor.r, panelColor.g, panelColor.b, 0.34f);
            outline.enabled = false;

            float side = Mathf.Abs(rect.anchoredPosition.x) < 0.01f
                ? 0f
                : Mathf.Sign(rect.anchoredPosition.x);
            rect.localRotation = Quaternion.Euler(2.2f, -side * 4.2f, side * 1.0f);
            Vector3 local = rect.localPosition;
            local.z = 8f;
            rect.localPosition = local;

            if (label != null)
            {
                label.text = nodeLabel;
                label.color = new Color(textMuted.r, textMuted.g, textMuted.b, 0.72f);
                label.fontStyle = FontStyle.Normal;
                label.fontSize = 10;
                label.raycastTarget = false;
                label.rectTransform.sizeDelta = new Vector2(150f, 48f);
                label.rectTransform.anchoredPosition = new Vector2(0f, -9f);
            }

            if (icon != null)
            {
                icon.rectTransform.sizeDelta = Vector2.one * 32f;
                icon.color = textMuted;
            }
        }
    }

    private static void DisableLegacySpatialGlass(RectTransform rect)
    {
        if (rect == null)
            return;

        BattleSpatialGlassPanel glass = rect.GetComponent<BattleSpatialGlassPanel>();
        if (glass != null)
            glass.enabled = false;

        string[] names = { "__Spatial_Shadow", "__Spatial_Glass", "__Spatial_Key" };
        for (int i = 0; i < names.Length; i++)
        {
            Transform child = rect.Find(names[i]);
            if (child != null && child.gameObject.activeSelf)
                child.gameObject.SetActive(false);
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

        hitRect.sizeDelta = Vector2.one * Mathf.Max(150f, pointerHitSize);

        Button nodeButton = node.GetComponent<Button>();
        bool clickable = nodeButton != null && nodeButton.interactable;

        if (hitImage != null)
            hitImage.raycastTarget = false;

        // Map selection callback ownership은 원본 StageNode Button 하나만 유지합니다.
        // 넓은 HitArea는 hover/raycast와 BattleSpatialMapController의 direct hit-test에만 사용합니다.
        Button legacyHitButton = hitRect.GetComponent<Button>();
        if (legacyHitButton != null)
        {
            legacyHitButton.onClick.RemoveAllListeners();
            legacyHitButton.enabled = false;
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

        RectTransform rect = CreateRect(mapContent, ControlHintName, new Vector2(360f, 28f));
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-28f, 18f);

        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = "HOVER = PREVIEW   /   CLICK = SELECT";
        text.fontSize = 11;
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

        return typeName + "\nRATING " + stars + " / 5";
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
    IPointerExitHandler,
    IPointerClickHandler
{
    private const float HoverScale = 1.12f;
    private const float SelectedScale = 1.08f;
    private const float TweenDuration = 0.18f;

    private RectTransform nodeRect;
    private Image nodeImage;
    private Image nodeIcon;
    private Button nodeButton;
    private Outline nodeOutline;
    private Text label;
    private Color accent;
    private Color baseFill;
    private Color baseOutline;
    private Color baseLabelColor;
    private Color baseIconColor = Color.white;
    private string baseLabel = "STAGE";
    private Quaternion baseRotation = Quaternion.identity;
    private float baseZ;
    private bool hovered;
    private bool selected;

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
        nodeButton = nodeRect != null ? nodeRect.GetComponent<Button>() : null;
        nodeOutline = outline;
        label = nodeLabel;
        accent = accentColor;

        if (nodeRect != null)
        {
            baseRotation = nodeRect.localRotation;
            baseZ = nodeRect.localPosition.z;
        }

        if (nodeImage != null)
            baseFill = nodeImage.color;
        if (nodeOutline != null)
            baseOutline = nodeOutline.effectColor;
        if (label != null)
        {
            baseLabel = ExtractLabel(label.text);
            baseLabelColor = label.color;
        }

        Image resolvedIcon = FindNodeIcon(nodeRect, nodeImage);
        if (resolvedIcon != nodeIcon)
        {
            nodeIcon = resolvedIcon;
            if (nodeIcon != null)
                baseIconColor = nodeIcon.color;
        }

        if (!selected && !hovered)
            ApplyNormal(true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetHovered(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetHovered(false);
    }

    public void SetHovered(bool value)
    {
        if (selected || hovered == value)
            return;

        hovered = value;
        if (hovered)
            ApplyHover();
        else
            ApplyNormal(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (selected || nodeButton == null ||
            !nodeButton.IsActive() || !nodeButton.IsInteractable())
        {
            return;
        }

        NotifySelected();
        nodeButton.onClick.Invoke();
    }

    public void NotifySelected()
    {
        if (selected)
            return;

        selected = true;
        hovered = false;

        if (nodeImage != null)
            nodeImage.color = new Color(accent.r, accent.g, accent.b, 0.34f);

        if (nodeOutline != null)
        {
            nodeOutline.enabled = true;
            nodeOutline.effectColor = accent;
        }

        if (label != null)
        {
            label.text = baseLabel + "\nSELECTED";
            label.color = accent;
            label.fontStyle = FontStyle.Bold;
        }

        TweenPose(Vector3.one * SelectedScale, Quaternion.identity, -28f);
    }

    private void ApplyHover()
    {
        if (nodeImage != null)
            nodeImage.color = new Color(accent.r, accent.g, accent.b, 0.28f);

        if (nodeOutline != null)
        {
            nodeOutline.enabled = true;
            nodeOutline.effectColor = accent;
        }

        if (nodeIcon != null)
            nodeIcon.color = Color.white;

        if (label != null)
        {
            label.text = baseLabel + "\nSELECT";
            label.color = accent;
            label.fontStyle = FontStyle.Bold;
        }

        TweenPose(Vector3.one * HoverScale, Quaternion.Euler(0.2f, 0f, 0f), -18f);
    }

    private void ApplyNormal(bool immediate)
    {
        if (nodeImage != null)
            nodeImage.color = baseFill;

        if (nodeOutline != null)
        {
            nodeOutline.enabled = true;
            nodeOutline.effectColor = baseOutline;
        }

        if (nodeIcon != null)
            nodeIcon.color = baseIconColor;

        if (label != null)
        {
            label.text = baseLabel;
            label.color = baseLabelColor;
            label.fontStyle = FontStyle.Bold;
        }

        if (nodeRect == null)
            return;

        if (immediate)
        {
            nodeRect.DOKill();
            nodeRect.localScale = Vector3.one;
            nodeRect.localRotation = baseRotation;
            Vector3 local = nodeRect.localPosition;
            local.z = baseZ;
            nodeRect.localPosition = local;
            return;
        }

        TweenPose(Vector3.one, baseRotation, baseZ);
    }

    private void TweenPose(Vector3 scale, Quaternion rotation, float z)
    {
        if (nodeRect == null)
            return;

        nodeRect.DOKill();
        nodeRect.DOScale(scale, TweenDuration).SetUpdate(true).SetEase(Ease.OutCubic);
        nodeRect.DOLocalRotate(rotation.eulerAngles, TweenDuration, RotateMode.Fast)
            .SetUpdate(true)
            .SetEase(Ease.OutCubic);
        nodeRect.DOLocalMoveZ(z, TweenDuration)
            .SetUpdate(true)
            .SetEase(Ease.OutCubic);
    }

    private void OnDisable()
    {
        if (nodeRect != null)
            nodeRect.DOKill();

        hovered = false;
        selected = false;
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
        int selectedLine = normalized.IndexOf("\nSELECTED", System.StringComparison.OrdinalIgnoreCase);
        if (selectedLine >= 0)
            normalized = normalized.Substring(0, selectedLine);

        int selectLine = normalized.IndexOf("\nSELECT", System.StringComparison.OrdinalIgnoreCase);
        if (selectLine >= 0)
            normalized = normalized.Substring(0, selectLine);

        return normalized.Trim().ToUpperInvariant();
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Stage Map의 연출을 "항상 화려한 UI"가 아니라 "결정해야 하는 곳만 강하게" 보이도록 정리합니다.
///
/// - Reward 테마가 공용 TV Frame에 남긴 기울기/강한 장식을 Map에서 중화합니다.
/// - MapSelectionContent를 TV 전체 Safe Area로 확장하고 Map 전용 Mask/RectMask를 해제해 가장자리 잘림을 줄입니다.
/// - WorldSpace Canvas / CanvasGroup의 Raycast 상태를 보강합니다.
/// - 선택 가능한 StageNode는 표시 크기와 별개로 넓은 투명 Pointer Hit Area를 가집니다.
/// - 평상시 Path/Node는 절제하고, Hover/선택 가능한 Node에서만 Yellow/Pink Accent가 강해집니다.
/// - Map 상태 동안 기존 Show Focus를 조금 더 강하게 하여 실제 화면으로 시선을 모읍니다.
///
/// 기존 NodeGraphSO, Scene, Sprite, Reward 데이터는 수정하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32580)]
public sealed class BattleStageMapPurposefulUIController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
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
    [SerializeField] private BattleShowFocusController showFocus;

    [Header("Purposeful Map Theme")]
    [SerializeField] private Color inkColor = new(0.028f, 0.030f, 0.040f, 0.995f);
    [SerializeField] private Color panelColor = new(0.052f, 0.055f, 0.068f, 0.995f);
    [SerializeField] private Color paperColor = new(0.93f, 0.90f, 0.80f, 1f);
    [SerializeField] private Color mutedColor = new(0.36f, 0.39f, 0.46f, 0.76f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.48f, 1f);
    [SerializeField] private Color accentCyan = new(0.16f, 0.86f, 0.92f, 1f);
    [SerializeField, Min(56f)] private float pointerHitSize = 92f;
    [SerializeField, Min(28f)] private float selectableNodeSize = 52f;
    [SerializeField, Min(28f)] private float eliteNodeSize = 58f;
    [SerializeField, Min(0.02f)] private float refreshInterval = 0.08f;

    [Header("Map Screen Focus")]
    [SerializeField, Range(0f, 1f)] private float mapNearDimAlpha = 0.72f;
    [SerializeField, Range(0f, 1f)] private float mapFarDimAlpha = 0.985f;
    [SerializeField, Range(0.05f, 1.5f)] private float mapDimFalloffRadius = 0.50f;
    [SerializeField, Range(0f, 1f)] private float openingMapNearDimAlpha = 0.58f;
    [SerializeField, Range(0f, 1f)] private float openingMapFarDimAlpha = 0.955f;
    [SerializeField, Range(0.05f, 1.5f)] private float openingMapDimFalloffRadius = 0.60f;
    [SerializeField, Range(0f, 0.05f)] private float mapScreenFocusPadding = 0.012f;

    private RectTransform sharedFrame;
    private RectTransform screenInner;
    private RectTransform viewport;
    private RectTransform mapContent;
    private CanvasGroup viewportGroup;
    private CanvasGroup mapGroup;
    private CanvasGroup tvGroup;
    private Canvas tvCanvas;

    private float nextRefresh;
    private bool wasMapActive;
    private bool focusValuesCaptured;
    private FocusValues originalFocusValues;

    private bool frameVisualCaptured;
    private FrameVisualState originalFrameVisual;
    private readonly Dictionary<GameObject, bool> rewardAccentStates = new();
    private readonly Dictionary<Behaviour, bool> clippingStates = new();

    private readonly struct FocusValues
    {
        public readonly float near;
        public readonly float far;
        public readonly float radius;
        public readonly float openingNear;
        public readonly float openingFar;
        public readonly float openingRadius;
        public readonly float padding;

        public FocusValues(
            float near,
            float far,
            float radius,
            float openingNear,
            float openingFar,
            float openingRadius,
            float padding)
        {
            this.near = near;
            this.far = far;
            this.radius = radius;
            this.openingNear = openingNear;
            this.openingFar = openingFar;
            this.openingRadius = openingRadius;
            this.padding = padding;
        }
    }

    private struct FrameVisualState
    {
        public Quaternion rotation;
        public Color frameColor;
        public bool hasFrameImage;
        public Color frameOutlineColor;
        public Vector2 frameOutlineDistance;
        public bool hasFrameOutline;
        public Color innerColor;
        public bool hasInnerImage;
        public Color innerOutlineColor;
        public Vector2 innerOutlineDistance;
        public bool hasInnerOutline;
    }

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

        if (!wasMapActive)
        {
            ResolveUi();
            CaptureFrameVisual();
            CaptureFocusValues();
            wasMapActive = true;
        }

        ApplyFocusedMapSettings();
        MaintainInteraction();

        if (Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + Mathf.Max(0.02f, refreshInterval);
        ResolveUi();
        ApplyMapFrame();
        ApplyMapHierarchy();
    }

    private void LateUpdate()
    {
        if (!IsMapActive())
            return;

        // Reward Theme / Shared TV가 같은 프레임을 늦게 다시 만져도 Map 정책이 최종 상태가 되게 합니다.
        ApplyMapFrame();
        MaintainInteraction();
    }

    private bool IsMapActive()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.SelectingNode;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (showFocus == null)
            showFocus = FindFirstObjectByType<BattleShowFocusController>();
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

    private void CaptureFrameVisual()
    {
        if (frameVisualCaptured || sharedFrame == null)
            return;

        Image frameImage = sharedFrame.GetComponent<Image>();
        Outline frameOutline = sharedFrame.GetComponent<Outline>();
        Image innerImage = screenInner != null ? screenInner.GetComponent<Image>() : null;
        Outline innerOutline = screenInner != null ? screenInner.GetComponent<Outline>() : null;

        originalFrameVisual = new FrameVisualState
        {
            rotation = sharedFrame.localRotation,
            frameColor = frameImage != null ? frameImage.color : Color.white,
            hasFrameImage = frameImage != null,
            frameOutlineColor = frameOutline != null ? frameOutline.effectColor : Color.white,
            frameOutlineDistance = frameOutline != null ? frameOutline.effectDistance : Vector2.zero,
            hasFrameOutline = frameOutline != null,
            innerColor = innerImage != null ? innerImage.color : Color.white,
            hasInnerImage = innerImage != null,
            innerOutlineColor = innerOutline != null ? innerOutline.effectColor : Color.white,
            innerOutlineDistance = innerOutline != null ? innerOutline.effectDistance : Vector2.zero,
            hasInnerOutline = innerOutline != null
        };
        frameVisualCaptured = true;
    }

    private void ApplyMapFrame()
    {
        ResolveUi();
        if (sharedFrame == null)
            return;

        sharedFrame.localRotation = Quaternion.identity;

        Image frameImage = sharedFrame.GetComponent<Image>();
        if (frameImage != null)
            frameImage.color = inkColor;

        Outline frameOutline = sharedFrame.GetComponent<Outline>();
        if (frameOutline != null)
        {
            frameOutline.effectColor = new Color(paperColor.r, paperColor.g, paperColor.b, 0.42f);
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
                innerOutline.effectColor = new Color(accentCyan.r, accentCyan.g, accentCyan.b, 0.20f);
                innerOutline.effectDistance = new Vector2(1f, -1f);
            }
        }

        if (mapContent != null)
        {
            // 기존 2.5~6% Inset을 없애 실제 TV 화면을 Safe Area로 전부 사용합니다.
            // SpatialMap의 기존 Node 좌표는 중앙 기준이라 그대로 유지되며 가장자리 여유만 늘어납니다.
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
        ResolveUi();

        if (viewportGroup != null)
        {
            viewportGroup.interactable = true;
            viewportGroup.blocksRaycasts = true;
        }

        if (mapGroup != null)
        {
            mapGroup.interactable = true;
            // Reveal/확정 Routine이 직접 false로 잠그는 경우를 존중하기 위해 Alpha가 충분할 때만 보강합니다.
            if (mapGroup.alpha >= 0.80f)
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

            if (rect.name.StartsWith("StageNode_", StringComparison.Ordinal))
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
        text.fontSize = 25;
        text.color = paperColor;

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(42f, -16f);
        rect.sizeDelta = new Vector2(-84f, 38f);
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
        rect.anchoredPosition = new Vector2(42f, -50f);
        rect.sizeDelta = new Vector2(-84f, 22f);
        rect.localRotation = Quaternion.identity;
    }

    private void StyleLink(RectTransform rect)
    {
        Image image = rect.GetComponent<Image>();
        if (image != null)
            image.color = new Color(paperColor.r, paperColor.g, paperColor.b, 0.22f);

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
        bool elite = label != null && (label.text ?? string.Empty).IndexOf("ELITE", StringComparison.OrdinalIgnoreCase) >= 0;
        bool current = runManager != null && runManager.CurrentNode != null &&
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
                string type = ExtractNodeType(label.text);
                label.text = type;
                label.fontStyle = FontStyle.Bold;
                label.fontSize = 10;
                label.color = paperColor;
            }

            EnsurePointerHitArea(rect, image, outline, label, accent);
        }
        else if (current)
        {
            image.color = new Color(accentCyan.r * 0.30f, accentCyan.g * 0.30f, accentCyan.b * 0.30f, 1f);
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
            hitImage = hit.AddComponent<Image>();
            hitImage.color = new Color(1f, 1f, 1f, 0.001f);
            hitImage.raycastTarget = true;
            hitRect.anchorMin = hitRect.anchorMax = new Vector2(0.5f, 0.5f);
            hitRect.pivot = new Vector2(0.5f, 0.5f);
            hitRect.anchoredPosition = Vector2.zero;
            hit.transform.SetAsLastSibling();
        }

        hitRect.sizeDelta = Vector2.one * Mathf.Max(56f, pointerHitSize);
        if (hitImage != null)
            hitImage.raycastTarget = true;

        BattleStageMapNodePointerFeedback feedback = hitRect.GetComponent<BattleStageMapNodePointerFeedback>();
        if (feedback == null)
            feedback = hitRect.gameObject.AddComponent<BattleStageMapNodePointerFeedback>();
        feedback.Configure(nodeImage, nodeOutline, label, accent, inkColor, paperColor);
    }

    private void EnsureDecisionAccent()
    {
        if (mapContent == null || mapContent.Find(DecisionAccentName) != null)
            return;

        RectTransform accent = CreateRect(mapContent, DecisionAccentName, new Vector2(8f, 52f));
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

        RectMask2D[] rectMasks = root.GetComponents<RectMask2D>();
        for (int i = 0; i < rectMasks.Length; i++)
        {
            RectMask2D mask = rectMasks[i];
            if (mask == null)
                continue;
            if (!clippingStates.ContainsKey(mask))
                clippingStates.Add(mask, mask.enabled);
            mask.enabled = false;
        }

        Mask[] masks = root.GetComponents<Mask>();
        for (int i = 0; i < masks.Length; i++)
        {
            Mask mask = masks[i];
            if (mask == null)
                continue;
            if (!clippingStates.ContainsKey(mask))
                clippingStates.Add(mask, mask.enabled);
            mask.enabled = false;
        }
    }

    private void DisableRewardAccentsDuringMap()
    {
        RectTransform[] all = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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

    private void CaptureFocusValues()
    {
        if (focusValuesCaptured || showFocus == null)
            return;

        originalFocusValues = new FocusValues(
            ReadFloat(showFocus, "nearDimAlpha", 0.62f),
            ReadFloat(showFocus, "farDimAlpha", 0.96f),
            ReadFloat(showFocus, "dimFalloffRadius", 0.58f),
            ReadFloat(showFocus, "openingMapNearDimAlpha", 0.48f),
            ReadFloat(showFocus, "openingMapFarDimAlpha", 0.92f),
            ReadFloat(showFocus, "openingMapDimFalloffRadius", 0.66f),
            ReadFloat(showFocus, "screenRectPadding", 0.002f));
        focusValuesCaptured = true;
    }

    private void ApplyFocusedMapSettings()
    {
        if (showFocus == null)
            return;

        CaptureFocusValues();
        WriteFloat(showFocus, "nearDimAlpha", mapNearDimAlpha);
        WriteFloat(showFocus, "farDimAlpha", mapFarDimAlpha);
        WriteFloat(showFocus, "dimFalloffRadius", mapDimFalloffRadius);
        WriteFloat(showFocus, "openingMapNearDimAlpha", openingMapNearDimAlpha);
        WriteFloat(showFocus, "openingMapFarDimAlpha", openingMapFarDimAlpha);
        WriteFloat(showFocus, "openingMapDimFalloffRadius", openingMapDimFalloffRadius);
        WriteFloat(showFocus, "screenRectPadding", mapScreenFocusPadding);
    }

    private void RestoreMapOnlyState()
    {
        if (focusValuesCaptured && showFocus != null)
        {
            WriteFloat(showFocus, "nearDimAlpha", originalFocusValues.near);
            WriteFloat(showFocus, "farDimAlpha", originalFocusValues.far);
            WriteFloat(showFocus, "dimFalloffRadius", originalFocusValues.radius);
            WriteFloat(showFocus, "openingMapNearDimAlpha", originalFocusValues.openingNear);
            WriteFloat(showFocus, "openingMapFarDimAlpha", originalFocusValues.openingFar);
            WriteFloat(showFocus, "openingMapDimFalloffRadius", originalFocusValues.openingRadius);
            WriteFloat(showFocus, "screenRectPadding", originalFocusValues.padding);
        }
        focusValuesCaptured = false;

        if (frameVisualCaptured && sharedFrame != null)
        {
            sharedFrame.localRotation = originalFrameVisual.rotation;
            Image frameImage = sharedFrame.GetComponent<Image>();
            if (originalFrameVisual.hasFrameImage && frameImage != null)
                frameImage.color = originalFrameVisual.frameColor;

            Outline frameOutline = sharedFrame.GetComponent<Outline>();
            if (originalFrameVisual.hasFrameOutline && frameOutline != null)
            {
                frameOutline.effectColor = originalFrameVisual.frameOutlineColor;
                frameOutline.effectDistance = originalFrameVisual.frameOutlineDistance;
            }

            if (screenInner != null)
            {
                Image innerImage = screenInner.GetComponent<Image>();
                if (originalFrameVisual.hasInnerImage && innerImage != null)
                    innerImage.color = originalFrameVisual.innerColor;

                Outline innerOutline = screenInner.GetComponent<Outline>();
                if (originalFrameVisual.hasInnerOutline && innerOutline != null)
                {
                    innerOutline.effectColor = originalFrameVisual.innerOutlineColor;
                    innerOutline.effectDistance = originalFrameVisual.innerOutlineDistance;
                }
            }
        }
        frameVisualCaptured = false;

        foreach (KeyValuePair<GameObject, bool> pair in rewardAccentStates)
        {
            if (pair.Key != null)
                pair.Key.SetActive(pair.Value);
        }
        rewardAccentStates.Clear();

        foreach (KeyValuePair<Behaviour, bool> pair in clippingStates)
        {
            if (pair.Key != null)
                pair.Key.enabled = pair.Value;
        }
        clippingStates.Clear();
    }

    private static float ReadFloat(object target, string fieldName, float fallback)
    {
        if (target == null)
            return fallback;
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        if (field == null || field.FieldType != typeof(float))
            return fallback;
        object value = field.GetValue(target);
        return value is float result ? result : fallback;
    }

    private static void WriteFloat(object target, string fieldName, float value)
    {
        if (target == null)
            return;
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        if (field != null && field.FieldType == typeof(float))
            field.SetValue(target, value);
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject go = new("BattleStageMapEventSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }
}

/// <summary>
/// 실제 Node는 작게 유지하고 Pointer Target만 넓게 잡습니다.
/// 평상시에는 절제된 Node, Hover 순간에만 강한 Accent를 사용합니다.
/// </summary>
internal sealed class BattleStageMapNodePointerFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
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

public static class BattleStageMapPurposefulUIAutoInstaller
{
#if UNITY_EDITOR
    private static bool installQueued;

    [InitializeOnLoadMethod]
    private static void InitializeEditorInstaller()
    {
        EditorApplication.hierarchyChanged -= QueueInstall;
        EditorApplication.hierarchyChanged += QueueInstall;
        QueueInstall();
    }

    private static void QueueInstall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || installQueued)
            return;
        installQueued = true;
        EditorApplication.delayCall += EnsureEditorComponents;
    }

    private static void EnsureEditorComponents()
    {
        installQueued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null || EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded)
                continue;

            if (manager.GetComponent<BattleStageMapPurposefulUIController>() != null)
                continue;

            Undo.AddComponent<BattleStageMapPurposefulUIController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponents()
    {
        BattleSceneManager[] managers = Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleStageMapPurposefulUIController>() == null)
                manager.gameObject.AddComponent<BattleStageMapPurposefulUIController>();
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward(아이템 선택) / SelectingNode(맵 선택)가 서로 다른 TV 프레임을 교체하지 않도록
/// 런타임 UI를 하나의 고정된 TV Screen Frame + 교체 가능한 Content View 구조로 정리합니다.
///
/// 기존 BattleHUD가 만들어 둔 두 화면을 삭제/재생성하지 않고 다음과 같이 병합합니다.
/// PrizeSelectionScreen (공용 프레임)
///   ScreenInner (공용 유리/배경)
///     ShowContentViewport
///       RewardSelectionContent
///       MapSelectionContent
///
/// MapSelectionScreen은 내용(MapSelectionContent)을 넘긴 뒤 비활성화합니다.
/// BattleShowWorldSetController가 구형 두 화면을 계속 토글하더라도 이 컴포넌트가 더 늦은 순서에서
/// 공용 프레임 정책을 최종 적용합니다.
///
/// Reward <-> Map 전환에서는 프레임/Carrier/TV GameObject를 움직이지 않고 Content만 바꾸며,
/// 짧은 정전 + 아날로그 노이즈 + Sync Bar + 미세한 수평 Jitter로 TV 쇼 전환을 만듭니다.
/// </summary>
[DefaultExecutionOrder(32350)]
[DisallowMultipleComponent]
public sealed class BattleShowSharedTvContentController : MonoBehaviour
{
    private enum ContentMode
    {
        None,
        Reward,
        Map
    }

    private const string SharedFrameName = "PrizeSelectionScreen";
    private const string LegacyMapFrameName = "MapSelectionScreen";
    private const string MapContentName = "MapSelectionContent";
    private const string ScreenInnerName = "ScreenInner";
    private const string MountedTvName = "BattleShowMountedTV";
    private const string ViewportName = "ShowContentViewport";
    private const string RewardViewName = "RewardSelectionContent";
    private const string TransitionRootName = "TvContentTransitionOverlay";

    [Header("TV Content Transition")]
    [SerializeField, Min(0.03f)] private float switchOutDuration = 0.10f;
    [SerializeField, Min(0f)] private float staticHoldDuration = 0.025f;
    [SerializeField, Min(0.03f)] private float switchInDuration = 0.15f;
    [SerializeField, Range(0f, 1f)] private float staticPeakAlpha = 0.92f;
    [SerializeField, Range(0f, 12f)] private float horizontalJitterPixels = 3.5f;
    [SerializeField, Range(0f, 0.12f)] private float verticalSquash = 0.035f;
    [SerializeField, Range(2f, 32f)] private float syncBarHeight = 14f;
    [SerializeField, Range(0f, 1f)] private float syncBarPeakAlpha = 0.72f;

    [Header("Analog Burst")]
    [SerializeField, Range(0f, 0.8f)] private float transitionNoise = 0.42f;
    [SerializeField, Range(0f, 0.35f)] private float transitionScanline = 0.16f;
    [SerializeField, Range(0f, 0.5f)] private float transitionRollingBand = 0.22f;
    [SerializeField] private Color transitionTint = new(0.70f, 0.78f, 0.82f, 1f);

    private BattleRunManager runManager;

    private RectTransform sharedFrame;
    private RectTransform legacyMapFrame;
    private RectTransform mapContent;
    private RectTransform screenInner;
    private RectTransform viewport;
    private RectTransform rewardView;
    private CanvasGroup viewportGroup;

    private RectTransform transitionRoot;
    private CanvasGroup transitionGroup;
    private RawImage transitionStatic;
    private RawImage transitionDarken;
    private RawImage syncBar;
    private RectTransform syncBarRect;
    private Material transitionMaterial;

    private Coroutine transitionRoutine;
    private ContentMode currentMode;
    private bool bound;
    private Vector2 viewportBasePosition;
    private Vector3 viewportBaseScale = Vector3.one;

    private void OnEnable()
    {
        bound = false;
        currentMode = ContentMode.None;
    }

    private void OnDisable()
    {
        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
            transitionRoutine = null;
        }

        ResetTransitionVisuals();
    }

    private void OnDestroy()
    {
        if (transitionMaterial != null)
            Destroy(transitionMaterial);
    }

    private void Update()
    {
        ResolveSystems();

        if (!bound)
        {
            if (!TryBindSharedScreen())
                return;

            currentMode = ResolveDesiredMode();
            ApplyModeImmediate(currentMode);
        }

        ContentMode desired = ResolveDesiredMode();
        if (transitionRoutine != null || desired == currentMode)
            return;

        if (IsContentSwitch(currentMode, desired))
        {
            transitionRoutine = StartCoroutine(PlayTvContentTransition(desired));
            return;
        }

        currentMode = desired;
        ApplyModeImmediate(currentMode);
    }

    private void LateUpdate()
    {
        if (!bound)
            return;

        // BattleShowWorldSetController(20000)와 BattleHUD가 구형 두 Frame을 다시 토글해도
        // 렌더 직전에 공용 TV 정책이 최종 상태가 되도록 보정합니다.
        if (legacyMapFrame != null && legacyMapFrame.gameObject.activeSelf)
            legacyMapFrame.gameObject.SetActive(false);

        if (transitionRoutine != null)
            return;

        ContentMode desired = ResolveDesiredMode();
        currentMode = desired;
        ApplyModeImmediate(desired);
    }

    private void ResolveSystems()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
    }

    private ContentMode ResolveDesiredMode()
    {
        if (runManager == null || !runManager.RunActive)
            return ContentMode.None;

        return runManager.State switch
        {
            BattleRunState.Reward => ContentMode.Reward,
            BattleRunState.SelectingNode => ContentMode.Map,
            _ => ContentMode.None
        };
    }

    private static bool IsContentSwitch(ContentMode from, ContentMode to)
    {
        return (from == ContentMode.Reward && to == ContentMode.Map) ||
               (from == ContentMode.Map && to == ContentMode.Reward);
    }

    private bool TryBindSharedScreen()
    {
        sharedFrame = FindRect(SharedFrameName);
        legacyMapFrame = FindRect(LegacyMapFrameName);
        mapContent = FindRect(MapContentName);

        if (sharedFrame == null || legacyMapFrame == null || mapContent == null)
            return false;

        // WorldSet이 두 화면을 실제 Mounted TV에 옮긴 뒤 병합합니다.
        // 너무 일찍 병합하면 WorldSet의 BindWhenReady가 MapSelectionScreen을 찾지 못할 수 있습니다.
        Transform tvParent = sharedFrame.parent;
        if (tvParent == null || tvParent.name != MountedTvName || legacyMapFrame.parent != tvParent)
            return false;

        screenInner = sharedFrame.Find(ScreenInnerName) as RectTransform;
        if (screenInner == null)
            return false;

        BuildSharedViewport();
        BuildTransitionOverlay();

        legacyMapFrame.gameObject.SetActive(false);
        bound = viewport != null && rewardView != null && mapContent != null;
        return bound;
    }

    private void BuildSharedViewport()
    {
        Transform existingViewport = screenInner.Find(ViewportName);
        if (existingViewport is RectTransform existingRect)
        {
            viewport = existingRect;
            rewardView = viewport.Find(RewardViewName) as RectTransform;
            viewportGroup = viewport.GetComponent<CanvasGroup>() ?? viewport.gameObject.AddComponent<CanvasGroup>();

            if (mapContent.parent != viewport)
                mapContent.SetParent(viewport, false);

            viewportBasePosition = viewport.anchoredPosition;
            viewportBaseScale = viewport.localScale;
            return;
        }

        List<Transform> rewardChildren = new();
        for (int i = 0; i < screenInner.childCount; i++)
            rewardChildren.Add(screenInner.GetChild(i));

        GameObject viewportObject = new(ViewportName);
        viewportObject.transform.SetParent(screenInner, false);
        viewport = viewportObject.AddComponent<RectTransform>();
        Stretch(viewport);
        viewportGroup = viewportObject.AddComponent<CanvasGroup>();

        GameObject rewardObject = new(RewardViewName);
        rewardObject.transform.SetParent(viewport, false);
        rewardView = rewardObject.AddComponent<RectTransform>();
        Stretch(rewardView);

        // 기존 Reward ScreenInner의 모든 실제 내용만 새 Reward View로 이동합니다.
        // ScreenInner 자신의 Image/Outline은 그대로 남아 Reward/Map 공용 TV 프레임이 됩니다.
        for (int i = 0; i < rewardChildren.Count; i++)
        {
            Transform child = rewardChildren[i];
            if (child != null && child != viewport)
                child.SetParent(rewardView, false);
        }

        mapContent.SetParent(viewport, false);
        mapContent.SetAsLastSibling();

        viewportBasePosition = viewport.anchoredPosition;
        viewportBaseScale = viewport.localScale;
    }

    private void BuildTransitionOverlay()
    {
        Transform existing = screenInner.Find(TransitionRootName);
        if (existing is RectTransform existingRect)
        {
            transitionRoot = existingRect;
            transitionGroup = existing.GetComponent<CanvasGroup>();
            transitionStatic = existing.Find("AnalogStatic")?.GetComponent<RawImage>();
            transitionDarken = existing.Find("DarkCut")?.GetComponent<RawImage>();
            syncBar = existing.Find("SyncBar")?.GetComponent<RawImage>();
            syncBarRect = syncBar != null ? syncBar.rectTransform : null;
            ResetTransitionVisuals();
            return;
        }

        GameObject root = new(TransitionRootName);
        root.transform.SetParent(screenInner, false);
        transitionRoot = root.AddComponent<RectTransform>();
        Stretch(transitionRoot);
        transitionGroup = root.AddComponent<CanvasGroup>();
        transitionGroup.alpha = 0f;
        transitionGroup.interactable = false;
        transitionGroup.blocksRaycasts = false;

        GameObject dark = new("DarkCut");
        dark.transform.SetParent(transitionRoot, false);
        RectTransform darkRect = dark.AddComponent<RectTransform>();
        Stretch(darkRect);
        transitionDarken = dark.AddComponent<RawImage>();
        transitionDarken.texture = Texture2D.whiteTexture;
        transitionDarken.color = new Color(0.005f, 0.008f, 0.012f, 0.44f);
        transitionDarken.raycastTarget = false;

        GameObject staticObject = new("AnalogStatic");
        staticObject.transform.SetParent(transitionRoot, false);
        RectTransform staticRect = staticObject.AddComponent<RectTransform>();
        Stretch(staticRect);
        transitionStatic = staticObject.AddComponent<RawImage>();
        transitionStatic.texture = Texture2D.whiteTexture;
        transitionStatic.raycastTarget = false;

        Shader analogShader = Shader.Find("UI/BattleAnalogTvOverlay");
        if (analogShader != null)
        {
            transitionMaterial = new Material(analogShader)
            {
                name = "BattleTvContentTransition_Runtime"
            };
            transitionMaterial.SetFloat("_Strength", 1f);
            transitionMaterial.SetFloat("_ScanlineSpacing", 2f);
            transitionMaterial.SetFloat("_ScanlineStrength", transitionScanline);
            transitionMaterial.SetFloat("_NoiseStrength", transitionNoise);
            transitionMaterial.SetFloat("_RollingBandStrength", transitionRollingBand);
            transitionMaterial.SetColor("_OverlayTint", transitionTint);
            transitionStatic.material = transitionMaterial;
        }
        else
        {
            transitionStatic.color = new Color(transitionTint.r, transitionTint.g, transitionTint.b, 0.30f);
        }

        GameObject barObject = new("SyncBar");
        barObject.transform.SetParent(transitionRoot, false);
        syncBarRect = barObject.AddComponent<RectTransform>();
        syncBarRect.anchorMin = new Vector2(0f, 0.5f);
        syncBarRect.anchorMax = new Vector2(1f, 0.5f);
        syncBarRect.pivot = new Vector2(0.5f, 0.5f);
        syncBarRect.offsetMin = new Vector2(0f, -syncBarHeight * 0.5f);
        syncBarRect.offsetMax = new Vector2(0f, syncBarHeight * 0.5f);

        syncBar = barObject.AddComponent<RawImage>();
        syncBar.texture = Texture2D.whiteTexture;
        syncBar.color = new Color(0.86f, 0.92f, 1f, 0f);
        syncBar.raycastTarget = false;

        transitionRoot.SetAsLastSibling();
        ResetTransitionVisuals();
    }

    private IEnumerator PlayTvContentTransition(ContentMode next)
    {
        if (sharedFrame != null && !sharedFrame.gameObject.activeSelf)
            sharedFrame.gameObject.SetActive(true);
        if (legacyMapFrame != null)
            legacyMapFrame.gameObject.SetActive(false);

        if (viewportGroup == null || transitionGroup == null)
        {
            currentMode = next;
            ApplyModeImmediate(next);
            transitionRoutine = null;
            yield break;
        }

        viewportGroup.interactable = false;
        viewportGroup.blocksRaycasts = false;
        transitionRoot.gameObject.SetActive(true);

        float outDuration = Mathf.Max(0.03f, switchOutDuration);
        float elapsed = 0f;
        while (elapsed < outDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / outDuration);
            ApplyTransitionPhase(t * 0.5f);
            yield return null;
        }

        ApplyTransitionPhase(0.5f);
        currentMode = next;
        ApplyContentRoots(next);

        float hold = Mathf.Max(0f, staticHoldDuration);
        if (hold > 0f)
        {
            float holdElapsed = 0f;
            while (holdElapsed < hold)
            {
                holdElapsed += Time.unscaledDeltaTime;
                ApplyTransitionPhase(0.5f);
                yield return null;
            }
        }

        float inDuration = Mathf.Max(0.03f, switchInDuration);
        elapsed = 0f;
        while (elapsed < inDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / inDuration);
            ApplyTransitionPhase(0.5f + t * 0.5f);
            yield return null;
        }

        ResetTransitionVisuals();
        viewportGroup.interactable = true;
        viewportGroup.blocksRaycasts = true;
        transitionRoutine = null;

        // 전환 도중 State가 또 바뀐 경우 다음 Update에서 최신 상태를 다시 처리합니다.
    }

    private void ApplyTransitionPhase(float progress)
    {
        progress = Mathf.Clamp01(progress);
        float peak = Mathf.Sin(progress * Mathf.PI);
        float easedPeak = peak * peak * (3f - 2f * peak);

        if (viewportGroup != null)
            viewportGroup.alpha = Mathf.Lerp(1f, 0.055f, easedPeak);

        if (viewport != null)
        {
            float jitter = Mathf.Sin(progress * Mathf.PI * 39f) * horizontalJitterPixels * easedPeak;
            viewport.anchoredPosition = viewportBasePosition + new Vector2(jitter, 0f);
            viewport.localScale = new Vector3(
                viewportBaseScale.x * (1f + 0.010f * easedPeak),
                viewportBaseScale.y * (1f - verticalSquash * easedPeak),
                viewportBaseScale.z);
        }

        if (transitionGroup != null)
            transitionGroup.alpha = Mathf.Clamp01(staticPeakAlpha * easedPeak);

        if (transitionMaterial != null)
        {
            transitionMaterial.SetFloat("_Strength", Mathf.Lerp(0.65f, 1.35f, easedPeak));
            transitionMaterial.SetFloat("_NoiseStrength", transitionNoise * Mathf.Lerp(0.55f, 1f, easedPeak));
            transitionMaterial.SetFloat("_ScanlineStrength", transitionScanline * Mathf.Lerp(0.70f, 1f, easedPeak));
            transitionMaterial.SetFloat("_RollingBandStrength", transitionRollingBand * Mathf.Lerp(0.55f, 1f, easedPeak));
            transitionMaterial.SetColor("_OverlayTint", transitionTint);
        }

        if (syncBarRect != null && screenInner != null)
        {
            float halfHeight = Mathf.Max(1f, screenInner.rect.height * 0.5f);
            syncBarRect.anchoredPosition = new Vector2(0f, Mathf.Lerp(halfHeight, -halfHeight, progress));
            syncBarRect.sizeDelta = new Vector2(syncBarRect.sizeDelta.x, syncBarHeight);
        }

        if (syncBar != null)
        {
            Color color = syncBar.color;
            color.a = syncBarPeakAlpha * easedPeak;
            syncBar.color = color;
        }
    }

    private void ResetTransitionVisuals()
    {
        if (viewportGroup != null)
        {
            viewportGroup.alpha = 1f;
            bool show = currentMode != ContentMode.None;
            viewportGroup.interactable = show;
            viewportGroup.blocksRaycasts = show;
        }

        if (viewport != null)
        {
            viewport.anchoredPosition = viewportBasePosition;
            viewport.localScale = viewportBaseScale;
        }

        if (transitionGroup != null)
            transitionGroup.alpha = 0f;

        if (syncBar != null)
        {
            Color color = syncBar.color;
            color.a = 0f;
            syncBar.color = color;
        }

        if (transitionRoot != null)
            transitionRoot.gameObject.SetActive(false);
    }

    private void ApplyModeImmediate(ContentMode mode)
    {
        if (sharedFrame == null || rewardView == null || mapContent == null)
            return;

        if (mode != ContentMode.None && !sharedFrame.gameObject.activeSelf)
            sharedFrame.gameObject.SetActive(true);

        if (legacyMapFrame != null && legacyMapFrame.gameObject.activeSelf)
            legacyMapFrame.gameObject.SetActive(false);

        ApplyContentRoots(mode);

        if (viewportGroup != null)
        {
            bool interactive = mode != ContentMode.None;
            viewportGroup.alpha = 1f;
            viewportGroup.interactable = interactive;
            viewportGroup.blocksRaycasts = interactive;
        }
    }

    private void ApplyContentRoots(ContentMode mode)
    {
        if (rewardView != null)
        {
            bool showReward = mode == ContentMode.Reward;
            if (rewardView.gameObject.activeSelf != showReward)
                rewardView.gameObject.SetActive(showReward);
        }

        if (mapContent != null)
        {
            bool showMap = mode == ContentMode.Map;
            if (mapContent.gameObject.activeSelf != showMap)
                mapContent.gameObject.SetActive(showMap);
        }
    }

    private static RectTransform FindRect(string targetName)
    {
        RectTransform[] rects = FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect != null && rect.name == targetName)
                return rect;
        }

        return null;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchoredPosition = Vector2.zero;
    }
}

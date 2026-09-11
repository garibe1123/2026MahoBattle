using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Reward / Map Show 화면의 TV 크기, 카메라 framing, 선택 화면 레이아웃만 담당합니다.
///
/// Phase 4부터 PACK / Full Grid / Detail / DONE / TRASH의 RectTransform은
/// BattleUnifiedInventoryInspectController가 단독 소유합니다.
/// 이 클래스는 Inventory RectTransform을 전혀 수정하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33480)]
public sealed class BattleSelectionLayoutPolicyController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("Show Screen")]
    [SerializeField] private Vector2 expandedTvCanvasSize = new(1500f, 780f);
    [SerializeField, Min(32f)] private float expandedTvPixelsPerUnit = 150f;
    [SerializeField, Range(0.65f, 0.96f)] private float showWidthCoverage = 0.88f;
    [SerializeField, Range(0.55f, 0.94f)] private float showHeightCoverage = 0.78f;
    [SerializeField, Min(0.5f)] private float minimumShowCameraSize = 2.8f;
    [SerializeField] private Vector2 rewardCameraBiasWorld = new(0.18f, 0.08f);
    [SerializeField] private Vector2 mapCameraBiasWorld = new(0f, 0.02f);

    [Header("Reward Choice Layout")]
    [SerializeField] private Vector2 rewardCardSize = new(360f, 350f);
    [SerializeField, Min(0f)] private float rewardCardGap = 42f;
    [SerializeField, Range(1f, 1.4f)] private float mapContentScale = 1.14f;

    private BattleRunManager runManager;
    private BattleRewardFlow rewardFlow;
    private BattleShowWorldSetController showWorldSet;

    private RectTransform tvRect;
    private RectTransform rewardScreen;
    private RectTransform mapScreen;
    private RectTransform rewardInner;
    private RectTransform mapInner;
    private RectTransform prizeChoices;
    private RectTransform descriptionBar;
    private RectTransform placementNotice;
    private RectTransform mapContent;

    private FieldInfo tvCanvasSizeField;
    private FieldInfo tvPixelsPerUnitField;
    private FieldInfo tvRectField;
    private FieldInfo tvBaseScaleField;
    private FieldInfo cameraTargetField;
    private FieldInfo cameraSizeField;
    private MethodInfo resolveTvMountLocalPositionMethod;
    private MethodInfo computeSharedCameraFrameMethod;

    private float nextResolveTime;
    private float nextCameraRecomputeTime;

    private void Awake()
    {
        ResolveReferences();
        CacheReflection();
        ResolveUi();
        ApplyWorldSetConfiguration(true);
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheReflection();
        ResolveUi();
        nextResolveTime = 0f;
        nextCameraRecomputeTime = 0f;
    }

    private void Update()
    {
        ResolveReferences();
        CacheReflection();
        rewardFlow?.RefreshFromRunState();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveUi();
        }

        ApplyWorldSetConfiguration(false);
        ApplyTightShowCamera();

        if (IsRewardChoicePhase())
            DisableLegacyRewardHoverMotion();
    }

    private void LateUpdate()
    {
        ResolveUi();

        if (IsRewardChoicePhase())
            ApplyRewardChoiceLayout();

        if (IsMapPhase())
            ApplyMapLayout();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (rewardFlow == null)
            rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        if (showWorldSet == null)
            showWorldSet = FindFirstObjectByType<BattleShowWorldSetController>();
    }

    private void CacheReflection()
    {
        if (showWorldSet == null)
            return;

        System.Type type = typeof(BattleShowWorldSetController);
        tvCanvasSizeField ??= type.GetField("tvCanvasSize", PrivateInstance);
        tvPixelsPerUnitField ??= type.GetField("tvPixelsPerUnit", PrivateInstance);
        tvRectField ??= type.GetField("tvRect", PrivateInstance);
        tvBaseScaleField ??= type.GetField("tvBaseScale", PrivateInstance);
        cameraTargetField ??= type.GetField("cameraTargetWorld", PrivateInstance);
        cameraSizeField ??= type.GetField("cameraSizeWorld", PrivateInstance);
        resolveTvMountLocalPositionMethod ??= type.GetMethod("ResolveTvMountLocalPosition", PrivateInstance);
        computeSharedCameraFrameMethod ??= type.GetMethod("ComputeSharedCameraFrame", PrivateInstance);
    }

    private void ResolveUi()
    {
        if (tvRect == null)
        {
            if (showWorldSet != null && tvRectField != null)
                tvRect = tvRectField.GetValue(showWorldSet) as RectTransform;
            if (tvRect == null)
                tvRect = FindRect("BattleShowMountedTV");
        }

        if (rewardScreen == null)
            rewardScreen = FindRect("PrizeSelectionScreen");
        if (mapScreen == null)
            mapScreen = FindRect("MapSelectionScreen");

        rewardInner = ResolveInner(rewardScreen, rewardInner);
        mapInner = ResolveInner(mapScreen, mapInner);

        if (prizeChoices == null && rewardInner != null)
            prizeChoices = rewardInner.Find("PrizeChoices") as RectTransform;
        if (descriptionBar == null && rewardInner != null)
            descriptionBar = rewardInner.Find("RewardActiveDescriptionBar") as RectTransform;
        if (placementNotice == null && rewardInner != null)
            placementNotice = rewardInner.Find("PlacementNotice") as RectTransform;
        if (mapContent == null)
            mapContent = FindRect("MapSelectionContent");
    }

    private static RectTransform ResolveInner(RectTransform screen, RectTransform cached)
    {
        if (cached != null)
            return cached;
        return screen != null ? screen.Find("ScreenInner") as RectTransform : null;
    }

    private void ApplyWorldSetConfiguration(bool forceCameraRecompute)
    {
        if (showWorldSet == null)
            return;

        Vector2 targetSize = new(
            Mathf.Max(960f, expandedTvCanvasSize.x),
            Mathf.Max(480f, expandedTvCanvasSize.y));
        float targetPpu = Mathf.Max(32f, expandedTvPixelsPerUnit);

        bool changed = false;

        if (tvCanvasSizeField != null)
        {
            object raw = tvCanvasSizeField.GetValue(showWorldSet);
            if (raw is not Vector2 current || (current - targetSize).sqrMagnitude > 0.01f)
            {
                tvCanvasSizeField.SetValue(showWorldSet, targetSize);
                changed = true;
            }
        }

        if (tvPixelsPerUnitField != null)
        {
            object raw = tvPixelsPerUnitField.GetValue(showWorldSet);
            float current = raw is float value ? value : 0f;
            if (!Mathf.Approximately(current, targetPpu))
            {
                tvPixelsPerUnitField.SetValue(showWorldSet, targetPpu);
                changed = true;
            }
        }

        if (tvRect == null && tvRectField != null)
            tvRect = tvRectField.GetValue(showWorldSet) as RectTransform;

        Vector3 targetBaseScale = new(1f / targetPpu, 1f / targetPpu, 1f);
        if (tvBaseScaleField != null)
            tvBaseScaleField.SetValue(showWorldSet, targetBaseScale);

        if (tvRect != null)
        {
            tvRect.sizeDelta = targetSize;
            tvRect.localScale = targetBaseScale;

            if (tvRect.parent != null && tvRect.parent.name.StartsWith("ShowScreenCarrier_"))
            {
                object result = resolveTvMountLocalPositionMethod?.Invoke(showWorldSet, null);
                if (result is Vector3 localPosition)
                    tvRect.localPosition = localPosition;
            }
        }

        ApplyScreenSize(rewardScreen, rewardInner, targetSize);
        ApplyScreenSize(mapScreen, mapInner, targetSize);

        if ((changed || forceCameraRecompute || Time.unscaledTime >= nextCameraRecomputeTime) &&
            computeSharedCameraFrameMethod != null)
        {
            nextCameraRecomputeTime = Time.unscaledTime + 0.20f;
            computeSharedCameraFrameMethod.Invoke(showWorldSet, null);
        }
    }

    private static void ApplyScreenSize(RectTransform screen, RectTransform inner, Vector2 size)
    {
        if (screen != null)
        {
            screen.anchorMin = screen.anchorMax = new Vector2(0.5f, 0.5f);
            screen.pivot = new Vector2(0.5f, 0.5f);
            screen.sizeDelta = size;
            screen.anchoredPosition = Vector2.zero;
        }

        if (inner != null)
        {
            inner.anchorMin = inner.anchorMax = new Vector2(0.5f, 0.5f);
            inner.pivot = new Vector2(0.5f, 0.5f);
            inner.sizeDelta = size - new Vector2(34f, 34f);
            inner.anchoredPosition = Vector2.zero;
        }
    }

    private void ApplyTightShowCamera()
    {
        if (showWorldSet == null || runManager == null || !runManager.RunActive ||
            cameraTargetField == null || cameraSizeField == null)
            return;

        RectTransform activeScreen = null;
        Vector2 bias = Vector2.zero;

        if (runManager.State == BattleRunState.Reward)
        {
            activeScreen = rewardScreen;
            bias = rewardCameraBiasWorld;
        }
        else if (runManager.State == BattleRunState.SelectingNode)
        {
            activeScreen = mapScreen;
            bias = mapCameraBiasWorld;
        }

        if (activeScreen == null || !activeScreen.gameObject.activeInHierarchy)
            return;

        Camera camera = Camera.main;
        if (camera == null)
            return;

        Vector3[] corners = new Vector3[4];
        activeScreen.GetWorldCorners(corners);

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        for (int i = 0; i < corners.Length; i++)
        {
            minX = Mathf.Min(minX, corners[i].x);
            maxX = Mathf.Max(maxX, corners[i].x);
            minY = Mathf.Min(minY, corners[i].y);
            maxY = Mathf.Max(maxY, corners[i].y);
        }

        float width = Mathf.Max(0.1f, maxX - minX);
        float height = Mathf.Max(0.1f, maxY - minY);
        float aspect = Mathf.Max(0.1f, camera.aspect);
        float widthCoverage = Mathf.Clamp(showWidthCoverage, 0.65f, 0.96f);
        float heightCoverage = Mathf.Clamp(showHeightCoverage, 0.55f, 0.94f);

        float sizeByWidth = width / (2f * aspect * widthCoverage);
        float sizeByHeight = height / (2f * heightCoverage);
        float cameraSize = Mathf.Max(minimumShowCameraSize, Mathf.Max(sizeByWidth, sizeByHeight));

        Vector3 center = new(
            (minX + maxX) * 0.5f + bias.x,
            (minY + maxY) * 0.5f + bias.y,
            0f);

        cameraTargetField.SetValue(showWorldSet, center);
        cameraSizeField.SetValue(showWorldSet, cameraSize);
    }

    private bool IsRewardChoicePhase()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Reward &&
               (rewardFlow == null || rewardFlow.Phase == BattleRewardPhase.Choosing);
    }

    private bool IsMapPhase()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.SelectingNode;
    }

    private void DisableLegacyRewardHoverMotion()
    {
        if (prizeChoices == null)
            return;

        RewardCardHover[] legacyHovers = prizeChoices.GetComponentsInChildren<RewardCardHover>(true);
        for (int i = 0; i < legacyHovers.Length; i++)
        {
            RewardCardHover hover = legacyHovers[i];
            if (hover != null && hover.enabled)
                hover.enabled = false;
        }
    }

    private void ApplyRewardChoiceLayout()
    {
        if (rewardScreen == null || rewardInner == null)
            return;

        rewardScreen.localRotation = Quaternion.identity;
        ApplyScreenSize(rewardScreen, rewardInner, expandedTvCanvasSize);

        if (prizeChoices != null)
        {
            prizeChoices.anchorMin = new Vector2(0.055f, 0.295f);
            prizeChoices.anchorMax = new Vector2(0.945f, 0.825f);
            prizeChoices.offsetMin = Vector2.zero;
            prizeChoices.offsetMax = Vector2.zero;

            int count = prizeChoices.childCount;
            if (count > 0)
            {
                RectTransform[] stableCards = new RectTransform[count];

                for (int childIndex = 0; childIndex < count; childIndex++)
                {
                    RectTransform card = prizeChoices.GetChild(childIndex) as RectTransform;
                    if (card == null)
                        continue;

                    RewardCardHover legacyHover = card.GetComponent<RewardCardHover>();
                    if (legacyHover != null && legacyHover.enabled)
                        legacyHover.enabled = false;

                    RewardPrizeDrag drag = card.GetComponent<RewardPrizeDrag>();
                    int stableIndex = drag != null
                        ? Mathf.Clamp(drag.RewardIndex, 0, count - 1)
                        : childIndex;

                    if (stableCards[stableIndex] == null)
                        stableCards[stableIndex] = card;
                }

                float usableWidth = expandedTvCanvasSize.x * 0.82f;
                float gap = Mathf.Max(0f, rewardCardGap);
                float width = Mathf.Min(
                    rewardCardSize.x,
                    Mathf.Max(220f, (usableWidth - gap * Mathf.Max(0, count - 1)) / count));
                float total = count * width + Mathf.Max(0, count - 1) * gap;
                float start = -total * 0.5f + width * 0.5f;

                for (int i = 0; i < count; i++)
                {
                    RectTransform card = stableCards[i];
                    if (card == null)
                        continue;

                    if (card.GetSiblingIndex() != i)
                        card.SetSiblingIndex(i);

                    card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
                    card.pivot = new Vector2(0.5f, 0.5f);
                    card.sizeDelta = new Vector2(width, rewardCardSize.y);
                    card.anchoredPosition = new Vector2(start + i * (width + gap), 0f);
                    card.localScale = Vector3.one;

                    RectTransform icon = card.Find("PrizeIcon") as RectTransform;
                    if (icon != null)
                        icon.sizeDelta = new Vector2(150f, 150f);

                    Text[] texts = card.GetComponentsInChildren<Text>(true);
                    for (int t = 0; t < texts.Length; t++)
                    {
                        Text text = texts[t];
                        if (text == null)
                            continue;

                        string value = text.text ?? string.Empty;
                        if (value.Contains("CLICK") || value.Contains("DRAG"))
                            text.fontSize = 10;
                        else if (text.fontSize >= 13)
                            text.fontSize = 18;
                        else if (text.fontSize >= 8)
                            text.fontSize = Mathf.Max(text.fontSize, 10);
                    }
                }
            }
        }

        if (descriptionBar != null)
        {
            descriptionBar.anchorMin = new Vector2(0.055f, 0.105f);
            descriptionBar.anchorMax = new Vector2(0.945f, 0.265f);
            descriptionBar.offsetMin = Vector2.zero;
            descriptionBar.offsetMax = Vector2.zero;
        }

        if (placementNotice != null)
        {
            placementNotice.anchorMin = placementNotice.anchorMax = new Vector2(0.5f, 0.055f);
            placementNotice.pivot = new Vector2(0.5f, 0.5f);
            placementNotice.sizeDelta = new Vector2(expandedTvCanvasSize.x * 0.86f, 58f);
            placementNotice.anchoredPosition = Vector2.zero;
        }

        Text[] screenTexts = rewardInner.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < screenTexts.Length; i++)
        {
            Text text = screenTexts[i];
            if (text != null && (text.text ?? string.Empty).Contains("CHOOSE YOUR PRIZE"))
                text.fontSize = 38;
        }
    }

    private void ApplyMapLayout()
    {
        if (mapScreen == null || mapInner == null)
            return;

        ApplyScreenSize(mapScreen, mapInner, expandedTvCanvasSize);
        mapScreen.localRotation = Quaternion.identity;

        if (mapContent != null)
        {
            mapContent.anchorMin = Vector2.zero;
            mapContent.anchorMax = Vector2.one;
            mapContent.offsetMin = Vector2.zero;
            mapContent.offsetMax = Vector2.zero;
            mapContent.pivot = new Vector2(0.5f, 0.5f);
            mapContent.localScale = Vector3.one * mapContentScale;
        }
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = UnityEngine.Object.FindObjectsByType<RectTransform>(
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
}

public static class BattleSelectionLayoutPolicyAutoInstaller
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
        EditorApplication.delayCall += EnsureEditorComponent;
    }

    private static void EnsureEditorComponent()
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

            if (manager.GetComponent<BattleSelectionLayoutPolicyController>() != null)
                continue;

            Undo.AddComponent<BattleSelectionLayoutPolicyController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponent()
    {
        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleSelectionLayoutPolicyController>() == null)
                manager.gameObject.AddComponent<BattleSelectionLayoutPolicyController>();
        }
    }
}

using UnityEngine;

/// <summary>
/// Reward / Map Show의 TV 크기와 공용 카메라 framing만 담당합니다.
///
/// Phase 5 ownership:
/// - Reward 카드 / 결정 / 포기 / Selection Locked: BattleRewardCardActionController
/// - Mini PACK / Full Grid / Detail / DONE / TRASH: BattleUnifiedInventoryInspectController
/// - 이 클래스: World TV size, Reward/Map camera frame, Map content scale
///
/// Reward/Inventory UI RectTransform을 직접 수정하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33480)]
public sealed class BattleSelectionLayoutPolicyController : MonoBehaviour
{
    [Header("Show Screen")]
    [SerializeField] private Vector2 expandedTvCanvasSize = new(1500f, 780f);
    [SerializeField, Min(32f)] private float expandedTvPixelsPerUnit = 150f;
    [SerializeField, Range(0.65f, 0.96f)] private float showWidthCoverage = 0.88f;
    [SerializeField, Range(0.55f, 0.94f)] private float showHeightCoverage = 0.78f;
    [SerializeField, Min(0.5f)] private float minimumShowCameraSize = 2.8f;
    [SerializeField] private Vector2 rewardCameraBiasWorld = new(0.18f, -0.72f);
    [SerializeField] private Vector2 mapCameraBiasWorld = new(0f, 0.02f);

    [Header("Map")]
    [SerializeField, Range(1f, 1.4f)] private float mapContentScale = 1.14f;

    private BattleRunManager runManager;
    private BattleShowWorldSetController showWorldSet;

    private RectTransform tvRect;
    private RectTransform rewardScreen;
    private RectTransform mapScreen;
    private RectTransform rewardInner;
    private RectTransform mapInner;
    private RectTransform mapContent;

    private float nextResolveTime;
    private float nextCameraRecomputeTime;
    private bool tvConfigured;
    private Vector2 configuredTvSize;
    private float configuredTvPixelsPerUnit = -1f;

    public Vector2 RewardCameraBiasWorld => rewardCameraBiasWorld;

    private void Awake()
    {
        ResolveReferences();
        ResolveUi();
        ApplyWorldSetConfiguration(true);
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveUi();
        nextResolveTime = 0f;
        nextCameraRecomputeTime = 0f;
        tvConfigured = false;
    }

    private void Update()
    {
        ResolveReferences();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.10f;
            ResolveUi();
        }

        ApplyWorldSetConfiguration(false);
        ApplyTightShowCamera();
    }

    private void LateUpdate()
    {
        if (IsMapPhase())
            ApplyMapLayout();
    }

    public void SetRewardCameraBiasY(float y)
    {
        if (rewardCameraBiasWorld.y > y)
            rewardCameraBiasWorld = new Vector2(rewardCameraBiasWorld.x, y);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (showWorldSet == null)
            showWorldSet = FindFirstObjectByType<BattleShowWorldSetController>();
    }

    private void ResolveUi()
    {
        if (tvRect == null && showWorldSet != null)
            tvRect = showWorldSet.MountedTvRect;
        if (tvRect == null)
            tvRect = FindRect("BattleShowMountedTV");

        if (rewardScreen == null)
            rewardScreen = FindRect("PrizeSelectionScreen");
        if (mapScreen == null)
            mapScreen = FindRect("MapSelectionScreen");

        rewardInner = ResolveInner(rewardScreen, rewardInner);
        mapInner = ResolveInner(mapScreen, mapInner);

        if (mapContent == null)
            mapContent = FindRect("MapSelectionContent");
    }

    private static RectTransform ResolveInner(RectTransform screen, RectTransform cached)
    {
        if (cached != null && cached.parent == screen)
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

        bool configurationChanged = !tvConfigured ||
                                    (configuredTvSize - targetSize).sqrMagnitude > 0.01f ||
                                    !Mathf.Approximately(configuredTvPixelsPerUnit, targetPpu);

        if (configurationChanged)
        {
            configuredTvSize = targetSize;
            configuredTvPixelsPerUnit = targetPpu;
            tvConfigured = true;
            showWorldSet.ConfigureTvPresentation(targetSize, targetPpu);
            tvRect = showWorldSet.MountedTvRect;
        }

        ApplyScreenSize(rewardScreen, rewardInner, targetSize);
        ApplyScreenSize(mapScreen, mapInner, targetSize);

        if (forceCameraRecompute || Time.unscaledTime >= nextCameraRecomputeTime)
        {
            nextCameraRecomputeTime = Time.unscaledTime + 0.20f;
            showWorldSet.RecomputeSharedCameraFrame();
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
            screen.localRotation = Quaternion.identity;
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
        if (showWorldSet == null || runManager == null || !runManager.RunActive)
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
        float sizeByWidth = width / (2f * aspect * Mathf.Clamp(showWidthCoverage, 0.65f, 0.96f));
        float sizeByHeight = height / (2f * Mathf.Clamp(showHeightCoverage, 0.55f, 0.94f));
        float cameraSize = Mathf.Max(minimumShowCameraSize, Mathf.Max(sizeByWidth, sizeByHeight));

        Vector3 center = new(
            (minX + maxX) * 0.5f + bias.x,
            (minY + maxY) * 0.5f + bias.y,
            0f);

        showWorldSet.OverrideShowCameraFrame(center, cameraSize);
    }

    private bool IsMapPhase()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.SelectingNode;
    }

    private void ApplyMapLayout()
    {
        if (mapScreen == null || mapInner == null)
            return;

        ApplyScreenSize(mapScreen, mapInner, expandedTvCanvasSize);
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

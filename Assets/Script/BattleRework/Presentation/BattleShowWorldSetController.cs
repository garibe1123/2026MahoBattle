using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward / Map 공용 월드 쇼 세트.
///
/// 공통 화면 유닛:
/// - Persistent 4x4의 왼쪽 끝과 10x2 Screen Carrier의 왼쪽 끝을 정확히 맞춥니다.
/// - Screen Carrier가 위쪽 레일에서 내려와 4x4 상단에 도킹합니다.
/// - TV의 아래 Edge를 10x2 Carrier의 두 타일 행 사이 중앙선에 맞추고, TV RectTransform 자체도 하단 중앙 Pivot을 사용합니다.
/// - TV는 Floor/Carrier보다 항상 앞 Sorting Order에 배치합니다.
/// - TV는 Screen Carrier의 자식이므로 Reward/Map 모두 같은 물리 유닛을 사용합니다.
/// - Reward -> Map에서는 Screen Carrier/TV를 유지하고 내용만 Map으로 바꿉니다.
///
/// Reward 전용 유닛:
/// - Presenter 6x4가 Base 오른쪽에서 도킹합니다.
/// - Presenter SpriteRenderer는 Presenter 6x4의 자식으로 함께 움직입니다.
/// - Presenter Sprite는 Carrier의 오른쪽 Edge에 맞춰 정렬합니다.
/// - 실제 Presenter Sprite가 비어 있으면 BattleHudSpriteCache.DefaultSprite를 표시합니다.
///
/// 카메라:
/// - Reward는 Persistent 4x4 위의 월드 상품 Showcase를 기준으로 잡고, Hover Item으로 Smooth Zoom합니다.
/// - Map은 기존 Mounted TV 기준 Framing / Cursor Tracking을 그대로 사용합니다.
/// - Presenter 유닛은 카메라 기준 Bounds에는 개입하지 않습니다.
/// </summary>
[DefaultExecutionOrder(20000)]
[DisallowMultipleComponent]
public sealed class BattleShowWorldSetController : MonoBehaviour
{
    private enum ShowMode { None, Reward, Map }

    private sealed class RewardShowcaseItem
    {
        public int index;
        public BattleEquipmentSO equipment;
        public GameObject root;
        public SpriteRenderer baseRenderer;
        public SpriteRenderer itemRenderer;
        public BattleCharacterLightVisual spotlight;
    }

    private const int ScreenCarrierWidth = 10;
    private const int ScreenCarrierDepth = 2;
    private const int PresenterCarrierWidth = 6;
    private const int PresenterCarrierDepth = 4;
    // World-space TV must always render above battle field / carrier / decor sprites.
    // BattleWorldSorting actor range tops out at 10000, so this leaves a large protected gap.
    private const int ProtectedShowCanvasOrder = 20000;

    private static BattleShowWorldSetController instance;

    [Header("TV")]
    [SerializeField] private Vector2 tvCanvasSize = new(1120f, 560f);
    [SerializeField, Min(32f)] private float tvPixelsPerUnit = 122f;
    [Tooltip("10x2 Screen Carrier의 두 타일 행 사이 중앙선을 기준으로 TV 아래 Edge를 미세 조정할 값입니다.")]
    [SerializeField, Range(-1f, 1f)] private float tvBottomAnchorYOffset = 0f;

    [Header("Dock Units")]
    [SerializeField, Min(0.05f)] private float carrierEntryDuration = 0.62f;
    [SerializeField, Min(2f)] private float carrierRailDistance = 12f;
    [SerializeField, Min(0f)] private float presenterEntryDelay = 0.16f;
    [SerializeField, Range(0f, 2f)] private float carrierImpactStrength = 1.05f;
    [SerializeField] private int carrierFloorSortingOrder = -18;

    [Header("Reward Camera Focus")]
    [Tooltip("Reward 기본 진입 시 상품 Base 전체가 들어오도록 주는 여백입니다.")]
    [SerializeField, Min(0f)] private float rewardCameraPadding = 0.38f;
    [Tooltip("Reward 상품 전체를 보여줄 때의 최소 Orthographic Size입니다.")]
    [SerializeField, Min(0.1f)] private float rewardCameraMinSize = 2.55f;

    [Header("Reward Item Showcase")]
    [Tooltip("상품 Base 중심 간 World 간격입니다. 기본 1이면 Floor 한 칸 간격입니다.")]
    [SerializeField, Min(0.5f)] private float rewardShowcaseSpacingWorld = 1.15f;
    [Tooltip("Persistent 4x4 중심에서 상품 진열 행을 위/아래로 이동합니다.")]
    [SerializeField] private float rewardShowcaseRowYOffsetWorld = -0.15f;
    [Tooltip("아이템 Hover 시 사용할 Orthographic Size입니다. BattleCamera Show Min Zoom보다 작으면 Camera 쪽 최소값에서 제한됩니다.")]
    [SerializeField, Min(0.5f)] private float rewardItemHoverCameraSize = 2.20f;
    [Tooltip("월드 좌표 Hover 판정 시 1칸 Base 바깥으로 추가하는 여유입니다.")]
    [SerializeField, Range(0f, 0.5f)] private float rewardHoverBoundsPaddingWorld = 0.10f;

    [Header("Reward Item Spotlight")]
    [SerializeField] private Color rewardSpotlightColor = new(1f, 0.96f, 0.78f, 1f);
    [SerializeField, Range(0f, 1f)] private float rewardSpotlightPoolAlpha = 0.34f;
    [SerializeField, Range(0f, 1f)] private float rewardSpotlightBeamAlpha = 0.58f;
    [SerializeField, Min(0.2f)] private float rewardSpotlightWidth = 1.8f;
    [SerializeField, Min(0.2f)] private float rewardSpotlightHeight = 2.4f;
    [SerializeField, Min(0.1f)] private float rewardSpotlightFadeSharpness = 10f;

    [Header("Map Camera Focus")]
    [Tooltip("Map 선택에서는 Persistent Base/Carrier 전체가 아니라 TV 화면을 주 피사체로 잡습니다.")]
    [SerializeField, Min(0f)] private float mapCameraPadding = 0.22f;
    [Tooltip("16:9 기준 1120x560 TV가 화면 대부분을 채우되 가장자리가 잘리지 않는 최소 Orthographic Size입니다.")]
    [SerializeField, Min(0.1f)] private float mapCameraMinSize = 2.85f;

    [Header("Presenter")]
    [SerializeField] private Sprite presenterFallbackSprite;
    [SerializeField, Min(0.5f)] private float presenterWorldHeight = 3.6f;
    [SerializeField] private float presenterPadYOffset = 0.20f;
    [SerializeField, Range(0f, 1f)] private float presenterRightPadding = 0.15f;
    [SerializeField, Min(1)] private int presenterFrontOrder = 20;

    private BattleRunManager runManager;
    private BattleHUD hud;
    private PlayerController player;
    private BattleCameraController battleCamera;
    private RoomBaseTemplate baseTemplate;
    private BattleShowPresentationManager presentation;
    private BattleRewardFlow rewardFlow;

    private RectTransform rewardScreen;
    private RectTransform mapScreen;
    private RectTransform mapContent;
    private RectTransform equipmentDock;
    private RectTransform rewardLoadoutStrip;
    private GameObject legacyMapCanvas;
    private Image legacyPresenter;

    private GameObject stageRoot;
    private GameObject tvObject;
    private Canvas tvCanvas;
    private RectTransform tvRect;
    private CanvasGroup tvGroup;
    private Vector3 tvBaseScale;

    private Transform presenterTransform;
    private SpriteRenderer presenterRenderer;
    private Sprite lastPresenterSprite;

    private MapBlock screenCarrier;
    private MapBlock presenterCarrier;

    private GameObject rewardShowcaseRoot;
    private readonly List<RewardShowcaseItem> rewardShowcaseItems = new();
    private int rewardShowcaseSignature;
    private int rewardHoveredIndex = -1;
    private int rewardSelectedIndex = -1;

    private Coroutine bindRoutine;
    private Coroutine transitionRoutine;
    private ShowMode currentMode;
    private ShowMode desiredMode;

    private bool bound;
    private bool stageTransitioning;
    private bool externalGate;
    private bool hasExplicitStageAnchor;
    private bool dockCaptured;

    private Vector3 explicitStageAnchor;
    private Vector3 stageAnchorWorld;
    private Vector3 screenCarrierDestination;
    private Vector3 presenterCarrierDestination;
    private Vector3 tvMountedWorld;
    private Vector3 cameraTargetWorld;
    private float cameraSizeWorld = 6.1f;

    public bool IsShowActive => currentMode != ShowMode.None || stageTransitioning;
    public bool IsTransitioning => stageTransitioning;
    public bool IsRewardMode => currentMode == ShowMode.Reward && !stageTransitioning;
    public bool IsMapMode => currentMode == ShowMode.Map && !stageTransitioning;
    public bool HasCameraAnchor => dockCaptured && !externalGate && currentMode != ShowMode.None;
    public Vector3 CameraTargetWorld => cameraTargetWorld;
    public float ShowCameraSize => Mathf.Max(0.1f, cameraSizeWorld);
    public RectTransform MountedTvRect => tvRect;
    public bool HasRewardShowcase =>
        rewardShowcaseRoot != null &&
        rewardShowcaseRoot.activeInHierarchy &&
        rewardShowcaseItems.Count > 0;
    public int RewardHoveredIndex => rewardHoveredIndex;
    public int RewardSelectedIndex => rewardSelectedIndex;

    public Transform PresenterWorldTransform =>
        presenterRenderer != null && presenterRenderer.enabled && presenterRenderer.gameObject.activeInHierarchy
            ? presenterTransform
            : null;

    public void SetRewardShowcaseSelectedIndex(int index)
    {
        rewardSelectedIndex =
            FindRewardShowcaseItem(index) != null
                ? index
                : -1;
    }

    public bool TryGetRewardShowcaseScreenPoint(
        int index,
        out Vector2 screenPoint)
    {
        screenPoint = Vector2.zero;

        RewardShowcaseItem item =
            FindRewardShowcaseItem(index);

        Camera camera = Camera.main;
        if (item == null ||
            item.itemRenderer == null ||
            camera == null)
        {
            return false;
        }

        Vector3 world =
            item.itemRenderer.bounds.center;

        Vector3 screen =
            camera.WorldToScreenPoint(world);

        if (screen.z <= 0f)
            return false;

        screenPoint =
            new Vector2(
                screen.x,
                screen.y);

        return true;
    }

    public bool TryGetRewardShowcaseWorldPosition(
        int index,
        out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;

        RewardShowcaseItem item =
            FindRewardShowcaseItem(index);

        if (item == null ||
            item.itemRenderer == null)
        {
            return false;
        }

        worldPosition =
            item.itemRenderer.bounds.center;

        return true;
    }

    public void SetExternalGate(bool held)
    {
        externalGate = held;
        if (held)
            battleCamera?.SetShowCursorTracking(false, Vector2.zero);
    }

    public void SetStageAnchor(Vector3 fourByFourCenterWorld)
    {
        explicitStageAnchor = fourByFourCenterWorld;
        explicitStageAnchor.z = 0f;
        hasExplicitStageAnchor = true;
    }

    public void ConfigureTvPresentation(Vector2 canvasSize, float pixelsPerUnit)
    {
        Vector2 targetSize = new(
            Mathf.Max(960f, canvasSize.x),
            Mathf.Max(480f, canvasSize.y));
        float targetPpu = Mathf.Max(32f, pixelsPerUnit);

        tvCanvasSize = targetSize;
        tvPixelsPerUnit = targetPpu;
        float scale = 1f / targetPpu;
        tvBaseScale = new Vector3(scale, scale, 1f);

        if (tvRect != null)
        {
            tvRect.sizeDelta = targetSize;
            tvRect.pivot = new Vector2(0.5f, 0f);
            tvRect.localScale = tvBaseScale;
            if (tvRect.parent != null && tvRect.parent.name.StartsWith("ShowScreenCarrier_", StringComparison.Ordinal))
                tvRect.localPosition = ResolveTvMountLocalPosition();
        }

        RecomputeSharedCameraFrame();
    }

    public void RecomputeSharedCameraFrame()
    {
        if (stageRoot == null)
            return;
        ComputeSharedCameraFrame();
    }

    public void OverrideShowCameraFrame(Vector3 targetWorld, float orthographicSize)
    {
        targetWorld.z = 0f;
        cameraTargetWorld = targetWorld;
        cameraSizeWorld = Mathf.Max(0.1f, orthographicSize);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            return;
        }

        instance = this;
    }

    private void OnEnable()
    {
        if (bindRoutine == null)
            bindRoutine = StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
            StopCoroutine(bindRoutine);
        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);

        bindRoutine = null;
        transitionRoutine = null;
        battleCamera?.SetShowCursorTracking(false, Vector2.zero);
        KillCarrierTweens();
        if (tvRect != null)
        {
            tvRect.DOKill();
            tvRect.localScale = tvBaseScale;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private IEnumerator BindWhenReady()
    {
        while (enabled)
        {
            ResolveSystems();
            ResolveUi();

            if (runManager != null && hud != null && rewardScreen != null && mapScreen != null && mapContent != null)
            {
                BuildStage();
                ReparentScreens();
                DisableLegacyOnce();
                bound = true;
                bindRoutine = null;
                yield break;
            }

            yield return null;
        }

        bindRoutine = null;
    }

    private void ResolveSystems()
    {
        if (runManager == null) runManager = FindFirstObjectByType<BattleRunManager>();
        if (hud == null) hud = FindFirstObjectByType<BattleHUD>();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (battleCamera == null) battleCamera = FindFirstObjectByType<BattleCameraController>();
        if (baseTemplate == null) baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (rewardFlow == null) rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        if (presentation == null)
        {
            presentation = BattleShowPresentationManager.Instance != null
                ? BattleShowPresentationManager.Instance
                : FindFirstObjectByType<BattleShowPresentationManager>();
        }
    }

    private void ResolveUi()
    {
        if (rewardScreen == null) rewardScreen = FindRect("PrizeSelectionScreen");
        if (mapScreen == null) mapScreen = FindRect("MapSelectionScreen");
        if (mapContent == null) mapContent = FindRect("MapSelectionContent");
        if (equipmentDock == null) equipmentDock = FindRect("EquipmentDock");
        if (rewardLoadoutStrip == null) rewardLoadoutStrip = FindRect("RewardLoadoutStrip");
        if (legacyPresenter == null) legacyPresenter = FindPresenterImage();

        if (legacyMapCanvas == null)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];
                if (candidate != null && candidate.name == "BattleMapSelectionWorldCanvas")
                {
                    legacyMapCanvas = candidate.gameObject;
                    break;
                }
            }
        }
    }

    private void BuildStage()
    {
        if (stageRoot != null)
            return;

        stageRoot = new GameObject("BattleShowDockStage");
        stageRoot.transform.SetParent(transform, false);
        stageRoot.transform.position = Vector3.zero;

        tvObject = new GameObject("BattleShowMountedTV");
        tvObject.transform.SetParent(stageRoot.transform, false);

        tvCanvas = tvObject.AddComponent<Canvas>();
        tvCanvas.renderMode = RenderMode.WorldSpace;
        tvCanvas.overrideSorting = true;
        tvCanvas.worldCamera = Camera.main;
        tvObject.AddComponent<GraphicRaycaster>();

        tvGroup = tvObject.AddComponent<CanvasGroup>();
        tvGroup.interactable = false;
        tvGroup.blocksRaycasts = false;

        tvRect = tvObject.GetComponent<RectTransform>();
        tvRect.sizeDelta = tvCanvasSize;
        tvRect.pivot = new Vector2(0.5f, 0f);
        float tvScale = 1f / Mathf.Max(32f, tvPixelsPerUnit);
        tvBaseScale = new Vector3(tvScale, tvScale, 1f);
        tvRect.localScale = tvBaseScale;
        tvObject.SetActive(false);

        GameObject presenterObject = new("PresenterWorldSprite");
        presenterObject.transform.SetParent(stageRoot.transform, false);
        presenterTransform = presenterObject.transform;
        presenterRenderer = presenterObject.AddComponent<SpriteRenderer>();
        presenterRenderer.color = Color.white;
        presenterRenderer.enabled = false;
        presenterObject.SetActive(false);

        stageRoot.SetActive(false);
    }

    private void ReparentScreens()
    {
        ReparentToTv(rewardScreen);
        ReparentToTv(mapScreen);

        if (legacyMapCanvas != null)
            legacyMapCanvas.SetActive(false);
        if (rewardLoadoutStrip != null)
            rewardLoadoutStrip.gameObject.SetActive(false);
    }

    private void ReparentToTv(RectTransform rect)
    {
        if (rect == null || tvRect == null)
            return;

        rect.SetParent(tvRect, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = tvCanvasSize;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private void DisableLegacyOnce()
    {
        DisableImage(FindImage("FieldBroadcastFilter"));
        DisableImage(FindImage("PlayerFloorSpotlight"));
        DisableImage(FindImage("PresenterFloorSpotlight"));
        DisableImage(legacyPresenter);
        if (rewardLoadoutStrip != null)
            rewardLoadoutStrip.gameObject.SetActive(false);
    }

    private static void DisableImage(Image image)
    {
        if (image == null)
            return;
        image.raycastTarget = false;
        image.enabled = false;
    }

    private void Update()
    {
        if (!bound)
        {
            if (bindRoutine == null)
                bindRoutine = StartCoroutine(BindWhenReady());
            return;
        }

        ResolveSystems();
        ResolveUi();
        desiredMode = ResolveDesiredMode();
        UpdatePresenter();
        UpdateRewardShowcase();
        UpdatePointerTracking();

        if (transitionRoutine == null && desiredMode != currentMode)
            transitionRoutine = StartCoroutine(Transition());
    }

    private void LateUpdate()
    {
        if (!bound)
            return;

        DisableImage(legacyPresenter);
        if (legacyMapCanvas != null && legacyMapCanvas.activeSelf)
            legacyMapCanvas.SetActive(false);
        if (rewardLoadoutStrip != null && rewardLoadoutStrip.gameObject.activeSelf)
            rewardLoadoutStrip.gameObject.SetActive(false);
        if (tvCanvas != null && tvCanvas.worldCamera != Camera.main)
            tvCanvas.worldCamera = Camera.main;

        if (tvRect != null)
            tvRect.localScale = tvBaseScale;

        HideScreenCarrierTopHandle();
        UpdateSorting();
        MaintainEquipmentDock();
    }

    private ShowMode ResolveDesiredMode()
    {
        if (externalGate || runManager == null || !runManager.RunActive)
            return ShowMode.None;
        if (runManager.State == BattleRunState.Reward)
            return ShowMode.Reward;
        if (runManager.State == BattleRunState.SelectingNode)
            return ShowMode.Map;
        return ShowMode.None;
    }

    private IEnumerator Transition()
    {
        stageTransitioning = true;
        SetInteraction(false);

        while (currentMode != desiredMode)
        {
            ShowMode next = desiredMode;

            if (currentMode == ShowMode.Reward && next == ShowMode.Map)
            {
                yield return RewardToMap();
                continue;
            }

            if (currentMode == ShowMode.Map && next == ShowMode.Reward)
            {
                yield return MapToReward();
                continue;
            }

            if (currentMode != ShowMode.None && next == ShowMode.None)
            {
                yield return ExitCurrentStage();
                currentMode = ShowMode.None;
                SetContent(ShowMode.None);
                dockCaptured = false;
                continue;
            }

            if (currentMode == ShowMode.None && next != ShowMode.None)
            {
                CaptureStageDock();
                stageRoot.SetActive(true);
                dockCaptured = true;

                yield return EnterScreenCarrier(next);
                currentMode = next;
                SetContent(currentMode);
                continue;
            }

            currentMode = next;
            SetContent(currentMode);
        }

        stageTransitioning = false;
        SetInteraction(currentMode != ShowMode.None);
        transitionRoutine = null;
    }

    private IEnumerator EnterScreenCarrier(ShowMode mode)
    {
        ClearAllCarriers(false);

        screenCarrier = CreateCarrier(
            "ShowScreenCarrier_10x2",
            ScreenCarrierWidth,
            ScreenCarrierDepth,
            screenCarrierDestination);
        MountTvToScreenCarrier();
        HideScreenCarrierTopHandle();

        if (mode == ShowMode.Reward)
        {
            presenterCarrier = CreateCarrier(
                "PresenterCarrier_6x4",
                PresenterCarrierWidth,
                PresenterCarrierDepth,
                presenterCarrierDestination);
            AttachPresenterToCarrier();
            presentation?.PlayPresenterAnimation(true);
            UpdatePresenter();
        }

        SetContent(mode);

        if (mode == ShowMode.Reward)
            EnsureRewardShowcase(forceRebuild: true);
        else
            DestroyRewardShowcase();

        ComputeSharedCameraFrame();

        float screenDuration = 0f;
        float presenterDuration = 0f;

        if (screenCarrier != null)
        {
            screenCarrier.PlayEnter(screenCarrierDestination, Vector2.up, 0f);
            screenDuration = screenCarrier.GetEntryDuration(0f);
        }

        if (presenterCarrier != null)
        {
            float delay = Mathf.Max(0f, presenterEntryDelay);
            presenterCarrier.PlayEnter(presenterCarrierDestination, Vector2.right, delay);
            presenterDuration = presenterCarrier.GetEntryDuration(delay);
        }

        BattleDockHandleVisibilityController.RefreshNow();
        HideScreenCarrierTopHandle();

        float wait = Mathf.Max(screenDuration, presenterDuration);
        if (wait > 0f)
            yield return new WaitForSecondsRealtime(wait + 0.04f);

        BattleDockHandleVisibilityController.RefreshNow();
        HideScreenCarrierTopHandle();
    }

    private IEnumerator RewardToMap()
    {
        SetInteraction(false);

        float presenterDuration = 0f;
        if (presenterCarrier != null)
        {
            presenterCarrier.PlayExit(Vector2.right);
            presenterDuration = presenterCarrier.ExitDuration;
        }

        if (presenterRenderer != null)
            presenterRenderer.enabled = false;

        if (presenterDuration > 0f)
            yield return new WaitForSecondsRealtime(presenterDuration + 0.03f);

        DestroyPresenterCarrier();
        DestroyRewardShowcase();
        currentMode = ShowMode.Map;
        SetContent(ShowMode.Map);
        ComputeSharedCameraFrame();
        BattleDockHandleVisibilityController.RefreshNow();
        HideScreenCarrierTopHandle();
    }

    private IEnumerator MapToReward()
    {
        SetInteraction(false);

        if (screenCarrier == null)
        {
            yield return EnterScreenCarrier(ShowMode.Reward);
            currentMode = ShowMode.Reward;
            SetContent(ShowMode.Reward);
            yield break;
        }

        presenterCarrier = CreateCarrier(
            "PresenterCarrier_6x4",
            PresenterCarrierWidth,
            PresenterCarrierDepth,
            presenterCarrierDestination);
        AttachPresenterToCarrier();
        presentation?.PlayPresenterAnimation(true);
        UpdatePresenter();

        currentMode = ShowMode.Reward;
        SetContent(ShowMode.Reward);
        EnsureRewardShowcase(forceRebuild: true);

        if (presenterCarrier != null)
        {
            presenterCarrier.PlayEnter(presenterCarrierDestination, Vector2.right, 0f);
            float duration = presenterCarrier.GetEntryDuration(0f);
            if (duration > 0f)
                yield return new WaitForSecondsRealtime(duration + 0.03f);
        }

        ComputeSharedCameraFrame();
        BattleDockHandleVisibilityController.RefreshNow();
        HideScreenCarrierTopHandle();
    }

    private IEnumerator ExitCurrentStage()
    {
        battleCamera?.SetShowCursorTracking(false, Vector2.zero);
        SetInteraction(false);

        float screenDuration = 0f;
        float presenterDuration = 0f;

        if (screenCarrier != null)
        {
            screenCarrier.PlayExit(Vector2.up);
            screenDuration = screenCarrier.ExitDuration;
        }

        if (presenterCarrier != null)
        {
            presenterCarrier.PlayExit(Vector2.right);
            presenterDuration = presenterCarrier.ExitDuration;
        }

        float wait = Mathf.Max(screenDuration, presenterDuration);
        if (wait > 0f)
            yield return new WaitForSecondsRealtime(wait + 0.03f);

        if (tvObject != null)
            tvObject.SetActive(false);

        DestroyRewardShowcase();
        ClearAllCarriers(false);
        if (stageRoot != null)
            stageRoot.SetActive(false);
    }

    private void CaptureStageDock()
    {
        ResolveSystems();

        if (hasExplicitStageAnchor)
            stageAnchorWorld = explicitStageAnchor;
        else if (baseTemplate != null && baseTemplate.HasPersistentBase)
            stageAnchorWorld = baseTemplate.FixedCenterWorld;
        else
            stageAnchorWorld = player != null ? player.transform.position : Vector3.zero;

        stageAnchorWorld.z = 0f;

        float baseHalfTileSpan = (RoomBaseTemplate.FixedBaseTiles - 1) * 0.5f;
        float baseLeftTileCenterX = stageAnchorWorld.x - baseHalfTileSpan;
        float baseBottomTileCenterY = stageAnchorWorld.y - baseHalfTileSpan;
        float baseTopTileCenterY = stageAnchorWorld.y + baseHalfTileSpan;

        screenCarrierDestination = new Vector3(
            baseLeftTileCenterX,
            baseTopTileCenterY + 1f,
            0f);

        presenterCarrierDestination = new Vector3(
            stageAnchorWorld.x + baseHalfTileSpan + 1f,
            baseBottomTileCenterY,
            0f);

        tvMountedWorld = ResolveMountedTvWorld();
        ComputeSharedCameraFrame();
    }

    private MapBlock CreateCarrier(string objectName, int width, int height, Vector3 destination)
    {
        GameObject root = new(objectName);
        root.transform.SetParent(stageRoot.transform, true);
        root.SetActive(false);

        GameObject visualObject = new("Visual");
        visualObject.transform.SetParent(root.transform, false);
        Transform visual = visualObject.transform;

        MapBlock block = root.AddComponent<MapBlock>();
        block.ConfigureRuntimeDockingBlock(
            visual,
            false,
            carrierImpactStrength,
            carrierEntryDuration,
            carrierRailDistance);

        SpriteRenderer playerRenderer = player != null ? player.GetComponentInChildren<SpriteRenderer>(true) : null;
        int sortingLayerId = playerRenderer != null ? playerRenderer.sortingLayerID : 0;
        bool createdAnyTile = false;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Sprite sprite = presentation != null ? presentation.GetRandomFloorSprite() : null;
                if (sprite == null)
                    continue;

                GameObject tile = new($"ShowTile_{x}_{y}");
                tile.transform.SetParent(visual, false);
                tile.transform.localPosition = new Vector3(x, y, 0f);

                SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingLayerID = sortingLayerId;
                renderer.sortingOrder = carrierFloorSortingOrder;
                renderer.color = presentation != null && presentation.ActiveFloorTemplate != null
                    ? presentation.ActiveFloorTemplate.FloorTint
                    : Color.white;
                createdAnyTile = true;
            }
        }

        if (!createdAnyTile)
        {
            Debug.LogWarning(
                $"[BattleShowWorldSetController] '{objectName}'을 만들 Floor Variant가 없습니다. " +
                "BattleShowPresentationManager Default Floor Template을 확인하세요.",
                this);
        }

        block.SnapTo(destination);
        root.SetActive(true);
        presentation?.ApplySlidingTemplate(block, Vector2.zero, true);
        return block;
    }

    private void MountTvToScreenCarrier()
    {
        if (tvRect == null || tvObject == null || screenCarrier == null)
            return;

        tvRect.DOKill();
        tvRect.SetParent(screenCarrier.transform, false);
        tvRect.localRotation = Quaternion.identity;
        tvRect.pivot = new Vector2(0.5f, 0f);
        tvRect.localScale = tvBaseScale;
        tvRect.localPosition = ResolveTvMountLocalPosition();
        tvObject.SetActive(true);
        tvMountedWorld = ResolveMountedTvWorld();
    }

    private Vector3 ResolveTvMountLocalPosition()
    {
        float carrierMidlineY = (ScreenCarrierDepth - 1) * 0.5f;
        float tvBottomY = carrierMidlineY + tvBottomAnchorYOffset;

        return new Vector3(
            (ScreenCarrierWidth - 1) * 0.5f,
            tvBottomY,
            0f);
    }

    private Vector3 ResolveMountedTvWorld()
    {
        Vector3 bottomLocal = ResolveTvMountLocalPosition();
        float tvWorldHeight = tvCanvasSize.y / Mathf.Max(32f, tvPixelsPerUnit);
        return screenCarrierDestination + bottomLocal + Vector3.up * (tvWorldHeight * 0.5f);
    }

    private void AttachPresenterToCarrier()
    {
        if (presenterTransform == null || presenterCarrier == null)
            return;

        presenterTransform.SetParent(presenterCarrier.transform, false);
        presenterTransform.localPosition = new Vector3(
            (PresenterCarrierWidth - 1) * 0.5f,
            (PresenterCarrierDepth - 1) * 0.5f + presenterPadYOffset,
            0f);
        presenterTransform.localRotation = Quaternion.identity;
        presenterTransform.gameObject.SetActive(true);
        UpdatePresenter();
    }

    private void AlignPresenterToCarrierRight(Sprite sprite)
    {
        if (presenterTransform == null || presenterCarrier == null || sprite == null)
            return;

        float xScale = presenterTransform.localScale.x;
        float visualRightOffset = xScale >= 0f
            ? sprite.bounds.max.x * xScale
            : sprite.bounds.min.x * xScale;

        float carrierRightEdge = PresenterCarrierWidth - 0.5f;
        float x = carrierRightEdge - Mathf.Max(0f, presenterRightPadding) - visualRightOffset;
        float y = (PresenterCarrierDepth - 1) * 0.5f + presenterPadYOffset;
        presenterTransform.localPosition = new Vector3(x, y, 0f);
    }

    private void HideScreenCarrierTopHandle()
    {
        if (screenCarrier == null)
            return;

        Transform visual = screenCarrier.transform.Find("Visual");
        if (visual == null)
            return;

        Transform template = visual.Find("PresentationTemplate");
        if (template == null)
            return;

        Transform upper = template.Find("DockHandle_Upper");
        if (upper != null && upper.gameObject.activeSelf)
            upper.gameObject.SetActive(false);
    }

    private void DestroyPresenterCarrier()
    {
        if (presenterTransform != null && stageRoot != null)
        {
            presenterTransform.SetParent(stageRoot.transform, true);
            presenterTransform.gameObject.SetActive(false);
        }

        DestroyCarrier(ref presenterCarrier);
        if (presenterRenderer != null)
            presenterRenderer.enabled = false;
    }

    private void ClearAllCarriers(bool keepTvVisible)
    {
        if (tvRect != null && stageRoot != null && tvRect.parent != stageRoot.transform)
            tvRect.SetParent(stageRoot.transform, true);

        if (!keepTvVisible && tvObject != null)
            tvObject.SetActive(false);

        DestroyPresenterCarrier();
        DestroyCarrier(ref screenCarrier);
    }

    private static void DestroyCarrier(ref MapBlock block)
    {
        if (block == null)
            return;

        block.transform.DOKill();
        UnityEngine.Object.Destroy(block.gameObject);
        block = null;
    }

    private void KillCarrierTweens()
    {
        if (screenCarrier != null)
            screenCarrier.transform.DOKill();
        if (presenterCarrier != null)
            presenterCarrier.transform.DOKill();
    }

    private void UpdateRewardShowcase()
    {
        bool rewardActive =
            (currentMode == ShowMode.Reward ||
             desiredMode == ShowMode.Reward) &&
            runManager != null &&
            runManager.RunActive &&
            runManager.State == BattleRunState.Reward;

        if (!rewardActive)
        {
            if (rewardShowcaseRoot != null)
                DestroyRewardShowcase();

            return;
        }

        // Persistent 4x4 중심 좌표를 Capture하기 전에는 상품을 만들지 않습니다.
        // Reward State가 Show Transition보다 한 프레임 먼저 바뀌어도 (0,0)에 잠깐 생성되지 않습니다.
        if (!dockCaptured ||
            stageRoot == null ||
            !stageRoot.activeInHierarchy)
        {
            return;
        }

        EnsureRewardShowcase(
            forceRebuild: false);

        bool choosing =
            rewardFlow == null ||
            rewardFlow.Phase == BattleRewardPhase.Choosing;

        int nextHover =
            choosing &&
            !stageTransitioning &&
            !externalGate
                ? ResolveRewardShowcaseHover()
                : -1;

        if (nextHover != rewardHoveredIndex)
        {
            rewardHoveredIndex =
                nextHover;

            ApplyRewardShowcaseFocus();
        }

        for (int i = 0;
             i < rewardShowcaseItems.Count;
             i++)
        {
            RewardShowcaseItem item =
                rewardShowcaseItems[i];

            if (item?.spotlight == null)
                continue;

            bool hovered =
                choosing &&
                item.index == rewardHoveredIndex;

            item.spotlight.SetTarget(
                hovered,
                hovered ? 1f : 0f);
        }
    }

    private void EnsureRewardShowcase(
        bool forceRebuild)
    {
        if (stageRoot == null ||
            runManager == null)
        {
            return;
        }

        IReadOnlyList<BattleEquipmentSO> choices =
            runManager.CurrentRewardChoices;

        int signature =
            ComputeRewardShowcaseSignature(
                choices);

        if (!forceRebuild &&
            rewardShowcaseRoot != null &&
            signature == rewardShowcaseSignature)
        {
            if (!rewardShowcaseRoot.activeSelf)
                rewardShowcaseRoot.SetActive(true);

            return;
        }

        DestroyRewardShowcase();

        if (choices == null ||
            choices.Count <= 0)
        {
            return;
        }

        rewardShowcaseSignature =
            signature;

        rewardShowcaseRoot =
            new GameObject(
                "RewardItemShowcase");

        rewardShowcaseRoot.transform.SetParent(
            stageRoot.transform,
            true);

        float spacing =
            Mathf.Max(
                0.5f,
                rewardShowcaseSpacingWorld);

        float startX =
            stageAnchorWorld.x -
            (choices.Count - 1) *
            spacing *
            0.5f;

        float rowY =
            stageAnchorWorld.y +
            rewardShowcaseRowYOffsetWorld;

        float floorPpu =
            presentation != null
                ? presentation.GetFloorPixelsPerUnit()
                : 32f;

        Vector2 floorTileSize =
            presentation != null
                ? presentation.GetFloorTileWorldSize()
                : Vector2.one;

        SpriteRenderer playerRenderer =
            player != null
                ? player.GetComponentInChildren<SpriteRenderer>(true)
                : null;

        int sortingLayerId =
            playerRenderer != null
                ? playerRenderer.sortingLayerID
                : 0;

        for (int i = 0;
             i < choices.Count;
             i++)
        {
            BattleEquipmentSO equipment =
                choices[i];

            if (equipment == null)
                continue;

            Vector3 baseWorld =
                new(
                    startX +
                    i * spacing,
                    rowY,
                    0f);

            GameObject itemRoot =
                new(
                    $"RewardShowcase_{i}_{equipment.GetDisplayName()}");

            itemRoot.transform.SetParent(
                rewardShowcaseRoot.transform,
                true);

            itemRoot.transform.position =
                baseWorld;

            GameObject baseObject =
                new("RewardBase");

            baseObject.transform.SetParent(
                itemRoot.transform,
                false);

            SpriteRenderer baseRenderer =
                baseObject.AddComponent<SpriteRenderer>();

            Sprite baseSprite =
                presentation != null
                    ? presentation.GetRewardBaseSprite(
                        equipment.rarity)
                    : null;

            baseRenderer.sprite =
                baseSprite;

            baseRenderer.sortingLayerID =
                sortingLayerId;

            baseRenderer.sortingOrder =
                BattleWorldSorting.FloorOrder +
                48;

            if (baseSprite != null)
            {
                Vector2 baseWorldSize =
                    new(
                        Mathf.Max(
                            0.001f,
                            Mathf.Abs(
                                baseSprite.bounds.size.x)),
                        Mathf.Max(
                            0.001f,
                            Mathf.Abs(
                                baseSprite.bounds.size.y)));

                baseObject.transform.localScale =
                    new Vector3(
                        floorTileSize.x /
                        baseWorldSize.x,
                        floorTileSize.y /
                        baseWorldSize.y,
                        1f);
            }

            GameObject iconObject =
                new("RewardItemSprite");

            iconObject.transform.SetParent(
                itemRoot.transform,
                false);

            float pixelYOffset =
                presentation != null
                    ? presentation.RewardItemPixelYOffset
                    : -4;

            iconObject.transform.localPosition =
                new Vector3(
                    0f,
                    pixelYOffset /
                    Mathf.Max(
                        1f,
                        floorPpu),
                    0f);

            SpriteRenderer itemRenderer =
                iconObject.AddComponent<SpriteRenderer>();

            itemRenderer.sprite =
                equipment.icon;

            itemRenderer.sortingLayerID =
                sortingLayerId;

            itemRenderer.sortingOrder =
                BattleWorldSorting.WorldYToOrder(
                    baseWorld.y,
                    120 + i);

            if (equipment.icon != null)
            {
                float iconPpu =
                    Mathf.Max(
                        1f,
                        equipment.icon.pixelsPerUnit);

                float ppuScale =
                    iconPpu /
                    Mathf.Max(
                        1f,
                        floorPpu);

                iconObject.transform.localScale =
                    new Vector3(
                        ppuScale,
                        ppuScale,
                        1f);
            }

            BattleCharacterLightVisual spotlight =
                itemRoot.AddComponent<BattleCharacterLightVisual>();

            spotlight.Configure(
                itemRenderer,
                rewardSpotlightColor,
                rewardSpotlightColor,
                rewardSpotlightPoolAlpha,
                rewardSpotlightWidth,
                0.18f,
                0.045f,
                rewardSpotlightFadeSharpness);

            spotlight.ConfigureKeyLight(
                true,
                rewardSpotlightColor,
                rewardSpotlightBeamAlpha,
                rewardSpotlightWidth,
                rewardSpotlightHeight,
                0.24f);

            spotlight.SetImmediate(0f);

            rewardShowcaseItems.Add(
                new RewardShowcaseItem
                {
                    index = i,
                    equipment = equipment,
                    root = itemRoot,
                    baseRenderer = baseRenderer,
                    itemRenderer = itemRenderer,
                    spotlight = spotlight
                });
        }

        rewardHoveredIndex = -1;
        rewardSelectedIndex = -1;

        rewardShowcaseRoot.SetActive(true);

        ComputeSharedCameraFrame();
    }

    private int ResolveRewardShowcaseHover()
    {
        Camera camera =
            Camera.main;

        if (camera == null ||
            !Input.mousePresent)
        {
            return -1;
        }

        Vector3 mouse =
            Input.mousePosition;

        Vector3 world =
            camera.ScreenToWorldPoint(
                new Vector3(
                    mouse.x,
                    mouse.y,
                    Mathf.Abs(
                        camera.transform.position.z)));

        int bestIndex = -1;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0;
             i < rewardShowcaseItems.Count;
             i++)
        {
            RewardShowcaseItem item =
                rewardShowcaseItems[i];

            if (item == null ||
                item.baseRenderer == null)
            {
                continue;
            }

            Bounds bounds =
                item.baseRenderer.bounds;

            bounds.Expand(
                new Vector3(
                    rewardHoverBoundsPaddingWorld * 2f,
                    rewardHoverBoundsPaddingWorld * 2f,
                    0f));

            bool contains =
                world.x >= bounds.min.x &&
                world.x <= bounds.max.x &&
                world.y >= bounds.min.y &&
                world.y <= bounds.max.y;

            if (!contains &&
                item.itemRenderer != null &&
                item.itemRenderer.sprite != null)
            {
                Bounds itemBounds =
                    item.itemRenderer.bounds;

                contains =
                    world.x >= itemBounds.min.x &&
                    world.x <= itemBounds.max.x &&
                    world.y >= itemBounds.min.y &&
                    world.y <= itemBounds.max.y;
            }

            if (!contains)
                continue;

            Vector2 delta =
                (Vector2)world -
                (Vector2)bounds.center;

            float distance =
                delta.sqrMagnitude;

            if (distance >= bestDistance)
                continue;

            bestDistance =
                distance;

            bestIndex =
                item.index;
        }

        return bestIndex;
    }

    private void ApplyRewardShowcaseFocus()
    {
        if (rewardHoveredIndex < 0)
        {
            ComputeSharedCameraFrame();
            return;
        }

        RewardShowcaseItem item =
            FindRewardShowcaseItem(
                rewardHoveredIndex);

        if (item == null ||
            item.itemRenderer == null)
        {
            ComputeSharedCameraFrame();
            return;
        }

        Vector3 focus =
            item.itemRenderer.bounds.center;

        focus.z = 0f;

        OverrideShowCameraFrame(
            focus,
            rewardItemHoverCameraSize);
    }

    private RewardShowcaseItem FindRewardShowcaseItem(
        int index)
    {
        for (int i = 0;
             i < rewardShowcaseItems.Count;
             i++)
        {
            RewardShowcaseItem item =
                rewardShowcaseItems[i];

            if (item != null &&
                item.index == index)
            {
                return item;
            }
        }

        return null;
    }

    private bool TryGetRewardShowcaseBounds(
        out Bounds bounds)
    {
        bounds =
            default;

        bool initialized =
            false;

        for (int i = 0;
             i < rewardShowcaseItems.Count;
             i++)
        {
            RewardShowcaseItem item =
                rewardShowcaseItems[i];

            if (item == null ||
                item.baseRenderer == null)
            {
                continue;
            }

            Bounds next =
                item.baseRenderer.bounds;

            if (item.itemRenderer != null &&
                item.itemRenderer.sprite != null)
            {
                next.Encapsulate(
                    item.itemRenderer.bounds);
            }

            if (!initialized)
            {
                bounds =
                    next;

                initialized =
                    true;
            }
            else
            {
                bounds.Encapsulate(
                    next);
            }
        }

        return initialized;
    }

    private int ComputeRewardShowcaseSignature(
        IReadOnlyList<BattleEquipmentSO> choices)
    {
        unchecked
        {
            int hash = 17;

            if (choices == null)
                return hash;

            hash =
                hash * 31 +
                choices.Count;

            for (int i = 0;
                 i < choices.Count;
                 i++)
            {
                hash =
                    hash * 31 +
                    (choices[i] != null
                        ? choices[i].GetInstanceID()
                        : 0);
            }

            return hash;
        }
    }

    private void DestroyRewardShowcase()
    {
        for (int i = 0;
             i < rewardShowcaseItems.Count;
             i++)
        {
            RewardShowcaseItem item =
                rewardShowcaseItems[i];

            if (item?.spotlight != null)
                item.spotlight.SetImmediate(0f);
        }

        rewardShowcaseItems.Clear();
        rewardHoveredIndex = -1;
        rewardSelectedIndex = -1;
        rewardShowcaseSignature = 0;

        if (rewardShowcaseRoot != null)
        {
            Destroy(
                rewardShowcaseRoot);

            rewardShowcaseRoot = null;
        }
    }

    private void ComputeSharedCameraFrame()
    {
        tvMountedWorld = ResolveMountedTvWorld();
        Vector2 tvWorldSize = new(
            tvCanvasSize.x / Mathf.Max(32f, tvPixelsPerUnit),
            tvCanvasSize.y / Mathf.Max(32f, tvPixelsPerUnit));

        bool rewardFocus =
            currentMode == ShowMode.Reward ||
            desiredMode == ShowMode.Reward;
        bool mapFocus =
            currentMode == ShowMode.Map ||
            desiredMode == ShowMode.Map;
        bool tvDecisionFocus = rewardFocus || mapFocus;

        Bounds bounds;
        if (rewardFocus &&
            TryGetRewardShowcaseBounds(
                out Bounds rewardBounds))
        {
            // Reward는 TV 카드가 아니라 Persistent Base 위의 실제 상품 진열대를
            // 주 피사체로 사용합니다. Map은 기존 TV Framing을 그대로 유지합니다.
            bounds =
                rewardBounds;
        }
        else if (tvDecisionFocus)
        {
            bounds = new Bounds(
                tvMountedWorld,
                new Vector3(
                    tvWorldSize.x,
                    tvWorldSize.y,
                    0.1f));
        }
        else
        {
            bounds = new Bounds(
                new Vector3(stageAnchorWorld.x, stageAnchorWorld.y, 0f),
                new Vector3(RoomBaseTemplate.FixedBaseTiles, RoomBaseTemplate.FixedBaseTiles, 0.1f));

            Bounds screenBounds = CreateTileUnitBounds(
                screenCarrierDestination,
                ScreenCarrierWidth,
                ScreenCarrierDepth);
            bounds.Encapsulate(screenBounds);
            bounds.Encapsulate(new Bounds(
                tvMountedWorld,
                new Vector3(tvWorldSize.x, tvWorldSize.y, 0.1f)));
        }

        cameraTargetWorld = new Vector3(bounds.center.x, bounds.center.y, 0f);

        float padding = rewardFocus
            ? Mathf.Max(0f, rewardCameraPadding)
            : Mathf.Max(0f, mapCameraPadding);

        float aspect = Camera.main != null && Camera.main.aspect > 0.01f
            ? Camera.main.aspect
            : 16f / 9f;

        float sizeByHeight = bounds.extents.y + padding;
        float sizeByWidth = (bounds.extents.x + padding) / Mathf.Max(0.1f, aspect);
        float minSize = rewardFocus
            ? rewardCameraMinSize
            : mapCameraMinSize;

        cameraSizeWorld = Mathf.Max(
            Mathf.Max(0.1f, minSize),
            Mathf.Max(sizeByHeight, sizeByWidth));
    }

    private static Bounds CreateTileUnitBounds(Vector3 lowerLeftTileCenter, int width, int height)
    {
        Vector3 center = lowerLeftTileCenter + new Vector3(
            (width - 1) * 0.5f,
            (height - 1) * 0.5f,
            0f);
        return new Bounds(center, new Vector3(width, height, 0.1f));
    }

    private void SetContent(ShowMode mode)
    {
        if (rewardScreen != null)
            rewardScreen.gameObject.SetActive(mode == ShowMode.Reward);
        if (mapScreen != null)
            mapScreen.gameObject.SetActive(mode == ShowMode.Map);
        if (mapContent != null && mode == ShowMode.Map)
            mapContent.gameObject.SetActive(true);

        MaintainEquipmentDock();
    }

    private void SetInteraction(bool enabledInteraction)
    {
        if (tvGroup == null)
            return;

        bool active = enabledInteraction && currentMode != ShowMode.None && !externalGate;
        tvGroup.interactable = active;
        tvGroup.blocksRaycasts = active;
    }

    private void UpdatePointerTracking()
    {
        // Reward 상품 선택은 TV 커서 좌표가 아니라 월드 Item/Base Hover가
        // 카메라 Focus를 직접 소유합니다. Map의 기존 TV 추적은 그대로 유지합니다.
        if (currentMode == ShowMode.Reward &&
            HasRewardShowcase)
        {
            battleCamera?.SetShowCursorTracking(
                false,
                Vector2.zero);
            return;
        }

        if (battleCamera == null || tvRect == null || currentMode == ShowMode.None ||
            stageTransitioning || externalGate || tvObject == null || !tvObject.activeSelf)
        {
            battleCamera?.SetShowCursorTracking(false, Vector2.zero);
            return;
        }

        Camera camera = Camera.main;
        Vector2 local = Vector2.zero;
        bool valid = camera != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
            tvRect,
            Input.mousePosition,
            camera,
            out local);
        bool inside = valid && tvRect.rect.Contains(local);
        Vector2 normalized = Vector2.zero;

        if (inside)
        {
            Rect rect = tvRect.rect;
            float halfWidth = Mathf.Max(1f, rect.width * 0.5f);
            float halfHeight = Mathf.Max(1f, rect.height * 0.5f);
            Vector2 centered = local - rect.center;
            normalized = new Vector2(
                Mathf.Clamp(centered.x / halfWidth, -1f, 1f),
                Mathf.Clamp(centered.y / halfHeight, -1f, 1f));
        }

        battleCamera.SetShowCursorTracking(inside, normalized);
    }

    private void UpdatePresenter()
    {
        if (presenterRenderer == null || presenterTransform == null)
            return;

        Sprite sprite = ResolvePresenterSprite();
        if (sprite != lastPresenterSprite)
        {
            lastPresenterSprite = sprite;
            presenterRenderer.sprite = sprite;

            if (sprite != null)
            {
                float scale = presenterWorldHeight / Mathf.Max(0.0001f, Mathf.Abs(sprite.bounds.size.y));
                bool flip = legacyPresenter != null && legacyPresenter.rectTransform.localScale.x < 0f;
                presenterTransform.localScale = new Vector3(flip ? -scale : scale, scale, 1f);
            }
        }

        AlignPresenterToCarrierRight(sprite);

        bool rewardMode = currentMode == ShowMode.Reward ||
                          (stageTransitioning && desiredMode == ShowMode.Reward);
        bool carrierVisible = presenterCarrier != null && presenterCarrier.gameObject.activeInHierarchy;
        presenterRenderer.enabled = sprite != null && rewardMode && carrierVisible && presenterTransform.gameObject.activeInHierarchy;
    }

    private Sprite ResolvePresenterSprite()
    {
        if (legacyPresenter == null)
            legacyPresenter = FindPresenterImage();

        Sprite sprite = legacyPresenter != null ? legacyPresenter.sprite : null;
        if (sprite == BattleHudSpriteCache.DefaultSprite)
            sprite = null;

        if (sprite == null)
            sprite = presenterFallbackSprite;
        if (sprite == null)
            sprite = BattleHudSpriteCache.DefaultSprite;

        return sprite;
    }

    private void MaintainEquipmentDock()
    {
        if (equipmentDock == null)
            return;

        bool reward = currentMode == ShowMode.Reward || desiredMode == ShowMode.Reward;
        bool map = currentMode == ShowMode.Map || desiredMode == ShowMode.Map;
        if (!reward && !map)
            return;

        bool shouldShow = reward && !map;
        if (equipmentDock.gameObject.activeSelf != shouldShow)
            equipmentDock.gameObject.SetActive(shouldShow);
    }

    private void UpdateSorting()
    {
        SpriteRenderer playerRenderer = player != null ? player.GetComponentInChildren<SpriteRenderer>(true) : null;
        if (playerRenderer == null)
            return;

        int fieldOrder = GetHighestFieldOrder(playerRenderer.sortingLayerID);
        int playerOrder = playerRenderer.sortingOrder;
        int resolvedTvOrder = Mathf.Max(
            ProtectedShowCanvasOrder,
            Mathf.Max(fieldOrder + 2, playerOrder + 1));

        if (tvCanvas != null)
        {
            tvCanvas.sortingLayerID = playerRenderer.sortingLayerID;
            tvCanvas.sortingOrder = resolvedTvOrder;
        }

        if (presenterRenderer != null)
        {
            presenterRenderer.sortingLayerID = playerRenderer.sortingLayerID;
            // Presenter is a world sprite and may overlap the physical TV edge, so keep it
            // intentionally in front of the protected TV canvas while all floor/decor stays behind.
            presenterRenderer.sortingOrder = Mathf.Min(
                32000,
                resolvedTvOrder + Mathf.Max(1, presenterFrontOrder));
        }
    }

    private static int GetHighestFieldOrder(int sortingLayerId)
    {
        int highest = -1000;
        BattleWalkableField[] fields = FindObjectsByType<BattleWalkableField>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < fields.Length; i++)
        {
            BattleWalkableField field = fields[i];
            SpriteRenderer renderer = field != null ? field.GetComponent<SpriteRenderer>() : null;
            if (renderer != null && renderer.sortingLayerID == sortingLayerId)
                highest = Mathf.Max(highest, renderer.sortingOrder);
        }
        return highest;
    }

    private static RectTransform FindRect(string name)
    {
        RectTransform[] rects = FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < rects.Length; i++)
        {
            if (rects[i] != null && rects[i].name == name)
                return rects[i];
        }
        return null;
    }

    private static Image FindImage(string name)
    {
        Image[] images = FindObjectsByType<Image>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null && images[i].name == name)
                return images[i];
        }
        return null;
    }

    private static Image FindPresenterImage()
    {
        Image[] images = FindObjectsByType<Image>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null || image.name != "Presenter")
                continue;

            Transform parent = image.transform.parent;
            while (parent != null)
            {
                if (parent.name == "RewardQuizShow")
                    return image;
                parent = parent.parent;
            }
        }
        return null;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
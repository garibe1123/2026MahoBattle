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
/// - TV의 아래 Edge를 10x2 Carrier의 두 타일 행 사이 중앙선에 맞추고, 화면 본체는 그 지점에서 위로 올라갑니다.
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
/// - Persistent 4x4 + 10x2 Screen Carrier + TV의 실제 최종 Bounds를 기준으로 계산합니다.
/// - TV 자체는 커서 Hover로 확대하지 않습니다. 커서 반응은 카메라 Tracking만 사용합니다.
/// - Presenter 유닛은 카메라 기준에 개입하지 않아 Reward/Map 전환에서 카메라 기준이 바뀌지 않습니다.
/// </summary>
[DefaultExecutionOrder(20000)]
[DisallowMultipleComponent]
public sealed class BattleShowWorldSetController : MonoBehaviour
{
    private enum ShowMode { None, Reward, Map }

    private const int ScreenCarrierWidth = 10;
    private const int ScreenCarrierDepth = 2;
    private const int PresenterCarrierWidth = 6;
    private const int PresenterCarrierDepth = 4;

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

    [Header("Shared Camera From Docked Screen")]
    [SerializeField, Min(0f)] private float rewardCameraPadding = 0.85f;
    [SerializeField, Min(0.1f)] private float rewardCameraMinSize = 5.4f;

    [Header("Presenter")]
    [SerializeField] private Sprite presenterFallbackSprite;
    [SerializeField, Min(0.5f)] private float presenterWorldHeight = 3.6f;
    [SerializeField] private float presenterPadYOffset = 0.20f;
    [SerializeField, Range(0f, 1f)] private float presenterRightPadding = 0.15f;
    [SerializeField, Min(1)] private int presenterFrontOrder = 20;

    [Header("Map Start")]
    [SerializeField] private Vector2 mapStartSize = new(92f, 46f);
    [SerializeField, Min(20f)] private float mapStartGap = 108f;

    private BattleRunManager runManager;
    private BattleHUD hud;
    private PlayerController player;
    private BattleCameraController battleCamera;
    private RoomBaseTemplate baseTemplate;
    private BattleShowPresentationManager presentation;

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
    public bool HasCameraAnchor => dockCaptured && !externalGate && currentMode != ShowMode.None;
    public Vector3 CameraTargetWorld => cameraTargetWorld;
    public float ShowCameraSize => Mathf.Max(0.1f, cameraSizeWorld);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleShowWorldSetController>() != null)
            return;

        GameObject host = new("BattleShowWorldSetRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleShowWorldSetController>();
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

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
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
                PrepareCombatDropSlots();
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
        tvRect.pivot = new Vector2(0.5f, 0.5f);
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

    private void PrepareCombatDropSlots()
    {
        if (equipmentDock == null)
            return;

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            Transform slot = equipmentDock.Find($"Slot_{i + 1}");
            if (slot == null)
                continue;

            RewardInventoryDropZone zone = slot.GetComponent<RewardInventoryDropZone>();
            if (zone == null)
                zone = slot.gameObject.AddComponent<RewardInventoryDropZone>();
            zone.Configure(hud, i);
        }
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
        EnsureMapStartMarker();
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
        tvRect.localScale = tvBaseScale;
        tvRect.localPosition = ResolveTvMountLocalPosition();
        tvObject.SetActive(true);
        tvMountedWorld = screenCarrierDestination + ResolveTvMountLocalPosition();
    }

    private Vector3 ResolveTvMountLocalPosition()
    {
        float tvWorldHeight = tvCanvasSize.y / Mathf.Max(32f, tvPixelsPerUnit);
        float carrierMidlineY = (ScreenCarrierDepth - 1) * 0.5f;
        float tvBottomY = carrierMidlineY + tvBottomAnchorYOffset;

        return new Vector3(
            (ScreenCarrierWidth - 1) * 0.5f,
            tvBottomY + tvWorldHeight * 0.5f,
            0f);
    }

    private Vector3 ResolveMountedTvWorld()
    {
        return screenCarrierDestination + ResolveTvMountLocalPosition();
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

    private void ComputeSharedCameraFrame()
    {
        Bounds bounds = new(
            new Vector3(stageAnchorWorld.x, stageAnchorWorld.y, 0f),
            new Vector3(RoomBaseTemplate.FixedBaseTiles, RoomBaseTemplate.FixedBaseTiles, 0.1f));

        Bounds screenBounds = CreateTileUnitBounds(
            screenCarrierDestination,
            ScreenCarrierWidth,
            ScreenCarrierDepth);
        bounds.Encapsulate(screenBounds);

        tvMountedWorld = ResolveMountedTvWorld();
        Vector2 tvWorldSize = new(
            tvCanvasSize.x / Mathf.Max(32f, tvPixelsPerUnit),
            tvCanvasSize.y / Mathf.Max(32f, tvPixelsPerUnit));
        bounds.Encapsulate(new Bounds(
            tvMountedWorld,
            new Vector3(tvWorldSize.x, tvWorldSize.y, 0.1f)));

        cameraTargetWorld = new Vector3(bounds.center.x, bounds.center.y, 0f);

        float padding = Mathf.Max(0f, rewardCameraPadding);
        float aspect = Camera.main != null && Camera.main.aspect > 0.01f
            ? Camera.main.aspect
            : 16f / 9f;

        float sizeByHeight = bounds.extents.y + padding;
        float sizeByWidth = (bounds.extents.x + padding) / Mathf.Max(0.1f, aspect);
        cameraSizeWorld = Mathf.Max(rewardCameraMinSize, Mathf.Max(sizeByHeight, sizeByWidth));
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
            normalized = new Vector2(
                Mathf.Clamp(local.x / halfWidth, -1f, 1f),
                Mathf.Clamp(local.y / halfHeight, -1f, 1f));
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

        if (sprite == null && presentation != null)
        {
            System.Reflection.FieldInfo field = typeof(BattleShowPresentationManager).GetField(
                "presenterFrames",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Sprite[] frames = field != null ? field.GetValue(presentation) as Sprite[] : null;
            if (frames != null)
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    if (frames[i] == null)
                        continue;
                    sprite = frames[i];
                    break;
                }
            }
        }

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

        if (tvCanvas != null)
        {
            tvCanvas.sortingLayerID = playerRenderer.sortingLayerID;
            int highestFloorOrder = Mathf.Max(fieldOrder, carrierFloorSortingOrder);
            tvCanvas.sortingOrder = Mathf.Max(highestFloorOrder + 2, playerOrder + 1);
        }

        if (presenterRenderer != null)
        {
            presenterRenderer.sortingLayerID = playerRenderer.sortingLayerID;
            presenterRenderer.sortingOrder = playerOrder + Mathf.Max(1, presenterFrontOrder);
        }
    }

    private void EnsureMapStartMarker()
    {
        if (currentMode != ShowMode.Map || mapContent == null || mapContent.Find("StageStartMarker") != null)
            return;

        List<RectTransform> nodes = new();
        for (int i = 0; i < mapContent.childCount; i++)
        {
            Transform child = mapContent.GetChild(i);
            if (child is RectTransform rect && child.name.StartsWith("StageNode_", StringComparison.Ordinal))
                nodes.Add(rect);
        }
        if (nodes.Count == 0)
            return;

        float minX = float.MaxValue;
        for (int i = 0; i < nodes.Count; i++)
            minX = Mathf.Min(minX, nodes[i].anchoredPosition.x);

        List<RectTransform> first = new();
        float averageY = 0f;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (Mathf.Abs(nodes[i].anchoredPosition.x - minX) > 1.5f)
                continue;
            first.Add(nodes[i]);
            averageY += nodes[i].anchoredPosition.y;
        }
        if (first.Count == 0)
            return;

        averageY /= first.Count;
        Vector2 start = new(minX - mapStartGap, averageY);

        GameObject marker = new("StageStartMarker");
        marker.transform.SetParent(mapContent, false);
        RectTransform markerRect = marker.AddComponent<RectTransform>();
        markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerRect.sizeDelta = mapStartSize;
        markerRect.anchoredPosition = start;

        Image image = marker.AddComponent<Image>();
        image.color = new Color(0.10f, 0.78f, 0.98f, 1f);
        image.raycastTarget = false;

        GameObject label = new("Label");
        label.transform.SetParent(marker.transform, false);
        RectTransform labelRect = label.AddComponent<RectTransform>();
        Stretch(labelRect);

        Text text = label.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = "START!  ▶";
        text.fontSize = 14;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;

        Vector2 from = start + Vector2.right * (mapStartSize.x * 0.5f + 4f);
        for (int i = 0; i < first.Count; i++)
            CreateLine(mapContent, from, first[i].anchoredPosition);
    }

    private static void CreateLine(RectTransform parent, Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        if (delta.sqrMagnitude < 1f)
            return;

        GameObject line = new("StartRouteLink");
        line.transform.SetParent(parent, false);
        RectTransform rect = line.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = (from + to) * 0.5f;
        rect.sizeDelta = new Vector2(delta.magnitude, 5f);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

        Image image = line.AddComponent<Image>();
        image.color = new Color(0.16f, 0.78f, 1f, 0.92f);
        image.raycastTarget = false;
        line.transform.SetAsFirstSibling();
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

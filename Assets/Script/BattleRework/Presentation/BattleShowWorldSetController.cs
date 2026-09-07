using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward / Map 선택을 실제 전투 월드의 TV 쇼 세트로 운용합니다.
///
/// - 아이템 화면과 맵 화면은 같은 WorldSpace TV viewport 안에서 교체됩니다.
/// - viewport에는 RectMask2D를 걸어 TV 내부 콘텐츠가 화면 바깥으로 새지 않습니다.
/// - TV와 사회자는 MapBlock의 진입/퇴장 연출을 그대로 재사용합니다.
/// - TV는 생성 시점의 카메라 viewport에 맞춰 자동 축소/보정되어 화면 밖으로 잘리지 않습니다.
/// - 사회자는 Canvas Image가 아니라 SpriteRenderer 월드 오브젝트입니다.
/// - 맵에는 좌측 START! 기점을 만들고 최초 노드까지 실제 선으로 연결합니다.
/// - 현재 클릭 가능한 노드는 hit area / outline / CLICK! 표식을 강화합니다.
/// </summary>
[DefaultExecutionOrder(20000)]
[DisallowMultipleComponent]
public sealed class BattleShowWorldSetController : MonoBehaviour
{
    private enum ShowMode
    {
        None,
        Reward,
        Map
    }

    private static BattleShowWorldSetController instance;

    [Header("World TV")]
    [Tooltip("TV UI의 기준 PPU입니다. 값이 높을수록 월드에서 작게 보입니다.")]
    [SerializeField, Min(16f)] private float worldPixelsPerUnit = 88.5f;

    [Tooltip("4x4 Base 중심을 기준으로 TV가 놓일 선호 위치입니다. 실제 위치는 카메라 안으로 자동 보정됩니다.")]
    [SerializeField] private Vector2 screenWorldOffset = new(0f, 3.65f);

    [Tooltip("TV + 아이템 Loadout Strip을 포함하는 World Canvas의 가상 픽셀 크기입니다.")]
    [SerializeField] private Vector2 worldCanvasSize = new(1180f, 760f);

    [Tooltip("아이템/맵 내용이 실제로 출력되는 TV viewport 크기입니다.")]
    [SerializeField] private Vector2 screenViewportSize = new(1120f, 560f);

    [Tooltip("World Canvas 중앙을 기준으로 TV viewport의 Y 위치입니다.")]
    [SerializeField] private float screenCanvasYOffset = 78f;

    [Tooltip("아이템 선택 시 Loadout Strip의 Canvas 내 Y 위치입니다.")]
    [SerializeField] private float inventoryCanvasY = -278f;

    [Tooltip("TV World Canvas Sorting Order. 바닥/캐릭터보다 뒤에 놓는 기본값입니다.")]
    [SerializeField] private int screenSortingOrder = -50;

    [Header("Camera Fit")]
    [Tooltip("TV 세트가 사용할 수 있는 카메라 가로 비율입니다. 호버 확대분까지 포함해 계산합니다.")]
    [SerializeField, Range(0.55f, 0.98f)] private float maxViewportWidthRatio = 0.86f;

    [Tooltip("TV 세트가 사용할 수 있는 카메라 세로 비율입니다. 플레이어/바닥이 완전히 가려지지 않도록 제한합니다.")]
    [SerializeField, Range(0.45f, 0.95f)] private float maxViewportHeightRatio = 0.72f;

    [Tooltip("카메라 가장자리와 TV 사이에 남길 최소 월드 여백입니다.")]
    [SerializeField, Min(0f)] private float cameraViewportMargin = 0.45f;

    [Tooltip("작은 해상도에서도 TV를 이 비율 이하로 축소하지 않습니다.")]
    [SerializeField, Range(0.35f, 1f)] private float minimumCameraFitScale = 0.58f;

    [Header("Presenter World Sprite")]
    [Tooltip("4x4 Base 중심을 기준으로 사회자가 서는 선호 위치입니다. 실제 위치는 카메라 안으로 자동 보정됩니다.")]
    [SerializeField] private Vector2 presenterWorldOffset = new(5.15f, 0.95f);

    [Tooltip("사회자 Sprite를 월드에서 이 높이로 정규화합니다.")]
    [SerializeField, Min(0.5f)] private float presenterWorldHeight = 3.6f;

    [SerializeField] private int presenterSortingOrder = 30;

    [Header("Mechanical Entry / Exit")]
    [Tooltip("TV가 위 레일에서 내려오는 방향입니다.")]
    [SerializeField] private Vector2 screenRailDirection = Vector2.up;

    [Tooltip("사회자가 오른쪽 세트 밖에서 들어오는 방향입니다.")]
    [SerializeField] private Vector2 presenterRailDirection = Vector2.right;

    [SerializeField, Min(0.05f)] private float screenEntryDuration = 0.62f;
    [SerializeField, Min(0.05f)] private float presenterEntryDuration = 0.48f;
    [SerializeField, Min(2f)] private float screenRailDistance = 16f;
    [SerializeField, Min(2f)] private float presenterRailDistance = 10f;
    [SerializeField, Range(0f, 1.5f)] private float screenImpactStrength = 0.78f;
    [SerializeField, Range(0f, 1.5f)] private float presenterImpactStrength = 0.48f;

    [Header("Cursor Focus")]
    [SerializeField, Range(1f, 1.30f)] private float cursorFocusScale = 1.12f;
    [SerializeField, Min(0.5f)] private float cursorFocusSharpness = 6.5f;

    [Header("Map Readability")]
    [Tooltip("START! 표식을 첫 노드보다 왼쪽으로 떨어뜨리는 거리입니다.")]
    [SerializeField, Min(30f)] private float mapStartGap = 112f;

    [SerializeField] private Vector2 mapStartSize = new(92f, 46f);
    [SerializeField] private Color mapStartColor = new(0.11f, 0.78f, 0.98f, 1f);
    [SerializeField] private Color mapStartLinkColor = new(0.16f, 0.78f, 1f, 0.92f);
    [SerializeField] private Color selectableNodeAccent = new(1f, 0.78f, 0.16f, 1f);
    [SerializeField, Min(40f)] private float selectableNodeMinSize = 60f;
    [SerializeField, Min(0.02f)] private float mapDecorationInterval = 0.08f;

    private BattleRunManager runManager;
    private BattleHUD hud;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;

    private GameObject screenRailRoot;
    private Transform screenFocusRoot;
    private Canvas worldCanvas;
    private RectTransform worldCanvasRect;
    private RectTransform screenViewportRect;
    private GraphicRaycaster worldRaycaster;
    private MapBlock screenRailBlock;

    private RectTransform rewardScreenRect;
    private RectTransform rewardInventoryRect;
    private RectTransform mapScreenRect;
    private RectTransform mapSelectionRect;
    private CanvasGroup rewardScreenGroup;
    private CanvasGroup rewardInventoryGroup;
    private CanvasGroup mapScreenGroup;

    private GameObject presenterRailRoot;
    private Transform presenterVisual;
    private SpriteRenderer presenterRenderer;
    private MapBlock presenterRailBlock;

    private Image legacyFieldFilter;
    private Image legacyPlayerSpotlight;
    private Image legacyPresenterSpotlight;
    private GameObject legacyPresenterObject;

    private Coroutine bindRoutine;
    private Coroutine transitionRoutine;
    private ShowMode currentMode;
    private ShowMode desiredMode;
    private bool bound;
    private bool railTransitioning;
    private float currentFocusScale = 1f;
    private float currentCameraFitScale = 1f;
    private float nextMapDecorationTime;
    private Sprite lastPresenterSprite;

    private Vector3 screenDockPosition;
    private Vector3 presenterDockPosition;
    private Vector3 lastDockBaseCenter;

    private static readonly BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo HudPresenterSpriteField =
        typeof(BattleHUD).GetField("presenterSprite", PrivateInstance);

    private static readonly FieldInfo HudPresenterColorField =
        typeof(BattleHUD).GetField("presenterColor", PrivateInstance);

    private static readonly FieldInfo HudPresenterFlipField =
        typeof(BattleHUD).GetField("presenterFlipX", PrivateInstance);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleShowWorldSetController>() != null)
            return;

        GameObject host = new("BattleShowWorldSetRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleShowWorldSetController>();
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
        bindRoutine = null;

        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);
        transitionRoutine = null;

        KillRailTweens();
        SetWorldInteraction(false);
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

            if (hud != null && runManager != null && TryBindHudShowObjects())
            {
                EnsureWorldSetObjects();
                MoveHudContentIntoWorldSet();
                SuppressLegacyScreenSpaceShowVisuals();
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
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (hud == null)
            hud = FindFirstObjectByType<BattleHUD>();
        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
    }

    private bool TryBindHudShowObjects()
    {
        if (rewardScreenRect == null)
            rewardScreenRect = FindRectTransform("PrizeSelectionScreen");
        if (rewardInventoryRect == null)
            rewardInventoryRect = FindRectTransform("RewardLoadoutStrip");
        if (mapScreenRect == null)
            mapScreenRect = FindRectTransform("MapSelectionScreen");
        if (mapSelectionRect == null)
            mapSelectionRect = FindRectTransform("MapSelectionContent");

        if (legacyFieldFilter == null)
            legacyFieldFilter = FindImage("FieldBroadcastFilter");
        if (legacyPlayerSpotlight == null)
            legacyPlayerSpotlight = FindImage("PlayerFloorSpotlight");
        if (legacyPresenterSpotlight == null)
            legacyPresenterSpotlight = FindImage("PresenterFloorSpotlight");
        if (legacyPresenterObject == null)
            legacyPresenterObject = FindShowPresenterObject();

        return rewardScreenRect != null &&
               rewardInventoryRect != null &&
               mapScreenRect != null &&
               mapSelectionRect != null;
    }

    private void EnsureWorldSetObjects()
    {
        if (screenRailRoot == null)
        {
            screenRailRoot = new GameObject("BattleShowScreenRail");
            screenRailRoot.transform.SetParent(transform, false);

            GameObject focusObject = new("ScreenFocusRoot");
            focusObject.transform.SetParent(screenRailRoot.transform, false);
            screenFocusRoot = focusObject.transform;

            GameObject canvasObject = new("BattleShowWorldCanvas");
            canvasObject.transform.SetParent(screenFocusRoot, false);
            worldCanvas = canvasObject.AddComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;
            worldCanvas.overrideSorting = true;
            worldCanvas.sortingOrder = screenSortingOrder;
            worldCanvas.worldCamera = Camera.main;

            worldRaycaster = canvasObject.AddComponent<GraphicRaycaster>();
            worldCanvasRect = canvasObject.GetComponent<RectTransform>();
            worldCanvasRect.sizeDelta = worldCanvasSize;
            worldCanvasRect.pivot = new Vector2(0.5f, 0.5f);
            worldCanvasRect.localPosition = Vector3.zero;
            worldCanvasRect.localRotation = Quaternion.identity;

            GameObject viewportObject = new("TVContentViewport");
            viewportObject.transform.SetParent(worldCanvasRect, false);
            screenViewportRect = viewportObject.AddComponent<RectTransform>();
            screenViewportRect.anchorMin = screenViewportRect.anchorMax = new Vector2(0.5f, 0.5f);
            screenViewportRect.pivot = new Vector2(0.5f, 0.5f);
            screenViewportRect.sizeDelta = screenViewportSize;
            screenViewportRect.anchoredPosition = new Vector2(0f, screenCanvasYOffset);
            viewportObject.AddComponent<RectMask2D>();

            screenRailBlock = screenRailRoot.AddComponent<MapBlock>();
            screenRailBlock.ConfigureRuntimeDockingBlock(
                screenRailRoot.transform,
                false,
                screenImpactStrength,
                screenEntryDuration,
                screenRailDistance);
        }

        if (presenterRailRoot == null)
        {
            presenterRailRoot = new GameObject("BattleShowPresenterRail");
            presenterRailRoot.transform.SetParent(transform, false);

            GameObject visualObject = new("PresenterWorldSprite");
            visualObject.transform.SetParent(presenterRailRoot.transform, false);
            presenterVisual = visualObject.transform;
            presenterRenderer = visualObject.AddComponent<SpriteRenderer>();
            presenterRenderer.sortingOrder = presenterSortingOrder;
            presenterRenderer.enabled = false;

            presenterRailBlock = presenterRailRoot.AddComponent<MapBlock>();
            presenterRailBlock.ConfigureRuntimeDockingBlock(
                presenterRailRoot.transform,
                false,
                presenterImpactStrength,
                presenterEntryDuration,
                presenterRailDistance);
        }

        ApplyCameraFitAndDockTargets();
        screenRailRoot.SetActive(false);
        presenterRailRoot.SetActive(false);
    }

    private void MoveHudContentIntoWorldSet()
    {
        if (worldCanvasRect == null || screenViewportRect == null)
            return;

        Vector2 resolvedViewportSize = screenViewportSize;
        if (rewardScreenRect != null && rewardScreenRect.sizeDelta.x > 1f && rewardScreenRect.sizeDelta.y > 1f)
            resolvedViewportSize = rewardScreenRect.sizeDelta;
        screenViewportRect.sizeDelta = resolvedViewportSize;

        ReparentScreenRect(rewardScreenRect, screenViewportRect);
        ReparentScreenRect(mapScreenRect, screenViewportRect);

        if (rewardInventoryRect != null)
        {
            rewardInventoryRect.SetParent(worldCanvasRect, false);
            rewardInventoryRect.anchorMin = rewardInventoryRect.anchorMax = new Vector2(0.5f, 0.5f);
            rewardInventoryRect.pivot = new Vector2(0.5f, 0.5f);
            rewardInventoryRect.anchoredPosition = new Vector2(0f, inventoryCanvasY);
            rewardInventoryRect.localScale = Vector3.one;
            rewardInventoryRect.localRotation = Quaternion.identity;
        }

        rewardScreenGroup = EnsureCanvasGroup(rewardScreenRect);
        rewardInventoryGroup = EnsureCanvasGroup(rewardInventoryRect);
        mapScreenGroup = EnsureCanvasGroup(mapScreenRect);

        SetContentActive(ShowMode.None, false);
        SetWorldInteraction(false);
        ApplyCameraFitAndDockTargets();
    }

    private static void ReparentScreenRect(RectTransform rect, RectTransform parent)
    {
        if (rect == null || parent == null)
            return;

        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static CanvasGroup EnsureCanvasGroup(RectTransform rect)
    {
        if (rect == null)
            return null;

        CanvasGroup group = rect.GetComponent<CanvasGroup>();
        if (group == null)
            group = rect.gameObject.AddComponent<CanvasGroup>();
        return group;
    }

    private void SuppressLegacyScreenSpaceShowVisuals()
    {
        if (legacyFieldFilter != null)
        {
            legacyFieldFilter.raycastTarget = false;
            legacyFieldFilter.enabled = false;
        }

        if (legacyPlayerSpotlight != null)
        {
            legacyPlayerSpotlight.raycastTarget = false;
            legacyPlayerSpotlight.enabled = false;
        }

        if (legacyPresenterSpotlight != null)
        {
            legacyPresenterSpotlight.raycastTarget = false;
            legacyPresenterSpotlight.enabled = false;
        }

        // 사회자 UI Image는 완전히 제거합니다. BattleHUD의 presenterSprite 값 자체는 남겨
        // PresentationManager가 재생하는 Sprite Sheet를 월드 SpriteRenderer가 계속 읽습니다.
        if (legacyPresenterObject != null)
        {
            Destroy(legacyPresenterObject);
            legacyPresenterObject = null;
        }
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
        UpdateDesiredMode();
        UpdatePresenterVisual();

        if (transitionRoutine == null && desiredMode != currentMode)
            transitionRoutine = StartCoroutine(TransitionLoop());
    }

    private void LateUpdate()
    {
        if (!bound)
            return;

        if (legacyFieldFilter != null)
            legacyFieldFilter.enabled = false;
        if (legacyPlayerSpotlight != null)
            legacyPlayerSpotlight.enabled = false;
        if (legacyPresenterSpotlight != null)
            legacyPresenterSpotlight.enabled = false;

        if (worldCanvas != null && worldCanvas.worldCamera != Camera.main)
            worldCanvas.worldCamera = Camera.main;

        UpdateWorldSorting();
        UpdateCursorFocus();
        FollowCurrentBaseWhileDocked();
        EnsureMapReadabilityDecorations();
        AnimateMapReadability();
    }

    private void UpdateDesiredMode()
    {
        ShowMode next = ShowMode.None;
        if (runManager != null && runManager.RunActive)
        {
            if (runManager.State == BattleRunState.Reward)
                next = ShowMode.Reward;
            else if (runManager.State == BattleRunState.SelectingNode)
                next = ShowMode.Map;
        }

        desiredMode = next;
    }

    private IEnumerator TransitionLoop()
    {
        railTransitioning = true;
        SetWorldInteraction(false);

        while (currentMode != desiredMode)
        {
            ShowMode leaving = currentMode;
            if (leaving != ShowMode.None)
            {
                // 기존 화면을 즉시 끄지 않고 레일 퇴장 완료까지 유지합니다.
                SetContentActive(leaving, true);
                PlayRailExit();
                yield return new WaitForSecondsRealtime(GetExitWaitDuration());
            }

            ShowMode entering = desiredMode;
            currentMode = ShowMode.None;
            SetContentActive(ShowMode.None, false);

            if (entering == ShowMode.None)
            {
                if (screenRailRoot != null)
                    screenRailRoot.SetActive(false);
                if (presenterRailRoot != null)
                    presenterRailRoot.SetActive(false);
                currentFocusScale = 1f;
                if (screenFocusRoot != null)
                    screenFocusRoot.localScale = Vector3.one;
                continue;
            }

            SetContentActive(entering, true);
            PrepareRailObjectsForEntry();
            PlayRailEnter();
            yield return new WaitForSecondsRealtime(GetEntryWaitDuration());

            currentMode = entering;
            SetContentActive(currentMode, true);

            if (currentMode == desiredMode)
                SetWorldInteraction(true);
        }

        railTransitioning = false;
        transitionRoutine = null;

        if (currentMode == desiredMode && currentMode != ShowMode.None)
            SetWorldInteraction(true);
    }

    private void PrepareRailObjectsForEntry()
    {
        ApplyCameraFitAndDockTargets();

        if (screenRailRoot != null)
            screenRailRoot.SetActive(true);
        if (presenterRailRoot != null)
            presenterRailRoot.SetActive(true);

        currentFocusScale = 1f;
        if (screenFocusRoot != null)
            screenFocusRoot.localScale = Vector3.one;
    }

    private void PlayRailEnter()
    {
        if (screenRailBlock != null)
            screenRailBlock.PlayEnter(screenDockPosition, NormalizeDirection(screenRailDirection));
        if (presenterRailBlock != null)
            presenterRailBlock.PlayEnter(presenterDockPosition, NormalizeDirection(presenterRailDirection));
    }

    private void PlayRailExit()
    {
        if (screenRailBlock != null && screenRailRoot != null && screenRailRoot.activeSelf)
            screenRailBlock.PlayExit(NormalizeDirection(screenRailDirection));
        if (presenterRailBlock != null && presenterRailRoot != null && presenterRailRoot.activeSelf)
            presenterRailBlock.PlayExit(NormalizeDirection(presenterRailDirection));
    }

    private float GetEntryWaitDuration()
    {
        float screen = screenRailBlock != null ? screenRailBlock.GetEntryDuration() : screenEntryDuration;
        float presenter = presenterRailBlock != null ? presenterRailBlock.GetEntryDuration() : presenterEntryDuration;
        return Mathf.Max(screen, presenter) + 0.03f;
    }

    private float GetExitWaitDuration()
    {
        float screen = screenRailBlock != null ? screenRailBlock.ExitDuration : screenEntryDuration;
        float presenter = presenterRailBlock != null ? presenterRailBlock.ExitDuration : presenterEntryDuration;
        return Mathf.Max(screen, presenter) + 0.03f;
    }

    private void SetContentActive(ShowMode mode, bool forceVisible)
    {
        bool reward = forceVisible && mode == ShowMode.Reward;
        bool map = forceVisible && mode == ShowMode.Map;

        if (rewardScreenRect != null)
            rewardScreenRect.gameObject.SetActive(reward);
        if (rewardInventoryRect != null)
            rewardInventoryRect.gameObject.SetActive(reward);
        if (mapScreenRect != null)
            mapScreenRect.gameObject.SetActive(map);

        if (mapSelectionRect != null && map)
            mapSelectionRect.gameObject.SetActive(true);
    }

    private void SetWorldInteraction(bool enabledInteraction)
    {
        bool rewardActive = enabledInteraction && currentMode == ShowMode.Reward;
        bool mapActive = enabledInteraction && currentMode == ShowMode.Map;

        ConfigureCanvasGroup(rewardScreenGroup, rewardActive);
        ConfigureCanvasGroup(rewardInventoryGroup, rewardActive);
        ConfigureCanvasGroup(mapScreenGroup, mapActive);

        if (worldRaycaster != null)
            worldRaycaster.enabled = enabledInteraction && currentMode != ShowMode.None;
    }

    private static void ConfigureCanvasGroup(CanvasGroup group, bool interactive)
    {
        if (group == null)
            return;

        group.alpha = 1f;
        group.interactable = interactive;
        group.blocksRaycasts = interactive;
    }

    // ------------------------------------------------------------------
    // Camera fit / world placement
    // ------------------------------------------------------------------

    private void ApplyCameraFitAndDockTargets()
    {
        float baseScale = 1f / Mathf.Max(16f, worldPixelsPerUnit);
        currentCameraFitScale = CalculateCameraFitScale(baseScale);

        if (worldCanvasRect != null)
        {
            float scale = baseScale * currentCameraFitScale;
            worldCanvasRect.localScale = new Vector3(scale, scale, 1f);
        }

        Vector3 baseCenter = ResolveBaseCenter();
        lastDockBaseCenter = baseCenter;
        screenDockPosition = ResolveScreenDockPosition(baseCenter, baseScale * currentCameraFitScale);
        presenterDockPosition = ResolvePresenterDockPosition(baseCenter);
    }

    private float CalculateCameraFitScale(float baseWorldPerPixel)
    {
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic)
            return 1f;

        float cameraHeight = camera.orthographicSize * 2f;
        float cameraWidth = cameraHeight * Mathf.Max(0.1f, camera.aspect);
        float allowedWidth = Mathf.Max(1f, cameraWidth * maxViewportWidthRatio - cameraViewportMargin * 2f);
        float allowedHeight = Mathf.Max(1f, cameraHeight * maxViewportHeightRatio - cameraViewportMargin * 2f);

        // 호버 확대까지 계산해서 확대 순간 다시 화면 밖으로 잘리지 않게 합니다.
        float nominalWidth = worldCanvasSize.x * baseWorldPerPixel * Mathf.Max(1f, cursorFocusScale);
        float nominalHeight = worldCanvasSize.y * baseWorldPerPixel * Mathf.Max(1f, cursorFocusScale);
        float fit = Mathf.Min(
            1f,
            Mathf.Min(
                allowedWidth / Mathf.Max(0.01f, nominalWidth),
                allowedHeight / Mathf.Max(0.01f, nominalHeight)));

        return Mathf.Clamp(fit, minimumCameraFitScale, 1f);
    }

    private Vector3 ResolveScreenDockPosition(Vector3 baseCenter, float worldScale)
    {
        Vector3 preferred = baseCenter + (Vector3)screenWorldOffset;
        Vector2 halfExtents = new(
            worldCanvasSize.x * worldScale * Mathf.Max(1f, cursorFocusScale) * 0.5f,
            worldCanvasSize.y * worldScale * Mathf.Max(1f, cursorFocusScale) * 0.5f);
        return ClampPointInsideCamera(preferred, halfExtents);
    }

    private Vector3 ResolvePresenterDockPosition(Vector3 baseCenter)
    {
        Vector3 preferred = baseCenter + (Vector3)presenterWorldOffset;
        float aspect = 0.65f;
        if (presenterRenderer != null && presenterRenderer.sprite != null &&
            presenterRenderer.sprite.bounds.size.y > 0.0001f)
        {
            aspect = Mathf.Abs(
                presenterRenderer.sprite.bounds.size.x /
                presenterRenderer.sprite.bounds.size.y);
        }

        Vector2 halfExtents = new(
            presenterWorldHeight * Mathf.Max(0.25f, aspect) * 0.5f,
            presenterWorldHeight * 0.5f);
        return ClampPointInsideCamera(preferred, halfExtents);
    }

    private Vector3 ClampPointInsideCamera(Vector3 preferred, Vector2 halfExtents)
    {
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic)
            return preferred;

        Vector3 center = camera.transform.position;
        float cameraHalfHeight = camera.orthographicSize;
        float cameraHalfWidth = cameraHalfHeight * Mathf.Max(0.1f, camera.aspect);

        float left = center.x - cameraHalfWidth + cameraViewportMargin + halfExtents.x;
        float right = center.x + cameraHalfWidth - cameraViewportMargin - halfExtents.x;
        float bottom = center.y - cameraHalfHeight + cameraViewportMargin + halfExtents.y;
        float top = center.y + cameraHalfHeight - cameraViewportMargin - halfExtents.y;

        Vector3 result = preferred;
        result.x = left <= right ? Mathf.Clamp(preferred.x, left, right) : center.x;
        result.y = bottom <= top ? Mathf.Clamp(preferred.y, bottom, top) : center.y;
        return result;
    }

    private Vector3 ResolveBaseCenter()
    {
        if (baseTemplate == null)
            baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();

        if (baseTemplate != null && baseTemplate.ActiveBase != null)
            return baseTemplate.FixedCenterWorld;

        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
        return player != null ? player.transform.position : Vector3.zero;
    }

    private void FollowCurrentBaseWhileDocked()
    {
        if (railTransitioning || currentMode == ShowMode.None)
            return;

        Vector3 baseCenter = ResolveBaseCenter();
        // 카메라 추적 때문에 TV가 화면을 따라다니지 않도록 Base 자체가 바뀐 경우에만 재도킹합니다.
        if (Vector2.Distance(baseCenter, lastDockBaseCenter) > 0.20f)
        {
            ApplyCameraFitAndDockTargets();
            if (screenRailRoot != null && screenRailRoot.activeSelf)
                screenRailRoot.transform.position = screenDockPosition;
            if (presenterRailRoot != null && presenterRailRoot.activeSelf)
                presenterRailRoot.transform.position = presenterDockPosition;
        }
    }

    // ------------------------------------------------------------------
    // Cursor focus
    // ------------------------------------------------------------------

    private void UpdateCursorFocus()
    {
        if (screenFocusRoot == null)
            return;

        bool focused = false;
        if (!railTransitioning && currentMode != ShowMode.None)
        {
            RectTransform activeRect = currentMode == ShowMode.Map ? mapScreenRect : rewardScreenRect;
            Camera eventCamera = Camera.main;
            if (activeRect != null && activeRect.gameObject.activeInHierarchy && eventCamera != null)
            {
                focused = RectTransformUtility.RectangleContainsScreenPoint(
                    activeRect,
                    Input.mousePosition,
                    eventCamera);
            }
        }

        float target = focused ? Mathf.Max(1f, cursorFocusScale) : 1f;
        float blend = 1f - Mathf.Exp(-Mathf.Max(0.5f, cursorFocusSharpness) * Time.unscaledDeltaTime);
        currentFocusScale = Mathf.Lerp(currentFocusScale, target, blend);
        screenFocusRoot.localScale = Vector3.one * currentFocusScale;
    }

    // ------------------------------------------------------------------
    // Map START / selectable readability
    // ------------------------------------------------------------------

    private void EnsureMapReadabilityDecorations()
    {
        if (currentMode != ShowMode.Map || mapSelectionRect == null || !mapSelectionRect.gameObject.activeInHierarchy)
            return;
        if (Time.unscaledTime < nextMapDecorationTime)
            return;

        nextMapDecorationTime = Time.unscaledTime + Mathf.Max(0.02f, mapDecorationInterval);

        List<RectTransform> nodes = CollectStageNodes();
        if (nodes.Count == 0)
            return;

        for (int i = 0; i < nodes.Count; i++)
            DecorateSelectableNode(nodes[i]);

        if (mapSelectionRect.Find("StageStartMarker") != null)
            return;

        float minX = float.MaxValue;
        for (int i = 0; i < nodes.Count; i++)
            minX = Mathf.Min(minX, nodes[i].anchoredPosition.x);

        List<RectTransform> firstNodes = new();
        float firstYSum = 0f;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (Mathf.Abs(nodes[i].anchoredPosition.x - minX) > 1.5f)
                continue;
            firstNodes.Add(nodes[i]);
            firstYSum += nodes[i].anchoredPosition.y;
        }

        if (firstNodes.Count == 0)
            return;

        float firstY = firstYSum / firstNodes.Count;
        float panelLeft = mapSelectionRect.rect.xMin;
        float desiredX = minX - mapStartGap;
        float minimumX = panelLeft + mapStartSize.x * 0.5f + 16f;
        float startX = Mathf.Max(minimumX, desiredX);

        // 공간이 너무 좁으면 첫 노드와 겹치지 않는 범위에서 START를 최대한 좌측에 고정합니다.
        if (startX > minX - mapStartSize.x * 0.65f)
            startX = Mathf.Min(minX - mapStartSize.x * 0.65f, minimumX);

        Vector2 startPosition = new(startX, firstY);
        CreateStartMarker(startPosition);

        Vector2 lineStart = startPosition + Vector2.right * (mapStartSize.x * 0.5f + 4f);
        for (int i = 0; i < firstNodes.Count; i++)
            CreateMapLine(lineStart, firstNodes[i].anchoredPosition, "StartRouteLink");
    }

    private List<RectTransform> CollectStageNodes()
    {
        List<RectTransform> result = new();
        if (mapSelectionRect == null)
            return result;

        for (int i = 0; i < mapSelectionRect.childCount; i++)
        {
            Transform child = mapSelectionRect.GetChild(i);
            if (child == null || !child.name.StartsWith("StageNode_", StringComparison.Ordinal))
                continue;

            RectTransform rect = child as RectTransform;
            if (rect != null)
                result.Add(rect);
        }

        return result;
    }

    private void DecorateSelectableNode(RectTransform node)
    {
        if (node == null)
            return;

        Button button = node.GetComponent<Button>();
        if (button == null)
            return;

        node.sizeDelta = new Vector2(
            Mathf.Max(node.sizeDelta.x, selectableNodeMinSize),
            Mathf.Max(node.sizeDelta.y, selectableNodeMinSize));

        Outline outline = node.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = selectableNodeAccent;
            outline.effectDistance = new Vector2(3f, -3f);
        }

        ColorBlock colors = button.colors;
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(1f, 0.86f, 0.50f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        if (node.Find("SelectableClickPrompt") != null)
            return;

        GameObject prompt = new("SelectableClickPrompt");
        prompt.transform.SetParent(node, false);
        RectTransform promptRect = prompt.AddComponent<RectTransform>();
        promptRect.anchorMin = promptRect.anchorMax = new Vector2(0.5f, 1f);
        promptRect.pivot = new Vector2(0.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 12f);
        promptRect.sizeDelta = new Vector2(96f, 24f);

        Text text = prompt.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = "▼  CLICK!";
        text.fontSize = 12;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = selectableNodeAccent;
        text.raycastTarget = false;

        CanvasGroup group = prompt.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private void CreateStartMarker(Vector2 position)
    {
        if (mapSelectionRect == null)
            return;

        GameObject marker = new("StageStartMarker");
        marker.transform.SetParent(mapSelectionRect, false);

        RectTransform rect = marker.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = mapStartSize;

        Image image = marker.AddComponent<Image>();
        image.color = mapStartColor;
        image.raycastTarget = false;

        Outline outline = marker.AddComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(2f, -2f);

        Text text = CreateSimpleText(marker.transform, "START!  ▶", 14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        if (text != null)
            StretchRect(text.rectTransform);
    }

    private void CreateMapLine(Vector2 from, Vector2 to, string objectName)
    {
        if (mapSelectionRect == null)
            return;

        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 1f)
            return;

        GameObject line = new(objectName);
        line.transform.SetParent(mapSelectionRect, false);
        Image image = line.AddComponent<Image>();
        image.color = mapStartLinkColor;
        image.raycastTarget = false;

        RectTransform rect = line.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = (from + to) * 0.5f;
        rect.sizeDelta = new Vector2(length, 5f);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        line.transform.SetAsFirstSibling();
    }

    private void AnimateMapReadability()
    {
        if (currentMode != ShowMode.Map || mapSelectionRect == null || !mapSelectionRect.gameObject.activeInHierarchy)
            return;

        float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.5f);
        float pulse = Mathf.Lerp(1f, 1.055f, wave);
        float alpha = Mathf.Lerp(0.62f, 1f, wave);

        Transform start = mapSelectionRect.Find("StageStartMarker");
        if (start != null)
            start.localScale = Vector3.one * Mathf.Lerp(1f, 1.035f, wave);

        for (int i = 0; i < mapSelectionRect.childCount; i++)
        {
            Transform child = mapSelectionRect.GetChild(i);
            if (child == null || !child.name.StartsWith("StageNode_", StringComparison.Ordinal))
                continue;

            Transform prompt = child.Find("SelectableClickPrompt");
            if (prompt == null)
                continue;

            prompt.localScale = Vector3.one * pulse;
            CanvasGroup group = prompt.GetComponent<CanvasGroup>();
            if (group != null)
                group.alpha = alpha;
        }
    }

    // ------------------------------------------------------------------
    // Presenter
    // ------------------------------------------------------------------

    private void UpdatePresenterVisual()
    {
        if (hud == null || presenterRenderer == null || presenterVisual == null)
            return;

        Sprite sprite = HudPresenterSpriteField != null
            ? HudPresenterSpriteField.GetValue(hud) as Sprite
            : null;

        if (sprite != lastPresenterSprite)
        {
            lastPresenterSprite = sprite;
            presenterRenderer.sprite = sprite;
            ApplyPresenterWorldScale(sprite);

            if (!railTransitioning && currentMode != ShowMode.None)
                presenterDockPosition = ResolvePresenterDockPosition(lastDockBaseCenter);
        }

        if (HudPresenterColorField != null && HudPresenterColorField.GetValue(hud) is Color color)
            presenterRenderer.color = color;

        presenterRenderer.enabled = sprite != null;
    }

    private void ApplyPresenterWorldScale(Sprite sprite)
    {
        if (presenterVisual == null)
            return;

        float height = sprite != null ? Mathf.Abs(sprite.bounds.size.y) : 0f;
        float scale = height > 0.0001f ? presenterWorldHeight / height : 1f;
        bool flip = HudPresenterFlipField != null &&
                    HudPresenterFlipField.GetValue(hud) is bool flipX &&
                    flipX;

        presenterVisual.localScale = new Vector3(flip ? -scale : scale, scale, 1f);
    }

    private void UpdateWorldSorting()
    {
        SpriteRenderer baseRenderer = null;
        if (baseTemplate != null && baseTemplate.ActiveBase != null)
            baseRenderer = baseTemplate.ActiveBase.GetComponentInChildren<SpriteRenderer>(true);

        if (worldCanvas != null)
        {
            worldCanvas.overrideSorting = true;
            if (baseRenderer != null)
            {
                worldCanvas.sortingLayerID = baseRenderer.sortingLayerID;
                worldCanvas.sortingOrder = Mathf.Min(screenSortingOrder, baseRenderer.sortingOrder - 1);
            }
            else
            {
                worldCanvas.sortingOrder = screenSortingOrder;
            }
        }

        if (presenterRenderer != null)
        {
            SpriteRenderer playerRenderer = player != null
                ? player.GetComponentInChildren<SpriteRenderer>(true)
                : null;

            if (playerRenderer != null)
            {
                presenterRenderer.sortingLayerID = playerRenderer.sortingLayerID;
                presenterRenderer.sortingOrder = Mathf.Max(
                    presenterSortingOrder,
                    playerRenderer.sortingOrder + 1);
            }
            else if (baseRenderer != null)
            {
                presenterRenderer.sortingLayerID = baseRenderer.sortingLayerID;
                presenterRenderer.sortingOrder = Mathf.Max(
                    presenterSortingOrder,
                    baseRenderer.sortingOrder + 1);
            }
            else
            {
                presenterRenderer.sortingOrder = presenterSortingOrder;
            }
        }
    }

    private void KillRailTweens()
    {
        if (screenRailRoot != null)
            screenRailRoot.transform.DOKill();
        if (presenterRailRoot != null)
            presenterRailRoot.transform.DOKill();
        if (screenFocusRoot != null)
            screenFocusRoot.DOKill();
    }

    private static Vector2 NormalizeDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return Vector2.up;

        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
            return direction.x >= 0f ? Vector2.right : Vector2.left;
        return direction.y >= 0f ? Vector2.up : Vector2.down;
    }

    private static RectTransform FindRectTransform(string objectName)
    {
        RectTransform[] rects = FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }

        return null;
    }

    private static Image FindImage(string objectName)
    {
        Image[] images = FindObjectsByType<Image>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image != null && image.name == objectName)
                return image;
        }

        return null;
    }

    private static GameObject FindShowPresenterObject()
    {
        RectTransform[] rects = FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect == null || rect.name != "Presenter")
                continue;

            Transform parent = rect.parent;
            while (parent != null)
            {
                if (parent.name == "RewardQuizShow")
                    return rect.gameObject;
                parent = parent.parent;
            }
        }

        return null;
    }

    private static Text CreateSimpleText(
        Transform parent,
        string value,
        int fontSize,
        FontStyle style,
        TextAnchor anchor,
        Color color)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return null;

        GameObject go = new("Text");
        go.transform.SetParent(parent, false);
        Text text = go.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void StretchRect(RectTransform rect)
    {
        if (rect == null)
            return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

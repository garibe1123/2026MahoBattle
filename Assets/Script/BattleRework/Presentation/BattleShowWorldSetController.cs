using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전투 쇼 화면의 하이브리드 배치 관리자입니다.
///
/// 구조:
/// - TV의 위치/진입/퇴장은 실제 월드 GameObject(BattleShowScreenRail)가 담당합니다.
/// - TV 안의 Prize / Map / Loadout은 ScreenSpace UI를 유지합니다.
/// - 따라서 필드 SpriteRenderer가 TV를 가리지 않고, 클릭/드래그도 일반 UI 이벤트로 처리됩니다.
/// - UI는 매 프레임 월드 TV Anchor의 화면 좌표를 따라가므로 시각적으로는 필드 위에 떠 있는 TV처럼 보입니다.
/// - TV의 기준점은 Player가 아니라 현재 BattleWalkableField 전체 Bounds의 위쪽입니다.
/// - 사회자는 Canvas Image를 사용하지 않고 별도의 SpriteRenderer 월드 GameObject로 유지합니다.
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

    [Header("TV - Field 기준 월드 배치")]
    [Tooltip("현재 Walkable Field의 가장 윗면에서 TV 아래쪽까지 띄우는 월드 간격입니다.")]
    [SerializeField, Min(0f)] private float fieldTopGap = 0.65f;

    [Tooltip("Field Bounds를 찾지 못했을 때 카메라 중심에서 TV를 올리는 월드 오프셋입니다.")]
    [SerializeField] private Vector2 fallbackCameraOffset = new(0f, 2.8f);

    [Header("TV - ScreenSpace UI 크기")]
    [Tooltip("TV가 화면 가로에서 차지할 수 있는 최대 비율입니다.")]
    [SerializeField, Range(0.35f, 0.95f)] private float maxScreenWidthRatio = 0.72f;

    [Tooltip("TV + Loadout 전체가 화면 세로에서 차지할 수 있는 최대 비율입니다.")]
    [SerializeField, Range(0.35f, 0.95f)] private float maxScreenTotalHeightRatio = 0.68f;

    [Tooltip("화면 가장자리와 TV/UI 사이에 남기는 Canvas 기준 여백입니다.")]
    [SerializeField, Min(0f)] private float screenSafeMargin = 28f;

    [Tooltip("TV와 아이템 Loadout Strip 사이의 Canvas 기준 간격입니다.")]
    [SerializeField, Min(0f)] private float inventoryGap = 12f;

    [Header("Mechanical Entry / Exit")]
    [Tooltip("TV가 화면 밖 위쪽에서 들어오는 레일 방향입니다.")]
    [SerializeField] private Vector2 screenRailDirection = Vector2.up;

    [SerializeField, Min(0.05f)] private float screenEntryDuration = 0.62f;
    [SerializeField, Min(2f)] private float screenRailDistance = 16f;
    [SerializeField, Range(0f, 1.5f)] private float screenImpactStrength = 0.78f;

    [Header("Cursor Focus")]
    [SerializeField, Range(1f, 1.25f)] private float cursorFocusScale = 1.08f;
    [SerializeField, Min(0.5f)] private float cursorFocusSharpness = 7f;

    [Header("Presenter - World Sprite")]
    [Tooltip("TV 화면 옆에서 사회자가 떨어져 보이는 최소 화면 픽셀 간격입니다.")]
    [SerializeField, Min(0f)] private float presenterScreenGap = 34f;

    [Tooltip("사회자 Sprite의 월드 높이입니다.")]
    [SerializeField, Min(0.5f)] private float presenterWorldHeight = 3.4f;

    [Tooltip("사회자를 TV 중심보다 아래로 내리는 화면 픽셀 오프셋입니다.")]
    [SerializeField] private float presenterScreenYOffset = -82f;

    [Tooltip("Presenter Frames / HUD Sprite가 모두 비어 있을 때 사용할 선택적 대체 Sprite입니다.")]
    [SerializeField] private Sprite presenterFallbackSprite;

    [SerializeField] private int presenterSortingOrder = 300;
    [SerializeField] private Vector2 presenterRailDirection = Vector2.right;
    [SerializeField, Min(0.05f)] private float presenterEntryDuration = 0.48f;
    [SerializeField, Min(2f)] private float presenterRailDistance = 10f;
    [SerializeField, Range(0f, 1.5f)] private float presenterImpactStrength = 0.48f;

    [Header("Map Readability")]
    [SerializeField, Min(30f)] private float mapStartGap = 112f;
    [SerializeField] private Vector2 mapStartSize = new(92f, 46f);
    [SerializeField] private Color mapStartColor = new(0.11f, 0.78f, 0.98f, 1f);
    [SerializeField] private Color mapStartLinkColor = new(0.16f, 0.78f, 1f, 0.92f);
    [SerializeField] private Color selectableNodeAccent = new(1f, 0.78f, 0.16f, 1f);
    [SerializeField, Min(40f)] private float selectableNodeMinSize = 60f;
    [SerializeField, Min(0.02f)] private float mapDecorationInterval = 0.08f;

    private BattleRunManager runManager;
    private BattleHUD hud;
    private PlayerController player;
    private BattleShowPresentationManager presentationManager;

    private Canvas overlayCanvas;
    private RectTransform overlayCanvasRect;
    private RectTransform rewardRootRect;
    private RectTransform overlayAnchorRect;
    private RectTransform rewardScreenRect;
    private RectTransform rewardInventoryRect;
    private RectTransform mapScreenRect;
    private RectTransform mapSelectionRect;
    private GameObject legacyMapWorldCanvasRoot;

    private CanvasGroup rewardScreenGroup;
    private CanvasGroup rewardInventoryGroup;
    private CanvasGroup mapScreenGroup;

    private Image legacyFieldFilter;
    private Image legacyPlayerSpotlight;
    private Image legacyPresenterSpotlight;
    private GameObject legacyPresenterObject;
    private Sprite legacyPresenterSprite;

    private GameObject screenRailRoot;
    private MapBlock screenRailBlock;
    private GameObject presenterRailRoot;
    private Transform presenterVisual;
    private SpriteRenderer presenterRenderer;
    private MapBlock presenterRailBlock;

    private Coroutine bindRoutine;
    private Coroutine transitionRoutine;
    private ShowMode currentMode;
    private ShowMode desiredMode;
    private bool bound;
    private bool railTransitioning;
    private float overlayFitScale = 1f;
    private float currentFocusScale = 1f;
    private float nextMapDecorationTime;

    private Vector2 rewardScreenBaseSize = new(1120f, 560f);
    private Vector2 rewardInventoryBaseSize = new(1120f, 150f);
    private Vector2 mapScreenBaseSize = new(1120f, 560f);
    private Vector3 screenDockPosition;
    private Vector3 presenterDockPosition;
    private Sprite lastPresenterSprite;

    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo HudPresenterSpriteField =
        typeof(BattleHUD).GetField("presenterSprite", PrivateInstance);

    private static readonly FieldInfo HudPresenterColorField =
        typeof(BattleHUD).GetField("presenterColor", PrivateInstance);

    private static readonly FieldInfo HudPresenterFlipField =
        typeof(BattleHUD).GetField("presenterFlipX", PrivateInstance);

    private static readonly FieldInfo PresenterFramesField =
        typeof(BattleShowPresentationManager).GetField("presenterFrames", PrivateInstance);

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
        SetUiInteraction(false);
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
            if (hud != null && runManager != null && TryBindHudObjects())
            {
                CaptureLegacyPresenterSprite();
                BuildWorldRails();
                PrepareScreenSpaceShowUi();
                SuppressLegacyScreenSpaceDecoration();
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
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
        if (presentationManager == null)
            presentationManager = FindFirstObjectByType<BattleShowPresentationManager>();
    }

    private bool TryBindHudObjects()
    {
        if (overlayCanvas == null)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];
                if (candidate != null && candidate.name == "BattleBroadcastHUDCanvas")
                {
                    overlayCanvas = candidate;
                    overlayCanvasRect = candidate.GetComponent<RectTransform>();
                    break;
                }
            }
        }

        if (rewardRootRect == null)
            rewardRootRect = FindRectTransform("RewardQuizShow");
        if (rewardScreenRect == null)
            rewardScreenRect = FindRectTransform("PrizeSelectionScreen");
        if (rewardInventoryRect == null)
            rewardInventoryRect = FindRectTransform("RewardLoadoutStrip");
        if (mapScreenRect == null)
            mapScreenRect = FindRectTransform("MapSelectionScreen");
        if (mapSelectionRect == null)
            mapSelectionRect = FindRectTransform("MapSelectionContent");

        if (legacyMapWorldCanvasRoot == null)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i] != null && canvases[i].name == "BattleMapSelectionWorldCanvas")
                {
                    legacyMapWorldCanvasRoot = canvases[i].gameObject;
                    break;
                }
            }
        }

        if (legacyFieldFilter == null)
            legacyFieldFilter = FindImage("FieldBroadcastFilter");
        if (legacyPlayerSpotlight == null)
            legacyPlayerSpotlight = FindImage("PlayerFloorSpotlight");
        if (legacyPresenterSpotlight == null)
            legacyPresenterSpotlight = FindImage("PresenterFloorSpotlight");
        if (legacyPresenterObject == null)
            legacyPresenterObject = FindLegacyPresenterObject();

        return overlayCanvas != null && overlayCanvasRect != null &&
               rewardRootRect != null && rewardScreenRect != null &&
               rewardInventoryRect != null && mapScreenRect != null &&
               mapSelectionRect != null;
    }

    private void CaptureLegacyPresenterSprite()
    {
        if (legacyPresenterObject == null)
            return;

        Image image = legacyPresenterObject.GetComponent<Image>();
        if (image != null && image.sprite != null)
            legacyPresenterSprite = image.sprite;
    }

    private void BuildWorldRails()
    {
        if (screenRailRoot == null)
        {
            screenRailRoot = new GameObject("BattleShowScreenRail");
            screenRailRoot.transform.SetParent(transform, false);
            screenRailBlock = screenRailRoot.AddComponent<MapBlock>();
            screenRailBlock.ConfigureRuntimeDockingBlock(
                screenRailRoot.transform,
                false,
                screenImpactStrength,
                screenEntryDuration,
                screenRailDistance);
            screenRailRoot.SetActive(false);
        }

        if (presenterRailRoot == null)
        {
            presenterRailRoot = new GameObject("BattleShowPresenterRail");
            presenterRailRoot.transform.SetParent(transform, false);

            GameObject visual = new("PresenterWorldSprite");
            visual.transform.SetParent(presenterRailRoot.transform, false);
            presenterVisual = visual.transform;
            presenterRenderer = visual.AddComponent<SpriteRenderer>();
            presenterRenderer.sortingOrder = presenterSortingOrder;
            presenterRenderer.enabled = false;

            presenterRailBlock = presenterRailRoot.AddComponent<MapBlock>();
            presenterRailBlock.ConfigureRuntimeDockingBlock(
                presenterRailRoot.transform,
                false,
                presenterImpactStrength,
                presenterEntryDuration,
                presenterRailDistance);
            presenterRailRoot.SetActive(false);
        }
    }

    private void PrepareScreenSpaceShowUi()
    {
        if (rewardRootRect == null)
            return;

        if (overlayAnchorRect == null)
        {
            GameObject anchor = new("WorldTVProjectedUI");
            anchor.transform.SetParent(rewardRootRect, false);
            overlayAnchorRect = anchor.AddComponent<RectTransform>();
            overlayAnchorRect.anchorMin = overlayAnchorRect.anchorMax = new Vector2(0.5f, 0.5f);
            overlayAnchorRect.pivot = new Vector2(0.5f, 0.5f);
            overlayAnchorRect.sizeDelta = Vector2.zero;
        }

        rewardScreenBaseSize = ResolveBaseSize(rewardScreenRect, rewardScreenBaseSize);
        rewardInventoryBaseSize = ResolveBaseSize(rewardInventoryRect, rewardInventoryBaseSize);
        mapScreenBaseSize = ResolveBaseSize(mapScreenRect, mapScreenBaseSize);

        ReparentOverlayRect(rewardScreenRect, overlayAnchorRect, Vector2.zero, rewardScreenBaseSize);
        ReparentOverlayRect(mapScreenRect, overlayAnchorRect, Vector2.zero, mapScreenBaseSize);

        Vector2 inventoryPosition = new(
            0f,
            -(rewardScreenBaseSize.y * 0.5f + inventoryGap + rewardInventoryBaseSize.y * 0.5f));
        ReparentOverlayRect(
            rewardInventoryRect,
            overlayAnchorRect,
            inventoryPosition,
            rewardInventoryBaseSize);

        rewardScreenGroup = EnsureCanvasGroup(rewardScreenRect);
        rewardInventoryGroup = EnsureCanvasGroup(rewardInventoryRect);
        mapScreenGroup = EnsureCanvasGroup(mapScreenRect);

        overlayFitScale = CalculateOverlayFitScale();
        currentFocusScale = 1f;
        overlayAnchorRect.localScale = Vector3.one * overlayFitScale;
        overlayAnchorRect.localRotation = Quaternion.identity;

        if (legacyMapWorldCanvasRoot != null)
            legacyMapWorldCanvasRoot.SetActive(false);

        SetContentActive(ShowMode.None, false);
        SetUiInteraction(false);
    }

    private static Vector2 ResolveBaseSize(RectTransform rect, Vector2 fallback)
    {
        if (rect == null)
            return fallback;

        Vector2 size = rect.sizeDelta;
        if (size.x <= 1f || size.y <= 1f)
            return fallback;
        return size;
    }

    private static void ReparentOverlayRect(
        RectTransform rect,
        RectTransform parent,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        if (rect == null || parent == null)
            return;

        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
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

    private void SuppressLegacyScreenSpaceDecoration()
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

        if (legacyMapWorldCanvasRoot != null && legacyMapWorldCanvasRoot.activeSelf)
            legacyMapWorldCanvasRoot.SetActive(false);

        if (legacyFieldFilter != null)
            legacyFieldFilter.enabled = false;
        if (legacyPlayerSpotlight != null)
            legacyPlayerSpotlight.enabled = false;
        if (legacyPresenterSpotlight != null)
            legacyPresenterSpotlight.enabled = false;

        UpdatePresenterSorting();
        SyncProjectedUiToWorldTv();
        UpdateCursorFocus();
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
        SetUiInteraction(false);

        while (currentMode != desiredMode)
        {
            ShowMode leaving = currentMode;
            if (leaving != ShowMode.None)
            {
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
                continue;
            }

            PrepareDockTargets();
            SetContentActive(entering, true);

            if (screenRailRoot != null)
                screenRailRoot.SetActive(true);
            if (presenterRailRoot != null)
                presenterRailRoot.SetActive(true);

            PlayRailEnter();
            yield return new WaitForSecondsRealtime(GetEntryWaitDuration());

            currentMode = entering;
            SetContentActive(currentMode, true);
            if (currentMode == desiredMode)
                SetUiInteraction(true);
        }

        railTransitioning = false;
        transitionRoutine = null;

        if (currentMode == desiredMode && currentMode != ShowMode.None)
            SetUiInteraction(true);
    }

    private void PrepareDockTargets()
    {
        overlayFitScale = CalculateOverlayFitScale();
        currentFocusScale = 1f;
        screenDockPosition = ResolveTvDockPosition();
        presenterDockPosition = ResolvePresenterDockPosition(screenDockPosition);
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

        if (rewardRootRect != null && forceVisible)
            rewardRootRect.gameObject.SetActive(true);
        if (rewardScreenRect != null)
            rewardScreenRect.gameObject.SetActive(reward);
        if (rewardInventoryRect != null)
            rewardInventoryRect.gameObject.SetActive(reward);
        if (mapScreenRect != null)
            mapScreenRect.gameObject.SetActive(map);
        if (mapSelectionRect != null && map)
            mapSelectionRect.gameObject.SetActive(true);
    }

    private void SetUiInteraction(bool interactionEnabled)
    {
        bool rewardInteractive = interactionEnabled && currentMode == ShowMode.Reward;
        bool mapInteractive = interactionEnabled && currentMode == ShowMode.Map;

        ConfigureCanvasGroup(rewardScreenGroup, rewardInteractive);
        ConfigureCanvasGroup(rewardInventoryGroup, rewardInteractive);
        ConfigureCanvasGroup(mapScreenGroup, mapInteractive);
    }

    private static void ConfigureCanvasGroup(CanvasGroup group, bool interactive)
    {
        if (group == null)
            return;

        group.alpha = 1f;
        group.interactable = interactive;
        group.blocksRaycasts = interactive;
    }

    private float CalculateOverlayFitScale()
    {
        if (rewardRootRect == null)
            return 1f;

        float rootWidth = Mathf.Max(1f, rewardRootRect.rect.width);
        float rootHeight = Mathf.Max(1f, rewardRootRect.rect.height);
        float maxFocus = Mathf.Max(1f, cursorFocusScale);
        float totalReferenceHeight = rewardScreenBaseSize.y + inventoryGap + rewardInventoryBaseSize.y;

        float widthFit = rootWidth * maxScreenWidthRatio /
                         Mathf.Max(1f, rewardScreenBaseSize.x * maxFocus);
        float heightFit = rootHeight * maxScreenTotalHeightRatio /
                          Mathf.Max(1f, totalReferenceHeight * maxFocus);

        return Mathf.Min(1f, Mathf.Max(0.05f, Mathf.Min(widthFit, heightFit)));
    }

    private Vector3 ResolveTvDockPosition()
    {
        Camera camera = Camera.main;
        if (camera == null)
            return Vector3.zero;

        Vector3 preferred;
        if (TryGetLiveFieldBounds(out Bounds fieldBounds))
        {
            float tvWorldHeight = GetUiHeightInWorld(rewardScreenBaseSize.y * overlayFitScale * cursorFocusScale);
            preferred = new Vector3(
                fieldBounds.center.x,
                fieldBounds.max.y + fieldTopGap + tvWorldHeight * 0.5f,
                fieldBounds.center.z);
        }
        else
        {
            preferred = camera.transform.position + (Vector3)fallbackCameraOffset;
            preferred.z = 0f;
        }

        return ClampTvDockInsideCamera(preferred);
    }

    private Vector3 ClampTvDockInsideCamera(Vector3 preferred)
    {
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic || rewardRootRect == null)
            return preferred;

        float cameraHalfHeight = camera.orthographicSize;
        float cameraHalfWidth = cameraHalfHeight * Mathf.Max(0.1f, camera.aspect);
        Vector3 cameraCenter = camera.transform.position;

        float maxScale = overlayFitScale * Mathf.Max(1f, cursorFocusScale);
        float halfWidth = GetUiWidthInWorld(rewardScreenBaseSize.x * maxScale) * 0.5f;
        float topExtent = GetUiHeightInWorld(rewardScreenBaseSize.y * maxScale) * 0.5f;
        float bottomExtent = topExtent +
                             GetUiHeightInWorld((inventoryGap + rewardInventoryBaseSize.y) * maxScale);
        float marginX = GetUiWidthInWorld(screenSafeMargin);
        float marginY = GetUiHeightInWorld(screenSafeMargin);

        float left = cameraCenter.x - cameraHalfWidth + halfWidth + marginX;
        float right = cameraCenter.x + cameraHalfWidth - halfWidth - marginX;
        float bottom = cameraCenter.y - cameraHalfHeight + bottomExtent + marginY;
        float top = cameraCenter.y + cameraHalfHeight - topExtent - marginY;

        Vector3 result = preferred;
        result.x = left <= right ? Mathf.Clamp(preferred.x, left, right) : cameraCenter.x;
        result.y = bottom <= top ? Mathf.Clamp(preferred.y, bottom, top) : cameraCenter.y;
        result.z = 0f;
        return result;
    }

    private float GetUiWidthInWorld(float uiWidth)
    {
        Camera camera = Camera.main;
        if (camera == null || rewardRootRect == null)
            return uiWidth / 100f;

        float cameraWidth = camera.orthographicSize * 2f * Mathf.Max(0.1f, camera.aspect);
        return uiWidth / Mathf.Max(1f, rewardRootRect.rect.width) * cameraWidth;
    }

    private float GetUiHeightInWorld(float uiHeight)
    {
        Camera camera = Camera.main;
        if (camera == null || rewardRootRect == null)
            return uiHeight / 100f;

        float cameraHeight = camera.orthographicSize * 2f;
        return uiHeight / Mathf.Max(1f, rewardRootRect.rect.height) * cameraHeight;
    }

    private static bool TryGetLiveFieldBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        BattleWalkableField[] fields = FindObjectsByType<BattleWalkableField>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < fields.Length; i++)
        {
            BattleWalkableField field = fields[i];
            if (field == null || !field.gameObject.activeInHierarchy)
                continue;

            Collider2D collider = field.GetComponent<Collider2D>();
            Bounds candidate;
            if (collider != null && collider.enabled)
            {
                candidate = collider.bounds;
            }
            else
            {
                SpriteRenderer renderer = field.GetComponent<SpriteRenderer>();
                if (renderer == null || !renderer.enabled)
                    continue;
                candidate = renderer.bounds;
            }

            if (!found)
            {
                bounds = candidate;
                found = true;
            }
            else
            {
                bounds.Encapsulate(candidate);
            }
        }

        return found;
    }

    private void SyncProjectedUiToWorldTv()
    {
        if (overlayAnchorRect == null || rewardRootRect == null || screenRailRoot == null)
            return;
        if (!screenRailRoot.activeSelf)
            return;

        Camera camera = Camera.main;
        if (camera == null)
            return;

        Vector3 screenPoint = camera.WorldToScreenPoint(screenRailRoot.transform.position);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rewardRootRect,
                screenPoint,
                null,
                out Vector2 localPoint))
            return;

        if (!railTransitioning)
            localPoint = ClampOverlayCenterInsideRoot(localPoint);

        overlayAnchorRect.anchoredPosition = localPoint;

        float railScale = Mathf.Max(
            0.75f,
            Mathf.Max(
                Mathf.Abs(screenRailRoot.transform.localScale.x),
                Mathf.Abs(screenRailRoot.transform.localScale.y)));
        float finalScale = overlayFitScale * currentFocusScale * railScale;
        overlayAnchorRect.localScale = Vector3.one * finalScale;
        overlayAnchorRect.localRotation = Quaternion.Euler(0f, 0f, screenRailRoot.transform.eulerAngles.z);
    }

    private Vector2 ClampOverlayCenterInsideRoot(Vector2 center)
    {
        if (rewardRootRect == null)
            return center;

        float maxScale = overlayFitScale * Mathf.Max(1f, cursorFocusScale);
        float halfWidth = rewardScreenBaseSize.x * maxScale * 0.5f;
        float topExtent = rewardScreenBaseSize.y * maxScale * 0.5f;
        float bottomExtent = topExtent +
                             (inventoryGap + rewardInventoryBaseSize.y) * maxScale;

        Rect root = rewardRootRect.rect;
        float minX = root.xMin + screenSafeMargin + halfWidth;
        float maxX = root.xMax - screenSafeMargin - halfWidth;
        float minY = root.yMin + screenSafeMargin + bottomExtent;
        float maxY = root.yMax - screenSafeMargin - topExtent;

        center.x = minX <= maxX ? Mathf.Clamp(center.x, minX, maxX) : 0f;
        center.y = minY <= maxY ? Mathf.Clamp(center.y, minY, maxY) : 0f;
        return center;
    }

    private Vector3 ResolvePresenterDockPosition(Vector3 tvWorldPosition)
    {
        Camera camera = Camera.main;
        if (camera == null)
            return tvWorldPosition + Vector3.right * 4f;

        Vector3 tvScreen = camera.WorldToScreenPoint(tvWorldPosition);
        float canvasScaleFactor = overlayCanvas != null ? Mathf.Max(0.01f, overlayCanvas.scaleFactor) : 1f;
        float tvHalfWidthPixels = rewardScreenBaseSize.x * overlayFitScale * 0.5f * canvasScaleFactor;
        float presenterGapPixels = presenterScreenGap * canvasScaleFactor;

        float presenterAspect = GetPresenterAspect();
        float presenterWorldWidth = presenterWorldHeight * presenterAspect;
        float presenterHalfPixels = WorldWidthToScreenPixels(presenterWorldWidth) * 0.5f;

        float rightX = tvScreen.x + tvHalfWidthPixels + presenterGapPixels + presenterHalfPixels;
        float leftX = tvScreen.x - tvHalfWidthPixels - presenterGapPixels - presenterHalfPixels;
        bool placeRight = rightX <= Screen.width - screenSafeMargin * canvasScaleFactor;
        float x = placeRight ? rightX : leftX;
        float y = tvScreen.y + presenterScreenYOffset * canvasScaleFactor;

        x = Mathf.Clamp(x, presenterHalfPixels + 8f, Screen.width - presenterHalfPixels - 8f);
        float presenterHalfHeightPixels = WorldHeightToScreenPixels(presenterWorldHeight) * 0.5f;
        y = Mathf.Clamp(y, presenterHalfHeightPixels + 8f, Screen.height - presenterHalfHeightPixels - 8f);

        Vector3 world = camera.ScreenToWorldPoint(new Vector3(x, y, Mathf.Abs(camera.transform.position.z)));
        world.z = 0f;
        return world;
    }

    private float GetPresenterAspect()
    {
        Sprite sprite = ResolvePresenterSprite();
        if (sprite != null && sprite.bounds.size.y > 0.0001f)
            return Mathf.Max(0.2f, Mathf.Abs(sprite.bounds.size.x / sprite.bounds.size.y));
        return 0.62f;
    }

    private static float WorldWidthToScreenPixels(float worldWidth)
    {
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic)
            return worldWidth * 64f;
        float cameraWidth = camera.orthographicSize * 2f * Mathf.Max(0.1f, camera.aspect);
        return worldWidth / Mathf.Max(0.01f, cameraWidth) * Screen.width;
    }

    private static float WorldHeightToScreenPixels(float worldHeight)
    {
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic)
            return worldHeight * 64f;
        float cameraHeight = camera.orthographicSize * 2f;
        return worldHeight / Mathf.Max(0.01f, cameraHeight) * Screen.height;
    }

    private void UpdatePresenterVisual()
    {
        if (presenterRenderer == null || presenterVisual == null)
            return;

        Sprite sprite = ResolvePresenterSprite();
        if (sprite != lastPresenterSprite)
        {
            lastPresenterSprite = sprite;
            presenterRenderer.sprite = sprite;
            ApplyPresenterWorldScale(sprite);
        }

        if (hud != null && HudPresenterColorField != null &&
            HudPresenterColorField.GetValue(hud) is Color color)
        {
            presenterRenderer.color = color;
        }
        else
        {
            presenterRenderer.color = Color.white;
        }

        bool show = (currentMode != ShowMode.None || desiredMode != ShowMode.None || railTransitioning) &&
                    presenterRailRoot != null && presenterRailRoot.activeSelf;
        presenterRenderer.enabled = show && sprite != null;
    }

    private Sprite ResolvePresenterSprite()
    {
        if (hud != null && HudPresenterSpriteField != null)
        {
            Sprite hudSprite = HudPresenterSpriteField.GetValue(hud) as Sprite;
            if (hudSprite != null && hudSprite != BattleHudSpriteCache.DefaultSprite)
                return hudSprite;
        }

        if (presentationManager == null)
            presentationManager = FindFirstObjectByType<BattleShowPresentationManager>();
        if (presentationManager != null && PresenterFramesField != null)
        {
            Sprite[] frames = PresenterFramesField.GetValue(presentationManager) as Sprite[];
            if (frames != null)
            {
                for (int i = 0; i < frames.Length; i++)
                    if (frames[i] != null)
                        return frames[i];
            }
        }

        if (presenterFallbackSprite != null)
            return presenterFallbackSprite;
        if (legacyPresenterSprite != null)
            return legacyPresenterSprite;
        return BattleHudSpriteCache.DefaultSprite;
    }

    private void ApplyPresenterWorldScale(Sprite sprite)
    {
        if (presenterVisual == null)
            return;

        float spriteHeight = sprite != null ? Mathf.Abs(sprite.bounds.size.y) : 0f;
        float scale = spriteHeight > 0.0001f ? presenterWorldHeight / spriteHeight : presenterWorldHeight;
        bool flip = hud != null && HudPresenterFlipField != null &&
                    HudPresenterFlipField.GetValue(hud) is bool flipX && flipX;

        presenterVisual.localScale = new Vector3(flip ? -scale : scale, scale, 1f);
    }

    private void UpdatePresenterSorting()
    {
        if (presenterRenderer == null)
            return;

        SpriteRenderer playerRenderer = player != null
            ? player.GetComponentInChildren<SpriteRenderer>(true)
            : null;

        if (playerRenderer != null)
        {
            presenterRenderer.sortingLayerID = playerRenderer.sortingLayerID;
            presenterRenderer.sortingOrder = Mathf.Max(
                presenterSortingOrder,
                playerRenderer.sortingOrder + 50);
        }
        else
        {
            presenterRenderer.sortingOrder = presenterSortingOrder;
        }
    }

    private void UpdateCursorFocus()
    {
        if (overlayAnchorRect == null)
            return;

        bool focused = false;
        if (!railTransitioning && currentMode != ShowMode.None)
        {
            RectTransform activeRect = currentMode == ShowMode.Map ? mapScreenRect : rewardScreenRect;
            if (activeRect != null && activeRect.gameObject.activeInHierarchy)
            {
                focused = RectTransformUtility.RectangleContainsScreenPoint(
                    activeRect,
                    Input.mousePosition,
                    null);
            }
        }

        float target = focused ? Mathf.Max(1f, cursorFocusScale) : 1f;
        float blend = 1f - Mathf.Exp(-Mathf.Max(0.5f, cursorFocusSharpness) * Time.unscaledDeltaTime);
        currentFocusScale = Mathf.Lerp(currentFocusScale, target, blend);
    }

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

            if (child is RectTransform rect)
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
        colors.pressedColor = new Color(1f, 0.92f, 0.50f, 1f);
        button.colors = colors;

        if (node.Find("ClickHint") == null)
            CreateClickHint(node);
    }

    private void CreateClickHint(RectTransform node)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        GameObject hint = new("ClickHint");
        hint.transform.SetParent(node, false);
        Text text = hint.AddComponent<Text>();
        text.font = font;
        text.text = "▼ CLICK!";
        text.fontSize = 11;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = selectableNodeAccent;
        text.raycastTarget = false;

        RectTransform rect = hint.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 9f);
        rect.sizeDelta = new Vector2(100f, 22f);
    }

    private void CreateStartMarker(Vector2 position)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null || mapSelectionRect == null)
            return;

        GameObject marker = new("StageStartMarker");
        marker.transform.SetParent(mapSelectionRect, false);

        Image image = marker.AddComponent<Image>();
        image.color = mapStartColor;
        image.raycastTarget = false;

        Outline outline = marker.AddComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(2f, -2f);

        RectTransform rect = marker.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = mapStartSize;

        GameObject label = new("Label");
        label.transform.SetParent(marker.transform, false);
        Text text = label.AddComponent<Text>();
        text.font = font;
        text.text = "START!  ▶";
        text.fontSize = 14;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        Stretch(label.GetComponent<RectTransform>());
    }

    private void CreateMapLine(Vector2 from, Vector2 to, string name)
    {
        if (mapSelectionRect == null)
            return;

        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 1f)
            return;

        GameObject line = new(name);
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
        if (currentMode != ShowMode.Map || mapSelectionRect == null)
            return;

        float pulse = 0.78f + Mathf.Sin(Time.unscaledTime * 5.5f) * 0.22f;
        for (int i = 0; i < mapSelectionRect.childCount; i++)
        {
            Transform child = mapSelectionRect.GetChild(i);
            if (child == null || !child.name.StartsWith("StageNode_", StringComparison.Ordinal))
                continue;
            if (child.GetComponent<Button>() == null)
                continue;

            Transform hint = child.Find("ClickHint");
            if (hint == null)
                continue;
            Text text = hint.GetComponent<Text>();
            if (text == null)
                continue;

            Color color = selectableNodeAccent;
            color.a = pulse;
            text.color = color;
        }
    }

    private void KillRailTweens()
    {
        if (screenRailRoot != null)
            screenRailRoot.transform.DOKill();
        if (presenterRailRoot != null)
            presenterRailRoot.transform.DOKill();
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

    private static GameObject FindLegacyPresenterObject()
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

    private static void Stretch(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전투 쇼 화면의 하이브리드 배치 관리자.
///
/// 최종 구조:
/// - Prize / Map / Loadout은 ScreenSpace UI를 유지해서 클릭/드래그를 일반 UI로 처리합니다.
/// - TV의 "어디에 떠 있는가"는 현재 BattleWalkableField의 화면상 상단을 기준으로 계산합니다.
/// - TV + Loadout의 최하단이 Field 상단을 침범하지 않도록 크기/위치를 계산합니다.
/// - TV 진입/퇴장은 보이지 않는 월드 MapBlock Anchor를 이용해 기존 기계식 도킹 감각을 재사용합니다.
/// - 사회자는 Canvas가 아니라 실제 SpriteRenderer GameObject이며 Field 우측에 별도로 배치합니다.
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

    [Header("TV - Field 위 ScreenSpace 배치")]
    [Tooltip("Field의 화면상 상단과 TV/Loadout 최하단 사이에 확보할 UI 픽셀 간격입니다.")]
    [SerializeField, Min(0f)] private float fieldScreenGap = 26f;

    [Tooltip("TV가 화면 가로에서 사용할 수 있는 최대 비율입니다.")]
    [SerializeField, Range(0.35f, 0.95f)] private float maxScreenWidthRatio = 0.68f;

    [Tooltip("TV + Loadout을 너무 작게 만들지 않기 위한 최소 배율입니다. 공간이 부족하면 이 값보다 더 작아질 수 있습니다.")]
    [SerializeField, Range(0.20f, 1f)] private float preferredMinimumScale = 0.46f;

    [Tooltip("화면 가장자리와 TV/UI 사이의 안전 여백입니다.")]
    [SerializeField, Min(0f)] private float screenSafeMargin = 24f;

    [Tooltip("TV와 아이템 Loadout Strip 사이 간격입니다.")]
    [SerializeField, Min(0f)] private float inventoryGap = 12f;

    [Header("Mechanical Entry / Exit")]
    [SerializeField] private Vector2 screenRailDirection = Vector2.up;
    [SerializeField, Min(0.05f)] private float screenEntryDuration = 0.62f;
    [SerializeField, Min(2f)] private float screenRailDistance = 16f;
    [SerializeField, Range(0f, 1.5f)] private float screenImpactStrength = 0.78f;

    [Header("Cursor Focus")]
    [SerializeField, Range(1f, 1.20f)] private float cursorFocusScale = 1.06f;
    [SerializeField, Min(0.5f)] private float cursorFocusSharpness = 7f;

    [Header("Presenter - 실제 World Sprite")]
    [Tooltip("Field 오른쪽 끝에서 안쪽으로 들어오는 거리입니다.")]
    [SerializeField, Min(0f)] private float presenterFieldInsetX = 1.0f;

    [Tooltip("Field 세로 중앙 기준 사회자의 Y 오프셋입니다.")]
    [SerializeField] private float presenterFieldYOffset = 0.15f;

    [Tooltip("사회자 Sprite의 월드 높이입니다.")]
    [SerializeField, Min(0.5f)] private float presenterWorldHeight = 3.4f;

    [Tooltip("화면 가장자리에서 사회자 Sprite가 잘리지 않도록 확보할 월드 여백입니다.")]
    [SerializeField, Min(0f)] private float presenterCameraMargin = 0.35f;

    [Tooltip("Presenter Frames / HUD Sprite가 모두 없을 때 사용할 선택적 대체 Sprite입니다.")]
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
    private Image legacyPresenterImage;
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
    private bool warnedMissingPresenterSprite;

    private Vector2 rewardScreenBaseSize = new(1120f, 560f);
    private Vector2 rewardInventoryBaseSize = new(1120f, 150f);
    private Vector2 mapScreenBaseSize = new(1120f, 560f);

    private Vector2 dockedOverlayCenter;
    private Vector3 screenDockPosition;
    private Vector3 presenterDockPosition;
    private Sprite lastPresenterSprite;

    private static readonly BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo HudPresenterSpriteField =
        typeof(BattleHUD).GetField("presenterSprite", PrivateInstance);

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
            Canvas[] canvases = FindObjectsByType<Canvas>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];
                if (candidate == null || candidate.name != "BattleBroadcastHUDCanvas")
                    continue;

                overlayCanvas = candidate;
                overlayCanvasRect = candidate.GetComponent<RectTransform>();
                break;
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
            Canvas[] canvases = FindObjectsByType<Canvas>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];
                if (candidate != null && candidate.name == "BattleMapSelectionWorldCanvas")
                {
                    legacyMapWorldCanvasRoot = candidate.gameObject;
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

        return overlayCanvas != null &&
               overlayCanvasRect != null &&
               rewardRootRect != null &&
               rewardScreenRect != null &&
               rewardInventoryRect != null &&
               mapScreenRect != null &&
               mapSelectionRect != null;
    }

    private void CaptureLegacyPresenterSprite()
    {
        if (legacyPresenterObject == null)
            return;

        legacyPresenterImage = legacyPresenterObject.GetComponent<Image>();
        if (legacyPresenterImage != null && legacyPresenterImage.sprite != null)
            legacyPresenterSprite = legacyPresenterImage.sprite;
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
            presenterRenderer.color = Color.white;
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

        ReparentOverlayRect(
            rewardScreenRect,
            overlayAnchorRect,
            Vector2.zero,
            rewardScreenBaseSize);

        ReparentOverlayRect(
            mapScreenRect,
            overlayAnchorRect,
            Vector2.zero,
            mapScreenBaseSize);

        Vector2 inventoryPosition = new(
            0f,
            -(rewardScreenBaseSize.y * 0.5f +
              inventoryGap +
              rewardInventoryBaseSize.y * 0.5f));

        ReparentOverlayRect(
            rewardInventoryRect,
            overlayAnchorRect,
            inventoryPosition,
            rewardInventoryBaseSize);

        rewardScreenGroup = EnsureCanvasGroup(rewardScreenRect);
        rewardInventoryGroup = EnsureCanvasGroup(rewardInventoryRect);
        mapScreenGroup = EnsureCanvasGroup(mapScreenRect);

        if (legacyMapWorldCanvasRoot != null)
            legacyMapWorldCanvasRoot.SetActive(false);

        currentFocusScale = 1f;
        overlayAnchorRect.localRotation = Quaternion.identity;

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
        DisableImage(legacyFieldFilter);
        DisableImage(legacyPlayerSpotlight);
        DisableImage(legacyPresenterSpotlight);

        // 삭제하지 않습니다. 기존 Sprite 참조를 계속 보존하되 렌더만 끕니다.
        if (legacyPresenterImage != null)
        {
            legacyPresenterImage.raycastTarget = false;
            legacyPresenterImage.enabled = false;
        }
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

        DisableImage(legacyFieldFilter);
        DisableImage(legacyPlayerSpotlight);
        DisableImage(legacyPresenterSpotlight);
        if (legacyPresenterImage != null)
            legacyPresenterImage.enabled = false;

        UpdateDockedLayout();
        UpdateCursorFocus();
        UpdatePresenterSorting();
        UpdatePresenterPosition();
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

            PrepareDockTargets(entering);
            SetContentActive(entering, true);

            if (screenRailRoot != null)
                screenRailRoot.SetActive(true);
            if (presenterRailRoot != null)
                presenterRailRoot.SetActive(true);

            if (presentationManager != null)
                presentationManager.PlayPresenterAnimation(true);

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

    private void PrepareDockTargets(ShowMode enteringMode)
    {
        currentFocusScale = 1f;

        CalculateFinalOverlayLayout(
            enteringMode,
            out dockedOverlayCenter,
            out overlayFitScale,
            out screenDockPosition);

        presenterDockPosition = ResolvePresenterDockPosition();
    }

    private void PlayRailEnter()
    {
        if (screenRailBlock != null)
            screenRailBlock.PlayEnter(
                screenDockPosition,
                NormalizeDirection(screenRailDirection));

        if (presenterRailBlock != null)
            presenterRailBlock.PlayEnter(
                presenterDockPosition,
                NormalizeDirection(presenterRailDirection));
    }

    private void PlayRailExit()
    {
        if (screenRailBlock != null &&
            screenRailRoot != null &&
            screenRailRoot.activeSelf)
        {
            screenRailBlock.PlayExit(NormalizeDirection(screenRailDirection));
        }

        if (presenterRailBlock != null &&
            presenterRailRoot != null &&
            presenterRailRoot.activeSelf)
        {
            presenterRailBlock.PlayExit(NormalizeDirection(presenterRailDirection));
        }
    }

    private float GetEntryWaitDuration()
    {
        float screen = screenRailBlock != null
            ? screenRailBlock.GetEntryDuration()
            : screenEntryDuration;

        float presenter = presenterRailBlock != null
            ? presenterRailBlock.GetEntryDuration()
            : presenterEntryDuration;

        return Mathf.Max(screen, presenter) + 0.03f;
    }

    private float GetExitWaitDuration()
    {
        float screen = screenRailBlock != null
            ? screenRailBlock.ExitDuration
            : screenEntryDuration;

        float presenter = presenterRailBlock != null
            ? presenterRailBlock.ExitDuration
            : presenterEntryDuration;

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
        bool rewardInteractive =
            interactionEnabled && currentMode == ShowMode.Reward;

        bool mapInteractive =
            interactionEnabled && currentMode == ShowMode.Map;

        ConfigureCanvasGroup(rewardScreenGroup, rewardInteractive);
        ConfigureCanvasGroup(rewardInventoryGroup, rewardInteractive);
        ConfigureCanvasGroup(mapScreenGroup, mapInteractive);
    }

    private static void ConfigureCanvasGroup(
        CanvasGroup group,
        bool interactive)
    {
        if (group == null)
            return;

        group.alpha = 1f;
        group.interactable = interactive;
        group.blocksRaycasts = interactive;
    }

    // ------------------------------------------------------------------
    // TV 위치 / 크기
    // ------------------------------------------------------------------

    private void UpdateDockedLayout()
    {
        ShowMode layoutMode =
            currentMode != ShowMode.None ? currentMode : desiredMode;

        if (layoutMode == ShowMode.None ||
            overlayAnchorRect == null ||
            rewardRootRect == null)
            return;

        Vector2 targetCenter;
        float targetFit;
        Vector3 targetWorld;

        CalculateFinalOverlayLayout(
            layoutMode,
            out targetCenter,
            out targetFit,
            out targetWorld);

        dockedOverlayCenter = targetCenter;
        overlayFitScale = targetFit;
        screenDockPosition = targetWorld;

        if (!railTransitioning)
        {
            if (screenRailRoot != null && screenRailRoot.activeSelf)
                screenRailRoot.transform.position = screenDockPosition;

            overlayAnchorRect.anchoredPosition = dockedOverlayCenter;
            overlayAnchorRect.localRotation = Quaternion.identity;
            overlayAnchorRect.localScale =
                Vector3.one * (overlayFitScale * currentFocusScale);
        }
        else
        {
            SyncUiToRailDuringTransition();
        }
    }

    private void CalculateFinalOverlayLayout(
        ShowMode mode,
        out Vector2 centerLocal,
        out float fitScale,
        out Vector3 worldAnchor)
    {
        centerLocal = Vector2.zero;
        fitScale = 1f;
        worldAnchor = Vector3.zero;

        if (rewardRootRect == null)
            return;

        Rect root = rewardRootRect.rect;
        float maxFocus = Mathf.Max(1f, cursorFocusScale);

        float screenWidth =
            mode == ShowMode.Map
                ? mapScreenBaseSize.x
                : rewardScreenBaseSize.x;

        float screenHeight =
            mode == ShowMode.Map
                ? mapScreenBaseSize.y
                : rewardScreenBaseSize.y;

        float bottomExtra =
            mode == ShowMode.Reward
                ? inventoryGap + rewardInventoryBaseSize.y
                : 0f;

        float totalReferenceHeight = screenHeight + bottomExtra;

        float usableWidth =
            Mathf.Max(
                1f,
                root.width * maxScreenWidthRatio -
                screenSafeMargin * 2f);

        float widthFit =
            usableWidth /
            Mathf.Max(1f, screenWidth * maxFocus);

        float fieldTopY;
        float fieldCenterX;

        if (TryGetLiveFieldBounds(out Bounds fieldBounds) &&
            TryWorldToRewardRootLocal(
                new Vector3(
                    fieldBounds.center.x,
                    fieldBounds.max.y,
                    0f),
                out Vector2 fieldTopLocal))
        {
            fieldTopY = fieldTopLocal.y;
            fieldCenterX = fieldTopLocal.x;
        }
        else
        {
            fieldTopY = root.yMin + root.height * 0.42f;
            fieldCenterX = 0f;
        }

        float availableHeight =
            root.yMax -
            screenSafeMargin -
            (fieldTopY + fieldScreenGap);

        float heightFit =
            availableHeight /
            Mathf.Max(1f, totalReferenceHeight * maxFocus);

        fitScale = Mathf.Min(
            1f,
            Mathf.Max(
                0.05f,
                Mathf.Min(widthFit, heightFit)));

        // preferredMinimumScale은 "가능하면" 지키되,
        // 그 값을 사용하면 Field를 덮는 경우에는 높이 제한을 우선합니다.
        if (fitScale < preferredMinimumScale &&
            totalReferenceHeight * preferredMinimumScale * maxFocus <= availableHeight &&
            screenWidth * preferredMinimumScale * maxFocus <= usableWidth)
        {
            fitScale = preferredMinimumScale;
        }

        float halfWidthAtMax =
            screenWidth * fitScale * maxFocus * 0.5f;

        float bottomExtentAtMax =
            (screenHeight * 0.5f + bottomExtra) *
            fitScale *
            maxFocus;

        float topExtentAtMax =
            screenHeight *
            fitScale *
            maxFocus *
            0.5f;

        float minCenterX = root.xMin + screenSafeMargin + halfWidthAtMax;
        float maxCenterX = root.xMax - screenSafeMargin - halfWidthAtMax;
        centerLocal.x = minCenterX <= maxCenterX
            ? Mathf.Clamp(fieldCenterX, minCenterX, maxCenterX)
            : 0f;

        centerLocal.y =
            fieldTopY +
            fieldScreenGap +
            bottomExtentAtMax;

        // 계산 오차가 생겨도 상단만 넘지 않게 마지막으로 위쪽 제한.
        float maxCenterY =
            root.yMax -
            screenSafeMargin -
            topExtentAtMax;

        centerLocal.y = Mathf.Min(centerLocal.y, maxCenterY);

        if (TryRewardRootLocalToWorld(centerLocal, out Vector3 world))
            worldAnchor = world;
        else if (Camera.main != null)
            worldAnchor = Camera.main.transform.position;
    }

    private bool TryWorldToRewardRootLocal(
        Vector3 world,
        out Vector2 local)
    {
        local = Vector2.zero;

        Camera camera = Camera.main;
        if (camera == null || rewardRootRect == null)
            return false;

        Vector3 screen = camera.WorldToScreenPoint(world);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rewardRootRect,
            screen,
            null,
            out local);
    }

    private bool TryRewardRootLocalToWorld(
        Vector2 local,
        out Vector3 world)
    {
        world = Vector3.zero;

        Camera camera = Camera.main;
        if (camera == null || rewardRootRect == null)
            return false;

        Vector3 uiWorld = rewardRootRect.TransformPoint(local);
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(
            null,
            uiWorld);

        float depth = Mathf.Abs(camera.transform.position.z);
        world = camera.ScreenToWorldPoint(
            new Vector3(screen.x, screen.y, depth));

        world.z = 0f;
        return true;
    }

    private void SyncUiToRailDuringTransition()
    {
        if (screenRailRoot == null ||
            !screenRailRoot.activeSelf ||
            overlayAnchorRect == null ||
            rewardRootRect == null)
            return;

        Camera camera = Camera.main;
        if (camera == null)
            return;

        Vector3 screen =
            camera.WorldToScreenPoint(
                screenRailRoot.transform.position);

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rewardRootRect,
                screen,
                null,
                out Vector2 local))
            return;

        float railScale = Mathf.Max(
            0.75f,
            Mathf.Max(
                Mathf.Abs(screenRailRoot.transform.localScale.x),
                Mathf.Abs(screenRailRoot.transform.localScale.y)));

        overlayAnchorRect.anchoredPosition = local;
        overlayAnchorRect.localScale =
            Vector3.one *
            (overlayFitScale * currentFocusScale * railScale);

        overlayAnchorRect.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                screenRailRoot.transform.eulerAngles.z);
    }

    private static bool TryGetLiveFieldBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        BattleWalkableField[] fields =
            FindObjectsByType<BattleWalkableField>(
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
                SpriteRenderer renderer =
                    field.GetComponent<SpriteRenderer>();

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

    // ------------------------------------------------------------------
    // Presenter
    // ------------------------------------------------------------------

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

        // HUD의 기본 presenterColor가 회색 placeholder용이므로
        // 실제 월드 Sprite는 원본 색을 보존합니다.
        presenterRenderer.color = Color.white;

        bool show =
            (currentMode != ShowMode.None ||
             desiredMode != ShowMode.None ||
             railTransitioning) &&
            presenterRailRoot != null &&
            presenterRailRoot.activeSelf;

        presenterRenderer.enabled =
            show && sprite != null;

        if (show &&
            sprite == BattleHudSpriteCache.DefaultSprite &&
            !warnedMissingPresenterSprite)
        {
            warnedMissingPresenterSprite = true;
            Debug.LogWarning(
                "[BattleShowWorldSetController] 사회자 Sprite가 비어 있습니다. " +
                "BattleShowPresentationManager.presenterFrames 또는 presenterFallbackSprite를 할당하세요.",
                this);
        }
    }

    private Sprite ResolvePresenterSprite()
    {
        if (hud != null && HudPresenterSpriteField != null)
        {
            Sprite hudSprite =
                HudPresenterSpriteField.GetValue(hud) as Sprite;

            if (hudSprite != null &&
                hudSprite != BattleHudSpriteCache.DefaultSprite)
                return hudSprite;
        }

        if (presentationManager == null)
            presentationManager =
                FindFirstObjectByType<BattleShowPresentationManager>();

        if (presentationManager != null &&
            PresenterFramesField != null)
        {
            Sprite[] frames =
                PresenterFramesField.GetValue(
                    presentationManager) as Sprite[];

            if (frames != null)
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    if (frames[i] != null)
                        return frames[i];
                }
            }
        }

        if (presenterFallbackSprite != null)
            return presenterFallbackSprite;

        if (legacyPresenterSprite != null &&
            legacyPresenterSprite != BattleHudSpriteCache.DefaultSprite)
            return legacyPresenterSprite;

        return BattleHudSpriteCache.DefaultSprite;
    }

    private void ApplyPresenterWorldScale(Sprite sprite)
    {
        if (presenterVisual == null)
            return;

        float spriteHeight =
            sprite != null
                ? Mathf.Abs(sprite.bounds.size.y)
                : 0f;

        float scale =
            spriteHeight > 0.0001f
                ? presenterWorldHeight / spriteHeight
                : 1f;

        bool flip =
            hud != null &&
            HudPresenterFlipField != null &&
            HudPresenterFlipField.GetValue(hud) is bool flipX &&
            flipX;

        presenterVisual.localScale =
            new Vector3(
                flip ? -scale : scale,
                scale,
                1f);
    }

    private Vector3 ResolvePresenterDockPosition()
    {
        Camera camera = Camera.main;

        Vector3 preferred = Vector3.zero;

        if (TryGetLiveFieldBounds(out Bounds fieldBounds))
        {
            preferred = new Vector3(
                fieldBounds.max.x - presenterFieldInsetX,
                fieldBounds.center.y + presenterFieldYOffset,
                0f);
        }
        else if (player != null)
        {
            preferred =
                player.transform.position +
                new Vector3(3f, 0f, 0f);
        }
        else if (camera != null)
        {
            preferred = camera.transform.position;
            preferred.z = 0f;
        }

        return ClampPresenterInsideCamera(preferred);
    }

    private Vector3 ClampPresenterInsideCamera(
        Vector3 preferred)
    {
        Camera camera = Camera.main;

        if (camera == null || !camera.orthographic)
            return preferred;

        float aspect = GetPresenterAspect();
        float halfHeight = presenterWorldHeight * 0.5f;
        float halfWidth =
            presenterWorldHeight *
            Mathf.Max(0.2f, aspect) *
            0.5f;

        Vector3 cameraCenter = camera.transform.position;
        float cameraHalfHeight = camera.orthographicSize;
        float cameraHalfWidth =
            cameraHalfHeight *
            Mathf.Max(0.1f, camera.aspect);

        float left =
            cameraCenter.x -
            cameraHalfWidth +
            presenterCameraMargin +
            halfWidth;

        float right =
            cameraCenter.x +
            cameraHalfWidth -
            presenterCameraMargin -
            halfWidth;

        float bottom =
            cameraCenter.y -
            cameraHalfHeight +
            presenterCameraMargin +
            halfHeight;

        float top =
            cameraCenter.y +
            cameraHalfHeight -
            presenterCameraMargin -
            halfHeight;

        Vector3 result = preferred;
        result.x =
            left <= right
                ? Mathf.Clamp(preferred.x, left, right)
                : cameraCenter.x;

        result.y =
            bottom <= top
                ? Mathf.Clamp(preferred.y, bottom, top)
                : cameraCenter.y;

        result.z = 0f;
        return result;
    }

    private float GetPresenterAspect()
    {
        Sprite sprite = ResolvePresenterSprite();

        if (sprite != null &&
            sprite.bounds.size.y > 0.0001f)
        {
            return Mathf.Max(
                0.2f,
                Mathf.Abs(
                    sprite.bounds.size.x /
                    sprite.bounds.size.y));
        }

        return 0.62f;
    }

    private void UpdatePresenterPosition()
    {
        if (presenterRailRoot == null ||
            !presenterRailRoot.activeSelf)
            return;

        if (railTransitioning)
            return;

        presenterDockPosition =
            ResolvePresenterDockPosition();

        presenterRailRoot.transform.position =
            presenterDockPosition;
    }

    private void UpdatePresenterSorting()
    {
        if (presenterRenderer == null)
            return;

        SpriteRenderer playerRenderer =
            player != null
                ? player.GetComponentInChildren<SpriteRenderer>(true)
                : null;

        if (playerRenderer != null)
        {
            presenterRenderer.sortingLayerID =
                playerRenderer.sortingLayerID;

            presenterRenderer.sortingOrder =
                Mathf.Max(
                    presenterSortingOrder,
                    playerRenderer.sortingOrder + 100);
        }
        else
        {
            presenterRenderer.sortingOrder =
                presenterSortingOrder;
        }
    }

    // ------------------------------------------------------------------
    // Cursor
    // ------------------------------------------------------------------

    private void UpdateCursorFocus()
    {
        if (overlayAnchorRect == null)
            return;

        bool focused = false;

        if (!railTransitioning &&
            currentMode != ShowMode.None)
        {
            RectTransform activeRect =
                currentMode == ShowMode.Map
                    ? mapScreenRect
                    : rewardScreenRect;

            if (activeRect != null &&
                activeRect.gameObject.activeInHierarchy)
            {
                focused =
                    RectTransformUtility.RectangleContainsScreenPoint(
                        activeRect,
                        Input.mousePosition,
                        null);
            }
        }

        float target =
            focused
                ? Mathf.Max(1f, cursorFocusScale)
                : 1f;

        float blend =
            1f -
            Mathf.Exp(
                -Mathf.Max(
                    0.5f,
                    cursorFocusSharpness) *
                Time.unscaledDeltaTime);

        currentFocusScale =
            Mathf.Lerp(
                currentFocusScale,
                target,
                blend);
    }

    // ------------------------------------------------------------------
    // Map START / selectable readability
    // ------------------------------------------------------------------

    private void EnsureMapReadabilityDecorations()
    {
        if (currentMode != ShowMode.Map ||
            mapSelectionRect == null ||
            !mapSelectionRect.gameObject.activeInHierarchy)
            return;

        if (Time.unscaledTime < nextMapDecorationTime)
            return;

        nextMapDecorationTime =
            Time.unscaledTime +
            Mathf.Max(0.02f, mapDecorationInterval);

        List<RectTransform> nodes =
            CollectStageNodes();

        if (nodes.Count == 0)
            return;

        for (int i = 0; i < nodes.Count; i++)
            DecorateSelectableNode(nodes[i]);

        if (mapSelectionRect.Find("StageStartMarker") != null)
            return;

        float minX = float.MaxValue;

        for (int i = 0; i < nodes.Count; i++)
            minX =
                Mathf.Min(
                    minX,
                    nodes[i].anchoredPosition.x);

        List<RectTransform> firstNodes = new();
        float firstYSum = 0f;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (Mathf.Abs(
                    nodes[i].anchoredPosition.x -
                    minX) > 1.5f)
                continue;

            firstNodes.Add(nodes[i]);
            firstYSum +=
                nodes[i].anchoredPosition.y;
        }

        if (firstNodes.Count == 0)
            return;

        float firstY =
            firstYSum /
            firstNodes.Count;

        float panelLeft =
            mapSelectionRect.rect.xMin;

        float desiredX =
            minX -
            mapStartGap;

        float minimumX =
            panelLeft +
            mapStartSize.x * 0.5f +
            16f;

        float startX =
            Mathf.Max(
                minimumX,
                desiredX);

        if (startX >
            minX -
            mapStartSize.x * 0.65f)
        {
            startX =
                Mathf.Min(
                    minX -
                    mapStartSize.x * 0.65f,
                    minimumX);
        }

        Vector2 startPosition =
            new(startX, firstY);

        CreateStartMarker(startPosition);

        Vector2 lineStart =
            startPosition +
            Vector2.right *
            (mapStartSize.x * 0.5f + 4f);

        for (int i = 0; i < firstNodes.Count; i++)
        {
            CreateMapLine(
                lineStart,
                firstNodes[i].anchoredPosition,
                "StartRouteLink");
        }
    }

    private List<RectTransform> CollectStageNodes()
    {
        List<RectTransform> result = new();

        if (mapSelectionRect == null)
            return result;

        for (int i = 0;
             i < mapSelectionRect.childCount;
             i++)
        {
            Transform child =
                mapSelectionRect.GetChild(i);

            if (child == null ||
                !child.name.StartsWith(
                    "StageNode_",
                    StringComparison.Ordinal))
                continue;

            if (child is RectTransform rect)
                result.Add(rect);
        }

        return result;
    }

    private void DecorateSelectableNode(
        RectTransform node)
    {
        if (node == null)
            return;

        Button button =
            node.GetComponent<Button>();

        if (button == null)
            return;

        node.sizeDelta =
            new Vector2(
                Mathf.Max(
                    node.sizeDelta.x,
                    selectableNodeMinSize),
                Mathf.Max(
                    node.sizeDelta.y,
                    selectableNodeMinSize));

        Outline outline =
            node.GetComponent<Outline>();

        if (outline != null)
        {
            outline.effectColor =
                selectableNodeAccent;

            outline.effectDistance =
                new Vector2(3f, -3f);
        }

        ColorBlock colors =
            button.colors;

        colors.highlightedColor =
            Color.white;

        colors.pressedColor =
            new Color(
                1f,
                0.92f,
                0.50f,
                1f);

        button.colors = colors;

        if (node.Find("ClickHint") == null)
            CreateClickHint(node);
    }

    private void CreateClickHint(
        RectTransform node)
    {
        Font font =
            Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");

        if (font == null)
            return;

        GameObject hint =
            new("ClickHint");

        hint.transform.SetParent(
            node,
            false);

        Text text =
            hint.AddComponent<Text>();

        text.font = font;
        text.text = "▼ CLICK!";
        text.fontSize = 11;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = selectableNodeAccent;
        text.raycastTarget = false;

        RectTransform rect =
            hint.GetComponent<RectTransform>();

        rect.anchorMin =
            rect.anchorMax =
                new Vector2(0.5f, 1f);

        rect.pivot =
            new Vector2(0.5f, 0f);

        rect.anchoredPosition =
            new Vector2(0f, 9f);

        rect.sizeDelta =
            new Vector2(100f, 22f);
    }

    private void CreateStartMarker(
        Vector2 position)
    {
        Font font =
            Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");

        if (font == null ||
            mapSelectionRect == null)
            return;

        GameObject marker =
            new("StageStartMarker");

        marker.transform.SetParent(
            mapSelectionRect,
            false);

        Image image =
            marker.AddComponent<Image>();

        image.color = mapStartColor;
        image.raycastTarget = false;

        Outline outline =
            marker.AddComponent<Outline>();

        outline.effectColor =
            Color.white;

        outline.effectDistance =
            new Vector2(2f, -2f);

        RectTransform rect =
            marker.GetComponent<RectTransform>();

        rect.anchorMin =
            rect.anchorMax =
                new Vector2(0.5f, 0.5f);

        rect.pivot =
            new Vector2(0.5f, 0.5f);

        rect.anchoredPosition =
            position;

        rect.sizeDelta =
            mapStartSize;

        GameObject label =
            new("Label");

        label.transform.SetParent(
            marker.transform,
            false);

        Text text =
            label.AddComponent<Text>();

        text.font = font;
        text.text = "START!  ▶";
        text.fontSize = 14;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;

        Stretch(
            label.GetComponent<RectTransform>());
    }

    private void CreateMapLine(
        Vector2 from,
        Vector2 to,
        string name)
    {
        if (mapSelectionRect == null)
            return;

        Vector2 delta = to - from;
        float length = delta.magnitude;

        if (length < 1f)
            return;

        GameObject line =
            new(name);

        line.transform.SetParent(
            mapSelectionRect,
            false);

        Image image =
            line.AddComponent<Image>();

        image.color =
            mapStartLinkColor;

        image.raycastTarget = false;

        RectTransform rect =
            line.GetComponent<RectTransform>();

        rect.anchorMin =
            rect.anchorMax =
                new Vector2(0.5f, 0.5f);

        rect.pivot =
            new Vector2(0.5f, 0.5f);

        rect.anchoredPosition =
            (from + to) * 0.5f;

        rect.sizeDelta =
            new Vector2(
                length,
                5f);

        rect.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                Mathf.Atan2(
                    delta.y,
                    delta.x) *
                Mathf.Rad2Deg);

        line.transform.SetAsFirstSibling();
    }

    private void AnimateMapReadability()
    {
        if (currentMode != ShowMode.Map ||
            mapSelectionRect == null)
            return;

        float pulse =
            0.78f +
            Mathf.Sin(
                Time.unscaledTime *
                5.5f) *
            0.22f;

        for (int i = 0;
             i < mapSelectionRect.childCount;
             i++)
        {
            Transform child =
                mapSelectionRect.GetChild(i);

            if (child == null ||
                !child.name.StartsWith(
                    "StageNode_",
                    StringComparison.Ordinal))
                continue;

            if (child.GetComponent<Button>() == null)
                continue;

            Transform hint =
                child.Find("ClickHint");

            if (hint == null)
                continue;

            Text text =
                hint.GetComponent<Text>();

            if (text == null)
                continue;

            Color color =
                selectableNodeAccent;

            color.a = pulse;
            text.color = color;
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void KillRailTweens()
    {
        if (screenRailRoot != null)
            screenRailRoot.transform.DOKill();

        if (presenterRailRoot != null)
            presenterRailRoot.transform.DOKill();
    }

    private static Vector2 NormalizeDirection(
        Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return Vector2.up;

        if (Mathf.Abs(direction.x) >=
            Mathf.Abs(direction.y))
        {
            return direction.x >= 0f
                ? Vector2.right
                : Vector2.left;
        }

        return direction.y >= 0f
            ? Vector2.up
            : Vector2.down;
    }

    private static RectTransform FindRectTransform(
        string objectName)
    {
        RectTransform[] rects =
            FindObjectsByType<RectTransform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];

            if (rect != null &&
                rect.name == objectName)
                return rect;
        }

        return null;
    }

    private static Image FindImage(
        string objectName)
    {
        Image[] images =
            FindObjectsByType<Image>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];

            if (image != null &&
                image.name == objectName)
                return image;
        }

        return null;
    }

    private static GameObject FindLegacyPresenterObject()
    {
        RectTransform[] rects =
            FindObjectsByType<RectTransform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];

            if (rect == null ||
                rect.name != "Presenter")
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

    private static void Stretch(
        RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

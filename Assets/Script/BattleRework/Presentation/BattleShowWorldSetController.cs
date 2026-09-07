using System.Collections;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward / Map 선택 연출을 ScreenSpace HUD가 아니라 실제 전투 월드의 쇼 세트로 운용합니다.
///
/// 핵심 원칙:
/// - Prize / Map 화면은 하나의 WorldSpace Canvas를 가진 월드 GameObject에 붙습니다.
/// - 화면 Rail과 사회자 Rail은 MapBlock의 WheelSlide 진입/퇴장을 그대로 재사용합니다.
///   따라서 필드 타일처럼 화면 밖에서 굴러오고, 도킹 반동 뒤에 멈추며, 상태가 끝나면 다시 빠져나갑니다.
/// - 사회자는 Canvas Image를 사용하지 않고 SpriteRenderer GameObject로 표시합니다.
/// - 기존 BattleHUD가 만드는 Reward UI의 실제 내용(버튼/드래그/맵 노드)은 버리지 않고
///   WorldSpace Canvas로 옮기므로 기존 선택 로직을 그대로 보존합니다.
/// - BattleShowPresentationManager가 SetPresenterSprite()로 갱신하는 presenterSprite 값은
///   월드 SpriteRenderer가 읽어 Sprite Sheet 애니메이션을 그대로 이어받습니다.
///
/// 별도 씬 세팅 없이도 동작하도록 Runtime Host를 만들지만, 씬에 직접 배치한 인스턴스가 있으면
/// Inspector 값을 사용하도록 중복 인스턴스는 자동 비활성화합니다.
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

    [Header("World Screen")]
    [Tooltip("기존 1120px TV 화면을 월드 크기로 환산할 PPU입니다. 기존 Map World Screen 기본값과 동일합니다.")]
    [SerializeField, Min(16f)] private float worldPixelsPerUnit = 88.5f;

    [Tooltip("현재 4x4 Base 중심을 기준으로 TV 세트 중심이 놓일 월드 오프셋입니다.")]
    [SerializeField] private Vector2 screenWorldOffset = new(0f, 5.30f);

    [Tooltip("WorldSpace Canvas의 가상 픽셀 크기입니다. TV 아래 Loadout Strip까지 포함할 수 있게 세로를 넉넉히 잡습니다.")]
    [SerializeField] private Vector2 worldCanvasSize = new(1180f, 820f);

    [Tooltip("TV 본체가 Canvas 중앙에서 위로 올라가는 픽셀 오프셋입니다.")]
    [SerializeField] private float screenCanvasYOffset = 90f;

    [Tooltip("아이템 선택 시 Loadout Strip의 Canvas 내 Y 위치입니다.")]
    [SerializeField] private float inventoryCanvasY = -300f;

    [Tooltip("TV World Canvas Sorting Order. 바닥/캐릭터보다 뒤에 놓는 기본값입니다.")]
    [SerializeField] private int screenSortingOrder = -50;

    [Header("Presenter World Sprite")]
    [Tooltip("현재 4x4 Base 중심을 기준으로 사회자가 서는 위치입니다.")]
    [SerializeField] private Vector2 presenterWorldOffset = new(5.35f, 1.05f);

    [Tooltip("사회자 Sprite를 월드에서 이 높이로 정규화합니다.")]
    [SerializeField, Min(0.5f)] private float presenterWorldHeight = 3.6f;

    [Tooltip("사회자 SpriteRenderer Sorting Order. Player 앞/옆에 읽히도록 충분히 높게 둡니다.")]
    [SerializeField] private int presenterSortingOrder = 30;

    [Header("Mechanical Entry / Exit")]
    [Tooltip("TV가 위 레일에서 내려오는 이동 방향입니다. MapBlock의 WheelSlide를 그대로 사용합니다.")]
    [SerializeField] private Vector2 screenRailDirection = Vector2.up;

    [Tooltip("사회자가 오른쪽 세트 밖에서 들어오는 이동 방향입니다.")]
    [SerializeField] private Vector2 presenterRailDirection = Vector2.right;

    [SerializeField, Min(0.05f)] private float screenEntryDuration = 0.62f;
    [SerializeField, Min(0.05f)] private float presenterEntryDuration = 0.48f;
    [SerializeField, Min(2f)] private float screenRailDistance = 16f;
    [SerializeField, Min(2f)] private float presenterRailDistance = 10f;
    [SerializeField, Range(0f, 1.5f)] private float screenImpactStrength = 0.78f;
    [SerializeField, Range(0f, 1.5f)] private float presenterImpactStrength = 0.48f;

    [Header("Cursor Focus")]
    [Tooltip("커서가 현재 TV 화면 안에 있을 때 전체 세트를 확대합니다.")]
    [SerializeField, Range(1f, 1.30f)] private float cursorFocusScale = 1.12f;
    [SerializeField, Min(0.5f)] private float cursorFocusSharpness = 6.5f;

    private BattleRunManager runManager;
    private BattleHUD hud;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;

    private GameObject screenRailRoot;
    private Transform screenFocusRoot;
    private Canvas worldCanvas;
    private RectTransform worldCanvasRect;
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
    private Sprite lastPresenterSprite;

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

            GameObject focus = new("ScreenFocusRoot");
            focus.transform.SetParent(screenRailRoot.transform, false);
            screenFocusRoot = focus.transform;

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
            float scale = 1f / Mathf.Max(16f, worldPixelsPerUnit);
            worldCanvasRect.localScale = new Vector3(scale, scale, 1f);
            worldCanvasRect.localPosition = Vector3.zero;
            worldCanvasRect.localRotation = Quaternion.identity;

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
        }

        screenRailRoot.SetActive(false);
        presenterRailRoot.SetActive(false);
    }

    private void MoveHudContentIntoWorldSet()
    {
        if (worldCanvasRect == null)
            return;

        ReparentScreenRect(rewardScreenRect, new Vector2(0f, screenCanvasYOffset));
        ReparentScreenRect(mapScreenRect, new Vector2(0f, screenCanvasYOffset));

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
    }

    private static void ReparentScreenRect(RectTransform rect, Vector2 anchoredPosition)
    {
        if (rect == null || instance == null || instance.worldCanvasRect == null)
            return;

        rect.SetParent(instance.worldCanvasRect, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
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

    private void SuppressLegacyScreenSpaceShowVisuals()
    {
        // 전장 전체를 덮던 ScreenSpace 필터는 쇼 세트가 월드에 존재하게 된 뒤에는 사용하지 않습니다.
        if (legacyFieldFilter != null)
        {
            legacyFieldFilter.raycastTarget = false;
            legacyFieldFilter.enabled = false;
        }

        // 기존 UI 조명은 화면 좌표 기반이므로 렌더만 끕니다.
        // BattleShowPresentationManager가 Sprite를 갱신해도 월드 세트와 충돌하지 않습니다.
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

        // 사회자는 이제 SpriteRenderer만 사용합니다. HUD 내부 Image 오브젝트 자체를 제거합니다.
        // BattleHUD.SetPresenterSprite()는 presenterSprite 필드 갱신을 계속 수행하므로
        // BattleShowPresentationManager의 기존 Sprite Sheet 재생 API는 그대로 살아 있습니다.
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

        // BattleHUD / BattleShowPresentationManager가 같은 프레임에 옛 UI를 다시 켜더라도
        // 렌더 직전에 확실하게 비활성화합니다.
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
                // HUD가 상태 변경과 함께 기존 화면을 먼저 꺼도, 기계식 퇴장이 끝날 때까지
                // 현재 화면을 다시 살려 두어 '퍽 꺼지는' 컷을 없앱니다.
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

            // 진입 도중 상태가 또 바뀌었으면 Raycast를 열지 않고 바로 다음 기계식 전환으로 갑니다.
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
        if (screenRailRoot != null)
            screenRailRoot.SetActive(true);
        if (presenterRailRoot != null)
            presenterRailRoot.SetActive(true);

        if (screenFocusRoot != null)
            screenFocusRoot.localScale = Vector3.one * currentFocusScale;
    }

    private void PlayRailEnter()
    {
        if (screenRailBlock != null)
            screenRailBlock.PlayEnter(ResolveScreenWorldPosition(), NormalizeDirection(screenRailDirection));
        if (presenterRailBlock != null)
            presenterRailBlock.PlayEnter(ResolvePresenterWorldPosition(), NormalizeDirection(presenterRailDirection));
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

        // BattleSpatialMapController가 직접 들고 있는 실제 노드 Root입니다.
        // Map 모드에서 부모 화면과 함께 활성 상태를 보장합니다.
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

    private Vector3 ResolveScreenWorldPosition()
    {
        Vector3 center = ResolveBaseCenter();
        center += (Vector3)screenWorldOffset;
        return center;
    }

    private Vector3 ResolvePresenterWorldPosition()
    {
        Vector3 center = ResolveBaseCenter();
        center += (Vector3)presenterWorldOffset;
        return center;
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

        if (screenRailRoot != null && screenRailRoot.activeSelf)
            screenRailRoot.transform.position = ResolveScreenWorldPosition();
        if (presenterRailRoot != null && presenterRailRoot.activeSelf)
            presenterRailRoot.transform.position = ResolvePresenterWorldPosition();
    }

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
}

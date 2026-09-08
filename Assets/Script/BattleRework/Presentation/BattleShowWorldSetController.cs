using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward / Map 공용 월드 쇼 세트.
/// 공간 기준은 BattleStageTransitionController가 확정한 Player 4x4 중심 하나만 사용합니다.
/// Reward <-> Map에서는 TV 내용만 바뀌며 Camera/TV/Presenter/Floor 배치는 유지됩니다.
///
/// Show 진입 순서:
/// 1) 4x4 오른쪽 Show Floor가 오른쪽 레일에서 순차 도킹
/// 2) Presenter는 마지막 Presenter Floor에 실려 함께 진입
/// 3) Floor 도킹 완료 후 TV가 위쪽 레일에서 진입
/// </summary>
[DefaultExecutionOrder(20000)]
[DisallowMultipleComponent]
public sealed class BattleShowWorldSetController : MonoBehaviour
{
    private enum ShowMode { None, Reward, Map }

    private static BattleShowWorldSetController instance;

    [Header("Shared TV")]
    [SerializeField] private Vector2 tvCanvasSize = new(1120f, 560f);
    [SerializeField, Min(32f)] private float tvPixelsPerUnit = 122f;
    [SerializeField] private float tvCenterYOffset = 3.65f;
    [SerializeField, Min(0.05f)] private float tvEntryDuration = 0.52f;
    [SerializeField, Min(2f)] private float tvRailDistance = 10f;
    [SerializeField, Range(1f, 1.2f)] private float tvPointerFocusScale = 1.08f;
    [SerializeField, Min(1f)] private float tvPointerFocusSharpness = 7f;

    [Header("Shared Show Floor")]
    [SerializeField, Range(4, 12)] private int showFloorWidth = 8;
    [SerializeField, Range(2, 6)] private int showFloorDepth = 4;
    [SerializeField, Range(2, 5)] private int showFloorPieceCount = 3;
    [SerializeField, Min(0.05f)] private float showFloorEntryDuration = 0.62f;
    [SerializeField, Min(0f)] private float showFloorEntryStagger = 0.16f;
    [SerializeField, Min(2f)] private float showFloorRailDistance = 12f;
    [SerializeField, Range(0f, 2f)] private float showFloorImpactStrength = 1.05f;
    [SerializeField] private int showFloorFallbackSortingOrder = -18;

    [Header("Shared Camera Frame")]
    [SerializeField] private Vector2 showCameraOffset = new(0f, 0.75f);
    [SerializeField, Min(0.1f)] private float showCameraSize = 6.1f;

    [Header("Presenter")]
    [SerializeField] private Sprite presenterFallbackSprite;
    [SerializeField, Min(0.5f)] private float presenterWorldHeight = 3.4f;
    [SerializeField] private float presenterPadYOffset = 0.55f;
    [SerializeField, Min(1)] private int presenterFrontOrder = 20;

    [Header("Optional Three Characters")]
    [Tooltip("레퍼런스의 TV 앞 캐릭터 3자리. Sprite가 할당된 자리만 표시하며 Player는 복제하지 않습니다.")]
    [SerializeField] private Sprite[] contestantSprites = new Sprite[3];
    [SerializeField] private Vector2[] contestantLocalOffsets =
    {
        new(-1.2f, -0.25f),
        new(0f, -0.25f),
        new(1.2f, -0.25f)
    };
    [SerializeField, Min(0.25f)] private float contestantWorldHeight = 1.4f;
    [SerializeField, Min(1)] private int contestantFrontOrder = 10;

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
    private readonly SpriteRenderer[] contestantRenderers = new SpriteRenderer[3];
    private readonly List<MapBlock> showFloorPieces = new();

    private Coroutine bindRoutine;
    private Coroutine transitionRoutine;
    private ShowMode currentMode;
    private ShowMode desiredMode;

    private bool bound;
    private bool stageTransitioning;
    private bool externalGate;
    private bool hasExplicitStageAnchor;
    private bool dockCaptured;
    private bool presenterWarningShown;

    private Vector3 explicitStageAnchor;
    private Vector3 stageAnchorWorld;
    private Vector3 cameraTargetWorld;
    private Vector3 tvDestinationWorld;
    private Sprite lastPresenterSprite;

    public bool IsShowActive => currentMode != ShowMode.None || stageTransitioning;
    public bool HasCameraAnchor => dockCaptured && !externalGate && stageRoot != null && stageRoot.activeSelf;
    public Vector3 CameraTargetWorld => cameraTargetWorld;
    public float ShowCameraSize => Mathf.Max(0.1f, showCameraSize);

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

        if (tvRect != null)
            tvRect.DOKill();
        KillShowFloorTweens();
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

        stageRoot = new GameObject("BattleShowSharedStage");
        stageRoot.transform.SetParent(transform, false);

        tvObject = new GameObject("BattleShowSharedWorldTV");
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

        BuildPresenter();
        BuildContestants();
        stageRoot.SetActive(false);
    }

    private void BuildPresenter()
    {
        GameObject go = new("PresenterWorldSprite");
        go.transform.SetParent(stageRoot.transform, false);
        presenterTransform = go.transform;
        presenterRenderer = go.AddComponent<SpriteRenderer>();
        presenterRenderer.color = Color.white;
        presenterRenderer.enabled = false;
    }

    private void BuildContestants()
    {
        EnsureContestantArrays();
        for (int i = 0; i < contestantRenderers.Length; i++)
        {
            GameObject go = new($"ShowCharacter_{i + 1}");
            go.transform.SetParent(stageRoot.transform, false);
            go.transform.localPosition = contestantLocalOffsets[i];
            contestantRenderers[i] = go.AddComponent<SpriteRenderer>();
        }
        RefreshContestants();
    }

    private void EnsureContestantArrays()
    {
        if (contestantSprites == null || contestantSprites.Length != 3)
        {
            Sprite[] next = new Sprite[3];
            if (contestantSprites != null)
            {
                for (int i = 0; i < Mathf.Min(3, contestantSprites.Length); i++)
                    next[i] = contestantSprites[i];
            }
            contestantSprites = next;
        }

        if (contestantLocalOffsets == null || contestantLocalOffsets.Length != 3)
        {
            contestantLocalOffsets = new[]
            {
                new Vector2(-1.2f, -0.25f),
                Vector2.zero,
                new Vector2(1.2f, -0.25f)
            };
        }
    }

    private void ReparentScreens()
    {
        ReparentToTv(rewardScreen);
        ReparentToTv(mapScreen);
        if (legacyMapCanvas != null) legacyMapCanvas.SetActive(false);
        if (rewardLoadoutStrip != null) rewardLoadoutStrip.gameObject.SetActive(false);
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
        if (rewardLoadoutStrip != null) rewardLoadoutStrip.gameObject.SetActive(false);
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
        RefreshContestants();
        UpdatePointerTracking();

        if (transitionRoutine == null && desiredMode != currentMode)
            transitionRoutine = StartCoroutine(Transition());
    }

    private void LateUpdate()
    {
        if (!bound)
            return;

        DisableImage(legacyPresenter);
        if (legacyMapCanvas != null && legacyMapCanvas.activeSelf) legacyMapCanvas.SetActive(false);
        if (rewardLoadoutStrip != null && rewardLoadoutStrip.gameObject.activeSelf) rewardLoadoutStrip.gameObject.SetActive(false);
        if (tvCanvas != null && tvCanvas.worldCamera != Camera.main) tvCanvas.worldCamera = Camera.main;

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

            if (currentMode != ShowMode.None && next != ShowMode.None)
            {
                currentMode = next;
                SetContent(currentMode);
                continue;
            }

            if (currentMode != ShowMode.None && next == ShowMode.None)
            {
                yield return ExitSharedStage();
                currentMode = ShowMode.None;
                SetContent(ShowMode.None);
                dockCaptured = false;
                continue;
            }

            if (currentMode == ShowMode.None && next != ShowMode.None)
            {
                CaptureStageDock();
                currentMode = next;
                SetContent(currentMode);

                stageRoot.SetActive(true);
                stageRoot.transform.position = stageAnchorWorld;
                stageRoot.transform.localScale = Vector3.one;
                dockCaptured = true;

                BuildShowFloor();
                presentation?.PlayPresenterAnimation(true);
                UpdatePresenter();

                float floorDuration = PlayShowFloorEnter();
                if (floorDuration > 0f)
                    yield return new WaitForSecondsRealtime(floorDuration + 0.04f);

                BattleDockHandleVisibilityController.RefreshNow();
                yield return PlayTvEnter();
            }
        }

        stageTransitioning = false;
        SetInteraction(currentMode != ShowMode.None);
        transitionRoutine = null;
    }

    private IEnumerator ExitSharedStage()
    {
        battleCamera?.SetShowCursorTracking(false, Vector2.zero);
        SetInteraction(false);

        float tvDuration = PlayTvExit();
        if (tvDuration > 0f)
            yield return new WaitForSecondsRealtime(tvDuration * 0.55f);

        float floorDuration = PlayShowFloorExit();
        float wait = Mathf.Max(Mathf.Max(0f, tvDuration * 0.45f), floorDuration);
        if (wait > 0f)
            yield return new WaitForSecondsRealtime(wait + 0.03f);

        if (tvObject != null)
            tvObject.SetActive(false);
        ClearShowFloorImmediate();
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
        float combinedCenterX = Mathf.Max(0, showFloorWidth) * 0.5f;

        tvDestinationWorld = stageAnchorWorld + new Vector3(combinedCenterX, tvCenterYOffset, 0f);
        cameraTargetWorld = stageAnchorWorld + new Vector3(
            combinedCenterX + showCameraOffset.x,
            showCameraOffset.y,
            0f);
    }

    private void BuildShowFloor()
    {
        ClearShowFloorImmediate();
        ResolveSystems();

        int totalWidth = Mathf.Clamp(showFloorWidth, 4, 12);
        int depth = Mathf.Clamp(showFloorDepth, 2, 6);
        int pieceCount = Mathf.Clamp(showFloorPieceCount, 2, Mathf.Min(5, totalWidth));

        Vector3 baseLowerLeft = stageAnchorWorld + new Vector3(
            -(RoomBaseTemplate.FixedBaseTiles - 1) * 0.5f,
            -(RoomBaseTemplate.FixedBaseTiles - 1) * 0.5f,
            0f);

        int consumed = 0;
        MapBlock presenterCarrier = null;
        int presenterCarrierWidth = 1;

        for (int i = 0; i < pieceCount; i++)
        {
            int piecesLeft = pieceCount - i;
            int tilesLeft = totalWidth - consumed;
            int width = i == pieceCount - 1
                ? tilesLeft
                : Mathf.Max(1, Mathf.CeilToInt(tilesLeft / (float)piecesLeft));

            Vector3 destination = baseLowerLeft + new Vector3(
                RoomBaseTemplate.FixedBaseTiles + consumed,
                0f,
                0f);

            bool presenterPiece = i == pieceCount - 1;
            MapBlock piece = CreateShowFloorPiece(i, width, depth, destination, presenterPiece);
            if (piece != null)
            {
                showFloorPieces.Add(piece);
                if (presenterPiece)
                {
                    presenterCarrier = piece;
                    presenterCarrierWidth = width;
                }
            }

            consumed += width;
        }

        for (int i = 0; i < showFloorPieces.Count; i++)
        {
            MapBlock piece = showFloorPieces[i];
            if (piece != null)
                piece.gameObject.SetActive(true);
        }

        if (presentation != null)
        {
            for (int i = 0; i < showFloorPieces.Count; i++)
            {
                MapBlock piece = showFloorPieces[i];
                if (piece != null)
                    presentation.ApplySlidingTemplate(piece, Vector2.left, true);
            }
        }

        AttachPresenterToCarrier(presenterCarrier, presenterCarrierWidth, depth);
        BattleDockHandleVisibilityController.RefreshNow();
    }

    private MapBlock CreateShowFloorPiece(int index, int width, int height, Vector3 destination, bool presenterPiece)
    {
        GameObject root = new(presenterPiece
            ? $"PresenterShowFloorPiece_{index}_{width}x{height}"
            : $"ShowFloorPiece_{index}_{width}x{height}");
        root.transform.SetParent(stageRoot.transform, true);
        root.SetActive(false);

        GameObject visualObject = new("Visual");
        visualObject.transform.SetParent(root.transform, false);
        Transform visual = visualObject.transform;

        MapBlock block = root.AddComponent<MapBlock>();
        block.ConfigureRuntimeDockingBlock(
            visual,
            false,
            showFloorImpactStrength,
            showFloorEntryDuration,
            showFloorRailDistance);

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
                renderer.sortingOrder = showFloorFallbackSortingOrder;
                renderer.color = presentation != null && presentation.ActiveFloorTemplate != null
                    ? presentation.ActiveFloorTemplate.FloorTint
                    : Color.white;
                createdAnyTile = true;
            }
        }

        if (!createdAnyTile)
        {
            Debug.LogWarning(
                "[BattleShowWorldSetController] Show Floor용 Floor Variant가 없습니다. " +
                "BattleShowPresentationManager Default Floor Template을 확인하세요.",
                this);
        }

        block.SnapTo(destination);
        return block;
    }

    private void AttachPresenterToCarrier(MapBlock carrier, int width, int depth)
    {
        if (presenterTransform == null)
            return;

        if (carrier == null)
        {
            presenterTransform.SetParent(stageRoot.transform, false);
            presenterTransform.localPosition = new Vector3(
                RoomBaseTemplate.FixedBaseTiles + Mathf.Max(1, showFloorWidth) - 1f,
                0f,
                0f);
            return;
        }

        presenterTransform.SetParent(carrier.transform, false);
        presenterTransform.localPosition = new Vector3(
            Mathf.Max(0f, (width - 1) * 0.5f),
            Mathf.Max(0f, (depth - 1) * 0.5f) + presenterPadYOffset,
            0f);
    }

    private float PlayShowFloorEnter()
    {
        float longest = 0f;
        float stagger = Mathf.Max(0f, showFloorEntryStagger);

        for (int i = 0; i < showFloorPieces.Count; i++)
        {
            MapBlock piece = showFloorPieces[i];
            if (piece == null)
                continue;

            float delay = stagger * i;
            Vector3 destination = piece.EntryDestination;
            piece.PlayEnter(destination, Vector2.right, delay);
            longest = Mathf.Max(longest, piece.GetEntryDuration(delay));
        }

        return longest;
    }

    private float PlayShowFloorExit()
    {
        float longest = 0f;
        float stagger = Mathf.Max(0f, showFloorEntryStagger * 0.7f);

        for (int i = showFloorPieces.Count - 1; i >= 0; i--)
        {
            MapBlock piece = showFloorPieces[i];
            if (piece == null)
                continue;

            int reverseIndex = showFloorPieces.Count - 1 - i;
            float delay = stagger * reverseIndex;
            Tween tween = piece.PlayExit(Vector2.right);
            if (tween != null && delay > 0f)
                tween.SetDelay(delay);
            longest = Mathf.Max(longest, delay + piece.ExitDuration);
        }

        return longest;
    }

    private IEnumerator PlayTvEnter()
    {
        if (tvRect == null || tvObject == null)
            yield break;

        tvRect.DOKill();
        tvRect.localScale = tvBaseScale;
        tvRect.position = tvDestinationWorld + Vector3.up * Mathf.Max(2f, tvRailDistance);
        tvObject.SetActive(true);

        float duration = Mathf.Max(0.05f, tvEntryDuration);
        tvRect.DOMove(tvDestinationWorld, duration).SetEase(Ease.OutCubic).SetUpdate(true);
        yield return new WaitForSecondsRealtime(duration);
        tvRect.position = tvDestinationWorld;
    }

    private float PlayTvExit()
    {
        if (tvRect == null || tvObject == null || !tvObject.activeSelf)
            return 0f;

        tvRect.DOKill();
        float duration = Mathf.Max(0.05f, tvEntryDuration);
        Vector3 destination = tvRect.position + Vector3.up * Mathf.Max(2f, tvRailDistance);
        tvRect.DOMove(destination, duration).SetEase(Ease.InCubic).SetUpdate(true);
        return duration;
    }

    private void ClearShowFloorImmediate()
    {
        if (presenterTransform != null && stageRoot != null)
            presenterTransform.SetParent(stageRoot.transform, true);

        for (int i = 0; i < showFloorPieces.Count; i++)
        {
            MapBlock piece = showFloorPieces[i];
            if (piece == null)
                continue;
            piece.transform.DOKill();
            Destroy(piece.gameObject);
        }
        showFloorPieces.Clear();

        if (presenterRenderer != null)
            presenterRenderer.enabled = false;
    }

    private void KillShowFloorTweens()
    {
        for (int i = 0; i < showFloorPieces.Count; i++)
        {
            MapBlock piece = showFloorPieces[i];
            if (piece != null)
                piece.transform.DOKill();
        }
    }

    private void SetContent(ShowMode mode)
    {
        if (rewardScreen != null) rewardScreen.gameObject.SetActive(mode == ShowMode.Reward);
        if (mapScreen != null) mapScreen.gameObject.SetActive(mode == ShowMode.Map);
        if (mapContent != null && mode == ShowMode.Map) mapContent.gameObject.SetActive(true);
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
            stageTransitioning || externalGate || stageRoot == null || !stageRoot.activeSelf ||
            tvObject == null || !tvObject.activeSelf)
        {
            battleCamera?.SetShowCursorTracking(false, Vector2.zero);
            ApplyTvFocus(false);
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
        ApplyTvFocus(inside);
    }

    private void ApplyTvFocus(bool focused)
    {
        if (tvRect == null)
            return;

        float target = focused ? Mathf.Max(1f, tvPointerFocusScale) : 1f;
        float t = 1f - Mathf.Exp(-Mathf.Max(1f, tvPointerFocusSharpness) * Time.unscaledDeltaTime);
        tvRect.localScale = Vector3.Lerp(tvRect.localScale, tvBaseScale * target, t);
    }

    private void UpdatePresenter()
    {
        if (presenterRenderer == null || presenterTransform == null)
            return;

        if (legacyPresenter == null)
            legacyPresenter = FindPresenterImage();

        Sprite sprite = legacyPresenter != null ? legacyPresenter.sprite : null;
        if (sprite == BattleHudSpriteCache.DefaultSprite)
            sprite = null;
        if (sprite == null)
            sprite = presenterFallbackSprite;

        if (sprite != lastPresenterSprite)
        {
            lastPresenterSprite = sprite;
            presenterRenderer.sprite = sprite;

            if (sprite != null)
            {
                float scale = presenterWorldHeight / Mathf.Max(0.0001f, Mathf.Abs(sprite.bounds.size.y));
                bool flip = legacyPresenter != null && legacyPresenter.rectTransform.localScale.x < 0f;
                presenterTransform.localScale = new Vector3(flip ? -scale : scale, scale, 1f);
                presenterWarningShown = false;
            }
        }

        bool carrierVisible = presenterTransform.parent != null && presenterTransform.parent.gameObject.activeInHierarchy;
        presenterRenderer.enabled = sprite != null && IsShowActive && carrierVisible;

        if (sprite == null && IsShowActive && !presenterWarningShown)
        {
            presenterWarningShown = true;
            Debug.LogWarning(
                "[BattleShowWorldSetController] Presenter Sprite가 없습니다. " +
                "BattleShowPresentationManager.presenterFrames 또는 presenterFallbackSprite를 확인하세요.",
                this);
        }
    }

    private void RefreshContestants()
    {
        EnsureContestantArrays();
        for (int i = 0; i < contestantRenderers.Length; i++)
        {
            SpriteRenderer renderer = contestantRenderers[i];
            if (renderer == null)
                continue;

            Sprite sprite = contestantSprites[i];
            renderer.sprite = sprite;
            renderer.enabled = sprite != null && IsShowActive;
            renderer.transform.localPosition = contestantLocalOffsets[i];

            if (sprite == null)
                continue;

            float scale = contestantWorldHeight / Mathf.Max(0.0001f, Mathf.Abs(sprite.bounds.size.y));
            renderer.transform.localScale = Vector3.one * scale;
        }
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
            tvCanvas.sortingOrder = Mathf.Min(fieldOrder + 1, playerOrder - 1);
        }

        if (presenterRenderer != null)
        {
            presenterRenderer.sortingLayerID = playerRenderer.sortingLayerID;
            presenterRenderer.sortingOrder = playerOrder + Mathf.Max(1, presenterFrontOrder);
        }

        for (int i = 0; i < contestantRenderers.Length; i++)
        {
            SpriteRenderer renderer = contestantRenderers[i];
            if (renderer == null)
                continue;
            renderer.sortingLayerID = playerRenderer.sortingLayerID;
            renderer.sortingOrder = playerOrder + Mathf.Max(1, contestantFrontOrder);
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

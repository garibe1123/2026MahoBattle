using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward / Map에서 공통으로 사용하는 하나의 월드 TV 쇼 세트입니다.
///
/// 불변 규칙:
/// 1) TV Transform / Scale / Camera Anchor는 Reward와 Map에서 동일합니다.
/// 2) Reward <-> Map 전환은 TV 내용만 교체합니다.
/// 3) Player는 기존 Player GameObject 그대로 사용하며 복제하지 않습니다.
/// 4) Presenter와 선택적인 3명의 참가자만 World SpriteRenderer로 관리합니다.
/// 5) 커서 추적은 TV 전체를 기준으로 공통 처리합니다.
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

    [Header("Shared TV")]
    [SerializeField] private Vector2 tvCanvasSize = new(1120f, 560f);
    [SerializeField, Min(32f)] private float tvPixelsPerUnit = 122f;
    [SerializeField, Min(0f)] private float tvFieldOverlap = 0.35f;
    [SerializeField, Range(1f, 1.2f)] private float tvPointerFocusScale = 1.08f;
    [SerializeField, Min(1f)] private float tvPointerFocusSharpness = 7f;

    [Header("Shared Camera Frame")]
    [Tooltip("TV 중심을 기준으로 한 선택 쇼 카메라 중심 위치입니다. Reward / Map 공용입니다.")]
    [SerializeField] private Vector2 showCameraLocalOffset = new(0.75f, -2.25f);
    [Tooltip("Reward / Map이 공통으로 사용하는 Orthographic Size입니다.")]
    [SerializeField, Min(0.1f)] private float showCameraSize = 6.1f;

    [Header("Stage Entry")]
    [SerializeField] private Vector2 stageRailDirection = Vector2.up;
    [SerializeField, Min(0.05f)] private float stageEntryDuration = 0.58f;
    [SerializeField, Min(2f)] private float stageRailDistance = 12f;
    [SerializeField, Range(0f, 1.5f)] private float stageImpactStrength = 0.65f;

    [Header("Presenter - World Sprite")]
    [SerializeField] private Vector2 presenterLocalOffset = new(5.0f, -2.2f);
    [SerializeField, Min(0.5f)] private float presenterWorldHeight = 3.4f;
    [SerializeField, Min(1)] private int presenterFrontOrder = 20;

    [Header("Optional Three Characters")]
    [Tooltip("레퍼런스의 TV 앞 3캐릭터 자리입니다. 비어 있으면 아무것도 만들지 않습니다. Player fallback은 사용하지 않습니다.")]
    [SerializeField] private Sprite[] contestantSprites = new Sprite[3];
    [SerializeField] private Vector2[] contestantLocalOffsets =
    {
        new(-4.2f, -2.45f),
        new(-2.8f, -2.45f),
        new(-1.4f, -2.45f)
    };
    [SerializeField, Min(0.25f)] private float contestantWorldHeight = 1.4f;
    [SerializeField, Min(1)] private int contestantFrontOrder = 10;

    [Header("Map Start Marker")]
    [SerializeField] private Vector2 mapStartSize = new(92f, 46f);
    [SerializeField, Min(20f)] private float mapStartGap = 108f;

    private BattleRunManager runManager;
    private BattleHUD hud;
    private PlayerController player;
    private BattleCameraController cameraController;
    private BattleShowPresentationManager presentationManager;

    private RectTransform rewardScreen;
    private RectTransform mapScreen;
    private RectTransform mapContent;
    private RectTransform equipmentDock;
    private RectTransform rewardLoadoutStrip;
    private GameObject legacyMapCanvasRoot;
    private Image legacyPresenterImage;
    private Image legacyFieldFilter;
    private Image legacyPlayerSpotlight;
    private Image legacyPresenterSpotlight;

    private GameObject stageRoot;
    private MapBlock stageBlock;
    private Canvas tvCanvas;
    private RectTransform tvRect;
    private CanvasGroup tvGroup;
    private Vector3 tvBaseScale;

    private Transform presenterTransform;
    private SpriteRenderer presenterRenderer;
    private readonly SpriteRenderer[] contestantRenderers = new SpriteRenderer[3];

    private Coroutine bindRoutine;
    private Coroutine transitionRoutine;
    private ShowMode currentMode;
    private ShowMode desiredMode;
    private bool bound;
    private bool stageTransitioning;
    private bool dockCaptured;
    private Vector3 stageDockPosition;
    private Sprite lastPresenterSprite;
    private bool presenterWarningShown;

    public bool IsShowActive => desiredMode != ShowMode.None || currentMode != ShowMode.None || stageTransitioning;
    public bool HasCameraAnchor => dockCaptured;
    public Vector3 CameraTargetWorld => stageDockPosition + (Vector3)showCameraLocalOffset;
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
        cameraController?.SetShowCursorTracking(false, Vector2.zero);

        if (stageRoot != null)
            stageRoot.transform.DOKill();
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
            ResolveUiReferences();

            if (runManager != null && hud != null && rewardScreen != null && mapScreen != null && mapContent != null)
            {
                BuildStage();
                MoveScreensIntoSharedTv();
                PrepareCombatDropSlots();
                DisableLegacyPresentation();
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
        if (cameraController == null)
            cameraController = FindFirstObjectByType<BattleCameraController>();
        if (presentationManager == null)
            presentationManager = FindFirstObjectByType<BattleShowPresentationManager>();
    }

    private void ResolveUiReferences()
    {
        if (rewardScreen == null)
            rewardScreen = FindRect("PrizeSelectionScreen");
        if (mapScreen == null)
            mapScreen = FindRect("MapSelectionScreen");
        if (mapContent == null)
            mapContent = FindRect("MapSelectionContent");
        if (equipmentDock == null)
            equipmentDock = FindRect("EquipmentDock");
        if (rewardLoadoutStrip == null)
            rewardLoadoutStrip = FindRect("RewardLoadoutStrip");

        if (legacyPresenterImage == null)
            legacyPresenterImage = FindPresenterImage();
        if (legacyFieldFilter == null)
            legacyFieldFilter = FindImage("FieldBroadcastFilter");
        if (legacyPlayerSpotlight == null)
            legacyPlayerSpotlight = FindImage("PlayerFloorSpotlight");
        if (legacyPresenterSpotlight == null)
            legacyPresenterSpotlight = FindImage("PresenterFloorSpotlight");

        if (legacyMapCanvasRoot == null)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i] != null && canvases[i].name == "BattleMapSelectionWorldCanvas")
                {
                    legacyMapCanvasRoot = canvases[i].gameObject;
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

        GameObject tvObject = new("BattleShowSharedWorldTV");
        tvObject.transform.SetParent(stageRoot.transform, false);

        tvCanvas = tvObject.AddComponent<Canvas>();
        tvCanvas.renderMode = RenderMode.WorldSpace;
        tvCanvas.overrideSorting = true;
        tvCanvas.worldCamera = Camera.main;
        tvObject.AddComponent<GraphicRaycaster>();

        tvGroup = tvObject.AddComponent<CanvasGroup>();
        tvGroup.alpha = 1f;
        tvGroup.interactable = false;
        tvGroup.blocksRaycasts = false;

        tvRect = tvObject.GetComponent<RectTransform>();
        tvRect.sizeDelta = tvCanvasSize;
        tvRect.pivot = new Vector2(0.5f, 0.5f);
        float scale = 1f / Mathf.Max(32f, tvPixelsPerUnit);
        tvBaseScale = new Vector3(scale, scale, 1f);
        tvRect.localScale = tvBaseScale;

        BuildPresenter();
        BuildContestants();

        stageBlock = stageRoot.AddComponent<MapBlock>();
        stageBlock.ConfigureRuntimeDockingBlock(
            stageRoot.transform,
            false,
            stageImpactStrength,
            stageEntryDuration,
            stageRailDistance);

        stageRoot.SetActive(false);
    }

    private void BuildPresenter()
    {
        GameObject go = new("PresenterWorldSprite");
        go.transform.SetParent(stageRoot.transform, false);
        presenterTransform = go.transform;
        presenterTransform.localPosition = presenterLocalOffset;
        presenterRenderer = go.AddComponent<SpriteRenderer>();
        presenterRenderer.color = Color.white;
        presenterRenderer.enabled = false;
    }

    private void BuildContestants()
    {
        EnsureContestantArrays();

        for (int i = 0; i < 3; i++)
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
                new Vector2(-4.2f, -2.45f),
                new Vector2(-2.8f, -2.45f),
                new Vector2(-1.4f, -2.45f)
            };
        }
    }

    private void MoveScreensIntoSharedTv()
    {
        ReparentToTv(rewardScreen);
        ReparentToTv(mapScreen);

        if (legacyMapCanvasRoot != null)
            legacyMapCanvasRoot.SetActive(false);
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

    private void DisableLegacyPresentation()
    {
        DisableImage(legacyFieldFilter);
        DisableImage(legacyPlayerSpotlight);
        DisableImage(legacyPresenterSpotlight);
        DisableImage(legacyPresenterImage);

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
        UpdateDesiredMode();
        UpdatePresenter();
        RefreshContestants();
        UpdateSharedPointerTracking();

        if (transitionRoutine == null && desiredMode != currentMode)
            transitionRoutine = StartCoroutine(Transition());
    }

    private void LateUpdate()
    {
        if (!bound)
            return;

        // BattleHUD의 기존 선택 연출은 렌더 직전에 확실하게 차단합니다.
        DisableImage(legacyFieldFilter);
        DisableImage(legacyPlayerSpotlight);
        DisableImage(legacyPresenterSpotlight);
        DisableImage(legacyPresenterImage);

        if (legacyMapCanvasRoot != null && legacyMapCanvasRoot.activeSelf)
            legacyMapCanvasRoot.SetActive(false);
        if (rewardLoadoutStrip != null && rewardLoadoutStrip.gameObject.activeSelf)
            rewardLoadoutStrip.gameObject.SetActive(false);

        if (tvCanvas != null && tvCanvas.worldCamera != Camera.main)
            tvCanvas.worldCamera = Camera.main;

        UpdateSorting();
        MaintainEquipmentDock();
        EnsureMapStartMarker();
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

    private IEnumerator Transition()
    {
        stageTransitioning = true;
        SetInteraction(false);

        while (currentMode != desiredMode)
        {
            ShowMode next = desiredMode;

            // 같은 TV 안에서 내용만 교체합니다. Transform / Scale / Camera Anchor는 건드리지 않습니다.
            if (currentMode != ShowMode.None && next != ShowMode.None)
            {
                currentMode = next;
                SetContent(currentMode);
                SetInteraction(true);
                continue;
            }

            if (currentMode != ShowMode.None && next == ShowMode.None)
            {
                cameraController?.SetShowCursorTracking(false, Vector2.zero);
                if (stageBlock != null && stageRoot != null && stageRoot.activeSelf)
                {
                    stageBlock.PlayExit(NormalizeDirection(stageRailDirection));
                    yield return new WaitForSecondsRealtime(stageBlock.ExitDuration + 0.03f);
                }

                currentMode = ShowMode.None;
                SetContent(ShowMode.None);
                if (stageRoot != null)
                    stageRoot.SetActive(false);
                dockCaptured = false;
                continue;
            }

            if (currentMode == ShowMode.None && next != ShowMode.None)
            {
                CaptureStageDock();
                currentMode = next;
                SetContent(currentMode);
                stageRoot.SetActive(true);
                stageRoot.transform.localScale = Vector3.one;

                if (presentationManager != null)
                    presentationManager.PlayPresenterAnimation(true);

                if (stageBlock != null)
                {
                    stageBlock.PlayEnter(stageDockPosition, NormalizeDirection(stageRailDirection));
                    yield return new WaitForSecondsRealtime(stageBlock.GetEntryDuration() + 0.03f);
                }
                else
                {
                    stageRoot.transform.position = stageDockPosition;
                }

                SetInteraction(true);
            }
        }

        stageTransitioning = false;
        transitionRoutine = null;
    }

    private void SetContent(ShowMode mode)
    {
        bool reward = mode == ShowMode.Reward;
        bool map = mode == ShowMode.Map;

        if (rewardScreen != null)
            rewardScreen.gameObject.SetActive(reward);
        if (mapScreen != null)
            mapScreen.gameObject.SetActive(map);
        if (mapContent != null && map)
            mapContent.gameObject.SetActive(true);

        MaintainEquipmentDock();
    }

    private void SetInteraction(bool enabledInteraction)
    {
        if (tvGroup == null)
            return;

        bool active = enabledInteraction && currentMode != ShowMode.None;
        tvGroup.interactable = active;
        tvGroup.blocksRaycasts = active;
    }

    private void CaptureStageDock()
    {
        if (TryGetLiveFieldBounds(out Bounds fieldBounds))
        {
            float tvHeight = tvCanvasSize.y / Mathf.Max(32f, tvPixelsPerUnit);
            float tvBottom = fieldBounds.max.y - tvFieldOverlap;
            stageDockPosition = new Vector3(
                fieldBounds.center.x,
                tvBottom + tvHeight * 0.5f,
                0f);
        }
        else
        {
            Vector3 fallback = player != null ? player.transform.position : Vector3.zero;
            float tvHeight = tvCanvasSize.y / Mathf.Max(32f, tvPixelsPerUnit);
            stageDockPosition = fallback + new Vector3(0f, 3.0f + tvHeight * 0.5f, 0f);
        }

        dockCaptured = true;
    }

    private void UpdateSharedPointerTracking()
    {
        if (cameraController == null || tvRect == null || !IsShowActive || stageRoot == null || !stageRoot.activeSelf)
        {
            cameraController?.SetShowCursorTracking(false, Vector2.zero);
            ApplyTvFocus(false);
            return;
        }

        Camera camera = Camera.main;
        if (camera == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                tvRect,
                Input.mousePosition,
                camera,
                out Vector2 local))
        {
            cameraController.SetShowCursorTracking(false, Vector2.zero);
            ApplyTvFocus(false);
            return;
        }

        Rect rect = tvRect.rect;
        bool inside = rect.Contains(local);
        Vector2 normalized = inside
            ? new Vector2(
                Mathf.Clamp(local.x / Mathf.Max(1f, rect.width * 0.5f), -1f, 1f),
                Mathf.Clamp(local.y / Mathf.Max(1f, rect.height * 0.5f), -1f, 1f))
            : Vector2.zero;

        cameraController.SetShowCursorTracking(inside, normalized);
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

        Sprite sprite = legacyPresenterImage != null ? legacyPresenterImage.sprite : null;
        if (sprite == BattleHudSpriteCache.DefaultSprite)
            sprite = null;

        if (sprite != lastPresenterSprite)
        {
            lastPresenterSprite = sprite;
            presenterRenderer.sprite = sprite;
            if (sprite != null)
            {
                float height = Mathf.Abs(sprite.bounds.size.y);
                float scale = height > 0.0001f ? presenterWorldHeight / height : 1f;
                bool flip = legacyPresenterImage != null && legacyPresenterImage.rectTransform.localScale.x < 0f;
                presenterTransform.localScale = new Vector3(flip ? -scale : scale, scale, 1f);
            }
        }

        presenterRenderer.enabled = sprite != null && IsShowActive;

        if (sprite == null && IsShowActive && !presenterWarningShown)
        {
            presenterWarningShown = true;
            Debug.LogWarning(
                "[BattleShowWorldSetController] Presenter Sprite가 비어 있습니다. " +
                "BattleHUD.presenterSprite 또는 BattleShowPresentationManager.presenterFrames를 할당하세요.",
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

            if (sprite == null)
                continue;

            float height = Mathf.Abs(sprite.bounds.size.y);
            float scale = height > 0.0001f ? contestantWorldHeight / height : 1f;
            renderer.transform.localScale = Vector3.one * scale;
            renderer.transform.localPosition = contestantLocalOffsets[i];
        }
    }

    private void MaintainEquipmentDock()
    {
        if (equipmentDock == null || runManager == null)
            return;

        bool reward = runManager.State == BattleRunState.Reward;
        if (equipmentDock.gameObject.activeSelf != reward)
            equipmentDock.gameObject.SetActive(reward);
    }

    private void UpdateSorting()
    {
        SpriteRenderer playerRenderer = player != null
            ? player.GetComponentInChildren<SpriteRenderer>(true)
            : null;
        if (playerRenderer == null)
            return;

        int fieldOrder = GetHighestFieldOrder(playerRenderer.sortingLayerID);
        int playerOrder = playerRenderer.sortingOrder;

        if (tvCanvas != null)
        {
            tvCanvas.sortingLayerID = playerRenderer.sortingLayerID;
            int tvOrder = fieldOrder + 1;
            if (tvOrder >= playerOrder)
                tvOrder = playerOrder - 1;
            tvCanvas.sortingOrder = tvOrder;
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
            if (child is RectTransform rect && child.name.StartsWith("StageNode_"))
                nodes.Add(rect);
        }

        if (nodes.Count == 0)
            return;

        float minX = float.MaxValue;
        for (int i = 0; i < nodes.Count; i++)
            minX = Mathf.Min(minX, nodes[i].anchoredPosition.x);

        List<RectTransform> first = new();
        float y = 0f;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (Mathf.Abs(nodes[i].anchoredPosition.x - minX) > 1.5f)
                continue;
            first.Add(nodes[i]);
            y += nodes[i].anchoredPosition.y;
        }

        if (first.Count == 0)
            return;

        y /= first.Count;
        Vector2 startPosition = new(minX - mapStartGap, y);

        GameObject marker = new("StageStartMarker");
        marker.transform.SetParent(mapContent, false);
        Image image = marker.AddComponent<Image>();
        image.color = new Color(0.10f, 0.78f, 0.98f, 1f);
        image.raycastTarget = false;
        RectTransform markerRect = marker.GetComponent<RectTransform>();
        markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerRect.sizeDelta = mapStartSize;
        markerRect.anchoredPosition = startPosition;

        GameObject textObject = new("Label");
        textObject.transform.SetParent(marker.transform, false);
        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = "START!  ▶";
        text.fontSize = 14;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        Stretch(textObject.GetComponent<RectTransform>());

        Vector2 from = startPosition + Vector2.right * (mapStartSize.x * 0.5f + 4f);
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
        Image image = line.AddComponent<Image>();
        image.color = new Color(0.16f, 0.78f, 1f, 0.92f);
        image.raycastTarget = false;
        RectTransform rect = line.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = (from + to) * 0.5f;
        rect.sizeDelta = new Vector2(delta.magnitude, 5f);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        line.transform.SetAsFirstSibling();
    }

    private static int GetHighestFieldOrder(int sortingLayerId)
    {
        int highest = -1000;
        BattleWalkableField[] fields = FindObjectsByType<BattleWalkableField>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int i = 0; i < fields.Length; i++)
        {
            SpriteRenderer renderer = fields[i] != null ? fields[i].GetComponent<SpriteRenderer>() : null;
            if (renderer != null && renderer.sortingLayerID == sortingLayerId)
                highest = Mathf.Max(highest, renderer.sortingOrder);
        }

        return highest;
    }

    private static bool TryGetLiveFieldBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        BattleWalkableField[] fields = FindObjectsByType<BattleWalkableField>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int i = 0; i < fields.Length; i++)
        {
            BattleWalkableField field = fields[i];
            if (field == null || !field.gameObject.activeInHierarchy)
                continue;

            Bounds candidate;
            Collider2D collider = field.GetComponent<Collider2D>();
            if (collider != null && collider.enabled)
                candidate = collider.bounds;
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

    private static Vector2 NormalizeDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return Vector2.up;
        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
            return direction.x >= 0f ? Vector2.right : Vector2.left;
        return direction.y >= 0f ? Vector2.up : Vector2.down;
    }

    private static RectTransform FindRect(string name)
    {
        RectTransform[] rects = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < rects.Length; i++)
            if (rects[i] != null && rects[i].name == name)
                return rects[i];
        return null;
    }

    private static Image FindImage(string name)
    {
        Image[] images = FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < images.Length; i++)
            if (images[i] != null && images[i].name == name)
                return images[i];
        return null;
    }

    private static Image FindPresenterImage()
    {
        Image[] images = FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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
        if (rect == null)
            return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

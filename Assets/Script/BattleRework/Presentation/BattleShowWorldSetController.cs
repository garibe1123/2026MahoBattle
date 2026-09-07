using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward / Map 선택 연출을 하나의 공용 월드 TV 세트로 관리합니다.
///
/// 핵심 원칙:
/// - Reward와 Map은 서로 다른 TV가 아닙니다. 같은 Stage Root / 같은 TV Transform을 공유합니다.
/// - Reward <-> Map 전환에서는 TV 위치/스케일/카메라 구도를 다시 계산하지 않고 TV 내용만 교체합니다.
/// - PrizeChoices는 TV 화면 내부의 아이템 선택 UI입니다.
/// - TV 앞의 3자리는 아이템 슬롯이 아니라 캐릭터 이미지용 World SpriteRenderer입니다.
/// - Presenter도 Stage Root 소속의 World SpriteRenderer입니다.
/// - 전투 EquipmentDock의 외형은 PrizeChoices 스타일 템플릿으로만 재사용합니다.
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

    [Header("Shared World TV")]
    [SerializeField] private Vector2 tvCanvasSize = new(1120f, 560f);
    [SerializeField, Min(32f)] private float tvPixelsPerUnit = 122f;
    [SerializeField, Min(0f)] private float tvFieldOverlap = 0.40f;
    [SerializeField, Min(1)] private int tvBehindPlayerOrder = 20;

    [Header("Shared Stage Entry")]
    [SerializeField] private Vector2 stageRailDirection = Vector2.up;
    [SerializeField, Min(0.05f)] private float stageEntryDuration = 0.62f;
    [SerializeField, Min(2f)] private float stageRailDistance = 12f;
    [SerializeField, Range(0f, 1.5f)] private float stageImpactStrength = 0.72f;

    [Header("Presenter - World Sprite")]
    [Tooltip("TV 중심 기준 사회자 월드 위치입니다. Reward/Map 모두 같은 값을 사용합니다.")]
    [SerializeField] private Vector2 presenterLocalOffset = new(5.15f, -1.55f);
    [SerializeField, Min(0.5f)] private float presenterWorldHeight = 3.35f;
    [SerializeField, Min(1)] private int presenterFrontOrder = 30;
    [SerializeField] private Sprite presenterFallbackSprite;

    [Header("Three Character Images - World Sprite")]
    [Tooltip("레퍼런스에서 TV 앞에 서 있는 3명입니다. 아이템 슬롯이 아닙니다.")]
    [SerializeField] private Sprite[] contestantSprites = new Sprite[3];
    [SerializeField] private Vector2[] contestantLocalOffsets =
    {
        new(-4.25f, -2.20f),
        new(-2.85f, -2.20f),
        new(-1.45f, -2.20f)
    };
    [SerializeField, Min(0.25f)] private float contestantWorldHeight = 1.35f;
    [SerializeField, Min(1)] private int contestantFrontOrder = 10;

    [Header("Prize UI Inside TV")]
    [Tooltip("아이템 후보는 TV 내부에 유지하되 전투 Equipment Slot의 외형만 재사용합니다.")]
    [SerializeField] private Vector2 prizeSlotSize = new(176f, 152f);
    [SerializeField, Min(0f)] private float prizeSlotSpacing = 22f;

    private BattleRunManager runManager;
    private BattleHUD hud;
    private PlayerController player;
    private BattleShowPresentationManager presentationManager;

    private RectTransform rewardRootRect;
    private RectTransform rewardScreenRect;
    private RectTransform prizeChoiceRoot;
    private RectTransform rewardInventoryStripRect;
    private RectTransform equipmentDockRect;
    private RectTransform mapScreenRect;
    private RectTransform mapSelectionRect;

    private GameObject legacyMapWorldCanvasRoot;
    private Image legacyPresenterImage;
    private Sprite legacyPresenterSprite;
    private Image legacyFieldFilter;
    private Image legacyPlayerSpotlight;
    private Image legacyPresenterSpotlight;

    private GameObject stageRoot;
    private MapBlock stageRailBlock;
    private Canvas tvCanvas;
    private RectTransform tvCanvasRect;
    private CanvasGroup tvCanvasGroup;

    private Transform presenterVisual;
    private SpriteRenderer presenterRenderer;
    private readonly SpriteRenderer[] contestantRenderers = new SpriteRenderer[3];

    private Coroutine bindRoutine;
    private Coroutine transitionRoutine;
    private ShowMode currentMode;
    private ShowMode desiredMode;
    private bool bound;
    private bool stageTransitioning;
    private int styledPrizeChildCount = -1;
    private Sprite lastPresenterSprite;
    private bool warnedMissingPresenterSprite;

    private Vector3 stageDockPosition;

    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo HudPresenterSpriteField =
        typeof(BattleHUD).GetField("presenterSprite", PrivateInstance);

    private static readonly FieldInfo HudPresenterFlipField =
        typeof(BattleHUD).GetField("presenterFlipX", PrivateInstance);

    private static readonly FieldInfo PresenterFramesField =
        typeof(BattleShowPresentationManager).GetField("presenterFrames", PrivateInstance);

    private static readonly FieldInfo PendingRewardIndexField =
        typeof(BattleHUD).GetField("pendingRewardIndex", PrivateInstance);

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
        EnsureContestantArrayShape();
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

        if (stageRoot != null)
            stageRoot.transform.DOKill();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public void SetContestantSprites(Sprite first, Sprite second, Sprite third)
    {
        EnsureContestantArrayShape();
        contestantSprites[0] = first;
        contestantSprites[1] = second;
        contestantSprites[2] = third;
        RefreshContestantVisuals();
    }

    private IEnumerator BindWhenReady()
    {
        while (enabled)
        {
            ResolveSystems();
            if (hud != null && runManager != null && TryResolveHudObjects())
            {
                CaptureLegacyPresenterSprite();
                BuildSharedWorldStage();
                MoveBothScreensIntoSharedTv();
                SuppressLegacyDuplicatePresentation();
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

    private bool TryResolveHudObjects()
    {
        if (rewardRootRect == null)
            rewardRootRect = FindRectTransform("RewardQuizShow");
        if (rewardScreenRect == null)
            rewardScreenRect = FindRectTransform("PrizeSelectionScreen");
        if (prizeChoiceRoot == null)
            prizeChoiceRoot = FindRectTransform("PrizeChoices");
        if (rewardInventoryStripRect == null)
            rewardInventoryStripRect = FindRectTransform("RewardLoadoutStrip");
        if (equipmentDockRect == null)
            equipmentDockRect = FindRectTransform("EquipmentDock");
        if (mapScreenRect == null)
            mapScreenRect = FindRectTransform("MapSelectionScreen");
        if (mapSelectionRect == null)
            mapSelectionRect = FindRectTransform("MapSelectionContent");

        if (legacyMapWorldCanvasRoot == null)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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

        if (legacyPresenterImage == null)
        {
            GameObject presenterObject = FindLegacyPresenterObject();
            if (presenterObject != null)
                legacyPresenterImage = presenterObject.GetComponent<Image>();
        }

        if (legacyFieldFilter == null)
            legacyFieldFilter = FindImage("FieldBroadcastFilter");
        if (legacyPlayerSpotlight == null)
            legacyPlayerSpotlight = FindImage("PlayerFloorSpotlight");
        if (legacyPresenterSpotlight == null)
            legacyPresenterSpotlight = FindImage("PresenterFloorSpotlight");

        return rewardRootRect != null &&
               rewardScreenRect != null &&
               prizeChoiceRoot != null &&
               equipmentDockRect != null &&
               mapScreenRect != null &&
               mapSelectionRect != null;
    }

    private void CaptureLegacyPresenterSprite()
    {
        if (legacyPresenterImage == null || legacyPresenterImage.sprite == null)
            return;

        if (legacyPresenterImage.sprite != BattleHudSpriteCache.DefaultSprite)
            legacyPresenterSprite = legacyPresenterImage.sprite;
    }

    private void BuildSharedWorldStage()
    {
        if (stageRoot != null)
            return;

        stageRoot = new GameObject("BattleShowSharedStage");
        stageRoot.transform.SetParent(transform, false);

        GameObject canvasObject = new("BattleShowSharedWorldTV");
        canvasObject.transform.SetParent(stageRoot.transform, false);

        tvCanvas = canvasObject.AddComponent<Canvas>();
        tvCanvas.renderMode = RenderMode.WorldSpace;
        tvCanvas.overrideSorting = true;
        tvCanvas.worldCamera = Camera.main;
        canvasObject.AddComponent<GraphicRaycaster>();

        tvCanvasGroup = canvasObject.AddComponent<CanvasGroup>();
        tvCanvasGroup.alpha = 1f;
        tvCanvasGroup.interactable = false;
        tvCanvasGroup.blocksRaycasts = false;

        tvCanvasRect = canvasObject.GetComponent<RectTransform>();
        tvCanvasRect.sizeDelta = tvCanvasSize;
        tvCanvasRect.pivot = new Vector2(0.5f, 0.5f);
        float worldScale = 1f / Mathf.Max(32f, tvPixelsPerUnit);
        tvCanvasRect.localScale = new Vector3(worldScale, worldScale, 1f);
        tvCanvasRect.localPosition = Vector3.zero;
        tvCanvasRect.localRotation = Quaternion.identity;

        BuildPresenterWorldObject();
        BuildContestantWorldObjects();

        stageRailBlock = stageRoot.AddComponent<MapBlock>();
        stageRailBlock.ConfigureRuntimeDockingBlock(
            stageRoot.transform,
            false,
            stageImpactStrength,
            stageEntryDuration,
            stageRailDistance);

        stageRoot.SetActive(false);
    }

    private void BuildPresenterWorldObject()
    {
        GameObject presenterObject = new("PresenterWorldSprite");
        presenterObject.transform.SetParent(stageRoot.transform, false);
        presenterVisual = presenterObject.transform;
        presenterVisual.localPosition = presenterLocalOffset;

        presenterRenderer = presenterObject.AddComponent<SpriteRenderer>();
        presenterRenderer.color = Color.white;
        presenterRenderer.enabled = false;
    }

    private void BuildContestantWorldObjects()
    {
        EnsureContestantArrayShape();

        for (int i = 0; i < contestantRenderers.Length; i++)
        {
            GameObject character = new($"ShowCharacter_{i + 1}");
            character.transform.SetParent(stageRoot.transform, false);
            character.transform.localPosition = contestantLocalOffsets[i];

            SpriteRenderer renderer = character.AddComponent<SpriteRenderer>();
            renderer.color = Color.white;
            renderer.enabled = false;
            contestantRenderers[i] = renderer;
        }

        RefreshContestantVisuals();
    }

    private void EnsureContestantArrayShape()
    {
        if (contestantSprites == null || contestantSprites.Length != 3)
        {
            Sprite[] resized = new Sprite[3];
            if (contestantSprites != null)
            {
                int copy = Mathf.Min(3, contestantSprites.Length);
                for (int i = 0; i < copy; i++)
                    resized[i] = contestantSprites[i];
            }
            contestantSprites = resized;
        }

        if (contestantLocalOffsets == null || contestantLocalOffsets.Length != 3)
        {
            contestantLocalOffsets = new[]
            {
                new Vector2(-4.25f, -2.20f),
                new Vector2(-2.85f, -2.20f),
                new Vector2(-1.45f, -2.20f)
            };
        }
    }

    private void MoveBothScreensIntoSharedTv()
    {
        if (tvCanvasRect == null)
            return;

        // PrizeChoices는 원래 Reward 화면 내부에 있는 아이템 선택 UI입니다.
        // TV 앞의 3개 캐릭터와 절대 섞지 않습니다.
        ReparentWorldScreen(rewardScreenRect);
        ReparentWorldScreen(mapScreenRect);

        if (legacyMapWorldCanvasRoot != null)
            legacyMapWorldCanvasRoot.SetActive(false);

        if (rewardInventoryStripRect != null)
            rewardInventoryStripRect.gameObject.SetActive(false);
    }

    private void ReparentWorldScreen(RectTransform rect)
    {
        if (rect == null || tvCanvasRect == null)
            return;

        rect.SetParent(tvCanvasRect, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = tvCanvasSize;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private void SuppressLegacyDuplicatePresentation()
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

        // 실제 월드 Presenter Sprite를 확보한 경우에만 구 UI Presenter를 끕니다.
        // Sprite를 못 찾은 상태에서 먼저 꺼서 사회자가 통째로 사라지는 문제를 막습니다.
        if (legacyPresenterImage != null && ResolvePresenterSprite() != null)
        {
            legacyPresenterImage.raycastTarget = false;
            legacyPresenterImage.enabled = false;
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
        RefreshContestantVisuals();

        if (transitionRoutine == null && desiredMode != currentMode)
            transitionRoutine = StartCoroutine(TransitionLoop());
    }

    private void LateUpdate()
    {
        if (!bound)
            return;

        if (legacyMapWorldCanvasRoot != null && legacyMapWorldCanvasRoot.activeSelf)
            legacyMapWorldCanvasRoot.SetActive(false);
        if (rewardInventoryStripRect != null && rewardInventoryStripRect.gameObject.activeSelf)
            rewardInventoryStripRect.gameObject.SetActive(false);
        if (legacyFieldFilter != null)
            legacyFieldFilter.enabled = false;
        if (legacyPlayerSpotlight != null)
            legacyPlayerSpotlight.enabled = false;
        if (legacyPresenterSpotlight != null)
            legacyPresenterSpotlight.enabled = false;

        if (legacyPresenterImage != null && ResolvePresenterSprite() != null)
            legacyPresenterImage.enabled = false;

        if (tvCanvas != null && tvCanvas.worldCamera != Camera.main)
            tvCanvas.worldCamera = Camera.main;

        UpdateWorldSorting();
        KeepSharedStageDocked();
        MaintainRewardUiInsideTv();
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
        stageTransitioning = true;
        SetInteraction(false);

        while (currentMode != desiredMode)
        {
            ShowMode entering = desiredMode;

            // Reward <-> Map 전환은 같은 TV를 그대로 둔 채 내용만 바꿉니다.
            if (currentMode != ShowMode.None && entering != ShowMode.None)
            {
                currentMode = entering;
                SetContentActive(currentMode);
                SetInteraction(true);
                continue;
            }

            // 선택 쇼를 완전히 나갈 때만 Stage 전체가 퇴장합니다.
            if (currentMode != ShowMode.None && entering == ShowMode.None)
            {
                if (stageRailBlock != null && stageRoot != null && stageRoot.activeSelf)
                    stageRailBlock.PlayExit(NormalizeDirection(stageRailDirection));

                float exitDuration = stageRailBlock != null
                    ? stageRailBlock.ExitDuration
                    : stageEntryDuration;
                yield return new WaitForSecondsRealtime(exitDuration + 0.03f);

                currentMode = ShowMode.None;
                SetContentActive(ShowMode.None);
                if (stageRoot != null)
                    stageRoot.SetActive(false);
                continue;
            }

            // None -> Reward/Map 진입에서만 공용 Stage가 한 번 들어옵니다.
            if (currentMode == ShowMode.None && entering != ShowMode.None)
            {
                ResolveSharedStageDock();
                currentMode = entering;
                SetContentActive(currentMode);

                if (stageRoot != null)
                    stageRoot.SetActive(true);

                if (presentationManager != null)
                    presentationManager.PlayPresenterAnimation(true);

                if (stageRailBlock != null)
                    stageRailBlock.PlayEnter(stageDockPosition, NormalizeDirection(stageRailDirection));
                else if (stageRoot != null)
                    stageRoot.transform.position = stageDockPosition;

                float entryDuration = stageRailBlock != null
                    ? stageRailBlock.GetEntryDuration()
                    : stageEntryDuration;
                yield return new WaitForSecondsRealtime(entryDuration + 0.03f);

                if (currentMode == desiredMode)
                    SetInteraction(true);
            }
        }

        stageTransitioning = false;
        transitionRoutine = null;

        if (currentMode != ShowMode.None && currentMode == desiredMode)
            SetInteraction(true);
    }

    private void SetContentActive(ShowMode mode)
    {
        bool visible = mode != ShowMode.None;
        bool reward = mode == ShowMode.Reward;
        bool map = mode == ShowMode.Map;

        if (rewardRootRect != null && visible)
            rewardRootRect.gameObject.SetActive(true);

        if (rewardScreenRect != null)
            rewardScreenRect.gameObject.SetActive(reward);
        if (mapScreenRect != null)
            mapScreenRect.gameObject.SetActive(map);
        if (mapSelectionRect != null)
            mapSelectionRect.gameObject.SetActive(map);
        if (prizeChoiceRoot != null)
            prizeChoiceRoot.gameObject.SetActive(reward);

        if (rewardInventoryStripRect != null)
            rewardInventoryStripRect.gameObject.SetActive(false);

        // EquipmentDock은 전투 HUD 원래 위치를 유지합니다.
        // Reward 후보 3개를 대신하는 용도로 이동시키지 않습니다.
    }

    private void SetInteraction(bool enabledInteraction)
    {
        if (tvCanvasGroup == null)
            return;

        bool interactive = enabledInteraction && currentMode != ShowMode.None;
        tvCanvasGroup.interactable = interactive;
        tvCanvasGroup.blocksRaycasts = interactive;
    }

    private void ResolveSharedStageDock()
    {
        if (TryGetLiveFieldBounds(out Bounds fieldBounds))
        {
            float tvWorldHeight = tvCanvasSize.y / Mathf.Max(32f, tvPixelsPerUnit);
            float tvBottom = fieldBounds.max.y - tvFieldOverlap;
            stageDockPosition = new Vector3(
                fieldBounds.center.x,
                tvBottom + tvWorldHeight * 0.5f,
                0f);
            return;
        }

        Vector3 fallback = player != null ? player.transform.position : Vector3.zero;
        float fallbackTvHeight = tvCanvasSize.y / Mathf.Max(32f, tvPixelsPerUnit);
        stageDockPosition = fallback + new Vector3(0f, 3.2f + fallbackTvHeight * 0.5f, 0f);
    }

    private void KeepSharedStageDocked()
    {
        if (stageTransitioning || currentMode == ShowMode.None || stageRoot == null || !stageRoot.activeSelf)
            return;

        // Reward와 Map 모두 동일한 함수 / 동일한 Transform / 동일한 값을 사용합니다.
        ResolveSharedStageDock();
        stageRoot.transform.position = stageDockPosition;
    }

    private void MaintainRewardUiInsideTv()
    {
        bool reward = runManager != null && runManager.State == BattleRunState.Reward;

        if (prizeChoiceRoot != null)
            prizeChoiceRoot.gameObject.SetActive(reward);

        if (!reward || prizeChoiceRoot == null)
            return;

        if (styledPrizeChildCount != prizeChoiceRoot.childCount)
        {
            styledPrizeChildCount = prizeChoiceRoot.childCount;
            StylePrizeChoicesLikeCombatSlots();
        }
        else
        {
            ApplyPrizeSelectionColors();
        }
    }

    private void StylePrizeChoicesLikeCombatSlots()
    {
        if (prizeChoiceRoot == null || equipmentDockRect == null)
            return;

        Transform templateSlot = equipmentDockRect.Find("Slot_1");
        Image templateImage = templateSlot != null ? templateSlot.GetComponent<Image>() : null;

        List<RectTransform> cards = new();
        for (int i = 0; i < prizeChoiceRoot.childCount; i++)
        {
            Transform child = prizeChoiceRoot.GetChild(i);
            if (child == null || child.GetComponent<RewardPrizeDrag>() == null)
                continue;
            if (child is RectTransform rect)
                cards.Add(rect);
        }

        float width = prizeSlotSize.x;
        float totalWidth = cards.Count * width + Mathf.Max(0, cards.Count - 1) * prizeSlotSpacing;
        float startX = -totalWidth * 0.5f + width * 0.5f;

        for (int i = 0; i < cards.Count; i++)
        {
            RectTransform card = cards[i];
            Vector2 basePosition = new(startX + i * (width + prizeSlotSpacing), 0f);

            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = prizeSlotSize;
            card.anchoredPosition = basePosition;
            card.localScale = Vector3.one;

            Image background = card.GetComponent<Image>();
            if (background != null && templateImage != null)
            {
                background.sprite = templateImage.sprite;
                background.type = templateImage.type;
                background.material = templateImage.material;
            }

            RewardPrizeDrag drag = card.GetComponent<RewardPrizeDrag>();
            RewardCardHover hover = card.GetComponent<RewardCardHover>();
            if (drag != null && hover != null)
                hover.Configure(hud, drag.RewardIndex, card, basePosition);
        }

        ApplyPrizeSelectionColors();
    }

    private void ApplyPrizeSelectionColors()
    {
        if (prizeChoiceRoot == null || equipmentDockRect == null)
            return;

        Transform templateSlot = equipmentDockRect.Find("Slot_1");
        Image templateImage = templateSlot != null ? templateSlot.GetComponent<Image>() : null;
        Color baseColor = templateImage != null
            ? templateImage.color
            : new Color(0.055f, 0.062f, 0.082f, 1f);

        int selectedIndex = -1;
        if (hud != null && PendingRewardIndexField != null &&
            PendingRewardIndexField.GetValue(hud) is int pending)
        {
            selectedIndex = pending;
        }

        for (int i = 0; i < prizeChoiceRoot.childCount; i++)
        {
            Transform child = prizeChoiceRoot.GetChild(i);
            RewardPrizeDrag drag = child != null ? child.GetComponent<RewardPrizeDrag>() : null;
            Image image = child != null ? child.GetComponent<Image>() : null;
            if (drag == null || image == null)
                continue;

            image.color = drag.RewardIndex == selectedIndex
                ? new Color(0.20f, 0.18f, 0.08f, 1f)
                : baseColor;
        }
    }

    private void UpdatePresenterVisual()
    {
        if (presenterRenderer == null || presenterVisual == null)
            return;

        presenterVisual.localPosition = presenterLocalOffset;

        Sprite sprite = ResolvePresenterSprite();
        if (sprite != lastPresenterSprite)
        {
            lastPresenterSprite = sprite;
            presenterRenderer.sprite = sprite;
            ApplyPresenterScale(sprite);
        }

        bool show = sprite != null &&
                    stageRoot != null &&
                    stageRoot.activeSelf &&
                    (currentMode != ShowMode.None || desiredMode != ShowMode.None || stageTransitioning);
        presenterRenderer.enabled = show;
    }

    private Sprite ResolvePresenterSprite()
    {
        if (hud != null && HudPresenterSpriteField != null)
        {
            Sprite sprite = HudPresenterSpriteField.GetValue(hud) as Sprite;
            if (sprite != null && sprite != BattleHudSpriteCache.DefaultSprite)
                return sprite;
        }

        if (presentationManager == null)
            presentationManager = FindFirstObjectByType<BattleShowPresentationManager>();

        if (presentationManager != null && PresenterFramesField != null)
        {
            Sprite[] frames = PresenterFramesField.GetValue(presentationManager) as Sprite[];
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
        if (legacyPresenterSprite != null)
            return legacyPresenterSprite;

        if (!warnedMissingPresenterSprite)
        {
            warnedMissingPresenterSprite = true;
            Debug.LogWarning(
                "[BattleShowWorldSetController] 월드 사회자 Sprite 소스를 찾지 못했습니다. " +
                "기존 Presenter UI는 이 경우 강제로 끄지 않습니다.",
                this);
        }

        return null;
    }

    private void ApplyPresenterScale(Sprite sprite)
    {
        if (presenterVisual == null || sprite == null)
            return;

        float height = Mathf.Abs(sprite.bounds.size.y);
        float scale = height > 0.0001f ? presenterWorldHeight / height : 1f;
        bool flip = hud != null && HudPresenterFlipField != null &&
                    HudPresenterFlipField.GetValue(hud) is bool flipX && flipX;

        presenterVisual.localScale = new Vector3(flip ? -scale : scale, scale, 1f);
    }

    private void RefreshContestantVisuals()
    {
        EnsureContestantArrayShape();

        Sprite playerFallback = null;
        if (player != null)
        {
            SpriteRenderer playerRenderer = player.GetComponentInChildren<SpriteRenderer>(true);
            if (playerRenderer != null)
                playerFallback = playerRenderer.sprite;
        }

        for (int i = 0; i < contestantRenderers.Length; i++)
        {
            SpriteRenderer renderer = contestantRenderers[i];
            if (renderer == null)
                continue;

            renderer.transform.localPosition = contestantLocalOffsets[i];
            Sprite sprite = contestantSprites[i];
            if (i == 0 && sprite == null)
                sprite = playerFallback;

            renderer.sprite = sprite;
            renderer.enabled = sprite != null && stageRoot != null && stageRoot.activeSelf;

            if (sprite != null)
            {
                float height = Mathf.Abs(sprite.bounds.size.y);
                float scale = height > 0.0001f ? contestantWorldHeight / height : 1f;
                renderer.transform.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }

    private void UpdateWorldSorting()
    {
        SpriteRenderer playerRenderer = player != null
            ? player.GetComponentInChildren<SpriteRenderer>(true)
            : null;

        if (playerRenderer == null)
        {
            if (tvCanvas != null)
                tvCanvas.sortingOrder = -10;
            if (presenterRenderer != null)
                presenterRenderer.sortingOrder = 30;
            for (int i = 0; i < contestantRenderers.Length; i++)
                if (contestantRenderers[i] != null)
                    contestantRenderers[i].sortingOrder = 10;
            return;
        }

        int layerId = playerRenderer.sortingLayerID;
        int playerOrder = playerRenderer.sortingOrder;

        if (tvCanvas != null)
        {
            tvCanvas.sortingLayerID = layerId;
            int tvOrder = playerOrder - Mathf.Max(1, tvBehindPlayerOrder);
            int highestFieldOrder = GetHighestFieldSortingOrder(layerId);
            if (highestFieldOrder < playerOrder)
                tvOrder = Mathf.Max(tvOrder, highestFieldOrder + 1);
            tvCanvas.sortingOrder = Mathf.Min(tvOrder, playerOrder - 1);
        }

        for (int i = 0; i < contestantRenderers.Length; i++)
        {
            SpriteRenderer renderer = contestantRenderers[i];
            if (renderer == null)
                continue;
            renderer.sortingLayerID = layerId;
            renderer.sortingOrder = playerOrder + Mathf.Max(1, contestantFrontOrder);
        }

        if (presenterRenderer != null)
        {
            presenterRenderer.sortingLayerID = layerId;
            presenterRenderer.sortingOrder = playerOrder + Mathf.Max(1, presenterFrontOrder);
        }
    }

    private static int GetHighestFieldSortingOrder(int sortingLayerId)
    {
        int highest = int.MinValue;
        BattleWalkableField[] fields = FindObjectsByType<BattleWalkableField>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < fields.Length; i++)
        {
            BattleWalkableField field = fields[i];
            if (field == null)
                continue;

            SpriteRenderer renderer = field.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sortingLayerID != sortingLayerId)
                continue;

            highest = Mathf.Max(highest, renderer.sortingOrder);
        }

        return highest == int.MinValue ? -1000 : highest;
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
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DELTARUNE식 TV 쇼 구도를 전투 월드에 구성합니다.
///
/// 레이어 원칙:
///   [뒤] 전투 Field 바닥
///        World TV (WorldSpace Canvas / 실제 GameObject)
///        Player + Presenter (실제 SpriteRenderer)
///        Prize 선택 슬롯 + 전투 Loadout (ScreenSpace UI)
///   [앞]
///
/// TV는 Player 화면좌표를 따라다니지 않습니다. 현재 BattleWalkableField 전체를 무대로 보고
/// 그 뒤쪽(월드 +Y)에 실제 GameObject로 배치합니다. Prize/Map 내용만 TV의 WorldSpace Canvas에
/// 끼워 넣습니다. 아이템 선택/드롭은 기존 전투 EquipmentDock Slot을 그대로 사용합니다.
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

    [Header("World TV Backdrop")]
    [Tooltip("TV WorldSpace Canvas의 픽셀 기준 크기입니다. 기존 Prize / Map 화면과 동일한 비율을 유지합니다.")]
    [SerializeField] private Vector2 tvCanvasSize = new(1120f, 560f);

    [Tooltip("TV 픽셀을 월드 유닛으로 환산하는 값입니다. 높을수록 TV가 작아집니다.")]
    [SerializeField, Min(32f)] private float tvPixelsPerUnit = 122f;

    [Tooltip("Field 뒤쪽 끝에서 TV 하단이 Field 쪽으로 살짝 겹치는 거리입니다. TV는 캐릭터보다 뒤에 렌더됩니다.")]
    [SerializeField, Min(0f)] private float tvFieldOverlap = 0.40f;

    [Tooltip("Player보다 TV를 기본적으로 몇 Sorting Order 뒤에 둘지 결정합니다. 실제로는 Field보다 앞이 되도록 자동 보정합니다.")]
    [SerializeField, Min(1)] private int tvBehindPlayerOrder = 20;

    [Header("TV Mechanical Entry")]
    [SerializeField] private Vector2 tvRailDirection = Vector2.up;
    [SerializeField, Min(0.05f)] private float tvEntryDuration = 0.62f;
    [SerializeField, Min(2f)] private float tvRailDistance = 12f;
    [SerializeField, Range(0f, 1.5f)] private float tvImpactStrength = 0.72f;

    [Header("Presenter - World Sprite")]
    [Tooltip("Field 오른쪽 끝에서 사회자를 안쪽으로 들이는 거리입니다.")]
    [SerializeField, Min(0f)] private float presenterInsetX = 1.15f;

    [Tooltip("Field 세로 중앙 기준 사회자 위치 보정입니다.")]
    [SerializeField] private float presenterYOffset = 0.20f;

    [SerializeField, Min(0.5f)] private float presenterWorldHeight = 3.35f;
    [SerializeField, Min(1)] private int presenterFrontOrder = 30;
    [SerializeField] private Vector2 presenterRailDirection = Vector2.right;
    [SerializeField, Min(0.05f)] private float presenterEntryDuration = 0.48f;
    [SerializeField, Min(2f)] private float presenterRailDistance = 8f;
    [SerializeField, Range(0f, 1.5f)] private float presenterImpactStrength = 0.45f;
    [SerializeField] private Sprite presenterFallbackSprite;

    [Header("Foreground Prize Slots")]
    [Tooltip("Prize 후보 슬롯은 전투 Equipment Slot의 외형을 복사해 사용합니다.")]
    [SerializeField] private Vector2 prizeSlotSize = new(98f, 94f);
    [SerializeField, Min(0f)] private float prizeSlotSpacing = 14f;
    [SerializeField] private Vector2 prizeChoiceAnchor = new(0.36f, 0.285f);

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

    private GameObject tvRailRoot;
    private MapBlock tvRailBlock;
    private Canvas tvCanvas;
    private RectTransform tvCanvasRect;
    private CanvasGroup tvCanvasGroup;

    private GameObject presenterRailRoot;
    private MapBlock presenterRailBlock;
    private Transform presenterVisual;
    private SpriteRenderer presenterRenderer;

    private Coroutine bindRoutine;
    private Coroutine transitionRoutine;
    private ShowMode currentMode;
    private ShowMode desiredMode;
    private bool bound;
    private bool railTransitioning;
    private float nextMapDecorationTime;
    private int styledPrizeChildCount = -1;
    private Sprite lastPresenterSprite;
    private bool warnedMissingPresenterSprite;

    private Vector3 tvDockPosition;
    private Vector3 presenterDockPosition;

    private static readonly BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;

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

        KillTweens();
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
            if (hud != null && runManager != null && TryResolveHudObjects())
            {
                CaptureLegacyPresenterSprite();
                BuildWorldStage();
                MoveTvScreensIntoWorld();
                PrepareForegroundRewardSlots();
                SuppressLegacyShowDecoration();
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
               rewardInventoryStripRect != null &&
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

    private void BuildWorldStage()
    {
        if (tvRailRoot == null)
        {
            tvRailRoot = new GameObject("BattleShowTVRail");
            tvRailRoot.transform.SetParent(transform, false);

            GameObject canvasObject = new("BattleShowWorldTV");
            canvasObject.transform.SetParent(tvRailRoot.transform, false);

            tvCanvas = canvasObject.AddComponent<Canvas>();
            tvCanvas.renderMode = RenderMode.WorldSpace;
            tvCanvas.overrideSorting = true;
            tvCanvas.worldCamera = Camera.main;
            canvasObject.AddComponent<GraphicRaycaster>();

            tvCanvasGroup = canvasObject.AddComponent<CanvasGroup>();
            tvCanvasGroup.alpha = 1f;
            tvCanvasGroup.interactable = true;
            tvCanvasGroup.blocksRaycasts = true;

            tvCanvasRect = canvasObject.GetComponent<RectTransform>();
            tvCanvasRect.sizeDelta = tvCanvasSize;
            tvCanvasRect.pivot = new Vector2(0.5f, 0.5f);
            float scale = 1f / Mathf.Max(32f, tvPixelsPerUnit);
            tvCanvasRect.localScale = new Vector3(scale, scale, 1f);
            tvCanvasRect.localPosition = Vector3.zero;
            tvCanvasRect.localRotation = Quaternion.identity;

            tvRailBlock = tvRailRoot.AddComponent<MapBlock>();
            tvRailBlock.ConfigureRuntimeDockingBlock(
                tvRailRoot.transform,
                false,
                tvImpactStrength,
                tvEntryDuration,
                tvRailDistance);

            tvRailRoot.SetActive(false);
        }

        if (presenterRailRoot == null)
        {
            presenterRailRoot = new GameObject("BattleShowPresenterRail");
            presenterRailRoot.transform.SetParent(transform, false);

            GameObject visualObject = new("PresenterWorldSprite");
            visualObject.transform.SetParent(presenterRailRoot.transform, false);
            presenterVisual = visualObject.transform;

            presenterRenderer = visualObject.AddComponent<SpriteRenderer>();
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

    private void MoveTvScreensIntoWorld()
    {
        if (tvCanvasRect == null)
            return;

        // Prize 후보는 TV 안이 아니라 무대 전경에 남깁니다.
        prizeChoiceRoot.SetParent(rewardRootRect, false);
        prizeChoiceRoot.anchorMin = prizeChoiceRoot.anchorMax = prizeChoiceAnchor;
        prizeChoiceRoot.pivot = new Vector2(0.5f, 0.5f);
        prizeChoiceRoot.sizeDelta = new Vector2(520f, 118f);
        prizeChoiceRoot.anchoredPosition = Vector2.zero;
        prizeChoiceRoot.localScale = Vector3.one;
        prizeChoiceRoot.localRotation = Quaternion.identity;

        ReparentWorldScreen(rewardScreenRect);
        ReparentWorldScreen(mapScreenRect);

        if (legacyMapWorldCanvasRoot != null)
            legacyMapWorldCanvasRoot.SetActive(false);

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

    private void PrepareForegroundRewardSlots()
    {
        if (equipmentDockRect == null)
            return;

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            Transform slot = equipmentDockRect.Find($"Slot_{i + 1}");
            if (slot == null)
                continue;

            RewardInventoryDropZone zone = slot.GetComponent<RewardInventoryDropZone>();
            if (zone == null)
                zone = slot.gameObject.AddComponent<RewardInventoryDropZone>();
            zone.Configure(hud, i);
        }
    }

    private void SuppressLegacyShowDecoration()
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

        if (legacyPresenterImage != null)
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

        if (transitionRoutine == null && desiredMode != currentMode)
            transitionRoutine = StartCoroutine(TransitionLoop());
    }

    private void LateUpdate()
    {
        if (!bound)
            return;

        // BattleHUD가 상태 전환 때 다시 켜도 최종 렌더 직전에 구 구조를 차단합니다.
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
        if (legacyPresenterImage != null)
            legacyPresenterImage.enabled = false;

        if (tvCanvas != null && tvCanvas.worldCamera != Camera.main)
            tvCanvas.worldCamera = Camera.main;

        UpdateWorldSorting();
        KeepDockedStageOnCurrentField();
        MaintainForegroundRewardUi();
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
        SetInteraction(false);

        while (currentMode != desiredMode)
        {
            ShowMode leaving = currentMode;
            if (leaving != ShowMode.None)
            {
                SetContentActive(leaving, true);
                PlayExit();
                yield return new WaitForSecondsRealtime(GetExitDuration());
            }

            ShowMode entering = desiredMode;
            currentMode = ShowMode.None;
            SetContentActive(ShowMode.None, false);

            if (entering == ShowMode.None)
            {
                if (tvRailRoot != null)
                    tvRailRoot.SetActive(false);
                if (presenterRailRoot != null)
                    presenterRailRoot.SetActive(false);
                continue;
            }

            ResolveDockTargets();
            SetContentActive(entering, true);

            tvRailRoot.SetActive(true);
            presenterRailRoot.SetActive(true);

            if (presentationManager != null)
                presentationManager.PlayPresenterAnimation(true);

            PlayEnter();
            yield return new WaitForSecondsRealtime(GetEntryDuration());

            currentMode = entering;
            SetContentActive(currentMode, true);
            if (currentMode == desiredMode)
                SetInteraction(true);
        }

        railTransitioning = false;
        transitionRoutine = null;

        if (currentMode != ShowMode.None && currentMode == desiredMode)
            SetInteraction(true);
    }

    private void SetContentActive(ShowMode mode, bool visible)
    {
        bool reward = visible && mode == ShowMode.Reward;
        bool map = visible && mode == ShowMode.Map;

        if (rewardRootRect != null && visible)
            rewardRootRect.gameObject.SetActive(true);

        if (rewardScreenRect != null)
            rewardScreenRect.gameObject.SetActive(reward);
        if (mapScreenRect != null)
            mapScreenRect.gameObject.SetActive(map);
        if (mapSelectionRect != null && map)
            mapSelectionRect.gameObject.SetActive(true);
        if (prizeChoiceRoot != null)
            prizeChoiceRoot.gameObject.SetActive(reward);

        if (rewardInventoryStripRect != null)
            rewardInventoryStripRect.gameObject.SetActive(false);

        if (equipmentDockRect != null && visible)
            equipmentDockRect.gameObject.SetActive(reward);
    }

    private void SetInteraction(bool enabledInteraction)
    {
        if (tvCanvasGroup != null)
        {
            tvCanvasGroup.interactable = enabledInteraction && currentMode == ShowMode.Map;
            tvCanvasGroup.blocksRaycasts = enabledInteraction && currentMode == ShowMode.Map;
        }
    }

    private void ResolveDockTargets()
    {
        if (TryGetLiveFieldBounds(out Bounds fieldBounds))
        {
            float tvWorldHeight = tvCanvasSize.y / Mathf.Max(32f, tvPixelsPerUnit);
            float tvBottom = fieldBounds.max.y - tvFieldOverlap;
            tvDockPosition = new Vector3(
                fieldBounds.center.x,
                tvBottom + tvWorldHeight * 0.5f,
                0f);

            presenterDockPosition = new Vector3(
                fieldBounds.max.x - presenterInsetX,
                fieldBounds.center.y + presenterYOffset,
                0f);
        }
        else
        {
            Vector3 fallback = player != null ? player.transform.position : Vector3.zero;
            float tvWorldHeight = tvCanvasSize.y / Mathf.Max(32f, tvPixelsPerUnit);
            tvDockPosition = fallback + new Vector3(0f, 3.2f + tvWorldHeight * 0.5f, 0f);
            presenterDockPosition = fallback + new Vector3(3.8f, 0.2f, 0f);
        }

        presenterDockPosition = ClampPresenterInsideCamera(presenterDockPosition);
    }

    private void KeepDockedStageOnCurrentField()
    {
        if (railTransitioning || currentMode == ShowMode.None)
            return;

        ResolveDockTargets();
        if (tvRailRoot != null && tvRailRoot.activeSelf)
            tvRailRoot.transform.position = tvDockPosition;
        if (presenterRailRoot != null && presenterRailRoot.activeSelf)
            presenterRailRoot.transform.position = presenterDockPosition;
    }

    private void PlayEnter()
    {
        if (tvRailBlock != null)
            tvRailBlock.PlayEnter(tvDockPosition, NormalizeDirection(tvRailDirection));
        if (presenterRailBlock != null)
            presenterRailBlock.PlayEnter(presenterDockPosition, NormalizeDirection(presenterRailDirection));
    }

    private void PlayExit()
    {
        if (tvRailBlock != null && tvRailRoot != null && tvRailRoot.activeSelf)
            tvRailBlock.PlayExit(NormalizeDirection(tvRailDirection));
        if (presenterRailBlock != null && presenterRailRoot != null && presenterRailRoot.activeSelf)
            presenterRailBlock.PlayExit(NormalizeDirection(presenterRailDirection));
    }

    private float GetEntryDuration()
    {
        float tvDuration = tvRailBlock != null ? tvRailBlock.GetEntryDuration() : tvEntryDuration;
        float presenterDuration = presenterRailBlock != null ? presenterRailBlock.GetEntryDuration() : presenterEntryDuration;
        return Mathf.Max(tvDuration, presenterDuration) + 0.03f;
    }

    private float GetExitDuration()
    {
        float tvDuration = tvRailBlock != null ? tvRailBlock.ExitDuration : tvEntryDuration;
        float presenterDuration = presenterRailBlock != null ? presenterRailBlock.ExitDuration : presenterEntryDuration;
        return Mathf.Max(tvDuration, presenterDuration) + 0.03f;
    }

    private void MaintainForegroundRewardUi()
    {
        if (runManager == null)
            return;

        bool reward = runManager.State == BattleRunState.Reward;

        if (rewardInventoryStripRect != null)
            rewardInventoryStripRect.gameObject.SetActive(false);

        // Reward 중에는 BattleHUD가 숨긴 기존 전투 Loadout을 그대로 다시 사용합니다.
        if (equipmentDockRect != null && reward)
            equipmentDockRect.gameObject.SetActive(true);

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
            Vector2 basePosition = new(
                startX + i * (width + prizeSlotSpacing),
                0f);

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

            Transform iconTransform = card.Find("PrizeIcon");
            if (iconTransform is RectTransform iconRect)
            {
                iconRect.sizeDelta = new Vector2(44f, 44f);
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.62f);
            }

            Text[] texts = card.GetComponentsInChildren<Text>(true);
            Text nameText = null;
            Text actionText = null;
            for (int t = 0; t < texts.Length; t++)
            {
                Text text = texts[t];
                if (text == null)
                    continue;

                if (text.text == "CLICK / DRAG")
                {
                    actionText = text;
                    continue;
                }

                if (nameText == null || text.fontSize > nameText.fontSize)
                    nameText = text;
            }

            for (int t = 0; t < texts.Length; t++)
            {
                Text text = texts[t];
                if (text == null)
                    continue;
                text.gameObject.SetActive(text == nameText || text == actionText);
            }

            if (nameText != null)
            {
                nameText.fontSize = 9;
                nameText.alignment = TextAnchor.MiddleCenter;
                SetAnchors(nameText.rectTransform, new Vector2(0.05f, 0.14f), new Vector2(0.95f, 0.34f));
            }

            if (actionText != null)
            {
                actionText.fontSize = 7;
                actionText.alignment = TextAnchor.MiddleCenter;
                SetAnchors(actionText.rectTransform, new Vector2(0.04f, 0.015f), new Vector2(0.96f, 0.13f));
            }
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

        Sprite sprite = ResolvePresenterSprite();
        if (sprite != lastPresenterSprite)
        {
            lastPresenterSprite = sprite;
            presenterRenderer.sprite = sprite;
            ApplyPresenterScale(sprite);
        }

        bool show = sprite != null &&
                    presenterRailRoot != null &&
                    presenterRailRoot.activeSelf &&
                    (currentMode != ShowMode.None || desiredMode != ShowMode.None || railTransitioning);
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
                    if (frames[i] != null)
                        return frames[i];
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
                "[BattleShowWorldSetController] 사회자 Sprite가 없습니다. " +
                "BattleShowPresentationManager.presenterFrames 또는 BattleHUD.presenterSprite를 할당하세요.",
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

    private void UpdateWorldSorting()
    {
        SpriteRenderer playerRenderer = player != null
            ? player.GetComponentInChildren<SpriteRenderer>(true)
            : null;

        if (playerRenderer != null)
        {
            if (tvCanvas != null)
            {
                tvCanvas.sortingLayerID = playerRenderer.sortingLayerID;

                int playerOrder = playerRenderer.sortingOrder;
                int tvOrder = playerOrder - Mathf.Max(1, tvBehindPlayerOrder);
                int highestFieldOrder = GetHighestFieldSortingOrder(playerRenderer.sortingLayerID);

                // TV는 바닥보다 앞, Player보다 뒤여야 합니다.
                if (highestFieldOrder < playerOrder)
                    tvOrder = Mathf.Max(tvOrder, highestFieldOrder + 1);
                tvOrder = Mathf.Min(tvOrder, playerOrder - 1);

                tvCanvas.sortingOrder = tvOrder;
            }

            if (presenterRenderer != null)
            {
                presenterRenderer.sortingLayerID = playerRenderer.sortingLayerID;
                presenterRenderer.sortingOrder =
                    playerRenderer.sortingOrder + Mathf.Max(1, presenterFrontOrder);
            }
        }
        else
        {
            if (tvCanvas != null)
                tvCanvas.sortingOrder = -10;
            if (presenterRenderer != null)
                presenterRenderer.sortingOrder = 30;
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

    private Vector3 ClampPresenterInsideCamera(Vector3 preferred)
    {
        Camera camera = Camera.main;
        Sprite sprite = ResolvePresenterSprite();
        if (camera == null || !camera.orthographic || sprite == null)
            return preferred;

        float aspect = sprite.bounds.size.y > 0.0001f
            ? Mathf.Abs(sprite.bounds.size.x / sprite.bounds.size.y)
            : 0.62f;
        float halfWidth = presenterWorldHeight * aspect * 0.5f;
        float halfHeight = presenterWorldHeight * 0.5f;

        Vector3 center = camera.transform.position;
        float cameraHalfHeight = camera.orthographicSize;
        float cameraHalfWidth = cameraHalfHeight * Mathf.Max(0.1f, camera.aspect);

        preferred.x = Mathf.Clamp(
            preferred.x,
            center.x - cameraHalfWidth + halfWidth + 0.2f,
            center.x + cameraHalfWidth - halfWidth - 0.2f);
        preferred.y = Mathf.Clamp(
            preferred.y,
            center.y - cameraHalfHeight + halfHeight + 0.2f,
            center.y + cameraHalfHeight - halfHeight - 0.2f);
        preferred.z = 0f;
        return preferred;
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
        rect.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
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

    private void KillTweens()
    {
        if (tvRailRoot != null)
            tvRailRoot.transform.DOKill();
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

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        if (rect == null)
            return;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
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

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Combat TAB의 Presentation 보정만 담당합니다.
/// - LIVE / Viewers / Likes는 전투 중 우측 상단에 항상 유지합니다.
/// - 평소에는 작은 Metric Bar, TAB을 열면 같은 자리에서 부드럽게 확대합니다.
/// - FAN MISSION 아래에 방송 스타일 Live Chat을 표시합니다.
/// - 실제 Viewer가 0명일 때는 채팅을 생성하지 않습니다.
/// - 0명 구간에서는 표시용 1~2명 유입이 간헐적으로 들어왔다가 빠지며, 가끔 짧은 이탈성 댓글을 남깁니다.
/// - Combat TAB의 PACK은 GridBoard 자체가 아니라 BroadcastPackDock 부모를 좌측 Rail에 맞춰 이동합니다.
/// - Equipment Detail은 PACK 가까이에 붙이고, 우측 Mission / Chat 포커스 중에는 숨깁니다.
/// - 우측 하단 CurrentLoadoutChip의 빨간 AccentSlash 장식은 숨깁니다.
/// - Reward / Equipment 데이터 소유권은 건드리지 않습니다.
/// </summary>
internal enum BattleCombatTabPrimaryFocus
{
    None,
    Pack,
    Rules
}

internal sealed class BattleCombatPackFocusPointerRelay : MonoBehaviour,
    UnityEngine.EventSystems.IPointerEnterHandler,
    UnityEngine.EventSystems.IPointerExitHandler
{
    private BattleCombatTabPresentationPolishController owner;

    public void Configure(BattleCombatTabPresentationPolishController controller)
    {
        owner = controller;
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        owner?.NotifyPackPointerEnter();
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
    {
        owner?.NotifyPackPointerExit();
    }
}


[DisallowMultipleComponent]
[DefaultExecutionOrder(33520)]
public sealed class BattleCombatTabPresentationPolishController : MonoBehaviour
{
    private const float DetailAnchorX = 0.55f;
    private const float DetailAnchorY = 0.52f;
    private const float DetailScale = 0.92f;
    private const int DetailSortingPadding = 15;
    private const int MetricSortingOrder = 1695;

    private const float ChatHeight = 184f;
    private const float ChatGap = 16f;
    private const float ZeroViewerChatInterval = 45f;
    private const float HighViewerChatInterval = 0.70f;
    private const int MaxChatLines = 5;

    private const float RightPanelEnterX = 0.61f;
    private const float RightPanelReturnX = 0.50f;

    private static readonly Vector2 MetricCompactOffset = new(-18f, -18f);
    private static readonly Vector2 MetricOpenOffset = new(-28f, -26f);
    private const float MetricCompactScale = 0.68f;
    private const float MetricOpenScale = 1f;
    private const float MetricCompactRotation = -0.35f;
    private const float MetricOpenRotation = -0.6f;
    private const float MetricTweenSharpness = 13f;

    private static readonly string[] ChatNames =
    {
        "MahoFan_17",
        "하루_404",
        "LunaMX",
        "cafezinhoBR",
        "Camille_FR",
        "BerlinByte",
        "pixelina",
        "kopiSusu",
        "AnkaraAim",
        "WarsawCrit",
        "SeoulWatcher",
        "clipHunter"
    };

    private static readonly string[] ChatComments =
    {
        "that PACK is getting scary",
        "방금 교체 진짜 깔끔했다",
        "esa build sí funciona",
        "isso ficou forte demais",
        "garde cet objet !",
        "das war knapp",
        "questa combo funziona davvero",
        "jangan buang item itu",
        "bu eşya kalsın, güçlü",
        "ten build zaczyna działać",
        "한 방 더 가자",
        "clean swap, keep going"
    };

    private static readonly string[] DriveByChatNames =
    {
        "guest_031",
        "지나가던사람",
        "wrong_tab",
        "noSignal",
        "ㅇㅇ",
        "justPassing"
    };

    private static readonly string[] DriveByChatComments =
    {
        "아 잘못 들어왔네",
        "아 노잼",
        "뭐야 아무도 없네",
        "잘못 눌렀다 ㅂㅂ",
        "음... 그냥 나갈게",
        "이 방송 뭐 하는 데임?"
    };

    private static readonly string[] PreferredMultilingualFonts =
    {
        "Malgun Gothic",
        "Arial Unicode MS",
        "Noto Sans CJK KR",
        "Segoe UI",
        "Arial"
    };

    [Header("AUTO REFERENCES")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleUIThemeController uiTheme;
    [SerializeField] private RunProgressSystem runProgress;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;
    [SerializeField] private BattleRuleRouletteController ruleRoulette;
    [SerializeField] private BattleCombatHudInputBridge inputBridge;

    [Header("COMBAT PACK DOCK POSITION")]
    [Tooltip("PACK Focus일 때 BroadcastPackDock 부모를 좌측 사선 Rail에 붙이는 X Offset입니다.")]
    [SerializeField] private float packFocusedDockX = -300f;
    [Tooltip("Mission/Chat Focus에서 PACK이 더 왼쪽으로 물러나는 BroadcastPackDock X Offset입니다.")]
    [SerializeField] private float missionFocusedDockX = -405f;
    [SerializeField] private float packDockY = 12f;
    [SerializeField, Range(4f, 30f)] private float packDockTweenSharpness = 16f;

    [Header("RULE DETAIL FOCUS")]
    [Tooltip("룰 패널 Focus 시 PACK을 오른쪽으로 비워주는 추가 X 이동량입니다.")]
    [SerializeField, Min(0f)] private float ruleDetailPackShiftX = 118f;
    [Tooltip("룰 패널 Focus 시 PACK 전체를 아래로 내려 시야에서 비워주는 Y 이동량입니다.")]
    [SerializeField, Min(0f)] private float ruleDetailPackDropY = 170f;
    [Tooltip("룰 패널 Focus 시 PACK GridBoard가 뒤로 물러나는 Scale 배율입니다.")]
    [SerializeField, Range(0.45f, 1f)] private float ruleDetailPackScale = 0.68f;
    [Tooltip("룰 패널 Focus 시 PACK의 비활성 상태 Alpha 배율입니다.")]
    [SerializeField, Range(0.20f, 1f)] private float ruleDetailPackAlpha = 0.56f;
    [Tooltip("룰 패널 Focus 시 방송 Dashboard를 오른쪽으로 비워주는 추가 X 이동량입니다.")]
    [SerializeField, Min(0f)] private float ruleDetailDashboardShiftX = 150f;
    [Tooltip("룰 패널 Focus 시 방송 Dashboard 전체가 뒤로 물러나는 Scale입니다.")]
    [SerializeField, Range(0.60f, 1f)] private float ruleDetailDashboardScale = 0.82f;
    [SerializeField, Range(4f, 30f)] private float ruleDetailShiftSharpness = 14f;

    [Header("LIVE CHAT — VIEWER PACED")]
    [SerializeField, Range(20f, 90f)] private float zeroViewerChatInterval = ZeroViewerChatInterval;
    [SerializeField, Range(0.35f, 4f)] private float highViewerChatInterval = HighViewerChatInterval;
    [SerializeField, Range(3, 7)] private int maxChatLines = MaxChatLines;

    [Header("ZERO VIEWER — AMBIENT DROP-IN")]
    [Tooltip("실제 Viewer가 0명일 때 1~2명이 들어오기까지의 최소 대기 시간입니다.")]
    [SerializeField, Range(2f, 30f)] private float ambientViewerIdleMin = 6f;
    [Tooltip("실제 Viewer가 0명일 때 1~2명이 들어오기까지의 최대 대기 시간입니다.")]
    [SerializeField, Range(3f, 45f)] private float ambientViewerIdleMax = 15f;
    [Tooltip("들어온 1~2명이 머무는 최소 시간입니다.")]
    [SerializeField, Range(1f, 12f)] private float ambientViewerStayMin = 3f;
    [Tooltip("들어온 1~2명이 머무는 최대 시간입니다.")]
    [SerializeField, Range(2f, 20f)] private float ambientViewerStayMax = 7f;
    [Tooltip("짧게 들어온 Viewer가 이탈성 댓글 하나를 남길 확률입니다.")]
    [SerializeField, Range(0f, 1f)] private float ambientDriveByCommentChance = 0.48f;

    private RectTransform fullRoot;
    private RectTransform packDockRoot;
    private RectTransform packBoardMotionRoot;
    private CanvasGroup packBoardMotionGroup;
    private RectTransform dashboardRoot;
    private RectTransform missionPanel;
    private Canvas dashboardCanvas;

    private RectTransform metricBar;
    private Canvas metricCanvas;
    private CanvasGroup metricGroup;
    private Text metricViewersText;
    private Text metricLikesText;

    private RectTransform chatPanel;
    private CanvasGroup chatGroup;
    private Text chatHeader;
    private Text chatBody;

    private Canvas detailCanvas;
    private int originalDetailSortingOrder;
    private bool originalDetailOverrideSorting;
    private bool detailSortingCaptured;
    private bool rightPanelFocused;
    private bool packDockTweenInitialized;
    private bool ruleDetailFocused;
    private BattleCombatTabPrimaryFocus primaryFocus = BattleCombatTabPrimaryFocus.None;
    private BattleCombatPackFocusPointerRelay packFocusRelay;
    private float packDockVisualX;
    private float packDockVisualY;
    private float packRuleVisualScale = 1f;
    private float packRuleVisualAlpha = 1f;
    private float dashboardRuleVisualOffsetX;
    private float dashboardRuleVisualScale = 1f;
    private Vector3 dashboardRuleBaseScale = Vector3.one;
    private bool dashboardRuleScaleCaptured;

    private readonly List<string> chatHistory = new();
    private int chatSequence;
    private float nextChatAt;
    private float nextResolveAt;

    private bool ambientViewerScheduled;
    private int ambientViewerCount;
    private float nextAmbientViewerAt;
    private float ambientViewerLeaveAt;
    private bool ambientCommentPending;
    private int ambientSequence;
    private int driveByChatSequence;

    private static Font multilingualFont;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TryInstall();
    }

    private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        TryInstall();
    }

    private static void TryInstall()
    {
        if (UnityEngine.Object.FindFirstObjectByType<BattleCombatTabPresentationPolishController>(FindObjectsInactive.Include) != null)
            return;

        BattleBroadcastDashboardController dashboard =
            UnityEngine.Object.FindFirstObjectByType<BattleBroadcastDashboardController>(FindObjectsInactive.Include);
        BattleKineticLoadoutUI loadout =
            UnityEngine.Object.FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
        BattleRunManager run =
            UnityEngine.Object.FindFirstObjectByType<BattleRunManager>(FindObjectsInactive.Include);

        GameObject host = dashboard != null
            ? dashboard.gameObject
            : loadout != null
                ? loadout.gameObject
                : run != null
                    ? run.gameObject
                    : GameObject.Find("BattleSystems");

        if (host != null)
            host.AddComponent<BattleCombatTabPresentationPolishController>();
    }

    private void Awake()
    {
        ResolveReferences(true);
    }

    private void OnEnable()
    {
        ResolveReferences(true);
        nextResolveAt = 0f;
        nextChatAt = 0f;
        rightPanelFocused = false;
        packDockTweenInitialized = false;
        ResetAmbientAudience();
    }

    private void OnDisable()
    {
        SetPrimaryFocus(BattleCombatTabPrimaryFocus.None);
        rightPanelFocused = false;
        packDockTweenInitialized = false;
        RestorePackRuleScale();
        RestoreDashboardRuleOffset();
        ResetAmbientAudience();
        RestoreDetailSorting();
        SetChatVisible(false);
        SetMetricVisible(false);
    }

    public bool CombatTabOpen => IsCombatTabOpen();

    public void SetRuleDetailFocus(bool focused)
    {
        if (focused)
            NotifyRulePointerEnter();
        else
            NotifyRulePointerExit();
    }

    public void NotifyRulePointerEnter()
    {
        if (!IsCombatTabOpen())
            return;

        SetPrimaryFocus(BattleCombatTabPrimaryFocus.Rules);
    }

    public void NotifyRulePointerExit()
    {
        if (primaryFocus == BattleCombatTabPrimaryFocus.Rules)
            SetPrimaryFocus(BattleCombatTabPrimaryFocus.None);
    }

    public void NotifyPackPointerEnter()
    {
        if (!IsCombatTabOpen())
            return;

        SetPrimaryFocus(BattleCombatTabPrimaryFocus.Pack);
    }

    public void NotifyPackPointerExit()
    {
        if (primaryFocus == BattleCombatTabPrimaryFocus.Pack)
            SetPrimaryFocus(BattleCombatTabPrimaryFocus.None);
    }

    private void SetPrimaryFocus(BattleCombatTabPrimaryFocus next)
    {
        if (!IsCombatTabOpen())
            next = BattleCombatTabPrimaryFocus.None;

        if (primaryFocus == next)
            return;

        primaryFocus = next;
        ruleDetailFocused = primaryFocus == BattleCombatTabPrimaryFocus.Rules;

        // RULES와 PACK은 동시에 포커스를 가질 수 없습니다.
        // RULES 진입 순간 기존 PACK 선택/hover를 즉시 해제합니다.
        if (ruleDetailFocused)
        {
            rightPanelFocused = false;
            kineticLoadout?.ClearExternalSelection();
            inputBridge?.ClearPackHoverImmediate();
        }
        else if (primaryFocus == BattleCombatTabPrimaryFocus.Pack)
        {
            rightPanelFocused = false;
        }

        ruleRoulette?.ApplyCombatRuleFocusFromCoordinator(ruleDetailFocused);
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextResolveAt)
        {
            nextResolveAt = Time.unscaledTime + 0.20f;
            ResolveReferences(false);
            ResolveDashboardUi();
            RemoveCompactAccentSlash();
        }

        bool combatActive = IsCombatActive();
        UpdateAmbientAudience(combatActive);

        bool tabOpen = IsCombatTabOpen();
        UpdateMetricOverlay(combatActive, tabOpen);

        if (!tabOpen)
        {
            SetPrimaryFocus(BattleCombatTabPrimaryFocus.None);
            rightPanelFocused = false;
            packDockTweenInitialized = false;
            RestoreDashboardRuleOffset();
            SetChatVisible(false);
            return;
        }

        EnsureChatPanel();
        SetChatVisible(true);
        UpdateChatFeed();
    }

    private void LateUpdate()
    {
        if (!IsCombatTabOpen())
        {
            SetPrimaryFocus(BattleCombatTabPrimaryFocus.None);
            rightPanelFocused = false;
            packDockTweenInitialized = false;
            RestoreDashboardRuleOffset();
            RestoreDetailSorting();
            return;
        }

        ResolveDashboardUi();
        EnsurePrimaryFocusRelay();

        // Primary focus는 Pointer Enter/Exit 이벤트만으로 변경됩니다.
        // LateUpdate에서는 상태를 다시 추론하지 않고 현재 상태의 시각 보간만 수행합니다.
        EnsureChatPanel();
        UpdateRightPanelFocus();
        ApplyCombatPackDockPosition();
        ApplyRuleDetailDashboardShift();
        ApplyDetailPresentation();
        ApplyChatLayout();
    }

    private bool IsCombatActive()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Combat;
    }

    private bool IsCombatTabOpen()
    {
        return IsCombatActive() && kineticLoadout != null && kineticLoadout.IsSwitchBoardOpen;
    }

    private void ResolveReferences(bool force)
    {
        if (force || runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (force || uiTheme == null)
            uiTheme = BattleUIThemeController.Instance != null
                ? BattleUIThemeController.Instance
                : FindFirstObjectByType<BattleUIThemeController>(FindObjectsInactive.Include);
        if (force || runProgress == null)
            runProgress = FindFirstObjectByType<RunProgressSystem>();
        if (force || kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
        if (force || detailController == null)
            detailController = FindFirstObjectByType<BattleEquipmentDetailPanelController>(FindObjectsInactive.Include);
        if (force || ruleRoulette == null)
            ruleRoulette = FindFirstObjectByType<BattleRuleRouletteController>(FindObjectsInactive.Include);
        if (force || inputBridge == null)
            inputBridge = FindFirstObjectByType<BattleCombatHudInputBridge>(FindObjectsInactive.Include);
    }

    private void ResolveDashboardUi()
    {
        if (fullRoot == null && kineticLoadout != null)
            fullRoot = kineticLoadout.FullRoot;

        if (packDockRoot == null && fullRoot != null)
            packDockRoot = fullRoot.Find("BroadcastPackDock") as RectTransform;

        if (packBoardMotionRoot == null && packDockRoot != null)
            packBoardMotionRoot =
                packDockRoot.Find("BroadcastPackBoardMotion") as RectTransform;

        if (packBoardMotionRoot != null && packBoardMotionGroup == null)
        {
            packBoardMotionGroup =
                packBoardMotionRoot.GetComponent<CanvasGroup>();
            if (packBoardMotionGroup == null)
                packBoardMotionGroup =
                    packBoardMotionRoot.gameObject.AddComponent<CanvasGroup>();

            packBoardMotionGroup.blocksRaycasts = true;
            packBoardMotionGroup.interactable = true;
        }

        if (dashboardRoot == null && fullRoot != null)
            dashboardRoot = fullRoot.Find("BroadcastDashboard") as RectTransform;

        if (dashboardRoot != null)
        {
            if (!dashboardRuleScaleCaptured)
            {
                dashboardRuleBaseScale = dashboardRoot.localScale;
                dashboardRuleVisualScale = 1f;
                dashboardRuleScaleCaptured = true;
            }

            dashboardCanvas ??= dashboardRoot.GetComponent<Canvas>();
            missionPanel ??= dashboardRoot.Find("MissionPanel") as RectTransform;

            if (chatPanel == null)
                chatPanel = dashboardRoot.Find("LiveChatPanel") as RectTransform;
        }

        if (metricBar == null)
        {
            if (fullRoot != null)
                metricBar = fullRoot.Find("BroadcastMetricBar") as RectTransform;
            if (metricBar == null && dashboardRoot != null)
                metricBar = dashboardRoot.Find("BroadcastMetricBar") as RectTransform;
        }

        EnsureMetricDetached();
    }

    private void EnsurePrimaryFocusRelay()
    {
        RectTransform board = kineticLoadout != null ? kineticLoadout.GridBoard : null;
        if (board == null)
            return;

        // 슬롯 사이의 빈 공간에서도 PACK 영역 Enter/Exit가 끊기지 않도록
        // 입력 전용 투명 Graphic을 가장 뒤에 둡니다. 슬롯 자체 입력은 그대로 우선합니다.
        RectTransform hitRegion = board.Find("PackPrimaryFocusHitRegion") as RectTransform;
        if (hitRegion == null)
        {
            hitRegion = CreateRect(board, "PackPrimaryFocusHitRegion", Vector2.zero);
            hitRegion.anchorMin = Vector2.zero;
            hitRegion.anchorMax = Vector2.one;
            hitRegion.offsetMin = Vector2.zero;
            hitRegion.offsetMax = Vector2.zero;
            hitRegion.SetAsFirstSibling();

            Image hitImage = hitRegion.gameObject.AddComponent<Image>();
            hitImage.color = Color.clear;
            hitImage.raycastTarget = true;
        }

        if (packFocusRelay == null || packFocusRelay.gameObject != board.gameObject)
        {
            packFocusRelay = board.GetComponent<BattleCombatPackFocusPointerRelay>();
            if (packFocusRelay == null)
                packFocusRelay = board.gameObject.AddComponent<BattleCombatPackFocusPointerRelay>();
        }

        packFocusRelay.Configure(this);
    }

    private void EnsureMetricDetached()
    {
        if (metricBar == null || fullRoot == null)
            return;

        if (metricBar.parent != fullRoot)
            metricBar.SetParent(fullRoot, false);

        metricBar.anchorMin = metricBar.anchorMax = Vector2.one;
        metricBar.pivot = Vector2.one;
        metricBar.SetAsLastSibling();

        metricCanvas = metricBar.GetComponent<Canvas>();
        if (metricCanvas == null)
            metricCanvas = metricBar.gameObject.AddComponent<Canvas>();
        metricCanvas.overrideSorting = true;
        metricCanvas.sortingOrder = MetricSortingOrder;

        metricGroup = metricBar.GetComponent<CanvasGroup>();
        if (metricGroup == null)
            metricGroup = metricBar.gameObject.AddComponent<CanvasGroup>();
        metricGroup.ignoreParentGroups = true;
        metricGroup.alpha = 1f;
        metricGroup.blocksRaycasts = false;
        metricGroup.interactable = false;

        metricViewersText ??= metricBar.Find("Viewers/Count")?.GetComponent<Text>();
        metricLikesText ??= metricBar.Find("Likes/Count")?.GetComponent<Text>();

        Transform textOnlyMetric = dashboardRoot != null ? dashboardRoot.Find("BroadcastMetricText") : null;
        if (textOnlyMetric != null && textOnlyMetric.gameObject.activeSelf)
            textOnlyMetric.gameObject.SetActive(false);
    }

    private void UpdateMetricOverlay(bool combatActive, bool tabOpen)
    {
        if (metricBar == null)
        {
            ResolveDashboardUi();
            if (metricBar == null)
                return;
        }

        SetMetricVisible(combatActive);
        if (!combatActive)
            return;

        EnsureMetricDetached();
        UpdateMetricCounts();

        float t = 1f - Mathf.Exp(-MetricTweenSharpness * Time.unscaledDeltaTime);
        Vector2 targetPosition = tabOpen ? MetricOpenOffset : MetricCompactOffset;
        float targetScale = tabOpen ? MetricOpenScale : MetricCompactScale;
        float targetRotation = tabOpen ? MetricOpenRotation : MetricCompactRotation;

        metricBar.anchoredPosition = Vector2.Lerp(metricBar.anchoredPosition, targetPosition, t);
        metricBar.localScale = Vector3.Lerp(metricBar.localScale, Vector3.one * targetScale, t);

        float currentRotation = NormalizeAngle(metricBar.localEulerAngles.z);
        float nextRotation = Mathf.LerpAngle(currentRotation, targetRotation, t);
        Quaternion targetSpatialRotation = Quaternion.Euler(
            tabOpen ? -1.5f : -0.5f,
            tabOpen ? -3.5f : -1.8f,
            nextRotation);
        metricBar.localRotation = Quaternion.Slerp(
            metricBar.localRotation,
            targetSpatialRotation,
            t);

        Vector3 metricLocal = metricBar.localPosition;
        metricLocal.z = Mathf.Lerp(metricLocal.z, tabOpen ? -8f : 2f, t);
        metricBar.localPosition = metricLocal;

        BattleSpatialGlassPanel metricGlass =
            metricBar.GetComponent<BattleSpatialGlassPanel>();
        metricGlass?.SetSpatialState(
            tabOpen ? 0.62f : 0.12f,
            tabOpen ? 0.45f : -0.18f);
    }

    private void SetMetricVisible(bool visible)
    {
        if (metricBar != null && metricBar.gameObject.activeSelf != visible)
            metricBar.gameObject.SetActive(visible);
    }

    private void UpdateMetricCounts()
    {
        if (runProgress == null)
            return;

        metricViewersText ??= metricBar != null ? metricBar.Find("Viewers/Count")?.GetComponent<Text>() : null;
        metricLikesText ??= metricBar != null ? metricBar.Find("Likes/Count")?.GetComponent<Text>() : null;

        string viewers = CurrentViewers.ToString("N0");
        string likes = Mathf.Max(0, runProgress.Likes).ToString("N0");

        if (metricViewersText != null && metricViewersText.text != viewers)
            metricViewersText.text = viewers;
        if (metricLikesText != null && metricLikesText.text != likes)
            metricLikesText.text = likes;
    }

    private void UpdateAmbientAudience(bool combatActive)
    {
        if (!combatActive || runProgress == null)
        {
            ResetAmbientAudience();
            return;
        }

        if (runProgress.Viewers > 0)
        {
            ResetAmbientAudience();
            return;
        }

        float now = Time.unscaledTime;
        if (!ambientViewerScheduled)
        {
            ambientViewerScheduled = true;
            nextAmbientViewerAt = now + NextAmbientRange(ambientViewerIdleMin, ambientViewerIdleMax);
        }

        if (ambientViewerCount > 0)
        {
            if (now < ambientViewerLeaveAt)
                return;

            ambientViewerCount = 0;
            ambientCommentPending = false;
            nextAmbientViewerAt = now + NextAmbientRange(ambientViewerIdleMin, ambientViewerIdleMax);
            return;
        }

        if (now < nextAmbientViewerAt)
            return;

        ambientViewerCount = NextAmbient01() < 0.72f ? 1 : 2;
        ambientViewerLeaveAt = now + NextAmbientRange(ambientViewerStayMin, ambientViewerStayMax);
        ambientCommentPending = NextAmbient01() < Mathf.Clamp01(ambientDriveByCommentChance);
    }

    private void ResetAmbientAudience()
    {
        ambientViewerScheduled = false;
        ambientViewerCount = 0;
        nextAmbientViewerAt = 0f;
        ambientViewerLeaveAt = 0f;
        ambientCommentPending = false;
    }

    private float NextAmbientRange(float min, float max)
    {
        min = Mathf.Max(0f, min);
        max = Mathf.Max(min, max);
        return Mathf.Lerp(min, max, NextAmbient01());
    }

    private float NextAmbient01()
    {
        unchecked
        {
            ambientSequence++;
            int hash = ambientSequence * 1103515245 + 12345;
            return (hash & 0x7fffffff) / (float)int.MaxValue;
        }
    }

    private void RemoveCompactAccentSlash()
    {
        RectTransform compactRoot = kineticLoadout != null ? kineticLoadout.CompactRoot : null;
        Transform accentSlash = compactRoot != null ? compactRoot.Find("AccentSlash") : null;
        if (accentSlash != null && accentSlash.gameObject.activeSelf)
            accentSlash.gameObject.SetActive(false);
    }

    private static float NormalizeAngle(float degrees)
    {
        return Mathf.Repeat(degrees + 180f, 360f) - 180f;
    }

    private void UpdateRightPanelFocus()
    {
        if (!Input.mousePresent)
        {
            rightPanelFocused = false;
            return;
        }

        Vector2 mouse = Input.mousePosition;

        bool insideMission = missionPanel != null && missionPanel.gameObject.activeInHierarchy &&
                             RectTransformUtility.RectangleContainsScreenPoint(missionPanel, mouse, null);
        bool insideChat = chatPanel != null && chatPanel.gameObject.activeInHierarchy &&
                          RectTransformUtility.RectangleContainsScreenPoint(chatPanel, mouse, null);

        if (insideMission || insideChat)
        {
            rightPanelFocused = true;
            return;
        }

        RectTransform packBoard = kineticLoadout != null ? kineticLoadout.GridBoard : null;
        bool insidePack = packBoard != null && packBoard.gameObject.activeInHierarchy &&
                          RectTransformUtility.RectangleContainsScreenPoint(packBoard, mouse, null);
        if (insidePack)
        {
            rightPanelFocused = false;
            return;
        }

        float normalizedX = Screen.width > 0 ? mouse.x / Screen.width : 0f;
        if (!rightPanelFocused && normalizedX >= RightPanelEnterX)
            rightPanelFocused = true;
        else if (rightPanelFocused && normalizedX <= RightPanelReturnX)
            rightPanelFocused = false;
    }

    private void ApplyCombatPackDockPosition()
    {
        if (kineticLoadout == null || kineticLoadout.GridBoard == null || fullRoot == null)
            return;

        if (packDockRoot == null)
            packDockRoot = fullRoot.Find("BroadcastPackDock") as RectTransform;
        if (packDockRoot == null)
            return;

        if (packBoardMotionRoot == null)
            packBoardMotionRoot =
                packDockRoot.Find("BroadcastPackBoardMotion") as RectTransform;
        if (packBoardMotionRoot == null)
            return;

        if (!packDockTweenInitialized)
        {
            packDockVisualX = packDockRoot.localPosition.x;
            packDockVisualY = packDockRoot.localPosition.y;
            packDockTweenInitialized = true;
        }

        float targetX = rightPanelFocused ? missionFocusedDockX : packFocusedDockX;
        float targetY = packDockY;

        float t = 1f - Mathf.Exp(
            -Mathf.Max(4f, packDockTweenSharpness) * Time.unscaledDeltaTime);

        packDockVisualX = Mathf.Lerp(packDockVisualX, targetX, t);
        packDockVisualY = Mathf.Lerp(packDockVisualY, targetY, t);

        if (Mathf.Abs(packDockVisualX - targetX) <= 0.25f)
            packDockVisualX = targetX;
        if (Mathf.Abs(packDockVisualY - targetY) <= 0.25f)
            packDockVisualY = targetY;

        packDockRoot.localPosition =
            new Vector3(packDockVisualX, packDockVisualY, 0f);

        // GridBoard 자체는 UnifiedInventoryInspectController가 매 프레임 0,0을 소유합니다.
        // 따라서 Rule Focus는 별도 부모 Wrapper를 움직여야 덮어쓰기 충돌이 없습니다.
        Vector2 targetMotionPosition = ruleDetailFocused
            ? new Vector2(0f, -Mathf.Max(0f, ruleDetailPackDropY))
            : Vector2.zero;

        packBoardMotionRoot.anchoredPosition = Vector2.Lerp(
            packBoardMotionRoot.anchoredPosition,
            targetMotionPosition,
            t);

        if ((packBoardMotionRoot.anchoredPosition - targetMotionPosition).sqrMagnitude <= 0.0625f)
            packBoardMotionRoot.anchoredPosition = targetMotionPosition;

        float targetRuleScale = ruleDetailFocused
            ? Mathf.Clamp(ruleDetailPackScale, 0.45f, 1f)
            : 1f;

        Vector3 targetMotionScale = Vector3.one * targetRuleScale;
        packBoardMotionRoot.localScale = Vector3.Lerp(
            packBoardMotionRoot.localScale,
            targetMotionScale,
            t);

        if ((packBoardMotionRoot.localScale - targetMotionScale).sqrMagnitude <= 0.000004f)
            packBoardMotionRoot.localScale = targetMotionScale;

        packRuleVisualScale = targetRuleScale;

        if (packBoardMotionGroup != null)
        {
            float targetRuleAlpha = ruleDetailFocused
                ? Mathf.Clamp01(ruleDetailPackAlpha)
                : 1f;

            packRuleVisualAlpha = Mathf.Lerp(
                packRuleVisualAlpha,
                targetRuleAlpha,
                t);

            if (Mathf.Abs(packRuleVisualAlpha - targetRuleAlpha) <= 0.002f)
                packRuleVisualAlpha = targetRuleAlpha;

            // 기존 GridBoard CanvasGroup은 Dashboard가 계속 단독 소유합니다.
            // RULE Focus 비활성 Alpha는 Wrapper 전용 CanvasGroup만 사용합니다.
            packBoardMotionGroup.alpha = packRuleVisualAlpha;
        }
    }

    private void ApplyRuleDetailDashboardShift()
    {
        if (dashboardRoot == null)
            return;

        Vector3 basePosition = dashboardRoot.localPosition;
        basePosition.x -= dashboardRuleVisualOffsetX;

        float t = 1f - Mathf.Exp(
            -Mathf.Max(4f, ruleDetailShiftSharpness) * Time.unscaledDeltaTime);

        // RULES는 PACK 부속 모듈이므로 방송/미션 영역까지 밀어내지 않습니다.
        dashboardRuleVisualOffsetX = Mathf.Lerp(
            dashboardRuleVisualOffsetX,
            0f,
            t);
        dashboardRuleVisualScale = Mathf.Lerp(
            dashboardRuleVisualScale,
            1f,
            t);

        if (Mathf.Abs(dashboardRuleVisualOffsetX) <= 0.25f)
            dashboardRuleVisualOffsetX = 0f;
        if (Mathf.Abs(dashboardRuleVisualScale - 1f) <= 0.002f)
            dashboardRuleVisualScale = 1f;

        basePosition.x += dashboardRuleVisualOffsetX;
        dashboardRoot.localPosition = basePosition;

        Vector3 baseScale = dashboardRuleScaleCaptured
            ? dashboardRuleBaseScale
            : Vector3.one;
        dashboardRoot.localScale = baseScale * dashboardRuleVisualScale;
    }

    private void RestorePackRuleScale()
    {
        if (packBoardMotionRoot == null && packDockRoot != null)
            packBoardMotionRoot =
                packDockRoot.Find("BroadcastPackBoardMotion") as RectTransform;

        if (packBoardMotionRoot != null)
        {
            packBoardMotionRoot.anchoredPosition = Vector2.zero;
            packBoardMotionRoot.localScale = Vector3.one;
            packBoardMotionRoot.localRotation = Quaternion.identity;
        }

        if (packBoardMotionGroup != null)
            packBoardMotionGroup.alpha = 1f;

        packRuleVisualScale = 1f;
        packRuleVisualAlpha = 1f;
        packDockVisualY = packDockY;
    }

    private void RestoreDashboardRuleOffset()
    {
        if (dashboardRoot != null)
        {
            if (Mathf.Abs(dashboardRuleVisualOffsetX) > 0.001f)
            {
                Vector3 position = dashboardRoot.localPosition;
                position.x -= dashboardRuleVisualOffsetX;
                dashboardRoot.localPosition = position;
            }

            if (dashboardRuleScaleCaptured)
                dashboardRoot.localScale = dashboardRuleBaseScale;
        }

        dashboardRuleVisualOffsetX = 0f;
        dashboardRuleVisualScale = 1f;
    }

    private void ApplyDetailPresentation()
    {
        if (detailController == null || detailController.Root == null)
            return;

        CanvasGroup detailGroup = detailController.Group;
        if (rightPanelFocused || ruleDetailFocused)
        {
            if (detailGroup != null)
            {
                detailGroup.alpha = 0f;
                detailGroup.blocksRaycasts = false;
                detailGroup.interactable = false;
            }

            RestoreDetailSorting();
            return;
        }

        if (detailGroup != null)
        {
            bool shouldShow = detailController.DisplayedSlot >= 0;
            detailGroup.alpha = shouldShow ? 1f : 0f;
            detailGroup.blocksRaycasts = false;
            detailGroup.interactable = false;
        }

        RectTransform detailRoot = detailController.Root;
        detailRoot.anchorMin = detailRoot.anchorMax = new Vector2(DetailAnchorX, DetailAnchorY);
        detailRoot.pivot = new Vector2(0.5f, 0.5f);
        detailRoot.anchoredPosition = Vector2.zero;
        detailRoot.localScale = Vector3.one * DetailScale;
        detailRoot.localRotation = Quaternion.identity;

        Canvas resolvedCanvas = detailRoot.GetComponentInParent<Canvas>();
        if (resolvedCanvas == null)
            return;

        if (detailCanvas != resolvedCanvas)
        {
            RestoreDetailSorting();
            detailCanvas = resolvedCanvas;
            originalDetailSortingOrder = detailCanvas.sortingOrder;
            originalDetailOverrideSorting = detailCanvas.overrideSorting;
            detailSortingCaptured = true;
        }

        int dashboardOrder = dashboardCanvas != null ? dashboardCanvas.sortingOrder : originalDetailSortingOrder;
        detailCanvas.overrideSorting = true;
        detailCanvas.sortingOrder = Mathf.Max(originalDetailSortingOrder, dashboardOrder + DetailSortingPadding);
    }

    private void RestoreDetailSorting()
    {
        if (!detailSortingCaptured || detailCanvas == null)
            return;

        detailCanvas.sortingOrder = originalDetailSortingOrder;
        detailCanvas.overrideSorting = originalDetailOverrideSorting;
        detailSortingCaptured = false;
        detailCanvas = null;
    }

    private void EnsureChatPanel()
    {
        if (dashboardRoot == null)
            return;

        if (chatPanel == null)
        {
            chatPanel = CreateRect(dashboardRoot, "LiveChatPanel", new Vector2(540f, ChatHeight));
            chatPanel.anchorMin = chatPanel.anchorMax = Vector2.one;
            chatPanel.pivot = Vector2.one;
        }

        chatPanel.SetAsLastSibling();

        Image back = chatPanel.GetComponent<Image>();
        if (back == null)
            back = chatPanel.gameObject.AddComponent<Image>();
        back.enabled = true;
        back.color = Color.clear;
        back.raycastTarget = false;

        Outline outline = chatPanel.GetComponent<Outline>();
        if (outline == null)
            outline = chatPanel.gameObject.AddComponent<Outline>();
        outline.enabled = false;

        BattleSpatialGlassPanel chatGlass =
            chatPanel.GetComponent<BattleSpatialGlassPanel>();
        if (chatGlass == null)
            chatGlass = chatPanel.gameObject.AddComponent<BattleSpatialGlassPanel>();
        chatGlass.Configure(false, 0.10f, -0.040f);

        chatGroup = chatPanel.GetComponent<CanvasGroup>();
        if (chatGroup == null)
            chatGroup = chatPanel.gameObject.AddComponent<CanvasGroup>();
        chatGroup.blocksRaycasts = false;
        chatGroup.interactable = false;

        chatHeader = chatPanel.Find("Header")?.GetComponent<Text>();
        if (chatHeader == null)
            chatHeader = CreateText(chatPanel, "LIVE CHAT  //  VIEWER FEED", 13, FontStyle.Bold,
                TextAnchor.MiddleLeft, new Color(0.10f, 0.88f, 0.95f, 1f), "Header", ResolveMultilingualFont());

        SetAnchors(chatHeader.rectTransform, new Vector2(0.045f, 0.76f), new Vector2(0.95f, 0.96f));
        chatHeader.font = ResolveMultilingualFont();
        chatHeader.fontSize = 13;
        chatHeader.fontStyle = FontStyle.Bold;
        chatHeader.alignment = TextAnchor.MiddleLeft;
        BattleUIThemeProfile activeTheme = uiTheme != null ? uiTheme.CurrentProfile : null;
        chatHeader.color = activeTheme != null
            ? activeTheme.keyColor
            : new Color(1f, 0.82f, 0.10f, 1f);
        chatHeader.raycastTarget = false;

        RectTransform divider = chatPanel.Find("Divider") as RectTransform;
        if (divider != null && divider.gameObject.activeSelf)
            divider.gameObject.SetActive(false);

        chatBody = chatPanel.Find("Body")?.GetComponent<Text>();
        if (chatBody == null)
            chatBody = CreateText(chatPanel, string.Empty, 14, FontStyle.Normal,
                TextAnchor.UpperLeft, new Color(0.94f, 0.95f, 0.97f, 0.98f), "Body", ResolveMultilingualFont());

        SetAnchors(chatBody.rectTransform, new Vector2(0.045f, 0.06f), new Vector2(0.955f, 0.70f));
        chatBody.font = ResolveMultilingualFont();
        chatBody.fontSize = 14;
        chatBody.fontStyle = FontStyle.Normal;
        chatBody.alignment = TextAnchor.UpperLeft;
        chatBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        chatBody.verticalOverflow = VerticalWrapMode.Truncate;
        chatBody.lineSpacing = 1.10f;
        chatBody.supportRichText = true;
        chatBody.raycastTarget = false;
        chatBody.color = activeTheme != null
            ? activeTheme.textPrimary
            : new Color(0.94f, 0.95f, 0.97f, 0.98f);

        Shadow bodyShadow = chatBody.GetComponent<Shadow>();
        if (bodyShadow == null)
            bodyShadow = chatBody.gameObject.AddComponent<Shadow>();
        bodyShadow.effectColor = new Color(0f, 0f, 0f, 0.82f);
        bodyShadow.effectDistance = new Vector2(1.5f, -1.5f);

        SeedChatIfNeeded();
        ApplyChatLayout();
    }

    private void ApplyChatLayout()
    {
        if (chatPanel == null || missionPanel == null || dashboardRoot == null)
            return;

        float width = Mathf.Clamp(missionPanel.sizeDelta.x, 420f, 620f);
        float desiredY = missionPanel.anchoredPosition.y - missionPanel.sizeDelta.y - ChatGap;

        float rootHeight = Mathf.Max(720f, dashboardRoot.rect.height);
        float minimumTopY = -rootHeight + ChatHeight + 24f;
        float chatY = Mathf.Max(desiredY, minimumTopY);

        chatPanel.sizeDelta = new Vector2(width, ChatHeight);
        chatPanel.anchorMin = chatPanel.anchorMax = Vector2.one;
        chatPanel.pivot = Vector2.one;
        chatPanel.anchoredPosition = new Vector2(missionPanel.anchoredPosition.x, chatY);
        float spatialT = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
        Quaternion targetRotation = rightPanelFocused
            ? Quaternion.Euler(-1.0f, -2.0f, 0f)
            : Quaternion.Euler(1.8f, -6.0f, 0f);
        chatPanel.localRotation = Quaternion.Slerp(
            chatPanel.localRotation,
            targetRotation,
            spatialT);
        chatPanel.localScale = Vector3.Lerp(
            chatPanel.localScale,
            Vector3.one * (rightPanelFocused ? 1f : 0.965f),
            spatialT);

        Vector3 chatLocal = chatPanel.localPosition;
        chatLocal.z = Mathf.Lerp(
            chatLocal.z,
            rightPanelFocused ? -10f : 4f,
            spatialT);
        chatPanel.localPosition = chatLocal;

        BattleSpatialGlassPanel chatGlass =
            chatPanel.GetComponent<BattleSpatialGlassPanel>();
        chatGlass?.SetSpatialState(
            rightPanelFocused ? 0.85f : 0.10f,
            rightPanelFocused ? 0.65f : -0.28f);
    }

    private void SetChatVisible(bool visible)
    {
        if (chatPanel != null && chatPanel.gameObject.activeSelf != visible)
            chatPanel.gameObject.SetActive(visible);

        if (chatGroup == null)
            return;

        chatGroup.alpha = visible ? 1f : 0f;
        chatGroup.blocksRaycasts = false;
        chatGroup.interactable = false;
    }

    private int BaseViewers => runProgress != null ? Mathf.Max(0, runProgress.Viewers) : 0;
    private bool IsAmbientViewerVisit => BaseViewers <= 0 && ambientViewerCount > 0;
    private int CurrentViewers => BaseViewers > 0 ? BaseViewers : Mathf.Max(0, ambientViewerCount);

    private void SeedChatIfNeeded()
    {
        if (chatBody == null)
            return;

        int viewers = CurrentViewers;
        if (viewers <= 0 || IsAmbientViewerVisit)
            return;

        if (chatHistory.Count > 0)
        {
            RefreshChatBody();
            return;
        }

        int seedCount = viewers >= 1000 ? 5 : viewers >= 100 ? 4 : viewers >= 10 ? 3 : 1;
        seedCount = Mathf.Min(seedCount, Mathf.Clamp(maxChatLines, 3, 7));

        for (int i = 0; i < seedCount; i++)
            AppendNextChatLine();

        ScheduleNextChat();
    }

    private void UpdateChatFeed()
    {
        if (chatBody == null)
            return;

        int viewers = CurrentViewers;
        if (viewers <= 0)
            return;

        if (IsAmbientViewerVisit)
        {
            if (ambientCommentPending)
            {
                AppendDriveByChatLine();
                ambientCommentPending = false;
            }
            return;
        }

        SeedChatIfNeeded();
        if (Time.unscaledTime < nextChatAt)
            return;

        AppendNextChatLine();
        ScheduleNextChat();
    }

    private void ScheduleNextChat()
    {
        int viewers = CurrentViewers;
        if (viewers <= 0)
        {
            nextChatAt = float.PositiveInfinity;
            return;
        }

        nextChatAt = Time.unscaledTime + CalculateViewerPacedInterval(viewers);
    }

    private float CalculateViewerPacedInterval(int viewers)
    {
        viewers = Mathf.Max(0, viewers);
        if (viewers <= 0)
            return float.PositiveInfinity;

        float slow = Mathf.Max(20f, zeroViewerChatInterval);
        float fast = Mathf.Clamp(highViewerChatInterval, 0.35f, 4f);
        float baseInterval;

        if (viewers < 10)
            baseInterval = Mathf.Lerp(slow * 0.78f, 24f, viewers / 10f);
        else if (viewers < 100)
            baseInterval = Mathf.Lerp(24f, 10f, (viewers - 10f) / 90f);
        else if (viewers < 1000)
            baseInterval = Mathf.Lerp(10f, 2.5f, (viewers - 100f) / 900f);
        else
            baseInterval = Mathf.Lerp(2.5f, fast, Mathf.Clamp01((viewers - 1000f) / 9000f));

        int hash = unchecked((chatSequence + 1) * 73856093 ^ (viewers + 17) * 19349663);
        float normalized = Mathf.Abs(hash % 1000) / 999f;
        float jitter = Mathf.Lerp(0.88f, 1.12f, normalized);
        return Mathf.Max(fast, baseInterval * jitter);
    }

    private void AppendNextChatLine()
    {
        int index = chatSequence % Mathf.Min(ChatNames.Length, ChatComments.Length);
        string nickname = ChatNames[index];
        string comment = ChatComments[index];
        chatSequence++;

        AppendChatLine(nickname, comment);
    }

    private void AppendDriveByChatLine()
    {
        int count = Mathf.Min(DriveByChatNames.Length, DriveByChatComments.Length);
        if (count <= 0)
            return;

        int index = driveByChatSequence % count;
        driveByChatSequence++;
        AppendChatLine(DriveByChatNames[index], DriveByChatComments[index]);
    }

    private void AppendChatLine(string nickname, string comment)
    {
        chatHistory.Add($"<color=#19E0F2><b>{nickname}</b></color>  {comment}");

        int keep = Mathf.Clamp(maxChatLines, 3, 7);
        while (chatHistory.Count > keep)
            chatHistory.RemoveAt(0);

        RefreshChatBody();
    }

    private void RefreshChatBody()
    {
        if (chatBody != null)
            chatBody.text = string.Join("\n", chatHistory);
    }

    private static Font ResolveMultilingualFont()
    {
        if (multilingualFont != null)
            return multilingualFont;

        string[] installed = Font.GetOSInstalledFontNames();
        for (int i = 0; i < PreferredMultilingualFonts.Length; i++)
        {
            string preferred = PreferredMultilingualFonts[i];
            for (int j = 0; j < installed.Length; j++)
            {
                if (!string.Equals(installed[j], preferred, StringComparison.OrdinalIgnoreCase))
                    continue;

                multilingualFont = Font.CreateDynamicFontFromOSFont(installed[j], 16);
                if (multilingualFont != null)
                    return multilingualFont;
            }
        }

        multilingualFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return multilingualFont;
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static Text CreateText(
        Transform parent,
        string value,
        int fontSize,
        FontStyle style,
        TextAnchor alignment,
        Color color,
        string objectName,
        Font font = null)
    {
        RectTransform rect = CreateRect(parent, objectName, Vector2.zero);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Combat TAB의 방송 정보 영역을 담당합니다.
///
/// 소유권:
/// - BattleKineticLoadoutUI: PACK / GridBoard / Slot의 실제 Scale, Rotation, Z-depth.
/// - 이 Controller: Fan Mission, Viewer/Like Metrics, Chat 표시.
/// - Gameplay 데이터는 읽기만 하며 RunProgress/FanMission 데이터를 수정하지 않습니다.
///
/// 원칙:
/// - 장식용 선/띠/이미지를 만들지 않습니다.
/// - 상태는 Alpha만이 아니라 XYZ Rotation + Z-depth + Scale로 표현합니다.
/// - Combat/TAB 상태 발견은 Event 기반이며 Update는 Pointer/시간/보간만 담당합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33440)]
public sealed class BattleBroadcastDashboardController : MonoBehaviour
{
    private enum DashboardState
    {
        Hidden,
        Entering,
        Open,
        Exiting
    }

    private enum DashboardFocus
    {
        Pack,
        Mission
    }

    private const int MaxMissionSlots = 6;
    private const int DashboardSortingOrder = 1685;
    private const int MaxChatLines = 5;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private FanMissionSystem fanMissionSystem;
    [SerializeField] private RunProgressSystem runProgress;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;

    [Header("Motion")]
    [SerializeField, Range(4f, 30f)] private float layoutSharpness = 14f;

    [Header("Mission")]
    [SerializeField] private Vector2 standbyMissionSize = new(320f, 72f);
    [SerializeField] private Vector2 compactMissionSize = new(520f, 300f);
    [SerializeField] private Vector2 focusedMissionSize = new(760f, 560f);
    [SerializeField] private Vector2 missionPanelOffset = new(-32f, -112f);
    [SerializeField] private Vector2 focusedMissionPanelOffset = new(-38f, -126f);

    [Header("Chat")]
    [SerializeField] private Vector2 chatSize = new(520f, 228f);

    [Header("Functional Colors")]
    [SerializeField] private Color inkColor = new(0.020f, 0.024f, 0.032f, 0.97f);
    [SerializeField] private Color panelColor = new(0.040f, 0.046f, 0.058f, 0.98f);
    [SerializeField] private Color paperColor = new(0.93f, 0.95f, 0.97f, 1f);
    [SerializeField] private Color mutedColor = new(0.48f, 0.52f, 0.60f, 1f);
    [SerializeField] private Color activeColor = new(0.12f, 0.86f, 0.92f, 1f);
    [SerializeField] private Color progressColor = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color dangerColor = new(1f, 0.20f, 0.38f, 1f);

    private RectTransform fullRoot;

    private RectTransform dashboardRoot;
    private CanvasGroup dashboardGroup;

    private RectTransform metricBar;
    private Text viewersText;
    private Text likesText;

    private RectTransform missionPanel;
    private CanvasGroup missionPanelGroup;
    private Text missionHeader;
    private Text missionRange;
    private Text missionEmptyState;
    private RectTransform missionListRoot;
    private RectTransform missionDetailRoot;
    private CanvasGroup missionDetailGroup;
    private Text missionDetailTitle;
    private Text missionDetailType;
    private Text missionDetailDescription;
    private Text missionDetailProgress;
    private Text missionDetailTime;
    private Text missionDetailReward;
    private Text missionDetailFailure;

    private readonly RectTransform[] missionRows = new RectTransform[MaxMissionSlots];
    private readonly CanvasGroup[] missionRowGroups = new CanvasGroup[MaxMissionSlots];
    private readonly Image[] missionRowBackgrounds = new Image[MaxMissionSlots];
    private readonly Outline[] missionRowOutlines = new Outline[MaxMissionSlots];
    private readonly Text[] missionRowTitles = new Text[MaxMissionSlots];
    private readonly Text[] missionRowProgress = new Text[MaxMissionSlots];
    private readonly bool[] missionRowActive = new bool[MaxMissionSlots];

    private RectTransform chatPanel;
    private CanvasGroup chatGroup;
    private Text chatHeader;
    private Text chatBody;
    private readonly List<string> chatHistory = new();

    private DashboardState state = DashboardState.Hidden;
    private DashboardFocus focus = DashboardFocus.Pack;
    private int selectedMissionIndex = -1;
    private int hoveredMissionIndex = -1;
    private int activeMissionCount;
    private int currentViewers;

    private BattleRunManager subscribedRunManager;
    private BattleKineticLoadoutUI subscribedLoadout;
    private FanMissionSystem subscribedMissionSystem;
    private RunProgressSystem subscribedRunProgress;
    private Coroutine bindRoutine;

    private void Awake()
    {
        ResolveReferences();
        TryResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        TryResolveUi();

        if (!ReferencesReady() || dashboardRoot == null)
            bindRoutine = StartCoroutine(BindWhenReady());
        else
            RefreshOpenState(true);
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
            StopCoroutine(bindRoutine);
        bindRoutine = null;

        Unsubscribe();
        state = DashboardState.Hidden;

        if (dashboardRoot != null)
            dashboardRoot.gameObject.SetActive(false);
    }

    private IEnumerator BindWhenReady()
    {
        while (isActiveAndEnabled)
        {
            ResolveReferences();
            Subscribe();
            TryResolveUi();

            if (ReferencesReady() && dashboardRoot != null)
            {
                bindRoutine = null;
                RefreshOpenState(true);
                yield break;
            }

            yield return null;
        }

        bindRoutine = null;
    }

    private bool ReferencesReady()
    {
        return runManager != null &&
               fanMissionSystem != null &&
               runProgress != null &&
               kineticLoadout != null;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (fanMissionSystem == null)
            fanMissionSystem = FindFirstObjectByType<FanMissionSystem>();
        if (runProgress == null)
            runProgress = FindFirstObjectByType<RunProgressSystem>();
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
    }

    private void Subscribe()
    {
        if (subscribedRunManager != runManager)
        {
            if (subscribedRunManager != null)
                subscribedRunManager.StateChanged -= HandleRunStateChanged;

            subscribedRunManager = runManager;
            if (subscribedRunManager != null)
                subscribedRunManager.StateChanged += HandleRunStateChanged;
        }

        if (subscribedLoadout != kineticLoadout)
        {
            if (subscribedLoadout != null)
                subscribedLoadout.SwitchBoardVisibilityChanged -= HandlePackVisibilityChanged;

            subscribedLoadout = kineticLoadout;
            if (subscribedLoadout != null)
                subscribedLoadout.SwitchBoardVisibilityChanged += HandlePackVisibilityChanged;
        }

        if (subscribedMissionSystem != fanMissionSystem)
        {
            if (subscribedMissionSystem != null)
                subscribedMissionSystem.MissionsChanged -= HandleMissionsChanged;

            subscribedMissionSystem = fanMissionSystem;
            if (subscribedMissionSystem != null)
                subscribedMissionSystem.MissionsChanged += HandleMissionsChanged;
        }

        if (subscribedRunProgress != runProgress)
        {
            if (subscribedRunProgress != null)
                subscribedRunProgress.BroadcastMetricsChanged -= HandleBroadcastMetricsChanged;

            subscribedRunProgress = runProgress;
            if (subscribedRunProgress != null)
                subscribedRunProgress.BroadcastMetricsChanged += HandleBroadcastMetricsChanged;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedRunManager != null)
            subscribedRunManager.StateChanged -= HandleRunStateChanged;
        if (subscribedLoadout != null)
            subscribedLoadout.SwitchBoardVisibilityChanged -= HandlePackVisibilityChanged;
        if (subscribedMissionSystem != null)
            subscribedMissionSystem.MissionsChanged -= HandleMissionsChanged;
        if (subscribedRunProgress != null)
            subscribedRunProgress.BroadcastMetricsChanged -= HandleBroadcastMetricsChanged;

        subscribedRunManager = null;
        subscribedLoadout = null;
        subscribedMissionSystem = null;
        subscribedRunProgress = null;
    }

    private void HandleRunStateChanged(BattleRunState _)
    {
        RefreshOpenState(false);
    }

    private void HandlePackVisibilityChanged(bool _)
    {
        RefreshOpenState(false);
    }

    private void HandleMissionsChanged()
    {
        RefreshMissionData();
    }

    private void HandleBroadcastMetricsChanged(int viewers, int likes)
    {
        currentViewers = Mathf.Max(0, viewers);
        ApplyBroadcastMetrics(currentViewers, Mathf.Max(0, likes));
        RefreshChatState();
    }

    private void RefreshOpenState(bool force)
    {
        bool shouldOpen =
            runManager != null &&
            runManager.RunActive &&
            runManager.State == BattleRunState.Combat &&
            kineticLoadout != null &&
            kineticLoadout.IsSwitchBoardOpen;

        bool currentlyOpen =
            state == DashboardState.Entering ||
            state == DashboardState.Open;

        if (!force && shouldOpen == currentlyOpen)
            return;

        if (shouldOpen)
        {
            TryResolveUi();
            if (dashboardRoot == null)
                return;

            dashboardRoot.gameObject.SetActive(true);
            state = DashboardState.Entering;
            focus = DashboardFocus.Pack;
            hoveredMissionIndex = -1;
            RefreshMissionData();
            currentViewers = runProgress != null ? Mathf.Max(0, runProgress.Viewers) : 0;
            int likes = runProgress != null ? Mathf.Max(0, runProgress.Likes) : 0;
            ApplyBroadcastMetrics(currentViewers, likes);
            RefreshChatState();
        }
        else if (state != DashboardState.Hidden)
        {
            state = DashboardState.Exiting;
            focus = DashboardFocus.Pack;
            hoveredMissionIndex = -1;
        }
    }

    private void Update()
    {
        if (state == DashboardState.Hidden || dashboardRoot == null)
            return;

        if (state == DashboardState.Entering || state == DashboardState.Open)
        {
            TrackPointerFocus();

            // RemainingTime 자체는 FanMissionSystem의 gameplay timer가 갱신합니다.
            // 여기서는 이미 선택된 미션의 표시 문자열만 갱신합니다.
            if (focus == DashboardFocus.Mission)
                RefreshMissionDetail();
        }

        AnimatePresentation();
    }

    private void TryResolveUi()
    {
        if (kineticLoadout == null)
            return;

        fullRoot = kineticLoadout.FullRoot;
        if (fullRoot == null)
            return;

        if (dashboardRoot == null)
            BuildDashboardUi();
    }

    private void BuildDashboardUi()
    {
        RectTransform existing = fullRoot.Find("BroadcastDashboard") as RectTransform;
        if (existing != null)
            Object.Destroy(existing.gameObject);

        dashboardRoot = CreateRect(fullRoot, "BroadcastDashboard", Vector2.zero);
        Stretch(dashboardRoot);
        dashboardRoot.SetAsLastSibling();

        Canvas canvas = dashboardRoot.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = DashboardSortingOrder;

        dashboardGroup = dashboardRoot.gameObject.AddComponent<CanvasGroup>();
        dashboardGroup.alpha = 0f;
        dashboardGroup.blocksRaycasts = false;
        dashboardGroup.interactable = false;

        BuildMetricBar();
        BuildMissionPanel();
        BuildChatPanel();

        dashboardRoot.gameObject.SetActive(false);
    }

    private void BuildMetricBar()
    {
        metricBar = CreateRect(dashboardRoot, "BroadcastMetricBar", new Vector2(470f, 64f));
        metricBar.anchorMin = metricBar.anchorMax = Vector2.one;
        metricBar.pivot = Vector2.one;
        metricBar.anchoredPosition = new Vector2(-28f, -24f);
        metricBar.localRotation = Quaternion.Euler(0.5f, -2.2f, -0.35f);

        Image back = metricBar.gameObject.AddComponent<Image>();
        back.color = inkColor;
        back.raycastTarget = false;

        Outline outline = metricBar.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(paperColor.r, paperColor.g, paperColor.b, 0.22f);
        outline.effectDistance = new Vector2(2f, -2f);

        Text live = CreateText(metricBar, "LIVE", 11, FontStyle.Bold, TextAnchor.MiddleLeft, dangerColor, "Live");
        SetAnchors(live.rectTransform, new Vector2(0.04f, 0.12f), new Vector2(0.18f, 0.88f));

        viewersText = CreateText(metricBar, "VIEWERS 0", 16, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor, "Viewers");
        SetAnchors(viewersText.rectTransform, new Vector2(0.22f, 0.08f), new Vector2(0.60f, 0.92f));

        likesText = CreateText(metricBar, "LIKES 0", 16, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor, "Likes");
        SetAnchors(likesText.rectTransform, new Vector2(0.62f, 0.08f), new Vector2(0.96f, 0.92f));
    }

    private void BuildMissionPanel()
    {
        missionPanel = CreateRect(dashboardRoot, "MissionPanel", standbyMissionSize);
        missionPanel.anchorMin = missionPanel.anchorMax = Vector2.one;
        missionPanel.pivot = Vector2.one;
        missionPanel.anchoredPosition = missionPanelOffset;

        Image back = missionPanel.gameObject.AddComponent<Image>();
        back.color = panelColor;
        back.raycastTarget = false;

        Outline outline = missionPanel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(mutedColor.r, mutedColor.g, mutedColor.b, 0.42f);
        outline.effectDistance = new Vector2(2f, -2f);

        missionPanelGroup = missionPanel.gameObject.AddComponent<CanvasGroup>();
        missionPanelGroup.blocksRaycasts = false;
        missionPanelGroup.interactable = false;

        missionHeader = CreateText(
            missionPanel,
            "FAN MISSION",
            18,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            paperColor,
            "Header");
        SetAnchors(missionHeader.rectTransform, new Vector2(0.055f, 0.62f), new Vector2(0.72f, 0.94f));

        missionRange = CreateText(
            missionPanel,
            "ACTIVE 0 / 3",
            10,
            FontStyle.Bold,
            TextAnchor.MiddleRight,
            mutedColor,
            "Range");
        SetAnchors(missionRange.rectTransform, new Vector2(0.65f, 0.66f), new Vector2(0.945f, 0.93f));

        missionEmptyState = CreateText(
            missionPanel,
            "STANDBY",
            11,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            mutedColor,
            "EmptyState");
        SetAnchors(missionEmptyState.rectTransform, new Vector2(0.055f, 0.08f), new Vector2(0.945f, 0.48f));

        missionListRoot = CreateRect(missionPanel, "MissionList", Vector2.zero);
        missionListRoot.anchorMin = new Vector2(0.055f, 0.08f);
        missionListRoot.anchorMax = new Vector2(0.945f, 0.68f);
        missionListRoot.offsetMin = Vector2.zero;
        missionListRoot.offsetMax = Vector2.zero;

        for (int i = 0; i < MaxMissionSlots; i++)
            BuildMissionRow(i);

        BuildMissionDetail();
    }

    private void BuildMissionRow(int index)
    {
        RectTransform row = CreateRect(missionListRoot, $"MissionRow_{index}", new Vector2(0f, 42f));
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0f, -index * 48f);

        Image background = row.gameObject.AddComponent<Image>();
        background.color = new Color(0.055f, 0.060f, 0.074f, 0.98f);
        background.raycastTarget = false;

        Outline outline = row.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(mutedColor.r, mutedColor.g, mutedColor.b, 0.22f);
        outline.effectDistance = new Vector2(1f, -1f);

        CanvasGroup group = row.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        Text title = CreateText(row, $"MISSION {index + 1}", 13, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor, "Title");
        SetAnchors(title.rectTransform, new Vector2(0.04f, 0.08f), new Vector2(0.72f, 0.92f));

        Text progress = CreateText(row, "-- / --", 12, FontStyle.Bold, TextAnchor.MiddleRight, progressColor, "Progress");
        SetAnchors(progress.rectTransform, new Vector2(0.70f, 0.08f), new Vector2(0.96f, 0.92f));

        missionRows[index] = row;
        missionRowGroups[index] = group;
        missionRowBackgrounds[index] = background;
        missionRowOutlines[index] = outline;
        missionRowTitles[index] = title;
        missionRowProgress[index] = progress;
    }

    private void BuildMissionDetail()
    {
        missionDetailRoot = CreateRect(missionPanel, "MissionDetail", Vector2.zero);
        missionDetailRoot.anchorMin = new Vector2(0.45f, 0.08f);
        missionDetailRoot.anchorMax = new Vector2(0.95f, 0.68f);
        missionDetailRoot.offsetMin = Vector2.zero;
        missionDetailRoot.offsetMax = Vector2.zero;

        Image back = missionDetailRoot.gameObject.AddComponent<Image>();
        back.color = new Color(0.025f, 0.030f, 0.040f, 0.98f);
        back.raycastTarget = false;

        Outline outline = missionDetailRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(activeColor.r, activeColor.g, activeColor.b, 0.34f);
        outline.effectDistance = new Vector2(2f, -2f);

        missionDetailGroup = missionDetailRoot.gameObject.AddComponent<CanvasGroup>();
        missionDetailGroup.alpha = 0f;
        missionDetailGroup.blocksRaycasts = false;
        missionDetailGroup.interactable = false;

        missionDetailTitle = CreateText(missionDetailRoot, "NO ACTIVE MISSION", 22, FontStyle.Bold, TextAnchor.UpperLeft, paperColor, "Title");
        SetAnchors(missionDetailTitle.rectTransform, new Vector2(0.06f, 0.78f), new Vector2(0.94f, 0.95f));

        missionDetailType = CreateText(missionDetailRoot, "TYPE // STANDBY", 11, FontStyle.Bold, TextAnchor.UpperLeft, activeColor, "Type");
        SetAnchors(missionDetailType.rectTransform, new Vector2(0.06f, 0.69f), new Vector2(0.94f, 0.79f));

        missionDetailDescription = CreateText(missionDetailRoot, "GOAL // --", 13, FontStyle.Normal, TextAnchor.UpperLeft, paperColor, "Description");
        SetAnchors(missionDetailDescription.rectTransform, new Vector2(0.06f, 0.47f), new Vector2(0.94f, 0.68f));

        missionDetailProgress = CreateText(missionDetailRoot, "PROGRESS -- / --", 15, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor, "Progress");
        SetAnchors(missionDetailProgress.rectTransform, new Vector2(0.06f, 0.35f), new Vector2(0.94f, 0.47f));

        missionDetailTime = CreateText(missionDetailRoot, "TIME // NO LIMIT", 12, FontStyle.Bold, TextAnchor.MiddleLeft, mutedColor, "Time");
        SetAnchors(missionDetailTime.rectTransform, new Vector2(0.06f, 0.26f), new Vector2(0.94f, 0.36f));

        missionDetailReward = CreateText(missionDetailRoot, "SUCCESS --", 12, FontStyle.Bold, TextAnchor.MiddleLeft, activeColor, "Reward");
        SetAnchors(missionDetailReward.rectTransform, new Vector2(0.06f, 0.13f), new Vector2(0.94f, 0.25f));

        missionDetailFailure = CreateText(missionDetailRoot, "FAIL --", 12, FontStyle.Bold, TextAnchor.MiddleLeft, dangerColor, "Failure");
        SetAnchors(missionDetailFailure.rectTransform, new Vector2(0.06f, 0.02f), new Vector2(0.94f, 0.13f));
    }

    private void BuildChatPanel()
    {
        chatPanel = CreateRect(dashboardRoot, "LiveChatPanel", chatSize);
        chatPanel.anchorMin = chatPanel.anchorMax = Vector2.one;
        chatPanel.pivot = Vector2.one;
        chatPanel.anchoredPosition = new Vector2(-32f, -280f);

        Image back = chatPanel.gameObject.AddComponent<Image>();
        back.color = inkColor;
        back.raycastTarget = false;

        Outline outline = chatPanel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(mutedColor.r, mutedColor.g, mutedColor.b, 0.30f);
        outline.effectDistance = new Vector2(2f, -2f);

        chatGroup = chatPanel.gameObject.AddComponent<CanvasGroup>();
        chatGroup.blocksRaycasts = false;
        chatGroup.interactable = false;

        chatHeader = CreateText(
            chatPanel,
            $"LIVE CHAT // 0 VIEWERS // LAST {MaxChatLines}",
            12,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            mutedColor,
            "Header");
        SetAnchors(chatHeader.rectTransform, new Vector2(0.045f, 0.77f), new Vector2(0.955f, 0.96f));

        chatBody = CreateText(
            chatPanel,
            $"CHAT PAUSED // 0 VIEWERS\nFEED RANGE: LAST {MaxChatLines} MESSAGES",
            13,
            FontStyle.Normal,
            TextAnchor.UpperLeft,
            paperColor,
            "Body");
        SetAnchors(chatBody.rectTransform, new Vector2(0.045f, 0.07f), new Vector2(0.955f, 0.72f));
        chatBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        chatBody.verticalOverflow = VerticalWrapMode.Truncate;
        chatBody.supportRichText = true;
        chatBody.lineSpacing = 1.08f;
    }

    private void TrackPointerFocus()
    {
        if (!Input.mousePresent)
        {
            SetFocus(DashboardFocus.Pack);
            return;
        }

        int missionCount = activeMissionCount;
        if (missionCount <= 0)
        {
            SetFocus(DashboardFocus.Pack);
            return;
        }

        Vector2 mouse = Input.mousePosition;
        int hovered = FindMissionRowUnderPointer(mouse);

        if (hovered >= 0)
        {
            SetMissionSelection(hovered);
            SetFocus(DashboardFocus.Mission);
        }
        else
        {
            // MissionPanel 전체 사각형을 Hover 영역으로 사용하지 않습니다.
            // 실제 보이는 MissionRow 위에 있을 때만 Mission Focus가 활성화됩니다.
            SetFocus(DashboardFocus.Pack);
        }
    }

    private int FindMissionRowUnderPointer(Vector2 mouse)
    {
        int count = fanMissionSystem != null
            ? Mathf.Min(MaxMissionSlots, fanMissionSystem.ActiveMissions.Count)
            : 0;

        int result = -1;
        for (int i = 0; i < count; i++)
        {
            RectTransform row = missionRows[i];
            if (row == null)
                continue;

            if (RectTransformUtility.RectangleContainsScreenPoint(row, mouse, null))
            {
                result = i;
                break;
            }
        }

        if (hoveredMissionIndex != result)
        {
            hoveredMissionIndex = result;
            RefreshMissionRowState();
        }

        return result;
    }

    private void SetFocus(DashboardFocus next)
    {
        if (next == DashboardFocus.Mission &&
            (fanMissionSystem == null || fanMissionSystem.ActiveMissions.Count == 0))
        {
            next = DashboardFocus.Pack;
        }

        if (focus == next)
            return;

        focus = next;
        RefreshMissionRowState();
    }

    private void SetMissionSelection(int index)
    {
        int count = fanMissionSystem != null ? fanMissionSystem.ActiveMissions.Count : 0;
        index = count > 0 ? Mathf.Clamp(index, 0, count - 1) : -1;
        if (selectedMissionIndex == index)
            return;

        selectedMissionIndex = index;
        RefreshMissionDetail();
        RefreshMissionRowState();
    }

    private void RefreshMissionData()
    {
        if (missionPanel == null)
            return;

        int count = fanMissionSystem != null
            ? Mathf.Min(MaxMissionSlots, fanMissionSystem.ActiveMissions.Count)
            : 0;
        activeMissionCount = count;

        int unlocked = fanMissionSystem != null
            ? Mathf.Clamp(fanMissionSystem.UnlockedSlots, 0, MaxMissionSlots)
            : 0;

        SetText(missionHeader, count > 0 ? "FAN MISSION" : "FAN MISSION");
        SetText(missionRange, $"ACTIVE {count} / {unlocked}");

        if (missionRange != null)
            missionRange.enabled = count > 0;
        if (missionEmptyState != null)
            missionEmptyState.enabled = count == 0;

        for (int i = 0; i < MaxMissionSlots; i++)
        {
            bool active = i < count;
            missionRowActive[i] = active;

            if (!active)
            {
                SetText(missionRowTitles[i], string.Empty);
                SetText(missionRowProgress[i], string.Empty);
                continue;
            }

            FanMissionRuntime runtime = fanMissionSystem.ActiveMissions[i];
            FanMissionSO definition = runtime != null ? runtime.Definition : null;
            if (definition == null)
                continue;

            string title = string.IsNullOrWhiteSpace(definition.missionName)
                ? $"MISSION {i + 1}"
                : definition.missionName.ToUpperInvariant();

            SetText(missionRowTitles[i], title);
            SetText(
                missionRowProgress[i],
                $"{runtime.Progress} / {Mathf.Max(1, definition.targetCount)}");
        }

        if (count <= 0)
        {
            selectedMissionIndex = -1;
            hoveredMissionIndex = -1;
            focus = DashboardFocus.Pack;
        }
        else if (selectedMissionIndex < 0 || selectedMissionIndex >= count)
        {
            selectedMissionIndex = 0;
        }

        RefreshMissionDetail();
        RefreshMissionRowState();
    }

    private void RefreshMissionDetail()
    {
        if (missionDetailTitle == null)
            return;

        int count = fanMissionSystem != null ? fanMissionSystem.ActiveMissions.Count : 0;
        if (selectedMissionIndex < 0 || selectedMissionIndex >= count)
        {
            SetText(missionDetailTitle, "NO ACTIVE MISSION");
            SetText(missionDetailType, "TYPE // STANDBY");
            SetText(missionDetailDescription, "GOAL // --");
            SetText(missionDetailProgress, "PROGRESS -- / --");
            SetText(missionDetailTime, "TIME // NO LIMIT");
            SetText(missionDetailReward, "SUCCESS --");
            SetText(missionDetailFailure, "FAIL --");
            return;
        }

        FanMissionRuntime runtime = fanMissionSystem.ActiveMissions[selectedMissionIndex];
        FanMissionSO definition = runtime != null ? runtime.Definition : null;
        if (definition == null)
            return;

        string title = string.IsNullOrWhiteSpace(definition.missionName)
            ? $"MISSION {selectedMissionIndex + 1}"
            : definition.missionName.ToUpperInvariant();
        string description = string.IsNullOrWhiteSpace(definition.description)
            ? "NO DESCRIPTION"
            : definition.description;
        string timer = definition.duration > 0f
            ? $"{runtime.RemainingTime:0.0}s"
            : "NO LIMIT";

        SetText(missionDetailTitle, title);
        SetText(missionDetailType, $"TYPE // {definition.type.ToString().ToUpperInvariant()}");
        SetText(missionDetailDescription, $"GOAL // {description}");
        SetText(
            missionDetailProgress,
            $"PROGRESS {runtime.Progress} / {Mathf.Max(1, definition.targetCount)}");
        SetText(missionDetailTime, $"TIME // {timer}");
        SetText(
            missionDetailReward,
            $"SUCCESS REWARD // POP {Signed(definition.successPopularity)} // FP {Signed(definition.successFanPoints)}");
        SetText(
            missionDetailFailure,
            $"FAIL PENALTY // POP {Signed(definition.failPopularity)} // FP {Signed(definition.failFanPoints)}");
    }

    private void RefreshMissionRowState()
    {
        for (int i = 0; i < MaxMissionSlots; i++)
        {
            bool active = missionRowActive[i];
            bool hovered = active && i == hoveredMissionIndex;
            bool selected = active && i == selectedMissionIndex;

            if (missionRowBackgrounds[i] != null)
            {
                missionRowBackgrounds[i].color = hovered
                    ? new Color(activeColor.r, activeColor.g, activeColor.b, 0.20f)
                    : selected
                        ? new Color(activeColor.r, activeColor.g, activeColor.b, 0.10f)
                        : new Color(0.055f, 0.060f, 0.074f, 0.98f);
            }

            if (missionRowOutlines[i] != null)
            {
                missionRowOutlines[i].effectColor = hovered
                    ? activeColor
                    : selected
                        ? new Color(activeColor.r, activeColor.g, activeColor.b, 0.58f)
                        : new Color(mutedColor.r, mutedColor.g, mutedColor.b, 0.22f);
                missionRowOutlines[i].effectDistance = new Vector2(
                    hovered || selected ? 2f : 1f,
                    hovered || selected ? -2f : -1f);
            }

            if (missionRowTitles[i] != null)
                missionRowTitles[i].color = active ? paperColor : mutedColor;
            if (missionRowProgress[i] != null)
                missionRowProgress[i].color = hovered ? activeColor : progressColor;
        }
    }

    private void ApplyBroadcastMetrics(int viewers, int likes)
    {
        int safeViewers = Mathf.Max(0, viewers);
        int safeLikes = Mathf.Max(0, likes);

        SetText(viewersText, $"VIEWERS {safeViewers:N0}");
        SetText(likesText, $"LIKES {safeLikes:N0}");
    }

    private void RefreshChatState()
    {
        int viewers = currentViewers;

        SetText(
            chatHeader,
            $"LIVE CHAT // {viewers:N0} VIEWERS // LAST {MaxChatLines} MESSAGES");

        if (viewers <= 0)
        {
            SetText(
                chatBody,
                $"CHAT PAUSED // 0 VIEWERS\nLAST {MaxChatLines} MESSAGES");
            return;
        }

        RefreshChatBody();
    }

    /// <summary>
    /// 실제 방송/채팅 시스템이 수신한 메시지만 UI에 전달하는 입력점입니다.
    /// 이 Controller는 메시지를 생성하거나 Viewer 수를 근거로 가짜 채팅을 만들지 않습니다.
    /// </summary>
    public void PushChatMessage(string sender, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        string safeSender = NormalizeChatText(sender);
        string safeMessage = NormalizeChatText(message);
        string line = string.IsNullOrEmpty(safeSender)
            ? safeMessage
            : $"{safeSender}  {safeMessage}";

        chatHistory.Add(line);
        while (chatHistory.Count > MaxChatLines)
            chatHistory.RemoveAt(0);

        RefreshChatState();
    }

    public void ClearChatMessages()
    {
        chatHistory.Clear();
        RefreshChatState();
    }

    private void RefreshChatBody()
    {
        if (chatBody == null)
            return;

        if (chatHistory.Count == 0)
        {
            SetText(
                chatBody,
                $"NO CHAT MESSAGES RECEIVED\nLAST {MaxChatLines} MESSAGES");
            return;
        }

        SetText(
            chatBody,
            string.Join("\n", chatHistory) +
            $"\nLAST {MaxChatLines} MESSAGES");
    }

    private static string NormalizeChatText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }

    private void AnimatePresentation()
    {
        float t = 1f - Mathf.Exp(-Mathf.Max(4f, layoutSharpness) * Time.unscaledDeltaTime);
        bool targetVisible = state == DashboardState.Entering || state == DashboardState.Open;
        int missionCount = activeMissionCount;
        bool missionFocused = targetVisible &&
                              missionCount > 0 &&
                              focus == DashboardFocus.Mission;

        float dashboardTargetAlpha = targetVisible ? 1f : 0f;
        Vector3 dashboardTargetScale = Vector3.one * (targetVisible ? 1f : 0.94f);
        Quaternion dashboardTargetRotation = Quaternion.Euler(
            targetVisible ? 0f : 2.5f,
            targetVisible ? 0f : -5f,
            targetVisible ? 0f : -0.7f);
        float dashboardTargetZ = targetVisible ? 0f : 34f;

        if (dashboardGroup != null)
            dashboardGroup.alpha = Mathf.Lerp(dashboardGroup.alpha, dashboardTargetAlpha, t);

        dashboardRoot.localScale = Vector3.Lerp(
            dashboardRoot.localScale,
            dashboardTargetScale,
            t);
        dashboardRoot.localRotation = Quaternion.Slerp(
            dashboardRoot.localRotation,
            dashboardTargetRotation,
            t);
        Vector3 dashboardLocal = dashboardRoot.localPosition;
        dashboardLocal.z = Mathf.Lerp(dashboardLocal.z, dashboardTargetZ, t);
        dashboardRoot.localPosition = dashboardLocal;

        AnimateMetricBar(t, targetVisible);
        AnimateMissionPanel(t, missionCount, missionFocused);
        AnimateMissionRows(t);
        AnimateChat(t, targetVisible);

        if (state == DashboardState.Entering &&
            dashboardGroup != null &&
            dashboardGroup.alpha >= 0.985f)
        {
            dashboardGroup.alpha = 1f;
            state = DashboardState.Open;
        }
        else if (state == DashboardState.Exiting &&
                 dashboardGroup != null &&
                 dashboardGroup.alpha <= 0.015f)
        {
            dashboardGroup.alpha = 0f;
            state = DashboardState.Hidden;
            dashboardRoot.gameObject.SetActive(false);
        }
    }

    private void AnimateMetricBar(float t, bool dashboardVisible)
    {
        if (metricBar == null)
            return;

        metricBar.localScale = Vector3.Lerp(
            metricBar.localScale,
            Vector3.one * (dashboardVisible ? 1f : 0.94f),
            t);
        metricBar.localRotation = Quaternion.Slerp(
            metricBar.localRotation,
            Quaternion.Euler(
                dashboardVisible ? 0.4f : 2.6f,
                dashboardVisible ? -1.4f : -5.0f,
                dashboardVisible ? -0.25f : 0.6f),
            t);

        Vector3 local = metricBar.localPosition;
        local.z = Mathf.Lerp(local.z, dashboardVisible ? -6f : 20f, t);
        metricBar.localPosition = local;
    }

    private void AnimateMissionPanel(float t, int missionCount, bool missionFocused)
    {
        if (missionPanel == null)
            return;

        bool standby = missionCount <= 0;
        Vector2 targetSize = standby
            ? standbyMissionSize
            : missionFocused ? focusedMissionSize : compactMissionSize;
        Vector2 targetPosition = missionFocused
            ? focusedMissionPanelOffset
            : missionPanelOffset;

        missionPanel.sizeDelta = Vector2.Lerp(
            missionPanel.sizeDelta,
            targetSize,
            t);
        missionPanel.anchoredPosition = Vector2.Lerp(
            missionPanel.anchoredPosition,
            targetPosition,
            t);

        Quaternion targetRotation = Quaternion.Euler(
            standby ? 2.4f : missionFocused ? 0.2f : 1.0f,
            standby ? -6f : missionFocused ? -1f : -3.2f,
            standby ? 0.6f : missionFocused ? -0.15f : 0.2f);
        missionPanel.localRotation = Quaternion.Slerp(
            missionPanel.localRotation,
            targetRotation,
            t);

        Vector3 local = missionPanel.localPosition;
        local.z = Mathf.Lerp(
            local.z,
            standby ? 20f : missionFocused ? -14f : 4f,
            t);
        missionPanel.localPosition = local;

        if (missionPanelGroup != null)
            missionPanelGroup.alpha = Mathf.Lerp(
                missionPanelGroup.alpha,
                standby ? 0.72f : missionFocused ? 1f : 0.94f,
                t);

        bool detailVisible = !standby &&
                             missionFocused &&
                             selectedMissionIndex >= 0;

        if (missionListRoot != null && missionDetailRoot != null)
        {
            missionListRoot.anchorMin = new Vector2(0.055f, 0.08f);
            missionListRoot.anchorMax = detailVisible
                ? new Vector2(0.42f, 0.68f)
                : new Vector2(0.945f, 0.68f);
            missionListRoot.offsetMin = Vector2.zero;
            missionListRoot.offsetMax = Vector2.zero;

            missionDetailRoot.anchorMin = new Vector2(0.45f, 0.08f);
            missionDetailRoot.anchorMax = new Vector2(0.95f, 0.68f);
            missionDetailRoot.offsetMin = Vector2.zero;
            missionDetailRoot.offsetMax = Vector2.zero;
        }

        if (missionDetailGroup != null)
            missionDetailGroup.alpha = Mathf.Lerp(
                missionDetailGroup.alpha,
                detailVisible ? 1f : 0f,
                t);

        if (missionDetailRoot != null)
        {
            missionDetailRoot.localScale = Vector3.Lerp(
                missionDetailRoot.localScale,
                Vector3.one * (detailVisible ? 1f : 0.92f),
                t);
            missionDetailRoot.localRotation = Quaternion.Slerp(
                missionDetailRoot.localRotation,
                Quaternion.Euler(
                    detailVisible ? 0f : 2.2f,
                    detailVisible ? 0f : -4f,
                    detailVisible ? 0f : 0.5f),
                t);
            Vector3 detailLocal = missionDetailRoot.localPosition;
            detailLocal.z = Mathf.Lerp(
                detailLocal.z,
                detailVisible ? -12f : 18f,
                t);
            missionDetailRoot.localPosition = detailLocal;
        }
    }

    private void AnimateMissionRows(float t)
    {
        if (missionListRoot == null)
            return;

        float availableWidth = Mathf.Max(180f, missionListRoot.rect.width);

        for (int i = 0; i < MaxMissionSlots; i++)
        {
            RectTransform row = missionRows[i];
            CanvasGroup group = missionRowGroups[i];
            if (row == null || group == null)
                continue;

            bool active = missionRowActive[i];
            bool hovered = active && i == hoveredMissionIndex;
            bool selected = active && i == selectedMissionIndex;

            group.alpha = Mathf.Lerp(group.alpha, active ? 1f : 0f, t);

            row.sizeDelta = Vector2.Lerp(
                row.sizeDelta,
                new Vector2(availableWidth, 44f),
                t);
            row.anchoredPosition = Vector2.Lerp(
                row.anchoredPosition,
                new Vector2(0f, -i * 50f),
                t);

            Vector3 targetScale = Vector3.one *
                                  (hovered ? 1.035f : active ? 1f : 0.94f);
            row.localScale = Vector3.Lerp(row.localScale, targetScale, t);

            Quaternion targetRotation = Quaternion.Euler(
                hovered ? 0f : selected ? 0.5f : 1.4f,
                hovered ? 0f : selected ? -1.5f : -3.5f,
                hovered ? 0f : selected ? -0.15f : -0.45f);
            row.localRotation = Quaternion.Slerp(
                row.localRotation,
                targetRotation,
                t);

            Vector3 local = row.localPosition;
            local.z = Mathf.Lerp(
                local.z,
                hovered ? -14f : selected ? -7f : active ? 2f : 18f,
                t);
            row.localPosition = local;
        }
    }

    private void AnimateChat(float t, bool dashboardVisible)
    {
        if (chatPanel == null || chatGroup == null)
            return;

        int viewers = currentViewers;
        bool active = dashboardVisible && viewers > 0;

        float missionHeight = missionPanel != null
            ? missionPanel.sizeDelta.y
            : standbyMissionSize.y;
        Vector2 targetPosition = new(
            -32f,
            -(missionHeight + 132f));

        chatPanel.anchoredPosition = Vector2.Lerp(
            chatPanel.anchoredPosition,
            targetPosition,
            t);

        chatGroup.alpha = Mathf.Lerp(
            chatGroup.alpha,
            dashboardVisible ? active ? 1f : 0.72f : 0f,
            t);

        chatPanel.localScale = Vector3.Lerp(
            chatPanel.localScale,
            Vector3.one * (active ? 1f : 0.94f),
            t);
        chatPanel.localRotation = Quaternion.Slerp(
            chatPanel.localRotation,
            Quaternion.Euler(
                active ? 0.4f : 2.5f,
                active ? -1.5f : -5.5f,
                active ? -0.25f : 0.65f),
            t);

        Vector3 local = chatPanel.localPosition;
        local.z = Mathf.Lerp(local.z, active ? -8f : 18f, t);
        chatPanel.localPosition = local;
    }

    private static string Signed(int value)
    {
        return value > 0 ? $"+{value}" : value.ToString();
    }

    private static void SetText(Text text, string value)
    {
        if (text != null && text.text != value)
            text.text = value;
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
        string objectName)
    {
        RectTransform rect = CreateRect(parent, objectName, Vector2.zero);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Combat TAB 화면을 PACK + FAN MISSION + Broadcast Metrics가 결합된 가변 Dashboard로 구성합니다.
///
/// 소유권 규칙:
/// - BattleUnifiedInventoryInspectController는 기존 GridBoard의 Anchor / AnchoredPosition을 계속 소유합니다.
/// - 이 Controller는 GridBoard 바깥에 BroadcastPackDock 부모를 한 단계 두고 그 부모 Offset만 움직입니다.
/// - Mission은 FanMissionSystem의 Runtime 데이터를 읽기만 하며 클릭으로 선택하지 않습니다.
/// - 실제 Input.mousePosition이 가리키는 영역으로 Pack / Mission Focus가 결정됩니다.
/// - 활성 미션이 0개여도 FAN MISSION 프레임은 STANDBY 상태로 항상 보입니다.
/// - 댓글 문자열은 표시하지 않고 Viewers / Likes만 별도 LIVE METRICS Bar에 표시합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33440)]
public sealed class BattleBroadcastDashboardController : MonoBehaviour
{
    private enum DashboardFocus
    {
        Pack,
        Mission
    }

    private const int MaxMissionSlots = 6;
    private const int DashboardSortingOrder = 1685;
    private const int CurrentLayoutVersion = 1;

    [Header("REFERENCES — 자동 연결")]
    [Tooltip("현재 Combat 상태를 확인하는 Run Manager입니다. 비어 있으면 자동으로 찾습니다.")]
    [SerializeField] private BattleRunManager runManager;
    [Tooltip("현재 활성 Fan Mission과 진행도를 제공하는 기존 Mission System입니다.")]
    [SerializeField] private FanMissionSystem fanMissionSystem;
    [Tooltip("실시간 시청자 수와 좋아요 수를 제공하는 Run Progress System입니다.")]
    [SerializeField] private RunProgressSystem runProgress;
    [Tooltip("TAB Hold 상태와 기존 PACK GridBoard를 제공하는 Loadout UI입니다.")]
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;

    [Header("FOCUS TRACKING — 커서 위치 기반")]
    [Tooltip("커서가 화면의 이 X 비율보다 오른쪽으로 넘어가면 Mission Focus로 전환합니다. Mission Panel 위에서는 즉시 Mission Focus가 됩니다.")]
    [SerializeField, Range(0.48f, 0.85f)] private float missionFocusEnterX = 0.61f;
    [Tooltip("Mission Focus에서 이 X 비율보다 왼쪽으로 돌아오면 PACK Focus로 복귀합니다. Enter보다 작게 두어 경계 떨림을 막습니다.")]
    [SerializeField, Range(0.25f, 0.70f)] private float packFocusReturnX = 0.50f;
    [Tooltip("Layout Morph 반응 속도입니다. 높을수록 빠르게 목표 형태에 붙습니다.")]
    [SerializeField, Range(4f, 30f)] private float layoutSharpness = 13f;

    [Header("PACK DOCK — 좌측 사선 바 결합")]
    [Tooltip("PACK Focus 상태에서 기존 GridBoard 전체를 좌측 사선 바 쪽으로 이동시키는 화면 픽셀 Offset입니다. 기존 GridBoard Anchor는 건드리지 않습니다.")]
    [SerializeField] private Vector2 packFocusedDockOffset = new(-260f, 4f);
    [Tooltip("Mission Focus 상태에서 PACK이 더 왼쪽으로 물러나는 화면 픽셀 Offset입니다.")]
    [SerializeField] private Vector2 missionFocusedDockOffset = new(-360f, 4f);
    [Tooltip("PACK Focus 상태에서 GridBoard의 시각 Scale입니다.")]
    [SerializeField, Range(0.70f, 1.15f)] private float packFocusedScale = 1f;
    [Tooltip("Mission Focus 상태에서 PACK이 물러날 때의 Scale입니다.")]
    [SerializeField, Range(0.55f, 1f)] private float missionFocusedPackScale = 0.78f;
    [Tooltip("PACK Focus 상태의 불투명도입니다.")]
    [SerializeField, Range(0f, 1f)] private float packFocusedAlpha = 1f;
    [Tooltip("Mission Focus 상태에서 PACK이 배경으로 물러날 때의 불투명도입니다.")]
    [SerializeField, Range(0f, 1f)] private float missionFocusedPackAlpha = 0.70f;
    [Tooltip("PACK Focus 상태의 기울기입니다.")]
    [SerializeField, Range(-15f, 15f)] private float packFocusedRotation = -4f;
    [Tooltip("Mission Focus 상태에서 PACK 기울기를 완화하는 각도입니다.")]
    [SerializeField, Range(-15f, 15f)] private float missionFocusedPackRotation = -1.5f;

    [Header("MISSION BAR — 항상 표시")]
    [Tooltip("PACK Focus일 때 우측에 유지되는 FAN MISSION 패널 크기입니다. 미션이 없어도 이 프레임은 사라지지 않습니다.")]
    [SerializeField] private Vector2 compactMissionSize = new(540f, 380f);
    [Tooltip("Mission Focus일 때 상세 정보까지 펼쳐지는 FAN MISSION 패널 크기입니다.")]
    [SerializeField] private Vector2 focusedMissionSize = new(760f, 700f);
    [Tooltip("PACK Focus 상태에서 화면 우측 상단 기준 Mission Panel 위치입니다.")]
    [SerializeField] private Vector2 missionPanelOffset = new(-54f, -126f);
    [Tooltip("Mission Focus 상태에서 확장된 Panel이 이동할 화면 우측 상단 기준 위치입니다. 크기 변화와 함께 이 위치까지 Tween됩니다.")]
    [SerializeField] private Vector2 focusedMissionPanelOffset = new(-78f, -154f);
    [Tooltip("Mission Panel 전체의 사선 회전 각도입니다.")]
    [SerializeField, Range(-10f, 10f)] private float missionPanelRotation = 2.2f;
    [Tooltip("PACK Focus 상태에서도 Mission Bar가 확실히 보이도록 유지할 Alpha입니다.")]
    [SerializeField, Range(0.5f, 1f)] private float compactMissionAlpha = 0.95f;

    [Header("LIVE METRICS — 우측 상단")]
    [Tooltip("시청자/좋아요 Bar의 크기입니다.")]
    [SerializeField] private Vector2 metricBarSize = new(430f, 68f);
    [Tooltip("화면 우측 상단 기준 Metric Bar 위치입니다.")]
    [SerializeField] private Vector2 metricBarOffset = new(-42f, -38f);
    [Tooltip("Metric Bar의 기울기입니다.")]
    [SerializeField, Range(-8f, 8f)] private float metricBarRotation = -1.6f;

    [Header("THEME — 기하학 UI 색상")]
    [Tooltip("주 패널과 Mission Row에 사용하는 거의 검은 잉크 색입니다.")]
    [SerializeField] private Color inkColor = new(0.025f, 0.028f, 0.045f, 0.985f);
    [Tooltip("텍스트/외곽선에 사용하는 밝은 종이 색입니다.")]
    [SerializeField] private Color paperColor = new(0.94f, 0.95f, 0.97f, 1f);
    [Tooltip("Mission Focus와 방송 지표에 사용하는 청록 Accent입니다. 굵은 장식선에는 사용하지 않고 Hover/아이콘/텍스트 포인트에만 사용합니다.")]
    [SerializeField] private Color accentCyan = new(0.10f, 0.88f, 0.95f, 1f);
    [Tooltip("진행률/성공 보상에 사용하는 노란 Accent입니다.")]
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [Tooltip("실패/위험 정보에 사용하는 핑크 Accent입니다.")]
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);

    [SerializeField, HideInInspector] private int serializedLayoutVersion;

    private RectTransform fullRoot;
    private RectTransform packBoard;
    private RectTransform packDockRoot;
    private CanvasGroup packBoardGroup;

    private RectTransform dashboardRoot;
    private Canvas dashboardCanvas;

    private RectTransform missionPanel;
    private CanvasGroup missionPanelGroup;
    private RectTransform missionListRoot;
    private RectTransform missionDetailRoot;
    private CanvasGroup missionDetailGroup;
    private Text missionHeader;
    private Text missionEmptyState;
    private Text missionDetailTitle;
    private Text missionDetailType;
    private Text missionDetailDescription;
    private Text missionDetailProgress;
    private Text missionDetailReward;
    private Text missionDetailFailure;

    private RectTransform metricBar;
    private Text viewersText;
    private Text likesText;

    private readonly RectTransform[] missionRows = new RectTransform[MaxMissionSlots];
    private readonly Image[] missionRowBackgrounds = new Image[MaxMissionSlots];
    private readonly Outline[] missionRowOutlines = new Outline[MaxMissionSlots];
    private readonly Text[] missionRowTitles = new Text[MaxMissionSlots];
    private readonly Text[] missionRowProgress = new Text[MaxMissionSlots];

    private DashboardFocus focus = DashboardFocus.Pack;
    private int selectedMissionIndex = -1;
    private int hoveredMissionIndex = -1;
    private bool dashboardWasOpen;
    private bool layoutDirty;
    private float nextReferenceResolveAt;
    private float nextUiResolveAt;
    private float nextTimerRefreshAt;

    private FanMissionSystem subscribedMissionSystem;
    private RunProgressSystem subscribedRunProgress;

    private void Awake()
    {
        UpgradeSerializedLayoutIfNeeded();
        ResolveReferences();
        TryResolveUi();
    }

    private void OnEnable()
    {
        UpgradeSerializedLayoutIfNeeded();
        ResolveReferences();
        Subscribe();
        nextReferenceResolveAt = 0f;
        nextUiResolveAt = 0f;
        nextTimerRefreshAt = 0f;
        dashboardWasOpen = false;
        layoutDirty = true;
        focus = DashboardFocus.Pack;
    }

    private void OnValidate()
    {
        UpgradeSerializedLayoutIfNeeded();
    }

    private void UpgradeSerializedLayoutIfNeeded()
    {
        if (serializedLayoutVersion >= CurrentLayoutVersion)
            return;

        // 이전 자동 기본값만 새 배치로 이동합니다. 사용자가 Inspector에서 이미 커스텀한 값은 보존합니다.
        if (Approximately(packFocusedDockOffset, new Vector2(-150f, 4f)))
            packFocusedDockOffset = new Vector2(-260f, 4f);
        if (Approximately(missionFocusedDockOffset, new Vector2(-245f, 4f)))
            missionFocusedDockOffset = new Vector2(-360f, 4f);

        serializedLayoutVersion = CurrentLayoutVersion;
    }

    private void OnDisable()
    {
        Unsubscribe();
        SetDashboardVisible(false);
        ResetPackVisual();
    }

    private void Update()
    {
        ResolveReferencesWhenNeeded();
        Subscribe();
        TryResolveUiWhenNeeded();

        bool open = IsDashboardOpen();
        if (!open)
        {
            if (dashboardWasOpen)
            {
                dashboardWasOpen = false;
                focus = DashboardFocus.Pack;
                hoveredMissionIndex = -1;
                layoutDirty = true;
                SetDashboardVisible(false);
                ResetPackVisual();
            }
            return;
        }

        if (!dashboardWasOpen)
        {
            dashboardWasOpen = true;
            focus = DashboardFocus.Pack;
            hoveredMissionIndex = -1;
            layoutDirty = true;
            SetDashboardVisible(true);
            RefreshMissionData();
            RefreshBroadcastMetrics();
        }

        TrackCursorFocus();

        if (layoutDirty)
            AnimateLayout();

        if (focus == DashboardFocus.Mission && Time.unscaledTime >= nextTimerRefreshAt)
        {
            nextTimerRefreshAt = Time.unscaledTime + 0.10f;
            RefreshMissionDetail();
        }
    }

    private bool IsDashboardOpen()
    {
        return runManager != null && runManager.RunActive &&
               runManager.State == BattleRunState.Combat &&
               kineticLoadout != null && kineticLoadout.IsSwitchBoardOpen &&
               fullRoot != null && packBoard != null && dashboardRoot != null;
    }

    private void SetDashboardVisible(bool visible)
    {
        if (dashboardRoot != null && dashboardRoot.gameObject.activeSelf != visible)
            dashboardRoot.gameObject.SetActive(visible);
    }

    private void TrackCursorFocus()
    {
        if (!Input.mousePresent)
            return;

        Vector2 mouse = Input.mousePosition;
        int missionHover = FindMissionRowUnderPointer(mouse);
        bool insideMissionPanel = missionPanel != null &&
                                  RectTransformUtility.RectangleContainsScreenPoint(missionPanel, mouse, null);
        bool insidePack = packBoard != null &&
                          RectTransformUtility.RectangleContainsScreenPoint(packBoard, mouse, null);

        if (missionHover >= 0)
        {
            SetMissionSelection(missionHover);
            SetFocus(DashboardFocus.Mission);
            return;
        }

        if (insideMissionPanel)
        {
            SetFocus(DashboardFocus.Mission);
            return;
        }

        if (insidePack)
        {
            SetFocus(DashboardFocus.Pack);
            return;
        }

        float normalizedX = Screen.width > 0 ? mouse.x / Screen.width : 0f;
        if (focus == DashboardFocus.Pack && normalizedX >= missionFocusEnterX)
            SetFocus(DashboardFocus.Mission);
        else if (focus == DashboardFocus.Mission && normalizedX <= packFocusReturnX)
            SetFocus(DashboardFocus.Pack);

        if (hoveredMissionIndex != -1)
        {
            hoveredMissionIndex = -1;
            RefreshMissionRowVisuals();
        }
    }

    private int FindMissionRowUnderPointer(Vector2 mouse)
    {
        int count = fanMissionSystem != null ? fanMissionSystem.ActiveMissions.Count : 0;
        int result = -1;

        for (int i = 0; i < Mathf.Min(count, MaxMissionSlots); i++)
        {
            RectTransform row = missionRows[i];
            if (row == null || !row.gameObject.activeInHierarchy)
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
            RefreshMissionRowVisuals();
        }

        return result;
    }

    private void SetFocus(DashboardFocus next)
    {
        if (focus == next)
            return;

        focus = next;
        layoutDirty = true;
        nextTimerRefreshAt = 0f;
        RefreshMissionRowVisuals();
    }

    private void SetMissionSelection(int index)
    {
        int count = fanMissionSystem != null ? fanMissionSystem.ActiveMissions.Count : 0;
        index = count > 0 ? Mathf.Clamp(index, 0, count - 1) : -1;
        if (selectedMissionIndex == index)
            return;

        selectedMissionIndex = index;
        layoutDirty = true;
        nextTimerRefreshAt = 0f;
        RefreshMissionDetail();
        RefreshMissionRowVisuals();
    }

    private void AnimateLayout()
    {
        float t = 1f - Mathf.Exp(-Mathf.Max(1f, layoutSharpness) * Time.unscaledDeltaTime);
        bool missionFocused = focus == DashboardFocus.Mission;
        bool settled = true;

        Vector3 dockTarget = new(
            missionFocused ? missionFocusedDockOffset.x : packFocusedDockOffset.x,
            missionFocused ? missionFocusedDockOffset.y : packFocusedDockOffset.y,
            0f);
        float targetPackScale = missionFocused ? missionFocusedPackScale : packFocusedScale;
        float targetPackAlpha = missionFocused ? missionFocusedPackAlpha : packFocusedAlpha;
        float targetPackRotation = missionFocused ? missionFocusedPackRotation : packFocusedRotation;
        Vector2 targetMissionSize = missionFocused ? focusedMissionSize : compactMissionSize;
        Vector2 targetMissionPosition = missionFocused ? focusedMissionPanelOffset : missionPanelOffset;
        float targetMissionAlpha = missionFocused ? 1f : compactMissionAlpha;
        float targetDetailAlpha = missionFocused && selectedMissionIndex >= 0 ? 1f : 0f;

        if (packDockRoot != null)
        {
            Vector3 next = Vector3.Lerp(packDockRoot.localPosition, dockTarget, t);
            if ((next - dockTarget).sqrMagnitude <= 0.01f)
                next = dockTarget;
            else
                settled = false;

            if ((packDockRoot.localPosition - next).sqrMagnitude > 0.0001f)
                packDockRoot.localPosition = next;
        }

        if (packBoard != null)
        {
            Vector3 scaleTarget = Vector3.one * targetPackScale;
            Vector3 nextScale = Vector3.Lerp(packBoard.localScale, scaleTarget, t);
            if ((nextScale - scaleTarget).sqrMagnitude <= 0.000001f)
                nextScale = scaleTarget;
            else
                settled = false;
            if ((packBoard.localScale - nextScale).sqrMagnitude > 0.0000001f)
                packBoard.localScale = nextScale;

            float currentRotation = NormalizeAngle(packBoard.localEulerAngles.z);
            float nextRotation = Mathf.LerpAngle(currentRotation, targetPackRotation, t);
            if (Mathf.Abs(Mathf.DeltaAngle(nextRotation, targetPackRotation)) <= 0.01f)
                nextRotation = targetPackRotation;
            else
                settled = false;
            if (Mathf.Abs(Mathf.DeltaAngle(currentRotation, nextRotation)) > 0.001f)
                packBoard.localRotation = Quaternion.Euler(0f, 0f, nextRotation);
        }

        if (packBoardGroup != null)
        {
            float nextAlpha = Mathf.Lerp(packBoardGroup.alpha, targetPackAlpha, t);
            if (Mathf.Abs(nextAlpha - targetPackAlpha) <= 0.001f)
                nextAlpha = targetPackAlpha;
            else
                settled = false;
            if (!Mathf.Approximately(packBoardGroup.alpha, nextAlpha))
                packBoardGroup.alpha = nextAlpha;
        }

        if (missionPanel != null)
        {
            Vector2 nextSize = Vector2.Lerp(missionPanel.sizeDelta, targetMissionSize, t);
            if ((nextSize - targetMissionSize).sqrMagnitude <= 0.01f)
                nextSize = targetMissionSize;
            else
                settled = false;
            if ((missionPanel.sizeDelta - nextSize).sqrMagnitude > 0.0001f)
                missionPanel.sizeDelta = nextSize;

            Vector2 nextPosition = Vector2.Lerp(missionPanel.anchoredPosition, targetMissionPosition, t);
            if ((nextPosition - targetMissionPosition).sqrMagnitude <= 0.01f)
                nextPosition = targetMissionPosition;
            else
                settled = false;
            if ((missionPanel.anchoredPosition - nextPosition).sqrMagnitude > 0.0001f)
                missionPanel.anchoredPosition = nextPosition;

            float currentRotation = NormalizeAngle(missionPanel.localEulerAngles.z);
            if (Mathf.Abs(Mathf.DeltaAngle(currentRotation, missionPanelRotation)) > 0.001f)
                missionPanel.localRotation = Quaternion.Euler(0f, 0f, missionPanelRotation);
        }

        if (missionPanelGroup != null)
        {
            float nextAlpha = Mathf.Lerp(missionPanelGroup.alpha, targetMissionAlpha, t);
            if (Mathf.Abs(nextAlpha - targetMissionAlpha) <= 0.001f)
                nextAlpha = targetMissionAlpha;
            else
                settled = false;
            if (!Mathf.Approximately(missionPanelGroup.alpha, nextAlpha))
                missionPanelGroup.alpha = nextAlpha;
        }

        LayoutMissionStructure(missionFocused);

        if (missionDetailGroup != null && missionDetailRoot != null)
        {
            float nextAlpha = Mathf.Lerp(missionDetailGroup.alpha, targetDetailAlpha, t);
            if (Mathf.Abs(nextAlpha - targetDetailAlpha) <= 0.001f)
                nextAlpha = targetDetailAlpha;
            else
                settled = false;
            if (!Mathf.Approximately(missionDetailGroup.alpha, nextAlpha))
                missionDetailGroup.alpha = nextAlpha;

            float targetDetailScale = targetDetailAlpha > 0.5f ? 1f : 0.92f;
            Vector3 detailTarget = Vector3.one * targetDetailScale;
            Vector3 nextScale = Vector3.Lerp(missionDetailRoot.localScale, detailTarget, t);
            if ((nextScale - detailTarget).sqrMagnitude <= 0.000001f)
                nextScale = detailTarget;
            else
                settled = false;
            if ((missionDetailRoot.localScale - nextScale).sqrMagnitude > 0.0000001f)
                missionDetailRoot.localScale = nextScale;
        }

        layoutDirty = !settled;
    }

    private void LayoutMissionStructure(bool missionFocused)
    {
        if (missionListRoot == null || missionDetailRoot == null)
            return;

        Vector2 listMin = new(0.055f, 0.08f);
        Vector2 listMax = missionFocused ? new Vector2(0.43f, 0.80f) : new Vector2(0.945f, 0.80f);
        if (missionListRoot.anchorMin != listMin || missionListRoot.anchorMax != listMax)
        {
            missionListRoot.anchorMin = listMin;
            missionListRoot.anchorMax = listMax;
            missionListRoot.offsetMin = Vector2.zero;
            missionListRoot.offsetMax = Vector2.zero;
        }

        missionDetailRoot.anchorMin = new Vector2(0.46f, 0.08f);
        missionDetailRoot.anchorMax = new Vector2(0.95f, 0.80f);
        missionDetailRoot.offsetMin = Vector2.zero;
        missionDetailRoot.offsetMax = Vector2.zero;

        LayoutMissionRows(missionFocused);
    }

    private void ResetPackVisual()
    {
        if (packDockRoot != null)
            packDockRoot.localPosition = Vector3.zero;

        if (packBoard != null)
        {
            packBoard.localScale = Vector3.one;
            packBoard.localRotation = Quaternion.Euler(0f, 0f, -4f);
        }

        if (packBoardGroup != null)
            packBoardGroup.alpha = 1f;
    }

    private void ResolveReferencesWhenNeeded()
    {
        if (runManager != null && fanMissionSystem != null && runProgress != null && kineticLoadout != null)
            return;
        if (Time.unscaledTime < nextReferenceResolveAt)
            return;

        nextReferenceResolveAt = Time.unscaledTime + 0.25f;
        ResolveReferences();
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

    private void TryResolveUiWhenNeeded()
    {
        if (fullRoot != null && packBoard != null && packDockRoot != null && dashboardRoot != null)
            return;
        if (Time.unscaledTime < nextUiResolveAt)
            return;

        nextUiResolveAt = Time.unscaledTime + 0.10f;
        TryResolveUi();
    }

    private void TryResolveUi()
    {
        if (kineticLoadout == null)
            return;

        fullRoot = kineticLoadout.FullRoot;
        packBoard = kineticLoadout.GridBoard;
        if (fullRoot == null || packBoard == null)
            return;

        EnsurePackDock();

        packBoardGroup = packBoard.GetComponent<CanvasGroup>();
        if (packBoardGroup == null)
        {
            packBoardGroup = packBoard.gameObject.AddComponent<CanvasGroup>();
            packBoardGroup.blocksRaycasts = true;
            packBoardGroup.interactable = true;
        }

        if (dashboardRoot == null)
            BuildDashboardUi();
    }

    private void EnsurePackDock()
    {
        if (fullRoot == null || packBoard == null)
            return;

        packDockRoot = fullRoot.Find("BroadcastPackDock") as RectTransform;
        if (packDockRoot == null)
        {
            packDockRoot = CreateRect(fullRoot, "BroadcastPackDock", Vector2.zero);
            Stretch(packDockRoot);
            packDockRoot.localPosition = Vector3.zero;
        }

        if (packBoard.parent != packDockRoot)
            packBoard.SetParent(packDockRoot, false);
    }

    private void BuildDashboardUi()
    {
        RectTransform existing = fullRoot.Find("BroadcastDashboard") as RectTransform;
        if (existing != null)
        {
            dashboardRoot = existing;
            ResolveBuiltDashboard(existing);
            SetDashboardVisible(false);
            return;
        }

        dashboardRoot = CreateRect(fullRoot, "BroadcastDashboard", Vector2.zero);
        Stretch(dashboardRoot);
        dashboardRoot.SetAsLastSibling();

        dashboardCanvas = dashboardRoot.gameObject.AddComponent<Canvas>();
        dashboardCanvas.overrideSorting = true;
        dashboardCanvas.sortingOrder = DashboardSortingOrder;

        BuildMetricBar();
        BuildMissionPanel();
        SetDashboardVisible(false);
    }

    private void ResolveBuiltDashboard(RectTransform root)
    {
        dashboardCanvas = root.GetComponent<Canvas>();
        if (dashboardCanvas == null)
            dashboardCanvas = root.gameObject.AddComponent<Canvas>();
        dashboardCanvas.overrideSorting = true;
        dashboardCanvas.sortingOrder = DashboardSortingOrder;

        metricBar = root.Find("BroadcastMetricBar") as RectTransform;
        missionPanel = root.Find("MissionPanel") as RectTransform;
        missionPanelGroup = missionPanel != null ? missionPanel.GetComponent<CanvasGroup>() : null;
        missionHeader = missionPanel != null ? missionPanel.Find("Header")?.GetComponent<Text>() : null;
        missionListRoot = missionPanel != null ? missionPanel.Find("MissionList") as RectTransform : null;
        missionEmptyState = missionPanel != null ? missionPanel.Find("EmptyState")?.GetComponent<Text>() : null;
        missionDetailRoot = missionPanel != null ? missionPanel.Find("MissionDetail") as RectTransform : null;
        missionDetailGroup = missionDetailRoot != null ? missionDetailRoot.GetComponent<CanvasGroup>() : null;

        // 이전 버전에서 만들어진 굵은 Cyan 장식선은 새 디자인에서 사용하지 않습니다.
        Transform oldMetricSlash = root.Find("BroadcastMetricBar/AccentSlash");
        if (oldMetricSlash != null && oldMetricSlash.gameObject.activeSelf)
            oldMetricSlash.gameObject.SetActive(false);
        Transform oldMissionSlash = root.Find("MissionPanel/MissionSlash");
        if (oldMissionSlash != null && oldMissionSlash.gameObject.activeSelf)
            oldMissionSlash.gameObject.SetActive(false);

        if (metricBar != null)
        {
            viewersText = metricBar.Find("Viewers/Count")?.GetComponent<Text>();
            likesText = metricBar.Find("Likes/Count")?.GetComponent<Text>();
        }

        if (missionListRoot != null)
        {
            for (int i = 0; i < MaxMissionSlots; i++)
            {
                RectTransform row = missionListRoot.Find($"MissionRow_{i}") as RectTransform;
                missionRows[i] = row;
                missionRowBackgrounds[i] = row != null ? row.GetComponent<Image>() : null;
                missionRowOutlines[i] = row != null ? row.GetComponent<Outline>() : null;
                missionRowTitles[i] = row != null ? row.Find("Title")?.GetComponent<Text>() : null;
                missionRowProgress[i] = row != null ? row.Find("Progress")?.GetComponent<Text>() : null;
            }
        }

        if (missionDetailRoot != null)
        {
            missionDetailTitle = missionDetailRoot.Find("Title")?.GetComponent<Text>();
            missionDetailType = missionDetailRoot.Find("Type")?.GetComponent<Text>();
            missionDetailDescription = missionDetailRoot.Find("Description")?.GetComponent<Text>();
            missionDetailProgress = missionDetailRoot.Find("Progress")?.GetComponent<Text>();
            missionDetailReward = missionDetailRoot.Find("Reward")?.GetComponent<Text>();
            missionDetailFailure = missionDetailRoot.Find("Failure")?.GetComponent<Text>();
        }
    }

    private void BuildMetricBar()
    {
        metricBar = CreateRect(dashboardRoot, "BroadcastMetricBar", metricBarSize);
        metricBar.anchorMin = metricBar.anchorMax = Vector2.one;
        metricBar.pivot = Vector2.one;
        metricBar.anchoredPosition = metricBarOffset;
        metricBar.localRotation = Quaternion.Euler(0f, 0f, metricBarRotation);

        Image back = metricBar.gameObject.AddComponent<Image>();
        back.color = inkColor;
        back.raycastTarget = false;
        Outline outline = metricBar.gameObject.AddComponent<Outline>();
        outline.effectColor = paperColor;
        outline.effectDistance = new Vector2(3f, -3f);

        Text live = CreateText(metricBar, "LIVE", 11, FontStyle.Bold, TextAnchor.MiddleLeft, accentPink, "LiveLabel");
        SetAnchors(live.rectTransform, new Vector2(0.055f, 0.12f), new Vector2(0.17f, 0.88f));

        BuildEyeMetric(metricBar);
        BuildLikeMetric(metricBar);
    }

    private void BuildEyeMetric(Transform parent)
    {
        RectTransform root = CreateRect(parent, "Viewers", new Vector2(150f, 48f));
        root.anchorMin = root.anchorMax = new Vector2(0.40f, 0.5f);
        root.anchoredPosition = Vector2.zero;

        RectTransform eye = CreateRect(root, "EyeIcon", new Vector2(28f, 18f));
        eye.anchorMin = eye.anchorMax = new Vector2(0f, 0.5f);
        eye.anchoredPosition = new Vector2(10f, 0f);
        eye.localRotation = Quaternion.Euler(0f, 0f, 45f);
        SetImage(eye, paperColor);

        RectTransform cut = CreateRect(eye, "Cut", new Vector2(17f, 10f));
        cut.anchorMin = cut.anchorMax = new Vector2(0.5f, 0.5f);
        cut.localRotation = Quaternion.identity;
        SetImage(cut, inkColor);

        RectTransform pupil = CreateRect(root, "Pupil", new Vector2(7f, 7f));
        pupil.anchorMin = pupil.anchorMax = new Vector2(0f, 0.5f);
        pupil.anchoredPosition = new Vector2(10f, 0f);
        SetImage(pupil, accentCyan);

        viewersText = CreateText(root, "0", 21, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor, "Count");
        SetAnchors(viewersText.rectTransform, new Vector2(0.30f, 0f), new Vector2(1f, 1f));
    }

    private void BuildLikeMetric(Transform parent)
    {
        RectTransform root = CreateRect(parent, "Likes", new Vector2(150f, 48f));
        root.anchorMin = root.anchorMax = new Vector2(0.80f, 0.5f);
        root.anchoredPosition = Vector2.zero;

        RectTransform palm = CreateRect(root, "ThumbPalm", new Vector2(17f, 15f));
        palm.anchorMin = palm.anchorMax = new Vector2(0f, 0.5f);
        palm.anchoredPosition = new Vector2(12f, -2f);
        SetImage(palm, paperColor);

        RectTransform thumb = CreateRect(root, "Thumb", new Vector2(8f, 16f));
        thumb.anchorMin = thumb.anchorMax = new Vector2(0f, 0.5f);
        thumb.anchoredPosition = new Vector2(8f, 8f);
        thumb.localRotation = Quaternion.Euler(0f, 0f, -28f);
        SetImage(thumb, paperColor);

        RectTransform wrist = CreateRect(root, "Wrist", new Vector2(6f, 15f));
        wrist.anchorMin = wrist.anchorMax = new Vector2(0f, 0.5f);
        wrist.anchoredPosition = new Vector2(2f, -2f);
        SetImage(wrist, accentYellow);

        likesText = CreateText(root, "0", 21, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor, "Count");
        SetAnchors(likesText.rectTransform, new Vector2(0.30f, 0f), new Vector2(1f, 1f));
    }

    private void BuildMissionPanel()
    {
        missionPanel = CreateRect(dashboardRoot, "MissionPanel", compactMissionSize);
        missionPanel.anchorMin = missionPanel.anchorMax = Vector2.one;
        missionPanel.pivot = Vector2.one;
        missionPanel.anchoredPosition = missionPanelOffset;
        missionPanel.localRotation = Quaternion.Euler(0f, 0f, missionPanelRotation);

        Image back = missionPanel.gameObject.AddComponent<Image>();
        back.color = inkColor;
        back.raycastTarget = false;
        Outline outline = missionPanel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(paperColor.r, paperColor.g, paperColor.b, 0.70f);
        outline.effectDistance = new Vector2(4f, -4f);

        missionPanelGroup = missionPanel.gameObject.AddComponent<CanvasGroup>();
        missionPanelGroup.alpha = compactMissionAlpha;
        missionPanelGroup.blocksRaycasts = false;
        missionPanelGroup.interactable = false;

        RectTransform whitePlate = CreateRect(missionPanel, "WhitePlate", Vector2.zero);
        Stretch(whitePlate);
        whitePlate.offsetMin = new Vector2(-10f, -10f);
        whitePlate.offsetMax = new Vector2(10f, 10f);
        whitePlate.SetAsFirstSibling();
        SetImage(whitePlate, new Color(paperColor.r, paperColor.g, paperColor.b, 0.14f));

        missionHeader = CreateText(missionPanel, "FAN MISSION // STANDBY", 27, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor, "Header");
        SetAnchors(missionHeader.rectTransform, new Vector2(0.07f, 0.84f), new Vector2(0.78f, 0.97f));

        Text cue = CreateText(missionPanel, "CURSOR FOCUS", 11, FontStyle.Bold, TextAnchor.MiddleRight, accentCyan, "FocusCue");
        SetAnchors(cue.rectTransform, new Vector2(0.72f, 0.86f), new Vector2(0.95f, 0.96f));

        missionListRoot = CreateRect(missionPanel, "MissionList", Vector2.zero);
        missionListRoot.anchorMin = new Vector2(0.055f, 0.08f);
        missionListRoot.anchorMax = new Vector2(0.945f, 0.80f);
        missionListRoot.offsetMin = Vector2.zero;
        missionListRoot.offsetMax = Vector2.zero;

        for (int i = 0; i < MaxMissionSlots; i++)
            BuildMissionRow(i);

        missionEmptyState = CreateText(missionPanel, "NO ACTIVE MISSION\n// WAITING FOR BROADCAST ORDER", 18, FontStyle.Bold, TextAnchor.MiddleCenter, paperColor, "EmptyState");
        SetAnchors(missionEmptyState.rectTransform, new Vector2(0.10f, 0.22f), new Vector2(0.90f, 0.72f));

        BuildMissionDetail();
        LayoutMissionStructure(false);
    }

    private void BuildMissionRow(int index)
    {
        RectTransform row = CreateRect(missionListRoot, $"MissionRow_{index}", new Vector2(0f, 40f));
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.localRotation = Quaternion.Euler(0f, 0f, index % 2 == 0 ? -1.2f : 1.0f);

        Image background = row.gameObject.AddComponent<Image>();
        background.color = new Color(0.07f, 0.075f, 0.10f, 0.98f);
        background.raycastTarget = false;
        Outline outline = row.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(paperColor.r, paperColor.g, paperColor.b, 0.28f);
        outline.effectDistance = new Vector2(2f, -2f);

        Text title = CreateText(row, $"MISSION {index + 1}", 14, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor, "Title");
        SetAnchors(title.rectTransform, new Vector2(0.05f, 0.10f), new Vector2(0.72f, 0.90f));

        Text progress = CreateText(row, "-- / --", 13, FontStyle.Bold, TextAnchor.MiddleRight, accentYellow, "Progress");
        SetAnchors(progress.rectTransform, new Vector2(0.68f, 0.10f), new Vector2(0.95f, 0.90f));

        missionRows[index] = row;
        missionRowBackgrounds[index] = background;
        missionRowOutlines[index] = outline;
        missionRowTitles[index] = title;
        missionRowProgress[index] = progress;
    }

    private void BuildMissionDetail()
    {
        missionDetailRoot = CreateRect(missionPanel, "MissionDetail", Vector2.zero);
        missionDetailRoot.anchorMin = new Vector2(0.46f, 0.08f);
        missionDetailRoot.anchorMax = new Vector2(0.95f, 0.80f);
        missionDetailRoot.offsetMin = Vector2.zero;
        missionDetailRoot.offsetMax = Vector2.zero;
        missionDetailRoot.localRotation = Quaternion.Euler(0f, 0f, -1.2f);

        Image back = missionDetailRoot.gameObject.AddComponent<Image>();
        back.color = new Color(paperColor.r, paperColor.g, paperColor.b, 0.965f);
        back.raycastTarget = false;
        Outline outline = missionDetailRoot.gameObject.AddComponent<Outline>();
        outline.effectColor = inkColor;
        outline.effectDistance = new Vector2(5f, -5f);

        missionDetailGroup = missionDetailRoot.gameObject.AddComponent<CanvasGroup>();
        missionDetailGroup.alpha = 0f;
        missionDetailGroup.blocksRaycasts = false;
        missionDetailGroup.interactable = false;

        missionDetailTitle = CreateText(missionDetailRoot, "NO MISSION", 26, FontStyle.Bold, TextAnchor.UpperLeft, inkColor, "Title");
        SetAnchors(missionDetailTitle.rectTransform, new Vector2(0.07f, 0.78f), new Vector2(0.94f, 0.94f));

        missionDetailType = CreateText(missionDetailRoot, "STANDBY", 11, FontStyle.Bold, TextAnchor.UpperLeft, accentPink, "Type");
        SetAnchors(missionDetailType.rectTransform, new Vector2(0.07f, 0.70f), new Vector2(0.94f, 0.79f));

        missionDetailDescription = CreateText(missionDetailRoot, "NO ACTIVE FAN MISSION", 14, FontStyle.Normal, TextAnchor.UpperLeft, inkColor, "Description");
        SetAnchors(missionDetailDescription.rectTransform, new Vector2(0.07f, 0.42f), new Vector2(0.94f, 0.69f));

        missionDetailProgress = CreateText(missionDetailRoot, "PROGRESS  -- / --", 18, FontStyle.Bold, TextAnchor.MiddleLeft, inkColor, "Progress");
        SetAnchors(missionDetailProgress.rectTransform, new Vector2(0.07f, 0.31f), new Vector2(0.94f, 0.42f));

        missionDetailReward = CreateText(missionDetailRoot, "SUCCESS  --", 13, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.06f, 0.52f, 0.48f, 1f), "Reward");
        SetAnchors(missionDetailReward.rectTransform, new Vector2(0.07f, 0.17f), new Vector2(0.94f, 0.29f));

        missionDetailFailure = CreateText(missionDetailRoot, "FAIL  --", 13, FontStyle.Bold, TextAnchor.MiddleLeft, accentPink, "Failure");
        SetAnchors(missionDetailFailure.rectTransform, new Vector2(0.07f, 0.05f), new Vector2(0.94f, 0.17f));
    }

    private void LayoutMissionRows(bool missionFocused)
    {
        if (missionListRoot == null)
            return;

        float availableWidth = Mathf.Max(160f, missionListRoot.rect.width);
        float rowHeight = missionFocused ? 68f : 38f;
        float gap = missionFocused ? 8f : 5f;

        for (int i = 0; i < missionRows.Length; i++)
        {
            RectTransform row = missionRows[i];
            if (row == null)
                continue;

            Vector2 targetSize = new(availableWidth, rowHeight);
            Vector2 targetPosition = new(0f, -i * (rowHeight + gap));
            if ((row.sizeDelta - targetSize).sqrMagnitude > 0.01f)
                row.sizeDelta = targetSize;
            if ((row.anchoredPosition - targetPosition).sqrMagnitude > 0.01f)
                row.anchoredPosition = targetPosition;
        }
    }

    private void Subscribe()
    {
        if (subscribedMissionSystem != fanMissionSystem)
        {
            if (subscribedMissionSystem != null)
                subscribedMissionSystem.MissionsChanged -= RefreshMissionData;
            subscribedMissionSystem = fanMissionSystem;
            if (subscribedMissionSystem != null)
                subscribedMissionSystem.MissionsChanged += RefreshMissionData;
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
        if (subscribedMissionSystem != null)
            subscribedMissionSystem.MissionsChanged -= RefreshMissionData;
        if (subscribedRunProgress != null)
            subscribedRunProgress.BroadcastMetricsChanged -= HandleBroadcastMetricsChanged;
        subscribedMissionSystem = null;
        subscribedRunProgress = null;
    }

    private void RefreshMissionData()
    {
        if (missionPanel == null)
            return;

        int count = fanMissionSystem != null ? Mathf.Min(MaxMissionSlots, fanMissionSystem.ActiveMissions.Count) : 0;

        if (missionHeader != null)
        {
            string header = count > 0 ? $"FAN MISSION // ACTIVE {count}" : "FAN MISSION // STANDBY";
            SetTextIfChanged(missionHeader, header);
        }

        if (missionEmptyState != null && missionEmptyState.gameObject.activeSelf != (count == 0))
            missionEmptyState.gameObject.SetActive(count == 0);

        for (int i = 0; i < MaxMissionSlots; i++)
        {
            RectTransform row = missionRows[i];
            if (row == null)
                continue;

            bool active = i < count;
            if (row.gameObject.activeSelf != active)
                row.gameObject.SetActive(active);
            if (!active)
                continue;

            FanMissionRuntime runtime = fanMissionSystem.ActiveMissions[i];
            FanMissionSO definition = runtime != null ? runtime.Definition : null;
            if (definition == null)
                continue;

            string title = string.IsNullOrWhiteSpace(definition.missionName)
                ? $"MISSION {i + 1}"
                : definition.missionName.ToUpperInvariant();
            string progress = $"{runtime.Progress} / {Mathf.Max(1, definition.targetCount)}";
            SetTextIfChanged(missionRowTitles[i], title);
            SetTextIfChanged(missionRowProgress[i], progress);
        }

        if (count <= 0)
            selectedMissionIndex = -1;
        else if (selectedMissionIndex < 0 || selectedMissionIndex >= count)
            selectedMissionIndex = 0;

        layoutDirty = true;
        RefreshMissionDetail();
        RefreshMissionRowVisuals();
    }

    private void RefreshMissionDetail()
    {
        if (missionDetailTitle == null)
            return;

        int count = fanMissionSystem != null ? fanMissionSystem.ActiveMissions.Count : 0;
        if (selectedMissionIndex < 0 || selectedMissionIndex >= count)
        {
            SetTextIfChanged(missionDetailTitle, "NO ACTIVE MISSION");
            SetTextIfChanged(missionDetailType, "STANDBY");
            SetTextIfChanged(missionDetailDescription, "WAITING FOR THE NEXT BROADCAST ORDER.");
            SetTextIfChanged(missionDetailProgress, "PROGRESS  -- / --");
            SetTextIfChanged(missionDetailReward, "SUCCESS  --");
            SetTextIfChanged(missionDetailFailure, "FAIL  --");
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
        string timer = definition.duration > 0f ? $"   TIME {runtime.RemainingTime:0.0}s" : string.Empty;

        SetTextIfChanged(missionDetailTitle, title);
        SetTextIfChanged(missionDetailType, definition.type.ToString().ToUpperInvariant());
        SetTextIfChanged(missionDetailDescription, description);
        SetTextIfChanged(missionDetailProgress, $"PROGRESS  {runtime.Progress} / {Mathf.Max(1, definition.targetCount)}{timer}");
        SetTextIfChanged(missionDetailReward, $"SUCCESS  POP {Signed(definition.successPopularity)}   FP {Signed(definition.successFanPoints)}");
        SetTextIfChanged(missionDetailFailure, $"FAIL     POP {Signed(definition.failPopularity)}   FP {Signed(definition.failFanPoints)}");
    }

    private void RefreshMissionRowVisuals()
    {
        int count = fanMissionSystem != null ? Mathf.Min(MaxMissionSlots, fanMissionSystem.ActiveMissions.Count) : 0;
        for (int i = 0; i < MaxMissionSlots; i++)
        {
            if (missionRowBackgrounds[i] == null || missionRowOutlines[i] == null)
                continue;

            bool active = i < count;
            bool hovered = active && i == hoveredMissionIndex;
            bool selected = active && i == selectedMissionIndex;

            Color targetBack = hovered
                ? new Color(accentCyan.r, accentCyan.g, accentCyan.b, 0.96f)
                : selected && focus == DashboardFocus.Mission
                    ? new Color(0.10f, 0.13f, 0.17f, 1f)
                    : new Color(0.07f, 0.075f, 0.10f, 0.98f);
            if (missionRowBackgrounds[i].color != targetBack)
                missionRowBackgrounds[i].color = targetBack;

            Color outlineColor = hovered
                ? paperColor
                : selected
                    ? accentCyan
                    : new Color(paperColor.r, paperColor.g, paperColor.b, 0.28f);
            if (missionRowOutlines[i].effectColor != outlineColor)
                missionRowOutlines[i].effectColor = outlineColor;

            if (missionRowTitles[i] != null)
            {
                Color target = hovered ? inkColor : paperColor;
                if (missionRowTitles[i].color != target)
                    missionRowTitles[i].color = target;
            }

            if (missionRowProgress[i] != null)
            {
                Color target = hovered ? inkColor : accentYellow;
                if (missionRowProgress[i].color != target)
                    missionRowProgress[i].color = target;
            }
        }
    }

    private void HandleBroadcastMetricsChanged(int viewers, int likes)
    {
        ApplyBroadcastMetricText(viewers, likes);
    }

    private void RefreshBroadcastMetrics()
    {
        ApplyBroadcastMetricText(
            runProgress != null ? runProgress.Viewers : 0,
            runProgress != null ? runProgress.Likes : 0);
    }

    private void ApplyBroadcastMetricText(int viewers, int likes)
    {
        SetTextIfChanged(viewersText, Mathf.Max(0, viewers).ToString("N0"));
        SetTextIfChanged(likesText, Mathf.Max(0, likes).ToString("N0"));
    }

    private static void SetTextIfChanged(Text text, string value)
    {
        if (text != null && text.text != value)
            text.text = value;
    }

    private static string Signed(int value)
    {
        return value > 0 ? $"+{value}" : value.ToString();
    }

    private static float NormalizeAngle(float degrees)
    {
        return Mathf.Repeat(degrees + 180f, 360f) - 180f;
    }

    private static bool Approximately(Vector2 a, Vector2 b)
    {
        return (a - b).sqrMagnitude <= 0.0001f;
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

    private static void SetImage(RectTransform rect, Color color)
    {
        Image image = rect.GetComponent<Image>();
        if (image == null)
            image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
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

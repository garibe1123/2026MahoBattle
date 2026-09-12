using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Combat TAB 화면을 PACK + FAN MISSION + Broadcast Metrics가 결합된 하나의 가변 Dashboard로 구성합니다.
///
/// 디자인 원칙:
/// - Persona 계열의 기울어진 사각형/사선/비대칭 여백을 사용합니다.
/// - 클릭으로 메뉴를 확정하지 않습니다. 실제 Input.mousePosition이 가리키는 영역만으로 Focus를 바꿉니다.
/// - PACK 위치는 기존 Inventory 시스템이 계속 소유합니다. 이 Controller는 PACK의 Scale/Rotation/Alpha만 보정합니다.
/// - Mission은 FanMissionSystem의 기존 Runtime 데이터를 읽기만 합니다.
/// - 댓글 문자열은 출력하지 않고 RunProgressSystem의 Viewers / Likes만 우측 상단 Bar에 표시합니다.
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

    [Header("REFERENCES — 자동 연결")]
    [Tooltip("현재 Combat/Reward 상태를 확인하는 Run Manager입니다. 비어 있으면 자동으로 찾습니다.")]
    [SerializeField] private BattleRunManager runManager;
    [Tooltip("현재 활성 Fan Mission 목록과 진행도를 제공하는 기존 Mission System입니다.")]
    [SerializeField] private FanMissionSystem fanMissionSystem;
    [Tooltip("실시간 시청자 수와 좋아요 수를 제공하는 Run Progress System입니다.")]
    [SerializeField] private RunProgressSystem runProgress;
    [Tooltip("TAB Hold 상태와 기존 PACK RectTransform을 제공하는 Loadout UI입니다.")]
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;

    [Header("FOCUS TRACKING — 커서 위치 기반")]
    [Tooltip("커서 X가 화면 너비의 이 비율보다 오른쪽으로 가면 Mission Focus로 전환합니다. Mission Panel 위에서는 이 값과 무관하게 Mission Focus입니다.")]
    [SerializeField, Range(0.50f, 0.85f)] private float missionFocusEnterX = 0.62f;
    [Tooltip("Mission Focus 상태에서 커서 X가 이 값보다 왼쪽으로 돌아오면 PACK Focus로 복귀합니다. Enter 값보다 작게 두어 경계 떨림을 방지합니다.")]
    [SerializeField, Range(0.30f, 0.70f)] private float packFocusReturnX = 0.53f;
    [Tooltip("RectTransform이 목표 Layout을 따라가는 속도입니다. 높을수록 빠르게 반응합니다.")]
    [SerializeField, Range(4f, 30f)] private float layoutSharpness = 13f;

    [Header("PACK FOCUS MORPH — 기존 PACK 시각 보정")]
    [Tooltip("PACK에 커서 Focus가 있을 때 GridBoard의 크기입니다. 기존 레이아웃의 기본 크기를 유지하려면 1을 사용합니다.")]
    [SerializeField, Range(0.70f, 1.15f)] private float packFocusedScale = 1f;
    [Tooltip("Mission에 Focus가 있을 때 PACK이 물러나는 크기입니다.")]
    [SerializeField, Range(0.55f, 1f)] private float missionFocusedPackScale = 0.78f;
    [Tooltip("PACK Focus 상태의 GridBoard 불투명도입니다.")]
    [SerializeField, Range(0f, 1f)] private float packFocusedAlpha = 1f;
    [Tooltip("Mission Focus 상태에서 PACK이 배경으로 물러날 때의 불투명도입니다.")]
    [SerializeField, Range(0f, 1f)] private float missionFocusedPackAlpha = 0.72f;
    [Tooltip("PACK Focus 상태의 GridBoard Z 회전 각도입니다.")]
    [SerializeField, Range(-15f, 15f)] private float packFocusedRotation = -4f;
    [Tooltip("Mission Focus 상태에서 PACK이 정돈되며 돌아가는 Z 회전 각도입니다.")]
    [SerializeField, Range(-15f, 15f)] private float missionFocusedPackRotation = -1.5f;

    [Header("MISSION PANEL — 크기 / 위치")]
    [Tooltip("PACK Focus일 때 우측에 접혀 있는 Mission Panel 크기입니다.")]
    [SerializeField] private Vector2 compactMissionSize = new(430f, 300f);
    [Tooltip("Mission Focus일 때 상세 정보를 포함해 확장되는 Panel 크기입니다.")]
    [SerializeField] private Vector2 focusedMissionSize = new(700f, 700f);
    [Tooltip("우측 상단 기준 Mission Panel 위치입니다.")]
    [SerializeField] private Vector2 missionPanelOffset = new(-44f, -128f);
    [Tooltip("Mission Panel 자체의 기울기입니다.")]
    [SerializeField, Range(-10f, 10f)] private float missionPanelRotation = 2.2f;
    [Tooltip("Mission Focus가 아닐 때 패널 전체의 불투명도입니다.")]
    [SerializeField, Range(0.3f, 1f)] private float compactMissionAlpha = 0.78f;

    [Header("BROADCAST METRICS — 우측 상단")]
    [Tooltip("시청자/좋아요 Bar의 크기입니다.")]
    [SerializeField] private Vector2 metricBarSize = new(410f, 64f);
    [Tooltip("화면 우측 상단 기준 Metric Bar 위치입니다.")]
    [SerializeField] private Vector2 metricBarOffset = new(-42f, -42f);
    [Tooltip("Metric Bar의 사선 회전 각도입니다.")]
    [SerializeField, Range(-8f, 8f)] private float metricBarRotation = -1.6f;

    [Header("THEME — Persona 계열 기하학 색상")]
    [Tooltip("주 패널과 Mission Row에 사용하는 거의 검은 잉크 색입니다.")]
    [SerializeField] private Color inkColor = new(0.025f, 0.028f, 0.045f, 0.985f);
    [Tooltip("텍스트/외곽선에 사용하는 밝은 종이 색입니다.")]
    [SerializeField] private Color paperColor = new(0.94f, 0.95f, 0.97f, 1f);
    [Tooltip("Mission Focus와 방송 지표에 사용하는 청록 Accent입니다.")]
    [SerializeField] private Color accentCyan = new(0.10f, 0.88f, 0.95f, 1f);
    [Tooltip("진행률/성공 보상에 사용하는 노란 Accent입니다.")]
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [Tooltip("실패/위험 정보에 사용하는 핑크 Accent입니다.")]
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);

    private RectTransform fullRoot;
    private RectTransform packBoard;
    private CanvasGroup packBoardGroup;

    private RectTransform dashboardRoot;
    private RectTransform missionPanel;
    private CanvasGroup missionPanelGroup;
    private RectTransform missionListRoot;
    private RectTransform missionDetailRoot;
    private CanvasGroup missionDetailGroup;
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
    private bool subscribed;
    private bool dashboardWasOpen;
    private float nextResolveAt;

    private void Awake()
    {
        ResolveReferences();
        TryResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        nextResolveAt = 0f;
        dashboardWasOpen = false;
        focus = DashboardFocus.Pack;
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (dashboardRoot != null)
            dashboardRoot.gameObject.SetActive(false);
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
                ResetPackVisual();
                if (dashboardRoot != null)
                    dashboardRoot.gameObject.SetActive(false);
            }
            return;
        }

        if (!dashboardWasOpen)
        {
            dashboardWasOpen = true;
            focus = DashboardFocus.Pack;
            if (dashboardRoot != null)
                dashboardRoot.gameObject.SetActive(true);
            RefreshMissionData();
            RefreshBroadcastMetrics();
        }

        TrackCursorFocus();
        AnimateLayout();
    }

    private bool IsDashboardOpen()
    {
        return runManager != null && runManager.RunActive &&
               runManager.State == BattleRunState.Combat &&
               kineticLoadout != null && kineticLoadout.IsSwitchBoardOpen &&
               fullRoot != null && packBoard != null && dashboardRoot != null;
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
        int result = -1;
        for (int i = 0; i < missionRows.Length; i++)
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
        RefreshMissionRowVisuals();
    }

    private void SetMissionSelection(int index)
    {
        int count = fanMissionSystem != null ? fanMissionSystem.ActiveMissions.Count : 0;
        index = count > 0 ? Mathf.Clamp(index, 0, count - 1) : -1;
        if (selectedMissionIndex == index)
            return;

        selectedMissionIndex = index;
        RefreshMissionDetail();
        RefreshMissionRowVisuals();
    }

    private void AnimateLayout()
    {
        float t = 1f - Mathf.Exp(-Mathf.Max(1f, layoutSharpness) * Time.unscaledDeltaTime);
        bool missionFocused = focus == DashboardFocus.Mission;

        if (packBoard != null)
        {
            float targetScale = missionFocused ? missionFocusedPackScale : packFocusedScale;
            Vector3 target = Vector3.one * targetScale;
            packBoard.localScale = Vector3.Lerp(packBoard.localScale, target, t);

            float targetRotation = missionFocused ? missionFocusedPackRotation : packFocusedRotation;
            float currentRotation = NormalizeAngle(packBoard.localEulerAngles.z);
            float nextRotation = Mathf.LerpAngle(currentRotation, targetRotation, t);
            packBoard.localRotation = Quaternion.Euler(0f, 0f, nextRotation);
        }

        if (packBoardGroup != null)
        {
            float targetAlpha = missionFocused ? missionFocusedPackAlpha : packFocusedAlpha;
            packBoardGroup.alpha = Mathf.Lerp(packBoardGroup.alpha, targetAlpha, t);
        }

        if (missionPanel != null)
        {
            Vector2 targetSize = missionFocused ? focusedMissionSize : compactMissionSize;
            missionPanel.sizeDelta = Vector2.Lerp(missionPanel.sizeDelta, targetSize, t);
            missionPanel.anchoredPosition = Vector2.Lerp(missionPanel.anchoredPosition, missionPanelOffset, t);
            missionPanel.localRotation = Quaternion.Euler(0f, 0f, missionPanelRotation);
        }

        if (missionPanelGroup != null)
        {
            float targetAlpha = missionFocused ? 1f : compactMissionAlpha;
            missionPanelGroup.alpha = Mathf.Lerp(missionPanelGroup.alpha, targetAlpha, t);
        }

        if (missionDetailGroup != null)
        {
            float targetAlpha = missionFocused && selectedMissionIndex >= 0 ? 1f : 0f;
            missionDetailGroup.alpha = Mathf.Lerp(missionDetailGroup.alpha, targetAlpha, t);
            float detailScale = Mathf.Lerp(0.92f, 1f, missionDetailGroup.alpha);
            missionDetailRoot.localScale = Vector3.one * detailScale;
        }

        LayoutMissionRows(missionFocused);
    }

    private void ResetPackVisual()
    {
        if (packBoard != null)
        {
            packBoard.localScale = Vector3.one * packFocusedScale;
            packBoard.localRotation = Quaternion.Euler(0f, 0f, packFocusedRotation);
        }
        if (packBoardGroup != null)
            packBoardGroup.alpha = packFocusedAlpha;
    }

    private void ResolveReferencesWhenNeeded()
    {
        if (runManager != null && fanMissionSystem != null && runProgress != null && kineticLoadout != null)
            return;
        if (Time.unscaledTime < nextResolveAt)
            return;

        nextResolveAt = Time.unscaledTime + 0.25f;
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
        if (fullRoot != null && packBoard != null && dashboardRoot != null)
            return;
        if (Time.unscaledTime < nextResolveAt)
            return;

        nextResolveAt = Time.unscaledTime + 0.10f;
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

    private void BuildDashboardUi()
    {
        RectTransform existing = fullRoot.Find("BroadcastDashboard") as RectTransform;
        if (existing != null)
        {
            dashboardRoot = existing;
            ResolveBuiltDashboard(existing);
            dashboardRoot.gameObject.SetActive(false);
            return;
        }

        dashboardRoot = CreateRect(fullRoot, "BroadcastDashboard", Vector2.zero);
        Stretch(dashboardRoot);
        dashboardRoot.SetAsLastSibling();

        BuildMetricBar();
        BuildMissionPanel();
        dashboardRoot.gameObject.SetActive(false);
    }

    private void ResolveBuiltDashboard(RectTransform root)
    {
        metricBar = root.Find("BroadcastMetricBar") as RectTransform;
        missionPanel = root.Find("MissionPanel") as RectTransform;
        missionPanelGroup = missionPanel != null ? missionPanel.GetComponent<CanvasGroup>() : null;
        missionListRoot = missionPanel != null ? missionPanel.Find("MissionList") as RectTransform : null;
        missionDetailRoot = missionPanel != null ? missionPanel.Find("MissionDetail") as RectTransform : null;
        missionDetailGroup = missionDetailRoot != null ? missionDetailRoot.GetComponent<CanvasGroup>() : null;

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

        RectTransform accent = CreateRect(metricBar, "AccentSlash", new Vector2(18f, metricBarSize.y + 18f));
        accent.anchorMin = accent.anchorMax = new Vector2(0f, 0.5f);
        accent.anchoredPosition = new Vector2(10f, 0f);
        accent.localRotation = Quaternion.Euler(0f, 0f, 12f);
        SetImage(accent, accentCyan);

        BuildEyeMetric(metricBar);
        BuildLikeMetric(metricBar);
    }

    private void BuildEyeMetric(Transform parent)
    {
        RectTransform root = CreateRect(parent, "Viewers", new Vector2(176f, 48f));
        root.anchorMin = root.anchorMax = new Vector2(0.28f, 0.5f);
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
        RectTransform root = CreateRect(parent, "Likes", new Vector2(176f, 48f));
        root.anchorMin = root.anchorMax = new Vector2(0.75f, 0.5f);
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
        outline.effectColor = accentCyan;
        outline.effectDistance = new Vector2(5f, -5f);
        missionPanelGroup = missionPanel.gameObject.AddComponent<CanvasGroup>();
        missionPanelGroup.blocksRaycasts = false;
        missionPanelGroup.interactable = false;

        RectTransform whitePlate = CreateRect(missionPanel, "WhitePlate", Vector2.zero);
        whitePlate.anchorMin = new Vector2(0f, 0f);
        whitePlate.anchorMax = new Vector2(1f, 1f);
        whitePlate.offsetMin = new Vector2(-10f, -10f);
        whitePlate.offsetMax = new Vector2(10f, 10f);
        whitePlate.SetAsFirstSibling();
        SetImage(whitePlate, new Color(paperColor.r, paperColor.g, paperColor.b, 0.12f));

        Text header = CreateText(missionPanel, "FAN MISSION // LIVE", 28, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor, "Header");
        SetAnchors(header.rectTransform, new Vector2(0.06f, 0.86f), new Vector2(0.76f, 0.97f));

        Text cue = CreateText(missionPanel, "CURSOR FOCUS", 11, FontStyle.Bold, TextAnchor.MiddleRight, accentCyan, "FocusCue");
        SetAnchors(cue.rectTransform, new Vector2(0.72f, 0.88f), new Vector2(0.95f, 0.96f));

        RectTransform slash = CreateRect(missionPanel, "HeaderSlash", new Vector2(14f, 92f));
        slash.anchorMin = slash.anchorMax = new Vector2(0.04f, 0.92f);
        slash.anchoredPosition = Vector2.zero;
        slash.localRotation = Quaternion.Euler(0f, 0f, 12f);
        SetImage(slash, accentCyan);

        missionListRoot = CreateRect(missionPanel, "MissionList", Vector2.zero);
        missionListRoot.anchorMin = new Vector2(0.05f, 0.08f);
        missionListRoot.anchorMax = new Vector2(0.48f, 0.84f);
        missionListRoot.offsetMin = Vector2.zero;
        missionListRoot.offsetMax = Vector2.zero;

        for (int i = 0; i < MaxMissionSlots; i++)
            BuildMissionRow(i);

        BuildMissionDetail();
        LayoutMissionRows(false);
    }

    private void BuildMissionRow(int index)
    {
        RectTransform row = CreateRect(missionListRoot, $"MissionRow_{index}", new Vector2(0f, 62f));
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.localRotation = Quaternion.Euler(0f, 0f, index % 2 == 0 ? -1.2f : 1.0f);

        Image background = row.gameObject.AddComponent<Image>();
        background.color = new Color(0.07f, 0.075f, 0.10f, 0.98f);
        background.raycastTarget = false;
        Outline outline = row.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(paperColor.r, paperColor.g, paperColor.b, 0.28f);
        outline.effectDistance = new Vector2(2f, -2f);

        Text title = CreateText(row, $"MISSION {index + 1}", 15, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor, "Title");
        SetAnchors(title.rectTransform, new Vector2(0.06f, 0.18f), new Vector2(0.70f, 0.86f));

        Text progress = CreateText(row, "-- / --", 14, FontStyle.Bold, TextAnchor.MiddleRight, accentYellow, "Progress");
        SetAnchors(progress.rectTransform, new Vector2(0.66f, 0.18f), new Vector2(0.94f, 0.86f));

        missionRows[index] = row;
        missionRowBackgrounds[index] = background;
        missionRowOutlines[index] = outline;
        missionRowTitles[index] = title;
        missionRowProgress[index] = progress;
    }

    private void BuildMissionDetail()
    {
        missionDetailRoot = CreateRect(missionPanel, "MissionDetail", Vector2.zero);
        missionDetailRoot.anchorMin = new Vector2(0.51f, 0.08f);
        missionDetailRoot.anchorMax = new Vector2(0.95f, 0.84f);
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

        float availableWidth = Mathf.Max(150f, missionListRoot.rect.width);
        float rowHeight = missionFocused ? 74f : 54f;
        float gap = missionFocused ? 10f : 7f;

        for (int i = 0; i < missionRows.Length; i++)
        {
            RectTransform row = missionRows[i];
            if (row == null)
                continue;

            row.sizeDelta = new Vector2(availableWidth, rowHeight);
            row.anchoredPosition = new Vector2(0f, -i * (rowHeight + gap));
        }
    }

    private void Subscribe()
    {
        if (subscribed)
            return;

        if (fanMissionSystem != null)
            fanMissionSystem.MissionsChanged += RefreshMissionData;
        if (runProgress != null)
            runProgress.BroadcastMetricsChanged += HandleBroadcastMetricsChanged;

        subscribed = fanMissionSystem != null || runProgress != null;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (fanMissionSystem != null)
            fanMissionSystem.MissionsChanged -= RefreshMissionData;
        if (runProgress != null)
            runProgress.BroadcastMetricsChanged -= HandleBroadcastMetricsChanged;
        subscribed = false;
    }

    private void RefreshMissionData()
    {
        if (missionRows[0] == null)
            return;

        int count = fanMissionSystem != null ? Mathf.Min(MaxMissionSlots, fanMissionSystem.ActiveMissions.Count) : 0;
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

            if (missionRowTitles[i] != null)
                missionRowTitles[i].text = string.IsNullOrWhiteSpace(definition.missionName)
                    ? $"MISSION {i + 1}"
                    : definition.missionName.ToUpperInvariant();
            if (missionRowProgress[i] != null)
                missionRowProgress[i].text = $"{runtime.Progress} / {Mathf.Max(1, definition.targetCount)}";
        }

        if (count <= 0)
        {
            selectedMissionIndex = -1;
        }
        else if (selectedMissionIndex < 0 || selectedMissionIndex >= count)
        {
            selectedMissionIndex = 0;
        }

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
            missionDetailTitle.text = "NO ACTIVE MISSION";
            missionDetailType.text = "STANDBY";
            missionDetailDescription.text = "FAN MISSION FEED IS EMPTY.";
            missionDetailProgress.text = "PROGRESS  -- / --";
            missionDetailReward.text = "SUCCESS  --";
            missionDetailFailure.text = "FAIL  --";
            return;
        }

        FanMissionRuntime runtime = fanMissionSystem.ActiveMissions[selectedMissionIndex];
        FanMissionSO definition = runtime != null ? runtime.Definition : null;
        if (definition == null)
            return;

        missionDetailTitle.text = string.IsNullOrWhiteSpace(definition.missionName)
            ? $"MISSION {selectedMissionIndex + 1}"
            : definition.missionName.ToUpperInvariant();
        missionDetailType.text = definition.type.ToString().ToUpperInvariant();
        missionDetailDescription.text = string.IsNullOrWhiteSpace(definition.description)
            ? "NO DESCRIPTION"
            : definition.description;

        string timer = definition.duration > 0f ? $"   TIME {runtime.RemainingTime:0.0}s" : string.Empty;
        missionDetailProgress.text = $"PROGRESS  {runtime.Progress} / {Mathf.Max(1, definition.targetCount)}{timer}";
        missionDetailReward.text = $"SUCCESS  POP {Signed(definition.successPopularity)}   FP {Signed(definition.successFanPoints)}";
        missionDetailFailure.text = $"FAIL     POP {Signed(definition.failPopularity)}   FP {Signed(definition.failFanPoints)}";
    }

    private void RefreshMissionRowVisuals()
    {
        int count = fanMissionSystem != null ? fanMissionSystem.ActiveMissions.Count : 0;
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

            Color outlineColor = hovered ? paperColor : selected ? accentCyan : new Color(paperColor.r, paperColor.g, paperColor.b, 0.28f);
            if (missionRowOutlines[i].effectColor != outlineColor)
                missionRowOutlines[i].effectColor = outlineColor;

            if (missionRowTitles[i] != null)
                missionRowTitles[i].color = hovered ? inkColor : paperColor;
            if (missionRowProgress[i] != null)
                missionRowProgress[i].color = hovered ? inkColor : accentYellow;
        }
    }

    private void HandleBroadcastMetricsChanged(int viewers, int likes)
    {
        ApplyBroadcastMetricText(viewers, likes);
    }

    private void RefreshBroadcastMetrics()
    {
        ApplyBroadcastMetricText(runProgress != null ? runProgress.Viewers : 0,
                                 runProgress != null ? runProgress.Likes : 0);
    }

    private void ApplyBroadcastMetricText(int viewers, int likes)
    {
        if (viewersText != null)
            viewersText.text = Mathf.Max(0, viewers).ToString("N0");
        if (likesText != null)
            likesText.text = Mathf.Max(0, likes).ToString("N0");
    }

    private static string Signed(int value)
    {
        return value > 0 ? $"+{value}" : value.ToString();
    }

    private static float NormalizeAngle(float degrees)
    {
        return Mathf.Repeat(degrees + 180f, 360f) - 180f;
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static Text CreateText(Transform parent, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color, string objectName)
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
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Combat TAB의 Presentation 보정만 담당합니다.
/// - 기존 BroadcastMetricBar(LIVE / Viewers / Likes)는 그대로 유지합니다.
/// - FAN MISSION 아래에 방송 스타일 Live Chat을 표시합니다.
/// - Combat TAB의 PACK 위치를 보정합니다.
/// - 우측 Mission / Chat 포커스 중에는 Equipment Detail을 숨깁니다.
/// - Reward / Equipment 데이터 소유권은 건드리지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33520)]
public sealed class BattleCombatTabPresentationPolishController : MonoBehaviour
{
    private const float DetailAnchorX = 0.72f;
    private const float DetailAnchorY = 0.52f;
    private const float DetailScale = 0.92f;
    private const int DetailSortingPadding = 15;

    private const float ChatHeight = 184f;
    private const float ChatGap = 16f;
    private const float ZeroViewerChatInterval = 45f;
    private const float HighViewerChatInterval = 0.70f;
    private const int MaxChatLines = 5;

    // BattleBroadcastDashboardController의 Focus hysteresis와 동일한 기준을 사용합니다.
    private const float RightPanelEnterX = 0.61f;
    private const float RightPanelReturnX = 0.50f;

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
    [SerializeField] private RunProgressSystem runProgress;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;

    [Header("COMBAT PACK POSITION")]
    [Tooltip("Combat TAB에서 PACK Grid를 authoritative anchor 기준으로 추가 이동시키는 X 값입니다. 값이 작을수록 더 왼쪽입니다.")]
    [SerializeField, Range(-240f, 320f)] private float combatPackRightShift = 45f;

    [Header("LIVE CHAT — VIEWER PACED")]
    [SerializeField, Range(20f, 90f)] private float zeroViewerChatInterval = ZeroViewerChatInterval;
    [SerializeField, Range(0.35f, 4f)] private float highViewerChatInterval = HighViewerChatInterval;
    [SerializeField, Range(3, 7)] private int maxChatLines = MaxChatLines;

    private RectTransform dashboardRoot;
    private RectTransform missionPanel;
    private Canvas dashboardCanvas;
    private RectTransform metricBar;

    private RectTransform chatPanel;
    private CanvasGroup chatGroup;
    private Text chatHeader;
    private Text chatBody;

    private Canvas detailCanvas;
    private int originalDetailSortingOrder;
    private bool originalDetailOverrideSorting;
    private bool detailSortingCaptured;
    private bool rightPanelFocused;

    private readonly List<string> chatHistory = new();
    private int chatSequence;
    private float nextChatAt;
    private float nextResolveAt;

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
    }

    private void OnDisable()
    {
        rightPanelFocused = false;
        RestoreDetailSorting();
        SetChatVisible(false);
        RestoreMetricFrame();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextResolveAt)
        {
            nextResolveAt = Time.unscaledTime + 0.20f;
            ResolveReferences(false);
            ResolveDashboardUi();
        }

        bool open = IsCombatTabOpen();
        if (!open)
        {
            rightPanelFocused = false;
            SetChatVisible(false);
            return;
        }

        RestoreMetricFrame();
        EnsureChatPanel();
        SetChatVisible(true);
        UpdateChatFeed();
    }

    private void LateUpdate()
    {
        if (!IsCombatTabOpen())
        {
            rightPanelFocused = false;
            RestoreDetailSorting();
            return;
        }

        ResolveDashboardUi();
        RestoreMetricFrame();
        EnsureChatPanel();
        UpdateRightPanelFocus();
        ApplyCombatPackPosition();
        ApplyDetailPresentation();
        ApplyChatLayout();
    }

    private bool IsCombatTabOpen()
    {
        return runManager != null && runManager.RunActive &&
               runManager.State == BattleRunState.Combat &&
               kineticLoadout != null && kineticLoadout.IsSwitchBoardOpen;
    }

    private void ResolveReferences(bool force)
    {
        if (force || runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (force || runProgress == null)
            runProgress = FindFirstObjectByType<RunProgressSystem>();
        if (force || kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
        if (force || detailController == null)
            detailController = FindFirstObjectByType<BattleEquipmentDetailPanelController>(FindObjectsInactive.Include);
    }

    private void ResolveDashboardUi()
    {
        if (dashboardRoot == null)
        {
            RectTransform fullRoot = kineticLoadout != null ? kineticLoadout.FullRoot : null;
            if (fullRoot != null)
                dashboardRoot = fullRoot.Find("BroadcastDashboard") as RectTransform;
        }

        if (dashboardRoot == null)
            return;

        dashboardCanvas ??= dashboardRoot.GetComponent<Canvas>();
        missionPanel ??= dashboardRoot.Find("MissionPanel") as RectTransform;
        metricBar ??= dashboardRoot.Find("BroadcastMetricBar") as RectTransform;

        if (chatPanel == null)
            chatPanel = dashboardRoot.Find("LiveChatPanel") as RectTransform;
    }

    private void RestoreMetricFrame()
    {
        if (dashboardRoot == null)
            return;

        metricBar ??= dashboardRoot.Find("BroadcastMetricBar") as RectTransform;
        if (metricBar != null && !metricBar.gameObject.activeSelf)
            metricBar.gameObject.SetActive(true);

        Transform textOnlyMetric = dashboardRoot.Find("BroadcastMetricText");
        if (textOnlyMetric != null && textOnlyMetric.gameObject.activeSelf)
            textOnlyMetric.gameObject.SetActive(false);
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

    private void ApplyCombatPackPosition()
    {
        if (kineticLoadout == null || kineticLoadout.GridBoard == null)
            return;

        RectTransform board = kineticLoadout.GridBoard;
        Vector2 target = new(combatPackRightShift, 0f);
        if ((board.anchoredPosition - target).sqrMagnitude > 0.001f)
            board.anchoredPosition = target;
    }

    private void ApplyDetailPresentation()
    {
        if (detailController == null || detailController.Root == null)
            return;

        CanvasGroup detailGroup = detailController.Group;
        if (rightPanelFocused)
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
        back.color = new Color(0.025f, 0.028f, 0.045f, 0.94f);
        back.raycastTarget = false;

        Outline outline = chatPanel.GetComponent<Outline>();
        if (outline == null)
            outline = chatPanel.gameObject.AddComponent<Outline>();
        outline.enabled = true;
        outline.effectColor = new Color(0.94f, 0.95f, 0.97f, 0.58f);
        outline.effectDistance = new Vector2(3f, -3f);

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
        chatHeader.color = new Color(0.10f, 0.88f, 0.95f, 1f);
        chatHeader.raycastTarget = false;

        RectTransform divider = chatPanel.Find("Divider") as RectTransform;
        if (divider == null)
        {
            divider = CreateRect(chatPanel, "Divider", Vector2.zero);
            Image dividerImage = divider.gameObject.AddComponent<Image>();
            dividerImage.color = new Color(0.94f, 0.95f, 0.97f, 0.20f);
            dividerImage.raycastTarget = false;
        }
        SetAnchors(divider, new Vector2(0.045f, 0.735f), new Vector2(0.955f, 0.745f));

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
        chatBody.color = new Color(0.94f, 0.95f, 0.97f, 0.98f);

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
        chatPanel.localRotation = Quaternion.Euler(0f, 0f, -0.8f);
        chatPanel.localScale = Vector3.one;
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

    private int CurrentViewers => runProgress != null ? Mathf.Max(0, runProgress.Viewers) : 0;

    private void SeedChatIfNeeded()
    {
        if (chatBody == null)
            return;

        if (chatHistory.Count > 0)
        {
            RefreshChatBody();
            return;
        }

        int viewers = CurrentViewers;
        int seedCount = viewers >= 1000 ? 5 : viewers >= 100 ? 4 : viewers >= 10 ? 3 : 2;
        seedCount = Mathf.Min(seedCount, Mathf.Clamp(maxChatLines, 3, 7));

        for (int i = 0; i < seedCount; i++)
            AppendNextChatLine();

        ScheduleNextChat();
    }

    private void UpdateChatFeed()
    {
        if (chatBody == null)
            return;

        SeedChatIfNeeded();
        if (Time.unscaledTime < nextChatAt)
            return;

        AppendNextChatLine();
        ScheduleNextChat();
    }

    private void ScheduleNextChat()
    {
        nextChatAt = Time.unscaledTime + CalculateViewerPacedInterval(CurrentViewers);
    }

    private float CalculateViewerPacedInterval(int viewers)
    {
        viewers = Mathf.Max(0, viewers);
        float slow = Mathf.Max(20f, zeroViewerChatInterval);
        float fast = Mathf.Clamp(highViewerChatInterval, 0.35f, 4f);
        float baseInterval;

        if (viewers <= 0)
            baseInterval = slow;
        else if (viewers < 10)
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

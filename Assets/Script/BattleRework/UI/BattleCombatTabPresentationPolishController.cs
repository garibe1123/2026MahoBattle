using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Combat TAB의 최종 Presentation 보정만 담당합니다.
///
/// 범위:
/// - PACK 선택 장비 Detail을 Grid 쪽으로 당기고, FAN MISSION보다 위에 렌더합니다.
/// - FAN MISSION 아래에는 배경 없는 Text-only 채팅을 표시합니다.
/// - 채팅 발생 속도는 RunProgressSystem.Viewers에 비례합니다.
/// - 우측 상단에 현재 Viewers / Likes를 Text-only로 표시합니다.
///
/// 비소유 범위:
/// - 장비 선택/장착/Swap/Reward 데이터는 변경하지 않습니다.
/// - FanMission 진행/판정 데이터는 변경하지 않습니다.
/// - Viewers / Likes 값을 생성하거나 보정하지 않고 기존 RunProgressSystem 값을 읽기만 합니다.
/// - Reward 화면의 Detail 배치는 기존 BattleUnifiedInventoryInspectController가 계속 소유합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33520)]
public sealed class BattleCombatTabPresentationPolishController : MonoBehaviour
{
    private const float DetailAnchorX = 0.72f;
    private const float DetailAnchorY = 0.52f;
    private const float DetailScale = 0.92f;
    private const int DetailSortingPadding = 15;

    private const float ChatHeight = 172f;
    private const float ChatGap = 14f;
    private const float ZeroViewerChatInterval = 45f;
    private const float HighViewerChatInterval = 0.70f;
    private const int MaxChatLines = 5;

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

    // 일부러 여러 언어를 섞습니다. 현재 프로젝트에는 전용 다국어 Font Asset이 없으므로
    // Runtime에서는 OS의 다국어 Font를 우선 사용하고 없으면 Unity 기본 Font로 fallback 합니다.
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
    [SerializeField] private BattleBroadcastDashboardController dashboardController;

    [Header("LIVE CHAT — VIEWER PACED")]
    [Tooltip("시청자가 0명일 때 채팅 한 줄이 새로 생기는 기본 간격입니다. 거의 멈춘 상태를 의도합니다.")]
    [SerializeField, Range(20f, 90f)] private float zeroViewerChatInterval = ZeroViewerChatInterval;
    [Tooltip("시청자가 매우 많을 때 도달하는 최소 채팅 간격입니다.")]
    [SerializeField, Range(0.35f, 4f)] private float highViewerChatInterval = HighViewerChatInterval;
    [Tooltip("한 번에 화면에 유지할 최근 채팅 줄 수입니다.")]
    [SerializeField, Range(3, 7)] private int maxChatLines = MaxChatLines;

    private RectTransform dashboardRoot;
    private RectTransform missionPanel;
    private Canvas dashboardCanvas;

    private RectTransform chatPanel;
    private CanvasGroup chatGroup;
    private Text chatBody;

    private RectTransform legacyMetricBar;
    private Text broadcastMetricText;

    private Canvas detailCanvas;
    private int originalDetailSortingOrder;
    private bool detailSortingCaptured;

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
        if (Object.FindFirstObjectByType<BattleCombatTabPresentationPolishController>(FindObjectsInactive.Include) != null)
            return;

        BattleBroadcastDashboardController dashboard =
            Object.FindFirstObjectByType<BattleBroadcastDashboardController>(FindObjectsInactive.Include);
        BattleKineticLoadoutUI loadout =
            Object.FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
        BattleRunManager run = Object.FindFirstObjectByType<BattleRunManager>(FindObjectsInactive.Include);

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
    }

    private void OnDisable()
    {
        RestoreDetailSorting();
        SetChatVisible(false);

        // 이 보정 컴포넌트 자체가 꺼지는 경우에는 원래 Dashboard Metric Bar를 복구합니다.
        if (legacyMetricBar != null)
            legacyMetricBar.gameObject.SetActive(true);
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextResolveAt)
        {
            nextResolveAt = Time.unscaledTime + 0.25f;
            ResolveReferences(false);
            ResolveDashboardUi();
        }

        bool open = IsCombatTabOpen();
        SetChatVisible(open);
        if (!open)
            return;

        EnsureMetricText();
        EnsureChatPanel();
        SetChatVisible(true);
        UpdateBroadcastMetricText();
        UpdateChatFeed();
    }

    private void LateUpdate()
    {
        if (!IsCombatTabOpen())
        {
            RestoreDetailSorting();
            return;
        }

        ResolveDashboardUi();
        EnsureMetricText();
        EnsureChatPanel();
        ApplyDetailPresentation();
        ApplyMetricLayout();
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
        if (force || dashboardController == null)
            dashboardController = FindFirstObjectByType<BattleBroadcastDashboardController>(FindObjectsInactive.Include);
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

        if (dashboardCanvas == null)
            dashboardCanvas = dashboardRoot.GetComponent<Canvas>();
        if (missionPanel == null)
            missionPanel = dashboardRoot.Find("MissionPanel") as RectTransform;

        legacyMetricBar = dashboardRoot.Find("BroadcastMetricBar") as RectTransform;
        if (legacyMetricBar != null && legacyMetricBar.gameObject.activeSelf)
            legacyMetricBar.gameObject.SetActive(false);

        if (broadcastMetricText == null)
        {
            RectTransform metricRoot = dashboardRoot.Find("BroadcastMetricText") as RectTransform;
            if (metricRoot != null)
                broadcastMetricText = metricRoot.GetComponent<Text>();
        }

        if (chatPanel == null)
        {
            chatPanel = dashboardRoot.Find("LiveChatPanel") as RectTransform;
            if (chatPanel != null)
                ResolveExistingChatPanel();
        }
    }

    private void ApplyDetailPresentation()
    {
        if (detailController == null || detailController.Root == null)
            return;

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
        detailSortingCaptured = false;
        detailCanvas = null;
    }

    private void EnsureMetricText()
    {
        if (broadcastMetricText != null || dashboardRoot == null)
            return;

        RectTransform metricRect = CreateRect(dashboardRoot, "BroadcastMetricText", new Vector2(480f, 42f));
        metricRect.anchorMin = metricRect.anchorMax = Vector2.one;
        metricRect.pivot = Vector2.one;
        metricRect.SetAsLastSibling();

        broadcastMetricText = metricRect.gameObject.AddComponent<Text>();
        broadcastMetricText.font = ResolveMultilingualFont();
        broadcastMetricText.fontSize = 16;
        broadcastMetricText.fontStyle = FontStyle.Bold;
        broadcastMetricText.alignment = TextAnchor.UpperRight;
        broadcastMetricText.color = new Color(0.94f, 0.95f, 0.97f, 1f);
        broadcastMetricText.raycastTarget = false;
        broadcastMetricText.supportRichText = true;

        Shadow shadow = metricRect.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.78f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        ApplyMetricLayout();
        UpdateBroadcastMetricText();
    }

    private void ApplyMetricLayout()
    {
        if (broadcastMetricText == null)
            return;

        RectTransform rect = broadcastMetricText.rectTransform;
        rect.anchorMin = rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.sizeDelta = new Vector2(480f, 42f);
        rect.anchoredPosition = new Vector2(-38f, -28f);
        rect.localRotation = Quaternion.identity;
    }

    private void UpdateBroadcastMetricText()
    {
        if (broadcastMetricText == null)
            return;

        int viewers = CurrentViewers;
        int likes = runProgress != null ? Mathf.Max(0, runProgress.Likes) : 0;
        string value =
            $"<color=#19E0F2>VIEWERS</color>  {viewers:N0}    //    <color=#FFD11A>LIKES</color>  {likes:N0}";

        if (broadcastMetricText.text != value)
            broadcastMetricText.text = value;
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
            chatPanel.localRotation = Quaternion.identity;
            chatPanel.SetAsLastSibling();

            chatGroup = chatPanel.gameObject.AddComponent<CanvasGroup>();
            chatGroup.alpha = 0f;
            chatGroup.blocksRaycasts = false;
            chatGroup.interactable = false;

            chatBody = CreateText(chatPanel, string.Empty, 14, FontStyle.Normal, TextAnchor.UpperLeft,
                new Color(0.94f, 0.95f, 0.97f, 0.96f), "Body", ResolveMultilingualFont());
            Stretch(chatBody.rectTransform);
            chatBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            chatBody.verticalOverflow = VerticalWrapMode.Truncate;
            chatBody.lineSpacing = 1.15f;
            chatBody.supportRichText = true;

            Shadow shadow = chatBody.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.82f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }
        else
        {
            ResolveExistingChatPanel();
        }

        StripLegacyChatChrome();
        SeedChatIfNeeded();
        ApplyChatLayout();
    }

    private void ResolveExistingChatPanel()
    {
        if (chatPanel == null)
            return;

        chatGroup = chatPanel.GetComponent<CanvasGroup>();
        if (chatGroup == null)
        {
            chatGroup = chatPanel.gameObject.AddComponent<CanvasGroup>();
            chatGroup.blocksRaycasts = false;
            chatGroup.interactable = false;
        }

        chatBody = chatPanel.Find("Body")?.GetComponent<Text>();
        if (chatBody != null)
        {
            chatBody.font = ResolveMultilingualFont();
            chatBody.fontSize = 14;
            chatBody.alignment = TextAnchor.UpperLeft;
            Stretch(chatBody.rectTransform);
        }
    }

    private void StripLegacyChatChrome()
    {
        if (chatPanel == null)
            return;

        Image rootImage = chatPanel.GetComponent<Image>();
        if (rootImage != null)
            rootImage.enabled = false;

        Outline rootOutline = chatPanel.GetComponent<Outline>();
        if (rootOutline != null)
            rootOutline.enabled = false;

        DisableChild("Header");
        DisableChild("Live");
        DisableChild("LiveAccent");

        if (chatBody != null)
        {
            Stretch(chatBody.rectTransform);
            chatBody.color = new Color(0.94f, 0.95f, 0.97f, 0.96f);
        }
    }

    private void DisableChild(string childName)
    {
        Transform child = chatPanel != null ? chatPanel.Find(childName) : null;
        if (child != null && child.gameObject.activeSelf)
            child.gameObject.SetActive(false);
    }

    private void ApplyChatLayout()
    {
        if (chatPanel == null || missionPanel == null)
            return;

        float width = Mathf.Max(420f, missionPanel.sizeDelta.x);
        chatPanel.sizeDelta = new Vector2(width, ChatHeight);
        chatPanel.anchorMin = chatPanel.anchorMax = Vector2.one;
        chatPanel.pivot = Vector2.one;
        chatPanel.anchoredPosition = new Vector2(
            missionPanel.anchoredPosition.x,
            missionPanel.anchoredPosition.y - missionPanel.sizeDelta.y - ChatGap);
        chatPanel.localRotation = Quaternion.identity;
    }

    private void SetChatVisible(bool visible)
    {
        if (chatGroup == null)
            return;

        chatGroup.alpha = visible ? 0.96f : 0f;
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

        // 0명이어도 완전히 빈 UI처럼 보이지 않도록 오래된 한 줄만 남겨 둡니다.
        // 이후 새 채팅은 0명 기준 약 45초 간격이라 사실상 정지에 가깝습니다.
        int viewers = CurrentViewers;
        int seedCount = viewers >= 1000 ? 4 : viewers >= 100 ? 3 : viewers >= 10 ? 2 : 1;
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
        {
            baseInterval = slow;
        }
        else if (viewers < 10)
        {
            baseInterval = Mathf.Lerp(slow * 0.78f, 24f, viewers / 10f);
        }
        else if (viewers < 100)
        {
            baseInterval = Mathf.Lerp(24f, 10f, (viewers - 10f) / 90f);
        }
        else if (viewers < 1000)
        {
            baseInterval = Mathf.Lerp(10f, 2.5f, (viewers - 100f) / 900f);
        }
        else
        {
            baseInterval = Mathf.Lerp(2.5f, fast, Mathf.Clamp01((viewers - 1000f) / 9000f));
        }

        // Gameplay Random state를 건드리지 않도록 sequence/viewer 값으로 작은 deterministic jitter만 만듭니다.
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

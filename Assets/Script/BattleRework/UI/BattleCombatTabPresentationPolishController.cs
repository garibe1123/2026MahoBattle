using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Combat TAB의 최종 Presentation 보정만 담당합니다.
///
/// 범위:
/// - PACK 선택 장비 Detail을 Grid 쪽으로 당기고, FAN MISSION보다 위에 렌더합니다.
/// - FAN MISSION 바로 아래에 닉네임 + 코멘트가 누적되는 LIVE CHAT을 표시합니다.
///
/// 비소유 범위:
/// - 장비 선택/장착/Swap/Reward 데이터는 변경하지 않습니다.
/// - FanMission 진행/판정 데이터는 변경하지 않습니다.
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

    private const float ChatHeight = 184f;
    private const float ChatGap = 14f;
    private const float ChatRotation = -1.1f;
    private const float ChatInterval = 2.2f;
    private const int MaxChatLines = 5;

    private static readonly string[] ChatNames =
    {
        "MahoFan_17",
        "clipHunter",
        "RoomWatcher",
        "zeroHPclub",
        "pack_rat",
        "livewire",
        "missionEnjoyer",
        "noReload"
    };

    private static readonly string[] FallbackComments =
    {
        "that PACK is getting scary",
        "keep that one, trust",
        "clean slot swap",
        "fan mission when??",
        "that build actually works",
        "don't throw that item away",
        "clip that!",
        "one more room!",
        "the grid link is online",
        "okay this run is cooking"
    };

    [Header("AUTO REFERENCES")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;
    [SerializeField] private BattleEquipmentDetailPanelController detailController;
    [SerializeField] private BattleBroadcastDashboardController dashboardController;

    [Header("LIVE CHAT")]
    [Tooltip("채팅 새 줄이 들어오는 간격입니다. 실제 게임 시간 정지와 무관하게 UI 시간으로 갱신됩니다.")]
    [SerializeField, Range(0.8f, 8f)] private float chatInterval = ChatInterval;
    [Tooltip("한 번에 화면에 유지할 최근 채팅 줄 수입니다.")]
    [SerializeField, Range(3, 7)] private int maxChatLines = MaxChatLines;

    private RectTransform dashboardRoot;
    private RectTransform missionPanel;
    private Canvas dashboardCanvas;

    private RectTransform chatPanel;
    private CanvasGroup chatGroup;
    private Text chatHeader;
    private Text chatBody;

    private Canvas detailCanvas;
    private int originalDetailSortingOrder;
    private bool detailSortingCaptured;

    private readonly List<string> chatHistory = new();
    private int chatSequence;
    private float nextChatAt;
    private float nextResolveAt;

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
        BattleBroadcastDashboardController dashboard =
            Object.FindFirstObjectByType<BattleBroadcastDashboardController>(FindObjectsInactive.Include);
        if (dashboard == null)
            return;

        if (dashboard.GetComponent<BattleCombatTabPresentationPolishController>() == null)
            dashboard.gameObject.AddComponent<BattleCombatTabPresentationPolishController>();
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

        EnsureChatPanel();
        UpdateChatHeader();
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
        EnsureChatPanel();
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

        dashboardCanvas ??= dashboardRoot.GetComponent<Canvas>();
        missionPanel ??= dashboardRoot.Find("MissionPanel") as RectTransform;

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

    private void EnsureChatPanel()
    {
        if (chatPanel != null || dashboardRoot == null)
            return;

        chatPanel = CreateRect(dashboardRoot, "LiveChatPanel", new Vector2(540f, ChatHeight));
        chatPanel.anchorMin = chatPanel.anchorMax = Vector2.one;
        chatPanel.pivot = Vector2.one;
        chatPanel.localRotation = Quaternion.Euler(0f, 0f, ChatRotation);
        chatPanel.SetAsLastSibling();

        Image back = chatPanel.gameObject.AddComponent<Image>();
        back.color = new Color(0.025f, 0.028f, 0.045f, 0.955f);
        back.raycastTarget = false;

        Outline outline = chatPanel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.94f, 0.95f, 0.97f, 0.62f);
        outline.effectDistance = new Vector2(3f, -3f);

        chatGroup = chatPanel.gameObject.AddComponent<CanvasGroup>();
        chatGroup.alpha = 0f;
        chatGroup.blocksRaycasts = false;
        chatGroup.interactable = false;

        RectTransform accent = CreateRect(chatPanel, "LiveAccent", Vector2.zero);
        accent.anchorMin = new Vector2(0f, 0f);
        accent.anchorMax = new Vector2(0.012f, 1f);
        accent.offsetMin = Vector2.zero;
        accent.offsetMax = Vector2.zero;
        Image accentImage = accent.gameObject.AddComponent<Image>();
        accentImage.color = new Color(0.10f, 0.88f, 0.95f, 1f);
        accentImage.raycastTarget = false;

        chatHeader = CreateText(chatPanel, "LIVE CHAT // FAN FEED", 15, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(0.94f, 0.95f, 0.97f, 1f), "Header");
        SetAnchors(chatHeader.rectTransform, new Vector2(0.055f, 0.76f), new Vector2(0.94f, 0.95f));

        Text live = CreateText(chatPanel, "● LIVE", 10, FontStyle.Bold, TextAnchor.MiddleRight,
            new Color(1f, 0.18f, 0.52f, 1f), "Live");
        SetAnchors(live.rectTransform, new Vector2(0.76f, 0.78f), new Vector2(0.94f, 0.95f));

        chatBody = CreateText(chatPanel, string.Empty, 13, FontStyle.Normal, TextAnchor.UpperLeft,
            new Color(0.94f, 0.95f, 0.97f, 0.96f), "Body");
        SetAnchors(chatBody.rectTransform, new Vector2(0.055f, 0.08f), new Vector2(0.95f, 0.76f));
        chatBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        chatBody.verticalOverflow = VerticalWrapMode.Truncate;
        chatBody.lineSpacing = 1.12f;
        chatBody.supportRichText = true;

        SeedChatIfNeeded();
        ApplyChatLayout();
    }

    private void ResolveExistingChatPanel()
    {
        if (chatPanel == null)
            return;

        chatGroup = chatPanel.GetComponent<CanvasGroup>();
        chatHeader = chatPanel.Find("Header")?.GetComponent<Text>();
        chatBody = chatPanel.Find("Body")?.GetComponent<Text>();
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
        chatPanel.localRotation = Quaternion.Euler(0f, 0f, ChatRotation);
    }

    private void SetChatVisible(bool visible)
    {
        if (chatGroup == null)
            return;

        chatGroup.alpha = visible ? 0.96f : 0f;
        chatGroup.blocksRaycasts = false;
        chatGroup.interactable = false;
    }

    private void UpdateChatHeader()
    {
        if (chatHeader == null)
            return;

        ShootingThemeSO theme = runManager != null ? runManager.ShootingTheme : null;
        string suffix = theme != null && !string.IsNullOrWhiteSpace(theme.displayName)
            ? theme.displayName.Trim().ToUpperInvariant()
            : "FAN FEED";
        string value = $"LIVE CHAT // {suffix}";
        if (chatHeader.text != value)
            chatHeader.text = value;
    }

    private void SeedChatIfNeeded()
    {
        if (chatHistory.Count > 0)
        {
            RefreshChatBody();
            return;
        }

        int seedCount = Mathf.Min(4, Mathf.Clamp(maxChatLines, 3, 7));
        for (int i = 0; i < seedCount; i++)
            AppendNextChatLine();

        nextChatAt = Time.unscaledTime + Mathf.Max(0.8f, chatInterval);
    }

    private void UpdateChatFeed()
    {
        if (chatBody == null)
            return;

        SeedChatIfNeeded();
        if (Time.unscaledTime < nextChatAt)
            return;

        AppendNextChatLine();
        nextChatAt = Time.unscaledTime + Mathf.Max(0.8f, chatInterval);
    }

    private void AppendNextChatLine()
    {
        int nameIndex = chatSequence % ChatNames.Length;
        int commentIndex = (chatSequence * 3 + 1) % FallbackComments.Length;
        string nickname = ChatNames[nameIndex];
        string comment = FallbackComments[commentIndex];
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
}

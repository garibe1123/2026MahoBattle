using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Reward 및 Script Selection이 사용할 최소 staging shell만 생성합니다.
///
/// 현재 ownership:
/// - Combat HP/ST/PACK: BattleKineticLoadoutUI
/// - Broadcast Metrics/Mission/Chat: BattleBroadcastDashboardController
/// - Reward 카드/결정/포기: BattleRewardCardActionController
/// - Reward business state: BattleRewardFlow
/// - PACK/Grid/Detail: BattleUnifiedInventoryInspectController
/// - Script Selection 카드/대본: BattleSpatialMapController
/// - TV/Carrier/Presenter/Show Camera: BattleShowWorldSetController
///
/// 이 클래스는 Reward 선택 상태, Reward Drag/Drop, Show Camera, Spotlight를 소유하지 않습니다.
/// PrizeSelectionScreen은 Reward TV staging shell입니다.
/// Script Selection은 독립 World-Space Canvas로 분리되어 있으며,
/// 기존 MapSelectionScreen / MapSelectionContent 오브젝트명은 씬/런타임 호환을 위해서만 유지합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleHUD : MonoBehaviour
{
    private static BattleHUD instance;
    internal static event Action RewardCardsRebuilt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        RewardCardsRebuilt = null;
    }

    [Header("Combat HUD")]
    [SerializeField] private Color accentColor = new(1f, 0.18f, 0.58f, 1f);
    [SerializeField] private Color goldColor = new(1f, 0.80f, 0.25f, 1f);

    [Header("Shared UI Surface")]
    [Tooltip("패널 배경에 공유할 UI Material입니다. 블러 등 셰이더 설정은 이 재질에서 지정합니다.")]
    [SerializeField] private Material uiMaterial;
    [SerializeField, Range(0f, 1f)] private float combatUiOpacity = 0.72f;
    [SerializeField, Range(0f, 1f)] private float interactionUiOpacity = 1f;
    private bool inventoryInteractionActive;

    internal static Material SharedUiMaterial => instance != null ? instance.uiMaterial : null;
    internal static float UiSurfaceOpacity => instance == null ? 1f :
        (instance.inventoryInteractionActive || instance.runManager == null || instance.runManager.State != BattleRunState.Combat
            ? instance.interactionUiOpacity : instance.combatUiOpacity);

    internal static void SetInventoryInteraction(bool active)
    {
        if (instance != null)
            instance.inventoryInteractionActive = active;
    }

    [Header("Show Content Shell")]
    [SerializeField] private Vector2 showScreenSize = new(1120f, 560f);

    [Header("Presenter Metadata")]
    [Tooltip("실제 화면 표시는 BattleShowWorldSetController의 SpriteRenderer가 담당합니다. 이 값은 Presenter 소스 메타데이터로만 사용됩니다.")]
    [SerializeField] private Sprite presenterSprite;
    [SerializeField] private Color presenterColor = Color.white;
    [SerializeField] private Vector2 presenterAnchor = new(0.88f, 0.49f);
    [SerializeField] private Vector2 presenterSize = new(370f, 600f);
    [SerializeField] private Vector2 presenterOffset = Vector2.zero;
    [SerializeField] private bool presenterFlipX;

    private BattleRunManager runManager;

    private Canvas canvas;
    private CanvasGroup canvasGroup;

    private GameObject showStagingRoot;
    private RectTransform rewardScreenRect;
    private RectTransform rewardCardRoot;
    private RectTransform scriptSelectionStagingScreenRect;
    private RectTransform scriptSelectionRoot;
    private Image presenterMetadataImage;

    private BattleRunManager subscribedRunManager;
    private readonly List<RewardCardSlot> rewardCardSlots = new();

    private sealed class RewardCardSlot
    {
        public RectTransform rect;
        public Image background;
        public Button button;
        public Image icon;
        public Text rarity;
        public Text name;
        public Text type;
        public Text action;
    }

    /// <summary>BattleSpatialMapController가 대본 카드를 생성하는 Script Selection Root입니다.</summary>
    public RectTransform ScriptSelectionRoot => scriptSelectionRoot;

    /// <summary>Script Selection 콘텐츠가 최초 생성되는 레거시 staging shell입니다.</summary>
    public RectTransform ScriptSelectionStagingScreenRoot => scriptSelectionStagingScreenRect;

    [Obsolete("Use ScriptSelectionRoot. Map route presentation has been replaced by Script Selection.")]
    public RectTransform MapSelectionRoot => ScriptSelectionRoot;

    /// <summary>Show 계층을 직접 탐색하지 않아도 되는 명시적 Reward Screen 참조입니다.</summary>
    public RectTransform RewardScreenRoot => rewardScreenRect;

    [Obsolete("Use ScriptSelectionStagingScreenRoot. This is only a legacy staging shell.")]
    public RectTransform MapScreenRoot => ScriptSelectionStagingScreenRoot;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            return;
        }

        instance = this;
        accentColor = BattleUiPanelDecoration.Mint;
        goldColor = BattleUiPanelDecoration.Yellow;
        EnsureCanvas();
    }

    private void OnEnable()
    {
        ResolveRunManager();
        SubscribeRunEvents();
        RefreshShellState();
        if (runManager != null && runManager.State == BattleRunState.Reward)
            RebuildRewardCards();
    }

    private void OnDisable()
    {
        UnsubscribeRunEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeRunEvents();
        if (instance == this)
            instance = null;
    }

    private void ResolveRunManager()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
    }

    private void SubscribeRunEvents()
    {
        if (subscribedRunManager == runManager)
            return;

        if (subscribedRunManager != null)
        {
            subscribedRunManager.StateChanged -= HandleRunStateChanged;
            subscribedRunManager.RewardSelectionRequested -= HandleRewardSelectionRequested;
        }

        subscribedRunManager = runManager;
        if (subscribedRunManager != null)
        {
            subscribedRunManager.StateChanged += HandleRunStateChanged;
            subscribedRunManager.RewardSelectionRequested += HandleRewardSelectionRequested;
        }
    }

    private void UnsubscribeRunEvents()
    {
        if (subscribedRunManager != null)
        {
            subscribedRunManager.StateChanged -= HandleRunStateChanged;
            subscribedRunManager.RewardSelectionRequested -= HandleRewardSelectionRequested;
        }

        subscribedRunManager = null;
    }

    private void HandleRunStateChanged(BattleRunState _)
    {
        RefreshShellState();
    }

    private void HandleRewardSelectionRequested(IReadOnlyList<BattleEquipmentSO> _)
    {
        RebuildRewardCards();
    }

    private void RefreshShellState()
    {
        bool active = runManager != null && runManager.RunActive;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = active ? 1f : 0f;
            canvasGroup.blocksRaycasts = active;
            canvasGroup.interactable = active;
        }
    }

    private void EnsureCanvas()
    {
        if (canvas != null)
            return;

        EnsureEventSystem();

        GameObject canvasObject = new("BattleBroadcastHUDCanvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        canvasGroup = canvasObject.AddComponent<CanvasGroup>();

        BuildShowContentShell();
        RefreshShellState();
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject go = new("BattleUIEventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    private void BuildShowContentShell()
    {
        showStagingRoot = new GameObject("RewardQuizShow");
        showStagingRoot.transform.SetParent(canvas.transform, false);
        RectTransform stagingRect = showStagingRoot.AddComponent<RectTransform>();
        Stretch(stagingRect);

        // Presenter는 실제 화면 Image가 아닙니다. WorldSet이 Sprite source/flip 정보를 읽는 metadata bridge입니다.
        GameObject presenter = new("Presenter");
        presenter.transform.SetParent(showStagingRoot.transform, false);
        RectTransform presenterRect = presenter.AddComponent<RectTransform>();
        presenterRect.anchorMin = presenterRect.anchorMax = presenterAnchor;
        presenterRect.sizeDelta = presenterSize;
        presenterRect.anchoredPosition = presenterOffset;
        presenterMetadataImage = presenter.AddComponent<Image>();
        presenterMetadataImage.raycastTarget = false;
        presenterMetadataImage.enabled = false;
        ApplyPresenterMetadata();

        rewardScreenRect = BuildRewardScreenShell(showStagingRoot.transform);
        scriptSelectionStagingScreenRect =
            BuildScriptSelectionStagingShell(
                showStagingRoot.transform);

        rewardScreenRect.gameObject.SetActive(false);
        scriptSelectionStagingScreenRect.gameObject.SetActive(false);
    }

    private RectTransform BuildRewardScreenShell(Transform parent)
    {
        GameObject screen = CreatePanel(parent, "PrizeSelectionScreen", showScreenSize, new Color(0.025f, 0.020f, 0.055f, 0.985f));
        RectTransform screenRect = screen.GetComponent<RectTransform>();
        screenRect.anchorMin = screenRect.anchorMax = new Vector2(0.5f, 0.5f);
        screenRect.pivot = new Vector2(0.5f, 0.5f);
        screenRect.anchoredPosition = Vector2.zero;

        GameObject inner = CreatePanel(
            screen.transform,
            "ScreenInner",
            showScreenSize - new Vector2(34f, 34f),
            new Color(0.055f, 0.045f, 0.105f, 1f));
        RectTransform innerRect = inner.GetComponent<RectTransform>();
        innerRect.anchorMin = innerRect.anchorMax = new Vector2(0.5f, 0.5f);
        innerRect.anchoredPosition = Vector2.zero;

        inner.GetComponent<Image>().color = BattleUiPanelDecoration.Surface;
        BattleUiPanelDecoration.Attach(screenRect, BattleUiPanelDecoration.Mint);
        BattleUiPanelDecoration.Attach(innerRect, BattleUiPanelDecoration.Mint);
        Text title = CreateText(inner.transform, "CHOOSE YOUR PRIZE", 30, FontStyle.Bold, TextAnchor.MiddleLeft, BattleUiPanelDecoration.Paper);
        SetAnchors(title.rectTransform, new Vector2(0.05f, 0.865f), new Vector2(0.72f, 0.96f));

        Text subtitle = CreateText(inner.transform, "SELECT  •  CONFIRM", 11, FontStyle.Bold, TextAnchor.MiddleLeft, BattleUiPanelDecoration.Muted);
        SetAnchors(subtitle.rectTransform, new Vector2(0.05f, 0.805f), new Vector2(0.72f, 0.86f));

        Text live = CreateText(inner.transform, "[ON LIVE]", 14, FontStyle.Bold, TextAnchor.MiddleRight, new Color(1f, 0.10f, 0.12f, 1f));
        SetAnchors(live.rectTransform, new Vector2(0.77f, 0.87f), new Vector2(0.95f, 0.95f));

        GameObject cardRoot = new("PrizeChoices");
        cardRoot.transform.SetParent(inner.transform, false);
        rewardCardRoot = cardRoot.AddComponent<RectTransform>();
        SetAnchors(rewardCardRoot, new Vector2(0.055f, 0.20f), new Vector2(0.945f, 0.79f));

        Text focusName = CreateText(inner.transform, "SELECT A PRIZE", 16, FontStyle.Bold, TextAnchor.MiddleLeft, goldColor);
        SetAnchors(focusName.rectTransform, new Vector2(0.055f, 0.13f), new Vector2(0.38f, 0.19f));

        Text focusStats = CreateText(inner.transform, "Hover to inspect. Click to select.", 11, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.78f, 0.82f, 0.90f, 1f));
        SetAnchors(focusStats.rectTransform, new Vector2(0.38f, 0.12f), new Vector2(0.945f, 0.19f));

        // RewardCardActionController가 이 Root를 compact skip button으로 재사용합니다.
        GameObject notice = CreatePanel(
            inner.transform,
            "PlacementNotice",
            new Vector2(304f, 54f),
            new Color(0.02f, 0.02f, 0.025f, 0.98f));
        RectTransform noticeRect = notice.GetComponent<RectTransform>();
        noticeRect.anchorMin = noticeRect.anchorMax = new Vector2(0.5f, 0.055f);
        noticeRect.anchoredPosition = Vector2.zero;
        Text noticeText = CreateText(notice.transform, "아이템 획득 포기하기", 12, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(noticeText.rectTransform);

        return screenRect;
    }

    private RectTransform BuildScriptSelectionStagingShell(Transform parent)
    {
        GameObject screen = CreatePanel(parent, "MapSelectionScreen", showScreenSize, new Color(0.025f, 0.020f, 0.055f, 0.985f));
        RectTransform screenRect = screen.GetComponent<RectTransform>();
        screenRect.anchorMin = screenRect.anchorMax = new Vector2(0.5f, 0.5f);
        screenRect.pivot = new Vector2(0.5f, 0.5f);
        screenRect.anchoredPosition = Vector2.zero;

        GameObject inner = CreatePanel(
            screen.transform,
            "ScreenInner",
            showScreenSize - new Vector2(34f, 34f),
            new Color(0.012f, 0.021f, 0.048f, 0.97f));
        RectTransform innerRect = inner.GetComponent<RectTransform>();
        innerRect.anchorMin = innerRect.anchorMax = new Vector2(0.5f, 0.5f);
        innerRect.anchoredPosition = Vector2.zero;

        // Object names stay legacy-compatible because existing scene/runtime
        // lookups may still reference them. Ownership is Script Selection only.
        GameObject scriptRoot = new("MapSelectionContent");
        scriptRoot.transform.SetParent(inner.transform, false);
        scriptSelectionRoot = scriptRoot.AddComponent<RectTransform>();
        SetAnchors(
            scriptSelectionRoot,
            new Vector2(0.025f, 0.055f),
            new Vector2(0.975f, 0.94f));

        return screenRect;
    }

    private void RebuildRewardCards()
    {
        if (rewardCardRoot == null || runManager == null)
            return;

        EventSystem events = EventSystem.current;
        if (events != null && events.currentSelectedGameObject != null &&
            events.currentSelectedGameObject.transform.IsChildOf(rewardCardRoot))
            events.SetSelectedGameObject(null);

        for (int i = 0; i < rewardCardSlots.Count; i++)
        {
            RewardCardSlot slot = rewardCardSlots[i];
            slot.rect.gameObject.SetActive(false);
            slot.button.onClick.RemoveAllListeners();
            slot.icon.sprite = null;
            slot.rarity.text = slot.name.text = slot.type.text = slot.action.text = string.Empty;
        }

        int count = runManager.CurrentRewardChoices.Count;
        float width = count > 0 ? Mathf.Min(275f, 835f / count) : 275f;
        const float height = 245f;
        const float spacing = 22f;
        float total = count * width + (count - 1) * spacing;
        float start = -total * 0.5f + width * 0.5f;

        for (int i = 0; i < count; i++)
        {
            BattleEquipmentSO reward = runManager.CurrentRewardChoices[i];
            if (reward == null)
                continue;

            while (rewardCardSlots.Count <= i)
                rewardCardSlots.Add(CreateRewardCardSlot(rewardCardSlots.Count));
            RewardCardSlot slot = rewardCardSlots[i];
            RectTransform cardRect = slot.rect;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(width, height);
            cardRect.anchoredPosition = new Vector2(start + i * (width + spacing), 0f);
            cardRect.localScale = Vector3.one;
            cardRect.localRotation = Quaternion.identity;
            slot.background.color = new Color(0.055f, 0.057f, 0.066f, 0.995f);
            slot.button.interactable = true;
            slot.icon.rectTransform.anchorMin = slot.icon.rectTransform.anchorMax = new Vector2(0.5f, 0.67f);
            slot.icon.rectTransform.anchoredPosition = Vector2.zero;
            slot.icon.rectTransform.sizeDelta = new Vector2(105f, 105f);
            slot.icon.rectTransform.localScale = Vector3.one;
            slot.icon.rectTransform.localRotation = Quaternion.identity;
            slot.icon.color = Color.white;
            slot.icon.sprite = reward.icon;
            slot.icon.enabled = reward.icon != null;
            slot.rarity.fontSize = 9;
            slot.name.fontSize = 14;
            slot.type.fontSize = 8;
            slot.action.fontSize = 8;
            slot.rarity.text = reward.rarity.ToString().ToUpperInvariant();
            slot.name.text = reward.GetDisplayName();
            slot.type.text = reward.type.ToString().ToUpperInvariant();
            slot.action.text = "CLICK TO SELECT";
            ResetRewardCardText(slot.rarity, new Vector2(0.08f, 0.39f), new Vector2(0.92f, 0.47f), goldColor);
            ResetRewardCardText(slot.name, new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.39f), Color.white);
            ResetRewardCardText(slot.type, new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.22f), new Color(0.66f, 0.84f, 0.93f, 1f));
            ResetRewardCardText(slot.action, new Vector2(0.08f, 0.025f), new Vector2(0.92f, 0.12f), accentColor);
            cardRect.gameObject.SetActive(true);
        }
        RewardCardsRebuilt?.Invoke();
        BattleUiStyleController.RegisterRoot(rewardCardRoot);
    }

    private RewardCardSlot CreateRewardCardSlot(int index)
    {
        GameObject card = CreatePanel(rewardCardRoot, $"Prize_{index}", new Vector2(275f, 245f),
            new Color(0.055f, 0.057f, 0.066f, 0.995f));
        RewardCardSlot slot = new()
        {
            rect = card.GetComponent<RectTransform>(),
            background = card.GetComponent<Image>(),
            button = card.AddComponent<Button>(),
            icon = CreateImage(card.transform, "PrizeIcon", new Vector2(105f, 105f)),
            rarity = CreateText(card.transform, string.Empty, 9, FontStyle.Bold, TextAnchor.MiddleCenter, goldColor),
            name = CreateText(card.transform, string.Empty, 14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white),
            type = CreateText(card.transform, string.Empty, 8, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.66f, 0.84f, 0.93f, 1f)),
            action = CreateText(card.transform, string.Empty, 8, FontStyle.Bold, TextAnchor.MiddleCenter, accentColor)
        };
        slot.button.targetGraphic = slot.background;
        card.AddComponent<RewardPrizeIndex>().Configure(index);
        card.SetActive(false);
        return slot;
    }

    private static void ResetRewardCardText(Text text, Vector2 min, Vector2 max, Color color)
    {
        text.gameObject.SetActive(true);
        SetAnchors(text.rectTransform, min, max);
        text.rectTransform.localScale = Vector3.one;
        text.rectTransform.localRotation = Quaternion.identity;
        text.color = color;
    }

    // ---------------------------------------------------------------------
    // Explicit metadata / compatibility API
    // ---------------------------------------------------------------------

    public void SetPresenterSprite(Sprite sprite)
    {
        presenterSprite = sprite;
        ApplyPresenterMetadata();
    }

    public void SetPresenterColor(Color color)
    {
        presenterColor = color;
        ApplyPresenterMetadata();
    }

    public void SetPresenterLayout(Vector2 anchor, Vector2 size, Vector2 offset, bool flipX = false)
    {
        presenterAnchor = anchor;
        presenterSize = size;
        presenterOffset = offset;
        presenterFlipX = flipX;
        ApplyPresenterMetadata();
    }

    private void ApplyPresenterMetadata()
    {
        if (presenterMetadataImage == null)
            return;

        RectTransform rect = presenterMetadataImage.rectTransform;
        rect.anchorMin = rect.anchorMax = presenterAnchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = presenterSize;
        rect.anchoredPosition = presenterOffset;
        rect.localScale = new Vector3(presenterFlipX ? -1f : 1f, 1f, 1f);
        presenterMetadataImage.sprite = presenterSprite;
        presenterMetadataImage.color = presenterColor;
        presenterMetadataImage.enabled = false;
    }

    private static string Shorten(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value ?? string.Empty;
        return value.Substring(0, Mathf.Max(1, max - 1)) + "…";
    }

    private static GameObject CreatePanel(Transform parent, string name, Vector2 size, Color color)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        Image image = go.AddComponent<Image>();
        image.sprite = BattleHudSpriteCache.RoundedPanel;
        image.type = Image.Type.Sliced;
        image.color = BattleUiPanelDecoration.Surface;
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.10f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.enabled = false;
        return go;
    }

    private static Image CreateImage(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        Image image = go.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private static Text CreateText(
        Transform parent,
        string content,
        int size,
        FontStyle style,
        TextAnchor alignment,
        Color color)
    {
        GameObject go = new("Text");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        Text text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        BattleUiStyleController.RegisterGraphic(text);
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

/// <summary>Reward 카드의 안정적인 choice index만 보관하는 경량 marker입니다.</summary>
internal sealed class RewardPrizeIndex : MonoBehaviour
{
    [SerializeField] private int rewardIndex;
    public int RewardIndex => rewardIndex;
    public void Configure(int index) => rewardIndex = index;
}

internal static class BattleHudSpriteCache
{
    private static Sprite roundedPanel;
    private static Sprite defaultSprite;
    private static Sprite floorSpotlight;

    public static Sprite RoundedPanel => roundedPanel != null ? roundedPanel : roundedPanel = CreateRoundedPanel();
    public static Sprite DefaultSprite => defaultSprite != null ? defaultSprite : defaultSprite = CreateDefaultSprite();
    public static Sprite FloorSpotlight => floorSpotlight != null ? floorSpotlight : floorSpotlight = CreateFloorSpotlight();

    private static Sprite CreateRoundedPanel()
    {
        const int pixels = 32;
        const float radius = 7f;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (int y = 0; y < pixels; y++)
        {
            for (int x = 0; x < pixels; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x - 15.5f) - (15.5f - radius), 0f);
                float dy = Mathf.Max(Mathf.Abs(y - 15.5f) - (15.5f - radius), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(radius + 0.5f - d);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, pixels, pixels),
            new Vector2(0.5f, 0.5f),
            pixels,
            0,
            SpriteMeshType.FullRect,
            new Vector4(8f, 8f, 8f, 8f));
        sprite.name = "RuntimeHudRoundedPanel";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static Sprite CreateDefaultSprite()
    {
        const int pixels = 16;
        Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (int y = 0; y < pixels; y++)
            for (int x = 0; x < pixels; x++)
                texture.SetPixel(x, y, Color.white);

        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, pixels, pixels),
            new Vector2(0.5f, 0.5f),
            pixels,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "RuntimeSpriteDefault";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static Sprite CreateFloorSpotlight()
    {
        const int width = 256;
        const int height = 128;
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Vector2 center = new((width - 1) * 0.5f, (height - 1) * 0.5f);
        float invRadiusX = 1f / Mathf.Max(1f, center.x);
        float invRadiusY = 1f / Mathf.Max(1f, center.y);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (x - center.x) * invRadiusX;
                float ny = (y - center.y) * invRadiusY;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);
                float core = 1f - Mathf.SmoothStep(0.08f, 0.70f, radius);
                float feather = 1f - Mathf.SmoothStep(0.56f, 1f, radius);
                float alpha = Mathf.Clamp01(core * 0.52f + feather * 0.48f);
                alpha *= Mathf.Clamp01(1f - Mathf.Pow(radius, 3.2f));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            128f,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "RuntimeRewardBirdEyeFloorSpotlight";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}

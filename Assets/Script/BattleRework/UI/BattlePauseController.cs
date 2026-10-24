using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// BattleInputRouter 기반 전투 Pause / Resume.
/// ESC/게임패드 Cancel의 소유권은 Router modal stack이 결정합니다.
/// Time.timeScale은 직접 수정하지 않고 BattleTimeScaleController에 요청합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33000)]
public sealed class BattlePauseController : MonoBehaviour, IInputModal
{
    private const int PauseCanvasOrder = 1600;
    private static readonly Vector2 PanelSize = new(560f, 540f);
    private static readonly Vector2 MenuButtonSize = new(400f, 64f);
    private const float MenuButtonSpacing = 80f;

    public static bool IsPaused { get; private set; }

    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleTimeScaleController timeScaleController;
    [SerializeField] private BattleInputRouter inputRouter;

    [Header("Pause Theme")]
    [SerializeField] private Color inkColor = new(0.035f, 0.030f, 0.055f, 0.995f);
    [SerializeField] private Color paperColor = new(0.94f, 0.90f, 0.76f, 1f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField] private Color accentCyan = new(0.15f, 0.88f, 0.92f, 1f);

    private Canvas pauseCanvas;
    private GameObject pauseRoot;
    private GameObject menuRoot;
    private GameObject settingsRoot;
    private Button resumeButton;
    private Button styleButton;
    private Text styleLabel;
    private Slider volumeSlider;
    private BattleShowPresentationManager presentation;
    private bool inputSubscribed;

    private void Awake()
    {
        ResolveReferences();
        EnsurePauseUi();
    }

    private void Start()
    {
        ResolveReferences();
        SubscribeInput();
        RegisterPauseStyle();
    }

    private void RegisterPauseStyle()
    {
        if (pauseRoot == null) return;
        BattleUiStyleController.RegisterRoot(pauseRoot.transform);
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsurePauseUi();
        SubscribeInput();

        if (IsPaused)
        {
            timeScaleController?.Request(BattleTimeScaleController.Owner.Pause, 0f);
            inputRouter?.PushModal(this);
        }
    }

    private void Update()
    {
        if (IsPaused && !CanRemainPaused())
            Resume();
    }

    private void OnDisable()
    {
        UnsubscribeInput();
        if (IsPaused)
            Resume();
    }

    private void OnDestroy()
    {
        UnsubscribeInput();
        if (IsPaused)
            Resume();
    }

    private void HandlePauseRequested()
    {
        if (IsPaused)
        {
            Resume();
            return;
        }

        if (CanPause())
            Pause();
    }

    public void RequestClose()
    {
        if (settingsRoot != null && settingsRoot.activeSelf)
        {
            ShowMenu();
            return;
        }
        if (IsPaused)
            Resume();
    }

    public void Pause()
    {
        if (IsPaused || !CanPause())
            return;

        ResolveReferences();
        if (timeScaleController == null)
            return;

        IsPaused = true;
        timeScaleController.Request(BattleTimeScaleController.Owner.Pause, 0f);
        inputRouter?.PushModal(this);

        if (pauseRoot != null)
            pauseRoot.SetActive(true);
        ShowMenu();
    }

    public void Resume()
    {
        if (!IsPaused)
            return;

        IsPaused = false;
        inputRouter?.PopModal(this);
        timeScaleController?.Release(BattleTimeScaleController.Owner.Pause);

        if (settingsRoot != null) settingsRoot.SetActive(false);
        if (menuRoot != null) menuRoot.SetActive(true);
        if (pauseRoot != null)
            pauseRoot.SetActive(false);
    }

    private bool CanPause()
    {
        return runManager != null && runManager.RunActive;
    }

    private bool CanRemainPaused()
    {
        return runManager != null && runManager.RunActive;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (timeScaleController == null)
            timeScaleController = BattleTimeScaleController.ResolveOrCreate(this);
        if (inputRouter == null && Application.isPlaying)
            inputRouter = BattleInputRouter.ResolveOrCreate(this);

        if (IsPaused)
            timeScaleController?.Request(BattleTimeScaleController.Owner.Pause, 0f);
    }

    private void SubscribeInput()
    {
        if (inputSubscribed || inputRouter == null)
            return;

        inputRouter.PauseRequested += HandlePauseRequested;
        inputSubscribed = true;
    }

    private void UnsubscribeInput()
    {
        if (!inputSubscribed)
            return;

        if (inputRouter != null)
            inputRouter.PauseRequested -= HandlePauseRequested;
        inputSubscribed = false;
    }

    private void EnsurePauseUi()
    {
        if (pauseCanvas != null)
            return;

        EnsureEventSystem();

        GameObject canvasObject = new("BattlePauseCanvas");
        canvasObject.transform.SetParent(transform, false);
        pauseCanvas = canvasObject.AddComponent<Canvas>();
        pauseCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        pauseCanvas.overrideSorting = true;
        pauseCanvas.sortingOrder = PauseCanvasOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        pauseRoot = new GameObject("PauseOverlay");
        pauseRoot.transform.SetParent(canvasObject.transform, false);
        RectTransform rootRect = pauseRoot.AddComponent<RectTransform>();
        Stretch(rootRect);

        Image dim = pauseRoot.AddComponent<Image>();
        dim.color = new Color(0.008f, 0.006f, 0.015f, 0.76f);
        dim.raycastTarget = true;

        RectTransform panel = CreateRect(pauseRoot.transform, "PausePanel", PanelSize);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = inkColor;
        BattleUiPanelDecoration.Round(panelImage);
        Color panelColor = panelImage.color;
        panelColor.a = inkColor.a;
        panelImage.color = panelColor;
        BattleUiPanelDecoration.Attach(panel, BattleUiPanelDecoration.Mint, false, false);

        Text title = CreateText(panel, "PAUSE", 48, FontStyle.Bold, TextAnchor.MiddleCenter, paperColor);
        title.name = "Title";
        SetAnchors(title.rectTransform, new Vector2(0.10f, 0.80f), new Vector2(0.90f, 0.96f));

        RectTransform menu = CreateRect(panel, "PauseMenu", Vector2.zero);
        Stretch(menu);
        menuRoot = menu.gameObject;
        resumeButton = CreateMenuButton(menu, "ResumeButton", "계속하기 / ESC", 0, Resume);
        CreateMenuButton(menu, "SettingsButton", "설정", 1, ShowSettings);
        CreateMenuButton(menu, "AbandonButton", "전투 포기", 2, AbandonRun);
        CreateMenuButton(menu, "QuitButton", "게임 종료", 3, QuitGame);

        RectTransform settings = CreateRect(panel, "PauseSettings", Vector2.zero);
        Stretch(settings);
        settingsRoot = settings.gameObject;
        Text volumeLabel = CreateText(settings, "전체 음량", 20, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
        SetAnchors(volumeLabel.rectTransform, new Vector2(0.15f, 0.65f), new Vector2(0.85f, 0.74f));

        RectTransform volumeRect = CreateRect(settings, "Volume", new Vector2(MenuButtonSize.x, 32f));
        volumeRect.anchoredPosition = new Vector2(0f, 44f);
        Image track = volumeRect.gameObject.AddComponent<Image>();
        track.color = inkColor;
        track.canvasRenderer.SetAlpha(0.25f);
        volumeSlider = volumeRect.gameObject.AddComponent<Slider>();
        RectTransform handle = CreateRect(volumeRect, "Handle", new Vector2(24f, 0f));
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = accentCyan;
        volumeSlider.handleRect = handle;
        volumeSlider.targetGraphic = handleImage;
        volumeSlider.direction = Slider.Direction.LeftToRight;
        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 1f;
        volumeSlider.SetValueWithoutNotify(AudioListener.volume);
        volumeSlider.onValueChanged.AddListener(value => AudioListener.volume = value);

        styleButton = CreateMenuButton(settings, "StyleButton", string.Empty, 2, CycleStyle);
        styleLabel = styleButton.GetComponentInChildren<Text>();
        CreateMenuButton(settings, "BackButton", "뒤로 / ESC", 3, ShowMenu);
        settingsRoot.SetActive(false);

        pauseRoot.SetActive(false);
        RegisterPauseStyle();
    }

    private Button CreateMenuButton(RectTransform parent, string name, string label, int row,
        UnityEngine.Events.UnityAction action)
    {
        RectTransform buttonRect = CreateRect(parent, name, MenuButtonSize);
        buttonRect.anchoredPosition = new Vector2(0f, 100f - row * MenuButtonSpacing);
        Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.color = accentYellow;
        BattleUiPanelDecoration.Round(buttonImage);
        Color buttonColor = buttonImage.color;
        buttonColor.a = accentYellow.a;
        buttonImage.color = buttonColor;
        BattleUiPanelDecoration.Attach(buttonRect, BattleUiPanelDecoration.Mint, false, false);

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        button.onClick.AddListener(action);

        Text text = CreateText(buttonRect, label, 22, FontStyle.Bold, TextAnchor.MiddleCenter, paperColor);
        Stretch(text.rectTransform);
        return button;
    }

    private void ShowMenu()
    {
        settingsRoot.SetActive(false);
        menuRoot.SetActive(true);
        EventSystem.current?.SetSelectedGameObject(resumeButton.gameObject);
    }

    private void ShowSettings()
    {
        menuRoot.SetActive(false);
        settingsRoot.SetActive(true);
        volumeSlider.SetValueWithoutNotify(AudioListener.volume);
        presentation = BattleShowPresentationManager.Instance;
        styleButton.interactable = presentation != null;
        UpdateStyleLabel();
        EventSystem.current?.SetSelectedGameObject(volumeSlider.gameObject);
    }

    private void CycleStyle()
    {
        if (presentation == null) return;
        BattleUiStyle next = presentation.UiStyle switch
        {
            BattleUiStyle.pixelStyleUi => BattleUiStyle.comicStyleUi,
            BattleUiStyle.comicStyleUi => BattleUiStyle.VectorStyleUi,
            BattleUiStyle.VectorStyleUi => BattleUiStyle.MahoShojo,
            BattleUiStyle.MahoShojo => BattleUiStyle.Stylish,
            BattleUiStyle.Stylish => BattleUiStyle.Birthday,
            _ => BattleUiStyle.pixelStyleUi
        };
        presentation.SetUiStyle(next);
        UpdateStyleLabel();
    }

    private void UpdateStyleLabel()
    {
        styleLabel.text = presentation != null ? $"UI 스타일: {presentation.UiStyle}" : "UI 스타일";
    }

    private void AbandonRun()
    {
        if (!IsPaused) return;
        Resume();
        runManager?.QuitRun();
    }

    private void QuitGame()
    {
        Resume();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject go = new("BattlePauseEventSystem");
            DontDestroyOnLoad(go);
            eventSystem = go.AddComponent<EventSystem>();
        }

        StandaloneInputModule legacy = eventSystem.GetComponent<StandaloneInputModule>();
        if (legacy != null)
            Destroy(legacy);

        InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (module == null)
        {
            module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static Text CreateText(Transform parent, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color)
    {
        RectTransform rect = CreateRect(parent, "Text", Vector2.zero);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = BattleFeedbackFont.Resolve();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void SetImage(RectTransform rect, Color color, bool raycast)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
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

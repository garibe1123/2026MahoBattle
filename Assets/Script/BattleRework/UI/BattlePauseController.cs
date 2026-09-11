using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ESC 기반 전투 Pause / Resume.
///
/// Phase 8부터 Time.timeScale을 직접 쓰지 않습니다.
/// BattleTimeScaleController에 Pause=0 요청을 등록하고 해제하며,
/// Combat/Reward Inventory의 기존 slow-motion 요청은 그대로 유지됩니다.
/// 따라서 Inventory 중 Pause -> Resume 시 자동으로 원래 Inventory slow-motion으로 복귀합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33000)]
public sealed class BattlePauseController : MonoBehaviour
{
    private const int PauseCanvasOrder = 1600;

    public static bool IsPaused { get; private set; }

    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleTimeScaleController timeScaleController;

    [Header("Pause Theme")]
    [SerializeField] private Color inkColor = new(0.035f, 0.030f, 0.055f, 0.995f);
    [SerializeField] private Color paperColor = new(0.94f, 0.90f, 0.76f, 1f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField] private Color accentCyan = new(0.15f, 0.88f, 0.92f, 1f);

    private Canvas pauseCanvas;
    private GameObject pauseRoot;

    private void Awake()
    {
        ResolveReferences();
        EnsurePauseUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsurePauseUi();

        if (IsPaused)
            timeScaleController?.Request(BattleTimeScaleController.Owner.Pause, 0f);
    }

    private void Update()
    {
        ResolveReferences();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (IsPaused)
                Resume();
            else if (CanPause())
                Pause();
        }

        if (IsPaused && !CanRemainPaused())
            Resume();
    }

    private void OnDisable()
    {
        if (IsPaused)
            Resume();
    }

    private void OnDestroy()
    {
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

        if (pauseRoot != null)
            pauseRoot.SetActive(true);
    }

    public void Resume()
    {
        if (!IsPaused)
            return;

        IsPaused = false;
        timeScaleController?.Release(BattleTimeScaleController.Owner.Pause);

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

        if (IsPaused)
            timeScaleController?.Request(BattleTimeScaleController.Owner.Pause, 0f);
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

        RectTransform wedge = CreateRect(pauseRoot.transform, "PauseYellowWedge", new Vector2(780f, 1320f));
        wedge.anchorMin = wedge.anchorMax = new Vector2(0f, 0.5f);
        wedge.anchoredPosition = new Vector2(-330f, 0f);
        wedge.localRotation = Quaternion.Euler(0f, 0f, -15f);
        SetImage(wedge, new Color(accentYellow.r, accentYellow.g, accentYellow.b, 0.92f), false);

        RectTransform panel = CreateRect(pauseRoot.transform, "PausePanel", new Vector2(560f, 250f));
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = new Vector2(140f, 0f);
        panel.localRotation = Quaternion.Euler(0f, 0f, -3f);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = inkColor;
        Outline panelOutline = panel.gameObject.AddComponent<Outline>();
        panelOutline.effectColor = accentPink;
        panelOutline.effectDistance = new Vector2(7f, -7f);

        RectTransform slash = CreateRect(panel, "PausePinkSlash", new Vector2(22f, 286f));
        slash.anchorMin = slash.anchorMax = new Vector2(0f, 0.5f);
        slash.anchoredPosition = new Vector2(18f, 0f);
        slash.localRotation = Quaternion.Euler(0f, 0f, 12f);
        SetImage(slash, accentPink, false);

        Text title = CreateText(panel, "PAUSE", 56, FontStyle.Bold, TextAnchor.MiddleLeft, paperColor);
        SetAnchors(title.rectTransform, new Vector2(0.10f, 0.58f), new Vector2(0.92f, 0.94f));
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);

        Text sub = CreateText(panel, "COMBAT FEED // HOLD", 11, FontStyle.Bold, TextAnchor.MiddleLeft, accentCyan);
        SetAnchors(sub.rectTransform, new Vector2(0.11f, 0.46f), new Vector2(0.92f, 0.60f));

        RectTransform buttonRect = CreateRect(panel, "ResumeButton", new Vector2(248f, 58f));
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(48f, 46f);
        buttonRect.localRotation = Quaternion.Euler(0f, 0f, 1.5f);
        Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.color = accentYellow;

        Button resumeButton = buttonRect.gameObject.AddComponent<Button>();
        resumeButton.targetGraphic = buttonImage;
        resumeButton.onClick.AddListener(Resume);

        Text resume = CreateText(buttonRect, "RESUME  /  ESC", 18, FontStyle.Bold, TextAnchor.MiddleCenter, inkColor);
        Stretch(resume.rectTransform);

        pauseRoot.SetActive(false);
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject go = new("BattlePauseEventSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
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
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

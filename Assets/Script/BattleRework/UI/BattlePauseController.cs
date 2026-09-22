using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// ESC / Gamepad Start 기반 전투 Pause.
///
/// 입력은 즉시 Modal로 잠그되, 시간은 unscaledDeltaTime 기반으로 자연스럽게
/// Running -> Pausing -> Paused -> Resuming 상태를 거쳐 0까지 감속 / 원래 속도로 복귀합니다.
///
/// Time.timeScale은 직접 수정하지 않고 BattleTimeScaleController의 Pause owner만 사용합니다.
/// TAB / Reward / HitStop 등 다른 TimeScale owner가 남아 있으면 Resume 후 그 요청값으로 복귀합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33000)]
public sealed class BattlePauseController : MonoBehaviour, IInputModal
{
    private enum PauseState
    {
        Running,
        Pausing,
        Paused,
        Resuming
    }

    private const int PauseCanvasOrder = 1800;

    public static bool IsPaused { get; private set; }

    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleTimeScaleController timeScaleController;
    [SerializeField] private BattleInputRouter inputRouter;

    [Header("Pause Motion")]
    [SerializeField, Range(0.12f, 0.80f)] private float pauseInDuration = 0.34f;
    [SerializeField, Range(0.12f, 0.90f)] private float resumeDuration = 0.44f;
    [SerializeField, Range(0.30f, 0.90f)] private float pauseDimAlpha = 0.64f;
    [SerializeField, Range(8f, 60f)] private float signalFrequency = 34f;

    [Header("Pause Theme")]
    [SerializeField] private Color inkColor = new(0.010f, 0.014f, 0.020f, 1f);
    [SerializeField] private Color paperColor = new(0.93f, 0.95f, 0.97f, 1f);
    [SerializeField] private Color accentYellow = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color accentPink = new(1f, 0.18f, 0.52f, 1f);
    [SerializeField] private Color accentCyan = new(0.15f, 0.88f, 0.92f, 1f);

    private Canvas pauseCanvas;
    private GameObject pauseRoot;
    private CanvasGroup pauseGroup;
    private Image dimImage;

    private RectTransform pauseGlyphRoot;
    private RectTransform pauseBarLeft;
    private RectTransform pauseBarRight;
    private Image pauseBarLeftImage;
    private Image pauseBarRightImage;

    private RectTransform signalBand;
    private Image signalBandImage;
    private RectTransform signalEcho;
    private Image signalEchoImage;

    private Text statusText;
    private Text timeFlowText;
    private Text resumeText;

    private PauseState state = PauseState.Running;
    private bool inputSubscribed;
    private float transitionElapsed;
    private float transitionStartScale = 1f;
    private float transitionStartVisual;
    private float pauseRequestedScale = 1f;
    private float visualAmount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsPaused = false;
    }

    private void Awake()
    {
        ResolveReferences();
        EnsurePauseUi();
        ForceRunningVisuals();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsurePauseUi();
        SubscribeInput();
    }

    private void Update()
    {
        ResolveReferences();
        SubscribeInput();

        if (state != PauseState.Running && !CanRemainPaused())
        {
            ForceResumeImmediate();
            return;
        }

        UpdatePauseTransition();
        UpdatePauseVisuals();
    }

    private void OnDisable()
    {
        UnsubscribeInput();
        ForceResumeImmediate();
    }

    private void OnDestroy()
    {
        UnsubscribeInput();
        ForceResumeImmediate();
    }

    private void HandlePauseRequested()
    {
        switch (state)
        {
            case PauseState.Running:
            case PauseState.Resuming:
                Pause();
                break;

            case PauseState.Pausing:
            case PauseState.Paused:
                Resume();
                break;
        }
    }

    public void RequestClose()
    {
        if (state != PauseState.Running)
            Resume();
    }

    public void Pause()
    {
        if (state == PauseState.Paused || state == PauseState.Pausing || !CanPause())
            return;

        ResolveReferences();
        if (timeScaleController == null)
            return;

        IsPaused = true;

        transitionStartScale = Mathf.Clamp01(timeScaleController.AppliedScale);
        transitionStartVisual = visualAmount;
        pauseRequestedScale = transitionStartScale;
        transitionElapsed = 0f;
        state = PauseState.Pausing;

        timeScaleController.Request(
            BattleTimeScaleController.Owner.Pause,
            pauseRequestedScale);

        inputRouter?.PushModal(this);

        if (pauseRoot != null)
            pauseRoot.SetActive(true);
    }

    public void Resume()
    {
        if (state == PauseState.Running || state == PauseState.Resuming)
            return;

        ResolveReferences();
        if (timeScaleController == null)
        {
            ForceResumeImmediate();
            return;
        }

        transitionStartScale = Mathf.Clamp01(timeScaleController.AppliedScale);
        transitionStartVisual = visualAmount;
        pauseRequestedScale = transitionStartScale;
        transitionElapsed = 0f;
        state = PauseState.Resuming;

        // Keep the modal and IsPaused active until time has actually recovered.
        timeScaleController.Request(
            BattleTimeScaleController.Owner.Pause,
            pauseRequestedScale);
    }

    private void UpdatePauseTransition()
    {
        if (timeScaleController == null)
            return;

        switch (state)
        {
            case PauseState.Running:
                visualAmount = Mathf.MoveTowards(
                    visualAmount,
                    0f,
                    Time.unscaledDeltaTime * 4f);
                return;

            case PauseState.Pausing:
            {
                transitionElapsed += Time.unscaledDeltaTime;
                float duration = Mathf.Max(0.01f, pauseInDuration);
                float t = Mathf.Clamp01(transitionElapsed / duration);
                float eased = EaseOutCubic(t);

                pauseRequestedScale = Mathf.Lerp(
                    transitionStartScale,
                    0f,
                    eased);
                timeScaleController.Request(
                    BattleTimeScaleController.Owner.Pause,
                    pauseRequestedScale);

                visualAmount = Mathf.Lerp(
                    transitionStartVisual,
                    1f,
                    eased);

                if (t >= 1f)
                {
                    pauseRequestedScale = 0f;
                    timeScaleController.Request(
                        BattleTimeScaleController.Owner.Pause,
                        0f);
                    state = PauseState.Paused;
                    visualAmount = 1f;
                }
                return;
            }

            case PauseState.Paused:
                pauseRequestedScale = 0f;
                timeScaleController.Request(
                    BattleTimeScaleController.Owner.Pause,
                    0f);
                visualAmount = 1f;
                return;

            case PauseState.Resuming:
            {
                transitionElapsed += Time.unscaledDeltaTime;
                float duration = Mathf.Max(0.01f, resumeDuration);
                float t = Mathf.Clamp01(transitionElapsed / duration);
                float eased = EaseInOutCubic(t);

                // Requesting toward 1 does not override slower owners.
                // BattleTimeScaleController resolves the minimum request.
                pauseRequestedScale = Mathf.Lerp(
                    transitionStartScale,
                    1f,
                    eased);
                timeScaleController.Request(
                    BattleTimeScaleController.Owner.Pause,
                    pauseRequestedScale);

                visualAmount = Mathf.Lerp(
                    transitionStartVisual,
                    0f,
                    eased);

                if (t >= 1f)
                    CompleteResume();
                return;
            }
        }
    }

    private void CompleteResume()
    {
        timeScaleController?.Release(BattleTimeScaleController.Owner.Pause);
        inputRouter?.PopModal(this);

        state = PauseState.Running;
        IsPaused = false;
        pauseRequestedScale = 1f;
        transitionElapsed = 0f;
        visualAmount = 0f;

        if (pauseRoot != null)
            pauseRoot.SetActive(false);
    }

    private void ForceResumeImmediate()
    {
        timeScaleController?.Release(BattleTimeScaleController.Owner.Pause);
        inputRouter?.PopModal(this);

        state = PauseState.Running;
        IsPaused = false;
        transitionElapsed = 0f;
        transitionStartScale = 1f;
        transitionStartVisual = 0f;
        pauseRequestedScale = 1f;
        visualAmount = 0f;

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

        if (state != PauseState.Running && timeScaleController != null)
        {
            timeScaleController.Request(
                BattleTimeScaleController.Owner.Pause,
                pauseRequestedScale);
        }
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

        pauseGroup = pauseRoot.AddComponent<CanvasGroup>();
        pauseGroup.alpha = 0f;
        pauseGroup.blocksRaycasts = true;
        pauseGroup.interactable = true;

        dimImage = pauseRoot.AddComponent<Image>();
        dimImage.color = new Color(
            inkColor.r,
            inkColor.g,
            inkColor.b,
            0f);
        dimImage.raycastTarget = true;

        BuildSignalBand(rootRect);
        BuildPauseGlyph(rootRect);

        statusText = CreateText(
            pauseRoot.transform,
            "TIME // HOLD",
            14,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            accentCyan);
        SetAnchors(
            statusText.rectTransform,
            new Vector2(0.37f, 0.30f),
            new Vector2(0.63f, 0.35f));

        timeFlowText = CreateText(
            pauseRoot.transform,
            "TIME FLOW 1.00x",
            11,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            paperColor);
        SetAnchors(
            timeFlowText.rectTransform,
            new Vector2(0.37f, 0.255f),
            new Vector2(0.63f, 0.30f));

        resumeText = CreateText(
            pauseRoot.transform,
            "ESC / START  //  RESUME",
            12,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            accentYellow);
        SetAnchors(
            resumeText.rectTransform,
            new Vector2(0.36f, 0.18f),
            new Vector2(0.64f, 0.235f));

        pauseRoot.SetActive(false);
    }

    private void BuildSignalBand(RectTransform root)
    {
        signalBand = CreateRect(
            root,
            "PauseSignalBand",
            new Vector2(0f, 3f));
        signalBand.anchorMin = new Vector2(0f, 0.5f);
        signalBand.anchorMax = new Vector2(1f, 0.5f);
        signalBand.pivot = new Vector2(0.5f, 0.5f);
        signalBand.anchoredPosition = Vector2.zero;
        signalBand.sizeDelta = new Vector2(0f, 3f);

        signalBandImage = signalBand.gameObject.AddComponent<Image>();
        signalBandImage.color = new Color(
            accentCyan.r,
            accentCyan.g,
            accentCyan.b,
            0f);
        signalBandImage.raycastTarget = false;

        signalEcho = CreateRect(
            root,
            "PauseSignalEcho",
            new Vector2(0f, 1f));
        signalEcho.anchorMin = new Vector2(0f, 0.5f);
        signalEcho.anchorMax = new Vector2(1f, 0.5f);
        signalEcho.pivot = new Vector2(0.5f, 0.5f);
        signalEcho.anchoredPosition = new Vector2(0f, -7f);
        signalEcho.sizeDelta = new Vector2(0f, 1f);

        signalEchoImage = signalEcho.gameObject.AddComponent<Image>();
        signalEchoImage.color = new Color(
            accentPink.r,
            accentPink.g,
            accentPink.b,
            0f);
        signalEchoImage.raycastTarget = false;
    }

    private void BuildPauseGlyph(RectTransform root)
    {
        pauseGlyphRoot = CreateRect(
            root,
            "PauseGlyph",
            new Vector2(176f, 176f));
        pauseGlyphRoot.anchorMin =
            pauseGlyphRoot.anchorMax =
                new Vector2(0.5f, 0.5f);
        pauseGlyphRoot.pivot = new Vector2(0.5f, 0.5f);
        pauseGlyphRoot.anchoredPosition = Vector2.zero;

        pauseBarLeft = CreateRect(
            pauseGlyphRoot,
            "PauseBarLeft",
            new Vector2(25f, 88f));
        pauseBarLeft.anchorMin =
            pauseBarLeft.anchorMax =
                new Vector2(0.5f, 0.5f);
        pauseBarLeft.anchoredPosition = new Vector2(-24f, 0f);
        pauseBarLeftImage = pauseBarLeft.gameObject.AddComponent<Image>();
        pauseBarLeftImage.color = accentCyan;
        pauseBarLeftImage.raycastTarget = false;

        pauseBarRight = CreateRect(
            pauseGlyphRoot,
            "PauseBarRight",
            new Vector2(25f, 88f));
        pauseBarRight.anchorMin =
            pauseBarRight.anchorMax =
                new Vector2(0.5f, 0.5f);
        pauseBarRight.anchoredPosition = new Vector2(24f, 0f);
        pauseBarRightImage = pauseBarRight.gameObject.AddComponent<Image>();
        pauseBarRightImage.color = accentCyan;
        pauseBarRightImage.raycastTarget = false;

        BuildGlyphCorner(pauseGlyphRoot, "TopLeft", new Vector2(-66f, 66f), 1f, -1f);
        BuildGlyphCorner(pauseGlyphRoot, "TopRight", new Vector2(66f, 66f), -1f, -1f);
        BuildGlyphCorner(pauseGlyphRoot, "BottomLeft", new Vector2(-66f, -66f), 1f, 1f);
        BuildGlyphCorner(pauseGlyphRoot, "BottomRight", new Vector2(66f, -66f), -1f, 1f);
    }

    private void BuildGlyphCorner(
        Transform parent,
        string name,
        Vector2 position,
        float horizontalDirection,
        float verticalDirection)
    {
        RectTransform root = CreateRect(parent, name, new Vector2(34f, 34f));
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = position;

        RectTransform horizontal = CreateRect(
            root,
            "H",
            new Vector2(34f, 4f));
        horizontal.anchorMin =
            horizontal.anchorMax =
                new Vector2(
                    horizontalDirection > 0f ? 1f : 0f,
                    verticalDirection > 0f ? 0f : 1f);
        horizontal.pivot = new Vector2(
            horizontalDirection > 0f ? 1f : 0f,
            0.5f);
        horizontal.anchoredPosition = Vector2.zero;
        SetImage(horizontal, paperColor, false);

        RectTransform vertical = CreateRect(
            root,
            "V",
            new Vector2(4f, 34f));
        vertical.anchorMin =
            vertical.anchorMax =
                new Vector2(
                    horizontalDirection > 0f ? 1f : 0f,
                    verticalDirection > 0f ? 0f : 1f);
        vertical.pivot = new Vector2(
            0.5f,
            verticalDirection > 0f ? 0f : 1f);
        vertical.anchoredPosition = Vector2.zero;
        SetImage(vertical, paperColor, false);
    }

    private void UpdatePauseVisuals()
    {
        if (pauseRoot == null || !pauseRoot.activeSelf)
            return;

        float amount = Mathf.Clamp01(visualAmount);
        float visual = EaseOutCubic(amount);
        float signal = 0.5f + 0.5f * Mathf.Sin(
            Time.unscaledTime * Mathf.Max(8f, signalFrequency));

        if (pauseGroup != null)
            pauseGroup.alpha = visual;

        if (dimImage != null)
        {
            Color dim = dimImage.color;
            dim.a = pauseDimAlpha * visual * Mathf.Lerp(0.94f, 1f, signal);
            dimImage.color = dim;
        }

        if (pauseGlyphRoot != null)
        {
            float pulse = 1f + signal * 0.008f * visual;
            pauseGlyphRoot.localScale = Vector3.one *
                                        Mathf.Lerp(0.82f, pulse, visual);
            pauseGlyphRoot.localRotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.Lerp(-5f, 0f, visual));
        }

        if (pauseBarLeft != null && pauseBarRight != null)
        {
            float spacing = Mathf.Lerp(13f, 24f, visual);
            pauseBarLeft.anchoredPosition = new Vector2(-spacing, 0f);
            pauseBarRight.anchoredPosition = new Vector2(spacing, 0f);
        }

        if (pauseBarLeftImage != null)
        {
            Color c = accentCyan;
            c.a = Mathf.Lerp(0.25f, 1f, visual);
            pauseBarLeftImage.color = c;
        }

        if (pauseBarRightImage != null)
        {
            Color c = accentCyan;
            c.a = Mathf.Lerp(0.25f, 1f, visual);
            pauseBarRightImage.color = c;
        }

        if (signalBand != null)
        {
            signalBand.anchoredPosition = new Vector2(
                0f,
                Mathf.Sin(Time.unscaledTime * signalFrequency * 0.63f) *
                2.2f *
                visual);
            signalBand.sizeDelta = new Vector2(
                0f,
                Mathf.Lerp(1f, 3f + signal * 2f, visual));
        }

        if (signalBandImage != null)
        {
            Color c = accentCyan;
            c.a = visual * Mathf.Lerp(0.18f, 0.52f, signal);
            signalBandImage.color = c;
        }

        if (signalEcho != null)
        {
            signalEcho.anchoredPosition = new Vector2(
                0f,
                -7f + signal * 3f);
        }

        if (signalEchoImage != null)
        {
            Color c = accentPink;
            c.a = visual * Mathf.Lerp(0.05f, 0.20f, 1f - signal);
            signalEchoImage.color = c;
        }

        float shownScale = timeScaleController != null
            ? timeScaleController.AppliedScale
            : Time.timeScale;

        if (timeFlowText != null)
            timeFlowText.text = $"TIME FLOW {shownScale:0.00}x";

        if (statusText != null)
        {
            statusText.text = state switch
            {
                PauseState.Pausing => "TIME // BRAKING",
                PauseState.Paused => "TIME // HOLD",
                PauseState.Resuming => "TIME // RECOVERING",
                _ => "TIME // RUNNING"
            };
        }

        if (resumeText != null)
        {
            Color c = accentYellow;
            c.a = Mathf.Lerp(0.38f, 1f, visual);
            resumeText.color = c;
        }
    }

    private void ForceRunningVisuals()
    {
        visualAmount = 0f;
        if (pauseGroup != null)
            pauseGroup.alpha = 0f;
        if (pauseRoot != null)
            pauseRoot.SetActive(false);
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    private static float EaseInOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
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

        StandaloneInputModule legacy =
            eventSystem.GetComponent<StandaloneInputModule>();
        if (legacy != null)
            Destroy(legacy);

        InputSystemUIInputModule module =
            eventSystem.GetComponent<InputSystemUIInputModule>();
        if (module == null)
        {
            module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }
    }

    private static RectTransform CreateRect(
        Transform parent,
        string name,
        Vector2 size)
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
        Color color)
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

    private static void SetImage(
        RectTransform rect,
        Color color,
        bool raycast)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
    }

    private static void SetAnchors(
        RectTransform rect,
        Vector2 min,
        Vector2 max)
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

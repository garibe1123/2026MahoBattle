using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Broadcast-language overlay for combat stage transitions.
///
/// Physical stage flow remains owned by BattleStageTransitionController. This component only
/// listens to FlowStateChanged and renders an unscaled presentation layer:
/// - RoomEntering: black diagonal wipe -> CRT power-on line -> static/sync acquisition -> LIVE lock.
/// - Combat: keep only a very subtle scanline texture + LIVE bug.
/// - RoomExiting: short signal-break burst, without covering the tile retirement animation.
///
/// The overlay never blocks raycasts and does not change Time.timeScale, camera ownership,
/// world positions, tile motion, or Show state.
/// </summary>
[DefaultExecutionOrder(34000)]
[DisallowMultipleComponent]
public sealed class BattleBroadcastTransitionController : MonoBehaviour
{
    private const int CanvasSortingOrder = 2400;
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;

    [Header("Timing")]
    [SerializeField, Min(0.01f)] private float wipeToBlackDuration = 0.14f;
    [SerializeField, Min(0f)] private float blackHoldDuration = 0.045f;
    [SerializeField, Min(0.01f)] private float crtOpenDuration = 0.24f;
    [SerializeField, Min(0.01f)] private float signalTuneDuration = 0.30f;
    [SerializeField, Min(0.01f)] private float liveFadeDuration = 0.12f;
    [SerializeField, Min(0.01f)] private float signalBreakDuration = 0.14f;

    [Header("Analog Signal")]
    [SerializeField, Range(0f, 1f)] private float entryStaticAlpha = 0.72f;
    [SerializeField, Range(0f, 0.3f)] private float entryScanlineAlpha = 0.18f;
    [SerializeField, Range(0f, 0.12f)] private float combatScanlineAlpha = 0.035f;
    [SerializeField, Range(8f, 60f)] private float noiseRefreshHz = 30f;
    [SerializeField, Range(0f, 0.5f)] private float signalBreakStaticAlpha = 0.30f;

    [Header("Wipe")]
    [SerializeField] private float wipeRotation = -9f;
    [SerializeField] private float wipeTravel = 2450f;

    private static BattleBroadcastTransitionController instance;

    private BattleStageTransitionController stageFlow;
    private Coroutine bindRoutine;
    private Coroutine transitionRoutine;
    private BattleStageFlowState lastFlowState = BattleStageFlowState.Base;

    private Canvas overlayCanvas;
    private CanvasGroup overlayGroup;
    private RectTransform overlayRoot;

    private RectTransform wipeRect;
    private Image wipeImage;
    private Image blackFill;
    private RectTransform topShutter;
    private RectTransform bottomShutter;
    private RectTransform powerLineRect;
    private Image powerLineImage;
    private Image noiseImage;
    private Image scanlineImage;
    private RectTransform syncBandRect;
    private Image syncBandImage;
    private Text signalLabel;
    private CanvasGroup liveGroup;

    private Texture2D noiseTexture;
    private Sprite noiseSprite;
    private Color32[] noisePixels;
    private Sprite scanlineSprite;
    private System.Random noiseRandom;
    private bool noiseActive;
    private float nextNoiseRefresh;

    public static BattleBroadcastTransitionController Instance => instance;
    public bool IsTransitioning => transitionRoutine != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterSceneLoad()
    {
        EnsureInstalled();
    }

    private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        EnsureInstalled();
    }

    private static void EnsureInstalled()
    {
        if (FindFirstObjectByType<BattleBroadcastTransitionController>(FindObjectsInactive.Include) != null)
            return;

        BattleStageTransitionController stage = BattleStageTransitionController.Instance != null
            ? BattleStageTransitionController.Instance
            : FindFirstObjectByType<BattleStageTransitionController>(FindObjectsInactive.Include);

        if (stage != null)
        {
            stage.gameObject.AddComponent<BattleBroadcastTransitionController>();
            return;
        }

        GameObject host = new("BattleBroadcastTransitionRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleBroadcastTransitionController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        noiseRandom = new System.Random(0x4D41484F);
        EnsureUi();
        HideAllImmediate();
    }

    private void OnEnable()
    {
        EnsureUi();
        if (bindRoutine == null)
            bindRoutine = StartCoroutine(BindWhenReady());
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
            StopCoroutine(bindRoutine);
        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);

        bindRoutine = null;
        transitionRoutine = null;
        noiseActive = false;
        UnbindStageFlow();
        HideAllImmediate();
    }

    private void OnDestroy()
    {
        UnbindStageFlow();

        if (noiseSprite != null)
            Destroy(noiseSprite);
        if (noiseTexture != null)
            Destroy(noiseTexture);
        if (scanlineSprite != null && scanlineSprite.texture != null)
            Destroy(scanlineSprite.texture);
        if (scanlineSprite != null)
            Destroy(scanlineSprite);

        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        if (!noiseActive || noiseTexture == null || noisePixels == null)
            return;

        if (Time.unscaledTime < nextNoiseRefresh)
            return;

        nextNoiseRefresh = Time.unscaledTime + 1f / Mathf.Max(8f, noiseRefreshHz);
        RefreshNoiseTexture();
    }

    private IEnumerator BindWhenReady()
    {
        while (enabled)
        {
            BattleStageTransitionController resolved = BattleStageTransitionController.Instance != null
                ? BattleStageTransitionController.Instance
                : FindFirstObjectByType<BattleStageTransitionController>(FindObjectsInactive.Include);

            if (resolved != null)
            {
                BindStageFlow(resolved);
                break;
            }

            yield return null;
        }

        bindRoutine = null;
    }

    private void BindStageFlow(BattleStageTransitionController resolved)
    {
        if (stageFlow == resolved)
            return;

        UnbindStageFlow();
        stageFlow = resolved;
        stageFlow.FlowStateChanged += HandleFlowStateChanged;
        lastFlowState = stageFlow.FlowState;

        if (lastFlowState == BattleStageFlowState.RoomEntering)
            PlayBroadcastOn();
        else if (lastFlowState == BattleStageFlowState.Combat)
            SetCombatSignalLockedImmediate();
    }

    private void UnbindStageFlow()
    {
        if (stageFlow != null)
            stageFlow.FlowStateChanged -= HandleFlowStateChanged;
        stageFlow = null;
    }

    private void HandleFlowStateChanged(BattleStageFlowState next)
    {
        BattleStageFlowState previous = lastFlowState;
        lastFlowState = next;

        switch (next)
        {
            case BattleStageFlowState.RoomEntering:
                if (previous != BattleStageFlowState.RoomEntering)
                    PlayBroadcastOn();
                break;

            case BattleStageFlowState.Combat:
                // Normally the RoomEntering sequence has already reached signal lock.
                // If a very short room skips ahead, do not restart the wipe; just converge to LIVE.
                if (transitionRoutine == null)
                    SetCombatSignalLockedImmediate();
                break;

            case BattleStageFlowState.RoomExiting:
                PlaySignalBreak();
                break;

            case BattleStageFlowState.NonCombat:
            case BattleStageFlowState.ShowEntering:
            case BattleStageFlowState.RewardShow:
            case BattleStageFlowState.MapShow:
            case BattleStageFlowState.ShowExiting:
            case BattleStageFlowState.Base:
            case BattleStageFlowState.Ended:
                if (transitionRoutine == null)
                    HideBroadcastIdentity();
                break;
        }
    }

    public void PlayBroadcastOn()
    {
        EnsureUi();
        StopTransition();
        transitionRoutine = StartCoroutine(PlayBroadcastOnRoutine());
    }

    public void PlaySignalBreak()
    {
        EnsureUi();
        StopTransition();
        transitionRoutine = StartCoroutine(PlaySignalBreakRoutine());
    }

    private void StopTransition()
    {
        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);
        transitionRoutine = null;
        noiseActive = false;
    }

    private IEnumerator PlayBroadcastOnRoutine()
    {
        overlayGroup.alpha = 1f;
        overlayGroup.blocksRaycasts = false;
        liveGroup.alpha = 0f;

        SetImageAlpha(scanlineImage, 0f);
        SetImageAlpha(noiseImage, 0f);
        SetImageAlpha(syncBandImage, 0f);
        signalLabel.gameObject.SetActive(false);
        powerLineImage.gameObject.SetActive(false);
        topShutter.gameObject.SetActive(false);
        bottomShutter.gameObject.SetActive(false);
        blackFill.gameObject.SetActive(false);

        wipeImage.gameObject.SetActive(true);
        wipeRect.localRotation = Quaternion.Euler(0f, 0f, wipeRotation);
        wipeRect.anchoredPosition = new Vector2(-Mathf.Abs(wipeTravel), 0f);

        yield return TweenUnscaled(wipeToBlackDuration, t =>
        {
            float eased = EaseOutCubic(t);
            wipeRect.anchoredPosition = new Vector2(
                Mathf.Lerp(-Mathf.Abs(wipeTravel), 0f, eased),
                0f);
        });

        blackFill.gameObject.SetActive(true);
        wipeImage.gameObject.SetActive(false);

        if (blackHoldDuration > 0f)
            yield return WaitRealtime(blackHoldDuration);

        // CRT power-on: the screen exists first as a bright horizontal line, then opens vertically.
        topShutter.gameObject.SetActive(true);
        bottomShutter.gameObject.SetActive(true);
        topShutter.localScale = Vector3.one;
        bottomShutter.localScale = Vector3.one;
        blackFill.gameObject.SetActive(false);

        powerLineImage.gameObject.SetActive(true);
        powerLineRect.localScale = new Vector3(0.08f, 1f, 1f);
        SetImageAlpha(powerLineImage, 1f);

        noiseActive = true;
        noiseImage.gameObject.SetActive(true);
        scanlineImage.gameObject.SetActive(true);
        syncBandImage.gameObject.SetActive(true);
        signalLabel.gameObject.SetActive(true);
        signalLabel.text = "CH 04  //  SYNC SEARCH";
        SetImageAlpha(noiseImage, entryStaticAlpha);
        SetImageAlpha(scanlineImage, entryScanlineAlpha);
        RefreshNoiseTexture();

        yield return TweenUnscaled(crtOpenDuration, t =>
        {
            float open = Smooth01(t);
            topShutter.localScale = new Vector3(1f, 1f - open, 1f);
            bottomShutter.localScale = new Vector3(1f, 1f - open, 1f);

            float lineExpand = Smooth01(Mathf.Clamp01(t * 3.2f));
            powerLineRect.localScale = new Vector3(Mathf.Lerp(0.08f, 1.08f, lineExpand), 1f, 1f);
            SetImageAlpha(powerLineImage, 1f - Smooth01(Mathf.InverseLerp(0.36f, 1f, t)));

            ApplySyncJitter(1f - t, 0.18f);
        });

        topShutter.gameObject.SetActive(false);
        bottomShutter.gameObject.SetActive(false);
        powerLineImage.gameObject.SetActive(false);
        signalLabel.text = "CH 04  //  SIGNAL LOCK";

        yield return TweenUnscaled(signalTuneDuration, t =>
        {
            float settle = Smooth01(t);
            SetImageAlpha(noiseImage, Mathf.Lerp(entryStaticAlpha * 0.78f, 0f, settle));
            SetImageAlpha(scanlineImage, Mathf.Lerp(entryScanlineAlpha, combatScanlineAlpha, settle));
            ApplySyncJitter(1f - settle, Mathf.Lerp(0.16f, 0f, settle));
        });

        noiseActive = false;
        noiseImage.gameObject.SetActive(false);
        syncBandImage.gameObject.SetActive(false);
        signalLabel.gameObject.SetActive(false);

        liveGroup.gameObject.SetActive(true);
        liveGroup.alpha = 0f;
        yield return TweenUnscaled(liveFadeDuration, t => liveGroup.alpha = Smooth01(t));

        transitionRoutine = null;
    }

    private IEnumerator PlaySignalBreakRoutine()
    {
        liveGroup.alpha = 0f;
        liveGroup.gameObject.SetActive(false);
        blackFill.gameObject.SetActive(false);
        wipeImage.gameObject.SetActive(false);
        topShutter.gameObject.SetActive(false);
        bottomShutter.gameObject.SetActive(false);
        powerLineImage.gameObject.SetActive(false);

        overlayGroup.alpha = 1f;
        noiseActive = true;
        noiseImage.gameObject.SetActive(true);
        scanlineImage.gameObject.SetActive(true);
        syncBandImage.gameObject.SetActive(true);
        signalLabel.gameObject.SetActive(true);
        signalLabel.text = "CH 04  //  SIGNAL LOST";
        RefreshNoiseTexture();

        yield return TweenUnscaled(signalBreakDuration, t =>
        {
            float pulse = 1f - Smooth01(t);
            SetImageAlpha(noiseImage, signalBreakStaticAlpha * (0.45f + pulse * 0.55f));
            SetImageAlpha(scanlineImage, Mathf.Lerp(entryScanlineAlpha * 0.8f, 0f, t));
            ApplySyncJitter(pulse, 0.20f * pulse);
        });

        noiseActive = false;
        noiseImage.gameObject.SetActive(false);
        scanlineImage.gameObject.SetActive(false);
        syncBandImage.gameObject.SetActive(false);
        signalLabel.gameObject.SetActive(false);
        transitionRoutine = null;
    }

    private void SetCombatSignalLockedImmediate()
    {
        EnsureUi();
        StopTransition();

        overlayGroup.alpha = 1f;
        blackFill.gameObject.SetActive(false);
        wipeImage.gameObject.SetActive(false);
        topShutter.gameObject.SetActive(false);
        bottomShutter.gameObject.SetActive(false);
        powerLineImage.gameObject.SetActive(false);
        noiseImage.gameObject.SetActive(false);
        syncBandImage.gameObject.SetActive(false);
        signalLabel.gameObject.SetActive(false);
        scanlineImage.gameObject.SetActive(combatScanlineAlpha > 0.001f);
        SetImageAlpha(scanlineImage, combatScanlineAlpha);
        liveGroup.gameObject.SetActive(true);
        liveGroup.alpha = 1f;
    }

    private void HideBroadcastIdentity()
    {
        if (liveGroup != null)
        {
            liveGroup.alpha = 0f;
            liveGroup.gameObject.SetActive(false);
        }

        if (scanlineImage != null)
            scanlineImage.gameObject.SetActive(false);
    }

    private void HideAllImmediate()
    {
        noiseActive = false;

        if (overlayGroup != null)
        {
            overlayGroup.alpha = 1f;
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable = false;
        }

        SetActive(blackFill, false);
        SetActive(wipeImage, false);
        SetActive(powerLineImage, false);
        SetActive(noiseImage, false);
        SetActive(scanlineImage, false);
        SetActive(syncBandImage, false);

        if (topShutter != null)
            topShutter.gameObject.SetActive(false);
        if (bottomShutter != null)
            bottomShutter.gameObject.SetActive(false);
        if (signalLabel != null)
            signalLabel.gameObject.SetActive(false);
        if (liveGroup != null)
        {
            liveGroup.alpha = 0f;
            liveGroup.gameObject.SetActive(false);
        }
    }

    private void ApplySyncJitter(float amount, float alpha)
    {
        if (syncBandRect == null || syncBandImage == null)
            return;

        float a = Mathf.Clamp01(amount);
        float y = Mathf.Sin(Time.unscaledTime * 73f) * ReferenceHeight * 0.34f * a;
        float x = Mathf.Sin(Time.unscaledTime * 119f) * 46f * a;
        syncBandRect.anchoredPosition = new Vector2(x, y);
        SetImageAlpha(syncBandImage, Mathf.Clamp01(alpha));
    }

    private IEnumerator TweenUnscaled(float duration, Action<float> apply)
    {
        float resolvedDuration = Mathf.Max(0.001f, duration);
        float elapsed = 0f;

        apply?.Invoke(0f);
        while (elapsed < resolvedDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            apply?.Invoke(Mathf.Clamp01(elapsed / resolvedDuration));
            yield return null;
        }

        apply?.Invoke(1f);
    }

    private static IEnumerator WaitRealtime(float duration)
    {
        float end = Time.unscaledTime + Mathf.Max(0f, duration);
        while (Time.unscaledTime < end)
            yield return null;
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    private void EnsureUi()
    {
        if (overlayCanvas != null)
            return;

        GameObject canvasObject = new("BattleBroadcastTransitionCanvas");
        canvasObject.transform.SetParent(transform, false);
        overlayCanvas = canvasObject.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = CanvasSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.matchWidthOrHeight = 0.5f;

        overlayRoot = CreateRect(canvasObject.transform, "BroadcastOverlay", Vector2.zero);
        Stretch(overlayRoot);
        overlayGroup = overlayRoot.gameObject.AddComponent<CanvasGroup>();
        overlayGroup.blocksRaycasts = false;
        overlayGroup.interactable = false;

        blackFill = CreateImage(overlayRoot, "BlackFill", Color.black);
        Stretch(blackFill.rectTransform);

        wipeImage = CreateImage(overlayRoot, "BlackWipe", Color.black);
        wipeRect = wipeImage.rectTransform;
        wipeRect.anchorMin = wipeRect.anchorMax = new Vector2(0.5f, 0.5f);
        wipeRect.sizeDelta = new Vector2(2800f, 1500f);
        wipeRect.localRotation = Quaternion.Euler(0f, 0f, wipeRotation);

        topShutter = CreateRect(overlayRoot, "CRTTopShutter", Vector2.zero);
        topShutter.anchorMin = new Vector2(0f, 0.5f);
        topShutter.anchorMax = Vector2.one;
        topShutter.offsetMin = Vector2.zero;
        topShutter.offsetMax = Vector2.zero;
        topShutter.pivot = new Vector2(0.5f, 1f);
        Image topImage = topShutter.gameObject.AddComponent<Image>();
        topImage.color = Color.black;
        topImage.raycastTarget = false;

        bottomShutter = CreateRect(overlayRoot, "CRTBottomShutter", Vector2.zero);
        bottomShutter.anchorMin = Vector2.zero;
        bottomShutter.anchorMax = new Vector2(1f, 0.5f);
        bottomShutter.offsetMin = Vector2.zero;
        bottomShutter.offsetMax = Vector2.zero;
        bottomShutter.pivot = new Vector2(0.5f, 0f);
        Image bottomImage = bottomShutter.gameObject.AddComponent<Image>();
        bottomImage.color = Color.black;
        bottomImage.raycastTarget = false;

        powerLineImage = CreateImage(overlayRoot, "CRTPowerLine", Color.white);
        powerLineRect = powerLineImage.rectTransform;
        powerLineRect.anchorMin = powerLineRect.anchorMax = new Vector2(0.5f, 0.5f);
        powerLineRect.sizeDelta = new Vector2(ReferenceWidth * 1.08f, 4f);

        noiseImage = CreateImage(overlayRoot, "AnalogStatic", Color.white);
        Stretch(noiseImage.rectTransform);
        EnsureNoiseTexture();
        noiseImage.sprite = noiseSprite;
        noiseImage.type = Image.Type.Simple;

        scanlineImage = CreateImage(overlayRoot, "Scanlines", Color.white);
        Stretch(scanlineImage.rectTransform);
        scanlineSprite = CreateScanlineSprite();
        scanlineImage.sprite = scanlineSprite;
        scanlineImage.type = Image.Type.Tiled;
        scanlineImage.pixelsPerUnitMultiplier = 1f;

        syncBandImage = CreateImage(overlayRoot, "VerticalSyncTear", new Color(1f, 1f, 1f, 0f));
        syncBandRect = syncBandImage.rectTransform;
        syncBandRect.anchorMin = syncBandRect.anchorMax = new Vector2(0.5f, 0.5f);
        syncBandRect.sizeDelta = new Vector2(ReferenceWidth * 1.18f, 54f);

        signalLabel = CreateText(overlayRoot, "CH 04  //  SYNC SEARCH", 18, FontStyle.Bold, TextAnchor.UpperLeft, Color.white);
        signalLabel.rectTransform.anchorMin = signalLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
        signalLabel.rectTransform.pivot = new Vector2(0f, 1f);
        signalLabel.rectTransform.sizeDelta = new Vector2(520f, 44f);
        signalLabel.rectTransform.anchoredPosition = new Vector2(36f, -30f);

        RectTransform liveRoot = CreateRect(overlayRoot, "LiveBug", new Vector2(410f, 46f));
        liveRoot.anchorMin = liveRoot.anchorMax = new Vector2(1f, 1f);
        liveRoot.pivot = new Vector2(1f, 1f);
        liveRoot.anchoredPosition = new Vector2(-28f, -24f);
        Image liveBack = liveRoot.gameObject.AddComponent<Image>();
        liveBack.color = new Color(0.015f, 0.015f, 0.018f, 0.78f);
        liveBack.raycastTarget = false;
        liveGroup = liveRoot.gameObject.AddComponent<CanvasGroup>();
        liveGroup.blocksRaycasts = false;
        liveGroup.interactable = false;

        Text liveText = CreateText(liveRoot, "● LIVE   //   BATTLE BROADCAST", 18, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.93f, 0.90f, 1f));
        Stretch(liveText.rectTransform);
    }

    private void EnsureNoiseTexture()
    {
        if (noiseTexture != null && noiseSprite != null && noisePixels != null)
            return;

        const int width = 96;
        const int height = 54;
        noiseTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "BattleBroadcastStatic_Runtime",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.HideAndDontSave
        };
        noisePixels = new Color32[width * height];
        RefreshNoiseTexture();

        noiseSprite = Sprite.Create(
            noiseTexture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            32f,
            0,
            SpriteMeshType.FullRect);
        noiseSprite.name = "BattleBroadcastStaticSprite_Runtime";
        noiseSprite.hideFlags = HideFlags.HideAndDontSave;
    }

    private void RefreshNoiseTexture()
    {
        if (noiseTexture == null || noisePixels == null)
            return;

        for (int i = 0; i < noisePixels.Length; i++)
        {
            int value = noiseRandom != null ? noiseRandom.Next(24, 246) : UnityEngine.Random.Range(24, 246);
            byte tone = (byte)value;
            noisePixels[i] = new Color32(tone, tone, tone, 255);
        }

        noiseTexture.SetPixels32(noisePixels);
        noiseTexture.Apply(false, false);
    }

    private static Sprite CreateScanlineSprite()
    {
        const int width = 8;
        const int height = 8;
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "BattleBroadcastScanlines_Runtime",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            byte alpha = (byte)((y % 4 == 0 || y % 4 == 1) ? 110 : 0);
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = new Color32(0, 0, 0, alpha);
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            8f,
            0,
            SpriteMeshType.FullRect);
        sprite.name = "BattleBroadcastScanlinesSprite_Runtime";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static Image CreateImage(Transform parent, string name, Color color)
    {
        RectTransform rect = CreateRect(parent, name, Vector2.zero);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
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

    private static void Stretch(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetImageAlpha(Image image, float alpha)
    {
        if (image == null)
            return;
        Color color = image.color;
        color.a = Mathf.Clamp01(alpha);
        image.color = color;
    }

    private static void SetActive(Image image, bool active)
    {
        if (image != null)
            image.gameObject.SetActive(active);
    }
}

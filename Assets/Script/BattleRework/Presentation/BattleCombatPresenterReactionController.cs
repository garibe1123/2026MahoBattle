using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전투 중 하이라이트를 우측 상단의 짧은 사회자 리액션 카드로 보여줍니다.
///
/// - BattleRoomManager.MonsterDefeated를 직접 구독합니다.
/// - 아주 짧은 시간 안의 처치를 하나의 Kill Burst로 묶어 Multi Kill을 판정합니다.
/// - 마지막 적 처치 / Elite / Boss / 외부 점수 이벤트에 반응합니다.
/// - 전투용 상체 이미지는 BattleShowPresentationManager의 Combat Presenter Motions를 사용합니다.
/// - Shader Material은 Reward/Map 큰 사회자와 동일한 ScreenPresenterMaterial을 공유합니다.
/// </summary>
[DefaultExecutionOrder(71000)]
[DisallowMultipleComponent]
public sealed class BattleCombatPresenterReactionController : MonoBehaviour
{
    private enum PopupPhase
    {
        Hidden,
        PortraitBoot,
        BubblePop,
        Typing,
        Holding,
        Shutdown
    }

    private enum ReactionPriority
    {
        None = 0,
        Medium = 10,
        High = 20,
        Extreme = 30
    }

    private const int OverlaySortingOrder = 560;

    [Header("Kill Detection")]
    [Tooltip("이 시간 안에 발생한 처치를 하나의 Multi Kill로 묶습니다. unscaled seconds.")]
    [SerializeField, Range(0.05f, 0.35f)] private float multiKillCollectWindow = 0.16f;

    [Header("Popup Layout")]
    [SerializeField] private Vector2 referenceResolution = new(1920f, 1080f);
    [SerializeField] private Vector2 popupSize = new(610f, 250f);
    [SerializeField] private Vector2 popupVisibleOffset = new(-24f, -24f);

    [Header("Reaction Timing")]
    [SerializeField, Range(0.04f, 0.30f)] private float portraitBootDuration = 0.13f;
    [SerializeField, Range(0.04f, 0.30f)] private float bubblePopDuration = 0.12f;
    [SerializeField, Min(1f)] private float typeCharactersPerSecond = 30f;
    [SerializeField, Range(0.4f, 3f)] private float holdSeconds = 1.20f;
    [SerializeField, Range(0.05f, 0.35f)] private float shutdownDuration = 0.16f;

    [Header("Portrait")]
    [SerializeField] private Vector2 portraitSize = new(230f, 205f);
    [SerializeField, Range(1f, 1.25f)] private float portraitBootScale = 1.10f;

    [Header("Speech Bubble")]
    [SerializeField] private Vector2 bubbleSize = new(390f, 150f);
    [SerializeField] private Vector2 bubbleOffset = new(-205f, -55f);
    [SerializeField, Range(-8f, 8f)] private float bubbleRotation = -2.5f;
    [SerializeField, Range(0.7f, 1f)] private float bubbleStartScale = 0.86f;
    [SerializeField, Range(1f, 1.15f)] private float bubbleOvershootScale = 1.055f;
    [Tooltip("전투용 사회자 상체 Rect 안에서 말풍선 꼬리가 향할 기준점입니다. (0,0)=좌하단, (1,1)=우상단")]
    [SerializeField] private Vector2 combatTailPortraitAnchor = new(0.18f, 0.52f);
    [SerializeField] private Vector2 combatTailPivotOffset = new(-4f, -2f);

    [Header("Glitch Boot")]
    [Tooltip("Shader가 이 프로퍼티를 지원하면 전투 리액션 얼굴에만 자동으로 지지직 강도를 적용합니다.")]
    [SerializeField] private string glitchStrengthProperty = "_GlitchStrength";
    [SerializeField] private string noiseStrengthProperty = "_NoiseStrength";
    [SerializeField] private string rgbSplitProperty = "_RGBSplit";
    [SerializeField, Range(0f, 2f)] private float glitchBootStrength = 1f;
    [SerializeField, Range(0f, 2f)] private float glitchShutdownStrength = 0.8f;
    [SerializeField, Range(0f, 1f)] private float glitchNoiseStrength = 0.65f;
    [SerializeField, Range(0f, 0.1f)] private float glitchRgbSplit = 0.025f;

    [Header("Score Hooks")]
    [Tooltip("외부 점수 시스템에서 NotifyScore를 호출할 때 이 값 이상이면 리액션을 띄웁니다.")]
    [SerializeField, Min(1)] private int mediumScoreThreshold = 500;
    [SerializeField, Min(1)] private int highScoreThreshold = 1000;
    [SerializeField, Min(1)] private int extremeScoreThreshold = 2000;

    private static BattleCombatPresenterReactionController instance;

    private BattleRoomManager roomManager;
    private BattleRoomManager subscribedRoom;
    private BattleShowPresentationManager presentation;

    private Canvas overlayCanvas;
    private RectTransform overlayRoot;
    private RectTransform popupRect;
    private CanvasGroup popupGroup;
    private RectTransform portraitRect;
    private CanvasGroup portraitGroup;
    private Image portraitImage;
    private RectTransform bubbleRect;
    private CanvasGroup bubbleGroup;
    private Image bubbleBack;
    private RectTransform tailPivotRect;
    private BattleSpeechBubbleTailTriangleController bubbleTailGraphic;
    private RectTransform tagBadgeRect;
    private Text tagText;
    private Text lineText;

    private PopupPhase popupPhase = PopupPhase.Hidden;
    private float phaseTime;
    private float holdUntil;
    private ReactionPriority activePriority;
    private string activeLine = string.Empty;
    private float typeProgress;
    private int visibleCharacters;

    private readonly List<Sprite> runtimeFrames = new();
    private CombatPresenterMotionClip activeClip;
    private Sprite activeSource;
    private int frameIndex;
    private float frameTimer;
    private Material appliedMaterial;
    private Material runtimePortraitMaterial;

    private int pendingKillCount;
    private bool pendingElite;
    private bool pendingBoss;
    private bool pendingClear;
    private bool clearReactionShownForCurrentRoom;
    private float pendingKillResolveAt = -1f;

    private readonly Dictionary<string, int> lastLineIndex = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleCombatPresenterReactionController>() != null)
            return;

        GameObject host = new("BattleCombatPresenterReactionRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleCombatPresenterReactionController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        UnsubscribeRoom();
        ReleaseRuntimeFrames();
        ReleaseRuntimePortraitMaterial();

        if (overlayCanvas != null)
            Destroy(overlayCanvas.gameObject);

        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        ResolveReferences();
        EnsureOverlay();

        if (pendingKillCount > 0 &&
            pendingKillResolveAt >= 0f &&
            Time.unscaledTime >= pendingKillResolveAt)
        {
            ResolvePendingKillBurst();
        }

        UpdatePopup();
        UpdatePortraitAnimation();
    }

    public static void NotifyScore(int scoreDelta, int totalScore = -1)
    {
        BattleCombatPresenterReactionController owner = Resolve();
        if (owner == null)
            return;

        owner.HandleScore(scoreDelta, totalScore);
    }

    public static void PreviewReaction()
    {
        Resolve()?.ShowReaction(
            "HIGHLIGHT!",
            "좋습니다! 이런 장면 하나쯤 나와줘야죠!",
            ScreenPresenterMotionState.Excited,
            ReactionPriority.High);
    }

    private static BattleCombatPresenterReactionController Resolve()
    {
        if (instance != null)
            return instance;

        instance = FindFirstObjectByType<BattleCombatPresenterReactionController>();
        if (instance != null)
            return instance;

        GameObject host = new("BattleCombatPresenterReactionRuntime");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<BattleCombatPresenterReactionController>();
        return instance;
    }

    private void ResolveReferences()
    {
        if (presentation == null)
        {
            presentation = BattleShowPresentationManager.Instance != null
                ? BattleShowPresentationManager.Instance
                : FindFirstObjectByType<BattleShowPresentationManager>();
        }

        BattleRoomManager foundRoom = FindFirstObjectByType<BattleRoomManager>();
        if (foundRoom == subscribedRoom)
        {
            roomManager = foundRoom;
            return;
        }

        UnsubscribeRoom();
        roomManager = foundRoom;

        if (roomManager != null)
        {
            subscribedRoom = roomManager;
            subscribedRoom.MonsterDefeated += HandleMonsterDefeated;
            subscribedRoom.RoomCombatStarted += HandleCombatStarted;
            subscribedRoom.RoomCombatCleared += HandleCombatCleared;
        }
    }

    private void UnsubscribeRoom()
    {
        if (subscribedRoom == null)
            return;

        subscribedRoom.MonsterDefeated -= HandleMonsterDefeated;
        subscribedRoom.RoomCombatStarted -= HandleCombatStarted;
        subscribedRoom.RoomCombatCleared -= HandleCombatCleared;
        subscribedRoom = null;
    }

    private void HandleCombatStarted(RoomDefinitionSO _)
    {
        clearReactionShownForCurrentRoom = false;
        ClearPendingKills();
        HideImmediate();
    }

    private void HandleCombatCleared(RoomDefinitionSO _)
    {
        // Normally the last MonsterDefeated callback already marks pendingClear.
        // This is only a safety path for externally-completed rooms.
        if (clearReactionShownForCurrentRoom || pendingKillCount > 0)
            return;

        clearReactionShownForCurrentRoom = true;
        ShowReaction(
            "ALL CLEAR!",
            PickLine(
                "clear",
                "좋습니다! 무대 정리됐습니다!",
                "깔끔하게 끝냈네요. 다음 코너 준비하죠!",
                "전원 정리! 이걸로 이번 무대 종료입니다!"),
            ScreenPresenterMotionState.Excited,
            ReactionPriority.High);
    }

    private void HandleMonsterDefeated(MonsterController monster)
    {
        if (monster == null)
            return;

        pendingKillCount++;
        pendingElite |= monster.IsElite;
        pendingBoss |= monster.IsBoss;
        pendingClear |= roomManager != null && roomManager.AliveMonsterCount <= 0;
        pendingKillResolveAt =
            Time.unscaledTime + Mathf.Max(0.05f, multiKillCollectWindow);
    }

    private void ResolvePendingKillBurst()
    {
        int kills = pendingKillCount;
        bool elite = pendingElite;
        bool boss = pendingBoss;
        bool clear = pendingClear;
        ClearPendingKills();

        if (kills <= 0)
            return;

        if (clear && kills >= 3)
        {
            clearReactionShownForCurrentRoom = true;
            ShowReaction(
                "FINISH!",
                PickLine(
                    "clear_multi",
                    $"마지막에 {kills}명 한 번에 정리! 이건 제대로 끝냈네요!",
                    $"와, 마지막 {kills}명까지 한 번에! 오늘 하이라이트 후보입니다!",
                    $"끝내는 장면이 좋았어요. {kills}명 동시에 정리!"),
                ScreenPresenterMotionState.Surprised,
                ReactionPriority.Extreme);
            return;
        }

        if (boss)
        {
            if (clear)
                clearReactionShownForCurrentRoom = true;

            ShowReaction(
                "BOSS DOWN!",
                PickLine(
                    "boss",
                    "좋습니다! 메인 타깃 잡았습니다!",
                    "보스 다운! 이건 큰 장면이죠!",
                    "끝났네요! 오늘 메인 무대, 여기서 정리됩니다!"),
                ScreenPresenterMotionState.Excited,
                ReactionPriority.Extreme);
            return;
        }

        if (kills >= 4)
        {
            ShowReaction(
                $"{kills} KILL!",
                PickLine(
                    "multi4",
                    $"잠깐, {kills}명을 한 번에요? 이건 다시 보여줘도 되겠는데요!",
                    $"{kills}명 동시 정리! 좋아요, 화면 제대로 나왔습니다!",
                    $"이건 좀 놀랍네요. 한 번에 {kills}명입니다!"),
                ScreenPresenterMotionState.Surprised,
                ReactionPriority.Extreme);
            return;
        }

        if (kills == 3)
        {
            ShowReaction(
                "TRIPLE KILL!",
                PickLine(
                    "triple",
                    "셋을 한 번에 정리했네요! 좋습니다!",
                    "트리플! 방금 장면 꽤 좋았는데요?",
                    "세 명 동시 정리! 이런 장면은 반갑죠!"),
                ScreenPresenterMotionState.Excited,
                ReactionPriority.High);
            return;
        }

        if (kills == 2)
        {
            ShowReaction(
                "DOUBLE KILL!",
                PickLine(
                    "double",
                    "오, 둘을 한 번에 정리했네요!",
                    "더블 킬! 깔끔하게 들어갔습니다.",
                    "좋아요, 두 명 동시에 정리!"),
                ScreenPresenterMotionState.Excited,
                ReactionPriority.Medium);
            return;
        }

        if (clear)
        {
            clearReactionShownForCurrentRoom = true;
            ShowReaction(
                "ALL CLEAR!",
                PickLine(
                    "clear",
                    "좋습니다! 무대 정리됐습니다!",
                    "마지막 한 명까지 정리! 이번 무대 종료입니다.",
                    "깔끔하게 끝났네요. 다음 코너 준비하죠!"),
                ScreenPresenterMotionState.Excited,
                ReactionPriority.High);
            return;
        }

        if (elite)
        {
            ShowReaction(
                "ELITE DOWN!",
                PickLine(
                    "elite",
                    "좋아요, 까다로운 쪽 하나 정리됐습니다!",
                    "엘리트 다운! 이제 조금 편해지겠네요.",
                    "핵심 타깃 정리! 이런 건 바로 표시해줘야죠."),
                ScreenPresenterMotionState.Excited,
                ReactionPriority.High);
        }
    }

    private void HandleScore(int scoreDelta, int totalScore)
    {
        int value = Mathf.Max(0, scoreDelta);
        if (value < Mathf.Max(1, mediumScoreThreshold))
            return;

        if (value >= Mathf.Max(extremeScoreThreshold, highScoreThreshold))
        {
            ShowReaction(
                "BIG SCORE!",
                totalScore >= 0
                    ? $"+{value:N0}! 누적 {totalScore:N0}점입니다. 이건 크네요!"
                    : $"+{value:N0}! 오, 점수 크게 들어왔습니다!",
                ScreenPresenterMotionState.Surprised,
                ReactionPriority.Extreme);
            return;
        }

        if (value >= Mathf.Max(highScoreThreshold, mediumScoreThreshold))
        {
            ShowReaction(
                "NICE SCORE!",
                totalScore >= 0
                    ? $"+{value:N0}! 현재 {totalScore:N0}점입니다."
                    : $"+{value:N0}! 점수 잘 챙겼네요!",
                ScreenPresenterMotionState.Excited,
                ReactionPriority.High);
            return;
        }

        ShowReaction(
            "SCORE!",
            $"+{value:N0}! 좋아요, 점수도 챙겼습니다.",
            ScreenPresenterMotionState.Curious,
            ReactionPriority.Medium);
    }

    private void ClearPendingKills()
    {
        pendingKillCount = 0;
        pendingElite = false;
        pendingBoss = false;
        pendingClear = false;
        pendingKillResolveAt = -1f;
    }

    private void ShowReaction(
        string tag,
        string line,
        ScreenPresenterMotionState motion,
        ReactionPriority priority)
    {
        EnsureOverlay();

        if (popupPhase != PopupPhase.Hidden &&
            priority < activePriority)
        {
            return;
        }

        activePriority = priority;
        activeLine = line ?? string.Empty;
        typeProgress = 0f;
        visibleCharacters = 0;

        if (tagText != null)
            tagText.text = tag ?? string.Empty;
        if (lineText != null)
            lineText.text = string.Empty;

        SetPortraitMotion(motion);

        phaseTime = 0f;
        popupPhase = PopupPhase.PortraitBoot;

        if (popupGroup != null)
            popupGroup.alpha = 1f;

        if (portraitGroup != null)
            portraitGroup.alpha = 0f;
        if (portraitRect != null)
            portraitRect.localScale = Vector3.one * portraitBootScale;

        if (bubbleGroup != null)
            bubbleGroup.alpha = 0f;

        if (bubbleRect != null)
        {
            BattleSpeechBubbleFrameStyle frameStyle =
                presentation != null
                    ? presentation.CombatSpeechBubbleFrameStyle
                    : null;

            bubbleRect.localScale =
                Vector3.one *
                (frameStyle != null
                    ? frameStyle.startScale
                    : bubbleStartScale);
        }

        ApplyGlitch(glitchBootStrength, glitchNoiseStrength, glitchRgbSplit);
    }

    private void UpdatePopup()
    {
        if (popupRect == null || popupGroup == null)
            return;

        float dt = Time.unscaledDeltaTime;

        switch (popupPhase)
        {
            case PopupPhase.Hidden:
                popupGroup.alpha = 0f;
                break;

            case PopupPhase.PortraitBoot:
            {
                phaseTime += dt;
                float t = Mathf.Clamp01(
                    phaseTime / Mathf.Max(0.04f, portraitBootDuration));

                float flicker =
                    Mathf.Sin(Time.unscaledTime * 95f) > 0.15f
                        ? 1f
                        : 0.35f;

                if (portraitGroup != null)
                {
                    portraitGroup.alpha =
                        Mathf.Lerp(0.15f, 1f, t) *
                        Mathf.Lerp(flicker, 1f, t);
                }

                if (portraitRect != null)
                {
                    float scale = Mathf.Lerp(
                        portraitBootScale,
                        1f,
                        EaseOutBackSoft(t));
                    portraitRect.localScale = Vector3.one * scale;
                }

                ApplyGlitch(
                    Mathf.Lerp(glitchBootStrength, 0f, t),
                    Mathf.Lerp(glitchNoiseStrength, 0f, t),
                    Mathf.Lerp(glitchRgbSplit, 0f, t));

                if (t >= 1f)
                {
                    if (portraitGroup != null)
                        portraitGroup.alpha = 1f;
                    if (portraitRect != null)
                        portraitRect.localScale = Vector3.one;

                    ApplyGlitch(0f, 0f, 0f);
                    phaseTime = 0f;
                    popupPhase = PopupPhase.BubblePop;
                }
                break;
            }

            case PopupPhase.BubblePop:
            {
                phaseTime += dt;
                float t = Mathf.Clamp01(
                    phaseTime / Mathf.Max(0.04f, bubblePopDuration));

                if (bubbleGroup != null)
                    bubbleGroup.alpha = t;

                if (bubbleRect != null)
                {
                    BattleSpeechBubbleFrameStyle frameStyle =
                        presentation != null
                            ? presentation.CombatSpeechBubbleFrameStyle
                            : null;

                    float startScale =
                        frameStyle != null
                            ? frameStyle.startScale
                            : bubbleStartScale;

                    float overshootScale =
                        frameStyle != null
                            ? frameStyle.overshootScale
                            : bubbleOvershootScale;

                    float settledScale =
                        frameStyle != null
                            ? frameStyle.settledScale
                            : 1f;

                    float scale;
                    if (t < 0.72f)
                    {
                        float a = t / 0.72f;
                        scale = Mathf.Lerp(
                            startScale,
                            overshootScale,
                            EaseOutCubic(a));
                    }
                    else
                    {
                        float b = (t - 0.72f) / 0.28f;
                        scale = Mathf.Lerp(
                            overshootScale,
                            settledScale,
                            b);
                    }

                    bubbleRect.localScale = Vector3.one * scale;
                }

                if (t >= 1f)
                {
                    if (bubbleGroup != null)
                        bubbleGroup.alpha = 1f;

                    if (bubbleRect != null)
                    {
                        BattleSpeechBubbleFrameStyle frameStyle =
                            presentation != null
                                ? presentation.CombatSpeechBubbleFrameStyle
                                : null;

                        bubbleRect.localScale =
                            Vector3.one *
                            (frameStyle != null
                                ? frameStyle.settledScale
                                : 1f);
                    }

                    phaseTime = 0f;
                    typeProgress = 0f;
                    visibleCharacters = 0;
                    popupPhase = PopupPhase.Typing;
                }
                break;
            }

            case PopupPhase.Typing:
            {
                typeProgress +=
                    dt * Mathf.Max(1f, typeCharactersPerSecond);

                int targetCharacters = Mathf.Clamp(
                    Mathf.FloorToInt(typeProgress),
                    0,
                    activeLine.Length);

                if (targetCharacters != visibleCharacters)
                {
                    visibleCharacters = targetCharacters;
                    if (lineText != null)
                    {
                        lineText.text =
                            activeLine.Substring(0, visibleCharacters);
                    }
                }

                if (visibleCharacters >= activeLine.Length)
                {
                    if (lineText != null)
                        lineText.text = activeLine;

                    holdUntil =
                        Time.unscaledTime + Mathf.Max(0.4f, holdSeconds);
                    popupPhase = PopupPhase.Holding;
                }
                break;
            }

            case PopupPhase.Holding:
                if (Time.unscaledTime >= holdUntil)
                {
                    phaseTime = 0f;
                    popupPhase = PopupPhase.Shutdown;
                }
                break;

            case PopupPhase.Shutdown:
            {
                phaseTime += dt;
                float t = Mathf.Clamp01(
                    phaseTime / Mathf.Max(0.05f, shutdownDuration));

                if (bubbleGroup != null)
                    bubbleGroup.alpha = 1f - t;

                if (bubbleRect != null)
                {
                    BattleSpeechBubbleFrameStyle frameStyle =
                        presentation != null
                            ? presentation.CombatSpeechBubbleFrameStyle
                            : null;

                    float settledScale =
                        frameStyle != null
                            ? frameStyle.settledScale
                            : 1f;

                    float shutdownScale =
                        frameStyle != null
                            ? frameStyle.shutdownScale
                            : 0.92f;

                    float scale =
                        Mathf.Lerp(
                            settledScale,
                            shutdownScale,
                            t);

                    bubbleRect.localScale = Vector3.one * scale;
                }

                if (portraitGroup != null)
                {
                    float flicker =
                        Mathf.Sin(Time.unscaledTime * 115f) > -0.1f
                            ? 1f
                            : 0.15f;
                    portraitGroup.alpha =
                        (1f - t) * Mathf.Lerp(1f, flicker, t);
                }

                ApplyGlitch(
                    Mathf.Lerp(0f, glitchShutdownStrength, t),
                    Mathf.Lerp(0f, glitchNoiseStrength, t),
                    Mathf.Lerp(0f, glitchRgbSplit, t));

                if (t >= 1f)
                    HideImmediate();

                break;
            }
        }
    }

    private void HideImmediate()
    {
        if (popupGroup != null)
            popupGroup.alpha = 0f;

        if (portraitGroup != null)
            portraitGroup.alpha = 0f;
        if (portraitRect != null)
            portraitRect.localScale = Vector3.one;

        if (bubbleGroup != null)
            bubbleGroup.alpha = 0f;

        if (bubbleRect != null)
        {
            BattleSpeechBubbleFrameStyle frameStyle =
                presentation != null
                    ? presentation.CombatSpeechBubbleFrameStyle
                    : null;

            bubbleRect.localScale =
                Vector3.one *
                (frameStyle != null
                    ? frameStyle.settledScale
                    : 1f);
        }

        if (lineText != null)
            lineText.text = string.Empty;

        ApplyGlitch(0f, 0f, 0f);

        activeLine = string.Empty;
        typeProgress = 0f;
        visibleCharacters = 0;
        phaseTime = 0f;
        popupPhase = PopupPhase.Hidden;
        activePriority = ReactionPriority.None;
    }

    private void EnsureOverlay()
    {
        if (overlayCanvas != null)
            return;

        GameObject canvasObject =
            new("BattleCombatPresenterReactionOverlay");
        canvasObject.transform.SetParent(transform, false);

        overlayCanvas = canvasObject.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = OverlaySortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        overlayRoot = canvasObject.GetComponent<RectTransform>();

        GameObject popup = new("CombatPresenterReaction", typeof(RectTransform));
        popup.transform.SetParent(overlayRoot, false);
        popupRect = popup.GetComponent<RectTransform>();
        popupRect.anchorMin = popupRect.anchorMax = new Vector2(1f, 1f);
        popupRect.pivot = new Vector2(1f, 1f);
        popupRect.sizeDelta = popupSize;
        popupRect.anchoredPosition = popupVisibleOffset;

        popupGroup = popup.AddComponent<CanvasGroup>();
        popupGroup.alpha = 0f;
        popupGroup.interactable = false;
        popupGroup.blocksRaycasts = false;

        // Portrait: fixed in the upper-right. It does not slide; it boots in with glitch/flicker.
        GameObject portrait = new("PresenterPortrait", typeof(RectTransform));
        portrait.transform.SetParent(popupRect, false);
        portraitRect = portrait.GetComponent<RectTransform>();
        portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(1f, 1f);
        portraitRect.pivot = new Vector2(1f, 1f);
        portraitRect.sizeDelta = portraitSize;
        portraitRect.anchoredPosition = Vector2.zero;

        portraitGroup = portrait.AddComponent<CanvasGroup>();
        portraitGroup.alpha = 0f;
        portraitGroup.interactable = false;
        portraitGroup.blocksRaycasts = false;

        portraitImage = portrait.AddComponent<Image>();
        portraitImage.preserveAspect = true;
        portraitImage.raycastTarget = false;
        portraitImage.sprite = BattleHudSpriteCache.DefaultSprite;

        GameObject tailPivot = new("CombatTailPivot", typeof(RectTransform));
        tailPivot.transform.SetParent(portraitRect, false);
        tailPivotRect = tailPivot.GetComponent<RectTransform>();
        tailPivotRect.anchorMin = tailPivotRect.anchorMax = combatTailPortraitAnchor;
        tailPivotRect.pivot = new Vector2(0.5f, 0.5f);
        tailPivotRect.sizeDelta = Vector2.zero;
        tailPivotRect.anchoredPosition = combatTailPivotOffset;

        BattleSpeechBubbleFrameStyle frameStyle =
            presentation != null
                ? presentation.CombatSpeechBubbleFrameStyle
                : null;

        // Angular speech bubble inspired by comic/game-show cut-ins.
        GameObject bubble = new("ReactionSpeechBubble", typeof(RectTransform));
        bubble.transform.SetParent(popupRect, false);
        bubbleRect = bubble.GetComponent<RectTransform>();
        bubbleRect.anchorMin = bubbleRect.anchorMax = new Vector2(1f, 1f);
        bubbleRect.pivot = new Vector2(1f, 1f);
        bubbleRect.sizeDelta =
            frameStyle != null
                ? frameStyle.size
                : bubbleSize;

        bubbleRect.anchoredPosition = bubbleOffset;

        bubbleRect.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                frameStyle != null
                    ? frameStyle.rotation
                    : bubbleRotation);

        bubbleGroup = bubble.AddComponent<CanvasGroup>();
        bubbleGroup.alpha = 0f;
        bubbleGroup.interactable = false;
        bubbleGroup.blocksRaycasts = false;

        // Black offset backing creates a rough ink-outline silhouette.
        GameObject shadow = new("BubbleInkBack", typeof(RectTransform));
        shadow.transform.SetParent(bubbleRect, false);
        RectTransform shadowRect = shadow.GetComponent<RectTransform>();
        Stretch(
            shadowRect,
            new Vector2(
                -(frameStyle != null ? frameStyle.outlineLeft : 7f),
                -(frameStyle != null ? frameStyle.outlineBottom : 8f)),
            new Vector2(
                frameStyle != null ? frameStyle.outlineRight : 7f,
                frameStyle != null ? frameStyle.outlineTop : 8f));

        Image shadowImage = shadow.AddComponent<Image>();
        shadowImage.color =
            frameStyle != null
                ? frameStyle.outlineColor
                : new Color(0.015f, 0.015f, 0.02f, 0.98f);
        shadowImage.raycastTarget = false;

        GameObject face = new("BubbleFace", typeof(RectTransform));
        face.transform.SetParent(bubbleRect, false);
        RectTransform faceRect = face.GetComponent<RectTransform>();
        Stretch(
            faceRect,
            new Vector2(
                frameStyle != null ? frameStyle.fillInsetLeft : 2f,
                frameStyle != null ? frameStyle.fillInsetBottom : 2f),
            new Vector2(
                -(frameStyle != null ? frameStyle.fillInsetRight : 2f),
                -(frameStyle != null ? frameStyle.fillInsetTop : 2f)));

        bubbleBack = face.AddComponent<Image>();
        bubbleBack.color =
            frameStyle != null
                ? frameStyle.fillColor
                : new Color(0.97f, 0.97f, 0.94f, 1f);
        bubbleBack.raycastTarget = false;

        // Short comic tail preset: targetPivot selects direction only.
        // The tail stays attached to the speech bubble and never stretches to the presenter.
        bubbleTailGraphic =
            bubbleRect.gameObject.AddComponent<BattleSpeechBubbleTailTriangleController>();
        bubbleTailGraphic.Configure(
            bubbleRect,
            tailPivotRect,
            bubbleBack.color,
            presentation != null
                ? presentation.CombatSpeechBubbleTailStyle
                : null);

        GameObject badge = new("ReactionTagBadge", typeof(RectTransform));
        badge.transform.SetParent(bubbleRect, false);
        tagBadgeRect = badge.GetComponent<RectTransform>();
        tagBadgeRect.anchorMin = tagBadgeRect.anchorMax = new Vector2(0f, 1f);
        tagBadgeRect.pivot = new Vector2(0f, 0.5f);
        tagBadgeRect.sizeDelta = new Vector2(205f, 42f);
        tagBadgeRect.anchoredPosition = new Vector2(14f, 8f);
        tagBadgeRect.localRotation = Quaternion.Euler(0f, 0f, 3f);
        Image badgeBack = badge.AddComponent<Image>();
        badgeBack.color = new Color(0.02f, 0.02f, 0.025f, 1f);
        badgeBack.raycastTarget = false;

        tagText = CreateText(
            tagBadgeRect,
            "ReactionTag",
            string.Empty,
            23,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            Color.white);
        Stretch(tagText.rectTransform, new Vector2(8f, 2f), new Vector2(-8f, -2f));

        lineText = CreateText(
            bubbleRect,
            "ReactionLine",
            string.Empty,
            23,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            new Color(0.035f, 0.035f, 0.045f, 1f));

        RectTransform lineRect = lineText.rectTransform;
        lineRect.anchorMin = Vector2.zero;
        lineRect.anchorMax = Vector2.one;
        lineRect.offsetMin = new Vector2(28f, 20f);
        lineRect.offsetMax = new Vector2(-28f, -38f);
        lineText.horizontalOverflow = HorizontalWrapMode.Wrap;
        lineText.verticalOverflow = VerticalWrapMode.Truncate;

        SetPortraitMotion(ScreenPresenterMotionState.Idle);
        HideImmediate();
    }

    private void SetPortraitMotion(ScreenPresenterMotionState state)
    {
        CombatPresenterMotionClip clip =
            presentation != null
                ? presentation.GetCombatPresenterMotion(state)
                : null;

        if ((clip == null || clip.source == null) &&
            presentation != null)
        {
            clip = presentation.GetCombatPresenterMotion(
                ScreenPresenterMotionState.Idle);
        }

        Sprite source = clip != null ? clip.source : null;

        if (activeClip != clip || activeSource != source)
        {
            activeClip = clip;
            activeSource = source;
            BuildRuntimeFrames(source, clip);
        }

        if (portraitImage != null)
        {
            portraitImage.sprite =
                runtimeFrames.Count > 0
                    ? runtimeFrames[0]
                    : source != null
                        ? source
                        : BattleHudSpriteCache.DefaultSprite;

            Material material =
                presentation != null
                    ? presentation.ScreenPresenterMaterial
                    : null;

            if (appliedMaterial != material)
            {
                appliedMaterial = material;
                RefreshRuntimePortraitMaterial(material);
            }
        }
    }

    private void BuildRuntimeFrames(
        Sprite source,
        CombatPresenterMotionClip clip)
    {
        ReleaseRuntimeFrames();
        frameIndex = 0;
        frameTimer = 0f;

        if (source == null || clip == null)
            return;

        int cellWidth = Mathf.Max(1, clip.frameSize.x);
        int cellHeight = Mathf.Max(1, clip.frameSize.y);
        int width = Mathf.RoundToInt(source.rect.width);
        int height = Mathf.RoundToInt(source.rect.height);

        bool sheet =
            width > cellWidth ||
            height > cellHeight;

        if (!sheet)
            return;

        int columns = Mathf.Max(1, width / cellWidth);
        int rows = Mathf.Max(1, height / cellHeight);
        Rect sourceRect = source.rect;

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                float x = sourceRect.x + column * cellWidth;
                float y =
                    sourceRect.y +
                    sourceRect.height -
                    (row + 1) * cellHeight;

                if (x + cellWidth > sourceRect.xMax + 0.01f ||
                    y < sourceRect.y - 0.01f)
                {
                    continue;
                }

                Sprite frame = Sprite.Create(
                    source.texture,
                    new Rect(x, y, cellWidth, cellHeight),
                    new Vector2(0.5f, 0.5f),
                    source.pixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect);

                frame.name =
                    $"{source.name}_CombatReaction_{row:00}_{column:00}";
                frame.hideFlags = HideFlags.HideAndDontSave;
                runtimeFrames.Add(frame);
            }
        }
    }

    private void UpdatePortraitAnimation()
    {
        if (portraitImage == null ||
            runtimeFrames.Count <= 1 ||
            popupPhase == PopupPhase.Hidden)
        {
            return;
        }

        float fps =
            activeClip != null
                ? Mathf.Max(1f, activeClip.fps)
                : 10f;

        float frameDuration = 1f / fps;
        frameTimer += Time.unscaledDeltaTime;

        while (frameTimer >= frameDuration)
        {
            frameTimer -= frameDuration;
            frameIndex = (frameIndex + 1) % runtimeFrames.Count;
        }

        portraitImage.sprite = runtimeFrames[frameIndex];
    }

    private void ReleaseRuntimeFrames()
    {
        for (int i = 0; i < runtimeFrames.Count; i++)
        {
            if (runtimeFrames[i] != null)
                Destroy(runtimeFrames[i]);
        }

        runtimeFrames.Clear();
    }

    private void RefreshRuntimePortraitMaterial(Material source)
    {
        ReleaseRuntimePortraitMaterial();

        if (portraitImage == null)
            return;

        if (source == null)
        {
            portraitImage.material = null;
            return;
        }

        runtimePortraitMaterial = new Material(source)
        {
            name = source.name + "_CombatReactionRuntime",
            hideFlags = HideFlags.HideAndDontSave
        };

        portraitImage.material = runtimePortraitMaterial;
        ApplyGlitch(0f, 0f, 0f);
    }

    private void ReleaseRuntimePortraitMaterial()
    {
        if (runtimePortraitMaterial != null)
            Destroy(runtimePortraitMaterial);

        runtimePortraitMaterial = null;
    }

    private void ApplyGlitch(float glitch, float noise, float rgbSplit)
    {
        if (runtimePortraitMaterial == null)
            return;

        SetMaterialFloatIfPresent(
            runtimePortraitMaterial,
            glitchStrengthProperty,
            Mathf.Max(0f, glitch));

        SetMaterialFloatIfPresent(
            runtimePortraitMaterial,
            noiseStrengthProperty,
            Mathf.Max(0f, noise));

        SetMaterialFloatIfPresent(
            runtimePortraitMaterial,
            rgbSplitProperty,
            Mathf.Max(0f, rgbSplit));
    }

    private static void SetMaterialFloatIfPresent(
        Material material,
        string propertyName,
        float value)
    {
        if (material == null ||
            string.IsNullOrWhiteSpace(propertyName) ||
            !material.HasProperty(propertyName))
        {
            return;
        }

        material.SetFloat(propertyName, value);
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    private static float EaseOutBackSoft(float t)
    {
        t = Mathf.Clamp01(t);
        const float overshoot = 1.35f;
        float x = t - 1f;
        return 1f + (overshoot + 1f) * x * x * x + overshoot * x * x;
    }

    private static void Stretch(
        RectTransform rect,
        Vector2 minOffset,
        Vector2 maxOffset)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = minOffset;
        rect.offsetMax = maxOffset;
    }

    private string PickLine(string key, params string[] lines)
    {
        if (lines == null || lines.Length == 0)
            return string.Empty;

        if (lines.Length == 1)
            return lines[0];

        int previous =
            lastLineIndex.TryGetValue(key, out int remembered)
                ? remembered
                : -1;

        int pick = Random.Range(0, lines.Length - 1);
        if (previous >= 0 && pick >= previous)
            pick++;

        pick = Mathf.Clamp(pick, 0, lines.Length - 1);
        lastLineIndex[key] = pick;
        return lines[pick];
    }

    private static Text CreateText(
        Transform parent,
        string objectName,
        string value,
        int size,
        FontStyle fontStyle,
        TextAnchor alignment,
        Color color)
    {
        GameObject go =
            new(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Text text = go.AddComponent<Text>();
        text.font =
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }
}

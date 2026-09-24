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
        Entering,
        Holding,
        Exiting
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
    [SerializeField] private Vector2 popupSize = new(500f, 190f);
    [SerializeField] private Vector2 popupVisibleOffset = new(-28f, -28f);
    [SerializeField, Min(0f)] private float popupHiddenOffsetX = 540f;

    [Header("Popup Timing")]
    [SerializeField, Min(100f)] private float slideSpeedPixels = 1750f;
    [SerializeField, Range(0.4f, 3f)] private float holdSeconds = 1.35f;

    [Header("Portrait")]
    [SerializeField] private Vector2 portraitSize = new(205f, 165f);

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
    private Image popupBack;
    private Image portraitImage;
    private Text tagText;
    private Text lineText;

    private PopupPhase popupPhase = PopupPhase.Hidden;
    private float holdUntil;
    private ReactionPriority activePriority;

    private readonly List<Sprite> runtimeFrames = new();
    private CombatPresenterMotionClip activeClip;
    private Sprite activeSource;
    private int frameIndex;
    private float frameTimer;
    private Material appliedMaterial;

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
        tagText.text = tag ?? string.Empty;
        lineText.text = line ?? string.Empty;
        SetPortraitMotion(motion);

        if (popupPhase == PopupPhase.Hidden ||
            popupPhase == PopupPhase.Exiting)
        {
            popupRect.anchoredPosition =
                popupVisibleOffset + Vector2.right * popupHiddenOffsetX;
            popupGroup.alpha = 1f;
            popupPhase = PopupPhase.Entering;
        }
        else
        {
            popupRect.anchoredPosition = popupVisibleOffset;
            popupGroup.alpha = 1f;
            popupPhase = PopupPhase.Holding;
            holdUntil = Time.unscaledTime + Mathf.Max(0.4f, holdSeconds);
        }
    }

    private void UpdatePopup()
    {
        if (popupRect == null || popupGroup == null)
            return;

        float step = Mathf.Max(100f, slideSpeedPixels) * Time.unscaledDeltaTime;

        switch (popupPhase)
        {
            case PopupPhase.Hidden:
                popupGroup.alpha = 0f;
                break;

            case PopupPhase.Entering:
                popupGroup.alpha = 1f;
                popupRect.anchoredPosition = Vector2.MoveTowards(
                    popupRect.anchoredPosition,
                    popupVisibleOffset,
                    step);

                if (Vector2.SqrMagnitude(
                        popupRect.anchoredPosition - popupVisibleOffset) < 0.25f)
                {
                    popupRect.anchoredPosition = popupVisibleOffset;
                    popupPhase = PopupPhase.Holding;
                    holdUntil =
                        Time.unscaledTime + Mathf.Max(0.4f, holdSeconds);
                }
                break;

            case PopupPhase.Holding:
                popupRect.anchoredPosition = popupVisibleOffset;
                if (Time.unscaledTime >= holdUntil)
                    popupPhase = PopupPhase.Exiting;
                break;

            case PopupPhase.Exiting:
            {
                Vector2 hidden =
                    popupVisibleOffset + Vector2.right * popupHiddenOffsetX;

                popupRect.anchoredPosition = Vector2.MoveTowards(
                    popupRect.anchoredPosition,
                    hidden,
                    step);

                if (Vector2.SqrMagnitude(
                        popupRect.anchoredPosition - hidden) < 0.25f)
                {
                    HideImmediate();
                }
                break;
            }
        }
    }

    private void HideImmediate()
    {
        if (popupRect != null)
        {
            popupRect.anchoredPosition =
                popupVisibleOffset + Vector2.right * popupHiddenOffsetX;
        }

        if (popupGroup != null)
            popupGroup.alpha = 0f;

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
        popupRect.anchoredPosition =
            popupVisibleOffset + Vector2.right * popupHiddenOffsetX;

        popupBack = popup.AddComponent<Image>();
        popupBack.color = new Color(0.015f, 0.018f, 0.028f, 0.93f);
        popupBack.raycastTarget = false;

        popupGroup = popup.AddComponent<CanvasGroup>();
        popupGroup.alpha = 0f;
        popupGroup.interactable = false;
        popupGroup.blocksRaycasts = false;

        GameObject portrait = new("PresenterPortrait", typeof(RectTransform));
        portrait.transform.SetParent(popupRect, false);
        RectTransform portraitRect = portrait.GetComponent<RectTransform>();
        portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(1f, 0.5f);
        portraitRect.pivot = new Vector2(1f, 0.5f);
        portraitRect.sizeDelta = portraitSize;
        portraitRect.anchoredPosition = new Vector2(-10f, 0f);

        portraitImage = portrait.AddComponent<Image>();
        portraitImage.preserveAspect = true;
        portraitImage.raycastTarget = false;
        portraitImage.sprite = BattleHudSpriteCache.DefaultSprite;

        tagText = CreateText(
            popupRect,
            "ReactionTag",
            string.Empty,
            24,
            FontStyle.Bold,
            TextAnchor.UpperLeft,
            new Color(1f, 0.82f, 0.10f, 1f));

        RectTransform tagRect = tagText.rectTransform;
        tagRect.anchorMin = new Vector2(0f, 1f);
        tagRect.anchorMax = new Vector2(1f, 1f);
        tagRect.pivot = new Vector2(0f, 1f);
        tagRect.offsetMin = new Vector2(20f, -62f);
        tagRect.offsetMax = new Vector2(-(portraitSize.x + 18f), -14f);

        lineText = CreateText(
            popupRect,
            "ReactionLine",
            string.Empty,
            20,
            FontStyle.Bold,
            TextAnchor.MiddleLeft,
            Color.white);

        RectTransform lineRect = lineText.rectTransform;
        lineRect.anchorMin = new Vector2(0f, 0f);
        lineRect.anchorMax = new Vector2(1f, 1f);
        lineRect.offsetMin = new Vector2(20f, 18f);
        lineRect.offsetMax = new Vector2(-(portraitSize.x + 18f), -68f);
        lineText.horizontalOverflow = HorizontalWrapMode.Wrap;
        lineText.verticalOverflow = VerticalWrapMode.Truncate;

        SetPortraitMotion(ScreenPresenterMotionState.Idle);
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
                portraitImage.material = material;
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

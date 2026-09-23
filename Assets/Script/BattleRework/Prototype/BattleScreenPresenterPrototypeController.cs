using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Disposable prototype for a separate presenter feed inside the shared TV.
/// Existing Reward/Map owners remain authoritative; this only renders commentary.
/// Remove this file and the four Notify... call sites to remove the experiment.
/// </summary>
[DefaultExecutionOrder(70000)]
[DisallowMultipleComponent]
public sealed class BattleScreenPresenterPrototypeController : MonoBehaviour
{
    private enum Mode { None, Reward, Map }
    private enum Mood { Neutral, Curious, Excited, Concerned }

    private const string RootName = "ScreenPresenterPrototypeCanvas";
    private const string SharedFrameName = "PrizeSelectionScreen";
    private const string ScreenInnerName = "ScreenInner";

    private static BattleScreenPresenterPrototypeController instance;

    [Header("Prototype Layout")]
    [SerializeField] private Vector2 panelSize = new(430f, 190f);
    [SerializeField] private Vector2 visiblePosition = new(-24f, -84f);
    [SerializeField, Min(0f)] private float hiddenOffsetX = 42f;
    [SerializeField, Min(0.01f)] private float showDelay = 0.16f;
    [SerializeField, Min(0.1f)] private float fadeSharpness = 12f;
    [SerializeField, Range(1f, 1.25f)] private float showCameraZoomOut = 1.08f;
    [SerializeField, Min(0.05f)] private float cameraSettleDuration = 0.32f;

    [Header("Prototype Theme")]
    [SerializeField] private Color panelColor = new(0.025f, 0.03f, 0.04f, 0.94f);
    [SerializeField] private Color panelOutline = new(0.88f, 0.91f, 0.96f, 0.78f);
    [SerializeField] private Color accent = new(1f, 0.20f, 0.42f, 1f);
    [SerializeField] private Color liveAccent = new(1f, 0.82f, 0.12f, 1f);
    [SerializeField] private Color textColor = new(0.96f, 0.97f, 1f, 1f);
    [SerializeField] private Color mutedText = new(0.66f, 0.70f, 0.78f, 1f);

    private BattleRunManager runManager;
    private BattleShowWorldSetController showWorld;
    private BattleRunState lastState = (BattleRunState)(-1);
    private Mode mode;

    private RectTransform screenInner;
    private RectTransform root;
    private CanvasGroup group;
    private Image portrait;
    private RectTransform portraitRect;
    private Text headerText;
    private Text keywordText;
    private Text commentText;
    private Text reactionText;

    private float visibleBlend;
    private float showAt;
    private float reactionStartedAt = -10f;

    private string currentHeader = "LIVE SHOP";
    private string currentKeyword = "TODAY'S PICK";
    private string currentComment = string.Empty;
    private Mood currentMood = Mood.Neutral;
    private bool requestedVisible;
    private bool cameraApplied;
    private float cameraSettleUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleScreenPresenterPrototypeController>() != null)
            return;

        GameObject host = new("BattleScreenPresenterPrototypeRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleScreenPresenterPrototypeController>();
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
        RestoreCamera();
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        ResolveReferences();

        BattleRunState state = runManager != null ? runManager.State : (BattleRunState)(-1);
        if (state != lastState)
        {
            lastState = state;
            HandleState(state);
        }

        if (mode != Mode.None)
        {
            EnsureView();
            UpdatePortrait();
            UpdatePrototypeCamera();
        }

        UpdateMotion();
    }

    public static void NotifyRewardHover(BattleEquipmentSO equipment)
    {
        Resolve()?.ShowReward(equipment, false);
    }

    public static void NotifyRewardSelected(BattleEquipmentSO equipment)
    {
        Resolve()?.ShowReward(equipment, true);
    }

    public static void NotifyRewardConfirm(BattleEquipmentSO equipment)
    {
        BattleScreenPresenterPrototypeController owner = Resolve();
        if (owner == null || equipment == null)
            return;

        owner.SetCopy(
            "SOLD / INSTALLING",
            $"{equipment.rarity.ToString().ToUpperInvariant()}  /  {equipment.GetDisplayName()}",
            "좋아요, 그걸로 가죠. 장착되는 모습까지 한번 보시죠!",
            Mood.Excited);
    }

    public static void NotifyMapHover(BattleNodeData node, int stars)
    {
        Resolve()?.ShowMap(node, stars);
    }

    public static void NotifyMapConfirm(BattleNodeData node, int stars)
    {
        BattleScreenPresenterPrototypeController owner = Resolve();
        if (owner == null || node == null)
            return;

        owner.SetCopy(
            "NEXT COURSE",
            $"ROUTE LOCKED  /  STAGE {Mathf.Max(1, node.depth + 1):00}",
            "좋습니다! 다음 방송 코스, 이쪽으로 가보죠!",
            Mood.Excited);
    }

    private static BattleScreenPresenterPrototypeController Resolve()
    {
        if (instance != null)
            return instance;

        instance = FindFirstObjectByType<BattleScreenPresenterPrototypeController>();
        if (instance != null)
            return instance;

        GameObject host = new("BattleScreenPresenterPrototypeRuntime");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<BattleScreenPresenterPrototypeController>();
        return instance;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (showWorld == null)
            showWorld = FindFirstObjectByType<BattleShowWorldSetController>();
    }

    private void HandleState(BattleRunState state)
    {
        RestoreCamera();

        if (state == BattleRunState.Reward)
        {
            mode = Mode.Reward;
            BeginShow();
            SetCopy(
                "LIVE SHOP",
                "TODAY'S PICK",
                "자, 오늘 들어온 물건들을 한번 살펴볼까요?",
                Mood.Neutral);
            return;
        }

        if (state == BattleRunState.SelectingNode)
        {
            mode = Mode.Map;
            BeginShow();
            SetCopy(
                "ROUTE DESK",
                "NEXT STAGE",
                "다음 코스를 정할 시간이에요. 어느 쪽이 더 그림이 좋을까요?",
                Mood.Curious);
            return;
        }

        mode = Mode.None;
        requestedVisible = false;
    }

    private void BeginShow()
    {
        requestedVisible = false;
        showAt = Time.unscaledTime + showDelay;
        cameraSettleUntil = Time.unscaledTime + cameraSettleDuration;
    }

    private void EnsureView()
    {
        RectTransform resolved = ResolveScreenInner();
        if (resolved == null)
            return;

        if (screenInner != resolved)
        {
            if (root != null)
                Destroy(root.gameObject);

            screenInner = resolved;
            root = null;
        }

        if (root != null)
        {
            root.SetAsLastSibling();
            return;
        }

        GameObject rootObject = new(RootName, typeof(RectTransform));
        rootObject.transform.SetParent(screenInner, false);

        root = rootObject.GetComponent<RectTransform>();
        root.anchorMin = root.anchorMax = new Vector2(1f, 1f);
        root.pivot = new Vector2(1f, 1f);
        root.sizeDelta = panelSize;
        root.anchoredPosition = visiblePosition + Vector2.right * hiddenOffsetX;

        // Nested under the existing World-Space TV canvas: visually separate, same physical screen.
        rootObject.AddComponent<Canvas>();

        Image back = rootObject.AddComponent<Image>();
        back.color = panelColor;
        back.raycastTarget = false;

        Outline outline = rootObject.AddComponent<Outline>();
        outline.effectColor = panelOutline;
        outline.effectDistance = new Vector2(2f, -2f);

        group = rootObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        AddAccentBar();
        AddHeader();
        AddCopy();
        AddPortrait();
        ApplyCachedCopy();

        root.SetAsLastSibling();
    }

    private void AddAccentBar()
    {
        GameObject bar = new("AccentBar", typeof(RectTransform));
        bar.transform.SetParent(root, false);

        RectTransform rect = bar.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, 5f);

        Image image = bar.AddComponent<Image>();
        image.color = accent;
        image.raycastTarget = false;
    }

    private void AddHeader()
    {
        headerText = CreateText(root, "Header", "LIVE SHOP", 15, FontStyle.Bold, TextAnchor.MiddleLeft, liveAccent);
        Place(headerText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(245f, 24f));
    }

    private void AddCopy()
    {
        keywordText = CreateText(root, "Keyword", "TODAY'S PICK", 12, FontStyle.Bold, TextAnchor.UpperLeft, liveAccent);
        Place(keywordText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -48f), new Vector2(250f, 28f));

        commentText = CreateText(root, "Comment", string.Empty, 15, FontStyle.Normal, TextAnchor.UpperLeft, textColor);
        Place(commentText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -78f), new Vector2(250f, 86f));
        commentText.horizontalOverflow = HorizontalWrapMode.Wrap;
        commentText.verticalOverflow = VerticalWrapMode.Truncate;
        commentText.lineSpacing = 1.05f;

        Text footer = CreateText(root, "Footer", "ON AIR  •  MAHO SHOPPING CHANNEL", 9, FontStyle.Normal, TextAnchor.LowerLeft, mutedText);
        Place(footer.rectTransform, Vector2.zero, Vector2.zero, new Vector2(18f, 11f), new Vector2(260f, 18f));
    }

    private void AddPortrait()
    {
        GameObject frameObject = new("PresenterPortraitFrame", typeof(RectTransform));
        frameObject.transform.SetParent(root, false);

        RectTransform frame = frameObject.GetComponent<RectTransform>();
        Place(frame, Vector2.one, Vector2.one, new Vector2(-14f, -28f), new Vector2(138f, 138f));

        Image frameBack = frameObject.AddComponent<Image>();
        frameBack.color = new Color(0.065f, 0.075f, 0.095f, 1f);
        frameBack.raycastTarget = false;

        Outline outline = frameObject.AddComponent<Outline>();
        outline.effectColor = new Color(liveAccent.r, liveAccent.g, liveAccent.b, 0.78f);
        outline.effectDistance = new Vector2(2f, -2f);

        GameObject portraitObject = new("Presenter128", typeof(RectTransform));
        portraitObject.transform.SetParent(frame, false);

        portraitRect = portraitObject.GetComponent<RectTransform>();
        portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(0.5f, 0.5f);
        portraitRect.pivot = new Vector2(0.5f, 0.5f);
        portraitRect.anchoredPosition = Vector2.zero;
        portraitRect.sizeDelta = new Vector2(128f, 128f);

        portrait = portraitObject.AddComponent<Image>();
        portrait.preserveAspect = true;
        portrait.raycastTarget = false;

        reactionText = CreateText(root, "Reaction", "ON AIR", 10, FontStyle.Bold, TextAnchor.MiddleCenter, liveAccent);
        Place(reactionText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 12f), new Vector2(138f, 24f));
    }

    private void UpdateMotion()
    {
        if (mode != Mode.None && !requestedVisible && Time.unscaledTime >= showAt)
            requestedVisible = true;

        float target = requestedVisible && mode != Mode.None && root != null ? 1f : 0f;
        float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, fadeSharpness) * Time.unscaledDeltaTime);
        visibleBlend = Mathf.Lerp(visibleBlend, target, t);

        if (Mathf.Abs(visibleBlend - target) < 0.001f)
            visibleBlend = target;

        if (group != null)
            group.alpha = visibleBlend;

        if (root != null)
        {
            float eased = visibleBlend * visibleBlend * (3f - 2f * visibleBlend);
            root.anchoredPosition = Vector2.Lerp(
                visiblePosition + Vector2.right * hiddenOffsetX,
                visiblePosition,
                eased);
        }

        if (portraitRect != null)
        {
            float age = Time.unscaledTime - reactionStartedAt;
            float pulse = age >= 0f && age < 0.32f
                ? Mathf.Sin(age / 0.32f * Mathf.PI) * 0.075f
                : 0f;
            portraitRect.localScale = Vector3.one * (1f + pulse);
        }
    }

    private void UpdatePrototypeCamera()
    {
        if (showWorld == null || mode == Mode.None || !showWorld.HasCameraAnchor)
            return;

        if (cameraApplied && Time.unscaledTime > cameraSettleUntil)
            return;

        // Reset to the authored shared framing first so the multiplier never compounds.
        showWorld.RecomputeSharedCameraFrame();
        showWorld.OverrideShowCameraFrame(
            showWorld.CameraTargetWorld,
            showWorld.ShowCameraSize * Mathf.Max(1f, showCameraZoomOut));
        cameraApplied = true;
    }

    private void RestoreCamera()
    {
        if (!cameraApplied)
            return;

        if (showWorld != null)
            showWorld.RecomputeSharedCameraFrame();

        cameraApplied = false;
    }

    private void UpdatePortrait()
    {
        if (portrait == null)
            return;

        Sprite sprite = null;

        if (showWorld != null && showWorld.PresenterWorldTransform != null)
        {
            SpriteRenderer renderer = showWorld.PresenterWorldTransform.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = showWorld.PresenterWorldTransform.GetComponentInChildren<SpriteRenderer>(true);
            if (renderer != null)
                sprite = renderer.sprite;
        }

        if (sprite == null)
        {
            Image[] images = FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image != null && image.name == "Presenter" && image.sprite != null)
                {
                    sprite = image.sprite;
                    break;
                }
            }
        }

        portrait.sprite = sprite;
        portrait.enabled = sprite != null;
    }

    private void ShowReward(BattleEquipmentSO equipment, bool selected)
    {
        if (equipment == null)
            return;

        string name = equipment.GetDisplayName();
        string lower = string.IsNullOrEmpty(name) ? string.Empty : name.ToLowerInvariant();

        if (lower.Contains("톱") || lower.Contains("saw"))
        {
            SetCopy(selected ? "PICKED" : "CURIOUS PICK", "ODD WEAPON  /  MULTI HIT",
                "그래요, 이 톱날은 꽤나 컬트한 매력이 있을지도요!?", Mood.Curious);
            return;
        }

        if (equipment.HasTag(EquipmentTag.OddWeapon))
        {
            SetCopy(selected ? "PICKED" : "CULT PICK", "ODD WEAPON",
                "호불호는 좀 있겠지만요. 이런 물건을 좋아하는 분은 정말 좋아하시겠네요?", Mood.Curious);
            return;
        }

        if (equipment.rarity == EquipmentRarity.Unique || equipment.rarity == EquipmentRarity.Epic)
        {
            SetCopy(selected ? "PICKED" : "SPECIAL ITEM",
                $"{equipment.rarity.ToString().ToUpperInvariant()}  /  {name}",
                "오, 이건 화면에 잡힐 만하네요. 오늘 상품 중에서는 확실히 눈에 띕니다!", Mood.Excited);
            return;
        }

        if (equipment.HasTag(EquipmentTag.Explosion) || equipment.HasTag(EquipmentTag.Burn))
        {
            SetCopy(selected ? "PICKED" : "HOT ITEM", "EXPLOSIVE  /  PRESSURE",
                "이건 설명이 필요 없겠네요. 화끈한 쪽을 좋아하신다면 꽤 괜찮은 선택이에요!", Mood.Excited);
            return;
        }

        if (equipment.HasTag(EquipmentTag.Heal) ||
            equipment.HasTag(EquipmentTag.Defense) ||
            equipment.HasTag(EquipmentTag.Sustain))
        {
            SetCopy(selected ? "PICKED" : "SAFE PICK", "SURVIVAL  /  STABILITY",
                "화려하진 않아도 오래 살아남는 건 꽤 중요한 일이죠. 안정적인 상품입니다.", Mood.Neutral);
            return;
        }

        SetCopy(
            selected ? "PICKED" : "ITEM CHECK",
            $"{equipment.rarity.ToString().ToUpperInvariant()}  /  {name}",
            selected ? "좋아요. 일단 이쪽을 좀 더 자세히 보죠."
                     : "음, 무난해 보이지만 조합에 따라 제법 재미있는 그림이 나올지도요.",
            selected ? Mood.Excited : Mood.Neutral);
    }

    private void ShowMap(BattleNodeData node, int stars)
    {
        if (node == null)
            return;

        string stage = $"STAGE {Mathf.Max(1, node.depth + 1):00}  /  {Mathf.Clamp(stars, 1, 5)} STAR";

        switch (node.type)
        {
            case BattleNodeType.Elite:
                SetCopy("CAUTION", $"{stage}  /  ELITE",
                    "조금 거친 코스네요. 대신 방송 분량은 확실하겠어요.", Mood.Concerned);
                break;

            case BattleNodeType.Shop:
                SetCopy("SHOPPING BREAK", $"{stage}  /  SHOP",
                    "잠깐 쇼핑 타임이군요. 다음 싸움 전에 지갑부터 한번 열어볼까요?", Mood.Curious);
                break;

            case BattleNodeType.Event:
                SetCopy("SPECIAL SEGMENT", $"{stage}  /  EVENT",
                    "이쪽은 무슨 일이 나올지 모르겠네요. 방송적으로는 꽤 흥미롭겠어요.", Mood.Curious);
                break;

            default:
                SetCopy("COURSE CHECK", $"{stage}  /  COMBAT",
                    stars >= 4
                        ? "난도가 꽤 높네요. 그래도 이 정도는 가야 그림이 나오겠죠?"
                        : "정석적인 코스네요. 다음 전투를 보기엔 무난한 선택입니다.",
                    stars >= 4 ? Mood.Concerned : Mood.Neutral);
                break;
        }
    }

    private void SetCopy(string header, string keyword, string comment, Mood mood)
    {
        currentHeader = header ?? string.Empty;
        currentKeyword = keyword ?? string.Empty;
        currentComment = comment ?? string.Empty;
        currentMood = mood;

        EnsureView();
        ApplyCachedCopy();
        reactionStartedAt = Time.unscaledTime;
    }

    private void ApplyCachedCopy()
    {
        if (headerText != null)
            headerText.text = currentHeader;
        if (keywordText != null)
            keywordText.text = currentKeyword;
        if (commentText != null)
            commentText.text = currentComment;

        if (reactionText == null)
            return;

        reactionText.text = currentMood switch
        {
            Mood.Curious => "CURIOUS",
            Mood.Excited => "WOW!",
            Mood.Concerned => "CAUTION",
            _ => "ON AIR"
        };

        reactionText.color = currentMood == Mood.Concerned
            ? new Color(1f, 0.56f, 0.22f, 1f)
            : currentMood == Mood.Excited
                ? accent
                : liveAccent;
    }

    private RectTransform ResolveScreenInner()
    {
        if (showWorld != null && showWorld.MountedTvRect != null)
        {
            Transform frame = FindChildRecursive(showWorld.MountedTvRect, SharedFrameName);
            if (frame != null && frame.Find(ScreenInnerName) is RectTransform inner)
                return inner;
        }

        RectTransform[] rects = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect != null && rect.name == SharedFrameName && rect.Find(ScreenInnerName) is RectTransform inner)
                return inner;
        }

        return null;
    }

    private static Transform FindChildRecursive(Transform rootTransform, string childName)
    {
        if (rootTransform == null)
            return null;
        if (rootTransform.name == childName)
            return rootTransform;

        for (int i = 0; i < rootTransform.childCount; i++)
        {
            Transform result = FindChildRecursive(rootTransform.GetChild(i), childName);
            if (result != null)
                return result;
        }

        return null;
    }

    private static Text CreateText(
        Transform parent,
        string objectName,
        string value,
        int size,
        FontStyle style,
        TextAnchor alignment,
        Color color)
    {
        GameObject go = new(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Text text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void Place(
        RectTransform rect,
        Vector2 anchor,
        Vector2 pivot,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}

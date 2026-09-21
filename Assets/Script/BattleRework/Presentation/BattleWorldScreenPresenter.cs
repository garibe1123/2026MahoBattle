using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public enum BattleWorldScreenProgram
{
    Hidden,
    Reward,
    Map
}

public enum BattleProgramTransitionState
{
    Stable,
    Entering,
    Switching,
    Exiting
}

/// <summary>
/// 하나의 World-Space TV Canvas 안에서 Reward/Map Program만 전환합니다.
/// TV 물성은 BattleTVSurfaceController가 소유하고, 이 클래스는 Program layer/transition/input gate만 소유합니다.
/// </summary>
[DefaultExecutionOrder(20500)]
[DisallowMultipleComponent]
public sealed class BattleWorldScreenPresenter : MonoBehaviour
{
    [Header("Program Transition")]
    [SerializeField, Min(0.05f)] private float programTweenDuration = 0.34f;
    [SerializeField] private Ease programEase = Ease.OutCubic;
    [SerializeField] private bool useUnscaledTime = true;

    private Canvas rootCanvas;
    private BattleTVSurfaceController tvSurface;

    private RectTransform physicalScreenSurface;
    private RectTransform backLayer;
    private RectTransform mainLayer;
    private RectTransform frontLayer;
    private RectTransform fxLayer;

    private RectTransform rewardProgram;
    private RectTransform mapProgram;
    private BattleSpatialUIElement rewardProgramElement;
    private BattleSpatialUIElement mapProgramElement;

    private BattleUIThemeController themeController;
    private BattleWorldScreenProgram currentProgram = BattleWorldScreenProgram.Hidden;
    private BattleProgramTransitionState transitionState = BattleProgramTransitionState.Stable;
    private Sequence programTransition;

    public BattleWorldScreenProgram CurrentProgram => currentProgram;
    public BattleProgramTransitionState TransitionState => transitionState;
    public bool CanReceiveProgramInput => transitionState == BattleProgramTransitionState.Stable;

    public RectTransform GetBackLayer() => backLayer;
    public RectTransform GetMainLayer() => mainLayer;
    public RectTransform GetFrontLayer() => frontLayer;
    public RectTransform GetFxLayer() => fxLayer;

    // Legacy aliases: 기존 호출부를 깨지 않고 새 4-layer 규약으로 수렴시킵니다.
    public RectTransform GetFloatingLayer() => frontLayer;
    public RectTransform GetFocusLayer() => frontLayer;
    public RectTransform GetInteractionLayer() => fxLayer;

    private void Awake()
    {
        rootCanvas = GetComponent<Canvas>();
        tvSurface = GetComponent<BattleTVSurfaceController>();
        if (tvSurface == null)
            tvSurface = gameObject.AddComponent<BattleTVSurfaceController>();

        BuildLayers();
        ResolveTheme();
        DiscoverProgramRoots();
    }

    private void OnEnable()
    {
        BuildLayers();
        ResolveTheme();
        DiscoverProgramRoots();
        ApplyThemeOverride();
        tvSurface?.SetState(BattleTVState.Live, true);
    }

    private void OnDisable()
    {
        KillProgramTransition();
        if (themeController != null)
            themeController.ClearContextOverride(this);
    }

    private void OnDestroy()
    {
        KillProgramTransition();
        if (themeController != null)
            themeController.ClearContextOverride(this);
    }

    private void LateUpdate()
    {
        if (rootCanvas == null)
            rootCanvas = GetComponent<Canvas>();

        EnsureRootInteraction();

        if (rewardProgram == null || mapProgram == null)
            DiscoverProgramRoots();

        if (themeController == null)
        {
            ResolveTheme();
            ApplyThemeOverride();
        }

        tvSurface?.RefreshFromCanvas();
    }

    public void BindProgramRoots(RectTransform rewardRoot, RectTransform mapRoot)
    {
        rewardProgram = rewardRoot;
        mapProgram = mapRoot;
        rewardProgramElement = EnsureProgramElement(rewardProgram);
        mapProgramElement = EnsureProgramElement(mapProgram);
        ApplyCurrentProgram(true);
    }

    public void ShowReward() => SetProgram(BattleWorldScreenProgram.Reward);
    public void ShowMap() => SetProgram(BattleWorldScreenProgram.Map);
    public void HideContent() => SetProgram(BattleWorldScreenProgram.Hidden);

    public void CollapseContent()
    {
        BattleSpatialUIElement active = GetElement(currentProgram);
        if (active == null)
            return;

        transitionState = BattleProgramTransitionState.Exiting;
        active.SetState(SpatialUIState.Exiting);
    }

    private void SetProgram(BattleWorldScreenProgram next)
    {
        DiscoverProgramRoots();

        if (currentProgram == next && transitionState == BattleProgramTransitionState.Stable)
            return;

        BattleWorldScreenProgram previous = currentProgram;
        currentProgram = next;
        ApplyThemeOverride();

        if (!Application.isPlaying)
        {
            ApplyCurrentProgram(true);
            return;
        }

        BeginProgramTransition(previous, next);
    }

    private void BeginProgramTransition(BattleWorldScreenProgram previous, BattleWorldScreenProgram next)
    {
        KillProgramTransition();

        BattleSpatialUIElement outgoing = GetElement(previous);
        BattleSpatialUIElement incoming = GetElement(next);

        if (previous == BattleWorldScreenProgram.Hidden && next != BattleWorldScreenProgram.Hidden)
            transitionState = BattleProgramTransitionState.Entering;
        else if (next == BattleWorldScreenProgram.Hidden)
            transitionState = BattleProgramTransitionState.Exiting;
        else
            transitionState = BattleProgramTransitionState.Switching;

        SetProgramRaycast(rewardProgram, false);
        SetProgramRaycast(mapProgram, false);

        if (outgoing != null && previous != next)
        {
            outgoing.gameObject.SetActive(true);
            outgoing.SetState(SpatialUIState.Exiting);
        }

        if (incoming != null)
        {
            incoming.gameObject.SetActive(true);
            incoming.SnapToState(SpatialUIState.Entering);
            incoming.SetState(SpatialUIState.Focused);
        }

        tvSurface?.PlayProgramSwitchPulse();

        programTransition = DOTween.Sequence();
        programTransition.SetUpdate(useUnscaledTime);
        programTransition.AppendInterval(Mathf.Max(0.05f, programTweenDuration));
        programTransition.OnComplete(() =>
        {
            if (previous != next)
                SetProgramActive(previous, false);

            SetProgramActive(next, next != BattleWorldScreenProgram.Hidden);
            SetProgramRaycast(rewardProgram, next == BattleWorldScreenProgram.Reward);
            SetProgramRaycast(mapProgram, next == BattleWorldScreenProgram.Map);
            transitionState = BattleProgramTransitionState.Stable;
            tvSurface?.SetState(next == BattleWorldScreenProgram.Hidden ? BattleTVState.Off : BattleTVState.Live);
            programTransition = null;
        });
        programTransition.OnKill(() => programTransition = null);
    }

    private void ApplyCurrentProgram(bool immediate)
    {
        ApplyElementState(rewardProgramElement,
            currentProgram == BattleWorldScreenProgram.Reward ? SpatialUIState.Focused : SpatialUIState.Hidden,
            immediate);
        ApplyElementState(mapProgramElement,
            currentProgram == BattleWorldScreenProgram.Map ? SpatialUIState.Focused : SpatialUIState.Hidden,
            immediate);

        SetProgramActive(BattleWorldScreenProgram.Reward, currentProgram == BattleWorldScreenProgram.Reward);
        SetProgramActive(BattleWorldScreenProgram.Map, currentProgram == BattleWorldScreenProgram.Map);
        SetProgramRaycast(rewardProgram, currentProgram == BattleWorldScreenProgram.Reward);
        SetProgramRaycast(mapProgram, currentProgram == BattleWorldScreenProgram.Map);

        transitionState = BattleProgramTransitionState.Stable;
    }

    private static void ApplyElementState(BattleSpatialUIElement element, SpatialUIState state, bool immediate)
    {
        if (element == null)
            return;

        element.gameObject.SetActive(true);
        element.SetState(state, immediate);
    }

    private void BuildLayers()
    {
        RectTransform owner = transform as RectTransform;
        if (owner == null)
            return;

        physicalScreenSurface = EnsureLayer(owner, "PhysicalScreenSurface", 12f);
        backLayer = EnsureLayer(owner, "ProgramBackLayer", 6f);
        mainLayer = EnsureLayer(owner, "ProgramMainLayer", 0f);
        frontLayer = EnsureLayer(owner, "ProgramFrontLayer", -8f);
        fxLayer = EnsureLayer(owner, "ProgramFXLayer", -14f);

        MigrateLegacyLayer(owner, "SpatialBackLayer", backLayer);
        MigrateLegacyLayer(owner, "SpatialMainLayer", mainLayer);
        MigrateLegacyLayer(owner, "SpatialFloatingLayer", frontLayer);
        MigrateLegacyLayer(owner, "SpatialFocusLayer", frontLayer);
        MigrateLegacyLayer(owner, "InteractionLayer", fxLayer);

        physicalScreenSurface.SetSiblingIndex(0);
        backLayer.SetSiblingIndex(Mathf.Min(1, owner.childCount - 1));
        mainLayer.SetSiblingIndex(Mathf.Min(2, owner.childCount - 1));
        frontLayer.SetSiblingIndex(Mathf.Min(3, owner.childCount - 1));
        fxLayer.SetAsLastSibling();

        EnsureRootInteraction();
    }

    private static void MigrateLegacyLayer(RectTransform owner, string oldName, RectTransform target)
    {
        Transform legacy = owner.Find(oldName);
        if (legacy == null || legacy == target)
            return;

        while (legacy.childCount > 0)
            legacy.GetChild(0).SetParent(target, false);

        legacy.gameObject.SetActive(false);
    }

    private RectTransform EnsureLayer(RectTransform parent, string layerName, float localZ)
    {
        RectTransform rect = parent.Find(layerName) as RectTransform;
        if (rect == null)
        {
            GameObject go = new(layerName);
            go.transform.SetParent(parent, false);
            rect = go.AddComponent<RectTransform>();
        }

        Stretch(rect);
        Vector3 local = rect.localPosition;
        local.z = localZ;
        rect.localPosition = local;

        GraphicRaycaster nestedRaycaster = rect.GetComponent<GraphicRaycaster>();
        if (nestedRaycaster != null)
        {
            nestedRaycaster.enabled = false;
            if (Application.isPlaying) Destroy(nestedRaycaster);
            else DestroyImmediate(nestedRaycaster);
        }

        Canvas nestedCanvas = rect.GetComponent<Canvas>();
        if (nestedCanvas != null)
        {
            nestedCanvas.enabled = false;
            if (Application.isPlaying) Destroy(nestedCanvas);
            else DestroyImmediate(nestedCanvas);
        }

        return rect;
    }

    private void EnsureRootInteraction()
    {
        if (rootCanvas == null)
            rootCanvas = GetComponent<Canvas>();
        if (rootCanvas == null)
            return;

        if (rootCanvas.renderMode == RenderMode.WorldSpace && rootCanvas.worldCamera != Camera.main)
            rootCanvas.worldCamera = Camera.main;

        GraphicRaycaster raycaster = rootCanvas.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
            raycaster = rootCanvas.gameObject.AddComponent<GraphicRaycaster>();
        raycaster.enabled = true;
    }

    private void DiscoverProgramRoots()
    {
        RectTransform owner = transform as RectTransform;
        if (owner == null)
            return;

        RectTransform reward = FindDescendant(owner, "PrizeSelectionScreen");
        RectTransform map = FindDescendant(owner, "MapSelectionScreen");

        if (reward != null && reward != rewardProgram)
        {
            rewardProgram = reward;
            rewardProgramElement = EnsureProgramElement(rewardProgram);
        }

        if (map != null && map != mapProgram)
        {
            mapProgram = map;
            mapProgramElement = EnsureProgramElement(mapProgram);
        }
    }

    private BattleSpatialUIElement EnsureProgramElement(RectTransform root)
    {
        if (root == null)
            return null;

        BattleSpatialUIElement element = root.GetComponent<BattleSpatialUIElement>();
        if (element == null)
            element = root.gameObject.AddComponent<BattleSpatialUIElement>();

        element.SetTransition(programTweenDuration, programEase, useUnscaledTime);
        element.SetLocalForward(Vector3.back);
        element.SetPose(SpatialUIState.Hidden,
            new SpatialUIPose(new Vector3(0f, -6f, 0f), new Vector3(4f, 0f, 0f),
                new Vector3(0.96f, 0.96f, 1f), 0f, -34f));
        element.SetPose(SpatialUIState.Entering,
            new SpatialUIPose(new Vector3(0f, -3f, 0f), new Vector3(3f, 0f, 0f),
                new Vector3(0.975f, 0.975f, 1f), 0f, -26f));
        element.SetPose(SpatialUIState.Focused,
            new SpatialUIPose(Vector3.zero, Vector3.zero, Vector3.one, 1f, 0f));
        element.SetPose(SpatialUIState.Exiting,
            new SpatialUIPose(new Vector3(0f, 3f, 0f), new Vector3(4f, 0f, 0f),
                new Vector3(0.965f, 0.965f, 1f), 0f, -30f));

        return element;
    }

    private BattleSpatialUIElement GetElement(BattleWorldScreenProgram program)
    {
        return program switch
        {
            BattleWorldScreenProgram.Reward => rewardProgramElement,
            BattleWorldScreenProgram.Map => mapProgramElement,
            _ => null
        };
    }

    private void SetProgramActive(BattleWorldScreenProgram program, bool active)
    {
        RectTransform root = program switch
        {
            BattleWorldScreenProgram.Reward => rewardProgram,
            BattleWorldScreenProgram.Map => mapProgram,
            _ => null
        };

        if (root != null && root.gameObject.activeSelf != active)
            root.gameObject.SetActive(active);
    }

    private static void SetProgramRaycast(RectTransform root, bool enabled)
    {
        if (root == null)
            return;

        CanvasGroup group = root.GetComponent<CanvasGroup>();
        if (group == null)
            group = root.gameObject.AddComponent<CanvasGroup>();

        group.interactable = enabled;
        group.blocksRaycasts = enabled;
    }

    private void ResolveTheme()
    {
        if (themeController != null)
            return;

        themeController = BattleUIThemeController.Instance != null
            ? BattleUIThemeController.Instance
            : FindFirstObjectByType<BattleUIThemeController>(FindObjectsInactive.Include);
    }

    private void ApplyThemeOverride()
    {
        if (themeController == null)
            return;

        switch (currentProgram)
        {
            case BattleWorldScreenProgram.Reward:
                themeController.SetContextOverride(this, BattleUIThemeContext.Reward, 80);
                break;
            case BattleWorldScreenProgram.Map:
                themeController.SetContextOverride(this, BattleUIThemeContext.Map, 80);
                break;
            default:
                themeController.ClearContextOverride(this);
                break;
        }
    }

    private void KillProgramTransition()
    {
        if (programTransition != null)
        {
            programTransition.Kill(false);
            programTransition = null;
        }
        transitionState = BattleProgramTransitionState.Stable;
    }

    private static RectTransform FindDescendant(Transform root, string targetName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == targetName)
                return child as RectTransform;

            RectTransform nested = FindDescendant(child, targetName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }
}

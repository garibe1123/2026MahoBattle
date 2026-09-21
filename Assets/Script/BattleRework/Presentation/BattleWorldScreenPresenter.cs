using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public enum BattleWorldScreenProgram
{
    Hidden,
    Reward,
    Map
}

/// <summary>
/// BattleShowMountedTV 내부의 물리 디스플레이 Presentation 계층을 관리합니다.
/// Reward/Map business state를 변경하지 않고, 동일한 World-Space TV 안의 Program과 Spatial Layer만 관리합니다.
/// </summary>
[DefaultExecutionOrder(20500)]
[DisallowMultipleComponent]
public sealed class BattleWorldScreenPresenter : MonoBehaviour
{
    [Header("Program Transition")]
    [SerializeField, Min(0.05f)] private float programTweenDuration = 0.20f;
    [SerializeField] private Ease programEase = Ease.OutCubic;

    private Canvas rootCanvas;
    private RectTransform physicalScreenSurface;
    private RectTransform backLayer;
    private RectTransform mainLayer;
    private RectTransform floatingLayer;
    private RectTransform focusLayer;
    private RectTransform interactionLayer;

    private RectTransform rewardProgram;
    private RectTransform mapProgram;
    private BattleSpatialUIElement rewardProgramElement;
    private BattleSpatialUIElement mapProgramElement;

    private BattleUIThemeController themeController;
    private BattleWorldScreenProgram currentProgram = BattleWorldScreenProgram.Hidden;

    public BattleWorldScreenProgram CurrentProgram => currentProgram;
    public RectTransform GetBackLayer() => backLayer;
    public RectTransform GetMainLayer() => mainLayer;
    public RectTransform GetFloatingLayer() => floatingLayer;
    public RectTransform GetFocusLayer() => focusLayer;
    public RectTransform GetInteractionLayer() => interactionLayer;

    private void Awake()
    {
        rootCanvas = GetComponent<Canvas>();
        BuildLayers();
        ResolveTheme();
    }

    private void OnEnable()
    {
        BuildLayers();
        ResolveTheme();
        ApplyThemeOverride();
    }

    private void OnDisable()
    {
        if (themeController != null)
            themeController.ClearContextOverride(this);
    }

    private void OnDestroy()
    {
        if (themeController != null)
            themeController.ClearContextOverride(this);
    }

    private void LateUpdate()
    {
        if (rootCanvas == null)
            rootCanvas = GetComponent<Canvas>();

        EnsureRootInteraction();

        if (themeController == null)
        {
            ResolveTheme();
            ApplyThemeOverride();
        }
    }

    public void BindProgramRoots(RectTransform rewardRoot, RectTransform mapRoot)
    {
        rewardProgram = rewardRoot;
        mapProgram = mapRoot;

        rewardProgramElement = EnsureProgramElement(rewardProgram);
        mapProgramElement = EnsureProgramElement(mapProgram);

        ApplyCurrentProgram(false);
    }

    public void ShowReward()
    {
        SetProgram(BattleWorldScreenProgram.Reward);
    }

    public void ShowMap()
    {
        SetProgram(BattleWorldScreenProgram.Map);
    }

    public void HideContent()
    {
        SetProgram(BattleWorldScreenProgram.Hidden);
    }

    private void SetProgram(BattleWorldScreenProgram next)
    {
        if (currentProgram == next)
            return;

        currentProgram = next;
        ApplyThemeOverride();
        ApplyCurrentProgram(false);
    }

    public void CollapseContent()
    {
        BattleSpatialUIElement active = currentProgram switch
        {
            BattleWorldScreenProgram.Reward => rewardProgramElement,
            BattleWorldScreenProgram.Map => mapProgramElement,
            _ => null
        };

        active?.SetState(SpatialUIState.Exiting);
    }

    private void BuildLayers()
    {
        RectTransform owner = transform as RectTransform;
        if (owner == null)
            return;

        // Interaction invariant:
        // Reward / Map buttons must stay under the single BattleShowMountedTV root Canvas.
        // Nested Canvas + separate GraphicRaycaster changes UGUI raycast ownership and breaks
        // the existing Stage Map Button path. Spatial layers are therefore Transform layers,
        // while the root World-Space Canvas remains the sole interactive Canvas.
        physicalScreenSurface = EnsureLayer(owner, "PhysicalScreenSurface", 14f);
        backLayer = EnsureLayer(owner, "SpatialBackLayer", 8f);
        mainLayer = EnsureLayer(owner, "SpatialMainLayer", 0f);
        floatingLayer = EnsureLayer(owner, "SpatialFloatingLayer", -8f);
        focusLayer = EnsureLayer(owner, "SpatialFocusLayer", -16f);
        interactionLayer = EnsureLayer(owner, "InteractionLayer", -20f);

        physicalScreenSurface.SetSiblingIndex(0);
        backLayer.SetSiblingIndex(Mathf.Min(1, owner.childCount - 1));
        mainLayer.SetSiblingIndex(Mathf.Min(2, owner.childCount - 1));
        floatingLayer.SetSiblingIndex(Mathf.Min(3, owner.childCount - 1));
        focusLayer.SetSiblingIndex(Mathf.Min(4, owner.childCount - 1));
        interactionLayer.SetAsLastSibling();

        EnsureRootInteraction();
    }

    private RectTransform EnsureLayer(
        RectTransform parent,
        string layerName,
        float localZ)
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

        // Migration guard for a live/domain-reloaded scene created by the previous revision.
        // Disable nested raycast ownership immediately, then remove those components.
        GraphicRaycaster nestedRaycaster = rect.GetComponent<GraphicRaycaster>();
        if (nestedRaycaster != null)
        {
            nestedRaycaster.enabled = false;
            if (Application.isPlaying)
                Destroy(nestedRaycaster);
            else
                DestroyImmediate(nestedRaycaster);
        }

        Canvas nestedCanvas = rect.GetComponent<Canvas>();
        if (nestedCanvas != null)
        {
            nestedCanvas.enabled = false;
            if (Application.isPlaying)
                Destroy(nestedCanvas);
            else
                DestroyImmediate(nestedCanvas);
        }

        return rect;
    }

    private void EnsureRootInteraction()
    {
        if (rootCanvas == null)
            rootCanvas = GetComponent<Canvas>();
        if (rootCanvas == null)
            return;

        if (rootCanvas.renderMode == RenderMode.WorldSpace &&
            rootCanvas.worldCamera != Camera.main)
        {
            rootCanvas.worldCamera = Camera.main;
        }

        GraphicRaycaster raycaster = rootCanvas.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
            raycaster = rootCanvas.gameObject.AddComponent<GraphicRaycaster>();

        raycaster.enabled = true;
    }

    private BattleSpatialUIElement EnsureProgramElement(RectTransform root)
    {
        if (root == null)
            return null;

        BattleSpatialUIElement element = root.GetComponent<BattleSpatialUIElement>();
        if (element == null)
            element = root.gameObject.AddComponent<BattleSpatialUIElement>();

        element.SetTransition(programTweenDuration, programEase, true);
        element.SetPose(
            SpatialUIState.Hidden,
            new SpatialUIPose(
                new Vector3(0f, -4f, 22f),
                new Vector3(2.8f, 0f, 0f),
                new Vector3(0.97f, 0.97f, 1f),
                0f));
        element.SetPose(
            SpatialUIState.Focused,
            new SpatialUIPose(
                Vector3.zero,
                Vector3.zero,
                Vector3.one,
                1f));
        element.SetPose(
            SpatialUIState.Exiting,
            new SpatialUIPose(
                new Vector3(0f, 3f, 26f),
                new Vector3(-2.2f, 0f, 0f),
                new Vector3(0.965f, 0.965f, 1f),
                0f));

        return element;
    }

    private void ApplyCurrentProgram(bool immediate)
    {
        if (rewardProgramElement != null)
        {
            SpatialUIState state = currentProgram == BattleWorldScreenProgram.Reward
                ? SpatialUIState.Focused
                : SpatialUIState.Hidden;
            rewardProgramElement.SetState(state, immediate);
        }

        if (mapProgramElement != null)
        {
            SpatialUIState state = currentProgram == BattleWorldScreenProgram.Map
                ? SpatialUIState.Focused
                : SpatialUIState.Hidden;
            mapProgramElement.SetState(state, immediate);
        }
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

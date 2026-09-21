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
    private const int BackOffset = 10;
    private const int MainOffset = 20;
    private const int FloatingOffset = 30;
    private const int FocusOffset = 40;
    private const int InteractionOffset = 50;

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

        UpdateSorting();

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

        physicalScreenSurface = EnsureLayer(owner, "PhysicalScreenSurface", 8f, 0, false);
        backLayer = EnsureLayer(owner, "SpatialBackLayer", 14f, BackOffset, false);
        mainLayer = EnsureLayer(owner, "SpatialMainLayer", 0f, MainOffset, true);
        floatingLayer = EnsureLayer(owner, "SpatialFloatingLayer", -8f, FloatingOffset, false);
        focusLayer = EnsureLayer(owner, "SpatialFocusLayer", -16f, FocusOffset, false);
        interactionLayer = EnsureLayer(owner, "InteractionLayer", -20f, InteractionOffset, true);

        physicalScreenSurface.SetSiblingIndex(0);
        backLayer.SetSiblingIndex(Mathf.Min(1, owner.childCount - 1));
        mainLayer.SetSiblingIndex(Mathf.Min(2, owner.childCount - 1));
        floatingLayer.SetSiblingIndex(Mathf.Min(3, owner.childCount - 1));
        focusLayer.SetSiblingIndex(Mathf.Min(4, owner.childCount - 1));
        interactionLayer.SetAsLastSibling();

        UpdateSorting();
    }

    private RectTransform EnsureLayer(
        RectTransform parent,
        string layerName,
        float localZ,
        int sortingOffset,
        bool needsRaycaster)
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

        Canvas canvas = rect.GetComponent<Canvas>();
        if (canvas == null)
            canvas = rect.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;

        if (needsRaycaster && rect.GetComponent<GraphicRaycaster>() == null)
            rect.gameObject.AddComponent<GraphicRaycaster>();

        if (!needsRaycaster)
        {
            GraphicRaycaster raycaster = rect.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
                raycaster.enabled = false;
        }

        if (rootCanvas != null)
        {
            canvas.sortingLayerID = rootCanvas.sortingLayerID;
            canvas.sortingOrder = Mathf.Min(32760, rootCanvas.sortingOrder + sortingOffset);
        }

        return rect;
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

    private void UpdateSorting()
    {
        if (rootCanvas == null)
            return;

        ApplySorting(physicalScreenSurface, 0);
        ApplySorting(backLayer, BackOffset);
        ApplySorting(mainLayer, MainOffset);
        ApplySorting(floatingLayer, FloatingOffset);
        ApplySorting(focusLayer, FocusOffset);
        ApplySorting(interactionLayer, InteractionOffset);
    }

    private void ApplySorting(RectTransform layer, int offset)
    {
        if (layer == null)
            return;

        Canvas canvas = layer.GetComponent<Canvas>();
        if (canvas == null)
            return;

        canvas.sortingLayerID = rootCanvas.sortingLayerID;
        canvas.sortingOrder = Mathf.Min(32760, rootCanvas.sortingOrder + offset);
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

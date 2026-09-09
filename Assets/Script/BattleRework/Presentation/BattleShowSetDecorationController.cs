using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BattleShowSetLayoutSO를 실제 Reward / Map TV 쇼 세트에 적용합니다.
/// 기존 전투 MapBlock/바닥 SO는 변경하지 않고, 이름이 고정된 쇼 Carrier와 런타임 장식 루트만 다룹니다.
/// </summary>
[DefaultExecutionOrder(32600)]
[DisallowMultipleComponent]
public sealed class BattleShowSetDecorationController : MonoBehaviour
{
    private sealed class DecorationBinding
    {
        public GameObject gameObject;
        public BattleShowTileObjectData data;
    }

    private const string StageRootName = "BattleShowDockStage";
    private const string DecorationRootName = "BattleShowSetDecor_Runtime";
    private const string ScreenCarrierName = "ScreenCarrier_10x2";
    private const string PresenterCarrierName = "PresenterCarrier_6x4";
    private const string PresenterObjectName = "PresenterWorldSprite";

    [Header("Show Set Layout")]
    [Tooltip("TV 쇼 세트 전용 SO입니다. 기존 BattleShowFloorTemplateSO와 별개로 사회자/쇼 바닥/외곽 장식만 제어합니다.")]
    [SerializeField] private BattleShowSetLayoutSO showSetLayout;

    [Header("Runtime Refresh")]
    [SerializeField, Range(0.03f, 0.5f)] private float carrierRefreshInterval = 0.10f;

    private BattleRunManager runManager;
    private RoomBaseTemplate baseTemplate;
    private PlayerController player;
    private BattleShowWorldSetController worldSet;

    private Transform stageRoot;
    private Transform decorationRoot;
    private SpriteRenderer presenterRenderer;
    private Animator managedPresenterAnimator;

    private BattleShowSetLayoutSO appliedLayout;
    private Transform appliedStageRoot;
    private float nextCarrierRefresh;
    private float presenterAnimationStart;
    private bool wasReward;

    private readonly List<DecorationBinding> decorationBindings = new();
    private readonly HashSet<int> floorRenderersApplied = new();

    public BattleShowSetLayoutSO ShowSetLayout => showSetLayout;

    public void SetShowSetLayout(BattleShowSetLayoutSO layout, bool refreshNow = true)
    {
        showSetLayout = layout;
        InvalidateLayout();
        if (refreshNow)
            RefreshNow();
    }

    public void RefreshNow()
    {
        ResolveSystems();
        ResolveStage();
        InvalidateLayout();
        if (IsShowState() && showSetLayout != null && stageRoot != null)
        {
            RebuildDecorationRoot();
            ApplyShowFloorToCarriers();
            UpdateDecorationVisibility();
            UpdatePresenter(true);
        }
    }

    private void OnEnable()
    {
        InvalidateLayout();
    }

    private void OnDisable()
    {
        if (decorationRoot != null)
            decorationRoot.gameObject.SetActive(false);
        if (managedPresenterAnimator != null)
            managedPresenterAnimator.enabled = false;
        floorRenderersApplied.Clear();
    }

    private void Update()
    {
        ResolveSystems();
        if (!IsShowState() || showSetLayout == null)
        {
            if (decorationRoot != null)
                decorationRoot.gameObject.SetActive(false);
            wasReward = false;
            return;
        }

        ResolveStage();
        if (stageRoot == null)
            return;

        bool layoutChanged = appliedLayout != showSetLayout || appliedStageRoot != stageRoot;
        if (layoutChanged)
        {
            RebuildDecorationRoot();
            presenterAnimationStart = Time.unscaledTime;
            floorRenderersApplied.Clear();
        }

        if (decorationRoot != null && !decorationRoot.gameObject.activeSelf)
            decorationRoot.gameObject.SetActive(true);

        if (Time.unscaledTime >= nextCarrierRefresh)
        {
            nextCarrierRefresh = Time.unscaledTime + Mathf.Max(0.03f, carrierRefreshInterval);
            ApplyShowFloorToCarriers();
        }

        bool reward = IsRewardState();
        if (reward && !wasReward)
            presenterAnimationStart = Time.unscaledTime;
        wasReward = reward;

        UpdateDecorationVisibility();
        UpdatePresenter(false);
    }

    private void LateUpdate()
    {
        if (!IsRewardState() || showSetLayout == null || presenterRenderer == null || presenterRenderer.sprite == null)
            return;

        AlignPresenter(presenterRenderer.sprite);
    }

    private void ResolveSystems()
    {
        if (runManager == null) runManager = FindFirstObjectByType<BattleRunManager>();
        if (baseTemplate == null) baseTemplate = FindFirstObjectByType<RoomBaseTemplate>();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (worldSet == null) worldSet = FindFirstObjectByType<BattleShowWorldSetController>();
    }

    private void ResolveStage()
    {
        Transform candidate = worldSet != null ? worldSet.transform.Find(StageRootName) : null;
        if (candidate == null)
        {
            GameObject found = GameObject.Find(StageRootName);
            candidate = found != null ? found.transform : null;
        }

        if (candidate != stageRoot)
        {
            stageRoot = candidate;
            decorationRoot = null;
            presenterRenderer = null;
            managedPresenterAnimator = null;
            floorRenderersApplied.Clear();
        }

        if (stageRoot == null)
            return;

        Transform presenter = FindRecursive(stageRoot, PresenterObjectName);
        if (presenter != null)
            presenterRenderer = presenter.GetComponent<SpriteRenderer>();
    }

    private bool IsShowState()
    {
        return runManager != null && runManager.RunActive &&
               (runManager.State == BattleRunState.Reward || runManager.State == BattleRunState.SelectingNode);
    }

    private bool IsRewardState()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
    }

    private void InvalidateLayout()
    {
        appliedLayout = null;
        appliedStageRoot = null;
        nextCarrierRefresh = 0f;
        presenterAnimationStart = Time.unscaledTime;
        floorRenderersApplied.Clear();
    }

    private void RebuildDecorationRoot()
    {
        if (stageRoot == null || showSetLayout == null)
            return;

        Transform existing = stageRoot.Find(DecorationRootName);
        if (existing != null)
        {
            existing.name = DecorationRootName + "_Removing";
            existing.gameObject.SetActive(false);
            Destroy(existing.gameObject);
        }

        GameObject root = new(DecorationRootName);
        root.transform.SetParent(stageRoot, true);
        decorationRoot = root.transform;
        decorationRoot.position = ResolveGridOriginWorld();
        decorationRoot.rotation = Quaternion.identity;
        decorationRoot.localScale = Vector3.one;

        decorationBindings.Clear();
        int sortingLayerId = ResolveSortingLayerId();
        IReadOnlyList<BattleShowTileObjectData> entries = showSetLayout.TileObjects;
        for (int i = 0; i < entries.Count; i++)
        {
            BattleShowTileObjectData entry = entries[i];
            if (entry == null || entry.Sprite == null)
                continue;

            GameObject tile = new($"DecorTile_{i:000}_{SafeName(entry.Label)}");
            tile.transform.SetParent(decorationRoot, false);
            float cell = showSetLayout.CellWorldSize;
            tile.transform.localPosition = new Vector3(
                entry.GridPosition.x * cell + entry.LocalOffset.x,
                entry.GridPosition.y * cell + entry.LocalOffset.y,
                0f);
            tile.transform.localRotation = Quaternion.Euler(0f, 0f, entry.QuarterTurns * 90f);
            tile.transform.localScale = new Vector3(entry.Scale.x, entry.Scale.y, 1f);

            SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
            renderer.sprite = entry.Sprite;
            renderer.color = entry.Tint;
            renderer.flipX = entry.FlipX;
            renderer.flipY = entry.FlipY;
            renderer.sortingLayerID = sortingLayerId;
            renderer.sortingOrder = entry.SortingOrder;

            decorationBindings.Add(new DecorationBinding
            {
                gameObject = tile,
                data = entry
            });
        }

        appliedLayout = showSetLayout;
        appliedStageRoot = stageRoot;
        decorationRoot.gameObject.SetActive(IsShowState());
    }

    private Vector3 ResolveGridOriginWorld()
    {
        Vector3 origin;
        if (baseTemplate != null && baseTemplate.HasPersistentBase)
        {
            origin = baseTemplate.FixedTileOriginWorld;
        }
        else if (player != null)
        {
            float halfSpan = (RoomBaseTemplate.FixedBaseTiles - 1) * 0.5f * RoomBaseTemplate.TileWorldSize;
            origin = player.transform.position + new Vector3(-halfSpan, -halfSpan, 0f);
        }
        else
        {
            origin = Vector3.zero;
        }

        origin += new Vector3(showSetLayout.WorldOffset.x, showSetLayout.WorldOffset.y, 0f);
        origin.z = 0f;
        return origin;
    }

    private void UpdateDecorationVisibility()
    {
        bool reward = IsRewardState();
        bool map = runManager != null && runManager.RunActive && runManager.State == BattleRunState.SelectingNode;
        for (int i = 0; i < decorationBindings.Count; i++)
        {
            DecorationBinding binding = decorationBindings[i];
            if (binding?.gameObject == null || binding.data == null)
                continue;
            bool visible = (reward && binding.data.VisibleInReward) || (map && binding.data.VisibleInMap);
            if (binding.gameObject.activeSelf != visible)
                binding.gameObject.SetActive(visible);
        }
    }

    private void ApplyShowFloorToCarriers()
    {
        if (stageRoot == null || showSetLayout == null || !showSetLayout.HasShowFloorSprites)
            return;

        ApplyShowFloorToCarrier(stageRoot.Find(ScreenCarrierName));
        ApplyShowFloorToCarrier(stageRoot.Find(PresenterCarrierName));
    }

    private void ApplyShowFloorToCarrier(Transform carrier)
    {
        if (carrier == null)
            return;

        Transform visual = carrier.Find("Visual");
        if (visual == null)
            return;

        for (int i = 0; i < visual.childCount; i++)
        {
            Transform child = visual.GetChild(i);
            if (child == null || !child.name.StartsWith("ShowTile_", StringComparison.Ordinal))
                continue;

            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            if (renderer == null)
                continue;

            int id = renderer.GetInstanceID();
            if (!floorRenderersApplied.Contains(id))
            {
                Sprite sprite = showSetLayout.GetRandomShowFloorSprite(renderer.sprite);
                if (sprite != null)
                    renderer.sprite = sprite;
                floorRenderersApplied.Add(id);
            }

            renderer.color = showSetLayout.ShowFloorTint;
        }
    }

    private void UpdatePresenter(bool forceRestart)
    {
        if (stageRoot == null || showSetLayout == null)
            return;

        Transform presenter = FindRecursive(stageRoot, PresenterObjectName);
        if (presenter == null)
            return;

        presenterRenderer = presenter.GetComponent<SpriteRenderer>();
        if (presenterRenderer == null)
            return;

        bool reward = IsRewardState();
        Transform presenterCarrier = stageRoot.Find(PresenterCarrierName);
        bool carrierVisible = presenterCarrier != null && presenterCarrier.gameObject.activeInHierarchy;
        if (!reward || !carrierVisible)
        {
            if (managedPresenterAnimator != null)
                managedPresenterAnimator.enabled = false;
            return;
        }

        RuntimeAnimatorController animatorController = showSetLayout.PresenterAnimatorController;
        if (animatorController != null)
        {
            if (managedPresenterAnimator == null)
                managedPresenterAnimator = presenter.GetComponent<Animator>() ?? presenter.gameObject.AddComponent<Animator>();

            if (managedPresenterAnimator.runtimeAnimatorController != animatorController)
            {
                managedPresenterAnimator.runtimeAnimatorController = animatorController;
                forceRestart = true;
            }

            managedPresenterAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
            managedPresenterAnimator.enabled = true;
            if (forceRestart)
                managedPresenterAnimator.Rebind();
            presenterRenderer.enabled = true;
            return;
        }

        if (managedPresenterAnimator != null)
            managedPresenterAnimator.enabled = false;

        Sprite[] frames = showSetLayout.PresenterAnimationFrames;
        Sprite sprite = null;
        if (frames != null && frames.Length > 0)
        {
            int validCount = 0;
            for (int i = 0; i < frames.Length; i++)
                if (frames[i] != null)
                    validCount++;

            if (validCount > 0)
            {
                int rawFrame = Mathf.Max(0, Mathf.FloorToInt((Time.unscaledTime - presenterAnimationStart) * showSetLayout.PresenterFps));
                int targetOrdinal = showSetLayout.PresenterLoop ? rawFrame % validCount : Mathf.Min(rawFrame, validCount - 1);
                int ordinal = 0;
                for (int i = 0; i < frames.Length; i++)
                {
                    if (frames[i] == null)
                        continue;
                    if (ordinal == targetOrdinal)
                    {
                        sprite = frames[i];
                        break;
                    }
                    ordinal++;
                }
            }
        }

        if (sprite == null)
            sprite = showSetLayout.PresenterSprite;
        if (sprite == null)
            return;

        presenterRenderer.sprite = sprite;
        presenterRenderer.enabled = true;
        AlignPresenter(sprite);
    }

    private void AlignPresenter(Sprite sprite)
    {
        if (presenterRenderer == null || sprite == null || showSetLayout == null || stageRoot == null)
            return;

        Transform presenter = presenterRenderer.transform;
        Transform carrier = stageRoot.Find(PresenterCarrierName);
        if (carrier == null || presenter.parent != carrier)
            return;

        float scale = showSetLayout.PresenterWorldHeight / Mathf.Max(0.0001f, Mathf.Abs(sprite.bounds.size.y));
        float signedScale = showSetLayout.PresenterFlipX ? -scale : scale;
        presenter.localScale = new Vector3(signedScale, scale, 1f);

        float visualRightOffset = signedScale >= 0f
            ? sprite.bounds.max.x * signedScale
            : sprite.bounds.min.x * signedScale;
        float carrierRightEdge = 6f - 0.5f;
        float x = carrierRightEdge - showSetLayout.PresenterRightPadding - visualRightOffset;
        float y = (4f - 1f) * 0.5f + showSetLayout.PresenterPadYOffset;
        presenter.localPosition = new Vector3(x, y, 0f);
        presenter.localRotation = Quaternion.identity;
    }

    private int ResolveSortingLayerId()
    {
        SpriteRenderer playerRenderer = player != null ? player.GetComponentInChildren<SpriteRenderer>(true) : null;
        return playerRenderer != null ? playerRenderer.sortingLayerID : 0;
    }

    private static Transform FindRecursive(Transform root, string targetName)
    {
        if (root == null)
            return null;
        if (root.name == targetName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindRecursive(root.GetChild(i), targetName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static string SafeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Tile";

        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c == '/' || c == '\\' || c == ':' || c == '*')
                chars[i] = '_';
        }
        return new string(chars);
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Central, scene-authored presentation controller for the battle-show layer.
/// Attach this component to one Empty GameObject and assign the art from Inspector.
/// It owns replaceable stage floor sprites, mandatory upper/lower 32px rail-edge sprites,
/// presenter sprite-sheet playback, bird-eye spotlight sprite-sheet playback,
/// and full-screen cue effects such as "LET'S ROLL!" and "CUT!".
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleShowPresentationManager : MonoBehaviour
{
    public static BattleShowPresentationManager Instance { get; private set; }

    [Header("Default / Sliding Floor - 32px")]
    [Tooltip("Random floor tile variants. Each tile of an incoming show slab chooses from this list. Expected: 32x32 px, PPU 32.")]
    [SerializeField] private Sprite[] floorVariants;
    [Tooltip("Mandatory repeatable 32px upper extension/edge sprite for every sliding slab.")]
    [SerializeField] private Sprite upperEdgeSprite32;
    [Tooltip("Mandatory repeatable 32px lower extension/edge sprite for every sliding slab.")]
    [SerializeField] private Sprite lowerEdgeSprite32;
    [SerializeField] private Color floorTint = Color.white;
    [SerializeField] private Color edgeTint = Color.white;
    [SerializeField] private int floorSortingOrder = -18;
    [SerializeField] private int edgeSortingOrder = -17;

    [Header("Presenter Sprite Sheet")]
    [Tooltip("Put sliced presenter sprite-sheet frames here in playback order.")]
    [SerializeField] private Sprite[] presenterFrames;
    [SerializeField, Min(1f)] private float presenterFps = 8f;
    [SerializeField] private bool presenterLoop = true;
    [SerializeField] private bool autoPlayPresenterDuringReward = true;

    [Header("Spotlight Sprite Sheet")]
    [Tooltip("Optional bird-eye floor-light animation frames. If empty, BattleHUD keeps its generated soft ellipse.")]
    [SerializeField] private Sprite[] spotlightFrames;
    [SerializeField, Min(1f)] private float spotlightFps = 10f;
    [SerializeField] private bool spotlightLoop = true;
    [SerializeField] private bool autoPlaySpotlightDuringReward = true;

    [Header("Show Cue Sprite Sheets")]
    [Tooltip("Sliced frames for the LET'S ROLL! cue. Automatically plays when entering a Combat/Elite node.")]
    [SerializeField] private Sprite[] letsRollFrames;
    [Tooltip("Sliced frames for the CUT! cue. Automatically plays when combat ends and reward selection starts.")]
    [SerializeField] private Sprite[] cutFrames;
    [SerializeField, Min(1f)] private float cueFps = 12f;
    [SerializeField] private Vector2 cueSize = new(900f, 360f);
    [SerializeField] private Vector2 cueAnchor = new(0.5f, 0.58f);
    [SerializeField] private Vector2 cueOffset = Vector2.zero;
    [SerializeField] private int cueCanvasSortingOrder = 900;

    private BattleRunManager runManager;
    private BattleHUD hud;
    private Image playerSpotlightImage;
    private Image presenterSpotlightImage;
    private Canvas cueCanvas;
    private Image cueImage;

    private Coroutine presenterRoutine;
    private Coroutine spotlightRoutine;
    private Coroutine cueRoutine;
    private Coroutine floorDecorateRoutine;
    private bool rewardPresentationActive;
    private bool subscribed;
    private bool warnedMissingUpperEdge;
    private bool warnedMissingLowerEdge;

    private static Sprite fallback32;

    public Color FloorTint => floorTint;
    public Color EdgeTint => edgeTint;
    public Sprite UpperEdgeSprite32 => upperEdgeSprite32 != null ? upperEdgeSprite32 : Default32Sprite;
    public Sprite LowerEdgeSprite32 => lowerEdgeSprite32 != null ? lowerEdgeSprite32 : Default32Sprite;
    public bool HasFloorVariants => floorVariants != null && floorVariants.Length > 0;

    private static Sprite Default32Sprite
    {
        get
        {
            if (fallback32 != null)
                return fallback32;

            const int pixels = 32;
            Texture2D texture = new(pixels, pixels, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color[] colors = new Color[pixels * pixels];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            texture.SetPixels(colors);
            texture.Apply(false, true);

            fallback32 = Sprite.Create(
                texture,
                new Rect(0f, 0f, pixels, pixels),
                new Vector2(0.5f, 0.5f),
                pixels,
                0,
                SpriteMeshType.FullRect);
            fallback32.name = "RuntimeShowDefault32";
            fallback32.hideFlags = HideFlags.HideAndDontSave;
            return fallback32;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[BattleShowPresentationManager] Multiple managers found. Keeping the first one.", this);
            enabled = false;
            return;
        }

        Instance = this;
        ResolveReferences();
        EnsureCueCanvas();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeRunEvents();
    }

    private void OnDisable()
    {
        UnsubscribeRunEvents();
        StopPresenterAnimation();
        StopSpotlightAnimation();
        StopCue();
        if (floorDecorateRoutine != null)
            StopCoroutine(floorDecorateRoutine);
        floorDecorateRoutine = null;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (hud == null || runManager == null)
        {
            ResolveReferences();
            SubscribeRunEvents();
        }

        if (rewardPresentationActive)
            ResolveHudSpotlightImages();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (hud == null)
            hud = FindFirstObjectByType<BattleHUD>();
    }

    private void SubscribeRunEvents()
    {
        if (runManager == null)
            return;

        if (subscribed)
            return;

        runManager.StateChanged += HandleRunStateChanged;
        runManager.NodeEntered += HandleNodeEntered;
        runManager.RewardSelectionRequested += HandleRewardSelectionRequested;
        subscribed = true;
        HandleRunStateChanged(runManager.State);
    }

    private void UnsubscribeRunEvents()
    {
        if (!subscribed || runManager == null)
            return;

        runManager.StateChanged -= HandleRunStateChanged;
        runManager.NodeEntered -= HandleNodeEntered;
        runManager.RewardSelectionRequested -= HandleRewardSelectionRequested;
        subscribed = false;
    }

    private void HandleRunStateChanged(BattleRunState state)
    {
        bool reward = state == BattleRunState.Reward;
        if (reward == rewardPresentationActive)
            return;

        rewardPresentationActive = reward;
        if (reward)
        {
            if (autoPlayPresenterDuringReward)
                PlayPresenterAnimation(true);
            if (autoPlaySpotlightDuringReward)
                PlaySpotlightAnimation(true);
        }
        else
        {
            StopPresenterAnimation();
            StopSpotlightAnimation();
        }
    }

    private void HandleNodeEntered(BattleNodeData node)
    {
        if (node == null)
            return;
        if (node.type == BattleNodeType.Combat || node.type == BattleNodeType.Elite)
            PlayLetsRoll();
    }

    private void HandleRewardSelectionRequested(IReadOnlyList<BattleEquipmentSO> _)
    {
        PlayCut();

        if (floorDecorateRoutine != null)
            StopCoroutine(floorDecorateRoutine);
        floorDecorateRoutine = StartCoroutine(DecorateIncomingShowFloorNextFrame());
    }

    // ------------------------------------------------------------------
    // Floor / rail artwork.
    // BattleStageTransitionController still owns movement. This manager only
    // skin/decorates the newly-created slabs so art remains scene-configurable.
    // ------------------------------------------------------------------

    public Sprite GetRandomFloorSprite(Sprite fallback = null)
    {
        if (floorVariants == null || floorVariants.Length == 0)
            return fallback;

        int start = UnityEngine.Random.Range(0, floorVariants.Length);
        for (int i = 0; i < floorVariants.Length; i++)
        {
            Sprite sprite = floorVariants[(start + i) % floorVariants.Length];
            if (sprite != null)
                return sprite;
        }
        return fallback;
    }

    public void RefreshSlidingFloorArt()
    {
        if (floorDecorateRoutine != null)
            StopCoroutine(floorDecorateRoutine);
        floorDecorateRoutine = StartCoroutine(DecorateIncomingShowFloorNextFrame());
    }

    private IEnumerator DecorateIncomingShowFloorNextFrame()
    {
        // RewardSelectionRequested can fire before or after the transition controller.
        // Waiting one frame guarantees the temporary slabs exist in either case.
        yield return null;
        DecorateAllRewardShowSlabs();
        floorDecorateRoutine = null;
    }

    private void DecorateAllRewardShowSlabs()
    {
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform root = transforms[i];
            if (root == null || !root.name.StartsWith("RewardShowSlab_", StringComparison.Ordinal))
                continue;
            DecorateRewardShowSlab(root);
        }
    }

    private void DecorateRewardShowSlab(Transform slabRoot)
    {
        if (slabRoot == null)
            return;

        Transform visual = slabRoot.Find("Visual");
        if (visual == null || visual.Find("PresentationEdges") != null)
            return;

        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minY = int.MaxValue;
        int maxY = int.MinValue;
        bool foundTile = false;

        for (int i = 0; i < visual.childCount; i++)
        {
            Transform child = visual.GetChild(i);
            if (child == null || !child.name.StartsWith("ShowTile_", StringComparison.Ordinal))
                continue;

            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            if (renderer == null)
                continue;

            Sprite randomFloor = GetRandomFloorSprite(renderer.sprite);
            if (randomFloor != null)
                renderer.sprite = randomFloor;
            renderer.color = floorTint;
            renderer.sortingOrder = floorSortingOrder;

            int x = Mathf.RoundToInt(child.localPosition.x);
            int y = Mathf.RoundToInt(child.localPosition.y);
            minX = Mathf.Min(minX, x);
            maxX = Mathf.Max(maxX, x);
            minY = Mathf.Min(minY, y);
            maxY = Mathf.Max(maxY, y);
            foundTile = true;
        }

        if (!foundTile)
            return;

        if (upperEdgeSprite32 == null && !warnedMissingUpperEdge)
        {
            warnedMissingUpperEdge = true;
            Debug.LogWarning("[BattleShowPresentationManager] Upper Edge 32px is not assigned. A plain Sprite-Default placeholder is being used.", this);
        }
        if (lowerEdgeSprite32 == null && !warnedMissingLowerEdge)
        {
            warnedMissingLowerEdge = true;
            Debug.LogWarning("[BattleShowPresentationManager] Lower Edge 32px is not assigned. A plain Sprite-Default placeholder is being used.", this);
        }

        GameObject edgeRootObject = new("PresentationEdges");
        edgeRootObject.transform.SetParent(visual, false);
        Transform edgeRoot = edgeRootObject.transform;

        for (int x = minX; x <= maxX; x++)
        {
            CreateEdgeTile(edgeRoot, $"UpperEdge_{x}", new Vector3(x, maxY + 1, 0f), UpperEdgeSprite32);
            CreateEdgeTile(edgeRoot, $"LowerEdge_{x}", new Vector3(x, minY - 1, 0f), LowerEdgeSprite32);
        }
    }

    private void CreateEdgeTile(Transform parent, string objectName, Vector3 localPosition, Sprite sprite)
    {
        GameObject go = new(objectName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = edgeTint;
        renderer.sortingOrder = edgeSortingOrder;
    }

    // ------------------------------------------------------------------
    // Presenter.
    // ------------------------------------------------------------------

    public void PlayPresenterAnimation(bool restart = true)
    {
        ResolveReferences();
        if (presenterFrames == null || presenterFrames.Length == 0 || hud == null)
            return;

        if (presenterRoutine != null)
        {
            if (!restart)
                return;
            StopCoroutine(presenterRoutine);
        }
        presenterRoutine = StartCoroutine(PlayPresenterFrames());
    }

    public void StopPresenterAnimation()
    {
        if (presenterRoutine != null)
            StopCoroutine(presenterRoutine);
        presenterRoutine = null;
    }

    private IEnumerator PlayPresenterFrames()
    {
        int frame = 0;
        float delay = 1f / Mathf.Max(1f, presenterFps);
        while (true)
        {
            Sprite sprite = presenterFrames[frame];
            if (sprite != null && hud != null)
                hud.SetPresenterSprite(sprite);

            frame++;
            if (frame >= presenterFrames.Length)
            {
                if (!presenterLoop)
                    break;
                frame = 0;
            }
            yield return new WaitForSecondsRealtime(delay);
        }
        presenterRoutine = null;
    }

    // ------------------------------------------------------------------
    // Bird-eye spotlight sprite-sheet playback.
    // ------------------------------------------------------------------

    public void PlaySpotlightAnimation(bool restart = true)
    {
        if (spotlightFrames == null || spotlightFrames.Length == 0)
            return;
        ResolveHudSpotlightImages();

        if (spotlightRoutine != null)
        {
            if (!restart)
                return;
            StopCoroutine(spotlightRoutine);
        }
        spotlightRoutine = StartCoroutine(PlaySpotlightFrames());
    }

    public void StopSpotlightAnimation()
    {
        if (spotlightRoutine != null)
            StopCoroutine(spotlightRoutine);
        spotlightRoutine = null;
    }

    private IEnumerator PlaySpotlightFrames()
    {
        int frame = 0;
        float delay = 1f / Mathf.Max(1f, spotlightFps);
        while (true)
        {
            ResolveHudSpotlightImages();
            Sprite sprite = spotlightFrames[frame];
            if (sprite != null)
            {
                if (playerSpotlightImage != null) playerSpotlightImage.sprite = sprite;
                if (presenterSpotlightImage != null) presenterSpotlightImage.sprite = sprite;
            }

            frame++;
            if (frame >= spotlightFrames.Length)
            {
                if (!spotlightLoop)
                    break;
                frame = 0;
            }
            yield return new WaitForSecondsRealtime(delay);
        }
        spotlightRoutine = null;
    }

    private void ResolveHudSpotlightImages()
    {
        if (playerSpotlightImage != null && presenterSpotlightImage != null)
            return;

        Image[] images = FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null) continue;
            if (image.name == "PlayerFloorSpotlight") playerSpotlightImage = image;
            else if (image.name == "PresenterFloorSpotlight") presenterSpotlightImage = image;
        }
    }

    // ------------------------------------------------------------------
    // Show cues.
    // ------------------------------------------------------------------

    public void PlayLetsRoll() => PlayCue(letsRollFrames);
    public void PlayCut() => PlayCue(cutFrames);

    public void PlayCue(Sprite[] frames)
    {
        EnsureCueCanvas();
        if (frames == null || frames.Length == 0 || cueImage == null)
            return;

        if (cueRoutine != null)
            StopCoroutine(cueRoutine);
        cueRoutine = StartCoroutine(PlayCueFrames(frames));
    }

    public void StopCue()
    {
        if (cueRoutine != null)
            StopCoroutine(cueRoutine);
        cueRoutine = null;
        if (cueImage != null)
            cueImage.enabled = false;
    }

    private IEnumerator PlayCueFrames(Sprite[] frames)
    {
        cueImage.enabled = true;
        float delay = 1f / Mathf.Max(1f, cueFps);
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null)
                cueImage.sprite = frames[i];
            yield return new WaitForSecondsRealtime(delay);
        }
        cueImage.enabled = false;
        cueRoutine = null;
    }

    private void EnsureCueCanvas()
    {
        if (cueCanvas != null)
            return;

        GameObject canvasObject = new("ShowCueCanvas");
        canvasObject.transform.SetParent(transform, false);
        cueCanvas = canvasObject.AddComponent<Canvas>();
        cueCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        cueCanvas.sortingOrder = cueCanvasSortingOrder;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject imageObject = new("ShowCueImage");
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = imageObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = cueAnchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = cueSize;
        rect.anchoredPosition = cueOffset;

        cueImage = imageObject.AddComponent<Image>();
        cueImage.preserveAspect = true;
        cueImage.raycastTarget = false;
        cueImage.enabled = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        Validate32PxSprite(upperEdgeSprite32, "Upper Edge");
        Validate32PxSprite(lowerEdgeSprite32, "Lower Edge");
        if (floorVariants != null)
        {
            for (int i = 0; i < floorVariants.Length; i++)
                Validate32PxSprite(floorVariants[i], $"Floor Variant {i}");
        }
    }

    private void Validate32PxSprite(Sprite sprite, string label)
    {
        if (sprite == null)
            return;
        if (Mathf.RoundToInt(sprite.rect.width) != 32 || Mathf.RoundToInt(sprite.rect.height) != 32)
            Debug.LogWarning($"[BattleShowPresentationManager] {label} is expected to be 32x32px: {sprite.name}", this);
    }
#endif
}

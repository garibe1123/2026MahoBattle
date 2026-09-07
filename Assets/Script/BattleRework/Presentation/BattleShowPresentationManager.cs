using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Scene-authored presentation controller for the battle-show layer.
/// Attach this component to one Empty GameObject and assign the art from Inspector.
///
/// Responsibilities:
/// - random 32px floor variants for incoming show slabs
/// - six-part sliding template: upper/lower plates + left/right/upper/lower handles
/// - presenter sprite-sheet playback
/// - bird-eye spotlight sprite-sheet playback
/// - one-shot show cues such as "LET'S ROLL!" and "CUT!"
///
/// BattleStageTransitionController still owns movement/topology.
/// This manager only controls replaceable presentation art and timing.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleShowPresentationManager : MonoBehaviour
{
    public enum HandlePlacementMode
    {
        ContactSideOnly,
        AllFourSides,
        None
    }

    public static BattleShowPresentationManager Instance { get; private set; }

    [Header("Sliding Floor / Template - 32px")]
    [Tooltip("Random interior floor tile variants. Expected: 32x32 px, PPU 32.")]
    [SerializeField] private Sprite[] floorVariants;

    [Tooltip("MANDATORY upper plate. Repeated across the entire upper edge of every sliding slab. It always sits one full 32px row above the floor body.")]
    [FormerlySerializedAs("upperEdgeSprite32")]
    [SerializeField] private Sprite upperPlateSprite32;

    [Tooltip("Lower plate. Repeated across the entire lower edge of every sliding slab.")]
    [FormerlySerializedAs("lowerEdgeSprite32")]
    [SerializeField] private Sprite lowerPlateSprite32;

    [Tooltip("32px handle used when the block connects on its left side.")]
    [SerializeField] private Sprite leftHandleSprite32;

    [Tooltip("32px handle used when the block connects on its right side.")]
    [SerializeField] private Sprite rightHandleSprite32;

    [Tooltip("32px handle used when the block connects on its upper side.")]
    [SerializeField] private Sprite upperHandleSprite32;

    [Tooltip("32px handle used when the block connects on its lower side.")]
    [SerializeField] private Sprite lowerHandleSprite32;

    [Tooltip("Reward slabs currently arrive from screen-right, so the left handle is the actual contact handle.")]
    [SerializeField] private HandlePlacementMode rewardHandlePlacement = HandlePlacementMode.ContactSideOnly;

    [SerializeField] private Color floorTint = Color.white;
    [SerializeField] private Color plateTint = Color.white;
    [SerializeField] private Color handleTint = Color.white;

    [Header("Sliding Template Sorting")]
    [SerializeField] private int floorSortingOrder = -18;
    [SerializeField] private int lowerPlateSortingOrder = -17;
    [Tooltip("Upper plate is forcibly kept above the floor body, even if this value is set too low.")]
    [SerializeField] private int upperPlateSortingOrder = -15;
    [SerializeField] private int handleSortingOrder = -14;

    [Header("Sliding Template Placement")]
    [Tooltip("Local tile-space offset for side handles from the slab border.")]
    [SerializeField, Range(0.5f, 1.5f)] private float sideHandleDistance = 1f;
    [Tooltip("Local tile-space offset from the upper/lower plate row for vertical handles.")]
    [SerializeField, Range(0.5f, 1.5f)] private float verticalHandleDistance = 1f;

    [Header("Dock Click")]
    [Tooltip("Small visual snap on the contact handle when MapBlock reports its docking impact.")]
    [SerializeField, Range(0f, 0.35f)] private float dockHandlePunch = 0.14f;
    [SerializeField, Min(0.03f)] private float dockHandlePunchDuration = 0.14f;
    [SerializeField, Range(1, 12)] private int dockHandlePunchVibrato = 4;

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
    [Tooltip("Sliced frames for LET'S ROLL!. Automatically plays when entering a Combat/Elite node.")]
    [SerializeField] private Sprite[] letsRollFrames;
    [Tooltip("Sliced frames for CUT!. Automatically plays when combat ends and reward selection starts.")]
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

    private readonly HashSet<MapBlock> decoratedBlocks = new();

    private bool warnedUpperPlate;
    private bool warnedLowerPlate;
    private bool warnedLeftHandle;
    private bool warnedRightHandle;
    private bool warnedUpperHandle;
    private bool warnedLowerHandle;

    private static Sprite fallback32;

    public Color FloorTint => floorTint;
    public Color PlateTint => plateTint;
    public Color HandleTint => handleTint;

    public Sprite UpperPlateSprite32 => upperPlateSprite32 != null ? upperPlateSprite32 : Default32Sprite;
    public Sprite LowerPlateSprite32 => lowerPlateSprite32;
    public Sprite LeftHandleSprite32 => leftHandleSprite32;
    public Sprite RightHandleSprite32 => rightHandleSprite32;
    public Sprite UpperHandleSprite32 => upperHandleSprite32;
    public Sprite LowerHandleSprite32 => lowerHandleSprite32;
    public bool HasFloorVariants => floorVariants != null && floorVariants.Length > 0;

    /// <summary>
    /// Upper plate must always read above the floor body.
    /// Handles are allowed above it because they are mechanical overlays.
    /// </summary>
    private int EffectiveUpperPlateSortingOrder =>
        Mathf.Max(upperPlateSortingOrder, floorSortingOrder + 2);

    private int EffectiveHandleSortingOrder =>
        Mathf.Max(handleSortingOrder, EffectiveUpperPlateSortingOrder + 1);

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
            for (int i = 0; i < colors.Length; i++)
                colors[i] = Color.white;

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

        UnsubscribeDecoratedBlocks();
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

        CleanupDecoratedBlocks();
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
        if (runManager == null || subscribed)
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
    // Sliding floor/template artwork
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

    /// <summary>
    /// Re-scans the temporary reward floor. Useful after changing art at runtime.
    /// </summary>
    public void RefreshSlidingFloorArt()
    {
        if (floorDecorateRoutine != null)
            StopCoroutine(floorDecorateRoutine);

        floorDecorateRoutine = StartCoroutine(DecorateIncomingShowFloorNextFrame(true));
    }

    /// <summary>
    /// Public hook for any future cardinal sliding MapBlock.
    /// contactSide is the face that physically meets the already-occupied floor:
    /// left/right/up/down.
    /// </summary>
    public void ApplySlidingTemplate(MapBlock block, Vector2 contactSide, bool rebuild = false)
    {
        if (block == null)
            return;

        DecorateSlidingBlock(block.transform, contactSide, rebuild);
    }

    private IEnumerator DecorateIncomingShowFloorNextFrame(bool rebuild = false)
    {
        // RewardSelectionRequested may fire before or after the transition controller.
        // One frame is enough because the slabs are created and immediately moved from offscreen.
        yield return null;

        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform root = transforms[i];
            if (root == null || !root.name.StartsWith("RewardShowSlab_", StringComparison.Ordinal))
                continue;

            // Reward slabs currently come from screen-right toward the Base,
            // so their left face is the physical click/contact face.
            DecorateSlidingBlock(root, Vector2.left, rebuild);
        }

        floorDecorateRoutine = null;
    }

    private void DecorateSlidingBlock(Transform slabRoot, Vector2 contactSide, bool rebuild)
    {
        if (slabRoot == null)
            return;

        Transform visual = slabRoot.Find("Visual");
        if (visual == null)
            return;

        Transform oldTemplate = visual.Find("PresentationTemplate");
        if (oldTemplate != null)
        {
            if (!rebuild)
            {
                SubscribeDockImpact(slabRoot.GetComponent<MapBlock>());
                return;
            }

            oldTemplate.gameObject.SetActive(false);
            Destroy(oldTemplate.gameObject);
        }

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

        WarnMissingTemplateSprites();

        GameObject templateObject = new("PresentationTemplate");
        templateObject.transform.SetParent(visual, false);
        Transform templateRoot = templateObject.transform;

        // Plate rows are extension rows, not replacements for the walkable body.
        // The mandatory upper plate therefore always occupies maxY + 1.
        for (int x = minX; x <= maxX; x++)
        {
            CreateTemplateSprite(
                templateRoot,
                $"UpperPlate_{x}",
                new Vector3(x, maxY + 1f, 0f),
                UpperPlateSprite32,
                plateTint,
                EffectiveUpperPlateSortingOrder);

            if (lowerPlateSprite32 != null)
            {
                CreateTemplateSprite(
                    templateRoot,
                    $"LowerPlate_{x}",
                    new Vector3(x, minY - 1f, 0f),
                    lowerPlateSprite32,
                    plateTint,
                    lowerPlateSortingOrder);
            }
        }

        CreateHandles(
            templateRoot,
            minX,
            maxX,
            minY,
            maxY,
            contactSide,
            rewardHandlePlacement);

        SubscribeDockImpact(slabRoot.GetComponent<MapBlock>());
    }

    private void CreateHandles(
        Transform templateRoot,
        int minX,
        int maxX,
        int minY,
        int maxY,
        Vector2 contactSide,
        HandlePlacementMode placementMode)
    {
        if (placementMode == HandlePlacementMode.None)
            return;

        float midX = (minX + maxX) * 0.5f;
        float midY = (minY + maxY) * 0.5f;

        bool all = placementMode == HandlePlacementMode.AllFourSides;
        Vector2 side = NormalizeCardinal(contactSide);

        if (all || side == Vector2.left)
        {
            CreateOptionalHandle(
                templateRoot,
                "DockHandle_Left",
                new Vector3(minX - sideHandleDistance, midY, 0f),
                leftHandleSprite32);
        }

        if (all || side == Vector2.right)
        {
            CreateOptionalHandle(
                templateRoot,
                "DockHandle_Right",
                new Vector3(maxX + sideHandleDistance, midY, 0f),
                rightHandleSprite32);
        }

        if (all || side == Vector2.up)
        {
            CreateOptionalHandle(
                templateRoot,
                "DockHandle_Upper",
                new Vector3(midX, maxY + 1f + verticalHandleDistance, 0f),
                upperHandleSprite32);
        }

        if (all || side == Vector2.down)
        {
            CreateOptionalHandle(
                templateRoot,
                "DockHandle_Lower",
                new Vector3(midX, minY - 1f - verticalHandleDistance, 0f),
                lowerHandleSprite32);
        }
    }

    private void CreateOptionalHandle(
        Transform parent,
        string objectName,
        Vector3 localPosition,
        Sprite sprite)
    {
        if (sprite == null)
            return;

        CreateTemplateSprite(
            parent,
            objectName,
            localPosition,
            sprite,
            handleTint,
            EffectiveHandleSortingOrder);
    }

    private static SpriteRenderer CreateTemplateSprite(
        Transform parent,
        string objectName,
        Vector3 localPosition,
        Sprite sprite,
        Color tint,
        int sortingOrder)
    {
        GameObject go = new(objectName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = tint;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    private void SubscribeDockImpact(MapBlock block)
    {
        if (block == null || decoratedBlocks.Contains(block))
            return;

        block.Impacted += HandleBlockImpacted;
        decoratedBlocks.Add(block);
    }

    private void HandleBlockImpacted(
        MapBlock block,
        Vector3 _,
        Vector2 travelDirection,
        float strength)
    {
        if (block == null)
            return;

        Transform visual = block.transform.Find("Visual");
        Transform template = visual != null ? visual.Find("PresentationTemplate") : null;
        if (template == null)
            return;

        Vector2 contactSide = NormalizeCardinal(travelDirection);
        Transform handle = FindHandle(template, contactSide);
        if (handle == null)
            return;

        handle.DOKill();
        handle.localScale = Vector3.one;

        float punch = Mathf.Max(0f, dockHandlePunch) * Mathf.Clamp(strength, 0.45f, 1.8f);
        if (punch <= 0.001f)
            return;

        handle.DOPunchScale(
                new Vector3(punch, punch, 0f),
                Mathf.Max(0.03f, dockHandlePunchDuration),
                Mathf.Max(1, dockHandlePunchVibrato),
                0.45f)
            .SetUpdate(true);
    }

    private static Transform FindHandle(Transform templateRoot, Vector2 side)
    {
        if (templateRoot == null)
            return null;

        if (side == Vector2.left)
            return templateRoot.Find("DockHandle_Left");
        if (side == Vector2.right)
            return templateRoot.Find("DockHandle_Right");
        if (side == Vector2.up)
            return templateRoot.Find("DockHandle_Upper");
        if (side == Vector2.down)
            return templateRoot.Find("DockHandle_Lower");

        return null;
    }

    private static Vector2 NormalizeCardinal(Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return Vector2.left;

        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
            return direction.x >= 0f ? Vector2.right : Vector2.left;

        return direction.y >= 0f ? Vector2.up : Vector2.down;
    }

    private void CleanupDecoratedBlocks()
    {
        if (decoratedBlocks.Count == 0)
            return;

        List<MapBlock> dead = null;
        foreach (MapBlock block in decoratedBlocks)
        {
            if (block != null)
                continue;

            dead ??= new List<MapBlock>();
            dead.Add(block);
        }

        if (dead == null)
            return;

        for (int i = 0; i < dead.Count; i++)
            decoratedBlocks.Remove(dead[i]);
    }

    private void UnsubscribeDecoratedBlocks()
    {
        foreach (MapBlock block in decoratedBlocks)
        {
            if (block != null)
                block.Impacted -= HandleBlockImpacted;
        }

        decoratedBlocks.Clear();
    }

    private void WarnMissingTemplateSprites()
    {
        WarnOnce(
            upperPlateSprite32 == null,
            ref warnedUpperPlate,
            "Upper Plate 32px is mandatory. A plain 32px Sprite-Default placeholder is being used.");

        WarnOnce(
            lowerPlateSprite32 == null,
            ref warnedLowerPlate,
            "Lower Plate 32px is not assigned.");

        WarnOnce(
            leftHandleSprite32 == null,
            ref warnedLeftHandle,
            "Left Handle 32px is not assigned.");

        WarnOnce(
            rightHandleSprite32 == null,
            ref warnedRightHandle,
            "Right Handle 32px is not assigned.");

        WarnOnce(
            upperHandleSprite32 == null,
            ref warnedUpperHandle,
            "Upper Handle 32px is not assigned.");

        WarnOnce(
            lowerHandleSprite32 == null,
            ref warnedLowerHandle,
            "Lower Handle 32px is not assigned.");
    }

    private void WarnOnce(bool condition, ref bool warned, string message)
    {
        if (!condition || warned)
            return;

        warned = true;
        Debug.LogWarning($"[BattleShowPresentationManager] {message}", this);
    }

    // ------------------------------------------------------------------
    // Presenter
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
    // Bird-eye spotlight sprite-sheet playback
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
                if (playerSpotlightImage != null)
                    playerSpotlightImage.sprite = sprite;

                if (presenterSpotlightImage != null)
                    presenterSpotlightImage.sprite = sprite;
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

        Image[] images = FindObjectsByType<Image>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null)
                continue;

            if (image.name == "PlayerFloorSpotlight")
                playerSpotlightImage = image;
            else if (image.name == "PresenterFloorSpotlight")
                presenterSpotlightImage = image;
        }
    }

    // ------------------------------------------------------------------
    // Show cues
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
        Validate32PxSprite(upperPlateSprite32, "Upper Plate");
        Validate32PxSprite(lowerPlateSprite32, "Lower Plate");
        Validate32PxSprite(leftHandleSprite32, "Left Handle");
        Validate32PxSprite(rightHandleSprite32, "Right Handle");
        Validate32PxSprite(upperHandleSprite32, "Upper Handle");
        Validate32PxSprite(lowerHandleSprite32, "Lower Handle");

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

        if (Mathf.RoundToInt(sprite.rect.width) != 32 ||
            Mathf.RoundToInt(sprite.rect.height) != 32)
        {
            Debug.LogWarning(
                $"[BattleShowPresentationManager] {label} is expected to be 32x32px: {sprite.name}",
                this);
        }
    }
#endif
}

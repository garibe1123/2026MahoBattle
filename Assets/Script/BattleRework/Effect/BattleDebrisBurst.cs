using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public enum BattleDebrisMaterial { Wood, Styrofoam, Metal, Stone }

/// <summary>
/// Shared, visual-only 2.5D debris burst for breakable movie-set props.
/// Each material has two 512x384 sprite sheets (vertical/horizontal),
/// six rows (Small A/B, Medium A/B, Large A/B) and eight 64px frames per row.
/// Landing location chooses cardinal direction, axis, and contact frame.
/// Uses pooled visual-only shards with top-down virtual-height ballistics.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleDebrisBurst : MonoBehaviour
{
    private const int CellPixels = 64;
    private const int HardLimit = 6;
    private const int MaximumPoolSlots = 600;
    private const string ResourcePath = "BattleEffects/PF_BattleDebrisBurst";

    private const int FramesPerPiece = 8;
    private const int RowsPerSheet = 6;
    private const int SheetWidth = CellPixels * FramesPerPiece;
    private const int SheetHeight = CellPixels * RowsPerSheet;

    private enum PieceSize { Small, Medium, Large }
    private enum LandingDirection { Down, Up, Left, Right }

    [Serializable]
    private sealed class MaterialSheets
    {
        [Tooltip("Up/down landings: 512x384px PNG, 6 rows x 8 frames.")]
        public Texture2D vertical;
        [Tooltip("Left/right landings: 512x384px PNG, 6 rows x 8 frames.")]
        public Texture2D horizontal;
    }

    [Serializable]
    private sealed class SizeMotion
    {
        [Min(0f)] public float selectionWeight = 1f;
        [Min(1f)] public float fpsMin = 10f;
        [Min(1f)] public float fpsMax = 16f;
        [Range(0.05f, 2f)] public float scaleMin = 0.3f;
        [Range(0.05f, 2f)] public float scaleMax = 0.5f;
        [Tooltip("0..7. Contact pose for rotation 1 of the Vertical sheet (Up/Down).")]
        [Range(0, 7)] public int landingFrameVerticalFirst = 3;
        [FormerlySerializedAs("landingFrameVertical")]
        [Tooltip("0..7. Contact pose for rotation 2 of the Vertical sheet (Up/Down).")]
        [Range(0, 7)] public int landingFrameVerticalSecond = 7;
        [Tooltip("0..7. Contact pose for rotation 1 of the Horizontal sheet (Left/Right).")]
        [Range(0, 7)] public int landingFrameHorizontalFirst = 3;
        [FormerlySerializedAs("landingFrameHorizontal")]
        [Tooltip("0..7. Contact pose for rotation 2 of the Horizontal sheet (Left/Right).")]
        [Range(0, 7)] public int landingFrameHorizontalSecond = 7;
        [Tooltip("50% horizontal mirror chance for pieces of this size.")]
        public bool randomFlipX = true;

        public SizeMotion(float weight, float fpsLow, float fpsHigh,
            float scaleLow, float scaleHigh)
        {
            selectionWeight = weight;
            fpsMin = fpsLow; fpsMax = fpsHigh;
            scaleMin = scaleLow; scaleMax = scaleHigh;
        }
    }

    [Header("Material Sheets - 512 x 384px, 8 frames x 6 rows")]
    [SerializeField] private BattleDebrisMaterial defaultMaterial = BattleDebrisMaterial.Wood;
    [SerializeField] private MaterialSheets wood = new();
    [SerializeField] private MaterialSheets styrofoam = new();
    [SerializeField] private MaterialSheets metal = new();
    [SerializeField] private MaterialSheets stone = new();

    [Header("Size Profiles - Sprite FPS, Landing Frame and Flip (A/B shared)")]
    [SerializeField] private SizeMotion small = new(3f, 12f, 18f, 0.25f, 0.37f);
    [SerializeField] private SizeMotion medium = new(2f, 8f, 14f, 0.36f, 0.49f);
    [SerializeField] private SizeMotion large = new(1f, 5f, 10f, 0.48f, 0.62f);

    [Header("Visual Animation")]
    [SerializeField] private bool loopDuringFlight = true;
    [SerializeField] private bool randomStartFrame = true;
    [SerializeField] private Material debrisMaterial;
    [SerializeField, Min(1f)] private float pixelsPerUnit = 64f;
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 25;

    [Header("Pieces per Burst")]
    [Tooltip("Inclusive lower bound for the number of shards requested by one explosion.")]
    [SerializeField, Range(1, HardLimit)] private int minimumPieces = 3;
    [Tooltip("Inclusive upper bound. Always limited to six shards per explosion.")]
    [SerializeField, Range(1, HardLimit)] private int maximumPieces = 6;

    [Header("Shared Pool (Piece Slots)")]
    [Tooltip("Total shard slots to prepare before the first explosion in a BattleScene. Each pooled Burst owns up to Max Pieces slots.")]
    [SerializeField, Range(0, MaximumPoolSlots)] private int prewarmPieceSlots = 24;
    [Tooltip("Maximum simultaneous shard slots, including active bursts. Slots are reserved in groups of Max Pieces.")]
    [SerializeField, Range(HardLimit, MaximumPoolSlots)] private int maximumPieceSlots = 48;

    [Header("Non-overlapping Scatter")]
    [SerializeField, Min(0.05f)] private float launchRadius = 0.80f;
    [SerializeField, Min(0.01f)] private float launchSpacing = 0.13f;
    [SerializeField, Min(0.1f)] private float landingDistanceMin = 1.20f;
    [SerializeField, Min(0.1f)] private float landingDistanceMax = 2.80f;
    [SerializeField, Min(0.05f)] private float landingSpacing = 0.38f;
    [SerializeField, Range(0f, 0.7f)] private float impactDirectionBias = 0.18f;
    [SerializeField, Range(0f, 1f)] private float curveMin = 0.06f;
    [SerializeField, Range(0f, 1.5f)] private float curveMax = 0.30f;
    [SerializeField, Range(0f, 0.2f)] private float inFlightWobble = 0.035f;
    [SerializeField, Range(0f, 0.5f)] private float landingSlideMin = 0.05f;
    [SerializeField, Range(0f, 0.5f)] private float landingSlideMax = 0.18f;
    [SerializeField, Min(0.01f)] private float landingSlideDuration = 0.18f;

    [Header("Top-down Fake 3D Projection")]
    [Tooltip("World XY is ground-plane movement. Height is a separate virtual Z axis projected upward on screen. 0 hides the height offset; 0.65 is a subtle overhead view.")]
    [SerializeField, Range(0f, 2f)] private float heightToScreenY = 0.65f;
    [Tooltip("Subtle apparent enlargement when a piece moves toward the camera (set 0 for strictly orthographic pixel art).")]
    [SerializeField, Range(0f, 0.25f)] private float perspectiveScalePerHeight = 0.05f;
    [Tooltip("Contact shadow contracts as virtual height increases.")]
    [SerializeField, Range(0f, 0.8f)] private float shadowShrinkPerHeight = 0.28f;
    [SerializeField, Range(0f, 1f)] private float shadowFadePerHeight = 0.35f;

    [Header("Arc, Bounce, Settle")]
    [SerializeField, Min(0f)] private float initialHeight = 0.10f;
    [SerializeField, Min(0.1f)] private float riseVelocityMin = 2.7f;
    [SerializeField, Min(0.1f)] private float riseVelocityMax = 4.0f;
    [SerializeField, Min(0.1f)] private float gravity = 11f;
    [SerializeField, Range(0, 4)] private int maximumBounces = 2;
    [SerializeField, Range(0f, 0.8f)] private float bounceRetention = 0.38f;
    [SerializeField, Min(0f)] private float settleDuration = 0.35f;
    [SerializeField, Min(0f)] private float restDuration = 0.65f;
    [SerializeField, Min(0.05f)] private float blinkDuration = 0.55f;
    [SerializeField, Range(2f, 18f)] private float blinkFrequency = 9f;

    private sealed class Shard
    {
        public GameObject rootObject;
        public Transform root;
        public Transform spriteTransform;
        public SpriteRenderer visual;
        public SpriteRenderer shadow;
        public Vector2 start;
        public Vector2 landing;
        public float worldZ;
        public float size;
        public float height;
        public float age;
        public float firstLandingTime;
        public float arcElapsed;
        public float arcDuration;
        public float arcStartHeight;
        public float arcRiseSpeed;
        public Sprite[] frames;
        public float fps;
        public int landingFrame;
        public int landingFrameFirst;
        public int landingFrameSecond;
        public int startingFrame;
        public bool hasHitGround;
        public Vector2 flightLanding;
        public Vector2 curvePerpendicular;
        public float curveOffset;
        public float wobblePhase;
        public float landedAge;
        public float restingAge;
        public int bounceCount;
        public bool resting;
        public bool active;
    }

    private readonly List<Shard> shards = new();
    private readonly List<Vector2> launchPositions = new();
    private readonly List<Vector2> landingPositions = new();
    private readonly List<float> landingFootprints = new();
    private readonly Dictionary<Texture2D, Sprite[][]> clipsBySheet = new();
    private Sprite shadowSprite;
    private Texture2D shadowTexture;
    private bool playing;
    private bool spawnedFromPool;
    private int activeCount;

    private static int preparedSceneHandle = -1;
    private static bool poolReady;
    private static int activePooledBursts;
    private static readonly List<GameObject> PrewarmInstances = new();

    private int ConfiguredMaximumPieces => Mathf.Clamp(maximumPieces, 1, HardLimit);
    private int ConfiguredMinimumPieces =>
        Mathf.Clamp(minimumPieces, 1, ConfiguredMaximumPieces);
    private int MaximumConcurrentBursts =>
        Mathf.Max(1, Mathf.Clamp(maximumPieceSlots, HardLimit, MaximumPoolSlots) /
            ConfiguredMaximumPieces);

    private MaterialSheets GetMaterialSheets(BattleDebrisMaterial type)
    {
        switch (type)
        {
            case BattleDebrisMaterial.Styrofoam: return styrofoam;
            case BattleDebrisMaterial.Metal: return metal;
            case BattleDebrisMaterial.Stone: return stone;
            default: return wood;
        }
    }

    private SizeMotion GetSizeMotion(PieceSize size)
    {
        switch (size)
        {
            case PieceSize.Medium: return medium;
            case PieceSize.Large: return large;
            default: return small;
        }
    }

    private PieceSize RandomSize()
    {
        float a = Mathf.Max(0f, small.selectionWeight);
        float b = Mathf.Max(0f, medium.selectionWeight);
        float c = Mathf.Max(0f, large.selectionWeight);
        if (a + b + c <= 0f)
            return PieceSize.Small;
        float roll = UnityEngine.Random.value * (a + b + c);
        if (roll < a) return PieceSize.Small;
        return roll < a + b ? PieceSize.Medium : PieceSize.Large;
    }

    private static float RandomBetween(float a, float b)
    {
        return UnityEngine.Random.Range(Mathf.Min(a, b), Mathf.Max(a, b));
    }

    private static LandingDirection FindLandingDirection(Vector2 offset)
    {
        if (Mathf.Abs(offset.x) > Mathf.Abs(offset.y))
            return offset.x >= 0f ? LandingDirection.Right : LandingDirection.Left;
        return offset.y >= 0f ? LandingDirection.Up : LandingDirection.Down;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPoolState()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        preparedSceneHandle = -1;
        poolReady = false;
        activePooledBursts = 0;
        PrewarmInstances.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterScenePrewarm()
    {
        // BattleScene may load later from a menu. Subscribe once so the pool
        // prepares during the scene transition, not on the first explosion.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (SceneManager.GetActiveScene().name == "BattleScene")
            PreparePool();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "BattleScene")
            PreparePool();
    }

    private void Awake()
    {
        // Each burst instance owns its six reusable child GameObjects.
        // Prewarm in Awake so there are no new shard objects on impact.
        for (int i = shards.Count; i < ConfiguredMaximumPieces; i++)
        {
            Shard shard = Acquire(i);
            shard.rootObject.SetActive(false);
        }
    }

    /// <summary>
    /// Prepare the shared scene-local PoolService in units of individual
    /// piece slots, grouped into reusable bursts. Safe to call repeatedly.
    /// </summary>
    public static void PreparePool()
    {
        BattleDebrisBurst prefab = Resources.Load<BattleDebrisBurst>(ResourcePath);
        if (prefab == null)
            return;

        int sceneHandle = SceneManager.GetActiveScene().handle;
        if (poolReady && preparedSceneHandle == sceneHandle)
            return;

        poolReady = true;
        preparedSceneHandle = sceneHandle;
        activePooledBursts = 0;
        int maxConcurrent = prefab.MaximumConcurrentBursts;
        int burstCount = Mathf.Clamp(
            Mathf.CeilToInt(prefab.prewarmPieceSlots /
                (float)prefab.ConfiguredMaximumPieces), 0, maxConcurrent);

        PrewarmInstances.Clear();
        for (int i = 0; i < burstCount; i++)
        {
            GameObject instance = PoolService.Spawn(
                prefab.gameObject, Vector3.zero, Quaternion.identity);
            if (instance != null)
                PrewarmInstances.Add(instance);
        }
        for (int i = 0; i < PrewarmInstances.Count; i++)
            PoolService.Release(PrewarmInstances[i]);
        PrewarmInstances.Clear();
    }

    private void ReleaseSharedPoolBudget()
    {
        if (!spawnedFromPool)
            return;

        spawnedFromPool = false;
        activePooledBursts = Mathf.Max(0, activePooledBursts - 1);
    }

    /// <summary>Existing call sites use the prefab's default material.</summary>
    public static BattleDebrisBurst Spawn(Vector3 position, Vector2 impactDirection)
    {
        BattleDebrisBurst prefab = Resources.Load<BattleDebrisBurst>(ResourcePath);
        return prefab == null ? null :
            Spawn(position, impactDirection, prefab.defaultMaterial);
    }

    /// <summary>Choose Wood, Styrofoam, Metal or Stone for the broken prop.</summary>
    public static BattleDebrisBurst Spawn(Vector3 position, Vector2 impactDirection,
        BattleDebrisMaterial materialType)
    {
        return Spawn(position, impactDirection, materialType, 0, 0);
    }

    /// <summary>
    /// Use a Vector2 world center and per-object inclusive piece-count limits.
    /// The optional z depth is 0; use the Vector3 overload to preserve source depth.
    /// </summary>
    public static BattleDebrisBurst Spawn(Vector2 position, Vector2 impactDirection,
        BattleDebrisMaterial materialType, int minimumPieces, int maximumPieces)
    {
        return Spawn(new Vector3(position.x, position.y, 0f),
            impactDirection, materialType, minimumPieces, maximumPieces);
    }

    /// <summary>
    /// Per-call overrides: values 1..6 are inclusive; 0 means the prefab
    /// default. Pool instances keep no count overrides after the burst ends.
    /// </summary>
    public static BattleDebrisBurst Spawn(Vector3 position, Vector2 impactDirection,
        BattleDebrisMaterial materialType, int minimumPieces, int maximumPieces)
    {
        BattleDebrisBurst prefab = Resources.Load<BattleDebrisBurst>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning("[BattleDebris] Missing prefab at Resources/" + ResourcePath);
            return null;
        }

        PreparePool();
        if (activePooledBursts >= prefab.MaximumConcurrentBursts)
            return null;

        BattleDebrisBurst instance =
            PoolService.Spawn(prefab, position, Quaternion.identity);
        if (instance == null)
            return null;

        instance.spawnedFromPool = true;
        activePooledBursts++;
        instance.Play(position, impactDirection, materialType,
            minimumPieces, maximumPieces);
        return instance.gameObject.activeInHierarchy ? instance : null;
    }

    public void Play(Vector3 position, Vector2 impactDirection)
    {
        Play(position, impactDirection, defaultMaterial, 0, 0);
    }

    public void Play(Vector3 position, Vector2 impactDirection,
        BattleDebrisMaterial materialType)
    {
        Play(position, impactDirection, materialType, 0, 0);
    }

    public void Play(Vector3 position, Vector2 impactDirection,
        BattleDebrisMaterial materialType, int minimumOverride, int maximumOverride)
    {
        StopActive();
        transform.position = position;
        MaterialSheets sheets = GetMaterialSheets(materialType);
        if (sheets == null)
        {
            Debug.LogWarning("[BattleDebris] Material sheet configuration is missing.", this);
            FinishBurst();
            return;
        }

        // Read both axes once per burst; only landing-point geometry chooses
        // which one gets used for a particular piece.
        Sprite[][] vertical = GetClips(sheets.vertical);
        Sprite[][] horizontal = GetClips(sheets.horizontal);
        if (vertical.Length == 0 && horizontal.Length == 0)
        {
            Debug.LogWarning("[BattleDebris] No valid 512x384 sheets for " +
                materialType + ". Assign vertical/horizontal textures.", this);
            FinishBurst();
            return;
        }

        // Clamp separately for each invocation: never modify the shared
        // prefab's serialized limits or leak a previous pooled caller's values.
        int chosenMin = Mathf.Clamp(
            minimumOverride > 0 ? minimumOverride : ConfiguredMinimumPieces,
            1, HardLimit);
        int chosenMax = Mathf.Clamp(
            maximumOverride > 0 ? maximumOverride : ConfiguredMaximumPieces,
            1, HardLimit);
        int low = Mathf.Min(chosenMin, chosenMax);
        int high = Mathf.Max(chosenMin, chosenMax);
        int targetCount = UnityEngine.Random.Range(low, high + 1);
        launchPositions.Clear();
        landingPositions.Clear();
        landingFootprints.Clear();
        activeCount = 0;

        Vector2 impact = impactDirection.sqrMagnitude > 0.0001f ?
            impactDirection.normalized : Vector2.zero;

        // Failed placements don't consume an active slot; try other sizes and
        // landing sectors before accepting fewer pieces.
        int attempts = 0;
        while (activeCount < targetCount && attempts++ < 96)
        {
            PieceSize size = RandomSize();
            SizeMotion motion = GetSizeMotion(size);
            float scale = RandomBetween(motion.scaleMin, motion.scaleMax);
            float radius = CellPixels / Mathf.Max(1f, pixelsPerUnit) *
                scale * 0.7071068f;
            if (!TryPlace(impact, radius, out Vector2 start, out Vector2 landing))
                continue;

            LandingDirection landingDirection = FindLandingDirection(
                landing - (Vector2)position);
            bool verticalAxis = landingDirection == LandingDirection.Up ||
                landingDirection == LandingDirection.Down;
            Sprite[][] clips = verticalAxis ? vertical : horizontal;

            int row = (int)size * 2 + UnityEngine.Random.Range(0, 2);
            // The other A/B variant can be used when one row is blank.
            if (clips.Length != RowsPerSheet || clips[row] == null)
                row = (int)size * 2 + (1 - row % 2);
            if (clips.Length != RowsPerSheet || clips[row] == null)
                continue;

            int firstContactFrame = verticalAxis
                ? motion.landingFrameVerticalFirst
                : motion.landingFrameHorizontalFirst;
            int secondContactFrame = verticalAxis
                ? motion.landingFrameVerticalSecond
                : motion.landingFrameHorizontalSecond;
            Shard shard = Acquire(activeCount);
            BeginShard(shard, clips[row], start, landing, position.z, scale,
                RandomBetween(motion.fpsMin, motion.fpsMax),
                firstContactFrame, secondContactFrame,
                motion.randomFlipX);
            launchPositions.Add(start);
            landingPositions.Add(landing);
            landingFootprints.Add(radius);
            activeCount++;
        }

        playing = activeCount > 0;
        if (!playing)
            FinishBurst();
    }

    [ContextMenu("TEST - Burst (Play Mode)")]
    private void TestBurst()
    {
        if (Application.isPlaying)
            Play(transform.position, Vector2.right);
        else
            Debug.Log("[BattleDebris] Enter Play Mode and use TEST - Burst.", this);
    }

    private bool TryPlace(Vector2 impact, float footprintRadius,
        out Vector2 start, out Vector2 landing)
    {
        float radiusMin = Mathf.Max(0.1f, landingDistanceMin);
        float radiusMax = Mathf.Max(radiusMin, landingDistanceMax);
        for (int attempt = 0; attempt < 28; attempt++)
        {
            float angle = UnityEngine.Random.Range(-Mathf.PI, Mathf.PI);
            Vector2 outgoing = new(Mathf.Cos(angle), Mathf.Sin(angle));
            if (impact.sqrMagnitude > 0.01f)
                outgoing = (outgoing +
                    impact * Mathf.Clamp01(impactDirectionBias)).normalized;

            // Even spread in the annulus rather than clustering toward center.
            float t = UnityEngine.Random.value;
            float distance = Mathf.Sqrt(Mathf.Lerp(
                radiusMin * radiusMin, radiusMax * radiusMax, t));
            Vector2 candidateLanding =
                (Vector2)transform.position + outgoing * distance;
            if (!IsSpacedFootprints(candidateLanding, footprintRadius,
                landingPositions, landingFootprints, landingSpacing))
                continue;

            float startAngle = UnityEngine.Random.Range(-Mathf.PI, Mathf.PI);
            float startRadius = Mathf.Sqrt(UnityEngine.Random.value) * launchRadius;
            Vector2 candidateStart = (Vector2)transform.position +
                new Vector2(Mathf.Cos(startAngle), Mathf.Sin(startAngle)) * startRadius;
            if (!IsSpaced(candidateStart, launchPositions, launchSpacing))
                continue;

            start = candidateStart;
            landing = candidateLanding;
            return true;
        }

        start = landing = default;
        return false;
    }

    private static bool IsSpacedFootprints(Vector2 candidate, float radius,
        List<Vector2> positions, List<float> radii, float gap)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            float required = radius + radii[i] + gap;
            if ((candidate - positions[i]).sqrMagnitude < required * required)
                return false;
        }
        return true;
    }

    private static bool IsSpaced(Vector2 candidate, List<Vector2> existing, float spacing)
    {
        float square = spacing * spacing;
        for (int i = 0; i < existing.Count; i++)
            if ((candidate - existing[i]).sqrMagnitude < square)
                return false;
        return true;
    }

    private Sprite[][] GetClips(Texture2D sheet)
    {
        if (sheet == null)
            return Array.Empty<Sprite[]>();
        if (clipsBySheet.TryGetValue(sheet, out Sprite[][] cached))
            return cached;

        if (sheet.width != SheetWidth || sheet.height != SheetHeight)
        {
            Debug.LogWarning("[BattleDebris] " + sheet.name +
                ": expected exactly 512x384px (8 columns x 6 rows of 64px).", this);
            clipsBySheet[sheet] = Array.Empty<Sprite[]>();
            return clipsBySheet[sheet];
        }

        Color32[] pixels = sheet.isReadable ? sheet.GetPixels32() : null;
        Sprite[][] output = new Sprite[RowsPerSheet][];
        for (int row = 0; row < RowsPerSheet; row++)
        {
            int y = (RowsPerSheet - 1 - row) * CellPixels;
            bool visible = pixels == null;
            if (pixels != null)
                for (int frame = 0; frame < FramesPerPiece && !visible; frame++)
                    visible = HasVisiblePixel(
                        pixels, SheetWidth, frame * CellPixels, y);
            if (!visible)
                continue;

            Sprite[] clip = new Sprite[FramesPerPiece];
            for (int frame = 0; frame < FramesPerPiece; frame++)
            {
                Sprite sprite = Sprite.Create(sheet,
                    new Rect(frame * CellPixels, y, CellPixels, CellPixels),
                    new Vector2(0.5f, 0.5f), Mathf.Max(1f, pixelsPerUnit),
                    0, SpriteMeshType.FullRect);
                sprite.name = sheet.name + "_row_" + row + "_frame_" + frame;
                sprite.hideFlags = HideFlags.HideAndDontSave;
                clip[frame] = sprite;
            }
            output[row] = clip;
        }

        clipsBySheet.Add(sheet, output);
        return output;
    }

    private static bool HasVisiblePixel(Color32[] pixels, int width, int x, int y)
    {
        for (int py = 0; py < CellPixels; py++)
        for (int px = 0; px < CellPixels; px++)
            if (pixels[(y + py) * width + x + px].a > 8)
                return true;
        return false;
    }

    private Shard Acquire(int index)
    {
        if (index < shards.Count)
            return shards[index];

        // The prefab includes six author-visible children, each with its own
        // Visual SpriteRenderer and ContactShadow SpriteRenderer.
        // A manually placed empty emitter still has a runtime fallback.
        Transform pieceRoot = transform.Find("DebrisPiece_" + index);
        if (pieceRoot == null)
        {
            GameObject created = new("DebrisPiece_" + index);
            pieceRoot = created.transform;
            pieceRoot.SetParent(transform, false);
        }

        Transform body = pieceRoot.Find("Visual");
        if (body == null)
        {
            body = new GameObject("Visual").transform;
            body.SetParent(pieceRoot, false);
        }
        SpriteRenderer visual = body.GetComponent<SpriteRenderer>();
        if (visual == null)
            visual = body.gameObject.AddComponent<SpriteRenderer>();
        if (debrisMaterial != null)
            visual.sharedMaterial = debrisMaterial;

        Transform ground = pieceRoot.Find("ContactShadow");
        if (ground == null)
        {
            ground = new GameObject("ContactShadow").transform;
            ground.SetParent(pieceRoot, false);
        }
        SpriteRenderer shadow = ground.GetComponent<SpriteRenderer>();
        if (shadow == null)
            shadow = ground.gameObject.AddComponent<SpriteRenderer>();
        shadow.sprite = GetShadowSprite();
        shadow.color = new Color(0.04f, 0.05f, 0.08f, 0.26f);

        Shard shard = new()
        {
            rootObject = pieceRoot.gameObject,
            root = pieceRoot,
            spriteTransform = body,
            visual = visual,
            shadow = shadow
        };
        shards.Add(shard);
        return shard;
    }

    private Sprite GetShadowSprite()
    {
        if (shadowSprite != null)
            return shadowSprite;

        const int w = 16;
        const int h = 8;
        shadowTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color32[] pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float nx = (x + 0.5f - w / 2f) / (w / 2f);
            float ny = (y + 0.5f - h / 2f) / (h / 2f);
            byte alpha = nx * nx + ny * ny <= 1f ? (byte)160 : (byte)0;
            pixels[y * w + x] = new Color32(12, 14, 22, alpha);
        }
        shadowTexture.SetPixels32(pixels);
        shadowTexture.Apply(false, true);
        shadowSprite = Sprite.Create(shadowTexture,
            new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 16f);
        shadowSprite.hideFlags = HideFlags.HideAndDontSave;
        return shadowSprite;
    }

    private void BeginShard(Shard shard, Sprite[] frames,
        Vector2 start, Vector2 landing, float z, float size,
        float fps, int landingFrameFirst, int landingFrameSecond,
        bool allowFlip)
    {
        shard.rootObject.SetActive(true);
        shard.visual.enabled = true;
        shard.shadow.enabled = true;
        shard.frames = frames;
        shard.fps = Mathf.Max(1f, fps);
        shard.landingFrameFirst = Mathf.Clamp(
            landingFrameFirst, 0, FramesPerPiece - 1);
        shard.landingFrameSecond = Mathf.Clamp(
            landingFrameSecond, 0, FramesPerPiece - 1);
        shard.landingFrame = shard.landingFrameFirst;
        shard.startingFrame = randomStartFrame
            ? UnityEngine.Random.Range(0, FramesPerPiece) : 0;
        shard.visual.sprite = frames[shard.startingFrame];
        shard.visual.flipX = allowFlip && UnityEngine.Random.value < 0.5f;
        shard.visual.flipY = false;

        shard.start = start;
        shard.landing = landing;
        shard.worldZ = z;
        shard.size = size;
        shard.height = initialHeight;
        shard.hasHitGround = false;
        shard.landedAge = 0f;

        Vector2 travel = landing - start;
        Vector2 travelDirection = travel.sqrMagnitude > 0.00001f ?
            travel.normalized : Vector2.right;
        float slide = RandomBetween(landingSlideMin, landingSlideMax);
        shard.flightLanding = landing - travelDirection * slide;
        shard.curvePerpendicular = new Vector2(-travelDirection.y, travelDirection.x);
        shard.curveOffset = RandomBetween(curveMin, curveMax) *
            (UnityEngine.Random.value < 0.5f ? -1f : 1f);
        shard.wobblePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        float riseSpeed = RandomBetween(riseVelocityMin, riseVelocityMax);
        float g = Mathf.Max(0.1f, gravity);
        shard.arcStartHeight = Mathf.Max(0f, initialHeight);
        shard.arcRiseSpeed = riseSpeed;
        shard.arcElapsed = 0f;
        shard.arcDuration = (riseSpeed +
            Mathf.Sqrt(riseSpeed * riseSpeed + 2f * g * shard.arcStartHeight)) / g;
        shard.firstLandingTime = shard.arcDuration;

        shard.age = shard.restingAge = 0f;
        shard.bounceCount = 0;
        shard.resting = false;
        shard.active = true;

        int layer = SortingLayer.NameToID(sortingLayerName);
        shard.visual.sortingLayerID = layer;
        shard.shadow.sortingLayerID = layer;
        shard.visual.sortingOrder = sortingOrder + 1;
        shard.shadow.sortingOrder = sortingOrder;
        shard.visual.color = Color.white;
        shard.spriteTransform.localScale = Vector3.one * shard.size;
        shard.spriteTransform.localRotation = Quaternion.identity;
        shard.spriteTransform.localPosition = Vector3.zero;
        shard.shadow.transform.localScale =
            new Vector3(shard.size * 0.85f, shard.size * 0.7f, 1f);
        shard.root.position = new Vector3(start.x, start.y, z);
    }

    private void Update()
    {
        if (!playing)
            return;

        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f)
            return;

        int alive = 0;
        for (int i = 0; i < activeCount; i++)
        {
            Shard shard = shards[i];
            if (!shard.active)
                continue;

            Tick(shard, dt);
            if (shard.active)
                alive++;
        }

        if (alive == 0)
            FinishBurst();
    }

    private int FlightFrameIndex(Shard shard)
    {
        int frame = Mathf.FloorToInt(shard.age * shard.fps) + shard.startingFrame;
        return loopDuringFlight ? frame % FramesPerPiece :
            Mathf.Min(frame, FramesPerPiece - 1);
    }

    private int ChooseLandingFrame(int currentFrame, int first, int second)
    {
        first = Mathf.Clamp(first, 0, FramesPerPiece - 1);
        second = Mathf.Clamp(second, 0, FramesPerPiece - 1);
        int deltaFirst = Mathf.Abs(currentFrame - first);
        int deltaSecond = Mathf.Abs(currentFrame - second);
        if (loopDuringFlight)
        {
            deltaFirst = Mathf.Min(deltaFirst, FramesPerPiece - deltaFirst);
            deltaSecond = Mathf.Min(deltaSecond, FramesPerPiece - deltaSecond);
        }
        if (deltaFirst == deltaSecond)
            return UnityEngine.Random.value < 0.5f ? first : second;
        return deltaFirst < deltaSecond ? first : second;
    }

    private void Tick(Shard shard, float dt)
    {
        shard.age += dt;
        if (!shard.resting)
        {
            shard.arcElapsed = Mathf.Min(shard.arcElapsed + dt, shard.arcDuration);
            float g = Mathf.Max(0.1f, gravity);
            float t = shard.arcElapsed;
            shard.height = Mathf.Max(0f, shard.arcStartHeight +
                shard.arcRiseSpeed * t - 0.5f * g * t * t);

            if (shard.arcElapsed >= shard.arcDuration)
            {
                shard.height = 0f;
                if (!shard.hasHitGround)
                {
                    shard.hasHitGround = true;
                    shard.landedAge = 0f;
                    int currentFrame = FlightFrameIndex(shard);
                    shard.landingFrame = ChooseLandingFrame(
                        currentFrame, shard.landingFrameFirst,
                        shard.landingFrameSecond);
                    shard.visual.sprite = shard.frames[shard.landingFrame];
                }

                float speedOnImpact =
                    Mathf.Abs(shard.arcRiseSpeed - g * shard.arcDuration);
                float rebound = speedOnImpact * bounceRetention;
                if (shard.bounceCount < maximumBounces && rebound > 0.35f)
                {
                    shard.bounceCount++;
                    shard.arcStartHeight = 0f;
                    shard.arcRiseSpeed = rebound;
                    shard.arcElapsed = 0f;
                    shard.arcDuration = 2f * rebound / g;
                }
                else
                {
                    shard.resting = true;
                    shard.restingAge = 0f;
                }
            }
        }
        else
        {
            shard.restingAge += dt;
            if (shard.restingAge >= settleDuration + restDuration + blinkDuration)
            {
                shard.active = false;
                shard.rootObject.SetActive(false);
                return;
            }
        }

        if (shard.hasHitGround)
            shard.landedAge += dt;

        Vector2 ground;
        if (!shard.hasHitGround)
        {
            float u = Mathf.Clamp01(
                shard.age / Mathf.Max(0.05f, shard.firstLandingTime));
            float arch = 4f * u * (1f - u);
            float side = arch * shard.curveOffset +
                Mathf.Sin(u * Mathf.PI * 4f + shard.wobblePhase) *
                Mathf.Sin(u * Mathf.PI) * inFlightWobble;
            ground = Vector2.Lerp(shard.start, shard.flightLanding, u) +
                shard.curvePerpendicular * side;
        }
        else
        {
            // A short directional slide after the *first* landing, even when
            // subsequent smaller hops are still taking place.
            float u = Mathf.Clamp01(
                shard.landedAge / Mathf.Max(0.01f, landingSlideDuration));
            float eased = 1f - (1f - u) * (1f - u);
            ground = Vector2.Lerp(shard.flightLanding, shard.landing, eased);
        }
        shard.root.position = new Vector3(ground.x, ground.y, shard.worldZ);

        // Bird-eye 2.5D: ground stays in XY and visual rises relative to shadow.
        float height = shard.height;
        shard.spriteTransform.localPosition =
            new Vector3(0f, height * heightToScreenY, 0f);
        float apparentScale = 1f + height * perspectiveScalePerHeight;
        shard.spriteTransform.localScale =
            Vector3.one * shard.size * apparentScale;
        float shadowFactor = Mathf.Max(0.12f, 1f - height * shadowShrinkPerHeight);
        shard.shadow.transform.localScale = new Vector3(
            shard.size * 0.85f * shadowFactor,
            shard.size * 0.7f * shadowFactor, 1f);
        shard.shadow.color = new Color(0.04f, 0.05f, 0.08f,
            0.26f / (1f + height * shadowFadePerHeight));

        // Before first contact use randomized per-piece frame rate and phase.
        // At contact lock the direction-specific frame; bounce/settle retain it.
        if (!shard.hasHitGround)
        {
            shard.visual.sprite = shard.frames[FlightFrameIndex(shard)];
        }
        else
            shard.visual.sprite = shard.frames[shard.landingFrame];

        bool blinking = shard.resting &&
            shard.restingAge >= settleDuration + restDuration;
        float blinkAge = shard.restingAge - settleDuration - restDuration;
        shard.visual.enabled = !blinking ||
            Mathf.FloorToInt(blinkAge * blinkFrequency * 2f) % 2 == 0;
        shard.shadow.enabled = !blinking;
    }

    private void FinishBurst()
    {
        playing = false;
        if (spawnedFromPool)
        {
            ReleaseSharedPoolBudget();
            PoolService.Release(gameObject);
        }
    }

    private void StopActive()
    {
        playing = false;
        for (int i = 0; i < shards.Count; i++)
        {
            shards[i].active = false;
            shards[i].rootObject.SetActive(false);
        }
        activeCount = 0;
    }

    private void OnDisable()
    {
        // Also release the slot budget if another system disables the effect.
        ReleaseSharedPoolBudget();
        StopActive();
    }

    private static void ValidateMotion(SizeMotion motion)
    {
        if (motion == null) return;
        motion.fpsMin = Mathf.Max(1f, motion.fpsMin);
        motion.fpsMax = Mathf.Max(1f, motion.fpsMax);
        motion.scaleMin = Mathf.Clamp(motion.scaleMin, 0.05f, 2f);
        motion.scaleMax = Mathf.Clamp(motion.scaleMax, 0.05f, 2f);
        motion.landingFrameVerticalFirst = Mathf.Clamp(
            motion.landingFrameVerticalFirst, 0, FramesPerPiece - 1);
        motion.landingFrameVerticalSecond = Mathf.Clamp(
            motion.landingFrameVerticalSecond, 0, FramesPerPiece - 1);
        motion.landingFrameHorizontalFirst = Mathf.Clamp(
            motion.landingFrameHorizontalFirst, 0, FramesPerPiece - 1);
        motion.landingFrameHorizontalSecond = Mathf.Clamp(
            motion.landingFrameHorizontalSecond, 0, FramesPerPiece - 1);
    }

    private void OnValidate()
    {
        maximumPieces = Mathf.Clamp(maximumPieces, 1, HardLimit);
        minimumPieces = Mathf.Clamp(minimumPieces, 1, maximumPieces);
        maximumPieceSlots = Mathf.Clamp(
            maximumPieceSlots, maximumPieces, MaximumPoolSlots);
        prewarmPieceSlots = Mathf.Clamp(
            prewarmPieceSlots, 0, maximumPieceSlots);
        ValidateMotion(small);
        ValidateMotion(medium);
        ValidateMotion(large);
    }

    private void OnDestroy()
    {
        foreach (Sprite[][] clips in clipsBySheet.Values)
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null) continue;
            for (int f = 0; f < clips[i].Length; f++)
                if (clips[i][f] != null)
                    Destroy(clips[i][f]);
        }
        clipsBySheet.Clear();
        if (shadowSprite != null)
            Destroy(shadowSprite);
        if (shadowTexture != null)
            Destroy(shadowTexture);
    }
}

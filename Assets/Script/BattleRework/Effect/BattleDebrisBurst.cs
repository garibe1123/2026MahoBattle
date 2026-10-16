using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared, visual-only 2.5D debris burst for breakable movie-set props.
/// Drop a transparent sprite sheet into Common Sheet (64x64 cells), or override
/// individual directional groups. One Update drives all pooled visual shards:
/// no Rigidbody2D, Collider2D, per-frame sprite editing or Animator required.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleDebrisBurst : MonoBehaviour
{
    private const int CellPixels = 64;
    private const int HardLimit = 6;
    private const int MaximumPoolSlots = 600;
    private const string ResourcePath = "BattleEffects/PF_BattleDebrisBurst";

    [Serializable]
    private sealed class DirectionGroup
    {
        [Min(0)] public int count = 3;
        [Tooltip("Optional: a separate 64px-cell sheet for this direction. Empty = Common Sheet.")]
        public Texture2D sheetOverride;
    }

    [Header("64 x 64 Sprite Sheet")]
    [Tooltip("Transparent PNG with an integer number of 64x64 cells. No manual Sprite Editor slicing required.")]
    [SerializeField] private Texture2D commonSheet;
    [SerializeField, Min(1f)] private float pixelsPerUnit = 64f;
    [SerializeField] private Material debrisMaterial;
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 25;

    [Header("Directional Groups (screen-space XY)")]
    [SerializeField] private DirectionGroup down = new();
    [SerializeField] private DirectionGroup up = new();
    [SerializeField] private DirectionGroup left = new();
    [SerializeField] private DirectionGroup right = new();

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
    [SerializeField, Range(0f, 85f)] private float directionalSpread = 38f;
    [SerializeField, Range(0f, 0.7f)] private float impactDirectionBias = 0.18f;
    [SerializeField, Range(0.1f, 1f)] private float sizeMin = 0.30f;
    [SerializeField, Range(0.1f, 1f)] private float sizeMax = 0.54f;

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
        public float vy;
        public float age;
        public float firstLandingTime;
        public float rotation;
        public float angularSpeed;
        public float settleAge;
        public float restingAge;
        public int bounceCount;
        public bool resting;
        public bool active;
    }

    private readonly List<Shard> shards = new();
    private readonly List<Vector2> launchPositions = new();
    private readonly List<Vector2> landingPositions = new();
    private readonly List<float> landingFootprints = new();
    private readonly List<int> directions = new();
    private readonly Dictionary<Texture2D, Sprite[]> slicesBySheet = new();
    private readonly int[] lastPicked = { -1, -1, -1, -1 };
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

    /// <summary>
    /// One-line shared call from an obstacle, projectile impact, or scripted destruction.
    /// Configure the Resources prefab once and invoke this from the destruction event.
    /// </summary>
    public static BattleDebrisBurst Spawn(Vector3 position, Vector2 impactDirection)
    {
        BattleDebrisBurst prefab = Resources.Load<BattleDebrisBurst>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning("[BattleDebris] Configure a prefab at Resources/" + ResourcePath);
            return null;
        }

        PreparePool();
        if (activePooledBursts >= prefab.MaximumConcurrentBursts)
            return null; // The shared effect budget is full; never instantiate unbounded pieces.

        BattleDebrisBurst instance = PoolService.Spawn(
            prefab, position, Quaternion.identity);
        if (instance == null)
            return null;

        instance.spawnedFromPool = true;
        activePooledBursts++;
        instance.Play(position, impactDirection);
        return instance.gameObject.activeInHierarchy ? instance : null;
    }

    /// <summary>Reusable for a scene-placed emitter too. Can be called repeatedly.</summary>
    public void Play(Vector3 position, Vector2 impactDirection)
    {
        StopActive();
        transform.position = position;
        int maximum = ConfiguredMaximumPieces;
        int minimum = ConfiguredMinimumPieces;
        int requestedCount = UnityEngine.Random.Range(minimum, maximum + 1);

        Sprite[][] available = new Sprite[4][];
        DirectionGroup[] groups = { down, up, left, right };
        directions.Clear();
        for (int d = 0; d < groups.Length; d++)
        {
            Texture2D sheet = groups[d].sheetOverride != null
                ? groups[d].sheetOverride : commonSheet;
            available[d] = GetSlices(sheet);
            if (available[d].Length == 0)
                continue;
            int requested = Mathf.Clamp(groups[d].count, 0, HardLimit);
            for (int j = 0; j < requested; j++)
                directions.Add(d);
        }

        if (directions.Count == 0)
        {
            Debug.LogWarning("[BattleDebris] No valid 64x64 sprites. Assign a transparent Common Sheet.", this);
            FinishBurst();
            return;
        }

        // Shuffle directional requests first, so groups are spatially interleaved.
        for (int i = directions.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (directions[i], directions[j]) = (directions[j], directions[i]);
        }

        launchPositions.Clear();
        landingPositions.Clear();
        landingFootprints.Clear();
        int targetCount = Mathf.Min(requestedCount, directions.Count);
        Vector2 impulse = impactDirection.sqrMagnitude > 0.0001f
            ? impactDirection.normalized : Vector2.zero;
        activeCount = 0;

        for (int i = 0; i < directions.Count && activeCount < targetCount; i++)
        {
            int d = directions[i];
            float size = UnityEngine.Random.Range(
                Mathf.Min(sizeMin, sizeMax), Mathf.Max(sizeMin, sizeMax));
            float pieceDiameter = CellPixels / Mathf.Max(1f, pixelsPerUnit) * size;
            // Circumscribed circle protects a rotated square at final rest.
            float radius = pieceDiameter * 0.707107f;
            if (!TryPlace(d, impulse, radius, out Vector2 start, out Vector2 landing))
                continue; // Never force overlapping placements when space is full.

            Sprite[] options = available[d];
            int pick = UnityEngine.Random.Range(0, options.Length);
            if (options.Length > 1 && pick == lastPicked[d])
                pick = (pick + UnityEngine.Random.Range(1, options.Length)) % options.Length;
            lastPicked[d] = pick;

            Shard shard = Acquire(activeCount);
            BeginShard(shard, options[pick], start, landing, position.z, size);
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

    private bool TryPlace(int direction, Vector2 impact, float footprintRadius,
        out Vector2 start, out Vector2 landing)
    {
        Vector2 cardinal = direction switch
        {
            0 => Vector2.down,
            1 => Vector2.up,
            2 => Vector2.left,
            _ => Vector2.right
        };

        Vector2 axis = impact.sqrMagnitude > 0.01f
            ? Vector2.Lerp(cardinal, impact, impactDirectionBias).normalized
            : cardinal;

        float radiusMin = Mathf.Max(0.1f, landingDistanceMin);
        float radiusMax = Mathf.Max(radiusMin, landingDistanceMax);
        for (int attempt = 0; attempt < 28; attempt++)
        {
            float degrees = UnityEngine.Random.Range(-directionalSpread, directionalSpread);
            Vector2 outgoing = Quaternion.Euler(0f, 0f, degrees) * axis;
            float range = UnityEngine.Random.Range(radiusMin, radiusMax);
            Vector2 candidateLanding = (Vector2)transform.position + outgoing * range;
            if (!IsSpacedFootprints(candidateLanding, footprintRadius,
                landingPositions, landingFootprints, landingSpacing))
                continue;

            float angle = UnityEngine.Random.Range(-180f, 180f) * Mathf.Deg2Rad;
            float radius = Mathf.Sqrt(UnityEngine.Random.value) * launchRadius;
            Vector2 candidateStart = (Vector2)transform.position +
                new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
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

    private Sprite[] GetSlices(Texture2D sheet)
    {
        if (sheet == null)
            return Array.Empty<Sprite>();
        if (slicesBySheet.TryGetValue(sheet, out Sprite[] cached))
            return cached;

        if (sheet.width % CellPixels != 0 || sheet.height % CellPixels != 0)
        {
            Debug.LogWarning(
                "[BattleDebris] " + sheet.name +
                ": sheet dimensions must be multiples of 64px; no sprites generated.", this);
            slicesBySheet[sheet] = Array.Empty<Sprite>();
            return slicesBySheet[sheet];
        }

        int columns = sheet.width / CellPixels;
        int rows = sheet.height / CellPixels;
        // Detect transparent cells only when Read/Write is enabled. Sprite.Create
        // itself works with non-readable textures; unreadable blank cells remain.
        Color32[] pixels = sheet.isReadable ? sheet.GetPixels32() : null;
        List<Sprite> output = new(columns * rows);
        for (int y = rows - 1; y >= 0; y--)
        for (int x = 0; x < columns; x++)
        {
            if (pixels != null && !HasVisiblePixel(
                pixels, sheet.width, x * CellPixels, y * CellPixels))
                continue;

            Sprite sprite = Sprite.Create(sheet,
                new Rect(x * CellPixels, y * CellPixels, CellPixels, CellPixels),
                new Vector2(0.5f, 0.5f), Mathf.Max(1f, pixelsPerUnit),
                0, SpriteMeshType.FullRect);
            sprite.name = sheet.name + "_piece_" + x + "_" + y;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            output.Add(sprite);
        }
        slicesBySheet.Add(sheet, output.ToArray());
        return slicesBySheet[sheet];
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

        GameObject root = new("DebrisPiece_" + index);
        root.transform.SetParent(transform, false);
        GameObject body = new("Visual");
        body.transform.SetParent(root.transform, false);
        SpriteRenderer visual = body.AddComponent<SpriteRenderer>();
        if (debrisMaterial != null)
            visual.sharedMaterial = debrisMaterial;

        GameObject ground = new("ContactShadow");
        ground.transform.SetParent(root.transform, false);
        SpriteRenderer shadow = ground.AddComponent<SpriteRenderer>();
        shadow.sprite = GetShadowSprite();
        shadow.color = new Color(0.04f, 0.05f, 0.08f, 0.26f);

        Shard shard = new()
        {
            rootObject = root,
            root = root.transform,
            spriteTransform = body.transform,
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

    private void BeginShard(Shard shard, Sprite sprite, Vector2 start,
        Vector2 landing, float z, float size)
    {
        shard.rootObject.SetActive(true);
        shard.visual.enabled = true;
        shard.shadow.enabled = true;
        shard.visual.sprite = sprite;
        shard.start = start;
        shard.landing = landing;
        shard.worldZ = z;
        shard.size = size;
        shard.height = initialHeight;
        shard.vy = UnityEngine.Random.Range(
            Mathf.Min(riseVelocityMin, riseVelocityMax),
            Mathf.Max(riseVelocityMin, riseVelocityMax));
        shard.firstLandingTime = (shard.vy +
            Mathf.Sqrt(shard.vy * shard.vy + 2f * gravity * initialHeight)) /
            Mathf.Max(0.1f, gravity);
        shard.angularSpeed = UnityEngine.Random.Range(-560f, 560f);
        shard.rotation = UnityEngine.Random.Range(-45f, 45f);
        shard.age = shard.settleAge = shard.restingAge = 0f;
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

    private void Tick(Shard shard, float dt)
    {
        shard.age += dt;
        if (!shard.resting)
        {
            shard.vy -= gravity * dt;
            shard.height += shard.vy * dt;
            if (shard.height <= 0f)
            {
                shard.height = 0f;
                if (shard.bounceCount < maximumBounces &&
                    Mathf.Abs(shard.vy) * bounceRetention > 0.35f)
                {
                    shard.vy = -shard.vy * bounceRetention;
                    shard.bounceCount++;
                    shard.angularSpeed *= 0.45f;
                }
                else
                {
                    shard.resting = true;
                    shard.restingAge = 0f;
                    shard.angularSpeed = 0f;
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

        float movement = Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01(shard.age / Mathf.Max(0.05f, shard.firstLandingTime)));
        Vector2 ground = Vector2.LerpUnclamped(shard.start, shard.landing, movement);
        shard.root.position = new Vector3(ground.x, ground.y, shard.worldZ);
        shard.spriteTransform.localPosition = Vector3.up * shard.height;

        if (!shard.resting)
            shard.rotation += shard.angularSpeed * dt;
        else if (shard.restingAge < settleDuration)
        {
            float t = shard.restingAge / Mathf.Max(0.001f, settleDuration);
            shard.rotation = Mathf.Sin(t * 19f) * 13f * (1f - t);
        }
        shard.spriteTransform.localRotation =
            Quaternion.Euler(0f, 0f, shard.rotation);

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

    private void OnValidate()
    {
        maximumPieces = Mathf.Clamp(maximumPieces, 1, HardLimit);
        minimumPieces = Mathf.Clamp(minimumPieces, 1, maximumPieces);
        maximumPieceSlots = Mathf.Clamp(
            maximumPieceSlots, maximumPieces, MaximumPoolSlots);
        prewarmPieceSlots = Mathf.Clamp(
            prewarmPieceSlots, 0, maximumPieceSlots);
    }

    private void OnDestroy()
    {
        foreach (Sprite[] sprites in slicesBySheet.Values)
            for (int i = 0; i < sprites.Length; i++)
                if (sprites[i] != null)
                    Destroy(sprites[i]);
        slicesBySheet.Clear();
        if (shadowSprite != null)
            Destroy(shadowSprite);
        if (shadowTexture != null)
            Destroy(shadowTexture);
    }
}

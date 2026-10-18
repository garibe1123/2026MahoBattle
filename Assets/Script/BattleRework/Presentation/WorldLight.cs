using UnityEngine;
using UnityEngine.SceneManagement;

public enum BattleShadowMode { None, Contact, Projected }

[DisallowMultipleComponent]
[DefaultExecutionOrder(32690)]
public sealed class WorldLight : MonoBehaviour
{
    public static WorldLight Instance { get; private set; }
    public BattleShadowMode shadowMode = BattleShadowMode.Projected;
    [Tooltip("Direction toward the light in world XY; shadows extend in the opposite direction.")]
    public Vector2 lightDirection = new(-0.55f, 0.85f);
    [Min(0f)] public float shadowLength = 1.4f;
    [Min(0.01f)] public float shadowWidth = 1f;
    public Color shadowColor = new(0.045f, 0.055f, 0.075f, 1f);
    [Range(0f, 1f)] public float shadowOpacity = 0.19f;
    [Tooltip("Enable to keep these Inspector values instead of initializing from the room profile.")]
    public bool overrideProfileDefaults;
    public BattleLightingProfileSO initialProfile;
    [Tooltip("Use the same Sorting Layer as the battle floor. ShadowOrder keeps shadows below world actors.")]
    public string shadowSortingLayerName = "Default";
    [SerializeField] private Camera targetCamera;

#if UNITY_EDITOR
    [Header("Shadow Connection Preview")]
    public bool previewConnectSpriteBottom;
    [Range(0f, 0.5f)] public float previewBottomInset;
    public bool showShadowDebugRays = true;
    [Min(0.05f)] public float shadowDebugRayLength = 1f;
    public Camera DebugCamera => targetCamera;
#endif

    private bool initialized;
    private WorldLight previous;
    private BattleShadowFloorMask floorMask;
    private readonly Plane[] cameraPlanes = new Plane[6];
    private bool cameraReady;

    public static BattleShadowMode Mode => Instance != null && Instance.isActiveAndEnabled
        ? Instance.shadowMode : BattleShadowMode.Contact;
    public Vector2 ShadowDirection => lightDirection.sqrMagnitude > 0.0001f
        ? -lightDirection.normalized : new Vector2(0.55f, -0.85f).normalized;
    public int ShadowSortingLayerId => SortingLayer.NameToID(shadowSortingLayerName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        Instance = null;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        WorldLight authored = FindFirstObjectByType<WorldLight>();
        if (authored != null || FindFirstObjectByType<BattleRunManager>() == null)
            return;
        GameObject host = new("WorldLight");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<WorldLight>();
    }

    private void Awake()
    {
        floorMask = gameObject.AddComponent<BattleShadowFloorMask>();
        if (targetCamera == null)
            targetCamera = Camera.main;
        BattleFieldCinematicDirector director = FindFirstObjectByType<BattleFieldCinematicDirector>();
        InitializeFromProfile(initialProfile != null ? initialProfile : director != null ? director.ActiveLightingProfile : null);
    }

    private void OnEnable()
    {
        previous = Instance;
        Instance = this;
    }

    private void LateUpdate()
    {
        cameraReady = targetCamera != null && targetCamera.isActiveAndEnabled;
        if (cameraReady)
            GeometryUtility.CalculateFrustumPlanes(targetCamera, cameraPlanes);
    }

    public bool IsVisible(Bounds bounds) => !cameraReady || GeometryUtility.TestPlanesAABB(cameraPlanes, bounds);

    private void OnDisable()
    {
        if (Instance == this)
            Instance = previous != null && previous.isActiveAndEnabled ? previous : null;
    }

    private void OnDestroy()
    {
        if (floorMask != null)
            Destroy(floorMask);
    }

    public static void ApplyInitialProfile(BattleLightingProfileSO profile)
    {
        if (Instance != null)
            Instance.InitializeFromProfile(profile);
    }

    private void InitializeFromProfile(BattleLightingProfileSO profile)
    {
        if (initialized || overrideProfileDefaults || profile == null)
            return;
        lightDirection = profile.spriteVolumeFillDirection;
        shadowColor = new Color(0.045f * profile.spriteVolumeFillColor.r,
            0.055f * profile.spriteVolumeFillColor.g, 0.075f * profile.spriteVolumeFillColor.b, 1f);
        shadowOpacity = profile.contactShadowOpacity;
        initialized = true;
    }

    public static BattleSpriteShadow Attach(SpriteRenderer source)
    {
        if (source == null)
            return null;
        BattleSpriteShadow shadow = source.GetComponent<BattleSpriteShadow>();
        PlayerController player = source.GetComponentInParent<PlayerController>(true);
        MonsterController monster = source.GetComponentInParent<MonsterController>(true);
        Transform owner = player != null ? player.transform : monster != null ? monster.transform : null;
        if (owner == null)
            return shadow;
        if (shadow == null)
            shadow = source.gameObject.AddComponent<BattleSpriteShadow>();
        shadow.SetCharacterOwner(owner);
        return shadow;
    }
}

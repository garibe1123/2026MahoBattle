using UnityEngine;

/// <summary>
/// Attach to a destructible GameObject to configure its debris on that object.
/// BattleObstacle.Broken is supported automatically; other objects should call
/// Burst() from their actual destruction event, before they are destroyed.
/// Disabling/destroying this component by itself never causes a burst.
/// </summary>
[DisallowMultipleComponent]
public sealed class BurstObj : MonoBehaviour
{
    [Header("Burst Piece")]
    [SerializeField] private BattleDebrisMaterial material = BattleDebrisMaterial.Wood;
    [SerializeField, Range(1, 6)] private int minimumPieces = 3;
    [SerializeField, Range(1, 6)] private int maximumPieces = 6;

    [Header("Burst Center (world XY)")]
    [Tooltip("Optional collider used to obtain the object's bounds center. " +
        "Auto-detected when empty; its center is cached before destruction.")]
    [SerializeField] private Collider2D centerCollider;
    [Tooltip("Fallback center if no enabled Collider2D is available.")]
    [SerializeField] private Renderer centerRenderer;
    [Tooltip("Local-space offset from the detected object center, in world units.")]
    [SerializeField] private Vector2 localCenterOffset;

    [Header("Activation")]
    [Tooltip("Subscribe to BattleObstacle.Broken if a BattleObstacle exists on " +
        "this object or a parent. Other breakables should invoke Burst() explicitly.")]
    [SerializeField] private bool burstOnBattleObstacleBroken = true;
    [Tooltip("Zero gives radial, uniformly randomized scatter. " +
        "A nonzero vector biases debris away from the impact direction.")]
    [SerializeField] private Vector2 defaultImpactDirection = Vector2.zero;
    [Tooltip("Prevents the same destruction event from spawning multiple bursts. " +
        "Reset automatically when the object is enabled again.")]
    [SerializeField] private bool oncePerEnable = true;

    private BattleObstacle battleObstacle;
    private Vector3 cachedLocalCenter;
    private bool hasCachedCenter;
    private bool burstTriggered;
    private bool subscribed;

    public BattleDebrisMaterial Material => material;
    public int MinimumPieces => Mathf.Clamp(minimumPieces, 1, 6);
    public int MaximumPieces => Mathf.Clamp(maximumPieces, MinimumPieces, 6);

    private void Awake()
    {
        ResolveReferences();
        CacheCenterIfAvailable();
    }

    private void OnEnable()
    {
        burstTriggered = false;
        ResolveReferences();
        CacheCenterIfAvailable();

        if (burstOnBattleObstacleBroken && battleObstacle != null)
        {
            battleObstacle.Broken -= HandleBattleObstacleBroken;
            battleObstacle.Broken += HandleBattleObstacleBroken;
            subscribed = true;
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
        // Do not emit during pooling, unloading a scene, or Destroy().
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;
        if (battleObstacle != null)
            battleObstacle.Broken -= HandleBattleObstacleBroken;
        subscribed = false;
    }

    private void HandleBattleObstacleBroken(BattleObstacle obstacle)
    {
        Burst();
    }

    private void ResolveReferences()
    {
        if (battleObstacle == null)
        {
            battleObstacle = GetComponent<BattleObstacle>();
            if (battleObstacle == null)
                battleObstacle = GetComponentInParent<BattleObstacle>();
        }

        if (centerCollider == null)
        {
            centerCollider = GetComponent<Collider2D>();
            if (centerCollider == null)
                centerCollider = GetComponentInChildren<Collider2D>();
        }
        if (centerRenderer == null)
        {
            centerRenderer = GetComponent<Renderer>();
            if (centerRenderer == null)
                centerRenderer = GetComponentInChildren<Renderer>();
        }
    }

    private void CacheCenterIfAvailable()
    {
        if (centerCollider != null && centerCollider.enabled &&
            centerCollider.gameObject.activeInHierarchy)
        {
            cachedLocalCenter = transform.InverseTransformPoint(
                centerCollider.bounds.center);
            hasCachedCenter = true;
            return;
        }

        if (centerRenderer != null && centerRenderer.enabled &&
            centerRenderer.gameObject.activeInHierarchy)
        {
            cachedLocalCenter = transform.InverseTransformPoint(
                centerRenderer.bounds.center);
            hasCachedCenter = true;
        }
    }

    private Vector2 ResolveCenter()
    {
        // Broken may be invoked after colliders have been disabled, so prefer
        // the cached local center instead of disabled Collider2D.bounds.
        Vector3 worldCenter;
        if (hasCachedCenter)
        {
            worldCenter = transform.TransformPoint(cachedLocalCenter);
        }
        else
        {
            CacheCenterIfAvailable();
            worldCenter = hasCachedCenter
                ? transform.TransformPoint(cachedLocalCenter)
                : transform.position;
        }

        worldCenter += transform.TransformVector(
            new Vector3(localCenterOffset.x, localCenterOffset.y, 0f));
        return new Vector2(worldCenter.x, worldCenter.y);
    }

    /// <summary>
    /// Called by UnityEvents or destructible scripts immediately on breaking.
    /// For BattleObstacle, the Broken event already invokes this automatically.
    /// </summary>
    public void Burst()
    {
        TryBurstAt(ResolveCenter(), defaultImpactDirection);
    }

    /// <summary>Explicit world-space Vector2 destruction center.</summary>
    public void BurstAt(Vector2 worldPosition)
    {
        TryBurstAt(worldPosition, defaultImpactDirection);
    }

    /// <summary>Explicit center and directional impulse from an impact.</summary>
    public void BurstAt(Vector2 worldPosition, Vector2 impactDirection)
    {
        TryBurstAt(worldPosition, impactDirection);
    }

    /// <summary>
    /// Returns false if this object already burst or the shared pool/sheets
    /// cannot spawn it. The object itself is not destroyed by this component.
    /// </summary>
    public bool TryBurstAt(Vector2 worldPosition, Vector2 impactDirection)
    {
        if (!Application.isPlaying || !isActiveAndEnabled ||
            (oncePerEnable && burstTriggered))
            return false;

        Vector3 origin = new(worldPosition.x, worldPosition.y, transform.position.z);
        BattleDebrisBurst burst = BattleDebrisBurst.Spawn(
            origin, impactDirection, material, MinimumPieces, MaximumPieces);
        if (burst == null)
            return false;

        burstTriggered = true;
        return true;
    }

    /// <summary>Call when reusing a breakable without disabling it.</summary>
    public void ResetForReuse()
    {
        burstTriggered = false;
        CacheCenterIfAvailable();
    }

    [ContextMenu("TEST - Burst (Play Mode)")]
    private void TestBurst()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[BurstObj] Test requires Play Mode.", this);
            return;
        }
        Burst();
    }

    private void OnValidate()
    {
        maximumPieces = Mathf.Clamp(maximumPieces, 1, 6);
        minimumPieces = Mathf.Clamp(minimumPieces, 1, maximumPieces);
    }
}

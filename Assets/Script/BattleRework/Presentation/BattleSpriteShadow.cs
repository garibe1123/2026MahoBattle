using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

[DefaultExecutionOrder(32700)]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class BattleSpriteShadow : MonoBehaviour
{
    [SerializeField] private SpriteRenderer source;
    [Tooltip("Opt this field sprite into shadow casting and receiving. Players and monsters are registered automatically.")]
    public bool explicitFieldObject;
    [Tooltip("Use the same owner for sprites belonging to one field object to prevent self-shadowing.")]
    public Transform shadowOwner;
    [Tooltip("Zero uses the visible sprite height in world units.")]
    [Min(0f)] public float objectHeight;
    public Transform groundAnchor;
    public Vector2 localGroundAnchor;
    public bool captureSpriteBottom = true;
    [Min(0f)] public float virtualHeight;
    [System.NonSerialized] public bool contactVisible = true;

    private static Material material;
    private SpriteRenderer shadowRenderer;
    private SpriteRenderer contactRenderer;
    private GameObject shadowObject;
    private MaterialPropertyBlock properties;
    private bool anchorCaptured;
    private float visualHeightOffset;
    private static readonly HashSet<BattleSpriteShadow> ActiveTargets = new();
    private Transform characterOwner;
    private SortingGroup sortingGroup;
    private SpriteRenderer receiverRenderer;
    private Material receiverMaterial;
    private MaterialPropertyBlock receiverProperties;
    private SortingGroup receiverGroup;
    private SpriteRenderer receiverSourceRenderer;
    private MaterialPropertyBlock receiverSourceProperties;
    private bool sourceSuppressed;
    private Bounds receiverProjectionBounds;

    internal SpriteRenderer Source => source;
    internal SpriteRenderer Projection => shadowRenderer;
    internal Bounds ReceiverProjectionBounds => receiverProjectionBounds;
    internal bool SourceVisible => isActiveAndEnabled && (explicitFieldObject || characterOwner != null) &&
        source != null && source.enabled && (!source.forceRenderingOff || sourceSuppressed) && source.gameObject.activeInHierarchy &&
        source.sprite != null && source.color.a > 0.001f;
    private Transform Owner => characterOwner != null ? characterOwner : shadowOwner != null ? shadowOwner :
        groundAnchor != null ? groundAnchor : transform;

    internal void SetCharacterOwner(Transform owner) => characterOwner = owner;

    private void OnEnable()
    {
        anchorCaptured = false;
        ActiveTargets.Add(this);
#if UNITY_EDITOR
        DebugTargets.Add(this);
#endif
    }

    internal static void CopyActive(List<BattleSpriteShadow> destination)
    {
        destination.Clear();
        foreach (BattleSpriteShadow target in ActiveTargets)
            if (target != null && target.isActiveAndEnabled)
                destination.Add(target);
    }

    internal bool CanCastOn(BattleSpriteShadow receiver, Camera camera)
    {
        if (this == receiver || Owner == receiver.Owner || shadowRenderer == null || !shadowRenderer.enabled ||
            !BattleShadowProjection.OverlapsXY(receiverProjectionBounds, receiver.source.bounds))
            return false;
        bool sameGroup = sortingGroup == receiver.sortingGroup;
        int casterLayer = !sameGroup && sortingGroup != null && sortingGroup.isActiveAndEnabled
            ? sortingGroup.sortingLayerID : source.sortingLayerID;
        int receiverLayer = !sameGroup && receiver.sortingGroup != null && receiver.sortingGroup.isActiveAndEnabled
            ? receiver.sortingGroup.sortingLayerID : receiver.source.sortingLayerID;
        int layerDifference = SortingLayer.GetLayerValueFromID(casterLayer).CompareTo(
            SortingLayer.GetLayerValueFromID(receiverLayer));
        if (layerDifference != 0)
            return layerDifference > 0;
        int casterOrder = !sameGroup && sortingGroup != null && sortingGroup.isActiveAndEnabled
            ? sortingGroup.sortingOrder : source.sortingOrder;
        int receiverOrder = !sameGroup && receiver.sortingGroup != null && receiver.sortingGroup.isActiveAndEnabled
            ? receiver.sortingGroup.sortingOrder : receiver.source.sortingOrder;
        if (casterOrder != receiverOrder)
            return casterOrder > receiverOrder;
        if (camera == null || (sameGroup && sortingGroup != null && sortingGroup.isActiveAndEnabled))
            return false;

        TransparencySortMode mode = camera.transparencySortMode;
        Vector3 axis = camera.transparencySortAxis;
        if (mode == TransparencySortMode.Default)
        {
            mode = GraphicsSettings.transparencySortMode;
            axis = GraphicsSettings.transparencySortAxis;
        }
        if (mode == TransparencySortMode.Default)
            mode = camera.orthographic ? TransparencySortMode.Orthographic : TransparencySortMode.Perspective;
        Vector3 cameraPosition = camera.transform.position;
        Vector3 cameraForward = camera.transform.forward;
        float casterDistance = BattleShadowProjection.SortDistance(SortPosition(!sameGroup), cameraPosition,
            cameraForward, mode, axis);
        float receiverDistance = BattleShadowProjection.SortDistance(receiver.SortPosition(!sameGroup), cameraPosition,
            cameraForward, mode, axis);
        return casterDistance < receiverDistance;
    }

    private Vector3 SortPosition(bool useGroup) => useGroup && sortingGroup != null && sortingGroup.isActiveAndEnabled
        ? sortingGroup.transform.position
        : source.spriteSortPoint == SpriteSortPoint.Pivot ? source.transform.position : source.bounds.center;

    internal void SetReceiverMask(bool visible, Vector4 worldRect = default, Vector4 atlasRect = default)
    {
        receiverRenderer.enabled = visible && SourceVisible;
        receiverSourceRenderer.enabled = receiverRenderer.enabled;
        if (!receiverRenderer.enabled)
        {
            if (sourceSuppressed && source != null)
                source.forceRenderingOff = false;
            sourceSuppressed = false;
            return;
        }
        source.GetPropertyBlock(receiverSourceProperties);
        receiverSourceRenderer.SetPropertyBlock(receiverSourceProperties);
        sourceSuppressed = true;
        source.forceRenderingOff = true;
        receiverProperties.SetVector("_ReceiverWorldRect", worldRect);
        receiverProperties.SetVector("_ReceiverAtlasRect", atlasRect);
        receiverProperties.SetColor("_ShadowColor", WorldLight.Instance.shadowColor);
        receiverRenderer.SetPropertyBlock(receiverProperties);
    }

#if UNITY_EDITOR
    public struct DebugGeometry
    {
        public Bounds spriteBounds;
        public Vector3 spriteBottom;
        public Vector3 spriteBottomLeft;
        public Vector3 spriteBottomRight;
        public Vector3 capturedAnchor;
        public Vector3 shadowStart;
        public Vector3 shadowEnd;
        public Vector3 receiverShadowStart;
        public Vector3 receiverShadowEnd;
        public bool grounded;
    }

    private static readonly HashSet<BattleSpriteShadow> DebugTargets = new();
    private DebugGeometry debugGeometry;
    private bool debugGeometryReady;
    private readonly List<BattleSpriteShadow> debugCasters = new();
    private int debugReceiverFrame = -1;
    private Camera debugReceiverCamera;
    private bool debugReceiverVisible;
    internal SpriteRenderer DebugReceiverRenderer => receiverRenderer;

    internal void BeginReceiverDebug(Camera camera)
    {
        if (WorldLight.Instance == null || camera != WorldLight.Instance.DebugCamera)
            return;
        debugCasters.Clear();
        debugReceiverFrame = Time.frameCount;
        debugReceiverCamera = camera;
        debugReceiverVisible = false;
    }

    internal void RecordReceiverDebug(Camera camera, BattleSpriteShadow caster = null)
    {
        if (camera != debugReceiverCamera || debugReceiverFrame != Time.frameCount)
            return;
        debugReceiverVisible = true;
        if (caster != null)
            debugCasters.Add(caster);
    }

    public bool CopyReceiverDebugCasters(Camera camera, List<BattleSpriteShadow> destination)
    {
        destination.Clear();
        if (!debugReceiverVisible || camera != debugReceiverCamera || debugReceiverFrame < Time.frameCount - 1)
            return false;
        for (int i = 0; i < debugCasters.Count; i++)
            if (debugCasters[i] != null)
                destination.Add(debugCasters[i]);
        return true;
    }

    public static void CopyDebugTargets(List<BattleSpriteShadow> destination)
    {
        destination.Clear();
        foreach (BattleSpriteShadow target in DebugTargets)
            if (target != null && target.SourceVisible &&
                (WorldLight.Instance == null || WorldLight.Instance.IsVisible(target.source.bounds)))
                destination.Add(target);
    }

    public bool TryGetDebugGeometry(out DebugGeometry geometry)
    {
        geometry = debugGeometry;
        return isActiveAndEnabled && debugGeometryReady;
    }
#endif

    public bool UseContactShadow => (explicitFieldObject || characterOwner != null) &&
        WorldLight.Mode == BattleShadowMode.Contact;

    public void BindContactShadow(SpriteRenderer contact) => contactRenderer = contact;

    [ContextMenu("Reset Ground Anchor Capture")]
    public void ResetGroundAnchorCapture()
    {
        anchorCaptured = false;
    }

    public void SetVirtualHeight(float height, float visualOffsetWorldY)
    {
        virtualHeight = Mathf.Max(0f, height);
        visualHeightOffset = visualOffsetWorldY;
    }

    private void Awake()
    {
        source = GetComponent<SpriteRenderer>();
        properties = new MaterialPropertyBlock();
        shadowObject = new GameObject("ProjectedSpriteShadow") { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(shadowObject, gameObject.scene);
        shadowRenderer = shadowObject.AddComponent<SpriteRenderer>();
        shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        shadowRenderer.receiveShadows = false;
        shadowRenderer.lightProbeUsage = LightProbeUsage.Off;
        shadowRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        shadowRenderer.sharedMaterial = SharedMaterial;
        shadowRenderer.enabled = false;
        sortingGroup = GetComponentInParent<SortingGroup>(true);
        GameObject receiverGroupObject = new("SpriteShadowComposite") { hideFlags = HideFlags.DontSave };
        receiverGroupObject.transform.SetParent(source.transform, false);
        receiverGroup = receiverGroupObject.AddComponent<SortingGroup>();
        GameObject receiverSourceObject = new("SpriteShadowSource") { hideFlags = HideFlags.DontSave };
        receiverSourceObject.transform.SetParent(receiverGroupObject.transform, false);
        receiverSourceRenderer = receiverSourceObject.AddComponent<SpriteRenderer>();
        receiverSourceRenderer.enabled = false;
        receiverSourceProperties = new MaterialPropertyBlock();
        GameObject receiverObject = new("SpriteShadowReceiver") { hideFlags = HideFlags.DontSave };
        receiverObject.transform.SetParent(receiverGroupObject.transform, false);
        receiverRenderer = receiverObject.AddComponent<SpriteRenderer>();
        receiverRenderer.shadowCastingMode = ShadowCastingMode.Off;
        receiverRenderer.receiveShadows = false;
        receiverRenderer.lightProbeUsage = LightProbeUsage.Off;
        receiverRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        if (SharedMaterial != null)
        {
            receiverMaterial = new Material(SharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
            receiverMaterial.SetFloat("_ShadowReceiver", 1f);
            receiverRenderer.sharedMaterial = receiverMaterial;
        }
        receiverProperties = new MaterialPropertyBlock();
        receiverRenderer.enabled = false;
    }

    public static Material SharedMaterial
    {
        get
        {
            if (material != null)
                return material;
            Material resource = Resources.Load<Material>("BattleSpriteShadow");
            Shader shader = resource != null ? resource.shader : Resources.Load<Shader>("BattleSpriteShadow");
            if (shader != null)
                material = resource != null ? new Material(resource) : new Material(shader);
            if (material != null)
                material.hideFlags = HideFlags.HideAndDontSave;
            return material;
        }
    }

    private void LateUpdate()
    {
        SetReceiverMask(false);
#if UNITY_EDITOR
        debugGeometryReady = false;
#endif
        if (contactRenderer != null)
            contactRenderer.enabled = contactVisible && UseContactShadow;
        WorldLight light = WorldLight.Instance;
        bool visible = SourceVisible &&
            light != null && WorldLight.Mode == BattleShadowMode.Projected && SharedMaterial != null;
        shadowRenderer.enabled = visible;
        if (!visible)
            return;

        Vector3 sortPosition = source.spriteSortPoint == SpriteSortPoint.Pivot ? source.transform.position : source.bounds.center;
        receiverGroup.transform.position = sortPosition;
        Vector3 visualOffset = -receiverGroup.transform.localPosition;
        receiverSourceRenderer.transform.localPosition = visualOffset;
        receiverRenderer.transform.localPosition = visualOffset;
        receiverGroup.sortingLayerID = source.sortingLayerID;
        receiverGroup.sortingOrder = source.sortingOrder;
        receiverSourceRenderer.sprite = source.sprite;
        receiverSourceRenderer.drawMode = source.drawMode;
        receiverSourceRenderer.size = source.size;
        receiverSourceRenderer.tileMode = source.tileMode;
        receiverSourceRenderer.adaptiveModeThreshold = source.adaptiveModeThreshold;
        receiverSourceRenderer.flipX = source.flipX;
        receiverSourceRenderer.flipY = source.flipY;
        receiverSourceRenderer.color = source.color;
        receiverSourceRenderer.maskInteraction = source.maskInteraction;
        receiverSourceRenderer.spriteSortPoint = source.spriteSortPoint;
        receiverSourceRenderer.sortingLayerID = source.sortingLayerID;
        receiverSourceRenderer.sortingOrder = 0;
        receiverSourceRenderer.gameObject.layer = source.gameObject.layer;
        receiverSourceRenderer.sharedMaterial = source.sharedMaterial;
        receiverSourceRenderer.renderingLayerMask = source.renderingLayerMask;
        receiverSourceRenderer.shadowCastingMode = source.shadowCastingMode;
        receiverSourceRenderer.receiveShadows = source.receiveShadows;
        receiverSourceRenderer.lightProbeUsage = source.lightProbeUsage;
        receiverSourceRenderer.reflectionProbeUsage = source.reflectionProbeUsage;
        receiverRenderer.sprite = source.sprite;
        receiverRenderer.drawMode = source.drawMode;
        receiverRenderer.size = source.size;
        receiverRenderer.tileMode = source.tileMode;
        receiverRenderer.adaptiveModeThreshold = source.adaptiveModeThreshold;
        receiverRenderer.flipX = source.flipX;
        receiverRenderer.flipY = source.flipY;
        receiverRenderer.color = source.color;
        receiverRenderer.maskInteraction = source.maskInteraction;
        receiverRenderer.spriteSortPoint = source.spriteSortPoint;
        receiverRenderer.sortingLayerID = source.sortingLayerID;
        receiverRenderer.sortingOrder = 1;
        receiverRenderer.gameObject.layer = source.gameObject.layer;
        if (receiverMaterial != null)
            receiverMaterial.renderQueue = source.sharedMaterial != null ? source.sharedMaterial.renderQueue : 3000;

        Bounds bounds = source.bounds;
        Bounds candidate = bounds;
        candidate.Encapsulate(groundAnchor != null ? groundAnchor.position : source.transform.TransformPoint(localGroundAnchor));
        float reach = Mathf.Max(0f, light.shadowLength) * (Mathf.Max(objectHeight, bounds.size.y) + Mathf.Max(0f, virtualHeight));
        candidate.Expand(2f * (reach + bounds.size.x * Mathf.Max(1f, light.shadowWidth)));
        if (!light.IsVisible(candidate))
        {
            shadowRenderer.enabled = false;
            return;
        }
        // Keep alpha-shader rendering active while readback is pending or unavailable.
        Bounds alphaBounds = bounds;
        Vector3 bottomLeft = new(bounds.min.x, bounds.min.y, bounds.center.z);
        Vector3 bottomRight = new(bounds.max.x, bounds.min.y, bounds.center.z);
        bool hasAlphaGeometry = BattleSpriteAlphaGeometry.TryGet(source.sprite, out Vector2[] alphaHull);
        if (hasAlphaGeometry)
            alphaBounds = BattleSpriteAlphaGeometry.WorldBounds(source, alphaHull, out bottomLeft, out bottomRight);
        Vector3 spriteBottom = (bottomLeft + bottomRight) * 0.5f;
        bool grounded = virtualHeight <= 0.0001f && Mathf.Abs(visualHeightOffset) <= 0.0001f;
        if (captureSpriteBottom && groundAnchor == null && !anchorCaptured)
        {
            localGroundAnchor = source.transform.InverseTransformPoint(spriteBottom);
            anchorCaptured = hasAlphaGeometry;
        }
        Vector3 anchor = groundAnchor != null ? groundAnchor.position : source.transform.TransformPoint(localGroundAnchor);
        // Ground placement and virtual height are independent of the sprite's alpha-bottom height origin.
        Vector3 sourceAnchor = captureSpriteBottom && groundAnchor == null
            ? spriteBottom : new Vector3(anchor.x, spriteBottom.y, anchor.z);
#if UNITY_EDITOR
        Vector3 capturedAnchor = anchor;
        if (grounded && light.previewConnectSpriteBottom)
        {
            anchor = spriteBottom + Vector3.up * light.previewBottomInset;
            sourceAnchor = anchor;
        }
#endif
        float height = objectHeight > 0f ? objectHeight : alphaBounds.size.y;
        Vector2 direction = light.ShadowDirection;
        Vector2 across = BattleShadowProjection.WidthAxis(direction, grounded);
        Vector4 shape = new(Mathf.Max(0f, light.shadowLength), Mathf.Max(0.01f, light.shadowWidth),
            height / Mathf.Max(0.001f, alphaBounds.size.y), Mathf.Max(0f, virtualHeight));
        Vector4 origin = new(anchor.x, anchor.y, sourceAnchor.y, anchor.z);
        Bounds projected = BattleShadowProjection.Bounds(bounds, origin, direction, across, shape, sourceAnchor.x);
        receiverProjectionBounds = projected;
        if (!light.IsVisible(projected))
        {
            shadowRenderer.enabled = false;
            return;
        }
#if UNITY_EDITOR
        debugGeometry = new DebugGeometry
        {
            spriteBounds = alphaBounds,
            spriteBottom = spriteBottom,
            spriteBottomLeft = bottomLeft,
            spriteBottomRight = bottomRight,
            capturedAnchor = capturedAnchor,
            shadowStart = BattleShadowProjection.Point(spriteBottom, origin, direction, across, shape, sourceAnchor.x),
            shadowEnd = BattleShadowProjection.Point(new Vector3(alphaBounds.center.x, alphaBounds.max.y, alphaBounds.center.z),
                origin, direction, across, shape, sourceAnchor.x),
            receiverShadowStart = BattleShadowProjection.Point(spriteBottom, origin, direction, across, shape, sourceAnchor.x),
            receiverShadowEnd = BattleShadowProjection.Point(new Vector3(alphaBounds.center.x, alphaBounds.max.y, alphaBounds.center.z),
                origin, direction, across, shape, sourceAnchor.x),
            grounded = grounded
        };
        debugGeometryReady = true;
#endif
        shadowRenderer.sprite = source.sprite;
        shadowRenderer.drawMode = source.drawMode;
        shadowRenderer.size = source.size;
        shadowRenderer.tileMode = source.tileMode;
        shadowRenderer.adaptiveModeThreshold = source.adaptiveModeThreshold;
        shadowRenderer.flipX = source.flipX;
        shadowRenderer.flipY = source.flipY;
        shadowRenderer.sharedMaterial = SharedMaterial;
        shadowRenderer.bounds = projected;
        shadowRenderer.sortingLayerID = light.ShadowSortingLayerId;
        shadowRenderer.sortingOrder = BattleWorldSorting.ShadowOrder;
        shadowObject.layer = source.gameObject.layer;
        properties.SetMatrix("_SourceToWorld", source.transform.localToWorldMatrix);
        properties.SetVector("_ShadowOrigin", origin);
        properties.SetFloat("_ShadowSourceX", sourceAnchor.x);
        properties.SetVector("_ShadowDirection", new Vector4(direction.x, direction.y, across.x, across.y));
        properties.SetVector("_ShadowShape", shape);
        Color color = light.shadowColor;
        color.a *= Mathf.Clamp01(light.shadowOpacity) * source.color.a;
        properties.SetColor("_ShadowColor", color);
        shadowRenderer.SetPropertyBlock(properties);
    }

    private void OnDisable()
    {
        ActiveTargets.Remove(this);
        if (receiverRenderer != null)
            SetReceiverMask(false);
#if UNITY_EDITOR
        DebugTargets.Remove(this);
        debugGeometryReady = false;
        debugCasters.Clear();
        debugReceiverVisible = false;
#endif
        if (shadowRenderer != null)
            shadowRenderer.enabled = false;
    }

    private void OnTransformParentChanged() => sortingGroup = GetComponentInParent<SortingGroup>(true);

    private void OnDestroy()
    {
        ActiveTargets.Remove(this);
        if (sourceSuppressed && source != null)
            source.forceRenderingOff = false;
        if (shadowObject != null)
            Destroy(shadowObject);
        if (receiverGroup != null)
            Destroy(receiverGroup.gameObject);
        if (receiverMaterial != null)
            Destroy(receiverMaterial);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearCache()
    {
        ActiveTargets.Clear();
#if UNITY_EDITOR
        DebugTargets.Clear();
#endif
        if (material != null)
            Destroy(material);
        material = null;
    }
}

internal static class BattleShadowProjection
{
    internal static float SortDistance(Vector3 point, Vector3 cameraPosition, Vector3 cameraForward,
        TransparencySortMode mode, Vector3 axis) => mode == TransparencySortMode.Perspective
        ? (point - cameraPosition).sqrMagnitude
        : Vector3.Dot(point - cameraPosition, mode == TransparencySortMode.CustomAxis ? axis : cameraForward);

#if UNITY_EDITOR
    internal static bool ClipSegmentXY(Vector3 start, Vector3 end, Bounds bounds, out Vector3 from, out Vector3 to)
    {
        Vector3 delta = end - start;
        float enter = 0f;
        float exit = 1f;
        bool hit = ClipAxis(start.x, delta.x, bounds.min.x, bounds.max.x, ref enter, ref exit) &&
            ClipAxis(start.y, delta.y, bounds.min.y, bounds.max.y, ref enter, ref exit);
        from = start + delta * enter;
        to = start + delta * exit;
        return hit;
    }

    private static bool ClipAxis(float start, float delta, float min, float max, ref float enter, ref float exit)
    {
        if (Mathf.Abs(delta) < 0.000001f)
            return start >= min && start <= max;
        float a = (min - start) / delta;
        float b = (max - start) / delta;
        enter = Mathf.Max(enter, Mathf.Min(a, b));
        exit = Mathf.Min(exit, Mathf.Max(a, b));
        return enter <= exit;
    }
#endif
    internal static bool OverlapsXY(Bounds a, Bounds b) => a.min.x <= b.max.x && a.max.x >= b.min.x &&
        a.min.y <= b.max.y && a.max.y >= b.min.y;

    internal static Vector2 WidthAxis(Vector2 direction, bool grounded)
    {
        Vector2 perpendicular = new(-direction.y, direction.x);
        if (!grounded)
            return perpendicular;

        // Keep the base nearly horizontal, with a thin transverse component at lateral angles.
        float sideBlend = Mathf.Clamp01(1f - Mathf.Abs(direction.y) / 0.1f);
        return Vector2.right - perpendicular * (Mathf.Sign(direction.y) * 0.02f * sideBlend);
    }

    internal static Vector3 Point(Vector3 point, Vector4 origin, Vector2 direction, Vector2 across, Vector4 shape,
        float sourceAnchorX)
    {
        float height = (point.y - origin.z) * shape.z + shape.w;
        // A height-independent width axis keeps the projection affine.
        Vector2 widthAxis = across * shape.y;
        Vector2 result = new Vector2(origin.x, origin.y) + widthAxis * (point.x - sourceAnchorX) +
            direction * (height * shape.x);
        return new Vector3(result.x, result.y, origin.w);
    }

    internal static Bounds Bounds(Bounds source, Vector4 origin, Vector2 direction, Vector2 across, Vector4 shape,
        float sourceAnchorX)
    {
        Bounds result = new(Point(source.min, origin, direction, across, shape, sourceAnchorX), Vector3.zero);
        result.Encapsulate(Point(source.max, origin, direction, across, shape, sourceAnchorX));
        result.Encapsulate(Point(new Vector3(source.min.x, source.max.y, 0f), origin, direction, across, shape, sourceAnchorX));
        result.Encapsulate(Point(new Vector3(source.max.x, source.min.y, 0f), origin, direction, across, shape, sourceAnchorX));
        result.Expand(0.02f);
        return result;
    }
}

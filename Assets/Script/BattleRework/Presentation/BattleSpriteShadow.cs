using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(32700)]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class BattleSpriteShadow : MonoBehaviour
{
    [SerializeField] private SpriteRenderer source;
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

    public bool UseContactShadow => WorldLight.Mode == BattleShadowMode.Contact;

    public void BindContactShadow(SpriteRenderer contact) => contactRenderer = contact;

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
        if (contactRenderer != null)
            contactRenderer.enabled = contactVisible && UseContactShadow;
        WorldLight light = WorldLight.Instance;
        bool visible = source != null && source.enabled && !source.forceRenderingOff &&
            source.gameObject.activeInHierarchy && source.sprite != null && source.color.a > 0.001f &&
            light != null && WorldLight.Mode == BattleShadowMode.Projected && SharedMaterial != null;
        shadowRenderer.enabled = visible;
        if (!visible)
            return;

        Bounds bounds = source.bounds;
        if (!anchorCaptured && source.sprite != null)
        {
            if (captureSpriteBottom)
                localGroundAnchor = source.transform.InverseTransformPoint(
                    new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            anchorCaptured = true;
        }
        Vector3 anchor = groundAnchor != null ? groundAnchor.position : source.transform.TransformPoint(localGroundAnchor);
        float height = objectHeight > 0f ? objectHeight : bounds.size.y;
        Vector2 direction = light.ShadowDirection;
        Vector2 across = new(-direction.y, direction.x);
        Vector4 shape = new(Mathf.Max(0f, light.shadowLength), Mathf.Max(0.01f, light.shadowWidth),
            height / Mathf.Max(0.001f, bounds.size.y), Mathf.Max(0f, virtualHeight));
        Vector4 origin = new(anchor.x, anchor.y, anchor.y + visualHeightOffset, anchor.z);
        Bounds projected = BattleShadowProjection.Bounds(bounds, origin, direction, across, shape);
        if (!light.IsVisible(projected))
        {
            shadowRenderer.enabled = false;
            return;
        }
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
        properties.SetVector("_ShadowDirection", new Vector4(direction.x, direction.y, across.x, across.y));
        properties.SetVector("_ShadowShape", shape);
        Color color = light.shadowColor;
        color.a *= Mathf.Clamp01(light.shadowOpacity) * source.color.a;
        properties.SetColor("_ShadowColor", color);
        shadowRenderer.SetPropertyBlock(properties);
    }

    private void OnDisable()
    {
        if (shadowRenderer != null)
            shadowRenderer.enabled = false;
    }

    private void OnDestroy()
    {
        if (shadowObject != null)
            Destroy(shadowObject);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearCache()
    {
        if (material != null)
            Destroy(material);
        material = null;
    }
}

internal static class BattleShadowProjection
{
    internal static Vector3 Point(Vector3 point, Vector4 origin, Vector2 direction, Vector2 across, Vector4 shape)
    {
        float height = (point.y - origin.z) * shape.z + shape.w;
        Vector2 result = new Vector2(origin.x, origin.y) + across * ((point.x - origin.x) * shape.y) +
            direction * (height * shape.x);
        return new Vector3(result.x, result.y, origin.w);
    }

    internal static Bounds Bounds(Bounds source, Vector4 origin, Vector2 direction, Vector2 across, Vector4 shape)
    {
        Bounds result = new(Point(source.min, origin, direction, across, shape), Vector3.zero);
        result.Encapsulate(Point(source.max, origin, direction, across, shape));
        result.Encapsulate(Point(new Vector3(source.min.x, source.max.y, 0f), origin, direction, across, shape));
        result.Encapsulate(Point(new Vector3(source.max.x, source.min.y, 0f), origin, direction, across, shape));
        result.Expand(0.02f);
        return result;
    }
}

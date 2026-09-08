using UnityEngine;

/// <summary>
/// Character presentation light that does not behave like a circular point lamp.
/// - A compressed soft ellipse is placed under the lower edge of the sprite.
/// - A very weak additive top-light overlay makes the sprite read as lit from above.
/// - No local Point Light2D is created, so multiple characters do not fill the field with circular light blobs.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleCharacterLightVisual : MonoBehaviour
{
    public const string PoolRendererName = "CharacterLightPool";
    public const string GlowRendererName = "CharacterSpriteTopLight";

    private const string TopLightShaderName = "Sprites/BattleCharacterTopLight";

    private static Sprite sharedPoolSprite;
    private static Material sharedTopLightMaterial;

    private SpriteRenderer targetRenderer;
    private SpriteRenderer poolRenderer;
    private SpriteRenderer glowRenderer;
    private Transform poolTransform;
    private Transform glowTransform;
    private MaterialPropertyBlock glowProperties;

    private Color poolColor = Color.white;
    private Color topLightColor = Color.white;
    private float poolMaxAlpha = 0.2f;
    private float poolWidthMultiplier = 1.4f;
    private float poolHeightRatio = 0.18f;
    private float topLightStrength = 0.04f;
    private float fadeSharpness = 7f;

    private float targetVisibility;
    private float targetStrength = 1f;
    private float currentVisibility;

    public void Configure(
        SpriteRenderer spriteRenderer,
        Color lightPoolColor,
        Color spriteTopLightColor,
        float maxPoolAlpha,
        float widthMultiplier,
        float heightRatio,
        float spriteTopLightStrength,
        float lightFadeSharpness)
    {
        if (spriteRenderer != null)
            targetRenderer = spriteRenderer;

        poolColor = lightPoolColor;
        topLightColor = spriteTopLightColor;
        poolMaxAlpha = Mathf.Clamp01(maxPoolAlpha);
        poolWidthMultiplier = Mathf.Max(0.1f, widthMultiplier);
        poolHeightRatio = Mathf.Clamp(heightRatio, 0.05f, 0.55f);
        topLightStrength = Mathf.Clamp(spriteTopLightStrength, 0f, 0.35f);
        fadeSharpness = Mathf.Max(0.1f, lightFadeSharpness);

        EnsureRig();
        ApplyVisualState();
    }

    public void SetTarget(bool active, float strength = 1f)
    {
        targetVisibility = active ? 1f : 0f;
        targetStrength = Mathf.Clamp01(strength);
    }

    public void SetImmediate(float visibility)
    {
        currentVisibility = Mathf.Clamp01(visibility);
        targetVisibility = currentVisibility;
        ApplyVisualState();
    }

    private void Awake()
    {
        RemoveLegacyBodyLight();

        if (targetRenderer == null)
            targetRenderer = ResolveRenderer();

        EnsureRig();
        ApplyVisualState();
    }

    private void OnEnable()
    {
        EnsureRig();
        ApplyVisualState();
    }

    private void Update()
    {
        float target = targetVisibility * targetStrength;
        float t = 1f - Mathf.Exp(-fadeSharpness * Time.unscaledDeltaTime);
        currentVisibility = Mathf.Lerp(currentVisibility, target, t);

        if (Mathf.Abs(currentVisibility - target) < 0.001f)
            currentVisibility = target;

        ApplyVisualState();
    }

    private void LateUpdate()
    {
        if (targetRenderer == null)
            targetRenderer = ResolveRenderer();
        if (targetRenderer == null)
            return;

        EnsureRig();
        UpdatePlacement();
        SyncGlowRenderer();
        ApplyVisualState();
    }

    private void RemoveLegacyBodyLight()
    {
        Transform legacy = transform.Find("CharacterBodyGlowLight");
        if (legacy != null)
        {
            legacy.gameObject.SetActive(false);
            Destroy(legacy.gameObject);
        }
    }

    private SpriteRenderer ResolveRenderer()
    {
        SpriteRenderer direct = GetComponent<SpriteRenderer>();
        if (IsUsableTargetRenderer(direct))
            return direct;

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer best = null;
        float bestArea = -1f;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (!IsUsableTargetRenderer(renderer))
                continue;

            float area = Mathf.Abs(renderer.bounds.size.x * renderer.bounds.size.y);
            if (area <= bestArea)
                continue;

            bestArea = area;
            best = renderer;
        }

        return best;
    }

    private static bool IsUsableTargetRenderer(SpriteRenderer renderer)
    {
        return renderer != null &&
               renderer.name != PoolRendererName &&
               renderer.name != GlowRendererName;
    }

    private void EnsureRig()
    {
        if (poolRenderer == null)
        {
            Transform existing = transform.Find(PoolRendererName);
            GameObject poolObject = existing != null
                ? existing.gameObject
                : new GameObject(PoolRendererName);

            poolObject.transform.SetParent(transform, true);
            poolTransform = poolObject.transform;

            poolRenderer = poolObject.GetComponent<SpriteRenderer>();
            if (poolRenderer == null)
                poolRenderer = poolObject.AddComponent<SpriteRenderer>();

            poolRenderer.sprite = GetOrCreatePoolSprite();
        }

        if (targetRenderer != null &&
            (glowRenderer == null || glowTransform == null || glowTransform.parent != targetRenderer.transform))
        {
            if (glowRenderer != null)
                Destroy(glowRenderer.gameObject);

            GameObject glowObject = new(GlowRendererName);
            glowObject.transform.SetParent(targetRenderer.transform, false);

            glowTransform = glowObject.transform;
            glowTransform.localPosition = Vector3.zero;
            glowTransform.localRotation = Quaternion.identity;
            glowTransform.localScale = Vector3.one;

            glowRenderer = glowObject.AddComponent<SpriteRenderer>();
            glowRenderer.sharedMaterial = GetOrCreateTopLightMaterial();
        }

        if (poolTransform == null && poolRenderer != null)
            poolTransform = poolRenderer.transform;

        glowProperties ??= new MaterialPropertyBlock();
    }

    private void UpdatePlacement()
    {
        Bounds bounds = targetRenderer.bounds;
        float spriteWidth = Mathf.Max(0.2f, bounds.size.x);
        float spriteHeight = Mathf.Max(0.2f, bounds.size.y);

        float poolWidth = Mathf.Max(0.72f, spriteWidth * poolWidthMultiplier);
        float poolHeight = Mathf.Max(0.11f, poolWidth * poolHeightRatio);
        float poolY = bounds.min.y + Mathf.Max(0.02f, spriteHeight * 0.035f);

        if (poolTransform != null && poolRenderer != null && poolRenderer.sprite != null)
        {
            poolTransform.position = new Vector3(
                bounds.center.x,
                poolY,
                targetRenderer.transform.position.z);
            poolTransform.rotation = Quaternion.identity;

            Vector3 parentScale = transform.lossyScale;
            float inverseX = Mathf.Abs(parentScale.x) > 0.0001f ? 1f / Mathf.Abs(parentScale.x) : 1f;
            float inverseY = Mathf.Abs(parentScale.y) > 0.0001f ? 1f / Mathf.Abs(parentScale.y) : 1f;

            Vector2 spriteSize = poolRenderer.sprite.bounds.size;
            float sourceWidth = Mathf.Max(0.0001f, spriteSize.x);
            float sourceHeight = Mathf.Max(0.0001f, spriteSize.y);

            poolTransform.localScale = new Vector3(
                poolWidth / sourceWidth * inverseX,
                poolHeight / sourceHeight * inverseY,
                1f);
        }

        if (poolRenderer != null)
        {
            poolRenderer.sortingLayerID = targetRenderer.sortingLayerID;
            poolRenderer.sortingOrder = targetRenderer.sortingOrder - 1;
        }
    }

    private void SyncGlowRenderer()
    {
        if (targetRenderer == null || glowRenderer == null)
            return;

        glowRenderer.sprite = targetRenderer.sprite;
        glowRenderer.flipX = targetRenderer.flipX;
        glowRenderer.flipY = targetRenderer.flipY;
        glowRenderer.drawMode = targetRenderer.drawMode;
        glowRenderer.size = targetRenderer.size;
        glowRenderer.maskInteraction = targetRenderer.maskInteraction;
        glowRenderer.sortingLayerID = targetRenderer.sortingLayerID;
        glowRenderer.sortingOrder = targetRenderer.sortingOrder + 1;
        glowRenderer.color = new Color(1f, 1f, 1f, targetRenderer.color.a);

        if (glowRenderer.sharedMaterial == null)
            glowRenderer.sharedMaterial = GetOrCreateTopLightMaterial();
    }

    private void ApplyVisualState()
    {
        float visibility = Mathf.Clamp01(currentVisibility);

        if (poolRenderer != null)
        {
            Color color = poolColor;
            color.a = poolMaxAlpha * visibility;
            poolRenderer.color = color;
            poolRenderer.enabled = color.a > 0.001f;
        }

        if (glowRenderer != null)
        {
            Material material = GetOrCreateTopLightMaterial();
            glowRenderer.sharedMaterial = material;

            bool canGlow = material != null &&
                           targetRenderer != null &&
                           targetRenderer.enabled &&
                           targetRenderer.gameObject.activeInHierarchy;

            glowRenderer.enabled = canGlow && topLightStrength * visibility > 0.001f;
            if (glowRenderer.enabled)
            {
                glowRenderer.GetPropertyBlock(glowProperties);
                glowProperties.SetColor("_GlowColor", topLightColor);
                glowProperties.SetFloat("_Strength", topLightStrength * visibility);
                glowRenderer.SetPropertyBlock(glowProperties);
            }
        }
    }

    private static Material GetOrCreateTopLightMaterial()
    {
        if (sharedTopLightMaterial != null)
            return sharedTopLightMaterial;

        Shader shader = Shader.Find(TopLightShaderName);
        if (shader == null)
            shader = Resources.Load<Shader>("BattleCharacterTopLight");
        if (shader == null)
            return null;

        sharedTopLightMaterial = new Material(shader)
        {
            name = "BattleCharacterTopLight_Runtime",
            hideFlags = HideFlags.HideAndDontSave
        };
        return sharedTopLightMaterial;
    }

    private static Sprite GetOrCreatePoolSprite()
    {
        if (sharedPoolSprite != null)
            return sharedPoolSprite;

        const int width = 96;
        const int height = 32;
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "RuntimeCharacterLightPool",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float ny = ((y + 0.5f) / height) * 2f - 1f;

            for (int x = 0; x < width; x++)
            {
                float nx = ((x + 0.5f) / width) * 2f - 1f;
                float distance = Mathf.Sqrt(nx * nx + ny * ny);
                float edge = Mathf.Clamp01(1f - distance);
                float alpha = Mathf.SmoothStep(0f, 1f, edge);
                alpha = alpha * alpha * (0.82f + 0.18f * edge);

                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        sharedPoolSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            32f,
            0,
            SpriteMeshType.FullRect);
        sharedPoolSprite.name = "RuntimeCharacterLightPoolSprite";
        sharedPoolSprite.hideFlags = HideFlags.HideAndDontSave;
        return sharedPoolSprite;
    }
}

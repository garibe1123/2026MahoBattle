using UnityEngine;

/// <summary>
/// Character presentation spotlight.
///
/// Beam / floor pool / sprite top-light are one visual rig and are always driven by the
/// same normalized spotlight strength. The strength changes both brightness and aperture,
/// so the light opens/closes like a stage spotlight instead of independently fading layers.
/// Enemies can still disable the key beam and use only their configured contact light.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleCharacterLightVisual : MonoBehaviour
{
    public const string KeyRendererName = "CharacterKeySpotlight";
    public const string PoolRendererName = "CharacterLightPool";
    public const string GlowRendererName = "CharacterSpriteTopLight";

    private const string TopLightShaderName = "Sprites/BattleCharacterTopLight";
    private const string KeyLightShaderName = "Sprites/BattleSoftKeyLight";

    // A stage spotlight should visually open/close, not only change alpha.
    // Beam and floor pool share this same aperture value.
    private const float ClosedApertureScale = 0.26f;
    private const float ClosedBeamHeightScale = 0.82f;
    private const float ClosedPoolHeightScale = 0.42f;

    private static Sprite sharedPoolSprite;
    private static Sprite sharedKeyLightSprite;
    private static Material sharedTopLightMaterial;
    private static Material sharedKeyLightMaterial;

    private SpriteRenderer targetRenderer;
    private SpriteRenderer keyRenderer;
    private SpriteRenderer poolRenderer;
    private SpriteRenderer glowRenderer;
    private Transform keyTransform;
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

    private bool keyLightEnabled;
    private Color keyLightColor = Color.white;
    private float keyLightMaxAlpha = 0.2f;
    private float keyLightWidthMultiplier = 1.6f;
    private float keyLightHeightMultiplier = 1.6f;
    private float keyLightVerticalOffsetRatio = 0.15f;

    private float targetVisibility;
    private float targetStrength = 1f;
    private float currentVisibility;

    public float CurrentSpotlightStrength => Mathf.Clamp01(currentVisibility);

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

    public void ConfigureKeyLight(
        bool enabled,
        Color lightColor,
        float maxAlpha,
        float widthMultiplier,
        float heightMultiplier,
        float verticalOffsetRatio)
    {
        keyLightEnabled = enabled;
        keyLightColor = lightColor;
        keyLightMaxAlpha = Mathf.Clamp01(maxAlpha);
        keyLightWidthMultiplier = Mathf.Max(0.2f, widthMultiplier);
        keyLightHeightMultiplier = Mathf.Max(0.2f, heightMultiplier);
        keyLightVerticalOffsetRatio = Mathf.Clamp(verticalOffsetRatio, -1f, 1f);

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
        targetStrength = 1f;
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
               renderer.name != KeyRendererName &&
               renderer.name != PoolRendererName &&
               renderer.name != GlowRendererName;
    }

    private void EnsureRig()
    {
        if (keyRenderer == null)
        {
            Transform existing = transform.Find(KeyRendererName);
            GameObject keyObject = existing != null
                ? existing.gameObject
                : new GameObject(KeyRendererName);

            keyObject.transform.SetParent(transform, true);
            keyTransform = keyObject.transform;

            keyRenderer = keyObject.GetComponent<SpriteRenderer>();
            if (keyRenderer == null)
                keyRenderer = keyObject.AddComponent<SpriteRenderer>();

            keyRenderer.sprite = GetOrCreateKeyLightSprite();
            keyRenderer.sharedMaterial = GetOrCreateKeyLightMaterial();
        }

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

        // Beam and pool intentionally use the same additive light material so they read as one lamp.
        Material spotlightMaterial = GetOrCreateKeyLightMaterial();
        if (keyRenderer != null)
        {
            keyRenderer.sharedMaterial = spotlightMaterial;
            // Runtime verification shows the SpriteRenderer + spotlight shader path presents the
            // authored cone vertically inverted. Flip only the beam so the screen result is
            // definitively narrow at the source (top) and broad at the floor (bottom).
            keyRenderer.flipY = true;
        }
        if (poolRenderer != null)
            poolRenderer.sharedMaterial = spotlightMaterial;

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

        if (keyTransform == null && keyRenderer != null)
            keyTransform = keyRenderer.transform;
        if (poolTransform == null && poolRenderer != null)
            poolTransform = poolRenderer.transform;

        glowProperties ??= new MaterialPropertyBlock();
    }

    private void UpdatePlacement()
    {
        Bounds bounds = targetRenderer.bounds;
        float spriteWidth = Mathf.Max(0.2f, bounds.size.x);
        float spriteHeight = Mathf.Max(0.2f, bounds.size.y);

        // One aperture drives both the cone and its footprint.
        float aperture = ResolveSpotlightAperture();
        float widthScale = Mathf.Lerp(ClosedApertureScale, 1f, aperture);
        float beamHeightScale = Mathf.Lerp(ClosedBeamHeightScale, 1f, aperture);
        float poolHeightScale = Mathf.Lerp(ClosedPoolHeightScale, 1f, aperture);

        if (keyTransform != null && keyRenderer != null && keyRenderer.sprite != null)
        {
            float baseKeyWidth = Mathf.Max(0.9f, spriteWidth * keyLightWidthMultiplier);
            float baseKeyHeight = Mathf.Max(1.1f, spriteHeight * keyLightHeightMultiplier);
            float keyWidth = baseKeyWidth * widthScale;
            float keyHeight = baseKeyHeight * beamHeightScale;
            float keyY = bounds.center.y + spriteHeight * keyLightVerticalOffsetRatio;

            keyTransform.position = new Vector3(
                bounds.center.x,
                keyY,
                targetRenderer.transform.position.z);
            keyTransform.rotation = Quaternion.identity;

            ApplyWorldSizeToChild(
                keyTransform,
                keyRenderer.sprite,
                keyWidth,
                keyHeight);

            keyRenderer.sortingLayerID = targetRenderer.sortingLayerID;
            keyRenderer.sortingOrder = targetRenderer.sortingOrder - 2;
        }

        float basePoolWidth = Mathf.Max(0.72f, spriteWidth * poolWidthMultiplier);
        float basePoolHeight = Mathf.Max(0.11f, basePoolWidth * poolHeightRatio);
        float poolWidth = basePoolWidth * widthScale;
        float poolHeight = basePoolHeight * poolHeightScale;
        float poolY = bounds.min.y + Mathf.Max(0.02f, spriteHeight * 0.035f);

        if (poolTransform != null && poolRenderer != null && poolRenderer.sprite != null)
        {
            poolTransform.position = new Vector3(
                bounds.center.x,
                poolY,
                targetRenderer.transform.position.z);
            poolTransform.rotation = Quaternion.identity;

            ApplyWorldSizeToChild(
                poolTransform,
                poolRenderer.sprite,
                poolWidth,
                poolHeight);

            poolRenderer.sortingLayerID = targetRenderer.sortingLayerID;
            poolRenderer.sortingOrder = targetRenderer.sortingOrder - 1;
        }
    }

    private float ResolveSpotlightAperture()
    {
        float value = Mathf.Clamp01(currentVisibility);
        return value * value * (3f - 2f * value);
    }

    private void ApplyWorldSizeToChild(
        Transform child,
        Sprite sprite,
        float width,
        float height)
    {
        if (child == null || sprite == null)
            return;

        Vector3 parentScale = transform.lossyScale;
        float inverseX = Mathf.Abs(parentScale.x) > 0.0001f
            ? 1f / Mathf.Abs(parentScale.x)
            : 1f;
        float inverseY = Mathf.Abs(parentScale.y) > 0.0001f
            ? 1f / Mathf.Abs(parentScale.y)
            : 1f;

        Vector2 spriteSize = sprite.bounds.size;
        float sourceWidth = Mathf.Max(0.0001f, spriteSize.x);
        float sourceHeight = Mathf.Max(0.0001f, spriteSize.y);

        child.localScale = new Vector3(
            width / sourceWidth * inverseX,
            height / sourceHeight * inverseY,
            1f);
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
        float visibility = ResolveSpotlightAperture();
        Material spotlightMaterial = GetOrCreateKeyLightMaterial();

        if (keyRenderer != null)
        {
            Color color = keyLightColor;
            color.a = keyLightEnabled ? keyLightMaxAlpha * visibility : 0f;
            keyRenderer.color = color;
            keyRenderer.sharedMaterial = spotlightMaterial;
            keyRenderer.enabled =
                keyLightEnabled &&
                spotlightMaterial != null &&
                color.a > 0.001f;
        }

        if (poolRenderer != null)
        {
            Color color = poolColor;
            color.a = poolMaxAlpha * visibility;
            poolRenderer.color = color;
            poolRenderer.sharedMaterial = spotlightMaterial;
            poolRenderer.enabled = spotlightMaterial != null && color.a > 0.001f;
        }

        if (glowRenderer != null)
        {
            Material material = GetOrCreateTopLightMaterial();
            glowRenderer.sharedMaterial = material;

            bool canGlow =
                material != null &&
                targetRenderer != null &&
                targetRenderer.enabled &&
                targetRenderer.gameObject.activeInHierarchy;

            glowRenderer.enabled =
                canGlow &&
                topLightStrength * visibility > 0.001f;

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

    private static Material GetOrCreateKeyLightMaterial()
    {
        if (sharedKeyLightMaterial != null)
            return sharedKeyLightMaterial;

        Shader shader = Shader.Find(KeyLightShaderName);
        if (shader == null)
            shader = Resources.Load<Shader>("BattleSoftKeyLight");
        if (shader == null)
            return null;

        sharedKeyLightMaterial = new Material(shader)
        {
            name = "BattleSoftKeyLight_Runtime",
            hideFlags = HideFlags.HideAndDontSave
        };
        return sharedKeyLightMaterial;
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

    private static Sprite GetOrCreateKeyLightSprite()
    {
        if (sharedKeyLightSprite != null)
            return sharedKeyLightSprite;

        const int width = 64;
        const int height = 128;
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "RuntimeCharacterKeySpotlight",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            float verticalFadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.20f, v));
            float verticalFadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 1f, v));
            float vertical = verticalFadeIn * verticalFadeOut;

            // Texture Y=0 is authored as the broad floor side and Y=1 as the narrow source side.
            // The renderer is flipped in EnsureRig because the runtime shader path presents this texture upside-down.
            float widthAtHeight = Mathf.Lerp(1.00f, 0.32f, v);

            for (int x = 0; x < width; x++)
            {
                float nx = Mathf.Abs(((x + 0.5f) / width) * 2f - 1f);
                float normalizedX = nx / Mathf.Max(0.001f, widthAtHeight);
                float horizontal = 1f - Mathf.SmoothStep(0.40f, 1f, normalizedX);
                float centerLift = Mathf.Lerp(0.82f, 1f, 1f - Mathf.Clamp01(normalizedX));
                float alpha = Mathf.Clamp01(horizontal * vertical * centerLift);

                pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        sharedKeyLightSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            32f,
            0,
            SpriteMeshType.FullRect);
        sharedKeyLightSprite.name = "RuntimeCharacterKeySpotlightSprite";
        sharedKeyLightSprite.hideFlags = HideFlags.HideAndDontSave;
        return sharedKeyLightSprite;
    }
}

using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Runtime character light visual.
/// - Soft ellipse pool is anchored near the lower part of the current SpriteRenderer bounds.
/// - Local Light2D gives the sprite a weak stage-lit response.
/// - A very low-alpha sprite overlay keeps the character readable even with an unlit sprite material.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleCharacterLightVisual : MonoBehaviour
{
    public const string PoolRendererName = "CharacterLightPool";
    public const string GlowRendererName = "CharacterSpriteGlow";

    private static Sprite sharedPoolSprite;

    private SpriteRenderer targetRenderer;
    private SpriteRenderer poolRenderer;
    private SpriteRenderer glowRenderer;
    private Light2D bodyLight;
    private Transform poolTransform;
    private Transform glowTransform;
    private Transform bodyLightTransform;

    private Color poolColor = Color.white;
    private Color bodyLightColor = Color.white;
    private float poolMaxAlpha = 0.3f;
    private float poolWidthMultiplier = 1.35f;
    private float poolHeightRatio = 0.25f;
    private float bodyLightIntensity = 0.15f;
    private float bodyLightRadiusMultiplier = 0.8f;
    private float spriteGlowAlpha = 0.08f;
    private float fadeSharpness = 7f;

    private float targetVisibility;
    private float targetStrength = 1f;
    private float currentVisibility;

    public void Configure(
        SpriteRenderer spriteRenderer,
        Color lightPoolColor,
        Color localBodyLightColor,
        float maxPoolAlpha,
        float widthMultiplier,
        float heightRatio,
        float localBodyIntensity,
        float localBodyRadiusMultiplier,
        float glowAlpha,
        float lightFadeSharpness)
    {
        if (spriteRenderer != null)
            targetRenderer = spriteRenderer;

        poolColor = lightPoolColor;
        bodyLightColor = localBodyLightColor;
        poolMaxAlpha = Mathf.Clamp01(maxPoolAlpha);
        poolWidthMultiplier = Mathf.Max(0.1f, widthMultiplier);
        poolHeightRatio = Mathf.Clamp(heightRatio, 0.08f, 0.8f);
        bodyLightIntensity = Mathf.Max(0f, localBodyIntensity);
        bodyLightRadiusMultiplier = Mathf.Max(0.1f, localBodyRadiusMultiplier);
        spriteGlowAlpha = Mathf.Clamp(glowAlpha, 0f, 0.5f);
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

    private SpriteRenderer ResolveRenderer()
    {
        SpriteRenderer direct = GetComponent<SpriteRenderer>();
        if (direct != null && direct.name != PoolRendererName && direct.name != GlowRendererName)
            return direct;

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer best = null;
        float bestArea = -1f;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null ||
                renderer.name == PoolRendererName ||
                renderer.name == GlowRendererName)
            {
                continue;
            }

            float area = Mathf.Abs(renderer.bounds.size.x * renderer.bounds.size.y);
            if (area <= bestArea)
                continue;

            bestArea = area;
            best = renderer;
        }

        return best;
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
        }

        if (bodyLight == null)
        {
            Transform existing = transform.Find("CharacterBodyGlowLight");
            GameObject lightObject = existing != null
                ? existing.gameObject
                : new GameObject("CharacterBodyGlowLight");

            lightObject.transform.SetParent(transform, true);
            bodyLightTransform = lightObject.transform;
            bodyLight = lightObject.GetComponent<Light2D>();
            if (bodyLight == null)
                bodyLight = lightObject.AddComponent<Light2D>();

            bodyLight.lightType = Light2D.LightType.Point;
            bodyLight.blendStyleIndex = 0;
            bodyLight.pointLightInnerAngle = 360f;
            bodyLight.pointLightOuterAngle = 360f;
            bodyLight.falloffIntensity = 0.72f;
            bodyLight.overlapOperation = Light2D.OverlapOperation.Additive;
            bodyLight.shadowsEnabled = false;
            bodyLight.volumetricEnabled = false;
        }

        if (poolTransform == null && poolRenderer != null)
            poolTransform = poolRenderer.transform;
        if (bodyLightTransform == null && bodyLight != null)
            bodyLightTransform = bodyLight.transform;
    }

    private void UpdatePlacement()
    {
        Bounds bounds = targetRenderer.bounds;
        float spriteWidth = Mathf.Max(0.2f, bounds.size.x);
        float spriteHeight = Mathf.Max(0.2f, bounds.size.y);

        float poolWidth = Mathf.Max(0.8f, spriteWidth * poolWidthMultiplier);
        float poolHeight = Mathf.Max(0.16f, poolWidth * poolHeightRatio);
        float poolY = bounds.min.y + Mathf.Max(0.025f, spriteHeight * 0.055f);

        if (poolTransform != null)
        {
            poolTransform.position = new Vector3(
                bounds.center.x,
                poolY,
                targetRenderer.transform.position.z);

            Vector3 parentScale = transform.lossyScale;
            float inverseX = Mathf.Abs(parentScale.x) > 0.0001f ? 1f / Mathf.Abs(parentScale.x) : 1f;
            float inverseY = Mathf.Abs(parentScale.y) > 0.0001f ? 1f / Mathf.Abs(parentScale.y) : 1f;

            poolTransform.localScale = new Vector3(
                poolWidth * 0.5f * inverseX,
                poolHeight * inverseY,
                1f);
            poolTransform.rotation = Quaternion.identity;
        }

        if (poolRenderer != null)
        {
            poolRenderer.sortingLayerID = targetRenderer.sortingLayerID;
            poolRenderer.sortingOrder = targetRenderer.sortingOrder - 1;
        }

        if (bodyLightTransform != null)
        {
            bodyLightTransform.position = new Vector3(
                bounds.center.x,
                bounds.center.y - spriteHeight * 0.05f,
                targetRenderer.transform.position.z);
        }

        if (bodyLight != null)
        {
            float bodyRadius = Mathf.Max(spriteWidth, spriteHeight) * bodyLightRadiusMultiplier;
            bodyLight.pointLightInnerRadius = Mathf.Max(0.05f, bodyRadius * 0.28f);
            bodyLight.pointLightOuterRadius = Mathf.Max(bodyLight.pointLightInnerRadius + 0.08f, bodyRadius);
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

        if (bodyLight != null)
        {
            bodyLight.color = bodyLightColor;
            bodyLight.intensity = bodyLightIntensity * visibility;
            bodyLight.enabled = bodyLight.intensity > 0.001f;
        }

        if (glowRenderer != null)
        {
            Color glow = bodyLightColor;
            glow.a = spriteGlowAlpha * visibility * (targetRenderer != null ? targetRenderer.color.a : 1f);
            glowRenderer.color = glow;
            glowRenderer.enabled = glow.a > 0.001f && targetRenderer != null && targetRenderer.enabled;
        }
    }

    private static Sprite GetOrCreatePoolSprite()
    {
        if (sharedPoolSprite != null)
            return sharedPoolSprite;

        const int width = 64;
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
                alpha *= alpha;

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

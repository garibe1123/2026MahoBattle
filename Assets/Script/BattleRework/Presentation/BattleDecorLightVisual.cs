using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class BattleDecorLightVisual : MonoBehaviour
{
    public const string GlowName = "DecorLightGlow";

    private static Texture2D glowTexture;
    private static Sprite glowSprite;
    private static Material glowMaterial;
    private static int users;
    private Light2D source;
    private BattleDecorPart data;
    private SpriteRenderer glow;
    private float readability = 1f;
    private float phase;
    private int sortingLayerId;
    private bool ownsGlow;

    public void Configure(Light2D light, BattleDecorPart part, int targetSortingLayerId)
    {
        source = light;
        data = part;
        sortingLayerId = targetSortingLayerId;
        phase = (GetInstanceID() & 1023) * 0.37f;
        Apply();
    }

    private void Start()
    {
        if (source == null || data == null)
            return;

        EnsureGlowSprite();
        users++;
        ownsGlow = true;
        GameObject child = new(GlowName);
        child.transform.SetParent(transform, false);
        glow = child.AddComponent<SpriteRenderer>();
        glow.sprite = glowSprite;
        if (glowMaterial != null)
            glow.sharedMaterial = glowMaterial;
        glow.sortingLayerID = sortingLayerId;

        if (!data.IsStandaloneLight && transform.parent.TryGetComponent(out SpriteRenderer emitter))
        {
            glow.sortingLayerID = emitter.sortingLayerID;
            glow.sortingOrder = emitter.sortingOrder + 1;
        }
        else
        {
            Transform partsRoot = data.IsStandaloneLight ? transform.parent : transform.parent.parent;
            SpriteRenderer[] parts = partsRoot.GetComponentsInChildren<SpriteRenderer>(true);
            float nearest = float.PositiveInfinity;
            int frontLayer = int.MinValue;
            int frontOrder = int.MinValue;
            for (int i = 0; i < parts.Length; i++)
            {
                SpriteRenderer part = parts[i];
                if (part == glow || part.name == GlowName || part.sprite == null)
                    continue;
                Bounds bounds = part.bounds;
                Vector3 position = transform.position;
                position.z = bounds.center.z;
                float distance = (bounds.ClosestPoint(position) - position).sqrMagnitude;
                int layer = SortingLayer.GetLayerValueFromID(part.sortingLayerID);
                bool sameDistance = Mathf.Approximately(distance, nearest);
                if (distance > nearest && !sameDistance)
                    continue;
                if (sameDistance && (layer < frontLayer ||
                    (layer == frontLayer && part.sortingOrder <= frontOrder)))
                    continue;
                nearest = distance;
                frontLayer = layer;
                frontOrder = part.sortingOrder;
                glow.sortingLayerID = part.sortingLayerID;
                glow.sortingOrder = part.sortingOrder + 1;
            }
        }

        float diameter = Mathf.Clamp(data.LightInnerRadius * 2f, 0.08f, 0.3f);
        child.transform.localScale = new Vector3(diameter, diameter, 1f);
        Apply();
    }

    public void SetReadability(float multiplier)
    {
        readability = Mathf.Max(0f, multiplier);
        Apply();
    }

    private void OnEnable()
    {
        Apply();
    }

    private void OnDisable()
    {
        if (source != null)
            source.enabled = false;
        if (glow != null)
            glow.enabled = false;
    }

    private void Update()
    {
        Apply();
    }

    private void Apply()
    {
        if (source == null || data == null)
            return;
        float time = Time.unscaledTime;
        float flicker = 0.88f + 0.08f * Mathf.Sin(time * 7.3f + phase)
            + 0.04f * Mathf.Sin(time * 19.1f + phase * 2f);
        float intensity = data.LightIntensity * readability * flicker;
        source.enabled = isActiveAndEnabled && data.AddLight2D;
        source.intensity = intensity;
        if (glow == null)
            return;
        glow.enabled = source.enabled && intensity > 0f;
        Color color = data.LightColor;
        color.a *= Mathf.Clamp01(data.LightIntensity * readability) * flicker;
        glow.color = color;
    }

    private static void EnsureGlowSprite()
    {
        if (glowSprite != null)
            return;
        const int size = 16;
        glowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "BattleDecorLightGlow",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float radius = new Vector2((x + 0.5f) / size * 2f - 1f,
                (y + 0.5f) / size * 2f - 1f).magnitude;
            float alpha = Mathf.Clamp01(1f - radius);
            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha * alpha);
        }
        glowTexture.SetPixels(pixels);
        glowTexture.Apply(false, true);
        glowSprite = Sprite.Create(glowTexture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), size);
        glowSprite.hideFlags = HideFlags.HideAndDontSave;
        Shader shader = Resources.Load<Shader>("BattleSoftKeyLight");
        if (shader != null)
            glowMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    private void OnDestroy()
    {
        if (!ownsGlow || --users > 0)
            return;
        Destroy(glowSprite);
        Destroy(glowTexture);
        if (glowMaterial != null)
            Destroy(glowMaterial);
        glowSprite = null;
        glowTexture = null;
        glowMaterial = null;
    }
}

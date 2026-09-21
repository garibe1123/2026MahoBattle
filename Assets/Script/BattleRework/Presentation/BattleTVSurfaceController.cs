using DG.Tweening;
using UnityEngine;

public enum BattleTVState
{
    Off,
    Booting,
    Live,
    SwitchingProgram,
    Warning,
    Shutdown
}

/// <summary>
/// BattleShowMountedTV의 물리 케이스와 Screen Surface만 담당합니다.
/// Program UI와 입력을 소유하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleTVSurfaceController : MonoBehaviour
{
    [Header("Physical TV")]
    [SerializeField] private Color bodyColor = new(0.055f, 0.06f, 0.07f, 1f);
    [SerializeField] private Color backPlateColor = new(0.025f, 0.028f, 0.032f, 1f);
    [SerializeField] private Color bezelColor = new(0.10f, 0.11f, 0.13f, 1f);
    [SerializeField] private Color screenColor = new(0.78f, 0.90f, 0.92f, 1f);
    [SerializeField, Min(0f)] private float bezelThickness = 42f;
    [SerializeField, Min(0f)] private float bodyPadding = 62f;
    [SerializeField, Min(0f)] private float backDepthOffset = 22f;
    [SerializeField, Min(0f)] private float sideDepthOffset = 12f;

    [Header("Screen Material Signal")]
    [SerializeField, Range(0.2f, 2f)] private float screenBrightness = 1f;
    [SerializeField, Range(0f, 1f)] private float scanlineStrength = 0.06f;
    [SerializeField, Range(0f, 1f)] private float noiseStrength = 0.025f;
    [SerializeField, Range(0f, 1f)] private float vignetteStrength = 0.08f;
    [SerializeField, Range(0f, 1f)] private float colorBleed = 0.015f;
    [SerializeField, Range(0f, 1f)] private float curvatureHint = 0.02f;

    private RectTransform ownerRect;
    private Transform physicalRoot;
    private SpriteRenderer body;
    private SpriteRenderer backPlate;
    private SpriteRenderer sideDepth;
    private SpriteRenderer bezel;
    private SpriteRenderer screen;
    private Material screenMaterial;
    private BattleTVState state = BattleTVState.Off;
    private Tween pulseTween;
    private Sprite unitSprite;

    public BattleTVState State => state;
    public Transform PhysicalRoot => physicalRoot;
    public SpriteRenderer ScreenRenderer => screen;

    private void Awake()
    {
        ownerRect = transform as RectTransform;
        EnsurePhysicalHierarchy();
        ApplyGeometry();
        ApplySorting();
        ApplySignalParameters();
    }

    private void OnEnable()
    {
        EnsurePhysicalHierarchy();
        ApplyGeometry();
        ApplySorting();
        SetState(BattleTVState.Live, true);
    }

    private void OnDisable()
    {
        pulseTween?.Kill();
        pulseTween = null;
    }

    private void OnDestroy()
    {
        pulseTween?.Kill();
        if (screenMaterial != null)
            Destroy(screenMaterial);
        if (unitSprite != null)
            Destroy(unitSprite);
    }

    public void RefreshFromCanvas()
    {
        ownerRect = transform as RectTransform;
        EnsurePhysicalHierarchy();
        ApplyGeometry();
        ApplySorting();
    }

    public void SetState(BattleTVState next, bool immediate = false)
    {
        state = next;
        float target = next switch
        {
            BattleTVState.Off => 0.08f,
            BattleTVState.Booting => 0.55f,
            BattleTVState.SwitchingProgram => 1.18f,
            BattleTVState.Warning => 1.28f,
            BattleTVState.Shutdown => 0.15f,
            _ => 1f
        };

        pulseTween?.Kill();
        if (screen == null)
            return;

        Color baseColor = screenColor;
        if (immediate || !Application.isPlaying)
        {
            baseColor *= target * Mathf.Max(0.2f, screenBrightness);
            baseColor.a = screenColor.a;
            screen.color = baseColor;
            return;
        }

        Color from = screen.color;
        Color to = screenColor * (target * Mathf.Max(0.2f, screenBrightness));
        to.a = screenColor.a;
        pulseTween = DOTween.To(
                () => from,
                c =>
                {
                    from = c;
                    screen.color = c;
                },
                to,
                0.16f)
            .SetUpdate(true)
            .SetEase(Ease.OutCubic)
            .OnComplete(() => pulseTween = null);
    }

    public void PlayProgramSwitchPulse()
    {
        SetState(BattleTVState.SwitchingProgram);
        DOVirtual.DelayedCall(0.12f, () => SetState(BattleTVState.Live), true);
    }

    private void EnsurePhysicalHierarchy()
    {
        if (physicalRoot == null)
        {
            Transform found = transform.Find("PhysicalRoot");
            if (found != null)
                physicalRoot = found;
            else
            {
                GameObject go = new("PhysicalRoot");
                go.transform.SetParent(transform, false);
                physicalRoot = go.transform;
            }
        }

        backPlate = EnsureRenderer("TVBackPlate", backPlateColor, -104);
        sideDepth = EnsureRenderer("TVSideDepth", bodyColor * 0.82f, -103);
        body = EnsureRenderer("TVBody", bodyColor, -102);
        bezel = EnsureRenderer("TVScreenBezel", bezelColor, -101);
        screen = EnsureRenderer("TVScreenSurface", screenColor, -100);

        if (screenMaterial == null && screen != null)
        {
            Material source = screen.sharedMaterial;
            if (source != null)
            {
                screenMaterial = new Material(source) { name = "BattleTVScreen_Runtime" };
                screen.material = screenMaterial;
            }
        }
    }

    private SpriteRenderer EnsureRenderer(string name, Color color, int order)
    {
        Transform child = physicalRoot.Find(name);
        if (child == null)
        {
            GameObject go = new(name);
            go.transform.SetParent(physicalRoot, false);
            child = go.transform;
        }

        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        if (renderer == null)
            renderer = child.gameObject.AddComponent<SpriteRenderer>();

        renderer.sprite = GetUnitSprite();
        renderer.color = color;
        renderer.sortingOrder = order;
        return renderer;
    }

    private Sprite GetUnitSprite()
    {
        if (unitSprite != null)
            return unitSprite;

        Texture2D texture = new(1, 1, TextureFormat.RGBA32, false)
        {
            name = "BattleTVUnitTexture_Runtime",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        unitSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        unitSprite.name = "BattleTVUnitSprite_Runtime";
        return unitSprite;
    }

    private void ApplyGeometry()
    {
        if (ownerRect == null || physicalRoot == null)
            return;

        Vector2 size = ownerRect.rect.size;
        if (size.x <= 1f || size.y <= 1f)
            size = ownerRect.sizeDelta;

        float bodyW = size.x + bodyPadding * 2f;
        float bodyH = size.y + bodyPadding * 2f;
        float bezelW = size.x + bezelThickness * 2f;
        float bezelH = size.y + bezelThickness * 2f;

        SetRect(backPlate, bodyW, bodyH, new Vector3(backDepthOffset, -backDepthOffset, 28f));
        SetRect(sideDepth, bodyW, bodyH, new Vector3(sideDepthOffset, -sideDepthOffset, 18f));
        SetRect(body, bodyW, bodyH, new Vector3(0f, 0f, 12f));
        SetRect(bezel, bezelW, bezelH, new Vector3(0f, 0f, 7f));
        SetRect(screen, size.x, size.y, new Vector3(0f, 0f, 3f));
    }

    private static void SetRect(SpriteRenderer renderer, float width, float height, Vector3 localPosition)
    {
        if (renderer == null)
            return;

        renderer.transform.localPosition = localPosition;
        renderer.transform.localRotation = Quaternion.identity;
        renderer.transform.localScale = new Vector3(Mathf.Max(1f, width), Mathf.Max(1f, height), 1f);
    }

    private void ApplySorting()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
            return;

        int baseOrder = canvas.sortingOrder - 120;
        SpriteRenderer[] renderers = { backPlate, sideDepth, body, bezel, screen };
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.sortingLayerID = canvas.sortingLayerID;
            renderer.sortingOrder = baseOrder + i;
        }
    }

    private void ApplySignalParameters()
    {
        if (screenMaterial == null)
            return;

        SetFloatIfExists("_Brightness", screenBrightness);
        SetFloatIfExists("_ScanlineStrength", scanlineStrength);
        SetFloatIfExists("_NoiseStrength", noiseStrength);
        SetFloatIfExists("_VignetteStrength", vignetteStrength);
        SetFloatIfExists("_ColorBleed", colorBleed);
        SetFloatIfExists("_CurvatureHint", curvatureHint);
    }

    private void SetFloatIfExists(string property, float value)
    {
        if (screenMaterial != null && screenMaterial.HasProperty(property))
            screenMaterial.SetFloat(property, value);
    }
}

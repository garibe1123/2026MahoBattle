using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 짧은 만화식 말풍선 꼬리 프리셋 컨트롤러.
///
/// Target Pivot은 "방향 선택"에만 사용하고, 꼬리는 절대로 Target까지 늘어나지 않습니다.
/// RightUp / RightMid / RightDown 3개 프리셋을 기준으로 하고,
/// 왼쪽 방향은 동일 Sprite를 X Flip해서 사용합니다.
///
/// 커스텀 Sprite가 비어 있으면 런타임에서 각진 흑백 꼬리 3종을 자동 생성합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubbleTailPresetController : MonoBehaviour
{
    private enum VerticalPreset
    {
        Up,
        Mid,
        Down
    }

    [Header("References")]
    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;

    [Header("Optional Custom Sprites")]
    [Tooltip("오른쪽 위 방향 꼬리. 비어 있으면 런타임 기본 만화 꼬리를 사용합니다.")]
    [SerializeField] private Sprite rightUpSprite;
    [Tooltip("오른쪽 정면 방향 꼬리. 비어 있으면 런타임 기본 만화 꼬리를 사용합니다.")]
    [SerializeField] private Sprite rightMidSprite;
    [Tooltip("오른쪽 아래 방향 꼬리. 비어 있으면 런타임 기본 만화 꼬리를 사용합니다.")]
    [SerializeField] private Sprite rightDownSprite;

    [Header("Layout")]
    [SerializeField] private Vector2 tailSize = new(148f, 78f);
    [Tooltip("말풍선 모서리에서 안쪽으로 얼마나 붙일지 조절합니다.")]
    [SerializeField] private float edgeInset = 10f;
    [Tooltip("Up / Down 판정 기준. Target이 말풍선 중심보다 이만큼 위/아래면 해당 프리셋을 씁니다.")]
    [SerializeField] private float verticalThreshold = 58f;

    [Header("Preset Rotation")]
    [SerializeField, Range(-45f, 45f)] private float upRotation = 18f;
    [SerializeField, Range(-45f, 45f)] private float midRotation = 0f;
    [SerializeField, Range(-45f, 45f)] private float downRotation = -18f;

    private Image tailImage;
    private RectTransform tailRect;

    private static Sprite generatedUp;
    private static Sprite generatedMid;
    private static Sprite generatedDown;

    private static Texture2D generatedUpTexture;
    private static Texture2D generatedMidTexture;
    private static Texture2D generatedDownTexture;

    public void Configure(
        RectTransform bubble,
        RectTransform target)
    {
        bubbleRect = bubble;
        targetPivot = target;

        EnsureVisual();
        RefreshPreset();
    }

    public void SetTarget(RectTransform target)
    {
        targetPivot = target;
        RefreshPreset();
    }

    private void Awake()
    {
        EnsureVisual();
    }

    private void OnEnable()
    {
        EnsureVisual();
        RefreshPreset();
    }

    private void LateUpdate()
    {
        RefreshPreset();
    }

    private void EnsureVisual()
    {
        if (tailImage != null && tailRect != null)
            return;

        GameObject tail = new("TailImage", typeof(RectTransform));
        tail.transform.SetParent(transform, false);

        tailRect = tail.GetComponent<RectTransform>();
        tailRect.anchorMin = tailRect.anchorMax = new Vector2(1f, 0.5f);
        tailRect.pivot = new Vector2(0f, 0.5f);
        tailRect.sizeDelta = tailSize;

        tailImage = tail.AddComponent<Image>();
        tailImage.type = Image.Type.Simple;
        tailImage.preserveAspect = false;
        tailImage.raycastTarget = false;
        tailImage.color = Color.white;

        // 말풍선 본체보다 뒤에 보이게 하되 같은 Canvas에서 렌더링합니다.
        tail.transform.SetAsFirstSibling();
    }

    private void RefreshPreset()
    {
        if (tailImage == null ||
            tailRect == null ||
            bubbleRect == null ||
            targetPivot == null)
        {
            if (tailImage != null)
                tailImage.enabled = false;
            return;
        }

        Vector3 bubbleCenterWorld =
            bubbleRect.TransformPoint(
                bubbleRect.rect.center);

        Vector3 targetWorld =
            targetPivot.position;

        Vector3 localDelta =
            bubbleRect.InverseTransformVector(
                targetWorld - bubbleCenterWorld);

        bool targetOnRight =
            localDelta.x >= 0f;

        VerticalPreset vertical;
        if (localDelta.y > verticalThreshold)
            vertical = VerticalPreset.Up;
        else if (localDelta.y < -verticalThreshold)
            vertical = VerticalPreset.Down;
        else
            vertical = VerticalPreset.Mid;

        tailImage.enabled = true;
        tailImage.sprite = ResolveSprite(vertical);
        tailImage.preserveAspect = false;

        ApplyLayout(
            targetOnRight,
            vertical);
    }

    private void ApplyLayout(
        bool targetOnRight,
        VerticalPreset vertical)
    {
        float anchorY;
        float rotation;

        switch (vertical)
        {
            case VerticalPreset.Up:
                anchorY = 0.82f;
                rotation = upRotation;
                break;

            case VerticalPreset.Down:
                anchorY = 0.18f;
                rotation = downRotation;
                break;

            default:
                anchorY = 0.50f;
                rotation = midRotation;
                break;
        }

        if (targetOnRight)
        {
            tailRect.anchorMin =
                tailRect.anchorMax =
                    new Vector2(1f, anchorY);

            tailRect.pivot =
                new Vector2(0f, 0.5f);

            tailRect.anchoredPosition =
                new Vector2(-edgeInset, 0f);

            tailRect.localScale =
                Vector3.one;

            tailRect.localRotation =
                Quaternion.Euler(0f, 0f, rotation);
        }
        else
        {
            tailRect.anchorMin =
                tailRect.anchorMax =
                    new Vector2(0f, anchorY);

            tailRect.pivot =
                new Vector2(1f, 0.5f);

            tailRect.anchoredPosition =
                new Vector2(edgeInset, 0f);

            tailRect.localScale =
                new Vector3(-1f, 1f, 1f);

            // X Flip 후 회전 방향도 반대로 적용합니다.
            tailRect.localRotation =
                Quaternion.Euler(0f, 0f, -rotation);
        }

        tailRect.sizeDelta = tailSize;
    }

    private Sprite ResolveSprite(
        VerticalPreset vertical)
    {
        switch (vertical)
        {
            case VerticalPreset.Up:
                if (rightUpSprite != null)
                    return rightUpSprite;

                if (generatedUp == null)
                {
                    generatedUp =
                        CreateGeneratedSprite(
                            VerticalPreset.Up,
                            out generatedUpTexture);
                }

                return generatedUp;

            case VerticalPreset.Down:
                if (rightDownSprite != null)
                    return rightDownSprite;

                if (generatedDown == null)
                {
                    generatedDown =
                        CreateGeneratedSprite(
                            VerticalPreset.Down,
                            out generatedDownTexture);
                }

                return generatedDown;

            default:
                if (rightMidSprite != null)
                    return rightMidSprite;

                if (generatedMid == null)
                {
                    generatedMid =
                        CreateGeneratedSprite(
                            VerticalPreset.Mid,
                            out generatedMidTexture);
                }

                return generatedMid;
        }
    }

    private static Sprite CreateGeneratedSprite(
        VerticalPreset preset,
        out Texture2D texture)
    {
        const int width = 192;
        const int height = 112;

        texture = new Texture2D(
            width,
            height,
            TextureFormat.RGBA32,
            false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
            name = $"BattleComicTail_{preset}_RuntimeTexture"
        };

        Color32 transparent =
            new(0, 0, 0, 0);

        Color32 black =
            new(4, 4, 7, 255);

        Color32 white =
            new(248, 248, 242, 255);

        Color32[] pixels =
            new Color32[width * height];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = transparent;

        Vector2[] outer =
            GetOuterPolygon(preset);

        RasterizePolygon(
            pixels,
            width,
            height,
            outer,
            black);

        Vector2 center =
            ComputeCenter(outer);

        Vector2[] inner =
            new Vector2[outer.Length];

        // 일정 비율 안쪽으로 들어간 흰 면으로 굵은 검은 외곽을 만듭니다.
        const float innerScale = 0.86f;

        for (int i = 0; i < outer.Length; i++)
        {
            inner[i] =
                center +
                (outer[i] - center) *
                innerScale;
        }

        RasterizePolygon(
            pixels,
            width,
            height,
            inner,
            white);

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);

        sprite.name =
            $"BattleComicTail_{preset}_RuntimeSprite";

        sprite.hideFlags =
            HideFlags.HideAndDontSave;

        return sprite;
    }

    private static Vector2[] GetOuterPolygon(
        VerticalPreset preset)
    {
        switch (preset)
        {
            case VerticalPreset.Up:
                return new[]
                {
                    // Bubble connection.
                    new Vector2(0.00f, 0.32f),
                    new Vector2(0.18f, 0.34f),

                    // Comic notch.
                    new Vector2(0.12f, 0.17f),
                    new Vector2(0.30f, 0.34f),

                    // Main body bends upward.
                    new Vector2(0.68f, 0.43f),
                    new Vector2(0.82f, 0.50f),

                    // Point.
                    new Vector2(1.00f, 0.72f),

                    // Return edge.
                    new Vector2(0.78f, 0.69f),
                    new Vector2(0.62f, 0.64f),
                    new Vector2(0.28f, 0.57f),

                    // Lower notch.
                    new Vector2(0.11f, 0.76f),
                    new Vector2(0.18f, 0.55f),
                    new Vector2(0.00f, 0.55f)
                };

            case VerticalPreset.Down:
                return new[]
                {
                    new Vector2(0.00f, 0.45f),
                    new Vector2(0.18f, 0.45f),
                    new Vector2(0.11f, 0.24f),
                    new Vector2(0.28f, 0.43f),

                    new Vector2(0.62f, 0.36f),
                    new Vector2(0.78f, 0.31f),

                    new Vector2(1.00f, 0.08f),

                    new Vector2(0.82f, 0.50f),
                    new Vector2(0.68f, 0.57f),
                    new Vector2(0.30f, 0.66f),

                    new Vector2(0.12f, 0.83f),
                    new Vector2(0.18f, 0.64f),
                    new Vector2(0.00f, 0.68f)
                };

            default:
                return new[]
                {
                    // Bubble-side connection.
                    new Vector2(0.00f, 0.34f),
                    new Vector2(0.18f, 0.36f),

                    // Strong notch like the supplied comic reference.
                    new Vector2(0.105f, 0.18f),
                    new Vector2(0.31f, 0.37f),

                    // Broad, slightly uneven body.
                    new Vector2(0.68f, 0.39f),
                    new Vector2(0.83f, 0.42f),

                    // Pointed tip.
                    new Vector2(1.00f, 0.50f),

                    // Lower return edge.
                    new Vector2(0.82f, 0.61f),
                    new Vector2(0.64f, 0.64f),
                    new Vector2(0.29f, 0.63f),

                    // Bottom notch.
                    new Vector2(0.10f, 0.82f),
                    new Vector2(0.18f, 0.62f),
                    new Vector2(0.00f, 0.66f)
                };
        }
    }

    private static Vector2 ComputeCenter(
        Vector2[] polygon)
    {
        Vector2 sum = Vector2.zero;

        for (int i = 0; i < polygon.Length; i++)
            sum += polygon[i];

        return sum /
               Mathf.Max(1, polygon.Length);
    }

    private static void RasterizePolygon(
        Color32[] pixels,
        int width,
        int height,
        Vector2[] polygon,
        Color32 color)
    {
        for (int y = 0; y < height; y++)
        {
            float py =
                (y + 0.5f) /
                height;

            for (int x = 0; x < width; x++)
            {
                float px =
                    (x + 0.5f) /
                    width;

                if (PointInPolygon(
                        new Vector2(px, py),
                        polygon))
                {
                    pixels[y * width + x] =
                        color;
                }
            }
        }
    }

    private static bool PointInPolygon(
        Vector2 point,
        Vector2[] polygon)
    {
        bool inside = false;
        int j = polygon.Length - 1;

        for (int i = 0;
             i < polygon.Length;
             i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];

            bool intersects =
                ((a.y > point.y) !=
                 (b.y > point.y)) &&
                (point.x <
                 (b.x - a.x) *
                 (point.y - a.y) /
                 (b.y - a.y) +
                 a.x);

            if (intersects)
                inside = !inside;

            j = i;
        }

        return inside;
    }
}

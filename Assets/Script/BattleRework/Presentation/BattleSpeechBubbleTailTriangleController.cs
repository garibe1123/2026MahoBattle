using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 말풍선 옆에 붙는 코드 생성 Tail.
///
/// OUTLINE / FILL을 별도 UI Image로 분리합니다.
/// OUTLINE에는 Stroke Material을 적용할 수 있고,
/// FILL은 기본 UI Material을 유지합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubbleTailTriangleController : MonoBehaviour
{
    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;
    [SerializeField] private bool exactTargetMode;

    private BattleSpeechBubbleTailStyle style;
    private Color fillColor =
        new(0.97f, 0.97f, 0.94f, 1f);

    private Material strokeMaterial;

    private RectTransform tailRect;
    private Image outlineImage;
    private Image fillImage;

    private Texture2D outlineTexture;
    private Texture2D fillTexture;
    private Sprite outlineSprite;
    private Sprite fillSprite;
    private int appliedHash = int.MinValue;

    public void Configure(
        RectTransform bubble,
        RectTransform target,
        Color color,
        BattleSpeechBubbleTailStyle tailStyle,
        Material outlineMaterial = null)
    {
        bubbleRect = bubble;
        targetPivot = target;
        fillColor = color;
        style = tailStyle ?? new BattleSpeechBubbleTailStyle();
        strokeMaterial = outlineMaterial;

        EnsureTail();
        RefreshSpriteIfNeeded(force: true);
        RefreshPlacement();
    }

    public void SetTarget(RectTransform target)
    {
        targetPivot = target;
        RefreshPlacement();
    }

    /// <summary>
    /// true면 Tail의 Tip을 targetPivot 중심에 정확히 고정합니다.
    /// 아이템 설명/비교 Tooltip처럼 Bubble이 화면 중앙 쪽으로 이동하는 UI에서 사용합니다.
    /// 기존 Host/Combat 말풍선은 false를 유지해 기존 수평 Tail 배치를 그대로 사용합니다.
    /// </summary>
    public void SetExactTargetMode(bool enabled)
    {
        exactTargetMode = enabled;
        RefreshPlacement();
    }

    private void Awake()
    {
        if (style == null)
            style = new BattleSpeechBubbleTailStyle();

        EnsureTail();
        RefreshSpriteIfNeeded(force: true);
    }

    private void OnEnable()
    {
        if (style == null)
            style = new BattleSpeechBubbleTailStyle();

        EnsureTail();
        RefreshSpriteIfNeeded(force: true);
        RefreshPlacement();
    }

    private void LateUpdate()
    {
        RefreshSpriteIfNeeded(force: false);
        RefreshPlacement();
    }

    private void OnDestroy()
    {
        ReleaseRuntimeSprites();
    }

    private void EnsureTail()
    {
        if (tailRect != null &&
            outlineImage != null &&
            fillImage != null)
        {
            return;
        }

        Transform existingTail =
            transform.Find(
                "TailShape");

        GameObject tail;

        if (existingTail != null)
        {
            tail =
                existingTail.gameObject;
        }
        else
        {
            tail =
                new GameObject(
                    "TailShape",
                    typeof(RectTransform));

            tail.transform.SetParent(
                transform,
                false);
        }

        tailRect =
            tail.GetComponent<RectTransform>();

        Vector2 parentPivot =
            bubbleRect != null
                ? bubbleRect.pivot
                : new Vector2(0.5f, 0.5f);

        tailRect.anchorMin =
            tailRect.anchorMax =
                parentPivot;

        tailRect.pivot =
            new Vector2(0.5f, 0.5f);

        outlineImage =
            tail.GetComponent<Image>();

        if (outlineImage == null)
            outlineImage = tail.AddComponent<Image>();

        outlineImage.type =
            Image.Type.Simple;

        outlineImage.preserveAspect =
            false;

        outlineImage.raycastTarget =
            false;

        Transform existingFill =
            tail.transform.Find(
                "TailFill");

        GameObject fillObject;

        if (existingFill != null)
        {
            fillObject =
                existingFill.gameObject;
        }
        else
        {
            fillObject =
                new GameObject(
                    "TailFill",
                    typeof(RectTransform));

            fillObject.transform.SetParent(
                tail.transform,
                false);
        }

        RectTransform fillRect =
            fillObject.GetComponent<RectTransform>();

        fillRect.anchorMin =
            Vector2.zero;

        fillRect.anchorMax =
            Vector2.one;

        fillRect.pivot =
            new Vector2(0.5f, 0.5f);

        fillRect.offsetMin =
            Vector2.zero;

        fillRect.offsetMax =
            Vector2.zero;

        fillImage =
            fillObject.GetComponent<Image>();

        if (fillImage == null)
            fillImage = fillObject.AddComponent<Image>();

        fillImage.type =
            Image.Type.Simple;

        fillImage.preserveAspect =
            false;

        fillImage.raycastTarget =
            false;

        fillObject.transform.SetAsLastSibling();

        // Bubble Frame 이후, 텍스트/배지 이전에 Tail 전체가 오도록 유지합니다.
        tail.transform.SetAsLastSibling();
    }

    private void RefreshSpriteIfNeeded(bool force)
    {
        if (outlineImage == null ||
            fillImage == null)
        {
            return;
        }

        BattleSpeechBubbleTailStyle activeStyle =
            style ?? new BattleSpeechBubbleTailStyle();

        int hash =
            activeStyle.ComputeHash();

        unchecked
        {
            hash =
                hash * 31 +
                fillColor.GetHashCode();

            hash =
                hash * 31 +
                (strokeMaterial != null
                    ? strokeMaterial.GetInstanceID()
                    : 0);
        }

        if (!force &&
            hash == appliedHash &&
            outlineSprite != null &&
            fillSprite != null)
        {
            if (outlineImage.sprite != outlineSprite)
                outlineImage.sprite = outlineSprite;

            if (fillImage.sprite != fillSprite)
                fillImage.sprite = fillSprite;

            ApplyImageStyle(
                activeStyle);
            return;
        }

        appliedHash =
            hash;

        ReleaseRuntimeSprites();

        outlineTexture =
            BattleSpeechBubbleTailTextureBuilder.BuildOutlineMaskTexture(
                activeStyle,
                "BattleSpeechTail_OutlineMask_Runtime");

        fillTexture =
            BattleSpeechBubbleTailTextureBuilder.BuildFillMaskTexture(
                activeStyle,
                "BattleSpeechTail_FillMask_Runtime");

        outlineSprite =
            CreateRuntimeSprite(
                outlineTexture,
                "BattleSpeechTail_Outline_RuntimeSprite");

        fillSprite =
            CreateRuntimeSprite(
                fillTexture,
                "BattleSpeechTail_Fill_RuntimeSprite");

        outlineImage.sprite =
            outlineSprite;

        fillImage.sprite =
            fillSprite;

        ApplyImageStyle(
            activeStyle);
    }

    private void ApplyImageStyle(
        BattleSpeechBubbleTailStyle activeStyle)
    {
        if (outlineImage != null)
        {
            outlineImage.color =
                activeStyle != null
                    ? activeStyle.outlineColor
                    : Color.black;

            outlineImage.material =
                strokeMaterial;
        }

        if (fillImage != null)
        {
            fillImage.color =
                fillColor;

            fillImage.material =
                null;
        }
    }

    private void RefreshPlacement()
    {
        if (tailRect == null ||
            outlineImage == null ||
            fillImage == null ||
            bubbleRect == null ||
            targetPivot == null)
        {
            if (outlineImage != null)
                outlineImage.enabled = false;

            if (fillImage != null)
                fillImage.enabled = false;

            return;
        }

        BattleSpeechBubbleTailStyle activeStyle =
            style ?? new BattleSpeechBubbleTailStyle();

        outlineImage.enabled = true;
        fillImage.enabled = true;

        if (exactTargetMode)
        {
            PlaceExactTarget(activeStyle);
            return;
        }

        tailRect.anchorMin =
            tailRect.anchorMax =
                bubbleRect.pivot;

        tailRect.sizeDelta =
            BattleSpeechBubbleTailTextureBuilder.GetRenderSize(
                activeStyle.uiSize);

        Vector3 bubbleCenterWorld =
            bubbleRect.TransformPoint(
                bubbleRect.rect.center);

        Vector3 localDelta3 =
            bubbleRect.InverseTransformVector(
                targetPivot.position -
                bubbleCenterWorld);

        Vector2 localDelta =
            new(
                localDelta3.x,
                localDelta3.y);

        Rect rect =
            bubbleRect.rect;

        bool placeRight =
            localDelta.x >= 0f;

        PlaceHorizontal(
            placeRight,
            localDelta.y,
            rect,
            activeStyle);
    }

    private void PlaceExactTarget(
        BattleSpeechBubbleTailStyle activeStyle)
    {
        Vector2 targetLocal =
            bubbleRect.InverseTransformPoint(
                targetPivot.position);

        Rect bubble =
            bubbleRect.rect;

        if (bubble.Contains(targetLocal))
        {
            outlineImage.enabled = false;
            fillImage.enabled = false;
            return;
        }

        Vector2 center =
            bubble.center;

        Vector2 ray =
            targetLocal - center;

        if (ray.sqrMagnitude <= 0.001f)
        {
            outlineImage.enabled = false;
            fillImage.enabled = false;
            return;
        }

        float tx =
            Mathf.Abs(ray.x) > 0.001f
                ? (ray.x > 0f
                    ? bubble.xMax - center.x
                    : center.x - bubble.xMin) /
                  Mathf.Abs(ray.x)
                : float.PositiveInfinity;

        float ty =
            Mathf.Abs(ray.y) > 0.001f
                ? (ray.y > 0f
                    ? bubble.yMax - center.y
                    : center.y - bubble.yMin) /
                  Mathf.Abs(ray.y)
                : float.PositiveInfinity;

        float edgeT =
            Mathf.Min(tx, ty);

        Vector2 edgePoint =
            center +
            ray * edgeT;

        Vector2 direction =
            (targetLocal - edgePoint).normalized;

        float overlapAmount =
            Mathf.Min(
                Mathf.Max(0f, activeStyle.overlap),
                Mathf.Min(
                    bubble.width,
                    bubble.height) * 0.35f);

        Vector2 rootPoint =
            edgePoint -
            direction * overlapAmount;

        Vector2 rootToTarget =
            targetLocal -
            rootPoint;

        float distance =
            rootToTarget.magnitude;

        if (distance <= 2f)
        {
            outlineImage.enabled = false;
            fillImage.enabled = false;
            return;
        }

        direction =
            rootToTarget.normalized;

        float angle =
            Mathf.Atan2(
                direction.y,
                direction.x) *
            Mathf.Rad2Deg;

        Vector2 renderSize =
            BattleSpeechBubbleTailTextureBuilder.GetRenderSize(
                activeStyle.uiSize);

        float tipX =
            Mathf.Clamp(
                activeStyle.tip.x,
                0.1f,
                1f);

        float visualWidth =
            distance /
            tipX;

        tailRect.anchorMin =
            tailRect.anchorMax =
                bubbleRect.pivot;

        tailRect.pivot =
            new Vector2(
                0f,
                0.5f);

        tailRect.anchoredPosition =
            rootPoint;

        tailRect.sizeDelta =
            new Vector2(
                visualWidth,
                renderSize.y);

        tailRect.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                angle);

        tailRect.localScale =
            Vector3.one;
    }

    private void PlaceHorizontal(
        bool right,
        float targetLocalY,
        Rect bubble,
        BattleSpeechBubbleTailStyle activeStyle)
    {
        tailRect.pivot =
            new Vector2(
                0.5f,
                0.5f);

        Vector2 size =
            BattleSpeechBubbleTailTextureBuilder.GetRenderSize(
                activeStyle.uiSize);

        float padding =
            Mathf.Min(
                Mathf.Max(
                    0f,
                    activeStyle.edgePadding),
                bubble.height * 0.45f);

        float y =
            Mathf.Clamp(
                targetLocalY,
                bubble.yMin + padding,
                bubble.yMax - padding);

        float outside =
            size.x * 0.5f -
            Mathf.Max(
                0f,
                activeStyle.overlap);

        tailRect.anchoredPosition =
            new Vector2(
                right
                    ? bubble.xMax + outside
                    : bubble.xMin - outside,
                y);

        tailRect.localRotation =
            Quaternion.identity;

        tailRect.localScale =
            right
                ? Vector3.one
                : new Vector3(-1f, 1f, 1f);
    }

    private static Sprite CreateRuntimeSprite(
        Texture2D texture,
        string spriteName)
    {
        if (texture == null)
            return null;

        Sprite sprite =
            Sprite.Create(
                texture,
                new Rect(
                    0f,
                    0f,
                    texture.width,
                    texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);

        sprite.name =
            spriteName;

        sprite.hideFlags =
            HideFlags.HideAndDontSave;

        return sprite;
    }

    private void ReleaseRuntimeSprites()
    {
        if (outlineSprite != null)
        {
            Destroy(outlineSprite);
            outlineSprite = null;
        }

        if (fillSprite != null)
        {
            Destroy(fillSprite);
            fillSprite = null;
        }

        if (outlineTexture != null)
        {
            Destroy(outlineTexture);
            outlineTexture = null;
        }

        if (fillTexture != null)
        {
            Destroy(fillTexture);
            fillTexture = null;
        }
    }
}

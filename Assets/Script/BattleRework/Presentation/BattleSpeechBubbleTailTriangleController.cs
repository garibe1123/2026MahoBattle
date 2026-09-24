using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 말풍선 옆에 붙는 코드 생성 Tail.
///
/// 외부 PNG는 사용하지 않습니다.
/// BattleSpeechBubbleTailStyle의 Pivot/폭/Stroke 값을
/// BattleSpeechBubbleTailTextureBuilder로 Texture2D에 Rasterize한 뒤
/// Unity 기본 Image로 표시합니다.
///
/// Editor Preview와 Runtime이 같은 Builder를 사용하므로 모양이 동일합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubbleTailTriangleController : MonoBehaviour
{
    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;

    private BattleSpeechBubbleTailStyle style;
    private Color fillColor =
        new(0.97f, 0.97f, 0.94f, 1f);

    private RectTransform tailRect;
    private Image tailImage;

    private Texture2D runtimeTexture;
    private Sprite runtimeSprite;
    private int appliedHash = int.MinValue;

    public void Configure(
        RectTransform bubble,
        RectTransform target,
        Color color,
        BattleSpeechBubbleTailStyle tailStyle)
    {
        bubbleRect = bubble;
        targetPivot = target;
        fillColor = color;
        style = tailStyle ?? new BattleSpeechBubbleTailStyle();

        EnsureTail();
        RefreshSpriteIfNeeded(force: true);
        RefreshPlacement();
    }

    public void SetTarget(RectTransform target)
    {
        targetPivot = target;
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
        ReleaseRuntimeSprite();
    }

    private void EnsureTail()
    {
        if (tailRect != null &&
            tailImage != null)
        {
            return;
        }

        GameObject tail =
            new("TailShape", typeof(RectTransform));

        tail.transform.SetParent(transform, false);

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

        tailImage =
            tail.AddComponent<Image>();

        tailImage.type =
            Image.Type.Simple;

        tailImage.preserveAspect =
            false;

        tailImage.raycastTarget =
            false;

        tailImage.color =
            Color.white;

        // Bubble Face 이후, 텍스트/배지 이전에 생성되도록 호출자가 구성합니다.
        tail.transform.SetAsLastSibling();
    }

    private void RefreshSpriteIfNeeded(bool force)
    {
        if (tailImage == null)
            return;

        BattleSpeechBubbleTailStyle activeStyle =
            style ?? new BattleSpeechBubbleTailStyle();

        int hash =
            activeStyle.ComputeHash();

        unchecked
        {
            hash =
                hash * 31 +
                fillColor.GetHashCode();
        }

        if (!force &&
            hash == appliedHash &&
            runtimeSprite != null)
        {
            if (tailImage.sprite != runtimeSprite)
                tailImage.sprite = runtimeSprite;

            return;
        }

        appliedHash = hash;

        ReleaseRuntimeSprite();

        runtimeTexture =
            BattleSpeechBubbleTailTextureBuilder.BuildTexture(
                activeStyle,
                fillColor,
                "BattleSpeechTail_Runtime");

        runtimeSprite =
            Sprite.Create(
                runtimeTexture,
                new Rect(
                    0f,
                    0f,
                    runtimeTexture.width,
                    runtimeTexture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);

        runtimeSprite.name =
            "BattleSpeechTail_RuntimeSprite";

        runtimeSprite.hideFlags =
            HideFlags.HideAndDontSave;

        tailImage.sprite =
            runtimeSprite;
    }

    private void RefreshPlacement()
    {
        if (tailRect == null ||
            tailImage == null ||
            bubbleRect == null ||
            targetPivot == null)
        {
            if (tailImage != null)
                tailImage.enabled = false;

            return;
        }

        BattleSpeechBubbleTailStyle activeStyle =
            style ?? new BattleSpeechBubbleTailStyle();

        tailImage.enabled = true;

        tailRect.anchorMin =
            tailRect.anchorMax =
                bubbleRect.pivot;

        tailRect.sizeDelta =
            new Vector2(
                Mathf.Max(8f, activeStyle.uiSize.x),
                Mathf.Max(8f, activeStyle.uiSize.y));

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

    private void PlaceHorizontal(
        bool right,
        float targetLocalY,
        Rect bubble,
        BattleSpeechBubbleTailStyle activeStyle)
    {
        Vector2 size =
            new(
                Mathf.Max(8f, activeStyle.uiSize.x),
                Mathf.Max(8f, activeStyle.uiSize.y));

        float padding =
            Mathf.Min(
                Mathf.Max(0f, activeStyle.edgePadding),
                bubble.height * 0.45f);

        float y =
            Mathf.Clamp(
                targetLocalY,
                bubble.yMin + padding,
                bubble.yMax - padding);

        float outside =
            size.x * 0.5f -
            Mathf.Max(0f, activeStyle.overlap);

        tailRect.anchoredPosition =
            new Vector2(
                right
                    ? bubble.xMax + outside
                    : bubble.xMin - outside,
                y);

        // 좌측은 180도 회전하지 않고 X Flip만 사용합니다.
        // 번개형 Pivot의 위/아래 방향이 뒤집히지 않습니다.
        tailRect.localRotation =
            Quaternion.identity;

        tailRect.localScale =
            right
                ? Vector3.one
                : new Vector3(-1f, 1f, 1f);
    }

    private void ReleaseRuntimeSprite()
    {
        if (runtimeSprite != null)
        {
            Destroy(runtimeSprite);
            runtimeSprite = null;
        }

        if (runtimeTexture != null)
        {
            Destroy(runtimeTexture);
            runtimeTexture = null;
        }
    }
}

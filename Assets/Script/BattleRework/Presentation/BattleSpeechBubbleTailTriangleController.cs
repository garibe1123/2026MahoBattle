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
[DefaultExecutionOrder(61000)]
public sealed class BattleSpeechBubbleTailTriangleController : MonoBehaviour
{
    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;
    [SerializeField] private bool exactTargetMode;
    [SerializeField, Range(0.15f, 0.85f)] private float comicSideAttachment = 0.5f;
    [SerializeField, Range(16f, 120f)] private float comicHostTailLength = 48f;

    private BattleSpeechBubbleTailStyle style;
    private Color fillColor =
        new(0.97f, 0.97f, 0.94f, 1f);

    private Material strokeMaterial;

    private RectTransform tailRect;
    private RectTransform fillRect;
    private Image outlineImage;
    private Image fillImage;

    private Texture2D outlineTexture;
    private Texture2D fillTexture;
    private Sprite outlineSprite;
    private Sprite fillSprite;
    private int appliedHash = int.MinValue;
    private RectTransform registeredStyleRoot;
    private BattleSpeechBubbleFrameFillController frameController;
    private readonly Vector3[] frameCorners = new Vector3[4];
    private readonly Vector2[] comicFrameCorners = new Vector2[4];
    private float rootJoinHalfHeight;
    internal BattleSpeechBubbleTailStyle ComicStyle => style;

    public void Configure(
        RectTransform bubble,
        RectTransform target,
        Color color,
        BattleSpeechBubbleTailStyle tailStyle,
        Material outlineMaterial = null)
    {
        bubbleRect = bubble;
        frameController = bubble != null
            ? bubble.GetComponentInChildren<BattleSpeechBubbleFrameFillController>(true)
            : null;
        targetPivot = target;
        if (frameController != null) frameController.ComicTail = this;
        fillColor = color;
        style = tailStyle ?? new BattleSpeechBubbleTailStyle();
        if (BattleUiStyleController.IsComic)
        {
            style = style.CreateRuntimeVariant(null, 0);
            style.ApplyLightningPreset();
            style.outlineColor = BattleUiStyleController.Settings.comicWhite;
            fillColor = BattleUiStyleController.Settings.comicInk;
        }
        strokeMaterial = outlineMaterial;

        EnsureTail();
        RefreshSpriteIfNeeded(force: true);
        RefreshPlacement();
        if (registeredStyleRoot != bubbleRect)
        {
            registeredStyleRoot = bubbleRect;
            BattleUiStyleController.RegisterRoot(registeredStyleRoot);
        }
        BattleUiStyleController.RegisterSpeechTail(outlineImage, fillImage,
            frameController != null ? frameController.FrameRect : null);
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

        fillRect =
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

        fillObject.transform.SetParent(tailRect.parent, false);
        fillObject.transform.SetAsLastSibling();

        tail.transform.SetAsFirstSibling();
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

        rootJoinHalfHeight = BattleSpeechBubbleTailTextureBuilder.GetRootJoinHalfHeight(activeStyle);

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

            BattleUiStyleController.PrepareMaterialTarget(outlineImage);
            outlineImage.material =
                strokeMaterial;
            BattleShowPresentationManager.Instance?.RegisterUiMaterialTarget(
                outlineImage, BattleUiMaterialController.UiChannel.PresenterDialogue);
        }

        if (fillImage != null)
        {
            fillImage.color =
                fillColor;

            BattleUiStyleController.PrepareMaterialTarget(fillImage);
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

        Rect attachmentRect = GetAttachmentRect();
        if (tailRect.GetSiblingIndex() != 0) tailRect.SetAsFirstSibling();

        if (exactTargetMode)
        {
            PlaceExactTarget(activeStyle, attachmentRect);
            SyncFillPlacement();
            RectTransform frame = frameController != null ? frameController.FrameRect : null;
            if (BattleUiStyleController.IsComic && frame != null && frame.parent == fillRect.parent &&
                fillRect.GetSiblingIndex() != frame.GetSiblingIndex() + 1)
                fillRect.SetSiblingIndex(frame.GetSiblingIndex() + 1);
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

        Rect rect = attachmentRect;

        bool placeRight =
            BattleUiStyleController.IsComic || localDelta.x >= 0f;

        PlaceHorizontal(
            placeRight,
            localDelta.y + bubbleRect.rect.center.y,
            rect,
            activeStyle);
        SyncFillPlacement();
    }

    private void PlaceExactTarget(
        BattleSpeechBubbleTailStyle activeStyle, Rect bubble)
    {
        Vector3 targetCenterWorld =
            targetPivot.TransformPoint(
                targetPivot.rect.center);

        Vector2 targetLocal =
            bubbleRect.InverseTransformPoint(
                targetCenterWorld);

        if (BattleUiStyleController.IsComic)
        {

            PlaceComicTopTarget(activeStyle, bubble, targetLocal);
            return;
        }

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

        float contentHeight = renderSize.y / BattleSpeechBubbleTailTextureBuilder.VerticalDisplayScale;
        float rootOffset = (Mathf.Clamp01(activeStyle.rootY) - 0.5f) * contentHeight;
        float tipOffset = (Mathf.Clamp01(activeStyle.tip.y) - Mathf.Clamp01(activeStyle.rootY)) * contentHeight;
        float halfWidth = Mathf.Clamp(activeStyle.rootHalfWidth, 0.02f, 0.5f) * contentHeight;
        // Keep the root cap inside the body for every rotation.
        float insetX = Mathf.Min(halfWidth, bubble.width * 0.49f);
        float insetY = Mathf.Min(halfWidth, bubble.height * 0.49f);
        rootPoint = new Vector2(
            Mathf.Clamp(rootPoint.x, bubble.xMin + insetX, bubble.xMax - insetX),
            Mathf.Clamp(rootPoint.y, bubble.yMin + insetY, bubble.yMax - insetY));
        distance = Vector2.Distance(rootPoint, targetLocal);
        float finalAlong = Mathf.Sqrt(Mathf.Max(1f, distance * distance - tipOffset * tipOffset));
        angle = (Mathf.Atan2(targetLocal.y - rootPoint.y, targetLocal.x - rootPoint.x) -
            Mathf.Atan2(tipOffset, finalAlong)) * Mathf.Rad2Deg;
        visualWidth = finalAlong / tipX;

        tailRect.anchorMin =
            tailRect.anchorMax =
                bubbleRect.pivot;

        tailRect.pivot =
            new Vector2(
                0f,
                0.5f);

        tailRect.anchoredPosition =
            rootPoint - (Vector2)(Quaternion.Euler(0f, 0f, angle) * new Vector3(0f, rootOffset, 0f));

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

    internal bool TryGetComicTip(RectTransform space, out Vector2 tip, out float attachment, out Vector2 size)
    {
        tip = Vector2.zero;
        size = Vector2.zero;
        attachment = comicSideAttachment;
        if (!isActiveAndEnabled || targetPivot == null || space == null || tailRect == null || style == null) return false;
        Rect rect = tailRect.rect;
        tip = space.InverseTransformPoint(tailRect.TransformPoint(new Vector2(
            Mathf.Lerp(rect.xMin, rect.xMax, 0.985f), rect.center.y)));
        Vector2 renderSize = BattleSpeechBubbleTailTextureBuilder.GetRenderSize(style.uiSize);
        size.x = space.InverseTransformVector(bubbleRect.TransformVector(Vector3.right * renderSize.x)).magnitude;
        size.y = space.InverseTransformVector(bubbleRect.TransformVector(Vector3.up *
            (renderSize.y / BattleSpeechBubbleTailTextureBuilder.VerticalDisplayScale * 0.92f))).magnitude;
        if (exactTargetMode)
            tip = space.InverseTransformPoint(targetPivot.TransformPoint(targetPivot.rect.center));
        else
        {
            Rect frame = GetAttachmentRect();
            Vector2 hostTip = new(frame.xMax + comicHostTailLength,
                Mathf.Lerp(frame.yMin, frame.yMax, comicSideAttachment));
            tip = space.InverseTransformPoint(bubbleRect.TransformPoint(hostTip));
        }
        return true;
    }

    private void PlaceComicTopTarget(BattleSpeechBubbleTailStyle activeStyle, Rect bubble, Vector2 target)
    {
        Vector2 renderSize = BattleSpeechBubbleTailTextureBuilder.GetRenderSize(activeStyle.uiSize);
        float contentHeight = renderSize.y / BattleSpeechBubbleTailTextureBuilder.VerticalDisplayScale;
        float halfWidth = contentHeight * activeStyle.rootHalfWidth;
        float inset = Mathf.Min(halfWidth, bubble.height * 0.35f);
        Vector2 root = new(target.x >= bubble.center.x ? bubble.xMax - inset : bubble.xMin + inset,
            Mathf.Lerp(bubble.yMin, bubble.yMax, comicSideAttachment));
        Vector2 delta = target - root;
        tailRect.anchorMin = tailRect.anchorMax = bubbleRect.pivot;
        tailRect.pivot = new Vector2(0f, 0.5f);
        tailRect.anchoredPosition = root;
        tailRect.sizeDelta = new Vector2(delta.magnitude / activeStyle.tip.x, renderSize.y);
        tailRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        tailRect.localScale = Vector3.one;
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

        float contentHeight = size.y / BattleSpeechBubbleTailTextureBuilder.VerticalDisplayScale;
        padding = Mathf.Min(bubble.height * 0.49f, Mathf.Max(padding,
            rootJoinHalfHeight));

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

        float rootX = right
            ? bubble.xMax - Mathf.Max(0f, activeStyle.overlap)
            : bubble.xMin + Mathf.Max(0f, activeStyle.overlap);
        float halfWidth = rootJoinHalfHeight;
        if (frameController != null)
        {
            halfWidth += contentHeight / BattleSpeechBubbleTailTextureBuilder.ContentHeight;
            bool fits = frameController.TryGetFillVerticalSpan(bubbleRect, rootX, out float minY, out float maxY)
                && maxY - minY >= halfWidth * 2f;
            if (!fits && frameController.TryGetFillVerticalSpan(bubbleRect, bubble.center.x,
                    out float centerMinY, out float centerMaxY) && centerMaxY - centerMinY >= halfWidth * 2f)
            {
                float outsideX = rootX;
                float insideX = bubble.center.x;
                for (int i = 0; i < 8; i++)
                {
                    float candidateX = (outsideX + insideX) * 0.5f;
                    if (frameController.TryGetFillVerticalSpan(bubbleRect, candidateX,
                            out float candidateMinY, out float candidateMaxY) &&
                        candidateMaxY - candidateMinY >= halfWidth * 2f)
                        insideX = candidateX;
                    else outsideX = candidateX;
                }
                rootX = insideX;
                fits = frameController.TryGetFillVerticalSpan(bubbleRect, rootX, out minY, out maxY);
            }
            if (fits)
            {
                y = Mathf.Clamp(y, minY + halfWidth, maxY - halfWidth);
                outside = right ? rootX - bubble.xMax + size.x * 0.5f
                    : bubble.xMin - rootX + size.x * 0.5f;
            }
        }

        tailRect.anchoredPosition =
            new Vector2(
                right
                    ? bubble.xMax + outside
                    : bubble.xMin - outside,
                y - (Mathf.Clamp01(activeStyle.rootY) - 0.5f) * contentHeight);

        tailRect.localRotation =
            Quaternion.identity;

        tailRect.localScale =
            right
                ? Vector3.one
                : new Vector3(-1f, 1f, 1f);
    }

    private Rect GetAttachmentRect()
    {
        RectTransform frame = frameController != null ? frameController.FrameRect : null;
        if (frame == null) return bubbleRect.rect;
        if (BattleUiStyleController.IsComic && frameController.TryGetFillCorners(comicFrameCorners))
        {
            Vector2 fillMin = bubbleRect.InverseTransformPoint(frame.TransformPoint(comicFrameCorners[0]));
            Vector2 fillMax = fillMin;
            for (int i = 1; i < comicFrameCorners.Length; i++)
            {
                Vector2 point = bubbleRect.InverseTransformPoint(frame.TransformPoint(comicFrameCorners[i]));
                fillMin = Vector2.Min(fillMin, point);
                fillMax = Vector2.Max(fillMax, point);
            }
            return Rect.MinMaxRect(fillMin.x, fillMin.y, fillMax.x, fillMax.y);
        }
        frame.GetWorldCorners(frameCorners);
        Vector2 min = bubbleRect.InverseTransformPoint(frameCorners[0]);
        Vector2 max = min;
        for (int i = 1; i < frameCorners.Length; i++)
        {
            Vector2 point = bubbleRect.InverseTransformPoint(frameCorners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private void SyncFillPlacement()
    {
        fillRect.anchorMin = tailRect.anchorMin;
        fillRect.anchorMax = tailRect.anchorMax;
        fillRect.pivot = tailRect.pivot;
        fillRect.sizeDelta = tailRect.sizeDelta;
        fillRect.anchoredPosition3D = tailRect.anchoredPosition3D;
        fillRect.localRotation = tailRect.localRotation;
        fillRect.localScale = tailRect.localScale;
        RectTransform frame = frameController != null ? frameController.FrameRect : null;
        if (BattleUiStyleController.IsComic && frameController != null &&
            frameController.TryGetFillCorners(comicFrameCorners))
        {
            outlineImage.enabled = false;
            fillImage.enabled = false;
            frameController.RefreshComicOutline();
        }
        if (frame != null && frame.parent == fillRect.parent &&
            fillRect.GetSiblingIndex() != frame.GetSiblingIndex() + 1)
            fillRect.SetSiblingIndex(frame.GetSiblingIndex() + 1);
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

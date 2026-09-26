using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 말풍선 FRAME을 OUTLINE / FILL 두 UI Image로 분리해 렌더링합니다.
///
/// OUTLINE은 흰 Alpha Mask Texture + style.outlineColor Tint를 사용하고
/// 별도의 Stroke Material을 적용할 수 있습니다.
/// FILL은 별도 Mask Texture + style.fillColor Tint를 사용하며
/// Stroke Material의 영향을 받지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class BattleSpeechBubbleFrameFillController : MonoBehaviour
{
    private Image outlineImage;
    private Image fillImage;
    private RectTransform targetRect;

    private BattleSpeechBubbleFrameStyle style;
    private Vector2 runtimeSize;
    private Material strokeMaterial;

    private Texture2D outlineTexture;
    private Texture2D fillTexture;
    private Sprite outlineSprite;
    private Sprite fillSprite;
    private int appliedHash = int.MinValue;

    public void Configure(
        Image image,
        BattleSpeechBubbleFrameStyle frameStyle,
        Vector2 fixedRuntimeSize,
        Material outlineMaterial = null)
    {
        outlineImage =
            image != null
                ? image
                : outlineImage != null
                    ? outlineImage
                    : GetComponent<Image>();

        targetRect =
            outlineImage != null
                ? outlineImage.rectTransform
                : GetComponent<RectTransform>();

        style =
            frameStyle;

        runtimeSize =
            fixedRuntimeSize;

        strokeMaterial =
            outlineMaterial;

        EnsureFillImage();
        Refresh(force: true);
    }

    private void Awake()
    {
        if (outlineImage == null)
            outlineImage = GetComponent<Image>();

        if (targetRect == null &&
            outlineImage != null)
        {
            targetRect =
                outlineImage.rectTransform;
        }

        EnsureFillImage();
    }

    private void LateUpdate()
    {
        Refresh(force: false);
    }

    private void OnDestroy()
    {
        ReleaseRuntimeAssets();
    }

    private void EnsureFillImage()
    {
        if (fillImage != null)
            return;

        Transform existing =
            transform.Find(
                "BubbleFrameFill");

        GameObject fillObject;

        if (existing != null)
        {
            fillObject =
                existing.gameObject;
        }
        else
        {
            fillObject =
                new GameObject(
                    "BubbleFrameFill",
                    typeof(RectTransform));

            fillObject.transform.SetParent(
                transform,
                false);
        }

        RectTransform fillRect =
            fillObject.GetComponent<RectTransform>();

        fillRect.anchorMin =
            Vector2.zero;

        fillRect.anchorMax =
            Vector2.one;

        fillRect.pivot =
            new Vector2(
                0.5f,
                0.5f);

        fillRect.offsetMin =
            Vector2.zero;

        fillRect.offsetMax =
            Vector2.zero;

        fillImage =
            fillObject.GetComponent<Image>();

        if (fillImage == null)
            fillImage = fillObject.AddComponent<Image>();

        fillImage.raycastTarget = false;
        fillImage.type = Image.Type.Simple;
        fillImage.preserveAspect = false;

        // OUTLINE parent Image 위에 FILL을 덮습니다.
        fillObject.transform.SetAsLastSibling();
    }

    private void Refresh(bool force)
    {
        if (outlineImage == null ||
            fillImage == null ||
            targetRect == null ||
            style == null)
        {
            return;
        }

        style.EnsureCornerPointDefaults();

        int hash =
            style.ComputeHash();

        unchecked
        {
            hash =
                hash * 31 +
                runtimeSize.GetHashCode();

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

            ApplyImageStyle();
            return;
        }

        appliedHash =
            hash;

        ReleaseRuntimeAssets();

        outlineTexture =
            BattleSpeechBubbleFrameTextureBuilder.BuildOutlineMaskTexture(
                style,
                runtimeSize,
                "BattleSpeechBubbleFrame_OutlineMask_Runtime",
                out Rect localBounds);

        fillTexture =
            BattleSpeechBubbleFrameTextureBuilder.BuildFillMaskTexture(
                style,
                runtimeSize,
                "BattleSpeechBubbleFrame_FillMask_Runtime",
                out _);

        outlineSprite =
            CreateRuntimeSprite(
                outlineTexture,
                "BattleSpeechBubbleFrame_Outline_RuntimeSprite");

        fillSprite =
            CreateRuntimeSprite(
                fillTexture,
                "BattleSpeechBubbleFrame_Fill_RuntimeSprite");

        // localBounds는 Root 좌하단을 (0,0)으로 한 좌표입니다.
        targetRect.anchorMin =
            targetRect.anchorMax =
                new Vector2(
                    0.5f,
                    0.5f);

        targetRect.pivot =
            new Vector2(
                0.5f,
                0.5f);

        targetRect.sizeDelta =
            localBounds.size;

        targetRect.anchoredPosition =
            localBounds.center -
            runtimeSize * 0.5f;

        outlineImage.sprite =
            outlineSprite;

        fillImage.sprite =
            fillSprite;

        ApplyImageStyle();
    }

    private void ApplyImageStyle()
    {
        if (outlineImage != null)
        {
            outlineImage.type =
                Image.Type.Simple;

            outlineImage.preserveAspect =
                false;

            outlineImage.raycastTarget =
                false;

            outlineImage.color =
                style != null
                    ? style.outlineColor
                    : Color.black;

            outlineImage.material =
                strokeMaterial;
        }

        if (fillImage != null)
        {
            fillImage.type =
                Image.Type.Simple;

            fillImage.preserveAspect =
                false;

            fillImage.raycastTarget =
                false;

            fillImage.color =
                style != null
                    ? style.fillColor
                    : Color.white;

            // Fill은 Stroke용 Hologram Material에서 완전히 분리합니다.
            fillImage.material =
                null;
        }
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
                new Vector2(
                    0.5f,
                    0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);

        sprite.name =
            spriteName;

        sprite.hideFlags =
            HideFlags.HideAndDontSave;

        return sprite;
    }

    private void ReleaseRuntimeAssets()
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

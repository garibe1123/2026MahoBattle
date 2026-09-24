using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 말풍선 FRAME 전체(검은 OUTLINE + 흰 INNER)를 하나의 런타임 Sprite로 렌더링합니다.
///
/// Custom Graphic/OnPopulateMesh는 사용하지 않습니다.
/// BattleSpeechBubbleFrameStyle의 빨강 OUTLINE 4점과 초록 INNER 4점을
/// BattleSpeechBubbleFrameTextureBuilder가 동일하게 Rasterize합니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class BattleSpeechBubbleFrameFillController : MonoBehaviour
{
    private Image targetImage;
    private RectTransform targetRect;
    private BattleSpeechBubbleFrameStyle style;
    private Vector2 runtimeSize;

    private Texture2D runtimeTexture;
    private Sprite runtimeSprite;
    private int appliedHash = int.MinValue;

    public void Configure(
        Image image,
        BattleSpeechBubbleFrameStyle frameStyle,
        Vector2 fixedRuntimeSize)
    {
        targetImage =
            image != null
                ? image
                : GetComponent<Image>();

        targetRect =
            targetImage != null
                ? targetImage.rectTransform
                : GetComponent<RectTransform>();

        style =
            frameStyle;

        runtimeSize =
            fixedRuntimeSize;

        Refresh(force: true);
    }

    private void Awake()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();

        if (targetRect == null &&
            targetImage != null)
        {
            targetRect =
                targetImage.rectTransform;
        }
    }

    private void LateUpdate()
    {
        Refresh(force: false);
    }

    private void OnDestroy()
    {
        ReleaseRuntimeAssets();
    }

    private void Refresh(bool force)
    {
        if (targetImage == null ||
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
        }

        if (!force &&
            hash == appliedHash &&
            runtimeSprite != null)
        {
            if (targetImage.sprite != runtimeSprite)
                targetImage.sprite = runtimeSprite;

            return;
        }

        appliedHash =
            hash;

        ReleaseRuntimeAssets();

        runtimeTexture =
            BattleSpeechBubbleFrameTextureBuilder.BuildFrameTexture(
                style,
                runtimeSize,
                "BattleSpeechBubbleFrame_Runtime",
                out Rect localBounds);

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
            "BattleSpeechBubbleFrame_RuntimeSprite";

        runtimeSprite.hideFlags =
            HideFlags.HideAndDontSave;

        // localBounds는 Root의 좌하단을 (0,0)으로 한 좌표입니다.
        // Child anchor를 중앙에 두고, Bounds 중심이 Root 중심에서 얼마나
        // 이동했는지만 anchoredPosition에 반영합니다.
        targetRect.anchorMin =
            targetRect.anchorMax =
                new Vector2(0.5f, 0.5f);

        targetRect.pivot =
            new Vector2(0.5f, 0.5f);

        targetRect.sizeDelta =
            localBounds.size;

        targetRect.anchoredPosition =
            localBounds.center -
            runtimeSize * 0.5f;

        targetImage.sprite =
            runtimeSprite;

        targetImage.type =
            Image.Type.Simple;

        targetImage.preserveAspect =
            false;

        targetImage.color =
            Color.white;

        targetImage.raycastTarget =
            false;
    }

    private void ReleaseRuntimeAssets()
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

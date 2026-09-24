using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Corner Stroke 기반 흰 Fill 사변형을 일반 UI Image로 렌더링합니다.
/// Custom Graphic/OnPopulateMesh를 사용하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class BattleSpeechBubbleFrameFillController : MonoBehaviour
{
    private Image targetImage;
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
        targetImage = image != null
            ? image
            : GetComponent<Image>();

        style = frameStyle;
        runtimeSize = fixedRuntimeSize;

        Refresh(force: true);
    }

    private void Awake()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();
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
            style == null)
        {
            return;
        }

        style.EnsureCornerStrokeDefaults();

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

        appliedHash = hash;

        ReleaseRuntimeAssets();

        runtimeTexture =
            BattleSpeechBubbleFrameTextureBuilder.BuildFillTexture(
                style,
                runtimeSize,
                "BattleSpeechBubbleFrameFill_Runtime");

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
            "BattleSpeechBubbleFrameFill_RuntimeSprite";

        runtimeSprite.hideFlags =
            HideFlags.HideAndDontSave;

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

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Static Sprite 기반의 짧은 만화식 말풍선 꼬리 컨트롤러.
///
/// - 꼬리 모양을 절차적으로 생성하지 않습니다.
/// - Target Pivot은 Up / Mid / Down 및 좌/우 방향 선택에만 사용합니다.
/// - 기본 Sprite는 Resources/BattleShow/SpeechTails의 흰색 PNG 3종을 사용합니다.
/// - BattleShowPresentationManager의 Override Sprite가 있으면 그것을 우선 사용합니다.
/// - 왼쪽 방향은 오른쪽 Sprite를 X Flip해서 재사용합니다.
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

    private const string DefaultUpPath =
        "BattleShow/SpeechTails/comic_tail_right_up";

    private const string DefaultMidPath =
        "BattleShow/SpeechTails/comic_tail_right_mid";

    private const string DefaultDownPath =
        "BattleShow/SpeechTails/comic_tail_right_down";

    private static Sprite cachedDefaultUp;
    private static Sprite cachedDefaultMid;
    private static Sprite cachedDefaultDown;

    [Header("References")]
    [SerializeField] private RectTransform bubbleRect;
    [SerializeField] private RectTransform targetPivot;

    [Header("Optional Sprite Override")]
    [SerializeField] private Sprite rightUpSprite;
    [SerializeField] private Sprite rightMidSprite;
    [SerializeField] private Sprite rightDownSprite;

    [Header("Layout")]
    [Tooltip("꼬리 자체는 짧게 유지합니다. 사회자까지 늘어나지 않습니다.")]
    [SerializeField] private Vector2 tailSize = new(118f, 68f);

    [Tooltip("말풍선 본체 안쪽으로 꼬리 Root를 겹치는 양입니다. 흰 꼬리가 기존 검은 Edge를 덮어 자연스럽게 이어집니다.")]
    [SerializeField, Range(0f, 40f)] private float edgeInset = 28f;

    [Tooltip("Target이 말풍선 중심보다 이 값 이상 위/아래에 있을 때 Up / Down 프리셋을 선택합니다.")]
    [SerializeField, Min(1f)] private float verticalThreshold = 72f;

    [Header("Anchor")]
    [SerializeField, Range(0.55f, 0.95f)] private float upAnchorY = 0.78f;
    [SerializeField, Range(0.25f, 0.75f)] private float midAnchorY = 0.50f;
    [SerializeField, Range(0.05f, 0.45f)] private float downAnchorY = 0.22f;

    private Image tailImage;
    private RectTransform tailRect;
    private bool warnedMissingSprites;

    public void Configure(
        RectTransform bubble,
        RectTransform target,
        SpeechBubbleTailSpriteSet overrides = null)
    {
        bubbleRect = bubble;
        targetPivot = target;

        if (overrides != null)
        {
            if (overrides.rightUp != null)
                rightUpSprite = overrides.rightUp;

            if (overrides.rightMid != null)
                rightMidSprite = overrides.rightMid;

            if (overrides.rightDown != null)
                rightDownSprite = overrides.rightDown;
        }

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

        GameObject tail =
            new("TailImage", typeof(RectTransform));

        tail.transform.SetParent(transform, false);

        tailRect = tail.GetComponent<RectTransform>();
        tailRect.anchorMin =
            tailRect.anchorMax =
                new Vector2(1f, 0.5f);

        tailRect.pivot =
            new Vector2(0f, 0.5f);

        tailRect.sizeDelta = tailSize;

        tailImage = tail.AddComponent<Image>();
        tailImage.type = Image.Type.Simple;
        tailImage.preserveAspect = true;
        tailImage.raycastTarget = false;
        tailImage.color = Color.white;

        // 이 컴포넌트는 BubbleFace 생성 뒤 붙이는 것을 전제로 합니다.
        // 흰 Tail Root가 검은 Bubble Edge를 살짝 덮어 이음새를 감춥니다.
        tail.transform.SetAsLastSibling();
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

        Vector3 localDelta =
            bubbleRect.InverseTransformVector(
                targetPivot.position -
                bubbleCenterWorld);

        bool targetOnRight =
            localDelta.x >= 0f;

        VerticalPreset vertical;
        if (localDelta.y > verticalThreshold)
            vertical = VerticalPreset.Up;
        else if (localDelta.y < -verticalThreshold)
            vertical = VerticalPreset.Down;
        else
            vertical = VerticalPreset.Mid;

        Sprite sprite =
            ResolveSprite(vertical);

        if (sprite == null)
        {
            tailImage.enabled = false;

            if (!warnedMissingSprites)
            {
                warnedMissingSprites = true;
                Debug.LogWarning(
                    "[BattleSpeechBubbleTailPresetController] 말풍선 꼬리 Sprite를 찾지 못했습니다. " +
                    "Resources/BattleShow/SpeechTails 기본 PNG 또는 BattleShowPresentationManager Override를 확인하세요.",
                    this);
            }

            return;
        }

        tailImage.enabled = true;
        tailImage.sprite = sprite;
        tailImage.color = Color.white;
        tailImage.preserveAspect = true;

        ApplyLayout(
            targetOnRight,
            vertical);
    }

    private void ApplyLayout(
        bool targetOnRight,
        VerticalPreset vertical)
    {
        float anchorY = vertical switch
        {
            VerticalPreset.Up => upAnchorY,
            VerticalPreset.Down => downAnchorY,
            _ => midAnchorY
        };

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
        }

        tailRect.localRotation =
            Quaternion.identity;

        tailRect.sizeDelta =
            tailSize;
    }

    private Sprite ResolveSprite(
        VerticalPreset vertical)
    {
        switch (vertical)
        {
            case VerticalPreset.Up:
                if (rightUpSprite != null)
                    return rightUpSprite;

                if (cachedDefaultUp == null)
                {
                    cachedDefaultUp =
                        Resources.Load<Sprite>(
                            DefaultUpPath);
                }

                return cachedDefaultUp;

            case VerticalPreset.Down:
                if (rightDownSprite != null)
                    return rightDownSprite;

                if (cachedDefaultDown == null)
                {
                    cachedDefaultDown =
                        Resources.Load<Sprite>(
                            DefaultDownPath);
                }

                return cachedDefaultDown;

            default:
                if (rightMidSprite != null)
                    return rightMidSprite;

                if (cachedDefaultMid == null)
                {
                    cachedDefaultMid =
                        Resources.Load<Sprite>(
                            DefaultMidPath);
                }

                return cachedDefaultMid;
        }
    }
}

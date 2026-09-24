using System;
using UnityEngine;

/// <summary>
/// 말풍선 본체의 "형태"만 저장하는 스타일.
///
/// 실제 창 Size와 Pop Scale은 선택씬/전투씬 컨트롤러의 고정 디자인 값과
/// CanvasScaler가 담당합니다. 이 스타일은 화면 비율에 따라 바뀌면 안 되는
/// 외곽선 형태와 색만 조절합니다.
///
/// Outline Outset = 검은 면이 Root 바깥으로 나가는 양.
/// Stroke Thickness = Root 경계를 기준으로 보이는 최종 검은 선의 총 두께.
/// 내부 Fill Inset은 Stroke - Outset으로 자동 계산합니다.
/// </summary>
[Serializable]
public sealed class BattleSpeechBubbleFrameStyle
{
    [Header("Frame Shape")]
    [Tooltip("말풍선 전체 회전 각도입니다. 창 Size/Scale은 고정값을 사용합니다.")]
    [Range(-12f, 12f)]
    public float rotation = -1.5f;

    [Header("Outline Outset")]
    [Tooltip("검은 Outline이 Root 왼쪽 바깥으로 나가는 양입니다.")]
    [Range(0f, 48f)]
    public float outlineLeft = 8f;

    [Tooltip("검은 Outline이 Root 아래쪽 바깥으로 나가는 양입니다.")]
    [Range(0f, 48f)]
    public float outlineBottom = 9f;

    [Tooltip("검은 Outline이 Root 오른쪽 바깥으로 나가는 양입니다.")]
    [Range(0f, 48f)]
    public float outlineRight = 8f;

    [Tooltip("검은 Outline이 Root 위쪽 바깥으로 나가는 양입니다.")]
    [Range(0f, 48f)]
    public float outlineTop = 9f;

    [Header("Stroke Thickness")]
    [Tooltip("왼쪽에 최종적으로 보이는 검은 Stroke의 총 두께입니다.")]
    [Range(0f, 64f)]
    public float strokeLeft = 10f;

    [Tooltip("아래쪽에 최종적으로 보이는 검은 Stroke의 총 두께입니다.")]
    [Range(0f, 64f)]
    public float strokeBottom = 11f;

    [Tooltip("오른쪽에 최종적으로 보이는 검은 Stroke의 총 두께입니다.")]
    [Range(0f, 64f)]
    public float strokeRight = 10f;

    [Tooltip("위쪽에 최종적으로 보이는 검은 Stroke의 총 두께입니다.")]
    [Range(0f, 64f)]
    public float strokeTop = 11f;

    [Header("Color")]
    public Color outlineColor = new(0.012f, 0.012f, 0.018f, 0.99f);
    public Color fillColor = new(0.97f, 0.97f, 0.94f, 1f);

    public float FillInsetLeft =>
        Mathf.Max(0f, strokeLeft - outlineLeft);

    public float FillInsetBottom =>
        Mathf.Max(0f, strokeBottom - outlineBottom);

    public float FillInsetRight =>
        Mathf.Max(0f, strokeRight - outlineRight);

    public float FillInsetTop =>
        Mathf.Max(0f, strokeTop - outlineTop);

    public static BattleSpeechBubbleFrameStyle CreateSelectionDefault()
    {
        return new BattleSpeechBubbleFrameStyle
        {
            rotation = -1.5f,
            outlineLeft = 8f,
            outlineBottom = 9f,
            outlineRight = 8f,
            outlineTop = 9f,
            strokeLeft = 10f,
            strokeBottom = 11f,
            strokeRight = 10f,
            strokeTop = 11f
        };
    }

    public static BattleSpeechBubbleFrameStyle CreateCombatDefault()
    {
        return new BattleSpeechBubbleFrameStyle
        {
            rotation = -2.5f,
            outlineLeft = 7f,
            outlineBottom = 8f,
            outlineRight = 7f,
            outlineTop = 8f,
            strokeLeft = 9f,
            strokeBottom = 10f,
            strokeRight = 9f,
            strokeTop = 10f
        };
    }

    public int ComputeHash()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + rotation.GetHashCode();
            hash = hash * 31 + outlineLeft.GetHashCode();
            hash = hash * 31 + outlineBottom.GetHashCode();
            hash = hash * 31 + outlineRight.GetHashCode();
            hash = hash * 31 + outlineTop.GetHashCode();
            hash = hash * 31 + strokeLeft.GetHashCode();
            hash = hash * 31 + strokeBottom.GetHashCode();
            hash = hash * 31 + strokeRight.GetHashCode();
            hash = hash * 31 + strokeTop.GetHashCode();
            hash = hash * 31 + outlineColor.GetHashCode();
            hash = hash * 31 + fillColor.GetHashCode();
            return hash;
        }
    }
}

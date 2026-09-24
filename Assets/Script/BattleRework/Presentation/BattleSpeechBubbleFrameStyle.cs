using System;
using UnityEngine;

/// <summary>
/// 말풍선 본체(네모 프레임)의 크기/외곽/애니메이션 스케일 스타일.
///
/// Root Rect 자체의 Size를 기준으로:
/// - Outline Outset: 검은 배경이 Root 밖으로 얼마나 더 나갈지
/// - Face Inset: 흰 Fill이 Root 안쪽으로 얼마나 줄어들지
/// 를 각각 좌/하/우/상 방향으로 독립 조절합니다.
///
/// 따라서 한쪽만 두껍게 하거나, 위/아래/좌/우의 Stroke 체감을 서로 다르게 만들 수 있습니다.
/// </summary>
[Serializable]
public sealed class BattleSpeechBubbleFrameStyle
{
    [Header("Frame Size")]
    [Tooltip("말풍선 Root의 기준 크기입니다.")]
    public Vector2 size = new(1500f, 180f);

    [Tooltip("말풍선 전체 회전 각도입니다.")]
    [Range(-12f, 12f)]
    public float rotation = -1.5f;

    [Header("Open / Close Scale")]
    [Tooltip("말풍선이 처음 나타날 때의 시작 Scale입니다.")]
    [Range(0.40f, 1.20f)]
    public float startScale = 0.88f;

    [Tooltip("Pop 애니메이션 중 잠깐 커지는 Scale입니다.")]
    [Range(0.80f, 1.40f)]
    public float overshootScale = 1.04f;

    [Tooltip("대사가 표시되는 동안 유지되는 최종 Scale입니다.")]
    [Range(0.60f, 1.30f)]
    public float settledScale = 1f;

    [Tooltip("닫힐 때 마지막으로 줄어드는 Scale입니다. 전투 리액션 Shutdown에서 사용합니다.")]
    [Range(0.40f, 1.20f)]
    public float shutdownScale = 0.92f;

    [Header("Outline Outset")]
    [Tooltip("검은 Outline이 Root 왼쪽 바깥으로 나가는 크기입니다.")]
    [Range(0f, 48f)]
    public float outlineLeft = 8f;

    [Tooltip("검은 Outline이 Root 아래쪽 바깥으로 나가는 크기입니다.")]
    [Range(0f, 48f)]
    public float outlineBottom = 9f;

    [Tooltip("검은 Outline이 Root 오른쪽 바깥으로 나가는 크기입니다.")]
    [Range(0f, 48f)]
    public float outlineRight = 8f;

    [Tooltip("검은 Outline이 Root 위쪽 바깥으로 나가는 크기입니다.")]
    [Range(0f, 48f)]
    public float outlineTop = 9f;

    [Header("Fill Shrink / Inner Inset")]
    [Tooltip("흰 Fill을 Root 왼쪽에서 안쪽으로 줄이는 크기입니다.")]
    [Range(0f, 32f)]
    public float fillInsetLeft = 2f;

    [Tooltip("흰 Fill을 Root 아래쪽에서 안쪽으로 줄이는 크기입니다.")]
    [Range(0f, 32f)]
    public float fillInsetBottom = 2f;

    [Tooltip("흰 Fill을 Root 오른쪽에서 안쪽으로 줄이는 크기입니다.")]
    [Range(0f, 32f)]
    public float fillInsetRight = 2f;

    [Tooltip("흰 Fill을 Root 위쪽에서 안쪽으로 줄이는 크기입니다.")]
    [Range(0f, 32f)]
    public float fillInsetTop = 2f;

    [Header("Color")]
    public Color outlineColor = new(0.012f, 0.012f, 0.018f, 0.99f);
    public Color fillColor = new(0.97f, 0.97f, 0.94f, 1f);

    public static BattleSpeechBubbleFrameStyle CreateSelectionDefault()
    {
        return new BattleSpeechBubbleFrameStyle
        {
            size = new Vector2(1500f, 180f),
            rotation = -1.5f,
            startScale = 0.88f,
            overshootScale = 1.04f,
            settledScale = 1f,
            shutdownScale = 0.92f,
            outlineLeft = 8f,
            outlineBottom = 9f,
            outlineRight = 8f,
            outlineTop = 9f,
            fillInsetLeft = 2f,
            fillInsetBottom = 2f,
            fillInsetRight = 2f,
            fillInsetTop = 2f
        };
    }

    public static BattleSpeechBubbleFrameStyle CreateCombatDefault()
    {
        return new BattleSpeechBubbleFrameStyle
        {
            size = new Vector2(390f, 150f),
            rotation = -2.5f,
            startScale = 0.86f,
            overshootScale = 1.055f,
            settledScale = 1f,
            shutdownScale = 0.92f,
            outlineLeft = 7f,
            outlineBottom = 8f,
            outlineRight = 7f,
            outlineTop = 8f,
            fillInsetLeft = 2f,
            fillInsetBottom = 2f,
            fillInsetRight = 2f,
            fillInsetTop = 2f
        };
    }

    public int ComputeHash()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + size.GetHashCode();
            hash = hash * 31 + rotation.GetHashCode();
            hash = hash * 31 + startScale.GetHashCode();
            hash = hash * 31 + overshootScale.GetHashCode();
            hash = hash * 31 + settledScale.GetHashCode();
            hash = hash * 31 + shutdownScale.GetHashCode();
            hash = hash * 31 + outlineLeft.GetHashCode();
            hash = hash * 31 + outlineBottom.GetHashCode();
            hash = hash * 31 + outlineRight.GetHashCode();
            hash = hash * 31 + outlineTop.GetHashCode();
            hash = hash * 31 + fillInsetLeft.GetHashCode();
            hash = hash * 31 + fillInsetBottom.GetHashCode();
            hash = hash * 31 + fillInsetRight.GetHashCode();
            hash = hash * 31 + fillInsetTop.GetHashCode();
            hash = hash * 31 + outlineColor.GetHashCode();
            hash = hash * 31 + fillColor.GetHashCode();
            return hash;
        }
    }
}

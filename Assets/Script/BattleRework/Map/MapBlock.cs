using System;
using DG.Tweening;
using UnityEngine;

public enum MapBlockEntryType
{
    WheelSlide,
    CeilingDrop,
    RiseFromFloor,
    Static
}

/// <summary>
/// 2x2 유닛(기본 128x128px) 맵 블록 하나를 표현합니다.
/// 방(Room)은 이 블록 여러 개를 격자로 조립해서 구성합니다.
///
/// Entry는 단순 이동이 아니라 접근 -> 충돌 -> 반동 -> Grid Snap 순서로 처리합니다.
/// 충돌 순간은 Impacted 이벤트로 BattleRoomManager에 전달되어 VFX/Camera Shake를 재생합니다.
/// </summary>
public class MapBlock : MonoBehaviour
{
    public const float UnitWorldSize = 1f;
    public static readonly Vector2 BlockWorldSize = new(2f, 2f);

    [Header("Entry")]
    [SerializeField] private MapBlockEntryType entryType = MapBlockEntryType.WheelSlide;
    [SerializeField, Min(0f)] private float entryDuration = 0.7f;
    [SerializeField, Min(0f)] private float entryOffset = 8f;
    [SerializeField] private Ease entryEase = Ease.InQuad;

    [Header("Heavy Approach Presentation")]
    [Tooltip("맵 본체 Collider와 분리된 시각 Root. 비어 있으면 자식 SpriteRenderer를 자동 탐색합니다.")]
    [SerializeField] private Transform presentationRoot;
    [Tooltip("WheelSlide에서 실제 바퀴 파츠가 있다면 지정. null이면 회전 연출을 생략합니다.")]
    [SerializeField] private Transform wheelRoot;
    [SerializeField, Min(0f)] private float approachRumbleDegrees = 1.5f;
    [SerializeField, Range(1, 40)] private int approachRumbleVibrato = 12;
    [SerializeField] private float wheelSpinDegreesPerWorldUnit = 180f;

    [Header("Impact")]
    [SerializeField, Min(0f)] private float impactReboundDistance = 0.10f;
    [SerializeField, Min(0.01f)] private float impactSettleDuration = 0.14f;
    [SerializeField, Min(0f)] private float impactPunchScale = 0.08f;
    [SerializeField, Min(0f)] private float impactStrength = 1f;

    [Header("Exit")]
    [SerializeField, Min(0f)] private float exitDuration = 0.6f;
    [SerializeField] private Ease exitEase = Ease.InQuad;

    private Quaternion presentationBaseRotation;
    private Vector3 presentationBaseScale = Vector3.one;
    private Vector3 wheelBaseEuler;

    public MapBlockEntryType EntryType => entryType;
    public bool WillImpact => entryType != MapBlockEntryType.Static;
    public float EntryDuration => entryType == MapBlockEntryType.Static
        ? 0f
        : entryDuration + impactSettleDuration;
    public float ExitDuration => exitDuration;

    /// <summary>
    /// block, impactPosition, travelDirection, impactStrength
    /// </summary>
    public event Action<MapBlock, Vector3, Vector2, float> Impacted;

    private void Awake()
    {
        ResolvePresentationRoot();
        CachePresentationPose();
    }

    private void OnDisable()
    {
        KillTweens();
    }

    private void ResolvePresentationRoot()
    {
        if (presentationRoot != null)
            return;

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].transform != transform)
            {
                presentationRoot = renderers[i].transform;
                break;
            }
        }
    }

    private void CachePresentationPose()
    {
        if (presentationRoot != null)
        {
            presentationBaseRotation = presentationRoot.localRotation;
            presentationBaseScale = presentationRoot.localScale;
        }

        if (wheelRoot != null)
            wheelBaseEuler = wheelRoot.localEulerAngles;
    }

    private void RestorePresentationPose()
    {
        if (presentationRoot != null)
        {
            presentationRoot.localRotation = presentationBaseRotation;
            presentationRoot.localScale = presentationBaseScale;
        }
    }

    private void KillTweens()
    {
        transform.DOKill();
        if (presentationRoot != null)
            presentationRoot.DOKill();
        if (wheelRoot != null && wheelRoot != presentationRoot)
            wheelRoot.DOKill();
    }

    public void SnapTo(Vector3 worldPosition)
    {
        KillTweens();
        transform.position = worldPosition;
        RestorePresentationPose();
    }

    public float GetEntryDuration(float delay = 0f)
    {
        return Mathf.Max(0f, delay) + EntryDuration;
    }

    public Tween PlayEnter(Vector3 destination, Vector2 preferredDirection, float delay = 0f)
    {
        KillTweens();
        RestorePresentationPose();

        if (entryType == MapBlockEntryType.Static)
        {
            transform.position = destination;
            return transform.DOMove(destination, 0f);
        }

        Vector3 start = destination;
        Vector2 sourceDirection;
        Vector2 preferred = preferredDirection.sqrMagnitude > 0.001f
            ? preferredDirection.normalized
            : Vector2.right;

        switch (entryType)
        {
            case MapBlockEntryType.CeilingDrop:
                start += Vector3.up * entryOffset;
                sourceDirection = Vector2.up;
                break;

            case MapBlockEntryType.RiseFromFloor:
                start += Vector3.down * entryOffset;
                sourceDirection = Vector2.down;
                break;

            default:
                start += (Vector3)(preferred * entryOffset);
                sourceDirection = preferred;
                break;
        }

        Vector2 travelDirection = ((Vector2)destination - (Vector2)start).normalized;
        if (travelDirection.sqrMagnitude <= 0.001f)
            travelDirection = -sourceDirection;

        transform.position = start;

        float safeDelay = Mathf.Max(0f, delay);
        float approachTime = Mathf.Max(0.01f, entryDuration);
        float settleTime = Mathf.Max(0.01f, impactSettleDuration);

        if (presentationRoot != null && approachRumbleDegrees > 0f)
        {
            presentationRoot
                .DOShakeRotation(
                    approachTime,
                    new Vector3(0f, 0f, approachRumbleDegrees),
                    Mathf.Max(1, approachRumbleVibrato),
                    35f,
                    false)
                .SetDelay(safeDelay)
                .SetEase(Ease.Linear);
        }

        if (entryType == MapBlockEntryType.WheelSlide && wheelRoot != null)
        {
            float distance = Vector2.Distance(start, destination);
            float sign = travelDirection.x >= 0f ? -1f : 1f;
            Vector3 targetEuler = wheelBaseEuler +
                                  new Vector3(0f, 0f, distance * wheelSpinDegreesPerWorldUnit * sign);

            wheelRoot
                .DOLocalRotate(targetEuler, approachTime, RotateMode.FastBeyond360)
                .SetDelay(safeDelay)
                .SetEase(Ease.Linear);
        }

        Sequence sequence = DOTween.Sequence();
        if (safeDelay > 0f)
            sequence.AppendInterval(safeDelay);

        sequence.Append(transform.DOMove(destination, approachTime).SetEase(entryEase));
        sequence.AppendCallback(() =>
        {
            transform.position = destination;

            if (presentationRoot != null && impactPunchScale > 0f)
            {
                presentationRoot.DOKill();
                presentationRoot.localRotation = presentationBaseRotation;
                presentationRoot.localScale = presentationBaseScale;
                presentationRoot.DOPunchScale(
                    Vector3.one * impactPunchScale,
                    settleTime,
                    5,
                    0.45f);
            }

            Impacted?.Invoke(this, destination, travelDirection, Mathf.Max(0f, impactStrength));
        });

        if (impactReboundDistance > 0f)
        {
            Vector3 rebound = destination - (Vector3)(travelDirection * impactReboundDistance);
            sequence.Append(
                transform.DOMove(rebound, settleTime * 0.35f)
                    .SetEase(Ease.OutQuad));
        }

        sequence.Append(
            transform.DOMove(destination, settleTime * 0.65f)
                .SetEase(Ease.OutBack));

        sequence.OnComplete(() =>
        {
            // NavMesh/Collider 기준 좌표에 미세 Tween 오차가 남지 않도록 강제 Snap.
            transform.position = destination;
            RestorePresentationPose();
        });

        return sequence;
    }

    public Tween PlayExit(Vector2 direction)
    {
        KillTweens();
        RestorePresentationPose();

        Vector2 dir = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
        Vector3 destination = transform.position + (Vector3)(dir * entryOffset);
        return transform.DOMove(destination, exitDuration).SetEase(exitEase);
    }
}

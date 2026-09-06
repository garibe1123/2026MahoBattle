using System;
using DG.Tweening;
using NavMeshPlus.Components;
using UnityEngine;

public enum MapBlockEntryType
{
    WheelSlide,
    CeilingDrop,
    RiseFromFloor,
    Static
}

/// <summary>
/// 2x2 world unit Extension MapBlock.
/// Persistent Start Base 바깥에 붙는 추가 Block만 이 컴포넌트의 진입/도킹 연출을 사용합니다.
///
/// Entry:
/// approach -> contact face impact -> 짧은 directional compression -> 미세 rebound -> exact grid snap.
/// 충돌 VFX 자체는 BattleRoomManager가 접촉면 기준으로 재생합니다.
/// </summary>
public class MapBlock : MonoBehaviour
{
    public const float UnitWorldSize = 1f;
    public static readonly Vector2 BlockWorldSize = new(2f, 2f);

    [Header("Entry")]
    [SerializeField] private MapBlockEntryType entryType = MapBlockEntryType.WheelSlide;
    [SerializeField, Min(0f)] private float entryDuration = 0.62f;
    [SerializeField, Min(0f)] private float entryOffset = 8f;
    [SerializeField] private Ease entryEase = Ease.InCubic;

    [Header("Navigation Surface")]
    [Tooltip("Extension Block의 대표 바닥 SpriteRenderer를 NavMeshPlus Walkable Source로 사용합니다. 벽/장식 전용 블록이면 끄세요.")]
    [SerializeField] private bool contributesWalkableNavMesh = true;
    [Tooltip("직접 지정하지 않으면 presentationRoot의 SpriteRenderer에 NavMeshModifier를 자동 보강합니다.")]
    [SerializeField] private NavMeshModifier walkableNavModifier;

    [Header("Approach Presentation")]
    [Tooltip("맵 본체 Collider와 분리된 시각 Root. 비어 있으면 자식 SpriteRenderer를 자동 탐색합니다.")]
    [SerializeField] private Transform presentationRoot;
    [Tooltip("WheelSlide에서 실제 바퀴 파츠가 있다면 지정. null이면 회전 연출을 생략합니다.")]
    [SerializeField] private Transform wheelRoot;
    [SerializeField, Min(0f)] private float approachRumbleDegrees = 0.55f;
    [SerializeField, Range(1, 40)] private int approachRumbleVibrato = 8;
    [SerializeField] private float wheelSpinDegreesPerWorldUnit = 180f;

    [Header("Docking Impact")]
    [SerializeField, Min(0f)] private float impactReboundDistance = 0.055f;
    [SerializeField, Min(0.01f)] private float impactSettleDuration = 0.11f;
    [Tooltip("Punch 확대가 아니라 진행축을 눌러주는 압축량입니다. 0.04면 약 4% 이내의 짧은 압축만 발생합니다.")]
    [SerializeField, Range(0f, 0.15f)] private float impactPunchScale = 0.045f;
    [SerializeField, Min(0f)] private float impactStrength = 1f;
    [Tooltip("충돌 VFX를 블록 외곽선보다 아주 조금 안쪽에 배치하는 거리입니다.")]
    [SerializeField, Min(0f)] private float impactFaceInset = 0.02f;

    [Header("Exit")]
    [SerializeField, Min(0f)] private float exitDuration = 0.6f;
    [SerializeField] private Ease exitEase = Ease.InQuad;

    private Quaternion presentationBaseRotation;
    private Vector3 presentationBaseScale = Vector3.one;
    private Vector3 wheelBaseEuler;

    public MapBlockEntryType EntryType => entryType;
    public bool WillImpact => entryType != MapBlockEntryType.Static;
    public bool ContributesWalkableNavMesh => contributesWalkableNavMesh;
    public float EntryDuration => entryType == MapBlockEntryType.Static
        ? 0f
        : entryDuration + impactSettleDuration;
    public float ExitDuration => exitDuration;

    /// <summary>
    /// block, contactFacePosition, travelDirection, impactStrength
    /// </summary>
    public event Action<MapBlock, Vector3, Vector2, float> Impacted;

    private void Awake()
    {
        ResolvePresentationRoot();
        EnsureWalkableNavMeshSource();
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

        if (presentationRoot == null)
        {
            SpriteRenderer ownRenderer = GetComponent<SpriteRenderer>();
            if (ownRenderer != null)
                presentationRoot = ownRenderer.transform;
        }
    }

    private void EnsureWalkableNavMeshSource()
    {
        if (!contributesWalkableNavMesh)
            return;

        if (walkableNavModifier != null)
            return;

        if (presentationRoot != null)
            walkableNavModifier = presentationRoot.GetComponent<NavMeshModifier>();

        if (walkableNavModifier == null)
            walkableNavModifier = GetComponentInChildren<NavMeshModifier>(true);

        if (walkableNavModifier != null)
            return;

        SpriteRenderer sourceRenderer = presentationRoot != null
            ? presentationRoot.GetComponent<SpriteRenderer>()
            : null;

        if (sourceRenderer == null)
            sourceRenderer = GetComponentInChildren<SpriteRenderer>(true);

        if (sourceRenderer == null)
        {
            Debug.LogWarning(
                $"[MapBlock] '{name}' contributesWalkableNavMesh is enabled but no SpriteRenderer was found. " +
                "Assign a floor presentationRoot or disable the option for non-walkable blocks.",
                this);
            return;
        }

        walkableNavModifier = sourceRenderer.GetComponent<NavMeshModifier>();
        if (walkableNavModifier == null)
            walkableNavModifier = sourceRenderer.gameObject.AddComponent<NavMeshModifier>();

        walkableNavModifier.ignoreFromBuild = false;
        walkableNavModifier.overrideArea = false;
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

    public Vector3 GetImpactContactPoint(Vector3 destination, Vector2 travelDirection)
    {
        Vector2 direction = travelDirection.sqrMagnitude > 0.001f
            ? travelDirection.normalized
            : Vector2.down;

        float halfWidth = BlockWorldSize.x * 0.5f;
        float halfHeight = BlockWorldSize.y * 0.5f;
        float supportDistance =
            Mathf.Abs(direction.x) * halfWidth +
            Mathf.Abs(direction.y) * halfHeight;

        supportDistance = Mathf.Max(0f, supportDistance - impactFaceInset);
        return destination + (Vector3)(direction * supportDistance);
    }

    public float GetImpactFaceLength(Vector2 travelDirection)
    {
        Vector2 direction = travelDirection.sqrMagnitude > 0.001f
            ? travelDirection.normalized
            : Vector2.down;

        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? BlockWorldSize.y
            : BlockWorldSize.x;
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
                    20f,
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
            AnimateDirectionalCompression(travelDirection, settleTime);

            Vector3 contactPoint = GetImpactContactPoint(destination, travelDirection);
            Impacted?.Invoke(this, contactPoint, travelDirection, Mathf.Max(0f, impactStrength));
        });

        if (impactReboundDistance > 0f)
        {
            Vector3 rebound = destination - (Vector3)(travelDirection * impactReboundDistance);
            sequence.Append(
                transform.DOMove(rebound, settleTime * 0.30f)
                    .SetEase(Ease.OutQuad));
        }

        sequence.Append(
            transform.DOMove(destination, settleTime * 0.70f)
                .SetEase(Ease.OutCubic));

        sequence.OnComplete(() =>
        {
            transform.position = destination;
            RestorePresentationPose();
        });

        return sequence;
    }

    private void AnimateDirectionalCompression(Vector2 travelDirection, float settleTime)
    {
        if (presentationRoot == null || impactPunchScale <= 0f)
            return;

        presentationRoot.DOKill();
        presentationRoot.localRotation = presentationBaseRotation;

        Vector3 compressed = presentationBaseScale;
        float compression = Mathf.Clamp(impactPunchScale, 0f, 0.15f);
        float counterStretch = compression * 0.16f;

        if (Mathf.Abs(travelDirection.x) >= Mathf.Abs(travelDirection.y))
        {
            compressed.x *= 1f - compression;
            compressed.y *= 1f + counterStretch;
        }
        else
        {
            compressed.y *= 1f - compression;
            compressed.x *= 1f + counterStretch;
        }

        presentationRoot.localScale = compressed;
        presentationRoot
            .DOScale(presentationBaseScale, Mathf.Max(0.03f, settleTime * 0.82f))
            .SetEase(Ease.OutCubic);
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

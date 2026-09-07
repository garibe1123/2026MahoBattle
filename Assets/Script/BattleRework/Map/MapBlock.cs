using System;
using System.Collections.Generic;
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
/// Runtime Map / Corridor / Large Room Piece의 도킹 연출을 담당합니다.
/// contributesWalkableNavMesh가 켜진 Block은 실제 Walkable NavMesh Renderer만
/// BattleWalkableField로 등록하여 Player가 '실제로 존재하는 바닥' 위에서만 이동하게 합니다.
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
    [Tooltip("실제로 플레이어/몬스터가 설 수 있는 바닥이면 켭니다. 벽/장식 전용 블록이면 끕니다.")]
    [SerializeField] private bool contributesWalkableNavMesh = true;
    [SerializeField] private NavMeshModifier walkableNavModifier;

    [Header("Approach Presentation")]
    [SerializeField] private Transform presentationRoot;
    [SerializeField] private Transform wheelRoot;
    [SerializeField, Min(0f)] private float approachRumbleDegrees = 0.55f;
    [SerializeField, Range(1, 40)] private int approachRumbleVibrato = 8;
    [SerializeField] private float wheelSpinDegreesPerWorldUnit = 180f;

    [Header("Docking Impact")]
    [SerializeField, Min(0f)] private float impactReboundDistance = 0.055f;
    [SerializeField, Min(0.01f)] private float impactSettleDuration = 0.11f;
    [SerializeField, Range(0f, 0.15f)] private float impactPunchScale = 0.045f;
    [SerializeField, Min(0f)] private float impactStrength = 1f;
    [SerializeField, Min(0f)] private float impactFaceInset = 0.02f;

    [Header("Exit")]
    [SerializeField, Min(0f)] private float exitDuration = 0.6f;
    [SerializeField] private Ease exitEase = Ease.InQuad;

    private Quaternion presentationBaseRotation;
    private Vector3 presentationBaseScale = Vector3.one;
    private Vector3 wheelBaseEuler;
    private Vector3 entryDestination;
    private bool hasEntryDestination;

    public MapBlockEntryType EntryType => entryType;
    public bool WillImpact => entryType != MapBlockEntryType.Static;
    public bool ContributesWalkableNavMesh => contributesWalkableNavMesh;
    public float EntryDuration => entryType == MapBlockEntryType.Static ? 0f : entryDuration + impactSettleDuration;
    public float ExitDuration => exitDuration;
    public bool HasEntryDestination => hasEntryDestination;
    public Vector3 EntryDestination => hasEntryDestination ? entryDestination : transform.position;

    public event Action<MapBlock, Vector3, Vector2, float> Impacted;

    private void Awake()
    {
        ResolvePresentationRoot();
        EnsureWalkableNavMeshSource();
        CachePresentationPose();
    }

    private void OnEnable()
    {
        // Prefab 복제/Pool 재활성화에서도 Field 등록을 보장합니다.
        if (contributesWalkableNavMesh)
            EnsureWalkableNavMeshSource();
    }

    private void OnDisable()
    {
        KillTweens();
    }

    public void ConfigureRuntimeDockingBlock(
        Transform visualRoot,
        bool walkable,
        float strength = 0.6f,
        float duration = 0.50f,
        float offset = 5.5f)
    {
        presentationRoot = visualRoot != null ? visualRoot : transform;
        contributesWalkableNavMesh = walkable;
        entryType = MapBlockEntryType.WheelSlide;
        entryDuration = Mathf.Max(0.05f, duration);
        entryOffset = Mathf.Max(0f, offset);
        impactStrength = Mathf.Max(0f, strength);
        approachRumbleDegrees = Mathf.Min(approachRumbleDegrees, 0.35f);
        impactReboundDistance = Mathf.Min(impactReboundDistance, 0.04f);
        impactPunchScale = Mathf.Min(impactPunchScale, 0.035f);
        ResolvePresentationRoot();
        EnsureWalkableNavMeshSource();
        CachePresentationPose();
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

    /// <summary>
    /// NavMeshModifier overrideArea=false인 Renderer만 실제 바닥으로 간주합니다.
    /// Large Room의 외곽 Wall은 overrideArea=true(Not Walkable)이므로 Player Field에 등록되지 않습니다.
    /// </summary>
    private void EnsureWalkableNavMeshSource()
    {
        if (!contributesWalkableNavMesh)
            return;

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        bool foundWalkableRenderer = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null)
                continue;

            NavMeshModifier modifier = renderer.GetComponent<NavMeshModifier>();
            if (modifier == null)
                continue;

            if (modifier.ignoreFromBuild || modifier.overrideArea)
                continue;

            if (walkableNavModifier == null)
                walkableNavModifier = modifier;

            BattleWalkableField.Ensure(renderer);
            foundWalkableRenderer = true;
        }

        if (foundWalkableRenderer)
            return;

        SpriteRenderer sourceRenderer = presentationRoot != null
            ? presentationRoot.GetComponent<SpriteRenderer>()
            : null;

        if (sourceRenderer == null)
            sourceRenderer = GetComponentInChildren<SpriteRenderer>(true);

        if (sourceRenderer == null)
        {
            Debug.LogWarning(
                $"[MapBlock] '{name}' is walkable but no SpriteRenderer was found. Player movement will not be allowed on this block.",
                this);
            return;
        }

        walkableNavModifier = sourceRenderer.GetComponent<NavMeshModifier>();
        if (walkableNavModifier == null)
            walkableNavModifier = sourceRenderer.gameObject.AddComponent<NavMeshModifier>();

        walkableNavModifier.ignoreFromBuild = false;
        walkableNavModifier.overrideArea = false;
        BattleWalkableField.Ensure(sourceRenderer);
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
        entryDestination = worldPosition;
        hasEntryDestination = true;
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

        if (TryGetPresentationBounds(out Bounds bounds))
        {
            float supportDistance =
                Mathf.Abs(direction.x) * bounds.extents.x +
                Mathf.Abs(direction.y) * bounds.extents.y;
            supportDistance = Mathf.Max(0f, supportDistance - impactFaceInset);
            return bounds.center + (Vector3)(direction * supportDistance);
        }

        float fallbackDistance =
            Mathf.Abs(direction.x) * BlockWorldSize.x * 0.5f +
            Mathf.Abs(direction.y) * BlockWorldSize.y * 0.5f;
        fallbackDistance = Mathf.Max(0f, fallbackDistance - impactFaceInset);
        return destination + (Vector3)(direction * fallbackDistance);
    }

    public float GetImpactFaceLength(Vector2 travelDirection)
    {
        Vector2 direction = travelDirection.sqrMagnitude > 0.001f
            ? travelDirection.normalized
            : Vector2.down;

        if (TryGetPresentationBounds(out Bounds bounds))
        {
            return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
                ? bounds.size.y
                : bounds.size.x;
        }

        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? BlockWorldSize.y
            : BlockWorldSize.x;
    }

    private bool TryGetPresentationBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        Renderer[] renderers = presentationRoot != null
            ? presentationRoot.GetComponentsInChildren<Renderer>(true)
            : GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (found)
            return true;

        Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.enabled)
                continue;

            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return found;
    }

    public Tween PlayEnter(Vector3 destination, Vector2 preferredDirection, float delay = 0f)
    {
        KillTweens();
        RestorePresentationPose();
        entryDestination = destination;
        hasEntryDestination = true;

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

/// <summary>
/// Player 이동 허용 여부를 결정하는 실제 바닥 Marker입니다.
/// Trigger Collider는 물리 충돌용이 아니라 '여기에 실제 Field가 존재하는가' 판정 전용입니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleWalkableField : MonoBehaviour
{
    private static readonly HashSet<BattleWalkableField> ActiveFields = new();

    [SerializeField] private Collider2D supportCollider;

    public static int ActiveCount => ActiveFields.Count;

    private void OnEnable()
    {
        ActiveFields.Add(this);
    }

    private void OnDisable()
    {
        ActiveFields.Remove(this);
    }

    public bool Contains(Vector2 worldPoint)
    {
        return isActiveAndEnabled &&
               supportCollider != null &&
               supportCollider.enabled &&
               supportCollider.OverlapPoint(worldPoint);
    }

    public static BattleWalkableField Ensure(SpriteRenderer renderer)
    {
        if (renderer == null || renderer.sprite == null)
            return null;

        BattleWalkableField marker = renderer.GetComponent<BattleWalkableField>();
        if (marker == null)
            marker = renderer.gameObject.AddComponent<BattleWalkableField>();

        if (marker.supportCollider == null)
        {
            BoxCollider2D support = renderer.gameObject.AddComponent<BoxCollider2D>();
            support.isTrigger = true;
            support.usedByEffector = false;

            Vector2 localSize = renderer.drawMode == SpriteDrawMode.Simple
                ? (Vector2)renderer.sprite.bounds.size
                : renderer.size;

            support.size = new Vector2(
                Mathf.Max(0.02f, localSize.x),
                Mathf.Max(0.02f, localSize.y));
            marker.supportCollider = support;
        }

        ActiveFields.Add(marker);
        return marker;
    }

    public static bool HasSupport(Vector2 worldPoint)
    {
        if (ActiveFields.Count == 0)
            return false;

        // 복사본을 만들지 않고 직접 순회해 GC를 피합니다.
        foreach (BattleWalkableField field in ActiveFields)
        {
            if (field != null && field.Contains(worldPoint))
                return true;
        }

        return false;
    }
}

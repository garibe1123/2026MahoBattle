using System;
using System.Reflection;
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
/// 2x2 유닛(기본 128x128px) 맵 블록 하나를 표현합니다.
/// 방(Room)은 이 블록 여러 개를 격자로 조립해서 구성합니다.
///
/// Entry는 접근 -> 충돌 -> 반동 -> Grid Snap 순서로 처리합니다.
/// 충돌 VFX 기준점은 블록 중심이 아니라 실제 진행 방향의 앞면(Contact Face)입니다.
/// 기본 fallback VFX는 원형이 아니라 충돌 면을 따라 생기는 얇은 이음새/먼지 형태를 사용합니다.
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

    [Header("Navigation Surface")]
    [Tooltip("이 블록의 대표 바닥 SpriteRenderer를 NavMeshPlus Walkable Source로 사용합니다. 벽/장식 전용 블록이면 끄세요.")]
    [SerializeField] private bool contributesWalkableNavMesh = true;
    [Tooltip("직접 지정하지 않으면 presentationRoot의 SpriteRenderer에 NavMeshModifier를 자동 보강합니다.")]
    [SerializeField] private NavMeshModifier walkableNavModifier;

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
    [Tooltip("충돌 VFX를 블록 외곽선보다 아주 조금 안쪽에 배치하는 거리입니다.")]
    [SerializeField, Min(0f)] private float impactFaceInset = 0.025f;

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
        MapBlockFaceImpactFallback.EnsureInstalled();
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

    /// <summary>
    /// NavMeshPlus 2D는 NavMeshModifier가 붙은 Renderer/Collider를 소스로 수집합니다.
    /// 테스트용/단순 MapBlock이 SpriteRenderer만 가지고 있어도 플레이 시 바닥 NavMesh가 누락되지 않도록
    /// 대표 바닥 Renderer에 Modifier를 자동 보강합니다.
    /// </summary>
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

        // 축 정렬 2x2 블록을 direction 방향으로 투영했을 때의 외곽까지 거리.
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

            Vector3 contactPoint = GetImpactContactPoint(destination, travelDirection);
            Impacted?.Invoke(this, contactPoint, travelDirection, Mathf.Max(0f, impactStrength));
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

/// <summary>
/// BattleRoomManager에 실제 Impact Sprite가 없을 때만 설치되는 코드 기반 fallback입니다.
/// 기존 원형 Ring/Flash dummy를 사용하지 않고, 2 world 길이의 세로형 접촉선 Sprite를 넣습니다.
/// BattleRoomManager는 travelDirection만큼 회전하므로 세로 Sprite가 자동으로 충돌 면의 접선 방향에 정렬됩니다.
/// </summary>
internal static class MapBlockFaceImpactFallback
{
    private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;

    private static Sprite[] impactFrames;
    private static Sprite[] dustFrames;
    private static int configuredManagerId;

    public static void EnsureInstalled()
    {
        BattleRoomManager manager = UnityEngine.Object.FindFirstObjectByType<BattleRoomManager>();
        if (manager == null)
            return;

        int managerId = manager.GetInstanceID();
        if (configuredManagerId == managerId)
            return;

        FieldInfo impactField = typeof(BattleRoomManager).GetField("impactSprites", FieldFlags);
        FieldInfo dustField = typeof(BattleRoomManager).GetField("dustSprites", FieldFlags);
        FieldInfo sortingField = typeof(BattleRoomManager).GetField("impactVfxSortingOrder", FieldFlags);

        if (impactField == null || dustField == null)
            return;

        Sprite[] currentImpact = impactField.GetValue(manager) as Sprite[];
        Sprite[] currentDust = dustField.GetValue(manager) as Sprite[];
        bool needsImpact = currentImpact == null || currentImpact.Length == 0;
        bool needsDust = currentDust == null || currentDust.Length == 0;

        if (!needsImpact && !needsDust)
        {
            configuredManagerId = managerId;
            return;
        }

        EnsureFrames();

        if (needsImpact)
            impactField.SetValue(manager, impactFrames);
        if (needsDust)
            dustField.SetValue(manager, dustFrames);

        // 완전 fallback 상태일 때만 Tile 아래쪽으로 보냅니다.
        // 실제 Sprite를 하나라도 사용자가 지정했다면 사용자의 Sorting 설정을 보존합니다.
        if (needsImpact && needsDust && sortingField != null)
        {
            object current = sortingField.GetValue(manager);
            if (current is int order && order == 90)
                sortingField.SetValue(manager, -20);
        }

        configuredManagerId = managerId;
    }

    private static void EnsureFrames()
    {
        if (impactFrames == null || impactFrames.Length == 0)
        {
            impactFrames = new Sprite[4];
            for (int i = 0; i < impactFrames.Length; i++)
                impactFrames[i] = CreateImpactFrame(i, impactFrames.Length);
        }

        if (dustFrames == null || dustFrames.Length == 0)
        {
            dustFrames = new Sprite[5];
            for (int i = 0; i < dustFrames.Length; i++)
                dustFrames[i] = CreateDustFrame(i, dustFrames.Length);
        }
    }

    private static Sprite CreateImpactFrame(int frame, int frameCount)
    {
        const int width = 12;
        const int height = 32;
        const float ppu = 16f;

        Texture2D texture = CreateTexture(width, height, $"MapImpactFace_{frame}");
        float life = 1f - frame / (float)Mathf.Max(1, frameCount);
        int center = width / 2;

        for (int y = 2; y < height - 2; y++)
        {
            int jitter = ((y * 17 + frame * 7) % 5) - 2;
            int x = center + Mathf.RoundToInt(jitter * 0.35f);
            int thickness = frame <= 1 ? 1 : 0;

            for (int dx = -thickness; dx <= thickness; dx++)
            {
                int px = Mathf.Clamp(x + dx, 0, width - 1);
                float alpha = Mathf.Clamp01((0.95f - Mathf.Abs(y - height * 0.5f) / height * 0.25f) * life);
                texture.SetPixel(px, y, new Color(1f, 0.92f, 0.72f, alpha));
            }
        }

        // 충돌 이음새 바깥으로 아주 짧게 튀는 파편. 원형으로 퍼지지 않습니다.
        int shardCount = Mathf.Max(1, 4 - frame);
        for (int i = 0; i < shardCount; i++)
        {
            int y = 5 + ((i * 7 + frame * 3) % 22);
            int side = i % 2 == 0 ? -1 : 1;
            int x = Mathf.Clamp(center + side * (2 + frame), 0, width - 1);
            texture.SetPixel(x, y, new Color(1f, 0.82f, 0.48f, Mathf.Clamp01(life)));
        }

        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), ppu);
    }

    private static Sprite CreateDustFrame(int frame, int frameCount)
    {
        const int width = 16;
        const int height = 32;
        const float ppu = 16f;

        Texture2D texture = CreateTexture(width, height, $"MapImpactDustFace_{frame}");
        int center = width / 2;
        float life = 1f - frame / (float)Mathf.Max(1, frameCount);
        int spread = 1 + frame;

        for (int i = 0; i < 9; i++)
        {
            int seed = i * 19 + frame * 11;
            int y = 3 + (seed % 26);
            int side = i % 2 == 0 ? -1 : 1;
            int x = Mathf.Clamp(center + side * (1 + (seed % Mathf.Max(1, spread + 1))), 0, width - 1);

            Color color = new(0.70f, 0.67f, 0.60f, Mathf.Clamp01(0.80f * life));
            texture.SetPixel(x, y, color);
            if (frame <= 1 && x + side >= 0 && x + side < width)
                texture.SetPixel(x + side, y, color);
        }

        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), ppu);
    }

    private static Texture2D CreateTexture(int width, int height, string textureName)
    {
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false)
        {
            name = textureName,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] clear = new Color[width * height];
        texture.SetPixels(clear);
        return texture;
    }
}

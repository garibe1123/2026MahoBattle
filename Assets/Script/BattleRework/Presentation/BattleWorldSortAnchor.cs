using UnityEngine;
using UnityEngine.Rendering;

public enum BattleWorldSortAnchorMode
{
    SpriteBottom,
    TransformOrigin,
    ManualLocalY
}

/// <summary>
/// Player / Monster / Theme Object 등 움직이는 World Sprite의 공용 Y-Sort 컴포넌트입니다.
/// SpriteBottom 모드는 활성화 시점의 Sprite 하단을 한 번만 Anchor로 캡처하므로
/// 애니메이션 프레임마다 Sprite Bounds가 달라져도 Sorting Order가 흔들리지 않습니다.
/// </summary>
[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public sealed class BattleWorldSortAnchor : MonoBehaviour
{
    [Header("WORLD SORT TARGET")]
    [SerializeField] private SpriteRenderer referenceRenderer;
    [SerializeField] private SortingGroup sortingGroup;

    [Header("ANCHOR")]
    [SerializeField] private BattleWorldSortAnchorMode anchorMode = BattleWorldSortAnchorMode.SpriteBottom;
    [Tooltip("Root Transform 기준 local Y입니다. SpriteBottom 모드에서는 활성화 시 자동으로 갱신됩니다.")]
    [SerializeField] private float anchorLocalY;
    [Tooltip("같은 발 위치에서 앞/뒤 우선순위를 미세 조정할 때만 사용합니다.")]
    [SerializeField] private int localSortOffset;

    private int lastAppliedOrder = int.MinValue;
    private bool spriteBottomCaptured;

    public float AnchorLocalY => anchorLocalY;
    public int LocalSortOffset => localSortOffset;
    public int CurrentSortingOrder => lastAppliedOrder;
    public float CurrentAnchorWorldY => ResolveAnchorWorldY();

    public static BattleWorldSortAnchor Ensure(GameObject target)
    {
        if (target == null)
            return null;

        BattleWorldSortAnchor existing = target.GetComponent<BattleWorldSortAnchor>();
        return existing != null ? existing : target.AddComponent<BattleWorldSortAnchor>();
    }

    private void Awake()
    {
        ResolveTargets();
    }

    private void OnEnable()
    {
        ResolveTargets();
        CaptureSpriteBottomIfNeeded(true);
        ApplySorting(true);
    }

    private void LateUpdate()
    {
        ApplySorting(false);
    }

    private void OnValidate()
    {
        ResolveTargets();

        if (!Application.isPlaying && anchorMode == BattleWorldSortAnchorMode.SpriteBottom)
            CaptureSpriteBottomIfNeeded(true);
    }

    [ContextMenu("Capture Sort Anchor From Sprite Bottom")]
    public void CaptureAnchorFromSpriteBottom()
    {
        ResolveTargets();
        CaptureSpriteBottomIfNeeded(true);
        ApplySorting(true);
    }

    public void SetManualAnchorLocalY(float localY)
    {
        anchorMode = BattleWorldSortAnchorMode.ManualLocalY;
        anchorLocalY = localY;
        spriteBottomCaptured = true;
        ApplySorting(true);
    }

    public void SetLocalSortOffset(int offset)
    {
        localSortOffset = offset;
        ApplySorting(true);
    }

    public void ApplySortingNow()
    {
        ApplySorting(true);
    }

    private void ResolveTargets()
    {
        if (sortingGroup == null)
            sortingGroup = GetComponent<SortingGroup>();

        if (referenceRenderer == null)
            referenceRenderer = GetComponent<SpriteRenderer>();

        if (referenceRenderer == null)
            referenceRenderer = GetComponentInChildren<SpriteRenderer>(true);
    }

    private void CaptureSpriteBottomIfNeeded(bool force)
    {
        if (anchorMode != BattleWorldSortAnchorMode.SpriteBottom)
            return;
        if (!force && spriteBottomCaptured)
            return;
        if (referenceRenderer == null || referenceRenderer.sprite == null)
            return;

        Bounds bounds = referenceRenderer.bounds;
        Vector3 worldBottom = new(bounds.center.x, bounds.min.y, bounds.center.z);
        anchorLocalY = transform.InverseTransformPoint(worldBottom).y;
        spriteBottomCaptured = true;
    }

    private float ResolveAnchorWorldY()
    {
        if (anchorMode == BattleWorldSortAnchorMode.TransformOrigin)
            return transform.position.y;

        Vector3 localAnchor = new(0f, anchorLocalY, 0f);
        return transform.TransformPoint(localAnchor).y;
    }

    private void ApplySorting(bool force)
    {
        ResolveTargets();
        CaptureSpriteBottomIfNeeded(false);

        if (sortingGroup == null && referenceRenderer == null)
            return;

        int order = BattleWorldSorting.WorldYToOrder(ResolveAnchorWorldY(), localSortOffset);
        if (!force && order == lastAppliedOrder)
            return;

        if (sortingGroup != null)
            sortingGroup.sortingOrder = order;
        else
            referenceRenderer.sortingOrder = order;

        lastAppliedOrder = order;
    }
}

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전투 UI의 공통 2.5D Surface.
///
/// 기존 UI Owner가 Position / Rotation / Scale을 계속 소유하도록 두고,
/// 이 컴포넌트는 Late spatial pass에서 "추가" X/Y Tilt, Z Depth, Canvas Sorting만 얹습니다.
/// 다음 프레임 시작 전에 Coordinator가 RestoreOwnerPose()를 호출해 추가값을 제거하므로
/// 기존 UI 애니메이션과 Transform 소유권이 충돌하지 않습니다.
///
/// 사용 위치:
/// - GridBoard / CombatRulePanel / MissionPanel / ChatPanel 같은 "메인 판" RectTransform
/// - 개별 Icon/Text가 아니라 그들을 감싸는 Parent 공간에 부착
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleUiSpatialSurface : MonoBehaviour
{
    public enum SurfaceRole
    {
        Pack,
        Rules,
        Mission,
        Chat,
        CombatCompact,
        MiniPack,
        Metric,
        Overlay
    }

    [Header("Identity")]
    [SerializeField] private SurfaceRole role;
    [SerializeField] private float baseDepth;
    [SerializeField, Range(0f, 2f)] private float tiltMultiplier = 1f;

    [Header("Runtime Canvas")]
    [SerializeField] private bool spatialSorting = true;
    [SerializeField] private bool preserveRaycasts = true;

    private RectTransform rect;
    private Canvas spatialCanvas;
    private GraphicRaycaster spatialRaycaster;

    private bool canvasWasAdded;
    private bool raycasterWasAdded;
    private bool originalOverrideSorting;
    private int originalSortingOrder;
    private bool originalCanvasStateCaptured;

    private Quaternion ownerRotation;
    private float ownerZ;
    private bool ownerPoseCaptured;
    private bool spatialPoseApplied;

    private Vector2 currentTilt;
    private float currentZOffset;
    private int currentSortingOrder;

    public SurfaceRole Role => role;
    public float BaseDepth => baseDepth;
    public float TiltMultiplier => Mathf.Max(0f, tiltMultiplier);
    public RectTransform Rect => rect != null ? rect : rect = transform as RectTransform;
    public bool SpatialSorting => spatialSorting;

    public bool IsRenderable
    {
        get
        {
            RectTransform target = Rect;
            if (target == null ||
                !target.gameObject.activeInHierarchy)
            {
                return false;
            }

            CanvasGroup group = target.GetComponent<CanvasGroup>();
            return group == null ||
                   group.alpha > 0.015f;
        }
    }

    public void Configure(
        SurfaceRole nextRole,
        float nextBaseDepth,
        float nextTiltMultiplier,
        bool enableSorting,
        bool keepRaycasts)
    {
        role = nextRole;
        baseDepth = nextBaseDepth;
        tiltMultiplier = Mathf.Max(0f, nextTiltMultiplier);
        spatialSorting = enableSorting;
        preserveRaycasts = keepRaycasts;

        rect = transform as RectTransform;

        if (spatialSorting)
            EnsureSpatialCanvas();
    }

    /// <summary>
    /// 지난 LateUpdate에서 얹었던 공간 효과만 제거합니다.
    /// 기존 Owner가 이번 Update에서 자신의 원래 애니메이션 계산을 할 수 있게 만드는 단계입니다.
    /// </summary>
    public void RestoreOwnerPose()
    {
        RectTransform target = Rect;
        if (target == null ||
            !spatialPoseApplied ||
            !ownerPoseCaptured)
        {
            return;
        }

        target.localRotation =
            ownerRotation;

        Vector3 local =
            target.localPosition;

        local.z =
            ownerZ;

        target.localPosition =
            local;

        spatialPoseApplied =
            false;

        ownerPoseCaptured =
            false;
    }

    /// <summary>
    /// 기존 UI Owner가 계산을 끝낸 뒤 호출합니다.
    /// Owner Rotation/Z를 캡처하고 커서 Tilt만 추가합니다.
    /// </summary>
    public void BeginSpatialPass(
        Vector2 targetTilt,
        float sharpness,
        float deltaTime)
    {
        RectTransform target = Rect;
        if (target == null ||
            !target.gameObject.activeInHierarchy)
        {
            return;
        }

        ownerRotation =
            target.localRotation;

        ownerZ =
            target.localPosition.z;

        ownerPoseCaptured =
            true;

        float blend =
            1f -
            Mathf.Exp(
                -Mathf.Max(
                    0.1f,
                    sharpness) *
                Mathf.Max(
                    0f,
                    deltaTime));

        currentTilt =
            Vector2.Lerp(
                currentTilt,
                targetTilt,
                blend);

        target.localRotation =
            ownerRotation *
            Quaternion.Euler(
                currentTilt.x,
                currentTilt.y,
                0f);

        spatialPoseApplied =
            true;
    }

    /// <summary>
    /// Tilt가 적용된 Screen Bounds를 검사한 뒤 최종 Z / Sorting을 적용합니다.
    /// </summary>
    public void ApplyDepth(
        float targetZOffset,
        int sortingOrder,
        float sharpness,
        float deltaTime)
    {
        RectTransform target = Rect;
        if (target == null ||
            !ownerPoseCaptured)
        {
            return;
        }

        float blend =
            1f -
            Mathf.Exp(
                -Mathf.Max(
                    0.1f,
                    sharpness) *
                Mathf.Max(
                    0f,
                    deltaTime));

        currentZOffset =
            Mathf.Lerp(
                currentZOffset,
                targetZOffset,
                blend);

        Vector3 local =
            target.localPosition;

        local.z =
            ownerZ +
            currentZOffset;

        target.localPosition =
            local;

        if (!spatialSorting)
            return;

        EnsureSpatialCanvas();

        if (spatialCanvas == null)
            return;

        currentSortingOrder =
            sortingOrder;

        spatialCanvas.overrideSorting =
            true;

        spatialCanvas.sortingOrder =
            currentSortingOrder;
    }

    public Rect GetScreenRect()
    {
        RectTransform target = Rect;
        if (target == null)
            return default;

        Vector3[] corners =
            new Vector3[4];

        target.GetWorldCorners(
            corners);

        Canvas canvas =
            target.GetComponentInParent<Canvas>();

        Camera eventCamera =
            canvas != null &&
            canvas.renderMode !=
                RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

        Vector2 min =
            RectTransformUtility.WorldToScreenPoint(
                eventCamera,
                corners[0]);

        Vector2 max =
            min;

        for (int i = 1;
             i < corners.Length;
             i++)
        {
            Vector2 point =
                RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    corners[i]);

            min =
                Vector2.Min(
                    min,
                    point);

            max =
                Vector2.Max(
                    max,
                    point);
        }

        return Rect.MinMaxRect(
            min.x,
            min.y,
            max.x,
            max.y);
    }

    public bool ContainsScreenPoint(
        Vector2 screenPoint,
        float padding = 0f)
    {
        Rect screenRect =
            GetScreenRect();

        float safePadding =
            Mathf.Max(
                0f,
                padding);

        screenRect.xMin -=
            safePadding;

        screenRect.xMax +=
            safePadding;

        screenRect.yMin -=
            safePadding;

        screenRect.yMax +=
            safePadding;

        return screenRect.Contains(
            screenPoint);
    }

    public void SnapSpatialState()
    {
        currentTilt =
            Vector2.zero;

        currentZOffset =
            0f;
    }

    private void EnsureSpatialCanvas()
    {
        RectTransform target = Rect;
        if (target == null)
            return;

        if (spatialCanvas == null)
        {
            spatialCanvas =
                target.GetComponent<Canvas>();

            if (spatialCanvas == null)
            {
                spatialCanvas =
                    target.gameObject.AddComponent<Canvas>();

                canvasWasAdded =
                    true;
            }
        }

        if (!originalCanvasStateCaptured)
        {
            originalOverrideSorting =
                spatialCanvas.overrideSorting;

            originalSortingOrder =
                spatialCanvas.sortingOrder;

            originalCanvasStateCaptured =
                true;
        }

        spatialCanvas.overrideSorting =
            true;

        if (preserveRaycasts)
        {
            spatialRaycaster =
                target.GetComponent<GraphicRaycaster>();

            if (spatialRaycaster == null)
            {
                spatialRaycaster =
                    target.gameObject.AddComponent<GraphicRaycaster>();

                raycasterWasAdded =
                    true;
            }
        }
    }

    private void OnDisable()
    {
        RestoreOwnerPose();
    }

    private void OnDestroy()
    {
        RestoreOwnerPose();

        if (spatialCanvas != null &&
            originalCanvasStateCaptured &&
            !canvasWasAdded)
        {
            spatialCanvas.overrideSorting =
                originalOverrideSorting;

            spatialCanvas.sortingOrder =
                originalSortingOrder;
        }

        if (raycasterWasAdded &&
            spatialRaycaster != null)
        {
            Destroy(
                spatialRaycaster);
        }

        if (canvasWasAdded &&
            spatialCanvas != null)
        {
            Destroy(
                spatialCanvas);
        }
    }
}

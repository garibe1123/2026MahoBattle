using System;
using DG.Tweening;
using UnityEngine;

public enum SpatialUIState
{
    Hidden,
    Entering,
    Idle,
    Hovered,
    Focused,
    Selected,
    Background,
    Disabled,
    Exiting
}

[Serializable]
public struct SpatialUIPose
{
    public Vector3 localPosition;
    public Vector3 localEulerAngles;
    public Vector3 localScale;
    [Range(0f, 1f)] public float alpha;
    [Tooltip("TV screen plane에서 camera 방향으로 얼마나 전진할지 나타내는 논리 Depth입니다.")]
    public float visualDepth;

    public SpatialUIPose(
        Vector3 position,
        Vector3 eulerAngles,
        Vector3 scale,
        float poseAlpha,
        float depth = 0f)
    {
        localPosition = position;
        localEulerAngles = eulerAngles;
        localScale = scale == Vector3.zero ? Vector3.one : scale;
        alpha = Mathf.Clamp01(poseAlpha);
        visualDepth = depth;
    }
}

[Serializable]
public struct SpatialUITransition
{
    [Min(0f)] public float duration;
    public Ease positionEase;
    public Ease rotationEase;
    public Ease scaleEase;
    public Ease alphaEase;

    public static SpatialUITransition Create(float duration, Ease ease)
    {
        return new SpatialUITransition
        {
            duration = Mathf.Max(0f, duration),
            positionEase = ease,
            rotationEase = ease,
            scaleEase = ease,
            alphaEase = ease
        };
    }
}

/// <summary>
/// Spatial presentation transform의 단일 owner.
/// Gameplay/Layout controller는 이 컴포넌트의 targetTransform을 직접 수정하지 않고 SetState만 호출해야 합니다.
/// LayoutRoot -> SpatialRoot(BattleSpatialUIElement) -> VisualRoot 구조를 권장합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpatialUIElement : MonoBehaviour
{
    [Header("Ownership")]
    [SerializeField] private Transform targetTransform;
    [SerializeField] private CanvasGroup alphaGroup;

    [Tooltip("논리 Depth를 localPosition에 합성할 local 방향. TV가 +Z를 뒤로 쓰는 현재 구성에서는 Vector3.back이 camera 방향입니다.")]
    [SerializeField] private Vector3 localForward = Vector3.back;

    [Header("Default Transition")]
    [SerializeField, Min(0f)] private float transitionDuration = 0.22f;
    [SerializeField] private Ease transitionEase = Ease.OutCubic;
    [SerializeField] private bool useUnscaledTime = true;

    [Header("State Transition Overrides")]
    [SerializeField] private bool useStateTransitionOverrides = true;
    [SerializeField] private SpatialUITransition hoverTransition = new()
    {
        duration = 0.18f,
        positionEase = Ease.OutCubic,
        rotationEase = Ease.OutCubic,
        scaleEase = Ease.OutCubic,
        alphaEase = Ease.OutCubic
    };
    [SerializeField] private SpatialUITransition selectedTransition = new()
    {
        duration = 0.28f,
        positionEase = Ease.OutBack,
        rotationEase = Ease.OutCubic,
        scaleEase = Ease.OutBack,
        alphaEase = Ease.OutCubic
    };
    [SerializeField] private SpatialUITransition exitTransition = new()
    {
        duration = 0.24f,
        positionEase = Ease.InCubic,
        rotationEase = Ease.InCubic,
        scaleEase = Ease.InCubic,
        alphaEase = Ease.InCubic
    };

    [Header("State Poses")]
    [SerializeField] private SpatialUIPose hiddenPose =
        new(new Vector3(0f, -8f, 0f), new Vector3(3f, 0f, 0f), new Vector3(0.94f, 0.94f, 1f), 0f, -26f);
    [SerializeField] private SpatialUIPose enteringPose =
        new(new Vector3(0f, -4f, 0f), new Vector3(2.5f, 0f, 0f), new Vector3(0.97f, 0.97f, 1f), 0f, -18f);
    [SerializeField] private SpatialUIPose idlePose =
        new(Vector3.zero, new Vector3(2f, -5f, 0f), new Vector3(0.96f, 0.96f, 1f), 1f, 0f);
    [SerializeField] private SpatialUIPose hoveredPose =
        new(new Vector3(0f, 10f, 0f), new Vector3(0.8f, -1.5f, 0f), new Vector3(1.03f, 1.03f, 1f), 1f, 20f);
    [SerializeField] private SpatialUIPose focusedPose =
        new(new Vector3(0f, 3f, 0f), Vector3.zero, new Vector3(1.025f, 1.025f, 1f), 1f, 16f);
    [SerializeField] private SpatialUIPose selectedPose =
        new(new Vector3(0f, 6f, 0f), Vector3.zero, new Vector3(1.05f, 1.05f, 1f), 1f, 34f);
    [SerializeField] private SpatialUIPose backgroundPose =
        new(new Vector3(0f, -2f, 0f), new Vector3(3f, -6f, 0f), Vector3.one, 0.62f, -24f);
    [SerializeField] private SpatialUIPose disabledPose =
        new(new Vector3(0f, -3f, 0f), new Vector3(3f, -7f, 0f), new Vector3(0.95f, 0.95f, 1f), 0.48f, -18f);
    [SerializeField] private SpatialUIPose exitingPose =
        new(new Vector3(0f, 4f, 0f), new Vector3(-3f, 0f, 0f), new Vector3(0.94f, 0.94f, 1f), 0f, -30f);

    private Sequence transition;
    private SpatialUIState currentState = SpatialUIState.Idle;
    private float depthOffset;

    public SpatialUIState CurrentState => currentState;
    public float DepthOffset => depthOffset;
    public float TransitionDuration => transitionDuration;
    public Transform PresentationTransform => targetTransform != null ? targetTransform : transform;

    private void Awake() => ResolveTargets();
    private void OnEnable() => ResolveTargets();
    private void OnDisable() => KillTransition();
    private void OnDestroy() => KillTransition();

    public void BindPresentationTransform(Transform presentationTransform, CanvasGroup group = null)
    {
        KillTransition();
        targetTransform = presentationTransform != null ? presentationTransform : transform;
        alphaGroup = group != null ? group : targetTransform.GetComponent<CanvasGroup>();
        if (alphaGroup == null)
            alphaGroup = targetTransform.gameObject.AddComponent<CanvasGroup>();
    }

    public void SetLocalForward(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
            return;
        localForward = direction.normalized;
        SetState(currentState, true, true);
    }

    public void SetTransition(float duration, Ease ease, bool unscaled = true)
    {
        transitionDuration = Mathf.Max(0f, duration);
        transitionEase = ease;
        useUnscaledTime = unscaled;
    }

    public void SetPose(SpatialUIState state, SpatialUIPose pose)
    {
        pose.alpha = Mathf.Clamp01(pose.alpha);
        if (pose.localScale == Vector3.zero)
            pose.localScale = Vector3.one;

        switch (state)
        {
            case SpatialUIState.Hidden: hiddenPose = pose; break;
            case SpatialUIState.Entering: enteringPose = pose; break;
            case SpatialUIState.Idle: idlePose = pose; break;
            case SpatialUIState.Hovered: hoveredPose = pose; break;
            case SpatialUIState.Focused: focusedPose = pose; break;
            case SpatialUIState.Selected: selectedPose = pose; break;
            case SpatialUIState.Background: backgroundPose = pose; break;
            case SpatialUIState.Disabled: disabledPose = pose; break;
            case SpatialUIState.Exiting: exitingPose = pose; break;
        }
    }

    public SpatialUIPose GetPose(SpatialUIState state)
    {
        return state switch
        {
            SpatialUIState.Hidden => hiddenPose,
            SpatialUIState.Entering => enteringPose,
            SpatialUIState.Hovered => hoveredPose,
            SpatialUIState.Focused => focusedPose,
            SpatialUIState.Selected => selectedPose,
            SpatialUIState.Background => backgroundPose,
            SpatialUIState.Disabled => disabledPose,
            SpatialUIState.Exiting => exitingPose,
            _ => idlePose
        };
    }

    public void SetDepthOffset(float offset, bool immediate = false)
    {
        depthOffset = offset;
        SetState(currentState, immediate, true);
    }

    public void SetState(SpatialUIState state) => SetState(state, false, false);
    public void SetState(SpatialUIState state, bool immediate) => SetState(state, immediate, false);
    public void SnapToState(SpatialUIState state) => SetState(state, true, true);

    private void SetState(SpatialUIState state, bool immediate, bool force)
    {
        ResolveTargets();
        if (targetTransform == null)
            return;
        if (!force && state == currentState)
            return;

        currentState = state;
        SpatialUIPose pose = GetPose(state);
        Vector3 forward = localForward.sqrMagnitude > 0.0001f ? localForward.normalized : Vector3.back;
        Vector3 targetPosition = pose.localPosition + forward * (pose.visualDepth + depthOffset);

        KillTransition();

        SpatialUITransition profile = ResolveTransition(state);
        float duration = profile.duration;

        if (immediate || duration <= 0.0001f || !Application.isPlaying)
        {
            targetTransform.localPosition = targetPosition;
            targetTransform.localRotation = Quaternion.Euler(pose.localEulerAngles);
            targetTransform.localScale = pose.localScale;
            if (alphaGroup != null)
                alphaGroup.alpha = pose.alpha;
            return;
        }

        transition = DOTween.Sequence().SetUpdate(useUnscaledTime);
        transition.Join(targetTransform.DOLocalMove(targetPosition, duration).SetEase(profile.positionEase));
        transition.Join(targetTransform.DOLocalRotate(pose.localEulerAngles, duration, RotateMode.Fast).SetEase(profile.rotationEase));
        transition.Join(targetTransform.DOScale(pose.localScale, duration).SetEase(profile.scaleEase));

        if (alphaGroup != null)
        {
            transition.Join(DOTween
                .To(() => alphaGroup.alpha, value => alphaGroup.alpha = value, pose.alpha, duration)
                .SetEase(profile.alphaEase));
        }

        transition.OnComplete(() => transition = null);
        transition.OnKill(() => transition = null);
    }

    private SpatialUITransition ResolveTransition(SpatialUIState state)
    {
        if (!useStateTransitionOverrides)
            return SpatialUITransition.Create(transitionDuration, transitionEase);

        return state switch
        {
            SpatialUIState.Hovered => ValidateTransition(hoverTransition),
            SpatialUIState.Selected => ValidateTransition(selectedTransition),
            SpatialUIState.Exiting => ValidateTransition(exitTransition),
            _ => SpatialUITransition.Create(transitionDuration, transitionEase)
        };
    }

    private SpatialUITransition ValidateTransition(SpatialUITransition value)
    {
        if (value.duration <= 0f)
            value.duration = transitionDuration;
        return value;
    }

    private void ResolveTargets()
    {
        if (targetTransform == null)
            targetTransform = transform;

        if (alphaGroup == null)
            alphaGroup = targetTransform.GetComponent<CanvasGroup>();
        if (alphaGroup == null)
            alphaGroup = targetTransform.gameObject.AddComponent<CanvasGroup>();

        if (localForward.sqrMagnitude < 0.0001f)
            localForward = Vector3.back;
    }

    private void KillTransition()
    {
        if (transition == null)
            return;
        transition.Kill(false);
        transition = null;
    }
}

using System;
using DG.Tweening;
using UnityEngine;

public enum SpatialUIState
{
    Hidden,
    Idle,
    Hovered,
    Focused,
    Selected,
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

    public SpatialUIPose(Vector3 position, Vector3 eulerAngles, Vector3 scale, float poseAlpha)
    {
        localPosition = position;
        localEulerAngles = eulerAngles;
        localScale = scale;
        alpha = Mathf.Clamp01(poseAlpha);
    }
}

/// <summary>
/// World-Space UI 요소의 공간 상태를 단독 소유하는 공용 Presentation Component.
/// 외부 Controller는 RectTransform을 프레임마다 직접 만지지 않고 SetState만 호출합니다.
/// 진행 중인 Tween은 현재 Transform 값에서 새 상태로 자연스럽게 재타겟됩니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpatialUIElement : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform targetTransform;
    [SerializeField] private CanvasGroup alphaGroup;

    [Header("Tween")]
    [SerializeField, Min(0f)] private float transitionDuration = 0.22f;
    [SerializeField] private Ease transitionEase = Ease.OutCubic;
    [SerializeField] private bool useUnscaledTime = true;

    [Header("State Poses")]
    [SerializeField] private SpatialUIPose hiddenPose =
        new(new Vector3(0f, -8f, 26f), new Vector3(3f, 0f, 0f), new Vector3(0.94f, 0.94f, 1f), 0f);
    [SerializeField] private SpatialUIPose idlePose =
        new(Vector3.zero, new Vector3(2f, -5f, 0f), new Vector3(0.96f, 0.96f, 1f), 1f);
    [SerializeField] private SpatialUIPose hoveredPose =
        new(new Vector3(0f, 10f, -10f), new Vector3(1f, -2f, 0f), Vector3.one, 1f);
    [SerializeField] private SpatialUIPose focusedPose =
        new(new Vector3(0f, 3f, -16f), Vector3.zero, new Vector3(1.025f, 1.025f, 1f), 1f);
    [SerializeField] private SpatialUIPose selectedPose =
        new(new Vector3(0f, 6f, -24f), Vector3.zero, new Vector3(1.07f, 1.07f, 1f), 1f);
    [SerializeField] private SpatialUIPose disabledPose =
        new(new Vector3(0f, -3f, 18f), new Vector3(3f, -7f, 0f), new Vector3(0.95f, 0.95f, 1f), 0.48f);
    [SerializeField] private SpatialUIPose exitingPose =
        new(new Vector3(0f, -10f, 30f), new Vector3(4f, 0f, 0f), new Vector3(0.90f, 0.90f, 1f), 0f);

    private Sequence transition;
    private SpatialUIState currentState = SpatialUIState.Idle;
    private float depthOffset;

    public SpatialUIState CurrentState => currentState;
    public float DepthOffset => depthOffset;
    public float TransitionDuration => transitionDuration;

    private void Awake()
    {
        ResolveTargets();
    }

    private void OnEnable()
    {
        ResolveTargets();
    }

    private void OnDisable()
    {
        KillTransition();
    }

    private void OnDestroy()
    {
        KillTransition();
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
            case SpatialUIState.Idle: idlePose = pose; break;
            case SpatialUIState.Hovered: hoveredPose = pose; break;
            case SpatialUIState.Focused: focusedPose = pose; break;
            case SpatialUIState.Selected: selectedPose = pose; break;
            case SpatialUIState.Disabled: disabledPose = pose; break;
            case SpatialUIState.Exiting: exitingPose = pose; break;
        }
    }

    public SpatialUIPose GetPose(SpatialUIState state)
    {
        return state switch
        {
            SpatialUIState.Hidden => hiddenPose,
            SpatialUIState.Hovered => hoveredPose,
            SpatialUIState.Focused => focusedPose,
            SpatialUIState.Selected => selectedPose,
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

    public void SetState(SpatialUIState state)
    {
        SetState(state, false, false);
    }

    public void SetState(SpatialUIState state, bool immediate)
    {
        SetState(state, immediate, false);
    }

    public void SnapToState(SpatialUIState state)
    {
        SetState(state, true, true);
    }

    private void SetState(SpatialUIState state, bool immediate, bool force)
    {
        ResolveTargets();
        if (targetTransform == null)
            return;

        if (!force && state == currentState)
            return;

        currentState = state;
        SpatialUIPose pose = GetPose(state);
        Vector3 targetPosition = pose.localPosition;
        targetPosition.z += depthOffset;

        KillTransition();

        if (immediate || transitionDuration <= 0.0001f || !Application.isPlaying)
        {
            targetTransform.localPosition = targetPosition;
            targetTransform.localRotation = Quaternion.Euler(pose.localEulerAngles);
            targetTransform.localScale = pose.localScale;
            if (alphaGroup != null)
                alphaGroup.alpha = pose.alpha;
            return;
        }

        transition = DOTween.Sequence();
        transition.SetUpdate(useUnscaledTime);
        transition.Join(targetTransform
            .DOLocalMove(targetPosition, transitionDuration)
            .SetEase(transitionEase));
        transition.Join(targetTransform
            .DOLocalRotate(pose.localEulerAngles, transitionDuration, RotateMode.Fast)
            .SetEase(transitionEase));
        transition.Join(targetTransform
            .DOScale(pose.localScale, transitionDuration)
            .SetEase(transitionEase));

        if (alphaGroup != null)
        {
            transition.Join(DOTween
                .To(() => alphaGroup.alpha, value => alphaGroup.alpha = value, pose.alpha, transitionDuration)
                .SetEase(transitionEase));
        }

        transition.OnComplete(() => transition = null);
        transition.OnKill(() => transition = null);
    }

    private void ResolveTargets()
    {
        if (targetTransform == null)
            targetTransform = transform;

        if (alphaGroup == null)
            alphaGroup = GetComponent<CanvasGroup>();
        if (alphaGroup == null)
            alphaGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void KillTransition()
    {
        if (transition == null)
            return;

        transition.Kill(false);
        transition = null;
    }
}

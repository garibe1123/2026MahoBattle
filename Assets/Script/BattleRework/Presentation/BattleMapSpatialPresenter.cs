using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Stage Map의 보드/노드 transform presentation만 소유합니다.
/// BattleSpatialMapController는 node 선택/그래프/business state만 유지합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleMapSpatialPresenter : MonoBehaviour
{
    private Tween boardTween;

    private void OnDisable()
    {
        boardTween?.Kill();
        boardTween = null;
    }

    public void PlayConfirm(RectTransform board, RectTransform selectedNode, float zoom, float duration)
    {
        if (board == null)
            return;

        boardTween?.Kill();
        board.DOKill();

        float safeDuration = Mathf.Max(0.15f, duration);
        float peak = Mathf.Max(1f, zoom);

        BattleMapNodeSpatialView view = selectedNode != null
            ? selectedNode.GetComponent<BattleMapNodeSpatialView>()
            : null;
        view?.SetSelected(true);
        view?.PlayConfirmPulse(safeDuration);

        Sequence sequence = DOTween.Sequence().SetUpdate(true);
        sequence.Append(board.DOScale(Vector3.one * peak, safeDuration * 0.34f).SetEase(Ease.OutCubic));
        sequence.Append(board.DOScale(Vector3.one, safeDuration * 0.66f).SetEase(Ease.OutBack, 0.55f));
        sequence.OnComplete(() =>
        {
            board.localScale = Vector3.one;
            boardTween = null;
        });
        sequence.OnKill(() => boardTween = null);
        boardTween = sequence;
    }

    public void ResetBoard(RectTransform board, Vector2 anchoredPosition)
    {
        boardTween?.Kill();
        boardTween = null;

        if (board == null)
            return;

        board.DOKill();
        board.anchoredPosition = anchoredPosition;
        board.localScale = Vector3.one;
    }
}

/// <summary>
/// Map node의 LayoutRoot는 BattleSpatialMapController가 소유하고,
/// 이 컴포넌트는 child SpatialRoot만 움직입니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleMapNodeSpatialView :
    MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [SerializeField] private RectTransform spatialRoot;
    [SerializeField] private RectTransform visualRoot;
    [SerializeField] private BattleSpatialUIElement spatialElement;
    [SerializeField] private bool selectable;

    private Tween confirmPulse;
    private bool selected;

    public void Configure(RectTransform spatialTransform, RectTransform visualTransform, bool canSelect)
    {
        spatialRoot = spatialTransform;
        visualRoot = visualTransform;
        selectable = canSelect;

        if (spatialRoot != null)
        {
            spatialElement = spatialRoot.GetComponent<BattleSpatialUIElement>();
            if (spatialElement == null)
                spatialElement = spatialRoot.gameObject.AddComponent<BattleSpatialUIElement>();

            spatialElement.BindPresentationTransform(spatialRoot);
            spatialElement.SetLocalForward(Vector3.back);
            spatialElement.SetPose(
                SpatialUIState.Idle,
                new SpatialUIPose(
                    Vector3.zero,
                    new Vector3(1.6f, 0f, 0f),
                    Vector3.one,
                    1f,
                    selectable ? 8f : -6f));
            spatialElement.SetPose(
                SpatialUIState.Hovered,
                new SpatialUIPose(
                    new Vector3(0f, 5f, 0f),
                    new Vector3(0.4f, 0f, 0f),
                    new Vector3(1.04f, 1.04f, 1f),
                    1f,
                    24f));
            spatialElement.SetPose(
                SpatialUIState.Selected,
                new SpatialUIPose(
                    new Vector3(0f, 8f, 0f),
                    Vector3.zero,
                    new Vector3(1.08f, 1.08f, 1f),
                    1f,
                    40f));
            spatialElement.SetPose(
                SpatialUIState.Background,
                new SpatialUIPose(
                    new Vector3(0f, -2f, 0f),
                    new Vector3(2.5f, 0f, 0f),
                    Vector3.one,
                    0.55f,
                    -18f));

            spatialElement.SnapToState(selectable ? SpatialUIState.Idle : SpatialUIState.Background);
        }
    }

    private void OnDisable()
    {
        confirmPulse?.Kill();
        confirmPulse = null;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!selectable || selected || spatialElement == null)
            return;

        spatialElement.SetState(SpatialUIState.Hovered);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!selectable || selected || spatialElement == null)
            return;

        spatialElement.SetState(SpatialUIState.Idle);
    }

    public void SetSelected(bool value)
    {
        selected = value;
        if (spatialElement == null)
            return;

        spatialElement.SetState(value
            ? SpatialUIState.Selected
            : selectable
                ? SpatialUIState.Idle
                : SpatialUIState.Background);
    }

    public void PlayConfirmPulse(float duration)
    {
        if (visualRoot == null)
            return;

        confirmPulse?.Kill();
        Vector3 baseScale = visualRoot.localScale;

        confirmPulse = visualRoot
            .DOPunchScale(new Vector3(0.08f, 0.08f, 0f), Mathf.Max(0.15f, duration), 5, 0.45f)
            .SetUpdate(true)
            .OnKill(() => confirmPulse = null)
            .OnComplete(() =>
            {
                visualRoot.localScale = baseScale;
                confirmPulse = null;
            });
    }
}

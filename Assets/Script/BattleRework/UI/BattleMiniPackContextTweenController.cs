using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BattleUnifiedInventoryInspectController가 결정한 Mini PACK의 최종 위치/스케일/알파를
/// Combat <-> Reward Choice 전환에서만 부드럽게 보간하는 presentation layer입니다.
///
/// 중요:
/// - 목표값의 소유권은 UnifiedInventoryInspectController에 그대로 있습니다.
/// - 이 컴포넌트는 Unified가 LateUpdate에서 기록한 목표값을 읽고, 그 프레임의 최종 표시값만 보간합니다.
/// - Reward PackEditing / Combat Tab Morph가 시작되면 즉시 제어권을 놓아 기존 Morph Controller와 충돌하지 않습니다.
/// - Mini PACK 검색은 Run이 실제로 시작된 뒤에만 수행하여 초기 Scene bootstrap 비용에 보태지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33420)]
public sealed class BattleMiniPackContextTweenController : MonoBehaviour
{
    private enum MiniPackContext
    {
        Unknown,
        Combat,
        RewardChoice,
        Passthrough
    }

    [Header("REFERENCES — 자동 연결")]
    [Tooltip("현재 Run 상태를 읽어 Combat인지 Reward인지 판정합니다. 비워두면 자동으로 찾습니다.")]
    [SerializeField] private BattleRunManager runManager;
    [Tooltip("Reward가 카드 선택 단계인지 PACK 편집 단계인지 판정합니다. 비워두면 자동으로 찾습니다.")]
    [SerializeField] private BattleRewardFlow rewardFlow;
    [Tooltip("Combat 중 Full PACK이 열렸는지 확인합니다. 비워두면 자동으로 찾습니다.")]
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;

    [Header("PACK TRANSITION — 전투 ↔ 아이템 선택")]
    [Tooltip("Combat Mini PACK이 Reward 선택용 크기/위치/투명도로 변하는 시간입니다. 값이 클수록 천천히 변합니다. Unscaled Time을 사용합니다.")]
    [SerializeField, Range(0.08f, 0.60f)] private float transitionDuration = 0.24f;
    [Tooltip("전환 진행률 0~1에 적용할 Ease 곡선입니다. 기본 EaseInOut은 시작과 끝을 부드럽게 만들어 갑작스러운 축소/페이드를 방지합니다.")]
    [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private RectTransform miniPackRoot;
    private CanvasGroup miniPackGroup;

    private MiniPackContext context = MiniPackContext.Unknown;
    private bool initialized;
    private bool tweening;
    private float elapsed;
    private float nextUiResolveAt;

    private Vector2 currentPosition;
    private Vector3 currentScale = Vector3.one;
    private float currentAlpha = 1f;

    private Vector2 tweenStartPosition;
    private Vector3 tweenStartScale = Vector3.one;
    private float tweenStartAlpha = 1f;

    private Vector2 tweenTargetPosition;
    private Vector3 tweenTargetScale = Vector3.one;
    private float tweenTargetAlpha = 1f;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        initialized = false;
        tweening = false;
        context = MiniPackContext.Unknown;
        nextUiResolveAt = 0f;
    }

    private void LateUpdate()
    {
        ResolveReferences();
        if (runManager == null || !runManager.RunActive)
            return;

        ResolveMiniPackWhenNeeded();
        if (miniPackRoot == null || miniPackGroup == null)
            return;

        // UnifiedInventoryInspectController(DefaultExecutionOrder 33380)가 이 프레임의 authoritative
        // 목표값을 먼저 기록합니다. 우리는 그 값을 읽은 뒤 최종 표시만 덮어씁니다.
        Vector2 authoredPosition = miniPackRoot.anchoredPosition;
        Vector3 authoredScale = miniPackRoot.localScale;
        float authoredAlpha = miniPackGroup.alpha;
        MiniPackContext nextContext = ResolveContext();

        if (!initialized)
        {
            initialized = true;
            context = nextContext;
            currentPosition = authoredPosition;
            currentScale = authoredScale;
            currentAlpha = authoredAlpha;
            return;
        }

        // Full PACK Morph가 사용하는 구간은 기존 시스템이 그대로 소유합니다.
        if (nextContext == MiniPackContext.Passthrough)
        {
            context = nextContext;
            tweening = false;
            currentPosition = authoredPosition;
            currentScale = authoredScale;
            currentAlpha = authoredAlpha;
            return;
        }

        if (nextContext != context)
        {
            bool shouldTween =
                (context == MiniPackContext.Combat && nextContext == MiniPackContext.RewardChoice) ||
                (context == MiniPackContext.RewardChoice && nextContext == MiniPackContext.Combat);

            context = nextContext;
            if (shouldTween)
            {
                BeginTween(authoredPosition, authoredScale, authoredAlpha);
            }
            else
            {
                tweening = false;
                currentPosition = authoredPosition;
                currentScale = authoredScale;
                currentAlpha = authoredAlpha;
            }
        }
        else if (tweening)
        {
            // Inspector에서 목표값을 조정해도 진행 중인 Tween이 자연스럽게 새 목표를 따라갑니다.
            tweenTargetPosition = authoredPosition;
            tweenTargetScale = authoredScale;
            tweenTargetAlpha = authoredAlpha;
        }
        else
        {
            currentPosition = authoredPosition;
            currentScale = authoredScale;
            currentAlpha = authoredAlpha;
        }

        if (!tweening)
            return;

        elapsed += Time.unscaledDeltaTime;
        float duration = Mathf.Max(0.01f, transitionDuration);
        float p = Mathf.Clamp01(elapsed / duration);
        float eased = transitionCurve != null ? Mathf.Clamp01(transitionCurve.Evaluate(p)) : SmoothStep01(p);

        currentPosition = Vector2.LerpUnclamped(tweenStartPosition, tweenTargetPosition, eased);
        currentScale = Vector3.LerpUnclamped(tweenStartScale, tweenTargetScale, eased);
        currentAlpha = Mathf.LerpUnclamped(tweenStartAlpha, tweenTargetAlpha, eased);

        miniPackRoot.anchoredPosition = currentPosition;
        miniPackRoot.localScale = currentScale;
        miniPackGroup.alpha = Mathf.Clamp01(currentAlpha);

        if (p >= 1f)
        {
            tweening = false;
            currentPosition = tweenTargetPosition;
            currentScale = tweenTargetScale;
            currentAlpha = tweenTargetAlpha;
        }
    }

    private void BeginTween(Vector2 targetPosition, Vector3 targetScale, float targetAlpha)
    {
        tweenStartPosition = currentPosition;
        tweenStartScale = currentScale;
        tweenStartAlpha = currentAlpha;

        tweenTargetPosition = targetPosition;
        tweenTargetScale = targetScale;
        tweenTargetAlpha = targetAlpha;

        elapsed = 0f;
        tweening = true;

        // Unified가 이미 새 Context의 목표값을 써둔 프레임이므로, 화면에는 이전 pose를 한 번 복원합니다.
        miniPackRoot.anchoredPosition = currentPosition;
        miniPackRoot.localScale = currentScale;
        miniPackGroup.alpha = Mathf.Clamp01(currentAlpha);
    }

    private MiniPackContext ResolveContext()
    {
        if (runManager == null || !runManager.RunActive)
            return MiniPackContext.Passthrough;

        if (runManager.State == BattleRunState.Reward)
        {
            bool choosing = rewardFlow == null || rewardFlow.Phase == BattleRewardPhase.Choosing;
            return choosing ? MiniPackContext.RewardChoice : MiniPackContext.Passthrough;
        }

        if (runManager.State == BattleRunState.Combat)
        {
            bool fullPackOpen = kineticLoadout != null && kineticLoadout.IsSwitchBoardOpen;
            return fullPackOpen ? MiniPackContext.Passthrough : MiniPackContext.Combat;
        }

        return MiniPackContext.Passthrough;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (rewardFlow == null)
            rewardFlow = FindFirstObjectByType<BattleRewardFlow>(FindObjectsInactive.Include);
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
    }

    private void ResolveMiniPackWhenNeeded()
    {
        if (miniPackRoot != null && miniPackGroup != null)
            return;
        if (Time.unscaledTime < nextUiResolveAt)
            return;

        nextUiResolveAt = Time.unscaledTime + 0.10f;
        RectTransform[] all = FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect == null || rect.name != "BackpackMiniGrid")
                continue;

            miniPackRoot = rect;
            miniPackGroup = rect.GetComponent<CanvasGroup>();
            break;
        }
    }

    private static float SmoothStep01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }
}

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 작은 BackpackMiniGrid와 LoadoutSwitchFull 사이를 하나의 PACK이 실제로 변형되는 것처럼 연결합니다.
///
/// 실제 PACK RectTransform을 직접 움직이지 않습니다. 다른 UI 레이아웃 컨트롤러가 PACK 위치를 갱신해도
/// 전환 모션이 끊기지 않도록, 전환 시작 순간의 PACK을 복제한 비상호작용 Ghost를 전용 Overlay Canvas에서
/// 이동/회전/확대합니다.
///
/// Open:
/// - 작은 PACK이 잠깐 반대 방향으로 눌리는 anticipation
/// - 중앙 GridBoard 쪽으로 곡선을 그리며 이동
/// - 회전 + Back overshoot 확대
/// - Full UI와 cross-fade
/// - 마지막에 Ghost가 Grid에 정착하며 Full UI로 자연스럽게 치환
///
/// Close는 위 동작을 역순으로 재생합니다.
/// 모든 트위닝은 unscaledDeltaTime 기반이라 Combat/Reward Bullet Time에서도 UI 속도는 일정합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33680)]
public sealed class BattleInventoryMorphTransitionController : MonoBehaviour
{
    private const int TransitionSortingOrder = 2400;

    private enum MorphState
    {
        Closed,
        Opening,
        Open,
        Closing
    }

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleInventoryInteractionController inventoryInteraction;
    [SerializeField] private BattleKineticLoadoutUI kineticLoadout;

    [Header("Timing")]
    [SerializeField, Range(0.20f, 0.70f)] private float openDuration = 0.42f;
    [SerializeField, Range(0.18f, 0.60f)] private float closeDuration = 0.34f;

    [Header("PACK Motion")]
    [SerializeField, Range(0.82f, 1.02f)] private float targetBoardCoverage = 0.94f;
    [SerializeField, Range(0f, 12f)] private float rotationKickDegrees = 5.5f;
    [SerializeField, Range(0f, 120f)] private float arcHeight = 54f;
    [SerializeField, Range(0f, 70f)] private float anticipationDistance = 24f;
    [SerializeField, Range(0.82f, 1f)] private float anticipationScale = 0.95f;
    [SerializeField, Range(0.8f, 2.4f)] private float scaleOvershoot = 1.18f;

    [Header("Cross Fade")]
    [SerializeField, Range(0f, 0.65f)] private float fullFadeStart = 0.22f;
    [SerializeField, Range(0.35f, 0.90f)] private float packFadeStart = 0.60f;
    [SerializeField, Range(0.65f, 0.98f)] private float raycastEnablePoint = 0.88f;
    [SerializeField, Range(0.72f, 0.98f)] private float fullUiStartScale = 0.88f;

    [Header("Final Full UI Scale")]
    [SerializeField, Range(0.85f, 1.10f)] private float combatFullScale = 1f;
    [SerializeField, Range(0.85f, 1.10f)] private float rewardFullScale = 0.96f;

    private RectTransform miniPackRoot;
    private CanvasGroup miniPackGroup;
    private RectTransform fullRoot;
    private CanvasGroup fullGroup;
    private RectTransform boardRoot;

    private Canvas transitionCanvas;
    private RectTransform transitionRoot;
    private RectTransform ghostRoot;
    private CanvasGroup ghostGroup;

    private MorphState state = MorphState.Closed;
    private float elapsed;
    private bool rewardMode;
    private bool initialized;
    private float nextResolveTime;

    // 전환 시작 때 저장한 작은 PACK의 화면 기준 Home pose.
    private bool homePoseValid;
    private Vector2 homeCenter;
    private Vector2 homeSize;
    private Vector3 homeGhostScale = Vector3.one;
    private float homeRotation;
    private float homeAlpha = 1f;

    // 현재 전환의 시작 pose. 중간에 방향이 반전되어도 현재 위치부터 이어서 움직입니다.
    private Vector2 transitionStartCenter;
    private Vector3 transitionStartScale = Vector3.one;
    private float transitionStartRotation;
    private float transitionStartGhostAlpha = 1f;
    private float transitionStartFullAlpha;

    private void Awake()
    {
        ResolveReferences();
        EnsureTransitionCanvas();
        ResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureTransitionCanvas();
        ResolveUi();
        nextResolveTime = 0f;
        initialized = false;
        state = MorphState.Closed;
    }

    private void OnDisable()
    {
        DestroyGhost();
        RestoreMiniPackVisibility();
    }

    private void OnDestroy()
    {
        DestroyGhost();
    }

    private void Update()
    {
        ResolveReferences();
        EnsureTransitionCanvas();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveUi();
        }

        bool requested = IsInspectRequested(out bool isReward);

        if (!initialized)
        {
            initialized = true;
            rewardMode = isReward;
            if (requested)
                BeginOpening(isReward);
            return;
        }

        if (requested)
        {
            rewardMode = isReward;
            if (state == MorphState.Closed || state == MorphState.Closing)
                BeginOpening(isReward);
        }
        else if (state == MorphState.Open || state == MorphState.Opening)
        {
            BeginClosing();
        }
    }

    private void LateUpdate()
    {
        ResolveUi();
        if (miniPackRoot == null || miniPackGroup == null || fullRoot == null || fullGroup == null || boardRoot == null)
            return;

        switch (state)
        {
            case MorphState.Opening:
                elapsed += Time.unscaledDeltaTime;
                ApplyOpening(Mathf.Clamp01(elapsed / Mathf.Max(0.01f, openDuration)));
                break;

            case MorphState.Open:
                MaintainOpenState();
                break;

            case MorphState.Closing:
                elapsed += Time.unscaledDeltaTime;
                ApplyClosing(Mathf.Clamp01(elapsed / Mathf.Max(0.01f, closeDuration)));
                break;

            case MorphState.Closed:
                MaintainClosedState();
                break;
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (inventoryInteraction == null)
            inventoryInteraction = FindFirstObjectByType<BattleInventoryInteractionController>();
        if (kineticLoadout == null)
            kineticLoadout = FindFirstObjectByType<BattleKineticLoadoutUI>();
    }

    private void ResolveUi()
    {
        if (miniPackRoot == null)
        {
            miniPackRoot = FindRect("BackpackMiniGrid");
            if (miniPackRoot != null)
            {
                miniPackGroup = miniPackRoot.GetComponent<CanvasGroup>();
                if (miniPackGroup == null)
                    miniPackGroup = miniPackRoot.gameObject.AddComponent<CanvasGroup>();
            }
        }

        if (fullRoot == null)
        {
            fullRoot = FindRect("LoadoutSwitchFull");
            if (fullRoot != null)
            {
                fullGroup = fullRoot.GetComponent<CanvasGroup>();
                boardRoot = FindChildRect(fullRoot, "GridBoard");
            }
        }
    }

    private bool IsInspectRequested(out bool isReward)
    {
        isReward = runManager != null &&
                   runManager.RunActive &&
                   runManager.State == BattleRunState.Reward &&
                   inventoryInteraction != null &&
                   inventoryInteraction.IsRewardPackEditing;

        if (isReward)
            return true;

        if (runManager == null || !runManager.RunActive || runManager.State != BattleRunState.Combat || kineticLoadout == null)
            return false;

        return kineticLoadout.SwitchHeld && kineticLoadout.BoardWasShown;
    }

    private void BeginOpening(bool isReward)
    {
        ResolveUi();
        EnsureTransitionCanvas();
        if (!CanMorph())
            return;

        rewardMode = isReward;

        if (state == MorphState.Closed || ghostRoot == null)
        {
            CaptureHomePose();
            CreateGhostFromPack();
        }

        if (ghostRoot == null)
            return;

        transitionStartCenter = ghostRoot.anchoredPosition;
        transitionStartScale = ghostRoot.localScale;
        transitionStartRotation = NormalizeAngle(ghostRoot.localEulerAngles.z);
        transitionStartGhostAlpha = ghostGroup != null ? ghostGroup.alpha : 1f;
        transitionStartFullAlpha = Mathf.Clamp01(fullGroup.alpha);

        elapsed = 0f;
        state = MorphState.Opening;

        fullRoot.gameObject.SetActive(true);
        fullGroup.blocksRaycasts = false;
        fullGroup.interactable = false;

        HideRealMiniPack();
    }

    private void BeginClosing()
    {
        ResolveUi();
        EnsureTransitionCanvas();
        if (!CanMorph() || !homePoseValid)
            return;

        if (ghostRoot == null)
            CreateGhostAtBoardPose();

        if (ghostRoot == null)
            return;

        transitionStartCenter = ghostRoot.anchoredPosition;
        transitionStartScale = ghostRoot.localScale;
        transitionStartRotation = NormalizeAngle(ghostRoot.localEulerAngles.z);
        transitionStartGhostAlpha = ghostGroup != null ? ghostGroup.alpha : 0f;
        transitionStartFullAlpha = Mathf.Clamp01(fullGroup.alpha);

        elapsed = 0f;
        state = MorphState.Closing;

        fullGroup.blocksRaycasts = false;
        fullGroup.interactable = false;
        HideRealMiniPack();
    }

    private bool CanMorph()
    {
        return miniPackRoot != null &&
               miniPackGroup != null &&
               fullRoot != null &&
               fullGroup != null &&
               boardRoot != null &&
               transitionRoot != null;
    }

    private void CaptureHomePose()
    {
        if (miniPackRoot == null || transitionRoot == null)
            return;

        if (!TryGetRectInTransitionSpace(miniPackRoot, out Vector2 center, out Vector2 size))
            return;

        homePoseValid = true;
        homeCenter = center;
        homeSize = size;
        homeRotation = GetScreenRotation(miniPackRoot);
        homeAlpha = miniPackGroup != null ? Mathf.Clamp01(miniPackGroup.alpha) : 1f;

        Vector2 intrinsic = GetIntrinsicSize(miniPackRoot);
        homeGhostScale = new Vector3(
            size.x / Mathf.Max(1f, intrinsic.x),
            size.y / Mathf.Max(1f, intrinsic.y),
            1f);
    }

    private void CreateGhostFromPack()
    {
        DestroyGhost();
        if (!homePoseValid || miniPackRoot == null || transitionRoot == null)
            return;

        GameObject clone = Instantiate(miniPackRoot.gameObject, transitionRoot, false);
        clone.name = "BackpackMorphGhost";
        ghostRoot = clone.GetComponent<RectTransform>();
        if (ghostRoot == null)
        {
            Destroy(clone);
            return;
        }

        PrepareGhostHierarchy(clone);
        ghostGroup = clone.GetComponent<CanvasGroup>();
        if (ghostGroup == null)
            ghostGroup = clone.AddComponent<CanvasGroup>();
        ghostGroup.alpha = Mathf.Max(0.01f, homeAlpha);
        ghostGroup.blocksRaycasts = false;
        ghostGroup.interactable = false;

        ghostRoot.anchorMin = ghostRoot.anchorMax = new Vector2(0.5f, 0.5f);
        ghostRoot.pivot = new Vector2(0.5f, 0.5f);
        ghostRoot.anchoredPosition = homeCenter;
        ghostRoot.localScale = homeGhostScale;
        ghostRoot.localRotation = Quaternion.Euler(0f, 0f, homeRotation);
        ghostRoot.SetAsLastSibling();
    }

    private void CreateGhostAtBoardPose()
    {
        if (!homePoseValid)
            CaptureHomePose();
        CreateGhostFromPack();
        if (ghostRoot == null)
            return;

        GetBoardPose(out Vector2 boardCenter, out Vector3 boardScale, out float boardRotation);
        ghostRoot.anchoredPosition = boardCenter;
        ghostRoot.localScale = boardScale;
        ghostRoot.localRotation = Quaternion.Euler(0f, 0f, boardRotation);
        if (ghostGroup != null)
            ghostGroup.alpha = 0f;
    }

    private void PrepareGhostHierarchy(GameObject clone)
    {
        Canvas[] nestedCanvases = clone.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < nestedCanvases.Length; i++)
        {
            Canvas nested = nestedCanvases[i];
            if (nested == null)
                continue;
            nested.overrideSorting = false;
        }

        Graphic[] graphics = clone.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
            if (graphics[i] != null)
                graphics[i].raycastTarget = false;
    }

    private void ApplyOpening(float p)
    {
        bool paused = BattlePauseController.IsPaused;
        GetBoardPose(out Vector2 boardCenter, out Vector3 boardScale, out float boardRotation);

        float anticipationIn = SmoothRange(0f, 0.12f, p);
        float anticipationOut = 1f - SmoothRange(0.12f, 0.28f, p);
        float anticipation = anticipationIn * anticipationOut;

        float travelP = SmoothRange(0.12f, 1f, p);
        float move = EaseInOutCubic(travelP);
        float scaleT = EaseOutBack(travelP, scaleOvershoot);

        Vector2 anticipationVector = new(-anticipationDistance, anticipationDistance * 0.26f);
        Vector2 center = Vector2.LerpUnclamped(transitionStartCenter, boardCenter, move);
        center += anticipationVector * anticipation;
        center += Vector2.up * Mathf.Sin(travelP * Mathf.PI) * arcHeight;

        Vector3 anticipatedStartScale = Vector3.Scale(transitionStartScale, new Vector3(anticipationScale, anticipationScale, 1f));
        Vector3 fromScale = Vector3.Lerp(transitionStartScale, anticipatedStartScale, anticipationIn * anticipationOut);
        Vector3 scale = Vector3.LerpUnclamped(fromScale, boardScale, scaleT);

        float rotation = Mathf.LerpAngle(transitionStartRotation, boardRotation, move);
        rotation += Mathf.Sin(travelP * Mathf.PI) * -rotationKickDegrees;

        SetGhostPose(center, scale, rotation);

        float packFade = 1f - SmoothRange(packFadeStart, 0.96f, p);
        if (ghostGroup != null)
            ghostGroup.alpha = Mathf.Clamp01(transitionStartGhostAlpha * packFade);

        float fullFade = SmoothRange(fullFadeStart, 0.92f, p);
        float finalScale = rewardMode ? rewardFullScale : combatFullScale;
        float fullScaleT = EaseOutBack(SmoothRange(0.10f, 1f, p), 1.03f);
        float visualScale = Mathf.Lerp(fullUiStartScale, 1f, fullScaleT);

        fullRoot.localScale = Vector3.one * finalScale * visualScale;
        fullGroup.alpha = Mathf.Max(transitionStartFullAlpha * (1f - fullFade), fullFade);
        fullGroup.blocksRaycasts = !paused && p >= raycastEnablePoint;
        fullGroup.interactable = !paused && p >= raycastEnablePoint;

        HideRealMiniPack();

        if (p >= 1f)
            CompleteOpening();
    }

    private void CompleteOpening()
    {
        state = MorphState.Open;
        elapsed = 0f;
        DestroyGhost();

        if (fullGroup != null)
        {
            fullGroup.alpha = 1f;
            fullGroup.blocksRaycasts = !BattlePauseController.IsPaused;
            fullGroup.interactable = !BattlePauseController.IsPaused;
        }

        if (fullRoot != null)
            fullRoot.localScale = Vector3.one * (rewardMode ? rewardFullScale : combatFullScale);

        HideRealMiniPack();
    }

    private void MaintainOpenState()
    {
        if (fullRoot == null || fullGroup == null || miniPackGroup == null)
            return;

        bool paused = BattlePauseController.IsPaused;
        fullGroup.alpha = 1f;
        fullGroup.blocksRaycasts = !paused;
        fullGroup.interactable = !paused;
        fullRoot.localScale = Vector3.one * (rewardMode ? rewardFullScale : combatFullScale);
        HideRealMiniPack();
    }

    private void ApplyClosing(float p)
    {
        bool paused = BattlePauseController.IsPaused;

        float travel = EaseInOutCubic(p);
        float ghostFade = SmoothRange(0.04f, 0.28f, p) * (1f - SmoothRange(0.82f, 1f, p));

        Vector2 center = Vector2.LerpUnclamped(transitionStartCenter, homeCenter, travel);
        center += Vector2.up * Mathf.Sin(p * Mathf.PI) * arcHeight * 0.62f;

        Vector3 scale = Vector3.LerpUnclamped(transitionStartScale, homeGhostScale, EaseInOutCubic(p));
        float rotation = Mathf.LerpAngle(transitionStartRotation, homeRotation, travel);
        rotation += Mathf.Sin(p * Mathf.PI) * rotationKickDegrees * 0.58f;

        SetGhostPose(center, scale, rotation);
        if (ghostGroup != null)
            ghostGroup.alpha = Mathf.Clamp01(Mathf.Max(transitionStartGhostAlpha, ghostFade));

        float fullFade = 1f - SmoothRange(0.02f, 0.72f, p);
        float finalScale = rewardMode ? rewardFullScale : combatFullScale;
        fullGroup.alpha = Mathf.Clamp01(transitionStartFullAlpha * fullFade);
        fullRoot.localScale = Vector3.one * finalScale * Mathf.Lerp(1f, fullUiStartScale, EaseInCubic(p));
        fullGroup.blocksRaycasts = false;
        fullGroup.interactable = false;

        HideRealMiniPack();

        if (p >= 1f)
            CompleteClosing(paused);
    }

    private void CompleteClosing(bool paused)
    {
        state = MorphState.Closed;
        elapsed = 0f;
        DestroyGhost();

        if (fullGroup != null)
        {
            fullGroup.alpha = 0f;
            fullGroup.blocksRaycasts = false;
            fullGroup.interactable = false;
        }

        if (fullRoot != null)
            fullRoot.localScale = Vector3.one * fullUiStartScale;

        if (miniPackGroup != null)
        {
            miniPackGroup.alpha = homeAlpha > 0.01f ? homeAlpha : 1f;
            miniPackGroup.blocksRaycasts = !paused;
            miniPackGroup.interactable = !paused;
        }

        homePoseValid = false;
    }

    private void MaintainClosedState()
    {
        if (ghostRoot != null)
            DestroyGhost();
    }

    private void HideRealMiniPack()
    {
        if (miniPackGroup == null)
            return;

        miniPackGroup.alpha = 0f;
        miniPackGroup.blocksRaycasts = false;
        miniPackGroup.interactable = false;
    }

    private void RestoreMiniPackVisibility()
    {
        if (miniPackGroup == null)
            return;

        miniPackGroup.alpha = homePoseValid && homeAlpha > 0.01f ? homeAlpha : 1f;
        miniPackGroup.blocksRaycasts = !BattlePauseController.IsPaused;
        miniPackGroup.interactable = !BattlePauseController.IsPaused;
    }

    private void GetBoardPose(out Vector2 center, out Vector3 scale, out float rotation)
    {
        center = Vector2.zero;
        scale = Vector3.one;
        rotation = 0f;

        if (boardRoot == null || transitionRoot == null || ghostRoot == null)
            return;

        if (!TryGetRectInTransitionSpace(boardRoot, out center, out Vector2 boardSize))
            return;

        Vector2 intrinsic = GetIntrinsicSize(ghostRoot);
        float cover = Mathf.Clamp(targetBoardCoverage, 0.82f, 1.02f);
        scale = new Vector3(
            boardSize.x * cover / Mathf.Max(1f, intrinsic.x),
            boardSize.y * cover / Mathf.Max(1f, intrinsic.y),
            1f);
        rotation = GetScreenRotation(boardRoot);
    }

    private void SetGhostPose(Vector2 center, Vector3 scale, float rotation)
    {
        if (ghostRoot == null)
            return;

        ghostRoot.anchoredPosition = center;
        ghostRoot.localScale = scale;
        ghostRoot.localRotation = Quaternion.Euler(0f, 0f, rotation);
    }

    private void EnsureTransitionCanvas()
    {
        if (transitionCanvas != null)
            return;

        GameObject canvasObject = new("BattleInventoryMorphCanvas");
        canvasObject.transform.SetParent(transform, false);
        transitionCanvas = canvasObject.AddComponent<Canvas>();
        transitionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        transitionCanvas.overrideSorting = true;
        transitionCanvas.sortingOrder = TransitionSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        transitionRoot = CreateRect(canvasObject.transform, "MorphTransitionRoot", Vector2.zero);
        Stretch(transitionRoot);
        transitionRoot.anchorMin = transitionRoot.anchorMax = new Vector2(0.5f, 0.5f);
        transitionRoot.pivot = new Vector2(0.5f, 0.5f);
        transitionRoot.sizeDelta = new Vector2(1920f, 1080f);
        transitionRoot.anchoredPosition = Vector2.zero;

        CanvasGroup group = transitionRoot.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private bool TryGetRectInTransitionSpace(RectTransform source, out Vector2 center, out Vector2 size)
    {
        center = Vector2.zero;
        size = Vector2.zero;
        if (source == null || transitionRoot == null)
            return false;

        Vector3[] corners = new Vector3[4];
        source.GetWorldCorners(corners);

        Canvas sourceCanvas = source.GetComponentInParent<Canvas>();
        Camera camera = sourceCanvas != null && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? sourceCanvas.worldCamera
            : null;

        Vector2[] local = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(transitionRoot, screen, null, out local[i]))
                return false;
        }

        Vector2 min = local[0];
        Vector2 max = local[0];
        for (int i = 1; i < 4; i++)
        {
            min = Vector2.Min(min, local[i]);
            max = Vector2.Max(max, local[i]);
        }

        center = (min + max) * 0.5f;
        size = new Vector2(Mathf.Max(1f, max.x - min.x), Mathf.Max(1f, max.y - min.y));
        return true;
    }

    private static Vector2 GetIntrinsicSize(RectTransform rect)
    {
        if (rect == null)
            return Vector2.one;

        Vector2 size = rect.rect.size;
        if (size.x < 1f || size.y < 1f)
            size = rect.sizeDelta;
        return new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));
    }

    private static float GetScreenRotation(RectTransform rect)
    {
        if (rect == null)
            return 0f;
        return NormalizeAngle(rect.eulerAngles.z);
    }

    private void DestroyGhost()
    {
        if (ghostRoot != null)
            Destroy(ghostRoot.gameObject);
        ghostRoot = null;
        ghostGroup = null;
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = UnityEngine.Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }
        return null;
    }

    private static RectTransform FindChildRect(RectTransform root, string objectName)
    {
        if (root == null)
            return null;

        RectTransform[] all = root.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }
        return null;
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 size)
    {
        GameObject go = new(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static float SmoothRange(float start, float end, float value)
    {
        if (Mathf.Approximately(start, end))
            return value >= end ? 1f : 0f;
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));
    }

    private static float EaseInOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }

    private static float EaseInCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }

    private static float EaseOutBack(float t, float overshoot)
    {
        t = Mathf.Clamp01(t);
        float c1 = Mathf.Max(0.01f, overshoot);
        float c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }
}

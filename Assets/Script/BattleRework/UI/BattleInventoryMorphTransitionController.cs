using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 작은 BackpackMiniGrid가 갑자기 사라지고 LoadoutSwitchFull이 켜지는 점프컷을 제거합니다.
/// Combat Tab과 Reward PACK 편집 모두 같은 전환을 사용합니다.
///
/// Open:
/// 1) 현재 작은 PACK이 살짝 비틀리며 화면 중앙 GridBoard 쪽으로 이동
/// 2) 크기가 커지며 약한 overshoot
/// 3) Full Tab UI가 뒤에서 cross-fade
/// 4) 작은 PACK이 사라지고 실제 GridBoard가 정착
///
/// Close는 위 모션의 역방향입니다.
/// 모든 시간은 unscaledDeltaTime을 사용하므로 Bullet Time / Reward Slow 상태에서도
/// UI 전환 속도는 일정합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(33580)]
public sealed class BattleInventoryMorphTransitionController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

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
    [SerializeField, Range(0.16f, 0.60f)] private float openDuration = 0.34f;
    [SerializeField, Range(0.14f, 0.50f)] private float closeDuration = 0.26f;

    [Header("PACK Morph")]
    [SerializeField, Range(0.80f, 1.00f)] private float targetBoardCoverage = 0.92f;
    [SerializeField, Range(0f, 8f)] private float rotationKickDegrees = 3.4f;
    [SerializeField, Range(0f, 80f)] private float arcHeight = 34f;
    [SerializeField, Range(0.4f, 2.5f)] private float backOvershoot = 1.08f;

    [Header("Cross Fade")]
    [SerializeField, Range(0f, 0.75f)] private float fullFadeStart = 0.28f;
    [SerializeField, Range(0.25f, 0.95f)] private float packFadeStart = 0.56f;
    [SerializeField, Range(0.70f, 1f)] private float raycastEnablePoint = 0.86f;
    [SerializeField, Range(0.75f, 0.98f)] private float fullUiStartScale = 0.90f;

    [Header("Final Full UI Scale")]
    [SerializeField, Range(0.85f, 1.10f)] private float combatFullScale = 1f;
    [SerializeField, Range(0.85f, 1.10f)] private float rewardFullScale = 0.96f;

    private RectTransform miniPackRoot;
    private CanvasGroup miniPackGroup;
    private RectTransform fullRoot;
    private CanvasGroup fullGroup;
    private RectTransform boardRoot;
    private RectTransform detailRoot;
    private CanvasGroup detailGroup;

    private FieldInfo loadoutSwitchHeldField;
    private FieldInfo loadoutBoardShownField;

    private MorphState state = MorphState.Closed;
    private float elapsed;
    private bool rewardMode;
    private float nextResolveTime;

    // PACK home pose. 실제 작은 PACK이 화면에 떠 있던 상태를 그대로 기억합니다.
    private bool homePoseValid;
    private Vector2 homeAnchoredPosition;
    private Vector3 homeLocalScale = Vector3.one;
    private Quaternion homeLocalRotation = Quaternion.identity;
    private Vector3 homeCenterWorld;
    private float homeAlpha = 1f;

    // Transition start pose. 방향이 중간에 뒤집혀도 현재 위치에서 자연스럽게 이어집니다.
    private Vector3 transitionStartCenterWorld;
    private Vector3 transitionStartScale = Vector3.one;
    private Quaternion transitionStartRotation = Quaternion.identity;
    private float transitionStartPackAlpha = 1f;
    private float transitionStartFullAlpha;

    private bool initialized;

    private void Awake()
    {
        ResolveReferences();
        CacheReflection();
        ResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheReflection();
        ResolveUi();
        nextResolveTime = 0f;
        initialized = false;
    }

    private void OnDisable()
    {
        if (homePoseValid)
            RestoreHomePose();
    }

    private void Update()
    {
        ResolveReferences();
        CacheReflection();

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

    private void CacheReflection()
    {
        if (kineticLoadout == null)
            return;

        loadoutSwitchHeldField ??= typeof(BattleKineticLoadoutUI).GetField("switchHeld", PrivateInstance);
        loadoutBoardShownField ??= typeof(BattleKineticLoadoutUI).GetField("boardWasShown", PrivateInstance);
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

        if (detailRoot == null)
        {
            detailRoot = FindRect("EquipmentDetailPanel");
            if (detailRoot != null)
                detailGroup = detailRoot.GetComponent<CanvasGroup>();
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

        bool held = ReadBool(loadoutSwitchHeldField, kineticLoadout);
        bool shown = ReadBool(loadoutBoardShownField, kineticLoadout);
        return held && shown;
    }

    private void BeginOpening(bool isReward)
    {
        ResolveUi();
        if (miniPackRoot == null || miniPackGroup == null || fullRoot == null || fullGroup == null || boardRoot == null)
            return;

        rewardMode = isReward;

        // Closed 상태에서만 실제 HUD의 현재 위치를 home으로 저장합니다.
        // Closing 중 다시 열리면 현재 transition pose에서 방향만 전환합니다.
        if (state == MorphState.Closed || !homePoseValid)
            CaptureHomePose();

        transitionStartCenterWorld = GetMiniPackCenterWorld();
        transitionStartScale = miniPackRoot.localScale;
        transitionStartRotation = miniPackRoot.localRotation;
        transitionStartPackAlpha = Mathf.Clamp01(miniPackGroup.alpha > 0.001f ? miniPackGroup.alpha : homeAlpha);
        transitionStartFullAlpha = Mathf.Clamp01(fullGroup.alpha);

        elapsed = 0f;
        state = MorphState.Opening;

        fullRoot.gameObject.SetActive(true);
        miniPackRoot.gameObject.SetActive(true);
        miniPackGroup.alpha = transitionStartPackAlpha;
        miniPackGroup.blocksRaycasts = false;
        miniPackGroup.interactable = false;
        fullGroup.blocksRaycasts = false;
        fullGroup.interactable = false;
    }

    private void BeginClosing()
    {
        ResolveUi();
        if (miniPackRoot == null || miniPackGroup == null || fullRoot == null || fullGroup == null || boardRoot == null)
            return;

        // Open 완료 후 작은 PACK은 home 위치로 복구되어 투명해져 있습니다.
        // 닫기 시작 시에는 GridBoard 위치에 다시 겹쳐 놓고 거기서 home으로 축소합니다.
        if (state == MorphState.Open)
        {
            Vector3 boardCenter = GetBoardCenterWorld();
            Vector3 targetScale = ComputeBoardCoverScale();
            Quaternion targetRotation = ComputeBoardLocalRotation();
            SetMiniPackVisualCenter(boardCenter, targetScale, targetRotation);
            miniPackGroup.alpha = 0f;
        }

        transitionStartCenterWorld = GetMiniPackCenterWorld();
        transitionStartScale = miniPackRoot.localScale;
        transitionStartRotation = miniPackRoot.localRotation;
        transitionStartPackAlpha = miniPackGroup.alpha;
        transitionStartFullAlpha = Mathf.Clamp01(fullGroup.alpha);

        elapsed = 0f;
        state = MorphState.Closing;
        miniPackRoot.gameObject.SetActive(true);
        miniPackGroup.blocksRaycasts = false;
        miniPackGroup.interactable = false;
        fullGroup.blocksRaycasts = false;
        fullGroup.interactable = false;
    }

    private void CaptureHomePose()
    {
        if (miniPackRoot == null)
            return;

        homePoseValid = true;
        homeAnchoredPosition = miniPackRoot.anchoredPosition;
        homeLocalScale = miniPackRoot.localScale;
        homeLocalRotation = miniPackRoot.localRotation;
        homeCenterWorld = GetMiniPackCenterWorld();
        homeAlpha = miniPackGroup != null ? Mathf.Clamp01(miniPackGroup.alpha) : 1f;
    }

    private void RestoreHomePose()
    {
        if (!homePoseValid || miniPackRoot == null)
            return;

        miniPackRoot.anchoredPosition = homeAnchoredPosition;
        miniPackRoot.localScale = homeLocalScale;
        miniPackRoot.localRotation = homeLocalRotation;
    }

    private void ApplyOpening(float p)
    {
        bool paused = BattlePauseController.IsPaused;
        float move = EaseInOutCubic(p);
        float scaleEase = EaseOutBack(p, backOvershoot);
        float fullFade = SmoothRange(fullFadeStart, 0.90f, p);
        float packFade = 1f - SmoothRange(packFadeStart, 0.94f, p);

        Vector3 boardCenter = GetBoardCenterWorld();
        Vector3 targetScale = ComputeBoardCoverScale();
        Quaternion targetRotation = ComputeBoardLocalRotation();

        Vector3 center = Vector3.LerpUnclamped(transitionStartCenterWorld, boardCenter, move);
        center += GetWorldUpOffset(Mathf.Sin(p * Mathf.PI) * arcHeight);

        Vector3 scale = Vector3.LerpUnclamped(transitionStartScale, targetScale, scaleEase);
        Quaternion rotation = Quaternion.SlerpUnclamped(transitionStartRotation, targetRotation, move);
        float kick = Mathf.Sin(p * Mathf.PI) * rotationKickDegrees;
        rotation *= Quaternion.Euler(0f, 0f, -kick);

        SetMiniPackVisualCenter(center, scale, rotation);
        miniPackGroup.alpha = Mathf.Clamp01(transitionStartPackAlpha * packFade);
        miniPackGroup.blocksRaycasts = false;
        miniPackGroup.interactable = false;

        float finalScale = rewardMode ? rewardFullScale : combatFullScale;
        float uiScale = Mathf.Lerp(fullUiStartScale, 1f, EaseOutCubic(p));
        fullRoot.localScale = Vector3.one * finalScale * uiScale;
        fullGroup.alpha = Mathf.Max(transitionStartFullAlpha * (1f - fullFade), fullFade);
        fullGroup.blocksRaycasts = !paused && p >= raycastEnablePoint;
        fullGroup.interactable = !paused && p >= raycastEnablePoint;

        if (detailGroup != null)
        {
            float desired = detailGroup.alpha;
            float detailFade = SmoothRange(0.62f, 0.96f, p);
            detailGroup.alpha = desired * detailFade;
        }

        if (p >= 1f)
            CompleteOpening();
    }

    private void CompleteOpening()
    {
        state = MorphState.Open;
        elapsed = 0f;

        RestoreHomePose();
        if (miniPackGroup != null)
        {
            miniPackGroup.alpha = 0f;
            miniPackGroup.blocksRaycasts = false;
            miniPackGroup.interactable = false;
        }

        if (fullGroup != null)
        {
            fullGroup.alpha = 1f;
            fullGroup.blocksRaycasts = !BattlePauseController.IsPaused;
            fullGroup.interactable = !BattlePauseController.IsPaused;
        }

        if (fullRoot != null)
            fullRoot.localScale = Vector3.one * (rewardMode ? rewardFullScale : combatFullScale);
    }

    private void MaintainOpenState()
    {
        if (fullGroup == null || fullRoot == null || miniPackGroup == null)
            return;

        bool paused = BattlePauseController.IsPaused;
        fullGroup.alpha = 1f;
        fullGroup.blocksRaycasts = !paused;
        fullGroup.interactable = !paused;
        fullRoot.localScale = Vector3.one * (rewardMode ? rewardFullScale : combatFullScale);

        miniPackGroup.alpha = 0f;
        miniPackGroup.blocksRaycasts = false;
        miniPackGroup.interactable = false;
    }

    private void ApplyClosing(float p)
    {
        float move = EaseInOutCubic(p);
        float fullFade = 1f - SmoothRange(0.04f, 0.72f, p);
        float packFade = SmoothRange(0.16f, 0.58f, p);

        Vector3 center = Vector3.LerpUnclamped(transitionStartCenterWorld, homeCenterWorld, move);
        center += GetWorldUpOffset(Mathf.Sin(p * Mathf.PI) * arcHeight * 0.55f);

        Vector3 scale = Vector3.LerpUnclamped(transitionStartScale, homeLocalScale, EaseInOutCubic(p));
        Quaternion rotation = Quaternion.SlerpUnclamped(transitionStartRotation, homeLocalRotation, move);
        float kick = Mathf.Sin(p * Mathf.PI) * rotationKickDegrees * 0.55f;
        rotation *= Quaternion.Euler(0f, 0f, kick);

        SetMiniPackVisualCenter(center, scale, rotation);
        miniPackGroup.alpha = Mathf.Lerp(transitionStartPackAlpha, homeAlpha, packFade);
        miniPackGroup.blocksRaycasts = false;
        miniPackGroup.interactable = false;

        float finalScale = rewardMode ? rewardFullScale : combatFullScale;
        float uiScale = Mathf.Lerp(1f, fullUiStartScale, EaseInCubic(p));
        fullRoot.localScale = Vector3.one * finalScale * uiScale;
        fullGroup.alpha = transitionStartFullAlpha * fullFade;
        fullGroup.blocksRaycasts = false;
        fullGroup.interactable = false;

        if (detailGroup != null)
            detailGroup.alpha *= fullFade;

        if (p >= 1f)
            CompleteClosing();
    }

    private void CompleteClosing()
    {
        state = MorphState.Closed;
        elapsed = 0f;

        RestoreHomePose();
        if (miniPackGroup != null)
        {
            miniPackGroup.alpha = homeAlpha;
            bool interactive = !BattlePauseController.IsPaused &&
                               runManager != null && runManager.RunActive &&
                               (runManager.State == BattleRunState.Combat || runManager.State == BattleRunState.Reward);
            miniPackGroup.blocksRaycasts = interactive;
            miniPackGroup.interactable = interactive;
        }

        if (fullGroup != null)
        {
            fullGroup.alpha = 0f;
            fullGroup.blocksRaycasts = false;
            fullGroup.interactable = false;
        }

        if (fullRoot != null)
            fullRoot.localScale = Vector3.one * (rewardMode ? rewardFullScale : combatFullScale);

        homePoseValid = false;
    }

    private Vector3 GetMiniPackCenterWorld()
    {
        return miniPackRoot != null
            ? miniPackRoot.TransformPoint(miniPackRoot.rect.center)
            : Vector3.zero;
    }

    private Vector3 GetBoardCenterWorld()
    {
        return boardRoot != null
            ? boardRoot.TransformPoint(boardRoot.rect.center)
            : GetMiniPackCenterWorld();
    }

    private Vector3 ComputeBoardCoverScale()
    {
        if (miniPackRoot == null || boardRoot == null)
            return homeLocalScale;

        float miniWorldWidth = Mathf.Max(0.0001f, WorldRectWidth(miniPackRoot));
        float miniWorldHeight = Mathf.Max(0.0001f, WorldRectHeight(miniPackRoot));
        float localX = Mathf.Max(0.0001f, Mathf.Abs(miniPackRoot.localScale.x));
        float localY = Mathf.Max(0.0001f, Mathf.Abs(miniPackRoot.localScale.y));

        float unitWorldWidth = miniWorldWidth / localX;
        float unitWorldHeight = miniWorldHeight / localY;
        float boardWorldWidth = Mathf.Max(0.0001f, WorldRectWidth(boardRoot));
        float boardWorldHeight = Mathf.Max(0.0001f, WorldRectHeight(boardRoot));

        float targetX = boardWorldWidth * targetBoardCoverage / unitWorldWidth;
        float targetY = boardWorldHeight * targetBoardCoverage / unitWorldHeight;
        float uniform = Mathf.Max(0.01f, Mathf.Min(targetX, targetY));

        float signX = homeLocalScale.x < 0f ? -1f : 1f;
        float signY = homeLocalScale.y < 0f ? -1f : 1f;
        return new Vector3(uniform * signX, uniform * signY, homeLocalScale.z);
    }

    private Quaternion ComputeBoardLocalRotation()
    {
        if (miniPackRoot == null || boardRoot == null || miniPackRoot.parent == null)
            return homeLocalRotation;

        return Quaternion.Inverse(miniPackRoot.parent.rotation) * boardRoot.rotation;
    }

    private void SetMiniPackVisualCenter(Vector3 centerWorld, Vector3 localScale, Quaternion localRotation)
    {
        if (miniPackRoot == null || miniPackRoot.parent == null)
            return;

        miniPackRoot.localScale = localScale;
        miniPackRoot.localRotation = localRotation;

        Vector3 localCenter = miniPackRoot.rect.center;
        Vector3 scaledCenter = Vector3.Scale(localCenter, localScale);
        Vector3 rotatedCenterInParent = localRotation * scaledCenter;
        Vector3 worldOffset = miniPackRoot.parent.TransformVector(rotatedCenterInParent);
        miniPackRoot.position = centerWorld - worldOffset;
    }

    private Vector3 GetWorldUpOffset(float localAmount)
    {
        if (miniPackRoot == null || miniPackRoot.parent == null)
            return Vector3.up * localAmount;
        return miniPackRoot.parent.TransformVector(new Vector3(0f, localAmount, 0f));
    }

    private static float WorldRectWidth(RectTransform rect)
    {
        if (rect == null)
            return 0f;
        Vector3 left = rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.center.y, 0f));
        Vector3 right = rect.TransformPoint(new Vector3(rect.rect.xMax, rect.rect.center.y, 0f));
        return Vector3.Distance(left, right);
    }

    private static float WorldRectHeight(RectTransform rect)
    {
        if (rect == null)
            return 0f;
        Vector3 bottom = rect.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.yMin, 0f));
        Vector3 top = rect.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.yMax, 0f));
        return Vector3.Distance(bottom, top);
    }

    private static float SmoothRange(float start, float end, float value)
    {
        if (end <= start + 0.0001f)
            return value >= end ? 1f : 0f;
        float t = Mathf.Clamp01((value - start) / (end - start));
        return t * t * (3f - 2f * t);
    }

    private static float EaseInOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float u = 1f - t;
        return 1f - u * u * u;
    }

    private static float EaseInCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }

    private static float EaseOutBack(float t, float strength)
    {
        t = Mathf.Clamp01(t);
        float c1 = 1.70158f * Mathf.Max(0.01f, strength);
        float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    private static bool ReadBool(FieldInfo field, object owner)
    {
        if (field == null || owner == null)
            return false;
        object value = field.GetValue(owner);
        return value is bool result && result;
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

    private static RectTransform FindChildRect(RectTransform root, string name)
    {
        if (root == null)
            return null;
        RectTransform[] all = root.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].name == name)
                return all[i];
        return null;
    }
}

public static class BattleInventoryMorphTransitionAutoInstaller
{
#if UNITY_EDITOR
    private static bool installQueued;

    [InitializeOnLoadMethod]
    private static void InitializeEditorInstaller()
    {
        EditorApplication.hierarchyChanged -= QueueInstall;
        EditorApplication.hierarchyChanged += QueueInstall;
        QueueInstall();
    }

    private static void QueueInstall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || installQueued)
            return;

        installQueued = true;
        EditorApplication.delayCall += EnsureEditorComponent;
    }

    private static void EnsureEditorComponent()
    {
        installQueued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null || EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded)
                continue;

            if (manager.GetComponent<BattleInventoryMorphTransitionController>() != null)
                continue;

            Undo.AddComponent<BattleInventoryMorphTransitionController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponent()
    {
        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleInventoryMorphTransitionController>() == null)
                manager.gameObject.AddComponent<BattleInventoryMorphTransitionController>();
        }
    }
}

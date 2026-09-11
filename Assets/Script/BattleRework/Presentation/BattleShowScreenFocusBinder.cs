using System.Reflection;
using UnityEngine;

/// <summary>
/// BattleShowFocusController의 사각형 Focus를 TV 외곽이 아니라 실제 활성 ScreenInner에 고정합니다.
/// Reward / Map 진입 시 ShowWorldSet의 도킹 타이밍이 늦더라도 Focus fade가 시작되도록 보강합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(25950)]
public sealed class BattleShowScreenFocusBinder : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string MountedTvName = "BattleShowMountedTV";
    private const string ScreenInnerName = "ScreenInner";

    private BattleRunManager runManager;
    private BattleShowFocusController focusController;

    private FieldInfo tvFocusRectField;
    private FieldInfo selectionShowRequestedField;
    private FieldInfo showStageBecameActiveAtField;

    private float nextResolveTime;

    private void Awake()
    {
        ResolveReferences();
        CacheFields();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheFields();
        nextResolveTime = 0f;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.08f;
            ResolveReferences();
            CacheFields();
        }

        if (focusController == null || runManager == null)
            return;

        bool showRequested = runManager.RunActive &&
            (runManager.State == BattleRunState.Reward || runManager.State == BattleRunState.SelectingNode);

        selectionShowRequestedField?.SetValue(focusController, showRequested);
        if (!showRequested)
            return;

        RectTransform screen = FindActiveMountedScreenInner();
        if (screen != null)
            tvFocusRectField?.SetValue(focusController, screen);

        // 기존 Focus Controller는 WorldSet.IsShowActive가 true가 될 때까지 시작 시간을 잡지 않았습니다.
        // 콘텐츠가 먼저 켜진 프레임에도 사각형 Focus가 확실히 들어오도록 요청 시점부터 시간을 보장합니다.
        if (showStageBecameActiveAtField != null)
        {
            object raw = showStageBecameActiveAtField.GetValue(focusController);
            float startedAt = raw is float value ? value : -1f;
            if (startedAt < 0f)
                showStageBecameActiveAtField.SetValue(focusController, Time.unscaledTime - 0.45f);
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (focusController == null)
            focusController = BattleShowFocusController.Instance != null
                ? BattleShowFocusController.Instance
                : FindFirstObjectByType<BattleShowFocusController>();
    }

    private void CacheFields()
    {
        if (tvFocusRectField != null)
            return;

        System.Type type = typeof(BattleShowFocusController);
        tvFocusRectField = type.GetField("tvFocusRect", PrivateInstance);
        selectionShowRequestedField = type.GetField("selectionShowRequested", PrivateInstance);
        showStageBecameActiveAtField = type.GetField("showStageBecameActiveAt", PrivateInstance);
    }

    private static RectTransform FindActiveMountedScreenInner()
    {
        RectTransform[] rects = UnityEngine.Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        RectTransform fallback = null;
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rect = rects[i];
            if (rect == null || rect.name != ScreenInnerName || !IsUnderMountedTv(rect))
                continue;

            if (fallback == null)
                fallback = rect;

            if (rect.gameObject.activeInHierarchy)
                return rect;
        }

        return fallback;
    }

    private static bool IsUnderMountedTv(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.name == MountedTvName)
                return true;
            current = current.parent;
        }
        return false;
    }
}

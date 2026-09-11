using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// BattleRework 내부의 Time.timeScale / Time.fixedDeltaTime 단일 writer입니다.
///
/// 각 시스템은 현재 값을 저장했다가 복구하지 않고, 자신의 owner key로 원하는 scale을 요청/해제합니다.
/// 동시에 여러 요청이 존재하면 가장 느린(scale이 가장 작은) 요청을 적용합니다.
/// 따라서 Inventory 0.05 상태에서 Pause 0이 들어오면 0이 되고,
/// Pause를 해제하면 Inventory 요청이 남아 있으므로 자동으로 0.05로 복귀합니다.
///
/// 새 HitStop / Cinematic도 Time.timeScale을 직접 수정하지 말고 이 API를 사용해야 합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-9000)]
public sealed class BattleTimeScaleController : MonoBehaviour
{
    public enum Owner
    {
        CombatInventory,
        RewardInventory,
        HitStop,
        Cinematic,
        StageTransition,
        Pause,
        Debug
    }

    private static BattleTimeScaleController instance;

    [Header("Base Time")]
    [SerializeField, Min(0.0001f)] private float normalFixedDeltaTime = 0.02f;

    private readonly Dictionary<Owner, float> requests = new();
    private float appliedScale = 1f;
    private bool baseFixedTimeCaptured;

    public static BattleTimeScaleController Instance => instance;
    public float AppliedScale => appliedScale;
    public bool HasAnyRequest => requests.Count > 0;

    public event Action<float> ScaleChanged;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        CaptureBaseFixedDeltaTime();
        ApplyResolvedScale(force: true);
    }

    private void OnEnable()
    {
        if (instance == null)
            instance = this;

        CaptureBaseFixedDeltaTime();
        ApplyResolvedScale(force: true);
    }

    private void LateUpdate()
    {
        // Phase 8 전환 중 남아 있을 수 있는 외부 writer보다 이 Owner가 최종 권한을 갖습니다.
        // 요청이 하나도 없을 때는 매 프레임 1x를 강제하지 않아 다른 시스템의 마이그레이션을 방해하지 않습니다.
        if (requests.Count > 0)
            ApplyResolvedScale(force: false);
    }

    private void OnDisable()
    {
        if (instance != this)
            return;

        requests.Clear();
        ApplyScale(1f, force: true);
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;

        requests.Clear();
        ApplyScale(1f, force: true);
        instance = null;
    }

    public void Request(Owner owner, float scale)
    {
        float clamped = Mathf.Clamp(scale, 0f, 1f);
        if (requests.TryGetValue(owner, out float current) && Mathf.Approximately(current, clamped))
            return;

        requests[owner] = clamped;
        ApplyResolvedScale(force: true);
    }

    public void Release(Owner owner)
    {
        if (!requests.Remove(owner))
            return;

        ApplyResolvedScale(force: true);
    }

    public bool HasRequest(Owner owner)
    {
        return requests.ContainsKey(owner);
    }

    public void ReleaseAll()
    {
        if (requests.Count == 0)
            return;

        requests.Clear();
        ApplyResolvedScale(force: true);
    }

    public static BattleTimeScaleController ResolveOrCreate(Component requester = null)
    {
        if (instance != null)
            return instance;

        BattleTimeScaleController existing = FindFirstObjectByType<BattleTimeScaleController>(FindObjectsInactive.Include);
        if (existing != null)
        {
            instance = existing;
            return existing;
        }

        BattleSceneManager sceneManager = FindFirstObjectByType<BattleSceneManager>(FindObjectsInactive.Include);
        if (sceneManager != null)
            return sceneManager.gameObject.AddComponent<BattleTimeScaleController>();

        if (requester != null && requester.gameObject != null)
            return requester.gameObject.AddComponent<BattleTimeScaleController>();

        GameObject host = new("BattleTimeScaleRuntime");
        return host.AddComponent<BattleTimeScaleController>();
    }

    private void CaptureBaseFixedDeltaTime()
    {
        if (baseFixedTimeCaptured)
            return;

        // 기존 코드가 이미 timeScale과 fixedDeltaTime을 함께 줄인 상태에서 설치되더라도
        // 원래 fixed step(대부분 0.02)을 최대한 복원합니다.
        if (Time.timeScale > 0.0001f && Time.fixedDeltaTime > 0.0001f)
            normalFixedDeltaTime = Mathf.Max(0.0001f, Time.fixedDeltaTime / Time.timeScale);
        else
            normalFixedDeltaTime = Mathf.Max(0.0001f, normalFixedDeltaTime);

        baseFixedTimeCaptured = true;
    }

    private void ApplyResolvedScale(bool force)
    {
        float target = 1f;
        foreach (KeyValuePair<Owner, float> pair in requests)
            target = Mathf.Min(target, pair.Value);

        ApplyScale(target, force);
    }

    private void ApplyScale(float scale, bool force)
    {
        float clamped = Mathf.Clamp(scale, 0f, 1f);
        bool changed = !Mathf.Approximately(appliedScale, clamped);
        bool engineMismatch = !Mathf.Approximately(Time.timeScale, clamped);

        if (!force && !changed && !engineMismatch)
            return;

        appliedScale = clamped;
        Time.timeScale = clamped;
        Time.fixedDeltaTime = clamped > 0f
            ? Mathf.Max(0.0001f, normalFixedDeltaTime * clamped)
            : Mathf.Max(0.0001f, normalFixedDeltaTime);

        if (changed)
            ScaleChanged?.Invoke(appliedScale);
    }
}

public static class BattleTimeScaleControllerAutoInstaller
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

            if (manager.GetComponent<BattleTimeScaleController>() != null)
                continue;

            Undo.AddComponent<BattleTimeScaleController>(manager.gameObject);
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
            if (manager != null && manager.GetComponent<BattleTimeScaleController>() == null)
                manager.gameObject.AddComponent<BattleTimeScaleController>();
        }
    }
}

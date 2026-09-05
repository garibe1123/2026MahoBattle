using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 다른 Scene -> BattleScene으로 전달되는 1회용 전투 요청 데이터입니다.
/// null/비어 있는 값은 BattleSceneManager의 Inspector 값, 그마저 없으면 BattleTestDefaults로 fallback 합니다.
/// </summary>
[Serializable]
public class BattleSceneRequest
{
    [Header("Scene")]
    public string battleSceneName = BattleSceneEntry.DefaultBattleSceneName;
    public string returnSceneName;
    public bool useCurrentSceneAsReturnScene = true;
    public bool autoStart = true;
    public bool autoReturnOnRunEnd;

    [Header("Run Override - null이면 BattleScene 기본값 사용")]
    public NodeGraphSO nodeGraph;
    public ClanDefinitionSO clan;
    public ShootingThemeSO shootingTheme;
    public PlayerSpriteSO playerSprite;

    [Header("Starting Equipment Override")]
    [Tooltip("켜면 startingEquipment을 이번 전투의 시작 장비로 사용합니다. 비어 있으면 안전상 BattleScene/Test Default로 fallback 합니다.")]
    public bool overrideStartingEquipment;
    public List<BattleEquipmentSO> startingEquipment = new();

    [Header("Core Loadout Override")]
    public bool overrideCoreLoadout;
    public CoreDefinitionSO mainCore;
    public List<CoreDefinitionSO> subCores = new();
    [Tooltip("-1이면 현재/기본 해금 슬롯 수를 유지합니다.")]
    public int unlockedSubCoreSlots = -1;

    public BattleSceneRequest Clone()
    {
        return new BattleSceneRequest
        {
            battleSceneName = battleSceneName,
            returnSceneName = returnSceneName,
            useCurrentSceneAsReturnScene = useCurrentSceneAsReturnScene,
            autoStart = autoStart,
            autoReturnOnRunEnd = autoReturnOnRunEnd,
            nodeGraph = nodeGraph,
            clan = clan,
            shootingTheme = shootingTheme,
            playerSprite = playerSprite,
            overrideStartingEquipment = overrideStartingEquipment,
            startingEquipment = startingEquipment != null
                ? new List<BattleEquipmentSO>(startingEquipment)
                : new List<BattleEquipmentSO>(),
            overrideCoreLoadout = overrideCoreLoadout,
            mainCore = mainCore,
            subCores = subCores != null
                ? new List<CoreDefinitionSO>(subCores)
                : new List<CoreDefinitionSO>(),
            unlockedSubCoreSlots = unlockedSubCoreSlots
        };
    }
}

/// <summary>
/// 전투 종료 후 이전 Scene이 읽을 수 있는 최소 결과 데이터입니다.
/// 정산 규칙이 확정되기 전이므로 현재는 RunEndReason과 방송/인기 지표만 전달합니다.
/// </summary>
[Serializable]
public class BattleSceneResult
{
    public RunEndReason endReason;
    public string battleSceneName;
    public string returnSceneName;
    public int popularity;
    public int fanPoints;
    public int viewers;
    public int likes;
}

/// <summary>
/// 모든 Scene에서 BattleScene으로 들어갈 때 사용하는 단일 Gateway입니다.
///
/// 코드:
/// BattleSceneEntry.Enter(new BattleSceneRequest { clan = clan, shootingTheme = theme });
///
/// Inspector/Button:
/// 이 컴포넌트를 버튼/포탈에 붙이고 EnterConfiguredBattle()을 호출합니다.
///
/// Request 우선순위:
/// BattleSceneEntry Request > BattleSceneManager Inspector > BattleTestDefaults
/// </summary>
public class BattleSceneEntry : MonoBehaviour
{
    public const string DefaultBattleSceneName = "BattleScene";

    [Header("Configured Entry")]
    [SerializeField] private BattleSceneRequest request = new();

    private static readonly BindingFlags FieldFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static BattleSceneRequest pendingRequest;
    private static BattleSceneResult lastResult;
    private static string activeReturnSceneName;
    private static bool sceneHookInstalled;

    private BattleSceneManager runtimeManager;
    private BattleSceneRequest runtimeRequest;
    private bool runtimeHost;
    private bool runEndSubscribed;

    public static bool HasPendingRequest => pendingRequest != null;
    public static bool HasResult => lastResult != null;
    public static string ActiveReturnSceneName => activeReturnSceneName;

    public void EnterConfiguredBattle()
    {
        Enter(request);
    }

    public void EnterDefaultBattle()
    {
        Enter(new BattleSceneRequest());
    }

    public static void EnterDefault()
    {
        Enter(new BattleSceneRequest());
    }

    /// <summary>
    /// 다른 Scene에서 BattleScene을 여는 공식 진입점입니다.
    /// 전달 객체는 즉시 Clone하여 호출 측의 후속 수정 영향을 받지 않습니다.
    /// </summary>
    public static void Enter(BattleSceneRequest sourceRequest)
    {
        BattleSceneRequest next = sourceRequest != null
            ? sourceRequest.Clone()
            : new BattleSceneRequest();

        if (string.IsNullOrWhiteSpace(next.battleSceneName))
            next.battleSceneName = DefaultBattleSceneName;

        Scene current = SceneManager.GetActiveScene();
        if (next.useCurrentSceneAsReturnScene && string.IsNullOrWhiteSpace(next.returnSceneName))
        {
            if (current.IsValid() && current.isLoaded && current.name != next.battleSceneName)
                next.returnSceneName = current.name;
        }

        pendingRequest = next;
        activeReturnSceneName = next.returnSceneName;
        lastResult = null;

        EnsureSceneHook();
        SceneManager.LoadScene(next.battleSceneName, LoadSceneMode.Single);
    }

    public static bool TryConsumeRequest(out BattleSceneRequest result)
    {
        if (pendingRequest == null)
        {
            result = null;
            return false;
        }

        result = pendingRequest;
        pendingRequest = null;

        if (!string.IsNullOrWhiteSpace(result.returnSceneName))
            activeReturnSceneName = result.returnSceneName;

        return true;
    }

    public static bool TryPeekRequest(out BattleSceneRequest result)
    {
        result = pendingRequest;
        return result != null;
    }

    public static void ClearPendingRequest()
    {
        pendingRequest = null;
    }

    public static void RecordResult(BattleSceneResult result)
    {
        lastResult = result;
        if (result != null && !string.IsNullOrWhiteSpace(result.returnSceneName))
            activeReturnSceneName = result.returnSceneName;
    }

    public static bool TryConsumeLastResult(out BattleSceneResult result)
    {
        if (lastResult == null)
        {
            result = null;
            return false;
        }

        result = lastResult;
        lastResult = null;
        return true;
    }

    public static bool TryPeekLastResult(out BattleSceneResult result)
    {
        result = lastResult;
        return result != null;
    }

    public static bool ReturnToPreviousScene()
    {
        if (string.IsNullOrWhiteSpace(activeReturnSceneName))
        {
            Debug.LogWarning("[BattleSceneEntry] No return scene was recorded.");
            return false;
        }

        SceneManager.LoadScene(activeReturnSceneName, LoadSceneMode.Single);
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetRuntimeState()
    {
        pendingRequest = null;
        lastResult = null;
        activeReturnSceneName = null;
        sceneHookInstalled = false;
        EnsureSceneHook();
    }

    private static void EnsureSceneHook()
    {
        if (sceneHookInstalled)
            return;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        sceneHookInstalled = true;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsBattleScene(scene))
            return;

        GameObject root = GameObject.Find("BattleSystems");
        if (root == null)
            root = new GameObject("BattleSystems");

        BattleSceneManager manager = UnityEngine.Object.FindFirstObjectByType<BattleSceneManager>();
        if (manager == null)
            manager = root.AddComponent<BattleSceneManager>();

        if (manager == null)
        {
            Debug.LogError("[BattleSceneEntry] BattleSceneManager could not be created.");
            return;
        }

        // BattleSceneManager의 기존 installer를 그대로 호출하여 동일한 hierarchy를 런타임에도 구성합니다.
        InvokeManagerInfrastructureRepair(manager);

        BattleSceneRequest consumed;
        if (!TryConsumeRequest(out consumed))
        {
            consumed = new BattleSceneRequest
            {
                battleSceneName = scene.name,
                useCurrentSceneAsReturnScene = false,
                returnSceneName = activeReturnSceneName,
                autoStart = true,
                autoReturnOnRunEnd = false
            };
        }

        // 가장 높은 우선순위인 Entry 값을 먼저 넣습니다.
        ApplyRequestOverrides(manager, consumed);
        manager.InstallOrRepairScene();

        // 그 다음 비어 있는 칸만 Test Default가 채웁니다.
        // 최소 BattleScene은 Manager가 sceneLoaded 시점에 생성되므로 기존 AfterSceneLoad callback보다
        // 늦을 수 있어 여기서 명시적으로 한 번 적용합니다.
        ApplyAvailableTestDefaults(manager);
        manager.InstallOrRepairScene();
        ApplyCoreLoadout(consumed);

        BattleSceneEntry host = root.GetComponent<BattleSceneEntry>();
        if (host == null)
            host = root.AddComponent<BattleSceneEntry>();

        host.BeginRuntimeHost(manager, consumed);
    }

    private void BeginRuntimeHost(BattleSceneManager manager, BattleSceneRequest consumed)
    {
        runtimeHost = true;
        runtimeManager = manager;
        runtimeRequest = consumed != null ? consumed.Clone() : new BattleSceneRequest();

        SubscribeRunEnd();
        StopAllCoroutines();
        StartCoroutine(WaitForReadyAndStart());
    }

    private IEnumerator WaitForReadyAndStart()
    {
        float timeoutAt = Time.realtimeSinceStartup + 5f;
        string lastReport = string.Empty;

        while (runtimeManager != null && Time.realtimeSinceStartup < timeoutAt)
        {
            runtimeManager.InstallOrRepairScene();
            if (runtimeManager.ValidateStartGate(out lastReport))
                break;

            yield return null;
        }

        if (runtimeManager == null)
            yield break;

        if (!runtimeManager.ValidateStartGate(out lastReport))
        {
            Debug.LogError(
                $"[BattleSceneEntry] BattleScene bootstrap timed out. START BLOCKED.\n{lastReport}",
                runtimeManager);
            yield break;
        }

        ApplyCoreLoadout(runtimeRequest);
        SubscribeRunEnd();

        if (runtimeRequest == null || runtimeRequest.autoStart)
            runtimeManager.TryStartRun();
    }

    private void SubscribeRunEnd()
    {
        if (!runtimeHost || runEndSubscribed || runtimeManager == null || runtimeManager.RunManager == null)
            return;

        runtimeManager.RunManager.RunEnded += HandleRunEnded;
        runEndSubscribed = true;
    }

    private void UnsubscribeRunEnd()
    {
        if (!runEndSubscribed || runtimeManager == null || runtimeManager.RunManager == null)
            return;

        runtimeManager.RunManager.RunEnded -= HandleRunEnded;
        runEndSubscribed = false;
    }

    private void HandleRunEnded(RunEndReason reason)
    {
        RunProgressSystem progress = runtimeManager != null && runtimeManager.RunManager != null
            ? runtimeManager.RunManager.Progress
            : null;

        BattleSceneResult result = new()
        {
            endReason = reason,
            battleSceneName = gameObject.scene.name,
            returnSceneName = runtimeRequest != null ? runtimeRequest.returnSceneName : activeReturnSceneName,
            popularity = progress != null ? progress.Popularity : 0,
            fanPoints = progress != null ? progress.FanPoints : 0,
            viewers = progress != null ? progress.Viewers : 0,
            likes = progress != null ? progress.Likes : 0
        };

        RecordResult(result);

        if (runtimeRequest != null && runtimeRequest.autoReturnOnRunEnd &&
            !string.IsNullOrWhiteSpace(result.returnSceneName))
        {
            StartCoroutine(ReturnNextFrame());
        }
    }

    private IEnumerator ReturnNextFrame()
    {
        yield return null;
        ReturnToPreviousScene();
    }

    private void OnDisable()
    {
        if (runtimeHost)
            UnsubscribeRunEnd();
    }

    private static void ApplyRequestOverrides(BattleSceneManager manager, BattleSceneRequest entry)
    {
        if (manager == null || entry == null)
            return;

        if (entry.nodeGraph != null)
            SetManagerField(manager, "nodeGraph", entry.nodeGraph);
        if (entry.clan != null)
            SetManagerField(manager, "clan", entry.clan);
        if (entry.shootingTheme != null)
            SetManagerField(manager, "shootingTheme", entry.shootingTheme);
        if (entry.playerSprite != null)
            SetManagerField(manager, "playerSprite", entry.playerSprite);

        // 현재 전투 구조는 스타터 무기 1개 이상을 요구하므로 빈 override는 fallback으로 처리합니다.
        if (entry.overrideStartingEquipment &&
            entry.startingEquipment != null &&
            entry.startingEquipment.Count > 0)
        {
            SetManagerField(
                manager,
                "startingEquipment",
                new List<BattleEquipmentSO>(entry.startingEquipment));
        }
    }

    private static void ApplyCoreLoadout(BattleSceneRequest entry)
    {
        if (entry == null || !entry.overrideCoreLoadout)
            return;

        PlayerLoadout loadout = UnityEngine.Object.FindFirstObjectByType<PlayerLoadout>();
        if (loadout == null)
            return;

        if (entry.unlockedSubCoreSlots >= 1)
            loadout.SetUnlockedSubCoreSlots(entry.unlockedSubCoreSlots);

        loadout.SetMainCore(entry.mainCore);
        loadout.ClearSubCores();

        if (entry.subCores == null)
            return;

        for (int i = 0; i < entry.subCores.Count; i++)
        {
            CoreDefinitionSO core = entry.subCores[i];
            if (core != null)
                loadout.TryAddSubCore(core);
        }
    }

    /// <summary>
    /// BattleTestDefaults의 기존 생성/적용 구현을 다시 만들지 않고 동일 구현을 재사용합니다.
    /// Reflection은 이 bootstrap 경계에서만 사용하고 실제 전투 런에는 관여하지 않습니다.
    /// </summary>
    private static void ApplyAvailableTestDefaults(BattleSceneManager manager)
    {
        if (manager == null || manager.ValidateStartGate(out _))
            return;

        Type defaultsType = typeof(BattleTestDefaults);
        MethodInfo loadMethod = defaultsType.GetMethod("LoadRuntimeBundle", StaticFlags);
        MethodInfo applyMethod = defaultsType.GetMethod("ApplyDefaults", StaticFlags);

        if (loadMethod == null || applyMethod == null)
        {
            Debug.LogError("[BattleSceneEntry] BattleTestDefaults API could not be resolved.");
            return;
        }

        object bundle = null;
        try
        {
            bundle = loadMethod.Invoke(null, null);
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(exception.InnerException ?? exception);
        }

        if (!IsTestBundleUsable(bundle))
        {
#if UNITY_EDITOR
            // 정상적으로는 InitializeOnLoad에서 미리 생성합니다. 여기서는 사용자가 스크립트 컴파일 직후
            // 바로 Play한 경우를 위한 최후의 Editor fallback입니다.
            MethodInfo buildMethod = defaultsType.GetMethod("BuildOrRefreshEditorBundle", StaticFlags);
            if (buildMethod != null && !EditorApplication.isCompiling)
            {
                try
                {
                    bundle = buildMethod.Invoke(null, null);
                }
                catch (TargetInvocationException exception)
                {
                    Debug.LogException(exception.InnerException ?? exception);
                }
            }
#endif
        }

        if (!IsTestBundleUsable(bundle))
        {
            Debug.LogWarning(
                "[BattleSceneEntry] Battle Test Defaults are unavailable. " +
                "Custom BattleSceneRequest/BattleSceneManager content must provide the missing values.");
            return;
        }

        try
        {
            applyMethod.Invoke(null, new[] { (object)manager, bundle, false });
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(exception.InnerException ?? exception);
        }
    }

    private static bool IsTestBundleUsable(object bundle)
    {
        if (bundle == null)
            return false;

        PropertyInfo property = bundle.GetType().GetProperty(
            "IsUsable",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        return property != null &&
               property.GetValue(bundle) is bool usable &&
               usable;
    }

    private static void InvokeManagerInfrastructureRepair(BattleSceneManager manager)
    {
        if (manager == null)
            return;

        MethodInfo method = typeof(BattleSceneManager).GetMethod(
            "CreateOnlyMissingSceneInfrastructure",
            FieldFlags);

        if (method == null)
        {
            Debug.LogError(
                "[BattleSceneEntry] BattleSceneManager runtime installer method was not found. " +
                "BattleSceneEntry and BattleSceneManager versions are out of sync.");
            return;
        }

        try
        {
            method.Invoke(manager, null);
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(exception.InnerException ?? exception);
        }
    }

    private static void SetManagerField(BattleSceneManager manager, string fieldName, object value)
    {
        FieldInfo field = typeof(BattleSceneManager).GetField(fieldName, FieldFlags);
        if (field == null)
        {
            Debug.LogError($"[BattleSceneEntry] BattleSceneManager field '{fieldName}' was not found.");
            return;
        }

        field.SetValue(manager, value);
    }

    private static bool IsBattleScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return false;

        if (pendingRequest != null &&
            !string.IsNullOrWhiteSpace(pendingRequest.battleSceneName) &&
            scene.name == pendingRequest.battleSceneName)
        {
            return true;
        }

        return scene.name == DefaultBattleSceneName;
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void InstallEditorSceneHook()
    {
        EditorSceneManager.sceneOpened -= HandleEditorSceneOpened;
        EditorSceneManager.sceneOpened += HandleEditorSceneOpened;

        EditorApplication.delayCall -= EnsureEditorDefaultAssets;
        EditorApplication.delayCall += EnsureEditorDefaultAssets;

        EditorApplication.delayCall -= RepairOpenedBattleScene;
        EditorApplication.delayCall += RepairOpenedBattleScene;
    }

    /// <summary>
    /// 다른 Scene에서 곧바로 BattleSceneEntry.Enter()를 호출해도 테스트 Resources가 존재하도록
    /// Play 전에 기본 Asset Bundle을 한 번 생성/갱신합니다.
    /// </summary>
    private static void EnsureEditorDefaultAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            return;

        MethodInfo buildMethod = typeof(BattleTestDefaults).GetMethod(
            "BuildOrRefreshEditorBundle",
            StaticFlags);

        if (buildMethod == null)
            return;

        try
        {
            buildMethod.Invoke(null, null);
        }
        catch (TargetInvocationException exception)
        {
            Debug.LogException(exception.InnerException ?? exception);
        }
    }

    private static void HandleEditorSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.name != DefaultBattleSceneName)
            return;

        EditorApplication.delayCall -= RepairOpenedBattleScene;
        EditorApplication.delayCall += RepairOpenedBattleScene;
    }

    private static void RepairOpenedBattleScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene active = SceneManager.GetActiveScene();
        if (!active.IsValid() || !active.isLoaded || active.name != DefaultBattleSceneName)
            return;

        GameObject root = GameObject.Find("BattleSystems");
        if (root == null)
            root = new GameObject("BattleSystems");

        BattleSceneManager manager = UnityEngine.Object.FindFirstObjectByType<BattleSceneManager>();
        if (manager == null)
        {
            manager = root.AddComponent<BattleSceneManager>();
            EditorSceneManager.MarkSceneDirty(active);
        }

        manager.InstallOrRepairScene();
    }
#endif
}

using System;
using System.Collections.Generic;
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
    [Tooltip("켜면 startingEquipment 리스트 자체를 이번 전투의 시작 장비로 사용합니다. 꺼져 있으면 BattleScene 기본값을 사용합니다.")]
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
/// 사용 방법 1 - 코드:
/// BattleSceneEntry.Enter(new BattleSceneRequest { clan = clan, shootingTheme = theme });
///
/// 사용 방법 2 - Inspector/Button:
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

    private static BattleSceneRequest pendingRequest;
    private static BattleSceneResult lastResult;
    private static string activeReturnSceneName;
    private static bool sceneHookInstalled;

    public static bool HasPendingRequest => pendingRequest != null;
    public static bool HasResult => lastResult != null;
    public static string ActiveReturnSceneName => activeReturnSceneName;

    /// <summary>Inspector/Button용 진입 함수.</summary>
    public void EnterConfiguredBattle()
    {
        Enter(request);
    }

    /// <summary>모든 값을 기본값에 맡기는 가장 단순한 테스트 진입.</summary>
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

    /// <summary>
    /// BattleSceneManager만 호출해야 하는 1회용 Consume API입니다.
    /// 한 번 읽은 Request는 제거되어 다음 전투에 이전 설정이 남지 않습니다.
    /// </summary>
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

    /// <summary>
    /// 가장 최근 Entry가 지정한 복귀 Scene으로 이동합니다.
    /// 결과 데이터는 Consume할 때까지 유지됩니다.
    /// </summary>
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

        // BattleScene 파일은 BattleSystems 하나만 있어도 됩니다.
        // 실제 Manager/Player/Camera/Pool은 BattleSceneManager가 self-repair 합니다.
        BattleSceneManager manager = UnityEngine.Object.FindFirstObjectByType<BattleSceneManager>();
        if (manager != null)
            return;

        GameObject root = GameObject.Find("BattleSystems");
        if (root == null)
            root = new GameObject("BattleSystems");

        root.AddComponent<BattleSceneManager>();
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
        EditorApplication.delayCall -= RepairOpenedBattleScene;
        EditorApplication.delayCall += RepairOpenedBattleScene;
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

        BattleSceneManager manager = UnityEngine.Object.FindFirstObjectByType<BattleSceneManager>();
        if (manager == null)
        {
            GameObject root = GameObject.Find("BattleSystems");
            if (root == null)
                root = new GameObject("BattleSystems");

            manager = root.AddComponent<BattleSceneManager>();
            EditorSceneManager.MarkSceneDirty(active);
        }

        manager.InstallOrRepairScene();
    }
#endif
}

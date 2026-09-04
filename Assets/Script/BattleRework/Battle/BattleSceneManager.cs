using System;
using System.Collections.Generic;
using System.Reflection;
using NavMeshPlus.Components;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 전투 씬의 설치/배선/시작 전 검증을 한 곳에서 관리하는 최상위 Scene Manager입니다.
///
/// 원칙:
/// - BattleSystems GameObject에는 이 컴포넌트 하나만 직접 추가하면 됩니다.
/// - RequireComponent + Editor Installer가 필수 시스템 컴포넌트/기본 Scene Root를 자동 생성합니다.
/// - NodeGraph/Starter Equipment/Reward Pool/Prefab 같은 실제 콘텐츠 데이터는 이 Manager가 중앙에서 보관합니다.
/// - Play Mode에서는 누락된 핵심 오브젝트를 몰래 생성하지 않습니다. 누락 상태면 Start를 차단합니다.
/// - 기존 시스템의 SerializedField는 호환을 위해 유지하고 이 Manager가 자동으로 동기화합니다.
///
/// 현재 Player 무기 계층은 PlayerShootingSystem/WeaponDisplay legacy bridge를 유지합니다.
/// WeaponSO 전환 시 이 Installer의 Player Weapon 부분만 교체하면 나머지 씬 구조는 유지할 수 있습니다.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(BattleRunManager))]
[RequireComponent(typeof(RunProgressSystem))]
[RequireComponent(typeof(BattleEquipmentSystem))]
[RequireComponent(typeof(BattleRewardSystem))]
[RequireComponent(typeof(FanMissionSystem))]
[RequireComponent(typeof(SynergyManager))]
[RequireComponent(typeof(RoomBaseTemplate))]
[RequireComponent(typeof(PlayerLoadout))]
public class BattleSceneManager : MonoBehaviour
{
    [Header("Installer")]
    [SerializeField] private bool autoInstallInEditor = true;
    [SerializeField] private bool createFallbackPlayer = true;
    [SerializeField] private bool createFallbackCamera = true;

    [Header("Required Run Content - 중앙 설정")]
    [SerializeField] private NodeGraphSO nodeGraph;
    [SerializeField] private ClanDefinitionSO clan;
    [SerializeField] private ShootingThemeSO shootingTheme;
    [SerializeField] private PlayerSpriteSO playerSprite;

    [Header("Required Equipment Content")]
    [SerializeField] private List<BattleEquipmentSO> startingEquipment = new();
    [SerializeField] private List<BattleEquipmentSO> rewardPool = new();

    [Header("Required Runtime Prefabs")]
    [SerializeField] private MonsterController monsterPrefab;
    [SerializeField] private Projectile playerProjectilePrefab;
    [SerializeField] private Projectile enemyProjectilePrefab;

    [Header("Core Systems - 자동 배치")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private RunProgressSystem progressSystem;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleRewardSystem rewardSystem;
    [SerializeField] private FanMissionSystem fanMissionSystem;
    [SerializeField] private SynergyManager synergyManager;
    [SerializeField] private RoomBaseTemplate roomBaseTemplate;
    [SerializeField] private PlayerLoadout playerLoadout;

    [Header("Scene Runtime - 자동 배치")]
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private MonsterPool monsterPool;
    [SerializeField] private NavMeshSurface navSurface;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerShootingSystem playerShootingSystem;
    [SerializeField] private ProjectilePooler playerProjectilePool;
    [SerializeField] private ProjectilePooler enemyProjectilePool;
    [SerializeField] private WeaponDisplay weaponDisplay;

    [Header("Generated Scene Roots")]
    [SerializeField] private Transform roomOrigin;
    [SerializeField] private Transform mapRoot;
    [SerializeField] private Transform obstacleRoot;
    [SerializeField] private Transform monsterRoot;
    [SerializeField] private Transform impactVfxRoot;
    [SerializeField] private Transform cameraShakeTarget;

    [Header("Runtime Validation")]
    [SerializeField, TextArea(5, 16)] private string lastValidationReport;

    public static BattleSceneManager Instance { get; private set; }

    public BattleRunManager RunManager => runManager;
    public BattleRoomManager RoomManager => roomManager;
    public PlayerController Player => playerController;
    public MonsterPool MonsterPool => monsterPool;
    public bool ReadyToStart => ValidateStartGate(out _);
    public string LastValidationReport => lastValidationReport;

    private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void Reset()
    {
        if (!Application.isPlaying)
            InstallOrRepairScene();
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            Instance = this;
            ResolveExistingReferences();
            PullExistingContentIfManagerIsEmpty();
            ApplyBindings();
            ValidateStartGate(out _);
            return;
        }

#if UNITY_EDITOR
        if (autoInstallInEditor)
            ScheduleEditorInstall();
#endif
    }

    private void OnDisable()
    {
        if (Application.isPlaying && Instance == this)
            Instance = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying && autoInstallInEditor)
            ScheduleEditorInstall();
    }

    private void ScheduleEditorInstall()
    {
        EditorApplication.delayCall -= DelayedEditorInstall;
        EditorApplication.delayCall += DelayedEditorInstall;
    }

    private void DelayedEditorInstall()
    {
        if (this == null || Application.isPlaying || !autoInstallInEditor)
            return;

        InstallOrRepairScene();
    }
#endif

    [ContextMenu("Install / Repair Battle Scene")]
    public void InstallOrRepairScene()
    {
        ResolveCoreComponents();

        if (!Application.isPlaying)
            CreateMissingSceneInfrastructure();

        ResolveExistingReferences();
        PullExistingContentIfManagerIsEmpty();
        ApplyBindings();
        ValidateStartGate(out _);
        MarkSceneDirty();
    }

    [ContextMenu("Validate Battle Start")]
    public void ValidateFromContextMenu()
    {
        ResolveExistingReferences();
        PullExistingContentIfManagerIsEmpty();
        ApplyBindings();

        if (ValidateStartGate(out string report))
            Debug.Log("[BattleScene] READY TO START.", this);
        else
            Debug.LogError($"[BattleScene] START BLOCKED.\n{report}", this);
    }

    public bool TryStartRun()
    {
        ResolveExistingReferences();
        ApplyBindings();

        if (!ValidateStartGate(out string report))
        {
            Debug.LogError($"[BattleScene] Cannot start run. Fix the required setup first.\n{report}", this);
            return false;
        }

        if (runManager == null)
            return false;

        runManager.StartRun();
        return runManager.RunActive;
    }

    /// <summary>
    /// BattleRunManager.StartRun()에서 호출하는 강제 시작 Gate입니다.
    /// BattleRunManager.ValidateConfiguration()을 다시 호출하지 않아 재귀하지 않습니다.
    /// </summary>
    public bool ValidateStartGate(out string report)
    {
        List<string> errors = new();

        ValidateSingleton<BattleSceneManager>(errors, "BattleSceneManager");
        ValidateSingleton<BattleRunManager>(errors, "BattleRunManager");
        ValidateSingleton<BattleRoomManager>(errors, "BattleRoomManager");
        ValidateSingleton<BattleEquipmentSystem>(errors, "BattleEquipmentSystem");
        ValidateSingleton<BattleRewardSystem>(errors, "BattleRewardSystem");
        ValidateSingleton<MonsterPool>(errors, "MonsterPool");
        ValidateSingleton<PlayerController>(errors, "PlayerController");

        if (runManager == null) errors.Add("BattleRunManager is missing.");
        if (progressSystem == null) errors.Add("RunProgressSystem is missing.");
        if (equipmentSystem == null) errors.Add("BattleEquipmentSystem is missing.");
        if (rewardSystem == null) errors.Add("BattleRewardSystem is missing.");
        if (fanMissionSystem == null) errors.Add("FanMissionSystem is missing.");
        if (synergyManager == null) errors.Add("SynergyManager is missing.");
        if (roomBaseTemplate == null) errors.Add("RoomBaseTemplate is missing.");
        if (playerLoadout == null) errors.Add("PlayerLoadout is missing.");

        if (roomManager == null) errors.Add("BattleRoomManager is missing.");
        if (navSurface == null) errors.Add("NavMeshSurface is missing.");
        if (monsterPool == null) errors.Add("MonsterPool is missing.");
        if (playerController == null) errors.Add("PlayerController is missing.");
        if (playerShootingSystem == null) errors.Add("PlayerShootingSystem is missing (legacy weapon bridge until WeaponSO migration).");
        if (playerProjectilePool == null) errors.Add("Player ProjectilePooler is missing.");
        if (enemyProjectilePool == null) errors.Add("Enemy ProjectilePooler is missing.");

        if (roomOrigin == null) errors.Add("RoomOrigin is missing.");
        if (mapRoot == null) errors.Add("MapRoot is missing.");
        if (obstacleRoot == null) errors.Add("ObstacleRoot is missing.");
        if (monsterRoot == null) errors.Add("MonsterRoot is missing.");

        if (Camera.main == null)
            errors.Add("No Camera tagged MainCamera exists.");

        if (nodeGraph == null)
        {
            errors.Add("NodeGraphSO is not assigned on BattleSceneManager.");
        }
        else if (!nodeGraph.ValidateGraph(out string graphReport))
        {
            errors.Add($"NodeGraph invalid:\n{graphReport}");
        }

        if (playerSprite == null)
            errors.Add("PlayerSpriteSO is not assigned. Current PlayerAnimator requires it.");

        if (monsterPrefab == null)
            errors.Add("MonsterController prefab is not assigned.");

        if (playerProjectilePrefab == null)
            errors.Add("Player Projectile prefab is not assigned.");

        if (enemyProjectilePrefab == null)
            errors.Add("Enemy Projectile prefab is not assigned.");

        if (roomManager != null && !roomManager.ValidateConfiguration(out string roomReport))
            errors.Add($"BattleRoomManager invalid:\n{roomReport}");

        if (monsterPool != null && !monsterPool.ValidateConfiguration(out string monsterPoolReport))
            errors.Add($"MonsterPool invalid:\n{monsterPoolReport}");

        if (equipmentSystem != null && !equipmentSystem.ValidateConfiguration(out string equipmentReport))
            errors.Add($"BattleEquipmentSystem invalid:\n{equipmentReport}");

        if (rewardSystem != null && !rewardSystem.ValidateConfiguration(out string rewardReport))
            errors.Add($"BattleRewardSystem invalid:\n{rewardReport}");

        if (playerProjectilePool != null && playerProjectilePool.projectilePrefab == null)
            errors.Add("Player ProjectilePooler.projectilePrefab is null.");

        if (enemyProjectilePool != null && enemyProjectilePool.projectilePrefab == null)
            errors.Add("Enemy ProjectilePooler.projectilePrefab is null.");

        lastValidationReport = errors.Count == 0
            ? "READY"
            : string.Join("\n", errors);

        report = lastValidationReport;
        return errors.Count == 0;
    }

    private void ResolveCoreComponents()
    {
        runManager = GetOrAddComponent<BattleRunManager>(gameObject, !Application.isPlaying);
        progressSystem = GetOrAddComponent<RunProgressSystem>(gameObject, !Application.isPlaying);
        equipmentSystem = GetOrAddComponent<BattleEquipmentSystem>(gameObject, !Application.isPlaying);
        rewardSystem = GetOrAddComponent<BattleRewardSystem>(gameObject, !Application.isPlaying);
        fanMissionSystem = GetOrAddComponent<FanMissionSystem>(gameObject, !Application.isPlaying);
        synergyManager = GetOrAddComponent<SynergyManager>(gameObject, !Application.isPlaying);
        roomBaseTemplate = GetOrAddComponent<RoomBaseTemplate>(gameObject, !Application.isPlaying);
        playerLoadout = GetOrAddComponent<PlayerLoadout>(gameObject, !Application.isPlaying);
    }

    private void ResolveExistingReferences()
    {
        ResolveCoreComponents();

        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (monsterPool == null)
            monsterPool = FindFirstObjectByType<MonsterPool>();
        if (navSurface == null)
            navSurface = FindFirstObjectByType<NavMeshSurface>();
        if (playerController == null)
            playerController = FindFirstObjectByType<PlayerController>();
        if (playerShootingSystem == null)
            playerShootingSystem = FindFirstObjectByType<PlayerShootingSystem>();

        ProjectilePooler[] pools = FindObjectsByType<ProjectilePooler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < pools.Length; i++)
        {
            ProjectilePooler pool = pools[i];
            if (pool == null) continue;

            string lower = pool.name.ToLowerInvariant();
            if (playerProjectilePool == null && lower.Contains("player"))
                playerProjectilePool = pool;
            else if (enemyProjectilePool == null && (lower.Contains("enemy") || lower.Contains("monster")))
                enemyProjectilePool = pool;
        }

        if (weaponDisplay == null && playerController != null)
            weaponDisplay = playerController.GetComponentInChildren<WeaponDisplay>(true);

        if (roomManager != null)
        {
            if (roomOrigin == null) roomOrigin = FindChildRecursive(roomManager.transform, "RoomOrigin");
            if (mapRoot == null) mapRoot = FindChildRecursive(roomManager.transform, "MapRoot");
            if (obstacleRoot == null) obstacleRoot = FindChildRecursive(roomManager.transform, "ObstacleRoot");
            if (monsterRoot == null) monsterRoot = FindChildRecursive(roomManager.transform, "MonsterRoot");
            if (impactVfxRoot == null) impactVfxRoot = FindChildRecursive(roomManager.transform, "ImpactVFXRoot");
        }

        if (cameraShakeTarget == null && Camera.main != null)
            cameraShakeTarget = Camera.main.transform;
    }

    private void CreateMissingSceneInfrastructure()
    {
        GameObject roomObject = GetOrCreateChildObject(gameObject, "RoomSystem");
        roomManager = GetOrAddComponent<BattleRoomManager>(roomObject, true);

        roomOrigin = GetOrCreateChild(roomObject.transform, "RoomOrigin");
        mapRoot = GetOrCreateChild(roomObject.transform, "MapRoot");
        obstacleRoot = GetOrCreateChild(roomObject.transform, "ObstacleRoot");
        monsterRoot = GetOrCreateChild(roomObject.transform, "MonsterRoot");
        impactVfxRoot = GetOrCreateChild(roomObject.transform, "ImpactVFXRoot");

        GameObject navigationObject = GetOrCreateChildObject(gameObject, "Navigation");
        navSurface = GetOrAddComponent<NavMeshSurface>(navigationObject, true);

        GameObject projectilePoolsObject = GetOrCreateChildObject(gameObject, "ProjectilePools");
        GameObject playerPoolObject = GetOrCreateChildObject(projectilePoolsObject, "PlayerProjectilePool");
        GameObject enemyPoolObject = GetOrCreateChildObject(projectilePoolsObject, "EnemyProjectilePool");
        playerProjectilePool = GetOrAddComponent<ProjectilePooler>(playerPoolObject, true);
        enemyProjectilePool = GetOrAddComponent<ProjectilePooler>(enemyPoolObject, true);
        playerProjectilePool.initialSize = Mathf.Max(16, playerProjectilePool.initialSize);
        enemyProjectilePool.initialSize = Mathf.Max(16, enemyProjectilePool.initialSize);

        GameObject monsterPoolObject = GetOrCreateChildObject(gameObject, "MonsterPool");
        monsterPool = GetOrAddComponent<MonsterPool>(monsterPoolObject, true);

        if (createFallbackPlayer)
            CreateOrRepairPlayer();
        else if (playerController == null)
            playerController = FindFirstObjectByType<PlayerController>();

        if (createFallbackCamera && Camera.main == null)
            CreateFallbackMainCamera();
    }

    private void CreateOrRepairPlayer()
    {
        if (playerController != null)
            return;

        playerController = FindFirstObjectByType<PlayerController>();
        GameObject playerObject;

        if (playerController != null)
        {
            playerObject = playerController.gameObject;
        }
        else
        {
            playerObject = GetOrCreateChildObject(gameObject, "Player");

            Rigidbody2D body = GetOrAddComponent<Rigidbody2D>(playerObject, true);
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            if (playerObject.GetComponent<Collider2D>() == null)
            {
                CircleCollider2D collider = playerObject.AddComponent<CircleCollider2D>();
                collider.radius = 0.35f;
            }

            SpriteRenderer bodyRenderer = GetOrAddComponent<SpriteRenderer>(playerObject, true);
            bodyRenderer.sortingOrder = Mathf.Max(bodyRenderer.sortingOrder, 20);

            GetOrAddComponent<PlayerAnimator>(playerObject, true);
            playerShootingSystem = GetOrAddComponent<PlayerShootingSystem>(playerObject, true);
            playerController = GetOrAddComponent<PlayerController>(playerObject, true);

            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0)
                playerObject.layer = playerLayer;
            playerObject.tag = "Player";
        }

        GetOrAddComponent<SpriteRenderer>(playerObject, true);
        GetOrAddComponent<PlayerAnimator>(playerObject, true);
        playerShootingSystem = GetOrAddComponent<PlayerShootingSystem>(playerObject, true);

        Transform weaponPivot = GetOrCreateChild(playerObject.transform, "WeaponPivot");
        Transform weaponVisual = GetOrCreateChild(weaponPivot, "WeaponVisual");
        SpriteRenderer weaponRenderer = GetOrAddComponent<SpriteRenderer>(weaponVisual.gameObject, true);
        weaponRenderer.sortingOrder = Mathf.Max(weaponRenderer.sortingOrder, 30);
        weaponDisplay = GetOrAddComponent<WeaponDisplay>(weaponVisual.gameObject, true);
        weaponDisplay.pivot = weaponPivot;
        weaponDisplay.weaponSpriteRenderer = weaponRenderer;
    }

    private void CreateFallbackMainCamera()
    {
        GameObject pivotObject = GetOrCreateChildObject(gameObject, "CameraShakePivot");
        GameObject cameraObject = GetOrCreateChildObject(pivotObject, "Main Camera");
        Camera camera = GetOrAddComponent<Camera>(cameraObject, true);
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(5f, camera.orthographicSize);
        cameraObject.tag = "MainCamera";
        cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);

        if (FindFirstObjectByType<AudioListener>() == null)
            cameraObject.AddComponent<AudioListener>();

        cameraShakeTarget = pivotObject.transform;
    }

    private void PullExistingContentIfManagerIsEmpty()
    {
        if (runManager != null)
        {
            nodeGraph ??= GetPrivateField<NodeGraphSO>(runManager, "nodeGraph");
            clan ??= GetPrivateField<ClanDefinitionSO>(runManager, "clan");
            shootingTheme ??= GetPrivateField<ShootingThemeSO>(runManager, "shootingTheme");
        }

        if ((startingEquipment == null || startingEquipment.Count == 0) && equipmentSystem != null)
        {
            List<BattleEquipmentSO> existing = GetPrivateField<List<BattleEquipmentSO>>(equipmentSystem, "startingEquipment");
            if (existing != null && existing.Count > 0)
                startingEquipment = new List<BattleEquipmentSO>(existing);
        }

        if ((rewardPool == null || rewardPool.Count == 0) && rewardSystem != null)
        {
            List<BattleEquipmentSO> existing = GetPrivateField<List<BattleEquipmentSO>>(rewardSystem, "rewardPool");
            if (existing != null && existing.Count > 0)
                rewardPool = new List<BattleEquipmentSO>(existing);
        }

        if (monsterPrefab == null && monsterPool != null)
            monsterPrefab = GetPrivateField<MonsterController>(monsterPool, "monsterPrefab");

        if (playerProjectilePrefab == null && playerProjectilePool != null)
            playerProjectilePrefab = playerProjectilePool.projectilePrefab;

        if (enemyProjectilePrefab == null && enemyProjectilePool != null)
            enemyProjectilePrefab = enemyProjectilePool.projectilePrefab;

        if (playerSprite == null && playerController != null)
            playerSprite = playerController.spriteSO;
    }

    private void ApplyBindings()
    {
        if (runManager != null)
        {
            SetPrivateField(runManager, "nodeGraph", nodeGraph);
            SetPrivateField(runManager, "clan", clan);
            SetPrivateField(runManager, "shootingTheme", shootingTheme);
            SetPrivateField(runManager, "roomManager", roomManager);
            SetPrivateField(runManager, "progress", progressSystem);
            SetPrivateField(runManager, "rewardSystem", rewardSystem);
            SetPrivateField(runManager, "equipmentSystem", equipmentSystem);
            SetPrivateField(runManager, "fanMissionSystem", fanMissionSystem);
            SetPrivateField(runManager, "playerController", playerController);
            SetPrivateField(runManager, "sceneManager", this);
        }

        if (roomManager != null)
        {
            SetPrivateField(roomManager, "roomOrigin", roomOrigin != null ? roomOrigin : roomManager.transform);
            SetPrivateField(roomManager, "mapRoot", mapRoot);
            SetPrivateField(roomManager, "obstacleRoot", obstacleRoot);
            SetPrivateField(roomManager, "monsterRoot", monsterRoot);
            SetPrivateField(roomManager, "playerTarget", playerController != null ? playerController.transform : null);
            SetPrivateField(roomManager, "navSurface", navSurface);
            SetPrivateField(roomManager, "monsterPool", monsterPool);
            SetPrivateField(roomManager, "impactVfxRoot", impactVfxRoot != null ? impactVfxRoot : mapRoot);
            SetPrivateField(roomManager, "cameraShakeTarget", cameraShakeTarget);
        }

        if (monsterPool != null)
        {
            SetPrivateField(monsterPool, "monsterPrefab", monsterPrefab);
            SetPrivateField(monsterPool, "enemyProjectilePool", enemyProjectilePool);
        }

        if (playerProjectilePool != null)
            playerProjectilePool.projectilePrefab = playerProjectilePrefab;
        if (enemyProjectilePool != null)
            enemyProjectilePool.projectilePrefab = enemyProjectilePrefab;

        if (playerController != null)
        {
            playerController.spriteSO = playerSprite;
            playerController.shootingSystem = playerShootingSystem;
            SetPrivateField(playerController, "runManager", runManager);
        }

        if (playerShootingSystem != null)
        {
            playerShootingSystem.playerProjectilePool = playerProjectilePool;
            playerShootingSystem.weaponDisplay = weaponDisplay;
        }

        if (equipmentSystem != null)
        {
            SetPrivateField(equipmentSystem, "shootingSystem", playerShootingSystem);
            if (startingEquipment != null && startingEquipment.Count > 0)
                SetPrivateField(equipmentSystem, "startingEquipment", new List<BattleEquipmentSO>(startingEquipment));
        }

        if (rewardSystem != null && rewardPool != null && rewardPool.Count > 0)
            SetPrivateField(rewardSystem, "rewardPool", new List<BattleEquipmentSO>(rewardPool));

        if (fanMissionSystem != null)
            SetPrivateField(fanMissionSystem, "runProgress", progressSystem);

        if (synergyManager != null)
        {
            SetPrivateField(synergyManager, "equipmentSystem", equipmentSystem);
            SetPrivateField(synergyManager, "effectRoot", transform);
            SetPrivateField(synergyManager, "defaultEffectOrigin", playerController != null ? playerController.transform : null);
        }

        if (roomBaseTemplate != null)
        {
            SetPrivateField(roomBaseTemplate, "runManager", runManager);
            SetPrivateField(roomBaseTemplate, "roomManager", roomManager);
            SetPrivateField(roomBaseTemplate, "baseOrigin", roomOrigin);
            SetPrivateField(roomBaseTemplate, "baseRoot", mapRoot);
        }

        BattleTestBootstrap bootstrap = FindFirstObjectByType<BattleTestBootstrap>();
        if (bootstrap != null)
        {
            SetPrivateField(bootstrap, "sceneManager", this);
            SetPrivateField(bootstrap, "runManager", runManager);
            SetPrivateField(bootstrap, "roomManager", roomManager);
            SetPrivateField(bootstrap, "monsterPool", monsterPool);
            SetPrivateField(bootstrap, "equipmentSystem", equipmentSystem);
            SetPrivateField(bootstrap, "rewardSystem", rewardSystem);
            SetPrivateField(bootstrap, "synergyManager", synergyManager);
            SetPrivateField(bootstrap, "roomBaseTemplate", roomBaseTemplate);
        }

        MarkSceneDirty();
    }

    private static void ValidateSingleton<T>(List<string> errors, string displayName) where T : Component
    {
        T[] all = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all.Length > 1)
            errors.Add($"Duplicate {displayName}: {all.Length} instances found. Keep exactly one active battle system instance.");
    }

    private static GameObject GetOrCreateChildObject(GameObject parent, string childName)
    {
        Transform child = parent.transform.Find(childName);
        if (child != null)
            return child.gameObject;

        GameObject created = new(childName);
        created.transform.SetParent(parent.transform, false);
        return created;
    }

    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
            return child;

        GameObject created = new(childName);
        created.transform.SetParent(parent, false);
        return created.transform;
    }

    private static Transform FindChildRecursive(Transform root, string targetName)
    {
        if (root == null)
            return null;

        Transform direct = root.Find(targetName);
        if (direct != null)
            return direct;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), targetName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static T GetOrAddComponent<T>(GameObject target, bool allowAdd) where T : Component
    {
        if (target == null)
            return null;

        T component = target.GetComponent<T>();
        if (component != null || !allowAdd)
            return component;

        return target.AddComponent<T>();
    }

    private static T GetPrivateField<T>(object target, string fieldName) where T : class
    {
        if (target == null)
            return null;

        FieldInfo field = target.GetType().GetField(fieldName, FieldFlags);
        return field != null ? field.GetValue(target) as T : null;
    }

    private static bool SetPrivateField(object target, string fieldName, object value)
    {
        if (target == null)
            return false;

        FieldInfo field = target.GetType().GetField(fieldName, FieldFlags);
        if (field == null)
        {
            Debug.LogError($"[BattleScene] Auto-wire failed. Field '{fieldName}' was not found on {target.GetType().Name}.");
            return false;
        }

        field.SetValue(target, value);
        MarkObjectDirty(target as UnityEngine.Object);
        return true;
    }

    private static void MarkObjectDirty(UnityEngine.Object target)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && target != null)
            EditorUtility.SetDirty(target);
#endif
    }

    private void MarkSceneDirty()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && gameObject.scene.IsValid())
        {
            EditorUtility.SetDirty(this);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }
}

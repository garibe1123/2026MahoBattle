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
/// 전투 씬의 설치 / 자동 배선 / 시작 전 검증을 담당하는 최상위 Manager.
///
/// BattleSystems GameObject에 이 컴포넌트 하나를 추가하면:
/// 1) Core System 컴포넌트는 RequireComponent로 자동 배치됩니다.
/// 2) Edit Mode에서 RoomSystem / Navigation / Pool / Player / Camera 기본 구조를 자동 보수합니다.
/// 3) 기존 씬에 이미 있는 시스템은 새로 만들지 않고 우선 재사용합니다.
/// 4) NodeGraph / Starter Equipment / Reward Pool / Runtime Prefab은 이 Manager를 중앙 설정점으로 사용합니다.
/// 5) Play Mode에서는 누락된 핵심 구조를 몰래 생성하지 않고 START BLOCKED 처리합니다.
///
/// 현재 Player 무기 계층은 WeaponSO 마이그레이션 전까지
/// PlayerShootingSystem + WeaponDisplay를 legacy runtime bridge로 유지합니다.
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
    [SerializeField, HideInInspector] private bool importedLegacyContent;

    [Header("Required Run Content - 중앙 설정")]
    [SerializeField] private NodeGraphSO nodeGraph;
    [SerializeField] private ClanDefinitionSO clan;
    [SerializeField] private ShootingThemeSO shootingTheme;
    [SerializeField] private PlayerSpriteSO playerSprite;

    [Header("Required Equipment Content - 중앙 설정")]
    [SerializeField] private List<BattleEquipmentSO> startingEquipment = new();
    [SerializeField] private List<BattleEquipmentSO> rewardPool = new();

    [Header("Required Runtime Prefabs - 중앙 설정")]
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
            ResolveCoreComponents(false);
            ResolveExistingReferences();
            ImportExistingContentOnce();
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
        bool allowCreate = !Application.isPlaying;

        ResolveCoreComponents(allowCreate);
        ResolveExistingReferences();

        if (allowCreate)
        {
            CreateOnlyMissingSceneInfrastructure();
            ResolveExistingReferences();
        }

        ImportExistingContentOnce();
        ApplyBindings();
        ValidateStartGate(out _);
        MarkSceneDirty();
    }

    [ContextMenu("Validate Battle Start")]
    public void ValidateFromContextMenu()
    {
        ResolveCoreComponents(false);
        ResolveExistingReferences();
        ApplyBindings();

        if (ValidateStartGate(out string report))
            Debug.Log("[BattleScene] READY TO START.", this);
        else
            Debug.LogError($"[BattleScene] START BLOCKED.\n{report}", this);
    }

    public bool TryStartRun()
    {
        ResolveCoreComponents(false);
        ResolveExistingReferences();
        ApplyBindings();

        if (!ValidateStartGate(out string report))
        {
            Debug.LogError($"[BattleScene] Cannot start run. Fix required setup first.\n{report}", this);
            return false;
        }

        if (runManager == null)
            return false;

        runManager.StartRun();
        return runManager.RunActive;
    }

    /// <summary>
    /// BattleRunManager.StartRun()의 강제 Gate.
    /// 이 메서드는 BattleRunManager.ValidateConfiguration()을 호출하지 않으므로 재귀하지 않습니다.
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
        if (playerShootingSystem == null) errors.Add("PlayerShootingSystem is missing (legacy bridge until WeaponSO migration).");
        if (playerProjectilePool == null) errors.Add("Player ProjectilePooler is missing.");
        if (enemyProjectilePool == null) errors.Add("Enemy ProjectilePooler is missing.");
        if (weaponDisplay == null) errors.Add("WeaponDisplay is missing (legacy bridge until WeaponSO migration).");

        if (roomOrigin == null) errors.Add("RoomOrigin is missing.");
        if (mapRoot == null) errors.Add("MapRoot is missing.");
        if (obstacleRoot == null) errors.Add("ObstacleRoot is missing.");
        if (monsterRoot == null) errors.Add("MonsterRoot is missing.");
        if (impactVfxRoot == null) errors.Add("ImpactVFXRoot is missing.");

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

        if (playerController != null)
        {
            if (playerController.GetComponent<Rigidbody2D>() == null)
                errors.Add("Player Rigidbody2D is missing.");
            if (playerController.GetComponent<Collider2D>() == null)
                errors.Add("Player Collider2D is missing.");
            if (playerController.GetComponent<SpriteRenderer>() == null)
                errors.Add("Player SpriteRenderer is missing.");
            if (playerController.GetComponent<PlayerAnimator>() == null)
                errors.Add("PlayerAnimator is missing.");
        }

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

    private void ResolveCoreComponents(bool allowCreate)
    {
        runManager = GetOrAddComponent<BattleRunManager>(gameObject, allowCreate);
        progressSystem = GetOrAddComponent<RunProgressSystem>(gameObject, allowCreate);
        equipmentSystem = GetOrAddComponent<BattleEquipmentSystem>(gameObject, allowCreate);
        rewardSystem = GetOrAddComponent<BattleRewardSystem>(gameObject, allowCreate);
        fanMissionSystem = GetOrAddComponent<FanMissionSystem>(gameObject, allowCreate);
        synergyManager = GetOrAddComponent<SynergyManager>(gameObject, allowCreate);
        roomBaseTemplate = GetOrAddComponent<RoomBaseTemplate>(gameObject, allowCreate);
        playerLoadout = GetOrAddComponent<PlayerLoadout>(gameObject, allowCreate);
    }

    private void ResolveExistingReferences()
    {
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (monsterPool == null)
            monsterPool = FindFirstObjectByType<MonsterPool>();
        if (navSurface == null)
            navSurface = FindFirstObjectByType<NavMeshSurface>();
        if (playerController == null)
            playerController = FindFirstObjectByType<PlayerController>();
        if (playerShootingSystem == null && playerController != null)
            playerShootingSystem = playerController.GetComponent<PlayerShootingSystem>();
        if (playerShootingSystem == null)
            playerShootingSystem = FindFirstObjectByType<PlayerShootingSystem>();

        ResolveProjectilePools();

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
        {
            Transform cameraTransform = Camera.main.transform;
            cameraShakeTarget = cameraTransform.parent != null && cameraTransform.parent.name == "CameraShakePivot"
                ? cameraTransform.parent
                : cameraTransform;
        }
    }

    private void ResolveProjectilePools()
    {
        ProjectilePooler[] pools = FindObjectsByType<ProjectilePooler>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < pools.Length; i++)
        {
            ProjectilePooler pool = pools[i];
            if (pool == null) continue;

            string lower = pool.name.ToLowerInvariant();
            if (playerProjectilePool == null && lower.Contains("player"))
            {
                playerProjectilePool = pool;
                continue;
            }

            if (enemyProjectilePool == null && (lower.Contains("enemy") || lower.Contains("monster")))
                enemyProjectilePool = pool;
        }

        if (playerProjectilePool == null && pools.Length > 0)
            playerProjectilePool = pools[0];

        if (enemyProjectilePool == null)
        {
            for (int i = 0; i < pools.Length; i++)
            {
                if (pools[i] != null && pools[i] != playerProjectilePool)
                {
                    enemyProjectilePool = pools[i];
                    break;
                }
            }
        }
    }

    private void CreateOnlyMissingSceneInfrastructure()
    {
        if (roomManager == null)
        {
            GameObject roomObject = GetOrCreateChildObject(gameObject, "RoomSystem");
            roomManager = GetOrAddComponent<BattleRoomManager>(roomObject, true);
        }

        if (roomManager != null)
        {
            if (roomOrigin == null) roomOrigin = GetOrCreateChild(roomManager.transform, "RoomOrigin");
            if (mapRoot == null) mapRoot = GetOrCreateChild(roomManager.transform, "MapRoot");
            if (obstacleRoot == null) obstacleRoot = GetOrCreateChild(roomManager.transform, "ObstacleRoot");
            if (monsterRoot == null) monsterRoot = GetOrCreateChild(roomManager.transform, "MonsterRoot");
            if (impactVfxRoot == null) impactVfxRoot = GetOrCreateChild(roomManager.transform, "ImpactVFXRoot");
        }

        if (navSurface == null)
        {
            GameObject navigationObject = GetOrCreateChildObject(gameObject, "Navigation");
            navSurface = GetOrAddComponent<NavMeshSurface>(navigationObject, true);
        }

        GameObject projectilePoolsObject = null;
        if (playerProjectilePool == null || enemyProjectilePool == null)
            projectilePoolsObject = GetOrCreateChildObject(gameObject, "ProjectilePools");

        if (playerProjectilePool == null)
        {
            GameObject playerPoolObject = GetOrCreateChildObject(projectilePoolsObject, "PlayerProjectilePool");
            playerProjectilePool = GetOrAddComponent<ProjectilePooler>(playerPoolObject, true);
            playerProjectilePool.initialSize = Mathf.Max(16, playerProjectilePool.initialSize);
        }

        if (enemyProjectilePool == null)
        {
            GameObject enemyPoolObject = GetOrCreateChildObject(projectilePoolsObject, "EnemyProjectilePool");
            enemyProjectilePool = GetOrAddComponent<ProjectilePooler>(enemyPoolObject, true);
            enemyProjectilePool.initialSize = Mathf.Max(16, enemyProjectilePool.initialSize);
        }

        if (monsterPool == null)
        {
            GameObject monsterPoolObject = GetOrCreateChildObject(gameObject, "MonsterPool");
            monsterPool = GetOrAddComponent<MonsterPool>(monsterPoolObject, true);
        }

        if (createFallbackPlayer)
            CreateOrRepairPlayer();

        if (createFallbackCamera && Camera.main == null)
            CreateFallbackMainCamera();
    }

    private void CreateOrRepairPlayer()
    {
        if (playerController == null)
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
            GetOrAddComponent<PlayerShootingSystem>(playerObject, true);
            playerController = GetOrAddComponent<PlayerController>(playerObject, true);
        }

        Rigidbody2D existingBody = GetOrAddComponent<Rigidbody2D>(playerObject, true);
        existingBody.gravityScale = 0f;
        existingBody.freezeRotation = true;

        if (playerObject.GetComponent<Collider2D>() == null)
            playerObject.AddComponent<CircleCollider2D>();

        SpriteRenderer renderer = GetOrAddComponent<SpriteRenderer>(playerObject, true);
        renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, 20);
        GetOrAddComponent<PlayerAnimator>(playerObject, true);
        playerShootingSystem = GetOrAddComponent<PlayerShootingSystem>(playerObject, true);

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            playerObject.layer = playerLayer;

        // ProjectSettings/TagManager.asset에 Player Tag를 정의해 둡니다.
        playerObject.tag = "Player";

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

    /// <summary>
    /// 기존 씬에서 새 BattleSceneManager로 넘어올 때 딱 한 번만 기존 Inspector 데이터를 가져옵니다.
    /// 이후에는 BattleSceneManager 필드가 authoritative source이므로 빈 리스트도 그대로 반영됩니다.
    /// </summary>
    private void ImportExistingContentOnce()
    {
        if (importedLegacyContent)
            return;

        if (runManager != null)
        {
            if (nodeGraph == null) nodeGraph = GetPrivateField<NodeGraphSO>(runManager, "nodeGraph");
            if (clan == null) clan = GetPrivateField<ClanDefinitionSO>(runManager, "clan");
            if (shootingTheme == null) shootingTheme = GetPrivateField<ShootingThemeSO>(runManager, "shootingTheme");
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

        importedLegacyContent = true;
        MarkObjectDirty(this);
    }

    private void ApplyBindings()
    {
        if (runManager != null)
        {
            SetPrivateField(runManager, "nodeGraph", nodeGraph);
            SetPrivateField(runManager, "clan", clan);
            SetPrivateField(runManager, "shootingTheme", shootingTheme);
            SetPrivateField(runManager, "sceneManager", this);
            SetPrivateField(runManager, "roomManager", roomManager);
            SetPrivateField(runManager, "progress", progressSystem);
            SetPrivateField(runManager, "rewardSystem", rewardSystem);
            SetPrivateField(runManager, "equipmentSystem", equipmentSystem);
            SetPrivateField(runManager, "fanMissionSystem", fanMissionSystem);
            SetPrivateField(runManager, "playerController", playerController);
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
            SetPrivateField(
                equipmentSystem,
                "startingEquipment",
                startingEquipment != null ? new List<BattleEquipmentSO>(startingEquipment) : new List<BattleEquipmentSO>());
        }

        if (rewardSystem != null)
        {
            SetPrivateField(
                rewardSystem,
                "rewardPool",
                rewardPool != null ? new List<BattleEquipmentSO>(rewardPool) : new List<BattleEquipmentSO>());
        }

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

        MarkSceneDirty();
    }

    private static void ValidateSingleton<T>(List<string> errors, string displayName) where T : Component
    {
        T[] all = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all.Length > 1)
            errors.Add($"Duplicate {displayName}: {all.Length} instances found. Keep exactly one battle instance.");
    }

    private static GameObject GetOrCreateChildObject(GameObject parent, string childName)
    {
        if (parent == null)
            return null;

        Transform child = parent.transform.Find(childName);
        if (child != null)
            return child.gameObject;

        GameObject created = new(childName);
        created.transform.SetParent(parent.transform, false);
        return created;
    }

    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;

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

    private static T GetPrivateField<T>(object target, string fieldName)
    {
        if (target == null)
            return default;

        FieldInfo field = target.GetType().GetField(fieldName, FieldFlags);
        if (field == null)
            return default;

        object value = field.GetValue(target);
        return value is T typed ? typed : default;
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

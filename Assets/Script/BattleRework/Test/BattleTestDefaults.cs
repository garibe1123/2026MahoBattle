using System;
using System.Collections.Generic;
using System.Reflection;
using NavMeshPlus.Components;
using NavMeshPlus.Extensions;
using UnityEngine;
using UnityEngine.AI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// BattleSceneManager의 콘텐츠 칸이 비어 있을 때만 사용하는 테스트용 기본 템플릿 설치기입니다.
///
/// 목표:
/// - NodeGraph / Room / Enemy SO / Player Sprite / Weapon / Projectile / Reward를 전부 직접 만들지 않아도 Play 가능
/// - 사용자가 직접 지정한 값은 절대 덮어쓰지 않음
/// - Editor에서는 Resources/BattleTestDefaults 아래에 실제 Asset/Prefab을 생성해 참조 안정성 확보
/// - 생성된 기본값은 수직 슬라이스 기능 검증용이며 정식 밸런스 값이 아님
///
/// 기본 테스트 난이도:
/// Room A: 근접 2 + 원거리 1
/// Room B: 근접 3 + 원거리 2
/// Elite: 근접 2 + 원거리 1 + Elite 1
/// Player: HP 100 기준 기본 권총 12 Damage, 8발, 0.28초 간격
/// </summary>
public static class BattleTestDefaults
{
    private const string ResourceFolder = "BattleTestDefaults";
    private const string AssetFolder = "Assets/Resources/BattleTestDefaults";

    private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool busy;

#if UNITY_EDITOR
    private static double nextEditorCheck;

    [InitializeOnLoadMethod]
    private static void InitializeEditorWatcher()
    {
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
        EditorApplication.hierarchyChanged -= ScheduleSoon;
        EditorApplication.hierarchyChanged += ScheduleSoon;
        EditorApplication.delayCall += EnsureEditorDefaults;
    }

    private static void ScheduleSoon()
    {
        nextEditorCheck = 0d;
    }

    private static void EditorTick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (EditorApplication.timeSinceStartup < nextEditorCheck)
            return;

        nextEditorCheck = EditorApplication.timeSinceStartup + 0.8d;
        EnsureEditorDefaults();
    }

    private static void EnsureEditorDefaults()
    {
        if (busy || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        bool anyNeedsDefaults = false;

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (!IsEditableSceneManager(manager))
                continue;

            if (NeedsAnyDefault(manager))
            {
                anyNeedsDefaults = true;
                break;
            }
        }

        if (!anyNeedsDefaults)
            return;

        busy = true;
        try
        {
            TestDefaultBundle bundle = BuildOrRefreshEditorBundle();

            for (int i = 0; i < managers.Length; i++)
            {
                BattleSceneManager manager = managers[i];
                if (!IsEditableSceneManager(manager))
                    continue;

                ApplyDefaults(manager, bundle, true);
            }

            AssetDatabase.SaveAssets();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[BattleTestDefaults] Failed to install editor defaults.\n{exception}");
        }
        finally
        {
            busy = false;
        }
    }

    private static bool IsEditableSceneManager(BattleSceneManager manager)
    {
        return manager != null &&
               !EditorUtility.IsPersistent(manager) &&
               manager.gameObject.scene.IsValid() &&
               manager.gameObject.scene.isLoaded;
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeDefaults()
    {
        if (busy)
            return;

        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        if (managers == null || managers.Length == 0)
            return;

        TestDefaultBundle bundle = LoadRuntimeBundle();
        if (!bundle.IsUsable)
        {
            Debug.LogWarning(
                "[BattleTestDefaults] Generated test assets were not found in Resources. " +
                "Open the battle scene once in the Unity Editor so BattleTestDefaults can generate them.");
            return;
        }

        busy = true;
        try
        {
            for (int i = 0; i < managers.Length; i++)
                ApplyDefaults(managers[i], bundle, false);
        }
        finally
        {
            busy = false;
        }
    }

    private static bool NeedsAnyDefault(BattleSceneManager manager)
    {
        if (manager == null)
            return false;

        if (GetField<NodeGraphSO>(manager, "nodeGraph") == null) return true;
        if (GetField<ClanDefinitionSO>(manager, "clan") == null) return true;
        if (GetField<ShootingThemeSO>(manager, "shootingTheme") == null) return true;
        if (GetField<PlayerSpriteSO>(manager, "playerSprite") == null) return true;
        if (GetField<MonsterController>(manager, "monsterPrefab") == null) return true;
        if (GetField<Projectile>(manager, "playerProjectilePrefab") == null) return true;
        if (GetField<Projectile>(manager, "enemyProjectilePrefab") == null) return true;

        List<BattleEquipmentSO> starters = GetField<List<BattleEquipmentSO>>(manager, "startingEquipment");
        if (starters == null || starters.Count == 0) return true;

        List<BattleEquipmentSO> rewards = GetField<List<BattleEquipmentSO>>(manager, "rewardPool");
        return rewards == null || rewards.Count == 0;
    }

    private static void ApplyDefaults(BattleSceneManager manager, TestDefaultBundle bundle, bool editorMode)
    {
        if (manager == null || !bundle.IsUsable)
            return;

        bool changed = false;
        bool defaultMapInstalled = false;
        bool defaultEquipmentInstalled = false;

        if (GetField<NodeGraphSO>(manager, "nodeGraph") == null)
        {
            SetField(manager, "nodeGraph", bundle.nodeGraph);
            changed = true;
            defaultMapInstalled = true;
        }

        changed |= SetIfNull(manager, "clan", bundle.clan);
        changed |= SetIfNull(manager, "shootingTheme", bundle.theme);
        changed |= SetIfNull(manager, "playerSprite", bundle.playerSprite);
        changed |= SetIfNull(manager, "monsterPrefab", bundle.monsterPrefab);
        changed |= SetIfNull(manager, "playerProjectilePrefab", bundle.playerProjectilePrefab);
        changed |= SetIfNull(manager, "enemyProjectilePrefab", bundle.enemyProjectilePrefab);

        List<BattleEquipmentSO> starters = GetField<List<BattleEquipmentSO>>(manager, "startingEquipment");
        if (starters == null || starters.Count == 0)
        {
            SetField(manager, "startingEquipment", new List<BattleEquipmentSO> { bundle.starterEquipment });
            changed = true;
            defaultEquipmentInstalled = true;
        }

        List<BattleEquipmentSO> rewards = GetField<List<BattleEquipmentSO>>(manager, "rewardPool");
        if (rewards == null || rewards.Count == 0)
        {
            SetField(manager, "rewardPool", new List<BattleEquipmentSO>
            {
                bundle.starterEquipment,
                bundle.rapidEquipment,
                bundle.scatterEquipment
            });
            changed = true;
            defaultEquipmentInstalled = true;
        }

        if (!changed)
            return;

        manager.InstallOrRepairScene();

        if (defaultMapInstalled)
            ConfigureDefaultNavigationAndCamera(manager);

        if (defaultEquipmentInstalled)
            ConfigureDefaultEquipmentCapacity();

        ConfigureDefaultWeaponVisual();

#if UNITY_EDITOR
        if (editorMode)
        {
            EditorUtility.SetDirty(manager);
            if (manager.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
#endif

        string mode = editorMode ? "Editor" : "Runtime";
        Debug.Log(
            $"[BattleTestDefaults] {mode} test defaults filled only the empty BattleSceneManager fields. " +
            "Assign any custom SO/Prefab to replace the corresponding default.",
            manager);
    }

    private static void ConfigureDefaultNavigationAndCamera(BattleSceneManager manager)
    {
        NavMeshSurface surface = UnityEngine.Object.FindFirstObjectByType<NavMeshSurface>();
        if (surface != null)
        {
            if (surface.GetComponent<CollectSources2d>() == null)
                surface.gameObject.AddComponent<CollectSources2d>();

            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.layerMask = 1 << LayerMask.NameToLayer("Default");
            surface.hideEditorLogs = true;
            surface.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        }

        Camera camera = Camera.main;
        if (camera == null)
            return;

        Transform parent = camera.transform.parent;
        bool generatedCamera = parent != null &&
                               parent.name == "CameraShakePivot" &&
                               parent.IsChildOf(manager.transform);

        if (!generatedCamera)
            return;

        parent.position = new Vector3(3f, 3f, 0f);
        camera.transform.localPosition = new Vector3(0f, 0f, -10f);
        camera.orthographic = true;
        camera.orthographicSize = 5.5f;
    }

    private static void ConfigureDefaultEquipmentCapacity()
    {
        BattleEquipmentSystem equipment = UnityEngine.Object.FindFirstObjectByType<BattleEquipmentSystem>();
        if (equipment == null)
            return;

        int current = GetField<int>(equipment, "unlockedSlotCount");
        if (current < 3)
            SetField(equipment, "unlockedSlotCount", 3);
    }

    private static void ConfigureDefaultWeaponVisual()
    {
        WeaponDisplay display = UnityEngine.Object.FindFirstObjectByType<WeaponDisplay>();
        if (display == null)
            return;

        display.transform.localPosition = new Vector3(0.42f, 0f, 0f);
        display.transform.localScale = new Vector3(0.42f, 0.16f, 1f);
    }

    private static TestDefaultBundle LoadRuntimeBundle()
    {
        GameObject monsterPrefabObject = Resources.Load<GameObject>($"{ResourceFolder}/PF_TEST_Monster");
        GameObject playerProjectileObject = Resources.Load<GameObject>($"{ResourceFolder}/PF_TEST_PlayerProjectile");
        GameObject enemyProjectileObject = Resources.Load<GameObject>($"{ResourceFolder}/PF_TEST_EnemyProjectile");

        return new TestDefaultBundle
        {
            nodeGraph = Resources.Load<NodeGraphSO>($"{ResourceFolder}/TEST_NodeGraph"),
            clan = Resources.Load<ClanDefinitionSO>($"{ResourceFolder}/TEST_Clan"),
            theme = Resources.Load<ShootingThemeSO>($"{ResourceFolder}/TEST_ShootingTheme"),
            playerSprite = Resources.Load<PlayerSpriteSO>($"{ResourceFolder}/TEST_PlayerSprite"),
            starterEquipment = Resources.Load<BattleEquipmentSO>($"{ResourceFolder}/TEST_EQ_BasicPistol"),
            rapidEquipment = Resources.Load<BattleEquipmentSO>($"{ResourceFolder}/TEST_EQ_Rapid"),
            scatterEquipment = Resources.Load<BattleEquipmentSO>($"{ResourceFolder}/TEST_EQ_Scatter"),
            monsterPrefab = monsterPrefabObject != null ? monsterPrefabObject.GetComponent<MonsterController>() : null,
            playerProjectilePrefab = playerProjectileObject != null ? playerProjectileObject.GetComponent<Projectile>() : null,
            enemyProjectilePrefab = enemyProjectileObject != null ? enemyProjectileObject.GetComponent<Projectile>() : null
        };
    }

#if UNITY_EDITOR
    private static TestDefaultBundle BuildOrRefreshEditorBundle()
    {
        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "BattleTestDefaults");

        Sprite placeholder = GetBuiltInPlaceholderSprite();

        PlayerSpriteSO playerSprite = GetOrCreateAsset<PlayerSpriteSO>("TEST_PlayerSprite.asset");
        playerSprite.idleSprites = Frames(placeholder, 4);
        playerSprite.moveSprites = Frames(placeholder, 6);
        playerSprite.rollSprites = Frames(placeholder, 4);
        playerSprite.fps = 10f;
        Dirty(playerSprite);

        ProjectileVisualSO playerProjectileVisual = GetOrCreateAsset<ProjectileVisualSO>("TEST_ProjectileVisual_Player.asset");
        playerProjectileVisual.startSprites = Frames(placeholder, 1);
        playerProjectileVisual.idleSprites = Frames(placeholder, 2);
        playerProjectileVisual.hitSprites = Frames(placeholder, 2);
        playerProjectileVisual.fps = 14f;
        Dirty(playerProjectileVisual);

        ProjectileVisualSO enemyProjectileVisual = GetOrCreateAsset<ProjectileVisualSO>("TEST_ProjectileVisual_Enemy.asset");
        enemyProjectileVisual.startSprites = Frames(placeholder, 1);
        enemyProjectileVisual.idleSprites = Frames(placeholder, 2);
        enemyProjectileVisual.hitSprites = Frames(placeholder, 2);
        enemyProjectileVisual.fps = 12f;
        Dirty(enemyProjectileVisual);

        ProjectileSO playerBullet = GetOrCreateAsset<ProjectileSO>("TEST_Projectile_PlayerBasic.asset");
        ConfigureProjectile(playerBullet, playerProjectileVisual, 12f, 1.6f, 12, 0.12f, LayerMask.GetMask("Enemy"));

        ProjectileSO playerLightBullet = GetOrCreateAsset<ProjectileSO>("TEST_Projectile_PlayerLight.asset");
        ConfigureProjectile(playerLightBullet, playerProjectileVisual, 13f, 1.5f, 7, 0.10f, LayerMask.GetMask("Enemy"));

        ProjectileSO enemyBullet = GetOrCreateAsset<ProjectileSO>("TEST_Projectile_EnemyBasic.asset");
        ConfigureProjectile(enemyBullet, enemyProjectileVisual, 5.5f, 3f, 6, 0.13f, LayerMask.GetMask("Player"));

        PlayerShootingSO pistol = GetOrCreateAsset<PlayerShootingSO>("TEST_Weapon_BasicPistol.asset");
        ConfigureWeapon(pistol, "[TEST] Basic Pistol", placeholder, playerBullet, 8, 1.1f, 0.28f, 1, 0f);

        PlayerShootingSO rapid = GetOrCreateAsset<PlayerShootingSO>("TEST_Weapon_Rapid.asset");
        ConfigureWeapon(rapid, "[TEST] Rapid Shooter", placeholder, playerLightBullet, 18, 1.35f, 0.13f, 1, 2f);

        PlayerShootingSO scatter = GetOrCreateAsset<PlayerShootingSO>("TEST_Weapon_Scatter.asset");
        ConfigureWeapon(scatter, "[TEST] Scatter Gun", placeholder, playerLightBullet, 5, 1.45f, 0.62f, 5, 20f);

        BattleEquipmentSO pistolEquipment = GetOrCreateAsset<BattleEquipmentSO>("TEST_EQ_BasicPistol.asset");
        ConfigureEquipment(
            pistolEquipment,
            "TEST_BASIC_PISTOL",
            "[TEST] Basic Pistol",
            placeholder,
            pistol,
            EquipmentRarity.Common,
            EquipmentTag.Projectile,
            EquipmentTag.Precision);

        BattleEquipmentSO rapidEquipment = GetOrCreateAsset<BattleEquipmentSO>("TEST_EQ_Rapid.asset");
        ConfigureEquipment(
            rapidEquipment,
            "TEST_RAPID",
            "[TEST] Rapid Shooter",
            placeholder,
            rapid,
            EquipmentRarity.Uncommon,
            EquipmentTag.Projectile,
            EquipmentTag.Critical);

        BattleEquipmentSO scatterEquipment = GetOrCreateAsset<BattleEquipmentSO>("TEST_EQ_Scatter.asset");
        ConfigureEquipment(
            scatterEquipment,
            "TEST_SCATTER",
            "[TEST] Scatter Gun",
            placeholder,
            scatter,
            EquipmentRarity.Uncommon,
            EquipmentTag.Projectile,
            EquipmentTag.Area);

        GameObject mapBlockObject = BuildOrRefreshMapBlockPrefab(placeholder, false);
        GameObject exitBlockObject = BuildOrRefreshMapBlockPrefab(placeholder, true);
        GameObject monsterPrefabObject = BuildOrRefreshMonsterPrefab(placeholder);
        GameObject playerProjectilePrefabObject = BuildOrRefreshProjectilePrefab(placeholder, true);
        GameObject enemyProjectilePrefabObject = BuildOrRefreshProjectilePrefab(placeholder, false);

        MapBlock mapBlock = mapBlockObject.GetComponent<MapBlock>();
        MapBlock exitBlock = exitBlockObject.GetComponent<MapBlock>();
        MonsterController monsterPrefab = monsterPrefabObject.GetComponent<MonsterController>();
        Projectile playerProjectilePrefab = playerProjectilePrefabObject.GetComponent<Projectile>();
        Projectile enemyProjectilePrefab = enemyProjectilePrefabObject.GetComponent<Projectile>();

        MonsterDefinitionSO melee = GetOrCreateAsset<MonsterDefinitionSO>("TEST_Enemy_Melee.asset");
        ConfigureMeleeEnemy(melee, placeholder);

        MonsterDefinitionSO ranged = GetOrCreateAsset<MonsterDefinitionSO>("TEST_Enemy_Ranged.asset");
        ConfigureRangedEnemy(ranged, placeholder, enemyBullet);

        MonsterDefinitionSO elite = GetOrCreateAsset<MonsterDefinitionSO>("TEST_Enemy_Elite.asset");
        ConfigureEliteEnemy(elite, placeholder, enemyBullet);

        RoomDefinitionSO roomA = GetOrCreateAsset<RoomDefinitionSO>("TEST_Room_A.asset");
        ConfigureRoom(roomA, "TEST_ROOM_A", mapBlock, exitBlock, melee, ranged, null, 0);

        RoomDefinitionSO roomB = GetOrCreateAsset<RoomDefinitionSO>("TEST_Room_B.asset");
        ConfigureRoom(roomB, "TEST_ROOM_B", mapBlock, exitBlock, melee, ranged, null, 1);

        RoomDefinitionSO roomElite = GetOrCreateAsset<RoomDefinitionSO>("TEST_Room_ELITE.asset");
        ConfigureRoom(roomElite, "TEST_ROOM_ELITE", mapBlock, exitBlock, melee, ranged, elite, 2);

        NodeGraphSO graph = GetOrCreateAsset<NodeGraphSO>("TEST_NodeGraph.asset");
        ConfigureGraph(graph, roomA, roomB, roomElite);

        ClanDefinitionSO clan = GetOrCreateAsset<ClanDefinitionSO>("TEST_Clan.asset");
        clan.clanId = "TEST_DEFAULT_CLAN";
        clan.displayName = "[TEST] Default Clan";
        clan.visualConcept = "자동 생성된 테스트 클랜. 실제 아트/클랜 데이터가 들어오면 교체합니다.";
        clan.combatTendency = "근접 추격 + 원거리 견제 + 마지막 Elite를 통해 기본 전투 흐름을 검증합니다.";
        clan.mapBlockPool = new List<MapBlock> { mapBlock };
        clan.obstaclePool = new List<BattleObstacle>();
        clan.roomPool = new List<RoomDefinitionSO> { roomA, roomB, roomElite };
        clan.normalMonsterPool = new List<MonsterDefinitionSO> { melee, ranged };
        clan.eliteMonsterPool = new List<MonsterDefinitionSO> { elite };
        clan.villainPool = new List<MonsterDefinitionSO>();
        Dirty(clan);

        ShootingThemeSO theme = GetOrCreateAsset<ShootingThemeSO>("TEST_ShootingTheme.asset");
        theme.themeId = "TEST_BALANCED_THEME";
        theme.displayName = "[TEST] Balanced Shooting";
        theme.hudAccentColor = new Color(0.35f, 0.85f, 1f, 1f);
        theme.fanReactionTone = "기본 전투/사격/장비 선택 검증용 중립 촬영 테마";
        theme.rewardBiases = new List<ThemeRewardBias>
        {
            new() { tag = EquipmentTag.Projectile, weightBonus = 0.25f },
            new() { tag = EquipmentTag.Precision, weightBonus = 0.15f },
            new() { tag = EquipmentTag.Area, weightBonus = 0.10f }
        };
        Dirty(theme);

        AssetDatabase.SaveAssets();

        return new TestDefaultBundle
        {
            nodeGraph = graph,
            clan = clan,
            theme = theme,
            playerSprite = playerSprite,
            starterEquipment = pistolEquipment,
            rapidEquipment = rapidEquipment,
            scatterEquipment = scatterEquipment,
            monsterPrefab = monsterPrefab,
            playerProjectilePrefab = playerProjectilePrefab,
            enemyProjectilePrefab = enemyProjectilePrefab
        };
    }

    private static void ConfigureProjectile(
        ProjectileSO projectile,
        ProjectileVisualSO visual,
        float speed,
        float lifetime,
        int damage,
        float radius,
        LayerMask damageLayer)
    {
        projectile.speed = speed;
        projectile.lifetime = lifetime;
        projectile.movement = MovementType.Straight;
        projectile.impact = ImpactType.Despawn;
        projectile.damage = damage;
        projectile.colliderRadius = radius;
        projectile.basePierceCount = 0;
        projectile.useTargetPosition = false;
        projectile.visual = visual;
        projectile.damageLayer = damageLayer;
        Dirty(projectile);
    }

    private static void ConfigureWeapon(
        PlayerShootingSO weapon,
        string displayName,
        Sprite sprite,
        ProjectileSO projectile,
        int ammo,
        float reload,
        float fireRate,
        int projectileCount,
        float spread)
    {
        weapon.weaponName = displayName;
        weapon.weaponSprite = sprite;
        weapon.description = "자동 생성된 Battle Test 기본 무기";
        weapon.idleSprites = Frames(sprite, 2);
        weapon.shootSprites = Frames(sprite, 3);
        weapon.reloadSprites = Frames(sprite, 4);
        weapon.animFps = 12f;
        weapon.projectileData = projectile;
        weapon.maxAmmo = ammo;
        weapon.reloadTime = reload;
        weapon.fireRate = fireRate;
        weapon.knockbackForce = 1f;
        weapon.projectilesPerShot = projectileCount;
        weapon.spreadAngle = spread;
        Dirty(weapon);
    }

    private static void ConfigureEquipment(
        BattleEquipmentSO equipment,
        string id,
        string displayName,
        Sprite icon,
        PlayerShootingSO weapon,
        EquipmentRarity rarity,
        params EquipmentTag[] tags)
    {
        equipment.equipmentId = id;
        equipment.equipmentName = displayName;
        equipment.icon = icon;
        equipment.type = BattleEquipmentType.Manual;
        equipment.rarity = rarity;
        equipment.unlockLevel = 0;
        equipment.baseRewardWeight = 1f;
        equipment.tags = new List<EquipmentTag>(tags);
        equipment.shootingData = weapon;
        equipment.damageMultiplier = 1f;
        equipment.moveSpeedMultiplier = 1f;
        equipment.rangeMultiplier = 1f;
        Dirty(equipment);
    }

    private static void ConfigureMeleeEnemy(MonsterDefinitionSO enemy, Sprite sprite)
    {
        enemy.monsterId = "TEST_MELEE";
        enemy.displayName = "[TEST] Chaser";
        enemy.category = MonsterCategory.Aggressive;
        enemy.maxHp = 36f;
        enemy.defense = 0f;
        enemy.moveSpeed = 2.6f;
        enemy.acceleration = 10f;
        enemy.detectionRange = 10f;
        enemy.killPointReward = 1;
        enemy.moveType = MonsterMoveType.Chase;
        enemy.stoppingDistance = 0.7f;
        enemy.wallLayer = LayerMask.GetMask("Default");
        enemy.skills = new List<MonsterSkillConfig>
        {
            new()
            {
                type = MonsterSkillType.Melee,
                cooldown = 1.15f,
                range = 1.10f,
                damage = 8f,
                windup = 0.30f,
                duration = 0.15f,
                impactFrame = 2,
                meleeFrontDot = 0f,
                targetLayer = LayerMask.GetMask("Player")
            }
        };
        enemy.visual = BuildMonsterVisual(sprite, new Color(0.95f, 0.42f, 0.42f, 1f));
        Dirty(enemy);
    }

    private static void ConfigureRangedEnemy(MonsterDefinitionSO enemy, Sprite sprite, ProjectileSO projectile)
    {
        enemy.monsterId = "TEST_RANGED";
        enemy.displayName = "[TEST] Shooter";
        enemy.category = MonsterCategory.Neutral;
        enemy.maxHp = 28f;
        enemy.defense = 0f;
        enemy.moveSpeed = 2.2f;
        enemy.acceleration = 9f;
        enemy.detectionRange = 10f;
        enemy.killPointReward = 1;
        enemy.moveType = MonsterMoveType.KeepDistance;
        enemy.stoppingDistance = 0.3f;
        enemy.minKitingDistance = 3.4f;
        enemy.maxKitingDistance = 5.4f;
        enemy.wallLayer = LayerMask.GetMask("Default");
        enemy.skills = new List<MonsterSkillConfig>
        {
            new()
            {
                type = MonsterSkillType.Projectile,
                cooldown = 1.6f,
                range = 7f,
                damage = 6f,
                windup = 0.50f,
                duration = 0.15f,
                impactFrame = 3,
                projectileData = projectile,
                targetLayer = LayerMask.GetMask("Player")
            }
        };
        enemy.visual = BuildMonsterVisual(sprite, new Color(0.48f, 0.66f, 1f, 1f));
        Dirty(enemy);
    }

    private static void ConfigureEliteEnemy(MonsterDefinitionSO enemy, Sprite sprite, ProjectileSO projectile)
    {
        enemy.monsterId = "TEST_ELITE";
        enemy.displayName = "[TEST] Elite";
        enemy.category = MonsterCategory.Elite;
        enemy.maxHp = 96f;
        enemy.defense = 1f;
        enemy.moveSpeed = 2.8f;
        enemy.acceleration = 11f;
        enemy.detectionRange = 12f;
        enemy.killPointReward = 3;
        enemy.moveType = MonsterMoveType.DashThenChase;
        enemy.stoppingDistance = 0.8f;
        enemy.dashTriggerRange = 4.5f;
        enemy.dashSpeed = 8.5f;
        enemy.dashDuration = 0.25f;
        enemy.wallLayer = LayerMask.GetMask("Default");
        enemy.skills = new List<MonsterSkillConfig>
        {
            new()
            {
                type = MonsterSkillType.Melee,
                cooldown = 1.0f,
                range = 1.25f,
                damage = 12f,
                windup = 0.28f,
                impactFrame = 2,
                meleeFrontDot = 0.1f,
                targetLayer = LayerMask.GetMask("Player")
            },
            new()
            {
                type = MonsterSkillType.Projectile,
                cooldown = 2.0f,
                range = 6.5f,
                damage = 8f,
                windup = 0.55f,
                impactFrame = 3,
                projectileData = projectile,
                targetLayer = LayerMask.GetMask("Player")
            }
        };
        enemy.visual = BuildMonsterVisual(sprite, new Color(1f, 0.70f, 0.25f, 1f));
        Dirty(enemy);
    }

    private static MonsterVisualConfig BuildMonsterVisual(Sprite sprite, Color color)
    {
        return new MonsterVisualConfig
        {
            previewSprite = sprite,
            idleSprites = Frames(sprite, 4),
            moveSprites = Frames(sprite, 6),
            attackSprites = Frames(sprite, 5),
            rangedAttackSprites = Frames(sprite, 6),
            dashSprites = Frames(sprite, 4),
            guardSprites = Frames(sprite, 3),
            skillSprites = Frames(sprite, 5),
            hitSprites = Frames(sprite, 3),
            breakSprites = Frames(sprite, 4),
            dieSprites = Frames(sprite, 6),
            fps = 10f,
            sourceFacesRight = true,
            spriteColor = color,
            hitFlashColor = Color.white,
            hitFlashDuration = 0.08f
        };
    }

    private static void ConfigureRoom(
        RoomDefinitionSO room,
        string roomId,
        MapBlock mapBlock,
        MapBlock exitBlock,
        MonsterDefinitionSO melee,
        MonsterDefinitionSO ranged,
        MonsterDefinitionSO elite,
        int difficulty)
    {
        room.roomId = roomId;
        room.recommendedGridSize = new Vector2Int(4, 4);
        room.useRuntimeBase = true;
        room.basePaddingWorld = Vector2.zero;
        room.baseOffset = Vector2.zero;
        room.playerEntryOffset = new Vector2(3f, 3f);
        room.repositionPlayerOnEnter = true;
        room.blocks = BuildFourByFourLayout(mapBlock);
        room.obstacles = new List<ObstaclePlacement>();
        room.monsterSpawns = new List<MonsterSpawnEntry>();
        room.highlightBlockPrefab = exitBlock;
        room.highlightBlockOffset = new Vector2(3f, 0f);

        switch (difficulty)
        {
            case 0:
                room.monsterSpawns.Add(new MonsterSpawnEntry
                {
                    monster = melee,
                    localPosition = new Vector2(1.3f, 5.2f),
                    count = 2,
                    scatterRadius = 0.55f
                });
                room.monsterSpawns.Add(new MonsterSpawnEntry
                {
                    monster = ranged,
                    localPosition = new Vector2(5.3f, 5.1f),
                    count = 1,
                    scatterRadius = 0f
                });
                break;

            case 1:
                room.monsterSpawns.Add(new MonsterSpawnEntry
                {
                    monster = melee,
                    localPosition = new Vector2(1.4f, 4.8f),
                    count = 3,
                    scatterRadius = 0.85f
                });
                room.monsterSpawns.Add(new MonsterSpawnEntry
                {
                    monster = ranged,
                    localPosition = new Vector2(5.2f, 2.0f),
                    count = 2,
                    scatterRadius = 0.8f
                });
                break;

            default:
                room.monsterSpawns.Add(new MonsterSpawnEntry
                {
                    monster = melee,
                    localPosition = new Vector2(1.2f, 4.9f),
                    count = 2,
                    scatterRadius = 0.55f
                });
                room.monsterSpawns.Add(new MonsterSpawnEntry
                {
                    monster = ranged,
                    localPosition = new Vector2(5.1f, 1.4f),
                    count = 1,
                    scatterRadius = 0f
                });
                if (elite != null)
                {
                    room.monsterSpawns.Add(new MonsterSpawnEntry
                    {
                        monster = elite,
                        localPosition = new Vector2(5.1f, 5.2f),
                        count = 1,
                        scatterRadius = 0f
                    });
                }
                break;
        }

        Dirty(room);
    }

    private static List<MapBlockPlacement> BuildFourByFourLayout(MapBlock prefab)
    {
        List<MapBlockPlacement> placements = new(16);

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Vector2 direction;
                if (x == 0) direction = Vector2.left;
                else if (x == 3) direction = Vector2.right;
                else direction = y % 2 == 0 ? Vector2.down : Vector2.up;

                placements.Add(new MapBlockPlacement
                {
                    prefab = prefab,
                    gridPosition = new Vector2Int(x, y),
                    entryDirection = direction
                });
            }
        }

        return placements;
    }

    private static void ConfigureGraph(
        NodeGraphSO graph,
        RoomDefinitionSO roomA,
        RoomDefinitionSO roomB,
        RoomDefinitionSO roomElite)
    {
        graph.startNodeId = "TEST_A";
        graph.nodes = new List<BattleNodeData>
        {
            new()
            {
                id = "TEST_A",
                type = BattleNodeType.Combat,
                depth = 0,
                room = roomA,
                isTerminal = false,
                nextNodeIds = new List<string> { "TEST_B" }
            },
            new()
            {
                id = "TEST_B",
                type = BattleNodeType.Combat,
                depth = 1,
                room = roomB,
                isTerminal = false,
                nextNodeIds = new List<string> { "TEST_ELITE" }
            },
            new()
            {
                id = "TEST_ELITE",
                type = BattleNodeType.Elite,
                depth = 2,
                room = roomElite,
                isTerminal = true,
                nextNodeIds = new List<string>()
            }
        };
        Dirty(graph);
    }

    private static GameObject BuildOrRefreshMapBlockPrefab(Sprite sprite, bool exitPad)
    {
        string fileName = exitPad ? "PF_TEST_ExitBlock.prefab" : "PF_TEST_MapBlock.prefab";
        string path = $"{AssetFolder}/{fileName}";

        GameObject temp = new(exitPad ? "PF_TEST_ExitBlock" : "PF_TEST_MapBlock");
        temp.layer = LayerMask.NameToLayer("Default");

        MapBlock block = temp.AddComponent<MapBlock>();
        SetField(block, "entryType", exitPad ? MapBlockEntryType.CeilingDrop : MapBlockEntryType.WheelSlide);
        SetField(block, "entryDuration", exitPad ? 0.45f : 0.58f);
        SetField(block, "entryOffset", exitPad ? 5f : 6f);
        SetField(block, "impactStrength", exitPad ? 1.15f : 0.85f);
        SetField(block, "impactReboundDistance", exitPad ? 0.12f : 0.08f);

        GameObject visual = new("Visual");
        visual.transform.SetParent(temp.transform, false);
        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = exitPad
            ? new Color(1f, 0.85f, 0.20f, 0.80f)
            : new Color(0.25f, 0.29f, 0.34f, 1f);
        renderer.sortingOrder = exitPad ? 2 : -10;
        ScaleSpriteTransform(renderer, new Vector2(2f, 2f));

        if (exitPad)
        {
            BoxCollider2D trigger = temp.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(1.5f, 1.5f);
            temp.AddComponent<RoomExitPad>();
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
        UnityEngine.Object.DestroyImmediate(temp);
        return prefab;
    }

    private static GameObject BuildOrRefreshMonsterPrefab(Sprite sprite)
    {
        string path = $"{AssetFolder}/PF_TEST_Monster.prefab";
        GameObject temp = new("PF_TEST_Monster");
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        temp.layer = enemyLayer >= 0 ? enemyLayer : 0;

        SpriteRenderer renderer = temp.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 15;
        ScaleSpriteTransform(renderer, new Vector2(0.75f, 0.75f));

        CircleCollider2D collider = temp.AddComponent<CircleCollider2D>();
        collider.radius = 0.36f;
        collider.isTrigger = true;

        NavMeshAgent agent = temp.AddComponent<NavMeshAgent>();
        agent.radius = 0.30f;
        agent.height = 0.20f;
        agent.speed = 2.5f;
        agent.acceleration = 10f;
        agent.angularSpeed = 0f;
        agent.stoppingDistance = 0.5f;
        agent.updateRotation = false;
        agent.updateUpAxis = false;

        temp.AddComponent<EnemyAnimator>();
        temp.AddComponent<MonsterController>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
        UnityEngine.Object.DestroyImmediate(temp);
        return prefab;
    }

    private static GameObject BuildOrRefreshProjectilePrefab(Sprite sprite, bool playerProjectile)
    {
        string fileName = playerProjectile ? "PF_TEST_PlayerProjectile.prefab" : "PF_TEST_EnemyProjectile.prefab";
        string path = $"{AssetFolder}/{fileName}";
        GameObject temp = new(playerProjectile ? "PF_TEST_PlayerProjectile" : "PF_TEST_EnemyProjectile");

        int layer = LayerMask.NameToLayer(playerProjectile ? "PlayerProjectile" : "EnemyProjectile");
        temp.layer = layer >= 0 ? layer : 0;

        SpriteRenderer renderer = temp.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = playerProjectile
            ? new Color(0.35f, 0.90f, 1f, 1f)
            : new Color(1f, 0.35f, 0.30f, 1f);
        renderer.sortingOrder = 40;
        ScaleSpriteTransform(renderer, new Vector2(0.16f, 0.16f));

        CircleCollider2D collider = temp.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = 0.12f;

        Rigidbody2D body = temp.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        temp.AddComponent<ProjectileAnimator>();
        Projectile projectile = temp.AddComponent<Projectile>();
        SetField(projectile, "HitLayer", playerProjectile
            ? (LayerMask)LayerMask.GetMask("Enemy")
            : (LayerMask)LayerMask.GetMask("Player"));

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
        UnityEngine.Object.DestroyImmediate(temp);
        return prefab;
    }

    private static T GetOrCreateAsset<T>(string fileName) where T : ScriptableObject
    {
        string path = $"{AssetFolder}/{fileName}";
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null)
            return asset;

        asset = ScriptableObject.CreateInstance<T>();
        asset.name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static Sprite GetBuiltInPlaceholderSprite()
    {
        Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (sprite == null)
            sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        return sprite;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = $"{parent}/{child}";
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static void Dirty(UnityEngine.Object obj)
    {
        if (obj != null)
            EditorUtility.SetDirty(obj);
    }
#endif

    private static Sprite[] Frames(Sprite sprite, int count)
    {
        int safeCount = Mathf.Max(1, count);
        Sprite[] frames = new Sprite[safeCount];
        for (int i = 0; i < safeCount; i++)
            frames[i] = sprite;
        return frames;
    }

    private static void ScaleSpriteTransform(SpriteRenderer renderer, Vector2 targetSize)
    {
        if (renderer == null || renderer.sprite == null)
            return;

        Vector2 spriteSize = renderer.sprite.bounds.size;
        float x = spriteSize.x > 0.0001f ? targetSize.x / spriteSize.x : 1f;
        float y = spriteSize.y > 0.0001f ? targetSize.y / spriteSize.y : 1f;
        renderer.transform.localScale = new Vector3(x, y, 1f);
    }

    private static bool SetIfNull<T>(object target, string fieldName, T value) where T : UnityEngine.Object
    {
        if (target == null || value == null)
            return false;

        if (GetField<T>(target, fieldName) != null)
            return false;

        SetField(target, fieldName, value);
        return true;
    }

    private static T GetField<T>(object target, string fieldName)
    {
        if (target == null)
            return default;

        FieldInfo field = target.GetType().GetField(fieldName, FieldFlags);
        if (field == null)
            return default;

        object value = field.GetValue(target);
        return value is T typed ? typed : default;
    }

    private static void SetField(object target, string fieldName, object value)
    {
        if (target == null)
            return;

        FieldInfo field = target.GetType().GetField(fieldName, FieldFlags);
        if (field == null)
        {
            Debug.LogWarning($"[BattleTestDefaults] Field '{fieldName}' was not found on {target.GetType().Name}.");
            return;
        }

        field.SetValue(target, value);
    }

    private sealed class TestDefaultBundle
    {
        public NodeGraphSO nodeGraph;
        public ClanDefinitionSO clan;
        public ShootingThemeSO theme;
        public PlayerSpriteSO playerSprite;
        public BattleEquipmentSO starterEquipment;
        public BattleEquipmentSO rapidEquipment;
        public BattleEquipmentSO scatterEquipment;
        public MonsterController monsterPrefab;
        public Projectile playerProjectilePrefab;
        public Projectile enemyProjectilePrefab;

        public bool IsUsable =>
            nodeGraph != null &&
            clan != null &&
            theme != null &&
            playerSprite != null &&
            starterEquipment != null &&
            rapidEquipment != null &&
            scatterEquipment != null &&
            monsterPrefab != null &&
            playerProjectilePrefab != null &&
            enemyProjectilePrefab != null;
    }
}

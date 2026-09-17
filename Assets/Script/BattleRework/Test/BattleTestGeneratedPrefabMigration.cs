#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// BattleTestDefaults가 Unity compile/import 도중 AssetDatabase를 건드리지 않도록 보호하고,
/// Editor가 안정된 뒤 생성 테스트 에셋을 한 번 복구한 다음 기본값 설치를 재개합니다.
///
/// 이 파일은 Assets/Resources/BattleTestDefaults 아래의 자동 생성 테스트 에셋만 다룹니다.
/// 사용자 제작 Prefab/SO는 수정하지 않습니다.
/// </summary>
[InitializeOnLoad]
internal static class BattleTestGeneratedPrefabMigration
{
    private const string GeneratedFolder = "Assets/Resources/BattleTestDefaults";
    private const string GeneratedMonsterPrefabPath = GeneratedFolder + "/PF_TEST_Monster.prefab";
    private const string GeneratedPlayerProjectilePrefabPath = GeneratedFolder + "/PF_TEST_PlayerProjectile.prefab";
    private const string GeneratedEnemyProjectilePrefabPath = GeneratedFolder + "/PF_TEST_EnemyProjectile.prefab";
    private const string GeneratedPlayerSpritePath = GeneratedFolder + "/TEST_PlayerSprite.asset";
    private const string GeneratedNodeGraphPath = GeneratedFolder + "/TEST_NodeGraph.asset";
    private const string GeneratedRoomAPath = GeneratedFolder + "/TEST_Room_A.asset";
    private const string GeneratedRoomBPath = GeneratedFolder + "/TEST_Room_B.asset";
    private const string GeneratedRoomElitePath = GeneratedFolder + "/TEST_Room_ELITE.asset";

    private const string PlayerSpriteScriptPath =
        "Assets/Script/BattleRework/Player/PlayerSpriteSO.cs";

    private static readonly BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly FieldInfo DefaultsBusyField =
        typeof(BattleTestDefaults).GetField("busy", StaticPrivate);
    private static readonly MethodInfo EnsureDefaultsMethod =
        typeof(BattleTestDefaults).GetMethod("EnsureEditorDefaults", StaticPrivate);

    private static double nextAttemptTime;
    private static bool completed;

    static BattleTestGeneratedPrefabMigration()
    {
        // BattleTestDefaults의 delayCall/0.8초 EditorTick이 compile/import 직후 실행되면
        // ScriptableObject 생성과 Prefab 저장이 transient artifact 갱신과 충돌할 수 있습니다.
        // 먼저 잠그고, 아래 update에서 Editor가 안정된 뒤 한 번만 풀어 줍니다.
        SetDefaultsBusy(true);

        EditorApplication.update -= WaitForStableEditorAndRepair;
        EditorApplication.update += WaitForStableEditorAndRepair;
        AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;

        nextAttemptTime = EditorApplication.timeSinceStartup + 0.5d;
    }

    private static void BeforeAssemblyReload()
    {
        SetDefaultsBusy(false);
    }

    private static void WaitForStableEditorAndRepair()
    {
        if (completed)
        {
            StopWatching();
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            // Play 진입 자체를 busy 플래그로 막지는 않습니다.
            SetDefaultsBusy(false);
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            SetDefaultsBusy(true);
            nextAttemptTime = EditorApplication.timeSinceStartup + 0.5d;
            return;
        }

        if (EditorApplication.timeSinceStartup < nextAttemptTime)
            return;

        if (!IsPlayerSpriteScriptReady())
        {
            // MonoScript와 PlayerSpriteSO 타입 연결이 아직 끝나지 않았습니다.
            // 이 상태에서 CreateAsset을 호출하면
            // "No script asset for PlayerSpriteSO"가 발생할 수 있습니다.
            SetDefaultsBusy(true);
            nextAttemptTime = EditorApplication.timeSinceStartup + 0.5d;
            return;
        }

        SetDefaultsBusy(true);

        bool deletedGeneratedAsset = false;
        bool dirtyAsset = false;

        deletedGeneratedAsset |= DeleteBrokenPlayerSpriteAsset();
        deletedGeneratedAsset |= DeleteBrokenMonsterPrefab();
        deletedGeneratedAsset |= DeleteBrokenProjectilePrefab(
            GeneratedPlayerProjectilePrefabPath,
            "PF_TEST_PlayerProjectile");
        deletedGeneratedAsset |= DeleteBrokenProjectilePrefab(
            GeneratedEnemyProjectilePrefabPath,
            "PF_TEST_EnemyProjectile");

        dirtyAsset |= RepairGeneratedNodeGraph();
        dirtyAsset |= RepairGeneratedRoom(GeneratedRoomAPath);
        dirtyAsset |= RepairGeneratedRoom(GeneratedRoomBPath);
        dirtyAsset |= RepairGeneratedRoom(GeneratedRoomElitePath);

        if (dirtyAsset)
            AssetDatabase.SaveAssets();

        // DeleteAsset 직후에는 Unity가 다시 artifact 갱신에 들어갈 수 있습니다.
        // Refresh를 강제로 호출하지 않고 다음 안정 프레임까지 기다립니다.
        if (deletedGeneratedAsset || EditorApplication.isUpdating || EditorApplication.isCompiling)
        {
            nextAttemptTime = EditorApplication.timeSinceStartup + 0.75d;
            return;
        }

        SetDefaultsBusy(false);
        InvokeEnsureDefaultsOnce();

        // EnsureEditorDefaults가 생성 작업을 시작했다면 import가 끝난 뒤 한 번 더 확인합니다.
        if (EditorApplication.isUpdating || EditorApplication.isCompiling || !GeneratedCoreAssetsAreReady())
        {
            SetDefaultsBusy(true);
            nextAttemptTime = EditorApplication.timeSinceStartup + 0.75d;
            return;
        }

        // 생성된 NodeGraph/Room은 BuildOrRefreshEditorBundle에서 다시 덮어쓸 수 있으므로
        // 마지막으로 한 번 더 현재 규칙에 맞게 보정합니다.
        bool finalDirty = false;
        finalDirty |= RepairGeneratedNodeGraph();
        finalDirty |= RepairGeneratedRoom(GeneratedRoomAPath);
        finalDirty |= RepairGeneratedRoom(GeneratedRoomBPath);
        finalDirty |= RepairGeneratedRoom(GeneratedRoomElitePath);
        if (finalDirty)
            AssetDatabase.SaveAssets();

        completed = true;
        SetDefaultsBusy(false);
        StopWatching();

        Debug.Log(
            "[BattleTestDefaults] Generated test assets repaired after Unity import completed. " +
            "Automatic default installation resumed without forcing AssetDatabase.Refresh().");
    }

    private static bool IsPlayerSpriteScriptReady()
    {
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(PlayerSpriteScriptPath);
        return script != null && script.GetClass() == typeof(PlayerSpriteSO);
    }

    private static void SetDefaultsBusy(bool value)
    {
        DefaultsBusyField?.SetValue(null, value);
    }

    private static void InvokeEnsureDefaultsOnce()
    {
        if (EnsureDefaultsMethod == null)
        {
            Debug.LogError(
                "[BattleTestDefaults] Could not find EnsureEditorDefaults. " +
                "Generated defaults cannot be resumed safely.");
            return;
        }

        try
        {
            EnsureDefaultsMethod.Invoke(null, null);
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            Debug.LogError($"[BattleTestDefaults] Safe default installation failed.\n{inner}");
        }
    }

    private static bool DeleteBrokenPlayerSpriteAsset()
    {
        if (!File.Exists(GeneratedPlayerSpritePath))
            return false;

        PlayerSpriteSO typedAsset = AssetDatabase.LoadAssetAtPath<PlayerSpriteSO>(GeneratedPlayerSpritePath);
        if (typedAsset != null)
            return false;

        if (!AssetDatabase.DeleteAsset(GeneratedPlayerSpritePath))
            return false;

        Debug.Log(
            "[BattleTestDefaults] Removed broken generated TEST_PlayerSprite.asset. " +
            "It will be recreated after PlayerSpriteSO import is fully ready.");
        return true;
    }

    private static bool DeleteBrokenMonsterPrefab()
    {
        if (!File.Exists(GeneratedMonsterPrefabPath))
            return false;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GeneratedMonsterPrefabPath);
        bool broken = prefab == null ||
                      GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) > 0 ||
                      prefab.GetComponent<MonsterController>() == null ||
                      prefab.GetComponent<EnemyAnimator>() == null ||
                      prefab.GetComponent<SpriteRenderer>() == null ||
                      prefab.GetComponent<Collider2D>() == null ||
                      prefab.GetComponent<NavMeshAgent>() == null;

        if (!broken)
            return false;

        if (!AssetDatabase.DeleteAsset(GeneratedMonsterPrefabPath))
            return false;

        Debug.Log(
            "[BattleTestDefaults] Removed broken generated PF_TEST_Monster prefab. " +
            "It will be recreated once from current components.");
        return true;
    }

    private static bool DeleteBrokenProjectilePrefab(string path, string displayName)
    {
        if (!File.Exists(path))
            return false;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        bool broken = prefab == null ||
                      GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) > 0 ||
                      prefab.GetComponent<Projectile>() == null ||
                      prefab.GetComponent<ProjectileAnimator>() == null ||
                      prefab.GetComponent<Rigidbody2D>() == null ||
                      prefab.GetComponent<Collider2D>() == null;

        if (!broken)
            return false;

        if (!AssetDatabase.DeleteAsset(path))
            return false;

        Debug.Log(
            $"[BattleTestDefaults] Removed broken generated {displayName} prefab. " +
            "It will be recreated once from current components.");
        return true;
    }

    private static bool RepairGeneratedNodeGraph()
    {
        if (!File.Exists(GeneratedNodeGraphPath))
            return false;

        NodeGraphSO graph = AssetDatabase.LoadAssetAtPath<NodeGraphSO>(GeneratedNodeGraphPath);
        if (graph == null)
        {
            if (AssetDatabase.DeleteAsset(GeneratedNodeGraphPath))
            {
                Debug.Log(
                    "[BattleTestDefaults] Removed unreadable generated TEST_NodeGraph.asset. " +
                    "It will be recreated from the current schema.");
            }
            return false;
        }

        bool changed = graph.startNodeId != "TEST_A" ||
                       graph.startNodeIds == null ||
                       graph.startNodeIds.Count != 1 ||
                       graph.startNodeIds[0] != "TEST_A";

        if (!changed)
            return false;

        graph.startNodeIds ??= new List<string>();
        graph.startNodeIds.Clear();
        graph.startNodeIds.Add("TEST_A");
        graph.startNodeId = "TEST_A";
        EditorUtility.SetDirty(graph);

        Debug.Log(
            "[BattleTestDefaults] Repaired TEST_NodeGraph start IDs to TEST_A. " +
            "Removed stale TEST_A_LEFT / TEST_B_RIGHT references.");
        return true;
    }

    private static bool RepairGeneratedRoom(string roomPath)
    {
        if (!File.Exists(roomPath))
            return false;

        RoomDefinitionSO room = AssetDatabase.LoadAssetAtPath<RoomDefinitionSO>(roomPath);
        if (room == null)
            return false;

        bool changed = false;

        if (!room.useProceduralRoom)
        {
            room.useProceduralRoom = true;
            changed = true;
        }

        int safeChunk = Mathf.Max(
            RoomDefinitionSO.MinimumRoomChunkTiles,
            room.proceduralMinChunkTileSize);
        if (room.proceduralMinChunkTileSize != safeChunk)
        {
            room.proceduralMinChunkTileSize = safeChunk;
            changed = true;
        }

        Vector2Int safeMin = new(
            Mathf.Max(RoomDefinitionSO.MinimumCombatRoomTiles, room.proceduralMinTileSize.x),
            Mathf.Max(RoomDefinitionSO.MinimumCombatRoomTiles, room.proceduralMinTileSize.y));
        if (room.proceduralMinTileSize != safeMin)
        {
            room.proceduralMinTileSize = safeMin;
            changed = true;
        }

        Vector2Int safeMax = new(
            Mathf.Max(
                RoomDefinitionSO.MinimumPreferredMaxRoomTiles,
                safeMin.x + RoomDefinitionSO.MinimumRoomSizeVariation,
                room.proceduralMaxTileSize.x),
            Mathf.Max(
                RoomDefinitionSO.MinimumPreferredMaxRoomTiles,
                safeMin.y + RoomDefinitionSO.MinimumRoomSizeVariation,
                room.proceduralMaxTileSize.y));
        if (room.proceduralMaxTileSize != safeMax)
        {
            room.proceduralMaxTileSize = safeMax;
            changed = true;
        }

        if (changed)
            EditorUtility.SetDirty(room);

        return changed;
    }

    private static bool GeneratedCoreAssetsAreReady()
    {
        PlayerSpriteSO playerSprite =
            AssetDatabase.LoadAssetAtPath<PlayerSpriteSO>(GeneratedPlayerSpritePath);
        NodeGraphSO graph =
            AssetDatabase.LoadAssetAtPath<NodeGraphSO>(GeneratedNodeGraphPath);
        GameObject monster =
            AssetDatabase.LoadAssetAtPath<GameObject>(GeneratedMonsterPrefabPath);
        GameObject playerProjectile =
            AssetDatabase.LoadAssetAtPath<GameObject>(GeneratedPlayerProjectilePrefabPath);
        GameObject enemyProjectile =
            AssetDatabase.LoadAssetAtPath<GameObject>(GeneratedEnemyProjectilePrefabPath);

        return playerSprite != null &&
               graph != null &&
               graph.FindNode("TEST_A") != null &&
               graph.startNodeIds != null &&
               graph.startNodeIds.Count == 1 &&
               graph.startNodeIds[0] == "TEST_A" &&
               monster != null && monster.GetComponent<MonsterController>() != null &&
               playerProjectile != null && playerProjectile.GetComponent<Projectile>() != null &&
               enemyProjectile != null && enemyProjectile.GetComponent<Projectile>() != null;
    }

    private static void StopWatching()
    {
        EditorApplication.update -= WaitForStableEditorAndRepair;
        AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
    }
}
#endif

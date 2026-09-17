#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// BattleTestDefaults가 생성한 테스트 Asset/Prefab은 사용자 제작 에셋이 아니므로,
/// 스크립트 구조 변경 뒤 오래된 직렬화 상태가 남아 있으면 Editor 시작 시 폐기/복구합니다.
///
/// 이 migration은 다음 문제를 정리합니다.
/// - PF_TEST_Monster의 오래된 EnemyAnimator/MonsterController 직렬화
/// - PF_TEST_PlayerProjectile / PF_TEST_EnemyProjectile의 Missing Script 및 오래된 Projectile 구성
/// - TEST_PlayerSprite.asset이 현재 PlayerSpriteSO 타입으로 로드되지 않는 stale asset 상태
/// - TEST_NodeGraph.asset에 과거 TEST_A_LEFT / TEST_B_RIGHT startNodeIds가 남는 상태
///
/// 생성 에셋만 대상으로 하며 사용자 제작 Prefab/SO는 수정하지 않습니다.
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

    private const string EnemyAnimatorScriptPath =
        "Assets/Script/BattleRework/Animation/EnemyAnimator.cs";
    private const string MonsterControllerScriptPath =
        "Assets/Script/BattleRework/Monster/MonsterController.cs";
    private const string ProjectileAnimatorScriptPath =
        "Assets/Script/BattleRework/Animation/ProjectileAnimator.cs";
    private const string ProjectileScriptPath =
        "Assets/Script/BattleRework/Projectile/Projectile.cs";

    static BattleTestGeneratedPrefabMigration()
    {
        EditorApplication.delayCall -= RepairGeneratedAssets;
        EditorApplication.delayCall += RepairGeneratedAssets;
    }

    private static void RepairGeneratedAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        bool changed = false;

        changed |= DeleteStaleGeneratedScriptableObject<PlayerSpriteSO>(
            GeneratedPlayerSpritePath,
            "TEST_PlayerSprite.asset");

        changed |= DeleteStaleGeneratedPrefab(
            GeneratedMonsterPrefabPath,
            "PF_TEST_Monster.prefab",
            EnemyAnimatorScriptPath,
            MonsterControllerScriptPath);

        changed |= DeleteStaleGeneratedPrefab(
            GeneratedPlayerProjectilePrefabPath,
            "PF_TEST_PlayerProjectile.prefab",
            ProjectileAnimatorScriptPath,
            ProjectileScriptPath);

        changed |= DeleteStaleGeneratedPrefab(
            GeneratedEnemyProjectilePrefabPath,
            "PF_TEST_EnemyProjectile.prefab",
            ProjectileAnimatorScriptPath,
            ProjectileScriptPath);

        changed |= RepairGeneratedNodeGraph();

        if (!changed)
            return;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static bool DeleteStaleGeneratedScriptableObject<T>(string assetPath, string displayName)
        where T : ScriptableObject
    {
        if (!File.Exists(assetPath))
            return false;

        T typedAsset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (typedAsset != null)
            return false;

        if (!AssetDatabase.DeleteAsset(assetPath))
            return false;

        Debug.Log(
            $"[BattleTestDefaults] Removed stale generated {displayName}. " +
            "BattleTestDefaults will regenerate it with the current script type.");
        return true;
    }

    private static bool DeleteStaleGeneratedPrefab(
        string prefabPath,
        string displayName,
        params string[] requiredScriptPaths)
    {
        if (!File.Exists(prefabPath))
            return false;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        bool stale = prefab == null;

        if (!stale && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) > 0)
            stale = true;

        string prefabText = null;
        if (!stale)
        {
            try
            {
                prefabText = File.ReadAllText(prefabPath);
            }
            catch (IOException)
            {
                return false;
            }
        }

        if (!stale && requiredScriptPaths != null)
        {
            for (int i = 0; i < requiredScriptPaths.Length; i++)
            {
                string scriptPath = requiredScriptPaths[i];
                string scriptGuid = AssetDatabase.AssetPathToGUID(scriptPath);
                if (string.IsNullOrEmpty(scriptGuid))
                    continue;

                if (!prefabText.Contains($"guid: {scriptGuid}"))
                {
                    stale = true;
                    break;
                }
            }
        }

        if (!stale)
            return false;

        if (!AssetDatabase.DeleteAsset(prefabPath))
            return false;

        Debug.Log(
            $"[BattleTestDefaults] Removed stale generated {displayName}. " +
            "BattleTestDefaults will regenerate it once with current components serialized.");
        return true;
    }

    private static bool RepairGeneratedNodeGraph()
    {
        if (!File.Exists(GeneratedNodeGraphPath))
            return false;

        NodeGraphSO graph = AssetDatabase.LoadAssetAtPath<NodeGraphSO>(GeneratedNodeGraphPath);
        if (graph == null)
        {
            if (!AssetDatabase.DeleteAsset(GeneratedNodeGraphPath))
                return false;

            Debug.Log(
                "[BattleTestDefaults] Removed stale generated TEST_NodeGraph.asset. " +
                "BattleTestDefaults will regenerate it with the current NodeGraphSO schema.");
            return true;
        }

        bool needsRepair = graph.startNodeId != "TEST_A" ||
                           graph.startNodeIds == null ||
                           graph.startNodeIds.Count != 1 ||
                           graph.startNodeIds[0] != "TEST_A";

        if (!needsRepair)
            return false;

        graph.startNodeIds ??= new List<string>();
        graph.startNodeIds.Clear();
        graph.startNodeIds.Add("TEST_A");
        graph.startNodeId = "TEST_A";
        EditorUtility.SetDirty(graph);

        Debug.Log(
            "[BattleTestDefaults] Repaired generated TEST_NodeGraph start node IDs. " +
            "Removed obsolete TEST_A_LEFT / TEST_B_RIGHT start references.");
        return true;
    }
}
#endif

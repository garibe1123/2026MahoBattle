#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// BattleTestDefaults가 생성한 테스트 Prefab은 사용자 제작 에셋이 아니므로,
/// 스크립트 구조 변경 뒤 오래된 직렬화 상태가 남아 있으면 Editor 시작 시 한 번 폐기합니다.
///
/// 특히 MonsterController가 EnemyAnimator를 RequireComponent로 요구하는데
/// 예전 PF_TEST_Monster.prefab에 EnemyAnimator 컴포넌트가 직렬화되어 있지 않으면
/// PrefabUtility.SaveAsPrefabAsset 때 Unity가 매번 Required Component를 복구하며
/// "Creating missing EnemyAnimator component..." 로그가 반복될 수 있습니다.
/// </summary>
[InitializeOnLoad]
internal static class BattleTestGeneratedPrefabMigration
{
    private const string GeneratedMonsterPrefabPath =
        "Assets/Resources/BattleTestDefaults/PF_TEST_Monster.prefab";

    private const string EnemyAnimatorScriptPath =
        "Assets/Script/BattleRework/Animation/EnemyAnimator.cs";

    static BattleTestGeneratedPrefabMigration()
    {
        EditorApplication.delayCall -= RepairGeneratedMonsterPrefab;
        EditorApplication.delayCall += RepairGeneratedMonsterPrefab;
    }

    private static void RepairGeneratedMonsterPrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (!File.Exists(GeneratedMonsterPrefabPath))
            return;

        string animatorGuid = AssetDatabase.AssetPathToGUID(EnemyAnimatorScriptPath);
        if (string.IsNullOrEmpty(animatorGuid))
            return;

        string prefabText;
        try
        {
            prefabText = File.ReadAllText(GeneratedMonsterPrefabPath);
        }
        catch (IOException)
        {
            return;
        }

        // Unity YAML에 현재 EnemyAnimator script GUID가 없다면 이 Prefab은 D 리팩터링 이전의
        // stale generated asset입니다. 직접 고치지 않고 삭제해서 BattleTestDefaults가 한 번
        // 깨끗하게 재생성하게 하는 편이 참조 복구와 RequireComponent 검증 모두 안전합니다.
        if (prefabText.Contains($"guid: {animatorGuid}"))
            return;

        if (!AssetDatabase.DeleteAsset(GeneratedMonsterPrefabPath))
            return;

        AssetDatabase.Refresh();
        Debug.Log(
            "[BattleTestDefaults] Removed stale generated PF_TEST_Monster prefab. " +
            "BattleTestDefaults will regenerate it once with EnemyAnimator already serialized.");
    }
}
#endif

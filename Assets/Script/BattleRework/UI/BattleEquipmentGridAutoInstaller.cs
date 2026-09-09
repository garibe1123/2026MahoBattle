using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 3x3 Grid Synergy + Kinetic Loadout UI를 BattleSystems에 자동 배치합니다.
/// 기존 Scene/Asset 참조는 지우거나 교체하지 않고 누락된 컴포넌트만 추가합니다.
/// </summary>
public static class BattleEquipmentGridAutoInstaller
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
        EditorApplication.delayCall += EnsureEditorComponents;
    }

    private static void EnsureEditorComponents()
    {
        installQueued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null ||
                EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid() ||
                !manager.gameObject.scene.isLoaded)
            {
                continue;
            }

            bool changed = false;
            if (manager.GetComponent<BattleGridSynergyController>() == null)
            {
                Undo.AddComponent<BattleGridSynergyController>(manager.gameObject);
                changed = true;
            }

            if (manager.GetComponent<BattleKineticLoadoutUI>() == null)
            {
                Undo.AddComponent<BattleKineticLoadoutUI>(manager.gameObject);
                changed = true;
            }

            if (!changed)
                continue;

            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponents()
    {
        BattleSceneManager[] managers = Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null)
                continue;

            if (manager.GetComponent<BattleGridSynergyController>() == null)
                manager.gameObject.AddComponent<BattleGridSynergyController>();

            if (manager.GetComponent<BattleKineticLoadoutUI>() == null)
                manager.gameObject.AddComponent<BattleKineticLoadoutUI>();
        }
    }
}

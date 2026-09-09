using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// BattleShowPresentationManager / BattleShowSetDecorationController가 BattleSystems에서 빠지는 것을 방지하는 설치 보조기입니다.
///
/// Editor:
/// - BattleSceneManager가 존재하는 로드된 씬을 감시합니다.
/// - 같은 GameObject(BattleSystems)에 필요한 Show 컴포넌트가 없으면 자동으로 추가합니다.
/// - 추가된 컴포넌트가 씬에 저장될 수 있도록 Scene을 Dirty 처리합니다.
///
/// Runtime:
/// - 저장 누락이나 구형 씬에 대한 마지막 fallback으로 같은 위치에 컴포넌트를 보강합니다.
/// </summary>
public static class BattleShowPresentationAutoInstaller
{
#if UNITY_EDITOR
    private static bool editorInstallQueued;

    [InitializeOnLoadMethod]
    private static void InitializeEditorInstaller()
    {
        EditorApplication.hierarchyChanged -= QueueEditorInstall;
        EditorApplication.hierarchyChanged += QueueEditorInstall;
        QueueEditorInstall();
    }

    private static void QueueEditorInstall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || editorInstallQueued)
            return;

        editorInstallQueued = true;
        EditorApplication.delayCall += EnsureEditorComponents;
    }

    private static void EnsureEditorComponents()
    {
        editorInstallQueued = false;

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
            if (manager.GetComponent<BattleShowPresentationManager>() == null)
            {
                Undo.AddComponent<BattleShowPresentationManager>(manager.gameObject);
                changed = true;
            }

            if (manager.GetComponent<BattleShowSetDecorationController>() == null)
            {
                Undo.AddComponent<BattleShowSetDecorationController>(manager.gameObject);
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

            if (manager.GetComponent<BattleShowPresentationManager>() == null)
                manager.gameObject.AddComponent<BattleShowPresentationManager>();

            if (manager.GetComponent<BattleShowSetDecorationController>() == null)
                manager.gameObject.AddComponent<BattleShowSetDecorationController>();
        }
    }
}

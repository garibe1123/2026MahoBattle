using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HarnessStylishArcade
{
    [MenuItem("Tools/Harness/Apply Stylish Arcade")]
    private static void Apply()
    {
        foreach (BattleShowPresentationManager presentation in Object.FindObjectsByType<BattleShowPresentationManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(presentation, "Apply Stylish Arcade");
            presentation.ApplyStylishArcadeDesign();
            EditorUtility.SetDirty(presentation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(presentation);
            EditorSceneManager.MarkSceneDirty(presentation.gameObject.scene);
        }
    }
}

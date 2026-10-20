using UnityEditor;
using UnityEngine;

public static class HarnessElementalBlobWorld
{
    [MenuItem("Tools/Harness/Create Elemental Blob World")]
    private static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EffectBlobWorld world = Object.FindFirstObjectByType<EffectBlobWorld>(FindObjectsInactive.Include);
        if (world == null)
        {
            GameObject host = new("Elemental Blob World");
            world = host.AddComponent<EffectBlobWorld>();
            Undo.RegisterCreatedObjectUndo(host, "Create Elemental Blob World");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(host.scene);
        }
        Selection.activeGameObject = world.gameObject;
    }
}

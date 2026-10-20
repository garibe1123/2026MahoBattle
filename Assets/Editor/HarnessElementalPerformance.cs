using UnityEditor;
using UnityEngine;

public static class HarnessElementalPerformance
{
    [MenuItem("Tools/Harness/Add Elemental Performance Overlay", true)]
    private static bool CanCreate() => !Application.isPlaying;

    [MenuItem("Tools/Harness/Add Elemental Performance Overlay")]
    private static void Create()
    {
        if (Application.isPlaying) return;
        EffectPerformanceOverlay overlay = Object.FindFirstObjectByType<EffectPerformanceOverlay>(FindObjectsInactive.Include);
        if (overlay == null)
        {
            GameObject host = new("Elemental Performance Overlay");
            Undo.RegisterCreatedObjectUndo(host, "Add Elemental Performance Overlay");
            overlay = Undo.AddComponent<EffectPerformanceOverlay>(host);
        }
        Selection.activeGameObject = overlay.gameObject;
    }
}

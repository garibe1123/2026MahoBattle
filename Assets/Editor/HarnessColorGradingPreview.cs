using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BattleColorGradingController))]
public sealed class HarnessColorGradingPreview : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        BattleColorGradingController controller = (BattleColorGradingController)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Harness Color Preview", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Active source profile", controller.SourceLightingProfile,
                typeof(BattleLightingProfileSO), false);
            EditorGUILayout.ObjectField("Camera", controller.TargetCamera, typeof(Camera), true);
            EditorGUILayout.TextField("Camera Volume mask", $"0x{controller.CameraVolumeMask:X8}");
            EditorGUILayout.FloatField("State Volume weight", controller.CurrentWeight);
        }

        if (!Application.isPlaying || controller.SourceLightingProfile == null)
        {
            EditorGUILayout.HelpBox("Enter Play mode and select the active color grading controller.", MessageType.Info);
            return;
        }

        if (controller.CameraVolumeMask == 0)
            EditorGUILayout.HelpBox("The camera Volume mask is empty. This camera cannot receive Volume effects.", MessageType.Warning);

        bool enabled = EditorGUILayout.Toggle("Preview applied", controller.PreviewEnabled);
        if (enabled != controller.PreviewEnabled)
            controller.SetPreviewEnabled(enabled);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(controller.PreviewEnabled ? "Compare: original" : "Compare: preview"))
                controller.SetPreviewEnabled(!controller.PreviewEnabled);
            if (GUILayout.Button("Reset preview"))
                controller.ResetPreview();
        }

        BattleLightingProfileSO preview = controller.PreviewProfile;
        if (preview == null)
            return;

        EditorGUI.BeginChangeCheck();
        preview.splitShadows = EditorGUILayout.ColorField("Blue shadows", preview.splitShadows);
        preview.splitHighlights = EditorGUILayout.ColorField("Warm highlights", preview.splitHighlights);
        preview.splitBalance = EditorGUILayout.Slider("Split balance", preview.splitBalance, -100f, 100f);
        preview.bloomIntensity = EditorGUILayout.Slider("Bloom intensity", preview.bloomIntensity, 0f, 5f);
        preview.bloomThreshold = EditorGUILayout.Slider("Bloom threshold", preview.bloomThreshold, 0f, 10f);
        preview.bloomScatter = EditorGUILayout.Slider("Bloom scatter", preview.bloomScatter, 0f, 1f);
        preview.bloomTint = EditorGUILayout.ColorField("Bloom tint", preview.bloomTint);
        if (EditorGUI.EndChangeCheck())
            controller.RefreshPreview();
    }

    public override bool RequiresConstantRepaint() => Application.isPlaying;
}

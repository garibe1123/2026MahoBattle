using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class HarnessTabBackgroundCapture
{
    [MenuItem("Tools/Harness/Install Tab Background Capture")]
    private static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        ScriptableRendererData rendererData = Selection.activeObject as ScriptableRendererData;
        if (rendererData == null)
        {
            UniversalRenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) return;
            SerializedObject settings = new(pipeline);
            SerializedProperty renderers = settings.FindProperty("m_RendererDataList");
            int index = settings.FindProperty("m_DefaultRendererIndex").intValue;
            Camera camera = Camera.main;
            BattleShowBroadcastNoiseController controller = Object.FindFirstObjectByType<BattleShowBroadcastNoiseController>();
            if (controller != null)
            {
                Camera assigned = new SerializedObject(controller).FindProperty("targetCamera").objectReferenceValue as Camera;
                if (assigned != null) camera = assigned;
            }
            BattleColorGradingController grading = Object.FindFirstObjectByType<BattleColorGradingController>();
            if (grading != null && grading.TargetCamera != null)
                camera = grading.TargetCamera;
            if (camera != null && camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            {
                int cameraIndex = new SerializedObject(cameraData).FindProperty("m_RendererIndex").intValue;
                if (cameraIndex >= 0) index = cameraIndex;
            }
            if (index >= 0 && index < renderers.arraySize)
                rendererData = renderers.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData;
        }
        if (rendererData == null || !AssetDatabase.GetAssetPath(rendererData).StartsWith("Assets/")) return;

        foreach (ScriptableRendererFeature existing in rendererData.rendererFeatures)
            if (existing is BattleTabBackgroundCaptureFeature)
            {
                Undo.RecordObject(existing, "Enable tab background capture");
                existing.SetActive(true);
                EditorUtility.SetDirty(existing);
                rendererData.SetDirty();
                EditorUtility.SetDirty(rendererData);
                AssetDatabase.SaveAssetIfDirty(existing);
                AssetDatabase.SaveAssetIfDirty(rendererData);
                return;
            }

        Undo.RecordObject(rendererData, "Install tab background capture");
        BattleTabBackgroundCaptureFeature feature = ScriptableObject.CreateInstance<BattleTabBackgroundCaptureFeature>();
        feature.name = "Battle Tab Background Capture";
        AssetDatabase.AddObjectToAsset(feature, rendererData);
        Undo.RegisterCreatedObjectUndo(feature, "Install tab background capture");
        rendererData.rendererFeatures.Add(feature);

        SerializedObject rendererSettings = new(rendererData);
        SerializedProperty map = rendererSettings.FindProperty("m_RendererFeatureMap");
        map.arraySize = rendererData.rendererFeatures.Count;
        for (int i = 0; i < map.arraySize; i++)
            if (rendererData.rendererFeatures[i] != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                rendererData.rendererFeatures[i], out string guid, out long localId))
                map.GetArrayElementAtIndex(i).longValue = localId;
        rendererSettings.ApplyModifiedProperties();
        rendererData.SetDirty();
        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssetIfDirty(rendererData);
    }

    [MenuItem("Tools/Harness/Install Stylish Panel Blur")]
    private static void InstallStylish() => Install();
}

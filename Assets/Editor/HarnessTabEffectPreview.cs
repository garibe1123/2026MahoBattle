using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BattleShowBroadcastNoiseController))]
public sealed class HarnessTabEffectPreview : Editor
{
    private BattleLightingProfileSO legacyProfile;

    [MenuItem("Tools/Harness/Migrate TAB Settings")]
    private static void MigrateSelectedProfile()
    {
        BattleLightingProfileSO profile = Selection.activeObject as BattleLightingProfileSO;
        BattleShowBroadcastNoiseController controller =
            Object.FindFirstObjectByType<BattleShowBroadcastNoiseController>(FindObjectsInactive.Include);
        if (profile == null || controller == null || Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Migrate TAB Settings",
                "Open the battle scene, select its existing BattleLightingProfileSO asset, then run this menu outside Play mode.", "OK");
            return;
        }
        ImportLegacySettings(controller, profile);
        Selection.activeGameObject = controller.gameObject;
    }

    private static void ImportLegacySettings(BattleShowBroadcastNoiseController controller, BattleLightingProfileSO profile)
    {
        SerializedObject settings = new(controller);
        BattleShowPresentationManager presentation =
            settings.FindProperty("presentation").objectReferenceValue as BattleShowPresentationManager;
        if (presentation == null)
            presentation = Object.FindFirstObjectByType<BattleShowPresentationManager>(FindObjectsInactive.Include);
        Undo.RecordObject(controller, "Migrate TAB settings");
        if (!controller.MigrateLegacyTabSettings(profile, presentation)) return;
        EditorUtility.SetDirty(controller);
        PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        if (!Application.isPlaying && controller.gameObject.scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        BattleShowBroadcastNoiseController controller = (BattleShowBroadcastNoiseController)target;
        if (!serializedObject.FindProperty("tabSettingsMigrated").boolValue)
        {
            EditorGUILayout.HelpBox("Import the existing lighting profile once to preserve its TAB values before tuning here. " +
                "Unmigrated components also import automatically at runtime when the lighting profile is ready.", MessageType.Info);
            legacyProfile = (BattleLightingProfileSO)EditorGUILayout.ObjectField(
                "Legacy lighting profile", legacyProfile, typeof(BattleLightingProfileSO), false);
            using (new EditorGUI.DisabledScope(legacyProfile == null))
                if (GUILayout.Button("Import legacy TAB settings"))
                {
                    ImportLegacySettings(controller, legacyProfile);
                    serializedObject.Update();
                }
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("TAB Effect Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Play mode: enable preview during combat to view TAB effects without holding TAB. " +
            "This component's sliders control camera grain, camera noise and static directly. " +
            "Scrolling texture effects remain disabled. " +
            "Play mode changes are temporary; copy component values before stopping and paste afterward to keep them.",
            MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || !controller.isActiveAndEnabled))
        {
            bool preview = EditorGUILayout.Toggle("Preview TAB effects", controller.TabPreview);
            if (preview != controller.TabPreview) controller.SetTabPreview(preview);
            if (GUILayout.Button("Stop preview")) controller.SetTabPreview(false);
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying && target is BattleShowBroadcastNoiseController controller && controller.TabPreview)
            controller.SetTabPreview(false);
    }

    public override bool RequiresConstantRepaint() => Application.isPlaying;
}

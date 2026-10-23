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
        BattleShowBroadcastNoiseController controller = (BattleShowBroadcastNoiseController)target;
        EditorGUILayout.LabelField("TAB 화면 노이즈 출처", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "TAB 점무늬는 Assets/Resources/BattleAnalogTvOverlay.shader가 생성합니다. " +
            "이 컴포넌트의 'TAB 화면 점무늬 노이즈 강도 / 갱신 속도 / 픽셀 크기 / 색상'으로 조절합니다. " +
            "표시할 Image와 런타임 머티리얼은 BattleColorGradingController.Awake → EnsureAnalogOverlay에서 생성됩니다. " +
            "재생 중 아래에 실제 생성된 참조가 표시되며, 위치 표시 버튼으로 Hierarchy에서 찾을 수 있습니다. " +
            "카메라 필름 그레인은 Film Grain Intensity, 캡처 배경은 Tab Background Material로 조절합니다.",
            MessageType.Info);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("화면 노이즈 Image", controller.TabScreenNoiseImage, typeof(UnityEngine.UI.Image), true);
            EditorGUILayout.ObjectField("화면 노이즈 런타임 Material", controller.TabScreenNoiseMaterial, typeof(Material), false);
            EditorGUILayout.ObjectField("캡처 배경 RawImage", controller.TabBackgroundImage, typeof(UnityEngine.UI.RawImage), true);
            EditorGUILayout.ObjectField("캡처 배경 런타임 Material", controller.TabBackgroundRuntimeMaterial, typeof(Material), false);
        }
        using (new EditorGUI.DisabledScope(controller.TabScreenNoiseImage == null))
            if (GUILayout.Button("TAB 화면 노이즈 오브젝트 위치 표시"))
                EditorGUIUtility.PingObject(controller.TabScreenNoiseImage.gameObject);
        EditorGUILayout.Space();
        DrawDefaultInspector();
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

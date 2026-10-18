using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HarnessSpriteShadows
{
    [MenuItem("Tools/Harness/Install Sprite Shadows")]
    private static void Install()
    {
        Shader shader = Resources.Load<Shader>("BattleSpriteShadow");
        if (shader == null)
        {
            Debug.LogError("Battle/SpriteShadow shader is missing or has import errors.");
            return;
        }
        const string path = "Assets/Resources/BattleSpriteShadow.mat";
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
            AssetDatabase.CreateAsset(new Material(shader), path);
        WorldLight light = Object.FindFirstObjectByType<WorldLight>();
        if (light == null)
        {
            GameObject host = new("WorldLight");
            Undo.RegisterCreatedObjectUndo(host, "Install sprite shadows");
            light = Undo.AddComponent<WorldLight>(host);
        }
        BattleFieldCinematicDirector director = Object.FindFirstObjectByType<BattleFieldCinematicDirector>();
        BattleLightingProfileSO profile = director != null ? director.ActiveLightingProfile : null;
        if (profile != null && !light.overrideProfileDefaults)
        {
            Undo.RecordObject(light, "Initialize shadow defaults");
            light.initialProfile = profile;
            light.lightDirection = profile.spriteVolumeFillDirection;
            light.shadowColor = new Color(0.045f * profile.spriteVolumeFillColor.r,
                0.055f * profile.spriteVolumeFillColor.g, 0.075f * profile.spriteVolumeFillColor.b, 1f);
            light.shadowOpacity = profile.contactShadowOpacity;
        }
        light.overrideProfileDefaults = true;
        BattleDecorFloorSource floor = Object.FindFirstObjectByType<BattleDecorFloorSource>();
        if (floor != null && floor.Renderer != null)
            light.shadowSortingLayerName = SortingLayer.IDToName(floor.Renderer.sortingLayerID);
        EditorUtility.SetDirty(light);
        EditorSceneManager.MarkSceneDirty(light.gameObject.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = light.gameObject;
    }

    [MenuItem("Tools/Harness/Add Sprite Shadows To Selection")]
    private static void AttachSelection()
    {
        foreach (GameObject selected in Selection.gameObjects)
        {
            if (!selected.scene.IsValid())
                continue;
            SpriteRenderer renderer = selected.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.GetComponent<BattleDecorFloorSource>() == null)
            {
                BattleSpriteShadow shadow = renderer.GetComponent<BattleSpriteShadow>();
                if (shadow == null)
                    shadow = Undo.AddComponent<BattleSpriteShadow>(renderer.gameObject);
                Undo.RecordObject(shadow, "Enable field sprite shadows");
                shadow.explicitFieldObject = true;
                EditorUtility.SetDirty(shadow);
            }
            EditorSceneManager.MarkSceneDirty(selected.scene);
        }
    }
}

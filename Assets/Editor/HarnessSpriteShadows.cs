using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HarnessSpriteShadows
{
    private static WorldLight rotatingLight;
    private static Vector2 originalDirection;
    private static double rotationStarted;

    [MenuItem("Tools/Harness/Create Shadow Volume Samples")]
    private static void CreateVolumeSamples()
    {
        if (EditorApplication.isPlaying)
            return;
        Install();
        WorldLight light = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<WorldLight>() : null;
        if (light == null)
            return;
        Undo.RecordObject(light, "Preview volume shadows");
        light.shadowMode = BattleShadowMode.Projected;
        light.lightDirection = Vector2.right;
        EditorUtility.SetDirty(light);
        GameObject root = new("HarnessShadowVolumeSamples");
        Camera camera = Camera.main;
        Plane plane = new(Vector3.forward, Vector3.zero);
        if (camera != null)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (plane.Raycast(ray, out float distance))
                root.transform.position = ray.GetPoint(distance);
        }
        GameObject floor = new("SampleFloor");
        floor.transform.SetParent(root.transform, false);
        floor.transform.localPosition = new Vector3(0f, -4f, 0f);
        floor.transform.localScale = new Vector3(6f, 4f, 1f);
        SpriteRenderer floorRenderer = floor.AddComponent<SpriteRenderer>();
        floorRenderer.sprite = SampleSprite(0);
        floorRenderer.color = new Color(0.7f, 0.72f, 0.75f);
        floorRenderer.sortingLayerID = light.ShadowSortingLayerId;
        floorRenderer.sortingOrder = BattleWorldSorting.FloorOrder;
        BattleDecorFloorSource.Ensure(floorRenderer);
        string[] names = { "Round", "Pear", "SplitSilhouette" };
        for (int i = 0; i < names.Length; i++)
        {
            GameObject sample = new(names[i]);
            sample.transform.SetParent(root.transform, false);
            sample.transform.localPosition = new Vector3((i - 1) * 2.1f, i == 1 ? 0.35f : -0.3f, 0f);
            SpriteRenderer renderer = sample.AddComponent<SpriteRenderer>();
            renderer.sprite = SampleSprite(i + 1);
            renderer.color = new Color(0.8f, 0.65f + i * 0.1f, 0.45f);
            renderer.sortingLayerID = light.ShadowSortingLayerId;
            renderer.sortingOrder = BattleWorldSorting.WorldYToOrder(sample.transform.position.y);
            sample.AddComponent<BattleSpriteShadow>().explicitFieldObject = true;
        }
        for (int i = 0; i < 2; i++)
        {
            GameObject sample = new(i == 0 ? "TiledContinuous" : "TiledAdaptive");
            sample.transform.SetParent(root.transform, false);
            sample.transform.localPosition = new Vector3(i == 0 ? -3f : 3f, -3f, 0f);
            SpriteRenderer renderer = sample.AddComponent<SpriteRenderer>();
            renderer.sprite = SampleSprite(3);
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.size = new Vector2(4.5f, 2.5f);
            renderer.tileMode = i == 0 ? SpriteTileMode.Continuous : SpriteTileMode.Adaptive;
            renderer.adaptiveModeThreshold = 0.5f;
            renderer.sortingLayerID = light.ShadowSortingLayerId;
            renderer.sortingOrder = BattleWorldSorting.WorldYToOrder(sample.transform.position.y);
            sample.AddComponent<BattleSpriteShadow>().explicitFieldObject = true;
        }
        Undo.RegisterCreatedObjectUndo(root, "Create shadow volume samples");
        EditorSceneManager.MarkSceneDirty(root.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
    }

    private static Sprite SampleSprite(int shape)
    {
        string path = "Assets/Resources/HarnessShadowVolumeSample" + shape + ".asset";
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Sprite existing)
                return existing;
        const int size = 128;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            name = "ShadowVolumeSample" + shape, filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new((x + 0.5f) * 2f / size - 1f, (y + 0.5f) * 2f / size - 1f);
                float radius = 0.85f;
                bool opaque = p.sqrMagnitude <= radius * radius;
                if (shape == 2)
                    opaque = Mathf.Abs(p.y) < radius && Mathf.Abs(p.x) <=
                        0.72f * (1f - 0.4f * p.y) * Mathf.Sqrt(Mathf.Max(0f, 1f - p.y * p.y / (radius * radius)));
                if (shape == 3)
                    opaque &= p.y < -0.25f || Mathf.Abs(p.x) > 0.25f;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(shape == 0 || opaque ? 255 : 0));
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        AssetDatabase.CreateAsset(texture, path);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0f),
            64f, 0, SpriteMeshType.FullRect);
        sprite.name = texture.name;
        AssetDatabase.AddObjectToAsset(sprite, texture);
        return sprite;
    }

    [MenuItem("Tools/Harness/Rotate Shadow Light")]
    private static void ToggleLightRotation()
    {
        if (rotatingLight != null)
        {
            StopLightRotation();
            return;
        }
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("Enter Play Mode before rotating the shadow light.");
            return;
        }
        rotatingLight = Object.FindFirstObjectByType<WorldLight>();
        if (rotatingLight == null)
            return;
        originalDirection = rotatingLight.lightDirection;
        rotationStarted = EditorApplication.timeSinceStartup;
        rotatingLight.lightDirection = Vector2.right;
        EditorApplication.update += RotateLight;
        Menu.SetChecked("Tools/Harness/Rotate Shadow Light", true);
    }

    private static void RotateLight()
    {
        if (rotatingLight == null || !EditorApplication.isPlaying)
        {
            StopLightRotation();
            return;
        }
        float angle = (float)(EditorApplication.timeSinceStartup - rotationStarted) * Mathf.PI / 6f;
        rotatingLight.lightDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    private static void StopLightRotation()
    {
        EditorApplication.update -= RotateLight;
        if (rotatingLight != null)
            rotatingLight.lightDirection = originalDirection;
        rotatingLight = null;
        Menu.SetChecked("Tools/Harness/Rotate Shadow Light", false);
    }

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

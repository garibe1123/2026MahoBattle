using System.IO;
using UnityEditor;
using UnityEngine;

public static class HarnessElementalSamples
{
    private const string Root = "Assets/Resources/HarnessElemental";
    private const string TestRoot = "Assets/HarnessElementalTests";
    private const int Cell = ElementalTestSamples.Cell;
    private const float PixelsPerUnit = ElementalTestSamples.PixelsPerUnit;
    private const float TestSpacing = 2.5f;
    private static readonly string[] TestNames = ElementalTestSamples.Names;
    private static readonly int[] TestElementIndices = { 0, 2, 3 };
    private static readonly string[] Names = { "Fire", "Frost", "Fog", "Water", "Poison", "Acid" };
    private static readonly Color[] Colors = {
        new(1f, 0.4f, 0.08f), new(0.45f, 0.8f, 1f), new(0.65f, 0.6f, 0.85f),
        new(0.15f, 0.55f, 1f), new(0.55f, 0.85f, 0.2f), new(0.85f, 1f, 0.12f)
    };

    [MenuItem("Tools/Harness/Create Elemental Samples")]
    private static void Create()
    {
        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "HarnessElemental");
        Sprite source = CreateSource();
        Material material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/ElementalLit.mat");
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            if (shader != null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, Root + "/ElementalLit.mat");
            }
        }
        for (int i = 0; i < Names.Length; i++)
        {
            string elementPath = Root + "/" + Names[i] + "Element.asset";
            ElementDefinitionSO element = AssetDatabase.LoadAssetAtPath<ElementDefinitionSO>(elementPath);
            if (element == null)
            {
                element = ScriptableObject.CreateInstance<ElementDefinitionSO>();
                element.displayName = Names[i];
                element.previewColor = Colors[i];
                AssetDatabase.CreateAsset(element, elementPath);
            }
            string visualPath = Root + "/" + Names[i] + "Visual.asset";
            if (AssetDatabase.LoadAssetAtPath<EffectDefinitionSO>(visualPath) != null) continue;
            EffectDefinitionSO definition = ScriptableObject.CreateInstance<EffectDefinitionSO>();
            definition.element = element;
            definition.sourceSheet = source;
            definition.cellSize = new Vector2Int(Cell, Cell);
            definition.layout = new Vector2Int(4, 4);
            definition.customMaterial = material;
            definition.tint = Colors[i];
            definition.emissionColor = Colors[i];
            definition.lightColor = Colors[i];
            definition.family = i == 3 || i == 5 ? EffectFamily.Liquid : EffectFamily.Gas;
            definition.pieceCount = i == 0 ? 5 : i == 2 || i == 4 ? 4 : 1;
            definition.gasRise = i == 2 ? 0.65f : 0.2f;
            definition.gasExpansion = i == 2 ? 0.6f : 0.15f;
            definition.tailOffset = new Vector2(-0.13f, 0.1f);
            definition.useLight = i == 0 || i == 1;
            definition.lightMode = i == 0 ? EffectLightMode.Flicker : EffectLightMode.Steady;
            definition.lightIntensity = 0.8f;
            definition.lightRadius = 1.5f;
            AssetDatabase.CreateAsset(definition, visualPath);
        }
        AssetDatabase.SaveAssets();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<EffectDefinitionSO>(Root + "/FireVisual.asset");
    }

    [MenuItem("Tools/Harness/Create Fire Smoke Water Test")]
    private static void CreateFireSmokeWaterTest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Create();
        EnsureFolder("Assets", "HarnessElementalTests");
        EffectDefinitionSO[] definitions = new EffectDefinitionSO[TestNames.Length];
        for (int i = 0; i < TestNames.Length; i++)
        {
            string path = TestRoot + "/" + TestNames[i] + "TestVisual.asset";
            Sprite source = CreateSource(i, true);
            definitions[i] = AssetDatabase.LoadAssetAtPath<EffectDefinitionSO>(path);
            if (definitions[i] != null)
            {
                Undo.RecordObject(definitions[i], "Update Elemental Test Visual");
                definitions[i].sourceSheet = source;
                ElementalTestSamples.Configure(definitions[i], i);
                EditorUtility.SetDirty(definitions[i]);
                continue;
            }
            EffectDefinitionSO original = AssetDatabase.LoadAssetAtPath<EffectDefinitionSO>(
                Root + "/" + Names[TestElementIndices[i]] + "Visual.asset");
            EffectDefinitionSO definition = Object.Instantiate(original);
            definition.name = TestNames[i] + " Test Visual";
            definition.sourceSheet = source;
            ElementalTestSamples.Configure(definition, i);
            definitions[i] = definition;
            AssetDatabase.CreateAsset(definition, path);
        }
        AssetDatabase.SaveAssets();

        const string testName = "Harness Fire Smoke Water Tests";
        GameObject existing = GameObject.Find(testName);
        if (existing != null)
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                Transform child = existing.transform.Find(TestNames[i] + " Test");
                EffectEmitter emitter = child != null ? child.GetComponent<EffectEmitter>() : null;
                if (emitter == null) continue;
                Undo.RecordObject(emitter, "Update Elemental Test Emitter");
                emitter.definition = definitions[i];
                ElementalTestSamples.ConfigureEmitter(emitter, i);
                EditorUtility.SetDirty(emitter);
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(existing.scene);
            Selection.activeGameObject = existing;
            return;
        }
        Vector3 center = Vector3.zero;
        Camera camera = Camera.main;
        if (camera != null)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Plane ground = new(Vector3.forward, Vector3.zero);
            if (ground.Raycast(ray, out float distance)) center = ray.GetPoint(distance);
        }
        GameObject root = new(testName);
        root.transform.position = center;
        for (int i = 0; i < definitions.Length; i++)
        {
            GameObject host = new(TestNames[i] + " Test");
            host.transform.SetParent(root.transform, false);
            host.transform.localPosition = Vector3.right * ((i - 1) * TestSpacing);
            EffectEmitter emitter = host.AddComponent<EffectEmitter>();
            emitter.definition = definitions[i];
            emitter.mode = EffectEmissionMode.Faucet;
            ElementalTestSamples.ConfigureEmitter(emitter, i);
            emitter.emissionDuration = -1f;
            emitter.playOnStart = true;
        }
        Undo.RegisterCreatedObjectUndo(root, "Create Fire Smoke Water Test");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child);
    }

    private static Sprite CreateSource(int sample = -1, bool refresh = false)
    {
        string path = sample < 0 ? Root + "/TemporaryEffectSheet.png"
            : TestRoot + "/" + TestNames[sample] + "Sheet.png";
        bool exists = File.Exists(path);
        if (!exists || refresh)
        {
            Texture2D texture = ElementalTestSamples.CreateTexture(sample, Cell);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
        }
        if (!exists)
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}

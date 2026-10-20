using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
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

    [InitializeOnLoadMethod]
    private static void PrepareEditableSettingsOnLoad()
    {
        EditorApplication.delayCall += CreateEditableSettings;
    }

    [MenuItem("Tools/Harness/Create Editable Elemental Settings")]
    private static void CreateEditableSettings()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EnsureFolder("Assets", "Shaders");
        EnsureFolder("Assets/Shaders", "Elemental");
        string root = ElementalTestSamples.SettingsRoot;
        EnsureFolder(root, "Sources");
        string sources = root + "/Sources";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(sources + "/ElementalLit.mat");
        bool changed = false;
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            if (shader != null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, sources + "/ElementalLit.mat");
                changed = true;
            }
        }
        for (int i = 0; i < TestNames.Length; i++)
        {
            string path = root + "/" + TestNames[i] + "Visual.asset";
            EffectDefinitionSO data = AssetDatabase.LoadAssetAtPath<EffectDefinitionSO>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<EffectDefinitionSO>();
                ElementalTestSamples.Configure(data, i);
                AssetDatabase.CreateAsset(data, path);
                changed = true;
            }
            bool dirty = false;
            if (data.sourceSheet == null)
            {
                data.sourceSheet = CreateSource(i, false, sources);
                dirty = true;
            }
            if (data.customMaterial == null && material != null)
            {
                data.customMaterial = material;
                dirty = true;
            }
            if (data.element == null)
            {
                string elementPath = sources + "/" + TestNames[i] + "Element.asset";
                ElementDefinitionSO element = AssetDatabase.LoadAssetAtPath<ElementDefinitionSO>(elementPath);
                if (element == null)
                {
                    element = ScriptableObject.CreateInstance<ElementDefinitionSO>();
                    element.displayName = TestNames[i].ToUpperInvariant();
                    element.previewColor = data.tint;
                    AssetDatabase.CreateAsset(element, elementPath);
                }
                data.element = element;
                dirty = true;
            }
            if (!dirty) continue;
            EditorUtility.SetDirty(data);
            changed = true;
        }
        if (changed) AssetDatabase.SaveAssets();
    }

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

    private static Sprite CreateSource(int sample = -1, bool refresh = false, string targetRoot = null)
    {
        string path = targetRoot != null ? targetRoot + "/" + TestNames[sample] + "Sheet.png"
            : sample < 0 ? Root + "/TemporaryEffectSheet.png"
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

[CustomEditor(typeof(EffectDefinitionSO))]
public sealed class EffectDefinitionSOEditor : Editor
{
    private readonly List<EffectDefinitionSO> copies = new();
    private PreviewRenderUtility preview;
    private EffectVisual visual;
    private EffectBlobWorld world;
    private double lastTime;
    private float elapsed;
    private float viewSize = 3f;
    private Random.State randomState;

    private void OnEnable()
    {
        Selection.selectionChanged += ClosePreview;
        AssemblyReloadEvents.beforeAssemblyReload += ClosePreview;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.quitting += ClosePreview;
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= ClosePreview;
        AssemblyReloadEvents.beforeAssemblyReload -= ClosePreview;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.quitting -= ClosePreview;
        ClosePreview();
    }

    public override void OnInspectorGUI()
    {
        bool changed = DrawDefaultInspector();
        if (changed && preview != null) StartPreview();
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("재생")) StartPreview();
                using (new EditorGUI.DisabledScope(visual == null || !visual.IsPlaying))
                    if (GUILayout.Button("정지 (End)")) visual.RequestStop();
                if (GUILayout.Button("재시작")) StartPreview();
                using (new EditorGUI.DisabledScope(preview == null))
                    if (GUILayout.Button("닫기")) ClosePreview();
            }
            viewSize = EditorGUILayout.Slider("프리뷰 범위", viewSize, 0.5f, 12f);
        }
        Rect rect = GUILayoutUtility.GetRect(1f, 260f, GUILayout.ExpandWidth(true));
        if (preview == null)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.14f));
            GUI.Label(rect, "재생하면 Intro → Idle → End를 표시합니다.", EditorStyles.centeredGreyMiniLabel);
            return;
        }
        if (Event.current.type != EventType.Repaint) return;
        preview.BeginPreview(rect, GUIStyle.none);
        try
        {
            preview.camera.orthographicSize = viewSize;
            preview.camera.aspect = rect.width / Mathf.Max(1f, rect.height);
            preview.Render(true);
        }
        finally
        {
            Texture texture = preview.EndPreview();
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }
    }

    private EffectDefinitionSO CopyDefinition(EffectDefinitionSO source, Dictionary<EffectDefinitionSO, EffectDefinitionSO> known)
    {
        if (source == null) return null;
        if (known.TryGetValue(source, out EffectDefinitionSO existing)) return existing;
        EffectDefinitionSO copy = Instantiate(source);
        copy.hideFlags = HideFlags.HideAndDontSave;
        copies.Add(copy);
        known.Add(source, copy);
        copy.smokeTail = CopyDefinition(source.smokeTail, known);
        return copy;
    }

    private void StartPreview()
    {
        ClosePreview();
        if (EditorApplication.isPlayingOrWillChangePlaymode || target == null) return;
        Random.State previousRandom = Random.state;
        try
        {
            EffectDefinitionSO data = CopyDefinition((EffectDefinitionSO)target, new Dictionary<EffectDefinitionSO, EffectDefinitionSO>());
            preview = new PreviewRenderUtility();
            Camera camera = preview.camera;
            camera.orthographic = true;
            camera.orthographicSize = viewSize;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1f, -10f), Quaternion.identity);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            GameObject root = new("Elemental Inspector Preview") { hideFlags = HideFlags.HideAndDontSave };
            preview.AddSingleGO(root);
            world = root.AddComponent<EffectBlobWorld>();
            world.InitializePreview(camera);
            GameObject lightHost = new("Preview Light") { hideFlags = HideFlags.HideAndDontSave };
            lightHost.transform.SetParent(root.transform, false);
            Light2D light = lightHost.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;
            GameObject host = new("Preview Effect") { hideFlags = HideFlags.HideAndDontSave };
            host.transform.SetParent(root.transform, false);
            visual = host.AddComponent<EffectVisual>();
            visual.InitializePreview(camera, world);
            visual.Play(data, data.duration);
            randomState = Random.state;
            lastTime = EditorApplication.timeSinceStartup;
            elapsed = 0f;
            EditorApplication.update += UpdatePreview;
        }
        catch
        {
            ClosePreview();
            throw;
        }
        finally { Random.state = previousRandom; }
        Repaint();
    }

    private void UpdatePreview()
    {
        if (visual == null || !visual.IsPlaying || target == null)
        {
            ClosePreview();
            Repaint();
            return;
        }
        double now = EditorApplication.timeSinceStartup;
        float delta = (float)System.Math.Min(0.1d, System.Math.Max(0d, now - lastTime));
        lastTime = now;
        elapsed += delta;
        Random.State previousRandom = Random.state;
        try
        {
            Random.state = randomState;
            visual.Tick(delta);
            world.Tick(elapsed);
            randomState = Random.state;
        }
        catch
        {
            ClosePreview();
            throw;
        }
        finally { Random.state = previousRandom; }
        Repaint();
    }

    private void OnPlayModeChanged(PlayModeStateChange state) { ClosePreview(); }

    private void ClosePreview()
    {
        EditorApplication.update -= UpdatePreview;
        if (visual != null) visual.DisposePreview();
        if (world != null) world.DisposePreview();
        visual = null;
        world = null;
        if (preview != null)
        {
            preview.Cleanup();
            preview = null;
        }
        foreach (EffectDefinitionSO copy in copies)
            if (copy != null) DestroyImmediate(copy);
        copies.Clear();
    }
}

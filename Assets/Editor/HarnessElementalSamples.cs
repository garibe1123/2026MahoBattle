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
        EditorApplication.delayCall += () => WriteCharacterAndFieldSettings(false);
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

    [MenuItem("Tools/Harness/Create Elemental Size Variants")]
    private static void CreateElementalSizeVariants()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        string root = ElementalTestSamples.SettingsRoot;
        EffectDefinitionSO[] sources = new EffectDefinitionSO[TestNames.Length];
        for (int i = 0; i < sources.Length; i++)
        {
            sources[i] = AssetDatabase.LoadAssetAtPath<EffectDefinitionSO>(root + "/" + TestNames[i] + "Visual.asset");
            if (sources[i] != null) continue;
            EditorUtility.DisplayDialog("크기별 이펙트 생성", TestNames[i] + " 원본 SO가 없습니다. Create Editable Elemental Settings 메뉴를 먼저 실행해주세요.", "확인");
            return;
        }
        string[] sizes = { "big", "middle", "small" };
        float[] scales = { 1.5f, 1f, 0.5f };
        for (int i = 0; i < sources.Length; i++)
        {
            for (int j = 0; j < sizes.Length; j++)
            {
                string name = TestNames[i] + "Visual_" + sizes[j];
                string path = root + "/" + name + ".asset";
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) continue;
                EffectDefinitionSO copy = Object.Instantiate(sources[i]);
                copy.name = name;
                copy.visualScale = sources[i].visualScale * scales[j];
                AssetDatabase.CreateAsset(copy, path);
            }
        }
        AssetDatabase.SaveAssets();
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

    [MenuItem("Tools/Harness/Create Character And Field Effect Settings")]
    [MenuItem("Tools/Harness/Create Character And Field Effect Prefabs")]
    private static void CreateCharacterAndFieldSettings()
    {
        WriteCharacterAndFieldSettings(false);
        CreateCharacterAndFieldPrefabs();
    }

    [MenuItem("Tools/Harness/Refresh Character And Field Effect Settings")]
    private static void RefreshCharacterAndFieldSettings()
    {
        WriteCharacterAndFieldSettings(true);
        CreateCharacterAndFieldPrefabs();
    }

    private static void WriteCharacterAndFieldSettings(bool refresh)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        CreateEditableSettings();
        EffectDefinitionSO fire = AssetDatabase.LoadAssetAtPath<EffectDefinitionSO>(
            ElementalTestSamples.SettingsRoot + "/FireVisual.asset");
        using ElementalAdditionalSamples samples = new(fire);
        EffectDefinitionSO sparkle = null;
        for (int i = samples.Definitions.Length - 1; i >= 0; i--)
        {
            string path = ElementalTestSamples.SettingsRoot + "/" + ElementalAdditionalSamples.Names[i] + "Visual.asset";
            EffectDefinitionSO existing = AssetDatabase.LoadAssetAtPath<EffectDefinitionSO>(path);
            if (existing != null && !refresh)
            {
                if (existing.UpgradeCharacterSettings()) EditorUtility.SetDirty(existing);
                if (i == 6) sparkle = existing;
                continue;
            }
            EffectDefinitionSO data = samples.Definitions[i];
            if (i == 1) data.character.sparkle = sparkle;
            if (existing != null)
            {
                Undo.RecordObject(existing, "Refresh Character And Field Effect Settings");
                EditorUtility.CopySerialized(data, existing);
                data = existing;
            }
            else
            {
                data.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(data, path);
            }
            data.hideFlags = HideFlags.None;
            if (data.customMaterial != null && !AssetDatabase.Contains(data.customMaterial))
            {
                data.customMaterial.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(data.customMaterial, data);
            }
            EffectClipDefinition[] clips = { data.intro, data.idle, data.end };
            foreach (EffectClipDefinition clip in clips)
            {
                if (clip.useSheet) continue;
                foreach (Sprite sprite in clip.sprites)
                {
                    if (sprite == null || AssetDatabase.Contains(sprite)) continue;
                    Texture2D texture = sprite.texture;
                    sprite.hideFlags = texture.hideFlags = HideFlags.None;
                    if (!AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, data);
                    AssetDatabase.AddObjectToAsset(sprite, data);
                }
            }
            if (i == 6) sparkle = data;
            EditorUtility.SetDirty(data);
        }
        AssetDatabase.SaveAssets();
    }

    private static void CreateCharacterAndFieldPrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        string root = ElementalTestSamples.SettingsRoot;
        EnsureFolder(root, "Prefabs");
        for (int i = 0; i < 6; i++)
        {
            string name = ElementalAdditionalSamples.Names[i];
            string path = root + "/Prefabs/" + name + ".prefab";
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) continue;
            EffectDefinitionSO data = AssetDatabase.LoadAssetAtPath<EffectDefinitionSO>(root + "/" + name + "Visual.asset");
            if (data == null) continue;
            GameObject host = new(name);
            try
            {
                if (i < 3) host.AddComponent<CharacterStatusEffectVisual>().defaultDefinition = data;
                else
                {
                    EffectEmitter emitter = host.AddComponent<EffectEmitter>();
                    emitter.definition = data;
                    emitter.mode = EffectEmissionMode.Stationary;
                    emitter.playOnStart = true;
                }
                PrefabUtility.SaveAsPrefabAsset(host, path);
            }
            finally { Object.DestroyImmediate(host); }
        }
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(root + "/Prefabs/PoisonCharacter.prefab");
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
    private bool commonOpen = true;
    private bool familyOpen;
    private bool advancedOpen;

    private void OnEnable()
    {
        if (target is EffectDefinitionSO settings && settings.UpgradeCharacterSettings()) EditorUtility.SetDirty(settings);
        if (target is EffectDefinitionSO data && Selection.activeObject == target)
            EffectDefinitionPreviewWindow.ShowDefinition(data);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawSettings();
        bool changed = serializedObject.ApplyModifiedProperties();
        EffectDefinitionSO data = (EffectDefinitionSO)target;
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("프리뷰 탭 열기")) EffectDefinitionPreviewWindow.ShowDefinition(data);
            if (data.fireParticles && GUILayout.Button(new GUIContent("불 설정을 권장값으로 복원",
                "불 전용 설정·팔레트·불씨·시각 효과를 복원합니다. 원소·시트·클립·수명·머티리얼은 보존하며 Undo로 되돌릴 수 있습니다.")))
            {
                HarnessFireSettings.ResetDefinition(data);
                serializedObject.Update();
                changed = true;
            }
        }
        if (changed) EffectDefinitionPreviewWindow.NotifyChanged(data);
    }

    private SerializedProperty Property(string path) => serializedObject.FindProperty(path);
    private bool Enabled(string path) => Property(path).boolValue;
    private bool Assigned(string path) => Property(path).objectReferenceValue != null;
    private int Mode(string path) => Property(path).enumValueIndex;

    private void Fields(params string[] paths)
    {
        foreach (string path in paths) EditorGUILayout.PropertyField(Property(path), true);
    }

    private void Curves(string path)
    {
        Fields(path + ".enabled");
        if (Enabled(path + ".enabled"))
            Fields(path + ".colorProgress", path + ".opacity", path + ".offsetX", path + ".offsetY");
    }

    private void DrawSettings()
    {
        using (new EditorGUI.DisabledScope(true)) Fields("m_Script");
        commonOpen = EditorGUILayout.Foldout(commonOpen, "공통 · 기본 조절", true);
        if (commonOpen)
        {
            Fields("element", "family", "usage", "fireParticles", "character.shape", "beam.kind");
            Fields("visualScale", "countValue", "duration");
            if (Mode("beam.kind") == (int)EffectBeamKind.None || DetailEnabled()) Fields("tint");
        }

        bool beam = Mode("beam.kind") != (int)EffectBeamKind.None;
        bool shape = !beam && Mode("character.shape") != (int)EffectCharacterShape.None;
        bool fire = Enabled("fireParticles");
        bool sprites = !beam && !shape && !fire;
        bool liquid = Mode("family") == (int)EffectFamily.Liquid;
        familyOpen = EditorGUILayout.Foldout(familyOpen, "계열별 세부 설정", true);
        if (familyOpen)
        {
            if (beam) DrawBeam();
            else if (shape)
            {
                Fields("character.shapeAspect", "character.shapeCore", "character.shapeEdge");
                if (Mode("character.shape") == (int)EffectCharacterShape.PoisonBubble)
                    Fields("character.shapeHighlight");
            }
            else if (fire) DrawFire();
            else
            {
                Fields("pieceCount", "mixedPieceSizes");
                if (Enabled("mixedPieceSizes")) Fields("pieceSizeScales");
                if (Enabled("mixedPieceSizes") || Property("pieceCount").intValue > 1)
                {
                    Fields("pieceDelay", "pieceAttenuation", "tailTint", "tailExpansion", "edgeSpread");
                    if (!Enabled("mixedPieceSizes")) Fields("tailOffset");
                    Fields("smokeTail");
                    if (Assigned("smokeTail")) Fields("smokeTailPieces");
                }
                if (!liquid) Fields("gasRise", "gasExpansion");
                else Fields("puddleScale");
                Fields("mergeNearby");
                if (Enabled("mergeNearby")) DrawSurface(liquid);
            }
            if (liquid)
            {
                Fields("airborneDuration");
                if (sprites && Property("airborneDuration").floatValue > 0f) Fields("airborneHeight");
            }
            DrawDetails(fire, liquid);
        }

        advancedOpen = EditorGUILayout.Foldout(advancedOpen, "고급 설정", true);
        if (!advancedOpen) return;
        Fields("additionalElements", "idleEnd", "shortLife", "introStop");
        DrawClips((beam || shape) && !DetailEnabled());
        Fields("customMaterial");
        if (!beam)
        {
            if (sprites) Fields("introBrightness", "idleBrightness", "endBrightness", "fadeOnEnd");
            Curves("visualCurves");
            if (sprites || DetailEnabled() || fire) Fields("spentTint");
            Fields("emissionColor", "emissionStrength", "lightResponse", "rimStrength", "centerStrength",
                "emissionColorProperty", "emissionStrengthProperty", "lightResponseProperty", "rimProperty", "centerProperty");
        }
        if (!beam && !shape)
        {
            Fields("sizeVariation");
            if (sprites) Fields("shapeVariation");
        }
        Fields("motionWander");
        if (sprites || fire || Property("motionWander").floatValue > 0f) Fields("variationFrequency");
        if (!beam && !shape)
        {
            Fields("useLight");
            if (Enabled("useLight"))
            {
                Fields("lightMode", "lightColor", "lightIntensity", "lightRadius");
                if (Mode("lightMode") == (int)EffectLightMode.Flicker)
                    Fields("flickerAmount", "flickerFrequency");
            }
        }
        Fields("sortingMode");
        int sort = Mode("sortingMode");
        if (sort == (int)EffectSortMode.Automatic)
            sort = fire ? (int)EffectSortMode.SpawnDepth : liquid ? (int)EffectSortMode.Ground : (int)EffectSortMode.Definition;
        Fields("sortingLayerId");
        if (sort == (int)EffectSortMode.Definition || sort == (int)EffectSortMode.Attached) Fields("sortingOrder");
        EditorGUILayout.LabelField("캐릭터 부착", EditorStyles.boldLabel);
        Fields("character.duration", "character.radiusInBodyWidths", "character.emissionInterval",
            "character.particleLifetime", "character.particleSize", "character.tintAmount");
        Fields("character.fullFollowFraction", "character.maxLagInBodyWidths", "character.fadeStart");
        if (Mode("character.shape") == (int)EffectCharacterShape.FrostShard)
            Fields("character.frostFallDuration", "character.frostFallInBodyHeights");
        if (Property("character.tintAmount").floatValue > 0f) Fields("character.tint");
        Fields("character.sparkle");
        if (Assigned("character.sparkle"))
        {
            Fields("character.sparkleChance");
            if (Property("character.sparkleChance").floatValue > 0f) Fields("character.sparkleSize");
        }
        EditorGUILayout.LabelField("향후 연동 데이터", EditorStyles.boldLabel);
        Fields("attachmentOffset", "spawnOnImpact", "projectileVisualScale");
    }

    private bool DetailEnabled()
    {
        string path = Enabled("fireParticles") ? "embers" :
            Mode("family") == (int)EffectFamily.Liquid ? "waterDetails" : "gasDetails";
        return Enabled(path + ".enabled");
    }

    private void DrawClips(bool procedural)
    {
        EditorGUILayout.LabelField("단계별 애니메이션", EditorStyles.boldLabel);
        Fields("sourceSheet");
        if (!procedural && (Enabled("intro.useSheet") || Enabled("idle.useSheet") || Enabled("end.useSheet")))
            Fields("cellSize", "layout");
        foreach (string path in new[] { "intro", "idle", "end" })
        {
            EditorGUILayout.LabelField(Property(path).displayName, EditorStyles.miniBoldLabel);
            if (!procedural) Fields(path + ".useSheet", path + ".startIndex");
            Fields(path + ".frameCount", path + ".fps");
            if (!procedural && !Enabled(path + ".useSheet")) Fields(path + ".sprites");
        }
    }

    private void DrawFire()
    {
        Fields("pieceCount", "fireSourceCount", "fireSourceScale", "fireSourceSpread", "fireParticleCapacity",
            "fireEmissionRate", "fireEmissionIntervalScale", "fireParticleLifetime", "fireParticleRadius",
            "fireShapeScale", "fireSpread", "fireFadeStart", "fireFlameDuration", "fireSurgeStrength");
        Fields("fireSurgeFrequency", "gasRise", "gasExpansion", "mergeSoftness", "fireSmokeOpacity",
            "fireSmokeSpread", "fireSmokeExpansion", "fireSmokeEdgeFade");
        Fields("fireContourVariation", "fireTipSharpness", "fireTurbulence", "fireTearStrength",
            "fireEdgeStrength");
        if (Property("fireEdgeStrength").floatValue > 0f) Fields("fireEdgeWidth");
        Fields("fireHotColor", "fireGoldColor", "fireWarmColor", "fireEmberColor", "fireSmokeColor",
            "fireSootColor", "firePaletteWeights");
    }

    private void DrawSurface(bool liquid)
    {
        Fields("mergeRadius", "mergeSoftness", "mergeRefreshRate", "surfaceNoiseStrength");
        if (!liquid || Property("surfaceSpecular").floatValue > 0f)
            Fields("surfaceNoiseScale", "surfaceNoiseSpeed");
        Fields("surfaceBodyColor", "surfaceRelief");
        if (liquid) Fields("surfaceHighlightStrength");
        bool highlights = liquid && Property("surfaceHighlightStrength").floatValue > 0f;
        if (highlights) Fields("surfaceHighlightColor", "surfaceSpecular");
        if ((!liquid && Property("surfaceRelief").floatValue > 0f) ||
            (highlights && Property("surfaceSpecular").floatValue > 0f))
            Fields("surfaceLightDirection");
        if (!liquid && Property("surfaceRelief").floatValue > 0f) Fields("surfaceShadowColor");
        if (highlights && Property("surfaceSpecular").floatValue > 0f) Fields("surfaceShininess");
        if (liquid)
        {
            Fields("impactFoamStrength");
            if (highlights) Fields("foamOpacity");
            if (Property("impactFoamStrength").floatValue > 0f) Fields("impactFoamDuration");
        }
    }

    private void DrawDetails(bool fire, bool liquid)
    {
        string path = fire ? "embers" : liquid ? "waterDetails" : "gasDetails";
        EditorGUILayout.LabelField(fire ? "불씨" : liquid ? "물방울" : "구름 입자", EditorStyles.boldLabel);
        Fields(path + ".enabled");
        if (!Enabled(path + ".enabled")) return;
        Fields(path + ".capacity", path + ".emissionRate", path + ".minimumInterval",
            path + ".lifetime", path + ".spread", path + ".rise");
        if (fire)
            Fields(path + ".updraftAcceleration", "emberStartColor", "emberEndColor", "emberLifetimeScale",
                "emberTravelScale", "emberFadeStart");
        else if (!liquid)
            Fields(path + ".radius", path + ".expansion", "cloudDetailInterval", "cloudDetailOpacity");
        Curves(path + ".visualCurves");
    }

    private void DrawBeam()
    {
        int kind = Mode("beam.kind");
        if (kind != (int)EffectBeamKind.LightningStrike) Fields("beam.direction");
        Fields("beam.length", "beam.width", "beam.coreWidth", "beam.core", "beam.edge");
        if (kind != (int)EffectBeamKind.Laser) Fields("beam.zigzag", "beam.segments");
        Fields("beam.refreshRate", "beam.glowRadius");
        if (Property("beam.glowRadius").floatValue > 0f) Fields("beam.glowOpacity");
        Fields("beam.pulseStrength");
        if (Property("beam.pulseStrength").floatValue > 0f) Fields("beam.pulseFrequency");
        Fields("beam.flowSpeed", "beam.wobble", "beam.chargeRadius");
        if (kind == (int)EffectBeamKind.Laser) Fields("beam.chargeDuration", "beam.extensionDuration");
        Fields("beam.sparkCount");
        if (Property("beam.sparkCount").intValue > 0) Fields("beam.sparkLifetime", "beam.sparkSpread");
        Fields("beam.useLight");
        if (Enabled("beam.useLight")) Fields("beam.lightIntensity", "beam.lightRadius");
    }
}

[CustomEditor(typeof(CharacterStatusEffectVisual))]
public sealed class CharacterStatusEffectVisualEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EffectDefinitionSO data = ((CharacterStatusEffectVisual)target).defaultDefinition;
        using (new EditorGUI.DisabledScope(data == null || EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Effect Preview")) EffectDefinitionPreviewWindow.ShowDefinition(data);
    }
}

[CustomEditor(typeof(EffectEmitter))]
public sealed class EffectEmitterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EffectDefinitionSO data = ((EffectEmitter)target).definition;
        using (new EditorGUI.DisabledScope(data == null || EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Effect Preview")) EffectDefinitionPreviewWindow.ShowDefinition(data);
    }
}

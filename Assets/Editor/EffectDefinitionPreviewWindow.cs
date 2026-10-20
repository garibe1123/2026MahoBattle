using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed class EffectDefinitionPreviewWindow : EditorWindow
{
    private static EffectDefinitionPreviewWindow instance;
    [SerializeField] private EffectDefinitionSO definition;
    [SerializeField] private float viewSize = 3f;
    [SerializeField] private bool repeat = true;
    private readonly List<EffectDefinitionSO> copies = new();
    private PreviewRenderUtility preview;
    private EffectDefinitionSO previewDefinition;
    private EffectVisual visual;
    private CharacterStatusEffectVisual characterVisual;
    private Texture2D bodyTexture;
    private Sprite bodySprite;
    private Material bodyMaterial;
    private EffectBlobWorld world;
    private double lastTime;
    private float elapsed;
    private int definitionVersion;
    private Random.State randomState;
    private bool stopRequested, refreshRequested;

    [MenuItem("Tools/Harness/Effect Definition Preview")]
    private static void Open() => ShowDefinition(SelectedDefinition());

    private static EffectDefinitionSO SelectedDefinition()
    {
        if (Selection.activeObject is EffectDefinitionSO data) return data;
        GameObject host = Selection.activeGameObject;
        if (host == null) return null;
        CharacterStatusEffectVisual status = host.GetComponentInChildren<CharacterStatusEffectVisual>(true);
        if (status != null) return status.defaultDefinition;
        EffectEmitter emitter = host.GetComponentInChildren<EffectEmitter>(true);
        return emitter != null ? emitter.definition : null;
    }

    public static void ShowDefinition(EffectDefinitionSO data)
    {
        EffectDefinitionPreviewWindow window = GetWindow<EffectDefinitionPreviewWindow>("이펙트 프리뷰", false);
        window.Show();
        if (window.definition == data && window.preview != null) return;
        window.ClosePreview();
        window.definition = data;
        window.viewSize = data != null && data.ResolvedSorting == EffectSortMode.Attached ? 1.5f : 3f;
        window.StartPreview();
        window.Repaint();
    }

    public static void NotifyChanged(EffectDefinitionSO data)
    {
        if (instance == null || instance.definition != data) return;
        if (instance.preview == null) instance.StartPreview();
        else instance.refreshRequested = true;
    }

    private void OnEnable()
    {
        instance = this;
        minSize = new Vector2(320f, 280f);
        Selection.selectionChanged += OnSelectionChanged;
        Undo.undoRedoPerformed += OnDefinitionChanged;
        AssemblyReloadEvents.beforeAssemblyReload += ClosePreview;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.quitting += ClosePreview;
        definition = SelectedDefinition();
        if (definition != null && definition.ResolvedSorting == EffectSortMode.Attached) viewSize = 1.5f;
        StartPreview();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
        Undo.undoRedoPerformed -= OnDefinitionChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= ClosePreview;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.quitting -= ClosePreview;
        ClosePreview();
        if (instance == this) instance = null;
    }

    private void OnSelectionChanged()
    {
        EffectDefinitionSO selected = SelectedDefinition();
        if (definition == selected) return;
        ClosePreview();
        definition = selected;
        viewSize = selected != null && selected.ResolvedSorting == EffectSortMode.Attached ? 1.5f : 3f;
        StartPreview();
        Repaint();
    }

    private void OnDefinitionChanged() => NotifyChanged(definition);

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        ClosePreview();
        if (state == PlayModeStateChange.EnteredEditMode) StartPreview();
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(definition != null ? definition.name : "EffectDefinitionSO를 선택하세요.", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(definition == null || EditorApplication.isPlayingOrWillChangePlaymode))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("재생")) StartPreview();
                using (new EditorGUI.DisabledScope(!(visual != null && visual.IsPlaying) &&
                    !(characterVisual != null && characterVisual.IsPlaying)))
                    if (GUILayout.Button("정지 (End)"))
                    {
                        stopRequested = true;
                        if (characterVisual != null) characterVisual.Stop();
                        else visual.RequestStop();
                    }
                if (GUILayout.Button("재시작")) StartPreview();
                repeat = GUILayout.Toggle(repeat, new GUIContent("반복", "유한 수명 효과도 Intro·Idle·End를 마치면 처음부터 다시 재생합니다."), "Button");
            }
        }
        viewSize = EditorGUILayout.Slider("프리뷰 범위", viewSize, 0.5f, 12f);
        Rect rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (preview == null)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.14f));
            GUI.Label(rect, "SO 선택 시 자동 재생 · 변수 변경 시 즉시 반영", EditorStyles.centeredGreyMiniLabel);
            return;
        }
        if (Event.current.type != EventType.Repaint || rect.width <= 0f || rect.height <= 0f) return;
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
        if (copy.character != null)
            copy.character.sparkle = CopyDefinition(source.character.sparkle, known);
        return copy;
    }

    private SpriteRenderer CreatePreviewBody(Transform parent)
    {
        const int width = 16, height = 24;
        bodyTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
        Color32[] pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool head = y >= 16 && y <= 22 && x >= 4 && x <= 11;
                bool torso = y >= 6 && y <= 15 && x >= 2 && x <= 13;
                bool legs = y < 6 && (x >= 3 && x <= 6 || x >= 9 && x <= 12);
                if (head || torso || legs) pixels[y * width + x] = new Color32(185, 185, 185, 255);
            }
        bodyTexture.SetPixels32(pixels);
        bodyTexture.Apply(false, true);
        bodySprite = Sprite.Create(bodyTexture, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), 16f);
        bodySprite.hideFlags = HideFlags.HideAndDontSave;
        GameObject host = new("Preview Character") { hideFlags = HideFlags.HideAndDontSave };
        host.transform.SetParent(parent, false);
        SpriteRenderer body = host.AddComponent<SpriteRenderer>();
        body.sprite = bodySprite;
        body.sortingLayerID = previewDefinition.sortingLayerId;
        body.sortingOrder = previewDefinition.sortingOrder;
        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader != null)
        {
            bodyMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            body.sharedMaterial = bodyMaterial;
        }
        return body;
    }

    private void StartPreview()
    {
        ClosePreview();
        if (EditorApplication.isPlayingOrWillChangePlaymode || definition == null) return;
        Random.State previousRandom = Random.state;
        try
        {
            previewDefinition = CopyDefinition(definition, new Dictionary<EffectDefinitionSO, EffectDefinitionSO>());
            preview = new PreviewRenderUtility();
            Camera camera = preview.camera;
            camera.orthographic = true;
            camera.orthographicSize = viewSize;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1f, -10f), Quaternion.identity);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            GameObject root = new("Elemental Preview") { hideFlags = HideFlags.HideAndDontSave };
            preview.AddSingleGO(root);
            world = root.AddComponent<EffectBlobWorld>();
            world.pixelSizeMultiplier = EffectBlobWorld.DefaultPixelSize;
            world.InitializePreview(camera);
            GameObject lightHost = new("Preview Light") { hideFlags = HideFlags.HideAndDontSave };
            lightHost.transform.SetParent(root.transform, false);
            Light2D light = lightHost.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;
            GameObject host = new("Preview Effect") { hideFlags = HideFlags.HideAndDontSave };
            host.transform.SetParent(root.transform, false);
            if (previewDefinition.ResolvedSorting == EffectSortMode.Attached)
            {
                SpriteRenderer body = CreatePreviewBody(root.transform);
                characterVisual = host.AddComponent<CharacterStatusEffectVisual>();
                characterVisual.InitializePreview(body, camera, world);
                characterVisual.Play(previewDefinition);
            }
            else
            {
                if (previewDefinition.beam != null && previewDefinition.beam.kind != EffectBeamKind.None)
                {
                    EffectBeamSettings beam = previewDefinition.beam;
                    Vector2 direction = beam.kind == EffectBeamKind.LightningStrike ? Vector2.up :
                        beam.direction.sqrMagnitude > 0.0001f ? beam.direction.normalized : Vector2.right;
                    Vector2 center = direction * (beam.length * previewDefinition.visualScale * 0.5f);
                    camera.transform.position = new Vector3(center.x, center.y, -10f);
                }
                visual = host.AddComponent<EffectVisual>();
                visual.InitializePreview(camera, world);
                visual.Play(previewDefinition, previewDefinition.duration);
            }
            definitionVersion = EditorUtility.GetDirtyCount(definition);
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
        if (definition == null || (visual == null && characterVisual == null))
        {
            ClosePreview();
            Repaint();
            return;
        }
        if (refreshRequested || definitionVersion != EditorUtility.GetDirtyCount(definition))
        {
            StartPreview();
            return;
        }
        double now = EditorApplication.timeSinceStartup;
        float delta = (float)System.Math.Min(0.1d, System.Math.Max(0d, now - lastTime));
        lastTime = now;
        elapsed += delta;
        Random.State previousRandom = Random.state;
        bool finished = false;
        try
        {
            Random.state = randomState;
            if (characterVisual != null)
            {
                characterVisual.Tick(delta);
                if (!characterVisual.IsPlaying)
                {
                    if (repeat && !stopRequested) characterVisual.Play(previewDefinition);
                    else finished = true;
                }
            }
            else
            {
                visual.Tick(delta);
                if (!visual.IsPlaying)
                {
                    if (repeat && !stopRequested) visual.Play(previewDefinition, previewDefinition.duration);
                    else finished = true;
                }
            }
            world.Tick(elapsed);
            randomState = Random.state;
        }
        catch
        {
            ClosePreview();
            throw;
        }
        finally { Random.state = previousRandom; }
        if (finished) ClosePreview();
        Repaint();
    }

    private void ClosePreview()
    {
        EditorApplication.update -= UpdatePreview;
        if (characterVisual != null) characterVisual.Stop();
        if (visual != null) visual.DisposePreview();
        if (world != null) world.DisposePreview();
        visual = null;
        characterVisual = null;
        world = null;
        previewDefinition = null;
        stopRequested = refreshRequested = false;
        if (preview != null)
        {
            preview.Cleanup();
            preview = null;
        }
        if (bodySprite != null) DestroyImmediate(bodySprite);
        if (bodyTexture != null) DestroyImmediate(bodyTexture);
        if (bodyMaterial != null) DestroyImmediate(bodyMaterial);
        bodySprite = null;
        bodyTexture = null;
        bodyMaterial = null;
        foreach (EffectDefinitionSO copy in copies)
            if (copy != null) DestroyImmediate(copy);
        copies.Clear();
    }
}

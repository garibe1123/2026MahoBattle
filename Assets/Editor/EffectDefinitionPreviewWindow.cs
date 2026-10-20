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
    private EffectBlobWorld world;
    private double lastTime;
    private float elapsed;
    private int definitionVersion;
    private Random.State randomState;
    private bool stopRequested, refreshRequested;

    [MenuItem("Tools/Harness/Effect Definition Preview")]
    private static void Open() => ShowDefinition(Selection.activeObject as EffectDefinitionSO);

    public static void ShowDefinition(EffectDefinitionSO data)
    {
        EffectDefinitionPreviewWindow window = GetWindow<EffectDefinitionPreviewWindow>("이펙트 프리뷰", false);
        window.Show();
        if (window.definition == data && window.preview != null) return;
        window.ClosePreview();
        window.definition = data;
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
        definition = Selection.activeObject as EffectDefinitionSO;
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
        EffectDefinitionSO selected = Selection.activeObject as EffectDefinitionSO;
        if (definition == selected) return;
        ClosePreview();
        definition = selected;
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
                using (new EditorGUI.DisabledScope(visual == null || !visual.IsPlaying))
                    if (GUILayout.Button("정지 (End)"))
                    {
                        stopRequested = true;
                        visual.RequestStop();
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
        return copy;
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
            visual.Play(previewDefinition, previewDefinition.duration);
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
        if (definition == null || visual == null)
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
            visual.Tick(delta);
            if (!visual.IsPlaying)
            {
                if (repeat && !stopRequested) visual.Play(previewDefinition, previewDefinition.duration);
                else finished = true;
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
        if (visual != null) visual.DisposePreview();
        if (world != null) world.DisposePreview();
        visual = null;
        world = null;
        previewDefinition = null;
        stopRequested = refreshRequested = false;
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

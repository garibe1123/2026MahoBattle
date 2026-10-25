using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class HarnessBattleUiPreviewWindow : EditorWindow
{
    private enum Screen { Hud, Inventory, Reward, Roulette, Pause, Result, Settings }
    private BattleShowPresentationManager manager;
    private Screen screen;
    private PreviewRenderUtility preview;
    private GameObject root;
    private RectTransform content;
    private BattleUiStyleController styles;
    private Screen builtScreen;
    private Font font;
    private bool previewScreenTone = true;
    private Material toneMaterial;
    private readonly RenderTexture[] toneTextures = new RenderTexture[5];
    private Graphic[] previewGraphics;

    [MenuItem("Tools/Harness/Battle UI Preview")]
    private static void Open()
    {
        ShowFor(Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<BattleShowPresentationManager>() : null);
    }

    public static void ShowFor(BattleShowPresentationManager target)
    {
        HarnessBattleUiPreviewWindow window = GetWindow<HarnessBattleUiPreviewWindow>("Battle UI Preview");
        window.manager = target;
        window.minSize = new Vector2(640f, 440f);
        window.Show();
    }

    private void OnEnable() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        ClosePreview();
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode) ClosePreview();
    }

    private void OnInspectorUpdate() => Repaint();

    private void OnGUI()
    {
        manager = (BattleShowPresentationManager)EditorGUILayout.ObjectField(
            "Presentation Manager", manager, typeof(BattleShowPresentationManager), true);
        screen = (Screen)GUILayout.Toolbar((int)screen, new[]
            { "HUD", "Inventory", "Reward", "Roulette", "Pause", "Result", "Settings" });
        EditorGUILayout.HelpBox("공통 UI 생성·스타일 경로로 그린 레이아웃 샘플입니다. Inspector에서 테마와 색상을 바꾸면 반영됩니다. 게임 데이터와 입력·전환 동작은 Play Mode에서 확인하세요.", MessageType.Info);
        if (manager == null || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.LabelField("편집 모드에서 Presentation Manager를 연결하세요.");
            return;
        }
        EditorGUILayout.LabelField("Theme", manager.UiStyle.ToString());
        previewScreenTone = EditorGUILayout.Toggle("Screen Tone Preview", previewScreenTone);
        Rect available = GUILayoutUtility.GetRect(1f, 10000f, 1f, 10000f);
        Rect view = new(available.x, available.y, available.width, available.width * 9f / 16f);
        if (view.height > available.height)
        {
            view.height = available.height;
            view.width = view.height * 16f / 9f;
        }
        if (view.width < 1f || view.height < 1f || Event.current.type != EventType.Repaint) return;
        EnsurePreview();
        if (content == null || builtScreen != screen) BuildScreen();
        if (previewScreenTone && manager.EnableScreenTone && manager.ScreenToneSettings != null &&
            manager.ScreenToneSettings.ToneStrength > 0f)
        {
            DrawScreenTonePreview(view);
            return;
        }
        preview.BeginPreview(view, GUIStyle.none);
        Texture texture = null;
        try
        {
            styles.DrawEditorPreview(manager, root.transform, () => preview.Render(true));
        }
        finally { texture = preview.EndPreview(); }
        GUI.DrawTexture(view, texture, ScaleMode.StretchToFill, false);
    }

    private void EnsurePreview()
    {
        if (preview != null) return;
        preview = new PreviewRenderUtility();
        Camera camera = preview.camera;
        camera.orthographic = true;
        camera.orthographicSize = 540f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 2000f;
        camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -1000f), Quaternion.identity);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.025f, 0.08f);
        root = new GameObject("Battle UI Preview", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
        preview.AddSingleGO(root);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        ((RectTransform)root.transform).sizeDelta = new Vector2(1920f, 1080f);
        styles = root.AddComponent<BattleUiStyleController>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private void BuildScreen()
    {
        if (content != null)
        {
            // Recreate the controller with the sample to release its registered graphics and materials.
            ClosePreview();
            EnsurePreview();
        }
        builtScreen = screen;
        content = Rect(root.transform, "Sample", Vector2.zero, new Vector2(1920f, 1080f));
        Label(content, "ScreenTitle", screen == Screen.Result ? "STAGE 2 CLEAR" : "MAHO BATTLE / " + screen.ToString().ToUpperInvariant(),
            new Vector2(-350f, 430f), new Vector2(1000f, 100f), 52);
        switch (screen)
        {
            case Screen.Hud:
                Panel(content, "BackpackMiniGrid", new Vector2(-650f, -260f), new Vector2(460f, 430f));
                Slots(content, new Vector2(-800f, -130f), 112f);
                RectTransform hud = Panel(content, "CurrentItem", new Vector2(570f, -340f), new Vector2(650f, 230f));
                Label(hud, "ItemTitle", "BASIC PISTOL", new Vector2(0f, 50f), new Vector2(550f, 60f), 32);
                Label(hud, "Status", "HP  100 / 100     ST  80 / 100", new Vector2(0f, -25f), new Vector2(550f, 60f), 24);
                break;
            case Screen.Inventory:
                Panel(content, "GridBoard", new Vector2(-430f, -40f), new Vector2(750f, 680f));
                Slots(content, new Vector2(-655f, 175f), 180f);
                Detail(new Vector2(490f, 0f));
                Button(content, "DoneButton", "DONE / START", new Vector2(480f, -400f), true);
                break;
            case Screen.Reward:
                for (int i = 0; i < 3; i++)
                {
                    RectTransform card = Panel(content, "RewardCard_" + i, new Vector2(-570f + i * 570f, 30f), new Vector2(470f, 510f));
                    Label(card, "ItemTitle", "REWARD " + (i + 1), new Vector2(0f, 150f), new Vector2(390f, 70f), 36);
                    Label(card, "Description", "RARE / MANUAL\nDMG x1.20\nMOVE x1.05", Vector2.zero, new Vector2(390f, 170f), 25);
                    card.GetComponentInChildren<BattleUiPanelDecoration>().SetEmphasis(i == 1, i == 1);
                }
                Button(content, "ConfirmButton", "CONFIRM REWARD", new Vector2(420f, -390f), true);
                Button(content, "SkipButton", "SKIP", new Vector2(-420f, -390f), false);
                break;
            case Screen.Roulette:
                RectTransform roulette = Panel(content, "RulePanel", new Vector2(0f, 70f), new Vector2(1400f, 460f));
                Label(roulette, "RuleTitle", "RULE ROULETTE", new Vector2(0f, 100f), new Vector2(1150f, 100f), 58);
                Label(roulette, "Rules", "POWER UP  /  SPEED UP  /  BONUS REWARD", new Vector2(0f, -60f), new Vector2(1150f, 100f), 32);
                Button(content, "StartBattleButton", "START BATTLE", new Vector2(430f, -340f), true);
                break;
            case Screen.Pause:
            case Screen.Settings:
                RectTransform menu = Panel(content, "MenuPanel", Vector2.zero, new Vector2(950f, 680f));
                Label(menu, "MenuTitle", screen == Screen.Pause ? "PAUSED" : "SETTINGS", new Vector2(0f, 230f), new Vector2(800f, 90f), 54);
                Button(menu, "ResumeButton", screen == Screen.Pause ? "RESUME" : "APPLY", new Vector2(0f, 80f), true);
                Button(menu, "SettingsButton", screen == Screen.Pause ? "SETTINGS" : "AUDIO  80%", new Vector2(0f, -70f), false);
                Button(menu, "CloseButton", "CLOSE", new Vector2(0f, -220f), false);
                break;
            case Screen.Result:
                RectTransform result = Panel(content, "ResultPanel", new Vector2(0f, 50f), new Vector2(1450f, 520f));
                Label(result, "ResultTitle", "VICTORY!", new Vector2(0f, 150f), new Vector2(1200f, 100f), 70);
                Label(result, "Results", "375 DESTROYED    /    1397 COLLECTED\nTIME SURVIVED  20:29", new Vector2(0f, -35f), new Vector2(1200f, 170f), 36);
                Button(content, "ReplayButton", "REPLAY", new Vector2(410f, -370f), true);
                Button(content, "ReturnButton", "RETURN", new Vector2(-410f, -370f), false);
                break;
        }
    }

    private void Detail(Vector2 position)
    {
        RectTransform detail = Panel(content, "DetailPanel", position, new Vector2(650f, 610f));
        Label(detail, "ItemTitle", "BASIC PISTOL", new Vector2(0f, 200f), new Vector2(560f, 90f), 42);
        Label(detail, "Description", "RARE / MANUAL\n\nItem description / equipment details\n\nDMG x1.20   MOVE x1.05\nRANGE x1.10", new Vector2(0f, -30f), new Vector2(540f, 300f), 26);
    }

    private void Slots(Transform parent, Vector2 start, float size)
    {
        for (int i = 0; i < 9; i++)
        {
            RectTransform slot = Panel(parent, "GridSlot_" + i, start + new Vector2(i % 3 * (size + 18f), -i / 3 * (size + 18f)), new Vector2(size, size));
            Label(slot, "Name", i < 3 ? "ITEM " + (i + 1) : "EMPTY", Vector2.zero, new Vector2(size - 12f, 50f), 21);
            slot.GetComponentInChildren<BattleUiPanelDecoration>().SetEmphasis(i == 0, i == 0);
        }
    }

    private void Button(Transform parent, string name, string caption, Vector2 position, bool primary)
    {
        RectTransform rect = Panel(parent, name, position, new Vector2(primary ? 590f : 460f, primary ? 120f : 100f));
        UnityEngine.UI.Button button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        Label(rect, "Label", caption, Vector2.zero, new Vector2(rect.sizeDelta.x - 50f, 80f), 34);
    }

    private RectTransform Panel(Transform parent, string name, Vector2 position, Vector2 size)
    {
        RectTransform rect = Rect(parent, name, position, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.06f, 0.04f, 0.12f, 1f);
        image.raycastTarget = false;
        BattleUiPanelDecoration.Attach(rect, new Color(0.12f, 0.88f, 0.92f), animate: false);
        return rect;
    }

    private void Label(Transform parent, string name, string value, Vector2 position, Vector2 size, int pixels)
    {
        Text text = Rect(parent, name, position, size).gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = pixels;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
    {
        RectTransform rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private void ClosePreview()
    {
        if (toneMaterial != null) DestroyImmediate(toneMaterial);
        toneMaterial = null;
        ReleaseToneTextures();
        previewGraphics = null;
        if (styles != null) styles.ReleaseEditorPreview();
        if (root != null) DestroyImmediate(root);
        root = null;
        content = null;
        styles = null;
        preview?.Cleanup();
        preview = null;
    }

    private void DrawScreenTonePreview(Rect view)
    {
        if (toneMaterial == null)
        {
            Shader shader = Resources.Load<Shader>("BattleUiOptics");
            if (shader == null) return;
            toneMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        int width = Mathf.Max(1, Mathf.RoundToInt(view.width * EditorGUIUtility.pixelsPerPoint));
        int height = Mathf.Max(1, Mathf.RoundToInt(view.height * EditorGUIUtility.pixelsPerPoint));
        if (toneTextures[0] == null || toneTextures[0].width != width || toneTextures[0].height != height)
        {
            ReleaseToneTextures();
            for (int i = 0; i < toneTextures.Length; i++)
            {
                toneTextures[i] = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf)
                    { hideFlags = HideFlags.HideAndDontSave };
                toneTextures[i].Create();
            }
        }
        if (previewGraphics == null) previewGraphics = root.GetComponentsInChildren<Graphic>(true);
        bool[] culled = new bool[previewGraphics.Length];
        Color background = preview.camera.backgroundColor;
        RenderTexture previousActive = RenderTexture.active;
        for (int i = 0; i < culled.Length; i++) culled[i] = previewGraphics[i].canvasRenderer.cull;
        try
        {
            styles.DrawEditorPreview(manager, root.transform, () =>
            {
                for (int pass = 0; pass < 4; pass++)
                {
                    for (int i = 0; i < previewGraphics.Length; i++)
                        previewGraphics[i].canvasRenderer.cull = culled[i] ||
                            (pass >= 2 && BattleUiOpticsVisibility.IsToneSurface(previewGraphics[i]));
                    preview.camera.backgroundColor = pass % 2 == 0 ? Color.black : Color.white;
                    preview.BeginPreview(view, GUIStyle.none);
                    Texture texture;
                    try { preview.Render(true); }
                    finally { texture = preview.EndPreview(); }
                    Graphics.Blit(texture, toneTextures[pass]);
                }
            });
            toneMaterial.SetTexture("_WhiteTex", toneTextures[1]);
            toneMaterial.SetTexture("_ProtectedBlack", toneTextures[2]);
            toneMaterial.SetTexture("_ProtectedWhite", toneTextures[3]);
            toneMaterial.SetVector("_ScreenSize", new Vector4(width, height, 1f / width, 1f / height));
            manager.ScreenToneSettings.Apply(toneMaterial, width / 1920f);
            RenderTexture.active = toneTextures[4];
            GL.Clear(false, true, background);
            Graphics.Blit(toneTextures[0], toneTextures[4], toneMaterial);
            RenderTexture.active = previousActive;
            GUI.DrawTexture(view, toneTextures[4], ScaleMode.StretchToFill, false);
        }
        finally
        {
            RenderTexture.active = previousActive;
            preview.camera.backgroundColor = background;
            for (int i = 0; i < culled.Length; i++) previewGraphics[i].canvasRenderer.cull = culled[i];
        }
    }

    private void ReleaseToneTextures()
    {
        for (int i = 0; i < toneTextures.Length; i++)
        {
            if (toneTextures[i] == null) continue;
            toneTextures[i].Release();
            DestroyImmediate(toneTextures[i]);
            toneTextures[i] = null;
        }
    }
}

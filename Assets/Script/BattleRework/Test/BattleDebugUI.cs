using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 엔진 테스트용 IMGUI입니다.
/// Debug 버튼으로 통합 테스트 패널을 열고 닫으며,
/// 패널 안에서 KILL ALL ENEMY와 BATTLE RATING 테스트 선택기를 제공합니다.
/// 테스트 선택기는 Play Mode에서만 보이며 NodeGraphSO 값을 수정하지 않습니다.
/// 릴리스 빌드에서는 입력과 UI가 모두 비활성입니다.
/// </summary>
public class BattleDebugUI : MonoBehaviour
{
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleRoomManager roomManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleInputRouter inputRouter;
    [SerializeField] private bool visible = false;
    [SerializeField] private Vector2 debugButtonSize = new(80f, 32f);
    [SerializeField] private Vector2 debugMargin = new(12f, 12f);
    [SerializeField] private float debugPanelWidth = 420f;
    private RectTransform inputBoundary;

    private static bool sceneHookInstalled;

    private Vector2 scroll;
    private static readonly string[] uiStyleNames = { "pixelStyleUi", "comicStyleUi", "VectorStyleUi", "MahoShojo", "Stylish", "Birthday" };
    private Coroutine killAllRoutine;
#if UNITY_EDITOR
    [SerializeField] private EffectDefinitionSO[] elementalDefinitions;
    private ElementalTestSamples elementalSamples;
    private bool useElementalTestEmitters;
    [SerializeField] private float elementalTestDuration = 2f;
    private string elementalDurationText = "2";
    private EffectEmissionMode elementalMode;
    private EffectEmitter elementalEmitter;
    private Camera elementalCamera;
    private EffectBlobWorld elementalBlobWorld;
    private GUIStyle elementalSourceStyle;
    private static readonly string[] elementalTestTabs = { "이펙트", "성능 / FPS", "캐릭터 / 필드" };
    [SerializeField] private EffectDefinitionSO[] characterFieldDefinitions;
    private ElementalAdditionalSamples additionalSamples;
    private CharacterStatusEffectVisual characterEffect;
    private int activeCharacterEffect = -1;
    private int characterExecution;
    private bool testsInitialized;
    private readonly System.Collections.Generic.Dictionary<BattleDebrisBurst, int> debrisTests = new();
    private PlayerController elementalPlayer;
    private readonly EffectVisual[] fieldEffects = new EffectVisual[3];
    private readonly int[] fieldExecutions = new int[3];
    private static readonly string[] characterFieldLabels = { "독", "냉기", "화상", "낙뢰", "직선 전격", "레이저" };
    private int elementalTestTab;
    private Vector2 elementalPerformanceScroll;
    private EffectPerformanceOverlay elementalPerformance;
    private GUIStyle elementalPerformanceStyle, elementalPerformanceStatusStyle;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallSceneHook()
    {
        if (sceneHookInstalled)
            return;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        sceneHookInstalled = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureCurrentSceneHost()
    {
        EnsureEngineTestHost(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureEngineTestHost(scene);
    }

    private static void EnsureEngineTestHost(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;
        if (!Application.isEditor && !Debug.isDebugBuild)
            return;

        BattleDebugUI[] existing = FindObjectsByType<BattleDebugUI>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (existing.Length > 0)
        {
            foreach (BattleDebugUI existingUI in existing) existingUI.RequestClose();
            return;
        }

        GameObject host = new("BattleEngineTestDebugUI");
        BattleDebugUI ui = host.AddComponent<BattleDebugUI>();
        ui.visible = false;
    }

    private void Awake()
    {
        visible = false;
        if (!Application.isEditor && !Debug.isDebugBuild) return;
        ResolveReferences();
        GameObject boundaryHost = new("Debug Input Boundary", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        boundaryHost.transform.SetParent(transform, false);
        Canvas canvas = boundaryHost.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        GameObject blocker = new("Debug Pointer Blocker", typeof(RectTransform), typeof(Image));
        blocker.transform.SetParent(boundaryHost.transform, false);
        blocker.GetComponent<Image>().color = Color.clear;
        inputBoundary = (RectTransform)blocker.transform;
        RefreshInputBoundary();
    }

    private void OnEnable()
    {
        if (!Application.isEditor && !Debug.isDebugBuild) return;
        if (inputBoundary != null) inputBoundary.parent.gameObject.SetActive(true);
        if (inputRouter != null) inputRouter.RegisterDebugUI(this);
    }

#if UNITY_EDITOR
    private void InitializeTests()
    {
        if (testsInitialized) return;
        elementalPerformance = FindFirstObjectByType<EffectPerformanceOverlay>(FindObjectsInactive.Include);
        if (elementalPerformance == null) elementalPerformance = gameObject.AddComponent<EffectPerformanceOverlay>();
        elementalPerformance.drawStandalone = false;
        elementalPerformance.SetCollecting(false);
        elementalCamera = Camera.main;
        elementalBlobWorld = UnityEngine.Object.FindFirstObjectByType<EffectBlobWorld>();
        if (elementalBlobWorld == null)
        {
            GameObject worldHost = new("Elemental Blob World");
            elementalBlobWorld = worldHost.AddComponent<EffectBlobWorld>();
            elementalBlobWorld.pixelSizeMultiplier = EffectBlobWorld.DefaultPixelSize;
            elementalBlobWorld.refreshRate = EffectBlobWorld.DefaultQuality;
            elementalBlobWorld.surfaceRoughness = EffectBlobWorld.DefaultRoughness;
        }
        if (elementalDefinitions == null || elementalDefinitions.Length == 0)
        {
            useElementalTestEmitters = true;
            elementalDefinitions = new EffectDefinitionSO[ElementalTestSamples.Names.Length];
            for (int i = 0; i < elementalDefinitions.Length; i++)
            {
                EffectDefinitionSO saved = UnityEditor.AssetDatabase.LoadAssetAtPath<EffectDefinitionSO>(
                    ElementalTestSamples.SettingsRoot + "/" + ElementalTestSamples.Names[i] + "Visual.asset");
                if (saved != null && saved.sourceSheet != null) elementalDefinitions[i] = saved;
                else
                {
                    elementalSamples ??= new ElementalTestSamples();
                    elementalDefinitions[i] = elementalSamples.Definitions[i];
                }
            }
        }
        elementalDurationText = elementalTestDuration.ToString(System.Globalization.CultureInfo.InvariantCulture);
        GameObject emitterHost = new("Elemental Debug Emitter");
        emitterHost.transform.SetParent(transform, false);
        elementalEmitter = emitterHost.AddComponent<EffectEmitter>();
        if (characterFieldDefinitions == null || characterFieldDefinitions.Length != 6)
            characterFieldDefinitions = new EffectDefinitionSO[6];
        for (int i = 0; i < characterFieldDefinitions.Length; i++)
        {
            if (characterFieldDefinitions[i] != null) continue;
            characterFieldDefinitions[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<EffectDefinitionSO>(
                ElementalTestSamples.SettingsRoot + "/" + ElementalAdditionalSamples.Names[i] + "Visual.asset");
            if (characterFieldDefinitions[i] != null) continue;
            if (additionalSamples == null)
            {
                EffectDefinitionSO fire = UnityEditor.AssetDatabase.LoadAssetAtPath<EffectDefinitionSO>(
                    ElementalTestSamples.SettingsRoot + "/FireVisual.asset");
                if (fire == null)
                {
                    elementalSamples ??= new ElementalTestSamples();
                    fire = elementalSamples.Definitions[0];
                }
                additionalSamples = new ElementalAdditionalSamples(fire);
            }
            characterFieldDefinitions[i] = additionalSamples.Definitions[i];
        }
        testsInitialized = true;
    }
#endif

    private void Start()
    {
        if (!Application.isEditor && !Debug.isDebugBuild) return;
        ResolveReferences();
    }

    private Rect DebugButtonRect => new(Screen.width - debugMargin.x - debugButtonSize.x,
        debugMargin.y, debugButtonSize.x, debugButtonSize.y);

    public bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!isActiveAndEnabled) return false;
        return visible || DebugButtonRect.Contains(new Vector2(screenPoint.x, Screen.height - screenPoint.y));
    }

    private void RefreshInputBoundary()
    {
        if (inputBoundary == null) return;
        if (visible)
        {
            inputBoundary.anchorMin = Vector2.zero;
            inputBoundary.anchorMax = Vector2.one;
            inputBoundary.offsetMin = inputBoundary.offsetMax = Vector2.zero;
        }
        else
        {
            inputBoundary.anchorMin = inputBoundary.anchorMax = Vector2.one;
            inputBoundary.pivot = Vector2.one;
            inputBoundary.anchoredPosition = -debugMargin;
            inputBoundary.sizeDelta = debugButtonSize;
        }
    }

    private void SetVisible(bool value)
    {
        if (value && visible) return;
        if (value) ResolveReferences();
        visible = value;
        if (inputRouter != null) inputRouter.SetDebugActive(this, value);
        RefreshInputBoundary();
        if (value)
        {
#if UNITY_EDITOR
            InitializeTests();
            if (elementalCamera == null) elementalCamera = Camera.main;
            elementalEmitter.enabled = true;
#endif
            return;
        }

        StopAllCoroutines();
        killAllRoutine = null;
#if UNITY_EDITOR
        StopCharacterFieldTests();
        foreach (var test in debrisTests)
            if (test.Key != null && test.Key.ExecutionId == test.Value && test.Key.gameObject.activeInHierarchy)
                PoolService.Release(test.Key.gameObject);
        debrisTests.Clear();
        if (BattleFieldCinematicDirector.Instance != null)
            BattleFieldCinematicDirector.Instance.CloseDebugLightingControls();
        if (elementalEmitter != null)
        {
            elementalEmitter.ClearVisuals();
            elementalEmitter.enabled = false;
        }
        if (elementalPerformance != null) elementalPerformance.SetCollecting(false);
        elementalTestTab = 0;
#endif
    }

    public void RequestClose() => SetVisible(false);

    private void OnDisable()
    {
        RequestClose();
        if (inputRouter != null) inputRouter.UnregisterDebugUI(this);
        if (inputBoundary != null) inputBoundary.parent.gameObject.SetActive(false);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (roomManager == null)
            roomManager = FindFirstObjectByType<BattleRoomManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (inputRouter == null && Application.isPlaying)
            inputRouter = BattleInputRouter.ResolveOrCreate(this);
    }

    private void OnGUI()
    {
        if (!Application.isEditor && !Debug.isDebugBuild)
            return;

        GUI.depth = -1000;
        if (GUI.Button(DebugButtonRect, "Debug"))
        {
            SetVisible(!visible);
            return;
        }

        if (!visible)
            return;

        float top = debugMargin.y * 2f + debugButtonSize.y;
        float width = Mathf.Min(debugPanelWidth, Screen.width - debugMargin.x * 2f);
        GUILayout.BeginArea(new Rect(Screen.width - debugMargin.x - width, top, width,
            Mathf.Max(1f, Screen.height - top - debugMargin.y)), GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("BATTLE VERTICAL SLICE DEBUG");
        DrawBattleRatingTestSelector();
        DrawKillAllEnemyButton();
#if UNITY_EDITOR
        DrawElementalTestPanel();
        DrawDebrisArcTestButton();
        if (BattleFieldCinematicDirector.Instance != null)
            BattleFieldCinematicDirector.Instance.DrawDebugLightingControls();
#endif
        BattleShowPresentationManager presentation = BattleShowPresentationManager.Instance;
        if (presentation != null)
        {
            GUILayout.Label($"UI Style: {presentation.UiStyle}");
            int selectedStyle = GUILayout.SelectionGrid((int)presentation.UiStyle, uiStyleNames, 3);
            if (selectedStyle != (int)presentation.UiStyle)
                presentation.SetUiStyle((BattleUiStyle)selectedStyle);
            GUILayout.Label($"UI pixel scale: {presentation.UiPixelScale:0.00}x");
            float pixelScale = GUILayout.HorizontalSlider(presentation.UiPixelScale, 0.5f, 4f);
            if (Mathf.Abs(pixelScale - presentation.UiPixelScale) > 0.001f)
                presentation.SetUiPixelScale(pixelScale);
            bool gridEnabled = GUILayout.Toggle(presentation.ScreenGridEnabled, "Screen grid (2 x 2)");
            if (gridEnabled != presentation.ScreenGridEnabled)
                presentation.SetScreenGridEnabled(gridEnabled);
        }
        GUILayout.Space(6f);

        if (runManager == null)
        {
            GUILayout.Label("BattleRunManager: NULL");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            return;
        }

        GUILayout.Label($"Run Active: {runManager.RunActive}");
        GUILayout.Label($"State: {runManager.State}");
        GUILayout.Label($"Node: {(runManager.CurrentNode != null ? runManager.CurrentNode.id : "-")}");

        if (roomManager != null)
        {
            GUILayout.Label($"Room: {(roomManager.CurrentRoom != null ? roomManager.CurrentRoom.roomId : "-")}");
            GUILayout.Label($"Alive Monsters: {roomManager.AliveMonsterCount}");
            GUILayout.Label($"Combat Cleared: {roomManager.IsCombatCleared}");
            GUILayout.Label($"Exit Open: {roomManager.IsExitOpen}");
        }

        GUILayout.Space(8f);

        if (!runManager.RunActive || runManager.State == BattleRunState.Ended)
        {
            if (GUILayout.Button("START / RESTART RUN", GUILayout.Height(32f)))
                runManager.RestartRun();
        }
        else
        {
            if (GUILayout.Button("QUIT RUN"))
                runManager.QuitRun();
        }

        if (runManager.State == BattleRunState.Reward)
        {
            GUILayout.Space(10f);
            GUILayout.Label("REWARD - choose one");

            for (int i = 0; i < runManager.CurrentRewardChoices.Count; i++)
            {
                BattleEquipmentSO reward = runManager.CurrentRewardChoices[i];
                string label = reward != null ? reward.GetDisplayName() : "NULL REWARD";
                if (GUILayout.Button($"[{i}] {label}", GUILayout.Height(28f)))
                    runManager.SelectReward(i);
            }

            if (equipmentSystem != null && !equipmentSystem.HasFreeUnlockedSlot())
                GUILayout.Label("Inventory full: discard an equipment below or choose a duplicate that can merge.");

            if (GUILayout.Button("SKIP REWARD (DEBUG)"))
                runManager.SkipReward();
        }

        if (runManager.State == BattleRunState.SelectingNode)
        {
            GUILayout.Space(10f);
            GUILayout.Label("NEXT NODE");

            for (int i = 0; i < runManager.NextNodeChoices.Count; i++)
            {
                BattleNodeData node = runManager.NextNodeChoices[i];
                if (node == null)
                    continue;

                int stars = runManager.ResolveBattleRatingStars(node);
                if (GUILayout.Button(
                        $"{node.id} / {node.type} / Depth {node.depth} / {BuildDebugStars(stars)}",
                        GUILayout.Height(28f)))
                {
                    runManager.SelectNextNode(node.id);
                }
            }
        }

        if (runManager.State == BattleRunState.NonCombat)
        {
            GUILayout.Space(10f);
            GUILayout.Label("NON-COMBAT NODE");
            if (GUILayout.Button("RESOLVE NODE (DEBUG)", GUILayout.Height(28f)))
                runManager.ResolveNonCombatNode();
        }

        DrawEquipment();

        GUILayout.Space(10f);
        GUILayout.Label("Debug / F1 / Esc: Close");

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

#if UNITY_EDITOR
    private void OnDestroy()
    {
        RequestClose();
        additionalSamples?.Dispose();
        if (elementalEmitter != null) elementalEmitter.ClearVisuals();
        elementalSamples?.Dispose();
    }

    private void DrawElementalPerformanceTab()
    {
        if (elementalPerformance == null) return;
        elementalPerformanceStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        elementalPerformanceStatusStyle ??= new GUIStyle(elementalPerformanceStyle) { fontStyle = FontStyle.Bold };
        elementalPerformanceStatusStyle.normal.textColor = elementalPerformance.StatusColor;
        elementalPerformanceScroll = GUILayout.BeginScrollView(elementalPerformanceScroll, GUILayout.Height(270f));
        GUILayout.Label(elementalPerformance.StatusText, elementalPerformanceStatusStyle);
        GUILayout.Label(elementalPerformance.StatsText, elementalPerformanceStyle);
        GUILayout.EndScrollView();
    }

    private void DrawElementalTestPanel()
    {
        if (!Application.isPlaying || SceneManager.GetActiveScene().name != "BattleScene") return;
        elementalSourceStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.LowerRight };
        GUILayout.Label(
            "UI: BattleDebugUI.cs\nVFX: EffectBlobWorld.cs / EffectBlobRenderer.cs", elementalSourceStyle);
        GUILayout.Label("ELEMENTAL VISUAL TEST");
        elementalTestTab = GUILayout.Toolbar(elementalTestTab, elementalTestTabs);
        if (elementalPerformance != null) elementalPerformance.SetCollecting(elementalTestTab == 1);
        if (elementalTestTab == 1)
        {
            DrawElementalPerformanceTab();
            return;
        }
        if (elementalTestTab == 2)
        {
            DrawCharacterFieldTests();
            return;
        }
        if (elementalDefinitions == null || elementalDefinitions.Length == 0)
        {
            GUILayout.Label("No effect definitions assigned.");
            return;
        }
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(elementalMode == EffectEmissionMode.Explosion, "Explosion", GUI.skin.button)) elementalMode = EffectEmissionMode.Explosion;
        if (GUILayout.Toggle(elementalMode == EffectEmissionMode.Faucet, "Faucet", GUI.skin.button)) elementalMode = EffectEmissionMode.Faucet;
        if (GUILayout.Toggle(elementalMode == EffectEmissionMode.Sprinkler, "Sprinkler", GUI.skin.button)) elementalMode = EffectEmissionMode.Sprinkler;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Duration (-1 infinite)");
        elementalDurationText = GUILayout.TextField(elementalDurationText, GUILayout.Width(75f));
        if (float.TryParse(elementalDurationText, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float duration) &&
            !float.IsNaN(duration) && !float.IsInfinity(duration)) elementalTestDuration = duration;
        GUILayout.EndHorizontal();
        DrawElementalRenderControls();
        for (int row = 0; row < 2; row++)
        {
            GUILayout.BeginHorizontal();
            for (int column = 0; column < 3; column++)
            {
                int index = row * 3 + column;
                if (index >= elementalDefinitions.Length) continue;
                EffectDefinitionSO data = elementalDefinitions[index];
                if (data == null) continue;
                string label = data.element != null ? data.element.displayName : data.name;
                if (GUILayout.Button(label)) SpawnElementalTest(data);
            }
            GUILayout.EndHorizontal();
        }
        if (GUILayout.Button("FORCE END")) elementalEmitter.StopEmission();
        EffectVisual current = elementalEmitter.Latest;
        string phase = current == null ? "None" : current.IsAirborne ? "Airborne" : current.Phase.ToString();
        string remaining = current == null ? "0" : double.IsInfinity(current.Remaining) ? "Infinite" : current.Remaining.ToString("F2");
        GUILayout.Label($"Phase: {phase} / Remaining: {remaining}\nActive: {elementalEmitter.ActiveCount}");
    }

    private void ResolveElementalPlayer()
    {
        if (elementalPlayer == null) elementalPlayer = FindFirstObjectByType<PlayerController>();
        if (elementalPlayer == null || characterEffect != null) return;
        characterEffect = elementalPlayer.GetComponent<CharacterStatusEffectVisual>();
        if (characterEffect == null) characterEffect = elementalPlayer.gameObject.AddComponent<CharacterStatusEffectVisual>();
    }

    private void DrawCharacterFieldTests()
    {
        GUILayout.Label("캐릭터 착색: 마지막 선택 하나 / 필드: 개별 재생");
        for (int i = 0; i < 6; i++)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(characterFieldLabels[i] + " 재생"))
            {
                ResolveElementalPlayer();
                if (i < 3)
                {
                    if (characterEffect != null)
                    {
                        characterEffect.Play(characterFieldDefinitions[i]);
                        characterExecution = characterEffect.ExecutionId;
                        activeCharacterEffect = i;
                    }
                }
                else
                {
                    StopFieldTest(i - 3);
                    Vector3 position = elementalPlayer != null ? elementalPlayer.transform.position : elementalEmitter.transform.position;
                    EffectVisual effect = EffectVisual.Spawn(characterFieldDefinitions[i], position, Quaternion.identity, stationary: true);
                    fieldEffects[i - 3] = effect;
                    fieldExecutions[i - 3] = effect != null ? effect.ExecutionId : 0;
                }
            }
            if (GUILayout.Button("중지", GUILayout.Width(60f)))
            {
                if (i < 3)
                {
                    if (activeCharacterEffect == i)
                    {
                        StopCharacterTest();
                    }
                }
                else StopFieldTest(i - 3);
            }
            GUILayout.EndHorizontal();
        }
        if (GUILayout.Button("전체 중지")) StopCharacterFieldTests();
    }

    private void StopFieldTest(int index)
    {
        EffectVisual effect = fieldEffects[index];
        if (effect != null && effect.ExecutionId == fieldExecutions[index] && effect.IsPlaying)
        {
            effect.ResetForPool();
            PoolService.Release(effect.gameObject);
        }
        fieldEffects[index] = null;
    }

    private void StopCharacterFieldTests()
    {
        StopCharacterTest();
        for (int i = 0; i < fieldEffects.Length; i++) StopFieldTest(i);
    }

    private void StopCharacterTest()
    {
        if (activeCharacterEffect >= 0 && characterEffect != null && characterEffect.ExecutionId == characterExecution)
            characterEffect.Stop(true);
        activeCharacterEffect = -1;
    }

    private void DrawElementalRenderControls()
    {
        if (elementalBlobWorld == null) return;
        elementalBlobWorld.useSourcePixelsPerUnit = GUILayout.Toggle(
            elementalBlobWorld.useSourcePixelsPerUnit, "Use sprite PPU");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Manual PPU", GUILayout.Width(85f));
        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && !elementalBlobWorld.useSourcePixelsPerUnit;
        float ppu = Mathf.Round(1f / Mathf.Clamp(EffectNumbers.Positive(
            elementalBlobWorld.worldUnitsPerPixel, 0.03125f), 0.00390625f, 1f));
        float selectedPpu = Mathf.Round(GUILayout.HorizontalSlider(ppu, 1f, 256f));
        if (selectedPpu != ppu) elementalBlobWorld.worldUnitsPerPixel = 1f / selectedPpu;
        GUILayout.Label(selectedPpu.ToString("F0"), GUILayout.Width(34f));
        GUI.enabled = wasEnabled;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Pixel size", GUILayout.Width(85f));
        elementalBlobWorld.pixelSizeMultiplier = EffectBlobWorld.SnapPixelSize(GUILayout.HorizontalSlider(
            elementalBlobWorld.pixelSizeMultiplier, EffectBlobWorld.MinPixelSize, EffectBlobWorld.MaxPixelSize));
        GUILayout.Label($"x{elementalBlobWorld.pixelSizeMultiplier:F1}", GUILayout.Width(34f));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Quality (Hz)", GUILayout.Width(85f));
        elementalBlobWorld.refreshRate = Mathf.Round(GUILayout.HorizontalSlider(elementalBlobWorld.refreshRate,
            EffectBlobWorld.MinQuality, EffectBlobWorld.MaxQuality));
        GUILayout.Label(elementalBlobWorld.refreshRate.ToString("F0"), GUILayout.Width(34f));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Roughness", GUILayout.Width(85f));
        elementalBlobWorld.surfaceRoughness = Mathf.Round(GUILayout.HorizontalSlider(
            elementalBlobWorld.surfaceRoughness, 0f, 1f) * 20f) / 20f;
        GUILayout.Label(elementalBlobWorld.surfaceRoughness.ToString("F2"), GUILayout.Width(34f));
        GUILayout.EndHorizontal();
    }

    private void SpawnElementalTest(EffectDefinitionSO definition)
    {
        if (elementalCamera == null) return;
        Ray ray = elementalCamera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        Plane ground = new(Vector3.forward, Vector3.zero);
        if (!ground.Raycast(ray, out float distance)) return;
        elementalEmitter.transform.position = ray.GetPoint(distance);
        if (useElementalTestEmitters)
            ElementalTestSamples.ConfigureEmitter(elementalEmitter, System.Array.IndexOf(elementalDefinitions, definition));
        elementalEmitter.Trigger(definition, elementalMode, elementalTestDuration);
    }

    // This button exists only in Unity's Game view during Play Mode and is
    // not compiled into player builds (including development builds).
    private void DrawDebrisArcTestButton()
    {
        if (!Application.isPlaying ||
            SceneManager.GetActiveScene().name != "BattleScene")
            return;

        if (!GUILayout.Button("TEST DEBRIS ARC"))
            return;

        Camera camera = Camera.main;
        if (camera == null)
        {
            Debug.LogWarning("[BattleDebris] Arc test needs a Main Camera.");
            return;
        }

        // Raycast into the XY game field's Z=0 plane, rather than assuming
        // any specific side-view camera offset or perspective type.
        Ray ray = camera.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        Plane ground = new(Vector3.forward, Vector3.zero);
        if (!ground.Raycast(ray, out float hit))
        {
            Debug.LogWarning("[BattleDebris] Camera does not face the XY field plane.");
            return;
        }

        Vector3 point = ray.GetPoint(hit);
        BattleDebrisBurst burst = BattleDebrisBurst.SpawnEditorArcTest(
            new Vector3(point.x, point.y, 0f));
        if (burst != null) debrisTests[burst] = burst.ExecutionId;
    }
#endif

    private void DrawBattleRatingTestSelector()
    {
        if (!Application.isPlaying ||
            runManager == null ||
            runManager.State != BattleRunState.SelectingNode)
        {
            return;
        }

        GUILayout.Label(
            runManager.DebugBattleRatingOverride <= 0
                ? "BATTLE RATING TEST : AUTO"
                : $"BATTLE RATING TEST : {BuildDebugStars(runManager.DebugBattleRatingOverride)}");

        GUILayout.BeginHorizontal();

        DrawBattleRatingOverrideButton("AUTO", 0);
        for (int stars = 1; stars <= 5; stars++)
            DrawBattleRatingOverrideButton($"★{stars}", stars);

        GUILayout.EndHorizontal();
    }

    private void DrawBattleRatingOverrideButton(string label, int stars)
    {
        if (runManager == null)
            return;

        int current = runManager.DebugBattleRatingOverride;
        bool selected = current == stars;

        Color previous = GUI.backgroundColor;
        if (selected)
            GUI.backgroundColor = new Color(0.25f, 0.75f, 1f, 1f);

        if (GUILayout.Button(label, GUILayout.Height(30f)))
            runManager.SetDebugBattleRatingOverride(stars);

        GUI.backgroundColor = previous;
    }

    private static string BuildDebugStars(int stars)
    {
        stars = Mathf.Clamp(stars, 1, 5);
        return new string('★', stars) + new string('☆', 5 - stars);
    }

    private void DrawKillAllEnemyButton()
    {
        int aliveCount = CountAliveMonsters();
        if (aliveCount <= 0)
            return;

        const float height = 48f;

        Color previousBackground = GUI.backgroundColor;
        bool previousEnabled = GUI.enabled;
        GUI.backgroundColor = new Color(0.88f, 0.18f, 0.18f, 1f);

        string label = killAllRoutine != null
            ? $"KILLING ENEMIES... ({aliveCount})"
            : $"KILL ALL ENEMY ({aliveCount})";

        GUI.enabled = killAllRoutine == null;
        if (GUILayout.Button(label, GUILayout.Height(height)))
            killAllRoutine = StartCoroutine(KillAllEnemiesRoutine());

        GUI.enabled = previousEnabled;
        GUI.backgroundColor = previousBackground;
    }

    private static int CountAliveMonsters()
    {
        MonsterController[] monsters = FindObjectsByType<MonsterController>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        int count = 0;
        for (int i = 0; i < monsters.Length; i++)
        {
            if (monsters[i] != null && monsters[i].IsAlive)
                count++;
        }

        return count;
    }

    private IEnumerator KillAllEnemiesRoutine()
    {
        float timeoutAt = Time.realtimeSinceStartup + 3f;

        while (Time.realtimeSinceStartup < timeoutAt)
        {
            MonsterController[] monsters = FindObjectsByType<MonsterController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            bool foundAlive = false;
            for (int i = 0; i < monsters.Length; i++)
            {
                MonsterController monster = monsters[i];
                if (monster == null || !monster.IsAlive)
                    continue;

                foundAlive = true;
                ApplyEngineTestLethalDamage(monster);
            }

            if (!foundAlive)
                break;

            yield return null;
        }

        killAllRoutine = null;
    }

    private void ApplyEngineTestLethalDamage(MonsterController monster)
    {
        if (monster == null || !monster.IsAlive)
            return;

        Vector2 facing = monster.Facing.sqrMagnitude > 0.001f
            ? monster.Facing.normalized
            : Vector2.right;

        Vector2 sourcePosition = (Vector2)monster.transform.position - facing * 2f;
        float lethalDamage = Mathf.Max(
            1000000f,
            monster.CurrentHp + monster.ShieldDurability + 1000f);

        DamageContext context = new(
            gameObject,
            sourcePosition,
            lethalDamage,
            1f,
            0f,
            DamageKind.Melee);

        monster.ReceiveDamage(context, lethalDamage);
    }

    private void DrawEquipment()
    {
        if (equipmentSystem == null)
            return;

        GUILayout.Space(12f);
        GUILayout.Label($"EQUIPMENT ({equipmentSystem.UnlockedSlotCount}/{BattleEquipmentSystem.MaxSlotCount})");

        for (int i = 0; i < equipmentSystem.UnlockedSlotCount; i++)
        {
            BattleEquipmentSlot slot = equipmentSystem.Slots[i];
            if (slot == null || slot.equipment == null)
            {
                GUILayout.Label($"[{i + 1}] EMPTY");
                continue;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"[{i + 1}] {slot.equipment.GetDisplayName()} G{slot.grade} ({slot.copies}/3)");

            if (slot.equipment.shootingData != null && GUILayout.Button("Equip", GUILayout.Width(58f)))
                equipmentSystem.EquipSlot(i);

            if (GUILayout.Button("Discard", GUILayout.Width(68f)))
            {
                equipmentSystem.DiscardSlot(i);
                GUILayout.EndHorizontal();
                break;
            }

            GUILayout.EndHorizontal();
        }
    }
}

#if UNITY_EDITOR

public sealed class ElementalTestSamples : System.IDisposable
{
    public const string SettingsRoot = "Assets/Shaders/Elemental";
    public const int Cell = 32;
    public const float PixelsPerUnit = 32f;
    private const float FireOpacity = 0.9f;
    private const float SmokeOpacity = 0.45f;
    private const float WaterOpacity = 0.32f;
    private const float RoundedEdgeWidth = 0.14f;
    public static readonly string[] Names = { "Fire", "Smoke", "Water" };
    private static readonly Color[] Colors = {
        new(1f, 1f, 1f, FireOpacity), new(0.72f, 0.74f, 0.75f, SmokeOpacity), new(1f, 1f, 1f, WaterOpacity)
    };
    private readonly Texture2D[] textures = new Texture2D[3];
    private readonly Sprite[] sheets = new Sprite[3];
    private readonly ElementDefinitionSO[] elements = new ElementDefinitionSO[3];
    private readonly Material material;
    public EffectDefinitionSO[] Definitions { get; } = new EffectDefinitionSO[3];

    public ElementalTestSamples()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
        if (shader != null) material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        for (int i = 0; i < Definitions.Length; i++)
        {
            textures[i] = CreateTexture(i, Cell);
            textures[i].hideFlags = HideFlags.HideAndDontSave;
            sheets[i] = Sprite.Create(textures[i], new Rect(0, 0, textures[i].width, textures[i].height),
                new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sheets[i].hideFlags = HideFlags.HideAndDontSave;
            elements[i] = ScriptableObject.CreateInstance<ElementDefinitionSO>();
            elements[i].hideFlags = HideFlags.HideAndDontSave;
            elements[i].name = i == 1 ? "Fog" : Names[i];
            elements[i].displayName = Names[i].ToUpperInvariant();
            elements[i].previewColor = Colors[i];
            EffectDefinitionSO data = ScriptableObject.CreateInstance<EffectDefinitionSO>();
            data.hideFlags = HideFlags.HideAndDontSave;
            data.name = Names[i] + " Test Visual";
            data.element = elements[i];
            data.sourceSheet = sheets[i];
            data.cellSize = new Vector2Int(textures[i].width / 4, textures[i].height / 4);
            data.layout = new Vector2Int(4, 4);
            data.customMaterial = material;
            Configure(data, i);
            Definitions[i] = data;
        }
    }

    public static void Configure(EffectDefinitionSO data, int sample)
    {
        data.tint = data.emissionColor = data.lightColor = Colors[sample];
        if (sample == 0)
            data.emissionColor = data.lightColor = new Color(1f, 0.45f, 0.08f);
        data.family = sample == 2 ? EffectFamily.Liquid : EffectFamily.Gas;
        data.pieceCount = 1;
        data.tailTint = data.spentTint = Color.white;
        data.tailExpansion = 1f;
        data.visualScale = sample == 0 ? 0.5f : sample == 2 ? 1.35f : 1f;
        data.introBrightness = sample == 2 ? 1.15f : 1f;
        data.idleBrightness = 1f;
        data.endBrightness = sample == 2 ? 0.65f : 1f;
        data.fadeOnEnd = sample == 1;
        if (sample == 0 && data.sourceSheet != null)
            data.cellSize = new Vector2Int((int)data.sourceSheet.rect.width / 4, (int)data.sourceSheet.rect.height / 4);
        data.mergeNearby = sample == 1 || sample == 2;
        data.mergeSoftness = sample == 1 ? 0.3f : 0.12f;
        data.surfaceNoiseScale = sample == 1 ? 0.7f : sample == 2 ? 0.85f : 0.65f;
        data.surfaceNoiseStrength = sample == 1 ? 0.12f : sample == 2 ? 0.55f : 0.15f;
        data.surfaceNoiseSpeed = 0.08f;
        data.surfaceBodyColor = sample == 2 ? new Color(0.18f, 0.65f, 0.85f) : Color.white;
        data.surfaceHighlightColor = new Color(0.85f, 0.98f, 1f);
        data.surfaceHighlightStrength = sample == 2 ? 1f : 0f;
        data.surfaceRelief = sample == 2 ? 0.8f : 0f;
        data.surfaceShadowColor = new Color(0.18f, 0.21f, 0.27f);
        data.surfaceSpecular = sample == 2 ? 1.8f : 0f;
        data.surfaceShininess = 48f;
        data.impactFoamStrength = sample == 2 ? 0.9f : 0f;
        data.impactFoamDuration = 0.55f;
        data.foamOpacity = 0.9f;
        data.gasRise = sample == 1 ? 0.3f : 0.2f;
        data.gasExpansion = sample == 1 ? 0.2f : 0.05f;
        data.tailOffset = sample == 1 ? new Vector2(0.015f, 0.035f)
            : sample == 0 ? new Vector2(-0.035f, 0.22f) : Vector2.zero;
        data.edgeSpread = sample == 0 ? 0.1f : 0.025f;
        data.pieceDelay = sample == 1 ? 0.12f : 0.07f;
        data.pieceAttenuation = sample == 0 ? 0.88f : sample == 1 ? 0.72f : 0.75f;
        data.puddleScale = new Vector2(1.1f, 0.95f);
        data.motionWander = sample == 1 ? 0.18f : sample == 0 ? 0.2f : 0.25f;
        data.variationFrequency = sample == 0 ? 1.2f : sample == 1 ? 0.55f : 1.2f;
        data.sizeVariation = sample == 0 ? 0.08f : 0.18f;
        data.shapeVariation = sample == 0 ? 0.03f : 0.1f;
        data.emissionStrength = sample == 1 ? 0f : 1f;
        data.useLight = sample == 0;
        data.lightMode = EffectLightMode.Flicker;
        if (sample == 0)
        {
            data.smokeTail = null;
            data.fireParticles = true;
            data.pieceCount = 5;
            data.tint = Color.white;
            data.visualScale = 1f;
            data.gasRise = 1.6f;
            data.gasExpansion = 1.4f;
            data.mergeSoftness = 0.16f;
        }
        data.lightIntensity = 0.8f;
        data.lightRadius = 1.5f;
    }

    public static void ConfigureEmitter(EffectEmitter emitter, int sample)
    {
        emitter.burstCount = sample == 0 ? 1 : sample == 2 ? 8 : 32;
        emitter.streamCount = sample == 0 ? 1 : sample == 2 ? 2 : 4;
        emitter.interval = sample == 0 ? 0.22f : sample == 2 ? 0.12f : 0.08f;
        emitter.maxActive = sample == 0 ? 64 : 2048;
        emitter.direction = sample == 2 ? Vector2.down : Vector2.up;
        emitter.speed = sample == 2 ? 1.5f : 0.35f;
        emitter.spreadAngle = sample == 2 ? 25f : 35f;
    }

    public static Texture2D CreateTexture(int sample, int cell)
    {
        if (sample == 0) return ElementalFireFrames.CreateTexture(cell);
        Texture2D texture = new(cell * 4, cell * 4, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        Color32[] pixels = new Color32[texture.width * texture.height];
        for (int frame = 0; frame < 16; frame++)
        {
            int phase = frame < 4 ? 0 : frame < 12 ? 1 : 2;
            float progress = phase == 0 ? (frame + 1f) / 4f : phase == 1 ? (frame - 4f) / 8f : (frame - 11f) / 4f;
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    float dx = (x + 0.5f - cell * 0.5f) / (cell * 0.5f);
                    float dy = (y + 0.5f - cell * 0.5f) / (cell * 0.5f);
                    int px = frame % 4 * cell + x;
                    int py = (3 - frame / 4) * cell + y;
                    if (sample == 1 || sample == 2)
                    {
                        pixels[py * texture.width + px] = RoundedPixel(dx, dy, sample, phase, progress);
                        continue;
                    }
                    float angle = Mathf.Atan2(dy, dx);
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = phase == 0 ? Mathf.Lerp(0.15f, 0.62f, progress) : phase == 1 ? 0.64f : 0.68f + progress * 0.15f;
                    edge += Mathf.Sin(angle * 5f + frame * 1.1f) * 0.07f + Mathf.Cos(angle * 3f - frame) * 0.04f;
                    bool filled = radius <= edge;
                    if (phase == 2)
                        filled &= radius >= progress * 0.83f && Mathf.Sin(angle * 7f + frame) > -1f + progress * 1.4f;
                    byte shade = (byte)(radius < edge * 0.55f ? 255 : radius < edge * 0.85f ? 220 : 170);
                    pixels[py * texture.width + px] = filled ? new Color32(shade, shade, shade, 255) : new Color32(0, 0, 0, 0);
                }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    private static Color32 RoundedPixel(float x, float y, int sample, int phase, float progress)
    {
        float cycle = phase == 1 ? progress * Mathf.PI * 2f : 0f;
        float radius = phase == 0 ? Mathf.SmoothStep(0.24f, 0.68f, progress) : 0.68f;
        radius *= 1f + Mathf.Sin(cycle) * 0.025f;
        float distance = CircleDistance(x, y, 0f, 0f, radius * 0.85f);
        if (sample == 1)
        {
            float sway = Mathf.Sin(cycle) * 0.025f;
            distance = SmoothUnion(distance, CircleDistance(x, y, -radius * 0.34f, radius * 0.12f + sway, radius * 0.64f));
            distance = SmoothUnion(distance, CircleDistance(x, y, radius * 0.32f, radius * 0.08f - sway, radius * 0.65f));
            distance = SmoothUnion(distance, CircleDistance(x, y, radius * 0.04f, radius * 0.38f, radius * 0.6f));
        }
        else
        {
            distance = SmoothUnion(distance, CircleDistance(x, y, -radius * 0.25f, radius * 0.08f, radius * 0.68f));
            distance = SmoothUnion(distance, CircleDistance(x, y, radius * 0.27f, -radius * 0.06f, radius * 0.67f));
        }
        float coverage = CircleCoverage(distance);
        float radial = Mathf.Sqrt(x * x + y * y);
        if (phase == 2)
        {
            float hole = radius * progress * 0.85f;
            coverage *= Mathf.SmoothStep(0f, 1f, (radial - hole + RoundedEdgeWidth * 0.5f) / RoundedEdgeWidth);
            float fragments = 0f;
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * 0.5f + 0.4f;
                float offset = radius * (0.65f + progress * 0.35f);
                float fragmentRadius = radius * 0.3f * (1f - progress);
                fragments = Mathf.Max(fragments, CircleCoverage(CircleDistance(x, y,
                    Mathf.Cos(angle) * offset, Mathf.Sin(angle) * offset, fragmentRadius)));
            }
            coverage = Mathf.Lerp(coverage, fragments, Mathf.SmoothStep(0f, 1f, progress)) * (1f - progress);
        }
        byte shade = (byte)Mathf.RoundToInt(Mathf.Lerp(250f, 215f, Mathf.SmoothStep(0f, 1f, radial / radius)));
        byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(coverage) * 255f);
        return alpha > 0 ? new Color32(shade, shade, shade, alpha) : new Color32(0, 0, 0, 0);
    }

    private static float CircleDistance(float x, float y, float centerX, float centerY, float radius)
    {
        float dx = x - centerX;
        float dy = y - centerY;
        return Mathf.Sqrt(dx * dx + dy * dy) - radius;
    }

    private static float CircleCoverage(float distance) =>
        1f - Mathf.SmoothStep(0f, 1f, (distance + RoundedEdgeWidth * 0.5f) / RoundedEdgeWidth);

    private static float SmoothUnion(float a, float b)
    {
        float blend = Mathf.Clamp01(0.5f + 0.5f * (b - a) / RoundedEdgeWidth);
        return Mathf.Lerp(b, a, blend) - RoundedEdgeWidth * blend * (1f - blend);
    }

    public void Dispose()
    {
        for (int i = 0; i < Definitions.Length; i++)
        {
            UnityEngine.Object.Destroy(Definitions[i]);
            UnityEngine.Object.Destroy(elements[i]);
            UnityEngine.Object.Destroy(sheets[i]);
            UnityEngine.Object.Destroy(textures[i]);
            Definitions[i] = null;
            elements[i] = null;
            sheets[i] = null;
            textures[i] = null;
        }
        if (material != null) UnityEngine.Object.Destroy(material);
    }
}
#endif

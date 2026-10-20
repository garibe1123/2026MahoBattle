using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 엔진 테스트용 IMGUI입니다.
/// 좌측 레거시 디버그 패널은 Debug Action Map의 F1로 켜고 끌 수 있으며,
/// 우측 상단 KILL ALL ENEMY 퀵 액션과 맵 선택용 BATTLE RATING 테스트 선택기를 제공합니다.
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

    private static bool sceneHookInstalled;

    private Vector2 scroll;
    private Coroutine killAllRoutine;
#if UNITY_EDITOR
    [SerializeField] private EffectDefinitionSO[] elementalDefinitions;
    private ElementalTestSamples elementalSamples;
    [SerializeField] private float elementalTestDuration = 2f;
    private string elementalDurationText = "2";
    private EffectEmissionMode elementalMode;
    private EffectEmitter elementalEmitter;
    private Camera elementalCamera;
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
            return;

        GameObject host = new("BattleEngineTestDebugUI");
        BattleDebugUI ui = host.AddComponent<BattleDebugUI>();
        ui.visible = false;
    }

    private void Awake()
    {
        ResolveReferences();
#if UNITY_EDITOR
        elementalCamera = Camera.main;
        if (elementalDefinitions == null || elementalDefinitions.Length == 0)
        {
            elementalSamples = new ElementalTestSamples();
            elementalDefinitions = elementalSamples.Definitions;
        }
        elementalDurationText = elementalTestDuration.ToString(System.Globalization.CultureInfo.InvariantCulture);
        GameObject emitterHost = new("Elemental Debug Emitter");
        emitterHost.transform.SetParent(transform, false);
        elementalEmitter = emitterHost.AddComponent<EffectEmitter>();
#endif
    }

    private void Start()
    {
        ResolveReferences();
#if UNITY_EDITOR
        if (elementalCamera == null) elementalCamera = Camera.main;
#endif
    }

    private void Update()
    {
        if (!Application.isEditor && !Debug.isDebugBuild)
            return;

        if (inputRouter != null && inputRouter.DebugTogglePressedThisFrame)
            visible = !visible;
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

        DrawBattleRatingTestSelector();
        DrawKillAllEnemyButton();
#if UNITY_EDITOR
        DrawElementalTestPanel();
        DrawDebrisArcTestButton();
#endif

        if (!visible)
            return;

        GUILayout.BeginArea(new Rect(12f, 12f, 420f, Screen.height - 24f), GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("BATTLE VERTICAL SLICE DEBUG");
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
        GUILayout.Label("F1: Toggle Debug UI");

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

#if UNITY_EDITOR
    private void OnDestroy()
    {
        if (elementalSamples == null) return;
        if (elementalEmitter != null) elementalEmitter.ClearVisuals();
        elementalSamples.Dispose();
    }

    private void DrawElementalTestPanel()
    {
        if (!Application.isPlaying || SceneManager.GetActiveScene().name != "BattleScene") return;
        const float width = 300f;
        const float height = 224f;
        GUILayout.BeginArea(new Rect(Screen.width - width - 12f, Screen.height - height - 60f, width, height), GUI.skin.box);
        GUILayout.Label("ELEMENTAL VISUAL TEST");
        if (elementalDefinitions == null || elementalDefinitions.Length == 0)
        {
            GUILayout.Label("No effect definitions assigned.");
            GUILayout.EndArea();
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
        GUILayout.EndArea();
    }

    private void SpawnElementalTest(EffectDefinitionSO definition)
    {
        if (elementalCamera == null) return;
        Ray ray = elementalCamera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        Plane ground = new(Vector3.forward, Vector3.zero);
        if (!ground.Raycast(ray, out float distance)) return;
        elementalEmitter.transform.position = ray.GetPoint(distance);
        if (elementalSamples != null)
            ElementalTestSamples.ConfigureEmitter(elementalEmitter, System.Array.IndexOf(elementalDefinitions, definition));
        elementalEmitter.ClearVisuals();
        elementalEmitter.Trigger(definition, elementalMode, elementalTestDuration);
    }

    // This button exists only in Unity's Game view during Play Mode and is
    // not compiled into player builds (including development builds).
    private void DrawDebrisArcTestButton()
    {
        if (!Application.isPlaying ||
            SceneManager.GetActiveScene().name != "BattleScene")
            return;

        const float width = 220f;
        const float height = 40f;
        Rect rect = new(Screen.width - width - 12f,
            Screen.height - height - 12f, width, height);

        if (!GUI.Button(rect, "TEST DEBRIS ARC"))
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
        BattleDebrisBurst.SpawnEditorArcTest(
            new Vector3(point.x, point.y, 0f));
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

        const float width = 350f;
        const float height = 86f;
        const float rightMargin = 12f;
        const float top = 72f;

        Rect area = new(
            Screen.width - width - rightMargin,
            top,
            width,
            height);

        GUILayout.BeginArea(area, GUI.skin.box);
        GUILayout.Label(
            runManager.DebugBattleRatingOverride <= 0
                ? "BATTLE RATING TEST : AUTO"
                : $"BATTLE RATING TEST : {BuildDebugStars(runManager.DebugBattleRatingOverride)}");

        GUILayout.BeginHorizontal();

        DrawBattleRatingOverrideButton("AUTO", 0);
        for (int stars = 1; stars <= 5; stars++)
            DrawBattleRatingOverrideButton($"★{stars}", stars);

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
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

        const float width = 220f;
        const float height = 48f;
        float top = runManager != null && runManager.State == BattleRunState.SelectingNode
            ? 166f
            : 12f;
        Rect area = new(Screen.width - width - 12f, top, width, height);

        Color previousBackground = GUI.backgroundColor;
        bool previousEnabled = GUI.enabled;
        GUI.backgroundColor = new Color(0.88f, 0.18f, 0.18f, 1f);

        GUILayout.BeginArea(area);
        string label = killAllRoutine != null
            ? $"KILLING ENEMIES... ({aliveCount})"
            : $"KILL ALL ENEMY ({aliveCount})";

        GUI.enabled = killAllRoutine == null;
        if (GUILayout.Button(label, GUILayout.Width(width), GUILayout.Height(height)))
            killAllRoutine = StartCoroutine(KillAllEnemiesRoutine());

        GUILayout.EndArea();
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
    public const int Cell = 32;
    public const float PixelsPerUnit = 32f;
    private const float SampleOpacity = 0.6f;
    private const float RoundedEdgeWidth = 0.14f;
    public static readonly string[] Names = { "Fire", "Smoke", "Water" };
    private static readonly Color[] Colors = {
        new(1f, 0.4f, 0.08f, SampleOpacity), new(0.55f, 0.55f, 0.6f, SampleOpacity), new(0.15f, 0.55f, 1f, SampleOpacity)
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
            data.cellSize = new Vector2Int(Cell, Cell);
            data.layout = new Vector2Int(4, 4);
            data.customMaterial = material;
            Configure(data, i);
            Definitions[i] = data;
        }
    }

    public static void Configure(EffectDefinitionSO data, int sample)
    {
        data.tint = data.emissionColor = data.lightColor = Colors[sample];
        data.family = sample == 2 ? EffectFamily.Liquid : EffectFamily.Gas;
        data.pieceCount = sample == 0 ? 5 : 1;
        data.gasRise = sample == 1 ? 0.3f : 0.2f;
        data.gasExpansion = sample == 1 ? 0.2f : 0.15f;
        data.tailOffset = sample == 1 ? new Vector2(0.015f, 0.035f)
            : sample == 0 ? new Vector2(-0.13f, 0.1f) : Vector2.zero;
        data.edgeSpread = sample == 0 ? 0.1f : 0.025f;
        data.pieceDelay = sample == 1 ? 0.12f : 0.07f;
        data.pieceAttenuation = sample == 1 ? 0.72f : 0.75f;
        data.puddleScale = new Vector2(1.1f, 0.95f);
        data.motionWander = sample == 0 ? 0.55f : sample == 1 ? 0.4f : 0.25f;
        data.variationFrequency = sample == 0 ? 2.2f : sample == 1 ? 0.8f : 1.2f;
        data.sizeVariation = 0.18f;
        data.shapeVariation = sample == 0 ? 0.16f : 0.1f;
        data.emissionStrength = sample == 1 ? 0f : 1f;
        data.useLight = sample == 0;
        data.lightMode = EffectLightMode.Flicker;
        data.lightIntensity = 0.8f;
        data.lightRadius = 1.5f;
    }

    public static void ConfigureEmitter(EffectEmitter emitter, int sample)
    {
        bool singleBlob = sample == 1 || sample == 2;
        emitter.burstCount = singleBlob ? 1 : 8;
        emitter.streamCount = 1;
        emitter.maxActive = singleBlob ? 1 : 64;
        emitter.direction = sample == 2 ? Vector2.down : Vector2.up;
        emitter.speed = sample == 2 ? 1.5f : 0.35f;
        emitter.spreadAngle = sample == 2 ? 25f : 35f;
    }

    public static Texture2D CreateTexture(int sample, int cell)
    {
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
                    if (sample == 0)
                    {
                        dx /= Mathf.Lerp(1f, 0.3f, (dy + 1f) * 0.5f);
                        dy = (dy + 0.15f) / 1.2f;
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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 전투 중 Field 바깥 BattleDecor를 한 톤 눌러 실제 플레이 영역과 구분합니다.
/// Decor 생성/Pooling 구조는 건드리지 않고, Decor hierarchy가 바뀌거나 전투 상태가 바뀔 때만
/// SpriteRenderer / Light2D 캐시를 갱신합니다.
/// </summary>
[DefaultExecutionOrder(32650)]
[DisallowMultipleComponent]
public sealed class BattleDecorReadabilityController : MonoBehaviour
{
    private const string ClusterPrefix = "BattleDecorRuntime_";
    private const string CarrierTilePrefix = "DecorCarrierTile_";
    private const string FrameRootName = "DecorMechanicalFrame";
    private const string PartsRootName = "DecorParts";

    private enum VisualKind
    {
        Floor,
        Frame,
        Part
    }

    private struct SpriteTarget
    {
        public SpriteRenderer renderer;
        public Color baseColor;
        public VisualKind kind;
    }

    private struct LightTarget
    {
        public Light2D light;
        public float baseIntensity;
    }

    private static bool sceneHookInstalled;

    [Header("COMBAT READABILITY")]
    [SerializeField] private bool dimDecorDuringCombat = true;
    [Tooltip("Decor Carrier의 바닥 밝기. 실제 전투 Floor보다 한 단계만 어둡게 유지합니다.")]
    [SerializeField, Range(0.20f, 1f)] private float combatFloorBrightness = 0.84f;
    [Tooltip("기계 프레임/손잡이 밝기.")]
    [SerializeField, Range(0.20f, 1f)] private float combatFrameBrightness = 0.70f;
    [Tooltip("카메라/소품 등 authored Decor Part 밝기.")]
    [SerializeField, Range(0.20f, 1f)] private float combatPartBrightness = 0.64f;
    [Tooltip("Decor 자체 Light2D 강도 배율. 전투 주체보다 주변 기계 장식이 밝아지는 것을 막습니다.")]
    [SerializeField, Range(0f, 1f)] private float combatDecorLightIntensity = 0.50f;

    [Header("PRESENTATION")]
    [Tooltip("대기실/Reward/Map Show에서는 기존 Spotlight 연출을 살리기 위해 원래 밝기를 유지합니다.")]
    [SerializeField, Range(0.50f, 1f)] private float presentationBrightness = 1f;
    [SerializeField, Range(0f, 1f)] private float presentationDecorLightIntensity = 1f;

    [Header("BINDING")]
    [SerializeField] private BattleUniversalStageDecorCarrierSkinController decorOwner;
    [SerializeField] private BattleRunManager runManager;
    [SerializeField, Min(0.25f)] private float missingRunManagerRetryInterval = 1f;

    private readonly List<SpriteTarget> spriteTargets = new();
    private readonly List<LightTarget> lightTargets = new();

    private float nextRunManagerResolveAt;
    private bool hierarchyDirty = true;
    private bool modeInitialized;
    private bool lastCombatMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallSceneHook()
    {
        if (sceneHookInstalled)
            return;

        sceneHookInstalled = true;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForCurrentScene()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.IsValid())
            InstallForScene(active);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InstallForScene(scene);
    }

    private static void InstallForScene(Scene scene)
    {
        BattleUniversalStageDecorCarrierSkinController[] owners = FindObjectsByType<BattleUniversalStageDecorCarrierSkinController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < owners.Length; i++)
        {
            BattleUniversalStageDecorCarrierSkinController owner = owners[i];
            if (owner == null || owner.gameObject.scene != scene)
                continue;
            if (owner.GetComponent<BattleDecorReadabilityController>() == null)
                owner.gameObject.AddComponent<BattleDecorReadabilityController>();
        }
    }

    private void Awake()
    {
        if (decorOwner == null)
            decorOwner = GetComponent<BattleUniversalStageDecorCarrierSkinController>();
        ResolveRunManager();
    }

    private void OnEnable()
    {
        hierarchyDirty = true;
        modeInitialized = false;
        nextRunManagerResolveAt = 0f;
    }

    private void OnDisable()
    {
        RestoreCachedVisuals();
        spriteTargets.Clear();
        lightTargets.Clear();
    }

    private void OnTransformChildrenChanged()
    {
        hierarchyDirty = true;
    }

    private void OnValidate()
    {
        combatFloorBrightness = Mathf.Clamp(combatFloorBrightness, 0.20f, 1f);
        combatFrameBrightness = Mathf.Clamp(combatFrameBrightness, 0.20f, 1f);
        combatPartBrightness = Mathf.Clamp(combatPartBrightness, 0.20f, 1f);
        combatDecorLightIntensity = Mathf.Clamp01(combatDecorLightIntensity);
        presentationBrightness = Mathf.Clamp(presentationBrightness, 0.50f, 1f);
        presentationDecorLightIntensity = Mathf.Clamp01(presentationDecorLightIntensity);
        missingRunManagerRetryInterval = Mathf.Max(0.25f, missingRunManagerRetryInterval);

        if (Application.isPlaying)
        {
            hierarchyDirty = true;
            modeInitialized = false;
        }
    }

    private void LateUpdate()
    {
        if (decorOwner == null)
            decorOwner = GetComponent<BattleUniversalStageDecorCarrierSkinController>();
        if (decorOwner == null)
            return;

        if (runManager == null && Time.unscaledTime >= nextRunManagerResolveAt)
        {
            ResolveRunManager();
            nextRunManagerResolveAt = Time.unscaledTime + missingRunManagerRetryInterval;
        }

        bool combatMode = IsCombatMode();
        bool modeChanged = !modeInitialized || combatMode != lastCombatMode;

        if (hierarchyDirty)
        {
            RebuildVisualCache();
            hierarchyDirty = false;
            modeChanged = true;
        }

        if (!modeChanged)
            return;

        modeInitialized = true;
        lastCombatMode = combatMode;
        ApplyReadability(combatMode);
    }

    private void ResolveRunManager()
    {
        runManager = null;
        BattleRunManager[] managers = FindObjectsByType<BattleRunManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleRunManager candidate = managers[i];
            if (candidate == null || candidate.gameObject.scene != gameObject.scene)
                continue;
            runManager = candidate;
            break;
        }
    }

    private bool IsCombatMode()
    {
        return dimDecorDuringCombat &&
               runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Combat;
    }

    private void RebuildVisualCache()
    {
        // 기존 캐시가 이미 어두워진 상태라면 먼저 원본 색/강도로 복구한 뒤 다시 샘플링합니다.
        // 그래야 새 Cluster가 생길 때 밝기 배율이 누적되지 않습니다.
        RestoreCachedVisuals();
        spriteTargets.Clear();
        lightTargets.Clear();

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !TryResolveVisualKind(renderer.transform, out VisualKind kind))
                continue;

            spriteTargets.Add(new SpriteTarget
            {
                renderer = renderer,
                baseColor = renderer.color,
                kind = kind
            });
        }

        Light2D[] lights = GetComponentsInChildren<Light2D>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            Light2D light = lights[i];
            if (light == null || !IsInsideDecorCluster(light.transform))
                continue;

            lightTargets.Add(new LightTarget
            {
                light = light,
                baseIntensity = light.intensity
            });
        }
    }

    private bool TryResolveVisualKind(Transform source, out VisualKind kind)
    {
        kind = VisualKind.Part;
        if (source == null || !IsInsideDecorCluster(source))
            return false;

        if (source.name.StartsWith(CarrierTilePrefix, System.StringComparison.Ordinal))
        {
            kind = VisualKind.Floor;
            return true;
        }

        Transform cursor = source;
        while (cursor != null && cursor != transform)
        {
            if (cursor.name == FrameRootName)
            {
                kind = VisualKind.Frame;
                return true;
            }

            if (cursor.name == PartsRootName)
            {
                kind = VisualKind.Part;
                return true;
            }

            cursor = cursor.parent;
        }

        return false;
    }

    private bool IsInsideDecorCluster(Transform source)
    {
        Transform cursor = source;
        while (cursor != null && cursor != transform)
        {
            if (cursor.name.StartsWith(ClusterPrefix, System.StringComparison.Ordinal))
                return true;
            cursor = cursor.parent;
        }
        return false;
    }

    private void ApplyReadability(bool combatMode)
    {
        for (int i = 0; i < spriteTargets.Count; i++)
        {
            SpriteTarget target = spriteTargets[i];
            if (target.renderer == null)
                continue;

            float multiplier = combatMode
                ? ResolveCombatBrightness(target.kind)
                : presentationBrightness;

            target.renderer.color = MultiplyRgb(target.baseColor, multiplier);
        }

        float lightMultiplier = combatMode
            ? combatDecorLightIntensity
            : presentationDecorLightIntensity;

        for (int i = 0; i < lightTargets.Count; i++)
        {
            LightTarget target = lightTargets[i];
            if (target.light == null)
                continue;
            target.light.intensity = Mathf.Max(0f, target.baseIntensity * lightMultiplier);
        }
    }

    private float ResolveCombatBrightness(VisualKind kind)
    {
        switch (kind)
        {
            case VisualKind.Floor:
                return combatFloorBrightness;
            case VisualKind.Frame:
                return combatFrameBrightness;
            default:
                return combatPartBrightness;
        }
    }

    private void RestoreCachedVisuals()
    {
        for (int i = 0; i < spriteTargets.Count; i++)
        {
            SpriteTarget target = spriteTargets[i];
            if (target.renderer != null)
                target.renderer.color = target.baseColor;
        }

        for (int i = 0; i < lightTargets.Count; i++)
        {
            LightTarget target = lightTargets[i];
            if (target.light != null)
                target.light.intensity = target.baseIntensity;
        }
    }

    private static Color MultiplyRgb(Color source, float multiplier)
    {
        float m = Mathf.Max(0f, multiplier);
        return new Color(
            source.r * m,
            source.g * m,
            source.b * m,
            source.a);
    }
}

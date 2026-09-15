using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 전투 중 Field 바깥 BattleDecor를 강하게 어둡게 눌러 실제 플레이 영역과 분리합니다.
/// Light2D가 연결된 Part와 Decor Light는 상대적으로 밝게 유지해 무대 기계의 실루엣과 광원만 읽히게 합니다.
/// Decor 생성/Pooling 구조는 건드리지 않고, hierarchy 또는 전투 상태가 바뀔 때만 캐시를 갱신합니다.
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
        public bool hasConnectedLight;
    }

    private struct LightTarget
    {
        public Light2D light;
        public float baseIntensity;
        public bool standalone;
    }

    private static bool sceneHookInstalled;

    [Header("COMBAT READABILITY")]
    [SerializeField] private bool dimDecorDuringCombat = true;
    [Tooltip("Decor Carrier 바닥 밝기. 전투 Floor와 명확히 분리되도록 강하게 어둡게 합니다.")]
    [SerializeField, Range(0.05f, 1f)] private float combatFloorBrightness = 0.34f;
    [Tooltip("기계 프레임/손잡이 밝기.")]
    [SerializeField, Range(0.05f, 1f)] private float combatFrameBrightness = 0.16f;
    [Tooltip("일반 authored Decor Part 밝기.")]
    [SerializeField, Range(0.05f, 1f)] private float combatPartBrightness = 0.12f;
    [Tooltip("자식 Light2D가 연결된 Part는 광원 하우징이 읽히도록 일반 Part보다 밝게 유지합니다.")]
    [SerializeField, Range(0.05f, 1f)] private float combatLitPartBrightness = 0.30f;
    [Tooltip("Part에 연결된 Decor Light2D 강도 배율입니다.")]
    [SerializeField, Range(0f, 3f)] private float combatDecorLightIntensity = 1.35f;
    [Tooltip("독립적으로 배치된 Decor Light2D 강도 배율입니다.")]
    [SerializeField, Range(0f, 3f)] private float combatStandaloneLightIntensity = 1.55f;

    [Header("PRESENTATION")]
    [Tooltip("대기실/Reward/Map Show에서는 기존 Spotlight 연출을 살리기 위해 원래 밝기를 유지합니다.")]
    [SerializeField, Range(0.50f, 1f)] private float presentationBrightness = 1f;
    [SerializeField, Range(0f, 2f)] private float presentationDecorLightIntensity = 1f;

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
        combatFloorBrightness = Mathf.Clamp(combatFloorBrightness, 0.05f, 1f);
        combatFrameBrightness = Mathf.Clamp(combatFrameBrightness, 0.05f, 1f);
        combatPartBrightness = Mathf.Clamp(combatPartBrightness, 0.05f, 1f);
        combatLitPartBrightness = Mathf.Clamp(combatLitPartBrightness, 0.05f, 1f);
        combatDecorLightIntensity = Mathf.Clamp(combatDecorLightIntensity, 0f, 3f);
        combatStandaloneLightIntensity = Mathf.Clamp(combatStandaloneLightIntensity, 0f, 3f);
        presentationBrightness = Mathf.Clamp(presentationBrightness, 0.50f, 1f);
        presentationDecorLightIntensity = Mathf.Clamp(presentationDecorLightIntensity, 0f, 2f);
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
        // 이전 배율이 누적되지 않게 원본값으로 되돌린 뒤 새 hierarchy를 다시 샘플링합니다.
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
                kind = kind,
                hasConnectedLight = kind == VisualKind.Part && HasConnectedLight(renderer.transform)
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
                baseIntensity = light.intensity,
                standalone = IsStandaloneDecorLight(light.transform)
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

    private static bool HasConnectedLight(Transform source)
    {
        if (source == null)
            return false;

        Light2D connected = source.GetComponentInChildren<Light2D>(true);
        return connected != null;
    }

    private static bool IsStandaloneDecorLight(Transform lightTransform)
    {
        if (lightTransform == null || lightTransform.parent == null)
            return false;
        return lightTransform.parent.name == PartsRootName;
    }

    private void ApplyReadability(bool combatMode)
    {
        for (int i = 0; i < spriteTargets.Count; i++)
        {
            SpriteTarget target = spriteTargets[i];
            if (target.renderer == null)
                continue;

            float multiplier;
            if (!combatMode)
            {
                multiplier = presentationBrightness;
            }
            else if (target.kind == VisualKind.Part && target.hasConnectedLight)
            {
                multiplier = combatLitPartBrightness;
            }
            else
            {
                multiplier = ResolveCombatBrightness(target.kind);
            }

            target.renderer.color = MultiplyRgb(target.baseColor, multiplier);
        }

        for (int i = 0; i < lightTargets.Count; i++)
        {
            LightTarget target = lightTargets[i];
            if (target.light == null)
                continue;

            float multiplier = combatMode
                ? (target.standalone ? combatStandaloneLightIntensity : combatDecorLightIntensity)
                : presentationDecorLightIntensity;

            target.light.intensity = Mathf.Max(0f, target.baseIntensity * multiplier);
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

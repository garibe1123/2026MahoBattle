using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Combat lighting policy override.
///
/// Combat에서도 Player의 Stage Spotlight(Beam/Top Glow/Pool)를 유지합니다.
/// 원형 Point Light는 선택적 보조광일 뿐이며 기본값은 OFF입니다.
/// Reward/Map/대기실의 Spotlight 연출은 기존 Presentation Controller가 계속 소유합니다.
/// </summary>
[DefaultExecutionOrder(32700)]
[DisallowMultipleComponent]
public sealed class BattleCombatLightPolicyController : MonoBehaviour
{
    private const string PlayerFloorSpotlightName = "BattlePlayerFloorSpotlight";
    private const string CombatPointLightName = "BattleCombatPlayerPointLight";
    private const string CombatLightAnchorName = "CombatLightAnchor";

    private static BattleCombatLightPolicyController instance;

    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleStageTransitionController stageFlow;
    [SerializeField] private PlayerController player;

    [Header("COMBAT CHARACTER LIGHT POLICY")]
    [SerializeField, Min(0.10f)] private float bindingRefreshInterval = 0.75f;

    [Header("PLAYER COMBAT POINT LIGHT (OPTIONAL FILL)")]
    [SerializeField] private bool usePlayerCombatPointLight = false;
    [Tooltip("있으면 이 Transform을 플레이어 발밑 조명의 기준점으로 사용합니다. 비어 있으면 CombatLightAnchor 이름을 찾고, 그것도 없으면 시작 시 Sprite 하단을 기준점으로 캐시합니다.")]
    [SerializeField] private Transform combatLightAnchor;
    [SerializeField] private Color combatPointLightColor = new(1f, 0.985f, 0.94f, 1f);
    [SerializeField, Range(0f, 2f)] private float combatPointLightIntensity = 0.48f;
    [SerializeField, Min(0.05f)] private float combatPointLightInnerRadius = 0.80f;
    [SerializeField, Min(0.10f)] private float combatPointLightOuterRadius = 2.40f;
    [SerializeField, Range(0f, 1f)] private float combatPointLightFalloff = 0.80f;
    [SerializeField] private Vector2 combatPointLightOffset = new(0f, 0.05f);

    private BattleCharacterLightVisual[] cachedVisuals = System.Array.Empty<BattleCharacterLightVisual>();
    private SpriteRenderer legacyPlayerFloorSpotlight;
    private SpriteRenderer playerSpriteRenderer;
    private GameObject combatPointLightObject;
    private Light2D combatPointLight;
    private PlayerController boundPlayer;
    private Vector3 cachedFallbackFootLocalPosition;
    private bool hasCachedFallbackFootPosition;
    private float nextBindingRefresh;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ResolveReferences();
        RefreshBindings();
        RefreshPlayerLightBindings();
        SetCombatPointLightEnabled(false);
    }

    private void OnValidate()
    {
        bindingRefreshInterval = Mathf.Max(0.10f, bindingRefreshInterval);
        combatPointLightIntensity = Mathf.Max(0f, combatPointLightIntensity);
        combatPointLightInnerRadius = Mathf.Max(0.05f, combatPointLightInnerRadius);
        combatPointLightOuterRadius = Mathf.Max(combatPointLightInnerRadius + 0.05f, combatPointLightOuterRadius);
        combatPointLightFalloff = Mathf.Clamp01(combatPointLightFalloff);

        if (Application.isPlaying && combatPointLight != null)
            ConfigureCombatPointLight();
    }

    private void LateUpdate()
    {
        ResolveReferences();

        bool combat = IsCombat();
        if (!combat)
        {
            SetCombatPointLightEnabled(false);
            return;
        }

        if (Time.unscaledTime >= nextBindingRefresh)
            RefreshBindings();

        // Player의 Beam/Pool/Top Glow는 BattleFieldCinematicDirector와
        // BattlePlayerStageLightingController가 계속 구동합니다.
        // 여기서는 더 이상 CharacterLightVisual을 0으로 강제하지 않습니다.
        SetCombatPointLightEnabled(usePlayerCombatPointLight);
        if (usePlayerCombatPointLight)
            UpdateCombatPointLightPosition();
    }

    private void OnDisable()
    {
        SetCombatPointLightEnabled(false);
    }

    private void OnDestroy()
    {
        if (combatPointLightObject != null)
            Destroy(combatPointLightObject);

        if (instance == this)
            instance = null;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();

        if (stageFlow == null)
        {
            stageFlow = BattleStageTransitionController.Instance != null
                ? BattleStageTransitionController.Instance
                : FindFirstObjectByType<BattleStageTransitionController>();
        }

        if (player == null)
            player = FindFirstObjectByType<PlayerController>();

        if (player != boundPlayer)
            RefreshPlayerLightBindings();
    }

    private bool IsCombat()
    {
        if (stageFlow != null)
            return stageFlow.IsCombatPhase;

        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Combat;
    }

    private void RefreshBindings()
    {
        nextBindingRefresh = Time.unscaledTime + Mathf.Max(0.10f, bindingRefreshInterval);
        cachedVisuals = FindObjectsByType<BattleCharacterLightVisual>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
    }

    private void RefreshPlayerLightBindings()
    {
        boundPlayer = player;
        legacyPlayerFloorSpotlight = null;
        playerSpriteRenderer = null;
        hasCachedFallbackFootPosition = false;

        if (player == null)
        {
            combatLightAnchor = null;
            if (combatPointLightObject != null)
                Destroy(combatPointLightObject);
            combatPointLightObject = null;
            combatPointLight = null;
            return;
        }

        Transform legacy = FindRecursive(player.transform, PlayerFloorSpotlightName);
        if (legacy != null)
            legacyPlayerFloorSpotlight = legacy.GetComponent<SpriteRenderer>();

        if (combatLightAnchor == null || !combatLightAnchor.IsChildOf(player.transform))
            combatLightAnchor = FindRecursive(player.transform, CombatLightAnchorName);

        playerSpriteRenderer = ResolvePlayerRenderer();
        CacheFallbackFootPosition();

        EnsureCombatPointLight();
        UpdateCombatPointLightPosition();
    }

    private void SuppressAllCombatCharacterSpotlights()
    {
        if (cachedVisuals != null)
        {
            for (int i = 0; i < cachedVisuals.Length; i++)
            {
                BattleCharacterLightVisual visual = cachedVisuals[i];
                if (visual == null)
                    continue;

                // SetImmediate(0)는 Beam / Pool / Top Glow의 실제 Fade State까지 0으로 고정합니다.
                // 예전처럼 매 프레임 자식 이름을 재귀 탐색할 필요가 없습니다.
                visual.SetImmediate(0f);
            }
        }

        if (legacyPlayerFloorSpotlight != null)
            legacyPlayerFloorSpotlight.enabled = false;
    }

    private void EnsureCombatPointLight()
    {
        if (player == null)
            return;

        if (combatPointLightObject == null)
        {
            Transform existing = player.transform.Find(CombatPointLightName);
            combatPointLightObject = existing != null
                ? existing.gameObject
                : new GameObject(CombatPointLightName);
        }

        combatPointLightObject.transform.SetParent(player.transform, false);
        combatPointLightObject.transform.localRotation = Quaternion.identity;
        combatPointLightObject.transform.localScale = Vector3.one;

        if (combatPointLight == null)
        {
            combatPointLight = combatPointLightObject.GetComponent<Light2D>();
            if (combatPointLight == null)
                combatPointLight = combatPointLightObject.AddComponent<Light2D>();
        }

        ConfigureCombatPointLight();
    }

    private void ConfigureCombatPointLight()
    {
        if (combatPointLight == null)
            return;

        combatPointLight.lightType = Light2D.LightType.Point;
        combatPointLight.color = combatPointLightColor;
        combatPointLight.intensity = Mathf.Max(0f, combatPointLightIntensity);
        combatPointLight.pointLightInnerRadius = Mathf.Max(0.05f, combatPointLightInnerRadius);
        combatPointLight.pointLightOuterRadius = Mathf.Max(
            combatPointLight.pointLightInnerRadius + 0.05f,
            combatPointLightOuterRadius);
        combatPointLight.falloffIntensity = Mathf.Clamp01(combatPointLightFalloff);
        combatPointLight.pointLightInnerAngle = 360f;
        combatPointLight.pointLightOuterAngle = 360f;
        combatPointLight.shadowsEnabled = false;
    }

    private void SetCombatPointLightEnabled(bool enabled)
    {
        if (enabled && player != null && combatPointLight == null)
            EnsureCombatPointLight();

        if (combatPointLight != null)
        {
            if (enabled)
                ConfigureCombatPointLight();
            combatPointLight.enabled = enabled;
        }
    }

    private void UpdateCombatPointLightPosition()
    {
        if (combatPointLightObject == null || player == null)
            return;

        Vector3 basePosition;
        if (combatLightAnchor != null && combatLightAnchor.IsChildOf(player.transform))
        {
            basePosition = combatLightAnchor.position;
        }
        else if (hasCachedFallbackFootPosition)
        {
            basePosition = player.transform.TransformPoint(cachedFallbackFootLocalPosition);
        }
        else
        {
            basePosition = player.transform.position;
        }

        combatPointLightObject.transform.position = new Vector3(
            basePosition.x + combatPointLightOffset.x,
            basePosition.y + combatPointLightOffset.y,
            player.transform.position.z);
    }

    private void CacheFallbackFootPosition()
    {
        if (player == null || playerSpriteRenderer == null)
            return;

        Bounds bounds = playerSpriteRenderer.bounds;
        Vector3 worldFoot = new(
            player.transform.position.x,
            bounds.min.y,
            player.transform.position.z);

        cachedFallbackFootLocalPosition = player.transform.InverseTransformPoint(worldFoot);
        hasCachedFallbackFootPosition = true;
    }

    private SpriteRenderer ResolvePlayerRenderer()
    {
        if (player == null)
            return null;

        SpriteRenderer[] renderers = player.GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer best = null;
        float bestArea = -1f;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null ||
                renderer.name == BattleCharacterLightVisual.KeyRendererName ||
                renderer.name == BattleCharacterLightVisual.PoolRendererName ||
                renderer.name == BattleCharacterLightVisual.GlowRendererName ||
                renderer.name == PlayerFloorSpotlightName)
            {
                continue;
            }

            float area = Mathf.Abs(renderer.bounds.size.x * renderer.bounds.size.y);
            if (area <= bestArea)
                continue;

            bestArea = area;
            best = renderer;
        }

        return best;
    }

    private static Transform FindRecursive(Transform root, string targetName)
    {
        if (root == null)
            return null;

        if (root.name == targetName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindRecursive(root.GetChild(i), targetName);
            if (found != null)
                return found;
        }

        return null;
    }
}

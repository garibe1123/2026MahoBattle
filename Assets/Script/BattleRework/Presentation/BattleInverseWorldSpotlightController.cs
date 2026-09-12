using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Global World Light 밝기를 Character Spotlight의 단일 입력으로 사용합니다.
///
/// 규칙:
/// - World Light가 Show intensity까지 어두워지면 Spotlight strength = 1.
/// - World Light가 Idle intensity까지 밝아지면 Spotlight strength = 0.
/// - 그 사이는 SmoothStep으로 연속 보간합니다.
///
/// 별도의 Reward/Map ON/OFF 타이밍을 여기서 관리하지 않습니다.
/// BattleFieldCinematicDirector가 World Light target만 변경하면 Player/Presenter Spotlight가
/// 실제 현재 intensity에 반비례해 자연스럽게 따라옵니다.
///
/// Pre-combat의 전용 Player stage light와 Combat의 no-character-spotlight 정책은
/// 각각 BattlePlayerStageLightingController / BattleCombatLightPolicyController가 더 늦은
/// 최종 가시성 규칙으로 계속 소유합니다.
/// </summary>
[DefaultExecutionOrder(30000)]
[DisallowMultipleComponent]
public sealed class BattleInverseWorldSpotlightController : MonoBehaviour
{
    [SerializeField] private BattleFieldCinematicDirector fieldDirector;
    [SerializeField] private BattleShowWorldSetController showWorldSet;
    [SerializeField] private PlayerController player;
    [SerializeField, Min(0.05f)] private float bindingRefreshInterval = 0.20f;

    private Light2D worldLight;
    private BattleCharacterLightVisual playerLight;
    private BattleCharacterLightVisual presenterLight;
    private Transform presenterTarget;
    private float nextBindingRefresh;
    private float currentStrength;

    public float CurrentStrength => currentStrength;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterSceneLoad()
    {
        EnsureInstalled();
    }

    private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        EnsureInstalled();
    }

    private static void EnsureInstalled()
    {
        if (FindFirstObjectByType<BattleInverseWorldSpotlightController>(FindObjectsInactive.Include) != null)
            return;

        BattleFieldCinematicDirector director =
            FindFirstObjectByType<BattleFieldCinematicDirector>(FindObjectsInactive.Include);
        if (director != null)
        {
            director.gameObject.AddComponent<BattleInverseWorldSpotlightController>();
            return;
        }

        BattleSceneManager sceneManager =
            FindFirstObjectByType<BattleSceneManager>(FindObjectsInactive.Include);
        if (sceneManager != null)
            sceneManager.gameObject.AddComponent<BattleInverseWorldSpotlightController>();
    }

    private void OnEnable()
    {
        nextBindingRefresh = 0f;
        ResolveBindings(true);
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextBindingRefresh)
            ResolveBindings(false);

        BattleLightingProfileSO profile =
            fieldDirector != null ? fieldDirector.ActiveLightingProfile : null;
        if (worldLight == null || profile == null)
        {
            currentStrength = 0f;
            ApplyStrength(0f);
            return;
        }

        currentStrength = ResolveInverseStrength(worldLight.intensity, profile);
        ApplyStrength(currentStrength);
    }

    private void ResolveBindings(bool force)
    {
        nextBindingRefresh = Time.unscaledTime + Mathf.Max(0.05f, bindingRefreshInterval);

        if (fieldDirector == null)
            fieldDirector = FindFirstObjectByType<BattleFieldCinematicDirector>(FindObjectsInactive.Include);
        if (showWorldSet == null)
            showWorldSet = FindFirstObjectByType<BattleShowWorldSetController>(FindObjectsInactive.Include);
        if (player == null)
            player = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);

        if (worldLight == null || force)
            worldLight = ResolveWorldLight();

        if (player != null)
            playerLight = player.GetComponent<BattleCharacterLightVisual>();

        Transform nextPresenter = showWorldSet != null
            ? showWorldSet.PresenterWorldTransform
            : null;
        if (nextPresenter != presenterTarget || presenterLight == null)
        {
            presenterTarget = nextPresenter;
            presenterLight = presenterTarget != null
                ? presenterTarget.GetComponent<BattleCharacterLightVisual>()
                : null;
        }
    }

    private Light2D ResolveWorldLight()
    {
        Light2D[] lights = FindObjectsByType<Light2D>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        Light2D fallback = null;
        for (int i = 0; i < lights.Length; i++)
        {
            Light2D light = lights[i];
            if (light == null || light.lightType != Light2D.LightType.Global)
                continue;
            if (light.gameObject.scene != gameObject.scene)
                continue;

            fallback ??= light;
            if (light.name == "BattleWorldGlobalLight")
                return light;
        }

        return fallback;
    }

    private static float ResolveInverseStrength(float worldIntensity, BattleLightingProfileSO profile)
    {
        if (profile == null)
            return 0f;

        float darkReference = Mathf.Max(0f, profile.worldLightShowIntensity);
        float brightReference = Mathf.Max(darkReference + 0.001f, profile.worldLightIdleIntensity);

        // authored profile에서 Show/Idle 순서가 뒤집혀 있어도 "어두울수록 강함" 규칙은 유지합니다.
        if (profile.worldLightIdleIntensity < profile.worldLightShowIntensity)
        {
            darkReference = Mathf.Max(0f, profile.worldLightIdleIntensity);
            brightReference = Mathf.Max(darkReference + 0.001f, profile.worldLightShowIntensity);
        }

        float inverse = 1f - Mathf.InverseLerp(darkReference, brightReference, worldIntensity);
        inverse = Mathf.Clamp01(inverse);

        // 선형 변화보다 양 끝에서 속도가 자연스럽게 가라앉는 SmoothStep.
        return inverse * inverse * (3f - 2f * inverse);
    }

    private void ApplyStrength(float strength)
    {
        float resolved = Mathf.Clamp01(strength);

        if (playerLight != null && player != null && player.IsAlive && player.gameObject.activeInHierarchy)
            playerLight.SetTarget(resolved > 0.001f, resolved);
        else if (playerLight != null)
            playerLight.SetTarget(false, 0f);

        if (presenterLight != null && presenterTarget != null && presenterTarget.gameObject.activeInHierarchy)
            presenterLight.SetTarget(resolved > 0.001f, resolved);
        else if (presenterLight != null)
            presenterLight.SetTarget(false, 0f);
    }
}

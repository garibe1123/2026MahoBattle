using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum BattleUIThemeContext
{
    Combat,
    CombatTab,
    RuleRoulette,
    Reward,
    Map,
    Boss,
    Danger
}

[Serializable]
public sealed class BattleUIThemeProfile
{
    public BattleUIThemeContext context = BattleUIThemeContext.Combat;

    [Header("Neutral")]
    public Color background = new(0.035f, 0.038f, 0.045f, 1f);
    public Color surface = new(0.12f, 0.13f, 0.15f, 1f);
    public Color glassTint = new(0.80f, 0.84f, 0.88f, 0.18f);
    public Color textPrimary = new(0.96f, 0.97f, 0.98f, 1f);
    public Color textMuted = new(0.68f, 0.70f, 0.74f, 1f);

    [Header("Key")]
    public Color keyColor = new(1f, 0.82f, 0.10f, 1f);
    public Color keySoft = new(1f, 0.82f, 0.10f, 0.28f);

    [Header("Depth")]
    public Color depthShadow = new(0.01f, 0.012f, 0.018f, 0.48f);

    public BattleUIThemeProfile Clone()
    {
        return new BattleUIThemeProfile
        {
            context = context,
            background = background,
            surface = surface,
            glassTint = glassTint,
            textPrimary = textPrimary,
            textMuted = textMuted,
            keyColor = keyColor,
            keySoft = keySoft,
            depthShadow = depthShadow
        };
    }
}

/// <summary>
/// 전투 UI의 색상 소유권을 한 곳으로 모읍니다.
/// 플레이어 선택 테마가 아니라 현재 전투 모드/상황이 Context를 결정합니다.
/// 외부 시스템은 temporary override를 요청할 수 있고, 해제되면 자동 Context로 돌아갑니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-8800)]
public sealed class BattleUIThemeController : MonoBehaviour
{
    private sealed class OverrideRequest
    {
        public UnityEngine.Object owner;
        public BattleUIThemeContext context;
        public int priority;
    }

    private static BattleUIThemeController instance;
    private static readonly List<BattleUIThemeProfile> fallbackProfiles = BuildFallbackProfiles();

    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleKineticLoadoutUI loadoutUI;
    [SerializeField] private List<BattleUIThemeProfile> profiles = new();
    [SerializeField, Range(2f, 30f)] private float transitionSharpness = 10f;

    private readonly List<OverrideRequest> overrides = new();
    private BattleUIThemeContext currentContext = BattleUIThemeContext.Combat;
    private BattleUIThemeProfile currentProfile;
    private BattleUIThemeProfile displayedProfile;

    public static BattleUIThemeController Instance => instance;
    public BattleUIThemeContext CurrentContext => currentContext;
    public BattleUIThemeProfile CurrentProfile => displayedProfile ?? currentProfile ?? ResolveProfile(currentContext);

    public event Action<BattleUIThemeProfile> ThemeChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TryInstall();
    }

    private static void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        TryInstall();
    }

    private static void TryInstall()
    {
        if (FindFirstObjectByType<BattleUIThemeController>(FindObjectsInactive.Include) != null)
            return;

        BattleRunManager run = FindFirstObjectByType<BattleRunManager>(FindObjectsInactive.Include);
        GameObject host = run != null ? run.gameObject : GameObject.Find("BattleSystems");
        if (host != null)
            host.AddComponent<BattleUIThemeController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            return;
        }

        instance = this;
        EnsureProfiles();
        ResolveReferences();
        RefreshTheme(true);
    }

    private void OnEnable()
    {
        EnsureProfiles();
        ResolveReferences();
        RefreshTheme(true);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        ResolveReferences();
        CleanupOverrides();
        RefreshTheme(false);
        AnimateDisplayedTheme();
    }

    public void SetContextOverride(UnityEngine.Object owner, BattleUIThemeContext context, int priority = 0)
    {
        if (owner == null)
            return;

        for (int i = 0; i < overrides.Count; i++)
        {
            if (overrides[i].owner != owner)
                continue;

            overrides[i].context = context;
            overrides[i].priority = priority;
            RefreshTheme(true);
            return;
        }

        overrides.Add(new OverrideRequest
        {
            owner = owner,
            context = context,
            priority = priority
        });
        RefreshTheme(true);
    }

    public void ClearContextOverride(UnityEngine.Object owner)
    {
        if (owner == null)
            return;

        for (int i = overrides.Count - 1; i >= 0; i--)
        {
            if (overrides[i].owner == owner)
                overrides.RemoveAt(i);
        }

        RefreshTheme(true);
    }

    public BattleUIThemeProfile GetProfile(BattleUIThemeContext context)
    {
        return ResolveProfile(context);
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (loadoutUI == null)
            loadoutUI = FindFirstObjectByType<BattleKineticLoadoutUI>(FindObjectsInactive.Include);
    }

    private void RefreshTheme(bool force)
    {
        BattleUIThemeContext next = ResolveContext();
        BattleUIThemeProfile nextProfile = ResolveProfile(next);

        if (!force && next == currentContext && ReferenceEquals(nextProfile, currentProfile))
            return;

        currentContext = next;
        currentProfile = nextProfile;

        if (displayedProfile == null || force)
            displayedProfile = nextProfile.Clone();

        ThemeChanged?.Invoke(CurrentProfile);
    }

    private void AnimateDisplayedTheme()
    {
        if (currentProfile == null)
            return;

        if (displayedProfile == null)
        {
            displayedProfile = currentProfile.Clone();
            ThemeChanged?.Invoke(displayedProfile);
            return;
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(2f, transitionSharpness) * Time.unscaledDeltaTime);
        bool changed = false;

        changed |= LerpColor(ref displayedProfile.background, currentProfile.background, t);
        changed |= LerpColor(ref displayedProfile.surface, currentProfile.surface, t);
        changed |= LerpColor(ref displayedProfile.glassTint, currentProfile.glassTint, t);
        changed |= LerpColor(ref displayedProfile.textPrimary, currentProfile.textPrimary, t);
        changed |= LerpColor(ref displayedProfile.textMuted, currentProfile.textMuted, t);
        changed |= LerpColor(ref displayedProfile.keyColor, currentProfile.keyColor, t);
        changed |= LerpColor(ref displayedProfile.keySoft, currentProfile.keySoft, t);
        changed |= LerpColor(ref displayedProfile.depthShadow, currentProfile.depthShadow, t);
        displayedProfile.context = currentContext;

        if (changed)
            ThemeChanged?.Invoke(displayedProfile);
    }

    private static bool LerpColor(ref Color current, Color target, float t)
    {
        if ((current - target).sqrMagnitude <= 0.000002f)
        {
            current = target;
            return false;
        }

        current = Color.Lerp(current, target, t);
        return true;
    }

    private BattleUIThemeContext ResolveContext()
    {
        OverrideRequest best = null;
        for (int i = 0; i < overrides.Count; i++)
        {
            OverrideRequest candidate = overrides[i];
            if (candidate.owner == null)
                continue;

            if (best == null || candidate.priority > best.priority)
                best = candidate;
        }

        if (best != null)
            return best.context;

        if (runManager == null || !runManager.RunActive)
            return BattleUIThemeContext.Combat;

        if (runManager.State == BattleRunState.Reward)
            return BattleUIThemeContext.Reward;

        if (runManager.State == BattleRunState.SelectingNode)
            return BattleUIThemeContext.Map;

        if (runManager.State == BattleRunState.Combat &&
            loadoutUI != null &&
            loadoutUI.IsSwitchBoardOpen)
            return BattleUIThemeContext.CombatTab;

        return BattleUIThemeContext.Combat;
    }

    private BattleUIThemeProfile ResolveProfile(BattleUIThemeContext context)
    {
        for (int i = 0; i < profiles.Count; i++)
        {
            BattleUIThemeProfile profile = profiles[i];
            if (profile != null && profile.context == context)
                return profile;
        }

        for (int i = 0; i < fallbackProfiles.Count; i++)
        {
            if (fallbackProfiles[i].context == context)
                return fallbackProfiles[i];
        }

        return fallbackProfiles[0];
    }

    private void CleanupOverrides()
    {
        for (int i = overrides.Count - 1; i >= 0; i--)
        {
            if (overrides[i].owner == null)
                overrides.RemoveAt(i);
        }
    }

    private void EnsureProfiles()
    {
        if (profiles == null)
            profiles = new List<BattleUIThemeProfile>();

        if (profiles.Count > 0)
            return;

        for (int i = 0; i < fallbackProfiles.Count; i++)
            profiles.Add(fallbackProfiles[i].Clone());
    }

    private static List<BattleUIThemeProfile> BuildFallbackProfiles()
    {
        List<BattleUIThemeProfile> result = new();
        foreach (BattleUIThemeContext context in Enum.GetValues(typeof(BattleUIThemeContext)))
        {
            BattleUIThemeProfile profile = new()
            {
                context = context
            };
            result.Add(profile);
        }

        // 기본은 모두 무채색 + Yellow입니다.
        // 필요하면 Inspector에서 Context별 keyColor/glassTint만 바꿔도 전체 UI가 같이 바뀝니다.
        return result;
    }
}

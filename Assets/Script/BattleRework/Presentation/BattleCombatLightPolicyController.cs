using UnityEngine;

/// <summary>
/// Combat lighting policy override.
///
/// Combat must not use any per-character spotlight language.
/// Global world lighting + Volume are authoritative during Combat.
/// Pre-combat and Reward/Map presentation remain owned by their existing controllers.
///
/// This controller executes very late so older presentation scripts cannot accidentally
/// re-enable Player/Enemy key lights, contact pools, top glows, or the temporary floor spotlight
/// during the same frame.
/// </summary>
[DefaultExecutionOrder(32700)]
[DisallowMultipleComponent]
public sealed class BattleCombatLightPolicyController : MonoBehaviour
{
    private const string PlayerFloorSpotlightName = "BattlePlayerFloorSpotlight";

    private static BattleCombatLightPolicyController instance;

    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleStageTransitionController stageFlow;
    [SerializeField] private PlayerController player;
    [SerializeField, Min(0.05f)] private float bindingRefreshInterval = 0.20f;

    private BattleCharacterLightVisual[] cachedVisuals = System.Array.Empty<BattleCharacterLightVisual>();
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
    }

    private void LateUpdate()
    {
        ResolveReferences();

        if (!IsCombat())
            return;

        if (Time.unscaledTime >= nextBindingRefresh)
            RefreshBindings();

        SuppressAllCombatCharacterLights();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (stageFlow == null)
            stageFlow = BattleStageTransitionController.Instance != null
                ? BattleStageTransitionController.Instance
                : FindFirstObjectByType<BattleStageTransitionController>();
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();
    }

    private bool IsCombat()
    {
        if (stageFlow != null)
            return stageFlow.IsCombatPhase;

        // Compatibility fallback for scenes that have not installed the stage flow yet.
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Combat;
    }

    private void RefreshBindings()
    {
        nextBindingRefresh = Time.unscaledTime + Mathf.Max(0.05f, bindingRefreshInterval);
        cachedVisuals = FindObjectsByType<BattleCharacterLightVisual>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
    }

    private void SuppressAllCombatCharacterLights()
    {
        if (cachedVisuals != null)
        {
            for (int i = 0; i < cachedVisuals.Length; i++)
            {
                BattleCharacterLightVisual visual = cachedVisuals[i];
                if (visual == null)
                    continue;

                // Kill the fade state itself, not only the renderer, so it cannot bleed back in.
                visual.SetImmediate(0f);

                SetRendererEnabled(visual.transform, BattleCharacterLightVisual.KeyRendererName, false);
                SetRendererEnabled(visual.transform, BattleCharacterLightVisual.PoolRendererName, false);
                SetRendererEnabled(visual.transform, BattleCharacterLightVisual.GlowRendererName, false);
            }
        }

        if (player != null)
            SetRendererEnabled(player.transform, PlayerFloorSpotlightName, false);
    }

    private static void SetRendererEnabled(Transform root, string childName, bool enabled)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return;

        Transform child = FindRecursive(root, childName);
        if (child == null)
            return;

        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        if (renderer != null)
            renderer.enabled = enabled;
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
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Combat-only screen-space corner darkening.
///
/// This component is authored on the BattleSystems GameObject so its tuning values stay
/// visible and editable in the BattleScene Inspector before Play Mode. If another battle
/// scene is missing the component, the runtime fallback attaches it to that scene's
/// BattleSystems root instead of creating a separate presentation GameObject.
///
/// Unlike URP's circular Vignette, this overlay keeps the center almost untouched,
/// applies only a small amount of edge falloff, and deepens the four corners more strongly.
/// It sits below Battle HUD / Show Focus canvases, so it grades the battle image without
/// muddying UI readability or Reward / Map-selection presentation.
/// </summary>
[DefaultExecutionOrder(24500)]
[DisallowMultipleComponent]
public sealed class BattleCombatCornerVignetteController : MonoBehaviour
{
    private const string ShaderName = "UI/BattleCombatCornerVignette";
    private const string BattleSystemsName = "BattleSystems";
    private const int OverlaySortingOrder = 420;

    private static BattleCombatCornerVignetteController instance;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;

    [Header("Corner Vignette")]
    [SerializeField] private Color vignetteColor = Color.black;
    [Tooltip("전체 코너/엣지 비네팅 강도입니다. 0이면 효과가 완전히 꺼집니다.")]
    [SerializeField, Range(0f, 1f)] private float overallStrength = 1f;
    [Tooltip("화면 변 중앙부까지 아주 약하게 먹는 암부입니다. 너무 높이면 일반 비네팅처럼 보입니다.")]
    [SerializeField, Range(0f, 0.25f)] private float edgeDarkness = 0.045f;
    [Tooltip("네 귀퉁이에 추가되는 주 암부입니다.")]
    [SerializeField, Range(0f, 0.5f)] private float cornerDarkness = 0.22f;
    [Tooltip("좌우 끝에서 안쪽으로 암부가 퍼지는 범위입니다. 높을수록 중앙 쪽으로 넓게 퍼집니다.")]
    [SerializeField, Range(0.05f, 0.6f)] private float horizontalFalloff = 0.25f;
    [Tooltip("상하 끝에서 안쪽으로 암부가 퍼지는 범위입니다. 높을수록 중앙 쪽으로 넓게 퍼집니다.")]
    [SerializeField, Range(0.05f, 0.6f)] private float verticalFalloff = 0.31f;
    [Tooltip("값이 낮을수록 코너 암부가 넓고 부드럽게 퍼지고, 높을수록 모서리에 집중됩니다.")]
    [SerializeField, Range(0.25f, 4f)] private float cornerPower = 0.78f;

    [Header("Blend")]
    [SerializeField, Min(0.1f)] private float fadeInSharpness = 5.5f;
    [SerializeField, Min(0.1f)] private float fadeOutSharpness = 8f;

    private Canvas overlayCanvas;
    private Image overlayImage;
    private Material overlayMaterial;
    private float currentBlend;

    public static BattleCombatCornerVignetteController Instance => instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InstallSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        bool battleScene = scene.name == BattleSceneEntry.DefaultBattleSceneName;
        BattleSceneManager manager = Object.FindFirstObjectByType<BattleSceneManager>();

        if (!battleScene)
            battleScene = manager != null && manager.gameObject.scene == scene;

        if (!battleScene)
            return;

        BattleCombatCornerVignetteController existing =
            Object.FindFirstObjectByType<BattleCombatCornerVignetteController>();
        if (existing != null && existing.gameObject.scene == scene)
            return;

        GameObject host = FindBattleSystemsRoot(scene);

        if (host == null && manager != null && manager.gameObject.scene == scene)
            host = manager.gameObject;

        if (host == null)
        {
            host = new GameObject(BattleSystemsName);
            SceneManager.MoveGameObjectToScene(host, scene);
        }

        if (host.GetComponent<BattleCombatCornerVignetteController>() == null)
            host.AddComponent<BattleCombatCornerVignetteController>();
    }

    private static GameObject FindBattleSystemsRoot(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root != null && root.name == BattleSystemsName)
                return root;
        }

        return null;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ResolveReferences();
        EnsureOverlay();
        ApplyImmediate(0f);
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureOverlay();
    }

    private void Update()
    {
        ResolveReferences();
        EnsureOverlay();

        float target = IsCombat() ? 1f : 0f;
        float sharpness = target >= currentBlend
            ? Mathf.Max(0.1f, fadeInSharpness)
            : Mathf.Max(0.1f, fadeOutSharpness);

        float t = 1f - Mathf.Exp(-sharpness * Time.unscaledDeltaTime);
        currentBlend = Mathf.Lerp(currentBlend, target, t);

        if (Mathf.Abs(currentBlend - target) < 0.001f)
            currentBlend = target;

        ApplyVisual();
    }

    private void OnDisable()
    {
        ApplyImmediate(0f);
    }

    private void OnDestroy()
    {
        if (overlayMaterial != null)
            Destroy(overlayMaterial);

        if (instance == this)
            instance = null;
    }

    private void ResolveReferences()
    {
        if (runManager == null)
        {
            runManager = GetComponent<BattleRunManager>();
            if (runManager == null)
                runManager = FindFirstObjectByType<BattleRunManager>();
        }
    }

    private bool IsCombat()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Combat;
    }

    private void EnsureOverlay()
    {
        if (overlayCanvas == null)
        {
            GameObject canvasObject = new("BattleCombatCornerVignetteCanvas");
            canvasObject.transform.SetParent(transform, false);

            overlayCanvas = canvasObject.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder = OverlaySortingOrder;

            GameObject imageObject = new("BattleCombatCornerVignette", typeof(RectTransform));
            imageObject.transform.SetParent(canvasObject.transform, false);

            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            overlayImage = imageObject.AddComponent<Image>();
            overlayImage.raycastTarget = false;
            overlayImage.color = Color.white;
        }

        if (overlayMaterial == null)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                shader = Resources.Load<Shader>("BattleCombatCornerVignette");

            if (shader != null)
            {
                overlayMaterial = new Material(shader)
                {
                    name = "BattleCombatCornerVignette_Runtime",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        if (overlayImage != null)
            overlayImage.material = overlayMaterial;
    }

    private void ApplyVisual()
    {
        if (overlayImage == null || overlayMaterial == null)
            return;

        overlayMaterial.SetColor("_VignetteColor", vignetteColor);
        overlayMaterial.SetFloat("_Strength", Mathf.Clamp01(currentBlend * overallStrength));
        overlayMaterial.SetFloat("_EdgeDarkness", Mathf.Clamp(edgeDarkness, 0f, 0.25f));
        overlayMaterial.SetFloat("_CornerDarkness", Mathf.Clamp(cornerDarkness, 0f, 0.5f));
        overlayMaterial.SetFloat("_HorizontalFalloff", Mathf.Clamp(horizontalFalloff, 0.05f, 0.6f));
        overlayMaterial.SetFloat("_VerticalFalloff", Mathf.Clamp(verticalFalloff, 0.05f, 0.6f));
        overlayMaterial.SetFloat("_CornerPower", Mathf.Clamp(cornerPower, 0.25f, 4f));

        overlayImage.enabled = currentBlend > 0.001f;
    }

    private void ApplyImmediate(float value)
    {
        currentBlend = Mathf.Clamp01(value);

        if (overlayMaterial != null)
            overlayMaterial.SetFloat("_Strength", currentBlend * overallStrength);

        if (overlayImage != null)
            overlayImage.enabled = currentBlend > 0.001f;
    }
}

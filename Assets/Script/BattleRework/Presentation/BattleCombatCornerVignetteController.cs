using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Combat-only screen-space corner darkening.
///
/// This component is authored on the BattleSystems GameObject so its tuning values stay
/// visible and editable in the BattleScene Inspector before Play Mode.
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
    private const int OverlaySortingOrder = 420;

    private static BattleCombatCornerVignetteController instance;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleStageTransitionController stageFlow;

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

    [Header("Low HP Broadcast Signal")]
    [SerializeField, Range(0.2f, 0.4f)] private float warningThreshold = 0.35f;
    [SerializeField, Range(0.1f, 0.3f)] private float criticalThreshold = 0.20f;
    [SerializeField, Range(0.03f, 0.15f)] private float lastChanceThreshold = 0.10f;
    [SerializeField, Range(0f, 0.3f)] private float lowHpExtraVignette = 0.16f;
    [SerializeField, Range(0f, 0.2f)] private float damageBurstVignette = 0.08f;

    private Canvas overlayCanvas;
    private Image overlayImage;
    private Material overlayMaterial;
    private float currentBlend;

    private PlayerController player;
    private PlayerController subscribedPlayer;
    private BattleColorGradingController colorGrading;
    private float lowHpTarget;
    private float lowHpBlend;
    private float lastHp01 = 1f;
    private float damageBurst;
    private float signalRestoredUntil;
    private bool signalWasCritical;
    private Text signalStatusText;
    private CanvasGroup signalStatusGroup;

    public static BattleCombatCornerVignetteController Instance => instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        ResolveReferences();
        EnsurePlayerSubscription();
        EnsureOverlay();
        ApplyImmediate(0f);
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsurePlayerSubscription();
        EnsureOverlay();
    }

    private void Update()
    {
        ResolveReferences();
        EnsurePlayerSubscription();
        EnsureOverlay();

        bool combat = IsCombat();
        float target = combat ? 1f : 0f;
        float sharpness = target >= currentBlend
            ? Mathf.Max(0.1f, fadeInSharpness)
            : Mathf.Max(0.1f, fadeOutSharpness);

        float t = 1f - Mathf.Exp(-sharpness * Time.unscaledDeltaTime);
        currentBlend = Mathf.Lerp(currentBlend, target, t);

        if (Mathf.Abs(currentBlend - target) < 0.001f)
            currentBlend = target;

        float lowTarget = combat ? lowHpTarget : 0f;
        float lowT = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
        lowHpBlend = Mathf.Lerp(lowHpBlend, lowTarget, lowT);

        damageBurst = Mathf.MoveTowards(
            damageBurst,
            0f,
            Time.unscaledDeltaTime * 3.8f);

        colorGrading?.SetLowHpEmphasis(
            combat
                ? Mathf.Clamp01(lowHpBlend + damageBurst * 0.28f)
                : 0f);

        ApplyVisual();
        UpdateSignalStatus(combat);
    }

    private void OnDisable()
    {
        UnsubscribePlayer();
        colorGrading?.SetLowHpEmphasis(0f);
        lowHpBlend = 0f;
        damageBurst = 0f;
        ApplyImmediate(0f);
        HideSignalVisuals();
    }

    private void OnDestroy()
    {
        UnsubscribePlayer();

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

        if (stageFlow == null)
            stageFlow = BattleStageTransitionController.Instance != null
                ? BattleStageTransitionController.Instance
                : FindFirstObjectByType<BattleStageTransitionController>();

        if (player == null)
            player = FindFirstObjectByType<PlayerController>();

        if (colorGrading == null)
            colorGrading = FindFirstObjectByType<BattleColorGradingController>(FindObjectsInactive.Include);
    }

    private void EnsurePlayerSubscription()
    {
        if (player == null)
            player = FindFirstObjectByType<PlayerController>();

        if (subscribedPlayer == player)
            return;

        UnsubscribePlayer();
        subscribedPlayer = player;

        if (subscribedPlayer != null)
        {
            subscribedPlayer.HpChanged += HandleHpChanged;
            float maxHp = Mathf.Max(1f, subscribedPlayer.MaxHp);
            lastHp01 = Mathf.Clamp01(subscribedPlayer.CurrentHp / maxHp);
            signalWasCritical = lastHp01 <= criticalThreshold;
            lowHpTarget = ResolveLowHpSeverity(lastHp01);
        }
    }

    private void UnsubscribePlayer()
    {
        if (subscribedPlayer != null)
            subscribedPlayer.HpChanged -= HandleHpChanged;
        subscribedPlayer = null;
    }

    private void HandleHpChanged(float currentHp, float maxHp)
    {
        float hp01 = Mathf.Clamp01(currentHp / Mathf.Max(1f, maxHp));

        if (hp01 < lastHp01 - 0.0001f)
            damageBurst = 1f;

        if (hp01 <= criticalThreshold)
            signalWasCritical = true;

        if (signalWasCritical && hp01 > warningThreshold)
        {
            signalRestoredUntil = Time.unscaledTime + 1.0f;
            signalWasCritical = false;
        }

        lastHp01 = hp01;
        lowHpTarget = ResolveLowHpSeverity(hp01);
    }

    private float ResolveLowHpSeverity(float hp01)
    {
        if (hp01 > warningThreshold)
            return 0f;

        if (hp01 > criticalThreshold)
        {
            float t = Mathf.InverseLerp(warningThreshold, criticalThreshold, hp01);
            return Mathf.Lerp(0.18f, 0.38f, t);
        }

        if (hp01 > lastChanceThreshold)
        {
            float t = Mathf.InverseLerp(criticalThreshold, lastChanceThreshold, hp01);
            return Mathf.Lerp(0.52f, 0.78f, t);
        }

        float lastT = Mathf.InverseLerp(lastChanceThreshold, 0f, hp01);
        return Mathf.Lerp(0.84f, 1f, lastT);
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

        EnsureSignalVisuals();
    }

    private void EnsureSignalVisuals()
    {
        if (overlayCanvas == null)
            return;

        if (signalStatusText == null)
        {
            GameObject status = new("BattleSignalStatus", typeof(RectTransform));
            status.transform.SetParent(overlayCanvas.transform, false);

            RectTransform rect = status.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.16f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(420f, 44f);

            signalStatusText = status.AddComponent<Text>();
            signalStatusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            signalStatusText.fontSize = 17;
            signalStatusText.fontStyle = FontStyle.Bold;
            signalStatusText.alignment = TextAnchor.MiddleCenter;
            signalStatusText.color = new Color(0.92f, 0.95f, 1f, 1f);
            signalStatusText.raycastTarget = false;

            signalStatusGroup = status.AddComponent<CanvasGroup>();
            signalStatusGroup.blocksRaycasts = false;
            signalStatusGroup.interactable = false;
            signalStatusGroup.alpha = 0f;
        }
    }

    private void UpdateSignalStatus(bool combat)
    {
        EnsureSignalVisuals();

        if (signalStatusText == null || signalStatusGroup == null)
            return;

        if (combat && lastHp01 <= lastChanceThreshold)
        {
            signalStatusText.text = "CRITICAL";
            signalStatusGroup.alpha =
                Mathf.Lerp(0.55f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f));
        }
        else if (combat && Time.unscaledTime < signalRestoredUntil)
        {
            signalStatusText.text = "SIGNAL RESTORED";
            signalStatusGroup.alpha =
                Mathf.Clamp01((signalRestoredUntil - Time.unscaledTime) / 0.35f);
        }
        else
        {
            signalStatusGroup.alpha = 0f;
        }
    }

    private void HideSignalVisuals()
    {
        if (signalStatusGroup != null)
            signalStatusGroup.alpha = 0f;
    }

    private void ApplyVisual()
    {
        if (overlayImage == null || overlayMaterial == null)
            return;

        overlayMaterial.SetColor("_VignetteColor", vignetteColor);
        float signalStrength =
            currentBlend * overallStrength +
            lowHpBlend * lowHpExtraVignette +
            damageBurst * damageBurstVignette;

        overlayMaterial.SetFloat("_Strength", Mathf.Clamp01(signalStrength));
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
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Single owner for gameplay spotlight presentation.
/// Combat / Reward / Map share one beam policy and one runtime focus-mask Canvas.
/// Lens vignette stays independent in BattleCombatCornerVignetteController.
/// </summary>
[DefaultExecutionOrder(31900)]
[DisallowMultipleComponent]
public sealed class BattleSpotlightController : MonoBehaviour
{
    private const string FocusShaderName = "UI/BattleShowFocusMask";
    private const int FocusSortingOrder = 450;

    public enum BeamShapeDirection { NarrowAtTop, NarrowAtBottom }
    private enum SpotlightMode { None, Combat, Reward, Map }

    private struct FocusFrame
    {
        public Vector2 dimCenter;
        public float nearDim, farDim, dimRadius;

        public Vector2 playerCenter;
        public float playerRadius, playerStrength, playerScaleX, playerScaleY;

        public Vector2 presenterCenter;
        public float presenterRadius, presenterStrength;

        public Vector4 screenRect;
        public float screenStrength;

        public Vector2 itemCenter;
        public float itemRadius, itemStrength, itemFeather;

        public float characterVerticalRatio, characterLowerOffset, characterFeather, rectFeather;
    }

    private static BattleSpotlightController activeInstance;

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleStageTransitionController stageFlow;
    [SerializeField] private BattleShowWorldSetController showWorldSet;
    [SerializeField] private BattlePlayerStageLightingController stageLighting;
    [SerializeField] private PlayerController player;

    [Header("Shared Beam")]
    [SerializeField] private BeamShapeDirection beamShapeDirection = BeamShapeDirection.NarrowAtTop;
    [SerializeField, Range(-180f, 180f)] private float beamRotationDegrees;
    [SerializeField] private Vector2 beamLocalOffset = Vector2.zero;

    [Header("Combat Focus")]
    [SerializeField, Range(0f, 1f)] private float combatDimAlpha = 0.24f;
    [SerializeField, Min(0.1f)] private float combatFocusRadiusWorld = 1.78f;
    [SerializeField, Range(0.55f, 1f)] private float combatVerticalRatio = 0.72f;
    [SerializeField, Range(0.001f, 0.10f)] private float combatFeather = 0.040f;
    [SerializeField, Min(0.1f)] private float combatFocusFollowSharpness = 5.4f;
    [SerializeField, Min(0f)] private float combatFocusMaxLagWorld = 0.42f;
    [SerializeField, Range(0f, 0.45f)] private float combatFocusScaleResponse = 0.28f;

    [Header("Combat Scale-only Beam")]
    [SerializeField, Min(0.5f)] private float virtualLightHeight = 3.8f;
    [SerializeField, Min(0.1f)] private float aimFollowSharpness = 16f;
    [SerializeField, Min(0.1f)] private float sourceFollowSharpness = 2.9f;
    [SerializeField, Min(0f)] private float sourceVelocityTrailSeconds = 0.22f;
    [SerializeField, Min(0f)] private float sourceAccelerationTrail = 0.011f;
    [SerializeField, Min(0f)] private float maxSourceLagDistance = 1.75f;
    [SerializeField, Range(5f, 45f)] private float fullScaleResponseAngleDegrees = 26f;
    [SerializeField, Range(0f, 0.65f)] private float maxBeamLengthBoost = 0.38f;
    [SerializeField, Range(0f, 0.45f)] private float maxBeamWidthBoost = 0.22f;
    [SerializeField, Range(0f, 0.60f)] private float maxBeamBottomWidthBoost = 0.34f;
    [SerializeField, Range(0f, 0.8f)] private float motionOpacityLoss = 0.26f;
    [SerializeField, Range(0f, 1f)] private float beamSourceLagVisualScale = 0.82f;
    [SerializeField, Min(0f)] private float maxBeamSourceVisualOffset = 0.78f;

    [Header("Reward Focus")]
    [SerializeField, Range(0f, 1f)] private float rewardNearDimAlpha = 0.62f;
    [SerializeField, Range(0f, 1f)] private float rewardFarDimAlpha = 0.96f;
    [SerializeField, Range(0.05f, 1.5f)] private float rewardDimRadius = 0.58f;
    [SerializeField, Min(0.1f)] private float rewardItemRadiusWorld = 0.92f;
    [SerializeField, Range(0.001f, 0.08f)] private float rewardItemFeather = 0.020f;

    [Header("Reward / Map Character Focus")]
    [SerializeField, Min(0.1f)] private float showPlayerRadiusWorld = 1.48f;
    [SerializeField, Min(0.1f)] private float showPresenterRadiusWorld = 1.92f;
    [SerializeField, Range(0.2f, 1f)] private float showCharacterVerticalRatio = 0.86f;
    [SerializeField, Range(0f, 1f)] private float showCharacterLowerOffset = 0.02f;
    [SerializeField, Range(0.001f, 0.08f)] private float showCharacterFeather = 0.018f;
    [SerializeField, Range(0.0001f, 0.04f)] private float screenRectFeather = 0.0035f;
    [SerializeField, Range(-0.05f, 0.05f)] private float screenRectPadding = 0.002f;

    [Header("Reward / Map Idle")]
    [SerializeField] private bool useShowIdle = true;
    [SerializeField, Min(0.05f)] private float rewardIdleCyclesPerSecond = 0.30f;
    [SerializeField, Min(0.05f)] private float mapIdleCyclesPerSecond = 0.22f;
    [SerializeField, Range(0f, 0.10f)] private float rewardBeamWidthAmplitude = 0.035f;
    [SerializeField, Range(0f, 0.10f)] private float rewardBeamLengthAmplitude = 0.045f;
    [SerializeField, Range(0f, 0.15f)] private float rewardBeamAlphaAmplitude = 0.055f;
    [SerializeField, Range(0f, 0.10f)] private float mapBeamWidthAmplitude = 0.020f;
    [SerializeField, Range(0f, 0.10f)] private float mapBeamLengthAmplitude = 0.028f;
    [SerializeField, Range(0f, 0.15f)] private float mapBeamAlphaAmplitude = 0.035f;
    [SerializeField, Range(0f, 0.10f)] private float rewardFocusRadiusPulse = 0.045f;
    [SerializeField, Range(0f, 0.10f)] private float mapFocusRadiusPulse = 0.025f;
    [SerializeField, Range(0f, 0.30f)] private float focusFeatherPulse = 0.12f;
    [SerializeField, Min(0.1f)] private float visualRefreshInterval = 0.40f;

    [Header("Focus Fade")]
    [SerializeField, Min(0.1f)] private float combatFadeSharpness = 8f;
    [SerializeField, Min(0.1f)] private float showFadeSharpness = 6f;

    [Header("Mode Transition Tween")]
    [Tooltip("Combat / Reward / Map 전환 때 Spotlight 크기와 밝기가 새 프로필로 정착하는 시간입니다.")]
    [SerializeField, Min(0.08f)] private float modeTransitionDuration = 0.54f;
    [SerializeField, Range(0.65f, 1f)] private float transitionStartScale = 0.90f;
    [SerializeField, Range(0f, 0.18f)] private float transitionScaleOvershoot = 0.07f;
    [SerializeField, Range(0.1f, 1f)] private float transitionStartAlpha = 0.58f;
    [SerializeField, Range(0f, 0.15f)] private float focusTransitionOvershoot = 0.045f;

    [Header("Stage Light Flicker")]
    [Tooltip("모드 전환 때 공연 조명처럼 짧게 세 번 꺼졌다 켜지는 펄스를 넣습니다.")]
    [SerializeField] private bool useStageLightFlicker = true;
    [SerializeField, Range(0f, 0.95f)] private float stageFlickerStrength = 0.82f;

    private SpotlightMode mode;
    private Canvas focusCanvas;
    private Image focusImage;
    private Material focusMaterial;

    private BattleCharacterLightVisual[] lightVisuals;
    private float nextVisualRefresh;

    private Transform playerBeamTransform;
    private SpriteRenderer playerBeamRenderer;
    private SpriteRenderer playerGlowRenderer;
    private MaterialPropertyBlock beamProperties;
    private MaterialPropertyBlock glowProperties;

    private Vector2 lastPlayerPosition, lastPlayerVelocity;
    private Vector2 virtualAimWorld, virtualSourceWorld;
    private Vector2 currentGroundDirection = Vector2.down;
    private float currentMotion01;
    private bool hasMotionSample;

    private Vector3 combatFocusWorld;
    private bool combatFocusInitialized;
    private float focusBlend;

    private bool modeTransitionActive;
    private float modeTransitionStartedAt = -1f;
    private FocusFrame transitionFromFocusFrame;
    private bool hasTransitionFocusSource;
    private FocusFrame lastAppliedFocusFrame;
    private bool hasLastAppliedFocusFrame;

    public static BattleSpotlightController Instance => activeInstance;

    public BeamShapeDirection Direction
    {
        get => beamShapeDirection;
        set { if (beamShapeDirection != value) { beamShapeDirection = value; PushArtistSettings(); } }
    }

    public float RotationDegrees
    {
        get => beamRotationDegrees;
        set
        {
            float normalized = NormalizeDegrees(value);
            if (!Mathf.Approximately(beamRotationDegrees, normalized))
            {
                beamRotationDegrees = normalized;
                PushArtistSettings();
            }
        }
    }

    public Vector2 LocalOffset
    {
        get => beamLocalOffset;
        set { if (beamLocalOffset != value) { beamLocalOffset = value; PushArtistSettings(); } }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallRuntimeFallback()
    {
        if (FindFirstObjectByType<BattleSpotlightController>(FindObjectsInactive.Include) != null)
            return;

        BattleSceneManager manager = FindFirstObjectByType<BattleSceneManager>(FindObjectsInactive.Include);
        if (manager != null)
            manager.gameObject.AddComponent<BattleSpotlightController>();
    }

    private void Awake()
    {
        if (activeInstance != null && activeInstance != this) { Destroy(this); return; }
        activeInstance = this;
        ResolveReferences();
        EnsureFocusOverlay();
        PushArtistSettings();
        ResetCombatMotion();
        HideFocusImmediate();
    }

    private void OnEnable()
    {
        activeInstance = this;
        ResolveReferences();
        EnsureFocusOverlay();
        PushArtistSettings();
    }

    private void OnDisable()
    {
        ResetCombatMotion();
        ClearCombatBeamProperties();
        HideFocusImmediate();
        if (activeInstance == this) activeInstance = null;
    }

    private void OnDestroy()
    {
        if (focusMaterial != null) Destroy(focusMaterial);
        if (activeInstance == this) activeInstance = null;
    }

    private void OnValidate()
    {
        beamRotationDegrees = NormalizeDegrees(beamRotationDegrees);
        if (Application.isPlaying && isActiveAndEnabled)
            PushArtistSettings();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        EnsureFocusOverlay();

        SpotlightMode next = ResolveMode();
        if (next != mode) ChangeMode(next);

        switch (mode)
        {
            case SpotlightMode.Combat: UpdateCombat(); break;
            case SpotlightMode.Reward: UpdateShow(reward: true); break;
            case SpotlightMode.Map: UpdateShow(reward: false); break;
            default: UpdateInactive(); break;
        }

        ApplyModeTransitionToBeams();
        FinishModeTransitionIfSettled();
    }

    private SpotlightMode ResolveMode()
    {
        if (runManager == null || !runManager.RunActive) return SpotlightMode.None;

        if ((stageFlow != null && stageFlow.IsCombatPhase) || runManager.State == BattleRunState.Combat)
            return SpotlightMode.Combat;
        if (runManager.State == BattleRunState.Reward) return SpotlightMode.Reward;
        if (runManager.State == BattleRunState.SelectingNode) return SpotlightMode.Map;
        return SpotlightMode.None;
    }

    private void ChangeMode(SpotlightMode next)
    {
        SpotlightMode previous = mode;

        if (previous == SpotlightMode.Combat)
        {
            ResetCombatMotion();
            ClearCombatBeamProperties();
        }

        hasTransitionFocusSource =
            previous != SpotlightMode.None &&
            hasLastAppliedFocusFrame;

        if (hasTransitionFocusSource)
            transitionFromFocusFrame = lastAppliedFocusFrame;

        mode = next;
        combatFocusInitialized = false;

        if (mode != SpotlightMode.Combat)
            ClearCombatBeamProperties();

        modeTransitionActive =
            next != SpotlightMode.None;

        modeTransitionStartedAt =
            modeTransitionActive
                ? Time.unscaledTime
                : -1f;

        PushArtistSettings();
    }

    private void UpdateCombat()
    {
        float dt = Time.unscaledDeltaTime;
        bool valid = player != null && player.IsAlive && player.gameObject.activeInHierarchy;
        focusBlend = Damp01(focusBlend, valid ? 1f : 0f, combatFadeSharpness, dt);

        if (!valid) { HideFocusIfZero(); return; }

        ResolvePlayerBeam();
        if (playerBeamTransform != null && playerBeamRenderer != null && playerBeamRenderer.sprite != null)
        {
            UpdateCombatMotion();
            ApplyCombatBeam();
        }

        combatFocusWorld = ResolveCombatFocusPosition(ResolvePlayerVisualCenter(), dt);
        ApplyFocus(BuildCombatFocusFrame());
    }

    private void UpdateShow(bool reward)
    {
        ResetCombatMotion();
        ClearCombatBeamProperties();

        bool ready = showWorldSet != null && showWorldSet.IsShowActive;
        focusBlend = Damp01(focusBlend, ready ? 1f : 0f, showFadeSharpness, Time.unscaledDeltaTime);

        if (useShowIdle && ready) ApplyShowIdle(reward);
        if (ready) ApplyFocus(BuildShowFocusFrame(reward));
        else HideFocusIfZero();
    }

    private void UpdateInactive()
    {
        ResetCombatMotion();
        ClearCombatBeamProperties();
        focusBlend = Damp01(focusBlend, 0f, showFadeSharpness, Time.unscaledDeltaTime);
        HideFocusIfZero();
    }

    private void UpdateCombatMotion()
    {
        float dt = Mathf.Min(0.05f, Mathf.Max(0.001f, Time.unscaledDeltaTime));
        Vector2 position = player.transform.position;

        if (!hasMotionSample || Vector2.Distance(position, lastPlayerPosition) > 3f)
        {
            lastPlayerPosition = virtualAimWorld = virtualSourceWorld = position;
            lastPlayerVelocity = Vector2.zero;
            currentGroundDirection = Vector2.down;
            currentMotion01 = 0f;
            hasMotionSample = true;
            return;
        }

        Vector2 velocity = (position - lastPlayerPosition) / dt;
        Vector2 acceleration = Vector2.ClampMagnitude((velocity - lastPlayerVelocity) / dt, 30f);
        lastPlayerPosition = position;
        lastPlayerVelocity = velocity;

        float aimT = 1f - Mathf.Exp(-Mathf.Max(0.1f, aimFollowSharpness) * dt);
        virtualAimWorld = Vector2.Lerp(virtualAimWorld, position, aimT);

        Vector2 trail = -velocity * Mathf.Max(0f, sourceVelocityTrailSeconds);
        trail += -acceleration * Mathf.Max(0f, sourceAccelerationTrail);
        trail = Vector2.ClampMagnitude(trail, Mathf.Max(0f, maxSourceLagDistance));

        float sourceT = 1f - Mathf.Exp(-Mathf.Max(0.1f, sourceFollowSharpness) * dt);
        virtualSourceWorld = Vector2.Lerp(virtualSourceWorld, position + trail, sourceT);

        Vector2 planar = virtualAimWorld - virtualSourceWorld;
        float distance = planar.magnitude;
        if (distance > 0.0001f) currentGroundDirection = planar / distance;

        float angle = Mathf.Atan2(distance, Mathf.Max(0.5f, virtualLightHeight));
        float fullAngle = Mathf.Deg2Rad * Mathf.Clamp(fullScaleResponseAngleDegrees, 5f, 45f);
        currentMotion01 = Mathf.Clamp01(angle / Mathf.Max(0.001f, fullAngle));
    }

    private void ApplyCombatBeam()
    {
        Quaternion rotation = playerBeamTransform.rotation;
        Vector3 center = playerBeamTransform.position;
        Vector3 scale = playerBeamTransform.localScale;
        float worldHeight = playerBeamRenderer.sprite.bounds.size.y * Mathf.Abs(playerBeamTransform.lossyScale.y);
        if (worldHeight <= 0.0001f) return;

        float response = Mathf.Sqrt(Mathf.Clamp01(currentMotion01));
        float lengthScale = 1f + Mathf.Max(0f, maxBeamLengthBoost) * response;
        float widthScale = 1f + Mathf.Max(0f, maxBeamWidthBoost) * response;

        Vector3 source = center + rotation * (Vector3.up * (worldHeight * 0.5f));
        Vector2 sourceLag = virtualSourceWorld - virtualAimWorld;
        Vector2 sourceOffset = Vector2.ClampMagnitude(
            sourceLag * Mathf.Clamp01(beamSourceLagVisualScale),
            Mathf.Max(0f, maxBeamSourceVisualOffset));

        source += new Vector3(sourceOffset.x, sourceOffset.y * 0.45f, 0f);

        playerBeamTransform.localScale = new Vector3(
            scale.x * widthScale, scale.y * lengthScale, scale.z);
        playerBeamTransform.position = source + rotation * (Vector3.down * (worldHeight * lengthScale * 0.5f));
        playerBeamTransform.rotation = rotation;

        beamProperties ??= new MaterialPropertyBlock();
        playerBeamRenderer.GetPropertyBlock(beamProperties);
        beamProperties.SetFloat("_BeamBottomWidthScale", 1f + Mathf.Max(0f, maxBeamBottomWidthBoost) * response);
        beamProperties.SetFloat("_BeamOpacityScale", Mathf.Clamp01(1f - Mathf.Max(0f, motionOpacityLoss) * response));
        playerBeamRenderer.SetPropertyBlock(beamProperties);

        if (playerGlowRenderer != null)
        {
            glowProperties ??= new MaterialPropertyBlock();
            playerGlowRenderer.GetPropertyBlock(glowProperties);
            glowProperties.SetFloat("_HorizontalBias", Mathf.Clamp(-currentGroundDirection.x * response, -1f, 1f));
            glowProperties.SetFloat("_DirectionalAmount", 0.60f);
            playerGlowRenderer.SetPropertyBlock(glowProperties);
        }
    }

    private void ApplyShowIdle(bool reward)
    {
        RefreshVisualsIfNeeded();
        if (lightVisuals == null) return;

        float cycles = reward ? rewardIdleCyclesPerSecond : mapIdleCyclesPerSecond;
        float widthAmp = reward ? rewardBeamWidthAmplitude : mapBeamWidthAmplitude;
        float lengthAmp = reward ? rewardBeamLengthAmplitude : mapBeamLengthAmplitude;
        float alphaAmp = reward ? rewardBeamAlphaAmplitude : mapBeamAlphaAmplitude;
        float time = Time.unscaledTime * Mathf.Max(0.05f, cycles) * Mathf.PI * 2f;

        foreach (BattleCharacterLightVisual visual in lightVisuals)
        {
            if (visual == null || !visual.isActiveAndEnabled || visual.CurrentSpotlightStrength <= 0.01f)
                continue;

            Transform beam = visual.transform.Find(BattleCharacterLightVisual.KeyRendererName);
            SpriteRenderer renderer = beam != null ? beam.GetComponent<SpriteRenderer>() : null;
            if (renderer == null || !renderer.enabled) continue;

            float phase = Mathf.Abs(visual.GetInstanceID() % 997) * 0.0137f;
            float breath = Mathf.Clamp(
                Mathf.Sin(time + phase) * 0.72f +
                Mathf.Sin(time * 0.47f + phase * 1.71f) * 0.28f,
                -1f, 1f);

            Vector3 baseScale = beam.localScale;
            beam.localScale = new Vector3(
                baseScale.x * (1f + breath * widthAmp),
                baseScale.y * (1f + Mathf.Sin(time + phase + 0.65f) * lengthAmp),
                baseScale.z);

            Color color = renderer.color;
            color.a *= Mathf.Clamp(1f + breath * alphaAmp, 0.80f, 1.15f);
            renderer.color = color;
        }
    }

    private FocusFrame BuildCombatFocusFrame()
    {
        Camera camera = Camera.main;
        FocusFrame frame = DefaultFrame();

        if (camera == null ||
            !TryProjectWorldPoint(camera, combatFocusWorld, combatFocusRadiusWorld,
                out frame.playerCenter, out frame.playerRadius))
            return frame;

        Vector2 screenDirection = ResolveScreenDirection(camera, combatFocusWorld, currentGroundDirection);
        float response = Mathf.Sqrt(Mathf.Clamp01(currentMotion01));

        frame.dimCenter = frame.playerCenter;
        frame.nearDim = frame.farDim = combatDimAlpha;
        frame.dimRadius = 1f;
        frame.playerStrength = 1f;
        frame.playerScaleX = 1f + response * (0.08f + combatFocusScaleResponse * Mathf.Abs(screenDirection.x));
        frame.playerScaleY = 1f + response * (0.04f + combatFocusScaleResponse * 0.55f * Mathf.Abs(screenDirection.y));
        frame.characterVerticalRatio = combatVerticalRatio;
        frame.characterLowerOffset = 0f;
        frame.characterFeather = combatFeather;
        return frame;
    }

    private FocusFrame BuildShowFocusFrame(bool reward)
    {
        Camera camera = Camera.main;
        FocusFrame frame = DefaultFrame();
        if (camera == null) return frame;

        float cycles = reward ? rewardIdleCyclesPerSecond : mapIdleCyclesPerSecond;
        float radiusPulse = reward ? rewardFocusRadiusPulse : mapFocusRadiusPulse;
        float wave = useShowIdle ? EvaluateIdleBreath(Time.unscaledTime, cycles, reward ? 0.37f : 1.11f) : 0f;

        float playerRadiusWorld = !reward && stageLighting != null
            ? stageLighting.UnifiedPlayerFocusRadiusWorld
            : showPlayerRadiusWorld;

        bool playerVisible = TryProjectCharacter(
            camera, player != null && player.IsAlive ? player.transform : null,
            playerRadiusWorld, out frame.playerCenter, out frame.playerRadius);

        Transform presenter = showWorldSet != null ? showWorldSet.PresenterWorldTransform : null;
        bool presenterVisible = TryProjectCharacter(
            camera, presenter, showPresenterRadiusWorld,
            out frame.presenterCenter, out frame.presenterRadius);

        if (playerVisible) frame.playerRadius *= 1f + wave * radiusPulse;
        if (presenterVisible)
        {
            float pWave = useShowIdle ? EvaluateIdleBreath(Time.unscaledTime, cycles, 2.43f) : 0f;
            frame.presenterRadius *= 1f + pWave * radiusPulse;
        }

        frame.playerStrength = playerVisible ? 1f : 0f;
        frame.presenterStrength = presenterVisible ? 1f : 0f;
        frame.dimCenter = frame.playerCenter;

        if (reward)
        {
            frame.nearDim = rewardNearDimAlpha;
            frame.farDim = rewardFarDimAlpha;
            frame.dimRadius = rewardDimRadius;

            if (showWorldSet != null && showWorldSet.RewardHoveredIndex >= 0 &&
                showWorldSet.TryGetRewardShowcaseWorldPosition(showWorldSet.RewardHoveredIndex, out Vector3 itemWorld) &&
                TryProjectWorldPoint(camera, itemWorld, rewardItemRadiusWorld,
                    out frame.itemCenter, out frame.itemRadius))
            {
                frame.itemStrength = 1f;
                frame.itemRadius *= 1f + wave * rewardFocusRadiusPulse;
            }
        }
        else
        {
            frame.nearDim = stageLighting != null ? stageLighting.UnifiedNearDimAlpha : rewardNearDimAlpha;
            frame.farDim = stageLighting != null ? stageLighting.UnifiedFarDimAlpha : rewardFarDimAlpha;
            frame.dimRadius = stageLighting != null ? stageLighting.UnifiedDimFalloffRadius : rewardDimRadius;

            if (TryProjectScreenRect(camera, out frame.screenRect))
                frame.screenStrength = 1f;
        }

        frame.itemFeather = Mathf.Max(0.0001f, rewardItemFeather * (1f + wave * focusFeatherPulse));
        frame.characterVerticalRatio = !reward && stageLighting != null
            ? stageLighting.UnifiedCharacterVerticalRatio : showCharacterVerticalRatio;
        frame.characterLowerOffset = !reward && stageLighting != null
            ? stageLighting.UnifiedCharacterLowerOffset : showCharacterLowerOffset;
        frame.characterFeather = (!reward && stageLighting != null
            ? stageLighting.UnifiedCharacterFeather : showCharacterFeather) * (1f + wave * focusFeatherPulse);

        return frame;
    }

    private FocusFrame DefaultFrame()
    {
        return new FocusFrame
        {
            dimCenter = new Vector2(0.5f, 0.5f),
            playerCenter = new Vector2(0.5f, 0.5f),
            presenterCenter = new Vector2(0.5f, 0.5f),
            itemCenter = new Vector2(0.5f, 0.5f),
            screenRect = new Vector4(0.5f, 0.5f, 0.5f, 0.5f),
            playerScaleX = 1f,
            playerScaleY = 1f,
            itemFeather = rewardItemFeather,
            characterVerticalRatio = showCharacterVerticalRatio,
            characterLowerOffset = showCharacterLowerOffset,
            characterFeather = showCharacterFeather,
            rectFeather = screenRectFeather
        };
    }

    private void ApplyFocus(FocusFrame f)
    {
        if (focusMaterial == null || focusBlend <= 0.0001f) { HideFocusIfZero(); return; }

        float transitionT = GetModeTransition01();
        float ease = Smooth01(transitionT);

        if (modeTransitionActive && hasTransitionFocusSource)
            f = LerpFocusFrame(transitionFromFocusFrame, f, ease);

        if (modeTransitionActive)
        {
            float flicker = EvaluateStageLightFlicker(transitionT);
            float focusScale = EvaluateFocusTransitionScale(transitionT, flicker);

            f.playerRadius *= focusScale;
            f.presenterRadius *= focusScale;
            f.itemRadius *= focusScale;

            // The darkness itself stays stable; only the exposed spotlight apertures flicker.
            f.playerStrength *= flicker;
            f.presenterStrength *= flicker;
            f.itemStrength *= flicker;
        }

        lastAppliedFocusFrame = f;
        hasLastAppliedFocusFrame = true;

        focusMaterial.SetColor("_MaskColor", Color.black);
        focusMaterial.SetFloat("_Presentation", focusBlend);
        focusMaterial.SetVector("_DimCenter", new Vector4(f.dimCenter.x, f.dimCenter.y, 0f, 0f));
        focusMaterial.SetFloat("_NearDimAlpha", f.nearDim);
        focusMaterial.SetFloat("_FarDimAlpha", f.farDim);
        focusMaterial.SetFloat("_DimRadius", Mathf.Max(0.001f, f.dimRadius));

        focusMaterial.SetVector("_PlayerCenter", new Vector4(f.playerCenter.x, f.playerCenter.y, 0f, 0f));
        focusMaterial.SetFloat("_PlayerRadius", f.playerRadius);
        focusMaterial.SetFloat("_PlayerStrength", f.playerStrength);
        focusMaterial.SetFloat("_PlayerScaleX", Mathf.Max(1f, f.playerScaleX));
        focusMaterial.SetFloat("_PlayerScaleY", Mathf.Max(1f, f.playerScaleY));

        focusMaterial.SetVector("_PresenterCenter", new Vector4(f.presenterCenter.x, f.presenterCenter.y, 0f, 0f));
        focusMaterial.SetFloat("_PresenterRadius", f.presenterRadius);
        focusMaterial.SetFloat("_PresenterStrength", f.presenterStrength);

        focusMaterial.SetVector("_ScreenRect", f.screenRect);
        focusMaterial.SetFloat("_ScreenStrength", f.screenStrength);

        focusMaterial.SetVector("_ItemCenter", new Vector4(f.itemCenter.x, f.itemCenter.y, 0f, 0f));
        focusMaterial.SetFloat("_ItemRadius", f.itemRadius);
        focusMaterial.SetFloat("_ItemStrength", f.itemStrength);
        focusMaterial.SetFloat("_ItemFeather", Mathf.Max(0.0001f, f.itemFeather));

        focusMaterial.SetFloat("_CharacterVerticalRatio", Mathf.Clamp(f.characterVerticalRatio, 0.2f, 1f));
        focusMaterial.SetFloat("_CharacterLowerOffset", Mathf.Clamp01(f.characterLowerOffset));
        focusMaterial.SetFloat("_CircleFeather", Mathf.Max(0.0001f, f.characterFeather));
        focusMaterial.SetFloat("_RectFeather", Mathf.Max(0.0001f, f.rectFeather));

        if (focusImage != null) focusImage.enabled = true;
    }

    private Vector3 ResolveCombatFocusPosition(Vector3 target, float dt)
    {
        if (!combatFocusInitialized)
        {
            combatFocusWorld = target;
            combatFocusInitialized = true;
            return combatFocusWorld;
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, combatFocusFollowSharpness) * Mathf.Max(0f, dt));
        Vector3 next = Vector3.Lerp(combatFocusWorld, target, t);
        Vector3 lag = next - target;
        float maxLag = Mathf.Max(0f, combatFocusMaxLagWorld);

        if (maxLag > 0f && lag.magnitude > maxLag)
            next = target + lag.normalized * maxLag;

        return next;
    }

    private Vector3 ResolvePlayerVisualCenter()
    {
        if (player == null) return Vector3.zero;

        SpriteRenderer[] renderers = player.GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer best = null;
        float bestArea = -1f;

        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer == null ||
                renderer.name == BattleCharacterLightVisual.KeyRendererName ||
                renderer.name == BattleCharacterLightVisual.PoolRendererName ||
                renderer.name == BattleCharacterLightVisual.GlowRendererName)
                continue;

            float area = Mathf.Abs(renderer.bounds.size.x * renderer.bounds.size.y);
            if (area > bestArea) { bestArea = area; best = renderer; }
        }

        return best != null ? best.bounds.center : player.transform.position;
    }

    private void ResolvePlayerBeam()
    {
        if (player == null) return;

        if (playerBeamTransform == null)
        {
            playerBeamTransform = FindRecursive(player.transform, BattleCharacterLightVisual.KeyRendererName);
            playerBeamRenderer = playerBeamTransform != null ? playerBeamTransform.GetComponent<SpriteRenderer>() : null;
        }

        if (playerGlowRenderer == null)
        {
            Transform glow = FindRecursive(player.transform, BattleCharacterLightVisual.GlowRendererName);
            playerGlowRenderer = glow != null ? glow.GetComponent<SpriteRenderer>() : null;
        }
    }

    private void ClearCombatBeamProperties()
    {
        if (playerBeamRenderer != null)
        {
            beamProperties ??= new MaterialPropertyBlock();
            playerBeamRenderer.GetPropertyBlock(beamProperties);
            beamProperties.SetFloat("_BeamBottomWidthScale", 1f);
            beamProperties.SetFloat("_BeamOpacityScale", 1f);
            playerBeamRenderer.SetPropertyBlock(beamProperties);
        }

        if (playerGlowRenderer != null)
        {
            glowProperties ??= new MaterialPropertyBlock();
            playerGlowRenderer.GetPropertyBlock(glowProperties);
            glowProperties.SetFloat("_HorizontalBias", 0f);
            glowProperties.SetFloat("_DirectionalAmount", 0.55f);
            playerGlowRenderer.SetPropertyBlock(glowProperties);
        }
    }

    private void ResetCombatMotion()
    {
        hasMotionSample = false;
        lastPlayerVelocity = Vector2.zero;
        currentGroundDirection = Vector2.down;
        currentMotion01 = 0f;

        if (player != null)
        {
            Vector2 p = player.transform.position;
            lastPlayerPosition = virtualAimWorld = virtualSourceWorld = p;
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null) runManager = FindFirstObjectByType<BattleRunManager>();
        if (stageFlow == null)
            stageFlow = BattleStageTransitionController.Instance != null
                ? BattleStageTransitionController.Instance
                : FindFirstObjectByType<BattleStageTransitionController>();
        if (showWorldSet == null) showWorldSet = FindFirstObjectByType<BattleShowWorldSetController>();
        if (stageLighting == null)
            stageLighting = BattlePlayerStageLightingController.Instance != null
                ? BattlePlayerStageLightingController.Instance
                : FindFirstObjectByType<BattlePlayerStageLightingController>(FindObjectsInactive.Include);

        PlayerController resolved = player != null ? player : FindFirstObjectByType<PlayerController>();
        if (resolved != player)
        {
            player = resolved;
            playerBeamTransform = null;
            playerBeamRenderer = null;
            playerGlowRenderer = null;
            beamProperties = null;
            glowProperties = null;
            ResetCombatMotion();
        }
    }

    private void PushArtistSettings()
    {
        RefreshVisuals();
        if (lightVisuals == null) return;

        bool narrowAtTop = beamShapeDirection == BeamShapeDirection.NarrowAtTop;
        foreach (BattleCharacterLightVisual visual in lightVisuals)
            visual?.ApplyBeamArtistSettings(narrowAtTop, beamRotationDegrees, beamLocalOffset);
    }

    public static void ApplyCurrentSettingsTo(BattleCharacterLightVisual visual)
    {
        if (visual == null || activeInstance == null || !activeInstance.isActiveAndEnabled) return;

        visual.ApplyBeamArtistSettings(
            activeInstance.beamShapeDirection == BeamShapeDirection.NarrowAtTop,
            activeInstance.beamRotationDegrees,
            activeInstance.beamLocalOffset);
    }

    private void RefreshVisuals()
    {
        lightVisuals = FindObjectsByType<BattleCharacterLightVisual>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    private void RefreshVisualsIfNeeded()
    {
        if (Time.unscaledTime < nextVisualRefresh && lightVisuals != null && lightVisuals.Length > 0) return;
        RefreshVisuals();
        nextVisualRefresh = Time.unscaledTime + Mathf.Max(0.1f, visualRefreshInterval);
    }

    private void EnsureFocusOverlay()
    {
        if (focusCanvas == null)
        {
            GameObject canvasObject = new("BattleSpotlightFocusCanvas");
            canvasObject.transform.SetParent(transform, false);

            focusCanvas = canvasObject.AddComponent<Canvas>();
            focusCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            focusCanvas.overrideSorting = true;
            focusCanvas.sortingOrder = FocusSortingOrder;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject imageObject = new("BattleSpotlightFocusMask", typeof(RectTransform));
            imageObject.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            focusImage = imageObject.AddComponent<Image>();
            focusImage.raycastTarget = false;
            focusImage.color = Color.white;
        }

        if (focusMaterial == null)
        {
            Shader shader = Shader.Find(FocusShaderName);
            if (shader == null) shader = Resources.Load<Shader>("BattleShowFocusMask");

            if (shader != null)
            {
                focusMaterial = new Material(shader)
                {
                    name = "BattleSpotlightFocusMask_Runtime",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        if (focusImage != null) focusImage.material = focusMaterial;
    }

    private bool TryProjectScreenRect(Camera camera, out Vector4 rectUv)
    {
        rectUv = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
        RectTransform rect = showWorldSet != null ? showWorldSet.MountedTvRect : null;
        if (camera == null || rect == null || !rect.gameObject.activeInHierarchy) return false;

        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        float width = Mathf.Max(1f, Screen.width), height = Mathf.Max(1f, Screen.height);

        foreach (Vector3 corner in corners)
        {
            Vector3 screen = camera.WorldToScreenPoint(corner);
            if (screen.z <= 0f) return false;

            float x = screen.x / width, y = screen.y / height;
            minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
            maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
        }

        rectUv = new Vector4(
            Mathf.Clamp01(minX - screenRectPadding),
            Mathf.Clamp01(minY - screenRectPadding),
            Mathf.Clamp01(maxX + screenRectPadding),
            Mathf.Clamp01(maxY + screenRectPadding));

        return rectUv.z > rectUv.x && rectUv.w > rectUv.y;
    }

    private static bool TryProjectWorldPoint(
        Camera camera, Vector3 worldPoint, float radiusWorld,
        out Vector2 centerUv, out float radiusUv)
    {
        centerUv = new Vector2(0.5f, 0.5f);
        radiusUv = 0f;
        if (camera == null) return false;

        Vector3 center = camera.WorldToScreenPoint(worldPoint);
        if (center.z <= 0f) return false;

        Vector3 edge = camera.WorldToScreenPoint(
            worldPoint + camera.transform.right * Mathf.Max(0.01f, radiusWorld));

        float width = Mathf.Max(1f, Screen.width), height = Mathf.Max(1f, Screen.height);
        centerUv = new Vector2(Mathf.Clamp01(center.x / width), Mathf.Clamp01(center.y / height));
        radiusUv = Mathf.Max(0.0001f, Mathf.Abs(edge.x - center.x) / height);
        return true;
    }

    private static bool TryProjectCharacter(
        Camera camera, Transform target, float radiusWorld,
        out Vector2 centerUv, out float radiusUv)
    {
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            centerUv = new Vector2(0.5f, 0.5f);
            radiusUv = 0f;
            return false;
        }

        return TryProjectWorldPoint(camera, target.position, radiusWorld, out centerUv, out radiusUv);
    }

    private static Vector2 ResolveScreenDirection(Camera camera, Vector3 centerWorld, Vector2 directionWorld)
    {
        if (camera == null || directionWorld.sqrMagnitude < 0.0001f) return Vector2.right;

        Vector3 a = camera.WorldToScreenPoint(centerWorld);
        Vector3 b = camera.WorldToScreenPoint(centerWorld + (Vector3)directionWorld.normalized);
        Vector2 delta = new(b.x - a.x, b.y - a.y);
        return delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.right;
    }

    private void HideFocusIfZero()
    {
        if (focusBlend > 0.001f)
        {
            if (focusMaterial != null) focusMaterial.SetFloat("_Presentation", focusBlend);
            if (focusImage != null) focusImage.enabled = true;
            return;
        }

        HideFocusImmediate();
    }

    private void HideFocusImmediate()
    {
        focusBlend = 0f;
        hasLastAppliedFocusFrame = false;
        hasTransitionFocusSource = false;
        if (focusMaterial != null)
        {
            focusMaterial.SetFloat("_Presentation", 0f);
            focusMaterial.SetFloat("_PlayerStrength", 0f);
            focusMaterial.SetFloat("_PresenterStrength", 0f);
            focusMaterial.SetFloat("_ScreenStrength", 0f);
            focusMaterial.SetFloat("_ItemStrength", 0f);
            focusMaterial.SetFloat("_PlayerScaleX", 1f);
            focusMaterial.SetFloat("_PlayerScaleY", 1f);
        }
        if (focusImage != null) focusImage.enabled = false;
    }

    private void ApplyModeTransitionToBeams()
    {
        if (!modeTransitionActive || mode == SpotlightMode.None)
            return;

        RefreshVisualsIfNeeded();

        float t = GetModeTransition01();
        float flicker = EvaluateStageLightFlicker(t);
        float scaleEnvelope = EvaluateBeamTransitionScale(t, flicker);
        float alphaEnvelope = EvaluateBeamTransitionAlpha(t, flicker);

        if (mode == SpotlightMode.Combat)
        {
            ResolvePlayerBeam();
            ApplyBeamTransitionEnvelope(
                playerBeamTransform,
                playerBeamRenderer,
                scaleEnvelope,
                alphaEnvelope);
            return;
        }

        if (lightVisuals == null)
            return;

        foreach (BattleCharacterLightVisual visual in lightVisuals)
        {
            if (visual == null ||
                !visual.isActiveAndEnabled ||
                visual.CurrentSpotlightStrength <= 0.01f)
                continue;

            Transform beam = visual.transform.Find(BattleCharacterLightVisual.KeyRendererName);
            SpriteRenderer renderer = beam != null ? beam.GetComponent<SpriteRenderer>() : null;

            ApplyBeamTransitionEnvelope(
                beam,
                renderer,
                scaleEnvelope,
                alphaEnvelope);
        }
    }

    private static void ApplyBeamTransitionEnvelope(
        Transform beam,
        SpriteRenderer renderer,
        float scaleEnvelope,
        float alphaEnvelope)
    {
        if (beam == null || renderer == null || !renderer.enabled)
            return;

        Vector3 scale = beam.localScale;
        beam.localScale = new Vector3(
            scale.x * scaleEnvelope,
            scale.y * scaleEnvelope,
            scale.z);

        Color color = renderer.color;
        color.a *= alphaEnvelope;
        renderer.color = color;
    }

    private float GetModeTransition01()
    {
        if (!modeTransitionActive || modeTransitionStartedAt < 0f)
            return 1f;

        float duration = Mathf.Max(0.08f, modeTransitionDuration);
        return Mathf.Clamp01(
            (Time.unscaledTime - modeTransitionStartedAt) /
            duration);
    }

    private void FinishModeTransitionIfSettled()
    {
        if (!modeTransitionActive || GetModeTransition01() < 1f)
            return;

        modeTransitionActive = false;
        modeTransitionStartedAt = -1f;
        hasTransitionFocusSource = false;
    }

    private float EvaluateStageLightFlicker(float t)
    {
        if (!useStageLightFlicker || t >= 0.68f)
            return 1f;

        float p1 = SmoothPulse(t, 0.13f, 0.060f) * 1.00f;
        float p2 = SmoothPulse(t, 0.31f, 0.052f) * 0.72f;
        float p3 = SmoothPulse(t, 0.50f, 0.046f) * 0.44f;

        float dip = Mathf.Max(p1, Mathf.Max(p2, p3));
        return Mathf.Clamp01(
            1f -
            Mathf.Clamp01(stageFlickerStrength) *
            dip);
    }

    private float EvaluateBeamTransitionScale(float t, float flicker)
    {
        float ease = Smooth01(t);
        float baseScale = Mathf.Lerp(
            Mathf.Clamp(transitionStartScale, 0.65f, 1f),
            1f,
            ease);

        float overshoot =
            Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)) *
            Mathf.Max(0f, transitionScaleOvershoot) *
            (1f - 0.25f * t);

        float flickerCompression =
            (1f - flicker) * 0.025f;

        return Mathf.Max(
            0.5f,
            baseScale +
            overshoot -
            flickerCompression);
    }

    private float EvaluateBeamTransitionAlpha(float t, float flicker)
    {
        float baseAlpha = Mathf.Lerp(
            Mathf.Clamp01(transitionStartAlpha),
            1f,
            Smooth01(t));

        return Mathf.Clamp01(
            baseAlpha *
            flicker);
    }

    private float EvaluateFocusTransitionScale(float t, float flicker)
    {
        float ease = Smooth01(t);
        float baseScale = Mathf.Lerp(
            Mathf.Clamp(transitionStartScale, 0.65f, 1f),
            1f,
            ease);

        float overshoot =
            Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)) *
            Mathf.Max(0f, focusTransitionOvershoot);

        float flickerCompression =
            (1f - flicker) * 0.018f;

        return Mathf.Max(
            0.55f,
            baseScale +
            overshoot -
            flickerCompression);
    }

    private static FocusFrame LerpFocusFrame(
        FocusFrame from,
        FocusFrame to,
        float t)
    {
        return new FocusFrame
        {
            dimCenter = Vector2.LerpUnclamped(from.dimCenter, to.dimCenter, t),
            nearDim = Mathf.LerpUnclamped(from.nearDim, to.nearDim, t),
            farDim = Mathf.LerpUnclamped(from.farDim, to.farDim, t),
            dimRadius = Mathf.LerpUnclamped(from.dimRadius, to.dimRadius, t),

            playerCenter = Vector2.LerpUnclamped(from.playerCenter, to.playerCenter, t),
            playerRadius = Mathf.LerpUnclamped(from.playerRadius, to.playerRadius, t),
            playerStrength = Mathf.LerpUnclamped(from.playerStrength, to.playerStrength, t),
            playerScaleX = Mathf.LerpUnclamped(from.playerScaleX, to.playerScaleX, t),
            playerScaleY = Mathf.LerpUnclamped(from.playerScaleY, to.playerScaleY, t),

            presenterCenter = Vector2.LerpUnclamped(from.presenterCenter, to.presenterCenter, t),
            presenterRadius = Mathf.LerpUnclamped(from.presenterRadius, to.presenterRadius, t),
            presenterStrength = Mathf.LerpUnclamped(from.presenterStrength, to.presenterStrength, t),

            screenRect = Vector4.LerpUnclamped(from.screenRect, to.screenRect, t),
            screenStrength = Mathf.LerpUnclamped(from.screenStrength, to.screenStrength, t),

            itemCenter = Vector2.LerpUnclamped(from.itemCenter, to.itemCenter, t),
            itemRadius = Mathf.LerpUnclamped(from.itemRadius, to.itemRadius, t),
            itemStrength = Mathf.LerpUnclamped(from.itemStrength, to.itemStrength, t),
            itemFeather = Mathf.LerpUnclamped(from.itemFeather, to.itemFeather, t),

            characterVerticalRatio = Mathf.LerpUnclamped(
                from.characterVerticalRatio,
                to.characterVerticalRatio,
                t),
            characterLowerOffset = Mathf.LerpUnclamped(
                from.characterLowerOffset,
                to.characterLowerOffset,
                t),
            characterFeather = Mathf.LerpUnclamped(
                from.characterFeather,
                to.characterFeather,
                t),
            rectFeather = Mathf.LerpUnclamped(from.rectFeather, to.rectFeather, t)
        };
    }

    private static float SmoothPulse(float t, float center, float halfWidth)
    {
        float distance = Mathf.Abs(t - center);
        float normalized = 1f - distance / Mathf.Max(0.0001f, halfWidth);
        return Smooth01(Mathf.Clamp01(normalized));
    }

    private static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static float Damp01(float current, float target, float sharpness, float dt)
    {
        float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, sharpness) * Mathf.Max(0f, dt));
        float value = Mathf.Lerp(current, Mathf.Clamp01(target), t);
        if (Mathf.Abs(value - target) < 0.001f) value = target;
        return Mathf.Clamp01(value);
    }

    private static float EvaluateIdleBreath(float time, float cycles, float phase)
    {
        float omega = Mathf.Max(0.05f, cycles) * Mathf.PI * 2f;
        return Mathf.Clamp(
            Mathf.Sin(time * omega + phase) * 0.74f +
            Mathf.Sin(time * omega * 0.43f + phase * 1.67f) * 0.26f,
            -1f, 1f);
    }

    private static Transform FindRecursive(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName)) return null;
        if (root.name == targetName) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindRecursive(root.GetChild(i), targetName);
            if (found != null) return found;
        }

        return null;
    }

    private static float NormalizeDegrees(float degrees) =>
        Mathf.Repeat(degrees + 180f, 360f) - 180f;
}

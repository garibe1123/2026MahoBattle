using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Single owner for every gameplay spotlight presentation.
///
/// One component / one runtime focus-mask object drives:
/// - Combat: player follow lag + scale-only pseudo-3D beam response.
/// - Reward: hovered-item focus + subtle spotlight breathing.
/// - Map: player/presenter/screen focus + slower breathing.
///
/// Lens vignette is intentionally NOT owned here.
/// </summary>
[DefaultExecutionOrder(31900)]
[DisallowMultipleComponent]
public sealed class BattleSpotlightController : MonoBehaviour
{
    private const string FocusShaderName = "UI/BattleShowFocusMask";
    private const int FocusSortingOrder = 450;

    public enum BeamShapeDirection
    {
        NarrowAtTop,
        NarrowAtBottom
    }

    private enum SpotlightMode
    {
        None,
        Combat,
        Reward,
        Map
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

    [Header("Combat — Focus Mask")]
    [SerializeField, Range(0f, 1f)] private float combatDimAlpha = 0.24f;
    [SerializeField, Min(0.1f)] private float combatFocusRadiusWorld = 1.78f;
    [SerializeField, Range(0.55f, 1f)] private float combatVerticalRatio = 0.72f;
    [SerializeField, Range(0.001f, 0.10f)] private float combatFeather = 0.040f;
    [SerializeField, Min(0.1f)] private float combatFocusFollowSharpness = 5.4f;
    [SerializeField, Min(0f)] private float combatFocusMaxLagWorld = 0.42f;
    [SerializeField, Range(0f, 0.45f)] private float combatFocusScaleResponse = 0.28f;

    [Header("Combat — Scale-only Beam Motion")]
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

    [Header("Reward — Focus")]
    [SerializeField, Range(0f, 1f)] private float rewardNearDimAlpha = 0.62f;
    [SerializeField, Range(0f, 1f)] private float rewardFarDimAlpha = 0.96f;
    [SerializeField, Range(0.05f, 1.5f)] private float rewardDimRadius = 0.58f;
    [SerializeField, Min(0.1f)] private float rewardItemRadiusWorld = 0.92f;
    [SerializeField, Range(0.001f, 0.08f)] private float rewardItemFeather = 0.020f;

    [Header("Reward / Map — Character Focus")]
    [SerializeField, Min(0.1f)] private float showPlayerRadiusWorld = 1.48f;
    [SerializeField, Min(0.1f)] private float showPresenterRadiusWorld = 1.92f;
    [SerializeField, Range(0.2f, 1f)] private float showCharacterVerticalRatio = 0.86f;
    [SerializeField, Range(0f, 1f)] private float showCharacterLowerOffset = 0.02f;
    [SerializeField, Range(0.001f, 0.08f)] private float showCharacterFeather = 0.018f;
    [SerializeField, Range(0.0001f, 0.04f)] private float screenRectFeather = 0.0035f;
    [SerializeField, Range(-0.05f, 0.05f)] private float screenRectPadding = 0.002f;

    [Header("Reward / Map — Idle Breath")]
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

    private SpotlightMode currentMode;

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

    private Vector2 lastPlayerPosition;
    private Vector2 lastPlayerVelocity;
    private Vector2 virtualAimWorld;
    private Vector2 virtualSourceWorld;
    private Vector2 currentGroundDirection = Vector2.down;
    private float currentMotion01;
    private bool hasMotionSample;

    private Vector3 combatFocusWorld;
    private bool combatFocusInitialized;
    private float focusBlend;

    public static BattleSpotlightController Instance => activeInstance;

    public BeamShapeDirection Direction
    {
        get => beamShapeDirection;
        set
        {
            if (beamShapeDirection == value)
                return;

            beamShapeDirection = value;
            RefreshVisuals();
            ApplyArtistSettingsToAll();
        }
    }

    public float RotationDegrees
    {
        get => beamRotationDegrees;
        set
        {
            float normalized = NormalizeDegrees(value);
            if (Mathf.Approximately(beamRotationDegrees, normalized))
                return;

            beamRotationDegrees = normalized;
            RefreshVisuals();
            ApplyArtistSettingsToAll();
        }
    }

    public Vector2 LocalOffset
    {
        get => beamLocalOffset;
        set
        {
            if (beamLocalOffset == value)
                return;

            beamLocalOffset = value;
            RefreshVisuals();
            ApplyArtistSettingsToAll();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallRuntimeFallback()
    {
        if (FindFirstObjectByType<BattleSpotlightController>(FindObjectsInactive.Include) != null)
            return;

        BattleSceneManager manager =
            FindFirstObjectByType<BattleSceneManager>(FindObjectsInactive.Include);

        if (manager != null)
            manager.gameObject.AddComponent<BattleSpotlightController>();
    }

    private void Awake()
    {
        if (activeInstance != null && activeInstance != this)
        {
            Destroy(this);
            return;
        }

        activeInstance = this;
        ResolveReferences();
        EnsureFocusOverlay();
        RefreshVisuals();
        ApplyArtistSettingsToAll();
        ResetCombatMotion();
        ApplyFocusHiddenImmediate();
    }

    private void OnEnable()
    {
        activeInstance = this;
        ResolveReferences();
        EnsureFocusOverlay();
        RefreshVisuals();
        ApplyArtistSettingsToAll();
    }

    private void OnDisable()
    {
        ResetCombatMotion();
        ClearCombatBeamProperties();
        ApplyFocusHiddenImmediate();

        if (activeInstance == this)
            activeInstance = null;
    }

    private void OnDestroy()
    {
        if (focusMaterial != null)
            Destroy(focusMaterial);

        if (activeInstance == this)
            activeInstance = null;
    }

    private void OnValidate()
    {
        beamRotationDegrees = NormalizeDegrees(beamRotationDegrees);

        if (!Application.isPlaying || !isActiveAndEnabled)
            return;

        RefreshVisuals();
        ApplyArtistSettingsToAll();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        EnsureFocusOverlay();

        SpotlightMode nextMode = ResolveMode();
        if (nextMode != currentMode)
            HandleModeChanged(nextMode);

        float dt = Time.unscaledDeltaTime;

        switch (currentMode)
        {
            case SpotlightMode.Combat:
                UpdateCombat(dt);
                break;

            case SpotlightMode.Reward:
                UpdateShowMode(true, dt);
                break;

            case SpotlightMode.Map:
                UpdateShowMode(false, dt);
                break;

            default:
                UpdateInactive(dt);
                break;
        }
    }

    private SpotlightMode ResolveMode()
    {
        if (runManager == null || !runManager.RunActive)
            return SpotlightMode.None;

        bool combat =
            (stageFlow != null && stageFlow.IsCombatPhase) ||
            runManager.State == BattleRunState.Combat;

        if (combat)
            return SpotlightMode.Combat;

        if (runManager.State == BattleRunState.Reward)
            return SpotlightMode.Reward;

        if (runManager.State == BattleRunState.SelectingNode)
            return SpotlightMode.Map;

        return SpotlightMode.None;
    }

    private void HandleModeChanged(SpotlightMode next)
    {
        if (currentMode == SpotlightMode.Combat)
        {
            ResetCombatMotion();
            ClearCombatBeamProperties();
        }

        currentMode = next;
        combatFocusInitialized = false;

        if (currentMode != SpotlightMode.Combat)
            ClearCombatBeamProperties();

        RefreshVisuals();
        ApplyArtistSettingsToAll();
    }

    private void UpdateCombat(float dt)
    {
        bool validPlayer =
            player != null &&
            player.IsAlive &&
            player.gameObject.activeInHierarchy;

        focusBlend = Damp01(
            focusBlend,
            validPlayer ? 1f : 0f,
            combatFadeSharpness,
            dt);

        if (!validPlayer)
        {
            ApplyFocusHiddenIfZero();
            return;
        }

        ResolvePlayerBeam();

        if (playerBeamTransform != null &&
            playerBeamRenderer != null &&
            playerBeamRenderer.sprite != null)
        {
            UpdateCombatMotion();
            ApplyCombatBeam();
        }

        Vector3 targetFocus = ResolvePlayerVisualCenter();
        combatFocusWorld = ResolveCombatFocusPosition(
            targetFocus,
            dt);

        ApplyCombatFocus();
    }

    private void UpdateShowMode(bool reward, float dt)
    {
        ResetCombatMotion();
        ClearCombatBeamProperties();

        bool showReady =
            showWorldSet != null &&
            showWorldSet.IsShowActive;

        float sharpness = showFadeSharpness;
        focusBlend = Damp01(
            focusBlend,
            showReady ? 1f : 0f,
            sharpness,
            dt);

        if (useShowIdle && showReady)
            ApplyShowIdle(reward);

        ApplyShowFocus(reward);
    }

    private void UpdateInactive(float dt)
    {
        ResetCombatMotion();
        ClearCombatBeamProperties();

        focusBlend = Damp01(
            focusBlend,
            0f,
            showFadeSharpness,
            dt);

        ApplyFocusHiddenIfZero();
    }

    private void UpdateCombatMotion()
    {
        float dt =
            Mathf.Min(
                0.05f,
                Mathf.Max(
                    0.001f,
                    Time.unscaledDeltaTime));

        Vector2 playerPosition = player.transform.position;

        if (!hasMotionSample ||
            Vector2.Distance(playerPosition, lastPlayerPosition) > 3f)
        {
            lastPlayerPosition = playerPosition;
            lastPlayerVelocity = Vector2.zero;
            virtualAimWorld = playerPosition;
            virtualSourceWorld = playerPosition;
            currentGroundDirection = Vector2.down;
            currentMotion01 = 0f;
            hasMotionSample = true;
            return;
        }

        Vector2 velocity =
            (playerPosition - lastPlayerPosition) / dt;

        Vector2 acceleration =
            Vector2.ClampMagnitude(
                (velocity - lastPlayerVelocity) / dt,
                30f);

        lastPlayerPosition = playerPosition;
        lastPlayerVelocity = velocity;

        float aimT =
            1f -
            Mathf.Exp(
                -Mathf.Max(0.1f, aimFollowSharpness) *
                dt);

        virtualAimWorld =
            Vector2.Lerp(
                virtualAimWorld,
                playerPosition,
                aimT);

        Vector2 sourceTrail =
            -velocity *
            Mathf.Max(
                0f,
                sourceVelocityTrailSeconds);

        sourceTrail +=
            -acceleration *
            Mathf.Max(
                0f,
                sourceAccelerationTrail);

        sourceTrail =
            Vector2.ClampMagnitude(
                sourceTrail,
                Mathf.Max(
                    0f,
                    maxSourceLagDistance));

        Vector2 desiredSource =
            playerPosition +
            sourceTrail;

        float sourceT =
            1f -
            Mathf.Exp(
                -Mathf.Max(0.1f, sourceFollowSharpness) *
                dt);

        virtualSourceWorld =
            Vector2.Lerp(
                virtualSourceWorld,
                desiredSource,
                sourceT);

        Vector2 planar =
            virtualAimWorld -
            virtualSourceWorld;

        float planarDistance = planar.magnitude;

        if (planarDistance > 0.0001f)
            currentGroundDirection = planar / planarDistance;

        float responseAngle =
            Mathf.Atan2(
                planarDistance,
                Mathf.Max(
                    0.5f,
                    virtualLightHeight));

        float fullResponse =
            Mathf.Deg2Rad *
            Mathf.Clamp(
                fullScaleResponseAngleDegrees,
                5f,
                45f);

        currentMotion01 =
            Mathf.Clamp01(
                responseAngle /
                Mathf.Max(
                    0.001f,
                    fullResponse));
    }

    private void ApplyCombatBeam()
    {
        Quaternion authoredRotation =
            playerBeamTransform.rotation;

        Vector3 authoredCenter =
            playerBeamTransform.position;

        Vector3 authoredScale =
            playerBeamTransform.localScale;

        float authoredWorldHeight =
            playerBeamRenderer.sprite.bounds.size.y *
            Mathf.Abs(
                playerBeamTransform.lossyScale.y);

        if (authoredWorldHeight <= 0.0001f)
            return;

        float response =
            Mathf.Sqrt(
                Mathf.Clamp01(
                    currentMotion01));

        float lengthScale =
            1f +
            Mathf.Max(
                0f,
                maxBeamLengthBoost) *
            response;

        float widthScale =
            1f +
            Mathf.Max(
                0f,
                maxBeamWidthBoost) *
            response;

        Vector3 sourcePoint =
            authoredCenter +
            authoredRotation *
            (Vector3.up *
             (authoredWorldHeight * 0.5f));

        Vector2 sourceLag =
            virtualSourceWorld -
            virtualAimWorld;

        Vector2 sourceOffset =
            Vector2.ClampMagnitude(
                sourceLag *
                Mathf.Clamp01(
                    beamSourceLagVisualScale),
                Mathf.Max(
                    0f,
                    maxBeamSourceVisualOffset));

        sourcePoint +=
            new Vector3(
                sourceOffset.x,
                sourceOffset.y * 0.45f,
                0f);

        playerBeamTransform.localScale =
            new Vector3(
                authoredScale.x * widthScale,
                authoredScale.y * lengthScale,
                authoredScale.z);

        float halfHeight =
            authoredWorldHeight *
            lengthScale *
            0.5f;

        playerBeamTransform.position =
            sourcePoint +
            authoredRotation *
            (Vector3.down * halfHeight);

        playerBeamTransform.rotation =
            authoredRotation;

        beamProperties ??= new MaterialPropertyBlock();
        playerBeamRenderer.GetPropertyBlock(beamProperties);

        beamProperties.SetFloat(
            "_BeamBottomWidthScale",
            1f +
            Mathf.Max(
                0f,
                maxBeamBottomWidthBoost) *
            response);

        beamProperties.SetFloat(
            "_BeamOpacityScale",
            Mathf.Clamp01(
                1f -
                Mathf.Max(
                    0f,
                    motionOpacityLoss) *
                response));

        playerBeamRenderer.SetPropertyBlock(
            beamProperties);

        ApplyDirectionalTopLight(response);
    }

    private void ApplyDirectionalTopLight(float response)
    {
        if (playerGlowRenderer == null)
            return;

        glowProperties ??= new MaterialPropertyBlock();
        playerGlowRenderer.GetPropertyBlock(glowProperties);

        glowProperties.SetFloat(
            "_HorizontalBias",
            Mathf.Clamp(
                -currentGroundDirection.x *
                response,
                -1f,
                1f));

        glowProperties.SetFloat(
            "_DirectionalAmount",
            0.60f);

        playerGlowRenderer.SetPropertyBlock(
            glowProperties);
    }

    private void ApplyShowIdle(bool reward)
    {
        if (Time.unscaledTime >= nextVisualRefresh ||
            lightVisuals == null ||
            lightVisuals.Length == 0)
        {
            RefreshVisuals();
            nextVisualRefresh =
                Time.unscaledTime +
                Mathf.Max(
                    0.1f,
                    visualRefreshInterval);
        }

        if (lightVisuals == null)
            return;

        float cycles =
            reward
                ? rewardIdleCyclesPerSecond
                : mapIdleCyclesPerSecond;

        float widthAmplitude =
            reward
                ? rewardBeamWidthAmplitude
                : mapBeamWidthAmplitude;

        float lengthAmplitude =
            reward
                ? rewardBeamLengthAmplitude
                : mapBeamLengthAmplitude;

        float alphaAmplitude =
            reward
                ? rewardBeamAlphaAmplitude
                : mapBeamAlphaAmplitude;

        float time =
            Time.unscaledTime *
            Mathf.Max(
                0.05f,
                cycles) *
            Mathf.PI *
            2f;

        for (int i = 0; i < lightVisuals.Length; i++)
        {
            BattleCharacterLightVisual visual = lightVisuals[i];

            if (visual == null ||
                !visual.isActiveAndEnabled ||
                visual.CurrentSpotlightStrength <= 0.01f)
            {
                continue;
            }

            Transform beam =
                visual.transform.Find(
                    BattleCharacterLightVisual.KeyRendererName);

            if (beam == null)
                continue;

            SpriteRenderer renderer =
                beam.GetComponent<SpriteRenderer>();

            if (renderer == null || !renderer.enabled)
                continue;

            float phase =
                Mathf.Abs(
                    visual.GetInstanceID() % 997) *
                0.0137f;

            float primary =
                Mathf.Sin(
                    time +
                    phase);

            float secondary =
                Mathf.Sin(
                    time * 0.47f +
                    phase * 1.71f);

            float breath =
                Mathf.Clamp(
                    primary * 0.72f +
                    secondary * 0.28f,
                    -1f,
                    1f);

            Vector3 baseScale = beam.localScale;

            beam.localScale =
                new Vector3(
                    baseScale.x *
                    (1f +
                     breath *
                     widthAmplitude),
                    baseScale.y *
                    (1f +
                     Mathf.Sin(
                         time +
                         phase +
                         0.65f) *
                     lengthAmplitude),
                    baseScale.z);

            Color color = renderer.color;
            color.a *=
                Mathf.Clamp(
                    1f +
                    breath *
                    alphaAmplitude,
                    0.80f,
                    1.15f);
            renderer.color = color;
        }
    }

    private void ApplyCombatFocus()
    {
        Camera camera = Camera.main;

        if (focusMaterial == null ||
            camera == null ||
            focusBlend <= 0.0001f)
        {
            ApplyFocusHiddenIfZero();
            return;
        }

        if (!TryProjectWorldPoint(
                camera,
                combatFocusWorld,
                combatFocusRadiusWorld,
                out Vector2 playerUv,
                out float playerRadiusUv))
        {
            ApplyFocusHiddenIfZero();
            return;
        }

        Vector2 screenDirection =
            ResolveScreenDirection(
                camera,
                combatFocusWorld,
                currentGroundDirection);

        float response =
            Mathf.Sqrt(
                Mathf.Clamp01(
                    currentMotion01));

        float scaleX =
            1f +
            response *
            (
                0.08f +
                combatFocusScaleResponse *
                Mathf.Abs(
                    screenDirection.x));

        float scaleY =
            1f +
            response *
            (
                0.04f +
                combatFocusScaleResponse *
                0.55f *
                Mathf.Abs(
                    screenDirection.y));

        focusMaterial.SetColor("_MaskColor", Color.black);
        focusMaterial.SetFloat("_Presentation", focusBlend);
        focusMaterial.SetVector(
            "_DimCenter",
            new Vector4(
                playerUv.x,
                playerUv.y,
                0f,
                0f));
        focusMaterial.SetFloat("_NearDimAlpha", combatDimAlpha);
        focusMaterial.SetFloat("_FarDimAlpha", combatDimAlpha);
        focusMaterial.SetFloat("_DimRadius", 1f);

        focusMaterial.SetVector(
            "_PlayerCenter",
            new Vector4(
                playerUv.x,
                playerUv.y,
                0f,
                0f));
        focusMaterial.SetFloat("_PlayerRadius", playerRadiusUv);
        focusMaterial.SetFloat("_PlayerStrength", 1f);
        focusMaterial.SetFloat("_PlayerScaleX", scaleX);
        focusMaterial.SetFloat("_PlayerScaleY", scaleY);

        focusMaterial.SetFloat("_PresenterStrength", 0f);
        focusMaterial.SetFloat("_ScreenStrength", 0f);
        focusMaterial.SetFloat("_ItemStrength", 0f);

        focusMaterial.SetFloat(
            "_CharacterVerticalRatio",
            combatVerticalRatio);
        focusMaterial.SetFloat(
            "_CharacterLowerOffset",
            0f);
        focusMaterial.SetFloat(
            "_CircleFeather",
            combatFeather);
        focusMaterial.SetFloat(
            "_RectFeather",
            screenRectFeather);

        if (focusImage != null)
            focusImage.enabled = true;
    }

    private void ApplyShowFocus(bool reward)
    {
        Camera camera = Camera.main;

        if (focusMaterial == null ||
            camera == null ||
            focusBlend <= 0.0001f)
        {
            ApplyFocusHiddenIfZero();
            return;
        }

        bool playerVisible =
            TryProjectCharacter(
                camera,
                player != null && player.IsAlive
                    ? player.transform
                    : null,
                ResolveShowPlayerRadius(),
                out Vector2 playerUv,
                out float playerRadiusUv);

        Transform presenter =
            showWorldSet != null
                ? showWorldSet.PresenterWorldTransform
                : null;

        bool presenterVisible =
            TryProjectCharacter(
                camera,
                presenter,
                showPresenterRadiusWorld,
                out Vector2 presenterUv,
                out float presenterRadiusUv);

        float idleWave =
            useShowIdle
                ? EvaluateIdleBreath(
                    Time.unscaledTime,
                    reward
                        ? rewardIdleCyclesPerSecond
                        : mapIdleCyclesPerSecond,
                    reward ? 0.37f : 1.11f)
                : 0f;

        float radiusPulse =
            reward
                ? rewardFocusRadiusPulse
                : mapFocusRadiusPulse;

        if (playerVisible)
            playerRadiusUv *= 1f + idleWave * radiusPulse;

        if (presenterVisible)
        {
            float presenterWave =
                useShowIdle
                    ? EvaluateIdleBreath(
                        Time.unscaledTime,
                        reward
                            ? rewardIdleCyclesPerSecond
                            : mapIdleCyclesPerSecond,
                        2.43f)
                    : 0f;

            presenterRadiusUv *=
                1f +
                presenterWave *
                radiusPulse;
        }

        Vector4 screenRect =
            new(
                0.5f,
                0.5f,
                0.5f,
                0.5f);

        bool screenVisible =
            !reward &&
            TryProjectScreenRect(
                camera,
                out screenRect);

        bool itemVisible = false;
        Vector2 itemUv = new(0.5f, 0.5f);
        float itemRadiusUv = 0f;

        if (reward &&
            showWorldSet != null &&
            showWorldSet.RewardHoveredIndex >= 0 &&
            showWorldSet.TryGetRewardShowcaseWorldPosition(
                showWorldSet.RewardHoveredIndex,
                out Vector3 itemWorld))
        {
            itemVisible =
                TryProjectWorldPoint(
                    camera,
                    itemWorld,
                    rewardItemRadiusWorld,
                    out itemUv,
                    out itemRadiusUv);

            if (itemVisible)
                itemRadiusUv *=
                    1f +
                    idleWave *
                    rewardFocusRadiusPulse;
        }

        float nearDim =
            reward
                ? rewardNearDimAlpha
                : stageLighting != null
                    ? stageLighting.UnifiedNearDimAlpha
                    : rewardNearDimAlpha;

        float farDim =
            reward
                ? rewardFarDimAlpha
                : stageLighting != null
                    ? stageLighting.UnifiedFarDimAlpha
                    : rewardFarDimAlpha;

        float dimRadius =
            reward
                ? rewardDimRadius
                : stageLighting != null
                    ? stageLighting.UnifiedDimFalloffRadius
                    : rewardDimRadius;

        float verticalRatio =
            !reward && stageLighting != null
                ? stageLighting.UnifiedCharacterVerticalRatio
                : showCharacterVerticalRatio;

        float lowerOffset =
            !reward && stageLighting != null
                ? stageLighting.UnifiedCharacterLowerOffset
                : showCharacterLowerOffset;

        float feather =
            !reward && stageLighting != null
                ? stageLighting.UnifiedCharacterFeather
                : showCharacterFeather;

        if (useShowIdle)
        {
            feather *=
                1f +
                idleWave *
                focusFeatherPulse;
        }

        focusMaterial.SetColor("_MaskColor", Color.black);
        focusMaterial.SetFloat("_Presentation", focusBlend);
        focusMaterial.SetVector(
            "_DimCenter",
            new Vector4(
                playerUv.x,
                playerUv.y,
                0f,
                0f));
        focusMaterial.SetFloat("_NearDimAlpha", nearDim);
        focusMaterial.SetFloat("_FarDimAlpha", farDim);
        focusMaterial.SetFloat("_DimRadius", dimRadius);

        focusMaterial.SetVector(
            "_PlayerCenter",
            new Vector4(
                playerUv.x,
                playerUv.y,
                0f,
                0f));
        focusMaterial.SetFloat("_PlayerRadius", playerRadiusUv);
        focusMaterial.SetFloat(
            "_PlayerStrength",
            playerVisible ? 1f : 0f);
        focusMaterial.SetFloat("_PlayerScaleX", 1f);
        focusMaterial.SetFloat("_PlayerScaleY", 1f);

        focusMaterial.SetVector(
            "_PresenterCenter",
            new Vector4(
                presenterUv.x,
                presenterUv.y,
                0f,
                0f));
        focusMaterial.SetFloat(
            "_PresenterRadius",
            presenterRadiusUv);
        focusMaterial.SetFloat(
            "_PresenterStrength",
            presenterVisible ? 1f : 0f);

        focusMaterial.SetVector("_ScreenRect", screenRect);
        focusMaterial.SetFloat(
            "_ScreenStrength",
            screenVisible ? 1f : 0f);

        focusMaterial.SetVector(
            "_ItemCenter",
            new Vector4(
                itemUv.x,
                itemUv.y,
                0f,
                0f));
        focusMaterial.SetFloat("_ItemRadius", itemRadiusUv);
        focusMaterial.SetFloat(
            "_ItemStrength",
            itemVisible ? 1f : 0f);

        float itemFeather =
            rewardItemFeather *
            (
                1f +
                idleWave *
                focusFeatherPulse);

        focusMaterial.SetFloat(
            "_ItemFeather",
            Mathf.Max(
                0.0001f,
                itemFeather));

        focusMaterial.SetFloat(
            "_CharacterVerticalRatio",
            verticalRatio);
        focusMaterial.SetFloat(
            "_CharacterLowerOffset",
            lowerOffset);
        focusMaterial.SetFloat(
            "_CircleFeather",
            feather);
        focusMaterial.SetFloat(
            "_RectFeather",
            screenRectFeather);

        if (focusImage != null)
            focusImage.enabled = true;
    }

    private float ResolveShowPlayerRadius()
    {
        if (currentMode == SpotlightMode.Map &&
            stageLighting != null)
        {
            return stageLighting.UnifiedPlayerFocusRadiusWorld;
        }

        return showPlayerRadiusWorld;
    }

    private Vector3 ResolveCombatFocusPosition(
        Vector3 target,
        float dt)
    {
        if (!combatFocusInitialized)
        {
            combatFocusWorld = target;
            combatFocusInitialized = true;
            return combatFocusWorld;
        }

        float t =
            1f -
            Mathf.Exp(
                -Mathf.Max(
                    0.1f,
                    combatFocusFollowSharpness) *
                Mathf.Max(
                    0f,
                    dt));

        Vector3 next =
            Vector3.Lerp(
                combatFocusWorld,
                target,
                t);

        Vector3 lag = next - target;
        float maxLag = Mathf.Max(
            0f,
            combatFocusMaxLagWorld);

        if (maxLag > 0f &&
            lag.magnitude > maxLag)
        {
            next =
                target +
                lag.normalized *
                maxLag;
        }

        return next;
    }

    private Vector3 ResolvePlayerVisualCenter()
    {
        if (player == null)
            return Vector3.zero;

        SpriteRenderer[] renderers =
            player.GetComponentsInChildren<SpriteRenderer>(true);

        SpriteRenderer best = null;
        float bestArea = -1f;

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];

            if (renderer == null ||
                renderer.name == BattleCharacterLightVisual.KeyRendererName ||
                renderer.name == BattleCharacterLightVisual.PoolRendererName ||
                renderer.name == BattleCharacterLightVisual.GlowRendererName)
            {
                continue;
            }

            float area =
                Mathf.Abs(
                    renderer.bounds.size.x *
                    renderer.bounds.size.y);

            if (area <= bestArea)
                continue;

            bestArea = area;
            best = renderer;
        }

        return best != null
            ? best.bounds.center
            : player.transform.position;
    }

    private void ResolvePlayerBeam()
    {
        if (player == null)
            return;

        if (playerBeamTransform == null)
        {
            playerBeamTransform =
                FindRecursive(
                    player.transform,
                    BattleCharacterLightVisual.KeyRendererName);

            playerBeamRenderer =
                playerBeamTransform != null
                    ? playerBeamTransform.GetComponent<SpriteRenderer>()
                    : null;
        }

        if (playerGlowRenderer == null)
        {
            Transform glow =
                FindRecursive(
                    player.transform,
                    BattleCharacterLightVisual.GlowRendererName);

            playerGlowRenderer =
                glow != null
                    ? glow.GetComponent<SpriteRenderer>()
                    : null;
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
            Vector2 position = player.transform.position;
            lastPlayerPosition = position;
            virtualAimWorld = position;
            virtualSourceWorld = position;
        }
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();

        if (stageFlow == null)
        {
            stageFlow =
                BattleStageTransitionController.Instance != null
                    ? BattleStageTransitionController.Instance
                    : FindFirstObjectByType<BattleStageTransitionController>();
        }

        if (showWorldSet == null)
            showWorldSet = FindFirstObjectByType<BattleShowWorldSetController>();

        if (stageLighting == null)
        {
            stageLighting =
                BattlePlayerStageLightingController.Instance != null
                    ? BattlePlayerStageLightingController.Instance
                    : FindFirstObjectByType<BattlePlayerStageLightingController>(
                        FindObjectsInactive.Include);
        }

        PlayerController resolvedPlayer =
            player != null
                ? player
                : FindFirstObjectByType<PlayerController>();

        if (resolvedPlayer != player)
        {
            player = resolvedPlayer;
            playerBeamTransform = null;
            playerBeamRenderer = null;
            playerGlowRenderer = null;
            beamProperties = null;
            glowProperties = null;
            ResetCombatMotion();
        }
    }

    private void RefreshVisuals()
    {
        lightVisuals =
            FindObjectsByType<BattleCharacterLightVisual>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
    }

    private void ApplyArtistSettingsToAll()
    {
        if (lightVisuals == null)
            return;

        bool narrowAtTop =
            beamShapeDirection ==
            BeamShapeDirection.NarrowAtTop;

        for (int i = 0; i < lightVisuals.Length; i++)
        {
            BattleCharacterLightVisual visual = lightVisuals[i];
            if (visual == null)
                continue;

            visual.ApplyBeamArtistSettings(
                narrowAtTop,
                beamRotationDegrees,
                beamLocalOffset);
        }
    }

    public static void ApplyCurrentSettingsTo(
        BattleCharacterLightVisual visual)
    {
        if (visual == null ||
            activeInstance == null ||
            !activeInstance.isActiveAndEnabled)
        {
            return;
        }

        bool narrowAtTop =
            activeInstance.beamShapeDirection ==
            BeamShapeDirection.NarrowAtTop;

        visual.ApplyBeamArtistSettings(
            narrowAtTop,
            activeInstance.beamRotationDegrees,
            activeInstance.beamLocalOffset);
    }

    private void EnsureFocusOverlay()
    {
        if (focusCanvas == null)
        {
            GameObject canvasObject =
                new("BattleSpotlightFocusCanvas");

            canvasObject.transform.SetParent(
                transform,
                false);

            focusCanvas =
                canvasObject.AddComponent<Canvas>();

            focusCanvas.renderMode =
                RenderMode.ScreenSpaceOverlay;
            focusCanvas.overrideSorting = true;
            focusCanvas.sortingOrder =
                FocusSortingOrder;

            CanvasScaler scaler =
                canvasObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution =
                new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject imageObject =
                new(
                    "BattleSpotlightFocusMask",
                    typeof(RectTransform));

            imageObject.transform.SetParent(
                canvasObject.transform,
                false);

            RectTransform rect =
                imageObject.GetComponent<RectTransform>();

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            focusImage =
                imageObject.AddComponent<Image>();

            focusImage.raycastTarget = false;
            focusImage.color = Color.white;
        }

        if (focusMaterial == null)
        {
            Shader shader =
                Shader.Find(FocusShaderName);

            if (shader == null)
                shader =
                    Resources.Load<Shader>(
                        "BattleShowFocusMask");

            if (shader != null)
            {
                focusMaterial =
                    new Material(shader)
                    {
                        name =
                            "BattleSpotlightFocusMask_Runtime",
                        hideFlags =
                            HideFlags.HideAndDontSave
                    };
            }
        }

        if (focusImage != null)
            focusImage.material = focusMaterial;
    }

    private bool TryProjectScreenRect(
        Camera camera,
        out Vector4 rectUv)
    {
        rectUv =
            new Vector4(
                0.5f,
                0.5f,
                0.5f,
                0.5f);

        RectTransform rect =
            showWorldSet != null
                ? showWorldSet.MountedTvRect
                : null;

        if (camera == null ||
            rect == null ||
            !rect.gameObject.activeInHierarchy)
        {
            return false;
        }

        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);

        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 screen =
                camera.WorldToScreenPoint(
                    corners[i]);

            if (screen.z <= 0f)
                return false;

            float x = screen.x / width;
            float y = screen.y / height;

            minX = Mathf.Min(minX, x);
            minY = Mathf.Min(minY, y);
            maxX = Mathf.Max(maxX, x);
            maxY = Mathf.Max(maxY, y);
        }

        minX -= screenRectPadding;
        minY -= screenRectPadding;
        maxX += screenRectPadding;
        maxY += screenRectPadding;

        rectUv =
            new Vector4(
                Mathf.Clamp01(minX),
                Mathf.Clamp01(minY),
                Mathf.Clamp01(maxX),
                Mathf.Clamp01(maxY));

        return rectUv.z > rectUv.x &&
               rectUv.w > rectUv.y;
    }

    private static bool TryProjectWorldPoint(
        Camera camera,
        Vector3 worldPoint,
        float radiusWorld,
        out Vector2 centerUv,
        out float radiusUv)
    {
        centerUv = new Vector2(0.5f, 0.5f);
        radiusUv = 0f;

        if (camera == null)
            return false;

        Vector3 centerScreen =
            camera.WorldToScreenPoint(
                worldPoint);

        if (centerScreen.z <= 0f)
            return false;

        Vector3 edgeWorld =
            worldPoint +
            camera.transform.right *
            Mathf.Max(
                0.01f,
                radiusWorld);

        Vector3 edgeScreen =
            camera.WorldToScreenPoint(
                edgeWorld);

        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);

        centerUv =
            new Vector2(
                Mathf.Clamp01(centerScreen.x / width),
                Mathf.Clamp01(centerScreen.y / height));

        radiusUv =
            Mathf.Max(
                0.0001f,
                Mathf.Abs(
                    edgeScreen.x -
                    centerScreen.x) /
                height);

        return true;
    }

    private static bool TryProjectCharacter(
        Camera camera,
        Transform target,
        float radiusWorld,
        out Vector2 centerUv,
        out float radiusUv)
    {
        if (target == null ||
            !target.gameObject.activeInHierarchy)
        {
            centerUv = new Vector2(0.5f, 0.5f);
            radiusUv = 0f;
            return false;
        }

        return TryProjectWorldPoint(
            camera,
            target.position,
            radiusWorld,
            out centerUv,
            out radiusUv);
    }

    private static Vector2 ResolveScreenDirection(
        Camera camera,
        Vector3 centerWorld,
        Vector2 directionWorld)
    {
        if (camera == null ||
            directionWorld.sqrMagnitude < 0.0001f)
        {
            return Vector2.right;
        }

        Vector3 center =
            camera.WorldToScreenPoint(
                centerWorld);

        Vector3 direction =
            camera.WorldToScreenPoint(
                centerWorld +
                (Vector3)directionWorld.normalized);

        Vector2 delta =
            new(
                direction.x - center.x,
                direction.y - center.y);

        return delta.sqrMagnitude > 0.0001f
            ? delta.normalized
            : Vector2.right;
    }

    private void ApplyFocusHiddenIfZero()
    {
        if (focusBlend > 0.001f)
        {
            if (focusMaterial != null)
                focusMaterial.SetFloat(
                    "_Presentation",
                    focusBlend);

            if (focusImage != null)
                focusImage.enabled = true;

            return;
        }

        ApplyFocusHiddenImmediate();
    }

    private void ApplyFocusHiddenImmediate()
    {
        focusBlend = 0f;

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

        if (focusImage != null)
            focusImage.enabled = false;
    }

    private static float Damp01(
        float current,
        float target,
        float sharpness,
        float dt)
    {
        float t =
            1f -
            Mathf.Exp(
                -Mathf.Max(
                    0.1f,
                    sharpness) *
                Mathf.Max(
                    0f,
                    dt));

        float value =
            Mathf.Lerp(
                current,
                Mathf.Clamp01(target),
                t);

        if (Mathf.Abs(value - target) < 0.001f)
            value = target;

        return Mathf.Clamp01(value);
    }

    private static float EvaluateIdleBreath(
        float time,
        float cyclesPerSecond,
        float phase)
    {
        float omega =
            Mathf.Max(
                0.05f,
                cyclesPerSecond) *
            Mathf.PI *
            2f;

        float primary =
            Mathf.Sin(
                time * omega +
                phase);

        float secondary =
            Mathf.Sin(
                time * omega * 0.43f +
                phase * 1.67f);

        return Mathf.Clamp(
            primary * 0.74f +
            secondary * 0.26f,
            -1f,
            1f);
    }

    private static Transform FindRecursive(
        Transform root,
        string targetName)
    {
        if (root == null ||
            string.IsNullOrWhiteSpace(targetName))
        {
            return null;
        }

        if (root.name == targetName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found =
                FindRecursive(
                    root.GetChild(i),
                    targetName);

            if (found != null)
                return found;
        }

        return null;
    }

    private static float NormalizeDegrees(float degrees)
    {
        return Mathf.Repeat(
            degrees + 180f,
            360f) - 180f;
    }
}

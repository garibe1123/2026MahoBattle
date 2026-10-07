using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

/// <summary>
/// Artist-facing control for character spotlight beams plus the Combat fake-3D follow rig.
///
/// Static artist settings are still push-based. During Combat only, LateUpdate simulates a
/// virtual overhead light source and aim target, smooths their 3D direction with Quaternion
/// interpolation, then projects that state back into the 2D beam as rotation, shear, length,
/// width and intensity changes.
/// </summary>
[DefaultExecutionOrder(31900)]
[DisallowMultipleComponent]
public sealed class BattleSpotlightBeamDirectionController : MonoBehaviour
{
    private const string SpriteManagerObjectName = "SpriteManager";
    public enum BeamShapeDirection
    {
        /// <summary>Narrow source at the top, broad footprint at the bottom.</summary>
        NarrowAtTop,

        /// <summary>Broad at the top, narrow footprint at the bottom.</summary>
        NarrowAtBottom
    }

    private static BattleSpotlightBeamDirectionController activeInstance;

    [Header("SPOTLIGHT BEAM — 방향 / 형태")]
    [Tooltip("사다리꼴 Beam의 좁은 쪽이 어느 방향을 향할지 정합니다. Narrow At Top은 위쪽 광원이 좁고 바닥 쪽이 넓은 일반적인 스포트라이트 형태입니다.")]
    [SerializeField] private BeamShapeDirection beamShapeDirection = BeamShapeDirection.NarrowAtTop;

    [Tooltip("Beam 전체를 Z축으로 회전시키는 각도입니다. 0은 세로, 90은 오른쪽으로 눕힌 상태입니다. 단위는 도(°)입니다.")]
    [SerializeField, Range(-180f, 180f)] private float beamRotationDegrees;

    [Tooltip("캐릭터를 기준으로 계산된 Beam 위치에 추가하는 월드 좌표 오프셋입니다. X는 좌우, Y는 위아래 이동입니다.")]
    [SerializeField] private Vector2 beamLocalOffset = Vector2.zero;

    [Header("COMBAT PLAYER SPOTLIGHT — FAKE 3D RIG")]
    [Tooltip("가상의 천장 광원 높이입니다. 높을수록 같은 이동량에서도 Beam 기울기가 작아집니다.")]
    [SerializeField, Min(0.5f)] private float virtualLightHeight = 5.2f;
    [Tooltip("Player를 향하는 Aim Target 추종 속도입니다.")]
    [SerializeField, Min(0.1f)] private float aimFollowSharpness = 13f;
    [Tooltip("천장 Light Source의 추종 속도입니다. Aim보다 느리게 두어 실제 조명 헤드처럼 지연시킵니다.")]
    [SerializeField, Min(0.1f)] private float sourceFollowSharpness = 4.2f;
    [Tooltip("Player 속도에 비례해 가상 Light Source가 뒤에 남는 시간값입니다.")]
    [SerializeField, Min(0f)] private float sourceVelocityTrailSeconds = 0.14f;
    [Tooltip("급가속/급회전 때 Light Source가 추가로 뒤에 남는 양입니다.")]
    [SerializeField, Min(0f)] private float sourceAccelerationTrail = 0.006f;
    [SerializeField, Min(0f)] private float maxSourceLagDistance = 1.15f;
    [SerializeField, Range(1f, 45f)] private float maxRigTiltDegrees = 22f;
    [SerializeField, Min(0.1f)] private float angularFollowSharpness = 5.2f;

    [Header("COMBAT BEAM PROJECTION")]
    [SerializeField, Range(0f, 20f)] private float maxBeamRollDegrees = 12f;
    [SerializeField, Range(0f, 0.5f)] private float maxBeamShear = 0.22f;
    [SerializeField, Range(0f, 0.35f)] private float maxBeamLengthBoost = 0.16f;
    [SerializeField, Range(0f, 0.35f)] private float maxBeamWidthBoost = 0.10f;
    [SerializeField, Range(0f, 0.8f)] private float tiltOpacityLoss = 0.24f;

    private BattleCharacterLightVisual[] lightVisuals;

    private BattleRunManager runManager;
    private BattleStageTransitionController stageFlow;
    private PlayerController player;
    private Transform playerBeamTransform;
    private SpriteRenderer playerBeamRenderer;
    private SpriteRenderer playerGlowRenderer;
    private MaterialPropertyBlock beamProperties;
    private MaterialPropertyBlock glowMotionProperties;

    private Vector2 lastPlayerPosition;
    private Vector2 lastPlayerVelocity;
    private bool hasMotionSample;
    private Vector2 virtualAimWorld;
    private Vector2 virtualSourceWorld;
    private Quaternion currentVirtualRotation = Quaternion.identity;
    private Vector2 currentGroundDirection = Vector2.down;
    private float currentTilt01;

    public BeamShapeDirection Direction
    {
        get => beamShapeDirection;
        set
        {
            if (beamShapeDirection == value)
                return;

            beamShapeDirection = value;
            RefreshTargets();
            ApplyToAll();
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
            RefreshTargets();
            ApplyToAll();
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
            RefreshTargets();
            ApplyToAll();
        }
    }

    public static bool TryGetCombatProjection(
        out Vector2 groundDirection,
        out float tilt01)
    {
        groundDirection = Vector2.right;
        tilt01 = 0f;

        if (activeInstance == null ||
            !activeInstance.isActiveAndEnabled ||
            !activeInstance.IsCombatPlayerMotionActive() ||
            !activeInstance.hasMotionSample)
        {
            return false;
        }

        groundDirection =
            activeInstance.currentGroundDirection.sqrMagnitude > 0.0001f
                ? activeInstance.currentGroundDirection.normalized
                : Vector2.right;

        tilt01 = Mathf.Clamp01(
            activeInstance.currentTilt01);

        return true;
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void InstallEditorHook()
    {
        EditorApplication.delayCall -= EnsureInstalledOnBattleSceneManagerInEditor;
        EditorApplication.delayCall += EnsureInstalledOnBattleSceneManagerInEditor;

        EditorSceneManager.sceneOpened -= HandleEditorSceneOpened;
        EditorSceneManager.sceneOpened += HandleEditorSceneOpened;
    }

    private static void HandleEditorSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall -= EnsureInstalledOnBattleSceneManagerInEditor;
        EditorApplication.delayCall += EnsureInstalledOnBattleSceneManagerInEditor;
    }

    /// <summary>
    /// 기존 씬 호환용 fallback입니다. SpriteManager Organizer가 있으면 최종적으로 SpriteManager 아래에 정리됩니다.
    /// </summary>
    private static void EnsureInstalledOnBattleSceneManagerInEditor()
    {
        if (Application.isPlaying)
            return;

        BattleSceneManager manager =
            Object.FindFirstObjectByType<BattleSceneManager>(FindObjectsInactive.Include);
        if (manager == null)
            return;

        BattleSpotlightBeamDirectionController existing =
            Object.FindFirstObjectByType<BattleSpotlightBeamDirectionController>(FindObjectsInactive.Include);
        if (existing != null)
            return;

        Transform spriteManager = manager.transform.Find(SpriteManagerObjectName);
        GameObject target = spriteManager != null ? spriteManager.gameObject : manager.gameObject;
        Undo.AddComponent<BattleSpotlightBeamDirectionController>(target);
        EditorUtility.SetDirty(target);

        if (manager.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallRuntimeDefault()
    {
        BattleSpotlightBeamDirectionController existing =
            FindFirstObjectByType<BattleSpotlightBeamDirectionController>(FindObjectsInactive.Include);
        if (existing != null)
            return;

        BattleSceneManager manager =
            FindFirstObjectByType<BattleSceneManager>(FindObjectsInactive.Include);
        if (manager == null)
            return;

        Transform spriteManager = manager.transform.Find(SpriteManagerObjectName);
        GameObject target = spriteManager != null ? spriteManager.gameObject : manager.gameObject;
        target.AddComponent<BattleSpotlightBeamDirectionController>();
    }

    private void OnEnable()
    {
        activeInstance = this;
        RefreshTargets();
        ApplyToAll();
        ResolveCombatReferences();
        ResetCombatMotion();
    }

    private void OnDisable()
    {
        ResetCombatMotion();

        if (activeInstance == this)
            activeInstance = null;
    }

    private void OnDestroy()
    {
        if (activeInstance == this)
            activeInstance = null;
    }

    private void OnValidate()
    {
        beamRotationDegrees = NormalizeDegrees(beamRotationDegrees);

        // Play Mode에서 Inspector 값이 실제로 바뀐 순간에만 적용합니다.
        if (!Application.isPlaying || !isActiveAndEnabled)
            return;

        activeInstance = this;
        RefreshTargets();
        ApplyToAll();
    }

    private void LateUpdate()
    {
        ResolveCombatReferences();
        ResolvePlayerSpotlightChildren();

        if (!IsCombatPlayerMotionActive() ||
            playerBeamTransform == null ||
            playerBeamRenderer == null ||
            playerBeamRenderer.sprite == null)
        {
            ResetCombatMotion();
            ClearBeamProjectionProperties();
            return;
        }

        UpdateVirtualLightRig();
        ApplyFake3DBeamProjection();
    }

    private void ResolveCombatReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();

        if (stageFlow == null)
        {
            stageFlow = BattleStageTransitionController.Instance != null
                ? BattleStageTransitionController.Instance
                : FindFirstObjectByType<BattleStageTransitionController>();
        }

        PlayerController resolvedPlayer = player != null
            ? player
            : FindFirstObjectByType<PlayerController>();

        if (resolvedPlayer != player)
        {
            player = resolvedPlayer;
            playerBeamTransform = null;
            playerBeamRenderer = null;
            playerGlowRenderer = null;
            beamProperties = null;
            glowMotionProperties = null;
            ResetCombatMotion();
        }
    }

    private bool IsCombatPlayerMotionActive()
    {
        if (player == null || !player.gameObject.activeInHierarchy)
            return false;

        bool stageCombat =
            stageFlow != null &&
            stageFlow.IsCombatPhase;

        bool runCombat =
            runManager != null &&
            runManager.RunActive &&
            runManager.State == BattleRunState.Combat;

        // StageFlow와 RunState가 전환 프레임에 잠깐 어긋나도 전투 조명 반응이 끊기지 않게 합니다.
        return stageCombat || runCombat;
    }

    private void ResolvePlayerSpotlightChildren()
    {
        if (player == null)
            return;

        if (playerBeamTransform == null)
        {
            playerBeamTransform = FindRecursive(
                player.transform,
                BattleCharacterLightVisual.KeyRendererName);
            playerBeamRenderer = playerBeamTransform != null
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

    private void UpdateVirtualLightRig()
    {
        if (player == null)
            return;

        float dt =
            Mathf.Min(
                0.05f,
                Mathf.Max(
                    0.001f,
                    Time.unscaledDeltaTime));

        Vector2 playerPosition =
            player.transform.position;

        if (!hasMotionSample ||
            Vector2.Distance(
                playerPosition,
                lastPlayerPosition) > 3f)
        {
            lastPlayerPosition = playerPosition;
            lastPlayerVelocity = Vector2.zero;
            virtualAimWorld = playerPosition;
            virtualSourceWorld = playerPosition;
            currentVirtualRotation =
                Quaternion.LookRotation(
                    Vector3.back,
                    Vector3.up);
            currentGroundDirection = Vector2.down;
            currentTilt01 = 0f;
            hasMotionSample = true;
            return;
        }

        Vector2 velocity =
            (playerPosition - lastPlayerPosition) / dt;

        Vector2 acceleration =
            (velocity - lastPlayerVelocity) / dt;

        acceleration =
            Vector2.ClampMagnitude(
                acceleration,
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

        Vector3 desiredDirection =
            new(
                planar.x,
                planar.y,
                -Mathf.Max(
                    0.5f,
                    virtualLightHeight));

        if (desiredDirection.sqrMagnitude < 0.0001f)
            desiredDirection = Vector3.back;

        Quaternion targetRotation =
            Quaternion.LookRotation(
                desiredDirection.normalized,
                Vector3.up);

        float angularT =
            1f -
            Mathf.Exp(
                -Mathf.Max(0.1f, angularFollowSharpness) *
                dt);

        currentVirtualRotation =
            Quaternion.Slerp(
                currentVirtualRotation,
                targetRotation,
                angularT);

        Vector3 forward =
            currentVirtualRotation *
            Vector3.forward;

        Vector2 projectedGround =
            new(
                forward.x,
                forward.y);

        float groundMagnitude =
            projectedGround.magnitude;

        if (groundMagnitude > 0.0001f)
        {
            currentGroundDirection =
                projectedGround /
                groundMagnitude;
        }

        float maxTiltSin =
            Mathf.Sin(
                Mathf.Deg2Rad *
                Mathf.Clamp(
                    maxRigTiltDegrees,
                    1f,
                    45f));

        currentTilt01 =
            Mathf.Clamp01(
                groundMagnitude /
                Mathf.Max(
                    0.001f,
                    maxTiltSin));
    }

    private void ApplyFake3DBeamProjection()
    {
        Quaternion authoredRotation =
            playerBeamTransform.rotation;

        Vector3 authoredCenter =
            playerBeamTransform.position;

        Vector3 authoredLocalScale =
            playerBeamTransform.localScale;

        float authoredWorldHeight =
            playerBeamRenderer.sprite.bounds.size.y *
            Mathf.Abs(
                playerBeamTransform.lossyScale.y);

        if (authoredWorldHeight <= 0.0001f)
            return;

        float tilt =
            Mathf.Clamp01(
                currentTilt01);

        float rollDegrees =
            -currentGroundDirection.x *
            Mathf.Max(
                0f,
                maxBeamRollDegrees) *
            tilt;

        Quaternion finalRotation =
            authoredRotation *
            Quaternion.AngleAxis(
                rollDegrees,
                Vector3.forward);

        float lengthScale =
            1f +
            Mathf.Max(
                0f,
                maxBeamLengthBoost) *
            tilt;

        float widthScale =
            1f +
            Mathf.Max(
                0f,
                maxBeamWidthBoost) *
            tilt;

        Vector3 sourcePoint =
            authoredCenter +
            authoredRotation *
            (Vector3.up *
             (authoredWorldHeight * 0.5f));

        playerBeamTransform.localScale =
            new Vector3(
                authoredLocalScale.x * widthScale,
                authoredLocalScale.y * lengthScale,
                authoredLocalScale.z);

        float finalHalfHeight =
            authoredWorldHeight *
            lengthScale *
            0.5f;

        Vector3 finalCenter =
            sourcePoint +
            finalRotation *
            (Vector3.down *
             finalHalfHeight);

        playerBeamTransform.SetPositionAndRotation(
            finalCenter,
            finalRotation);

        beamProperties ??=
            new MaterialPropertyBlock();

        playerBeamRenderer.GetPropertyBlock(
            beamProperties);

        beamProperties.SetFloat(
            "_BeamShear",
            currentGroundDirection.x *
            Mathf.Max(0f, maxBeamShear) *
            tilt);

        beamProperties.SetFloat(
            "_BeamBottomWidthScale",
            1f + 0.18f * tilt);

        beamProperties.SetFloat(
            "_BeamOpacityScale",
            Mathf.Clamp01(
                1f -
                Mathf.Max(0f, tiltOpacityLoss) *
                tilt));

        playerBeamRenderer.SetPropertyBlock(
            beamProperties);

        ApplyDirectionalPlayerTopLight(tilt);
    }

    private void ApplyDirectionalPlayerTopLight(float tilt)
    {
        if (playerGlowRenderer == null)
            return;

        glowMotionProperties ??=
            new MaterialPropertyBlock();

        playerGlowRenderer.GetPropertyBlock(
            glowMotionProperties);

        // If the virtual light source trails on the left, the player's left/top side
        // receives more additive top light, and vice versa.
        glowMotionProperties.SetFloat(
            "_HorizontalBias",
            Mathf.Clamp(
                -currentGroundDirection.x *
                Mathf.Clamp01(tilt),
                -1f,
                1f));

        glowMotionProperties.SetFloat(
            "_DirectionalAmount",
            0.72f);

        playerGlowRenderer.SetPropertyBlock(
            glowMotionProperties);
    }

    private void ClearBeamProjectionProperties()
    {
        if (playerBeamRenderer == null)
            return;

        beamProperties ??=
            new MaterialPropertyBlock();

        playerBeamRenderer.GetPropertyBlock(
            beamProperties);

        beamProperties.SetFloat(
            "_BeamShear",
            0f);

        beamProperties.SetFloat(
            "_BeamBottomWidthScale",
            1f);

        beamProperties.SetFloat(
            "_BeamOpacityScale",
            1f);

        playerBeamRenderer.SetPropertyBlock(
            beamProperties);

        if (playerGlowRenderer != null)
        {
            glowMotionProperties ??=
                new MaterialPropertyBlock();

            playerGlowRenderer.GetPropertyBlock(
                glowMotionProperties);

            glowMotionProperties.SetFloat(
                "_HorizontalBias",
                0f);

            glowMotionProperties.SetFloat(
                "_DirectionalAmount",
                0.55f);

            playerGlowRenderer.SetPropertyBlock(
                glowMotionProperties);
        }
    }

    private void ResetCombatMotion()
    {
        hasMotionSample = false;
        lastPlayerVelocity = Vector2.zero;
        currentVirtualRotation =
            Quaternion.LookRotation(
                Vector3.back,
                Vector3.up);
        currentGroundDirection = Vector2.down;
        currentTilt01 = 0f;

        if (player != null)
        {
            Vector2 playerPosition =
                player.transform.position;

            lastPlayerPosition =
                playerPosition;

            virtualAimWorld =
                playerPosition;

            virtualSourceWorld =
                playerPosition;
        }
    }

    private static Transform FindRecursive(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrWhiteSpace(targetName))
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

    /// <summary>
    /// Called by BattleCharacterLightVisual when a runtime beam is created after this controller.
    /// </summary>
    public static void ApplyCurrentSettingsTo(BattleCharacterLightVisual visual)
    {
        if (visual == null || activeInstance == null || !activeInstance.isActiveAndEnabled)
            return;

        activeInstance.ApplyToVisual(visual);
    }

    /// <summary>
    /// Refreshes currently existing character spotlight rigs once. This is never called every frame.
    /// </summary>
    public void RefreshTargets()
    {
        lightVisuals = FindObjectsByType<BattleCharacterLightVisual>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
    }

    [ContextMenu("Apply Beam Settings Now")]
    public void ApplyNow()
    {
        activeInstance = this;
        RefreshTargets();
        ApplyToAll();
    }

    private void ApplyToAll()
    {
        if (lightVisuals == null)
            return;

        for (int i = 0; i < lightVisuals.Length; i++)
            ApplyToVisual(lightVisuals[i]);
    }

    private void ApplyToVisual(BattleCharacterLightVisual visual)
    {
        if (visual == null)
            return;

        visual.ApplyBeamArtistSettings(
            beamShapeDirection == BeamShapeDirection.NarrowAtTop,
            beamRotationDegrees,
            beamLocalOffset);
    }

    private static float NormalizeDegrees(float degrees)
    {
        return Mathf.Repeat(degrees + 180f, 360f) - 180f;
    }
}

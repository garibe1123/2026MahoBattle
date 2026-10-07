using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

/// <summary>
/// Artist-facing control for the trapezoid/cone part of character spotlights.
///
/// This controller is event-driven. It never applies settings from Update/LateUpdate.
/// Values are pushed only when:
/// - this component is enabled,
/// - an Inspector value changes during Play Mode,
/// - a public property changes,
/// - Apply Beam Settings Now is invoked,
/// - a BattleCharacterLightVisual creates its beam later and registers itself.
/// </summary>
[DefaultExecutionOrder(34000)]
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

    [Header("COMBAT PLAYER SPOTLIGHT — MOTION")]
    [Tooltip("전투 중 발밑 Pool이 플레이어 이동보다 늦게 따라오는 정도입니다.")]
    [SerializeField, Min(0.1f)] private float poolLagSharpness = 6.8f;
    [SerializeField, Min(0f)] private float maxPoolLagDistance = 0.24f;
    [SerializeField, Min(0f)] private float poolLagPerSpeed = 0.030f;

    [Tooltip("전투 중 상부 Beam이 이동 반대 방향으로 기울어지는 최대 각도입니다.")]
    [SerializeField, Range(0f, 12f)] private float maxBeamTiltDegrees = 5.5f;
    [SerializeField, Min(0.1f)] private float speedForFullBeamTilt = 5f;
    [SerializeField, Min(0.1f)] private float beamTiltSharpness = 4.8f;

    private BattleCharacterLightVisual[] lightVisuals;

    private BattleRunManager runManager;
    private BattleStageTransitionController stageFlow;
    private PlayerController player;
    private Transform playerBeamTransform;
    private SpriteRenderer playerBeamRenderer;
    private Transform playerPoolTransform;

    private Vector2 lastPlayerPosition;
    private bool hasMotionSample;
    private Vector2 currentPoolLag;
    private Quaternion currentBeamMotionRotation = Quaternion.identity;

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

        if (!IsCombatPlayerMotionActive())
        {
            ResetCombatMotion();
            return;
        }

        ResolvePlayerSpotlightChildren();
        if (playerBeamTransform == null && playerPoolTransform == null)
            return;

        Vector2 playerPosition = player.transform.position;
        if (!hasMotionSample)
        {
            lastPlayerPosition = playerPosition;
            hasMotionSample = true;
            return;
        }

        float dt = Mathf.Min(0.05f, Mathf.Max(0.001f, Time.unscaledDeltaTime));
        Vector2 velocity = (playerPosition - lastPlayerPosition) / dt;
        lastPlayerPosition = playerPosition;

        UpdatePoolLag(velocity);
        UpdateBeamQuaternion(velocity);
        ApplyPoolLag();
        ApplyBeamQuaternion();
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
            playerPoolTransform = null;
            ResetCombatMotion();
        }
    }

    private bool IsCombatPlayerMotionActive()
    {
        if (player == null || !player.gameObject.activeInHierarchy)
            return false;

        if (stageFlow != null)
            return stageFlow.IsCombatPhase;

        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Combat;
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

        if (playerPoolTransform == null)
        {
            playerPoolTransform = FindRecursive(
                player.transform,
                BattleCharacterLightVisual.PoolRendererName);
        }
    }

    private void UpdatePoolLag(Vector2 velocity)
    {
        Vector2 desired =
            Vector2.ClampMagnitude(
                -velocity * Mathf.Max(0f, poolLagPerSpeed),
                Mathf.Max(0f, maxPoolLagDistance));

        float t =
            1f -
            Mathf.Exp(
                -Mathf.Max(0.1f, poolLagSharpness) *
                Time.unscaledDeltaTime);

        currentPoolLag =
            Vector2.Lerp(
                currentPoolLag,
                desired,
                t);

        if (desired.sqrMagnitude < 0.000001f &&
            currentPoolLag.sqrMagnitude < 0.000001f)
        {
            currentPoolLag = Vector2.zero;
        }
    }

    private void UpdateBeamQuaternion(Vector2 velocity)
    {
        float normalizedHorizontal =
            Mathf.Clamp(
                velocity.x / Mathf.Max(0.1f, speedForFullBeamTilt),
                -1f,
                1f);

        float targetAngle =
            -normalizedHorizontal *
            Mathf.Max(0f, maxBeamTiltDegrees);

        Quaternion target =
            Quaternion.AngleAxis(
                targetAngle,
                Vector3.forward);

        float t =
            1f -
            Mathf.Exp(
                -Mathf.Max(0.1f, beamTiltSharpness) *
                Time.unscaledDeltaTime);

        currentBeamMotionRotation =
            Quaternion.Slerp(
                currentBeamMotionRotation,
                target,
                t);

        if (Mathf.Abs(normalizedHorizontal) < 0.001f &&
            Quaternion.Angle(
                currentBeamMotionRotation,
                Quaternion.identity) < 0.01f)
        {
            currentBeamMotionRotation = Quaternion.identity;
        }
    }

    private void ApplyPoolLag()
    {
        if (playerPoolTransform == null)
            return;

        Vector3 basePosition = playerPoolTransform.position;
        playerPoolTransform.position = new Vector3(
            basePosition.x + currentPoolLag.x,
            basePosition.y + currentPoolLag.y,
            basePosition.z);
    }

    private void ApplyBeamQuaternion()
    {
        if (playerBeamTransform == null ||
            playerBeamRenderer == null ||
            playerBeamRenderer.sprite == null)
        {
            return;
        }

        // BattleCharacterLightVisual has already restored the authored transform this frame.
        Quaternion authoredRotation = playerBeamTransform.rotation;
        Vector3 authoredCenter = playerBeamTransform.position;

        float beamHeight =
            playerBeamRenderer.sprite.bounds.size.y *
            Mathf.Abs(playerBeamTransform.lossyScale.y);
        if (beamHeight <= 0.0001f)
            return;

        // Treat the narrow upper edge as a virtual lamp/source pivot.
        Vector3 sourcePoint =
            authoredCenter +
            authoredRotation *
            (Vector3.up * (beamHeight * 0.5f));

        Quaternion finalRotation =
            authoredRotation *
            currentBeamMotionRotation;

        Vector3 finalCenter =
            sourcePoint +
            finalRotation *
            (Vector3.down * (beamHeight * 0.5f));

        playerBeamTransform.SetPositionAndRotation(
            finalCenter,
            finalRotation);
    }

    private void ResetCombatMotion()
    {
        hasMotionSample = false;
        currentPoolLag = Vector2.zero;
        currentBeamMotionRotation = Quaternion.identity;

        if (player != null)
            lastPlayerPosition = player.transform.position;
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

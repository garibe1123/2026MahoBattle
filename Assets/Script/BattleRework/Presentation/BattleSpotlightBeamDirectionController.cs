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
[DisallowMultipleComponent]
public sealed class BattleSpotlightBeamDirectionController : MonoBehaviour
{
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

    private BattleCharacterLightVisual[] lightVisuals;

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

        Transform spriteManager = manager.transform.Find(BattleSpriteManagerOrganizer.SpriteManagerObjectName);
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

        Transform spriteManager = manager.transform.Find(BattleSpriteManagerOrganizer.SpriteManagerObjectName);
        GameObject target = spriteManager != null ? spriteManager.gameObject : manager.gameObject;
        target.AddComponent<BattleSpotlightBeamDirectionController>();
    }

    private void OnEnable()
    {
        activeInstance = this;
        RefreshTargets();
        ApplyToAll();
    }

    private void OnDisable()
    {
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

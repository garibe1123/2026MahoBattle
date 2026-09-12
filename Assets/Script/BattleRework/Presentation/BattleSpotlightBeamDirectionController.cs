using UnityEngine;

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
///
/// Direction/rotation/offset are applied directly to BattleCharacterLightVisual instead of
/// being routed through shader UV manipulation. This keeps one clear owner for beam orientation.
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

    [Header("Beam Direction")]
    [SerializeField] private BeamShapeDirection beamShapeDirection = BeamShapeDirection.NarrowAtTop;
    [SerializeField, Range(-180f, 180f)] private float beamRotationDegrees;
    [Tooltip("World-unit offset added to the beam after it is positioned on the character.")]
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallRuntimeDefault()
    {
        BattleSpotlightBeamDirectionController existing =
            FindFirstObjectByType<BattleSpotlightBeamDirectionController>(FindObjectsInactive.Include);
        if (existing != null)
            return;

        GameObject host = new("BattleSpotlightBeamDirectionSettings");
        host.AddComponent<BattleSpotlightBeamDirectionController>();
    }

    private void OnEnable()
    {
        // If a controller is explicitly placed on a scene System object, that enabled component
        // becomes authoritative. The runtime fallback exists only when the scene has none.
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

        // During Play Mode the Inspector calls OnValidate only when the serialized value changes.
        // No per-frame polling is used.
        if (!Application.isPlaying || !isActiveAndEnabled)
            return;

        activeInstance = this;
        RefreshTargets();
        ApplyToAll();
    }

    /// <summary>
    /// Called by BattleCharacterLightVisual when a runtime beam is created after this controller.
    /// This fixes the old ordering bug where OnEnable ran before CharacterKeySpotlight existed.
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

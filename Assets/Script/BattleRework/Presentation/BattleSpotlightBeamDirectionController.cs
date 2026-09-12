using UnityEngine;

/// <summary>
/// Event-driven artist control for the trapezoid/cone part of character spotlights.
///
/// No per-frame orientation update is used. Values are pushed only when:
/// - this component is enabled,
/// - an Inspector value changes (OnValidate),
/// - a property is changed from code,
/// - RefreshTargets() is called explicitly.
///
/// Beam direction/rotation/offset are shader properties, so BattleCharacterLightVisual can
/// continue updating position/size without overwriting the artist-selected beam orientation.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleSpotlightBeamDirectionController : MonoBehaviour
{
    public enum BeamShapeDirection
    {
        /// <summary>Narrow source at the top, broad footprint at the bottom.</summary>
        NarrowAtTop,

        /// <summary>Broad at the top, narrow at the bottom.</summary>
        NarrowAtBottom
    }

    private static readonly int BeamFlipYId = Shader.PropertyToID("_BeamFlipY");
    private static readonly int BeamRotationId = Shader.PropertyToID("_BeamRotationDegrees");
    private static readonly int BeamOffsetId = Shader.PropertyToID("_BeamUvOffset");

    [Header("Beam Direction")]
    [SerializeField] private BeamShapeDirection beamShapeDirection = BeamShapeDirection.NarrowAtTop;
    [SerializeField, Range(-180f, 180f)] private float beamRotationDegrees;
    [SerializeField] private Vector2 beamLocalOffset = Vector2.zero;

    private BattleCharacterLightVisual[] lightVisuals;
    private MaterialPropertyBlock propertyBlock;

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
        propertyBlock ??= new MaterialPropertyBlock();
        RefreshTargets();
        ApplyToAll();
    }

    private void OnValidate()
    {
        beamRotationDegrees = NormalizeDegrees(beamRotationDegrees);

        // Inspector changes during Play Mode arrive here once per value edit/drag.
        // No Update/LateUpdate loop is used for these settings.
        if (!Application.isPlaying)
            return;

        propertyBlock ??= new MaterialPropertyBlock();
        RefreshTargets();
        ApplyToAll();
    }

    /// <summary>
    /// Refreshes currently existing character spotlight rigs once and immediately applies
    /// the current Inspector values. This is intentionally not called every frame.
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
        propertyBlock ??= new MaterialPropertyBlock();
        RefreshTargets();
        ApplyToAll();
    }

    private void ApplyToAll()
    {
        if (lightVisuals == null)
            return;

        propertyBlock ??= new MaterialPropertyBlock();

        for (int i = 0; i < lightVisuals.Length; i++)
        {
            BattleCharacterLightVisual visual = lightVisuals[i];
            if (visual == null)
                continue;

            Transform beam = visual.transform.Find(BattleCharacterLightVisual.KeyRendererName);
            if (beam == null)
                continue;

            SpriteRenderer renderer = beam.GetComponent<SpriteRenderer>();
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(
                BeamFlipYId,
                beamShapeDirection == BeamShapeDirection.NarrowAtBottom ? 1f : 0f);
            propertyBlock.SetFloat(BeamRotationId, beamRotationDegrees);
            propertyBlock.SetVector(
                BeamOffsetId,
                new Vector4(beamLocalOffset.x, beamLocalOffset.y, 0f, 0f));
            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    private static float NormalizeDegrees(float degrees)
    {
        return Mathf.Repeat(degrees + 180f, 360f) - 180f;
    }
}

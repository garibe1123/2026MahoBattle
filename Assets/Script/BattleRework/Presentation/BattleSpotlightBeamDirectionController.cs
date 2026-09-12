using UnityEngine;

/// <summary>
/// Global presentation override for the trapezoid/cone part of character spotlights.
///
/// BattleCharacterLightVisual owns strength, size and floor-pool coupling.
/// This component owns only the final beam orientation so the artist can choose the
/// visually correct direction from the Inspector without changing code.
///
/// Add this component to a persistent Battle scene object if you want the values to be
/// serialized with the scene. If none exists, a runtime default is created automatically.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(59000)]
public sealed class BattleSpotlightBeamDirectionController : MonoBehaviour
{
    public enum BeamShapeDirection
    {
        /// <summary>Screen result should read as a narrow source above and broad footprint below.</summary>
        NarrowAtTop,

        /// <summary>Inverse orientation, useful when the renderer/material path presents the texture flipped.</summary>
        NarrowAtBottom
    }

    [Header("Beam Direction")]
    [SerializeField] private BeamShapeDirection beamShapeDirection = BeamShapeDirection.NarrowAtTop;
    [SerializeField, Range(-180f, 180f)] private float beamRotationDegrees;
    [SerializeField] private Vector2 beamLocalOffset = Vector2.zero;

    [Header("Runtime")]
    [SerializeField, Min(0.02f)] private float rescanInterval = 0.20f;

    private BattleCharacterLightVisual[] lightVisuals;
    private float nextRescanTime;

    public BeamShapeDirection Direction
    {
        get => beamShapeDirection;
        set => beamShapeDirection = value;
    }

    public float RotationDegrees
    {
        get => beamRotationDegrees;
        set => beamRotationDegrees = Mathf.Repeat(value + 180f, 360f) - 180f;
    }

    public Vector2 LocalOffset
    {
        get => beamLocalOffset;
        set => beamLocalOffset = value;
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
        nextRescanTime = 0f;
        ResolveVisuals();
        ApplyToAll();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextRescanTime)
        {
            nextRescanTime = Time.unscaledTime + Mathf.Max(0.02f, rescanInterval);
            ResolveVisuals();
        }

        // BattleCharacterLightVisual also updates in LateUpdate. This controller has a much
        // later execution order, so the Inspector-selected orientation is the final result.
        ApplyToAll();
    }

    private void OnValidate()
    {
        rescanInterval = Mathf.Max(0.02f, rescanInterval);
        beamRotationDegrees = Mathf.Repeat(beamRotationDegrees + 180f, 360f) - 180f;

        if (!Application.isPlaying)
            return;

        ResolveVisuals();
        ApplyToAll();
    }

    private void ResolveVisuals()
    {
        lightVisuals = FindObjectsByType<BattleCharacterLightVisual>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
    }

    private void ApplyToAll()
    {
        if (lightVisuals == null)
            return;

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

            // Current authored runtime beam is broad at texture Y=0 and narrow at Y=1.
            // In this project's renderer/material path, flipY=true has been the screen-side
            // correction for NarrowAtTop. The enum exposes both possibilities so art can decide.
            renderer.flipY = beamShapeDirection == BeamShapeDirection.NarrowAtTop;

            Vector3 euler = beam.localEulerAngles;
            euler.z = beamRotationDegrees;
            beam.localEulerAngles = euler;

            if (beamLocalOffset != Vector2.zero)
            {
                Vector3 position = beam.localPosition;
                position.x += beamLocalOffset.x;
                position.y += beamLocalOffset.y;
                beam.localPosition = position;
            }
        }
    }
}

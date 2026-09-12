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

        /// <summary>Inverse orientation: broad at the top and narrow at the bottom.</summary>
        NarrowAtBottom
    }

    [Header("Beam Direction")]
    [SerializeField] private BeamShapeDirection beamShapeDirection = BeamShapeDirection.NarrowAtTop;
    [SerializeField, Range(-180f, 180f)] private float beamRotationDegrees;
    [SerializeField] private Vector2 beamLocalOffset = Vector2.zero;

    private BattleCharacterLightVisual[] lightVisuals;

    public BeamShapeDirection Direction
    {
        get => beamShapeDirection;
        set
        {
            beamShapeDirection = value;
            ApplyToAll();
        }
    }

    public float RotationDegrees
    {
        get => beamRotationDegrees;
        set
        {
            beamRotationDegrees = Mathf.Repeat(value + 180f, 360f) - 180f;
            ApplyToAll();
        }
    }

    public Vector2 LocalOffset
    {
        get => beamLocalOffset;
        set
        {
            beamLocalOffset = value;
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
        RefreshTargets();
        ApplyToAll();
    }

    private void LateUpdate()
    {
        // BattleCharacterLightVisual also updates in LateUpdate. This controller has a much
        // later execution order, so the Inspector-selected orientation is the final result.
        // Target discovery is NOT repeated here; only the cached references are used.
        ApplyToAll();
    }

    private void OnValidate()
    {
        beamRotationDegrees = Mathf.Repeat(beamRotationDegrees + 180f, 360f) - 180f;

        if (!Application.isPlaying)
            return;

        RefreshTargets();
        ApplyToAll();
    }

    /// <summary>
    /// Refreshes spotlight targets once. Call this only when character light rigs are spawned
    /// after scene initialization; it intentionally avoids recurring FindObjects scans.
    /// </summary>
    public void RefreshTargets()
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
            if (visual == null || !visual.isActiveAndEnabled || !visual.gameObject.activeInHierarchy)
                continue;

            Transform beam = visual.transform.Find(BattleCharacterLightVisual.KeyRendererName);
            if (beam == null)
                continue;

            SpriteRenderer renderer = beam.GetComponent<SpriteRenderer>();
            if (renderer == null)
                continue;

            // Runtime screen verification: the current hard-coded flipY=true result is inverted.
            // Therefore NarrowAtTop deliberately uses flipY=false, while NarrowAtBottom uses true.
            renderer.flipY = beamShapeDirection == BeamShapeDirection.NarrowAtBottom;

            // BattleCharacterLightVisual resets the beam rotation each frame. Apply the artist
            // override afterwards so this value is the final visible direction.
            beam.rotation = Quaternion.Euler(0f, 0f, beamRotationDegrees);

            if (beamLocalOffset != Vector2.zero)
            {
                Vector3 right = beam.right * beamLocalOffset.x;
                Vector3 up = beam.up * beamLocalOffset.y;
                beam.position += right + up;
            }
        }
    }
}

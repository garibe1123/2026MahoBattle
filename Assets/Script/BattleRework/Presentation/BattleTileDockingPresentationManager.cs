using DG.Tweening;
using UnityEngine;

/// <summary>
/// 모든 Tile / MapBlock 도킹 연출의 공통 물리감과 충돌 표현을 관리합니다.
///
/// 책임:
/// - 충돌 직후 3단 감쇠 반동(쾅 -> 쿵 -> 쿵쿵)
/// - 공통 Impact VFX / Dust / Camera Impulse
/// - 전투 Procedural 조립, 대기실 Screen Carrier, Reward/Map Show Carrier 등
///   서로 다른 호출 경로가 같은 도킹 감각을 사용하도록 단일 튜닝 지점을 제공합니다.
///
/// 이동 경로 탐색/관통 방지는 MapBlock 또는 BattleProceduralAssemblyAnimator가 담당하고,
/// 이 클래스는 '목적지에 닿은 뒤의 반응'만 소유합니다.
/// </summary>
[DefaultExecutionOrder(-5000)]
[DisallowMultipleComponent]
public sealed class BattleTileDockingPresentationManager : MonoBehaviour
{
    private const float FallbackSettleDuration = 0.202f;

    private static BattleTileDockingPresentationManager instance;

    [Header("3-Step Inertial Settle")]
    [SerializeField, Range(0.02f, 0.30f)] private float primaryReboundDistance = 0.12f;
    [SerializeField, Range(0.01f, 0.15f)] private float secondaryReboundDistance = 0.052f;
    [SerializeField, Range(0f, 0.08f)] private float microReboundDistance = 0.020f;
    [SerializeField, Range(0.01f, 0.10f)] private float primaryOutTime = 0.045f;
    [SerializeField, Range(0.01f, 0.12f)] private float primaryReturnTime = 0.055f;
    [SerializeField, Range(0.005f, 0.08f)] private float secondaryOutTime = 0.028f;
    [SerializeField, Range(0.005f, 0.08f)] private float secondaryReturnTime = 0.035f;
    [SerializeField, Range(0.005f, 0.06f)] private float microOutTime = 0.017f;
    [SerializeField, Range(0.005f, 0.06f)] private float microReturnTime = 0.022f;

    [Header("Impact FX")]
    [SerializeField, Range(0.4f, 2.2f)] private float impactVfxScale = 1f;
    [SerializeField] private int impactVfxSortingOrder = -8;
    [SerializeField] private Color impactColor = new(0.90f, 0.98f, 1f, 1f);
    [SerializeField] private Color dustColor = new(0.50f, 0.56f, 0.64f, 0.72f);

    [Header("Camera Impact")]
    [SerializeField, Range(0f, 0.35f)] private float cameraImpactAmplitude = 0.085f;
    [SerializeField, Range(0.05f, 0.35f)] private float cameraImpactDuration = 0.13f;
    [SerializeField, Range(1f, 1.8f)] private float finalImpactMultiplier = 1.30f;

    private BattleCameraController battleCamera;

    public static BattleTileDockingPresentationManager Instance => instance;

    public static float SharedSettleDuration => instance != null
        ? instance.SettleDuration
        : FallbackSettleDuration;

    public float SettleDuration =>
        Mathf.Max(0.005f, primaryOutTime) +
        Mathf.Max(0.005f, primaryReturnTime) +
        Mathf.Max(0.005f, secondaryOutTime) +
        Mathf.Max(0.005f, secondaryReturnTime) +
        Mathf.Max(0.005f, microOutTime) +
        Mathf.Max(0.005f, microReturnTime);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntimeHost()
    {
        if (FindFirstObjectByType<BattleTileDockingPresentationManager>() != null)
            return;

        GameObject host = new("BattleTileDockingPresentationRuntime");
        DontDestroyOnLoad(host);
        host.AddComponent<BattleTileDockingPresentationManager>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>
    /// 호출측 Sequence에 공통 도킹 반동을 이어 붙입니다.
    /// movingTransform은 이 메서드가 실행될 시점에 이미 destination에 닿아 있어야 합니다.
    /// playFx=false는 외부 시스템이 별도 Impact 이벤트를 소비하는 Legacy 경로의 중복 VFX 방지용입니다.
    /// </summary>
    public void AppendDockSettle(
        Sequence sequence,
        Transform movingTransform,
        Vector3 destination,
        Vector2 travelDirection,
        Vector3 contactPoint,
        float strength,
        bool playFx,
        bool finalImpact = false)
    {
        if (sequence == null || movingTransform == null)
            return;

        Vector2 direction = travelDirection.sqrMagnitude > 0.001f
            ? travelDirection.normalized
            : Vector2.down;
        float safeStrength = Mathf.Clamp(Mathf.Max(0.05f, strength), 0.25f, 2.5f);
        float recoilScale = Mathf.Lerp(0.82f, 1.22f, Mathf.InverseLerp(0.25f, 2.5f, safeStrength));

        sequence.AppendCallback(() =>
        {
            if (movingTransform == null)
                return;

            movingTransform.position = destination;
            if (playFx)
                PlayImpact(contactPoint, direction, safeStrength, finalImpact);
        });

        AppendBounce(
            sequence,
            movingTransform,
            destination,
            direction,
            Mathf.Max(0f, primaryReboundDistance) * recoilScale,
            primaryOutTime,
            primaryReturnTime);

        AppendBounce(
            sequence,
            movingTransform,
            destination,
            direction,
            Mathf.Max(0f, secondaryReboundDistance) * recoilScale,
            secondaryOutTime,
            secondaryReturnTime);

        AppendBounce(
            sequence,
            movingTransform,
            destination,
            direction,
            Mathf.Max(0f, microReboundDistance) * recoilScale,
            microOutTime,
            microReturnTime);
    }

    public void PlayImpact(Vector3 contactPoint, Vector2 travelDirection, float strength, bool finalImpact = false)
    {
        Vector2 direction = travelDirection.sqrMagnitude > 0.001f
            ? travelDirection.normalized
            : Vector2.down;
        float multiplier = finalImpact ? Mathf.Max(1f, finalImpactMultiplier) : 1f;
        float resolvedStrength = Mathf.Clamp(Mathf.Max(0.05f, strength) * multiplier, 0.25f, 2.5f);

        GameObject effect = new(finalImpact ? "TileDockImpact_Final" : "TileDockImpact");
        effect.transform.position = contactPoint;

        MapImpactVfxInstance instanceVfx = effect.AddComponent<MapImpactVfxInstance>();
        instanceVfx.Play(
            null,
            null,
            20f,
            impactVfxScale * Mathf.Lerp(0.86f, 1.30f, Mathf.InverseLerp(0.25f, 2.5f, resolvedStrength)),
            null,
            impactVfxSortingOrder,
            impactColor,
            dustColor,
            direction,
            finalImpact);

        if (battleCamera == null)
            battleCamera = FindFirstObjectByType<BattleCameraController>();

        if (battleCamera != null && cameraImpactAmplitude > 0f)
        {
            float amplitude = cameraImpactAmplitude * Mathf.Lerp(0.82f, 1.30f, Mathf.InverseLerp(0.25f, 2.5f, resolvedStrength));
            battleCamera.PushCameraImpulse(
                -direction,
                amplitude,
                cameraImpactDuration * (finalImpact ? 1.18f : 1f));
        }
    }

    private static void AppendBounce(
        Sequence sequence,
        Transform movingTransform,
        Vector3 destination,
        Vector2 travelDirection,
        float distance,
        float outTime,
        float returnTime)
    {
        if (sequence == null || movingTransform == null || distance <= 0f)
            return;

        Vector3 rebound = destination - (Vector3)(travelDirection * distance);
        sequence.Append(
            movingTransform.DOMove(rebound, Mathf.Max(0.005f, outTime))
                .SetEase(Ease.OutQuad));
        sequence.Append(
            movingTransform.DOMove(destination, Mathf.Max(0.005f, returnTime))
                .SetEase(Ease.InOutQuad));
    }
}

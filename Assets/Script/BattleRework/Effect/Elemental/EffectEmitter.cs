using System.Collections.Generic;
using UnityEngine;

public enum EffectEmissionMode { Explosion, Faucet, Sprinkler, Trail, Stationary, Line, Area }

[DisallowMultipleComponent]
public sealed class EffectEmitter : MonoBehaviour
{
    private struct ActiveVisual
    {
        public EffectVisual visual;
        public int execution;
        public EffectDefinitionSO definition;
        public bool detached;
    }

    public EffectDefinitionSO definition;
    public EffectVisual visualPrefab;
    [Tooltip("Faucet sprays in Direction; Trail deposits along movement; Stationary, Line and Area stay in world space.")]
    public EffectEmissionMode mode;
    public EffectFaction faction;
    public ElementDefinitionSO[] runtimeElements;
    [Min(1)] public int burstCount = 8;
    [Min(1)] public int streamCount = 1;
    [Min(0.01f)] public float interval = 0.12f;
    [Range(0f, 360f)] public float spreadAngle = 35f;
    public Vector2 direction = Vector2.right;
    [Min(0f)] public float speed = 1f;
    [Range(0f, 1f)] public float speedVariation = 0.25f;
    [Min(0f)] public float spawnRadius = 0.12f;
    public float sprinklerDegreesPerSecond = 140f;
    public float emissionDuration = 1.5f;
    public bool playOnStart;
    [Range(1, 4096)] public int maxActive = 64;
    [Min(0.01f)] public float trailSpacing = 0.2f;
    [Min(0f)] public float lineLength = 3f;
    [Tooltip("Emit an independent, finite Explosion when a BattleObstacle on this object or a parent breaks.")]
    public bool burstOnBattleObstacleBroken;
    [Tooltip("Fallback lifetime for detached destruction bursts whose definition waits for Stop.")]
    [Min(0.01f)] public float destructionBurstDuration = 2f;
    private readonly List<ActiveVisual> active = new();
    private readonly EffectEmissionPath path = new();
    private readonly Vector3[] pathPoints = new Vector3[64];
    private BattleObstacle battleObstacle;
    private Vector3 trailPosition;
    private bool emitting;
    private float elapsed, timer, visualDuration;

    public EffectVisual Latest
    {
        get
        {
            for (int i = active.Count - 1; i >= 0; i--)
                if (Valid(active[i])) return active[i].visual;
            return null;
        }
    }

    public int ActiveCount => active.Count;

    private void Awake()
    {
        if (burstOnBattleObstacleBroken) battleObstacle = GetComponentInParent<BattleObstacle>();
    }

    private void OnEnable()
    {
        if (burstOnBattleObstacleBroken && battleObstacle != null) battleObstacle.Broken += HandleBattleObstacleBroken;
    }

    private void HandleBattleObstacleBroken(BattleObstacle obstacle) { Burst(); }

    public void Burst()
    {
        if (definition == null) return;
        Emit(transform.position, EffectEmissionMode.Explosion,
            definition.duration == -1f ? EffectNumbers.Positive(destructionBurstDuration, 2f) : definition.duration, true);
    }

    private void Start() { if (playOnStart) Trigger(); }

    public void Trigger() { Trigger(definition, mode, definition != null ? definition.duration : 0f); }

    public void Trigger(EffectDefinitionSO data, EffectEmissionMode emissionMode, float duration)
    {
        StopEmission(false);
        definition = data;
        mode = emissionMode;
        visualDuration = duration;
        elapsed = timer = 0f;
        if (definition == null) return;
        trailPosition = transform.position;
        path.Reset(trailPosition, trailSpacing);
        Emit();
        emitting = mode == EffectEmissionMode.Trail ||
            ((mode == EffectEmissionMode.Faucet || mode == EffectEmissionMode.Sprinkler) && visualDuration != -1f);
    }

    private void Update()
    {
        for (int i = active.Count - 1; i >= 0; i--)
            if (!Valid(active[i])) active.RemoveAt(i);
        if (!emitting) return;
        float delta = EffectNumbers.NonNegative(Time.deltaTime);
        if (mode == EffectEmissionMode.Trail)
        {
            Vector3 point = transform.position;
            if (emissionDuration != -1f)
            {
                float remaining = Mathf.Max(0f, EffectNumbers.NonNegative(emissionDuration) - elapsed);
                if (remaining < delta && delta > 0f)
                    point = Vector3.Lerp(trailPosition, point, remaining / delta);
            }
            int count = path.Capture(point, trailSpacing, pathPoints);
            for (int i = 0; i < count; i++) Emit(pathPoints[i], mode, visualDuration);
            trailPosition = point;
        }
        elapsed += delta;
        if (emissionDuration != -1f && elapsed >= EffectNumbers.NonNegative(emissionDuration))
        {
            emitting = false;
            return;
        }
        if (mode == EffectEmissionMode.Trail) return;
        timer += delta;
        float step = EffectNumbers.Positive(interval, 0.12f);
        if (timer < step) return;
        timer %= step;
        Emit();
    }

    private void Emit()
    {
        Emit(transform.position, mode, visualDuration);
    }

    private void Emit(Vector3 origin, EffectEmissionMode emissionMode, float duration, bool detached = false)
    {
        int count = Mathf.Clamp(emissionMode == EffectEmissionMode.Explosion || emissionMode == EffectEmissionMode.Area
            ? burstCount : streamCount, 1, 64);
        float length = EffectNumbers.NonNegative(lineLength);
        if (emissionMode == EffectEmissionMode.Trail || emissionMode == EffectEmissionMode.Stationary) count = 1;
        else if (emissionMode == EffectEmissionMode.Line)
            count = length > 0f ? Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(63f,
                length / EffectNumbers.Positive(trailSpacing, 0.2f))) + 1, 2, 64) : 1;
        bool stationary = emissionMode == EffectEmissionMode.Trail || emissionMode == EffectEmissionMode.Stationary ||
            emissionMode == EffectEmissionMode.Line || emissionMode == EffectEmissionMode.Area;
        Vector2 heading = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        float baseAngle = Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg;
        if (emissionMode == EffectEmissionMode.Sprinkler) baseAngle += elapsed * sprinklerDegreesPerSecond;
        int matchingCount = 0;
        for (int i = 0; i < active.Count; i++)
            if (active[i].definition == definition && Valid(active[i])) matchingCount++;
        for (int i = 0; i < count && matchingCount < Mathf.Clamp(maxActive, 1, 4096); i++)
        {
            float angle = baseAngle + (emissionMode == EffectEmissionMode.Explosion ? (i + Random.value) * 360f / count
                : Random.Range(-spreadAngle * 0.5f, spreadAngle * 0.5f));
            Vector3 heading3 = new(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad), 0f);
            Vector3 point = origin;
            if (emissionMode == EffectEmissionMode.Line)
                point += (Vector3)heading * (count > 1 ? length * i / (count - 1) : 0f);
            else if (!stationary || emissionMode == EffectEmissionMode.Area)
                point += (Vector3)Random.insideUnitCircle * EffectNumbers.NonNegative(spawnRadius);
            float launchSpeed = EffectNumbers.NonNegative(speed) * Random.Range(
                1f - Mathf.Clamp01(speedVariation), 1f + Mathf.Clamp01(speedVariation));
            EffectVisual visual = EffectVisual.Spawn(definition, point, Quaternion.identity, duration,
                stationary ? Vector3.zero : heading3 * launchSpeed, !stationary && definition.family == EffectFamily.Liquid,
                faction, runtimeElements, visualPrefab, stationary: stationary);
            if (visual != null && visual.IsPlaying)
            {
                active.Add(new ActiveVisual { visual = visual, execution = visual.ExecutionId, definition = definition,
                    detached = detached });
                matchingCount++;
            }
        }
    }

    private static bool Valid(ActiveVisual item) => item.visual != null &&
        item.visual.ExecutionId == item.execution && item.visual.IsPlaying;

    public void StopEmission(bool finishVisuals = true)
    {
        emitting = false;
        if (!finishVisuals) return;
        for (int i = active.Count - 1; i >= 0; i--)
            if (!active[i].detached && Valid(active[i])) active[i].visual.RequestStop();
    }

    public void ClearVisuals()
    {
        ClearVisuals(true);
    }

    private void ClearVisuals(bool includeDetached)
    {
        emitting = false;
        for (int i = active.Count - 1; i >= 0; i--)
            if ((includeDetached || !active[i].detached) && Valid(active[i])) PoolService.Release(active[i].visual.gameObject);
        active.Clear();
    }

    private void OnDisable()
    {
        if (battleObstacle != null) battleObstacle.Broken -= HandleBattleObstacleBroken;
        StopEmission();
    }
    private void OnDestroy() { ClearVisuals(false); }
}

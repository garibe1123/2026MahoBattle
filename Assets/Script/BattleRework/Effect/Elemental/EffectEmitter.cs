using System.Collections.Generic;
using UnityEngine;

public enum EffectEmissionMode { Explosion, Faucet, Sprinkler }

[DisallowMultipleComponent]
public sealed class EffectEmitter : MonoBehaviour
{
    private struct ActiveVisual
    {
        public EffectVisual visual;
        public int execution;
    }

    public EffectDefinitionSO definition;
    public EffectVisual visualPrefab;
    public EffectEmissionMode mode;
    public EffectFaction faction;
    public ElementDefinitionSO[] runtimeElements;
    [Min(1)] public int burstCount = 8;
    [Min(1)] public int streamCount = 1;
    [Min(0.01f)] public float interval = 0.12f;
    [Range(0f, 360f)] public float spreadAngle = 35f;
    public Vector2 direction = Vector2.right;
    [Min(0f)] public float speed = 1f;
    [Min(0f)] public float spawnRadius = 0.12f;
    public float sprinklerDegreesPerSecond = 140f;
    public float emissionDuration = 1.5f;
    [Range(1, 256)] public int maxActive = 64;
    private readonly List<ActiveVisual> active = new();
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

    public void Trigger() { Trigger(definition, mode, definition != null ? definition.duration : 0f); }

    public void Trigger(EffectDefinitionSO data, EffectEmissionMode emissionMode, float duration)
    {
        StopEmission(false);
        definition = data;
        mode = emissionMode;
        visualDuration = duration;
        elapsed = timer = 0f;
        if (definition == null) return;
        Emit();
        emitting = mode != EffectEmissionMode.Explosion;
    }

    private void Update()
    {
        for (int i = active.Count - 1; i >= 0; i--)
            if (!Valid(active[i])) active.RemoveAt(i);
        if (!emitting) return;
        float delta = EffectNumbers.NonNegative(Time.deltaTime);
        elapsed += delta;
        if (emissionDuration != -1f && elapsed >= EffectNumbers.NonNegative(emissionDuration))
        {
            emitting = false;
            return;
        }
        timer += delta;
        float step = EffectNumbers.Positive(interval, 0.12f);
        if (timer < step) return;
        timer %= step;
        Emit();
    }

    private void Emit()
    {
        int count = Mathf.Clamp(mode == EffectEmissionMode.Explosion ? burstCount : streamCount, 1, 64);
        Vector2 heading = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        float baseAngle = Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg;
        if (mode == EffectEmissionMode.Sprinkler) baseAngle += elapsed * sprinklerDegreesPerSecond;
        for (int i = 0; i < count && active.Count < Mathf.Clamp(maxActive, 1, 256); i++)
        {
            float angle = baseAngle + (mode == EffectEmissionMode.Explosion ? i * 360f / count
                : Random.Range(-spreadAngle * 0.5f, spreadAngle * 0.5f));
            Vector3 heading3 = new(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad), 0f);
            Vector3 point = transform.position + heading3 * EffectNumbers.NonNegative(spawnRadius);
            EffectVisual visual = EffectVisual.Spawn(definition, point, Quaternion.identity, visualDuration,
                heading3 * EffectNumbers.NonNegative(speed), definition.family == EffectFamily.Liquid,
                faction, runtimeElements, visualPrefab);
            if (visual != null && visual.IsPlaying)
                active.Add(new ActiveVisual { visual = visual, execution = visual.ExecutionId });
        }
    }

    private static bool Valid(ActiveVisual item) => item.visual != null &&
        item.visual.ExecutionId == item.execution && item.visual.IsPlaying;

    public void StopEmission(bool finishVisuals = true)
    {
        emitting = false;
        if (!finishVisuals) return;
        for (int i = active.Count - 1; i >= 0; i--)
            if (Valid(active[i])) active[i].visual.RequestStop();
    }

    private void OnDisable() { StopEmission(); }
}

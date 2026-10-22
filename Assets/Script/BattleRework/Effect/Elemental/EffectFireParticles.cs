using System;
using UnityEngine;

internal sealed class EffectFireParticles
{
    private const int MaxParticles = 96;
    private const int MaxSources = 3;
    private struct Particle
    {
        public Vector3 position;
        public Vector3 origin;
        public Vector3 velocity;
        public Vector3 direction;
        public float age, lifetime, radius, seed, endAge, surge;
        public int source;
        public bool active;
    }

    private readonly Particle[] particles = new Particle[MaxParticles];
    private readonly Vector2[] sourceOffsets = new Vector2[MaxSources];
    private readonly float[] sourceScales = new float[MaxSources];
    private EffectDefinitionSO definition;
    private int cursor;
    private int sourceCount, nextSource;
    private float scale, timer, endProgress, flowTime, flowSeed;
    private bool ending;
    private float attachedLifetime;
    public int Capacity { get; private set; }

    public void Play(EffectDefinitionSO data, Vector3 origin, Vector3 velocity, float size, float attachedLifetime = 0f)
    {
        Reset();
        definition = data;
        this.attachedLifetime = attachedLifetime;
        scale = size;
        flowSeed = UnityEngine.Random.Range(0f, 1000f);
        Capacity = Mathf.Clamp(data.fireParticleCapacity, 8, MaxParticles);
        int minSources = Mathf.Clamp(Mathf.Min(data.fireSourceCount.x, data.fireSourceCount.y), attachedLifetime > 0f ? 1 : 2, MaxSources);
        int maxSources = Mathf.Clamp(Mathf.Max(data.fireSourceCount.x, data.fireSourceCount.y), minSources, MaxSources);
        sourceCount = UnityEngine.Random.Range(minSources, maxSources + 1);
        float minScale = EffectNumbers.Positive(data.fireSourceScale.x, 0.7f);
        float maxScale = EffectNumbers.Positive(data.fireSourceScale.y, 1.25f);
        float sourceSpread = EffectNumbers.NonNegative(data.fireSourceSpread) * scale;
        for (int i = 0; i < sourceCount; i++)
        {
            sourceScales[i] = UnityEngine.Random.Range(Mathf.Min(minScale, maxScale), Mathf.Max(minScale, maxScale)) *
                EffectNumbers.SpawnScale(data);
            sourceOffsets[i] = UnityEngine.Random.insideUnitCircle * sourceSpread;
        }
        int initial = Mathf.Clamp(data.pieceCount, sourceCount, 5);
        initial = UnityEngine.Random.Range(Mathf.Max(sourceCount, initial - 1), initial + 1);
        for (int i = 0; i < initial; i++) Emit(origin, velocity);
        timer = Interval();
    }

    public void Tick(float delta, Vector3 origin, Vector3 velocity, EffectPhase phase, float progress)
    {
        if (definition == null) return;
        flowTime += delta;
        if (attachedLifetime <= 0f && phase == EffectPhase.End && !ending)
        {
            ending = true;
            for (int i = 0; i < Capacity; i++) particles[i].endAge = NormalizedAge(particles[i]);
        }
        endProgress = ending ? Mathf.Clamp01(progress) : 0f;
        float spread = EffectNumbers.NonNegative(definition.fireSpread) * scale;
        float smokeSpread = EffectNumbers.NonNegative(definition.fireSmokeSpread);
        float surgeTime = flowTime * EffectNumbers.Positive(definition.fireSurgeFrequency, 1.2f);
        float flameDuration = FlameDuration;
        for (int i = 0; i < Capacity; i++)
        {
            Particle particle = particles[i];
            if (!particle.active) continue;
            particle.age += delta;
            float t = NormalizedAge(particle);
            if (ending) t = Mathf.Max(t, Mathf.Lerp(particle.endAge, 1f, endProgress));
            if (t >= 1f) { particle.active = false; particles[i] = particle; continue; }
            if (attachedLifetime > 0f) { particles[i] = particle; continue; }
            float wander = (Mathf.PerlinNoise(particle.seed, particle.age * 1.7f) - 0.5f) * 2f;
            float smoke = Mathf.SmoothStep(0f, 1f, (t - flameDuration) / (1f - flameDuration));
            float flow = (Mathf.PerlinNoise(flowSeed + particle.source * 19f,
                surgeTime - particle.age * 0.35f) - 0.5f) * 2f;
            Vector3 drift = particle.velocity * Mathf.Lerp(1f, 0.7f, smoke);
            drift.x += flow * (spread + particle.radius * particle.surge * 2f) * (1f - smoke);
            drift.y += particle.radius * particle.surge * (1f + flow) * (1f - smoke);
            drift.x += wander * spread * (Mathf.Lerp(0.25f, 1f, t) +
                smokeSpread * smoke);
            drift.y += (Mathf.PerlinNoise(particle.seed + 71f, particle.age * 1.3f) - 0.5f) *
                spread * smoke;
            particle.position += drift * delta;
            particle.direction = drift;
            particles[i] = particle;
        }
        if (attachedLifetime > 0f || ending || definition.fireEmissionRate <= 0f) return;
        timer -= delta;
        for (int emitted = 0; timer <= 0f && emitted < Capacity; emitted++)
        {
            Emit(origin, velocity);
            timer += Interval();
        }
        timer = Mathf.Max(0f, timer);
    }

    private float Interval() => UnityEngine.Random.Range(0.7f, 1.3f) /
        Mathf.Clamp(EffectNumbers.Positive(definition.fireEmissionRate, 32f), 1f, 240f) *
        EffectNumbers.Positive(definition.fireEmissionIntervalScale, 1.6f);

    private void Emit(Vector3 origin, Vector3 velocity)
    {
        for (int attempt = 0; attempt < Capacity; attempt++)
        {
            int index = cursor;
            cursor = (cursor + 1) % Capacity;
            if (particles[index].active) continue;
            int sourceIndex = nextSource;
            nextSource = (nextSource + 1) % sourceCount;
            float sourceScale = scale * sourceScales[sourceIndex];
            float surge = Mathf.PerlinNoise(flowSeed + sourceIndex * 19f,
                flowTime * EffectNumbers.Positive(definition.fireSurgeFrequency, 1.2f));
            surge = Mathf.Pow(Mathf.Clamp01((surge - 0.3f) / 0.5f), 2f) *
                Mathf.Clamp(definition.fireSurgeStrength, 0f, 2f);
            Vector2 sourceOffset = sourceOffsets[sourceIndex];
            float minLife = EffectNumbers.Positive(definition.fireParticleLifetime.x, 0.8f);
            float maxLife = EffectNumbers.Positive(definition.fireParticleLifetime.y, 1.3f);
            float minRadius = EffectNumbers.Positive(definition.fireParticleRadius.x, 0.09f);
            float maxRadius = EffectNumbers.Positive(definition.fireParticleRadius.y, 0.19f);
            float radius = UnityEngine.Random.Range(Mathf.Min(minRadius, maxRadius), Mathf.Max(minRadius, maxRadius)) *
                sourceScale * EffectNumbers.SpawnScale(definition);
            float spread = EffectNumbers.NonNegative(definition.fireSpread) * sourceScale;
            Vector2 jitter = UnityEngine.Random.insideUnitCircle * radius * 0.6f;
            particles[index] = new Particle
            {
                active = true,
                origin = origin + new Vector3(sourceOffset.x, sourceOffset.y, 0f),
                position = origin + new Vector3(sourceOffset.x + jitter.x, sourceOffset.y + jitter.y, 0f),
                velocity = velocity * 0.2f + new Vector3(UnityEngine.Random.Range(-spread, spread),
                    EffectNumbers.NonNegative(definition.gasRise) * sourceScale * UnityEngine.Random.Range(0.65f, 1.4f) +
                    (EffectNumbers.NonNegative(definition.gasRise) * sourceScale + radius * 2f) * surge, 0f),
                lifetime = attachedLifetime > 0f ? attachedLifetime :
                    UnityEngine.Random.Range(Mathf.Min(minLife, maxLife), Mathf.Max(minLife, maxLife)),
                radius = radius,
                seed = UnityEngine.Random.Range(0f, 1000f),
                source = sourceIndex,
                surge = surge
            };
            return;
        }
    }

    private static float NormalizedAge(Particle particle) => particle.lifetime > 0f
        ? Mathf.Clamp01(particle.age / particle.lifetime) : 1f;

    private float FlameDuration => Mathf.Clamp(EffectNumbers.Positive(definition.fireFlameDuration, 0.68f), 0.35f, 0.9f);

    private float Smoke(float age) => attachedLifetime > 0f ? 0f :
        Mathf.SmoothStep(0f, 1f, (age - FlameDuration) / (1f - FlameDuration));

    public float GetVisibilityRadius(Vector3 origin)
    {
        if (definition == null) return 0f;
        // Bound the existing sample footprint without generating render samples.
        float growth = (1f + EffectNumbers.NonNegative(definition.gasExpansion) +
            EffectNumbers.NonNegative(definition.fireSmokeExpansion)) *
            (1f + Mathf.Clamp(definition.fireTurbulence, 0f, 0.6f));
        Vector2 shape = new(EffectNumbers.Positive(definition.fireShapeScale.x, 1f),
            EffectNumbers.Positive(definition.fireShapeScale.y, 1.35f));
        float radius = 0f;
        for (int i = 0; i < Capacity; i++)
        {
            Particle particle = particles[i];
            if (!particle.active) continue;
            float t = NormalizedAge(particle);
            if (ending) t = Mathf.Max(t, Mathf.Lerp(particle.endAge, 1f, endProgress));
            Vector3 center = particle.position + (Vector3)(definition.visualCurves?.Offset(t) ?? Vector2.zero) * scale;
            Vector2 extent = new Vector2(shape.x * 1.25f, shape.y * (1f + particle.surge * 0.65f)) *
                (particle.radius * growth * 2f);
            radius = Mathf.Max(radius, (center - origin).magnitude + extent.magnitude);
        }
        return radius;
    }

    public bool Capture(int index, out EffectBlobSample sample)
    {
        sample = default;
        if (definition == null || index < 0 || index >= Capacity) return false;
        Particle particle = particles[index];
        if (!particle.active || particle.radius <= 0f) return false;
        float t = NormalizedAge(particle);
        if (ending) t = Mathf.Max(t, Mathf.Lerp(particle.endAge, 1f, endProgress));
        EffectVisualCurves curves = definition.visualCurves;
        float colorAge = curves?.ColorProgress(t) ?? t;
        float smoke = Smoke(colorAge);
        Color color = Color.Lerp(definition.fireHotColor, definition.fireWarmColor,
            Mathf.SmoothStep(0f, 1f, colorAge / FlameDuration));
        color = Color.Lerp(color, definition.fireSootColor, smoke);
        float fadeStart = attachedLifetime > 0f ? Mathf.Clamp(definition.character.fadeStart, 0f, 0.99f) :
            Mathf.Max(FlameDuration, Mathf.Clamp(definition.fireFadeStart, 0f, 0.9f));
        color.a = (1f - Mathf.SmoothStep(0f, 1f, (t - fadeStart) / (1f - fadeStart))) *
            Mathf.Clamp01(definition.tint.a);
        color.a *= curves?.Opacity(t) ?? 1f;
        color.r *= definition.tint.r;
        color.g *= definition.tint.g;
        color.b *= definition.tint.b;
        float lateral = Mathf.Abs(particle.position.x - particle.origin.x) /
            Mathf.Max(0.0001f, scale * EffectNumbers.NonNegative(definition.fireSpread) + particle.radius);
        color.a *= Mathf.Lerp(1f, Mathf.Clamp01(definition.fireSmokeOpacity), smoke) *
            Mathf.Exp(-lateral * lateral * smoke * EffectNumbers.NonNegative(definition.fireSmokeEdgeFade));
        sample.fireSmoke = smoke;
        float growth = 1f + EffectNumbers.NonNegative(definition.gasExpansion) * t +
            EffectNumbers.NonNegative(definition.fireSmokeExpansion) * smoke;
        float cycle = particle.age * EffectNumbers.Positive(definition.variationFrequency, 1.5f) * 6f;
        growth *= 1f + Mathf.Sin(particle.seed + cycle) *
            Mathf.Clamp(definition.fireTurbulence, 0f, 0.6f) * Mathf.Lerp(0.35f, 1f, smoke);
        float separation = Mathf.Lerp(1f, 0.55f, Mathf.SmoothStep(0f, 1f, (t - 0.65f) / 0.35f));
        sample.center = particle.position + (Vector3)(curves?.Offset(t) ?? Vector2.zero) * scale;
        sample.radius = new Vector2(Mathf.Lerp(1f, 1.25f, smoke), Mathf.Lerp(1f, 0.9f, smoke)) *
            (particle.radius * growth * separation);
        sample.radius = Vector2.Scale(sample.radius, new Vector2(
            EffectNumbers.Positive(definition.fireShapeScale.x, 1f),
            EffectNumbers.Positive(definition.fireShapeScale.y, 1.35f)));
        sample.radius.y *= 1f + particle.surge * 0.65f * (1f - smoke);
        float angle = particle.seed + particle.age * 0.8f;
        sample.fireShape = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) *
            (Mathf.Clamp(definition.fireContourVariation, 0f, 0.4f) * Mathf.Lerp(0.35f, 1f, smoke));
        Vector3 drift = particle.direction.sqrMagnitude > 0.0001f ? particle.direction : particle.velocity;
        Vector2 direction = new(drift.x / sample.radius.x, drift.y / sample.radius.y);
        sample.fireDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
        sample.fireTaper = Mathf.Clamp(definition.fireTipSharpness, 0f, 0.9f) * Mathf.Lerp(1f, 0.3f, smoke);
        float tearing = Mathf.SmoothStep(0f, 1f, (t - 0.25f) / 0.5f);
        sample.fireHole = new Vector2(Mathf.Sin(particle.seed + cycle * 0.8f) * 0.75f,
            Mathf.Lerp(-0.4f, 1.3f, tearing) + Mathf.Cos(particle.seed * 1.7f + cycle) * 0.3f);
        sample.fireHoleRadius = Mathf.Lerp(0.25f, 0.85f, tearing);
        sample.fireTear = tearing * Mathf.Clamp01(definition.fireTearStrength) *
            (0.85f + Mathf.Sin(particle.seed + cycle * 1.3f) * 0.4f);
        sample.weight = 1f;
        sample.fireColor = color;
        return color.a > 0.001f;
    }

    public void Translate(Vector3 delta)
    {
        for (int i = 0; i < Capacity; i++)
        {
            if (!particles[i].active) continue;
            particles[i].position += delta;
            particles[i].origin += delta;
        }
    }

    public void Reset()
    {
        Array.Clear(particles, 0, particles.Length);
        Array.Clear(sourceOffsets, 0, sourceOffsets.Length);
        Array.Clear(sourceScales, 0, sourceScales.Length);
        definition = null;
        Capacity = cursor = 0;
        sourceCount = nextSource = 0;
        scale = timer = endProgress = flowTime = flowSeed = 0f;
        ending = false;
        attachedLifetime = 0f;
    }
}

internal sealed class EffectDetailParticles
{
    public const int MaxParticles = 32;
    private const int MaxCloudPieces = 2, MaxWaterParticles = 8;
    private const float WaterGravity = 4f;
    private struct Particle
    {
        public Vector3 position, velocity;
        public float age, lifetime, radius, seed;
        public bool active;
    }

    private readonly Particle[] particles = new Particle[MaxParticles];
    private EffectDefinitionSO definition;
    private EffectDetailSettings settings;
    private float scale, timer, endFade = 1f;
    private float pixelStep, flowTime, flowSeed;
    private Vector2 footprint;
    private int capacity, cursor, emitted;
    public int Capacity => capacity;
    public bool IsCloud => definition != null && !definition.fireParticles && definition.family == EffectFamily.Gas;
    public float VisibilityRadius { get; private set; }

    public void Play(EffectDefinitionSO data, EffectDetailSettings detailSettings, Vector3 origin, float size,
        Vector2 sourceExtent, float pixelSize)
    {
        Reset();
        definition = data;
        settings = detailSettings;
        scale = size;
        footprint = sourceExtent;
        pixelStep = EffectNumbers.Positive(pixelSize, 0.0625f);
        endFade = 1f;
        capacity = Mathf.Clamp(settings.capacity, 1, IsCloud ? MaxCloudPieces : data.fireParticles ? MaxParticles : MaxWaterParticles);
        float life = Mathf.Max(EffectNumbers.Positive(settings.lifetime.x, 0.4f),
            EffectNumbers.Positive(settings.lifetime.y, 0.9f));
        float travel = 1f;
        if (data.fireParticles)
        {
            flowSeed = UnityEngine.Random.Range(0f, 1000f);
            life *= EffectNumbers.Positive(data.emberLifetimeScale, 2.5f);
            travel = EffectNumbers.Positive(data.emberTravelScale, 1.8f) * 1.6f;
        }
        VisibilityRadius = (EffectNumbers.NonNegative(settings.spread) + Mathf.Abs(settings.rise)) * travel * life * scale +
            Mathf.Max(EffectNumbers.Positive(settings.radius.x, 0.025f), EffectNumbers.Positive(settings.radius.y, 0.055f)) *
            3f * (1f + EffectNumbers.NonNegative(settings.expansion)) * scale;
        if (data.family == EffectFamily.Liquid && !data.fireParticles)
            VisibilityRadius += 0.5f * WaterGravity * scale * life * life;
        if (data.fireParticles)
            VisibilityRadius += 0.5f * EffectNumbers.NonNegative(settings.updraftAcceleration) * scale * life * life;
        VisibilityRadius += footprint.magnitude * 2f + pixelStep;
        if (data.fireParticles) Emit(origin);
        timer = Interval();
    }

    public void Tick(float delta, Vector3 origin, EffectPhase phase, float progress)
    {
        if (definition == null) return;
        flowTime += delta;
        endFade = phase == EffectPhase.End ? 1f - Mathf.Clamp01(progress) : 1f;
        bool fireParticles = definition.fireParticles;
        bool liquid = definition.family == EffectFamily.Liquid && !fireParticles;
        float gravityStep = WaterGravity * scale * delta;
        float currentTime = flowTime * 0.45f;
        float travel = fireParticles ? EffectNumbers.Positive(definition.emberTravelScale, 1.8f) : 1f;
        float spread = EffectNumbers.NonNegative(settings.spread);
        float updraft = EffectNumbers.NonNegative(settings.updraftAcceleration) * scale;
        float drag = 1f + delta * 0.35f;
        bool curved = settings.visualCurves != null && settings.visualCurves.enabled;
        float expansion = 1f + EffectNumbers.NonNegative(settings.expansion);
        for (int i = 0; i < capacity; i++)
        {
            Particle particle = particles[i];
            if (!particle.active) continue;
            particle.age += delta;
            if (particle.age >= particle.lifetime) particle.active = false;
            else
            {
                if (liquid)
                    particle.velocity.y -= gravityStep;
                else if (fireParticles)
                {
                    float cooling = Mathf.SmoothStep(0f, 1f, particle.age / particle.lifetime);
                    float current = (Mathf.PerlinNoise(flowSeed + particle.position.y * 0.5f, currentTime) - 0.5f) * 2f;
                    float eddy = (Mathf.PerlinNoise(particle.seed, particle.age * 1.4f) - 0.5f) * 2f;
                    particle.velocity.x = Mathf.Lerp(particle.velocity.x,
                        (current + eddy * 0.5f) * spread * scale * travel,
                        1f - Mathf.Exp(-delta * (1.2f + cooling)));
                    particle.velocity.y += updraft *
                        (1f - cooling * 1.35f) * delta;
                    particle.velocity.y /= drag;
                }
                particle.position += particle.velocity * delta;
                if (curved)
                    VisibilityRadius = Mathf.Max(VisibilityRadius, (particle.position - origin).magnitude +
                        settings.visualCurves.Offset(particle.age / particle.lifetime).magnitude * scale +
                        particle.radius * 2f * expansion);
            }
            particles[i] = particle;
        }
        if (phase == EffectPhase.End || settings.emissionRate <= 0f) return;
        timer -= delta;
        for (int count = 0; timer <= 0f && count < capacity; count++)
        {
            Emit(origin);
            timer += Interval();
        }
        timer = Mathf.Max(0f, timer);
    }

    private float Interval()
    {
        float interval = Mathf.Max(EffectNumbers.Positive(settings.minimumInterval, 0.25f),
            1f / Mathf.Clamp(EffectNumbers.Positive(settings.emissionRate, 12f), 1f, 120f));
        if (IsCloud) interval = Mathf.Max(interval, EffectNumbers.Positive(definition.cloudDetailInterval, 0.9f));
        return interval;
    }

    private void Emit(Vector3 origin)
    {
        for (int attempt = 0; attempt < capacity; attempt++)
        {
            int index = cursor;
            cursor = (cursor + 1) % capacity;
            if (particles[index].active) continue;
            float minLife = EffectNumbers.Positive(settings.lifetime.x, 0.4f);
            float maxLife = EffectNumbers.Positive(settings.lifetime.y, 0.9f);
            float minRadius = EffectNumbers.Positive(settings.radius.x, 0.025f);
            float maxRadius = EffectNumbers.Positive(settings.radius.y, 0.055f);
            float radius = pixelStep * 0.5f;
            float lifetimeScale = 1f;
            float seed = 0f;
            int piece = emitted++;
            Vector2 direction;
            Vector2 offset;
            Vector2 drift;
            if (definition.fireParticles)
            {
                seed = UnityEngine.Random.Range(0f, 1000f);
                lifetimeScale = EffectNumbers.Positive(definition.emberLifetimeScale, 2.5f);
                float travel = EffectNumbers.Positive(definition.emberTravelScale, 1.8f);
                offset = new Vector2(UnityEngine.Random.Range(-footprint.x, footprint.x) * 0.6f,
                    footprint.y * UnityEngine.Random.Range(0f, 0.25f));
                drift = new Vector2(UnityEngine.Random.Range(-0.7f, 0.7f) * EffectNumbers.NonNegative(settings.spread),
                    settings.rise * UnityEngine.Random.Range(0.85f, 1.6f)) * (scale * travel);
            }
            else
            {
                float angle = IsCloud ? (piece % 2 == 0 ? 0.35f : Mathf.PI - 0.35f) : UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                offset = Vector2.Scale(direction, footprint) * (IsCloud ? 0.45f : 0.85f);
                drift = direction * (EffectNumbers.NonNegative(settings.spread) * scale * (IsCloud ? 0.1f : 1f));
                drift.y += settings.rise * scale;
                if (IsCloud)
                {
                    float baseRadius = UnityEngine.Random.Range(Mathf.Min(minRadius, maxRadius), Mathf.Max(minRadius, maxRadius)) * scale;
                    radius = Mathf.Max(baseRadius, Mathf.Max(footprint.x, footprint.y) * 0.75f) *
                        EffectNumbers.PieceScale(definition, piece % 3) * EffectNumbers.SpawnScale(definition);
                }
            }
            particles[index] = new Particle
            {
                active = true, position = origin + (Vector3)offset,
                velocity = drift, radius = radius,
                lifetime = UnityEngine.Random.Range(Mathf.Min(minLife, maxLife), Mathf.Max(minLife, maxLife)) * lifetimeScale,
                seed = seed
            };
            return;
        }
    }

    public bool Capture(int index, out Vector3 position, out Vector2 size, out Color color)
    {
        position = default;
        size = default;
        color = default;
        if (definition == null || index < 0 || index >= capacity || !particles[index].active) return false;
        Particle particle = particles[index];
        float progress = Mathf.Clamp01(particle.age / particle.lifetime);
        EffectVisualCurves curves = settings.visualCurves;
        float colorAge = curves?.ColorProgress(progress) ?? progress;
        position = particle.position + (Vector3)(curves?.Offset(progress) ?? Vector2.zero) * scale;
        size = Vector2.one * pixelStep;
        if (IsCloud)
            size = Vector2.one * (particle.radius * 2f * (1f + EffectNumbers.NonNegative(settings.expansion) * progress));
        else
        {
            position.x = (Mathf.Floor(position.x / pixelStep) + 0.5f) * pixelStep;
            position.y = (Mathf.Floor(position.y / pixelStep) + 0.5f) * pixelStep;
        }
        color = definition.fireParticles ? Color.Lerp(definition.emberStartColor, definition.emberEndColor, colorAge) : definition.tint;
        if (!definition.fireParticles && curves != null && curves.enabled)
            color *= Color.Lerp(Color.white, definition.spentTint, colorAge);
        float fadeStart = Mathf.Clamp(definition.emberFadeStart, 0f, 0.9f);
        float fade = definition.fireParticles
            ? Mathf.Clamp01((progress - fadeStart) / (1f - fadeStart))
            : progress;
        color.a *= (1f - Mathf.SmoothStep(0f, 1f, fade)) * endFade;
        color.a *= curves?.Opacity(progress) ?? 1f;
        if (IsCloud) color.a *= Mathf.SmoothStep(0f, 1f, progress / 0.3f) * Mathf.Clamp01(definition.cloudDetailOpacity);
        return color.a > 0.001f && size.x > 0.0001f;
    }

    public void Reset()
    {
        Array.Clear(particles, 0, particles.Length);
        definition = null;
        settings = null;
        capacity = cursor = emitted = 0;
        scale = timer = VisibilityRadius = 0f;
        pixelStep = flowTime = flowSeed = 0f;
        footprint = Vector2.zero;
        endFade = 1f;
    }
}

internal static class EffectFireField
{
    public static void Accumulate(float[] density, Color[] colors, int stride, Vector2 origin,
        float step, RectInt clip, EffectBlobSample sample, EffectDefinitionSO definition)
    {
        if (clip.width == 0 || clip.height == 0 || sample.radius.x <= 0f || sample.radius.y <= 0f) return;
        Vector2 center = ((Vector2)sample.center - origin) / step;
        Vector2 radius = sample.radius / step;
        int left = Mathf.Clamp(Mathf.FloorToInt(center.x - radius.x * 2f), clip.xMin, clip.xMax - 1);
        int right = Mathf.Clamp(Mathf.CeilToInt(center.x + radius.x * 2f), clip.xMin, clip.xMax - 1);
        int bottom = Mathf.Clamp(Mathf.FloorToInt(center.y - radius.y * 2f), clip.yMin, clip.yMax - 1);
        int top = Mathf.Clamp(Mathf.CeilToInt(center.y + radius.y * 2f), clip.yMin, clip.yMax - 1);
        float holeSize = sample.fireHoleRadius * sample.fireHoleRadius;
        float inverseHoleSoftness = 1f / Mathf.Max(0.001f, holeSize * 0.45f);
        float edgeWidth = Mathf.Clamp(definition.fireEdgeWidth, 0.01f, 0.6f);
        float edgeStrength = Mathf.Clamp01(definition.fireEdgeStrength);
        Color edgeColor = Color.Lerp(definition.fireSootColor * definition.tint, sample.fireColor, 0.2f);
        edgeColor.a = sample.fireColor.a;
        float smokeCoverage = Mathf.Lerp(1f, Mathf.Sqrt(Mathf.Clamp01(sample.fireColor.a)), sample.fireSmoke);
        float smokeEdgeFade = sample.fireSmoke * EffectNumbers.NonNegative(definition.fireSmokeEdgeFade) * 8f;
        for (int y = bottom; y <= top; y++)
        {
            float dy = (y + 0.5f - center.y) / radius.y;
            int row = y * stride;
            for (int x = left; x <= right; x++)
            {
                float dx = (x + 0.5f - center.x) / radius.x;
                float along = dx * sample.fireDirection.x + dy * sample.fireDirection.y;
                float across = dx * sample.fireDirection.y - dy * sample.fireDirection.x;
                float width = Mathf.Lerp(1f - sample.fireTaper, 1f, Mathf.Clamp01((along + 1.6f) / 2.6f));
                across /= width;
                float roundDistance = across * across + along * along;
                float pointedDistance = Mathf.Abs(across) + Mathf.Abs(along);
                float distance = Mathf.Lerp(roundDistance, pointedDistance * pointedDistance, sample.fireTaper * 0.45f);
                float edge = Mathf.Max(0f, 1f - distance * 0.25f);
                float contour = sample.fireShape.x * dx + sample.fireShape.y * dy + 0.25f *
                    (sample.fireShape.x * dx * (dx * dx - 3f * dy * dy) +
                    sample.fireShape.y * dy * (3f * dx * dx - dy * dy));
                edge *= Mathf.Clamp(1f + contour, 0.2f, 1.8f);
                float contribution = edge * edge * edge;
                float border = 1f - Mathf.Clamp01(edge);
                contribution /= 1f + border * border * smokeEdgeFade;
                if (contribution <= 0f) continue;
                Color color = sample.fireColor;
                if (definition.ResolvedSorting == EffectSortMode.Attached && sample.fireSmoke < 0.5f)
                {
                    float heat = Mathf.Clamp01(1f - distance * 0.9f) * (1f - sample.fireSmoke * 2f);
                    Color core = Color.Lerp(definition.fireGoldColor, definition.fireHotColor,
                        Mathf.SmoothStep(0.3f, 0.85f, heat)) * definition.tint;
                    float alpha = color.a;
                    color = Color.Lerp(color, core, heat);
                    color.a = alpha;
                }
                if (sample.fireTear > 0f)
                {
                    float holeX = dx - sample.fireHole.x;
                    float holeY = dy - sample.fireHole.y;
                    float secondX = dx + sample.fireHole.x + 0.35f;
                    float secondY = dy - sample.fireHole.y - 0.65f;
                    float holeDistance = Mathf.Min(holeX * holeX + holeY * holeY,
                        (secondX * secondX + secondY * secondY) * 1.4f);
                    float cut = Mathf.Clamp01((holeSize - holeDistance) * inverseHoleSoftness);
                    // Tear this particle before merging so overlapping flames keep their own coverage.
                    contribution *= 1f - Mathf.Clamp01(cut * sample.fireTear);
                    if (contribution <= 0f) continue;
                    if (cut > 0f)
                    {
                        float outline = 1f - Mathf.SmoothStep(0f, 1f, (contribution - 0.15f) / edgeWidth);
                        color = Color.Lerp(color, edgeColor, outline * edgeStrength);
                    }
                }
                int index = row + x;
                float coverage = contribution * smokeCoverage;
                density[index] += coverage;
                colors[index] += color * coverage;
            }
        }
    }

    public static void Colorize(float[] density, Color[] colors, Color32[] pixels,
        int resolution, RectInt clip, EffectDefinitionSO definition, float roughness = 0f)
    {
        float softness = Mathf.Clamp(definition.mergeSoftness, 0.02f, 0.5f) * Mathf.Lerp(1f, 0.08f, roughness);
        float edgeWidth = Mathf.Clamp(definition.fireEdgeWidth, 0.01f, 0.6f);
        float edgeStrength = Mathf.Clamp01(definition.fireEdgeStrength);
        Color hot = definition.fireHotColor * definition.tint;
        Color gold = definition.fireGoldColor * definition.tint;
        Color warm = definition.fireWarmColor * definition.tint;
        Color ember = definition.fireEmberColor * definition.tint;
        Color smoke = definition.fireSmokeColor * definition.tint;
        Color soot = definition.fireSootColor * definition.tint;
        EffectFirePaletteWeights weights = definition.firePaletteWeights;
        float hotWeight = Mathf.Clamp(EffectNumbers.NonNegative(weights?.hot ?? 1f), 0f, 4f);
        float goldWeight = Mathf.Clamp(EffectNumbers.NonNegative(weights?.gold ?? 1f), 0f, 4f);
        float warmWeight = Mathf.Clamp(EffectNumbers.NonNegative(weights?.warm ?? 1f), 0f, 4f);
        float emberWeight = Mathf.Clamp(EffectNumbers.NonNegative(weights?.ember ?? 1f), 0f, 4f);
        float smokeWeight = Mathf.Clamp(EffectNumbers.NonNegative(weights?.smoke ?? 1f), 0f, 4f);
        float sootWeight = Mathf.Clamp(EffectNumbers.NonNegative(weights?.soot ?? 1f), 0f, 4f);
        int stride = resolution + 2;
        for (int y = clip.yMin; y < clip.yMax; y++)
        {
            int fieldRow = (y + 1) * stride;
            int pixelRow = y * resolution;
            for (int x = clip.xMin; x < clip.xMax; x++)
            {
                int fieldIndex = fieldRow + x + 1;
                float value = density[fieldIndex];
                if (value <= 0.15f) continue;
                Color color = colors[fieldIndex] / value;
                float outline = 1f - Mathf.SmoothStep(0f, 1f, (value - 0.15f) / edgeWidth);
                Color edgeColor = Color.Lerp(soot, color, 0.2f);
                edgeColor.a = color.a;
                color = Color.Lerp(color, edgeColor, outline * edgeStrength);
                Color result = ember;
                float closest = float.PositiveInfinity;
                SelectColor(hot, color, hotWeight, ref result, ref closest);
                SelectColor(gold, color, goldWeight, ref result, ref closest);
                SelectColor(warm, color, warmWeight, ref result, ref closest);
                SelectColor(ember, color, emberWeight, ref result, ref closest);
                SelectColor(smoke, color, smokeWeight, ref result, ref closest);
                SelectColor(soot, color, sootWeight, ref result, ref closest);
                result.a = color.a;
                color = result;
                color.a *= Mathf.SmoothStep(0f, 1f, (value - 0.15f) / softness);
                pixels[pixelRow + x] = color;
            }
        }
    }

    private static void SelectColor(Color candidate, Color target, float weight, ref Color result, ref float closest)
    {
        if (weight <= 0f) return;
        float red = candidate.r - target.r;
        float green = candidate.g - target.g;
        float blue = candidate.b - target.b;
        float distance = (red * red + green * green + blue * blue + 0.01f) / weight;
        if (distance >= closest) return;
        closest = distance;
        result = candidate;
    }
}

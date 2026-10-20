using System;
using UnityEngine;

internal sealed class EffectFireParticles
{
    private const int MaxParticles = 96;
    private const int MaxSources = 3;
    private struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float age, lifetime, radius, seed, endAge;
        public bool active;
    }

    private readonly Particle[] particles = new Particle[MaxParticles];
    private readonly Vector2[] sourceOffsets = new Vector2[MaxSources];
    private readonly float[] sourceScales = new float[MaxSources];
    private EffectDefinitionSO definition;
    private int cursor;
    private int sourceCount, nextSource;
    private float scale, timer, endProgress;
    private bool ending;
    public int Capacity { get; private set; }

    public void Play(EffectDefinitionSO data, Vector3 origin, Vector3 velocity, float size)
    {
        Reset();
        definition = data;
        scale = size;
        Capacity = Mathf.Clamp(data.fireParticleCapacity, 8, MaxParticles);
        int minSources = Mathf.Clamp(Mathf.Min(data.fireSourceCount.x, data.fireSourceCount.y), 2, MaxSources);
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
        if (phase == EffectPhase.End && !ending)
        {
            ending = true;
            for (int i = 0; i < Capacity; i++) particles[i].endAge = NormalizedAge(particles[i]);
        }
        endProgress = ending ? Mathf.Clamp01(progress) : 0f;
        for (int i = 0; i < Capacity; i++)
        {
            Particle particle = particles[i];
            if (!particle.active) continue;
            particle.age += delta;
            float t = NormalizedAge(particle);
            if (ending) t = Mathf.Max(t, Mathf.Lerp(particle.endAge, 1f, endProgress));
            if (t >= 1f) { particle.active = false; particles[i] = particle; continue; }
            float wander = (Mathf.PerlinNoise(particle.seed, particle.age * 1.7f) - 0.5f) * 2f;
            float spread = EffectNumbers.NonNegative(definition.fireSpread) * scale;
            float smoke = Mathf.SmoothStep(0f, 1f, (t - 0.4f) / 0.45f);
            Vector3 drift = particle.velocity * Mathf.Lerp(1f, 0.7f, smoke);
            drift.x += wander * spread * (Mathf.Lerp(0.25f, 1f, t) +
                EffectNumbers.NonNegative(definition.fireSmokeSpread) * smoke);
            drift.y += (Mathf.PerlinNoise(particle.seed + 71f, particle.age * 1.3f) - 0.5f) *
                spread * smoke;
            particle.position += drift * delta;
            particles[i] = particle;
        }
        if (ending || definition.fireEmissionRate <= 0f) return;
        timer -= delta;
        for (int emitted = 0; timer <= 0f && emitted < Capacity; emitted++)
        {
            Emit(origin, velocity);
            timer += Interval();
        }
        timer = Mathf.Max(0f, timer);
    }

    private float Interval() => UnityEngine.Random.Range(0.7f, 1.3f) /
        Mathf.Clamp(EffectNumbers.Positive(definition.fireEmissionRate, 32f), 1f, 240f);

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
                position = origin + new Vector3(sourceOffset.x + jitter.x, sourceOffset.y + jitter.y, 0f),
                velocity = velocity * 0.2f + new Vector3(UnityEngine.Random.Range(-spread, spread),
                    EffectNumbers.NonNegative(definition.gasRise) * sourceScale * UnityEngine.Random.Range(0.65f, 1.4f), 0f),
                lifetime = UnityEngine.Random.Range(Mathf.Min(minLife, maxLife), Mathf.Max(minLife, maxLife)),
                radius = radius,
                seed = UnityEngine.Random.Range(0f, 1000f)
            };
            return;
        }
    }

    private static float NormalizedAge(Particle particle) => particle.lifetime > 0f
        ? Mathf.Clamp01(particle.age / particle.lifetime) : 1f;

    public bool Capture(int index, out EffectBlobSample sample)
    {
        sample = default;
        if (definition == null || index < 0 || index >= Capacity) return false;
        Particle particle = particles[index];
        if (!particle.active || particle.radius <= 0f) return false;
        float t = NormalizedAge(particle);
        if (ending) t = Mathf.Max(t, Mathf.Lerp(particle.endAge, 1f, endProgress));
        Color color = t < 0.35f
            ? Color.Lerp(definition.fireHotColor, definition.fireWarmColor, Mathf.SmoothStep(0f, 1f, t / 0.35f))
            : Color.Lerp(definition.fireWarmColor, definition.fireSootColor,
                Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 0.35f));
        float fadeStart = Mathf.Clamp(definition.fireFadeStart, 0f, 0.9f);
        color.a = (1f - Mathf.SmoothStep(0f, 1f, (t - fadeStart) / (1f - fadeStart))) *
            Mathf.Clamp01(definition.tint.a);
        color.r *= definition.tint.r;
        color.g *= definition.tint.g;
        color.b *= definition.tint.b;
        float smoke = Mathf.SmoothStep(0f, 1f, (t - 0.4f) / 0.45f);
        float growth = 1f + EffectNumbers.NonNegative(definition.gasExpansion) * t +
            EffectNumbers.NonNegative(definition.fireSmokeExpansion) * smoke;
        float cycle = particle.age * EffectNumbers.Positive(definition.variationFrequency, 1.5f) * 6f;
        growth *= 1f + Mathf.Sin(particle.seed + cycle) *
            Mathf.Clamp(definition.fireTurbulence, 0f, 0.6f) * Mathf.Lerp(0.35f, 1f, smoke);
        float separation = Mathf.Lerp(1f, 0.55f, Mathf.SmoothStep(0f, 1f, (t - 0.65f) / 0.35f));
        sample.center = particle.position;
        sample.radius = new Vector2(Mathf.Lerp(1f, 1.25f, smoke), Mathf.Lerp(1f, 0.9f, smoke)) *
            (particle.radius * growth * separation);
        sample.radius = Vector2.Scale(sample.radius, new Vector2(
            EffectNumbers.Positive(definition.fireShapeScale.x, 1f),
            EffectNumbers.Positive(definition.fireShapeScale.y, 1.35f)));
        float angle = particle.seed + particle.age * 0.8f;
        sample.fireShape = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) *
            (Mathf.Clamp(definition.fireContourVariation, 0f, 0.4f) * Mathf.Lerp(0.35f, 1f, smoke));
        Vector2 direction = new(particle.velocity.x / sample.radius.x, particle.velocity.y / sample.radius.y);
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

    public void Reset()
    {
        Array.Clear(particles, 0, particles.Length);
        Array.Clear(sourceOffsets, 0, sourceOffsets.Length);
        Array.Clear(sourceScales, 0, sourceScales.Length);
        definition = null;
        Capacity = cursor = 0;
        sourceCount = nextSource = 0;
        scale = timer = endProgress = 0f;
        ending = false;
    }
}

internal sealed class EffectDetailParticles
{
    public const int MaxParticles = 32;
    private const float WaterGravity = 4f;
    private struct Particle
    {
        public Vector3 position, velocity;
        public float age, lifetime, radius;
        public bool active;
    }

    private readonly Particle[] particles = new Particle[MaxParticles];
    private EffectDefinitionSO definition;
    private EffectDetailSettings settings;
    private float scale, timer, endFade = 1f;
    private int capacity, cursor, emitted;
    public float VisibilityRadius { get; private set; }

    public void Play(EffectDefinitionSO data, EffectDetailSettings detailSettings, Vector3 origin, float size)
    {
        Reset();
        definition = data;
        settings = detailSettings;
        scale = size;
        endFade = 1f;
        capacity = Mathf.Clamp(settings.capacity, 1, MaxParticles);
        float life = Mathf.Max(EffectNumbers.Positive(settings.lifetime.x, 0.4f),
            EffectNumbers.Positive(settings.lifetime.y, 0.9f));
        VisibilityRadius = (EffectNumbers.NonNegative(settings.spread) + Mathf.Abs(settings.rise)) * life * scale +
            Mathf.Max(EffectNumbers.Positive(settings.radius.x, 0.025f), EffectNumbers.Positive(settings.radius.y, 0.055f)) *
            3f * (1f + EffectNumbers.NonNegative(settings.expansion)) * scale;
        if (data.family == EffectFamily.Liquid && !data.fireParticles)
            VisibilityRadius += 0.5f * WaterGravity * scale * life * life;
        for (int i = 0; i < Mathf.Min(3, capacity); i++) Emit(origin);
    }

    public void Tick(float delta, Vector3 origin, EffectPhase phase, float progress)
    {
        if (definition == null) return;
        endFade = phase == EffectPhase.End ? 1f - Mathf.Clamp01(progress) : 1f;
        for (int i = 0; i < capacity; i++)
        {
            Particle particle = particles[i];
            if (!particle.active) continue;
            particle.age += delta;
            if (particle.age >= particle.lifetime) particle.active = false;
            else
            {
                if (definition.family == EffectFamily.Liquid && !definition.fireParticles)
                    particle.velocity.y -= WaterGravity * scale * delta;
                particle.position += particle.velocity * delta;
            }
            particles[i] = particle;
        }
        if (phase == EffectPhase.End || settings.emissionRate <= 0f) return;
        timer -= delta;
        float interval = 1f / Mathf.Clamp(EffectNumbers.Positive(settings.emissionRate, 12f), 1f, 120f);
        for (int count = 0; timer <= 0f && count < capacity; count++)
        {
            Emit(origin);
            timer += interval;
        }
        timer = Mathf.Max(0f, timer);
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
            float radius = UnityEngine.Random.Range(Mathf.Min(minRadius, maxRadius), Mathf.Max(minRadius, maxRadius)) *
                EffectNumbers.PieceScale(definition, emitted++ % 3) * EffectNumbers.SpawnScale(definition) * scale;
            Vector2 direction = UnityEngine.Random.insideUnitCircle.normalized;
            Vector2 drift = direction * (EffectNumbers.NonNegative(settings.spread) * scale);
            drift.y += settings.rise * scale;
            particles[index] = new Particle
            {
                active = true, position = origin + (Vector3)(direction * radius),
                velocity = drift, radius = radius,
                lifetime = UnityEngine.Random.Range(Mathf.Min(minLife, maxLife), Mathf.Max(minLife, maxLife))
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
        position = particle.position;
        size = Vector2.one * (particle.radius * 2f * (1f + EffectNumbers.NonNegative(settings.expansion) * progress));
        if (definition.family == EffectFamily.Liquid && !definition.fireParticles) size.y *= 1.3f;
        color = definition.fireParticles ? Color.Lerp(definition.emberStartColor, definition.emberEndColor, progress) : definition.tint;
        color.a *= (1f - Mathf.SmoothStep(0f, 1f, progress)) * endFade;
        return color.a > 0.001f && size.x > 0.0001f;
    }

    public void Reset()
    {
        Array.Clear(particles, 0, particles.Length);
        definition = null;
        settings = null;
        capacity = cursor = emitted = 0;
        scale = timer = VisibilityRadius = 0f;
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
        for (int y = bottom; y <= top; y++)
            for (int x = left; x <= right; x++)
            {
                float dx = (x + 0.5f - center.x) / radius.x;
                float dy = (y + 0.5f - center.y) / radius.y;
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
                if (contribution <= 0f) continue;
                Color color = sample.fireColor;
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
                int index = y * stride + x;
                density[index] += contribution;
                colors[index] += color * contribution;
            }
    }

    public static void Colorize(float[] density, Color[] colors, Color32[] pixels,
        int resolution, RectInt clip, EffectDefinitionSO definition, float roughness = 0f)
    {
        float softness = Mathf.Clamp(definition.mergeSoftness, 0.02f, 0.5f) * Mathf.Lerp(1f, 0.08f, roughness);
        float edgeWidth = Mathf.Clamp(definition.fireEdgeWidth, 0.01f, 0.6f);
        float edgeStrength = Mathf.Clamp01(definition.fireEdgeStrength);
        int stride = resolution + 2;
        for (int y = clip.yMin; y < clip.yMax; y++)
            for (int x = clip.xMin; x < clip.xMax; x++)
            {
                int fieldIndex = (y + 1) * stride + x + 1;
                float value = density[fieldIndex];
                if (value <= 0.15f) continue;
                Color color = colors[fieldIndex] / value;
                float outline = 1f - Mathf.SmoothStep(0f, 1f, (value - 0.15f) / edgeWidth);
                Color edgeColor = Color.Lerp(definition.fireSootColor * definition.tint, color, 0.2f);
                edgeColor.a = color.a;
                color = Color.Lerp(color, edgeColor, outline * edgeStrength);
                color = PaletteColor(color, definition);
                color.a *= Mathf.SmoothStep(0f, 1f, (value - 0.15f) / softness);
                pixels[y * resolution + x] = color;
            }
    }

    private static Color PaletteColor(Color color, EffectDefinitionSO definition)
    {
        Color result = color;
        float closest = float.PositiveInfinity;
        SelectColor(definition.fireHotColor * definition.tint, color, ref result, ref closest);
        SelectColor(definition.fireGoldColor * definition.tint, color, ref result, ref closest);
        SelectColor(definition.fireWarmColor * definition.tint, color, ref result, ref closest);
        SelectColor(definition.fireEmberColor * definition.tint, color, ref result, ref closest);
        SelectColor(definition.fireSmokeColor * definition.tint, color, ref result, ref closest);
        SelectColor(definition.fireSootColor * definition.tint, color, ref result, ref closest);
        result.a = color.a;
        return result;
    }

    private static void SelectColor(Color candidate, Color target, ref Color result, ref float closest)
    {
        float red = candidate.r - target.r;
        float green = candidate.g - target.g;
        float blue = candidate.b - target.b;
        float distance = red * red + green * green + blue * blue;
        if (distance >= closest) return;
        closest = distance;
        result = candidate;
    }
}

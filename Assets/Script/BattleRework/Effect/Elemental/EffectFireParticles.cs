using System;
using UnityEngine;

internal sealed class EffectFireParticles
{
    private const int MaxParticles = 96;
    private struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float age, lifetime, radius, seed, endAge;
        public bool active;
    }

    private readonly Particle[] particles = new Particle[MaxParticles];
    private EffectDefinitionSO definition;
    private int cursor;
    private float scale, timer, endProgress;
    private bool ending;
    public int Capacity { get; private set; }

    public void Play(EffectDefinitionSO data, Vector3 origin, Vector3 velocity, float size)
    {
        Reset();
        definition = data;
        scale = size;
        Capacity = Mathf.Clamp(data.fireParticleCapacity, 8, MaxParticles);
        int initial = Mathf.Clamp(data.pieceCount, 1, 5);
        initial = UnityEngine.Random.Range(Mathf.Max(1, initial - 1), initial + 1);
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
            float minLife = EffectNumbers.Positive(definition.fireParticleLifetime.x, 0.8f);
            float maxLife = EffectNumbers.Positive(definition.fireParticleLifetime.y, 1.3f);
            float minRadius = EffectNumbers.Positive(definition.fireParticleRadius.x, 0.09f);
            float maxRadius = EffectNumbers.Positive(definition.fireParticleRadius.y, 0.19f);
            float radius = UnityEngine.Random.Range(Mathf.Min(minRadius, maxRadius), Mathf.Max(minRadius, maxRadius)) * scale;
            float spread = EffectNumbers.NonNegative(definition.fireSpread) * scale;
            Vector2 jitter = UnityEngine.Random.insideUnitCircle * radius * 0.6f;
            particles[index] = new Particle
            {
                active = true,
                position = origin + new Vector3(jitter.x, jitter.y, 0f),
                velocity = velocity * 0.2f + new Vector3(UnityEngine.Random.Range(-spread, spread),
                    EffectNumbers.NonNegative(definition.gasRise) * scale * UnityEngine.Random.Range(0.65f, 1.4f), 0f),
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
        definition = null;
        Capacity = cursor = 0;
        scale = timer = endProgress = 0f;
        ending = false;
    }
}

internal static class EffectFireField
{
    public static void Accumulate(float[] density, float[] erosion, Color[] colors, int stride, Vector2 origin,
        float step, RectInt clip, EffectBlobSample sample)
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
                int index = y * stride + x;
                density[index] += contribution;
                colors[index] += sample.fireColor * contribution;
                if (sample.fireTear <= 0f) continue;
                float holeX = dx - sample.fireHole.x;
                float holeY = dy - sample.fireHole.y;
                float secondX = dx + sample.fireHole.x + 0.35f;
                float secondY = dy - sample.fireHole.y - 0.65f;
                float holeDistance = Mathf.Min(holeX * holeX + holeY * holeY,
                    (secondX * secondX + secondY * secondY) * 1.4f);
                float cut = Mathf.Clamp01((holeSize - holeDistance) * inverseHoleSoftness);
                erosion[index] = Mathf.Max(erosion[index], cut * sample.fireTear);
            }
    }

    public static void Colorize(float[] density, float[] erosion, Color[] colors, Color32[] pixels,
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
                float rawValue = density[fieldIndex];
                if (rawValue <= 0.15f) continue;
                float value = rawValue * (1f - Mathf.Clamp01(erosion[fieldIndex]));
                if (value <= 0.15f) continue;
                Color color = colors[fieldIndex] / rawValue;
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

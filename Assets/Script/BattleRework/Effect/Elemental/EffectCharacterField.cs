using UnityEngine;

internal static class EffectCharacterField
{
    public static void Accumulate(float[] density, Color[] colors, int stride, Vector2 origin,
        float step, RectInt clip, EffectBlobSample sample, EffectCharacterSettings settings)
    {
        if (clip.width == 0 || clip.height == 0 || sample.radius.x <= 0f || sample.radius.y <= 0f) return;
        Vector2 radius = sample.radius / step;
        Vector2 center = ((Vector2)sample.center - origin) / step;
        float sway = (Mathf.PerlinNoise(sample.shapeSeed, sample.shapeAge * 2f) - 0.5f) * 0.3f;
        center.x += sway * radius.x;
        int left = Mathf.Clamp(Mathf.FloorToInt(center.x - radius.x * 2f), clip.xMin, clip.xMax - 1);
        int right = Mathf.Clamp(Mathf.CeilToInt(center.x + radius.x * 2f), clip.xMin, clip.xMax - 1);
        int bottom = Mathf.Clamp(Mathf.FloorToInt(center.y - radius.y * 2f), clip.yMin, clip.yMax - 1);
        int top = Mathf.Clamp(Mathf.CeilToInt(center.y + radius.y * 2f), clip.yMin, clip.yMax - 1);
        float softness = Mathf.Clamp(0.35f / Mathf.Min(radius.x, radius.y), 0.03f, 0.45f);
        for (int y = bottom; y <= top; y++)
            for (int x = left; x <= right; x++)
            {
                Vector2 point = new((x + 0.5f - center.x) / radius.x, (y + 0.5f - center.y) / radius.y);
                Color color = settings.shape == EffectCharacterShape.PoisonBubble
                    ? Bubble(point, softness, sample, settings)
                    : Ice(point, softness, sample, settings);
                color *= sample.fireColor;
                float alpha = Mathf.Clamp01(color.a);
                if (alpha <= 0f) continue;
                int index = y * stride + x;
                colors[index] = color * alpha + colors[index] * (1f - alpha);
                density[index] = alpha + density[index] * (1f - alpha);
            }
    }

    private static Color Bubble(Vector2 point, float softness, EffectBlobSample sample, EffectCharacterSettings settings)
    {
        float pop = sample.dissolve;
        float growth = Mathf.Lerp(0.3f, 0.92f, Mathf.SmoothStep(0f, 1f, sample.impact));
        float wobble = Mathf.Sin(sample.shapeSeed + sample.shapeAge * 9f) * 0.07f;
        Vector2 bubble = new(point.x / (growth + wobble), point.y / (growth - wobble));
        float distance = bubble.magnitude;
        float body = Coverage(distance, 1f, softness);
        float opening = Mathf.SmoothStep(0f, 1f, pop / 0.25f);
        float shell = Coverage(Mathf.Abs(distance - 0.85f), 0.16f, softness);
        shell *= 1f - Mathf.SmoothStep(-0.6f, 0.4f, bubble.y + opening);
        body = Mathf.Lerp(body, shell, opening);
        float fade = 1f - Mathf.SmoothStep(0f, 0.65f, pop);
        Color color = Color.Lerp(settings.shapeCore, settings.shapeEdge, Mathf.SmoothStep(0.45f, 1f, distance));
        float highlight = Coverage((bubble - new Vector2(-0.28f, 0.3f)).magnitude, 0.2f, softness);
        color = Color.Lerp(color, settings.shapeHighlight, highlight * 0.8f);
        color.a *= body * fade * 0.9f;
        for (int i = 0; i < 3 && pop > 0f; i++)
        {
            float random = Mathf.PerlinNoise(sample.shapeSeed + i * 17f, 0f);
            float side = i == 0 ? -1f : i == 1 ? 1f : random - 0.5f;
            Vector2 offset = new(side * (0.4f + pop * (0.5f + random)),
                0.2f + pop * (0.7f + random * 0.8f));
            float chip = Coverage((point - offset).magnitude, 0.13f * (1f - pop), softness);
            Color droplet = Color.Lerp(settings.shapeCore, settings.shapeEdge, random);
            float alpha = chip * (1f - pop) * (1f - pop) * 0.5f * opening;
            if (alpha <= color.a) continue;
            color = droplet;
            color.a *= alpha;
        }
        return color;
    }

    private static Color Ice(Vector2 point, float softness, EffectBlobSample sample, EffectCharacterSettings settings)
    {
        float growth = Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f, sample.impact));
        float fade = 1f - Mathf.SmoothStep(0f, 1f, sample.dissolve);
        point /= Mathf.Max(0.05f, growth * Mathf.Lerp(1f, 0.7f, sample.dissolve));
        float coverage;
        float core;
        if (settings.shape == EffectCharacterShape.FrostSpark)
        {
            float cross = Mathf.Min(Mathf.Abs(point.x) * 4f + Mathf.Abs(point.y),
                Mathf.Abs(point.y) * 4f + Mathf.Abs(point.x));
            coverage = Coverage(cross, 1f, softness);
            core = Coverage(point.magnitude, 0.25f, softness);
        }
        else
        {
            float tilt = (Mathf.PerlinNoise(sample.shapeSeed, 1f) - 0.5f) * 0.4f;
            point.x -= point.y * tilt;
            float tip = 0.85f + Mathf.PerlinNoise(sample.shapeSeed, 2f) * 0.3f;
            float main = Shard(point, -0.2f, tip, 0.65f, softness);
            float side = Shard(point, 0.5f, tip * 0.35f, 0.4f, softness);
            coverage = Mathf.Max(main, side);
            core = Coverage(Mathf.Abs(point.x + 0.2f + point.y * 0.08f), 0.18f, softness) * main;
        }
        Color color = Color.Lerp(settings.shapeEdge, settings.shapeCore, core);
        color.a *= coverage * growth * fade;
        return color;
    }

    private static float Shard(Vector2 point, float center, float tip, float width, float softness)
    {
        float sides = Mathf.Abs(point.x - center) + width * (point.y + 0.75f) / (tip + 0.75f);
        return Coverage(sides, width, softness) * Mathf.Clamp01((point.y + 0.75f) / softness);
    }

    private static float Coverage(float distance, float radius, float softness) =>
        Mathf.Clamp01((radius - distance) / Mathf.Max(0.001f, softness) + 0.5f);

    public static void Colorize(float[] density, Color[] colors, Color32[] pixels, int resolution, RectInt clip)
    {
        int stride = resolution + 2;
        for (int y = clip.yMin; y < clip.yMax; y++)
            for (int x = clip.xMin; x < clip.xMax; x++)
            {
                int index = (y + 1) * stride + x + 1;
                float alpha = density[index];
                if (alpha <= 0f) continue;
                Color color = colors[index] / alpha;
                color.a = alpha;
                pixels[y * resolution + x] = color;
            }
    }
}

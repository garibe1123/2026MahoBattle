#ifndef BATTLE_UI_SCREEN_TONE_INCLUDED
#define BATTLE_UI_SCREEN_TONE_INCLUDED
float4 _ToneDots, _TonePattern, _ToneVignette, _ToneColor;

// Input and output are premultiplied; coordinates use screen pixels / Canvas scale.
float4 BattleScreenTone(float4 source, float2 uv, float2 screenSize, float protection)
{
    float strength = _TonePattern.z * (1.0 - saturate(protection)) * saturate(_ToneColor.a);
    if (strength <= 0.0 || source.a <= 0.00001 || _ToneDots.z <= 0.0) return source;
    float2 pixels = uv * screenSize / _TonePattern.w;
    float2 rotated = float2(pixels.x * _TonePattern.x - pixels.y * _TonePattern.y,
                            pixels.x * _TonePattern.y + pixels.y * _TonePattern.x);
    float spacing = _ToneDots.y * _ToneDots.w;
    float2 grid = rotated / spacing;
    float2 cell = floor(grid);
    float3 color = source.rgb / max(source.a, 0.00001);
    float darkness = 1.0 - saturate(dot(color, float3(0.2126, 0.7152, 0.0722)));
    float2 centered = (uv - 0.5) * screenSize * 2.0 / max(1.0, min(screenSize.x, screenSize.y));
    float edge = smoothstep(_ToneVignette.y, _ToneVignette.y + _ToneVignette.z, length(centered));
    float coverage = saturate(source.a * (max(0.04, darkness) + edge * _ToneVignette.x));
    float radius = _ToneDots.x * _ToneDots.w * sqrt(coverage);
    float distanceToCenter = length((frac(grid) - 0.5) * spacing);
    float aa = max(fwidth(distanceToCenter), 0.001);
    float dots = (1.0 - smoothstep(radius - aa * 0.5, radius + aa * 0.5, distanceToCenter));
    if (_ToneDots.z < 1.0)
    {
        dots = 0.0;
        // Include adjacent cells so sparse circles retain round edges across cell boundaries.
        float sparseRadius = min(radius, spacing * 0.707107);
        [unroll] for (int row = -1; row <= 1; row++)
        {
            [unroll] for (int column = -1; column <= 1; column++)
            {
                float2 neighbor = float2(column, row);
                float hash = frac(sin(dot(cell + neighbor, float2(127.1, 311.7))) * 43758.5453);
                float distanceToDot = length((frac(grid) - 0.5 - neighbor) * spacing);
                float circle = 1.0 - smoothstep(sparseRadius - aa * 0.5, sparseRadius + aa * 0.5, distanceToDot);
                dots = max(dots, circle * step(hash, _ToneDots.z));
            }
        }
    }
    dots *= step(0.00001, radius);
    float alpha = lerp(source.a, dots * saturate(source.a / max(coverage, 0.00001)),
                       (1.0 - source.a) * darkness);
    float3 ink = lerp(color, _ToneColor.rgb, dots);
    return lerp(source, float4(ink * alpha, alpha), strength);
}
#endif

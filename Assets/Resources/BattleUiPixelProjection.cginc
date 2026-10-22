#ifndef BATTLE_UI_PIXEL_PROJECTION_INCLUDED
#define BATTLE_UI_PIXEL_PROJECTION_INCLUDED
float _PixelSize;

float2 BattlePixelOffset(float2 screen, float pixel)
{
    float size = max(1.0, pixel > 0.0 ? pixel : _PixelSize);
    float2 origin = 0.0;
    #if UNITY_UV_STARTS_AT_TOP
    origin.y = _ScreenParams.y;
    #endif
    return (floor((screen - origin) / size) + 0.5) * size + origin - screen;
}

void BattlePixelCoverage(float2 barycentric, float3 edges, float2 offset)
{
    float2 bary = barycentric + ddx(barycentric) * offset.x + ddy(barycentric) * offset.y;
    float3 coverage = float3(bary, 1.0 - bary.x - bary.y) + edges * 0.00001;
    clip(min(coverage.x, min(coverage.y, coverage.z)));
}
#endif

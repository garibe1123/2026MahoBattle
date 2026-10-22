#ifndef BATTLE_UI_PIXEL_PROJECTION_INCLUDED
#define BATTLE_UI_PIXEL_PROJECTION_INCLUDED
float _PixelSize;

float2 BattlePixelOffset(float2 local, float pixel)
{
    float size = pixel > 0.0 ? pixel : max(1.0, _PixelSize);
    float2 offset = (floor(local / size) + 0.5) * size - local;
    float2 dx = ddx(local), dy = ddy(local);
    float determinant = dx.x * dy.y - dx.y * dy.x;
    if (abs(determinant) < 0.00000001) return float2(0.0, 0.0);
    return float2(offset.x * dy.y - offset.y * dy.x,
        dx.x * offset.y - dx.y * offset.x) / determinant;
}

void BattlePixelCoverage(float2 barycentric, float3 edges, float2 offset)
{
    float2 bary = barycentric + ddx(barycentric) * offset.x + ddy(barycentric) * offset.y;
    float3 coverage = float3(bary, 1.0 - bary.x - bary.y) + edges * 0.00001;
    clip(min(coverage.x, min(coverage.y, coverage.z)));
}
#endif

Shader "UI/BattleAnalogTvOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Strength ("Strength", Range(0,1)) = 1
        _ScanlineStrength ("Scanline Strength", Range(0,0.25)) = 0.035
        _ScanlineSpacing ("Scanline Spacing Pixels", Range(1,12)) = 3
        _NoiseStrength ("Noise Strength", Range(0,0.15)) = 0.018
        _RollingBandStrength ("Rolling Band Strength", Range(0,0.15)) = 0.018
        _OverlayTint ("Overlay Tint", Color) = (0.015,0.02,0.025,1)
        _BattleUiUnscaledTime ("Unscaled Time", Float) = 0
        _Curvature ("CRT Curvature", Range(0,0.15)) = 0.035
        _EdgeWidth ("CRT Edge Width", Range(0.01,0.25)) = 0.1
        _EdgeStrength ("CRT Edge Strength", Range(0,1)) = 0.86
        _CenterGlow ("CRT Center Glow", Range(0,0.1)) = 0.025
        _GlassTint ("CRT Glass Tint", Color) = (0.40,0.46,0.60,1)
        _GlassTintStrength ("CRT Glass Tint Strength", Range(0,1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float _Strength;
            float _ScanlineStrength;
            float _ScanlineSpacing;
            float _NoiseStrength;
            float _RollingBandStrength;
            fixed4 _OverlayTint;
            float _BattleUiUnscaledTime;
            float _Curvature, _EdgeWidth, _EdgeStrength, _CenterGlow;
            fixed4 _GlassTint;
            float _GlassTintStrength;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 glass = uv * 2.0 - 1.0;
                glass *= 1.0 + _Curvature * dot(glass, glass);
                float spacing = max(1.0, _ScanlineSpacing);

                float pixelY = (glass.y * 0.5 + 0.5) * _ScreenParams.y;
                float linePhase = frac(pixelY / spacing);
                float scanline = smoothstep(0.66, 0.98, linePhase) * _ScanlineStrength;

                float frame = floor(_BattleUiUnscaledTime * 24.0);
                float2 noiseCell = floor(uv * _ScreenParams.xy * 0.42 + frame);
                float noise = abs(Hash21(noiseCell) - 0.5) * 2.0 * _NoiseStrength;

                float rollingCenter = frac(_BattleUiUnscaledTime * 0.045);
                float rollingDistance = abs(uv.y - rollingCenter);
                rollingDistance = min(rollingDistance, 1.0 - rollingDistance);
                float rollingBand = exp(-rollingDistance * rollingDistance * 1100.0) * _RollingBandStrength;

                float edge = max(abs(glass.x), abs(glass.y));
                float edgeGlass = smoothstep(1.0 - 2.0 * _EdgeWidth, 1.0, edge) * _EdgeStrength;
                float outsideGlass = smoothstep(0.995, 1.005, edge);
                edgeGlass = max(edgeGlass, outsideGlass);
                float tint = _GlassTintStrength * 0.08;
                float2 glowUv = (uv - float2(0.5, 0.75)) * float2(1.5, 1.0);
                float glow = exp(-dot(glowUv, glowUv) * 12.0) * _CenterGlow;
                float surface = scanline + noise + rollingBand;
                float coverage = surface + edgeGlass + tint + glow;
                float3 color = (_OverlayTint.rgb * surface + _GlassTint.rgb * tint + glow)
                    / max(coverage, 0.0001);

                float sourceAlpha = tex2D(_MainTex, uv).a;
                float alpha = saturate(
                    _Strength *
                    coverage) * sourceAlpha;

                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
}

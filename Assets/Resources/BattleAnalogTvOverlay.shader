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
                float spacing = max(1.0, _ScanlineSpacing);

                float pixelY = uv.y * _ScreenParams.y;
                float linePhase = frac(pixelY / spacing);
                float scanline = smoothstep(0.66, 0.98, linePhase) * _ScanlineStrength;

                float frame = floor(_Time.y * 24.0);
                float2 noiseCell = floor(uv * _ScreenParams.xy * 0.42 + frame);
                float noise = abs(Hash21(noiseCell) - 0.5) * 2.0 * _NoiseStrength;

                float rollingCenter = frac(_Time.y * 0.045);
                float rollingDistance = abs(uv.y - rollingCenter);
                rollingDistance = min(rollingDistance, 1.0 - rollingDistance);
                float rollingBand = exp(-rollingDistance * rollingDistance * 1100.0) * _RollingBandStrength;

                float2 edgeUv = (uv - 0.5) * float2(1.0, 0.82);
                float edgeGlass = smoothstep(0.43, 0.68, length(edgeUv)) * 0.012;

                float sourceAlpha = tex2D(_MainTex, uv).a;
                float alpha = saturate(
                    _Strength *
                    (scanline + noise + rollingBand + edgeGlass)) * sourceAlpha;

                return fixed4(_OverlayTint.rgb, alpha);
            }
            ENDCG
        }
    }
}

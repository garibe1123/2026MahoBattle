Shader "UI/BattleUiHologramStroke"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BattleUiUnscaledTime ("Unscaled Time", Float) = 0
        _ScanlineDensity ("Scanline Density", Range(8,160)) = 74
        _ScanlineSpeed ("Scanline Speed", Range(-8,8)) = 1.8
        _ScanlineStrength ("Scanline Strength", Range(0,0.8)) = 0
        _NoiseStrength ("Noise Strength", Range(0,0.8)) = 0.035
        _PulseStrength ("Pulse Strength", Range(0,0.8)) = 0.02
        _CyanBoost ("Cyan Boost", Range(0,1)) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float _BattleUiUnscaledTime;
            float _ScanlineDensity;
            float _ScanlineSpeed;
            float _ScanlineStrength;
            float _NoiseStrength;
            float _PulseStrength;
            float _CyanBoost;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 source =
                    (tex2D(_MainTex, i.texcoord) + _TextureSampleAdd) *
                    i.color;

                float t = _BattleUiUnscaledTime;

                float scan =
                    0.5 +
                    0.5 *
                    sin(
                        (i.texcoord.y * max(8.0, _ScanlineDensity) +
                         t * _ScanlineSpeed) *
                        6.2831853);

                float2 noiseCell =
                    floor(
                        i.texcoord *
                        float2(92.0, 54.0) +
                        float2(
                            floor(t * 18.0),
                            floor(t * 11.0)));

                float noise =
                    Hash21(noiseCell) -
                    0.5;

                float pulse =
                    0.5 +
                    0.5 *
                    sin(t * 7.4);

                float brightness =
                    1.0 +
                    noise * _NoiseStrength +
                    pulse * _PulseStrength;

                fixed3 rgb =
                    source.rgb *
                    brightness;

                rgb +=
                    fixed3(
                        0.0,
                        0.72,
                        1.0) *
                    _CyanBoost;

                float alpha =
                    source.a *
                    saturate(
                        0.88 +
                        pulse * 0.12 +
                        noise * 0.08);

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(
                    i.worldPosition.xy,
                    _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(
                    rgb,
                    alpha);
            }
            ENDCG
        }
    }
}

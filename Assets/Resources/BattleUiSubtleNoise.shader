Shader "UI/BattleUiSubtleNoise"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BattleUiUnscaledTime ("Unscaled Time", Float) = 0
        _NoiseStrength ("Noise Strength", Range(0,0.12)) = 0.014
        _NoiseSpeed ("Noise Speed", Range(1,30)) = 14
        _NoiseTint ("Noise Tint", Color) = (0.76,0.79,0.82,1)

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
            float _NoiseStrength;
            float _NoiseSpeed;
            fixed4 _NoiseTint;

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
                float frame = floor(t * max(1.0, _NoiseSpeed));

                // 좌표를 시간에 따라 밀지 않고, 고정된 화면 셀에
                // 프레임별 Hash salt만 바꿔 실제 영상 Grain처럼 깜빡입니다.
                float2 fineCell =
                    floor(i.texcoord * _ScreenParams.xy * 0.58);

                float2 coarseCell =
                    floor(i.texcoord * _ScreenParams.xy * 0.16);

                float frameSalt =
                    Hash21(
                        float2(
                            frame * 0.7548777,
                            frame * 0.5698403));

                float2 temporalSalt =
                    float2(
                        frameSalt * 347.17,
                        frameSalt * 911.73);

                float staticFine =
                    Hash21(fineCell + 13.37) - 0.5;

                float temporalFine =
                    Hash21(fineCell + temporalSalt) - 0.5;

                float staticCoarse =
                    Hash21(coarseCell + 91.71) - 0.5;

                float temporalCoarse =
                    Hash21(
                        coarseCell +
                        temporalSalt * 0.37 +
                        31.19) - 0.5;

                float fine =
                    staticFine * 0.28 +
                    temporalFine * 0.72;

                float coarse =
                    staticCoarse * 0.45 +
                    temporalCoarse * 0.55;

                float grain =
                    abs(fine * 0.82 + coarse * 0.18) * 2.0;

                float alpha =
                    source.a *
                    grain *
                    _NoiseStrength;

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(
                    i.worldPosition.xy,
                    _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(
                    _NoiseTint.rgb,
                    alpha);
            }
            ENDCG
        }
    }
}

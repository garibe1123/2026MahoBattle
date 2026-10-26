Shader "UI/BattleInkPanel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _InkOpacity ("Glass ink opacity", Range(0.4, 0.95)) = 0.72
        _InkBlurPixels ("Backdrop blur at 1080p", Range(0, 24)) = 12
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct Attributes { float4 vertex : POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 vertex : SV_POSITION;
                half4 color : COLOR;
                float4 local : TEXCOORD0;
                float4 screen : TEXCOORD1;
                float glass : TEXCOORD2;
            };
            sampler2D _BattleInkBackground;
            float4 _BattleInkScreenSize, _ClipRect;
            float _BattleInkBackgroundReady, _InkOpacity, _InkBlurPixels;
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.local = input.vertex;
                output.screen = ComputeScreenPos(output.vertex);
                output.color = input.color;
                output.glass = input.uv.w;
                return output;
            }
            half3 ReadBackdrop(float2 uv)
            {
                float2 d = _BattleInkScreenSize.zw * (_InkBlurPixels * _BattleInkScreenSize.y / 1080.0);
                half3 c = tex2D(_BattleInkBackground, saturate(uv)).rgb * 4;
                c += tex2D(_BattleInkBackground, saturate(uv + float2(d.x, 0))).rgb * 2;
                c += tex2D(_BattleInkBackground, saturate(uv - float2(d.x, 0))).rgb * 2;
                c += tex2D(_BattleInkBackground, saturate(uv + float2(0, d.y))).rgb * 2;
                c += tex2D(_BattleInkBackground, saturate(uv - float2(0, d.y))).rgb * 2;
                c += tex2D(_BattleInkBackground, saturate(uv + d)).rgb;
                c += tex2D(_BattleInkBackground, saturate(uv - d)).rgb;
                c += tex2D(_BattleInkBackground, saturate(uv + float2(d.x, -d.y))).rgb;
                c += tex2D(_BattleInkBackground, saturate(uv + float2(-d.x, d.y))).rgb;
                return c * (1.0 / 16.0);
            }
            half4 frag(Varyings input) : SV_Target
            {
                half4 color = input.color;
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.local.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                if (color.a <= 0.001) return 0;
                if (input.glass > 0.5)
                {
                    if (_BattleInkBackgroundReady > 0.5)
                        color.rgb = lerp(ReadBackdrop(input.screen.xy / input.screen.w), color.rgb, _InkOpacity);
                    else color.a *= _InkOpacity;
                }
                return color;
            }
            ENDHLSL
        }
    }
}

Shader "UI/BattlePixelText"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font", 2D) = "white" {}
        _PixelSize ("Screen Pixel Size", Float) = 1
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
            #include "BattleUiPixelProjection.cginc"
            struct Attributes { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 bary : TEXCOORD1; float4 edges : TEXCOORD2; };
            struct Varyings { float4 vertex : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float4 local : TEXCOORD1; float3 bary : TEXCOORD2; float4 edges : TEXCOORD3; };
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _ClipRect;
            half4 _TextureSampleAdd;
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.local = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.uv = input.uv;
                output.bary = input.bary.xyz;
                output.edges = input.edges;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 offset = BattlePixelOffset(input.vertex.xy, input.bary.z);
                float2 projectedUV = input.uv;
                if (input.edges.w > 0.5)
                {
                    BattlePixelCoverage(input.bary.xy, input.edges.xyz, offset);
                    projectedUV += ddx(input.uv) * offset.x + ddy(input.uv) * offset.y;
                }
                float2 uv = (floor(projectedUV * _MainTex_TexelSize.zw) + 0.5) * _MainTex_TexelSize.xy;
                half4 color = input.color;
                half4 sampleColor = tex2D(_MainTex, uv) + _TextureSampleAdd;
                color.rgb *= floor(saturate(sampleColor.rgb) * 5.0 + 0.5) / 5.0;
                color.a *= step(0.5, sampleColor.a);
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.local.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDHLSL
        }
    }
}

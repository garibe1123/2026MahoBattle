Shader "Battle/SpriteShadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" "DisableBatching"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Pass
        {
            Name "ProjectedShadow"
            Tags { "LightMode"="Universal2D" }
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 maskUV : TEXCOORD1; };
            sampler2D _BattleShadowFloorMask;
            float4x4 _SourceToWorld;
            float4 _ShadowOrigin, _ShadowDirection, _ShadowShape, _ShadowColor, _ShadowGrounding;
            float4 _BattleShadowMaskRect;
            Varyings vert(appdata_t input)
            {
                Varyings output;
                float3 world = mul(_SourceToWorld, UnityFlipSprite(input.vertex, _Flip)).xyz;
                float height = (world.y - _ShadowOrigin.z) * _ShadowShape.z + _ShadowShape.w;
                float widthBlend = 1 - _ShadowGrounding.x + _ShadowGrounding.x * (world.y - _ShadowOrigin.z) * _ShadowGrounding.y;
                float2 widthAxis = lerp(float2(1, 0), _ShadowDirection.zw * _ShadowShape.y, widthBlend);
                float2 projected = _ShadowOrigin.xy + widthAxis * (world.x - _ShadowOrigin.x)
                    + _ShadowDirection.xy * (height * _ShadowShape.x);
                output.positionCS = mul(UNITY_MATRIX_VP, float4(projected, _ShadowOrigin.w, 1));
                output.uv = input.texcoord;
                output.maskUV = (projected - _BattleShadowMaskRect.xy) * _BattleShadowMaskRect.zw;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                clip(input.maskUV);
                clip(1 - input.maskUV);
                half alpha = SampleSpriteTexture(input.uv).a;
                alpha *= tex2D(_BattleShadowFloorMask, input.maskUV).r;
                return half4(_ShadowColor.rgb, alpha * _ShadowColor.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "FloorMask"
            Tags { "LightMode"="BattleShadowFloorMask" }
            Blend One One
            BlendOp Max
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            float4 _BattleShadowMaskRect;
            v2f vert(appdata_t input)
            {
                v2f output;
                float3 world = mul(unity_ObjectToWorld, UnityFlipSprite(input.vertex, _Flip)).xyz;
                float2 uv = (world.xy - _BattleShadowMaskRect.xy) * _BattleShadowMaskRect.zw;
                output.vertex = float4(uv * 2 - 1, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                    output.vertex.y = -output.vertex.y;
                #endif
                output.texcoord = input.texcoord;
                output.color = input.color * _Color * _RendererColor;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                fixed alpha = SampleSpriteTexture(input.texcoord).a * input.color.a;
                return alpha.xxxx;
            }
            ENDCG
        }
    }
}

Shader "UI/BattleScreenGrid"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Contrast ("Contrast", Range(0, 0.08)) = 0.08
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend DstColor Zero
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Attributes { float4 vertex : POSITION; };
            struct Varyings { float4 vertex : SV_POSITION; };
            float _Contrast;
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 pixel = floor(input.vertex.xy);
                float checker = fmod(pixel.x + pixel.y, 2.0);
                half shade = 1.0 - min(_Contrast, 0.08) * checker;
                return half4(shade, shade, shade, 1.0);
            }
            ENDHLSL
        }
    }
}

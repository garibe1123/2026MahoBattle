Shader "UI/BattleUiOptics"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI on black", 2D) = "black" {}
        _WhiteTex ("UI on white", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _WhiteTex;
            float4 _UiOptics, _PixelLimits, _ScreenSize;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Input input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }
            float2 Limited(float2 offset, float limit)
            {
                float2 pixels = offset * _ScreenSize.xy;
                return offset * min(1.0, limit / max(length(pixels), 0.00001));
            }
            half4 ReadUI(float2 uv)
            {
                if (any(uv < 0.0) || any(uv > 1.0)) return 0;
                half3 black = tex2D(_MainTex, uv).rgb;
                half3 white = tex2D(_WhiteTex, uv).rgb;
                half3 coverage = saturate(1.0 - (white - black));
                return half4(black, max(coverage.r, max(coverage.g, coverage.b)));
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 radial = input.uv * 2.0 - 1.0;
                float2 lens = radial * (-_UiOptics.y * dot(radial, radial) * 0.05 - _UiOptics.z * 0.5);
                lens += radial * radial.yx * radial.yx * _UiOptics.w * 0.5;
                lens = Limited(lens, _PixelLimits.y);
                float2 split = Limited(radial * dot(radial, radial) * _UiOptics.x * 0.05, _PixelLimits.x);
                float limit = max(_PixelLimits.x, _PixelLimits.y);
                half4 red = ReadUI(input.uv + Limited(lens + split, limit));
                half4 green = ReadUI(input.uv + lens);
                half4 blue = ReadUI(input.uv + Limited(lens - split, limit));
                return half4(red.r, green.g, blue.b, max(red.a, max(green.a, blue.a)));
            }
            ENDHLSL
        }
    }
}

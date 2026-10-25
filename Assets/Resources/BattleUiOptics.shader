Shader "UI/BattleUiOptics"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI on black", 2D) = "black" {}
        _WhiteTex ("UI on white", 2D) = "white" {}
        _ProtectedBlack ("Protected UI on black", 2D) = "black" {}
        _ProtectedWhite ("Protected UI on white", 2D) = "white" {}
        _BlurRadius ("TAB blur radius in pixels", Float) = 0
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
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "BattleUiScreenTone.cginc"
            sampler2D _MainTex, _WhiteTex;
            sampler2D _ProtectedBlack, _ProtectedWhite;
            float4 _UiOptics, _PixelLimits, _ScreenSize;
            float _BlurRadius;
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
            float ProtectedCoverage(float2 uv)
            {
                if (any(uv < 0.0) || any(uv > 1.0)) return 0;
                float3 coverage = saturate(1.0 - (tex2D(_ProtectedWhite, uv).rgb - tex2D(_ProtectedBlack, uv).rgb));
                return max(coverage.r, max(coverage.g, coverage.b));
            }
            half4 ReadBlurredUI(float2 uv)
            {
                if (_BlurRadius <= 0.001) return ReadUI(uv);
                float2 stepUV = _ScreenSize.zw * _BlurRadius * 0.5;
                half4 color = ReadUI(uv) * 0.25;
                color += ReadUI(uv + float2(stepUV.x, 0)) * 0.125;
                color += ReadUI(uv - float2(stepUV.x, 0)) * 0.125;
                color += ReadUI(uv + float2(0, stepUV.y)) * 0.125;
                color += ReadUI(uv - float2(0, stepUV.y)) * 0.125;
                color += ReadUI(uv + stepUV * 2) * 0.0625;
                color += ReadUI(uv - stepUV * 2) * 0.0625;
                color += ReadUI(uv + float2(stepUV.x, -stepUV.y) * 2) * 0.0625;
                color += ReadUI(uv + float2(-stepUV.x, stepUV.y) * 2) * 0.0625;
                return color;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 radial = input.uv * 2.0 - 1.0;
                float2 lens = radial * (-_UiOptics.y * dot(radial, radial) * 0.05 - _UiOptics.z * 0.5);
                lens += radial * radial.yx * radial.yx * _UiOptics.w * 0.5;
                lens = Limited(lens, _PixelLimits.y);
                // Keep subtle RGB fringes visible on UI near the screen center too.
                float2 splitDirection = float2(1.0, radial.y * 0.25);
                float2 split = Limited(splitDirection * _UiOptics.x * 0.05, _PixelLimits.x);
                float limit = max(_PixelLimits.x, _PixelLimits.y);
                if (_UiOptics.x == 0) return ReadBlurredUI(input.uv + lens);
                half4 red = ReadBlurredUI(input.uv + Limited(lens + split, limit));
                half4 green = ReadBlurredUI(input.uv + lens);
                half4 blue = ReadBlurredUI(input.uv + Limited(lens - split, limit));
                half4 color = half4(red.r, green.g, blue.b, max(red.a, max(green.a, blue.a)));
                return color;
            }
            ENDHLSL
        }
    }
}

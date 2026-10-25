Shader "Hidden/Harness/BattleUiScreenTonePreview"
{
    Properties
    {
        _MainTex ("UI on black", 2D) = "black" {}
        _WhiteTex ("UI on white", 2D) = "white" {}
        _ProtectedBlack ("Protected UI on black", 2D) = "black" {}
        _ProtectedWhite ("Protected UI on white", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Assets/Resources/BattleUiScreenTone.cginc"

            sampler2D _MainTex, _WhiteTex;
            float4 _ScreenSize, _PreviewBackgroundColor;

            float Coverage(float3 black, float3 white)
            {
                float3 coverage = saturate(1.0 - (white - black));
                return max(coverage.r, max(coverage.g, coverage.b));
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float3 black = tex2D(_MainTex, uv).rgb;
                float alpha = Coverage(black, tex2D(_WhiteTex, uv).rgb);
                float2 centered = (uv - 0.5) * _ScreenSize.xy * 2.0 /
                    max(1.0, min(_ScreenSize.x, _ScreenSize.y));
                float edge = smoothstep(_ToneVignette.y, _ToneVignette.y + _ToneVignette.z, length(centered));
                float dots = BattleScreenToneDots(uv, _ScreenSize.xy, edge * _ToneVignette.x);
                float strength = dots * _TonePattern.z * saturate(_ToneColor.a);
                float3 background = lerp(_PreviewBackgroundColor.rgb, _ToneColor.rgb, strength);
                return float4(black + background * (1.0 - alpha), 1.0);
            }
            ENDHLSL
        }
    }
}

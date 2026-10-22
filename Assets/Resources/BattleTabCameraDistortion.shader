Shader "Hidden/BattleTabCameraDistortion"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _CrtSettings;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.texcoord;
                float2 glass = screenUV * 2.0 - 1.0;
                float2 curved = glass * (1.0 + _CrtSettings.x * glass.yx * glass.yx);
                float2 uv = curved * 0.5 + 0.5;
                float edge = max(abs(curved.x), abs(curved.y));
                float border = 1.0 - smoothstep(1.0 - max(fwidth(edge), 0.001), 1.0, edge);
                float falloff = 1.0 - smoothstep(1.0 - _CrtSettings.y, 1.0, edge) * _CrtSettings.z;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp,
                    clamp(uv, 0.0, 1.0) * _BlitScaleBias.xy + _BlitScaleBias.zw);
                color.rgb *= border * falloff;
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}

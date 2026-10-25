Shader "UI/BattlePaperPanel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Mask", 2D) = "white" {}
        _UseSpriteMask ("Use Sprite Mask", Float) = 0
        _PaperTexture ("Grain, Stain, Reserved, Fiber Scale", Vector) = (0.065, 0.08, 0.22, 0.7)
        _PaperPunch ("Hole Radius, Spacing, Top Inset, Reserved", Vector) = (3.5, 32, 12, 0)
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
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
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

            sampler2D _MainTex;
            float4 _ClipRect, _TextureSampleAdd, _PaperTexture, _PaperPunch;
            float _UseSpriteMask;
            struct Attributes { float4 vertex : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };
            struct Varyings { float4 vertex : SV_POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; float2 local : TEXCOORD1; };

            float Hash(float2 p)
            {
                float3 h = frac(float3(p.x, p.y, p.x) * 0.1031);
                h += dot(h, h.yzx + 33.33);
                return frac((h.x + h.y) * h.z);
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), f.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + 1.0), f.x), f.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.uv = input.uv;
                output.local = input.vertex.xy;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float4 color = input.color;
                if (_UseSpriteMask > 0.5)
                    color.a *= saturate((tex2D(_MainTex, input.uv.xy) + _TextureSampleAdd).a);
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.local, _ClipRect);
                #endif
                float2 seed = frac(input.uv.zw);
                float2 offset = seed * float2(937.0, 617.0);
                float angle = seed.x * 6.2831853;
                float2 direction = float2(cos(angle), sin(angle));
                float2 p = float2(dot(input.local, direction), dot(input.local, float2(-direction.y, direction.x)));
                p = p / max(0.1, _PaperTexture.w) + offset;
                float grain = Noise(p * float2(1.7, 0.6)) - 0.5;
                float fibers = Noise(p * float2(0.09, 2.4)) - 0.5;
                float stain = smoothstep(0.48, 0.85, Noise(p * 0.025));
                float face = smoothstep(0.45, 0.8, min(color.r, min(color.g, color.b)));
                float grainShade = (grain * 0.65 + fibers * 0.35) * _PaperTexture.x;
                color.rgb += face * grainShade;
                color.rgb -= face * stain * _PaperTexture.y * float3(0.38, 0.55, 0.8);
                if (_UseSpriteMask < 0.5 && input.uv.z >= 2.0 && _PaperPunch.x > 0.0)
                {
                    float spacing = max(12.0, _PaperPunch.y);
                    float x = (frac(input.uv.x / spacing) - 0.5) * spacing;
                    float distanceToHole = length(float2(x, input.uv.y - _PaperPunch.z)) - _PaperPunch.x;
                    float aa = max(0.25, fwidth(distanceToHole));
                    color.a *= smoothstep(-aa, aa, distanceToHole);
                    color.rgb *= 1.0 - (1.0 - smoothstep(0.0, 1.5, distanceToHole)) * 0.16;
                }
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                color.rgb = saturate(color.rgb);
                return color;
            }
            ENDHLSL
        }
    }
}

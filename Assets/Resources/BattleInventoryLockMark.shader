Shader "UI/BattleInventoryLockMark"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _LockStyle ("Theme, Width, Variation, Ink Noise", Vector) = (0, 0.075, 0.65, 0.35)
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

            float4 _ClipRect, _LockStyle;
            struct Attributes { float4 vertex : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };
            struct Varyings { float4 vertex : SV_POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; float2 local : TEXCOORD1; };

            float Hash(float2 p)
            {
                float3 h = frac(float3(p.x, p.y, p.x) * 0.1031);
                h += dot(h, h.yzx + 33.33);
                return frac((h.x + h.y) * h.z);
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

            float Stroke(float2 p, float2 seed, int stroke)
            {
                bool comic = _LockStyle.x == 1;
                bool maho = _LockStyle.x == 2;
                bool stylish = _LockStyle.x == 3;
                bool birthday = _LockStyle.x == 4;
                bool paper = _LockStyle.x == 5;
                float character = Hash(seed * 171 + stroke * 31) * 2 - 1;
                float variation = _LockStyle.z;
                float angle = character * variation * (paper ? 0.13 : 0.035);
                if (stylish) angle += stroke == 0 ? 0.17 : -0.17;
                float2 q = float2(p.x * cos(angle) + p.y * sin(angle), -p.x * sin(angle) + p.y * cos(angle));
                float halfExtent = paper ? 0.42 : 0.40;
                float2 start = float2(-halfExtent, stroke == 0 ? -halfExtent : halfExtent);
                float2 end = -start;
                start *= 1 - (character + 1) * variation * (paper ? 0.09 : 0.035);
                end *= 1 - (1 - character) * variation * (paper ? 0.09 : 0.035);
                float2 direction = normalize(end - start);
                float2 normal = float2(-direction.y, direction.x);
                float weight = comic ? 2 : stylish ? 0.5 : birthday ? 1.8 : paper ? 0.72 : 1;
                float radius = _LockStyle.y * weight * 0.5;
                float bend = paper ? character * variation * 0.012 : maho ? 0.025 : birthday ? 0.016 : 0;
                float distanceToInk = 10;
                float2 previous = start;
                [unroll]
                for (int segment = 1; segment <= 6; segment++)
                {
                    float t = segment / 6.0;
                    float2 curvePoint = lerp(start, end, t) + normal * (4 * t * (1 - t) * bend);
                    float2 edge = curvePoint - previous;
                    float along = saturate(dot(q - previous, edge) / dot(edge, edge));
                    float pressure = (segment - 1 + along) / 6.0;
                    float thickness = paper ? lerp(0.55, 1.1, sin(pressure * 2.2 + 0.35)) * (1 + character * 0.12) :
                        comic ? lerp(0.65, 1.2, sin(pressure * 3.14159265)) : 1;
                    distanceToInk = min(distanceToInk, length(q - lerp(previous, curvePoint, along)) - radius * thickness);
                    previous = curvePoint;
                }
                if (!paper && !maho && !birthday)
                    distanceToInk = max(distanceToInk, max(dot(start - q, direction), dot(q - end, direction)));
                return distanceToInk;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv.xy - 0.5;
                // Union the ink before alpha blending so the crossing has one opacity.
                float distanceToInk = min(Stroke(p, input.uv.zw, 0), Stroke(p, input.uv.zw, 1));
                float grain = Hash(floor(input.uv.xy * 230) + input.uv.zw * 997);
                float paper = _LockStyle.x == 5 ? 1 : 0;
                distanceToInk += (grain - 0.5) * 0.006 * _LockStyle.w * paper;
                float aa = max(0.0001, fwidth(distanceToInk));
                float coverage = 1 - smoothstep(-aa * 0.5, aa * 0.5, distanceToInk);
                float ink = 1 - paper * _LockStyle.w * (grain * 0.3 + step(0.94, grain) * 0.65);
                float4 color = input.color;
                color.a *= coverage * ink;
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.local, _ClipRect);
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

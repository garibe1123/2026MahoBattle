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
        [HideInInspector] _ShadowReceiver ("Receive Object Shadows", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" "DisableBatching"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        HLSLINCLUDE
        #include "UnitySprites.cginc"
        sampler2D _VolumeTex;
        float4x4 _WorldToSource, _SourceToWorld;
        float _ShadowVolumeEnabled;
        float4 _VolumeRect, _VolumeData, _VolumeWorldBounds, _VolumeSpriteBounds, _VolumeBorder, _VolumeTarget;
        float _VolumePlaneZ;
        float4 _ShadowOrigin, _ShadowDirection, _ShadowShape, _ShadowColor;
        float4 _ShadowBounds, _ReceiverShadowBounds, _ReceiverShadowShape, _ReceiverShadowOrigin;
        float _ShadowSourceX;

        float Unslice(float point, float minimum, float size, float target, float lower, float upper)
        {
            float borderScale = min(1, target / max(0.0001, lower + upper));
            float offset = point - minimum / max(0.0001, size) * target;
            if (offset <= lower * borderScale)
                return minimum + offset / max(0.0001, borderScale);
            if (offset >= target - upper * borderScale)
                return minimum + size - (target - offset) / max(0.0001, borderScale);
            return minimum + lower + (offset - lower * borderScale) * max(0, size - lower - upper) /
                max(0.0001, target - (lower + upper) * borderScale);
        }

        float2 VolumeUV(float2 local)
        {
            if (_VolumeTarget.z > 0.5)
            {
                local.x = Unslice(local.x, _VolumeSpriteBounds.x, _VolumeSpriteBounds.z,
                    _VolumeTarget.x, _VolumeBorder.x, _VolumeBorder.z);
                local.y = Unslice(local.y, _VolumeSpriteBounds.y, _VolumeSpriteBounds.w,
                    _VolumeTarget.y, _VolumeBorder.y, _VolumeBorder.w);
            }
            return (local - _VolumeRect.xy) * _VolumeRect.zw;
        }

        float UnsliceMaxSlope(float size, float target, float lower, float upper)
        {
            float borderScale = min(1, target / max(0.0001, lower + upper));
            float centerSize = target - (lower + upper) * borderScale;
            float centerSlope = centerSize > 0.0001 ? max(0, size - lower - upper) / centerSize : 0;
            return max(1 / max(0.0001, borderScale), centerSlope);
        }

        bool ClipVolumeRay(float start, float delta, float minimum, float maximum, inout float enter, inout float exit)
        {
            if (abs(delta) < 0.000001)
                return start >= minimum && start <= maximum;
            float a = (minimum - start) / delta;
            float b = (maximum - start) / delta;
            enter = max(enter, min(a, b));
            exit = min(exit, max(a, b));
            return enter <= exit;
        }

        half VolumeAlpha(float2 projected, float4 origin, float4 shape)
        {
            float height = (_VolumeWorldBounds.y - origin.z) * shape.z + shape.w;
            float2 start = float2(_ShadowSourceX + (projected.x - origin.x -
                _ShadowDirection.x * height * shape.x) / shape.y, _VolumeWorldBounds.y);
            float2 delta = float2(-_ShadowDirection.x * shape.x * shape.z / shape.y, 1);
            float radius = max(0.00001, _VolumeData.x * shape.y);
            float depth = (projected.y - origin.y - _ShadowDirection.y * height * shape.x) / radius;
            float depthDelta = -_ShadowDirection.y * shape.x * shape.z / radius;
            float depthLimit = _VolumeData.y + 1.0 / _VolumeData.z;
            float enter = 0;
            float exit = _VolumeWorldBounds.w - _VolumeWorldBounds.y;
            if (!ClipVolumeRay(start.x, delta.x, _VolumeWorldBounds.x, _VolumeWorldBounds.z, enter, exit) ||
                !ClipVolumeRay(depth, depthDelta, -depthLimit, depthLimit, enter, exit))
                return 0;
            float2 localStart = mul(_WorldToSource, float4(start, _VolumePlaneZ, 1)).xy;
            float2 localDelta = mul(_WorldToSource, float4(delta, 0, 0)).xy;
            float2 uvRate = abs(localDelta) * _VolumeRect.zw;
            if (_VolumeTarget.z > 0.5)
            {
                uvRate.x *= UnsliceMaxSlope(_VolumeSpriteBounds.z, _VolumeTarget.x, _VolumeBorder.x, _VolumeBorder.z);
                uvRate.y *= UnsliceMaxSlope(_VolumeSpriteBounds.w, _VolumeTarget.y, _VolumeBorder.y, _VolumeBorder.w);
            }
            float texelRate = max(max(uvRate.x * _VolumeData.z, uvRate.y * _VolumeData.w),
                abs(depthDelta) * _VolumeData.z * 0.5);
            float stepLength = 0.5 / max(0.000001, texelRate);
            int steps = max(1, (int)ceil((exit - enter) / stepLength));
            half alpha = 0;
            // Advance at most half a volume texel, including sliced coordinates and depth.
            [loop] for (int i = 0; i < steps; i++)
            {
                float t = min(enter + (i + 0.5) * stepLength, exit);
                float2 uv = VolumeUV(localStart + localDelta * t);
                if (any(uv < 0) || any(uv > 1))
                    continue;
                float4 volumeSample = tex2Dlod(_VolumeTex, float4(uv, 0, 0));
                float thickness = dot(volumeSample.rg, float2(256.0 / 257.0, 1.0 / 257.0));
                half coverage = saturate((thickness - abs(depth + depthDelta * t)) * _VolumeData.z * 0.5 + 0.5);
                alpha = max(alpha, volumeSample.b * coverage);
                if (alpha >= 0.995)
                    break;
            }
            return alpha;
        }

        float2 ProjectCaster(appdata_t input, float4 bounds, float4 origin, float4 shape)
        {
            if (_ShadowVolumeEnabled > 0.5)
                return bounds.xy + input.texcoord * bounds.zw;
            float3 world = mul(_SourceToWorld, UnityFlipSprite(input.vertex, _Flip)).xyz;
            float height = (world.y - origin.z) * shape.z + shape.w;
            return origin.xy + _ShadowDirection.zw * shape.y * (world.x - _ShadowSourceX) +
                _ShadowDirection.xy * (height * shape.x);
        }

        half CasterAlpha(float2 uv, float4 origin, float4 shape)
        {
            if (_ShadowVolumeEnabled > 0.5)
                return VolumeAlpha(uv, origin, shape);
            return SampleSpriteTexture(uv).a;
        }
        ENDHLSL
        Pass
        {
            Name "ProjectedShadow"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 maskUV : TEXCOORD1; half alpha : TEXCOORD2; };
            sampler2D _BattleShadowFloorMask;
            sampler2D _BattleShadowReceiverMask;
            float4 _BattleShadowMaskRect;
            float4 _ReceiverWorldRect, _ReceiverAtlasRect;
            float _ShadowReceiver;
            Varyings vert(appdata_t input)
            {
                Varyings output;
                output.uv = input.texcoord;
                output.alpha = input.color.a * _Color.a * _RendererColor.a;
                if (_ShadowReceiver > 0.5)
                {
                    float3 receiverWorld = mul(unity_ObjectToWorld, UnityFlipSprite(input.vertex, _Flip)).xyz;
                    output.positionCS = mul(UNITY_MATRIX_VP, float4(receiverWorld, 1));
                    output.maskUV = (receiverWorld.xy - _ReceiverWorldRect.xy) * _ReceiverWorldRect.zw;
                    return output;
                }
                float2 projected = ProjectCaster(input, _ShadowBounds, _ShadowOrigin, _ShadowShape);
                output.positionCS = mul(UNITY_MATRIX_VP, float4(projected, _ShadowOrigin.w, 1));
                output.uv = _ShadowVolumeEnabled > 0.5 ? projected : input.texcoord;
                output.maskUV = (projected - _BattleShadowMaskRect.xy) * _BattleShadowMaskRect.zw;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                clip(input.maskUV);
                clip(1 - input.maskUV);
                if (_ShadowReceiver > 0.5)
                {
                    float2 atlasUV = _ReceiverAtlasRect.xy + input.maskUV * _ReceiverAtlasRect.zw;
                    half alpha = SampleSpriteTexture(input.uv).a * input.alpha * tex2D(_BattleShadowReceiverMask, atlasUV).r;
                    return half4(_ShadowColor.rgb, alpha);
                }
                half floorAlpha = tex2D(_BattleShadowFloorMask, input.maskUV).r;
                clip(floorAlpha - 0.001);
                half alpha = CasterAlpha(input.uv, _ShadowOrigin, _ShadowShape) * floorAlpha;
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
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
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
            ENDHLSL
        }
        Pass
        {
            Name "ObjectShadowMask"
            Tags { "LightMode"="BattleObjectShadowMask" }
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            float4 _BattleShadowReceiverRect;
            v2f vert(appdata_t input)
            {
                v2f output;
                float2 projected = ProjectCaster(input, _ReceiverShadowBounds, _ReceiverShadowOrigin, _ReceiverShadowShape);
                float2 uv = (projected - _BattleShadowReceiverRect.xy) * _BattleShadowReceiverRect.zw;
                output.vertex = float4(uv * 2 - 1, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                    output.vertex.y = -output.vertex.y;
                #endif
                output.texcoord = _ShadowVolumeEnabled > 0.5 ? projected : input.texcoord;
                output.color = 1;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                fixed alpha = CasterAlpha(input.texcoord, _ReceiverShadowOrigin, _ReceiverShadowShape) * _ShadowColor.a;
                return alpha.xxxx;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ReceivedAlphaDebug"
            Tags { "LightMode"="BattleReceivedAlphaDebug" }
            Blend Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            sampler2D _BattleShadowReceiverMask;
            float4 _ReceiverWorldRect, _ReceiverAtlasRect;
            struct DebugVaryings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float2 maskUV : TEXCOORD1; half alpha : TEXCOORD2; };
            DebugVaryings vert(appdata_t input)
            {
                DebugVaryings output;
                float3 world = mul(unity_ObjectToWorld, UnityFlipSprite(input.vertex, _Flip)).xyz;
                output.maskUV = (world.xy - _ReceiverWorldRect.xy) * _ReceiverWorldRect.zw;
                output.position = float4(output.maskUV * 2 - 1, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                    output.position.y = -output.position.y;
                #endif
                output.uv = input.texcoord;
                output.alpha = input.color.a * _Color.a * _RendererColor.a;
                return output;
            }
            half4 frag(DebugVaryings input) : SV_Target
            {
                float2 atlasUV = _ReceiverAtlasRect.xy + input.maskUV * _ReceiverAtlasRect.zw;
                half alpha = SampleSpriteTexture(input.uv).a * input.alpha * tex2D(_BattleShadowReceiverMask, atlasUV).r;
                half visibleAlpha = saturate(alpha * 4);
                return half4(visibleAlpha, visibleAlpha, visibleAlpha, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "TiledVolumeAlpha"
            Tags { "LightMode"="BattleTiledVolumeAlpha" }
            Blend Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            float4 _VolumeBakeRect;
            v2f vert(appdata_t input)
            {
                v2f output;
                float2 uv = (input.vertex.xy - _VolumeBakeRect.xy) * _VolumeBakeRect.zw;
                output.vertex = float4(uv * 2 - 1, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                    output.vertex.y = -output.vertex.y;
                #endif
                output.texcoord = input.texcoord;
                output.color = 1;
                return output;
            }
            half4 frag(v2f input) : SV_Target
            {
                half alpha = SampleSpriteTexture(input.texcoord).a;
                return alpha.xxxx;
            }
            ENDHLSL
        }
    }
}

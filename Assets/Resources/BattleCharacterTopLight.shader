Shader "Sprites/BattleCharacterTopLight"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _GlowColor ("Glow Color", Color) = (1,1,1,1)
        _Strength ("Strength", Range(0,1)) = 0.04
        _HorizontalBias ("Horizontal Light Bias", Range(-1,1)) = 0
        _DirectionalAmount ("Directional Amount", Range(0,1)) = 0.55
        _VolumeStrength ("Automatic Volume Strength", Range(0,0.35)) = 0
        _VolumeColor ("Automatic Volume Color", Color) = (1,0.96,0.85,1)
        _VolumeLightDir ("Automatic Local Light Direction", Vector) = (-0.55,0.85,0,0)
        _SpriteLocalRect ("Sprite Local Min / Inv Size", Vector) = (-0.5,-0.5,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha One
        ColorMask RGB

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 localPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _GlowColor;
            float _Strength;
            float _HorizontalBias;
            float _DirectionalAmount;
            float _VolumeStrength;
            fixed4 _VolumeColor;
            float4 _VolumeLightDir;
            float4 _SpriteLocalRect;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.texcoord;
                o.localPos = v.vertex.xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 sprite = tex2D(_MainTex, i.uv) * i.color;
                float top = smoothstep(0.08, 0.95, i.uv.y);
                float topLight = lerp(0.18, 1.0, top);

                float sideCoord = (i.uv.x - 0.5) * 2.0;
                float sideLight = saturate(
                    0.5 +
                    sideCoord *
                    _HorizontalBias *
                    0.5);

                float sideShaping = lerp(
                    0.58,
                    1.0,
                    sideLight);

                float directional = topLight * lerp(
                    1.0,
                    sideShaping,
                    saturate(abs(_HorizontalBias) * _DirectionalAmount));

                // SpriteRenderer atlas UVs differ per animation frame. Local
                // vertex coordinates plus sprite.bounds yield a stable 0..1
                // volume coordinate without authoring normal maps.
                float2 normalized = saturate(
                    (i.localPos - _SpriteLocalRect.xy) * _SpriteLocalRect.zw);
                float2 xy = normalized * 2.0 - 1.0;
                float height = sqrt(saturate(
                    1.0 - xy.x * xy.x * 0.55 - xy.y * xy.y * 0.32));
                float3 virtualNormal = normalize(
                    float3(xy.x * 0.76, xy.y * 0.46, height));
                float3 lightDirection = normalize(
                    float3(_VolumeLightDir.xy * 0.82, 0.86));
                float facing = saturate(dot(virtualNormal, lightDirection));
                float volume = saturate(_VolumeStrength) *
                    smoothstep(0.12, 0.92, facing) *
                    lerp(0.50, 1.0, facing);

                float top = saturate(_Strength) * directional;
                float combined = top + volume;
                float3 tint = (
                    _GlowColor.rgb * top +
                    _VolumeColor.rgb * volume) / max(0.0001, combined);

                return fixed4(tint, sprite.a * saturate(combined));
            }
            ENDCG
        }
    }
}

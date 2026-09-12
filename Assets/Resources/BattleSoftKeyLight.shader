Shader "Sprites/BattleSoftKeyLight"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _BeamFlipY ("Beam Flip Y", Float) = 0
        [PerRendererData] _BeamRotationDegrees ("Beam Rotation Degrees", Float) = 0
        [PerRendererData] _BeamUvOffset ("Beam UV Offset", Vector) = (0,0,0,0)
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
            };

            sampler2D _MainTex;
            float _BeamFlipY;
            float _BeamRotationDegrees;
            float4 _BeamUvOffset;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 beamUv = i.uv;

                if (_BeamFlipY > 0.5)
                    beamUv.y = 1.0 - beamUv.y;

                float radiansValue = radians(_BeamRotationDegrees);
                float sinValue = sin(radiansValue);
                float cosValue = cos(radiansValue);
                float2 centered = beamUv - 0.5;
                centered = float2(
                    centered.x * cosValue - centered.y * sinValue,
                    centered.x * sinValue + centered.y * cosValue);
                beamUv = centered + 0.5 + _BeamUvOffset.xy;

                fixed4 tex = tex2D(_MainTex, beamUv);
                return fixed4(i.color.rgb, tex.a * i.color.a);
            }
            ENDCG
        }
    }
}

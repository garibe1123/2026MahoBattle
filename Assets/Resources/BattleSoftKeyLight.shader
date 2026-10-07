Shader "Sprites/BattleSoftKeyLight"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _BeamBottomWidthScale ("Beam Bottom Width Scale", Range(0.5,2)) = 1
        _BeamOpacityScale ("Beam Opacity Scale", Range(0,1)) = 1
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
        Blend SrcAlpha OneMinusSrcAlpha
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
            float _BeamBottomWidthScale;
            float _BeamOpacityScale;

            v2f vert(appdata_t v)
            {
                v2f o;

                float bottomWeight =
                    1.0 - saturate(v.texcoord.y);

                // Keep the narrow source edge almost fixed while the lower cone
                // opens up through scale only. No left/right shear or rotation.
                v.vertex.x *=
                    lerp(
                        1.0,
                        max(0.01, _BeamBottomWidthScale),
                        bottomWeight);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                return fixed4(
                    i.color.rgb,
                    tex.a *
                    i.color.a *
                    saturate(_BeamOpacityScale));
            }
            ENDCG
        }
    }
}

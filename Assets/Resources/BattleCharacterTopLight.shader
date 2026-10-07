Shader "Sprites/BattleCharacterTopLight"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _GlowColor ("Glow Color", Color) = (1,1,1,1)
        _Strength ("Strength", Range(0,1)) = 0.04
        _HorizontalBias ("Horizontal Light Bias", Range(-1,1)) = 0
        _DirectionalAmount ("Directional Amount", Range(0,1)) = 0.55
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
            fixed4 _GlowColor;
            float _Strength;
            float _HorizontalBias;
            float _DirectionalAmount;

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

                float alpha = sprite.a * saturate(_Strength) * directional;
                return fixed4(_GlowColor.rgb, alpha);
            }
            ENDCG
        }
    }
}

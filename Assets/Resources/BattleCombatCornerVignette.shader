Shader "UI/BattleCombatCornerVignette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _VignetteColor ("Vignette Color", Color) = (0,0,0,1)
        _Strength ("Strength", Range(0,1)) = 1
        _EdgeDarkness ("Edge Darkness", Range(0,0.25)) = 0.045
        _CornerDarkness ("Corner Darkness", Range(0,0.5)) = 0.22
        _HorizontalFalloff ("Horizontal Falloff", Range(0.05,0.6)) = 0.25
        _VerticalFalloff ("Vertical Falloff", Range(0.05,0.6)) = 0.31
        _CornerPower ("Corner Power", Range(0.25,4)) = 0.78
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _VignetteColor;
            float _Strength;
            float _EdgeDarkness;
            float _CornerDarkness;
            float _HorizontalFalloff;
            float _VerticalFalloff;
            float _CornerPower;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = abs(i.uv * 2.0 - 1.0);

                float horizontalStart = 1.0 - max(0.001, _HorizontalFalloff);
                float verticalStart = 1.0 - max(0.001, _VerticalFalloff);

                float edgeX = smoothstep(horizontalStart, 1.0, p.x);
                float edgeY = smoothstep(verticalStart, 1.0, p.y);

                // A very small edge component keeps the picture seated inside the frame,
                // while the multiplied component deepens only where two edges meet.
                float edge = max(edgeX, edgeY);
                float corner = pow(saturate(edgeX * edgeY), max(0.001, _CornerPower));

                // Broaden the diagonal shoulder so the corners fade like lens / CRT glass
                // instead of looking like four pasted black circles.
                float diagonal = smoothstep(1.10, 1.82, p.x + p.y);
                corner = max(corner, diagonal * 0.72);

                float alpha = saturate(
                    _Strength *
                    (edge * _EdgeDarkness + corner * _CornerDarkness));

                return fixed4(_VignetteColor.rgb, alpha * _VignetteColor.a);
            }
            ENDCG
        }
    }
}

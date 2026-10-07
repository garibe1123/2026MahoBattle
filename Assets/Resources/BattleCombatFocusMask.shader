Shader "UI/BattleCombatFocusMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _MaskColor ("Mask Color", Color) = (0,0,0,1)
        _Presentation ("Presentation", Range(0,1)) = 0
        _DimAlpha ("Dim Alpha", Range(0,1)) = 0.24
        _Center ("Focus Center", Vector) = (0.5,0.5,0,0)
        _Radius ("Focus Radius", Float) = 0.12
        _VerticalRatio ("Vertical Ratio", Range(0.2,1)) = 0.86
        _Elongation ("Directional Elongation", Range(1,1.5)) = 1
        _RotationRadians ("Rotation Radians", Float) = 0
        _Feather ("Feather", Float) = 0.03
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
        ZTest [unity_GUIZTestMode]
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
            fixed4 _MaskColor;
            float _Presentation;
            float _DimAlpha;
            float4 _Center;
            float _Radius;
            float _VerticalRatio;
            float _Elongation;
            float _RotationRadians;
            float _Feather;

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
                float aspect = _ScreenParams.x / max(1.0, _ScreenParams.y);
                float2 delta = i.uv - _Center.xy;
                delta.x *= aspect;

                float c = cos(_RotationRadians);
                float s = sin(_RotationRadians);
                float2 rotated = float2(
                    c * delta.x + s * delta.y,
                    -s * delta.x + c * delta.y);

                float safeRadius = max(0.0001, _Radius);
                float major = safeRadius * max(1.0, _Elongation);
                float minor = safeRadius * max(0.05, _VerticalRatio);
                float2 normalized = float2(
                    rotated.x / major,
                    rotated.y / minor);

                float distanceFromCenter = length(normalized);
                float normalizedFeather = max(0.0001, _Feather) / safeRadius;
                float hole =
                    1.0 - smoothstep(
                        max(0.0, 1.0 - normalizedFeather),
                        1.0 + normalizedFeather,
                        distanceFromCenter);

                float alpha =
                    saturate(_DimAlpha) *
                    saturate(_Presentation) *
                    (1.0 - hole);

                return fixed4(
                    _MaskColor.rgb,
                    alpha * _MaskColor.a * i.color.a);
            }
            ENDCG
        }
    }
}

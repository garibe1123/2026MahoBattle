Shader "UI/BattleShowFocusMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _MaskColor ("Mask Color", Color) = (0,0,0,1)

        _Presentation ("Presentation", Range(0,1)) = 0

        _NearDimAlpha ("Near Dim Alpha", Range(0,1)) = 0.62
        _FarDimAlpha ("Far Dim Alpha", Range(0,1)) = 0.96
        _DimCenter ("Dim Center", Vector) = (0.5,0.5,0,0)
        _DimRadius ("Dim Radius", Float) = 0.58

        _PlayerCenter ("Player Center", Vector) = (0.5,0.5,0,0)
        _PlayerRadius ("Player Radius", Float) = 0.1
        _PlayerStrength ("Player Strength", Range(0,1)) = 0

        _PresenterCenter ("Presenter Center", Vector) = (0.5,0.5,0,0)
        _PresenterRadius ("Presenter Radius", Float) = 0.1
        _PresenterStrength ("Presenter Strength", Range(0,1)) = 0

        _ScreenRect ("Screen Rect MinMax", Vector) = (0.4,0.4,0.6,0.6)
        _ScreenStrength ("Screen Strength", Range(0,1)) = 0

        _CircleFeather ("Circle Feather", Float) = 0.018
        _RectFeather ("Rect Feather", Float) = 0.0035
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

            float _NearDimAlpha;
            float _FarDimAlpha;
            float4 _DimCenter;
            float _DimRadius;

            float4 _PlayerCenter;
            float _PlayerRadius;
            float _PlayerStrength;

            float4 _PresenterCenter;
            float _PresenterRadius;
            float _PresenterStrength;

            float4 _ScreenRect;
            float _ScreenStrength;

            float _CircleFeather;
            float _RectFeather;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.texcoord;
                return o;
            }

            float AspectDistance(float2 a, float2 b)
            {
                float aspect = _ScreenParams.x / max(1.0, _ScreenParams.y);
                float2 delta = a - b;
                delta.x *= aspect;
                return length(delta);
            }

            float CircleHole(float2 uv, float2 center, float radius, float feather)
            {
                float distanceFromCenter = AspectDistance(uv, center);
                return 1.0 - smoothstep(
                    max(0.0, radius - feather),
                    radius + feather,
                    distanceFromCenter);
            }

            float RectSignedDistance(float2 uv, float4 rectMinMax)
            {
                float2 center = (rectMinMax.xy + rectMinMax.zw) * 0.5;
                float2 halfSize = max(float2(0.00001, 0.00001), (rectMinMax.zw - rectMinMax.xy) * 0.5);
                float2 q = abs(uv - center) - halfSize;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // Player 근처는 Dark Gray가 남고, 멀어질수록 거의 Black으로 떨어집니다.
                float distanceFromPlayer = AspectDistance(uv, _DimCenter.xy);
                float farT = smoothstep(0.0, max(0.0001, _DimRadius), distanceFromPlayer);
                float dimAlpha = lerp(_NearDimAlpha, _FarDimAlpha, farT);

                // Character = Circle Focus
                float playerHole =
                    CircleHole(uv, _PlayerCenter.xy, _PlayerRadius, _CircleFeather)
                    * _PlayerStrength;

                float presenterHole =
                    CircleHole(uv, _PresenterCenter.xy, _PresenterRadius, _CircleFeather)
                    * _PresenterStrength;

                // Screen = Rect Focus. TV에는 원형 Spotlight / Beam을 만들지 않습니다.
                float screenSd = RectSignedDistance(uv, _ScreenRect);
                float screenHole =
                    (1.0 - smoothstep(-_RectFeather, _RectFeather, screenSd))
                    * _ScreenStrength;

                float exposed = saturate(max(playerHole, max(presenterHole, screenHole)));
                float finalAlpha = dimAlpha * _Presentation * (1.0 - exposed);

                return fixed4(
                    _MaskColor.rgb,
                    saturate(finalAlpha * _MaskColor.a * i.color.a));
            }
            ENDCG
        }
    }
}

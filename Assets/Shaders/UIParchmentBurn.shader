// The night parchment (board task T21, mockups tour-de-nuit v5 C3): the default UI shader plus a burn that every graphic
// of the banner shares (the parchment, its seal and its text), so they burn along one front. The front is a field over
// the banner's own rect (_BurnRect, canvas space, set by NightTurnBanner): a gradient from the lower left corner mixed
// with value noise. Where the field is under _Burn the sheet is gone; just above it a thin ember line, then charred paper,
// then a scorched tint. _Unroll clips the sheet from the centre outwards while it unrolls.
// Everything under "Burn" is there to be tuned on the material (Assets/Art/Materials/UI/ParchmentBurn.mat).
Shader "CdP/UI Parchment Burn"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Burn)]
        _BannerSize ("Banner size in reference pixels (noise scale)", Vector) = (1060, 120, 0, 0)
        _NoiseCellLarge ("Noise cell, large (px)", Float) = 46
        _NoiseCellSmall ("Noise cell, small (px)", Float) = 13
        _NoiseWeight ("Noise weight (0 = straight front)", Range(0, 1)) = 0.18
        _CornerStretch ("Front shape: vertical squash of the corner distance", Range(0.05, 1)) = 0.35
        _EmberWidth ("Ember line width", Range(0.001, 0.05)) = 0.012
        _CharWidth ("Charred band (from the front)", Range(0.005, 0.2)) = 0.045
        _ScorchWidth ("Scorched band (from the front)", Range(0.01, 0.4)) = 0.11
        [HDR] _EmberHot ("Ember, at the front", Color) = (1, 0.82, 0.47, 1)
        [HDR] _EmberCool ("Ember, behind", Color) = (0.78, 0.31, 0.08, 1)
        _CharColor ("Charred paper", Color) = (0.235, 0.11, 0.047, 1)
        _ScorchTint ("Scorched tint (multiplies)", Color) = (0.55, 0.45, 0.35, 1)

        [HideInInspector] _BurnRect ("Banner rect, canvas space (x, y, w, h)", Vector) = (0, 0, 1, 1)
        [HideInInspector] _Burn ("Burn front", Float) = 0
        [HideInInspector] _Unroll ("Unrolled", Range(0, 1)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 bannerUV : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _BannerSize;
            float _NoiseCellLarge, _NoiseCellSmall, _NoiseWeight, _CornerStretch;
            float _EmberWidth, _CharWidth, _ScorchWidth;
            fixed4 _EmberHot, _EmberCool, _CharColor, _ScorchTint;
            float4 _BurnRect;
            float _Burn, _Unroll;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                // UI vertices are in the root canvas' space: the banner's own 0..1 coordinates, whatever the graphic.
                o.bannerUV = (v.vertex.xy - _BurnRect.xy) / _BurnRect.zw;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // 0 where the fire starts (lower left corner) .. about 1 where it ends (upper right).
            float BurnField(float2 uv)
            {
                float2 fromEnd = float2(1.0 - uv.x, (1.0 - uv.y) * _CornerStretch);
                float gradient = 1.0 - length(fromEnd) / 1.06;
                float2 px = uv * _BannerSize.xy;
                float noise = ValueNoise(px / _NoiseCellLarge) * 0.7 + ValueNoise(px / _NoiseCellSmall + 17.0) * 0.3;
                return gradient * (1.0 - _NoiseWeight) + noise * _NoiseWeight;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = (tex2D(_MainTex, i.texcoord) + _TextureSampleAdd) * i.color;

                // Unrolling: only the centre part, _Unroll wide, is open.
                clip(_Unroll * 0.5 - abs(i.bannerUV.x - 0.5));
                // Not lit yet: the corner where the fire starts must not look charred already.
                if (_Burn <= 0) return c;

                float v = BurnField(i.bannerUV) - _Burn;
                clip(v);
                if (v < _EmberWidth)
                {
                    c.rgb = lerp(_EmberHot.rgb, _EmberCool.rgb, v / _EmberWidth);
                }
                else if (v < _CharWidth)
                {
                    float k = (v - _EmberWidth) / (_CharWidth - _EmberWidth);
                    c.rgb = lerp(_CharColor.rgb, c.rgb * _ScorchTint.rgb, k);
                }
                else if (v < _ScorchWidth)
                {
                    float k = (v - _CharWidth) / (_ScorchWidth - _CharWidth);
                    c.rgb *= lerp(_ScorchTint.rgb, 1.0, k);
                }
                return c;
            }
            ENDCG
        }
    }
}

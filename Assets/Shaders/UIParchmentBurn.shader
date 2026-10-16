// The night parchment (board task T21, mockups tour-de-nuit v5 C3): the default UI shader plus a burn that every graphic
// of the banner shares (the parchment and its text), so they burn along one front. The front is a field over
// the banner's own rect (_BurnRect, canvas space, set by NightTurnBanner): a gradient from the lower left corner mixed
// with fractal noise, crawling a little over time. Where the field is under _Burn the sheet is gone but glows along the
// edge; just ahead of it a flickering ember line, then an irregular band of charred paper with sparks, then a browning
// scorch. _Unroll clips the sheet from the centre outwards while it unrolls.
// Everything under "Burn" is there to be tuned on the material (Assets/Art/Materials/UI/ParchmentBurn.mat).
Shader "CdP/UI Parchment Burn"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Burn front)]
        _BannerSize ("Banner size in reference pixels (noise scale)", Vector) = (1060, 120, 0, 0)
        _NoiseCell ("Edge noise, largest cell (px)", Float) = 70
        _NoiseWeight ("Edge noise weight (0 = smooth front)", Range(0, 1)) = 0.22
        _CornerStretch ("Front shape: vertical squash of the corner distance", Range(0.05, 1)) = 0.35
        _Crawl ("Edge crawl speed", Float) = 1.5
        _CrawlAmount ("Edge crawl amount", Range(0, 0.05)) = 0.012

        [Header(Ember edge)]
        _EmberWidth ("Ember line width", Range(0.001, 0.05)) = 0.013
        [HDR] _EmberHot ("Ember core", Color) = (1, 0.72, 0.22, 1)
        [HDR] _EmberCool ("Ember outer", Color) = (0.9, 0.22, 0.03, 1)
        _GlowWidth ("Glow into the burnt hole", Range(0, 0.08)) = 0.03
        _GlowStrength ("Glow strength", Range(0, 1)) = 0.55
        _Flicker ("Flicker", Range(0, 1)) = 0.35

        [Header(Char and scorch)]
        _CharWidth ("Charred band (from the front)", Range(0.005, 0.2)) = 0.04
        _CharColor ("Charred paper", Color) = (0.05, 0.025, 0.015, 1)
        _CharEdge ("Charred paper, toward the scorch", Color) = (0.24, 0.12, 0.05, 1)
        _Sparks ("Sparks in the char", Range(0, 1)) = 0.35
        _ScorchWidth ("Scorched band (from the front)", Range(0.01, 0.4)) = 0.1
        _ScorchTint ("Scorched tint (multiplies)", Color) = (0.62, 0.45, 0.3, 1)

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
            float _NoiseCell, _NoiseWeight, _CornerStretch, _Crawl, _CrawlAmount;
            float _EmberWidth, _GlowWidth, _GlowStrength, _Flicker;
            float _CharWidth, _Sparks, _ScorchWidth;
            fixed4 _EmberHot, _EmberCool, _CharColor, _CharEdge, _ScorchTint;
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
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
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

            // Four octaves, largest cell _NoiseCell pixels: a ragged edge with fine tears.
            float Fbm(float2 px)
            {
                float2 p = px / _NoiseCell;
                return ValueNoise(p) * 0.55 + ValueNoise(p * 2.13 + 7.1) * 0.27
                     + ValueNoise(p * 4.37 + 3.7) * 0.13 + ValueNoise(p * 9.11 + 1.3) * 0.05;
            }

            // 0 where the fire starts (lower left corner) .. about 1 where it ends (upper right).
            float BurnField(float2 uv, float2 px)
            {
                float2 fromEnd = float2(1.0 - uv.x, (1.0 - uv.y) * _CornerStretch);
                float gradient = 1.0 - length(fromEnd) / 1.06;
                float crawl = (ValueNoise(px / 9.0 + _Time.y * _Crawl) - 0.5) * _CrawlAmount;
                return gradient * (1.0 - _NoiseWeight) + Fbm(px) * _NoiseWeight + crawl;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.texcoord);
                fixed4 c = (tex + _TextureSampleAdd) * i.color;

                // Unrolling: only the centre part, _Unroll wide, is open.
                clip(_Unroll * 0.5 - abs(i.bannerUV.x - 0.5));
                // Not lit yet: the corner where the fire starts must not look charred already.
                if (_Burn <= 0) return c;

                float2 px = i.bannerUV * _BannerSize.xy;
                float d = BurnField(i.bannerUV, px) - _Burn;   // distance ahead of the front
                float flicker = 1.0 - _Flicker * ValueNoise(px / 6.0 + _Time.y * 9.0);

                if (d < 0)
                {
                    // Burnt away; a glow hugs the edge, on the sheet itself only (tex.a: not a glyph's empty quad).
                    float k = saturate(1.0 + d / max(_GlowWidth, 1e-4));
                    float a = k * k * _GlowStrength * flicker * tex.a;
                    clip(a - 0.004);
                    return fixed4(_EmberCool.rgb, a);
                }
                if (d < _EmberWidth)
                {
                    c.rgb = lerp(_EmberHot.rgb, _EmberCool.rgb, d / _EmberWidth) * lerp(1.0, flicker, 0.6);
                    return c;
                }
                // Irregular bands: the char and the scorch do not run parallel to the front.
                float charW = _EmberWidth + _CharWidth * (0.7 + 0.6 * ValueNoise(px / 35.0 + 31.0));
                float scorchW = charW + _ScorchWidth * (0.75 + 0.5 * ValueNoise(px / 90.0 + 5.0));
                if (d < charW)
                {
                    float k = (d - _EmberWidth) / max(charW - _EmberWidth, 1e-4);
                    float3 charred = lerp(_CharColor.rgb, _CharEdge.rgb, k * k);
                    // Sparks: a few hot specks near the front, twinkling.
                    float spark = step(0.9, ValueNoise(px / 2.5 + floor(_Time.y * 12.0) * 3.3)) * saturate(1.0 - k * 3.0) * _Sparks;
                    c.rgb = lerp(charred, _EmberHot.rgb, spark);
                    return c;
                }
                if (d < scorchW)
                {
                    float k = (d - charW) / (scorchW - charW);
                    // Browning: from the char's edge colour back to the paper, smoothly.
                    c.rgb = lerp(c.rgb * _ScorchTint.rgb * 0.75, c.rgb, smoothstep(0.0, 1.0, k));
                }
                return c;
            }
            ENDCG
        }
    }
}

// Floor of the seated first-person view (board task T11): the room's wood, laid as procedural planks (staggered rows,
// one shade per plank, a dark groove and a bevel between them), UNLIT so it reads the same whatever the scene lights do:
// the bevel and the wood's normal map are shaded by a fixed direction set on the material. Visible under the table and
// the cats, then fading to black in a circle centred on the table (_Center, _FadeStart -> _FadeEnd, world units), with
// a soft contact shadow under the table (a rounded box of half sizes _ShadowSize around _Center). Opaque; the camera
// clears to black around it (SeatedSurroundings).
Shader "Corruption/Seated Floor"
{
    Properties
    {
        [MainTexture] _BaseMap("Wood", 2D) = "white" {}
        [Normal] _NormalMap("Wood normal map", 2D) = "bump" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _Brightness("Brightness", Range(0.0, 2.0)) = 0.6
        _Tile("Wood texture tiles per world unit", Float) = 0.055

        _PlankSize("Plank width (x) and length (z), world units", Vector) = (1.6, 9, 0, 0)
        _PlankShade("Shade spread between planks", Range(0.0, 1.0)) = 0.25
        _GrooveWidth("Groove half width", Float) = 0.05
        _GrooveDarkness("Groove darkness", Range(0.0, 1.0)) = 0.8
        _BevelWidth("Bevel width", Float) = 0.12
        _ReliefDirection("Relief light direction (world)", Vector) = (0.3, 1, 0.2, 0)
        _Relief("Relief strength", Range(0.0, 1.0)) = 0.6

        _Center("Fade centre (world xz)", Vector) = (0,0,0,0)
        _FadeStart("Fade start radius", Float) = 25
        _FadeEnd("Fade end radius (black)", Float) = 40

        _ShadowSize("Contact shadow half size (world x, z)", Vector) = (12,8,0,0)
        _ShadowSoftness("Contact shadow softness", Float) = 4
        _ShadowStrength("Contact shadow strength", Range(0.0, 1.0)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _NormalMap_ST;
                half4 _BaseColor;
                half _Brightness;
                float _Tile;
                float4 _PlankSize;
                half _PlankShade;
                float _GrooveWidth;
                half _GrooveDarkness;
                float _BevelWidth;
                float4 _ReliefDirection;
                half _Relief;
                float4 _Center;
                float _FadeStart;
                float _FadeEnd;
                float4 _ShadowSize;
                float _ShadowSoftness;
                half _ShadowStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz;

                // Planks: rows along x (width), each row's joints staggered along z (length) by a random offset.
                float row = floor(p.x / _PlankSize.x);
                float along = p.y / _PlankSize.y + Hash(float2(row, 7.0));
                float column = floor(along);
                float2 cell = float2(frac(p.x / _PlankSize.x), frac(along));
                float plankId = Hash(float2(row, column));

                // Distance (world units) to the plank's long edges and to its ends.
                float toSide = min(cell.x, 1.0 - cell.x) * _PlankSize.x;
                float toEnd = min(cell.y, 1.0 - cell.y) * _PlankSize.y;
                float toEdge = min(toSide, toEnd);

                // Wood: the grain runs along the plank (texture v along z), each plank cut from another part of it.
                float2 uv = float2(p.x, p.y) * _Tile + float2(plankId * 3.7, plankId * 11.3);
                half3 wood = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                wood *= lerp(1.0h - _PlankShade, 1.0h + _PlankShade * 0.5h, (half)plankId);

                // Relief: the wood's normal map plus a bevel at each plank's edges, lit by a fixed direction.
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv));
                // Fade the plank lines out where they get thinner than a pixel (no shimmering far away).
                float pixel = max(fwidth(toEdge), 1e-4);
                half lineFade = saturate(1.0 - (pixel - _GrooveWidth) / (_GrooveWidth * 2.0));
                half bevelFade = saturate(1.0 - (pixel - _BevelWidth) / (_BevelWidth * 2.0));
                float bevelSide = 1.0 - smoothstep(0.0, _BevelWidth, toSide);
                float bevelEnd = 1.0 - smoothstep(0.0, _BevelWidth, toEnd);
                float2 tilt = float2((cell.x < 0.5 ? -1.0 : 1.0) * bevelSide, (cell.y < 0.5 ? -1.0 : 1.0) * bevelEnd) * bevelFade;
                half3 normalWS = normalize(half3(normalTS.x + tilt.x, normalTS.z, normalTS.y + tilt.y));
                half relief = saturate(dot(normalWS, normalize(_ReliefDirection.xyz)));
                relief = lerp(1.0h, relief, _Relief);
                half groove = (1.0h - smoothstep(_GrooveWidth, _GrooveWidth + pixel, toEdge)) * lineFade;

                half3 colour = wood * _BaseColor.rgb * _Brightness * relief * (1.0h - _GrooveDarkness * groove);

                float2 offset = p - _Center.xz;
                // Circle of visibility around the table, fading to black.
                half visible = 1.0h - smoothstep(_FadeStart, _FadeEnd, length(offset));
                // Contact shadow: a rounded box (the table's footprint) blurred by the softness.
                float2 outside = max(abs(offset) - _ShadowSize.xy, 0.0);
                half shadow = 1.0h - smoothstep(0.0, max(_ShadowSoftness, 0.001), length(outside));

                return half4(colour * visible * (1.0h - _ShadowStrength * shadow), 1.0h);
            }
            ENDHLSL
        }
    }
}

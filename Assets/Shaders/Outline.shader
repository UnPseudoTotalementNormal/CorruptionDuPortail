// Screen-space outline (OutlineFeature, Game.Rendering). The marked renderers are drawn into a mask (their outline
// style index), a jump flood turns the mask's boundary into a distance field, and the composite draws the line, the
// glow and the inner rim from that distance: same width all around, smooth edges, whatever the mesh looks like.
Shader "Hidden/CdP/Outline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        Cull Off

        // 0 Mask: override shader of the marked renderers. Their own material properties stay bound, so an alpha
        // clipped material (whiskers, cut-out cosmetics) keeps its cut-out shape in the mask.
        Pass
        {
            Name "OutlineMask"
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;

            float _OutlineMaskValue;
            float _OutlineUseDepth;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
                #endif
                // Only what the camera sees of the object: a cat half hidden by the table is outlined along the
                // table's edge, not through it.
                if (_OutlineUseDepth > 0.5)
                {
                    float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    float selfEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                    if (selfEye > sceneEye * 1.01 + 0.02)
                    {
                        discard;
                    }
                }
                return _OutlineMaskValue;
            }
            ENDHLSL
        }

        // 1 Jump flood seed: the mask pixels touching an unmarked pixel hold their own position, the rest nothing.
        Pass
        {
            Name "OutlineSeed"
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _OutlineTexelSize; // width, height, 1/width, 1/height

            float MaskAt(int2 pixel)
            {
                pixel = clamp(pixel, int2(0, 0), int2(_OutlineTexelSize.xy) - 1);
                return LOAD_TEXTURE2D_X(_BlitTexture, pixel).r;
            }

            float2 Frag(Varyings input) : SV_Target
            {
                int2 pixel = int2(input.positionCS.xy);
                if (MaskAt(pixel) <= 0.0)
                {
                    return float2(-1.0, -1.0);
                }
                bool edge = MaskAt(pixel + int2(1, 0)) <= 0.0 || MaskAt(pixel - int2(1, 0)) <= 0.0
                    || MaskAt(pixel + int2(0, 1)) <= 0.0 || MaskAt(pixel - int2(0, 1)) <= 0.0;
                return edge ? input.positionCS.xy : float2(-1.0, -1.0);
            }
            ENDHLSL
        }

        // 2 Jump flood step: keep the nearest seed among the 9 neighbours at _OutlineJumpStep pixels.
        Pass
        {
            Name "OutlineJump"
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _OutlineTexelSize;
            float _OutlineJumpStep;

            float2 Frag(Varyings input) : SV_Target
            {
                float2 position = input.positionCS.xy;
                int2 pixel = int2(position);
                int step = (int)_OutlineJumpStep;
                int2 size = int2(_OutlineTexelSize.xy);
                float2 best = float2(-1.0, -1.0);
                float bestDistance = 1e20;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        int2 tap = pixel + int2(x, y) * step;
                        if (any(tap < 0) || any(tap >= size))
                        {
                            continue;
                        }
                        float2 seed = LOAD_TEXTURE2D_X(_BlitTexture, tap).xy;
                        if (seed.x < 0.0)
                        {
                            continue;
                        }
                        float2 delta = seed - position;
                        float distanceSq = dot(delta, delta);
                        if (distanceSq < bestDistance)
                        {
                            bestDistance = distanceSq;
                            best = seed;
                        }
                    }
                }
                return best;
            }
            ENDHLSL
        }

        // 3 Composite over the camera colour, premultiplied: line + glow outside the object, rim inside it.
        Pass
        {
            Name "OutlineComposite"
            ZTest Always
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define MAX_OUTLINE_STYLES 8

            TEXTURE2D(_OutlineMask);
            float4 _OutlineTexelSize;
            float _OutlineScale;
            // Per style: colour (rgb may go past 1 to feed the bloom, a = line opacity);
            // shape = line width, line softness, glow radius, glow intensity (pixels at 1080p);
            // extra = rim width, rim intensity, pulse speed (Hz), pulse amount.
            float4 _OutlineColors[MAX_OUTLINE_STYLES];
            float4 _OutlineShapes[MAX_OUTLINE_STYLES];
            float4 _OutlineExtras[MAX_OUTLINE_STYLES];

            int StyleAt(int2 pixel)
            {
                return (int)round(LOAD_TEXTURE2D(_OutlineMask, pixel).r * 255.0) - 1;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 position = input.positionCS.xy;
                int2 pixel = int2(position);
                float2 seed = LOAD_TEXTURE2D_X(_BlitTexture, pixel).xy;
                if (seed.x < 0.0)
                {
                    return 0;
                }
                float dist = length(seed - position);
                int own = StyleAt(pixel);
                int style = own >= 0 ? own : StyleAt(int2(seed));
                if (style < 0 || style >= MAX_OUTLINE_STYLES)
                {
                    return 0;
                }

                float4 color = _OutlineColors[style];
                float4 shape = _OutlineShapes[style] * float4(_OutlineScale, _OutlineScale, _OutlineScale, 1.0);
                float4 extra = _OutlineExtras[style];

                float alpha;
                if (own >= 0)
                {
                    // Inside the object: a rim of light along its silhouette.
                    float rimWidth = extra.x * _OutlineScale;
                    float rim = rimWidth > 0.0 ? saturate(1.0 - dist / rimWidth) : 0.0;
                    alpha = extra.y * rim * rim;
                }
                else
                {
                    // Outside: the adjacent pixel is at distance 1. Full line up to the width, then a soft fall-off.
                    float width = shape.x;
                    float softness = max(shape.y, 0.5);
                    float lineAlpha = width > 0.0 ? saturate((width + 0.5 - dist) / softness + 0.5) : 0.0;
                    float glow = shape.z > 0.0 ? saturate(1.0 - max(dist - width, 0.0) / shape.z) : 0.0;
                    alpha = max(lineAlpha * color.a, shape.w * glow * glow);
                }

                if (extra.w > 0.0)
                {
                    float wave = 0.5 + 0.5 * sin(_Time.y * extra.z * 6.2831853);
                    alpha *= 1.0 - extra.w * wave;
                }
                alpha = saturate(alpha);
                return half4(color.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}

// Smoke of the blown-out vote candle (board task T19): soft puffs for a particle system, no texture. Each particle is a
// round puff with a little noise in it, tinted and faded by the particle's vertex colour (alpha blended).
Shader "Corruption/Candle Smoke"
{
    Properties
    {
        _Softness("Edge softness", Range(0.05, 1)) = 0.6
        _NoiseScale("Noise scale", Float) = 3.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Smoke"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex SmokeVertex
            #pragma fragment SmokeFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Softness;
                float _NoiseScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0; // xy = quad uv, z = random per particle (custom vertex stream)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float3 uv : TEXCOORD0;
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash12(i), Hash12(i + float2(1, 0)), f.x),
                            lerp(Hash12(i + float2(0, 1)), Hash12(i + float2(1, 1)), f.x), f.y);
            }

            Varyings SmokeVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv.xyz;
                return output;
            }

            half4 SmokeFragment(Varyings input) : SV_Target
            {
                float2 p = input.uv.xy * 2.0 - 1.0;
                float r = length(p);
                float puff = 1.0 - smoothstep(1.0 - _Softness, 1.0, r);
                float n = Noise(input.uv.xy * _NoiseScale + input.uv.z * 17.0) * 0.6 + Noise(input.uv.xy * _NoiseScale * 2.3 + input.uv.z * 5.0) * 0.4;
                float a = puff * saturate(n * 1.6 - 0.2) * input.color.a;
                return half4(input.color.rgb, a);
            }
            ENDHLSL
        }
    }
}

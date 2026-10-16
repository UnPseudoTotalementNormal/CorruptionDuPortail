// The halo of the candle flame (board task T19): a soft additive glow in the air around the flame, on a quad that
// always faces the camera. VoteCandle drives _Strength with the flame's flicker. Quad mesh: x, y in [-0.5, 0.5].
Shader "Corruption/Candle Glow"
{
    Properties
    {
        [HDR] _Color("Colour", Color) = (1.0, 0.55, 0.2, 1)
        _Strength("Strength", Range(0, 2)) = 1
        _Power("Falloff power", Float) = 2.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex GlowVertex
            #pragma fragment GlowFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Strength;
                half _Power;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings GlowVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                float scale = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 positionWS = originWS + (right * input.positionOS.x + up * input.positionOS.y) * scale;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.positionOS.xy * 2.0;
                return output;
            }

            half4 GlowFragment(Varyings input) : SV_Target
            {
                float d = saturate(1.0 - length(input.uv));
                return half4(_Color.rgb * pow(d, _Power) * _Strength, 1);
            }
            ENDHLSL
        }
    }
}

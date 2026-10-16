// Candle flame (board task T19, the vote candle): a procedural flame drawn on a quad that always turns to the camera
// around the vertical axis. Shape = a teardrop whose edge is pushed by scrolling noise; colours from a blue base to a
// white core, a yellow body and an orange tip that thins out. HDR, additive: the bloom catches it.
// VoteCandle drives _Agitation (how nervous it is), _Lean (sideways bend) and _Strength (0 = out).
// The quad mesh is x in [-0.5, 0.5], y in [0, 1], the base of the flame at the object's origin.
Shader "Corruption/Candle Flame"
{
    Properties
    {
        [HDR] _CoreColor("Core", Color) = (6.0, 5.0, 3.6, 1)
        [HDR] _BodyColor("Body", Color) = (4.0, 2.2, 0.7, 1)
        [HDR] _TipColor("Tip", Color) = (2.2, 0.55, 0.12, 1)
        [HDR] _BaseColor("Blue base", Color) = (0.25, 0.4, 1.4, 1)
        _Agitation("Agitation", Range(0, 1)) = 0.15
        _Lean("Lean", Range(-1, 1)) = 0
        _Strength("Strength", Range(0, 1)) = 1
        _Speed("Noise speed", Float) = 2.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Flame"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex FlameVertex
            #pragma fragment FlameFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _BodyColor;
                half4 _TipColor;
                half4 _BaseColor;
                half _Agitation;
                half _Lean;
                half _Strength;
                float _Speed;
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

            float Fbm(float2 p)
            {
                return Noise(p) * 0.6 + Noise(p * 2.1 + 7.3) * 0.3 + Noise(p * 4.3 + 1.7) * 0.1;
            }

            Varyings FlameVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Turn the quad to the camera. Seen from the side it stands up along the world vertical; seen from
                // above (the night's top view) the vertical shrinks on screen, so the flame folds into a short bright
                // blob instead of vanishing edge-on.
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 toCamera = normalize(GetCameraPositionWS() - originWS);
                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 worldUp = float3(0, 1, 0);
                float3 upOnScreen = worldUp - dot(worldUp, toCamera) * toCamera;
                float side = length(upOnScreen);
                float3 up = side > 0.2 ? upOnScreen / side : normalize(lerp(UNITY_MATRIX_V[1].xyz, upOnScreen / max(side, 1e-4), side / 0.2));
                float3 right = normalize(cross(up, toCamera));
                if (dot(right, camRight) < 0) right = -right;
                float scaleX = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float scaleY = length(float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y)) * max(side, 0.35);
                float y = input.positionOS.y;
                // The bend grows with height, so the base stays on the wick.
                float x = input.positionOS.x + _Lean * 0.35 * y * y;
                float3 positionWS = originWS + right * (x * scaleX) + up * (y * scaleY);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = float2(input.positionOS.x, y);
                return output;
            }

            half4 FlameFragment(Varyings input) : SV_Target
            {
                float t = saturate(input.uv.y);
                float time = _Time.y * _Speed;

                // The edge wavers more toward the tip and more when agitated.
                float wobble = (Fbm(float2(t * 3.0 - time, input.uv.x * 2.0)) - 0.5) * (0.06 + 0.34 * _Agitation);
                float x = input.uv.x + wobble * t * 1.6 - _Lean * 0.12 * t * t;
                // Height flicker: the tip breathes.
                float stretch = 1.0 + (Fbm(float2(time * 0.7, 3.1)) - 0.5) * (0.12 + 0.5 * _Agitation);
                float ts = saturate(t / stretch);

                // Teardrop: widest at a third of the height, round base, pointed tip.
                float r = 0.42 * pow(max(ts, 1e-4), 0.45) * pow(saturate(1.0 - ts), 0.9) / 0.421;
                float body = saturate((r - abs(x)) / max(r * 0.55, 0.01));
                body *= saturate((1.0 - ts) * 6.0);

                float rCore = r * 0.5;
                float core = saturate((rCore - abs(x)) / max(rCore * 0.8, 0.005)) * saturate((0.62 - ts) * 4.0) * saturate(ts * 6.0);

                // Blue at the root, around the wick.
                float blue = saturate(1.0 - ts * 5.5) * saturate((r * 1.1 - abs(x)) / max(r * 0.6, 0.01));

                half3 col = lerp(_BodyColor.rgb, _TipColor.rgb, smoothstep(0.35, 0.95, ts)) * body;
                col += _CoreColor.rgb * core * core;
                col = lerp(col, _BaseColor.rgb * body, blue * 0.75);
                return half4(col * _Strength, 1);
            }
            ENDHLSL
        }
    }
}

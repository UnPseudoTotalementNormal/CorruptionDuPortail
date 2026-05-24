Shader "Hidden/BackgroundBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float _Spread;

        // Gaussien séparable 9 taps. dir = (1,0) horizontal, (0,1) vertical.
        half4 GaussianBlur(float2 uv, float2 dir)
        {
            float2 step = _BlitTexture_TexelSize.xy * dir * _Spread;

            half4 col  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv)              * 0.227027;
            col += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + step * 1.0) * 0.1945946;
            col += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - step * 1.0) * 0.1945946;
            col += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + step * 2.0) * 0.1216216;
            col += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - step * 2.0) * 0.1216216;
            col += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + step * 3.0) * 0.054054;
            col += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - step * 3.0) * 0.054054;
            col += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + step * 4.0) * 0.016216;
            col += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - step * 4.0) * 0.016216;
            return col;
        }
        ENDHLSL

        Pass
        {
            Name "BlurHorizontal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return GaussianBlur(input.texcoord, float2(1.0, 0.0));
            }
            ENDHLSL
        }

        Pass
        {
            Name "BlurVertical"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return GaussianBlur(input.texcoord, float2(0.0, 1.0));
            }
            ENDHLSL
        }
    }
}

// The cats' shader: the artist's Blender look (two-tone toon + diffuse, glowing parts; see CatToon.hlsl) with the
// per-renderer screen-door DITHER FADE of "Corruption/Cat Dither Lit" (_CatFadeAmount, CatDither.hlsl) clipped in every
// pass that draws the body. Lit's property names are kept (_BaseMap, _BaseColor, _Cutoff, _Cull, _EmissionMap…), so a
// material moves from "Cat Dither Lit" to this shader with its textures and settings, and the code that drives them
// (CatAppearance, AvatarVisibilityController, CatCosmeticsAssetBuilder) does not change.
Shader "Corruption/Cat Toon"
{
    Properties
    {
        // Emote self-view fade: 1 = fully visible, 0 = fully clipped. Overridden per renderer via MPB.
        _CatFadeAmount("Dither Fade Amount", Range(0.0, 1.0)) = 1.0

        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        [Header(Toon)]
        _ToonMix("Flat two-tone share (Blender Mix Shader)", Range(0.0, 1.0)) = 0.5
        _ToonThreshold("Lit side from (share of the light)", Range(0.0, 1.0)) = 0.5
        _ToonSoftness("Cut softness", Range(0.0, 0.5)) = 0.02
        _ToonShadow("Shadow side value", Range(0.0, 1.0)) = 0.5
        _ToonSelfLight("Flat half ignores the lights", Range(0.0, 1.0)) = 0.0
        _ToonChannel("Light channel read for the cut", Vector) = (0, 0, 1, 0)

        [Header(Emission layer 1)]
        [NoScaleOffset] _EmissionMap("Mask (R)", 2D) = "black" {}
        [HDR] _EmissionColor("Colour", Color) = (0,0,0)
        _EmissionCover("Covers the surface by mask", Range(0.0, 1.0)) = 1.0
        _EmissionCoverBias("Covers the surface everywhere", Range(0.0, 1.0)) = 0.0
        [ToggleUI] _EmissionScaleByMask("Strength follows the mask", Float) = 0.0

        [Header(Emission layer 2)]
        [NoScaleOffset] _Emission2Map("Mask (R)", 2D) = "black" {}
        [HDR] _Emission2Color("Colour", Color) = (0,0,0)
        _Emission2Cover("Covers the surface by mask", Range(0.0, 1.0)) = 1.0
        _Emission2CoverBias("Covers the surface everywhere", Range(0.0, 1.0)) = 0.0
        [ToggleUI] _Emission2ScaleByMask("Strength follows the mask", Float) = 0.0

        // Lit inputs the stock pass files may read (kept so LitInput's layout and the copied settings hold).
        [HideInInspector] _WorkflowMode("WorkflowMode", Float) = 1.0
        [HideInInspector] _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0
        [HideInInspector] _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _MetallicGlossMap("Metallic", 2D) = "white" {}
        [HideInInspector] _SpecColor("Specular", Color) = (0, 0, 0)
        [HideInInspector] _SpecGlossMap("Specular", 2D) = "white" {}
        [HideInInspector] _BumpScale("Scale", Float) = 1.0
        [HideInInspector] _BumpMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _Parallax("Scale", Range(0.005, 0.08)) = 0.005
        [HideInInspector] _ParallaxMap("Height Map", 2D) = "black" {}
        [HideInInspector] _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _OcclusionMap("Occlusion", 2D) = "white" {}
        [HideInInspector] _DetailMask("Detail Mask", 2D) = "white" {}
        [HideInInspector] _DetailAlbedoMapScale("Scale", Range(0.0, 2.0)) = 1.0
        [HideInInspector] _DetailAlbedoMap("Detail Albedo x2", 2D) = "linearGrey" {}
        [HideInInspector] _DetailNormalMapScale("Scale", Range(0.0, 2.0)) = 1.0
        [HideInInspector] _DetailNormalMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _ClearCoatMask("_ClearCoatMask", Float) = 0.0
        [HideInInspector] _ClearCoatSmoothness("_ClearCoatSmoothness", Float) = 0.0

        // Render state (same names as Lit: the builder and the copied materials set them).
        [HideInInspector] _Surface("__surface", Float) = 0.0
        [HideInInspector] _Blend("__blend", Float) = 0.0
        _Cull("Cull (0 = both faces)", Float) = 2.0
        [ToggleUI] _AlphaClip("Alpha clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
        [HideInInspector] _AddPrecomputedVelocity("_AddPrecomputedVelocity", Float) = 0.0
        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        [HideInInspector] _QueueOffset("Queue offset", Float) = 0.0

        // ObsoleteProperties
        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        // ------------------------------------------------------------------
        // Forward only: the toon lighting has no GBuffer equivalent (drawn the same way by Forward, Forward+ and
        // Deferred renderers).
        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForwardOnly"
            }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex LitPassVertex
            #pragma fragment CatToonPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "CatToonInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            #include "CatDither.hlsl"
            #include "CatToon.hlsl"

            void CatToonPassFragment(
                Varyings input
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                CatDitherClip(input.positionCS);

                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv, surfaceData);

            #ifdef LOD_FADE_CROSSFADE
                LODFadeCrossFade(input.positionCS);
            #endif

                InputData inputData;
                InitializeInputData(input, surfaceData.normalTS, inputData);
                InitializeBakedGIData(input, inputData);

                half3 colour = CatToonShade(inputData, surfaceData, input.uv);
                colour = MixFog(colour, inputData.fogCoord);
                outColor = half4(colour, OutputAlpha(surfaceData.alpha, IsSurfaceTypeTransparent()));

            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragmentDither

            #pragma shader_feature_local _ALPHATEST_ON

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #pragma multi_compile _ LOD_FADE_CROSSFADE
            // Directional and punctual light shadows apply Normal Bias differently.
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "CatToonInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            #include "CatDither.hlsl"

            // Fade the SHADOW too: otherwise a dissolving body keeps casting a solid silhouette.
            half4 ShadowPassFragmentDither(Varyings input) : SV_TARGET
            {
                CatDitherClip(input.positionCS);
                return ShadowPassFragment(input);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragmentDither

            #pragma shader_feature_local _ALPHATEST_ON

            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "CatToonInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            #include "CatDither.hlsl"

            half DepthOnlyFragmentDither(Varyings input) : SV_TARGET
            {
                CatDitherClip(input.positionCS);
                return DepthOnlyFragment(input);
            }
            ENDHLSL
        }

        // Drawn to _CameraNormalsTexture (the screen outline reads it).
        Pass
        {
            Name "DepthNormals"
            Tags
            {
                "LightMode" = "DepthNormals"
            }

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragmentDither

            #pragma shader_feature_local _ALPHATEST_ON

            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "CatToonInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            #include "CatDither.hlsl"

            void DepthNormalsFragmentDither(
                Varyings input
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                CatDitherClip(input.positionCS);
            #ifdef _WRITE_RENDERING_LAYERS
                DepthNormalsFragment(input, outNormalWS, outRenderingLayers);
            #else
                DepthNormalsFragment(input, outNormalWS);
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG

            HLSLPROGRAM
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY

            #include "CatToonInput.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

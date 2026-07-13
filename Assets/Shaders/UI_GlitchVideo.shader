Shader "Custom/UI_GlitchVideo"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,1)

        [Header(Glitch)]
        _GlitchIntensity ("Glitch Intensity", Range(0, 1)) = 1
        _GlitchSpeed ("Glitch Speed", Float) = 12
        _BlockIntensity ("Block Displacement", Range(0, 1)) = 0.6
        _BlockCount ("Block Count", Float) = 14
        _RGBSplit ("RGB Split", Range(0, 1)) = 0.5
        _JitterIntensity ("Line Jitter", Range(0, 1)) = 0.35
        _ScanlineIntensity ("Scanline Intensity", Range(0, 1)) = 0.25
        _ScanlineCount ("Scanline Count", Float) = 220
        _NoiseIntensity ("Static Noise", Range(0, 1)) = 0.2
        _ColorDrift ("Color Drift", Range(0, 1)) = 0.3

        // Required for UI Mask component
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
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline"    = "UniversalPipeline"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull     Off
        Lighting Off
        ZWrite   Off
        ZTest    [unity_GUIZTestMode]
        Blend    SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UIGlitchVideo"

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float  _GlitchIntensity;
                float  _GlitchSpeed;
                float  _BlockIntensity;
                float  _BlockCount;
                float  _RGBSplit;
                float  _JitterIntensity;
                float  _ScanlineIntensity;
                float  _ScanlineCount;
                float  _NoiseIntensity;
                float  _ColorDrift;
            CBUFFER_END

            // Renseignés par uGUI au moment du rendu (texte legacy / RectMask2D).
            half4  _TextureSampleAdd;
            float4 _ClipRect;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
                float2 canvasPos   : TEXCOORD2;
            };

            // Hash sans sin() (Dave Hoskins) : stable sur toutes les plateformes.
            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color       = IN.color * _Color;
                OUT.screenPos   = ComputeScreenPos(OUT.positionHCS);
                OUT.canvasPos   = IN.positionOS.xy; // espace canvas, requis par RectMask2D
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv       = IN.uv;
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

                // Temps quantifié : un glitch vidéo saute d'un état au suivant, il ne glisse pas.
                float glitchFrame = floor(_Time.y * _GlitchSpeed);

                // Rafales : alternance accalmie / pic, sinon l'effet est trop régulier.
                float burst = lerp(0.25, 1.0, step(0.55, Hash11(glitchFrame * 0.173 + 0.5)));
                float gi    = _GlitchIntensity * burst;

                // Déplacement horizontal par blocs. Bandes calculées en espace ÉCRAN :
                // tous les Graphics qui partagent le matériau glitchent aux mêmes hauteurs,
                // donc la carte entière se déchire d'un seul tenant.
                float band     = floor(screenUV.y * _BlockCount);
                float bandGate = step(1.0 - 0.4 * gi, Hash21(float2(band * 1.7 + 3.0, glitchFrame)));
                uv.x += (Hash21(float2(band, glitchFrame)) - 0.5) * 2.0 * bandGate * _BlockIntensity * 0.12 * gi;

                // Jitter fin par ligne (tracking VHS).
                float row     = floor(screenUV.y * 480.0);
                float rowGate = step(0.85, Hash21(float2(row, glitchFrame * 3.0 + 7.0)));
                uv.x += (Hash21(float2(row, glitchFrame)) - 0.5) * rowGate * _JitterIntensity * 0.03 * gi;

                // Séparation RGB : canaux R et B décalés horizontalement.
                float split = _RGBSplit * (0.3 + 0.7 * Hash11(glitchFrame * 0.531)) * 0.012 * gi;
                half4 texC = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) + _TextureSampleAdd;
                half4 texR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(split, 0.0)) + _TextureSampleAdd;
                half4 texB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(split, 0.0)) + _TextureSampleAdd;
                half4 col  = half4(texR.r, texC.g, texB.b, texC.a);

                // Dérive de couleur : légère rotation des canaux, ondulante le long de Y.
                float drift = _ColorDrift * gi * (0.5 + 0.5 * sin(_Time.y * 4.0 + screenUV.y * 14.0));
                col.rgb = lerp(col.rgb, col.brg, drift * 0.2);

                // Scanlines (espace écran, continues sur toute la carte).
                float scan = 0.5 + 0.5 * sin(screenUV.y * _ScanlineCount * TWO_PI + _Time.y * 6.0);
                col.rgb *= 1.0 - _ScanlineIntensity * _GlitchIntensity * scan * 0.5;

                // Neige (bruit statique), grain de 2 px.
                float snow = Hash21(floor(screenUV * _ScreenParams.xy * 0.5) + glitchFrame * 31.0);
                col.rgb = lerp(col.rgb, float3(snow, snow, snow), _NoiseIntensity * gi * 0.6);

                // Teinte du Graphic + alpha du CanvasGroup (vertex color) : le fade reste fonctionnel.
                col *= IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                float2 inside = step(_ClipRect.xy, IN.canvasPos) * step(IN.canvasPos, _ClipRect.zw);
                col.a *= inside.x * inside.y;
                #endif

                return col;
            }
            ENDHLSL
        }
    }
}

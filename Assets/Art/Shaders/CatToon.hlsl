#ifndef CAT_TOON_INCLUDED
#define CAT_TOON_INCLUDED

// The look of the artist's Blender materials (Corrupted_Cats.blend), rebuilt for URP's lights.
//
// Every cat material in the .blend mixes, half and half (_ToonMix):
//   - a Diffuse BSDF: the texture lit by the lights (Lambert, no specular, no reflection);
//   - the "Toon Shader+" group: a FLAT two-tone colour, the texture on the lit side and the texture at half value
//     (_ToonShadow) on the other, cut sharply where the diffuse lighting crosses half (_ToonThreshold). That half is
//     plugged straight into the shader output, so Blender treats it as light emitted: it does not dim with the lights.
//     _ToonSelfLight = 1 keeps it so (the cat reads even in the dark); 0 multiplies it by the light reaching the cat.
// Then up to two emission layers replace part of the result (Blender's Mix Shader towards an Emission):
//   colour = lerp(colour, emissionColour * (mask or 1), saturate(mask * cover + coverBias)).
//
// The two-tone cut is taken on the lights' channel _ToonChannel (blue in the .blend), NORMALISED by the light that
// reaches the point: it then depends on the angle to the lights and on shadows, not on their intensity, so the same
// material cuts at the same place under the Blender sun, the menu's lights and the seated view's spot.

struct CatToonLighting
{
    half3 irradiance; // sum of light colour * attenuation * shadow * N.L (+ ambient): the diffuse half's light
    half3 reach;      // sum of light colour * attenuation (+ ambient): what reaches the point, facing or not
};

void CatToonAddLight(Light light, half3 normalWS, inout CatToonLighting lighting)
{
    half3 colour = light.color * light.distanceAttenuation;
    lighting.reach += colour;
    lighting.irradiance += colour * light.shadowAttenuation * saturate(dot(normalWS, light.direction));
}

CatToonLighting CatToonGatherLights(InputData inputData, SurfaceData surfaceData)
{
    CatToonLighting lighting;
    lighting.irradiance = inputData.bakedGI;
    lighting.reach = inputData.bakedGI;

    uint meshRenderingLayers = GetMeshRenderingLayer();
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        CatToonAddLight(mainLight, inputData.normalWS, lighting);
    }

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            CatToonAddLight(light, inputData.normalWS, lighting);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            CatToonAddLight(light, inputData.normalWS, lighting);
        }
    LIGHT_LOOP_END
#endif

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    lighting.irradiance += inputData.vertexLighting;
    lighting.reach += inputData.vertexLighting;
#endif
    return lighting;
}

// One emission layer: Blender's Mix Shader towards an Emission whose factor (and maybe strength) is a mask.
half3 CatToonEmission(half3 colour, half mask, half3 emission, half scaleByMask, half cover, half coverBias)
{
    half3 emitted = emission * lerp(1.0h, mask, scaleByMask);
    return lerp(colour, emitted, saturate(mask * cover + coverBias));
}

half3 CatToonShade(InputData inputData, SurfaceData surfaceData, float2 uv)
{
    CatToonLighting lighting = CatToonGatherLights(inputData, surfaceData);
    half3 albedo = surfaceData.albedo;

    half3 diffuse = albedo * lighting.irradiance;

    half lit = dot(lighting.irradiance, _ToonChannel.rgb);
    half reached = dot(lighting.reach, _ToonChannel.rgb);
    half signal = lit / max(reached, 1e-4h);
    half litSide = smoothstep(_ToonThreshold - _ToonSoftness, _ToonThreshold + _ToonSoftness + 1e-4h, signal);
    half3 toon = albedo * lerp(_ToonShadow, 1.0h, litSide);
    toon *= lerp(saturate(lighting.reach), 1.0h, _ToonSelfLight);

    half3 colour = lerp(diffuse, toon, _ToonMix);

    half mask = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).r;
    colour = CatToonEmission(colour, mask, _EmissionColor.rgb, _EmissionScaleByMask, _EmissionCover, _EmissionCoverBias);
    half mask2 = SAMPLE_TEXTURE2D(_Emission2Map, sampler_Emission2Map, uv).r;
    colour = CatToonEmission(colour, mask2, _Emission2Color.rgb, _Emission2ScaleByMask, _Emission2Cover, _Emission2CoverBias);
    return colour;
}

#endif

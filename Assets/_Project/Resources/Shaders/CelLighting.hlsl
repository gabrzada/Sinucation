#ifndef FRIENDSLOP_CEL_LIGHTING_INCLUDED
#define FRIENDSLOP_CEL_LIGHTING_INCLUDED

// Reuse the installed URP's shadows, light cookies, light layers, AO and Forward+ loop.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Global, deliberately outside UnityPerMaterial: existing and newly spawned materials
// switch together when the player applies the graphics settings.
float _FSCelStyle; // 0 original PBR, 1 soft cel, 2 graphic cel

half FSCelRamp(half ndl, half graphic, half normalAA)
{
    half width = max(normalAA, lerp(0.075h, 0.018h, graphic));
    half soft = 0.22h * smoothstep(0.04h-width,0.04h+width,ndl)
              + 0.30h * smoothstep(0.38h-width,0.38h+width,ndl)
              + 0.48h * smoothstep(0.74h-width,0.74h+width,ndl);
    half hard = 0.43h * smoothstep(0.18h-width,0.18h+width,ndl)
              + 0.57h * smoothstep(0.64h-width,0.64h+width,ndl);
    return lerp(soft, hard, graphic);
}

URP_LIGHT_ACCUM3 FSCelLight(Light light, InputData inputData, SurfaceData surface, half graphic, half normalAA, bool specular)
{
    half ndl = saturate(dot(inputData.normalWS, light.direction));
    half ramp = FSCelRamp(ndl, graphic, normalAA);
    // Keep attenuation continuous: a point/spot light must fade without moving rings on the floor.
    URP_LIGHT_ACCUM3 radiance = light.color * light.distanceAttenuation * light.shadowAttenuation;
    half3 diffuse = surface.albedo * lerp(1.0h,0.70h,surface.metallic);
    half3 color = diffuse * ramp;
    if (specular)
    {
        half3 h = SafeNormalize(light.direction + inputData.viewDirectionWS);
        half highlight = pow(saturate(dot(inputData.normalWS,h)),lerp(16.0h,100.0h,surface.smoothness));
        half aa = max(min(normalAA*4.0h,0.20h),lerp(0.16h,0.04h,graphic));
        half spot = smoothstep(0.52h-aa,0.52h+aa,highlight) * smoothstep(0.0h,0.15h,ndl);
        color += lerp(half3(0.12h,0.12h,0.12h),surface.albedo*0.5h,surface.metallic) * spot;
    }
    return color * radiance;
}

URP_LIGHT_ACCUM4 FSCelFragmentPBR(InputData inputData, SurfaceData surfaceData,
    bool isSpecularSetup, bool useSpecularHighlights, bool useAlphaPremultiply,
    bool useClearCoat, bool receiveShadows, bool isSurfaceTypeTransparent, bool useEnvironmentReflections)
{
    // Original mode and transparent surfaces use their original shader in VisualFactory.

    half graphic = saturate(_FSCelStyle-1.0);
    // Evaluate derivatives before the variable-length clustered light loop.
    half normalAA = min(length(fwidth(inputData.normalWS)),0.12h);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(inputData,surfaceData,isSurfaceTypeTransparent);
    uint layers = GetMeshRenderingLayer();
    Light mainLight = GetMainLight(inputData,shadowMask,ao,receiveShadows,isSurfaceTypeTransparent);
    MixRealtimeAndBakedGI(mainLight,inputData.normalWS,inputData.bakedGI);
    LightingData lighting = CreateLightingData(inputData,surfaceData);
    // Retain ambient color, contact shadows and a restrained material-dependent reflection.
    lighting.giColor = inputData.bakedGI * surfaceData.albedo * surfaceData.occlusion *
        ao.indirectAmbientOcclusion * lerp(0.85h,0.65h,graphic);
    if (IsMatchingLightLayer(mainLight,layers))
        lighting.mainLightColor = FSCelLight(mainLight,inputData,surfaceData,graphic,normalAA,useSpecularHighlights);

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex=0; lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex,inputData,shadowMask,ao,receiveShadows,isSurfaceTypeTransparent);
        if (IsMatchingLightLayer(light,layers))
            lighting.additionalLightsColor += FSCelLight(light,inputData,surfaceData,graphic,normalAA,useSpecularHighlights);
    }
    #endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex,inputData,shadowMask,ao,receiveShadows,isSurfaceTypeTransparent);
        if (IsMatchingLightLayer(light,layers))
            lighting.additionalLightsColor += FSCelLight(light,inputData,surfaceData,graphic,normalAA,useSpecularHighlights);
    LIGHT_LOOP_END
#endif
#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    lighting.vertexLightingColor += inputData.vertexLighting * surfaceData.albedo;
#endif
    // A thin inner silhouette accent; emission remains untouched so displays stay readable.
    half facing = saturate(dot(inputData.normalWS,inputData.viewDirectionWS));
    half edge = 1.0h-smoothstep(0.035h,lerp(0.16h,0.23h,graphic),facing);
    half ink = 1.0h-edge*lerp(0.16h,0.42h,graphic);
    lighting.giColor *= ink;
    lighting.mainLightColor *= ink;
    lighting.additionalLightsColor *= ink;
    lighting.vertexLightingColor *= ink;
    return CalculateFinalColor(lighting,surfaceData.alpha);
}
#endif

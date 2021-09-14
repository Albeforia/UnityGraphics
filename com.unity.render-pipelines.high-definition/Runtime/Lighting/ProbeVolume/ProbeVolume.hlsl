#ifndef __PROBEVOLUME_HLSL__
#define __PROBEVOLUME_HLSL__

#include "Packages/com.unity.render-pipelines.high-definition-config/Runtime/ShaderConfig.cs.hlsl"

#ifndef PROBE_VOLUMES_SAMPLING_MODE
// Default to sampling probe volumes at native atlas encoding mode.
// Users can override this by defining PROBE_VOLUMES_SAMPLING_MODE before including LightLoop.hlsl
// TODO: It's likely we will want to extend this out to simply be shader LOD quality levels,
// as there are other parameters such as bilateral filtering, additive blending, and normal bias
// that we will want to disable for a low quality high performance mode.
#define PROBE_VOLUMES_SAMPLING_MODE SHADEROPTIONS_PROBE_VOLUMES_ENCODING_MODE
#endif

#ifndef PROBE_VOLUMES_BILATERAL_FILTERING_MODE
// Default to filtering probe volumes with mode specified in ShaderConfig.cs
// Users can override this by defining PROBE_VOLUMES_BILATERAL_FILTERING_MODE before including LightLoop.hlsl
#define PROBE_VOLUMES_BILATERAL_FILTERING_MODE SHADEROPTIONS_PROBE_VOLUMES_BILATERAL_FILTERING_MODE
#endif

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/ProbeVolume/ProbeVolume.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/ProbeVolume/ProbeVolumeLightLoopDef.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/ProbeVolume/ProbeVolumeAtlas.hlsl"

bool ProbeVolumeGetReflectionProbeNormalizationEnabled()
{
    return _ProbeVolumeReflectionProbeNormalizationParameters.x > 0.0f;
}

float ProbeVolumeGetReflectionProbeNormalizationWeight()
{
    return _ProbeVolumeReflectionProbeNormalizationParameters.x;
}

bool ProbeVolumeGetReflectionProbeNormalizationDCOnly()
{
    return _ProbeVolumeReflectionProbeNormalizationParameters.y >= 0.5f;
}

float ProbeVolumeGetReflectionProbeNormalizationMin()
{
    return _ProbeVolumeReflectionProbeNormalizationParameters.z;
}

float ProbeVolumeGetReflectionProbeNormalizationMax()
{
    return _ProbeVolumeReflectionProbeNormalizationParameters.w;
}

// Copied from VolumeVoxelization.compute
float ProbeVolumeComputeFadeFactor(
    float3 samplePositionBoxNDC,
    float depthWS,
    float3 rcpPosFaceFade,
    float3 rcpNegFaceFade,
    float rcpDistFadeLen,
    float endTimesRcpDistFadeLen)
{
    float3 posF = Remap10(samplePositionBoxNDC, rcpPosFaceFade, rcpPosFaceFade);
    float3 negF = Remap01(samplePositionBoxNDC, rcpNegFaceFade, 0);
    float  dstF = Remap10(depthWS, rcpDistFadeLen, endTimesRcpDistFadeLen);
    float  fade = posF.x * posF.y * posF.z * negF.x * negF.y * negF.z;

    return dstF * fade;
}

#if PROBE_VOLUMES_BILATERAL_FILTERING_MODE != PROBEVOLUMESBILATERALFILTERINGMODES_DISABLED
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/ProbeVolume/ProbeVolumeBilateralFilter.hlsl"
#endif

void ProbeVolumeComputeOBBBoundsToFrame(OrientedBBox probeVolumeBounds, out float3x3 obbFrame, out float3 obbExtents, out float3 obbCenter)
{
    obbFrame = float3x3(probeVolumeBounds.right, probeVolumeBounds.up, cross(probeVolumeBounds.right, probeVolumeBounds.up));
    obbExtents = float3(probeVolumeBounds.extentX, probeVolumeBounds.extentY, probeVolumeBounds.extentZ);
    obbCenter = probeVolumeBounds.center; 
}

void ProbeVolumeComputeTexel3DAndWeight(
    float weightHierarchy,
    ProbeVolumeEngineData probeVolumeData,
    float3x3 obbFrame,
    float3 obbExtents,
    float3 obbCenter,
    float3 samplePositionWS,
    float samplePositionLinearDepth,
    out float3 probeVolumeTexel3D,
    out float weight)
{
    float3 samplePositionBS = mul(obbFrame, samplePositionWS - obbCenter);
    float3 samplePositionBCS = samplePositionBS * rcp(obbExtents);
    float3 samplePositionBNDC = samplePositionBCS * 0.5 + 0.5;
    float3 probeVolumeUVW = clamp(samplePositionBNDC.xyz, 0.5 * probeVolumeData.resolutionInverse, 1.0 - probeVolumeData.resolutionInverse * 0.5);
    probeVolumeTexel3D = probeVolumeUVW * probeVolumeData.resolution;

    float fadeFactor = ProbeVolumeComputeFadeFactor(
        samplePositionBNDC,
        samplePositionLinearDepth,
        probeVolumeData.rcpPosFaceFade,
        probeVolumeData.rcpNegFaceFade,
        probeVolumeData.rcpDistFadeLen,
        probeVolumeData.endTimesRcpDistFadeLen
    );

    weight = fadeFactor * probeVolumeData.weight;

#if SHADEROPTIONS_PROBE_VOLUMES_ADDITIVE_BLENDING
    if (probeVolumeData.volumeBlendMode == VOLUMEBLENDMODE_ADDITIVE)
        weight = fadeFactor;
    else if (probeVolumeData.volumeBlendMode == VOLUMEBLENDMODE_SUBTRACTIVE)
        weight = -fadeFactor;
    else
#endif
    {
        // Alpha composite: weight = (1.0f - weightHierarchy) * fadeFactor;
        weight = weightHierarchy * -fadeFactor + fadeFactor;
    }
}

float3 ProbeVolumeEvaluateSphericalHarmonicsL0(float3 normalWS, ProbeVolumeSphericalHarmonicsL0 coefficients)
{

#ifdef DEBUG_DISPLAY
    if (_DebugProbeVolumeMode == PROBEVOLUMEDEBUGMODE_VISUALIZE_DEBUG_COLORS)
    {
        float3 debugColors = coefficients.data[0].rgb; 
        return debugColors;
    }
    else if (_DebugProbeVolumeMode == PROBEVOLUMEDEBUGMODE_VISUALIZE_VALIDITY)
    {
        float validity = coefficients.data[0].x;
        return lerp(float3(1, 0, 0), float3(0, 1, 0), validity);
    }
    else
#endif
    {
        float3 sampleOutgoingRadiance = coefficients.data[0].rgb;
        return sampleOutgoingRadiance;
    }
}

float3 ProbeVolumeEvaluateSphericalHarmonicsL1(float3 normalWS, ProbeVolumeSphericalHarmonicsL1 coefficients)
{

#ifdef DEBUG_DISPLAY
    if (_DebugProbeVolumeMode == PROBEVOLUMEDEBUGMODE_VISUALIZE_DEBUG_COLORS)
    {
        float3 debugColors = coefficients.data[0].rgb; 
        return debugColors;
    }
    else if (_DebugProbeVolumeMode == PROBEVOLUMEDEBUGMODE_VISUALIZE_VALIDITY)
    {
        float validity = coefficients.data[0].x;
        return lerp(float3(1, 0, 0), float3(0, 1, 0), validity);
    }
    else
#endif
    {
        float3 sampleOutgoingRadiance = SHEvalLinearL0L1(normalWS, coefficients.data[0], coefficients.data[1], coefficients.data[2]);
        return sampleOutgoingRadiance;
    }
}

float3 ProbeVolumeEvaluateSphericalHarmonicsL2(float3 normalWS, ProbeVolumeSphericalHarmonicsL2 coefficients)
{

#ifdef DEBUG_DISPLAY
    if (_DebugProbeVolumeMode == PROBEVOLUMEDEBUGMODE_VISUALIZE_DEBUG_COLORS)
    {
        float3 debugColors = coefficients.data[0].rgb; 
        return debugColors;
    }
    else if (_DebugProbeVolumeMode == PROBEVOLUMEDEBUGMODE_VISUALIZE_VALIDITY)
    {
        float validity = coefficients.data[0].x;
        return lerp(float3(1, 0, 0), float3(0, 1, 0), validity);
    }
    else
#endif
    {
        float3 sampleOutgoingRadiance = SampleSH9(coefficients.data, normalWS);
        return sampleOutgoingRadiance;
    }
}

// Fallback to global ambient probe lighting when probe volume lighting weight is not fully saturated.
float3 ProbeVolumeEvaluateAmbientProbeFallback(float3 normalWS, float weightHierarchy)
{
    float3 sampleAmbientProbeOutgoingRadiance = float3(0.0, 0.0, 0.0);
    if (weightHierarchy < 1.0
#ifdef DEBUG_DISPLAY
        && (_DebugProbeVolumeMode != PROBEVOLUMEDEBUGMODE_VISUALIZE_DEBUG_COLORS)
        && (_DebugProbeVolumeMode != PROBEVOLUMEDEBUGMODE_VISUALIZE_VALIDITY)
#endif
    )
    {

        sampleAmbientProbeOutgoingRadiance = SampleSH9(_ProbeVolumeAmbientProbeFallbackPackedCoeffs, normalWS) * (1.0 - weightHierarchy);
    }

    return sampleAmbientProbeOutgoingRadiance;
}

// Generate ProbeVolumeAccumulateSphericalHarmonicsL0 function:
#define PROBE_VOLUMES_ACCUMULATE_MODE PROBEVOLUMESENCODINGMODES_SPHERICAL_HARMONICS_L0
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/ProbeVolume/ProbeVolumeAccumulate.hlsl"
#undef PROBE_VOLUMES_ACCUMULATE_MODE

// Generate ProbeVolumeAccumulateSphericalHarmonicsL1 function:
#define PROBE_VOLUMES_ACCUMULATE_MODE PROBEVOLUMESENCODINGMODES_SPHERICAL_HARMONICS_L1
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/ProbeVolume/ProbeVolumeAccumulate.hlsl"
#undef PROBE_VOLUMES_ACCUMULATE_MODE

// Generate ProbeVolumeAccumulateSphericalHarmonicsL2 function:
#define PROBE_VOLUMES_ACCUMULATE_MODE PROBEVOLUMESENCODINGMODES_SPHERICAL_HARMONICS_L2
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/ProbeVolume/ProbeVolumeAccumulate.hlsl"
#undef PROBE_VOLUMES_ACCUMULATE_MODE

void ProbeVolumeEvaluateSphericalHarmonics(PositionInputs posInput, float3 normalWS, float3 backNormalWS, float3 reflectionDirectionWS, float3 viewDirectionWS, uint renderingLayers, float weightHierarchy, inout float3 bakeDiffuseLighting, inout float3 backBakeDiffuseLighting, inout float3 reflectionProbeNormalizationLighting, out float reflectionProbeNormalizationWeight)
{
#if PROBE_VOLUMES_SAMPLING_MODE == PROBEVOLUMESENCODINGMODES_SPHERICAL_HARMONICS_L0
    ProbeVolumeSphericalHarmonicsL0 coefficients;
    ProbeVolumeAccumulateSphericalHarmonicsL0(posInput, normalWS, viewDirectionWS, renderingLayers, coefficients, weightHierarchy);
    bakeDiffuseLighting += ProbeVolumeEvaluateSphericalHarmonicsL0(normalWS, coefficients);
    backBakeDiffuseLighting += ProbeVolumeEvaluateSphericalHarmonicsL0(backNormalWS, coefficients);
    reflectionProbeNormalizationLighting += ProbeVolumeEvaluateSphericalHarmonicsL0(reflectionDirectionWS, coefficients);

#elif PROBE_VOLUMES_SAMPLING_MODE == PROBEVOLUMESENCODINGMODES_SPHERICAL_HARMONICS_L1
    ProbeVolumeSphericalHarmonicsL1 coefficients;
    ProbeVolumeAccumulateSphericalHarmonicsL1(posInput, normalWS, viewDirectionWS, renderingLayers, coefficients, weightHierarchy);
    bakeDiffuseLighting += ProbeVolumeEvaluateSphericalHarmonicsL1(normalWS, coefficients);
    backBakeDiffuseLighting += ProbeVolumeEvaluateSphericalHarmonicsL1(backNormalWS, coefficients);
    reflectionProbeNormalizationLighting += ProbeVolumeGetReflectionProbeNormalizationDCOnly()
        ? ProbeVolumeEvaluateSphericalHarmonicsL0(reflectionDirectionWS, ProbeVolumeSphericalHarmonicsL0FromL1(coefficients))
        : ProbeVolumeEvaluateSphericalHarmonicsL1(reflectionDirectionWS, coefficients);

#elif PROBE_VOLUMES_SAMPLING_MODE == PROBEVOLUMESENCODINGMODES_SPHERICAL_HARMONICS_L2
    ProbeVolumeSphericalHarmonicsL2 coefficients;
    ProbeVolumeAccumulateSphericalHarmonicsL2(posInput, normalWS, viewDirectionWS, renderingLayers, coefficients, weightHierarchy);
    bakeDiffuseLighting += ProbeVolumeEvaluateSphericalHarmonicsL2(normalWS, coefficients);
    backBakeDiffuseLighting += ProbeVolumeEvaluateSphericalHarmonicsL2(backNormalWS, coefficients);
    reflectionProbeNormalizationLighting += ProbeVolumeGetReflectionProbeNormalizationDCOnly()
        ? ProbeVolumeEvaluateSphericalHarmonicsL0(reflectionDirectionWS, ProbeVolumeSphericalHarmonicsL0FromL2(coefficients))
        : ProbeVolumeEvaluateSphericalHarmonicsL2(reflectionDirectionWS, coefficients);

#endif

    bakeDiffuseLighting += ProbeVolumeEvaluateAmbientProbeFallback(normalWS, weightHierarchy);
    backBakeDiffuseLighting += ProbeVolumeEvaluateAmbientProbeFallback(backNormalWS, weightHierarchy);

    // The ambient probe fallback does not contribute to reflection probe normalization.
    // The idea here is that probe volume samples are higher frequency than reflection probes, so normalization is useful,
    // but the ambient probe fallback is lower frequency than reflection probes (there is only 1 ambient probe)
    // so normalizing by the ambient probe is not useful.
    // This also handles the common case where a scene may contain reflection probes but no probe volumes.
    reflectionProbeNormalizationWeight = weightHierarchy * ProbeVolumeGetReflectionProbeNormalizationWeight();
}

// Ref: "Efficient Evaluation of Irradiance Environment Maps" from ShaderX 2
real SHEvalLinearL0L1Luminance(real3 N, real4 shA)
{
    // Linear (L1) + constant (L0) polynomial terms
    return dot(shA.xyz, N) + shA.w;
}

real SHEvalLinearL2Luminance(real3 N, real4 shB, real shC)
{
    // 4 of the quadratic (L2) polynomials
    real4 vB = N.xyzz * N.yzzx;
    real x2 = dot(shB, vB);

    // Final (5th) quadratic (L2) polynomial
    real vC = N.x * N.x - N.y * N.y;
    real x3 = shC * vC;

    return x2 + x3;
}

half SampleSH9Luminance(half3 N, half4 shA, half4 shB, half shC)
{
    // Linear + constant polynomial terms
    half3 res = SHEvalLinearL0L1Luminance(N, shA);

    // Quadratic polynomials
    res += SHEvalLinearL2Luminance(N, shB, shC);

    return res;
}

// Same idea as in Rendering of COD:IW [Drobot 2017]
float GetReflectionProbeNormalizationFactor(float3 sampleDirectionWS, float4 reflProbeSHL0L1, float4 reflProbeSHL2_1, float reflProbeSHL2_2)
{
    float outFactor = 0;
    float L0 = reflProbeSHL0L1.x;

    if (ProbeVolumeGetReflectionProbeNormalizationDCOnly())
    {
        return L0;
    }

#if PROBE_VOLUMES_SAMPLING_MODE == PROBEVOLUMESENCODINGMODES_SPHERICAL_HARMONICS_L0
    outFactor = L0;

#elif PROBE_VOLUMES_SAMPLING_MODE == PROBEVOLUMESENCODINGMODES_SPHERICAL_HARMONICS_L1
    // SHEvalLinearL0L1() expects coefficients in real4 shAr, real4 shAg, real4 shAb vectors whos channels are laid out {x, y, z, DC}
    float4 shALuminance = float4(reflProbeSHL0L1.y, reflProbeSHL0L1.z, reflProbeSHL0L1.w, reflProbeSHL0L1.x);
    outFactor = SHEvalLinearL0L1Luminance(sampleDirectionWS, shALuminance);

#else PROBE_VOLUMES_SAMPLING_MODE == PROBEVOLUMESENCODINGMODES_SPHERICAL_HARMONICS_L2
    // SHEvalLinearL0L1() expects coefficients in real4 shAr, real4 shAg, real4 shAb vectors whos channels are laid out {x, y, z, DC}
    float4 shALuminance = float4(reflProbeSHL0L1.y, reflProbeSHL0L1.z, reflProbeSHL0L1.w, reflProbeSHL0L1.x);
    float4 shBLuminance = reflProbeSHL2_1;
    float shCLuminance = reflProbeSHL2_2;

    // Normalize DC term:
    shALuminance.w -= shBLuminance.z;

    // Normalize Quadratic term:
    shBLuminance.z *= 3.0f;

    outFactor = SampleSH9Luminance(sampleDirectionWS, shALuminance, shBLuminance, shCLuminance);
#endif

    // Avoid negative values which can happen due to SH ringing.
    // Avoid divide by zero in caller.
    return max(1e-5f, outFactor);
}

float GetReflectionProbeNormalizationFactor(float3 reflectionProbeNormalizationLighting, float reflectionProbeNormalizationWeight, float3 sampleDirectionWS, float4 reflProbeSHL0L1, float4 reflProbeSHL2_1, float reflProbeSHL2_2)
{
    float refProbeNormalization = GetReflectionProbeNormalizationFactor(sampleDirectionWS, reflProbeSHL0L1, reflProbeSHL2_1, reflProbeSHL2_2);
    float localNormalization = max(0.0f, Luminance(reflectionProbeNormalizationLighting));

    float normalization = localNormalization / refProbeNormalization;
    normalization = clamp(normalization, ProbeVolumeGetReflectionProbeNormalizationMin(), ProbeVolumeGetReflectionProbeNormalizationMax());

    return lerp(1.0f, normalization, reflectionProbeNormalizationWeight);
}

#endif // __PROBEVOLUME_HLSL__

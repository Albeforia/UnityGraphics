
#ifndef UNIVERSAL_DEBUGGING_INCLUDED
#define UNIVERSAL_DEBUGGING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DebugViewEnums.cs.hlsl"

// Set of colors that should still provide contrast for the Color-blind
#define kPurpleColor float4(156.0 / 255.0, 79.0 / 255.0, 255.0 / 255.0, 1.0) // #9C4FFF
#define kRedColor float4(203.0 / 255.0, 48.0 / 255.0, 34.0 / 255.0, 1.0) // #CB3022
#define kGreenColor float4(8.0 / 255.0, 215.0 / 255.0, 139.0 / 255.0, 1.0) // #08D78B
#define kYellowGreenColor float4(151.0 / 255.0, 209.0 / 255.0, 61.0 / 255.0, 1.0) // #97D13D
#define kBlueColor float4(75.0 / 255.0, 146.0 / 255.0, 243.0 / 255.0, 1.0) // #4B92F3
#define kOrangeBrownColor float4(219.0 / 255.0, 119.0 / 255.0, 59.0 / 255.0, 1.0) // #4B92F3
#define kGrayColor float4(174.0 / 255.0, 174.0 / 255.0, 174.0 / 255.0, 1.0) // #AEAEAE

#if defined(_DEBUG_SHADER)

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Debug.hlsl"

int _DebugMaterialIndex;
int _DebugLightingIndex;
int _DebugAttributesIndex;
int _DebugLightingFeatureMask;
int _DebugMipIndex;
int _DebugValidationIndex;

half _AlbedoMinLuminance = 0.01;
half _AlbedoMaxLuminance = 0.90;
half _AlbedoSaturationTolerance = 0.214;
half _AlbedoHueTolerance = 0.104;
half3 _AlbedoCompareColor = half3(0.5, 0.5, 0.5);

sampler2D _DebugNumberTexture;

struct DebugData
{
    half3 brdfDiffuse;
    half3 brdfSpecular;
    float2 uv;

    float4 texelSize;   // 1 / width, 1 / height, width, height
    uint mipCount;
};

// TODO: Set of colors that should still provide contrast for the Color-blind
//#define kPurpleColor half4(156.0 / 255.0, 79.0 / 255.0, 255.0 / 255.0, 1.0) // #9C4FFF
//#define kRedColor half4(203.0 / 255.0, 48.0 / 255.0, 34.0 / 255.0, 1.0) // #CB3022
//#define kGreenColor = half4(8.0 / 255.0, 215.0 / 255.0, 139.0 / 255.0, 1.0) // #08D78B
//#define kYellowGreenColor = half4(151.0 / 255.0, 209.0 / 255.0, 61.0 / 255.0, 1.0) // #97D13D
//#define kBlueColor = half4(75.0 / 255.0, 146.0 / 255.0, 243.0 / 255.0, 1.0) // #4B92F3
//#define kOrangeBrownColor = half4(219.0 / 255.0, 119.0 / 255.0, 59.0 / 255.0, 1.0) // #4B92F3
//#define kGrayColor = half4(174.0 / 255.0, 174.0 / 255.0, 174.0 / 255.0, 1.0) // #AEAEAE

half4 GetShadowCascadeColor(float4 shadowCoord, float3 positionWS);

DebugData CreateDebugData(half3 brdfDiffuse, half3 brdfSpecular, float2 uv)
{
    DebugData debugData;

    debugData.brdfDiffuse = brdfDiffuse;
    debugData.brdfSpecular = brdfSpecular;
    debugData.uv = uv;

    // TODO: Pass the actual mipmap and texel data in here somehow, but we don't have access to textures here...
    const int textureWdith = 1024;
    const int textureHeight = 1024;
    debugData.texelSize = half4(1.0h / textureWdith, 1.0h / textureHeight, textureWdith, textureHeight);
    debugData.mipCount = 9;

    return debugData;
}

half4 GetLODDebugColor()
{
    if (IsBitSet(unity_LODFade.z, 0))
        return half4(0.4831376f, 0.6211768f, 0.0219608f, 1.0f);
    if (IsBitSet(unity_LODFade.z, 1))
        return half4(0.2792160f, 0.4078432f, 0.5835296f, 1.0f);
    if (IsBitSet(unity_LODFade.z, 2))
        return half4(0.2070592f, 0.5333336f, 0.6556864f, 1.0f);
    if (IsBitSet(unity_LODFade.z, 3))
        return half4(0.5333336f, 0.1600000f, 0.0282352f, 1.0f);
    if (IsBitSet(unity_LODFade.z, 4))
        return half4(0.3827448f, 0.2886272f, 0.5239216f, 1.0f);
    if (IsBitSet(unity_LODFade.z, 5))
        return half4(0.8000000f, 0.4423528f, 0.0000000f, 1.0f);
    if (IsBitSet(unity_LODFade.z, 6))
        return half4(0.4486272f, 0.4078432f, 0.0501960f, 1.0f);
    if (IsBitSet(unity_LODFade.z, 7))
        return half4(0.7749016f, 0.6368624f, 0.0250984f, 1.0f);
    return half4(0.2,0.2,0.2,1);
}

// Convert rgb to luminance
// with rgb in linear space with sRGB primaries and D65 white point
half LinearRgbToLuminance(half3 linearRgb)
{
    return dot(linearRgb, half3(0.2126729f, 0.7151522f, 0.0721750f));
}

half3 UnityMeta_RGBToHSVHelper(float offset, half dominantColor, half colorone, half colortwo)
{
    half H, S, V;
    V = dominantColor;

    if (V != 0.0)
    {
        half small = 0.0;
        if (colorone > colortwo)
            small = colortwo;
        else
            small = colorone;

        half diff = V - small;

        if (diff != 0)
        {
            S = diff / V;
            H = offset + ((colorone - colortwo) / diff);
        }
        else
        {
            S = 0;
            H = offset + (colorone - colortwo);
        }

        H /= 6.0;

        if (H < 6.0)
        {
            H += 1.0;
        }
    }
    else
    {
        S = 0;
        H = 0;
    }
    return half3(H, S, V);
}

half3 UnityMeta_RGBToHSV(half3 rgbColor)
{
    // when blue is highest valued
    if ((rgbColor.b > rgbColor.g) && (rgbColor.b > rgbColor.r))
        return UnityMeta_RGBToHSVHelper(4.0, rgbColor.b, rgbColor.r, rgbColor.g);
    //when green is highest valued
    else if (rgbColor.g > rgbColor.r)
        return UnityMeta_RGBToHSVHelper(2.0, rgbColor.g, rgbColor.b, rgbColor.r);
    //when red is highest valued
    else
        return UnityMeta_RGBToHSVHelper(0.0, rgbColor.r, rgbColor.g, rgbColor.b);
}

bool UpdateSurfaceAndInputDataForDebug(inout SurfaceData surfaceData, inout InputData inputData)
{
    bool changed = false;

    if (_DebugLightingIndex == LIGHTINGDEBUGMODE_LIGHT_ONLY || _DebugLightingIndex == LIGHTINGDEBUGMODE_LIGHT_DETAIL)
    {
        surfaceData.albedo = half3(1, 1, 1);
        surfaceData.emission = half3(0, 0, 0);
        surfaceData.specular = half3(0, 0, 0);
        surfaceData.occlusion = 1;
        surfaceData.clearCoatMask = 0;
        surfaceData.clearCoatSmoothness = 1;
        surfaceData.metallic = 0;
        surfaceData.smoothness = 0;
        changed = true;
    }
    else if (_DebugLightingIndex == LIGHTINGDEBUGMODE_REFLECTIONS || _DebugLightingIndex == LIGHTINGDEBUGMODE_REFLECTIONS_WITH_SMOOTHNESS)
    {
        surfaceData.albedo = half3(0, 0, 0);
        surfaceData.emission = half3(0, 0, 0);
        surfaceData.occlusion = 1;
        surfaceData.clearCoatMask = 0;
        surfaceData.clearCoatSmoothness = 1;
        if (_DebugLightingIndex == LIGHTINGDEBUGMODE_REFLECTIONS)
        {
            surfaceData.specular = half3(1, 1, 1);
            surfaceData.metallic = 0;
            surfaceData.smoothness = 1;
        }
        else if (_DebugLightingIndex == LIGHTINGDEBUGMODE_REFLECTIONS_WITH_SMOOTHNESS)
        {
            surfaceData.specular = half3(0, 0, 0);
            surfaceData.metallic = 1;
            surfaceData.smoothness = 0;
        }
        changed = true;
    }

    if (_DebugLightingIndex == LIGHTINGDEBUGMODE_LIGHT_ONLY || _DebugLightingIndex == LIGHTINGDEBUGMODE_REFLECTIONS)
    {
        half3 normalTS = half3(0, 0, 1);

        #if defined(_NORMALMAP)
        inputData.normalWS = TransformTangentToWorld(normalTS, inputData.tangentMatrixWS);
        #else
        inputData.normalWS = inputData.normalWS;
        #endif
        inputData.normalTS = normalTS;
        surfaceData.normalTS = normalTS;
        changed = true;
    }

    return changed;
}

half3 GetDebugColor(uint index)
{
    // TODO: Make these colors colorblind friendly...
    const uint maxColors = 10;
    float4 lut[maxColors] = {
        kPurpleColor,
        kRedColor,
        kGreenColor,
        kYellowGreenColor,
        kBlueColor,
        kOrangeBrownColor,
        kGrayColor,
        float4(1, 1, 1, 0),
        float4(0.8, 0.3, 0.7, 0),
        float4(0.8, 0.7, 0.3, 0),
    };
    uint clammpedIndex = clamp(index, 0, maxColors - 1);

    return lut[clammpedIndex].rgb;
}

half4 GetTextNumber(uint numberValue, float3 positionWS)
{
    float4 clipPos = TransformWorldToHClip(positionWS);
    float2 ndc = saturate((clipPos.xy / clipPos.w) * 0.5 + 0.5);

#if UNITY_UV_STARTS_AT_TOP
    if (_ProjectionParams.x < 0)
        ndc.y = 1.0 - ndc.y;
#endif

    // There are currently 10 characters in the font texture, 0-9.
    const float invNumChar = 1.0 / 10.0f;
    // The following are hardcoded scales that make the font size readable.
    ndc.x *= 5.0;
    ndc.y *= 15.0;
    ndc.x = fmod(ndc.x, invNumChar) + (numberValue * invNumChar);

    return tex2D(_DebugNumberTexture, ndc.xy);
}

half4 CalculateDebugColorWithNumber(in InputData inputData, in SurfaceData surfaceData, uint index)
{
    // TODO: Opacity could be user-defined...
    const float opacity = 0.8f;
    half3 debugColor = GetDebugColor(index);
    half3 fc = lerp(surfaceData.albedo, debugColor, opacity);
    half4 textColor = GetTextNumber(index, inputData.positionWS);

    return textColor * half4(fc, 1);
}

float GetMipMapLevel(float2 nonNormalizedUVCoordinate)
{
    // The OpenGL Graphics System: A Specification 4.2
    //  - chapter 3.9.11, equation 3.21

    float2  dx_vtc = ddx(nonNormalizedUVCoordinate);
    float2  dy_vtc = ddy(nonNormalizedUVCoordinate);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));

    return 0.5 * log2(delta_max_sqr);
}

half4 GetMipLevelDebugColor(in InputData inputData, in SurfaceData surfaceData, in DebugData debugData)
{
    float mipLevel = GetMipMapLevel(debugData.uv * debugData.texelSize.zw);

    return CalculateDebugColorWithNumber(inputData, surfaceData, (int)mipLevel);
}

half4 GetMipCountDebugColor(in InputData inputData, in SurfaceData surfaceData, in DebugData debugData)
{
    uint mipCount = debugData.mipCount;

    return CalculateDebugColorWithNumber(inputData, surfaceData, mipCount);
}

bool CalculateValidationColorForDebug(InputData inputData, SurfaceData surfaceData, DebugData debugData, out half4 color)
{
    if (_DebugValidationIndex == DEBUGVALIDATIONMODE_VALIDATE_ALBEDO)
    {
        half value = LinearRgbToLuminance(surfaceData.albedo);
        if (_AlbedoMinLuminance > value)
        {
             color = half4(1.0f, 0.0f, 0.0f, 1.0f);
        }
        else if (_AlbedoMaxLuminance < value)
        {
             color = half4(0.0f, 1.0f, 0.0f, 1.0f);
        }
        else
        {
            half3 hsv = UnityMeta_RGBToHSV(surfaceData.albedo);
            half hue = hsv.r;
            half sat = hsv.g;

            half3 compHSV = UnityMeta_RGBToHSV(_AlbedoCompareColor.rgb);
            half compHue = compHSV.r;
            half compSat = compHSV.g;

            if ((compSat - _AlbedoSaturationTolerance > sat) || ((compHue - _AlbedoHueTolerance > hue) && (compHue - _AlbedoHueTolerance + 1.0 > hue)))
            {
                color = half4(1.0f, 0.0f, 0.0f, 1.0f);
            }
            else if ((sat > compSat + _AlbedoSaturationTolerance) || ((hue > compHue + _AlbedoHueTolerance) && (hue > compHue + _AlbedoHueTolerance - 1.0)))
            {
                color = half4(0.0f, 1.0f, 0.0f, 1.0f);
            }
            else
            {
                color = half4(value, value, value, 1.0);
            }
        }
        return true;
    }
    else
    {
        color = half4(0, 0, 0, 1);
        return false;
    }
}

bool CalculateValidationColorForMipMaps(InputData inputData, SurfaceData surfaceData, DebugData debugData, out half4 color)
{
    color = half4(surfaceData.albedo, 1);

    switch (_DebugMipIndex)
    {
        case DEBUGMIPINFO_LEVEL:
            color = GetMipLevelDebugColor(inputData, surfaceData, debugData);
            return true;
        case DEBUGMIPINFO_COUNT:
            color = GetMipCountDebugColor(inputData, surfaceData, debugData);
            return true;
        default:
            return false;
    }
}

bool CalculateColorForDebugMaterial(InputData inputData, SurfaceData surfaceData, DebugData debugData, out half4 color)
{
    color = half4(0, 0, 0, 1);

    // Debug materials...
    switch(_DebugMaterialIndex)
    {
        case DEBUGMATERIALINDEX_UNLIT:
            color.rgb = surfaceData.albedo;
            return true;

        case DEBUGMATERIALINDEX_DIFFUSE:
            color.rgb = debugData.brdfDiffuse;
            return true;

        case DEBUGMATERIALINDEX_SPECULAR:
            color.rgb = debugData.brdfSpecular;
            return true;

        case DEBUGMATERIALINDEX_ALPHA:
            color.rgb = surfaceData.alpha.rrr;
            return true;

        case DEBUGMATERIALINDEX_SMOOTHNESS:
            color.rgb = surfaceData.smoothness.rrr;
            return true;

        case DEBUGMATERIALINDEX_AMBIENT_OCCLUSION:
            color.rgb = surfaceData.occlusion.rrr;
            return true;

        case DEBUGMATERIALINDEX_EMISSION:
            color.rgb = surfaceData.emission;
            return true;

        case DEBUGMATERIALINDEX_NORMAL_WORLD_SPACE:
            color.rgb = inputData.normalWS.xyz * 0.5 + 0.5;
            return true;

        case DEBUGMATERIALINDEX_NORMAL_TANGENT_SPACE:
            color.rgb = surfaceData.normalTS.xyz * 0.5 + 0.5;
            return true;
        case DEBUGMATERIALINDEX_LOD:
            color.rgb = GetLODDebugColor().rgb;
            return true;
        case DEBUGMATERIALINDEX_METALLIC:
            color.rgb = surfaceData.metallic.rrr;
            return true;

        default:
            return false;
    }
}

bool CalculateColorForDebug(InputData inputData, SurfaceData surfaceData, DebugData debugData, out half4 color)
{
    if(CalculateColorForDebugMaterial(inputData, surfaceData, debugData, color))
    {
        return true;
    }
    else if(CalculateValidationColorForDebug(inputData, surfaceData, debugData, color))
    {
        return true;
    }
    else if(CalculateValidationColorForMipMaps(inputData, surfaceData, debugData, color))
    {
        return true;
    }
    else
    {
        color = half4(0, 0, 0, 1);
        return false;
    }
}

#endif

bool IsLightingFeatureEnabled(uint bitMask)
{
    #if defined(_DEBUG_SHADER)
    return (_DebugLightingFeatureMask == 0) || ((_DebugLightingFeatureMask & bitMask) != 0);
    #else
    return true;
    #endif
}

#endif

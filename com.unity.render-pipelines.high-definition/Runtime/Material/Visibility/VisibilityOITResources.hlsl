#ifndef VISIBILITY_OIT_RESOURCES
#define VISIBILITY_OIT_RESOURCES

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Visibility/VisibilityCommon.hlsl"

#define SAMPLES_DISPATCH_THREAD_COUNT 64
#define DITHER_TILE_SIZE 128
#define DITHER_TILE_TOTAL_PIXELS (DITHER_TILE_SIZE * DITHER_TILE_SIZE)

TEXTURE2D_X_UINT2(_VisOITCount);
ByteAddressBuffer _VisOITBuffer;
ByteAddressBuffer _VisOITHistogramBuffer;
ByteAddressBuffer _VisOITPrefixedHistogramBuffer;
ByteAddressBuffer _VisOITListsCounts;
ByteAddressBuffer _VisOITListsOffsets;
ByteAddressBuffer _VisOITSubListsCounts;
ByteAddressBuffer _VisOITPixelHash;

#ifdef DEBUG_DISPLAY
float _VisOITGBufferLayerIdx;
float _VisOITGBufferLayer;
float _VBufferOITLightingOffscreenWidth;
#endif

#if defined(USE_TEXTURE2D_X_AS_ARRAY)
Texture2DArray<float2> _OITTileHiZ;
#else
Texture2D<float2> _OITTileHiZ;
#endif

TEXTURE2D_X_UINT4(_VisOITOffscreenGBuffer0);
TEXTURE2D_X_UINT2(_VisOITOffscreenGBuffer1);
TEXTURE2D_X(_VisOITOffscreenDirectReflectionLighting);
TEXTURE2D_X(_VisOITOffscreenPhotonRadianceLighting);
TEXTURE2D_X(_VisOITOffscreenLighting);

float4 DebugDrawOITHistogram(float2 sampleUV, float2 screenSize)
{
    float2 workOffset = float2(0.02,0.02);
    float2 workSize = float2(0.3, 0.25);

    sampleUV = (sampleUV - workOffset) / workSize;
    if (any(sampleUV.xy < float2(0.0, 0.0)) || any(sampleUV.xy > float2(1.0, 1.0)))
        return float4(0,0,0,0);

    float samplePixelSize = rcp(screenSize.x * workSize.x);
    float barsPerPixel = (float)DITHER_TILE_TOTAL_PIXELS * samplePixelSize;
    uint offset = floor(sampleUV.x * DITHER_TILE_TOTAL_PIXELS);

    uint accumulation = 0;

    [loop]
    for (int i = -0.5*round(barsPerPixel); i < 0.5*round(barsPerPixel); ++i)
    {
        accumulation += _VisOITHistogramBuffer.Load(clamp((offset + i), 0, DITHER_TILE_TOTAL_PIXELS - 1) << 2);
    }

    float perc = float(accumulation);
    float maxVal = (float)_VisOITPrefixedHistogramBuffer.Load((DITHER_TILE_TOTAL_PIXELS - 1) << 2);
    return float4(sqrt(perc/maxVal).xxx, 1.0);
}

namespace VisibilityOIT
{

void PackVisibilityData(Visibility::VisibilityData data, uint2 texelCoordinate, float depth, out uint3 packedData)
{
    uint2 packedDataHalfs;
    Visibility::PackVisibilityData(data, packedData.x, packedDataHalfs);
    packedData.y = (packedDataHalfs.x & 0xFF) | (packedDataHalfs.y << 8) | PackFloatToUInt(depth, 16, 16);
    packedData.z = (texelCoordinate.x & 0xFFFF) | ((texelCoordinate.y & 0xFFFF) << 16);
}

void UnpackVisibilityData(uint3 packedData, out Visibility::VisibilityData data, out uint2 texelCoordinate, out float depth)
{
    uint2 packedDataHalfs = uint2(packedData.y & 0xFF, (packedData.y >> 8) & 0xFF);
    Visibility::UnpackVisibilityData(packedData.x, packedDataHalfs, data);
    texelCoordinate = uint2(packedData.z & 0xFFFF, (packedData.z >> 16) & 0xFFFF);
    depth = UnpackUIntToFloat(packedData.y, 16, 16);
}

void GetPixelList(uint pixelOffset, out uint listCount, out uint listOffset)
{
    listCount = _VisOITSubListsCounts.Load(pixelOffset << 2);
    listOffset = _VisOITListsOffsets.Load(pixelOffset << 2);
}

void GetVisibilitySample(uint i, uint listOffset, out Visibility::VisibilityData data, out uint2 texelCoordinate, out float depthValue)
{
    uint3 packedData = _VisOITBuffer.Load3(((listOffset + i) * 3) << 2);
    VisibilityOIT::UnpackVisibilityData(packedData, data, texelCoordinate, depthValue);
}

void GetVisibilitySampleWithLinearDepth(uint i, uint listOffset, out Visibility::VisibilityData data, out uint2 texelCoordinate, out float linearDepthValue)
{
    float deviceDepth;
    GetVisibilitySample(i, listOffset, data, texelCoordinate, deviceDepth);

    PositionInputs posInput = GetPositionInput(texelCoordinate, _ScreenSize.zw);
    posInput.positionWS = ComputeWorldSpacePosition(posInput.positionNDC, deviceDepth, UNITY_MATRIX_I_VP);
    linearDepthValue = LinearEyeDepth(posInput.positionWS, GetWorldToViewMatrix());
    linearDepthValue = (linearDepthValue - _ProjectionParams.y) / (_ProjectionParams.z - _ProjectionParams.y);
}

void PackOITGBufferData(float3 normal, float roughness, float3 baseColor, float metalness, float3 absorptionCoefficient, float ior, out uint4 packedData0, out uint2 packedData1)
{
    float2 oct = saturate(PackNormalOctQuadEncode(normal) * 0.5f + 0.5f);

    packedData0.r = PackFloatToUInt(oct.x, 0, 16);// | PackFloatToUInt(oct.y, 16, 16);
    //
    packedData0.g = PackFloatToUInt(absorptionCoefficient.r,  0, 8)
                  | PackFloatToUInt(absorptionCoefficient.g,  8, 8)
                  | PackFloatToUInt(absorptionCoefficient.b, 16, 8)
                  | PackFloatToUInt(metalness, 24, 7);
    packedData0.b = PackFloatToUInt(baseColor.r, 0, 8)
                  | PackFloatToUInt(baseColor.g, 8, 8)
                  | PackFloatToUInt(baseColor.b, 16, 8)
                  | PackFloatToUInt(roughness, 24, 7);
    packedData0.a = PackFloatToUInt(oct.y, 0, 16);
    packedData1.r = 255.0 * clamp(ior, 0.0f, 4.0f) / 4.0f;
        //PackFloatToUInt(clamp(ior, 0.0f, 4.0f)/4.0f, 0, 8);
    packedData1.g = 0;
}


void UnpackOITNormalFromGBufferData0(uint4 packedData0, out float3 normal)
{
    float2 oct;
    oct.x = UnpackUIntToFloat(packedData0.r, 0, 16);
    oct.y = UnpackUIntToFloat(packedData0.a, 0, 16);

    normal = normalize(UnpackNormalOctQuadEncode(oct * 2.0f - 1.0f));
}

void UnpackOITGBufferData(uint4 packedData0, uint2 packedData1, out float3 normal, out float roughness, out float3 baseColor, out float metalness, out float3 absorptionCoefficient, out float ior)
{
    UnpackOITNormalFromGBufferData0(packedData0, normal);

    absorptionCoefficient.r = UnpackUIntToFloat(packedData0.g, 0, 8);
    absorptionCoefficient.g = UnpackUIntToFloat(packedData0.g, 8, 8);
    absorptionCoefficient.b = UnpackUIntToFloat(packedData0.g, 16, 8);
    metalness = UnpackUIntToFloat(packedData0.g, 24, 7);

    baseColor.r = UnpackUIntToFloat(packedData0.b, 0, 8);
    baseColor.g = UnpackUIntToFloat(packedData0.b, 8, 8);
    baseColor.b = UnpackUIntToFloat(packedData0.b, 16, 8);
    roughness = UnpackUIntToFloat(packedData0.b, 24, 7);

    ior = 4.0f * packedData1.r / 255.0f;
        //UnpackUIntToFloat(packedData1.r, 0, 8) * 4.0f;
}

}

#endif

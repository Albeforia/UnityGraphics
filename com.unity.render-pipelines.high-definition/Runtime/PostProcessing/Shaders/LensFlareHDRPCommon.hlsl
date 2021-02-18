//C:\Code\pkgs\GraphicsHTTPS\com.unity.render-pipelines.core\ShaderLibrary\Random.hlsl
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Sampling/Sampling.hlsl"

struct Attributes
{
    uint vertexID : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 texcoord : TEXCOORD0;
    float4 occlusion : TEXCOORD1;
    UNITY_VERTEX_OUTPUT_STEREO
};

sampler2D _FlareTex;
float4 _FlareColor;
float4 _FlareData0; // x: RayPos, y: AngleRotation (< 0 == Auto), zw: Size (Width, Height) in Screen Height Ratio
float4 _FlareData1; // xy: ScreenPos, z: Depth, w: Occlusion radius
float4 _FlareData2; // x: Sample Count, y: Speed, z: _FlareOffscreen

#define _RayPos _FlareData0.x
#define _Angle _FlareData0.y
#define _Size _FlareData0.zw

#define _FlareScreenPos _FlareData1.xy
#define _FlareDepth _FlareData1.z
#define _FlareOcclusionRadius _FlareData1.w

#define _FlareOcclusionSamplesCount _FlareData2.x
#define _FlareSpeed _FlareData2.y
#define _FlareOffscreen _FlareData2.z

float GetOcclusion(float2 screenPos, float flareDepth, float ratio)
{
    float contrib = 0.0f;
    float sample_Contrib = 1.0f / _FlareOcclusionSamplesCount;
    float2 ratioScale = float2(1.0f / ratio, 1.0);
    if (_FlareOcclusionSamplesCount == 0.0f)
        return 1.0f;

    for (uint i = 0; i < (uint)_FlareOcclusionSamplesCount; i++)
    {
        float2 dir = _FlareOcclusionRadius * SampleDiskUniform(Hash(2 * i + 0 + 1), Hash(2 * i + 1 + 1));
        float2 pos = screenPos + dir;
        pos.xy = pos * 0.5f + 0.5f;
        pos.y = 1.0f - pos.y;
        if (all(pos >= 0) && all(pos <= 1))
        {
            float depth0 = LinearEyeDepth(SampleCameraDepth(pos), _ZBufferParams);
            if (flareDepth < depth0)
                contrib += sample_Contrib;
        }
        else if (_FlareOffscreen > 0.0f)
        {
            contrib += sample_Contrib;
        }
    }

    return contrib;
}

Varyings vert(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float screenRatio = _ScreenSize.x / _ScreenSize.y;
    float2 flareSize = _Size;

    float4 posPreScale = float4(2.0f, 2.0f, 1.0f, 1.0f) * GetQuadVertexPosition(input.vertexID) - float4(1.0f, 1.0f, 0.0f, 0.0);
    output.texcoord = GetQuadTexCoord(input.vertexID);
    float2 screenPos = _FlareScreenPos;

    // position and rotate
    float angle = _Angle;
    // negative stands for: also rotate to face the light
    if (angle >= 0)
    {
        angle = -angle;
        float2 dir = normalize(screenPos * float2(screenRatio, 1.0f));
        angle += atan2(dir.y, dir.x) + 1.57079632675; // arbitrary, we need V to face the source, not U;
    }

    float cos0 = cos(angle);
    float sin0 = sin(angle);

    posPreScale.xy *= flareSize;
    float2 local = float2((posPreScale.x * cos0 - posPreScale.y * sin0),
                          (posPreScale.x * sin0 + posPreScale.y * cos0));

    local.x *= 1.0f / screenRatio;

    float4 centerPos = float4(local.x,
                              local.y,
                              posPreScale.z,
                              posPreScale.w);
    float2 rayOffset = -screenPos * (_RayPos - 1.0f) * _FlareSpeed;

    output.positionCS = centerPos;
    output.positionCS.xy += rayOffset;
    float occlusion = GetOcclusion(_FlareScreenPos.xy, _FlareDepth, screenRatio);

    if (_FlareOffscreen < 0.0f && // No lens flare off screen
        (any(_FlareScreenPos.xy < -1) || any(_FlareScreenPos.xy >= 1)))
        occlusion *= 0.0f;

    output.occlusion = occlusion;

    return output;
}

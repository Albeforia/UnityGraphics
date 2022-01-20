using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using Unity.Collections;

namespace UnityEngine.Rendering.HighDefinition
{
    public partial class HDRenderPipeline
    {
        struct VBufferOITOutput
        {
            public bool valid;
            public TextureHandle stencilBuffer;
            public ComputeBufferHandle histogramBuffer;
            public ComputeBufferHandle prefixedHistogramBuffer;
            public ComputeBufferHandle sampleListCountBuffer;
            public ComputeBufferHandle sampleListOffsetBuffer;
            public ComputeBufferHandle sublistCounterBuffer;
            public ComputeBufferHandle pixelHashBuffer;
            public ComputeBufferHandle samplesDispatchArgsBuffer;
            public ComputeBufferHandle samplesGpuCountBuffer;
            public ComputeBufferHandle oitVisibilityBuffer;
            public RenderBRGBindingData BRGBindingData;
            public ComputeBuffer depthPyramidMipLevelOffsetsBuffer;

            public static VBufferOITOutput NewDefault()
            {
                return new VBufferOITOutput()
                {
                    valid = false,
                    stencilBuffer = TextureHandle.nullHandle,
                    BRGBindingData = RenderBRGBindingData.NewDefault(),
                    depthPyramidMipLevelOffsetsBuffer = new ComputeBuffer(15, sizeof(int) * 2)
                };
            }

            public VBufferOITOutput Read(RenderGraphBuilder builder)
            {
                VBufferOITOutput readVBuffer = VBufferOITOutput.NewDefault();
                readVBuffer.valid = valid;
                readVBuffer.stencilBuffer = builder.ReadTexture(stencilBuffer);
                if (!valid)
                    return readVBuffer;

                readVBuffer.histogramBuffer = builder.ReadComputeBuffer(histogramBuffer);
                readVBuffer.prefixedHistogramBuffer = builder.ReadComputeBuffer(prefixedHistogramBuffer);
                readVBuffer.sampleListCountBuffer = builder.ReadComputeBuffer(sampleListCountBuffer);
                readVBuffer.sampleListOffsetBuffer = builder.ReadComputeBuffer(sampleListOffsetBuffer);
                readVBuffer.sublistCounterBuffer = builder.ReadComputeBuffer(sublistCounterBuffer);
                readVBuffer.pixelHashBuffer = builder.ReadComputeBuffer(pixelHashBuffer);
                readVBuffer.samplesDispatchArgsBuffer = builder.ReadComputeBuffer(samplesDispatchArgsBuffer);
                readVBuffer.samplesGpuCountBuffer = builder.ReadComputeBuffer(samplesGpuCountBuffer);
                readVBuffer.oitVisibilityBuffer = builder.ReadComputeBuffer(oitVisibilityBuffer);

                readVBuffer.BRGBindingData = BRGBindingData;
                return readVBuffer;
            }
        }

        GraphicsFormat GetForwardFastFormat()
        {
            //return GetColorBufferFormat(); For now just use 16 bit, so we can pack the alpha tightly.
            return GraphicsFormat.R16G16B16A16_SFloat;
        }

        GraphicsFormat GetDeferredSSTracingGBuffer0Format()
        {
            return GraphicsFormat.R32G32B32A32_UInt;
        }

        GraphicsFormat GetDeferredSSTracingGBuffer1Format()
        {
            return GraphicsFormat.R8_UInt;
        }

        GraphicsFormat GetDeferredSSTracingHiZFormat()
        {
            return GraphicsFormat.R16G16_SFloat;
        }

        int GetOITVisibilityBufferSize()
        {
            return sizeof(uint) * 3; //12 bytes
        }

        int GetMaxMaterialOITSampleCount(HDCamera hdCamera)
        {
            float budget = currentAsset.currentPlatformRenderPipelineSettings.orderIndependentTransparentSettings.memoryBudget;
            float availableBytes = budget * 1024.0f * 1024.0f;

            int pixelCount = hdCamera.actualWidth * hdCamera.actualHeight;

            int bufferSize;
            if (currentAsset.currentPlatformRenderPipelineSettings.orderIndependentTransparentSettings.oitLightingMode == OITLightingMode.ForwardFast)
            {
                bufferSize = pixelCount * HDUtils.GetFormatSizeInBytes(GetForwardFastFormat());
            }
            else
            {
                bufferSize = pixelCount * (
                                            HDUtils.GetFormatSizeInBytes(GetForwardFastFormat()) + // Offscreen Direct Reflection Lighting
                                            HDUtils.GetFormatSizeInBytes(GetDeferredSSTracingGBuffer0Format()) + // GBuffer0 == {Normal, BaseColor, Roughness}
                                            HDUtils.GetFormatSizeInBytes(GetDeferredSSTracingGBuffer1Format()) // GBuffer1 == {Metalness}
                                            )
                            + hdCamera.depthBufferMipChainInfo.textureSize.x * hdCamera.depthBufferMipChainInfo.textureSize.x * GetOITVisibilityBufferSize(); // HiZ for tracing
            }
            availableBytes -= bufferSize;

            //for now store visibility
            float visibilityCost = GetOITVisibilityBufferSize();
            return (int)Math.Max(1, Math.Ceiling(availableBytes / visibilityCost));
        }

        internal bool IsVisibilityOITPassEnabled()
        {
            return currentAsset != null && currentAsset.VisibilityOITMaterial != null && currentAsset.currentPlatformRenderPipelineSettings.orderIndependentTransparentSettings.enabled && RenderBRG.GetRenderBRGMaterialBindingData().valid; ;
        }

        void RenderVBufferOIT(RenderGraph renderGraph, TextureHandle colorBuffer, HDCamera hdCamera, CullingResults cullResults, ref PrepassOutput output)
        {
            output.vbufferOIT = VBufferOITOutput.NewDefault();

            var BRGBindingData = RenderBRG.GetRenderBRGMaterialBindingData();
            if (!IsVisibilityOITPassEnabled())
            {
                output.vbufferOIT.stencilBuffer = renderGraph.defaultResources.blackTextureXR;
                return;
            }

            output.vbufferOIT.valid = true;

            output.vbufferOIT.stencilBuffer = RenderVBufferOITCountPass(renderGraph, colorBuffer, hdCamera, cullResults, BRGBindingData);

            var screenSize = new Vector2Int((int)hdCamera.screenSize.x, (int)hdCamera.screenSize.y);
            int histogramSize, tileSize;

            var histogramBuffer = ComputeOITTiledHistogram(renderGraph, screenSize, hdCamera.viewCount, output.vbufferOIT.stencilBuffer, out histogramSize, out tileSize);
            var prefixedHistogramBuffer = ComputeOITTiledPrefixSumHistogramBuffer(renderGraph, histogramBuffer, histogramSize);

            int maxMaterialSampleCount = GetMaxMaterialOITSampleCount(hdCamera);
            ComputeOITAllocateSampleLists(
                renderGraph, maxMaterialSampleCount, screenSize, output.vbufferOIT.stencilBuffer, prefixedHistogramBuffer,
                out ComputeBufferHandle sampleListCountBuffer, out ComputeBufferHandle sampleListOffsetBuffer, out ComputeBufferHandle sublistCounterBuffer, out ComputeBufferHandle pixelHashBuffer);

            ComputeOITSampleDispatchArgs(renderGraph, sampleListCountBuffer, screenSize, out ComputeBufferHandle samplesDispatchArgsBuffer, out ComputeBufferHandle samplesGpuCountBuffer);

            ComputeBufferHandle oitVisibilityBuffer = RenderVBufferOITStoragePass(
                renderGraph, maxMaterialSampleCount, hdCamera, cullResults, BRGBindingData,
                sampleListCountBuffer, sampleListOffsetBuffer, ref sublistCounterBuffer, pixelHashBuffer);

            output.vbufferOIT.histogramBuffer = histogramBuffer;
            output.vbufferOIT.prefixedHistogramBuffer = prefixedHistogramBuffer;
            output.vbufferOIT.sampleListCountBuffer = sampleListCountBuffer;
            output.vbufferOIT.sampleListOffsetBuffer = sampleListOffsetBuffer;
            output.vbufferOIT.sublistCounterBuffer = sublistCounterBuffer;
            output.vbufferOIT.pixelHashBuffer = pixelHashBuffer;
            output.vbufferOIT.samplesDispatchArgsBuffer = samplesDispatchArgsBuffer;
            output.vbufferOIT.samplesGpuCountBuffer = samplesGpuCountBuffer;
            output.vbufferOIT.oitVisibilityBuffer = oitVisibilityBuffer;
            output.vbufferOIT.BRGBindingData = BRGBindingData;
        }

        class VBufferOITCountPassData
        {
            public FrameSettings frameSettings;
            public RendererListHandle rendererList;
            public RenderBRGBindingData BRGBindingData;
        }

        TextureHandle RenderVBufferOITCountPass(RenderGraph renderGraph, TextureHandle colorBuffer, HDCamera hdCamera, CullingResults cullResults, in RenderBRGBindingData BRGBindingData)
        {
            TextureHandle outputStencilCount;
            using (var builder = renderGraph.AddRenderPass<VBufferOITCountPassData>("VBufferOITCount", out var passData, ProfilingSampler.Get(HDProfileId.VBufferOITCount)))
            {
                FrameSettings frameSettings = hdCamera.frameSettings;

                passData.frameSettings = frameSettings;

                outputStencilCount = builder.UseDepthBuffer(renderGraph.CreateTexture(new TextureDesc(Vector2.one, true, true)
                {
                    depthBufferBits = DepthBits.Depth24,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    name = "VisOITStencilCount"
                }), DepthAccess.ReadWrite);

                passData.BRGBindingData = BRGBindingData;
                passData.rendererList = builder.UseRendererList(
                   renderGraph.CreateRendererList(CreateOpaqueRendererListDesc(
                        cullResults, hdCamera.camera,
                        HDShaderPassNames.s_VBufferOITCountName,
                        m_CurrentRendererConfigurationBakedLighting,
                        new RenderQueueRange() { lowerBound = (int)HDRenderQueue.Priority.OrderIndependentTransparent, upperBound = (int)(int)HDRenderQueue.Priority.OrderIndependentTransparent })));

                builder.SetRenderFunc(
                    (VBufferOITCountPassData data, RenderGraphContext context) =>
                    {
                        data.BRGBindingData.globalGeometryPool.BindResourcesGlobal(context.cmd);
                        DrawTransparentRendererList(context, data.frameSettings, data.rendererList);
                    });
            }

            return outputStencilCount;
        }

        class OITTileHistogramPassData
        {
            public int tileSize;
            public int histogramSize;
            public Vector2Int screenSize;
            public int blockJitterOffsetX;
            public int blockJitterOffsetY;
            public ComputeShader cs;
            public Texture2D ditherTexture;
            public TextureHandle stencilBuffer;
            public ComputeBufferHandle histogramBuffer;
        }

        ComputeBufferHandle ComputeOITTiledHistogram(RenderGraph renderGraph, Vector2Int screenSize, int viewCount, TextureHandle stencilBuffer, out int histogramSize, out int tileSize)
        {
            ComputeBufferHandle histogramBuffer = ComputeBufferHandle.nullHandle;
            tileSize = 128;
            histogramSize = tileSize * tileSize;
            using (var builder = renderGraph.AddRenderPass<OITTileHistogramPassData>("OITTileHistogramPassData", out var passData, ProfilingSampler.Get(HDProfileId.OITHistogram)))
            {
                passData.cs = defaultResources.shaders.oitTileHistogramCS;
                passData.screenSize = screenSize;
                passData.blockJitterOffsetX = 0;
                passData.blockJitterOffsetY = 0;
                passData.tileSize = tileSize;
                passData.ditherTexture = defaultResources.textures.blueNoise128RTex;
                passData.stencilBuffer = builder.ReadTexture(stencilBuffer);
                passData.histogramSize = histogramSize;
                passData.histogramBuffer = builder.WriteComputeBuffer(renderGraph.CreateComputeBuffer(new ComputeBufferDesc(histogramSize, sizeof(uint), ComputeBufferType.Raw) { name = "OITHistogram" }));
                histogramBuffer = passData.histogramBuffer;

                builder.SetRenderFunc(
                    (OITTileHistogramPassData data, RenderGraphContext context) =>
                    {
                        int clearKernel = data.cs.FindKernel("MainClearHistogram");
                        context.cmd.SetComputeBufferParam(data.cs, clearKernel, HDShaderIDs._VisOITHistogramOutput, data.histogramBuffer);
                        context.cmd.DispatchCompute(data.cs, clearKernel, HDUtils.DivRoundUp(passData.histogramSize, 64), 1, 1);

                        int histogramKernel = data.cs.FindKernel("MainCreateStencilHistogram");
                        context.cmd.SetComputeTextureParam(data.cs, histogramKernel, HDShaderIDs._OITDitherTexture, data.ditherTexture);
                        context.cmd.SetComputeTextureParam(data.cs, histogramKernel, HDShaderIDs._VisOITCount, (RenderTexture)data.stencilBuffer, 0, RenderTextureSubElement.Stencil);
                        context.cmd.SetComputeBufferParam(data.cs, histogramKernel, HDShaderIDs._VisOITHistogramOutput, data.histogramBuffer);
                        context.cmd.SetComputeIntParam(data.cs, HDShaderIDs._VisOITBlockJitterOffsetX, data.blockJitterOffsetX);
                        context.cmd.SetComputeIntParam(data.cs, HDShaderIDs._VisOITBlockJitterOffsetY, data.blockJitterOffsetY);
                        context.cmd.DispatchCompute(data.cs, histogramKernel, HDUtils.DivRoundUp(data.screenSize.x, 8), HDUtils.DivRoundUp(data.screenSize.y, 8), viewCount);
                    });
            }

            return histogramBuffer;
        }

        class OITHistogramPrefixSumPassData
        {
            public int inputCount;
            public ComputeBufferHandle inputBuffer;
            public GpuPrefixSum prefixSumSystem;
            public GpuPrefixSumRenderGraphResources resources;
        }

        ComputeBufferHandle ComputeOITTiledPrefixSumHistogramBuffer(RenderGraph renderGraph, ComputeBufferHandle histogramInput, int histogramSize)
        {
            ComputeBufferHandle output;
            using (var builder = renderGraph.AddRenderPass<OITHistogramPrefixSumPassData>("OITHistogramPrefixSum", out var passData, ProfilingSampler.Get(HDProfileId.OITHistogramPrefixSum)))
            {
                passData.inputCount = histogramSize;
                passData.inputBuffer = builder.ReadComputeBuffer(histogramInput);
                passData.prefixSumSystem = m_PrefixSumSystem;
                passData.resources = GpuPrefixSumRenderGraphResources.Create(histogramSize, renderGraph, builder);
                output = passData.resources.output;

                builder.SetRenderFunc(
                    (OITHistogramPrefixSumPassData data, RenderGraphContext context) =>
                    {
                        var resources = GpuPrefixSumSupportResources.Load(data.resources);
                        data.prefixSumSystem.DispatchDirect(context.cmd, new GpuPrefixSumDirectArgs()
                        { exclusive = false, inputCount = data.inputCount, input = data.inputBuffer, supportResources = resources });
                    });
            }

            return output;
        }

        class OITAllocateSampleListsPassData
        {
            public ComputeShader cs;
            public GpuPrefixSum prefixSumSystem;
            public Vector2Int screenSize;
            public Vector4 packedArgs;
            public Texture2D ditherTexture;
            public TextureHandle stencilBuffer;
            public ComputeBufferHandle prefixedHistogramBuffer;
            public ComputeBufferHandle outCountBuffer;
            public ComputeBufferHandle outSublistCounterBuffer;
            public ComputeBufferHandle outPixelHashBuffer;
            public GpuPrefixSumRenderGraphResources prefixResources;
        }

        void ComputeOITAllocateSampleLists(
            RenderGraph renderGraph, int maxMaterialSampleCount, Vector2Int screenSize, TextureHandle stencilBuffer, ComputeBufferHandle prefixedHistogramBuffer,
            out ComputeBufferHandle outCountBuffer, out ComputeBufferHandle outOffsetBuffer, out ComputeBufferHandle outSublistCounterBuffer, out ComputeBufferHandle outPixelHashBuffer)
        {
            using (var builder = renderGraph.AddRenderPass<OITAllocateSampleListsPassData>("OITAllocateSampleLists", out var passData, ProfilingSampler.Get(HDProfileId.OITAllocateSampleLists)))
            {
                passData.cs = defaultResources.shaders.oitTileHistogramCS;
                passData.prefixSumSystem = m_PrefixSumSystem;
                passData.screenSize = screenSize;
                passData.ditherTexture = defaultResources.textures.blueNoise128RTex;
                passData.stencilBuffer = builder.ReadTexture(stencilBuffer);
                passData.prefixedHistogramBuffer = builder.ReadComputeBuffer(prefixedHistogramBuffer);
                passData.outCountBuffer = builder.WriteComputeBuffer(renderGraph.CreateComputeBuffer(new ComputeBufferDesc(screenSize.x * screenSize.y, sizeof(uint), ComputeBufferType.Raw) { name = "OITMaterialCountBuffer" }));
                passData.outSublistCounterBuffer = builder.WriteComputeBuffer(renderGraph.CreateComputeBuffer(new ComputeBufferDesc(screenSize.x * screenSize.y, sizeof(uint), ComputeBufferType.Raw) { name = "OITMaterialSublistCounter" }));
                passData.outPixelHashBuffer = builder.WriteComputeBuffer(renderGraph.CreateComputeBuffer(new ComputeBufferDesc(screenSize.x * screenSize.y, sizeof(uint), ComputeBufferType.Raw) { name = "OITMaterialHashBuffer" }));
                passData.prefixResources = GpuPrefixSumRenderGraphResources.Create(screenSize.x * screenSize.y, renderGraph, builder);

                float maxMaterialSampleCountAsFloat; unsafe { maxMaterialSampleCountAsFloat = *((float*)&maxMaterialSampleCount); };
                passData.packedArgs = new Vector4(maxMaterialSampleCountAsFloat, 0.0f, 0.0f, 0.0f);

                outCountBuffer = passData.outCountBuffer;
                outOffsetBuffer = passData.prefixResources.output;
                outSublistCounterBuffer = passData.outSublistCounterBuffer;
                outPixelHashBuffer = passData.outPixelHashBuffer;

                builder.SetRenderFunc(
                    (OITAllocateSampleListsPassData data, RenderGraphContext context) =>
                    {
                        int flatCountKernel = data.cs.FindKernel("MainFlatEnableActiveCounts");
                        context.cmd.SetComputeVectorParam(data.cs, HDShaderIDs._PackedOITTileHistogramArgs, data.packedArgs);
                        context.cmd.SetComputeTextureParam(data.cs, flatCountKernel, HDShaderIDs._OITDitherTexture, data.ditherTexture);
                        context.cmd.SetComputeTextureParam(data.cs, flatCountKernel, HDShaderIDs._VisOITCount, (RenderTexture)data.stencilBuffer, 0, RenderTextureSubElement.Stencil);
                        context.cmd.SetComputeBufferParam(data.cs, flatCountKernel, HDShaderIDs._VisOITPrefixedHistogramBuffer, data.prefixedHistogramBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, flatCountKernel, HDShaderIDs._OITOutputActiveCounts, data.outCountBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, flatCountKernel, HDShaderIDs._OITOutputSublistCounter, data.outSublistCounterBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, flatCountKernel, HDShaderIDs._OITOutputPixelHash, data.outPixelHashBuffer);
                        context.cmd.DispatchCompute(data.cs, flatCountKernel, HDUtils.DivRoundUp(data.screenSize.x, 8), HDUtils.DivRoundUp(data.screenSize.y, 8), 1);

                        var prefixResources = GpuPrefixSumSupportResources.Load(data.prefixResources);
                        data.prefixSumSystem.DispatchDirect(context.cmd, new GpuPrefixSumDirectArgs()
                        { exclusive = true, inputCount = data.screenSize.x * data.screenSize.y, input = data.outCountBuffer, supportResources = prefixResources });
                    });
            }
        }

        class OITComputeSamplesDispatchArgsPassData
        {
            public ComputeShader cs;
            public Vector2Int screenSize;
            public GpuPrefixSum prefixSumSystem;
            public ComputeBufferHandle inputCountBuffer;
            public GpuPrefixSumRenderGraphResources prefixResources;
            public ComputeBufferHandle outputDispatchArgsBuffer;
            public ComputeBufferHandle outputGpuCountBuffer;
        }

        void ComputeOITSampleDispatchArgs(RenderGraph renderGraph, ComputeBufferHandle inputCountBuffer, Vector2Int screenSize, out ComputeBufferHandle outputDispatchArgs, out ComputeBufferHandle outputGpuCountBuffer)
        {
            using (var builder = renderGraph.AddRenderPass<OITComputeSamplesDispatchArgsPassData>("OITComputeSamplesDispatchArgs", out var passData, ProfilingSampler.Get(HDProfileId.OITComputeSamplesDispatchArgs)))
            {
                passData.cs = defaultResources.shaders.oitTileHistogramCS;
                passData.screenSize = screenSize;
                passData.prefixSumSystem = m_PrefixSumSystem;
                passData.inputCountBuffer = builder.ReadComputeBuffer(inputCountBuffer);
                passData.prefixResources = GpuPrefixSumRenderGraphResources.Create(screenSize.x * screenSize.y, renderGraph, builder, outputIsTemp : true);
                passData.outputDispatchArgsBuffer = builder.WriteComputeBuffer(renderGraph.CreateComputeBuffer(new ComputeBufferDesc(1, sizeof(uint) * 4, ComputeBufferType.Structured | ComputeBufferType.IndirectArguments) { name = "OITSamplesDispatchArgs" }));
                passData.outputGpuCountBuffer = builder.WriteComputeBuffer(renderGraph.CreateComputeBuffer(new ComputeBufferDesc(1, sizeof(uint), ComputeBufferType.Raw) { name = "OITSamplesCountBuffer" }));

                outputDispatchArgs = passData.outputDispatchArgsBuffer;
                outputGpuCountBuffer = passData.outputGpuCountBuffer;

                builder.SetRenderFunc(
                    (OITComputeSamplesDispatchArgsPassData data, RenderGraphContext context) =>
                    {
                        var prefixResources = GpuPrefixSumSupportResources.Load(data.prefixResources);
                        data.prefixSumSystem.DispatchDirect(context.cmd, new GpuPrefixSumDirectArgs()
                        { exclusive = false, inputCount = data.screenSize.x * data.screenSize.y, input = data.inputCountBuffer, supportResources = prefixResources });

                        int kernel = data.cs.FindKernel("MainCreateSampleDispatchArgs");
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITInputInclusivePrefixSumActiveCount, prefixResources.output);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._OITOutputDispatchSampleArgs, data.outputDispatchArgsBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._OITOutputSamplesCount, data.outputGpuCountBuffer);
                        context.cmd.DispatchCompute(data.cs, kernel, 1, 1, 1);
                    });
            }
        }

        class VBufferOITStoragePassData
        {
            public FrameSettings frameSettings;
            public RendererListHandle rendererList;
            public RenderBRGBindingData BRGBindingData;
            public ComputeBufferHandle countBuffer;
            public ComputeBufferHandle offsetBuffer;
            public ComputeBufferHandle sublistCounterBuffer;
            public ComputeBufferHandle outVisibilityBuffer;
            public ComputeBufferHandle pixelHashBuffer;
        }

        ComputeBufferHandle RenderVBufferOITStoragePass(
            RenderGraph renderGraph, int maxMaterialSampleCount, HDCamera hdCamera, CullingResults cullResults, in RenderBRGBindingData BRGBindingData,
            ComputeBufferHandle countBuffer, ComputeBufferHandle offsetBuffer, ref ComputeBufferHandle sublistCounterBuffer, ComputeBufferHandle pixelHashBuffer)
        {
            ComputeBufferHandle outVisibilityBuffer;
            using (var builder = renderGraph.AddRenderPass<VBufferOITStoragePassData>("VBufferOITStorage", out var passData, ProfilingSampler.Get(HDProfileId.VBufferOITStorage)))
            {
                builder.AllowRendererListCulling(false);

                FrameSettings frameSettings = hdCamera.frameSettings;

                passData.frameSettings = frameSettings;

                passData.countBuffer = builder.ReadComputeBuffer(countBuffer);
                passData.offsetBuffer = builder.ReadComputeBuffer(offsetBuffer);
                passData.sublistCounterBuffer = builder.WriteComputeBuffer(sublistCounterBuffer);
                passData.outVisibilityBuffer = builder.WriteComputeBuffer(renderGraph.CreateComputeBuffer(new ComputeBufferDesc(maxMaterialSampleCount, GetOITVisibilityBufferSize(), ComputeBufferType.Raw) { name = "OITVisibilityBuffer" }));
                passData.pixelHashBuffer = builder.WriteComputeBuffer(pixelHashBuffer);

                outVisibilityBuffer = passData.outVisibilityBuffer;
                sublistCounterBuffer = passData.sublistCounterBuffer;

                passData.BRGBindingData = BRGBindingData;
                passData.rendererList = builder.UseRendererList(
                   renderGraph.CreateRendererList(CreateOpaqueRendererListDesc(
                        cullResults, hdCamera.camera,
                        HDShaderPassNames.s_VBufferOITStorageName,
                        m_CurrentRendererConfigurationBakedLighting,
                        new RenderQueueRange() { lowerBound = (int)HDRenderQueue.Priority.OrderIndependentTransparent, upperBound = (int)(int)HDRenderQueue.Priority.OrderIndependentTransparent })));

                builder.SetRenderFunc(
                    (VBufferOITStoragePassData data, RenderGraphContext context) =>
                    {
                        context.cmd.SetGlobalBuffer(HDShaderIDs._VisOITListsCounts, passData.countBuffer);
                        context.cmd.SetGlobalBuffer(HDShaderIDs._VisOITListsOffsets, passData.offsetBuffer);
                        context.cmd.SetRandomWriteTarget(1, passData.sublistCounterBuffer);
                        context.cmd.SetRandomWriteTarget(2, passData.outVisibilityBuffer);
                        context.cmd.SetRandomWriteTarget(3, passData.pixelHashBuffer);
                        data.BRGBindingData.globalGeometryPool.BindResourcesGlobal(context.cmd);
                        DrawTransparentRendererList(context, data.frameSettings, data.rendererList);
                    });
            }

            return outVisibilityBuffer;
        }

        class VBufferOITComputeHiZPassData
        {
            public ComputeShader cs;
            public RenderBRGBindingData BRGBindingData;
            public ComputeBufferHandle countBuffer;
            public ComputeBufferHandle offsetBuffer;
            public ComputeBufferHandle sublistCounterBuffer;
            public ComputeBufferHandle oitVisibilityBuffer;

            public TextureHandle oitTileHiZOutput;

            public int oitHiZMipIdxGenerated;
            public Vector2Int[] oitHiZMipsOffsets;
            public Vector2Int[] oitHiZMipsSizes;

            public Vector2Int screenSize;
            public Vector4 packedArgs;
        }

        void RenderVBufferOITTileHiZPass(
            RenderGraph renderGraph, HDCamera hdCamera, int maxMaterialSampleCount, Vector2Int screenSize, VBufferOITOutput vbufferOIT,
            ComputeBufferHandle countBuffer, ComputeBufferHandle offsetBuffer, ComputeBufferHandle sublistCounterBuffer, ComputeBufferHandle oitVisibilityBuffer,
            out TextureHandle oitTileHiZTexture/*, out ComputeBufferHandle depthPyramidMipLevelOffsetsBuffer*/)
        {
            using (var builder = renderGraph.AddRenderPass<VBufferOITComputeHiZPassData>("VBufferOITComputeHiZ", out var passData, ProfilingSampler.Get(HDProfileId.VBufferOITComputeHiZ)))
            {
                passData.cs = defaultResources.shaders.oitTileComputeHiZCS;

                passData.screenSize = screenSize;

                float maxMaterialSampleCountAsFloat; unsafe { maxMaterialSampleCountAsFloat = *((float*)&maxMaterialSampleCount); };
                passData.packedArgs = new Vector4(maxMaterialSampleCountAsFloat, 0.0f, 0.0f, 0.0f);
                passData.countBuffer = builder.ReadComputeBuffer(countBuffer);
                passData.oitVisibilityBuffer = builder.ReadComputeBuffer(oitVisibilityBuffer);
                passData.sublistCounterBuffer = builder.ReadComputeBuffer(sublistCounterBuffer);
                passData.offsetBuffer = builder.ReadComputeBuffer(offsetBuffer);

                //depthPyramidMipLevelOffsetsBuffer = renderGraph.CreateComputeBuffer(new ComputeBufferDesc(15, 2*sizeof(int), ComputeBufferType.Structured));

                passData.oitHiZMipsOffsets = hdCamera.depthBufferMipChainInfo.mipLevelOffsets;
                passData.oitHiZMipsSizes = hdCamera.depthBufferMipChainInfo.mipLevelSizes;

                oitTileHiZTexture = builder.ReadWriteTexture(renderGraph.CreateTexture(
                    new TextureDesc(hdCamera.depthBufferMipChainInfo.textureSize.x, hdCamera.depthBufferMipChainInfo.textureSize.y, false, true)
                    {
                        enableRandomWrite = true,
                        useMipMap = false,
                        colorFormat = GetDeferredSSTracingHiZFormat(),
                        bindTextureMS = false,
                        clearBuffer = true,
                        clearColor = Color.black,
                        name = "OITTileHiZ"
                    }));
                passData.oitTileHiZOutput = oitTileHiZTexture;

                builder.SetRenderFunc(
                    (VBufferOITComputeHiZPassData data, RenderGraphContext context) =>
                    {
                        int kernel = data.cs.FindKernel("MainInitialize");
                        context.cmd.SetComputeVectorParam(data.cs, HDShaderIDs._VBufferLightingOffscreenParams, data.packedArgs);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITBuffer, data.oitVisibilityBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITSubListsCounts, data.countBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITListsOffsets, data.offsetBuffer);

                        Vector2Int offsetInput = passData.oitHiZMipsOffsets[0];
                        Vector2Int offsetOutput = passData.oitHiZMipsOffsets[0];
                        Vector2Int sizeOutput = passData.oitHiZMipsSizes[0];

                        context.cmd.SetComputeIntParams(data.cs, HDShaderIDs._OITHiZMipInfos, offsetInput.x, offsetInput.y, offsetOutput.x, offsetOutput.y);

                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._OITTileHiZOutput, data.oitTileHiZOutput, 0);

                        context.cmd.DispatchCompute(data.cs, kernel, HDUtils.DivRoundUp(sizeOutput.x, 8), HDUtils.DivRoundUp(sizeOutput.y, 8), 1);
                    });
            }

            int maxHiZMip = currentAsset.currentPlatformRenderPipelineSettings.orderIndependentTransparentSettings.maxHiZMip;
            Vector2Int screenSizeCurrent = screenSize;
            for (int i = 1; i < maxHiZMip; ++i)
            {
                using (var builder = renderGraph.AddRenderPass<VBufferOITComputeHiZPassData>($"VBufferOITComputeHiZ_{i}", out var passData, ProfilingSampler.Get(HDProfileId.VBufferOITComputeHiZ)))
                {
                    passData.cs = defaultResources.shaders.oitTileComputeHiZCS;

                    float maxMaterialSampleCountAsFloat; unsafe { maxMaterialSampleCountAsFloat = *((float*)&maxMaterialSampleCount); };
                    passData.packedArgs = new Vector4(maxMaterialSampleCountAsFloat, 0.0f, 0.0f, 0.0f);
                    passData.countBuffer = builder.ReadComputeBuffer(countBuffer);
                    passData.oitVisibilityBuffer = builder.ReadComputeBuffer(oitVisibilityBuffer);
                    passData.sublistCounterBuffer = builder.ReadComputeBuffer(sublistCounterBuffer);
                    passData.offsetBuffer = builder.ReadComputeBuffer(offsetBuffer);
                    passData.oitTileHiZOutput = builder.ReadWriteTexture(oitTileHiZTexture);

                    screenSizeCurrent = new Vector2Int(screenSizeCurrent.x / 2, screenSizeCurrent.y / 2);
                    passData.screenSize = screenSizeCurrent;
                    passData.oitHiZMipsOffsets = hdCamera.depthBufferMipChainInfo.mipLevelOffsets;
                    passData.oitHiZMipsSizes = hdCamera.depthBufferMipChainInfo.mipLevelSizes;
                    passData.oitHiZMipIdxGenerated = i;

                    builder.SetRenderFunc(
                        (VBufferOITComputeHiZPassData data, RenderGraphContext context) =>
                        {
                            int kernel = data.cs.FindKernel("MainTileComputeHiZ");
                            context.cmd.SetComputeVectorParam(data.cs, HDShaderIDs._VBufferLightingOffscreenParams, data.packedArgs);
                            context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITBuffer, data.oitVisibilityBuffer);
                            context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITSubListsCounts, data.countBuffer);
                            context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITListsOffsets, data.offsetBuffer);

                            int idx = data.oitHiZMipIdxGenerated;

                            Vector2Int srcSize = data.oitHiZMipsSizes[idx - 1];
                            Vector2Int dstSize = data.oitHiZMipsSizes[idx];
                            Vector2Int srcOffset = data.oitHiZMipsOffsets[idx - 1];
                            Vector2Int dstOffset = data.oitHiZMipsOffsets[idx];
                            Vector2Int srcLimit = srcOffset + srcSize - Vector2Int.one;

                            int[] srcOffsetParams = { srcOffset.x, srcOffset.y, srcLimit.x, srcLimit.y };
                            int[] dstOffsetParams = { dstOffset.x, dstOffset.y, 0, 0 };

                            context.cmd.SetComputeIntParams(data.cs, HDShaderIDs._SrcOffsetAndLimit, srcOffsetParams);
                            context.cmd.SetComputeIntParams(data.cs, HDShaderIDs._DstOffset, dstOffsetParams);
                            context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._OITTileHiZOutput, data.oitTileHiZOutput);

                            context.cmd.DispatchCompute(data.cs, kernel, HDUtils.DivRoundUp(dstSize.x, 8), HDUtils.DivRoundUp(dstSize.y, 8), 1);
                        });
                }
            }

            PushFullScreenDebugTexture(renderGraph, oitTileHiZTexture, FullScreenDebugMode.VisibilityOITHiZ);
        }

        class VBufferOITLightingOffscreen : ForwardOpaquePassData
        {
            public RenderBRGBindingData BRGBindingData;
            public ComputeBufferHandle oitVisibilityBuffer;
            public ComputeBufferHandle pixelHashBuffer;
            public TextureHandle outputColor;
            public Vector4 packedArgs;
            public Vector2Int offscreenDimensions;
        }

        Vector2Int GetOITOffscreenLightingSize(HDCamera hdCamera, int maxSampleCounts)
        {
            int acceptedWidth = Math.Max(hdCamera.actualWidth, 1024);
            return new Vector2Int(acceptedWidth, (int)HDUtils.DivRoundUp(maxSampleCounts, acceptedWidth));
        }

        void RenderOITLighting(
            RenderGraph renderGraph,
            CullingResults cull,
            HDCamera hdCamera,
            ShadowResult shadowResult,
            in BuildGPULightListOutput lightLists,
            in PrepassOutput prepassData,
            in TextureHandle depthBuffer,
            ref TextureHandle colorBuffer)
        {
            if (!prepassData.vbufferOIT.valid)
                return;


            if (m_Asset.currentPlatformRenderPipelineSettings.orderIndependentTransparentSettings.oitLightingMode == OITLightingMode.ForwardFast)
            {
                TextureHandle offscreenLightingTexture = RenderOITVBufferLightingOffscreenForwardFast(
                    renderGraph, cull, hdCamera, shadowResult, prepassData.vbufferOIT, lightLists, prepassData, out var offscreenDimensions);

                TextureHandle sparseColorTexture;
                OITResolveLightingForwardFast(renderGraph, hdCamera, prepassData.vbufferOIT, offscreenLightingTexture, offscreenDimensions, depthBuffer, out sparseColorTexture);

                ResolveSparseLighting(renderGraph, hdCamera,
                    prepassData.vbufferOIT,
                    offscreenDimensions,
                    sparseColorTexture,
                    prepassData.vbufferOIT.pixelHashBuffer,
                    depthBuffer, ref colorBuffer);
            }
            else //if (m_Asset.currentPlatformRenderPipelineSettings.orderIndependentTransparentSettings.oitLightingMode == OITLightingMode.DeferredSSTracing)
            {
                TextureHandle gBuffer0Texture;
                TextureHandle gBuffer1Texture;
                TextureHandle offscreenDirectReflectionLightingTexture;
                RenderOITVBufferLightingOffscreenDeferredSSTracing(
                    renderGraph, cull, hdCamera, shadowResult, prepassData.vbufferOIT, lightLists, prepassData,
                    //depthBuffer,
                    out gBuffer0Texture,
                    out gBuffer1Texture,
                    out offscreenDirectReflectionLightingTexture,
                    out var offscreenDimensions);

                int maxMaterialSampleCount = GetMaxMaterialOITSampleCount(hdCamera);
                TextureHandle oitTileHiZTexture;
                //ComputeBufferHandle depthPyramidMipLevelOffsetsBuffer;
                RenderVBufferOITTileHiZPass(
                    renderGraph, hdCamera, maxMaterialSampleCount, new Vector2Int(hdCamera.actualWidth, hdCamera.actualHeight), prepassData.vbufferOIT,
                    prepassData.vbufferOIT.sampleListCountBuffer,
                    prepassData.vbufferOIT.sampleListOffsetBuffer,
                    prepassData.vbufferOIT.sublistCounterBuffer,
                    prepassData.vbufferOIT.oitVisibilityBuffer, out oitTileHiZTexture);

                TextureHandle photonBufferTexture = OITComputePhotonRefractionBuffer(
                    renderGraph, maxMaterialSampleCount, prepassData.vbufferOIT, gBuffer0Texture, gBuffer1Texture, offscreenDirectReflectionLightingTexture, offscreenDimensions);

                OITResolveLightingDeferredSSTracing(renderGraph, hdCamera, prepassData.vbufferOIT,
                    gBuffer0Texture,
                    gBuffer1Texture,
                    offscreenDirectReflectionLightingTexture,
                    photonBufferTexture,
                    oitTileHiZTexture, offscreenDimensions, depthBuffer, ref colorBuffer);
            }
        }

        TextureHandle RenderOITVBufferLightingOffscreenForwardFast(
            RenderGraph renderGraph,
            CullingResults cull,
            HDCamera hdCamera,
            ShadowResult shadowResult,
            in VBufferOITOutput vbufferOIT,
            in BuildGPULightListOutput lightLists,
            in PrepassOutput prepassData,
            out Vector2Int offscreenDimensions)
        {
            var BRGBindingData = RenderBRG.GetRenderBRGMaterialBindingData();

            int maxSampleCounts = GetMaxMaterialOITSampleCount(hdCamera);
            offscreenDimensions = GetOITOffscreenLightingSize(hdCamera, maxSampleCounts);

            TextureHandle outputColor;
            using (var builder = renderGraph.AddRenderPass<VBufferOITLightingOffscreen>("VBufferOITLightingOffscreenForwardFast", out var passData, ProfilingSampler.Get(HDProfileId.VBufferOITLightingOffscreenForwardFast)))
            {
                var renderListDesc = CreateOpaqueRendererListDesc(
                                    cull,
                                    hdCamera.camera,
                                    HDShaderPassNames.s_VBufferLightingOffscreenForwardFastName, m_CurrentRendererConfigurationBakedLighting, HDRenderQueue.k_RenderQueue_AllTransparentOIT);
                //TODO: hide this from the UI!!
                renderListDesc.renderingLayerMask = DeferredMaterialBRG.RenderLayerMask;
                PrepareCommonForwardPassData(renderGraph, builder, passData, true, hdCamera.frameSettings, renderListDesc, lightLists, shadowResult);

                outputColor = builder.UseColorBuffer(renderGraph.CreateTexture(
                    new TextureDesc(offscreenDimensions.x, offscreenDimensions.y, false, true)
                    {
                        colorFormat = GetForwardFastFormat(),
                        name = "OITOffscreenLightingForwardFast"
                    }), 0);

                passData.BRGBindingData = BRGBindingData;
                passData.oitVisibilityBuffer = builder.ReadComputeBuffer(vbufferOIT.oitVisibilityBuffer);
                passData.pixelHashBuffer = builder.ReadComputeBuffer(vbufferOIT.pixelHashBuffer);

                float packedWidth; unsafe { int offscreenDimsWidth = offscreenDimensions.x; packedWidth = *((float*)&offscreenDimsWidth); };
                float packedMaxSamples; unsafe { packedMaxSamples = *((float*)&maxSampleCounts); };
                passData.packedArgs = new Vector4(packedWidth, packedMaxSamples, 0.0f, 0.0f);
                passData.offscreenDimensions = offscreenDimensions;

                builder.SetRenderFunc(
                    (VBufferOITLightingOffscreen data, RenderGraphContext context) =>
                    {
                        BindGlobalLightListBuffers(data, context);
                        BindDBufferGlobalData(data.dbuffer, context);

                        CoreUtils.SetKeyword(context.cmd, "USE_FPTL_LIGHTLIST", false);
                        CoreUtils.SetKeyword(context.cmd, "USE_CLUSTERED_LIGHTLIST", true);

                        data.BRGBindingData.globalGeometryPool.BindResourcesGlobal(context.cmd);
                        Rect targetViewport = new Rect(0.0f, 0.0f, (float)data.offscreenDimensions.x, (float)data.offscreenDimensions.y);

                        context.cmd.SetGlobalBuffer(HDShaderIDs._OITOutputPixelHash, data.pixelHashBuffer);

                        context.cmd.SetGlobalBuffer(HDShaderIDs._VisOITBuffer, data.oitVisibilityBuffer);
                        context.cmd.SetGlobalVector(HDShaderIDs._VBufferLightingOffscreenParams, data.packedArgs);
                        context.cmd.SetViewport(targetViewport);
                        DrawOpaqueRendererList(context, data.frameSettings, data.rendererList);
                    });
            }

            return outputColor;
        }

        void RenderOITVBufferLightingOffscreenDeferredSSTracing(
            RenderGraph renderGraph,
            CullingResults cull,
            HDCamera hdCamera,
            ShadowResult shadowResult,
            in VBufferOITOutput vbufferOIT,
            in BuildGPULightListOutput lightLists,
            in PrepassOutput prepassData,
            //TextureHandle depthBufferInput,
            out TextureHandle gBuffer0Texture,
            out TextureHandle gBuffer1Texture,
            out TextureHandle offscreenDirectReflectionLightingTexture,
            out Vector2Int offscreenDimensions)
        {
            var BRGBindingData = RenderBRG.GetRenderBRGMaterialBindingData();

            int maxSampleCounts = GetMaxMaterialOITSampleCount(hdCamera);
            offscreenDimensions = GetOITOffscreenLightingSize(hdCamera, maxSampleCounts);

            using (var builder = renderGraph.AddRenderPass<VBufferOITLightingOffscreen>("VBufferOITLightingOffscreenDeferredSSTracing", out var passData, ProfilingSampler.Get(HDProfileId.VBufferOITLightingOffscreenDeferredSSTracing)))
            {
                var renderListDesc = CreateOpaqueRendererListDesc(
                                    cull,
                                    hdCamera.camera,
                                    HDShaderPassNames.s_VBufferLightingOffscreenDeferredSSTracingName, m_CurrentRendererConfigurationBakedLighting, HDRenderQueue.k_RenderQueue_AllTransparentOIT);
                //TODO: hide this from the UI!!
                renderListDesc.renderingLayerMask = DeferredMaterialBRG.RenderLayerMask;
                PrepareCommonForwardPassData(renderGraph, builder, passData, true, hdCamera.frameSettings, renderListDesc, lightLists, shadowResult);

                gBuffer0Texture = builder.UseColorBuffer(renderGraph.CreateTexture(
                    new TextureDesc(offscreenDimensions.x, offscreenDimensions.y, false, true)
                    {
                        colorFormat = GetDeferredSSTracingGBuffer0Format(),
                        name = "OITOffscreenLightingDeferredSSTracing_GBuffer0"
                    }), 0);
                gBuffer1Texture = builder.UseColorBuffer(renderGraph.CreateTexture(
                    new TextureDesc(offscreenDimensions.x, offscreenDimensions.y, false, true)
                    {
                        colorFormat = GetDeferredSSTracingGBuffer1Format(),
                        name = "OITOffscreenLightingDeferredSSTracing_GBuffer1"
                    }), 1);
                offscreenDirectReflectionLightingTexture = builder.UseColorBuffer(renderGraph.CreateTexture(
                    new TextureDesc(offscreenDimensions.x, offscreenDimensions.y, false, true)
                    {
                        colorFormat = GetForwardFastFormat(),
                        name = "OITOffscreenLightingDeferredSSTracing_OffscreenDirectReflectionLighting"
                    }), 2);
                // Setting a depth even if it's unused "Setting MRTs without a depth buffer is not supported."
                TextureHandle depthBuffer = builder.UseDepthBuffer(renderGraph.CreateTexture(
                    new TextureDesc(offscreenDimensions.x, offscreenDimensions.y, false, true)
                    {
                        colorFormat = GraphicsFormat.DepthAuto,
                        name = "OITOffscreenLightingDeferredSSTracing_DepthBuffer"
                    }), DepthAccess.ReadWrite);

                passData.BRGBindingData = BRGBindingData;
                passData.oitVisibilityBuffer = builder.ReadComputeBuffer(vbufferOIT.oitVisibilityBuffer);
                passData.pixelHashBuffer = builder.ReadComputeBuffer(vbufferOIT.pixelHashBuffer);

                float packedWidth; unsafe { int offscreenDimsWidth = offscreenDimensions.x; packedWidth = *((float*)&offscreenDimsWidth); };
                float packedMaxSamples; unsafe { packedMaxSamples = *((float*)&maxSampleCounts); };
                passData.packedArgs = new Vector4(packedWidth, packedMaxSamples, 0.0f, 0.0f);
                passData.offscreenDimensions = offscreenDimensions;

                builder.SetRenderFunc(
                    (VBufferOITLightingOffscreen data, RenderGraphContext context) =>
                    {
                        BindGlobalLightListBuffers(data, context);
                        BindDBufferGlobalData(data.dbuffer, context);

                        CoreUtils.SetKeyword(context.cmd, "USE_FPTL_LIGHTLIST", false);
                        CoreUtils.SetKeyword(context.cmd, "USE_CLUSTERED_LIGHTLIST", true);

                        data.BRGBindingData.globalGeometryPool.BindResourcesGlobal(context.cmd);
                        Rect targetViewport = new Rect(0.0f, 0.0f, (float)data.offscreenDimensions.x, (float)data.offscreenDimensions.y);

                        context.cmd.SetGlobalBuffer(HDShaderIDs._OITOutputPixelHash, data.pixelHashBuffer);
                        context.cmd.SetGlobalBuffer(HDShaderIDs._VisOITBuffer, data.oitVisibilityBuffer);
                        context.cmd.SetGlobalVector(HDShaderIDs._VBufferLightingOffscreenParams, data.packedArgs);
                        context.cmd.SetViewport(targetViewport);
                        DrawOpaqueRendererList(context, data.frameSettings, data.rendererList);
                    });
            }
        }


        class OITResolveForwardFastRenderPass
        {
            public ComputeShader cs;
            public Vector2Int screenSize;
            public TextureHandle offscreenLighting;
            public TextureHandle depthBuffer;
            public ComputeBufferHandle oitVisibilityBuffer;
            public ComputeBufferHandle offsetListBuffer;
            public ComputeBufferHandle sublistCounterBuffer;
            public TextureHandle sparseColorTexture;
            public Vector4 packedArgs;
        }

        void OITResolveLightingForwardFast(RenderGraph renderGraph, HDCamera hdCamera,
            in VBufferOITOutput vbufferOIT,
            TextureHandle offscreenLighting,
            Vector2Int offscreenLightingSize,
            TextureHandle depthBuffer, out TextureHandle sparseColorTexture)
        {

            using (var builder = renderGraph.AddRenderPass<OITResolveForwardFastRenderPass>("OITResolveForwardFastRenderPass", out var passData, ProfilingSampler.Get(HDProfileId.OITResolveLightingForwardFast)))
            {
                passData.cs = defaultResources.shaders.oitResolveForwardFastCS;
                passData.screenSize = new Vector2Int(hdCamera.actualWidth, hdCamera.actualHeight);
                passData.oitVisibilityBuffer = builder.ReadComputeBuffer(vbufferOIT.oitVisibilityBuffer);
                passData.sublistCounterBuffer = builder.ReadComputeBuffer(vbufferOIT.sublistCounterBuffer);
                passData.offsetListBuffer = builder.ReadComputeBuffer(vbufferOIT.sampleListOffsetBuffer);
                passData.offscreenLighting = builder.ReadTexture(offscreenLighting);
                passData.depthBuffer = builder.ReadTexture(depthBuffer);

                sparseColorTexture = builder.ReadWriteTexture(renderGraph.CreateTexture(
                    new TextureDesc(hdCamera.actualWidth, hdCamera.actualHeight, false, true)
                    {
                        enableRandomWrite = true,
                        useMipMap = false,
                        colorFormat = GetForwardFastFormat(),
                        bindTextureMS = false,
                        clearBuffer = true,
                        clearColor = Color.black,
                        name = "OITSparseColorTexture"
                    }));
                passData.sparseColorTexture = builder.WriteTexture(sparseColorTexture);


                float offscreenWidthAsFloat; unsafe { int offscreenWidthInt = offscreenLightingSize.x; offscreenWidthAsFloat = *((float*)&offscreenWidthInt); }
                passData.packedArgs = new Vector4(offscreenWidthAsFloat, 0.0f, 0.0f, 0.0f);

                builder.SetRenderFunc(
                    (OITResolveForwardFastRenderPass data, RenderGraphContext context) =>
                    {
                        int kernel = data.cs.FindKernel("MainResolveOffscreenLighting");
                        context.cmd.SetComputeVectorParam(data.cs, HDShaderIDs._VBufferLightingOffscreenParams, data.packedArgs);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITBuffer, data.oitVisibilityBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITSubListsCounts, data.sublistCounterBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITListsOffsets, data.offsetListBuffer);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITOffscreenLighting, data.offscreenLighting);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._DepthTexture, data.depthBuffer);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITOutputSparseColorBuffer, data.sparseColorTexture);
                        context.cmd.DispatchCompute(data.cs, kernel, HDUtils.DivRoundUp(data.screenSize.x, 8), HDUtils.DivRoundUp(data.screenSize.y, 8), 1);
                    });
            }
        }

        class OITResolveDeferredSSTracingRenderPass
        {
            public ComputeShader cs;
            public Vector2Int screenSize;
            public TextureHandle gBuffer0Texture;
            public TextureHandle gBuffer1Texture;
            public TextureHandle offscreenDirectReflectionLightingTexture;
            public TextureHandle photonTexture;
            public TextureHandle depthBuffer;
            public ComputeBufferHandle oitVisibilityBuffer;
            public ComputeBufferHandle offsetListBuffer;
            public ComputeBufferHandle sublistCounterBuffer;
            public TextureHandle oitTileHiZTexture;
            public TextureHandle outputColor;
            public int maxMipHiZ;
            public ComputeBuffer depthPyramidMipLevelOffsetsBuffer;
            public Vector4 packedArgs;
        }

        void OITResolveLightingDeferredSSTracing(RenderGraph renderGraph, HDCamera hdCamera,
            in VBufferOITOutput vbufferOIT,
            TextureHandle gBuffer0Texture,
            TextureHandle gBuffer1Texture,
            TextureHandle offscreenDirectReflectionLightingTexture,
            TextureHandle photonTexture,
            TextureHandle oitTileHiZTexture,
            Vector2Int offscreenLightingSize,
            TextureHandle depthBuffer, ref TextureHandle colorBuffer)
        {
            using (var builder = renderGraph.AddRenderPass<OITResolveDeferredSSTracingRenderPass>("OITResolveDeferredSSTracingRenderPass", out var passData, ProfilingSampler.Get(HDProfileId.OITResolveLightingDeferredSSTracing)))
            {
                passData.cs = defaultResources.shaders.oitResolveDeferredSSTracingCS;
                passData.screenSize = new Vector2Int(hdCamera.actualWidth, hdCamera.actualHeight);
                passData.oitVisibilityBuffer = builder.ReadComputeBuffer(vbufferOIT.oitVisibilityBuffer);
                passData.sublistCounterBuffer = builder.ReadComputeBuffer(vbufferOIT.sublistCounterBuffer);
                passData.offsetListBuffer = builder.ReadComputeBuffer(vbufferOIT.sampleListOffsetBuffer);

                passData.oitTileHiZTexture = builder.ReadTexture(oitTileHiZTexture);

                passData.gBuffer0Texture = builder.ReadTexture(gBuffer0Texture);
                passData.gBuffer1Texture = builder.ReadTexture(gBuffer1Texture);
                passData.photonTexture = builder.ReadTexture(photonTexture);
                passData.offscreenDirectReflectionLightingTexture = builder.ReadTexture(offscreenDirectReflectionLightingTexture);
                passData.depthBuffer = builder.ReadTexture(depthBuffer);
                passData.outputColor = builder.WriteTexture(colorBuffer);

                float offscreenWidthAsFloat; unsafe { int offscreenWidthInt = offscreenLightingSize.x; offscreenWidthAsFloat = *((float*)&offscreenWidthInt); }
                passData.packedArgs = new Vector4(offscreenWidthAsFloat, 0.0f, 0.0f, 0.0f);

                passData.maxMipHiZ = currentAsset.currentPlatformRenderPipelineSettings.orderIndependentTransparentSettings.maxHiZMip;
                passData.depthPyramidMipLevelOffsetsBuffer = hdCamera.depthBufferMipChainInfo.GetOffsetBufferData(vbufferOIT.depthPyramidMipLevelOffsetsBuffer);

                colorBuffer = passData.outputColor;

                builder.SetRenderFunc(
                    (OITResolveDeferredSSTracingRenderPass data, RenderGraphContext context) =>
                    {
                        int kernel = data.cs.FindKernel("MainResolveOffscreenLighting");
                        context.cmd.SetComputeVectorParam(data.cs, HDShaderIDs._VBufferLightingOffscreenParams, data.packedArgs);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITBuffer, data.oitVisibilityBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITSubListsCounts, data.sublistCounterBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITListsOffsets, data.offsetListBuffer);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._OITTileHiZ, data.oitTileHiZTexture);

                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITOffscreenGBuffer0, data.gBuffer0Texture);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITOffscreenGBuffer1, data.gBuffer1Texture);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITOffscreenDirectReflectionLighting, data.offscreenDirectReflectionLightingTexture);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITOffscreenPhotonRadianceLighting, data.photonTexture);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITOffscreenLighting, data.offscreenDirectReflectionLightingTexture);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._DepthTexture, data.depthBuffer);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._OutputTexture, data.outputColor);

                        context.cmd.SetComputeIntParam(data.cs, HDShaderIDs._OITHiZMaxMip, data.maxMipHiZ);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._DepthPyramidMipLevelOffsets, data.depthPyramidMipLevelOffsetsBuffer);

                        context.cmd.DispatchCompute(data.cs, kernel, HDUtils.DivRoundUp(data.screenSize.x, 8), HDUtils.DivRoundUp(data.screenSize.y, 8), 1);
                    });
            }
        }

        class OITComputePhotonRefractionBufferPassData
        {
            public ComputeShader cs;
            public ComputeBufferHandle oitVisibilityBuffer;
            public ComputeBufferHandle sublistCounterBuffer;
            public ComputeBufferHandle offsetListBuffer;
            public ComputeBufferHandle samplesDispatchArgsBuffer;
            public ComputeBufferHandle samplesGpuCountBuffer;
            public TextureHandle gBuffer0Texture;
            public TextureHandle gBuffer1Texture;
            public TextureHandle offscreenDirectReflectionLightingTexture;
            public Vector4 packedArgs;
            public TextureHandle outputPhotonTexture;
        }

        TextureHandle OITComputePhotonRefractionBuffer(
            RenderGraph renderGraph, int maxSampleCounts,
            in VBufferOITOutput vbufferOIT,
            TextureHandle gBuffer0Texture,
            TextureHandle gBuffer1Texture,
            TextureHandle offscreenDirectReflectionLightingTexture,
            Vector2Int    offscreenLightingSize)
        {
            TextureHandle output;
            using (var builder = renderGraph.AddRenderPass<OITComputePhotonRefractionBufferPassData>("OITComputePhotonRefractionBuffer", out var passData, ProfilingSampler.Get(HDProfileId.OITComputePhotonRefractionBuffer)))
            {
                passData.cs = defaultResources.shaders.oitPhotonBuffer;
                passData.oitVisibilityBuffer = builder.ReadComputeBuffer(vbufferOIT.oitVisibilityBuffer);
                passData.sublistCounterBuffer = builder.ReadComputeBuffer(vbufferOIT.sublistCounterBuffer);
                passData.offsetListBuffer = builder.ReadComputeBuffer(vbufferOIT.sampleListOffsetBuffer);
                passData.samplesDispatchArgsBuffer = builder.ReadComputeBuffer(vbufferOIT.samplesDispatchArgsBuffer);
                passData.samplesGpuCountBuffer = builder.ReadComputeBuffer(vbufferOIT.samplesGpuCountBuffer);

                passData.gBuffer0Texture = builder.ReadTexture(gBuffer0Texture);
                passData.gBuffer1Texture = builder.ReadTexture(gBuffer1Texture);
                passData.offscreenDirectReflectionLightingTexture = builder.ReadTexture(offscreenDirectReflectionLightingTexture);
                passData.outputPhotonTexture = builder.WriteTexture(renderGraph.CreateTexture(
                    new TextureDesc(offscreenLightingSize.x, offscreenLightingSize.y, false, true)
                    {
                        colorFormat = GetForwardFastFormat(),
                        name = "OITOffscreenLightingDeferredSSTracing_PhotonTexture",
                        enableRandomWrite = true,
                        clearBuffer = true,
                        clearColor = Color.black
                    }));

                float offscreenWidthAsFloat; unsafe { int offscreenWidthInt = offscreenLightingSize.x; offscreenWidthAsFloat = *((float*)&offscreenWidthInt); }
                float maxSampleCountAsFloat; unsafe { maxSampleCountAsFloat = *((float*)&maxSampleCounts); }
                passData.packedArgs = new Vector4(offscreenWidthAsFloat, maxSampleCountAsFloat, 0.0f, 0.0f);

                output = passData.outputPhotonTexture;

                builder.SetRenderFunc(
                    (OITComputePhotonRefractionBufferPassData data, RenderGraphContext context) =>
                    {
                        int kernel = data.cs.FindKernel("MainComputePhotonBuffer");
                        context.cmd.SetComputeVectorParam(data.cs, HDShaderIDs._VBufferLightingOffscreenParams, data.packedArgs);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITBuffer, data.oitVisibilityBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITSubListsCounts, data.sublistCounterBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITListsOffsets, data.offsetListBuffer);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITSamplesCountBuffer, data.samplesGpuCountBuffer);

                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITOffscreenGBuffer0, data.gBuffer0Texture);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITOffscreenGBuffer1, data.gBuffer1Texture);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITOffscreenDirectReflectionLighting, data.offscreenDirectReflectionLightingTexture);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITOffscreenDirectReflectionLighting, data.offscreenDirectReflectionLightingTexture);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITOutputPhotonTexture, data.outputPhotonTexture);
                        context.cmd.DispatchCompute(data.cs, kernel, data.samplesDispatchArgsBuffer, 0);
                    });
            }

            return output;
        }

        class OITResolveSparseLighting
        {
            public ComputeShader cs;
            public Vector2Int screenSize;
            public TextureHandle sparseColorBuffer;
            public ComputeBufferHandle pixelHashBuffer;
            public TextureHandle depthBuffer;
            public TextureHandle outputColor;
            public Vector4 packedArgs;
        }

        void ResolveSparseLighting(RenderGraph renderGraph, HDCamera hdCamera,
            in VBufferOITOutput vbufferOIT,
            Vector2Int offscreenLightingSize,
            TextureHandle sparseColorBuffer,
            ComputeBufferHandle pixelHashBuffer,
            TextureHandle depthBuffer, ref TextureHandle colorBuffer)
        {
            using (var builder = renderGraph.AddRenderPass<OITResolveSparseLighting>("VBufferOITLightingOffscreenResolveSparse", out var passData, ProfilingSampler.Get(HDProfileId.VBufferOITLightingOffscreenResolveSparse)))
            {
                passData.cs = defaultResources.shaders.oitResolveForwardFastCS;
                passData.screenSize = new Vector2Int(hdCamera.actualWidth, hdCamera.actualHeight);

                passData.sparseColorBuffer = builder.ReadTexture(sparseColorBuffer);
                passData.pixelHashBuffer = builder.ReadComputeBuffer(pixelHashBuffer);
                passData.depthBuffer = builder.ReadTexture(depthBuffer);
                passData.outputColor = builder.WriteTexture(colorBuffer);

                float offscreenWidthAsFloat; unsafe { int offscreenWidthInt = offscreenLightingSize.x; offscreenWidthAsFloat = *((float*)&offscreenWidthInt); }
                passData.packedArgs = new Vector4(offscreenWidthAsFloat, 0.0f, 0.0f, 0.0f);

                colorBuffer = passData.outputColor;

                builder.SetRenderFunc(
                    (OITResolveSparseLighting data, RenderGraphContext context) =>
                    {
                        int kernel = data.cs.FindKernel("MainResolveSparseOITLighting");
                        context.cmd.SetComputeVectorParam(data.cs, HDShaderIDs._VBufferLightingOffscreenParams, data.packedArgs);
                        
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._VisOITSparseColorBuffer, data.sparseColorBuffer);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._DepthTexture, data.depthBuffer);
                        context.cmd.SetComputeTextureParam(data.cs, kernel, HDShaderIDs._OutputTexture, data.outputColor);
                        context.cmd.SetComputeBufferParam(data.cs, kernel, HDShaderIDs._VisOITPixelHash, data.pixelHashBuffer);
                        context.cmd.DispatchCompute(data.cs, kernel, HDUtils.DivRoundUp(data.screenSize.x, 8), HDUtils.DivRoundUp(data.screenSize.y, 8), 1);
                    });
            }
        }


        class VBufferOITTestLighting
        {
            public RenderBRGBindingData BRGBindingData;
            public Material testLightingMaterial;
            public ComputeBufferHandle oitVisibilityBuffer;
            public ComputeBufferHandle sampleListCountBuffer;
            public ComputeBufferHandle sampleListOffsetBuffer;
            public ComputeBufferHandle sublistCounterBuffer;
            public int testLightingPass;
            public int width;
            public int height;
        }

        TextureHandle TestOITLighting(RenderGraph renderGraph, ref PrepassOutput prepassData, TextureHandle colorBuffer, HDCamera hdCamera)
        {
            var vbufferOIT = prepassData.vbufferOIT;
            if (!vbufferOIT.valid)
                return colorBuffer;

            Material testLightingMaterial = currentAsset.VisibilityOITMaterial;
            int testLightingPass = -1;
            for (int i = 0; i < testLightingMaterial.passCount; ++i)
            {
                if (testLightingMaterial.GetPassName(i).IndexOf("VBufferTestLighting") < 0)
                    continue;

                testLightingPass = i;
                break;
            }

            if (testLightingPass < 0)
                return colorBuffer;

            var BRGBindingData = RenderBRG.GetRenderBRGMaterialBindingData();

            TextureHandle outputColor;
            using (var builder = renderGraph.AddRenderPass<VBufferOITTestLighting>("VBufferTestLighting", out var passData, ProfilingSampler.Get(HDProfileId.VBufferOITTestLighting)))
            {
                outputColor = builder.UseColorBuffer(colorBuffer, 0);
                passData.BRGBindingData = BRGBindingData;
                passData.testLightingMaterial = testLightingMaterial;
                passData.testLightingPass = testLightingPass;
                passData.oitVisibilityBuffer = builder.ReadComputeBuffer(vbufferOIT.oitVisibilityBuffer);
                passData.sampleListCountBuffer = builder.ReadComputeBuffer(vbufferOIT.sampleListCountBuffer);
                passData.sampleListOffsetBuffer = builder.ReadComputeBuffer(vbufferOIT.sampleListOffsetBuffer);
                passData.sublistCounterBuffer = builder.ReadComputeBuffer(vbufferOIT.sublistCounterBuffer);
                passData.width = hdCamera.actualWidth;
                passData.height = hdCamera.actualHeight;

                builder.SetRenderFunc(
                    (VBufferOITTestLighting data, RenderGraphContext context) =>
                    {
                        data.BRGBindingData.globalGeometryPool.BindResourcesGlobal(context.cmd);
                        Rect targetViewport = new Rect(0.0f, 0.0f, data.width, data.height);
                        context.cmd.SetGlobalBuffer(HDShaderIDs._VisOITBuffer, data.oitVisibilityBuffer);
                        context.cmd.SetGlobalBuffer(HDShaderIDs._VisOITListsCounts, data.sampleListCountBuffer);
                        context.cmd.SetGlobalBuffer(HDShaderIDs._VisOITListsOffsets, data.sampleListOffsetBuffer);
                        context.cmd.SetGlobalBuffer(HDShaderIDs._VisOITSubListsCounts, data.sublistCounterBuffer);
                        context.cmd.SetViewport(targetViewport);
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.testLightingMaterial, data.testLightingPass, MeshTopology.Triangles, 3, 1, null);
                    });
            }

            return outputColor;
        }



    }
}

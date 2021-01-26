using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Mathematics;

namespace UnityEngine.Experimental.Rendering.Universal
{

    // TODO: Culling of shadow casters, rotate color channels for shadow casting, check get material functions.


    internal static class ShadowRendering
    {
        private static readonly int k_LightPosID = Shader.PropertyToID("_LightPos");
        private static readonly int k_SelfShadowingID = Shader.PropertyToID("_SelfShadowing");
        private static readonly int k_ShadowStencilGroupID = Shader.PropertyToID("_ShadowStencilGroup");
        private static readonly int k_ShadowIntensityID = Shader.PropertyToID("_ShadowIntensity");
        private static readonly int k_ShadowVolumeIntensityID = Shader.PropertyToID("_ShadowVolumeIntensity");
        private static readonly int k_ShadowRadiusID = Shader.PropertyToID("_ShadowRadius");
        private static readonly int k_ShadowColorMaskID = Shader.PropertyToID("_ShadowColorMask");

        private static readonly ProfilingSampler m_ProfilingSamplerShadows = new ProfilingSampler("Draw 2D Shadow Texture");

        private static RenderTargetHandle[] m_RenderTargets = null;
        private static readonly Color[] k_ColorLookup = new Color[4] { new Color(0, 0, 0, 1), new Color(0, 0, 1, 0), new Color(0, 1, 0, 0), new Color(1, 0, 0, 0) };

        public static  uint maxTextureCount { get; private set; }

        public static void InitializeBudget(uint maxTextureCount)
        {
            if (m_RenderTargets == null || m_RenderTargets.Length != maxTextureCount)
            {
                m_RenderTargets = new RenderTargetHandle[maxTextureCount];
                ShadowRendering.maxTextureCount = maxTextureCount;

                for (int i = 0; i < maxTextureCount; i++)
                {
                    unsafe
                    {
                        m_RenderTargets[i].id = Shader.PropertyToID($"ShadowTex_{i}");
                    }
                }
            }
        }

        private static Material GetProjectedShadowMaterial(this Renderer2DData rendererData, int colorMask)
        {
            //if (rendererData.projectedShadowMaterial == null || rendererData.projectedShadowShader != rendererData.projectedShadowMaterial.shader)
            {
                var material = CoreUtils.CreateEngineMaterial(rendererData.projectedShadowShader);
                material.SetInt(k_ShadowColorMaskID, colorMask);
                material.SetPass(0);
                rendererData.projectedShadowMaterial = material;
            }

            return rendererData.projectedShadowMaterial;
        }

        private static Material GetStencilOnlyShadowMaterial(this Renderer2DData rendererData, int colorMask)
        {
            //if (rendererData.stencilOnlyShadowMaterial == null || rendererData.projectedShadowShader != rendererData.stencilOnlyShadowMaterial.shader)
            {
                var material = CoreUtils.CreateEngineMaterial(rendererData.projectedShadowShader);
                material.SetInt(k_ShadowColorMaskID, colorMask);
                material.SetPass(1);
                rendererData.stencilOnlyShadowMaterial = material;
            }

            return rendererData.stencilOnlyShadowMaterial;
        }

        private static Material GetSpriteSelfShadowMaterial(this Renderer2DData rendererData, int colorMask)
        {
            //if (rendererData.spriteSelfShadowMaterial == null || rendererData.spriteShadowShader != rendererData.spriteSelfShadowMaterial.shader)
            {
                Material material = CoreUtils.CreateEngineMaterial(rendererData.spriteShadowShader);
                material.SetInt(k_ShadowColorMaskID, colorMask);
                rendererData.spriteSelfShadowMaterial = material;
            }

            return rendererData.spriteSelfShadowMaterial;
        }

        private static Material GetSpriteUnshadowMaterial(this Renderer2DData rendererData, int colorMask)
        {
            //if (rendererData.spriteUnshadowMaterial == null || rendererData.spriteUnshadowShader != rendererData.spriteUnshadowMaterial.shader)
            {
                Material material = CoreUtils.CreateEngineMaterial(rendererData.spriteUnshadowShader);
                material.SetInt(k_ShadowColorMaskID, colorMask);
                rendererData.spriteUnshadowMaterial = material;
            }

            return rendererData.spriteUnshadowMaterial;
        }

        public static void CreateShadowRenderTexture(IRenderPass2D pass, RenderingData renderingData, CommandBuffer cmdBuffer, int shadowIndex)
        {
            CreateShadowRenderTexture(pass, m_RenderTargets[shadowIndex], renderingData, cmdBuffer);
        }

        public static void PrerenderShadows(IRenderPass2D pass, RenderingData renderingData, CommandBuffer cmdBuffer, int layerToRender, Light2D light, int shadowIndex, float shadowIntensity)
        {
            // Render the shadows for this light
            RenderShadows(pass, renderingData, cmdBuffer, layerToRender, light, shadowIntensity, m_RenderTargets[shadowIndex].Identifier());
        }

        public static void SetGlobalShadowTexture(CommandBuffer cmdBuffer, Light2D light, int shadowIndex)
        {
            cmdBuffer.SetGlobalTexture("_ShadowTex", m_RenderTargets[shadowIndex].Identifier());
            cmdBuffer.SetGlobalFloat(k_ShadowIntensityID, 1 - light.shadowIntensity);
            cmdBuffer.SetGlobalFloat(k_ShadowVolumeIntensityID, 1 - light.shadowVolumeIntensity);
        }

        public static void DisableGlobalShadowTexture(CommandBuffer cmdBuffer)
        {
            cmdBuffer.SetGlobalFloat(k_ShadowIntensityID, 1);
            cmdBuffer.SetGlobalFloat(k_ShadowVolumeIntensityID, 1);
        }

        private static void CreateShadowRenderTexture(IRenderPass2D pass, RenderTargetHandle rtHandle, RenderingData renderingData, CommandBuffer cmdBuffer)
        {
            var renderTextureScale = Mathf.Clamp(pass.rendererData.lightRenderTextureScale, 0.01f, 1.0f);
            var width = (int)(renderingData.cameraData.cameraTargetDescriptor.width * renderTextureScale);
            var height = (int)(renderingData.cameraData.cameraTargetDescriptor.height * renderTextureScale);

            var descriptor = new RenderTextureDescriptor(width, height);
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.depthBufferBits = 24;
            descriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
            descriptor.msaaSamples = 1;
            descriptor.dimension = TextureDimension.Tex2D;

            cmdBuffer.GetTemporaryRT(rtHandle.id, descriptor, FilterMode.Bilinear);
        }

        public static void ReleaseShadowRenderTexture(CommandBuffer cmdBuffer, int shadowIndex)
        {
            cmdBuffer.ReleaseTemporaryRT(m_RenderTargets[shadowIndex].id);
        }

        public static void RenderShadows(IRenderPass2D pass, RenderingData renderingData, CommandBuffer cmdBuffer, int layerToRender, Light2D light, float shadowIntensity, RenderTargetIdentifier renderTexture)
        {
            using (new ProfilingScope(cmdBuffer, m_ProfilingSamplerShadows))
            {
                cmdBuffer.SetRenderTarget(renderTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
                cmdBuffer.ClearRenderTarget(true, true, Color.clear);  // clear stencil

                var shadowRadius = light.boundingSphere.radius;

                cmdBuffer.SetGlobalVector(k_LightPosID, light.transform.position);
                cmdBuffer.SetGlobalFloat(k_ShadowRadiusID, shadowRadius);

                // TODO: We should have colorBit passed in to this function
                // Bit 0 - Alpha, 1 - Blue, 2 - Green, 3 - Red
                int colorBit = 0;
                int colorMask = 1 << colorBit;

                cmdBuffer.SetGlobalColor(k_ShadowColorMaskID, k_ColorLookup[colorBit]);
                var projectedShadowsMaterial = pass.rendererData.GetProjectedShadowMaterial(colorMask);
                var selfShadowMaterial = pass.rendererData.GetSpriteSelfShadowMaterial(colorMask);
                var unshadowMaterial = pass.rendererData.GetSpriteUnshadowMaterial(colorMask);
                var setGlobalStencilMaterial = pass.rendererData.GetStencilOnlyShadowMaterial(colorMask);

                var shadowCasterGroups = ShadowCasterGroup2DManager.shadowCasterGroups;
                if (shadowCasterGroups != null && shadowCasterGroups.Count > 0)
                {
                    var previousShadowGroupIndex = -1;
                    var incrementingGroupIndex = 0;
                    for (var group = 0; group < shadowCasterGroups.Count; group++)
                    {
                        var shadowCasterGroup = shadowCasterGroups[group];
                        var shadowCasters = shadowCasterGroup.GetShadowCasters();

                        if (shadowCasters != null)
                        {
                            // Draw the projected shadows for the shadow caster group. Writing into the group stencil buffer bit
                            for (var i = 0; i < shadowCasters.Count; i++)
                            {
                                var shadowCaster = shadowCasters[i];

                                if (shadowCaster.IsLit(light))
                                {
                                    if (shadowCaster != null && projectedShadowsMaterial != null && shadowCaster.IsShadowedLayer(layerToRender))
                                    {
                                        if (shadowCaster.castsShadows)
                                            cmdBuffer.DrawMesh(shadowCaster.mesh, shadowCaster.transform.localToWorldMatrix, projectedShadowsMaterial, 0, 0);
                                    }
                                }
                            }

                            // Draw the sprites, either as self shadowing or unshadowing
                            for (var i = 0; i < shadowCasters.Count; i++)
                            {
                                var shadowCaster = shadowCasters[i];

                                if (shadowCaster != null && shadowCaster.IsShadowedLayer(layerToRender))
                                {
                                    if (shadowCaster.useRendererSilhouette)
                                    {
                                        // Draw using the sprite renderer
                                        var renderer = (Renderer)null;
                                        shadowCaster.TryGetComponent<Renderer>(out renderer);

                                        if (renderer != null)
                                        {
                                            var material = shadowCaster.selfShadows ? selfShadowMaterial : unshadowMaterial;
                                            if (material != null)
                                                cmdBuffer.DrawRenderer(renderer, material);
                                        }
                                    }
                                    else
                                    {
                                        var meshMat = shadowCaster.transform.localToWorldMatrix;
                                        var material = shadowCaster.selfShadows ? selfShadowMaterial : unshadowMaterial;

                                        // Draw using the shadow mesh
                                        if (material != null)
                                            cmdBuffer.DrawMesh(shadowCaster.mesh, meshMat, material);
                                    }
                                }
                            }

                            // Draw the projected shadows for the shadow caster group. Writing clearing the group stencil bit, and setting the global bit
                            for (var i = 0; i < shadowCasters.Count; i++)
                            {
                                var shadowCaster = shadowCasters[i];

                                if (shadowCaster.IsLit(light))
                                {

                                    if (shadowCaster != null && projectedShadowsMaterial != null && shadowCaster.IsShadowedLayer(layerToRender))
                                    {
                                        if (shadowCaster.castsShadows)
                                            cmdBuffer.DrawMesh(shadowCaster.mesh, shadowCaster.transform.localToWorldMatrix, projectedShadowsMaterial, 0, 1);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

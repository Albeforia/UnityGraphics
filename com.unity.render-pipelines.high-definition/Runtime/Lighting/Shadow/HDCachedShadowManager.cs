using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Serialization;

namespace UnityEngine.Rendering.HighDefinition
{
    // Note: The punctual and area light shadows have a specific atlas, however because there can be only be only one directional light casting shadow
    // we use this cached shadow manager only as a source of utilities functions, but the data is stored in the dynamic shadow atlas.

    /// <summary>
    /// The class responsible to handle cached shadow maps (shadows with Update mode set to OnEnable or OnDemand).
    /// </summary>
    public class HDCachedShadowManager
    {
        public enum FlagType
        {
            directionalShadowPendingUpdate,
            directionalShadowHasRendered
        }

        private static HDCachedShadowManager s_Instance = new HDCachedShadowManager();
        /// <summary>
        /// Get the cached shadow manager to control cached shadow maps.
        /// </summary>
        public static HDCachedShadowManager instance { get { return s_Instance; } }

        // Data for cached directional light shadows.
        private const int m_MaxShadowCascades = 4;
        private BitArray8 directionalShadowPendingUpdate;
        private BitArray8 directionalShadowHasRendered;
        private Vector3 m_CachedDirectionalForward;
        private float3 m_CachedDirectionalAngles;

        // Helper array used to check what has been tmp filled.
        private (int, int)[] m_TempFilled = new(int, int)[6];

        // Cached atlas
        internal HDCachedShadowAtlas punctualShadowAtlas;
        internal HDCachedShadowAtlas areaShadowAtlas;
        // Cache here to be able to compute resolutions.
        private HDShadowInitParameters m_InitParams;


        // ------------------------ Debug API -------------------------------
#if UNITY_EDITOR
        internal void PrintLightStatusInCachedAtlas()
        {
            bool headerPrinted = false;
            var lights = GameObject.FindObjectsOfType<HDAdditionalLightData>();
            foreach (var light in lights)
            {
                ShadowMapType shadowMapType = light.GetShadowMapType(light.type);
                if (instance.LightIsPendingPlacement(light, shadowMapType))
                {
                    if (!headerPrinted)
                    {
                        Debug.Log(" ===== Lights pending placement in the cached shadow atlas: ===== ");
                        headerPrinted = true;
                    }
                    Debug.Log("\t Name: " + light.name + " Type: " + light.type + " Resolution: " + light.GetResolutionFromSettings(shadowMapType, m_InitParams));
                }
            }

            headerPrinted = false;
            foreach (var light in lights)
            {
                ShadowMapType shadowMapType = light.GetShadowMapType(light.type);
                if (!(instance.LightIsPendingPlacement(light, light.GetShadowMapType(light.type))) && light.lightIdxForCachedShadows != -1)
                {
                    if (!headerPrinted)
                    {
                        Debug.Log("===== Lights placed in cached shadow atlas: ===== ");
                        headerPrinted = true;
                    }
                    Debug.Log("\t Name: " + light.name + " Type: " + light.type + " Resolution: " + light.GetResolutionFromSettings(shadowMapType, m_InitParams));
                }
            }
        }
#endif
        // ------------------------ Public API -------------------------------

        /// <summary>
        /// This function verifies if a shadow map of resolution shadowResolution for a light of type lightType would fit in the atlas when inserted.
        /// </summary>
        /// <param name="shadowResolution">The resolution of the hypothetical shadow map that we are assessing.</param>
        /// <param name="lightType">The type of the light that cast the hypothetical shadow map that we are assessing.</param>
        /// <returns>True if the shadow map would fit in the atlas, false otherwise.</returns>
        public bool WouldFitInAtlas(int shadowResolution, HDLightType lightType)
        {
            bool fits = true;
            int x = 0;
            int y = 0;

            if (lightType == HDLightType.Point)
            {
                int fitted = 0;
                for (int i = 0; i < 6; ++i)
                {
                    fits = fits && HDShadowManager.cachedShadowManager.punctualShadowAtlas.FindSlotInAtlas(shadowResolution, true, out x, out y);
                    if (fits)
                    {
                        m_TempFilled[fitted++] = (x, y);
                    }
                    else
                    {
                        // Free the temp filled ones.
                        for (int filled = 0; filled < fitted; ++filled)
                        {
                            HDShadowManager.cachedShadowManager.punctualShadowAtlas.FreeTempFilled(m_TempFilled[filled].Item1, m_TempFilled[filled].Item2, shadowResolution);
                        }
                        return false;
                    }
                }

                // Free the temp filled ones.
                for (int filled = 0; filled < fitted; ++filled)
                {
                    HDShadowManager.cachedShadowManager.punctualShadowAtlas.FreeTempFilled(m_TempFilled[filled].Item1, m_TempFilled[filled].Item2, shadowResolution);
                }
            }

            if (lightType == HDLightType.Spot)
                fits = fits && HDShadowManager.cachedShadowManager.punctualShadowAtlas.FindSlotInAtlas(shadowResolution, out x, out y);

            if (lightType == HDLightType.Area)
                fits = fits && HDShadowManager.cachedShadowManager.areaShadowAtlas.FindSlotInAtlas(shadowResolution, out x, out y);

            return fits;
        }

        /// <summary>
        /// This function verifies if the shadow map for the passed light would fit in the atlas when inserted.
        /// </summary>
        /// <param name="lightData">The light that we try to fit in the atlas.</param>
        /// <returns>True if the shadow map would fit in the atlas, false otherwise. If lightData does not cast shadows, false is returned.</returns>
        public bool WouldFitInAtlas(HDAdditionalLightData lightData)
        {
            if (lightData.legacyLight.shadows != LightShadows.None)
            {
                var lightType = lightData.type;
                var resolution = lightData.GetResolutionFromSettings(lightData.GetShadowMapType(lightType), m_InitParams);
                return WouldFitInAtlas(resolution, lightType);
            }
            return false;
        }

        /// <summary>
        /// If a light is added after a scene is loaded, its placement in the atlas might be not optimal and the suboptimal placement might prevent a light to find a place in the atlas.
        /// This function will force a defragmentation of the atlas containing lights of type lightType and redistributes the shadows inside so that the placement is optimal. Note however that this will also mark the shadow maps
        /// as dirty and they will be re-rendered as soon the light will come into view for the first time after this function call.
        /// </summary>
        /// <param name="lightType">The type of the light contained in the atlas that need defragmentation.</param>
        public void DefragAtlas(HDLightType lightType)
        {
            if (lightType == HDLightType.Area)
                instance.areaShadowAtlas.DefragmentAtlasAndReRender(instance.m_InitParams);
            if (lightType == HDLightType.Point || lightType == HDLightType.Spot)
                instance.punctualShadowAtlas.DefragmentAtlasAndReRender(instance.m_InitParams);
        }

        /// <summary>
        /// This function can be used to evict a light from its atlas. The slots occupied by such light will be available to be occupied by other shadows.
        /// Note that eviction happens automatically upon light destruction and, if lightData.preserveCachedShadow is false, upon disabling of the light.
        /// </summary>
        /// <param name="lightData">The light to evict from the atlas.</param>
        public void ForceEvictLight(HDAdditionalLightData lightData)
        {
            EvictLight(lightData);
            lightData.lightIdxForCachedShadows = -1;
        }

        /// <summary>
        /// This function can be used to register a light to the cached shadow system if not already registered. It is necessary to call this function if a light has been
        /// evicted with ForceEvictLight and it needs to be registered again. Please note that a light is automatically registered when enabled or when the shadow update changes
        /// from EveryFrame to OnDemand or OnEnable.
        /// </summary>
        /// <param name="lightData">The light to register.</param>
        public void ForceRegisterLight(HDAdditionalLightData lightData)
        {
            // Note: this is for now just calling the internal API, but having a separate API helps with future
            // changes to the process.
            RegisterLight(lightData);
        }

 /// <summary>
        /// This function verifies if the light has its shadow maps placed in the cached shadow atlas.
        /// </summary>
        /// <param name="lightData">The light that we want to check the placement of.</param>
        /// <returns>True if the shadow map is already placed in the atlas, false otherwise.</returns>
        public bool LightHasBeenPlacedInAtlas(HDAdditionalLightData lightData)
        {
            var lightType = lightData.type;
            if (lightType == HDLightType.Area)
                return instance.areaShadowAtlas.LightIsPlaced(lightData);
            if (lightType == HDLightType.Point || lightType == HDLightType.Spot)
                return instance.punctualShadowAtlas.LightIsPlaced(lightData);
            if (lightType == HDLightType.Directional)
                return !lightData.ShadowIsUpdatedEveryFrame();

            return false;
        }

        /// <summary>
        /// This function verifies if the light has its shadow maps placed in the cached shadow atlas and if it was rendered at least once.
        /// </summary>
        /// <param name="lightData">The light that we want to check.</param>
        /// <param name="numberOfCascades">Optional parameter required only when querying data about a directional light. It needs to match the number of cascades used by the directional light.</param>
        /// <returns>True if the shadow map is already placed in the atlas and rendered at least once, false otherwise.</returns>
        public bool LightHasBeenPlaceAndRenderdeAtLeastOnce(HDAdditionalLightData lightData, int numberOfCascades = 0)
        {
            var lightType = lightData.type;
            if (lightType == HDLightType.Area)
            {
                return instance.areaShadowAtlas.LightIsPlaced(lightData) && instance.areaShadowAtlas.FullLightShadowHasRenderedAtLeastOnce(lightData);
            }
            if (lightType == HDLightType.Point || lightType == HDLightType.Spot)
            {
                return instance.punctualShadowAtlas.LightIsPlaced(lightData) && instance.punctualShadowAtlas.FullLightShadowHasRenderedAtLeastOnce(lightData);
            }
            if (lightType == HDLightType.Directional)
            {
                Debug.Assert(numberOfCascades <= m_MaxShadowCascades, "numberOfCascades is bigger than the maximum cascades allowed");
                bool hasRendered = true;
                for (int i = 0; i < numberOfCascades; ++i)
                {
                    hasRendered = hasRendered && directionalShadowHasRendered[(uint)i];
                }
                return !lightData.ShadowIsUpdatedEveryFrame() && hasRendered;
            }

            return false;
        }

        /// <summary>
        /// This function verifies if the light if a specific sub-shadow maps is placed in the cached shadow atlas and if it was rendered at least once.
        /// </summary>
        /// <param name="lightData">The light that we want to check.</param>
        /// <param name="shadowIndex">The sub-shadow index (e.g. cascade index or point light face). It is ignored when irrelevant to the light type.</param>
        /// <returns>True if the shadow map is already placed in the atlas and rendered at least once, false otherwise.</returns>
        public bool ShadowHasBeenPlaceAndRenderedAtLeastOnce(HDAdditionalLightData lightData, int shadowIndex)
        {
            var lightType = lightData.type;
            if (lightType == HDLightType.Area)
            {
                return instance.areaShadowAtlas.LightIsPlaced(lightData) && instance.areaShadowAtlas.ShadowHasRenderedAtLeastOnce(lightData.lightIdxForCachedShadows);
            }
            if (lightType == HDLightType.Spot)
            {
                return instance.punctualShadowAtlas.LightIsPlaced(lightData) && instance.punctualShadowAtlas.ShadowHasRenderedAtLeastOnce(lightData.lightIdxForCachedShadows);
            }
            if (lightType == HDLightType.Point || lightType == HDLightType.Spot)
            {
                if (lightType == HDLightType.Point)
                    Debug.Assert(shadowIndex < 6, "Shadow Index is bigger than the available sub-shadows");

                return instance.punctualShadowAtlas.LightIsPlaced(lightData) && instance.punctualShadowAtlas.ShadowHasRenderedAtLeastOnce(lightData.lightIdxForCachedShadows + shadowIndex);
            }
            if (lightType == HDLightType.Directional)
            {
                Debug.Assert(shadowIndex < m_MaxShadowCascades, "Shadow Index is bigger than the maximum cascades allowed");
                return !lightData.ShadowIsUpdatedEveryFrame() && directionalShadowHasRendered[(uint)shadowIndex];
            }

            return false;
        }


        // ------------------------------------------------------------------------------------------------------------------

        private void MarkAllDirectionalShadowsForUpdate()
        {
            for (int i = 0; i < m_MaxShadowCascades; ++i)
            {
                directionalShadowPendingUpdate[(uint)i] = true;
                directionalShadowHasRendered[(uint)i] = false;
            }
        }

        private HDCachedShadowManager()
        {
            punctualShadowAtlas = new HDCachedShadowAtlas(ShadowMapType.PunctualAtlas);
            if (ShaderConfig.s_AreaLights == 1)
                areaShadowAtlas = new HDCachedShadowAtlas(ShadowMapType.AreaLightAtlas);
        }

        internal void InitPunctualShadowAtlas(HDShadowAtlas.HDShadowAtlasInitParameters atlasInitParams)
        {
            m_InitParams = atlasInitParams.initParams;
            punctualShadowAtlas.InitAtlas(atlasInitParams);
        }

        internal void InitAreaLightShadowAtlas(HDShadowAtlas.HDShadowAtlasInitParameters atlasInitParams)
        {
            m_InitParams = atlasInitParams.initParams;
            areaShadowAtlas.InitAtlas(atlasInitParams);
        }

        internal void SetCachedDirectionalAngles(float3 angles)
        {
            m_CachedDirectionalAngles = angles;
        }
        internal void RegisterLight(HDAdditionalLightData lightData)
        {
			if (!lightData.lightEntity.valid)
            {
                return;
            }
            HDLightType lightType = lightData.type;

            if (lightType == HDLightType.Directional)
            {
                lightData.lightIdxForCachedShadows = 0;
                MarkAllDirectionalShadowsForUpdate();
            }

            if (lightType == HDLightType.Spot || lightType == HDLightType.Point)
            {
                punctualShadowAtlas.RegisterLight(lightData);
            }

            if (ShaderConfig.s_AreaLights == 1 && lightType == HDLightType.Area && lightData.areaLightShape == AreaLightShape.Rectangle)
            {
                areaShadowAtlas.RegisterLight(lightData);
            }
        }

        internal void EvictLight(HDAdditionalLightData lightData)
        {
            HDLightType lightType = lightData.type;

            if (lightType == HDLightType.Directional)
            {
                lightData.lightIdxForCachedShadows = -1;
                MarkAllDirectionalShadowsForUpdate();
            }

            if (lightType == HDLightType.Spot || lightType == HDLightType.Point)
            {
                punctualShadowAtlas.EvictLight(lightData);
            }

            if (ShaderConfig.s_AreaLights == 1 && lightType == HDLightType.Area)
            {
                areaShadowAtlas.EvictLight(lightData);
            }
        }

        internal void RegisterTransformToCache(HDAdditionalLightData lightData)
        {
            HDLightType lightType = lightData.type;

            if (lightType == HDLightType.Spot || lightType == HDLightType.Point)
                punctualShadowAtlas.RegisterTransformCacheSlot(lightData);
            if (ShaderConfig.s_AreaLights == 1 && lightType == HDLightType.Area)
                areaShadowAtlas.RegisterTransformCacheSlot(lightData);
            if (lightType == HDLightType.Directional)
                m_CachedDirectionalAngles = lightData.transform.eulerAngles;
        }

        internal void RemoveTransformFromCache(HDAdditionalLightData lightData)
        {
            HDLightType lightType = lightData.type;

            if (lightType == HDLightType.Spot || lightType == HDLightType.Point)
                punctualShadowAtlas.RemoveTransformFromCache(lightData);
            if (ShaderConfig.s_AreaLights == 1 && lightType == HDLightType.Area)
                areaShadowAtlas.RemoveTransformFromCache(lightData);
        }


        internal void AssignSlotsInAtlases()
        {
            punctualShadowAtlas.AssignOffsetsInAtlas(m_InitParams);
            if(ShaderConfig.s_AreaLights == 1)
                areaShadowAtlas.AssignOffsetsInAtlas(m_InitParams);
        }

        internal void MarkDirectionalShadowAsRendered(int shadowIdx)
        {
            directionalShadowPendingUpdate[(uint)shadowIdx] = false;
            directionalShadowHasRendered[(uint)shadowIdx] = true;
        }

        internal void UpdateDebugSettings(LightingDebugSettings lightingDebugSettings)
        {
            punctualShadowAtlas.UpdateDebugSettings(lightingDebugSettings);
            if (ShaderConfig.s_AreaLights == 1)
                areaShadowAtlas.UpdateDebugSettings(lightingDebugSettings);
        }

        internal void ScheduleShadowUpdate(HDAdditionalLightData light)
        {
            var lightType = light.type;
            if (lightType == HDLightType.Point || lightType == HDLightType.Spot)
                punctualShadowAtlas.ScheduleShadowUpdate(light);
            else if (lightType == HDLightType.Area)
                areaShadowAtlas.ScheduleShadowUpdate(light);
            else if (lightType == HDLightType.Directional)
            {
                MarkAllDirectionalShadowsForUpdate();
            }
        }

        internal void ScheduleShadowUpdate(HDAdditionalLightData light, int subShadowIndex)
        {
            var lightType = light.type;
            if (lightType == HDLightType.Spot)
                punctualShadowAtlas.ScheduleShadowUpdate(light);
            if (lightType == HDLightType.Area)
                areaShadowAtlas.ScheduleShadowUpdate(light);
            if (lightType == HDLightType.Point)
            {
                Debug.Assert(subShadowIndex < 6);
                punctualShadowAtlas.SchedulePartialShadowUpdate(light, light.lightIdxForCachedShadows + subShadowIndex);
            }
            if (lightType == HDLightType.Directional)
            {
                Debug.Assert(subShadowIndex < m_MaxShadowCascades);
                directionalShadowPendingUpdate[(uint)subShadowIndex] = true;
            }
        }

        internal bool LightIsPendingPlacement(HDAdditionalLightData light, ShadowMapType shadowMapType)
        {
            if (shadowMapType == ShadowMapType.PunctualAtlas)
                return punctualShadowAtlas.LightIsPendingPlacement(light);
            if (shadowMapType == ShadowMapType.AreaLightAtlas)
                return areaShadowAtlas.LightIsPendingPlacement(light);

            return false;
        }

        internal void GetUnmanagedDataForShadowRequestJobs(ref HDCachedShadowManagerUnmanaged unmanagedData)
        {
            unmanagedData.directionalShadowHasRendered = directionalShadowHasRendered;
            unmanagedData.directionalShadowPendingUpdate = directionalShadowPendingUpdate;
            punctualShadowAtlas.GetUnmanageDataForShadowRequestJobs(ref unmanagedData.punctualShadowAtlas);
            areaShadowAtlas.GetUnmanageDataForShadowRequestJobs(ref unmanagedData.areaShadowAtlas);
            unmanagedData.cachedDirectionalAngles[0] = m_CachedDirectionalAngles;
        }

        internal void ClearShadowRequests()
        {
            punctualShadowAtlas.Clear();
            if (ShaderConfig.s_AreaLights == 1)
                areaShadowAtlas.Clear();
        }

        internal void Dispose()
        {
            punctualShadowAtlas.Release();
            if (ShaderConfig.s_AreaLights == 1)
                areaShadowAtlas.Release();
        }

		internal void DisposeNativeCollections()
        {
            if (punctualShadowAtlas != null)
                punctualShadowAtlas.DisposeNativeCollections();

            if (areaShadowAtlas != null)
                areaShadowAtlas.DisposeNativeCollections();
        }
    }
}

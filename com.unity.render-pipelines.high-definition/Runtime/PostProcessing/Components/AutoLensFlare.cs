using System;

namespace UnityEngine.Rendering.HighDefinition
{
    [VolumeComponentMenu("Post-processing/AutoLensFlare")]
    public sealed class AutoLensFlare : VolumeComponent, IPostProcessComponent
    {
        /// <summary>
        /// Controls the effectiveness of The Director
        /// </summary>
        [Tooltip("Intensity")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 1f);

        public ClampedFloatParameter blurSize = new ClampedFloatParameter(4f, 0f, 16f);
        public ClampedIntParameter blurSampleCount = new ClampedIntParameter(4, 2, 8);
        public ClampedFloatParameter vignetteInfluence = new ClampedFloatParameter(1f, 0f, 1f);

        public ClampedFloatParameter chromaticAbberationIntensity = new ClampedFloatParameter(0.025f, 0f, 1f);
        public ClampedIntParameter chromaticAbberationSampleCount = new ClampedIntParameter(16, 2, 64);

        public ClampedFloatParameter firstFlareIntensity = new ClampedFloatParameter(0.1f, 0f, 1f);
        public ClampedFloatParameter secondaryFlareIntensity = new ClampedFloatParameter(0.1f, 0f, 1f);
        public ClampedFloatParameter polarFlareIntensity = new ClampedFloatParameter(0.1f, 0f, 1f);

        public ClampedFloatParameter blurContribution = new ClampedFloatParameter(0.5f, 0f, 1f);
        public ClampedFloatParameter chromaContribution = new ClampedFloatParameter(1f, 0f, 1f);

        /// <summary>
        /// Mandatory function, cannot have an Override without it
        /// </summary>
        /// <returns></returns>
        public bool IsActive()
        {
            return intensity.value > 0f;
        }
    }

}

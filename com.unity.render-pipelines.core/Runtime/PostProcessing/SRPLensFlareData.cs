using System.IO;
using UnityEditor;

namespace UnityEngine
{
    /// <summary>
    /// SRPLensFlareBlendMode defined the available blend mode for each LensFlareElement
    /// </summary>
    [System.Serializable]
    public enum SRPLensFlareBlendMode
    {
        /// <summary>
        /// Lerp: Blend SrcAlpha OneMinusSrcAlpha
        /// </summary>
        Lerp,
        /// <summary>
        /// Additive: Blend One One
        /// </summary>
        Additive,
        /// <summary>
        /// Premultiply:
        ///     Blend One OneMinusSrcAlpha
        ///     ColorMask RGB
        /// </summary>
        Premultiply
    }

    /// <summary>
    /// SRPLensFlareDataElement defines a single texture used in a SRPLensFlareData
    /// </summary>
    [System.Serializable]
    public sealed class SRPLensFlareDataElement
    {
        /// <summary>
        /// Initialize default values
        /// </summary>
        public SRPLensFlareDataElement()
        {
            localIntensity = 1.0f;
            position = 1.0f;
            lensFlareTexture = null;
            size = 1.0f;
            aspectRatio = 1.0f;
            rotation = 0.0f;
            tint = new Color(1.0f, 1.0f, 1.0f, 0.5f);
            speed = 1.0f;
            blendMode = SRPLensFlareBlendMode.Additive;
            autoRotate = false;
            isFoldOpened = false;
        }

        /// <summary>
        /// Intensity of this element
        /// </summary>
        [Range(0.0f, 1.0f)]
        public float localIntensity;
        /// <summary>
        /// Relative position compare to the previous one
        /// </summary>
        public float position;
        /// <summary>
        /// Texture used to for this Lens Flare Element
        /// </summary>
        public Texture lensFlareTexture;
        /// <summary>
        /// Scale applied on the width of the texture
        /// </summary>
        [Min(0.0f)]
        public float size;
        /// <summary>
        /// Aspect ratio (height / width)
        /// </summary>
        [Min(0.0f)]
        public float aspectRatio;
        /// <summary>
        /// Local rotation of the texture
        /// </summary>
        [Range(0, 360)]
        public float rotation;
        /// <summary>
        /// Tint of the texture can be modulated by the light we are attached to
        /// </summary>
        public Color tint;
        /// <summary>
        /// Speed of the element on the line
        /// </summary>
        public float speed;
        /// <summary>
        /// Blend mode used
        /// </summary>
        public SRPLensFlareBlendMode blendMode;
        /// <summary>
        /// Rotate the texture relative to the angle on the screen (the rotation will be added to the parameter 'rotation')
        /// </summary>
        public bool autoRotate;
        /// <summary>
        /// Modulate by light color if the asset is used in a 'SRP Lens Flare Source Override'
        /// </summary>
        public bool modulateByLightColor;
        /// <summary>
        /// Internal value use to store the state of minimized or maximized LensFlareElement
        /// </summary>
        public bool isFoldOpened;
    }

    /// <summary>
    /// SRPLensFlareData defines a Lens Flare with a set of SRPLensFlareDataElement
    /// </summary>
    [System.Serializable]
    public sealed class SRPLensFlareData : ScriptableObject
    {
        /// <summary>
        /// Initialize default value
        /// </summary>
        public SRPLensFlareData()
        {
            globalIntensity = 1.0f;
            //scaleCurve = new AnimationCurve(new Keyframe(0.0f, 1.0f), new Keyframe(1.0f, 1.0f));
            scaleCurve = new AnimationCurve(new Keyframe(-1.0f, 1.0f, -1.0f, -1.0f), new Keyframe(0.0f, 0.0f, -1.0f, 1.0f), new Keyframe(1.0f, 1.0f, 1.0f, 1.0f));
            positionCurve = new AnimationCurve(new Keyframe(-1.0f, -1.0f, 1.0f, 1.0f), new Keyframe(0.0f, 0.0f, 1.0f, 1.0f), new Keyframe(1.0f, 1.0f, 1.0f, 1.0f));
            elements = null;
        }

        /// <summary>
        /// GlobalIntensity is used as a multiplier of each elements in the array of SRPLensFlareDataElement
        /// </summary>
        [Min(0)]
        public float globalIntensity;
        /// <summary>
        /// ScaleCurve allow us to define the global shape the lens flares
        /// </summary>
        public AnimationCurve scaleCurve;
        /// <summary>
        /// PositionCurve allow us to place each element of the flare
        /// </summary>
        public AnimationCurve positionCurve;
        /// <summary>
        /// List of SRPLensFlareDataElement
        /// </summary>
        [SerializeField]
        public SRPLensFlareDataElement[] elements;
    }

#if UNITY_EDITOR
    internal static class SRPLensFlareMenu
    {
        private static string GetSelectedAssetFolder()
        {
            if ((Selection.activeObject != null) && AssetDatabase.Contains(Selection.activeObject))
            {
                string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                string assetPathAbsolute = string.Format("{0}/{1}", Path.GetDirectoryName(Application.dataPath), assetPath);

                if (Directory.Exists(assetPathAbsolute))
                {
                    return assetPath;
                }
                else
                {
                    return Path.GetDirectoryName(assetPath);
                }
            }

            return "Assets";
        }

        private static ScriptableObject Create(string className, string assetName, string folder)
        {
            ScriptableObject asset = ScriptableObject.CreateInstance<SRPLensFlareData>();
            if (asset == null)
            {
                Debug.LogError("failed to create instance of " + className);
                return null;
            }

            asset.name = assetName ?? typeof(SRPLensFlareData).Name;

            string assetPath = GetUnusedAssetPath(folder, asset.name);
            AssetDatabase.CreateAsset(asset, assetPath);

            return asset;
        }

        private static string GetUnusedAssetPath(string folder, string assetName)
        {
            for (int n = 0; n < 9999; n++)
            {
                string assetPath = string.Format("{0}/{1}{2}.asset", folder, assetName, (n == 0 ? "" : n.ToString()));
                string existingGUID = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(existingGUID))
                {
                    return assetPath;
                }
            }

            return null;
        }

        [MenuItem("Assets/Create/SRP Lens Flare", priority = 303)]
        private static void CreateSRPLensFlareAsset()
        {
            string className = typeof(SRPLensFlareData).Name;
            string assetName = className;
            string folder = GetSelectedAssetFolder();

            string[] standardNames = new string[] { "Asset", "Attributes", "Container" };
            foreach (string standardName in standardNames)
            {
                assetName = assetName.Replace(standardName, "");
            }

            Create(className, assetName, folder);
        }
    }
#endif
}

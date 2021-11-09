using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomPropertyDrawer(typeof(DecalRendererFeature), false)]
    internal class DecalSettings : ScriptableRendererFeaturePropertyDrawer
    {
        private struct Styles
        {
            public static GUIContent Technique = EditorGUIUtility.TrTextContent("Technique", "This option determines what method is used for rendering decals.");
            public static GUIContent MaxDrawDistance = EditorGUIUtility.TrTextContent("Max Draw Distance", "Maximum global draw distance of decals.");
            public static GUIContent SurfaceData = EditorGUIUtility.TrTextContent("Surface Data", "Allows specifying which decals surface data should be blended with surfaces.");
            public static GUIContent NormalBlend = EditorGUIUtility.TrTextContent("Normal Blend", "Controls the quality of normal reconstruction. The higher the value the more accurate normal reconstruction and the cost on performance.");
            public static GUIContent UseGBuffer = EditorGUIUtility.TrTextContent("Use GBuffer", "Uses traditional GBuffer decals, if renderer is set to deferred. Support only base color, normal and emission. Ignored when using forward rendering.");
        }

        private SerializedProperty m_Technique;
        private SerializedProperty m_MaxDrawDistance;
        private SerializedProperty m_DBufferSettings;
        private SerializedProperty m_DBufferSurfaceData;
        private SerializedProperty m_ScreenSpaceSettings;
        private SerializedProperty m_ScreenSpaceNormalBlend;
        private SerializedProperty m_ScreenSpaceUseGBuffer;

        private SerializedProperty storedProperty = null;

        private void Init(SerializedProperty property)
        {
            if (storedProperty != property)
            {
                SerializedProperty settings = property.FindPropertyRelative("m_Settings");
                m_Technique = settings.FindPropertyRelative("technique");
                m_MaxDrawDistance = settings.FindPropertyRelative("maxDrawDistance");
                m_DBufferSettings = settings.FindPropertyRelative("dBufferSettings");
                m_DBufferSurfaceData = m_DBufferSettings.FindPropertyRelative("surfaceData");
                m_ScreenSpaceSettings = settings.FindPropertyRelative("screenSpaceSettings");
                m_ScreenSpaceNormalBlend = m_ScreenSpaceSettings.FindPropertyRelative("normalBlend");
                m_ScreenSpaceUseGBuffer = m_ScreenSpaceSettings.FindPropertyRelative("useGBuffer");
            }
        }

        private void ValidateGraphicsApis()
        {
            BuildTarget platform = EditorUserBuildSettings.activeBuildTarget;
            GraphicsDeviceType[] graphicsAPIs = PlayerSettings.GetGraphicsAPIs(platform);

            if (System.Array.FindIndex(graphicsAPIs, element => element == GraphicsDeviceType.OpenGLES2) >= 0)
            {
                EditorGUILayout.HelpBox("Decals are not supported with OpenGLES2.", MessageType.Warning);
            }
        }
        protected override void OnGUIRendererFeature(ref Rect position, SerializedProperty property, GUIContent content)
        {
            Init(property);
            ValidateGraphicsApis();

            DrawProperty(ref position, m_Technique, Styles.Technique);

            DecalTechniqueOption technique = (DecalTechniqueOption)m_Technique.intValue;

            EditorGUI.indentLevel++;
            if (technique == DecalTechniqueOption.DBuffer)
            {
                DrawProperty(ref position, m_DBufferSurfaceData, Styles.SurfaceData);
            }
            else if (technique == DecalTechniqueOption.ScreenSpace)
            {
                DrawProperty(ref position, m_ScreenSpaceNormalBlend, Styles.NormalBlend);
                DrawProperty(ref position, m_ScreenSpaceUseGBuffer, Styles.UseGBuffer);
            }
            EditorGUI.indentLevel--;
            DrawProperty(ref position, m_MaxDrawDistance, Styles.MaxDrawDistance);
        }
    }
}

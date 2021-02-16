using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityEditor.Rendering
{
    [CustomEditor(typeof(SRPLensFlareOverride))]
    public class SRPLensFlareOverrideEditor : Editor
    {
        SerializedProperty m_LensFlareData;
        SerializedProperty m_Intensity;
        SerializedProperty m_DistanceAttenuationCurve;
        SerializedProperty m_AttenuationByLightShape;
        SerializedProperty m_RadialScreenAttenuationCurve;
        SerializedProperty m_OcclusionRadius;
        SerializedProperty m_SamplesCount;
        SerializedProperty m_OcclusionOffset;
        SerializedProperty m_AllowOffScreen;

        /// <summary>
        /// Prepare the code for the UI
        /// </summary>
        public void OnEnable()
        {
            PropertyFetcher<SRPLensFlareOverride> entryPoint = new PropertyFetcher<SRPLensFlareOverride>(serializedObject);
            m_LensFlareData = entryPoint.Find(x => x.lensFlareData);
            m_Intensity = entryPoint.Find(x => x.intensity);
            m_DistanceAttenuationCurve = entryPoint.Find(x => x.distanceAttenuationCurve);
            m_AttenuationByLightShape = entryPoint.Find(x => x.attenuationByLightShape);
            m_RadialScreenAttenuationCurve = entryPoint.Find(x => x.radialScreenAttenuationCurve);
            m_OcclusionRadius = entryPoint.Find(x => x.occlusionRadius);
            m_SamplesCount = entryPoint.Find(x => x.sampleCount);
            m_OcclusionOffset = entryPoint.Find(x => x.occlusionOffset);
            m_AllowOffScreen = entryPoint.Find(x => x.allowOffScreen);
        }

        /// <summary>
        /// Implement this function to make a custom inspector
        /// </summary>
        public override void OnInspectorGUI()
        {
            SRPLensFlareOverride lensFlareDat = m_Intensity.serializedObject.targetObject as SRPLensFlareOverride;
            bool attachedToLight = false;
            if (lensFlareDat != null &&
                lensFlareDat.GetComponent<Light>() != null)
            {
                attachedToLight = true;
            }

            EditorGUI.BeginChangeCheck();
            ++EditorGUI.indentLevel;
            EditorGUILayout.BeginFoldoutHeaderGroup(true, "    General", EditorStyles.boldLabel);
            {
                EditorGUILayout.PropertyField(m_LensFlareData);
                EditorGUILayout.PropertyField(m_Intensity);
                EditorGUILayout.PropertyField(m_DistanceAttenuationCurve);
                if (attachedToLight)
                    EditorGUILayout.PropertyField(m_AttenuationByLightShape);
                EditorGUILayout.PropertyField(m_RadialScreenAttenuationCurve);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.BeginFoldoutHeaderGroup(false, "    Occlusion", EditorStyles.boldLabel);
            {
                EditorGUILayout.PropertyField(m_OcclusionRadius);   // Occlusion Fade Radius
                ++EditorGUI.indentLevel;
                EditorGUILayout.PropertyField(m_SamplesCount);      // 
                --EditorGUI.indentLevel;
                EditorGUILayout.PropertyField(m_OcclusionOffset);
                EditorGUILayout.PropertyField(m_AllowOffScreen);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            --EditorGUI.indentLevel;
            if (EditorGUI.EndChangeCheck())
            {
                m_LensFlareData.serializedObject.ApplyModifiedProperties();
            }
        }

        sealed class Styles
        {
            static public readonly GUIContent lensFlareData = new GUIContent("Lens Flare Data", "Lens flare asset used on this component.");
            static public readonly GUIContent intensity = new GUIContent("Intensity", "Intensity.");
            static public readonly GUIContent distanceAttenuationCurve = new GUIContent("Distance Attenuation Curve", "Attenuation by distance, uses world space values.");
            static public readonly GUIContent attenuationByLightShape = new GUIContent("Distance Attenuation Curve", "If component attached to a light, attenuation the lens flare per light type.");
            static public readonly GUIContent radialScreenAttenuationCurve = new GUIContent("Radial Screen Attenuation Curve", "Attenuation used radially, which allow for instance to enable flare only on the edge of the screen.");
            static public readonly GUIContent occlusionRadius = new GUIContent("Occlusion Radius", "Radius around the light used to occlude the flare (value in world space).");
            static public readonly GUIContent sampleCount = new GUIContent("Sample Count", "Random Samples Count used inside the disk with 'occlusionRadius'.");
            static public readonly GUIContent occlusionOffset = new GUIContent("Occlusion Offset", "Z Occlusion Offset allow us to offset the plane where the disc of occlusion is place (closer to camera), value on world space.\nUseful for instance to sample occlusion outside a light bulb if we place a flare inside the light bulb.");
            static public readonly GUIContent allowOffScreen = new GUIContent("Allow Off Screen", "If allowOffScreen is true then If the lens flare is outside the screen we still emit the flare on screen.");
        }
    }
}

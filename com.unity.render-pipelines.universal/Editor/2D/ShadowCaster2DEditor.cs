using UnityEditor.EditorTools;
using UnityEditor.Rendering.Universal.Path2D;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    internal class ShadowCasterPath : ScriptablePath
    {
        internal Bounds GetBounds()
        {
            ShadowCaster2D shadowCaster = (ShadowCaster2D)owner;
            Renderer m_Renderer = shadowCaster.GetComponent<Renderer>();
            if (m_Renderer != null)
            {
                return m_Renderer.bounds;
            }
            else
            {
                Collider2D collider = shadowCaster.GetComponent<Collider2D>();
                if (collider != null)
                    return collider.bounds;
            }

            return new Bounds(shadowCaster.transform.position, shadowCaster.transform.lossyScale);
        }

        public override void SetDefaultShape()
        {
            Clear();
            Bounds bounds = GetBounds();

            AddPoint(new ControlPoint(bounds.min));
            AddPoint(new ControlPoint(new Vector3(bounds.min.x, bounds.max.y)));
            AddPoint(new ControlPoint(bounds.max));
            AddPoint(new ControlPoint(new Vector3(bounds.max.x, bounds.min.y)));

            base.SetDefaultShape();
        }
    }


    [CustomEditor(typeof(ShadowCaster2D))]
    [CanEditMultipleObjects]
    internal class ShadowCaster2DEditor : PathComponentEditor<ShadowCasterPath>
    {
        [EditorTool("Edit Shadow Caster Shape", typeof(ShadowCaster2D))]
        class ShadowCaster2DShadowCasterShapeTool : ShadowCaster2DShapeTool { };

        private static class Styles
        {
            public static GUIContent castsShadows = EditorGUIUtility.TrTextContent("Casts Shadows", "Specifies if this renderer will cast shadows");
            public static GUIContent castingSourcePrefixLabel = EditorGUIUtility.TrTextContent("Casting Source", "Specifies the source used for projected shadows");
            public static GUIContent sortingLayerPrefixLabel = EditorGUIUtility.TrTextContent("Target Sorting Layers", "Apply shadows to the specified sorting layers.");
            public static GUIContent shadowShapeProvider = EditorGUIUtility.TrTextContent("Shape Provider", "This allows a selected component provide a different shape from the Shadow Caster 2D shape. This component must implement IShadowShape2DProvider");
            public static GUIContent shadowShapeContract = EditorGUIUtility.TrTextContent("Contract Edge", "This contracts the edge of the shape given by the shape provider by the specified amount");
            public static GUIContent rendererSilhouette = EditorGUIUtility.TrTextContent("Renderer Silhoutte", "Specifies how to draw the renderer used with the ShadowCaster2D");
            public static GUIContent castingSource = EditorGUIUtility.TrTextContent("Casting Source", "Specifies the source of the shape used for projected shadows");
            public static GUIContent edgeProcessing = EditorGUIUtility.TrTextContent("Edge Processing", "Specifies the edge processing used for contraction");
        }

        SerializedProperty m_RendererSilhouette;
        SerializedProperty m_CastsShadows;
        SerializedProperty m_ShadowShapeProvider;
        SerializedProperty m_CastingSource;
        SerializedProperty m_ShadowMesh;
        SerializedProperty m_EdgeProcessing;
        SerializedProperty m_ContractEdge;

        SortingLayerDropDown m_SortingLayerDropDown;
        CastingSourceDropDown m_CastingSourceDropDown;



        public void OnEnable()
        {
            m_RendererSilhouette = serializedObject.FindProperty("m_RendererSilhouette");
            m_CastsShadows = serializedObject.FindProperty("m_CastsShadows");
            m_ShadowShapeProvider = serializedObject.FindProperty("m_ShadowShapeProvider");
            m_CastingSource = serializedObject.FindProperty("m_ShadowCastingSource");
            m_ShadowMesh = serializedObject.FindProperty("m_ShadowMesh");
            m_EdgeProcessing = m_ShadowMesh.FindPropertyRelative("m_EdgeProcessing");
            m_ContractEdge = m_ShadowMesh.FindPropertyRelative("m_ContractEdge");

            m_SortingLayerDropDown = new SortingLayerDropDown();
            m_SortingLayerDropDown.OnEnable(serializedObject, "m_ApplyToSortingLayers");

            m_CastingSourceDropDown = new CastingSourceDropDown();

            
        }

        public void ShadowCaster2DSceneGUI()
        {
            ShadowCaster2D shadowCaster = target as ShadowCaster2D;

            Transform t = shadowCaster.transform;
            shadowCaster.DrawPreviewOutline();
        }

        public void ShadowCaster2DInspectorGUI<T>() where T : ShadowCaster2DShapeTool
        {
            DoEditButton<T>(PathEditorToolContents.icon, "Edit Shape");
            DoPathInspector<T>();
            DoSnappingInspector<T>();
        }

        public void OnSceneGUI()
        {
            if (m_CastsShadows.boolValue)
                ShadowCaster2DSceneGUI();
        }

        public bool HasRenderer()
        {
            if (targets != null)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    ShadowCaster2D shadowCaster = (ShadowCaster2D)targets[i];
                    Renderer renderer = shadowCaster.GetComponent<Renderer>();
                    if (renderer != null)
                        return true;
                }
            }

            return false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            m_CastingSourceDropDown.OnCastingSource(serializedObject, targets, Styles.castingSourcePrefixLabel);

            if (m_CastingSource.intValue == (int)ShadowCaster2D.ShadowCastingSources.ShapeProvider)
            {
                EditorGUILayout.PropertyField(m_ContractEdge, Styles.shadowShapeContract);
                if (m_ContractEdge.floatValue < 0)
                    m_ContractEdge.floatValue = 0;

               
                EditorGUILayout.PropertyField(m_EdgeProcessing, Styles.edgeProcessing);
            }

            if ((ShadowCaster2D.ShadowCastingSources)m_CastingSource.intValue == ShadowCaster2D.ShadowCastingSources.ShapeEditor)
                ShadowCaster2DInspectorGUI<ShadowCaster2DShadowCasterShapeTool>();
            else if (EditorToolManager.IsActiveTool<ShadowCaster2DShadowCasterShapeTool>())
                ToolManager.RestorePreviousTool();

            if (!HasRenderer())
            {
                using (new EditorGUI.DisabledScope(true))  // Done to support multiedit
                    EditorGUILayout.EnumPopup(Styles.rendererSilhouette, ShadowCaster2D.RendererSilhoutteOptions.None);
            }
            else
            {
                EditorGUILayout.PropertyField(m_RendererSilhouette, Styles.rendererSilhouette);
            }
           

            m_SortingLayerDropDown.OnTargetSortingLayers(serializedObject, targets, Styles.sortingLayerPrefixLabel, null);

            serializedObject.ApplyModifiedProperties();
        }
    }
}

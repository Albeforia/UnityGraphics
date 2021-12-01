using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    using CED = CoreEditorDrawer<SerializedRenderer2DData>;

    struct LightBlendStyleProps
    {
        public SerializedProperty name;
        public SerializedProperty maskTextureChannel;
        public SerializedProperty blendMode;
        public SerializedProperty blendFactorMultiplicative;
        public SerializedProperty blendFactorAdditive;
    }

    enum ShowUIRenderer2DData
    {
        All = 1 << 0,
        General = 1 << 1,
        LightingRenderTexture = 1 << 2,
        LightingBlendStyles = 1 << 3,
        CameraSortingLayerTexture = 1 << 4,
        RendererFeatures = 1 << 5,

    }

    class SerializedRenderer2DData
    {
        public SerializedProperty name;
        public SerializedProperty HDREmulationScale;
        public SerializedProperty lightRenderTextureScale;
        public SerializedProperty lightBlendStyles;
        public LightBlendStyleProps[] lightBlendStylePropsArray;
        public SerializedProperty useDepthStencilBuffer;
        public SerializedProperty maxLightRenderTextureCount;
        public SerializedProperty maxShadowRenderTextureCount;

        public SerializedProperty useCameraSortingLayersTexture;
        public SerializedProperty cameraSortingLayersTextureBound;
        public SerializedProperty cameraSortingLayerDownsamplingMethod;

        public ScriptableRendererFeatureEditor rendererFeatureEditor;

        public Analytics.Renderer2DAnalytics m_Analytics = Analytics.Renderer2DAnalytics.instance;
        public Renderer2DData data;
        public bool m_WasModified;

        public SerializedProperty serializedProperty;

        public ExpandedState<ShowUIRenderer2DData, Renderer2DData> k_showUI { get; }

        public SerializedRenderer2DData(SerializedProperty serializedProperty, int index)
        {
            this.serializedProperty = serializedProperty;
            data = (serializedProperty.serializedObject.targetObject as UniversalRenderPipelineAsset).m_RendererDataList[index] as Renderer2DData;

            m_WasModified = false;

            name = serializedProperty.FindPropertyRelative(nameof(ScriptableRendererData.name));
            HDREmulationScale = serializedProperty.FindPropertyRelative("m_HDREmulationScale");
            lightRenderTextureScale = serializedProperty.FindPropertyRelative("m_LightRenderTextureScale");
            lightBlendStyles = serializedProperty.FindPropertyRelative("m_LightBlendStyles");
            maxLightRenderTextureCount = serializedProperty.FindPropertyRelative("m_MaxLightRenderTextureCount");
            maxShadowRenderTextureCount = serializedProperty.FindPropertyRelative("m_MaxShadowRenderTextureCount");

            useCameraSortingLayersTexture = serializedProperty.FindPropertyRelative("m_UseCameraSortingLayersTexture");
            cameraSortingLayersTextureBound = serializedProperty.FindPropertyRelative("m_CameraSortingLayersTextureBound");
            cameraSortingLayerDownsamplingMethod = serializedProperty.FindPropertyRelative("m_CameraSortingLayerDownsamplingMethod");

            int numBlendStyles = lightBlendStyles.arraySize;
            lightBlendStylePropsArray = new LightBlendStyleProps[numBlendStyles];

            for (int i = 0; i < numBlendStyles; ++i)
            {
                SerializedProperty blendStyleProp = lightBlendStyles.GetArrayElementAtIndex(i);
                ref LightBlendStyleProps props = ref lightBlendStylePropsArray[i];

                props.name = blendStyleProp.FindPropertyRelative("name");
                props.maskTextureChannel = blendStyleProp.FindPropertyRelative("maskTextureChannel");
                props.blendMode = blendStyleProp.FindPropertyRelative("blendMode");
                props.blendFactorMultiplicative = blendStyleProp.FindPropertyRelative("customBlendFactors.multiplicative");
                props.blendFactorAdditive = blendStyleProp.FindPropertyRelative("customBlendFactors.additive");

                if (props.blendFactorMultiplicative == null)
                    props.blendFactorMultiplicative = blendStyleProp.FindPropertyRelative("customBlendFactors.modulate");
                if (props.blendFactorAdditive == null)
                    props.blendFactorAdditive = blendStyleProp.FindPropertyRelative("customBlendFactors.additve");
            }

            useDepthStencilBuffer = serializedProperty.FindPropertyRelative("m_UseDepthStencilBuffer");

            rendererFeatureEditor = new ScriptableRendererFeatureEditor(serializedProperty.FindPropertyRelative(nameof(ScriptableRendererData.m_RendererFeatures)));

            k_showUI = new(ShowUIRenderer2DData.General, $"{index}_URP");
        }
    }

    [CustomPropertyDrawer(typeof(Renderer2DData), false)]
    internal class Renderer2DDataEditor : ScriptableRendererDataEditor
    {
        class Styles
        {
            public static readonly GUIContent generalHeader = EditorGUIUtility.TrTextContent("General");
            public static readonly GUIContent lightRenderTexturesHeader = EditorGUIUtility.TrTextContent("Light Render Textures");
            public static readonly GUIContent lightBlendStylesHeader = EditorGUIUtility.TrTextContent("Light Blend Styles", "A Light Blend Style is a collection of properties that describe a particular way of applying lighting.");
            public static readonly GUIContent postProcessHeader = EditorGUIUtility.TrTextContent("Post-processing");
            public static readonly GUIContent rendererFeaturesHeader = EditorGUIUtility.TrTextContent("Renderer Features");

            public static readonly GUIContent hdrEmulationScale = EditorGUIUtility.TrTextContent("HDR Emulation Scale", "Describes the scaling used by lighting to remap dynamic range between LDR and HDR");
            public static readonly GUIContent lightRTScale = EditorGUIUtility.TrTextContent("Render Scale", "The resolution of intermediate light render textures, in relation to the screen resolution. 1.0 means full-screen size.");
            public static readonly GUIContent maxLightRTCount = EditorGUIUtility.TrTextContent("Max Light Render Textures", "How many intermediate light render textures can be created and utilized concurrently. Higher value usually leads to better performance on mobile hardware at the cost of more memory.");
            public static readonly GUIContent maxShadowRTCount = EditorGUIUtility.TrTextContent("Max Shadow Render Textures", "How many intermediate shadow render textures can be created and utilized concurrently. Higher value usually leads to better performance on mobile hardware at the cost of more memory.");

            public static readonly GUIContent name = EditorGUIUtility.TrTextContent("Name");
            public static readonly GUIContent maskTextureChannel = EditorGUIUtility.TrTextContent("Mask Texture Channel", "Which channel of the mask texture will affect this Light Blend Style.");
            public static readonly GUIContent blendMode = EditorGUIUtility.TrTextContent("Blend Mode", "How the lighting should be blended with the main color of the objects.");
            public static readonly GUIContent useDepthStencilBuffer = EditorGUIUtility.TrTextContent("Depth/Stencil Buffer", "Uncheck this when you are certain you don't use any feature that requires the depth/stencil buffer (e.g. Sprite Mask). Not using the depth/stencil buffer may improve performance, especially on mobile platforms.");
            public static readonly GUIContent postProcessIncluded = EditorGUIUtility.TrTextContent("Enabled", "Turns post-processing on (check box selected) or off (check box cleared). If you clear this check box, Unity excludes post-processing render Passes, shaders, and textures from the build.");
            public static readonly GUIContent postProcessData = EditorGUIUtility.TrTextContent("Data", "The asset containing references to shaders and Textures that the Renderer uses for post-processing.");

            public static readonly GUIContent cameraSortingLayerTextureHeader = EditorGUIUtility.TrTextContent("Camera Sorting Layer Texture", "Layers from back most to selected bounds will be rendered to _CameraSortingLayerTexture");
            public static readonly GUIContent cameraSortingLayerTextureBound = EditorGUIUtility.TrTextContent("Foremost Sorting Layer", "Layers from back most to selected bounds will be rendered to _CameraSortingLayersTexture");
            public static readonly GUIContent cameraSortingLayerDownsampling = EditorGUIUtility.TrTextContent("Downsampling Method", "Method used to copy _CameraSortingLayersTexture");
        }

        SerializedRenderer2DData serialized;
        int lastIndex = -1;

        void SendModifiedAnalytics(Analytics.IAnalytics analytics)
        {
            if (serialized.m_WasModified)
            {
                Analytics.RendererAssetData modifiedData = new Analytics.RendererAssetData();
                modifiedData.was_create_event = false;
                modifiedData.blending_layers_count = 0;
                modifiedData.blending_modes_used = 0;
                analytics.SendData(Analytics.AnalyticsDataTypes.k_Renderer2DDataString, modifiedData);
            }
        }



        private void Init(SerializedProperty property)
        {
            if (serialized != null && property != serialized.serializedProperty || lastIndex != index)
            {
                OnDestroy();
                serialized = new SerializedRenderer2DData(property, index);
                lastIndex = index;
            }
        }
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Init(property);
            DrawGUI(position);
        }

        private void OnDestroy()
        {
            if (serialized != null)
                SendModifiedAnalytics(serialized.m_Analytics);
        }

        public void DrawGUI(Rect position)
        {
            EditorGUI.BeginProperty(position, new GUIContent(serialized.name.stringValue), serialized.serializedProperty);
            CED.FoldoutGroup(new GUIContent(serialized.name.stringValue),
                ShowUIRenderer2DData.All, serialized.k_showUI,
                FoldoutOption.Boxed, DrawRenderer).Draw(serialized, null);
            EditorGUI.EndProperty();
        }

        void DrawRenderer(SerializedRenderer2DData serialized, Editor ownerEditor)
        {
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            CED.Group(
                CED.FoldoutGroup(Styles.generalHeader,
                    ShowUIRenderer2DData.General, serialized.k_showUI,
                    FoldoutOption.SubFoldout, DrawGeneral),
                CED.FoldoutGroup(Styles.lightRenderTexturesHeader,
                    ShowUIRenderer2DData.LightingRenderTexture, serialized.k_showUI,
                    FoldoutOption.SubFoldout, DrawLightRenderTextures),
                CED.FoldoutGroup(Styles.lightBlendStylesHeader,
                    ShowUIRenderer2DData.LightingBlendStyles, serialized.k_showUI,
                    FoldoutOption.SubFoldout, DrawLightBlendStyles),
                CED.FoldoutGroup(Styles.cameraSortingLayerDownsampling,
                    ShowUIRenderer2DData.CameraSortingLayerTexture, serialized.k_showUI,
                    FoldoutOption.SubFoldout, DrawCameraSortingLayerTexture),
                CED.FoldoutGroup(Styles.rendererFeaturesHeader,
                    ShowUIRenderer2DData.RendererFeatures, serialized.k_showUI,
                    FoldoutOption.SubFoldout, DrawRendererFeatures)
            ).Draw(serialized, null);
            if (EditorGUI.EndChangeCheck())
            {
                serialized.m_WasModified |= true;
            }
            EditorGUI.indentLevel--;
        }

        private static void DrawGeneral(SerializedRenderer2DData serialized, Editor ownerEditor)
        {
            EditorGUI.indentLevel += 2;
            EditorGUILayout.PropertyField(serialized.useDepthStencilBuffer, Styles.useDepthStencilBuffer);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serialized.HDREmulationScale, Styles.hdrEmulationScale);
            if (EditorGUI.EndChangeCheck() && serialized.HDREmulationScale.floatValue < 1.0f)
                serialized.HDREmulationScale.floatValue = 1.0f;

            EditorGUILayout.Space();
            EditorGUI.indentLevel -= 2;
        }

        private static void DrawLightRenderTextures(SerializedRenderer2DData serialized, Editor ownerEditor)
        {
            EditorGUI.indentLevel += 2;
            EditorGUILayout.PropertyField(serialized.lightRenderTextureScale, Styles.lightRTScale);
            EditorGUILayout.PropertyField(serialized.maxLightRenderTextureCount, Styles.maxLightRTCount);
            EditorGUILayout.PropertyField(serialized.maxShadowRenderTextureCount, Styles.maxShadowRTCount);

            EditorGUILayout.Space();
            EditorGUI.indentLevel -= 2;
        }

        private static void DrawLightBlendStyles(SerializedRenderer2DData serialized, Editor ownerEditor)
        {
            EditorGUI.indentLevel += 2;
            int numBlendStyles = serialized.lightBlendStyles.arraySize;
            for (int i = 0; i < numBlendStyles; ++i)
            {
                ref LightBlendStyleProps props = ref serialized.lightBlendStylePropsArray[i];

                EditorGUILayout.PropertyField(props.name, Styles.name);
                EditorGUILayout.PropertyField(props.maskTextureChannel, Styles.maskTextureChannel);
                EditorGUILayout.PropertyField(props.blendMode, Styles.blendMode);

                EditorGUILayout.Space();
                EditorGUILayout.Space();
            }

            EditorGUILayout.Space();
            EditorGUI.indentLevel -= 2;
        }

        public static void DrawCameraSortingLayerTexture(SerializedRenderer2DData serialized, Editor ownerEditor)
        {
            EditorGUI.indentLevel += 2;
            SortingLayer[] sortingLayers = SortingLayer.layers;
            string[] optionNames = new string[sortingLayers.Length + 1];
            int[] optionIds = new int[sortingLayers.Length + 1];
            optionNames[0] = "Disabled";
            optionIds[0] = -1;

            int currentOptionIndex = 0;
            for (int i = 0; i < sortingLayers.Length; i++)
            {
                optionNames[i + 1] = sortingLayers[i].name;
                optionIds[i + 1] = sortingLayers[i].id;
                if (sortingLayers[i].id == serialized.cameraSortingLayersTextureBound.intValue)
                    currentOptionIndex = i + 1;
            }


            int selectedOptionIndex = !serialized.useCameraSortingLayersTexture.boolValue ? 0 : currentOptionIndex;
            selectedOptionIndex = EditorGUILayout.Popup(Styles.cameraSortingLayerTextureBound, selectedOptionIndex, optionNames);

            serialized.useCameraSortingLayersTexture.boolValue = selectedOptionIndex != 0;
            serialized.cameraSortingLayersTextureBound.intValue = optionIds[selectedOptionIndex];

            EditorGUI.BeginDisabledGroup(!serialized.useCameraSortingLayersTexture.boolValue);
            EditorGUILayout.PropertyField(serialized.cameraSortingLayerDownsamplingMethod, Styles.cameraSortingLayerDownsampling);
            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel -= 2;
        }

        private static void DrawRendererFeatures(SerializedRenderer2DData serialized, Editor ownerEditor)
        {
            EditorGUILayout.Space();
            serialized.rendererFeatureEditor.DrawRendererFeatures();

        }
    }
}

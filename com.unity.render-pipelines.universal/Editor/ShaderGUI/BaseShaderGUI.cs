using System;
using System.Collections.Generic;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEditor.ShaderGraph;
using RenderQueue = UnityEngine.Rendering.RenderQueue;

namespace UnityEditor
{
    public abstract class BaseShaderGUI : ShaderGUI
    {
        #region EnumsAndClasses

        [Flags]
        protected enum Expandable
        {
            SurfaceOptions = 1 << 0,
            SurfaceInputs = 1 << 1,
            Advanced = 1 << 2,
            Details = 1 << 3,
        }

        public enum SurfaceType
        {
            Opaque,
            Transparent
        }

        public enum BlendMode
        {
            Alpha,   // Old school alpha-blending mode, fresnel does not affect amount of transparency
            Premultiply, // Physically plausible transparency mode, implemented as alpha pre-multiply
            Additive,
            Multiply
        }

        public enum SmoothnessSource
        {
            BaseAlpha,
            SpecularAlpha
        }

        public enum RenderFace
        {
            Front = 2,
            Back = 1,
            Both = 0
        }

        protected class Styles
        {
            public static readonly string[] surfaceTypeNames = Enum.GetNames(typeof(SurfaceType));
            public static readonly string[] blendModeNames = Enum.GetNames(typeof(BlendMode));
            public static readonly string[] renderFaceNames = Enum.GetNames(typeof(RenderFace));

            // Categories
            public static readonly GUIContent SurfaceOptions =
                EditorGUIUtility.TrTextContent("Surface Options", "Controls how Universal RP renders the Material on a screen.");

            public static readonly GUIContent SurfaceInputs = EditorGUIUtility.TrTextContent("Surface Inputs",
                "These settings describe the look and feel of the surface itself.");

            public static readonly GUIContent AdvancedLabel = EditorGUIUtility.TrTextContent("Advanced Options",
                "These settings affect behind-the-scenes rendering and underlying calculations.");

            public static readonly GUIContent surfaceType = EditorGUIUtility.TrTextContent("Surface Type",
                "Select a surface type for your texture. Choose between Opaque or Transparent.");

            public static readonly GUIContent blendingMode = EditorGUIUtility.TrTextContent("Blending Mode",
                "Controls how the color of the Transparent surface blends with the Material color in the background.");

            public static readonly GUIContent cullingText = EditorGUIUtility.TrTextContent("Render Face",
                "Specifies which faces to cull from your geometry. Front culls front faces. Back culls backfaces. None means that both sides are rendered.");

            public static readonly GUIContent alphaClipText = EditorGUIUtility.TrTextContent("Alpha Clipping",
                "Makes your Material act like a Cutout shader. Use this to create a transparent effect with hard edges between opaque and transparent areas.");

            public static readonly GUIContent alphaClipThresholdText = EditorGUIUtility.TrTextContent("Threshold",
                "Sets where the Alpha Clipping starts. The higher the value is, the brighter the  effect is when clipping starts.");

            public static readonly GUIContent castShadowText = EditorGUIUtility.TrTextContent("Cast Shadows",
                "When enabled, this GameObject will cast shadows onto any geometry that can receive them.");

            public static readonly GUIContent receiveShadowText = EditorGUIUtility.TrTextContent("Receive Shadows",
                "When enabled, other GameObjects can cast shadows onto this GameObject.");

            public static readonly GUIContent baseMap = EditorGUIUtility.TrTextContent("Base Map",
                "Specifies the base Material and/or Color of the surface. If you’ve selected Transparent or Alpha Clipping under Surface Options, your Material uses the Texture’s alpha channel or color.");

            public static readonly GUIContent emissionMap = EditorGUIUtility.TrTextContent("Emission Map",
                "Sets a Texture map to use for emission. You can also select a color with the color picker. Colors are multiplied over the Texture.");

            public static readonly GUIContent normalMapText =
                EditorGUIUtility.TrTextContent("Normal Map", "Assigns a tangent-space normal map.");

            public static readonly GUIContent bumpScaleNotSupported =
                EditorGUIUtility.TrTextContent("Bump scale is not supported on mobile platforms");

            public static readonly GUIContent fixNormalNow = EditorGUIUtility.TrTextContent("Fix now",
                "Converts the assigned texture to be a normal map format.");

            public static readonly GUIContent queueSlider = EditorGUIUtility.TrTextContent("Priority",
                "Determines the chronological rendering order for a Material. High values are rendered first.");
        }

        #endregion

        #region Variables

        protected MaterialEditor materialEditor { get; set; }

        protected MaterialProperty surfaceTypeProp { get; set; }

        protected MaterialProperty blendModeProp { get; set; }

        protected MaterialProperty cullingProp { get; set; }

        protected MaterialProperty alphaClipProp { get; set; }

        protected MaterialProperty alphaCutoffProp { get; set; }

        protected MaterialProperty castShadowsProp { get; set; }

        protected MaterialProperty receiveShadowsProp { get; set; }

        // Common Surface Input properties

        protected MaterialProperty baseMapProp { get; set; }

        protected MaterialProperty baseColorProp { get; set; }

        protected MaterialProperty emissionMapProp { get; set; }

        protected MaterialProperty emissionColorProp { get; set; }

        protected MaterialProperty queueOffsetProp { get; set; }

        protected bool isShaderGraph { get; set; }

        public bool m_FirstTimeApply = true;

        // By default, everything is expanded, except advanced
        readonly MaterialHeaderScopeList m_MaterialScopeList = new MaterialHeaderScopeList(uint.MaxValue & ~(uint)Expandable.Advanced);

        #endregion

        private const int queueOffsetRange = 50;
        ////////////////////////////////////
        // General Functions              //
        ////////////////////////////////////
        #region GeneralFunctions

        public abstract void MaterialChanged(Material material);

        public virtual void FindProperties(MaterialProperty[] properties)
        {
            surfaceTypeProp = FindProperty(Property.Surface(isShaderGraph), properties);
            blendModeProp = FindProperty(Property.Blend(isShaderGraph), properties);
            cullingProp = FindProperty(Property.Cull(isShaderGraph), properties);
            alphaClipProp = FindProperty(Property.AlphaClip(isShaderGraph), properties);

            // ShaderGraph Lit and Unlit Subtargets only
            castShadowsProp = FindProperty(Property.CastShadows(isShaderGraph), properties, false);

            // ShaderGraph Lit, and Lit.shader
            receiveShadowsProp = FindProperty(Property.ReceiveShadows(isShaderGraph), properties, false);

            // The following are not mandatory for shadergraphs (it's up to the user to add them to their graph)
            alphaCutoffProp = FindProperty("_Cutoff", properties, false);
            baseMapProp = FindProperty("_BaseMap", properties, false);
            baseColorProp = FindProperty("_BaseColor", properties, false);
            emissionMapProp = FindProperty(Property.EmissionMap, properties, false);
            emissionColorProp = FindProperty(Property.EmissionColor, properties, false);
            queueOffsetProp = FindProperty(Property.QueueOffset(isShaderGraph), properties, false);
        }

        protected MaterialProperty[] properties;

        public override void OnGUI(MaterialEditor materialEditorIn, MaterialProperty[] properties)
        {
            if (materialEditorIn == null)
                throw new ArgumentNullException("materialEditorIn");

            this.properties = properties;

            materialEditor = materialEditorIn;
            Material material = materialEditor.target as Material;
            isShaderGraph = material.IsShaderGraph();

            FindProperties(properties);   // MaterialProperties can be animated so we do not cache them but fetch them every event to ensure animated values are updated correctly

            // Make sure that needed setup (ie keywords/renderqueue) are set up if we're switching some existing
            // material to a universal shader.
            if (m_FirstTimeApply)
            {
                OnOpenGUI(material, materialEditorIn);
                m_FirstTimeApply = false;
            }

            ShaderPropertiesGUI(material);
        }

        void UpdateMaterials(MaterialEditor materialEditor)
        {
            foreach (var obj in materialEditor.targets)
                MaterialChanged((Material)obj);
        }

        public virtual void OnOpenGUI(Material material, MaterialEditor materialEditor)
        {
            // Generate the foldouts
            m_MaterialScopeList.RegisterHeaderScope(Styles.SurfaceOptions, (uint)Expandable.SurfaceOptions, DrawSurfaceOptions);
            m_MaterialScopeList.RegisterHeaderScope(Styles.SurfaceInputs, (uint)Expandable.SurfaceInputs, DrawSurfaceInputs);

            FillAdditionalFoldouts(m_MaterialScopeList);

            m_MaterialScopeList.RegisterHeaderScope(Styles.AdvancedLabel, (uint)Expandable.Advanced, DrawAdvancedOptions);

            UpdateMaterials(materialEditor);
        }

        public void ShaderPropertiesGUI(Material material)
        {
            EditorGUI.BeginChangeCheck();
            {
                m_MaterialScopeList.DrawHeaders(materialEditor, material);
                if (EditorGUI.EndChangeCheck())
                    UpdateMaterials(materialEditor);
            }
        }

        #endregion
        ////////////////////////////////////
        // Drawing Functions              //
        ////////////////////////////////////
        #region DrawingFunctions

        public void DrawShaderGraphProperties(Material material)
        {
            if (properties == null)
                return;

            for (var i = 0; i < properties.Length; i++)
            {
                if ((properties[i].flags & (MaterialProperty.PropFlags.HideInInspector | MaterialProperty.PropFlags.PerRendererData)) != 0)
                    continue;

                float h = materialEditor.GetPropertyHeight(properties[i], properties[i].displayName);
                Rect r = EditorGUILayout.GetControlRect(true, h, EditorStyles.layerMaskField);

                materialEditor.ShaderProperty(r, properties[i], properties[i].displayName);
            }
        }

        public static void DrawFloatToggleProperty(GUIContent styles, MaterialProperty prop)
        {
            if (prop == null)
                return;

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = prop.hasMixedValue;
            bool newValue = EditorGUILayout.Toggle(styles, prop.floatValue == 1);
            if (EditorGUI.EndChangeCheck())
                prop.floatValue = newValue ? 1.0f : 0.0f;
            EditorGUI.showMixedValue = false;
        }

        public virtual void DrawSurfaceOptions(Material material)
        {
            DoPopup(Styles.surfaceType, surfaceTypeProp, Styles.surfaceTypeNames);
            if ((SurfaceType)surfaceTypeProp.floatValue == SurfaceType.Transparent)
                DoPopup(Styles.blendingMode, blendModeProp, Styles.blendModeNames);

            DoPopup(Styles.cullingText, cullingProp, Styles.renderFaceNames);

            // materialEditor.ShaderProperty(alphaClipProp, Styles.alphaClipText);      // this fails for ShaderGraphs, that can't tag it as [ToggleUI]
            DrawFloatToggleProperty(Styles.alphaClipText, alphaClipProp);
            if ((alphaClipProp.floatValue == 1) && (alphaCutoffProp != null))
                materialEditor.ShaderProperty(alphaCutoffProp, Styles.alphaClipThresholdText, 1);

            if (castShadowsProp != null)
                // materialEditor.ShaderProperty(castShadowsProp, Styles.castShadowText);
                DrawFloatToggleProperty(Styles.castShadowText, castShadowsProp);

            if (receiveShadowsProp != null)
                // materialEditor.ShaderProperty(receiveShadowsProp, Styles.receiveShadowText);
                DrawFloatToggleProperty(Styles.receiveShadowText, receiveShadowsProp);
        }

        public virtual void DrawSurfaceInputs(Material material)
        {
            DrawBaseProperties(material);
        }

        public virtual void DrawAdvancedOptions(Material material)
        {
            materialEditor.EnableInstancingField();
            DrawQueueOffsetField();
        }

        protected void DrawQueueOffsetField()
        {
            if (queueOffsetProp != null)
                materialEditor.IntSliderShaderProperty(queueOffsetProp, -queueOffsetRange, queueOffsetRange, Styles.queueSlider);
        }

        public virtual void FillAdditionalFoldouts(MaterialHeaderScopeList materialScopesList) {}

        public virtual void DrawBaseProperties(Material material)
        {
            if (baseMapProp != null && baseColorProp != null) // Draw the baseMap, most shader will have at least a baseMap
            {
                materialEditor.TexturePropertySingleLine(Styles.baseMap, baseMapProp, baseColorProp);
            }
        }

        protected virtual void DrawEmissionProperties(Material material, bool keyword)
        {
            var emissive = true;
            var hadEmissionTexture = emissionMapProp.textureValue != null;

            if (!keyword)
            {
                materialEditor.TexturePropertyWithHDRColor(Styles.emissionMap, emissionMapProp, emissionColorProp,
                    false);
            }
            else
            {
                // Emission for GI?
                emissive = materialEditor.EmissionEnabledProperty();

                EditorGUI.BeginDisabledGroup(!emissive);
                {
                    // Texture and HDR color controls
                    materialEditor.TexturePropertyWithHDRColor(Styles.emissionMap, emissionMapProp,
                        emissionColorProp,
                        false);
                }
                EditorGUI.EndDisabledGroup();
            }

            // If texture was assigned and color was black set color to white
            var brightness = emissionColorProp.colorValue.maxColorComponent;
            if (emissionMapProp.textureValue != null && !hadEmissionTexture && brightness <= 0f)
                emissionColorProp.colorValue = Color.white;

            // UniversalRP does not support RealtimeEmissive. We set it to bake emissive and handle the emissive is black right.
            if (emissive)
            {
                var oldFlags = material.globalIlluminationFlags;
                var newFlags = MaterialGlobalIlluminationFlags.BakedEmissive;

                if (brightness <= 0f)
                    newFlags |= MaterialGlobalIlluminationFlags.EmissiveIsBlack;

                if (newFlags != oldFlags)
                    material.globalIlluminationFlags = newFlags;
            }
        }

        public static void DrawNormalArea(MaterialEditor materialEditor, MaterialProperty bumpMap, MaterialProperty bumpMapScale = null)
        {
            if (bumpMapScale != null)
            {
                materialEditor.TexturePropertySingleLine(Styles.normalMapText, bumpMap,
                    bumpMap.textureValue != null ? bumpMapScale : null);
                if (bumpMapScale.floatValue != 1 &&
                    UnityEditorInternal.InternalEditorUtility.IsMobilePlatform(
                        EditorUserBuildSettings.activeBuildTarget))
                    if (materialEditor.HelpBoxWithButton(Styles.bumpScaleNotSupported, Styles.fixNormalNow))
                        bumpMapScale.floatValue = 1;
            }
            else
            {
                materialEditor.TexturePropertySingleLine(Styles.normalMapText, bumpMap);
            }
        }

        protected static void DrawTileOffset(MaterialEditor materialEditor, MaterialProperty textureProp)
        {
            materialEditor.TextureScaleOffsetProperty(textureProp);
        }

        #endregion
        ////////////////////////////////////
        // Material Data Functions        //
        ////////////////////////////////////
        #region MaterialDataFunctions

        public static void SetMaterialKeywords(Material material, Action<Material> shadingModelFunc = null, Action<Material> shaderFunc = null)
        {
            // Setup blending - consistent across all Universal RP shaders
            SetupMaterialBlendMode(material);

            bool isShaderGraph = material.IsShaderGraph();

            // Cast Shadows
            var castShadowsProp = Property.CastShadows(isShaderGraph);
            if (material.HasProperty(castShadowsProp))
            {
                bool castShadows = (material.GetFloat(castShadowsProp) != 0.0f);
                material.SetShaderPassEnabled("ShadowCaster", castShadows);
            }

            // Receive Shadows
            var receiveShadowsProp = Property.ReceiveShadows(isShaderGraph);
            if (material.HasProperty(receiveShadowsProp))
                CoreUtils.SetKeyword(material, Keyword.HW_ReceiveShadowsOff, material.GetFloat(receiveShadowsProp) == 0.0f);

            // Setup double sided GI based on Cull state
            var cullProp = Property.Cull(isShaderGraph);
            if (material.HasProperty(cullProp))
            {
                bool doubleSidedGI = (RenderFace)material.GetFloat(cullProp) != RenderFace.Front;
                if (doubleSidedGI != material.doubleSidedGI)
                    material.doubleSidedGI = doubleSidedGI;
            }

            if (!isShaderGraph)
            {
                // TODO: This should be moved outside of the BaseShaderGUI class if it is not generic
                // Temporary fix for lightmapping. TODO: to be replaced with attribute tag.
                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", material.GetTexture("_BaseMap"));
                    material.SetTextureScale("_MainTex", material.GetTextureScale("_BaseMap"));
                    material.SetTextureOffset("_MainTex", material.GetTextureOffset("_BaseMap"));
                }
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", material.GetColor("_BaseColor"));
            }

            // Emission
            if (material.HasProperty(Property.EmissionColor))
                MaterialEditor.FixupEmissiveFlag(material);

            bool shouldEmissionBeEnabled =
                (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0;

            // Not sure what this is used for, I don't see this property declared by any Unity shader in our repo...
            // I'm guessing it is some kind of legacy material upgrade support thing?  Or maybe just dead code now...
            if (material.HasProperty("_EmissionEnabled") && !shouldEmissionBeEnabled)
                shouldEmissionBeEnabled = material.GetFloat("_EmissionEnabled") >= 0.5f;

            CoreUtils.SetKeyword(material, Keyword.HW_Emission, shouldEmissionBeEnabled);

            // Normal Map
            if (material.HasProperty("_BumpMap"))
                CoreUtils.SetKeyword(material, Keyword.HW_NormalMap, material.GetTexture("_BumpMap"));

            // Shader specific keyword functions
            shadingModelFunc?.Invoke(material);
            shaderFunc?.Invoke(material);
        }

        public static void SetMaterialSrcDstBlendProperties(Material material, bool isShaderGraph, UnityEngine.Rendering.BlendMode srcBlend, UnityEngine.Rendering.BlendMode dstBlend)
        {
            var srcBlendProp = Property.SrcBlend(isShaderGraph);
            if (material.HasProperty(srcBlendProp))
                material.SetFloat(srcBlendProp, (float)srcBlend);

            var dstBlendProp = Property.DstBlend(isShaderGraph);
            if (material.HasProperty(dstBlendProp))
                material.SetFloat(dstBlendProp, (float)dstBlend);
        }

        public static void SetMaterialZWriteProperty(Material material, bool zwriteEnabled)
        {
            bool isShaderGraph = material.IsShaderGraph();
            var zwriteProp = Property.ZWrite(isShaderGraph);

            if (material.HasProperty(zwriteProp))
                material.SetFloat(zwriteProp, zwriteEnabled ? 1.0f : 0.0f);
        }

        public static void SetupMaterialBlendMode(Material material)
        {
            if (material == null)
                throw new ArgumentNullException("material");

            bool isShaderGraph = material.IsShaderGraph();

            bool alphaClip = false;
            var alphaClipProp = Property.AlphaClip(isShaderGraph);
            if (material.HasProperty(alphaClipProp))
                alphaClip = material.GetFloat(alphaClipProp) >= 0.5;

            CoreUtils.SetKeyword(material, Keyword.HW_AlphaTestOn, alphaClip);

            var queueOffsetProp = Property.QueueOffset(isShaderGraph);
            var surfaceProp = Property.Surface(isShaderGraph);
            if (material.HasProperty(surfaceProp))
            {
                SurfaceType surfaceType = (SurfaceType)material.GetFloat(surfaceProp);
                CoreUtils.SetKeyword(material, Keyword.HW_SurfaceTypeTransparent, surfaceType == SurfaceType.Transparent);
                if (surfaceType == SurfaceType.Opaque)
                {
                    int renderQueue;
                    if (alphaClip)
                    {
                        renderQueue = (int)RenderQueue.AlphaTest;
                        material.SetOverrideTag("RenderType", "TransparentCutout");
                    }
                    else
                    {
                        renderQueue = (int)RenderQueue.Geometry;
                        material.SetOverrideTag("RenderType", "Opaque");
                    }

                    if (material.HasProperty(queueOffsetProp))
                        renderQueue += (int)material.GetFloat(queueOffsetProp);

                    material.renderQueue = renderQueue;
                    SetMaterialSrcDstBlendProperties(material, isShaderGraph, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.Zero);
                    SetMaterialZWriteProperty(material, true);
                    material.DisableKeyword(Keyword.HW_AlphaPremultiplyOn);
                    material.SetShaderPassEnabled("ShadowCaster", true);
                }
                else // SurfaceType Transparent
                {
                    var blendProp = Property.Blend(isShaderGraph);
                    BlendMode blendMode = (BlendMode)material.GetFloat(blendProp);

                    // Specific Transparent Mode Settings
                    switch (blendMode)
                    {
                        case BlendMode.Alpha:
                            SetMaterialSrcDstBlendProperties(material, isShaderGraph,
                                UnityEngine.Rendering.BlendMode.SrcAlpha,
                                UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                            material.DisableKeyword(Keyword.HW_AlphaPremultiplyOn);
                            break;
                        case BlendMode.Premultiply:
                            SetMaterialSrcDstBlendProperties(material, isShaderGraph,
                                UnityEngine.Rendering.BlendMode.One,
                                UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                            material.EnableKeyword(Keyword.HW_AlphaPremultiplyOn);
                            break;
                        case BlendMode.Additive:
                            SetMaterialSrcDstBlendProperties(material, isShaderGraph,
                                UnityEngine.Rendering.BlendMode.SrcAlpha,
                                UnityEngine.Rendering.BlendMode.One);
                            material.DisableKeyword(Keyword.HW_AlphaPremultiplyOn);
                            break;
                        case BlendMode.Multiply:
                            SetMaterialSrcDstBlendProperties(material, isShaderGraph,
                                UnityEngine.Rendering.BlendMode.DstColor,
                                UnityEngine.Rendering.BlendMode.Zero);
                            material.DisableKeyword(Keyword.HW_AlphaPremultiplyOn);
                            material.EnableKeyword(Keyword.HW_AlphaModulateOn);
                            break;
                    }

                    // General Transparent Material Settings
                    material.SetOverrideTag("RenderType", "Transparent");
                    SetMaterialZWriteProperty(material, false);
                    material.renderQueue = (int)RenderQueue.Transparent;
                    material.renderQueue += material.HasProperty(queueOffsetProp) ? (int)material.GetFloat(queueOffsetProp) : 0;
                    material.SetShaderPassEnabled("ShadowCaster", false);
                }
            }
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            // Clear all keywords for fresh start
            // Note: this will nuke user-selected custom keywords when they change shaders
            material.shaderKeywords = null;

            base.AssignNewShaderToMaterial(material, oldShader, newShader);

            // Setup keywords based on the new shader
            Unity.Rendering.Universal.ShaderUtils.ResetMaterialKeywords(material);
        }

        #endregion
        ////////////////////////////////////
        // Helper Functions               //
        ////////////////////////////////////
        #region HelperFunctions

        public static void TwoFloatSingleLine(GUIContent title, MaterialProperty prop1, GUIContent prop1Label,
            MaterialProperty prop2, GUIContent prop2Label, MaterialEditor materialEditor, float labelWidth = 30f)
        {
            const int kInterFieldPadding = 2;

            Rect rect = EditorGUILayout.GetControlRect();
            EditorGUI.PrefixLabel(rect, title);

            var indent = EditorGUI.indentLevel;
            var preLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = labelWidth;

            Rect propRect1 = new Rect(rect.x + preLabelWidth, rect.y,
                (rect.width - preLabelWidth) * 0.5f - 1, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = prop1.hasMixedValue;
            var prop1val = EditorGUI.FloatField(propRect1, prop1Label, prop1.floatValue);
            if (EditorGUI.EndChangeCheck())
                prop1.floatValue = prop1val;

            Rect propRect2 = new Rect(propRect1.x + propRect1.width + kInterFieldPadding, rect.y,
                propRect1.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = prop2.hasMixedValue;
            var prop2val = EditorGUI.FloatField(propRect2, prop2Label, prop2.floatValue);
            if (EditorGUI.EndChangeCheck())
                prop2.floatValue = prop2val;

            EditorGUI.indentLevel = indent;
            EditorGUIUtility.labelWidth = preLabelWidth;

            EditorGUI.showMixedValue = false;
        }

        public void DoPopup(GUIContent label, MaterialProperty property, string[] options)
        {
            materialEditor.PopupShaderProperty(property, label, options);
        }

        // Helper to show texture and color properties
        public static Rect TextureColorProps(MaterialEditor materialEditor, GUIContent label, MaterialProperty textureProp, MaterialProperty colorProp, bool hdr = false)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            EditorGUI.showMixedValue = textureProp.hasMixedValue;
            materialEditor.TexturePropertyMiniThumbnail(rect, textureProp, label.text, label.tooltip);
            EditorGUI.showMixedValue = false;

            if (colorProp != null)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = colorProp.hasMixedValue;
                int indentLevel = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                Rect rectAfterLabel = new Rect(rect.x + EditorGUIUtility.labelWidth, rect.y,
                    EditorGUIUtility.fieldWidth, EditorGUIUtility.singleLineHeight);
                var col = EditorGUI.ColorField(rectAfterLabel, GUIContent.none, colorProp.colorValue, true,
                    false, hdr);
                EditorGUI.indentLevel = indentLevel;
                if (EditorGUI.EndChangeCheck())
                {
                    materialEditor.RegisterPropertyChangeUndo(colorProp.displayName);
                    colorProp.colorValue = col;
                }
                EditorGUI.showMixedValue = false;
            }

            return rect;
        }

        // Copied from shaderGUI as it is a protected function in an abstract class, unavailable to others
        public new static MaterialProperty FindProperty(string propertyName, MaterialProperty[] properties)
        {
            return FindProperty(propertyName, properties, true);
        }

        // Copied from shaderGUI as it is a protected function in an abstract class, unavailable to others
        public new static MaterialProperty FindProperty(string propertyName, MaterialProperty[] properties, bool propertyIsMandatory)
        {
            for (int index = 0; index < properties.Length; ++index)
            {
                if (properties[index] != null && properties[index].name == propertyName)
                    return properties[index];
            }
            if (propertyIsMandatory)
                throw new ArgumentException("Could not find MaterialProperty: '" + propertyName + "', Num properties: " + (object)properties.Length);
            return null;
        }

        #endregion
    }
}

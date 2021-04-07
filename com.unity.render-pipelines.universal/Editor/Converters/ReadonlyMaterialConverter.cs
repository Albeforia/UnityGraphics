﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEditor.SceneManagement;
using UnityEditor.Search;
using UnityEditor.Search.Providers;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Editor.Converters
{
    enum IdentifierType { kNullIdentifier = 0, kImportedAsset = 1, kSceneObject = 2, kSourceAsset = 3, kBuiltInAsset = 4 };

    public static class ReadonlyMaterialMap
    {
        public static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            {"Default-Diffuse", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat"},
            {"Default-Material", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat"},
            {"Default-ParticleSystem", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat"},
            {"Default-Particle", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat"},
            {"Default-Terrain-Diffuse", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat"},
            {"Default-Terrain-Specular", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat"},
            {"Default-Terrain-Standard", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat"},
            {"Sprites-Default", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat"},
            {"Sprites-Mask", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat"},

            // TODO: Replace these with something more appropriate
            {"Default UI Material", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat"},
            {"ETC1 Supported UI Material", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat"},
            {"Default-Line", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat"},
            {"Default-Skybox", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat"},
            {"SpatialMappingOcclusion", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat"},
            {"SpatialMappingWireframe", "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat"},
        };
    }

    public class ReadonlyMaterialConverter : RenderPipelineConverter
    {
        public override string name => "Readonly Material Converter";
        public override string info => "Converts references to Built-In readonly materials to URP readonly materials";
        public override Type conversion => typeof(BuiltInToURPConversion);

        private bool _startingSceneIsClosed;

        public override void OnInitialize(InitializeConverterContext ctx)
        {
            using var context = SearchService.CreateContext("asset", "urp:convert");
            using var request = SearchService.Request(context);
            {
                // we're going to do this step twice in order to get them ordered, but it should be fast
                var orderedRequest = request.OrderBy(req =>
                    {
                        GlobalObjectId.TryParse(req.id, out var gid);
                        return gid.assetGUID;
                    })
                    .ToList();

                foreach (var r in orderedRequest)
                {
                    if (r == null || !GlobalObjectId.TryParse(r.id, out var gid))
                    {
                        continue;
                    }

                    var label = r.provider.fetchLabel(r, r.context);
                    var description = r.provider.fetchDescription(r, r.context);

                    var item = new ConverterItemDescriptor()
                    {
                        name = $"{label} : {description}",
                        path = gid.ToString(),
                    };

                    ctx.AddAssetToConvert(item);
                }
            }
        }

        public override void OnRun(RunConverterContext ctx)
        {
            // order by the assetGuid so that they run in order
            var items = ctx.items.ToList();

            foreach (var (index, obj) in EnumerateObjects(items, ctx).Where(item => item != null))
            {
                var materials = MaterialReferenceBuilder.GetMaterialsFromObject(obj);

                var result = true;
                var errorString = new StringBuilder();
                foreach (var material in materials)
                {
                    // there might be multiple materials on this object, we only care about the ones we explicitly try to remap that fail
                    if (!MaterialReferenceBuilder.GetIsReadonlyMaterial(material)) continue;
                    if (!ReadonlyMaterialMap.Map.ContainsKey(material.name)) continue;
                    if (!ReAssignMaterial(obj, material.name, ReadonlyMaterialMap.Map[material.name]))
                    {
                        result = false;
                        errorString.AppendLine($"Material {material.name} failed to be reassigned");
                    }
                }

                if (result)
                {
                    ctx.MarkSuccessful(index);
                }
                else
                {
                    ctx.MarkFailed(index, errorString.ToString());
                }
            }
        }

        private static bool ReAssignMaterial(Object obj, string oldMaterialName, string newMaterialPath)
        {
            var result = false;

            // do the reflection to make sure we get the right material reference
            if (obj is GameObject go)
            {
                foreach (var key in MaterialReferenceBuilder.GetComponentTypes())
                {
                    var components = go.GetComponentsInChildren(key);
                    foreach (var component in components)
                    {
                        result = ReassignMaterialOnComponentOrObject(component,
                            oldMaterialName,
                            newMaterialPath,
                            result);
                    }
                }
            }
            else
            {
                result = ReassignMaterialOnComponentOrObject(obj,
                    oldMaterialName,
                    newMaterialPath);
            }

            return result;
        }

        private static bool ReassignMaterialOnComponentOrObject(Object obj,
            string oldMaterialName,
            string newMaterialPath,
            bool result = false)
        {
            var materialProperties = obj.GetType().GetMaterialPropertiesWithoutLeaking();

            foreach (var property in materialProperties)
            {
                var material = property.GetGetMethod().Invoke(obj, null) as Material;
                if (material != null && material.name.Equals(oldMaterialName, StringComparison.OrdinalIgnoreCase))
                {
                    var newMaterial = AssetDatabase.LoadAssetAtPath<Material>(newMaterialPath);

                    if (newMaterial != null)
                    {
                        var setMethod = property.GetSetMethod();

                        if (setMethod != null)
                        {
                            setMethod.Invoke(obj, new object[] { newMaterial });
                            result = true;
                        }
                    }
                }
            }

            return result;
        }

        private IEnumerable<Tuple<int,Object>> EnumerateObjects(IReadOnlyList<ConverterItemInfo> items, RunConverterContext ctx)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];

                if (GlobalObjectId.TryParse(item.descriptor.path, out var gid))
                {
                    var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                    if (!obj)
                    {
                        // Open container scene
                        if (gid.identifierType == (int)IdentifierType.kSceneObject)
                        {
                            // Before we open a new scene, we need to save.
                            // However, we shouldn't save the first scene.
                            // Todo: This should probably be expanded to the context of all converters. This is an example for now.
                            if (_startingSceneIsClosed)
                            {
                                var currentScene = SceneManager.GetActiveScene();
                                EditorSceneManager.SaveScene(currentScene);
                            }

                            _startingSceneIsClosed = true;

                            var containerPath = AssetDatabase.GUIDToAssetPath(gid.assetGUID);

                            var mainInstanceID = AssetDatabase.LoadAssetAtPath<Object>(containerPath);
                            AssetDatabase.OpenAsset(mainInstanceID);
                            yield return null;

                            var scene = SceneManager.GetActiveScene();
                            while (!scene.isLoaded)
                            {
                                yield return null;
                            }
                        }

                        // Reload object
                        obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                        if (!obj)
                        {
                            ctx.MarkFailed(i, $"Object {gid.assetGUID} failed to load...");
                            continue;
                        }
                    }

                    yield return new Tuple<int, Object>(i, obj);
                }
                else
                {
                    ctx.MarkFailed(i, $"Failed to parse Global ID {item.descriptor.path}...");
                }
            }
        }
    }
}

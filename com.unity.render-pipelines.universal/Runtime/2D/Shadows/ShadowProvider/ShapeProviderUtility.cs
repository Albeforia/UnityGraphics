using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace UnityEngine.Rendering.Universal
{
    internal class ShapeProviderUtility
    {
        static public void CallOnBeforeRender(Component component, ShadowMesh2D shadowMesh, Matrix4x4 cameraLightFrustum)
        {
            if (component != null  && component.TryGetComponent<IShadowShape2DProvider>(out var shapeProvider))
            {
                shapeProvider.OnBeforeRender(shadowMesh, cameraLightFrustum);
            }
            else if (shadowMesh != null && shadowMesh.mesh != null)
            {
                shadowMesh.mesh.Clear();
            }
        }

        static public void PersistantDataCreated(Component component, ShadowMesh2D shadowMesh)
        {
            if (component != null && component.TryGetComponent<IShadowShape2DProvider>(out var shapeProvider))
            {
                shapeProvider.OnPersistantDataCreated(shadowMesh);
            }
        }

        static public List<Component> GetShadowShapeProviders(GameObject go)
        {
            Component[] components = go.GetComponents<Component>();

            List<Component> retList = new List<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (components[i] as IShadowShape2DProvider != null)
                {
                    retList.Add(component);
                }
            }

            return retList;
        }

        static public Component GetDefaultShadowCastingSource(GameObject go)
        {
            Component[] components = go.GetComponents<Component>();

            Component defaultComponent = null;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (components[i] as IShadowShape2DProvider != null)
                {
                    if (component as Renderer) // There can only be one renderer
                        defaultComponent = component;
                    else if (defaultComponent == null) // Renderer takes priority
                        defaultComponent = component;
                }
            }

            return defaultComponent;
        }
    }
}

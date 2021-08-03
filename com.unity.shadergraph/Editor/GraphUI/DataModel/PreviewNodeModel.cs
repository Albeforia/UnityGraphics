using UnityEditor.GraphToolsFoundation.Overdrive;
using UnityEditor.GraphToolsFoundation.Overdrive.BasicModel;
using UnityEditor.ShaderGraph.GraphUI.DataModel;
using UnityEditor.ShaderGraph.Registry;
using UnityEngine;

namespace UnityEditor.ShaderGraph.GraphUI
{
    public class PreviewNodeModel : NodeModel
    {
        [SerializeField]
        RegistryKey m_RegistryKey;

        /// <summary>
        /// The registry key used to look up this node's topology. Must be set before DefineNode is called.
        ///
        /// RegistryNodeSearcherItem sets this in an initialization callback, and the extension method
        /// GraphModel.CreateRegistryNode also handles assigning it.
        /// </summary>
        public RegistryKey registryKey
        {
            get => m_RegistryKey;
            set => m_RegistryKey = value;
        }

        protected override void OnDefineNode()
        {
            base.OnDefineNode();

            var stencil = (ShaderGraphStencil) GraphModel.Stencil;
            var registry = stencil.GetRegistry();
            var reader = registry.GetDefaultTopology(registryKey);

            if (reader == null) return;
            foreach (var portReader in reader.GetPorts())
            {
                AddPortFromReader(portReader);
            }
        }

        void AddPortFromReader(GraphDelta.IPortReader portReader)
        {
            var isInput = portReader.GetFlags().isInput;
            var orientation = portReader.GetFlags().isHorizontal
                ? PortOrientation.Horizontal
                : PortOrientation.Vertical;

            var type = ShaderGraphTypes.GetTypeHandleFromKey(portReader.GetRegistryKey());

            if (isInput)
                this.AddDataInputPort(portReader.GetName(), type, orientation: orientation);
            else
                this.AddDataOutputPort(portReader.GetName(), type, orientation: orientation);
        }
    }
}

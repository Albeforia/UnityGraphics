using Usage = UnityEditor.ShaderGraph.GraphDelta.GraphType.Usage;

namespace UnityEditor.ShaderGraph.Defs
{
    internal class ComputeWaterVertexPositionNode : IStandardNode
    {
        public static string Name => "ComputeWaterVertexPosition";
        public static int Version => 1;

        public static FunctionDescriptor FunctionDescriptor => new(
            Name,
@"PositionWS = GetWaterVertexPosition(temp);",
            new ParameterDescriptor[]
            {
                new ParameterDescriptor("temp", TYPE.Vec3, Usage.Local, REF.WorldSpace_Position),
                new ParameterDescriptor("PositionWS", TYPE.Vec3, Usage.Out)
            }
        );

        public static NodeUIDescriptor NodeUIDescriptor => new(
            Version,
            Name,
            displayName: "Compute Water Vertex Position",
            tooltip: "",
            category: "Utility/HDRP/Water",
            synonyms: new string[0],
            hasPreview: false,
            parameters: new ParameterUIDescriptor[] {
                new ParameterUIDescriptor(
                    name: "PositionWS",
                    displayName: "PositionWS",
                    tooltip: ""
                )
            }
        );
    }
}


using System.Collections.Generic;
using Usage = UnityEditor.ShaderGraph.GraphDelta.GraphType.Usage;

namespace UnityEditor.ShaderGraph.Defs
{

    internal class Vector4Node : IStandardNode
    {
        public static string Name = "Vector4";
        public static int Version = 1;

        public static FunctionDescriptor FunctionDescriptor => new(
            Version,
            Name,
@"
    Out.x = X;
    Out.y = Y;
    Out.z = Z;
    Out.w = W;
",
            new ParameterDescriptor("X", TYPE.Float, Usage.In),
            new ParameterDescriptor("Y", TYPE.Float, Usage.In),
            new ParameterDescriptor("Z", TYPE.Float, Usage.In),
            new ParameterDescriptor("W", TYPE.Float, Usage.In),
            new ParameterDescriptor("Out", TYPE.Vec4, Usage.Out)
        );

        public static NodeUIDescriptor NodeUIDescriptor => new(
            Version,
            Name,
            displayName: "Vector 4",
            tooltip: "a user-defined value with 4 channels",
            categories: new string[2] { "Input", "Basic" },
            synonyms: new string[4] { "4", "v4", "vec4", "float4" },
            parameters: new ParameterUIDescriptor[5] {
                new ParameterUIDescriptor(
                    name: "X",
                    tooltip: "the first component"
                ),
                new ParameterUIDescriptor(
                    name: "Y",
                    tooltip: "the second component"
                ),
                new ParameterUIDescriptor(
                    name: "Z",
                    tooltip: "the third component"
                ),
                new ParameterUIDescriptor(
                    name: "W",
                    tooltip: "the forth component"
                ),
                new ParameterUIDescriptor(
                    name: "Out",
                    tooltip: "a user-defined value with 4 channels"
                )
            }
        );

        public static Dictionary<string, string> UIStrings => new()
        {
            { "DisplayName", "Vector 4" },
            { "Category", "Input, Basic" },
            { "Name.Synonyms", "4, v4, vec4, float4" },
            { "Tooltip", "a user-defined value with 4 channels" },
            { "Parameters.X.Tooltip", "the first component" },
            { "Parameters.Y.Tooltip", "the second component" },
            { "Parameters.Z.Tooltip", "the third component" },
            { "Parameters.W.Tooltip", "the forth component" },
            { "Parameters.Out.Tooltip", "a user-defined value with 4 channels" }
        };
    }
}

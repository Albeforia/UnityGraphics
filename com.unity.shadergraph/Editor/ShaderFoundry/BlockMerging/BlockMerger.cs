using System.Collections.Generic;
using System.Linq;

namespace UnityEditor.ShaderFoundry
{
    internal class BlockMerger
    {
        ShaderContainer container;
        internal ShaderContainer Container => container;

        internal class Context
        {
            internal IEnumerable<BlockLinkInstance> BlockLinkInstances = Enumerable.Empty<BlockLinkInstance>();
            internal IEnumerable<BlockVariable> Inputs = Enumerable.Empty<BlockVariable>();
            internal IEnumerable<BlockVariable> Outputs = Enumerable.Empty<BlockVariable>();
        }

        internal BlockMerger(ShaderContainer container)
        {
            this.container = container;
        }

        internal static string BuildVariableName(Block block, VariableLinkInstance varInstance)
        {
            return $"{block.Name}_{varInstance.Name}";
        }

        VariableLinkInstance FindOrCreateVariableInstance(VariableLinkInstance ownerInstance, VariableLinkInstance variable, string name)
        {
            // If this field already exists on the owner (duplicate blocks) use the existing field, otherwise create a new one
            var matchingField = ownerInstance.FindField(name);
            if (matchingField == null)
            {
                matchingField = ownerInstance.CreateSubField(variable.Type, name, variable.Attributes);
            }
            return matchingField;
        }

        internal VariableLinkInstance FindMatch(ScopeSet scopes, VariableLinkInstance variable, ref string matchingName)
        {
            // Find if there's a matching field using the input name
            var matchingField = scopes.Find(variable.Type, variable.Name);
            if (matchingField != null)
            {
                matchingName = variable.Name;
                return matchingField;
            }
            
            foreach (var aliasName in variable.Aliases)
            {
                matchingField = scopes.Find(variable.Type, aliasName);

                if (matchingField != null)
                {
                    matchingName = aliasName;
                    break;
                }
            }
            return matchingField;
        }

        internal void LinkBlockInputs(ScopeSet scopes, BlockLinkInstance mergedBlockLinkInstance, BlockLinkInstance blockInstance)
        {
            var block = blockInstance.Block;
            var mergedInputInstance = mergedBlockLinkInstance.InputInstance;
            var blockInputInstance = blockInstance.InputInstance;
            // Try matching all input fields
            foreach (var input in blockInputInstance.Fields)
            {
                var inputName = input.Name;
                bool isProperty = input.IsProperty;
                VariableLinkInstance matchingField = null;

                // If this isn't a property, always check for a match from the available outputs
                if(!isProperty)
                    matchingField = FindMatch(scopes, input, ref inputName);

                // If this is a property or we didn't find a matching field, promote this variable to an input
                bool createNewVariable = isProperty == true || (matchingField == null);

                // If we need to create a new variable on the merged block to link to
                if (createNewVariable)
                {
                    // If the field is a property then use the original name, otherwise build a unique one
                    var newVariableName = input.Name;
                    if (!isProperty)
                        newVariableName = BuildVariableName(block, input);

                    // Make sure a field exists on the merged input instance
                    matchingField = FindOrCreateVariableInstance(mergedInputInstance, input, newVariableName);

                    // Propagate the alias up to the new variable. This alias is used to know know how the linking is supposed to work at subsequent merges.
                    // Note: Don't do this if this is a property as the alias would provide no new information
                    if (!isProperty)
                        matchingField.AddAlias(inputName);
                }

                // Mark both fields as being used and then hook up the source
                matchingField.IsUsed = true;
                input.IsUsed = true;
                input.SetSource(matchingField);
            }
        }

        internal void LinkBlockOutputs(ScopeSet scopes, BlockLinkInstance mergedBlockLinkInstance, BlockLinkInstance blockInstance)
        {
            var block = blockInstance.Block;
            var mergedOutputInstance = mergedBlockLinkInstance.OutputInstance;
            var blockOutputInstance = blockInstance.OutputInstance;
            foreach (var output in blockOutputInstance.Fields)
            {
                // Always hookup the output for future inputs to link to
                scopes.Set(output, output.Name);

                // Always create an output on the merged block since anyone could use the output later.
                var newOutputName = BuildVariableName(block, output);
                var availableOutput = FindOrCreateVariableInstance(mergedOutputInstance, output, newOutputName);
                // Link the new output to the block's output. This variable is always used.
                availableOutput.SetSource(output);
                availableOutput.IsUsed = true;

                // Propagate the original name and aliases onto the new variable (for recursive merging).
                // Also add lookup entries for each alias on the original output.
                availableOutput.AddAlias(output.Name);
                foreach (var alias in output.Aliases)
                {
                    availableOutput.AddAlias(alias);
                    scopes.Set(output, alias);
                }
            }
        }

        internal void LinkBlockFields(ScopeSet scopes, BlockLinkInstance mergedBlockLinkInstance, BlockLinkInstance blockLinkInstance)
        {
            LinkBlockInputs(scopes, mergedBlockLinkInstance, blockLinkInstance);
            LinkBlockOutputs(scopes, mergedBlockLinkInstance, blockLinkInstance);
        }

        internal void SetupInputs(ScopeSet scopes, IEnumerable<BlockVariable> inputs, BlockLinkInstance mergedBlockLinkInstance)
        {
            // Add all available inputs to the input struct
            var mergedInputInstance = mergedBlockLinkInstance.InputInstance;
            foreach (var input in inputs)
            {
                var inputInstance = mergedInputInstance.CreateSubField(input.Type, input.ReferenceName, input.Attributes);
                scopes.Set(inputInstance);
            }
        }

        internal void LinkFinalOutputs(ScopeSet scopes, IEnumerable<BlockVariable> outputs, BlockLinkInstance mergedBlockLinkInstance)
        {
            var mergedOutputInstance = mergedBlockLinkInstance.OutputInstance;
            // For all available outputs, create the output field and find out who writes out to this last
            foreach (var output in outputs)
            {
                var instance = mergedOutputInstance.CreateSubField(output.Type, output.ReferenceName, output.Attributes);
                // Record if someone writes out to this output
                var matchingField = scopes.Find(output.Type, output.ReferenceName);
                if (matchingField != null)
                {
                    instance.SetSource(matchingField);
                    instance.IsUsed = true;
                }
            }
        }

        internal BlockLinkInstance Link(Context context)
        {
            var scopes = new ScopeSet();
            var mergedBlockLinkInstance = new BlockLinkInstance(Container);
            SetupInputs(scopes, context.Inputs, mergedBlockLinkInstance);

            foreach (var blockLinkInstance in context.BlockLinkInstances)
                LinkBlockFields(scopes, mergedBlockLinkInstance, blockLinkInstance);

            LinkFinalOutputs(scopes, context.Outputs, mergedBlockLinkInstance);
            return mergedBlockLinkInstance;
        }
    }
}


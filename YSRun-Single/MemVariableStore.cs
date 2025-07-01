using System;
using System.Collections.Generic;
using Yarn;

namespace YSRunSingle
{
    #nullable enable
    
    /// <summary>
    /// A concrete implementation of IVariableStorage that keeps all
    /// variables in memory.
    /// This is an adaptation of Yarn.MemoryVariableStore which permits
    /// JSON serialization.
    /// </summary>
    public class MemVariableStore : IVariableStorage
    {
        private readonly Dictionary<string, object> variables = new Dictionary<string, object>();

        private static bool TryGetAsType<T>(Dictionary<string, object> dictionary, string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? result)
        {
            if (dictionary.TryGetValue(key, out var objectResult) == true
                && typeof(T).IsAssignableFrom(objectResult.GetType()))
            {
                result = (T)objectResult;
                return true;
            }

            result = default!;
            return false;
        }

        /// <inheritdoc/>
        public Program? Program { get; set; }

        /// <inheritdoc/>
        public ISmartVariableEvaluator? SmartVariableEvaluator { get; set; }

        /// <inheritdoc/>
        public virtual bool TryGetValue<T>(string variableName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? result)
        {
            if (Program == null)
            {
                throw new InvalidOperationException($"Can't get variable {variableName}: {nameof(Program)} is null");
            }

            switch (GetVariableKind(variableName))
            {
                case VariableKind.Stored:
                    // This is a stored value. First, attempt to fetch it from the
                    // variable storage.

                    // Try to get the value from the dictionary, and check to see that it's the 
                    if (TryGetAsType(variables, variableName, out result))
                    {
                        // We successfully fetched it from storage.
                        return true;
                    }
                    else
                    {
                        // We didn't fetch it from storage. Fall back to the
                        // program's initial value storage.
                        return Program.TryGetInitialValue(variableName, out result);
                    }
                case VariableKind.Smart:
                    // The variable is a smart variable. Ask our smart variable
                    // evaluator.
                    if (SmartVariableEvaluator == null)
                    {
                        throw new InvalidOperationException($"Can't get variable {variableName}: {nameof(SmartVariableEvaluator)} is null");
                    }
                    return this.SmartVariableEvaluator.TryGetSmartVariable(variableName, out result);
                case VariableKind.Unknown:
                default:
                    // The variable is not known.
                    result = default;
                    return false;
            }
        }

        /// <inheritdoc/>
        public void Clear()
        {
            this.variables.Clear();
        }

        /// <inheritdoc/>
        public virtual void SetValue(string variableName, string stringValue)
        {
            this.variables[variableName] = stringValue;
        }

        /// <inheritdoc/>
        public virtual void SetValue(string variableName, float floatValue)
        {
            this.variables[variableName] = floatValue;
        }

        /// <inheritdoc/>
        public virtual void SetValue(string variableName, bool boolValue)
        {
            this.variables[variableName] = boolValue;
        }

        /// <inheritdoc/>
        public VariableKind GetVariableKind(string name)
        {
            // Does this variable exist in our stored values?
            if (this.variables.ContainsKey(name))
            {
                return VariableKind.Stored;
            }
            if (this.Program == null)
            {
                // We don't have a Program, so we can't ask it for other
                // information.
                return VariableKind.Unknown;
            }
            // Ask our Program about it. It will be able to tell if the variable
            // is stored, smart, or unknown.
            return this.Program.GetVariableKind(name);
        }
    }
}

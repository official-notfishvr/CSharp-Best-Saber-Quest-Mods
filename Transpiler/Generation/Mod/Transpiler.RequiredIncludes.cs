using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Transpiler;

internal sealed partial class Transpiler
{
    private IEnumerable<string> CollectBodyIncludes(MethodDefinition method)
    {
        if (!method.HasBody || method.Body == null)
            yield break;

        foreach (var variable in method.Body.Variables)
        {
            if (_typeSystem.GetIncludePath(variable.VariableType) is { } variableInclude)
                yield return variableInclude;
        }

        foreach (var instruction in method.Body.Instructions)
        {
            switch (instruction.Operand)
            {
                case MethodReference referencedMethod:
                    if (IsTranspilerOwnedType(referencedMethod.DeclaringType))
                        break;

                    if (_typeSystem.GetIncludePath(referencedMethod.DeclaringType) is { } methodDeclaringTypeInclude)
                        yield return methodDeclaringTypeInclude;

                    if (_typeSystem.GetIncludePath(referencedMethod.ReturnType) is { } methodReturnTypeInclude)
                        yield return methodReturnTypeInclude;

                    foreach (var parameter in referencedMethod.Parameters)
                    {
                        if (_typeSystem.GetIncludePath(parameter.ParameterType) is { } methodParameterInclude)
                            yield return methodParameterInclude;
                    }

                    break;

                case FieldReference referencedField:
                    if (IsTranspilerOwnedType(referencedField.DeclaringType))
                        break;

                    if (_typeSystem.GetIncludePath(referencedField.DeclaringType) is { } fieldDeclaringTypeInclude)
                        yield return fieldDeclaringTypeInclude;

                    if (_typeSystem.GetIncludePath(referencedField.FieldType) is { } fieldTypeInclude)
                        yield return fieldTypeInclude;

                    break;
            }
        }
    }

    private IEnumerable<string> CollectCurrentModuleIncludePaths()
    {
        if (_module == null)
            yield break;

        var pending = new Stack<TypeDefinition>(_module.Types.Where(IsTranspilerRelevantType).Reverse());
        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (_typeSystem.GetIncludePath(type) is { } include)
                yield return include;

            for (var i = type.NestedTypes.Count - 1; i >= 0; i--)
                pending.Push(type.NestedTypes[i]);
        }
    }

    private bool IsTranspilerOwnedType(TypeReference? type)
    {
        var resolved = type?.Resolve();
        return resolved != null && IsTranspilerRelevantType(resolved);
    }
}

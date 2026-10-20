using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Transpiler;

internal sealed partial class Transpiler
{
    private void LoadHelperMethods(TypeDefinition type)
    {
        if (!type.Methods.Any(method => method.CustomAttributes.Any(IsPatchAttribute) || method.CustomAttributes.Any(IsMenuButtonAttribute) || method.CustomAttributes.Any(IsGameplaySetupTabAttribute)))
            return;

        foreach (var method in type.Methods)
        {
            if (!method.IsStatic || !method.HasBody || method.IsConstructor || method.IsGetter || method.IsSetter)
                continue;

            if (method.CustomAttributes.Any(IsPatchAttribute))
                continue;

            AddHelperMethod(method);
        }
    }

    private void AddHelperMethod(MethodDefinition method)
    {
        if (_helperMethods.Contains(method))
            return;

        _helperMethods.Add(method);
    }
}

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
    private void LoadPatches(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            var patchAttribute = method.CustomAttributes.FirstOrDefault(IsPatchAttribute);
            if (patchAttribute == null)
                continue;

            if (!method.IsStatic)
                throw new InvalidOperationException($"Patch methods must be static: {method.FullName}");

            if (method.Parameters.Count == 0)
                throw new InvalidOperationException($"Patch methods must have at least the self parameter: {method.FullName}");

            var targetType = ReadHookTargetType(patchAttribute) ?? method.Parameters[0].ParameterType;
            var targetMethod = ReadHookMethodName(patchAttribute) ?? method.Name;
            var isConstructor = string.Equals(targetMethod, ".ctor", StringComparison.Ordinal);
            if (isConstructor)
                targetMethod = ".ctor";
            var phase = ReadPatchKind(patchAttribute);
            if (phase == PatchBehavior.Prefix && method.ReturnType.FullName is not ("System.Void" or "System.Boolean"))
                throw new InvalidOperationException($"Prefix hooks must return void or bool (true continues to the original, false skips it): {method.FullName}");

            if (phase == PatchBehavior.Postfix && method.ReturnType.FullName != "System.Void")
                throw new InvalidOperationException($"Postfix hooks must return void: {method.FullName}");

            var hookName = BuildHookName(targetType, targetMethod, method);

            _patches.Add(
                new PatchDefinition
                {
                    HookName = hookName,
                    TargetMethod = targetMethod,
                    TargetType = targetType,
                    Method = method,
                    IsConstructor = isConstructor,
                    Phase = phase,
                }
            );
        }
    }
}

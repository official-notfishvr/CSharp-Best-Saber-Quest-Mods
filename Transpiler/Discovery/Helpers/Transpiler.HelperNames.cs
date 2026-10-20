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
    private static string GetHelperFunctionName(PatchDefinition hook)
    {
        return CppIdentifier.Sanitize(hook.Method.Name, "HookHelper");
    }

    private static string GetHelperFunctionName(MethodDefinition method)
    {
        var typeToken = CppIdentifier.Sanitize(method.DeclaringType.FullName, "Type");
        var methodToken = CppIdentifier.Sanitize(method.Name, "Helper");
        var signatureToken = BuildSignatureToken(method.Parameters.Select(parameter => parameter.ParameterType));
        return signatureToken.Length == 0 ? $"{typeToken}_{methodToken}" : $"{typeToken}_{methodToken}_{signatureToken}";
    }
}

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
    private static string MapTargetMethodName(PatchDefinition hook)
    {
        return hook.IsConstructor ? "_ctor" : hook.TargetMethod;
    }

    private static string BuildHookName(TypeReference targetType, string targetMethod, MethodDefinition hookMethod)
    {
        var typeToken = CppIdentifier.Sanitize(targetType.FullName, "Type");
        var methodToken = CppIdentifier.Sanitize(targetMethod, "Method");
        var signatureToken = BuildSignatureToken(hookMethod.Parameters.Select(parameter => parameter.ParameterType));

        return signatureToken.Length == 0 ? $"{typeToken}_{methodToken}_Hook" : $"{typeToken}_{methodToken}_{signatureToken}_Hook";
    }

    private static string BuildSignatureToken(IEnumerable<TypeReference> parameters)
    {
        var tokens = parameters.Select(parameter => CppIdentifier.Sanitize(parameter.FullName, "Arg")).Where(token => !string.IsNullOrWhiteSpace(token)).ToArray();

        return tokens.Length == 0 ? "" : string.Join("_", tokens);
    }
}

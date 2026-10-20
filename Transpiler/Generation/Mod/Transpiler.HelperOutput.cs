using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Transpiler;

internal sealed partial class Transpiler
{
    private void WriteHelperFunction(CppCodeWriter writer, HelperMethodEmission helperMethod)
    {
        var returnType = _typeSystem.MapType(helperMethod.Method.ReturnType);
        var parameters = helperMethod.Method.Parameters.Select(parameter => $"{_typeSystem.MapType(parameter.ParameterType)} {CppIdentifier.Sanitize(parameter.Name)}").ToList();
        writer.WriteLine($"static {returnType} {helperMethod.FunctionName}({string.Join(", ", parameters)}) {{");
        foreach (var line in helperMethod.Body.Statements)
            writer.WriteLine($"    {line}");
        writer.WriteLine("}");
        writer.WriteLine();
    }
}

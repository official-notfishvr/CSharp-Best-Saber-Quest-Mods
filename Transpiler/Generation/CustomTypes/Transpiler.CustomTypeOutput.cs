using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace Transpiler;

internal sealed partial class Transpiler
{
    private void WriteCustomTypes(CppCodeWriter writer, IReadOnlyList<CustomTypeMethodEmission> methodEmissions)
    {
        foreach (var customType in _customTypes)
        {
            var fieldDefaults = customType.Fields.Where(field => field.DefaultValueCpp != null).ToList();
            writer.WriteLine($"DECLARE_CLASS_CODEGEN_DLL({customType.CppNamespace}, {customType.CppName}, {customType.BaseCppType}, \"{customType.DllName}\") {{");
            writer.WriteLine(fieldDefaults.Count == 0 ? "    DECLARE_DEFAULT_CTOR();" : "    DECLARE_CTOR(__ctor);");
            foreach (var field in customType.Fields)
                writer.WriteLine($"    DECLARE_INSTANCE_FIELD({field.CppType}, {field.Name});");
            foreach (var method in customType.Methods)
                writer.WriteLine($"    DECLARE_INSTANCE_METHOD({_typeSystem.MapType(method.Method.ReturnType)}, {method.CppName}{BuildCustomTypeMethodDeclarationSuffix(method.Method)});");
            writer.WriteLine("};");
            writer.WriteLine();
            writer.WriteLine($"DEFINE_TYPE({customType.CppNamespace}, {customType.CppName});");
            writer.WriteLine();

            if (fieldDefaults.Count == 0)
                continue;

            var qualifiedType = $"{customType.CppNamespace}::{customType.CppName}";
            writer.WriteLine($"void {qualifiedType}::__ctor() {{");
            writer.WriteLine("    INVOKE_CTOR();");
            writer.WriteLine($"    INVOKE_BASE_CTOR({qualifiedType}::___TypeRegistration::get()->baseType());");
            foreach (var field in fieldDefaults)
                writer.WriteLine($"    {field.Name} = {field.DefaultValueCpp};");
            writer.WriteLine("}");
            writer.WriteLine();
        }

        foreach (var emission in methodEmissions)
            WriteCustomTypeMethod(writer, emission);
    }

    private string BuildCustomTypeMethodDeclarationSuffix(MethodDefinition method)
    {
        if (method.Parameters.Count == 0)
            return "";

        var parameters = method.Parameters.Select(parameter => $"{_typeSystem.MapType(parameter.ParameterType)} {CppIdentifier.Sanitize(parameter.Name, $"arg{parameter.Index}")}");
        return $", {string.Join(", ", parameters)}";
    }

    private void WriteCustomTypeMethod(CppCodeWriter writer, CustomTypeMethodEmission emission)
    {
        var method = emission.Method.Method;
        var returnType = _typeSystem.MapType(method.ReturnType);
        var qualifiedType = $"{emission.CustomType.CppNamespace}::{emission.CustomType.CppName}";
        var parameters = method.Parameters.Select(parameter => $"{_typeSystem.MapType(parameter.ParameterType)} {CppIdentifier.Sanitize(parameter.Name, $"arg{parameter.Index}")}").ToList();

        writer.WriteLine($"{returnType} {qualifiedType}::{emission.Method.CppName}({string.Join(", ", parameters)}) {{");
        foreach (var line in emission.Body.Statements)
            writer.WriteLine($"    {line}");
        writer.WriteLine("}");
        writer.WriteLine();
    }
}

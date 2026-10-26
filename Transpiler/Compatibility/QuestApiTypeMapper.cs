using Mono.Cecil;

namespace Transpiler;

internal static class QuestApiTypeMapper
{
    public static bool QuestReturnMatches(string pcType, string questType) =>
        string.Equals(pcType, questType, StringComparison.Ordinal)
        || questType.StartsWith("::UnityW<", StringComparison.Ordinal) && questType.EndsWith(">", StringComparison.Ordinal) && string.Equals(questType[9..^1], pcType.EndsWith("*", StringComparison.Ordinal) ? pcType[..^1] : pcType, StringComparison.Ordinal);

    public static string? Map(TypeReference type)
    {
        while (type is OptionalModifierType or RequiredModifierType)
            type = ((TypeSpecification)type).ElementType;
        if (type is GenericParameter genericParameter)
            return genericParameter.Name;
        if (type is ByReferenceType byReference)
            return Map(byReference.ElementType) is { } byRefElement ? $"::ByRef<{byRefElement}>" : null;
        if (type is PointerType pointer)
            return Map(pointer.ElementType) is { } pointerElement ? $"{pointerElement}*" : null;
        if (type is ArrayType array)
        {
            if (array.Rank != 1 || Map(array.ElementType) is not { } element)
                return null;
            return $"::ArrayW<{element}, ::Array<{element}>*>";
        }
        if (type is GenericInstanceType generic)
            return MapGeneric(generic);

        var primitive = type.FullName switch
        {
            "System.Void" => "void",
            "System.Boolean" => "bool",
            "System.Byte" => "uint8_t",
            "System.SByte" => "int8_t",
            "System.Int16" => "int16_t",
            "System.UInt16" => "uint16_t",
            "System.Int32" => "int32_t",
            "System.UInt32" => "uint32_t",
            "System.Int64" => "int64_t",
            "System.UInt64" => "uint64_t",
            "System.Single" => "float_t",
            "System.Double" => "double",
            "System.String" => "::StringW",
            "System.Char" => "char16_t",
            "System.Object" => "::System::Object*",
            _ => null,
        };
        if (primitive is not null)
            return primitive;
        if (type.Name.Contains('`'))
            return null;

        TypeDefinition? definition;
        try
        {
            definition = type.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
        catch (ResolutionException)
        {
            return null;
        }
        if (definition is null)
            return null;

        var cppNamespace = string.IsNullOrWhiteSpace(type.Namespace) ? "GlobalNamespace" : string.Join("::", type.Namespace.Split('.'));
        var nestedNames = new Stack<string>();
        TypeReference? current = type;
        while (current is not null)
        {
            if (current.Name.Contains('`'))
                return null;
            nestedNames.Push(CppIdentifier(current.Name));
            current = current.DeclaringType;
        }
        var cppName = string.Join("_", nestedNames);
        return $"::{cppNamespace}::{cppName}{(definition.IsValueType ? "" : "*")}";
    }

    private static string? MapGeneric(GenericInstanceType type)
    {
        if (type.GenericArguments.Count == 0)
            return null;

        var definition = type.ElementType;
        if (!definition.Name.Contains('`') || !int.TryParse(definition.Name.AsSpan(definition.Name.IndexOf('`') + 1), out var arity) || arity != type.GenericArguments.Count)
            return null;

        TypeDefinition? resolved;
        try
        {
            resolved = definition.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
        catch (ResolutionException)
        {
            return null;
        }
        if (resolved is null)
            return null;

        var arguments = new string[type.GenericArguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            var mapped = Map(type.GenericArguments[i]);
            if (mapped is null)
                return null;
            arguments[i] = mapped;
        }

        var cppNamespace = string.IsNullOrWhiteSpace(definition.Namespace) ? "GlobalNamespace" : string.Join("::", definition.Namespace.Split('.'));
        var baseName = definition.Name[..definition.Name.IndexOf('`')];
        var nestedNames = new Stack<string>();
        TypeReference? current = definition;
        while (current is not null)
        {
            if (current.Name.Contains('`'))
            {
                var tick = current.Name.IndexOf('`');
                current = current.DeclaringType;
                if (current is null)
                    nestedNames.Push($"{baseName}_{arity}");
                continue;
            }
            nestedNames.Push(CppIdentifier(current.Name));
            current = current.DeclaringType;
        }
        var cppName = string.Join("_", nestedNames);
        return $"::{cppNamespace}::{cppName}<{string.Join(", ", arguments)}>{(resolved.IsValueType ? "" : "*")}";
    }

    private static string CppIdentifier(string value) => System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9_]", "_");
}

using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Transpiler;

internal sealed class CppTypeSystem
{
    private static readonly Dictionary<string, string> PrimitiveTypes = new(StringComparer.Ordinal)
    {
        ["System.Void"] = "void",
        ["System.Boolean"] = "bool",
        ["System.Byte"] = "uint8_t",
        ["System.SByte"] = "int8_t",
        ["System.Int16"] = "int16_t",
        ["System.UInt16"] = "uint16_t",
        ["System.Int32"] = "int32_t",
        ["System.UInt32"] = "uint32_t",
        ["System.Int64"] = "int64_t",
        ["System.UInt64"] = "uint64_t",
        ["System.Single"] = "float",
        ["System.Double"] = "double",
        ["System.Char"] = "Il2CppChar",
        ["System.String"] = "Il2CppString*",
        ["System.Object"] = "Il2CppObject*",
        ["System.IntPtr"] = "void*",
        ["System.UIntPtr"] = "void*",
    };

    private static readonly HashSet<string> BuiltinNamespaceTypes = new(StringComparer.Ordinal) { "System.String", "System.Object" };

    public string MapType(TypeReference type)
    {
        var normalized = Normalize(type);

        if (PrimitiveTypes.TryGetValue(normalized.FullName, out var primitive))
            return primitive;

        if (normalized is ByReferenceType byReferenceType)
            return $"{MapType(byReferenceType.ElementType)}&";

        if (normalized is PointerType pointerType)
            return $"{MapType(pointerType.ElementType)}*";

        if (normalized is ArrayType arrayType)
            return $"ArrayW<{MapArrayElementTypeName(arrayType.ElementType)}>";

        if (normalized is GenericParameter)
            return "Il2CppObject*";

        var genericInstance = normalized as GenericInstanceType;
        var baseType = genericInstance?.ElementType ?? normalized;
        var cppNamespace = MapNamespace(baseType.Namespace);
        var cppName = ComposeTypeName(baseType);
        var isValueType = normalized.IsValueType || baseType.Resolve()?.IsValueType == true;

        if (genericInstance != null)
        {
            var genericArgs = string.Join(", ", genericInstance.GenericArguments.Select(MapType));
            return $"{cppNamespace}::{cppName}<{genericArgs}>{(isValueType ? "" : "*")}";
        }

        return $"{cppNamespace}::{cppName}{(isValueType ? "" : "*")}";
    }

    public string MapNamespace(string? ns)
    {
        return string.IsNullOrWhiteSpace(ns) ? "GlobalNamespace" : ns.Replace(".", "::", StringComparison.Ordinal);
    }

    public string ComposeTypeName(TypeReference type)
    {
        var names = new Stack<string>();
        TypeReference? current = type;
        while (current != null)
        {
            names.Push(StripArity(current.Name));
            current = current.DeclaringType;
        }

        return string.Join("::", names);
    }

    public string? GetIncludePath(TypeReference? type)
    {
        if (type == null)
            return null;

        var normalized = Normalize(type);
        if (PrimitiveTypes.ContainsKey(normalized.FullName))
            return null;
        if (normalized is GenericParameter)
            return null;

        if (normalized is ByReferenceType byReferenceType)
            return GetIncludePath(byReferenceType.ElementType);

        if (normalized is PointerType pointerType)
            return GetIncludePath(pointerType.ElementType);

        if (normalized is ArrayType arrayType)
            return GetIncludePath(arrayType.ElementType);

        var genericInstance = normalized as GenericInstanceType;
        var baseType = genericInstance?.ElementType ?? normalized;

        if (BuiltinNamespaceTypes.Contains(baseType.FullName))
            return null;

        var ns = string.IsNullOrWhiteSpace(baseType.Namespace) ? "GlobalNamespace" : baseType.Namespace.Replace('.', '/');
        var name = BuildIncludeName(baseType);
        return $"{ns}/{name}.hpp";
    }

    public string GetDefaultValue(TypeReference type)
    {
        var normalized = Normalize(type);

        if (normalized is ByReferenceType)
            return "{}";

        if (normalized.IsValueType || normalized.Resolve()?.IsValueType == true)
        {
            if (normalized.MetadataType == MetadataType.Boolean)
                return "false";

            if (normalized.MetadataType is MetadataType.Byte or MetadataType.SByte or MetadataType.Int16 or MetadataType.UInt16 or MetadataType.Int32 or MetadataType.UInt32 or MetadataType.Int64 or MetadataType.UInt64 or MetadataType.Single or MetadataType.Double or MetadataType.Char)
                return "0";

            return "{}";
        }

        return "nullptr";
    }

    public string MapArrayElementTypeName(TypeReference elementType)
    {
        var elementFullName = Normalize(elementType).FullName;
        return PrimitiveTypes.TryGetValue(elementFullName, out var mapped) ? mapped.TrimEnd('*') : "Il2CppObject*";
    }

    private static TypeReference Normalize(TypeReference type)
    {
        if (type is OptionalModifierType optionalModifierType)
            return Normalize(optionalModifierType.ElementType);
        if (type is RequiredModifierType requiredModifierType)
            return Normalize(requiredModifierType.ElementType);
        return type;
    }

    private static string StripArity(string name)
    {
        var backtickIndex = name.IndexOf('`');
        return backtickIndex >= 0 ? name[..backtickIndex] : name;
    }

    private static string BuildIncludeName(TypeReference type)
    {
        var names = new Stack<string>();
        TypeReference? current = type;
        while (current != null)
        {
            names.Push(StripArity(current.Name));
            current = current.DeclaringType;
        }

        return string.Join("/", names);
    }
}

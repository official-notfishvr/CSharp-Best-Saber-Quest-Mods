using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Transpiler;

internal sealed class TypeMetadataIndex
{
    private readonly Dictionary<string, GeneratedTypeMetadataRecord> _types = new(StringComparer.Ordinal);

    private TypeMetadataIndex() { }

    public static TypeMetadataIndex Load(string assemblyPath)
    {
        var index = new TypeMetadataIndex();
        var metadataPath = FindMetadataPath(assemblyPath);
        if (metadataPath == null)
            return index;

        var json = File.ReadAllText(metadataPath);
        var records = JsonSerializer.Deserialize<List<GeneratedTypeMetadataRecord>>(json) ?? new List<GeneratedTypeMetadataRecord>();
        foreach (var record in records)
        {
            var fullName = string.IsNullOrWhiteSpace(record.Namespace) ? record.TypeName : $"{record.Namespace}.{record.TypeName}";
            index._types[fullName] = record;
        }

        return index;
    }

    public string? ResolvePropertyName(string declaringTypeFullName, string accessorName)
    {
        if (!_types.TryGetValue(declaringTypeFullName, out var type))
            return null;

        if (!accessorName.StartsWith("get_", StringComparison.Ordinal) && !accessorName.StartsWith("set_", StringComparison.Ordinal))
            return null;

        var candidateName = accessorName.Substring(4);
        var property = type.Properties.FirstOrDefault(item =>
            string.Equals(item.Name, candidateName, StringComparison.OrdinalIgnoreCase) &&
            ((accessorName.StartsWith("get_", StringComparison.Ordinal) && item.HasGetter) || (accessorName.StartsWith("set_", StringComparison.Ordinal) && item.HasSetter)));

        if (property == null)
            return null;

        return property.Name;
    }

    public string? ResolveMethodName(string declaringTypeFullName, string methodName, int parameterCount)
    {
        if (!_types.TryGetValue(declaringTypeFullName, out var type))
            return null;

        var method = type.Methods.FirstOrDefault(item =>
            string.Equals(item.Name, methodName, StringComparison.Ordinal) &&
            item.Parameters.Count == parameterCount);
        if (method != null)
            return method.Name;

        method = type.Methods.FirstOrDefault(item =>
            string.Equals(item.Name, methodName, StringComparison.OrdinalIgnoreCase) &&
            item.Parameters.Count == parameterCount);
        return method?.Name;
    }

    public string? ResolveFieldStorageName(string declaringTypeFullName, string fieldName)
    {
        if (!_types.TryGetValue(declaringTypeFullName, out var type))
            return null;

        var field = type.Fields.FirstOrDefault(item => string.Equals(item.Name, fieldName, StringComparison.Ordinal));
        if (field == null)
            return null;

        return type.IsValueType ? field.Name : $"___{field.Name}";
    }

    public IReadOnlyList<string> ResolveSquaredMagnitudeComponentFields(string declaringTypeFullName)
    {
        if (!_types.TryGetValue(declaringTypeFullName, out var type) || !type.IsValueType)
            return Array.Empty<string>();

        var numericFields = new List<string>();
        foreach (var candidate in new[] { "x", "y", "z", "w" })
        {
            var field = type.Fields.FirstOrDefault(item => string.Equals(item.Name, candidate, StringComparison.Ordinal));
            if (field == null || !IsNumericFieldType(field.Type))
                break;

            numericFields.Add(field.Name);
        }

        return numericFields.Count >= 2 ? numericFields : Array.Empty<string>();
    }

    private static bool IsNumericFieldType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;

        return type is "float" or "double" or "int32_t" or "uint32_t" or "int64_t" or "uint64_t" or "int16_t" or "uint16_t" or "int8_t" or "uint8_t";
    }

    private static string? FindMetadataPath(string assemblyPath)
    {
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
            return null;

        var candidates = new List<string>();
        var current = assemblyDirectory;
        for (var depth = 0; depth < 6 && !string.IsNullOrWhiteSpace(current); depth++)
        {
            candidates.Add(Path.Combine(current, "TypeGenerator", "Output", "GeneratedTypes.metadata.json"));
            candidates.Add(Path.Combine(current, "Output", "GeneratedTypes.metadata.json"));
            current = Directory.GetParent(current)?.FullName;
        }

        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "TypeGenerator", "Output", "GeneratedTypes.metadata.json"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "Output", "GeneratedTypes.metadata.json"));

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
    }
}

internal sealed class GeneratedTypeMetadataRecord
{
    public string Namespace { get; set; } = "";
    public string TypeName { get; set; } = "";
    public bool IsValueType { get; set; }
    public List<GeneratedPropertyMetadataRecord> Properties { get; set; } = new();
    public List<GeneratedFieldMetadataRecord> Fields { get; set; } = new();
    public List<GeneratedMethodMetadataRecord> Methods { get; set; } = new();
}

internal sealed class GeneratedPropertyMetadataRecord
{
    public string Name { get; set; } = "";
    public bool HasGetter { get; set; }
    public bool HasSetter { get; set; }
    public string BackingFieldName { get; set; } = "";
}

internal sealed class GeneratedFieldMetadataRecord
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsStatic { get; set; }
}

internal sealed class GeneratedMethodMetadataRecord
{
    public string Name { get; set; } = "";
    public bool IsStatic { get; set; }
    public List<GeneratedMethodParameterMetadataRecord> Parameters { get; set; } = new();
}

internal sealed class GeneratedMethodParameterMetadataRecord
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

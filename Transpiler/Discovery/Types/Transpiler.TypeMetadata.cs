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
    private void AssignConfigIdentifiers()
    {
        foreach (var configGroup in _configValues.GroupBy(config => config.Name, StringComparer.Ordinal))
        {
            if (configGroup.Count() == 1)
                continue;

            foreach (var config in configGroup)
                config.CppIdentifier = BuildUniqueConfigIdentifier(config);
        }
    }

    private static string BuildUniqueConfigIdentifier(ConfigEntry config)
    {
        var typeToken = CppIdentifier.Sanitize(config.DeclaringTypeFullName, "Config");
        var nameToken = CppIdentifier.Sanitize(config.Name, "Value");
        return $"{typeToken}_{nameToken}";
    }

    private static string BuildUniqueLocalStaticIdentifier(string declaringTypeFullName, string fieldName)
    {
        var typeToken = CppIdentifier.Sanitize(declaringTypeFullName, "Static");
        var nameToken = CppIdentifier.Sanitize(fieldName, "Value");
        return $"{typeToken}_{nameToken}";
    }

    private static bool IsTranspilerRelevantType(TypeDefinition type)
    {
        if (type.CustomAttributes.Any(IsCustomTypeAttribute))
            return true;

        if (type.CustomAttributes.Any(IsModAttribute))
            return true;

        if (type.Methods.Any(method => method.CustomAttributes.Any(IsPatchAttribute)))
            return true;

        if (type.Fields.Any(field => field.CustomAttributes.Any(IsConfigAttribute)))
            return true;

        return type.Properties.Any(property => property.CustomAttributes.Any(IsConfigAttribute));
    }

    private bool CanEmitLocalStaticField(TypeReference type)
    {
        if (type is ByReferenceType byReferenceType)
            return CanEmitLocalStaticField(byReferenceType.ElementType);

        if (type is ArrayType arrayType)
            return CanEmitLocalStaticField(arrayType.ElementType);

        if (type is GenericInstanceType genericInstanceType)
        {
            if (!CanEmitLocalStaticField(genericInstanceType.ElementType))
                return false;

            foreach (var argument in genericInstanceType.GenericArguments)
            {
                if (!CanEmitLocalStaticField(argument))
                    return false;
            }

            return true;
        }

        return true;
    }

    private static string? TryGetAutoPropertyName(string fieldName)
    {
        const string suffix = ">k__BackingField";
        if (!fieldName.StartsWith("<", StringComparison.Ordinal) || !fieldName.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        return fieldName[1..^suffix.Length];
    }
}
